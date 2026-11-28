using System.Net;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Ekyc;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// eKYC session lifecycle: creation (consent-gated) and status reads.
/// TODO(slice-4 registration-scope): creation currently requires a fully authenticated patient
/// account. Plan v2.1 mục 3.1 requires pre-activation registration-scope sessions (scope-limited
/// token) — when that mechanism lands, this service must accept the registration-scope principal
/// and bind <see cref="EkycVerificationSession.RegistrationSessionId"/> instead of UserId.
/// The TODO is explicit at the auth-resolution site below; nothing is faked.
/// </summary>
public sealed class EkycSessionService : IEkycSessionService
{
    public const string ConsentRequiredMessage =
        "Yêu cầu xác minh danh tính điện tử cần có sự đồng ý của bạn (consent) trước khi tải ảnh lên.";
    public const string ConsentVersionMismatchMessage =
        "Phiên bản consent không còn hiệu lực. Vui lòng tải lại trang và chấp nhận phiên bản consent hiện hành.";
    public const string AccountNotActiveMessage =
        "Tài khoản không ở trạng thái cho phép tạo phiên xác minh danh tính.";
    public const string SessionNotFoundMessage = "Không tìm thấy phiên xác minh danh tính.";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly EkycOptions _options;

    public EkycSessionService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IOptions<EkycOptions>? options = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options?.Value ?? new EkycOptions();
    }

    public async Task<CreateEkycSessionResponse> CreateAsync(
        long actorUserId,
        CreateEkycSessionRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        // --- Consent gate (hard requirement 11): BEFORE any image can be uploaded -------------
        if (!request.ConsentAccepted)
        {
            throw new ValidationException(ConsentRequiredMessage);
        }

        var configuredConsentVersion = _options.ConsentVersion?.Trim();
        if (string.IsNullOrWhiteSpace(configuredConsentVersion))
        {
            throw new InvalidOperationException("Ekyc:ConsentVersion chưa được cấu hình.");
        }

        var requestedVersion = request.ConsentVersion?.Trim();
        if (string.IsNullOrWhiteSpace(requestedVersion)
            || requestedVersion.Length > EkycConstants.ConsentVersionMaxLength
            || !string.Equals(requestedVersion, configuredConsentVersion, StringComparison.Ordinal))
        {
            throw new ValidationException(ConsentVersionMismatchMessage);
        }

        // --- Actor validation ------------------------------------------------------------------
        // TODO(slice-4): registration-scope principal path — accept scope-limited registration
        // tokens here and bind RegistrationSessionId instead of requiring an ACTIVE patient user.
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (user.AccountStatus != AccountStatus.ACTIVE
            && user.AccountStatus != AccountStatus.PENDING)
        {
            throw new ForbiddenException(AccountNotActiveMessage);
        }

        // Session timestamps: ekyc_verification_sessions columns are timestamp-without-time-zone
        // (migration 029) → require Kind=Unspecified. audit_logs.occurred_at has no explicit EF type
        // mapping (Npgsql timestamptz inference) → requires Kind=Utc, matching AuditLogger convention.
        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var auditOccurredAt = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = nowUtc.AddMinutes(_options.SessionTtlMinutes);

        var session = new EkycVerificationSession
        {
            UserId = actorUserId,
            Status = EkycVerificationStatuses.Initiated,
            VerificationMethod = EkycVerificationMethods.EkycVnpt,
            ConsentVersion = requestedVersion,
            ConsentAcceptedAt = nowUtc,
            ClientIp = IPAddress.TryParse(ipAddress, out var parsedIp) ? parsedIp : null,
            UserAgent = Truncate(userAgent, 1024),
            ExpiresAt = expiresAt,
            AttemptCount = 0,
            BindingState = EkycBindingStates.Pending,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        _db.EkycVerificationSessions.Add(session);

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = EkycConstants.AuditActionSessionCreated,
            EntityType = EkycConstants.AuditEntityType,
            EntityId = null, // session id not yet generated — change_data carries consent evidence
            Reason = "Tạo phiên xác minh danh tính điện tử (eKYC).",
            ChangeData = System.Text.Json.JsonSerializer.Serialize(new
            {
                ConsentVersion = requestedVersion,
                ConsentAcceptedAt = nowUtc,
                ExpiresAt = expiresAt,
            }),
            IpAddress = session.ClientIp,
            OccurredAt = auditOccurredAt,
        });

        await _db.SaveChangesAsync(ct);

        return new CreateEkycSessionResponse(
            SessionId: session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ProxyBasePath: $"/api/v1/ekyc/proxy/{session.Id}",
            Status: session.Status,
            ExpiresAt: session.ExpiresAt);
    }

    public async Task<EkycSessionStatusResponse> GetStatusAsync(
        long actorUserId,
        long sessionId,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var session = await _db.EkycVerificationSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException("EkycVerificationSession", sessionId);

        // Ownership enforcement (layer-2 authz): only the owning account may read status.
        if (session.UserId != actorUserId)
        {
            throw new NotFoundException("EkycVerificationSession", sessionId);
        }

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var expired = session.ExpiresAt <= nowUtc;

        return new EkycSessionStatusResponse(
            SessionId: session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Status: session.Status,
            VerificationMethod: session.VerificationMethod,
            BindingState: session.BindingState,
            AttemptCount: session.AttemptCount,
            ExpiresAt: session.ExpiresAt,
            Expired: expired,
            FailureReason: session.FailureReason,
            CreatedAt: session.CreatedAt);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
