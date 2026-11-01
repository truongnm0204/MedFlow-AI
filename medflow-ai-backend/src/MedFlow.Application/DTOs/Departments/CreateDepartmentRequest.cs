namespace MedFlow.Application.DTOs.Departments;

public sealed record CreateDepartmentRequest(
    string Code,
    string Name,
    long? HospitalId = null,
    bool? IsActive = true
);
