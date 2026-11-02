using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Appointments;

public sealed class MyAppointmentsEndpointTests : AuthTestBase
{
    private const string Endpoint = "/api/v1/appointments/me";

    public MyAppointmentsEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyAppointments_Anonymous_Returns401()
    {
        var response = await Client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyAppointments_HospitalAdminRole_Returns403()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyAppointments_Patient01DefaultQuery_Returns200WithSeededCompletedAppointmentsAndMemberCounts()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AppointmentListResponse>();
        Assert.NotNull(dto);

        var apt1 = Assert.Single(dto.Items, i => i.AppointmentCode == "APT-202610-0001");
        Assert.Equal("COMPLETED", apt1.Status);
        Assert.Equal("PAID_VIETQR", apt1.PaymentStatus);

        Assert.DoesNotContain(dto.Items, i => i.AppointmentCode == "APT-202610-0002");

        Assert.Contains(dto.MemberCounts, m => m.PatientCode == "PAT-0001");
        Assert.Contains(dto.MemberCounts, m => m.PatientCode == "PAT-0003");
    }

    [Fact]
    public async Task GetMyAppointments_ForbiddenAndInvalidQueryParameters_ReturnsExpectedStatusCodes()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var forbiddenReq = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?memberId=2");
        forbiddenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var forbiddenRes = await Client.SendAsync(forbiddenReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);

        using var badMemberReq = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?memberId=abc");
        badMemberReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var badMemberRes = await Client.SendAsync(badMemberReq);
        Assert.Equal(HttpStatusCode.BadRequest, badMemberRes.StatusCode);

        using var badTabReq = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?tab=FOO");
        badTabReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var badTabRes = await Client.SendAsync(badTabReq);
        Assert.Equal(HttpStatusCode.BadRequest, badTabRes.StatusCode);
    }

    [Fact]
    public async Task GetMyAppointments_WithInsertedTomorrowBookedAppointment_AppearsInUpcomingTab()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var tomorrow = DateOnly.FromDateTime(vnNow).AddDays(1);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = tomorrow.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified),
            EndsAt = tomorrow.ToDateTime(new TimeOnly(9, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        var apptCode = $"APT-U-{Guid.NewGuid():N}"[..30];
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

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?tab=UPCOMING");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<AppointmentListResponse>();
            Assert.NotNull(dto);

            var inserted = Assert.Single(dto.Items, i => i.AppointmentCode == apptCode);
            Assert.Equal("BOOKED", inserted.Status);
        }
        finally
        {
            await Db.Appointments.Where(a => a.Id == appointment.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GetMyAppointments_Patient02_DoesNotContainPatient01Appointments()
    {
        var tokens = await LoginAsync("patient02@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AppointmentListResponse>();
        Assert.NotNull(dto);

        Assert.DoesNotContain(dto.Items, i => i.AppointmentCode == "APT-202610-0001");
    }
}
