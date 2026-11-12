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
    /// GET /api/v1/doctor/consultation/{idOrCode}/diagnostic-orders — Xem danh mục dịch vụ cận lâm sàng và các phiếu chỉ định hiện có của lượt khám (UC34 / #120).
    /// </summary>
    [HttpGet("/api/v1/doctor/consultation/{idOrCode}/diagnostic-orders")]
    [HttpGet("/api/v1/doctor/consultation/{idOrCode}/diagnostic-services")]
    [HttpGet("/api/v1/doctor/visits/{idOrCode}/diagnostic-orders")]
    [HttpGet("/api/v1/visits/{idOrCode}/diagnostic-orders")]
    [ProducesResponseType<DiagnosticOrderCatalogResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticOrderCatalogResponseDto>> GetDiagnosticOrders(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.GetDiagnosticOrderCatalogAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// POST /api/v1/doctor/consultation/{idOrCode}/diagnostic-orders — Tạo phiếu chỉ định cận lâm sàng và xếp hàng cận lâm sàng cho lượt khám (UC34 / #120).
    /// </summary>
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/diagnostic-orders")]
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/diagnostic-order")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/diagnostic-orders")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/diagnostic-order")]
    [HttpPost("/api/v1/visits/{idOrCode}/diagnostic-orders")]
    [HttpPost("/api/v1/visits/{idOrCode}/diagnostic-order")]
    [ProducesResponseType<ConsultationWorkspaceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationWorkspaceResponseDto>> CreateDiagnosticOrder(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateDiagnosticOrderRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.CreateDiagnosticOrderAsync(
            GetUserId(),
            idOrCode,
            request!,
            GetIp(),
            ct));

    /// <summary>
    /// POST /api/v1/doctor/consultation/{idOrCode}/additional-diagnostic-orders — Tạo phiếu chỉ định cận lâm sàng bổ sung trong phiên khám hoặc khám lại (UC34 / #116 / M3-008).
    /// </summary>
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/additional-diagnostic-orders")]
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/additional-diagnostic-order")]
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/diagnostic-orders/additional")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/additional-diagnostic-orders")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/additional-diagnostic-order")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/diagnostic-orders/additional")]
    [HttpPost("/api/v1/visits/{idOrCode}/additional-diagnostic-orders")]
    [HttpPost("/api/v1/visits/{idOrCode}/additional-diagnostic-order")]
    [HttpPost("/api/v1/visits/{idOrCode}/diagnostic-orders/additional")]
    [ProducesResponseType<ConsultationWorkspaceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationWorkspaceResponseDto>> CreateAdditionalDiagnosticOrder(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateAdditionalDiagnosticOrderRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.CreateAdditionalDiagnosticOrderAsync(
            GetUserId(),
            idOrCode,
            request!,
            GetIp(),
            ct));

    /// <summary>
    /// GET /api/v1/doctor/consultation/{idOrCode}/diagnostic-results — Xem danh sách và lịch sử phiên bản kết quả cận lâm sàng của lượt khám (UC35 / #114).
    /// </summary>
    [HttpGet("/api/v1/doctor/consultation/{idOrCode}/diagnostic-results")]
    [HttpGet("/api/v1/doctor/consultation/{idOrCode}/results")]
    [HttpGet("/api/v1/doctor/visits/{idOrCode}/diagnostic-results")]
    [HttpGet("/api/v1/visits/{idOrCode}/diagnostic-results")]
    [ProducesResponseType<DiagnosticResultsReviewResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticResultsReviewResponseDto>> GetDiagnosticResults(
        string idOrCode,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] bool readyOnly = false,
        [FromQuery] long? orderId = null,
        [FromQuery] string? orderCode = null,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.GetDiagnosticResultsAsync(
            GetUserId(),
            idOrCode,
            new GetDiagnosticResultsRequest(category, status, readyOnly, orderId, orderCode),
            ct));

    /// <summary>
    /// GET /api/v1/doctor/consultation/{idOrCode}/diagnostic-results/{orderIdOrCode} — Xem chi tiết kết quả cận lâm sàng theo mã hoặc ID phiếu chỉ định (UC35 / #114).
    /// </summary>
    [HttpGet("/api/v1/doctor/consultation/{idOrCode}/diagnostic-results/{orderIdOrCode}")]
    [HttpGet("/api/v1/doctor/visits/{idOrCode}/diagnostic-results/{orderIdOrCode}")]
    [HttpGet("/api/v1/visits/{idOrCode}/diagnostic-results/{orderIdOrCode}")]
    [ProducesResponseType<ConsultationDiagnosticResultItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationDiagnosticResultItemDto>> GetDiagnosticResultByOrder(
        string idOrCode,
        string orderIdOrCode,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.GetDiagnosticResultByOrderAsync(
            GetUserId(),
            idOrCode,
            orderIdOrCode,
            ct));

    /// <summary>
    /// GET /api/v1/doctor/diagnostic-results/tracking — Theo dõi mức độ hoàn thành chỉ định/kết quả cận lâm sàng theo lượt khám của bác sĩ (UC35 / #122).
    /// </summary>
    [HttpGet("/api/v1/doctor/diagnostic-results/tracking")]
    [ProducesResponseType<DiagnosticResultsTrackingResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DiagnosticResultsTrackingResponseDto>> GetDiagnosticResultsTracking(
        [FromQuery] string? date = null,
        [FromQuery] string? status = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.GetDiagnosticResultsTrackingAsync(
            GetUserId(),
            new GetDiagnosticResultsTrackingRequest(date, status, keyword, page, pageSize),
            ct));

    /// <summary>
    /// POST/PUT /api/v1/doctor/consultation/{idOrCode}/diagnostic-results/review — Xác nhận xem xét và ghi nhận nhận định lâm sàng đối với kết quả cận lâm sàng (UC35 / #114).
    /// </summary>
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/diagnostic-results/review")]
    [HttpPut("/api/v1/doctor/consultation/{idOrCode}/diagnostic-results/review")]
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/review-diagnostic-results")]
    [HttpPut("/api/v1/doctor/consultation/{idOrCode}/review-diagnostic-results")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/diagnostic-results/review")]
    [HttpPut("/api/v1/doctor/visits/{idOrCode}/diagnostic-results/review")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/review-diagnostic-results")]
    [HttpPut("/api/v1/doctor/visits/{idOrCode}/review-diagnostic-results")]
    [HttpPost("/api/v1/visits/{idOrCode}/diagnostic-results/review")]
    [HttpPut("/api/v1/visits/{idOrCode}/diagnostic-results/review")]
    [HttpPost("/api/v1/visits/{idOrCode}/review-diagnostic-results")]
    [HttpPut("/api/v1/visits/{idOrCode}/review-diagnostic-results")]
    [ProducesResponseType<ConsultationWorkspaceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationWorkspaceResponseDto>> ReviewDiagnosticResults(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReviewDiagnosticResultsRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.ReviewDiagnosticResultsAsync(
            GetUserId(),
            idOrCode,
            request,
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
    /// POST/PUT /api/v1/doctor/consultation/{idOrCode}/prescriptions — Kê đơn thuốc ngoại trú (UC37 / #140).
    /// </summary>
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/prescriptions")]
    [HttpPut("/api/v1/doctor/consultation/{idOrCode}/prescriptions")]
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/prescription")]
    [HttpPut("/api/v1/doctor/consultation/{idOrCode}/prescription")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/prescriptions")]
    [HttpPut("/api/v1/doctor/visits/{idOrCode}/prescriptions")]
    [HttpPost("/api/v1/visits/{idOrCode}/prescriptions")]
    [HttpPut("/api/v1/visits/{idOrCode}/prescriptions")]
    [ProducesResponseType<ConsultationWorkspaceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationWorkspaceResponseDto>> CreatePrescription(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreatePrescriptionRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.CreatePrescriptionAsync(
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

    /// <summary>
    /// POST/PUT /api/v1/doctor/consultation/{idOrCode}/complete — Hoàn tất lượt khám ngoại trú và khóa hồ sơ bệnh án (UC38 / #118).
    /// </summary>
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/complete")]
    [HttpPut("/api/v1/doctor/consultation/{idOrCode}/complete")]
    [HttpPost("/api/v1/doctor/consultation/{idOrCode}/complete-visit")]
    [HttpPut("/api/v1/doctor/consultation/{idOrCode}/complete-visit")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/complete")]
    [HttpPut("/api/v1/doctor/visits/{idOrCode}/complete")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/complete-visit")]
    [HttpPut("/api/v1/doctor/visits/{idOrCode}/complete-visit")]
    [HttpPost("/api/v1/visits/{idOrCode}/complete")]
    [HttpPut("/api/v1/visits/{idOrCode}/complete")]
    [HttpPost("/api/v1/visits/{idOrCode}/complete-visit")]
    [HttpPut("/api/v1/visits/{idOrCode}/complete-visit")]
    [ProducesResponseType<ConsultationWorkspaceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsultationWorkspaceResponseDto>> CompleteVisit(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CompleteVisitRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _consultationWorkspaceService.CompleteVisitAsync(
            GetUserId(),
            idOrCode,
            request,
            GetIp(),
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
