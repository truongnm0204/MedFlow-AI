using System.Net;
using System.Net.Mail;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MedFlow.Application.Services.SystemAdmin;

public sealed class IntegrationConfigService : IIntegrationConfigService
{
    public const string PolicyType = "INTEGRATION";
    public const string AuditActionUpdated = "INTEGRATION_CONFIG_UPDATED";
    public const string AuditEntityType = "INTEGRATION_CONFIG";

    public const string ProviderVietQrSePay = "VIETQR_SEPAY";
    public const string ProviderSmtpEmail = "SMTP_EMAIL";
    public const string ProviderAi = "AI_PROVIDER";

    private const string PolicyKeyVietQrSePay = "vietqr_sepay";
    private const string PolicyKeySmtpEmail = "smtp_email";
    private const string PolicyKeyAiProvider = "ai_provider";

    private const string DefaultTransferPrefix = "MF";
    private const int DefaultPendingExpiryMinutes = 15;
    private const string DefaultSmtpHost = "smtp.gmail.com";
    private const int DefaultSmtpPort = 587;
    private const string DefaultSmtpFromDisplayName = "MedFlow AI";
    private const string DefaultSmtpSubjectPrefix = "[MedFlow]";
    private const int DefaultSmtpTimeoutSeconds = 30;
    private const string DefaultAiProviderName = "GEMINI";
    private const string DefaultAiModelName = "gemini-2.5-flash";
    private const int DefaultAiTimeoutSeconds = 15;
    private const int DefaultAiMaxSuggestions = 3;
    private const string DefaultAiDisclaimer =
        "Gợi ý từ AI chỉ mang tính tham khảo điều hướng khám, không thay thế chẩn đoán y khoa của bác sĩ.";

