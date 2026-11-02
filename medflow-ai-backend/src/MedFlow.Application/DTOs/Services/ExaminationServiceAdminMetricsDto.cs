namespace MedFlow.Application.DTOs.Services;

public sealed record ExaminationServiceAdminMetricsDto(
    int Total,
    int Active,
    int Suspended,
    int OnlineBookingCount,
    int DepartmentsCount
);
