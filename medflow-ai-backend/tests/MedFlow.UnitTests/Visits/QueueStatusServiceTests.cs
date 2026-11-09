using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Visits;

public sealed class QueueStatusServiceTests : IDisposable
{
    private sealed class QueueStatusTestDbContext : DbContext, IApplicationDbContext
    {
        public QueueStatusTestDbContext(DbContextOptions<QueueStatusTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for QueueStatusService unit tests.");

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
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<ServicePrice>();
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
            modelBuilder.Entity<QueueServicePoint>(b => b.HasKey(sp => sp.Id));

            modelBuilder.Entity<HospitalPolicy>(b => b.HasKey(p => p.Id));
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

    private readonly QueueStatusTestDbContext _db;
    private readonly QueueStatusService _service;

    public QueueStatusServiceTests()
    {
        var options = new DbContextOptionsBuilder<QueueStatusTestDbContext>()
            .UseInMemoryDatabase($"QueueStatusServiceTests_{Guid.NewGuid()}")
            .Options;

        _db = new QueueStatusTestDbContext(options);
        SeedBaseData();
        _service = new QueueStatusService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedBaseData()
    {
        _db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "MEDFLOW-HCM-01", Name = "Bệnh viện MedFlow Quận 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "MEDFLOW-HN-01", Name = "Bệnh viện MedFlow Hà Nội", IsActive = true });

        _db.Departments.AddRange(
            new Department { Id = 10, HospitalId = 1, DepartmentCode = "NOI", Name = "Khoa Nội Tổng Quát", IsActive = true },
            new Department { Id = 20, HospitalId = 2, DepartmentCode = "NOI-HN", Name = "Khoa Nội Hà Nội", IsActive = true });

        _db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 101,
                HospitalId = 1,
                DepartmentId = 10,
                RoomCode = "A-101",
                Name = "Phòng khám Nội 1",
                RoomType = "EXAMINATION",
                Capacity = 5,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 102,
                HospitalId = 1,
                DepartmentId = 10,
                RoomCode = "A-102",
                Name = "Phòng khám Nội 2 (Bảo trì)",
                RoomType = "EXAMINATION",
                Capacity = 5,
                Status = "MAINTENANCE",
            },
            new HospitalRoom
            {
                Id = 201,
                HospitalId = 2,
                DepartmentId = 20,
                RoomCode = "B-201",
                Name = "Phòng khám Nội HN 1",
                RoomType = "EXAMINATION",
                Capacity = 5,
                Status = "ACTIVE",
            });

        _db.QueueServicePoints.AddRange(
            new QueueServicePoint
            {
                Id = 1,
                HospitalId = 1,
                RoomId = 101,
                DoctorId = 50,
                PointType = "EXAMINATION",
                Name = "Buồng khám Nội 1",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 2,
                HospitalId = 2,
                RoomId = 201,
                DoctorId = 60,
                PointType = "EXAMINATION",
                Name = "Buồng khám Nội HN 1",
                IsActive = true,
            });

        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "RECEPTION", Name = "Lễ tân" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Nhân viên CLS" },
            new Role { Id = 5, Code = "PHARMACY_STAFF", Name = "Dược sĩ" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 7, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" });

        _db.Permissions.AddRange(
            new Permission { Id = 5, Code = "QUEUE_MANAGE" },
            new Permission { Id = 6, Code = "MEDICAL_RECORD_WRITE" },
            new Permission { Id = 99, Code = "OTHER_PERM" });

        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 2, PermissionId = 5 },
            new RolePermission { RoleId = 3, PermissionId = 6 },
            new RolePermission { RoleId = 4, PermissionId = 5 },
            new RolePermission { RoleId = 6, PermissionId = 5 });

        _db.Users.AddRange(
            new User { Id = 1, Email = "patient@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 5, Email = "reception@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 3, Email = "doctor1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 4, Email = "doctor2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 9, Email = "pharmacy@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "sysadmin@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 5, RoleId = 2 },
            new UserRole { UserId = 3, RoleId = 3 },
            new UserRole { UserId = 4, RoleId = 3 },
            new UserRole { UserId = 9, RoleId = 5 },
            new UserRole { UserId = 11, RoleId = 7 });

