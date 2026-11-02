using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.IntegrationTests.Doctors;

public sealed class DoctorsEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;

    public DoctorsEndpointTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDoctorByIdOrCode_ValidCode_Returns200WithListedPriceAndRoomLocation()
    {
        var response = await _client.GetAsync("/api/v1/doctors/DOC-001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<DoctorDetailDto>();
        Assert.NotNull(dto);
        Assert.Equal("1", dto.Id);
        Assert.Equal("DOC-001", dto.DoctorCode);
        Assert.Equal("STF-001", dto.StaffCode);
        Assert.Equal("BS.CKI Nguyễn Minh Đức", dto.Name);
        Assert.Equal("BSCKI", dto.Title);
        Assert.Equal("Khoa Tiêu hóa", dto.DepartmentName);
        Assert.Equal("Phòng 101 - Tầng 1, Khu Demo", dto.Room);
        Assert.Equal(200_000m, dto.Price);
        Assert.Equal(9, dto.YearsOfExperience);
        Assert.Equal("DEMO-CCHN-001", dto.LicenseNumber);
        Assert.Equal(3, dto.Education.Count);
        Assert.NotEmpty(dto.Hashtags);
        Assert.Equal(3, dto.WeeklySchedule.Count);
    }

    [Theory]
    [InlineData("1", "DOC-001", "Phòng 101 - Tầng 1, Khu Demo", 200_000)]
    [InlineData("DOC-002", "DOC-002", "Phòng 201 - Tầng 1, Khu Demo", 220_000)]
    [InlineData("doc-01", "DOC-001", "Phòng 101 - Tầng 1, Khu Demo", 200_000)]
    [InlineData("doc-02", "DOC-002", "Phòng 201 - Tầng 1, Khu Demo", 220_000)]
    [InlineData("STF-001", "DOC-001", "Phòng 101 - Tầng 1, Khu Demo", 200_000)]
    public async Task GetDoctorByIdOrCode_ResolvesByIdDoctorCodeStaffCodeOrLegacySlug(
        string idOrCode,
        string expectedDoctorCode,
        string expectedRoom,
        decimal expectedPrice)
    {
        var response = await _client.GetAsync($"/api/v1/doctors/{idOrCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<DoctorDetailDto>();
        Assert.NotNull(dto);
        Assert.Equal(expectedDoctorCode, dto.DoctorCode);
        Assert.Equal(expectedRoom, dto.Room);
        Assert.Equal(expectedPrice, dto.Price);
    }

    [Fact]
    public async Task GetDoctorByIdOrCode_AuthenticatedPatient_Returns200()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = "patient01@medflow.test", Password = "Demo@12345" });
        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(tokens);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/doctors/DOC-001");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<DoctorDetailDto>();
        Assert.NotNull(dto);
        Assert.Equal("DOC-001", dto.DoctorCode);
    }

    [Fact]
    public async Task GetDoctorByIdOrCode_InvalidIdentifier_Returns400ProblemDetails()
    {
        var tooLongCode = new string('A', 65);
        var response = await _client.GetAsync($"/api/v1/doctors/{tooLongCode}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task GetDoctorByIdOrCode_UnknownIdentifier_Returns404ProblemDetails()
    {
        var unknownResponse = await _client.GetAsync("/api/v1/doctors/NON-EXISTENT-DOCTOR");
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
        var unknownProblem = await unknownResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(unknownProblem);
        Assert.Equal(404, unknownProblem.Status);

        var numericResponse = await _client.GetAsync("/api/v1/doctors/999");
        Assert.Equal(HttpStatusCode.NotFound, numericResponse.StatusCode);
        var numericProblem = await numericResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(numericProblem);
        Assert.Equal(404, numericProblem.Status);
    }

    [Fact]
    public async Task SearchDoctors_Default_Returns200WithBookableDoctors()
    {
        var response = await _client.GetAsync("/api/v1/doctors");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<DoctorSearchResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(6, dto.PageSize);
        var ids = dto.Items.Select(x => x.Id).ToList();
        Assert.Contains("1", ids);
        Assert.Contains("2", ids);
    }

    [Fact]
    public async Task SearchDoctors_FilterBySpecialty_ReturnsMatchingDoctors()
    {
        var response = await _client.GetAsync("/api/v1/doctors?specialty=tieu-hoa");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<DoctorSearchResponseDto>();
        Assert.NotNull(dto);
        var ids = dto.Items.Select(x => x.Id).ToList();
        Assert.Contains("1", ids);
        Assert.DoesNotContain("2", ids);
    }

    [Fact]
    public async Task SearchDoctors_FilterByDegreeAndKeyword_ReturnsMatchingCardWithPriceAndRoom()
    {
        var response = await _client.GetAsync("/api/v1/doctors?degree=ThS&keyword=Thu%20H%C3%A0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<DoctorSearchResponseDto>();
        Assert.NotNull(dto);
        var ids = dto.Items.Select(x => x.Id).ToList();
        Assert.Contains("2", ids);
        Assert.DoesNotContain("1", ids);

        var doc2 = Assert.Single(dto.Items, x => x.Id == "2");
        Assert.Equal(220_000m, doc2.Price);
        Assert.Equal("Phòng 201 - Tầng 1, Khu Demo", doc2.Room);
    }

    [Fact]
    public async Task SearchDoctors_AuthenticatedPatient_Returns200()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = "patient01@medflow.test", Password = "Demo@12345" });
        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(tokens);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/doctors");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<DoctorSearchResponseDto>();
        Assert.NotNull(dto);
        Assert.NotEmpty(dto.Items);
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?page=0")]
    [InlineData("?degree=XYZ")]
    [InlineData("?specialty=tim%20mach!")]
    public async Task SearchDoctors_InvalidQuery_Returns400ProblemDetails(string query)
    {
        var response = await _client.GetAsync($"/api/v1/doctors{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }
}
