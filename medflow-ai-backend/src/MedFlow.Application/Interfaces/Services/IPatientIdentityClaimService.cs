using MedFlow.Application.DTOs.Patients;

namespace MedFlow.Application.Interfaces.Services;

public interface IPatientIdentityClaimService
{
    Task<SendIdentityClaimOtpResponse> SendOtpAsync(long actorUserId, CancellationToken ct = default);

    Task<IdentityClaimResponse> CreateAsync(
        long actorUserId,
        CreateIdentityClaimRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<IdentityClaimResponse>> GetMineAsync(
        long actorUserId,
        CancellationToken ct = default);

    Task<IdentityClaimResponse> GetMineByIdAsync(
        long actorUserId,
        long claimId,
        CancellationToken ct = default);

    Task<IReadOnlyList<StaffIdentityClaimDto>> GetReviewQueueAsync(
        long actorUserId,
        string? status,
        CancellationToken ct = default);

    Task<IdentityClaimResponse> ApproveAsync(
        long actorUserId,
        long claimId,
        CancellationToken ct = default);

    Task<IdentityClaimResponse> RejectAsync(
        long actorUserId,
        long claimId,
        RejectIdentityClaimRequest request,
        CancellationToken ct = default);
}
