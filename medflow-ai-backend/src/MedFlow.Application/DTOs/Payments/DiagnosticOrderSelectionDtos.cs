namespace MedFlow.Application.DTOs.Payments;

public sealed record UpdateDiagnosticOrderSelectionRequestDto(
    IReadOnlyList<long>? AcceptedOrderIds = null,
    IReadOnlyList<long>? UndecidedOrderIds = null,
    IReadOnlyList<long>? ReviewRequestedOrderIds = null,
    string? Reason = null);

public sealed record DeclineDiagnosticOrderRequestDto(
    string? Reason = null,
    bool? ConfirmAbandonPaid = null);

public sealed record DeclineDiagnosticOrderResponseDto(
    long VisitId,
    string? VisitCode,
    string? VisitStatus,
    long OrderId,
    string? OrderCode,
    string OrderStatus,
    string SelectionStatus,
    long? VisitChargeId,
    string? ChargeStatus,
    bool WasAlreadyPaid,
    int CancelledPendingQrCount,
    bool TransitionedToReturnConsultation,
    VisitChargesResponseDto ChargesSnapshot,
    bool RefusalRecorded = false,
    bool DoctorNotified = false,
    long? NotifiedDoctorId = null);
