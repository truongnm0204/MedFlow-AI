namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record GetPharmacyDashboardRequest(
    int QueueLimit = 20,
    string? Status = null,
    string? Keyword = null
);
