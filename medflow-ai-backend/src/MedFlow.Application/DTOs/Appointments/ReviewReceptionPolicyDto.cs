namespace MedFlow.Application.DTOs.Appointments;

public sealed record ReviewReceptionPolicyDto(
    string BadgeLabel,
    string RoomNumber,
    string ReceptionCounterLabel,
    int EarlyArrivalMinutes,
    string CheckInDeadlineTime,
    int LateGraceMinutes);