        _db.Staff.AddRange(
            new Staff
            {
                Id = 10,
                UserId = 5,
                HospitalId = 1,
                DepartmentId = 10,
                StaffCode = "REC-001",
                FullName = "Lễ tân Nguyễn Thị Mai",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 20,
                UserId = 3,
                HospitalId = 1,
                DepartmentId = 10,
                StaffCode = "DOC-001",
                FullName = "BS. Trần Văn Hùng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 21,
                UserId = 4,
                HospitalId = 1,
                DepartmentId = 10,
                StaffCode = "DOC-002",
                FullName = "BS. Lê Thị Lan",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 30,
                UserId = 9,
                HospitalId = 1,
                DepartmentId = 10,
                StaffCode = "PHA-001",
                FullName = "DS. Phạm Quốc Bảo",
                Status = "ACTIVE",
            });

        _db.Doctors.AddRange(
            new Doctor { Id = 50, StaffId = 20, HospitalId = 1, DoctorCode = "DR-050", IsActive = true },
            new Doctor { Id = 51, StaffId = 21, HospitalId = 1, DoctorCode = "DR-051", IsActive = true },
            new Doctor { Id = 60, HospitalId = 2, DoctorCode = "DR-060", IsActive = true });

        _db.Patients.AddRange(
            new Patient { Id = 500, PatientCode = "BN-500", FullName = "Nguyễn Văn An" },
            new Patient { Id = 501, PatientCode = "BN-501", FullName = "Trần Thị Bích" },
            new Patient { Id = 502, PatientCode = "BN-502", FullName = "Lê Hoàng Nam" });

