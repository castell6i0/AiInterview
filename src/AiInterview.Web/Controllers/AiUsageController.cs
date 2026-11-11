using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiInterview.Web.Controllers;

public sealed record AiUsageRow(string UserId, string Provider, string KeySource, int Requests,
    long InputTokens, long OutputTokens, decimal KnownCostUsd, int UnknownCostCount);
public sealed record AiUsagePage(DateOnly Day, int Page, bool HasMore, bool SystemKeyEnabled,
    int DailyQuota, int MonthlyQuota, IReadOnlyList<AiUsageRow> Rows);

[Authorize(Roles = Roles.Admin), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiUsageController(AppDbContext db, IOptionsMonitor<AiQuotaOptions> options) : Controller
{
    public async Task<IActionResult> Index(DateOnly? day, int page = 1, CancellationToken ct = default)
    {
        var selected = day ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (selected.Year is < 2000 or > 9998) return BadRequest("Ngày không hợp lệ.");
        page = Math.Clamp(page, 1, 10000);
        var start = new DateTimeOffset(selected.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);
        var grouped = db.AiRequestLogs.AsNoTracking()
            .Where(x => x.CreatedAt >= start && x.CreatedAt < end && x.Status != "cancelled_before_send")
            .GroupBy(x => new { x.UserId, x.Provider, x.KeySource })
            .Select(g => new AiUsageRow(g.Key.UserId, g.Key.Provider, g.Key.KeySource, g.Count(),
                g.Sum(x => (long?)x.InputTokens) ?? 0, g.Sum(x => (long?)x.OutputTokens) ?? 0,
                g.Sum(x => x.EstimatedCostUsd) ?? 0, g.Count(x => x.EstimatedCostUsd == null)));
        var rows = await grouped.OrderByDescending(x => x.Requests).ThenBy(x => x.UserId)
            .ThenBy(x => x.Provider).ThenBy(x => x.KeySource).Skip((page - 1) * 50).Take(51).ToListAsync(ct);
        var policy = options.CurrentValue;
        return View(new AiUsagePage(selected, page, rows.Count > 50, policy.SystemKeyEnabled,
            policy.SystemRequestsPerDay, policy.SystemRequestsPerMonth, rows.Take(50).ToList()));
    }
}
