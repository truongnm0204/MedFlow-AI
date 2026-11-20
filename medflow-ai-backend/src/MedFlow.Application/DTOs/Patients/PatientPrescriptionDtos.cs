namespace MedFlow.Application.DTOs.Patients;

public sealed record GetPatientPrescriptionsRequest(
    int Page = 1,
    int PageSize = 20);

public sealed record PatientPrescriptionListItemDto(
    long Id,
    string? PrescriptionCode,
    long VisitId,
    string? VisitCode,
    string Status,
    DateTime? IssuedAt,
    string? PrescribingDoctorName,
    string? HospitalName,
    int ItemCount);

public sealed record PatientPrescriptionListResponseDto(
    IReadOnlyList<PatientPrescriptionListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record PatientPrescriptionMedicineDto(
    long Id,
    string? MedicineCode,
    string MedicineName,
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    decimal PrescribedQuantity,
    string? DispensingUnit,
    decimal? SingleDoseQuantity,
    string? SingleDoseUnit,
    string? DoseScheduleText,
    string? Frequency,
    int? DurationDays,
    string? AdministrationRoute,
    string? Instructions);

public sealed record PatientPrescriptionDetailDto(
    PatientPrescriptionListItemDto Prescription,
    IReadOnlyList<PatientPrescriptionMedicineDto> Items);
