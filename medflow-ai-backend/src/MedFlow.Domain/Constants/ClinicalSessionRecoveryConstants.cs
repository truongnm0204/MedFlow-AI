namespace MedFlow.Domain.Constants;

/// <summary>
/// Clinical Session Recovery authority (PR #859 / Issue #858, F01 Option B).
/// One narrowly-scoped recovery request/grant table — NOT a general delegation subsystem.
/// </summary>
public static class ClinicalSessionRecoveryStatuses
{
    public const string Requested = "REQUESTED";
    public const string Granted = "GRANTED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";
    public const string Recovered = "RECOVERED";
    public const string Revoked = "REVOKED";
    public const string Expired = "EXPIRED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Requested, Granted, Rejected, Cancelled, Recovered, Revoked, Expired,
    };

    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Rejected, Cancelled, Recovered, Revoked, Expired,
    };
}

/// <summary>Operation tokens bound into the idempotency tuple (actor, operation, request).</summary>
public static class ClinicalSessionRecoveryOperations
{
    public const string Request = "REQUEST";
    public const string Grant = "GRANT";
    public const string Reject = "REJECT";
    public const string Revoke = "REVOKE";
    public const string Cancel = "CANCEL";
    public const string Confirm = "CONFIRM";
}

public static class ClinicalSessionRecoveryConstants
{
    /// <summary>Server-side grant lifetime (never accepted from the client, never auto-renewed).</summary>
    public static readonly TimeSpan GrantLifetime = TimeSpan.FromHours(2);

    public const int ReasonMinLength = 3;
    public const int ReasonMaxLength = 500;
    public const int IdempotencyKeyMaxLength = 128;

    public const string ClinicalHandoffRequiredCode = "CLINICAL_HANDOFF_REQUIRED";
    public const string RecoveryQueueStatusReasonPrefix = "[CLINICAL_SESSION_RECOVERY]";

    public const string RequestNotFoundMessage =
        "[CLINICAL_SESSION_RECOVERY_NOT_FOUND] Không tìm thấy yêu cầu phục hồi phiên khám trong phạm vi được phép truy cập.";

    public const string ContextNotFoundMessage =
        "[CLINICAL_SESSION_RECOVERY_NOT_FOUND] Không tìm thấy lượt khám hoặc ngữ cảnh phục hồi phiên khám trong phạm vi được phép truy cập.";

    public const string ClinicalHandoffRequiredMessage =
        "[CLINICAL_HANDOFF_REQUIRED] Lượt khám đang có phiên lâm sàng chưa được phục hồi an toàn. Vui lòng hoàn tất quy trình phục hồi phiên khám (Clinical Session Recovery) trước khi tiếp tục điều phối.";

    public const string NotClinicalQueueMessage =
        "[RECOVERY_NOT_CLINICAL_QUEUE] Clinical Session Recovery chỉ áp dụng cho phiên khám lâm sàng (EXAMINATION / RETURN_CONSULTATION). Không dùng authority này để hủy bỏ cận lâm sàng hoặc dược.";

    public const string SessionNotInterruptedMessage =
        "[RECOVERY_SESSION_NOT_INTERRUPTED] Lượt hàng đợi không ở trạng thái phiên khám đang thực hiện (IN_PROGRESS / IN_CONSULTATION / SERVING). Không xác nhận phiên bị gián đoạn khi chưa có căn cứ.";

    public const string StaleStateVersionMessage =
        "[RECOVERY_STALE_STATE] Phiên bản trạng thái lượt hàng đợi đã thay đổi (stale StateVersion / assignment). Vui lòng tải lại và thử lại.";

    public const string UnresolvedRequestExistsMessage =
        "[RECOVERY_REQUEST_ALREADY_UNRESOLVED] Đã tồn tại một yêu cầu phục hồi chưa kết thúc cho lượt hàng đợi này.";

    public const string GrantExpiredMessage =
        "[RECOVERY_GRANT_EXPIRED] Grant phục hồi phiên khám đã hết hiệu lực. Không thể xác nhận recovery bằng grant này.";

    public const string GrantNotActiveMessage =
        "[RECOVERY_GRANT_NOT_ACTIVE] Yêu cầu phục hồi không ở trạng thái cho phép thao tác này.";

    public const string VisitTerminalMessage =
        "[RECOVERY_VISIT_TERMINAL] Lượt khám đã ở trạng thái kết thúc, không thể phục hồi phiên khám.";

    public const string IdempotencyMismatchMessage =
        "[RECOVERY_IDEMPOTENCY_MISMATCH] Idempotency key đã được dùng cho một thao tác hoặc payload khác.";

    public const string AlreadyRecoveredMessage =
        "[RECOVERY_ALREADY_RECOVERED] Yêu cầu phục hồi đã được xác nhận bởi thao tác khác.";

    public const string ConfirmationAttestationRequiredMessage =
        "[RECOVERY_ATTESTATION_REQUIRED] Bác sĩ phải xác nhận phiên khám thực sự bị gián đoạn (ConfirmInterrupted = true).";

    public const string NoPhysicianAuthorityMessage =
        "[RECOVERY_NO_PHYSICIAN_AUTHORITY] Không có bác sĩ đủ thẩm quyền chuyên môn để thực hiện phục hồi phiên khám. Liên hệ điều phối chuyên môn của bệnh viện.";

    public const string SelfGrantMessage =
        "[RECOVERY_SELF_GRANT_FORBIDDEN] Bác sĩ cấp grant không được tự cấp grant cho chính mình.";

    public const string RecipientNotEligibleMessage =
        "[RECOVERY_RECIPIENT_NOT_ELIGIBLE] Bác sĩ nhận grant không đủ điều kiện (không hoạt động, thiếu CLINICAL_SESSION_RECOVER, sai bệnh viện hoặc sai chuyên môn).";

    public const string IssuerNotEligibleMessage =
        "[RECOVERY_ISSUER_NOT_ELIGIBLE] Bác sĩ cấp grant không đủ điều kiện (không hoạt động, thiếu CLINICAL_SESSION_RECOVERY_GRANT, sai bệnh viện hoặc sai chuyên môn).";

    // Audit actions — reasons are persisted in audit_logs.reason only, never in log/SignalR payloads.
    public const string RecoveryRequestedAuditAction = "CLINICAL_SESSION_RECOVERY_REQUESTED";
    public const string RecoveryGrantedAuditAction = "CLINICAL_SESSION_RECOVERY_GRANTED";
    public const string RecoveryRejectedAuditAction = "CLINICAL_SESSION_RECOVERY_REJECTED";
    public const string RecoveryRevokedAuditAction = "CLINICAL_SESSION_RECOVERY_REVOKED";
    public const string RecoveryCancelledAuditAction = "CLINICAL_SESSION_RECOVERY_CANCELLED";
    public const string RecoveryExpiredAuditAction = "CLINICAL_SESSION_RECOVERY_EXPIRED";
    public const string RecoveryConfirmedAuditAction = "CLINICAL_SESSION_RECOVERY_CONFIRMED";
}
