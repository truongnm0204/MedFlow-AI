using MedFlow.Application.DTOs.Ekyc;

namespace MedFlow.Application.Interfaces.Services;

public interface IEkycSessionService
{
    /// <summary>
    /// Creates an INITIATED eKYC session for the authenticated patient account.
    /// Requires valid consent (version + acceptance) — hard requirement 11.
    /// </summary>
    Task<CreateEkycSessionResponse> CreateAsync(
        long actorUserId,
        CreateEkycSessionRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    /// <summary>Returns session status for the owning account — ownership-enforced, never leaks evidence.</summary>
    Task<EkycSessionStatusResponse> GetStatusAsync(
        long actorUserId,
        long sessionId,
        CancellationToken ct = default);
}
