namespace MedFlow.Application.DTOs.Visits;

public sealed record VisitPatientSummaryDto(
    string VisitId,
    long VisitNumericId,
    string VisitCode,
    long? PatientId,
    string PatientCode,
    string FullName,
    string Gender,
    int Age,
    bool CccdVerified,
    long? ServiceId,
    string ServiceCode,
    string ServiceName,
    decimal Price);
