using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Visits;

public sealed class VisitRoomAssignmentServiceTests : IDisposable
{
    private sealed class VisitRoutingTestDbContext : DbContext, IApplicationDbContext
    {
        public VisitRoutingTestDbContext(DbContextOptions<VisitRoutingTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for VisitRoomAssignmentService unit tests.");

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
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => throw NotUsed();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();

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
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
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
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
        }
    }

    private readonly VisitRoutingTestDbContext _db;
    private readonly VisitRoomAssignmentService _service;

    public VisitRoomAssignmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<VisitRoutingTestDbContext>()
            .UseInMemoryDatabase($"VisitRoomAssignmentTests_{Guid.NewGuid():N}")
            .Options;

        _db = new VisitRoutingTestDbContext(options);
        _service = new VisitRoomAssignmentService(_db, TimeProvider.System);

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
            new RolePermission { RoleId = 6, PermissionId = 4 });

        _db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện MedFlow 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện MedFlow 2", IsActive = true });

        _db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "OPD", Name = "Khoa Khám bệnh", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "GI", Name = "Khoa Tiêu hóa", IsActive = true },
            new Department { Id = 3, HospitalId = 1, DepartmentCode = "CARD", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 4, HospitalId = 1, DepartmentCode = "DIA", Name = "Khoa Cận lâm sàng", IsActive = true },
            new Department { Id = 20, HospitalId = 2, DepartmentCode = "GI-2", Name = "Khoa Tiêu hóa BV2", IsActive = true });

        _db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true });

        _db.Users.AddRange(
            new User { Id = 1, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 5, Email = "reception01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "system_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 25, Email = "reception_hos2@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 5, RoleId = 3 },
            new UserRole { UserId = 11, RoleId = 6 },
            new UserRole { UserId = 13, RoleId = 7 },
            new UserRole { UserId = 25, RoleId = 3 });

        _db.Staff.AddRange(
            new Staff { Id = 1, UserId = 3, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-001", FullName = "BS Nguyễn Minh Đức", Status = "ACTIVE" },
            new Staff { Id = 2, UserId = null, HospitalId = 1, DepartmentId = 3, StaffCode = "STF-002", FullName = "BS Trần Thu Hà", Status = "ACTIVE" },
            new Staff { Id = 3, UserId = 5, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-003", FullName = "Lễ tân Lê Lan", Status = "ACTIVE" },
            new Staff { Id = 9, UserId = 11, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-009", FullName = "QLBV Phan Bình", Status = "ACTIVE" },
            new Staff { Id = 11, UserId = 13, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-011", FullName = "QTHT Lưu Huy", Status = "ACTIVE" },
            new Staff { Id = 20, UserId = 25, HospitalId = 2, DepartmentId = 20, StaffCode = "STF-020", FullName = "Lễ tân BV2", Status = "ACTIVE" });

        _db.Doctors.AddRange(
            new Doctor { Id = 1, StaffId = 1, HospitalId = 1, DoctorCode = "DOC-001", SpecialtyId = 1, ProfessionalTitle = "BS.CKI", IsActive = true },
            new Doctor { Id = 2, StaffId = 2, HospitalId = 1, DoctorCode = "DOC-002", SpecialtyId = 2, ProfessionalTitle = "ThS.BS", IsActive = true });

        _db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 1, HospitalId = 1, DepartmentId = 2, RoomCode = "RM-001", Name = "Khám Tiêu hóa 101", RoomType = "EXAMINATION", Location = "Tầng 1", Capacity = 2, Status = "ACTIVE" },
            new HospitalRoom { Id = 2, HospitalId = 1, DepartmentId = 2, RoomCode = "RM-002", Name = "Khám Tiêu hóa 102", RoomType = "EXAMINATION", Location = "Tầng 1", Capacity = 1, Status = "ACTIVE" },
            new HospitalRoom { Id = 3, HospitalId = 1, DepartmentId = 3, RoomCode = "RM-003", Name = "Khám Tim mạch 201", RoomType = "EXAMINATION", Location = "Tầng 2", Capacity = 2, Status = "ACTIVE" },
            new HospitalRoom { Id = 5, HospitalId = 1, DepartmentId = 4, RoomCode = "RM-005", Name = "Phòng Cận lâm sàng 205", RoomType = "DIAGNOSTIC", Location = "Tầng 2", Capacity = 1, Status = "ACTIVE" },
            new HospitalRoom { Id = 6, HospitalId = 1, DepartmentId = 2, RoomCode = "RM-006", Name = "Khám Tiêu hóa Bảo trì", RoomType = "EXAMINATION", Location = "Tầng 1", Capacity = 2, Status = "MAINTENANCE" },
            new HospitalRoom { Id = 20, HospitalId = 2, DepartmentId = 20, RoomCode = "RM-201", Name = "Khám Tiêu hóa BV2", RoomType = "EXAMINATION", Location = "Tầng 1 BV2", Capacity = 2, Status = "ACTIVE" });

        _db.Services.AddRange(
            new Service { Id = 1, HospitalId = 1, DepartmentId = 2, SpecialtyId = 1, ServiceCode = "EX-GI-01", Name = "Khám Tiêu hóa lần đầu", ServiceType = "EXAMINATION", IsActive = true },
            new Service { Id = 2, HospitalId = 1, DepartmentId = 3, SpecialtyId = 2, ServiceCode = "EX-CARD-01", Name = "Khám Tim mạch lần đầu", ServiceType = "EXAMINATION", IsActive = true });

        _db.Patients.AddRange(
            new Patient { Id = 1, UserId = 1, PatientCode = "PAT-0001", FullName = "Nguyễn Văn An", Status = "ACTIVE" },
            new Patient { Id = 2, UserId = null, PatientCode = "PAT-0002", FullName = "Trần Thị Bình", Status = "ACTIVE" });

        _db.ExaminationSlots.Add(
            new ExaminationSlot
            {
                Id = 10,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 27, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 27, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });

        _db.Appointments.Add(
            new Appointment
            {
                Id = 100,
                AppointmentCode = "APT-202610-0100",
                PatientId = 1,
                SlotId = 10,
                BookingChannel = "ONLINE",
                Status = "CHECKED_IN",
            });

        _db.QueueServicePoints.AddRange(
            new QueueServicePoint { Id = 1, HospitalId = 1, RoomId = 1, DoctorId = 1, ServiceId = 1, PointType = "DOCTOR", Name = "Hàng đợi bác sĩ 1 - Phòng 101", IsActive = true },
            new QueueServicePoint { Id = 2, HospitalId = 1, RoomId = 2, DoctorId = 1, ServiceId = 1, PointType = "DOCTOR", Name = "Hàng đợi bác sĩ 1 - Phòng 102", IsActive = true });

        _db.Visits.AddRange(
            new Visit
            {
                Id = 100,
                VisitCode = "VIS-202610-0100",
                PatientId = 1,
                AppointmentId = 100,
                AssignedDoctorId = 1,
                ExaminationRoomId = null,
                CheckedInByStaffId = null,
                ReasonForVisit = "Đau thượng vị",
                Status = "CHECKED_IN",
                CheckedInAt = new DateTime(2026, 10, 27, 8, 50, 0),
            },
            new Visit
            {
                Id = 101,
                VisitCode = "VIS-202610-0101",
                PatientId = 2,
                AppointmentId = 100,
                AssignedDoctorId = null, // Will be resolved from Appointment.Slot.DoctorId = 1
                ExaminationRoomId = null,
                Status = "CHECKED_IN",
            },
            new Visit
            {
                Id = 102,
                VisitCode = "VIS-202610-0102",
                PatientId = 2,
                AppointmentId = null,
                AssignedDoctorId = null, // Walk-in without assigned doctor -> UC26 conflict
                ExaminationRoomId = null,
                Status = "CHECKED_IN",
            },
            new Visit
            {
                Id = 103,
                VisitCode = "VIS-202610-0103",
                PatientId = 1,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                Status = "COMPLETED",
                CompletedAt = new DateTime(2026, 10, 25, 11, 0, 0),
            },
            new Visit
            {
                Id = 104,
                VisitCode = "VIS-202610-0104",
                PatientId = 1,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                Status = "IN_CONSULTATION",
            });

        _db.QueueEntries.Add(
            new QueueEntry
            {
                Id = 500,
                VisitId = null,
                AppointmentId = 100,
                ServicePointId = 1,
                CurrentServicePointId = null,
                QueueDate = new DateOnly(2026, 10, 27),
                QueueNumber = 3,
                QueueType = "EXAMINATION",
                Status = "RESERVED",
                PresenceStatus = "NOT_ARRIVED",
                ManualPriority = "NORMAL",
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task AssignExaminationRoomAsync_ValidCheckedInVisit_UpdatesVisitQueueEntryAndWritesAuditLog()
    {
        var request = new AssignExaminationRoomRequest(
            ExaminationRoomId: 1,
            Priority: "PREGNANT",
            Reason: "Điều phối bệnh nhân đau thượng vị vào buồng khám 101",
            PriorityVerificationConfirmed: true);

        var result = await _service.AssignExaminationRoomAsync(
            actorUserId: 5,
            visitIdOrCode: "VIS-202610-0100",
            request: request,
            ipAddress: "127.0.0.1");

        Assert.Equal(100L, result.VisitId);
        Assert.Equal("VIS-202610-0100", result.VisitCode);
        Assert.Equal(1L, result.ExaminationRoomId);
        Assert.Equal("RM-001", result.RoomCode);
        Assert.Equal("Khám Tiêu hóa 101", result.RoomName);
        Assert.Equal("WAITING", result.VisitStatus);
        Assert.Equal(1L, result.AssignedDoctorId);
        Assert.Equal("DOC-001", result.AssignedDoctorCode);
        Assert.Equal(3L, result.CheckedInByStaffId);
        Assert.Equal(500L, result.QueueEntryId);
        Assert.Equal(3, result.QueueNumber);
        Assert.Equal("B003", result.QueueCode);
        Assert.Equal("WAITING", result.QueueStatus);
        Assert.Equal("PREGNANT", result.Priority);
        Assert.Equal(1L, result.ServicePointId);
        Assert.Equal(2, result.CurrentRoomOccupancy); // Visit 104 (IN_CONSULTATION) + Visit 100 (WAITING)

        var updatedVisit = await _db.Visits.AsNoTracking().SingleAsync(v => v.Id == 100);
        Assert.Equal(1L, updatedVisit.ExaminationRoomId);
        Assert.Equal("WAITING", updatedVisit.Status);
        Assert.Equal(3L, updatedVisit.CheckedInByStaffId);

        var updatedQueue = await _db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == 500);
        Assert.Equal(100L, updatedQueue.VisitId);
        Assert.Equal("WAITING", updatedQueue.Status);
        Assert.Equal("PRESENT", updatedQueue.PresenceStatus);
        Assert.Equal("PREGNANT", updatedQueue.ManualPriority);
        Assert.Equal(1L, updatedQueue.CurrentServicePointId);
        Assert.NotNull(updatedQueue.ReadyAt);

        var audit = await _db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == VisitRoomAssignmentService.VisitRoomAssignedAction && a.EntityId == "100");
        Assert.Equal(5L, audit.ActorUserId);
        Assert.Equal(1L, audit.HospitalId);
        Assert.Equal("Điều phối bệnh nhân đau thượng vị vào buồng khám 101", audit.Reason);
    }

    [Fact]
    public async Task AssignExaminationRoomAsync_VisitWithSlotDoctorOnly_AutoPopulatesAssignedDoctorAndCreatesQueueEntry()
    {
        var walkInVisitWithDoctor = new Visit
        {
            Id = 110,
            VisitCode = "VIS-202610-0110",
            PatientId = 2,
            AppointmentId = null,
            AssignedDoctorId = 1,
            Status = "CHECKED_IN",
        };
        _db.Visits.Add(walkInVisitWithDoctor);
        await _db.SaveChangesAsync();

        var slotBackedResult = await _service.AssignExaminationRoomAsync(
            actorUserId: 5,
            visitIdOrCode: "101",
            request: new AssignExaminationRoomRequest(
                RoomCode: "rm-002",
                Priority: "ELDERLY",
                Reason: "Người bệnh cao tuổi ≥ 75 tuổi",
                PriorityVerificationConfirmed: true));

        Assert.Equal(101L, slotBackedResult.VisitId);
        Assert.Equal(1L, slotBackedResult.AssignedDoctorId);
        Assert.Equal(2L, slotBackedResult.ExaminationRoomId);
        Assert.Equal("RM-002", slotBackedResult.RoomCode);
        Assert.Equal("ELDERLY", slotBackedResult.Priority);

        var walkInResult = await _service.AssignExaminationRoomAsync(
            actorUserId: 11,
            visitIdOrCode: "VIS-202610-0110",
            request: new AssignExaminationRoomRequest(RoomId: 1));

        Assert.Equal(110L, walkInResult.VisitId);
        Assert.Equal(1L, walkInResult.ExaminationRoomId);
        Assert.NotNull(walkInResult.QueueEntryId);
        Assert.Equal("NORMAL", walkInResult.Priority);
        Assert.Equal("WAITING", walkInResult.QueueStatus);
    }

    [Fact]
    public async Task AssignExaminationRoomAsync_WalkInWithoutAssignedDoctor_ThrowsConflictExceptionForMissingUc26()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 5,
                visitIdOrCode: "VIS-202610-0102",
                request: new AssignExaminationRoomRequest(ExaminationRoomId: 1)));

        Assert.Contains("UC26", ex.Message);
    }

    [Fact]
    public async Task AssignExaminationRoomAsync_IncompatibleDepartmentOrDiagnosticRoom_ThrowsValidationException()
    {
        // RM-003 is Cardiology (Department 3), while Visit 100 is GI (Department 2)
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 5,
                visitIdOrCode: "VIS-202610-0100",
                request: new AssignExaminationRoomRequest(RoomCode: "RM-003")));

        // RM-005 is DIAGNOSTIC room type
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 5,
                visitIdOrCode: "VIS-202610-0100",
                request: new AssignExaminationRoomRequest(RoomCode: "RM-005")));
    }

    [Fact]
    public async Task AssignExaminationRoomAsync_RoomInMaintenanceOrInactiveDepartment_ThrowsConflictException()
    {
        // RM-006 is in MAINTENANCE status
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 5,
                visitIdOrCode: "VIS-202610-0100",
                request: new AssignExaminationRoomRequest(RoomCode: "RM-006")));
    }

    [Fact]
    public async Task AssignExaminationRoomAsync_RoomAtFullCapacity_ThrowsConflictException_WhileSameVisitReassignmentIsIdempotent()
    {
        // RM-002 has Capacity = 1. Assign Visit 100 to RM-002 first:
        var firstAssign = await _service.AssignExaminationRoomAsync(
            actorUserId: 5,
            visitIdOrCode: "VIS-202610-0100",
            request: new AssignExaminationRoomRequest(RoomCode: "RM-002"));

        Assert.Equal(2L, firstAssign.ExaminationRoomId);
        Assert.Equal(1, firstAssign.CurrentRoomOccupancy);

        // Re-assigning the same Visit 100 to RM-002 (e.g., updating priority) succeeds without double-counting:
        var idempotentReassign = await _service.AssignExaminationRoomAsync(
            actorUserId: 5,
            visitIdOrCode: "VIS-202610-0100",
            request: new AssignExaminationRoomRequest(
                RoomCode: "RM-002",
                Priority: "PREGNANT",
                Reason: "Thai phụ có sổ khám thai",
                PriorityVerificationConfirmed: true));

        Assert.Equal("PREGNANT", idempotentReassign.Priority);
        Assert.Equal(1, idempotentReassign.CurrentRoomOccupancy);

        // Assigning a second distinct visit (101) to RM-002 fails because capacity (1) is full:
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 5,
                visitIdOrCode: "VIS-202610-0101",
                request: new AssignExaminationRoomRequest(RoomCode: "RM-002")));
    }

    [Fact]
    public async Task AssignExaminationRoomAsync_CompletedOrInConsultationVisit_ThrowsConflictException()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 5,
                visitIdOrCode: "VIS-202610-0103",
                request: new AssignExaminationRoomRequest(ExaminationRoomId: 1)));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 5,
                visitIdOrCode: "VIS-202610-0104",
                request: new AssignExaminationRoomRequest(ExaminationRoomId: 1)));
    }

    [Fact]
    public async Task AssignExaminationRoomAsync_UnauthorizedRoleOrCrossHospitalActor_ThrowsForbiddenException()
    {
        // Patient (UserId = 1) or Doctor (UserId = 3) cannot assign rooms
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 1,
                visitIdOrCode: "VIS-202610-0100",
                request: new AssignExaminationRoomRequest(ExaminationRoomId: 1)));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 3,
                visitIdOrCode: "VIS-202610-0100",
                request: new AssignExaminationRoomRequest(ExaminationRoomId: 1)));

        // Receptionist of Hospital 2 (UserId = 25) cannot assign a visit in Hospital 1
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 25,
                visitIdOrCode: "VIS-202610-0100",
                request: new AssignExaminationRoomRequest(ExaminationRoomId: 1)));

        // Receptionist of Hospital 1 (UserId = 5) cannot assign Room 20 (belonging to Hospital 2)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.AssignExaminationRoomAsync(
                actorUserId: 5,
                visitIdOrCode: "VIS-202610-0100",
                request: new AssignExaminationRoomRequest(ExaminationRoomId: 20)));
    }

    [Fact]
    public async Task GetCompatibleRoomsAsync_ReturnsHospitalExaminationRoomsWithCompatibilityAndAvailabilityFlags()
    {
        var response = await _service.GetCompatibleRoomsAsync(
            actorUserId: 5,
            visitIdOrCode: "VIS-202610-0100");

        Assert.Equal(100L, response.VisitId);
        Assert.Equal(2L, response.RequiredDepartmentId);
        Assert.Equal(4, response.Rooms.Count); // RM-001, RM-002, RM-003, RM-006 (excludes DIAGNOSTIC RM-005 and Hospital 2 RM-201)

        var rm001 = Assert.Single(response.Rooms, r => r.RoomCode == "RM-001");
        Assert.True(rm001.IsCompatible);
        Assert.True(rm001.IsAvailable);
        Assert.Equal(1, rm001.CurrentOccupancy); // Visit 104 is IN_CONSULTATION in RM-001
        Assert.Equal(1, rm001.AvailableCapacity);

        var rm003 = Assert.Single(response.Rooms, r => r.RoomCode == "RM-003");
        Assert.False(rm003.IsCompatible);
        Assert.False(rm003.IsAvailable);

        var rm006 = Assert.Single(response.Rooms, r => r.RoomCode == "RM-006");
        Assert.True(rm006.IsCompatible);
        Assert.False(rm006.IsAvailable);
    }
}
