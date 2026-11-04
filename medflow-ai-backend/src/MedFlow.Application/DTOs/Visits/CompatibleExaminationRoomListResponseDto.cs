namespace MedFlow.Application.DTOs.Visits;

public sealed record CompatibleExaminationRoomListResponseDto(
    long VisitId,
    string VisitCode,
    string VisitStatus,
    long? HospitalId,
    long? AssignedDoctorId,
    string? AssignedDoctorCode,
    string? AssignedDoctorName,
    long? CurrentExaminationRoomId,
    string? CurrentExaminationRoomCode,
    long? RequiredDepartmentId,
    string? RequiredDepartmentName,
    IReadOnlyList<AssignableExaminationRoomItemDto> Rooms);
