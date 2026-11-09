using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Policies;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Policies;

public sealed class QueuePolicyEndpointTests : AuthTestBase
{
    private const string Endpoint = "/api/v1/admin/queue-policy";

    public QueuePolicyEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private static UpdateQueuePolicyRequest ValidRequest(
        int graceMinutes = 15,
        int expectedDurationMinutes = 20,
        bool publicNumberImmutable = true)
    {
        return new UpdateQueuePolicyRequest
        {
            GraceMinutes = graceMinutes,
            ExpectedDurationMinutes = expectedDurationMinutes,
            SafetyBufferMinutes = 5,
            WalkinPriorityRatio = new UpdateQueuePriorityRatioRequest
            {
                Priority = 3,
                Normal = 1,
            },
            PublicNumberImmutable = publicNumberImmutable,
            ManualPriorityRequiresVerification = true,
        };
    }

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
        var dto = await response.Content.ReadFromJsonAsync<QueuePolicyDto>();
        Assert.NotNull(dto);
        Assert.Equal(15, dto.GraceMinutes);
        Assert.True(dto.ExpectedDurationMinutes is 15 or 20);
        Assert.Equal(5, dto.SafetyBufferMinutes);
        Assert.Equal(3, dto.WalkinPriorityRatio.Priority);
        Assert.Equal(1, dto.WalkinPriorityRatio.Normal);
        Assert.True(dto.PublicNumberImmutable);
        Assert.True(dto.ManualPriorityRequiresVerification);
    }

    [Fact]
    public async Task Put_HospitalAdmin_UpdatesChangedKeysCreatesVersionsAndAudit()
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
            .Where(p => p.HospitalId == 1 && p.PolicyType == "QUEUE" && p.EffectiveTo == null)
            .Select(p => p.Id)
            .ToListAsync();
        var expectedBaseVersion = await Db.HospitalPolicies
            .AsNoTracking()
            .Where(p => p.HospitalId == 1
                && p.PolicyType == "QUEUE"
                && p.PolicyKey == "expected_duration_minutes")
            .MaxAsync(p => p.PolicyVersion) ?? 0;

        try
        {
            using var putReq = new HttpRequestMessage(HttpMethod.Put, Endpoint)
            {
                Content = JsonContent.Create(ValidRequest(graceMinutes: 10, expectedDurationMinutes: 25)),
            };
            putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var putRes = await Client.SendAsync(putReq);
            Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

            var updatedDto = await putRes.Content.ReadFromJsonAsync<QueuePolicyDto>();
            Assert.NotNull(updatedDto);
            Assert.Equal(10, updatedDto.GraceMinutes);
            Assert.Equal(25, updatedDto.ExpectedDurationMinutes);

            using var getReq = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var getRes = await Client.SendAsync(getReq);
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
            var getDto = await getRes.Content.ReadFromJsonAsync<QueuePolicyDto>();
            Assert.NotNull(getDto);
            Assert.Equal(10, getDto.GraceMinutes);
            Assert.Equal(25, getDto.ExpectedDurationMinutes);

            var activeGraceRows = await Db.HospitalPolicies
                .AsNoTracking()
                .Where(p => p.HospitalId == 1
                    && p.PolicyType == "QUEUE"
                    && p.PolicyKey == "grace_minutes"
                    && p.EffectiveTo == null)
                .ToListAsync();
            var activeGrace = Assert.Single(activeGraceRows);
            Assert.Equal(2, activeGrace.PolicyVersion);

            var oldSeedGrace = await Db.HospitalPolicies
                .AsNoTracking()
                .FirstAsync(p => p.Id == 4);
            Assert.NotNull(oldSeedGrace.EffectiveTo);

            var activeExpectedRows = await Db.HospitalPolicies
                .AsNoTracking()
                .Where(p => p.HospitalId == 1
                    && p.PolicyType == "QUEUE"
                    && p.PolicyKey == "expected_duration_minutes"
                    && p.EffectiveTo == null)
                .ToListAsync();
            var activeExpected = Assert.Single(activeExpectedRows);
            Assert.Equal(expectedBaseVersion + 1, activeExpected.PolicyVersion);

            var auditExists = await Db.AuditLogs
                .AsNoTracking()
                .AnyAsync(a => a.Id > maxAuditId
                    && a.Action == "QUEUE_POLICY_UPDATE"
                    && a.ActorUserId == 11
                    && a.EntityId == "1");
            Assert.True(auditExists);
        }
        finally
        {
            await Db.HospitalPolicies
                .Where(p => p.Id > maxPolicyId && p.PolicyType == "QUEUE")
                .ExecuteDeleteAsync();

            await Db.HospitalPolicies
                .Where(p => activeIds.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EffectiveTo, (DateTime?)null));
        }
    }

    [Fact]
    public async Task Put_PublicNumberImmutableFalse_Returns400ProblemDetailsAndPersistsNothing()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");
        var countBefore = await Db.HospitalPolicies.AsNoTracking().CountAsync(p => p.PolicyType == "QUEUE");

        using var request = new HttpRequestMessage(HttpMethod.Put, Endpoint)
        {
            Content = JsonContent.Create(ValidRequest(publicNumberImmutable: false)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);

        var countAfter = await Db.HospitalPolicies.AsNoTracking().CountAsync(p => p.PolicyType == "QUEUE");
        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task Put_PatientRole_Returns403AndPersistsNothing()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
        var countBefore = await Db.HospitalPolicies.AsNoTracking().CountAsync(p => p.PolicyType == "QUEUE");

        using var request = new HttpRequestMessage(HttpMethod.Put, Endpoint)
        {
            Content = JsonContent.Create(ValidRequest(graceMinutes: 10)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var countAfter = await Db.HospitalPolicies.AsNoTracking().CountAsync(p => p.PolicyType == "QUEUE");
        Assert.Equal(countBefore, countAfter);
    }
}
