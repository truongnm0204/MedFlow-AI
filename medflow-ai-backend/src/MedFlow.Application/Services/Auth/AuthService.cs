using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Auth;

public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IJwtTokenService _jwtService;
    private readonly IAuthSessionService _sessionService;
    private readonly IConfiguration _configuration;
    private readonly AuthOtpService _otpService;
    private readonly IAuditLogger _audit;
    private readonly AuthSecurityOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeAccessRevoker _accessRevoker;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(
        IApplicationDbContext db,
        IJwtTokenService jwtService,
        IAuthSessionService sessionService,
        IConfiguration configuration,
        AuthOtpService otpService,
        IAuditLogger audit,
        IOptions<AuthSecurityOptions>? authOptions = null,
        TimeProvider? timeProvider = null,
        IRealtimeAccessRevoker? accessRevoker = null)
    {
        _db = db;
        _jwtService = jwtService;
        _sessionService = sessionService;
        _configuration = configuration;
        _otpService = otpService;
        _audit = audit;
        _options = authOptions?.Value ?? new AuthSecurityOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _accessRevoker = accessRevoker ?? MedFlow.Application.Services.Realtime.NullRealtimeAccessRevoker.Instance;
    }

    // ── Register ──────────────────────────────────────────────────────────
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        await EnsureEmailNotTakenAsync(email, ct);
        var patientRole = await GetDefaultRoleAsync(ct);
        var (otp, challenge) = CreateOtpChallenge(email);

        var user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.HashPassword(null!, request.Password),
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.PENDING,
        };

        var patient = new Patient
        {
            PatientCode = await GeneratePatientCodeAsync(ct),
            FullName = request.FullName,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            Phone = request.Phone,
            Email = email,
            Status = "ACTIVE",
        };

        await PersistRegisterDataAsync(user, patient, patientRole, challenge, ipAddress, ct);
        await SendOtpEmailAfterCommitAsync(email, request.FullName, otp, user.Id, ct);

        return new RegisterResponse(user.Id, email);
    }

    private static string NormalizeEmail(string email) => email.ToLowerInvariant();

    private async Task EnsureEmailNotTakenAsync(string email, CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email, ct))
            throw new ValidationException("Email đã được sử dụng");
    }

    private async Task<Role> GetDefaultRoleAsync(CancellationToken ct)
        => await _db.Roles.FirstOrDefaultAsync(r => r.Code == RoleNames.Patient, ct)
            ?? throw new InvalidOperationException($"Role {RoleNames.Patient} not seeded.");

    private (string otp, AuthVerificationChallenge challenge) CreateOtpChallenge(string email)
    {
        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
        var otp = AuthCryptoHelpers.GenerateOtp();
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var challenge = new AuthVerificationChallenge
        {
            Purpose = ChallengePurpose.REGISTER_EMAIL,
            Channel = ChallengeChannel.EMAIL,
            RecipientFingerprint = AuthCryptoHelpers.ComputeHmacBytes(email, otpSecret),
            SecretHash = AuthCryptoHelpers.ComputeHmacHex(otp, otpSecret),
            Status = ChallengeStatus.PENDING,
            MaxAttempts = _options.OtpMaxAttempts,
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc.AddMinutes(_options.OtpTtlMinutes),
        };
        return (otp, challenge);
    }

    private async Task PersistRegisterDataAsync(
        User user, Patient patient, Role patientRole,
        AuthVerificationChallenge challenge, string? ipAddress, CancellationToken ct)
    {
        await using var transaction = await _db.BeginTransactionAsync(ct);
        try
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            patient.UserId = user.Id;
            _db.Patients.Add(patient);
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = patientRole.Id });

            challenge.UserId = user.Id;
            _db.AuthVerificationChallenges.Add(challenge);

            _audit.Add(user.Id, "REGISTER", "USER", user.Id.ToString(),
                new { user.Email }, ipAddress);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private Task SendOtpEmailAfterCommitAsync(string email, string fullName, string otp, long userId, CancellationToken ct)
        => _otpService.SendOtpAndAuditAsync(email, fullName, otp, userId, ct, _options.OtpTtlMinutes);

    // ── Login ─────────────────────────────────────────────────────────────
    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var email = request.Email.ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Patient)
            .Include(u => u.Staff)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        // Generic message chống enumeration
        var invalidMsg = "Sai thông tin đăng nhập";

        if (user is null || user.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException(invalidMsg);

        if (!string.Equals(user.AuthProvider, "LOCAL", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw new UnauthorizedAccessException(invalidMsg);
        }

        ApplyLockoutRules(user);

        var result = _passwordHasher.VerifyHashedPassword(null!, user.PasswordHash!, request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            await RecordFailedLoginAsync(user, ipAddress, ct);
            throw new UnauthorizedAccessException(invalidMsg);
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;

        var tokens = await IssueTokensAsync(user, ipAddress, ct);
        _audit.Add(user.Id, "LOGIN_SUCCESS", "USER", user.Id.ToString(), null, ipAddress);
        await _db.SaveChangesAsync(ct);

        return new LoginResponse(
            tokens.AccessToken,
            tokens.ExpiresInSeconds,
            tokens.RawRefreshToken,
            GetRefreshExpirySeconds(),
            BuildUserDto(user, tokens.Roles));
    }

    private void ApplyLockoutRules(User user)
    {
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        if (user.LockedUntil.HasValue && user.LockedUntil > nowUtc)
            throw new ValidationException("Tài khoản đang bị khóa, vui lòng thử lại sau");
    }

    private async Task RecordFailedLoginAsync(User user, string? ipAddress, CancellationToken ct)
    {
        var lockedNow = false;
        user.FailedLoginAttempts++;
        if (user.FailedLoginAttempts >= _options.MaxLoginAttempts)
        {
            user.LockedUntil = HospitalClock.GetUtcNow(_timeProvider).AddMinutes(_options.LockoutDurationMinutes);
            user.FailedLoginAttempts = 0;
            lockedNow = true;
        }
        _audit.Add(user.Id, "LOGIN_FAILED", "USER", user.Id.ToString(), null, ipAddress);
        await _db.SaveChangesAsync(ct);

        if (lockedNow)
        {
            await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryRevokeUserConnectionsSafeAsync(
                _accessRevoker,
                user.Id,
                "ACCOUNT_LOCKED",
                ct);
        }
    }

    private async Task<(string AccessToken, int ExpiresInSeconds, string RawRefreshToken, string[] Roles)>
        IssueTokensAsync(User user, string? ipAddress, CancellationToken ct)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Code).ToArray();
        var refresh = await _sessionService.GenerateAsync(user.Id, ipAddress, ct);
        var accessToken = _jwtService.GenerateAccessToken(user, roles, refresh.Entity?.Id);
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        return (accessToken.AccessToken,
            (int)(accessToken.ExpiresAt - nowUtc).TotalSeconds,
            refresh.RawToken, roles);
    }

    private static UserDto BuildUserDto(User user, string[] roles)
        => new(
            user.Id,
            user.Email,
            user.Patient?.FullName ?? user.Staff?.FullName,
            roles,
            user.Patient?.PatientCode,
            user.Staff?.StaffCode);

    // ── Refresh ───────────────────────────────────────────────────────────
    public async Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var result = await _sessionService.RotateAsync(request.RefreshToken, ipAddress, ct);

        if (result.ReuseDetected)
        {
            _audit.Add(result.User?.Id, "REFRESH_REUSE_DETECTED", "USER",
                result.User?.Id.ToString(), null, ipAddress);
            await _db.SaveChangesAsync(ct);
            if (result.User?.Id is long reuseUserId)
            {
                await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryRevokeUserConnectionsSafeAsync(
                    _accessRevoker,
                    reuseUserId,
                    "REFRESH_REUSE_DETECTED",
                    ct);
            }
            throw new UnauthorizedAccessException("Refresh token không hợp lệ");
        }

        if (!result.Success || result.Entity is null || result.User is null)
            throw new UnauthorizedAccessException("Refresh token không hợp lệ");

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        if (result.User.AccountStatus != AccountStatus.ACTIVE ||
            (result.User.LockedUntil.HasValue && result.User.LockedUntil.Value > nowUtc))
        {
            await _sessionService.RevokeAllForUserAsync(result.User.Id, ipAddress, ct);
            await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryRevokeUserConnectionsSafeAsync(
                _accessRevoker,
                result.User.Id,
                "ACCOUNT_INACTIVE",
                ct);
            throw new UnauthorizedAccessException("Refresh token không hợp lệ");
        }

        var roles = await _db.UserRoles
            .Where(ur => ur.UserId == result.User.Id)
            .Select(ur => ur.Role.Code)
            .ToArrayAsync(ct);

        var accessToken = _jwtService.GenerateAccessToken(result.User, roles, result.Entity.Id);

        return new RefreshResponse(
            accessToken.AccessToken,
            (int)(accessToken.ExpiresAt - nowUtc).TotalSeconds,
            result.RawToken!,
            GetRefreshExpirySeconds());
    }

    // ── Logout ────────────────────────────────────────────────────────────
    public async Task LogoutAsync(long userId, string refreshToken, string? ipAddress, CancellationToken ct = default)
    {
        await _sessionService.RevokeAsync(refreshToken, ipAddress, ct);
        _audit.Add(userId, "LOGOUT", "USER", userId.ToString(), null, ipAddress);
        await _db.SaveChangesAsync(ct);
        await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryRevokeUserConnectionsSafeAsync(
            _accessRevoker,
            userId,
            "LOGOUT",
            ct);
    }

    // ── OTP (delegate) ────────────────────────────────────────────────────
    public Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct = default)
        => _otpService.VerifyOtpAsync(request, ct);

    public Task<ResendOtpResponse> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct = default)
        => _otpService.ResendOtpAsync(request, ct);

    // ── Password Recovery & Change Password ───────────────────────────────
    public Task<ForgotPasswordResponse> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _otpService.SendPasswordResetOtpAsync(request.Email, ipAddress, ct);
    }

    public async Task<ResetPasswordResponse> ResetPasswordAsync(
        ResetPasswordRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var otpCode = (request.OtpCode ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(email) || otpCode.Length != 6 || !otpCode.All(char.IsAsciiDigit))
            throw new ValidationException("Mã OTP phải gồm 6 chữ số.");

        EnsureStrongPassword(request.NewPassword);

        var userLookup = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (userLookup is null ||
            userLookup.AccountStatus != AccountStatus.ACTIVE ||
            !string.Equals(userLookup.AuthProvider, "LOCAL", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(userLookup.PasswordHash))
        {
            var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
            _ = AuthCryptoHelpers.ComputeHmacHex(otpCode, otpSecret);
            throw new ValidationException("Mã OTP không hợp lệ hoặc đã hết hạn.");
        }

        await using var tx = await _db.BeginTransactionAsync(ct);
        await _db.TryLockUserForUpdateAsync(userLookup.Id, ct);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userLookup.Id, ct)
            ?? throw new ValidationException("Mã OTP không hợp lệ hoặc đã hết hạn.");

        if (user.AccountStatus != AccountStatus.ACTIVE ||
            !string.Equals(user.AuthProvider, "LOCAL", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw new ValidationException("Mã OTP không hợp lệ hoặc đã hết hạn.");
        }

        await _otpService.ConsumePasswordResetOtpInTransactionAsync(user.Id, otpCode, tx, ct);

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;

        await _sessionService.RevokeAllForUserAsync(user.Id, ipAddress, ct);
        _audit.Add(user.Id, "PASSWORD_RESET_COMPLETED", "USER", user.Id.ToString(), null, ipAddress);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryRevokeUserConnectionsSafeAsync(
            _accessRevoker,
            user.Id,
            "PASSWORD_RESET",
            ct);

        return new ResetPasswordResponse();
    }

    public async Task<ChangePasswordResponse> ChangePasswordAsync(
        long userId,
        ChangePasswordRequest request,
        long? currentSessionId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.CurrentPassword))
            throw new ValidationException("Mật khẩu hiện tại là bắt buộc.");

        EnsureStrongPassword(request.NewPassword);

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        long? keptSessionId;
        var lockedNow = false;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            await _db.TryLockUserForUpdateAsync(userId, ct);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                ?? throw new UnauthorizedAccessException("Tài khoản không hợp lệ.");

            if (user.AccountStatus != AccountStatus.ACTIVE ||
                !string.Equals(user.AuthProvider, "LOCAL", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                throw new UnauthorizedAccessException("Tài khoản hiện không hỗ trợ đổi mật khẩu nội bộ.");
            }

            ApplyLockoutRules(user);

            var verifyCurrent = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, request.CurrentPassword);
            if (verifyCurrent == PasswordVerificationResult.Failed)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= _options.MaxLoginAttempts)
                {
                    user.LockedUntil = nowUtc.AddMinutes(_options.LockoutDurationMinutes);
                    user.FailedLoginAttempts = 0;
                    lockedNow = true;
                    await _sessionService.RevokeAllForUserAsync(user.Id, ipAddress, ct);
                }

                _audit.Add(
                    user.Id,
                    "PASSWORD_CHANGE_FAILED",
                    "USER",
                    user.Id.ToString(),
                    new { Locked = lockedNow },
                    ipAddress);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                if (lockedNow)
                {
                    await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryRevokeUserConnectionsSafeAsync(
                        _accessRevoker,
                        user.Id,
                        "ACCOUNT_LOCKED",
                        ct);
                }

                throw new ValidationException("Mật khẩu hiện tại không chính xác.");
            }

            if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal) ||
                _passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, request.NewPassword) != PasswordVerificationResult.Failed)
            {
                throw new ValidationException("Mật khẩu mới không được trùng với mật khẩu hiện tại.");
            }

            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;

            var revokeOutcome = await _sessionService.RevokeOtherSessionsForUserAsync(
                user.Id,
                request.CurrentRefreshToken,
                currentSessionId,
                ipAddress,
                ct);
            keptSessionId = revokeOutcome.KeptSessionId;

            _audit.Add(
                user.Id,
                "PASSWORD_CHANGED",
                "USER",
                user.Id.ToString(),
                new
                {
                    KeptCurrentSession = keptSessionId.HasValue,
                    RevokedSessionsCount = revokeOutcome.RevokedCount,
                },
                ipAddress);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions
            .TryRevokeUserConnectionsExceptSessionSafeAsync(
                _accessRevoker,
                userId,
                keptSessionId,
                "PASSWORD_CHANGED",
                ct);

        return new ChangePasswordResponse(
            Message: keptSessionId.HasValue
                ? "Đổi mật khẩu thành công. Các phiên đăng nhập khác đã được thu hồi."
                : "Đổi mật khẩu thành công. Toàn bộ phiên đăng nhập cũ đã được thu hồi.",
            KeptCurrentSession: keptSessionId.HasValue);
    }

    private static void EnsureStrongPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new ValidationException("Mật khẩu tối thiểu 8 ký tự");

        var hasLower = false;
        var hasUpper = false;
        var hasDigit = false;
        foreach (var ch in password)
        {
            if (char.IsAsciiLetterLower(ch)) hasLower = true;
            else if (char.IsAsciiLetterUpper(ch)) hasUpper = true;
            else if (char.IsAsciiDigit(ch)) hasDigit = true;
        }

        if (!hasLower || !hasUpper || !hasDigit)
            throw new ValidationException("Mật khẩu cần có chữ hoa, chữ thường và số");
    }

    // ── GetMe ─────────────────────────────────────────────────────────────
    public async Task<UserDto> GetMeAsync(long userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Patient)
            .Include(u => u.Staff)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User", userId);

        var roles = user.UserRoles.Select(ur => ur.Role.Code).ToArray();
        return BuildUserDto(user, roles);
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private int GetRefreshExpirySeconds()
    {
        var days = int.TryParse(_configuration["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 7;
        return days * 86400;
    }

    private async Task<string> GeneratePatientCodeAsync(CancellationToken ct)
    {
        var code = $"PAT-{HospitalClock.GetNow(_timeProvider):yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var exists = await _db.Patients.AnyAsync(p => p.PatientCode == code, ct);
        return exists ? await GeneratePatientCodeAsync(ct) : code;
    }
}
