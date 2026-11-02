using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Auth;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace MedFlow.UnitTests.Auth;

public sealed class AuthOtpServiceTests : IDisposable
{
    private const string TestOtpSecret = "unit-test-otp-secret-32-bytes-minimum!!";

    private sealed class OtpTestDbContext : DbContext, IApplicationDbContext
    {
        public OtpTestDbContext(DbContextOptions<OtpTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => Set<AuthVerificationChallenge>();

        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Staff> Staff => throw NotUsed();
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
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Staff>();
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
                b.Ignore(u => u.UserRoles);
                b.Ignore(u => u.Staff);
                b.Ignore(u => u.AuthSessions);
            });

            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User).WithOne(u => u.Patient).HasForeignKey<Patient>(p => p.UserId);
            });

            modelBuilder.Entity<AuthVerificationChallenge>(b =>
            {
                b.HasKey(c => c.Id);
                b.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId);
            });
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong AuthOtpServiceTests.");
    }

    private sealed class StubConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "Auth:OtpSecret" ? TestOtpSecret : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren()
            => throw new NotSupportedException();

        public IChangeToken GetReloadToken()
            => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key)
            => throw new NotSupportedException();
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string ToEmail, string Subject, string HtmlBody, string? ExtractedOtp)> Sent { get; } = [];

        public bool ShouldThrow { get; set; }

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (ShouldThrow)
                throw new InvalidOperationException("Simulated SMTP failure");

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

    private readonly OtpTestDbContext _db;
    private readonly RecordingEmailSender _emailSender = new();
    private readonly RecordingAuditLogger _audit = new();
    private readonly AuthOtpService _sut;

    public AuthOtpServiceTests()
    {
        var options = new DbContextOptionsBuilder<OtpTestDbContext>()
            .UseInMemoryDatabase($"AuthOtpServiceTests_{Guid.NewGuid():N}")
            .Options;

        _db = new OtpTestDbContext(options);
        _sut = new AuthOtpService(
            _db,
            _emailSender,
            new StubConfiguration(),
            NullLogger<AuthOtpService>.Instance,
            _audit);
    }

    public void Dispose() => _db.Dispose();

    private static string ComputeOtpHmacHex(string otp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestOtpSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(otp));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<(User User, AuthVerificationChallenge Challenge)> SeedPendingUserWithChallenge(
        string email = "patient@medflow.test",
        string otp = "123456",
        int attempts = 0,
        int maxAttempts = 5,
        DateTime? expiresAt = null,
        DateTime? createdAt = null,
        AccountStatus status = AccountStatus.PENDING)
    {
        var user = new User
        {
            Email = email,
            AccountStatus = status,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var patient = new Patient
        {
            UserId = user.Id,
            FullName = "Nguyễn Văn A",
            PatientCode = "PAT-20261101-1001",
            Email = email,
            Status = "ACTIVE",
        };
        _db.Patients.Add(patient);

        var challenge = new AuthVerificationChallenge
        {
            UserId = user.Id,
            Purpose = ChallengePurpose.REGISTER_EMAIL,
            Channel = ChallengeChannel.EMAIL,
            SecretHash = ComputeOtpHmacHex(otp),
            Status = ChallengeStatus.PENDING,
            Attempts = attempts,
            MaxAttempts = maxAttempts,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(5),
            CreatedAt = createdAt ?? DateTime.UtcNow.AddMinutes(-2),
        };
        _db.AuthVerificationChallenges.Add(challenge);
        await _db.SaveChangesAsync();

        return (user, challenge);
    }

    [Fact]
    public async Task VerifyOtpAsync_ValidCode_MarksChallengeVerifiedActivatesUserAndAudits()
    {
        var (user, challenge) = await SeedPendingUserWithChallenge(
            email: "patient@medflow.test",
            otp: "123456");

        var response = await _sut.VerifyOtpAsync(new VerifyOtpRequest
        {
            Email = "Patient@MedFlow.Test",
            Otp = "123456",
        });

        Assert.Equal(user.Id, response.UserId);
        Assert.Equal(ChallengeStatus.VERIFIED, challenge.Status);
        Assert.NotNull(challenge.VerifiedAt);
        Assert.Equal(AccountStatus.ACTIVE, user.AccountStatus);
        Assert.NotNull(user.EmailVerifiedAt);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(user.Id, entry.Actor);
        Assert.Equal("OTP_VERIFY", entry.Action);
        Assert.Equal("USER", entry.EntityType);
        Assert.Equal(user.Id.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task VerifyOtpAsync_UnknownEmail_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.VerifyOtpAsync(new VerifyOtpRequest
            {
                Email = "missing@medflow.test",
                Otp = "123456",
            }));

        Assert.Equal("OTP không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task VerifyOtpAsync_AlreadyActiveUser_ThrowsValidation()
    {
        await SeedPendingUserWithChallenge(
            email: "active@medflow.test",
            status: AccountStatus.ACTIVE);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.VerifyOtpAsync(new VerifyOtpRequest
            {
                Email = "active@medflow.test",
                Otp = "123456",
            }));

        Assert.Equal("Tài khoản đã được kích hoạt, vui lòng đăng nhập", ex.Message);
    }

    [Fact]
    public async Task VerifyOtpAsync_NoPendingChallenge_ThrowsValidation()
    {
        var user = new User
        {
            Email = "nopending@medflow.test",
            AccountStatus = AccountStatus.PENDING,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.VerifyOtpAsync(new VerifyOtpRequest
            {
                Email = "nopending@medflow.test",
                Otp = "123456",
            }));

        Assert.Equal("OTP đã hết hạn hoặc không tồn tại", ex.Message);
    }

    [Fact]
    public async Task VerifyOtpAsync_ExpiredChallenge_TransitionsToExpiredAndThrowsValidation()
    {
        var (_, challenge) = await SeedPendingUserWithChallenge(
            email: "expired@medflow.test",
            otp: "123456",
            expiresAt: DateTime.UtcNow.AddSeconds(-1));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.VerifyOtpAsync(new VerifyOtpRequest
            {
                Email = "expired@medflow.test",
                Otp = "123456",
            }));

        Assert.Equal("OTP đã hết hạn, vui lòng yêu cầu mã mới", ex.Message);
        Assert.Equal(ChallengeStatus.EXPIRED, challenge.Status);
    }

    [Fact]
    public async Task VerifyOtpAsync_AttemptsAlreadyAtMax_TransitionsToLockedAndThrowsValidation()
    {
        var (_, challenge) = await SeedPendingUserWithChallenge(
            email: "locked@medflow.test",
            otp: "123456",
            attempts: 5,
            maxAttempts: 5);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.VerifyOtpAsync(new VerifyOtpRequest
            {
                Email = "locked@medflow.test",
                Otp = "123456",
            }));

        Assert.Equal("OTP đã bị khóa do nhập sai quá nhiều lần", ex.Message);
        Assert.Equal(ChallengeStatus.LOCKED, challenge.Status);
        Assert.Equal(5, challenge.Attempts);
    }

    [Fact]
    public async Task VerifyOtpAsync_WrongCode_IncrementsAttemptsAndKeepsPending()
    {
        var (user, challenge) = await SeedPendingUserWithChallenge(
            email: "wrong@medflow.test",
            otp: "123456",
            attempts: 1);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.VerifyOtpAsync(new VerifyOtpRequest
            {
                Email = "wrong@medflow.test",
                Otp = "999999",
            }));

        Assert.Equal("OTP không đúng", ex.Message);
        Assert.Equal(2, challenge.Attempts);
        Assert.Equal(ChallengeStatus.PENDING, challenge.Status);
        Assert.Equal(AccountStatus.PENDING, user.AccountStatus);
    }

    [Fact]
    public async Task ResendOtpAsync_NonExistentEmail_ReturnsResponseWithoutSendingOrAuditing()
    {
        var response = await _sut.ResendOtpAsync(new ResendOtpRequest
        {
            Email = "unknown@medflow.test",
        });

        Assert.NotNull(response);
        Assert.Empty(_emailSender.Sent);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task ResendOtpAsync_AlreadyActiveUser_ThrowsValidation()
    {
        await SeedPendingUserWithChallenge(
            email: "active@medflow.test",
            status: AccountStatus.ACTIVE);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.ResendOtpAsync(new ResendOtpRequest
            {
                Email = "active@medflow.test",
            }));

        Assert.Equal("Tài khoản đã được kích hoạt, vui lòng đăng nhập", ex.Message);
    }

    [Fact]
    public async Task ResendOtpAsync_WithinThrottleWindow_ThrowsValidationAndKeepsPreviousPending()
    {
        var (_, challenge) = await SeedPendingUserWithChallenge(
            email: "throttle@medflow.test",
            createdAt: DateTime.UtcNow.AddSeconds(-10));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.ResendOtpAsync(new ResendOtpRequest
            {
                Email = "throttle@medflow.test",
            }));

        Assert.Equal("Vui lòng đợi trước khi gửi lại", ex.Message);
        Assert.Equal(ChallengeStatus.PENDING, challenge.Status);
        Assert.Empty(_emailSender.Sent);
    }

    [Fact]
    public async Task ResendOtpAsync_AfterThrottleWindow_RevokesOldCreatesNewSendsEmailAndVerifiesWithNewOtp()
    {
        var (user, oldChallenge) = await SeedPendingUserWithChallenge(
            email: "resend@medflow.test",
            otp: "111111",
            createdAt: DateTime.UtcNow.AddSeconds(-60));

        var resendResponse = await _sut.ResendOtpAsync(new ResendOtpRequest
        {
            Email = "Resend@MedFlow.Test",
        });

        Assert.NotNull(resendResponse);
        Assert.Equal(ChallengeStatus.REVOKED, oldChallenge.Status);

        var pendingChallenges = await _db.AuthVerificationChallenges
            .Where(c => c.UserId == user.Id && c.Status == ChallengeStatus.PENDING)
            .ToListAsync();
        Assert.Single(pendingChallenges);

        Assert.Contains(_audit.Entries, e => e.Action == "OTP_RESEND" && e.Actor == user.Id);

        var sentEmail = Assert.Single(_emailSender.Sent);
        Assert.Equal("resend@medflow.test", sentEmail.ToEmail);
        Assert.NotNull(sentEmail.ExtractedOtp);
        Assert.Equal(6, sentEmail.ExtractedOtp!.Length);

        var verifyResponse = await _sut.VerifyOtpAsync(new VerifyOtpRequest
        {
            Email = "resend@medflow.test",
            Otp = sentEmail.ExtractedOtp,
        });

        Assert.Equal(user.Id, verifyResponse.UserId);
        Assert.Equal(AccountStatus.ACTIVE, user.AccountStatus);
    }

    [Fact]
    public async Task ResendOtpAsync_WhenEmailSenderThrows_DoesNotThrowAndRecordsEmailSendFailedAudit()
    {
        var (user, _) = await SeedPendingUserWithChallenge(
            email: "smtpfail@medflow.test",
            createdAt: DateTime.UtcNow.AddSeconds(-60));
        _emailSender.ShouldThrow = true;

        var response = await _sut.ResendOtpAsync(new ResendOtpRequest
        {
            Email = "smtpfail@medflow.test",
        });

        Assert.NotNull(response);
        Assert.Contains(_audit.Entries, e => e.Action == "OTP_RESEND" && e.Actor == user.Id);
        Assert.Contains(_audit.Entries, e => e.Action == "EMAIL_SEND_FAILED" && e.Actor == user.Id);
    }
}
