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

public sealed class DiagnosticPatientReceiveServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DiagnosticPatientReceiveTestDbContext : DbContext, IApplicationDbContext
    {
        public DiagnosticPatientReceiveTestDbContext(DbContextOptions<DiagnosticPatientReceiveTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DiagnosticPatientReceiveService unit tests.");

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

    private readonly DiagnosticPatientReceiveTestDbContext _dbContext;
    private readonly DiagnosticPatientReceiveService _service;

    public DiagnosticPatientReceiveServiceTests()
    {
        var options = new DbContextOptionsBuilder<DiagnosticPatientReceiveTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DiagnosticPatientReceiveTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new DiagnosticPatientReceiveTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new DiagnosticPatientReceiveService(_dbContext, timeProvider);
    }

    private static void SeedData(DiagnosticPatientReceiveTestDbContext db)
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
                Id = 105,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-205",
                Name = "Phòng Điện tâm đồ 205",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu D",
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
                Id = 506,
                HospitalId = 1,
                RoomId = 105,
                ServiceId = 7,
                PointType = "DIAGNOSTIC",
                Name = "Điểm Điện tâm đồ 205",
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
            new Role { Id = 8, Code = "DIAGNOSTIC_NO_PERM", Name = "CLS không có quyền" });

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
            new User { Id = 24, Email = "noperm_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 25, Email = "reception01@medflow.test", AccountStatus = AccountStatus.ACTIVE });

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
            new UserRole { UserId = 24, RoleId = 8 },
            new UserRole { UserId = 25, RoleId = 3 });

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

        db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                UserId = 23,
                PatientCode = "PAT-0001",
                FullName = "Nguyễn Văn An",
                DateOfBirth = new DateOnly(1948, 5, 10), // 78 years old -> eligible for ELDERLY
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
                DateOfBirth = new DateOnly(1992, 8, 20), // 34 years old ->FEMALE, not elderly
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
            VisitId = 11,
            Status = "DRAFT",
        });

        db.MedicalRecordRevisions.Add(new MedicalRecordRevision
        {
            Id = 1001,
            MedicalRecordId = 100,
            RevisionNo = 1,
            PresentingComplaint = "Sốt cao liên tục 39 độ C",
            RelevantHistoryAllergies = "Dị ứng hải sản",
            ClinicalFindings = "Họng đỏ, hạch góc hàm",
            ClinicalConclusion = "Theo dõi nhiễm siêu vi cấp",
            RecordedAt = new DateTime(2026, 10, 20, 8, 20, 0),
        });

        db.VisitDiagnoses.Add(new VisitDiagnosis
        {
            Id = 500,
            VisitId = 11,
            CodeSnapshot = "B34.9",
            NameSnapshot = "Nhiễm virus, không xác định",
            IsPrimary = true,
            ClinicalDiagnosis = "Sốt siêu vi ngày thứ 3",
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
                PresenceStatus = "NOT_ARRIVED",
                ArrivedAt = null,
                ReadyAt = null,
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
                PresenceStatus = "NOT_ARRIVED",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 40, 0),
                ReadyAt = null,
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
                Id = 17,
                VisitId = 10,
                ServicePointId = 506,
                CurrentServicePointId = 506,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 4,
                QueueType = "DIAGNOSTIC",
                Status = "READY",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 50, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 52, 0),
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 2,
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
            // Order 11: ORDERED (not yet confirmed in UC39 -> cannot receive yet)
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
            // Order 12: CONFIRMED + Queue WAITING (eligible for reception, IMAGING, NORMAL)
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
                Status = "CONFIRMED",
                ConfirmedByStaffId = 7,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 35, 0),
                OrderedAt = new DateTime(2026, 10, 20, 8, 32, 0),
            },
            // Order 13: CONFIRMED + Queue WAITING (eligible for reception, LABORATORY, STAT)
            new DiagnosticOrder
            {
                Id = 13,
                OrderCode = "DIA-202610-0013",
                VisitId = 11,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = 13,
                Instruction = "Kiểm tra bạch cầu cấp",
                RequiredForConclusion = true,
                Priority = "STAT",
                Status = "CONFIRMED",
                ConfirmedByStaffId = 7,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 45, 0),
                OrderedAt = new DateTime(2026, 10, 20, 8, 40, 0),
            },
            // Order 14: COMPLETED
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
            // Order 16: CONFIRMED without pre-linked QueueEntry (eligible, allocates READY queue on receive)
            new DiagnosticOrder
            {
                Id = 16,
                OrderCode = "DIA-202610-0016",
                VisitId = 11,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = null,
                Instruction = "Xét nghiệm bổ sung đã xác nhận",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "CONFIRMED",
                ConfirmedByStaffId = 7,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 51, 0),
                OrderedAt = new DateTime(2026, 10, 20, 8, 50, 0),
            },
            // Order 17: CONFIRMED + Queue READY (already received into queue)
            new DiagnosticOrder
            {
                Id = 17,
                OrderCode = "DIA-202610-0017",
                VisitId = 10,
                OrderingDoctorId = 1,
                ServiceId = 7,
                QueueEntryId = 17,
                Instruction = "Đo điện tim 12 chuyển đạo",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "CONFIRMED",
                ConfirmedByStaffId = 7,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 49, 0),
                OrderedAt = new DateTime(2026, 10, 20, 8, 48, 0),
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
                Status = "CONFIRMED",
                ConfirmedByStaffId = 8,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 36, 0),
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
    public async Task GetOrdersForReceiveAsync_ReturnsHospitalScopedOrdersAndSummaryStats()
    {
        var response = await _service.GetOrdersForReceiveAsync(7, new GetDiagnosticOrdersForReceiveRequest());

        Assert.Equal(1, response.HospitalId);
        Assert.Equal("HOS-DEMO-01", response.HospitalCode);
        Assert.Equal(7, response.StaffProfile.UserId);
        Assert.Equal("STF-007", response.StaffProfile.StaffCode);

        // Hospital 1 has orders 11, 12, 13, 14, 15, 16, 17 (Order 20 belongs to Hospital 2 and must be excluded)
        Assert.Equal(7, response.Summary.TotalOrders);
        Assert.Equal(1, response.Summary.PendingConfirmationCount); // 11
        Assert.Equal(3, response.Summary.WaitingReceptionCount); // 12, 13, 16
        Assert.Equal(1, response.Summary.ReadyInQueueCount); // 17
        Assert.Equal(1, response.Summary.CompletedCount); // 14
        Assert.Equal(1, response.Summary.CancelledCount); // 15
        Assert.Equal(1, response.Summary.UrgentOrStatWaitingCount); // 13 is STAT and waiting reception
        Assert.DoesNotContain(response.Items, i => i.OrderId == 20);
        Assert.NotEmpty(response.AvailableServicePoints);

        // ReadyToReceiveOnly filter
        var readyToReceiveOnly = await _service.GetOrdersForReceiveAsync(
            7,
            new GetDiagnosticOrdersForReceiveRequest(ReadyToReceiveOnly: true));
        Assert.Equal(3, readyToReceiveOnly.Items.Count);
        Assert.All(readyToReceiveOnly.Items, i =>
        {
            Assert.Equal("CONFIRMED", i.Status);
            Assert.True(i.CanReceive);
            Assert.Null(i.DisabledReason);
        });

        // QueueStatus + Category filter
        var readyFunctional = await _service.GetOrdersForReceiveAsync(
            7,
            new GetDiagnosticOrdersForReceiveRequest(QueueStatus: "READY", Category: "FUNCTIONAL"));
        Assert.Single(readyFunctional.Items);
        Assert.Equal(17, readyFunctional.Items[0].OrderId);

        // Keyword filter
        var keywordFiltered = await _service.GetOrdersForReceiveAsync(
            7,
            new GetDiagnosticOrdersForReceiveRequest(Keyword: "C003"));
        Assert.Single(keywordFiltered.Items);
        Assert.Equal(13, keywordFiltered.Items[0].OrderId);
    }

    [Fact]
    public async Task GetOrderForReceiveByIdOrCodeAsync_ResolvesMultipleFormatsAndReturnsClinicalContext()
    {
        var byCode = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "DIA-202610-0013");
        var byNumericId = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "13");
        var byDoPrefix = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "do-0013");
        var byQueuePrefix = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "q-013");
        var byTicketCode = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "C003");
        var byVisitCode = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "VIS-202610-0011");

        Assert.Equal(13, byCode.Order.OrderId);
        Assert.Equal(13, byNumericId.Order.OrderId);
        Assert.Equal(13, byDoPrefix.Order.OrderId);
        Assert.Equal(13, byQueuePrefix.Order.OrderId);
        Assert.Equal(13, byTicketCode.Order.OrderId);
        Assert.Equal(13, byVisitCode.Order.OrderId); // STAT confirmed waiting order on visit 11 prioritized

        Assert.True(byCode.Order.CanReceive);
        Assert.Null(byCode.Order.DisabledReason);
        Assert.True(byCode.Order.Eligibility.IsEligible);
        Assert.Equal("Trần Thị Bích", byCode.Order.Patient.FullName);
        Assert.Equal("Dị ứng hải sản", byCode.Order.Patient.Allergies);
        Assert.Equal("B34.9", byCode.ClinicalContext.PrimaryDiagnosisCode);
        Assert.Equal("Nhiễm virus, không xác định", byCode.ClinicalContext.PrimaryDiagnosisName);
        Assert.Equal("Họng đỏ, hạch góc hàm", byCode.ClinicalContext.ClinicalFindings);

        // Order 11 is ORDERED -> CanReceive = false
        var orderedDetail = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "DIA-202610-0011");
        Assert.False(orderedDetail.Order.CanReceive);
        Assert.Contains("ORDERED", orderedDetail.Order.DisabledReason);

        // Order 17 is already READY -> CanReceive = false
        var readyDetail = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "DIA-202610-0017");
        Assert.False(readyDetail.Order.CanReceive);
        Assert.Contains("READY", readyDetail.Order.DisabledReason);

        // Order 14 is COMPLETED -> CanReceive = false
        var completedDetail = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "DIA-202610-0014");
        Assert.False(completedDetail.Order.CanReceive);
        Assert.Contains("COMPLETED", completedDetail.Order.DisabledReason);
    }

    [Fact]
    public async Task ReceiveDiagnosticPatientAsync_HappyPath_TransitionsQueueToReadyUpdatesPresenceAndWritesAuditLog()
    {
        var response = await _service.ReceiveDiagnosticPatientAsync(
            actorUserId: 7,
            orderIdOrCode: "DIA-202610-0013",
            request: new ReceiveDiagnosticPatientRequest(
                IsPatientPresent: true,
                IsIdentityVerified: true,
                IsPreparationVerified: true,
                ManualPriority: "PREGNANT",
                Instruction: "Bệnh nhân đã nhịn ăn, lấy mẫu máu tĩnh mạch ngay",
                ReceptionNote: "Bệnh nhân đã có mặt tại quầy tiếp nhận CLS",
                ExpectedStateVersion: 2),
            ipAddress: "10.0.0.28");

        Assert.Equal(DiagnosticPatientReceiveService.DiagnosticPatientReceivedAction, response.ActionOutcome);
        Assert.Equal(1, response.ReceivedCount);
        Assert.Equal(13, response.Order.OrderId);
        Assert.Equal("CONFIRMED", response.Order.Status);
        Assert.Equal("READY", response.Order.QueueStatus);
        Assert.Equal("PRESENT", response.Order.PresenceStatus);
        Assert.Equal("PREGNANT", response.Order.ManualPriority);
        Assert.Equal("PREGNANT", response.Order.EffectiveClinicalPriority);
        Assert.Equal(3, response.Order.StateVersion);
        Assert.NotNull(response.Order.ArrivedAt);
        Assert.NotNull(response.Order.ReadyAt);
        Assert.Equal(7, response.Order.ReceivedByStaffId);
        Assert.Equal("STF-007", response.Order.ReceivedByStaffCode);
        Assert.Equal("KTV. Lê Thị Minh Anh", response.Order.ReceivedByStaffName);
        Assert.Equal("Bệnh nhân đã có mặt tại quầy tiếp nhận CLS", response.Order.ReceptionNote);
        Assert.False(response.Order.CanReceive);

        var updatedQueue = await _dbContext.QueueEntries.FirstAsync(q => q.Id == 13);
        Assert.Equal("READY", updatedQueue.Status);
        Assert.Equal("PRESENT", updatedQueue.PresenceStatus);
        Assert.Equal("PREGNANT", updatedQueue.ManualPriority);
        Assert.Equal(3, updatedQueue.StateVersion);
        Assert.NotNull(updatedQueue.ArrivedAt);
        Assert.NotNull(updatedQueue.ReadyAt);

        var auditLog = await _dbContext.AuditLogs
            .FirstOrDefaultAsync(a =>
                a.Action == DiagnosticPatientReceiveService.DiagnosticPatientReceivedAction &&
                a.EntityId == "13");
        Assert.NotNull(auditLog);
        Assert.Equal(7, auditLog.ActorUserId);
        Assert.Equal(1, auditLog.HospitalId);
        Assert.Equal("10.0.0.28", auditLog.IpAddress?.ToString());
        Assert.Contains("DIA-202610-0013", auditLog.ChangeData);
        Assert.Contains("READY", auditLog.ChangeData);
        Assert.Equal("Bệnh nhân đã có mặt tại quầy tiếp nhận CLS", auditLog.Reason);
        Assert.Contains("PRESENT", auditLog.ChangeData);

        var reloadedDetail = await _service.GetOrderForReceiveByIdOrCodeAsync(7, "DIA-202610-0013");
        Assert.Equal("READY", reloadedDetail.Order.QueueStatus);
        Assert.Equal("Bệnh nhân đã có mặt tại quầy tiếp nhận CLS", reloadedDetail.Order.ReceptionNote);
        Assert.Equal(7, reloadedDetail.Order.ReceivedByStaffId);
        Assert.Equal("STF-007", reloadedDetail.Order.ReceivedByStaffCode);
        Assert.False(reloadedDetail.Order.CanReceive);
    }

    [Fact]
    public async Task ReceiveDiagnosticPatientAsync_ConfirmedOrderWithoutQueueEntry_AllocatesReadyDiagnosticQueueEntry()
    {
        var response = await _service.ReceiveDiagnosticPatientAsync(
            actorUserId: 7,
            orderIdOrCode: "16",
            request: new ReceiveDiagnosticPatientRequest(
                IsPatientPresent: true,
                IsIdentityVerified: true,
                ReceptionNote: "Tiếp nhận bệnh nhân và cấp số hàng đợi READY"));

        Assert.Equal("CONFIRMED", response.Order.Status);
        Assert.Equal("DIA-202610-0016", response.Order.OrderCode);
        Assert.NotNull(response.Order.QueueEntryId);
        Assert.Equal("READY", response.Order.QueueStatus);
        Assert.Equal("PRESENT", response.Order.PresenceStatus);
        Assert.Equal(1, response.Order.StateVersion);
        Assert.StartsWith("C", response.Order.TicketNo);
        Assert.Equal(501, response.Order.ServicePointId);
        Assert.Equal("DIA-201", response.Order.RoomCode);
        Assert.False(response.Order.CanReceive);
    }

    [Fact]
    public async Task ReceiveDiagnosticPatientsBatchAsync_ReceivesMultipleConfirmedOrdersInOneTransaction()
    {
        var batchByVisit = await _service.ReceiveDiagnosticPatientsBatchAsync(
            actorUserId: 7,
            request: new ReceiveDiagnosticPatientsBatchRequest(
                VisitCode: "VIS-202610-0011",
                IsPatientPresent: true,
                IsIdentityVerified: true,
                ReceptionNote: "Tiếp nhận hàng loạt các chỉ định đã xác nhận của lượt khám VIS-202610-0011"),
            ipAddress: "192.168.1.55");

        // Visit 11 has two CONFIRMED waiting orders: 13 and 16 (Order 15 is CANCELLED)
        Assert.Equal(2, batchByVisit.ReceivedCount);
        Assert.Equal(2, batchByVisit.ReceivedOrders.Count);
        Assert.All(batchByVisit.ReceivedOrders, o =>
        {
            Assert.Equal("CONFIRMED", o.Status);
            Assert.Equal("READY", o.QueueStatus);
            Assert.Equal("PRESENT", o.PresenceStatus);
            Assert.Equal(7, o.ReceivedByStaffId);
            Assert.False(o.CanReceive);
        });

        var auditCount = await _dbContext.AuditLogs.CountAsync(
            a => a.Action == DiagnosticPatientReceiveService.DiagnosticPatientReceivedAction);
        Assert.Equal(2, auditCount);
    }

    [Fact]
    public async Task ReceiveDiagnosticPatientAsync_OrderedAlreadyReadyInProgressCompletedOrCancelled_ThrowsConflictException()
    {
        // Order 11 is ORDERED (not confirmed in UC39)
        var exOrdered = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReceiveDiagnosticPatientAsync(7, "DIA-202610-0011"));
        Assert.Contains("ORDERED", exOrdered.Message);

        // Order 17 is CONFIRMED, but QueueEntry 17 is already READY
        var exReady = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReceiveDiagnosticPatientAsync(7, "DIA-202610-0017"));
        Assert.Contains("READY", exReady.Message);

        // Order 14 is COMPLETED
        var exCompleted = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReceiveDiagnosticPatientAsync(7, "DIA-202610-0014"));
        Assert.Contains("COMPLETED", exCompleted.Message);

        // Order 15 is CANCELLED
        var exCancelled = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReceiveDiagnosticPatientAsync(7, "DIA-202610-0015"));
        Assert.Contains("đã bị hủy", exCancelled.Message);
    }

    [Fact]
    public async Task ReceiveDiagnosticPatientAsync_StateVersionMismatchAndIneligibleDependencies_ThrowsConflictException()
    {
        // StateVersion mismatch (QueueEntry 13 has StateVersion = 2)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(ExpectedStateVersion: 99)));

        // Maintenance room (103)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(RoomId: 103)));

        // Inactive service point (504)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(ServicePointId: 504)));

        // Incompatible service point (502 is for Service 6, Order 13 is for Service 5)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(ServicePointId: 502)));

        // Deactivate Service 5 -> Conflict
        var service5 = await _dbContext.Services.FirstAsync(s => s.Id == 5);
        service5.IsActive = false;
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReceiveDiagnosticPatientAsync(7, "DIA-202610-0013"));
    }

    [Fact]
    public async Task ValidationFailures_ThrowValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForReceiveAsync(7, new GetDiagnosticOrdersForReceiveRequest(Limit: 0)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForReceiveAsync(7, new GetDiagnosticOrdersForReceiveRequest(Category: "INVALID")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForReceiveAsync(7, new GetDiagnosticOrdersForReceiveRequest(Status: "INVALID")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForReceiveAsync(7, new GetDiagnosticOrdersForReceiveRequest(QueueStatus: "INVALID")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForReceiveAsync(7, new GetDiagnosticOrdersForReceiveRequest(Priority: "INVALID")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrderForReceiveByIdOrCodeAsync(7, "   "));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(IsPatientPresent: false)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(IsIdentityVerified: false)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(IsPreparationVerified: false)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(ExpectedStateVersion: 0)));

        // Patient 2 (Visit 11, Order 13) is 34 years old -> ineligible for ELDERLY priority (>= 75)
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(ManualPriority: "ELDERLY")));

        // Patient 1 (Visit 10, Order 12) is MALE -> ineligible for PREGNANT priority
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0012",
                new ReceiveDiagnosticPatientRequest(ManualPriority: "PREGNANT")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(ReceptionNote: "<script>alert(1)</script>")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientAsync(
                7,
                "DIA-202610-0013",
                new ReceiveDiagnosticPatientRequest(RoomId: 104))); // CONSULTATION room, not DIAGNOSTIC

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientsBatchAsync(
                7,
                new ReceiveDiagnosticPatientsBatchRequest()));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReceiveDiagnosticPatientsBatchAsync(
                7,
                new ReceiveDiagnosticPatientsBatchRequest(OrderIds: [12, 12])));
    }

    [Fact]
    public async Task AuthorizationAndCrossHospitalIsolation_EnforcedAtLayer2()
    {
        // Invalid userId <= 0 -> 401
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetOrdersForReceiveAsync(0));

        // Non-existent user -> 404
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetOrdersForReceiveAsync(9999));

        // Non-diagnostic roles -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForReceiveAsync(21)); // DOCTOR
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForReceiveAsync(22)); // HOSPITAL_ADMIN
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForReceiveAsync(23)); // PATIENT
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForReceiveAsync(25)); // RECEPTION

        // Locked / inactive user / inactive staff / missing staff / missing hospital / inactive hospital / inactive dept / missing permission -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOrdersForReceiveAsync(14));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOrdersForReceiveAsync(15));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOrdersForReceiveAsync(16));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOrdersForReceiveAsync(17));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOrdersForReceiveAsync(18));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOrdersForReceiveAsync(19));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOrdersForReceiveAsync(20));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOrdersForReceiveAsync(24));

        // Cross-hospital access: Staff 7 (Hospital 1) accessing Order 20 (Hospital 2) -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrderForReceiveByIdOrCodeAsync(7, "DIA-202610-0020"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.ReceiveDiagnosticPatientAsync(7, "DIA-202610-0020"));

        // Staff with DIAGNOSTIC_RESULT_WRITE only (missing QUEUE_MANAGE) can view list with CanReceive=false, but mutation throws 403
        var queueManageRolePerm = await _dbContext.RolePermissions
            .FirstAsync(rp => rp.RoleId == 4 && rp.PermissionId == 2);
        _dbContext.RolePermissions.Remove(queueManageRolePerm);
        await _dbContext.SaveChangesAsync();

        var readList = await _service.GetOrdersForReceiveAsync(7);
        Assert.All(readList.Items, i => Assert.False(i.CanReceive));

        var exMissingQueueManage = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.ReceiveDiagnosticPatientAsync(7, "DIA-202610-0013"));
        Assert.Contains("QUEUE_MANAGE", exMissingQueueManage.Message);
    }
}
