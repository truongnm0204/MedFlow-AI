using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Visits;

public sealed class VisitRoomTransferServiceTests : IDisposable
{
    private sealed class VisitRoomTransferTestDbContext : DbContext, IApplicationDbContext
    {
        public VisitRoomTransferTestDbContext(DbContextOptions<VisitRoomTransferTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for VisitRoomTransferService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => Set<DoctorServiceAuthorization>();
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

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();
            modelBuilder.Ignore<Medicine>();
            modelBuilder.Ignore<MedicineBatch>();
            modelBuilder.Ignore<Prescription>();
            modelBuilder.Ignore<PrescriptionItem>();
            modelBuilder.Ignore<MedicationDispense>();
            modelBuilder.Ignore<DispenseItem>();
            modelBuilder.Ignore<StockTransaction>();

            modelBuilder.Entity<User>(b => b.HasKey(u => u.Id));
            modelBuilder.Entity<Role>(b => b.HasKey(r => r.Id));
            modelBuilder.Entity<Permission>(b => b.HasKey(p => p.Id));
            modelBuilder.Entity<UserRole>(b => b.HasKey(ur => new { ur.UserId, ur.RoleId }));
            modelBuilder.Entity<RolePermission>(b => b.HasKey(rp => new { rp.RoleId, rp.PermissionId }));
            modelBuilder.Entity<Hospital>(b => b.HasKey(h => h.Id));
            modelBuilder.Entity<Department>(b => b.HasKey(d => d.Id));
            modelBuilder.Entity<Specialty>(b => b.HasKey(s => s.Id));
            modelBuilder.Entity<Patient>(b => b.HasKey(p => p.Id));

            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department)
                    .WithMany(d => d.HospitalRooms)
                    .HasForeignKey(r => r.DepartmentId);
            });

            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.User)
                    .WithOne(u => u.Staff)
                    .HasForeignKey<Staff>(s => s.UserId);
                b.HasOne(s => s.Department)
                    .WithMany()
                    .HasForeignKey(s => s.DepartmentId);
            });

            modelBuilder.Entity<Doctor>(b =>
            {
                b.HasKey(d => d.Id);
                b.HasOne(d => d.Staff)
                    .WithOne(s => s.Doctor)
                    .HasForeignKey<Doctor>(d => d.StaffId);
                b.HasOne(d => d.Specialty)
                    .WithMany()
                    .HasForeignKey(d => d.SpecialtyId);
            });

            modelBuilder.Entity<DoctorServiceAuthorization>(b =>
            {
                b.HasKey(a => new { a.DoctorId, a.ServiceId });
            });

            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department)
                    .WithMany(d => d.Services)
                    .HasForeignKey(s => s.DepartmentId);
            });

            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Service)
                    .WithMany()
                    .HasForeignKey(s => s.ServiceId);
                b.HasOne(s => s.Doctor)
                    .WithMany(d => d.ExaminationSlots)
                    .HasForeignKey(s => s.DoctorId);
                b.HasOne(s => s.Room)
                    .WithMany()
                    .HasForeignKey(s => s.RoomId);
            });

            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.HasOne(a => a.Slot)
                    .WithMany(s => s.Appointments)
                    .HasForeignKey(a => a.SlotId);
                b.HasOne(a => a.Patient)
                    .WithMany()
                    .HasForeignKey(a => a.PatientId);
            });

            modelBuilder.Entity<Visit>(b => b.HasKey(v => v.Id));
            modelBuilder.Entity<QueueEntry>(b => b.HasKey(q => q.Id));
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
            });
        }
    }

    private readonly VisitRoomTransferTestDbContext _db;
    private readonly VisitRoomTransferService _service;

    public VisitRoomTransferServiceTests()
    {
        var options = new DbContextOptionsBuilder<VisitRoomTransferTestDbContext>()
            .UseInMemoryDatabase($"VisitRoomTransferTests_{Guid.NewGuid()}")
            .Options;

        _db = new VisitRoomTransferTestDbContext(options);
        _db.Database.EnsureCreated();

        SeedTestData();

        _service = new VisitRoomTransferService(_db);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedTestData()
    {
        _db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện Trung tâm 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện Cơ sở 2", IsActive = true });

        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Lễ tân" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Nhân viên CLS" },
            new Role { Id = 5, Code = "PHARMACY_STAFF", Name = "Nhân viên Dược" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 7, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" });

        _db.Permissions.AddRange(
            new Permission { Id = 3, Code = "PATIENT_CHECKIN" },
            new Permission { Id = 4, Code = "VISIT_ASSIGN" },
            new Permission { Id = 5, Code = "QUEUE_MANAGE" },
            new Permission { Id = 11, Code = "HOSPITAL_CONFIG" });

        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 3, PermissionId = 3 },
            new RolePermission { RoleId = 3, PermissionId = 4 },
            new RolePermission { RoleId = 3, PermissionId = 5 },
            new RolePermission { RoleId = 6, PermissionId = 4 },
            new RolePermission { RoleId = 6, PermissionId = 11 },
            new RolePermission { RoleId = 7, PermissionId = 11 });

        _db.Users.AddRange(
            new User { Id = 1, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 5, Email = "reception01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 7, Email = "diagnostic01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 9, Email = "pharmacy01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "system_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "inactive_reception@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User
            {
                Id = 21,
                Email = "locked_reception@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = DateTime.UtcNow.AddHours(2),
            },
            new User { Id = 22, Email = "inactive_staff@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 23, Email = "no_hospital_staff@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 5, RoleId = 3 },
            new UserRole { UserId = 7, RoleId = 4 },
            new UserRole { UserId = 9, RoleId = 5 },
            new UserRole { UserId = 11, RoleId = 6 },
            new UserRole { UserId = 13, RoleId = 7 },
            new UserRole { UserId = 20, RoleId = 3 },
            new UserRole { UserId = 21, RoleId = 3 },
            new UserRole { UserId = 22, RoleId = 3 },
            new UserRole { UserId = 23, RoleId = 3 });

        var deptGi = new Department { Id = 2, HospitalId = 1, DepartmentCode = "GI", Name = "Khoa Tiêu hóa", IsActive = true };
        var deptCard = new Department { Id = 3, HospitalId = 1, DepartmentCode = "CARD", Name = "Khoa Tim mạch", IsActive = true };
        var deptCls = new Department { Id = 4, HospitalId = 1, DepartmentCode = "DIA", Name = "Khoa Cận lâm sàng", IsActive = true };
        var deptInactive = new Department { Id = 5, HospitalId = 1, DepartmentCode = "INACT", Name = "Khoa Tạm ngưng", IsActive = false };
        var deptHos2 = new Department { Id = 6, HospitalId = 2, DepartmentCode = "GI-H2", Name = "Khoa Tiêu hóa CS2", IsActive = true };
        _db.Departments.AddRange(deptGi, deptCard, deptCls, deptInactive, deptHos2);

        var specGi = new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Tiêu hóa", IsActive = true };
        var specCard = new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true };
        _db.Specialties.AddRange(specGi, specCard);

        _db.Staff.AddRange(
            new Staff { Id = 1, UserId = 3, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-001", FullName = "BS Nguyễn Minh Đức", Status = "ACTIVE" },
            new Staff { Id = 2, UserId = null, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-002", FullName = "BS Lê Thanh Tùng", Status = "ACTIVE" },
            new Staff { Id = 3, UserId = 5, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-003", FullName = "Lễ tân Lê Lan", Status = "ACTIVE" },
            new Staff { Id = 4, UserId = 11, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-009", FullName = "QLBV Phan Bình", Status = "ACTIVE" },
            new Staff { Id = 5, UserId = 13, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-011", FullName = "QTHT Lưu Huy", Status = "ACTIVE" },
            new Staff { Id = 6, UserId = 22, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-INACT", FullName = "Lễ tân Ngừng việc", Status = "INACTIVE" },
            new Staff { Id = 7, UserId = 23, HospitalId = null, DepartmentId = 2, StaffCode = "STF-NOHOS", FullName = "Lễ tân Chưa gán BV", Status = "ACTIVE" },
            new Staff { Id = 8, UserId = null, HospitalId = 1, DepartmentId = 3, StaffCode = "STF-CARD", FullName = "BS Trần Thu Hà", Status = "ACTIVE" },
            new Staff { Id = 9, UserId = null, HospitalId = 2, DepartmentId = 6, StaffCode = "STF-HOS2", FullName = "BS Cơ sở 2", Status = "ACTIVE" },
            new Staff { Id = 10, UserId = null, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-DOC-INACT", FullName = "BS Tạm nghỉ", Status = "ACTIVE" });

        _db.Doctors.AddRange(
            new Doctor { Id = 1, StaffId = 1, HospitalId = 1, DoctorCode = "DOC-001", SpecialtyId = 1, ProfessionalTitle = "BS.CKI", IsActive = true },
            new Doctor { Id = 2, StaffId = 2, HospitalId = 1, DoctorCode = "DOC-002", SpecialtyId = 1, ProfessionalTitle = "ThS.BS", IsActive = true },
            new Doctor { Id = 3, StaffId = 8, HospitalId = 1, DoctorCode = "DOC-003", SpecialtyId = 2, ProfessionalTitle = "BS.CKII", IsActive = true },
            new Doctor { Id = 4, StaffId = 9, HospitalId = 2, DoctorCode = "DOC-004", SpecialtyId = 1, ProfessionalTitle = "BS", IsActive = true },
            new Doctor { Id = 5, StaffId = 10, HospitalId = 1, DoctorCode = "DOC-005", SpecialtyId = 1, ProfessionalTitle = "BS", IsActive = false });

        _db.Services.AddRange(
            new Service { Id = 1, HospitalId = 1, DepartmentId = 2, SpecialtyId = 1, ServiceCode = "EX-GI-01", Name = "Khám Tiêu hóa lần đầu", ServiceType = "EXAMINATION", IsActive = true },
            new Service { Id = 2, HospitalId = 1, DepartmentId = 3, SpecialtyId = 2, ServiceCode = "EX-CARD-01", Name = "Khám Tim mạch lần đầu", ServiceType = "EXAMINATION", IsActive = true });

        _db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 1, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 2, ServiceId = 1, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 3, ServiceId = 2, IsActive = true });

        _db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-001",
                Name = "Khám Tiêu hóa 101",
                RoomType = "EXAMINATION",
                Location = "Tầng 1, Khu A",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 2,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-002",
                Name = "Khám Tiêu hóa 102",
                RoomType = "EXAMINATION",
                Location = "Tầng 1, Khu A",
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
                Location = "Tầng 2, Khu A",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 4,
                RoomCode = "RM-005",
                Name = "Phòng Cận lâm sàng 205",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2, Khu B",
                Capacity = 1,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 6,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-006",
                Name = "Khám Tiêu hóa 103 (Bảo trì)",
                RoomType = "EXAMINATION",
                Location = "Tầng 1, Khu A",
                Capacity = 2,
                Status = "MAINTENANCE",
            },
            new HospitalRoom
            {
                Id = 7,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-007",
                Name = "Khám Tiêu hóa 104 (Đầy chỗ)",
                RoomType = "EXAMINATION",
                Location = "Tầng 1, Khu A",
                Capacity = 1,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 8,
                HospitalId = 1,
                DepartmentId = 5,
                RoomCode = "RM-008",
                Name = "Phòng Khoa Tạm ngưng",
                RoomType = "EXAMINATION",
                Location = "Tầng 3",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 9,
                HospitalId = 2,
                DepartmentId = 6,
                RoomCode = "RM-201",
                Name = "Khám Tiêu hóa CS2 - P1",
                RoomType = "EXAMINATION",
                Location = "Tầng 1, CS2",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 10,
                HospitalId = 2,
                DepartmentId = 6,
                RoomCode = "RM-202",
                Name = "Khám Tiêu hóa CS2 - P2",
                RoomType = "EXAMINATION",
                Location = "Tầng 1, CS2",
                Capacity = 2,
                Status = "ACTIVE",
            });

        _db.Patients.AddRange(
            new Patient { Id = 1, UserId = 1, PatientCode = "PAT-0001", FullName = "Nguyễn Văn An", Status = "ACTIVE" },
            new Patient { Id = 2, UserId = null, PatientCode = "PAT-0002", FullName = "Trần Thị Bình", Status = "ACTIVE" });

        _db.ExaminationSlots.Add(
            new ExaminationSlot
            {
                Id = 1,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 1, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 1, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });

        _db.Appointments.Add(
            new Appointment
            {
                Id = 1,
                AppointmentCode = "APT-202611-0001",
                PatientId = 1,
                SlotId = 1,
                BookingChannel = "COUNTER",
                Status = "CHECKED_IN",
            });

        _db.Visits.AddRange(
            new Visit
            {
                Id = 100,
                VisitCode = "VIS-202611-0001",
                PatientId = 1,
                AppointmentId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Đau thượng vị",
                Status = "CALLED",
                CheckedInAt = new DateTime(2026, 11, 1, 8, 50, 0),
            },
            new Visit
            {
                Id = 101,
                VisitCode = "VIS-202611-0002",
                PatientId = 2,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 7, // occupies RM-007 (capacity 1)
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám tiêu hóa",
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 11, 1, 8, 55, 0),
            },
            new Visit
            {
                Id = 102,
                VisitCode = "VIS-202611-0003",
                PatientId = 2,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Đã hoàn tất",
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 11, 1, 8, 0, 0),
                CompletedAt = new DateTime(2026, 11, 1, 9, 0, 0),
            },
            new Visit
            {
                Id = 103,
                VisitCode = "VIS-202611-0004",
                PatientId = 2,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Đang khám lâm sàng",
                Status = "IN_CONSULTATION",
                CheckedInAt = new DateTime(2026, 11, 1, 8, 15, 0),
            },
            new Visit
            {
                Id = 104,
                VisitCode = "VIS-202611-0005",
                PatientId = 2,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = null, // unassigned room
                CheckedInByStaffId = 3,
                ReasonForVisit = "Chưa gán phòng",
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 11, 1, 8, 20, 0),
            },
            new Visit
            {
                Id = 105,
                VisitCode = "VIS-202611-0006",
                PatientId = 2,
                AppointmentId = null,
                AssignedDoctorId = 4,
                ExaminationRoomId = 9, // Hospital 2
                CheckedInByStaffId = 9,
                ReasonForVisit = "Khám tại CS2",
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 11, 1, 8, 30, 0),
            });

        _db.QueueEntries.Add(
            new QueueEntry
            {
                Id = 500,
                VisitId = 100,
                AppointmentId = 1,
                QueueDate = new DateOnly(2026, 11, 1),
                QueueNumber = 15,
                QueueType = "EXAMINATION",
                Status = "CALLED",
                PresenceStatus = "PRESENT",
                CalledAt = new DateTime(2026, 11, 1, 8, 58, 0),
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetCompatibleRoomsAsync_ReturnsSameDepartmentExaminationRoomsAndAvailability()
    {
        var result = await _service.GetCompatibleRoomsAsync(actorUserId: 5, idOrCode: "VIS-202611-0001");

        Assert.Equal(100L, result.VisitId);
        Assert.Equal("VIS-202611-0001", result.VisitCode);
        Assert.Equal("RM-001", result.CurrentRoomCode);
        Assert.Equal(2L, result.DepartmentId);
        Assert.Equal(15, result.QueueNumber);

        // Only Department 2 EXAMINATION rooms in Hospital 1: RM-001, RM-002, RM-006, RM-007
        Assert.Equal(["RM-001", "RM-002", "RM-006", "RM-007"], result.CompatibleRooms.Select(r => r.RoomCode));

        var current = Assert.Single(result.CompatibleRooms, r => r.RoomCode == "RM-001");
        Assert.True(current.IsCurrentRoom);
        Assert.False(current.IsAvailable);
        Assert.NotNull(current.UnavailableReason);

        var compatibleAvailable = Assert.Single(result.CompatibleRooms, r => r.RoomCode == "RM-002");
        Assert.False(compatibleAvailable.IsCurrentRoom);
        Assert.True(compatibleAvailable.IsCompatible);
        Assert.True(compatibleAvailable.IsAvailable);
        Assert.Null(compatibleAvailable.UnavailableReason);

        var maintenance = Assert.Single(result.CompatibleRooms, r => r.RoomCode == "RM-006");
        Assert.False(maintenance.IsAvailable);
        Assert.Contains("bảo trì", maintenance.UnavailableReason, StringComparison.OrdinalIgnoreCase);

        var fullRoom = Assert.Single(result.CompatibleRooms, r => r.RoomCode == "RM-007");
        Assert.False(fullRoom.IsAvailable);
        Assert.Equal(1, fullRoom.ActiveVisitCount);
        Assert.Equal(0, fullRoom.AvailableCapacity);
        Assert.Contains("sức chứa tối đa", fullRoom.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransferToCompatibleRoomAsync_ValidTransfer_UpdatesVisitPreservesQueueNumberResetsCalledAndWritesAuditHistory()
    {
        var response = await _service.TransferToCompatibleRoomAsync(
            actorUserId: 5,
            idOrCode: "#vis-202611-0001",
            request: new TransferVisitRoomRequest(
                TargetRoomId: 2,
                TargetRoomCode: "rm-002",
                Reason: "Phòng RM-001 đang quá tải bệnh nhân chờ khám",
                Notes: "Điều phối sang phòng 102 cùng khoa Tiêu hóa",
                TargetDoctorId: 2),
            ipAddress: "127.0.0.1");

        Assert.Equal(100L, response.VisitId);
        Assert.Equal(1L, response.PreviousRoomId);
        Assert.Equal("RM-001", response.PreviousRoomCode);
        Assert.Equal(2L, response.CurrentRoomId);
        Assert.Equal("RM-002", response.CurrentRoomCode);
        Assert.Equal(1L, response.PreviousDoctorId);
        Assert.Equal(2L, response.AssignedDoctorId);
        Assert.Equal("DOC-002", response.AssignedDoctorCode);
        Assert.Equal("WAITING", response.VisitStatus);
        Assert.Equal(15, response.QueueNumber);
        Assert.Equal("WAITING", response.QueueStatus);
        Assert.Equal("STF-003", response.TransferredByStaffCode);

        var historyItem = Assert.Single(response.TransferHistory);
        Assert.Equal("RM-001", historyItem.FromRoomCode);
        Assert.Equal("RM-002", historyItem.ToRoomCode);
        Assert.Equal("Phòng RM-001 đang quá tải bệnh nhân chờ khám", historyItem.Reason);
        Assert.Equal("Điều phối sang phòng 102 cùng khoa Tiêu hóa", historyItem.Notes);

        var updatedVisit = await _db.Visits.FirstAsync(v => v.Id == 100);
        Assert.Equal(2L, updatedVisit.ExaminationRoomId);
        Assert.Equal(2L, updatedVisit.AssignedDoctorId);
        Assert.Equal("WAITING", updatedVisit.Status);

        var updatedQueue = await _db.QueueEntries.FirstAsync(q => q.Id == 500);
        Assert.Equal(15, updatedQueue.QueueNumber);
        Assert.Equal("WAITING", updatedQueue.Status);
        Assert.Null(updatedQueue.CalledAt);
    }

    [Fact]
    public async Task TransferToCompatibleRoomAsync_MultiHopTransfer_MaintainsOrderedTransferHistory()
    {
        await _service.TransferToCompatibleRoomAsync(
            actorUserId: 5,
            idOrCode: "100",
            request: new TransferVisitRoomRequest(
                TargetRoomCode: "RM-002",
                Reason: "Chuyển lần 1 sang RM-002"),
            ipAddress: "10.0.0.1");

        var secondTransfer = await _service.TransferToCompatibleRoomAsync(
            actorUserId: 11,
            idOrCode: "100",
            request: new TransferVisitRoomRequest(
                TargetRoomId: 1,
                Reason: "Chuyển lần 2 quay lại RM-001",
                Notes: "Bác sĩ phòng 101 đã sẵn sàng"),
            ipAddress: "10.0.0.2");

        Assert.Equal(1L, secondTransfer.CurrentRoomId);
        Assert.Equal("RM-001", secondTransfer.CurrentRoomCode);
        Assert.Equal(2, secondTransfer.TransferHistory.Count);

        Assert.Equal("RM-002", secondTransfer.TransferHistory[0].FromRoomCode);
        Assert.Equal("RM-001", secondTransfer.TransferHistory[0].ToRoomCode);
        Assert.Equal("Chuyển lần 2 quay lại RM-001", secondTransfer.TransferHistory[0].Reason);

        Assert.Equal("RM-001", secondTransfer.TransferHistory[1].FromRoomCode);
        Assert.Equal("RM-002", secondTransfer.TransferHistory[1].ToRoomCode);
        Assert.Equal("Chuyển lần 1 sang RM-002", secondTransfer.TransferHistory[1].Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("bad@code")]
    public async Task TransferToCompatibleRoomAsync_InvalidVisitIdentifier_ThrowsValidationException(string invalidId)
    {
        var req = new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Lý do hợp lệ");

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(5, invalidId, req, null));
    }

    [Fact]
    public async Task TransferToCompatibleRoomAsync_InvalidRequestFields_ThrowsValidationException()
    {
        // Missing target room
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(Reason: "Có lý do"),
                null));

        // Mismatched TargetRoomId and TargetRoomCode
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 2, TargetRoomCode: "RM-003", Reason: "Có lý do"),
                null));

        // Missing Reason
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "   "),
                null));

        // Reason > 500 chars
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: new string('A', 501)),
                null));

        // Notes > 500 chars
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Lý do", Notes: new string('B', 501)),
                null));

        // Same room (RM-001 -> RM-001)
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 1, Reason: "Trùng phòng hiện tại"),
                null));

        // Diagnostic room (RM-005)
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 5, Reason: "Phòng CLS không hợp lệ"),
                null));

        // Incompatible department room (RM-003 in Cardiology while visit is Gastroenterology)
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 3, Reason: "Khác chuyên khoa"),
                null));
    }

    [Fact]
    public async Task TransferToCompatibleRoomAsync_StateAndAvailabilityConflicts_ThrowsConflictException()
    {
        // Completed visit (102)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "102",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Lượt khám đã hoàn tất"),
                null));

        // In-consultation visit (103)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "103",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Đang khám"),
                null));

        // Unassigned initial room visit (104)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "104",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Chưa gán phòng ban đầu"),
                null));

        // Target room in MAINTENANCE (RM-006)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 6, Reason: "Phòng đang bảo trì"),
                null));

        // Target room at full capacity (RM-007)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 7, Reason: "Phòng đã đầy"),
                null));

        // Queue entry already started consultation
        var queue = await _db.QueueEntries.FirstAsync(q => q.Id == 500);
        queue.StartedAt = new DateTime(2026, 11, 1, 9, 1, 0);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Hàng đợi đã bắt đầu khám"),
                null));
    }

    [Fact]
    public async Task AuthorizationAndHospitalOwnership_EnforcesTwoLayerChecks()
    {
        var validReq = new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Điều phối");

        // Invalid actorUserId -> 401 UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetCompatibleRoomsAsync(0, "100"));

        // Non-existent user -> 404 NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetCompatibleRoomsAsync(999, "100"));

        // Disallowed roles -> 403 ForbiddenException
        foreach (var disallowedUserId in new[] { 1L, 3L, 7L, 9L })
        {
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                _service.TransferToCompatibleRoomAsync(disallowedUserId, "100", validReq, null));
        }

        // Inactive user / locked user / inactive staff / staff without hospital -> 403 ForbiddenException
        foreach (var blockedUserId in new[] { 20L, 21L, 22L, 23L })
        {
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                _service.TransferToCompatibleRoomAsync(blockedUserId, "100", validReq, null));
        }

        // Reception in Hospital 1 trying to access or transfer Visit 105 in Hospital 2 -> 403 ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetCompatibleRoomsAsync(5, "105"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "105",
                new TransferVisitRoomRequest(TargetRoomId: 10, Reason: "Khác bệnh viện"),
                null));

        // SYSTEM_ADMIN (userId: 13) can transfer Visit 105 in Hospital 2 between compatible rooms in Hospital 2
        var sysAdminResult = await _service.TransferToCompatibleRoomAsync(
            13,
            "105",
            new TransferVisitRoomRequest(TargetRoomId: 10, Reason: "QTHT điều phối tại CS2"),
            null);

        Assert.Equal(10L, sysAdminResult.CurrentRoomId);
        Assert.Equal("RM-202", sysAdminResult.CurrentRoomCode);
    }

    [Fact]
    public async Task TransferToCompatibleRoomAsync_TargetDoctorValidation_ChecksDepartmentStatusAndAuthorization()
    {
        // Non-existent doctor -> 404
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Đổi BS", TargetDoctorId: 999),
                null));

        // Doctor from another hospital -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Đổi BS", TargetDoctorId: 4),
                null));

        // Inactive doctor -> 409
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Đổi BS", TargetDoctorId: 5),
                null));

        // Doctor in different department (DOC-003 in Cardiology) -> 400
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.TransferToCompatibleRoomAsync(
                5,
                "100",
                new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Đổi BS", TargetDoctorId: 3),
                null));
    }
}
