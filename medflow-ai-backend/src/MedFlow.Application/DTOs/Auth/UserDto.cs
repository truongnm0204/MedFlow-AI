namespace MedFlow.Application.DTOs.Auth;

public sealed record UserDto(
    long Id,
    string Email,
    string? FullName,
    string[] Roles,
    string? PatientCode,
    string? StaffCode = null);
