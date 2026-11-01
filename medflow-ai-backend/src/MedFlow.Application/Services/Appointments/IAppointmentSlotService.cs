using MedFlow.Application.DTOs.Appointments;

namespace MedFlow.Application.Services.Appointments;

public interface IAppointmentSlotService
{
    Task<AvailableSlotsResponse> GetAvailableSlotsAsync(
        long userId,
        GetAvailableSlotsRequest request,
        CancellationToken ct = default);

    Task<ServiceShiftsResponse> GetServiceShiftsAsync(
        long userId,
        GetServiceShiftsRequest request,
        CancellationToken ct = default);
}
