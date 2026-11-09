using MedFlow.Application.DTOs.Reception;

namespace MedFlow.Application.Interfaces.Services;

public interface ICreatePatientAtReceptionService
{
    Task<CreatePatientResponseDto> CreatePatientAsync(CreatePatientAtReceptionRequest request, CancellationToken ct = default);
}
