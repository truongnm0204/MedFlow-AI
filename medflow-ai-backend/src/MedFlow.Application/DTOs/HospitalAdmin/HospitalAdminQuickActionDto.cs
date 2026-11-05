namespace MedFlow.Application.DTOs.HospitalAdmin;

public sealed record HospitalAdminQuickActionDto(
    string Id,
    string Label,
    string Description,
    string UcCode,
    string Shortcut,
    string Route,
    string Accent,
    string Icon
);
