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

public sealed class CheckInScheduledPatientServiceTests : IDisposable
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

    private sealed class CheckInTestDbContext : DbContext, IApplicationDbContext
    {
        public CheckInTestDbContext(DbContextOptions<CheckInTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for CheckInScheduledPatientService unit tests.");

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
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany().HasForeignKey(s => s.SpecialtyId);
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

    // 2026-10-27T01:50:00Z == 2026-10-27 08:50:00 Vietnam time (UTC+7), 10m before 09:00 slot
    private readonly FixedTimeProvider _timeProvider = new(
        new DateTimeOffset(2026, 10, 27, 1, 50, 0, TimeSpan.Zero));
    private readonly CheckInTestDbContext _db;
    private readonly CheckInScheduledPatientService _service;

    public CheckInScheduledPatientServiceTests()
    {
        var options = new DbContextOptionsBuilder<CheckInTestDbContext>()
            .UseInMemoryDatabase($"CheckInScheduledPatientTests_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new CheckInTestDbContext(options);
        _service = new CheckInScheduledPatientService(_db, _timeProvider);

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
                DepartmentCode = "DEP-GI",
                Name = "Khoa Tiêu hóa",
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

        _db.Specialties.Add(new Specialty
        {
            Id = 2,
            SpecialtyCode = "SPC-GI",
            Name = "Tiêu hóa",
            IsActive = true,
        });

        _db.Services.AddRange(
            new Service
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 2,
                ServiceCode = "SVC-GI-01",
                Name = "Khám chuyên khoa Tiêu hóa",
                ServiceType = "EXAMINATION",
                IsActive = true,
            },
            new Service
            {
                Id = 20,
                HospitalId = 2,
                DepartmentId = 20,
                SpecialtyId = 2,
                ServiceCode = "SVC-H2-01",
                Name = "Khám Nội CS2",
                ServiceType = "EXAMINATION",
                IsActive = true,
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
                FullName = "BS Nguyễn Minh Đức",
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
                StaffCode = "STF-HAD-01",
                FullName = "Quản Trị BV 1",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 25,
                UserId = 25,
                HospitalId = 2,
                DepartmentId = 20,
                StaffCode = "STF-REC-02",
                FullName = "Tiếp Đón CS2",
                Status = "ACTIVE",
            });

        _db.Doctors.Add(new Doctor
        {
            Id = 1,
            StaffId = 1,
            HospitalId = 1,
            SpecialtyId = 2,
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
            Name = "Điểm khám Tiêu hóa 101",
            PointType = "EXAMINATION",
            IsActive = true,
        });

        _db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                UserId = 1,
                PatientCode = "PAT-0001",
                FullName = "Nguyễn Văn An",
                Gender = "MALE",
                DateOfBirth = new DateOnly(1990, 5, 15),
                Phone = "0901234567",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 2,
                PatientCode = "PAT-0002",
                FullName = "Trần Thị Mai",
                Gender = "FEMALE",
                DateOfBirth = new DateOnly(1995, 8, 20),
                Phone = "0902345678",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 3,
                PatientCode = "PAT-0003",
                FullName = "Cụ Ông Cao Tuổi",
                Gender = "MALE",
                DateOfBirth = new DateOnly(1945, 1, 10),
                Phone = "0903456789",
                Status = "ACTIVE",
            });

        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 10,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 27, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 27, 9, 30, 0),
                Capacity = 5,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 20,
                ServiceId = 20,
                DoctorId = 1,
                RoomId = 20,
                StartsAt = new DateTime(2026, 10, 27, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 27, 9, 30, 0),
                Capacity = 5,
                Status = "OPEN",
            });

        _db.Appointments.AddRange(
            new Appointment
            {
                Id = 100,
                AppointmentCode = "APT-202610-0100",
                PatientId = 1,
                SlotId = 10,
                BookingChannel = "RECEPTION",
                ReasonForVisit = "Đau thượng vị sau ăn kéo dài",
                Status = "BOOKED",
                BookedByUserId = 5,
                BookedAt = new DateTime(2026, 10, 26, 10, 0, 0),
            },
            new Appointment
            {
                Id = 101,
                AppointmentCode = "APT-202610-0101",
                PatientId = 2,
                SlotId = 10,
                BookingChannel = "ONLINE",
                ReasonForVisit = "Khám tiêu hóa định kỳ",
                Status = "BOOKED",
                BookedByUserId = 1,
                BookedAt = new DateTime(2026, 10, 26, 11, 0, 0),
            },
            new Appointment
            {
                Id = 102,
                AppointmentCode = "APT-202610-0102",
                PatientId = 3,
                SlotId = 10,
                BookingChannel = "ONLINE",
                ReasonForVisit = "Khám lão khoa tiêu hóa",
                Status = "BOOKED",
                BookedByUserId = 5,
                BookedAt = new DateTime(2026, 10, 26, 12, 0, 0),
            });

        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 500,
            AppointmentId = 100,
            ServicePointId = 1,
            CurrentServicePointId = 1,
            QueueDate = new DateOnly(2026, 10, 27),
            QueueNumber = 1,
            QueueType = "EXAMINATION",
            Status = "RESERVED",
            ScheduledAt = new DateTime(2026, 10, 27, 9, 0, 0),
            PresenceStatus = "NOT_ARRIVED",
            ManualPriority = "NORMAL",
            StateVersion = 1,
        });

        _db.SaveChanges();
    }

    [Fact]
    public async Task CheckInScheduledPatientAsync_WithReservedQueueEntry_TransitionsAppointmentCreatesVisitActivatesQueueAndWritesAuditLog()
    {
        var request = new CheckInScheduledPatientRequest(
            ReasonForVisit: "Đau thượng vị kèm ợ chua nhiều ngày",
            BloodPressure: "120/80",
            HeartRate: 76,
            Temperature: 36.6,
            IsIdentityVerified: true,
            IsInsuranceVerified: true,
            Priority: "NORMAL",
            Notes: "Đã đối chiếu CCCD và thẻ BHYT tại quầy số 1");

        var result = await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0100",
            request: request,
            ipAddress: "127.0.0.1");

        Assert.True(result.VisitId > 0);
        Assert.Equal($"VIS-202610-{result.VisitId:D4}", result.VisitCode);
        Assert.Equal($"TKT-{result.VisitCode}", result.TicketCode);
        Assert.Equal("A001", result.TicketNo);
        Assert.Equal(500L, result.QueueEntryId);
        Assert.Equal(1, result.QueueNumber);
        Assert.Equal("001", result.QueueNumberFormatted);
        Assert.Equal("WAITING", result.QueueStatus);
        Assert.Equal("PRESENT", result.PresenceStatus);
        Assert.Equal("ON_TIME_OR_GRACE", result.ArrivalClass);
        Assert.Equal("NORMAL", result.Priority);
        Assert.Equal("NORMAL", result.ManualPriority);
        Assert.Equal(100L, result.AppointmentId);
        Assert.Equal("APT-202610-0100", result.AppointmentCode);
        Assert.Equal("CHECKED_IN", result.AppointmentStatus);
        Assert.Equal("WAITING", result.VisitStatus);
        Assert.Equal("SCHEDULED", result.VisitType);
        Assert.Equal(1L, result.PatientId);
        Assert.Equal("PAT-0001", result.PatientCode);
        Assert.Equal("Nguyễn Văn An", result.PatientName);
        Assert.Equal(1L, result.HospitalId);
        Assert.Equal(2L, result.DepartmentId);
        Assert.Equal("Khoa Tiêu hóa", result.DepartmentName);
        Assert.Equal(1L, result.RoomId);
        Assert.Equal("RM-001", result.RoomCode);
        Assert.Equal("Khám Tiêu hóa 101", result.RoomName);
        Assert.Equal(1L, result.DoctorId);
        Assert.Equal("DOC-001", result.DoctorCode);
        Assert.Equal("BS Nguyễn Minh Đức", result.DoctorName);
        Assert.Equal(1L, result.ServicePointId);
        Assert.Equal("Đau thượng vị kèm ợ chua nhiều ngày", result.ReasonForVisit);
        Assert.Equal("120/80", result.BloodPressure);
        Assert.Equal(76, result.HeartRate);
        Assert.Equal(36.6, result.Temperature);
        Assert.True(result.IsIdentityVerified);
        Assert.True(result.IsInsuranceVerified);
        Assert.Equal(0, result.WaitingCountBefore);
        Assert.Equal(0, result.EstimatedWaitMinutes);
        Assert.Equal("08:50", result.CheckedInAt);
        Assert.Equal(3L, result.CheckedInByStaffId);
        Assert.Equal("Lê Thị Tiếp Đón", result.CheckedInByStaffName);
        Assert.Equal(3, result.NextStepInstructions.Count);

        var updatedAppointment = await _db.Appointments.AsNoTracking().SingleAsync(a => a.Id == 100);
        Assert.Equal("CHECKED_IN", updatedAppointment.Status);
        Assert.Equal("Đã đối chiếu CCCD và thẻ BHYT tại quầy số 1", updatedAppointment.ReceptionNote);

        var createdVisit = await _db.Visits.AsNoTracking().SingleAsync(v => v.Id == result.VisitId);
        Assert.Equal(100L, createdVisit.AppointmentId);
        Assert.Equal(1L, createdVisit.PatientId);
        Assert.Equal(1L, createdVisit.AssignedDoctorId);
        Assert.Equal(1L, createdVisit.ExaminationRoomId);
        Assert.Equal(3L, createdVisit.CheckedInByStaffId);
        Assert.Equal("WAITING", createdVisit.Status);

        var activatedQueue = await _db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == 500);
        Assert.Equal(result.VisitId, activatedQueue.VisitId);
        Assert.Equal(1, activatedQueue.QueueNumber);
        Assert.Equal("WAITING", activatedQueue.Status);
        Assert.Equal("PRESENT", activatedQueue.PresenceStatus);
        Assert.Equal("ON_TIME_OR_GRACE", activatedQueue.ArrivalClass);
        Assert.Equal(2, activatedQueue.StateVersion);

        var auditLog = await _db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == CheckInScheduledPatientService.PatientCheckedInAction && a.EntityId == result.VisitId.ToString());
        Assert.Equal(5L, auditLog.ActorUserId);
        Assert.Equal(1L, auditLog.HospitalId);
        Assert.Equal("VISIT", auditLog.EntityType);
        Assert.NotNull(auditLog.ChangeData);

        using var doc = JsonDocument.Parse(auditLog.ChangeData!);
        Assert.Equal("CHECKED_IN", doc.RootElement.GetProperty("updatedAppointmentStatus").GetString());
        Assert.Equal("120/80", doc.RootElement.GetProperty("vitals").GetProperty("bloodPressure").GetString());
        Assert.Equal(76, doc.RootElement.GetProperty("vitals").GetProperty("heartRate").GetInt32());
        Assert.True(doc.RootElement.GetProperty("isIdentityVerified").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("isInsuranceVerified").GetBoolean());
    }

    [Fact]
    public async Task CheckInScheduledPatientAsync_WithoutExistingQueueEntry_AllocatesNextQueueNumberAndComputesWaitTime()
    {
        var result = await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "101",
            request: new CheckInScheduledPatientRequest(IsIdentityVerified: true));

        Assert.Equal(2, result.QueueNumber);
        Assert.Equal("002", result.QueueNumberFormatted);
        Assert.Equal("A002", result.TicketNo);
        Assert.Equal(1, result.WaitingCountBefore);
        Assert.Equal(1, result.WaitingAheadCount);
        Assert.Equal(15, result.EstimatedWaitMinutes);
        Assert.Equal("Khám tiêu hóa định kỳ", result.ReasonForVisit);
    }

    [Theory]
    [InlineData(1, 10, "EARLY")]            // 08:10 Vietnam time (< 09:00 - 15m)
    [InlineData(2, 10, "ON_TIME_OR_GRACE")] // 09:10 Vietnam time (within +/- 15m)
    [InlineData(2, 25, "LATE")]             // 09:25 Vietnam time (> 09:00 + 15m)
    public async Task CheckInScheduledPatientAsync_ClassifiesArrivalClassAccordingToGracePolicy(
        int utcHour,
        int utcMinute,
        string expectedArrivalClass)
    {
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 10, 27, utcHour, utcMinute, 0, TimeSpan.Zero));

        var result = await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0100",
            request: new CheckInScheduledPatientRequest(IsIdentityVerified: true));

        Assert.Equal(expectedArrivalClass, result.ArrivalClass);
    }

    [Fact]
    public async Task CheckInScheduledPatientAsync_ClinicalPriorityValidation_EnforcesEligibilityAndRejectsUrgent()
    {
        // Valid PREGNANT for female patient (PatientId = 2, AppointmentId = 101)
        var pregnantResult = await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0101",
            request: new CheckInScheduledPatientRequest(
                IsIdentityVerified: true,
                Priority: "PREGNANT",
                PriorityVerificationConfirmed: true));
        Assert.Equal("PREGNANT", pregnantResult.Priority);
        Assert.Equal("PREGNANT", pregnantResult.ManualPriority);

        // Valid ELDERLY for 81-year-old patient (PatientId = 3, AppointmentId = 102)
        var elderlyResult = await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0102",
            request: new CheckInScheduledPatientRequest(
                IsIdentityVerified: true,
                Priority: "ELDERLY",
                PriorityVerificationConfirmed: true));
        Assert.Equal("ELDERLY", elderlyResult.Priority);

        // Invalid ELDERLY for 36-year-old patient (PatientId = 1, AppointmentId = 100)
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    Priority: "ELDERLY",
                    PriorityVerificationConfirmed: true)));

        // Invalid PREGNANT for MALE patient (PatientId = 1, AppointmentId = 100)
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    Priority: "PREGNANT",
                    PriorityVerificationConfirmed: true)));

        // Priority verification explicitly false when hospital policy requires verification
        _db.Appointments.Add(new Appointment
        {
            Id = 103,
            AppointmentCode = "APT-202610-0103",
            PatientId = 3,
            SlotId = 10,
            ReasonForVisit = "Khám tiêu hóa người cao tuổi",
            Status = "BOOKED",
        });
        await _db.SaveChangesAsync();

        // Remove existing visit for patient 3 so same-day visit guard does not fire before priority check
        var existingElderlyVisit = await _db.Visits.SingleAsync(v => v.AppointmentId == 102);
        existingElderlyVisit.Status = "COMPLETED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0103",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    Priority: "ELDERLY",
                    PriorityVerificationConfirmed: false)));

        // Invalid priority value "URGENT"
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    Priority: "URGENT")));
    }

    [Fact]
    public async Task CheckInScheduledPatientAsync_InvalidInputs_ThrowsValidationException()
    {
        // Identity not verified
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(IsIdentityVerified: false)));

        // Short ReasonForVisit
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(IsIdentityVerified: true, ReasonForVisit: "ab")));

        // Overlong ReasonForVisit
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    ReasonForVisit: new string('A', 301))));

        // Invalid BloodPressure format
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    BloodPressure: "120-80")));

        // Out-of-range HeartRate
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    HeartRate: 30)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    HeartRate: 250)));

        // Out-of-range Temperature
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    Temperature: 32.0)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(
                    IsIdentityVerified: true,
                    Temperature: 45.0)));

        // Invalid appointment identifiers
        foreach (var badId in new[] { "", "0", "-1", "invalid@code" })
        {
            await Assert.ThrowsAsync<ValidationException>(() =>
                _service.CheckInScheduledPatientAsync(
                    actorUserId: 5,
                    appointmentIdOrCode: badId,
                    request: new CheckInScheduledPatientRequest(IsIdentityVerified: true)));
        }
    }

    [Fact]
    public async Task CheckInScheduledPatientAsync_DuplicateOrInvalidAppointmentOrSlotOrDoctorOrRoomStates_ThrowsConflictException()
    {
        // First check-in succeeds
        await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0100",
            request: new CheckInScheduledPatientRequest(IsIdentityVerified: true));

        // Repeat check-in on same appointment -> 409 Conflict without creating a second Visit
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0100",
                request: new CheckInScheduledPatientRequest(IsIdentityVerified: true)));

        Assert.Equal(1, await _db.Visits.CountAsync(v => v.AppointmentId == 100));

        // Patient 1 already has an active visit today -> checking in another appointment for Patient 1 fails
        _db.Appointments.Add(new Appointment
        {
            Id = 110,
            AppointmentCode = "APT-202610-0110",
            PatientId = 1,
            SlotId = 10,
            ReasonForVisit = "Khám thêm trong ngày",
            Status = "BOOKED",
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0110",
                request: new CheckInScheduledPatientRequest(IsIdentityVerified: true)));

        // CANCELLED, NO_SHOW, COMPLETED appointments
        foreach (var (id, status) in new[] { (111L, "CANCELLED"), (112L, "NO_SHOW"), (113L, "COMPLETED") })
        {
            _db.Appointments.Add(new Appointment
            {
                Id = id,
                AppointmentCode = $"APT-202610-{id:D4}",
                PatientId = 2,
                SlotId = 10,
                ReasonForVisit = "Khám tiêu hóa",
                Status = status,
            });
        }

        // Past & future slots
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 11,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 26, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 26, 9, 30, 0),
                Capacity = 5,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 12,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 28, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 28, 9, 30, 0),
                Capacity = 5,
                Status = "OPEN",
            });

        _db.Appointments.AddRange(
            new Appointment
            {
                Id = 114,
                AppointmentCode = "APT-202610-0114",
                PatientId = 2,
                SlotId = 11,
                ReasonForVisit = "Lịch hẹn ngày hôm qua",
                Status = "BOOKED",
            },
            new Appointment
            {
                Id = 115,
                AppointmentCode = "APT-202610-0115",
                PatientId = 2,
                SlotId = 12,
                ReasonForVisit = "Lịch hẹn ngày mai",
                Status = "BOOKED",
            });

        await _db.SaveChangesAsync();

        foreach (var code in new[]
                 {
                     "APT-202610-0111",
                     "APT-202610-0112",
                     "APT-202610-0113",
                     "APT-202610-0114",
                     "APT-202610-0115",
                 })
        {
            await Assert.ThrowsAsync<ConflictException>(() =>
                _service.CheckInScheduledPatientAsync(
                    actorUserId: 5,
                    appointmentIdOrCode: code,
                    request: new CheckInScheduledPatientRequest(IsIdentityVerified: true)));
        }

        // Maintenance room -> ConflictException
        var room = await _db.HospitalRooms.SingleAsync(r => r.Id == 1);
        room.Status = "MAINTENANCE";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0101",
                request: new CheckInScheduledPatientRequest(IsIdentityVerified: true)));

        room.Status = "ACTIVE";

        // Inactive doctor -> ConflictException
        var doctor = await _db.Doctors.SingleAsync(d => d.Id == 1);
        doctor.IsActive = false;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CheckInScheduledPatientAsync(
                actorUserId: 5,
                appointmentIdOrCode: "APT-202610-0101",
                request: new CheckInScheduledPatientRequest(IsIdentityVerified: true)));
    }

    [Fact]
    public async Task CheckInScheduledPatientAsync_TwoLayerAuthorization_EnforcesRolesPermissionsStatusAndHospitalScope()
    {
        var validRequest = new CheckInScheduledPatientRequest(IsIdentityVerified: true);

        // actorUserId <= 0 -> UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.CheckInScheduledPatientAsync(0, "APT-202610-0100", validRequest));

        // Unknown user -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CheckInScheduledPatientAsync(999, "APT-202610-0100", validRequest));

        // PATIENT (UserId = 1) or DOCTOR (UserId = 3) -> ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckInScheduledPatientAsync(1, "APT-202610-0100", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckInScheduledPatientAsync(3, "APT-202610-0100", validRequest));

        // Cross-hospital reception staff (UserId = 25 in Hospital 2) checking in Hospital 1 appointment -> ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckInScheduledPatientAsync(25, "APT-202610-0100", validRequest));

        // Receptionist missing PATIENT_CHECKIN permission when RolePermissions are configured -> ForbiddenException
        var checkInRolePerm = await _db.RolePermissions
            .SingleAsync(rp => rp.RoleId == 3 && rp.PermissionId == 3);
        _db.RolePermissions.Remove(checkInRolePerm);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckInScheduledPatientAsync(5, "APT-202610-0100", validRequest));

        _db.RolePermissions.Add(new RolePermission { RoleId = 3, PermissionId = 3 });

        // Locked user -> ForbiddenException
        var receptionUser = await _db.Users.SingleAsync(u => u.Id == 5);
        receptionUser.LockedUntil = new DateTime(2026, 10, 27, 5, 0, 0, DateTimeKind.Utc);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckInScheduledPatientAsync(5, "APT-202610-0100", validRequest));

        receptionUser.LockedUntil = null;

        // Inactive staff -> ForbiddenException
        var receptionStaff = await _db.Staff.SingleAsync(s => s.UserId == 5);
        receptionStaff.Status = "INACTIVE";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CheckInScheduledPatientAsync(5, "APT-202610-0100", validRequest));

        receptionStaff.Status = "ACTIVE";
        await _db.SaveChangesAsync();

        // SYSTEM_ADMIN (UserId = 13, no hospital-bound staff required) succeeds across hospitals
        _db.Appointments.Add(new Appointment
        {
            Id = 200,
            AppointmentCode = "APT-202610-0200",
            PatientId = 2,
            SlotId = 20,
            ReasonForVisit = "Khám Nội cơ sở 2",
            Status = "BOOKED",
        });
        await _db.SaveChangesAsync();

        var sysAdminResult = await _service.CheckInScheduledPatientAsync(
            actorUserId: 13,
            appointmentIdOrCode: "APT-202610-0200",
            request: validRequest);

        Assert.Equal(200L, sysAdminResult.AppointmentId);
        Assert.Equal(2L, sysAdminResult.HospitalId);
        Assert.Equal("CHECKED_IN", sysAdminResult.AppointmentStatus);
    }

    [Fact]
    public async Task GetCheckInResultByAppointmentAndVisitAsync_ReturnsFullResultWithReconstructedAuditVitals_AndIsReadOnly()
    {
        var checkInResult = await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0100",
            request: new CheckInScheduledPatientRequest(
                ReasonForVisit: "Đau thượng vị kèm ợ chua",
                BloodPressure: "125/82",
                HeartRate: 78,
                Temperature: 36.8,
                IsIdentityVerified: true,
                IsInsuranceVerified: true,
                Priority: "NORMAL"),
            ipAddress: "127.0.0.1");

        var visitCountBefore = await _db.Visits.CountAsync();
        var queueCountBefore = await _db.QueueEntries.CountAsync();
        var auditCountBefore = await _db.AuditLogs.CountAsync();

        // 1. Lookup by appointment code
        var byApptCode = await _service.GetCheckInResultByAppointmentAsync(5, "APT-202610-0100");
        Assert.Equal(checkInResult.VisitId, byApptCode.VisitId);
        Assert.Equal(checkInResult.VisitCode, byApptCode.VisitCode);
        Assert.Equal(checkInResult.TicketCode, byApptCode.TicketCode);
        Assert.Equal("A001", byApptCode.TicketNo);
        Assert.Equal(500L, byApptCode.QueueEntryId);
        Assert.Equal(1, byApptCode.QueueNumber);
        Assert.Equal("001", byApptCode.QueueNumberFormatted);
        Assert.Equal("WAITING", byApptCode.QueueStatus);
        Assert.Equal("PRESENT", byApptCode.PresenceStatus);
        Assert.Equal("ON_TIME_OR_GRACE", byApptCode.ArrivalClass);
        Assert.Equal("NORMAL", byApptCode.Priority);
        Assert.Equal(100L, byApptCode.AppointmentId);
        Assert.Equal("APT-202610-0100", byApptCode.AppointmentCode);
        Assert.Equal("CHECKED_IN", byApptCode.AppointmentStatus);
        Assert.Equal("WAITING", byApptCode.VisitStatus);
        Assert.Equal("SCHEDULED", byApptCode.VisitType);
        Assert.Equal(1L, byApptCode.PatientId);
        Assert.Equal("PAT-0001", byApptCode.PatientCode);
        Assert.Equal("Nguyễn Văn An", byApptCode.PatientName);
        Assert.Equal(1L, byApptCode.HospitalId);
        Assert.Equal("Khoa Tiêu hóa", byApptCode.DepartmentName);
        Assert.Equal("RM-001", byApptCode.RoomCode);
        Assert.Equal("Khám Tiêu hóa 101", byApptCode.RoomName);
        Assert.Equal("BS Nguyễn Minh Đức", byApptCode.DoctorName);
        Assert.Equal("Đau thượng vị kèm ợ chua", byApptCode.ReasonForVisit);
        Assert.Equal("125/82", byApptCode.BloodPressure);
        Assert.Equal(78, byApptCode.HeartRate);
        Assert.Equal(36.8, byApptCode.Temperature);
        Assert.True(byApptCode.IsIdentityVerified);
        Assert.True(byApptCode.IsInsuranceVerified);
        Assert.Equal("08:50", byApptCode.CheckedInAt);
        Assert.Equal(3L, byApptCode.CheckedInByStaffId);
        Assert.Equal("Lê Thị Tiếp Đón", byApptCode.CheckedInByStaffName);
        Assert.Equal(3, byApptCode.NextStepInstructions.Count);

        // 2. Lookup by numeric appointment ID
        var byApptId = await _service.GetCheckInResultByAppointmentAsync(5, "100");
        Assert.Equal(checkInResult.VisitId, byApptId.VisitId);
        Assert.Equal("125/82", byApptId.BloodPressure);

        // 3. Lookup by visit code and numeric visit ID
        var byVisitCode = await _service.GetCheckInResultByVisitAsync(5, checkInResult.VisitCode);
        Assert.Equal(checkInResult.VisitId, byVisitCode.VisitId);
        Assert.Equal("A001", byVisitCode.TicketNo);
        Assert.Equal("125/82", byVisitCode.BloodPressure);

        var byVisitId = await _service.GetCheckInResultByVisitAsync(5, checkInResult.VisitId.ToString());
        Assert.Equal(checkInResult.VisitCode, byVisitId.VisitCode);

        // 4. Cross-lookup resilience (passing VIS-... to Appointment endpoint or APT-... to Visit endpoint)
        var crossVisitOnAppt = await _service.GetCheckInResultByAppointmentAsync(5, checkInResult.VisitCode);
        Assert.Equal(checkInResult.VisitId, crossVisitOnAppt.VisitId);

        var crossApptOnVisit = await _service.GetCheckInResultByVisitAsync(5, "APT-202610-0100");
        Assert.Equal(checkInResult.VisitId, crossApptOnVisit.VisitId);

        // Read-only invariant: no extra rows written
        Assert.Equal(visitCountBefore, await _db.Visits.CountAsync());
        Assert.Equal(queueCountBefore, await _db.QueueEntries.CountAsync());
        Assert.Equal(auditCountBefore, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task GetCheckInResult_ReflectsUpdatedRoomAfterTransfer_AndZeroWaitTimeWhenCalledOrCompleted()
    {
        await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0100",
            request: new CheckInScheduledPatientRequest(IsIdentityVerified: true));

        var secondCheckIn = await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0101",
            request: new CheckInScheduledPatientRequest(IsIdentityVerified: true));

        var initialView = await _service.GetCheckInResultByVisitAsync(5, secondCheckIn.VisitCode);
        Assert.Equal(1, initialView.WaitingCountBefore);
        Assert.Equal(15, initialView.EstimatedWaitMinutes);
        Assert.Equal("RM-001", initialView.RoomCode);

        // Simulate room transfer (UC27) to a new room in Hospital 1
        _db.HospitalRooms.Add(new HospitalRoom
        {
            Id = 2,
            HospitalId = 1,
            DepartmentId = 2,
            RoomCode = "RM-002",
            Name = "Khám Tiêu hóa 102",
            RoomType = "EXAMINATION",
            Location = "Tầng 1 - Khu B",
            Capacity = 2,
            Status = "ACTIVE",
        });

        var visit = await _db.Visits.SingleAsync(v => v.Id == secondCheckIn.VisitId);
        visit.ExaminationRoomId = 2;

        var queueEntry = await _db.QueueEntries.SingleAsync(q => q.Id == secondCheckIn.QueueEntryId);
        queueEntry.Status = "CALLED";
        await _db.SaveChangesAsync();

        var updatedView = await _service.GetCheckInResultByAppointmentAsync(5, "APT-202610-0101");
        Assert.Equal(2L, updatedView.RoomId);
        Assert.Equal("RM-002", updatedView.RoomCode);
        Assert.Equal("Khám Tiêu hóa 102", updatedView.RoomName);
        Assert.Equal("Tầng 1 - Khu B", updatedView.RoomLocation);
        Assert.Contains("Khám Tiêu hóa 102 (RM-002)", updatedView.NextStepInstructions[0]);
        Assert.Equal("CALLED", updatedView.QueueStatus);
        Assert.Equal(0, updatedView.WaitingCountBefore);
        Assert.Equal(0, updatedView.EstimatedWaitMinutes);
    }

    [Fact]
    public async Task GetCheckInResult_ValidationNotFoundAndConflictGuards_ThrowsExpectedExceptions()
    {
        // 400 ValidationException for invalid identifiers
        foreach (var invalid in new[] { "", "   ", "0", "-5", "bad@code", new string('A', 45) })
        {
            await Assert.ThrowsAsync<ValidationException>(() =>
                _service.GetCheckInResultByAppointmentAsync(5, invalid));
            await Assert.ThrowsAsync<ValidationException>(() =>
                _service.GetCheckInResultByVisitAsync(5, invalid));
        }

        // 404 NotFoundException for non-existent appointment or visit
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetCheckInResultByAppointmentAsync(5, "APT-202610-9999"));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetCheckInResultByAppointmentAsync(5, "99999"));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetCheckInResultByVisitAsync(5, "VIS-202610-9999"));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetCheckInResultByVisitAsync(5, "99999"));

        // 409 ConflictException when appointment is still BOOKED (not checked in yet)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetCheckInResultByAppointmentAsync(5, "APT-202610-0101"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetCheckInResultByVisitAsync(5, "101"));

        // 409 ConflictException when appointment is CANCELLED or NO_SHOW
        var appt102 = await _db.Appointments.SingleAsync(a => a.Id == 102);
        appt102.Status = "CANCELLED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetCheckInResultByAppointmentAsync(5, "APT-202610-0102"));

        appt102.Status = "NO_SHOW";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetCheckInResultByAppointmentAsync(5, "APT-202610-0102"));

        // 409 ConflictException when checked-in visit or queue entry is later CANCELLED
        var checkedIn = await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0100",
            request: new CheckInScheduledPatientRequest(IsIdentityVerified: true));

        var queueEntry = await _db.QueueEntries.SingleAsync(q => q.Id == checkedIn.QueueEntryId);
        queueEntry.Status = "CANCELLED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetCheckInResultByVisitAsync(5, checkedIn.VisitCode));

        queueEntry.Status = "WAITING";
        var visit = await _db.Visits.SingleAsync(v => v.Id == checkedIn.VisitId);
        visit.Status = "CANCELLED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetCheckInResultByVisitAsync(5, checkedIn.VisitCode));
    }

    [Fact]
    public async Task GetCheckInResult_TwoLayerAuthorizationAndHospitalIsolation_EnforcesRoleAndHospitalBoundary()
    {
        var checkedIn = await _service.CheckInScheduledPatientAsync(
            actorUserId: 5,
            appointmentIdOrCode: "APT-202610-0100",
            request: new CheckInScheduledPatientRequest(IsIdentityVerified: true));

        // 401 UnauthorizedAccessException for invalid actorUserId
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetCheckInResultByAppointmentAsync(0, "APT-202610-0100"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetCheckInResultByVisitAsync(-1, checkedIn.VisitCode));

        // 403 ForbiddenException for PATIENT (1) and DOCTOR (3)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetCheckInResultByAppointmentAsync(1, "APT-202610-0100"));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetCheckInResultByVisitAsync(3, checkedIn.VisitCode));

        // 403 ForbiddenException for cross-hospital reception staff (UserId = 25 in Hospital 2)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetCheckInResultByAppointmentAsync(25, "APT-202610-0100"));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetCheckInResultByVisitAsync(25, checkedIn.VisitCode));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetCheckInResultByAppointmentAsync(25, "APT-202610-0101"));

        // HOSPITAL_ADMIN (11) in Hospital 1 and SYSTEM_ADMIN (13) succeed
        var hospitalAdminView = await _service.GetCheckInResultByAppointmentAsync(11, "APT-202610-0100");
        Assert.Equal(checkedIn.VisitId, hospitalAdminView.VisitId);

        var sysAdminView = await _service.GetCheckInResultByVisitAsync(13, checkedIn.VisitCode);
        Assert.Equal(checkedIn.VisitId, sysAdminView.VisitId);
    }
}
