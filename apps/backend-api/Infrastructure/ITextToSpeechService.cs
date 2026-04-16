namespace VinhKhanh.BackendApi.Infrastructure;

public interface ITextToSpeechService
{
    Task<TextToSpeechResult> GenerateAudioAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken);
}

public sealed record TextToSpeechRequest(
    string Text,
    string LanguageCode,
    string? VoiceId = null,
    string? ModelId = null,
    string? OutputFormat = null,
    string? PreviousText = null,
    string? NextText = null,
    int? SegmentIndex = null,
    int? TotalSegments = null);

public sealed record TextToSpeechResult(
    byte[] Content,
    string ContentType,
    string Provider,
    string VoiceId,
    string ModelId,
    string OutputFormat);

public sealed class TextToSpeechConfigurationException : InvalidOperationException
{
    public TextToSpeechConfigurationException(string message, int statusCode = 503)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public sealed class TextToSpeechGenerationException : InvalidOperationException
{
    public TextToSpeechGenerationException(string message, int statusCode = 502, string? responseSnippet = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseSnippet = responseSnippet;
    }

    public TextToSpeechGenerationException(
        string message,
        Exception innerException,
        int statusCode = 502,
        string? responseSnippet = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseSnippet = responseSnippet;
    }

    public int StatusCode { get; }
    public string? ResponseSnippet { get; }
}
