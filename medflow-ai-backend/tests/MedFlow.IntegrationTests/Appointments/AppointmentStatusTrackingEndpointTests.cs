using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Appointments;

public sealed class AppointmentStatusTrackingEndpointTests : AuthTestBase
{
    private const string BasePath = "/api/v1/appointments";

    public AppointmentStatusTrackingEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private static HttpRequestMessage AuthedGet(string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    [Fact]
    public async Task GetStatusTracking_Anonymous_Returns401()
    {
        var response = await Client.GetAsync($"{BasePath}/1/status-tracking");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStatusTracking_HospitalAdminRole_Returns403()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");

        using var request = AuthedGet($"{BasePath}/1/status-tracking", tokens.AccessToken);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetStatusTracking_Patient01ById1_Returns200WithAllStepsDoneAndFollowUp()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = AuthedGet($"{BasePath}/1/status-tracking", tokens.AccessToken);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AppointmentStatusTrackingDto>();
        Assert.NotNull(dto);

        Assert.Equal("APT-202610-0001", dto.AppointmentCode);
        Assert.Equal("COMPLETED", dto.Status);
        Assert.Equal(
            ["BOOKED", "CHECKED_IN", "IN_CONSULTATION", "COMPLETED"],
            dto.Steps.Select(s => s.Key).ToArray());
        Assert.All(dto.Steps, s => Assert.Equal("done", s.State));
        Assert.Equal("2026-10-12T10:00:00+07:00", dto.Steps[0].OccurredAt);
        Assert.Equal("2026-10-15T08:55:00+07:00", dto.Steps[1].OccurredAt);
        Assert.Equal("2026-10-15T09:05:00+07:00", dto.Steps[2].OccurredAt);
        Assert.Equal("2026-10-15T11:30:00+07:00", dto.Steps[3].OccurredAt);
        Assert.Equal("FOLLOW_UP", dto.NextStep.Code);
    }

    [Fact]
    public async Task GetStatusTracking_Patient01ForbiddenNotFoundAndBadRequest_ReturnsExpectedStatusCodes()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var memberReq = AuthedGet($"{BasePath}/APT-202610-0003/status-tracking", tokens.AccessToken);
        var memberRes = await Client.SendAsync(memberReq);
        Assert.Equal(HttpStatusCode.OK, memberRes.StatusCode);

        using var forbiddenReq = AuthedGet($"{BasePath}/2/status-tracking", tokens.AccessToken);
        var forbiddenRes = await Client.SendAsync(forbiddenReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);

        using var notFoundReq = AuthedGet($"{BasePath}/99999999/status-tracking", tokens.AccessToken);
        var notFoundRes = await Client.SendAsync(notFoundReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);

        using var badReq = AuthedGet($"{BasePath}/0/status-tracking", tokens.AccessToken);
        var badRes = await Client.SendAsync(badReq);
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
    }

    [Fact]
    public async Task GetStatusTracking_WithInsertedBookedAppointmentPlus3Days_ReturnsCheckInNextStep()
    {
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

        var apptCode = $"APT-ST-{Guid.NewGuid():N}"[..30];
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

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var request = AuthedGet($"{BasePath}/{appointment.Id}/status-tracking", tokens.AccessToken);
            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<AppointmentStatusTrackingDto>();
            Assert.NotNull(dto);

            Assert.Equal("BOOKED", dto.Status);
            Assert.Equal(
                ["done", "current", "upcoming", "upcoming"],
                dto.Steps.Select(s => s.State).ToArray());
            Assert.Equal("CHECK_IN", dto.NextStep.Code);
            Assert.NotNull(dto.NextStep.DueAt);
            Assert.EndsWith("T08:45:00+07:00", dto.NextStep.DueAt);
            Assert.NotNull(dto.QueueNumber);
        }
        finally
        {
            await Db.Appointments.Where(a => a.Id == appointment.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }
}
