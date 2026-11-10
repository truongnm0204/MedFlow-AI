namespace MedFlow.Application.DTOs.Reception;

public sealed record PatientEmergencyContactDto(
    string Name,
    string Relationship,
    string Phone);

public sealed record PatientTodayAppointmentDetailDto(
    long Id,
    string AppointmentCode,
    string Time,
    string TimeRange,
    string DoctorName,
    string Department,
    string Room,
    string RoomName,
    string Status,
    string AppointmentStatus,
    long? VisitId,
    string? VisitCode,
    string? VisitStatus);

public sealed record PatientPastVisitSummaryDto(
    long Id,
    string VisitCode,
    string Date,
    DateTime? VisitedAt,
    string Department,
    string DoctorName,
    string Diagnosis);

public sealed record PatientDetailResponseDto(
    long Id,
    string PatientCode,
    string FullName,
    string Gender,
    string GenderLabel,
    int Age,
    string DateOfBirth,
    DateOnly? DateOfBirthIso,
    string Cccd,
    string? IdentityCardMasked,
    bool IsIdentityVerified,
    string Phone,
    string? Email,
    string? BhytNumber,
    string? HealthInsuranceNumber,
    string Address,
    string? BloodType,
    string Status,
    PatientEmergencyContactDto? EmergencyContact,
    PatientTodayAppointmentDetailDto? TodayAppointment,
    int TotalPastVisits,
    IReadOnlyList<PatientPastVisitSummaryDto> PastVisits);
