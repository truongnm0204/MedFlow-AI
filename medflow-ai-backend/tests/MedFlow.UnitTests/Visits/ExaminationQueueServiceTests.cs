using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Visits;

public sealed class ExaminationQueueServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ExaminationQueueTestDbContext : DbContext, IApplicationDbContext
    {
        private readonly bool _throwOnQueueServicePoints;

        public ExaminationQueueTestDbContext(
            DbContextOptions<ExaminationQueueTestDbContext> options,
            bool throwOnQueueServicePoints = false)
            : base(options)
        {
            _throwOnQueueServicePoints = throwOnQueueServicePoints;
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for ExaminationQueueService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => throw NotUsed();

        public DbSet<QueueServicePoint> QueueServicePoints =>
            _throwOnQueueServicePoints
                ? throw new NotSupportedException("QueueServicePoints DbSet is not configured in this test context.")
                : Set<QueueServicePoint>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department).WithMany(d => d.HospitalRooms).HasForeignKey(r => r.DepartmentId);
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
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Service).WithMany().HasForeignKey(s => s.ServiceId);
                b.HasOne(s => s.Doctor).WithMany(d => d.ExaminationSlots).HasForeignKey(s => s.DoctorId);
                b.HasOne(s => s.Room).WithMany().HasForeignKey(s => s.RoomId);
            });
            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId);
                b.HasOne(a => a.Slot).WithMany(s => s.Appointments).HasForeignKey(a => a.SlotId);
            });
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
        }
    }

    private readonly DbContextOptions<ExaminationQueueTestDbContext> _dbOptions;
    private readonly ExaminationQueueTestDbContext _db;
    private readonly FixedTimeProvider _timeProvider;
    private readonly ExaminationQueueService _service;

    public ExaminationQueueServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<ExaminationQueueTestDbContext>()
            .UseInMemoryDatabase($"ExaminationQueueServiceTests_{Guid.NewGuid():N}")
            .Options;

        _db = new ExaminationQueueTestDbContext(_dbOptions);
        // 2026-10-22 02:00 UTC == 2026-10-22 09:00 Vietnam time (morning shift)
        _timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 10, 22, 2, 0, 0, TimeSpan.Zero));
        _service = new ExaminationQueueService(_db, _timeProvider);

        SeedTestData();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedTestData()
    {
        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Nhân viên tiếp đón" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 7, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" });

        _db.Permissions.AddRange(
            new Permission { Id = 3, Code = "PATIENT_CHECKIN" },
            new Permission { Id = 4, Code = "VISIT_ASSIGN" },
            new Permission { Id = 5, Code = "QUEUE_MANAGE" },
            new Permission { Id = 10, Code = "UNRELATED_PERMISSION" });

        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 3, PermissionId = 3 },
            new RolePermission { RoleId = 3, PermissionId = 4 },
            new RolePermission { RoleId = 3, PermissionId = 5 },
            new RolePermission { RoleId = 6, PermissionId = 5 });

        _db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện MedFlow 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện MedFlow 2", IsActive = true });

        _db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "OPD", Name = "Khoa Khám bệnh", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "GI", Name = "Khoa Tiêu hóa", IsActive = true },
            new Department { Id = 3, HospitalId = 1, DepartmentCode = "CARD", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 4, HospitalId = 1, DepartmentCode = "DIA", Name = "Khoa Cận lâm sàng", IsActive = true },
            new Department { Id = 20, HospitalId = 2, DepartmentCode = "GI-2", Name = "Khoa Tiêu hóa BV2", IsActive = true });

        _db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true });

        _db.Users.AddRange(
            new User { Id = 1, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 5, Email = "reception01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "system_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 25, Email = "reception_hos2@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 5, RoleId = 3 },
            new UserRole { UserId = 11, RoleId = 6 },
            new UserRole { UserId = 13, RoleId = 7 },
            new UserRole { UserId = 25, RoleId = 3 });

        _db.Staff.AddRange(
            new Staff { Id = 1, UserId = 3, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-001", FullName = "BS Nguyễn Minh Đức", Status = "ACTIVE" },
            new Staff { Id = 2, UserId = null, HospitalId = 1, DepartmentId = 3, StaffCode = "STF-002", FullName = "BS Trần Thu Hà", Status = "ACTIVE" },
            new Staff { Id = 3, UserId = 5, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-003", FullName = "Lễ tân Lê Lan", Status = "ACTIVE" },
            new Staff { Id = 9, UserId = 11, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-009", FullName = "QLBV Phan Bình", Status = "ACTIVE" },
            new Staff { Id = 11, UserId = 13, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-011", FullName = "QTHT Lưu Huy", Status = "ACTIVE" },
            new Staff { Id = 20, UserId = 25, HospitalId = 2, DepartmentId = 20, StaffCode = "STF-020", FullName = "Lễ tân BV2", Status = "ACTIVE" });

        _db.Doctors.AddRange(
            new Doctor { Id = 1, StaffId = 1, HospitalId = 1, DoctorCode = "DOC-001", SpecialtyId = 1, ProfessionalTitle = "BS.CKI", IsActive = true },
            new Doctor { Id = 2, StaffId = 2, HospitalId = 1, DoctorCode = "DOC-002", SpecialtyId = 2, ProfessionalTitle = "ThS.BS", IsActive = true },
            new Doctor { Id = 20, StaffId = null, HospitalId = 2, DoctorCode = "DOC-020", SpecialtyId = 1, ProfessionalTitle = "BS", IsActive = true });

        _db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 1, HospitalId = 1, DepartmentId = 2, RoomCode = "RM-001", Name = "Khám Tiêu hóa 101", RoomType = "EXAMINATION", Location = "Tầng 1", Capacity = 2, Status = "ACTIVE" },
            new HospitalRoom { Id = 2, HospitalId = 1, DepartmentId = 2, RoomCode = "RM-002", Name = "Khám Tiêu hóa 102", RoomType = "EXAMINATION", Location = "Tầng 1", Capacity = 2, Status = "ACTIVE" },
            new HospitalRoom { Id = 3, HospitalId = 1, DepartmentId = 3, RoomCode = "RM-003", Name = "Khám Tim mạch 201", RoomType = "EXAMINATION", Location = "Tầng 2", Capacity = 2, Status = "ACTIVE" },
            new HospitalRoom { Id = 5, HospitalId = 1, DepartmentId = 4, RoomCode = "RM-005", Name = "Phòng Cận lâm sàng 205", RoomType = "DIAGNOSTIC", Location = "Tầng 2", Capacity = 1, Status = "ACTIVE" },
            new HospitalRoom { Id = 20, HospitalId = 2, DepartmentId = 20, RoomCode = "RM-201", Name = "Khám Tiêu hóa BV2", RoomType = "EXAMINATION", Location = "Tầng 1 BV2", Capacity = 2, Status = "ACTIVE" });

        _db.QueueServicePoints.AddRange(
            new QueueServicePoint { Id = 1, HospitalId = 1, RoomId = 1, DoctorId = 1, PointType = "DOCTOR", Name = "Hàng đợi bác sĩ 1", IsActive = true },
            new QueueServicePoint { Id = 2, HospitalId = 1, RoomId = 3, DoctorId = 2, PointType = "DOCTOR", Name = "Hàng đợi bác sĩ 2", IsActive = true },
            new QueueServicePoint { Id = 3, HospitalId = 1, RoomId = 5, DoctorId = null, PointType = "DIAGNOSTIC", Name = "Hàng đợi CLS", IsActive = true },
            new QueueServicePoint { Id = 20, HospitalId = 2, RoomId = 20, DoctorId = 20, PointType = "DOCTOR", Name = "Hàng đợi BV2", IsActive = true });

        _db.Services.AddRange(
            new Service { Id = 1, HospitalId = 1, DepartmentId = 2, SpecialtyId = 1, ServiceCode = "EX-GI-01", Name = "Khám Tiêu hóa lần đầu", ServiceType = "EXAMINATION", IsActive = true },
            new Service { Id = 2, HospitalId = 1, DepartmentId = 3, SpecialtyId = 2, ServiceCode = "EX-CARD-01", Name = "Khám Tim mạch lần đầu", ServiceType = "EXAMINATION", IsActive = true });

        _db.Patients.AddRange(
            new Patient { Id = 1, UserId = 1, PatientCode = "PAT-0001", FullName = "Nguyễn Văn An", DateOfBirth = new DateOnly(1990, 5, 10), Status = "ACTIVE" },
            new Patient { Id = 2, UserId = null, PatientCode = "PAT-0002", FullName = "Trần Thị Bình", DateOfBirth = new DateOnly(1952, 3, 15), Status = "ACTIVE" },
            new Patient { Id = 3, UserId = null, PatientCode = "PAT-0003", FullName = "Lê Hoàng Cường", DateOfBirth = new DateOnly(1988, 8, 20), Status = "ACTIVE" },
            new Patient { Id = 4, UserId = null, PatientCode = "PAT-0004", FullName = "Phạm Mai Dung", DateOfBirth = new DateOnly(1995, 1, 12), Status = "ACTIVE" },
            new Patient { Id = 5, UserId = null, PatientCode = "PAT-0005", FullName = "Hoàng Quốc Bảo", DateOfBirth = new DateOnly(1982, 11, 5), Status = "ACTIVE" },
            new Patient { Id = 20, UserId = null, PatientCode = "PAT-0020", FullName = "Bệnh nhân BV2", DateOfBirth = new DateOnly(1991, 4, 4), Status = "ACTIVE" });

        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 1,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 22, 8, 0, 0),
                EndsAt = new DateTime(2026, 10, 22, 9, 0, 0),
                Capacity = 5,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 2,
                ServiceId = 2,
                DoctorId = 2,
                RoomId = 3,
                StartsAt = new DateTime(2026, 10, 22, 8, 30, 0),
                EndsAt = new DateTime(2026, 10, 22, 9, 30, 0),
                Capacity = 5,
                Status = "OPEN",
            });

        _db.Appointments.AddRange(
            new Appointment { Id = 1, AppointmentCode = "APT-202610-0001", PatientId = 1, SlotId = 1, Status = "CHECKED_IN" },
            new Appointment { Id = 2, AppointmentCode = "APT-202610-0002", PatientId = 2, SlotId = 1, Status = "CHECKED_IN" },
            new Appointment { Id = 3, AppointmentCode = "APT-202610-0003", PatientId = 3, SlotId = 1, Status = "CHECKED_IN" },
            new Appointment { Id = 5, AppointmentCode = "APT-202610-0005", PatientId = 5, SlotId = 2, Status = "CHECKED_IN" });

        _db.Visits.AddRange(
            new Visit
            {
                Id = 1,
                VisitCode = "VIS-202610-0001",
                PatientId = 1,
                AppointmentId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 10, 22, 7, 50, 0),
            },
            new Visit
            {
                Id = 2,
                VisitCode = "VIS-202610-0002",
                PatientId = 2,
                AppointmentId = 2,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                Status = "IN_CONSULTATION",
                CheckedInAt = new DateTime(2026, 10, 22, 7, 55, 0),
            },
            new Visit
            {
                Id = 3,
                VisitCode = "VIS-202610-0003",
                PatientId = 3,
                AppointmentId = 3,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 10, 22, 8, 10, 0),
            },
            new Visit
            {
                Id = 4,
                VisitCode = "VIS-202610-0004",
                PatientId = 4,
                AppointmentId = null, // Walk-in
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 10, 22, 7, 30, 0),
                CompletedAt = new DateTime(2026, 10, 22, 7, 48, 0),
            },
            new Visit
            {
                Id = 5,
                VisitCode = "VIS-202610-0005",
                PatientId = 5,
                AppointmentId = 5,
                AssignedDoctorId = 2,
                ExaminationRoomId = 3,
                CheckedInByStaffId = 3,
                Status = "CALLED",
                CheckedInAt = new DateTime(2026, 10, 22, 8, 15, 0),
            },
            new Visit
            {
                Id = 20,
                VisitCode = "VIS-202610-0020",
                PatientId = 20,
                AppointmentId = null,
                AssignedDoctorId = 20,
                ExaminationRoomId = 20,
                CheckedInByStaffId = 20,
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 10, 22, 8, 0, 0),
            });

        _db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 1,
                VisitId = 1,
                AppointmentId = 1,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 2,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                ScheduledAt = new DateTime(2026, 10, 22, 8, 0, 0),
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 22, 7, 50, 0),
                ArrivalClass = "ON_TIME_OR_GRACE",
                ManualPriority = "NORMAL",
                ReadyAt = new DateTime(2026, 10, 22, 7, 51, 0),
            },
            new QueueEntry
            {
                Id = 2,
                VisitId = 2,
                AppointmentId = 2,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 3,
                QueueType = "EXAMINATION",
                Status = "IN_PROGRESS",
                ScheduledAt = new DateTime(2026, 10, 22, 8, 0, 0),
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 22, 7, 55, 0),
                ArrivalClass = "ON_TIME_OR_GRACE",
                ManualPriority = "ELDERLY",
                ReadyAt = new DateTime(2026, 10, 22, 7, 56, 0),
                CalledAt = new DateTime(2026, 10, 22, 8, 2, 0),
                StartedAt = new DateTime(2026, 10, 22, 8, 5, 0),
            },
            new QueueEntry
            {
                Id = 3,
                VisitId = 3,
                AppointmentId = 3,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 4,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                ScheduledAt = new DateTime(2026, 10, 22, 8, 0, 0),
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 22, 8, 10, 0),
                ArrivalClass = "LATE",
                ManualPriority = "URGENT",
                ReadyAt = new DateTime(2026, 10, 22, 8, 11, 0),
            },
            new QueueEntry
            {
                Id = 4,
                VisitId = 4,
                AppointmentId = null,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "COMPLETED",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 22, 7, 30, 0),
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StartedAt = new DateTime(2026, 10, 22, 7, 35, 0),
                CompletedAt = new DateTime(2026, 10, 22, 7, 48, 0),
            },
            new QueueEntry
            {
                Id = 5,
                VisitId = 5,
                AppointmentId = 5,
                ServicePointId = 2,
                CurrentServicePointId = 2,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "CALLED",
                ScheduledAt = new DateTime(2026, 10, 22, 8, 30, 0),
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 22, 8, 15, 0),
                ArrivalClass = "EARLY",
                ManualPriority = "RETURN_RESULT",
                CalledAt = new DateTime(2026, 10, 22, 8, 25, 0),
            },
            new QueueEntry
            {
                Id = 20,
                VisitId = 20,
                AppointmentId = null,
                ServicePointId = 20,
                CurrentServicePointId = 20,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 22, 8, 0, 0),
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetExaminationQueueAsync_GroupsByServicePoint_SortsByServingOrder_AndExcludesOtherHospitals()
    {
        var result = await _service.GetExaminationQueueAsync(actorUserId: 5);

        Assert.Equal("2026-10-22", result.QueueDate);
        Assert.Equal("Bệnh viện MedFlow 1", result.FacilityName);
        Assert.Equal("Ca sáng (07:00 - 11:30)", result.ShiftLabel);

        // Hospital 1 has 2 QueueServicePoints (sp-1 -> RM-001, sp-2 -> RM-003) + 1 unlinked EXAMINATION room (sp-room-2 -> RM-002).
        // Diagnostic room RM-005 (sp-3) and Hospital 2 room RM-201 (sp-20) must be excluded.
        Assert.Equal(3, result.ServicePoints.Count);
        Assert.DoesNotContain(result.ServicePoints, sp => sp.RoomCode == "RM-005");
        Assert.DoesNotContain(result.ServicePoints, sp => sp.RoomCode == "RM-201");

        // Overall stats across Hospital 1 entries (5 total: 2 WAITING, 2 CALLED/IN_PROGRESS, 1 COMPLETED, 0 SKIPPED)
        Assert.Equal(5, result.Stats.TotalEntries);
        Assert.Equal(2, result.Stats.WaitingCount);
        Assert.Equal(2, result.Stats.CalledOrServingCount);
        Assert.Equal(1, result.Stats.CompletedCount);
        Assert.Equal(0, result.Stats.SkippedCount);
        Assert.Equal(3, result.Stats.ActiveServicePoints);

        // Verify sp-1 (RM-001) serving order:
        // 1. IN_PROGRESS (QueueNumber 3 -> A003)
        // 2. WAITING + URGENT (QueueNumber 4 -> A004, even though LATE arrival)
        // 3. WAITING + NORMAL + ON_TIME_OR_GRACE (QueueNumber 2 -> A002)
        // 4. COMPLETED (QueueNumber 1 -> A001)
        var sp1 = Assert.Single(result.ServicePoints, sp => sp.ServicePointId == "sp-1");
        Assert.Equal("RM-001", sp1.RoomCode);
        Assert.Equal("BS.CKI BS Nguyễn Minh Đức", sp1.DoctorName);
        Assert.Equal("A003", sp1.CurrentServingTicketNo);
        Assert.Equal("A004", sp1.NextTicketNo);
        Assert.Equal(2, sp1.WaitingCount);
        Assert.Equal(1, sp1.InProgressCount);
        Assert.Equal(1, sp1.CompletedCount);
        Assert.Equal(0, sp1.SkippedCount);

        Assert.Equal(4, sp1.Entries.Count);
        Assert.Equal(["A003", "A004", "A002", "A001"], sp1.Entries.Select(e => e.TicketNo).ToArray());
        Assert.Equal([1, 2, 3, 4], sp1.Entries.Select(e => e.ServingOrder).ToArray());

        // Verify sp-2 (RM-003)
        var sp2 = Assert.Single(result.ServicePoints, sp => sp.ServicePointId == "sp-2");
        Assert.Equal("RM-003", sp2.RoomCode);
        Assert.Equal("B001", sp2.CurrentServingTicketNo);
        Assert.Null(sp2.NextTicketNo);
        Assert.Single(sp2.Entries);
        Assert.Equal("CALLED", sp2.Entries[0].Status);
        Assert.Equal("RETURN_RESULT", sp2.Entries[0].Priority);
    }

    [Fact]
    public async Task GetExaminationQueueAsync_WhenVisitTransferredToAnotherRoom_RoutesEntryToTargetRoomBucket()
    {
        // Transfer Visit 1 from RM-001 (id=1) to RM-002 (id=2)
        var visit1 = await _db.Visits.SingleAsync(v => v.Id == 1);
        visit1.ExaminationRoomId = 2;
        await _db.SaveChangesAsync();

        var result = await _service.GetExaminationQueueAsync(actorUserId: 5);

        var sp1 = Assert.Single(result.ServicePoints, sp => sp.RoomCode == "RM-001");
        Assert.DoesNotContain(sp1.Entries, e => e.VisitCode == "VIS-202610-0001");

        var spRoom2 = Assert.Single(result.ServicePoints, sp => sp.RoomCode == "RM-002");
        var movedEntry = Assert.Single(spRoom2.Entries);
        Assert.Equal("VIS-202610-0001", movedEntry.VisitCode);
        Assert.Equal("RM-002", movedEntry.RoomCode);
        Assert.Equal(1, movedEntry.ServingOrder);
        Assert.Equal(movedEntry.TicketNo, spRoom2.NextTicketNo);
    }

    [Fact]
    public async Task GetExaminationQueueAsync_AppliesServicePointStatusPriorityVisitTypeAndKeywordFilters()
    {
        var byServicePoint = await _service.GetExaminationQueueAsync(
            actorUserId: 5,
            new GetExaminationQueueRequest(ServicePointId: "sp-1"));
        var singleSp = Assert.Single(byServicePoint.ServicePoints);
        Assert.Equal("sp-1", singleSp.ServicePointId);
        Assert.Equal(4, byServicePoint.Stats.TotalEntries);

        var byRoomCode = await _service.GetExaminationQueueAsync(
            actorUserId: 5,
            new GetExaminationQueueRequest(ServicePointId: "RM-003"));
        var roomSp = Assert.Single(byRoomCode.ServicePoints);
        Assert.Equal("RM-003", roomSp.RoomCode);
        Assert.Equal(1, byRoomCode.Stats.TotalEntries);

        var byStatusAndPriority = await _service.GetExaminationQueueAsync(
            actorUserId: 5,
            new GetExaminationQueueRequest(Status: "WAITING", Priority: "URGENT"));
        var urgentSp = Assert.Single(byStatusAndPriority.ServicePoints);
        var urgentEntry = Assert.Single(urgentSp.Entries);
        Assert.Equal("VIS-202610-0003", urgentEntry.VisitCode);
        Assert.Equal(1, urgentEntry.ServingOrder);

        var byWalkIn = await _service.GetExaminationQueueAsync(
            actorUserId: 5,
            new GetExaminationQueueRequest(VisitType: "WALK_IN"));
        var walkInEntry = Assert.Single(byWalkIn.ServicePoints.SelectMany(sp => sp.Entries));
        Assert.Equal("VIS-202610-0004", walkInEntry.VisitCode);
        Assert.Equal("WALK_IN", walkInEntry.VisitType);

        var byKeyword = await _service.GetExaminationQueueAsync(
            actorUserId: 5,
            new GetExaminationQueueRequest(Keyword: "Hoàng Quốc Bảo"));
        var keywordEntry = Assert.Single(byKeyword.ServicePoints.SelectMany(sp => sp.Entries));
        Assert.Equal("PAT-0005", keywordEntry.PatientCode);
    }

    [Fact]
    public async Task GetExaminationQueueAsync_WhenQueueServicePointsNotSupported_FallsBackToExaminationRooms()
    {
        await using var fallbackDb = new ExaminationQueueTestDbContext(_dbOptions, throwOnQueueServicePoints: true);
        var fallbackService = new ExaminationQueueService(fallbackDb, _timeProvider);

        var result = await fallbackService.GetExaminationQueueAsync(actorUserId: 5);

        Assert.Equal(3, result.ServicePoints.Count);
        Assert.Contains(result.ServicePoints, sp => sp.RoomCode == "RM-001" && sp.Entries.Count == 4);
        Assert.Contains(result.ServicePoints, sp => sp.RoomCode == "RM-003" && sp.Entries.Count == 1);
    }

    [Theory]
    [InlineData("2026-99-99", null, null, null, null, null)]
    [InlineData("invalid-date", null, null, null, null, null)]
    [InlineData(null, "-1", null, null, null, null)]
    [InlineData(null, "0", null, null, null, null)]
    [InlineData(null, "<script>", null, null, null, null)]
    [InlineData(null, null, "UNKNOWN_STATUS", null, null, null)]
    [InlineData(null, null, null, "SUPER_VIP", null, null)]
    [InlineData(null, null, null, null, "EMERGENCY_TYPE", null)]
    [InlineData(null, null, null, null, null, "<img src=x>")]
    public async Task GetExaminationQueueAsync_InvalidFilterParameters_ThrowsValidationException(
        string? queueDate,
        string? servicePointId,
        string? status,
        string? priority,
        string? visitType,
        string? keyword)
    {
        var request = new GetExaminationQueueRequest(
            QueueDate: queueDate,
            ServicePointId: servicePointId,
            Status: status,
            Priority: priority,
            VisitType: visitType,
            Keyword: keyword);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetExaminationQueueAsync(actorUserId: 5, request));
    }

    [Fact]
    public async Task GetExaminationQueueAsync_KeywordExceedingMaxLength_ThrowsValidationException()
    {
        var longKeyword = new string('A', 101);
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetExaminationQueueAsync(
                actorUserId: 5,
                new GetExaminationQueueRequest(Keyword: longKeyword)));
    }

    [Fact]
    public async Task GetExaminationQueueAsync_EnforcesTwoLayerAuthorizationAndOwnershipChecks()
    {
        // 1. Invalid actorUserId -> 401 UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetExaminationQueueAsync(actorUserId: 0));

        // 2. Non-existent user -> 404 NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetExaminationQueueAsync(actorUserId: 9999));

        // 3. Disallowed role (PATIENT=1, DOCTOR=3) -> 403 ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationQueueAsync(actorUserId: 1));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationQueueAsync(actorUserId: 3));

        // 4. Locked user -> 403 ForbiddenException
        var receptionUser = await _db.Users.SingleAsync(u => u.Id == 5);
        receptionUser.LockedUntil = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(15);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationQueueAsync(actorUserId: 5));

        receptionUser.LockedUntil = null;
        await _db.SaveChangesAsync();

        // 5. Missing queue/reception permission -> 403 ForbiddenException
        var existingPerms = await _db.RolePermissions.Where(rp => rp.RoleId == 3).ToListAsync();
        _db.RolePermissions.RemoveRange(existingPerms);
        _db.RolePermissions.Add(new RolePermission { RoleId = 3, PermissionId = 10 });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationQueueAsync(actorUserId: 5));

        _db.RolePermissions.RemoveRange(_db.RolePermissions.Where(rp => rp.RoleId == 3));
        _db.RolePermissions.AddRange(existingPerms);
        await _db.SaveChangesAsync();

        // 6. Inactive hospital -> 403 ForbiddenException
        var hospital1 = await _db.Hospitals.SingleAsync(h => h.Id == 1);
        hospital1.IsActive = false;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationQueueAsync(actorUserId: 5));

        hospital1.IsActive = true;
        await _db.SaveChangesAsync();

        // 7. Hospital 2 reception staff sees only Hospital 2 queue
        var hos2Result = await _service.GetExaminationQueueAsync(actorUserId: 25);
        Assert.Equal("Bệnh viện MedFlow 2", hos2Result.FacilityName);
        var hos2Sp = Assert.Single(hos2Result.ServicePoints);
        Assert.Equal("RM-201", hos2Sp.RoomCode);
        Assert.Single(hos2Sp.Entries);
        Assert.Equal("VIS-202610-0020", hos2Sp.Entries[0].VisitCode);
    }
}
