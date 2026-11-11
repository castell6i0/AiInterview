using System.Data;
using System.ComponentModel.DataAnnotations;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Identity;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AiInterview.Web.Services;

public sealed class AdminService(AppDbContext db, UserManager<ApplicationUser> users)
{
    private static readonly string[] CriterionCodes = ["accuracy", "depth", "structure", "relevance", "language"];

    public async Task<AdminIndexVm> IndexAsync() => new()
    {
        UserCount = await db.Users.CountAsync(), SessionCount = await db.Sessions.CountAsync(), QuestionCount = await db.Questions.CountAsync(),
        AiRequestCount = await db.AiRequestLogs.CountAsync(),
        ReportedInputTokens = await db.AiRequestLogs.SumAsync(l => (long?)l.InputTokens) ?? 0,
        ReportedOutputTokens = await db.AiRequestLogs.SumAsync(l => (long?)l.OutputTokens) ?? 0,
        KnownEstimatedCostUsd = await db.AiRequestLogs.SumAsync(l => l.EstimatedCostUsd) ?? 0,
        UnknownCostCount = await db.AiRequestLogs.CountAsync(l => l.EstimatedCostUsd == null)
    };

    public async Task<AdminUsersVm> UsersAsync(string? search)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        var term = search?.Trim() ?? "";
        if (term.Length > 0) query = query.Where(u => u.FullName.Contains(term) || (u.Email != null && u.Email.Contains(term)));
        var found = await query.OrderBy(u => u.Email).Take(101).ToListAsync();
        var result = new AdminUsersVm { Search = term, HasMore = found.Count > 100 };
        foreach (var user in found.Take(100)) result.Users.Add(new AdminUserVm
        {
            Id = user.Id, FullName = user.FullName, Email = user.Email ?? "", Roles = (await users.GetRolesAsync(user)).ToList(),
            Locked = user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow
        });
        return result;
    }

    public async Task UpdateUserAsync(UserAccessVm model, string actorId)
    {
        if (model.Role is not (Roles.Admin or Roles.Lecturer or Roles.Student)) throw new InvalidOperationException("Vai trò không hợp lệ.");
        if (model.UserId == actorId && (model.Locked || model.Role != Roles.Admin))
            throw new InvalidOperationException("Bạn không thể tự khóa hoặc tự gỡ quyền quản trị của mình.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await users.FindByIdAsync(model.UserId) ?? throw new InvalidOperationException("Người dùng không tồn tại.");
        var roles = await users.GetRolesAsync(user);
        if (roles.Contains(Roles.Admin) && (model.Role != Roles.Admin || model.Locked))
        {
            var admins = await users.GetUsersInRoleAsync(Roles.Admin);
            if (!admins.Any(u => u.Id != user.Id && (!u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow)))
                throw new InvalidOperationException("Phải giữ ít nhất một quản trị viên đang hoạt động.");
        }
        var remove = roles.Where(r => r != model.Role).ToArray();
        if (remove.Length > 0) Check(await users.RemoveFromRolesAsync(user, remove));
        if (!roles.Contains(model.Role)) Check(await users.AddToRoleAsync(user, model.Role));
        Check(await users.SetLockoutEnabledAsync(user, true));
        Check(await users.SetLockoutEndDateAsync(user, model.Locked ? DateTimeOffset.UtcNow.AddYears(100) : null));
        if (!model.Locked) Check(await users.ResetAccessFailedCountAsync(user));
        Check(await users.UpdateSecurityStampAsync(user));
        await transaction.CommitAsync();
    }

    public async Task<string> CreateUserAsync(CreateUserVm model)
    {
        ValidateUserDetails(model.FullName, model.Email);
        if (model.Role is not (Roles.Admin or Roles.Lecturer or Roles.Student))
            throw new InvalidOperationException("Vai trò không hợp lệ.");
        if (model.Password is null || model.Password.Length is < 10 or > 100 || model.Password != model.ConfirmPassword)
            throw new InvalidOperationException("Mật khẩu cần từ 10 đến 100 ký tự và phần xác nhận phải khớp.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var email = model.Email.Trim();
        var user = new ApplicationUser { FullName = model.FullName.Trim(), Email = email, UserName = email };
        Check(await users.CreateAsync(user, model.Password));
        Check(await users.AddToRoleAsync(user, model.Role));
        if (model.Role == Roles.Student)
            db.StudentProfiles.Add(new StudentProfile { UserId = user.Id, FullName = user.FullName });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return user.Id;
    }

    public async Task<EditUserVm?> UserAsync(string id)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
        return user is null ? null : new EditUserVm { UserId = user.Id, FullName = user.FullName, Email = user.Email ?? "" };
    }

    public async Task EditUserAsync(EditUserVm model)
    {
        ValidateUserDetails(model.FullName, model.Email);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await users.FindByIdAsync(model.UserId) ?? throw new InvalidOperationException("Người dùng không tồn tại.");
        var email = model.Email.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)) user.EmailConfirmed = false;
        user.FullName = model.FullName.Trim();
        user.Email = email;
        user.UserName = email;
        // Identity validates both identifiers, normalizes them and checks uniqueness before saving.
        Check(await users.UpdateAsync(user));
        var profile = await db.StudentProfiles.SingleOrDefaultAsync(p => p.UserId == user.Id);
        if (profile is not null) profile.FullName = user.FullName;
        await db.SaveChangesAsync();
        Check(await users.UpdateSecurityStampAsync(user));
        await transaction.CommitAsync();
    }

    public async Task<DeleteUserVm?> DeletionAsync(string id, string actorId)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return null;
        var refusal = await DeletionRefusalAsync(user, actorId);
        return new DeleteUserVm { UserId = user.Id, FullName = user.FullName, Email = user.Email ?? "", CanDelete = refusal is null, RefusalReason = refusal ?? "" };
    }

    public async Task DeleteUserAsync(string id, string actorId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await users.FindByIdAsync(id) ?? throw new InvalidOperationException("Người dùng không tồn tại.");
        var refusal = await DeletionRefusalAsync(user, actorId);
        if (refusal is not null) throw new InvalidOperationException(refusal);
        // A basic student profile is account metadata. All learning/teaching history blocks deletion.
        var profile = await db.StudentProfiles.SingleOrDefaultAsync(p => p.UserId == id);
        if (profile is not null) db.StudentProfiles.Remove(profile);
        try
        {
            await db.SaveChangesAsync();
            Check(await users.DeleteAsync(user));
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("Tài khoản đang được liên kết với dữ liệu khác nên chưa thể xóa. Hãy khóa tài khoản để giữ lịch sử.");
        }
    }

    private async Task<string?> DeletionRefusalAsync(ApplicationUser user, string actorId)
    {
        if (user.Id == actorId) return "Bạn không thể tự xóa tài khoản đang sử dụng.";
        if (await users.IsInRoleAsync(user, Roles.Admin))
        {
            var admins = await users.GetUsersInRoleAsync(Roles.Admin);
            if (!admins.Any(u => u.Id != user.Id && (!u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow)))
                return "Phải giữ ít nhất một quản trị viên đang hoạt động.";
        }
        var id = user.Id;
        if (await db.CvProfiles.AnyAsync(x => x.StudentId == id) || await db.JobDescriptions.AnyAsync(x => x.StudentId == id)
            || await db.Sessions.AnyAsync(x => x.StudentId == id) || await db.AiRequestLogs.AnyAsync(x => x.UserId == id)
            || await db.Classes.AnyAsync(x => x.LecturerId == id) || await db.ClassMembers.AnyAsync(x => x.StudentId == id)
            || await db.AssignmentSubmissions.AnyAsync(x => x.StudentId == id) || await db.LecturerComments.AnyAsync(x => x.LecturerId == id))
            return "Tài khoản đã có CV, JD, lịch sử luyện tập hoặc dữ liệu lớp học nên không thể xóa. Hãy khóa tài khoản để bảo toàn lịch sử.";
        return null;
    }

    private static void ValidateUserDetails(string? fullName, string? email)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 160)
            throw new InvalidOperationException("Họ tên cần từ 1 đến 160 ký tự.");
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 256 || !new EmailAddressAttribute().IsValid(email.Trim()))
            throw new InvalidOperationException("Email chưa hợp lệ.");
    }

    private static void Check(IdentityResult result)
    {
        if (result.Succeeded) return;
        var errors = result.Errors.Select(e => e.Code).ToArray();
        if (errors.Any(c => c is "DuplicateEmail" or "DuplicateUserName"))
            throw new InvalidOperationException("Email này đã được dùng cho tài khoản khác.");
        if (errors.Any(c => c.StartsWith("Password", StringComparison.Ordinal)))
            throw new InvalidOperationException("Mật khẩu cần ít nhất 10 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.");
        if (errors.Any(c => c is "InvalidEmail" or "InvalidUserName"))
            throw new InvalidOperationException("Email chưa hợp lệ. Hãy kiểm tra lại địa chỉ.");
        throw new InvalidOperationException("Không cập nhật được người dùng. Hãy tải lại trang và thử lại.");
    }

    public Task<List<Position>> PositionsAsync() => db.Positions.AsNoTracking().OrderBy(p => p.Name).ToListAsync();

    public async Task<PositionEditVm?> PositionAsync(int id)
    {
        if (id == 0) return new PositionEditVm();
        var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
        return position is null ? null : new PositionEditVm { Id = position.Id, Name = position.Name, Description = position.Description, IsActive = position.IsActive };
    }

    public async Task SavePositionAsync(PositionEditVm model)
    {
        var name = model.Name.Trim();
        if (await db.Positions.AnyAsync(p => p.Id != model.Id && p.Name == name)) throw new InvalidOperationException("Tên vị trí đã tồn tại.");
        var position = model.Id == 0 ? new Position() : await db.Positions.SingleOrDefaultAsync(p => p.Id == model.Id)
            ?? throw new InvalidOperationException("Không tìm thấy vị trí.");
        position.Name = name; position.Description = (model.Description ?? "").Trim(); position.IsActive = model.IsActive;
        if (model.Id == 0) db.Positions.Add(position);
        await db.SaveChangesAsync();
    }

    public async Task<string> DeletePositionAsync(int id)
    {
        var position = await db.Positions.FindAsync(id) ?? throw new InvalidOperationException("Không tìm thấy vị trí.");
        var referenced = await db.Sessions.AnyAsync(s => s.PositionId == id) || await db.Assignments.AnyAsync(a => a.PositionId == id)
            || await db.Questions.AnyAsync(q => q.PositionId == id);
        if (referenced) position.IsActive = false; else db.Positions.Remove(position);
        await db.SaveChangesAsync();
        return referenced ? "Vị trí đang được sử dụng nên đã chuyển sang ngừng hoạt động để giữ dữ liệu lịch sử." : "Đã xóa vị trí chưa được sử dụng.";
    }

    public async Task<AdminQuestionsVm> QuestionsAsync(string? search, int? positionId, int page)
    {
        var term = search?.Trim() ?? "";
        var query = db.Questions.AsNoTracking().AsQueryable();
        if (term.Length > 0) query = query.Where(q => q.Content.Contains(term) || q.CompanyType.Contains(term) || q.Tags.Any(t => t.Skill.Name.Contains(term)));
        if (positionId is > 0) query = query.Where(q => q.PositionId == positionId);
        var count = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(count / 25.0));
        page = Math.Clamp(page, 1, pages);
        return new AdminQuestionsVm
        {
            Search = term, PositionId = positionId, Page = page, TotalPages = pages,
            Positions = await PositionsAsync(),
            Questions = await query.Include(q => q.Position).Include(q => q.Tags).ThenInclude(t => t.Skill)
                .OrderByDescending(q => q.Id).Skip((page - 1) * 25).Take(25).ToListAsync()
        };
    }

    public async Task<QuestionEditVm?> QuestionAsync(int id)
    {
        var model = new QuestionEditVm();
        if (id != 0)
        {
            var question = await db.Questions.AsNoTracking().Include(q => q.Tags).ThenInclude(t => t.Skill).SingleOrDefaultAsync(q => q.Id == id);
            if (question is null) return null;
            model = new QuestionEditVm
            {
                Id = question.Id, PositionId = question.PositionId, Content = question.Content, Level = question.Level,
                Language = question.Language, Kind = question.Kind, CompanyType = question.CompanyType, IsActive = question.IsActive,
                SkillTags = string.Join(", ", question.Tags.Select(t => t.Skill.Name))
            };
        }
        model.Positions = await PositionsAsync();
        return model;
    }

    public async Task SaveQuestionAsync(QuestionEditVm model)
    {
        if (!await db.Positions.AnyAsync(p => p.Id == model.PositionId)) throw new InvalidOperationException("Vị trí không tồn tại.");
        var tags = (model.SkillTags ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tags.Length > 15 || tags.Any(t => t.Length > 80)) throw new InvalidOperationException("Tối đa 15 chủ đề, mỗi chủ đề tối đa 80 ký tự.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var question = model.Id == 0 ? new QuestionBank() : await db.Questions.Include(q => q.Tags).SingleOrDefaultAsync(q => q.Id == model.Id)
            ?? throw new InvalidOperationException("Không tìm thấy câu hỏi.");
        question.PositionId = model.PositionId; question.Content = model.Content.Trim(); question.Level = model.Level;
        question.Language = model.Language; question.Kind = model.Kind; question.CompanyType = model.CompanyType.Trim(); question.IsActive = model.IsActive;
        if (model.Id == 0) db.Questions.Add(question);
        else { db.QuestionSkillTags.RemoveRange(question.Tags); question.Tags.Clear(); await db.SaveChangesAsync(); }
        foreach (var name in tags)
        {
            var skill = await db.SkillTags.SingleOrDefaultAsync(t => t.Name == name);
            if (skill is null) { skill = new SkillTag { Name = name }; db.SkillTags.Add(skill); }
            question.Tags.Add(new QuestionSkillTag { Question = question, Skill = skill });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task DeleteQuestionAsync(int id)
    {
        var question = await db.Questions.Include(q => q.Tags).SingleOrDefaultAsync(q => q.Id == id)
            ?? throw new InvalidOperationException("Không tìm thấy câu hỏi.");
        db.QuestionSkillTags.RemoveRange(question.Tags);
        db.Questions.Remove(question);
        await db.SaveChangesAsync();
    }

    public async Task<RubricEditVm> RubricAsync()
    {
        var active = await db.RubricVersions.AsNoTracking().Include(r => r.Criteria).SingleOrDefaultAsync(r => r.IsActive);
        return new RubricEditVm
        {
            Name = $"Rubric {DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)):yyyy-MM-dd HH:mm:ss}",
            Criteria = active?.Criteria.OrderBy(c => Array.IndexOf(CriterionCodes, c.Code)).Select(c => new RubricCriterionEditVm
            { Code = c.Code, Name = c.Name, Description = c.Description, Weight = c.Weight }).ToList() ?? DefaultCriteria(),
            History = await db.RubricVersions.AsNoTracking().Include(r => r.Criteria).OrderByDescending(r => r.CreatedAt).ToListAsync()
        };
    }

    public async Task SaveRubricAsync(RubricEditVm model)
    {
        if (model.Criteria.Count != 5 || !model.Criteria.Select(c => c.Code).Order().SequenceEqual(CriterionCodes.Order())
            || model.Criteria.Any(c => c.Weight <= 0 || c.Weight > 100 || string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Description)))
            throw new InvalidOperationException("Rubric phải có đúng 5 tiêu chí chuẩn và mỗi trọng số lớn hơn 0, không quá 100.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (await db.RubricVersions.AnyAsync(r => r.Name == model.Name.Trim()))
            throw new InvalidOperationException("Tên phiên bản rubric đã tồn tại. Hãy dùng tên mới để dễ phân biệt các phiên bản.");
        var previous = await db.RubricVersions.Where(r => r.IsActive).ToListAsync();
        foreach (var rubric in previous) rubric.IsActive = false;
        await db.SaveChangesAsync();
        db.RubricVersions.Add(new RubricVersion
        {
            Name = model.Name.Trim(), IsActive = true,
            Criteria = model.Criteria.Select(c => new RubricCriterion { Code = c.Code, Name = c.Name.Trim(), Description = c.Description.Trim(), Weight = c.Weight }).ToList()
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static List<RubricCriterionEditVm> DefaultCriteria() =>
    [
        new() { Code = "accuracy", Name = "Đúng kiến thức", Description = "0–2: sai nền tảng; 3–4: nhiều lỗi; 5–6: cơ bản đúng; 7–8: chính xác, có ví dụ; 9–10: chính xác và nhận diện giới hạn.", Weight = 30 },
        new() { Code = "depth", Name = "Độ sâu và đầy đủ", Description = "0–2: không giải thích; 3–4: thiếu ý chính; 5–6: đủ ý cơ bản; 7–8: phân tích lý do và ví dụ; 9–10: nêu đánh đổi và tình huống biên phù hợp Intern/Fresher.", Weight = 25 },
        new() { Code = "structure", Name = "Cấu trúc và mạch lạc", Description = "0–2: rời rạc; 3–4: khó theo dõi; 5–6: có bố cục; 7–8: logic rõ, STAR với hành vi; 9–10: súc tích, dẫn chứng và kết quả rõ.", Weight = 20 },
        new() { Code = "relevance", Name = "Liên quan tới câu hỏi và vị trí", Description = "0–2: lạc đề; 3–4: liên quan ít; 5–6: trả lời trọng tâm; 7–8: gắn yêu cầu vị trí; 9–10: tập trung đúng vấn đề với ví dụ thực tế.", Weight = 15 },
        new() { Code = "language", Name = "Ngôn ngữ và cách diễn đạt", Description = "0–2: khó hiểu; 3–4: nhiều lỗi; 5–6: hiểu được; 7–8: rõ ràng và dùng thuật ngữ đúng; 9–10: tự nhiên, chính xác. Tiếng Anh xét ngữ pháp và từ vựng; không suy diễn tốc độ nói.", Weight = 10 }
    ];
}
