using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/tts")]
public sealed class TtsController(
    ITextToSpeechService textToSpeechService,
    IOptions<TextToSpeechOptions> optionsAccessor,
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
        var apiKeyPresent = !string.IsNullOrWhiteSpace(optionsAccessor.Value.ApiKey);
        Response.Headers["X-TTS-Config-Api-Key-Present"] = apiKeyPresent ? "true" : "false";
        Response.Headers["X-TTS-Requested-Language"] = languageCode?.Trim() ?? string.Empty;

        logger.LogInformation(
            "Received TTS proxy request. language={LanguageCode}; textLength={TextLength}; voiceId={VoiceId}; modelId={ModelId}; apiKeyPresent={ApiKeyPresent}; segment={SegmentIndex}/{TotalSegments}",
            languageCode,
            text?.Trim().Length ?? 0,
            voiceId,
            modelId,
            apiKeyPresent,
            idx,
            total);

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
                exception.StatusCode,
                ApiResponse<string>.Fail(exception.Message));
        }
        catch (TextToSpeechGenerationException exception)
        {
            Response.Headers["X-TTS-Upstream-Status"] = exception.StatusCode.ToString();
            logger.LogWarning(
                exception,
                "Unable to generate ElevenLabs TTS audio. language={LanguageCode}; segment={SegmentIndex}/{TotalSegments}; status={StatusCode}; response={ResponseSnippet}",
                languageCode,
                idx,
                total,
                exception.StatusCode,
                exception.ResponseSnippet);
            return StatusCode(
                exception.StatusCode,
                ApiResponse<string>.Fail(exception.Message));
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(
                exception,
                "TTS proxy request timed out before ElevenLabs returned audio. language={LanguageCode}; segment={SegmentIndex}/{TotalSegments}",
                languageCode,
                idx,
                total);
            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                ApiResponse<string>.Fail("Yeu cau tao audio dang bi timeout."));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Unable to reach ElevenLabs TTS upstream. language={LanguageCode}; segment={SegmentIndex}/{TotalSegments}",
                languageCode,
                idx,
                total);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<string>.Fail("Khong the ket noi den ElevenLabs TTS."));
        }
    }
}
