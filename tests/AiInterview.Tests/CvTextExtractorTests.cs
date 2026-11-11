using System.IO.Compression;
using System.Text;
using AiInterview.Application.Interfaces;
using AiInterview.Infrastructure.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PageSize = UglyToad.PdfPig.Content.PageSize;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AiInterview.Tests;

public class CvTextExtractorTests
{
    private readonly CvTextExtractor extractor = new();
    private const string Sample = "Student developer with C# ASP.NET Core MVC and SQL Server project experience.";

    [Fact]
    public async Task ReadsRealDocxWithoutSavingUpload()
    {
        using var buffer = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document, true))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body(new Paragraph(new Run(new Text(Sample)))));
        }
        buffer.Position = 0;
        var text = await extractor.ExtractAsync(buffer, "cv.DOCX");
        Assert.Contains(Sample, text);
        Assert.True(buffer.CanRead);
    }

    [Fact]
    public async Task ReadsRealSelectablePdfText()
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddText(Sample, 10, new PdfPoint(30, 700), builder.AddStandard14Font(Standard14Font.Helvetica));
        using var buffer = new MemoryStream(builder.Build());
        Assert.Contains("SQL Server", await extractor.ExtractAsync(buffer, "cv.pdf"));
    }

    [Fact]
    public async Task PdfWithoutTextExplainsOcrLimitation()
    {
        using var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        using var buffer = new MemoryStream(builder.Build());
        var error = await Assert.ThrowsAsync<CvExtractionException>(() => extractor.ExtractAsync(buffer, "scan.pdf"));
        Assert.Contains("ảnh/scan", error.Message);
    }

    [Theory]
    [InlineData("cv.docx", "%PDF-not-a-docx")]
    [InlineData("cv.pdf", "PK\u0003\u0004invalid")]
    [InlineData("cv.exe", "unknown content")]
    public async Task DoesNotTrustFileExtension(string fileName, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await Assert.ThrowsAsync<CvExtractionException>(() => extractor.ExtractAsync(stream, fileName));
    }

    [Fact]
    public async Task EnforcesUploadLimitBeforeParsing()
    {
        using var stream = new MemoryStream(new byte[CvTextExtractor.MaxUploadBytes + 1]);
        var error = await Assert.ThrowsAsync<CvExtractionException>(() => extractor.ExtractAsync(stream, "huge.pdf"));
        Assert.Contains("5 MB", error.Message);
    }

    [Fact]
    public async Task RejectsCompressedBombBeforeOpeningDocument()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            archive.CreateEntry("[Content_Types].xml");
            var body = archive.CreateEntry("word/document.xml", CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(body.Open());
            writer.Write(new string('x', 2 * 1024 * 1024));
        }
        stream.Position = 0;
        var error = await Assert.ThrowsAsync<CvExtractionException>(() => extractor.ExtractAsync(stream, "bomb.docx"));
        Assert.Contains("giải nén", error.Message);
    }

    [Fact]
    public async Task RejectsPdfWithTooManyPages()
    {
        using var builder = new PdfDocumentBuilder();
        for (var i = 0; i <= CvTextExtractor.MaxPdfPages; i++) builder.AddPage(PageSize.A4);
        using var stream = new MemoryStream(builder.Build());
        var error = await Assert.ThrowsAsync<CvExtractionException>(() => extractor.ExtractAsync(stream, "pages.pdf"));
        Assert.Contains("50 trang", error.Message);
    }
}
