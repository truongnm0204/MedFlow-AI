namespace MedFlow.Application.DTOs.Departments;

public sealed record DepartmentItemDto(
    long Id,
    string Code,
    string Name,
    long? HospitalId,
    string Facility,
    bool IsActive,
    string Status
);

