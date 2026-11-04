namespace MedFlow.Application.DTOs.DoctorDashboard;

public sealed record DoctorStatsDto(
    int AssignedWaiting,
    int InConsultation,
    int PendingLabResults,
    int CompletedToday
);
