using MedFlow.Application.DTOs.Pharmacy;

namespace MedFlow.Application.Interfaces.Services;

public interface IConfirmMedicineDispensingService
{
    Task<DispenseConfirmContextDto> GetDispenseContextAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null);

    Task<DispensingResultDetailDto> ConfirmDispenseAsync(
        long actorUserId,
        string idOrCode,
        ConfirmDispenseRequest request,
        CancellationToken ct = default,
        string? ipAddress = null);

    Task<DispensingResultDetailDto> GetDispensingResultAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null);
}
