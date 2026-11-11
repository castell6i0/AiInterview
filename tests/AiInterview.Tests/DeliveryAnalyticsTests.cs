using AiInterview.Application.DTOs;
using AiInterview.Application.Services;
using AiInterview.Domain.Entities;
using Xunit;

namespace AiInterview.Tests;

public class DeliveryAnalyticsTests
{
    private readonly DeliveryAnalyticsService service = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextInputHasWordCountButNoInventedSpeechMetrics(bool telemetryExists)
    {
        var telemetry = telemetryExists ? new SpeechTelemetry { UsedRecognition = false, ElapsedMs = 30000, FirstSpeechMs = 1000 } : null;
        var metric = service.Calculate(Guid.NewGuid(), "one two three", InterviewLanguage.English, telemetry);
        Assert.Equal(3, metric.WordCount);
        Assert.Null(metric.WordsPerMinute); Assert.Null(metric.ThinkingSeconds); Assert.Null(metric.SpeakingSeconds);
        Assert.Null(metric.FillerCount); Assert.Null(metric.LongPauseCount); Assert.Equal("text", metric.Availability);
    }

    [Fact]
    public void BrowserTimingProducesExplicitlyEstimatedRateAndThinkingTime()
    {
        var metric = service.Calculate(Guid.NewGuid(), "one two three four five six", InterviewLanguage.English,
            new SpeechTelemetry { UsedRecognition = true, FirstSpeechMs = 2000, ElapsedMs = 8000 });
        Assert.Equal(2, metric.ThinkingSeconds); Assert.Equal(6, metric.SpeakingSeconds); Assert.Equal(60, metric.WordsPerMinute);
        Assert.Equal("estimated-browser-events", metric.Availability);
        Assert.Null(metric.LongPauseCount);
    }

    [Fact]
    public void EnglishFillerMatchingUsesTokenBoundariesAndCountsPhrasesOnce()
    {
        var metric = service.Calculate(Guid.NewGuid(), "Um, umbrella. Uh, thumb. LIKE likely. You know, basically.", InterviewLanguage.English,
            new SpeechTelemetry { UsedRecognition = true });
        Assert.Equal(5, metric.FillerCount);
    }

    [Fact]
    public void VietnameseFillerMatchingUsesUnicodeBoundaries()
    {
        var metric = service.Calculate(Guid.NewGuid(), "Ờ, à, ừm, kiểu như, thì là, nói chung. Từ 'là' đứng riêng không phải cụm từ đệm.", InterviewLanguage.Vietnamese,
            new SpeechTelemetry { UsedRecognition = true });
        Assert.Equal(6, metric.FillerCount);
    }

    [Theory]
    [InlineData(-1, 5000)]
    [InlineData(5000, 1000)]
    [InlineData(5000, 5000)]
    [InlineData(0, 0)]
    [InlineData(0, 3600001)]
    [InlineData(double.NaN, 5000)]
    [InlineData(0, double.PositiveInfinity)]
    public void ImpossibleTimestampsDoNotYieldTimingMetrics(double firstSpeechMs, double elapsedMs)
    {
        var metric = service.Calculate(Guid.NewGuid(), "test answer", InterviewLanguage.English,
            new SpeechTelemetry { UsedRecognition = true, FirstSpeechMs = firstSpeechMs, ElapsedMs = elapsedMs });
        Assert.Null(metric.ThinkingSeconds); Assert.Null(metric.SpeakingSeconds); Assert.Null(metric.WordsPerMinute);
    }

    [Fact]
    public void LongPauseCountUsesActualAdjacentWindowsAndTwoSecondThreshold()
    {
        var metric = service.Calculate(Guid.NewGuid(), "test answer", InterviewLanguage.English,
            new SpeechTelemetry { UsedRecognition = true, FirstSpeechMs = 1000, ElapsedMs = 15000,
                Windows = [new() { StartMs = 9000, EndMs = 11000 }, new() { StartMs = 1000, EndMs = 3000 }, new() { StartMs = 5000, EndMs = 7500 }] });
        Assert.Equal(1, metric.LongPauseCount);
    }

    [Fact]
    public void OverlappingSpeechWindowsLeavePauseMetricUnknown()
    {
        var metric = service.Calculate(Guid.NewGuid(), "test answer", InterviewLanguage.English,
            new SpeechTelemetry { UsedRecognition = true, FirstSpeechMs = 1000, ElapsedMs = 15000,
                Windows = [new() { StartMs = 1000, EndMs = 7000 }, new() { StartMs = 5000, EndMs = 9000 }] });
        Assert.Null(metric.LongPauseCount);
    }

    [Fact]
    public void WindowBeyondSubmissionTimeCannotBeCountedAsAPause()
    {
        var metric = service.Calculate(Guid.NewGuid(), "test answer", InterviewLanguage.English,
            new SpeechTelemetry { UsedRecognition = true, FirstSpeechMs = 1000, ElapsedMs = 10000,
                Windows = [new() { StartMs = 1000, EndMs = 3000 }, new() { StartMs = 99000, EndMs = 100000 }] });
        Assert.Null(metric.LongPauseCount);
    }

    [Fact]
    public void InvalidWindowDoesNotDisappearAndCreateANewFalsePause()
    {
        var metric = service.Calculate(Guid.NewGuid(), "test answer", InterviewLanguage.English,
            new SpeechTelemetry { UsedRecognition = true, FirstSpeechMs = 1000, ElapsedMs = 15000,
                Windows = [new() { StartMs = 1000, EndMs = 3000 }, new() { StartMs = 4000, EndMs = 3500 }, new() { StartMs = 6000, EndMs = 9000 }] });
        Assert.Null(metric.LongPauseCount);
    }

    [Fact]
    public void NullWindowIsHandledAsUnknownTelemetry()
    {
        var metric = service.Calculate(Guid.NewGuid(), "test answer", InterviewLanguage.English,
            new SpeechTelemetry { UsedRecognition = true, FirstSpeechMs = 1000, ElapsedMs = 15000,
                Windows = [new() { StartMs = 1000, EndMs = 3000 }, null!] });
        Assert.Null(metric.LongPauseCount);
    }
}
