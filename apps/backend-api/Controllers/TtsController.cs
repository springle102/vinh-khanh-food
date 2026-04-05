using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/tts")]
public sealed class TtsController(
    GoogleTranslateTtsProxyService googleTranslateTtsProxyService,
    ILogger<TtsController> logger) : ControllerBase
{
    [HttpGet("google")]
    public async Task<IActionResult> ProxyGoogleTranslateTts(
        [FromQuery] string text,
        [FromQuery] string languageCode,
        [FromQuery] int? idx,
        [FromQuery] int? total,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("Text lÃ  báº¯t buá»™c.");
        }

        try
        {
            var audio = await googleTranslateTtsProxyService.FetchAudioAsync(
                text,
                languageCode,
                idx,
                total,
                cancellationToken);

            Response.Headers["X-TTS-Provider"] = "google_translate_proxy";
            return File(audio.Content, audio.ContentType);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException ||
            exception is HttpRequestException ||
            exception is TaskCanceledException)
        {
            logger.LogWarning(
                exception,
                "Unable to proxy Google Translate TTS. language={LanguageCode}; segment={SegmentIndex}/{TotalSegments}",
                languageCode,
                idx,
                total);
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
