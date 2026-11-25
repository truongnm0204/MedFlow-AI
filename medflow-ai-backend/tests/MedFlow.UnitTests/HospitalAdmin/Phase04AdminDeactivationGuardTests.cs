using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.DTOs.ExaminationRooms;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.ExaminationRooms;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.HospitalAdmin;

/// <summary>
/// Unit tests for Phase 04 BUG-P4-05: Synchronized Admin Deactivation Locks &amp; Active Queue/Visit Guards
/// across DoctorAdminService, ExaminationRoomService, ExaminationServiceAdminService, and DiagnosticServiceAdminService.
/// </summary>
public sealed class Phase04AdminDeactivationGuardTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class AdminGuardTestDbContext(DbContextOptions<AdminGuardTestDbContext> options)
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
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<Payment> Payments => Set<Payment>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<DoctorServiceAuthorization>().HasKey(x => new { x.DoctorId, x.ServiceId });
            modelBuilder.Entity<PatientFamilyLink>(b =>
            {
                b.HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
                b.HasOne(l => l.OwnerPatient).WithMany().HasForeignKey(l => l.OwnerPatientId);
                b.HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
            });
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
            });
            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.User).WithOne(u => u.Staff).HasForeignKey<Staff>(s => s.UserId);
                b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId);
            });
            modelBuilder.Entity<Doctor>(b =>
            {
                b.HasKey(d => d.Id);
                b.HasOne(d => d.Staff).WithOne(s => s.Doctor).HasForeignKey<Doctor>(d => d.StaffId);
                b.HasOne(d => d.Specialty).WithMany().HasForeignKey(d => d.SpecialtyId);
            });
            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department).WithMany(d => d.HospitalRooms).HasForeignKey(r => r.DepartmentId);
            });
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany(d => d.Services).HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany(sp => sp.Services).HasForeignKey(s => s.SpecialtyId);
                b.HasMany(s => s.ServicePrices).WithOne(p => p.Service).HasForeignKey(p => p.ServiceId);
            });
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Doctor).WithMany(d => d.ExaminationSlots).HasForeignKey(s => s.DoctorId);
                b.HasOne(s => s.Room).WithMany().HasForeignKey(s => s.RoomId);
                b.HasOne(s => s.Service).WithMany().HasForeignKey(s => s.ServiceId);
            });
        }
    }

    // 2026-11-26T02:00:00Z = 2026-11-26 09:00:00 VN
    private static readonly DateTimeOffset FixedUtc = new(2026, 11, 26, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TodayVn = new(2026, 11, 26);

    private readonly AdminGuardTestDbContext _db;
    private readonly FixedTimeProvider _clock = new(FixedUtc);

    public Phase04AdminDeactivationGuardTests()
    {
        var opts = new DbContextOptionsBuilder<AdminGuardTestDbContext>()
            .UseInMemoryDatabase($"phase04-admin-guard-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AdminGuardTestDbContext(opts);
        Seed();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void Seed()
    {
        _db.Hospitals.Add(new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện MedFlow Hà Nội", IsActive = true });
        _db.Roles.Add(new Role { Id = 1, Code = "HOSPITAL_ADMIN", Name = "Hospital Admin" });
        _db.Permissions.Add(new Permission { Id = 1, Code = "HOSPITAL_CONFIG" });
        _db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });

        _db.Users.AddRange(
            new User { Id = 1, Email = "admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 2, Email = "doc1@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = 1, RoleId = 1 });

        _db.Departments.Add(new Department { Id = 1, HospitalId = 1, DepartmentCode = "KNT", Name = "Khoa Nội Tổng Hợp", IsActive = true });
        _db.Specialties.Add(new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Tiêu hóa", IsActive = true });

        _db.Staff.AddRange(
            new Staff { Id = 1, UserId = 1, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-ADM", FullName = "Quản trị CS1", Status = "ACTIVE" },
            new Staff { Id = 2, UserId = 2, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-DOC", FullName = "BS. Nguyễn Văn Khoa", Status = "ACTIVE" });

        _db.Doctors.Add(new Doctor
        {
            Id = 10,
            HospitalId = 1,
            StaffId = 2,
            SpecialtyId = 1,
            DoctorCode = "DOC-010",
            ProfessionalTitle = "BS.CKI",
            LicenseNumber = "CCHN-0010",
            IsActive = true,
        });

        _db.HospitalRooms.Add(new HospitalRoom
        {
            Id = 20,
            HospitalId = 1,
            DepartmentId = 1,
            RoomCode = "P.101",
            Name = "Phòng khám Nội 101",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
        });

        _db.Services.AddRange(
            new Service
            {
                Id = 30,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "EX-GI-30",
                Name = "Khám Nội Tiêu Hóa",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            },
            new Service
            {
                Id = 40,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "DIA-US-40",
                Name = "Siêu âm ổ bụng",
                ServiceType = "DIAGNOSTIC",
                AllowsOnlineBooking = false,
                IsActive = true,
            });

        _db.QueueServicePoints.AddRange(
            new QueueServicePoint
            {
                Id = 100,
                HospitalId = 1,
                Name = "Điểm khám P.101",
                PointType = "EXAMINATION",
                RoomId = 20,
                DoctorId = 10,
                ServiceId = 30,
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 101,
                HospitalId = 1,
                Name = "Điểm siêu âm",
                PointType = "DIAGNOSTIC",
                ServiceId = 40,
                IsActive = true,
            });

        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 500,
            DoctorId = 10,
            RoomId = 20,
            ServiceId = 30,
            StartsAt = new DateTime(2026, 11, 27, 8, 0, 0),
            EndsAt = new DateTime(2026, 11, 27, 8, 30, 0),
            Capacity = 4,
            Status = "OPEN",
        });

        _db.SaveChanges();
    }

    [Fact]
    public async Task DoctorPutAndDeactivate_BlockWhenActiveQueueOrReturnConsultationVisit_AndCloseSlotsAndServicePointsWhenClear()
    {
        var sut = new DoctorAdminService(_db, _clock);

        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 1,
            ServicePointId = 100,
            CurrentServicePointId = 100,
            QueueDate = TodayVn,
            Status = "WAITING",
        });
        await _db.SaveChangesAsync();

        // Both PUT (IsActive = false) and /deactivate must reject with 409 ConflictException
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.UpdateDoctorAsync(
                1,
                10,
                new UpdateDoctorAdminRequest(
                    FullName: "BS. Nguyễn Văn Khoa",
                    ProfessionalTitle: "BS.CKI",
                    LicenseNumber: "CCHN-0010",
                    DepartmentId: 1,
                    SpecialtyId: 1,
                    IsActive: false)));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.DeactivateDoctorAsync(1, "10", new DeactivateDoctorRequest(Reason: "Nghỉ phép", ConfirmUnderstood: true)));

        // Replace active queue with RETURN_CONSULTATION clinical visit -> still blocked!
        _db.QueueEntries.RemoveRange(_db.QueueEntries);
        _db.Visits.Add(new Visit
        {
            Id = 1,
            PatientId = 1,
            AssignedDoctorId = 10,
            Status = "RETURN_CONSULTATION",
            CheckedInAt = new DateTime(2026, 11, 26, 8, 15, 0),
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.UpdateDoctorAsync(
                1,
                10,
                new UpdateDoctorAdminRequest(
                    FullName: "BS. Nguyễn Văn Khoa",
                    ProfessionalTitle: "BS.CKI",
                    LicenseNumber: "CCHN-0010",
                    DepartmentId: 1,
                    SpecialtyId: 1,
                    Status: "INACTIVE")));

        // Complete the visit -> PUT deactivation succeeds, closes future slot 500, and deactivates QueueServicePoint 100
        var visit = await _db.Visits.SingleAsync(v => v.Id == 1);
        visit.Status = "COMPLETED";
        await _db.SaveChangesAsync();

        var updated = await sut.UpdateDoctorAsync(
            1,
            10,
            new UpdateDoctorAdminRequest(
                FullName: "BS. Nguyễn Văn Khoa",
                ProfessionalTitle: "BS.CKI",
                LicenseNumber: "CCHN-0010",
                DepartmentId: 1,
                SpecialtyId: 1,
                IsActive: false));

        Assert.False(updated.IsActive);
        Assert.Equal("CLOSED", (await _db.ExaminationSlots.SingleAsync(s => s.Id == 500)).Status);
        Assert.False((await _db.QueueServicePoints.SingleAsync(sp => sp.Id == 100)).IsActive);
    }

    [Fact]
    public async Task RoomAndServicesPutAndDeactivate_BlockWhenActiveTodayQueueEntryExists()
    {
        var roomSut = new ExaminationRoomService(_db, _clock);
        var examServiceSut = new ExaminationServiceAdminService(_db, _clock);
        var diagServiceSut = new DiagnosticServiceAdminService(_db, _clock);

        _db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 10,
                ServicePointId = 100,
                CurrentServicePointId = 100,
                QueueDate = TodayVn,
                Status = "IN_PROGRESS",
            },
            new QueueEntry
            {
                Id = 11,
                ServicePointId = 101,
                CurrentServicePointId = 101,
                QueueDate = TodayVn,
                Status = "CALLED",
            });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            roomSut.UpdateExaminationRoomAsync(
                1,
                20,
                new UpdateExaminationRoomRequest(
                    RoomCode: "P.101",
                    Name: "Phòng khám Nội 101",
                    DepartmentId: 1,
                    RoomType: "EXAMINATION",
                    Location: "Tầng 1",
                    Status: "MAINTENANCE")));

        await Assert.ThrowsAsync<ConflictException>(() =>
            examServiceSut.UpdateExaminationServiceAsync(
                1,
                30,
                new UpdateExaminationServiceRequest(
                    Name: "Khám Nội Tiêu Hóa",
                    ServiceType: "EXAMINATION",
                    DepartmentId: 1,
                    SpecialtyId: 1,
                    IsActive: false)));

        await Assert.ThrowsAsync<ConflictException>(() =>
            diagServiceSut.DeactivateDiagnosticServiceAsync(
                1,
                40,
                new DeactivateDiagnosticServiceAdminRequest(Reason: "Bảo trì thiết bị cận lâm sàng"),
                "127.0.0.1",
                CancellationToken.None));
    }
}
