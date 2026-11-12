namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record GetDiagnosticResultViewsRequest(
    string? ResultStatus = null,
    string? Category = null,
    string? Keyword = null,
    long? VisitId = null,
    string? VisitCode = null,
    int Limit = 50);

public sealed record DiagnosticResultDocumentDto(
    string? ReportFileUrl,
    bool HasDocument,
    string? FileName);

public sealed record DiagnosticResultViewItemDto(
    long OrderId,
    string OrderCode,
    long VisitId,
    string VisitCode,
    string VisitStatus,
    DiagnosticOrderPatientSummaryDto Patient,
    long? OrderingDoctorId,
    string? OrderingDoctorCode,
    string OrderingDoctorName,
    long ServiceId,
    string ServiceCode,
    string ServiceName,
    string Category,
    decimal UnitPrice,
    string Priority,
    string OrderStatus,
    string DepartmentName,
    string? RoomCode,
    string? RoomName,
    string? ServicePointName,
    string? Instruction,
    DateTime OrderedAt,
    DateTime? CompletedAt,
    long? ResultId,
    string ResultStatus,
    bool IsResultFinalized,
    DateTime? ResultFinalizedAt,
    long? FinalizedByStaffId,
    string? FinalizedByStaffCode,
    string? FinalizedByStaffName,
    int? LatestVersionNo,
    string? ResultText,
    string? Conclusion,
    DiagnosticResultDocumentDto Document,
    long? EnteredByStaffId,
    string? EnteredByStaffCode,
    string? EnteredByStaffName,
    DateTime? EnteredAt,
    long? VerifiedByStaffId,
    string? VerifiedByStaffCode,
    string? VerifiedByStaffName,
    DateTime? VerifiedAt,
    string? CorrectionReason);

public sealed record DiagnosticResultViewSummaryDto(
    int TotalWithResult,
    int DraftOrPreliminaryCount,
    int PendingVerificationCount,
    int FinalizedOrVerifiedCount,
    int CancelledCount);

public sealed record DiagnosticResultViewListResponseDto(
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    long? DepartmentId,
    string DepartmentName,
    DateTime GeneratedAtUtc,
    DiagnosticStaffProfileDto StaffProfile,
    DiagnosticResultViewSummaryDto Summary,
    IReadOnlyList<DiagnosticResultViewItemDto> Items);

public sealed record DiagnosticResultViewDetailResponseDto(
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    DateTime GeneratedAtUtc,
    DiagnosticStaffProfileDto StaffProfile,
    DiagnosticResultViewItemDto Item,
    DiagnosticOrderClinicalSummaryDto ClinicalContext,
    IReadOnlyList<DiagnosticResultVersionItemDto> Versions,
    IReadOnlyList<DiagnosticResultHistoryItemDto> StatusHistory);
