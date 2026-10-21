using System;
using System.Collections.Generic;
using System.Text;

namespace MedFlow.Application.DTOs.Patients;

public sealed record UpdatePersonalProfileRequest(
    string FullName,
    string? Phone,
    string? Email,
    DateOnly? DateOfBirth,
    string? Gender,
    string? IdentificationNo,
    DateOnly? IdentityIssuedDate,
    string? IdentityIssuedPlace,
    string? Address,
    string? AvatarUrl,
    string? Nationality,
    string? BloodType,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? EmergencyContactRelationship
);
