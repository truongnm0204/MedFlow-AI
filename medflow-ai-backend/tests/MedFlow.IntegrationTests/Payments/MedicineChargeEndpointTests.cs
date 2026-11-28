using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Payments;

/// <summary>
/// #866 / S3b: MEDICINE charge + Cash/QR settlement at the pharmacy counter (owner decision D1).
/// Requires a database initialised with migrations 002..035 (scripts/db/init-test-db.sh).
/// </summary>
public sealed class MedicineChargeEndpointTests : AuthTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MedicineChargeEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task PharmacyCash_CollectsMedicineCharge_AndAllocatesPaid()
    {
        var seed = await SeedAsync();
        try
        {
            var request = new CollectCashPaymentRequestDto(
                VisitChargeIds: [seed.MedicineChargeId],
                IdempotencyKey: $"cash-866-{Guid.NewGuid():N}");

            var response = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/visits/{seed.VisitCode}/payments/cash",
                request,
                PharmacyToken());

            var body = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK,
                $"{response.StatusCode}: {body}");

            var dto = JsonSerializer.Deserialize<CollectCashPaymentResponseDto>(body, JsonOptions);
            Assert.NotNull(dto);
            var allocation = Assert.Single(dto!.Allocations);
            Assert.Equal(VisitChargeTypes.Medicine, allocation.ChargeType);
            Assert.Equal(seed.MedicineId, allocation.MedicineId);
            Assert.Equal(seed.PrescriptionId, allocation.PrescriptionId);
            Assert.Equal(seed.LineAmount, allocation.AllocatedAmount);
            Assert.Equal(PaymentAllocationStatuses.Paid, allocation.Status);
            Assert.Equal(seed.LineAmount, dto.Amount);

            var payment = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == dto.PaymentId);
            Assert.Equal(PaymentMethods.Cash, payment.PaymentMethod);
            Assert.Equal(seed.PharmacyStaffId, payment.CollectedByStaffId);

            var storedAllocation = await Db.PaymentAllocations.AsNoTracking()
                .SingleAsync(a => a.PaymentId == dto.PaymentId);
            Assert.Equal(seed.MedicineChargeId, storedAllocation.VisitChargeId);
            Assert.Equal(seed.LineAmount, storedAllocation.AllocatedAmount);
            Assert.Equal(PaymentAllocationStatuses.Paid, storedAllocation.Status);

            // #866: dispense clearance opens only after the medicine charge is settled.
            using var scope = Factory.Services.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();
            var gate = await reader.CheckPharmacyMedicineEligibilityAsync(seed.VisitId, seed.PrescriptionId);
            Assert.True(gate.IsUnlocked, gate.Message);
            Assert.Equal(VisitChargeSettlementReader.StatusPaid, gate.StatusCode);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task PharmacyQr_IssuesPendingAllocationForMedicineCharge()
    {
        var seed = await SeedAsync(withQrPolicy: true);
        try
        {
            var request = new GenerateQrPaymentRequestDto(
                VisitChargeIds: [seed.MedicineChargeId],
                IdempotencyKey: $"qr-866-{Guid.NewGuid():N}");

            var response = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/visits/{seed.VisitCode}/payments/qr",
                request,
                PharmacyToken());

            var body = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK,
                $"{response.StatusCode}: {body}");

            var dto = JsonSerializer.Deserialize<GenerateQrPaymentResponseDto>(body, JsonOptions);
            Assert.NotNull(dto);
            Assert.Equal(seed.LineAmount, dto!.Amount);
            var allocation = Assert.Single(dto!.Allocations!);
            Assert.Equal(VisitChargeTypes.Medicine, allocation.ChargeType);
            Assert.Equal(seed.LineAmount, allocation.AllocatedAmount);
            Assert.Equal(PaymentAllocationStatuses.Pending, allocation.Status);

            var storedAllocation = await Db.PaymentAllocations.AsNoTracking()
                .SingleAsync(a => a.PaymentId == dto.PaymentId);
            Assert.Equal(seed.MedicineChargeId, storedAllocation.VisitChargeId);
            Assert.Equal(PaymentAllocationStatuses.Pending, storedAllocation.Status);

            using var scope = Factory.Services.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();
            var gate = await reader.CheckPharmacyMedicineEligibilityAsync(seed.VisitId, seed.PrescriptionId);
            Assert.False(gate.IsUnlocked);
            Assert.Contains("tiền thuốc", gate.Message!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task ReceptionCash_OnMedicineCharge_IsForbidden()
    {
        var seed = await SeedAsync();
        try
        {
            var request = new CollectCashPaymentRequestDto(
                VisitChargeIds: [seed.MedicineChargeId],
                IdempotencyKey: $"cash-866-{Guid.NewGuid():N}");

            var response = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{seed.VisitCode}/payments/cash",
                request,
                ReceptionToken());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("PHARMACY_COUNTER_ONLY", body);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task PharmacyCash_OnExaminationCharge_IsForbidden()
    {
        var seed = await SeedAsync(includeExaminationCharge: true);
        try
        {
            var request = new CollectCashPaymentRequestDto(
                VisitChargeIds: [seed.ExaminationChargeId!.Value],
                IdempotencyKey: $"cash-866-{Guid.NewGuid():N}");

            var response = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/visits/{seed.VisitCode}/payments/cash",
                request,
                PharmacyToken());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("PHARMACY_MEDICINE_ONLY", body);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task ReceptionQr_OnMedicineCharge_IsForbidden()
    {
        var seed = await SeedAsync();
        try
        {
            var request = new GenerateQrPaymentRequestDto(
                VisitChargeIds: [seed.MedicineChargeId],
                IdempotencyKey: $"qr-866-{Guid.NewGuid():N}");

            var response = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{seed.VisitCode}/payments/qr",
                request,
                ReceptionToken());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task PharmacyQr_OnExaminationCharge_IsForbidden()
    {
        var seed = await SeedAsync(includeExaminationCharge: true);
        try
        {
            var request = new GenerateQrPaymentRequestDto(
                VisitChargeIds: [seed.ExaminationChargeId!.Value],
                IdempotencyKey: $"qr-866-{Guid.NewGuid():N}");

            var response = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/visits/{seed.VisitCode}/payments/qr",
                request,
                PharmacyToken());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("PHARMACY_MEDICINE_ONLY", body);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task PharmacyChargesView_ReturnsMedicineSnapshotFields()
    {
        var seed = await SeedAsync();
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/pharmacy/visits/{seed.VisitCode}/charges");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PharmacyToken());

            var response = await Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");

            var dto = JsonSerializer.Deserialize<VisitChargesResponseDto>(body, JsonOptions);
            Assert.NotNull(dto);
            var line = Assert.Single(dto!.Charges);
            Assert.Equal(VisitChargeTypes.Medicine, line.ChargeType);
            Assert.Equal(seed.MedicineId, line.MedicineId);
            Assert.Equal(seed.PrescriptionId, line.PrescriptionId);
            Assert.Equal(seed.PrescriptionItemId, line.PrescriptionItemId);
            Assert.Equal(2m, line.Quantity);
            Assert.Equal(seed.MedicineUnitPrice, line.MedicineUnitPrice);
            Assert.Equal(seed.LineAmount, line.UnitPrice);
            Assert.False(string.IsNullOrWhiteSpace(line.MedicineName));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    private async Task<Seed> SeedAsync(bool includeExaminationCharge = false, bool withQrPolicy = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var paidAt = new DateTime(2026, 11, 29, 9, 0, 0);

        var roomHospital = await Db.Staff.AsNoTracking()
            .Where(s => s.UserId == 5)
            .Select(s => s.HospitalId)
            .FirstAsync();

        var room = new HospitalRoom
        {
            HospitalId = roomHospital!.Value,
            RoomCode = $"P866{suffix}"[..12],
            Name = "Phòng test #866",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
        };
        Db.HospitalRooms.Add(room);
        await Db.SaveChangesAsync();

        var visit = new Visit
        {
            VisitCode = $"VC866-{suffix}",
            PatientId = 1,
            Status = "CHECKED_IN",
            ExaminationRoomId = room.Id,
            CheckedInAt = paidAt,
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        var medicine = await Db.Medicines.AsNoTracking()
            .FirstAsync(m => m.HospitalId == roomHospital.Value);

        var prescription = new Prescription
        {
            PrescriptionCode = $"RX866-{suffix}",
            VisitId = visit.Id,
            FulfillmentChoice = "HOSPITAL",
            Status = "ISSUED",
            IssuedAt = paidAt,
            CreatedAt = paidAt,
        };
        Db.Prescriptions.Add(prescription);
        await Db.SaveChangesAsync();

        var item = new PrescriptionItem
        {
            PrescriptionId = prescription.Id,
            MedicineId = medicine.Id,
            PrescribedQuantity = 2m,
            SingleDoseQuantity = 1m,
            SingleDoseUnit = medicine.DispensingUnit ?? "viên",
            Instructions = "Uống 1 viên/lần",
        };
        Db.PrescriptionItems.Add(item);
        await Db.SaveChangesAsync();

        const decimal unitPrice = 12_500m;
        const decimal quantity = 2m;
        var lineAmount = unitPrice * quantity;

        var medicineCharge = new VisitCharge
        {
            VisitId = visit.Id,
            PrescriptionId = prescription.Id,
            PrescriptionItemId = item.Id,
            MedicineId = medicine.Id,
            ChargeType = VisitChargeTypes.Medicine,
            Quantity = quantity,
            MedicineUnitPrice = unitPrice,
            UnitPrice = lineAmount,
            PaymentRequirement = "REQUIRED",
            Status = VisitChargeStatuses.Active,
            CreatedAt = paidAt,
        };
        Db.VisitCharges.Add(medicineCharge);

        VisitCharge? examinationCharge = null;
        if (includeExaminationCharge)
        {
            examinationCharge = new VisitCharge
            {
                VisitId = visit.Id,
                ChargeType = VisitChargeTypes.Examination,
                UnitPrice = 150_000m,
                PaymentRequirement = "REQUIRED",
                Status = VisitChargeStatuses.Active,
                CreatedAt = paidAt,
            };
            Db.VisitCharges.Add(examinationCharge);
        }

        await Db.SaveChangesAsync();

        var pharmacyStaffId = await Db.Staff.AsNoTracking()
            .Where(s => s.UserId == 9)
            .Select(s => s.Id)
            .FirstAsync();

        long? policyId = null;
        if (withQrPolicy)
        {
            var policy = new HospitalPolicy
            {
                HospitalId = roomHospital.Value,
                PolicyType = "PAYMENT",
                PolicyKey = "vietqr_account",
                PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN DEMO"}""",
                PolicyVersion = 1,
            };
            Db.HospitalPolicies.Add(policy);
            await Db.SaveChangesAsync();
            policyId = policy.Id;
        }

        return new Seed(
            visit.Id,
            visit.VisitCode!,
            room.Id,
            prescription.Id,
            item.Id,
            medicineCharge.Id,
            examinationCharge?.Id,
            medicine.Id,
            pharmacyStaffId,
            unitPrice,
            lineAmount,
            policyId);
    }

    private async Task CleanupAsync(Seed seed)
    {
        Db.ChangeTracker.Clear();
        await Db.PaymentAllocations.Where(a => a.VisitId == seed.VisitId).ExecuteDeleteAsync();
        await Db.PaymentEnrollmentIntents.Where(i => i.VisitId == seed.VisitId).ExecuteDeleteAsync();
        await Db.Payments.Where(p => p.VisitId == seed.VisitId).ExecuteDeleteAsync();
        await Db.VisitCharges.Where(c => c.VisitId == seed.VisitId).ExecuteDeleteAsync();
        await Db.PrescriptionItems.Where(i => i.PrescriptionId == seed.PrescriptionId).ExecuteDeleteAsync();
        await Db.Prescriptions.Where(p => p.Id == seed.PrescriptionId).ExecuteDeleteAsync();
        await Db.Visits.Where(v => v.Id == seed.VisitId).ExecuteDeleteAsync();
        await Db.HospitalRooms.Where(r => r.Id == seed.RoomId).ExecuteDeleteAsync();
        if (seed.PolicyId is long policyId)
            await Db.HospitalPolicies.Where(p => p.Id == policyId).ExecuteDeleteAsync();
    }

    private string PharmacyToken() => GenerateToken(9, "pharmacy_staff01@medflow.test", RoleNames.PharmacyStaff);

    private string ReceptionToken() => GenerateToken(5, "reception01@medflow.test", RoleNames.Reception);

    private string GenerateToken(long userId, string email, string role)
    {
        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, [role]).AccessToken;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string url,
        object payload,
        string token)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    private sealed record Seed(
        long VisitId,
        string VisitCode,
        long RoomId,
        long PrescriptionId,
        long PrescriptionItemId,
        long MedicineChargeId,
        long? ExaminationChargeId,
        long MedicineId,
        long PharmacyStaffId,
        decimal MedicineUnitPrice,
        decimal LineAmount,
        long? PolicyId);
}
