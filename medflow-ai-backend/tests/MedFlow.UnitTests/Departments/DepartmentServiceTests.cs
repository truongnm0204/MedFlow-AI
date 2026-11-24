using MedFlow.Application.DTOs.Departments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Departments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Departments;

public sealed class DepartmentServiceTests : IDisposable
{
    private sealed class DepartmentTestDbContext : DbContext, IApplicationDbContext
    {
        public DepartmentTestDbContext(DbContextOptions<DepartmentTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() => new("Not needed for DepartmentService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => throw NotUsed();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Hospital>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<ExaminationSlot>().HasKey(s => s.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
        }
    }

    private readonly DepartmentTestDbContext _dbContext;
    private readonly DepartmentService _service;

    public DepartmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<DepartmentTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DepartmentTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new DepartmentTestDbContext(options);
        _dbContext.Database.EnsureCreated();

        SeedTestData();

        _service = new DepartmentService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    private void SeedTestData()
    {
        _dbContext.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DEP-001", Name = "Khoa Nội Tiêu hóa", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DEP-002", Name = "Khoa Khám bệnh", IsActive = true },
            new Department { Id = 3, HospitalId = 1, DepartmentCode = "DEP-003", Name = "Khoa Nội tiết", IsActive = true },
            new Department { Id = 4, HospitalId = 1, DepartmentCode = "DEP-004", Name = "Khoa Chẩn đoán hình ảnh", IsActive = true },
            new Department { Id = 5, HospitalId = 1, DepartmentCode = "DEP-005", Name = "Khoa Xét nghiệm", IsActive = true },
            new Department { Id = 6, HospitalId = 1, DepartmentCode = "DEP-006", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 7, HospitalId = 1, DepartmentCode = "DEP-007", Name = "Khoa Nhi", IsActive = true },
            new Department { Id = 8, HospitalId = 1, DepartmentCode = "DEP-008", Name = "Khoa Sản", IsActive = true },
            new Department { Id = 9, HospitalId = 1, DepartmentCode = "DEP-009", Name = "Khoa Ngoại tổng hợp", IsActive = true },
            new Department { Id = 10, HospitalId = 1, DepartmentCode = "DEP-010", Name = "Khoa Tai Mũi Họng", IsActive = true },
            new Department { Id = 11, HospitalId = 1, DepartmentCode = "DEP-011", Name = "Khoa Răng Hàm Mặt", IsActive = true },
            new Department { Id = 12, HospitalId = 1, DepartmentCode = "DEP-012", Name = "Khoa Mắt", IsActive = true },
            new Department { Id = 13, HospitalId = 1, DepartmentCode = "DEP-013", Name = "Khoa Da liễu", IsActive = false },
            new Department { Id = 14, HospitalId = 2, DepartmentCode = "DEP-014", Name = "Khoa Y học cổ truyền", IsActive = false }
        );

        _dbContext.Roles.AddRange(
            new Role { Id = 1, Code = "HOSPITAL_ADMIN", Name = "Hospital Admin" },
            new Role { Id = 2, Code = "SYSTEM_ADMIN", Name = "System Admin" }
        );

        _dbContext.Users.AddRange(
            new User { Id = 11, Email = "admin1@test.com", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "admin2@test.com", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "sysadmin@test.com", AccountStatus = AccountStatus.ACTIVE }
        );

        _dbContext.UserRoles.AddRange(
            new UserRole { UserId = 11, RoleId = 1 },
            new UserRole { UserId = 12, RoleId = 1 },
            new UserRole { UserId = 13, RoleId = 2 }
        );

        _dbContext.Staff.AddRange(
            new Staff { Id = 1, UserId = 11, HospitalId = 1, StaffCode = "STF-001", FullName = "Admin 1", DepartmentId = 1 },
            new Staff { Id = 2, UserId = 12, HospitalId = 2, StaffCode = "STF-002", FullName = "Admin 2", DepartmentId = 14 },
            new Staff { Id = 3, UserId = 13, HospitalId = null, StaffCode = "STF-003", FullName = "System Admin" },
            new Staff { Id = 10, UserId = null, HospitalId = 1, StaffCode = "DOC-STF-01", FullName = "Bác sĩ Tiêu hóa", DepartmentId = 6 }
        );

