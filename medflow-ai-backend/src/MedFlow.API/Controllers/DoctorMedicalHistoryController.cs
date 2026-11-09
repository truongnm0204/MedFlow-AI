using System.Security.Claims;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "DOCTOR")]
public sealed class DoctorMedicalHistoryController : ControllerBase
{
    private readonly IMedicalHistoryService _medicalHistoryService;

    public DoctorMedicalHistoryController(IMedicalHistoryService medicalHistoryService)
    {
        _medicalHistoryService = medicalHistoryService;
    }

    /// <summary>
    /// GET /api/v1/doctor/patients/{patientIdOrCode}/medical-history — Xem lịch sử khám bệnh lâm sàng theo bệnh nhân (UC32).
    /// </summary>
    [HttpGet("api/v1/doctor/patients/{patientIdOrCode}/medical-history")]
    [HttpGet("api/v1/patients/{patientIdOrCode}/medical-history")]
    [HttpGet("api/v1/consultation/patients/{patientIdOrCode}/medical-history")]
    [HttpGet("api/v1/doctor/medical-history/patients/{patientIdOrCode}")]
    [ProducesResponseType<PatientMedicalHistoryResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientMedicalHistoryResponseDto>> GetByPatient(
        [FromRoute] string patientIdOrCode,
        [FromQuery] string? visitIdOrCode = null,
        [FromQuery] string? status = null,
        [FromQuery] string? specialtyCode = null,
        [FromQuery] long? departmentId = null,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null,
        [FromQuery] string? keyword = null,
        [FromQuery] bool includeCurrentVisit = true,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var request = new GetPatientMedicalHistoryRequest(
            VisitIdOrCode: visitIdOrCode,
            Status: status,
            SpecialtyCode: specialtyCode,
            DepartmentId: departmentId,
            FromDate: fromDate,
            ToDate: toDate,
            Keyword: keyword,
            IncludeCurrentVisit: includeCurrentVisit,
            Limit: limit);

        var result = await _medicalHistoryService.GetByPatientAsync(
            GetUserId(),
            patientIdOrCode,
            request,
            GetIp(),
            ct);

        return Ok(result);
    }

    /// <summary>
    /// GET /api/v1/doctor/visits/{visitIdOrCode}/medical-history — Xem lịch sử khám bệnh lâm sàng từ ngữ cảnh lượt khám hiện tại (UC32).
    /// </summary>
    [HttpGet("api/v1/doctor/visits/{visitIdOrCode}/medical-history")]
    [HttpGet("api/v1/visits/{visitIdOrCode}/medical-history")]
    [HttpGet("api/v1/consultation/visits/{visitIdOrCode}/medical-history")]
    [HttpGet("api/v1/doctor/consultations/{visitIdOrCode}/medical-history")]
    [ProducesResponseType<PatientMedicalHistoryResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientMedicalHistoryResponseDto>> GetByVisit(
        [FromRoute] string visitIdOrCode,
        [FromQuery] string? status = null,
        [FromQuery] string? specialtyCode = null,
        [FromQuery] long? departmentId = null,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null,
        [FromQuery] string? keyword = null,
        [FromQuery] bool includeCurrentVisit = true,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var request = new GetPatientMedicalHistoryRequest(
            VisitIdOrCode: visitIdOrCode,
            Status: status,
            SpecialtyCode: specialtyCode,
            DepartmentId: departmentId,
            FromDate: fromDate,
            ToDate: toDate,
            Keyword: keyword,
            IncludeCurrentVisit: includeCurrentVisit,
            Limit: limit);

        var result = await _medicalHistoryService.GetByVisitAsync(
            GetUserId(),
            visitIdOrCode,
            request,
            GetIp(),
            ct);

        return Ok(result);
    }

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
