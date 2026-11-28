using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.DTOs.Payments;

/// <remarks>
/// D2 / BR-BF02-06: client KHÔNG còn chọn chỉ định, điểm hay thứ tự cận lâm sàng để tự lấy số.
/// Sau khi thanh toán, hệ thống tự chọn chỉ định CLS kế tiếp hợp lệ và tự resolver điểm thực hiện.
/// </remarks>
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
    IReadOnlyList<PaymentAllocationItemDto> Allocations,
    string EnrollmentStatus = "NOT_REQUESTED",
    string? EnrollmentOutcomeCode = null,
    string? EnrollmentOutcomeMessage = null,
    VisitQueueEnrollmentTicketDto? EnrolledQueueEntry = null);
