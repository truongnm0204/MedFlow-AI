using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Appointments;

public sealed class AppointmentQueueTrackingEndpointTests : AuthTestBase
{
    private const string BasePath = "/api/v1/appointments";

    public AppointmentQueueTrackingEndpointTests(TestWebAppFactory factory)
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
    public async Task GetQueueStatus_Anonymous_Returns401()
    {
        var response = await Client.GetAsync($"{BasePath}/1/queue-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQueueStatus_HospitalAdminRole_Returns403()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");

        using var request = AuthedGet($"{BasePath}/1/queue-status", tokens.AccessToken);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetQueueStatus_Patient01ById1_Returns200CompletedWithThreeDoneSteps()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = AuthedGet($"{BasePath}/1/queue-status", tokens.AccessToken);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AppointmentQueueStatusDto>();
        Assert.NotNull(dto);

        Assert.Equal("1", dto.AppointmentId);
        Assert.Equal("APT-202610-0001", dto.AppointmentCode);
        Assert.Equal("COMPLETED", dto.Status);
        Assert.Equal("ĐÃ HOÀN THÀNH", dto.StatusLabel);
        Assert.Null(dto.EstimatedWaitLabel);
        Assert.Equal(
            ["CHECK_IN", "CONSULTATION", "COMPLETE"],
            dto.VisitSteps.Select(s => s.Key).ToArray());
        Assert.All(dto.VisitSteps, s => Assert.Equal("done", s.State));
    }

    [Fact]
    public async Task GetQueueStatus_Patient01FamilyMemberForbiddenNotFoundAndBadRequest_ReturnsExpectedStatusCodes()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var memberReq = AuthedGet($"{BasePath}/APT-202610-0003/queue-status", tokens.AccessToken);
        var memberRes = await Client.SendAsync(memberReq);
        Assert.Equal(HttpStatusCode.OK, memberRes.StatusCode);

        using var forbiddenReq = AuthedGet($"{BasePath}/2/queue-status", tokens.AccessToken);
        var forbiddenRes = await Client.SendAsync(forbiddenReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);
        var forbiddenProblem = await forbiddenRes.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(forbiddenProblem);
        Assert.Equal("Bạn không có quyền xem hàng đợi của lịch hẹn này.", forbiddenProblem.Detail);

        using var notFoundReq = AuthedGet($"{BasePath}/99999999/queue-status", tokens.AccessToken);
        var notFoundRes = await Client.SendAsync(notFoundReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);

