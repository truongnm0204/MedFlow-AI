using MedFlow.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace MedFlow.Application.Services.Auth;

/// <summary>Gửi email OTP — tách từ AuthService để giữ file nhỏ.</summary>
internal static class OtpEmailBuilder
{
    public static string BuildHtml(string fullName, string otp)
        => $"""
            <h2>MedFlow AI — Mã xác thực đăng ký</h2>
            <p>Xin chào <strong>{fullName}</strong>,</p>
            <p>Mã OTP của bạn: <strong style="font-size:24px;letter-spacing:4px">{otp}</strong></p>
            <p>Mã có hiệu lực trong <strong>5 phút</strong>.</p>
            """;

    /// <summary>Gửi OTP; trả false nếu gửi lỗi (đã audit ngoài).</summary>
    public static async Task<bool> SendAsync(
        IEmailSender sender, ILogger logger,
        string email, string fullName, string otp, CancellationToken ct)
    {
        try
        {
            await sender.SendAsync(email, "MedFlow AI — Mã xác thực đăng ký", BuildHtml(fullName, otp), ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send OTP email to {Email}", email);
            return false;
        }
    }
}
