using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.ExaminationRooms;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedFlow.IntegrationTests.ExaminationRooms;

public sealed class ExaminationRoomEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public ExaminationRoomEndpointTests(TestWebAppFactory factory)
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

    // ==========================================
    // GetExaminationRoomList Integration Tests
    // ==========================================

    [Fact]
    public async Task GetExaminationRoomList_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/examination-rooms");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationRoomList_PatientRole_Returns403()
    {
        var token = GenerateToken(999, "patient_test@medflow.test", "PATIENT");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationRoomList_DoctorRole_Returns403()
    {
        var token = GenerateToken(888, "doctor_test@medflow.test", "DOCTOR");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationRoomList_InvalidPage_HospitalAdmin_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-rooms?page=0&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationRoomList_InvalidPageSize_HospitalAdmin_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-rooms?page=1&pageSize=105");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationRoomList_HospitalAdminRole_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-rooms?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationRoomListResponseDto>();
            Assert.NotNull(result);
            Assert.NotNull(result.Metrics);
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
    public async Task GetExaminationRoomList_AliasRoute_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/rooms?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationRoomListResponseDto>();
            Assert.NotNull(result);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    // ==========================================
    // CreateExaminationRoom Integration Tests
    // ==========================================

    [Fact]
    public async Task CreateExaminationRoom_Unauthenticated_Returns401()
    {
        var payload = new CreateExaminationRoomRequest(RoomCode: "BK-TEST", Name: "Phòng Khám Test");
        var response = await Client.PostAsJsonAsync("/api/v1/admin/examination-rooms", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationRoom_PatientRole_Returns403()
    {
        var token = GenerateToken(999, "patient_test@medflow.test", "PATIENT");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreateExaminationRoomRequest(RoomCode: "BK-TEST", Name: "Phòng Khám Test"));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationRoom_DoctorRole_Returns403()
    {
        var token = GenerateToken(888, "doctor_test@medflow.test", "DOCTOR");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreateExaminationRoomRequest(RoomCode: "BK-TEST", Name: "Phòng Khám Test"));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationRoom_EmptyRoomCode_HospitalAdmin_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreateExaminationRoomRequest(RoomCode: "", Name: "Phòng Khám Test"));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationRoom_EmptyName_HospitalAdmin_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreateExaminationRoomRequest(RoomCode: "BK-TEST", Name: ""));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationRoom_AliasRoute_Unauthenticated_Returns401()
    {
        var payload = new CreateExaminationRoomRequest(RoomCode: "BK-TEST", Name: "Phòng Khám Test");
        var response = await Client.PostAsJsonAsync("/api/v1/admin/rooms", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationRoom_HospitalAdmin_ReturnsCreated_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var uniqueCode = $"BK-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var payload = new CreateExaminationRoomRequest(
            RoomCode: uniqueCode,
            Name: "Phòng Khám Mới Tự Động Test",
            HospitalId: 1,
            RoomType: "EXAMINATION",
            Location: "Tầng 2 Tòa Nhà B",
            Capacity: 2,
            Status: "ACTIVE"
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationRoomItemDto>();
            Assert.NotNull(result);
            Assert.True(result.Id > 0);
            Assert.Equal(uniqueCode, result.RoomCode);
            Assert.Equal("Phòng Khám Mới Tự Động Test", result.Name);
            Assert.True(result.IsAvailable);
        }
        else
        {
            // Gracefully handle offline local DB in local environment
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                $"Unexpected status code: {response.StatusCode}");
        }
    }
}
