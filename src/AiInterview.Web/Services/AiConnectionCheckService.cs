using System.Text.Json;
using System.Diagnostics;
using AiInterview.Application.Interfaces;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.AI;
using AiInterview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiInterview.Web.Services;

public sealed record AiConnectionCheckResult(bool Succeeded, string Message);

public sealed class AiConnectionCheckService(IHttpClientFactory clients, CurrentUserAiConfiguration current,
    DbContextOptions<AppDbContext>? dbOptions = null, AiRequestLimiter? limiter = null)
{
    // Exercises the actual generation endpoint with a tiny fixed sample, never interview/CV data.
    public Task<AiConnectionCheckResult> CheckAsync(CancellationToken ct) => CheckAsync(current, ct);

    public async Task<AiConnectionCheckResult> CheckLatestSavedAsync(CancellationToken ct) =>
        await CheckAsync(await current.ReadLatestSavedAsync(ct), ct);

    private async Task<AiConnectionCheckResult> CheckAsync(CurrentUserAiConfiguration configuration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!configuration.IsConfigured)
            return new(false, "Chưa có key dùng được. Hãy lưu API key trước khi kiểm tra kết nối.");
        await using var logDb = dbOptions is null ? null : new AppDbContext(dbOptions);
        AiRequestLog? log = null;
        IDisposable? callLease = null;
        var dispatched = false;
        var watch = Stopwatch.StartNew();
        try
        {
            using var message = configuration.CreateConnectionProbeRequest();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            if (logDb is not null && limiter is not null)
            {
                callLease = limiter.EnterCall(configuration.UserId ?? "", !configuration.UsePersonalSettings);
                log = await limiter.ReserveAsync(logDb,
                    new AiRequest(configuration.UserId ?? "", null, "connection-check", "", new { }, new { }),
                    configuration.Provider, configuration.Model, !configuration.UsePersonalSettings,
                    configuration.MaxRequestsPerMinute, 1, timeout.Token);
                await limiter.MarkDispatchedAsync(logDb, log, timeout.Token);
            }
            timeout.Token.ThrowIfCancellationRequested();
            dispatched = true;
            using var response = await clients.CreateClient("AiConnectionCheck")
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var usage = await AiConnectionProbe.VerifyResponseAsync(configuration.Provider, response, timeout.Token);
            if (log is not null)
            {
                log.Status = "success";
                log.InputTokens = usage.InputTokens;
                log.OutputTokens = usage.OutputTokens;
                log.ThinkingTokens = usage.ThinkingTokens;
            }
            return new(true, "AI đã tạo được nội dung mẫu đúng định dạng bằng key và model đang lưu. Bạn có thể tiếp tục luyện phỏng vấn.");
        }
        catch (AiServiceException error)
        {
            if (log is not null) log.Status = error.Code;
            return new(false, AiRequestLimiter.DescribeProviderError(error, !configuration.UsePersonalSettings).Message);
        }
        catch (UserAiSettingsValidationException)
        { return new(false, "Cấu hình AI chưa dùng được. Hãy kiểm tra model và lưu lại API key."); }
        catch (FormatException)
        { return new(false, "API key chưa đúng định dạng. Hãy lưu lại key rồi thử lại."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(false, "Kết nối mất quá nhiều thời gian. Hãy thử lại sau ít phút."); }
        catch (HttpRequestException)
        { return new(false, "Chưa kết nối được với dịch vụ AI. Hãy kiểm tra mạng rồi thử lại."); }
        catch (IOException)
        { return new(false, "Kết nối tới dịch vụ AI bị gián đoạn. Hãy thử kiểm tra lại."); }
        catch (JsonException)
        { return new(false, "AI chưa tạo được nội dung mẫu đúng định dạng. Hãy thử kiểm tra lại hoặc chọn model khác trong Cài đặt AI."); }
        finally
        {
            callLease?.Dispose();
            if (log is not null && logDb is not null)
            {
                if (!dispatched) { log.Status = "cancelled_before_send"; log.DispatchedAt = null; }
                else if (log.Status == "started") log.Status = ct.IsCancellationRequested ? "cancelled" : "probe_failed";
                log.DurationMs = watch.ElapsedMilliseconds;
                using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await logDb.SaveChangesAsync(saveTimeout.Token); }
                catch (Exception ex) when (ex is DbUpdateException or OperationCanceledException or System.Data.Common.DbException)
                { /* The durable reservation remains charged if final usage cannot be recorded. */ }
            }
        }
    }
}
