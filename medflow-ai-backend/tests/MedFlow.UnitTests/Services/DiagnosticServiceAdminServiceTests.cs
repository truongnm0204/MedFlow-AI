using System.Text.Json;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Services;

public sealed class DiagnosticServiceAdminServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DiagnosticServiceAdminTestDbContext : DbContext, IApplicationDbContext
    {
        public DiagnosticServiceAdminTestDbContext(DbContextOptions<DiagnosticServiceAdminTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DiagnosticServiceAdminService unit tests.");

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
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();

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
            modelBuilder.Ignore<HospitalPolicy>();
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
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<Specialty>().HasKey(sp => sp.Id);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<Service>()
                .HasOne(s => s.Department)
                .WithMany()
                .HasForeignKey(s => s.DepartmentId);
            modelBuilder.Entity<Service>()
                .HasOne(s => s.Specialty)
                .WithMany()
                .HasForeignKey(s => s.SpecialtyId);
            modelBuilder.Entity<Service>()
                .HasMany(s => s.ServicePrices)
                .WithOne(sp => sp.Service)
                .HasForeignKey(sp => sp.ServiceId);
            modelBuilder.Entity<ServicePrice>().HasKey(sp => sp.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(qsp => qsp.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(o => o.Id);
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    private readonly DiagnosticServiceAdminTestDbContext _dbContext;
    private readonly DiagnosticServiceAdminService _service;

    public DiagnosticServiceAdminServiceTests()
    {
        var options = new DbContextOptionsBuilder<DiagnosticServiceAdminTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DiagnosticServiceAdminTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new DiagnosticServiceAdminTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new DiagnosticServiceAdminService(_dbContext, timeProvider);
    }

    private static void SeedData(DiagnosticServiceAdminTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-DEMO-01", Name = "Bệnh viện Đa khoa MedFlow Cơ sở 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-DEMO-02", Name = "Bệnh viện Đa khoa MedFlow Cơ sở 2", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "HOS-DEMO-03", Name = "Bệnh viện Ngừng Hoạt Động", IsActive = false });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "SYSTEM_ADMIN", Name = "System Admin" },
            new Role { Id = 2, Code = "HOSPITAL_ADMIN", Name = "Hospital Admin" },
            new Role { Id = 3, Code = "HOSPITAL_ADMIN", Name = "Hospital Admin thiếu HOSPITAL_CONFIG" });

        db.Permissions.Add(new Permission { Id = 11, Code = "HOSPITAL_CONFIG" });
        db.RolePermissions.Add(new RolePermission { RoleId = 2, PermissionId = 11 });

        db.Users.AddRange(
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "hospital_admin02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "sysadmin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 14, Email = "locked_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE, LockedUntil = FixedNowUtc.AddDays(1) },
            new User { Id = 15, Email = "inactive_user@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 16, Email = "inactive_staff@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 17, Email = "nostaff_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 18, Email = "nohospital_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 19, Email = "inactive_hosp_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "noconfig_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE });

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
            new UserRole { UserId = 20, RoleId = 3 });

        db.Staff.AddRange(
            new Staff { Id = 101, UserId = 11, HospitalId = 1, StaffCode = "STF-011", FullName = "Admin CS1", Status = "ACTIVE" },
            new Staff { Id = 102, UserId = 12, HospitalId = 2, StaffCode = "STF-012", FullName = "Admin CS2", Status = "ACTIVE" },
            new Staff { Id = 103, UserId = 13, HospitalId = 1, StaffCode = "STF-013", FullName = "SysAdmin", Status = "ACTIVE" },
            new Staff { Id = 104, UserId = 14, HospitalId = 1, StaffCode = "STF-014", FullName = "Locked Admin", Status = "ACTIVE" },
            new Staff { Id = 105, UserId = 15, HospitalId = 1, StaffCode = "STF-015", FullName = "Inactive User", Status = "ACTIVE" },
            new Staff { Id = 106, UserId = 16, HospitalId = 1, StaffCode = "STF-016", FullName = "Inactive Staff", Status = "INACTIVE" },
            new Staff { Id = 108, UserId = 18, HospitalId = null, StaffCode = "STF-018", FullName = "No Hospital", Status = "ACTIVE" },
            new Staff { Id = 109, UserId = 19, HospitalId = 3, StaffCode = "STF-019", FullName = "Inactive Hosp", Status = "ACTIVE" },
            new Staff { Id = 120, UserId = 20, HospitalId = 1, StaffCode = "STF-020", FullName = "Admin thiếu quyền cấu hình", Status = "ACTIVE" });

        db.Departments.AddRange(
            new Department { Id = 4, HospitalId = 1, DepartmentCode = "DIA", Name = "Khoa Cận lâm sàng", IsActive = true },
            new Department { Id = 7, HospitalId = 1, DepartmentCode = "IMG-DEP", Name = "Khoa Chẩn đoán hình ảnh", IsActive = true },
            new Department { Id = 8, HospitalId = 1, DepartmentCode = "OLD", Name = "Khoa đã ngừng", IsActive = false },
            new Department { Id = 9, HospitalId = 2, DepartmentCode = "DIA-H2", Name = "Khoa Cận lâm sàng Cơ sở 2", IsActive = true });

        db.Specialties.AddRange(
            new Specialty { Id = 3, SpecialtyCode = "LAB", Name = "Xét nghiệm", IsActive = true },
            new Specialty { Id = 4, SpecialtyCode = "IMG", Name = "Chẩn đoán hình ảnh", IsActive = true },
            new Specialty { Id = 5, SpecialtyCode = "OLD", Name = "Chuyên khoa đã ngừng", IsActive = false });

        db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 5, HospitalId = 1, DepartmentId = 4, RoomCode = "RM-005", Name = "Phòng Xét nghiệm 1", RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
            new HospitalRoom { Id = 6, HospitalId = 1, DepartmentId = 4, RoomCode = "RM-006", Name = "Phòng Xét nghiệm 2", RoomType = "DIAGNOSTIC", Status = "ACTIVE" });

        db.Services.AddRange(
            new Service
            {
                Id = 5,
                HospitalId = 1,
                ServiceCode = "DIA-CBC",
                Name = "Tổng phân tích tế bào máu",
                Description = "Xét nghiệm công thức máu ngoại vi",
                ServiceType = "DIAGNOSTIC",
                IsActive = true,
                RequiresResultVerification = true,
                DepartmentId = 4,
                SpecialtyId = 3
            },
            new Service
            {
                Id = 6,
                HospitalId = 1,
                ServiceCode = "DIA-US",
                Name = "Siêu âm ổ bụng tổng quát",
                Description = "Siêu âm gan mật tụy lách thận",
                ServiceType = "DIAGNOSTIC",
                IsActive = true,
                RequiresResultVerification = false,
                DepartmentId = 7,
                SpecialtyId = 4
            },
            new Service
            {
                Id = 7,
                HospitalId = 1,
                ServiceCode = "DIA-XRAY",
                Name = "Chụp X-quang ngực thẳng",
                Description = "X-quang tim phổi",
                ServiceType = "DIAGNOSTIC",
                IsActive = false,
                RequiresResultVerification = true,
                DepartmentId = 4,
                SpecialtyId = null
            },
            new Service
            {
                Id = 1,
                HospitalId = 1,
                ServiceCode = "EX-GI-01",
                Name = "Khám Nội tiêu hóa",
                ServiceType = "EXAMINATION",
                IsActive = true
            },
            new Service
            {
                Id = 20,
                HospitalId = 2,
                ServiceCode = "DIA-H2",
                Name = "Xét nghiệm sinh hóa Cơ sở 2",
                ServiceType = "DIAGNOSTIC",
                IsActive = true
            });

        db.ServicePrices.AddRange(
            new ServicePrice
            {
                Id = 1,
                ServiceId = 5,
                PriceAmount = 100000m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                EffectiveTo = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Unspecified)
            },
            new ServicePrice
            {
                Id = 2,
                ServiceId = 5,
                PriceAmount = 150000m,
                EffectiveFrom = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Unspecified),
                EffectiveTo = null
            });

        db.QueueServicePoints.AddRange(
            new QueueServicePoint
            {
                Id = 3,
                HospitalId = 1,
                RoomId = 5,
                ServiceId = 5,
                PointType = "DIAGNOSTIC",
                Name = "Điểm lấy mẫu máu 1",
                IsActive = true
            },
            new QueueServicePoint
            {
                Id = 4,
                HospitalId = 1,
                RoomId = 6,
                ServiceId = 5,
                PointType = "DIAGNOSTIC",
                Name = "Điểm lấy mẫu máu 2",
                IsActive = false
            },
            new QueueServicePoint
            {
                Id = 30,
                HospitalId = 1,
                RoomId = null,
                ServiceId = 5,
                PointType = "DOCTOR",
                Name = "Bàn bác sĩ",
                IsActive = true
            },
            new QueueServicePoint
            {
                Id = 9,
                HospitalId = 1,
                RoomId = null,
                ServiceId = null,
                PointType = "RECEPTION",
                Name = "Quầy tiếp đón",
                IsActive = true
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetDiagnosticServiceListAsync_ValidHospitalAdmin_ReturnsOnlyOwnHospitalDiagnosticServices()
    {
        var response = await _service.GetDiagnosticServiceListAsync(
            11,
            new GetDiagnosticServiceListAdminRequest());

        Assert.Equal(1, response.HospitalId);
        Assert.Equal("HOS-DEMO-01", response.HospitalCode);
        Assert.Equal(3, response.Total);
        Assert.Equal(new long[] { 5, 6, 7 }, response.Data.Select(d => d.Id).ToArray());

        Assert.Equal(3, response.Metrics.Total);
        Assert.Equal(2, response.Metrics.Active);
        Assert.Equal(1, response.Metrics.Inactive);
        Assert.Equal(2, response.Metrics.RequiresVerificationCount);
        Assert.Equal(2, response.Metrics.DepartmentsCount);
        Assert.Equal(1, response.Metrics.ActiveServicePointsCount);
    }

    [Fact]
    public async Task GetDiagnosticServiceListAsync_ActivePriceAndServicePoints_MappedCorrectly()
    {
        var response = await _service.GetDiagnosticServiceListAsync(
            11,
            new GetDiagnosticServiceListAdminRequest());

        var service5 = Assert.Single(response.Data, d => d.Id == 5);
        Assert.Equal(150000m, service5.Price);
        Assert.NotNull(service5.PriceFormatted);
        Assert.EndsWith(" đ", service5.PriceFormatted);
        Assert.Equal(2, service5.ServicePointCount);
        Assert.Equal(1, service5.ActiveServicePointCount);
        Assert.Equal(2, service5.ServicePoints.Count);
        Assert.Equal("RM-005", service5.ServicePoints[0].RoomCode);
        Assert.DoesNotContain(service5.ServicePoints, sp => sp.ServicePointId == 30);

        var service6 = Assert.Single(response.Data, d => d.Id == 6);
        Assert.Null(service6.Price);
        Assert.Null(service6.PriceFormatted);
        Assert.Empty(service6.ServicePoints);

        var service7 = Assert.Single(response.Data, d => d.Id == 7);
        Assert.Equal("INACTIVE", service7.Status);
        Assert.False(service7.IsActive);
    }

    public static TheoryData<GetDiagnosticServiceListAdminRequest, long[]> FilterTestCases => new()
    {
        { new GetDiagnosticServiceListAdminRequest(Keyword: "cbc"), [5] },
        { new GetDiagnosticServiceListAdminRequest(Keyword: "dia-"), [5, 6, 7] },
        { new GetDiagnosticServiceListAdminRequest(DepartmentId: 4), [5, 7] },
        { new GetDiagnosticServiceListAdminRequest(SpecialtyId: 4), [6] },
        { new GetDiagnosticServiceListAdminRequest(Status: "inactive"), [7] },
        { new GetDiagnosticServiceListAdminRequest(Status: "ALL"), [5, 6, 7] },
        { new GetDiagnosticServiceListAdminRequest(RequiresResultVerification: false), [6] }
    };

    [Theory]
    [MemberData(nameof(FilterTestCases))]
    public async Task GetDiagnosticServiceListAsync_Filters_NarrowDataButKeepMetrics(
        GetDiagnosticServiceListAdminRequest request,
        long[] expectedIds)
    {
        var response = await _service.GetDiagnosticServiceListAsync(11, request);

        Assert.Equal(expectedIds, response.Data.Select(d => d.Id).ToArray());
        Assert.Equal(expectedIds.Length, response.Total);
        Assert.Equal(3, response.Metrics.Total);
    }

    [Fact]
    public async Task GetDiagnosticServiceListAsync_Pagination_ReturnsRequestedSlice()
    {
        var page2 = await _service.GetDiagnosticServiceListAsync(
            11,
            new GetDiagnosticServiceListAdminRequest(Page: 2, PageSize: 1));

        Assert.Equal(new long[] { 6 }, page2.Data.Select(d => d.Id).ToArray());
        Assert.Equal(3, page2.Total);
        Assert.Equal(3, page2.TotalPages);

        var page5 = await _service.GetDiagnosticServiceListAsync(
            11,
            new GetDiagnosticServiceListAdminRequest(Page: 5, PageSize: 1));

        Assert.Empty(page5.Data);
        Assert.Equal(3, page5.Total);
        Assert.Equal(3, page5.TotalPages);
    }

    [Fact]
    public async Task GetDiagnosticServiceListAsync_OtherHospitalAdmin_SeesOnlyOwnHospital()
    {
        var response = await _service.GetDiagnosticServiceListAsync(
            12,
            new GetDiagnosticServiceListAdminRequest());

        Assert.Equal(2, response.HospitalId);
        Assert.Equal(new long[] { 20 }, response.Data.Select(d => d.Id).ToArray());
    }

    public static TheoryData<GetDiagnosticServiceListAdminRequest> InvalidRequests => new()
    {
        new GetDiagnosticServiceListAdminRequest(Page: 0),
        new GetDiagnosticServiceListAdminRequest(PageSize: 0),
        new GetDiagnosticServiceListAdminRequest(PageSize: 101),
        new GetDiagnosticServiceListAdminRequest(Status: "PAUSED"),
        new GetDiagnosticServiceListAdminRequest(Keyword: new string('a', 101)),
        new GetDiagnosticServiceListAdminRequest(DepartmentId: 0),
        new GetDiagnosticServiceListAdminRequest(SpecialtyId: -1)
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task GetDiagnosticServiceListAsync_InvalidRequest_ThrowsValidationException(
        GetDiagnosticServiceListAdminRequest request)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticServiceListAsync(11, request));
    }

    [Fact]
    public async Task GetDiagnosticServiceListAsync_UserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDiagnosticServiceListAsync(999, new GetDiagnosticServiceListAdminRequest()));
    }

    [Theory]
    [InlineData(13L)]
    [InlineData(14L)]
    [InlineData(15L)]
    [InlineData(16L)]
    [InlineData(17L)]
    [InlineData(18L)]
    [InlineData(19L)]
    public async Task GetDiagnosticServiceListAsync_NotPermittedOrInvalidAssignment_ThrowsForbiddenException(
        long userId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticServiceListAsync(userId, new GetDiagnosticServiceListAdminRequest()));
    }

    [Fact]
    public async Task CreateDiagnosticServiceAsync_ValidRequest_PersistsActiveServicePriceAndAudit()
    {
        var item = await _service.CreateDiagnosticServiceAsync(
            11,
            new CreateDiagnosticServiceAdminRequest(
                ServiceCode: "dia-new-01",
                Name: "  CT mới  ",
                Description: "   ",
                DepartmentId: 4,
                SpecialtyId: null,
                RequiresResultVerification: null,
                Price: 350000m),
            ipAddress: null);

        Assert.Equal("DIA-NEW-01", item.ServiceCode);
        Assert.Equal("CT mới", item.Name);
        Assert.Null(item.Description);
        Assert.Equal("ACTIVE", item.Status);
        Assert.True(item.IsActive);
        Assert.True(item.RequiresResultVerification);
        Assert.Equal(350000m, item.Price);
        Assert.Equal(4, item.DepartmentId);

        var prices = await _dbContext.ServicePrices.Where(p => p.ServiceId == item.Id).ToListAsync();
        Assert.Single(prices);
        Assert.Equal(350000m, prices[0].PriceAmount);

        var audit = Assert.Single(await _dbContext.AuditLogs.ToListAsync());
        Assert.Equal("DIAGNOSTIC_SERVICE_CREATED", audit.Action);
        Assert.Equal("SERVICE", audit.EntityType);
        Assert.Equal(item.Id.ToString(), audit.EntityId);
    }

    [Fact]
    public async Task CreateDiagnosticServiceAsync_DuplicateCodeIgnoringCase_ThrowsConflict()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateDiagnosticServiceAsync(
            11,
            new CreateDiagnosticServiceAdminRequest(
                ServiceCode: "dia-cbc",
                Name: "Trùng mã",
                DepartmentId: 4,
                Price: 10000m),
            ipAddress: null));

        Assert.Equal("Mã dịch vụ 'DIA-CBC' đã tồn tại trong cơ sở.", ex.Message);
    }

    [Fact]
    public async Task CreateDiagnosticServiceAsync_DepartmentOfOtherHospital_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateDiagnosticServiceAsync(
            11,
            new CreateDiagnosticServiceAdminRequest(
                ServiceCode: "DIA-OTHER",
                Name: "Khoa cơ sở khác",
                DepartmentId: 9),
            ipAddress: null));
    }

    [Fact]
    public async Task CreateDiagnosticServiceAsync_InactiveDepartment_ThrowsConflict()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateDiagnosticServiceAsync(
            11,
            new CreateDiagnosticServiceAdminRequest(
                ServiceCode: "DIA-OLD",
                Name: "Khoa ngừng",
                DepartmentId: 8),
            ipAddress: null));

        Assert.Equal("Khoa thực hiện đang ngừng hoạt động.", ex.Message);
    }

    [Fact]
    public async Task CreateDiagnosticServiceAsync_InvalidServiceCode_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.CreateDiagnosticServiceAsync(
            11,
            new CreateDiagnosticServiceAdminRequest(
                ServiceCode: "có dấu",
                Name: "Sai mã",
                DepartmentId: 4),
            ipAddress: null));

        Assert.Equal("Mã dịch vụ chỉ gồm chữ in hoa, số và các ký tự - _ .", ex.Message);
    }

    [Fact]
    public async Task Mutations_AdminMissingHospitalConfig_ThrowForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateDiagnosticServiceAsync(
            20,
            new CreateDiagnosticServiceAdminRequest(ServiceCode: "DIA-X", Name: "Không quyền", DepartmentId: 4),
            ipAddress: null));

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.UpdateDiagnosticServiceAsync(
            20,
            5,
            new UpdateDiagnosticServiceAdminRequest(Name: "Không quyền", DepartmentId: 4),
            ipAddress: null));

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.DeactivateDiagnosticServiceAsync(
            20,
            5,
            new DeactivateDiagnosticServiceAdminRequest("Không đủ quyền cấu hình"),
            ipAddress: null));
    }

    [Fact]
    public async Task UpdateDiagnosticServiceAsync_ServiceOfOtherHospital_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateDiagnosticServiceAsync(
            11,
            20,
            new UpdateDiagnosticServiceAdminRequest(Name: "Sửa cơ sở khác", DepartmentId: 4),
            ipAddress: null));
    }

    [Fact]
    public async Task UpdateDiagnosticServiceAsync_ChangesNameAndDepartment_KeepsCodeAndWritesBeforeAfter()
    {
        var item = await _service.UpdateDiagnosticServiceAsync(
            11,
            6,
            new UpdateDiagnosticServiceAdminRequest(
                Name: "Siêu âm đã đổi tên",
                Description: "Mô tả mới",
                DepartmentId: 4,
                SpecialtyId: 3,
                RequiresResultVerification: true),
            ipAddress: null);

        Assert.Equal("DIA-US", item.ServiceCode);
        Assert.Equal("Siêu âm đã đổi tên", item.Name);
        Assert.Equal(4, item.DepartmentId);
        Assert.Equal(3, item.SpecialtyId);
        Assert.True(item.RequiresResultVerification);

        var audit = Assert.Single(await _dbContext.AuditLogs.ToListAsync());
        Assert.Equal("DIAGNOSTIC_SERVICE_UPDATED", audit.Action);
        Assert.NotNull(audit.ChangeData);
        using var change = JsonDocument.Parse(audit.ChangeData);
        Assert.Equal("DIA-US", change.RootElement.GetProperty("serviceCode").GetString());
        Assert.Equal("Siêu âm ổ bụng tổng quát", change.RootElement.GetProperty("before").GetProperty("name").GetString());
        Assert.Equal("Siêu âm đã đổi tên", change.RootElement.GetProperty("after").GetProperty("name").GetString());
        Assert.Equal(4, change.RootElement.GetProperty("after").GetProperty("departmentId").GetInt64());
    }

    [Fact]
    public async Task UpdateDiagnosticServiceAsync_InactiveService_ThrowsConflict()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateDiagnosticServiceAsync(
            11,
            7,
            new UpdateDiagnosticServiceAdminRequest(Name: "Không sửa được", DepartmentId: 4),
            ipAddress: null));

        Assert.Equal("Không thể cập nhật dịch vụ cận lâm sàng đang ngừng hoạt động.", ex.Message);
    }

    [Fact]
    public async Task DeactivateDiagnosticServiceAsync_OpenOrder_ThrowsConflictAndStaysActive()
    {
        _dbContext.DiagnosticOrders.Add(new DiagnosticOrder
        {
            Id = 501,
            ServiceId = 5,
            Status = "ORDERED",
        });
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.DeactivateDiagnosticServiceAsync(
            11,
            5,
            new DeactivateDiagnosticServiceAdminRequest("Ngừng khi còn phiếu"),
            ipAddress: null));

        Assert.Contains("1", ex.Message, StringComparison.Ordinal);
        var service = await _dbContext.Services.SingleAsync(s => s.Id == 5);
        Assert.NotEqual(false, service.IsActive);
    }

    [Fact]
    public async Task DeactivateDiagnosticServiceAsync_OnlyClosedOrders_SetsInactiveAndTrimsReason()
    {
        _dbContext.DiagnosticOrders.AddRange(
            new DiagnosticOrder { Id = 601, ServiceId = 6, Status = "COMPLETED" },
            new DiagnosticOrder { Id = 602, ServiceId = 6, Status = "CANCELLED" });
        await _dbContext.SaveChangesAsync();

        var item = await _service.DeactivateDiagnosticServiceAsync(
            11,
            6,
            new DeactivateDiagnosticServiceAdminRequest("  Ngừng vì hết nhu cầu  "),
            ipAddress: null);

        Assert.Equal("INACTIVE", item.Status);
        Assert.False(item.IsActive);

        var audit = Assert.Single(await _dbContext.AuditLogs.ToListAsync());
        Assert.Equal("DIAGNOSTIC_SERVICE_DEACTIVATED", audit.Action);
        Assert.Equal("Ngừng vì hết nhu cầu", audit.Reason);
    }

    [Fact]
    public async Task DeactivateDiagnosticServiceAsync_AlreadyInactive_ThrowsConflict()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.DeactivateDiagnosticServiceAsync(
            11,
            7,
            new DeactivateDiagnosticServiceAdminRequest("Ngừng lần nữa"),
            ipAddress: null));

        Assert.Equal("Dịch vụ cận lâm sàng đã ngừng hoạt động trước đó.", ex.Message);
    }

    [Fact]
    public async Task DeactivateDiagnosticServiceAsync_ShortReason_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.DeactivateDiagnosticServiceAsync(
            11,
            5,
            new DeactivateDiagnosticServiceAdminRequest("abcd"),
            ipAddress: null));

        Assert.Equal("Lý do ngừng hoạt động phải từ 5 đến 500 ký tự.", ex.Message);
    }

    [Fact]
    public async Task GetFormOptionsAsync_ReturnsActiveDepartmentsOfHospitalAndActiveSpecialties()
    {
        var options = await _service.GetFormOptionsAsync(11);

        Assert.Equal(1, options.HospitalId);
        Assert.Equal(new long[] { 4, 7 }, options.Departments.Select(d => d.Id).OrderBy(id => id).ToArray());
        Assert.Equal(
            options.Departments.Select(d => d.Name).OrderBy(n => n, StringComparer.CurrentCulture).ToArray(),
            options.Departments.Select(d => d.Name).ToArray());
        Assert.DoesNotContain(options.Departments, d => d.Id == 8);
        Assert.DoesNotContain(options.Departments, d => d.Id == 9);
        Assert.Equal(new long[] { 4, 3 }, options.Specialties.Select(s => s.Id).ToArray());
        Assert.DoesNotContain(options.Specialties, s => s.Id == 5);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
