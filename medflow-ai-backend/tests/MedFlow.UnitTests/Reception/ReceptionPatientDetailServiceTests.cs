using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Patients;
using MedFlow.Application.Services.Reception;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Reception;

public sealed class ReceptionPatientDetailServiceTests : IDisposable
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class DetailTestDbContext : DbContext, IApplicationDbContext
    {
        public DetailTestDbContext(DbContextOptions<DetailTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for ReceptionPatientDetailService unit tests.");

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
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => throw NotUsed();
        public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
        public DbSet<MedicalRecordRevision> MedicalRecordRevisions => Set<MedicalRecordRevision>();
        public DbSet<VisitDiagnosis> VisitDiagnoses => Set<VisitDiagnosis>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<MedicalRecord>().HasKey(mr => mr.Id);
            modelBuilder.Entity<MedicalRecordRevision>().HasKey(mrr => mrr.Id);
            modelBuilder.Entity<VisitDiagnosis>().HasKey(vd => vd.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);

            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User).WithOne(u => u.Patient).HasForeignKey<Patient>(p => p.UserId);
            });

            modelBuilder.Entity<PatientFamilyLink>(b =>
            {
                b.HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
                b.HasOne(l => l.OwnerPatient).WithMany().HasForeignKey(l => l.OwnerPatientId);
                b.HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
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

    // 2026-10-15 02:00 UTC == 2026-10-15 09:00 Vietnam time (UTC+7)
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateTime TodayLocal = new(2026, 10, 15);

    private readonly DetailTestDbContext _db;
    private readonly ReceptionPatientDetailService _service;

    public ReceptionPatientDetailServiceTests()
    {
        var options = new DbContextOptionsBuilder<DetailTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new DetailTestDbContext(options);
        _service = new ReceptionPatientDetailService(_db, new FixedTimeProvider(FixedUtcNow));

        SeedBaselineData();
    }

    public void Dispose() => _db.Dispose();

    private void SeedBaselineData()
    {
        // Hospitals
        _db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOSP-01", Name = "Bệnh viện Trung tâm MedFlow", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOSP-02", Name = "Bệnh viện Chi nhánh 2", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "HOSP-INACTIVE", Name = "Bệnh viện Tạm ngưng", IsActive = false });

        // Roles & Permissions
        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Tiếp đón" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 7, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" });

        _db.Permissions.AddRange(
            new Permission { Id = 1, Code = "PATIENT_PROFILE_READ" },
            new Permission { Id = 2, Code = "PATIENT_CHECKIN" },
            new Permission { Id = 3, Code = "HOSPITAL_CONFIG" });

        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 3, PermissionId = 1 },
            new RolePermission { RoleId = 3, PermissionId = 2 },
            new RolePermission { RoleId = 6, PermissionId = 3 });

        // Users
        _db.Users.AddRange(
            new User { Id = 1, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 5, Email = "reception01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "hospadmin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "sysadmin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 50, Email = "locked@medflow.test", AccountStatus = AccountStatus.ACTIVE, LockedUntil = FixedUtcNow.UtcDateTime.AddMinutes(30) },
            new User { Id = 51, Email = "suspended@medflow.test", AccountStatus = AccountStatus.INACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 5, RoleId = 3 },
            new UserRole { UserId = 11, RoleId = 6 },
            new UserRole { UserId = 13, RoleId = 7 },
            new UserRole { UserId = 50, RoleId = 3 },
            new UserRole { UserId = 51, RoleId = 3 });

        // Departments, Specialties, Rooms, Staff, Doctors
        _db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "NOI", Name = "Khoa Nội Tổng Quát", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "TIM", Name = "Khoa Tim Mạch", IsActive = true },
            new Department { Id = 3, HospitalId = 2, DepartmentCode = "NHI", Name = "Khoa Nhi Cơ Sở 2", IsActive = true });

        _db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "NOI_TQ", Name = "Nội tổng quát", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "TIM_MACH", Name = "Tim mạch", IsActive = true });

        _db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 101, HospitalId = 1, DepartmentId = 1, RoomCode = "P101", Name = "Phòng 101 - Tầng 1", RoomType = "EXAMINATION", Status = "ACTIVE" },
            new HospitalRoom { Id = 202, HospitalId = 1, DepartmentId = 2, RoomCode = "P202", Name = "Phòng 202 - Tầng 2", RoomType = "EXAMINATION", Status = "ACTIVE" },
            new HospitalRoom { Id = 301, HospitalId = 2, DepartmentId = 3, RoomCode = "P301", Name = "Phòng 301 - Cơ sở 2", RoomType = "EXAMINATION", Status = "ACTIVE" });

        _db.Staff.AddRange(
            new Staff { Id = 5, UserId = 5, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-0005", FullName = "Lễ tân Nguyễn Thị Mai", Status = "ACTIVE" },
            new Staff { Id = 11, UserId = 11, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-0011", FullName = "Quản trị BV Trần Hùng", Status = "ACTIVE" },
            new Staff { Id = 100, HospitalId = 1, DepartmentId = 1, StaffCode = "DOC-STF-1", FullName = "BS.CKII Trần Văn Hùng", Status = "ACTIVE" },
            new Staff { Id = 101, HospitalId = 1, DepartmentId = 2, StaffCode = "DOC-STF-2", FullName = "ThS.BS Lê Hoàng Nam", Status = "ACTIVE" },
            new Staff { Id = 102, HospitalId = 2, DepartmentId = 3, StaffCode = "DOC-STF-3", FullName = "BS.CKI Phạm Thu Hà", Status = "ACTIVE" });

        _db.Doctors.AddRange(
            new Doctor { Id = 10, StaffId = 100, HospitalId = 1, SpecialtyId = 1, DoctorCode = "DOC-0010", IsActive = true },
            new Doctor { Id = 11, StaffId = 101, HospitalId = 1, SpecialtyId = 2, DoctorCode = "DOC-0011", IsActive = true },
            new Doctor { Id = 12, StaffId = 102, HospitalId = 2, SpecialtyId = 1, DoctorCode = "DOC-0012", IsActive = true });

        _db.Services.AddRange(
            new Service { Id = 1, HospitalId = 1, DepartmentId = 1, SpecialtyId = 1, ServiceCode = "SRV-NOI", Name = "Khám Nội tổng quát", ServiceType = "CLINICAL", IsActive = true },
            new Service { Id = 2, HospitalId = 1, DepartmentId = 2, SpecialtyId = 2, ServiceCode = "SRV-TIM", Name = "Khám Tim mạch", ServiceType = "CLINICAL", IsActive = true },
            new Service { Id = 3, HospitalId = 2, DepartmentId = 3, SpecialtyId = 1, ServiceCode = "SRV-NHI", Name = "Khám Nhi", ServiceType = "CLINICAL", IsActive = true });

        // Patients
        _db.Patients.AddRange(
            new Patient
            {
                Id = 10,
                UserId = 1,
                PatientCode = "PAT-0010",
                FullName = "Nguyễn Văn An",
                DateOfBirth = new DateOnly(1988, 5, 14),
                Gender = "MALE",
                Phone = "0901234567",
                Email = null, // falls back to User.Email
                IdentificationHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                Address = "123 Nguyễn Trãi, Quận 1, TP.HCM",
                BloodType = "O+",
                Status = "ACTIVE"
            },
            new Patient
            {
                Id = 11,
                PatientCode = "PAT-0011",
                FullName = "Nguyễn Thị Hoa",
                DateOfBirth = new DateOnly(1990, 8, 20),
                Gender = "FEMALE",
                Phone = "0909888777",
                Email = "hoa.nguyen@medflow.test",
                IdentificationHash = null,
                Address = "123 Nguyễn Trãi, Quận 1, TP.HCM",
                Status = "ACTIVE"
            },
            new Patient
            {
                Id = 12,
                PatientCode = "PAT-INACTIVE",
                FullName = "Trần Văn Bị Khóa",
                DateOfBirth = new DateOnly(1975, 1, 1),
                Gender = "MALE",
                Phone = "0911222333",
                Status = "INACTIVE"
            });

        // Family link: PAT-0010 (Owner) -> PAT-0011 (Member, SPOUSE, FEMALE => "Vợ")
        _db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = 10,
            MemberPatientId = 11,
            Relationship = "SPOUSE",
            VerificationStatus = "VERIFIED",
            VerifiedAt = TodayLocal.AddDays(-30)
        });

        _db.SaveChanges();
    }

    [Theory]
    [InlineData("10")]
    [InlineData("PAT-0010")]
    [InlineData("pat-0010")]
    [InlineData("#PAT-0010")]
    [InlineData("  #pat-0010  ")]
    public async Task GetPatientDetailAsync_ValidIdOrCodeFormats_ReturnsFullPatientDetail(string idOrCode)
    {
        var result = await _service.GetPatientDetailAsync(5, idOrCode);

        Assert.Equal(10, result.Id);
        Assert.Equal("PAT-0010", result.PatientCode);
        Assert.Equal("Nguyễn Văn An", result.FullName);
        Assert.Equal("MALE", result.Gender);
        Assert.Equal("Nam", result.GenderLabel);
        Assert.Equal(38, result.Age); // 2026-10-15 minus 1988-05-14
        Assert.Equal("14/05/1988", result.DateOfBirth);
        Assert.Equal(new DateOnly(1988, 5, 14), result.DateOfBirthIso);
        Assert.True(result.IsIdentityVerified);
        Assert.Equal("****** (Đã xác thực CCCD)", result.Cccd);
        Assert.Equal("****** (Đã xác thực CCCD)", result.IdentityCardMasked);
        Assert.Equal("0901234567", result.Phone);
        Assert.Equal("patient01@medflow.test", result.Email);
        Assert.Null(result.BhytNumber);
        Assert.Null(result.HealthInsuranceNumber);
        Assert.Equal("123 Nguyễn Trãi, Quận 1, TP.HCM", result.Address);
        Assert.Equal("O+", result.BloodType);
        Assert.Equal("ACTIVE", result.Status);

        Assert.NotNull(result.EmergencyContact);
        Assert.Equal("Nguyễn Thị Hoa", result.EmergencyContact!.Name);
        Assert.Equal("Vợ", result.EmergencyContact.Relationship);
        Assert.Equal("0909888777", result.EmergencyContact.Phone);
    }

    [Fact]
    public async Task GetPatientDetailAsync_UnverifiedIdentityAndCounterpartMemberDirection_MapsInverseRelationshipAndFallbacks()
    {
        // Query PAT-0011 (Member in link where Owner is PAT-0010 MALE, SPOUSE => "Chồng")
        var result = await _service.GetPatientDetailAsync(5, "PAT-0011");

        Assert.Equal(11, result.Id);
        Assert.Equal("FEMALE", result.Gender);
        Assert.Equal("Nữ", result.GenderLabel);
        Assert.False(result.IsIdentityVerified);
        Assert.Null(result.IdentityCardMasked);
        Assert.Equal("Chưa cập nhật", result.Cccd);
        Assert.Equal("hoa.nguyen@medflow.test", result.Email);

        Assert.NotNull(result.EmergencyContact);
        Assert.Equal("Nguyễn Văn An", result.EmergencyContact!.Name);
        Assert.Equal("Chồng", result.EmergencyContact.Relationship);
        Assert.Equal("0901234567", result.EmergencyContact.Phone);
    }

    [Fact]
    public async Task GetPatientDetailAsync_TodayAppointmentTieBreakingAndStatusTransitions_MapsBookedArrivedInProgressAndDone()
    {
        // Slot 1: 08:00 - 08:30 (BOOKED), Slot 2: 09:30 - 10:00 (CHECKED_IN)
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 500,
                ServiceId = 1,
                DoctorId = 10,
                RoomId = 101,
                StartsAt = TodayLocal.AddHours(8),
                EndsAt = TodayLocal.AddHours(8).AddMinutes(30),
                Capacity = 10,
                Status = "OPEN"
            },
            new ExaminationSlot
            {
                Id = 501,
                ServiceId = 2,
                DoctorId = 11,
                RoomId = 202,
                StartsAt = TodayLocal.AddHours(9).AddMinutes(30),
                EndsAt = TodayLocal.AddHours(10),
                Capacity = 10,
                Status = "OPEN"
            });

        _db.Appointments.AddRange(
            new Appointment
            {
                Id = 1000,
                AppointmentCode = "APT-MORNING-BOOKED",
                PatientId = 10,
                SlotId = 500,
                Status = "BOOKED"
            },
            new Appointment
            {
                Id = 1001,
                AppointmentCode = "APT-CHECKED-IN",
                PatientId = 10,
                SlotId = 501,
                Status = "CHECKED_IN"
            });
        await _db.SaveChangesAsync();

        // 1. CHECKED_IN wins over earlier BOOKED and maps to ARRIVED
        var arrivedDetail = await _service.GetPatientDetailAsync(5, "PAT-0010");
        Assert.NotNull(arrivedDetail.TodayAppointment);
        Assert.Equal(1001, arrivedDetail.TodayAppointment!.Id);
        Assert.Equal("APT-CHECKED-IN", arrivedDetail.TodayAppointment.AppointmentCode);
        Assert.Equal("09:30", arrivedDetail.TodayAppointment.Time);
        Assert.Equal("09:30 - 10:00", arrivedDetail.TodayAppointment.TimeRange);
        Assert.Equal("ThS.BS Lê Hoàng Nam", arrivedDetail.TodayAppointment.DoctorName);
        Assert.Equal("Khoa Tim Mạch", arrivedDetail.TodayAppointment.Department);
        Assert.Equal("Phòng 202 - Tầng 2", arrivedDetail.TodayAppointment.Room);
        Assert.Equal("ARRIVED", arrivedDetail.TodayAppointment.Status);

        // 2. Active Visit in IN_PROGRESS transitions TodayAppointment.Status to IN_PROGRESS
        var visit = new Visit
        {
            Id = 2001,
            VisitCode = "VIS-2026-2001",
            PatientId = 10,
            AppointmentId = 1001,
            AssignedDoctorId = 10, // Reassigned to Doctor 10 in Room 101
            ExaminationRoomId = 101,
            Status = "IN_PROGRESS",
            CheckedInAt = TodayLocal.AddHours(9).AddMinutes(15)
        };
        _db.Visits.Add(visit);
        await _db.SaveChangesAsync();

        var inProgressDetail = await _service.GetPatientDetailAsync(5, "PAT-0010");
        Assert.NotNull(inProgressDetail.TodayAppointment);
        Assert.Equal("IN_PROGRESS", inProgressDetail.TodayAppointment!.Status);
        Assert.Equal(2001, inProgressDetail.TodayAppointment.VisitId);
        Assert.Equal("VIS-2026-2001", inProgressDetail.TodayAppointment.VisitCode);
        Assert.Equal("BS.CKII Trần Văn Hùng", inProgressDetail.TodayAppointment.DoctorName);
        Assert.Equal("Phòng 101 - Tầng 1", inProgressDetail.TodayAppointment.Room);

        // 3. Completed Visit transitions TodayAppointment.Status to DONE
        visit.Status = "COMPLETED";
        visit.CompletedAt = TodayLocal.AddHours(9).AddMinutes(50);
        await _db.SaveChangesAsync();

        var doneDetail = await _service.GetPatientDetailAsync(5, "PAT-0010");
        Assert.NotNull(doneDetail.TodayAppointment);
        Assert.Equal("DONE", doneDetail.TodayAppointment!.Status);
    }

    [Fact]
    public async Task GetPatientDetailAsync_PastVisitsDiagnosisFallbacksAndHospitalScoping_FiltersByActorHospital()
    {
        // Visit 1 (Hospital 1, older): diagnosis from MedicalRecordRevision.ClinicalConclusion
        _db.Visits.Add(new Visit
        {
            Id = 3001,
            VisitCode = "VIS-H1-OLD",
            PatientId = 10,
            AssignedDoctorId = 10,
            ExaminationRoomId = 101,
            ReasonForVisit = "Đau đầu kéo dài",
            Status = "COMPLETED",
            CheckedInAt = TodayLocal.AddDays(-20).AddHours(8),
            CompletedAt = TodayLocal.AddDays(-20).AddHours(9)
        });
        _db.MedicalRecords.Add(new MedicalRecord
        {
            Id = 4001,
            VisitId = 3001,
            CreatedByDoctorId = 10,
            Status = "FINALIZED",
            FinalizedAt = TodayLocal.AddDays(-20).AddHours(9)
        });
        _db.MedicalRecordRevisions.Add(new MedicalRecordRevision
        {
            Id = 5001,
            MedicalRecordId = 4001,
            RevisionNo = 1,
            ClinicalConclusion = "Theo dõi đau nửa đầu Migraine",
            IsConfirmed = true,
            RecordedAt = TodayLocal.AddDays(-20).AddHours(9)
        });

        // Visit 2 (Hospital 1, newer): primary diagnosis from VisitDiagnoses
        _db.Visits.Add(new Visit
        {
            Id = 3002,
            VisitCode = "VIS-H1-NEW",
            PatientId = 10,
            AssignedDoctorId = 11,
            ExaminationRoomId = 202,
            ReasonForVisit = "Đau tức ngực",
            Status = "COMPLETED",
            CheckedInAt = TodayLocal.AddDays(-5).AddHours(10),
            CompletedAt = TodayLocal.AddDays(-5).AddHours(11)
        });
        _db.VisitDiagnoses.AddRange(
            new VisitDiagnosis
            {
                Id = 6001,
                VisitId = 3002,
                IsPrimary = false,
                NameSnapshot = "Rối loạn lipid máu",
                Status = "CONFIRMED",
                ConfirmedAt = TodayLocal.AddDays(-5).AddHours(10).AddMinutes(40)
            },
            new VisitDiagnosis
            {
                Id = 6002,
                VisitId = 3002,
                IsPrimary = true,
                ClinicalDiagnosis = "Tăng huyết áp nguyên phát độ 2",
                NameSnapshot = "Bệnh tăng huyết áp vô căn",
                Status = "CONFIRMED",
                ConfirmedAt = TodayLocal.AddDays(-5).AddHours(10).AddMinutes(45)
            });

        // Visit 3 (Hospital 2): should be excluded for Hospital 1 Reception/Admin, included for SYSTEM_ADMIN
        _db.Visits.Add(new Visit
        {
            Id = 3003,
            VisitCode = "VIS-H2-OTHER",
            PatientId = 10,
            AssignedDoctorId = 12,
            ExaminationRoomId = 301,
            ReasonForVisit = "Khám tổng quát tại cơ sở 2",
            Status = "COMPLETED",
            CheckedInAt = TodayLocal.AddDays(-2).AddHours(8),
            CompletedAt = TodayLocal.AddDays(-2).AddHours(9)
        });

        await _db.SaveChangesAsync();

        // Reception at Hospital 1 sees only the 2 Hospital 1 visits, newest first
        var receptionResult = await _service.GetPatientDetailAsync(5, "PAT-0010");
        Assert.Equal(2, receptionResult.TotalPastVisits);
        Assert.Equal(2, receptionResult.PastVisits.Count);

        Assert.Equal(3002, receptionResult.PastVisits[0].Id);
        Assert.Equal("VIS-H1-NEW", receptionResult.PastVisits[0].VisitCode);
        Assert.Equal("10/10/2026", receptionResult.PastVisits[0].Date);
        Assert.Equal("Khoa Tim Mạch", receptionResult.PastVisits[0].Department);
        Assert.Equal("ThS.BS Lê Hoàng Nam", receptionResult.PastVisits[0].DoctorName);
        Assert.Equal("Tăng huyết áp nguyên phát độ 2", receptionResult.PastVisits[0].Diagnosis);

        Assert.Equal(3001, receptionResult.PastVisits[1].Id);
        Assert.Equal("VIS-H1-OLD", receptionResult.PastVisits[1].VisitCode);
        Assert.Equal("25/09/2026", receptionResult.PastVisits[1].Date);
        Assert.Equal("Khoa Nội Tổng Quát", receptionResult.PastVisits[1].Department);
        Assert.Equal("BS.CKII Trần Văn Hùng", receptionResult.PastVisits[1].DoctorName);
        Assert.Equal("Theo dõi đau nửa đầu Migraine", receptionResult.PastVisits[1].Diagnosis);

        // SYSTEM_ADMIN (userId = 13) sees all 3 visits across hospitals
        var sysAdminResult = await _service.GetPatientDetailAsync(13, "PAT-0010");
        Assert.Equal(3, sysAdminResult.TotalPastVisits);
        Assert.Equal(3, sysAdminResult.PastVisits.Count);
        Assert.Equal("VIS-H2-OTHER", sysAdminResult.PastVisits[0].VisitCode);
        Assert.Equal("Khám tổng quát tại cơ sở 2", sysAdminResult.PastVisits[0].Diagnosis);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetPatientDetailAsync_InvalidActorUserId_ThrowsUnauthorizedAccessException(long actorUserId)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetPatientDetailAsync(actorUserId, "PAT-0010"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("_PAT0010")]
    [InlineData("PAT@0010")]
    [InlineData("PAT-0010-1234567890123456789012345678901234567890")]
    public async Task GetPatientDetailAsync_InvalidPatientIdentifier_ThrowsValidationException(string idOrCode)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetPatientDetailAsync(5, idOrCode));
    }

    [Theory]
    [InlineData(1)]  // PATIENT
    [InlineData(3)]  // DOCTOR
    [InlineData(50)] // Locked RECEPTION
    [InlineData(51)] // Suspended RECEPTION
    public async Task GetPatientDetailAsync_UnauthorizedOrInactiveActor_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetPatientDetailAsync(actorUserId, "PAT-0010"));
    }

    [Fact]
    public async Task GetPatientDetailAsync_UnknownPatient_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetPatientDetailAsync(5, "PAT-9999"));
    }

    [Fact]
    public async Task GetPatientDetailAsync_InactivePatient_ThrowsConflictException()
    {
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.GetPatientDetailAsync(5, "PAT-INACTIVE"));
    }
}
