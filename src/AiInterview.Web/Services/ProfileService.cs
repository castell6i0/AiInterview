using AiInterview.Application.DTOs;
using AiInterview.Application.Interfaces;
using AiInterview.Application.Services;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiInterview.Web.Services;

public sealed class ProfileService(AppDbContext db, UserManager<ApplicationUser> users, ICvTextExtractor extractor, IAiService ai,
    IOptions<StorageLimitsOptions>? storageOptions = null, UserStorageLimiter? storageLimiter = null)
{
    private readonly StorageLimitsOptions limits = storageOptions?.Value ?? new();
    private readonly UserStorageLimiter storage = storageLimiter ?? UserStorageLimiter.Shared;
    public async Task<ProfileIndexModel> IndexAsync(string userId, CancellationToken ct)
    {
        var profile = await db.StudentProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var user = await users.FindByIdAsync(userId) ?? throw new KeyNotFoundException("Không tìm thấy tài khoản.");
        return new ProfileIndexModel
        {
            Profile = new EditProfileModel { FullName = profile?.FullName ?? user.FullName, University = profile?.University ?? "", Major = profile?.Major ?? "", AcademicYear = profile?.AcademicYear ?? 4 },
            Cvs = await db.CvProfiles.AsNoTracking().Where(x => x.StudentId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct),
            Jds = await db.JobDescriptions.AsNoTracking().Where(x => x.StudentId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)
        };
    }

    public async Task SaveProfileAsync(string userId, EditProfileModel model, CancellationToken ct)
    {
        var name = model.FullName.Trim();
        if (name.Length is < 1 or > 160 || model.AcademicYear is < 1 or > 8) throw new InvalidOperationException("Thông tin hồ sơ không hợp lệ.");
        var user = await users.FindByIdAsync(userId) ?? throw new KeyNotFoundException("Không tìm thấy tài khoản.");
        var profile = await db.StudentProfiles.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (profile is null) { profile = new StudentProfile { UserId = userId }; db.StudentProfiles.Add(profile); }
        user.FullName = profile.FullName = name;
        profile.University = model.University?.Trim() ?? "";
        profile.Major = model.Major?.Trim() ?? "";
        profile.AcademicYear = model.AcademicYear;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> UploadAsync(string userId, IFormFile? upload, CancellationToken ct)
    {
        if (upload is null || upload.Length == 0) throw new CvExtractionException("Chọn tệp CV PDF hoặc DOCX.");
        if (upload.Length > 5 * 1024 * 1024) throw new CvExtractionException("CV không được vượt quá 5 MB.");
        using var lease = await storage.AcquireAsync(userId, ct);
        if (await db.CvProfiles.CountAsync(x => x.StudentId == userId, ct) >= limits.MaxCvsPerUser)
            throw new CvExtractionException($"Bạn đã lưu tối đa {limits.MaxCvsPerUser} CV. Hãy xóa CV chưa dùng hoặc liên hệ quản trị viên.");
        var fileName = Path.GetFileName(upload.FileName.Replace('\\', '/'));
        if (fileName.Length > 255) throw new CvExtractionException("Tên tệp quá dài; hãy đổi tên CV ngắn hơn.");
        await using var stream = upload.OpenReadStream();
        var text = await extractor.ExtractAsync(stream, fileName, ct);
        // Keep only extracted text. No original upload is written to disk or sent to AI here.
        var cv = new CvProfile { StudentId = userId, FileName = fileName, ExtractedText = text };
        db.CvProfiles.Add(cv);
        await db.SaveChangesAsync(ct);
        return cv.Id;
    }

    public async Task<CvDetailModel> CvAsync(string userId, int id, CancellationToken ct)
    {
        var cv = await OwnedCv(userId, id, ct);
        return new CvDetailModel { Cv = cv, Analysis = AiPrompts.Read<CvAnalysis>(cv.AnalysisJson), AiConfigured = ai.IsConfigured,
            CanDelete = !await db.Sessions.AnyAsync(x => x.CvProfileId == id, ct) && !await db.JobDescriptions.AnyAsync(x => x.ComparedCvId == id, ct) };
    }

    public async Task AnalyzeCvAsync(string userId, int id, bool consent, string? consentedProvider, CancellationToken ct)
    {
        if (!consent) throw new InvalidOperationException("Cần đồng ý gửi nội dung CV tới nhà cung cấp AI đã chọn trước khi phân tích.");
        AiProviderConsent.RequireCurrent(consentedProvider, ai.Provider);
        var cv = await OwnedCv(userId, id, ct);
        var result = await ai.GenerateAsync(new AiRequest(userId, null, "cv-analysis", AiPrompts.Cv,
                new { cv = cv.ExtractedText }, AiPrompts.Schema<CvAnalysis>()),
            (CvAnalysis value) => string.IsNullOrWhiteSpace(value.Summary) || value.Summary.Length > 6000 ||
                !ValidItems(value.Skills, 100, 300) || !ValidItems(value.Projects, 60, 6000) || !ValidItems(value.Experience, 60, 6000)
                ? "Invalid CV analysis fields" : null, cancellationToken: ct);
        var changed = await db.CvProfiles.Where(x => x.Id == id && x.StudentId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.AnalysisJson, AiPrompts.Json(result)), ct);
        if (changed == 0) throw new KeyNotFoundException("CV đã bị xóa trong khi phân tích.");
    }

    public async Task DeleteCvAsync(string userId, int id, CancellationToken ct)
    {
        var cv = await OwnedCv(userId, id, ct);
        if (await db.Sessions.AnyAsync(x => x.CvProfileId == id, ct) || await db.JobDescriptions.AnyAsync(x => x.ComparedCvId == id, ct))
            throw new InvalidOperationException("CV này đã được dùng trong một buổi hoặc so sánh JD. Giữ phiên bản để bảo toàn lịch sử; bạn có thể tải CV mới.");
        db.CvProfiles.Remove(cv);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new InvalidOperationException("CV vừa được liên kết với dữ liệu khác và chưa thể xóa. Hãy tải lại trang."); }
    }

    public async Task<int> CreateJdAsync(string userId, CreateJdModel input, CancellationToken ct)
    {
        var title = input.Title.Trim(); var content = input.Content.Trim();
        if (title.Length is < 1 or > 200 || content.Length is < 30 or > 30000) throw new InvalidOperationException("Tiêu đề hoặc nội dung JD không hợp lệ.");
        string? url = string.IsNullOrWhiteSpace(input.SourceUrl) ? null : input.SourceUrl.Trim();
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new InvalidOperationException("Link tin tuyển dụng phải dùng http:// hoặc https://.");
        using var lease = await storage.AcquireAsync(userId, ct);
        if (await db.JobDescriptions.CountAsync(x => x.StudentId == userId, ct) >= limits.MaxJdsPerUser)
            throw new InvalidOperationException($"Bạn đã lưu tối đa {limits.MaxJdsPerUser} JD. Hãy xóa JD chưa dùng hoặc liên hệ quản trị viên.");
        var jd = new JobDescription { StudentId = userId, Title = title, Company = input.Company?.Trim() ?? "", Content = content, SourceUrl = url };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync(ct);
        return jd.Id;
    }

    public async Task<JdDetailModel> JdAsync(string userId, int id, CancellationToken ct)
    {
        var jd = await OwnedJd(userId, id, ct);
        return new JdDetailModel { Jd = jd, Gap = AiPrompts.Read<SkillGapAnalysis>(jd.SkillGapJson), AiConfigured = ai.IsConfigured,
            Cvs = await db.CvProfiles.AsNoTracking().Where(x => x.StudentId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct),
            CanDelete = !await db.Sessions.AnyAsync(x => x.JobDescriptionId == id, ct) };
    }

    public async Task CompareGapAsync(string userId, CompareGapModel input, CancellationToken ct)
    {
        if (!input.Consent) throw new InvalidOperationException("Cần đồng ý gửi CV và JD đã chọn tới nhà cung cấp AI đã chọn.");
        AiProviderConsent.RequireCurrent(input.ConsentedProvider, ai.Provider);
        var jd = await OwnedJd(userId, input.JdId, ct);
        var cv = await OwnedCv(userId, input.CvId, ct);
        var result = await ai.GenerateAsync(new AiRequest(userId, null, "skill-gap", AiPrompts.Gap,
            new { cv = cv.ExtractedText, jd = jd.Content }, AiPrompts.Schema<SkillGapAnalysis>()),
            (SkillGapAnalysis value) => !ValidItems(value.MatchedSkills, 100, 2000) || !ValidItems(value.MissingEvidence, 100, 2000) || !ValidItems(value.Recommendations, 100, 3000)
                ? "Invalid skill-gap analysis fields" : null, cancellationToken: ct);
        var changed = await db.JobDescriptions.Where(x => x.Id == input.JdId && x.StudentId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(j => j.SkillGapJson, AiPrompts.Json(result)).SetProperty(j => j.ComparedCvId, input.CvId), ct);
        if (changed == 0) throw new KeyNotFoundException("JD đã bị xóa trong khi so sánh.");
    }

    public async Task DeleteJdAsync(string userId, int id, CancellationToken ct)
    {
        var jd = await OwnedJd(userId, id, ct);
        if (await db.Sessions.AnyAsync(x => x.JobDescriptionId == id, ct))
            throw new InvalidOperationException("JD đã được dùng trong lịch sử phỏng vấn. Giữ nguyên phiên bản này và tạo JD mới khi cần cập nhật.");
        db.JobDescriptions.Remove(jd);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new InvalidOperationException("JD vừa được liên kết với buổi phỏng vấn và chưa thể xóa. Hãy tải lại trang."); }
    }

    private async Task<CvProfile> OwnedCv(string userId, int id, CancellationToken ct) => await db.CvProfiles.SingleOrDefaultAsync(x => x.Id == id && x.StudentId == userId, ct)
        ?? throw new KeyNotFoundException("Không tìm thấy CV của bạn.");
    private async Task<JobDescription> OwnedJd(string userId, int id, CancellationToken ct) => await db.JobDescriptions.SingleOrDefaultAsync(x => x.Id == id && x.StudentId == userId, ct)
        ?? throw new KeyNotFoundException("Không tìm thấy JD của bạn.");
    private static bool ValidItems(string[]? items, int maxCount, int maxLength) => items is not null && items.Length <= maxCount && items.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= maxLength);
}
