using MedFlow.Application.DTOs.Appointments;

namespace MedFlow.Application.Services.Appointments;

public interface IAppointmentQueueTrackingService
{
    Task<AppointmentQueueStatusDto> GetMyAppointmentQueueStatusAsync(
        long userId,
        string? idOrCode,
        CancellationToken ct = default);
}
