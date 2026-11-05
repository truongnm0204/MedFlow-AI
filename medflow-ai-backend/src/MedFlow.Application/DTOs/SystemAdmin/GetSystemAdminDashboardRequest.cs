namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record GetSystemAdminDashboardRequest(
    int RecentAuditLimit = 10,
    long? HospitalId = null
);
