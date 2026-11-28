using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Pharmacy;

public sealed class PrepareMedicinesEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public PrepareMedicinesEndpointTests(TestWebAppFactory factory)
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
    public async Task GetPreparationContext_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/pharmacy/prescriptions/RX-001/prepare");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPreparationContext_RouteAlias_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/pharmacy/prepare/RX-001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetPreparationContext_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/RX-001/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PreparePrescription_Unauthenticated_Returns401()
    {
        var requestPayload = new PreparePrescriptionRequest(Items: []);
        var response = await Client.PostAsJsonAsync("/api/v1/pharmacy/prescriptions/RX-001/prepare", requestPayload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    public async Task PreparePrescription_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pharmacy/prescriptions/RX-001/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new PreparePrescriptionRequest(Items: []));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PreparePrescription_EmptyItems_Returns400()
    {
        var token = GenerateToken(9, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pharmacy/prescriptions/RX-001/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new PreparePrescriptionRequest(Items: []));

        var response = await Client.SendAsync(request);

        // ValidationException is mapped to 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPreparationContext_TooLongIdOrCode_Returns400()
    {
        var token = GenerateToken(9, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");
        var tooLongCode = new string('A', 101);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/pharmacy/prescriptions/{tooLongCode}/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPreparationContext_NonExistentPrescription_Returns404OrDbUnavailable()
    {
        var token = GenerateToken(9, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/NON-EXISTENT-RX-9999/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
            $"Unexpected status code: {response.StatusCode}");
    }
}

/// <summary>
/// BR-BF03-03: soạn thuốc phải khớp 100% số lượng kê đơn — ShortageReason không còn là "vé" cho soạn thiếu.
/// In-memory endpoint tests (independent of a local PostgreSQL instance); reuses the pharmacy DbContext
/// shape from <see cref="ConfirmMedicineDispensingEndpointTests"/>.
/// </summary>
public sealed class PrepareMedicinesNoPartialEndpointTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    private const string PrescriptionCode = "RX-864-0001";

    private static readonly DateTime FixedNowUtc = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

    private readonly WebApplicationFactory<Program> _customFactory;
    private readonly HttpClient _client;

    public PrepareMedicinesNoPartialEndpointTests(TestWebAppFactory factory)
    {
        var dbOptions = new DbContextOptionsBuilder<ConfirmMedicineDispensingEndpointTests.IntegrationPharmacyDbContext>()
            .UseInMemoryDatabase($"PrepareMedicinesNoPartialTests_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using (var seedCtx = new ConfirmMedicineDispensingEndpointTests.IntegrationPharmacyDbContext(dbOptions))
        {
            SeedData(seedCtx);
        }

        _customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationDbContext>();
                services.AddScoped<IApplicationDbContext>(
                    _ => new ConfirmMedicineDispensingEndpointTests.IntegrationPharmacyDbContext(dbOptions));
            });
        });

        _client = _customFactory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _customFactory.Dispose();
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = _customFactory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, roles).AccessToken;
    }

    private static void SeedData(ConfirmMedicineDispensingEndpointTests.IntegrationPharmacyDbContext db)
    {
        db.Hospitals.Add(new Hospital { Id = 1, Name = "Bệnh viện Đa khoa Quốc tế MedFlow", IsActive = true });
        db.Departments.Add(new Department { Id = 1, HospitalId = 1, Name = "Khoa Dược", DepartmentCode = "PHA", IsActive = true });
        db.HospitalRooms.Add(new HospitalRoom { Id = 1, HospitalId = 1, DepartmentId = 1, RoomCode = "PHA-01", Name = "Quầy phát thuốc 01", RoomType = "PHARMACY", Status = "ACTIVE" });

        db.Roles.Add(new Role { Id = 1, Code = "PHARMACY_STAFF", Name = "Dược sĩ" });
        db.Permissions.Add(new Permission { Id = 1, Code = "MEDICINE_DISPENSE" });
        db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });

        db.Users.Add(new User { Id = 10, Email = "pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        db.UserRoles.Add(new UserRole { UserId = 10, RoleId = 1 });
        db.Staff.Add(new Staff { Id = 10, UserId = 10, HospitalId = 1, DepartmentId = 1, FullName = "Đỗ Thị Lan Anh", Status = "ACTIVE" });

        db.Patients.Add(new Patient { Id = 100, PatientCode = "PAT-0864", FullName = "Nguyễn Văn An", Gender = "Nam", Status = "ACTIVE" });

        db.Medicines.Add(new Medicine
        {
            Id = 1,
            HospitalId = 1,
            MedicineCode = "MED-864",
            Name = "Bismuth subsalicylate 300mg",
            DosageForm = "Viên nhai",
            DispensingUnit = "viên",
            IsActive = true
        });

        db.MedicineBatches.AddRange(
            new MedicineBatch
            {
                Id = 1,
                MedicineId = 1,
                BatchNumber = "LO-864-A",
                ExpiryDate = new DateOnly(2028, 6, 30),
                QuantityOnHand = 100m,
                IsBlocked = false
            },
            new MedicineBatch
            {
                Id = 2,
                MedicineId = 1,
                BatchNumber = "LO-864-LOW",
                ExpiryDate = new DateOnly(2028, 12, 31),
                QuantityOnHand = 10m, // < prescribed 28 → insufficient stock
                IsBlocked = false
            });

        db.Visits.Add(new Visit
        {
            Id = 501,
            VisitCode = "VIS-2026-0864",
            PatientId = 100,
            ExaminationRoomId = 1,
            Status = "COMPLETED"
        });

        db.Prescriptions.Add(new Prescription
        {
            Id = 1001,
            PrescriptionCode = PrescriptionCode,
            VisitId = 501,
            FulfillmentChoice = "HOSPITAL",
            Status = "ISSUED",
            IssuedAt = FixedNowUtc.AddMinutes(-30),
            CreatedAt = FixedNowUtc.AddMinutes(-40)
        });

        db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 1,
            PrescriptionId = 1001,
            MedicineId = 1,
            PrescribedQuantity = 28m,
            SingleDoseQuantity = 1m,
            SingleDoseUnit = "viên"
        });

        db.SaveChanges();
    }

    private async Task<HttpResponseMessage> PostPrepareAsync(PreparePrescriptionRequest payload)
    {
        var token = GenerateToken(10, "pharmacist@medflow.test", "PHARMACY_STAFF");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/pharmacy/prescriptions/{PrescriptionCode}/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);
        return await _client.SendAsync(request);
    }

    private async Task AssertPrescriptionNotPreparedAsync()
    {
        using var scope = _customFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Equal("ISSUED", (await db.Prescriptions.AsNoTracking().SingleAsync(p => p.Id == 1001)).Status);
        Assert.Equal(0, await db.MedicationDispenses.CountAsync(md => md.PrescriptionId == 1001));
    }

    [Fact]
    public async Task PreparePrescription_ShortQuantityWithShortageReason_ReturnsBadRequestAndKeepsIssued()
    {
        var response = await PostPrepareAsync(new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 1,
                    MedicineBatchId: 1,
                    BatchCode: null,
                    PickedQuantity: 14m, // Prescribed 28
                    ShortageReason: "Kho chỉ còn 14 viên")
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertPrescriptionNotPreparedAsync();
    }

    [Fact]
    public async Task PreparePrescription_ExcessQuantity_ReturnsBadRequestAndKeepsIssued()
    {
        var response = await PostPrepareAsync(new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 1,
                    MedicineBatchId: 1,
                    BatchCode: null,
                    PickedQuantity: 29m, // Prescribed 28
                    ShortageReason: "Cố tình khai lý do")
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertPrescriptionNotPreparedAsync();
    }

    [Fact]
    public async Task PreparePrescription_InsufficientBatchStock_ReturnsConflictAndKeepsIssued()
    {
        var response = await PostPrepareAsync(new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 1,
                    MedicineBatchId: 2, // On hand 10 < picked 28
                    BatchCode: null,
                    PickedQuantity: 28m)
            ]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertPrescriptionNotPreparedAsync();
    }

    [Fact]
    public async Task PreparePrescription_ExactQuantity_ReturnsOkAndTransitionsToReady()
    {
        var response = await PostPrepareAsync(new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 1,
                    MedicineBatchId: 1,
                    BatchCode: null,
                    PickedQuantity: 28m,
                    ShortageReason: "Lý do thừa phải bị bỏ qua")
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _customFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Assert.Equal("READY", (await db.Prescriptions.AsNoTracking().SingleAsync(p => p.Id == 1001)).Status);

        var dispense = await db.MedicationDispenses.AsNoTracking().SingleAsync(md => md.PrescriptionId == 1001);
        var dispenseItem = await db.DispenseItems.AsNoTracking().SingleAsync(di => di.DispenseId == dispense.Id);
        Assert.Equal(28m, dispenseItem.PlannedQuantity);
        Assert.Null(dispenseItem.ShortageReason);
    }
}
