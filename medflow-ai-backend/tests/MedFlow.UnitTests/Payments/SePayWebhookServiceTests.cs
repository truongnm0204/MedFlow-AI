using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Payments;

public sealed class SePayWebhookServiceTests : IDisposable
{
    private const long HospitalA = 1;
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private readonly WebhookTestDb _db;
    private readonly FakeStore _store;
    private readonly SePayWebhookService _service;

    public SePayWebhookServiceTests()
    {
        var options = new DbContextOptionsBuilder<WebhookTestDb>()
            .UseInMemoryDatabase($"SePay_{Guid.NewGuid()}")
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
        _service = new SePayWebhookService(_db, _store, new FixedTime(FixedNow));
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
    }

    [Fact]
    public async Task UnknownCode_RecordsUnmatchedAndKeepsState()
    {
        var payment = AddPayment("MF100", 120_000m, PaymentStatuses.Pending);

        var result = await _service.HandleAsync(Request(content: "khong co ma"));

        Assert.Equal(PaymentGatewayOutcomes.Unmatched, result.Outcome);
        Assert.Single(_store.Events);
        Assert.Equal(PaymentGatewayOutcomes.Unmatched, _store.Events[0].Outcome);
        Assert.Equal(PaymentStatuses.Pending, (await _db.Payments.FindAsync(payment.Id))!.Status);
    }

    [Fact]
    public async Task ExactAmount_ConfirmsPaymentAndAudits()
    {
        var payment = AddPayment("MF100", 120_000m, PaymentStatuses.Pending);

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
    }

    [Theory]
    [InlineData("MF100")]
    [InlineData("mf100")]
    public async Task CodeFromContent_IsCaseInsensitive(string code)
    {
        AddPayment("MF100", 50_000m, PaymentStatuses.Pending);

        var result = await _service.HandleAsync(Request(content: $"CK {code} chuyen tien", amount: 50_000m));

        Assert.Equal(PaymentGatewayOutcomes.Paid, result.Outcome);
    }

    [Fact]
    public async Task PartialTransfers_AccumulateUntilPaid()
    {
        var payment = AddPayment("MF100", 120_000m, PaymentStatuses.Pending);

        var first = await _service.HandleAsync(Request(id: 1, content: payment.PaymentCode, amount: 60_000m));
        Assert.Equal(PaymentGatewayOutcomes.Partial, first.Outcome);
        Assert.Equal(PaymentStatuses.Pending, (await _db.Payments.FindAsync(payment.Id))!.Status);

        var second = await _service.HandleAsync(Request(id: 2, content: payment.PaymentCode, amount: 60_000m));
        Assert.Equal(PaymentGatewayOutcomes.Paid, second.Outcome);
        Assert.Equal(PaymentStatuses.Paid, (await _db.Payments.FindAsync(payment.Id))!.Status);
        Assert.Equal(2, _store.Events.Count);
    }

    [Fact]
    public async Task Overpayment_ConfirmsAndFlagsOverpaid()
    {
        var payment = AddPayment("MF100", 100_000m, PaymentStatuses.Pending);

        var result = await _service.HandleAsync(Request(content: payment.PaymentCode, amount: 150_000m));

        Assert.Equal(PaymentGatewayOutcomes.Overpaid, result.Outcome);
        Assert.Equal(PaymentStatuses.Paid, (await _db.Payments.FindAsync(payment.Id))!.Status);
    }

    [Fact]
    public async Task DuplicateExternalId_IgnoredWithoutSecondEvent()
    {
        var payment = AddPayment("MF100", 100_000m, PaymentStatuses.Pending);
        var request = Request(id: 77, content: payment.PaymentCode, amount: 100_000m);

        await _service.HandleAsync(request);
        var duplicate = await _service.HandleAsync(request);

        Assert.Equal(PaymentGatewayOutcomes.Ignored, duplicate.Outcome);
        Assert.Single(_store.Events);
        var audits = _db.AuditLogs.Count(a => a.Action == SePayWebhookService.AuditAction);
        Assert.Equal(1, audits);
    }

    [Fact]
    public async Task UniqueViolationOnSave_ReturnsIgnored()
    {
        var options = new DbContextOptionsBuilder<WebhookThrowDb>()
            .UseInMemoryDatabase($"SePayThrow_{Guid.NewGuid()}")
            .Options;
        await using var db = new WebhookThrowDb(options);
        db.Hospitals.Add(new Hospital { Id = HospitalA, HospitalCode = "H-A", Name = "A", IsActive = true });
        db.HospitalRooms.Add(new HospitalRoom { Id = 6000, HospitalId = HospitalA, RoomCode = "R", Name = "P", RoomType = "EXAM", Status = "ACTIVE" });
        var payment = new Payment { Id = 5, PaymentCode = "MF5", Amount = 100_000m, Status = PaymentStatuses.Pending, VisitId = 1000 };
        db.Payments.Add(payment);
        db.SaveChanges();
        db.ThrowOnNextSave = true;
        var store = new FakeStore();
        var service = new SePayWebhookService(db, store, new FixedTime(FixedNow));

        var result = await service.HandleAsync(Request(content: "MF5", amount: 100_000m));

        Assert.Equal(PaymentGatewayOutcomes.Ignored, result.Outcome);
        Assert.Equal(PaymentStatuses.Pending, db.Payments.Single(p => p.Id == 5).Status);
    }

    [Fact]
    public async Task AlreadyPaid_DoesNotRewrite()
    {
        var paidAt = new DateTime(2026, 11, 14, 8, 0, 0);
        var payment = AddPayment("MF100", 100_000m, PaymentStatuses.Paid, paidAt);

        var result = await _service.HandleAsync(Request(content: payment.PaymentCode, amount: 100_000m));

        Assert.Equal(PaymentGatewayOutcomes.AlreadyPaid, result.Outcome);
        var stored = await _db.Payments.FindAsync(payment.Id);
        Assert.Equal(paidAt, stored!.PaidAt);
    }

    [Fact]
    public async Task ExpiredPending_CanStillBePaid()
    {
        var payment = AddPayment("MF100", 100_000m, PaymentStatuses.Expired);

        var result = await _service.HandleAsync(Request(content: payment.PaymentCode, amount: 100_000m));

        Assert.Equal(PaymentGatewayOutcomes.Paid, result.Outcome);
        Assert.Equal(PaymentStatuses.Paid, (await _db.Payments.FindAsync(payment.Id))!.Status);
    }

    [Fact]
    public async Task ClosedPayment_NotReopened()
    {
        var payment = AddPayment("MF100", 100_000m, PaymentStatuses.Cancelled);

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
        var payment = AddPayment("MF100", null, PaymentStatuses.Pending);

        var result = await _service.HandleAsync(Request(content: payment.PaymentCode, amount: 10_000m));

        Assert.Equal(PaymentGatewayOutcomes.Unmatched, result.Outcome);
        Assert.Equal(PaymentStatuses.Pending, (await _db.Payments.FindAsync(payment.Id))!.Status);
    }

    private Payment AddPayment(string code, decimal? amount, string status, DateTime? paidAt = null)
    {
        var visit = new Visit { Id = 1000, VisitCode = "VIS-1000", PatientId = 100, Status = "CHECKED_IN", ExaminationRoomId = 6000 };
        _db.Visits.Add(visit);
        var payment = new Payment
        {
            Id = 100,
            PaymentCode = code,
            Amount = amount,
            Status = status,
            VisitId = visit.Id,
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
