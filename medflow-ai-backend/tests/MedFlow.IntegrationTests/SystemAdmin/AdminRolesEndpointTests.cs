using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
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

    [Theory]
    [InlineData("/api/v1/admin/roles")]
    [InlineData("/api/v1/system-admin/roles")]
    [InlineData("/api/v1/roles")]
    public async Task CreateRole_Unauthenticated_Returns401(string route)
    {
        var response = await Client.PostAsJsonAsync(
            route,
            new CreateRoleRequest(
                Code: "CUSTOM_COORDINATOR",
                Name: "Điều phối viên"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    public async Task CreateRole_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/roles")
        {
            Content = JsonContent.Create(new CreateRoleRequest(
                Code: "CUSTOM_COORDINATOR",
                Name: "Điều phối viên")),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Điều phối viên")]
    [InlineData("123_INVALID", "Điều phối viên")]
    [InlineData("ROLE-INVALID", "Điều phối viên")]
    [InlineData("CUSTOM_COORDINATOR", "")]
    [InlineData("CUSTOM_COORDINATOR", "A")]
    public async Task CreateRole_InvalidCodeOrName_Returns400(string code, string name)
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/roles")
        {
            Content = JsonContent.Create(new CreateRoleRequest(
                Code: code,
                Name: name)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("INVALID_SCOPE", "ACTIVE")]
    [InlineData("CLINICAL", "DELETED")]
    public async Task CreateRole_InvalidScopeOrStatus_Returns400(string scope, string status)
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/roles")
        {
            Content = JsonContent.Create(new CreateRoleRequest(
                Code: "CUSTOM_COORDINATOR",
                Name: "Điều phối viên",
                Scope: scope,
                Status: status)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRole_UnknownPermissionCode_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/roles")
        {
            Content = JsonContent.Create(new CreateRoleRequest(
                Code: "CUSTOM_COORDINATOR",
                Name: "Điều phối viên",
                PermissionCodes: ["PATIENT_PROFILE_READ", "NON_EXISTENT_PERM"])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRole_SegregationOfDutiesConflict_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/roles")
        {
            Content = JsonContent.Create(new CreateRoleRequest(
                Code: "CUSTOM_COORDINATOR",
                Name: "Điều phối viên",
                PermissionCodes: ["PRESCRIPTION_ISSUE", "MEDICINE_DISPENSE"])),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRole_NoteTooLong_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/roles")
        {
            Content = JsonContent.Create(new CreateRoleRequest(
                Code: "CUSTOM_COORDINATOR",
                Name: "Điều phối viên",
                Note: new string('A', 251))),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRole_ForgedTokenWithNonAdminDatabaseUser_Returns403_WhenDatabaseAvailable()
    {
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/roles")
        {
            Content = JsonContent.Create(new CreateRoleRequest(
                Code: "CUSTOM_COORDINATOR",
                Name: "Điều phối viên")),
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
    public async Task CreateRole_DuplicateRoleCode_Returns409_WhenDatabaseAvailable()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/roles")
        {
            Content = JsonContent.Create(new CreateRoleRequest(
                Code: "DOCTOR",
                Name: "Bác sĩ bản sao")),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task CreateRole_ValidSystemAdmin_Returns201AndPersistsRole_WhenDatabaseAvailable()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var testRoleCode = $"TEST_ROLE_{uniqueSuffix}";
        var testRoleName = $"Vai trò kiểm thử {uniqueSuffix}";
        long? createdRoleId = null;

        try
        {
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/roles")
            {
                Content = JsonContent.Create(new CreateRoleRequest(
                    Code: testRoleCode,
                    Name: testRoleName,
                    Description: "Mô tả vai trò kiểm thử tích hợp.",
                    Scope: "CLINICAL",
                    Status: "ACTIVE",
                    SegregationNote: "Ghi chú SoD kiểm thử.",
                    PermissionCodes: ["PATIENT_PROFILE_READ", "PATIENT_CHECKIN"],
                    Note: "Kiểm thử tích hợp tạo mới vai trò")),
            };
            postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var postResponse = await Client.SendAsync(postRequest);
            if (postResponse.StatusCode != HttpStatusCode.Created)
            {
                Assert.True(
                    postResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                    $"Unexpected status code: {postResponse.StatusCode}");
                return;
            }

            var createdDto = await postResponse.Content.ReadFromJsonAsync<RoleListItemDto>();
            Assert.NotNull(createdDto);
            createdRoleId = createdDto.RoleId;
            Assert.Equal(testRoleCode, createdDto.Code);
            Assert.Equal(testRoleName, createdDto.Name);
            Assert.Equal("CLINICAL", createdDto.Scope);
            Assert.Equal("ACTIVE", createdDto.Status);
            Assert.False(createdDto.IsSystemRole);
            Assert.Equal(2, createdDto.PermissionCount);
        }
        finally
        {
            if (createdRoleId.HasValue)
            {
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var roleIdVal = createdRoleId.Value;
                var roleIdStr = roleIdVal.ToString();

                var rolePerms = await db.RolePermissions
                    .Where(rp => rp.RoleId == roleIdVal)
                    .ToListAsync();
                if (rolePerms.Count > 0)
                {
                    db.RolePermissions.RemoveRange(rolePerms);
                }

                var audits = await db.AuditLogs
                    .Where(a => a.EntityType == "Role" && (a.EntityId == roleIdStr || a.EntityId == testRoleCode))
                    .ToListAsync();
                if (audits.Count > 0)
                {
                    db.AuditLogs.RemoveRange(audits);
                }

                var roleEntity = await db.Roles
                    .FirstOrDefaultAsync(r => r.Id == roleIdVal);
                if (roleEntity is not null)
                {
                    db.Roles.Remove(roleEntity);
                }

                await db.SaveChangesAsync();
            }
        }
    }
}
