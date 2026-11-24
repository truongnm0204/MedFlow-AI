using System.Text.Json;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace MedFlow.UnitTests.SystemAdmin;

public sealed class IntegrationConfigServiceTests : IDisposable
{
    private static readonly DateTime FixedNowUtc = new(2026, 11, 25, 3, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class IntegrationTestDbContext : DbContext, IApplicationDbContext
    {
        public IntegrationTestDbContext(DbContextOptions<IntegrationTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for IntegrationConfigService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.User);
                b.Ignore(s => s.Department);
                b.Ignore(s => s.Doctor);
            });
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<HospitalPolicy>().HasKey(p => p.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.ActorUser);
                b.Ignore(a => a.IpAddress);
            });
        }
    }

    private const string RawSePaySecret = "sepay-super-secret-webhook-key-2026";
    private const string RawSmtpPassword = "smtp-gmail-app-password-secret-999";
    private const string RawAiApiKey = "sk-live-gemini-secret-key-xyz789";

    private readonly IntegrationTestDbContext _db;
    private readonly IntegrationConfigService _sut;

    public IntegrationConfigServiceTests()
    {
        var options = new DbContextOptionsBuilder<IntegrationTestDbContext>()
            .UseInMemoryDatabase($"IntegrationConfigTests_{Guid.NewGuid():N}")
            .Options;

        _db = new IntegrationTestDbContext(options);
        SeedData(_db);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SePay:WebhookApiKey"] = RawSePaySecret,
                ["QrPayment:PendingExpiryMinutes"] = "15",
                ["Smtp:Host"] = "smtp.medflow.vn",
                ["Smtp:Port"] = "587",
                ["Smtp:Username"] = "noreply@medflow.vn",
                ["Smtp:Password"] = RawSmtpPassword,
                ["Smtp:FromName"] = "MedFlow Central",
                ["AiRecommendation:ApiKey"] = RawAiApiKey,
                ["AiRecommendation:Model"] = "gemini-2.5-flash",
            })
            .Build();

        _sut = new IntegrationConfigService(
            _db,
            config,
            new FixedTimeProvider(new DateTimeOffset(FixedNowUtc)));
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void SeedData(IntegrationTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện MedFlow Cơ sở 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện MedFlow Cơ sở 2", IsActive = true });

        db.Roles.AddRange(
            new Role { Id = 1, Code = RoleNames.SystemAdmin, Name = "Quản trị hệ thống" },
            new Role { Id = 2, Code = RoleNames.HospitalAdmin, Name = "Quản trị bệnh viện" },
            new Role { Id = 3, Code = RoleNames.Doctor, Name = "Bác sĩ" });

        db.Permissions.AddRange(
            new Permission { Id = 11, Code = PermissionCodes.HospitalConfig },
            new Permission { Id = 12, Code = PermissionCodes.UserManage });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 11 },
            new RolePermission { RoleId = 1, PermissionId = 12 },
            new RolePermission { RoleId = 2, PermissionId = 11 });

        db.Users.AddRange(
            new User { Id = 13, Email = "system_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "hospital_admin02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 21, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 13, RoleId = 1 },
            new UserRole { UserId = 11, RoleId = 2 },
            new UserRole { UserId = 12, RoleId = 2 },
            new UserRole { UserId = 21, RoleId = 3 });

        db.Staff.AddRange(
            new Staff { Id = 101, UserId = 13, HospitalId = 1, StaffCode = "STF-011", FullName = "QTHT Lưu Huy", Status = "ACTIVE" },
            new Staff { Id = 102, UserId = 11, HospitalId = 1, StaffCode = "STF-009", FullName = "QLBV Phan Bình", Status = "ACTIVE" },
            new Staff { Id = 103, UserId = 12, HospitalId = 2, StaffCode = "STF-010", FullName = "QLBV Đỗ Hương", Status = "ACTIVE" });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetIntegrationsAsync_MasksAllSecretsAndNeverExposesRawKeys()
    {
        var result = await _sut.GetIntegrationsAsync(13, new GetIntegrationsRequest(HospitalId: 1));
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain(RawSePaySecret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(RawSmtpPassword, json, StringComparison.Ordinal);
        Assert.DoesNotContain(RawAiApiKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain("noreply@medflow.vn", json, StringComparison.Ordinal);

        Assert.Equal(3, result.Integrations.Count);
        Assert.All(result.Integrations, item =>
        {
            Assert.True(item.SecretStatus.IsConfigured);
            Assert.Equal("••••••••••••", item.SecretStatus.MaskedValue);
        });

        var smtp = result.Integrations.Single(i => i.ProviderKey == IntegrationConfigService.ProviderSmtpEmail);
        Assert.Equal("no***@medflow.vn", smtp.Smtp!.SenderIdentity);
    }

    [Fact]
    public async Task UpdateIntegrationAsync_VietQrSePay_SyncsPaymentAccountVersionsPolicyAndWritesAudit()
    {
        var updated = await _sut.UpdateIntegrationAsync(
            11,
            "vietqr-sepay",
            new UpdateIntegrationConfigRequest
            {
                Enabled = true,
                BankBin = "970422",
                AccountNumber = "0123456789",
                AccountName = "Benh Vien MedFlow CS1",
                TransferPrefix = "mfcs1",
                PendingExpiryMinutes = 20,
                Reason = "Cấu hình tài khoản nhận viện phí Cơ sở 1",
            },
            "10.0.0.15");

        Assert.Equal("VIETQR_SEPAY", updated.ProviderKey);
        Assert.True(updated.Enabled);
        Assert.Equal("ACTIVE", updated.Status);
        Assert.Equal(1, updated.PolicyVersion);
        Assert.Equal("970422", updated.VietQr!.BankBin);
        Assert.Equal("0123456789", updated.VietQr.AccountNumber);
        Assert.Equal("BENH VIEN MEDFLOW CS1", updated.VietQr.AccountName);
        Assert.Equal("MFCS1", updated.VietQr.TransferPrefix);
        Assert.Equal(20, updated.VietQr.PendingExpiryMinutes);

        // Verify VietQrAccountPolicyReader compatibility for QR payment generation
        var qrAccount = await VietQrAccountPolicyReader.ReadAsync(
            _db,
            1,
            DateTime.SpecifyKind(FixedNowUtc, DateTimeKind.Unspecified),
            CancellationToken.None);
        Assert.Equal("970422", qrAccount.Bin);
        Assert.Equal("0123456789", qrAccount.AccountNumber);
        Assert.Equal("BENH VIEN MEDFLOW CS1", qrAccount.AccountName);

        var audit = Assert.Single(_db.AuditLogs);
        Assert.Equal("INTEGRATION_CONFIG_UPDATED", audit.Action);
        Assert.Equal("INTEGRATION_CONFIG", audit.EntityType);
        Assert.Equal(1L, audit.HospitalId);
        Assert.Equal(11L, audit.ActorUserId);

        // Second update increments version and closes previous row
        var v2 = await _sut.UpdateIntegrationAsync(
            11,
            "VIETQR_SEPAY",
            new UpdateIntegrationConfigRequest
            {
                Enabled = true,
                BankBin = "970422",
                AccountNumber = "0123456789",
                AccountName = "Benh Vien MedFlow CS1",
                TransferPrefix = "MFCS1",
                PendingExpiryMinutes = 30,
            },
            "10.0.0.15");

        Assert.Equal(2, v2.PolicyVersion);
        Assert.Equal(2, await _db.AuditLogs.CountAsync());

        // Identical payload does not create duplicate version
        var unchanged = await _sut.UpdateIntegrationAsync(
            11,
            "VIETQR_SEPAY",
            new UpdateIntegrationConfigRequest
            {
                Enabled = true,
                BankBin = "970422",
                AccountNumber = "0123456789",
                AccountName = "Benh Vien MedFlow CS1",
                TransferPrefix = "MFCS1",
                PendingExpiryMinutes = 30,
            },
            "10.0.0.15");

        Assert.Equal(2, unchanged.PolicyVersion);
        Assert.Equal(2, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task UpdateIntegrationAsync_HospitalIsolation_PreventsCrossHospitalReadAndWrite()
    {
        // Hospital Admin 1 cannot read or write Hospital 2
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetIntegrationsAsync(11, new GetIntegrationsRequest(HospitalId: 2)));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.UpdateIntegrationAsync(
                11,
                "smtp-email",
                new UpdateIntegrationConfigRequest
                {
                    HospitalId = 2,
                    Enabled = false,
                },
                "127.0.0.1"));

        // System Admin updates AI provider on Hospital 2; Hospital 1 stays on default
        var aiHosp2 = await _sut.UpdateIntegrationAsync(
            13,
            "ai-provider",
            new UpdateIntegrationConfigRequest
            {
                HospitalId = 2,
                Enabled = true,
                ProviderName = "OPENAI",
                ModelName = "gpt-4.1-mini",
                TimeoutSeconds = 20,
                MaxSuggestions = 4,
                DisclaimerTemplate = "Gợi ý chuyên khoa chỉ mang tính tham khảo ban đầu tại Cơ sở 2.",
            },
            "127.0.0.1");

        Assert.Equal("OPENAI", aiHosp2.Ai!.ProviderName);
        Assert.Equal("gpt-4.1-mini", aiHosp2.Ai.ModelName);

        var hosp1List = await _sut.GetIntegrationsAsync(11, new GetIntegrationsRequest());
        var aiHosp1 = hosp1List.Integrations.Single(i => i.ProviderKey == "AI_PROVIDER");
        Assert.Equal("GEMINI", aiHosp1.Ai!.ProviderName);
        Assert.Equal(0, aiHosp1.PolicyVersion);
    }

    [Fact]
    public async Task UpdateIntegrationAsync_RejectsRawSecretsAndInvalidInputs()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateIntegrationAsync(
                13,
                "ai-provider",
                new UpdateIntegrationConfigRequest
                {
                    HospitalId = 1,
                    Enabled = true,
                    ApiKey = "sk-raw-attempt",
                },
                "127.0.0.1"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateIntegrationAsync(
                13,
                "vietqr-sepay",
                new UpdateIntegrationConfigRequest
                {
                    HospitalId = 1,
                    Enabled = true,
                    BankBin = "123",
                    AccountNumber = "0123456789",
                    AccountName = "MedFlow",
                },
                "127.0.0.1"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateIntegrationAsync(
                13,
                "smtp-email",
                new UpdateIntegrationConfigRequest
                {
                    HospitalId = 1,
                    Enabled = true,
                    ReplyToEmail = "not-an-email",
                },
                "127.0.0.1"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateIntegrationAsync(
                13,
                "unknown-provider",
                new UpdateIntegrationConfigRequest { HospitalId = 1, Enabled = true },
                "127.0.0.1"));

        Assert.Equal(0, await _db.HospitalPolicies.CountAsync());
        Assert.Equal(0, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task GetIntegrationsAsync_EmptyRolePermissionsOrNonAdmin_FailsClosedWithForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetIntegrationsAsync(21, new GetIntegrationsRequest()));

        _db.RolePermissions.RemoveRange(_db.RolePermissions);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetIntegrationsAsync(13, new GetIntegrationsRequest()));
    }
}
