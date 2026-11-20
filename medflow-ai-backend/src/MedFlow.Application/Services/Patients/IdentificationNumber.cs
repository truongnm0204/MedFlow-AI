using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MedFlow.Application.Options;
using MedFlow.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Patients;

/// <summary>
/// Chuẩn hóa số định danh dùng chung cho mọi điểm nhập (CCCD 12 số hoặc CMND 9 số).
/// Không tự chuyển đổi giữa hai định dạng; chỉ bỏ khoảng trắng/gach ngang.
/// </summary>
internal static class IdentificationNumber
{
    public const string InvalidMessage = "Số CCCD/định danh không hợp lệ (cần 9 hoặc 12 chữ số).";
    public const string MissingKeyMessage = "Identity:HashKey chưa được cấu hình.";
    public const short CurrentHashVersion = 1;

    private static readonly Regex ValidDigits = new(@"^\d{9}$|^\d{12}$", RegexOptions.Compiled);

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ValidationException(InvalidMessage);

        var compact = Regex.Replace(raw.Trim(), @"[\s\-]", string.Empty);
        if (!ValidDigits.IsMatch(compact))
            throw new ValidationException(InvalidMessage);

        return compact;
    }

    /// <summary>SHA-256 không khóa — chỉ dùng cho dual-read/dual-write trong giai đoạn chuyển tiếp.</summary>
    public static string ComputeSha256(string canonical)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

    public static string ComputeHmac(string canonical, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var digest = Encoding.UTF8.GetBytes(ComputeSha256(canonical));
        return Convert.ToHexString(HMACSHA256.HashData(keyBytes, digest)).ToLowerInvariant();
    }

    public static string RequireKey(IOptions<IdentityHashOptions>? options)
    {
        var key = options?.Value.HashKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(MissingKeyMessage);
        return key;
    }

    public static bool AllowShaFallback(IOptions<IdentityHashOptions>? options)
        => !string.Equals(options?.Value.Mode, "hmac", StringComparison.OrdinalIgnoreCase);
}
