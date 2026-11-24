namespace MedFlow.Application.DTOs.Departments;

public sealed record DepartmentItemDto(
    long Id,
    string Code,
    string Name,
    long? HospitalId,
    string Facility,
    bool IsActive,
    string Status,
    int? ActiveRoomsCount = null,
    int? ActiveDoctorsCount = null,
    int? ActiveServicesCount = null,
    int? PendingAppointmentsCount = null,
    bool? HasActiveDependencies = null
);

