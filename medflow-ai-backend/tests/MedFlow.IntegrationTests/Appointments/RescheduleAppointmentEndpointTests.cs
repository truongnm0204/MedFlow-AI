using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Appointments;

public sealed class RescheduleAppointmentEndpointTests : AuthTestBase
{
    private const string BasePath = "/api/v1/appointments";

    public RescheduleAppointmentEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ExaminationSlot SlotA { get; }
        public ExaminationSlot SlotB { get; }
        private readonly MedFlow.Infrastructure.Persistence.MedFlowDbContext _db;

        private Fixture(MedFlow.Infrastructure.Persistence.MedFlowDbContext db, ExaminationSlot a, ExaminationSlot b)
        {
            _db = db;
            SlotA = a;
            SlotB = b;
        }

        public static async Task<Fixture> CreateAsync(MedFlow.Infrastructure.Persistence.MedFlowDbContext db)
        {
            var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
            var targetDate = DateOnly.FromDateTime(vnNow).AddDays(2);

            var a = new ExaminationSlot
            {
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = targetDate.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Unspecified),
                EndsAt = targetDate.ToDateTime(new TimeOnly(6, 30), DateTimeKind.Unspecified),
                Capacity = 2,
                Status = "OPEN",
            };
            var b = new ExaminationSlot
            {
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = targetDate.ToDateTime(new TimeOnly(7, 0), DateTimeKind.Unspecified),
                EndsAt = targetDate.ToDateTime(new TimeOnly(7, 30), DateTimeKind.Unspecified),
                Capacity = 2,
                Status = "OPEN",
            };

            db.ExaminationSlots.AddRange(a, b);
            await db.SaveChangesAsync();
            return new Fixture(db, a, b);
        }

        public async Task<Appointment> AddAppointmentAsync(long patientId, ExaminationSlot slot)
        {
            var appt = new Appointment
            {
                PatientId = patientId,
                SlotId = slot.Id,
                AppointmentCode = $"APT-TEST-RS-{Guid.NewGuid():N}"[..20],
                BookingChannel = "ONLINE",
                Status = "BOOKED",
                BookedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            };
            _db.Appointments.Add(appt);
            await _db.SaveChangesAsync();
            return appt;
        }

        public async ValueTask DisposeAsync()
        {
            var apptIds = await _db.Appointments
                .Where(a => a.SlotId == SlotA.Id || a.SlotId == SlotB.Id)
                .Select(a => a.Id.ToString())
                .ToListAsync();

            if (apptIds.Count > 0)
            {
                await _db.AuditLogs
                    .Where(l => l.EntityType == "APPOINTMENT" && l.EntityId != null && apptIds.Contains(l.EntityId))
                    .ExecuteDeleteAsync();
            }

            await _db.Appointments
                .Where(a => a.SlotId == SlotA.Id || a.SlotId == SlotB.Id)
                .ExecuteDeleteAsync();
            await _db.ExaminationSlots
                .Where(s => s.Id == SlotA.Id || s.Id == SlotB.Id)
                .ExecuteDeleteAsync();
        }
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task Reschedule_Anonymous_Returns401()
    {
        var response = await Client.PostAsJsonAsync($"{BasePath}/1/reschedule",
            new RescheduleAppointmentRequest { TargetSlotId = "1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_HospitalAdminRole_Returns403()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");
        using var req = Authed(HttpMethod.Post, $"{BasePath}/1/reschedule", tokens.AccessToken,
            new RescheduleAppointmentRequest { TargetSlotId = "1" });
        var response = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_ValidMove_Returns200NewAppointmentAndPersistsAudit()
    {
        await using var fx = await Fixture.CreateAsync(Db);
        var old = await fx.AddAppointmentAsync(1, fx.SlotA);

        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
        using var req = Authed(HttpMethod.Post, $"{BasePath}/{old.Id}/reschedule", tokens.AccessToken,
            new RescheduleAppointmentRequest { TargetSlotId = fx.SlotB.Id.ToString(CultureInfo.InvariantCulture) });

        var response = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AppointmentDetailDto>();
        Assert.NotNull(dto);
        Assert.NotEqual(old.Id.ToString(CultureInfo.InvariantCulture), dto.Id);
        Assert.Equal("BOOKED", dto.Status);

        var oldRow = await Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == old.Id);
        Assert.Equal("CANCELLED", oldRow.Status);

        var newRow = await Db.Appointments.AsNoTracking().SingleAsync(a => a.SlotId == fx.SlotB.Id && a.Status == "BOOKED");
        Assert.Equal(1, newRow.PatientId);
        Assert.Equal($"APT-{DateTime.UtcNow.AddHours(7):yyyyMM}-{newRow.Id:D4}", newRow.AppointmentCode);

        var log = await Db.AuditLogs.AsNoTracking()
            .SingleAsync(l => l.Action == "APPOINTMENT_RESCHEDULE" && l.EntityId == old.Id.ToString());
        Assert.Equal("APPOINTMENT", log.EntityType);

        // Follow-up GET on old appointment shows CANCELLED.
        using var getReq = Authed(HttpMethod.Get, $"{BasePath}/{old.Id}", tokens.AccessToken);
        var getResp = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getDto = await getResp.Content.ReadFromJsonAsync<AppointmentDetailDto>();
        Assert.NotNull(getDto);
        Assert.Equal("CANCELLED", getDto.Status);
    }

    [Fact]
    public async Task Reschedule_NotOwnedAppointment_Returns403AndRowStaysBooked()
    {
        await using var fx = await Fixture.CreateAsync(Db);
        var otherAppt = await fx.AddAppointmentAsync(2, fx.SlotA); // patient 2, not owned by patient01

        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
        using var req = Authed(HttpMethod.Post, $"{BasePath}/{otherAppt.Id}/reschedule", tokens.AccessToken,
            new RescheduleAppointmentRequest { TargetSlotId = fx.SlotB.Id.ToString(CultureInfo.InvariantCulture) });

        var response = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var row = await Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == otherAppt.Id);
        Assert.Equal("BOOKED", row.Status);
        Assert.Equal(1, await Db.Appointments.CountAsync(a => a.SlotId == fx.SlotA.Id));
    }

    [Fact]
    public async Task Reschedule_SameSlot_Returns400WithDetail()
    {
        await using var fx = await Fixture.CreateAsync(Db);
        var appt = await fx.AddAppointmentAsync(1, fx.SlotA);

        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
        using var req = Authed(HttpMethod.Post, $"{BasePath}/{appt.Id}/reschedule", tokens.AccessToken,
            new RescheduleAppointmentRequest { TargetSlotId = fx.SlotA.Id.ToString(CultureInfo.InvariantCulture) });

        var response = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Khung giờ mới trùng với khung giờ hiện tại của lịch hẹn.", problem.Detail);
    }
}
