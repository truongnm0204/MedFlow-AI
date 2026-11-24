using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.Application.DTOs.ExaminationRooms;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.ExaminationRooms;
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

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => throw NotUsed();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
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
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>(b => b.HasKey(u => u.Id));
            modelBuilder.Entity<Role>(b => b.HasKey(r => r.Id));
            modelBuilder.Entity<UserRole>(b => b.HasKey(ur => new { ur.UserId, ur.RoleId }));
            modelBuilder.Entity<Staff>(b => b.HasKey(s => s.Id));
            modelBuilder.Entity<Doctor>(b => b.HasKey(d => d.Id));
            modelBuilder.Entity<ExaminationSlot>(b => b.HasKey(s => s.Id));
            modelBuilder.Entity<Appointment>(b => b.HasKey(a => a.Id));
            modelBuilder.Entity<Visit>(b => b.HasKey(v => v.Id));
            modelBuilder.Entity<AuditLog>(b => b.HasKey(a => a.Id));

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

        _dbContext.Roles.AddRange(
            new Role { Id = 1, Code = "HOSPITAL_ADMIN", Name = "Quản trị cơ sở" },
            new Role { Id = 2, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 3, Code = "PATIENT", Name = "Bệnh nhân" }
        );

        _dbContext.Users.AddRange(
            new User { Id = 100, AccountStatus = AccountStatus.ACTIVE, Email = "admin@medflow.test" },
            new User { Id = 101, AccountStatus = AccountStatus.ACTIVE, Email = "sysadmin@medflow.test" },
            new User { Id = 102, AccountStatus = AccountStatus.ACTIVE, Email = "admin2@medflow.test" },
            new User { Id = 103, AccountStatus = AccountStatus.ACTIVE, Email = "locked@medflow.test", LockedUntil = DateTime.UtcNow.AddDays(1) },
            new User { Id = 104, AccountStatus = AccountStatus.INACTIVE, Email = "inactive@medflow.test" },
            new User { Id = 105, AccountStatus = AccountStatus.ACTIVE, Email = "patient@medflow.test" }
        );

        _dbContext.Staff.AddRange(
            new Staff { Id = 1, UserId = 100, HospitalId = 1, DepartmentId = 1, FullName = "Admin Hospital 1" },
            new Staff { Id = 2, UserId = 102, HospitalId = 2, DepartmentId = null, FullName = "Admin Hospital 2" }
        );

        _dbContext.UserRoles.AddRange(
            new UserRole { UserId = 100, RoleId = 1 },
            new UserRole { UserId = 101, RoleId = 2 },
            new UserRole { UserId = 102, RoleId = 1 },
            new UserRole { UserId = 103, RoleId = 1 },
            new UserRole { UserId = 104, RoleId = 1 },
            new UserRole { UserId = 105, RoleId = 3 }
        );

        _dbContext.Visits.Add(new Visit
        {
            Id = 1,
            ExaminationRoomId = 2,
            AssignedDoctorId = 10,
            Status = "IN_PROGRESS"
        });

        _dbContext.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 1,
            RoomId = 3,
            DoctorId = 20,
            Status = "OPEN",
            StartsAt = DateTime.UtcNow.AddHours(1),
            EndsAt = DateTime.UtcNow.AddHours(2)
        });

        _dbContext.Appointments.Add(new Appointment
        {
            Id = 1,
            SlotId = 1,
            Status = "BOOKED"
        });

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

    // ==========================================
    // UpdateExaminationRoomAsync Unit Tests
    // ==========================================

    [Fact]
    public async Task UpdateExaminationRoomAsync_ValidFullUpdate_UpdatesEntityAndReturnsMappedDto()
    {
        var request = new UpdateExaminationRoomRequest(
            RoomCode: "  rm-101-mod  ",
            Name: "  Phòng Khám Tiêu Hóa 1 Cập Nhật  ",
            DepartmentId: 2,
            HospitalId: 1,
            RoomType: "  procedural  ",
            Location: "  Tầng 2 Khu B Mới  ",
            Capacity: 4,
            Status: "  maintenance  "
        );

        var result = await _service.UpdateExaminationRoomAsync(1, request);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("RM-101-MOD", result.RoomCode);
        Assert.Equal("Phòng Khám Tiêu Hóa 1 Cập Nhật", result.Name);
        Assert.Equal(1, result.HospitalId);
        Assert.Equal("MedFlow Cơ sở 1", result.Facility);
        Assert.Equal(2, result.DepartmentId);
        Assert.Equal("DEP-002", result.DepartmentCode);
        Assert.Equal("Khoa Tim mạch", result.DepartmentName);
        Assert.Equal("PROCEDURAL", result.RoomType);
        Assert.Equal("Tầng 2 Khu B Mới", result.Location);
        Assert.Equal(4, result.Capacity);
        Assert.Equal("MAINTENANCE", result.Status);
        Assert.False(result.IsAvailable);
        Assert.False(result.IsActive);

        var saved = await _dbContext.HospitalRooms.FindAsync(1L);
        Assert.NotNull(saved);
        Assert.Equal("RM-101-MOD", saved.RoomCode);
        Assert.Equal("Phòng Khám Tiêu Hóa 1 Cập Nhật", saved.Name);
        Assert.Equal(2, saved.DepartmentId);
        Assert.Equal("PROCEDURAL", saved.RoomType);
        Assert.Equal("Tầng 2 Khu B Mới", saved.Location);
        Assert.Equal(4, saved.Capacity);
        Assert.Equal("MAINTENANCE", saved.Status);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_OmittingRoomCode_PreservesExistingRoomCode()
    {
        var request = new UpdateExaminationRoomRequest(
            RoomCode: null,
            Name: "Phòng Khám Giữ Nguyên Mã",
            Type: "diagnostic",
            Capacity: 3
        );

        var result = await _service.UpdateExaminationRoomAsync(1, request);

        Assert.Equal("RM-101", result.RoomCode);
        Assert.Equal("Phòng Khám Giữ Nguyên Mã", result.Name);
        Assert.Equal("DIAGNOSTIC", result.RoomType);
        Assert.Equal(3, result.Capacity);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_UsingCodeAlias_UpdatesRoomCode()
    {
        var request = new UpdateExaminationRoomRequest(
            Code: "bk-alias-101",
            Name: "Phòng Khám Cập Nhật Qua Code Alias"
        );

        var result = await _service.UpdateExaminationRoomAsync(1, request);

        Assert.Equal("BK-ALIAS-101", result.RoomCode);
    }

    [Theory]
    [InlineData("RM-101")]
    [InlineData("rm-101")]
    public async Task UpdateExaminationRoomAsync_KeepingSameRoomCode_DoesNotConflictWithSelf(string roomCode)
    {
        var request = new UpdateExaminationRoomRequest(
            RoomCode: roomCode,
            Name: "Phòng Khám Không Tự Xung Đột"
        );

        var result = await _service.UpdateExaminationRoomAsync(1, request);

        Assert.Equal("RM-101", result.RoomCode);
        Assert.Equal("Phòng Khám Không Tự Xung Đột", result.Name);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_StatusAndBooleanFallbacks_UpdateStatusAndAvailabilityFlags()
    {
        // Transition ACTIVE -> INACTIVE via Status
        var inactiveRes = await _service.UpdateExaminationRoomAsync(1, new UpdateExaminationRoomRequest(
            Name: "Phòng Tạm Ngưng",
            Status: "INACTIVE"
        ));
        Assert.Equal("INACTIVE", inactiveRes.Status);
        Assert.False(inactiveRes.IsAvailable);
        Assert.False(inactiveRes.IsActive);

        // Transition INACTIVE -> ACTIVE via IsAvailable boolean fallback
        var availableRes = await _service.UpdateExaminationRoomAsync(1, new UpdateExaminationRoomRequest(
            Name: "Phòng Mở Lại Qua IsAvailable",
            IsAvailable: true
        ));
        Assert.Equal("ACTIVE", availableRes.Status);
        Assert.True(availableRes.IsAvailable);
        Assert.True(availableRes.IsActive);

        // Transition ACTIVE -> INACTIVE via IsActive boolean fallback
        var isActiveFalseRes = await _service.UpdateExaminationRoomAsync(1, new UpdateExaminationRoomRequest(
            Name: "Phòng Tắt Qua IsActive",
            IsActive: false
        ));
        Assert.Equal("INACTIVE", isActiveFalseRes.Status);
        Assert.False(isActiveFalseRes.IsAvailable);
        Assert.False(isActiveFalseRes.IsActive);
    }

    [Theory]
    [InlineData("Khoa Tim mạch")]
    [InlineData("DEP-002")]
    public async Task UpdateExaminationRoomAsync_DepartmentLookupByNameOrCode_AssignsDepartment(string departmentKey)
    {
        var request = new UpdateExaminationRoomRequest(
            Name: "Phòng Khám Đổi Khoa Theo Tên/Mã",
            Department: departmentKey
        );

        var result = await _service.UpdateExaminationRoomAsync(1, request);

        Assert.Equal(2, result.DepartmentId);
        Assert.Equal("DEP-002", result.DepartmentCode);
        Assert.Equal("Khoa Tim mạch", result.DepartmentName);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_DepartmentNotFound_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationRoomRequest(
            Name: "Phòng Khám Sai Khoa",
            DepartmentId: 9999
        );

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("Không tìm thấy khoa phòng với ID 9999", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_DepartmentNameNotFound_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationRoomRequest(
            Name: "Phòng Khám Sai Tên Khoa",
            Department: "Khoa Không Tồn Tại"
        );

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("Không tìm thấy khoa phòng 'Khoa Không Tồn Tại'", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_CrossHospitalDepartment_ThrowsArgumentException()
    {
        // Room 6 is in Hospital 2, Department 1 is in Hospital 1
        var request = new UpdateExaminationRoomRequest(
            Name: "Phòng Cơ Sở 2 Gán Khoa Cơ Sở 1",
            DepartmentId: 1
        );

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(6, request));
        Assert.Contains("Khoa phòng không thuộc cơ sở y tế của buồng khám", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    public async Task UpdateExaminationRoomAsync_InvalidId_ThrowsArgumentException(long id)
    {
        var request = new UpdateExaminationRoomRequest(Name: "Phòng Hợp Lệ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(id, request));
        Assert.Contains("ID buồng khám phải lớn hơn 0", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateExaminationRoomAsync(1, null!));
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_RoomNotFound_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationRoomRequest(Name: "Phòng Không Tồn Tại");

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateExaminationRoomAsync(9999, request));
        Assert.Contains("Không tìm thấy buồng khám với ID 9999", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateExaminationRoomAsync_EmptyRoomCodeWhenProvided_ThrowsArgumentException(string emptyCode)
    {
        var request = new UpdateExaminationRoomRequest(RoomCode: emptyCode, Name: "Phòng Hợp Lệ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("Mã buồng khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_RoomCodeTooLong_ThrowsArgumentException()
    {
        var longCode = new string('R', 41);
        var request = new UpdateExaminationRoomRequest(RoomCode: longCode, Name: "Phòng Hợp Lệ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("Mã buồng khám không được vượt quá 40 ký tự", ex.Message);
    }

    [Theory]
    [InlineData(null!)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateExaminationRoomAsync_EmptyName_ThrowsArgumentException(string? name)
    {
        var request = new UpdateExaminationRoomRequest(Name: name!);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("Tên buồng khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_NameTooLong_ThrowsArgumentException()
    {
        var longName = new string('N', 201);
        var request = new UpdateExaminationRoomRequest(Name: longName);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("Tên buồng khám không được vượt quá 200 ký tự", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public async Task UpdateExaminationRoomAsync_InvalidCapacity_ThrowsArgumentException(int capacity)
    {
        var request = new UpdateExaminationRoomRequest(Name: "Phòng Hợp Lệ", Capacity: capacity);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("Sức chứa buồng khám phải lớn hơn 0", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task UpdateExaminationRoomAsync_InvalidDepartmentId_ThrowsArgumentException(long departmentId)
    {
        var request = new UpdateExaminationRoomRequest(Name: "Phòng Hợp Lệ", DepartmentId: departmentId);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("ID khoa phòng phải lớn hơn 0", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_RoomTypeTooLong_ThrowsArgumentException()
    {
        var longType = new string('T', 21);
        var request = new UpdateExaminationRoomRequest(Name: "Phòng Hợp Lệ", RoomType: longType);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("Loại phòng không được vượt quá 20 ký tự", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CLOSED")]
    [InlineData("UNKNOWN_STATUS_VALUE_TOO_LONG")]
    public async Task UpdateExaminationRoomAsync_InvalidStatus_ThrowsArgumentException(string status)
    {
        var request = new UpdateExaminationRoomRequest(Name: "Phòng Hợp Lệ", Status: status);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationRoomAsync(1, request));
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_DuplicateRoomCodeSameHospital_ThrowsInvalidOperationException()
    {
        // Room 1 ("RM-101") attempts to change code to "rm-102" (used by Room 2 in Hospital 1)
        var request = new UpdateExaminationRoomRequest(
            RoomCode: "rm-102",
            Name: "Phòng Khám Trùng Mã Trong Cùng Cơ Sở"
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateExaminationRoomAsync(1, request));
        Assert.Contains("đã tồn tại trong cơ sở y tế này", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_SameRoomCodeDifferentHospital_Succeeds()
    {
        // Room 6 is in Hospital 2; "RM-101" exists in Hospital 1
        var request = new UpdateExaminationRoomRequest(
            RoomCode: "RM-101",
            Name: "Phòng Cơ Sở 2 Đổi Sang Mã RM-101"
        );

        var result = await _service.UpdateExaminationRoomAsync(6, request);

        Assert.NotNull(result);
        Assert.Equal(6, result.Id);
        Assert.Equal("RM-101", result.RoomCode);
        Assert.Equal(2, result.HospitalId);
        Assert.Equal("MedFlow Cơ sở 2", result.Facility);
    }

    // ==========================================
    // CheckExaminationRoomDependenciesAsync Tests (#180 / M2-060)
    // ==========================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CheckExaminationRoomDependenciesAsync_NullOrEmptyIdOrCode_ThrowsArgumentException(string? invalidIdOrCode)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CheckExaminationRoomDependenciesAsync(100, invalidIdOrCode!));

        Assert.Equal("idOrCode", ex.ParamName);
    }

    [Fact]
    public async Task CheckExaminationRoomDependenciesAsync_RoomNotFound_ThrowsNotFoundException()
    {
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CheckExaminationRoomDependenciesAsync(100, "NON_EXISTENT_ROOM"));

        Assert.Contains("HospitalRoom", ex.Message);
    }

    [Fact]
    public async Task CheckExaminationRoomDependenciesAsync_CrossHospitalAccess_ThrowsForbiddenException()
    {
        // User 100 belongs to Hospital 1; Room 6 belongs to Hospital 2
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckExaminationRoomDependenciesAsync(100, "6"));

        Assert.Contains("thuộc cơ sở y tế khác", ex.Message);
    }

    [Fact]
    public async Task CheckExaminationRoomDependenciesAsync_AccountInactiveOrLocked_ThrowsForbiddenException()
    {
        // User 103 is locked
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckExaminationRoomDependenciesAsync(103, "1"));

        // User 104 is inactive
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckExaminationRoomDependenciesAsync(104, "1"));
    }

    [Fact]
    public async Task CheckExaminationRoomDependenciesAsync_NonAdminRole_ThrowsForbiddenException()
    {
        // User 105 has only PATIENT role
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckExaminationRoomDependenciesAsync(105, "1"));
    }

    [Fact]
    public async Task CheckExaminationRoomDependenciesAsync_SystemAdmin_CanAccessAnyHospitalRoom()
    {
        // User 101 is SYSTEM_ADMIN; accessing Room 6 in Hospital 2 succeeds
        var result = await _service.CheckExaminationRoomDependenciesAsync(101, "6");

        Assert.NotNull(result);
        Assert.Equal(6, result.RoomId);
        Assert.Equal("RM-CS2-01", result.RoomCode);
    }

    [Fact]
    public async Task CheckExaminationRoomDependenciesAsync_CleanRoom_ReturnsZeroDependenciesAndCanDeactivateTrue()
    {
        // Room 1 has no visits or slots
        var result = await _service.CheckExaminationRoomDependenciesAsync(100, "RM-101");

        Assert.NotNull(result);
        Assert.Equal(1, result.RoomId);
        Assert.Equal(0, result.ActiveVisitsCount);
        Assert.Equal(0, result.AssignedDoctorsCount);
        Assert.Equal(0, result.ActiveSlotsCount);
        Assert.Equal(0, result.PendingAppointmentsCount);
        Assert.False(result.HasActiveDependencies);
        Assert.True(result.CanDeactivate);
        Assert.Null(result.BlockingReason);
    }

    [Fact]
    public async Task CheckExaminationRoomDependenciesAsync_RoomWithActiveVisits_ReturnsActiveVisitsAndCanDeactivateFalse()
    {
        // Room 2 has an ongoing visit with assigned doctor 10
        var result = await _service.CheckExaminationRoomDependenciesAsync(100, "2");

        Assert.NotNull(result);
        Assert.Equal(2, result.RoomId);
        Assert.Equal(1, result.ActiveVisitsCount);
        Assert.Equal(1, result.AssignedDoctorsCount);
        Assert.True(result.HasActiveDependencies);
        Assert.False(result.CanDeactivate);
        Assert.NotNull(result.BlockingReason);
        Assert.Contains("lượt khám", result.BlockingReason);
    }

    [Fact]
    public async Task CheckExaminationRoomDependenciesAsync_RoomWithActiveSlots_ReturnsActiveSlots()
    {
        // Room 3 has future slot and appointment
        var result = await _service.CheckExaminationRoomDependenciesAsync(100, "3");

        Assert.NotNull(result);
        Assert.Equal(3, result.RoomId);
        Assert.Equal(1, result.ActiveSlotsCount);
        Assert.Equal(1, result.PendingAppointmentsCount);
        Assert.True(result.HasActiveDependencies);
        Assert.False(result.CanDeactivate);
    }

    // ==========================================
    // DeactivateExaminationRoomAsync Tests (#180 / M2-060)
    // ==========================================

    [Fact]
    public async Task DeactivateExaminationRoomAsync_RoomWithActiveVisits_ThrowsConflictException()
    {
        // Room 2 has ongoing visit
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.DeactivateExaminationRoomAsync(100, "2"));

        Assert.Contains("lượt khám", ex.Message);
    }

    [Fact]
    public async Task DeactivateExaminationRoomAsync_RoomWithPendingAppointments_ThrowsConflictException()
    {
        // Room 3 has booked appointment
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.DeactivateExaminationRoomAsync(100, "3", new DeactivateExaminationRoomRequest(ConfirmUnderstood: true)));

        Assert.Contains("lịch hẹn", ex.Message);
    }

    [Fact]
    public async Task DeactivateExaminationRoomAsync_RoomWithOnlyFutureSlots_WithoutConfirmation_ThrowsConflictException()
    {
        // Add Room 7 with future slot only (no appointment, no active visit)
        var room7 = new HospitalRoom
        {
            Id = 7,
            HospitalId = 1,
            DepartmentId = 1,
            RoomCode = "RM-701",
            Name = "Phòng Khám Phụ 7",
            RoomType = "EXAMINATION",
            Status = "ACTIVE"
        };
        _dbContext.HospitalRooms.Add(room7);
        _dbContext.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 70,
            RoomId = 7,
            DoctorId = null,
            Status = "OPEN",
            StartsAt = DateTime.UtcNow.AddDays(1),
            EndsAt = DateTime.UtcNow.AddDays(1).AddHours(2)
        });
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.DeactivateExaminationRoomAsync(100, "RM-701", new DeactivateExaminationRoomRequest(ConfirmUnderstood: false)));

        Assert.Contains("ca khám", ex.Message);
    }

    [Fact]
    public async Task DeactivateExaminationRoomAsync_RoomWithOnlyFutureSlots_WithConfirmation_ClosesSlotsAndSucceeds()
    {
        // Add Room 8 with future slot only
        var room8 = new HospitalRoom
        {
            Id = 8,
            HospitalId = 1,
            DepartmentId = 1,
            RoomCode = "RM-801",
            Name = "Phòng Khám Phụ 8",
            RoomType = "EXAMINATION",
            Status = "ACTIVE"
        };
        _dbContext.HospitalRooms.Add(room8);
        var slot80 = new ExaminationSlot
        {
            Id = 80,
            RoomId = 8,
            DoctorId = null,
            Status = "OPEN",
            StartsAt = DateTime.UtcNow.AddDays(1),
            EndsAt = DateTime.UtcNow.AddDays(1).AddHours(2)
        };
        _dbContext.ExaminationSlots.Add(slot80);
        await _dbContext.SaveChangesAsync();

        var result = await _service.DeactivateExaminationRoomAsync(
            100,
            "RM-801",
            new DeactivateExaminationRoomRequest(Reason: "Bảo trì định kỳ", Note: "Đóng ca trước", ConfirmUnderstood: true),
            "127.0.0.1");

        Assert.NotNull(result);
        Assert.Equal("INACTIVE", result.Status);
        Assert.False(result.IsActive);
        Assert.False(result.IsAvailable);
        Assert.Equal(1, result.ClosedSlotsCount);

        var updatedSlot = await _dbContext.ExaminationSlots.FindAsync(80L);
        Assert.Equal("CLOSED", updatedSlot?.Status);
    }

    [Fact]
    public async Task DeactivateExaminationRoomAsync_CleanRoom_SucceedsSetsInactiveAndLogsAudit()
    {
        // Room 1 has no dependencies
        var result = await _service.DeactivateExaminationRoomAsync(
            100,
            "1",
            new DeactivateExaminationRoomRequest(Reason: "Tạm ngưng hoạt động phòng"),
            "192.168.1.1");

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("INACTIVE", result.Status);
        Assert.False(result.IsActive);
        Assert.False(result.IsAvailable);
        Assert.Contains("Đã ngừng hoạt động", result.Message);

        // Verify entity persisted
        var entity = await _dbContext.HospitalRooms.FindAsync(1L);
        Assert.NotNull(entity);
        Assert.Equal("INACTIVE", entity.Status);

        // Verify AuditLog written
        var audit = await _dbContext.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "ROOM_DEACTIVATED" && a.EntityId == "1");
        Assert.NotNull(audit);
        Assert.Equal(100, audit.ActorUserId);
        Assert.Equal("HospitalRoom", audit.EntityType);
        Assert.Equal("Tạm ngưng hoạt động phòng", audit.Reason);
    }

    [Fact]
    public async Task DeactivateExaminationRoomAsync_AlreadyInactiveRoom_ThrowsConflictException()
    {
        // Room 4 is already INACTIVE in seed data
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.DeactivateExaminationRoomAsync(100, "4"));

        Assert.Contains("hiện đã ở trạng thái ngừng hoạt động", ex.Message);
    }

    // ==========================================
    // ReactivateExaminationRoomAsync Tests (#180 / M2-060)
    // ==========================================

    [Fact]
    public async Task ReactivateExaminationRoomAsync_InactiveRoom_SucceedsSetsActiveAndLogsAudit()
    {
        // Room 4 is INACTIVE
        var result = await _service.ReactivateExaminationRoomAsync(
            100,
            "RM-202",
            new ReactivateExaminationRoomRequest(Reason: "Hoàn tất nâng cấp trang thiết bị"),
            "10.0.0.1");

        Assert.NotNull(result);
        Assert.Equal(4, result.Id);
        Assert.Equal("ACTIVE", result.Status);
        Assert.True(result.IsActive);
        Assert.True(result.IsAvailable);
        Assert.Contains("Đã kích hoạt lại", result.Message);

        var entity = await _dbContext.HospitalRooms.FindAsync(4L);
        Assert.NotNull(entity);
        Assert.Equal("ACTIVE", entity.Status);

        var audit = await _dbContext.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "ROOM_REACTIVATED" && a.EntityId == "4");
        Assert.NotNull(audit);
        Assert.Equal(100, audit.ActorUserId);
        Assert.Equal("Hoàn tất nâng cấp trang thiết bị", audit.Reason);
    }

    [Fact]
    public async Task ReactivateExaminationRoomAsync_AlreadyActiveRoom_ThrowsConflictException()
    {
        // Room 1 is ACTIVE
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReactivateExaminationRoomAsync(100, "1"));

        Assert.Contains("đang ở trạng thái hoạt động", ex.Message);
    }

    [Fact]
    public async Task ReactivateExaminationRoomAsync_RoomNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.ReactivateExaminationRoomAsync(100, "NON_EXISTENT"));
    }

    [Fact]
    public async Task UpdateExaminationRoomAsync_ToInactive_WithActiveDependencies_ThrowsConflictException()
    {
        // Room 2 has ongoing visit. Updating status to INACTIVE should be blocked.
        var request = new UpdateExaminationRoomRequest(
            Name: "Phòng Khám Tiêu hóa 2",
            Status: "INACTIVE"
        );

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateExaminationRoomAsync(2, request));

        Assert.Contains("lượt khám", ex.Message);
    }
}
