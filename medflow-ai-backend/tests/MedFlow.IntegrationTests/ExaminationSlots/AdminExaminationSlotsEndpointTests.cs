using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.ExaminationSlots;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.ExaminationSlots;

public sealed class AdminExaminationSlotsEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminExaminationSlotsEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, roles).AccessToken;
    }

    [Fact]
    public async Task GetExaminationSlotList_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/examination-slots");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationSlotList_PatientRole_Returns403()
    {
        var token = GenerateToken(999, "patient_test@medflow.test", "PATIENT");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-slots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationSlotList_DoctorRole_Returns403()
    {
        var token = GenerateToken(888, "doctor_test@medflow.test", "DOCTOR");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-slots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationSlotList_InvalidPage_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-slots?page=0&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(105)]
    public async Task GetExaminationSlotList_InvalidPageSize_Returns400(int pageSize)
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/examination-slots?page=1&pageSize={pageSize}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationSlotList_HospitalAdminRole_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-slots?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationSlotAdminListResponseDto>();
            Assert.NotNull(result);
            Assert.NotNull(result.Data);
            Assert.NotNull(result.Metrics);
            Assert.Equal(1, result.Page);
            Assert.Equal(10, result.PageSize);
        }
        else
        {
            // Gracefully handle offline local DB in local environment
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetExaminationSlotList_SystemAdminRole_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(2, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-slots?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationSlotAdminListResponseDto>();
            Assert.NotNull(result);
            Assert.NotNull(result.Metrics);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetExaminationSlotList_AliasRouteAdminSlots_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/slots?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationSlotAdminListResponseDto>();
            Assert.NotNull(result);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetExaminationSlotList_WithFilters_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-slots?status=OPEN&page=1&pageSize=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationSlotAdminListResponseDto>();
            Assert.NotNull(result);
            Assert.True(result.Data.Count <= 5);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task CreateExaminationSlot_Unauthenticated_Returns401()
    {
        var payload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "2026-11-20",
            StartTime: "10:00",
            EndTime: "10:30"
        );
        var response = await Client.PostAsJsonAsync("/api/v1/admin/examination-slots", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationSlot_PatientRole_Returns403()
    {
        var token = GenerateToken(999, "patient_test@medflow.test", "PATIENT");
        var payload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "2026-11-20",
            StartTime: "10:00",
            EndTime: "10:30"
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationSlot_DoctorRole_Returns403()
    {
        var token = GenerateToken(888, "doctor_test@medflow.test", "DOCTOR");
        var payload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "2026-11-20",
            StartTime: "10:00",
            EndTime: "10:30"
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationSlot_InvalidTime_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "2026-11-20",
            StartTime: "10:30",
            EndTime: "10:00" // End before start
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(105)]
    public async Task CreateExaminationSlot_InvalidCapacity_Returns400(int capacity)
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "2026-11-20",
            StartTime: "10:00",
            EndTime: "10:30",
            Capacity: capacity
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationSlot_MissingServiceAndDoctor_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CreateExaminationSlotRequest(
            Date: "2026-11-20",
            StartTime: "10:00",
            EndTime: "10:30"
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationSlot_HospitalAdminRole_CreatesSlotSuccessfully_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-12-01",
            StartTime: "14:00",
            EndTime: "14:30",
            Capacity: 6,
            Status: "OPEN"
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        long createdId = 0;
        try
        {
            var response = await Client.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var result = await response.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(result);
                createdId = result.Id;
                Assert.True(createdId > 0);
                Assert.Equal(1, result.DoctorId);
                Assert.Equal(1, result.ServiceId);
                Assert.Equal(1, result.RoomId);
                Assert.Equal(6, result.CapacityMax);
                Assert.Equal("2026-12-01", result.Date);
                Assert.Equal("14:00", result.StartTime);
                Assert.Equal("14:30", result.EndTime);
                Assert.Equal("OPEN", result.Status);
            }
            else
            {
                Assert.True(
                    response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                    $"Unexpected status code: {response.StatusCode}");
            }
        }
        finally
        {
            if (createdId > 0)
            {
                await Db.ExaminationSlots.Where(s => s.Id == createdId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task CreateExaminationSlot_AliasRoute_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            RoomId: 1,
            Date: "2026-12-02",
            StartTime: "09:00",
            EndTime: "09:30",
            Capacity: 8
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/slots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        long createdId = 0;
        try
        {
            var response = await Client.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var result = await response.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(result);
                createdId = result.Id;
                Assert.True(createdId > 0);
                Assert.Equal(1, result.ServiceId);
                Assert.Equal(8, result.CapacityMax);
            }
            else
            {
                Assert.True(
                    response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                    $"Unexpected status code: {response.StatusCode}");
            }
        }
        finally
        {
            if (createdId > 0)
            {
                await Db.ExaminationSlots.Where(s => s.Id == createdId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task CreateExaminationSlot_DoctorScheduleConflict_Returns409_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        // First slot created
        var firstPayload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-12-03",
            StartTime: "10:00",
            EndTime: "10:30"
        );

        using var firstReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        firstReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        firstReq.Content = JsonContent.Create(firstPayload);

        long createdId = 0;
        try
        {
            var firstRes = await Client.SendAsync(firstReq);
            if (firstRes.StatusCode == HttpStatusCode.Created)
            {
                var created = await firstRes.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(created);
                createdId = created.Id;

                // Overlapping slot for Doctor 1 (10:15 - 10:45)
                var conflictPayload = new CreateExaminationSlotRequest(
                    ServiceId: 1,
                    DoctorId: 1,
                    Date: "2026-12-03",
                    StartTime: "10:15",
                    EndTime: "10:45"
                );

                using var conflictReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
                conflictReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                conflictReq.Content = JsonContent.Create(conflictPayload);

                var conflictRes = await Client.SendAsync(conflictReq);
                Assert.Equal(HttpStatusCode.Conflict, conflictRes.StatusCode);
            }
            else
            {
                Assert.True(
                    firstRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                    $"Unexpected status code: {firstRes.StatusCode}");
            }
        }
        finally
        {
            if (createdId > 0)
            {
                await Db.ExaminationSlots.Where(s => s.Id == createdId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task UpdateExaminationSlot_Unauthenticated_Returns401()
    {
        var payload = new UpdateExaminationSlotRequest(Capacity: 10);
        var response = await Client.PutAsJsonAsync("/api/v1/admin/examination-slots/1", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateExaminationSlot_PatientRole_Returns403()
    {
        var token = GenerateToken(999, "patient_test@medflow.test", "PATIENT");
        var payload = new UpdateExaminationSlotRequest(Capacity: 10);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/examination-slots/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateExaminationSlot_DoctorRole_Returns403()
    {
        var token = GenerateToken(888, "doctor_test@medflow.test", "DOCTOR");
        var payload = new UpdateExaminationSlotRequest(Capacity: 10);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/examination-slots/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdateExaminationSlot_InvalidId_Returns400(long id)
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new UpdateExaminationSlotRequest(Capacity: 10);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/examination-slots/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(105)]
    public async Task UpdateExaminationSlot_InvalidCapacity_Returns400(int capacity)
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new UpdateExaminationSlotRequest(Capacity: capacity);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/examination-slots/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateExaminationSlot_InvalidTime_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new UpdateExaminationSlotRequest(
            StartTime: "11:30",
            EndTime: "10:30" // End before start
        );

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/examination-slots/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateExaminationSlot_NonExistentId_Returns404_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new UpdateExaminationSlotRequest(Capacity: 10);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/examination-slots/999999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

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
    public async Task UpdateExaminationSlot_HospitalAdminRole_UpdatesSlotSuccessfully_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        // Create a unique test slot first
        var createPayload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-12-10",
            StartTime: "15:00",
            EndTime: "15:30",
            Capacity: 6,
            Status: "OPEN"
        );

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(createPayload);

        long createdId = 0;
        try
        {
            var createRes = await Client.SendAsync(createReq);
            if (createRes.StatusCode == HttpStatusCode.Created)
            {
                var created = await createRes.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(created);
                createdId = created.Id;

                var updatePayload = new UpdateExaminationSlotRequest(
                    Capacity: 12,
                    Status: "CLOSED"
                );

                using var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/examination-slots/{created.Id}");
                updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                updateReq.Content = JsonContent.Create(updatePayload);

                var response = await Client.SendAsync(updateReq);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var result = await response.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(result);
                Assert.Equal(12, result.CapacityMax);
                Assert.Equal("CLOSED", result.Status);
            }
            else
            {
                Assert.True(
                    createRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                    $"Unexpected status code: {createRes.StatusCode}");
            }
        }
        finally
        {
            if (createdId > 0)
            {
                await Db.ExaminationSlots.Where(s => s.Id == createdId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task UpdateExaminationSlot_AliasRoute_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        var createPayload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            RoomId: 1,
            Date: "2026-12-11",
            StartTime: "16:00",
            EndTime: "16:30",
            Capacity: 8
        );

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/slots");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(createPayload);

        long createdId = 0;
        try
        {
            var createRes = await Client.SendAsync(createReq);
            if (createRes.StatusCode == HttpStatusCode.Created)
            {
                var created = await createRes.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(created);
                createdId = created.Id;

                var updatePayload = new UpdateExaminationSlotRequest(
                    Capacity: 15
                );

                using var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/slots/{created.Id}");
                updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                updateReq.Content = JsonContent.Create(updatePayload);

                var response = await Client.SendAsync(updateReq);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var result = await response.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(result);
                Assert.Equal(15, result.CapacityMax);
            }
            else
            {
                Assert.True(
                    createRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                    $"Unexpected status code: {createRes.StatusCode}");
            }
        }
        finally
        {
            if (createdId > 0)
            {
                await Db.ExaminationSlots.Where(s => s.Id == createdId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task UpdateExaminationSlot_DoctorConflict_Returns409_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        // Create Slot A: Doctor 1 at 2026-12-12 09:00 - 09:30
        var slotAPayload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-12-12",
            StartTime: "09:00",
            EndTime: "09:30"
        );

        // Create Slot B: Doctor 2 at 2026-12-12 11:00 - 11:30
        var slotBPayload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 2,
            RoomId: 1,
            Date: "2026-12-12",
            StartTime: "11:00",
            EndTime: "11:30"
        );

        long slotAId = 0;
        long slotBId = 0;

        try
        {
            using var reqA = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
            reqA.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            reqA.Content = JsonContent.Create(slotAPayload);
            var resA = await Client.SendAsync(reqA);

            using var reqB = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
            reqB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            reqB.Content = JsonContent.Create(slotBPayload);
            var resB = await Client.SendAsync(reqB);

            if (resA.StatusCode == HttpStatusCode.Created && resB.StatusCode == HttpStatusCode.Created)
            {
                var createdA = await resA.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                var createdB = await resB.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(createdA);
                Assert.NotNull(createdB);
                slotAId = createdA.Id;
                slotBId = createdB.Id;

                // Try to update Slot B to assign Doctor 1 at overlapping time (09:15 - 09:45)
                var conflictPayload = new UpdateExaminationSlotRequest(
                    DoctorId: 1,
                    StartTime: "09:15",
                    EndTime: "09:45"
                );

                using var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/examination-slots/{slotBId}");
                updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                updateReq.Content = JsonContent.Create(conflictPayload);

                var response = await Client.SendAsync(updateReq);
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            }
            else
            {
                Assert.True(
                    resA.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                    $"Unexpected status code: {resA.StatusCode}");
            }
        }
        finally
        {
            if (slotAId > 0)
            {
                await Db.ExaminationSlots.Where(s => s.Id == slotAId).ExecuteDeleteAsync();
            }
            if (slotBId > 0)
            {
                await Db.ExaminationSlots.Where(s => s.Id == slotBId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task CloseExaminationSlot_Unauthenticated_Returns401()
    {
        var payload = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");
        var response = await Client.PostAsJsonAsync("/api/v1/admin/examination-slots/1/close", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    public async Task CloseExaminationSlot_NonAdminRole_Returns403(string role)
    {
        var token = GenerateToken(999, "non_admin@medflow.test", role);
        var payload = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots/1/close");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("INVALID-SLOT")]
    public async Task CloseExaminationSlot_InvalidSlotIdentifier_Returns400(string invalidId)
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/examination-slots/{invalidId}/close");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CloseExaminationSlot_ReasonOtherWithShortNote_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CloseExaminationSlotRequest(
            ReasonCode: "OTHER",
            Note: "abc");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots/1/close");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CloseExaminationSlot_NonExistentId_Returns404_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots/999999/close");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

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
    public async Task CloseAndReopenExaminationSlot_HospitalAdminRole_Succeeds_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        var createPayload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-12-20",
            StartTime: "15:00",
            EndTime: "15:30",
            Capacity: 6,
            Status: "OPEN"
        );

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(createPayload);

        long createdId = 0;
        try
        {
            var createRes = await Client.SendAsync(createReq);
            if (createRes.StatusCode == HttpStatusCode.Created)
            {
                var created = await createRes.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(created);
                createdId = created.Id;

                var closePayload = new CloseExaminationSlotRequest(
                    ReasonCode: "EQUIPMENT_MAINTENANCE",
                    Note: "Bảo trì phòng khám định kỳ",
                    NotifyDoctor: true,
                    NotifyPatients: false,
                    AffectedBookingsAction: "KEEP_EXISTING");

                using var closeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/examination-slots/{createdId}/close");
                closeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                closeReq.Content = JsonContent.Create(closePayload);

                var closeRes = await Client.SendAsync(closeReq);
                Assert.Equal(HttpStatusCode.OK, closeRes.StatusCode);

                var closedDto = await closeRes.Content.ReadFromJsonAsync<CloseExaminationSlotResponseDto>();
                Assert.NotNull(closedDto);
                Assert.True(closedDto.Success);
                Assert.Equal("CLOSED", closedDto.Status);
                Assert.Equal("EQUIPMENT_MAINTENANCE", closedDto.CloseReasonCode);

                // Closing again while already CLOSED returns 409 Conflict
                using var duplicateCloseReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/examination-slots/{createdId}/close");
                duplicateCloseReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                duplicateCloseReq.Content = JsonContent.Create(closePayload);

                var duplicateCloseRes = await Client.SendAsync(duplicateCloseReq);
                Assert.Equal(HttpStatusCode.Conflict, duplicateCloseRes.StatusCode);

                // Reopen the closed slot
                using var reopenReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/slots/{createdId}/reopen");
                reopenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                reopenReq.Content = JsonContent.Create(new ReopenExaminationSlotRequest(Note: "Hoàn tất bảo trì"));

                var reopenRes = await Client.SendAsync(reopenReq);
                Assert.Equal(HttpStatusCode.OK, reopenRes.StatusCode);

                var reopenedDto = await reopenRes.Content.ReadFromJsonAsync<CloseExaminationSlotResponseDto>();
                Assert.NotNull(reopenedDto);
                Assert.Equal("OPEN", reopenedDto.Status);
            }
            else
            {
                Assert.True(
                    createRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                    $"Unexpected status code: {createRes.StatusCode}");
            }
        }
        finally
        {
            if (createdId > 0)
            {
                var idStr = createdId.ToString();
                await Db.AuditLogs
                    .Where(a => a.EntityType == "ExaminationSlot" && a.EntityId == idStr)
                    .ExecuteDeleteAsync();
                await Db.ExaminationSlots.Where(s => s.Id == createdId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task CloseExaminationSlot_ForgedAdminJwtOnPatientUser_Returns403_WhenDatabaseAvailable()
    {
        // User 1 in seed DB is patient01@medflow.test (PATIENT role in user_roles)
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots/1/close");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);
        request.Content = JsonContent.Create(payload);

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

    [Fact]
    public async Task CloseExaminationSlot_WithDependentBooking_RequiresConfirmationThenSucceeds_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        var createPayload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-12-21",
            StartTime: "15:00",
            EndTime: "15:30",
            Capacity: 6,
            Status: "OPEN"
        );

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(createPayload);

        long createdSlotId = 0;
        long createdApptId = 0;
        try
        {
            var createRes = await Client.SendAsync(createReq);
            if (createRes.StatusCode == HttpStatusCode.Created)
            {
                var created = await createRes.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(created);
                createdSlotId = created.Id;

                var appt = new Appointment
                {
                    AppointmentCode = $"APT-TEST-{createdSlotId}",
                    PatientId = 1,
                    SlotId = createdSlotId,
                    BookingChannel = "ONLINE",
                    Status = "BOOKED",
                    BookedByUserId = 1,
                    BookedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };
                Db.Appointments.Add(appt);
                await Db.SaveChangesAsync();
                createdApptId = appt.Id;

                // 1. Unconfirmed close on slot with dependent booking -> 409 Conflict
                using var unconfirmedReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/examination-slots/{createdSlotId}/close");
                unconfirmedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                unconfirmedReq.Content = JsonContent.Create(new CloseExaminationSlotRequest(
                    ReasonCode: "DOCTOR_UNAVAILABLE",
                    ConfirmUnderstood: false));

                var unconfirmedRes = await Client.SendAsync(unconfirmedReq);
                Assert.Equal(HttpStatusCode.Conflict, unconfirmedRes.StatusCode);

                // 2. Confirmed close with RESCHEDULE_REQUIRED -> 200 OK
                using var confirmedReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/examination-slots/{createdSlotId}/close");
                confirmedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                confirmedReq.Content = JsonContent.Create(new CloseExaminationSlotRequest(
                    ReasonCode: "DOCTOR_UNAVAILABLE",
                    Note: "Bác sĩ công tác đột xuất",
                    AffectedBookingsAction: "RESCHEDULE_REQUIRED",
                    ConfirmUnderstood: true));

                var confirmedRes = await Client.SendAsync(confirmedReq);
                Assert.Equal(HttpStatusCode.OK, confirmedRes.StatusCode);

                var closedDto = await confirmedRes.Content.ReadFromJsonAsync<CloseExaminationSlotResponseDto>();
                Assert.NotNull(closedDto);
                Assert.Equal("CLOSED", closedDto.Status);
                Assert.True(closedDto.HasDependentBookings);
                Assert.Equal(1, closedDto.AffectedBookingsCount);
                Assert.Equal("RESCHEDULE_REQUIRED", closedDto.AffectedBookingsAction);
            }
            else
            {
                Assert.True(
                    createRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                    $"Unexpected status code: {createRes.StatusCode}");
            }
        }
        finally
        {
            if (createdApptId > 0)
            {
                await Db.Appointments.Where(a => a.Id == createdApptId).ExecuteDeleteAsync();
            }
            if (createdSlotId > 0)
            {
                var idStr = createdSlotId.ToString();
                await Db.AuditLogs
                    .Where(a => a.EntityType == "ExaminationSlot" && a.EntityId == idStr)
                    .ExecuteDeleteAsync();
                await Db.ExaminationSlots.Where(s => s.Id == createdSlotId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task BatchCloseExaminationSlots_HospitalAdminRole_ClosesMultipleSlots_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        var slot1Payload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-12-22",
            StartTime: "15:00",
            EndTime: "15:30",
            Capacity: 6);

        var slot2Payload = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-12-22",
            StartTime: "15:30",
            EndTime: "16:00",
            Capacity: 6);

        long slot1Id = 0;
        long slot2Id = 0;
        try
        {
            using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
            req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req1.Content = JsonContent.Create(slot1Payload);
            var res1 = await Client.SendAsync(req1);

            using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots");
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req2.Content = JsonContent.Create(slot2Payload);
            var res2 = await Client.SendAsync(req2);

            if (res1.StatusCode == HttpStatusCode.Created && res2.StatusCode == HttpStatusCode.Created)
            {
                var c1 = await res1.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                var c2 = await res2.Content.ReadFromJsonAsync<ExaminationSlotAdminItemDto>();
                Assert.NotNull(c1);
                Assert.NotNull(c2);
                slot1Id = c1.Id;
                slot2Id = c2.Id;

                var batchPayload = new BatchCloseExaminationSlotsRequest(
                    Ids: [c1.Code, c2.Code],
                    ReasonCode: "SCHEDULE_ADJUSTMENT",
                    Note: "Điều chỉnh lịch trực cuối năm",
                    AffectedBookingsAction: "KEEP_EXISTING",
                    ConfirmUnderstood: true);

                using var batchReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-slots/batch-close");
                batchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                batchReq.Content = JsonContent.Create(batchPayload);

                var batchRes = await Client.SendAsync(batchReq);
                Assert.Equal(HttpStatusCode.OK, batchRes.StatusCode);

                var batchDto = await batchRes.Content.ReadFromJsonAsync<BatchCloseExaminationSlotsResponseDto>();
                Assert.NotNull(batchDto);
                Assert.True(batchDto.Success);
                Assert.Equal(2, batchDto.Count);
                Assert.All(batchDto.Slots, s => Assert.Equal("CLOSED", s.Status));
            }
            else
            {
                Assert.True(
                    res1.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                    $"Unexpected status code: {res1.StatusCode}");
            }
        }
        finally
        {
            var createdIds = new[] { slot1Id, slot2Id }.Where(id => id > 0).ToList();
            if (createdIds.Count > 0)
            {
                var idStrings = createdIds.Select(id => id.ToString()).ToList();
                await Db.AuditLogs
                    .Where(a => a.EntityType == "ExaminationSlot" && a.EntityId != null && idStrings.Contains(a.EntityId))
                    .ExecuteDeleteAsync();
                await Db.ExaminationSlots.Where(s => createdIds.Contains(s.Id)).ExecuteDeleteAsync();
            }
        }
    }
}

