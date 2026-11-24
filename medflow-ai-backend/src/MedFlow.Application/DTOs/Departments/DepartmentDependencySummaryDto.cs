namespace MedFlow.Application.DTOs.Departments;

public sealed record DepartmentDependencySummaryDto(
    int ActiveRoomsCount,
    int ActiveDoctorsCount,
    int ActiveServicesCount,
    int PendingAppointmentsCount,
    bool CanDeactivate,
    string? BlockingReason = null
);
