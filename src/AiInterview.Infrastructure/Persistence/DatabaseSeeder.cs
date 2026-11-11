using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiInterview.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, bool development)
    {
        if (!configuration.GetValue<bool>("Seed:Enabled")) return;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Student, Roles.Lecturer, Roles.Admin })
            if (!await roleManager.RoleExistsAsync(role))
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)), $"Tạo role {role}");

        await SeedCatalogAsync(db);
        if (!development) return;

        var password = configuration["Seed:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSeeder")
                .LogWarning("Chưa tạo tài khoản mẫu: đặt Seed:Password bằng user-secrets rồi khởi động lại. Danh mục và role đã được tạo.");
            return;
        }
        var accounts = new[]
        {
            (Role: Roles.Admin, Email: configuration["Seed:AdminEmail"] ?? "admin@example.test", Name: "Quản trị viên mẫu"),
            (Role: Roles.Lecturer, Email: configuration["Seed:LecturerEmail"] ?? "lecturer@example.test", Name: "Giảng viên mẫu"),
            (Role: Roles.Student, Email: configuration["Seed:StudentEmail"] ?? "student@example.test", Name: "Sinh viên mẫu")
        };
        if (accounts.Select(x => x.Email.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Length)
            throw new InvalidOperationException("Các email Seed:AdminEmail, LecturerEmail, StudentEmail phải khác nhau.");

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var account in accounts)
        {
            var email = account.Email.Trim();
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                // Demo provisioning permits local sign-in, but it does not prove ownership of the mailbox.
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = false, EmailConfirmationRequired = false, FullName = account.Name };
                EnsureSucceeded(await users.CreateAsync(user, password), "Tạo tài khoản mẫu");
                EnsureSucceeded(await users.AddToRoleAsync(user, account.Role), "Gán role tài khoản mẫu mới");
            }
            else if (!await users.IsInRoleAsync(user, account.Role))
            {
                scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSeeder")
                    .LogWarning("Bỏ qua tài khoản seed cho role {Role}: email đã thuộc tài khoản khác. Seed không tự nâng quyền tài khoản đã tồn tại.", account.Role);
                continue;
            }
            if (account.Role == Roles.Student && !await db.StudentProfiles.AnyAsync(x => x.UserId == user.Id))
                db.StudentProfiles.Add(new StudentProfile
                {
                    UserId = user.Id, FullName = account.Name, University = "Đại học mẫu",
                    Major = "Kỹ thuật phần mềm", AcademicYear = 4
                });
        }
        await db.SaveChangesAsync();
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{action} thất bại: {string.Join(", ", result.Errors.Select(x => x.Code))}. Kiểm tra cấu hình Identity và user-secrets.");
    }

    private static async Task SeedCatalogAsync(AppDbContext db)
    {
        var positions = await db.Positions.ToListAsync();
        foreach (var (name, description) in SeedQuestions.Positions)
            if (!positions.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                var position = new Position { Name = name, Description = description };
                positions.Add(position);
                db.Positions.Add(position);
            }

        const string rubricName = "Rubric IT Intern/Fresher v1";
        if (!await db.RubricVersions.AnyAsync(x => x.Name == rubricName))
        {
            var rubric = new RubricVersion { Name = rubricName, IsActive = !await db.RubricVersions.AnyAsync(x => x.IsActive) };
            rubric.Criteria =
            [
                new() { Code = "accuracy", Name = "Độ đúng và chính xác kiến thức", Weight = 30,
                    Description = "0–2: sai phần lớn hoặc không trả lời; 3–4: có ý đúng nhưng mắc lỗi cốt lõi; 5–6: nền tảng đúng, còn thiếu điều kiện áp dụng; 7–8: đúng và có ví dụ phù hợp; 9–10: chính xác, nêu được giới hạn và đánh đổi. Dẫn chứng nguyên văn từ câu trả lời; không suy diễn kiến thức chưa được nói." },
                new() { Code = "depth", Name = "Độ sâu và đầy đủ", Weight = 20,
                    Description = "0–2: không có luận điểm hữu ích; 3–4: chỉ liệt kê khái niệm; 5–6: giải thích được ý chính; 7–8: có cơ chế, ví dụ hoặc đánh đổi; 9–10: phân tích toàn diện phù hợp Intern/Fresher, bao gồm trường hợp biên. Không đòi kinh nghiệm senior ở sinh viên." },
                new() { Code = "structure", Name = "Cấu trúc và mạch lạc", Weight = 20,
                    Description = "0–2: không thể theo dõi; 3–4: rời rạc, thiếu kết nối; 5–6: có mở ý và giải thích; 7–8: trình tự rõ, ví dụ và kết luận; 9–10: súc tích, logic và làm nổi bật ý chính. Câu hành vi đánh giá STAR: tình huống, nhiệm vụ, hành động cá nhân, kết quả; không bắt buộc STAR cho câu kỹ thuật." },
                new() { Code = "relevance", Name = "Độ liên quan tới câu hỏi và vị trí", Weight = 20,
                    Description = "0–2: lạc đề; 3–4: trả lời phần nhỏ, thiếu trọng tâm; 5–6: đáp ứng yêu cầu chính; 7–8: bám sát câu hỏi và bối cảnh vị trí/CV/JD; 9–10: trả lời trực tiếp, ưu tiên đúng và chủ động làm rõ giả định. Chỉ sử dụng CV/JD đã được cung cấp." },
                new() { Code = "language", Name = "Ngôn ngữ và cách diễn đạt", Weight = 10,
                    Description = "0–2: nội dung khó hiểu; 3–4: diễn đạt mơ hồ; 5–6: hiểu được dù còn lỗi; 7–8: dùng thuật ngữ chính xác, câu rõ ràng; 9–10: tự nhiên, chính xác và phù hợp phỏng vấn. Với tiếng Anh xét ngữ pháp, từ vựng; không phạt giọng địa phương. Không tự đoán tốc độ nói, khoảng dừng hay ngữ điệu từ transcript." }
            ];
            db.RubricVersions.Add(rubric);
        }
        await db.SaveChangesAsync();

        var skillNames = SeedQuestions.Items.SelectMany(x => x.Skills).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var skills = await db.SkillTags.ToListAsync();
        foreach (var name in skillNames)
            if (!skills.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                var skill = new SkillTag { Name = name };
                skills.Add(skill);
                db.SkillTags.Add(skill);
            }
        await db.SaveChangesAsync();

        var existing = await db.Questions.Include(x => x.Tags).ToListAsync();
        foreach (var item in SeedQuestions.Items)
        {
            var position = positions.Single(x => x.Name.Equals(SeedQuestions.Positions[item.PositionIndex].Name, StringComparison.OrdinalIgnoreCase));
            var question = existing.SingleOrDefault(x => x.PositionId == position.Id && x.Content == item.Content);
            if (question is null)
            {
                question = new QuestionBank
                {
                    PositionId = position.Id, Content = item.Content, Level = item.Level, Language = item.Language,
                    Kind = item.Kind, CompanyType = item.CompanyType
                };
                db.Questions.Add(question);
                existing.Add(question);
                foreach (var skillName in item.Skills)
                    question.Tags.Add(new QuestionSkillTag { SkillTagId = skills.Single(x => x.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase)).Id });
            }
            // Existing questions/tags are intentionally preserved so an administrator's edits
            // are not overwritten when the application starts again.
        }
        await db.SaveChangesAsync();
    }
}
