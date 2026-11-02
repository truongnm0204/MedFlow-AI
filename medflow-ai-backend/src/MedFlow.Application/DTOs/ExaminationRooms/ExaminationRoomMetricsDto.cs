namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record ExaminationRoomMetricsDto(
    int Total,
    int Active,
    int Inactive,
    int Departments
);
