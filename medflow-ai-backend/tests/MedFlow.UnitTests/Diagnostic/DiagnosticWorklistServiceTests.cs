using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Diagnostic;

public sealed class DiagnosticWorklistServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DiagnosticWorklistTestDbContext : DbContext, IApplicationDbContext
    {
        public DiagnosticWorklistTestDbContext(DbContextOptions<DiagnosticWorklistTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DiagnosticWorklistService unit tests.");

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
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => throw NotUsed();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
        public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
        public DbSet<MedicalRecordRevision> MedicalRecordRevisions => Set<MedicalRecordRevision>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
        public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => Set<DiagnosticResultVersion>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<Specialty>().HasKey(sp => sp.Id);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Doctor>()
                .HasOne(d => d.Staff)
                .WithOne(s => s.Doctor)
                .HasForeignKey<Doctor>(d => d.StaffId);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<ServicePrice>().HasKey(sp => sp.Id);
            modelBuilder.Entity<ExaminationSlot>().HasKey(es => es.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<MedicalRecord>().HasKey(mr => mr.Id);
            modelBuilder.Entity<MedicalRecordRevision>().HasKey(rev => rev.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(o => o.Id);
            modelBuilder.Entity<DiagnosticResult>().HasKey(r => r.Id);
            modelBuilder.Entity<DiagnosticResultVersion>().HasKey(v => v.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
        }
    }

    // 2026-10-15 02:00 UTC = 2026-10-15 09:00 Vietnam time (UTC+7)
    private static readonly DateTime FixedNowUtc = new(2026, 10, 15, 2, 0, 0, DateTimeKind.Utc);

    private readonly DiagnosticWorklistTestDbContext _dbContext;
    private readonly DiagnosticWorklistService _service;

    public DiagnosticWorklistServiceTests()
    {
        var options = new DbContextOptionsBuilder<DiagnosticWorklistTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DiagnosticWorklistTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new DiagnosticWorklistTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new DiagnosticWorklistService(_dbContext, timeProvider);
    }

    private static void SeedData(DiagnosticWorklistTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-DEMO-01", Name = "Bệnh viện Đa khoa Trung tâm MedFlow", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-HCM-02", Name = "Cơ sở TP.HCM", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "HOS-OFF-03", Name = "Cơ sở Ngừng Hoạt Động", IsActive = false });

        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DIA", Name = "Khoa Chẩn đoán Hình ảnh & Xét nghiệm", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DIA-OFF", Name = "Khoa CLS Tạm Ngừng", IsActive = false },
            new Department { Id = 3, HospitalId = 2, DepartmentCode = "DIA-2", Name = "Khoa CLS Cơ sở 2", IsActive = true });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "LAB", Name = "Xét nghiệm", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true },
            new Specialty { Id = 4, SpecialtyCode = "IMG", Name = "Chẩn đoán hình ảnh", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 101,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-201",
                Name = "Phòng X-quang 201",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2, Khu A",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 102,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-202",
                Name = "Phòng Xét nghiệm 202",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2, Khu B",
                Capacity = 3,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 103,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-203",
                Name = "Phòng Điện tim 203",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2, Khu C",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 201,
                HospitalId = 2,
                DepartmentId = 3,
                RoomCode = "DIA-301",
                Name = "Phòng CLS Cơ sở 2",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 3",
                Capacity = 2,
                Status = "ACTIVE",
            });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Lễ tân" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Nhân viên Cận lâm sàng" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 8, Code = "DIAGNOSTIC_STAFF", Name = "Nhân viên CLS Không Quyền" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "DIAGNOSTIC_RESULT_WRITE" },
            new Permission { Id = 2, Code = "QUEUE_MANAGE" },
            new Permission { Id = 3, Code = "DIAGNOSTIC_ORDER" },
            new Permission { Id = 4, Code = "HOSPITAL_CONFIG" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 4, PermissionId = 1 },
            new RolePermission { RoleId = 4, PermissionId = 2 },
            new RolePermission { RoleId = 8, PermissionId = 4 });

        db.Users.AddRange(
            new User { Id = 7, Email = "diagnostic_staff01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 8, Email = "diagnostic_staff02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User
            {
                Id = 14,
                Email = "locked_diag@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = FixedNowUtc.AddMinutes(30),
            },
            new User { Id = 15, Email = "inactive_user_diag@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 16, Email = "inactive_staff_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 17, Email = "no_staff_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 18, Email = "no_hospital_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 19, Email = "inactive_hospital_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "inactive_dept_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 21, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 22, Email = "hosp_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 23, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 24, Email = "reception01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 25, Email = "noperm_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 7, RoleId = 4 },
            new UserRole { UserId = 8, RoleId = 4 },
            new UserRole { UserId = 14, RoleId = 4 },
            new UserRole { UserId = 15, RoleId = 4 },
            new UserRole { UserId = 16, RoleId = 4 },
            new UserRole { UserId = 17, RoleId = 4 },
            new UserRole { UserId = 18, RoleId = 4 },
            new UserRole { UserId = 19, RoleId = 4 },
            new UserRole { UserId = 20, RoleId = 4 },
            new UserRole { UserId = 21, RoleId = 2 },
            new UserRole { UserId = 22, RoleId = 6 },
            new UserRole { UserId = 23, RoleId = 1 },
            new UserRole { UserId = 24, RoleId = 3 },
            new UserRole { UserId = 25, RoleId = 8 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 5,
                UserId = 7,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-005",
                FullName = "KTV Hoàng Anh",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 6,
                UserId = null,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-006",
                FullName = "BS.CKI KTV Võ Nam",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 8,
                UserId = 8,
                HospitalId = 2,
                DepartmentId = 3,
                StaffCode = "STF-008",
                FullName = "KTV Cơ sở 2",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 14,
                UserId = 14,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-014",
                FullName = "KTV Tạm khóa",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 15,
                UserId = 15,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-015",
                FullName = "KTV Tài khoản ngưng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 16,
                UserId = 16,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-016",
                FullName = "KTV Nhân sự ngưng",
                Status = "INACTIVE",
            },
            new Staff
            {
                Id = 18,
                UserId = 18,
                HospitalId = null,
                DepartmentId = null,
                StaffCode = "STF-018",
                FullName = "KTV Chưa gán viện",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 19,
                UserId = 19,
                HospitalId = 3,
                DepartmentId = null,
                StaffCode = "STF-019",
                FullName = "KTV Viện ngưng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 20,
                UserId = 20,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-020",
                FullName = "KTV Khoa ngưng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 25,
                UserId = 25,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-025",
                FullName = "KTV Thiếu quyền",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 101,
                UserId = 21,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-DOC-01",
                FullName = "Trần Minh Hoàng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 201,
                UserId = null,
                HospitalId = 2,
                DepartmentId = 3,
                StaffCode = "STF-DOC-02",
                FullName = "Phạm Quốc Bảo",
                Status = "ACTIVE",
            });

        db.Doctors.AddRange(
            new Doctor
            {
                Id = 1,
                StaffId = 101,
                HospitalId = 1,
                SpecialtyId = 1,
                DoctorCode = "DOC-001",
                ProfessionalTitle = "BS.CKI",
                IsActive = true,
            },
            new Doctor
            {
                Id = 2,
                StaffId = 201,
                HospitalId = 2,
                SpecialtyId = 2,
                DoctorCode = "DOC-002",
                ProfessionalTitle = "ThS.BS",
                IsActive = true,
            });

        db.Services.AddRange(
            new Service
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "SRV-LAB-01",
                Name = "Tổng phân tích tế bào máu ngoại vi (CBC)",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 6,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 4,
                ServiceCode = "SRV-XRAY-01",
                Name = "Chụp X-quang ngực thẳng",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 7,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 2,
                ServiceCode = "SRV-ECG-01",
                Name = "Điện tâm đồ thường quy (ECG)",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = false,
                IsActive = true,
            },
            new Service
            {
                Id = 8,
                HospitalId = 2,
                DepartmentId = 3,
                SpecialtyId = 1,
                ServiceCode = "SRV-H2-LAB",
                Name = "Sinh hóa máu Cơ sở 2",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            });

        db.ServicePrices.AddRange(
            new ServicePrice
            {
                Id = 1,
                ServiceId = 5,
                PriceAmount = 120000m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0),
            },
            new ServicePrice
            {
                Id = 2,
                ServiceId = 6,
                PriceAmount = 180000m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0),
            },
            new ServicePrice
            {
                Id = 3,
                ServiceId = 7,
                PriceAmount = 150000m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0),
            },
            new ServicePrice
            {
                Id = 4,
                ServiceId = 8,
                PriceAmount = 200000m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0),
            });

        db.QueueServicePoints.AddRange(
            new QueueServicePoint
            {
                Id = 3,
                HospitalId = 1,
                RoomId = 102,
                ServiceId = 5,
                PointType = "DIAGNOSTIC",
                Name = "Điểm Xét nghiệm 202",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 4,
                HospitalId = 1,
                RoomId = 101,
                ServiceId = 6,
                PointType = "DIAGNOSTIC",
                Name = "Điểm X-quang 201",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 5,
                HospitalId = 1,
                RoomId = 103,
                ServiceId = 7,
                PointType = "DIAGNOSTIC",
                Name = "Điểm Điện tim 203",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 20,
                HospitalId = 2,
                RoomId = 201,
                ServiceId = 8,
                PointType = "DIAGNOSTIC",
                Name = "Điểm CLS Cơ sở 2",
                IsActive = true,
            });

        db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                PatientCode = "PAT-0001",
                FullName = "Nguyễn Văn An",
                Gender = "MALE",
                DateOfBirth = new DateOnly(1980, 5, 10),
                BloodType = "O+",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 2,
                PatientCode = "PAT-0002",
                FullName = "Trần Thị Bình",
                Gender = "FEMALE",
                DateOfBirth = new DateOnly(1945, 3, 20),
                BloodType = "A+",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 3,
                PatientCode = "PAT-0003",
                FullName = "Lê Hoàng Cường",
                Gender = "MALE",
                DateOfBirth = new DateOnly(1992, 11, 5),
                BloodType = "B+",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 4,
                PatientCode = "PAT-0004",
                FullName = "Phạm Thu Dung",
                Gender = "FEMALE",
                DateOfBirth = new DateOnly(1988, 7, 15),
                BloodType = "AB+",
                Status = "ACTIVE",
            });

        db.Visits.AddRange(
            new Visit
            {
                Id = 1,
                VisitCode = "VIS-202610-0001",
                PatientId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 102,
                ReasonForVisit = "Sốt cao, mệt mỏi kéo dài",
                Status = "AWAITING_RESULTS",
                CheckedInAt = new DateTime(2026, 10, 15, 8, 0, 0),
            },
            new Visit
            {
                Id = 2,
                VisitCode = "VIS-202610-0002",
                PatientId = 2,
                AssignedDoctorId = 1,
                ExaminationRoomId = 101,
                ReasonForVisit = "Đau ngực cấp cứu khó thở",
                Status = "IN_PROGRESS",
                CheckedInAt = new DateTime(2026, 10, 15, 8, 15, 0),
            },
            new Visit
            {
                Id = 3,
                VisitCode = "VIS-202610-0003",
                PatientId = 3,
                AssignedDoctorId = 1,
                ExaminationRoomId = 103,
                ReasonForVisit = "Hồi hộp đánh trống ngực",
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 10, 14, 8, 30, 0),
                CompletedAt = new DateTime(2026, 10, 14, 10, 30, 0),
            },
            new Visit
            {
                Id = 4,
                VisitCode = "VIS-202610-0004",
                PatientId = 4,
                AssignedDoctorId = 2,
                ExaminationRoomId = 201,
                ReasonForVisit = "Khám định kỳ Cơ sở 2",
                Status = "IN_PROGRESS",
                CheckedInAt = new DateTime(2026, 10, 15, 8, 45, 0),
            });

        db.MedicalRecords.AddRange(
            new MedicalRecord
            {
                Id = 1,
                VisitId = 1,
                CreatedByDoctorId = 1,
                Status = "DRAFT",
            },
            new MedicalRecord
            {
                Id = 2,
                VisitId = 2,
                CreatedByDoctorId = 1,
                Status = "DRAFT",
            });

        db.MedicalRecordRevisions.AddRange(
            new MedicalRecordRevision
            {
                Id = 1,
                MedicalRecordId = 1,
                RevisionNo = 1,
                PresentingComplaint = "Sốt 38.5 độ C ngày thứ 3",
                RelevantHistoryAllergies = "Dị ứng kháng sinh nhóm Penicillin",
                ClinicalFindings = "Họng đỏ, phổi thông khí rõ",
                RecordedAt = new DateTime(2026, 10, 15, 8, 10, 0),
            },
            new MedicalRecordRevision
            {
                Id = 2,
                MedicalRecordId = 2,
                RevisionNo = 1,
                PresentingComplaint = "Đau tức ngực trái lan lên vai",
                RelevantHistoryAllergies = "Không tiền sử dị ứng thuốc",
                ClinicalFindings = "Nhịp tim nhanh 110 lần/phút",
                RecordedAt = new DateTime(2026, 10, 15, 8, 20, 0),
            });

        db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 11,
                VisitId = 1,
                ServicePointId = 3,
                CurrentServicePointId = 3,
                QueueDate = new DateOnly(2026, 10, 15),
                QueueNumber = 1,
                QueueType = "DIAGNOSTIC",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = new DateTime(2026, 10, 15, 8, 25, 0),
            },
            new QueueEntry
            {
                Id = 12,
                VisitId = 2,
                ServicePointId = 4,
                CurrentServicePointId = 4,
                QueueDate = new DateOnly(2026, 10, 15),
                QueueNumber = 2,
                QueueType = "DIAGNOSTIC",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ManualPriority = "ELDERLY",
                StateVersion = 1,
                ArrivedAt = new DateTime(2026, 10, 15, 8, 30, 0),
            },
            new QueueEntry
            {
                Id = 13,
                VisitId = 2,
                ServicePointId = 5,
                CurrentServicePointId = 5,
                QueueDate = new DateOnly(2026, 10, 15),
                QueueNumber = 3,
                QueueType = "DIAGNOSTIC",
                Status = "READY",
                PresenceStatus = "PRESENT",
                ManualPriority = "ELDERLY",
                StateVersion = 2,
                ReadyAt = new DateTime(2026, 10, 15, 8, 40, 0),
            },
            new QueueEntry
            {
                Id = 14,
                VisitId = 1,
                ServicePointId = 4,
                CurrentServicePointId = 4,
                QueueDate = new DateOnly(2026, 10, 15),
                QueueNumber = 4,
                QueueType = "DIAGNOSTIC",
                Status = "IN_PROGRESS",
                PresenceStatus = "PRESENT",
                ManualPriority = "NORMAL",
                StateVersion = 3,
                StartedAt = new DateTime(2026, 10, 15, 8, 45, 0),
            },
            new QueueEntry
            {
                Id = 15,
                VisitId = 3,
                ServicePointId = 5,
                CurrentServicePointId = 5,
                QueueDate = new DateOnly(2026, 10, 14),
                QueueNumber = 5,
                QueueType = "DIAGNOSTIC",
                Status = "COMPLETED",
                PresenceStatus = "PRESENT",
                ManualPriority = "NORMAL",
                StateVersion = 4,
                StartedAt = new DateTime(2026, 10, 14, 9, 0, 0),
                CompletedAt = new DateTime(2026, 10, 14, 9, 20, 0),
            },
            new QueueEntry
            {
                Id = 16,
                VisitId = 3,
                ServicePointId = 3,
                CurrentServicePointId = 3,
                QueueDate = new DateOnly(2026, 10, 14),
                QueueNumber = 6,
                QueueType = "DIAGNOSTIC",
                Status = "CANCELLED",
                PresenceStatus = "NOT_ARRIVED",
                ManualPriority = "NORMAL",
                StateVersion = 2,
            },
            new QueueEntry
            {
                Id = 20,
                VisitId = 4,
                ServicePointId = 20,
                CurrentServicePointId = 20,
                QueueDate = new DateOnly(2026, 10, 15),
                QueueNumber = 1,
                QueueType = "DIAGNOSTIC",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ManualPriority = "NORMAL",
                StateVersion = 1,
            });

        // Orders:
        // Order 1: Hospital 1, LABORATORY (Service 5), ORDERED, NORMAL, Ticket C001
        // Order 2: Hospital 2, LABORATORY (Service 8), ORDERED, NORMAL -> must be excluded from Hospital 1 worklist and throw ForbiddenException on detail!
        // Order 3: Hospital 1, IMAGING (Service 6), ORDERED, STAT, Ticket C002
        // Order 4: Hospital 1, FUNCTIONAL (Service 7), CONFIRMED, URGENT, Ticket C003
        // Order 5: Hospital 1, IMAGING (Service 6), IN_PROGRESS, URGENT, Ticket C004 (additional order following Order 1)
        // Order 6: Hospital 1, FUNCTIONAL (Service 7), COMPLETED, NORMAL, Ticket C005 (on 2026-10-14)
        // Order 7: Hospital 1, LABORATORY (Service 5), CANCELLED, NORMAL, Ticket C006 (on 2026-10-14)
        db.DiagnosticOrders.AddRange(
            new DiagnosticOrder
            {
                Id = 1,
                OrderCode = "DIA-202610-0001",
                VisitId = 1,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = 11,
                Instruction = "Lấy máu tĩnh mạch lúc đói",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "ORDERED",
                OrderedAt = new DateTime(2026, 10, 15, 8, 25, 0),
            },
            new DiagnosticOrder
            {
                Id = 2,
                OrderCode = "DIA-202610-0002",
                VisitId = 4,
                OrderingDoctorId = 2,
                ServiceId = 8,
                QueueEntryId = 20,
                Instruction = "Chỉ định thuộc Cơ sở 2",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "ORDERED",
                OrderedAt = new DateTime(2026, 10, 15, 8, 46, 0),
            },
            new DiagnosticOrder
            {
                Id = 3,
                OrderCode = "DIA-202610-0003",
                VisitId = 2,
                OrderingDoctorId = 1,
                ServiceId = 6,
                QueueEntryId = 12,
                Instruction = "Chụp X-quang ngực cấp cứu tại giường",
                RequiredForConclusion = true,
                Priority = "STAT",
                Status = "ORDERED",
                OrderedAt = new DateTime(2026, 10, 15, 8, 30, 0),
            },
            new DiagnosticOrder
            {
                Id = 4,
                OrderCode = "DIA-202610-0004",
                VisitId = 2,
                OrderingDoctorId = 1,
                ServiceId = 7,
                QueueEntryId = 13,
                Instruction = "Đo điện tim 12 chuyển đạo khẩn",
                RequiredForConclusion = true,
                Priority = "URGENT",
                Status = "CONFIRMED",
                ConfirmedByStaffId = 5,
                ConfirmedAt = new DateTime(2026, 10, 15, 8, 40, 0),
                OrderedAt = new DateTime(2026, 10, 15, 8, 32, 0),
            },
            new DiagnosticOrder
            {
                Id = 5,
                OrderCode = "DIA-202610-0005",
                VisitId = 1,
                OrderingDoctorId = 1,
                ServiceId = 6,
                QueueEntryId = 14,
                Instruction = "Chụp X-quang kiểm tra thâm nhiễm phổi",
                RequiredForConclusion = false,
                Priority = "URGENT",
                Status = "IN_PROGRESS",
                ConfirmedByStaffId = 5,
                ConfirmedAt = new DateTime(2026, 10, 15, 8, 42, 0),
                OrderedAt = new DateTime(2026, 10, 15, 8, 38, 0),
            },
            new DiagnosticOrder
            {
                Id = 6,
                OrderCode = "DIA-202610-0006",
                VisitId = 3,
                OrderingDoctorId = 1,
                ServiceId = 7,
                QueueEntryId = 15,
                Instruction = "Đo điện tim kiểm tra nhịp xoang",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "COMPLETED",
                ConfirmedByStaffId = 5,
                ConfirmedAt = new DateTime(2026, 10, 14, 8, 55, 0),
                OrderedAt = new DateTime(2026, 10, 14, 8, 50, 0),
            },
            new DiagnosticOrder
            {
                Id = 7,
                OrderCode = "DIA-202610-0007",
                VisitId = 3,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = 16,
                Instruction = "Chỉ định đã hủy do trùng lặp",
                RequiredForConclusion = false,
                Priority = "NORMAL",
                Status = "CANCELLED",
                OrderedAt = new DateTime(2026, 10, 14, 8, 52, 0),
            });

        db.DiagnosticResults.AddRange(
            new DiagnosticResult
            {
                Id = 1,
                DiagnosticOrderId = 5,
                Status = "PENDING_VERIFY",
                FinalizedAt = null,
                FinalizedByStaffId = null,
            },
            new DiagnosticResult
            {
                Id = 2,
                DiagnosticOrderId = 6,
                Status = "FINALIZED",
                FinalizedAt = new DateTime(2026, 10, 14, 9, 25, 0),
                FinalizedByStaffId = 6,
            });

        db.DiagnosticResultVersions.AddRange(
            new DiagnosticResultVersion
            {
                Id = 1,
                DiagnosticResultId = 1,
                VersionNo = 1,
                ResultText = "Hình ảnh rốn phổi đậm nhẹ hai bên",
                Conclusion = "Theo dõi viêm phế quản cấp",
                EnteredByStaffId = 5,
                EnteredAt = new DateTime(2026, 10, 15, 8, 55, 0),
                IsFinalized = false,
            },
            new DiagnosticResultVersion
            {
                Id = 2,
                DiagnosticResultId = 2,
                VersionNo = 1,
                ResultText = "Nhịp xoang đều 78 chu kỳ/phút, trục trung gian",
                Conclusion = "Điện tâm đồ trong giới hạn bình thường",
                ReportFileUrl = "https://cdn.medflow.invalid/reports/dia-0006.pdf",
                EnteredByStaffId = 5,
                EnteredAt = new DateTime(2026, 10, 14, 9, 15, 0),
                IsFinalized = true,
                VerifiedByStaffId = 6,
                VerifiedAt = new DateTime(2026, 10, 14, 9, 25, 0),
            });

        db.AuditLogs.Add(
            new AuditLog
            {
                Id = 100,
                ActorUserId = 21,
                Action = "ADDITIONAL_DIAGNOSTIC_ORDER_CREATED",
                EntityType = "DIAGNOSTIC_ORDER",
                EntityId = "5",
                OccurredAt = new DateTime(2026, 10, 15, 8, 38, 0),
                ChangeData = """{"isAdditionalOrder":true,"orderSequenceNo":2,"followUpToOrderId":1,"followUpToOrderCode":"DIA-202610-0001","additionalOrderReason":"Cần chụp thêm X-quang phổi do sốt cao kèm ho"}""",
            });

        db.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetWorklistAsync_ValidDiagnosticStaff_ReturnsHospitalScopedOrdersStatsServicePointsAndMetadata()
    {
        var result = await _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest());

        Assert.Equal("Bệnh viện Đa khoa Trung tâm MedFlow", result.FacilityName);
        Assert.Equal("HOS-DEMO-01", result.FacilityCode);
        Assert.Equal("Khoa Chẩn đoán Hình ảnh & Xét nghiệm", result.DepartmentName);
        Assert.Equal(7, result.StaffProfile.UserId);
        Assert.Equal("STF-005", result.StaffProfile.StaffCode);
        Assert.Equal(1, result.StaffProfile.HospitalId);

        // Hospital 1 has 6 orders (Ids 1, 3, 4, 5, 6, 7); Order 2 belongs to Hospital 2 and must be excluded.
        Assert.Equal(6, result.TotalCount);
        Assert.Equal(6, result.Items.Count);
        Assert.DoesNotContain(result.Items, x => x.OrderCode == "DIA-202610-0002" || x.OrderId == 2);

        // Stats verification
        Assert.Equal(6, result.Stats.TotalOrders);
        Assert.Equal(2, result.Stats.PendingOrders);
        Assert.Equal(1, result.Stats.ConfirmedOrders);
        Assert.Equal(1, result.Stats.InExecution);
        Assert.Equal(1, result.Stats.CompletedOrders);
        Assert.Equal(1, result.Stats.CancelledOrders);
        Assert.Equal(1, result.Stats.StatOrders);
        Assert.Equal(2, result.Stats.UrgentOrders);
        Assert.Equal(4, result.Stats.MandatoryOrders);
        Assert.Equal(1, result.Stats.PendingResultVerificationCount);
        Assert.Equal(2, result.Stats.LaboratoryCount);
        Assert.Equal(2, result.Stats.ImagingCount);
        Assert.Equal(2, result.Stats.FunctionalCount);

        // Service points buckets verification
        Assert.Equal(3, result.ServicePoints.Count);
        var labSp = Assert.Single(result.ServicePoints, sp => sp.ServicePointId == 3);
        Assert.Equal("sp-003", labSp.FormattedServicePointId);
        Assert.Equal("DIA-202", labSp.RoomCode);
        Assert.Equal("C001", labSp.NextWaitingTicketNo);

        var xraySp = Assert.Single(result.ServicePoints, sp => sp.ServicePointId == 4);
        Assert.Equal("C004", xraySp.CurrentServingTicketNo);
        Assert.Equal("C002", xraySp.NextWaitingTicketNo);

        // Deterministic sort order:
        // 1. ORDERED + STAT (Order 3: DIA-202610-0003)
        // 2. ORDERED + NORMAL (Order 1: DIA-202610-0001)
        // 3. CONFIRMED + URGENT (Order 4: DIA-202610-0004)
        // 4. IN_PROGRESS + URGENT (Order 5: DIA-202610-0005)
        // 5. COMPLETED (Order 6: DIA-202610-0006)
        // 6. CANCELLED (Order 7: DIA-202610-0007)
        Assert.Equal(
            new[]
            {
                "DIA-202610-0003",
                "DIA-202610-0001",
                "DIA-202610-0004",
                "DIA-202610-0005",
                "DIA-202610-0006",
                "DIA-202610-0007",
            },
            result.Items.Select(x => x.OrderCode).ToArray());

        // Check clinical, patient, price & additional-order metadata on Order 1 & Order 5
        var order1 = result.Items.First(x => x.OrderId == 1);
        Assert.Equal("do-0001", order1.Id);
        Assert.Equal("C001", order1.TicketNo);
        Assert.Equal(120000m, order1.UnitPrice);
        Assert.Equal("O+", order1.BloodType);
        Assert.Equal(46, order1.PatientAge);
        Assert.Equal("Nam", order1.PatientGenderLabel);
        Assert.Equal("Dị ứng kháng sinh nhóm Penicillin", order1.AllergiesSummary);
        Assert.Equal("Sốt 38.5 độ C ngày thứ 3", order1.PresentingComplaint);
        Assert.True(order1.CanConfirmOrder);
        Assert.Null(order1.ConfirmDisabledReason);
        Assert.Contains(order1.AllowedActions, a => a.Id == "confirm-order" && a.UcCode == "UC39" && a.Enabled);

        var order5 = result.Items.First(x => x.OrderId == 5);
        Assert.True(order5.IsAdditionalOrder);
        Assert.Equal(2, order5.OrderSequenceNo);
        Assert.Equal(1L, order5.FollowUpToOrderId);
        Assert.Equal("DIA-202610-0001", order5.FollowUpToOrderCode);
        Assert.Equal("Cần chụp thêm X-quang phổi do sốt cao kèm ho", order5.AdditionalOrderReason);
        Assert.Equal("PENDING_VERIFY", order5.ResultStatus);
        Assert.True(order5.HasResult);
        Assert.False(order5.IsResultFinalized);
        Assert.True(order5.CanEnterResult);
    }

    [Fact]
    public async Task GetWorklistAsync_FilterByLaboratoryAndOrdered_ReturnsOnlyEligibleHospital1LaboratoryOrders()
    {
        var result = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(Category: "LABORATORY", Status: "ORDERED"));

        var item = Assert.Single(result.Items);
        Assert.Equal("DIA-202610-0001", item.OrderCode);
        Assert.Equal("LABORATORY", item.Category);
        Assert.Equal("ORDERED", item.Status);
        Assert.True(item.CanConfirmOrder);
        Assert.True(item.UnitPrice > 0);
        Assert.Equal("C001", item.TicketNo);
    }

    [Fact]
    public async Task GetWorklistAsync_AllOperationalFiltersAndPagination_FilterDeterministically()
    {
        // Filter by QueueStatus
        var readyResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(QueueStatus: "READY"));
        Assert.Equal("DIA-202610-0004", Assert.Single(readyResult.Items).OrderCode);

        // Filter by ResultStatus
        var pendingVerifyResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(ResultStatus: "PENDING_VERIFY"));
        Assert.Equal("DIA-202610-0005", Assert.Single(pendingVerifyResult.Items).OrderCode);

        // Filter by Priority
        var statResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(Priority: "STAT"));
        Assert.Equal("DIA-202610-0003", Assert.Single(statResult.Items).OrderCode);

        // Filter by OrderDate / QueueDate
        var oct14Result = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(OrderDate: "2026-10-14"));
        Assert.Equal(2, oct14Result.Items.Count);
        Assert.Equal("2026-10-14", oct14Result.EffectiveDate);

        // Filter by ServicePointId (formatted and numeric)
        var spResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(ServicePointId: "sp-004"));
        Assert.Equal(2, spResult.Items.Count);
        Assert.All(spResult.Items, x => Assert.Equal(4L, x.ServicePointId));

        // Filter by RoomId
        var roomResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(RoomId: "DIA-203"));
        Assert.Equal(2, roomResult.Items.Count);
        Assert.All(roomResult.Items, x => Assert.Equal("DIA-203", x.RoomCode));

        // Filter by ServiceId
        var serviceResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(ServiceId: "SRV-ECG-01"));
        Assert.Equal(2, serviceResult.Items.Count);

        // Filter by VisitId
        var visitResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(VisitId: "VIS-202610-0002"));
        Assert.Equal(2, visitResult.Items.Count);

        // Filter by PatientId
        var patientResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(PatientId: "PAT-0001"));
        Assert.Equal(2, patientResult.Items.Count);

        // Filter by DoctorId
        var doctorResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(DoctorId: "DOC-001"));
        Assert.Equal(6, doctorResult.Items.Count);

        // Filter by RequiredForConclusion
        var optionalResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(RequiredForConclusion: false));
        Assert.Equal(2, optionalResult.Items.Count);
        Assert.All(optionalResult.Items, x => Assert.False(x.RequiredForConclusion));

        // Filter by Keyword
        var keywordResult = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(Keyword: "thâm nhiễm phổi"));
        Assert.Equal("DIA-202610-0005", Assert.Single(keywordResult.Items).OrderCode);

        // Pagination
        var page2Result = await _service.GetWorklistAsync(
            7,
            new GetDiagnosticWorklistRequest(Page: 2, PageSize: 2));
        Assert.Equal(6, page2Result.TotalCount);
        Assert.Equal(2, page2Result.Page);
        Assert.Equal(2, page2Result.PageSize);
        Assert.Equal(3, page2Result.TotalPages);
        Assert.Equal(new[] { "DIA-202610-0004", "DIA-202610-0005" }, page2Result.Items.Select(x => x.OrderCode).ToArray());
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("do-0001", 1L)]
    [InlineData("DIA-202610-0001", 1L)]
    [InlineData("q-011", 1L)]
    [InlineData("C001", 1L)]
    [InlineData("DIA-202610-0006", 6L)]
    public async Task GetWorklistOrderDetailAsync_SupportedIdentifiers_ReturnsMatchingOrderDetail(
        string identifier,
        long expectedOrderId)
    {
        var detail = await _service.GetWorklistOrderDetailAsync(7, identifier);

        Assert.Equal(expectedOrderId, detail.OrderId);
    }

    [Fact]
    public async Task GetWorklistOrderDetailAsync_CrossHospitalOrder_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetWorklistOrderDetailAsync(7, "DIA-202610-0002"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetWorklistOrderDetailAsync(7, "2"));
    }

    [Fact]
    public async Task GetWorklistOrderDetailAsync_UnknownOrder_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetWorklistOrderDetailAsync(7, "DIA-202610-9999"));
    }

    [Fact]
    public async Task AuthorizationGuards_InvalidActorOrInactiveOrWrongRole_ThrowsExpectedExceptions()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetWorklistAsync(0, new GetDiagnosticWorklistRequest()));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetWorklistAsync(999, new GetDiagnosticWorklistRequest()));

        // Locked account (14), inactive user (15), inactive staff (16), no staff (17),
        // no hospital (18), inactive hospital (19), inactive department (20),
        // wrong roles: DOCTOR (21), HOSPITAL_ADMIN (22), PATIENT (23), RECEPTION (24),
        // missing diagnostic permissions (25)
        var forbiddenUserIds = new long[] { 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 };
        foreach (var userId in forbiddenUserIds)
        {
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                _service.GetWorklistAsync(userId, new GetDiagnosticWorklistRequest()));
        }
    }

    [Fact]
    public async Task ValidationGuards_InvalidParameters_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(Limit: 0)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(Limit: 101)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(QueueLimit: 0)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(PageSize: 101)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(Page: 0)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(Category: "RADIOLOGY_INVALID")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(Status: "UNKNOWN_STATUS")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(QueueStatus: "BAD_QUEUE")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(ResultStatus: "BAD_RESULT")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(Priority: "SUPER_URGENT")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(OrderDate: "15-10-2026")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(OrderDate: "2026-10-15", QueueDate: "2026-10-16")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(ServicePointId: "-5")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(Keyword: "<script>alert(1)</script>")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistAsync(7, new GetDiagnosticWorklistRequest(Keyword: new string('a', 101))));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistOrderDetailAsync(7, "   "));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistOrderDetailAsync(7, "<bad>"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetWorklistOrderDetailAsync(7, "0"));
    }
}
