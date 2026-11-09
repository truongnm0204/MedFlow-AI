namespace MedFlow.Application.DTOs.Reception;

public sealed record CheckInScheduledPatientRequest(
    string? ReasonForVisit = null,
    string? BloodPressure = null,
    int? HeartRate = null,
    double? Temperature = null,
    bool? IsIdentityVerified = null,
    bool? IdentityVerified = null,
    bool? IsInsuranceVerified = null,
    string? Priority = null,
    bool? PriorityVerificationConfirmed = null,
    string? Notes = null,
    long? AppointmentId = null,
    string? AppointmentCode = null);
