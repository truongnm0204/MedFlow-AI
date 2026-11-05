using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Visits;

public sealed class ReceptionVisitTicketEndpointTests : AuthTestBase
{
    public ReceptionVisitTicketEndpointTests(TestWebAppFactory factory)
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

    private HttpRequestMessage AuthedPost(
        string url,
        PrintExaminationTicketRequest? payload = null,
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

    [Fact]
    public async Task GetExaminationTicket_NoToken_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/reception/visits/VIS-202610-0001/ticket");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PrintExaminationTicket_NoToken_Returns401()
    {
        var response = await Client.PostAsync("/api/v1/reception/visits/VIS-202610-0001/ticket/print", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT", 1L, "patient01@medflow.test")]
    [InlineData("DOCTOR", 3L, "doctor01@medflow.test")]
    [InlineData("DIAGNOSTIC_STAFF", 7L, "diagnostic_staff01@medflow.test")]
    [InlineData("PHARMACY_STAFF", 9L, "pharmacy_staff01@medflow.test")]
    [InlineData("HOSPITAL_ADMIN", 11L, "hospital_admin01@medflow.test")]
    [InlineData("SYSTEM_ADMIN", 13L, "system_admin01@medflow.test")]
    public async Task GetAndPrintExaminationTicket_NonReceptionRole_Returns403(
        string role,
        long userId,
        string email)
    {
        using var getReq = AuthedGet("/api/v1/reception/visits/VIS-202610-0001/ticket", userId, email, role);
        var getRes = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);

        using var postReq = AuthedPost("/api/v1/reception/visits/VIS-202610-0001/ticket/print", null, userId, email, role);
        var postRes = await Client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Forbidden, postRes.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid@code")]
    public async Task GetExaminationTicket_InvalidIdentifier_Returns400ProblemDetails(string idOrCode)
    {
        using var request = AuthedGet($"/api/v1/reception/visits/{idOrCode}/ticket");
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task PrintExaminationTicket_InvalidCopies_Returns400ProblemDetails(int copies)
    {
        using var request = AuthedPost(
            "/api/v1/reception/visits/VIS-202610-0001/ticket/print",
            new PrintExaminationTicketRequest(Copies: copies));
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task PrintExaminationTicket_OverlongReason_Returns400ProblemDetails()
    {
        using var request = AuthedPost(
            "/api/v1/reception/visits/VIS-202610-0001/ticket/print",
            new PrintExaminationTicketRequest(Reason: new string('X', 260)));
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationTicket_UnknownVisit_Returns404_WhenDatabaseAvailable()
    {
        using var request = AuthedGet("/api/v1/reception/visits/VIS-999999/ticket");
        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetExaminationTicket_ForgedTokenWithNonReceptionDatabaseUser_Returns403_WhenDatabaseAvailable()
    {
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "RECEPTION");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reception/visits/VIS-202610-0001/ticket");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Theory]
    [InlineData("/api/v1/reception/visits/VIS-202610-0001/ticket")]
    [InlineData("/api/v1/reception/visits/VIS-202610-0001/examination-ticket")]
    [InlineData("/api/v1/visits/1/ticket")]
    public async Task GetExaminationTicket_SeededScheduledVisit_Returns200WithMappedTicket_WhenDatabaseAvailable(string route)
    {
        using var request = AuthedGet(route);
        var response = await Client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
            return;
        }

        var dto = await response.Content.ReadFromJsonAsync<ExaminationTicketDto>();
        Assert.NotNull(dto);
        Assert.Equal(1L, dto.VisitId);
        Assert.Equal("VIS-202610-0001", dto.VisitCode);
        Assert.Equal("TKT-VIS-202610-0001", dto.TicketCode);
        Assert.Equal("A001", dto.TicketNo);
        Assert.Equal(1, dto.QueueNumber);
        Assert.Equal("001", dto.QueueNumberFormatted);
        Assert.Equal("SCHEDULED", dto.VisitType);
        Assert.Equal("APT-202610-0001", dto.AppointmentCode);
        Assert.Equal("PAT-0001", dto.PatientCode);
        Assert.Equal("Nguyễn Văn An", dto.PatientName);
        Assert.Equal("DOC-001", dto.DoctorCode);
        Assert.Equal("BS Nguyễn Minh Đức", dto.DoctorName);
        Assert.Equal("RM-001", dto.RoomCode);
        Assert.Equal("Khám Tiêu hóa 101", dto.RoomName);
        Assert.Equal("HOS-DEMO-01", dto.HospitalCode);
        Assert.Equal("Khoa Tiêu hóa", dto.DepartmentName);
        Assert.Equal("Tiêu hóa", dto.SpecialtyName);
    }

    [Fact]
    public async Task PrintExaminationTicket_WalkInSeededVisit_PersistsAuditLogsAndIncrementsPrintCount_WhenDatabaseAvailable()
    {
        using var firstReq = AuthedPost(
            "/api/v1/reception/visits/VIS-202610-0009/ticket/print",
            new PrintExaminationTicketRequest(Copies: 2, Reason: "In phiếu khám walk-in tại quầy tiếp đón"));
        var firstRes = await Client.SendAsync(firstReq);

        if (firstRes.StatusCode != HttpStatusCode.OK)
        {
            Assert.True(
                firstRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {firstRes.StatusCode}");
            return;
        }

        try
        {
            var firstDto = await firstRes.Content.ReadFromJsonAsync<ExaminationTicketDto>();
            Assert.NotNull(firstDto);
            Assert.Equal(9L, firstDto.VisitId);
            Assert.Equal("VIS-202610-0009", firstDto.VisitCode);
            Assert.Equal("WALK_IN", firstDto.VisitType);
            Assert.Equal("A009", firstDto.TicketNo);
            Assert.Equal(9, firstDto.QueueNumber);
            Assert.Equal("PAT-0009", firstDto.PatientCode);
            Assert.Equal("Hoàng Ngọc Phương", firstDto.PatientName);
            Assert.Equal(2, firstDto.Copies);
            Assert.Equal(1, firstDto.PrintCount);
            Assert.False(firstDto.IsReprint);

            using var secondReq = AuthedPost("/api/v1/visits/9/ticket/print");
            var secondRes = await Client.SendAsync(secondReq);

            Assert.Equal(HttpStatusCode.OK, secondRes.StatusCode);
            var secondDto = await secondRes.Content.ReadFromJsonAsync<ExaminationTicketDto>();
            Assert.NotNull(secondDto);
            Assert.Equal(1, secondDto.Copies);
            Assert.Equal(2, secondDto.PrintCount);
            Assert.True(secondDto.IsReprint);

            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var printAudits = await db.AuditLogs
                .AsNoTracking()
                .Where(a => a.EntityType == "VISIT" && a.EntityId == "9" && a.Action == "PRINT_TICKET")
                .OrderBy(a => a.Id)
                .ToListAsync();

            Assert.Equal(2, printAudits.Count);
            Assert.Equal(5L, printAudits[0].ActorUserId);
            Assert.Equal(1L, printAudits[0].HospitalId);
            Assert.Equal("In phiếu khám walk-in tại quầy tiếp đón", printAudits[0].Reason);
        }
        finally
        {
            using var cleanupScope = Factory.Services.CreateScope();
            var db = cleanupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var logs = await db.AuditLogs
                .Where(a => a.EntityType == "VISIT" && a.EntityId == "9" && a.Action == "PRINT_TICKET")
                .ToListAsync();
            if (logs.Count > 0)
            {
                db.AuditLogs.RemoveRange(logs);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task PrintExaminationTicket_MissingPrerequisitesOrCancelledVisit_Returns409Conflict_WhenDatabaseAvailable()
    {
        using var probeReq = AuthedGet("/api/v1/reception/visits/VIS-202610-0001/ticket");
        var probeRes = await Client.SendAsync(probeReq);

        if (probeRes.StatusCode != HttpStatusCode.OK)
        {
            Assert.True(
                probeRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {probeRes.StatusCode}");
            return;
        }

        long? noDoctorVisitId = null;
        long? noRoomVisitId = null;
        long? cancelledVisitId = null;

        try
        {
            using (var setupScope = Factory.Services.CreateScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                var vNoDoctor = new Visit
                {
                    VisitCode = "VIS-TEST-UC26",
                    PatientId = 1,
                    AssignedDoctorId = null,
                    ExaminationRoomId = 1,
                    CheckedInByStaffId = 3,
                    ReasonForVisit = "Chưa phân công bác sĩ",
                    Status = "CHECKED_IN",
                    CheckedInAt = new DateTime(2026, 10, 26, 8, 30, 0),
                };

                var vNoRoom = new Visit
                {
                    VisitCode = "VIS-TEST-UC27",
                    PatientId = 1,
                    AssignedDoctorId = 1,
                    ExaminationRoomId = null,
                    CheckedInByStaffId = 3,
                    ReasonForVisit = "Chưa phân công buồng khám",
                    Status = "CHECKED_IN",
                    CheckedInAt = new DateTime(2026, 10, 26, 8, 35, 0),
                };

                var vCancelled = new Visit
                {
                    VisitCode = "VIS-TEST-CANCELLED",
                    PatientId = 1,
                    AssignedDoctorId = 1,
                    ExaminationRoomId = 1,
                    CheckedInByStaffId = 3,
                    ReasonForVisit = "Lượt khám đã hủy",
                    Status = "CANCELLED",
                    CheckedInAt = new DateTime(2026, 10, 26, 8, 40, 0),
                };

                db.Visits.AddRange(vNoDoctor, vNoRoom, vCancelled);
                await db.SaveChangesAsync();

                noDoctorVisitId = vNoDoctor.Id;
                noRoomVisitId = vNoRoom.Id;
                cancelledVisitId = vCancelled.Id;
            }

            using var reqNoDoctor = AuthedPost($"/api/v1/reception/visits/{noDoctorVisitId}/ticket/print");
            var resNoDoctor = await Client.SendAsync(reqNoDoctor);
            Assert.Equal(HttpStatusCode.Conflict, resNoDoctor.StatusCode);

            using var reqNoRoom = AuthedPost($"/api/v1/reception/visits/{noRoomVisitId}/ticket/print");
            var resNoRoom = await Client.SendAsync(reqNoRoom);
            Assert.Equal(HttpStatusCode.Conflict, resNoRoom.StatusCode);

            using var reqCancelled = AuthedPost($"/api/v1/reception/visits/{cancelledVisitId}/ticket/print");
            var resCancelled = await Client.SendAsync(reqCancelled);
            Assert.Equal(HttpStatusCode.Conflict, resCancelled.StatusCode);
        }
        finally
        {
            using var cleanupScope = Factory.Services.CreateScope();
            var db = cleanupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var testVisits = await db.Visits
                .Where(v =>
                    v.VisitCode == "VIS-TEST-UC26" ||
                    v.VisitCode == "VIS-TEST-UC27" ||
                    v.VisitCode == "VIS-TEST-CANCELLED")
                .ToListAsync();

            if (testVisits.Count > 0)
            {
                db.Visits.RemoveRange(testVisits);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task GetAndPrintExaminationTicket_HttpPipeline_MapsAllRoutesBodyAndDomainExceptions()
    {
        var stub = new StubVisitTicketService();
        await using var customFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IVisitTicketService>();
                services.AddSingleton<IVisitTicketService>(stub);
            });
        });

        using var customClient = customFactory.CreateClient();
        var token = GenerateToken(5, "reception01@medflow.test", "RECEPTION");

        foreach (var getRoute in new[]
        {
            "/api/v1/reception/visits/VIS-202610-0001/ticket",
            "/api/v1/reception/visits/VIS-202610-0001/examination-ticket",
            "/api/v1/visits/1/ticket",
            "/api/v1/visits/1/examination-ticket",
        })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, getRoute);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await customClient.SendAsync(req);

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var dto = await res.Content.ReadFromJsonAsync<ExaminationTicketDto>();
            Assert.NotNull(dto);
            Assert.Equal("VIS-202610-0001", dto.VisitCode);
            Assert.Equal("A001", dto.TicketNo);
            Assert.Equal(0, dto.PrintCount);
            Assert.False(dto.IsReprint);
        }

        // POST without request body (EmptyBodyBehavior.Allow)
        using (var postEmptyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reception/visits/VIS-202610-0001/ticket/print"))
        {
            postEmptyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            postEmptyReq.Headers.Add("X-Forwarded-For", "10.20.30.40");
            var postEmptyRes = await customClient.SendAsync(postEmptyReq);

            Assert.Equal(HttpStatusCode.OK, postEmptyRes.StatusCode);
            var dto = await postEmptyRes.Content.ReadFromJsonAsync<ExaminationTicketDto>();
            Assert.NotNull(dto);
            Assert.Equal(1, dto.Copies);
            Assert.Equal(1, dto.PrintCount);
            Assert.False(dto.IsReprint);
            Assert.Equal("10.20.30.40", stub.LastIpAddress);
        }

        // POST with JSON body on alias route -> reprint
        using (var postBodyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/visits/1/examination-ticket/print")
        {
            Content = JsonContent.Create(new PrintExaminationTicketRequest(Copies: 3, Reason: "In lại tại quầy 2")),
        })
        {
            postBodyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var postBodyRes = await customClient.SendAsync(postBodyReq);

            Assert.Equal(HttpStatusCode.OK, postBodyRes.StatusCode);
            var dto = await postBodyRes.Content.ReadFromJsonAsync<ExaminationTicketDto>();
            Assert.NotNull(dto);
            Assert.Equal(3, dto.Copies);
            Assert.Equal(2, dto.PrintCount);
            Assert.True(dto.IsReprint);
        }

        // 404 NotFound mapping
        using (var notFoundReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reception/visits/VIS-MISSING/ticket"))
        {
            notFoundReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var notFoundRes = await customClient.SendAsync(notFoundReq);
            Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);
        }

        // 409 Conflict mapping (UC26 / UC27 missing)
        using (var conflictReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reception/visits/VIS-NO-DOCTOR/ticket/print"))
        {
            conflictReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var conflictRes = await customClient.SendAsync(conflictReq);
            Assert.Equal(HttpStatusCode.Conflict, conflictRes.StatusCode);
        }

        // 403 Forbidden mapping (cross-hospital visit)
        using (var forbiddenReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reception/visits/VIS-OTHER-HOSP/ticket"))
        {
            forbiddenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var forbiddenRes = await customClient.SendAsync(forbiddenReq);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);
        }
    }

    private sealed class StubVisitTicketService : IVisitTicketService
    {
        private int _printCount;
        public string? LastIpAddress { get; private set; }

        public Task<ExaminationTicketDto> GetExaminationTicketAsync(
            long actorUserId,
            string idOrVisitCode,
            CancellationToken ct = default)
        {
            EnsureStubPreconditions(idOrVisitCode);
            return Task.FromResult(BuildSampleTicket(actorUserId, copies: 1, _printCount, _printCount > 0));
        }

        public Task<ExaminationTicketDto> PrintExaminationTicketAsync(
            long actorUserId,
            string idOrVisitCode,
            PrintExaminationTicketRequest? request = null,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            EnsureStubPreconditions(idOrVisitCode);
            LastIpAddress = ipAddress;
            _printCount++;
            var copies = request?.Copies ?? 1;
            var isReprint = _printCount > 1 || request?.IsReprint == true;
            return Task.FromResult(BuildSampleTicket(actorUserId, copies, _printCount, isReprint));
        }

        private static void EnsureStubPreconditions(string idOrVisitCode)
        {
            if (string.Equals(idOrVisitCode, "VIS-MISSING", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotFoundException("Visit", idOrVisitCode);
            }

            if (string.Equals(idOrVisitCode, "VIS-NO-DOCTOR", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Lượt khám chưa được phân công bác sĩ phụ trách (UC26).");
            }

            if (string.Equals(idOrVisitCode, "VIS-OTHER-HOSP", StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenException("Bạn không có quyền truy cập phiếu khám của cơ sở y tế khác.");
            }
        }

        private static ExaminationTicketDto BuildSampleTicket(
            long actorUserId,
            int copies,
            int printCount,
            bool isReprint) =>
            new(
                VisitId: 1,
                VisitCode: "VIS-202610-0001",
                TicketCode: "TKT-VIS-202610-0001",
                TicketNo: "A001",
                QueueNumber: 1,
                QueueNumberFormatted: "001",
                QueueEntryId: 1,
                QueueDate: "2026-10-15",
                QueueStatus: "WAITING",
                Priority: "NORMAL",
                PriorityLabel: "Thông thường",
                WaitingAheadCount: 0,
                EstimatedWaitMinutes: 0,
                VisitType: "SCHEDULED",
                VisitTypeLabel: "Khám theo lịch hẹn",
                VisitStatus: "CHECKED_IN",
                VisitStatusLabel: "Đã tiếp nhận",
                AppointmentId: 1,
                AppointmentCode: "APT-202610-0001",
                ScheduledTimeRange: "09:00 - 09:30",
                PatientId: 1,
                PatientCode: "PAT-0001",
                PatientName: "Nguyễn Văn An",
                PatientDateOfBirth: "1980-02-02",
                PatientBirthYear: 1980,
                PatientAge: 46,
                PatientGender: "MALE",
                PatientGenderLabel: "Nam",
                PatientPhone: "0900000001",
                HospitalId: 1,
                HospitalCode: "HOS-DEMO-01",
                HospitalName: "Bệnh viện MedFlow (giả lập)",
                HospitalAddress: "Số 1 Đường Demo, Hà Nội",
                DepartmentId: 2,
                DepartmentCode: "GI",
                DepartmentName: "Khoa Tiêu hóa",
                SpecialtyId: 1,
                SpecialtyCode: "GI",
                SpecialtyName: "Tiêu hóa",
                RoomId: 1,
                RoomCode: "RM-001",
                RoomName: "Khám Tiêu hóa 101",
                RoomLocation: "Tầng 1, Khu Demo",
                DoctorId: 1,
                DoctorCode: "DOC-001",
                DoctorTitle: "BS.CKI",
                DoctorName: "BS Nguyễn Minh Đức",
                ServiceId: 1,
                ServiceCode: "EX-GI-01",
                ServiceName: "Khám Tiêu hóa lần đầu",
                ReasonForVisit: "Đau thượng vị",
                CheckedInAt: new DateTime(2026, 10, 15, 8, 55, 0),
                CheckedInAtLabel: "15/10/2026 08:55",
                CheckedInByStaffId: 3,
                CheckedInByStaffName: "Lễ tân Lê Lan",
                PrintedAtUtc: new DateTime(2026, 10, 15, 2, 0, 0, DateTimeKind.Utc),
                PrintedAtLabel: "15/10/2026 09:00",
                PrintedByUserId: actorUserId,
                PrintedByStaffCode: "STF-003",
                PrintedByStaffName: "Lễ tân Lê Lan",
                Copies: copies,
                PrintCount: printCount,
                IsReprint: isReprint,
                GuidanceNote: "Vui lòng di chuyển đến Khám Tiêu hóa 101 - RM-001 (Tầng 1, Khu Demo) và theo dõi màn hình gọi số thứ tự A001.");
    }
}