        // Department 5 has an active room
        _dbContext.HospitalRooms.Add(
            new HospitalRoom { Id = 101, DepartmentId = 5, RoomCode = "101", Name = "Phòng 101", RoomType = "EXAMINATION", Status = "ACTIVE" }
        );

        // Department 6 has an active doctor
        _dbContext.Doctors.Add(
            new Doctor { Id = 201, StaffId = 10, HospitalId = 1, DoctorCode = "DOC-201", IsActive = true }
        );
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task GetDepartmentListAsync_ReturnsAllDepartmentsAndCorrectMetrics_WhenNoFilter()
    {
        var request = new GetDepartmentListRequest(Page: 1, PageSize: 10);

        var result = await _service.GetDepartmentListAsync(request);

        Assert.NotNull(result);
        Assert.Equal(14, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(10, result.Data.Count);

        // Verify Metrics
        Assert.Equal(14, result.Metrics.Total);
        Assert.Equal(12, result.Metrics.Active);
        Assert.Equal(2, result.Metrics.Inactive);
        Assert.Equal(2, result.Metrics.Facilities);
    }

    [Fact]
    public async Task GetDepartmentListAsync_FiltersByKeyword_MatchesCodeOrName()
    {
        var requestByCode = new GetDepartmentListRequest(Keyword: "DEP-003");
        var resultByCode = await _service.GetDepartmentListAsync(requestByCode);

        Assert.Single(resultByCode.Data);
        Assert.Equal("DEP-003", resultByCode.Data[0].Code);
        Assert.Equal("Khoa Nội tiết", resultByCode.Data[0].Name);

        var requestByName = new GetDepartmentListRequest(Keyword: "tiêu hóa");
        var resultByName = await _service.GetDepartmentListAsync(requestByName);

        Assert.Single(resultByName.Data);
        Assert.Equal("Khoa Nội Tiêu hóa", resultByName.Data[0].Name);
    }

    [Fact]
    public async Task GetDepartmentListAsync_FiltersByIsActive_TrueAndFalse()
    {
        var activeRequest = new GetDepartmentListRequest(IsActive: true, PageSize: 20);
        var activeResult = await _service.GetDepartmentListAsync(activeRequest);

        Assert.Equal(12, activeResult.Total);
        Assert.All(activeResult.Data, item => Assert.True(item.IsActive));
        Assert.All(activeResult.Data, item => Assert.Equal("ACTIVE", item.Status));

        var inactiveRequest = new GetDepartmentListRequest(IsActive: false);
        var inactiveResult = await _service.GetDepartmentListAsync(inactiveRequest);

        Assert.Equal(2, inactiveResult.Total);
        Assert.All(inactiveResult.Data, item => Assert.False(item.IsActive));
        Assert.All(inactiveResult.Data, item => Assert.Equal("INACTIVE", item.Status));
    }

    [Fact]
    public async Task GetDepartmentListAsync_FiltersByStatus_ActiveAndInactive()
    {
        var statusActiveRequest = new GetDepartmentListRequest(Status: "ACTIVE", PageSize: 20);
        var statusActiveResult = await _service.GetDepartmentListAsync(statusActiveRequest);

        Assert.Equal(12, statusActiveResult.Total);

        var statusInactiveRequest = new GetDepartmentListRequest(Status: "INACTIVE");
        var statusInactiveResult = await _service.GetDepartmentListAsync(statusInactiveRequest);

        Assert.Equal(2, statusInactiveResult.Total);
    }

    [Fact]
    public async Task GetDepartmentListAsync_FiltersByHospitalId()
    {
        var requestHospital2 = new GetDepartmentListRequest(HospitalId: 2);
        var result = await _service.GetDepartmentListAsync(requestHospital2);

        Assert.Single(result.Data);
        Assert.Equal("DEP-014", result.Data[0].Code);
        Assert.Equal("MedFlow Cơ sở 2", result.Data[0].Facility);
    }

    [Fact]
    public async Task GetDepartmentListAsync_PaginatesToSecondPage()
    {
        var requestPage2 = new GetDepartmentListRequest(Page: 2, PageSize: 10);
        var result = await _service.GetDepartmentListAsync(requestPage2);

        Assert.Equal(2, result.Page);
        Assert.Equal(4, result.Data.Count);
        Assert.Equal("DEP-011", result.Data[0].Code);
        Assert.Equal("DEP-014", result.Data[3].Code);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    public async Task GetDepartmentListAsync_InvalidPage_ThrowsArgumentException(int page, int pageSize)
    {
        var request = new GetDepartmentListRequest(Page: page, PageSize: pageSize);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetDepartmentListAsync(request));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1, 101)]
    public async Task GetDepartmentListAsync_InvalidPageSize_ThrowsArgumentException(int page, int pageSize)
    {
        var request = new GetDepartmentListRequest(Page: page, PageSize: pageSize);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetDepartmentListAsync(request));
    }

    [Fact]
    public async Task GetDepartmentListAsync_NoMatch_ReturnsEmptyDataAndCorrectTotal()
    {
        var request = new GetDepartmentListRequest(Keyword: "Không Tồn Tại 9999");
        var result = await _service.GetDepartmentListAsync(request);

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.TotalPages);
        // Overall metrics are still calculated across system
        Assert.Equal(14, result.Metrics.Total);
    }

    [Fact]
    public async Task CreateDepartmentAsync_ValidData_CreatesSuccessfullyAndReturnsDto()
    {
        var request = new CreateDepartmentRequest("dep-new-01", "  Khoa Cấp cứu  ", HospitalId: 1, IsActive: true);

        var result = await _service.CreateDepartmentAsync(request);

        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal("DEP-NEW-01", result.Code);
        Assert.Equal("Khoa Cấp cứu", result.Name);
        Assert.Equal(1, result.HospitalId);
        Assert.Equal("MedFlow Cơ sở 1", result.Facility);
        Assert.True(result.IsActive);
        Assert.Equal("ACTIVE", result.Status);

        var saved = await _dbContext.Departments.FirstOrDefaultAsync(d => d.DepartmentCode == "DEP-NEW-01");
        Assert.NotNull(saved);
        Assert.Equal("Khoa Cấp cứu", saved.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task CreateDepartmentAsync_EmptyCode_ThrowsArgumentException(string? code)
    {
        var request = new CreateDepartmentRequest(code!, "Khoa Hồi sức");

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDepartmentAsync(request));
    }

    [Fact]
    public async Task CreateDepartmentAsync_CodeTooLong_ThrowsArgumentException()
    {
        var longCode = new string('A', 41);
        var request = new CreateDepartmentRequest(longCode, "Khoa Hồi sức");

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDepartmentAsync(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task CreateDepartmentAsync_EmptyName_ThrowsArgumentException(string? name)
    {
        var request = new CreateDepartmentRequest("DEP-ICU", name!);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDepartmentAsync(request));
    }

    [Fact]
    public async Task CreateDepartmentAsync_NameTooLong_ThrowsArgumentException()
    {
        var longName = new string('B', 201);
        var request = new CreateDepartmentRequest("DEP-ICU", longName);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDepartmentAsync(request));
    }

    [Fact]
    public async Task CreateDepartmentAsync_DuplicateCodeSameHospital_ThrowsInvalidOperationException()
    {
        var request = new CreateDepartmentRequest("dep-001", "Khoa Trùng Mã", HospitalId: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateDepartmentAsync(request));
        Assert.Contains("đã tồn tại trong cơ sở này", ex.Message);
    }

    [Fact]
    public async Task CreateDepartmentAsync_SameCodeDifferentHospital_Succeeds()
    {
        // DEP-001 exists in HospitalId 1, creating in HospitalId 2 should succeed
        var request = new CreateDepartmentRequest("DEP-001", "Khoa Nội Tiêu hóa CS2", HospitalId: 2);

        var result = await _service.CreateDepartmentAsync(request);

        Assert.NotNull(result);
        Assert.Equal("DEP-001", result.Code);
        Assert.Equal(2, result.HospitalId);
        Assert.Equal("MedFlow Cơ sở 2", result.Facility);
    }

    [Fact]
    public async Task CreateDepartmentAsync_IsActiveFalse_CreatesWithInactiveStatus()
    {
        var request = new CreateDepartmentRequest("DEP-INACT", "Khoa Tạm Dừng", HospitalId: 1, IsActive: false);

        var result = await _service.CreateDepartmentAsync(request);

        Assert.False(result.IsActive);
        Assert.Equal("INACTIVE", result.Status);
    }

    // ==========================================
    // UpdateDepartmentAsync Unit Tests
    // ==========================================

    [Fact]
    public async Task UpdateDepartmentAsync_WithValidData_UpdatesAndReturnsDto()
    {
        var request = new UpdateDepartmentRequest(
            Code: "DEP-001-MOD",
            Name: "Khoa Nội Tiêu Hóa Cập Nhật",
            HospitalId: 1,
            IsActive: false
        );

        var result = await _service.UpdateDepartmentAsync(1, request);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("DEP-001-MOD", result.Code);
        Assert.Equal("Khoa Nội Tiêu Hóa Cập Nhật", result.Name);
        Assert.Equal(1, result.HospitalId);
        Assert.False(result.IsActive);
        Assert.Equal("INACTIVE", result.Status);
        Assert.Equal("MedFlow Cơ sở 1", result.Facility);

        // Verify persisted entity in DbContext
        var entity = await _dbContext.Departments.FindAsync(1L);
        Assert.NotNull(entity);
        Assert.Equal("DEP-001-MOD", entity.DepartmentCode);
        Assert.Equal("Khoa Nội Tiêu Hóa Cập Nhật", entity.Name);
        Assert.False(entity.IsActive);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_WhenKeepingSameCode_DoesNotConflictWithSelf()
    {
        var request = new UpdateDepartmentRequest(
            Code: "DEP-001",
            Name: "Khoa Nội Tiêu Hóa Mới",
            HospitalId: 1,
            IsActive: true
        );

        var result = await _service.UpdateDepartmentAsync(1, request);

        Assert.NotNull(result);
        Assert.Equal("DEP-001", result.Code);
        Assert.Equal("Khoa Nội Tiêu Hóa Mới", result.Name);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_CaseInsensitiveSameCode_DoesNotConflictWithSelf()
    {
        var request = new UpdateDepartmentRequest(
            Code: "dep-001",
            Name: "Khoa Nội Tiêu Hóa Chữ Thường",
            HospitalId: 1,
            IsActive: true
        );

        var result = await _service.UpdateDepartmentAsync(1, request);

        Assert.NotNull(result);
        Assert.Equal("DEP-001", result.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task UpdateDepartmentAsync_WithInvalidId_ThrowsArgumentException(long id)
    {
        var request = new UpdateDepartmentRequest(Code: "DEP-001", Name: "Khoa Hợp Lệ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateDepartmentAsync(id, request));
        Assert.Contains("ID khoa phòng phải lớn hơn 0", ex.Message);
    }


    [Fact]
    public async Task UpdateDepartmentAsync_WhenDepartmentNotFound_ThrowsKeyNotFoundException()
    {
        var request = new UpdateDepartmentRequest(Code: "DEP-999", Name: "Khoa Không Tồn Tại");

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateDepartmentAsync(9999, request));
        Assert.Contains("Không tìm thấy khoa phòng với ID 9999", ex.Message);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateDepartmentAsync(1, null!));
    }

    [Theory]
    [InlineData(null!)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateDepartmentAsync_WithEmptyCode_ThrowsArgumentException(string? code)
    {
        var request = new UpdateDepartmentRequest(Code: code!, Name: "Khoa Hợp Lệ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateDepartmentAsync(1, request));
        Assert.Contains("Mã khoa phòng không được để trống", ex.Message);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_WithCodeExceeding40Chars_ThrowsArgumentException()
    {
        var longCode = new string('A', 41);
        var request = new UpdateDepartmentRequest(Code: longCode, Name: "Khoa Hợp Lệ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateDepartmentAsync(1, request));
        Assert.Contains("Mã khoa phòng không được vượt quá 40 ký tự", ex.Message);
    }

    [Theory]
    [InlineData(null!)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateDepartmentAsync_WithEmptyName_ThrowsArgumentException(string? name)
    {
        var request = new UpdateDepartmentRequest(Code: "DEP-001", Name: name!);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateDepartmentAsync(1, request));
        Assert.Contains("Tên khoa phòng không được để trống", ex.Message);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_WithNameExceeding200Chars_ThrowsArgumentException()
    {
        var longName = new string('B', 201);
        var request = new UpdateDepartmentRequest(Code: "DEP-001", Name: longName);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateDepartmentAsync(1, request));
        Assert.Contains("Tên khoa phòng không được vượt quá 200 ký tự", ex.Message);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_WithDuplicateCodeInSameHospital_ThrowsInvalidOperationException()
    {
        // Department 1 ("DEP-001") attempts to update code to "DEP-002" which is already used by Department 2 in Hospital 1
        var request = new UpdateDepartmentRequest(
            Code: "DEP-002",
            Name: "Khoa Cập Nhật Trùng Mã",
            HospitalId: 1
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateDepartmentAsync(1, request));
        Assert.Contains("đã tồn tại trong cơ sở y tế này", ex.Message);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_WithSameCodeInDifferentHospital_Succeeds()
    {
        // Department 14 is in Hospital 2. It updates code to "DEP-001" (which exists in Hospital 1, but not Hospital 2).
        var request = new UpdateDepartmentRequest(
            Code: "DEP-001",
            Name: "Khoa Cơ sở 2 Dùng Chung Mã Cơ sở 1",
            HospitalId: 2,
            IsActive: true
        );

        var result = await _service.UpdateDepartmentAsync(14, request);

        Assert.NotNull(result);
        Assert.Equal("DEP-001", result.Code);
        Assert.Equal(2, result.HospitalId);
        Assert.Equal("MedFlow Cơ sở 2", result.Facility);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_NormalizesWhitespaceAndUppercase()
    {
        var request = new UpdateDepartmentRequest(
            Code: "   dep-norm-01   ",
            Name: "   Khoa Chuẩn Hóa Dữ Liệu   "
        );

        var result = await _service.UpdateDepartmentAsync(1, request);

        Assert.Equal("DEP-NORM-01", result.Code);
        Assert.Equal("Khoa Chuẩn Hóa Dữ Liệu", result.Name);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_WhenIsActiveNull_PreservesExistingValue()
    {
        // Department 13 initially has IsActive = false
        var request = new UpdateDepartmentRequest(
            Code: "DEP-013-KEEP",
            Name: "Khoa Da Liễu Giữ Trạng Thái",
            IsActive: null
        );

        var result = await _service.UpdateDepartmentAsync(13, request);

        Assert.False(result.IsActive);
        Assert.Equal("INACTIVE", result.Status);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_WhenHospitalIdNull_PreservesExistingHospitalId()
    {
        // Department 14 initially has HospitalId = 2
        var request = new UpdateDepartmentRequest(
            Code: "DEP-014-KEEP",
            Name: "Khoa Giữ HospitalId",
            HospitalId: null
        );

        var result = await _service.UpdateDepartmentAsync(14, request);

        Assert.Equal(2, result.HospitalId);
        Assert.Equal("MedFlow Cơ sở 2", result.Facility);
    }

    [Fact]
    public async Task CheckDependencies_EmptyIdOrCode_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CheckDepartmentDependenciesAsync(11, "   "));
    }

    [Fact]
    public async Task CheckDependencies_NotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CheckDepartmentDependenciesAsync(11, "9999"));
    }

    [Fact]
    public async Task CheckDependencies_CrossHospital_ThrowsForbiddenException()
    {
        // Dept 14 belongs to Hospital 2, User 11 is HospitalAdmin of Hospital 1
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckDepartmentDependenciesAsync(11, "14"));
    }

    [Fact]
    public async Task CheckDependencies_WithActiveRooms_ReturnsCanDeactivateFalse()
    {
        // Dept 5 has Room 101 active
        var result = await _service.CheckDepartmentDependenciesAsync(11, "5");

        Assert.False(result.CanDeactivate);
        Assert.Equal(1, result.ActiveRoomsCount);
        Assert.NotNull(result.BlockingReason);
        Assert.Contains("buồng khám", result.BlockingReason);
    }

    [Fact]
    public async Task CheckDependencies_WithActiveDoctors_ReturnsCanDeactivateFalse()
    {
        // Dept 6 has Doctor 201 active
        var result = await _service.CheckDepartmentDependenciesAsync(11, "6");

        Assert.False(result.CanDeactivate);
        Assert.Equal(1, result.ActiveDoctorsCount);
        Assert.NotNull(result.BlockingReason);
        Assert.Contains("bác sĩ", result.BlockingReason);
    }

    [Fact]
    public async Task CheckDependencies_CleanDepartment_ReturnsCanDeactivateTrue()
    {
        // Dept 3 has no rooms, doctors, or slots
        var result = await _service.CheckDepartmentDependenciesAsync(11, "3");

        Assert.True(result.CanDeactivate);
        Assert.Equal(0, result.ActiveRoomsCount);
        Assert.Equal(0, result.ActiveDoctorsCount);
        Assert.Null(result.BlockingReason);
    }

    [Fact]
    public async Task DeactivateDepartment_EmptyIdOrCode_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.DeactivateDepartmentAsync(11, ""));
    }

    [Fact]
    public async Task DeactivateDepartment_NotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.DeactivateDepartmentAsync(11, "NON_EXISTENT_DEP"));
    }

    [Fact]
    public async Task DeactivateDepartment_CrossHospital_ThrowsForbiddenException()
    {
        // Dept 14 is in Hospital 2, User 11 is HospitalAdmin in Hospital 1
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.DeactivateDepartmentAsync(11, "14"));
    }

    [Fact]
    public async Task DeactivateDepartment_AlreadyInactive_ThrowsConflictException()
    {
        // Dept 13 is already inactive (IsActive = false)
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.DeactivateDepartmentAsync(11, "13"));

        Assert.Contains("ngừng hoạt động", ex.Message);
    }

    [Fact]
    public async Task DeactivateDepartment_HasActiveRooms_ThrowsConflictException()
    {
        // Dept 5 has Room 101 active
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.DeactivateDepartmentAsync(11, "5"));

        Assert.Contains("buồng khám", ex.Message);
    }

    [Fact]
    public async Task DeactivateDepartment_HasActiveDoctors_ThrowsConflictException()
    {
        // Dept 6 has Doctor 201 active
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.DeactivateDepartmentAsync(11, "6"));

        Assert.Contains("bác sĩ", ex.Message);
    }

    [Fact]
    public async Task DeactivateDepartment_CleanDepartment_DeactivatesAndWritesAuditLog()
    {
        // Dept 3 is clean and active
        var request = new DeactivateDepartmentRequest(
            Reason: "RESTRUCTURING",
            Note: "Tái cơ cấu khoa nội tiết"
        );

        var result = await _service.DeactivateDepartmentAsync(11, "3", request, "127.0.0.1");

        Assert.Equal(3, result.Id);
        Assert.False(result.IsActive);
        Assert.Equal("INACTIVE", result.Status);
        Assert.Contains("Đã ngừng hoạt động khoa phòng", result.Message);

        var deptInDb = await _dbContext.Departments.FindAsync(3L);
        Assert.NotNull(deptInDb);
        Assert.False(deptInDb.IsActive);

        var auditLog = await _dbContext.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "DEPARTMENT_DEACTIVATED" && a.EntityId == "3");
        Assert.NotNull(auditLog);
        Assert.Equal(11, auditLog.ActorUserId);
        Assert.Contains("RESTRUCTURING", auditLog.ChangeData);
    }

    [Fact]
    public async Task ReactivateDepartment_AlreadyActive_ThrowsConflictException()
    {
        // Dept 1 is active
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReactivateDepartmentAsync(11, "1"));

        Assert.Contains("hoạt động", ex.Message);
    }

    [Fact]
    public async Task ReactivateDepartment_InactiveDepartment_ReactivatesAndWritesAuditLog()
    {
        // Dept 13 is inactive
        var request = new ReactivateDepartmentRequest(Reason: "Mở lại khoa da liễu");

        var result = await _service.ReactivateDepartmentAsync(11, "13", request, "127.0.0.1");

        Assert.Equal(13, result.Id);
        Assert.True(result.IsActive);
        Assert.Equal("ACTIVE", result.Status);

        var deptInDb = await _dbContext.Departments.FindAsync(13L);
        Assert.NotNull(deptInDb);
        Assert.True(deptInDb.IsActive);

        var auditLog = await _dbContext.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "DEPARTMENT_REACTIVATED" && a.EntityId == "13");
        Assert.NotNull(auditLog);
        Assert.Equal(11, auditLog.ActorUserId);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_WhenDeactivatingWithActiveDependencies_ThrowsConflictException()
    {
        // Dept 5 has active Room 101, updating to IsActive = false should be rejected
        var request = new UpdateDepartmentRequest(
            Code: "DEP-005",
            Name: "Khoa Xét nghiệm Cập nhật",
            IsActive: false
        );

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDepartmentAsync(5, request));

        Assert.Contains("buồng khám", ex.Message);
    }
}
