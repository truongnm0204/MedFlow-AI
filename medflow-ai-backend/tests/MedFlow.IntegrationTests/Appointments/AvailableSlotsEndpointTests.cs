using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Appointments;

public sealed class AvailableSlotsEndpointTests : AuthTestBase
{
    private const string AvailableSlotsEndpoint = "/api/v1/appointments/available-slots";
    private const string ServiceShiftsEndpoint = "/api/v1/appointments/service-shifts";

    public AvailableSlotsEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetAvailableSlots_Anonymous_Returns401()
    {
        var response = await Client.GetAsync($"{AvailableSlotsEndpoint}?doctorId=DOC-001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailableSlots_HospitalAdminRole_Returns403()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{AvailableSlotsEndpoint}?doctorId=DOC-001");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailableSlots_PatientWithSeededAndInsertedSlot_Returns200WithProfilesAndLastSlot()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var tomorrow = DateOnly.FromDateTime(vnNow).AddDays(1);
        var tomorrowIso = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = tomorrow.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Unspecified),
            EndsAt = tomorrow.ToDateTime(new TimeOnly(6, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        var appointment = new Appointment
        {
            PatientId = 2,
            SlotId = slot.Id,
            AppointmentCode = $"APT-T-{Guid.NewGuid():N}"[..30],
            BookingChannel = "ONLINE",
            Status = "BOOKED",
            BookedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };

        Db.Appointments.Add(appointment);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{AvailableSlotsEndpoint}?doctorId=DOC-001&serviceId=EX-GI-01&dateIso={tomorrowIso}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<AvailableSlotsResponse>();
            Assert.NotNull(dto);

            Assert.Equal(7, dto.Dates.Count);
            Assert.NotEmpty(dto.Profiles);
            Assert.Equal("PAT-0001", dto.Profiles[0].PatientCode);
            Assert.Contains(dto.Profiles, p => p.PatientCode == "PAT-0003");

            var slotIdStr = slot.Id.ToString(CultureInfo.InvariantCulture);
            var insertedSlot = Assert.Single(dto.MorningSlots, s => s.Id == slotIdStr);
            Assert.Equal(1, insertedSlot.RemainingSlots);
            Assert.Equal("LAST_SLOT", insertedSlot.Status);
        }
        finally
        {
            await Db.Appointments.Where(a => a.Id == appointment.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GetAvailableSlots_InvalidDateIso_Returns400()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{AvailableSlotsEndpoint}?doctorId=doc-01&dateIso=2026-13-01");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailableSlots_UnknownDoctor_Returns404()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{AvailableSlotsEndpoint}?doctorId=DOC-999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetServiceShifts_ValidService_Returns200WithTwoShifts()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var tomorrow = DateOnly.FromDateTime(vnNow).AddDays(1);
        var tomorrowIso = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = tomorrow.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Unspecified),
            EndsAt = tomorrow.ToDateTime(new TimeOnly(6, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{ServiceShiftsEndpoint}?serviceId=EX-GI-01&dateIso={tomorrowIso}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<ServiceShiftsResponse>();
            Assert.NotNull(dto);
            Assert.Equal(2, dto.Shifts.Count);

            var morningShift = Assert.Single(dto.Shifts, s => s.Shift == "MORNING");
            Assert.True(morningShift.RemainingCapacity >= 1);
        }
        finally
        {
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }
}
