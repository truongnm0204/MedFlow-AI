using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record UpdateStaffAccountRequest
{
    [MaxLength(200)]
    public string? FullName { get; init; }

    [MaxLength(20)]
    [JsonPropertyName("authMethod")]
    public string? AuthProvider { get; init; }

    [MaxLength(255)]
    public string? Facility { get; init; }

    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; init; }

    public string? Password { get; init; }

    [MaxLength(16)]
    public string? Status { get; init; }

    [MaxLength(50)]
    public string? Role { get; init; }

    [MaxLength(200)]
    public string? Department { get; init; }
}
