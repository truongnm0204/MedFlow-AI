using System.Text.RegularExpressions;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Auth;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace MedFlow.UnitTests.Auth;

public sealed class AuthServiceTests : IDisposable
{
    private const string TestOtpSecret = "unit-test-otp-secret-32-bytes-minimum!!";
    private const string DefaultPassword = "ValidPassword123";

    private sealed class AuthTestDbContext : DbContext, IApplicationDbContext
    {
        public AuthTestDbContext(DbContextOptions<AuthTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => Set<AuthVerificationChallenge>();

        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Hospital> Hospitals => throw NotUsed();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Hospital>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>(b =>
            {
                b.HasKey(u => u.Id);
                b.Ignore(u => u.AuthSessions);
            });

            modelBuilder.Entity<Role>(b =>
            {
                b.HasKey(r => r.Id);
                b.Ignore(r => r.RolePermissions);
            });

            modelBuilder.Entity<UserRole>(b =>
            {
                b.HasKey(ur => new { ur.UserId, ur.RoleId });
                b.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId);
                b.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId);
            });

            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User).WithOne(u => u.Patient).HasForeignKey<Patient>(p => p.UserId);
            });

            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.Department);
                b.Ignore(s => s.Doctor);
                b.HasOne(s => s.User).WithOne(u => u.Staff).HasForeignKey<Staff>(s => s.UserId);
            });

            modelBuilder.Entity<AuthVerificationChallenge>(b =>
            {
                b.HasKey(c => c.Id);
                b.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId);
            });
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong AuthServiceTests.");
    }

    private sealed class StubConfiguration : IConfiguration
    {
        private readonly string _refreshExpiryDays;

        public StubConfiguration(string refreshExpiryDays = "7")
        {
            _refreshExpiryDays = refreshExpiryDays;
        }

        public string? this[string key]
        {
            get => key switch
            {
                "Auth:OtpSecret" => TestOtpSecret,
                "Jwt:RefreshTokenExpiryDays" => _refreshExpiryDays,
                _ => null,
            };
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren()
            => throw new NotSupportedException();

        public IChangeToken GetReloadToken()
            => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key)
            => throw new NotSupportedException();
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        public List<(User User, string[] Roles)> Calls { get; } = [];

        public JwtTokenResult GenerateAccessToken(User user, IEnumerable<string> roles)
        {
            var roleArray = roles.ToArray();
            Calls.Add((user, roleArray));
            return new JwtTokenResult
            {
                AccessToken = "stub-access-token",
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            };
        }
    }

    private sealed class StubAuthSessionService : IAuthSessionService
    {
        public List<(long UserId, string? CreatedByIp)> GeneratedSessions { get; } = [];
        public List<(string RawToken, string? IpAddress)> RotatedTokens { get; } = [];
        public List<(string RawToken, string? IpAddress)> RevokedTokens { get; } = [];

        public RotateResult NextRotateResult { get; set; } = new() { Success = false };

        public Task<AuthSessionResult> GenerateAsync(long userId, string? createdByIp, CancellationToken ct = default)
        {
            GeneratedSessions.Add((userId, createdByIp));
            return Task.FromResult(new AuthSessionResult
            {
                RawToken = "stub-refresh-token",
                Entity = new AuthSession
                {
                    Id = 1,
                    UserId = userId,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                },
            });
        }

        public Task<RotateResult> RotateAsync(string rawToken, string? ipAddress, CancellationToken ct = default)
        {
            RotatedTokens.Add((rawToken, ipAddress));
            return Task.FromResult(NextRotateResult);
        }

        public Task RevokeAsync(string rawToken, string? ipAddress, CancellationToken ct = default)
        {
            RevokedTokens.Add((rawToken, ipAddress));
            return Task.CompletedTask;
        }

        public Task RevokeAllForUserAsync(long userId, string? ipAddress, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string ToEmail, string Subject, string HtmlBody, string? ExtractedOtp)> Sent { get; } = [];

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            var match = Regex.Match(htmlBody, @"<strong[^>]*>(\d{6})</strong>");
            var otp = match.Success ? match.Groups[1].Value : null;
            Sent.Add((toEmail, subject, htmlBody, otp));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<(long? Actor, string Action, string EntityType, string? EntityId, object? Data, string? Ip)> Entries { get; } = [];

        public void Add(
            long? actorUserId,
            string action,
            string entityType,
            string? entityId,
            object? changeData,
            string? ipAddress)
        {
            Entries.Add((actorUserId, action, entityType, entityId, changeData, ipAddress));
        }
    }

    private readonly AuthTestDbContext _db;
    private readonly StubJwtTokenService _jwt = new();
    private readonly StubAuthSessionService _sessions = new();
    private readonly StubConfiguration _config = new();
    private readonly RecordingEmailSender _emailSender = new();
    private readonly RecordingAuditLogger _audit = new();
    private readonly PasswordHasher<User> _passwordHasher = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AuthTestDbContext>()
            .UseInMemoryDatabase($"AuthServiceTests_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AuthTestDbContext(options);
        _db.Roles.Add(new Role { Id = 1, Code = "PATIENT", Name = "Patient" });
        _db.SaveChanges();

        var otpService = new AuthOtpService(
            _db,
            _emailSender,
            _config,
            NullLogger<AuthOtpService>.Instance,
            _audit);

        _sut = new AuthService(_db, _jwt, _sessions, _config, otpService, _audit);
    }

    public void Dispose() => _db.Dispose();

    private async Task RemoveSeededRolesAsync()
    {
        var roles = await _db.Roles.ToListAsync();
        _db.Roles.RemoveRange(roles);
        await _db.SaveChangesAsync();
    }

    private async Task<User> SeedUserAsync(
        string email = "user@medflow.test",
        string password = DefaultPassword,
        AccountStatus status = AccountStatus.ACTIVE,
        short failedLoginAttempts = 0,
        DateTime? lockedUntil = null,
        string? roleCode = "PATIENT",
        Patient? patient = null,
        Staff? staff = null)
    {
        var user = new User
        {
            Email = email.ToLowerInvariant(),
            PasswordHash = _passwordHasher.HashPassword(null!, password),
            AuthProvider = "LOCAL",
            AccountStatus = status,
            FailedLoginAttempts = failedLoginAttempts,
            LockedUntil = lockedUntil,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(roleCode))
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode);
            if (role is null)
            {
                role = new Role { Code = roleCode, Name = roleCode };
                _db.Roles.Add(role);
                await _db.SaveChangesAsync();
            }

            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        if (patient is not null)
        {
            patient.UserId = user.Id;
            _db.Patients.Add(patient);
        }

        if (staff is not null)
        {
            staff.UserId = user.Id;
            _db.Staff.Add(staff);
        }

        await _db.SaveChangesAsync();
        return user;
    }

    // ── Register ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_ValidRequest_NormalizesEmailCreatesPendingUserPatientRoleChallengeSendsOtpAndAudits()
    {
        var request = new RegisterRequest
        {
            Email = "NewUser@MedFlow.Test",
            Password = DefaultPassword,
            ConfirmPassword = DefaultPassword,
            FullName = "Nguyễn Thị B",
            DateOfBirth = new DateOnly(1995, 5, 15),
            Gender = "FEMALE",
            Phone = "0912345678",
        };

        var response = await _sut.RegisterAsync(request, "203.0.113.10");

        Assert.Equal("newuser@medflow.test", response.Email);

        var user = await _db.Users.SingleAsync(u => u.Id == response.UserId);
        Assert.Equal("newuser@medflow.test", user.Email);
        Assert.Equal(AccountStatus.PENDING, user.AccountStatus);
        Assert.Equal(
            PasswordVerificationResult.Success,
            _passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, DefaultPassword));

        var patient = await _db.Patients.SingleAsync(p => p.UserId == user.Id);
        Assert.Matches(@"^PAT-\d{8}-\d{4}$", patient.PatientCode);
        Assert.Equal("Nguyễn Thị B", patient.FullName);
        Assert.Equal("0912345678", patient.Phone);

        var userRole = await _db.UserRoles.Include(ur => ur.Role).SingleAsync(ur => ur.UserId == user.Id);
        Assert.Equal("PATIENT", userRole.Role.Code);

        var challenge = await _db.AuthVerificationChallenges.SingleAsync(c => c.UserId == user.Id);
        Assert.Equal(ChallengeStatus.PENDING, challenge.Status);
        Assert.Equal(ChallengePurpose.REGISTER_EMAIL, challenge.Purpose);

        var sentEmail = Assert.Single(_emailSender.Sent);
        Assert.Equal("newuser@medflow.test", sentEmail.ToEmail);
        Assert.NotNull(sentEmail.ExtractedOtp);

        var auditEntry = Assert.Single(_audit.Entries);
        Assert.Equal(user.Id, auditEntry.Actor);
        Assert.Equal("REGISTER", auditEntry.Action);
        Assert.Equal("203.0.113.10", auditEntry.Ip);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmailCaseInsensitive_ThrowsValidation()
    {
        await SeedUserAsync(email: "existing@medflow.test");

        var request = new RegisterRequest
        {
            Email = "EXISTING@medflow.test",
            Password = DefaultPassword,
            ConfirmPassword = DefaultPassword,
            FullName = "Duplicate User",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = "MALE",
            Phone = "0987654321",
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RegisterAsync(request, "127.0.0.1"));

        Assert.Equal("Email đã được sử dụng", ex.Message);
    }

    [Fact]
    public async Task RegisterAsync_WhenPatientRoleNotSeeded_ThrowsInvalidOperationException()
    {
        await RemoveSeededRolesAsync();

        var request = new RegisterRequest
        {
            Email = "norole@medflow.test",
            Password = DefaultPassword,
            ConfirmPassword = DefaultPassword,
            FullName = "No Role",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = "MALE",
            Phone = "0987654321",
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.RegisterAsync(request, "127.0.0.1"));

        Assert.Equal("Role PATIENT not seeded.", ex.Message);
    }

    // ── Login ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_UnknownEmail_ThrowsUnauthorizedWithGenericMessage()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LoginAsync(new LoginRequest
            {
                Email = "missing@medflow.test",
                Password = DefaultPassword,
            }, "127.0.0.1"));

        Assert.Equal("Sai thông tin đăng nhập", ex.Message);
    }

    [Theory]
    [InlineData(AccountStatus.PENDING)]
    [InlineData(AccountStatus.INACTIVE)]
    [InlineData(AccountStatus.LOCKED)]
    public async Task LoginAsync_NonActiveAccount_ThrowsUnauthorized(AccountStatus status)
    {
        await SeedUserAsync(email: "nonactive@medflow.test", status: status);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LoginAsync(new LoginRequest
            {
                Email = "nonactive@medflow.test",
                Password = DefaultPassword,
            }, "127.0.0.1"));

        Assert.Equal("Sai thông tin đăng nhập", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_CurrentlyLockedUntilFuture_ThrowsValidationBeforePasswordCheck()
    {
        await SeedUserAsync(
            email: "locked@medflow.test",
            password: DefaultPassword,
            status: AccountStatus.ACTIVE,
            lockedUntil: DateTime.UtcNow.AddMinutes(5));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.LoginAsync(new LoginRequest
            {
                Email = "locked@medflow.test",
                Password = DefaultPassword,
            }, "127.0.0.1"));

        Assert.Equal("Tài khoản đang bị khóa, vui lòng thử lại sau", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_WrongPasswordBelowThreshold_IncrementsFailedAttemptsAuditsAndThrowsUnauthorized()
    {
        var user = await SeedUserAsync(
            email: "wrongpass@medflow.test",
            password: DefaultPassword,
            failedLoginAttempts: 2);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LoginAsync(new LoginRequest
            {
                Email = "wrongpass@medflow.test",
                Password = "WrongPassword999",
            }, "10.0.0.5"));

        Assert.Equal("Sai thông tin đăng nhập", ex.Message);
        Assert.Equal(3, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);

        var auditEntry = Assert.Single(_audit.Entries);
        Assert.Equal(user.Id, auditEntry.Actor);
        Assert.Equal("LOGIN_FAILED", auditEntry.Action);
        Assert.Equal("10.0.0.5", auditEntry.Ip);
    }

    [Fact]
    public async Task LoginAsync_FifthWrongPassword_SetsLockedUntilResetsCounterAuditsAndThrowsUnauthorized()
    {
        var user = await SeedUserAsync(
            email: "fifthfail@medflow.test",
            password: DefaultPassword,
            failedLoginAttempts: 4);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LoginAsync(new LoginRequest
            {
                Email = "fifthfail@medflow.test",
                Password = "WrongPassword999",
            }, "10.0.0.6"));

        Assert.Equal("Sai thông tin đăng nhập", ex.Message);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.NotNull(user.LockedUntil);
        Assert.True(user.LockedUntil > DateTime.UtcNow.AddMinutes(14));

        var auditEntry = Assert.Single(_audit.Entries);
        Assert.Equal(user.Id, auditEntry.Actor);
        Assert.Equal("LOGIN_FAILED", auditEntry.Action);
    }

    [Fact]
    public async Task LoginAsync_ValidPatientCredentialsAfterPriorFailuresAndExpiredLock_ResetsLockIssuesTokensAndReturnsPatientDto()
    {
        var user = await SeedUserAsync(
            email: "patientlogin@medflow.test",
            password: DefaultPassword,
            failedLoginAttempts: 3,
            lockedUntil: DateTime.UtcNow.AddMinutes(-1),
            roleCode: "PATIENT",
            patient: new Patient
            {
                FullName = "BN A",
                PatientCode = "PAT-01",
                Status = "ACTIVE",
            });

        var response = await _sut.LoginAsync(new LoginRequest
        {
            Email = "PatientLogin@MedFlow.Test",
            Password = DefaultPassword,
        }, "10.0.0.7");

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal("stub-access-token", response.AccessToken);
        Assert.Equal("stub-refresh-token", response.RefreshToken);
        Assert.Equal(7 * 86400, response.RefreshTokenExpiresIn);
        Assert.Equal(user.Id, response.User.Id);
        Assert.Equal("patientlogin@medflow.test", response.User.Email);
        Assert.Equal("BN A", response.User.FullName);
        Assert.Equal("PAT-01", response.User.PatientCode);
        Assert.Null(response.User.StaffCode);
        Assert.Equal(["PATIENT"], response.User.Roles);

        var auditEntry = Assert.Single(_audit.Entries);
        Assert.Equal(user.Id, auditEntry.Actor);
        Assert.Equal("LOGIN_SUCCESS", auditEntry.Action);
        Assert.Equal("10.0.0.7", auditEntry.Ip);
    }

    [Fact]
    public async Task LoginAsync_ValidStaffCredentials_PopulatesStaffFullNameAndStaffCode()
    {
        await SeedUserAsync(
            email: "doctor@medflow.test",
            password: DefaultPassword,
            roleCode: "DOCTOR",
            staff: new Staff
            {
                FullName = "BS Trần B",
                StaffCode = "STF-009",
                Status = "ACTIVE",
            });

        var response = await _sut.LoginAsync(new LoginRequest
        {
            Email = "doctor@medflow.test",
            Password = DefaultPassword,
        }, "10.0.0.8");

        Assert.Equal("BS Trần B", response.User.FullName);
        Assert.Equal("STF-009", response.User.StaffCode);
        Assert.Null(response.User.PatientCode);
        Assert.Equal(["DOCTOR"], response.User.Roles);
    }

    // ── Refresh ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_ReuseDetected_AuditsReuseAndThrowsUnauthorized()
    {
        var user = await SeedUserAsync(email: "reuse@medflow.test");
        _sessions.NextRotateResult = new RotateResult
        {
            ReuseDetected = true,
            User = user,
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.RefreshAsync(new RefreshRequest
            {
                RefreshToken = "reused-token",
            }, "10.0.0.9"));

        Assert.Equal("Refresh token không hợp lệ", ex.Message);

        var auditEntry = Assert.Single(_audit.Entries);
        Assert.Equal(user.Id, auditEntry.Actor);
        Assert.Equal("REFRESH_REUSE_DETECTED", auditEntry.Action);
        Assert.Equal("10.0.0.9", auditEntry.Ip);
    }

    [Fact]
    public async Task RefreshAsync_UnsuccessfulRotation_ThrowsUnauthorizedWithoutAudit()
    {
        _sessions.NextRotateResult = new RotateResult
        {
            Success = false,
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.RefreshAsync(new RefreshRequest
            {
                RefreshToken = "expired-or-unknown-token",
            }, "10.0.0.10"));

        Assert.Equal("Refresh token không hợp lệ", ex.Message);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task RefreshAsync_SuccessfulRotation_LoadsUserRolesAndIssuesNewAccessToken()
    {
        var user = await SeedUserAsync(
            email: "refresh@medflow.test",
            roleCode: "PATIENT");

        _sessions.NextRotateResult = new RotateResult
        {
            Success = true,
            User = user,
            Entity = new AuthSession
            {
                Id = 99,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            },
            RawToken = "rotated-refresh-token",
        };

        var response = await _sut.RefreshAsync(new RefreshRequest
        {
            RefreshToken = "valid-refresh-token",
        }, "10.0.0.11");

        Assert.Equal("stub-access-token", response.AccessToken);
        Assert.Equal("rotated-refresh-token", response.RefreshToken);
        Assert.Equal(7 * 86400, response.RefreshTokenExpiresIn);

        var jwtCall = Assert.Single(_jwt.Calls);
        Assert.Equal(user.Id, jwtCall.User.Id);
        Assert.Equal(["PATIENT"], jwtCall.Roles);
    }

    // ── Logout ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LogoutAsync_RevokesSessionAndRecordsLogoutAudit()
    {
        await _sut.LogoutAsync(42, "raw-rt", "10.0.0.1");

        var revoked = Assert.Single(_sessions.RevokedTokens);
        Assert.Equal("raw-rt", revoked.RawToken);
        Assert.Equal("10.0.0.1", revoked.IpAddress);

        var auditEntry = Assert.Single(_audit.Entries);
        Assert.Equal(42L, auditEntry.Actor);
        Assert.Equal("LOGOUT", auditEntry.Action);
        Assert.Equal("USER", auditEntry.EntityType);
        Assert.Equal("42", auditEntry.EntityId);
        Assert.Equal("10.0.0.1", auditEntry.Ip);
    }

    // ── GetMe ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMeAsync_ExistingUser_ReturnsUserDtoWithRolesAndProfile()
    {
        var user = await SeedUserAsync(
            email: "me@medflow.test",
            roleCode: "PATIENT",
            patient: new Patient
            {
                FullName = "Phạm Văn C",
                PatientCode = "PAT-20261101-7777",
                Status = "ACTIVE",
            });

        var dto = await _sut.GetMeAsync(user.Id);

        Assert.Equal(user.Id, dto.Id);
        Assert.Equal("me@medflow.test", dto.Email);
        Assert.Equal("Phạm Văn C", dto.FullName);
        Assert.Equal("PAT-20261101-7777", dto.PatientCode);
        Assert.Null(dto.StaffCode);
        Assert.Equal(["PATIENT"], dto.Roles);
    }

    [Fact]
    public async Task GetMeAsync_NonExistentUser_ThrowsNotFoundException()
    {
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetMeAsync(999));

        Assert.Equal("User with key '999' not found.", ex.Message);
    }
}
