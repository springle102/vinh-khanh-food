using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/storage")]
public sealed class StorageController(StorageService storageService) : ControllerBase
{
    [HttpPost("upload")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<StoredFileResponse>>> Upload(
        [FromForm] IFormFile? file,
        [FromForm] string? folder,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(ApiResponse<StoredFileResponse>.Fail("File upload là bắt buộc."));
        }

        var stored = await storageService.SaveAsync(file, folder, cancellationToken);
        return Ok(ApiResponse<StoredFileResponse>.Ok(stored, "Upload file thành công."));
    }
}
