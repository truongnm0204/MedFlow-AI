using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

/// <summary>
/// Clinical Session Recovery authority (PR #859 / Issue #858, F01 Option B).
/// One narrowly-scoped request/grant per interrupted clinical serving ticket; no generic delegation.
/// </summary>
public interface IClinicalSessionRecoveryService
{
    Task<ClinicalSessionRecoveryContextDto> GetContextAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default);

    Task<ClinicalSessionRecoveryRequestDto> RequestRecoveryAsync(
        long actorUserId,
        string idOrCode,
        CreateClinicalSessionRecoveryRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ClinicalSessionRecoveryRequestDto> GrantAsync(
        long actorUserId,
        long requestId,
        GrantClinicalSessionRecoveryRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ClinicalSessionRecoveryRequestDto> RejectAsync(
        long actorUserId,
        long requestId,
        ClinicalSessionRecoveryDecisionRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ClinicalSessionRecoveryRequestDto> RevokeAsync(
        long actorUserId,
        long requestId,
        ClinicalSessionRecoveryDecisionRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ClinicalSessionRecoveryRequestDto> CancelAsync(
        long actorUserId,
        long requestId,
        ClinicalSessionRecoveryDecisionRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ClinicalSessionRecoveryConfirmResponseDto> ConfirmAsync(
        long actorUserId,
        long requestId,
        ConfirmClinicalSessionRecoveryRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);

    /// <summary>PM-approved Commit B addition: scoped request list for Doctor (grant/confirm) / Reception / HospitalAdmin.</summary>
    Task<IReadOnlyList<ClinicalSessionRecoveryListItemDto>> ListRequestsAsync(
        long actorUserId,
        CancellationToken ct = default);

    /// <summary>PM-approved Commit B addition: active doctors with RECOVER + same hospital/clinical scope, for GRANT holders.</summary>
    Task<IReadOnlyList<ClinicalSessionRecoveryEligibleRecipientDto>> GetEligibleRecipientsAsync(
        long actorUserId,
        long requestId,
        CancellationToken ct = default);
}
