using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// Transparent proxy forwarder for VNPT eKYC inference calls (PM ruling: transparent proxy +
/// session correlation via path + strict finalize — NO per-step ordering enforcement at this layer).
///
/// Guarantees:
/// <list type="bullet">
///   <item>Session validation BEFORE forwarding: exists, owned by actor, non-terminal, within TTL.</item>
///   <item>Origin header never forwarded (VNPT domain check rejects any Origin — probe Q4);
///         credentials injected server-side from env config — never exposed to clients.</item>
///   <item>Verbatim body forward, verbatim response return — inference payloads are never modified.</item>
///   <item>NO PERSISTENCE of images/base64: bodies live only in scoped memory for the duration of
///         the forward; exception paths never embed body content (messages carry endpoint+status only).</item>
///   <item>Per-call metadata logged to ekyc_proxy_call_logs (endpoint, status, hash refs, duration) —
///         never payloads.</item>
///   <item>Step-binding validation fail-closed: compare document reference must match a hash uploaded
///         within the same session; mismatch → INVALID_BINDING, unparseable → UNVERIFIABLE_BINDING.</item>
/// </list>
///
/// Timeout/retry policy (documented decision): single-attempt, no retries. VNPT inference
/// idempotency is unknown — retrying a liveness/OCR submission may double-charge or produce
/// inconsistent server-side state (probe evidence: every call is signed and stateful per
/// client_session). Timeouts surface as upstream errors to the caller; the SDK can re-submit
/// at the user's explicit action, which is the only safe retry semantics. Per-call timeout
/// from Ekyc:UpstreamTimeoutSeconds (default 30s — addFile uploads of large images need headroom).
/// </summary>
public sealed class EkycProxyService : IEkycProxyService
{
    public const string SessionNotFoundMessage = "Không tìm thấy phiên xác minh danh tính.";
    public const string SessionNotForwardableMessage =
        "Phiên xác minh danh tính không còn ở trạng thái cho phép thực hiện bước xác minh.";
    public const string SessionExpiredMessage =
        "Phiên xác minh danh tính đã hết hạn. Vui lòng tạo phiên mới.";
    public const string UpstreamFailureMessage =
        "Không kết nối được tới máy chủ xác minh danh tính. Vui lòng thử lại sau.";
    public const string InvalidEndpointMessage = "Đường dẫn endpoint xác minh không hợp lệ.";

    /// <summary>Endpoint suffixes whose requests carry document/face inference payloads.</summary>
    internal const string AddFileEndpointSuffix = "file-service/v1/addFile";
    internal const string CompareEndpointSuffix = "ai/v1/web/face/compare";

    /// <summary>Request bodies are capped to bound memory — probe evidence shows inference JSON
    /// envelopes are small (hash refs, not embedded images). 1 MiB is generous headroom.
    /// Public: the controller enforces the same bound while reading the incoming body.</summary>
    public const int MaxRequestBodyBytes = 1_048_576;

    /// <summary>Response bodies can be larger (OCR payloads) but stay bounded — 4 MiB.</summary>
    internal const int MaxResponseBodyBytes = 4 * 1_048_576;

