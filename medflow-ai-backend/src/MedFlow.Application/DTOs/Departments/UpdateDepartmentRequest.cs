namespace MedFlow.Application.DTOs.Departments;

public sealed record UpdateDepartmentRequest(
    string Code,
    string Name,
    long? HospitalId = null,
    bool? IsActive = null
);
