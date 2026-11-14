using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MedFlow.Domain.Exceptions;

namespace MedFlow.Application.Services.Patients;

/// <summary>
/// Chuẩn hóa số định danh dùng chung cho mọi điểm nhập (CCCD 12 số hoặc CMND 9 số).
/// Không tự chuyển đổi giữa hai định dạng; chỉ bỏ khoảng trắng/gach ngang.
/// </summary>
internal static class IdentificationNumber
{
    public const string InvalidMessage = "Số CCCD/định danh không hợp lệ (cần 9 hoặc 12 chữ số).";

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

    /// <summary>Tạm thời giữ SHA-256; G1 thay bằng HMAC có khóa (ADR 0045).</summary>
    public static string ComputeSha256(string canonical)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}
