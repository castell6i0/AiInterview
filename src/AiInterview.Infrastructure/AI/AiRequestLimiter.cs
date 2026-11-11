using AiInterview.Application.Interfaces;
using AiInterview.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AiInterview.Infrastructure.Persistence;

namespace AiInterview.Infrastructure.AI;

/// <summary>
/// Atomic reservations for the supported single-process localhost deployment. This singleton
/// holds the gate until the durable reservation is committed, never during a provider HTTP call.
/// A multi-instance deployment must replace it with a database/distributed reservation service.
/// </summary>
public sealed class AiRequestLimiter(IOptionsMonitor<AiQuotaOptions>? configured = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object concurrentLock = new();
    private readonly Dictionary<string, int> activeSystemCalls = new(StringComparer.Ordinal);
    private AiQuotaOptions Policy => configured?.CurrentValue ?? new AiQuotaOptions();

    public async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        return new Lease(() => gate.Release());
    }

    public IDisposable EnterCall(string userId, bool systemKey)
    {
        if (!systemKey) return new Lease(() => { });
        var policy = Policy;
        if (!policy.SystemKeyEnabled) throw Disabled();
        lock (concurrentLock)
        {
            var current = activeSystemCalls.GetValueOrDefault(userId);
            if (current >= policy.SystemMaxConcurrentRequests)
                throw new AiServiceException("system_concurrency", "Bạn đang có nhiều lượt AI xử lý cùng lúc. Hãy chờ lượt hiện tại hoàn tất rồi thử lại.");
            activeSystemCalls[userId] = current + 1;
        }
        return new Lease(() =>
        {
            lock (concurrentLock)
            {
                var next = activeSystemCalls.GetValueOrDefault(userId) - 1;
                if (next <= 0) activeSystemCalls.Remove(userId); else activeSystemCalls[userId] = next;
            }
        });
    }

    public async Task<AiRequestLog> ReserveAsync(AppDbContext db, AiRequest request, string provider,
        string model, bool systemKey, int maxPerMinute, int attempt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new AiServiceException("authentication_required", "Hãy đăng nhập trước khi sử dụng AI.");
        using var lease = await AcquireAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var minuteAgo = now.AddMinutes(-1);
        var charged = db.AiRequestLogs.Where(x => x.UserId == request.UserId && x.Status != "cancelled_before_send");
        if (await charged.CountAsync(x => x.CreatedAt >= minuteAgo, ct) >= maxPerMinute)
            throw new AiServiceException("rate_limit", "Bạn đã đạt giới hạn lượt gọi AI trong một phút. Vui lòng chờ một phút rồi tiếp tục.");
        if (systemKey)
        {
            var policy = Policy; // re-read for every retry; emergency switch is not a request snapshot
            await EnsureSystemEligibilityAsync(db, request.UserId, policy, now, ct);
            var startDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            var startMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var shared = charged.Where(x => x.KeySource == "System");
            if (await shared.CountAsync(x => x.CreatedAt >= startDay, ct) >= policy.SystemRequestsPerDay)
                throw new AiServiceException("system_quota_daily", "Bạn đã dùng hết lượt AI hệ thống hôm nay (ngày UTC, đặt lại lúc 07:00 giờ Việt Nam). Hãy thêm key cá nhân trong Cài đặt AI hoặc quay lại ngày mai.");
            if (policy.SystemRequestsPerMonth > 0 && await shared.CountAsync(x => x.CreatedAt >= startMonth, ct) >= policy.SystemRequestsPerMonth)
                throw new AiServiceException("system_quota_monthly", "Bạn đã dùng hết lượt AI hệ thống trong tháng UTC. Hãy thêm key cá nhân trong Cài đặt AI hoặc liên hệ quản trị viên.");
        }
        var log = new AiRequestLog
        {
            UserId = request.UserId, SessionId = request.SessionId, Provider = provider, Model = model,
            KeySource = systemKey ? "System" : "Personal", Operation = request.Operation,
            Status = "reserved", AttemptNumber = attempt, CreatedAt = now
        };
        db.AiRequestLogs.Add(log);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
        { throw new AiServiceException("logging_unavailable", "Không thể ghi nhật ký lượt gọi AI. Vui lòng thử lại sau."); }
        return log;
    }

    public async Task MarkDispatchedAsync(AppDbContext db, AiRequestLog log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (log.KeySource == "System")
            await EnsureSystemEligibilityAsync(db, log.UserId, Policy, DateTimeOffset.UtcNow, ct);
        log.DispatchedAt = DateTimeOffset.UtcNow;
        log.Status = "started";
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
        { throw new AiServiceException("logging_unavailable", "Không thể ghi nhật ký lượt gọi AI. Vui lòng thử lại sau."); }
    }

    private static async Task EnsureSystemEligibilityAsync(AppDbContext db, string userId, AiQuotaOptions policy,
        DateTimeOffset now, CancellationToken ct)
    {
        if (!policy.SystemKeyEnabled) throw Disabled();
        var user = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.EmailConfirmed, x.LockoutEnabled, x.LockoutEnd }).SingleOrDefaultAsync(ct);
        if (user is null) throw new AiServiceException("authentication_required", "Hãy đăng nhập trước khi sử dụng AI.");
        if (user.LockoutEnabled && user.LockoutEnd > now)
            throw new AiServiceException("account_locked", "Tài khoản đang tạm khóa. Hãy liên hệ quản trị viên.");
        if (policy.RequireConfirmedEmail && !user.EmailConfirmed)
            throw new AiServiceException("email_unconfirmed", "Hãy xác nhận email để dùng AI hệ thống. Bạn vẫn có thể thêm key cá nhân tại Cài đặt AI.");
    }

    public static AiServiceException DescribeProviderError(AiServiceException error, bool systemKey)
    {
        if (error.Code is "authentication" or "unauthorized" or "invalid_key" or "forbidden")
            return new(error.Code, systemKey
                ? "Kết nối AI hệ thống chưa dùng được. Hãy liên hệ quản trị viên hoặc thêm key cá nhân trong Cài đặt AI."
                : "Key AI cá nhân không được chấp nhận hoặc thiếu quyền. Hãy kiểm tra và lưu lại key trong Cài đặt AI.");
        if (systemKey && error.Code is "quota" or "quota_daily" or "quota_exhausted" or "unavailable")
            return new(error.Code, "Dịch vụ AI hệ thống đang quá tải hoặc hết quota. Vui lòng thử lại sau, liên hệ quản trị viên hoặc thêm key cá nhân.");
        return error;
    }

    private static AiServiceException Disabled() => new("system_disabled",
        "AI hệ thống đang tạm tắt. Hãy thêm key cá nhân trong Cài đặt AI hoặc liên hệ quản trị viên.");

    private sealed class Lease(Action release) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) release(); }
    }
}
