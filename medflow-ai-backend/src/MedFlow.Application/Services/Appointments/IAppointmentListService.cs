using MedFlow.Application.DTOs.Appointments;

namespace MedFlow.Application.Services.Appointments;

public interface IAppointmentListService
{
    Task<AppointmentListResponse> GetMyAppointmentsAsync(
        long userId,
        GetMyAppointmentsRequest request,
        CancellationToken ct = default);
}
