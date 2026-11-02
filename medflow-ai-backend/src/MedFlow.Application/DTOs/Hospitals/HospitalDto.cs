namespace MedFlow.Application.DTOs.Hospitals;

public record HospitalDto(
    long Id,
    string HospitalCode,
    string Name,
    string? Address,
    bool IsActive);
