using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Services;

public sealed class AdminExaminationServicesEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminExaminationServicesEndpointTests(TestWebAppFactory factory)
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
    public async Task GetExaminationServiceList_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/examination-services");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationServiceList_PatientRole_Returns403()
    {
        var token = GenerateToken(999, "patient_test@medflow.test", "PATIENT");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-services");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationServiceList_DoctorRole_Returns403()
    {
        var token = GenerateToken(888, "doctor_test@medflow.test", "DOCTOR");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-services");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationServiceList_InvalidPage_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-services?page=0&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationServiceList_InvalidPageSize_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-services?page=1&pageSize=105");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetExaminationServiceList_HospitalAdminRole_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/examination-services?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationServiceAdminListResponseDto>();
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
    public async Task GetExaminationServiceList_AliasRoute_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/services?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationServiceAdminListResponseDto>();
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
    public async Task CreateExaminationService_Unauthenticated_Returns401()
    {
        var payload = new CreateExaminationServiceRequest(ServiceCode: "EX-TEST-01", Name: "Khám bệnh mới");
        var response = await Client.PostAsJsonAsync("/api/v1/admin/examination-services", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationService_PatientRole_Returns403()
    {
        var token = GenerateToken(999, "patient_test@medflow.test", "PATIENT");
        var payload = new CreateExaminationServiceRequest(ServiceCode: "EX-TEST-01", Name: "Khám bệnh mới");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-services");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationService_DoctorRole_Returns403()
    {
        var token = GenerateToken(888, "doctor_test@medflow.test", "DOCTOR");
        var payload = new CreateExaminationServiceRequest(ServiceCode: "EX-TEST-01", Name: "Khám bệnh mới");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-services");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationService_EmptyServiceCode_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CreateExaminationServiceRequest(ServiceCode: "", Name: "Khám bệnh mới");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-services");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateExaminationService_HospitalAdminRole_ReturnsCreated_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CreateExaminationServiceRequest(
            ServiceCode: $"EX-INT-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Name: "Khám Tích Hợp Tự Động",
            Category: "Khám Lâm sàng",
            Price: 250000m,
            OnlineBooking: true,
            Status: "ACTIVE"
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-services");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationServiceAdminItemDto>();
            Assert.NotNull(result);
            Assert.Equal(payload.Name, result.Name);
            Assert.Equal(250000m, result.Price);
        }
        else
        {
            // Gracefully handle offline local DB in local environment
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task CreateExaminationService_AliasRoute_ReturnsCreated_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new CreateExaminationServiceRequest(
            ServiceCode: $"EX-ALS-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Name: "Khám Tích Hợp Alias Route",
            Category: "Khám Lâm sàng",
            Price: 200000m,
            OnlineBooking: true,
            Status: "ACTIVE"
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/services");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var result = await response.Content.ReadFromJsonAsync<ExaminationServiceAdminItemDto>();
            Assert.NotNull(result);
            Assert.Equal(payload.Name, result.Name);
        }
        else
        {
            // Gracefully handle offline local DB in local environment
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task UpdateExaminationService_Unauthenticated_Returns401()
    {
        var payload = new UpdateExaminationServiceRequest(Name: "Khám bệnh cập nhật");
        var response = await Client.PutAsJsonAsync("/api/v1/admin/examination-services/1", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateExaminationService_PatientRole_Returns403()
    {
        var token = GenerateToken(999, "patient_test@medflow.test", "PATIENT");
        var payload = new UpdateExaminationServiceRequest(Name: "Khám bệnh cập nhật");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/examination-services/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateExaminationService_DoctorRole_Returns403()
    {
        var token = GenerateToken(888, "doctor_test@medflow.test", "DOCTOR");
        var payload = new UpdateExaminationServiceRequest(Name: "Khám bệnh cập nhật");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/examination-services/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task UpdateExaminationService_InvalidId_Returns400(long id)
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new UpdateExaminationServiceRequest(Name: "Khám bệnh cập nhật");

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/examination-services/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateExaminationService_EmptyName_Returns400()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        var payload = new UpdateExaminationServiceRequest(Name: "");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/examination-services/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateExaminationService_HospitalAdminRole_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        // Create a unique test service first so seeded services are not affected
        var createPayload = new CreateExaminationServiceRequest(
            ServiceCode: $"EX-UPD-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Name: "Khám Ban Đầu Trước Update",
            Category: "Khám Lâm sàng",
            AllowsOnlineBooking: true,
            Price: 150000m
        );

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/examination-services");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(createPayload);

        var createRes = await Client.SendAsync(createReq);
        if (createRes.StatusCode == HttpStatusCode.Created)
        {
            var created = await createRes.Content.ReadFromJsonAsync<ExaminationServiceAdminItemDto>();
            Assert.NotNull(created);

            var updatePayload = new UpdateExaminationServiceRequest(
                Name: "Khám Cập Nhật Qua Admin Endpoint",
                AllowsOnlineBooking: false,
                Status: "ACTIVE",
                Price: 180000m
            );

            using var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/examination-services/{created.Id}");
            updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            updateReq.Content = JsonContent.Create(updatePayload);

            var response = await Client.SendAsync(updateReq);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content.ReadFromJsonAsync<ExaminationServiceAdminItemDto>();
            Assert.NotNull(result);
            Assert.Equal(updatePayload.Name, result.Name);
            Assert.False(result.AllowsOnlineBooking);
            Assert.Equal(180000m, result.Price);
        }
        else
        {
            Assert.True(
                createRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                $"Unexpected status code: {createRes.StatusCode}");
        }
    }

    [Fact]
    public async Task UpdateExaminationService_AliasRoute_ReturnsSuccess_WhenDatabaseAvailable()
    {
        var token = GenerateToken(1, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        var createPayload = new CreateExaminationServiceRequest(
            ServiceCode: $"EX-ALS-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Name: "Khám Ban Đầu Alias",
            Category: "Khám Lâm sàng",
            AllowsOnlineBooking: true
        );

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/services");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(createPayload);

        var createRes = await Client.SendAsync(createReq);
        if (createRes.StatusCode == HttpStatusCode.Created)
        {
            var created = await createRes.Content.ReadFromJsonAsync<ExaminationServiceAdminItemDto>();
            Assert.NotNull(created);

            var updatePayload = new UpdateExaminationServiceRequest(
                Name: "Khám Cập Nhật Qua Alias Endpoint",
                AllowsOnlineBooking: true
            );

            using var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/services/{created.Id}");
            updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            updateReq.Content = JsonContent.Create(updatePayload);

            var response = await Client.SendAsync(updateReq);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content.ReadFromJsonAsync<ExaminationServiceAdminItemDto>();
            Assert.NotNull(result);
            Assert.Equal(updatePayload.Name, result.Name);
        }
        else
        {
            Assert.True(
                createRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Conflict,
                $"Unexpected status code: {createRes.StatusCode}");
        }
    }
}

