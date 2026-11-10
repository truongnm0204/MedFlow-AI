using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Visits;

public sealed class VisitDoctorAssignmentServiceTests : IDisposable
{
    private sealed class VisitDoctorAssignmentTestDbContext : DbContext, IApplicationDbContext
    {
        public VisitDoctorAssignmentTestDbContext(DbContextOptions<VisitDoctorAssignmentTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for VisitDoctorAssignmentService unit tests.");

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
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
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
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany().HasForeignKey(s => s.SpecialtyId);
            });
            modelBuilder.Entity<ServicePrice>().HasKey(sp => sp.Id);
            modelBuilder.Entity<DoctorServiceAuthorization>().HasKey(a => new { a.DoctorId, a.ServiceId });
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

    private readonly VisitDoctorAssignmentTestDbContext _db;
    private readonly VisitDoctorAssignmentService _service;

    public VisitDoctorAssignmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<VisitDoctorAssignmentTestDbContext>()
            .UseInMemoryDatabase($"VisitDoctorAssignmentTests_{Guid.NewGuid():N}")
            .Options;

        _db = new VisitDoctorAssignmentTestDbContext(options);
        _service = new VisitDoctorAssignmentService(_db, TimeProvider.System);

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
            new Staff { Id = 4, UserId = null, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-004", FullName = "BS.CKI Trần Quốc Tuấn", Status = "ACTIVE" },
            new Staff { Id = 5, UserId = null, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-005", FullName = "BS Phạm Minh Trí", Status = "INACTIVE" },
            new Staff { Id = 9, UserId = 11, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-009", FullName = "QLBV Phan Bình", Status = "ACTIVE" },
            new Staff { Id = 11, UserId = 13, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-011", FullName = "QTHT Lưu Huy", Status = "ACTIVE" },
            new Staff { Id = 20, UserId = 25, HospitalId = 2, DepartmentId = 20, StaffCode = "STF-020", FullName = "Lễ tân BV2", Status = "ACTIVE" },
            new Staff { Id = 21, UserId = null, HospitalId = 2, DepartmentId = 20, StaffCode = "STF-021", FullName = "BS BV2", Status = "ACTIVE" });

        _db.Doctors.AddRange(
            new Doctor { Id = 1, StaffId = 1, HospitalId = 1, DoctorCode = "DOC-001", SpecialtyId = 1, ProfessionalTitle = "BS.CKI", IsActive = true },
            new Doctor { Id = 2, StaffId = 2, HospitalId = 1, DoctorCode = "DOC-002", SpecialtyId = 2, ProfessionalTitle = "ThS.BS", IsActive = true },
            new Doctor { Id = 3, StaffId = 4, HospitalId = 1, DoctorCode = "DOC-003", SpecialtyId = 1, ProfessionalTitle = "BS.CKI", IsActive = true },
            new Doctor { Id = 4, StaffId = 5, HospitalId = 1, DoctorCode = "DOC-004", SpecialtyId = 1, ProfessionalTitle = "BS.CKI", IsActive = false },
            new Doctor { Id = 20, StaffId = 21, HospitalId = 2, DoctorCode = "DOC-020", SpecialtyId = 1, ProfessionalTitle = "BS.CKI", IsActive = true });

        _db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 1, HospitalId = 1, DepartmentId = 2, RoomCode = "RM-001", Name = "Khám Tiêu hóa 101", RoomType = "EXAMINATION", Location = "Tầng 1", Capacity = 2, Status = "ACTIVE" },
            new HospitalRoom { Id = 2, HospitalId = 1, DepartmentId = 2, RoomCode = "RM-002", Name = "Khám Tiêu hóa 102", RoomType = "EXAMINATION", Location = "Tầng 1", Capacity = 2, Status = "ACTIVE" },
            new HospitalRoom { Id = 3, HospitalId = 1, DepartmentId = 3, RoomCode = "RM-003", Name = "Khám Tim mạch 201", RoomType = "EXAMINATION", Location = "Tầng 2", Capacity = 2, Status = "ACTIVE" },
            new HospitalRoom { Id = 5, HospitalId = 1, DepartmentId = 4, RoomCode = "RM-005", Name = "Phòng Cận lâm sàng 205", RoomType = "DIAGNOSTIC", Location = "Tầng 2", Capacity = 1, Status = "ACTIVE" },
            new HospitalRoom { Id = 20, HospitalId = 2, DepartmentId = 20, RoomCode = "RM-201", Name = "Khám Tiêu hóa BV2", RoomType = "EXAMINATION", Location = "Tầng 1 BV2", Capacity = 2, Status = "ACTIVE" });

        _db.Services.AddRange(
            new Service { Id = 1, HospitalId = 1, DepartmentId = 2, SpecialtyId = 1, ServiceCode = "EX-GI-01", Name = "Khám Tiêu hóa lần đầu", ServiceType = "EXAMINATION", IsActive = true },
            new Service { Id = 2, HospitalId = 1, DepartmentId = 3, SpecialtyId = 2, ServiceCode = "EX-CARD-01", Name = "Khám Tim mạch lần đầu", ServiceType = "EXAMINATION", IsActive = true },
            new Service { Id = 3, HospitalId = 1, DepartmentId = 2, SpecialtyId = 1, ServiceCode = "EX-GI-02", Name = "Khám Tiêu hóa chuyên sâu", ServiceType = "EXAMINATION", IsActive = true });

        _db.ServicePrices.AddRange(
            new ServicePrice { Id = 1, ServiceId = 1, PriceAmount = 250000m, EffectiveFrom = new DateTime(2026, 1, 1) },
            new ServicePrice { Id = 2, ServiceId = 2, PriceAmount = 300000m, EffectiveFrom = new DateTime(2026, 1, 1) },
            new ServicePrice { Id = 3, ServiceId = 3, PriceAmount = 350000m, EffectiveFrom = new DateTime(2026, 1, 1) });

        _db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 1, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 3, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 3, ServiceId = 1, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 3, ServiceId = 3, IsActive = false },
            new DoctorServiceAuthorization { DoctorId = 2, ServiceId = 2, IsActive = true });

        _db.QueueServicePoints.AddRange(
            new QueueServicePoint { Id = 1, HospitalId = 1, RoomId = 1, DoctorId = 1, ServiceId = 1, PointType = "DOCTOR", Name = "Hàng đợi BS 1 - Phòng 101", IsActive = true },
            new QueueServicePoint { Id = 2, HospitalId = 1, RoomId = 3, DoctorId = 2, ServiceId = 2, PointType = "DOCTOR", Name = "Hàng đợi BS 2 - Phòng 201", IsActive = true },
            new QueueServicePoint { Id = 3, HospitalId = 1, RoomId = 2, DoctorId = 3, ServiceId = 1, PointType = "DOCTOR", Name = "Hàng đợi BS 3 - Phòng 102", IsActive = true });

        _db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                UserId = 1,
                PatientCode = "PAT-0001",
                FullName = "Nguyễn Văn An",
                Gender = "MALE",
                DateOfBirth = new DateOnly(1985, 5, 10),
                IdentificationHash = "d4dac994f8bef67d90e2466997300b8e6e35901a751d0a8df2c080fe47836236",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 2,
                PatientCode = "PAT-0002",
                FullName = "Trần Thị Bình",
                Gender = "FEMALE",
                DateOfBirth = new DateOnly(1950, 2, 1),
                IdentificationHash = null,
                Status = "ACTIVE",
            });

        _db.ExaminationSlots.Add(
            new ExaminationSlot
            {
                Id = 10,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = DateTime.UtcNow.AddHours(1),
                EndsAt = DateTime.UtcNow.AddHours(2),
                Capacity = 5,
                Status = "OPEN",
            });

        _db.Appointments.Add(
            new Appointment
            {
                Id = 100,
                AppointmentCode = "APT-202610-0100",
                PatientId = 1,
                SlotId = 10,
                BookingChannel = "COUNTER",
                Status = "CHECKED_IN",
            });

        _db.Visits.AddRange(
            new Visit
            {
                Id = 100,
                VisitCode = "VIS-202610-0100",
                PatientId = 1,
                AppointmentId = 100,
                AssignedDoctorId = null,
                ExaminationRoomId = null,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Đau vùng thượng vị",
                Status = "CHECKED_IN",
                CheckedInAt = DateTime.UtcNow.AddMinutes(-10),
            },
            new Visit
            {
                Id = 101,
                VisitCode = "VIS-202610-0101",
                PatientId = 2,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám tiêu hóa định kỳ",
                Status = "WAITING",
                CheckedInAt = DateTime.UtcNow.AddMinutes(-20),
            },
            new Visit
            {
                Id = 102,
                VisitCode = "VIS-202610-0102",
                PatientId = 1,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Đã hoàn tất",
                Status = "COMPLETED",
                CheckedInAt = DateTime.UtcNow.AddHours(-2),
                CompletedAt = DateTime.UtcNow.AddHours(-1),
            },
            new Visit
            {
                Id = 103,
                VisitCode = "VIS-202610-0103",
                PatientId = 1,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Đang khám",
                Status = "IN_PROGRESS",
                CheckedInAt = DateTime.UtcNow.AddMinutes(-30),
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetAssignableDoctorsAsync_ReturnsPatientSummaryAndWorkloadOrderedDoctors()
    {
        // Add 4 active waiting visits for DOC-001 (MEDIUM workload) and 1 for DOC-003 (LOW workload)
        for (var i = 1; i <= 4; i++)
        {
            var visitId = 200L + i;
            _db.Visits.Add(new Visit
            {
                Id = visitId,
                VisitCode = $"VIS-LOAD-{visitId}",
                PatientId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                Status = "WAITING",
            });
            _db.QueueEntries.Add(new QueueEntry
            {
                Id = visitId,
                VisitId = visitId,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueNumber = i,
                QueueType = "EXAMINATION",
                Status = i == 1 ? "CALLED" : "WAITING",
                PresenceStatus = "PRESENT",
            });
        }

        await _db.SaveChangesAsync();

        var result = await _service.GetAssignableDoctorsAsync(5L, "VIS-202610-0100");

        Assert.Equal(100L, result.VisitId);
        Assert.Equal("VIS-202610-0100", result.VisitCode);
        Assert.Equal("EX-GI-01", result.RequiredServiceCode);
        Assert.Equal(2L, result.RequiredDepartmentId);

        // Patient summary assertions
        Assert.Equal("PAT-0001", result.Patient.PatientCode);
        Assert.Equal("Nguyễn Văn An", result.Patient.FullName);
        Assert.Equal("Nam", result.Patient.Gender);
        Assert.True(result.Patient.CccdVerified);
        Assert.Equal(250000m, result.Patient.Price);

        // Candidate doctors: DOC-003 (0 waiting, LOW) should rank before DOC-001 (5 waiting incl. visit 101, MEDIUM)
        Assert.Equal(4, result.Doctors.Count);
        var first = result.Doctors[0];
        Assert.Equal("DOC-003", first.DoctorCode);
        Assert.True(first.IsAvailable);
        Assert.True(first.IsCompatible);
        Assert.Equal(0, first.WaitingCount);
        Assert.Equal("LOW", first.WorkloadLevel);

        var second = result.Doctors[1];
        Assert.Equal("DOC-001", second.DoctorCode);
        Assert.True(second.IsAvailable);
        Assert.Equal(5, second.WaitingCount);
        Assert.Equal("MEDIUM", second.WorkloadLevel);
        Assert.Equal("001", second.CurrentTicket);

        // DOC-002 (Cardiology) is incompatible with GI service
        var cardDoc = Assert.Single(result.Doctors, d => d.DoctorCode == "DOC-002");
        Assert.False(cardDoc.IsCompatible);
        Assert.False(cardDoc.IsAvailable);
        Assert.NotNull(cardDoc.UnavailableReason);

        // DOC-004 is inactive
        var inactiveDoc = Assert.Single(result.Doctors, d => d.DoctorCode == "DOC-004");
        Assert.False(inactiveDoc.IsAvailable);
        Assert.Contains("tạm ngưng", inactiveDoc.UnavailableReason);
    }

    [Fact]
    public async Task AssignDoctorAsync_ValidDoctorForCheckedInVisit_PersistsDoctorAndAuditLog()
    {
        var response = await _service.AssignDoctorAsync(
            actorUserId: 5L,
            visitIdOrCode: "VIS-202610-0100",
            request: new AssignDoctorToVisitRequest(
                DoctorCode: "DOC-003",
                Reason: "Cân bằng tải sang BS Trần Quốc Tuấn"),
            ipAddress: "127.0.0.1");

        Assert.Equal(100L, response.VisitId);
        Assert.Equal(3L, response.AssignedDoctorId);
        Assert.Equal("DOC-003", response.AssignedDoctorCode);
        Assert.Equal("CHECKED_IN", response.VisitStatus);
        Assert.Null(response.ExaminationRoomId);
        Assert.Equal("LOW", response.WorkloadLevel);

        var updatedVisit = await _db.Visits.FindAsync(100L);
        Assert.NotNull(updatedVisit);
        Assert.Equal(3L, updatedVisit.AssignedDoctorId);

        var audit = await _db.AuditLogs
            .FirstOrDefaultAsync(a =>
                a.Action == VisitDoctorAssignmentService.VisitDoctorAssignedAction &&
                a.EntityId == "100");
        Assert.NotNull(audit);
        Assert.Equal(5L, audit.ActorUserId);
        Assert.Equal("Cân bằng tải sang BS Trần Quốc Tuấn", audit.Reason);
    }

    [Fact]
    public async Task AssignDoctorAsync_WithRoomAndElevatedPriority_TransitionsToWaitingAndCreatesQueueEntry()
    {
        // Visit 101 is for Patient 2 (born 1950 -> eligible for ELDERLY)
        var response = await _service.AssignDoctorAsync(
            actorUserId: 5L,
            visitIdOrCode: "VIS-202610-0101",
            request: new AssignDoctorToVisitRequest(
                DoctorId: 3L,
                RoomCode: "RM-002",
                Priority: "ELDERLY",
                Reason: "Chuyển sang BS Tuấn kèm ưu tiên người cao tuổi",
                PriorityVerificationConfirmed: true));

        Assert.Equal(101L, response.VisitId);
        Assert.Equal(1L, response.PreviousDoctorId);
        Assert.Equal(3L, response.AssignedDoctorId);
        Assert.Equal("DOC-003", response.AssignedDoctorCode);
        Assert.Equal(2L, response.ExaminationRoomId);
        Assert.Equal("RM-002", response.RoomCode);
        Assert.Equal("WAITING", response.VisitStatus);
        Assert.Equal("ELDERLY", response.Priority);
        Assert.Equal(3L, response.ServicePointId);
        Assert.NotNull(response.QueueEntryId);
        Assert.Equal(1, response.QueueNumber);
        Assert.Equal("B001", response.QueueCode);
    }

    [Fact]
    public async Task AssignDoctorAsync_ReassignSameDoctor_IsIdempotentAndPreservesQueueNumber()
    {
        var first = await _service.AssignDoctorAsync(
            5L,
            "VIS-202610-0101",
            new AssignDoctorToVisitRequest(DoctorId: 1L));

        var second = await _service.AssignDoctorAsync(
            5L,
            "VIS-202610-0101",
            new AssignDoctorToVisitRequest(DoctorCode: "DOC-001"));

        Assert.Equal(first.QueueEntryId, second.QueueEntryId);
        Assert.Equal(first.QueueNumber, second.QueueNumber);
        Assert.Equal(1L, second.AssignedDoctorId);
    }

    [Fact]
    public async Task AssignDoctorAsync_DoctorNotAuthorizedForService_ThrowsValidationException()
    {
        // Service 3 (EX-GI-02) has IsActive = false for DOC-003
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignDoctorAsync(
                5L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest(
                    DoctorCode: "DOC-003",
                    ServiceCode: "EX-GI-02")));
    }

    [Fact]
    public async Task AssignDoctorAsync_IncompatibleDepartmentDoctor_ThrowsValidationException()
    {
        // DOC-002 is Cardiology (Department 3), Visit 100 requires GI (Department 2)
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignDoctorAsync(
                5L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest(DoctorCode: "DOC-002")));
    }

    [Fact]
    public async Task AssignDoctorAsync_InactiveDoctor_ThrowsConflictException()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AssignDoctorAsync(
                5L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest(DoctorCode: "DOC-004")));
    }

    [Theory]
    [InlineData("VIS-202610-0102")] // COMPLETED
    [InlineData("VIS-202610-0103")] // IN_PROGRESS
    public async Task AssignDoctorAsync_TerminalOrInProgressVisit_ThrowsConflictException(string visitCode)
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.AssignDoctorAsync(
                5L,
                visitCode,
                new AssignDoctorToVisitRequest(DoctorId: 1L)));
    }

    [Fact]
    public async Task AssignDoctorAsync_DisallowedRoleOrCrossHospital_ThrowsForbiddenException()
    {
        // Patient role (UserId = 1)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.AssignDoctorAsync(
                1L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest(DoctorId: 1L)));

        // Receptionist from Hospital 2 (UserId = 25) accessing Hospital 1 visit
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.AssignDoctorAsync(
                25L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest(DoctorId: 1L)));

        // Receptionist from Hospital 1 assigning Doctor from Hospital 2 (DOC-020)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.AssignDoctorAsync(
                5L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest(DoctorCode: "DOC-020")));
    }

    [Theory]
    [InlineData(0, "LOW")]
    [InlineData(3, "LOW")]
    [InlineData(4, "MEDIUM")]
    [InlineData(6, "MEDIUM")]
    [InlineData(7, "HIGH")]
    [InlineData(12, "HIGH")]
    public void ClassifyWorkloadLevel_MapsWaitingCountsToExpectedTiers(int waitingCount, string expectedLevel)
    {
        Assert.Equal(expectedLevel, VisitDoctorAssignmentService.ClassifyWorkloadLevel(waitingCount));
    }

    [Fact]
    public async Task AssignDoctorAsync_CalledVisitWithDifferentDoctor_ResetsVisitAndQueueToWaiting()
    {
        var visit = await _db.Visits.FindAsync(101L);
        Assert.NotNull(visit);
        visit.Status = "CALLED";

        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 500,
            VisitId = 101,
            ServicePointId = 1,
            CurrentServicePointId = 1,
            QueueNumber = 9,
            QueueType = "EXAMINATION",
            Status = "CALLED",
            PresenceStatus = "PRESENT",
            CalledAt = DateTime.UtcNow.AddMinutes(-2),
        });
        await _db.SaveChangesAsync();

        var response = await _service.AssignDoctorAsync(
            5L,
            101L,
            new AssignDoctorToVisitRequest(DoctorCode: "DOC-003"));

        Assert.Equal("WAITING", response.VisitStatus);
        Assert.Equal("WAITING", response.QueueStatus);
        Assert.Equal(9, response.QueueNumber);

        var updatedQueue = await _db.QueueEntries.FindAsync(500L);
        Assert.NotNull(updatedQueue);
        Assert.Equal("WAITING", updatedQueue.Status);
        Assert.Null(updatedQueue.CalledAt);
        Assert.Equal(3L, updatedQueue.CurrentServicePointId);
    }

    [Fact]
    public async Task AssignDoctorAsync_InvalidInputsOrNotFound_ThrowsExpectedDomainExceptions()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.AssignDoctorAsync(
                0L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest(DoctorId: 1L)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignDoctorAsync(
                5L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest()));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignDoctorAsync(
                5L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest(DoctorId: 1L, DoctorCode: "DOC-999")));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.AssignDoctorAsync(
                5L,
                "VIS-999999",
                new AssignDoctorToVisitRequest(DoctorId: 1L)));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.AssignDoctorAsync(
                5L,
                "VIS-202610-0100",
                new AssignDoctorToVisitRequest(DoctorCode: "DOC-999")));
    }
}
