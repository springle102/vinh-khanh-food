using System.Net;

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
    string OutputFormat,
    int TextLength,
    int SegmentCount,
    double EstimatedDurationSeconds);

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

    public TextToSpeechGenerationException(
        string message,
        HttpStatusCode? statusCode,
        string? providerErrorCode,
        string? providerErrorMessage,
        string? responseBody,
        string? endpoint,
        string? voiceId,
        string? modelId,
        string? outputFormat,
        string? languageCode)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ResponseBody = responseBody;
        Endpoint = endpoint;
        VoiceId = voiceId;
        ModelId = modelId;
        OutputFormat = outputFormat;
        LanguageCode = languageCode;
    }

    public HttpStatusCode? StatusCode { get; }

    public string? ProviderErrorCode { get; }

    public string? ProviderErrorMessage { get; }

    public string? ResponseBody { get; }

    public string? Endpoint { get; }

    public string? VoiceId { get; }

    public string? ModelId { get; }

    public string? OutputFormat { get; }

    public string? LanguageCode { get; }
}
