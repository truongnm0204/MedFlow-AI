using MedFlow.Application.Options;
using MedFlow.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>Resolved non-secret + credential configuration for one VNPT eKYC call chain.</summary>
/// <param name="BackendUrl">VNPT AI gateway base URL.</param>
/// <param name="CompareThreshold">Minimum compare prob for positive verdict — config-driven, never hardcoded in logic.</param>
/// <param name="SessionTtlMinutes">Session lifetime.</param>
/// <param name="MaxAttempts">Max step re-submissions per session.</param>
/// <param name="TokenId">VNPT Token-id header credential (SECRET — env only).</param>
/// <param name="TokenKey">VNPT Token-key header credential (SECRET — env only).</param>
/// <param name="AccessToken">VNPT Bearer JWT (SECRET — env only, short-lived per probe Q5).</param>
/// <param name="PublicKeyCa">VNPT CA public key base64 DER/SPKI for dataSign verification (env only).</param>
internal sealed record VnptEkycConfig(
    string BackendUrl,
    double CompareThreshold,
    int SessionTtlMinutes,
    int MaxAttempts,
    string TokenId,
    string TokenKey,
    string AccessToken,
    string PublicKeyCa);

/// <summary>
/// Reads VNPT eKYC configuration from <see cref="EkycOptions"/> (env-backed, NEVER database).
/// Mirrors the reader style of <c>VietQrAccountPolicyReader</c> but sources from environment /
/// secret store instead of hospital policy rows, per hard requirement: credentials must never
/// be persisted in DB. Expected env bindings (see plans/ekyc-trial-credentials.env.example):
/// <c>Ekyc__TokenId</c>=VNPT_EKYC_TOKEN_ID, <c>Ekyc__TokenKey</c>=VNPT_EKYC_TOKEN_KEY,
/// <c>Ekyc__AccessToken</c>=VNPT_EKYC_ACCESS_TOKEN, <c>Ekyc__PublicKeyCa</c>=VNPT_EKYC_PUBLIC_KEY_CA,
/// <c>Ekyc__BackendUrl</c>=VNPT_EKYC_BACKEND_URL.
/// </summary>
internal static class VnptEkycConfigReader
{
    public const string MissingCredentialMessage =
        "Cấu hình VNPT eKYC chưa đầy đủ: thiếu credential bí mật (TokenId/TokenKey/AccessToken/PublicKeyCa). Cấu hình qua biến môi trường Ekyc__* — không lưu trong cơ sở dữ liệu.";

    public const string InvalidConfigMessage =
        "Cấu hình VNPT eKYC không hợp lệ: CompareThreshold phải trong khoảng (0, 100], SessionTtlMinutes và MaxAttempts phải dương, BackendUrl không được trống.";

    public static VnptEkycConfig Read(IOptions<EkycOptions>? options)
    {
        var value = options?.Value;

        var backendUrl = value?.BackendUrl?.Trim();
        var compareThreshold = value?.CompareThreshold ?? 0d;
        var sessionTtl = value?.SessionTtlMinutes ?? 0;
        var maxAttempts = value?.MaxAttempts ?? 0;

        if (string.IsNullOrWhiteSpace(backendUrl)
            || compareThreshold is <= 0d or > 100d
            || sessionTtl <= 0
            || maxAttempts <= 0)
        {
            throw new ConflictException(InvalidConfigMessage);
        }

        var tokenId = value?.TokenId?.Trim();
        var tokenKey = value?.TokenKey?.Trim();
        var accessToken = value?.AccessToken?.Trim();
        var publicKeyCa = value?.PublicKeyCa?.Trim();

        if (string.IsNullOrWhiteSpace(tokenId)
            || string.IsNullOrWhiteSpace(tokenKey)
            || string.IsNullOrWhiteSpace(accessToken)
            || string.IsNullOrWhiteSpace(publicKeyCa))
        {
            throw new ConflictException(MissingCredentialMessage);
        }

        return new VnptEkycConfig(
            BackendUrl: backendUrl,
            CompareThreshold: compareThreshold,
            SessionTtlMinutes: sessionTtl,
            MaxAttempts: maxAttempts,
            TokenId: tokenId,
            TokenKey: tokenKey,
            AccessToken: accessToken,
            PublicKeyCa: publicKeyCa);
    }

    /// <summary>
    /// Non-throwing probe used by health/status surfaces: true iff every required credential
    /// is present (values are not validated for correctness — only presence).
    /// </summary>
    public static bool HasCredentials(IOptions<EkycOptions>? options)
    {
        var value = options?.Value;
        return !string.IsNullOrWhiteSpace(value?.TokenId)
            && !string.IsNullOrWhiteSpace(value?.TokenKey)
            && !string.IsNullOrWhiteSpace(value?.AccessToken)
            && !string.IsNullOrWhiteSpace(value?.PublicKeyCa);
    }
}
