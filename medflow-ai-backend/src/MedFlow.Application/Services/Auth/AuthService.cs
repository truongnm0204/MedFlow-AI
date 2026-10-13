using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MedFlow.Application.Services.Auth;

public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IJwtTokenService _jwtService;
    private readonly IRefreshTokenService _refreshService;
    private readonly IConfiguration _configuration;
    private readonly AuthOtpService _otpService;
    private readonly IAuditLogger _audit;
    private readonly PasswordHasher<User> _passwordHasher = new();

    private const int MaxLoginAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(5);

    public AuthService(
        IApplicationDbContext db,
        IJwtTokenService jwtService,
        IRefreshTokenService refreshService,
        IConfiguration configuration,
        AuthOtpService otpService,
        IAuditLogger audit)
    {
        _db = db;
        _jwtService = jwtService;
        _refreshService = refreshService;
        _configuration = configuration;
        _otpService = otpService;
        _audit = audit;
    }

    // ── Register ──────────────────────────────────────────────────────────
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        await EnsureEmailNotTakenAsync(email, ct);
        var patientRole = await GetDefaultRoleAsync(ct);
        var (otp, challenge) = CreateOtpChallenge(email, ct);

        var user = new User
        {
            Username = email,
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
        => await _db.Roles.FirstOrDefaultAsync(r => r.Code == "PATIENT", ct)
            ?? throw new InvalidOperationException("Role PATIENT not seeded.");

    private (string otp, AuthVerificationChallenge challenge) CreateOtpChallenge(string email, CancellationToken ct)
    {
        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
        var otp = AuthCryptoHelpers.GenerateOtp();
        var challenge = new AuthVerificationChallenge
        {
            Purpose = ChallengePurpose.REGISTER_EMAIL,
            Channel = ChallengeChannel.EMAIL,
            RecipientFingerprint = AuthCryptoHelpers.ComputeHmacBytes(email, otpSecret),
            SecretHash = AuthCryptoHelpers.ComputeHmacHex(otp, otpSecret),
            Status = ChallengeStatus.PENDING,
            ExpiresAt = DateTime.UtcNow.Add(OtpTtl),
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

            _audit.Add(user.Id, "REGISTER", "USER", user.Id,
                new { user.Email, user.Username }, ipAddress);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private Task SendOtpEmailAfterCommitAsync(string email, string fullName, string otp, Guid userId, CancellationToken ct)
        => _otpService.SendOtpAndAuditAsync(email, fullName, otp, userId, ct);

    // ── Login ─────────────────────────────────────────────────────────────
    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var identifier = request.Identifier.ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Patient)
            .FirstOrDefaultAsync(u =>
                u.Username == identifier || u.Email == identifier, ct);

        // Generic message chống enumeration
        var invalidMsg = "Sai thông tin đăng nhập";

        if (user is null || user.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException(invalidMsg);

        ApplyLockoutRules(user);

        var result = _passwordHasher.VerifyHashedPassword(null!, user.PasswordHash!, request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            await RecordFailedLoginAsync(user, ipAddress, ct);
            throw new UnauthorizedAccessException(invalidMsg);
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;

        var tokens = await IssueTokensAsync(user, ipAddress, ct);
        _audit.Add(user.Id, "LOGIN_SUCCESS", "USER", user.Id, null, ipAddress);
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
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
            throw new ValidationException("Tài khoản đang bị khóa, vui lòng thử lại sau");
    }

    private async Task RecordFailedLoginAsync(User user, string? ipAddress, CancellationToken ct)
    {
        user.FailedLoginAttempts++;
        if (user.FailedLoginAttempts >= MaxLoginAttempts)
        {
            user.LockedUntil = DateTime.UtcNow.Add(LockDuration);
            user.FailedLoginAttempts = 0;
        }
        _audit.Add(user.Id, "LOGIN_FAILED", "USER", user.Id, null, ipAddress);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<(string AccessToken, int ExpiresInSeconds, string RawRefreshToken, string[] Roles)>
        IssueTokensAsync(User user, string? ipAddress, CancellationToken ct)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Code).ToArray();
        var accessToken = _jwtService.GenerateAccessToken(user, roles);
        var refresh = await _refreshService.GenerateAsync(user.Id, ipAddress, ct);
        return (accessToken.AccessToken,
            (int)(accessToken.ExpiresAt - DateTime.UtcNow).TotalSeconds,
            refresh.RawToken, roles);
    }

    private static UserDto BuildUserDto(User user, string[] roles)
        => new(user.Id, user.Username, user.Email,
            user.Patient?.FullName, roles, user.Patient?.PatientCode);

    // ── Refresh ───────────────────────────────────────────────────────────
    public async Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var result = await _refreshService.RotateAsync(request.RefreshToken, ipAddress, ct);

        if (result.ReuseDetected)
        {
            _audit.Add(result.User?.Id ?? Guid.Empty, "REFRESH_REUSE_DETECTED", "USER",
                result.User?.Id, null, ipAddress);
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Refresh token không hợp lệ");
        }

        if (!result.Success || result.Entity is null || result.User is null)
            throw new UnauthorizedAccessException("Refresh token không hợp lệ");

        var roles = await _db.UserRoles
            .Where(ur => ur.UserId == result.User.Id)
            .Select(ur => ur.Role.Code)
            .ToArrayAsync(ct);

        var accessToken = _jwtService.GenerateAccessToken(result.User, roles);

        return new RefreshResponse(
            accessToken.AccessToken,
            (int)(accessToken.ExpiresAt - DateTime.UtcNow).TotalSeconds,
            result.RawToken!,
            GetRefreshExpirySeconds());
    }

    // ── Logout ────────────────────────────────────────────────────────────
    public async Task LogoutAsync(Guid userId, string refreshToken, string? ipAddress, CancellationToken ct = default)
    {
        await _refreshService.RevokeAsync(refreshToken, ipAddress, ct);
        _audit.Add(userId, "LOGOUT", "USER", userId, null, ipAddress);
        await _db.SaveChangesAsync(ct);
    }

    // ── OTP (delegate) ────────────────────────────────────────────────────
    public Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct = default)
        => _otpService.VerifyOtpAsync(request, ct);

    public Task<ResendOtpResponse> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct = default)
        => _otpService.ResendOtpAsync(request, ct);

    // ── GetMe ─────────────────────────────────────────────────────────────
    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Patient)
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
        var code = $"PAT-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var exists = await _db.Patients.AnyAsync(p => p.PatientCode == code, ct);
        return exists ? await GeneratePatientCodeAsync(ct) : code;
    }
}
