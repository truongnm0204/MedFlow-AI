using System.Net;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Ekyc;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// eKYC session lifecycle: creation (consent-gated, quota-limited) and status reads.
/// ONE engine, three entry points (owner scope 2026-11-28):
///   * self-service registration / account activation (scope-limited token while PENDING);
///   * reception-assisted account registration — the same self-service session, staff presents it;
///   * reception create/verify patient profile — RECEPTION_ASSISTED + PATIENT_PROFILE.
/// Patient identity verification is separate from account activation: sessions with
/// purpose PATIENT_PROFILE never touch users.status.
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
    public const string IdentityCardRequiredMessage =
        "Vui lòng khai báo số CCCD/CMND trước khi xác minh danh tính (số khai báo phải khớp với ảnh giấy tờ).";
    public const string IdentityCardMismatchMessage =
        "Số CCCD khai báo không khớp với hồ sơ bệnh nhân hiện có. Vui lòng kiểm tra lại.";
    public const string PatientNotFoundMessage = "Không tìm thấy hồ sơ bệnh nhân.";
    public const string PatientAlreadyVerifiedMessage = "Hồ sơ bệnh nhân đã được xác minh danh tính, không cần chạy lại eKYC.";
    public const string PatientNotEligibleMessage = "Hồ sơ bệnh nhân không ở trạng thái cho phép xác minh danh tính.";
    public const string IdentityConflictMessage =
        "Số CCCD này đã gắn với hồ sơ bệnh nhân khác. Cần xử lý xung đột danh tính trước khi tạo phiên xác minh.";
    public const string QuotaExceededMessage =
        "Đã vượt giới hạn số phiên xác minh danh tính trong ngày. Vui lòng thử lại vào ngày mai hoặc tạo hồ sơ chưa xác minh.";
    public const string SelfVerifyForbiddenMessage =
        "Không thể tự xác minh danh tính cho hồ sơ gắn với chính tài khoản đang thao tác.";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly EkycOptions _options;
    private readonly IOptions<IdentityHashOptions>? _identityOptions;

    public EkycSessionService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IOptions<EkycOptions>? options = null,
        IOptions<IdentityHashOptions>? identityOptions = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options?.Value ?? new EkycOptions();
        _identityOptions = identityOptions;
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

        var consentVersion = ValidateConsent(request.ConsentAccepted, request.ConsentVersion);

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (user.AccountStatus != AccountStatus.ACTIVE
            && user.AccountStatus != AccountStatus.PENDING)
        {
            throw new ForbiddenException(AccountNotActiveMessage);
        }

        if (string.IsNullOrWhiteSpace(request.IdentityCard))
        {
            throw new ValidationException(IdentityCardRequiredMessage);
        }

        var declaredHmac = ComputeDeclaredIdentificationHmac(request.IdentityCard);

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var auditOccurredAt = _timeProvider.GetUtcNow().UtcDateTime;

        await EnforceQuotaAsync(
            userId: actorUserId,
            performedBy: null,
            actorUserId,
            ipAddress,
            auditOccurredAt,
            nowUtc,
            ct);

        // Snapshot declared registration identity so the finalize match does not depend on later
        // profile edits.
        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == actorUserId, ct);

        var expiresAt = nowUtc.AddMinutes(_options.SessionTtlMinutes);

        var session = new EkycVerificationSession
        {
            UserId = actorUserId,
            PatientId = patient?.Id,
            Status = EkycVerificationStatuses.Initiated,
            VerificationMode = EkycVerificationModes.SelfService,
            Purpose = EkycPurposes.AccountActivation,
            DeclaredFullName = patient?.FullName,
            DeclaredBirthDay = patient?.DateOfBirth,
            DeclaredIdentificationHmac = declaredHmac,
            ConsentVersion = consentVersion,
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
                ConsentVersion = consentVersion,
                ConsentAcceptedAt = nowUtc,
                ExpiresAt = expiresAt,
                VerificationMode = EkycVerificationModes.SelfService,
                Purpose = EkycPurposes.AccountActivation,
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

    public async Task<CreateEkycSessionResponse> CreateReceptionAsync(
        long actorUserId,
        CreateReceptionEkycSessionRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EkycStaffAuthority.RequireIdentityVerifyAsync(_db, actorUserId, _timeProvider, ct);

        if (request.PatientId <= 0)
        {
            throw new ValidationException(PatientNotFoundMessage);
        }

        var consentVersion = ValidateConsent(request.ConsentAccepted, request.ConsentVersion);

        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PatientId, ct)
            ?? throw new NotFoundException("Patient", request.PatientId);

        if (!string.Equals(patient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(PatientNotEligibleMessage);
        }

        if (string.Equals(patient.IdentityStatus, PatientIdentityStatuses.Verified, StringComparison.Ordinal))
        {
            throw new ConflictException(PatientAlreadyVerifiedMessage);
        }

        if (patient.UserId == actorUserId)
        {
            throw new ForbiddenException(SelfVerifyForbiddenMessage);
        }

        // Declared CCCD: request value or the record's previously declared HMAC. One is required —
        // the OCR number must be matched (owner scope), and dedupe needs the hash.
        var declaredHmac = string.IsNullOrWhiteSpace(request.IdentityCard)
            ? patient.IdentificationHashHmac
            : ComputeDeclaredIdentificationHmac(request.IdentityCard);

        if (string.IsNullOrWhiteSpace(declaredHmac))
        {
            throw new ValidationException(IdentityCardRequiredMessage);
        }

        if (!string.IsNullOrWhiteSpace(patient.IdentificationHashHmac)
            && !EkycCrypto.FixedTimeEquals(patient.IdentificationHashHmac, declaredHmac))
        {
            throw new ConflictException(IdentityCardMismatchMessage);
        }

        // Dedupe matrix (reception path): another profile carrying the same identification must be
        // resolved before a new verification session can run. NEVER auto-merge.
        var conflictingPatientIds = await _db.Patients
            .AsNoTracking()
            .Where(p => p.IdentificationHashHmac == declaredHmac && p.Id != patient.Id)
            .Select(p => p.Id)
            .Take(5)
            .ToListAsync(ct);
        if (conflictingPatientIds.Count > 0)
        {
            throw new ConflictException(IdentityConflictMessage);
        }

        await EnforceQuotaAsync(
            userId: patient.UserId,
            performedBy: actorUserId,
            actorUserId,
            ipAddress,
            _timeProvider.GetUtcNow().UtcDateTime,
            HospitalClock.GetUtcNowUnspecified(_timeProvider),
            ct);

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var auditOccurredAt = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = nowUtc.AddMinutes(_options.SessionTtlMinutes);

        var session = new EkycVerificationSession
        {
            UserId = patient.UserId,
            PatientId = patient.Id,
            Status = EkycVerificationStatuses.Initiated,
            VerificationMode = EkycVerificationModes.ReceptionAssisted,
            Purpose = EkycPurposes.PatientProfile,
            DeclaredFullName = patient.FullName,
            DeclaredBirthDay = patient.DateOfBirth,
            DeclaredIdentificationHmac = declaredHmac,
            ConsentVersion = consentVersion,
            ConsentAcceptedAt = nowUtc,
            ClientIp = IPAddress.TryParse(ipAddress, out var parsedIp) ? parsedIp : null,
            UserAgent = Truncate(userAgent, 1024),
            PerformedBy = actorUserId,
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
            EntityId = null,
            Reason = "Lễ tân tạo phiên xác minh danh tính cho hồ sơ bệnh nhân.",
            ChangeData = System.Text.Json.JsonSerializer.Serialize(new
            {
                patientId = patient.Id,
                ConsentVersion = consentVersion,
                ConsentAcceptedAt = nowUtc,
                VerificationMode = EkycVerificationModes.ReceptionAssisted,
                Purpose = EkycPurposes.PatientProfile,
            }),
            IpAddress = session.ClientIp,
            OccurredAt = auditOccurredAt,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = EkycConstants.AuditActionReceptionAssisted,
            EntityType = EkycConstants.AuditEntityTypePatient,
            EntityId = patient.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Reason = "Bắt đầu xác minh danh tính tại quầy tiếp đón.",
            ChangeData = System.Text.Json.JsonSerializer.Serialize(new
            {
                patientId = patient.Id,
                performedBy = actorUserId,
                verificationMode = EkycVerificationModes.ReceptionAssisted,
                purpose = EkycPurposes.PatientProfile,
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

        // Layer-2 ownership: subject account or the staff operator of a reception-assisted session.
        var isOperator = string.Equals(session.VerificationMode, EkycVerificationModes.ReceptionAssisted, StringComparison.Ordinal)
            && session.PerformedBy == actorUserId;
        if (session.UserId != actorUserId && !isOperator)
        {
            throw new NotFoundException("EkycVerificationSession", sessionId);
        }

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var expired = session.ExpiresAt <= nowUtc;

        return new EkycSessionStatusResponse(
            SessionId: session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Status: session.Status,
            VerificationMode: session.VerificationMode,
            Purpose: session.Purpose,
            BindingState: session.BindingState,
            AttemptCount: session.AttemptCount,
            ExpiresAt: session.ExpiresAt,
            Expired: expired,
            FailureReason: session.FailureReason,
            CreatedAt: session.CreatedAt);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string ValidateConsent(bool consentAccepted, string? requestedVersionRaw)
    {
        // Consent gate (hard requirement 11): BEFORE any image can be uploaded.
        if (!consentAccepted)
        {
            throw new ValidationException(ConsentRequiredMessage);
        }

        var configuredConsentVersion = _options.ConsentVersion?.Trim();
        if (string.IsNullOrWhiteSpace(configuredConsentVersion))
        {
            throw new InvalidOperationException("Ekyc:ConsentVersion chưa được cấu hình.");
        }

        var requestedVersion = requestedVersionRaw?.Trim();
        if (string.IsNullOrWhiteSpace(requestedVersion)
            || requestedVersion.Length > EkycConstants.ConsentVersionMaxLength
            || !string.Equals(requestedVersion, configuredConsentVersion, StringComparison.Ordinal))
        {
            throw new ValidationException(ConsentVersionMismatchMessage);
        }

        return requestedVersion;
    }

    private string ComputeDeclaredIdentificationHmac(string identityCard)
    {
        var canonical = IdentificationNumber.Normalize(identityCard);
        var hmacKey = EkycCrypto.RequireHmacKey(OptionsWrapper(), _identityOptions);
        return EkycCrypto.ComputeIdentificationHmac(canonical, hmacKey);
    }

    /// <summary>
    /// Quota enforcement: per subject per day and per staff member per day (ca làm việc ≈ ngày).
    /// Every rejection is audited. Failure code QUOTA_EXHAUSTED lets reception choose the
    /// UNVERIFIED-profile path deliberately.
    /// </summary>
    private async Task EnforceQuotaAsync(
        long? userId,
        long? performedBy,
        long actorUserId,
        string? ipAddress,
        DateTime auditOccurredAt,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var dayStart = nowUtc.Date;
        var dayEnd = dayStart.AddDays(1);

        if (userId is > 0 && _options.DailySessionQuotaPerSubject > 0)
        {
            var subjectCount = await _db.EkycVerificationSessions
                .AsNoTracking()
                .CountAsync(s => s.UserId == userId.Value
                                 && s.CreatedAt >= dayStart
                                 && s.CreatedAt < dayEnd, ct);
            if (subjectCount >= _options.DailySessionQuotaPerSubject)
            {
                await ThrowQuotaAsync(actorUserId, ipAddress, auditOccurredAt, "subject_daily", subjectCount, ct);
            }
        }

        if (performedBy is > 0 && _options.DailySessionQuotaPerStaff > 0)
        {
            var staffCount = await _db.EkycVerificationSessions
                .AsNoTracking()
                .CountAsync(s => s.PerformedBy == performedBy.Value
                                 && s.CreatedAt >= dayStart
                                 && s.CreatedAt < dayEnd, ct);
            if (staffCount >= _options.DailySessionQuotaPerStaff)
            {
                await ThrowQuotaAsync(actorUserId, ipAddress, auditOccurredAt, "staff_daily", staffCount, ct);
            }
        }
    }

    private async Task ThrowQuotaAsync(
        long actorUserId,
        string? ipAddress,
        DateTime auditOccurredAt,
        string scope,
        int currentCount,
        CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = EkycConstants.AuditActionQuotaExceeded,
            EntityType = EkycConstants.AuditEntityType,
            EntityId = null,
            Reason = "Từ chối tạo phiên eKYC do vượt giới hạn trong ngày.",
            ChangeData = System.Text.Json.JsonSerializer.Serialize(new
            {
                scope,
                currentCount,
                limit = scope == "staff_daily"
                    ? _options.DailySessionQuotaPerStaff
                    : _options.DailySessionQuotaPerSubject,
                code = EkycErrorCodes.QuotaExhausted,
            }),
            IpAddress = System.Net.IPAddress.TryParse(ipAddress, out var parsedIp) ? parsedIp : null,
            OccurredAt = auditOccurredAt,
        });

        await _db.SaveChangesAsync(ct);
        throw new EkycServiceException(EkycErrorCodes.QuotaExhausted, QuotaExceededMessage, 429);
    }

    private IOptions<EkycOptions> OptionsWrapper()
        => Microsoft.Extensions.Options.Options.Create(_options);

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
