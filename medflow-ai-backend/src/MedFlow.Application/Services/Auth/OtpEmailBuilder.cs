using System.Net;
using MedFlow.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace MedFlow.Application.Services.Auth;

/// <summary>Gửi email OTP — tách từ AuthService để giữ file nhỏ; tuyệt đối không ghi OTP ra log.</summary>
internal static class OtpEmailBuilder
{
    public static string BuildHtml(string fullName, string otp, int otpTtlMinutes = 5)
        => $"""
            <h2>MedFlow AI — Mã xác thực đăng ký</h2>
            <p>Xin chào <strong>{WebUtility.HtmlEncode(fullName)}</strong>,</p>
            <p>Mã OTP của bạn: <strong style="font-size:24px;letter-spacing:4px">{otp}</strong></p>
            <p>Mã có hiệu lực trong <strong>{otpTtlMinutes} phút</strong>.</p>
            """;

    public static string BuildPasswordResetHtml(string fullName, string otp, int otpTtlMinutes = 5)
        => $"""
            <h2>MedFlow AI — Mã OTP khôi phục mật khẩu</h2>
            <p>Xin chào <strong>{WebUtility.HtmlEncode(fullName)}</strong>,</p>
            <p>Bạn vừa yêu cầu đặt lại mật khẩu tài khoản MedFlow AI.</p>
            <p>Mã OTP khôi phục của bạn: <strong style="font-size:24px;letter-spacing:4px">{otp}</strong></p>
            <p>Mã chỉ sử dụng <strong>một lần</strong> và có hiệu lực trong <strong>{otpTtlMinutes} phút</strong>.</p>
            <p>Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email này.</p>
            """;

    /// <summary>Gửi OTP đăng ký; trả false nếu gửi lỗi (đã audit ngoài).</summary>
    public static async Task<bool> SendAsync(
        IEmailSender sender,
        ILogger logger,
        string email,
        string fullName,
        string otp,
        CancellationToken ct,
        int otpTtlMinutes = 5)
    {
        try
        {
            await sender.SendAsync(email, "MedFlow AI — Mã xác thực đăng ký", BuildHtml(fullName, otp, otpTtlMinutes), ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send OTP email to {Email}", email);
            return false;
        }
    }

    /// <summary>Gửi OTP khôi phục mật khẩu; che chắn lỗi SMTP để không lộ trạng thái tồn tại email.</summary>
    public static async Task<bool> SendPasswordResetAsync(
        IEmailSender sender,
        ILogger logger,
        string email,
        string fullName,
        string otp,
        CancellationToken ct,
        int otpTtlMinutes = 5)
    {
        try
        {
            await sender.SendAsync(
                email,
                "MedFlow AI — Mã OTP khôi phục mật khẩu",
                BuildPasswordResetHtml(fullName, otp, otpTtlMinutes),
                ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset OTP email.");
            return false;
        }
    }
}
