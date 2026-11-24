namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record ExaminationRoomDependencySummaryDto(
    long RoomId,
    string RoomCode,
    string RoomName,
    int AssignedDoctorsCount,
    int ActiveVisitsCount,
    int ActiveSlotsCount,
    int PendingAppointmentsCount,
    bool HasActiveDependencies,
    bool CanDeactivate,
    string? BlockingReason = null
);
