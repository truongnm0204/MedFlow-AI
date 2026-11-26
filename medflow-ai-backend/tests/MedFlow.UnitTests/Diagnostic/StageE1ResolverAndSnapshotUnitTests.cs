using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Consultation;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Diagnostic;

/// <summary>
/// Stage E1 (PR #857 Review Follow-up) fail-closed unit verification for:
/// - E1-01: <see cref="DiagnosticServicePointResolver"/> (DR-01..DR-11)
/// - E1-03: <see cref="OverdueReturnSnapshotHelper"/> v2 digest sensitivity &amp; fail-closed DB error handling (SN-08..SN-13, SN-15).
/// </summary>
public sealed class StageE1ResolverAndSnapshotUnitTests
{
    private const long Hospital1Id = 10;
    private const long Hospital2Id = 20;
    private const long LabDeptId = 100;
    private const long ImgDeptId = 200;

    [Fact]
    public void Dr01_Dr02_ExactServicePoint_AndSharedSameDepartmentPoint_AreAccepted()
    {
        var (depts, rooms, labSvc, _) = BuildBaseCatalog();
        var exactLabSp = new QueueServicePoint
        {
            Id = 501,
            HospitalId = Hospital1Id,
            RoomId = 1001,
            ServiceId = labSvc.Id,
            PointType = "DIAGNOSTIC",
            Name = "PXN Sinh hóa",
            IsActive = true,
        };
        var sharedLabSp = new QueueServicePoint
        {
            Id = 502,
            HospitalId = Hospital1Id,
            RoomId = 1001,
            ServiceId = null,
            PointType = "DIAGNOSTIC",
            Name = "PXN Dùng chung Khoa XN",
            IsActive = true,
        };
        var order = new DiagnosticOrder { Id = 9001, OrderCode = "DIA-9001", ServiceId = labSvc.Id, Status = "ORDERED" };

        // DR-01: Exact ServiceId, active room & department
        var resolvedExact = DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
            order,
            labSvc,
            requestedSpId: null,
            [exactLabSp, sharedLabSp],
            rooms,
            visitQueues: null,
            Hospital1Id,
            depts);
        Assert.Equal(501, resolvedExact);

