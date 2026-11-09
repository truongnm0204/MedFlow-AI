namespace MedFlow.Application.DTOs.Reception;

public sealed record TodayAppointmentSummaryDto(
    long Id,
    string Time,
    string DoctorName,
    string Department,
    string RoomName,
    string Status);
