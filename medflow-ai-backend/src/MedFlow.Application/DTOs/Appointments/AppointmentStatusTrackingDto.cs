namespace MedFlow.Application.DTOs.Appointments;

public sealed record AppointmentStatusTrackingDto(
    string AppointmentId,
    string AppointmentCode,
    string PatientName,
    string PatientRelationshipLabel,
    string Status,
    string StatusLabel,
    string ScheduledDateFullLabel,
    string ScheduledTimeRange,
    string DoctorName,
    string DepartmentName,
    string RoomName,
    string? QueueNumber,
    IReadOnlyList<AppointmentStatusStepDto> Steps,
    AppointmentNextStepDto NextStep);

public sealed record AppointmentStatusStepDto(
    string Key,
    string Title,
    string State,
    string? OccurredAt);

public sealed record AppointmentNextStepDto(
    string Code,
    string Title,
    string Description,
    string? DueAt);
