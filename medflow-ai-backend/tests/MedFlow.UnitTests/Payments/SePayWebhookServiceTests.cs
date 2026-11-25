using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Payments;

public sealed class SePayWebhookServiceTests : IDisposable
{
    private const long HospitalA = 1;
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private readonly WebhookTestDb _db;
    private readonly FakeStore _store;
    private readonly RecordingRealtimePublisher _publisher;
    private readonly SePayWebhookService _service;

    public SePayWebhookServiceTests()
    {
        var options = new DbContextOptionsBuilder<WebhookTestDb>()
            .UseInMemoryDatabase($"SePay_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new WebhookTestDb(options);
        _db.Hospitals.Add(new Hospital { Id = HospitalA, HospitalCode = "H-A", Name = "Bệnh viện A", IsActive = true });
        _db.HospitalRooms.Add(new HospitalRoom
        {
            Id = 6000,
            HospitalId = HospitalA,
            RoomCode = "R-6000",
            Name = "Phòng",
            RoomType = "EXAM",
            Status = "ACTIVE",
        });
        _db.SaveChanges();
        _store = new FakeStore();
        _publisher = new RecordingRealtimePublisher(_db);
        _service = new SePayWebhookService(_db, _store, new FixedTime(FixedNow), _publisher);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task OutgoingTransfer_IgnoredWithoutEvent()
    {
        var result = await _service.HandleAsync(Request(transferType: "out"));

        Assert.Equal(PaymentGatewayOutcomes.Ignored, result.Outcome);
        Assert.Empty(_store.Events);
        Assert.Empty(_publisher.PaymentNotifications);
    }

    [Fact]
    public async Task UnknownCode_RecordsUnmatchedAndKeepsState()
    {
        var payment = AddChargePayment("MF100", 120_000m, PaymentStatuses.Pending);

        var result = await _service.HandleAsync(Request(content: "khong co ma"));

        Assert.Equal(PaymentGatewayOutcomes.Unmatched, result.Outcome);
        Assert.Single(_store.Events);
        Assert.Equal(PaymentGatewayOutcomes.Unmatched, _store.Events[0].Outcome);
        Assert.Equal(PaymentStatuses.Pending, (await _db.Payments.FindAsync(payment.Id))!.Status);
    }

    [Fact]
    public async Task ExactAmount_ConfirmsPaymentAndAuditsAndPublishesPostCommit()
    {
        var payment = AddChargePayment("MF100", 120_000m, PaymentStatuses.Pending);

        var result = await _service.HandleAsync(Request(content: $"Thanh toan {payment.PaymentCode}", amount: 120_000m));

        Assert.Equal(PaymentGatewayOutcomes.Paid, result.Outcome);
        var stored = await _db.Payments.FindAsync(payment.Id);
        Assert.Equal(PaymentStatuses.Paid, stored!.Status);
        Assert.Equal(new DateTime(2026, 11, 14, 10, 15, 0), stored.PaidAt);
        Assert.Equal("FT-1", stored.ExternalReference);

        var audit = Assert.Single(_db.AuditLogs.Where(a => a.Action == SePayWebhookService.AuditAction));
        Assert.Equal(HospitalA, audit.HospitalId);
        Assert.Equal(payment.Id.ToString(CultureInfo.InvariantCulture), audit.EntityId);
        using var json = JsonDocument.Parse(audit.ChangeData!);
        Assert.Equal("SEPAY", json.RootElement.GetProperty("provider").GetString());
        Assert.Equal(120_000m, json.RootElement.GetProperty("receivedAmount").GetDecimal());
        Assert.DoesNotContain("Bệnh nhân", audit.ChangeData, StringComparison.Ordinal);

        Assert.Single(_publisher.PaymentNotifications);
        Assert.All(_publisher.PublishedWhileTransactionActive, active => Assert.False(active));
    }

    [Theory]
    [InlineData("MF100")]
    [InlineData("mf100")]
    public async Task CodeFromContent_IsCaseInsensitive(string code)
    {
        AddChargePayment("MF100", 50_000m, PaymentStatuses.Pending);

        var result = await _service.HandleAsync(Request(content: $"CK {code} chuyen tien", amount: 50_000m));

        Assert.Equal(PaymentGatewayOutcomes.Paid, result.Outcome);
    }

    [Fact]
    public async Task Underpayment_TransitionsToMismatchAndNeverUnlocksCharge()
    {
        var payment = AddChargePayment("MF100", 120_000m, PaymentStatuses.Pending);

        var first = await _service.HandleAsync(Request(id: 1, content: payment.PaymentCode, amount: 60_000m));
        Assert.Equal(PaymentGatewayOutcomes.Partial, first.Outcome);

        var storedAfterFirst = await _db.Payments.FindAsync(payment.Id);
        Assert.Equal(SePayWebhookService.MismatchPaymentStatus, storedAfterFirst!.Status);
        Assert.Null(storedAfterFirst.PaidAt);
        Assert.Single(_db.AuditLogs.Where(a => a.Action == SePayWebhookService.ReconciliationRequiredAuditAction));

        // Second partial transfer cannot auto-reopen or mark PAID on a MISMATCH row
        var second = await _service.HandleAsync(Request(id: 2, content: payment.PaymentCode, amount: 60_000m));
        Assert.Equal(PaymentGatewayOutcomes.AlreadyClosed, second.Outcome);
        Assert.Equal(SePayWebhookService.MismatchPaymentStatus, (await _db.Payments.FindAsync(payment.Id))!.Status);
        Assert.Equal(2, _store.Events.Count);
    }

    [Fact]
    public async Task Overpayment_TransitionsToMismatchAndFlagsOverpaidWithoutUnlocking()
    {
        var payment = AddChargePayment("MF100", 100_000m, PaymentStatuses.Pending);

        var result = await _service.HandleAsync(Request(content: payment.PaymentCode, amount: 150_000m));

        Assert.Equal(PaymentGatewayOutcomes.Overpaid, result.Outcome);
        var stored = await _db.Payments.FindAsync(payment.Id);
        Assert.Equal(SePayWebhookService.MismatchPaymentStatus, stored!.Status);
        Assert.Null(stored.PaidAt);
        Assert.Single(_db.AuditLogs.Where(a => a.Action == SePayWebhookService.ReconciliationRequiredAuditAction));
    }

    [Fact]
    public async Task CancelledDiagnosticOrder_RecordsRefundRequiredAndNeverUnlocksService()
    {
        var visit = new Visit { Id = 1000, VisitCode = "VIS-1000", PatientId = 100, Status = "IN_PROGRESS", ExaminationRoomId = 6000 };
        var order = new DiagnosticOrder { Id = 500, VisitId = 1000, OrderCode = "DIA-500", Status = "CANCELLED" };
        var charge = new VisitCharge
        {
            Id = 200,
            VisitId = 1000,
            DiagnosticOrderId = 500,
            ChargeType = VisitChargeTypes.Diagnostic,
            UnitPrice = 180_000m,
            PaymentRequirement = "REQUIRED",
            Status = VisitChargeStatuses.Cancelled,
        };
        var payment = Payment.CreateForCharge(1000, 200, 180_000m, new DateTime(2026, 11, 14, 9, 0, 0), PaymentStatuses.Pending);
        payment.Id = 100;
        payment.PaymentCode = "MF100";

        _db.Visits.Add(visit);
        _db.DiagnosticOrders.Add(order);
        _db.VisitCharges.Add(charge);
        _db.Payments.Add(payment);
        _db.SaveChanges();

        var result = await _service.HandleAsync(Request(content: "MF100", amount: 180_000m));

        Assert.Equal(SePayWebhookService.CancelledServiceRefundRequiredOutcome, result.Outcome);

        var updatedCharge = await _db.VisitCharges.FindAsync(200L);
        Assert.Equal(VisitChargeStatuses.CancelledRefundPending, updatedCharge!.Status);

        var updatedPayment = await _db.Payments.FindAsync(100L);
        Assert.Equal(SePayWebhookService.MismatchPaymentStatus, updatedPayment!.Status);
        Assert.Null(updatedPayment.PaidAt);

        var audit = Assert.Single(_db.AuditLogs.Where(a => a.Action == SePayWebhookService.CancelledServiceAuditAction));
        Assert.Contains(SePayWebhookService.CancelledServiceReasonCode, audit.ChangeData!, StringComparison.Ordinal);

        var reader = new VisitChargeSettlementReader(_db, new FixedTime(FixedNow));
        var gate = await reader.CheckDiagnosticOrderEligibilityAsync(500);
        Assert.False(gate.IsUnlocked);
    }

    [Fact]
    public async Task ExpiredOldQrPaidWhileNewPendingQrExists_ExpiresNewQrAndMarksOldPaid()
    {
        var visit = new Visit { Id = 1000, VisitCode = "VIS-1000", PatientId = 100, Status = "CHECKED_IN", ExaminationRoomId = 6000 };
        var charge = VisitCharge.CreateExamination(1000, 1, 200_000m, new DateTime(2026, 11, 14, 9, 0, 0));
        charge.Id = 300;

        var qr1 = Payment.CreateForCharge(1000, 300, 200_000m, new DateTime(2026, 11, 14, 9, 0, 0), PaymentStatuses.Expired);
        qr1.Id = 101;
        qr1.PaymentCode = "MF101";

        var qr2 = Payment.CreateForCharge(1000, 300, 200_000m, new DateTime(2026, 11, 14, 9, 20, 0), PaymentStatuses.Pending);
        qr2.Id = 102;
        qr2.PaymentCode = "MF102";

        _db.Visits.Add(visit);
        _db.VisitCharges.Add(charge);
        _db.Payments.AddRange(qr1, qr2);
        _db.SaveChanges();

        var result = await _service.HandleAsync(Request(id: 50, content: "MF101", amount: 200_000m));

        Assert.Equal(PaymentGatewayOutcomes.Paid, result.Outcome);
        Assert.Equal(PaymentStatuses.Paid, (await _db.Payments.FindAsync(101L))!.Status);
        Assert.Equal(PaymentStatuses.Expired, (await _db.Payments.FindAsync(102L))!.Status);
    }

    [Fact]
    public async Task ExpiredOldQrPaidAfterNewQrAlreadyPaid_RecordsDuplicateReconcileAndPreservesSinglePaid()
    {
        var visit = new Visit { Id = 1000, VisitCode = "VIS-1000", PatientId = 100, Status = "CHECKED_IN", ExaminationRoomId = 6000 };
        var charge = VisitCharge.CreateExamination(1000, 1, 200_000m, new DateTime(2026, 11, 14, 9, 0, 0));
        charge.Id = 301;

        var qr1 = Payment.CreateForCharge(1000, 301, 200_000m, new DateTime(2026, 11, 14, 9, 0, 0), PaymentStatuses.Expired);
        qr1.Id = 103;
        qr1.PaymentCode = "MF103";

        var qr2 = Payment.CreateForCharge(1000, 301, 200_000m, new DateTime(2026, 11, 14, 9, 20, 0), PaymentStatuses.Paid);
        qr2.Id = 104;
        qr2.PaymentCode = "MF104";
        qr2.PaidAt = new DateTime(2026, 11, 14, 9, 22, 0);

        _db.Visits.Add(visit);
        _db.VisitCharges.Add(charge);
        _db.Payments.AddRange(qr1, qr2);
        _db.SaveChanges();

        var result = await _service.HandleAsync(Request(id: 51, content: "MF103", amount: 200_000m));

        Assert.Equal(SePayWebhookService.DuplicateChargeReconcileOutcome, result.Outcome);
        Assert.Equal(SePayWebhookService.MismatchPaymentStatus, (await _db.Payments.FindAsync(103L))!.Status);
        Assert.Equal(PaymentStatuses.Paid, (await _db.Payments.FindAsync(104L))!.Status);
        Assert.Equal(1, _db.Payments.Count(p => p.VisitChargeId == 301 && p.Status == PaymentStatuses.Paid));
        Assert.Single(_db.AuditLogs.Where(a => a.Action == SePayWebhookService.LateDuplicateAuditAction));
    }

    [Fact]
    public async Task DuplicateExternalId_IgnoredWithoutSecondEvent()
    {
        var payment = AddChargePayment("MF100", 100_000m, PaymentStatuses.Pending);
        var request = Request(id: 77, content: payment.PaymentCode, amount: 100_000m);

        await _service.HandleAsync(request);
        var duplicate = await _service.HandleAsync(request);

        Assert.Equal(PaymentGatewayOutcomes.Ignored, duplicate.Outcome);
        Assert.Single(_store.Events);
        var audits = _db.AuditLogs.Count(a => a.Action == SePayWebhookService.AuditAction);
        Assert.Equal(1, audits);
        Assert.Single(_publisher.PaymentNotifications);
    }

    [Fact]
    public async Task UniqueViolationOnSave_ReturnsIgnoredAndDoesNotPublishSignalR()
    {
        var options = new DbContextOptionsBuilder<WebhookThrowDb>()
            .UseInMemoryDatabase($"SePayThrow_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new WebhookThrowDb(options);
        db.Hospitals.Add(new Hospital { Id = HospitalA, HospitalCode = "H-A", Name = "A", IsActive = true });
        db.HospitalRooms.Add(new HospitalRoom { Id = 6000, HospitalId = HospitalA, RoomCode = "R", Name = "P", RoomType = "EXAM", Status = "ACTIVE" });
        db.Visits.Add(new Visit { Id = 1000, VisitCode = "VIS-1000", PatientId = 100, Status = "CHECKED_IN", ExaminationRoomId = 6000 });
        db.VisitCharges.Add(new VisitCharge { Id = 50, VisitId = 1000, ChargeType = VisitChargeTypes.Examination, UnitPrice = 100_000m, PaymentRequirement = "REQUIRED", Status = VisitChargeStatuses.Active });
        var payment = new Payment { Id = 5, PaymentCode = "MF5", Amount = 100_000m, Status = PaymentStatuses.Pending, VisitId = 1000, VisitChargeId = 50, PaymentScope = PaymentScopes.Charge };
        db.Payments.Add(payment);
        db.SaveChanges();
        db.ThrowOnNextSave = true;
        var store = new FakeStore();
        var pub = new RecordingRealtimePublisher(db);
        var service = new SePayWebhookService(db, store, new FixedTime(FixedNow), pub);

        var result = await service.HandleAsync(Request(content: "MF5", amount: 100_000m));

        Assert.Equal(PaymentGatewayOutcomes.Ignored, result.Outcome);
        Assert.Equal(PaymentStatuses.Pending, db.Payments.Single(p => p.Id == 5).Status);
        Assert.Empty(pub.PaymentNotifications);
    }

    [Fact]
    public async Task AlreadyPaid_DoesNotRewrite()
    {
        var paidAt = new DateTime(2026, 11, 14, 8, 0, 0);
        var payment = AddChargePayment("MF100", 100_000m, PaymentStatuses.Paid, paidAt);

        var result = await _service.HandleAsync(Request(content: payment.PaymentCode, amount: 100_000m));

        Assert.Equal(PaymentGatewayOutcomes.AlreadyPaid, result.Outcome);
        var stored = await _db.Payments.FindAsync(payment.Id);
        Assert.Equal(paidAt, stored!.PaidAt);
    }

    [Fact]
    public async Task ExpiredPending_CanStillBePaid()
    {
        var payment = AddChargePayment("MF100", 100_000m, PaymentStatuses.Expired);

        var result = await _service.HandleAsync(Request(content: payment.PaymentCode, amount: 100_000m));

        Assert.Equal(PaymentGatewayOutcomes.Paid, result.Outcome);
        Assert.Equal(PaymentStatuses.Paid, (await _db.Payments.FindAsync(payment.Id))!.Status);
    }

    [Fact]
    public async Task ClosedPayment_NotReopened()
    {
        var payment = AddChargePayment("MF100", 100_000m, PaymentStatuses.Cancelled);

        var result = await _service.HandleAsync(Request(content: payment.PaymentCode, amount: 100_000m));

        Assert.Equal(PaymentGatewayOutcomes.AlreadyClosed, result.Outcome);
        Assert.Equal(PaymentStatuses.Cancelled, (await _db.Payments.FindAsync(payment.Id))!.Status);
    }

    [Fact]
    public async Task InvalidPayload_ThrowsValidation()
    {
        var badId = await Assert.ThrowsAsync<ValidationException>(() => _service.HandleAsync(Request(id: 0)));
        Assert.Equal(SePayWebhookService.InvalidTransactionMessage, badId.Message);

        var badAmount = await Assert.ThrowsAsync<ValidationException>(() => _service.HandleAsync(Request(amount: 0m)));
        Assert.Equal(SePayWebhookService.InvalidAmountMessage, badAmount.Message);
    }

    [Fact]
    public async Task PaymentWithoutAmount_StaysUnmatched()
    {
        var visit = new Visit { Id = 1000, VisitCode = "VIS-1000", PatientId = 100, Status = "CHECKED_IN", ExaminationRoomId = 6000 };
        _db.Visits.Add(visit);
        var payment = new Payment
        {
            Id = 100,
            PaymentCode = "MF100",
            Amount = null,
            Status = PaymentStatuses.Pending,
            VisitId = visit.Id,
            PaymentScope = PaymentScopes.LegacyVisit,
        };
        _db.Payments.Add(payment);
        _db.SaveChanges();

        var result = await _service.HandleAsync(Request(content: payment.PaymentCode, amount: 10_000m));

        Assert.Equal(PaymentGatewayOutcomes.Unmatched, result.Outcome);
        Assert.Equal(PaymentStatuses.Pending, (await _db.Payments.FindAsync(payment.Id))!.Status);
    }

    private Payment AddChargePayment(string code, decimal amount, string status, DateTime? paidAt = null)
    {
        var visit = new Visit { Id = 1000, VisitCode = "VIS-1000", PatientId = 100, Status = "CHECKED_IN", ExaminationRoomId = 6000 };
        var charge = new VisitCharge
        {
            Id = 500,
            VisitId = visit.Id,
            ChargeType = VisitChargeTypes.Examination,
            UnitPrice = amount,
            PaymentRequirement = "REQUIRED",
            Status = VisitChargeStatuses.Active,
        };
        _db.Visits.Add(visit);
        _db.VisitCharges.Add(charge);
        var payment = new Payment
        {
            Id = 100,
            PaymentCode = code,
            Amount = amount,
            Status = status,
            VisitId = visit.Id,
            VisitChargeId = charge.Id,
            PaymentScope = PaymentScopes.Charge,
            PaidAt = paidAt,
        };
        _db.Payments.Add(payment);
        _db.SaveChanges();
        return payment;
    }

    private static SePayWebhookRequest Request(
        long id = 1,
        decimal amount = 50_000m,
        string? content = "MF100",
        string transferType = "in") => new()
        {
            Id = id,
            Gateway = "Vietcombank",
            TransactionDate = "2026-11-14 10:15:00",
            AccountNumber = "0123456789",
            Code = null,
            Content = content,
            TransferType = transferType,
            TransferAmount = amount,
            ReferenceCode = "FT-1",
        };

    private sealed class RecordingRealtimePublisher(DbContext db) : IRealtimeNotificationPublisher
    {
        public List<VisitPaymentChangedNotification> PaymentNotifications { get; } = [];
        public List<QueueChangedNotification> QueueNotifications { get; } = [];
        public List<bool> PublishedWhileTransactionActive { get; } = [];

        public Task PublishQueueChangedAsync(QueueChangedNotification notification, CancellationToken ct = default)
        {
            PublishedWhileTransactionActive.Add(db.Database.CurrentTransaction is not null);
            QueueNotifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task PublishVisitPaymentChangedAsync(VisitPaymentChangedNotification notification, CancellationToken ct = default)
        {
            PublishedWhileTransactionActive.Add(db.Database.CurrentTransaction is not null);
            PaymentNotifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IPaymentGatewayEventStore
    {
        public List<PaymentGatewayEvent> Events { get; } = [];

        public Task<bool> ExistsAsync(string provider, string externalId, CancellationToken ct) =>
            Task.FromResult(Events.Any(e => e.Provider == provider && e.ExternalId == externalId));

        public Task<decimal> SumReceivedAmountAsync(string paymentCode, CancellationToken ct) =>
            Task.FromResult(Events
                .Where(e => e.PaymentCode == paymentCode
                    && PaymentGatewayOutcomes.Counted.Contains(e.Outcome))
                .Sum(e => e.TransferAmount ?? 0m));

        public void Add(PaymentGatewayEvent gatewayEvent) => Events.Add(gatewayEvent);
    }

    private sealed class SqlStateException(string sqlState) : Exception
    {
        public string SqlState { get; } = sqlState;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private class WebhookTestDb : DbContext, IApplicationDbContext
    {
        public WebhookTestDb(DbContextOptions<WebhookTestDb> options)
            : base(options)
        {
        }

        protected WebhookTestDb(DbContextOptions options)
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
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();

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
            modelBuilder.Entity<HospitalPolicy>().HasKey(p => p.Id);
            modelBuilder.Entity<PatientFamilyLink>().HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
            modelBuilder.Entity<PatientFamilyLink>().Ignore(l => l.OwnerPatient);
            modelBuilder.Entity<PatientFamilyLink>().HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<HospitalRoom>().Ignore(r => r.Department);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<VisitCharge>().HasKey(c => c.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(d => d.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
                b.Ignore(a => a.ActorUser);
            });
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
        }
    }

    private sealed class WebhookThrowDb : WebhookTestDb
    {
        public WebhookThrowDb(DbContextOptions<WebhookThrowDb> options)
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
