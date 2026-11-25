using MedFlow.Application.Common.Serialization;

namespace MedFlow.Application.DTOs.Patients;

public sealed record PatientEmergencyContactInfoDto(
    string? FullName,
    string? Relationship,
    string? Phone);

public sealed class UpdateEmergencyContactRequest
{
    public OptionalField<string?> FullName { get; init; }
    public OptionalField<string?> Relationship { get; init; }
    public OptionalField<string?> Phone { get; init; }
}

public sealed class UpdateMyPatientProfileRequest
{
    public OptionalField<string?> Phone { get; init; }
    public OptionalField<string?> Address { get; init; }
    public OptionalField<string?> BloodType { get; init; }
    public OptionalField<DateOnly?> DateOfBirth { get; init; }
    public OptionalField<string?> Gender { get; init; }
    public OptionalField<UpdateEmergencyContactRequest?> EmergencyContact { get; init; }

    // Legal identity guard properties: rejected with 400 ValidationException if client attempts modification.
    public OptionalField<string?> FullName { get; init; }
    public OptionalField<string?> IdentityCard { get; init; }
    public OptionalField<string?> IdentificationHash { get; init; }
}
