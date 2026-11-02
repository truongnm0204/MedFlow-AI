using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Doctors;

public sealed class AdminDoctorsEndpointTests : AuthTestBase
{
    private const string Endpoint = "/api/v1/admin/doctors";

    public AdminDoctorsEndpointTests(TestWebAppFactory factory)
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
        string query = "",
        long userId = 11,
        string email = "hospital_admin01@medflow.test",
        string role = "HOSPITAL_ADMIN")
    {
        var token = GenerateToken(userId, email, role);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task GetDoctorList_NoToken_Returns401()
    {
        var response = await Client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    public async Task GetDoctorList_NonAdminRole_Returns403(string role)
    {
        var (userId, email) = role switch
        {
            "DOCTOR" => (3L, "doctor01@medflow.test"),
            "RECEPTION" => (5L, "reception01@medflow.test"),
            _ => (999L, "patient_test@medflow.test"),
        };

        using var request = AuthedGet(userId: userId, email: email, role: role);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetDoctorList_AdminRole_Returns200WithSeededDoctors(string role)
    {
        var (userId, email) = role == "SYSTEM_ADMIN"
            ? (13L, "system_admin01@medflow.test")
            : (11L, "hospital_admin01@medflow.test");

        using var request = AuthedGet(userId: userId, email: email, role: role);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DoctorAdminListResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(2, dto.Total);
        Assert.Equal(1, dto.Page);
        Assert.Equal(10, dto.PageSize);
        Assert.Equal(1, dto.TotalPages);
        Assert.Equal(["DOC-001", "DOC-002"], dto.Data.Select(d => d.DoctorCode));
    }

    [Fact]
    public async Task GetDoctorList_MapsStaffUserDepartmentAndSpecialty()
    {
        using var request = AuthedGet();
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DoctorAdminListResponseDto>();
        Assert.NotNull(dto);

        var item = Assert.Single(dto.Data, d => d.DoctorCode == "DOC-001");
        Assert.Equal("STF-001", item.StaffCode);
        Assert.Equal("BS Nguyễn Minh Đức", item.FullName);
        Assert.Equal("doctor01@medflow.test", item.Email);
        Assert.Equal("BS.CKI", item.Degree);
        Assert.Equal(1L, item.SpecialtyId);
        Assert.Equal("Tiêu hóa", item.SpecialtyName);
        Assert.Equal(2L, item.DepartmentId);
        Assert.Equal("Khoa Tiêu hóa", item.DepartmentName);
        Assert.Equal("DEMO-CCHN-001", item.LicenseNumber);
        Assert.Equal((short)9, item.YearsOfExperience);
        Assert.True(item.IsActive);
        Assert.Null(item.Phone);
    }

    [Theory]
    [InlineData("?keyword=stf-002", "DOC-002")]
    [InlineData("?keyword=TIM", "DOC-002")]
    [InlineData("?keyword=%20demo-cchn-001%20", "DOC-001")]
    [InlineData("?departmentId=2", "DOC-001")]
    [InlineData("?specialtyId=2", "DOC-002")]
    public async Task GetDoctorList_Filter_ReturnsSingleMatch(string query, string expectedCode)
    {
        using var request = AuthedGet(query);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DoctorAdminListResponseDto>();
        Assert.NotNull(dto);

        var match = Assert.Single(dto.Data);
        Assert.Equal(expectedCode, match.DoctorCode);
    }

    [Fact]
    public async Task GetDoctorList_IsActiveFalse_ReturnsEmptyPage()
    {
        using var request = AuthedGet("?isActive=false");
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DoctorAdminListResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(0, dto.Total);
        Assert.Equal(0, dto.TotalPages);
        Assert.Empty(dto.Data);
    }

    [Fact]
    public async Task GetDoctorList_SecondPageOfSizeOne_ReturnsSecondDoctor()
    {
        using var request = AuthedGet("?page=2&pageSize=1");
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DoctorAdminListResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(2, dto.Total);
        Assert.Equal(2, dto.TotalPages);
        Assert.Equal(2, dto.Page);
        Assert.Equal(1, dto.PageSize);

        var item = Assert.Single(dto.Data);
        Assert.Equal("DOC-002", item.DoctorCode);
    }

    [Theory]
    [InlineData("?page=0&pageSize=500", 1, 100)]
    [InlineData("?page=-3&pageSize=0", 1, 10)]
    public async Task GetDoctorList_OutOfRangePaging_IsClamped(
        string query,
        int expectedPage,
        int expectedPageSize)
    {
        using var request = AuthedGet(query);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DoctorAdminListResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(expectedPage, dto.Page);
        Assert.Equal(expectedPageSize, dto.PageSize);
    }

    [Theory]
    [InlineData("?isActive=abc")]
    [InlineData("?departmentId=abc")]
    [InlineData("?page=abc")]
    public async Task GetDoctorList_MalformedQueryValue_Returns400(string query)
    {
        using var request = AuthedGet(query);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }
}
