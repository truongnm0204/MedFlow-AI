namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record StaffAccountMetricsDto(
    int Total,
    int Active,
    int Locked,
    int Disabled,
    int Pending
);
