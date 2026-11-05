namespace MedFlow.Application.DTOs.HospitalAdmin;

public sealed record HospitalAdminStatsDto(
    int ActiveDepartments,
    int TotalDepartments,
    int ActiveRooms,
    int TotalRooms,
    int ActiveDoctors,
    int OnlineServicesCount,
    int OpenSlotsToday,
    int ReviewSlotsCount
);
