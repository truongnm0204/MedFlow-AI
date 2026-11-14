using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Payments;

public sealed class SePayWebhookEndpointTests : AuthTestBase
{
    private const string WebhookUrl = "/api/v1/payments/sepay/webhook";
    private const string ApiKey = "IntegrationTest_SePay_Key";

    public SePayWebhookEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Webhook_NoAuthorization_Returns401()
    {
        var response = await Client.PostAsJsonAsync(WebhookUrl, Payload(1, 10_000m, "MF1"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_WrongKey_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = JsonContent.Create(Payload(1, 10_000m, "MF1")),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Apikey", "wrong-key");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_ExactAmount_ConfirmsPaymentOnce()
    {
        var seed = await SeedAsync();
        try
        {
            var first = await PostWebhookAsync(Payload(9001, seed.Amount, seed.Code));
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(1, await CountEventsAsync(seed.PaymentId));

            Db.ChangeTracker.Clear();
            var payment = await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == seed.PaymentId);
            Assert.Equal(PaymentStatuses.Paid, payment.Status);
            Assert.Equal("FT-9001", payment.ExternalReference);
            Assert.Equal(new DateTime(2026, 11, 14, 10, 15, 0), payment.PaidAt);
            Assert.Equal(1, await Db.AuditLogs.CountAsync(a => a.EntityId == seed.PaymentId.ToString() && a.Action == "PAYMENT_CONFIRMED"));

            var duplicate = await PostWebhookAsync(Payload(9001, seed.Amount, seed.Code));
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            Assert.Equal(1, await CountEventsAsync(seed.PaymentId));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Webhook_PartialTransfers_ConfirmOnSecond()
    {
        var seed = await SeedAsync(amount: 120_000m);
        try
        {
            await PostWebhookAsync(Payload(9002, 60_000m, seed.Code));
            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Pending, (await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == seed.PaymentId)).Status);

            await PostWebhookAsync(Payload(9003, 60_000m, seed.Code));
            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Paid, (await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == seed.PaymentId)).Status);
            Assert.Equal(2, await CountEventsAsync(seed.PaymentId));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Webhook_UnknownContent_RecordsEventAndKeepsPending()
    {
        var seed = await SeedAsync();
        try
        {
            var response = await PostWebhookAsync(Payload(9004, seed.Amount, "khong co ma"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Pending, (await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == seed.PaymentId)).Status);
            var gatewayEvent = await Db.PaymentGatewayEvents.AsNoTracking().FirstAsync(e => e.ExternalId == "9004");
            Assert.Equal(PaymentGatewayOutcomes.Unmatched, gatewayEvent.Outcome);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Webhook_OutgoingTransfer_IgnoredWithoutEvent()
    {
        var seed = await SeedAsync();
        try
        {
            var response = await PostWebhookAsync(Payload(9005, seed.Amount, seed.Code, transferType: "out"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, await CountEventsAsync(seed.PaymentId));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task CreateQr_StalePendingRow_ExpiresAndIssuesNew()
    {
        var seed = await SeedAsync(createdAt: VietnamNow().AddMinutes(-(GenerateQrPaymentService.PendingExpiryMinutes + 10)));
        try
        {
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/visits/{seed.VisitId}/payments/qr");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PatientToken());
            var response = await Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.Created, body);

            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Expired, (await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == seed.PaymentId)).Status);
            Assert.Equal(2, await Db.Payments.CountAsync(p => p.VisitId == seed.VisitId));
            Assert.Equal(1, await Db.Payments.CountAsync(p => p.VisitId == seed.VisitId && p.Status == PaymentStatuses.Pending));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Apikey", ApiKey);
        return await Client.SendAsync(request);
    }

    private string PatientToken()
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<MedFlow.Application.Interfaces.Services.IJwtTokenService>();
        return jwt.GenerateAccessToken(new User { Id = 1, Email = "patient@medflow.test" }, ["PATIENT"]).AccessToken;
    }

    private static object Payload(
        long id,
        decimal amount,
        string content,
        string transferType = "in") => new
        {
            id,
            gateway = "Vietcombank",
            transactionDate = "2026-11-14 10:15:00",
            accountNumber = "0123456789",
            code = (string?)null,
            content,
            transferType,
            transferAmount = amount,
            accumulated = amount,
            subAccount = (string?)null,
            referenceCode = $"FT-{id}",
        };

    private static DateTime VietnamNow() =>
        DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);

    private async Task<int> CountEventsAsync(long paymentId) =>
        await Db.PaymentGatewayEvents.CountAsync(e => e.PaymentId == paymentId);

    private async Task<Seed> SeedAsync(
        decimal amount = 120_000m,
        DateTime? createdAt = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var staff = await Db.Staff.AsNoTracking().FirstAsync(s => s.UserId == 5);
        var room = new HospitalRoom
        {
            HospitalId = staff.HospitalId,
            RoomCode = $"SW{suffix}"[..8],
            Name = "Phong webhook test",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
        };
        Db.HospitalRooms.Add(room);
        await Db.SaveChangesAsync();

        var visit = new Visit
        {
            VisitCode = $"SW164-{suffix}",
            PatientId = 1,
            Status = "CHECKED_IN",
            ExaminationRoomId = room.Id,
            CheckedInAt = new DateTime(2026, 11, 14, 9, 0, 0),
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();
        Db.VisitCharges.Add(new VisitCharge
        {
            VisitId = visit.Id,
            UnitPrice = amount,
            PaymentRequirement = "REQUIRED",
        });

        var policy = new HospitalPolicy
        {
            HospitalId = staff.HospitalId,
            PolicyType = "PAYMENT",
            PolicyKey = "vietqr_account",
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN DEMO"}""",
            PolicyVersion = 1,
        };
        Db.HospitalPolicies.Add(policy);
        var payment = new Payment
        {
            VisitId = visit.Id,
            Amount = amount,
            Status = PaymentStatuses.Pending,
            CreatedAt = createdAt ?? VietnamNow(),
        };
        Db.Payments.Add(payment);
        await Db.SaveChangesAsync();
        payment.PaymentCode = $"MF{payment.Id}";
        await Db.SaveChangesAsync();

        return new Seed(visit.Id, room.Id, policy.Id, payment.Id, payment.PaymentCode!, amount);
    }

    private async Task CleanupAsync(Seed seed)
    {
        Db.ChangeTracker.Clear();
        var events = await Db.PaymentGatewayEvents
            .Where(e => e.PaymentId == seed.PaymentId || e.PaymentCode == seed.Code)
            .ToListAsync();
        Db.PaymentGatewayEvents.RemoveRange(events);

        var paymentIds = await Db.Payments
            .Where(p => p.VisitId == seed.VisitId)
            .Select(p => p.Id)
            .ToListAsync();
        Db.Payments.RemoveRange(await Db.Payments.Where(p => p.VisitId == seed.VisitId).ToListAsync());
        Db.VisitCharges.RemoveRange(await Db.VisitCharges.Where(c => c.VisitId == seed.VisitId).ToListAsync());

        var entityIds = paymentIds.Select(id => id.ToString()).Append(seed.VisitId.ToString()).ToList();
        Db.AuditLogs.RemoveRange(await Db.AuditLogs.Where(a => a.EntityId != null && entityIds.Contains(a.EntityId)).ToListAsync());
        Db.Visits.RemoveRange(await Db.Visits.Where(v => v.Id == seed.VisitId).ToListAsync());
        await Db.SaveChangesAsync();

        Db.HospitalRooms.RemoveRange(await Db.HospitalRooms.Where(r => r.Id == seed.RoomId).ToListAsync());
        Db.HospitalPolicies.RemoveRange(await Db.HospitalPolicies.Where(p => p.Id == seed.PolicyId).ToListAsync());
        await Db.SaveChangesAsync();
    }

    private sealed record Seed(long VisitId, long RoomId, long PolicyId, long PaymentId, string Code, decimal Amount);
}
