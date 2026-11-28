namespace MedFlow.Application.DTOs.Payments;

/// <remarks>
/// D2 / BR-BF02-06: client KHÔNG còn chọn chỉ định, điểm hay thứ tự cận lâm sàng để tự lấy số.
/// Sau khi thanh toán, hệ thống tự chọn chỉ định CLS kế tiếp hợp lệ và tự resolver điểm thực hiện.
/// </remarks>
public sealed record GenerateQrPaymentRequestDto(
    long? VisitChargeId = null,
    IReadOnlyList<long>? VisitChargeIds = null,
    string? IdempotencyKey = null);
