using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;

namespace VinhKhanh.BackendApi.Controllers;

public sealed record BlobBackfillRequest(bool DryRun = false);

[ApiController]
[Route("api/v1/admin/blob-backfill")]
public sealed class BlobBackfillController(
    AdminRequestContextResolver adminRequestContextResolver,
    BlobBackfillService blobBackfillService) : ControllerBase
{
    [HttpPost("run")]
    [ProducesResponseType(typeof(ApiResponse<BlobBackfillResult>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<BlobBackfillResult>>> Run(
        [FromBody] BlobBackfillRequest? request,
        CancellationToken cancellationToken)
    {
        var actor = adminRequestContextResolver.RequireSuperAdmin();
        var result = await blobBackfillService.RunAsync(
            actor,
            request?.DryRun ?? false,
            cancellationToken);

        return Ok(ApiResponse<BlobBackfillResult>.Ok(result, "Blob backfill completed."));
    }
}
