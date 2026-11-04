namespace MedFlow.Application.DTOs.Visits;

public sealed record VisitCompatibleRoomsResponseDto(
    long VisitId,
    string VisitCode,
    long? PatientId,
    string? PatientCode,
    string? PatientName,
    string VisitStatus,
    long? CurrentRoomId,
    string? CurrentRoomCode,
    string? CurrentRoomName,
    long? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    long? AssignedDoctorId,
    string? AssignedDoctorCode,
    string? AssignedDoctorName,
    int? QueueNumber,
    string? QueueStatus,
    IReadOnlyList<CompatibleExaminationRoomItemDto> CompatibleRooms,
    IReadOnlyList<VisitRoomTransferHistoryItemDto> TransferHistory);
