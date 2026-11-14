using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Payments;

public sealed class GenerateQrPaymentEndpointTests : AuthTestBase
{
    public GenerateQrPaymentEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Create_NoToken_Returns401()
    {
        var response = await Client.PostAsync("/api/v1/visits/1/payments/qr", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_Patient_CreatesThenReusesSingleRow()
    {
        var seeded = await SeedAsync(120_000m, withPolicy: true);
        try
        {
            var created = await Client.SendAsync(AuthedPost($"/api/v1/patients/visits/{seeded.VisitCode}/payments/qr"));
            var createdBody = await created.Content.ReadAsStringAsync();
            Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
            var first = JsonSerializer.Deserialize<GenerateQrPaymentResponseDto>(createdBody, JsonOptions());
            Assert.NotNull(first);
            Assert.Equal(QrPaymentIssueKind.Created, first!.IssueKind);
            Assert.Equal(120_000m, first.Amount);
            Assert.Equal("970422", first.BankBin);
            Assert.Contains(first.PaymentCode, first.QrPayload, StringComparison.Ordinal);

            var again = await Client.SendAsync(AuthedPost($"/api/v1/visits/{seeded.VisitId}/payments/qr"));
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            var second = await again.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(JsonOptions());
            Assert.NotNull(second);
            Assert.Equal(QrPaymentIssueKind.Reused, second!.IssueKind);
            Assert.Equal(first.PaymentCode, second.PaymentCode);
            Assert.Equal(1, await Db.Payments.CountAsync(p => p.VisitId == seeded.VisitId));
        }
        finally
        {
            await CleanupAsync(seeded);
        }
    }

    [Fact]
    public async Task Create_OtherPatient_DoesNotLeakAmountOrAccount()
    {
        var seeded = await SeedAsync(654_321m, withPolicy: true);
        try
        {
            var response = await Client.SendAsync(AuthedPost(
                $"/api/v1/patients/visits/{seeded.VisitId}/payments/qr",
                userId: 2));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("654321", body);
            Assert.DoesNotContain("0123456789", body);
            Assert.Equal(0, await Db.Payments.CountAsync(p => p.VisitId == seeded.VisitId && p.Status == "PENDING" && p.PaymentCode != null && p.PaymentCode.StartsWith("MF")));
        }
        finally
        {
            await CleanupAsync(seeded);
        }
    }

    [Theory]
    [InlineData("RECEPTION", 5L)]
    [InlineData("DOCTOR", 4L)]
    public async Task Create_StaffRole_Returns403(string role, long userId)
    {
        var seeded = await SeedAsync(10_000m, withPolicy: true);
        try
        {
            var response = await Client.SendAsync(AuthedPost(
                $"/api/v1/visits/{seeded.VisitId}/payments/qr",
                userId,
                role));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await CleanupAsync(seeded);
        }
    }

    [Fact]
    public async Task Create_CashierVisit_Returns409()
    {
        var seeded = await SeedAsync(10_000m, withPolicy: true, paymentMethod: "CASHIER");
        try
        {
            var response = await Client.SendAsync(AuthedPost($"/api/v1/visits/{seeded.VisitId}/payments/qr"));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(0, await Db.Payments.CountAsync(p => p.VisitId == seeded.VisitId && p.PaymentCode != null && p.PaymentCode.StartsWith("MF")));
        }
        finally
        {
            await CleanupAsync(seeded);
        }
    }

    [Fact]
    public async Task Create_HospitalWithoutVietQrPolicy_Returns409()
    {
        var seeded = await SeedAsync(10_000m, withPolicy: false, isolatedHospital: true);
        try
        {
            var response = await Client.SendAsync(AuthedPost($"/api/v1/visits/{seeded.VisitId}/payments/qr"));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var problem = JsonDocument.Parse(body);
            Assert.Equal(
                "Cơ sở chưa cấu hình VietQR.",
                problem.RootElement.GetProperty("detail").GetString());
        }
        finally
        {
            await CleanupAsync(seeded);
        }
    }

    private HttpRequestMessage AuthedPost(string url, long userId = 1, string role = "PATIENT")
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(new User { Id = userId, Email = $"{role}@medflow.test" }, [role]).AccessToken;
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<Seed> SeedAsync(
        decimal price,
        bool withPolicy,
        string? paymentMethod = null,
        bool isolatedHospital = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var staff = await Db.Staff.AsNoTracking().FirstAsync(s => s.UserId == 5);
        var hospitalId = staff.HospitalId;
        long? isolatedHospitalId = null;

        if (isolatedHospital)
        {
            var hospital = new Hospital
            {
                HospitalCode = $"QRH{suffix}"[..10],
                Name = "Co so QR test",
                IsActive = true,
            };
            Db.Hospitals.Add(hospital);
            await Db.SaveChangesAsync();
            isolatedHospitalId = hospital.Id;
            hospitalId = hospital.Id;
        }

        var room = new HospitalRoom
        {
            HospitalId = hospitalId,
            RoomCode = $"QR{suffix}"[..8],
            Name = "Phong QR test",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
        };
        Db.HospitalRooms.Add(room);
        await Db.SaveChangesAsync();

        var visit = new Visit
        {
            VisitCode = $"QR162-{suffix}",
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
            UnitPrice = price,
            PaymentRequirement = "REQUIRED",
        });

        long? policyId = null;
        if (withPolicy)
        {
            var policy = new HospitalPolicy
            {
                HospitalId = hospitalId,
                PolicyType = "PAYMENT",
                PolicyKey = "vietqr_account",
                PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN DEMO"}""",
                PolicyVersion = 1,
            };
            Db.HospitalPolicies.Add(policy);
            await Db.SaveChangesAsync();
            policyId = policy.Id;
        }
        else
        {
            await Db.SaveChangesAsync();
        }

        if (paymentMethod is not null)
        {
            Db.AuditLogs.Add(new AuditLog
            {
                HospitalId = hospitalId,
                Action = "WALK_IN_VISIT_CREATED",
                EntityType = "VISIT",
                EntityId = visit.Id.ToString(),
                ChangeData = $$"""{"servicePrice":{{price}},"paymentMethod":"{{paymentMethod}}"}""",
                OccurredAt = DateTime.UtcNow,
            });
            await Db.SaveChangesAsync();
        }

        return new Seed(visit.Id, visit.VisitCode!, room.Id, policyId, isolatedHospitalId);
    }

    private async Task CleanupAsync(Seed seeded)
    {
        Db.ChangeTracker.Clear();
        var payments = await Db.Payments.Where(p => p.VisitId == seeded.VisitId).ToListAsync();
        Db.Payments.RemoveRange(payments);
        var charges = await Db.VisitCharges.Where(c => c.VisitId == seeded.VisitId).ToListAsync();
        Db.VisitCharges.RemoveRange(charges);
        var paymentIds = payments.Select(p => p.Id.ToString()).ToList();
        var visitKey = seeded.VisitId.ToString();
        var audits = await Db.AuditLogs.Where(a => a.EntityId == visitKey || paymentIds.Contains(a.EntityId!)).ToListAsync();
        Db.AuditLogs.RemoveRange(audits);
        var visit = await Db.Visits.FirstOrDefaultAsync(v => v.Id == seeded.VisitId);
        if (visit is not null)
            Db.Visits.Remove(visit);
        await Db.SaveChangesAsync();

        var room = await Db.HospitalRooms.FirstOrDefaultAsync(r => r.Id == seeded.RoomId);
        if (room is not null)
            Db.HospitalRooms.Remove(room);
        if (seeded.PolicyId is long policyId)
        {
            var policy = await Db.HospitalPolicies.FirstOrDefaultAsync(p => p.Id == policyId);
            if (policy is not null)
                Db.HospitalPolicies.Remove(policy);
        }

        if (seeded.HospitalId is long isolatedHospitalId)
        {
            var hospital = await Db.Hospitals.FirstOrDefaultAsync(h => h.Id == isolatedHospitalId);
            if (hospital is not null)
                Db.Hospitals.Remove(hospital);
        }

        await Db.SaveChangesAsync();
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

    private sealed record Seed(
        long VisitId,
        string VisitCode,
        long RoomId,
        long? PolicyId,
        long? HospitalId);
}
