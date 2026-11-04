using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    private HttpRequestMessage AuthedPut(
        string idOrCode,
        UpdateDoctorAdminRequest body,
        long userId = 11,
        string email = "hospital_admin01@medflow.test",
        string role = "HOSPITAL_ADMIN")
    {
        var token = GenerateToken(userId, email, role);
        var request = new HttpRequestMessage(HttpMethod.Put, $"{Endpoint}/{idOrCode}")
        {
            Content = JsonContent.Create(body),
        };
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

    [Fact]
    public async Task UpdateDoctor_NoToken_Returns401()
    {
        var response = await Client.PutAsJsonAsync(
            $"{Endpoint}/1",
            new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKII"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT", 1L, "patient01@medflow.test")]
    [InlineData("DOCTOR", 3L, "doctor01@medflow.test")]
    [InlineData("RECEPTION", 5L, "reception01@medflow.test")]
    [InlineData("DIAGNOSTIC_STAFF", 7L, "diagnostic01@medflow.test")]
    [InlineData("PHARMACY_STAFF", 9L, "pharmacy01@medflow.test")]
    public async Task UpdateDoctor_NonAdminRole_Returns403(string role, long userId, string email)
    {
        using var request = AuthedPut(
            "1",
            new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKII"),
            userId: userId,
            email: email,
            role: role);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDoctor_ForgedAdminClaimOnNonAdminDbUser_Returns403FromLayer2Check()
    {
        // UserId 1 is a PATIENT in database; even if JWT claims HOSPITAL_ADMIN, Layer 2 rejects it
        using var request = AuthedPut(
            "1",
            new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKII"),
            userId: 1L,
            email: "patient01@medflow.test",
            role: "HOSPITAL_ADMIN");
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("invalid@code")]
    public async Task UpdateDoctor_InvalidRouteId_Returns400(string invalidId)
    {
        using var request = AuthedPut(
            invalidId,
            new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKII"));
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDoctor_InvalidPayload_Returns400()
    {
        // Empty update payload
        using var emptyReq = AuthedPut("1", new UpdateDoctorAdminRequest(Reason: "No fields"));
        var emptyResp = await Client.SendAsync(emptyReq);
        Assert.Equal(HttpStatusCode.BadRequest, emptyResp.StatusCode);

        // Invalid status
        using var badStatusReq = AuthedPut("1", new UpdateDoctorAdminRequest(Status: "INVALID_STATUS"));
        var badStatusResp = await Client.SendAsync(badStatusReq);
        Assert.Equal(HttpStatusCode.BadRequest, badStatusResp.StatusCode);

        // Negative years of experience
        using var badExpReq = AuthedPut("1", new UpdateDoctorAdminRequest(YearsOfExperience: -1));
        var badExpResp = await Client.SendAsync(badExpReq);
        Assert.Equal(HttpStatusCode.BadRequest, badExpResp.StatusCode);
    }

    [Theory]
    [InlineData("99999", null, null)]
    [InlineData("DOC-9999", null, null)]
    [InlineData("1", 99999L, null)]
    [InlineData("1", null, 99999L)]
    public async Task UpdateDoctor_MissingDoctorOrSpecialtyOrDepartment_Returns404(
        string idOrCode,
        long? specialtyId,
        long? departmentId)
    {
        var payload = new UpdateDoctorAdminRequest(
            SpecialtyId: specialtyId,
            DepartmentId: departmentId,
            ProfessionalTitle: specialtyId is null && departmentId is null ? "BS.CKII" : null);

        using var request = AuthedPut(idOrCode, payload);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDoctor_DuplicateLicenseNumber_Returns409()
    {
        // DOC-002 owns DEMO-CCHN-002
        using var request = AuthedPut(
            "DOC-001",
            new UpdateDoctorAdminRequest(LicenseNumber: "DEMO-CCHN-002"));
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDoctor_ValidUpdateByHospitalAdminAndSystemAdmin_PersistsChangesAndWritesAuditLog()
    {
        try
        {
            // 1. HOSPITAL_ADMIN updates DOC-001 by numeric ID
            var updatePayload = new UpdateDoctorAdminRequest(
                SpecialtyId: 2,
                DepartmentId: 3,
                ProfessionalTitle: "PGS.TS",
                IsActive: true,
                YearsOfExperience: 14,
                ProfessionalBio: "Chuyên gia tim mạch và nội tiêu hóa.",
                QualificationsSummary: "PGS.TS Y học",
                Reason: "Cập nhật chuyên khoa và học hàm định kỳ");

            using var putReq = AuthedPut("1", updatePayload);
            var response = await Client.SendAsync(putReq);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var dto = await response.Content.ReadFromJsonAsync<DoctorAdminListItemDto>();
            Assert.NotNull(dto);
            Assert.Equal(1L, dto.Id);
            Assert.Equal("DOC-001", dto.DoctorCode);
            Assert.Equal(2L, dto.SpecialtyId);
            Assert.Equal("Tim mạch", dto.SpecialtyName);
            Assert.Equal(3L, dto.DepartmentId);
            Assert.Equal("Khoa Tim mạch", dto.DepartmentName);
            Assert.Equal("PGS.TS", dto.Degree);
            Assert.Equal((short)14, dto.YearsOfExperience);
            Assert.Equal("Chuyên gia tim mạch và nội tiêu hóa.", dto.ProfessionalBio);
            Assert.Equal("PGS.TS Y học", dto.QualificationsSummary);
            Assert.True(dto.IsActive);

            // Verify audit log in DB
            using (var scope = Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var audit = await db.AuditLogs
                    .AsNoTracking()
                    .OrderByDescending(a => a.Id)
                    .FirstOrDefaultAsync(a => a.Action == "DOCTOR_UPDATED" && a.EntityId == "1");

                Assert.NotNull(audit);
                Assert.Equal(11L, audit.ActorUserId);
                Assert.Equal("Cập nhật chuyên khoa và học hàm định kỳ", audit.Reason);
            }

            // 2. SYSTEM_ADMIN restores DOC-001 by DoctorCode
            var restorePayload = new UpdateDoctorAdminRequest(
                SpecialtyId: 1,
                DepartmentId: 2,
                Degree: "BS.CKI",
                Status: "ACTIVE",
                YearsOfExperience: 9,
                ProfessionalBio: string.Empty,
                QualificationsSummary: string.Empty,
                Reason: "Khôi phục dữ liệu gốc");

            using var restoreReq = AuthedPut(
                "DOC-001",
                restorePayload,
                userId: 13L,
                email: "system_admin01@medflow.test",
                role: "SYSTEM_ADMIN");
            var restoreResp = await Client.SendAsync(restoreReq);

            Assert.Equal(HttpStatusCode.OK, restoreResp.StatusCode);

            var restoredDto = await restoreResp.Content.ReadFromJsonAsync<DoctorAdminListItemDto>();
            Assert.NotNull(restoredDto);
            Assert.Equal(1L, restoredDto.SpecialtyId);
            Assert.Equal(2L, restoredDto.DepartmentId);
            Assert.Equal("BS.CKI", restoredDto.Degree);
            Assert.Equal((short)9, restoredDto.YearsOfExperience);
            Assert.Null(restoredDto.ProfessionalBio);
            Assert.Null(restoredDto.QualificationsSummary);
        }
        finally
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var doc1 = await db.Doctors.Include(d => d.Staff).FirstOrDefaultAsync(d => d.Id == 1);
            if (doc1 is not null)
            {
                doc1.SpecialtyId = 1;
                doc1.ProfessionalTitle = "BS.CKI";
                doc1.IsActive = true;
                doc1.YearsOfExperience = 9;
                doc1.LicenseNumber = "DEMO-CCHN-001";
                doc1.ProfessionalBio = null;
                doc1.QualificationsSummary = null;
                if (doc1.Staff is not null)
                {
                    doc1.Staff.DepartmentId = 2;
                    doc1.Staff.FullName = "BS Nguyễn Minh Đức";
                }

                await db.SaveChangesAsync();
            }
        }
    }
}
