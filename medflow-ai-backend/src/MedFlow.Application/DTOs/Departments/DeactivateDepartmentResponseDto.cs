namespace MedFlow.Application.DTOs.Departments;

public sealed record DeactivateDepartmentResponseDto(
    long Id,
    string Code,
    string Name,
    long? HospitalId,
    string Facility,
    bool IsActive,
    string Status,
    string Message,
    DepartmentDependencySummaryDto? Dependencies = null
);
