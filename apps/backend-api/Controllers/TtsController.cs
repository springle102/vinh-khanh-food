using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/tts")]
public sealed class TtsController(ILogger<TtsController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult GenerateAudio()
        => RuntimeTtsRemoved();

    [HttpPost]
    public IActionResult GenerateAudio([FromBody] object? _)
        => RuntimeTtsRemoved();

    private IActionResult RuntimeTtsRemoved()
    {
        logger.LogWarning("Deprecated runtime TTS endpoint was requested after the system switched to pre-generated audio.");
        return StatusCode(
            StatusCodes.Status410Gone,
            ApiResponse<string>.Fail("Runtime TTS da bi vo hieu hoa. Hay dung audio pre-generated tu backend."));
    }
}
