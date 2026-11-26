namespace MedFlow.Application.DTOs.Payments;

public sealed record CollectCashPaymentRequestDto(
    IReadOnlyList<long>? VisitChargeIds = null,
    long? VisitChargeId = null,
    string? IdempotencyKey = null,
    string? Notes = null);

public sealed record CollectCashPaymentResponseDto(
    long VisitId,
    string? VisitCode,
    long PaymentId,
    string PaymentCode,
    decimal Amount,
    string Currency,
    string Status,
    string PaymentMethod,
    long CollectedByStaffId,
    string? CollectedByStaffName,
    string IdempotencyKey,
    DateTime PaidAt,
    bool IsIdempotentReplay,
    IReadOnlyList<PaymentAllocationItemDto> Allocations);
