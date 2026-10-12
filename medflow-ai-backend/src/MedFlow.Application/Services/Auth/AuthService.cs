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
    private readonly PasswordHasher<User> _passwordHasher = new();

    private const int MaxLoginAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(5);

    public AuthService(
        IApplicationDbContext db,
        IJwtTokenService jwtService,
        IRefreshTokenService refreshService,
        IConfiguration configuration,
        AuthOtpService otpService)
    {
        _db = db;
        _jwtService = jwtService;
        _refreshService = refreshService;
        _configuration = configuration;
        _otpService = otpService;
    }

    // ── Register ──────────────────────────────────────────────────────────
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var emailLower = request.Email.ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == emailLower, ct))
            throw new ValidationException("Email đã được sử dụng");

        var patientRole = await _db.Roles.FirstOrDefaultAsync(r => r.Code == "PATIENT", ct)
            ?? throw new InvalidOperationException("Role PATIENT not seeded.");

        var otpSecret = AuthCryptoHelpers.GetOtpSecret(_configuration);
        var otp = AuthCryptoHelpers.GenerateOtp();
        var otpHash = AuthCryptoHelpers.ComputeHmacHex(otp, otpSecret);
        var fingerprint = AuthCryptoHelpers.ComputeHmacBytes(emailLower, otpSecret);

        var user = new User
        {
            Username = emailLower,
            Email = emailLower,
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
            Email = emailLower,
            Status = "ACTIVE",
        };

        await using var transaction = await _db.BeginTransactionAsync(ct);
        try
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            patient.UserId = user.Id;
            _db.Patients.Add(patient);

            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = patientRole.Id });

            _db.AuthVerificationChallenges.Add(new AuthVerificationChallenge
            {
                UserId = user.Id,
                Purpose = ChallengePurpose.REGISTER_EMAIL,
                Channel = ChallengeChannel.EMAIL,
                RecipientFingerprint = fingerprint,
                SecretHash = otpHash,
                Status = ChallengeStatus.PENDING,
                ExpiresAt = DateTime.UtcNow.Add(OtpTtl),
            });

            Audit(user.Id, "REGISTER", "USER", user.Id,
                new { user.Email, user.Username }, ipAddress);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        await _otpService.SendOtpAndAuditAsync(emailLower, request.FullName, otp, user.Id, ct);

        return new RegisterResponse(user.Id, emailLower);
    }

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

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
            throw new ValidationException("Tài khoản đang bị khóa, vui lòng thử lại sau");

        var result = _passwordHasher.VerifyHashedPassword(null!, user.PasswordHash!, request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxLoginAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.Add(LockDuration);
                user.FailedLoginAttempts = 0;
            }
            Audit(user.Id, "LOGIN_FAILED", "USER", user.Id, null, ipAddress);
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException(invalidMsg);
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;

        var roles = user.UserRoles.Select(ur => ur.Role.Code).ToArray();
        var accessToken = _jwtService.GenerateAccessToken(user, roles);
        var refresh = await _refreshService.GenerateAsync(user.Id, ipAddress, ct);

        Audit(user.Id, "LOGIN_SUCCESS", "USER", user.Id, null, ipAddress);
        await _db.SaveChangesAsync(ct);

        return new LoginResponse(
            accessToken.AccessToken,
            (int)(accessToken.ExpiresAt - DateTime.UtcNow).TotalSeconds,
            refresh.RawToken,
            GetRefreshExpirySeconds(),
            new UserDto(user.Id, user.Username, user.Email,
                user.Patient?.FullName, roles, user.Patient?.PatientCode));
    }

    // ── Refresh ───────────────────────────────────────────────────────────
    public async Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var result = await _refreshService.RotateAsync(request.RefreshToken, ipAddress, ct);

        if (result.ReuseDetected)
        {
            Audit(result.User?.Id ?? Guid.Empty, "REFRESH_REUSE_DETECTED", "USER",
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
        Audit(userId, "LOGOUT", "USER", userId, null, ipAddress);
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
        return new UserDto(user.Id, user.Username, user.Email,
            user.Patient?.FullName, roles, user.Patient?.PatientCode);
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

    private void Audit(Guid actorId, string action, string entityType,
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
