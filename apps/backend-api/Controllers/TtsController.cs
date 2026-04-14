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
    public async Task<IActionResult> GenerateAudio(
        [FromQuery] string text,
        [FromQuery] string languageCode,
        [FromQuery] string? customerUserId,
        [FromQuery] int? idx,
        [FromQuery] int? total,
        [FromQuery(Name = "voice_id")] string? voiceId,
        [FromQuery(Name = "model_id")] string? modelId,
        [FromQuery(Name = "output_format")] string? outputFormat,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest(ApiResponse<string>.Fail("Text is required."));
        }

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return BadRequest(ApiResponse<string>.Fail("LanguageCode is required."));
        }

        try
        {
            var audio = await textToSpeechService.GenerateAudioAsync(
                new TextToSpeechRequest(
                    text,
                    languageCode,
                    voiceId,
                    modelId,
                    outputFormat,
                    SegmentIndex: idx,
                    TotalSegments: total),
                cancellationToken);

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
                languageCode,
                idx,
                total);
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
                languageCode,
                idx,
                total);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResponse<string>.Fail("Unable to generate audio at this time."));
        }
    }
}
