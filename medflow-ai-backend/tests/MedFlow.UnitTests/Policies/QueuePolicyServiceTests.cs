using System.Text.Json;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Policies;

public sealed class QueuePolicyServiceTests : IDisposable
{
    private sealed class PolicyTestDbContext : DbContext, IApplicationDbContext
    {
        public PolicyTestDbContext(DbContextOptions<PolicyTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => throw NotUsed();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => throw NotUsed();
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
            modelBuilder.Ignore<User>();
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Hospital>();
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

            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.User);
                b.Ignore(s => s.Department);
                b.Ignore(s => s.Doctor);
            });

            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.ActorUser);
            });

            modelBuilder.Entity<HospitalPolicy>().HasKey(p => p.Id);
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong QueuePolicyServiceTests.");
    }

    private readonly PolicyTestDbContext _db;
    private readonly QueuePolicyService _sut;

    public QueuePolicyServiceTests()
    {
        var options = new DbContextOptionsBuilder<PolicyTestDbContext>()
            .UseInMemoryDatabase($"queue-policy-{Guid.NewGuid():N}")
            .Options;

        _db = new PolicyTestDbContext(options);
        _sut = new QueuePolicyService(_db, new AuditLogger(_db));

        _db.Staff.AddRange(
            new Staff
            {
                Id = 9,
                UserId = 11,
                HospitalId = 1,
                StaffCode = "STF-0009",
                FullName = "Admin Hospital 1",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 50,
                UserId = 99,
                HospitalId = null,
                StaffCode = "STF-0050",
                FullName = "Admin Without Hospital",
                Status = "ACTIVE",
            });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static UpdateQueuePolicyRequest ValidRequest(Action<UpdateQueuePolicyRequest>? mutate = null)
    {
        var req = new UpdateQueuePolicyRequest
        {
            GraceMinutes = 15,
            ExpectedDurationMinutes = 20,
            SafetyBufferMinutes = 5,
            WalkinPriorityRatio = new UpdateQueuePriorityRatioRequest
            {
                Priority = 3,
                Normal = 1,
            },
            PublicNumberImmutable = true,
            ManualPriorityRequiresVerification = true,
        };
        mutate?.Invoke(req);
        return req;
    }

    [Fact]
    public async Task GetEffectiveAsync_NoRows_ReturnsDefaults()
    {
        var result = await _sut.GetEffectiveAsync(1);

        Assert.Equal(15, result.GraceMinutes);
        Assert.Equal(20, result.ExpectedDurationMinutes);
        Assert.Equal(5, result.SafetyBufferMinutes);
        Assert.Equal(3, result.WalkinPriorityRatio.Priority);
        Assert.Equal(1, result.WalkinPriorityRatio.Normal);
        Assert.True(result.PublicNumberImmutable);
        Assert.True(result.ManualPriorityRequiresVerification);
        Assert.Null(result.LastUpdatedAt);
    }

    [Fact]
    public async Task GetEffectiveAsync_StoredRows_UsesCurrentVersionOnly()
    {
        var now = DateTime.UtcNow;
        var activeFrom = now.AddHours(-2);

        _db.HospitalPolicies.AddRange(
            new HospitalPolicy
            {
                Id = 1,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "grace_minutes",
                PolicyValue = "{\"minutes\":15}",
                PolicyVersion = 1,
                EffectiveFrom = now.AddDays(-5),
                EffectiveTo = now.AddHours(-2),
            },
            new HospitalPolicy
            {
                Id = 2,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "grace_minutes",
                PolicyValue = "{\"minutes\":10}",
                PolicyVersion = 2,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 3,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "grace_minutes",
                PolicyValue = "{\"minutes\":25}",
                PolicyVersion = 3,
                EffectiveFrom = now.AddDays(1),
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 4,
                HospitalId = 2,
                PolicyType = "QUEUE",
                PolicyKey = "grace_minutes",
                PolicyValue = "{\"minutes\":45}",
                PolicyVersion = 10,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 5,
                HospitalId = 1,
                PolicyType = "APPOINTMENT",
                PolicyKey = "grace_minutes",
                PolicyValue = "{\"minutes\":60}",
                PolicyVersion = 10,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 6,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "walkin_priority_ratio",
                PolicyValue = "{\"priority\":5,\"normal\":2}",
                PolicyVersion = 1,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 7,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "manual_priority_requires_verification",
                PolicyValue = "{\"enabled\":false}",
                PolicyVersion = 1,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            });
        await _db.SaveChangesAsync();

        var result = await _sut.GetEffectiveAsync(1);

        Assert.Equal(10, result.GraceMinutes);
        Assert.Equal(5, result.WalkinPriorityRatio.Priority);
        Assert.Equal(2, result.WalkinPriorityRatio.Normal);
        Assert.False(result.ManualPriorityRequiresVerification);
        Assert.Equal(activeFrom, result.LastUpdatedAt);
    }

    [Fact]
    public async Task GetEffectiveAsync_MalformedValue_FallsBackToDefault()
    {
        var activeFrom = DateTime.UtcNow.AddHours(-1);

        _db.HospitalPolicies.AddRange(
            new HospitalPolicy
            {
                Id = 1,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "manual_priority_requires_verification",
                PolicyValue = "{\"enabled\":\"yes\"}",
                PolicyVersion = 1,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 2,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "safety_buffer_minutes",
                PolicyValue = "{\"wrong\":1}",
                PolicyVersion = 1,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            });
        await _db.SaveChangesAsync();

        var result = await _sut.GetEffectiveAsync(1);

        Assert.True(result.ManualPriorityRequiresVerification);
        Assert.Equal(5, result.SafetyBufferMinutes);
    }

    [Fact]
    public async Task UpdateForAdminAsync_ChangedKey_ClosesOldRowAndInsertsNextVersion()
    {
        var initialFrom = DateTime.UtcNow.AddDays(-1);
        _db.HospitalPolicies.AddRange(
            new HospitalPolicy
            {
                Id = 3,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "walkin_priority_ratio",
                PolicyValue = "{\"priority\": 3, \"normal\": 1}",
                PolicyVersion = 1,
                EffectiveFrom = initialFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 4,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "grace_minutes",
                PolicyValue = "{\"minutes\": 15}",
                PolicyVersion = 1,
                EffectiveFrom = initialFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 5,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "public_number_immutable",
                PolicyValue = "{\"enabled\": true}",
                PolicyVersion = 1,
                EffectiveFrom = initialFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 6,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "manual_priority_requires_verification",
                PolicyValue = "{\"enabled\": true}",
                PolicyVersion = 1,
                EffectiveFrom = initialFrom,
                EffectiveTo = null,
            });
        await _db.SaveChangesAsync();

        var request = ValidRequest(r => r.GraceMinutes = 10);
        var dto = await _sut.UpdateForAdminAsync(11, request, "127.0.0.1");

        Assert.Equal(10, dto.GraceMinutes);

        var allRows = await _db.HospitalPolicies
            .Where(p => p.HospitalId == 1 && p.PolicyType == "QUEUE")
            .ToListAsync();

        var oldGrace = Assert.Single(allRows, r => r.PolicyKey == "grace_minutes" && r.PolicyVersion == 1);
        Assert.NotNull(oldGrace.EffectiveTo);

        var newGrace = Assert.Single(allRows, r => r.PolicyKey == "grace_minutes" && r.PolicyVersion == 2);
        Assert.Null(newGrace.EffectiveTo);
        using (var doc = JsonDocument.Parse(newGrace.PolicyValue!))
        {
            Assert.Equal(10, doc.RootElement.GetProperty("minutes").GetInt32());
        }

        foreach (var unchangedKey in new[]
        {
            "walkin_priority_ratio",
            "public_number_immutable",
            "manual_priority_requires_verification",
        })
        {
            var row = Assert.Single(allRows, r => r.PolicyKey == unchangedKey);
            Assert.Equal(1, row.PolicyVersion);
            Assert.Null(row.EffectiveTo);
        }

        foreach (var insertedKey in new[]
        {
            "expected_duration_minutes",
            "safety_buffer_minutes",
        })
        {
            var row = Assert.Single(allRows, r => r.PolicyKey == insertedKey);
            Assert.Equal(1, row.PolicyVersion);
            Assert.Null(row.EffectiveTo);
        }

        var audit = Assert.Single(_db.AuditLogs);
        Assert.Equal("QUEUE_POLICY_UPDATE", audit.Action);
        Assert.Equal("HOSPITAL_POLICY", audit.EntityType);
        Assert.Equal("1", audit.EntityId);
        Assert.Equal(11, audit.ActorUserId);
    }

    [Fact]
    public async Task UpdateForAdminAsync_NoChanges_WritesNothing()
    {
        var request = ValidRequest();

        await _sut.UpdateForAdminAsync(11, request, "127.0.0.1");
        var policyCountAfterFirst = await _db.HospitalPolicies.CountAsync();
        var auditCountAfterFirst = await _db.AuditLogs.CountAsync();

        await _sut.UpdateForAdminAsync(11, request, "127.0.0.1");
        var policyCountAfterSecond = await _db.HospitalPolicies.CountAsync();
        var auditCountAfterSecond = await _db.AuditLogs.CountAsync();

        Assert.Equal(6, policyCountAfterFirst);
        Assert.Equal(1, auditCountAfterFirst);
        Assert.Equal(policyCountAfterFirst, policyCountAfterSecond);
        Assert.Equal(auditCountAfterFirst, auditCountAfterSecond);
    }

    [Theory]
    [InlineData("GraceMinutes", 121)]
    [InlineData("ExpectedDurationMinutes", 4)]
    [InlineData("SafetyBufferMinutes", -1)]
    [InlineData("Priority", 0)]
    [InlineData("Normal", 11)]
    [InlineData("WalkinPriorityRatioNull", null)]
    [InlineData("ManualPriorityRequiresVerificationNull", null)]
    public async Task UpdateForAdminAsync_InvalidValue_ThrowsValidationAndPersistsNothing(
        string field,
        int? invalidValue)
    {
        var request = ValidRequest(r =>
        {
            switch (field)
            {
                case "GraceMinutes":
                    r.GraceMinutes = invalidValue;
                    break;
                case "ExpectedDurationMinutes":
                    r.ExpectedDurationMinutes = invalidValue;
                    break;
                case "SafetyBufferMinutes":
                    r.SafetyBufferMinutes = invalidValue;
                    break;
                case "Priority":
                    r.WalkinPriorityRatio!.Priority = invalidValue;
                    break;
                case "Normal":
                    r.WalkinPriorityRatio!.Normal = invalidValue;
                    break;
                case "WalkinPriorityRatioNull":
                    r.WalkinPriorityRatio = null;
                    break;
                case "ManualPriorityRequiresVerificationNull":
                    r.ManualPriorityRequiresVerification = null;
                    break;
            }
        });

        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.UpdateForAdminAsync(11, request, "127.0.0.1"));

        Assert.Equal(0, await _db.HospitalPolicies.CountAsync());
        Assert.Equal(0, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task UpdateForAdminAsync_DisablePublicNumberImmutable_Throws()
    {
        var request = ValidRequest(r => r.PublicNumberImmutable = false);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.UpdateForAdminAsync(11, request, "127.0.0.1"));

        Assert.Equal("Số thứ tự công khai luôn bất biến sau khi cấp, không thể tắt.", ex.Message);
        Assert.Equal(0, await _db.HospitalPolicies.CountAsync());
    }

    [Fact]
    public async Task UpdateForAdminAsync_DisableManualPriorityVerification_PersistsNewVersion()
    {
        var request = ValidRequest(r => r.ManualPriorityRequiresVerification = false);

        var dto = await _sut.UpdateForAdminAsync(11, request, "127.0.0.1");

        Assert.False(dto.ManualPriorityRequiresVerification);

        var row = await _db.HospitalPolicies
            .SingleAsync(p => p.HospitalId == 1
                && p.PolicyType == "QUEUE"
                && p.PolicyKey == "manual_priority_requires_verification"
                && p.EffectiveTo == null);
        Assert.Equal("{\"enabled\":false}", row.PolicyValue);
    }

    [Theory]
    [InlineData(99L)]
    [InlineData(12345L)]
    public async Task GetForAdminAsync_UserWithoutHospital_ThrowsForbidden(long userId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetForAdminAsync(userId));
    }
}
