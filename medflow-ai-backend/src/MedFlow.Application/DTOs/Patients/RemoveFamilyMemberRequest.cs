namespace MedFlow.Application.DTOs.Patients;

public sealed record RemoveFamilyMemberRequest
{
    public string? Reason { get; init; }
}

