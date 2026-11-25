using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminIntegrationsEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminIntegrationsEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    private HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string uri,
        long userId,
        string email,
        object? jsonBody = null,
        params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(
            new User { Id = userId, Email = email },
            roles).AccessToken;

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody);
        }

        return request;
    }

    [Fact]
    public async Task Integrations_Unauthenticated_Returns401()
    {
        var getPrimary = await Client.GetAsync("/api/v1/admin/integrations");
        var getAlias = await Client.GetAsync("/api/v1/system-admin/integrations");
        var putPrimary = await Client.PutAsJsonAsync(
            "/api/v1/admin/integrations/smtp-email",
            new UpdateIntegrationConfigRequest { Enabled = true });

        Assert.Equal(HttpStatusCode.Unauthorized, getPrimary.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, getAlias.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, putPrimary.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    public async Task Integrations_NonAdminRoles_Returns403(string role)
    {
        using var getReq = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/admin/integrations",
            3,
            "doctor01@medflow.test",
            null,
            role);
        var getRes = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);

        using var putReq = AuthorizedRequest(
            HttpMethod.Put,
            "/api/v1/admin/integrations/smtp-email",
            3,
            "doctor01@medflow.test",
            new UpdateIntegrationConfigRequest { Enabled = true },
            role);
        var putRes = await Client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.Forbidden, putRes.StatusCode);
    }

    [Fact]
    public async Task Integrations_ForgedSystemAdminClaim_Returns403()
    {
        using var getReq = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/admin/integrations",
            1,
            "patient01@medflow.test",
            null,
            RoleNames.SystemAdmin);
        var getRes = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);
    }

    [Fact]
    public async Task GetIntegrations_MasksConfiguredRuntimeSecretsAndNeverLeaksRawKeys()
    {
        using var req = AuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/admin/integrations",
            13,
            "system_admin01@medflow.test",
            null,
            RoleNames.SystemAdmin);

        var res = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var rawBody = await res.Content.ReadAsStringAsync();
        // appsettings.Test.json configures SePay:WebhookApiKey = "IntegrationTest_SePay_Key"
        Assert.DoesNotContain("IntegrationTest_SePay_Key", rawBody, StringComparison.Ordinal);

        var dto = await res.Content.ReadFromJsonAsync<IntegrationConfigListResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(3, dto.Integrations.Count);

        var vietQr = dto.Integrations.Single(i => i.ProviderKey == IntegrationConfigService.ProviderVietQrSePay);
        Assert.True(vietQr.SecretStatus.IsConfigured);
        Assert.Equal("••••••••••••", vietQr.SecretStatus.MaskedValue);
    }

    [Fact]
    public async Task PutIntegration_InvalidPayloadOrSecretInBody_Returns400AndPersistsNothing()
    {
        var policyCountBefore = await Db.HospitalPolicies.AsNoTracking().CountAsync();

        // 1. Raw secret submission rejected
        using var secretReq = AuthorizedRequest(
            HttpMethod.Put,
            "/api/v1/admin/integrations/ai-provider",
            13,
            "system_admin01@medflow.test",
            new UpdateIntegrationConfigRequest
            {
                HospitalId = 1,
                Enabled = true,
                ApiKey = "sk-should-be-blocked",
            },
            RoleNames.SystemAdmin);
        var secretRes = await Client.SendAsync(secretReq);
        Assert.Equal(HttpStatusCode.BadRequest, secretRes.StatusCode);

        // 2. Invalid VietQR BIN rejected
        using var badBinReq = AuthorizedRequest(
            HttpMethod.Put,
            "/api/v1/admin/integrations/vietqr-sepay",
            11,
            "hospital_admin01@medflow.test",
            new UpdateIntegrationConfigRequest
            {
                Enabled = true,
                BankBin = "97",
                AccountNumber = "0123456789",
                AccountName = "BENH VIEN MEDFLOW",
            },
            RoleNames.HospitalAdmin);
        var badBinRes = await Client.SendAsync(badBinReq);
        Assert.Equal(HttpStatusCode.BadRequest, badBinRes.StatusCode);
        var problem = await badBinRes.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);

        var policyCountAfter = await Db.HospitalPolicies.AsNoTracking().CountAsync();
        Assert.Equal(policyCountBefore, policyCountAfter);
    }

    [Fact]
    public async Task PutIntegration_PersistsHospitalPolicyAuditLogAndEnforcesHospitalIsolation()
    {
        var maxPolicyId = await Db.HospitalPolicies.AsNoTracking().MaxAsync(p => (long?)p.Id) ?? 0L;
        var maxAuditId = await Db.AuditLogs.AsNoTracking().MaxAsync(a => (long?)a.Id) ?? 0L;
        var previousPaymentActiveIds = await Db.HospitalPolicies.AsNoTracking()
            .Where(p => p.HospitalId == 1 && p.PolicyType == "PAYMENT" && p.PolicyKey == "vietqr_account" && p.EffectiveTo == null)
            .Select(p => p.Id)
            .ToListAsync();
        long? createdHospital2Id = null;

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
                    HospitalCode = $"HOS-I820-{Guid.NewGuid():N}"[..16],
                    Name = "Bệnh viện Tích hợp Cơ sở 2",
                    Address = "Địa chỉ kiểm thử",
                    IsActive = true,
                };
                Db.Hospitals.Add(tempHosp);
                await Db.SaveChangesAsync();
                createdHospital2Id = tempHosp.Id;
                hosp2Id = tempHosp.Id;
            }

            // Hospital Admin 1 updates VIETQR_SEPAY for own Hospital 1 -> 200 OK
            using var putVietQrReq = AuthorizedRequest(
                HttpMethod.Put,
                "/api/v1/admin/integrations/vietqr-sepay",
                11,
                "hospital_admin01@medflow.test",
                new UpdateIntegrationConfigRequest
                {
                    Enabled = true,
                    BankBin = "970422",
                    AccountNumber = "0987654321",
                    AccountName = "BENH VIEN MEDFLOW HA NOI",
                    TransferPrefix = "MFHN",
                    PendingExpiryMinutes = 20,
                    Reason = "Cập nhật tài khoản VietQR Cơ sở 1",
                },
                RoleNames.HospitalAdmin);

            var putVietQrRes = await Client.SendAsync(putVietQrReq);
            Assert.Equal(HttpStatusCode.OK, putVietQrRes.StatusCode);

            var updatedDto = await putVietQrRes.Content.ReadFromJsonAsync<IntegrationProviderItemDto>();
            Assert.NotNull(updatedDto);
            Assert.Equal("VIETQR_SEPAY", updatedDto.ProviderKey);
            Assert.Equal("0987654321", updatedDto.VietQr!.AccountNumber);
            Assert.Equal(20, updatedDto.VietQr.PendingExpiryMinutes);

            var auditExists = await Db.AuditLogs.AsNoTracking()
                .AnyAsync(a => a.Id > maxAuditId &&
                               a.Action == IntegrationConfigService.AuditActionUpdated &&
                               a.HospitalId == 1 &&
                               a.ActorUserId == 11);
            Assert.True(auditExists);

            // Hospital Admin 1 attempting to read or update Hospital 2 -> 403 Forbidden
            using var crossGetReq = AuthorizedRequest(
                HttpMethod.Get,
                $"/api/v1/admin/integrations?hospitalId={hosp2Id}",
                11,
                "hospital_admin01@medflow.test",
                null,
                RoleNames.HospitalAdmin);
            var crossGetRes = await Client.SendAsync(crossGetReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossGetRes.StatusCode);

            using var crossPutReq = AuthorizedRequest(
                HttpMethod.Put,
                "/api/v1/admin/integrations/smtp-email",
                11,
                "hospital_admin01@medflow.test",
                new UpdateIntegrationConfigRequest
                {
                    HospitalId = hosp2Id,
                    Enabled = false,
                },
                RoleNames.HospitalAdmin);
            var crossPutRes = await Client.SendAsync(crossPutReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossPutRes.StatusCode);

            // System Admin updates AI_PROVIDER on Hospital 2 via alias route -> 200 OK, isolated from Hospital 1
            using var sysPutHosp2Req = AuthorizedRequest(
                HttpMethod.Put,
                "/api/v1/system-admin/integrations/ai-provider",
                13,
                "system_admin01@medflow.test",
                new UpdateIntegrationConfigRequest
                {
                    HospitalId = hosp2Id,
                    Enabled = true,
                    ProviderName = "DETERMINISTIC_CLINICAL",
                    ModelName = "clinical-rules-v1",
                    TimeoutSeconds = 10,
                    MaxSuggestions = 3,
                    DisclaimerTemplate = "Gợi ý điều hướng chuyên khoa tham khảo, không thay thế bác sĩ.",
                },
                RoleNames.SystemAdmin);
            var sysPutHosp2Res = await Client.SendAsync(sysPutHosp2Req);
            Assert.Equal(HttpStatusCode.OK, sysPutHosp2Res.StatusCode);

            using var getHosp1Req = AuthorizedRequest(
                HttpMethod.Get,
                "/api/v1/admin/integrations",
                11,
                "hospital_admin01@medflow.test",
                null,
                RoleNames.HospitalAdmin);
            var getHosp1Res = await Client.SendAsync(getHosp1Req);
            var hosp1Dto = await getHosp1Res.Content.ReadFromJsonAsync<IntegrationConfigListResponseDto>();
            Assert.NotNull(hosp1Dto);
            var hosp1Ai = hosp1Dto.Integrations.Single(i => i.ProviderKey == "AI_PROVIDER");
            Assert.Equal("GEMINI", hosp1Ai.Ai!.ProviderName);
        }
        finally
        {
            await Db.AuditLogs
                .Where(a => a.Id > maxAuditId)
                .ExecuteDeleteAsync();

            await Db.HospitalPolicies
                .Where(p => p.Id > maxPolicyId)
                .ExecuteDeleteAsync();

            if (previousPaymentActiveIds.Count > 0)
            {
                await Db.HospitalPolicies
                    .Where(p => previousPaymentActiveIds.Contains(p.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.EffectiveTo, (DateTime?)null));
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
