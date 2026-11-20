using System.Security.Claims;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/pharmacy/prescriptions")]
[Authorize(Roles = "PHARMACY_STAFF")]
public sealed class PharmacyPrescriptionsController(
    IPrescriptionSearchService service,
    IConfirmMedicineDispensingService dispensingService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PrescriptionSearchResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PrescriptionSearchResponseDto>> Search(
        [FromQuery] GetPrescriptionSearchRequest request, CancellationToken ct)
        => Ok(await service.SearchAsync(GetUserId(), request, ct, GetIp()));

    [HttpGet("{idOrCode}")]
    [ProducesResponseType<PrescriptionSearchDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PrescriptionSearchDetailDto>> GetById(
        string idOrCode, CancellationToken ct)
        => Ok(await service.GetByIdAsync(GetUserId(), idOrCode, ct, GetIp()));

    /// <summary>
    /// GET /api/v1/pharmacy/prescriptions/{idOrCode}/dispense — Xem thông tin ngữ cảnh bàn giao đơn thuốc (UC46 / SCR-PHA-03).
    /// </summary>
    [HttpGet("{idOrCode}/dispense")]
    [HttpGet("/api/v1/pharmacy/dispense/{idOrCode}")]
    [ProducesResponseType<DispenseConfirmContextDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DispenseConfirmContextDto>> GetDispenseContext(
        string idOrCode, CancellationToken ct)
        => Ok(await dispensingService.GetDispenseContextAsync(GetUserId(), idOrCode, ct, GetIp()));

    /// <summary>
    /// POST /api/v1/pharmacy/prescriptions/{idOrCode}/dispense — Xác nhận bàn giao thuốc, trừ tồn kho và cập nhật đơn thuốc (UC46 / SCR-PHA-03).
    /// </summary>
    [HttpPost("{idOrCode}/dispense")]
    [HttpPost("{idOrCode}/confirm-dispense")]
    [HttpPost("/api/v1/pharmacy/dispenses/confirm")]
    [HttpPost("/api/v1/pharmacy/dispense/confirm")]
    [ProducesResponseType<DispensingResultDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DispensingResultDetailDto>> ConfirmDispense(
        string? idOrCode, [FromBody] ConfirmDispenseRequest request, CancellationToken ct)
    {
        var effectiveIdOrCode = !string.IsNullOrWhiteSpace(idOrCode)
            ? idOrCode
            : (request.PrescriptionIdOrCode ?? string.Empty);

        return Ok(await dispensingService.ConfirmDispenseAsync(GetUserId(), effectiveIdOrCode, request, ct, GetIp()));
    }

    /// <summary>
    /// GET /api/v1/pharmacy/prescriptions/{idOrCode}/dispense-result — Xem biên nhận kết quả cấp phát thuốc (UC46 / SCR-PHA-04).
    /// </summary>
    [HttpGet("{idOrCode}/dispense-result")]
    [HttpGet("/api/v1/pharmacy/dispensing/{idOrCode}")]
    [ProducesResponseType<DispensingResultDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DispensingResultDetailDto>> GetDispensingResult(
        string idOrCode, CancellationToken ct)
        => Ok(await dispensingService.GetDispensingResultAsync(GetUserId(), idOrCode, ct, GetIp()));
    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!long.TryParse(sub, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }
}
