using MedFlow.Application.DTOs.Appointments;

namespace MedFlow.Application.Services.Appointments;

public interface IAppointmentRescheduleService
{
    Task<AppointmentDetailDto> RescheduleAsync(
        long userId,
        string? idOrCode,
        RescheduleAppointmentRequest request,
        string? ipAddress,
        CancellationToken ct = default);
}
