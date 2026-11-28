namespace MedFlow.Application.DTOs.Diagnostic;

/// <summary>Điều phối lại điểm CLS (#870) — lý do bắt buộc.</summary>
public sealed record ReassignDiagnosticOrderRequest(
    long? TargetServicePointId = null,
    long? TargetRoomId = null,
    string? Reason = null);

public sealed record ReassignDiagnosticOrderResponseDto(
    long OrderId,
    string? OrderCode,
    long VisitId,
    string? VisitCode,
    long? PreviousServicePointId,
    string? PreviousServicePointName,
    long AssignedServicePointId,
    string AssignedServicePointName,
    long? RoomId,
    string? RoomCode,
    string? RoomName,
    string? Reason,
    long? QueueEntryId,
    bool QueueEntryMoved,
    string? QueueStatus,
    int? SequenceNo,
    long RoutingHistoryId,
    DateTime ChangedAtUtc);

/// <summary>
/// Phiếu chỉ định CLS / màn hình bệnh nhân read-only (#871):
/// dịch vụ, điểm được phân công, thứ tự đề xuất và trạng thái thực hiện.
/// </summary>
public sealed record DiagnosticOrderAssignmentItemDto(
    long OrderId,
    string? OrderCode,
    int? SequenceNo,
    long ServiceId,
    string ServiceCode,
    string ServiceName,
    string? DiagnosticGroup,
    string? DiagnosticGroupLabel,
    bool RequiresFasting,
    bool RequiredForConclusion,
    string Status,
    string StatusLabel,
    string SelectionStatus,
    long? AssignedServicePointId,
    string? AssignedServicePointName,
    bool AssignedServicePointActive,
    long? RoomId,
    string? RoomCode,
    string? RoomName,
    string? RoomLocation,
    long? QueueEntryId,
    string? TicketNo,
    int? QueueNumber,
    string? QueueStatus,
    bool IsExecuted,
    bool IsDeclined,
    string? DeclineReason,
    DateTime? DeclinedAt);

public sealed record DiagnosticOrderAssignmentSlipDto(
    long VisitId,
    string VisitCode,
    long? PatientId,
    string? PatientCode,
    string? PatientName,
    string? AssignedDoctorDisplay,
    DateTime GeneratedAtUtc,
    IReadOnlyList<DiagnosticOrderAssignmentItemDto> Orders);
