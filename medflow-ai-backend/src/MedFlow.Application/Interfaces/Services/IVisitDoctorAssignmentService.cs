using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IVisitDoctorAssignmentService
{
    Task<AssignableDoctorListResponseDto> GetAssignableDoctorsAsync(
        long actorUserId,
        string visitIdOrCode,
        long? serviceId = null,
        string? serviceCode = null,
        long? departmentId = null,
        CancellationToken ct = default);

    Task<AssignDoctorToVisitResponseDto> AssignDoctorAsync(
        long actorUserId,
        long visitId,
        AssignDoctorToVisitRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<AssignDoctorToVisitResponseDto> AssignDoctorAsync(
        long actorUserId,
        string visitIdOrCode,
        AssignDoctorToVisitRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
