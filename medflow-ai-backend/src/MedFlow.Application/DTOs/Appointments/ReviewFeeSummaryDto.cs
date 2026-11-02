namespace MedFlow.Application.DTOs.Appointments;

public sealed record ReviewFeeSummaryDto(
    string ServiceTypeBadge,
    IReadOnlyList<ReviewFeeLineItemDto> Items,
    decimal TotalAmount,
    string TotalDisclaimer,
    string PaymentMethodNote);
