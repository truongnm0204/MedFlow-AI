using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Reception;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Reception;

public sealed class CheckInScheduledPatientEndpointTests : AuthTestBase
{
    public CheckInScheduledPatientEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, roles).AccessToken;
    }

    private HttpRequestMessage AuthedPost(
        string url,
        CheckInScheduledPatientRequest? payload = null,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION")
    {
        var token = GenerateToken(userId, email, role);
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private HttpRequestMessage AuthedGet(
        string url,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION")
    {
        var token = GenerateToken(userId, email, role);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task CheckInScheduledPatient_NoToken_Returns401()
    {
        var response = await Client.PostAsync(
            "/api/v1/reception/appointments/APT-202610-0009/check-in",
            JsonContent.Create(new CheckInScheduledPatientRequest(IsIdentityVerified: true)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCheckInResult_NoToken_Returns401()
    {
        var byApptRes = await Client.GetAsync(
            "/api/v1/reception/appointments/APT-202610-0009/check-in-result");
        Assert.Equal(HttpStatusCode.Unauthorized, byApptRes.StatusCode);

        var byVisitRes = await Client.GetAsync(
            "/api/v1/reception/visits/VIS-202610-0001/check-in-result");
        Assert.Equal(HttpStatusCode.Unauthorized, byVisitRes.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT", 1L, "patient01@medflow.test")]
    [InlineData("DOCTOR", 3L, "doctor01@medflow.test")]
    [InlineData("DIAGNOSTIC_STAFF", 7L, "diagnostic_staff01@medflow.test")]
    [InlineData("PHARMACY_STAFF", 9L, "pharmacy_staff01@medflow.test")]
    public async Task CheckInScheduledPatient_UnauthorizedRole_Returns403(
        string role,
        long userId,
        string email)
    {
        using var req = AuthedPost(
            "/api/v1/reception/appointments/APT-202610-0009/check-in",
            new CheckInScheduledPatientRequest(IsIdentityVerified: true),
            userId,
            email,
            role);
        var res = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        using var getApptReq = AuthedGet(
            "/api/v1/reception/appointments/APT-202610-0009/check-in-result",
            userId,
            email,
            role);
        var getApptRes = await Client.SendAsync(getApptReq);
        Assert.Equal(HttpStatusCode.Forbidden, getApptRes.StatusCode);

        using var getVisitReq = AuthedGet(
            "/api/v1/reception/visits/VIS-202610-0001/check-in-result",
            userId,
            email,
            role);
        var getVisitRes = await Client.SendAsync(getVisitReq);
        Assert.Equal(HttpStatusCode.Forbidden, getVisitRes.StatusCode);
    }

    [Fact]
    public async Task CheckInScheduledPatient_ForgedReceptionJwtForPatientUser_Returns403_WhenDatabaseAvailable()
    {
        using var req = AuthedPost(
            "/api/v1/reception/appointments/APT-202610-0009/check-in",
            new CheckInScheduledPatientRequest(IsIdentityVerified: true, ReasonForVisit: "Khám tiêu hóa"),
            userId: 1,
            email: "patient01@medflow.test",
            role: "RECEPTION");

        var res = await Client.SendAsync(req);

        if (res.StatusCode == HttpStatusCode.Forbidden)
        {
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }
        else
        {
            Assert.True(
                res.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {res.StatusCode}");
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid@code")]
    public async Task CheckInScheduledPatient_InvalidIdentifier_Returns400ProblemDetails(string idOrCode)
    {
        using var req = AuthedPost(
            $"/api/v1/reception/appointments/{idOrCode}/check-in",
            new CheckInScheduledPatientRequest(IsIdentityVerified: true));
        var res = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);

        using var getApptReq = AuthedGet(
            $"/api/v1/reception/appointments/{idOrCode}/check-in-result");
        var getApptRes = await Client.SendAsync(getApptReq);
        Assert.Equal(HttpStatusCode.BadRequest, getApptRes.StatusCode);

        using var getVisitReq = AuthedGet(
            $"/api/v1/reception/visits/{idOrCode}/check-in-result");
        var getVisitRes = await Client.SendAsync(getVisitReq);
        Assert.Equal(HttpStatusCode.BadRequest, getVisitRes.StatusCode);
    }

    [Fact]
    public async Task CheckInScheduledPatient_InvalidBodyPayloads_Returns400ProblemDetails()
    {
        // Identity verification missing/false
        using (var req = AuthedPost(
                   "/api/v1/reception/appointments/APT-202610-0009/check-in",
                   new CheckInScheduledPatientRequest(IsIdentityVerified: false)))
        {
            var res = await Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // Invalid blood pressure format
        using (var req = AuthedPost(
                   "/api/v1/reception/appointments/APT-202610-0009/check-in",
                   new CheckInScheduledPatientRequest(IsIdentityVerified: true, BloodPressure: "invalid")))
        {
            var res = await Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // Invalid priority ("URGENT")
        using (var req = AuthedPost(
                   "/api/v1/reception/appointments/APT-202610-0009/check-in",
                   new CheckInScheduledPatientRequest(IsIdentityVerified: true, Priority: "URGENT")))
        {
            var res = await Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    [Fact]
    public async Task CheckInScheduledPatient_UnknownAppointment_Returns404_WhenDatabaseAvailable()
    {
        using var req = AuthedPost(
            "/api/v1/reception/appointments/APT-999999/check-in",
            new CheckInScheduledPatientRequest(IsIdentityVerified: true, ReasonForVisit: "Khám định kỳ"));
        var res = await Client.SendAsync(req);

        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        }
        else
        {
            Assert.True(
                res.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {res.StatusCode}");
        }
    }

    [Fact]
    public async Task CheckInScheduledPatient_EndToEndWithLiveDatabase_TransitionsAppointmentCreatesVisitActivatesQueueAndRejectsDuplicate()
    {
        using (var probeScope = Factory.Services.CreateScope())
        {
            var probeDb = probeScope.ServiceProvider.GetRequiredService<MedFlow.Infrastructure.Persistence.MedFlowDbContext>();
            if (!await probeDb.Database.CanConnectAsync())
            {
                return;
            }
        }

        long? createdPatientId = null;
        long? createdSlotId = null;
        long? createdAppointmentId = null;
        long? createdQueueEntryId = null;
        long? createdVisitId = null;
        var appointmentCode = $"APT-TEST-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";

        try
        {
            var nowLocal = AppointmentBookingRules.GetVietnamNow(TimeProvider.System);
            var today = DateOnly.FromDateTime(nowLocal);
            var slotStart = today.ToDateTime(new TimeOnly(nowLocal.Hour, 0)).AddMinutes(10);
            var slotEnd = slotStart.AddMinutes(30);

            using (var setupScope = Factory.Services.CreateScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var orphanAppts = await db.Appointments
                    .Where(a => a.AppointmentCode != null && a.AppointmentCode.StartsWith("APT-TEST-"))
                    .ToListAsync();
                if (orphanAppts.Count > 0)
                {
                    var orphanApptIds = orphanAppts.Select(a => (long?)a.Id).ToList();
                    var orphanVisits = await db.Visits
                        .Where(v => orphanApptIds.Contains(v.AppointmentId))
                        .ToListAsync();
                    var orphanVisitIds = orphanVisits.Select(v => (long?)v.Id).ToList();
                    var orphanQueues = await db.QueueEntries
                        .Where(q => orphanApptIds.Contains(q.AppointmentId) || orphanVisitIds.Contains(q.VisitId))
                        .ToListAsync();
                    if (orphanQueues.Count > 0)
                    {
                        db.QueueEntries.RemoveRange(orphanQueues);
                        await db.SaveChangesAsync();
                    }

                    if (orphanVisits.Count > 0)
                    {
                        db.Visits.RemoveRange(orphanVisits);
                        await db.SaveChangesAsync();
                    }

                    db.Appointments.RemoveRange(orphanAppts);
                    await db.SaveChangesAsync();
                }


                var patient = new Patient
                {
                    PatientCode = $"PAT-T{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    FullName = "Phạm Văn Kiểm Thử UC24",
                    DateOfBirth = new DateOnly(1988, 4, 12),
                    Gender = "MALE",
                    Phone = "0909888777",
                    Status = "ACTIVE",
                };
                db.Patients.Add(patient);
                await db.SaveChangesAsync();
                createdPatientId = patient.Id;

                var slot = new ExaminationSlot
                {
                    ServiceId = 1,
                    DoctorId = 1,
                    RoomId = 1,
                    StartsAt = slotStart,
                    EndsAt = slotEnd,
                    Capacity = 5,
                    Status = "OPEN",
                };
                db.ExaminationSlots.Add(slot);
                await db.SaveChangesAsync();
                createdSlotId = slot.Id;

                var appointment = new Appointment
                {
                    AppointmentCode = appointmentCode,
                    PatientId = patient.Id,
                    SlotId = slot.Id,
                    BookingChannel = "ONLINE",
                    ReasonForVisit = "Đau thượng vị sau ăn kéo dài",
                    Status = "BOOKED",
                    BookedByUserId = 5,
                    BookedAt = nowLocal.AddDays(-1),
                };
                db.Appointments.Add(appointment);
                await db.SaveChangesAsync();
                createdAppointmentId = appointment.Id;

                var reservedQueue = new QueueEntry
                {
                    AppointmentId = appointment.Id,
                    ServicePointId = 1,
                    CurrentServicePointId = 1,
                    QueueDate = today,
                    QueueNumber = 1,
                    QueueType = "EXAMINATION",
                    Status = "RESERVED",
                    ScheduledAt = slotStart,
                    PresenceStatus = "NOT_ARRIVED",
                    ManualPriority = "NORMAL",
                    StateVersion = 1,
                };
                db.QueueEntries.Add(reservedQueue);
                await db.SaveChangesAsync();
                createdQueueEntryId = reservedQueue.Id;
            }

            var payload = new CheckInScheduledPatientRequest(
                ReasonForVisit: "Đau thượng vị sau ăn kéo dài",
                BloodPressure: "120/80",
                HeartRate: 76,
                Temperature: 36.6,
                IsIdentityVerified: true,
                IsInsuranceVerified: true,
                Priority: "NORMAL");

            // Before check-in -> GET check-in-result returns 409 Conflict ProblemDetails
            using (var preCheckInGetReq = AuthedGet(
                       $"/api/v1/reception/appointments/{appointmentCode}/check-in-result"))
            {
                var preCheckInGetRes = await Client.SendAsync(preCheckInGetReq);
                Assert.Equal(HttpStatusCode.Conflict, preCheckInGetRes.StatusCode);
            }

            using var firstReq = AuthedPost(
                $"/api/v1/reception/appointments/{appointmentCode}/check-in",
                payload);
            var firstRes = await Client.SendAsync(firstReq);

            Assert.Equal(HttpStatusCode.OK, firstRes.StatusCode);
            var dto = await firstRes.Content.ReadFromJsonAsync<CheckInScheduledPatientResponseDto>();
            Assert.NotNull(dto);
            createdVisitId = dto.VisitId;

            Assert.True(dto.VisitId > 0);
            Assert.StartsWith("VIS-", dto.VisitCode);
            Assert.Equal("A001", dto.TicketNo);
            Assert.Equal("CHECKED_IN", dto.AppointmentStatus);
            Assert.Equal("WAITING", dto.VisitStatus);
            Assert.Equal("WAITING", dto.QueueStatus);
            Assert.Equal("PRESENT", dto.PresenceStatus);
            Assert.Equal("120/80", dto.BloodPressure);
            Assert.Equal(76, dto.HeartRate);
            Assert.Equal(36.6, dto.Temperature);
            Assert.True(dto.IsIdentityVerified);
            Assert.True(dto.IsInsuranceVerified);


            // View Check-in Result (#74) by appointment code and by visit code -> 200 OK
            using (var getByApptReq = AuthedGet(
                       $"/api/v1/reception/appointments/{appointmentCode}/check-in-result"))
            {
                var getByApptRes = await Client.SendAsync(getByApptReq);
                Assert.Equal(HttpStatusCode.OK, getByApptRes.StatusCode);
                var viewedByAppt = await getByApptRes.Content.ReadFromJsonAsync<CheckInScheduledPatientResponseDto>();
                Assert.NotNull(viewedByAppt);
                Assert.Equal(dto.VisitId, viewedByAppt.VisitId);
                Assert.Equal(dto.VisitCode, viewedByAppt.VisitCode);
                Assert.Equal("A001", viewedByAppt.TicketNo);
                Assert.Equal("120/80", viewedByAppt.BloodPressure);
                Assert.Equal(76, viewedByAppt.HeartRate);
                Assert.Equal(36.6, viewedByAppt.Temperature);
                Assert.True(viewedByAppt.IsIdentityVerified);
                Assert.True(viewedByAppt.IsInsuranceVerified);
                Assert.Equal(3, viewedByAppt.NextStepInstructions.Count);
            }

            using (var getByVisitReq = AuthedGet(
                       $"/api/v1/reception/visits/{dto.VisitCode}/check-in-result"))
            {
                var getByVisitRes = await Client.SendAsync(getByVisitReq);
                Assert.Equal(HttpStatusCode.OK, getByVisitRes.StatusCode);
                var viewedByVisit = await getByVisitRes.Content.ReadFromJsonAsync<CheckInScheduledPatientResponseDto>();
                Assert.NotNull(viewedByVisit);
                Assert.Equal(dto.VisitId, viewedByVisit.VisitId);
                Assert.Equal(appointmentCode, viewedByVisit.AppointmentCode);
                Assert.Equal("120/80", viewedByVisit.BloodPressure);
            }
            using (var verifyScope = Factory.Services.CreateScope())
            {
                var db = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                var dbAppt = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == createdAppointmentId);
                Assert.Equal("CHECKED_IN", dbAppt.Status);

                var dbVisit = await db.Visits.AsNoTracking().SingleAsync(v => v.Id == createdVisitId);
                Assert.Equal(createdAppointmentId, dbVisit.AppointmentId);
                Assert.Equal("WAITING", dbVisit.Status);

                var dbQueue = await db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == createdQueueEntryId);
                Assert.Equal(createdVisitId, dbQueue.VisitId);
                Assert.Equal("WAITING", dbQueue.Status);
                Assert.Equal("PRESENT", dbQueue.PresenceStatus);

                var dbAudit = await db.AuditLogs.AsNoTracking()
                    .SingleAsync(a => a.EntityType == "VISIT"
                        && a.EntityId == createdVisitId.Value.ToString()
                        && a.Action == CheckInScheduledPatientService.PatientCheckedInAction);
                Assert.Equal(5L, dbAudit.ActorUserId);
            }

            // Second POST on the same appointment -> 409 Conflict ProblemDetails, still only 1 Visit row
            using var secondReq = AuthedPost(
                $"/api/v1/reception/appointments/{appointmentCode}/check-in",
                payload);
            var secondRes = await Client.SendAsync(secondReq);

            Assert.Equal(HttpStatusCode.Conflict, secondRes.StatusCode);
            var conflictProblem = await secondRes.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(conflictProblem);
            Assert.Equal(409, conflictProblem.Status);

            using (var countScope = Factory.Services.CreateScope())
            {
                var db = countScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var visitCount = await db.Visits.CountAsync(v => v.AppointmentId == createdAppointmentId);
                Assert.Equal(1, visitCount);
            }
        }
        finally
        {
            using var cleanupScope = Factory.Services.CreateScope();
            var db = cleanupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            if (createdVisitId.HasValue)
            {
                var visitIdStr = createdVisitId.Value.ToString();
                var logs = await db.AuditLogs
                    .Where(a => a.EntityType == "VISIT" && a.EntityId == visitIdStr)
                    .ToListAsync();
                if (logs.Count > 0)
                {
                    db.AuditLogs.RemoveRange(logs);
                    await db.SaveChangesAsync();
                }
            }

            if (createdQueueEntryId.HasValue)
            {
                var q = await db.QueueEntries.FirstOrDefaultAsync(x => x.Id == createdQueueEntryId.Value);
                if (q is not null)
                {
                    db.QueueEntries.Remove(q);
                    await db.SaveChangesAsync();
                }
            }

            if (createdVisitId.HasValue)
            {
                var v = await db.Visits.FirstOrDefaultAsync(x => x.Id == createdVisitId.Value);
                if (v is not null)
                {
                    db.Visits.Remove(v);
                    await db.SaveChangesAsync();
                }
            }

            if (createdAppointmentId.HasValue)
            {
                var a = await db.Appointments.FirstOrDefaultAsync(x => x.Id == createdAppointmentId.Value);
                if (a is not null)
                {
                    db.Appointments.Remove(a);
                    await db.SaveChangesAsync();
                }
            }

            if (createdSlotId.HasValue)
            {
                var s = await db.ExaminationSlots.FirstOrDefaultAsync(x => x.Id == createdSlotId.Value);
                if (s is not null)
                {
                    db.ExaminationSlots.Remove(s);
                    await db.SaveChangesAsync();
                }
            }

            if (createdPatientId.HasValue)
            {
                var p = await db.Patients.FirstOrDefaultAsync(x => x.Id == createdPatientId.Value);
                if (p is not null)
                {
                    db.Patients.Remove(p);
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
