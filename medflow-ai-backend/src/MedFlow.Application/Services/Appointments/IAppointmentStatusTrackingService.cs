using MedFlow.Application.DTOs.Appointments;

namespace MedFlow.Application.Services.Appointments;

public interface IAppointmentStatusTrackingService
{
    Task<AppointmentStatusTrackingDto> GetMyAppointmentStatusAsync(
        long userId,
        string? idOrCode,
        CancellationToken ct = default);
}
