namespace MedFlow.Application.DTOs.HospitalAdmin;

public sealed record HospitalAdminStatsDto(
    int ActiveDepartments,
    int TotalDepartments,
    int ActiveRooms,
    int TotalRooms,
    int ActiveDoctors,
    int OnlineServicesCount,
    int OpenSlotsToday,
    int ReviewSlotsCount,
    int NewCheckInsToday = 0,
    int ResumedVisitsToday = 0,
    int AwaitingResultsOrReturnCount = 0,
    int CompletedVisitsToday = 0
);
