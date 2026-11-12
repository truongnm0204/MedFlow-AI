namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record GetPrescriptionSearchRequest(
    string? Query = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20);

public sealed record PrescriptionSearchItemDto(
    long Id,
    string PrescriptionCode,
    string VisitCode,
    string PatientCode,
    string PatientName,
    string? Gender,
    DateOnly? DateOfBirth,
    string? DoctorName,
    string Status,
    DateTime? IssuedAt,
    int ItemCount);

public sealed record PrescriptionSearchResponseDto(
    IReadOnlyList<PrescriptionSearchItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record PrescriptionSearchMedicineDto(
    long Id,
    string? MedicineCode,
    string MedicineName,
    decimal? PrescribedQuantity,
    string? DispensingUnit,
    decimal? SingleDoseQuantity,
    string? SingleDoseUnit,
    string? DoseScheduleText,
    string? Frequency,
    int? DurationDays,
    string? AdministrationRoute,
    string? Instructions);

public sealed record PrescriptionSearchDetailDto(
    PrescriptionSearchItemDto Prescription,
    IReadOnlyList<PrescriptionSearchMedicineDto> Items);
