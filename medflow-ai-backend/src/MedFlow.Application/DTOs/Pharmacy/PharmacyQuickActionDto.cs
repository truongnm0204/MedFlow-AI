namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record PharmacyQuickActionDto(
    string Id,
    string Label,
    string Description,
    string Shortcut,
    string Icon,
    string Accent
);
