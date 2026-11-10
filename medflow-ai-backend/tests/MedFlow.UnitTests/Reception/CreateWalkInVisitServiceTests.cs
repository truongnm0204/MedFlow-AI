using System.Net;
using System.Text.Json;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Reception;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Reception;

public sealed class CreateWalkInVisitServiceTests : IDisposable
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class WalkInTestDbContext : DbContext, IApplicationDbContext
    {
        public WalkInTestDbContext(DbContextOptions<WalkInTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for CreateWalkInVisitService unit tests.");

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
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<PatientFamilyLink>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<HospitalPolicy>().HasKey(p => p.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany(d => d.Services).HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany().HasForeignKey(s => s.SpecialtyId);
            });
            modelBuilder.Entity<ServicePrice>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.Service).WithMany(s => s.ServicePrices).HasForeignKey(p => p.ServiceId);
            });
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
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Property(a => a.IpAddress).HasConversion(
                    v => v == null ? null : v.ToString(),
                    v => v == null ? null : IPAddress.Parse(v));
            });
        }
    }

    // 2026-10-27T02:15:00Z == 2026-10-27 09:15:00 Vietnam time (UTC+7)
    private readonly FixedTimeProvider _timeProvider = new(
        new DateTimeOffset(2026, 10, 27, 2, 15, 0, TimeSpan.Zero));
    private readonly WalkInTestDbContext _db;
    private readonly CreateWalkInVisitService _service;

    public CreateWalkInVisitServiceTests()
    {
        var options = new DbContextOptionsBuilder<WalkInTestDbContext>()
            .UseInMemoryDatabase($"CreateWalkInVisitTests_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new WalkInTestDbContext(options);
        _service = new CreateWalkInVisitService(_db, _timeProvider);

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
            new Permission { Id = 5, Code = "QUEUE_MANAGE" });

        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 3, PermissionId = 3 },
            new RolePermission { RoleId = 3, PermissionId = 4 },
            new RolePermission { RoleId = 6, PermissionId = 3 });

        _db.Hospitals.AddRange(
            new Hospital
            {
                Id = 1,
                HospitalCode = "HOS-DEMO-01",
                Name = "Bệnh viện Đa khoa MedFlow Sài Gòn",
                IsActive = true,
            },
            new Hospital
            {
                Id = 2,
                HospitalCode = "HOS-DEMO-02",
                Name = "Bệnh viện MedFlow Cơ sở 2",
                IsActive = true,
            });

        _db.Departments.AddRange(
            new Department
            {
                Id = 2,
                HospitalId = 1,
                DepartmentCode = "GI",
                Name = "Khoa Tiêu hóa",
                IsActive = true,
            },
            new Department
            {
                Id = 3,
                HospitalId = 1,
                DepartmentCode = "CARD",
                Name = "Khoa Tim mạch",
                IsActive = true,
            },
            new Department
            {
                Id = 20,
                HospitalId = 2,
                DepartmentCode = "DEP-H2",
                Name = "Khoa Nội CS2",
                IsActive = true,
            });

        _db.Specialties.AddRange(
            new Specialty
            {
                Id = 1,
                SpecialtyCode = "SPC-GI",
                Name = "Tiêu hóa",
                IsActive = true,
            },
            new Specialty
            {
                Id = 2,
                SpecialtyCode = "SPC-CARD",
                Name = "Tim mạch",
                IsActive = true,
            });

        _db.Services.AddRange(
            new Service
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = "EX-GI-01",
                Name = "Khám Tiêu hóa lần đầu",
                ServiceType = "EXAMINATION",
                IsActive = true,
            },
            new Service
            {
                Id = 2,
                HospitalId = 1,
                DepartmentId = 3,
                SpecialtyId = 2,
                ServiceCode = "EX-CARD-01",
                Name = "Khám Tim mạch lần đầu",
                ServiceType = "EXAMINATION",
                IsActive = true,
            },
            new Service
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = "DG-GI-ENDO",
                Name = "Nội soi dạ dày chẩn đoán",
                ServiceType = "DIAGNOSTIC",
                IsActive = true,
            },
            new Service
            {
                Id = 20,
                HospitalId = 2,
                DepartmentId = 20,
                SpecialtyId = 1,
                ServiceCode = "EX-H2-01",
                Name = "Khám Nội CS2",
                ServiceType = "EXAMINATION",
                IsActive = true,
            });

        _db.ServicePrices.AddRange(
            new ServicePrice
            {
                Id = 1,
                ServiceId = 1,
                PriceAmount = 250_000m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0),
                EffectiveTo = null,
            },
            new ServicePrice
            {
                Id = 2,
                ServiceId = 2,
                PriceAmount = 300_000m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0),
                EffectiveTo = null,
            });

        _db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-001",
                Name = "Khám Tiêu hóa 101",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 - Khu A",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 3,
                HospitalId = 1,
                DepartmentId = 3,
                RoomCode = "RM-003",
                Name = "Khám Tim mạch 201",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 - Khu B",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 20,
                HospitalId = 2,
                DepartmentId = 20,
                RoomCode = "RM-201",
                Name = "Khám Nội 201",
                RoomType = "EXAMINATION",
                Location = "Tầng 2 - CS2",
                Capacity = 2,
                Status = "ACTIVE",
            });

        _db.Users.AddRange(
            new User
            {
                Id = 1,
                Email = "patient01@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 3,
                Email = "doctor01@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 5,
                Email = "reception01@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 11,
                Email = "hospital_admin01@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 13,
                Email = "system_admin01@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 25,
                Email = "reception_h2@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 5, RoleId = 3 },
            new UserRole { UserId = 11, RoleId = 6 },
            new UserRole { UserId = 13, RoleId = 7 },
            new UserRole { UserId = 25, RoleId = 3 });

        _db.Staff.AddRange(
            new Staff
            {
                Id = 1,
                UserId = 3,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-DOC-01",
                FullName = "BS.CKI Nguyễn Văn Hùng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 3,
                UserId = 5,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-REC-01",
                FullName = "Lê Thị Tiếp Đón",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 6,
                UserId = 11,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-ADM-01",
                FullName = "Quản Trị Bệnh Viện 1",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 25,
                UserId = 25,
                HospitalId = 2,
                DepartmentId = 20,
                StaffCode = "STF-REC-H2",
                FullName = "Tiếp Đón CS2",
                Status = "ACTIVE",
            });

        _db.Doctors.Add(new Doctor
        {
            Id = 1,
            StaffId = 1,
            HospitalId = 1,
            SpecialtyId = 1,
            DoctorCode = "DOC-001",
            ProfessionalTitle = "BS.CKI",
            IsActive = true,
        });

        _db.QueueServicePoints.Add(new QueueServicePoint
        {
            Id = 1,
            HospitalId = 1,
            RoomId = 1,
            DoctorId = 1,
            ServiceId = 1,
            PointType = "DOCTOR",
            Name = "Hàng đợi Tiêu hóa 101",
            IsActive = true,
        });

        _db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                UserId = 1,
                PatientCode = "BN-2024-0021",
                FullName = "Trần Minh Đức",
                DateOfBirth = new DateOnly(1985, 5, 10),
                Gender = "MALE",
                Phone = "0936888999",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 2,
                PatientCode = "BN-2024-0022",
                FullName = "Nguyễn Thị Lan",
                DateOfBirth = new DateOnly(1945, 3, 20), // Age 81 (>= 75 ELDERLY)
                Gender = "FEMALE",
                Phone = "0912345678",
                Status = "ACTIVE",
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task CreateWalkInVisitAsync_ValidPayloadWithCodes_CreatesVisitQueuePaymentAndAuditLog()
    {
        var request = new CreateWalkInVisitRequest(
            PatientCode: "BN-2024-0021",
            DepartmentCode: "GI",
            ServiceCode: "EX-GI-01",
            ClinicalPriority: "STANDARD",
            ReasonForVisit: "Đau thượng vị sau ăn kèm ợ chua kéo dài 2 ngày.",
            Symptoms: ["Đau thượng vị", "Ợ hơi/ợ chua", "đau thượng vị"],
            PaymentMethod: "VIETQR",
            BloodPressure: "120/80",
            HeartRate: 78,
            Temperature: 36.8,
            IsIdentityVerified: true,
            IsInsuranceVerified: false,
            Notes: "Bệnh nhân đến trực tiếp tại quầy.");

        var result = await _service.CreateWalkInVisitAsync(
            actorUserId: 5,
            request: request,
            ipAddress: "192.168.1.25");

        Assert.True(result.VisitId > 0);
        Assert.Equal($"VIS-202610-{result.VisitId:D4}", result.VisitCode);
        Assert.Equal($"TKT-{result.VisitCode}", result.TicketCode);
        Assert.Equal("A001", result.TicketNo);
        Assert.Equal(1, result.QueueNumber);
        Assert.Equal("001", result.QueueNumberFormatted);
        Assert.Equal("WAITING", result.QueueStatus);
        Assert.Equal("PRESENT", result.PresenceStatus);
        Assert.Equal("WALK_IN", result.ArrivalClass);
        Assert.Equal("NORMAL", result.ManualPriority);
        Assert.Equal("NORMAL", result.Priority);
        Assert.Equal("STANDARD", result.ClinicalPriority);
        Assert.Equal("CHECKED_IN", result.VisitStatus);
        Assert.Equal("WALK_IN", result.VisitType);
        Assert.Equal(1L, result.PatientId);
        Assert.Equal("BN-2024-0021", result.PatientCode);
        Assert.Equal("Trần Minh Đức", result.PatientName);
        Assert.Equal(2L, result.DepartmentId);
        Assert.Equal("Khoa Tiêu hóa", result.DepartmentName);
        Assert.Equal(1L, result.ServiceId);
        Assert.Equal("EX-GI-01", result.ServiceCode);
        Assert.Equal("Khám Tiêu hóa lần đầu", result.ServiceName);
        Assert.Equal(250_000m, result.ServicePrice);
        Assert.Equal("250.000 VNĐ", result.PriceFormatted);
        Assert.Equal("VIETQR", result.PaymentMethod);
        Assert.Equal("PENDING", result.PaymentStatus);
        Assert.Equal(2, result.Symptoms.Count); // Deduplicated case-insensitively
        Assert.Equal("09:15", result.ReceptionTime);

        var savedVisit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == result.VisitId);
        Assert.NotNull(savedVisit);
        Assert.Null(savedVisit!.AppointmentId);
        Assert.Equal("CHECKED_IN", savedVisit.Status);

        var savedQueue = await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == result.QueueEntryId);
        Assert.NotNull(savedQueue);
        Assert.Equal(result.VisitId, savedQueue!.VisitId);
        Assert.Null(savedQueue.AppointmentId);
        Assert.Equal("EXAMINATION", savedQueue.QueueType);
        Assert.Equal("WAITING", savedQueue.Status);
        Assert.Equal("PRESENT", savedQueue.PresenceStatus);
        Assert.Equal("WALK_IN", savedQueue.ArrivalClass);

        var savedPayment = await _db.Payments.FirstOrDefaultAsync(p => p.VisitId == result.VisitId);
        Assert.NotNull(savedPayment);
        Assert.Equal(250_000m, savedPayment!.Amount);
        Assert.Equal("PENDING", savedPayment.Status);

        var savedAudit = await _db.AuditLogs.FirstOrDefaultAsync(
            a => a.Action == CreateWalkInVisitService.WalkInVisitCreatedAction &&
                 a.EntityId == result.VisitId.ToString());
        Assert.NotNull(savedAudit);
        Assert.Equal(5L, savedAudit!.ActorUserId);
        Assert.Equal(1L, savedAudit.HospitalId);
    }

    [Fact]
    public async Task CreateWalkInVisitAsync_WithDoctorAndRoomAssigned_SetsVisitStatusWaiting()
    {
        var request = new CreateWalkInVisitRequest(
            PatientId: "2",
            DepartmentId: "2",
            ServiceId: "1",
            Priority: "ELDERLY",
            PriorityVerificationConfirmed: true,
            DoctorCode: "DOC-001",
            RoomCode: "RM-001",
            ReasonForVisit: "Khám tiêu hóa định kỳ cho người cao tuổi.",
            PaymentMethod: "CASHIER");

        var result = await _service.CreateWalkInVisitAsync(actorUserId: 5, request: request);

        Assert.Equal("WAITING", result.VisitStatus);
        Assert.Equal("ELDERLY", result.ManualPriority);
        Assert.Equal("ELDERLY", result.Priority);
        Assert.Equal("PRIORITY", result.ClinicalPriority);
        Assert.Equal(1L, result.DoctorId);
        Assert.Equal("DOC-001", result.DoctorCode);
        Assert.Equal(1L, result.RoomId);
        Assert.Equal("RM-001", result.RoomCode);
        Assert.Equal("CASHIER", result.PaymentMethod);
    }

    [Fact]
    public async Task GetWalkInVisitByIdOrCodeAsync_AfterCreation_ReconstructsFullDetails()
    {
        var created = await _service.CreateWalkInVisitAsync(
            actorUserId: 5,
            request: new CreateWalkInVisitRequest(
                PatientId: "1",
                DepartmentId: "2",
                ServiceId: "1",
                ReasonForVisit: "Đau vùng thượng vị kèm đầy hơi.",
                Symptoms: ["Đau thượng vị"],
                BloodPressure: "118/76",
                HeartRate: 72,
                Temperature: 36.5,
                PaymentMethod: "VIETQR"));

        var loaded = await _service.GetWalkInVisitByIdOrCodeAsync(
            actorUserId: 5,
            visitIdOrCode: created.VisitCode);

        Assert.Equal(created.VisitId, loaded.VisitId);
        Assert.Equal(created.VisitCode, loaded.VisitCode);
        Assert.Equal(created.TicketNo, loaded.TicketNo);
        Assert.Equal("VIETQR", loaded.PaymentMethod);
        Assert.Equal("250.000 VNĐ", loaded.PriceFormatted);
        Assert.Equal("118/76", loaded.BloodPressure);
        Assert.Equal(72, loaded.HeartRate);
        Assert.Equal(36.5, loaded.Temperature);
        Assert.Single(loaded.Symptoms);
    }

    [Fact]
    public async Task GetWalkInCatalogAsync_ReturnsHospitalScopedDepartmentsServicesAndSymptoms()
    {
        var catalog = await _service.GetWalkInCatalogAsync(actorUserId: 5);

        Assert.Equal(1L, catalog.HospitalId);
        Assert.Equal(2, catalog.Departments.Count);
        Assert.Contains(catalog.Departments, d => d.Code == "GI" && d.Services.Any(s => s.Code == "EX-GI-01" && s.Price == 250_000m));
        // Diagnostic service DG-GI-ENDO must be excluded from walk-in examination catalog
        Assert.DoesNotContain(catalog.Departments.SelectMany(d => d.Services), s => s.Code == "DG-GI-ENDO");
        Assert.NotEmpty(catalog.SymptomOptions);
    }

    [Theory]
    [InlineData(0L, typeof(UnauthorizedAccessException))]
    [InlineData(1L, typeof(ForbiddenException))] // PATIENT
    [InlineData(3L, typeof(ForbiddenException))] // DOCTOR
    public async Task CreateWalkInVisitAsync_UnauthorizedActor_ThrowsExpectedException(
        long actorUserId,
        Type expectedExceptionType)
    {
        var request = new CreateWalkInVisitRequest(
            PatientId: "1",
            DepartmentId: "2",
            ServiceId: "1",
            ReasonForVisit: "Đau thượng vị kéo dài.");

        await Assert.ThrowsAsync(
            expectedExceptionType,
            () => _service.CreateWalkInVisitAsync(actorUserId, request));
    }

    [Fact]
    public async Task CreateWalkInVisitAsync_CrossHospitalDepartmentOrService_ThrowsForbiddenException()
    {
        var request = new CreateWalkInVisitRequest(
            PatientId: "1",
            DepartmentId: "20", // Hospital 2
            ServiceId: "20",
            ReasonForVisit: "Khám nội tổng quát.");

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateWalkInVisitAsync(actorUserId: 5, request: request));
    }

    [Theory]
    [InlineData("URGENT")]
    [InlineData("EMERGENCY")]
    [InlineData("INVALID_PRIORITY")]
    public async Task CreateWalkInVisitAsync_InvalidOutpatientPriority_ThrowsValidationException(string priority)
    {
        var request = new CreateWalkInVisitRequest(
            PatientId: "1",
            DepartmentId: "2",
            ServiceId: "1",
            Priority: priority,
            ReasonForVisit: "Đau thượng vị cấp.");

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateWalkInVisitAsync(actorUserId: 5, request: request));
    }

    [Fact]
    public async Task CreateWalkInVisitAsync_IneligibleElderlyOrPregnantPriority_ThrowsValidationException()
    {
        // Patient 1 is born in 1985 (41 years old < 75) and MALE
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateWalkInVisitAsync(
                actorUserId: 5,
                request: new CreateWalkInVisitRequest(
                    PatientId: "1",
                    DepartmentId: "2",
                    ServiceId: "1",
                    Priority: "ELDERLY",
                    ReasonForVisit: "Khám tiêu hóa.")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateWalkInVisitAsync(
                actorUserId: 5,
                request: new CreateWalkInVisitRequest(
                    PatientId: "1",
                    DepartmentId: "2",
                    ServiceId: "1",
                    Priority: "PREGNANT",
                    ReasonForVisit: "Khám tiêu hóa.")));
    }

    [Fact]
    public async Task CreateWalkInVisitAsync_DiagnosticServiceOrDepartmentMismatch_ThrowsValidationException()
    {
        // Service 5 is DIAGNOSTIC
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateWalkInVisitAsync(
                actorUserId: 5,
                request: new CreateWalkInVisitRequest(
                    PatientId: "1",
                    DepartmentId: "2",
                    ServiceId: "5",
                    ReasonForVisit: "Nội soi dạ dày.")));

        // Service 2 belongs to Department 3 (CARD), not Department 2 (GI)
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateWalkInVisitAsync(
                actorUserId: 5,
                request: new CreateWalkInVisitRequest(
                    PatientId: "1",
                    DepartmentId: "2",
                    ServiceId: "2",
                    ReasonForVisit: "Khám tim mạch.")));
    }

    [Fact]
    public async Task CreateWalkInVisitAsync_DuplicateActiveSameDayVisit_ThrowsConflictException()
    {
        var request = new CreateWalkInVisitRequest(
            PatientId: "1",
            DepartmentId: "2",
            ServiceId: "1",
            ReasonForVisit: "Đau thượng vị sau ăn.");

        await _service.CreateWalkInVisitAsync(actorUserId: 5, request: request);

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateWalkInVisitAsync(actorUserId: 5, request: request));
    }

    [Fact]
    public async Task CreateWalkInVisitAsync_PatientHasActiveScheduledAppointmentToday_ThrowsConflictException()
    {
        var slot = new ExaminationSlot
        {
            Id = 50,
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 10, 27, 10, 0, 0),
            EndsAt = new DateTime(2026, 10, 27, 10, 30, 0),
            Capacity = 5,
            Status = "OPEN",
        };
        _db.ExaminationSlots.Add(slot);
        _db.Appointments.Add(new Appointment
        {
            Id = 50,
            AppointmentCode = "APT-202610-0050",
            PatientId = 1,
            SlotId = 50,
            Status = "BOOKED",
            ReasonForVisit = "Đã đặt lịch hẹn trước",
        });
        await _db.SaveChangesAsync();

        var request = new CreateWalkInVisitRequest(
            PatientId: "1",
            DepartmentId: "2",
            ServiceId: "1",
            ReasonForVisit: "Bệnh nhân đến quầy xin khám vãng lai.");

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateWalkInVisitAsync(actorUserId: 5, request: request));
    }
}
