using MedFlow.Application.DTOs.DoctorDashboard;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Doctors;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Doctors;

public sealed class DoctorDashboardServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DoctorDashboardTestDbContext : DbContext, IApplicationDbContext
    {
        public DoctorDashboardTestDbContext(DbContextOptions<DoctorDashboardTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DoctorDashboardService unit tests.");

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
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
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

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
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
            modelBuilder.Entity<Doctor>()
                .HasOne(d => d.Specialty)
                .WithMany()
                .HasForeignKey(d => d.SpecialtyId);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<ExaminationSlot>().HasKey(es => es.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 4, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly DoctorDashboardTestDbContext _dbContext;
    private readonly DoctorDashboardService _service;

    public DoctorDashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<DoctorDashboardTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DoctorDashboardTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new DoctorDashboardTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new DoctorDashboardService(_dbContext, timeProvider);
    }

    private static void SeedData(DoctorDashboardTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện Trung ương MedFlow", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Cơ sở 2 TP.HCM", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "HOS-03", Name = "Cơ sở Tạm Ngừng", IsActive = false });

        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "GI", Name = "Khoa Tiêu hóa", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "CARD", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 3, HospitalId = 2, DepartmentCode = "GEN", Name = "Khoa Nội CS2", IsActive = true });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 101,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "RM-001",
                Name = "Khám Tiêu hóa 101",
                RoomType = "EXAMINATION",
                Location = "Tầng 1",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 102,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-003",
                Name = "Khám Tim mạch 201",
                RoomType = "EXAMINATION",
                Location = "Tầng 2",
                Capacity = 2,
                Status = "ACTIVE",
            });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Nhân viên tiếp đón" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 7, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "PATIENT_CHECKIN" },
            new Permission { Id = 5, Code = "QUEUE_MANAGE" },
            new Permission { Id = 6, Code = "MEDICAL_RECORD_WRITE" },
            new Permission { Id = 7, Code = "DIAGNOSTIC_ORDER" },
            new Permission { Id = 9, Code = "PRESCRIPTION_ISSUE" },
            new Permission { Id = 11, Code = "HOSPITAL_CONFIG" },
            new Permission { Id = 12, Code = "USER_MANAGE" },
            new Permission { Id = 14, Code = "PAYMENT_VIEW" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 2, PermissionId = 6 },
            new RolePermission { RoleId = 2, PermissionId = 7 },
            new RolePermission { RoleId = 2, PermissionId = 9 },
            new RolePermission { RoleId = 2, PermissionId = 14 });

        db.Users.AddRange(
            new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 4, Email = "doctor02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 10, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User
            {
                Id = 14,
                Email = "locked.doctor@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = FixedNowUtc.AddMinutes(15),
            },
            new User { Id = 15, Email = "inactive.doctor@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 16, Email = "inactive.staff.doc@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 17, Email = "missing.staff.doc@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 18, Email = "missing.doctor.profile@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 19, Email = "inactive.doctor.profile@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "inactive.hospital.doc@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 4, RoleId = 2 },
            new UserRole { UserId = 10, RoleId = 1 },
            new UserRole { UserId = 11, RoleId = 6 },
            new UserRole { UserId = 14, RoleId = 2 },
            new UserRole { UserId = 15, RoleId = 2 },
            new UserRole { UserId = 16, RoleId = 2 },
            new UserRole { UserId = 17, RoleId = 2 },
            new UserRole { UserId = 18, RoleId = 2 },
            new UserRole { UserId = 19, RoleId = 2 },
            new UserRole { UserId = 20, RoleId = 2 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 1,
                UserId = 3,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-001",
                FullName = "Nguyễn Minh Đức",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 2,
                UserId = 4,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-002",
                FullName = "Trần Thu Hà",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 16,
                UserId = 16,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-016",
                FullName = "BS Ngừng Nhân Sự",
                Status = "INACTIVE",
            },
            new Staff
            {
                Id = 18,
                UserId = 18,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-018",
                FullName = "Nhân sự chưa gán Doctor",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 19,
                UserId = 19,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-019",
                FullName = "BS Tạm Ngừng Khám",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 20,
                UserId = 20,
                HospitalId = 3,
                DepartmentId = 1,
                StaffCode = "STF-020",
                FullName = "BS Cơ Sở Ngừng",
                Status = "ACTIVE",
            });

        db.Doctors.AddRange(
            new Doctor
            {
                Id = 1,
                StaffId = 1,
                HospitalId = 1,
                DoctorCode = "DOC-001",
                SpecialtyId = 1,
                ProfessionalTitle = "BS.CKI",
                LicenseNumber = "CCHN-001",
                IsActive = true,
            },
            new Doctor
            {
                Id = 2,
                StaffId = 2,
                HospitalId = 1,
                DoctorCode = "DOC-002",
                SpecialtyId = 2,
                ProfessionalTitle = "ThS.BS",
                LicenseNumber = "CCHN-002",
                IsActive = true,
            },
            new Doctor
            {
                Id = 19,
                StaffId = 19,
                HospitalId = 1,
                DoctorCode = "DOC-019",
                SpecialtyId = 1,
                ProfessionalTitle = "BS",
                IsActive = false,
            },
            new Doctor
            {
                Id = 20,
                StaffId = 20,
                HospitalId = 3,
                DoctorCode = "DOC-020",
                SpecialtyId = 1,
                ProfessionalTitle = "BS",
                IsActive = true,
            });

        db.Services.AddRange(
            new Service
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "EX-GI-01",
                Name = "Khám Tiêu hóa lần đầu",
                ServiceType = "EXAMINATION",
                IsActive = true,
            },
            new Service
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "DIA-CBC",
                Name = "Công thức máu ngoại vi",
                ServiceType = "DIAGNOSTIC",
                IsActive = true,
            },
            new Service
            {
                Id = 6,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "DIA-US",
                Name = "Siêu âm ổ bụng tổng quát",
                ServiceType = "DIAGNOSTIC",
                IsActive = true,
            });

        db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 1,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 101,
                StartsAt = new DateTime(2026, 4, 20, 7, 30, 0),
                EndsAt = new DateTime(2026, 4, 20, 11, 30, 0),
                Capacity = 10,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 2,
                ServiceId = 1,
                DoctorId = 2,
                RoomId = 102,
                StartsAt = new DateTime(2026, 4, 20, 8, 0, 0),
                EndsAt = new DateTime(2026, 4, 20, 11, 30, 0),
                Capacity = 10,
                Status = "OPEN",
            });

        db.Patients.AddRange(
            new Patient
            {
                Id = 101,
                PatientCode = "PAT-0101",
                FullName = "Nguyễn Văn An",
                DateOfBirth = new DateOnly(1968, 5, 10),
                Gender = "MALE",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 102,
                PatientCode = "PAT-0102",
                FullName = "Trần Thị Mai",
                DateOfBirth = new DateOnly(1992, 8, 15),
                Gender = "FEMALE",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 103,
                PatientCode = "PAT-0103",
                FullName = "Lê Hoàng Nam",
                DateOfBirth = new DateOnly(1955, 2, 1),
                Gender = "MALE",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 104,
                PatientCode = "PAT-0104",
                FullName = "Võ Minh Tuấn",
                DateOfBirth = new DateOnly(1981, 11, 20),
                Gender = "MALE",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 105,
                PatientCode = "PAT-0105",
                FullName = "Đặng Thu Thảo",
                DateOfBirth = new DateOnly(1997, 3, 12),
                Gender = "FEMALE",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 201,
                PatientCode = "PAT-0201",
                FullName = "Bệnh nhân Bác sĩ 2",
                DateOfBirth = new DateOnly(1980, 1, 1),
                Gender = "MALE",
                Status = "ACTIVE",
            });

        db.Appointments.AddRange(
            new Appointment
            {
                Id = 501,
                AppointmentCode = "APT-501",
                PatientId = 101,
                SlotId = 1,
                ReasonForVisit = "Đau thượng vị cấp kèm khó thở",
                Status = "CHECKED_IN",
                BookedAt = new DateTime(2026, 4, 19, 10, 0, 0),
            },
            new Appointment
            {
                Id = 502,
                AppointmentCode = "APT-502",
                PatientId = 102,
                SlotId = 1,
                ReasonForVisit = "Ợ nóng kéo dài 2 tuần",
                Status = "CHECKED_IN",
                BookedAt = new DateTime(2026, 4, 19, 10, 5, 0),
            },
            new Appointment
            {
                Id = 599,
                AppointmentCode = "APT-599",
                PatientId = 201,
                SlotId = 2,
                ReasonForVisit = "Khám tim mạch Bác sĩ 2",
                Status = "CHECKED_IN",
                BookedAt = new DateTime(2026, 4, 19, 11, 0, 0),
            });

        db.Visits.AddRange(
            new Visit
            {
                Id = 701,
                VisitCode = "VIS-701",
                PatientId = 101,
                AppointmentId = 501,
                AssignedDoctorId = 1,
                ExaminationRoomId = 101,
                ReasonForVisit = "Đau thượng vị cấp kèm khó thở",
                Status = "IN_CONSULTATION",
                CheckedInAt = new DateTime(2026, 4, 20, 8, 10, 0),
            },
            new Visit
            {
                Id = 702,
                VisitCode = "VIS-702",
                PatientId = 102,
                AppointmentId = 502,
                AssignedDoctorId = 1,
                ExaminationRoomId = 101,
                ReasonForVisit = "Ợ nóng kéo dài 2 tuần",
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 4, 20, 8, 18, 0),
            },
            new Visit
            {
                Id = 703,
                VisitCode = "VIS-703",
                PatientId = 103,
                AssignedDoctorId = 1,
                ExaminationRoomId = 101,
                ReasonForVisit = "Quay lại đọc kết quả xét nghiệm máu",
                Status = "RETURN_CONSULTATION",
                CheckedInAt = new DateTime(2026, 4, 20, 8, 25, 0),
            },
            new Visit
            {
                Id = 704,
                VisitCode = "VIS-704",
                PatientId = 104,
                AssignedDoctorId = 1,
                ExaminationRoomId = 101,
                ReasonForVisit = "Đau bụng vùng hạ sườn phải",
                Status = "AWAITING_DIAGNOSTIC",
                CheckedInAt = new DateTime(2026, 4, 20, 8, 0, 0),
            },
            new Visit
            {
                Id = 705,
                VisitCode = "VIS-705",
                PatientId = 105,
                AssignedDoctorId = 1,
                ExaminationRoomId = 101,
                ReasonForVisit = "Tái khám viêm dạ dày ổn định",
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 4, 20, 7, 45, 0),
                CompletedAt = new DateTime(2026, 4, 20, 8, 5, 0),
                CompletedByDoctorId = 1,
            },
            new Visit
            {
                Id = 799,
                VisitCode = "VIS-799",
                PatientId = 201,
                AppointmentId = 599,
                AssignedDoctorId = 2,
                ExaminationRoomId = 102,
                ReasonForVisit = "Khám tim mạch Bác sĩ 2",
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 4, 20, 8, 30, 0),
            });

        db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 801,
                VisitId = 701,
                AppointmentId = 501,
                QueueDate = new DateOnly(2026, 4, 20),
                QueueNumber = 8,
                QueueType = "EXAMINATION",
                Status = "IN_CONSULTATION",
                ManualPriority = "URGENT",
                ArrivedAt = new DateTime(2026, 4, 20, 8, 10, 0),
                StartedAt = new DateTime(2026, 4, 20, 8, 15, 0),
            },
            new QueueEntry
            {
                Id = 802,
                VisitId = 702,
                AppointmentId = 502,
                QueueDate = new DateOnly(2026, 4, 20),
                QueueNumber = 9,
                QueueType = "EXAMINATION",
                Status = "CALLED",
                ManualPriority = "NORMAL",
                ArrivedAt = new DateTime(2026, 4, 20, 8, 18, 0),
                CalledAt = new DateTime(2026, 4, 20, 8, 22, 0),
            },
            new QueueEntry
            {
                Id = 803,
                VisitId = 703,
                QueueDate = new DateOnly(2026, 4, 20),
                QueueNumber = 5,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                ManualPriority = "NORMAL",
                ArrivedAt = new DateTime(2026, 4, 20, 8, 25, 0),
            },
            new QueueEntry
            {
                Id = 804,
                VisitId = 704,
                QueueDate = new DateOnly(2026, 4, 20),
                QueueNumber = 6,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                ManualPriority = "NORMAL",
                ArrivedAt = new DateTime(2026, 4, 20, 8, 0, 0),
            },
            new QueueEntry
            {
                Id = 805,
                VisitId = 705,
                QueueDate = new DateOnly(2026, 4, 20),
                QueueNumber = 4,
                QueueType = "EXAMINATION",
                Status = "COMPLETED",
                ManualPriority = "NORMAL",
                ArrivedAt = new DateTime(2026, 4, 20, 7, 45, 0),
                CompletedAt = new DateTime(2026, 4, 20, 8, 5, 0),
            },
            // Diagnostic queue entries for Doctor 1's visits
            new QueueEntry
            {
                Id = 901,
                VisitId = 703,
                ServicePointId = 1,
                QueueDate = new DateOnly(2026, 4, 20),
                QueueNumber = 1,
                QueueType = "DIAGNOSTIC",
                Status = "COMPLETED",
                ArrivedAt = new DateTime(2026, 4, 20, 8, 5, 0),
                CompletedAt = new DateTime(2026, 4, 20, 8, 20, 0),
            },
            new QueueEntry
            {
                Id = 902,
                VisitId = 704,
                ServicePointId = 2,
                QueueDate = new DateOnly(2026, 4, 20),
                QueueNumber = 2,
                QueueType = "DIAGNOSTIC",
                Status = "IN_PROGRESS",
                ArrivedAt = new DateTime(2026, 4, 20, 8, 20, 0),
                StartedAt = new DateTime(2026, 4, 20, 8, 25, 0),
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetDashboardAsync_ValidDoctor_ReturnsScopedProfileStatsQueueDiagnosticsAndQuickActions()
    {
        var result = await _service.GetDashboardAsync(3, new GetDoctorDashboardRequest());

        Assert.NotNull(result);
        Assert.Equal("Bệnh viện Trung ương MedFlow", result.FacilityName);
        Assert.Equal("HOS-01", result.FacilityCode);
        Assert.Equal("RM-001", result.RoomCode);
        Assert.Equal("Khám Tiêu hóa 101", result.RoomName);
        Assert.Equal("Khoa Tiêu hóa", result.DepartmentName);
        Assert.Equal("Ca sáng 07:30–11:30", result.ShiftLabel);
        Assert.Equal(FixedNowUtc, result.GeneratedAtUtc);

        // Doctor profile assertions
        Assert.Equal(3, result.DoctorProfile.UserId);
        Assert.Equal(1, result.DoctorProfile.DoctorId);
        Assert.Equal("DOC-001", result.DoctorProfile.DoctorCode);
        Assert.Equal("STF-001", result.DoctorProfile.StaffCode);
        Assert.Equal("doctor01@medflow.test", result.DoctorProfile.Email);
        Assert.Equal("BS.CKI Nguyễn Minh Đức", result.DoctorProfile.DisplayName);
        Assert.Equal("Tiêu hóa", result.DoctorProfile.SpecialtyName);
        Assert.Contains("DOCTOR", result.DoctorProfile.Roles);
        Assert.Equal(
            ["DIAGNOSTIC_ORDER", "MEDICAL_RECORD_WRITE", "PAYMENT_VIEW", "PRESCRIPTION_ISSUE"],
            result.DoctorProfile.Permissions);

        // Stats scoped strictly to Doctor 1 (excludes Doctor 2's Visit 799)
        Assert.Equal(2, result.Stats.AssignedWaiting); // Visit 702 (CALLED) + Visit 703 (WAITING)
        Assert.Equal(1, result.Stats.InConsultation); // Visit 701 (IN_CONSULTATION)
        Assert.Equal(2, result.Stats.PendingLabResults); // 2 diagnostic items (901 RESULT_READY, 902 IN_PROGRESS)
        Assert.Equal(1, result.Stats.CompletedToday); // Visit 705 (COMPLETED)

        // AssignedQueue contains only Doctor 1's 5 visits, never Doctor 2's patient
        Assert.Equal(5, result.AssignedQueue.Count);
        Assert.DoesNotContain(result.AssignedQueue, q => q.PatientCode == "PAT-0201");
        Assert.Contains(result.AssignedQueue, q => q.QueueNo == "B008" && q.Status == "IN_CONSULTATION" && q.Priority == "URGENT");
        Assert.Contains(result.AssignedQueue, q => q.QueueNo == "B009" && q.Status == "CALLED");
        Assert.Contains(result.AssignedQueue, q => q.QueueNo == "B005" && q.Status == "WAITING" && q.Priority == "RETURN_RESULT");
        Assert.Contains(result.AssignedQueue, q => q.QueueNo == "B006" && q.Status == "AWAITING_RESULTS");
        Assert.Contains(result.AssignedQueue, q => q.QueueNo == "B004" && q.Status == "COMPLETED");

        // Pending diagnostics
        Assert.Equal(2, result.PendingDiagnostics.Count);
        Assert.Contains(result.PendingDiagnostics, d => d.OrderCode == "CLS-0901" && d.Status == "RESULT_READY");
        Assert.Contains(result.PendingDiagnostics, d => d.OrderCode == "CLS-0902" && d.Status == "IN_PROGRESS");

        // Quick actions strictly Doctor UCs (UC30, UC33, UC34, UC37)
        Assert.Equal(4, result.QuickActions.Count);
        Assert.Equal(
            ["UC30", "UC33", "UC34", "UC37"],
            result.QuickActions.Select(a => a.UcCode).ToArray());
    }

    [Fact]
    public async Task GetDashboardAsync_RestrictedPermissions_ReturnsOnlyPermittedQuickActions()
    {
        var extraPerms = _dbContext.RolePermissions
            .Where(rp => rp.RoleId == 2 && rp.PermissionId != 6) // Keep only MEDICAL_RECORD_WRITE (id=6)
            .ToList();
        _dbContext.RolePermissions.RemoveRange(extraPerms);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(3, new GetDoctorDashboardRequest());

        Assert.Equal(["MEDICAL_RECORD_WRITE"], result.DoctorProfile.Permissions);
        Assert.Equal(2, result.QuickActions.Count);
        Assert.Equal(["call-next", "open-consultation"], result.QuickActions.Select(a => a.Id).ToArray());
    }

    [Fact]
    public async Task GetDashboardAsync_StatusPriorityAndKeywordFilters_FiltersAssignedQueueWhileKeepingOverallStats()
    {
        var awaitingResult = await _service.GetDashboardAsync(
            3,
            new GetDoctorDashboardRequest(Status: "AWAITING_RESULTS"));

        var singleAwaiting = Assert.Single(awaitingResult.AssignedQueue);
        Assert.Equal("Võ Minh Tuấn", singleAwaiting.PatientName);
        Assert.Equal(2, awaitingResult.Stats.AssignedWaiting);
        Assert.Equal(1, awaitingResult.Stats.InConsultation);

        var urgentResult = await _service.GetDashboardAsync(
            3,
            new GetDoctorDashboardRequest(Priority: "URGENT", Keyword: "Nguyễn Văn An"));

        var singleUrgent = Assert.Single(urgentResult.AssignedQueue);
        Assert.Equal("PAT-0101", singleUrgent.PatientCode);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(51, 10)]
    [InlineData(10, 0)]
    [InlineData(10, 51)]
    public async Task GetDashboardAsync_InvalidLimits_ThrowsValidationException(int queueLimit, int diagLimit)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(
                3,
                new GetDoctorDashboardRequest(QueueLimit: queueLimit, DiagnosticLimit: diagLimit)));
    }

    [Fact]
    public async Task GetDashboardAsync_InvalidStatusOrPriority_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(3, new GetDoctorDashboardRequest(Status: "INVALID_STATUS")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(3, new GetDoctorDashboardRequest(Priority: "INVALID_PRIORITY")));
    }

    [Fact]
    public async Task GetDashboardAsync_UserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDashboardAsync(9999, new GetDoctorDashboardRequest()));
    }

    [Theory]
    [InlineData(10)] // PATIENT only
    [InlineData(11)] // HOSPITAL_ADMIN only
    public async Task GetDashboardAsync_NonDoctorUser_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(actorUserId, new GetDoctorDashboardRequest()));
    }

    [Theory]
    [InlineData(14)] // Locked doctor user
    [InlineData(15)] // Inactive doctor user
    [InlineData(16)] // Inactive staff profile
    [InlineData(17)] // Missing staff profile
    [InlineData(18)] // Missing doctor profile
    [InlineData(19)] // Inactive doctor profile
    [InlineData(20)] // Inactive hospital
    public async Task GetDashboardAsync_InvalidAccountStaffDoctorOrHospitalState_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(actorUserId, new GetDoctorDashboardRequest()));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
