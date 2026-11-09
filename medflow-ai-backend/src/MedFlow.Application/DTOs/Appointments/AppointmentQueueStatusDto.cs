namespace MedFlow.Application.DTOs.Appointments;

public sealed record AppointmentQueueStatusDto(
    string AppointmentId,
    string AppointmentCode,
    string VisitCode,
    string QueueNumber,
    string Status,
    string StatusLabel,
    string RoomName,
    string DoctorName,
    string DepartmentName,
    string? ServingQueueNumber,
    string? NextQueueNumber,
    string? EstimatedWaitLabel,
    IReadOnlyList<QueueVisitStepDto> VisitSteps,
    string GuidanceNote);

public sealed record QueueVisitStepDto(
    string Key,
    string Title,
    string State,
    string? StatusLabel,
    string Note);
