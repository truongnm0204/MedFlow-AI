namespace MedFlow.Application.DTOs.DoctorDashboard;

public sealed record DoctorStatsDto(
    int AssignedWaiting,
    int InConsultation,
    int PendingLabResults,
    int CompletedToday,
    int NewCheckInsToday = 0,
    int ResumedVisitsToday = 0,
    int AwaitingResultsOrReturnCount = 0,
    int CompletedVisitsToday = 0
);
