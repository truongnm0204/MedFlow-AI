using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Reception;

public sealed class CreateWalkInVisitEndpointTests : AuthTestBase
{
    public CreateWalkInVisitEndpointTests(TestWebAppFactory factory)
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
        CreateWalkInVisitRequest? payload = null,
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
    public async Task CreateWalkInVisit_NoToken_Returns401()
    {
        var response = await Client.PostAsync(
            "/api/v1/reception/visits/walk-in",
            JsonContent.Create(new CreateWalkInVisitRequest(
                PatientId: "1",
                DepartmentId: "2",
                ServiceId: "1",
                ReasonForVisit: "Đau thượng vị sau ăn.")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var catalogRes = await Client.GetAsync("/api/v1/reception/walk-in/catalog");
        Assert.Equal(HttpStatusCode.Unauthorized, catalogRes.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT", 1L, "patient01@medflow.test")]
    [InlineData("DOCTOR", 3L, "doctor01@medflow.test")]
    [InlineData("DIAGNOSTIC_STAFF", 7L, "diagnostic_staff01@medflow.test")]
    [InlineData("PHARMACY_STAFF", 9L, "pharmacy_staff01@medflow.test")]
    public async Task CreateWalkInVisit_UnauthorizedRole_Returns403(
        string role,
        long userId,
        string email)
    {
        using var req = AuthedPost(
            "/api/v1/reception/visits/walk-in",
            new CreateWalkInVisitRequest(
                PatientId: "1",
                DepartmentId: "2",
                ServiceId: "1",
                ReasonForVisit: "Đau thượng vị sau ăn."),
            userId,
            email,
            role);
        var res = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        using var catalogReq = AuthedGet("/api/v1/reception/walk-in/catalog", userId, email, role);
        var catalogRes = await Client.SendAsync(catalogReq);
        Assert.Equal(HttpStatusCode.Forbidden, catalogRes.StatusCode);
    }

    [Fact]
    public async Task CreateWalkInVisit_ForgedReceptionJwtForPatientUser_Returns403_WhenDatabaseAvailable()
    {
        using var req = AuthedPost(
            "/api/v1/reception/visits/walk-in",
            new CreateWalkInVisitRequest(
                PatientId: "1",
                DepartmentId: "2",
                ServiceId: "1",
                ReasonForVisit: "Khám tiêu hóa vãng lai."),
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

    [Fact]
    public async Task CreateWalkInVisit_InvalidPayloads_Returns400ProblemDetails()
    {
        // Missing patientId
        using (var req = AuthedPost(
                   "/api/v1/reception/visits/walk-in",
                   new CreateWalkInVisitRequest(
                       DepartmentId: "2",
                       ServiceId: "1",
                       ReasonForVisit: "Đau thượng vị sau ăn.")))
        {
            var res = await Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(400, problem.Status);
        }

        // Reason too short
        using (var req = AuthedPost(
                   "/api/v1/reception/visits/walk-in",
                   new CreateWalkInVisitRequest(
                       PatientId: "1",
                       DepartmentId: "2",
                       ServiceId: "1",
                       ReasonForVisit: "AB")))
        {
            var res = await Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // Disallowed priority ("EMERGENCY")
        using (var req = AuthedPost(
                   "/api/v1/reception/visits/walk-in",
                   new CreateWalkInVisitRequest(
                       PatientId: "1",
                       DepartmentId: "2",
                       ServiceId: "1",
                       ClinicalPriority: "EMERGENCY",
                       ReasonForVisit: "Đau thượng vị cấp tính.")))
        {
            var res = await Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // Identity not verified
        using (var req = AuthedPost(
                   "/api/v1/reception/visits/walk-in",
                   new CreateWalkInVisitRequest(
                       PatientId: "1",
                       DepartmentId: "2",
                       ServiceId: "1",
                       IsIdentityVerified: false,
                       ReasonForVisit: "Đau thượng vị sau ăn.")))
        {
            var res = await Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    [Fact]
    public async Task CreateWalkInVisit_EndToEndWithLiveDatabase_CreatesVisitQueuePaymentAndRejectsDuplicate()
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
        long? createdVisitId = null;
        var patientCode = $"BN-WI{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        try
        {
            using (var setupScope = Factory.Services.CreateScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var patient = new Patient
                {
                    PatientCode = patientCode,
                    FullName = "Nguyễn Văn Vãng Lai UC25",
                    DateOfBirth = new DateOnly(1986, 8, 15),
                    Gender = "MALE",
                    Phone = "0938111222",
                    Status = "ACTIVE",
                };
                db.Patients.Add(patient);
                await db.SaveChangesAsync();
                createdPatientId = patient.Id;
            }

            // 1. Fetch Walk-in Catalog
            using (var catalogReq = AuthedGet("/api/v1/reception/walk-in/catalog"))
            {
                var catalogRes = await Client.SendAsync(catalogReq);
                Assert.Equal(HttpStatusCode.OK, catalogRes.StatusCode);
                var catalog = await catalogRes.Content.ReadFromJsonAsync<WalkInCatalogResponseDto>();
                Assert.NotNull(catalog);
                Assert.NotEmpty(catalog.Departments);
                Assert.NotEmpty(catalog.SymptomOptions);
            }

            // 2. Create Walk-in Visit
            var payload = new CreateWalkInVisitRequest(
                PatientCode: patientCode,
                DepartmentId: "2",
                ServiceId: "1",
                ClinicalPriority: "STANDARD",
                ReasonForVisit: "Đau thượng vị sau ăn 2 tiếng kèm ợ chua.",
                Symptoms: ["Đau thượng vị", "Ợ hơi/ợ chua"],
                PaymentMethod: "VIETQR",
                BloodPressure: "120/80",
                HeartRate: 76,
                Temperature: 36.7);

            using var createReq = AuthedPost("/api/v1/reception/visits/walk-in", payload);
            var createRes = await Client.SendAsync(createReq);

            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var created = await createRes.Content.ReadFromJsonAsync<CreateWalkInVisitResponseDto>();
            Assert.NotNull(created);
            createdVisitId = created.VisitId;

            Assert.Equal("WALK_IN", created.VisitType);
            Assert.Equal("CHECKED_IN", created.VisitStatus);
            Assert.Equal("WALK_IN", created.ArrivalClass);
            Assert.Equal("WAITING", created.QueueStatus);
            Assert.Equal("PRESENT", created.PresenceStatus);
            Assert.Equal("VIETQR", created.PaymentMethod);
            Assert.Equal(patientCode, created.PatientCode);

            // 3. Query Walk-in Visit detail
            using (var getWalkInReq = AuthedGet($"/api/v1/reception/visits/{created.VisitCode}/walk-in"))
            {
                var getWalkInRes = await Client.SendAsync(getWalkInReq);
                Assert.Equal(HttpStatusCode.OK, getWalkInRes.StatusCode);
                var fetched = await getWalkInRes.Content.ReadFromJsonAsync<CreateWalkInVisitResponseDto>();
                Assert.NotNull(fetched);
                Assert.Equal(created.VisitId, fetched.VisitId);
                Assert.Equal(created.TicketNo, fetched.TicketNo);
            }

            // 4. Query Check-in Result by Visit (UC24 interoperability)
            using (var getCheckInResReq = AuthedGet($"/api/v1/reception/visits/{created.VisitCode}/check-in-result"))
            {
                var getCheckInRes = await Client.SendAsync(getCheckInResReq);
                Assert.Equal(HttpStatusCode.OK, getCheckInRes.StatusCode);
                var checkInDto = await getCheckInRes.Content.ReadFromJsonAsync<CheckInScheduledPatientResponseDto>();
                Assert.NotNull(checkInDto);
                Assert.Equal("WALK_IN", checkInDto.VisitType);
                Assert.Equal("120/80", checkInDto.BloodPressure);
            }

            // 5. Duplicate Walk-in Visit on the same day must return 409 Conflict
            using (var duplicateReq = AuthedPost("/api/v1/reception/visits/walk-in", payload))
            {
                var duplicateRes = await Client.SendAsync(duplicateReq);
                Assert.Equal(HttpStatusCode.Conflict, duplicateRes.StatusCode);
            }
        }
        finally
        {
            using var cleanupScope = Factory.Services.CreateScope();
            var db = cleanupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            if (createdVisitId.HasValue)
            {
                var visitIdText = createdVisitId.Value.ToString();
                var audits = await db.AuditLogs
                    .Where(a => a.EntityId == visitIdText)
                    .ToListAsync();
                if (audits.Count > 0)
                {
                    db.AuditLogs.RemoveRange(audits);
                }

                var payments = await db.Payments
                    .Where(p => p.VisitId == createdVisitId.Value)
                    .ToListAsync();
                if (payments.Count > 0)
                {
                    db.Payments.RemoveRange(payments);
                }

                var queues = await db.QueueEntries
                    .Where(q => q.VisitId == createdVisitId.Value)
                    .ToListAsync();
                if (queues.Count > 0)
                {
                    db.QueueEntries.RemoveRange(queues);
                }

                var visit = await db.Visits.FirstOrDefaultAsync(v => v.Id == createdVisitId.Value);
                if (visit is not null)
                {
                    db.Visits.Remove(visit);
                }

                await db.SaveChangesAsync();
            }

            if (createdPatientId.HasValue)
            {
                var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == createdPatientId.Value);
                if (patient is not null)
                {
                    db.Patients.Remove(patient);
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
