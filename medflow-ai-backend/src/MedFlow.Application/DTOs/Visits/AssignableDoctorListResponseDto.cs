namespace MedFlow.Application.DTOs.Visits;

public sealed record AssignableDoctorListResponseDto(
    long VisitId,
    string VisitCode,
    string VisitStatus,
    long? HospitalId,
    long? AssignedDoctorId,
    string? AssignedDoctorCode,
    string? AssignedDoctorName,
    long? CurrentExaminationRoomId,
    string? CurrentExaminationRoomCode,
    string? CurrentExaminationRoomName,
    long? RequiredServiceId,
    string? RequiredServiceCode,
    string? RequiredServiceName,
    long? RequiredDepartmentId,
    string? RequiredDepartmentName,
    VisitPatientSummaryDto Patient,
    IReadOnlyList<AssignableDoctorItemDto> Doctors);
