using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/reception/patients/{idOrCode}")]
[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class ReceptionUpdatePatientController : ControllerBase
{
    private readonly IUpdatePatientAtReceptionService _service;

    public ReceptionUpdatePatientController(IUpdatePatientAtReceptionService service)
    {
        _service = service;
    }

    /// <summary>
    /// PUT /api/v1/reception/patients/{idOrCode} — Cập nhật hồ sơ bệnh nhân tại quầy tiếp đón.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<UpdatePatientResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdatePatientResponseDto>> UpdatePatient(
        [FromRoute] string idOrCode,
        [FromBody] UpdatePatientAtReceptionRequest request,
        CancellationToken ct)
    {
        var result = await _service.UpdatePatientAsync(idOrCode, request, ct);
        return Ok(result);
    }
}
