using MedFlow.Application.DTOs.Ekyc;

namespace MedFlow.Application.Interfaces.Services;

public interface IEkycVerificationService
{
    /// <summary>
    /// Strict finalize — the TRUST ANCHOR of the eKYC pipeline. Validates ALL of:
    /// signed evidence per required step (dataSign via PUBLIC_KEY_CA), step completeness
    /// (document front+back+face+compare with document-before-compare), binding state VALID,
    /// policy verdict (liveness pass all + compare prob ≥ config threshold + tampering legal +
    /// OCR match against registration data), ownership, TTL, single-use. Atomic: concurrent
    /// finalize is single-winner via row lock. On full pass → VERIFIED + consumed_at set;
    /// otherwise → REJECTED + failure reason. Frontend never supplies verdict input.
    /// </summary>
    Task<FinalizeEkycSessionResponse> FinalizeAsync(
        long actorUserId,
        long sessionId,
        string? ipAddress,
        CancellationToken ct = default);
}
