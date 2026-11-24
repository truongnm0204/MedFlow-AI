using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Services;

public sealed class AdminServicePricesEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminServicePricesEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, roles).AccessToken;
    }

    [Fact]
    public async Task GetServicePrices_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/service-prices");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    public async Task GetServicePrices_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(900, $"{role.ToLowerInvariant()}@medflow.test", role);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/service-prices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateServicePrice_Unauthenticated_Returns401()
    {
        var payload = new UpdateServicePriceAdminRequest(PriceAmount: 300_000m);
        var response = await Client.PutAsJsonAsync("/api/v1/admin/examination-services/1/prices", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateServicePrice_InvalidNegativePrice_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/examination-services/1/prices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new UpdateServicePriceAdminRequest(PriceAmount: -50_000m));

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateServicePrice_InvalidDateRange_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-services/1/prices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new UpdateServicePriceAdminRequest(
            PriceAmount: 300_000m,
            ValidFrom: "2026-12-31",
            ValidTo: "2026-12-01"));

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ServicePriceLifecycle_HospitalAdmin_ListUpdateHistoryAndCrossHospital403()
    {
        var adminStaff = await Db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.HospitalId.HasValue && s.Status == "ACTIVE" &&
                                      Db.UserRoles.Any(ur => ur.UserId == s.UserId &&
                                                             Db.Roles.Any(r => r.Id == ur.RoleId && r.Code == "HOSPITAL_ADMIN")));

        if (adminStaff?.UserId is null || !adminStaff.HospitalId.HasValue)
        {
            return;
        }

        var adminUser = await Db.Users.AsNoTracking().FirstAsync(u => u.Id == adminStaff.UserId.Value);
        var token = GenerateToken(adminUser.Id, adminUser.Email, "HOSPITAL_ADMIN");
        var hospitalId = adminStaff.HospitalId.Value;

        var tempCode = $"EX-PRC-{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        var service = new Service
        {
            HospitalId = hospitalId,
            ServiceCode = tempCode,
            Name = "Dịch vụ kiểm thử biểu phí UC52",
            ServiceType = "EXAMINATION",
            AllowsOnlineBooking = true,
            IsActive = true,
        };

        Db.Services.Add(service);
        await Db.SaveChangesAsync();

        var initialPrice = new ServicePrice
        {
            ServiceId = service.Id,
            PriceAmount = 200_000m,
            EffectiveFrom = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-10), DateTimeKind.Unspecified),
            EffectiveTo = null,
        };
        Db.ServicePrices.Add(initialPrice);
        await Db.SaveChangesAsync();

        try
        {
            using var putReq = new HttpRequestMessage(
                HttpMethod.Put,
                $"/api/v1/admin/examination-services/{tempCode}/prices");
            putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            putReq.Content = JsonContent.Create(new UpdateServicePriceAdminRequest(
                PriceAmount: 320_000m,
                ValidFrom: "2026-11-25",
                DecisionNumber: "QĐ-818/BV-TC",
                AppliedBy: "Hội đồng Giá",
                Note: "Kiểm thử tích hợp cập nhật giá"));

            var putResp = await Client.SendAsync(putReq);
            Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

            var updatedDto = await putResp.Content.ReadFromJsonAsync<ServicePriceAdminItemDto>();
            Assert.NotNull(updatedDto);
            Assert.Equal(320_000m, updatedDto.Price);
            Assert.Equal("QĐ-818/BV-TC", updatedDto.DecisionNumber);
            Assert.True(updatedDto.PriceHistory.Count >= 2);

            var closedRow = await Db.ServicePrices.AsNoTracking().FirstAsync(p => p.Id == initialPrice.Id);
            Assert.NotNull(closedRow.EffectiveTo);

            var auditExists = await Db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.Action == ServicePriceAdminService.ServicePriceUpdatedAction &&
                a.EntityId == service.Id.ToString());
            Assert.True(auditExists);

            using var histReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/admin/examination-services/{service.Id}/prices");
            histReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var histResp = await Client.SendAsync(histReq);
            Assert.Equal(HttpStatusCode.OK, histResp.StatusCode);
            var detailDto = await histResp.Content.ReadFromJsonAsync<ServicePriceDetailResponseDto>();
            Assert.NotNull(detailDto);
            Assert.Equal("QĐ-818/BV-TC", detailDto.DecisionNumber);

            using var crossReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/admin/service-prices?hospitalId={hospitalId + 999}");
            crossReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var crossResp = await Client.SendAsync(crossReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossResp.StatusCode);
        }
        finally
        {
            await Db.AuditLogs
                .Where(a => a.Action == ServicePriceAdminService.ServicePriceUpdatedAction && a.EntityId == service.Id.ToString())
                .ExecuteDeleteAsync();
            await Db.ServicePrices.Where(p => p.ServiceId == service.Id).ExecuteDeleteAsync();
            await Db.Services.Where(s => s.Id == service.Id).ExecuteDeleteAsync();
        }
    }
}
