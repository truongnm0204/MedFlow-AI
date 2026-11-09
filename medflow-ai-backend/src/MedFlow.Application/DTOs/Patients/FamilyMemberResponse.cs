namespace MedFlow.Application.DTOs.Patients;

public sealed record FamilyMemberVitalsDto(
    string? BloodType,
    int? HeightCm,
    int? WeightKg
);

public sealed record FamilyMemberPermissionsDto(
    bool CanManage,
    bool CanBookAppointments,
    bool CanViewMedicalRecords,
    string RelationshipStatus,
    string? ConsentVerifiedAt
);

public sealed record FamilyMemberResponse(
    string Id,
    long MemberPatientId,
    string FullName,
    string Relationship,
    string RelationshipBadgeColor,
    string PatientCode,
    string SubStatus,
    bool IsPrimaryAccount,
    string? AvatarUrl,
    string? BirthDate,
    int? Age,
    string? Gender,
    string? Nationality,
    string? IdentificationNo,
    string? IdentityIssueDate,
    string? IdentityIssuePlace,
    bool IsIdentityVerified,
    string? Phone,
    string? Email,
    string? Address,
    string? Guardian,
    string? EmergencyContactName,
    string? EmergencyContactRelation,
    string? EmergencyPhone,
    string? LatestVisit,
    string? FollowUpSchedule,
    string? ServicePackage,
    string? ClinicalNotes,
    bool HasActiveAppointment,
    string? ActiveAppointmentDetails,
    int OutpatientVisitsCount,
    int ActivePrescriptionsCount,
    FamilyMemberVitalsDto? Vitals,
    FamilyMemberPermissionsDto? Permissions
);
