namespace MedFlow.Application.DTOs.Consultation;

public sealed record GetDiagnosticResultsTrackingRequest(
    string? Date = null,
    string? Status = null,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20);

public sealed record DiagnosticResultsTrackingResponseDto(
    DateOnly Date,
    long HospitalId,
    long DoctorId,
    string? DoctorCode,
    string DoctorName,
    DateTime GeneratedAtUtc,
    DiagnosticResultsTrackingStatsDto Stats,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<DiagnosticResultsTrackingVisitDto> Items);

public sealed record DiagnosticResultsTrackingStatsDto(
    int TotalVisits,
    int CompletedVisits,
    int InProgressVisits,
    int PendingSignVisits,
    int TotalOrders,
    int SignedOrders,
    int WaitingOrders);

public sealed record DiagnosticResultsTrackingVisitDto(
    long VisitId,
    string VisitCode,
    string VisitStatus,
    long? PatientId,
    string? PatientCode,
    string? PatientName,
    string TrackingStatus,
    int TotalOrders,
    int SignedOrders,
    int AwaitingSignOrders,
    int InProgressOrders,
    int PendingOrders,
    int MandatoryOrders,
    int MandatorySignedOrders,
    int CompletionPercent,
    DateTime? LatestOrderedAt,
    IReadOnlyList<DiagnosticResultsTrackingOrderDto> Orders);

public sealed record DiagnosticResultsTrackingOrderDto(
    long OrderId,
    string OrderCode,
    long? ServiceId,
    string? ServiceCode,
    string? ServiceName,
    string? Priority,
    bool RequiredForConclusion,
    string OrderStatus,
    string TrackingStatus,
    string ResultStatus,
    long? OrderingDoctorId,
    string? OrderingDoctorName,
    DateTime? OrderedAt,
    string? AssigneeStaffName,
    string? ResultSummary,
    DateTime? SignedAt,
    string? SignedByStaffName);
