using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MedFlow.IntegrationTests.Appointments;

public sealed class AppointmentJourneyTests : AuthTestBase
{
    private const string AppointmentsBasePath = "/api/v1/appointments";

    public AppointmentJourneyTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return req;
    }

    [Fact]
    public async Task Journey_DoctorMode_BookThenListDetailTrackCancel_Succeeds()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var tomorrow = DateOnly.FromDateTime(vnNow).AddDays(1);
        var tomorrowIso = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = tomorrow.ToDateTime(new TimeOnly(22, 0), DateTimeKind.Unspecified),
            EndsAt = tomorrow.ToDateTime(new TimeOnly(22, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
            var slotIdStr = slot.Id.ToString(CultureInfo.InvariantCulture);

            // 1. Book appointment (doctor mode)
            using var bookReq = Authed(HttpMethod.Post, AppointmentsBasePath, tokens.AccessToken, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                ServiceCode = "EX-GI-01",
                DateIso = tomorrowIso,
                SlotOrShiftId = slotIdStr,
                SymptomNote = "Khám tiêu hóa định kỳ hành trình E2E",
            });

            var bookRes = await Client.SendAsync(bookReq);
            Assert.Equal(HttpStatusCode.Created, bookRes.StatusCode);

            var booked = await bookRes.Content.ReadFromJsonAsync<CreateAppointmentResponse>();
            Assert.NotNull(booked);
            // Seam S1: BE returns "BOOKED" while FE ConfirmBookingResponse type literal declares 'CONFIRMED'.
            Assert.Equal("BOOKED", booked.Status);
            Assert.StartsWith("APT-", booked.AppointmentCode);

            // 2. Get appointment detail
            using var detailReq = Authed(HttpMethod.Get, $"{AppointmentsBasePath}/{booked.AppointmentId}", tokens.AccessToken);
            var detailRes = await Client.SendAsync(detailReq);
            Assert.Equal(HttpStatusCode.OK, detailRes.StatusCode);

            var detail = await detailRes.Content.ReadFromJsonAsync<AppointmentDetailDto>();
            Assert.NotNull(detail);
            Assert.Equal("BOOKED", detail.Status);
            Assert.Contains("CANCEL", detail.AvailableActions);
            Assert.Contains("RESCHEDULE", detail.AvailableActions);
            Assert.Contains("UPDATE_REASON", detail.AvailableActions);
            Assert.Contains("PRINT_SLIP", detail.AvailableActions);
            Assert.Contains("TRACK_QUEUE", detail.AvailableActions);

            // 3. Get status tracking
            using var trackReq = Authed(HttpMethod.Get, $"{AppointmentsBasePath}/{booked.AppointmentId}/status-tracking", tokens.AccessToken);
            var trackRes = await Client.SendAsync(trackReq);
            Assert.Equal(HttpStatusCode.OK, trackRes.StatusCode);

            var tracking = await trackRes.Content.ReadFromJsonAsync<AppointmentStatusTrackingDto>();
            Assert.NotNull(tracking);
            Assert.Equal(
                ["done", "current", "upcoming", "upcoming"],
                tracking.Steps.Select(s => s.State).ToArray());
            Assert.Equal("CHECK_IN", tracking.NextStep.Code);

            // 4. List UPCOMING appointments
            using var upcomingReq = Authed(HttpMethod.Get, $"{AppointmentsBasePath}/me?tab=UPCOMING&pageSize=50", tokens.AccessToken);
            var upcomingRes = await Client.SendAsync(upcomingReq);
            Assert.Equal(HttpStatusCode.OK, upcomingRes.StatusCode);

            var upcomingList = await upcomingRes.Content.ReadFromJsonAsync<AppointmentListResponse>();
            Assert.NotNull(upcomingList);
            Assert.Contains(upcomingList.Items, i => i.AppointmentCode == booked.AppointmentCode);

            // 5. Cancel appointment
            using var cancelReq = Authed(HttpMethod.Post, $"{AppointmentsBasePath}/{booked.AppointmentId}/cancel", tokens.AccessToken, new CancelAppointmentRequest
            {
                CancellationReason = "Bận việc đột xuất không thể đến",
            });
            var cancelRes = await Client.SendAsync(cancelReq);
            Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

            // Seam S2: BE returns AppointmentDetailDto (including AvailableActions), whereas FE types cancelAppointmentApi as AppointmentListItem.
            var cancelledDetail = await cancelRes.Content.ReadFromJsonAsync<AppointmentDetailDto>();
            Assert.NotNull(cancelledDetail);
            Assert.Equal("CANCELLED", cancelledDetail.Status);
            Assert.Contains("REBOOK", cancelledDetail.AvailableActions);

            // 6. List CANCELLED appointments
            using var cancelledListReq = Authed(HttpMethod.Get, $"{AppointmentsBasePath}/me?tab=CANCELLED&pageSize=50", tokens.AccessToken);
            var cancelledListRes = await Client.SendAsync(cancelledListReq);
            Assert.Equal(HttpStatusCode.OK, cancelledListRes.StatusCode);

            var cancelledList = await cancelledListRes.Content.ReadFromJsonAsync<AppointmentListResponse>();
            Assert.NotNull(cancelledList);
            Assert.Contains(cancelledList.Items, i => i.AppointmentCode == booked.AppointmentCode);
        }
        finally
        {
            var apptIds = await Db.Appointments
                .Where(a => a.SlotId == slot.Id)
                .Select(a => a.Id.ToString())
                .ToListAsync();

            if (apptIds.Count > 0)
            {
                await Db.AuditLogs
                    .Where(l => l.EntityType == "APPOINTMENT" && l.EntityId != null && apptIds.Contains(l.EntityId))
                    .ExecuteDeleteAsync();
            }

            await Db.Appointments.Where(a => a.SlotId == slot.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Journey_BookThenReschedule_CreatesReplacementAndCancelsOld()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var tomorrow = DateOnly.FromDateTime(vnNow).AddDays(1);
        var tomorrowIso = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var slotA = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = tomorrow.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Unspecified),
            EndsAt = tomorrow.ToDateTime(new TimeOnly(20, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };
        var slotB = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = tomorrow.ToDateTime(new TimeOnly(21, 0), DateTimeKind.Unspecified),
            EndsAt = tomorrow.ToDateTime(new TimeOnly(21, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.AddRange(slotA, slotB);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var bookReq = Authed(HttpMethod.Post, AppointmentsBasePath, tokens.AccessToken, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                ServiceCode = "EX-GI-01",
                DateIso = tomorrowIso,
                SlotOrShiftId = slotA.Id.ToString(CultureInfo.InvariantCulture),
                SymptomNote = "Đặt lịch ca tối rồi đổi giờ",
            });

            var bookRes = await Client.SendAsync(bookReq);
            Assert.Equal(HttpStatusCode.Created, bookRes.StatusCode);

            var booked = await bookRes.Content.ReadFromJsonAsync<CreateAppointmentResponse>();
            Assert.NotNull(booked);

            using var rescheduleReq = Authed(
                HttpMethod.Post,
                $"{AppointmentsBasePath}/{booked.AppointmentId}/reschedule",
                tokens.AccessToken,
                new RescheduleAppointmentRequest
                {
                    TargetSlotId = slotB.Id.ToString(CultureInfo.InvariantCulture),
                    RescheduleNote = "Đổi sang chiều",
                });

            var rescheduleRes = await Client.SendAsync(rescheduleReq);
            Assert.Equal(HttpStatusCode.OK, rescheduleRes.StatusCode);

            // Seam S2: BE returns AppointmentDetailDto for reschedule, with a newly created replacement Id.
            var rescheduledDetail = await rescheduleRes.Content.ReadFromJsonAsync<AppointmentDetailDto>();
            Assert.NotNull(rescheduledDetail);
            Assert.NotEqual(booked.AppointmentId, rescheduledDetail.Id);
            Assert.Equal("BOOKED", rescheduledDetail.Status);
            Assert.NotEmpty(rescheduledDetail.AvailableActions);

            var oldId = long.Parse(booked.AppointmentId, CultureInfo.InvariantCulture);
            var oldRow = await Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == oldId);
            Assert.Equal("CANCELLED", oldRow.Status);

            var newId = long.Parse(rescheduledDetail.Id, CultureInfo.InvariantCulture);
            var newRow = await Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == newId);
            Assert.Equal("BOOKED", newRow.Status);
            Assert.Equal(slotB.Id, newRow.SlotId);
        }
        finally
        {
            var apptIds = await Db.Appointments
                .Where(a => a.SlotId == slotA.Id || a.SlotId == slotB.Id)
                .Select(a => a.Id.ToString())
                .ToListAsync();

            if (apptIds.Count > 0)
            {
                await Db.AuditLogs
                    .Where(l => l.EntityType == "APPOINTMENT" && l.EntityId != null && apptIds.Contains(l.EntityId))
                    .ExecuteDeleteAsync();
            }

            await Db.Appointments
                .Where(a => a.SlotId == slotA.Id || a.SlotId == slotB.Id)
                .ExecuteDeleteAsync();
            await Db.ExaminationSlots
                .Where(s => s.Id == slotA.Id || s.Id == slotB.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Journey_FamilyBooking_BooksForLinkedMember()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var tomorrow = DateOnly.FromDateTime(vnNow).AddDays(1);
        var tomorrowIso = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = tomorrow.ToDateTime(new TimeOnly(19, 0), DateTimeKind.Unspecified),
            EndsAt = tomorrow.ToDateTime(new TimeOnly(19, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var bookReq = Authed(HttpMethod.Post, AppointmentsBasePath, tokens.AccessToken, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "3", // Linked family member (CHILD) of patient01
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                ServiceCode = "EX-GI-01",
                DateIso = tomorrowIso,
                SlotOrShiftId = slot.Id.ToString(CultureInfo.InvariantCulture),
                SymptomNote = "Đặt lịch khám cho con",
            });

            var bookRes = await Client.SendAsync(bookReq);
            Assert.Equal(HttpStatusCode.Created, bookRes.StatusCode);

            var booked = await bookRes.Content.ReadFromJsonAsync<CreateAppointmentResponse>();
            Assert.NotNull(booked);
            Assert.Equal("BOOKED", booked.Status);

            using var listReq = Authed(HttpMethod.Get, $"{AppointmentsBasePath}/me?memberId=3&pageSize=50", tokens.AccessToken);
            var listRes = await Client.SendAsync(listReq);
            Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

            var listDto = await listRes.Content.ReadFromJsonAsync<AppointmentListResponse>();
            Assert.NotNull(listDto);

            var item = Assert.Single(listDto.Items, i => i.AppointmentCode == booked.AppointmentCode);
            Assert.Equal("PAT-0003", item.PatientCode);
        }
        finally
        {
            var apptIds = await Db.Appointments
                .Where(a => a.SlotId == slot.Id)
                .Select(a => a.Id.ToString())
                .ToListAsync();

            if (apptIds.Count > 0)
            {
                await Db.AuditLogs
                    .Where(l => l.EntityType == "APPOINTMENT" && l.EntityId != null && apptIds.Contains(l.EntityId))
                    .ExecuteDeleteAsync();
            }

            await Db.Appointments.Where(a => a.SlotId == slot.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Journey_ReviewPreview_ServiceMode_ReturnsPayload()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var tomorrow = DateOnly.FromDateTime(vnNow).AddDays(1);
        var tomorrowIso = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = tomorrow.ToDateTime(new TimeOnly(5, 30), DateTimeKind.Unspecified),
            EndsAt = tomorrow.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
            var url = $"{AppointmentsBasePath}/review-preview?mode=service&patientId=1&serviceId=EX-GI-01&dateIso={tomorrowIso}&shiftId=shift-morning";

            using var req = Authed(HttpMethod.Get, url, tokens.AccessToken);
            var res = await Client.SendAsync(req);

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var dto = await res.Content.ReadFromJsonAsync<AppointmentReviewPreviewResponse>();
            Assert.NotNull(dto);
            Assert.NotNull(dto.PatientInfo);
            Assert.NotNull(dto.ScheduleLocation);
            Assert.NotNull(dto.FeeSummary);
            Assert.NotNull(dto.ReceptionPolicy);

            Assert.Equal("1", dto.PatientInfo.PatientId);
            Assert.Equal("service", dto.ScheduleLocation.Mode);
            Assert.Equal("shift-morning", dto.ScheduleLocation.SlotOrShiftId);
        }
        finally
        {
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }
}
