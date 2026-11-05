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

public sealed class BookAppointmentEndpointTests : AuthTestBase
{
    private const string BookEndpoint = "/api/v1/appointments";

    public BookAppointmentEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        var response = await Client.PostAsJsonAsync(BookEndpoint, new CreateAppointmentRequest
        {
            Mode = "doctor",
            PatientId = "1",
            DoctorId = "DOC-001",
            ServiceId = "EX-GI-01",
            DateIso = "2026-11-03",
            SlotOrShiftId = "1",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_HospitalAdminRole_Returns403()
    {
        var tokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
        {
            Content = JsonContent.Create(new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-11-03",
                SlotOrShiftId = "1",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_SelfBooking_Returns201AndPersistsAppointmentAndAudit()
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

            using var request = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
            {
                Content = JsonContent.Create(new CreateAppointmentRequest
                {
                    Mode = "doctor",
                    PatientId = "1",
                    DoctorId = "DOC-001",
                    ServiceId = "EX-GI-01",
                    ServiceCode = "EX-GI-01",
                    DateIso = tomorrowIso,
                    SlotOrShiftId = slot.Id.ToString(CultureInfo.InvariantCulture),
                    SymptomNote = "Đau thượng vị kéo dài",
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<CreateAppointmentResponse>();
            Assert.NotNull(dto);

            Assert.EndsWith($"/api/v1/appointments/{dto.AppointmentId}", response.Headers.Location?.ToString());
            Assert.StartsWith("APT-", dto.AppointmentCode);
            Assert.Equal("BOOKED", dto.Status);

            var apptId = long.Parse(dto.AppointmentId, CultureInfo.InvariantCulture);
            var dbRow = await Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == apptId);
            Assert.Equal("BOOKED", dbRow.Status);
            Assert.Equal("ONLINE", dbRow.BookingChannel);
            Assert.Equal("Đau thượng vị kéo dài", dbRow.ReasonForVisit);
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
    public async Task Create_UnownedPatientId_Returns403AndDoesNotPersist()
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

            using var request = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
            {
                Content = JsonContent.Create(new CreateAppointmentRequest
                {
                    Mode = "doctor",
                    PatientId = "2", // belongs to patient02
                    DoctorId = "DOC-001",
                    ServiceId = "EX-GI-01",
                    DateIso = tomorrowIso,
                    SlotOrShiftId = slot.Id.ToString(CultureInfo.InvariantCulture),
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var count = await Db.Appointments.AsNoTracking().CountAsync(a => a.SlotId == slot.Id);
            Assert.Equal(0, count);
        }
        finally
        {
            await Db.Appointments.Where(a => a.SlotId == slot.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Create_ConcurrentRequestsOnCapacityOneSlot_ExactlyOne201AndOne409()
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
            Capacity = 1,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
            var slotIdStr = slot.Id.ToString(CultureInfo.InvariantCulture);

            HttpRequestMessage BuildRequest(string patientId)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
                {
                    Content = JsonContent.Create(new CreateAppointmentRequest
                    {
                        Mode = "doctor",
                        PatientId = patientId,
                        DoctorId = "DOC-001",
                        ServiceId = "EX-GI-01",
                        DateIso = tomorrowIso,
                        SlotOrShiftId = slotIdStr,
                    }),
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                return req;
            }

            using var req1 = BuildRequest("1");
            using var req2 = BuildRequest("3");

            var responses = await Task.WhenAll(
                Client.SendAsync(req1),
                Client.SendAsync(req2));

            var statuses = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
            Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statuses);

            var persistedCount = await Db.Appointments.AsNoTracking().CountAsync(a => a.SlotId == slot.Id);
            Assert.Equal(1, persistedCount);
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
    public async Task Create_SamePatientPostsSameSlotTwice_SecondReturns409OverlapMessage()
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
            Capacity = 3,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
            var slotIdStr = slot.Id.ToString(CultureInfo.InvariantCulture);

            HttpRequestMessage BuildRequest()
            {
                var req = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
                {
                    Content = JsonContent.Create(new CreateAppointmentRequest
                    {
                        Mode = "doctor",
                        PatientId = "1",
                        DoctorId = "DOC-001",
                        ServiceId = "EX-GI-01",
                        DateIso = tomorrowIso,
                        SlotOrShiftId = slotIdStr,
                    }),
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                return req;
            }

            using var firstReq = BuildRequest();
            var firstResp = await Client.SendAsync(firstReq);
            Assert.Equal(HttpStatusCode.Created, firstResp.StatusCode);

            using var secondReq = BuildRequest();
            var secondResp = await Client.SendAsync(secondReq);
            Assert.Equal(HttpStatusCode.Conflict, secondResp.StatusCode);

            var problem = await secondResp.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(
                "Bệnh nhân đã có lịch khám đang hoạt động trong ngày này. Vui lòng hoàn thành hoặc hủy lịch khám hiện tại trước khi đặt lịch mới.",
                problem.Detail);
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
    public async Task Create_InvalidDateIso_Returns400()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
        {
            Content = JsonContent.Create(new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-13-01",
                SlotOrShiftId = "1",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ServiceMode_UsesEarliestAvailableShiftSlot()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var targetDate = DateOnly.FromDateTime(vnNow).AddDays(20);
        var targetDateIso = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var laterSlot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = targetDate.ToDateTime(new TimeOnly(5, 30), DateTimeKind.Unspecified),
            EndsAt = targetDate.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };
        var earliestSlot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = targetDate.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Unspecified),
            EndsAt = targetDate.ToDateTime(new TimeOnly(5, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.AddRange(laterSlot, earliestSlot);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var request = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
            {
                Content = JsonContent.Create(new CreateAppointmentRequest
                {
                    Mode = "service",
                    PatientId = "1",
                    ServiceId = "EX-GI-01",
                    ServiceCode = "EX-GI-01",
                    DateIso = targetDateIso,
                    SlotOrShiftId = "shift-morning",
                    SymptomNote = "Đặt theo dịch vụ ca sáng",
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<CreateAppointmentResponse>();
            Assert.NotNull(dto);
            Assert.Equal("BOOKED", dto.Status);

            var apptId = long.Parse(dto.AppointmentId, CultureInfo.InvariantCulture);
            var dbRow = await Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == apptId);
            Assert.Equal(earliestSlot.Id, dbRow.SlotId);
        }
        finally
        {
            var apptIds = await Db.Appointments
                .Where(a => a.SlotId == earliestSlot.Id || a.SlotId == laterSlot.Id)
                .Select(a => a.Id.ToString())
                .ToListAsync();

            if (apptIds.Count > 0)
            {
                await Db.AuditLogs
                    .Where(l => l.EntityType == "APPOINTMENT" && l.EntityId != null && apptIds.Contains(l.EntityId))
                    .ExecuteDeleteAsync();
            }

            await Db.Appointments
                .Where(a => a.SlotId == earliestSlot.Id || a.SlotId == laterSlot.Id)
                .ExecuteDeleteAsync();
            await Db.ExaminationSlots
                .Where(s => s.Id == earliestSlot.Id || s.Id == laterSlot.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Create_BookingWindowExceeded_Returns400()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var beyondWindowDate = DateOnly.FromDateTime(vnNow).AddDays(31);
        var beyondWindowIso = beyondWindowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = beyondWindowDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified),
            EndsAt = beyondWindowDate.ToDateTime(new TimeOnly(9, 30), DateTimeKind.Unspecified),
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var request = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
            {
                Content = JsonContent.Create(new CreateAppointmentRequest
                {
                    Mode = "doctor",
                    PatientId = "1",
                    DoctorId = "DOC-001",
                    ServiceId = "EX-GI-01",
                    DateIso = beyondWindowIso,
                    SlotOrShiftId = slot.Id.ToString(CultureInfo.InvariantCulture),
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal("Chỉ được đặt lịch trong vòng 30 ngày tới.", problem.Detail);
        }
        finally
        {
            await Db.Appointments.Where(a => a.SlotId == slot.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Create_BookingLeadViolated_Returns400()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var startsAt = vnNow.AddMinutes(30);
        var endsAt = startsAt.AddMinutes(30);
        var slotDateIso = DateOnly.FromDateTime(startsAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Capacity = 2,
            Status = "OPEN",
        };

        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            using var request = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
            {
                Content = JsonContent.Create(new CreateAppointmentRequest
                {
                    Mode = "doctor",
                    PatientId = "1",
                    DoctorId = "DOC-001",
                    ServiceId = "EX-GI-01",
                    DateIso = slotDateIso,
                    SlotOrShiftId = slot.Id.ToString(CultureInfo.InvariantCulture),
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal("Chỉ được đặt lịch trước giờ khám tối thiểu 2 giờ.", problem.Detail);
        }
        finally
        {
            await Db.Appointments.Where(a => a.SlotId == slot.Id).ExecuteDeleteAsync();
            await Db.ExaminationSlots.Where(s => s.Id == slot.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Create_SymptomNoteOver500_Returns400()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var tomorrow = DateOnly.FromDateTime(vnNow).AddDays(1);
        var tomorrowIso = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
        {
            Content = JsonContent.Create(new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "1",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = tomorrowIso,
                SlotOrShiftId = "1",
                SymptomNote = new string('a', 501),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Lý do khám tối đa 500 ký tự.", problem.Detail);
    }

    [Fact]
    public async Task Create_ConcurrentSamePatientSameDayDifferentSlots_AtMostOneSucceeds()
    {
        var vnNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var targetDate = DateOnly.FromDateTime(vnNow).AddDays(2);
        var targetDateIso = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var slotA = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = targetDate.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Unspecified),
            EndsAt = targetDate.ToDateTime(new TimeOnly(8, 30), DateTimeKind.Unspecified),
            Capacity = 3,
            Status = "OPEN",
        };
        var slotB = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = targetDate.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Unspecified),
            EndsAt = targetDate.ToDateTime(new TimeOnly(10, 30), DateTimeKind.Unspecified),
            Capacity = 3,
            Status = "OPEN",
        };

        Db.ExaminationSlots.AddRange(slotA, slotB);
        await Db.SaveChangesAsync();

        try
        {
            var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

            HttpRequestMessage BuildRequest(long slotId)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, BookEndpoint)
                {
                    Content = JsonContent.Create(new CreateAppointmentRequest
                    {
                        Mode = "doctor",
                        PatientId = "3",
                        DoctorId = "DOC-001",
                        ServiceId = "EX-GI-01",
                        DateIso = targetDateIso,
                        SlotOrShiftId = slotId.ToString(CultureInfo.InvariantCulture),
                        SymptomNote = "Kiểm tra đặt đồng thời 2 khung giờ trong cùng ngày",
                    }),
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                return req;
            }

            using var reqA = BuildRequest(slotA.Id);
            using var reqB = BuildRequest(slotB.Id);

            var responses = await Task.WhenAll(
                Client.SendAsync(reqA),
                Client.SendAsync(reqB));

            var statuses = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
            Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statuses);

            var persistedCount = await Db.Appointments
                .AsNoTracking()
                .CountAsync(a => a.SlotId == slotA.Id || a.SlotId == slotB.Id);
            Assert.Equal(1, persistedCount);
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
}
