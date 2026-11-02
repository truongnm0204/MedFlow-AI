namespace MedFlow.Application.DTOs.Appointments;

public sealed record ReviewFeeLineItemDto(
    string Id,
    string Title,
    string Description,
    decimal Amount,
    bool? IsIncludedFree,
    string? FreeBadgeLabel);
