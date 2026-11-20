using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Appointments;

public sealed class AppointmentDetailEndpointTests : AuthTestBase
{
    private const string BasePath = "/api/v1/appointments";

    public AppointmentDetailEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetDetail_Anonymous_Returns401()
    {
        var response = await Client.GetAsync($"{BasePath}/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_HospitalAdminRole_Returns403()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_Patient01ById1_Returns200WithCompletedDetailsPhiAndActions()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AppointmentDetailDto>();
        Assert.NotNull(dto);

        Assert.Equal("APT-202610-0001", dto.AppointmentCode);
        Assert.Equal("COMPLETED", dto.Status);
        Assert.Equal("PAID_VIETQR", dto.PaymentStatus);
        Assert.Equal(1, dto.VisitId);
        Assert.Equal("VIS-202610-0001", dto.VisitCode);
        Assert.Equal("001", dto.QueueNumber);
        Assert.Equal("0900000001", dto.PatientPhone);
        Assert.Equal("Địa chỉ demo số 1, Hà Nội", dto.PatientAddress);
        Assert.NotNull(dto.PatientBirthDateLabel);
        Assert.StartsWith("02/02/1980 (", dto.PatientBirthDateLabel);
        Assert.EndsWith("• Nam", dto.PatientBirthDateLabel);
        Assert.Equal(["PRINT_SLIP", "REBOOK"], dto.AvailableActions);
    }

    [Fact]
    public async Task GetDetail_Patient01ByFamilyMemberAppointmentCode_Returns200()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/APT-202610-0003");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AppointmentDetailDto>();
        Assert.NotNull(dto);

        Assert.Equal("PAT-0003", dto.PatientCode);
        Assert.Equal("Con", dto.PatientRelationshipLabel);
    }

    [Fact]
    public async Task GetDetail_Patient01ForbiddenNotFoundAndBadRequest_ReturnsExpectedStatusCodes()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var forbiddenIdReq = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/2");
        forbiddenIdReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var forbiddenIdRes = await Client.SendAsync(forbiddenIdReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenIdRes.StatusCode);

        using var forbiddenCodeReq = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/APT-202610-0002");
        forbiddenCodeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var forbiddenCodeRes = await Client.SendAsync(forbiddenCodeReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCodeRes.StatusCode);

        using var notFoundReq = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/99999999");
        notFoundReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var notFoundRes = await Client.SendAsync(notFoundReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);

        using var badReq = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/0");
        badReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var badRes = await Client.SendAsync(badReq);
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
    }

    [Fact]
    public async Task GetDetail_WithInsertedBookedAppointmentPlus3Days_ReturnsAllUpcomingActionsAndComputedQueue()
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

        var apptCode = $"APT-D-{Guid.NewGuid():N}"[..30];
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

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BasePath}/{appointment.Id}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<AppointmentDetailDto>();
            Assert.NotNull(dto);

            Assert.Equal("BOOKED", dto.Status);
            Assert.NotNull(dto.QueueNumber);
            Assert.Equal(
                ["CANCEL", "RESCHEDULE", "UPDATE_REASON", "PRINT_SLIP", "TRACK_QUEUE"],
                dto.AvailableActions);
        }
        finally
        {
            await Db.Appointments.Where(a => a.Id == appointment.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }
}
