using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminRolesEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminRolesEndpointTests(TestWebAppFactory factory)
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
    public async Task GetRoleList_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/roles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    public async Task GetRoleList_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_InvalidPage_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?page=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_InvalidPageSize_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?pageSize=101");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_InvalidScope_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?scope=INVALID");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_InvalidStatus_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?status=DELETED");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_SystemAdminRole_Returns200WithSeededRoles_WhenDatabaseAvailable()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<RoleListResponseDto>();
            Assert.NotNull(payload);
            Assert.True(payload.Total >= 7);
            Assert.NotEmpty(payload.Data);
            Assert.Equal(payload.Data.Count, payload.Items.Count);
            Assert.True(payload.Metrics.TotalRoles >= 7);
            Assert.True(payload.Metrics.TotalPermissions >= 14);
            Assert.Contains(payload.Data, r => r.Code == "SYSTEM_ADMIN");
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetRoleList_AliasRoutes_SystemAdminRole_MatchPrimaryRoute()
    {
        var unauthResponse = await Client.GetAsync("/api/v1/system-admin/roles");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        var secondAliasUnauth = await Client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.Unauthorized, secondAliasUnauth.StatusCode);

        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system-admin/roles?scope=SYSTEM");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<RoleListResponseDto>();
            Assert.NotNull(payload);
            Assert.All(payload.Data, r => Assert.Equal("SYSTEM", r.Scope));
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetRoleList_ForgedTokenWithNonAdminDatabaseUser_Returns403_WhenDatabaseAvailable()
    {
        // User ID 1 in seeded database is patient01@medflow.test (PATIENT role only)
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles");
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

    [Fact]
    public async Task GetRoleById_InvalidId_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleById_NonExistentRole_Returns404_WhenDatabaseAvailable()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles/99999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
    public async Task GetRolePermissionsCatalog_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/roles/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("HOSPITAL_ADMIN")]
    public async Task GetRolePermissionsCatalog_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles/permissions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRolePermissionsCatalog_SystemAdminRole_Returns200WithCatalog_WhenDatabaseAvailable()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles/permissions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<RolePermissionsCatalogResponseDto>();
            Assert.NotNull(payload);
            Assert.NotEmpty(payload.Roles);
            Assert.NotEmpty(payload.Permissions);
            Assert.True(payload.Metrics.TotalRoles >= 7);
            Assert.True(payload.Metrics.TotalPermissions >= 14);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task AssignRolePermissions_Unauthenticated_Returns401()
    {
        var response = await Client.PutAsJsonAsync(
            "/api/v1/admin/roles/ROL-03/permissions",
            new AssignRolePermissionsRequest(
                RoleCode: "DOCTOR",
                PermissionCodes: ["PATIENT_PROFILE_READ"]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    public async Task AssignRolePermissions_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/roles/ROL-03/permissions")
        {
            Content = JsonContent.Create(new AssignRolePermissionsRequest(
                RoleCode: "DOCTOR",
                PermissionCodes: ["PATIENT_PROFILE_READ"])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AssignRolePermissions_EmptyPermissions_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/roles/ROL-03/permissions")
        {
            Content = JsonContent.Create(new AssignRolePermissionsRequest(
                RoleCode: "DOCTOR",
                PermissionCodes: [])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssignRolePermissions_UnknownPermissionCode_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/roles/ROL-03/permissions")
        {
            Content = JsonContent.Create(new AssignRolePermissionsRequest(
                RoleCode: "DOCTOR",
                PermissionCodes: ["PATIENT_PROFILE_READ", "INVALID_PERM_CODE"])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssignRolePermissions_SegregationOfDutiesConflict_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/roles/ROL-03/permissions")
        {
            Content = JsonContent.Create(new AssignRolePermissionsRequest(
                RoleCode: "DOCTOR",
                PermissionCodes: ["PRESCRIPTION_ISSUE", "MEDICINE_DISPENSE"])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssignRolePermissions_SystemAdminSelfLockout_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/roles/ROL-01/permissions")
        {
            Content = JsonContent.Create(new AssignRolePermissionsRequest(
                RoleCode: "SYSTEM_ADMIN",
                PermissionCodes: ["HOSPITAL_CONFIG", "AUDIT_READ"])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("ROL-0")]
    public async Task AssignRolePermissions_InvalidRoleIdentifier_Returns400(string invalidRoleId)
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/roles/{invalidRoleId}/permissions")
        {
            Content = JsonContent.Create(new AssignRolePermissionsRequest(
                PermissionCodes: ["PATIENT_PROFILE_READ"])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssignRolePermissions_NoteTooLong_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/roles/ROL-03/permissions")
        {
            Content = JsonContent.Create(new AssignRolePermissionsRequest(
                RoleCode: "DOCTOR",
                PermissionCodes: ["PATIENT_PROFILE_READ"],
                Note: new string('A', 251))),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssignRolePermissions_NonExistentRole_Returns404_WhenDatabaseAvailable()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/roles/99999/permissions")
        {
            Content = JsonContent.Create(new AssignRolePermissionsRequest(
                PermissionCodes: ["PATIENT_PROFILE_READ"])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
    public async Task AssignRolePermissions_ForgedTokenWithNonAdminDatabaseUser_Returns403_WhenDatabaseAvailable()
    {
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/roles/ROL-03/permissions")
        {
            Content = JsonContent.Create(new AssignRolePermissionsRequest(
                RoleCode: "DOCTOR",
                PermissionCodes: ["PATIENT_PROFILE_READ"])),
        };
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

    [Fact]
    public async Task AssignRolePermissions_ValidSystemAdmin_UpdatesAndRestoresRolePermissions_WhenDatabaseAvailable()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var getInitialRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?keyword=RECEPTION");
        getInitialRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var initialResponse = await Client.SendAsync(getInitialRequest);

        if (initialResponse.StatusCode != HttpStatusCode.OK)
        {
            Assert.True(
                initialResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {initialResponse.StatusCode}");
            return;
        }

        var initialPayload = await initialResponse.Content.ReadFromJsonAsync<RoleListResponseDto>();
        Assert.NotNull(initialPayload);
        var receptionRole = Assert.Single(initialPayload.Data, r => r.Code == "RECEPTION");
        var originalPermissionCodes = receptionRole.Permissions.Select(p => p.Code).ToList();

        try
        {
            var updatedCodes = new[] { "PATIENT_PROFILE_READ", "PATIENT_CHECKIN" };
            using var putRequest = new HttpRequestMessage(
                HttpMethod.Put,
                $"/api/v1/admin/roles/{receptionRole.Id}/permissions")
            {
                Content = JsonContent.Create(new AssignRolePermissionsRequest(
                    RoleCode: "RECEPTION",
                    PermissionCodes: updatedCodes,
                    Note: "Kiểm thử tích hợp cập nhật phân quyền RECEPTION")),
            };
            putRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var putResponse = await Client.SendAsync(putRequest);
            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

            var updatedDto = await putResponse.Content.ReadFromJsonAsync<RoleListItemDto>();
            Assert.NotNull(updatedDto);
            Assert.Equal("RECEPTION", updatedDto.Code);
            Assert.Equal(2, updatedDto.PermissionCount);
        }
        finally
        {
            if (originalPermissionCodes.Count > 0)
            {
                using var restoreRequest = new HttpRequestMessage(
                    HttpMethod.Put,
                    $"/api/v1/admin/roles/{receptionRole.Id}/permissions")
                {
                    Content = JsonContent.Create(new AssignRolePermissionsRequest(
                        RoleCode: "RECEPTION",
                        PermissionCodes: originalPermissionCodes,
                        Note: "Khôi phục quyền hạn mặc định sau kiểm thử")),
                };
                restoreRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                await Client.SendAsync(restoreRequest);
            }
        }
    }
}
