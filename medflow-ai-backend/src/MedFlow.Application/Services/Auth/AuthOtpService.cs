using System.Globalization;
using System.Security.Cryptography;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Auth;

/// <summary>OTP challenge: verify + resend + password recovery — tách từ AuthService để giữ file nhỏ.</summary>
public class AuthOtpService
{
    private const int DefaultMaxAttempts = 5;
    private const int MinPasswordResetResponseFloorMs = 180;
    private const int MaxPasswordResetResponseJitterMs = 60;

    private readonly IApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthOtpService> _logger;
    private readonly IAuditLogger _audit;
    private readonly AuthSecurityOptions _options;
    private readonly TimeProvider _timeProvider;

    public AuthOtpService(
        IApplicationDbContext db,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILogger<AuthOtpService> logger,
        IAuditLogger audit,
        IOptions<AuthSecurityOptions>? authOptions = null,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
        _audit = audit;
        _options = authOptions?.Value ?? new AuthSecurityOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        if (challenge.ExpiresAt <= nowUtc)
        {
            challenge.Status = ChallengeStatus.EXPIRED;
            await _db.SaveChangesAsync(ct);
            throw new ValidationException("OTP đã hết hạn, vui lòng yêu cầu mã mới");
        }

        var maxAttempts = challenge.MaxAttempts != DefaultMaxAttempts
            ? challenge.MaxAttempts
            : _options.OtpMaxAttempts;
        if (challenge.Attempts >= maxAttempts)
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
        challenge.VerifiedAt = nowUtc;
        user.AccountStatus = AccountStatus.ACTIVE;
        user.EmailVerifiedAt = nowUtc;

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
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var resendThrottle = TimeSpan.FromSeconds(_options.OtpResendThrottleSeconds);

        if (latest is not null && latest.CreatedAt > nowUtc.Subtract(resendThrottle))
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
            MaxAttempts = _options.OtpMaxAttempts,
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc.AddMinutes(_options.OtpTtlMinutes),
        });

        _audit.Add(user.Id, "OTP_RESEND", "USER", user.Id.ToString(), null, null);
        await _db.SaveChangesAsync(ct);

        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        await SendOtpAndAuditAsync(emailLower, patient?.FullName ?? emailLower, otp, user.Id, ct, _options.OtpTtlMinutes);

        return new ResendOtpResponse();
    }

    /// <summary>Gửi OTP khôi phục mật khẩu với chống dò tài khoản (HTTP 200 đồng nhất + dummy HMAC + khóa hàng chống đua).</summary>
    public async Task<ForgotPasswordResponse> SendPasswordResetOtpAsync(
        string rawEmail,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var startedTimestamp = _timeProvider.GetTimestamp();
        var emailLower = (rawEmail ?? string.Empty).Trim().ToLowerInvariant();
        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);

        // Always perform candidate OTP + HMAC generation up front so non-existent/ineligible emails execute equivalent CPU work.
        var candidateOtp = AuthCryptoHelpers.GenerateOtp();
        var recipientFingerprint = AuthCryptoHelpers.ComputeHmacBytes(emailLower, otpSecret);
        var candidateSecretHash = AuthCryptoHelpers.ComputeHmacHex(candidateOtp, otpSecret);

        if (string.IsNullOrEmpty(emailLower))
        {
            await EnforcePasswordResetResponseFloorAsync(startedTimestamp, ct);
            return new ForgotPasswordResponse();
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == emailLower, ct);

        var isEligible = user is not null &&
                         user.AccountStatus == AccountStatus.ACTIVE &&
                         string.Equals(user.AuthProvider, "LOCAL", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(user.PasswordHash);

        if (!isEligible || user is null)
        {
            await EnforcePasswordResetResponseFloorAsync(startedTimestamp, ct);
            return new ForgotPasswordResponse();
        }

        var shouldSendEmail = false;
        string displayName = emailLower;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            await _db.TryLockUserForUpdateAsync(user.Id, ct);

            var lockedUser = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == user.Id, ct);

            if (lockedUser is null ||
                lockedUser.AccountStatus != AccountStatus.ACTIVE ||
                !string.Equals(lockedUser.AuthProvider, "LOCAL", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(lockedUser.PasswordHash))
            {
                await tx.RollbackAsync(ct);
                await EnforcePasswordResetResponseFloorAsync(startedTimestamp, ct);
                return new ForgotPasswordResponse();
            }

            var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
            var resendThrottle = TimeSpan.FromSeconds(_options.OtpResendThrottleSeconds);
            var lockoutWindowStart = nowUtc.AddMinutes(-_options.LockoutDurationMinutes);

            var recentChallenges = await _db.AuthVerificationChallenges
                .Where(c =>
                    c.UserId == user.Id &&
                    c.Purpose == ChallengePurpose.PASSWORD_RESET &&
                    c.CreatedAt >= lockoutWindowStart)
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .ToListAsync(ct);

            var latestPending = recentChallenges.FirstOrDefault(c => c.Status == ChallengeStatus.PENDING);
            if (latestPending is not null && latestPending.CreatedAt > nowUtc.Subtract(resendThrottle))
            {
                // Silent 45s throttle: return identical 200 OK without issuing a second OTP.
                await tx.RollbackAsync(ct);
                await EnforcePasswordResetResponseFloorAsync(startedTimestamp, ct);
                return new ForgotPasswordResponse();
            }

            // Carry over failed attempts since the most recent VERIFIED challenge in the lockout window
            // so resending OTP cannot reset brute-force guess counters (use Max since each resend already
            // seeds its initial Attempts from the prior challenge's cumulative count).
            var unverifiedStreak = recentChallenges
                .TakeWhile(c => c.Status != ChallengeStatus.VERIFIED)
                .ToList();
            var accumulatedFailedAttempts = unverifiedStreak.Count == 0
                ? 0
                : unverifiedStreak.Max(c => (int)c.Attempts);
            var hasActiveLockedChallenge = unverifiedStreak.Any(c => c.Status == ChallengeStatus.LOCKED);

            if (hasActiveLockedChallenge || accumulatedFailedAttempts >= _options.OtpMaxAttempts)
            {
                await tx.RollbackAsync(ct);
                await EnforcePasswordResetResponseFloorAsync(startedTimestamp, ct);
                return new ForgotPasswordResponse();
            }

            foreach (var pending in unverifiedStreak.Where(c => c.Status == ChallengeStatus.PENDING))
            {
                pending.Status = ChallengeStatus.REVOKED;
            }

            _db.AuthVerificationChallenges.Add(new AuthVerificationChallenge
            {
                UserId = user.Id,
                Purpose = ChallengePurpose.PASSWORD_RESET,
                Channel = ChallengeChannel.EMAIL,
                RecipientFingerprint = recipientFingerprint,
                SecretHash = candidateSecretHash,
                Status = ChallengeStatus.PENDING,
                Attempts = (short)accumulatedFailedAttempts,
                MaxAttempts = _options.OtpMaxAttempts,
                CreatedAt = nowUtc,
                ExpiresAt = nowUtc.AddMinutes(_options.OtpTtlMinutes),
            });

            _audit.Add(
                user.Id,
                "PASSWORD_RESET_OTP_SENT",
                "USER",
                user.Id.ToString(CultureInfo.InvariantCulture),
                null,
                ipAddress);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            shouldSendEmail = true;

            var patientName = await _db.Patients
                .AsNoTracking()
                .Where(p => p.UserId == user.Id)
                .Select(p => p.FullName)
                .FirstOrDefaultAsync(ct);
            var staffName = string.IsNullOrWhiteSpace(patientName)
                ? await _db.Staff
                    .AsNoTracking()
                    .Where(s => s.UserId == user.Id)
                    .Select(s => s.FullName)
                    .FirstOrDefaultAsync(ct)
                : null;
            displayName = !string.IsNullOrWhiteSpace(patientName)
                ? patientName
                : (!string.IsNullOrWhiteSpace(staffName) ? staffName : emailLower);
        }

        if (shouldSendEmail)
        {
            var sent = await OtpEmailBuilder.SendPasswordResetAsync(
                _emailSender,
                _logger,
                emailLower,
                displayName,
                candidateOtp,
                ct,
                _options.OtpTtlMinutes);

            if (!sent)
            {
                _audit.Add(
                    user.Id,
                    "EMAIL_SEND_FAILED",
                    "USER",
                    user.Id.ToString(CultureInfo.InvariantCulture),
                    new { Purpose = nameof(ChallengePurpose.PASSWORD_RESET) },
                    ipAddress);
                await _db.SaveChangesAsync(ct);
            }
        }

        await EnforcePasswordResetResponseFloorAsync(startedTimestamp, ct);
        return new ForgotPasswordResponse();
    }

    /// <summary>
    /// Xác minh và đánh dấu tiêu thụ 1 lần OTP <c>PASSWORD_RESET</c> bên trong transaction của caller.
    /// Nếu OTP hết hạn / khóa / sai: cập nhật và commit ngay bộ đếm thử sai trước khi ném <see cref="ValidationException"/>.
    /// </summary>
    internal async Task ConsumePasswordResetOtpInTransactionAsync(
        long userId,
        string otpCode,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction activeTransaction,
        CancellationToken ct)
    {
        var challenge = await GetLatestPendingChallengeAsync(userId, ChallengePurpose.PASSWORD_RESET, ct)
            ?? throw new ValidationException("Mã OTP không hợp lệ hoặc đã hết hạn.");

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        if (challenge.ExpiresAt <= nowUtc)
        {
            challenge.Status = ChallengeStatus.EXPIRED;
            await _db.SaveChangesAsync(ct);
            await activeTransaction.CommitAsync(ct);
            throw new ValidationException("Mã OTP đã hết hạn, vui lòng yêu cầu mã mới.");
        }

        var maxAttempts = challenge.MaxAttempts != DefaultMaxAttempts && challenge.MaxAttempts > 0
            ? challenge.MaxAttempts
            : _options.OtpMaxAttempts;
        if (challenge.Attempts >= maxAttempts)
        {
            challenge.Status = ChallengeStatus.LOCKED;
            await _db.SaveChangesAsync(ct);
            await activeTransaction.CommitAsync(ct);
            throw new ValidationException("Mã OTP đã bị khóa do nhập sai quá nhiều lần.");
        }

        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
        var inputHash = AuthCryptoHelpers.ComputeHmacHex(otpCode, otpSecret);

        if (!AuthCryptoHelpers.FixedTimeEquals(inputHash, challenge.SecretHash))
        {
            challenge.Attempts++;
            var lockedNow = challenge.Attempts >= maxAttempts;
            if (lockedNow)
            {
                challenge.Status = ChallengeStatus.LOCKED;
            }

            await _db.SaveChangesAsync(ct);
            await activeTransaction.CommitAsync(ct);
            throw new ValidationException(
                lockedNow
                    ? "Mã OTP đã bị khóa do nhập sai quá nhiều lần."
                    : "Mã OTP không đúng.");
        }

        challenge.Status = ChallengeStatus.VERIFIED;
        challenge.VerifiedAt = nowUtc;
    }

    /// <summary>Gửi OTP step-up cho thao tác liên kết hồ sơ (user đã đăng nhập).</summary>
    public async Task<DateTime> SendIdentityClaimOtpAsync(long userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        if (user.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException("Tài khoản người dùng hiện không ở trạng thái hoạt động.");

        var latest = await GetLatestPendingChallengeAsync(user.Id, ChallengePurpose.IDENTITY_CLAIM, ct);
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var resendThrottle = TimeSpan.FromSeconds(_options.OtpResendThrottleSeconds);
        if (latest is not null && latest.CreatedAt > nowUtc.Subtract(resendThrottle))
            throw new ValidationException("Vui lòng đợi trước khi gửi lại OTP.");

        if (latest is not null)
            latest.Status = ChallengeStatus.REVOKED;

        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
        var otp = AuthCryptoHelpers.GenerateOtp();
        var expiresAt = nowUtc.AddMinutes(_options.OtpTtlMinutes);

        _db.AuthVerificationChallenges.Add(new AuthVerificationChallenge
        {
            UserId = user.Id,
            Purpose = ChallengePurpose.IDENTITY_CLAIM,
            Channel = ChallengeChannel.EMAIL,
            RecipientFingerprint = AuthCryptoHelpers.ComputeHmacBytes(user.Email.ToLowerInvariant(), otpSecret),
            SecretHash = AuthCryptoHelpers.ComputeHmacHex(otp, otpSecret),
            Status = ChallengeStatus.PENDING,
            MaxAttempts = _options.OtpMaxAttempts,
            CreatedAt = nowUtc,
            ExpiresAt = expiresAt,
        });

        _audit.Add(user.Id, "IDENTITY_CLAIM_OTP_SENT", "USER", user.Id.ToString(), null, null);
        await _db.SaveChangesAsync(ct);

        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        await SendOtpAndAuditAsync(user.Email, patient?.FullName ?? user.Email, otp, user.Id, ct, _options.OtpTtlMinutes);

        return expiresAt;
    }

    /// <summary>Xác minh OTP step-up; thành công thì challenge chuyển VERIFIED (không dùng lại được).</summary>
    public async Task VerifyIdentityClaimOtpAsync(long userId, string otp, CancellationToken ct = default)
    {
        var challenge = await GetLatestPendingChallengeAsync(userId, ChallengePurpose.IDENTITY_CLAIM, ct)
            ?? throw new ValidationException("OTP đã hết hạn hoặc không tồn tại.");

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        if (challenge.ExpiresAt <= nowUtc)
        {
            challenge.Status = ChallengeStatus.EXPIRED;
            await _db.SaveChangesAsync(ct);
            throw new ValidationException("OTP đã hết hạn, vui lòng yêu cầu mã mới.");
        }

        var maxAttempts = challenge.MaxAttempts != DefaultMaxAttempts
            ? challenge.MaxAttempts
            : _options.OtpMaxAttempts;
        if (challenge.Attempts >= maxAttempts)
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
        challenge.VerifiedAt = nowUtc;
        _audit.Add(userId, "IDENTITY_CLAIM_OTP_VERIFIED", "USER", userId.ToString(), null, null);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Gửi OTP + audit nếu fail.</summary>
    internal async Task SendOtpAndAuditAsync(
        string email,
        string fullName,
        string otp,
        long userId,
        CancellationToken ct,
        int? otpTtlMinutes = null)
    {
        var ttlMinutes = otpTtlMinutes ?? _options.OtpTtlMinutes;
        var sent = await OtpEmailBuilder.SendAsync(_emailSender, _logger, email, fullName, otp, ct, ttlMinutes);
        if (!sent)
        {
            _audit.Add(userId, "EMAIL_SEND_FAILED", "USER", userId.ToString(), null, null);
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task EnforcePasswordResetResponseFloorAsync(long startedTimestamp, CancellationToken ct)
    {
        var smtpHost = _configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(smtpHost) ||
            string.Equals(smtpHost.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetMs = MinPasswordResetResponseFloorMs + RandomNumberGenerator.GetInt32(0, MaxPasswordResetResponseJitterMs + 1);
        var elapsedMs = _timeProvider.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        var remainingMs = (int)Math.Ceiling(targetMs - elapsedMs);
        if (remainingMs > 0)
        {
            await Task.Delay(remainingMs, ct);
        }
    }

    /// <summary>Latest pending challenge for a user+purpose — user is already resolved by the caller.</summary>
    private async Task<AuthVerificationChallenge?> GetLatestPendingChallengeAsync(
        long userId, ChallengePurpose purpose, CancellationToken ct)
        => await _db.AuthVerificationChallenges
            .Where(c => c.UserId == userId && c.Purpose == purpose && c.Status == ChallengeStatus.PENDING)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);
}
