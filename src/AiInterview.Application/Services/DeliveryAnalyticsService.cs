using System.Text.RegularExpressions;
using AiInterview.Application.DTOs;
using AiInterview.Domain.Entities;

namespace AiInterview.Application.Services;

public interface IDeliveryAnalyticsService { DeliveryMetric Calculate(Guid turnId, string answer, InterviewLanguage language, SpeechTelemetry? telemetry); }

public static class SpeechTelemetryRules
{
    public const double MaxElapsedMs = 3_600_000;

    /// <summary>Validates the complete observation set. Partial filtering could manufacture a pause between unrelated windows.</summary>
    public static bool IsValid(SpeechTelemetry? telemetry)
    {
        if (telemetry is null) return true;
        if (telemetry.ElapsedMs is { } elapsed && (!double.IsFinite(elapsed) || elapsed < 0 || elapsed > MaxElapsedMs)) return false;
        if (telemetry.FirstSpeechMs is { } first && (!double.IsFinite(first) || first < 0 || telemetry.ElapsedMs is null || first > telemetry.ElapsedMs)) return false;
        var windows = telemetry.Windows ?? [];
        if (windows.Count > 1000) return false;
        if (windows.Count == 0) return true;
        if (telemetry.ElapsedMs is null || windows.Any(w => w is null || !double.IsFinite(w.StartMs) || !double.IsFinite(w.EndMs)
            || w.StartMs < 0 || w.EndMs < w.StartMs || w.EndMs > telemetry.ElapsedMs)) return false;
        var ordered = windows.OrderBy(w => w.StartMs).ToArray();
        return ordered.Zip(ordered.Skip(1)).All(pair => pair.First.EndMs <= pair.Second.StartMs);
    }

    public static (DateTimeOffset? DisplayedAt, DateTimeOffset? StartedAt) Timestamps(DateTimeOffset submittedAt, SpeechTelemetry? telemetry)
    {
        if (telemetry is null || !IsValid(telemetry) || telemetry.ElapsedMs is not { } elapsed) return (null, null);
        var displayedAt = submittedAt.AddMilliseconds(-elapsed);
        return (displayedAt, telemetry.FirstSpeechMs is { } first ? displayedAt.AddMilliseconds(first) : null);
    }
}

public sealed class DeliveryAnalyticsService : IDeliveryAnalyticsService
{
    public DeliveryMetric Calculate(Guid turnId, string answer, InterviewLanguage language, SpeechTelemetry? telemetry)
    {
        var words = Regex.Matches(answer.Trim(), @"\S+").Count;
        var result = new DeliveryMetric { TurnId = turnId, WordCount = words };
        if (telemetry is null || !telemetry.UsedRecognition) return result;
        string[] fillers = language == InterviewLanguage.Vietnamese ? ["ờ", "à", "ừm", "kiểu như", "thì là", "nói chung"] : ["um", "uh", "like", "you know", "basically"];
        var counts = fillers.ToDictionary(f => f, f => Regex.Matches(answer, @"(?<![\p{L}\p{N}])" + Regex.Escape(f) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);
        result.FillerCount = counts.Values.Sum();
        result.FillerDetailsJson = AiPrompts.Json(counts);
        result.Availability = "estimated-browser-events";
        if (!SpeechTelemetryRules.IsValid(telemetry)) return result;
        if (telemetry.ElapsedMs is > 0 and <= 3_600_000 && telemetry.FirstSpeechMs is >= 0 && telemetry.FirstSpeechMs < telemetry.ElapsedMs)
        {
            result.ThinkingSeconds = Math.Round(telemetry.FirstSpeechMs.Value / 1000, 2);
            result.SpeakingSeconds = Math.Round((telemetry.ElapsedMs.Value - telemetry.FirstSpeechMs.Value) / 1000, 2);
            if (result.SpeakingSeconds >= 1) result.WordsPerMinute = Math.Round(words * 60 / result.SpeakingSeconds.Value, 1);
        }
        var windows = (telemetry.Windows ?? []).OrderBy(w => w.StartMs).ToArray();
        if (windows.Length > 0)
            result.LongPauseCount = windows.Zip(windows.Skip(1)).Count(pair => pair.Second.StartMs - pair.First.EndMs >= 2000);
        return result;
    }
}
