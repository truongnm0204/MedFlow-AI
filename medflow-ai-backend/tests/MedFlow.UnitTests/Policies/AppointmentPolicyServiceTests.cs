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

public sealed class AppointmentPolicyServiceTests : IDisposable
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
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();

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
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();

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
            => new("DbSet này không được dùng trong AppointmentPolicyServiceTests.");
    }

    private readonly PolicyTestDbContext _db;
    private readonly AppointmentPolicyService _sut;

    public AppointmentPolicyServiceTests()
    {
        var options = new DbContextOptionsBuilder<PolicyTestDbContext>()
            .UseInMemoryDatabase($"policy-{Guid.NewGuid():N}")
            .Options;

        _db = new PolicyTestDbContext(options);
        _sut = new AppointmentPolicyService(_db, new AuditLogger(_db));

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

    private static UpdateAppointmentPolicyRequest ValidRequest(Action<UpdateAppointmentPolicyRequest>? mutate = null)
    {
        var req = new UpdateAppointmentPolicyRequest
        {
            BookingWindowDays = 30,
            BookingLeadHours = 2,
            MaxActiveBookings = 3,
            RescheduleMinimumHours = 12,
            MaxReschedules = 2,
            CancelMinimumHours = 6,
            NoShowGraceMinutes = 15,
            NoShowMaxCount = 3,
            NoShowLookbackDays = 90,
            NoShowBlockDays = 30,
        };
        mutate?.Invoke(req);
        return req;
    }

    [Fact]
    public async Task GetEffectiveAsync_NoRows_ReturnsDefaults()
    {
        var result = await _sut.GetEffectiveAsync(1);

        Assert.Equal(30, result.BookingWindowDays);
        Assert.Equal(2, result.BookingLeadHours);
        Assert.Equal(3, result.MaxActiveBookings);
        Assert.Equal(12, result.RescheduleMinimumHours);
        Assert.Equal(2, result.MaxReschedules);
        Assert.Equal(6, result.CancelMinimumHours);
        Assert.Equal(15, result.NoShowGraceMinutes);
        Assert.Equal(3, result.NoShowMaxCount);
        Assert.Equal(90, result.NoShowLookbackDays);
        Assert.Equal(30, result.NoShowBlockDays);
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
                PolicyType = "APPOINTMENT",
                PolicyKey = "cancel_hours",
                PolicyValue = "{\"minimum_hours\":4}",
                PolicyVersion = 1,
                EffectiveFrom = now.AddDays(-5),
                EffectiveTo = now.AddHours(-2),
            },
            new HospitalPolicy
            {
                Id = 2,
                HospitalId = 1,
                PolicyType = "APPOINTMENT",
                PolicyKey = "cancel_hours",
                PolicyValue = "{\"minimum_hours\":6}",
                PolicyVersion = 2,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 3,
                HospitalId = 1,
                PolicyType = "APPOINTMENT",
                PolicyKey = "cancel_hours",
                PolicyValue = "{\"minimum_hours\":9}",
                PolicyVersion = 3,
                EffectiveFrom = now.AddDays(1),
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 4,
                HospitalId = 2,
                PolicyType = "APPOINTMENT",
                PolicyKey = "cancel_hours",
                PolicyValue = "{\"minimum_hours\":24}",
                PolicyVersion = 10,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 5,
                HospitalId = 1,
                PolicyType = "QUEUE",
                PolicyKey = "cancel_hours",
                PolicyValue = "{\"minimum_hours\":48}",
                PolicyVersion = 10,
                EffectiveFrom = activeFrom,
                EffectiveTo = null,
            });
        await _db.SaveChangesAsync();

        var result = await _sut.GetEffectiveAsync(1);

        Assert.Equal(6, result.CancelMinimumHours);
        Assert.Equal(activeFrom, result.LastUpdatedAt);
    }

    [Fact]
    public async Task GetEffectiveAsync_MalformedValue_FallsBackToDefault()
    {
        _db.HospitalPolicies.Add(new HospitalPolicy
        {
            Id = 1,
            HospitalId = 1,
            PolicyType = "APPOINTMENT",
            PolicyKey = "reschedule_hours",
            PolicyValue = "{\"wrong\":1}",
            PolicyVersion = 1,
            EffectiveFrom = DateTime.UtcNow.AddHours(-1),
            EffectiveTo = null,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetEffectiveAsync(1);

        Assert.Equal(12, result.RescheduleMinimumHours);
    }

    [Fact]
    public async Task UpdateForAdminAsync_ChangedKey_ClosesOldRowAndInsertsNextVersion()
    {
        var initialFrom = DateTime.UtcNow.AddDays(-1);
        _db.HospitalPolicies.AddRange(
            new HospitalPolicy
            {
                Id = 1,
                HospitalId = 1,
                PolicyType = "APPOINTMENT",
                PolicyKey = "reschedule_hours",
                PolicyValue = "{\"minimum_hours\":12}",
                PolicyVersion = 1,
                EffectiveFrom = initialFrom,
                EffectiveTo = null,
            },
            new HospitalPolicy
            {
                Id = 2,
                HospitalId = 1,
                PolicyType = "APPOINTMENT",
                PolicyKey = "cancel_hours",
                PolicyValue = "{\"minimum_hours\":6}",
                PolicyVersion = 1,
                EffectiveFrom = initialFrom,
                EffectiveTo = null,
            });
        await _db.SaveChangesAsync();

        var request = ValidRequest(r => r.CancelMinimumHours = 8);
        var dto = await _sut.UpdateForAdminAsync(11, request, "127.0.0.1");

        Assert.Equal(8, dto.CancelMinimumHours);

        var allRows = await _db.HospitalPolicies
            .Where(p => p.HospitalId == 1 && p.PolicyType == "APPOINTMENT")
            .ToListAsync();

        var oldCancel = Assert.Single(allRows, r => r.PolicyKey == "cancel_hours" && r.PolicyVersion == 1);
        Assert.NotNull(oldCancel.EffectiveTo);

        var newCancel = Assert.Single(allRows, r => r.PolicyKey == "cancel_hours" && r.PolicyVersion == 2);
        Assert.Null(newCancel.EffectiveTo);
        using (var doc = JsonDocument.Parse(newCancel.PolicyValue!))
        {
            Assert.Equal(8, doc.RootElement.GetProperty("minimum_hours").GetInt32());
        }

        var rescheduleRow = Assert.Single(allRows, r => r.PolicyKey == "reschedule_hours");
        Assert.Equal(1, rescheduleRow.PolicyVersion);
        Assert.Null(rescheduleRow.EffectiveTo);

        var otherKeys = new[]
        {
            "booking_window_days",
            "booking_lead_hours",
            "max_active_bookings",
            "max_reschedules",
            "no_show_grace_minutes",
            "no_show_limit",
        };

        foreach (var key in otherKeys)
        {
            var row = Assert.Single(allRows, r => r.PolicyKey == key);
            Assert.Equal(1, row.PolicyVersion);
            Assert.Null(row.EffectiveTo);
        }

        var audit = Assert.Single(_db.AuditLogs);
        Assert.Equal("APPOINTMENT_POLICY_UPDATE", audit.Action);
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

        Assert.Equal(8, policyCountAfterFirst);
        Assert.Equal(1, auditCountAfterFirst);
        Assert.Equal(policyCountAfterFirst, policyCountAfterSecond);
        Assert.Equal(auditCountAfterFirst, auditCountAfterSecond);
    }

    [Theory]
    [InlineData("BookingWindowDays", 0)]
    [InlineData("NoShowMaxCount", 21)]
    [InlineData("CancelMinimumHours", -1)]
    [InlineData("MaxReschedules", null)]
    public async Task UpdateForAdminAsync_InvalidValue_ThrowsValidationAndPersistsNothing(
        string field,
        int? invalidValue)
    {
        var request = ValidRequest(r =>
        {
            switch (field)
            {
                case "BookingWindowDays":
                    r.BookingWindowDays = invalidValue;
                    break;
                case "NoShowMaxCount":
                    r.NoShowMaxCount = invalidValue;
                    break;
                case "CancelMinimumHours":
                    r.CancelMinimumHours = invalidValue;
                    break;
                case "MaxReschedules":
                    r.MaxReschedules = invalidValue;
                    break;
            }
        });

        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.UpdateForAdminAsync(11, request, "127.0.0.1"));

        Assert.Equal(0, await _db.HospitalPolicies.CountAsync());
        Assert.Equal(0, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task UpdateForAdminAsync_LeadHoursNotBelowWindow_Throws()
    {
        var request = ValidRequest(r =>
        {
            r.BookingWindowDays = 1;
            r.BookingLeadHours = 24;
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.UpdateForAdminAsync(11, request, "127.0.0.1"));

        Assert.Equal("Thời gian đặt trước tối thiểu phải nhỏ hơn cửa sổ đặt lịch.", ex.Message);
        Assert.Equal(0, await _db.HospitalPolicies.CountAsync());
    }

    [Theory]
    [InlineData(99L)]
    [InlineData(12345L)]
    public async Task GetForAdminAsync_UserWithoutHospital_ThrowsForbidden(long userId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetForAdminAsync(userId));
    }
}
