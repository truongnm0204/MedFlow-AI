using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminStaffAccountsPostgresTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminStaffAccountsPostgresTests(TestWebAppFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_ConcurrentDuplicates_PersistsOneAggregateAndReturnsOneConflict()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");
        var email = UniqueEmail();
        var staffCode = await FindAvailableStaffCodeAsync();
        var payload = ValidRequest(staffCode, email, await FindActiveDepartmentCodeAsync());

        using var firstRequest = AuthorizedRequest(token, payload);
        using var secondRequest = AuthorizedRequest(token, payload);
        var responses = await Task.WhenAll(
            Client.SendAsync(firstRequest),
            Client.SendAsync(secondRequest));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

            Db.ChangeTracker.Clear();
            var users = await Db.Users.AsNoTracking().Where(u => u.Email == email).ToListAsync();
            var user = Assert.Single(users);
            var staff = await Db.Staff.AsNoTracking().SingleAsync(s => s.UserId == user.Id);
            Assert.Equal(staffCode, staff.StaffCode);
            Assert.Single(await Db.UserRoles.AsNoTracking().Where(ur => ur.UserId == user.Id).ToListAsync());
            Assert.Single(await Db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "STAFF_ACCOUNT_CREATED" && a.EntityId == staff.Id.ToString())
                .ToListAsync());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            await CleanupByEmailAsync(email);
        }
    }

    [Fact]
    public async Task Create_AuditFailure_RollsBackUserStaffAndRoleAssignment()
    {
        var email = UniqueEmail();
        var staffCode = await FindAvailableStaffCodeAsync();
        using var failureFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditLogger>();
                services.AddSingleton<IAuditLogger, ThrowingAuditLogger>();
            });
        });
        using var client = failureFactory.CreateClient();
        var token = GenerateToken(failureFactory.Services, 13, "system_admin01@medflow.test", "SYSTEM_ADMIN");
        using var request = AuthorizedRequest(
            token,
            ValidRequest(staffCode, email, await FindActiveDepartmentCodeAsync()));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Db.ChangeTracker.Clear();
        Assert.False(await Db.Users.AsNoTracking().AnyAsync(u => u.Email == email));
        Assert.False(await Db.Staff.AsNoTracking().AnyAsync(s => s.StaffCode == staffCode));
    }

    private string GenerateToken(long userId, string email, params string[] roles) =>
        GenerateToken(_factory.Services, userId, email, roles);

    private static string GenerateToken(
        IServiceProvider services,
        long userId,
        string email,
        params string[] roles)
    {
        using var scope = services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(
            new User { Id = userId, Email = email },
            roles).AccessToken;
    }

    private static HttpRequestMessage AuthorizedRequest(
        string token,
        CreateStaffAccountRequest payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/staff-accounts")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static CreateStaffAccountRequest ValidRequest(
        string staffCode,
        string email,
        string departmentCode) => new()
        {
            StaffCode = staffCode,
            FullName = "Nhân sự PostgreSQL test",
            AuthProvider = "LOCAL",
            Facility = "HOS-DEMO-01",
            Email = email,
            Password = "MedFlow9A",
            Status = "ACTIVE",
            Role = "RECEPTIONIST",
            Department = departmentCode,
        };

    private async Task<string> FindAvailableStaffCodeAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = $"STF-{RandomNumberGenerator.GetInt32(1000, 10000):D4}";
            if (!await Db.Staff.AsNoTracking().AnyAsync(s => s.HospitalId == 1 && s.StaffCode == candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Không tìm được mã nhân sự trống cho PostgreSQL test.");
    }

    private async Task<string> FindActiveDepartmentCodeAsync() =>
        await Db.Departments
            .AsNoTracking()
            .Where(department => department.HospitalId == 1 && department.IsActive == true)
            .Select(department => department.DepartmentCode)
            .FirstAsync();

    private async Task CleanupByEmailAsync(string email)
    {
        Db.ChangeTracker.Clear();
        var user = await Db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            return;
        }

        var staff = await Db.Staff.FirstOrDefaultAsync(s => s.UserId == user.Id);
        var userRoles = await Db.UserRoles.Where(ur => ur.UserId == user.Id).ToListAsync();
        if (staff is not null)
        {
            var audits = await Db.AuditLogs
                .Where(a => a.Action == "STAFF_ACCOUNT_CREATED" && a.EntityId == staff.Id.ToString())
                .ToListAsync();
            Db.AuditLogs.RemoveRange(audits);
            Db.Staff.Remove(staff);
        }

        Db.UserRoles.RemoveRange(userRoles);
        Db.Users.Remove(user);
        await Db.SaveChangesAsync();
    }

    private sealed class ThrowingAuditLogger : IAuditLogger
    {
        public void Add(
            long? actorUserId,
            string action,
            string entityType,
            string? entityId,
            object? changeData,
            string? ipAddress) => throw new InvalidOperationException("Injected audit failure.");
    }
}
