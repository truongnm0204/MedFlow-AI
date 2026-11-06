namespace MedFlow.Application.DTOs.Patients;

public sealed record CreateFamilyMemberRequest
{
    public string FullName { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string? BirthDate { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? IdentificationNo { get; init; }
    public string? Nationality { get; init; } = "Việt Nam";
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string Address { get; init; } = string.Empty;
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactRelation { get; init; }
    public string? EmergencyPhone { get; init; }
    public string? BloodType { get; init; }
}
