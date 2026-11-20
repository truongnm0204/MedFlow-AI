using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Patients;

public sealed class PatientVisitListEndpointTests : AuthTestBase
{
    private const string Url = "/api/v1/patients/me/visits";

    public PatientVisitListEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyVisits_NoToken_Returns401()
    {
        var response = await Client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyVisits_OwnVisit_ReturnsIdentityHospitalAndPayment()
    {
        var seed = await SeedAsync();
        try
        {
            var response = await Client.SendAsync(AuthedGet(Url, userId: 1, role: "PATIENT"));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = System.Text.Json.JsonSerializer.Deserialize<PatientVisitListResponseDto>(
                body,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            Assert.NotNull(payload);

            var item = payload!.Items.SingleOrDefault(i => i.VisitId == seed.VisitId);
            Assert.NotNull(item);
            Assert.Equal(seed.VisitCode, item!.VisitCode);
            Assert.Equal("CHECKED_IN", item.VisitStatus);
            Assert.Equal(seed.HospitalName, item.HospitalName);
            Assert.Equal("PENDING", item.PaymentStatus);
            Assert.Equal(seed.PaymentCode, item.PaymentCode);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task GetMyVisits_StrangerVisit_NotListed()
    {
        var seed = await SeedAsync();
        try
        {
            var response = await Client.SendAsync(AuthedGet(Url, userId: 1, role: "PATIENT"));
            var payload = await response.Content.ReadFromJsonAsync<PatientVisitListResponseDto>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(payload!.Items, i => i.VisitId == seed.StrangerVisitId);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task GetMyVisits_LimitZero_ClampsToOne()
    {
        var seed = await SeedAsync();
        try
        {
            var response = await Client.SendAsync(AuthedGet($"{Url}?limit=0", userId: 1, role: "PATIENT"));
            var payload = await response.Content.ReadFromJsonAsync<PatientVisitListResponseDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(payload!.Items);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task GetMyVisits_ReceptionRole_Returns403()
    {
        var response = await Client.SendAsync(AuthedGet(Url, userId: 5, role: "RECEPTION"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpRequestMessage AuthedGet(string url, long userId, string role)
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(new User { Id = userId, Email = $"{role}@medflow.test" }, [role]).AccessToken;
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<Seed> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var staff = await Db.Staff.AsNoTracking().FirstAsync(s => s.UserId == 5);
        var hospital = await Db.Hospitals.AsNoTracking().FirstAsync(h => h.Id == staff.HospitalId);
        var room = new HospitalRoom
        {
            HospitalId = staff.HospitalId,
            RoomCode = $"PV{suffix}"[..8],
            Name = "Phòng visit list",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
        };
        Db.HospitalRooms.Add(room);
        var stranger = new Patient
        {
            PatientCode = $"PV-{suffix}",
            FullName = "Người lạ",
            Status = "ACTIVE",
        };
        Db.Patients.Add(stranger);
        await Db.SaveChangesAsync();

        var checkedInAt = new DateTime(2026, 11, 14, 9, 0, 0);
        var visit = new Visit
        {
            VisitCode = $"PVIS-{suffix}",
            PatientId = 1,
            Status = "CHECKED_IN",
            ExaminationRoomId = room.Id,
            CheckedInAt = checkedInAt,
        };
        var strangerVisit = new Visit
        {
            VisitCode = $"PVIS-S-{suffix}",
            PatientId = stranger.Id,
            Status = "CHECKED_IN",
            ExaminationRoomId = room.Id,
            CheckedInAt = checkedInAt.AddMinutes(5),
        };
        Db.Visits.AddRange(visit, strangerVisit);
        await Db.SaveChangesAsync();

        var paymentCode = $"PVPAY-{suffix}";
        Db.Payments.Add(new Payment
        {
            VisitId = visit.Id,
            Amount = 50_000m,
            Status = "PENDING",
            PaymentCode = paymentCode,
        });
        await Db.SaveChangesAsync();

        return new Seed(
            visit.Id,
            visit.VisitCode!,
            strangerVisit.Id,
            room.Id,
            stranger.Id,
            paymentCode,
            hospital.Name ?? string.Empty);
    }

    private async Task CleanupAsync(Seed seed)
    {
        Db.ChangeTracker.Clear();
        Db.Payments.RemoveRange(await Db.Payments.Where(p => p.VisitId == seed.VisitId || p.VisitId == seed.StrangerVisitId).ToListAsync());
        Db.Visits.RemoveRange(await Db.Visits.Where(v => v.Id == seed.VisitId || v.Id == seed.StrangerVisitId).ToListAsync());
        await Db.SaveChangesAsync();
        Db.HospitalRooms.RemoveRange(await Db.HospitalRooms.Where(r => r.Id == seed.RoomId).ToListAsync());
        Db.Patients.RemoveRange(await Db.Patients.Where(p => p.Id == seed.StrangerPatientId).ToListAsync());
        await Db.SaveChangesAsync();
    }

    private sealed record Seed(
        long VisitId,
        string VisitCode,
        long StrangerVisitId,
        long RoomId,
        long StrangerPatientId,
        string PaymentCode,
        string HospitalName);
}
