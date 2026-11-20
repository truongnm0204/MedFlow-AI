using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminStaffAccountUpdatesEndpointTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _application;
    private readonly HttpClient _client;
    private readonly RecordingStaffAccountUpdateService _service;

    public AdminStaffAccountUpdatesEndpointTests(TestWebAppFactory factory)
    {
        _service = new RecordingStaffAccountUpdateService();
        _application = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStaffAccountUpdateService>();
                services.AddSingleton<IStaffAccountUpdateService>(_service);
            }));
        _client = _application.CreateClient();
    }

    [Fact]
    public async Task Put_Unauthenticated_Returns401WithoutCallingService()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/admin/staff-accounts/STF-0100",
            new UpdateStaffAccountRequest { FullName = "Updated" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(_service.LastCall);
    }

    [Fact]
    public async Task Put_NonSystemAdminRole_Returns403WithoutCallingService()
    {
        using var request = CreateRequest(
            HttpMethod.Put,
            "/api/v1/admin/staff-accounts/STF-0100",
            GenerateToken(2, "doctor@medflow.test", "DOCTOR"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(_service.LastCall);
    }

    [Fact]
    public async Task Put_SystemAdmin_Returns200AndPassesActorAndIdentifier()
    {
        using var request = CreateRequest(
            HttpMethod.Put,
            "/api/v1/admin/staff-accounts/STF-0100",
            GenerateToken(13, "system-admin@medflow.test", "SYSTEM_ADMIN"));

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<UpdateStaffAccountResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("STF-0100", payload.StaffCode);
        Assert.NotNull(_service.LastCall);
        Assert.Equal(13, _service.LastCall.ActorUserId);
        Assert.Equal("STF-0100", _service.LastCall.StaffIdentifier);
        Assert.Equal("Updated", _service.LastCall.Request.FullName);
    }

    [Fact]
    public async Task Patch_SystemAdminAlias_Returns200()
    {
        using var request = CreateRequest(
            HttpMethod.Patch,
            "/api/v1/system-admin/staff-accounts/2",
            GenerateToken(13, "system-admin@medflow.test", "SYSTEM_ADMIN"));
        request.Content = new StringContent(
            """{"fullName":"Updated","authMethod":"GOOGLE"}""",
            Encoding.UTF8,
            "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", _service.LastCall?.StaffIdentifier);
        Assert.Equal("GOOGLE", _service.LastCall?.Request.AuthProvider);
    }

    public void Dispose()
    {
        _client.Dispose();
        _application.Dispose();
        GC.SuppressFinalize(this);
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = _application.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(new User { Id = userId, Email = email }, roles)
            .AccessToken;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string route,
        string token)
    {
        var request = new HttpRequestMessage(method, route)
        {
            Content = JsonContent.Create(new UpdateStaffAccountRequest { FullName = "Updated" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed class RecordingStaffAccountUpdateService : IStaffAccountUpdateService
    {
        public ServiceCall? LastCall { get; private set; }

        public Task<UpdateStaffAccountResponse> UpdateAsync(
            long actorUserId,
            string staffIdentifier,
            UpdateStaffAccountRequest request,
            string? ipAddress,
            CancellationToken ct = default)
        {
            LastCall = new ServiceCall(actorUserId, staffIdentifier, request);
            return Task.FromResult(new UpdateStaffAccountResponse(
                2,
                2,
                "STF-0100",
                request.FullName ?? "Staff User",
                "staff@medflow.test",
                "LOCAL",
                "ACTIVE",
                "DOCTOR",
                1,
                "MedFlow Hà Nội",
                1,
                "Khoa Tim mạch",
                new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc)));
        }
    }

    private sealed record ServiceCall(
        long ActorUserId,
        string StaffIdentifier,
        UpdateStaffAccountRequest Request);
}
