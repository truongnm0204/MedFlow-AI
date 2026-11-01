using MedFlow.Application.DTOs.Appointments;

namespace MedFlow.Application.Services.Appointments;

public interface IAppointmentBookingService
{
    Task<CreateAppointmentResponse> CreateAsync(
        long userId,
        CreateAppointmentRequest request,
        string? ipAddress,
        CancellationToken ct = default);
}
