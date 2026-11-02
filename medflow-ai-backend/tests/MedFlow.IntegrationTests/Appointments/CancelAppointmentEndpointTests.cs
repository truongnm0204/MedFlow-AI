using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Appointments;

public sealed class CancelAppointmentEndpointTests : AuthTestBase
{
    private const string BasePath = "/api/v1/appointments";

    public CancelAppointmentEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Cancel_Anonymous_Returns401()
    {
        var response = await Client.PostAsJsonAsync(
            $"{BasePath}/1/cancel",
            new CancelAppointmentRequest { CancellationReason = "Bận công tác đột xuất" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_HospitalAdminRole_Returns403()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/1/cancel")
        {
            Content = JsonContent.Create(new CancelAppointmentRequest
            {
                CancellationReason = "Bận công tác đột xuất"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Patient01ForbiddenNotFoundAndBadRequest_ReturnsExpectedStatusCodes()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var forbiddenReq = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/2/cancel")
        {
            Content = JsonContent.Create(new CancelAppointmentRequest
            {
                CancellationReason = "Bận công tác đột xuất"
            })
        };
        forbiddenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var forbiddenRes = await Client.SendAsync(forbiddenReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);

        using var notFoundReq = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/99999999/cancel")
        {
            Content = JsonContent.Create(new CancelAppointmentRequest
            {
                CancellationReason = "Bận công tác đột xuất"
            })
        };
        notFoundReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var notFoundRes = await Client.SendAsync(notFoundReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);

        using var badReq = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/1/cancel")
        {
            Content = JsonContent.Create(new CancelAppointmentRequest
            {
                CancellationReason = "ngắn"
            })
        };
        badReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var badRes = await Client.SendAsync(badReq);
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
    }

    [Fact]
    public async Task Cancel_BookedUpcomingAppointment_Returns200PersistsAuditAndSecondCallReturns409()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var targetDate = DateOnly.FromDateTime(vnNow).AddDays(3);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = targetDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified),
            EndsAt = targetDate.ToDateTime(new TimeOnly(9, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        var apptCode = $"APT-C-{Guid.NewGuid():N}"[..30];
        var appointment = new Appointment
        {
            PatientId = 1,
            SlotId = slot.Id,
            AppointmentCode = apptCode,
            BookingChannel = "ONLINE",
            Status = "BOOKED",
            BookedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };

        Db.Appointments.Add(appointment);
        await Db.SaveChangesAsync();

        var appointmentIdStr = appointment.Id.ToString(CultureInfo.InvariantCulture);

        try
        {
            using var firstReq = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{appointment.Id}/cancel")
            {
                Content = JsonContent.Create(new CancelAppointmentRequest
                {
                    CancellationReason = "  Bận công tác đột xuất tại tỉnh xa  "
                })
            };
            firstReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var firstRes = await Client.SendAsync(firstReq);
            Assert.Equal(HttpStatusCode.OK, firstRes.StatusCode);

            var dto = await firstRes.Content.ReadFromJsonAsync<AppointmentDetailDto>();
            Assert.NotNull(dto);
            Assert.Equal("CANCELLED", dto.Status);
            Assert.Equal("Bận công tác đột xuất tại tỉnh xa", dto.CancellationReason);
            Assert.Equal(["REBOOK"], dto.AvailableActions);

            var persisted = await Db.Appointments
                .AsNoTracking()
                .FirstAsync(a => a.Id == appointment.Id);
            Assert.Equal("CANCELLED", persisted.Status);
            Assert.Equal("Bận công tác đột xuất tại tỉnh xa", persisted.CancellationReason);
            Assert.NotNull(persisted.CancelledAt);
            Assert.NotNull(persisted.CancelledByUserId);

            var auditLogs = await Db.AuditLogs
                .AsNoTracking()
                .Where(l => l.Action == "APPOINTMENT_CANCEL" && l.EntityId == appointmentIdStr)
                .ToListAsync();
            Assert.Single(auditLogs);

            using var secondReq = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{appointment.Id}/cancel")
            {
                Content = JsonContent.Create(new CancelAppointmentRequest
                {
                    CancellationReason = "Bận công tác đột xuất tại tỉnh xa"
                })
            };
            secondReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var secondRes = await Client.SendAsync(secondReq);
            Assert.Equal(HttpStatusCode.Conflict, secondRes.StatusCode);
        }
        finally
        {
            await Db.AuditLogs
                .Where(l => l.EntityType == "APPOINTMENT" && l.EntityId == appointmentIdStr)
                .ExecuteDeleteAsync();
            await Db.Appointments.Where(a => a.Id == appointment.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }
}
