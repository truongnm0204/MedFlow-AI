using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Payments;

public sealed class VisitChargesServiceTests : IDisposable
{
    private const long HospitalA = 1;
    private const long HospitalB = 2;
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private readonly TestChargesDbContext _db;
    private readonly VisitChargesService _service;

    public VisitChargesServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestChargesDbContext>()
            .UseInMemoryDatabase($"VisitCharges_{Guid.NewGuid()}")
            .Options;
        _db = new TestChargesDbContext(options);
        _db.Hospitals.AddRange(
            new Hospital { Id = HospitalA, HospitalCode = "H-A", Name = "Co so A", IsActive = true },
            new Hospital { Id = HospitalB, HospitalCode = "H-B", Name = "Co so B", IsActive = true });
        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Benh nhan" },
            new Role { Id = 2, Code = "RECEPTION", Name = "Le tan" },
            new Role { Id = 3, Code = "HOSPITAL_ADMIN", Name = "Admin benh vien" },
            new Role { Id = 4, Code = "SYSTEM_ADMIN", Name = "Admin he thong" },
            new Role { Id = 5, Code = "DOCTOR", Name = "Bac si" });
        _db.SaveChanges();
        _service = new VisitChargesService(_db, new FixedTime(FixedNow));
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task GetAsync_PatientOwner_ReturnsCharges()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, actor.PatientId, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 150_000m, "REQUIRED");

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(150_000m, result.PayableTotal);
        Assert.Equal(150_000m, result.AmountDue);
        Assert.Equal(VisitChargesService.SettlementUnpaid, result.SettlementStatus);
        Assert.True(result.CanPayByQr);
        Assert.Equal(VisitChargesService.CurrencyVnd, result.Currency);
        Assert.Equal("UNPAID", result.PaymentStatus);
    }

    [Fact]
    public async Task GetAsync_PatientOtherVisit_ThrowsNotFound()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 999, "CHECKED_IN", HospitalA);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(actor.UserId, visit.Id.ToString()));

        Assert.Equal("Không tìm thấy lượt khám.", ex.Message);
    }

    [Fact]
    public async Task GetAsync_ActiveFamilyLink_ReturnsCharges()
    {
        var actor = AddPatient(10, 100);
        AddPatient(11, 200);
        _db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = 100,
            MemberPatientId = 200,
            VerificationStatus = "VERIFIED",
            MemberPatient = null,
        });
        _db.Patients.Find(200L)!.Status = "ACTIVE";
        var visit = AddVisit(1000, 200, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 80_000m, "REQUIRED");
        _db.SaveChanges();

        var result = await _service.GetAsync(actor.UserId, "vis-family");

        Assert.Equal(80_000m, result.AmountDue);
    }

    [Fact]
    public async Task GetAsync_RevokedFamilyLink_ThrowsNotFound()
    {
        var actor = AddPatient(10, 100);
        AddPatient(11, 200);
        _db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = 100,
            MemberPatientId = 200,
            VerificationStatus = "VERIFIED",
            RevokedAt = FixedNow.UtcDateTime,
        });
        var visit = AddVisit(1000, 200, "CHECKED_IN", HospitalA);
        _db.SaveChanges();

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(actor.UserId, visit.Id.ToString()));
    }

    [Fact]
    public async Task GetAsync_StaffOtherHospital_ThrowsForbidden()
    {
        var actor = AddReception(20, HospitalA);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalB);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetAsync(actor.UserId, visit.Id.ToString()));

        Assert.Equal("Bạn không có quyền xem khoản phí của lượt khám này.", ex.Message);
    }

    [Fact]
    public async Task GetAsync_ConflictingQueueHospitals_ThrowsConflict()
    {
        var actor = AddReception(20, HospitalA);
        var visit = AddVisit(1000, 100, "CHECKED_IN", hospitalId: null);
        AddPoint(1, HospitalA);
        AddPoint(2, HospitalB);
        _db.QueueEntries.AddRange(
            new QueueEntry { Id = 1, VisitId = visit.Id, ServicePointId = 1 },
            new QueueEntry { Id = 2, VisitId = visit.Id, CurrentServicePointId = 2 });
        AddCharge(visit.Id, 777_001m, "REQUIRED");
        _db.SaveChanges();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.GetAsync(actor.UserId, visit.Id.ToString()));

        Assert.Equal("Không xác định được cơ sở của lượt khám.", ex.Message);
        Assert.DoesNotContain("777001", ex.Message);
    }

    [Fact]
    public async Task GetAsync_UnresolvedHospital_DoesNotUseActorHospital()
    {
        var actor = AddReception(20, HospitalA);
        var visit = AddVisit(1000, 100, "CHECKED_IN", hospitalId: null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(actor.UserId, visit.Id.ToString()));

        Assert.Equal("Không tìm thấy lượt khám.", ex.Message);
    }

    [Fact]
    public async Task GetAsync_Waived_ListedAndExcludedFromTotal()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 150_000m, "REQUIRED");
        AddCharge(visit.Id, 50_000m, "WAIVED");

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(2, result.Charges.Count);
        Assert.Contains(result.Charges, c => c.Payable == false && c.UnitPrice == 50_000m);
        Assert.Equal(150_000m, result.PayableTotal);
        Assert.Equal(150_000m, result.AmountDue);
    }

    [Fact]
    public async Task GetAsync_UnknownRequirement_IsNotWaived()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 90_000m, "REQUIERD");

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Null(result.Charges[0].Payable);
        Assert.Equal(VisitChargesService.ChargeDataInvalid, result.Charges[0].ChargeDataStatus);
        Assert.Equal(0m, result.PayableTotal);
        Assert.Null(result.AmountDue);
        Assert.False(result.CanPayByQr);
    }

    [Fact]
    public async Task GetAsync_NullRequirement_TreatedAsRequired()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 40_000m, null);

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.True(result.Charges[0].Payable);
        Assert.Equal(40_000m, result.AmountDue);
    }

    [Fact]
    public async Task GetAsync_PricedPlusUnpriced_AmountDueNull()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 200_000m, "REQUIRED");
        AddCharge(visit.Id, null, "REQUIRED");

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(200_000m, result.PayableTotal);
        Assert.True(result.HasUnpricedPayableCharges);
        Assert.Null(result.AmountDue);
        Assert.Equal(VisitChargesService.SettlementUndetermined, result.SettlementStatus);
        Assert.False(result.CanPayByQr);
    }

    [Fact]
    public async Task GetAsync_NoCharge_WalkInAuditPrice_DerivedFromServicePrice()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", hospitalId: null);
        _db.AuditLogs.Add(new AuditLog
        {
            Id = 1,
            HospitalId = HospitalA,
            Action = "WALK_IN_VISIT_CREATED",
            EntityType = "VISIT",
            EntityId = visit.Id.ToString(),
            ChangeData = JsonSerializer.Serialize(new
            {
                serviceId = 7,
                serviceCode = "KB",
                serviceName = "Kham benh",
                servicePrice = 180_000m,
                paymentMethod = "VIETQR",
            }),
            OccurredAt = FixedNow.UtcDateTime,
        });
        _db.Payments.Add(new Payment { Id = 1, VisitId = visit.Id, Amount = 50m, Status = "PENDING" });
        _db.SaveChanges();

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        var line = Assert.Single(result.Charges);
        Assert.Equal(VisitChargesService.ChargeSourceDerived, line.ChargeSource);
        Assert.Equal(180_000m, line.UnitPrice);
        Assert.NotEqual(50m, line.UnitPrice);
        Assert.NotEqual(200_000m, line.UnitPrice);
        Assert.Equal("VIETQR", result.PaymentMethod);
        Assert.False(result.CanPayByQr);
    }

    [Fact]
    public async Task GetAsync_NoPrice_DoesNotEmitZeroDue()
    {
        var actor = AddPatient(10, 100);
        var visit = AddScheduledVisit(1000, 100, HospitalA, serviceId: 7, price: null);

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Null(result.Charges[0].UnitPrice);
        Assert.Equal(VisitChargesService.ChargeDataUnpriced, result.Charges[0].ChargeDataStatus);
        Assert.Null(result.AmountDue);
        Assert.NotEqual(0m, result.AmountDue);
        Assert.NotEqual(200_000m, result.PayableTotal);
    }

    [Fact]
    public async Task GetAsync_NoCharge_DoesNotAppendWhenPersistedExists()
    {
        var actor = AddPatient(10, 100);
        var visit = AddScheduledVisit(1000, 100, HospitalA, serviceId: 7, price: 99_999m);
        AddCharge(visit.Id, 10_000m, "REQUIRED");

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        var line = Assert.Single(result.Charges);
        Assert.Equal(VisitChargesService.ChargeSourcePersisted, line.ChargeSource);
        Assert.Equal(10_000m, line.UnitPrice);
        Assert.Equal(10_000m, result.AmountDue);
    }

    [Fact]
    public async Task GetAsync_PartialPaid_IsMismatchNotSettled()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 300_000m, "REQUIRED");
        _db.Payments.Add(new Payment { Id = 1, VisitId = visit.Id, Amount = 100_000m, Status = "PAID", PaymentCode = "QR-1" });
        _db.SaveChanges();

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(100_000m, result.RecognizedPaidTotal);
        Assert.Equal(VisitChargesService.SettlementMismatch, result.SettlementStatus);
        Assert.Null(result.AmountDue);
        Assert.False(result.CanPayByQr);
    }

    [Fact]
    public async Task GetAsync_PaidThenFailed_KeepsRecognizedPaid()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 300_000m, "REQUIRED");
        _db.Payments.AddRange(
            new Payment { Id = 1, VisitId = visit.Id, Amount = 100_000m, Status = "PAID", PaymentCode = "QR-1" },
            new Payment { Id = 2, VisitId = visit.Id, Amount = 200_000m, Status = "FAILED", PaymentCode = "QR-2" });
        _db.SaveChanges();

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(100_000m, result.RecognizedPaidTotal);
        Assert.Equal("FAILED", result.PaymentStatus);
        Assert.Equal(VisitChargesService.SettlementMismatch, result.SettlementStatus);
        Assert.Null(result.AmountDue);
        Assert.NotEqual(300_000m, result.AmountDue);
    }

    [Fact]
    public async Task GetAsync_SinglePaidEqualsTotal_AmountDueZero()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 300_000m, "REQUIRED");
        _db.Payments.Add(new Payment { Id = 1, VisitId = visit.Id, Amount = 300_000m, Status = "PAID", PaymentCode = "QR-1" });
        _db.SaveChanges();

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(0m, result.AmountDue);
        Assert.Equal(VisitChargesService.SettlementPaid, result.SettlementStatus);
        Assert.False(result.CanPayByQr);
    }

    [Fact]
    public async Task GetAsync_TwoDistinctPaidKeys_NotTreatedAsSettled()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 300_000m, "REQUIRED");
        _db.Payments.AddRange(
            new Payment { Id = 1, VisitId = visit.Id, Amount = 150_000m, Status = "PAID", PaymentCode = "QR-1" },
            new Payment { Id = 2, VisitId = visit.Id, Amount = 150_000m, Status = "PAID", PaymentCode = "QR-2" });
        _db.SaveChanges();

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(300_000m, result.RecognizedPaidTotal);
        Assert.Equal(VisitChargesService.SettlementMismatch, result.SettlementStatus);
        Assert.Null(result.AmountDue);
    }

    [Fact]
    public async Task GetAsync_DuplicatePaymentCode_CountedOnce()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 300_000m, "REQUIRED");
        _db.Payments.AddRange(
            new Payment { Id = 1, VisitId = visit.Id, Amount = 300_000m, Status = "PAID", PaymentCode = "QR-1" },
            new Payment { Id = 2, VisitId = visit.Id, Amount = 300_000m, Status = "PAID", PaymentCode = "QR-1" });
        _db.SaveChanges();

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(300_000m, result.RecognizedPaidTotal);
        Assert.Equal(0m, result.AmountDue);
        Assert.Equal(VisitChargesService.SettlementPaid, result.SettlementStatus);
    }

    [Fact]
    public async Task GetAsync_CancelledVisit_OkButCannotPayByQr()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CANCELLED", HospitalA);
        AddCharge(visit.Id, 10_000m, "REQUIRED");

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(10_000m, result.AmountDue);
        Assert.False(result.CanPayByQr);
    }

    [Fact]
    public async Task GetAsync_PendingPayment_CannotPayByQr()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 10_000m, "REQUIRED");
        _db.Payments.Add(new Payment { Id = 1, VisitId = visit.Id, Amount = 10_000m, Status = "PENDING" });
        _db.SaveChanges();

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(10_000m, result.AmountDue);
        Assert.False(result.CanPayByQr);
    }

    [Fact]
    public async Task GetAsync_Cashier_CannotPayByQr()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", hospitalId: null);
        _db.AuditLogs.Add(new AuditLog
        {
            Id = 1,
            HospitalId = HospitalA,
            Action = "WALK_IN_VISIT_CREATED",
            EntityType = "VISIT",
            EntityId = visit.Id.ToString(),
            ChangeData = """{"servicePrice":10000,"paymentMethod":"CASHIER"}""",
            OccurredAt = FixedNow.UtcDateTime,
        });
        _db.SaveChanges();

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(10_000m, result.AmountDue);
        Assert.Equal("CASHIER", result.PaymentMethod);
        Assert.False(result.CanPayByQr);
    }

    [Fact]
    public async Task GetAsync_NoPayment_StatusUnpaid()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, 10_000m, "REQUIRED");
        var payments = _db.Payments.Count();
        var charges = _db.VisitCharges.Count();
        var audits = _db.AuditLogs.Count();

        var result = await _service.GetAsync(actor.UserId, visit.VisitCode!);

        Assert.Equal("UNPAID", result.PaymentStatus);
        Assert.Null(result.PaymentId);
        Assert.Equal(payments, _db.Payments.Count());
        Assert.Equal(charges, _db.VisitCharges.Count());
        Assert.Equal(audits, _db.AuditLogs.Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("12345678901234567890123456789012345678901")]
    public async Task GetAsync_BlankIdentifier_ThrowsValidation(string idOrCode)
    {
        var actor = AddPatient(10, 100);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.GetAsync(actor.UserId, idOrCode));

        Assert.Equal("Mã lượt khám không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task GetAsync_NegativeUnitPrice_InvalidNotSummed()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, "CHECKED_IN", HospitalA);
        AddCharge(visit.Id, -5m, "REQUIRED");

        var result = await _service.GetAsync(actor.UserId, visit.Id.ToString());

        Assert.Equal(VisitChargesService.ChargeDataInvalid, result.Charges[0].ChargeDataStatus);
        Assert.Null(result.Charges[0].Payable);
        Assert.Equal(0m, result.PayableTotal);
        Assert.Null(result.AmountDue);
    }

    private (long UserId, long PatientId) AddPatient(long userId, long patientId)
    {
        _db.Users.Add(new User
        {
            Id = userId,
            Email = $"p{userId}@test.local",
            AccountStatus = AccountStatus.ACTIVE,
        });
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
        return (userId, patientId);
    }

    private (long UserId, long StaffId) AddReception(long userId, long hospitalId)
    {
        _db.Users.Add(new User
        {
            Id = userId,
            Email = $"r{userId}@test.local",
            AccountStatus = AccountStatus.ACTIVE,
        });
        _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = 2 });
        _db.Staff.Add(new Staff
        {
            Id = userId,
            UserId = userId,
            HospitalId = hospitalId,
            StaffCode = $"S-{userId}",
            FullName = "Le tan",
            Status = "ACTIVE",
        });
        _db.SaveChanges();
        return (userId, userId);
    }

    private Visit AddVisit(long id, long? patientId, string status, long? hospitalId)
    {
        var visit = new Visit
        {
            Id = id,
            VisitCode = id == 1000 && patientId == 200 ? "VIS-FAMILY" : $"VIS-{id}",
            PatientId = patientId,
            Status = status,
        };
        if (hospitalId is long roomHospital)
        {
            var roomId = 5000 + id;
            _db.HospitalRooms.Add(new HospitalRoom
            {
                Id = roomId,
                HospitalId = roomHospital,
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

    private Visit AddScheduledVisit(long id, long patientId, long hospitalId, long serviceId, decimal? price)
    {
        var visit = AddVisit(id, patientId, "CHECKED_IN", hospitalId);
        _db.Services.Add(new Service
        {
            Id = serviceId,
            HospitalId = hospitalId,
            ServiceCode = "KB",
            Name = "Kham benh",
            ServiceType = "EXAM",
            IsActive = true,
        });
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = id,
            ServiceId = serviceId,
            Status = "BOOKED",
        });
        _db.Appointments.Add(new Appointment { Id = id, SlotId = id, PatientId = patientId, Status = "CHECKED_IN" });
        visit.AppointmentId = id;
        if (price is decimal amount)
        {
            _db.ServicePrices.Add(new ServicePrice { Id = id, ServiceId = serviceId, PriceAmount = amount });
        }

        _db.SaveChanges();
        return visit;
    }

    private void AddCharge(long visitId, decimal? unitPrice, string? requirement)
    {
        var next = _db.VisitCharges.Any() ? _db.VisitCharges.Max(c => c.Id) + 1 : 1;
        _db.VisitCharges.Add(new VisitCharge
        {
            Id = next,
            VisitId = visitId,
            UnitPrice = unitPrice,
            PaymentRequirement = requirement,
        });
        _db.SaveChanges();
    }

    private void AddPoint(long id, long hospitalId)
    {
        _db.QueueServicePoints.Add(new QueueServicePoint
        {
            Id = id,
            HospitalId = hospitalId,
            Name = $"Diem {id}",
        });
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestChargesDbContext : DbContext, IApplicationDbContext
    {
        public TestChargesDbContext(DbContextOptions<TestChargesDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() => new("Not used by VisitChargesServiceTests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<VisitCharge> VisitCharges => Set<VisitCharge>();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();

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
            modelBuilder.Ignore<HospitalPolicy>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Role>().Ignore(r => r.UserRoles);
            modelBuilder.Entity<Role>().Ignore(r => r.RolePermissions);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<UserRole>().Ignore(ur => ur.User);
            modelBuilder.Entity<UserRole>().Ignore(ur => ur.Role);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>()
                .HasOne(p => p.User)
                .WithOne(u => u.Patient)
                .HasForeignKey<Patient>(p => p.UserId);
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>().Ignore(s => s.User);
            modelBuilder.Entity<Staff>().Ignore(s => s.Department);
            modelBuilder.Entity<Staff>().Ignore(s => s.Doctor);
            modelBuilder.Entity<User>().Ignore(u => u.Staff);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<HospitalRoom>().Ignore(r => r.Department);
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
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Doctor>().Ignore(d => d.Staff);
            modelBuilder.Entity<Doctor>().Ignore(d => d.Specialty);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ServiceAuthorizations);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ExaminationSlots);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(p => p.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<VisitCharge>().HasKey(c => c.Id);
            modelBuilder.Entity<PatientFamilyLink>().HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
            modelBuilder.Entity<PatientFamilyLink>().Ignore(l => l.OwnerPatient);
            modelBuilder.Entity<PatientFamilyLink>()
                .HasOne(l => l.MemberPatient)
                .WithMany()
                .HasForeignKey(l => l.MemberPatientId);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
                b.Ignore(a => a.ActorUser);
            });
        }
    }
}
