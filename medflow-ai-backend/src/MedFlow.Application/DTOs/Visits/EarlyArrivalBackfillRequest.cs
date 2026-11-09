namespace MedFlow.Application.DTOs.Visits;

public sealed record EarlyArrivalBackfillRequest(
    string? ServicePointId,
    string? ActionMode,
    string? ArrivalClass,
    string? Reason,
    string? Notes,
    bool AcknowledgeAppointmentProtection);
