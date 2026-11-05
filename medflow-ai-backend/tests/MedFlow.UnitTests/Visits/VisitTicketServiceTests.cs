using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Visits;

public sealed class VisitTicketServiceTests : IDisposable
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class VisitTicketTestDbContext : DbContext, IApplicationDbContext
    {
        public VisitTicketTestDbContext(DbContextOptions<VisitTicketTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for VisitTicketService unit tests.");

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
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);

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

            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany(d => d.Services).HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany(sp => sp.Services).HasForeignKey(s => s.SpecialtyId);
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
                b.HasOne(a => a.Slot).WithMany(s => s.Appointments).HasForeignKey(a => a.SlotId);
                b.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId);
            });
        }
    }

    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero); // 09:00 Vietnam time

    private readonly VisitTicketTestDbContext _db;
    private readonly VisitTicketService _service;

    public VisitTicketServiceTests()
    {
        var options = new DbContextOptionsBuilder<VisitTicketTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new VisitTicketTestDbContext(options);
        _service = new VisitTicketService(_db, new FixedTimeProvider(FixedUtcNow));

        SeedTestData();
    }

    public void Dispose() => _db.Dispose();

    private void SeedTestData()
    {
        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Nhân viên tiếp đón" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" });

        _db.Permissions.AddRange(
            new Permission { Id = 1, Code = "PATIENT_PROFILE_READ" },
            new Permission { Id = 3, Code = "PATIENT_CHECKIN" },
            new Permission { Id = 4, Code = "VISIT_ASSIGN" },
            new Permission { Id = 5, Code = "QUEUE_MANAGE" });

        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 3, PermissionId = 1 },
            new RolePermission { RoleId = 3, PermissionId = 3 },
            new RolePermission { RoleId = 3, PermissionId = 4 },
            new RolePermission { RoleId = 3, PermissionId = 5 });

        _db.Hospitals.AddRange(
            new Hospital
            {
                Id = 1,
                HospitalCode = "HOS-DEMO-01",
                Name = "Bệnh viện MedFlow (giả lập)",
                Address = "Số 1 Đường Demo, Hà Nội",
                IsActive = true,
            },
            new Hospital
            {
                Id = 2,
                HospitalCode = "HOS-DEMO-02",
                Name = "Bệnh viện Cơ sở 2",
                Address = "Số 2 Đường Demo, TP.HCM",
                IsActive = true,
            });

        _db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "OPD", Name = "Khoa Khám bệnh", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "GI", Name = "Khoa Tiêu hóa", IsActive = true },
            new Department { Id = 20, HospitalId = 2, DepartmentCode = "GI-2", Name = "Khoa Tiêu hóa CS2", IsActive = true });

        _db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true });

        _db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-001",
                Name = "Khám Tiêu hóa 101",
                RoomType = "EXAMINATION",
                Location = "Tầng 1, Khu Demo",
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
                Location = "Tầng 1, Khu Demo",
                Capacity = 2,
                Status = "MAINTENANCE",
            },
            new HospitalRoom
            {
                Id = 20,
                HospitalId = 2,
                DepartmentId = 20,
                RoomCode = "RM-201",
                Name = "Khám Tiêu hóa CS2",
                RoomType = "EXAMINATION",
                Location = "Tầng 2, CS2",
                Capacity = 2,
                Status = "ACTIVE",
            });

        _db.Users.AddRange(
            new User { Id = 1, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 5, Email = "reception01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 5, RoleId = 3 },
            new UserRole { UserId = 11, RoleId = 6 });

        _db.Staff.AddRange(
            new Staff
            {
                Id = 1,
                UserId = 3,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-001",
                FullName = "BS Nguyễn Minh Đức",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 3,
                UserId = 5,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-003",
                FullName = "Lễ tân Lê Lan",
                Status = "ACTIVE",
            });

        _db.Doctors.Add(new Doctor
        {
            Id = 1,
            StaffId = 1,
            HospitalId = 1,
            DoctorCode = "DOC-001",
            SpecialtyId = 1,
            ProfessionalTitle = "BS.CKI",
            IsActive = true,
            YearsOfExperience = 9,
            LicenseNumber = "DEMO-CCHN-001",
        });

        _db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                UserId = 1,
                PatientCode = "PAT-0001",
                FullName = "Nguyễn Văn An",
                DateOfBirth = new DateOnly(1980, 2, 2),
                Gender = "MALE",
                Phone = "0900000001",
                Email = "patient01@medflow.test",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 9,
                PatientCode = "PAT-0009",
                FullName = "Hoàng Ngọc Phương",
                DateOfBirth = new DateOnly(1988, 10, 10),
                Gender = "FEMALE",
                Phone = "0900000009",
                Email = "patient09@medflow.test",
                Status = "ACTIVE",
            });

        _db.Services.Add(new Service
        {
            Id = 1,
            HospitalId = 1,
            DepartmentId = 2,
            SpecialtyId = 1,
            ServiceCode = "EX-GI-01",
            Name = "Khám Tiêu hóa lần đầu",
            ServiceType = "EXAMINATION",
            AllowsOnlineBooking = true,
            IsActive = true,
        });

        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 1,
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 10, 15, 9, 0, 0),
            EndsAt = new DateTime(2026, 10, 15, 9, 30, 0),
            Capacity = 3,
            Status = "OPEN",
        });

        _db.Appointments.Add(new Appointment
        {
            Id = 1,
            AppointmentCode = "APT-202610-0001",
            PatientId = 1,
            SlotId = 1,
            BookingChannel = "ONLINE",
            ReasonForVisit = "Đau thượng vị",
            Status = "CHECKED_IN",
            BookedByUserId = 1,
            BookedAt = new DateTime(2026, 10, 12, 10, 0, 0),
        });

        _db.Visits.AddRange(
            new Visit
            {
                Id = 1,
                VisitCode = "VIS-202610-0001",
                PatientId = 1,
                AppointmentId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Đau thượng vị",
                Status = "CHECKED_IN",
                CheckedInAt = new DateTime(2026, 10, 15, 8, 55, 0),
            },
            new Visit
            {
                Id = 9,
                VisitCode = "VIS-202610-0009",
                PatientId = 9,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Đau dạ dày từng đợt",
                Status = "CHECKED_IN",
                CheckedInAt = new DateTime(2026, 10, 22, 8, 55, 0),
            });

        _db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 1,
                VisitId = 1,
                AppointmentId = 1,
                ServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 15),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                ManualPriority = "NORMAL",
                ArrivedAt = new DateTime(2026, 10, 15, 8, 55, 0),
            },
            new QueueEntry
            {
                Id = 9,
                VisitId = 9,
                AppointmentId = null,
                ServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 9,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                ManualPriority = "NORMAL",
                ArrivedAt = new DateTime(2026, 10, 22, 8, 55, 0),
            });

        _db.SaveChanges();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("VIS-202610-0001")]
    [InlineData("vis-202610-0001")]
    [InlineData("#VIS-202610-0001")]
    public async Task GetExaminationTicketAsync_ScheduledVisit_ReturnsMappedTicketWithoutWritingAuditLog(
        string idOrVisitCode)
    {
        var dto = await _service.GetExaminationTicketAsync(5, idOrVisitCode);

        Assert.Equal(1L, dto.VisitId);
        Assert.Equal("VIS-202610-0001", dto.VisitCode);
        Assert.Equal("TKT-VIS-202610-0001", dto.TicketCode);
        Assert.Equal("A001", dto.TicketNo);
        Assert.Equal(1, dto.QueueNumber);
        Assert.Equal("001", dto.QueueNumberFormatted);
        Assert.Equal(1L, dto.QueueEntryId);
        Assert.Equal("2026-10-15", dto.QueueDate);
        Assert.Equal("WAITING", dto.QueueStatus);
        Assert.Equal("NORMAL", dto.Priority);
        Assert.Equal("SCHEDULED", dto.VisitType);
        Assert.Equal(1L, dto.AppointmentId);
        Assert.Equal("APT-202610-0001", dto.AppointmentCode);
        Assert.Equal("09:00 - 09:30", dto.ScheduledTimeRange);
        Assert.Equal(1L, dto.PatientId);
        Assert.Equal("PAT-0001", dto.PatientCode);
        Assert.Equal("Nguyễn Văn An", dto.PatientName);
        Assert.Equal("1980-02-02", dto.PatientDateOfBirth);
        Assert.Equal(1980, dto.PatientBirthYear);
        Assert.Equal(46, dto.PatientAge);
        Assert.Equal("MALE", dto.PatientGender);
        Assert.Equal("Nam", dto.PatientGenderLabel);
        Assert.Equal(1L, dto.HospitalId);
        Assert.Equal("HOS-DEMO-01", dto.HospitalCode);
        Assert.Equal(2L, dto.DepartmentId);
        Assert.Equal("Khoa Tiêu hóa", dto.DepartmentName);
        Assert.Equal(1L, dto.SpecialtyId);
        Assert.Equal("Tiêu hóa", dto.SpecialtyName);
        Assert.Equal(1L, dto.RoomId);
        Assert.Equal("RM-001", dto.RoomCode);
        Assert.Equal("Khám Tiêu hóa 101", dto.RoomName);
        Assert.Equal(1L, dto.DoctorId);
        Assert.Equal("DOC-001", dto.DoctorCode);
        Assert.Equal("BS.CKI", dto.DoctorTitle);
        Assert.Equal("BS Nguyễn Minh Đức", dto.DoctorName);
        Assert.Equal(1L, dto.ServiceId);
        Assert.Equal("EX-GI-01", dto.ServiceCode);
        Assert.Equal("Khám Tiêu hóa lần đầu", dto.ServiceName);
        Assert.Equal("Đau thượng vị", dto.ReasonForVisit);
        Assert.Equal(3L, dto.CheckedInByStaffId);
        Assert.Equal("Lễ tân Lê Lan", dto.CheckedInByStaffName);
        Assert.Equal(5L, dto.PrintedByUserId);
        Assert.Equal("STF-003", dto.PrintedByStaffCode);
        Assert.Equal(1, dto.Copies);
        Assert.Equal(0, dto.PrintCount);
        Assert.False(dto.IsReprint);

        Assert.Equal(0, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task GetExaminationTicketAsync_WalkInVisit_ResolvesSpecialtyServiceAndQueueAhead()
    {
        _db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 90,
                VisitId = 90,
                ServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 7,
                QueueType = "EXAMINATION",
                Status = "WAITING",
            },
            new QueueEntry
            {
                Id = 91,
                VisitId = 91,
                ServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 8,
                QueueType = "EXAMINATION",
                Status = "READY",
            });
        await _db.SaveChangesAsync();

        var dto = await _service.GetExaminationTicketAsync(5, "VIS-202610-0009");

        Assert.Equal(9L, dto.VisitId);
        Assert.Equal("WALK_IN", dto.VisitType);
        Assert.Null(dto.AppointmentId);
        Assert.Null(dto.ScheduledTimeRange);
        Assert.Equal("A009", dto.TicketNo);
        Assert.Equal(9, dto.QueueNumber);
        Assert.Equal("009", dto.QueueNumberFormatted);
        Assert.Equal(2, dto.WaitingAheadCount);
        Assert.Equal(30, dto.EstimatedWaitMinutes);
        Assert.Equal("PAT-0009", dto.PatientCode);
        Assert.Equal("Hoàng Ngọc Phương", dto.PatientName);
        Assert.Equal("EX-GI-01", dto.ServiceCode);
    }

    [Fact]
    public async Task PrintExaminationTicketAsync_FirstAndSecondPrint_PersistsAuditLogsAndTracksReprint()
    {
        var firstPrint = await _service.PrintExaminationTicketAsync(
            5,
            "VIS-202610-0001",
            new PrintExaminationTicketRequest(Copies: 2, Reason: "In phiếu tại quầy 1"),
            "127.0.0.1");

        Assert.Equal(2, firstPrint.Copies);
        Assert.Equal(1, firstPrint.PrintCount);
        Assert.False(firstPrint.IsReprint);

        var logsAfterFirst = await _db.AuditLogs.AsNoTracking().ToListAsync();
        var log1 = Assert.Single(logsAfterFirst);
        Assert.Equal(5L, log1.ActorUserId);
        Assert.Equal(1L, log1.HospitalId);
        Assert.Equal("PRINT_TICKET", log1.Action);
        Assert.Equal("VISIT", log1.EntityType);
        Assert.Equal("1", log1.EntityId);
        Assert.Equal("In phiếu tại quầy 1", log1.Reason);
        Assert.Contains("VIS-202610-0001", log1.ChangeData);

        var secondPrint = await _service.PrintExaminationTicketAsync(
            5,
            "1",
            new PrintExaminationTicketRequest(Copies: 1, Note: "Bệnh nhân xin in lại phiếu"));

        Assert.Equal(1, secondPrint.Copies);
        Assert.Equal(2, secondPrint.PrintCount);
        Assert.True(secondPrint.IsReprint);

        var previewAfterPrints = await _service.GetExaminationTicketAsync(5, "1");
        Assert.Equal(2, previewAfterPrints.PrintCount);
        Assert.True(previewAfterPrints.IsReprint);
        Assert.Equal(2, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task GetExaminationTicketAsync_NoQueueEntryRow_ComputesDeterministicRoomSequenceRank()
    {
        _db.Visits.AddRange(
            new Visit
            {
                Id = 50,
                VisitCode = "VIS-202610-0050",
                PatientId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                Status = "CHECKED_IN",
                CheckedInAt = new DateTime(2026, 10, 25, 8, 10, 0),
            },
            new Visit
            {
                Id = 51,
                VisitCode = "VIS-202610-0051",
                PatientId = 9,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                Status = "CHECKED_IN",
                CheckedInAt = new DateTime(2026, 10, 25, 8, 25, 0),
            });
        await _db.SaveChangesAsync();

        var dto = await _service.GetExaminationTicketAsync(5, "51");

        Assert.Null(dto.QueueEntryId);
        Assert.Equal(2, dto.QueueNumber);
        Assert.Equal("002", dto.QueueNumberFormatted);
        Assert.Equal("A002", dto.TicketNo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task GetExaminationTicketAsync_InvalidActorUserId_ThrowsUnauthorizedAccessException(long actorUserId)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetExaminationTicketAsync(actorUserId, "1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("VIS@202610")]
    [InlineData("VIS-202610-0001-EXTRA-LONG-IDENTIFIER-EXCEEDING-FORTY-CHARS")]
    public async Task GetExaminationTicketAsync_InvalidVisitIdentifier_ThrowsValidationException(string idOrCode)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetExaminationTicketAsync(5, idOrCode));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task PrintExaminationTicketAsync_InvalidCopies_ThrowsValidationException(int copies)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.PrintExaminationTicketAsync(5, "1", new PrintExaminationTicketRequest(Copies: copies)));
    }

    [Fact]
    public async Task PrintExaminationTicketAsync_OverlongReasonOrNote_ThrowsValidationException()
    {
        var overlong = new string('A', 251);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.PrintExaminationTicketAsync(5, "1", new PrintExaminationTicketRequest(Reason: overlong)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.PrintExaminationTicketAsync(5, "1", new PrintExaminationTicketRequest(Note: overlong)));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_UnknownActorUser_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetExaminationTicketAsync(999, "1"));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_InactiveOrLockedActorUser_ThrowsForbiddenException()
    {
        var user = await _db.Users.FirstAsync(u => u.Id == 5);
        user.AccountStatus = AccountStatus.INACTIVE;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));

        user.AccountStatus = AccountStatus.ACTIVE;
        user.LockedUntil = FixedUtcNow.UtcDateTime.AddMinutes(30);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));
    }

    [Theory]
    [InlineData(1)]  // PATIENT
    [InlineData(3)]  // DOCTOR
    [InlineData(11)] // HOSPITAL_ADMIN
    public async Task GetExaminationTicketAsync_NonReceptionUser_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationTicketAsync(actorUserId, "1"));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_RevokedReceptionPermissions_ThrowsForbiddenException()
    {
        var ticketRolePerms = await _db.RolePermissions
            .Where(rp => rp.RoleId == 3 && rp.PermissionId != 1)
            .ToListAsync();
        _db.RolePermissions.RemoveRange(ticketRolePerms);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_InactiveStaffOrHospital_ThrowsForbiddenException()
    {
        var staff = await _db.Staff.FirstAsync(s => s.Id == 3);
        staff.Status = "INACTIVE";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));

        staff.Status = "ACTIVE";
        var hospital = await _db.Hospitals.FirstAsync(h => h.Id == 1);
        hospital.IsActive = false;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_CrossHospitalVisit_ThrowsForbiddenException()
    {
        _db.Visits.Add(new Visit
        {
            Id = 200,
            VisitCode = "VIS-202610-0200",
            PatientId = 1,
            AssignedDoctorId = 1,
            ExaminationRoomId = 20, // Hospital 2
            CheckedInByStaffId = 3,
            Status = "CHECKED_IN",
            CheckedInAt = new DateTime(2026, 10, 15, 8, 55, 0),
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetExaminationTicketAsync(5, "200"));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_UnknownVisit_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetExaminationTicketAsync(5, "9999"));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetExaminationTicketAsync(5, "VIS-202610-9999"));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_CancelledVisit_ThrowsConflictException()
    {
        var visit = await _db.Visits.FirstAsync(v => v.Id == 1);
        visit.Status = "CANCELLED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_MissingOrInactiveAssignedDoctor_ThrowsConflictOrNotFound()
    {
        var visit = await _db.Visits.FirstAsync(v => v.Id == 1);
        visit.AssignedDoctorId = null;
        await _db.SaveChangesAsync();

        var missingDocEx = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));
        Assert.Contains("UC26", missingDocEx.Message);

        visit.AssignedDoctorId = 999;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));

        visit.AssignedDoctorId = 1;
        var doctor = await _db.Doctors.FirstAsync(d => d.Id == 1);
        doctor.IsActive = false;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_MissingOrInactiveExaminationRoom_ThrowsConflictOrNotFound()
    {
        var visit = await _db.Visits.FirstAsync(v => v.Id == 1);
        visit.ExaminationRoomId = null;
        await _db.SaveChangesAsync();

        var missingRoomEx = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));
        Assert.Contains("UC27", missingRoomEx.Message);

        visit.ExaminationRoomId = 999;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));

        visit.ExaminationRoomId = 2; // MAINTENANCE room
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));
    }

    [Fact]
    public async Task GetExaminationTicketAsync_CancelledExaminationQueueEntry_ThrowsConflictException()
    {
        var queue = await _db.QueueEntries.FirstAsync(q => q.Id == 1);
        queue.Status = "CANCELLED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetExaminationTicketAsync(5, "1"));
    }
}
