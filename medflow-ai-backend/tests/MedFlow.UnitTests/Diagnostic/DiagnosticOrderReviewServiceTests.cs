using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Diagnostic;

public sealed class DiagnosticOrderReviewServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DiagnosticOrderReviewTestDbContext : DbContext, IApplicationDbContext
    {
        public DiagnosticOrderReviewTestDbContext(DbContextOptions<DiagnosticOrderReviewTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DiagnosticOrderReviewService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => Set<DoctorServiceAuthorization>();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
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
        public DbSet<VisitDiagnosis> VisitDiagnoses => Set<VisitDiagnosis>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
        public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => Set<DiagnosticResultVersion>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<ExaminationSlot>();
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
            modelBuilder.Entity<DoctorServiceAuthorization>().HasKey(a => new { a.DoctorId, a.ServiceId });
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<ServicePrice>().HasKey(sp => sp.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<MedicalRecord>().HasKey(m => m.Id);
            modelBuilder.Entity<MedicalRecordRevision>().HasKey(r => r.Id);
            modelBuilder.Entity<VisitDiagnosis>().HasKey(d => d.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(o => o.Id);
            modelBuilder.Entity<DiagnosticResult>().HasKey(r => r.Id);
            modelBuilder.Entity<DiagnosticResultVersion>().HasKey(v => v.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc); // 09:00 Vietnam time

    private readonly DiagnosticOrderReviewTestDbContext _dbContext;
    private readonly DiagnosticOrderReviewService _service;

    public DiagnosticOrderReviewServiceTests()
    {
        var options = new DbContextOptionsBuilder<DiagnosticOrderReviewTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DiagnosticOrderReviewTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new DiagnosticOrderReviewTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new DiagnosticOrderReviewService(_dbContext, timeProvider);
    }

    private static void SeedData(DiagnosticOrderReviewTestDbContext db)
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
            new Specialty { Id = 1, SpecialtyCode = "INT", Name = "Nội tổng quát", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true },
            new Specialty { Id = 4, SpecialtyCode = "IMG", Name = "Chẩn đoán hình ảnh", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 101,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-201",
                Name = "Phòng Xét nghiệm Huyết học 201",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu A",
                Capacity = 3,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 102,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-202",
                Name = "Phòng X-quang Kỹ thuật số 202",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu B",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 103,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-203",
                Name = "Phòng CLS Bảo trì 203",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu C",
                Capacity = 1,
                Status = "MAINTENANCE",
            },
            new HospitalRoom
            {
                Id = 104,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "EXM-101",
                Name = "Phòng Khám Nội 101",
                RoomType = "CONSULTATION",
                Location = "Tầng 1",
                Capacity = 1,
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

        db.QueueServicePoints.AddRange(
            new QueueServicePoint
            {
                Id = 501,
                HospitalId = 1,
                RoomId = 101,
                ServiceId = 5,
                PointType = "DIAGNOSTIC",
                Name = "Điểm lấy mẫu Xét nghiệm 201",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 502,
                HospitalId = 1,
                RoomId = 102,
                ServiceId = 6,
                PointType = "DIAGNOSTIC",
                Name = "Điểm chụp X-quang 202",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 503,
                HospitalId = 1,
                RoomId = 103,
                ServiceId = 7,
                PointType = "DIAGNOSTIC",
                Name = "Điểm CLS Bảo trì 203",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 504,
                HospitalId = 1,
                RoomId = 101,
                ServiceId = 8,
                PointType = "DIAGNOSTIC",
                Name = "Điểm CLS Tạm ngưng 204",
                IsActive = false,
            },
            new QueueServicePoint
            {
                Id = 505,
                HospitalId = 1,
                RoomId = 104,
                ServiceId = 1,
                PointType = "CONSULTATION",
                Name = "Buồng khám Nội 101",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 601,
                HospitalId = 2,
                RoomId = 201,
                ServiceId = 20,
                PointType = "DIAGNOSTIC",
                Name = "Điểm CLS Cơ sở 2",
                IsActive = true,
            });

        db.Services.AddRange(
            new Service
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "KHAM-NOI",
                Name = "Khám Nội tổng quát",
                ServiceType = "CONSULTATION",
                RequiresResultVerification = false,
                IsActive = true,
            },
            new Service
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "LAB-CBC-01",
                Name = "Tổng phân tích tế bào máu ngoại vi",
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
                ServiceCode = "XRAY-CHEST-01",
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
                ServiceCode = "ECG-12-01",
                Name = "Điện tâm đồ 12 chuyển đạo",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 8,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "LAB-BIO-02",
                Name = "Sinh hóa máu tổng quát",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 9,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "LAB-OFF-09",
                Name = "Xét nghiệm ngừng triển khai",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = false,
            },
            new Service
            {
                Id = 20,
                HospitalId = 2,
                DepartmentId = 3,
                SpecialtyId = 4,
                ServiceCode = "XRAY-HCM-01",
                Name = "Chụp X-quang Cơ sở 2",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            });

        db.ServicePrices.AddRange(
            new ServicePrice
            {
                Id = 1,
                ServiceId = 5,
                PriceAmount = 120_000m,
                EffectiveFrom = new DateTime(2026, 1, 1),
            },
            new ServicePrice
            {
                Id = 2,
                ServiceId = 6,
                PriceAmount = 180_000m,
                EffectiveFrom = new DateTime(2026, 1, 1),
            },
            new ServicePrice
            {
                Id = 3,
                ServiceId = 7,
                PriceAmount = 150_000m,
                EffectiveFrom = new DateTime(2026, 1, 1),
            });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Lễ tân" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Nhân viên Cận lâm sàng" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 8, Code = "DIAGNOSTIC_NO_PERM", Name = "CLS không có quyền confirm" });

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
            new User { Id = 24, Email = "noperm_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE });

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
            new UserRole { UserId = 24, RoleId = 8 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 7,
                UserId = 7,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-007",
                FullName = "KTV. Lê Thị Minh Anh",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 8,
                UserId = 8,
                HospitalId = 2,
                DepartmentId = 3,
                StaffCode = "STF-008",
                FullName = "KTV. Trần Quốc Bảo",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 16,
                UserId = 16,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-016",
                FullName = "KTV. Ngừng Hoạt Động",
                Status = "INACTIVE",
            },
            new Staff
            {
                Id = 18,
                UserId = 18,
                HospitalId = null,
                DepartmentId = null,
                StaffCode = "STF-018",
                FullName = "KTV. Chưa Gán Cơ Sở",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 19,
                UserId = 19,
                HospitalId = 3,
                DepartmentId = null,
                StaffCode = "STF-019",
                FullName = "KTV. Cơ Sở Ngừng Hoạt Động",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 20,
                UserId = 20,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-020",
                FullName = "KTV. Khoa Ngừng Hoạt Động",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 24,
                UserId = 24,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-024",
                FullName = "KTV. Thiếu Quyền",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 30,
                UserId = 21,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-DOC-01",
                FullName = "Nguyễn Hoàng Nam",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 31,
                UserId = null,
                HospitalId = 2,
                DepartmentId = 3,
                StaffCode = "STF-DOC-02",
                FullName = "Phạm Văn Hùng",
                Status = "ACTIVE",
            });

        db.Doctors.AddRange(
            new Doctor
            {
                Id = 1,
                StaffId = 30,
                HospitalId = 1,
                SpecialtyId = 1,
                DoctorCode = "DOC-001",
                ProfessionalTitle = "BS.CKI",
                IsActive = true,
            },
            new Doctor
            {
                Id = 2,
                StaffId = 31,
                HospitalId = 2,
                SpecialtyId = 4,
                DoctorCode = "DOC-002",
                ProfessionalTitle = "ThS.BS",
                IsActive = true,
            });

        db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 5, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 6, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 7, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 8, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 2, ServiceId = 20, IsActive = true });

        db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                UserId = 23,
                PatientCode = "PAT-0001",
                FullName = "Nguyễn Văn An",
                DateOfBirth = new DateOnly(1985, 5, 10),
                Gender = "MALE",
                Phone = "0912345678",
                BloodType = "O+",
                IdentificationHash = "secret-hash-never-exposed",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 2,
                PatientCode = "PAT-0002",
                FullName = "Trần Thị Bích",
                DateOfBirth = new DateOnly(1992, 8, 20),
                Gender = "FEMALE",
                Phone = "0987654321",
                BloodType = "A+",
                Status = "ACTIVE",
            });

        db.Visits.AddRange(
            new Visit
            {
                Id = 10,
                VisitCode = "VIS-202610-0010",
                PatientId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 104,
                ReasonForVisit = "Đau ngực, khó thở khi gắng sức",
                Status = "AWAITING_RESULTS",
                CheckedInAt = new DateTime(2026, 10, 20, 8, 0, 0),
            },
            new Visit
            {
                Id = 11,
                VisitCode = "VIS-202610-0011",
                PatientId = 2,
                AssignedDoctorId = 1,
                ExaminationRoomId = 104,
                ReasonForVisit = "Sốt cao ngày thứ 3, mệt mỏi",
                Status = "IN_PROGRESS",
                CheckedInAt = new DateTime(2026, 10, 20, 8, 15, 0),
            },
            new Visit
            {
                Id = 12,
                VisitCode = "VIS-202610-0012",
                PatientId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 104,
                ReasonForVisit = "Khám định kỳ đã hoàn tất",
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 10, 15, 8, 0, 0),
                CompletedAt = new DateTime(2026, 10, 15, 11, 0, 0),
            },
            new Visit
            {
                Id = 20,
                VisitCode = "VIS-202610-0020",
                PatientId = 2,
                AssignedDoctorId = 2,
                ExaminationRoomId = 201,
                ReasonForVisit = "Khám tại cơ sở 2",
                Status = "AWAITING_RESULTS",
                CheckedInAt = new DateTime(2026, 10, 20, 8, 30, 0),
            });

        db.MedicalRecords.Add(new MedicalRecord
        {
            Id = 100,
            VisitId = 10,
            Status = "DRAFT",
        });

        db.MedicalRecordRevisions.Add(new MedicalRecordRevision
        {
            Id = 1001,
            MedicalRecordId = 100,
            RevisionNo = 1,
            PresentingComplaint = "Đau tức ngực trái lan lên vai",
            RelevantHistoryAllergies = "Dị ứng Penicillin",
            ClinicalFindings = "Tim đều 92 lần/phút, phổi thông khí rõ",
            ClinicalConclusion = "Theo dõi thiếu máu cơ tim cục bộ",
            RecordedAt = new DateTime(2026, 10, 20, 8, 20, 0),
        });

        db.VisitDiagnoses.Add(new VisitDiagnosis
        {
            Id = 500,
            VisitId = 10,
            CodeSnapshot = "I20.9",
            NameSnapshot = "Cơn đau thắt ngực, không đặc hiệu",
            IsPrimary = true,
            ClinicalDiagnosis = "Theo dõi cơn đau thắt ngực ổn định",
            Status = "PRELIMINARY",
            RecordedAt = new DateTime(2026, 10, 20, 8, 25, 0),
        });

        db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 11,
                VisitId = 10,
                ServicePointId = 501,
                CurrentServicePointId = 501,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 1,
                QueueType = "DIAGNOSTIC",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 30, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 30, 0),
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 1,
            },
            new QueueEntry
            {
                Id = 12,
                VisitId = 10,
                ServicePointId = 502,
                CurrentServicePointId = 502,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 2,
                QueueType = "DIAGNOSTIC",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 32, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 32, 0),
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 1,
            },
            new QueueEntry
            {
                Id = 13,
                VisitId = 11,
                ServicePointId = 501,
                CurrentServicePointId = 501,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 3,
                QueueType = "DIAGNOSTIC",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 40, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 40, 0),
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 2,
            },
            new QueueEntry
            {
                Id = 14,
                VisitId = 12,
                ServicePointId = 501,
                CurrentServicePointId = 501,
                QueueDate = new DateOnly(2026, 10, 15),
                QueueNumber = 1,
                QueueType = "DIAGNOSTIC",
                Status = "COMPLETED",
                PresenceStatus = "PRESENT",
                CompletedAt = new DateTime(2026, 10, 15, 10, 0, 0),
                StateVersion = 3,
            },
            new QueueEntry
            {
                Id = 20,
                VisitId = 20,
                ServicePointId = 601,
                CurrentServicePointId = 601,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 1,
                QueueType = "DIAGNOSTIC",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                StateVersion = 1,
            });

        db.DiagnosticOrders.AddRange(
            // Order 11: ORDERED, eligible for confirmation (LABORATORY, URGENT)
            new DiagnosticOrder
            {
                Id = 11,
                OrderCode = "DIA-202610-0011",
                VisitId = 10,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = 11,
                Instruction = "Lấy mẫu máu tĩnh mạch lúc đói",
                RequiredForConclusion = true,
                Priority = "URGENT",
                Status = "ORDERED",
                OrderedAt = new DateTime(2026, 10, 20, 8, 30, 0),
            },
            // Order 12: ORDERED, eligible for confirmation (IMAGING, NORMAL)
            new DiagnosticOrder
            {
                Id = 12,
                OrderCode = "DIA-202610-0012",
                VisitId = 10,
                OrderingDoctorId = 1,
                ServiceId = 6,
                QueueEntryId = 12,
                Instruction = "Chụp phổi tư thế đứng thẳng",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "ORDERED",
                OrderedAt = new DateTime(2026, 10, 20, 8, 32, 0),
            },
            // Order 13: Already CONFIRMED
            new DiagnosticOrder
            {
                Id = 13,
                OrderCode = "DIA-202610-0013",
                VisitId = 11,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = 13,
                Instruction = "Kiểm tra bạch cầu",
                RequiredForConclusion = true,
                Priority = "STAT",
                Status = "CONFIRMED",
                ConfirmedByStaffId = 7,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 45, 0),
                OrderedAt = new DateTime(2026, 10, 20, 8, 40, 0),
            },
            // Order 14: Already COMPLETED
            new DiagnosticOrder
            {
                Id = 14,
                OrderCode = "DIA-202610-0014",
                VisitId = 12,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = 14,
                Instruction = "Xét nghiệm định kỳ",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "COMPLETED",
                ConfirmedByStaffId = 7,
                ConfirmedAt = new DateTime(2026, 10, 15, 9, 0, 0),
                OrderedAt = new DateTime(2026, 10, 15, 8, 45, 0),
            },
            // Order 15: CANCELLED
            new DiagnosticOrder
            {
                Id = 15,
                OrderCode = "DIA-202610-0015",
                VisitId = 11,
                OrderingDoctorId = 1,
                ServiceId = 6,
                QueueEntryId = null,
                Instruction = "Đã hủy theo yêu cầu",
                RequiredForConclusion = false,
                Priority = "NORMAL",
                Status = "CANCELLED",
                OrderedAt = new DateTime(2026, 10, 20, 8, 42, 0),
            },
            // Order 16: ORDERED without pre-linked QueueEntry (tests auto-queue allocation)
            new DiagnosticOrder
            {
                Id = 16,
                OrderCode = null,
                VisitId = 11,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = null,
                Instruction = "Xét nghiệm bổ sung chưa tạo hàng đợi",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "ORDERED",
                OrderedAt = new DateTime(2026, 10, 20, 8, 50, 0),
            },
            // Order 20: Hospital 2 order (cross-hospital isolation)
            new DiagnosticOrder
            {
                Id = 20,
                OrderCode = "DIA-202610-0020",
                VisitId = 20,
                OrderingDoctorId = 2,
                ServiceId = 20,
                QueueEntryId = 20,
                Instruction = "Chụp X-quang cơ sở 2",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "ORDERED",
                OrderedAt = new DateTime(2026, 10, 20, 8, 35, 0),
            });

        db.DiagnosticResults.Add(new DiagnosticResult
        {
            Id = 1,
            DiagnosticOrderId = 14,
            Status = "FINALIZED",
            FinalizedAt = new DateTime(2026, 10, 15, 10, 0, 0),
            FinalizedByStaffId = 7,
        });

        db.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetDiagnosticOrdersAsync_ReturnsHospitalScopedOrdersAndSummaryStats()
    {
        var response = await _service.GetDiagnosticOrdersAsync(7, new GetDiagnosticOrdersForReviewRequest());

        Assert.Equal(1, response.HospitalId);
        Assert.Equal("HOS-DEMO-01", response.HospitalCode);
        Assert.Equal(7, response.StaffProfile.UserId);
        Assert.Equal("STF-007", response.StaffProfile.StaffCode);

        // Hospital 1 has orders 11, 12, 13, 14, 15, 16 (Order 20 belongs to Hospital 2 and must be excluded)
        Assert.Equal(6, response.Summary.TotalOrders);
        Assert.Equal(3, response.Summary.PendingConfirmationCount); // 11, 12, 16
        Assert.Equal(1, response.Summary.ConfirmedCount); // 13
        Assert.Equal(1, response.Summary.CompletedCount); // 14
        Assert.Equal(1, response.Summary.CancelledCount); // 15
        Assert.Equal(1, response.Summary.UrgentOrStatPendingCount); // 11 is URGENT and ORDERED
        Assert.DoesNotContain(response.Items, i => i.OrderId == 20);
        Assert.NotEmpty(response.AvailableServicePoints);

        // PendingOnly + Category filter
        var pendingLab = await _service.GetDiagnosticOrdersAsync(
            7,
            new GetDiagnosticOrdersForReviewRequest(Category: "LABORATORY", PendingOnly: true));
        Assert.Equal(2, pendingLab.Items.Count);
        Assert.All(pendingLab.Items, i =>
        {
            Assert.Equal("ORDERED", i.Status);
            Assert.Equal("LABORATORY", i.Category);
            Assert.True(i.CanConfirm);
        });

        // Keyword filter
        var keywordFiltered = await _service.GetDiagnosticOrdersAsync(
            7,
            new GetDiagnosticOrdersForReviewRequest(Keyword: "X-quang"));
        Assert.Equal(2, keywordFiltered.Items.Count);
        Assert.All(keywordFiltered.Items, i => Assert.Equal("IMAGING", i.Category));
    }

    [Fact]
    public async Task GetDiagnosticOrderByIdOrCodeAsync_ResolvesMultipleFormatsAndReturnsClinicalContext()
    {
        var byCode = await _service.GetDiagnosticOrderByIdOrCodeAsync(7, "DIA-202610-0011");
        var byNumericId = await _service.GetDiagnosticOrderByIdOrCodeAsync(7, "11");
        var byDoPrefix = await _service.GetDiagnosticOrderByIdOrCodeAsync(7, "do-0011");
        var byQueuePrefix = await _service.GetDiagnosticOrderByIdOrCodeAsync(7, "q-011");
        var byTicketCode = await _service.GetDiagnosticOrderByIdOrCodeAsync(7, "C001");

        Assert.Equal(11, byCode.Order.OrderId);
        Assert.Equal(11, byNumericId.Order.OrderId);
        Assert.Equal(11, byDoPrefix.Order.OrderId);
        Assert.Equal(11, byQueuePrefix.Order.OrderId);
        Assert.Equal(11, byTicketCode.Order.OrderId);

        Assert.True(byCode.Order.CanConfirm);
        Assert.Null(byCode.Order.DisabledReason);
        Assert.True(byCode.Order.Eligibility.IsEligible);
        Assert.Equal("Nguyễn Văn An", byCode.Order.Patient.FullName);
        Assert.Equal("Dị ứng Penicillin", byCode.Order.Patient.Allergies);
        Assert.Equal("I20.9", byCode.ClinicalContext.PrimaryDiagnosisCode);
        Assert.Equal("Cơn đau thắt ngực, không đặc hiệu", byCode.ClinicalContext.PrimaryDiagnosisName);
        Assert.Equal("Tim đều 92 lần/phút, phổi thông khí rõ", byCode.ClinicalContext.ClinicalFindings);

        // Already confirmed order #13 returns CanConfirm = false with DisabledReason
        var confirmedDetail = await _service.GetDiagnosticOrderByIdOrCodeAsync(7, "DIA-202610-0013");
        Assert.False(confirmedDetail.Order.CanConfirm);
        Assert.Contains("đã được xác nhận trước đó", confirmedDetail.Order.DisabledReason);
    }

    [Fact]
    public async Task ConfirmDiagnosticOrderAsync_HappyPath_TransitionsToConfirmedUpdatesQueueAndWritesAuditLog()
    {
        var response = await _service.ConfirmDiagnosticOrderAsync(
            actorUserId: 7,
            orderIdOrCode: "DIA-202610-0011",
            request: new ConfirmDiagnosticOrderRequest(
                IsEligibilityVerified: true,
                Priority: "STAT",
                Instruction: "Lấy mẫu máu khẩn cấp và chuyển ngay phòng xét nghiệm 201",
                ConfirmationNote: "Đã kiểm tra chỉ định lâm sàng và đối chiếu thông tin bệnh nhân",
                PatientPresent: true,
                ExpectedStateVersion: 1),
            ipAddress: "10.0.0.25");

        Assert.Equal(DiagnosticOrderReviewService.DiagnosticOrderConfirmedAction, response.ActionOutcome);
        Assert.Equal(1, response.ConfirmedCount);
        Assert.Equal(11, response.Order.OrderId);
        Assert.Equal("CONFIRMED", response.Order.Status);
        Assert.Equal("STAT", response.Order.Priority);
        Assert.Equal("Lấy mẫu máu khẩn cấp và chuyển ngay phòng xét nghiệm 201", response.Order.Instruction);
        Assert.Equal(7, response.Order.ConfirmedByStaffId);
        Assert.Equal("STF-007", response.Order.ConfirmedByStaffCode);
        Assert.Equal("KTV. Lê Thị Minh Anh", response.Order.ConfirmedByStaffName);
        Assert.Equal("Đã kiểm tra chỉ định lâm sàng và đối chiếu thông tin bệnh nhân", response.Order.ConfirmationNote);
        Assert.NotNull(response.Order.ConfirmedAt);
        Assert.Equal(2, response.Order.StateVersion);
        Assert.False(response.Order.CanConfirm);

        var updatedOrder = await _dbContext.DiagnosticOrders.FirstAsync(o => o.Id == 11);
        Assert.Equal("CONFIRMED", updatedOrder.Status);
        Assert.Equal(7, updatedOrder.ConfirmedByStaffId);
        Assert.NotNull(updatedOrder.ConfirmedAt);

        var updatedQueue = await _dbContext.QueueEntries.FirstAsync(q => q.Id == 11);
        Assert.Equal(2, updatedQueue.StateVersion);
        Assert.Equal("WAITING", updatedQueue.Status);
        Assert.Equal("PRESENT", updatedQueue.PresenceStatus);

        var auditLog = await _dbContext.AuditLogs
            .FirstOrDefaultAsync(a =>
                a.Action == DiagnosticOrderReviewService.DiagnosticOrderConfirmedAction &&
                a.EntityId == "11");
        Assert.NotNull(auditLog);
        Assert.Equal(7, auditLog.ActorUserId);
        Assert.Equal(1, auditLog.HospitalId);
        Assert.Equal("10.0.0.25", auditLog.IpAddress?.ToString());
        Assert.Contains("DIA-202610-0011", auditLog.ChangeData);
        Assert.Contains("CONFIRMED", auditLog.ChangeData);
    }

    [Fact]
    public async Task ConfirmDiagnosticOrderAsync_OrderWithoutQueueEntry_AllocatesNewDiagnosticQueueEntry()
    {
        var response = await _service.ConfirmDiagnosticOrderAsync(
            actorUserId: 7,
            orderIdOrCode: "16",
            request: new ConfirmDiagnosticOrderRequest(
                ConfirmationNote: "Xác nhận và cấp số hàng đợi CLS mới"));

        Assert.Equal("CONFIRMED", response.Order.Status);
        Assert.Equal("DIA-202610-0016", response.Order.OrderCode);
        Assert.NotNull(response.Order.QueueEntryId);
        Assert.Equal("WAITING", response.Order.QueueStatus);
        Assert.StartsWith("C", response.Order.TicketNo);
        Assert.Equal(501, response.Order.ServicePointId);
        Assert.Equal("DIA-201", response.Order.RoomCode);
    }

    [Fact]
    public async Task ConfirmDiagnosticOrdersBatchAsync_ConfirmsMultipleOrdersInOneTransaction()
    {
        var batchResponse = await _service.ConfirmDiagnosticOrdersBatchAsync(
            actorUserId: 7,
            request: new ConfirmDiagnosticOrdersBatchRequest(
                OrderCodes: ["DIA-202610-0011", "DIA-202610-0012"],
                ConfirmationNote: "Xác nhận hàng loạt 2 chỉ định của lượt khám VIS-202610-0010"),
            ipAddress: "192.168.1.50");

        Assert.Equal(2, batchResponse.ConfirmedCount);
        Assert.Equal(2, batchResponse.ConfirmedOrders.Count);
        Assert.All(batchResponse.ConfirmedOrders, o =>
        {
            Assert.Equal("CONFIRMED", o.Status);
            Assert.Equal(7, o.ConfirmedByStaffId);
            Assert.False(o.CanConfirm);
        });

        var auditCount = await _dbContext.AuditLogs.CountAsync(
            a => a.Action == DiagnosticOrderReviewService.DiagnosticOrderConfirmedAction);
        Assert.Equal(2, auditCount);
    }

    [Fact]
    public async Task ConfirmDiagnosticOrderAsync_AlreadyConfirmedCompletedOrCancelled_ThrowsConflictException()
    {
        // Order 13 is CONFIRMED
        var exConfirmed = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDiagnosticOrderAsync(7, "DIA-202610-0013"));
        Assert.Contains("đã được xác nhận trước đó", exConfirmed.Message);

        // Order 14 is COMPLETED
        var exCompleted = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDiagnosticOrderAsync(7, "DIA-202610-0014"));
        Assert.Contains("COMPLETED", exCompleted.Message);

        // Order 15 is CANCELLED
        var exCancelled = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDiagnosticOrderAsync(7, "DIA-202610-0015"));
        Assert.Contains("đã bị hủy", exCancelled.Message);
    }

    [Fact]
    public async Task ConfirmDiagnosticOrderAsync_StateVersionMismatchAndIneligibleDependencies_ThrowsConflictException()
    {
        // StateVersion mismatch
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDiagnosticOrderAsync(
                7,
                "DIA-202610-0011",
                new ConfirmDiagnosticOrderRequest(ExpectedStateVersion: 99)));

        // Service point in maintenance room (503 -> room 103 MAINTENANCE)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDiagnosticOrderAsync(
                7,
                "DIA-202610-0011",
                new ConfirmDiagnosticOrderRequest(RoomId: 103)));

        // Inactive service point (504)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDiagnosticOrderAsync(
                7,
                "DIA-202610-0011",
                new ConfirmDiagnosticOrderRequest(ServicePointId: 504)));

        // Incompatible service point (502 is for Service 6, Order 11 is for Service 5)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDiagnosticOrderAsync(
                7,
                "DIA-202610-0011",
                new ConfirmDiagnosticOrderRequest(ServicePointId: 502)));

        // Unauthorized doctor for service
        var authRow = await _dbContext.DoctorServiceAuthorizations
            .FirstAsync(a => a.DoctorId == 1 && a.ServiceId == 5);
        authRow.IsActive = false;
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDiagnosticOrderAsync(7, "DIA-202610-0011"));
    }

    [Fact]
    public async Task ValidationFailures_ThrowValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticOrdersAsync(7, new GetDiagnosticOrdersForReviewRequest(Limit: 0)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticOrdersAsync(7, new GetDiagnosticOrdersForReviewRequest(Category: "INVALID")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticOrdersAsync(7, new GetDiagnosticOrdersForReviewRequest(Status: "INVALID")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticOrdersAsync(7, new GetDiagnosticOrdersForReviewRequest(Priority: "INVALID")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticOrderByIdOrCodeAsync(7, "   "));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticOrderByIdOrCodeAsync(7, "-5"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ConfirmDiagnosticOrderAsync(
                7,
                "DIA-202610-0011",
                new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: false)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ConfirmDiagnosticOrderAsync(
                7,
                "DIA-202610-0011",
                new ConfirmDiagnosticOrderRequest(ExpectedStateVersion: 0)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ConfirmDiagnosticOrderAsync(
                7,
                "DIA-202610-0011",
                new ConfirmDiagnosticOrderRequest(ConfirmationNote: "<script>alert(1)</script>")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ConfirmDiagnosticOrderAsync(
                7,
                "DIA-202610-0011",
                new ConfirmDiagnosticOrderRequest(RoomId: 104))); // CONSULTATION room, not DIAGNOSTIC

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ConfirmDiagnosticOrdersBatchAsync(
                7,
                new ConfirmDiagnosticOrdersBatchRequest()));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ConfirmDiagnosticOrdersBatchAsync(
                7,
                new ConfirmDiagnosticOrdersBatchRequest(OrderIds: [11, 11])));
    }

    [Fact]
    public async Task AuthorizationAndCrossHospitalIsolation_EnforcedAtLayer2()
    {
        // Invalid userId <= 0 -> 401
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetDiagnosticOrdersAsync(0));

        // Non-existent user -> 404
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDiagnosticOrdersAsync(9999));

        // Non-diagnostic roles -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticOrdersAsync(21)); // DOCTOR
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticOrdersAsync(22)); // HOSPITAL_ADMIN
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticOrdersAsync(23)); // PATIENT

        // Locked / inactive user / inactive staff / missing staff / missing hospital / inactive hospital / inactive dept / missing permission -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetDiagnosticOrdersAsync(14));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetDiagnosticOrdersAsync(15));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetDiagnosticOrdersAsync(16));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetDiagnosticOrdersAsync(17));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetDiagnosticOrdersAsync(18));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetDiagnosticOrdersAsync(19));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetDiagnosticOrdersAsync(20));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetDiagnosticOrdersAsync(24));

        // Cross-hospital access: Staff 7 (Hospital 1) accessing Order 20 (Hospital 2) -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticOrderByIdOrCodeAsync(7, "DIA-202610-0020"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.ConfirmDiagnosticOrderAsync(7, "DIA-202610-0020"));
    }
}
