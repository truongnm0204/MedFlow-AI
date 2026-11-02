using MedFlow.Application.DTOs.Hospitals;
using MedFlow.Application.Services.Hospitals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/hospitals")]
[AllowAnonymous]
public sealed class HospitalsController : ControllerBase
{
    private readonly IHospitalCatalogService _hospitals;

    public HospitalsController(IHospitalCatalogService hospitals)
    {
        _hospitals = hospitals;
    }

    /// <summary>GET /api/v1/hospitals — Danh sách cơ sở khám bệnh ngoại trú.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<HospitalDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HospitalDto>>> List(CancellationToken ct)
        => Ok(await _hospitals.ListAsync(ct));
}
