using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AiInterview.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<UserAiSettings> UserAiSettings => Set<UserAiSettings>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<CvProfile> CvProfiles => Set<CvProfile>();
    public DbSet<JobDescription> JobDescriptions => Set<JobDescription>();
    public DbSet<RubricVersion> RubricVersions => Set<RubricVersion>();
    public DbSet<RubricCriterion> RubricCriteria => Set<RubricCriterion>();
    public DbSet<InterviewSession> Sessions => Set<InterviewSession>();
    public DbSet<InterviewTurn> Turns => Set<InterviewTurn>();
    public DbSet<TurnEvaluation> TurnEvaluations => Set<TurnEvaluation>();
    public DbSet<TurnScore> TurnScores => Set<TurnScore>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<DeliveryMetric> DeliveryMetrics => Set<DeliveryMetric>();
    public DbSet<AiRequestLog> AiRequestLogs => Set<AiRequestLog>();
    public DbSet<SkillTag> SkillTags => Set<SkillTag>();
    public DbSet<QuestionBank> Questions => Set<QuestionBank>();
    public DbSet<QuestionSkillTag> QuestionSkillTags => Set<QuestionSkillTag>();
    public DbSet<Classroom> Classes => Set<Classroom>();
    public DbSet<ClassMember> ClassMembers => Set<ClassMember>();
    public DbSet<ClassBan> ClassBans => Set<ClassBan>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
    public DbSet<LecturerComment> LecturerComments => Set<LecturerComment>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<ApplicationUser>().Property(x => x.FullName).HasMaxLength(160);
        model.Entity<UserAiSettings>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserAiSettings>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Provider).HasMaxLength(20);
            e.Property(x => x.GeminiModel).HasMaxLength(100);
            e.Property(x => x.OpenAiModel).HasMaxLength(100);
            e.Property(x => x.ProtectedGeminiApiKey).HasMaxLength(4096);
            e.Property(x => x.ProtectedOpenAiApiKey).HasMaxLength(4096);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.ToTable(t => t.HasCheckConstraint("CK_UserAiSettings_Provider", "[Provider] IN ('Gemini', 'OpenAI')"));
        });
        model.Entity<StudentProfile>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasOne<ApplicationUser>().WithOne().HasForeignKey<StudentProfile>(x => x.UserId);
            e.Property(x => x.FullName).HasMaxLength(160);
            e.Property(x => x.University).HasMaxLength(250);
            e.Property(x => x.Major).HasMaxLength(160);
        });
        model.Entity<Position>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(1500);
            e.HasIndex(x => x.Name).IsUnique();
        });
        model.Entity<CvProfile>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentId);
            e.Property(x => x.FileName).HasMaxLength(255);
            e.HasIndex(x => new { x.StudentId, x.CreatedAt });
        });
        model.Entity<JobDescription>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentId);
            e.HasOne<CvProfile>().WithMany().HasForeignKey(x => x.ComparedCvId);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Company).HasMaxLength(160);
            e.Property(x => x.SourceUrl).HasMaxLength(2048);
            e.HasIndex(x => new { x.StudentId, x.CreatedAt });
        });
        model.Entity<RubricVersion>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(160);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.IsActive).IsUnique().HasFilter("[IsActive] = 1");
            e.HasMany(x => x.Criteria).WithOne().HasForeignKey(x => x.RubricVersionId);
        });
        model.Entity<RubricCriterion>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Description).HasMaxLength(3000);
            e.Property(x => x.Weight).HasPrecision(8, 3);
            e.HasIndex(x => new { x.RubricVersionId, x.Code }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_RubricCriterion_Weight", "[Weight] >= 0"));
        });
        model.Entity<InterviewSession>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentId);
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId);
            e.HasOne(x => x.RubricVersion).WithMany().HasForeignKey(x => x.RubricVersionId);
            e.HasOne<CvProfile>().WithMany().HasForeignKey(x => x.CvProfileId);
            e.HasOne<JobDescription>().WithMany().HasForeignKey(x => x.JobDescriptionId);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.FocusSkills).HasMaxLength(1000);
            e.Property(x => x.AiProvider).HasMaxLength(20).HasDefaultValue("Gemini");
            e.HasIndex(x => new { x.StudentId, x.CreatedAt });
            e.HasIndex(x => new { x.StudentId, x.Status });
            e.HasMany(x => x.Turns).WithOne(x => x.Session).HasForeignKey(x => x.SessionId);
            e.ToTable(t => t.HasCheckConstraint("CK_Session_QuestionCount", "[PlannedQuestionCount] BETWEEN 5 AND 10"));
        });
        model.Entity<InterviewTurn>(e =>
        {
            e.HasOne<InterviewTurn>().WithMany().HasForeignKey(x => x.RootTurnId);
            e.HasOne<InterviewTurn>().WithMany().HasForeignKey(x => x.ParentTurnId);
            e.Property(x => x.SkillTags).HasMaxLength(1000);
            e.Property(x => x.CodeLanguage).HasMaxLength(60);
            e.HasIndex(x => new { x.SessionId, x.SequenceNumber }).IsUnique();
            e.HasIndex(x => new { x.SessionId, x.MainQuestionNumber });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Turn_Sequence", "[SequenceNumber] > 0 AND [MainQuestionNumber] > 0");
                t.HasCheckConstraint("CK_Turn_FollowUpDepth", "[FollowUpDepth] BETWEEN 0 AND 2");
            });
        });
        model.Entity<TurnEvaluation>(e =>
        {
            e.HasKey(x => x.TurnId);
            e.HasOne<InterviewTurn>().WithOne(x => x.Evaluation).HasForeignKey<TurnEvaluation>(x => x.TurnId);
            e.Property(x => x.OverallScore).HasPrecision(5, 2);
            e.HasMany(x => x.Scores).WithOne().HasForeignKey(x => x.TurnId);
            e.ToTable(t => t.HasCheckConstraint("CK_TurnEvaluation_Score", "[OverallScore] BETWEEN 0 AND 10"));
        });
        model.Entity<TurnScore>(e =>
        {
            e.HasOne(x => x.Criterion).WithMany().HasForeignKey(x => x.RubricCriterionId);
            e.Property(x => x.Score).HasPrecision(5, 2);
            e.HasIndex(x => new { x.TurnId, x.RubricCriterionId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_TurnScore_Score", "[Score] BETWEEN 0 AND 10"));
        });
        model.Entity<Evaluation>(e =>
        {
            e.HasKey(x => x.SessionId);
            e.HasOne<InterviewSession>().WithOne(x => x.Evaluation).HasForeignKey<Evaluation>(x => x.SessionId);
            e.Property(x => x.OverallScore).HasPrecision(5, 2);
            e.Property(x => x.CompletionRate).HasPrecision(6, 3);
            e.Property(x => x.Verdict).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(120);
            e.Property(x => x.PromptVersion).HasMaxLength(60);
            e.ToTable(t => t.HasCheckConstraint("CK_Evaluation_Score", "[OverallScore] BETWEEN 0 AND 10 AND [ReadinessScore] BETWEEN 0 AND 100 AND [CompletionRate] BETWEEN 0 AND 100"));
        });
        model.Entity<DeliveryMetric>(e =>
        {
            e.HasKey(x => x.TurnId);
            e.HasOne<InterviewTurn>().WithOne(x => x.DeliveryMetric).HasForeignKey<DeliveryMetric>(x => x.TurnId);
            e.Property(x => x.Availability).HasMaxLength(80);
            e.ToTable(t => t.HasCheckConstraint("CK_Delivery_Nonnegative", "[WordCount] >= 0 AND ([WordsPerMinute] IS NULL OR [WordsPerMinute] >= 0) AND ([ThinkingSeconds] IS NULL OR [ThinkingSeconds] >= 0) AND ([SpeakingSeconds] IS NULL OR [SpeakingSeconds] >= 0) AND ([LongPauseCount] IS NULL OR [LongPauseCount] >= 0) AND ([FillerCount] IS NULL OR [FillerCount] >= 0)"));
        });
        model.Entity<AiRequestLog>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<InterviewSession>().WithMany().HasForeignKey(x => x.SessionId);
            e.Property(x => x.Provider).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(120);
            e.Property(x => x.Operation).HasMaxLength(80);
            e.Property(x => x.Status).HasMaxLength(60);
            e.Property(x => x.EstimatedCostUsd).HasPrecision(18, 8);
            e.Property(x => x.KeySource).HasMaxLength(16).HasDefaultValue("Legacy");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.UserId, x.KeySource, x.CreatedAt });
            e.HasIndex(x => new { x.SessionId, x.CreatedAt });
        });
        model.Entity<SkillTag>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
        });
        model.Entity<QuestionBank>(e =>
        {
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId);
            e.Property(x => x.CompanyType).HasMaxLength(60);
            e.HasMany(x => x.Tags).WithOne(x => x.Question).HasForeignKey(x => x.QuestionBankId);
            e.HasIndex(x => new { x.PositionId, x.Level, x.Language, x.Kind, x.IsActive });
        });
        model.Entity<QuestionSkillTag>(e =>
        {
            e.HasKey(x => new { x.QuestionBankId, x.SkillTagId });
            e.HasOne(x => x.Skill).WithMany().HasForeignKey(x => x.SkillTagId);
        });
        model.Entity<Classroom>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LecturerId);
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.JoinCode).HasMaxLength(24);
            e.HasIndex(x => x.JoinCode).IsUnique();
            e.HasMany(x => x.Members).WithOne(x => x.Class).HasForeignKey(x => x.ClassId);
            e.HasMany(x => x.Assignments).WithOne(x => x.Class).HasForeignKey(x => x.ClassId);
        });
        model.Entity<ClassMember>(e =>
        {
            e.HasKey(x => new { x.ClassId, x.StudentId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentId);
        });
        model.Entity<ClassBan>(e =>
        {
            e.HasKey(x => new { x.ClassId, x.StudentId });
            e.HasOne<Classroom>().WithMany().HasForeignKey(x => x.ClassId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentId);
        });
        model.Entity<Assignment>(e =>
        {
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId);
            e.HasOne<RubricVersion>().WithMany().HasForeignKey(x => x.RubricVersionId);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasIndex(x => new { x.ClassId, x.DueAt });
            e.HasMany(x => x.Submissions).WithOne(x => x.Assignment).HasForeignKey(x => x.AssignmentId);
            e.ToTable(t => t.HasCheckConstraint("CK_Assignment_QuestionCount", "[QuestionCount] BETWEEN 5 AND 10"));
        });
        model.Entity<AssignmentSubmission>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentId);
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId);
            e.HasIndex(x => x.SessionId).IsUnique();
            e.HasIndex(x => new { x.AssignmentId, x.StudentId, x.AttemptNumber }).IsUnique();
        });
        model.Entity<LecturerComment>(e =>
        {
            e.HasOne<Classroom>().WithMany().HasForeignKey(x => x.ClassId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LecturerId);
            e.HasOne<InterviewSession>().WithMany().HasForeignKey(x => x.SessionId);
            e.HasIndex(x => new { x.SessionId, x.CreatedAt });
        });

        foreach (var limit in DatabaseTextLimits.All)
            model.Entity(limit.Entity).ToTable(table => table.HasCheckConstraint(limit.Constraint, limit.Predicate));

        // Domain records form several paths back to users/sessions. Explicit deletes preserve
        // historical evidence and avoid SQL Server's multiple cascade-path restriction.
        foreach (var entity in model.Model.GetEntityTypes().Where(x => x.ClrType.Namespace == typeof(InterviewSession).Namespace))
        {
            foreach (var foreignKey in entity.GetForeignKeys())
                foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
        }
    }
}
