using System.Text.RegularExpressions;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace MedFlow.UnitTests.Common;

public sealed class HospitalClockAndShiftRulesTests
{
    private const string TestOtpSecret = "unit-test-otp-secret-32-bytes-minimum!!";

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    [Fact]
    public void HospitalClock_MidnightRolloverUtcToHoChiMinh_ResolvesNextDayLocalWallClockAndToday()
    {
        // 2026-11-24 20:30:00 UTC == 2026-11-25 03:30:00 Asia/Ho_Chi_Minh (UTC+7)
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 24, 20, 30, 0, TimeSpan.Zero));

        var utcNow = HospitalClock.GetUtcNow(clock);
        var utcUnspecified = HospitalClock.GetUtcNowUnspecified(clock);
        var nowOffset = HospitalClock.GetNowOffset(clock);
        var localNow = HospitalClock.GetNow(clock);
        var localToday = HospitalClock.GetToday(clock);

        Assert.Equal(new DateTime(2026, 11, 24, 20, 30, 0, DateTimeKind.Utc), utcNow);
        Assert.Equal(DateTimeKind.Utc, utcNow.Kind);

        Assert.Equal(new DateTime(2026, 11, 24, 20, 30, 0, DateTimeKind.Unspecified), utcUnspecified);
        Assert.Equal(DateTimeKind.Unspecified, utcUnspecified.Kind);

        Assert.Equal(TimeSpan.FromHours(7), nowOffset.Offset);
        Assert.Equal(new DateTime(2026, 11, 25, 3, 30, 0), nowOffset.DateTime);

        Assert.Equal(new DateTime(2026, 11, 25, 3, 30, 0, DateTimeKind.Unspecified), localNow);
        Assert.Equal(DateTimeKind.Unspecified, localNow.Kind);

        Assert.Equal(new DateOnly(2026, 11, 25), localToday);
    }

    [Fact]
    public void HospitalClock_EnsureHospitalLocal_IsIdempotentOnUnspecifiedAndConvertsUtcPlusSevenOnce()
    {
        var alreadyLocalUnspecified = new DateTime(2026, 11, 25, 8, 30, 0, DateTimeKind.Unspecified);
        var normalizedOnce = HospitalClock.EnsureHospitalLocal(alreadyLocalUnspecified);
        var normalizedTwice = HospitalClock.EnsureHospitalLocal(normalizedOnce);

        Assert.Equal(alreadyLocalUnspecified, normalizedOnce);
        Assert.Equal(DateTimeKind.Unspecified, normalizedOnce.Kind);
        Assert.Equal(alreadyLocalUnspecified, normalizedTwice);

        var utcInstant = new DateTime(2026, 11, 25, 1, 30, 0, DateTimeKind.Utc);
        var convertedFromUtc = HospitalClock.EnsureHospitalLocal(utcInstant);
        var convertedAgain = HospitalClock.EnsureHospitalLocal(convertedFromUtc);

        Assert.Equal(new DateTime(2026, 11, 25, 8, 30, 0, DateTimeKind.Unspecified), convertedFromUtc);
        Assert.Equal(DateTimeKind.Unspecified, convertedFromUtc.Kind);
        Assert.Equal(convertedFromUtc, convertedAgain);

        var backToUtc = HospitalClock.ConvertHospitalLocalToUtc(convertedFromUtc);
        Assert.Equal(utcInstant, backToUtc);
        Assert.Equal(DateTimeKind.Utc, backToUtc.Kind);
    }

    [Theory]
    [InlineData(7, 0, false, false, HospitalShift.Morning)]
    [InlineData(7, 0, true, false, HospitalShift.Morning)]
    [InlineData(8, 30, false, true, HospitalShift.Morning)]
    [InlineData(8, 30, true, true, HospitalShift.Morning)]
    [InlineData(12, 30, false, false, HospitalShift.Afternoon)]
    [InlineData(12, 30, true, false, HospitalShift.Afternoon)]
    [InlineData(14, 0, false, true, HospitalShift.Afternoon)]
    [InlineData(14, 0, true, true, HospitalShift.Afternoon)]
    [InlineData(16, 45, false, false, HospitalShift.Afternoon)]
    [InlineData(16, 45, true, false, HospitalShift.Afternoon)]
    [InlineData(18, 0, false, false, HospitalShift.Afternoon)]
    [InlineData(18, 0, true, true, HospitalShift.EveningOnCall)]
    [InlineData(22, 0, false, false, HospitalShift.Afternoon)]
    [InlineData(22, 0, true, false, HospitalShift.EveningOnCall)]
    public void HospitalShiftRules_SevenBoundaryTimes_EnforceWorkingWindowsAndShiftGrouping(
        int hour,
        int minute,
        bool includeDiagnosticEvening,
        bool expectedWorkingHours,
        HospitalShift expectedShift)
    {
        var time = new TimeOnly(hour, minute);
        var localDateTime = new DateTime(2026, 11, 25, hour, minute, 0, DateTimeKind.Unspecified);

        Assert.Equal(expectedWorkingHours, HospitalShiftRules.IsWithinWorkingHours(time, includeDiagnosticEvening));
        Assert.Equal(expectedWorkingHours, HospitalShiftRules.IsWithinWorkingHours(localDateTime, includeDiagnosticEvening));
        Assert.Equal(expectedShift, HospitalShiftRules.ResolveShift(time, includeDiagnosticEvening));
        Assert.Equal(expectedShift, HospitalShiftRules.ResolveShift(localDateTime, includeDiagnosticEvening));

        var hasActiveShift = HospitalShiftRules.TryGetActiveServingShift(time, includeDiagnosticEvening, out var activeShift);
        Assert.Equal(expectedWorkingHours, hasActiveShift);
        if (expectedWorkingHours)
        {
            Assert.Equal(expectedShift, activeShift);
        }
    }

    [Fact]
    public void HospitalShiftRules_LabelsAndCodes_MatchShiftBuckets()
    {
        Assert.Equal("MORNING", HospitalShiftRules.ResolveShiftCode(new TimeOnly(8, 30)));
        Assert.Equal("AFTERNOON", HospitalShiftRules.ResolveShiftCode(new TimeOnly(14, 0)));
        Assert.Equal("EVENING", HospitalShiftRules.ResolveShiftCode(new TimeOnly(18, 0), includeDiagnosticEvening: true));
        Assert.Equal("AFTERNOON", HospitalShiftRules.ResolveShiftCode(new TimeOnly(18, 0), includeDiagnosticEvening: false));

        Assert.Equal("Ca sáng 07:30–11:30", HospitalShiftRules.ResolveDashboardShiftLabel(new DateTime(2026, 11, 25, 8, 30, 0)));
        Assert.Equal("Ca chiều 13:30–16:30", HospitalShiftRules.ResolveDashboardShiftLabel(new DateTime(2026, 11, 25, 14, 0, 0)));
        Assert.Equal("Ca trực tối 17:00–21:00", HospitalShiftRules.ResolveDashboardShiftLabel(new DateTime(2026, 11, 25, 18, 0, 0), includeDiagnosticEvening: true));
    }

    [Fact]
    public async Task AuthService_And_AuthOtpService_RespectCustomAuthSecurityOptionsAndHospitalDate()
    {
        // 2026-11-24 20:30 UTC -> 2026-11-25 03:30 local (Vietnam date is 20261125)
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 24, 20, 30, 0, TimeSpan.Zero));
        var customOptions = Options.Create(new AuthSecurityOptions
        {
            OtpTtlMinutes = 12,
            OtpResendThrottleSeconds = 90,
            OtpMaxAttempts = 3,
            MaxLoginAttempts = 3,
            LockoutDurationMinutes = 30,
            IdentityClaimTtlDays = 14,
        });

        var dbOptions = new DbContextOptionsBuilder<OptionsTestDbContext>()
            .UseInMemoryDatabase($"AuthOptionsOverride_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new OptionsTestDbContext(dbOptions);
        db.Roles.Add(new Role { Id = 1, Code = RoleNames.Patient, Name = "Bệnh nhân" });
        await db.SaveChangesAsync();

        var emailSender = new CapturingEmailSender();
        var audit = new AuditLogger(db, clock);
        var config = new StubConfiguration();
        var otpService = new AuthOtpService(
            db,
            emailSender,
            config,
            NullLogger<AuthOtpService>.Instance,
            audit,
            customOptions,
            clock);
        var authService = new AuthService(
            db,
            new StubJwtTokenService(clock),
            new StubAuthSessionService(clock),
            config,
            otpService,
            audit,
            customOptions,
            clock);

        var registerResponse = await authService.RegisterAsync(
            new RegisterRequest
            {
                Email = "custom-ttl@medflow.test",
                Password = "ValidPassword123",
                ConfirmPassword = "ValidPassword123",
                FullName = "Nguyễn Văn Tùy Chỉnh",
                DateOfBirth = new DateOnly(1994, 4, 10),
                Gender = "MALE",
                Phone = "0901122334",
            },
            "127.0.0.1");

        var patient = await db.Patients.SingleAsync(p => p.UserId == registerResponse.UserId);
        Assert.StartsWith("PAT-20261125-", patient.PatientCode, StringComparison.Ordinal);

        var initialChallenge = await db.AuthVerificationChallenges.SingleAsync(c => c.UserId == registerResponse.UserId);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(12), initialChallenge.ExpiresAt);
        Assert.Equal(3, initialChallenge.MaxAttempts);

        var initialEmail = Assert.Single(emailSender.Sent);
        Assert.Contains("12 phút", initialEmail.HtmlBody, StringComparison.Ordinal);

        // Resend within 90s throttle (at +60s — would have passed default 45s) must fail
        clock.Advance(TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<ValidationException>(() =>
            authService.ResendOtpAsync(new ResendOtpRequest { Email = "custom-ttl@medflow.test" }));

        // Resend after 90s throttle (at +95s total) succeeds with 12-minute TTL
        clock.Advance(TimeSpan.FromSeconds(35));
        await authService.ResendOtpAsync(new ResendOtpRequest { Email = "custom-ttl@medflow.test" });

        var resentChallenge = await db.AuthVerificationChallenges
            .SingleAsync(c => c.UserId == registerResponse.UserId && c.Status == ChallengeStatus.PENDING);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(12), resentChallenge.ExpiresAt);
        Assert.Contains("12 phút", emailSender.Sent[^1].HtmlBody, StringComparison.Ordinal);

        var latestOtp = emailSender.Sent[^1].ExtractedOtp!;
        await authService.VerifyOtpAsync(new VerifyOtpRequest
        {
            Email = "custom-ttl@medflow.test",
            Otp = latestOtp,
        });

        // 3 failed logins (MaxLoginAttempts = 3) lock account for 30 minutes (LockoutDurationMinutes = 30)
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authService.LoginAsync(
                    new LoginRequest { Email = "custom-ttl@medflow.test", Password = "WrongPassword999" },
                    "127.0.0.1"));
        }

        var lockedUser = await db.Users.SingleAsync(u => u.Id == registerResponse.UserId);
        Assert.Equal(0, lockedUser.FailedLoginAttempts);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(30), lockedUser.LockedUntil);
    }

    [Fact]
    public async Task GenerateQrPaymentService_RespectsCustomPendingExpiryMinutesOption()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 24, 3, 0, 0, TimeSpan.Zero));
        var customQrOptions = Options.Create(new QrPaymentOptions
        {
            PendingExpiryMinutes = 30,
        });

        var dbOptions = new DbContextOptionsBuilder<OptionsTestDbContext>()
            .UseInMemoryDatabase($"QrOptionsOverride_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new OptionsTestDbContext(dbOptions);
        db.Hospitals.Add(new Hospital { Id = 1, HospitalCode = "H-01", Name = "Bệnh viện MedFlow", IsActive = true });
        db.Roles.Add(new Role { Id = 1, Code = RoleNames.Patient, Name = "Bệnh nhân" });
        db.Users.Add(new User { Id = 10, Email = "qrpatient@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        db.UserRoles.Add(new UserRole { UserId = 10, RoleId = 1 });
        db.Patients.Add(new Patient { Id = 100, UserId = 10, PatientCode = "PAT-100", FullName = "BN QR", Status = "ACTIVE" });
        db.HospitalRooms.Add(new HospitalRoom { Id = 501, HospitalId = 1, RoomCode = "P501", Name = "Phòng 501", RoomType = "EXAM", Status = "ACTIVE" });
        db.Visits.Add(new Visit { Id = 1000, VisitCode = "VIS-1000", PatientId = 100, ExaminationRoomId = 501, Status = "CHECKED_IN" });
        db.VisitCharges.Add(new VisitCharge { Id = 1, VisitId = 1000, UnitPrice = 120_000m, PaymentRequirement = "REQUIRED" });
        db.HospitalPolicies.Add(new HospitalPolicy
        {
            Id = 1,
            HospitalId = 1,
            PolicyType = PaymentPolicyKeys.PolicyType,
            PolicyKey = PaymentPolicyKeys.VietQrAccount,
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN MEDFLOW"}""",
            PolicyVersion = 1,
        });
        await db.SaveChangesAsync();

        var service = new GenerateQrPaymentService(db, clock, customQrOptions);

        var created = await service.CreateAsync(10, "1000", "127.0.0.1");
        Assert.Equal(QrPaymentIssueKind.Created, created.IssueKind);

        // Advance 20 minutes: > default 15 min, but within custom 30 min -> must be REUSED
        clock.Advance(TimeSpan.FromMinutes(20));
        var reused = await service.CreateAsync(10, "1000", "127.0.0.1");
        Assert.Equal(QrPaymentIssueKind.Reused, reused.IssueKind);
        Assert.Equal(created.PaymentId, reused.PaymentId);

        // Advance another 15 minutes (35 min total > 30 min custom) -> expires old & creates new
        clock.Advance(TimeSpan.FromMinutes(15));
        var rotated = await service.CreateAsync(10, "1000", "127.0.0.1");
        Assert.Equal(QrPaymentIssueKind.Created, rotated.IssueKind);
        Assert.NotEqual(created.PaymentId, rotated.PaymentId);
        Assert.Equal(PaymentStatuses.Expired, (await db.Payments.SingleAsync(p => p.Id == created.PaymentId)).Status);
    }

    private sealed class StubConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => key switch
            {
                "Auth:OtpSecret" => TestOtpSecret,
                "Jwt:RefreshTokenExpiryDays" => "7",
                _ => null,
            };
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => throw new NotSupportedException();
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    private sealed class StubJwtTokenService(TimeProvider timeProvider) : IJwtTokenService
    {
        public JwtTokenResult GenerateAccessToken(User user, IEnumerable<string> roles) => new()
        {
            AccessToken = "stub-jwt",
            ExpiresAt = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(30),
        };
    }

    private sealed class StubAuthSessionService(TimeProvider timeProvider) : IAuthSessionService
    {
        public Task<AuthSessionResult> GenerateAsync(long userId, string? createdByIp, CancellationToken ct = default)
            => Task.FromResult(new AuthSessionResult
            {
                RawToken = "stub-refresh",
                Entity = new AuthSession
                {
                    Id = 1,
                    UserId = userId,
                    ExpiresAt = timeProvider.GetUtcNow().UtcDateTime.AddDays(7),
                },
            });

        public Task<RotateResult> RotateAsync(string rawToken, string? ipAddress, CancellationToken ct = default)
            => Task.FromResult(new RotateResult());

        public Task RevokeAsync(string rawToken, string? ipAddress, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RevokeAllForUserAsync(long userId, string? ipAddress, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class CapturingEmailSender : IEmailSender
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

    private sealed class OptionsTestDbContext(DbContextOptions<OptionsTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => Set<DoctorServiceAuthorization>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => Set<AuthVerificationChallenge>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<VisitCharge> VisitCharges => Set<VisitCharge>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<Specialty>();

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
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
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
            modelBuilder.Entity<Doctor>(b =>
            {
                b.HasKey(d => d.Id);
                b.Ignore(d => d.Staff);
                b.Ignore(d => d.Specialty);
                b.Ignore(d => d.ServiceAuthorizations);
                b.Ignore(d => d.ExaminationSlots);
            });
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.Service);
                b.Ignore(s => s.Doctor);
                b.Ignore(s => s.Room);
                b.Ignore(s => s.Appointments);
            });
            modelBuilder.Entity<AuthSession>().HasKey(s => s.Id);
            modelBuilder.Entity<AuthVerificationChallenge>(b =>
            {
                b.HasKey(c => c.Id);
                b.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId);
            });
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
                b.Ignore(a => a.ActorUser);
            });
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.Ignore(r => r.Department);
            });
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.Department);
                b.Ignore(s => s.Specialty);
                b.Ignore(s => s.ServicePrices);
            });
            modelBuilder.Entity<ServicePrice>().HasKey(sp => sp.Id);
            modelBuilder.Entity<HospitalPolicy>().HasKey(hp => hp.Id);
            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.Slot);
                b.Ignore(a => a.Patient);
            });
            modelBuilder.Entity<PatientFamilyLink>(b =>
            {
                b.HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
                b.Ignore(l => l.OwnerPatient);
                b.HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
            });
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<Payment>(b =>
            {
                b.HasKey(p => p.Id);
                b.Property(p => p.Id).ValueGeneratedOnAdd();
            });
            modelBuilder.Entity<VisitCharge>().HasKey(vc => vc.Id);
        }
    }
}
