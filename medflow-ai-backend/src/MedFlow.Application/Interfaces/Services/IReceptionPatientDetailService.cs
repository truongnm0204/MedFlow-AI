using MedFlow.Application.DTOs.Reception;

namespace MedFlow.Application.Interfaces.Services;

public interface IReceptionPatientDetailService
{
    Task<PatientDetailResponseDto> GetPatientDetailAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default);
}
