namespace MedFlow.Application.DTOs.Appointments;

public sealed record CreateAppointmentResponse(
    string AppointmentId,
    string AppointmentCode,
    string QueueNumber,
    string Status);
