using MedFlow.Application.DTOs.Appointments;

namespace MedFlow.Application.Services.Appointments;

public interface IAppointmentCancelService
{
    Task<AppointmentDetailDto> CancelMyAppointmentAsync(
        long userId,
        string? idOrCode,
        CancelAppointmentRequest? request,
        string? ipAddress,
        CancellationToken ct = default);
}
