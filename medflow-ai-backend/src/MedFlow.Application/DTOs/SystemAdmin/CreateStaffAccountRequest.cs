using System.ComponentModel.DataAnnotations;

namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record CreateStaffAccountRequest
{
    [Required]
    [MaxLength(40)]
    public string StaffCode { get; init; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string FullName { get; init; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string AuthProvider { get; init; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Facility { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; init; } = string.Empty;

    public string? Password { get; init; }

    [Required]
    [MaxLength(16)]
    public string Status { get; init; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Role { get; init; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Department { get; init; } = string.Empty;
}
