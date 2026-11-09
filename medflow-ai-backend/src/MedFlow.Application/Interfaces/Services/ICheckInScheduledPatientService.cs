using MedFlow.Application.DTOs.Reception;

namespace MedFlow.Application.Interfaces.Services;

public interface ICheckInScheduledPatientService
{
    Task<CheckInScheduledPatientResponseDto> CheckInScheduledPatientAsync(
        long actorUserId,
        string appointmentIdOrCode,
        CheckInScheduledPatientRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<CheckInScheduledPatientResponseDto> GetCheckInResultByAppointmentAsync(
        long actorUserId,
        string appointmentIdOrCode,
        CancellationToken ct = default);

    Task<CheckInScheduledPatientResponseDto> GetCheckInResultByVisitAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default);
}
