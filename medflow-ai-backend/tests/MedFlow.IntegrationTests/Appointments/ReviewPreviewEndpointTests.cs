using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Appointments;

public sealed class ReviewPreviewEndpointTests : AuthTestBase
{
    private const string ReviewPreviewEndpoint = "/api/v1/appointments/review-preview";

    public ReviewPreviewEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetReviewPreview_Anonymous_Returns401()
    {
        var url = $"{ReviewPreviewEndpoint}?mode=doctor&patientId=1&doctorId=DOC-001&serviceId=EX-GI-01&serviceCode=EX-GI-01&dateIso=2026-11-03&slotId=1&shiftId=shift-morning";
        var response = await Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetReviewPreview_HospitalAdminRole_Returns403()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");
        var url = $"{ReviewPreviewEndpoint}?mode=doctor&patientId=1&doctorId=DOC-001&serviceId=EX-GI-01&serviceCode=EX-GI-01&dateIso=2026-11-03&slotId=1&shiftId=shift-morning";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReviewPreview_SelfPatient_Returns200WithoutPersisting()
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
            var slotIdStr = slot.Id.ToString(CultureInfo.InvariantCulture);
            var url = $"{ReviewPreviewEndpoint}?mode=doctor&patientId=1&doctorId=DOC-001&serviceId=EX-GI-01&serviceCode=EX-GI-01&dateIso={tomorrowIso}&slotId={slotIdStr}&shiftId=shift-morning";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<AppointmentReviewPreviewResponse>();
            Assert.NotNull(dto);

            Assert.Equal("1", dto.PatientInfo.PatientId);
            Assert.Equal(slotIdStr, dto.ScheduleLocation.SlotOrShiftId);
            Assert.Equal(0, await Db.Appointments.AsNoTracking().CountAsync(a => a.SlotId == slot.Id));
        }
        finally
        {
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GetReviewPreview_UnownedPatientId_Returns403()
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
            var slotIdStr = slot.Id.ToString(CultureInfo.InvariantCulture);
            var url = $"{ReviewPreviewEndpoint}?mode=doctor&patientId=2&doctorId=DOC-001&serviceId=EX-GI-01&serviceCode=EX-GI-01&dateIso={tomorrowIso}&slotId={slotIdStr}&shiftId=shift-morning";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }
}
