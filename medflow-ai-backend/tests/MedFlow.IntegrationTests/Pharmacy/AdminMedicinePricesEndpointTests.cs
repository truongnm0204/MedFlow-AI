using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Pharmacy;

public sealed class AdminMedicinePricesEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminMedicinePricesEndpointTests(TestWebAppFactory factory)
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
    public async Task GetMedicinePrices_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/medicine-prices");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    public async Task GetMedicinePrices_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(900, $"{role.ToLowerInvariant()}@medflow.test", role);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/medicine-prices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMedicinePrice_Unauthenticated_Returns401()
    {
        var payload = new UpdateMedicinePriceAdminRequest(PriceAmount: 300m);
        var response = await Client.PutAsJsonAsync("/api/v1/admin/medicines/1/prices", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMedicinePrice_InvalidNegativePrice_Returns400()
    {
        var token = await GenerateHospitalAdminTokenAsync();
        if (token is null)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/medicines/1/prices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new UpdateMedicinePriceAdminRequest(PriceAmount: -50m));

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMedicinePrice_InvalidDateRange_Returns400()
    {
        var token = await GenerateHospitalAdminTokenAsync();
        if (token is null)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/medicines/1/prices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new UpdateMedicinePriceAdminRequest(
            PriceAmount: 300m,
            ValidFrom: "2026-12-31",
            ValidTo: "2026-12-01"));

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MedicinePriceLifecycle_HospitalAdmin_ListUpdateHistoryAuditAndCrossHospital403()
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

        var tempCode = $"MED-PRC-{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        var medicine = new Medicine
        {
            HospitalId = hospitalId,
            MedicineCode = tempCode,
            Name = "Thuốc kiểm thử bảng giá #865",
            ActiveIngredient = "Paracetamol",
            Strength = "500mg",
            DosageForm = "Viên nén",
            DispensingUnit = "Viên",
            IsActive = true,
        };

        Db.Medicines.Add(medicine);
        await Db.SaveChangesAsync();

        var initialPrice = new MedicinePrice
        {
            HospitalId = hospitalId,
            MedicineId = medicine.Id,
            PriceAmount = 200m,
            EffectiveFrom = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-10), DateTimeKind.Unspecified),
            EffectiveTo = null,
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };
        Db.MedicinePrices.Add(initialPrice);
        await Db.SaveChangesAsync();

        try
        {
            using var putReq = new HttpRequestMessage(
                HttpMethod.Put,
                $"/api/v1/admin/medicines/{tempCode}/prices");
            putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            putReq.Content = JsonContent.Create(new UpdateMedicinePriceAdminRequest(
                PriceAmount: 320m,
                ValidFrom: "2026-11-25",
                DecisionNumber: "QĐ-818/BV-TC",
                AppliedBy: "Hội đồng Giá",
                Note: "Kiểm thử tích hợp cập nhật giá thuốc"));

            var putResp = await Client.SendAsync(putReq);
            Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

            var updatedDto = await putResp.Content.ReadFromJsonAsync<MedicinePriceAdminItemDto>();
            Assert.NotNull(updatedDto);
            Assert.Equal(320m, updatedDto.Price);
            Assert.Equal("QĐ-818/BV-TC", updatedDto.DecisionNumber);
            Assert.Equal("Hội đồng Giá", updatedDto.AppliedBy);
            Assert.True(updatedDto.PriceHistory.Count >= 2);

            var closedRow = await Db.MedicinePrices.AsNoTracking().FirstAsync(p => p.Id == initialPrice.Id);
            Assert.NotNull(closedRow.EffectiveTo);

            var newRow = await Db.MedicinePrices.AsNoTracking()
                .FirstAsync(p => p.MedicineId == medicine.Id && p.Id != initialPrice.Id);
            Assert.Equal(hospitalId, newRow.HospitalId);
            Assert.Equal(adminUser.Id, newRow.CreatedByUserId);

            var audit = await Db.AuditLogs.AsNoTracking().SingleAsync(a =>
                a.Action == MedicinePriceAdminService.MedicinePriceUpdatedAction &&
                a.EntityType == "MEDICINE" &&
                a.EntityId == medicine.Id.ToString());
            Assert.Equal(hospitalId, audit.HospitalId);
            using (var auditDoc = System.Text.Json.JsonDocument.Parse(audit.ChangeData!))
            {
                Assert.Equal("QĐ-818/BV-TC",
                    auditDoc.RootElement.GetProperty("decisionNumber").GetString());
            }

            using var listReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/admin/medicine-prices?keyword={tempCode}");
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var listResp = await Client.SendAsync(listReq);
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            var listDto = await listResp.Content.ReadFromJsonAsync<MedicinePriceAdminListResponseDto>();
            Assert.NotNull(listDto);
            var listed = Assert.Single(listDto.Data);
            Assert.Equal(320m, listed.Price);
            Assert.True(listed.HasEffectivePrice);

            using var histReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/admin/medicines/{medicine.Id}/prices/history");
            histReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var histResp = await Client.SendAsync(histReq);
            Assert.Equal(HttpStatusCode.OK, histResp.StatusCode);
            var historyDto = await histResp.Content.ReadFromJsonAsync<MedicinePriceHistoryResponseDto>();
            Assert.NotNull(historyDto);
            Assert.Equal(medicine.Id, historyDto.MedicineId);
            Assert.True(historyDto.PriceHistory.Count >= 2);
            Assert.Equal("QĐ-818/BV-TC", historyDto.PriceHistory[0].DecisionNumber);

            using var detailReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/admin/medicines/{medicine.Id}/prices");
            detailReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var detailResp = await Client.SendAsync(detailReq);
            Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
            var detailDto = await detailResp.Content.ReadFromJsonAsync<MedicinePriceDetailResponseDto>();
            Assert.NotNull(detailDto);
            Assert.Equal("QĐ-818/BV-TC", detailDto.DecisionNumber);

            using var crossReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/admin/medicine-prices?hospitalId={hospitalId + 999}");
            crossReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var crossResp = await Client.SendAsync(crossReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossResp.StatusCode);
        }
        finally
        {
            await Db.AuditLogs
                .Where(a => a.Action == MedicinePriceAdminService.MedicinePriceUpdatedAction &&
                            a.EntityType == "MEDICINE" &&
                            a.EntityId == medicine.Id.ToString())
                .ExecuteDeleteAsync();
            await Db.MedicinePrices.Where(p => p.MedicineId == medicine.Id).ExecuteDeleteAsync();
            await Db.Medicines.Where(m => m.Id == medicine.Id).ExecuteDeleteAsync();
        }
    }

    private async Task<string?> GenerateHospitalAdminTokenAsync()
    {
        var adminStaff = await Db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.HospitalId.HasValue && s.Status == "ACTIVE" &&
                                      Db.UserRoles.Any(ur => ur.UserId == s.UserId &&
                                                             Db.Roles.Any(r => r.Id == ur.RoleId && r.Code == "HOSPITAL_ADMIN")));

        if (adminStaff?.UserId is null)
        {
            return null;
        }

        var adminUser = await Db.Users.AsNoTracking().FirstAsync(u => u.Id == adminStaff.UserId.Value);
        return GenerateToken(adminUser.Id, adminUser.Email, "HOSPITAL_ADMIN");
    }
}
