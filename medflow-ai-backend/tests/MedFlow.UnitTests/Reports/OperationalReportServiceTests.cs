using MedFlow.Application.DTOs.Reports;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Reports;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Reports;

public sealed class OperationalReportServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class OperationalReportTestDbContext : DbContext, IApplicationDbContext
    {
        public OperationalReportTestDbContext(DbContextOptions<OperationalReportTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for OperationalReportService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<Specialty>().HasKey(sp => sp.Id);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Doctor>()
                .HasOne(d => d.Staff)
                .WithOne(s => s.Doctor)
                .HasForeignKey<Doctor>(d => d.StaffId);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<ServicePrice>().HasKey(sp => sp.Id);
            modelBuilder.Entity<ExaminationSlot>().HasKey(es => es.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
        }
    }

    // 2026-11-24 03:00 UTC = 2026-11-24 10:00 Vietnam (UTC+7)
    private static readonly DateTime FixedNowUtc = new(2026, 11, 24, 3, 0, 0, DateTimeKind.Utc);

    private readonly OperationalReportTestDbContext _db;
    private readonly OperationalReportService _service;

    public OperationalReportServiceTests()
    {
        var options = new DbContextOptionsBuilder<OperationalReportTestDbContext>()
            .UseInMemoryDatabase($"OperationalReportTests_{Guid.NewGuid()}")
            .Options;

        _db = new OperationalReportTestDbContext(options);
        SeedData(_db);
        _service = new OperationalReportService(_db, new FixedTimeProvider(new DateTimeOffset(FixedNowUtc)));
    }

    private static void SeedData(OperationalReportTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện MedFlow Hà Nội", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện MedFlow Đà Nẵng", IsActive = true });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "HOSPITAL_ADMIN", Name = "Hospital Admin" },
            new Role { Id = 2, Code = "SYSTEM_ADMIN", Name = "System Admin" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Reception" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "HOSPITAL_CONFIG" },
            new Permission { Id = 2, Code = "AUDIT_READ" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 1 },
            new RolePermission { RoleId = 2, PermissionId = 2 });

        db.Users.AddRange(
            new User { Id = 11, Email = "hadmin1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "sysadmin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "reception@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 11, RoleId = 1 },
            new UserRole { UserId = 12, RoleId = 2 },
            new UserRole { UserId = 13, RoleId = 3 });

        db.Departments.AddRange(
            new Department { Id = 10, HospitalId = 1, DepartmentCode = "DEP-GI", Name = "Nội Tiêu Hóa", IsActive = true },
            new Department { Id = 11, HospitalId = 1, DepartmentCode = "DEP-CAR", Name = "Tim Mạch", IsActive = true },
            new Department { Id = 20, HospitalId = 2, DepartmentCode = "DEP-DN", Name = "Nội Đà Nẵng", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 101, HospitalId = 1, DepartmentId = 10, RoomCode = "P101", Name = "Phòng 101", RoomType = "EXAM", Status = "ACTIVE" },
            new HospitalRoom { Id = 102, HospitalId = 1, DepartmentId = 11, RoomCode = "P102", Name = "Phòng 102", RoomType = "EXAM", Status = "ACTIVE" },
            new HospitalRoom { Id = 201, HospitalId = 2, DepartmentId = 20, RoomCode = "P201", Name = "Phòng 201", RoomType = "EXAM", Status = "ACTIVE" });

        db.Staff.Add(new Staff
        {
            Id = 1,
            UserId = 11,
            HospitalId = 1,
            DepartmentId = 10,
            StaffCode = "STF-001",
            FullName = "Quản trị CS1",
            Status = "ACTIVE",
        });

        db.Services.AddRange(
            new Service { Id = 1001, HospitalId = 1, DepartmentId = 10, ServiceCode = "SRV-GI", Name = "Khám Nội Tiêu Hóa", IsActive = true },
            new Service { Id = 1002, HospitalId = 1, DepartmentId = 11, ServiceCode = "SRV-CAR", Name = "Khám Tim Mạch", IsActive = true });

        db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 5001,
                ServiceId = 1001,
                RoomId = 101,
                StartsAt = new DateTime(2026, 11, 24, 1, 0, 0, DateTimeKind.Utc), // 08:00 VN
                EndsAt = new DateTime(2026, 11, 24, 1, 30, 0, DateTimeKind.Utc),
                Capacity = 5,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 5002,
                ServiceId = 1002,
                RoomId = 102,
                StartsAt = new DateTime(2026, 11, 24, 2, 0, 0, DateTimeKind.Utc), // 09:00 VN
                EndsAt = new DateTime(2026, 11, 24, 2, 30, 0, DateTimeKind.Utc),
                Capacity = 5,
                Status = "OPEN",
            });

        db.Appointments.AddRange(
            new Appointment { Id = 7001, SlotId = 5001, Status = "CHECKED_IN", BookedAt = new DateTime(2026, 11, 23, 8, 0, 0, DateTimeKind.Utc) },
            new Appointment { Id = 7002, SlotId = 5002, Status = "NO_SHOW", BookedAt = new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc) });

        db.Visits.AddRange(
            // Scheduled completed visit in Hospital 1 Dept 10
            new Visit
            {
                Id = 9001,
                VisitCode = "VIS-9001",
                AppointmentId = 7001,
                ExaminationRoomId = 101,
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 11, 24, 1, 0, 0, DateTimeKind.Utc),
                CompletedAt = new DateTime(2026, 11, 24, 1, 25, 0, DateTimeKind.Utc),
            },
            // Walk-in in-progress visit in Hospital 1 Dept 11
            new Visit
            {
                Id = 9002,
                VisitCode = "VIS-9002",
                AppointmentId = null,
                ExaminationRoomId = 102,
                Status = "IN_PROGRESS",
                CheckedInAt = new DateTime(2026, 11, 24, 2, 0, 0, DateTimeKind.Utc),
            },
            // Hospital 2 visit — must NEVER leak into Hospital 1 admin report
            new Visit
            {
                Id = 9099,
                VisitCode = "VIS-9099",
                AppointmentId = null,
                ExaminationRoomId = 201,
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 11, 24, 1, 30, 0, DateTimeKind.Utc),
            });

        db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 1,
                VisitId = 9001,
                ArrivedAt = new DateTime(2026, 11, 24, 1, 0, 0, DateTimeKind.Utc),
                CalledAt = new DateTime(2026, 11, 24, 1, 10, 0, DateTimeKind.Utc), // 10 min wait
            },
            new QueueEntry
            {
                Id = 2,
                VisitId = 9002,
                ArrivedAt = new DateTime(2026, 11, 24, 2, 0, 0, DateTimeKind.Utc),
                StartedAt = new DateTime(2026, 11, 24, 2, 20, 0, DateTimeKind.Utc), // 20 min wait
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetOperationalReport_HospitalAdmin_AggregatesOwnHospitalOnly_WithExactWaitTimes()
    {
        var result = await _service.GetOperationalReportAsync(
            11,
            new GetOperationalReportRequest(
                FromDate: new DateOnly(2026, 11, 24),
                ToDate: new DateOnly(2026, 11, 24)));

        Assert.Equal(1L, result.HospitalId);
        Assert.Equal(2, result.Summary.TotalVisits);
        Assert.Equal(1, result.Summary.CompletedVisits);
        Assert.Equal(1, result.Summary.WalkInVisits);
        Assert.Equal(2, result.Summary.TotalAppointments);
        Assert.Equal(1, result.Summary.NoShowAppointments);
        Assert.Equal(15.0, result.Summary.AverageWaitMinutes);
        Assert.Equal(15.0, result.Summary.MedianWaitMinutes);
        Assert.Equal(2, result.DepartmentRows.Count);
        Assert.DoesNotContain(result.DepartmentRows, r => r.DepartmentId == 20);
    }

    [Fact]
    public async Task GetOperationalReport_FilterByDepartment_ReturnsScopedDepartmentMetrics()
    {
        var result = await _service.GetOperationalReportAsync(
            11,
            new GetOperationalReportRequest(
                FromDate: new DateOnly(2026, 11, 24),
                ToDate: new DateOnly(2026, 11, 24),
                DepartmentId: 10));

        Assert.Equal(1, result.Summary.TotalVisits);
        Assert.Equal(0, result.Summary.WalkInVisits);
        Assert.Equal(10.0, result.Summary.MedianWaitMinutes);
        Assert.Single(result.DepartmentRows);
        Assert.Equal("Nội Tiêu Hóa", result.DepartmentRows[0].DepartmentName);
    }

    [Fact]
    public async Task GetOperationalReport_EmptyDateWindow_ReturnsZeroCountsEmptyListsAndNullWaitTimes()
    {
        var result = await _service.GetOperationalReportAsync(
            11,
            new GetOperationalReportRequest(
                FromDate: new DateOnly(2026, 10, 1),
                ToDate: new DateOnly(2026, 10, 5)));

        Assert.Equal(0, result.Summary.TotalVisits);
        Assert.Equal(0, result.Summary.TotalAppointments);
        Assert.Null(result.Summary.AverageWaitMinutes);
        Assert.Null(result.Summary.MedianWaitMinutes);
        Assert.Empty(result.DailyVisits);
        Assert.Empty(result.HourlyIntake);
        Assert.Empty(result.DepartmentVisits);
        Assert.Empty(result.DepartmentRows);
    }

    [Fact]
    public async Task GetOperationalReport_VisitsWithoutCalledOrStartedQueue_ReturnsNullWaitTimeNotZero()
    {
        _db.Visits.Add(new Visit
        {
            Id = 9050,
            VisitCode = "VIS-9050",
            ExaminationRoomId = 101,
            Status = "CHECKED_IN",
            CheckedInAt = new DateTime(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc),
        });
        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 50,
            VisitId = 9050,
            ArrivedAt = new DateTime(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc),
            CalledAt = null,
            StartedAt = null,
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetOperationalReportAsync(
            11,
            new GetOperationalReportRequest(
                FromDate: new DateOnly(2026, 11, 20),
                ToDate: new DateOnly(2026, 11, 20)));

        Assert.Equal(1, result.Summary.TotalVisits);
        Assert.Null(result.Summary.AverageWaitMinutes);
        Assert.Null(result.Summary.MedianWaitMinutes);
    }

    [Fact]
    public async Task GetOperationalReport_HospitalAdminRequestingOtherHospital_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOperationalReportAsync(
                11,
                new GetOperationalReportRequest(HospitalId: 2)));
    }

    [Fact]
    public async Task GetOperationalReport_NonAdminRole_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOperationalReportAsync(
                13,
                new GetOperationalReportRequest()));
    }

    [Fact]
    public async Task GetOperationalReport_FromDateAfterToDate_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOperationalReportAsync(
                11,
                new GetOperationalReportRequest(
                    FromDate: new DateOnly(2026, 11, 25),
                    ToDate: new DateOnly(2026, 11, 24))));
    }

    [Fact]
    public async Task GetOperationalReport_EmptyRolePermissions_FailsClosedForHospitalAndSystemAdmin()
    {
        _db.RolePermissions.RemoveRange(_db.RolePermissions);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOperationalReportAsync(11, new GetOperationalReportRequest()));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOperationalReportAsync(12, new GetOperationalReportRequest()));
    }

    [Fact]
    public async Task GetOperationalReport_UnspecifiedLocalTimestamps_StaysOnSameHospitalDateWithoutDoubleOffset()
    {
        var eveningDay = new DateOnly(2026, 11, 18);
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 5090,
            ServiceId = 1001,
            RoomId = 101,
            StartsAt = new DateTime(2026, 11, 18, 18, 0, 0, DateTimeKind.Unspecified),
            EndsAt = new DateTime(2026, 11, 18, 18, 30, 0, DateTimeKind.Unspecified),
            Capacity = 5,
            Status = "OPEN",
        });
        _db.Appointments.Add(new Appointment
        {
            Id = 7090,
            SlotId = 5090,
            Status = "CHECKED_IN",
            BookedAt = new DateTime(2026, 11, 18, 17, 0, 0, DateTimeKind.Unspecified),
        });
        _db.Visits.Add(new Visit
        {
            Id = 9090,
            VisitCode = "VIS-9090",
            AppointmentId = 7090,
            ExaminationRoomId = 101,
            Status = "COMPLETED",
            CheckedInAt = new DateTime(2026, 11, 18, 18, 30, 0, DateTimeKind.Unspecified),
            CompletedAt = new DateTime(2026, 11, 18, 18, 50, 0, DateTimeKind.Unspecified),
        });
        await _db.SaveChangesAsync();

        var sameDayReport = await _service.GetOperationalReportAsync(
            11,
            new GetOperationalReportRequest(FromDate: eveningDay, ToDate: eveningDay));

        Assert.Equal(1, sameDayReport.Summary.TotalVisits);
        Assert.Equal(1, sameDayReport.Summary.TotalAppointments);
        var hour18 = Assert.Single(sameDayReport.HourlyIntake, h => h.Hour == "18:00");
        Assert.Equal(1, hour18.Count);

        var nextDay = eveningDay.AddDays(1);
        var nextDayReport = await _service.GetOperationalReportAsync(
            11,
            new GetOperationalReportRequest(FromDate: nextDay, ToDate: nextDay));
        Assert.Equal(0, nextDayReport.Summary.TotalVisits);
        Assert.Equal(0, nextDayReport.Summary.TotalAppointments);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
