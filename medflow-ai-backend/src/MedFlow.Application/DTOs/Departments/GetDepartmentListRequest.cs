namespace MedFlow.Application.DTOs.Departments;

public sealed record GetDepartmentListRequest(
    string? Keyword = null,
    long? HospitalId = null,
    string? Facility = null,
    bool? IsActive = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 10
);

