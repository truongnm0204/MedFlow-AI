namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record GetStaffAccountListRequest(
    string? Keyword = null,
    string? Query = null,
    long? HospitalId = null,
    long? DepartmentId = null,
    string? RoleCode = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 10
);
