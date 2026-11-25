namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record CancelDiagnosticOrderRequest(
    string? Reason = null,
    bool? TechnicalAbort = null,
    string? CancelDisposition = null,
    int? ExpectedStateVersion = null);

public sealed record CancelDiagnosticOrderResponseDto(
    string ActionOutcome,
    string Message,
    long OrderId,
    string OrderCode,
    long? VisitId,
    string? VisitCode,
    string OrderStatus,
    string? QueueStatus,
    string? ChargeStatus,
    bool WasAlreadyPaid = false,
    int ExpiredPendingPaymentsCount = 0,
    bool TransitionedToReturnConsultation = false,
    string? VisitStatus = null,
    string? PreviousOrderStatus = null,
    long? QueueEntryId = null,
    string? CancelDisposition = null,
    string? Reason = null,
    DateTime? CancelledAtUtc = null);
