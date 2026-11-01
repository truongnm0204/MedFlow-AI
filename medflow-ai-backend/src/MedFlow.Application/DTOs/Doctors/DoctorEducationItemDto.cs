namespace MedFlow.Application.DTOs.Doctors;

public sealed record DoctorEducationItemDto(
    string Id,
    string Title,
    string Institution,
    string? Badge,
    string Type);
