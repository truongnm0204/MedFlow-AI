using System.Security.Claims;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "DOCTOR")]
public sealed class DoctorConsultationController : ControllerBase
{
    private readonly IConsultationWorkspaceService _consultationWorkspaceService;

    public DoctorConsultationController(IConsultationWorkspaceService consultationWorkspaceService)
    {
        _consultationWorkspaceService = consultationWorkspaceService;
    }

    /// <summary>
    /// GET /api/v1/doctor/consultation/{idOrCode} — Xem không gian khám lâm sàng của lượt khám (UC31 / #106).
    /// </summary>
    [HttpGet("/api/v1/doctor/consultation/{idOrCode}")]
    [HttpGet("/api/v1/doctor/visits/{idOrCode}/consultation")]
    [HttpGet("/api/v1/visits/{idOrCode}/consultation")]
    [ProducesResponseType<ConsultationWorkspaceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationWorkspaceResponseDto>> GetWorkspace(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.GetWorkspaceAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// POST /api/v1/doctor/consultation/{idOrCode}/open — Mở phiên khám lâm sàng (CALLED -> IN_PROGRESS) và khởi tạo hồ sơ khám nháp (UC31 / #106).
    /// </summary>
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/open")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/open-consultation")]
    [HttpPost("/api/v1/visits/{idOrCode}/open-consultation")]
    [ProducesResponseType<ConsultationWorkspaceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationWorkspaceResponseDto>> OpenWorkspace(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] OpenConsultationWorkspaceRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.OpenWorkspaceAsync(GetUserId(), idOrCode, request, GetIp(), ct));

    /// <summary>
    /// POST/PUT /api/v1/doctor/consultation/{idOrCode}/clinical-examination — Ghi nhận triệu chứng, sinh hiệu và khám lâm sàng (UC33 / #110).
    /// </summary>
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/clinical-examination")]
    [HttpPut("/api/v1/doctor/consultation/{idOrCode}/clinical-examination")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/clinical-examination")]
    [HttpPut("/api/v1/doctor/visits/{idOrCode}/clinical-examination")]
    [HttpPost("/api/v1/visits/{idOrCode}/clinical-examination")]
    [HttpPut("/api/v1/visits/{idOrCode}/clinical-examination")]
    [ProducesResponseType<ConsultationWorkspaceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationWorkspaceResponseDto>> RecordClinicalExamination(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RecordClinicalExaminationRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.RecordClinicalExaminationAsync(
            GetUserId(),
            idOrCode,
            request!,
            GetIp(),
            ct));

    /// <summary>
    /// POST/PUT /api/v1/doctor/consultation/{idOrCode}/diagnosis — Ghi nhận chẩn đoán ICD-10 và kết luận lâm sàng (UC36 / #112).
    /// </summary>
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/diagnosis")]
    [HttpPut("/api/v1/doctor/consultation/{idOrCode}/diagnosis")]
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/diagnoses")]
    [HttpPut("/api/v1/doctor/consultation/{idOrCode}/diagnoses")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/diagnosis")]
    [HttpPut("/api/v1/doctor/visits/{idOrCode}/diagnosis")]
    [HttpPost("/api/v1/visits/{idOrCode}/diagnosis")]
    [HttpPut("/api/v1/visits/{idOrCode}/diagnosis")]
    [ProducesResponseType<ConsultationWorkspaceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationWorkspaceResponseDto>> RecordDiagnosis(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RecordDiagnosisRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.RecordDiagnosisAsync(
            GetUserId(),
            idOrCode,
            request!,
            GetIp(),
            ct));

    /// <summary>
    /// GET /api/v1/doctor/icd10-codes — Tra cứu danh mục mã bệnh ICD-10 (UC36 / #112).
    /// </summary>
    [HttpGet("/api/v1/doctor/icd10-codes")]
    [HttpGet("/api/v1/doctor/consultation/icd10-codes")]
    [HttpGet("/api/v1/icd10-codes")]
    [ProducesResponseType<IReadOnlyList<Icd10CatalogItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<Icd10CatalogItemDto>>> SearchIcd10Codes(
        [FromQuery] string? q = null,
        [FromQuery] bool primaryOnly = false,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.SearchIcd10CodesAsync(
            GetUserId(),
            q,
            primaryOnly,
            limit,
            ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId))
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}
