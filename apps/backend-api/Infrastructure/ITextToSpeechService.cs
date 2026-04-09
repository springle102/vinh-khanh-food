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

public sealed class TextToSpeechConfigurationException(string message) : InvalidOperationException(message);

public sealed class TextToSpeechGenerationException : InvalidOperationException
{
    public TextToSpeechGenerationException(string message)
        : base(message)
    {
    }

    public TextToSpeechGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