        _db.Visits.AddRange(
            new Visit
            {
                Id = 1001,
                VisitCode = "VIS-202604-1001",
                PatientId = 500,
                AssignedDoctorId = 50,
                ExaminationRoomId = 101,
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 4, 18, 7, 30, 0),
                CheckedInByStaffId = 10,
            },
            new Visit
            {
                Id = 1002,
                VisitCode = "VIS-202604-1002",
                PatientId = 501,
                AssignedDoctorId = 50,
                ExaminationRoomId = 101,
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 4, 18, 7, 35, 0),
                CheckedInByStaffId = 10,
            },
            new Visit
            {
                Id = 2001,
                VisitCode = "VIS-202604-2001",
                PatientId = 502,
                AssignedDoctorId = 60,
                ExaminationRoomId = 201,
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 4, 18, 8, 0, 0),
            });

        _db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 10,
                VisitId = 1001,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 4, 18),
                QueueNumber = 12,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "ON_TIME_OR_GRACE",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = new DateTime(2026, 4, 18, 7, 30, 0),
            },
            new QueueEntry
            {
                Id = 11,
                VisitId = 1002,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 4, 18),
                QueueNumber = 13,
                QueueType = "EXAMINATION",
                Status = "CALLED",
                PresenceStatus = "PRESENT",
                ArrivalClass = "ON_TIME_OR_GRACE",
                ManualPriority = "ELDERLY",
                StateVersion = 2,
                ArrivedAt = new DateTime(2026, 4, 18, 7, 35, 0),
                ReadyAt = new DateTime(2026, 4, 18, 7, 36, 0),
                CalledAt = new DateTime(2026, 4, 18, 7, 40, 0),
            },
            new QueueEntry
            {
                Id = 20,
                VisitId = 2001,
                ServicePointId = 2,
                CurrentServicePointId = 2,
                QueueDate = new DateOnly(2026, 4, 18),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                StateVersion = 1,
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetQueueEntryStatusAsync_ByQueueIdAndVisitCode_ReturnsCurrentStatusAllowedTargetsAndTicketNo()
    {
        var byQueueId = await _service.GetQueueEntryStatusAsync(5, "10");
        Assert.Equal(10, byQueueId.QueueEntryId);
        Assert.Equal(1001, byQueueId.VisitId);
        Assert.Equal("VIS-202604-1001", byQueueId.VisitCode);
        Assert.Equal("WAITING", byQueueId.CurrentStatus);
        Assert.Equal(12, byQueueId.QueueNumber);
        Assert.Equal("A012", byQueueId.TicketNo);
        Assert.Equal(1, byQueueId.StateVersion);
        Assert.Contains("READY", byQueueId.AllowedTargetStatuses);
        Assert.Contains("CALLED", byQueueId.AllowedTargetStatuses);
        Assert.Contains("SKIPPED", byQueueId.AllowedTargetStatuses);
        Assert.DoesNotContain("CANCELLED", byQueueId.AllowedTargetStatuses);

        var byVisitCode = await _service.GetQueueEntryStatusAsync(5, "VIS-202604-1002");
        Assert.Equal(11, byVisitCode.QueueEntryId);
        Assert.Equal("CALLED", byVisitCode.CurrentStatus);
        Assert.Equal("A013", byVisitCode.TicketNo);
        Assert.Equal(2, byVisitCode.StateVersion);
        Assert.Contains("IN_PROGRESS", byVisitCode.AllowedTargetStatuses);
    }

    [Fact]
    public async Task UpdateQueueEntryStatusAsync_FullLifecycle_UpdatesTimestampsVisitStatusStateVersionAndAuditLogs()
    {
        // 1. WAITING -> READY
        var readyRes = await _service.UpdateQueueEntryStatusAsync(
            5,
            "10",
            new UpdateQueueStatusRequest(TargetStatus: "READY", ExpectedStateVersion: 1),
            "127.0.0.1");

        Assert.Equal("WAITING", readyRes.PreviousStatus);
        Assert.Equal("READY", readyRes.CurrentStatus);
        Assert.Equal(1, readyRes.PreviousStateVersion);
        Assert.Equal(2, readyRes.StateVersion);
        Assert.Equal(12, readyRes.QueueNumber);
        Assert.NotNull(readyRes.ReadyAt);
        Assert.Equal("WAITING", readyRes.VisitStatus);

        // 2. READY -> CALLED
        var calledRes = await _service.UpdateQueueEntryStatusAsync(
            5,
            "10",
            new UpdateQueueStatusRequest(TargetStatus: "CALLED", ExpectedStateVersion: 2));

        Assert.Equal("READY", calledRes.PreviousStatus);
        Assert.Equal("CALLED", calledRes.CurrentStatus);
        Assert.Equal(3, calledRes.StateVersion);
        Assert.NotNull(calledRes.CalledAt);
        Assert.Equal("CALLED", calledRes.VisitStatus);

        // 3. CALLED -> IN_PROGRESS
        var inProgressRes = await _service.UpdateQueueEntryStatusAsync(
            3,
            "VIS-202604-1001",
            new UpdateQueueStatusRequest(TargetStatus: "IN_PROGRESS", ExpectedStateVersion: 3));

        Assert.Equal("CALLED", inProgressRes.PreviousStatus);
        Assert.Equal("IN_PROGRESS", inProgressRes.CurrentStatus);
        Assert.Equal(4, inProgressRes.StateVersion);
        Assert.NotNull(inProgressRes.StartedAt);
        Assert.Equal("IN_PROGRESS", inProgressRes.VisitStatus);

        // 4. IN_PROGRESS -> COMPLETED
        var completedRes = await _service.UpdateQueueEntryStatusAsync(
            3,
            "10",
            new UpdateQueueStatusRequest(TargetStatus: "COMPLETED", Notes: "Đã hoàn tất khám lâm sàng", ExpectedStateVersion: 4));

        Assert.Equal("IN_PROGRESS", completedRes.PreviousStatus);
        Assert.Equal("COMPLETED", completedRes.CurrentStatus);
        Assert.Equal(5, completedRes.StateVersion);
        Assert.Equal(12, completedRes.QueueNumber);
        Assert.NotNull(completedRes.CompletedAt);
        Assert.Equal("COMPLETED", completedRes.VisitStatus);
        Assert.Empty(completedRes.AllowedTargetStatuses);
        Assert.Equal(4, completedRes.StatusHistory.Count);

        var visitInDb = await _db.Visits.AsNoTracking().FirstAsync(v => v.Id == 1001);
        Assert.Equal("COMPLETED", visitInDb.Status);
        Assert.NotNull(visitInDb.CompletedAt);
        Assert.Equal(50, visitInDb.CompletedByDoctorId);

        // 5. Attempting further transition on COMPLETED throws ConflictException
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                5,
                "10",
                new UpdateQueueStatusRequest(TargetStatus: "WAITING")));
    }

    [Fact]
    public async Task UpdateQueueEntryStatusAsync_SkipAndRequeue_SetsAndClearsBlockedReason()
    {
        var skipRes = await _service.UpdateQueueEntryStatusAsync(
            5,
            "q-011",
            new UpdateQueueStatusRequest(
                TargetStatus: "SKIPPED",
                Reason: "Gọi 3 lần bệnh nhân chưa vào phòng",
                PresenceStatus: "LEFT"));

        Assert.Equal("CALLED", skipRes.PreviousStatus);
        Assert.Equal("SKIPPED", skipRes.CurrentStatus);
        Assert.Equal("LEFT", skipRes.PresenceStatus);
        Assert.Equal("Gọi 3 lần bệnh nhân chưa vào phòng", skipRes.BlockedReason);
        Assert.Equal("WAITING", skipRes.VisitStatus);

        var requeueRes = await _service.UpdateQueueEntryStatusAsync(
            5,
            "11",
            new UpdateQueueStatusRequest(
                TargetStatus: "WAITING",
                Reason: "Bệnh nhân đã quay lại quầy tiếp nhận",
                PresenceStatus: "PRESENT"));

        Assert.Equal("SKIPPED", requeueRes.PreviousStatus);
        Assert.Equal("WAITING", requeueRes.CurrentStatus);
        Assert.Equal("PRESENT", requeueRes.PresenceStatus);
        Assert.Null(requeueRes.BlockedReason);
        Assert.Null(requeueRes.CalledAt);
    }

    [Fact]
    public async Task UpdateQueueEntryStatusAsync_InvalidTransitionOrMissingSkipReasonOrVersionMismatch_ThrowsExpectedExceptions()
    {
        // Invalid transition WAITING -> COMPLETED
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                5,
                "10",
                new UpdateQueueStatusRequest(TargetStatus: "COMPLETED")));

        // SKIPPED without reason -> ValidationException
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                5,
                "10",
                new UpdateQueueStatusRequest(TargetStatus: "SKIPPED")));

        // CANCELLED via generic status endpoint -> ValidationException
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                5,
                "10",
                new UpdateQueueStatusRequest(TargetStatus: "CANCELLED", Reason: "x")));

        // Conflicting TargetStatus and Status -> ValidationException
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                5,
                "10",
                new UpdateQueueStatusRequest(TargetStatus: "CALLED", Status: "READY")));

        // Stale ExpectedStateVersion -> ConflictException
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                5,
                "11",
                new UpdateQueueStatusRequest(TargetStatus: "IN_PROGRESS", ExpectedStateVersion: 1)));
    }

    [Fact]
    public async Task UpdateQueueEntryStatusAsync_ConcurrentInProgressInSameServicePoint_ThrowsConflictException()
    {
        // Put entry 11 into IN_PROGRESS in service point 1 / room 101
        await _service.UpdateQueueEntryStatusAsync(
            5,
            "11",
            new UpdateQueueStatusRequest(TargetStatus: "IN_PROGRESS"));

        // Call entry 10 in same service point 1 / room 101
        await _service.UpdateQueueEntryStatusAsync(
            5,
            "10",
            new UpdateQueueStatusRequest(TargetStatus: "CALLED"));

        // Attempting to start IN_PROGRESS on entry 10 while entry 11 is still IN_PROGRESS throws ConflictException
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                5,
                "10",
                new UpdateQueueStatusRequest(TargetStatus: "IN_PROGRESS")));
    }

    [Fact]
    public async Task AuthorizationAndHospitalOwnership_EnforcesTwoLayerChecks()
    {
        // Invalid actorUserId -> 401
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetQueueEntryStatusAsync(0, "10"));

        // Disallowed role PATIENT (1) and PHARMACY_STAFF (9) -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                1,
                "10",
                new UpdateQueueStatusRequest(TargetStatus: "CALLED")));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                9,
                "10",
                new UpdateQueueStatusRequest(TargetStatus: "CALLED")));

        // Doctor 2 (userId 4, doctorId 51) attempting to update Doctor 1's queue entry (doctorId 50) -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                4,
                "10",
                new UpdateQueueStatusRequest(TargetStatus: "CALLED")));

        // Reception from Hospital 1 (userId 5) attempting to update Hospital 2 queue entry (id 20) -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateQueueEntryStatusAsync(
                5,
                "20",
                new UpdateQueueStatusRequest(TargetStatus: "CALLED")));

        // SYSTEM_ADMIN (userId 11) can update Hospital 2 queue entry -> 200
        var sysAdminRes = await _service.UpdateQueueEntryStatusAsync(
            11,
            "20",
            new UpdateQueueStatusRequest(TargetStatus: "CALLED"));
        Assert.Equal("CALLED", sysAdminRes.CurrentStatus);
    }

    [Fact]
    public async Task UpdateQueueEntryPriorityAsync_ValidReceptionRequest_UpdatesPriorityBumpsStateVersionPreservesQueueNumberAndWritesAudit()
    {
        var res = await _service.UpdateQueueEntryPriorityAsync(
            5,
            "10",
            new UpdateQueuePriorityRequest(
                Priority: "PREGNANT",
                Reason: "Đã kiểm tra sổ khám thai định kỳ",
                VerificationConfirmed: true,
                ExpectedStateVersion: 1),
            "127.0.0.1");

        Assert.Equal(10L, res.QueueEntryId);
        Assert.Equal(1001L, res.VisitId);
        Assert.Equal("VIS-202604-1001", res.VisitCode);
        Assert.Equal("A012", res.TicketNo);
        Assert.Equal(12, res.QueueNumber);
        Assert.Equal("EXAMINATION", res.QueueType);
        Assert.Equal("WAITING", res.CurrentStatus);
        Assert.Equal("NORMAL", res.PreviousPriority);
        Assert.Equal("PREGNANT", res.Priority);
        Assert.True(res.VerificationRequired);
        Assert.True(res.VerificationConfirmed);
        Assert.Equal("Đã kiểm tra sổ khám thai định kỳ", res.Reason);
        Assert.Equal(1, res.PreviousStateVersion);
        Assert.Equal(2, res.StateVersion);
        Assert.Equal(5L, res.UpdatedByUserId);
        Assert.Equal("REC-001", res.UpdatedByStaffCode);

        var entryInDb = await _db.QueueEntries.AsNoTracking().FirstAsync(q => q.Id == 10);
        Assert.Equal("PREGNANT", entryInDb.ManualPriority);
        Assert.Equal(2, entryInDb.StateVersion);
        Assert.Equal(12, entryInDb.QueueNumber);

        var audit = await _db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == QueueStatusService.QueuePriorityUpdateAction && a.EntityId == "10");
        Assert.Equal(5L, audit.ActorUserId);
        Assert.Equal(1L, audit.HospitalId);
        Assert.Equal("Đã kiểm tra sổ khám thai định kỳ", audit.Reason);
        Assert.Contains("\"previousPriority\":\"NORMAL\"", audit.ChangeData);
        Assert.Contains("\"updatedPriority\":\"PREGNANT\"", audit.ChangeData);
    }

    [Theory]
    [InlineData("URGENT")]
    [InlineData("RETURN_RESULT")]
    [InlineData("EMERGENCY")]
    [InlineData("")]
    public async Task UpdateQueueEntryPriorityAsync_DisallowedOrBlankPriority_ThrowsValidationException(string rawPriority)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "10",
                new UpdateQueuePriorityRequest(
                    Priority: rawPriority,
                    Reason: "Lý do hợp lệ",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 1)));
    }

    [Fact]
    public async Task UpdateQueueEntryPriorityAsync_MissingReasonOrExpectedStateVersion_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "10",
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "   ",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 1)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "10",
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "Có sổ khám thai",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: null)));
    }

    [Fact]
    public async Task UpdateQueueEntryPriorityAsync_VerificationPolicyEnforcement_HonorsHospitalPolicyAndDowngradeRule()
    {
        // 1. Default (no policy row -> fail-safe enabled=true): PREGNANT without confirmation fails
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "10",
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "Chưa xác minh giấy tờ",
                    VerificationConfirmed: false,
                    ExpectedStateVersion: 1)));

        // 2. Explicit hospital policy enabled=true: PREGNANT without confirmation fails
        var policy = new HospitalPolicy
        {
            Id = 1,
            HospitalId = 1,
            PolicyType = "QUEUE",
            PolicyKey = "manual_priority_requires_verification",
            PolicyValue = "{\"enabled\":true}",
            PolicyVersion = 1,
        };
        _db.HospitalPolicies.Add(policy);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "10",
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "Chưa xác minh giấy tờ",
                    VerificationConfirmed: null,
                    ExpectedStateVersion: 1)));

        // 3. Policy updated to enabled=false: PREGNANT succeeds without confirmation
        policy.PolicyValue = "{\"enabled\":false}";
        await _db.SaveChangesAsync();

        var elevated = await _service.UpdateQueueEntryPriorityAsync(
            5,
            "10",
            new UpdateQueuePriorityRequest(
                Priority: "PREGNANT",
                Reason: "Áp dụng theo chính sách không bắt buộc xác minh",
                VerificationConfirmed: false,
                ExpectedStateVersion: 1));

        Assert.Equal("PREGNANT", elevated.Priority);
        Assert.False(elevated.VerificationRequired);
        Assert.False(elevated.VerificationConfirmed);

        // 4. Re-enable policy: downgrade from PREGNANT -> NORMAL never requires verificationConfirmed
        policy.PolicyValue = "{\"enabled\":true}";
        await _db.SaveChangesAsync();

        var downgraded = await _service.UpdateQueueEntryPriorityAsync(
            5,
            "10",
            new UpdateQueuePriorityRequest(
                Priority: "NORMAL",
                Reason: "Hạ mức ưu tiên về thông thường",
                VerificationConfirmed: false,
                ExpectedStateVersion: 2));

        Assert.Equal("NORMAL", downgraded.Priority);
        Assert.False(downgraded.VerificationRequired);
    }

    [Fact]
    public async Task UpdateQueueEntryPriorityAsync_PatientEligibility_EnforcesAge75ForElderlyAndRejectsMaleForPregnant()
    {
        var todayVn = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var patient = await _db.Patients.FirstAsync(p => p.Id == 500);

        // 1. Aged 74 -> rejected for ELDERLY
        patient.DateOfBirth = todayVn.AddYears(-74);
        patient.Gender = "FEMALE";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "10",
                new UpdateQueuePriorityRequest(
                    Priority: "ELDERLY",
                    Reason: "Kiểm tra CCCD 74 tuổi",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 1)));

        // 2. Aged 75 -> eligible for ELDERLY
        patient.DateOfBirth = todayVn.AddYears(-75);
        await _db.SaveChangesAsync();

        var elderlyRes = await _service.UpdateQueueEntryPriorityAsync(
            5,
            "10",
            new UpdateQueuePriorityRequest(
                Priority: "ELDERLY",
                Reason: "Đã đối chiếu CCCD đủ 75 tuổi",
                VerificationConfirmed: true,
                ExpectedStateVersion: 1));
        Assert.Equal("ELDERLY", elderlyRes.Priority);

        // 3. Gender = MALE -> rejected for PREGNANT
        patient.Gender = "MALE";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "10",
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "Nhập nhầm giới tính",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 2)));
    }

    [Fact]
    public async Task UpdateQueueEntryPriorityAsync_StaleVersionSamePriorityInProgressCompletedOrDiagnostic_ThrowsConflictException()
    {
        // 1. Stale ExpectedStateVersion (entry 11 is at StateVersion 2)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "11",
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "Lý do hợp lệ",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 1)));

        // 2. Same priority as current (entry 10 is already NORMAL)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "10",
                new UpdateQueuePriorityRequest(
                    Priority: "NORMAL",
                    Reason: "Lý do hợp lệ",
                    VerificationConfirmed: false,
                    ExpectedStateVersion: 1)));

        // 3. IN_PROGRESS entry -> ConflictException
        var entry11 = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        entry11.Status = "IN_PROGRESS";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "11",
                new UpdateQueuePriorityRequest(
                    Priority: "NORMAL",
                    Reason: "Đang khám",
                    ExpectedStateVersion: 2)));

        // 4. COMPLETED entry -> ConflictException
        entry11.Status = "COMPLETED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "11",
                new UpdateQueuePriorityRequest(
                    Priority: "NORMAL",
                    Reason: "Đã hoàn tất",
                    ExpectedStateVersion: 2)));

        // 5. DIAGNOSTIC queue entry -> ConflictException
        var diagEntry = new QueueEntry
        {
            Id = 30,
            VisitId = 1001,
            ServicePointId = 1,
            CurrentServicePointId = 1,
            QueueDate = new DateOnly(2026, 4, 18),
            QueueNumber = 1,
            QueueType = "DIAGNOSTIC",
            Status = "WAITING",
            ManualPriority = "NORMAL",
            StateVersion = 1,
        };
        _db.QueueEntries.Add(diagEntry);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateQueueEntryPriorityAsync(
                5,
                "30",
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "Hàng đợi cận lâm sàng",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 1)));
    }

    [Fact]
    public async Task UpdateQueueEntryPriorityAsync_DoctorDiagnosticStaffOrOtherHospital_ThrowsForbiddenException()
    {
        _db.Users.Add(new User { Id = 7, Email = "diag@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = 7, RoleId = 4 }); // DIAGNOSTIC_STAFF
        _db.Staff.Add(new Staff
        {
            Id = 40,
            UserId = 7,
            HospitalId = 1,
            DepartmentId = 10,
            StaffCode = "CLS-001",
            FullName = "KTV Cận lâm sàng",
            Status = "ACTIVE",
        });
        await _db.SaveChangesAsync();

        var validRequest = new UpdateQueuePriorityRequest(
            Priority: "PREGNANT",
            Reason: "Kiểm tra phân quyền",
            VerificationConfirmed: true,
            ExpectedStateVersion: 1);

        // DOCTOR (user 3) -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateQueueEntryPriorityAsync(3, "10", validRequest));

        // DIAGNOSTIC_STAFF (user 7) -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateQueueEntryPriorityAsync(7, "10", validRequest));

        // Reception Hospital 1 (user 5) on Hospital 2 entry ("20") -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateQueueEntryPriorityAsync(5, "20", validRequest));
    }

    [Fact]
    public async Task CancelQueueEntryAsync_WaitingEntry_CancelsEntryAndVisitPreservesQueueNumberAndWritesAudit()
    {
        var res = await _service.CancelQueueEntryAsync(
            5,
            "10",
            new CancelQueueEntryRequest(
                Reason: "Bệnh nhân xin về",
                ExpectedStateVersion: 1),
            "127.0.0.1");

        Assert.Equal("WAITING", res.PreviousStatus);
        Assert.Equal("CANCELLED", res.CurrentStatus);
        Assert.Equal(1, res.PreviousStateVersion);
        Assert.Equal(2, res.StateVersion);
        Assert.Equal(12, res.QueueNumber);
        Assert.Equal("A012", res.TicketNo);
        Assert.Equal("Bệnh nhân xin về", res.BlockedReason);
        Assert.Equal("Bệnh nhân xin về", res.Reason);
        Assert.Equal("LEFT", res.PresenceStatus);
        Assert.Equal("CANCELLED", res.VisitStatus);
        Assert.Empty(res.AllowedTargetStatuses);
        var historyItem = Assert.Single(res.StatusHistory);
        Assert.Equal("CANCELLED", historyItem.TargetStatus);
        Assert.Equal("WAITING", historyItem.PreviousStatus);
        Assert.Equal("Bệnh nhân xin về", historyItem.Reason);

        var entryInDb = await _db.QueueEntries.AsNoTracking().FirstAsync(q => q.Id == 10);
        Assert.Equal("CANCELLED", entryInDb.Status);
        Assert.NotNull(entryInDb.CompletedAt);
        Assert.Equal(12, entryInDb.QueueNumber);
        Assert.Equal(2, entryInDb.StateVersion);

        var visitInDb = await _db.Visits.AsNoTracking().FirstAsync(v => v.Id == 1001);
        Assert.Equal("CANCELLED", visitInDb.Status);
        Assert.NotNull(visitInDb.CompletedAt);

        var audit = await _db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == QueueStatusService.QueueEntryCancelAction && a.EntityId == "10");
        Assert.Equal("Bệnh nhân xin về", audit.Reason);
        Assert.Equal(1L, audit.HospitalId);
        Assert.Equal(5L, audit.ActorUserId);
    }

    [Fact]
    public async Task CancelQueueEntryAsync_CalledEntryByVisitCode_Succeeds()
    {
        var res = await _service.CancelQueueEntryAsync(
            5,
            "VIS-202604-1002",
            new CancelQueueEntryRequest(
                Reason: "Bệnh nhân rời khỏi bệnh viện sau khi được gọi số",
                ExpectedStateVersion: 2));

        Assert.Equal(11L, res.QueueEntryId);
        Assert.Equal("CALLED", res.PreviousStatus);
        Assert.Equal("CANCELLED", res.CurrentStatus);
        Assert.Equal(3, res.StateVersion);
        Assert.Equal(13, res.QueueNumber);
        Assert.Equal("CANCELLED", res.VisitStatus);
    }

    [Fact]
    public async Task CancelQueueEntryAsync_ReturnConsultationEntry_KeepsVisitOpen()
    {
        var entry10 = await _db.QueueEntries.FirstAsync(q => q.Id == 10);
        entry10.QueueType = "RETURN_CONSULTATION";
        await _db.SaveChangesAsync();

        var res = await _service.CancelQueueEntryAsync(
            5,
            "10",
            new CancelQueueEntryRequest(
                Reason: "Bệnh nhân xin về chờ đọc kết quả sau",
                ExpectedStateVersion: 1));

        Assert.Equal("CANCELLED", res.CurrentStatus);
        Assert.Equal("WAITING", res.VisitStatus);

        var visitInDb = await _db.Visits.AsNoTracking().FirstAsync(v => v.Id == 1001);
        Assert.Equal("WAITING", visitInDb.Status);
    }

    [Fact]
    public async Task CancelQueueEntryAsync_ReservedEntryWithoutVisit_KeepsNotArrivedPresence()
    {
        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 30,
            VisitId = null,
            AppointmentId = null,
            ServicePointId = 1,
            CurrentServicePointId = 1,
            QueueDate = new DateOnly(2026, 4, 18),
            QueueNumber = 14,
            QueueType = "EXAMINATION",
            Status = "RESERVED",
            PresenceStatus = "NOT_ARRIVED",
            StateVersion = 1,
        });
        await _db.SaveChangesAsync();

        var res = await _service.CancelQueueEntryAsync(
            5,
            "30",
            new CancelQueueEntryRequest(
                Reason: "Hủy lượt đặt trước chưa đến",
                ExpectedStateVersion: 1));

        Assert.Equal("RESERVED", res.PreviousStatus);
        Assert.Equal("CANCELLED", res.CurrentStatus);
        Assert.Equal("NOT_ARRIVED", res.PresenceStatus);
        Assert.Null(res.VisitId);
        Assert.Equal(14, res.QueueNumber);
    }

    [Fact]
    public async Task CancelQueueEntryAsync_MissingReasonOrVersion_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CancelQueueEntryAsync(
                5,
                "10",
                new CancelQueueEntryRequest(
                    Reason: "   ",
                    ExpectedStateVersion: 1)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CancelQueueEntryAsync(
                5,
                "10",
                new CancelQueueEntryRequest(
                    Reason: "Bệnh nhân xin về",
                    ExpectedStateVersion: null)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CancelQueueEntryAsync(
                5,
                "10",
                new CancelQueueEntryRequest(
                    Reason: "Bệnh nhân xin về",
                    ExpectedStateVersion: 0)));
    }

    [Fact]
    public async Task CancelQueueEntryAsync_StaleVersionInProgressCompletedAlreadyCancelledOrDiagnostic_ThrowsConflictException()
    {
        // 1. Stale version (entry 11 is at StateVersion 2)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CancelQueueEntryAsync(
                5,
                "11",
                new CancelQueueEntryRequest(
                    Reason: "Bệnh nhân xin về",
                    ExpectedStateVersion: 1)));

        // 2. IN_PROGRESS -> conflict
        var entry10 = await _db.QueueEntries.FirstAsync(q => q.Id == 10);
        entry10.Status = "IN_PROGRESS";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CancelQueueEntryAsync(
                5,
                "10",
                new CancelQueueEntryRequest(
                    Reason: "Đang khám",
                    ExpectedStateVersion: 1)));

        // 3. COMPLETED -> conflict
        entry10.Status = "COMPLETED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CancelQueueEntryAsync(
                5,
                "10",
                new CancelQueueEntryRequest(
                    Reason: "Đã hoàn tất",
                    ExpectedStateVersion: 1)));

        // 4. Cancel entry 11 OK, then cancel again with v3 -> conflict "đã được hủy"
        var cancelRes = await _service.CancelQueueEntryAsync(
            5,
            "11",
            new CancelQueueEntryRequest(
                Reason: "Bệnh nhân xin về",
                ExpectedStateVersion: 2));
        Assert.Equal("CANCELLED", cancelRes.CurrentStatus);
        Assert.Equal(3, cancelRes.StateVersion);

        var exAlreadyCancelled = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CancelQueueEntryAsync(
                5,
                "11",
                new CancelQueueEntryRequest(
                    Reason: "Hủy lần 2",
                    ExpectedStateVersion: 3)));
        Assert.Contains("đã được hủy", exAlreadyCancelled.Message);

        // 5. DIAGNOSTIC queue entry -> conflict
        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 31,
            VisitId = 1001,
            ServicePointId = 1,
            CurrentServicePointId = 1,
            QueueDate = new DateOnly(2026, 4, 18),
            QueueNumber = 1,
            QueueType = "DIAGNOSTIC",
            Status = "WAITING",
            ManualPriority = "NORMAL",
            StateVersion = 1,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CancelQueueEntryAsync(
                5,
                "31",
                new CancelQueueEntryRequest(
                    Reason: "Hàng đợi cận lâm sàng",
                    ExpectedStateVersion: 1)));
    }

    [Fact]
    public async Task CancelQueueEntryAsync_DoctorPharmacyOrOtherHospital_ThrowsForbiddenException()
    {
        var req = new CancelQueueEntryRequest(
            Reason: "Bệnh nhân xin về",
            ExpectedStateVersion: 1);

        // Doctor (user 3) on "10" -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CancelQueueEntryAsync(3, "10", req));

        // Pharmacy (user 9) on "10" -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CancelQueueEntryAsync(9, "10", req));

        // Reception Hospital 1 (user 5) on Hospital 2 entry "20" -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CancelQueueEntryAsync(5, "20", req));

        // Sysadmin (user 11) on Hospital 2 entry "20" with v1 succeeds
        var sysRes = await _service.CancelQueueEntryAsync(11, "20", req);
        Assert.Equal("CANCELLED", sysRes.CurrentStatus);
        Assert.Equal(2, sysRes.StateVersion);
    }
}
