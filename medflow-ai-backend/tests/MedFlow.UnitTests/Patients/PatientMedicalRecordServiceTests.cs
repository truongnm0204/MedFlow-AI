using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Patients;

public sealed class PatientMedicalRecordServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed record RecordedAudit(
        long? ActorUserId,
        string Action,
        string EntityType,
        string? EntityId,
        object? ChangeData,
        string? IpAddress);

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<RecordedAudit> Entries { get; } = [];

        public void Add(
            long? actorUserId,
            string action,
            string entityType,
            string? entityId,
            object? changeData,
            string? ipAddress)
            => Entries.Add(new RecordedAudit(actorUserId, action, entityType, entityId, changeData, ipAddress));
    }

    private sealed class EmrTestDbContext(DbContextOptions<EmrTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
        public DbSet<MedicalRecordRevision> MedicalRecordRevisions => Set<MedicalRecordRevision>();
        public DbSet<VisitDiagnosis> VisitDiagnoses => Set<VisitDiagnosis>();
        public DbSet<Icd10Code> Icd10Codes => Set<Icd10Code>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
        public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => Set<DiagnosticResultVersion>();
        public DbSet<Medicine> Medicines => Set<Medicine>();
        public DbSet<Prescription> Prescriptions => Set<Prescription>();
        public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>(b =>
            {
                b.HasKey(u => u.Id);
                b.Ignore(u => u.UserRoles);
                b.Ignore(u => u.Staff);
                b.Ignore(u => u.AuthSessions);
            });
            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User)
                    .WithOne(u => u.Patient)
                    .HasForeignKey<Patient>(p => p.UserId);
            });
            modelBuilder.Entity<PatientFamilyLink>(b =>
            {
                b.HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
                b.HasOne(l => l.MemberPatient)
                    .WithMany()
                    .HasForeignKey(l => l.MemberPatientId);
                b.HasOne(l => l.OwnerPatient)
                    .WithMany()
                    .HasForeignKey(l => l.OwnerPatientId);
            });
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.User);
                b.Ignore(s => s.Department);
                b.Ignore(s => s.Doctor);
            });
            modelBuilder.Entity<Doctor>(b =>
            {
                b.HasKey(d => d.Id);
                b.Ignore(d => d.Staff);
                b.Ignore(d => d.Specialty);
                b.Ignore(d => d.ServiceAuthorizations);
                b.Ignore(d => d.ExaminationSlots);
            });
            modelBuilder.Entity<ExaminationSlot>().HasKey(s => s.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<MedicalRecord>().HasKey(r => r.Id);
            modelBuilder.Entity<MedicalRecordRevision>().HasKey(r => r.Id);
            modelBuilder.Entity<VisitDiagnosis>().HasKey(d => d.Id);
            modelBuilder.Entity<Icd10Code>().HasKey(c => c.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(o => o.Id);
            modelBuilder.Entity<DiagnosticResult>().HasKey(r => r.Id);
            modelBuilder.Entity<DiagnosticResultVersion>().HasKey(v => v.Id);
            modelBuilder.Entity<Medicine>().HasKey(m => m.Id);
            modelBuilder.Entity<Prescription>().HasKey(p => p.Id);
            modelBuilder.Entity<PrescriptionItem>().HasKey(i => i.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
            });
        }

        private static NotSupportedException NotUsed()
            => new("DbSet không dùng trong PatientMedicalRecordServiceTests.");
    }

    private static readonly DateTime NowUtc = new(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc);

    private readonly EmrTestDbContext _db;
    private readonly RecordingAuditLogger _audit = new();
    private readonly PatientMedicalRecordService _sut;

    public PatientMedicalRecordServiceTests()
    {
        var options = new DbContextOptionsBuilder<EmrTestDbContext>()
            .UseInMemoryDatabase($"patient-emr-{Guid.NewGuid():N}")
            .Options;
        _db = new EmrTestDbContext(options);
        _sut = new PatientMedicalRecordService(_db, _audit, new FixedTimeProvider(new DateTimeOffset(NowUtc)));
    }

    [Fact]
    public async Task GetMyMedicalRecordAsync_OwnCompletedFinalizedRecord_ReturnsFullClinicalSummaryAndAudits()
    {
        await SeedBaseCatalogAsync();
        await SeedCompletedEmrAsync(
            recordId: 501,
            visitId: 301,
            visitCode: "VIS-2026-0301",
            patientId: 1,
            hospitalId: 10,
            specialtyId: 20,
            completedAt: new DateTime(2026, 11, 15, 10, 30, 0));

        var detailByCode = await _sut.GetMyMedicalRecordAsync(100, "emr-501", ipAddress: "127.0.0.1");
        var detailByVisitCode = await _sut.GetMyMedicalRecordAsync(100, "vis-2026-0301", ipAddress: "127.0.0.1");

        Assert.Equal(501, detailByCode.MedicalRecordId);
        Assert.Equal("EMR-501", detailByCode.RecordCode);
        Assert.Equal("VIS-2026-0301", detailByCode.VisitCode);
        Assert.Equal(detailByCode.MedicalRecordId, detailByVisitCode.MedicalRecordId);
        Assert.True(detailByCode.IsSelfProfile);
        Assert.Equal("Bệnh viện Đa khoa MedFlow", detailByCode.HospitalName);
        Assert.Equal("Nội tổng quát", detailByCode.SpecialtyName);
        Assert.Equal("BS.CKI Trần Quốc Tuấn", detailByCode.DoctorName);
        Assert.Equal("Đau thượng vị âm ỉ 3 ngày", detailByCode.PresentingComplaint);
        Assert.Equal("Viêm dạ dày cấp ổn định, tái khám sau 2 tuần.", detailByCode.ClinicalConclusion);

        Assert.NotNull(detailByCode.VitalSigns);
        Assert.Equal("120/80 mmHg", detailByCode.VitalSigns!.BloodPressureLabel);
        Assert.Equal(22.9m, detailByCode.VitalSigns.Bmi);

        var primaryDx = Assert.Single(detailByCode.Diagnoses);
        Assert.Equal("K29.0", primaryDx.Icd10Code);
        Assert.True(primaryDx.IsPrimary);

        Assert.Equal(2, detailByCode.DiagnosticOrders.Count);
        var publishedCls = Assert.Single(detailByCode.DiagnosticOrders, o => o.OrderCode == "ORD-PUB-301");
        Assert.True(publishedCls.IsResultPublished);
        Assert.Equal("Viêm trợt hang vị nhẹ.", publishedCls.ConclusionSummary);
        Assert.True(publishedCls.HasReportFile);

        var pendingCls = Assert.Single(detailByCode.DiagnosticOrders, o => o.OrderCode == "ORD-PEND-301");
        Assert.False(pendingCls.IsResultPublished);
        Assert.Null(pendingCls.ConclusionSummary);
        Assert.False(pendingCls.HasReportFile);

        var rx = Assert.Single(detailByCode.Prescriptions);
        Assert.Equal("RX-FINAL-301", rx.PrescriptionCode);
        Assert.True(rx.CanViewPrescriptionDetail);
        Assert.Single(rx.Items);

        Assert.All(_audit.Entries, entry =>
        {
            Assert.Equal(PatientMedicalRecordService.AuditActionRecordViewed, entry.Action);
            Assert.Equal("MEDICAL_RECORD", entry.EntityType);
            Assert.Equal("501", entry.EntityId);
        });
    }

    [Fact]
    public async Task GetMyMedicalRecordsAsync_EnforcesPhiFamilyScope_AllowsOtpAndStaff_BlocksLegacyAndPending()
    {
        await SeedBaseCatalogAsync();

        _db.Patients.AddRange(
            new Patient { Id = 2, PatientCode = "PAT-OTP", FullName = "Bé Minh (OTP)", Status = "ACTIVE" },
            new Patient { Id = 3, PatientCode = "PAT-STAFF", FullName = "Bà Lan (STAFF)", Status = "ACTIVE" },
            new Patient { Id = 4, PatientCode = "PAT-LEGACY", FullName = "Ông Bình (LEGACY)", Status = "ACTIVE" },
            new Patient { Id = 5, PatientCode = "PAT-PENDING", FullName = "Em Hà (PENDING)", Status = "ACTIVE" });

        _db.PatientFamilyLinks.AddRange(
            new PatientFamilyLink
            {
                OwnerPatientId = 1,
                MemberPatientId = 2,
                Relationship = "Con trai",
                VerificationStatus = "VERIFIED",
                VerificationMethod = "OTP",
            },
            new PatientFamilyLink
            {
                OwnerPatientId = 1,
                MemberPatientId = 3,
                Relationship = "Mẹ ruột",
                VerificationStatus = "VERIFIED",
                VerificationMethod = "STAFF",
            },
            new PatientFamilyLink
            {
                OwnerPatientId = 1,
                MemberPatientId = 4,
                Relationship = "Bố ruột",
                VerificationStatus = "VERIFIED",
                VerificationMethod = "LEGACY",
                LegacyAccessUntil = NowUtc.AddDays(25),
            },
            new PatientFamilyLink
            {
                OwnerPatientId = 1,
                MemberPatientId = 5,
                Relationship = "Khác",
                VerificationStatus = "PENDING",
                VerificationMethod = null,
            });
        await _db.SaveChangesAsync();

        await SeedCompletedEmrAsync(501, 301, "VIS-SELF", patientId: 1, hospitalId: 10, specialtyId: 20, completedAt: new DateTime(2026, 11, 18, 9, 0, 0));
        await SeedCompletedEmrAsync(502, 302, "VIS-OTP", patientId: 2, hospitalId: 10, specialtyId: 20, completedAt: new DateTime(2026, 11, 17, 9, 0, 0));
        await SeedCompletedEmrAsync(503, 303, "VIS-STAFF", patientId: 3, hospitalId: 10, specialtyId: 20, completedAt: new DateTime(2026, 11, 16, 9, 0, 0));
        await SeedCompletedEmrAsync(504, 304, "VIS-LEGACY", patientId: 4, hospitalId: 10, specialtyId: 20, completedAt: new DateTime(2026, 11, 19, 9, 0, 0));
        await SeedCompletedEmrAsync(505, 305, "VIS-PENDING", patientId: 5, hospitalId: 10, specialtyId: 20, completedAt: new DateTime(2026, 11, 19, 10, 0, 0));

        var allVisible = await _sut.GetMyMedicalRecordsAsync(100, new GetPatientMedicalRecordsRequest());

        Assert.Equal(3, allVisible.TotalCount);
        Assert.Equal([501L, 502L, 503L], allVisible.Items.Select(i => i.MedicalRecordId).ToArray());
        Assert.Equal([1L, 2L, 3L], allVisible.Members.Select(m => m.PatientId).ToArray());

        var otpDetail = await _sut.GetMyMedicalRecordAsync(100, "502");
        Assert.False(otpDetail.IsSelfProfile);
        Assert.All(otpDetail.Prescriptions, rx => Assert.False(rx.CanViewPrescriptionDetail));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetMyMedicalRecordsAsync(100, new GetPatientMedicalRecordsRequest(PatientId: 4)));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetMyMedicalRecordAsync(100, "504"));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetMyMedicalRecordAsync(100, "VIS-PENDING"));
    }

    [Fact]
    public async Task GetMyMedicalRecordsAsync_FiltersByDateRangeHospitalSpecialtyAndExcludesUnfinalized()
    {
        await SeedBaseCatalogAsync();
        _db.Hospitals.Add(new Hospital { Id = 11, HospitalCode = "BV-02", Name = "Cơ sở Quận 7", IsActive = true });
        _db.Specialties.Add(new Specialty { Id = 21, SpecialtyCode = "CARDIO", Name = "Tim mạch" });
        _db.HospitalRooms.Add(new HospitalRoom { Id = 41, HospitalId = 11, RoomCode = "P201", Name = "Phòng Tim mạch", Status = "ACTIVE" });
        _db.Staff.Add(new Staff { Id = 61, HospitalId = 11, StaffCode = "BS61", FullName = "Lê Hoàng Nam", Status = "ACTIVE" });
        _db.Doctors.Add(new Doctor { Id = 51, StaffId = 61, HospitalId = 11, SpecialtyId = 21, DoctorCode = "DR51", ProfessionalTitle = "ThS.BS", IsActive = true });
        await _db.SaveChangesAsync();

        await SeedCompletedEmrAsync(601, 401, "VIS-OCT", patientId: 1, hospitalId: 10, specialtyId: 20, completedAt: new DateTime(2026, 10, 10, 10, 0, 0));
        await SeedCompletedEmrAsync(602, 402, "VIS-NOV-CARDIO", patientId: 1, hospitalId: 11, specialtyId: 21, completedAt: new DateTime(2026, 11, 12, 15, 0, 0), roomId: 41, doctorId: 51);

        // In-progress visit + draft record must be hidden
        _db.Visits.Add(new Visit { Id = 403, VisitCode = "VIS-DRAFT", PatientId = 1, ExaminationRoomId = 40, AssignedDoctorId = 50, Status = "IN_PROGRESS", CheckedInAt = new DateTime(2026, 11, 19, 8, 0, 0) });
        _db.MedicalRecords.Add(new MedicalRecord { Id = 603, VisitId = 403, CreatedByDoctorId = 50, Status = "DRAFT" });
        await _db.SaveChangesAsync();

        var filtered = await _sut.GetMyMedicalRecordsAsync(
            100,
            new GetPatientMedicalRecordsRequest(
                FromDate: "2026-11-01",
                ToDate: "2026-11-30",
                HospitalId: 11,
                SpecialtyCode: "cardio"));

        var single = Assert.Single(filtered.Items);
        Assert.Equal(602, single.MedicalRecordId);
        Assert.Equal("Cơ sở Quận 7", single.HospitalName);
        Assert.Equal("Tim mạch", single.SpecialtyName);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetMyMedicalRecordAsync(100, "603"));
    }

    [Fact]
    public async Task GetMyMedicalRecordAsync_ForeignRecord_ThrowsForbidden()
    {
        await SeedBaseCatalogAsync();
        _db.Patients.Add(new Patient { Id = 99, PatientCode = "PAT-99", FullName = "Người lạ", Status = "ACTIVE" });
        await _db.SaveChangesAsync();
        await SeedCompletedEmrAsync(701, 501, "VIS-STRANGER", patientId: 99, hospitalId: 10, specialtyId: 20, completedAt: new DateTime(2026, 11, 10, 9, 0, 0));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetMyMedicalRecordAsync(100, "701"));
    }

    [Fact]
    public async Task ValidationAndLockedAccount_ThrowExpectedExceptions()
    {
        await SeedBaseCatalogAsync(lockedUntil: NowUtc.AddMinutes(10));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetMyMedicalRecordsAsync(100, new GetPatientMedicalRecordsRequest()));

        var unlockedUser = await _db.Users.FirstAsync(u => u.Id == 100);
        unlockedUser.LockedUntil = null;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetMyMedicalRecordsAsync(100, new GetPatientMedicalRecordsRequest(FromDate: "2026-11-20", ToDate: "2026-11-01")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetMyMedicalRecordAsync(100, "  #  "));
    }

    private async Task SeedBaseCatalogAsync(DateTime? lockedUntil = null)
    {
        var user = new User
        {
            Id = 100,
            Email = "patient@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
            LockedUntil = lockedUntil,
        };
        var self = new Patient
        {
            Id = 1,
            UserId = 100,
            User = user,
            PatientCode = "PAT-001",
            FullName = "Nguyễn Văn An",
            Gender = "MALE",
            DateOfBirth = new DateOnly(1992, 5, 14),
            BloodType = "O+",
            Status = "ACTIVE",
        };

        _db.Users.Add(user);
        _db.Patients.Add(self);
        _db.Hospitals.Add(new Hospital { Id = 10, HospitalCode = "BV-01", Name = "Bệnh viện Đa khoa MedFlow", IsActive = true });
        _db.Departments.Add(new Department { Id = 30, HospitalId = 10, DepartmentCode = "KHOA-NOI", Name = "Khoa Nội" });
        _db.Specialties.Add(new Specialty { Id = 20, SpecialtyCode = "INT-MED", Name = "Nội tổng quát" });
        _db.HospitalRooms.Add(new HospitalRoom { Id = 40, HospitalId = 10, DepartmentId = 30, RoomCode = "P101", Name = "Phòng Nội 101", Status = "ACTIVE" });
        _db.Staff.Add(new Staff { Id = 60, HospitalId = 10, DepartmentId = 30, StaffCode = "BS60", FullName = "Trần Quốc Tuấn", Status = "ACTIVE" });
        _db.Doctors.Add(new Doctor { Id = 50, StaffId = 60, HospitalId = 10, SpecialtyId = 20, DoctorCode = "DR50", ProfessionalTitle = "BS.CKI", IsActive = true });
        _db.Services.AddRange(
            new Service { Id = 70, HospitalId = 10, ServiceCode = "CLS-ENDO", Name = "Nội soi dạ dày", IsActive = true },
            new Service { Id = 71, HospitalId = 10, ServiceCode = "CLS-CBC", Name = "Tổng phân tích tế bào máu", IsActive = true });
        _db.Icd10Codes.Add(new Icd10Code { Id = 80, Code = "K29.0", NameVi = "Viêm dạ dày cấp xuất huyết", CatalogVersion = "ICD10-VN" });
        _db.Medicines.Add(new Medicine { Id = 90, HospitalId = 10, MedicineCode = "MED-OME", Name = "Omeprazol", Strength = "20mg", DosageForm = "Viên nang", DispensingUnit = "Viên", IsActive = true });

        await _db.SaveChangesAsync();
    }

    private async Task SeedCompletedEmrAsync(
        long recordId,
        long visitId,
        string visitCode,
        long patientId,
        long hospitalId,
        long specialtyId,
        DateTime completedAt,
        long roomId = 40,
        long doctorId = 50)
    {
        _db.Visits.Add(new Visit
        {
            Id = visitId,
            VisitCode = visitCode,
            PatientId = patientId,
            ExaminationRoomId = roomId,
            AssignedDoctorId = doctorId,
            CompletedByDoctorId = doctorId,
            ReasonForVisit = "Đau thượng vị",
            Status = "COMPLETED",
            CheckedInAt = completedAt.AddHours(-1),
            CompletedAt = completedAt,
        });
        _db.MedicalRecords.Add(new MedicalRecord
        {
            Id = recordId,
            VisitId = visitId,
            CreatedByDoctorId = doctorId,
            Status = "FINALIZED",
            FinalizedAt = completedAt,
        });
        _db.MedicalRecordRevisions.AddRange(
            new MedicalRecordRevision
            {
                Id = recordId * 10 + 1,
                MedicalRecordId = recordId,
                RevisionNo = 1,
                PresentingComplaint = "Đau thượng vị âm ỉ 3 ngày",
                SymptomHistory = "Đau tăng sau ăn cay",
                RelevantHistoryAllergies = "Không dị ứng thuốc",
                VitalSigns = """{"bp_systolic_mmhg":120,"bp_diastolic_mmhg":80,"pulse_bpm":74,"temperature_c":36.7,"spo2_percent":98,"height_cm":175,"weight_kg":70}""",
                ClinicalFindings = "Bụng mềm, ấn tức thượng vị",
                ClinicalConclusion = "Viêm dạ dày cấp ổn định, tái khám sau 2 tuần.",
                EditedByDoctorId = doctorId,
                RecordedAt = completedAt,
                IsConfirmed = true,
            },
            new MedicalRecordRevision
            {
                Id = recordId * 10 + 2,
                MedicalRecordId = recordId,
                RevisionNo = 2,
                ClinicalConclusion = "NHÁP CHƯA XÁC NHẬN KHÔNG ĐƯỢC LỘ",
                EditedByDoctorId = doctorId,
                RecordedAt = completedAt.AddMinutes(5),
                IsConfirmed = false,
            });

        _db.VisitDiagnoses.Add(new VisitDiagnosis
        {
            Id = visitId * 10 + 1,
            VisitId = visitId,
            Icd10CodeId = 80,
            CodeSnapshot = "K29.0",
            NameSnapshot = "Viêm dạ dày cấp",
            ClinicalDiagnosis = "Viêm niêm mạc hang vị cấp",
            IsPrimary = true,
            Status = "CONFIRMED",
            ConfirmedAt = completedAt,
            RecordedByDoctorId = doctorId,
        });

        var pubOrderId = visitId * 10 + 1;
        var pendOrderId = visitId * 10 + 2;
        _db.DiagnosticOrders.AddRange(
            new DiagnosticOrder
            {
                Id = pubOrderId,
                OrderCode = $"ORD-PUB-{visitId}",
                VisitId = visitId,
                ServiceId = 70,
                OrderingDoctorId = doctorId,
                Priority = "NORMAL",
                Status = "COMPLETED",
                OrderedAt = completedAt.AddMinutes(-40),
            },
            new DiagnosticOrder
            {
                Id = pendOrderId,
                OrderCode = $"ORD-PEND-{visitId}",
                VisitId = visitId,
                ServiceId = 71,
                OrderingDoctorId = doctorId,
                Priority = "URGENT",
                Status = "IN_PROGRESS",
                OrderedAt = completedAt.AddMinutes(-35),
            });

        _db.DiagnosticResults.AddRange(
            new DiagnosticResult
            {
                Id = pubOrderId,
                DiagnosticOrderId = pubOrderId,
                Status = "FINALIZED",
                FinalizedAt = completedAt.AddMinutes(-15),
            },
            new DiagnosticResult
            {
                Id = pendOrderId,
                DiagnosticOrderId = pendOrderId,
                Status = "PENDING_VERIFY",
                FinalizedAt = null,
            });

        _db.DiagnosticResultVersions.AddRange(
            new DiagnosticResultVersion
            {
                Id = pubOrderId,
                DiagnosticResultId = pubOrderId,
                VersionNo = 1,
                ResultText = "Niêm mạc hang vị xung huyết.",
                Conclusion = "Viêm trợt hang vị nhẹ.",
                ReportFileUrl = "https://files.medflow.test/cls/endo.pdf",
                IsFinalized = true,
                VerifiedAt = completedAt.AddMinutes(-15),
            },
            new DiagnosticResultVersion
            {
                Id = pendOrderId,
                DiagnosticResultId = pendOrderId,
                VersionNo = 1,
                ResultText = "BÍ MẬT CHƯA DUYỆT",
                Conclusion = "KẾT LUẬN NHÁP CHƯA CÔNG BỐ",
                IsFinalized = false,
            });

        var oldRxId = visitId * 10 + 1;
        var finalRxId = visitId * 10 + 2;
        var draftRxId = visitId * 10 + 3;
        _db.Prescriptions.AddRange(
            new Prescription
            {
                Id = oldRxId,
                PrescriptionCode = $"RX-OLD-{visitId}",
                VisitId = visitId,
                PrescribingDoctorId = doctorId,
                Status = "ISSUED",
                IssuedAt = completedAt.AddMinutes(-10),
            },
            new Prescription
            {
                Id = finalRxId,
                PrescriptionCode = $"RX-FINAL-{visitId}",
                VisitId = visitId,
                PrescribingDoctorId = doctorId,
                Status = "ISSUED",
                ReplacesPrescriptionId = oldRxId,
                IssuedAt = completedAt,
            },
            new Prescription
            {
                Id = draftRxId,
                PrescriptionCode = $"RX-DRAFT-{visitId}",
                VisitId = visitId,
                PrescribingDoctorId = doctorId,
                Status = "DRAFT",
            });

        _db.PrescriptionItems.AddRange(
            new PrescriptionItem
            {
                Id = oldRxId,
                PrescriptionId = oldRxId,
                MedicineId = 90,
                PrescribedQuantity = 10,
            },
            new PrescriptionItem
            {
                Id = finalRxId,
                PrescriptionId = finalRxId,
                MedicineId = 90,
                PrescribedQuantity = 14,
                DoseScheduleText = "Sáng 1 viên, Tối 1 viên",
                Frequency = "2 lần/ngày",
                DurationDays = 7,
                AdministrationRoute = "Uống",
                Instructions = "Uống trước ăn 30 phút",
            },
            new PrescriptionItem
            {
                Id = draftRxId,
                PrescriptionId = draftRxId,
                MedicineId = 90,
                PrescribedQuantity = 5,
            });

        await _db.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();
}
