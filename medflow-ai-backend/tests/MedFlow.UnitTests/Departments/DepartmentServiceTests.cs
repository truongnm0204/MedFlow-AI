using MedFlow.Application.DTOs.Departments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Departments;
using MedFlow.Domain.Entities;
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

        public DbSet<User> Users => throw NotUsed();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => throw NotUsed();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Hospital> Hospitals => throw NotUsed();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
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
            modelBuilder.Ignore<Staff>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Hospital>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<Department>().HasKey(d => d.Id);
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
}
