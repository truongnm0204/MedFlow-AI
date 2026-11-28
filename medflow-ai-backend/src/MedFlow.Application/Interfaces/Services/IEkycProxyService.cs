using MedFlow.Domain.Entities;

namespace MedFlow.Application.Interfaces.Services;

/// <summary>Result of one transparently proxied VNPT call — response returned verbatim to the caller.</summary>
public sealed record EkycProxyForwardResult(
    int HttpStatus,
    string ContentType,
    byte[] Body,
    IReadOnlyDictionary<string, string[]> Headers);

public interface IEkycProxyService
{
    /// <summary>
    /// Validates session ownership/state then forwards the request verbatim to VNPT
    /// (strip Origin, inject credentials, never persist payloads). Logs per-call metadata
    /// and maintains step-binding validation state (fail-closed).
    /// </summary>
    Task<EkycProxyForwardResult> ForwardAsync(
        long actorUserId,
        long sessionId,
        string endpointPath,
        string httpMethod,
        string? contentType,
        byte[] requestBody,
        CancellationToken ct = default);

    /// <summary>
    /// Validates that the session exists, belongs to <paramref name="actorUserId"/>, and is in a
    /// forwardable state (not terminal, not expired). Throws NotFound/Forbidden/Conflict otherwise.
    /// Shared by the proxy endpoint and any future step endpoints.
    /// </summary>
    Task<EkycVerificationSession> ValidateSessionForForwardAsync(
        long actorUserId,
        long sessionId,
        CancellationToken ct = default);
}
