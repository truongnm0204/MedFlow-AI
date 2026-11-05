using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Diagnostic;

public sealed class DiagnosticDashboardServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DiagnosticTestDbContext : DbContext, IApplicationDbContext
    {
        public DiagnosticTestDbContext(DbContextOptions<DiagnosticTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DiagnosticDashboardService unit tests.");

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
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<ExaminationSlot>().HasKey(es => es.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 4, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly DiagnosticTestDbContext _dbContext;
    private readonly DiagnosticDashboardService _service;

    public DiagnosticDashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<DiagnosticTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DiagnosticDashboardTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new DiagnosticTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new DiagnosticDashboardService(_dbContext, timeProvider);
    }

    private static void SeedData(DiagnosticTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-DEMO-01", Name = "Bệnh viện Đa khoa Trung tâm MedFlow", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-HCM-02", Name = "Cơ sở TP.HCM", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "HOS-OFF-03", Name = "Cơ sở Ngừng Hoạt Động", IsActive = false });

        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DIA", Name = "Khoa Chẩn đoán Hình ảnh & Xét nghiệm", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DIA-OFF", Name = "Khoa CLS Tạm Ngừng", IsActive = false },
            new Department { Id = 3, HospitalId = 2, DepartmentCode = "DIA-2", Name = "Khoa CLS Cơ sở 2", IsActive = true });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "INT", Name = "Nội tổng quát", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true },
            new Specialty { Id = 4, SpecialtyCode = "IMG", Name = "Chẩn đoán hình ảnh", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 101,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-201",
                Name = "Phòng X-quang 201",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 102,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-202",
                Name = "Phòng Xét nghiệm 202",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2",
                Capacity = 3,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 201,
                HospitalId = 2,
                DepartmentId = 3,
                RoomCode = "DIA-301",
                Name = "Phòng CLS Cơ sở 2",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 3",
                Capacity = 2,
                Status = "ACTIVE",
            });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Nhân viên Cận lâm sàng" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "DIAGNOSTIC_RESULT_WRITE" },
            new Permission { Id = 2, Code = "QUEUE_MANAGE" },
            new Permission { Id = 3, Code = "DIAGNOSTIC_ORDER" },
            new Permission { Id = 4, Code = "HOSPITAL_CONFIG" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 4, PermissionId = 1 },
            new RolePermission { RoleId = 4, PermissionId = 2 });

        db.Users.AddRange(
            new User { Id = 7, Email = "diagnostic_staff01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 8, Email = "diagnostic_staff02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User
            {
                Id = 14,
                Email = "locked_diag@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = FixedNowUtc.AddMinutes(30),
            },
            new User { Id = 15, Email = "inactive_user_diag@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 16, Email = "inactive_staff_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 17, Email = "no_staff_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 18, Email = "no_hospital_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 19, Email = "inactive_hospital_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "inactive_dept_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 21, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 22, Email = "hosp_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 23, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 7, RoleId = 4 },
            new UserRole { UserId = 8, RoleId = 4 },
            new UserRole { UserId = 14, RoleId = 4 },
            new UserRole { UserId = 15, RoleId = 4 },
            new UserRole { UserId = 16, RoleId = 4 },
            new UserRole { UserId = 17, RoleId = 4 },
            new UserRole { UserId = 18, RoleId = 4 },
            new UserRole { UserId = 19, RoleId = 4 },
            new UserRole { UserId = 20, RoleId = 4 },
            new UserRole { UserId = 21, RoleId = 2 },
            new UserRole { UserId = 22, RoleId = 6 },
            new UserRole { UserId = 23, RoleId = 1 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 7,
                UserId = 7,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-007",
                FullName = "KTV. Lê Minh Châu",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 8,
                UserId = 8,
                HospitalId = 2,
                DepartmentId = 3,
                StaffCode = "STF-008",
                FullName = "KTV. Cơ sở 2",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 14,
                UserId = 14,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-014",
                FullName = "KTV. Tạm khóa",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 15,
                UserId = 15,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-015",
                FullName = "KTV. Tài khoản ngừng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 16,
                UserId = 16,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-016",
                FullName = "KTV. Nhân sự ngừng",
                Status = "INACTIVE",
            },
            new Staff
            {
                Id = 18,
                UserId = 18,
                HospitalId = null,
                StaffCode = "STF-018",
                FullName = "KTV. Chưa gán viện",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 19,
                UserId = 19,
                HospitalId = 3,
                StaffCode = "STF-019",
                FullName = "KTV. Viện ngừng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 20,
                UserId = 20,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-020",
                FullName = "KTV. Khoa ngừng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 101,
                UserId = 21,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-DOC-01",
                FullName = "Trần Khánh",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 102,
                HospitalId = 2,
                DepartmentId = 3,
                StaffCode = "STF-DOC-02",
                FullName = "Bác sĩ Cơ sở 2",
                Status = "ACTIVE",
            });

        db.Doctors.AddRange(
            new Doctor
            {
                Id = 1,
                StaffId = 101,
                HospitalId = 1,
                DoctorCode = "DOC-001",
                SpecialtyId = 1,
                ProfessionalTitle = "BS. CKI",
                IsActive = true,
            },
            new Doctor
            {
                Id = 2,
                StaffId = 102,
                HospitalId = 2,
                DoctorCode = "DOC-002",
                SpecialtyId = 1,
                ProfessionalTitle = "BS.",
                IsActive = true,
            });

        db.Services.AddRange(
            new Service
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "SRV-CON-INT",
                Name = "Khám Nội tổng quát",
                ServiceType = "CONSULTATION",
                IsActive = true,
            },
            new Service
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "SRV-DIA-CBC",
                Name = "Xét nghiệm công thức máu (CBC)",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 6,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 2,
                ServiceCode = "SRV-DIA-ECG",
                Name = "Điện tâm đồ (ECG)",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 7,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 4,
                ServiceCode = "SRV-DIA-XRAY",
                Name = "Chụp X-quang ngực thẳng",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 8,
                HospitalId = 2,
                DepartmentId = 3,
                SpecialtyId = 4,
                ServiceCode = "SRV-DIA-HCM",
                Name = "Siêu âm Cơ sở 2",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            });

        db.Patients.AddRange(
            new Patient { Id = 1, PatientCode = "PAT-001", FullName = "Nguyễn Văn An", Status = "ACTIVE" },
            new Patient { Id = 2, PatientCode = "PAT-002", FullName = "Trần Thị Bích", Status = "ACTIVE" },
            new Patient { Id = 3, PatientCode = "PAT-003", FullName = "Lê Minh Cường", Status = "ACTIVE" },
            new Patient { Id = 4, PatientCode = "PAT-004", FullName = "Phạm Thu Dung", Status = "ACTIVE" },
            new Patient { Id = 5, PatientCode = "PAT-201", FullName = "Bệnh nhân Cơ sở 2", Status = "ACTIVE" });

        var today = FixedNowUtc.Date;
        db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 1,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 101,
                StartsAt = today.AddHours(8),
                EndsAt = today.AddHours(9),
                Capacity = 5,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 2,
                ServiceId = 8,
                DoctorId = 2,
                RoomId = 201,
                StartsAt = today.AddHours(8),
                EndsAt = today.AddHours(9),
                Capacity = 5,
                Status = "OPEN",
            });

        db.Appointments.AddRange(
            new Appointment
            {
                Id = 101,
                AppointmentCode = "APT-001",
                PatientId = 1,
                SlotId = 1,
                ReasonForVisit = "Đau ngực cấp cứu",
                Status = "CONFIRMED",
                BookedAt = today.AddHours(7.5),
            },
            new Appointment
            {
                Id = 102,
                AppointmentCode = "APT-002",
                PatientId = 2,
                SlotId = 1,
                ReasonForVisit = "Khám định kỳ",
                Status = "CONFIRMED",
                BookedAt = today.AddHours(7.6),
            },
            new Appointment
            {
                Id = 103,
                AppointmentCode = "APT-003",
                PatientId = 3,
                SlotId = 1,
                ReasonForVisit = "Kiểm tra hô hấp",
                Status = "CONFIRMED",
                BookedAt = today.AddHours(7.7),
            },
            new Appointment
            {
                Id = 104,
                AppointmentCode = "APT-004",
                PatientId = 4,
                SlotId = 1,
                ReasonForVisit = "Khám tổng quát",
                Status = "BOOKED",
                BookedAt = today.AddHours(8.2),
            },
            new Appointment
            {
                Id = 201,
                AppointmentCode = "APT-201",
                PatientId = 5,
                SlotId = 2,
                ReasonForVisit = "Khám Cơ sở 2",
                Status = "CONFIRMED",
                BookedAt = today.AddHours(8.0),
            });

        db.Visits.AddRange(
            new Visit
            {
                Id = 501,
                PatientId = 1,
                AppointmentId = 101,
                ReasonForVisit = "Đau ngực cấp cứu",
                Status = "AWAITING_DIAGNOSTIC",
                CheckedInAt = today.AddHours(8.0),
            },
            new Visit
            {
                Id = 502,
                PatientId = 2,
                AppointmentId = 102,
                ReasonForVisit = "Khám định kỳ",
                Status = "CHECKED_IN",
                CheckedInAt = today.AddHours(8.1),
            },
            new Visit
            {
                Id = 503,
                PatientId = 3,
                AppointmentId = 103,
                ReasonForVisit = "Kiểm tra hô hấp",
                Status = "COMPLETED",
                CheckedInAt = today.AddHours(8.2),
                CompletedAt = today.AddHours(8.8),
            },
            new Visit
            {
                Id = 601,
                PatientId = 5,
                AppointmentId = 201,
                ReasonForVisit = "Khám Cơ sở 2",
                Status = "IN_PROGRESS",
                CheckedInAt = today.AddHours(8.0),
            });

        db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 1,
                AppointmentId = 101,
                QueueNumber = 18,
                QueueType = "DIAGNOSTIC",
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetDashboardAsync_ValidDiagnosticStaff_ReturnsScopedStatsQueuePendingResultsAndQuickActions()
    {
        var result = await _service.GetDashboardAsync(7, new GetDiagnosticDashboardRequest());

        Assert.NotNull(result);
        Assert.Equal("Bệnh viện Đa khoa Trung tâm MedFlow", result.FacilityName);
        Assert.Equal("HOS-DEMO-01", result.FacilityCode);
        Assert.Equal("Khoa Chẩn đoán Hình ảnh & Xét nghiệm", result.DepartmentName);
        Assert.Equal(FixedNowUtc, result.GeneratedAtUtc);

        // Staff profile assertions
        Assert.Equal(7, result.StaffProfile.UserId);
        Assert.Equal("diagnostic_staff01@medflow.test", result.StaffProfile.Email);
        Assert.Equal("KTV. Lê Minh Châu", result.StaffProfile.FullName);
        Assert.Equal("STF-007", result.StaffProfile.StaffCode);
        Assert.Equal(1, result.StaffProfile.HospitalId);
        Assert.Equal("HOS-DEMO-01", result.StaffProfile.HospitalCode);
        Assert.Equal(1, result.StaffProfile.DepartmentId);
        Assert.Contains("DIAGNOSTIC_STAFF", result.StaffProfile.Roles);
        Assert.Equal(
            ["DIAGNOSTIC_RESULT_WRITE", "QUEUE_MANAGE"],
            result.StaffProfile.Permissions);

        // Hospital 1 scoped stats (4 encounters: 1 ORDERED, 1 CONFIRMED, 1 IN_PROGRESS, 1 COMPLETED)
        Assert.Equal(1, result.Stats.PendingOrders);
        Assert.Equal(1, result.Stats.InQueue);
        Assert.Equal(1, result.Stats.InExecution);
        Assert.Equal(1, result.Stats.CompletedToday);

        // Queue excludes Hospital 2 data
        Assert.Equal(4, result.Queue.Count);
        Assert.DoesNotContain(result.Queue, q => q.PatientCode == "PAT-201" || q.Room == "DIA-301");
        Assert.Contains(result.Queue, q => q.PatientName == "Nguyễn Văn An" && q.TicketNo == "C018" && q.Status == "IN_PROGRESS" && q.Priority == "STAT" && q.Category == "LABORATORY");
        Assert.Contains(result.Queue, q => q.PatientName == "Trần Thị Bích" && q.Status == "CONFIRMED" && q.Category == "FUNCTIONAL");
        Assert.Contains(result.Queue, q => q.PatientName == "Lê Minh Cường" && q.Status == "COMPLETED" && q.Category == "IMAGING");
        Assert.Contains(result.Queue, q => q.PatientName == "Phạm Thu Dung" && q.Status == "ORDERED");

        // Pending results include IN_PROGRESS (PENDING_VERIFY), CONFIRMED (DRAFT), COMPLETED (FINALIZED)
        Assert.Equal(3, result.PendingResults.Count);
        Assert.Contains(result.PendingResults, r => r.PatientName == "Nguyễn Văn An" && r.Status == "PENDING_VERIFY");
        Assert.Contains(result.PendingResults, r => r.PatientName == "Trần Thị Bích" && r.Status == "DRAFT");
        Assert.Contains(result.PendingResults, r => r.PatientName == "Lê Minh Cường" && r.Status == "FINALIZED");

        // All 4 UC39-UC43 quick actions permitted when both QUEUE_MANAGE and DIAGNOSTIC_RESULT_WRITE are granted
        Assert.Equal(4, result.QuickActions.Count);
        Assert.Equal(
            ["UC39", "UC40", "UC42", "UC43"],
            result.QuickActions.Select(a => a.UcCode).ToArray());
    }

    [Fact]
    public async Task GetDashboardAsync_RestrictedPermissions_ReturnsOnlyPermittedQuickActions()
    {
        // Keep only QUEUE_MANAGE (PermissionId = 2), remove DIAGNOSTIC_RESULT_WRITE (PermissionId = 1)
        var writePerm = _dbContext.RolePermissions
            .Where(rp => rp.RoleId == 4 && rp.PermissionId == 1)
            .ToList();
        _dbContext.RolePermissions.RemoveRange(writePerm);
        await _dbContext.SaveChangesAsync();

        var queueOnlyResult = await _service.GetDashboardAsync(7, new GetDiagnosticDashboardRequest());

        Assert.Equal(["QUEUE_MANAGE"], queueOnlyResult.StaffProfile.Permissions);
        Assert.Equal(
            ["UC39", "UC40", "UC42"],
            queueOnlyResult.QuickActions.Select(a => a.UcCode).ToArray());
        Assert.DoesNotContain(queueOnlyResult.QuickActions, a => a.Id == "enter-result");

        // Switch to DIAGNOSTIC_RESULT_WRITE only (PermissionId = 1)
        var queuePerm = _dbContext.RolePermissions
            .Where(rp => rp.RoleId == 4 && rp.PermissionId == 2)
            .ToList();
        _dbContext.RolePermissions.RemoveRange(queuePerm);
        _dbContext.RolePermissions.Add(new RolePermission { RoleId = 4, PermissionId = 1 });
        await _dbContext.SaveChangesAsync();

        var writeOnlyResult = await _service.GetDashboardAsync(7, new GetDiagnosticDashboardRequest());

        Assert.Equal(["DIAGNOSTIC_RESULT_WRITE"], writeOnlyResult.StaffProfile.Permissions);
        Assert.Equal(
            ["UC39", "UC42", "UC43"],
            writeOnlyResult.QuickActions.Select(a => a.UcCode).ToArray());
        Assert.DoesNotContain(writeOnlyResult.QuickActions, a => a.Id == "receive-patient");
    }

    [Fact]
    public async Task GetDashboardAsync_CategoryStatusPriorityAndKeywordFilters_FiltersQueueItems()
    {
        var imagingItems = await _service.GetDashboardAsync(
            7,
            new GetDiagnosticDashboardRequest(Category: "IMAGING", Status: "COMPLETED"));

        var singleImaging = Assert.Single(imagingItems.Queue);
        Assert.Equal("IMAGING", singleImaging.Category);
        Assert.Equal("COMPLETED", singleImaging.Status);
        Assert.Equal("Lê Minh Cường", singleImaging.PatientName);

        var statPriorityItems = await _service.GetDashboardAsync(
            7,
            new GetDiagnosticDashboardRequest(Priority: "STAT", Keyword: "Nguyễn Văn An"));

        var singleStat = Assert.Single(statPriorityItems.Queue);
        Assert.Equal("STAT", singleStat.Priority);
        Assert.Equal("PAT-001", singleStat.PatientCode);

        var limitedResult = await _service.GetDashboardAsync(
            7,
            new GetDiagnosticDashboardRequest(QueueLimit: 2, PendingResultLimit: 1));

        Assert.Equal(2, limitedResult.Queue.Count);
        Assert.Single(limitedResult.PendingResults);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(51, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -2)]
    [InlineData(10, 51)]
    public async Task GetDashboardAsync_InvalidLimits_ThrowsValidationException(int queueLimit, int pendingLimit)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(
                7,
                new GetDiagnosticDashboardRequest(QueueLimit: queueLimit, PendingResultLimit: pendingLimit)));
    }

    [Fact]
    public async Task GetDashboardAsync_InvalidCategoryStatusOrPriority_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(7, new GetDiagnosticDashboardRequest(Category: "INVALID_CATEGORY")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(7, new GetDiagnosticDashboardRequest(Status: "INVALID_STATUS")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(7, new GetDiagnosticDashboardRequest(Priority: "INVALID_PRIORITY")));
    }

    [Fact]
    public async Task GetDashboardAsync_UserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDashboardAsync(9999, new GetDiagnosticDashboardRequest()));
    }

    [Theory]
    [InlineData(21)] // DOCTOR
    [InlineData(22)] // HOSPITAL_ADMIN
    [InlineData(23)] // PATIENT
    public async Task GetDashboardAsync_NonDiagnosticStaffUser_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(actorUserId, new GetDiagnosticDashboardRequest()));
    }

    [Theory]
    [InlineData(14)] // Locked user
    [InlineData(15)] // Inactive user
    public async Task GetDashboardAsync_InactiveOrLockedUser_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(actorUserId, new GetDiagnosticDashboardRequest()));
    }

    [Theory]
    [InlineData(16)] // Inactive staff profile
    [InlineData(17)] // Missing staff profile
    [InlineData(18)] // Staff without HospitalId
    [InlineData(19)] // Assigned hospital is inactive
    [InlineData(20)] // Assigned department is inactive
    public async Task GetDashboardAsync_InvalidStaffHospitalOrDepartmentState_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(actorUserId, new GetDiagnosticDashboardRequest()));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
