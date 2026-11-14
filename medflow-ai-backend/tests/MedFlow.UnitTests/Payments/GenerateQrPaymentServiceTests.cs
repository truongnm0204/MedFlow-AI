using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Payments;

public sealed class GenerateQrPaymentServiceTests : IDisposable
{
    private const long HospitalA = 1;
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private readonly QrTestDb _db;
    private readonly GenerateQrPaymentService _service;

    public GenerateQrPaymentServiceTests()
    {
        var options = new DbContextOptionsBuilder<QrTestDb>()
            .UseInMemoryDatabase($"QrPay_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new QrTestDb(options);
        _db.Hospitals.Add(new Hospital { Id = HospitalA, HospitalCode = "H-A", Name = "Bệnh viện A", IsActive = true });
        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Benh nhan" },
            new Role { Id = 5, Code = "DOCTOR", Name = "Bac si" });
        _db.SaveChanges();
        _service = new GenerateQrPaymentService(_db, new FixedTime(FixedNow));
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task CreateAsync_OwnedVisit_InsertsPendingAndOmitsAccountFromAudit()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(visit.Id, 150_000m, "REQUIRED");
        AddPolicy();

        var result = await _service.CreateAsync(actor, visit.Id.ToString(), "127.0.0.1");

        Assert.Equal(QrPaymentIssueKind.Created, result.IssueKind);
        Assert.Equal(150_000m, result.Amount);
        Assert.Equal("VND", result.Currency);
        Assert.Equal("PENDING", result.Status);
        Assert.Equal("970422", result.BankBin);
        Assert.Equal(result.PaymentCode, result.TransferContent);
        Assert.StartsWith("MF", result.PaymentCode, StringComparison.Ordinal);
        Assert.Contains(result.PaymentCode, result.QrPayload, StringComparison.Ordinal);
        Assert.Equal(1, _db.Payments.Count(p => p.VisitId == visit.Id));

        var audit = Assert.Single(_db.AuditLogs.Where(a => a.Action == "QR_PAYMENT_REQUESTED"));
        Assert.Equal(HospitalA, audit.HospitalId);
        Assert.DoesNotContain("accountNumber", audit.ChangeData, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0123456789", audit.ChangeData, StringComparison.Ordinal);
        Assert.DoesNotContain("qrPayload", audit.ChangeData, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(audit.ChangeData!);
        Assert.Equal(visit.Id, json.RootElement.GetProperty("visitId").GetInt64());
        Assert.Equal("CREATED", json.RootElement.GetProperty("issueKind").GetString());
    }

    [Fact]
    public async Task CreateAsync_SecondCall_ReusesWithoutSecondAudit()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(visit.Id, 80_000m, "REQUIRED");
        AddPolicy();

        var first = await _service.CreateAsync(actor, visit.VisitCode, null);
        var second = await _service.CreateAsync(actor, visit.Id.ToString(), null);

        Assert.Equal(QrPaymentIssueKind.Reused, second.IssueKind);
        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Equal(first.PaymentCode, second.PaymentCode);
        Assert.Equal(1, _db.Payments.Count(p => p.VisitId == visit.Id));
        Assert.Equal(1, _db.AuditLogs.Count(a => a.Action == "QR_PAYMENT_REQUESTED"));
    }

    [Fact]
    public async Task CreateAsync_WalkInPendingWithoutCode_AdoptsSameRow()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", hospitalFromAudit: true);
        AddWalkInAudit(visit.Id, 90_000m, "VIETQR");
        var pending = new Payment { Id = 44, VisitId = visit.Id, Amount = 90_000m, Status = "PENDING" };
        _db.Payments.Add(pending);
        _db.SaveChanges();
        AddPolicy();

        var result = await _service.CreateAsync(actor, visit.Id.ToString(), null);

        Assert.Equal(QrPaymentIssueKind.Adopted, result.IssueKind);
        Assert.Equal(44, result.PaymentId);
        Assert.Equal("MF44", result.PaymentCode);
        Assert.Equal(1, _db.Payments.Count(p => p.VisitId == visit.Id));
    }

