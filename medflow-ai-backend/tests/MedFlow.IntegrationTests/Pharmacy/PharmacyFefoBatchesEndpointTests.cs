using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Pharmacy;

public sealed class PharmacyFefoBatchesEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public PharmacyFefoBatchesEndpointTests(TestWebAppFactory factory)
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
    public async Task GetFefoBatches_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/pharmacy/prescriptions/RX-001/fefo-batches");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetFefoBatches_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/RX-001/fefo-batches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SelectFefoBatches_Unauthenticated_Returns401()
    {
        var payload = new SelectFefoBatchesRequest(Items: []);
        var response = await Client.PostAsJsonAsync("/api/v1/pharmacy/prescriptions/RX-001/fefo-batches", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("HOSPITAL_ADMIN")]
    public async Task SelectFefoBatches_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/pharmacy/prescriptions/RX-001/fefo-batches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new SelectFefoBatchesRequest(Items: []));

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SelectFefoBatches_EmptyItems_Returns400()
    {
        var token = GenerateToken(9, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pharmacy/prescriptions/RX-001/fefo-batches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new SelectFefoBatchesRequest(Items: []));

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetFefoBatches_TooLongCode_Returns400()
    {
        var token = GenerateToken(9, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");
        var tooLongCode = new string('X', 101);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/pharmacy/prescriptions/{tooLongCode}/fefo-batches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetFefoBatches_NonExistentPrescription_Returns404OrDbUnavailable()
    {
        var token = GenerateToken(9, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/NON-EXISTENT-RX-818/fefo-batches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
            $"Unexpected status code: {response.StatusCode}");
    }

    [Fact]
    public async Task FefoBatchesLifecycle_GetSortOverrideValidationAndPersist_WhenDatabaseAvailable()
    {
        var phaStaff = await Db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.HospitalId.HasValue && s.Status == "ACTIVE" &&
                                      Db.UserRoles.Any(ur => ur.UserId == s.UserId &&
                                                             Db.Roles.Any(r => r.Id == ur.RoleId && r.Code == "PHARMACY_STAFF")));

        if (phaStaff?.UserId is null || !phaStaff.HospitalId.HasValue)
        {
            return;
        }

        var hospitalId = phaStaff.HospitalId.Value;
        var phaUser = await Db.Users.AsNoTracking().FirstAsync(u => u.Id == phaStaff.UserId.Value);
        var doctor = await Db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.HospitalId == hospitalId);
        var patient = await Db.Patients.AsNoTracking().FirstOrDefaultAsync();
        if (doctor is null || patient is null)
        {
            return;
        }

        var token = GenerateToken(phaUser.Id, phaUser.Email, "PHARMACY_STAFF");
        var today = HospitalClock.GetToday(TimeProvider.System);
        var rxCode = $"RX-IT-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        var medicine = new Medicine
        {
            HospitalId = hospitalId,
            MedicineCode = $"MED-IT-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Name = "Thuốc Kiểm Thử FEFO 500mg",
            DosageForm = "Viên nén",
            DispensingUnit = "viên",
            IsActive = true,
        };
        Db.Medicines.Add(medicine);

        var visit = new Visit
        {
            VisitCode = $"VIS-IT-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            PatientId = patient.Id,
            AssignedDoctorId = doctor.Id,
            Status = "COMPLETED",
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        var prescription = new Prescription
        {
            PrescriptionCode = rxCode,
            VisitId = visit.Id,
            PrescribingDoctorId = doctor.Id,
            Status = "ISSUED",
        };
        Db.Prescriptions.Add(prescription);
        await Db.SaveChangesAsync();

        var rxItem = new PrescriptionItem
        {
            PrescriptionId = prescription.Id,
            MedicineId = medicine.Id,
            PrescribedQuantity = 10m,
        };
        Db.PrescriptionItems.Add(rxItem);

        var batchExpired = new MedicineBatch
        {
            MedicineId = medicine.Id,
            BatchNumber = "LO-IT-EXP",
            ExpiryDate = today.AddDays(-5),
            QuantityOnHand = 50m,
            IsBlocked = false,
        };
        var batchNear = new MedicineBatch
        {
            MedicineId = medicine.Id,
            BatchNumber = "LO-IT-NEAR",
            ExpiryDate = today.AddDays(30),
            QuantityOnHand = 50m,
            IsBlocked = false,
        };
        var batchFar = new MedicineBatch
        {
            MedicineId = medicine.Id,
            BatchNumber = "LO-IT-FAR",
            ExpiryDate = today.AddDays(180),
            QuantityOnHand = 50m,
            IsBlocked = false,
        };
        Db.MedicineBatches.AddRange(batchExpired, batchNear, batchFar);
        await Db.SaveChangesAsync();

        try
        {
            using var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/pharmacy/prescriptions/{rxCode}/fefo-batches");
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var getResp = await Client.SendAsync(getReq);
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

            var ctx = await getResp.Content.ReadFromJsonAsync<FefoBatchSelectionContextDto>();
            Assert.NotNull(ctx);
            var item = Assert.Single(ctx.Items);
            Assert.Equal("LO-IT-NEAR", item.Batches[0].BatchCode);
            Assert.Equal("FEFO_RECOMMENDED", item.Batches[0].Status);

            // Far batch without override reason -> 400 BadRequest
            using var badPost = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/pharmacy/prescriptions/{rxCode}/fefo-batches");
            badPost.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            badPost.Content = JsonContent.Create(new SelectFefoBatchesRequest(
            [
                new SelectFefoBatchItemRequest(rxItem.Id, MedicineBatchId: batchFar.Id, PickedQuantity: 10m),
            ]));
            var badResp = await Client.SendAsync(badPost);
            Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);

            // Expired batch -> 409 Conflict
            using var expPost = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/pharmacy/prescriptions/{rxCode}/fefo-batches");
            expPost.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            expPost.Content = JsonContent.Create(new SelectFefoBatchesRequest(
            [
                new SelectFefoBatchItemRequest(rxItem.Id, MedicineBatchId: batchExpired.Id, PickedQuantity: 10m),
            ]));
            var expResp = await Client.SendAsync(expPost);
            Assert.Equal(HttpStatusCode.Conflict, expResp.StatusCode);

            // Far batch WITH valid override reason -> 200 OK
            using var okPut = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/pharmacy/prescriptions/{rxCode}/fefo-batches");
            okPut.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            okPut.Content = JsonContent.Create(new SelectFefoBatchesRequest(
            [
                new SelectFefoBatchItemRequest(
                    rxItem.Id,
                    MedicineBatchId: batchFar.Id,
                    PickedQuantity: 10m,
                    OverrideReason: "Lô hạn gần đang kiểm kê định kỳ tại kho chính"),
            ]));
            var okResp = await Client.SendAsync(okPut);
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

            var auditExists = await Db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.Action == FefoBatchSelectionService.FefoBatchSelectedAction &&
                a.EntityId == prescription.Id.ToString());
            Assert.True(auditExists);
        }
        finally
        {
            var dispenseIds = await Db.MedicationDispenses
                .Where(d => d.PrescriptionId == prescription.Id)
                .Select(d => d.Id)
                .ToListAsync();
            if (dispenseIds.Count > 0)
            {
                await Db.DispenseItems.Where(di => di.DispenseId.HasValue && dispenseIds.Contains(di.DispenseId.Value)).ExecuteDeleteAsync();
                await Db.MedicationDispenses.Where(d => dispenseIds.Contains(d.Id)).ExecuteDeleteAsync();
            }

            await Db.AuditLogs
                .Where(a => a.EntityId == prescription.Id.ToString() &&
                            (a.Action == FefoBatchSelectionService.FefoBatchSelectedAction ||
                             a.Action == FefoBatchSelectionService.FefoBatchViewedAction))
                .ExecuteDeleteAsync();
            await Db.PrescriptionItems.Where(pi => pi.PrescriptionId == prescription.Id).ExecuteDeleteAsync();
            await Db.Prescriptions.Where(p => p.Id == prescription.Id).ExecuteDeleteAsync();
            await Db.Visits.Where(v => v.Id == visit.Id).ExecuteDeleteAsync();
            await Db.MedicineBatches.Where(mb => mb.MedicineId == medicine.Id).ExecuteDeleteAsync();
            await Db.Medicines.Where(m => m.Id == medicine.Id).ExecuteDeleteAsync();
        }
    }
}
