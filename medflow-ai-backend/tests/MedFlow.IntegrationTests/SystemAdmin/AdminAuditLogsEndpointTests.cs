using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminAuditLogsEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminAuditLogsEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    private HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string uri,
        long userId,
        string email,
        params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(
            new User { Id = userId, Email = email },
            roles).AccessToken;

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task GetAuditLogs_Unauthenticated_Returns401OnPrimaryAndAliasRoutes()
    {
        var primaryRes = await Client.GetAsync("/api/v1/admin/audit-logs");
        var aliasRes = await Client.GetAsync("/api/v1/system-admin/audit-logs");
        var detailRes = await Client.GetAsync("/api/v1/admin/audit-logs/1");

        Assert.Equal(HttpStatusCode.Unauthorized, primaryRes.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, aliasRes.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detailRes.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    public async Task GetAuditLogs_ForbiddenRoles_Returns403(string role)
    {
        using var req = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/admin/audit-logs",
            3,
            "doctor01@medflow.test",
            role);

        var res = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GetAuditLogs_ForgedSystemAdminJwtClaim_Returns403()
    {
        // User 1 in seed DB is patient01@medflow.test (PATIENT only)
        using var req = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/admin/audit-logs",
            1,
            "patient01@medflow.test",
            RoleNames.SystemAdmin);

        var res = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/audit-logs?page=0")]
    [InlineData("/api/v1/admin/audit-logs?pageSize=101")]
    [InlineData("/api/v1/admin/audit-logs?hospitalId=0")]
    [InlineData("/api/v1/admin/audit-logs?fromDate=2026-12-10&toDate=2026-12-01")]
    [InlineData("/api/v1/admin/audit-logs/0")]
    public async Task GetAuditLogs_InvalidParameters_Returns400ProblemDetails(string uri)
    {
        using var req = AuthorizedRequest(
            HttpMethod.Get,
            uri,
            13,
            "system_admin01@medflow.test",
            RoleNames.SystemAdmin);

        var res = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task GetAuditLogs_HospitalAdminWithoutAuditReadPermission_Returns403()
    {
        // By default in seed 003, HOSPITAL_ADMIN (role 6) does not have AUDIT_READ (permission 13).
        using var req = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/admin/audit-logs",
            11,
            "hospital_admin01@medflow.test",
            RoleNames.HospitalAdmin);

        var res = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GetAuditLogsAndDetail_HospitalIsolationUtcPlus7FilterAndSecretMasking_Returns200And403()
    {
        long? createdHospital2Id = null;
        var createdAuditIds = new List<long>();
        var addedHospitalAdminAuditPerm = false;

        try
        {
            var existingHosp2 = await Db.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.Id == 2);
            long hosp2Id;
            if (existingHosp2 is not null)
            {
                hosp2Id = existingHosp2.Id;
            }
            else
            {
                var tempHosp = new Hospital
                {
                    HospitalCode = $"HOS-T820-{Guid.NewGuid():N}"[..16],
                    Name = "Bệnh viện Kiểm thử Cơ sở 2",
                    Address = "Địa chỉ kiểm thử",
                    IsActive = true,
                };
                Db.Hospitals.Add(tempHosp);
                await Db.SaveChangesAsync();
                createdHospital2Id = tempHosp.Id;
                hosp2Id = tempHosp.Id;
            }

            // 2026-08-14 17:30 UTC == 2026-08-15 00:30 Asia/Ho_Chi_Minh (UTC+7)
            var uniqueEntityType = $"INTEGRATION_CFG_{Guid.NewGuid():N}"[..28];
            var logHosp1 = new AuditLog
            {
                ActorUserId = 13,
                HospitalId = 1,
                Action = "TEST_AUDIT_HOSP1_820",
                EntityType = uniqueEntityType,
                EntityId = "1:VIETQR_SEPAY",
                Reason = "Kiểm thử che khóa Bearer super-secret-bearer-820",
                ChangeData = "{\"webhookApiKey\":\"raw-sepay-secret-820\",\"password\":\"raw-pass-820\",\"safeField\":\"VISIBLE_OK\"}",
                OccurredAt = new DateTime(2026, 8, 14, 17, 30, 0, DateTimeKind.Utc),
            };

            var logHosp2 = new AuditLog
            {
                ActorUserId = 13,
                HospitalId = hosp2Id,
                Action = "TEST_AUDIT_HOSP2_820",
                EntityType = uniqueEntityType,
                EntityId = $"{hosp2Id}:SMTP_EMAIL",
                Reason = "Kiểm thử cô lập cơ sở 2",
                ChangeData = "{\"smtpPassword\":\"raw-smtp-820\"}",
                OccurredAt = new DateTime(2026, 8, 14, 17, 45, 0, DateTimeKind.Utc),
            };

            Db.AuditLogs.AddRange(logHosp1, logHosp2);
            await Db.SaveChangesAsync();
            createdAuditIds.Add(logHosp1.Id);
            createdAuditIds.Add(logHosp2.Id);

            // 1. SYSTEM_ADMIN sees both logs on 2026-08-15 (HospitalClock UTC+7) and secrets are masked
            using var sysListReq = AuthorizedRequest(
                HttpMethod.Get,
                $"/api/v1/system-admin/audit-logs?fromDate=2026-08-15&toDate=2026-08-15&entityType={uniqueEntityType}",
                13,
                "system_admin01@medflow.test",
                RoleNames.SystemAdmin);

            var sysListRes = await Client.SendAsync(sysListReq);
            Assert.Equal(HttpStatusCode.OK, sysListRes.StatusCode);

            var rawBody = await sysListRes.Content.ReadAsStringAsync();
            Assert.DoesNotContain("raw-sepay-secret-820", rawBody, StringComparison.Ordinal);
            Assert.DoesNotContain("raw-pass-820", rawBody, StringComparison.Ordinal);
            Assert.DoesNotContain("raw-smtp-820", rawBody, StringComparison.Ordinal);
            Assert.DoesNotContain("super-secret-bearer-820", rawBody, StringComparison.Ordinal);
            Assert.Contains("VISIBLE_OK", rawBody, StringComparison.Ordinal);

            var listDto = await sysListRes.Content.ReadFromJsonAsync<AuditLogListResponseDto>();
            Assert.NotNull(listDto);
            Assert.Contains(listDto.Items, i => i.Id == logHosp1.Id);
            Assert.Contains(listDto.Items, i => i.Id == logHosp2.Id);

            // 2. Grant AUDIT_READ to HOSPITAL_ADMIN temporarily and verify hospital isolation
            var roleIdHospAdmin = await Db.Roles.AsNoTracking()
                .Where(r => r.Code == RoleNames.HospitalAdmin)
                .Select(r => r.Id)
                .FirstAsync();
            var permIdAuditRead = await Db.Permissions.AsNoTracking()
                .Where(p => p.Code == PermissionCodes.AuditRead)
                .Select(p => p.Id)
                .FirstAsync();

            if (!await Db.RolePermissions.AsNoTracking()
                .AnyAsync(rp => rp.RoleId == roleIdHospAdmin && rp.PermissionId == permIdAuditRead))
            {
                Db.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleIdHospAdmin,
                    PermissionId = permIdAuditRead,
                });
                await Db.SaveChangesAsync();
                addedHospitalAdminAuditPerm = true;
            }

            using var hospListReq = AuthorizedRequest(
                HttpMethod.Get,
                $"/api/v1/admin/audit-logs?fromDate=2026-08-15&toDate=2026-08-15&entityType={uniqueEntityType}",
                11,
                "hospital_admin01@medflow.test",
                RoleNames.HospitalAdmin);

            var hospListRes = await Client.SendAsync(hospListReq);
            Assert.Equal(HttpStatusCode.OK, hospListRes.StatusCode);
            var hospListDto = await hospListRes.Content.ReadFromJsonAsync<AuditLogListResponseDto>();
            Assert.NotNull(hospListDto);
            Assert.Contains(hospListDto.Items, i => i.Id == logHosp1.Id);
            Assert.DoesNotContain(hospListDto.Items, i => i.Id == logHosp2.Id);
            Assert.All(hospListDto.Items, i => Assert.Equal(1L, i.HospitalId));

            // Cross-hospital query filter -> 403
            using var crossFilterReq = AuthorizedRequest(
                HttpMethod.Get,
                $"/api/v1/admin/audit-logs?hospitalId={hosp2Id}",
                11,
                "hospital_admin01@medflow.test",
                RoleNames.HospitalAdmin);
            var crossFilterRes = await Client.SendAsync(crossFilterReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossFilterRes.StatusCode);

            // Cross-hospital detail -> 403
            using var crossDetailReq = AuthorizedRequest(
                HttpMethod.Get,
                $"/api/v1/admin/audit-logs/{logHosp2.Id}",
                11,
                "hospital_admin01@medflow.test",
                RoleNames.HospitalAdmin);
            var crossDetailRes = await Client.SendAsync(crossDetailReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossDetailRes.StatusCode);

            // Own hospital detail -> 200 with redacted fields
            using var ownDetailReq = AuthorizedRequest(
                HttpMethod.Get,
                $"/api/v1/admin/audit-logs/{logHosp1.Id}",
                11,
                "hospital_admin01@medflow.test",
                RoleNames.HospitalAdmin);
            var ownDetailRes = await Client.SendAsync(ownDetailReq);
            Assert.Equal(HttpStatusCode.OK, ownDetailRes.StatusCode);
            var detailDto = await ownDetailRes.Content.ReadFromJsonAsync<AuditLogDetailDto>();
            Assert.NotNull(detailDto);
            Assert.Contains("webhookApiKey", detailDto.RedactedFields);
            Assert.Contains("password", detailDto.RedactedFields);
        }
        finally
        {
            if (addedHospitalAdminAuditPerm)
            {
                var roleIdHospAdmin = await Db.Roles.AsNoTracking()
                    .Where(r => r.Code == RoleNames.HospitalAdmin)
                    .Select(r => r.Id)
                    .FirstAsync();
                var permIdAuditRead = await Db.Permissions.AsNoTracking()
                    .Where(p => p.Code == PermissionCodes.AuditRead)
                    .Select(p => p.Id)
                    .FirstAsync();

                await Db.RolePermissions
                    .Where(rp => rp.RoleId == roleIdHospAdmin && rp.PermissionId == permIdAuditRead)
                    .ExecuteDeleteAsync();
            }

            if (createdAuditIds.Count > 0)
            {
                await Db.AuditLogs
                    .Where(a => createdAuditIds.Contains(a.Id))
                    .ExecuteDeleteAsync();
            }

            if (createdHospital2Id.HasValue)
            {
                await Db.Hospitals
                    .Where(h => h.Id == createdHospital2Id.Value)
                    .ExecuteDeleteAsync();
            }
        }
    }
}
