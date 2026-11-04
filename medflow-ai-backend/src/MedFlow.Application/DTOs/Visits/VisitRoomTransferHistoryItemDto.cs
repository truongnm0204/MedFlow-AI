namespace MedFlow.Application.DTOs.Visits;

public sealed record VisitRoomTransferHistoryItemDto(
    long AuditLogId,
    long? FromRoomId,
    string? FromRoomCode,
    string? FromRoomName,
    long ToRoomId,
    string ToRoomCode,
    string ToRoomName,
    long? PreviousDoctorId,
    long? AssignedDoctorId,
    string Reason,
    string? Notes,
    long? TransferredByUserId,
    string? TransferredByStaffCode,
    string? TransferredByName,
    DateTime TransferredAtUtc);
