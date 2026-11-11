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

public sealed class DiagnosticQueueServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DiagnosticQueueTestDbContext : DbContext, IApplicationDbContext
    {
        public DiagnosticQueueTestDbContext(DbContextOptions<DiagnosticQueueTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DiagnosticQueueService unit tests.");

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

    private readonly DiagnosticQueueTestDbContext _dbContext;
    private readonly DiagnosticQueueService _service;

    public DiagnosticQueueServiceTests()
    {
        var options = new DbContextOptionsBuilder<DiagnosticQueueTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DiagnosticQueueTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new DiagnosticQueueTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new DiagnosticQueueService(_dbContext, timeProvider);
    }

    private static void SeedData(DiagnosticQueueTestDbContext db)
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

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Lễ tân" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Nhân viên Cận lâm sàng" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 8, Code = "DIAGNOSTIC_NO_PERM", Name = "CLS không có quyền" },
            new Role { Id = 9, Code = "DIAGNOSTIC_READ_ONLY", Name = "CLS chỉ xem" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "DIAGNOSTIC_RESULT_WRITE" },
            new Permission { Id = 2, Code = "QUEUE_MANAGE" },
            new Permission { Id = 3, Code = "DIAGNOSTIC_ORDER" },
            new Permission { Id = 4, Code = "HOSPITAL_CONFIG" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 4, PermissionId = 1 },
            new RolePermission { RoleId = 4, PermissionId = 2 },
            new RolePermission { RoleId = 8, PermissionId = 4 },
            new RolePermission { RoleId = 9, PermissionId = 3 });

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
            new User { Id = 25, Email = "readonly_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE });

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
            new UserRole { UserId = 25, RoleId = 4 },
            new UserRole { UserId = 25, RoleId = 9 });

        // Remove RoleId 4 permissions for user 25? Wait: EnsureDiagnosticStaffAccessAsync checks roleCode "DIAGNOSTIC_STAFF" AND collects permissions across all assigned roles.
        // So if Role 4 has Permission 1 & 2, any user with Role 4 gets Permission 1 & 2!
        // Let's keep Role 4 for user 7..20, and for user 24 (no perm) give Role 4? Wait: how does EnsureDiagnosticStaffAccessAsync check permissions?
        // In EnsureDiagnosticStaffAccessAsync, user must have role DIAGNOSTIC_STAFF, and permissions come from RolePermissions for user's RoleIds!

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
                DateOfBirth = new DateOnly(1985, 5, 10),
                Gender = "MALE",
                Phone = "0912345678",
                BloodType = "O+",
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
            },
            new Patient
            {
                Id = 3,
                PatientCode = "PAT-0003",
                FullName = "Lê Hoàng Cường",
                DateOfBirth = new DateOnly(1950, 2, 15),
                Gender = "MALE",
                Phone = "0905112233",
                BloodType = "B+",
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
                PatientId = 3,
                AssignedDoctorId = 1,
                ExaminationRoomId = 104,
                ReasonForVisit = "Khó thở cấp, theo dõi mạch vành",
                Status = "AWAITING_RESULTS",
                CheckedInAt = new DateTime(2026, 10, 20, 8, 20, 0),
            },
            new Visit
            {
                Id = 13,
                VisitCode = "VIS-202610-0013",
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
            // Entry 11: ServicePoint 501 (Lab), WAITING, CONFIRMED order (NORMAL priority, arrived 08:30)
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
            // Entry 12: ServicePoint 502 (X-ray), READY, CONFIRMED order (URGENT priority)
            new QueueEntry
            {
                Id = 12,
                VisitId = 10,
                ServicePointId = 502,
                CurrentServicePointId = 502,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 2,
                QueueType = "DIAGNOSTIC",
                Status = "READY",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 32, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 32, 0),
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 1,
            },
            // Entry 13: ServicePoint 501 (Lab), READY, CONFIRMED order (STAT priority, arrived 08:40 -> ranks ahead of Entry 11!)
            new QueueEntry
            {
                Id = 13,
                VisitId = 11,
                ServicePointId = 501,
                CurrentServicePointId = 501,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 3,
                QueueType = "DIAGNOSTIC",
                Status = "READY",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 40, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 40, 0),
                ArrivalClass = "WALK_IN",
                ManualPriority = "PREGNANT",
                StateVersion = 2,
            },
            // Entry 14: ServicePoint 501 (Lab), WAITING, ORDERED (unconfirmed -> not eligible for call-next!)
            new QueueEntry
            {
                Id = 14,
                VisitId = 12,
                ServicePointId = 501,
                CurrentServicePointId = 501,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 4,
                QueueType = "DIAGNOSTIC",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 45, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 45, 0),
                ArrivalClass = "WALK_IN",
                ManualPriority = "ELDERLY",
                StateVersion = 1,
            },
            // Entry 15: Historical completed entry on 2026-10-15
            new QueueEntry
            {
                Id = 15,
                VisitId = 13,
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
            // Entry 20: Hospital 2 diagnostic queue entry
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
            },
            // Entry 30: Clinical consultation queue entry (non-diagnostic)
            new QueueEntry
            {
                Id = 30,
                VisitId = 10,
                ServicePointId = 505,
                CurrentServicePointId = 505,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 10,
                QueueType = "APPOINTMENT",
                Status = "IN_PROGRESS",
                PresenceStatus = "PRESENT",
                StateVersion = 2,
            });

        db.DiagnosticOrders.AddRange(
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
                Priority = "NORMAL",
                Status = "CONFIRMED",
                ConfirmedByStaffId = 7,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 31, 0),
                OrderedAt = new DateTime(2026, 10, 20, 8, 30, 0),
            },
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
                Priority = "URGENT",
                Status = "CONFIRMED",
                ConfirmedByStaffId = 7,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 33, 0),
                OrderedAt = new DateTime(2026, 10, 20, 8, 32, 0),
            },
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
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 41, 0),
                OrderedAt = new DateTime(2026, 10, 20, 8, 40, 0),
            },
            new DiagnosticOrder
            {
                Id = 14,
                OrderCode = "DIA-202610-0014",
                VisitId = 12,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = 14,
                Instruction = "Xét nghiệm chưa xác nhận tiếp nhận",
                RequiredForConclusion = false,
                Priority = "NORMAL",
                Status = "ORDERED",
                OrderedAt = new DateTime(2026, 10, 20, 8, 45, 0),
            },
            new DiagnosticOrder
            {
                Id = 15,
                OrderCode = "DIA-202610-0015",
                VisitId = 13,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = 15,
                Instruction = "Xét nghiệm định kỳ đã hoàn tất",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "COMPLETED",
                ConfirmedByStaffId = 7,
                ConfirmedAt = new DateTime(2026, 10, 15, 9, 0, 0),
                OrderedAt = new DateTime(2026, 10, 15, 8, 45, 0),
            },
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
            DiagnosticOrderId = 15,
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
    public async Task GetDiagnosticQueueAsync_ReturnsHospitalScopedBucketsDeterministicallyOrderedByPriorityAndFifo()
    {
        var board = await _service.GetDiagnosticQueueAsync(7, new GetDiagnosticQueueRequest(QueueDate: "2026-10-20"));

        Assert.Equal(1, board.HospitalId);
        Assert.Equal("HOS-DEMO-01", board.HospitalCode);
        Assert.Equal("2026-10-20", board.QueueDate);
        Assert.Equal(7, board.StaffProfile.UserId);
        Assert.Equal("STF-007", board.StaffProfile.StaffCode);

        // Today (2026-10-20) in Hospital 1 has entries 11, 12, 13, 14 (excludes entry 15 on 2026-10-15, entry 20 in Hospital 2, and entry 30 APPOINTMENT)
        Assert.Equal(4, board.Stats.TotalEntries);
        Assert.DoesNotContain(board.Entries, e => e.QueueEntryId == 20);
        Assert.DoesNotContain(board.Entries, e => e.QueueEntryId == 30);

        var labBucket = Assert.Single(board.ServicePoints, b => b.ServicePointId == 501);
        Assert.Equal(3, labBucket.Entries.Count);

        // Within ServicePoint 501:
        // Rank 1: Entry 13 (READY, STAT, CONFIRMED -> Eligible)
        // Rank 2: Entry 11 (WAITING, NORMAL, CONFIRMED -> Eligible)
        // Rank 3: Entry 14 (WAITING, ORDERED -> Ineligible because order is not confirmed yet)
        Assert.Equal(13, labBucket.Entries[0].QueueEntryId);
        Assert.Equal(1, labBucket.Entries[0].ServingOrder);
        Assert.True(labBucket.Entries[0].IsEligible);

        Assert.Equal(11, labBucket.Entries[1].QueueEntryId);
        Assert.Equal(2, labBucket.Entries[1].ServingOrder);
        Assert.True(labBucket.Entries[1].IsEligible);

        Assert.Equal(14, labBucket.Entries[2].QueueEntryId);
        Assert.Equal(3, labBucket.Entries[2].ServingOrder);
        Assert.False(labBucket.Entries[2].IsEligible);
        Assert.Contains("chưa được xác nhận", labBucket.Entries[2].IneligibleReason);

        Assert.Equal("C003", labBucket.NextWaitingTicketNo);
        Assert.Equal(13, labBucket.NextWaitingEntry?.QueueEntryId);

        // Filter by Category = IMAGING
        var imagingBoard = await _service.GetDiagnosticQueueAsync(
            7,
            new GetDiagnosticQueueRequest(Category: "IMAGING", ActiveOnly: true));
        var imagingEntry = Assert.Single(imagingBoard.Entries);
        Assert.Equal(12, imagingEntry.QueueEntryId);
        Assert.Equal("IMAGING", imagingEntry.Category);
    }

    [Fact]
    public async Task GetDiagnosticQueueEntryByIdOrCodeAsync_ResolvesMultipleFormatsAndReturnsClinicalContext()
    {
        var byCode = await _service.GetDiagnosticQueueEntryByIdOrCodeAsync(7, "q-011");
        Assert.Equal(11, byCode.Entry.QueueEntryId);
        Assert.Equal("C001", byCode.Entry.TicketNo);
        Assert.Equal("DIA-202610-0011", byCode.Entry.OrderCode);
        Assert.Equal("Đau tức ngực trái lan lên vai", byCode.ClinicalContext.PresentingComplaint);
        Assert.Equal("Tim đều 92 lần/phút, phổi thông khí rõ", byCode.ClinicalContext.ClinicalFindings);
        Assert.Equal("I20.9", byCode.ClinicalContext.PrimaryDiagnosisCode);

        var byOrderCode = await _service.GetDiagnosticQueueEntryByIdOrCodeAsync(7, "DIA-202610-0012");
        Assert.Equal(12, byOrderCode.Entry.QueueEntryId);

        var byTicket = await _service.GetDiagnosticQueueEntryByIdOrCodeAsync(7, "C003");
        Assert.Equal(13, byTicket.Entry.QueueEntryId);
    }

    [Fact]
    public async Task CallNextDiagnosticPatientAsync_CallsHighestPriorityEligiblePatient_AndBlocksSecondCallWhileBusy()
    {
        var callNext = await _service.CallNextDiagnosticPatientAsync(
            actorUserId: 7,
            servicePointId: "sp-501",
            request: new CallNextDiagnosticPatientRequest(Notes: "Mời bệnh nhân ưu tiên cấp cứu vào lấy mẫu"),
            ipAddress: "127.0.0.1");

        Assert.Equal("CALLED", callNext.Outcome);
        Assert.Equal("PRIORITY_STAT", callNext.SelectionReason);
        Assert.NotNull(callNext.CalledEntry);
        Assert.Equal(13, callNext.CalledEntry!.QueueEntryId);
        Assert.Equal("CALLED", callNext.CalledEntry.Status);
        Assert.Equal(2, callNext.PreviousStateVersion);
        Assert.Equal(3, callNext.StateVersion);
        Assert.Equal("C003", callNext.ServicePointBucket.CurrentServingTicketNo);

        // Verify Visit.Status was NOT overwritten
        var visit11 = await _dbContext.Visits.FindAsync(11L);
        Assert.Equal("IN_PROGRESS", visit11!.Status);

        // Verify AuditLog was written
        var audit = await _dbContext.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == DiagnosticQueueService.DiagnosticQueueCallNextAction && a.EntityId == "13");
        Assert.NotNull(audit);

        // Calling next again on sp-501 while Entry 13 is CALLED must throw 409 Conflict
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CallNextDiagnosticPatientAsync(7, "501"));
    }

    [Fact]
    public async Task CallNextDiagnosticPatientAsync_ReturnsNoEligiblePatient_WhenQueueHasNoConfirmedWaitingEntries()
    {
        // Complete Entry 12 on ServicePoint 502 first so sp-502 has no waiting entries
        await _service.UpdateDiagnosticQueueEntryStatusAsync(
            7,
            "q-012",
            new UpdateDiagnosticQueueStatusRequest(TargetStatus: "IN_PROGRESS"));
        await _service.UpdateDiagnosticQueueEntryStatusAsync(
            7,
            "q-012",
            new UpdateDiagnosticQueueStatusRequest(TargetStatus: "COMPLETED"));

        var result = await _service.CallNextDiagnosticPatientAsync(7, "sp-502");

        Assert.Equal("NO_ELIGIBLE_PATIENT", result.Outcome);
        Assert.Null(result.CalledEntry);
    }

    [Fact]
    public async Task UpdateDiagnosticQueueEntryStatusAsync_ExecutesFullLifecycle_SyncsOrderStatus_AndPreservesVisitStatus()
    {
        // Entry 11 starts at WAITING (StateVersion = 1), Order 11 is CONFIRMED, Visit 10 is AWAITING_RESULTS
        var readyResult = await _service.UpdateDiagnosticQueueEntryStatusAsync(
            actorUserId: 7,
            idOrCode: "q-011",
            request: new UpdateDiagnosticQueueStatusRequest(
                TargetStatus: "READY",
                PresenceStatus: "PRESENT",
                ExpectedStateVersion: 1),
            ipAddress: "10.0.0.10");

        Assert.Equal("WAITING", readyResult.PreviousStatus);
        Assert.Equal("READY", readyResult.CurrentStatus);
        Assert.Equal(2, readyResult.StateVersion);

        var calledResult = await _service.UpdateDiagnosticQueueEntryStatusAsync(
            actorUserId: 7,
            idOrCode: "q-011",
            request: new UpdateDiagnosticQueueStatusRequest(
                TargetStatus: "CALLED",
                ExpectedStateVersion: 2));

        Assert.Equal("CALLED", calledResult.CurrentStatus);
        Assert.Equal(3, calledResult.StateVersion);

        var startResult = await _service.UpdateDiagnosticQueueEntryStatusAsync(
            actorUserId: 7,
            idOrCode: "q-011",
            request: new UpdateDiagnosticQueueStatusRequest(
                TargetStatus: "IN_PROGRESS",
                Notes: "Bắt đầu lấy mẫu máu tĩnh mạch",
                ExpectedStateVersion: 3));

        Assert.Equal("IN_PROGRESS", startResult.CurrentStatus);
        Assert.Equal("IN_PROGRESS", startResult.OrderStatus);
        Assert.Equal(4, startResult.StateVersion);

        var orderInProgress = await _dbContext.DiagnosticOrders.FindAsync(11L);
        Assert.Equal("IN_PROGRESS", orderInProgress!.Status);

        var completeResult = await _service.UpdateDiagnosticQueueEntryStatusAsync(
            actorUserId: 7,
            idOrCode: "q-011",
            request: new UpdateDiagnosticQueueStatusRequest(
                TargetStatus: "COMPLETED",
                Notes: "Đã lấy mẫu hoàn tất, chuyển phòng xét nghiệm",
                ExpectedStateVersion: 4));

        Assert.Equal("COMPLETED", completeResult.CurrentStatus);
        Assert.Equal("COMPLETED", completeResult.OrderStatus);
        Assert.Equal(5, completeResult.StateVersion);

        var orderCompleted = await _dbContext.DiagnosticOrders.FindAsync(11L);
        Assert.Equal("COMPLETED", orderCompleted!.Status);

        // Clinical Visit 10 MUST remain AWAITING_RESULTS
        var visit10 = await _dbContext.Visits.FindAsync(10L);
        Assert.Equal("AWAITING_RESULTS", visit10!.Status);
        Assert.Null(visit10.CompletedAt);

        // Detail endpoint reconstructs status transition history from AuditLogs
        var detail = await _service.GetDiagnosticQueueEntryByIdOrCodeAsync(7, "q-011");
        Assert.Equal(4, detail.StatusHistory.Count);
        Assert.Equal("COMPLETED", detail.StatusHistory[0].TargetStatus);
    }

    [Fact]
    public async Task MissedTurn_RestoreTurn_AndCancel_EnforceStateMachineAndAuditTrail()
    {
        // Call Entry 12 first (READY -> CALLED)
        var called = await _service.UpdateDiagnosticQueueEntryStatusAsync(
            7,
            "q-012",
            new UpdateDiagnosticQueueStatusRequest(TargetStatus: "CALLED"));
        Assert.Equal("CALLED", called.CurrentStatus);

        // Mark missed turn (CALLED -> SKIPPED)
        var skipped = await _service.MarkDiagnosticMissedTurnAsync(
            actorUserId: 7,
            idOrCode: "q-012",
            request: new DiagnosticQueueTurnActionRequest(
                Reason: "Gọi 3 lần qua loa bệnh nhân chưa có mặt tại phòng X-quang",
                ExpectedStateVersion: called.StateVersion));

        Assert.Equal("MISSED_TURN", skipped.ActionOutcome);
        Assert.Equal("SKIPPED", skipped.CurrentStatus);
        Assert.Equal("LEFT", skipped.PresenceStatus);
        Assert.Contains("Gọi 3 lần", skipped.BlockedReason);

        // Restore skipped turn (SKIPPED -> READY)
        var restored = await _service.RestoreDiagnosticSkippedTurnAsync(
            actorUserId: 7,
            idOrCode: "q-012",
            request: new DiagnosticQueueTurnActionRequest(
                Reason: "Bệnh nhân đã quay lại quầy tiếp nhận X-quang",
                ExpectedStateVersion: skipped.StateVersion));

        Assert.Equal("RESTORED_TURN", restored.ActionOutcome);
        Assert.Equal("READY", restored.CurrentStatus);
        Assert.Equal("PRESENT", restored.PresenceStatus);
        Assert.Null(restored.BlockedReason);

        // Restoring again when already READY must fail with 409 Conflict
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.RestoreDiagnosticSkippedTurnAsync(7, "q-012"));

        // Cancel Entry 12 with CancelLinkedOrder = true
        var cancelled = await _service.CancelDiagnosticQueueEntryAsync(
            actorUserId: 7,
            idOrCode: "q-012",
            request: new CancelDiagnosticQueueEntryRequest(
                Reason: "Bệnh nhân xin hoãn chụp X-quang theo chỉ định mới của bác sĩ",
                CancelLinkedOrder: true,
                ExpectedStateVersion: restored.StateVersion));

        Assert.Equal("CANCELLED", cancelled.ActionOutcome);
        Assert.Equal("CANCELLED", cancelled.CurrentStatus);
        Assert.Equal("CANCELLED", cancelled.OrderStatus);

        var order12 = await _dbContext.DiagnosticOrders.FindAsync(12L);
        Assert.Equal("CANCELLED", order12!.Status);
    }

    [Fact]
    public async Task QueueMutations_EnforceUnconfirmedOrder_Concurrency_AndInactiveFacilityGuards()
    {
        // Entry 14 has Order 14 in ORDERED status -> calling or starting must throw 409 Conflict
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticQueueEntryStatusAsync(
                7,
                "q-014",
                new UpdateDiagnosticQueueStatusRequest(TargetStatus: "CALLED")));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticQueueEntryStatusAsync(
                7,
                "q-014",
                new UpdateDiagnosticQueueStatusRequest(TargetStatus: "IN_PROGRESS")));

        // Stale ExpectedStateVersion on Entry 13 (actual StateVersion = 2) -> 409 Conflict
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticQueueEntryStatusAsync(
                7,
                "q-013",
                new UpdateDiagnosticQueueStatusRequest(TargetStatus: "CALLED", ExpectedStateVersion: 99)));

        // Calling next on maintenance room ServicePoint 503 or inactive ServicePoint 504 -> 409 Conflict
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CallNextDiagnosticPatientAsync(7, "sp-503"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CallNextDiagnosticPatientAsync(7, "sp-504"));

        // Calling next on consultation ServicePoint 505 -> 409 Conflict
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CallNextDiagnosticPatientAsync(7, "505"));
    }

    [Fact]
    public async Task TwoLayerAuthorization_AndCrossHospitalIsolation_ThrowExpectedExceptions()
    {
        // Layer 2 actorUserId validation
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetDiagnosticQueueAsync(0));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDiagnosticQueueAsync(9999));

        // Account status & role checks
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(14)); // locked
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(15)); // inactive user
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(16)); // inactive staff
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(17)); // missing staff profile
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(18)); // unassigned hospital
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(19)); // inactive hospital
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(20)); // inactive department
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(21)); // DOCTOR role
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(22)); // HOSPITAL_ADMIN role
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueAsync(24)); // missing DIAGNOSTIC_STAFF role

        // Cross-hospital access by Hospital 1 staff (user 7) to Hospital 2 entry (q-020) or service point (sp-601)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDiagnosticQueueEntryByIdOrCodeAsync(7, "q-020"));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateDiagnosticQueueEntryStatusAsync(
                7,
                "q-020",
                new UpdateDiagnosticQueueStatusRequest(TargetStatus: "CALLED")));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CallNextDiagnosticPatientAsync(7, "sp-601"));

        // Non-diagnostic queue entry (Entry 30 is APPOINTMENT) -> 409 Conflict
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetDiagnosticQueueEntryByIdOrCodeAsync(7, "30"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task GetDiagnosticQueueAsync_InvalidLimit_ThrowsValidationException(int invalidLimit)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDiagnosticQueueAsync(7, new GetDiagnosticQueueRequest(Limit: invalidLimit)));
    }
}
