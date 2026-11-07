using MedFlow.Application.DTOs.Reception;

namespace MedFlow.Application.Interfaces.Services;

public interface IUpdatePatientAtReceptionService
{
    Task<UpdatePatientResponseDto> UpdatePatientAsync(
        string idOrCode,
        UpdatePatientAtReceptionRequest request,
        CancellationToken ct = default);
}
