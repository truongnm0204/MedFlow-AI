namespace MedFlow.Application.DTOs.HospitalAdmin;

public sealed record GetHospitalAdminDashboardRequest(
    int WorklistLimit = 20,
    string? Category = null,
    string? Status = null,
    string? Keyword = null
);
