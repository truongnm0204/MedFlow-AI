using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedFlow.Application.Services.Auth;

/// <summary>OTP challenge: verify + resend — tách từ AuthService để giữ file nhỏ.</summary>
public class AuthOtpService
{
    private readonly IApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthOtpService> _logger;

    private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResendThrottle = TimeSpan.FromSeconds(45);

    public AuthOtpService(
        IApplicationDbContext db,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILogger<AuthOtpService> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct = default)
    {
        var emailLower = request.Email.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == emailLower, ct)
            ?? throw new ValidationException("OTP không hợp lệ");

        if (user.AccountStatus == AccountStatus.ACTIVE)
            throw new ValidationException("Tài khoản đã được kích hoạt, vui lòng đăng nhập");

        var challenge = await _db.AuthVerificationChallenges
            .Where(c => c.UserId == user.Id
                && c.Purpose == ChallengePurpose.REGISTER_EMAIL
                && c.Status == ChallengeStatus.PENDING)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new ValidationException("OTP đã hết hạn hoặc không tồn tại");

        if (challenge.ExpiresAt <= DateTime.UtcNow)
        {
            challenge.Status = ChallengeStatus.EXPIRED;
            await _db.SaveChangesAsync(ct);
            throw new ValidationException("OTP đã hết hạn, vui lòng yêu cầu mã mới");
        }

        if (challenge.Attempts >= challenge.MaxAttempts)
        {
            challenge.Status = ChallengeStatus.LOCKED;
            await _db.SaveChangesAsync(ct);
            throw new ValidationException("OTP đã bị khóa do nhập sai quá nhiều lần");
        }

        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
        var inputHash = AuthCryptoHelpers.ComputeHmacHex(request.Otp, otpSecret);

        if (!AuthCryptoHelpers.FixedTimeEquals(inputHash, challenge.SecretHash))
        {
            challenge.Attempts++;
            await _db.SaveChangesAsync(ct);
            throw new ValidationException("OTP không đúng");
        }

        challenge.Status = ChallengeStatus.VERIFIED;
        challenge.VerifiedAt = DateTime.UtcNow;
        user.AccountStatus = AccountStatus.ACTIVE;
        user.EmailVerifiedAt = DateTime.UtcNow;

        Audit(user.Id, "OTP_VERIFY", "USER", user.Id, null, null);
        await _db.SaveChangesAsync(ct);

        return new VerifyOtpResponse(user.Id);
    }

    public async Task<ResendOtpResponse> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct = default)
    {
        var emailLower = request.Email.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == emailLower, ct);

        // Trả generic success để chống enumeration
        if (user is null)
            return new ResendOtpResponse();

        if (user.AccountStatus == AccountStatus.ACTIVE)
            throw new ValidationException("Tài khoản đã được kích hoạt, vui lòng đăng nhập");

        var latest = await _db.AuthVerificationChallenges
            .Where(c => c.UserId == user.Id
                && c.Purpose == ChallengePurpose.REGISTER_EMAIL
                && c.Status == ChallengeStatus.PENDING)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latest is not null && latest.CreatedAt > DateTime.UtcNow.Subtract(ResendThrottle))
            throw new ValidationException("Vui lòng đợi trước khi gửi lại");

        if (latest is not null)
            latest.Status = ChallengeStatus.REVOKED;

        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
        var otp = AuthCryptoHelpers.GenerateOtp();

        _db.AuthVerificationChallenges.Add(new AuthVerificationChallenge
        {
            UserId = user.Id,
            Purpose = ChallengePurpose.REGISTER_EMAIL,
            Channel = ChallengeChannel.EMAIL,
            RecipientFingerprint = AuthCryptoHelpers.ComputeHmacBytes(emailLower, otpSecret),
            SecretHash = AuthCryptoHelpers.ComputeHmacHex(otp, otpSecret),
            Status = ChallengeStatus.PENDING,
            ExpiresAt = DateTime.UtcNow.Add(OtpTtl),
        });

        Audit(user.Id, "OTP_RESEND", "USER", user.Id, null, null);
        await _db.SaveChangesAsync(ct);

        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        await SendOtpAndAuditAsync(emailLower, patient?.FullName ?? emailLower, otp, user.Id, ct);

        return new ResendOtpResponse();
    }

    /// <summary>Gửi OTP + audit nếu fail.</summary>
    internal async Task SendOtpAndAuditAsync(string email, string fullName, string otp, Guid userId, CancellationToken ct)
    {
        var sent = await OtpEmailBuilder.SendAsync(_emailSender, _logger, email, fullName, otp, ct);
        if (!sent)
        {
            Audit(userId, "EMAIL_SEND_FAILED", "USER", userId, null, null);
            await _db.SaveChangesAsync(ct);
        }
    }

    internal void Audit(Guid actorId, string action, string entityType,
        Guid? entityId, object? newValue, string? ipAddress)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorId == Guid.Empty ? null : actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            NewValue = newValue is null ? null : System.Text.Json.JsonSerializer.Serialize(newValue),
            IpAddress = System.Net.IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
        });
    }
}
