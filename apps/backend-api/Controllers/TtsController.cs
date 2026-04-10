using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/tts")]
public sealed class TtsController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver,
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

        var actor = adminRequestContextResolver.TryGetCurrentAdmin();
        var accessDecision = repository.EvaluateCustomerLanguageAccess(customerUserId, languageCode);
        var bypassPremiumGate = string.IsNullOrWhiteSpace(customerUserId) || actor is not null;
        if (!accessDecision.IsAllowed && !(bypassPremiumGate && accessDecision.RequiresPremium))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<string>.Fail(accessDecision.Message));
        }

        try
        {
            var audio = await textToSpeechService.GenerateAudioAsync(
                new TextToSpeechRequest(
                    text,
                    accessDecision.LanguageCode,
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
                accessDecision.LanguageCode,
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
                accessDecision.LanguageCode,
                idx,
                total);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResponse<string>.Fail("Unable to generate audio at this time."));
        }
    }
}
