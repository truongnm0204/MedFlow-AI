using MedFlow.Application.DTOs.Appointments;

namespace MedFlow.Application.Services.Appointments;

public interface IAppointmentDetailService
{
    Task<AppointmentDetailDto> GetMyAppointmentDetailAsync(
        long userId,
        string? idOrCode,
        CancellationToken ct = default);

    Task<AppointmentDetailDto> UpdateReasonAsync(
        long userId,
        string? idOrCode,
        UpdateAppointmentReasonRequest request,
        CancellationToken ct = default);
}
