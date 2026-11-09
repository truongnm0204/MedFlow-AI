namespace MedFlow.Application.DTOs.Visits;

public sealed record UpdateQueuePriorityResponseDto(
    long QueueEntryId,
    long? VisitId,
    string? VisitCode,
    string TicketNo,
    int QueueNumber,
    string QueueType,
    string CurrentStatus,
    string PreviousPriority,
    string Priority,
    bool VerificationRequired,
    bool VerificationConfirmed,
    string Reason,
    int PreviousStateVersion,
    int StateVersion,
    long UpdatedByUserId,
    string? UpdatedByStaffCode,
    string UpdatedByName,
    DateTime UpdatedAtUtc);
