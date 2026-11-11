namespace AiInterview.Application.Interfaces;

public interface ICvTextExtractor
{
    Task<string> ExtractAsync(Stream stream, string fileName, CancellationToken ct = default);
}

public sealed class CvExtractionException(string message) : Exception(message);
