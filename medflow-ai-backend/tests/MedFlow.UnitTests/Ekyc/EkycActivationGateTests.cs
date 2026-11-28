using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Auth;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace MedFlow.UnitTests.Ekyc;

/// <summary>
/// Activation gate behavior (plan v2.1 mục 3.2/3.13): with EKYC_MANDATORY_REGISTRATION ON,
/// email OTP verification keeps the account PENDING and issues a registration-scope token;
/// with the flag OFF the legacy behavior is byte-identical — immediate ACTIVE, no token.
/// Login for a PENDING account under flag ON returns a registration-scope resume token.
/// </summary>
public sealed class EkycActivationGateTests : IDisposable
{
    private const string TestOtpSecret = "unit-test-otp-secret-32-bytes-minimum!!";
    private const string TestJwtSecret = "unit-test-jwt-secret-key-at-least-32-bytes-long!!";

    private sealed class GateTestDbContext(DbContextOptions<GateTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => Set<AuthVerificationChallenge>();
        public DbSet<Role> Roles => Set<Role>();

        public DbSet<Staff> Staff => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
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
        public DbSet<EkycVerificationSession> EkycVerificationSessions => throw NotUsed();
        public DbSet<EkycProxyCallLog> EkycProxyCallLogs => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        private static Exception NotUsed() => new InvalidOperationException("Not used in this test context.");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
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
            modelBuilder.Ignore<EkycVerificationSession>();
            modelBuilder.Ignore<EkycProxyCallLog>();
            modelBuilder.Ignore<Staff>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Property(u => u.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<User>().Ignore(u => u.Patient);
            modelBuilder.Entity<User>().Ignore(u => u.Staff);
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>().Property(p => p.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<Patient>().Ignore(p => p.User);
            modelBuilder.Entity<AuthVerificationChallenge>().HasKey(c => c.Id);
            modelBuilder.Entity<AuthVerificationChallenge>().Property(c => c.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
        }
    }

    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:OtpSecret"] = TestOtpSecret,
            ["Jwt:Secret"] = TestJwtSecret,
        })
        .Build();

    private sealed class NullEmailSender : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NullAuditLogger : IAuditLogger
    {
        public void Add(long? userId, string action, string entityType, string? entityId, object? data, string? ipAddress)
        {
        }
    }

    private readonly GateTestDbContext _db;
    private readonly MedFlow.Infrastructure.Identity.JwtTokenService _jwtService;

    public EkycActivationGateTests()
    {
        var options = new DbContextOptionsBuilder<GateTestDbContext>()
            .UseInMemoryDatabase($"ekyc-gate-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new GateTestDbContext(options);
        _jwtService = new MedFlow.Infrastructure.Identity.JwtTokenService(BuildConfiguration());
    }

    public void Dispose() => _db.Dispose();

    private AuthOtpService CreateOtpService(bool mandatoryRegistration)
        => new(
            _db,
            new NullEmailSender(),
            BuildConfiguration(),
            NullLogger<AuthOtpService>.Instance,
            new NullAuditLogger(),
            jwtTokenService: _jwtService,
            ekycOptions: Options.Create(new EkycOptions { MandatoryRegistration = mandatoryRegistration }));

    private static string ComputeOtpHmacHex(string otp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestOtpSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(otp))).ToLowerInvariant();
    }

    private async Task<User> SeedPendingUserWithChallenge(string otp = "123456")
    {
        var user = new User
        {
            Email = "pending@medflow.test",
            AccountStatus = AccountStatus.PENDING,
            AuthProvider = "LOCAL",
            PasswordHash = "hash",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.AuthVerificationChallenges.Add(new AuthVerificationChallenge
        {
            UserId = user.Id,
            Purpose = ChallengePurpose.REGISTER_EMAIL,
            Channel = ChallengeChannel.EMAIL,
            SecretHash = ComputeOtpHmacHex(otp),
            Status = ChallengeStatus.PENDING,
            Attempts = 0,
            MaxAttempts = 5,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
        });
        await _db.SaveChangesAsync();
        return user;
    }

    // ---------------------------------------------------------------
    // Flag ON: OTP verify → stays PENDING + registration token
    // ---------------------------------------------------------------

    [Fact]
    public async Task FlagOn_OtpVerify_KeepsPendingAndIssuesRegistrationToken()
    {
        var user = await SeedPendingUserWithChallenge();
        var sut = CreateOtpService(mandatoryRegistration: true);

        var response = await sut.VerifyOtpAsync(new VerifyOtpRequest
        {
            Email = "pending@medflow.test",
            Otp = "123456",
        });

        Assert.True(response.RequiresEkycVerification);
        Assert.NotNull(response.AccessToken);
        Assert.NotNull(response.AccessTokenExpiresIn);
        Assert.Equal(AccountStatus.PENDING, user.AccountStatus); // NOT activated
        Assert.NotNull(user.EmailVerifiedAt); // email IS verified — the gate is eKYC, not email

        // The token is genuinely a registration-scope token.
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        Assert.Contains(jwt.Claims, c =>
            c.Type == MedFlow.Domain.Constants.AuthTokenScopes.ClaimType &&
            c.Value == MedFlow.Domain.Constants.AuthTokenScopes.EkycRegistration);
    }

    // ---------------------------------------------------------------
    // Flag OFF: legacy behavior byte-identical — immediate ACTIVE, no token
    // ---------------------------------------------------------------

    [Fact]
    public async Task FlagOff_OtpVerify_ActivatesImmediatelyWithoutToken()
    {
        var user = await SeedPendingUserWithChallenge();
        var sut = CreateOtpService(mandatoryRegistration: false);

        var response = await sut.VerifyOtpAsync(new VerifyOtpRequest
        {
            Email = "pending@medflow.test",
            Otp = "123456",
        });

        Assert.False(response.RequiresEkycVerification);
        Assert.Null(response.AccessToken);
        Assert.Equal(AccountStatus.ACTIVE, user.AccountStatus);
    }
}
