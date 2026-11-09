using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/reception/patients")]
[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class ReceptionCreatePatientController : ControllerBase
{
    private readonly ICreatePatientAtReceptionService _service;

    public ReceptionCreatePatientController(ICreatePatientAtReceptionService service)
    {
        _service = service;
    }

    /// <summary>POST /api/v1/reception/patients — Tạo hồ sơ bệnh nhân tại quầy tiếp đón.</summary>
    [HttpPost]
    [ProducesResponseType<CreatePatientResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatePatientResponseDto>> Create(
        [FromBody] CreatePatientAtReceptionRequest request,
        CancellationToken ct)
    {
        var result = await _service.CreatePatientAsync(request, ct);
        return Created($"/api/v1/reception/patients/{result.Id}", result);
    }
}
