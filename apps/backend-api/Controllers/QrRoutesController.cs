using Microsoft.AspNetCore.Mvc;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Infrastructure;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class QrRoutesController(AdminDataRepository repository) : ControllerBase
{
    [HttpGet("qr-codes")]
    public ActionResult<ApiResponse<IReadOnlyList<QRCodeRecord>>> GetQrCodes([FromQuery] bool? isActive)
    {
        IEnumerable<QRCodeRecord> query = repository.GetQrCodes();

        if (isActive.HasValue)
        {
            query = query.Where(item => item.IsActive == isActive.Value);
        }

        return Ok(ApiResponse<IReadOnlyList<QRCodeRecord>>.Ok(query.ToList()));
    }

    [HttpPatch("qr-codes/{id}/state")]
    public ActionResult<ApiResponse<QRCodeRecord>> UpdateQrCodeState(string id, [FromBody] QrCodeStateRequest request)
    {
        var updated = repository.UpdateQrState(id, request);
        return updated is null
            ? NotFound(ApiResponse<QRCodeRecord>.Fail("Khong tim thay QR code."))
            : Ok(ApiResponse<QRCodeRecord>.Ok(updated, "Cap nhat trang thai QR thanh cong."));
    }

    [HttpPatch("qr-codes/{id}/image")]
    public ActionResult<ApiResponse<QRCodeRecord>> UpdateQrCodeImage(string id, [FromBody] QrCodeImageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.QrImageUrl))
        {
            return BadRequest(ApiResponse<QRCodeRecord>.Fail("QrImageUrl la bat buoc."));
        }

        var updated = repository.UpdateQrImage(id, request);
        return updated is null
            ? NotFound(ApiResponse<QRCodeRecord>.Fail("Khong tim thay QR code."))
            : Ok(ApiResponse<QRCodeRecord>.Ok(updated, "Cap nhat anh QR thanh cong."));
    }

    [HttpGet("routes")]
    public ActionResult<ApiResponse<IReadOnlyList<TourRoute>>> GetRoutes()
        => Ok(ApiResponse<IReadOnlyList<TourRoute>>.Ok(repository.GetRoutes()));

    [HttpPost("routes")]
    public ActionResult<ApiResponse<TourRoute>> CreateRoute([FromBody] TourRouteUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<TourRoute>.Fail("Ten tuyen tham quan la bat buoc."));
        }

        var saved = repository.SaveRoute(null, request);
        return Ok(ApiResponse<TourRoute>.Ok(saved, "Tao tuyen tham quan thanh cong."));
    }

    [HttpPut("routes/{id}")]
    public ActionResult<ApiResponse<TourRoute>> UpdateRoute(string id, [FromBody] TourRouteUpsertRequest request)
    {
        var existing = repository.GetRoutes().Any(item => item.Id == id);
        if (!existing)
        {
            return NotFound(ApiResponse<TourRoute>.Fail("Khong tim thay tuyen tham quan."));
        }

        var saved = repository.SaveRoute(id, request);
        return Ok(ApiResponse<TourRoute>.Ok(saved, "Cap nhat tuyen tham quan thanh cong."));
    }

    [HttpDelete("routes/{id}")]
    public ActionResult<ApiResponse<string>> DeleteRoute(string id)
    {
        var deleted = repository.DeleteRoute(id);
        return deleted
            ? Ok(ApiResponse<string>.Ok(id, "Xoa tuyen tham quan thanh cong."))
            : NotFound(ApiResponse<string>.Fail("Khong tim thay tuyen tham quan."));
    }
}
