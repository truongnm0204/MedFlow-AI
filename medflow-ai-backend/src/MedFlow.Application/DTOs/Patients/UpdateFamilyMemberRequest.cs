namespace MedFlow.Application.DTOs.Patients;

public sealed record UpdateFamilyMemberRequest
{
    public string? FullName { get; init; }
    public string? Relationship { get; init; }
    public string? Gender { get; init; }
    public string? BirthDate { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactRelation { get; init; }
    public string? EmergencyPhone { get; init; }
    public string? BloodType { get; init; }
}