    private static readonly HashSet<string> ForwardedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
    };

    private readonly IApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly EkycOptions _options;
    private readonly ILogger<EkycProxyService>? _logger;

    public EkycProxyService(
        IApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        TimeProvider? timeProvider = null,
        IOptions<EkycOptions>? options = null,
        ILogger<EkycProxyService>? logger = null)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options?.Value ?? new EkycOptions();
        _logger = logger;
    }

    public async Task<EkycVerificationSession> ValidateSessionForForwardAsync(
        long actorUserId,
        long sessionId,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var session = await _db.EkycVerificationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException("EkycVerificationSession", sessionId);

        // Layer-2 ownership: a session belongs to exactly one account — never leak existence cross-account.
        if (session.UserId != actorUserId)
        {
            throw new NotFoundException("EkycVerificationSession", sessionId);
        }

        if (EkycVerificationStatuses.Terminal.Contains(session.Status))
        {
            throw new ConflictException(SessionNotForwardableMessage);
        }

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        if (session.ExpiresAt <= nowUtc)
        {
            throw new ConflictException(SessionExpiredMessage);
        }

        return session;
    }

    public async Task<EkycProxyForwardResult> ForwardAsync(
        long actorUserId,
        long sessionId,
        string endpointPath,
        string httpMethod,
        string? contentType,
        byte[] requestBody,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requestBody);

        var session = await ValidateSessionForForwardAsync(actorUserId, sessionId, ct);
        var config = VnptEkycConfigReader.Read(OptionsWrapper());

        var normalizedPath = NormalizeEndpointPath(endpointPath);
        var method = NormalizeMethod(httpMethod);

        if (requestBody.Length > MaxRequestBodyBytes)
        {
            throw new ValidationException(InvalidEndpointMessage);
        }

        // --- Metadata-only extraction BEFORE forward (never retains content) --------------------
        var isAddFile = normalizedPath.EndsWith(AddFileEndpointSuffix, StringComparison.OrdinalIgnoreCase);
        var isCompare = normalizedPath.EndsWith(CompareEndpointSuffix, StringComparison.OrdinalIgnoreCase);

        string? requestParseWarning = null;
        IReadOnlyList<string> requestHashes = [];
        string? compareDocumentHash = null;
        string? compareParseWarning = null;

        if (!isAddFile)
        {
            requestHashes = EkycProxyMetadataParser.ExtractRequestHashes(
                DecodeUtf8(requestBody), out requestParseWarning);
        }

        if (isCompare)
        {
            compareDocumentHash = EkycProxyMetadataParser.ExtractCompareDocumentHash(
                DecodeUtf8(requestBody), out compareParseWarning);
        }

        // --- Forward ----------------------------------------------------------------------------
        var upstreamUri = BuildUpstreamUri(config.BackendUrl, normalizedPath);
        var startedAt = _timeProvider.GetTimestamp();

        int httpStatus;
        string responseContentType;
        byte[] responseBody;
        var responseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        using (var upstreamRequest = new HttpRequestMessage(method, upstreamUri))
        {
            // Credentials injected server-side (probe Q3 headers): Bearer + Token-id + Token-key.
            // Origin is NEVER set — HttpRequestMessage carries none unless explicitly added, and we
            // deliberately do not copy any client headers (cookie/origin/referer isolation).
            upstreamRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.AccessToken}");
            upstreamRequest.Headers.TryAddWithoutValidation("Token-id", config.TokenId);
            upstreamRequest.Headers.TryAddWithoutValidation("Token-key", config.TokenKey);
            upstreamRequest.Headers.TryAddWithoutValidation("mac-address", "WEB-001");

            if (requestBody.Length > 0)
            {
                upstreamRequest.Content = new ByteArrayContent(requestBody);
                if (!string.IsNullOrWhiteSpace(contentType))
                {
                    upstreamRequest.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.UpstreamTimeoutSeconds)));

            try
            {
                var client = _httpClientFactory.CreateClient("vnpt-ekyc");
                using var upstreamResponse = await client.SendAsync(
                    upstreamRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

                httpStatus = (int)upstreamResponse.StatusCode;
                responseContentType = upstreamResponse.Content.Headers.ContentType?.ToString()
                    ?? "application/json";

                foreach (var header in upstreamResponse.Headers)
                {
                    if (ForwardedResponseHeaders.Contains(header.Key))
                    {
                        responseHeaders[header.Key] = [.. header.Value];
                    }
                }

                // Bounded in-memory read — disposed with the using; never written anywhere.
                using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(timeoutCts.Token);
                responseBody = await ReadBoundedAsync(responseStream, MaxResponseBodyBytes, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ConflictException(UpstreamFailureMessage);
            }
            catch (HttpRequestException)
            {
                throw new ConflictException(UpstreamFailureMessage);
            }
        }

        var durationMs = (int)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

        // --- Post-forward metadata extraction (never retains content) --------------------------
        string? responseObjectHash = null;
        string? responseParseWarning = null;
        if (isAddFile)
        {
            responseObjectHash = EkycProxyMetadataParser.ExtractAddFileHash(
                DecodeUtf8(responseBody), out responseParseWarning);
        }

        // --- Signed-evidence capture (slice 3 — trust anchor input) ----------------------------
        // Probe Q3: every AI response carries dataSign+dataBase64 and NO server re-query exists —
        // capture at proxy time or finalize has nothing to verify. Encrypted at rest (AES-256-GCM):
        // the plaintext is OCR personal data (never images). Capture failure NEVER breaks the
        // forward — a missing capture makes finalize fail-closed (rejected), which is safe.
        string? capturedDataSign = null;
        byte[]? capturedPayloadEncrypted = null;
        if (!isAddFile
            && EkycProxyMetadataParser.TryExtractSignedEvidence(
                DecodeUtf8(responseBody), out var dataSign, out var dataBase64))
        {
            capturedDataSign = dataSign;
            try
            {
                var encryptionKey = EkycCrypto.RequireEncryptionKey(OptionsWrapper());
                capturedPayloadEncrypted = EkycCrypto.Encrypt(
                    System.Text.Encoding.UTF8.GetBytes(dataBase64!), encryptionKey);
            }
            catch (InvalidOperationException)
            {
                // Encryption key missing/misconfigured — log-only capture. Finalize will fail
                // closed for lack of payload evidence; never surface config detail upstream.
                capturedPayloadEncrypted = null;
                _logger?.LogWarning(
                    "eKYC proxy could not encrypt response payload capture for session {SessionId} — encryption key unavailable",
                    session.Id);
            }
        }

        var parseWarning = FirstNonEmpty(requestParseWarning, compareParseWarning, responseParseWarning);

        // --- Persist metadata + binding state (never payloads) -----------------------------------
        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        _db.EkycProxyCallLogs.Add(new EkycProxyCallLog
        {
            SessionId = session.Id,
            EndpointPath = normalizedPath,
            HttpMethod = method.Method,
            HttpStatus = httpStatus,
            RequestObjectHashes = requestHashes.Count > 0
                ? JsonSerializer.Serialize(requestHashes)
                : null,
            ResponseObjectHash = responseObjectHash,
            ResponseDataSign = capturedDataSign,
            ResponsePayloadEncrypted = capturedPayloadEncrypted,
            ParseWarning = parseWarning,
            DurationMs = durationMs,
            CreatedAt = nowUtc,
        });

        await ApplyBindingValidationAsync(session, isCompare, compareDocumentHash, nowUtc, ct);

        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(parseWarning))
        {
            _logger?.LogWarning(
                "eKYC proxy parse warning for session {SessionId} endpoint {EndpointPath}: {ParseWarning}",
                session.Id, normalizedPath, parseWarning);
        }

        return new EkycProxyForwardResult(httpStatus, responseContentType, responseBody, responseHeaders);
    }

    // ------------------------------------------------------------------
    // Binding validation — fail-closed
    // ------------------------------------------------------------------

    /// <summary>
    /// Maintains session binding state. Rules:
    /// <list type="bullet">
    ///   <item>A successful addFile hash extends the session's known-hash set (inferred from logs — queried at compare time).</item>
    ///   <item>compare referencing a hash not in the session's uploaded set → INVALID_BINDING.</item>
    ///   <item>compare with unparseable document reference → UNVERIFIABLE_BINDING.</item>
    ///   <item>compare referencing a session-owned hash → VALID (provisional — finalize re-verifies).</item>
    ///   <item>Once INVALID/UNVERIFIABLE, state never self-heals within the session (sticky fail-closed).</item>
    /// </list>
    /// </summary>
    private async Task ApplyBindingValidationAsync(
        EkycVerificationSession session,
        bool isCompare,
        string? compareDocumentHash,
        DateTime nowUtc,
        CancellationToken ct)
    {
        session.UpdatedAt = nowUtc;

        if (!isCompare)
        {
            return;
        }

        // Sticky failure: once failed, never re-validate to VALID within the same session.
        if (session.BindingState is EkycBindingStates.Invalid or EkycBindingStates.Unverifiable)
        {
            return;
        }

        if (compareDocumentHash is null)
        {
            session.BindingState = EkycBindingStates.Unverifiable;
            return;
        }

        session.CompareReferenceHash ??= compareDocumentHash;

        // Known hashes = all addFile response hashes previously logged for this session.
        // Bounded by max-attempts per session — never a table scan. The current compare row
        // is not yet saved, so only genuine prior uploads are visible here.
        var knownHashes = await _db.EkycProxyCallLogs
            .AsNoTracking()
            .Where(l => l.SessionId == session.Id && l.ResponseObjectHash != null)
            .Select(l => l.ResponseObjectHash!)
            .ToListAsync(ct);

        session.BindingState = knownHashes.Contains(compareDocumentHash, StringComparer.Ordinal)
            ? EkycBindingStates.Valid
            : EkycBindingStates.Invalid;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private IOptions<EkycOptions> OptionsWrapper()
        => Microsoft.Extensions.Options.Options.Create(_options);

    internal static string NormalizeEndpointPath(string endpointPath)
    {
        if (string.IsNullOrWhiteSpace(endpointPath))
        {
            throw new ValidationException(InvalidEndpointMessage);
        }

        var trimmed = endpointPath.Trim();

        // Path traversal / absolute URL rejection — the proxy must only ever hit VNPT paths.
        if (trimmed.Contains("..", StringComparison.Ordinal)
            || trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.StartsWith('\\'))
        {
            throw new ValidationException(InvalidEndpointMessage);
        }

        var normalized = trimmed.TrimStart('/');
        if (normalized.Length == 0 || normalized.Length > 256)
        {
            throw new ValidationException(InvalidEndpointMessage);
        }

        return "/" + normalized;
    }

    internal static HttpMethod NormalizeMethod(string? httpMethod)
    {
        // VNPT inference endpoints are POST-only (probe Q3: GET returns 405). Allow POST plus
        // GET/HEAD for file-service health-style calls — anything else rejected.
        return httpMethod?.Trim().ToUpperInvariant() switch
        {
            "POST" => HttpMethod.Post,
            "GET" => HttpMethod.Get,
            "HEAD" => HttpMethod.Head,
            _ => throw new ValidationException(InvalidEndpointMessage),
        };
    }

    internal static Uri BuildUpstreamUri(string backendUrl, string normalizedPath)
    {
        var baseUri = new Uri(backendUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, normalizedPath.TrimStart('/'));
    }

    private static string? DecodeUtf8(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        try
        {
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream(capacity: Math.Min(maxBytes, 64 * 1024));
        var chunk = new byte[64 * 1024];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new ConflictException(UpstreamFailureMessage);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
