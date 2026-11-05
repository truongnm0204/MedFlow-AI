namespace MedFlow.Application.DTOs.HospitalAdmin;

public sealed record HospitalConfigWorklistItemDto(
    string Id,
    string Code,
    string Name,
    string Category,
    string DepartmentOrScope,
    string MetricLabel,
    string UpdatedAt,
    string Status,
    string Route
);