    private static readonly HashSet<string> AllowedAiProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "GEMINI",
        "OPENAI",
        "DETERMINISTIC_CLINICAL",
    };

    private readonly IApplicationDbContext _db;
    private readonly IConfiguration? _configuration;
    private readonly TimeProvider _timeProvider;

    public IntegrationConfigService(
        IApplicationDbContext db,
        IConfiguration? configuration = null,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _configuration = configuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IntegrationConfigListResponseDto> GetIntegrationsAsync(
        long actorUserId,
        GetIntegrationsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (request.HospitalId is <= 0)
        {
            throw new ValidationException("Mã cơ sở y tế lọc không hợp lệ.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var access = await EnsureIntegrationAccessAsync(actorUserId, nowUtc, ct);

        var targetHospital = await ResolveHospitalForReadAsync(request.HospitalId, access, ct);

        var availableHospitalsQuery = _db.Hospitals
            .AsNoTracking()
            .Where(h => h.IsActive != false);

        if (!access.IsSystemAdmin)
        {
            availableHospitalsQuery = availableHospitalsQuery.Where(h => h.Id == targetHospital.Id);
        }

        var availableHospitals = (await availableHospitalsQuery
            .OrderBy(h => h.Id)
            .ToListAsync(ct))
            .Select(h => new IntegrationConfigHospitalOptionDto(h.Id, h.HospitalCode, h.Name))
            .ToArray();

        var integrations = await LoadAllProvidersAsync(targetHospital.Id, nowUtc, ct);

        return new IntegrationConfigListResponseDto(
            HospitalId: targetHospital.Id,
            HospitalCode: targetHospital.HospitalCode,
            HospitalName: targetHospital.Name,
            CanEdit: targetHospital.IsActive != false,
            GeneratedAtUtc: nowUtc,
            Integrations: integrations,
            AvailableHospitals: availableHospitals);
    }

    public async Task<IntegrationProviderItemDto> UpdateIntegrationAsync(
        long actorUserId,
        string providerKey,
        UpdateIntegrationConfigRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        RejectForbiddenSecretFields(request);

        var canonicalProviderKey = NormalizeProviderKey(providerKey);

        if (request.HospitalId is <= 0)
        {
            throw new ValidationException("Mã cơ sở y tế cập nhật không hợp lệ.");
        }

        if (request.Reason?.Trim() is { Length: > 300 })
        {
            throw new ValidationException("Ghi chú lý do cập nhật không được vượt quá 300 ký tự.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var access = await EnsureIntegrationAccessAsync(actorUserId, nowUtc, ct);
        var targetHospital = await ResolveHospitalForWriteAsync(request.HospitalId, access, ct);

        var integrationRows = await HospitalPolicyStore.LoadRowsAsync(
            _db, targetHospital.Id, PolicyType, tracking: true, ct);
        var currentIntegrations = HospitalPolicyStore.SelectCurrent(integrationRows, nowUtc);

        var paymentRows = await HospitalPolicyStore.LoadRowsAsync(
            _db, targetHospital.Id, PaymentPolicyKeys.PolicyType, tracking: true, ct);
        var currentPayment = HospitalPolicyStore.SelectCurrent(paymentRows, nowUtc);

        var (policyKey, newJson, oldJson, isChanged) = canonicalProviderKey switch
        {
            ProviderVietQrSePay => PrepareVietQrUpdate(
                request,
                currentIntegrations,
                currentPayment,
                paymentRows,
                targetHospital.Id,
                nowUtc),
            ProviderSmtpEmail => PrepareSmtpUpdate(request, currentIntegrations),
            ProviderAi => PrepareAiUpdate(request, currentIntegrations),
            _ => throw new ValidationException("Nhà cung cấp tích hợp không được hỗ trợ."),
        };

        if (isChanged)
        {
            var nextVersion = HospitalPolicyStore.AppendVersion(
                _db,
                integrationRows,
                targetHospital.Id,
                PolicyType,
                policyKey,
                newJson,
                nowUtc);

            var normalizedReason = string.IsNullOrWhiteSpace(request.Reason)
                ? $"Cập nhật cấu hình tích hợp {canonicalProviderKey}"
                : request.Reason.Trim();

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = targetHospital.Id,
                Action = AuditActionUpdated,
                EntityType = AuditEntityType,
                EntityId = $"{targetHospital.Id}:{canonicalProviderKey}",
                Reason = normalizedReason,
                ChangeData = JsonSerializer.Serialize(new
                {
                    HospitalId = targetHospital.Id,
                    ProviderKey = canonicalProviderKey,
                    PolicyKey = policyKey,
                    PolicyVersion = nextVersion,
                    OldValue = oldJson,
                    NewValue = newJson,
                }),
                IpAddress = IPAddress.TryParse(ipAddress, out var parsedIp) ? parsedIp : null,
                OccurredAt = nowUtc,
            });

            await _db.SaveChangesAsync(ct);
        }

        var refreshed = await LoadAllProvidersAsync(targetHospital.Id, nowUtc, ct);
        return refreshed.First(item => item.ProviderKey == canonicalProviderKey);
    }

    internal static string NormalizeProviderKey(string? rawProviderKey)
    {
        if (string.IsNullOrWhiteSpace(rawProviderKey))
        {
            throw new ValidationException("Mã nhà cung cấp tích hợp không được để trống.");
        }

        var normalized = rawProviderKey.Trim().Replace('-', '_').ToUpperInvariant();
        return normalized switch
        {
            "VIETQR_SEPAY" or "VIETQR" or "SEPAY" => ProviderVietQrSePay,
            "SMTP_EMAIL" or "SMTP" => ProviderSmtpEmail,
            "AI_PROVIDER" or "AI" => ProviderAi,
            _ => throw new ValidationException(
                $"Nhà cung cấp tích hợp '{rawProviderKey.Trim()}' không được hỗ trợ. Chỉ chấp nhận VIETQR_SEPAY, SMTP_EMAIL hoặc AI_PROVIDER."),
        };
    }

    private static void RejectForbiddenSecretFields(UpdateIntegrationConfigRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ApiKey) ||
            !string.IsNullOrWhiteSpace(request.Secret) ||
            !string.IsNullOrWhiteSpace(request.WebhookApiKey) ||
            !string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ValidationException(
                "Không được phép gửi hoặc lưu khóa bí mật (API Key / Secret / Password) vào cấu hình cơ sở. Vui lòng cấu hình qua biến môi trường hoặc Secret Manager.");
        }
    }

    private async Task<IntegrationAccessScope> EnsureIntegrationAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản quản trị không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản quản trị đang bị tạm khóa.");
        }

        var actorRoleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var actorRoleCodes = actorRoleIds.Count == 0
            ? []
            : await _db.Roles
                .AsNoTracking()
                .Where(r => actorRoleIds.Contains(r.Id))
                .Select(r => r.Code)
                .ToListAsync(ct);

        var isSystemAdmin = actorRoleCodes.Any(code =>
            string.Equals(code, RoleNames.SystemAdmin, StringComparison.OrdinalIgnoreCase));
        var isHospitalAdmin = actorRoleCodes.Any(code =>
            string.Equals(code, RoleNames.HospitalAdmin, StringComparison.OrdinalIgnoreCase));

        if (!isSystemAdmin && !isHospitalAdmin)
        {
            throw new ForbiddenException("Người dùng không có quyền quản lý cấu hình tích hợp hệ thống.");
        }

        var hasHospitalConfig = actorRoleIds.Count > 0 && await (
            from rp in _db.RolePermissions.AsNoTracking()
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where actorRoleIds.Contains(rp.RoleId) &&
                  p.Code.ToUpper() == PermissionCodes.HospitalConfig
            select rp.PermissionId).AnyAsync(ct);

        if (!hasHospitalConfig)
        {
            throw new ForbiddenException("Tài khoản quản trị chưa được cấp quyền cấu hình cơ sở (HOSPITAL_CONFIG).");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên đã ngừng hoạt động.");
        }

        if (!isSystemAdmin && actorStaff?.HospitalId is null)
        {
            throw new ForbiddenException("Quản trị viên bệnh viện chưa được gán phạm vi cơ sở y tế.");
        }

        return new IntegrationAccessScope(isSystemAdmin, actorStaff?.HospitalId);
    }

    private async Task<Hospital> ResolveHospitalForReadAsync(
        long? requestedHospitalId,
        IntegrationAccessScope access,
        CancellationToken ct)
    {
        if (!access.IsSystemAdmin)
        {
            var ownHospitalId = access.HospitalId!.Value;
            if (requestedHospitalId.HasValue && requestedHospitalId.Value != ownHospitalId)
            {
                throw new ForbiddenException("Không được phép xem cấu hình tích hợp của cơ sở y tế khác.");
            }

            var ownHospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == ownHospitalId, ct)
                ?? throw new NotFoundException("Hospital", ownHospitalId);

            if (ownHospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế được gán cho quản trị viên hiện đang ngừng hoạt động.");
            }

            return ownHospital;
        }

        var targetId = requestedHospitalId ?? access.HospitalId;
        if (targetId.HasValue)
        {
            return await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == targetId.Value, ct)
                ?? throw new NotFoundException("Hospital", targetId.Value);
        }

        return await _db.Hospitals
            .AsNoTracking()
            .Where(h => h.IsActive != false)
            .OrderBy(h => h.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Hospital", 0);
    }

    private async Task<Hospital> ResolveHospitalForWriteAsync(
        long? requestedHospitalId,
        IntegrationAccessScope access,
        CancellationToken ct)
    {
        long targetHospitalId;
        if (!access.IsSystemAdmin)
        {
            var ownHospitalId = access.HospitalId!.Value;
            if (requestedHospitalId.HasValue && requestedHospitalId.Value != ownHospitalId)
            {
                throw new ForbiddenException("Không được phép cập nhật cấu hình tích hợp của cơ sở y tế khác.");
            }

            targetHospitalId = ownHospitalId;
        }
        else
        {
            var resolved = requestedHospitalId ?? access.HospitalId;
            if (!resolved.HasValue)
            {
                throw new ValidationException("Vui lòng chỉ định mã cơ sở y tế (hospitalId) cần cập nhật cấu hình tích hợp.");
            }

            targetHospitalId = resolved.Value;
        }

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == targetHospitalId, ct)
            ?? throw new NotFoundException("Hospital", targetHospitalId);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế mục tiêu hiện đang ngừng hoạt động.");
        }

        return hospital;
    }

    private async Task<IReadOnlyList<IntegrationProviderItemDto>> LoadAllProvidersAsync(
        long hospitalId,
        DateTime nowUtcUnspecified,
        CancellationToken ct)
    {
        var integrationRows = await HospitalPolicyStore.LoadRowsAsync(
            _db, hospitalId, PolicyType, tracking: false, ct);
        var currentIntegrations = HospitalPolicyStore.SelectCurrent(integrationRows, nowUtcUnspecified);

        var paymentRows = await HospitalPolicyStore.LoadRowsAsync(
            _db, hospitalId, PaymentPolicyKeys.PolicyType, tracking: false, ct);
        var currentPayment = HospitalPolicyStore.SelectCurrent(paymentRows, nowUtcUnspecified);

        currentIntegrations.TryGetValue(PolicyKeyVietQrSePay, out var vietQrRow);
        currentPayment.TryGetValue(PaymentPolicyKeys.VietQrAccount, out var paymentVietQrRow);
        currentIntegrations.TryGetValue(PolicyKeySmtpEmail, out var smtpRow);
        currentIntegrations.TryGetValue(PolicyKeyAiProvider, out var aiRow);

        return
        [
            BuildVietQrDto(vietQrRow, paymentVietQrRow),
            BuildSmtpDto(smtpRow),
            BuildAiDto(aiRow),
        ];
    }

    private IntegrationProviderItemDto BuildVietQrDto(
        HospitalPolicy? integrationRow,
        HospitalPolicy? paymentAccountRow)
    {
        var runtimeWebhookSecret = _configuration?["SePay:WebhookApiKey"];
        var (hasWebhookSecret, maskedWebhookSecret) = SensitiveDataMasker.MaskRuntimeSecret(runtimeWebhookSecret);
        var defaultExpiry = GetConfigInt("QrPayment:PendingExpiryMinutes", DefaultPendingExpiryMinutes);

        var bankBin = ReadJsonString(integrationRow, "bankBin")
            ?? ReadJsonString(paymentAccountRow, "bin");
        var accountNumber = ReadJsonString(integrationRow, "accountNumber")
            ?? ReadJsonString(paymentAccountRow, "accountNumber");
        var accountName = ReadJsonString(integrationRow, "accountName")
            ?? ReadJsonString(paymentAccountRow, "accountName");

        var hasBankAccount = !string.IsNullOrWhiteSpace(bankBin) &&
                             !string.IsNullOrWhiteSpace(accountNumber) &&
                             !string.IsNullOrWhiteSpace(accountName);

        var enabled = HospitalPolicyStore.ReadBool(
            integrationRow,
            "enabled",
            defaultValue: hasBankAccount);
        var transferPrefix = ReadJsonString(integrationRow, "transferPrefix") ?? DefaultTransferPrefix;
        var pendingExpiry = HospitalPolicyStore.ReadInt(
            integrationRow,
            "pendingExpiryMinutes",
            defaultExpiry);

        var status = !enabled
            ? "DISABLED"
            : (hasBankAccount && hasWebhookSecret ? "ACTIVE" : "NEEDS_SETUP");

        var effectiveRow = integrationRow ?? paymentAccountRow;
        var lastUpdatedAt = effectiveRow?.EffectiveFrom is { } dt
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : (DateTime?)null;

        return new IntegrationProviderItemDto(
            ProviderKey: ProviderVietQrSePay,
            DisplayName: "VietQR & Cổng đối soát SePay",
            Category: "PAYMENT",
            Description: "Sinh mã VietQR theo viện phí lượt khám và tự động xác nhận thanh toán qua SePay Webhook.",
            Enabled: enabled,
            Status: status,
            PolicyVersion: effectiveRow?.PolicyVersion ?? 0,
            LastUpdatedAt: lastUpdatedAt,
            SecretStatus: new IntegrationRuntimeSecretStatusDto(
                SecretName: "SePay:WebhookApiKey",
                IsConfigured: hasWebhookSecret,
                MaskedValue: maskedWebhookSecret,
                SourceHint: "Biến môi trường / User Secrets (chỉ đọc)"),
            VietQr: new VietQrIntegrationSettingsDto(
                BankBin: bankBin,
                AccountNumber: accountNumber,
                AccountName: accountName,
                TransferPrefix: transferPrefix,
                PendingExpiryMinutes: pendingExpiry,
                WebhookEndpointPath: "/api/v1/payments/sepay/webhook"),
            Smtp: null,
            Ai: null);
    }

    private IntegrationProviderItemDto BuildSmtpDto(HospitalPolicy? smtpRow)
    {
        var host = GetConfigString("Smtp:Host") ?? DefaultSmtpHost;
        var port = GetConfigInt("Smtp:Port", DefaultSmtpPort);
        var username = GetConfigString("Smtp:Username");
        var defaultFromName = GetConfigString("Smtp:FromName") ?? DefaultSmtpFromDisplayName;
        var runtimePassword = _configuration?["Smtp:Password"];
        var (hasPassword, maskedPassword) = SensitiveDataMasker.MaskRuntimeSecret(runtimePassword);

        var enabled = HospitalPolicyStore.ReadBool(smtpRow, "enabled", defaultValue: true);
        var fromDisplayName = ReadJsonString(smtpRow, "fromDisplayName") ?? defaultFromName;
        var replyToEmail = ReadJsonString(smtpRow, "replyToEmail");
        var subjectPrefix = ReadJsonString(smtpRow, "subjectPrefix") ?? DefaultSmtpSubjectPrefix;
        var timeoutSeconds = HospitalPolicyStore.ReadInt(smtpRow, "timeoutSeconds", DefaultSmtpTimeoutSeconds);

        var status = !enabled
            ? "DISABLED"
            : (!string.IsNullOrWhiteSpace(username) && hasPassword ? "ACTIVE" : "NEEDS_SETUP");

        var lastUpdatedAt = smtpRow?.EffectiveFrom is { } dt
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : (DateTime?)null;

        return new IntegrationProviderItemDto(
            ProviderKey: ProviderSmtpEmail,
            DisplayName: "Dịch vụ Email Giao dịch (SMTP)",
            Category: "NOTIFICATION",
            Description: "Gửi mã xác thực OTP đăng ký tài khoản, liên kết hồ sơ gia đình và thông báo lịch hẹn.",
            Enabled: enabled,
            Status: status,
            PolicyVersion: smtpRow?.PolicyVersion ?? 0,
            LastUpdatedAt: lastUpdatedAt,
            SecretStatus: new IntegrationRuntimeSecretStatusDto(
                SecretName: "Smtp:Password",
                IsConfigured: hasPassword,
                MaskedValue: maskedPassword,
                SourceHint: "Biến môi trường / User Secrets (chỉ đọc)"),
            VietQr: null,
            Smtp: new SmtpIntegrationSettingsDto(
                Host: host,
                Port: port,
                SenderIdentity: MaskEmailAddress(username),
                FromDisplayName: fromDisplayName,
                ReplyToEmail: replyToEmail,
                SubjectPrefix: subjectPrefix,
                TimeoutSeconds: timeoutSeconds),
            Ai: null);
    }

    private IntegrationProviderItemDto BuildAiDto(HospitalPolicy? aiRow)
    {
        var runtimeApiKey = FirstNonEmpty(
            _configuration?["AiRecommendation:ApiKey"],
            _configuration?["Ai:ApiKey"],
            _configuration?["Gemini:ApiKey"],
            _configuration?["OpenAi:ApiKey"]);
        var (hasApiKey, maskedApiKey) = SensitiveDataMasker.MaskRuntimeSecret(runtimeApiKey);
        var runtimeDefaultModel = FirstNonEmpty(
            _configuration?["AiRecommendation:Model"],
            _configuration?["Ai:Model"]) ?? DefaultAiModelName;

        var enabled = HospitalPolicyStore.ReadBool(aiRow, "enabled", defaultValue: true);
        var providerName = ReadJsonString(aiRow, "providerName") ?? DefaultAiProviderName;
        var modelName = ReadJsonString(aiRow, "modelName") ?? runtimeDefaultModel;
        var timeoutSeconds = HospitalPolicyStore.ReadInt(aiRow, "timeoutSeconds", DefaultAiTimeoutSeconds);
        var maxSuggestions = HospitalPolicyStore.ReadInt(aiRow, "maxSuggestions", DefaultAiMaxSuggestions);
        var disclaimer = ReadJsonString(aiRow, "disclaimerTemplate") ?? DefaultAiDisclaimer;

        var isDeterministic = string.Equals(providerName, "DETERMINISTIC_CLINICAL", StringComparison.OrdinalIgnoreCase);
        var status = !enabled
            ? "DISABLED"
            : (isDeterministic || hasApiKey ? "ACTIVE" : "NEEDS_SETUP");

        var lastUpdatedAt = aiRow?.EffectiveFrom is { } dt
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : (DateTime?)null;

        return new IntegrationProviderItemDto(
            ProviderKey: ProviderAi,
            DisplayName: "Trợ lý AI Điều hướng Chuyên khoa & Lịch khám",
            Category: "AI_ASSISTANT",
            Description: "Phân tích triệu chứng gợi ý chuyên khoa/dịch vụ khám và đề xuất khung giờ khám tối ưu (kèm miễn trừ y khoa).",
            Enabled: enabled,
            Status: status,
            PolicyVersion: aiRow?.PolicyVersion ?? 0,
            LastUpdatedAt: lastUpdatedAt,
            SecretStatus: new IntegrationRuntimeSecretStatusDto(
                SecretName: "AiRecommendation:ApiKey",
                IsConfigured: hasApiKey,
                MaskedValue: maskedApiKey,
                SourceHint: "Biến môi trường / User Secrets (chỉ đọc)"),
            VietQr: null,
            Smtp: null,
            Ai: new AiIntegrationSettingsDto(
                ProviderName: providerName,
                ModelName: modelName,
                TimeoutSeconds: timeoutSeconds,
                MaxSuggestions: maxSuggestions,
                DisclaimerTemplate: disclaimer));
    }

    private (string PolicyKey, string NewJson, string? OldJson, bool IsChanged) PrepareVietQrUpdate(
        UpdateIntegrationConfigRequest request,
        Dictionary<string, HospitalPolicy> currentIntegrations,
        Dictionary<string, HospitalPolicy> currentPayment,
        List<HospitalPolicy> paymentRows,
        long hospitalId,
        DateTime nowUtcUnspecified)
    {
        if (request.Enabled is null)
        {
            throw new ValidationException("Trạng thái bật/tắt (enabled) của tích hợp VietQR là bắt buộc.");
        }

        currentIntegrations.TryGetValue(PolicyKeyVietQrSePay, out var existingRow);
        currentPayment.TryGetValue(PaymentPolicyKeys.VietQrAccount, out var existingPaymentRow);

        var bankBin = NormalizeOptional(request.BankBin)
            ?? ReadJsonString(existingRow, "bankBin")
            ?? ReadJsonString(existingPaymentRow, "bin");
        var accountNumber = NormalizeOptional(request.AccountNumber)
            ?? ReadJsonString(existingRow, "accountNumber")
            ?? ReadJsonString(existingPaymentRow, "accountNumber");
        var accountName = NormalizeOptional(request.AccountName)
            ?? ReadJsonString(existingRow, "accountName")
            ?? ReadJsonString(existingPaymentRow, "accountName");

        var hasAnyAccountField = bankBin is not null || accountNumber is not null || accountName is not null;
        if (request.Enabled.Value || hasAnyAccountField)
        {
            if (bankBin is null || bankBin.Length != 6 || !bankBin.All(static c => c is >= '0' and <= '9'))
            {
                throw new ValidationException("Mã BIN ngân hàng VietQR phải gồm đúng 6 chữ số (ví dụ: 970422).");
            }

            if (accountNumber is null || accountNumber.Length is < 6 or > 19 || !accountNumber.All(static c => c is >= '0' and <= '9'))
            {
                throw new ValidationException("Số tài khoản nhận VietQR phải gồm từ 6 đến 19 chữ số.");
            }

            if (accountName is null || accountName.Length is < 2 or > 50)
            {
                throw new ValidationException("Tên chủ tài khoản nhận VietQR phải từ 2 đến 50 ký tự.");
            }

            accountName = accountName.ToUpperInvariant();
        }

        var transferPrefix = (NormalizeOptional(request.TransferPrefix)
            ?? ReadJsonString(existingRow, "transferPrefix")
            ?? DefaultTransferPrefix).ToUpperInvariant();

        if (transferPrefix.Length is < 2 or > 12 || !transferPrefix.All(char.IsLetterOrDigit))
        {
            throw new ValidationException("Tiền tố nội dung chuyển khoản phải từ 2 đến 12 ký tự chữ hoặc số.");
        }

        var defaultExpiry = GetConfigInt("QrPayment:PendingExpiryMinutes", DefaultPendingExpiryMinutes);
        var pendingExpiry = request.PendingExpiryMinutes
            ?? HospitalPolicyStore.ReadInt(existingRow, "pendingExpiryMinutes", defaultExpiry);

        if (pendingExpiry is < 1 or > 120)
        {
            throw new ValidationException("Thời gian hiệu lực mã QR thanh toán phải từ 1 đến 120 phút.");
        }

        var newJson = JsonSerializer.Serialize(new
        {
            enabled = request.Enabled.Value,
            bankBin,
            accountNumber,
            accountName,
            transferPrefix,
            pendingExpiryMinutes = pendingExpiry,
        });

        var isChanged = existingRow is null || !string.Equals(existingRow.PolicyValue, newJson, StringComparison.Ordinal);

        if (bankBin is not null && accountNumber is not null && accountName is not null)
        {
            var paymentJson = JsonSerializer.Serialize(new
            {
                bin = bankBin,
                accountNumber,
                accountName,
            });

            if (existingPaymentRow is null || !string.Equals(existingPaymentRow.PolicyValue, paymentJson, StringComparison.Ordinal))
            {
                HospitalPolicyStore.AppendVersion(
                    _db,
                    paymentRows,
                    hospitalId,
                    PaymentPolicyKeys.PolicyType,
                    PaymentPolicyKeys.VietQrAccount,
                    paymentJson,
                    nowUtcUnspecified);
                isChanged = true;
            }
        }

        return (PolicyKeyVietQrSePay, newJson, existingRow?.PolicyValue, isChanged);
    }

    private (string PolicyKey, string NewJson, string? OldJson, bool IsChanged) PrepareSmtpUpdate(
        UpdateIntegrationConfigRequest request,
        Dictionary<string, HospitalPolicy> currentIntegrations)
    {
        if (request.Enabled is null)
        {
            throw new ValidationException("Trạng thái bật/tắt (enabled) của tích hợp SMTP Email là bắt buộc.");
        }

        currentIntegrations.TryGetValue(PolicyKeySmtpEmail, out var existingRow);
        var defaultFromName = GetConfigString("Smtp:FromName") ?? DefaultSmtpFromDisplayName;

        var fromDisplayName = NormalizeOptional(request.FromDisplayName)
            ?? ReadJsonString(existingRow, "fromDisplayName")
            ?? defaultFromName;
        if (fromDisplayName.Length is < 2 or > 80)
        {
            throw new ValidationException("Tên hiển thị người gửi Email phải từ 2 đến 80 ký tự.");
        }

        var replyToEmail = request.ReplyToEmail is not null
            ? NormalizeOptional(request.ReplyToEmail)
            : ReadJsonString(existingRow, "replyToEmail");

        if (replyToEmail is not null)
        {
            if (replyToEmail.Length > 120 || !IsValidEmail(replyToEmail))
            {
                throw new ValidationException("Địa chỉ email phản hồi (Reply-To) không hợp lệ.");
            }
        }

        var subjectPrefix = NormalizeOptional(request.SubjectPrefix)
            ?? ReadJsonString(existingRow, "subjectPrefix")
            ?? DefaultSmtpSubjectPrefix;
        if (subjectPrefix.Length is < 2 or > 30)
        {
            throw new ValidationException("Tiền tố tiêu đề Email phải từ 2 đến 30 ký tự.");
        }

        var timeoutSeconds = request.TimeoutSeconds
            ?? HospitalPolicyStore.ReadInt(existingRow, "timeoutSeconds", DefaultSmtpTimeoutSeconds);
        if (timeoutSeconds is < 5 or > 120)
        {
            throw new ValidationException("Thời gian chờ kết nối SMTP phải từ 5 đến 120 giây.");
        }

        var newJson = JsonSerializer.Serialize(new
        {
            enabled = request.Enabled.Value,
            fromDisplayName,
            replyToEmail,
            subjectPrefix,
            timeoutSeconds,
        });

        var isChanged = existingRow is null || !string.Equals(existingRow.PolicyValue, newJson, StringComparison.Ordinal);
        return (PolicyKeySmtpEmail, newJson, existingRow?.PolicyValue, isChanged);
    }

    private (string PolicyKey, string NewJson, string? OldJson, bool IsChanged) PrepareAiUpdate(
        UpdateIntegrationConfigRequest request,
        Dictionary<string, HospitalPolicy> currentIntegrations)
    {
        if (request.Enabled is null)
        {
            throw new ValidationException("Trạng thái bật/tắt (enabled) của tích hợp AI Provider là bắt buộc.");
        }

        currentIntegrations.TryGetValue(PolicyKeyAiProvider, out var existingRow);
        var runtimeDefaultModel = FirstNonEmpty(
            _configuration?["AiRecommendation:Model"],
            _configuration?["Ai:Model"]) ?? DefaultAiModelName;

        var providerName = (NormalizeOptional(request.ProviderName)
            ?? ReadJsonString(existingRow, "providerName")
            ?? DefaultAiProviderName).ToUpperInvariant();
        if (!AllowedAiProviders.Contains(providerName))
        {
            throw new ValidationException("Nhà cung cấp AI không hợp lệ. Chỉ chấp nhận GEMINI, OPENAI hoặc DETERMINISTIC_CLINICAL.");
        }

        var modelName = NormalizeOptional(request.ModelName)
            ?? ReadJsonString(existingRow, "modelName")
            ?? runtimeDefaultModel;
        if (modelName.Length is < 2 or > 80)
        {
            throw new ValidationException("Tên mô hình AI (Model) phải từ 2 đến 80 ký tự.");
        }

        var timeoutSeconds = request.TimeoutSeconds
            ?? HospitalPolicyStore.ReadInt(existingRow, "timeoutSeconds", DefaultAiTimeoutSeconds);
        if (timeoutSeconds is < 3 or > 60)
        {
            throw new ValidationException("Thời gian chờ phản hồi AI phải từ 3 đến 60 giây.");
        }

        var maxSuggestions = request.MaxSuggestions
            ?? HospitalPolicyStore.ReadInt(existingRow, "maxSuggestions", DefaultAiMaxSuggestions);
        if (maxSuggestions is < 1 or > 5)
        {
            throw new ValidationException("Số lượng gợi ý tối đa của AI phải từ 1 đến 5.");
        }

        var disclaimer = NormalizeOptional(request.DisclaimerTemplate)
            ?? ReadJsonString(existingRow, "disclaimerTemplate")
            ?? DefaultAiDisclaimer;
        if (disclaimer.Length is < 10 or > 300)
        {
            throw new ValidationException("Nội dung tuyên bố miễn trừ y khoa phải từ 10 đến 300 ký tự.");
        }

        var newJson = JsonSerializer.Serialize(new
        {
            enabled = request.Enabled.Value,
            providerName,
            modelName,
            timeoutSeconds,
            maxSuggestions,
            disclaimerTemplate = disclaimer,
        });

        var isChanged = existingRow is null || !string.Equals(existingRow.PolicyValue, newJson, StringComparison.Ordinal);
        return (PolicyKeyAiProvider, newJson, existingRow?.PolicyValue, isChanged);
    }

    private static string? ReadJsonString(HospitalPolicy? row, string propertyName)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.PolicyValue))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.PolicyValue);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var val = prop.GetString();
                return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
            }
        }
        catch (JsonException)
        {
            // Ignore malformed JSON and fallback to default.
        }

        return null;
    }

    private string? GetConfigString(string key)
    {
        var raw = _configuration?[key];
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private int GetConfigInt(string key, int fallback)
    {
        var raw = _configuration?[key];
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var item in candidates)
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                return item.Trim();
            }
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new MailAddress(email);
            return string.Equals(addr.Address, email, StringComparison.OrdinalIgnoreCase) &&
                   email.Contains('.', StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string? MaskEmailAddress(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();
        var atIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 1)
        {
            return "***" + (atIndex >= 0 ? trimmed[atIndex..] : string.Empty);
        }

        var local = trimmed[..atIndex];
        var domain = trimmed[atIndex..];
        var visiblePrefix = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visiblePrefix}***{domain}";
    }

    private sealed record IntegrationAccessScope(bool IsSystemAdmin, long? HospitalId);
}
