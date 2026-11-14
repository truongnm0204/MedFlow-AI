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
    private readonly IAuditLogger _audit;

    private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResendThrottle = TimeSpan.FromSeconds(45);

    public AuthOtpService(
        IApplicationDbContext db,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILogger<AuthOtpService> logger,
        IAuditLogger audit)
    {
        _db = db;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
        _audit = audit;
    }

    public async Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct = default)
    {
        var emailLower = request.Email.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == emailLower, ct)
            ?? throw new ValidationException("OTP không hợp lệ");

        if (user.AccountStatus == AccountStatus.ACTIVE)
            throw new ValidationException("Tài khoản đã được kích hoạt, vui lòng đăng nhập");

        var challenge = await GetLatestPendingChallengeAsync(user.Id, ChallengePurpose.REGISTER_EMAIL, ct)
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

        _audit.Add(user.Id, "OTP_VERIFY", "USER", user.Id.ToString(), null, null);
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

        var latest = await GetLatestPendingChallengeAsync(user.Id, ChallengePurpose.REGISTER_EMAIL, ct);

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

        _audit.Add(user.Id, "OTP_RESEND", "USER", user.Id.ToString(), null, null);
        await _db.SaveChangesAsync(ct);

        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        await SendOtpAndAuditAsync(emailLower, patient?.FullName ?? emailLower, otp, user.Id, ct);

        return new ResendOtpResponse();
    }

    /// <summary>Gửi OTP step-up cho thao tác liên kết hồ sơ (user đã đăng nhập).</summary>
    public async Task<DateTime> SendIdentityClaimOtpAsync(long userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        if (user.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException("Tài khoản người dùng hiện không ở trạng thái hoạt động.");

        var latest = await GetLatestPendingChallengeAsync(user.Id, ChallengePurpose.IDENTITY_CLAIM, ct);
        if (latest is not null && latest.CreatedAt > DateTime.UtcNow.Subtract(ResendThrottle))
            throw new ValidationException("Vui lòng đợi trước khi gửi lại OTP.");

        if (latest is not null)
            latest.Status = ChallengeStatus.REVOKED;

        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
        var otp = AuthCryptoHelpers.GenerateOtp();
        var expiresAt = DateTime.UtcNow.Add(OtpTtl);

        _db.AuthVerificationChallenges.Add(new AuthVerificationChallenge
        {
            UserId = user.Id,
            Purpose = ChallengePurpose.IDENTITY_CLAIM,
            Channel = ChallengeChannel.EMAIL,
            RecipientFingerprint = AuthCryptoHelpers.ComputeHmacBytes(user.Email.ToLowerInvariant(), otpSecret),
            SecretHash = AuthCryptoHelpers.ComputeHmacHex(otp, otpSecret),
            Status = ChallengeStatus.PENDING,
            ExpiresAt = expiresAt,
        });

        _audit.Add(user.Id, "IDENTITY_CLAIM_OTP_SENT", "USER", user.Id.ToString(), null, null);
        await _db.SaveChangesAsync(ct);

        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        await SendOtpAndAuditAsync(user.Email, patient?.FullName ?? user.Email, otp, user.Id, ct);

        return expiresAt;
    }

    /// <summary>Xác minh OTP step-up; thành công thì challenge chuyển VERIFIED (không dùng lại được).</summary>
    public async Task VerifyIdentityClaimOtpAsync(long userId, string otp, CancellationToken ct = default)
    {
        var challenge = await GetLatestPendingChallengeAsync(userId, ChallengePurpose.IDENTITY_CLAIM, ct)
            ?? throw new ValidationException("OTP đã hết hạn hoặc không tồn tại.");

        if (challenge.ExpiresAt <= DateTime.UtcNow)
        {
            challenge.Status = ChallengeStatus.EXPIRED;
            await _db.SaveChangesAsync(ct);
            throw new ValidationException("OTP đã hết hạn, vui lòng yêu cầu mã mới.");
        }

        if (challenge.Attempts >= challenge.MaxAttempts)
        {
            challenge.Status = ChallengeStatus.LOCKED;
            await _db.SaveChangesAsync(ct);
            throw new ValidationException("OTP đã bị khóa do nhập sai quá nhiều lần.");
        }

        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
        var inputHash = AuthCryptoHelpers.ComputeHmacHex(otp, otpSecret);
        if (!AuthCryptoHelpers.FixedTimeEquals(inputHash, challenge.SecretHash))
        {
            challenge.Attempts++;
            await _db.SaveChangesAsync(ct);
            throw new ValidationException("OTP không đúng.");
        }

        challenge.Status = ChallengeStatus.VERIFIED;
        challenge.VerifiedAt = DateTime.UtcNow;
        _audit.Add(userId, "IDENTITY_CLAIM_OTP_VERIFIED", "USER", userId.ToString(), null, null);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Gửi OTP + audit nếu fail.</summary>
    internal async Task SendOtpAndAuditAsync(string email, string fullName, string otp, long userId, CancellationToken ct)
    {
        var sent = await OtpEmailBuilder.SendAsync(_emailSender, _logger, email, fullName, otp, ct);
        if (!sent)
        {
            _audit.Add(userId, "EMAIL_SEND_FAILED", "USER", userId.ToString(), null, null);
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Latest pending challenge for a user+purpose — user is already resolved by the caller.</summary>
    private async Task<AuthVerificationChallenge?> GetLatestPendingChallengeAsync(
        long userId, ChallengePurpose purpose, CancellationToken ct)
        => await _db.AuthVerificationChallenges
            .Where(c => c.UserId == userId && c.Purpose == purpose && c.Status == ChallengeStatus.PENDING)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
}
