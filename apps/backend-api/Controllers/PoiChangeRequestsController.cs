using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/poi-change-requests")]
public sealed class PoiChangeRequestsController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<PoiChangeRequest>>> GetChangeRequests()
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        return Ok(ApiResponse<IReadOnlyList<PoiChangeRequest>>.Ok(repository.GetPoiChangeRequests(actor)));
    }

    [HttpPost("poi/{poiId}")]
    public ActionResult<ApiResponse<PoiChangeRequest>> SubmitChangeRequest(
        string poiId,
        [FromBody] PoiChangeRequestCreateRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var saved = repository.SubmitPoiChangeRequest(poiId, request, actor);
        return Ok(ApiResponse<PoiChangeRequest>.Ok(saved, "Da gui yeu cau sua POI cho admin duyet."));
    }

    [HttpPost("{id}/approve")]
    public ActionResult<ApiResponse<Poi>> ApproveChangeRequest(string id)
    {
        var actor = adminRequestContextResolver.RequireSuperAdmin();
        var saved = repository.ApprovePoiChangeRequest(id, actor);
        return Ok(ApiResponse<Poi>.Ok(saved, "Da duyet va ap dung thay doi POI."));
    }

    [HttpPost("{id}/reject")]
    public ActionResult<ApiResponse<PoiChangeRequest>> RejectChangeRequest(
        string id,
        [FromBody] PoiChangeRequestDecisionRequest request)
    {
        var actor = adminRequestContextResolver.RequireSuperAdmin();
        var saved = repository.RejectPoiChangeRequest(id, request.Reason, actor);
        return Ok(ApiResponse<PoiChangeRequest>.Ok(saved, "Da tu choi yeu cau sua POI."));
    }
}
