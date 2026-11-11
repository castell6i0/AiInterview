namespace AiInterview.Application.Interfaces;

public record AiRequest(string UserId, Guid? SessionId, string Operation, string SystemInstruction, object Data, object Schema);

public interface IAiService
{
    string Provider => "Gemini";
    TimeSpan MaximumCallDuration => TimeSpan.FromMinutes(27);
    bool IsConfigured { get; }
    string Model { get; }

    /// <summary>Generates validated JSON. Streaming callbacks receive provider text deltas; an empty delta resets a retried stream.</summary>
    Task<T> GenerateAsync<T>(AiRequest request, Func<T, string?> validate,
        Func<string, Task>? onText = null, CancellationToken cancellationToken = default);
}

/// <summary>Contains only a message suitable for showing to the user, never the provider response or private prompt.</summary>
public sealed class AiServiceException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
