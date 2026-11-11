using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AiInterview.Application.Interfaces;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AiInterview.Infrastructure.Documents;

/// <summary>Extracts text only. Uploads are held in bounded memory and are never saved to a public or permanent path.</summary>
public sealed class CvTextExtractor : ICvTextExtractor
{
    public const int MaxUploadBytes = 5 * 1024 * 1024;
    public const int MaxTextCharacters = 50_000;
    public const int MaxPdfPages = 50;
    private const int MaxExpandedBytes = 20 * 1024 * 1024;

    public async Task<string> ExtractAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx")) throw new CvExtractionException("Chỉ chấp nhận CV định dạng PDF hoặc DOCX.");
        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(bytes.AsMemory(), ct)) > 0)
        {
            if (buffer.Length + read > MaxUploadBytes) throw new CvExtractionException("CV vượt giới hạn 5 MB. Hãy giảm kích thước tệp rồi tải lại.");
            await buffer.WriteAsync(bytes.AsMemory(0, read), ct);
        }
        if (buffer.Length < 5) throw new CvExtractionException("Tệp CV rỗng hoặc không đúng định dạng.");
        buffer.Position = 0;
        try
        {
            ct.ThrowIfCancellationRequested();
            var signature = buffer.GetBuffer();
            string text;
            if (extension == ".pdf")
            {
                if (!signature.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new CvExtractionException("Nội dung tệp không phải PDF hợp lệ.");
                text = ExtractPdf(buffer, ct);
            }
            else
            {
                if (!signature.AsSpan(0, 4).SequenceEqual(new byte[] { 0x50, 0x4b, 0x03, 0x04 }))
                    throw new CvExtractionException("Nội dung tệp không phải DOCX hợp lệ. Hãy lưu lại dưới dạng .docx.");
                ValidateDocxArchive(buffer, ct);
                buffer.Position = 0;
                text = ExtractDocx(buffer, ct);
            }
            var normalized = Normalize(text);
            if (normalized.Count(char.IsLetterOrDigit) < 30)
                throw new CvExtractionException(extension == ".pdf"
                    ? "Không tìm thấy đủ văn bản trong PDF. PDF ảnh/scan chưa được hỗ trợ; hãy dùng PDF có thể chọn và sao chép chữ hoặc DOCX."
                    : "Không tìm thấy đủ văn bản trong DOCX. Hãy kiểm tra CV có nội dung chữ, không chỉ chứa ảnh.");
            return normalized;
        }
        catch (CvExtractionException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            throw new CvExtractionException("Không đọc được CV. Tệp có thể bị hỏng, có mật khẩu hoặc không đúng định dạng PDF/DOCX. Hãy xuất lại tệp rồi thử lại.");
        }
    }

    private static string ExtractPdf(Stream stream, CancellationToken ct)
    {
        using var pdf = PdfDocument.Open(stream);
        if (pdf.NumberOfPages > MaxPdfPages) throw new CvExtractionException("CV PDF chỉ được có tối đa 50 trang.");
        var text = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            AppendBounded(text, ContentOrderTextExtractor.GetText(page));
        }
        return text.ToString();
    }

    private static void ValidateDocxArchive(Stream stream, CancellationToken ct)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 1000 || archive.GetEntry("[Content_Types].xml") is null || archive.GetEntry("word/document.xml") is null)
            throw new CvExtractionException("Cấu trúc DOCX không hợp lệ hoặc vượt giới hạn xử lý.");
        long expanded = 0;
        var scratch = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                throw new CvExtractionException("CV có macro không được hỗ trợ. Hãy lưu thành DOCX thông thường.");
            if (entry.Length > MaxExpandedBytes || expanded + entry.Length > MaxExpandedBytes
                || (entry.Length > 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 200))
                throw new CvExtractionException("DOCX có nội dung giải nén quá lớn. Hãy giảm ảnh hoặc xuất lại tệp.");
            // Check actual bytes as well as ZIP metadata; no entry is extracted onto disk.
            using var content = entry.Open();
            int count;
            long actualEntryBytes = 0;
            while ((count = content.Read(scratch, 0, scratch.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                expanded += count;
                actualEntryBytes += count;
                if (expanded > MaxExpandedBytes || actualEntryBytes > entry.Length)
                    throw new CvExtractionException("DOCX vượt giới hạn nội dung giải nén hoặc có cấu trúc không hợp lệ.");
            }
        }
    }

    private static string ExtractDocx(Stream stream, CancellationToken ct)
    {
        using var doc = WordprocessingDocument.Open(stream, false, new OpenSettings { MaxCharactersInPart = 2_000_000 });
        var mainPart = doc.MainDocumentPart;
        if (mainPart?.Document?.Body is not { } body) throw new CvExtractionException("DOCX không chứa nội dung văn bản hợp lệ.");
        var text = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            ct.ThrowIfCancellationRequested();
            AppendBounded(text, paragraph.InnerText);
        }
        foreach (var header in mainPart.HeaderParts)
            foreach (var paragraph in header.Header?.Descendants<Paragraph>() ?? [])
            {
                ct.ThrowIfCancellationRequested();
                AppendBounded(text, paragraph.InnerText);
            }
        foreach (var footer in mainPart.FooterParts)
            foreach (var paragraph in footer.Footer?.Descendants<Paragraph>() ?? [])
            {
                ct.ThrowIfCancellationRequested();
                AppendBounded(text, paragraph.InnerText);
            }
        return text.ToString();
    }

    private static void AppendBounded(StringBuilder result, string value)
    {
        if (result.Length + value.Length + 1 > MaxTextCharacters)
            throw new CvExtractionException("Văn bản CV vượt giới hạn 50.000 ký tự. Hãy rút gọn nội dung rồi tải lại.");
        result.Append(value).Append('\n');
    }

    private static string Normalize(string text)
    {
        text = text.Replace("\0", "").Replace("\r\n", "\n").Replace('\r', '\n');
        return Regex.Replace(text, @"[\t ]+", " ").Trim();
    }
}
