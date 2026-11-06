namespace MedFlow.Application.DTOs.Visits;

public sealed record EarlyArrivalBackfillResponseDto(
    string QueueEntryId,
    string TicketNo,
    int QueueNumber,
    string VisitCode,
    string PatientCode,
    string PatientName,
    string ServicePointId,
    string RoomCode,
    string RoomName,
    string PreviousStatus,
    string CurrentStatus,
    string ArrivalClass,
    int ServingOrder,
    bool IsBackfilled,
    bool AppointmentProtected,
    string? BlockedReason,
    string Reason,
    string? Notes,
    int StateVersion,
    DateTime UpdatedAtUtc,
    ExaminationQueueEntryDto UpdatedEntry);
