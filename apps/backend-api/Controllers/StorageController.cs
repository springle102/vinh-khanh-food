using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

public sealed class StorageUploadRequest
{
    public IFormFile? File { get; set; }
    public string? Folder { get; set; }
}

[ApiController]
[Route("api/v1/storage")]
public sealed class StorageController(
    AdminRequestContextResolver adminRequestContextResolver,
    StorageService storageService,
    ResponseUrlNormalizer responseUrlNormalizer) : ControllerBase
{
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<StoredFileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StoredFileResponse>), StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<StoredFileResponse>>> Upload(
        [FromForm] StorageUploadRequest request,
        CancellationToken cancellationToken)
    {
        adminRequestContextResolver.RequireAuthenticatedAdmin();

        if (request.File is null)
        {
            return BadRequest(ApiResponse<StoredFileResponse>.Fail("File upload la bat buoc."));
        }

        var stored = responseUrlNormalizer.Normalize(
            await storageService.SaveAsync(request.File, request.Folder, cancellationToken));

        return Ok(ApiResponse<StoredFileResponse>.Ok(stored, "Upload file thanh cong."));
    }
}