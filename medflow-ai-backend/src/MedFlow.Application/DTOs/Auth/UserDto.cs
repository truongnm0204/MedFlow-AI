namespace MedFlow.Application.DTOs.Auth;

public sealed record UserDto(
    Guid Id,
    string Username,
    string Email,
    string? FullName,
    string[] Roles,
    string? PatientCode);
