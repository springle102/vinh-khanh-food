using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1/tours")]
public sealed class ToursController(
    AdminDataRepository repository,
    AdminRequestContextResolver adminRequestContextResolver) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<IReadOnlyList<TourRoute>>> GetTours(
        [FromQuery] string? theme,
        [FromQuery] bool? isActive)
    {
        var actor = adminRequestContextResolver.TryGetCurrentAdmin();
        IEnumerable<TourRoute> query = repository.GetRoutes(actor);

        if (!string.IsNullOrWhiteSpace(theme))
        {
            query = query.Where(item => item.Theme.Equals(theme, StringComparison.OrdinalIgnoreCase));
        }

        if (isActive.HasValue)
        {
            query = query.Where(item => item.IsActive == isActive.Value);
        }

        return Ok(ApiResponse<IReadOnlyList<TourRoute>>.Ok(query.ToList()));
    }

    [HttpPost]
    public ActionResult<ApiResponse<TourRoute>> CreateTour([FromBody] TourRouteUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<TourRoute>.Fail("Ten tour la bat buoc."));
        }

        if (request.DurationMinutes <= 0)
        {
            return BadRequest(ApiResponse<TourRoute>.Fail("Thoi luong tour phai lon hon 0 phut."));
        }

        if (request.StopPoiIds is null || request.StopPoiIds.Count == 0)
        {
            return BadRequest(ApiResponse<TourRoute>.Fail("Tour phai co it nhat mot diem den."));
        }

        if (actor.IsPlaceOwner && request.StopPoiIds.Any(stopPoiId => !repository.GetPois(actor).Any(item => item.Id == stopPoiId)))
        {
            return NotFound(ApiResponse<TourRoute>.Fail("Chu quan chi duoc tao tour bang cac POI cua quan minh."));
        }

        var saved = repository.SaveRoute(null, request with
        {
            ActorName = actor.Name,
            ActorRole = actor.Role,
            ActorUserId = actor.UserId
        });

        return Ok(ApiResponse<TourRoute>.Ok(saved, "Tao tour thanh cong."));
    }

    [HttpPut("{id}")]
    public ActionResult<ApiResponse<TourRoute>> UpdateTour(string id, [FromBody] TourRouteUpsertRequest request)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var existing = repository.GetRoutes(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            return NotFound(ApiResponse<TourRoute>.Fail("Khong tim thay tour."));
        }

        if (actor.IsPlaceOwner &&
            (!string.Equals(existing.OwnerUserId, actor.UserId, StringComparison.OrdinalIgnoreCase) || existing.IsSystemRoute))
        {
            return NotFound(ApiResponse<TourRoute>.Fail("Khong tim thay tour."));
        }

        var saved = repository.SaveRoute(id, request with
        {
            ActorName = actor.Name,
            ActorRole = actor.Role,
            ActorUserId = actor.UserId
        });

        return Ok(ApiResponse<TourRoute>.Ok(saved, "Cap nhat tour thanh cong."));
    }

    [HttpDelete("{id}")]
    public ActionResult<ApiResponse<string>> DeleteTour(string id)
    {
        var actor = adminRequestContextResolver.RequireAuthenticatedAdmin();
        var existing = repository.GetRoutes(actor).FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            return NotFound(ApiResponse<string>.Fail("Khong tim thay tour."));
        }

        if (actor.IsPlaceOwner &&
            (!string.Equals(existing.OwnerUserId, actor.UserId, StringComparison.OrdinalIgnoreCase) || existing.IsSystemRoute))
        {
            return NotFound(ApiResponse<string>.Fail("Khong tim thay tour."));
        }

        var deleted = repository.DeleteRoute(id, actor);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa tour thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay tour."));
    }
}
