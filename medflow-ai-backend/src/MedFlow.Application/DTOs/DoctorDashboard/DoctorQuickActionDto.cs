namespace MedFlow.Application.DTOs.DoctorDashboard;

public sealed record DoctorQuickActionDto(
    string Id,
    string Label,
    string Description,
    string UcCode,
    string Shortcut,
    string Route,
    string Icon,
    string Accent
);
