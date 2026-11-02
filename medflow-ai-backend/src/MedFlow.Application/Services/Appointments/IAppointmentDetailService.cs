using MedFlow.Application.DTOs.Appointments;

namespace MedFlow.Application.Services.Appointments;

public interface IAppointmentDetailService
{
    Task<AppointmentDetailDto> GetMyAppointmentDetailAsync(
        long userId,
        string? idOrCode,
        CancellationToken ct = default);
}