        // DR-02: Shared point (ServiceId == null) in the same active department
        var resolvedShared = DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
            order,
            labSvc,
            requestedSpId: null,
            [sharedLabSp],
            rooms,
            visitQueues: null,
            Hospital1Id,
            depts);
        Assert.Equal(502, resolvedShared);
    }

    [Fact]
    public void Dr03_Dr04_Dr05_SharedAndExactPoints_FailClosedOnCrossDeptOrNullDepartmentsOrInactiveDept()
    {
        var (depts, rooms, labSvc, _) = BuildBaseCatalog();
        var order = new DiagnosticOrder { Id = 9002, OrderCode = "DIA-9002", ServiceId = labSvc.Id, Status = "ORDERED" };

        // DR-03: Shared point in Imaging department (ImgDeptId != LabDeptId) -> REJECT
        var sharedImgSp = new QueueServicePoint
        {
            Id = 503,
            HospitalId = Hospital1Id,
            RoomId = 2001, // ImgDeptId
            ServiceId = null,
            PointType = "DIAGNOSTIC",
            Name = "Điểm CĐHA dùng chung",
            IsActive = true,
        };
        var dr03Ex = Assert.Throws<ConflictException>(() =>
            DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
                order, labSvc, null, [sharedImgSp], rooms, null, Hospital1Id, depts));
        Assert.Contains(DiagnosticServicePointResolver.NoActiveCompatiblePointCode, dr03Ex.Message, StringComparison.Ordinal);

        // DR-04: Room missing DepartmentId == null -> REJECT (never wildcard!)
        var roomNoDept = new HospitalRoom
        {
            Id = 1099,
            HospitalId = Hospital1Id,
            DepartmentId = null,
            RoomCode = "RM-NODEPT",
            RoomType = "DIAGNOSTIC",
            Status = "ACTIVE",
        };
        var roomsWithNullDept = new Dictionary<long, HospitalRoom>(rooms) { [1099] = roomNoDept };
        var spOnNullDeptRoom = new QueueServicePoint
        {
            Id = 504,
            HospitalId = Hospital1Id,
            RoomId = 1099,
            ServiceId = null,
            PointType = "DIAGNOSTIC",
            IsActive = true,
        };
        Assert.False(DiagnosticServicePointResolver.IsOperational(spOnNullDeptRoom, roomsWithNullDept, depts, Hospital1Id));
        var dr04Ex = Assert.Throws<ConflictException>(() =>
            DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
                order, labSvc, null, [spOnNullDeptRoom], roomsWithNullDept, null, Hospital1Id, depts));
        Assert.Contains(DiagnosticServicePointResolver.NoActiveCompatiblePointCode, dr04Ex.Message, StringComparison.Ordinal);

        // DR-04b: Inactive Department -> REJECT even on exact ServiceId!
        var inactiveDepts = new Dictionary<long, Department>(depts)
        {
            [LabDeptId] = new Department { Id = LabDeptId, HospitalId = Hospital1Id, DepartmentCode = "LAB", Name = "XN", IsActive = false },
        };
        var exactSp = new QueueServicePoint
        {
            Id = 505,
            HospitalId = Hospital1Id,
            RoomId = 1001,
            ServiceId = labSvc.Id,
            PointType = "DIAGNOSTIC",
            IsActive = true,
        };
        Assert.False(DiagnosticServicePointResolver.IsOperational(exactSp, rooms, inactiveDepts, Hospital1Id));

        // DR-05: Service missing DepartmentId == null -> REJECT (never wildcard!)
        var svcNoDept = new Service
        {
            Id = labSvc.Id,
            HospitalId = Hospital1Id,
            DepartmentId = null,
            ServiceCode = "LAB-NODEPT",
            Name = "XN thiếu khoa",
            ServiceType = "DIAGNOSTIC",
            IsActive = true,
        };
        var sharedSameRoomSp = new QueueServicePoint
        {
            Id = 506,
            HospitalId = Hospital1Id,
            RoomId = 1001,
            ServiceId = null,
            PointType = "DIAGNOSTIC",
            IsActive = true,
        };
        Assert.False(DiagnosticServicePointResolver.IsCompatibleForOrder(sharedSameRoomSp, rooms, svcNoDept.Id, svcNoDept, depts, Hospital1Id));
        var dr05Ex = Assert.Throws<ConflictException>(() =>
            DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
                order, svcNoDept, null, [sharedSameRoomSp, exactSp], rooms, null, Hospital1Id, depts));
        Assert.Contains(DiagnosticServicePointResolver.NoActiveCompatiblePointCode, dr05Ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dr06_Dr07_Dr08_Dr09_Dr10_Dr11_Roomless_NullIsActive_Maintenance_CrossHospital_ExplicitNoFallback_AndStaleTicketReroute()
    {
        var (depts, rooms, labSvc, imgSvc) = BuildBaseCatalog();
        var order = new DiagnosticOrder { Id = 9003, OrderCode = "DIA-9003", ServiceId = labSvc.Id, Status = "ORDERED" };

        // DR-06: RoomId == null or missing room -> REJECT
        var roomlessSp = new QueueServicePoint
        {
            Id = 601,
            HospitalId = Hospital1Id,
            RoomId = null,
            ServiceId = labSvc.Id,
            PointType = "DIAGNOSTIC",
            IsActive = true,
        };
        var brokenRoomSp = new QueueServicePoint
        {
            Id = 602,
            HospitalId = Hospital1Id,
            RoomId = 99999,
            ServiceId = labSvc.Id,
            PointType = "DIAGNOSTIC",
            IsActive = true,
        };
        Assert.False(DiagnosticServicePointResolver.IsOperational(roomlessSp, rooms, depts, Hospital1Id));
        Assert.False(DiagnosticServicePointResolver.IsOperational(brokenRoomSp, rooms, depts, Hospital1Id));

        // DR-07: sp.IsActive == false AND sp.IsActive == null -> REJECT
        var falseSp = new QueueServicePoint
        {
            Id = 603,
            HospitalId = Hospital1Id,
            RoomId = 1001,
            ServiceId = labSvc.Id,
            PointType = "DIAGNOSTIC",
            IsActive = false,
        };
        var nullActiveSp = new QueueServicePoint
        {
            Id = 604,
            HospitalId = Hospital1Id,
            RoomId = 1001,
            ServiceId = labSvc.Id,
            PointType = "DIAGNOSTIC",
            IsActive = null,
        };
        Assert.False(DiagnosticServicePointResolver.IsOperational(falseSp, rooms, depts, Hospital1Id));
        Assert.False(DiagnosticServicePointResolver.IsOperational(nullActiveSp, rooms, depts, Hospital1Id));

        // DR-08: Room Status == MAINTENANCE / INACTIVE -> REJECT even on exact ServiceId
        var maintRoom = new HospitalRoom
        {
            Id = 1002,
            HospitalId = Hospital1Id,
            DepartmentId = LabDeptId,
            RoomCode = "RM-MAINT",
            RoomType = "DIAGNOSTIC",
            Status = "MAINTENANCE",
        };
        var roomsWithMaint = new Dictionary<long, HospitalRoom>(rooms) { [1002] = maintRoom };
        var maintExactSp = new QueueServicePoint
        {
            Id = 605,
            HospitalId = Hospital1Id,
            RoomId = 1002,
            ServiceId = labSvc.Id,
            PointType = "DIAGNOSTIC",
            IsActive = true,
        };
        Assert.False(DiagnosticServicePointResolver.IsOperational(maintExactSp, roomsWithMaint, depts, Hospital1Id));

        // DR-09: Foreign hospital on SP, Room, or Service -> REJECT
        var foreignSp = new QueueServicePoint
        {
            Id = 606,
            HospitalId = Hospital2Id,
            RoomId = 1001,
            ServiceId = labSvc.Id,
            PointType = "DIAGNOSTIC",
            IsActive = true,
        };
        Assert.False(DiagnosticServicePointResolver.IsOperational(foreignSp, roomsWithMaint, depts, Hospital1Id));
        var foreignSvc = new Service
        {
            Id = labSvc.Id,
            HospitalId = Hospital2Id,
            DepartmentId = LabDeptId,
            ServiceCode = "LAB-H2",
            Name = "XN viện khác",
            ServiceType = "DIAGNOSTIC",
            IsActive = true,
        };
        var activeBackupLabSp = new QueueServicePoint
        {
            Id = 607,
            HospitalId = Hospital1Id,
            RoomId = 1001,
            ServiceId = labSvc.Id,
            PointType = "DIAGNOSTIC",
            IsActive = true,
        };
        Assert.False(DiagnosticServicePointResolver.IsCompatibleForOrder(activeBackupLabSp, roomsWithMaint, foreignSvc.Id, foreignSvc, depts, Hospital1Id));

        // DR-10: Explicit incompatible/inactive ServicePointId or RoomId throws 409 [INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT]
        //        EVEN WHEN activeBackupLabSp (607) is available — NEVER silently falls back!
        var activeImgSp = new QueueServicePoint
        {
            Id = 608,
            HospitalId = Hospital1Id,
            RoomId = 2001,
            ServiceId = imgSvc.Id,
            PointType = "DIAGNOSTIC",
            IsActive = true,
        };
        var dr10SpEx = Assert.Throws<ConflictException>(() =>
            DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
                order,
                labSvc,
                requestedSpId: activeImgSp.Id,
                [activeBackupLabSp, activeImgSp, maintExactSp],
                roomsWithMaint,
                visitQueues: null,
                Hospital1Id,
                depts));
        Assert.Contains(DiagnosticServicePointResolver.IncompatibleServicePointCode, dr10SpEx.Message, StringComparison.Ordinal);

        var dr10RoomEx = Assert.Throws<ConflictException>(() =>
            DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
                order,
                labSvc,
                requestedSpId: null,
                [activeBackupLabSp, activeImgSp, maintExactSp],
                roomsWithMaint,
                visitQueues: null,
                Hospital1Id,
                depts,
                requestedRoomId: maintRoom.Id));
        Assert.Contains(DiagnosticServicePointResolver.IncompatibleServicePointCode, dr10RoomEx.Message, StringComparison.Ordinal);

        // DR-11: Order's prior QueueEntry points to maintExactSp (605, now MAINTENANCE):
        //        - When activeBackupLabSp (607) exists -> reroutes cleanly to 607
        //        - When only activeImgSp (608, different service/dept) exists -> throws 409 [NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT]
        var orderWithStaleQe = new DiagnosticOrder
        {
            Id = 9004,
            OrderCode = "DIA-9004",
            ServiceId = labSvc.Id,
            QueueEntryId = 777,
            Status = "ORDERED",
        };
        var staleQe = new QueueEntry
        {
            Id = 777,
            ServicePointId = maintExactSp.Id,
            CurrentServicePointId = maintExactSp.Id,
            QueueType = "DIAGNOSTIC",
            Status = "DEFERRED",
        };

        var reroutedSpId = DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
            orderWithStaleQe,
            labSvc,
            requestedSpId: null,
            [maintExactSp, activeBackupLabSp, activeImgSp],
            roomsWithMaint,
            [staleQe],
            Hospital1Id,
            depts);
        Assert.Equal(activeBackupLabSp.Id, reroutedSpId);

        var dr11BlockedEx = Assert.Throws<ConflictException>(() =>
            DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
                orderWithStaleQe,
                labSvc,
                requestedSpId: null,
                [maintExactSp, activeImgSp],
                roomsWithMaint,
                [staleQe],
                Hospital1Id,
                depts));
        Assert.Contains(DiagnosticServicePointResolver.NoActiveCompatiblePointCode, dr11BlockedEx.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sn08_To_Sn13_ComputeSnapshotHash_V2_DetectsContentTextConclusionFileVerifyAndOrderSetMutations()
    {
        var visit = new Visit { Id = 500, AssignedDoctorId = 11 };
        var mandatoryOrder = new DiagnosticOrder
        {
            Id = 1001,
            VisitId = 500,
            Status = "COMPLETED",
            SelectionStatus = "ACCEPTED",
            RequiredForConclusion = true,
        };
        var result = new DiagnosticResult
        {
            Id = 2001,
            DiagnosticOrderId = 1001,
            Status = "PENDING_VERIFY",
        };
        var v1 = new DiagnosticResultVersion
        {
            Id = 3001,
            DiagnosticResultId = 2001,
            VersionNo = 1,
            ResultText = "GLU 5.2 mmol/L",
            Conclusion = "Đường huyết bình thường",
            ReportFileUrl = "/storage/diagnostic-reports/glu-v1.pdf",
            IsFinalized = false,
            VerifiedByStaffId = null,
        };

        var baseHash = OverdueReturnSnapshotHelper.ComputeSnapshotHash(visit, [mandatoryOrder], [result], [v1]);
        Assert.True(OverdueReturnSnapshotHelper.IsValidSnapshotHex(baseHash));

        // SN-08: ResultText edited (even if VersionNo/Id unchanged!)
        var v1EditedText = new DiagnosticResultVersion
        {
            Id = 3001,
            DiagnosticResultId = 2001,
            VersionNo = 1,
            ResultText = "GLU 11.8 mmol/L",
            Conclusion = v1.Conclusion,
            ReportFileUrl = v1.ReportFileUrl,
            IsFinalized = false,
        };
        var textHash = OverdueReturnSnapshotHelper.ComputeSnapshotHash(visit, [mandatoryOrder], [result], [v1EditedText]);
        Assert.NotEqual(baseHash, textHash);

        // SN-09: Conclusion edited
        var v1EditedConclusion = new DiagnosticResultVersion
        {
            Id = 3001,
            DiagnosticResultId = 2001,
            VersionNo = 1,
            ResultText = v1.ResultText,
            Conclusion = "Tăng đường huyết lúc đói",
            ReportFileUrl = v1.ReportFileUrl,
            IsFinalized = false,
        };
        var conclusionHash = OverdueReturnSnapshotHelper.ComputeSnapshotHash(visit, [mandatoryOrder], [result], [v1EditedConclusion]);
        Assert.NotEqual(baseHash, conclusionHash);

        // SN-10: ReportFileUrl edited
        var v1EditedFile = new DiagnosticResultVersion
        {
            Id = 3001,
            DiagnosticResultId = 2001,
            VersionNo = 1,
            ResultText = v1.ResultText,
            Conclusion = v1.Conclusion,
            ReportFileUrl = "/storage/diagnostic-reports/glu-v2-corrected.pdf",
            IsFinalized = false,
        };
        var fileHash = OverdueReturnSnapshotHelper.ComputeSnapshotHash(visit, [mandatoryOrder], [result], [v1EditedFile]);
        Assert.NotEqual(baseHash, fileHash);

        // SN-11: PENDING_VERIFY -> VERIFIED (two-person verification step)
        var verifiedResult = new DiagnosticResult
        {
            Id = 2001,
            DiagnosticOrderId = 1001,
            Status = "VERIFIED",
            FinalizedAt = new DateTime(2026, 11, 20, 10, 0, 0),
        };
        var v2Verified = new DiagnosticResultVersion
        {
            Id = 3002,
            DiagnosticResultId = 2001,
            VersionNo = 2,
            ResultText = v1.ResultText,
            Conclusion = v1.Conclusion,
            ReportFileUrl = v1.ReportFileUrl,
            IsFinalized = true,
            VerifiedByStaffId = 88,
        };
        var verifiedHash = OverdueReturnSnapshotHelper.ComputeSnapshotHash(visit, [mandatoryOrder], [verifiedResult], [v1, v2Verified]);
        Assert.NotEqual(baseHash, verifiedHash);

        // SN-12 & SN-13: Adding an optional order -> changes hash; declining/cancelling it -> changes hash again
        var optOrder = new DiagnosticOrder
        {
            Id = 1002,
            VisitId = 500,
            Status = "ORDERED",
            SelectionStatus = "UNDECIDED",
            RequiredForConclusion = false,
        };
        var withOptHash = OverdueReturnSnapshotHelper.ComputeSnapshotHash(visit, [mandatoryOrder, optOrder], [verifiedResult], [v2Verified]);
        Assert.NotEqual(verifiedHash, withOptHash);

        var optDeclined = new DiagnosticOrder
        {
            Id = 1002,
            VisitId = 500,
            Status = "ORDERED",
            SelectionStatus = "DECLINED",
            RequiredForConclusion = false,
        };
        var withOptDeclinedHash = OverdueReturnSnapshotHelper.ComputeSnapshotHash(visit, [mandatoryOrder, optDeclined], [verifiedResult], [v2Verified]);
        Assert.NotEqual(withOptHash, withOptDeclinedHash);

        var optCancelled = new DiagnosticOrder
        {
            Id = 1002,
            VisitId = 500,
            Status = "CANCELLED",
            SelectionStatus = "DECLINED",
            RequiredForConclusion = false,
        };
        var withOptCancelledHash = OverdueReturnSnapshotHelper.ComputeSnapshotHash(visit, [mandatoryOrder, optCancelled], [verifiedResult], [v2Verified]);
        Assert.NotEqual(withOptDeclinedHash, withOptCancelledHash);
    }

    [Fact]
    public async Task Sn15_ComputeSnapshotHashAsync_WhenDiagnosticDbSetsThrowNotSupported_FailsClosedInsteadOfReturningEmptyDigest()
    {
        var visit = new Visit { Id = 999, AssignedDoctorId = 7 };
        var brokenDb = new BrokenDiagnosticDbContext();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            OverdueReturnSnapshotHelper.ComputeSnapshotHashAsync(brokenDb, visit, CancellationToken.None));
    }

    private static (
        Dictionary<long, Department> Depts,
        Dictionary<long, HospitalRoom> Rooms,
        Service LabService,
        Service ImgService) BuildBaseCatalog()
    {
        var depts = new Dictionary<long, Department>
        {
            [LabDeptId] = new() { Id = LabDeptId, HospitalId = Hospital1Id, DepartmentCode = "LAB", Name = "Khoa Xét nghiệm", IsActive = true },
            [ImgDeptId] = new() { Id = ImgDeptId, HospitalId = Hospital1Id, DepartmentCode = "IMG", Name = "Khoa CĐHA", IsActive = true },
        };

        var rooms = new Dictionary<long, HospitalRoom>
        {
            [1001] = new() { Id = 1001, HospitalId = Hospital1Id, DepartmentId = LabDeptId, RoomCode = "LAB-101", RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
            [2001] = new() { Id = 2001, HospitalId = Hospital1Id, DepartmentId = ImgDeptId, RoomCode = "IMG-201", RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
        };

        var labSvc = new Service
        {
            Id = 50,
            HospitalId = Hospital1Id,
            DepartmentId = LabDeptId,
            ServiceCode = "LAB-CBC",
            Name = "Công thức máu",
            ServiceType = "DIAGNOSTIC",
            IsActive = true,
        };

        var imgSvc = new Service
        {
            Id = 60,
            HospitalId = Hospital1Id,
            DepartmentId = ImgDeptId,
            ServiceCode = "IMG-US",
            Name = "Siêu âm bụng",
            ServiceType = "DIAGNOSTIC",
            IsActive = true,
        };

        return (depts, rooms, labSvc, imgSvc);
    }

    private sealed class BrokenDiagnosticDbContext : IApplicationDbContext
    {
        private static NotSupportedException Fail() => new("Simulated broken diagnostic query provider (SN-15).");

        public DbSet<User> Users => throw Fail();
        public DbSet<Role> Roles => throw Fail();
        public DbSet<Permission> Permissions => throw Fail();
        public DbSet<UserRole> UserRoles => throw Fail();
        public DbSet<RolePermission> RolePermissions => throw Fail();
        public DbSet<Patient> Patients => throw Fail();
        public DbSet<Staff> Staff => throw Fail();
        public DbSet<Doctor> Doctors => throw Fail();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw Fail();
        public DbSet<ExaminationSlot> ExaminationSlots => throw Fail();
        public DbSet<AuthSession> AuthSessions => throw Fail();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw Fail();
        public DbSet<AuditLog> AuditLogs => throw Fail();
        public DbSet<Hospital> Hospitals => throw Fail();
        public DbSet<Department> Departments => throw Fail();
        public DbSet<HospitalRoom> HospitalRooms => throw Fail();
        public DbSet<Specialty> Specialties => throw Fail();
        public DbSet<Service> Services => throw Fail();
        public DbSet<ServicePrice> ServicePrices => throw Fail();
        public DbSet<HospitalPolicy> HospitalPolicies => throw Fail();
        public DbSet<Appointment> Appointments => throw Fail();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw Fail();
        public DbSet<Visit> Visits => throw Fail();
        public DbSet<VisitCharge> VisitCharges => throw Fail();
        public DbSet<QueueEntry> QueueEntries => throw Fail();
        public DbSet<Payment> Payments => throw Fail();
        public DbSet<Medicine> Medicines => throw Fail();
        public DbSet<Prescription> Prescriptions => throw Fail();
        public DbSet<PrescriptionItem> PrescriptionItems => throw Fail();
        public DbSet<QueueServicePoint> QueueServicePoints => throw Fail();
        public DbSet<QueueFairnessState> QueueFairnessStates => throw Fail();
        public DbSet<MedicalRecord> MedicalRecords => throw Fail();
        public DbSet<MedicalRecordRevision> MedicalRecordRevisions => throw Fail();
        public DbSet<VisitDiagnosis> VisitDiagnoses => throw Fail();
        public DbSet<Icd10Code> Icd10Codes => throw Fail();
        public DbSet<DiagnosticOrder> DiagnosticOrders => throw Fail();
        public DbSet<DiagnosticResult> DiagnosticResults => throw Fail();
        public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => throw Fail();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw Fail();
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) => throw Fail();
    }
}
