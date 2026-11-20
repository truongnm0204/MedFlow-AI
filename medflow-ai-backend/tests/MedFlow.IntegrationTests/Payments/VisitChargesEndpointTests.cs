using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Payments;

public sealed class VisitChargesEndpointTests : AuthTestBase
{
    public VisitChargesEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetCharges_NoToken_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/visits/1/charges");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCharges_PatientOwner_ReturnsDueAndDoesNotWrite()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await SeedOwnedVisitAsync(suffix, unitPrice: 150_000.50m, waived: 50_000m);
        try
        {
            var before = await CountAsync(created.VisitId);
            var response = await Client.SendAsync(AuthedGet($"/api/v1/patients/visits/{created.VisitCode}/charges", userId: 1, role: "PATIENT"));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = JsonSerializer.Deserialize<VisitChargesResponseDto>(body, JsonOptions());
            Assert.NotNull(dto);
            Assert.Equal(150_000.50m, dto!.PayableTotal);
            Assert.Equal(150_000.50m, dto.AmountDue);
            Assert.True(dto.CanPayByQr);
            Assert.Contains(dto.Charges, c => c.Payable == false && c.UnitPrice == 50_000m);

            var again = await Client.SendAsync(AuthedGet($"/api/v1/visits/{created.VisitId}/charges", userId: 1, role: "PATIENT"));
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            var after = await CountAsync(created.VisitId);
            Assert.Equal(before, after);

            var stored = await Db.VisitCharges.AsNoTracking().FirstAsync(c => c.Id == created.ChargeId);
            Assert.Equal(150_000.50m, stored.UnitPrice);
            var payment = await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == created.PaymentId);
            Assert.Equal(created.PaidAt, payment.PaidAt);
            Assert.Equal(150_000.50m, payment.Amount);
        }
        finally
        {
            await CleanupAsync(created);
        }
    }

    [Fact]
    public async Task GetCharges_OtherPatient_DoesNotLeakAmount()
    {
        var created = await SeedOwnedVisitAsync(Guid.NewGuid().ToString("N")[..8], 150_000m, null);
        try
        {
            var response = await Client.SendAsync(AuthedGet(
                $"/api/v1/patients/visits/{created.VisitId}/charges",
                userId: 2,
                role: "PATIENT"));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("150000", body);
            Assert.DoesNotContain("150000.00", body);
        }
        finally
        {
            await CleanupAsync(created);
        }
    }

    [Fact]
    public async Task GetCharges_FamilyLink_ActiveOkRevokedNotFound()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var member = new Patient
        {
            PatientCode = $"VC160-{suffix}",
            FullName = "Thanh vien test",
            Status = "ACTIVE",
        };
        Db.Patients.Add(member);
        await Db.SaveChangesAsync();
        Db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = 1,
            MemberPatientId = member.Id,
            VerificationStatus = "VERIFIED",
            VerificationMethod = "STAFF",
        });
        await Db.SaveChangesAsync();

        var created = await SeedOwnedVisitAsync(suffix, 80_000m, null, member.Id);
        try
        {
            var ok = await Client.SendAsync(AuthedGet($"/api/v1/patients/visits/{created.VisitId}/charges", userId: 1, role: "PATIENT"));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

            var link = await Db.PatientFamilyLinks.FirstAsync(l => l.OwnerPatientId == 1 && l.MemberPatientId == member.Id);
            link.RevokedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await Db.SaveChangesAsync();

            var denied = await Client.SendAsync(AuthedGet($"/api/v1/patients/visits/{created.VisitId}/charges", userId: 1, role: "PATIENT"));
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        finally
        {
            await CleanupAsync(created);
            var link = await Db.PatientFamilyLinks.FirstOrDefaultAsync(l => l.OwnerPatientId == 1 && l.MemberPatientId == member.Id);
            if (link is not null)
                Db.PatientFamilyLinks.Remove(link);
            var patient = await Db.Patients.FirstOrDefaultAsync(p => p.Id == member.Id);
            if (patient is not null)
                Db.Patients.Remove(patient);
            await Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetCharges_LegacyFamilyLink_PhiDenied()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var member = new Patient
        {
            PatientCode = $"VCLEG-{suffix}",
            FullName = "Thanh vien legacy",
            Status = "ACTIVE",
        };
        Db.Patients.Add(member);
        await Db.SaveChangesAsync();
        Db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = 1,
            MemberPatientId = member.Id,
            Relationship = "CHILD",
            VerificationStatus = "VERIFIED",
            VerificationMethod = "LEGACY",
            LegacyAccessUntil = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(10), DateTimeKind.Unspecified),
        });
        await Db.SaveChangesAsync();

        var created = await SeedOwnedVisitAsync(suffix, 80_000m, null, member.Id);
        try
        {
            var denied = await Client.SendAsync(AuthedGet(
                $"/api/v1/patients/visits/{created.VisitId}/charges",
                userId: 1,
                role: "PATIENT"));

            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        finally
        {
            await CleanupAsync(created);
            var link = await Db.PatientFamilyLinks.FirstOrDefaultAsync(l => l.OwnerPatientId == 1 && l.MemberPatientId == member.Id);
            if (link is not null)
                Db.PatientFamilyLinks.Remove(link);
            var patient = await Db.Patients.FirstOrDefaultAsync(p => p.Id == member.Id);
            if (patient is not null)
                Db.Patients.Remove(patient);
            await Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetCharges_ReceptionSameHospitalOk_OtherHospitalForbidden()
    {
        var staff = await Db.Staff.AsNoTracking().FirstAsync(s => s.UserId == 5);
        var created = await SeedOwnedVisitAsync(Guid.NewGuid().ToString("N")[..8], 10_000m, null, hospitalId: staff.HospitalId);
        Hospital? other = null;
        long? otherRoomId = null;
        try
        {
            var ok = await Client.SendAsync(AuthedGet($"/api/v1/reception/visits/{created.VisitId}/charges"));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

            other = new Hospital
            {
                HospitalCode = $"VC160-{Guid.NewGuid():N}"[..12],
                Name = "Co so test khac",
                IsActive = true,
            };
            Db.Hospitals.Add(other);
            await Db.SaveChangesAsync();
            var room = await Db.HospitalRooms.FirstAsync(r => r.Id == created.RoomId);
            room.HospitalId = other.Id;
            otherRoomId = room.Id;
            await Db.SaveChangesAsync();

            var denied = await Client.SendAsync(AuthedGet($"/api/v1/reception/visits/{created.VisitId}/charges"));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        finally
        {
            if (created.RoomId is long roomId)
            {
                var room = await Db.HospitalRooms.FirstOrDefaultAsync(r => r.Id == roomId);
                if (room is not null)
                    room.HospitalId = staff.HospitalId;
                await Db.SaveChangesAsync();
            }

            await CleanupAsync(created);
            if (other is not null)
            {
                var hospital = await Db.Hospitals.FirstOrDefaultAsync(h => h.Id == other.Id);
                if (hospital is not null && otherRoomId is null)
                    Db.Hospitals.Remove(hospital);
                if (hospital is not null && otherRoomId is not null)
                    Db.Hospitals.Remove(hospital);
                await Db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task GetCharges_ConflictingQueueHospitals_Returns409WithoutTotal()
    {
        var staff = await Db.Staff.AsNoTracking().FirstAsync(s => s.UserId == 5);
        var other = new Hospital
        {
            HospitalCode = $"VC160-{Guid.NewGuid():N}"[..12],
            Name = "Co so xung dot",
            IsActive = true,
        };
        Db.Hospitals.Add(other);
        await Db.SaveChangesAsync();
        var created = await SeedOwnedVisitAsync(Guid.NewGuid().ToString("N")[..8], 777_001m, null, hospitalId: null);
        var pointA = new QueueServicePoint { HospitalId = staff.HospitalId, Name = "Diem A test", PointType = "EXAM" };
        var pointB = new QueueServicePoint { HospitalId = other.Id, Name = "Diem B test", PointType = "EXAM" };
        Db.QueueServicePoints.AddRange(pointA, pointB);
        await Db.SaveChangesAsync();
        var entryA = new QueueEntry { VisitId = created.VisitId, ServicePointId = pointA.Id, Status = "WAITING" };
        var entryB = new QueueEntry { VisitId = created.VisitId, CurrentServicePointId = pointB.Id, Status = "WAITING" };
        Db.QueueEntries.AddRange(entryA, entryB);
        await Db.SaveChangesAsync();
        try
        {
            var response = await Client.SendAsync(AuthedGet($"/api/v1/reception/visits/{created.VisitId}/charges"));
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var problem = JsonDocument.Parse(body);
            Assert.Equal(
                "Không xác định được cơ sở của lượt khám.",
                problem.RootElement.GetProperty("detail").GetString());
            Assert.DoesNotContain("777001", body);
        }
        finally
        {
            Db.QueueEntries.RemoveRange(
                await Db.QueueEntries.Where(q => q.Id == entryA.Id || q.Id == entryB.Id).ToListAsync());
            Db.QueueServicePoints.RemoveRange(
                await Db.QueueServicePoints.Where(p => p.Id == pointA.Id || p.Id == pointB.Id).ToListAsync());
            await Db.SaveChangesAsync();
            await CleanupAsync(created);
            var hospital = await Db.Hospitals.FirstOrDefaultAsync(h => h.Id == other.Id);
            if (hospital is not null)
                Db.Hospitals.Remove(hospital);
            await Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetCharges_WalkInAuditPrice_IgnoresPaymentAmount()
    {
        var staff = await Db.Staff.AsNoTracking().FirstAsync(s => s.UserId == 5);
        var created = await SeedOwnedVisitAsync(Guid.NewGuid().ToString("N")[..8], unitPrice: null, waived: null, hospitalId: null);
        Db.AuditLogs.Add(new AuditLog
        {
            HospitalId = staff.HospitalId,
            Action = "WALK_IN_VISIT_CREATED",
            EntityType = "VISIT",
            EntityId = created.VisitId.ToString(),
            ChangeData = """{"servicePrice":180000,"paymentMethod":"VIETQR"}""",
            OccurredAt = DateTime.UtcNow,
        });
        var payment = new Payment
        {
            VisitId = created.VisitId,
            Amount = 50m,
            Status = "PENDING",
            PaymentCode = $"VC160-{created.VisitId}",
        };
        Db.Payments.Add(payment);
        await Db.SaveChangesAsync();
        try
        {
            var response = await Client.SendAsync(AuthedGet($"/api/v1/visits/{created.VisitId}/charges", userId: 1, role: "PATIENT"));
            var dto = await response.Content.ReadFromJsonAsync<VisitChargesResponseDto>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(dto);
            Assert.Equal("DERIVED", dto!.Charges[0].ChargeSource);
            Assert.Equal(180_000m, dto.Charges[0].UnitPrice);
            Assert.False(dto.CanPayByQr);
        }
        finally
        {
            var row = await Db.Payments.FirstOrDefaultAsync(p => p.Id == payment.Id);
            if (row is not null)
                Db.Payments.Remove(row);
            var audits = await Db.AuditLogs.Where(a => a.EntityId == created.VisitId.ToString() && a.Action == "WALK_IN_VISIT_CREATED").ToListAsync();
            Db.AuditLogs.RemoveRange(audits);
            await Db.SaveChangesAsync();
            await CleanupAsync(created);
        }
    }

    [Fact]
    public async Task GetCharges_CancelledVisit_Returns200AndCannotPayByQr()
    {
        var created = await SeedOwnedVisitAsync(Guid.NewGuid().ToString("N")[..8], 10_000m, null, status: "CANCELLED");
        try
        {
            var response = await Client.SendAsync(AuthedGet($"/api/v1/visits/{created.VisitId}/charges", userId: 1, role: "PATIENT"));
            var dto = await response.Content.ReadFromJsonAsync<VisitChargesResponseDto>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(dto);
            Assert.False(dto!.CanPayByQr);
        }
        finally
        {
            await CleanupAsync(created);
        }
    }

    private HttpRequestMessage AuthedGet(string url, long userId = 5, string role = "RECEPTION")
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(new User { Id = userId, Email = $"{role}@medflow.test" }, [role]).AccessToken;
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<Seed> SeedOwnedVisitAsync(
        string suffix,
        decimal? unitPrice,
        decimal? waived,
        long patientId = 1,
        long? hospitalId = null,
        string status = "CHECKED_IN")
    {
        var staffHospital = hospitalId ?? (await Db.Staff.AsNoTracking().FirstAsync(s => s.UserId == 5)).HospitalId;
        HospitalRoom? room = null;
        if (staffHospital is long roomHospital)
        {
            room = new HospitalRoom
            {
                HospitalId = roomHospital,
                RoomCode = $"VC{suffix}"[..8],
                Name = "Phong test phi",
                RoomType = "EXAMINATION",
                Status = "ACTIVE",
            };
            Db.HospitalRooms.Add(room);
            await Db.SaveChangesAsync();
        }

        var paidAt = new DateTime(2026, 11, 14, 9, 30, 0);
        var visit = new Visit
        {
            VisitCode = $"VC160-{suffix}",
            PatientId = patientId,
            Status = status,
            ExaminationRoomId = room?.Id,
            CheckedInAt = paidAt,
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        long? chargeId = null;
        if (unitPrice is decimal price)
        {
            var charge = new VisitCharge
            {
                VisitId = visit.Id,
                UnitPrice = price,
                PaymentRequirement = "REQUIRED",
            };
            Db.VisitCharges.Add(charge);
            await Db.SaveChangesAsync();
            chargeId = charge.Id;
        }

        if (waived is decimal waivedPrice)
        {
            Db.VisitCharges.Add(new VisitCharge
            {
                VisitId = visit.Id,
                UnitPrice = waivedPrice,
                PaymentRequirement = "WAIVED",
            });
            await Db.SaveChangesAsync();
        }

        var payment = new Payment
        {
            VisitId = visit.Id,
            Amount = unitPrice ?? 0m,
            Status = "UNPAID",
            PaidAt = paidAt,
            CreatedAt = paidAt,
            PaymentCode = $"VC-{visit.Id}",
        };
        Db.Payments.Add(payment);
        await Db.SaveChangesAsync();

        return new Seed(visit.Id, visit.VisitCode!, chargeId, payment.Id, room?.Id, paidAt, patientId);
    }

    private async Task<(int Payments, int Charges, int Audits)> CountAsync(long visitId)
    {
        var entityId = visitId.ToString();
        return (
            await Db.Payments.CountAsync(p => p.VisitId == visitId),
            await Db.VisitCharges.CountAsync(c => c.VisitId == visitId),
            await Db.AuditLogs.CountAsync(a => a.EntityId == entityId && a.EntityType == "VISIT"));
    }

    private async Task CleanupAsync(Seed created)
    {
        Db.ChangeTracker.Clear();
        var payments = await Db.Payments.Where(p => p.VisitId == created.VisitId).ToListAsync();
        Db.Payments.RemoveRange(payments);
        var charges = await Db.VisitCharges.Where(c => c.VisitId == created.VisitId).ToListAsync();
        Db.VisitCharges.RemoveRange(charges);
        var entries = await Db.QueueEntries.Where(q => q.VisitId == created.VisitId).ToListAsync();
        Db.QueueEntries.RemoveRange(entries);
        var audits = await Db.AuditLogs.Where(a => a.EntityId == created.VisitId.ToString()).ToListAsync();
        Db.AuditLogs.RemoveRange(audits);
        var visit = await Db.Visits.FirstOrDefaultAsync(v => v.Id == created.VisitId);
        if (visit is not null)
            Db.Visits.Remove(visit);
        await Db.SaveChangesAsync();
        if (created.RoomId is long roomId)
        {
            var room = await Db.HospitalRooms.FirstOrDefaultAsync(r => r.Id == roomId);
            if (room is not null)
                Db.HospitalRooms.Remove(room);
            await Db.SaveChangesAsync();
        }
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

    private sealed record Seed(
        long VisitId,
        string VisitCode,
        long? ChargeId,
        long PaymentId,
        long? RoomId,
        DateTime PaidAt,
        long PatientId);
}
