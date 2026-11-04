namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record FefoBatchAlertItemDto(
    string Id,
    string MedicineName,
    string BatchCode,
    string ExpiryDate,
    string AvailableStock,
    string Status
);
