using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Persistence;
using AiInterview.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AiInterview.Web.Services;

public sealed class DashboardService(AppDbContext db)
{
    public async Task<DashboardModel> IndexAsync(string user, int? positionId, InterviewLanguage? language, InterviewLevel? level, CancellationToken ct)
    {
        var query = Filter(db.Sessions.AsNoTracking().Where(x => x.StudentId == user), positionId, language, level);
        var model = new DashboardModel
        {
            PositionId = positionId, Language = language, Level = level,
            Positions = await db.Positions.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct),
            TotalSessions = await query.CountAsync(ct),
            CompletedSessions = await query.CountAsync(x => x.Status == SessionStatus.Completed, ct),
            Recent = await query.Include(x => x.Position).Include(x => x.Evaluation).OrderByDescending(x => x.CreatedAt).Take(6).ToListAsync(ct)
        };
        model.HasAnySessions = model.TotalSessions > 0 ||
            ((positionId.HasValue || language.HasValue || level.HasValue) &&
             await db.Sessions.AsNoTracking().AnyAsync(x => x.StudentId == user, ct));
        // Only complete reports represent comparable attempts. Incomplete attempts stay in history.
        var reports = await query.Where(x => x.Status == SessionStatus.Completed && x.Evaluation != null && !x.Evaluation.IsProvisional && x.Evaluation.CompletionRate == 100)
            .Include(x => x.Position).Include(x => x.Evaluation)
            .Include(x => x.Turns).ThenInclude(x => x.Evaluation)
            .Include(x => x.Turns).ThenInclude(x => x.DeliveryMetric)
            .AsSplitQuery().OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
        model.FullEvaluatedSessions = reports.Count;
        model.AverageScore = reports.Count == 0 ? null : Math.Round(reports.Average(x => x.Evaluation!.OverallScore), 2);
        model.Progress = reports.OrderBy(x => x.CreatedAt).Select(x =>
        {
            var metrics = x.Turns.Where(t => t.DeliveryMetric != null).Select(t => t.DeliveryMetric!).ToArray();
            return new ProgressPoint(x.Id, $"{x.Position.Name} · {x.Level} · {(x.Language == InterviewLanguage.Vietnamese ? "Việt" : "Anh")}", x.CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM HH:mm"), x.Evaluation!.OverallScore,
                Average(metrics.Select(m => m.WordsPerMinute)), Average(metrics.Select(m => m.ThinkingSeconds)),
                Average(metrics.Select(m => (double?)m.FillerCount)), Average(metrics.Select(m => (double?)m.LongPauseCount)), Average(metrics.Select(m => (double?)m.WordCount)));
        }).ToList();
        model.Skills = CalculateSkills(reports);
        var last = model.Recent.FirstOrDefault(x => model.Positions.Any(p => p.Id == x.PositionId));
        model.Practice = new PracticeWeakModel { PositionId = positionId ?? last?.PositionId ?? model.Positions.FirstOrDefault()?.Id ?? 0,
            Language = language ?? last?.Language ?? InterviewLanguage.Vietnamese, Level = level ?? last?.Level ?? InterviewLevel.Intern };

        var weak = await WeakSkillsAsync(user, model.Practice.PositionId, model.Practice.Language, model.Practice.Level, ct);
        if (weak.Length > 0)
        {
            var bank = await db.Questions.AsNoTracking().Where(x => x.IsActive && x.Position.IsActive && x.PositionId == model.Practice.PositionId && x.Language == model.Practice.Language && x.Level == model.Practice.Level)
                .Include(x => x.Position).Include(x => x.Tags).ThenInclude(x => x.Skill).ToListAsync(ct);
            model.Recommendations = bank.Where(x => x.Tags.Any(t => weak.Contains(t.Skill.Name, StringComparer.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.Tags.Count(t => weak.Contains(t.Skill.Name, StringComparer.OrdinalIgnoreCase))).ThenBy(x => x.Id).Take(6)
                .Select(x => new QuestionRecommendation(x.Id, x.Content, x.Position.Name, x.Level, x.Language, x.Kind, x.CompanyType, x.Tags.Select(t => t.Skill.Name).ToArray())).ToList();
        }
        return model;
    }

    public async Task<string[]> WeakSkillsAsync(string user, int positionId, InterviewLanguage language, InterviewLevel level, CancellationToken ct)
    {
        var reports = await db.Sessions.AsNoTracking().Where(x => x.StudentId == user && x.PositionId == positionId && x.Language == language && x.Level == level &&
                x.Status == SessionStatus.Completed && x.Evaluation != null && !x.Evaluation.IsProvisional && x.Evaluation.CompletionRate == 100)
            .Include(x => x.Turns).ThenInclude(x => x.Evaluation).OrderByDescending(x => x.CreatedAt).Take(20).AsSplitQuery().ToListAsync(ct);
        return CalculateSkills(reports).OrderBy(x => x.Score).ThenByDescending(x => x.AnswerCount).Take(3).Select(x => x.Skill).ToArray();
    }

    public async Task<HistoryModel> HistoryAsync(string user, HistoryModel model, CancellationToken ct)
    {
        var query = Filter(db.Sessions.AsNoTracking().Where(x => x.StudentId == user), model.PositionId, model.Language, null);
        if (model.Status.HasValue) query = query.Where(x => x.Status == model.Status);
        if (model.From.HasValue)
        {
            var from = new DateTimeOffset(DateTime.SpecifyKind(model.From.Value.Date, DateTimeKind.Unspecified), TimeSpan.FromHours(7));
            query = query.Where(x => x.CreatedAt >= from);
        }
        if (model.To.HasValue && model.To.Value.Date < DateTime.MaxValue.Date)
        {
            var to = new DateTimeOffset(DateTime.SpecifyKind(model.To.Value.Date, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).AddDays(1);
            query = query.Where(x => x.CreatedAt < to);
        }
        model.TotalCount = await query.CountAsync(ct);
        model.PageCount = Math.Max(1, (int)Math.Ceiling(model.TotalCount / 20d));
        model.Page = Math.Clamp(model.Page, 1, model.PageCount);
        model.Sessions = await query.Include(x => x.Position).Include(x => x.Evaluation).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((model.Page - 1) * 20).Take(20).ToListAsync(ct);
        model.Positions = await db.Positions.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        return model;
    }

    private static IQueryable<InterviewSession> Filter(IQueryable<InterviewSession> query, int? position, InterviewLanguage? language, InterviewLevel? level)
    {
        if (position.HasValue) query = query.Where(x => x.PositionId == position);
        if (language.HasValue) query = query.Where(x => x.Language == language);
        if (level.HasValue) query = query.Where(x => x.Level == level);
        return query;
    }

    internal static List<SkillProgress> CalculateSkills(IEnumerable<InterviewSession> sessions) => sessions.SelectMany(x => x.Turns)
        .Where(x => x.Status == TurnStatus.Answered && x.Evaluation != null)
        .SelectMany(x => x.SkillTags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(tag => new { Skill = tag, Score = x.Evaluation!.OverallScore }))
        .GroupBy(x => x.Skill, StringComparer.OrdinalIgnoreCase)
        .Select(x => new SkillProgress(x.Key, Math.Round(x.Average(i => i.Score), 2), x.Count())).OrderBy(x => x.Score).ThenBy(x => x.Skill).ToList();

    private static double? Average(IEnumerable<double?> values)
    {
        var measured = values.Where(x => x.HasValue && double.IsFinite(x.Value)).Select(x => x!.Value).ToArray();
        return measured.Length == 0 ? null : Math.Round(measured.Average(), 1);
    }
}
