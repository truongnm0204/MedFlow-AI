using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Visits;
using Microsoft.EntityFrameworkCore;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Visits;

public sealed class ExaminationQueueEndpointTests : AuthTestBase
{
    private const string ReceptionQueueEndpoint = "/api/v1/reception/examination-queue";
    private const string AliasQueueEndpoint = "/api/v1/examination-queue";

    public ExaminationQueueEndpointTests(TestWebAppFactory factory)
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

    private HttpRequestMessage AuthedGetQueue(
        string queryString = "",
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION",
        bool useAliasRoute = false)
    {
        var token = GenerateToken(userId, email, role);
        var baseRoute = useAliasRoute ? AliasQueueEndpoint : ReceptionQueueEndpoint;
        var url = string.IsNullOrWhiteSpace(queryString)
            ? baseRoute
            : $"{baseRoute}?{queryString.TrimStart('?')}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task GetExaminationQueue_NoToken_Returns401()
    {
        var response = await Client.GetAsync(ReceptionQueueEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var aliasResponse = await Client.GetAsync(AliasQueueEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, aliasResponse.StatusCode);
    }

    [Theory]
    [InlineData(1L, "patient01@medflow.test", "PATIENT")]
    [InlineData(3L, "doctor01@medflow.test", "DOCTOR")]
    [InlineData(7L, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF")]
    [InlineData(9L, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF")]
    public async Task GetExaminationQueue_DisallowedRole_Returns403(long userId, string email, string role)
    {
        using var request = AuthedGetQueue(userId: userId, email: email, role: role);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("queueDate=2026-99-99")]
    [InlineData("servicePointId=-5")]
    [InlineData("status=INVALID_STATUS")]
    [InlineData("priority=INVALID_PRIORITY")]
    [InlineData("visitType=INVALID_VISIT_TYPE")]
    [InlineData("keyword=%3Cscript%3E")]
    public async Task GetExaminationQueue_InvalidQueryParams_Returns400ProblemDetails(string query)
    {
        using var request = AuthedGetQueue(queryString: query);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task ExaminationQueueController_HttpPipeline_Maps200AndDomainExceptionsToProblemDetails()
    {
        var customFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExaminationQueueService>();
                services.AddScoped<IExaminationQueueService, StubExaminationQueueService>();
            });
        });
        await using var _ = customFactory;
        using var client = customFactory.CreateClient();

        // 1. Primary route -> 200 OK
        using var okReq = AuthedGetQueue("queueDate=2026-10-22&servicePointId=sp-1");
        var okResp = await client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

        var dto = await okResp.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal("2026-10-22", dto.QueueDate);
        Assert.Equal(2, dto.Stats.TotalEntries);
        var sp = Assert.Single(dto.ServicePoints);
        Assert.Equal("sp-1", sp.ServicePointId);
        Assert.Equal("A001", sp.CurrentServingTicketNo);
        Assert.Equal("A002", sp.NextTicketNo);
        Assert.Equal(2, sp.Entries.Count);

        // 2. Alias route -> 200 OK
        using var aliasReq = AuthedGetQueue("queueDate=2026-10-22", useAliasRoute: true);
        var aliasResp = await client.SendAsync(aliasReq);
        Assert.Equal(HttpStatusCode.OK, aliasResp.StatusCode);

        // 3. ForbiddenException -> 403 ProblemDetails
        using var forbiddenReq = AuthedGetQueue("keyword=FORBIDDEN_TEST");
        var forbiddenResp = await client.SendAsync(forbiddenReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);

        // 4. NotFoundException -> 404 ProblemDetails
        using var notFoundReq = AuthedGetQueue("keyword=NOTFOUND_TEST");
        var notFoundResp = await client.SendAsync(notFoundReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
    }

    [Fact]
    public async Task GetExaminationQueue_LiveDatabase_ReturnsGroupedServicePointsAndServingOrder_WhenDatabaseAvailable()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        using var request = AuthedGetQueue("queueDate=2026-10-22");
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var queueDto = await response.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
        Assert.NotNull(queueDto);
        Assert.Equal("2026-10-22", queueDto.QueueDate);
        Assert.NotEmpty(queueDto.ServicePoints);
        Assert.True(queueDto.Stats.TotalEntries > 0);

        foreach (var sp in queueDto.ServicePoints)
        {
            var expectedOrder = Enumerable.Range(1, sp.Entries.Count).ToArray();
            Assert.Equal(expectedOrder, sp.Entries.Select(e => e.ServingOrder).ToArray());
        }
    }

    [Fact]
    public async Task AssignExaminationRoom_ThenGetExaminationQueue_ReflectsAssignedRoomAndUrgentPriority_WhenDatabaseAvailable()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        long createdVisitId = 0;
        var testVisitCode = $"VIS-QUEUE-{Guid.NewGuid():N}"[..24].ToUpperInvariant();

        try
        {
            using (var setupScope = Factory.Services.CreateScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var testVisit = new Visit
                {
                    VisitCode = testVisitCode,
                    PatientId = 9,
                    AppointmentId = null,
                    AssignedDoctorId = 1,
                    ExaminationRoomId = null,
                    CheckedInByStaffId = 3,
                    ReasonForVisit = "Khám tiêu hóa kiểm tra hàng đợi UC28",
                    Status = "CHECKED_IN",
                    CheckedInAt = new DateTime(2026, 10, 27, 8, 45, 0),
                };
                db.Visits.Add(testVisit);
                await db.SaveChangesAsync();
                createdVisitId = testVisit.Id;
            }

            var token = GenerateToken(5, "reception01@medflow.test", "RECEPTION");
            using var assignReq = new HttpRequestMessage(
                HttpMethod.Put,
                $"/api/v1/reception/visits/{testVisitCode}/examination-room")
            {
                Content = JsonContent.Create(new AssignExaminationRoomRequest(
                    RoomCode: "RM-002",
                    Priority: "PREGNANT",
                    Reason: "Điều phối kiểm tra hàng đợi UC28",
                    PriorityVerificationConfirmed: true)),
            };
            assignReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var assignResp = await Client.SendAsync(assignReq);
            Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);

            var assignedDto = await assignResp.Content.ReadFromJsonAsync<AssignExaminationRoomResponseDto>();
            Assert.NotNull(assignedDto);

            string expectedQueueDate;
            using (var dateScope = Factory.Services.CreateScope())
            {
                var db = dateScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var persistedQueueDate = await db.QueueEntries
                    .AsNoTracking()
                    .Where(q => q.VisitId == createdVisitId)
                    .Select(q => q.QueueDate)
                    .FirstOrDefaultAsync();
                expectedQueueDate = (persistedQueueDate ?? DateOnly.FromDateTime(assignedDto.AssignedAtUtc.AddHours(7)))
                    .ToString("yyyy-MM-dd");
            }

            using var queueReq = AuthedGetQueue($"queueDate={expectedQueueDate}&keyword={testVisitCode}");
            var queueResp = await Client.SendAsync(queueReq);
            Assert.Equal(HttpStatusCode.OK, queueResp.StatusCode);

            var queueDto = await queueResp.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
            Assert.NotNull(queueDto);
            var rm002Bucket = Assert.Single(queueDto.ServicePoints, sp => sp.RoomCode == "RM-002");
            var matchedEntry = Assert.Single(rm002Bucket.Entries, e => e.VisitCode == testVisitCode);
            Assert.Equal("PREGNANT", matchedEntry.Priority);
            Assert.Equal("WAITING", matchedEntry.Status);
            Assert.Equal("WALK_IN", matchedEntry.VisitType);
            Assert.Equal(1, matchedEntry.ServingOrder);
        }
        finally
        {
            if (createdVisitId > 0)
            {
                using var cleanupScope = Factory.Services.CreateScope();
                var db = cleanupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                var createdAudits = await db.AuditLogs
                    .Where(a =>
                        a.Action == VisitRoomAssignmentService.VisitRoomAssignedAction &&
                        a.EntityId == createdVisitId.ToString())
                    .ToListAsync();
                if (createdAudits.Count > 0)
                {
                    db.AuditLogs.RemoveRange(createdAudits);
                }

                var extraQueueEntries = await db.QueueEntries
                    .Where(q => q.VisitId == createdVisitId)
                    .ToListAsync();
                if (extraQueueEntries.Count > 0)
                {
                    db.QueueEntries.RemoveRange(extraQueueEntries);
                }

                var visitToDelete = await db.Visits.FirstOrDefaultAsync(v => v.Id == createdVisitId);
                if (visitToDelete is not null)
                {
                    db.Visits.Remove(visitToDelete);
                }

                await db.SaveChangesAsync();
            }
        }
    }

    private sealed class StubExaminationQueueService : IExaminationQueueService
    {
        public Task<ExaminationQueueResponseDto> GetExaminationQueueAsync(
            long actorUserId,
            GetExaminationQueueRequest? request = null,
            CancellationToken ct = default)
        {
            if (string.Equals(request?.Keyword, "FORBIDDEN_TEST", StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenException("Không có quyền truy cập hàng đợi của cơ sở khác.");
            }

            if (string.Equals(request?.Keyword, "NOTFOUND_TEST", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotFoundException("Hospital", 999);
            }

            return Task.FromResult(new ExaminationQueueResponseDto(
                QueueDate: request?.QueueDate ?? "2026-10-22",
                FacilityName: "Bệnh viện Đa khoa Trung tâm MedFlow",
                ShiftLabel: "Ca sáng (07:00 - 11:30)",
                GeneratedAtUtc: new DateTime(2026, 10, 22, 2, 0, 0, DateTimeKind.Utc),
                Stats: new ExaminationQueueStatsDto(
                    TotalEntries: 2,
                    WaitingCount: 1,
                    CalledOrServingCount: 1,
                    CompletedCount: 0,
                    SkippedCount: 0,
                    ActiveServicePoints: 1),
                ServicePoints:
                [
                    new ServicePointQueueDto(
                        ServicePointId: "sp-1",
                        ServicePointName: "Điểm khám Nội Tiêu hóa 101",
                        RoomCode: "RM-001",
                        RoomName: "Khám Tiêu hóa 101",
                        DepartmentName: "Khoa Tiêu hóa",
                        DoctorName: "BS.CKI Nguyễn Minh Đức",
                        IsActive: true,
                        CurrentServingTicketNo: "A001",
                        NextTicketNo: "A002",
                        WaitingCount: 1,
                        InProgressCount: 1,
                        CompletedCount: 0,
                        SkippedCount: 0,
                        Entries:
                        [
                            new ExaminationQueueEntryDto(
                                Id: "q-001",
                                QueueNumber: 1,
                                TicketNo: "A001",
                                ServingOrder: 1,
                                VisitCode: "VIS-202610-0001",
                                PatientName: "Nguyễn Văn An",
                                PatientCode: "PAT-0001",
                                VisitType: "SCHEDULED",
                                ServicePointId: "sp-1",
                                RoomCode: "RM-001",
                                RoomName: "Khám Tiêu hóa 101",
                                DepartmentName: "Khoa Tiêu hóa",
                                DoctorName: "BS.CKI Nguyễn Minh Đức",
                                Status: "IN_PROGRESS",
                                Priority: "NORMAL",
                                QueueType: "EXAMINATION",
                                PresenceStatus: "PRESENT",
                                ArrivalClass: "ON_TIME_OR_GRACE",
                                ScheduledAt: "08:00",
                                CheckedInAt: "07:50",
                                CalledAt: "08:00",
                                StartedAt: "08:03",
                                BlockedReason: null),
                            new ExaminationQueueEntryDto(
                                Id: "q-002",
                                QueueNumber: 2,
                                TicketNo: "A002",
                                ServingOrder: 2,
                                VisitCode: "VIS-202610-0002",
                                PatientName: "Trần Thị Bình",
                                PatientCode: "PAT-0002",
                                VisitType: "SCHEDULED",
                                ServicePointId: "sp-1",
                                RoomCode: "RM-001",
                                RoomName: "Khám Tiêu hóa 101",
                                DepartmentName: "Khoa Tiêu hóa",
                                DoctorName: "BS.CKI Nguyễn Minh Đức",
                                Status: "WAITING",
                                Priority: "PREGNANT",
                                QueueType: "EXAMINATION",
                                PresenceStatus: "PRESENT",
                                ArrivalClass: "ON_TIME_OR_GRACE",
                                ScheduledAt: "08:15",
                                CheckedInAt: "08:05",
                                CalledAt: null,
                                StartedAt: null,
                                BlockedReason: null),
                        ]),
                ]));
        }

        public Task<EarlyArrivalBackfillResponseDto> ApplyEarlyArrivalBackfillAsync(
            long actorUserId,
            string idOrCode,
            EarlyArrivalBackfillRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<QueueTurnActionResponseDto> MarkMissedTurnAsync(
            long actorUserId,
            string idOrCode,
            MarkMissedTurnRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<QueueTurnActionResponseDto> RestoreSkippedTurnAsync(
            long actorUserId,
            string idOrCode,
            RestoreQueueTurnRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
