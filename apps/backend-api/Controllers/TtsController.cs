using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/tts")]
public sealed class TtsController(
    ITextToSpeechService textToSpeechService,
    ILogger<TtsController> logger) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> GenerateAudio(
        [FromQuery] string text,
        [FromQuery] string languageCode,
        [FromQuery] string? customerUserId,
        [FromQuery] int? idx,
        [FromQuery] int? total,
        [FromQuery(Name = "voice_id")] string? voiceId,
        [FromQuery(Name = "model_id")] string? modelId,
        [FromQuery(Name = "output_format")] string? outputFormat,
        CancellationToken cancellationToken)
        => GenerateAudioCoreAsync(
            new TextToSpeechRequest(
                text,
                languageCode,
                voiceId,
                modelId,
                outputFormat,
                SegmentIndex: idx,
                TotalSegments: total),
            cancellationToken);

    [HttpPost]
    public Task<IActionResult> GenerateAudio(
        [FromBody] TextToSpeechBodyRequest body,
        CancellationToken cancellationToken)
        => GenerateAudioCoreAsync(
            new TextToSpeechRequest(
                body.Text,
                body.LanguageCode,
                body.VoiceId,
                body.ModelId,
                body.OutputFormat,
                body.PreviousText,
                body.NextText,
                body.SegmentIndex,
                body.TotalSegments),
            cancellationToken);

    private async Task<IActionResult> GenerateAudioCoreAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(ApiResponse<string>.Fail("Text is required."));
        }

        if (string.IsNullOrWhiteSpace(request.LanguageCode))
        {
            return BadRequest(ApiResponse<string>.Fail("LanguageCode is required."));
        }

        try
        {
            var audio = await textToSpeechService.GenerateAudioAsync(request, cancellationToken);

            Response.Headers["X-TTS-Provider"] = audio.Provider;
            Response.Headers["X-TTS-Voice-Id"] = audio.VoiceId;
            Response.Headers["X-TTS-Model-Id"] = audio.ModelId;
            Response.Headers["X-TTS-Output-Format"] = audio.OutputFormat;
            Response.Headers.CacheControl = "public, max-age=3600";
            Response.Headers.Remove("Pragma");
            Response.Headers.Remove("Expires");
            return File(audio.Content, audio.ContentType);
        }
        catch (TextToSpeechConfigurationException exception)
        {
            logger.LogError(
                exception,
                "ElevenLabs Text-to-Speech is not configured. language={LanguageCode}; segment={SegmentIndex}/{TotalSegments}",
                request.LanguageCode,
                request.SegmentIndex,
                request.TotalSegments);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<string>.Fail(exception.Message));
        }
        catch (Exception exception) when (
            exception is TextToSpeechGenerationException ||
            exception is HttpRequestException ||
            exception is TaskCanceledException)
        {
            logger.LogWarning(
                exception,
                "Unable to generate ElevenLabs TTS audio. language={LanguageCode}; segment={SegmentIndex}/{TotalSegments}",
                request.LanguageCode,
                request.SegmentIndex,
                request.TotalSegments);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResponse<string>.Fail("Unable to generate audio at this time."));
        }
    }

    public sealed class TextToSpeechBodyRequest
    {
        public string Text { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = string.Empty;
        public string? VoiceId { get; set; }
        public string? ModelId { get; set; }
        public string? OutputFormat { get; set; }
        public string? PreviousText { get; set; }
        public string? NextText { get; set; }
        public int? SegmentIndex { get; set; }
        public int? TotalSegments { get; set; }
    }
}