    [Fact]
    public async Task CreateAsync_PendingAmountMismatch_LeavesRow()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(visit.Id, 10_000m, "REQUIRED");
        _db.Payments.Add(new Payment { Id = 7, VisitId = visit.Id, Amount = 1m, Status = "pending" });
        _db.SaveChanges();
        AddPolicy();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1000", null));

        Assert.Equal(GenerateQrPaymentService.AmountMismatchMessage, ex.Message);
        Assert.Equal(1m, _db.Payments.Single(p => p.Id == 7).Amount);
        Assert.Null(_db.Payments.Single(p => p.Id == 7).PaymentCode);
    }

    [Fact]
    public async Task CreateAsync_TwoPending_Conflicts()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(visit.Id, 10_000m, "REQUIRED");
        _db.Payments.AddRange(
            new Payment { Id = 1, VisitId = visit.Id, Amount = 10_000m, Status = "PENDING" },
            new Payment { Id = 2, VisitId = visit.Id, Amount = 10_000m, Status = "PENDING" });
        _db.SaveChanges();
        AddPolicy();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1000", null));

        Assert.Equal(GenerateQrPaymentService.MultipleOpenMessage, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_Cashier_Conflicts()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", hospitalFromAudit: true);
        AddWalkInAudit(visit.Id, 10_000m, "CASHIER");
        AddPolicy();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1000", null));

        Assert.Equal(GenerateQrPaymentService.CashierMessage, ex.Message);
        Assert.Empty(_db.Payments);
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("NO_SHOW")]
    public async Task CreateAsync_BlockedVisit_Conflicts(string status)
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, status);
        AddCharge(visit.Id, 10_000m, "REQUIRED");
        AddPolicy();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1000", null));

        Assert.Equal(GenerateQrPaymentService.VisitBlockedMessage, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_UnpricedOrInvalidOrMismatch_Undetermined()
    {
        var actor = AddPatient(10, 100);
        var unpriced = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(unpriced.Id, null, "REQUIRED");
        AddPolicy();
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1000", null));
        Assert.Equal(GenerateQrPaymentService.UndeterminedMessage, ex.Message);

        var invalid = AddVisit(1001, 100, "CHECKED_IN");
        AddCharge(invalid.Id, -5m, "REQUIRED");
        ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1001", null));
        Assert.Equal(GenerateQrPaymentService.UndeterminedMessage, ex.Message);

        var mismatch = AddVisit(1002, 100, "CHECKED_IN");
        AddCharge(mismatch.Id, 10_000m, "REQUIRED");
        _db.Payments.Add(new Payment { Id = 9, VisitId = mismatch.Id, Amount = 1m, Status = "PAID", PaymentCode = "OLD" });
        _db.SaveChanges();
        ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1002", null));
        Assert.Equal(GenerateQrPaymentService.UndeterminedMessage, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_WaivedOnly_NothingDue()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(visit.Id, 10_000m, "WAIVED");
        AddPolicy();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1000", null));

        Assert.Equal(GenerateQrPaymentService.NothingDueMessage, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_FractionalVnd_Conflicts()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(visit.Id, 10.5m, "REQUIRED");
        AddPolicy();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1000", null));

        Assert.Equal(GenerateQrPaymentService.NotIntegerMessage, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_OtherPatientOrRevokedFamily_NotFound()
    {
        var actor = AddPatient(10, 100);
        AddPatient(11, 200);
        var other = AddVisit(1000, 999, "CHECKED_IN");
        AddCharge(other.Id, 10_000m, "REQUIRED");
        AddPolicy();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync(actor, "1000", null));
        Assert.Equal(VisitPaymentAccess.VisitNotFoundMessage, ex.Message);

        _db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = 100,
            MemberPatientId = 200,
            VerificationStatus = "VERIFIED",
            VerificationMethod = "STAFF",
            RevokedAt = FixedNow.UtcDateTime,
        });
        var familyVisit = AddVisit(1001, 200, "CHECKED_IN");
        AddCharge(familyVisit.Id, 10_000m, "REQUIRED");
        ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync(actor, "1001", null));
        Assert.Equal(VisitPaymentAccess.VisitNotFoundMessage, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ActiveFamily_Creates()
    {
        var actor = AddPatient(10, 100);
        AddPatient(11, 200);
        _db.Patients.Find(200L)!.Status = "ACTIVE";
        _db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = 100,
            MemberPatientId = 200,
            VerificationStatus = "VERIFIED",
            VerificationMethod = "STAFF",
        });
        _db.SaveChanges();
        var visit = AddVisit(1001, 200, "CHECKED_IN");
        AddCharge(visit.Id, 20_000m, "REQUIRED");
        AddPolicy();

        var result = await _service.CreateAsync(actor, "1001", null);

        Assert.Equal(QrPaymentIssueKind.Created, result.IssueKind);
        Assert.Equal(20_000m, result.Amount);
    }

    [Fact]
    public async Task CreateAsync_LockedOrInactive_Unauthorized()
    {
        var locked = AddPatient(10, 100);
        _db.Users.Find(10L)!.LockedUntil = FixedNow.UtcDateTime.AddMinutes(5);
        _db.SaveChanges();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateAsync(locked, "1", null));

        var inactive = AddPatient(12, 300);
        _db.Users.Find(12L)!.AccountStatus = AccountStatus.INACTIVE;
        _db.SaveChanges();
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateAsync(inactive, "1", null));
        Assert.Equal(VisitPaymentAccess.InactiveActorMessage, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_Doctor_Forbidden()
    {
        _db.Users.Add(new User { Id = 8, Email = "doc@test.local", AccountStatus = AccountStatus.ACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = 8, RoleId = 5 });
        _db.SaveChanges();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateAsync(8, "1", null));

        Assert.Equal(GenerateQrPaymentService.ForbiddenMessage, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_MissingOrInvalidPolicy_Conflicts()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(visit.Id, 10_000m, "REQUIRED");

        var missing = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1000", null));
        Assert.Equal(VietQrAccountPolicyReader.MissingMessage, missing.Message);

        _db.HospitalPolicies.Add(new HospitalPolicy
        {
            Id = 2,
            HospitalId = HospitalA,
            PolicyType = PaymentPolicyKeys.PolicyType,
            PolicyKey = PaymentPolicyKeys.VietQrAccount,
            PolicyValue = """{"bin":"12","accountNumber":"0123456789","accountName":"X"}""",
            PolicyVersion = 1,
        });
        _db.SaveChanges();
        var invalid = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(actor, "1000", null));
        Assert.Equal(VietQrAccountPolicyReader.InvalidMessage, invalid.Message);
    }

    [Fact]
    public async Task CreateAsync_UniqueViolationOnce_RetriesAndCreatesOneRow()
    {
        var options = new DbContextOptionsBuilder<ThrowOnceQrDb>()
            .UseInMemoryDatabase($"QrPayThrow_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ThrowOnceQrDb(options);
        db.Hospitals.Add(new Hospital { Id = HospitalA, HospitalCode = "H-A", Name = "BV", IsActive = true });
        db.Roles.Add(new Role { Id = 1, Code = "PATIENT", Name = "Benh nhan" });
        db.Users.Add(new User { Id = 10, Email = "p@test.local", AccountStatus = AccountStatus.ACTIVE });
        db.UserRoles.Add(new UserRole { UserId = 10, RoleId = 1 });
        db.Patients.Add(new Patient { Id = 100, UserId = 10, PatientCode = "P-100", FullName = "BN", Status = "ACTIVE" });
        db.SaveChanges();
        var visit = new Visit { Id = 1000, VisitCode = "VIS-1000", PatientId = 100, Status = "CHECKED_IN", ExaminationRoomId = 6000 };
        db.HospitalRooms.Add(new HospitalRoom { Id = 6000, HospitalId = HospitalA, RoomCode = "R", Name = "P", RoomType = "EXAM", Status = "ACTIVE" });
        db.Visits.Add(visit);
        db.VisitCharges.Add(new VisitCharge { Id = 1, VisitId = 1000, UnitPrice = 10_000m, PaymentRequirement = "REQUIRED" });
        db.HospitalPolicies.Add(new HospitalPolicy
        {
            Id = 1,
            HospitalId = HospitalA,
            PolicyType = PaymentPolicyKeys.PolicyType,
            PolicyKey = PaymentPolicyKeys.VietQrAccount,
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN DEMO"}""",
            PolicyVersion = 1,
        });
        db.SaveChanges();
        db.ThrowOnNextSave = true;
        var service = new GenerateQrPaymentService(db, new FixedTime(FixedNow));

        var result = await service.CreateAsync(10, "1000", null);

        Assert.Equal(QrPaymentIssueKind.Created, result.IssueKind);
        Assert.Equal(1, db.Payments.Count(p => p.VisitId == 1000));
    }

    [Fact]
    public async Task CreateAsync_StalePending_ExpiresAndCreatesNew()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(visit.Id, 10_000m, "REQUIRED");
        _db.Payments.Add(new Payment
        {
            Id = 60,
            VisitId = visit.Id,
            Amount = 10_000m,
            Status = PaymentStatuses.Pending,
            CreatedAt = VietnamNow().AddMinutes(-(GenerateQrPaymentService.PendingExpiryMinutes + 5)),
        });
        _db.SaveChanges();
        AddPolicy();

        var result = await _service.CreateAsync(actor, "1000", null);

        Assert.Equal(QrPaymentIssueKind.Created, result.IssueKind);
        Assert.Equal(PaymentStatuses.Expired, _db.Payments.Single(p => p.Id == 60).Status);
        Assert.Equal(2, _db.Payments.Count(p => p.VisitId == visit.Id));
        Assert.Equal(PaymentStatuses.Pending, _db.Payments.Single(p => p.Id == result.PaymentId).Status);
    }

    [Fact]
    public async Task CreateAsync_PendingWithinWindow_Reused()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN");
        AddCharge(visit.Id, 10_000m, "REQUIRED");
        _db.Payments.Add(new Payment
        {
            Id = 61,
            VisitId = visit.Id,
            Amount = 10_000m,
            Status = PaymentStatuses.Pending,
            PaymentCode = "MF61",
            CreatedAt = VietnamNow().AddMinutes(-(GenerateQrPaymentService.PendingExpiryMinutes - 1)),
        });
        _db.SaveChanges();
        AddPolicy();

        var result = await _service.CreateAsync(actor, "1000", null);

        Assert.Equal(QrPaymentIssueKind.Reused, result.IssueKind);
        Assert.Equal(61, result.PaymentId);
        Assert.Equal(PaymentStatuses.Pending, _db.Payments.Single(p => p.Id == 61).Status);
    }

    [Fact]
    public void UniqueViolation_DetectsSqlState()
    {
        Assert.True(PostgresUniqueViolation.Is(new DbUpdateException("dup", new SqlStateException("23505"))));
        Assert.False(PostgresUniqueViolation.Is(new DbUpdateException("other", new SqlStateException("23503"))));
    }

    private static DateTime VietnamNow()
        => MedFlow.Application.Services.Appointments.AppointmentBookingRules.GetVietnamNow(new FixedTime(FixedNow));

    private long AddPatient(long userId, long patientId)
    {
        _db.Users.Add(new User { Id = userId, Email = $"p{userId}@test.local", AccountStatus = AccountStatus.ACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = 1 });
        _db.Patients.Add(new Patient
        {
            Id = patientId,
            UserId = userId,
            PatientCode = $"P-{patientId}",
            FullName = "Benh nhan",
            Status = "ACTIVE",
        });
        _db.SaveChanges();
        return userId;
    }

    private Visit AddVisit(long id, long patientId, string status, bool hospitalFromAudit = false)
    {
        var visit = new Visit
        {
            Id = id,
            VisitCode = $"VIS-{id}",
            PatientId = patientId,
            Status = status,
        };
        if (!hospitalFromAudit)
        {
            var roomId = 5000 + id;
            _db.HospitalRooms.Add(new HospitalRoom
            {
                Id = roomId,
                HospitalId = HospitalA,
                RoomCode = $"R-{roomId}",
                Name = "Phong",
                RoomType = "EXAM",
                Status = "ACTIVE",
            });
            visit.ExaminationRoomId = roomId;
        }

        _db.Visits.Add(visit);
        _db.SaveChanges();
        return visit;
    }

    private void AddCharge(long visitId, decimal? price, string requirement)
    {
        var next = _db.VisitCharges.Any() ? _db.VisitCharges.Max(c => c.Id) + 1 : 1;
        _db.VisitCharges.Add(new VisitCharge
        {
            Id = next,
            VisitId = visitId,
            UnitPrice = price,
            PaymentRequirement = requirement,
        });
        _db.SaveChanges();
    }

    private void AddPolicy()
    {
        if (_db.HospitalPolicies.Any(p => p.Id == 1))
            return;
        _db.HospitalPolicies.Add(new HospitalPolicy
        {
            Id = 1,
            HospitalId = HospitalA,
            PolicyType = PaymentPolicyKeys.PolicyType,
            PolicyKey = PaymentPolicyKeys.VietQrAccount,
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN DEMO"}""",
            PolicyVersion = 1,
        });
        _db.SaveChanges();
    }

    private void AddWalkInAudit(long visitId, decimal price, string method)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            HospitalId = HospitalA,
            Action = "WALK_IN_VISIT_CREATED",
            EntityType = "VISIT",
            EntityId = visitId.ToString(),
            ChangeData = $$"""{"servicePrice":{{price}},"paymentMethod":"{{method}}"}""",
            OccurredAt = FixedNow.UtcDateTime,
        });
        _db.SaveChanges();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SqlStateException(string sqlState) : Exception
    {
        public string SqlState { get; } = sqlState;
    }

    private class QrTestDb : DbContext, IApplicationDbContext
    {
        public QrTestDb(DbContextOptions<QrTestDb> options)
            : base(options)
        {
        }

        protected QrTestDb(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => throw new NotSupportedException();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => throw new NotSupportedException();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw new NotSupportedException();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw new NotSupportedException();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw new NotSupportedException();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => throw new NotSupportedException();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => throw new NotSupportedException();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<VisitCharge> VisitCharges => Set<VisitCharge>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<Specialty>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<User>().Ignore(u => u.Staff);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Role>().Ignore(r => r.UserRoles);
            modelBuilder.Entity<Role>().Ignore(r => r.RolePermissions);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<UserRole>().Ignore(ur => ur.User);
            modelBuilder.Entity<UserRole>().Ignore(ur => ur.Role);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>().HasOne(p => p.User).WithOne(u => u.Patient).HasForeignKey<Patient>(p => p.UserId);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<HospitalRoom>().Ignore(r => r.Department);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<VisitCharge>().HasKey(c => c.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<Payment>().Property(p => p.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<HospitalPolicy>().HasKey(p => p.Id);
            modelBuilder.Entity<PatientFamilyLink>().HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
            modelBuilder.Entity<PatientFamilyLink>().Ignore(l => l.OwnerPatient);
            modelBuilder.Entity<PatientFamilyLink>().HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
                b.Ignore(a => a.ActorUser);
            });
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>().Ignore(s => s.User);
            modelBuilder.Entity<Staff>().Ignore(s => s.Department);
            modelBuilder.Entity<Staff>().Ignore(s => s.Doctor);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Doctor>().Ignore(d => d.Staff);
            modelBuilder.Entity<Doctor>().Ignore(d => d.Specialty);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ServiceAuthorizations);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ExaminationSlots);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<Service>().Ignore(s => s.Department);
            modelBuilder.Entity<Service>().Ignore(s => s.Specialty);
            modelBuilder.Entity<Service>().Ignore(s => s.ServicePrices);
            modelBuilder.Entity<ServicePrice>().HasKey(p => p.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Appointment>().Ignore(a => a.Slot);
            modelBuilder.Entity<Appointment>().Ignore(a => a.Patient);
            modelBuilder.Entity<ExaminationSlot>().HasKey(s => s.Id);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Service);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Doctor);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Room);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Appointments);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
        }
    }

    private sealed class ThrowOnceQrDb : QrTestDb
    {
        public ThrowOnceQrDb(DbContextOptions<ThrowOnceQrDb> options)
            : base(options)
        {
        }

        public bool ThrowOnNextSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!ThrowOnNextSave)
                return base.SaveChangesAsync(cancellationToken);

            ThrowOnNextSave = false;
            throw new DbUpdateException("duplicate", new SqlStateException("23505"));
        }
    }
}
