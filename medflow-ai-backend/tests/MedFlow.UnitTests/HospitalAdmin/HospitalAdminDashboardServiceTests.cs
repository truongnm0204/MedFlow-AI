using MedFlow.Application.DTOs.HospitalAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.HospitalAdmin;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.HospitalAdmin;

public sealed class HospitalAdminDashboardServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class HospitalAdminTestDbContext : DbContext, IApplicationDbContext
    {
        public HospitalAdminTestDbContext(DbContextOptions<HospitalAdminTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for HospitalAdminDashboardService unit tests.");

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
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
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
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<QueueEntry>();
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
            modelBuilder.Entity<Doctor>()
                .HasOne(d => d.Specialty)
                .WithMany()
                .HasForeignKey(d => d.SpecialtyId);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<ServicePrice>().HasKey(sp => sp.Id);
            modelBuilder.Entity<Service>()
                .HasMany(s => s.ServicePrices)
                .WithOne(sp => sp.Service)
                .HasForeignKey(sp => sp.ServiceId);
            modelBuilder.Entity<ExaminationSlot>().HasKey(es => es.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<HospitalPolicy>().HasKey(hp => hp.Id);
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 4, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly HospitalAdminTestDbContext _dbContext;
    private readonly HospitalAdminDashboardService _service;

    public HospitalAdminDashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<HospitalAdminTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"HospitalAdminDashboardTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new HospitalAdminTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new HospitalAdminDashboardService(_dbContext, timeProvider);
    }

    private static void SeedData(HospitalAdminTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "CS-HN-01", Name = "Cơ sở Hà Nội", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "CS-HCM-02", Name = "Cơ sở TP.HCM", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "CS-OFF-03", Name = "Cơ sở Ngừng Hoạt Động", IsActive = false });

        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DEP-001", Name = "Khoa Nội Tiêu hóa", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DEP-002", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 3, HospitalId = 1, DepartmentCode = "DEP-003", Name = "Khoa Bảo Trì", IsActive = false },
            new Department { Id = 4, HospitalId = 2, DepartmentCode = "DEP-201", Name = "Khoa Ngoại Cơ sở 2", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 101,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "PK-101",
                Name = "Phòng khám Nội Tiêu hóa 1",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 Khu A",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 102,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "PK-102",
                Name = "Phòng khám Tim mạch 1",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 Khu B",
                Capacity = 1,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 104,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-104",
                Name = "Phòng khám Tim mạch 2",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 Khu B",
                Capacity = 1,
                Status = "MAINTENANCE",
            },
            new HospitalRoom
            {
                Id = 201,
                HospitalId = 2,
                DepartmentId = 4,
                RoomCode = "PK-201",
                Name = "Phòng khám Ngoại Cơ sở 2",
                RoomType = "EXAMINATION",
                Location = "Tầng 2",
                Capacity = 2,
                Status = "ACTIVE",
            });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Nội Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 2, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "HOSPITAL_CONFIG" },
            new Permission { Id = 2, Code = "PAYMENT_VIEW" },
            new Permission { Id = 3, Code = "QUEUE_MANAGE" },
            new Permission { Id = 4, Code = "VISIT_ASSIGN" },
            new Permission { Id = 5, Code = "USER_MANAGE" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 1 },
            new RolePermission { RoleId = 1, PermissionId = 5 },
            new RolePermission { RoleId = 2, PermissionId = 1 },
            new RolePermission { RoleId = 2, PermissionId = 2 },
            new RolePermission { RoleId = 2, PermissionId = 3 },
            new RolePermission { RoleId = 2, PermissionId = 4 });

        db.Users.AddRange(
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "hospital_admin02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "system_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User
            {
                Id = 14,
                Email = "locked_hosp_admin@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = FixedNowUtc.AddMinutes(30),
            },
            new User { Id = 15, Email = "inactive_user_hosp_admin@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 16, Email = "inactive_staff_hosp_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 17, Email = "no_staff_hosp_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 18, Email = "no_hospital_hosp_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 19, Email = "inactive_hospital_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 21, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 11, RoleId = 2 },
            new UserRole { UserId = 12, RoleId = 2 },
            new UserRole { UserId = 13, RoleId = 1 },
            new UserRole { UserId = 14, RoleId = 2 },
            new UserRole { UserId = 15, RoleId = 2 },
            new UserRole { UserId = 16, RoleId = 2 },
            new UserRole { UserId = 17, RoleId = 2 },
            new UserRole { UserId = 18, RoleId = 2 },
            new UserRole { UserId = 19, RoleId = 2 },
            new UserRole { UserId = 21, RoleId = 3 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 104,
                UserId = 11,
                HospitalId = 1,
                StaffCode = "STF-010",
                FullName = "ThS. Nguyễn Văn Quản",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 105,
                UserId = 12,
                HospitalId = 2,
                StaffCode = "STF-020",
                FullName = "Quản trị viên Cơ sở 2",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 106,
                UserId = 13,
                HospitalId = 1,
                StaffCode = "STF-011",
                FullName = "Quản trị viên Hệ thống",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 107,
                UserId = 14,
                HospitalId = 1,
                StaffCode = "STF-014",
                FullName = "Quản trị viên Tạm khóa",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 108,
                UserId = 15,
                HospitalId = 1,
                StaffCode = "STF-015",
                FullName = "Quản trị viên Tài khoản khóa",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 109,
                UserId = 16,
                HospitalId = 1,
                StaffCode = "STF-016",
                FullName = "Quản trị viên Ngừng việc",
                Status = "INACTIVE",
            },
            new Staff
            {
                Id = 110,
                UserId = 18,
                HospitalId = null,
                StaffCode = "STF-018",
                FullName = "Quản trị viên Chưa gán viện",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 111,
                UserId = 19,
                HospitalId = 3,
                StaffCode = "STF-019",
                FullName = "Quản trị viên Viện ngừng hoạt động",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 201,
                UserId = 21,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-DOC-01",
                FullName = "Lê Hoàng Minh",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 202,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-DOC-02",
                FullName = "Trần Thị Mai",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 203,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-DOC-03",
                FullName = "Phạm Văn Tạm",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 204,
                HospitalId = 2,
                DepartmentId = 4,
                StaffCode = "STF-DOC-04",
                FullName = "Nguyễn Cơ Sở 2",
                Status = "ACTIVE",
            });

        db.Doctors.AddRange(
            new Doctor
            {
                Id = 1,
                StaffId = 201,
                HospitalId = 1,
                DoctorCode = "DOC-0142",
                SpecialtyId = 1,
                ProfessionalTitle = "BS. CKI",
                LicenseNumber = "0142/BYT-CCHN",
                IsActive = true,
            },
            new Doctor
            {
                Id = 2,
                StaffId = 202,
                HospitalId = 1,
                DoctorCode = "DOC-0143",
                SpecialtyId = 2,
                ProfessionalTitle = "ThS. BS",
                LicenseNumber = "0143/BYT-CCHN",
                IsActive = true,
            },
            new Doctor
            {
                Id = 3,
                StaffId = 203,
                HospitalId = 1,
                DoctorCode = "DOC-0144",
                SpecialtyId = 2,
                ProfessionalTitle = "BS",
                LicenseNumber = "0144/BYT-CCHN",
                IsActive = false,
            },
            new Doctor
            {
                Id = 4,
                StaffId = 204,
                HospitalId = 2,
                DoctorCode = "DOC-0201",
                SpecialtyId = 1,
                ProfessionalTitle = "BS. CKII",
                LicenseNumber = "0201/BYT-CCHN",
                IsActive = true,
            });

        db.Services.AddRange(
            new Service
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "EX-1-01",
                Name = "Khám Nội Tiêu hóa & Tư vấn",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            },
            new Service
            {
                Id = 2,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 2,
                ServiceCode = "EX-1-02",
                Name = "Khám Tim mạch Chuyên sâu",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            },
            new Service
            {
                Id = 3,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 2,
                ServiceCode = "EX-1-03",
                Name = "Đo Điện tâm đồ tại quầy",
                ServiceType = "DIAGNOSTIC",
                AllowsOnlineBooking = false,
                IsActive = true,
            },
            new Service
            {
                Id = 4,
                HospitalId = 2,
                DepartmentId = 4,
                SpecialtyId = 1,
                ServiceCode = "EX-2-01",
                Name = "Khám Ngoại Cơ sở 2",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            });

        db.ServicePrices.AddRange(
            new ServicePrice
            {
                Id = 1,
                ServiceId = 1,
                PriceAmount = 300000m,
                EffectiveFrom = FixedNowUtc.AddDays(-30),
            },
            new ServicePrice
            {
                Id = 2,
                ServiceId = 2,
                PriceAmount = 350000m,
                EffectiveFrom = FixedNowUtc.AddDays(-30),
            },
            new ServicePrice
            {
                Id = 3,
                ServiceId = 3,
                PriceAmount = 150000m,
                EffectiveFrom = FixedNowUtc.AddDays(-30),
            });

        var today = FixedNowUtc.Date;
        db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 1,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 101,
                StartsAt = today.AddHours(8),
                EndsAt = today.AddHours(8.5),
                Capacity = 5,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 9,
                ServiceId = 2,
                DoctorId = 2,
                RoomId = 102,
                StartsAt = today.AddHours(9),
                EndsAt = today.AddHours(9.5),
                Capacity = 2,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 10,
                ServiceId = 2,
                DoctorId = 2,
                RoomId = 102,
                StartsAt = today.AddHours(10),
                EndsAt = today.AddHours(10.5),
                Capacity = 4,
                Status = "CLOSED",
            },
            new ExaminationSlot
            {
                Id = 20,
                ServiceId = 4,
                DoctorId = 4,
                RoomId = 201,
                StartsAt = today.AddHours(8),
                EndsAt = today.AddHours(8.5),
                Capacity = 5,
                Status = "OPEN",
            });

        db.Appointments.AddRange(
            new Appointment { Id = 1001, SlotId = 1, Status = "CONFIRMED" },
            new Appointment { Id = 1002, SlotId = 9, Status = "CONFIRMED" },
            new Appointment { Id = 1003, SlotId = 9, Status = "BOOKED" },
            new Appointment { Id = 1004, SlotId = 1, Status = "CANCELLED" },
            new Appointment { Id = 2001, SlotId = 20, Status = "CONFIRMED" });

        db.Visits.AddRange(
            new Visit { Id = 5001, AppointmentId = 1001, Status = "COMPLETED" },
            new Visit { Id = 5002, AppointmentId = 1002, Status = "CHECKED_IN" },
            new Visit { Id = 6001, AppointmentId = 2001, Status = "COMPLETED" });

        db.HospitalPolicies.AddRange(
            new HospitalPolicy
            {
                Id = 1,
                HospitalId = 1,
                PolicyType = "APPOINTMENT",
                PolicyKey = "booking_window_days",
                PolicyValue = "{\"max_days_ahead\": 30}",
                PolicyVersion = 1,
                EffectiveFrom = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            new HospitalPolicy
            {
                Id = 2,
                HospitalId = 1,
                PolicyType = "APPOINTMENT",
                PolicyKey = "cancel_hours",
                PolicyValue = "{\"minimum_hours\": 4}",
                PolicyVersion = 1,
                EffectiveFrom = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetDashboardAsync_ValidHospitalAdmin_ReturnsScopedOverviewMetricsWorklistAndAlerts()
    {
        var result = await _service.GetDashboardAsync(11, new GetHospitalAdminDashboardRequest());

        Assert.NotNull(result);
        Assert.Equal("Cơ sở Hà Nội", result.FacilityName);
        Assert.Equal("CS-HN-01", result.FacilityCode);
        Assert.Equal(FixedNowUtc, result.GeneratedAtUtc);

        // Profile assertions
        Assert.Equal(11, result.AdminProfile.UserId);
        Assert.Equal("hospital_admin01@medflow.test", result.AdminProfile.Email);
        Assert.Equal("ThS. Nguyễn Văn Quản", result.AdminProfile.FullName);
        Assert.Equal("STF-010", result.AdminProfile.StaffCode);
        Assert.Equal(1, result.AdminProfile.HospitalId);
        Assert.Equal("CS-HN-01", result.AdminProfile.HospitalCode);
        Assert.Equal("Cơ sở Hà Nội", result.AdminProfile.HospitalName);
        Assert.Contains("HOSPITAL_ADMIN", result.AdminProfile.Roles);
        Assert.Equal(
            ["HOSPITAL_CONFIG", "PAYMENT_VIEW", "QUEUE_MANAGE", "VISIT_ASSIGN"],
            result.AdminProfile.Permissions);

        // Hospital 1 scoped stats (excludes Hospital 2 departments, rooms, doctors, services, slots)
        Assert.Equal(2, result.Stats.ActiveDepartments);
        Assert.Equal(3, result.Stats.TotalDepartments);
        Assert.Equal(2, result.Stats.ActiveRooms);
        Assert.Equal(3, result.Stats.TotalRooms);
        Assert.Equal(2, result.Stats.ActiveDoctors);
        Assert.Equal(2, result.Stats.OnlineServicesCount);
        Assert.Equal(2, result.Stats.OpenSlotsToday);
        Assert.Equal(2, result.Stats.ReviewSlotsCount); // Slot 9 (full 2/2) + Slot 10 (CLOSED)

        // Quick actions strictly UC47-UC56 (no System Admin UC57-UC60)
        Assert.Equal(6, result.QuickActions.Count);
        Assert.Equal(
            ["UC47", "UC48", "UC49", "UC50", "UC53", "UC56"],
            result.QuickActions.Select(a => a.UcCode).ToArray());

        // Worklist contains only Hospital 1 items + policy/report items
        Assert.NotEmpty(result.Worklist);
        Assert.DoesNotContain(result.Worklist, w => w.Code == "DEP-201" || w.Code == "PK-201" || w.Code == "DOC-0201" || w.Code == "EX-2-01");
        Assert.Contains(result.Worklist, w => w.Code == "POL-APT" && w.MetricLabel.Contains("30 ngày") && w.MetricLabel.Contains("4 giờ"));
        Assert.Contains(result.Worklist, w => w.Code == "RPT-OPS" && w.Status == "NEEDS_REVIEW");

        // Policy alerts include slot capacity warning, room maintenance warning, and active policy
        Assert.Equal(3, result.PolicyAlerts.Count);
        Assert.Contains(result.PolicyAlerts, a => a.Code == "SLT-009" && a.Severity == "WARNING");
        Assert.Contains(result.PolicyAlerts, a => a.Code == "RM-104" && a.Severity == "WARNING");
        Assert.Contains(result.PolicyAlerts, a => a.Code == "POL-APT" && a.Severity == "ACTIVE" && a.Title.Contains("30 ngày") && a.Title.Contains("4 giờ"));
    }

    [Fact]
    public async Task GetDashboardAsync_RestrictedPermissions_ReturnsOnlyPermittedQuickActions()
    {
        var configPermissions = _dbContext.RolePermissions
            .Where(rp => rp.RoleId == 2 && rp.PermissionId != 2) // Keep only PAYMENT_VIEW (id=2)
            .ToList();
        _dbContext.RolePermissions.RemoveRange(configPermissions);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(11, new GetHospitalAdminDashboardRequest());

        Assert.Equal(["PAYMENT_VIEW"], result.AdminProfile.Permissions);
        var singleAction = Assert.Single(result.QuickActions);
        Assert.Equal("view-operational-reports", singleAction.Id);
        Assert.Equal("UC56", singleAction.UcCode);
    }

    [Fact]
    public async Task GetDashboardAsync_CategoryStatusAndKeywordFilters_FiltersWorklistItems()
    {
        var maintenanceItems = await _service.GetDashboardAsync(
            11,
            new GetHospitalAdminDashboardRequest(Category: "DEPARTMENT_ROOM", Status: "MAINTENANCE"));

        Assert.All(maintenanceItems.Worklist, item =>
        {
            Assert.Equal("DEPARTMENT_ROOM", item.Category);
            Assert.Equal("MAINTENANCE", item.Status);
        });
        Assert.Contains(maintenanceItems.Worklist, item => item.Code == "RM-104");

        var keywordItems = await _service.GetDashboardAsync(
            11,
            new GetHospitalAdminDashboardRequest(Keyword: "Lê Hoàng Minh", WorklistLimit: 5));

        var docItem = Assert.Single(keywordItems.Worklist);
        Assert.Equal("DOC-0142", docItem.Code);
        Assert.Equal("BS. CKI Lê Hoàng Minh", docItem.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public async Task GetDashboardAsync_InvalidWorklistLimit_ThrowsValidationException(int invalidLimit)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(11, new GetHospitalAdminDashboardRequest(WorklistLimit: invalidLimit)));
    }

    [Fact]
    public async Task GetDashboardAsync_InvalidCategoryOrStatus_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(11, new GetHospitalAdminDashboardRequest(Category: "INVALID_CATEGORY")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(11, new GetHospitalAdminDashboardRequest(Status: "INVALID_STATUS")));
    }

    [Fact]
    public async Task GetDashboardAsync_UserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDashboardAsync(9999, new GetHospitalAdminDashboardRequest()));
    }

    [Theory]
    [InlineData(13)] // SYSTEM_ADMIN only
    [InlineData(21)] // DOCTOR only
    public async Task GetDashboardAsync_NonHospitalAdminUser_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(actorUserId, new GetHospitalAdminDashboardRequest()));
    }

    [Theory]
    [InlineData(14)] // Locked user
    [InlineData(15)] // Inactive user
    public async Task GetDashboardAsync_InactiveOrLockedUser_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(actorUserId, new GetHospitalAdminDashboardRequest()));
    }

    [Theory]
    [InlineData(16)] // Inactive staff profile
    [InlineData(17)] // Missing staff profile
    [InlineData(18)] // Staff without HospitalId
    [InlineData(19)] // Assigned hospital is inactive
    public async Task GetDashboardAsync_InvalidStaffOrHospitalAssignment_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(actorUserId, new GetHospitalAdminDashboardRequest()));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
