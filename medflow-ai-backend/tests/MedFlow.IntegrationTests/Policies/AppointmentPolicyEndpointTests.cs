using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Policies;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Policies;

public sealed class AppointmentPolicyEndpointTests : AuthTestBase
{
    private const string Endpoint = "/api/v1/admin/appointment-policy";

    public AppointmentPolicyEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private static UpdateAppointmentPolicyRequest ValidRequest(int cancelMinimumHours = 6, int bookingWindowDays = 30) => new()
    {
        BookingWindowDays = bookingWindowDays,
        BookingLeadHours = 2,
        MaxActiveBookings = 3,
        RescheduleMinimumHours = 12,
        MaxReschedules = 2,
        CancelMinimumHours = cancelMinimumHours,
        NoShowGraceMinutes = 15,
        NoShowMaxCount = 3,
        NoShowLookbackDays = 90,
        NoShowBlockDays = 30,
    };

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var response = await Client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("patient01@medflow.test")]
    [InlineData("system_admin01@medflow.test")]
    public async Task Get_ForbiddenRole_Returns403(string email)
    {
        var tokens = await LoginAsync(email, "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_HospitalAdmin_Returns200WithSeededAndDefaultPolicy()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AppointmentPolicyDto>();
        Assert.NotNull(dto);
        Assert.Equal(12, dto.RescheduleMinimumHours);
        Assert.Equal(6, dto.CancelMinimumHours);
        Assert.Equal(3, dto.MaxActiveBookings);
        Assert.Equal(30, dto.BookingWindowDays);
        Assert.Equal(3, dto.NoShowMaxCount);
    }

    [Fact]
    public async Task Put_HospitalAdmin_UpdatesChangedKeyAndCreatesNewVersionAndAudit()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");

        var maxPolicyId = await Db.HospitalPolicies
            .AsNoTracking()
            .MaxAsync(p => (long?)p.Id) ?? 0L;
        var maxAuditId = await Db.AuditLogs
            .AsNoTracking()
            .MaxAsync(a => (long?)a.Id) ?? 0L;
        var activeIds = await Db.HospitalPolicies
            .AsNoTracking()
            .Where(p => p.HospitalId == 1 && p.PolicyType == "APPOINTMENT" && p.EffectiveTo == null)
            .Select(p => p.Id)
            .ToListAsync();

        try
        {
            using var putReq = new HttpRequestMessage(HttpMethod.Put, Endpoint)
            {
                Content = JsonContent.Create(ValidRequest(cancelMinimumHours: 8)),
            };
            putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var putRes = await Client.SendAsync(putReq);
            Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

            var updatedDto = await putRes.Content.ReadFromJsonAsync<AppointmentPolicyDto>();
            Assert.NotNull(updatedDto);
            Assert.Equal(8, updatedDto.CancelMinimumHours);

            using var getReq = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var getRes = await Client.SendAsync(getReq);
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
            var getDto = await getRes.Content.ReadFromJsonAsync<AppointmentPolicyDto>();
            Assert.NotNull(getDto);
            Assert.Equal(8, getDto.CancelMinimumHours);

            var activeCancelRows = await Db.HospitalPolicies
                .AsNoTracking()
                .Where(p => p.HospitalId == 1
                    && p.PolicyType == "APPOINTMENT"
                    && p.PolicyKey == "cancel_hours"
                    && p.EffectiveTo == null)
                .ToListAsync();
            var activeCancel = Assert.Single(activeCancelRows);
            Assert.Equal(2, activeCancel.PolicyVersion);

            var oldSeedCancel = await Db.HospitalPolicies
                .AsNoTracking()
                .FirstAsync(p => p.Id == 2);
            Assert.NotNull(oldSeedCancel.EffectiveTo);

            var auditExists = await Db.AuditLogs
                .AsNoTracking()
                .AnyAsync(a => a.Id > maxAuditId
                    && a.Action == "APPOINTMENT_POLICY_UPDATE"
                    && a.ActorUserId == 11
                    && a.EntityId == "1");
            Assert.True(auditExists);
        }
        finally
        {
            await Db.HospitalPolicies
                .Where(p => p.Id > maxPolicyId)
                .ExecuteDeleteAsync();

            await Db.HospitalPolicies
                .Where(p => activeIds.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EffectiveTo, (DateTime?)null));
        }
    }

    [Fact]
    public async Task Put_InvalidPayload_Returns400ProblemDetailsAndPersistsNothing()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");
        var countBefore = await Db.HospitalPolicies.AsNoTracking().CountAsync();

        using var request = new HttpRequestMessage(HttpMethod.Put, Endpoint)
        {
            Content = JsonContent.Create(ValidRequest(bookingWindowDays: 0)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);

        var countAfter = await Db.HospitalPolicies.AsNoTracking().CountAsync();
        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task Put_PatientRole_Returns403AndPersistsNothing()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
        var countBefore = await Db.HospitalPolicies.AsNoTracking().CountAsync();

        using var request = new HttpRequestMessage(HttpMethod.Put, Endpoint)
        {
            Content = JsonContent.Create(ValidRequest(cancelMinimumHours: 8)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var countAfter = await Db.HospitalPolicies.AsNoTracking().CountAsync();
        Assert.Equal(countBefore, countAfter);
    }
}
