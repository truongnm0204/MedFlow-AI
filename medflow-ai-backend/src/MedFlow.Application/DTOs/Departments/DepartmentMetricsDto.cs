namespace MedFlow.Application.DTOs.Departments;

public sealed record DepartmentMetricsDto(
    int Total,
    int Active,
    int Inactive,
    int Facilities
);

