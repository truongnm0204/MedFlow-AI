using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Patients;

public sealed class PatientDiagnosticResultServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class ResultTestDbContext : DbContext, IApplicationDbContext
    {
        public ResultTestDbContext(DbContextOptions<ResultTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
        public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => Set<DiagnosticResultVersion>();

        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
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
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User)
                    .WithOne(u => u.Patient)
                    .HasForeignKey<Patient>(p => p.UserId);
            });
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(p => p.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(o => o.Id);
            modelBuilder.Entity<DiagnosticResult>().HasKey(r => r.Id);
            modelBuilder.Entity<DiagnosticResultVersion>().HasKey(v => v.Id);
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
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong PatientDiagnosticResultServiceTests.");
    }

    private static readonly DateTime FixedNowUtc = new(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc);

    private readonly ResultTestDbContext _db;
    private readonly PatientDiagnosticResultService _sut;

    public PatientDiagnosticResultServiceTests()
    {
        var options = new DbContextOptionsBuilder<ResultTestDbContext>()
            .UseInMemoryDatabase($"patient-diagnostic-result-{Guid.NewGuid():N}")
            .Options;
        _db = new ResultTestDbContext(options);
        _sut = new PatientDiagnosticResultService(_db, new FixedTimeProvider(new DateTimeOffset(FixedNowUtc)));
    }

    [Fact]
    public async Task GetMyResultAsync_VerifiedResult_ReturnsPublishedContentAndViewerLink()
    {
        await SeedActorAsync();
        var signedAt = new DateTime(2026, 4, 15, 10, 22, 0);
        await SeedOrderAsync(
            patientId: 1,
            orderId: 100,
            orderCode: "ORD-2026-0142",
            orderStatus: "COMPLETED",
            resultStatus: "VERIFIED",
            finalizedAt: signedAt,
            versions:
            [
                Version(1, "Niêm mạc hang vị trợt nông.", "Viêm hang vị mức độ nhẹ.", "https://files.medflow.test/reports/KQ-CLS-1.pdf", true, signedAt),
            ],
            includeDisplay: true);

        var detail = await _sut.GetMyResultAsync(10, "ord-2026-0142");

        Assert.True(detail.IsPublishedToPatient);
        Assert.Equal("PUBLISHED", detail.Status);
        Assert.Equal("Niêm mạc hang vị trợt nông.", detail.ResultText);
        Assert.Equal("Viêm hang vị mức độ nhẹ.", detail.Conclusion);
        Assert.Equal("https://files.medflow.test/reports/KQ-CLS-1.pdf", detail.ReportFileUrl);
        Assert.Equal("KQ-CLS-1.pdf", detail.FileName);
        Assert.Equal("Bệnh viện MedFlow", detail.HospitalName);
        Assert.Equal("Nội soi dạ dày", detail.ServiceName);
        Assert.Equal("BS.CKI Trần Quốc Tuấn", detail.OrderingDoctorName);
        Assert.Equal("Hoàng Minh Hải", detail.PerformingStaffName);
        Assert.Equal("Buồng Nội soi 102", detail.PerformedPlaceName);
        Assert.Equal("Nguyễn Văn An", detail.PatientName);
        Assert.Equal(1992, detail.BirthYear);
        Assert.Equal(signedAt, detail.SignedAt);
        Assert.Equal("Bản thân", detail.RelationshipLabel);
    }

    [Fact]
    public async Task GetMyResultAsync_PendingVerify_HidesClinicalContent()
    {
        await SeedActorAsync();
        await SeedOrderAsync(
            patientId: 1,
            orderId: 101,
            orderCode: "ORD-PENDING",
            orderStatus: "IN_PROGRESS",
            resultStatus: "PENDING_VERIFY",
            finalizedAt: null,
            versions:
            [
                Version(1, "SECRET-TEXT", "SECRET-CONCLUSION", "https://files.medflow.test/reports/secret.pdf", false, null),
            ]);

        var detail = await _sut.GetMyResultAsync(10, "ORD-PENDING");

        Assert.False(detail.IsPublishedToPatient);
        Assert.Equal("PENDING_RELEASE", detail.Status);
        Assert.Null(detail.ResultText);
        Assert.Null(detail.Conclusion);
        Assert.Null(detail.ReportFileUrl);
        Assert.Null(detail.FileName);
        Assert.Null(detail.SignedAt);
        Assert.Null(detail.PerformingStaffName);
    }

    [Fact]
    public async Task GetMyResultAsync_NewerDraftAfterFinalizedVersion_KeepsPublishedVersion()
    {
        await SeedActorAsync();
        var signedAt = new DateTime(2026, 4, 15, 10, 22, 0);
        await SeedOrderAsync(
            patientId: 1,
            orderId: 102,
            orderCode: "ORD-AMEND",
            orderStatus: "COMPLETED",
            resultStatus: "FINALIZED",
            finalizedAt: signedAt,
            versions:
            [
                Version(1, "PUBLISHED-TEXT", "PUBLISHED-CONCLUSION", "https://files.medflow.test/reports/public.pdf", true, signedAt),
                Version(2, "DRAFT-SECRET", "DRAFT-CONCLUSION", "https://files.medflow.test/reports/secret.pdf", false, null),
            ]);

        var detail = await _sut.GetMyResultAsync(10, "ORD-AMEND");

        Assert.True(detail.IsPublishedToPatient);
        Assert.Equal("PUBLISHED-TEXT", detail.ResultText);
        Assert.Equal("PUBLISHED-CONCLUSION", detail.Conclusion);
        Assert.Equal("https://files.medflow.test/reports/public.pdf", detail.ReportFileUrl);
        Assert.Equal("public.pdf", detail.FileName);
    }

    [Fact]
    public async Task GetMyResultAsync_OrderBelongsToAnotherPatient_ThrowsForbidden()
    {
        await SeedActorAsync();
        _db.Patients.Add(new Patient
        {
            Id = 9,
            PatientCode = "PAT-9",
            FullName = "Người khác",
            Status = "ACTIVE",
        });
        await _db.SaveChangesAsync();
        await SeedOrderAsync(
            patientId: 9,
            orderId: 109,
            orderCode: "ORD-OTHER",
            orderStatus: "COMPLETED",
            resultStatus: "VERIFIED",
            finalizedAt: FixedNowUtc,
            versions: [Version(1, "LEAK", "LEAK", "https://files.medflow.test/reports/leak.pdf", true, FixedNowUtc)]);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetMyResultAsync(10, "ORD-OTHER"));
    }

    [Fact]
    public async Task GetMyResultAsync_UnknownOrderCode_ThrowsNotFound()
    {
        await SeedActorAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetMyResultAsync(10, "ORD-MISSING"));
    }

    [Fact]
    public async Task GetMyResultAsync_BlankOrOversizedIdentifier_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetMyResultAsync(10, "  #  "));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetMyResultAsync(10, new string('A', 41)));
    }

    [Fact]
    public async Task GetMyResultsAsync_PageSizeAboveMax_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetMyResultsAsync(10, new GetPatientDiagnosticResultsRequest(PageSize: 51)));
    }

    [Fact]
    public async Task GetMyResultAsync_InactivePatient_ThrowsUnauthorized()
    {
        await SeedActorAsync(patientStatus: "INACTIVE");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetMyResultAsync(10, "ORD-1"));
    }

    [Fact]
    public async Task GetMyResultAsync_VerifiedFamilyMember_ReturnsMemberResult()
    {
        await SeedActorAsync();
        await SeedFamilyMemberAsync(relationship: "CHILD");
        await SeedOrderAsync(
            patientId: 2,
            orderId: 120,
            orderCode: "ORD-CHILD",
            orderStatus: "COMPLETED",
            resultStatus: "VERIFIED",
            finalizedAt: FixedNowUtc,
            versions: [Version(1, "KQ con", "Bình thường", "https://files.medflow.test/reports/child.pdf", true, FixedNowUtc)]);

        var detail = await _sut.GetMyResultAsync(10, "ORD-CHILD");

        Assert.Equal("Con", detail.RelationshipLabel);
        Assert.Equal("Bé Na", detail.PatientName);
        Assert.Equal("KQ con", detail.ResultText);
    }

    [Fact]
    public async Task GetMyResultAsync_RevokedFamilyLink_ThrowsForbidden()
    {
        await SeedActorAsync();
        await SeedFamilyMemberAsync(relationship: "CHILD", revokedAt: FixedNowUtc);
        await SeedOrderAsync(
            patientId: 2,
            orderId: 121,
            orderCode: "ORD-REVOKED",
            orderStatus: "COMPLETED",
            resultStatus: "VERIFIED",
            finalizedAt: FixedNowUtc,
            versions: [Version(1, "KQ con", "Bình thường", null, true, FixedNowUtc)]);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetMyResultAsync(10, "ORD-REVOKED"));
    }

    [Fact]
    public async Task GetMyResultAsync_ExpiredFamilyLink_ThrowsForbidden()
    {
        await SeedActorAsync();
        await SeedFamilyMemberAsync(relationship: "CHILD", validUntil: new DateTime(2026, 10, 20, 8, 0, 0));
        await SeedOrderAsync(
            patientId: 2,
            orderId: 122,
            orderCode: "ORD-EXPIRED",
            orderStatus: "COMPLETED",
            resultStatus: "VERIFIED",
            finalizedAt: FixedNowUtc,
            versions: [Version(1, "KQ con", "Bình thường", null, true, FixedNowUtc)]);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetMyResultAsync(10, "ORD-EXPIRED"));
    }

    [Fact]
    public async Task GetMyResultAsync_CancelledOrder_IsHiddenFromDetailAndList()
    {
        await SeedActorAsync();
        await SeedOrderAsync(
            patientId: 1,
            orderId: 130,
            orderCode: "ORD-CANCELLED",
            orderStatus: "CANCELLED",
            resultStatus: "FINALIZED",
            finalizedAt: FixedNowUtc,
            versions: [Version(1, "Đã hủy", "Đã hủy", "https://files.medflow.test/reports/cancelled.pdf", true, FixedNowUtc)]);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetMyResultAsync(10, "ORD-CANCELLED"));
        var list = await _sut.GetMyResultsAsync(10, null);
        Assert.Equal(0, list.TotalCount);
    }

    [Fact]
    public async Task GetMyResultsAsync_PublishedAndPending_ListsBothWithoutPendingBody()
    {
        await SeedActorAsync();
        var signedAt = new DateTime(2026, 4, 15, 10, 22, 0);
        await SeedOrderAsync(
            patientId: 1,
            orderId: 140,
            orderCode: "ORD-READY",
            orderStatus: "COMPLETED",
            resultStatus: "VERIFIED",
            finalizedAt: signedAt,
            versions: [Version(1, "Đã ký", "Ổn", "https://files.medflow.test/reports/ready.pdf", true, signedAt)]);
        await SeedOrderAsync(
            patientId: 1,
            orderId: 141,
            orderCode: "ORD-WAIT",
            orderStatus: "IN_PROGRESS",
            resultStatus: "DRAFT",
            finalizedAt: null,
            versions: [Version(1, "Nháp", "Nháp", "https://files.medflow.test/reports/wait.pdf", false, null)]);

        var list = await _sut.GetMyResultsAsync(10, null);
        var pending = await _sut.GetMyResultAsync(10, "ORD-WAIT");

        Assert.Equal(2, list.TotalCount);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal("ORD-READY", list.Items[0].OrderCode);
        Assert.True(list.Items[0].IsPublishedToPatient);
        Assert.Equal("ORD-WAIT", list.Items[1].OrderCode);
        Assert.False(list.Items[1].IsPublishedToPatient);
        Assert.Null(list.Items[1].SignedAt);
        Assert.Null(pending.ReportFileUrl);
        Assert.Null(pending.ResultText);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedActorAsync(string patientStatus = "ACTIVE")
    {
        _db.Users.Add(new User
        {
            Id = 10,
            Email = "patient@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        });
        _db.Patients.Add(new Patient
        {
            Id = 1,
            UserId = 10,
            PatientCode = "PAT-1",
            FullName = "Nguyễn Văn An",
            Gender = "Nam",
            DateOfBirth = new DateOnly(1992, 4, 15),
            Status = patientStatus,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedFamilyMemberAsync(
        string relationship,
        DateTime? revokedAt = null,
        DateTime? validUntil = null)
    {
        _db.Patients.Add(new Patient
        {
            Id = 2,
            PatientCode = "PAT-2",
            FullName = "Bé Na",
            Gender = "Nữ",
            DateOfBirth = new DateOnly(2018, 1, 2),
            Status = "ACTIVE",
        });
        _db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = 1,
            MemberPatientId = 2,
            Relationship = relationship,
            VerificationStatus = "VERIFIED",
            VerificationMethod = "STAFF",
            RevokedAt = revokedAt,
            ValidUntil = validUntil,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedOrderAsync(
        long patientId,
        long orderId,
        string orderCode,
        string? orderStatus,
        string? resultStatus,
        DateTime? finalizedAt,
        IReadOnlyList<DiagnosticResultVersion> versions,
        bool includeDisplay = false)
    {
        long? serviceId = null;
        long? doctorId = null;
        long? queueId = null;
        if (includeDisplay)
        {
            _db.Hospitals.Add(new Hospital
            {
                Id = orderId,
                HospitalCode = $"H{orderId}",
                Name = "Bệnh viện MedFlow",
                IsActive = true,
            });
            _db.Services.Add(new Service
            {
                Id = orderId,
                HospitalId = orderId,
                ServiceCode = $"SVC{orderId}",
                Name = "Nội soi dạ dày",
                ServiceType = "DIAGNOSTIC",
                IsActive = true,
            });
            _db.Staff.AddRange(
                new Staff { Id = orderId, StaffCode = $"NV{orderId}", FullName = "Hoàng Minh Hải", Status = "ACTIVE" },
                new Staff { Id = orderId + 50, StaffCode = $"BS{orderId}", FullName = "Trần Quốc Tuấn", Status = "ACTIVE" });
            _db.Doctors.Add(new Doctor
            {
                Id = orderId,
                StaffId = orderId + 50,
                HospitalId = orderId,
                DoctorCode = $"DR{orderId}",
                ProfessionalTitle = "BS.CKI",
                IsActive = true,
            });
            _db.HospitalRooms.Add(new HospitalRoom
            {
                Id = orderId,
                HospitalId = orderId,
                RoomCode = $"R{orderId}",
                Name = "Phòng 102",
                RoomType = "DIAGNOSTIC",
                Status = "ACTIVE",
            });
            _db.QueueServicePoints.Add(new QueueServicePoint
            {
                Id = orderId,
                HospitalId = orderId,
                RoomId = orderId,
                Name = "Buồng Nội soi 102",
                IsActive = true,
            });
            _db.QueueEntries.Add(new QueueEntry
            {
                Id = orderId,
                VisitId = orderId,
                ServicePointId = orderId,
                Status = "COMPLETED",
            });
            serviceId = orderId;
            doctorId = orderId;
            queueId = orderId;
        }

        _db.Visits.Add(new Visit
        {
            Id = orderId,
            VisitCode = $"VIS-{orderId}",
            PatientId = patientId,
            Status = "IN_PROGRESS",
        });
        _db.DiagnosticOrders.Add(new DiagnosticOrder
        {
            Id = orderId,
            OrderCode = orderCode,
            VisitId = orderId,
            ServiceId = serviceId,
            OrderingDoctorId = doctorId,
            QueueEntryId = queueId,
            Status = orderStatus,
        });

        var resultId = orderId + 5000;
        _db.DiagnosticResults.Add(new DiagnosticResult
        {
            Id = resultId,
            DiagnosticOrderId = orderId,
            Status = resultStatus,
            FinalizedAt = finalizedAt,
        });
        foreach (var version in versions)
        {
            version.Id = resultId * 10 + (version.VersionNo ?? 0);
            version.DiagnosticResultId = resultId;
            if (includeDisplay && version.IsFinalized == true)
                version.VerifiedByStaffId = orderId;
            _db.DiagnosticResultVersions.Add(version);
        }

        await _db.SaveChangesAsync();
    }

    private static DiagnosticResultVersion Version(
        int versionNo,
        string resultText,
        string conclusion,
        string? reportFileUrl,
        bool isFinalized,
        DateTime? verifiedAt)
        => new()
        {
            Id = versionNo,
            VersionNo = versionNo,
            ResultText = resultText,
            Conclusion = conclusion,
            ReportFileUrl = reportFileUrl,
            IsFinalized = isFinalized,
            VerifiedAt = verifiedAt,
        };
}
