using MedFlow.Application.DTOs.Pharmacy;

namespace MedFlow.Application.Interfaces.Services;

public interface IPrescriptionSearchService
{
    Task<PrescriptionSearchResponseDto> SearchAsync(long actorUserId, GetPrescriptionSearchRequest request, CancellationToken ct = default, string? ipAddress = null);
    Task<PrescriptionSearchDetailDto> GetByIdAsync(long actorUserId, string idOrCode, CancellationToken ct = default, string? ipAddress = null);
}