        using var badReq = AuthedGet($"{BasePath}/0/queue-status", tokens.AccessToken);
        var badRes = await Client.SendAsync(badReq);
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
    }

    [Fact]
    public async Task GetQueueStatus_CancelledAppointment_Returns409ConflictWithExpectedDetail()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var targetDate = DateOnly.FromDateTime(vnNow).AddDays(2);

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

        var appointment = new Appointment
        {
            PatientId = 1,
            SlotId = slot.Id,
            AppointmentCode = $"APT-CAN-{Guid.NewGuid():N}"[..30],
            BookingChannel = "ONLINE",
            Status = "CANCELLED",
            CancellationReason = "Bận đột xuất",
            BookedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            CancelledAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };
        Db.Appointments.Add(appointment);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var request = AuthedGet($"{BasePath}/{appointment.Id}/queue-status", tokens.AccessToken);
            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal("Lịch hẹn đã hủy nên không còn số thứ tự trong hàng đợi.", problem.Detail);
        }
        finally
        {
            await Db.Appointments.Where(a => a.Id == appointment.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GetQueueStatus_LiveQueueBucketWithServingAndEligibleWaiting_ReturnsEngineOrderedStatus()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var queueDate = DateOnly.FromDateTime(vnNow).AddDays(5);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = queueDate.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Unspecified),
            EndsAt = queueDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified),
            Capacity = 5,
            Status = "OPEN",
        };
        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var appt1 = new Appointment
        {
            PatientId = 2,
            SlotId = slot.Id,
            AppointmentCode = $"APT-Q1-{suffix}",
            BookingChannel = "ONLINE",
            Status = "CHECKED_IN",
            BookedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };
        var appt2 = new Appointment
        {
            PatientId = 3,
            SlotId = slot.Id,
            AppointmentCode = $"APT-Q2-{suffix}",
            BookingChannel = "ONLINE",
            Status = "CHECKED_IN",
            BookedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };
        var appt3 = new Appointment
        {
            PatientId = 1,
            SlotId = slot.Id,
            AppointmentCode = $"APT-Q3-{suffix}",
            BookingChannel = "ONLINE",
            Status = "CHECKED_IN",
            BookedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };
        Db.Appointments.AddRange(appt1, appt2, appt3);
        await Db.SaveChangesAsync();

        var visit1 = new Visit
        {
            AppointmentId = appt1.Id,
            PatientId = 2,
            AssignedDoctorId = 1,
            ExaminationRoomId = 1,
            VisitCode = $"VIS-Q1-{suffix}",
            Status = "IN_PROGRESS",
            CheckedInAt = queueDate.ToDateTime(new TimeOnly(7, 40), DateTimeKind.Unspecified),
        };
        var visit2 = new Visit
        {
            AppointmentId = appt2.Id,
            PatientId = 3,
            AssignedDoctorId = 1,
            ExaminationRoomId = 1,
            VisitCode = $"VIS-Q2-{suffix}",
            Status = "WAITING",
            CheckedInAt = queueDate.ToDateTime(new TimeOnly(7, 45), DateTimeKind.Unspecified),
        };
        var visit3 = new Visit
        {
            AppointmentId = appt3.Id,
            PatientId = 1,
            AssignedDoctorId = 1,
            ExaminationRoomId = 1,
            VisitCode = $"VIS-Q3-{suffix}",
            Status = "WAITING",
            CheckedInAt = queueDate.ToDateTime(new TimeOnly(7, 50), DateTimeKind.Unspecified),
        };
        Db.Visits.AddRange(visit1, visit2, visit3);
        await Db.SaveChangesAsync();

        var q1 = new QueueEntry
        {
            VisitId = visit1.Id,
            AppointmentId = appt1.Id,
            QueueDate = queueDate,
            QueueNumber = 1,
            QueueType = "EXAMINATION",
            Status = "IN_PROGRESS",
            PresenceStatus = "PRESENT",
            ArrivedAt = queueDate.ToDateTime(new TimeOnly(7, 40), DateTimeKind.Unspecified),
            StartedAt = queueDate.ToDateTime(new TimeOnly(7, 55), DateTimeKind.Unspecified),
        };
        var q2 = new QueueEntry
        {
            VisitId = visit2.Id,
            AppointmentId = appt2.Id,
            QueueDate = queueDate,
            QueueNumber = 2,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            PresenceStatus = "PRESENT",
            ArrivedAt = queueDate.ToDateTime(new TimeOnly(7, 45), DateTimeKind.Unspecified),
        };
        var q3 = new QueueEntry
        {
            VisitId = visit3.Id,
            AppointmentId = appt3.Id,
            QueueDate = queueDate,
            QueueNumber = 3,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            PresenceStatus = "PRESENT",
            ArrivedAt = queueDate.ToDateTime(new TimeOnly(7, 50), DateTimeKind.Unspecified),
        };
        Db.QueueEntries.AddRange(q1, q2, q3);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var request = AuthedGet($"{BasePath}/{appt3.Id}/queue-status", tokens.AccessToken);
            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<AppointmentQueueStatusDto>();
            Assert.NotNull(dto);

            Assert.Equal(appt3.Id.ToString(), dto.AppointmentId);
            Assert.Equal(visit3.VisitCode, dto.VisitCode);
            Assert.Equal("003", dto.QueueNumber);
            Assert.Equal("WAITING", dto.Status);
            Assert.Equal("ĐANG CHỜ KHÁM", dto.StatusLabel);
            Assert.Equal("001", dto.ServingQueueNumber);
            Assert.Equal("002", dto.NextQueueNumber);
            Assert.Equal("Ước tính chờ ~30 phút", dto.EstimatedWaitLabel);
            Assert.Equal(
                ["done", "current", "upcoming"],
                dto.VisitSteps.Select(s => s.State).ToArray());
        }
        finally
        {
            var qIds = new[] { q1.Id, q2.Id, q3.Id };
            var vIds = new[] { visit1.Id, visit2.Id, visit3.Id };
            var aIds = new[] { appt1.Id, appt2.Id, appt3.Id };

            await Db.QueueEntries.Where(q => qIds.Contains(q.Id)).ExecuteDeleteAsync();
            await Db.Visits.Where(v => vIds.Contains(v.Id)).ExecuteDeleteAsync();
            await Db.Appointments.Where(a => aIds.Contains(a.Id)).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }
}
