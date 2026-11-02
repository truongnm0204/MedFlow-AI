using MedFlow.Application.DTOs.ExaminationRooms;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.ExaminationRooms;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.ExaminationRooms;

public sealed class ExaminationRoomServiceTests : IDisposable
{
    private sealed class ExaminationRoomTestDbContext : DbContext, IApplicationDbContext
    {
        public ExaminationRoomTestDbContext(DbContextOptions<ExaminationRoomTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() => new("Not needed for ExaminationRoomService unit tests.");

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
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
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
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<Department>(b =>
            {
                b.HasKey(d => d.Id);
            });

            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department)
                    .WithMany(d => d.HospitalRooms)
                    .HasForeignKey(r => r.DepartmentId);
            });
        }
    }

    private readonly ExaminationRoomTestDbContext _dbContext;
    private readonly ExaminationRoomService _service;

    public ExaminationRoomServiceTests()
    {
        var options = new DbContextOptionsBuilder<ExaminationRoomTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"RoomTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new ExaminationRoomTestDbContext(options);
        _dbContext.Database.EnsureCreated();

        SeedTestData();

        _service = new ExaminationRoomService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    private void SeedTestData()
    {
        var dept1 = new Department { Id = 1, HospitalId = 1, DepartmentCode = "DEP-001", Name = "Khoa Nội Tiêu hóa", IsActive = true };
        var dept2 = new Department { Id = 2, HospitalId = 1, DepartmentCode = "DEP-002", Name = "Khoa Tim mạch", IsActive = true };

        _dbContext.Departments.AddRange(dept1, dept2);

        _dbContext.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "RM-101",
                Name = "Phòng Khám Tiêu hóa 1",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 Khu A",
                Capacity = 2,
                Status = "ACTIVE"
            },
            new HospitalRoom
            {
                Id = 2,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "RM-102",
                Name = "Phòng Khám Tiêu hóa 2",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 Khu A",
                Capacity = 2,
                Status = "ACTIVE"
            },
            new HospitalRoom
            {
                Id = 3,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-201",
                Name = "Phòng Khám Tim mạch 1",
                RoomType = "EXAMINATION",
                Location = "Tầng 2 Khu B",
                Capacity = 2,
                Status = "ACTIVE"
            },
            new HospitalRoom
            {
                Id = 4,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-202",
                Name = "Phòng Khám Tim mạch 2",
                RoomType = "EXAMINATION",
                Location = "Tầng 2 Khu B",
                Capacity = 2,
                Status = "INACTIVE"
            },
            new HospitalRoom
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "RM-301",
                Name = "Phòng Nội soi Cận lâm sàng",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 3 Khu C",
                Capacity = 1,
                Status = "MAINTENANCE"
            },
            new HospitalRoom
            {
                Id = 6,
                HospitalId = 2,
                DepartmentId = null,
                RoomCode = "RM-CS2-01",
                Name = "Phòng Khám Tổng quát Cơ sở 2",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 Cơ sở 2",
                Capacity = 3,
                Status = "ACTIVE"
            }
        );

        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_ReturnsAllRoomsAndCorrectMetrics_WhenNoFilter()
    {
        var request = new GetExaminationRoomListRequest(Page: 1, PageSize: 10);

        var result = await _service.GetExaminationRoomListAsync(request);

        Assert.NotNull(result);
        Assert.Equal(6, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(6, result.Data.Count);

        // Verify Metrics
        Assert.Equal(6, result.Metrics.Total);
        Assert.Equal(4, result.Metrics.Active);
        Assert.Equal(2, result.Metrics.Inactive);
        Assert.Equal(2, result.Metrics.Departments);

        // Check first item mapping
        var first = result.Data[0];
        Assert.Equal("RM-101", first.RoomCode);
        Assert.Equal("Phòng Khám Tiêu hóa 1", first.Name);
        Assert.Equal("Khoa Nội Tiêu hóa", first.DepartmentName);
        Assert.Equal("DEP-001", first.DepartmentCode);
        Assert.Equal("MedFlow Cơ sở 1", first.Facility);
        Assert.True(first.IsAvailable);
        Assert.True(first.IsActive);
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_FiltersByKeyword_MatchesCodeNameOrLocation()
    {
        var byCode = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(Keyword: "RM-101"));
        Assert.Single(byCode.Data);
        Assert.Equal("RM-101", byCode.Data[0].RoomCode);

        var byName = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(Keyword: "tim mạch"));
        Assert.Equal(2, byName.Data.Count);

        var byLocation = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(Keyword: "khu c"));
        Assert.Single(byLocation.Data);
        Assert.Equal("RM-301", byLocation.Data[0].RoomCode);
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_FiltersByDepartmentId()
    {
        var result = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(DepartmentId: 2));

        Assert.Equal(2, result.Total);
        Assert.All(result.Data, item => Assert.Equal(2, item.DepartmentId));
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_FiltersByHospitalId()
    {
        var result = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(HospitalId: 2));

        Assert.Single(result.Data);
        Assert.Equal("RM-CS2-01", result.Data[0].RoomCode);
        Assert.Equal("MedFlow Cơ sở 2", result.Data[0].Facility);
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_FiltersByRoomType()
    {
        var diagResult = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(RoomType: "DIAGNOSTIC"));
        Assert.Single(diagResult.Data);
        Assert.Equal("RM-301", diagResult.Data[0].RoomCode);

        var examResult = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(RoomType: "EXAMINATION"));
        Assert.Equal(5, examResult.Data.Count);
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_FiltersByStatus_ActiveAndInactive()
    {
        var activeResult = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(Status: "ACTIVE"));
        Assert.Equal(4, activeResult.Total);
        Assert.All(activeResult.Data, r => Assert.Equal("ACTIVE", r.Status));

        var inactiveResult = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(Status: "INACTIVE"));
        Assert.Single(inactiveResult.Data);
        Assert.Equal("RM-202", inactiveResult.Data[0].RoomCode);
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_FiltersByIsAvailable_TrueAndFalse()
    {
        var availableResult = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(IsAvailable: true));
        Assert.Equal(4, availableResult.Total);
        Assert.All(availableResult.Data, r => Assert.True(r.IsAvailable));

        var unavailableResult = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(IsAvailable: false));
        Assert.Equal(2, unavailableResult.Total);
        Assert.All(unavailableResult.Data, r => Assert.False(r.IsAvailable));
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_PaginatesCorrectly()
    {
        var page1 = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(Page: 1, PageSize: 2));
        Assert.Equal(2, page1.Data.Count);
        Assert.Equal("RM-101", page1.Data[0].RoomCode);
        Assert.Equal("RM-102", page1.Data[1].RoomCode);
        Assert.Equal(3, page1.TotalPages);

        var page2 = await _service.GetExaminationRoomListAsync(new GetExaminationRoomListRequest(Page: 2, PageSize: 2));
        Assert.Equal(2, page2.Data.Count);
        Assert.Equal("RM-201", page2.Data[0].RoomCode);
        Assert.Equal("RM-202", page2.Data[1].RoomCode);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    public async Task GetExaminationRoomListAsync_InvalidPage_ThrowsArgumentException(int page, int pageSize)
    {
        var request = new GetExaminationRoomListRequest(Page: page, PageSize: pageSize);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.GetExaminationRoomListAsync(request));
        Assert.Contains("Trang yêu cầu phải lớn hơn hoặc bằng 1", ex.Message);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1, 101)]
    public async Task GetExaminationRoomListAsync_InvalidPageSize_ThrowsArgumentException(int page, int pageSize)
    {
        var request = new GetExaminationRoomListRequest(Page: page, PageSize: pageSize);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.GetExaminationRoomListAsync(request));
        Assert.Contains("Kích thước trang phải từ 1 đến 100", ex.Message);
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_NoMatch_ReturnsEmptyDataAndCorrectMetrics()
    {
        var request = new GetExaminationRoomListRequest(Keyword: "KhongTonTai999");

        var result = await _service.GetExaminationRoomListAsync(request);

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.TotalPages);
        // Overall metrics are preserved
        Assert.Equal(6, result.Metrics.Total);
        Assert.Equal(4, result.Metrics.Active);
    }

    [Fact]
    public async Task GetExaminationRoomListAsync_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetExaminationRoomListAsync(null!));
    }

    // ==========================================
    // CreateExaminationRoomAsync Unit Tests
    // ==========================================

    [Fact]
    public async Task CreateExaminationRoomAsync_ValidData_CreatesSuccessfullyAndReturnsDto()
    {
        var request = new CreateExaminationRoomRequest(
            RoomCode: "bk-new-01",
            Name: "Phòng Khám Đa Khoa Mới",
            DepartmentId: 1,
            HospitalId: 1,
            RoomType: "EXAMINATION",
            Location: "Tầng 3 Tòa B",
            Capacity: 2,
            Status: "ACTIVE"
        );

        var result = await _service.CreateExaminationRoomAsync(request);

        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal("BK-NEW-01", result.RoomCode);
        Assert.Equal("Phòng Khám Đa Khoa Mới", result.Name);
        Assert.Equal(1, result.HospitalId);
        Assert.Equal("MedFlow Cơ sở 1", result.Facility);
        Assert.Equal(1, result.DepartmentId);
        Assert.Equal("DEP-001", result.DepartmentCode);
        Assert.Equal("Khoa Nội Tiêu hóa", result.DepartmentName);
        Assert.Equal("EXAMINATION", result.RoomType);
        Assert.Equal("Tầng 3 Tòa B", result.Location);
        Assert.Equal(2, result.Capacity);
        Assert.Equal("ACTIVE", result.Status);
        Assert.True(result.IsActive);
        Assert.True(result.IsAvailable);

        // Verify entity in DbContext
        var saved = await _dbContext.HospitalRooms.FirstOrDefaultAsync(r => r.RoomCode == "BK-NEW-01");
        Assert.NotNull(saved);
        Assert.Equal("Phòng Khám Đa Khoa Mới", saved.Name);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_DiagnosticRoomType_CreatesSuccessfully()
    {
        var request = new CreateExaminationRoomRequest(
            RoomCode: "PKT-MRI-01",
            Name: "Phòng Chụp Cộng Hưởng Từ MRI 01",
            DepartmentId: 2,
            HospitalId: 1,
            RoomType: "DIAGNOSTIC",
            Location: "Tầng Hầm B1",
            Capacity: 1,
            Status: "ACTIVE"
        );

        var result = await _service.CreateExaminationRoomAsync(request);

        Assert.NotNull(result);
        Assert.Equal("PKT-MRI-01", result.RoomCode);
        Assert.Equal("DIAGNOSTIC", result.RoomType);
        Assert.True(result.IsAvailable);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_InactiveStatus_ReturnsIsActiveFalse()
    {
        var request = new CreateExaminationRoomRequest(
            RoomCode: "BK-INACT-01",
            Name: "Phòng Khám Tạm Dừng Hoạt Động",
            Status: "INACTIVE"
        );

        var result = await _service.CreateExaminationRoomAsync(request);

        Assert.NotNull(result);
        Assert.Equal("BK-INACT-01", result.RoomCode);
        Assert.Equal("INACTIVE", result.Status);
        Assert.False(result.IsActive);
        Assert.False(result.IsAvailable);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_WithCodeFieldInsteadOfRoomCode_CreatesSuccessfully()
    {
        var request = new CreateExaminationRoomRequest(
            Code: "BK-ALT-01",
            Name: "Phòng Khám Dùng Trường Code Thay Vì RoomCode"
        );

        var result = await _service.CreateExaminationRoomAsync(request);

        Assert.NotNull(result);
        Assert.Equal("BK-ALT-01", result.RoomCode);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_NormalizesWhitespaceAndUppercase()
    {
        var request = new CreateExaminationRoomRequest(
            RoomCode: "   bk-norm-01   ",
            Name: "   Phòng Khám Chuẩn Hóa Cắt Khoảng Trắng   ",
            RoomType: "   examination   ",
            Location: "   Tầng 2   "
        );

        var result = await _service.CreateExaminationRoomAsync(request);

        Assert.Equal("BK-NORM-01", result.RoomCode);
        Assert.Equal("Phòng Khám Chuẩn Hóa Cắt Khoảng Trắng", result.Name);
        Assert.Equal("EXAMINATION", result.RoomType);
        Assert.Equal("Tầng 2", result.Location);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_WhenHospitalIdNull_DefaultsTo1()
    {
        var request = new CreateExaminationRoomRequest(
            RoomCode: "BK-DEFAULT-HOSP",
            Name: "Phòng Khám Mặc Định Cơ Sở 1",
            HospitalId: null
        );

        var result = await _service.CreateExaminationRoomAsync(request);

        Assert.Equal(1, result.HospitalId);
        Assert.Equal("MedFlow Cơ sở 1", result.Facility);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_WhenDepartmentIdNull_SucceedsWithoutDepartment()
    {
        var request = new CreateExaminationRoomRequest(
            RoomCode: "BK-NO-DEPT",
            Name: "Phòng Khám Đa Năng Chưa Gán Khoa",
            DepartmentId: null
        );

        var result = await _service.CreateExaminationRoomAsync(request);

        Assert.Null(result.DepartmentId);
        Assert.Null(result.DepartmentCode);
        Assert.Null(result.DepartmentName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task CreateExaminationRoomAsync_EmptyRoomCode_ThrowsArgumentException(string? code)
    {
        var request = new CreateExaminationRoomRequest(RoomCode: code!, Name: "Phòng Khám Hợp Lệ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationRoomAsync(request));
        Assert.Contains("Mã buồng khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_RoomCodeTooLong_ThrowsArgumentException()
    {
        var longCode = new string('R', 41);
        var request = new CreateExaminationRoomRequest(RoomCode: longCode, Name: "Phòng Khám Hợp Lệ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationRoomAsync(request));
        Assert.Contains("Mã buồng khám không được vượt quá 40 ký tự", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task CreateExaminationRoomAsync_EmptyName_ThrowsArgumentException(string? name)
    {
        var request = new CreateExaminationRoomRequest(RoomCode: "BK-001", Name: name!);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationRoomAsync(request));
        Assert.Contains("Tên buồng khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_NameTooLong_ThrowsArgumentException()
    {
        var longName = new string('N', 201);
        var request = new CreateExaminationRoomRequest(RoomCode: "BK-001", Name: longName);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationRoomAsync(request));
        Assert.Contains("Tên buồng khám không được vượt quá 200 ký tự", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public async Task CreateExaminationRoomAsync_InvalidCapacity_ThrowsArgumentException(int capacity)
    {
        var request = new CreateExaminationRoomRequest(RoomCode: "BK-001", Name: "Phòng Khám Hợp Lệ", Capacity: capacity);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationRoomAsync(request));
        Assert.Contains("Sức chứa buồng khám phải lớn hơn 0", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_DuplicateRoomCodeSameHospital_ThrowsInvalidOperationException()
    {
        // RM-101 already exists in Hospital 1
        var request = new CreateExaminationRoomRequest(
            RoomCode: "rm-101",
            Name: "Phòng Khám Trùng Mã Cơ Sở 1",
            HospitalId: 1
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateExaminationRoomAsync(request));
        Assert.Contains("đã tồn tại trong cơ sở y tế này", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_SameRoomCodeDifferentHospital_Succeeds()
    {
        // RM-101 exists in Hospital 1, creating in Hospital 2 should succeed
        var request = new CreateExaminationRoomRequest(
            RoomCode: "RM-101",
            Name: "Phòng Khám Cơ Sở 2 Dùng Chung Mã",
            HospitalId: 2
        );

        var result = await _service.CreateExaminationRoomAsync(request);

        Assert.NotNull(result);
        Assert.Equal("RM-101", result.RoomCode);
        Assert.Equal(2, result.HospitalId);
        Assert.Equal("MedFlow Cơ sở 2", result.Facility);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_DepartmentNotFound_ThrowsKeyNotFoundException()
    {
        var request = new CreateExaminationRoomRequest(
            RoomCode: "BK-INVALID-DEPT",
            Name: "Phòng Khám Sai Khoa",
            DepartmentId: 9999
        );

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateExaminationRoomAsync(request));
        Assert.Contains("Không tìm thấy khoa phòng với ID 9999", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationRoomAsync_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateExaminationRoomAsync(null!));
    }
}
