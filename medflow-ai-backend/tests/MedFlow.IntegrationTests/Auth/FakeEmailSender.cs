using System.Text.RegularExpressions;
using MedFlow.Application.Interfaces.Services;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>
/// Bắt email OTP trong test — lưu plaintext OTP trích từ HTML body
/// thay vì gọi SMTP thật. Cho phép verify-otp mà không bypass hash logic.
/// </summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<SentEmail> _sent = [];
    private static readonly Regex OtpRegex = new(@"<strong[^>]*>(\d{6})</strong>", RegexOptions.Compiled);

    public IReadOnlyList<SentEmail> Sent => _sent;

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var otp = OtpRegex.Match(htmlBody) is { Success: true } m ? m.Groups[1].Value : null;
        _sent.Add(new SentEmail(toEmail, subject, otp));
        return Task.CompletedTask;
    }

    /// <summary>OTP mới nhất đã gửi tới email (null nếu chưa gửi).</summary>
    public string? LatestOtpFor(string email)
        => _sent.LastOrDefault(e => e.ToEmail.Equals(email, StringComparison.OrdinalIgnoreCase))?.Otp;

    public void Clear() => _sent.Clear();
}

public sealed record SentEmail(string ToEmail, string Subject, string? Otp);
