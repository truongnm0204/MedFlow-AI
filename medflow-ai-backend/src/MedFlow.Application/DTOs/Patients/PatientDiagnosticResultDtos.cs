namespace MedFlow.Application.DTOs.Patients;

public sealed record GetPatientDiagnosticResultsRequest(
    long? PatientId = null,
    long? VisitId = null,
    string? VisitCode = null,
    int Page = 1,
    int PageSize = 20);

public sealed record PatientDiagnosticResultListItemDto(
    long OrderId,
    string? OrderCode,
    long? VisitId,
    string? VisitCode,
    long PatientId,
    string PatientCode,
    string PatientName,
    string RelationshipLabel,
    string? HospitalName,
    string? ServiceName,
    string? OrderingDoctorName,
    string? PerformedPlaceName,
    DateTime? SignedAt,
    bool IsPublishedToPatient,
    string Status);

public sealed record PatientDiagnosticResultListResponseDto(
    IReadOnlyList<PatientDiagnosticResultListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record PatientDiagnosticResultDetailDto(
    long OrderId,
    string? OrderCode,
    long? VisitId,
    string? VisitCode,
    long PatientId,
    string PatientCode,
    string PatientName,
    string? Gender,
    int? BirthYear,
    string RelationshipLabel,
    string? HospitalName,
    string? ServiceName,
    string? OrderingDoctorName,
    string? PerformingStaffName,
    string? PerformedPlaceName,
    DateTime? SignedAt,
    bool IsPublishedToPatient,
    string Status,
    string? ResultText,
    string? Conclusion,
    string? ReportFileUrl,
    string? FileName);
