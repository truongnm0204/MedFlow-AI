using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminStaffAccountUpdatesPostgresTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminStaffAccountUpdatesPostgresTests(TestWebAppFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Update_ValidRequest_PersistsAggregateRevokesSessionsAndWritesAudit()
    {
        var target = await CreateTargetAsync();
        var token = GenerateToken(_factory.Services, 13, "system_admin01@medflow.test", "SYSTEM_ADMIN");
        using var request = AuthorizedRequest(
            token,
            target.StaffId,
            new UpdateStaffAccountRequest
            {
                FullName = "Nhân sự đã cập nhật",
                AuthProvider = "GOOGLE",
                Status = "LOCKED",
                Role = "RECEPTIONIST",
            });

        try
        {
            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<UpdateStaffAccountResponse>();
            Assert.NotNull(payload);
            Assert.Equal("Nhân sự đã cập nhật", payload.FullName);
            Assert.Equal("GOOGLE", payload.AuthProvider);
            Assert.Equal("LOCKED", payload.Status);
            Assert.Equal("RECEPTION", payload.RoleCode);

            Db.ChangeTracker.Clear();
            var user = await Db.Users.AsNoTracking().SingleAsync(item => item.Id == target.UserId);
            var staff = await Db.Staff.AsNoTracking().SingleAsync(item => item.Id == target.StaffId);
            var roleCode = await (
                from assignment in Db.UserRoles.AsNoTracking()
                join role in Db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                where assignment.UserId == target.UserId
                select role.Code).SingleAsync();
            var session = await Db.AuthSessions.AsNoTracking()
                .SingleAsync(item => item.UserId == target.UserId);
            var audit = await Db.AuditLogs.AsNoTracking()
                .SingleAsync(item =>
                    item.Action == "STAFF_ACCOUNT_UPDATED" &&
                    item.EntityId == target.StaffId.ToString());

            Assert.Equal(AccountStatus.LOCKED, user.AccountStatus);
            Assert.Equal("GOOGLE", user.AuthProvider);
            Assert.Null(user.PasswordHash);
            Assert.Equal("Nhân sự đã cập nhật", staff.FullName);
            Assert.Equal("RECEPTION", roleCode);
            Assert.NotNull(session.RevokedAt);
            Assert.DoesNotContain(user.Email, audit.ChangeData, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(staff.FullName, audit.ChangeData, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await CleanupAsync(target.UserId, target.StaffId);
        }
    }

    [Fact]
    public async Task Update_AuditFailure_RollsBackAggregateChanges()
    {
        var target = await CreateTargetAsync();
        using var failureFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditLogger>();
                services.AddSingleton<IAuditLogger, ThrowingAuditLogger>();
            }));
        using var client = failureFactory.CreateClient();
        var token = GenerateToken(
            failureFactory.Services,
            13,
            "system_admin01@medflow.test",
            "SYSTEM_ADMIN");
        using var request = AuthorizedRequest(
            token,
            target.StaffId,
            new UpdateStaffAccountRequest { FullName = "Không được lưu" });

        try
        {
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Db.ChangeTracker.Clear();
            var staff = await Db.Staff.AsNoTracking().SingleAsync(item => item.Id == target.StaffId);
            Assert.Equal("Nhân sự PostgreSQL test", staff.FullName);
            Assert.False(await Db.AuditLogs.AsNoTracking().AnyAsync(item =>
                item.Action == "STAFF_ACCOUNT_UPDATED" &&
                item.EntityId == target.StaffId.ToString()));
        }
        finally
        {
            await CleanupAsync(target.UserId, target.StaffId);
        }
    }

    private async Task<TargetAccount> CreateTargetAsync()
    {
        var department = await Db.Departments.AsNoTracking()
            .Where(item => item.HospitalId != null && item.IsActive == true)
            .OrderBy(item => item.Id)
            .FirstAsync();
        var doctorRoleId = await Db.Roles.AsNoTracking()
            .Where(role => role.Code == "DOCTOR")
            .Select(role => role.Id)
            .SingleAsync();
        var email = UniqueEmail();
        var user = new User
        {
            Email = email,
            PasswordHash = "integration-test-hash",
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
            EmailVerifiedAt = DateTime.UtcNow,
        };
        var staff = new Staff
        {
            User = user,
            HospitalId = department.HospitalId,
            DepartmentId = department.Id,
            StaffCode = await FindAvailableStaffCodeAsync(department.HospitalId!.Value),
            FullName = "Nhân sự PostgreSQL test",
            Status = "ACTIVE",
        };
        user.UserRoles.Add(new UserRole { User = user, RoleId = doctorRoleId });
        user.AuthSessions.Add(new AuthSession
        {
            User = user,
            RefreshTokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow,
        });

        Db.Users.Add(user);
        Db.Staff.Add(staff);
        await Db.SaveChangesAsync();
        return new TargetAccount(user.Id, staff.Id);
    }

    private async Task<string> FindAvailableStaffCodeAsync(long hospitalId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = $"STF-{RandomNumberGenerator.GetInt32(1000, 10000):D4}";
            if (!await Db.Staff.AsNoTracking().AnyAsync(item =>
                    item.HospitalId == hospitalId && item.StaffCode == candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Không tìm được mã nhân sự trống cho PostgreSQL test.");
    }

    private async Task CleanupAsync(long userId, long staffId)
    {
        Db.ChangeTracker.Clear();
        var audits = await Db.AuditLogs
            .Where(item => item.EntityId == staffId.ToString() &&
                           item.Action == "STAFF_ACCOUNT_UPDATED")
            .ToListAsync();
        var sessions = await Db.AuthSessions.Where(item => item.UserId == userId).ToListAsync();
        var assignments = await Db.UserRoles.Where(item => item.UserId == userId).ToListAsync();
        var staff = await Db.Staff.FirstOrDefaultAsync(item => item.Id == staffId);
        var user = await Db.Users.FirstOrDefaultAsync(item => item.Id == userId);

        Db.AuditLogs.RemoveRange(audits);
        Db.AuthSessions.RemoveRange(sessions);
        Db.UserRoles.RemoveRange(assignments);
        if (staff is not null)
        {
            Db.Staff.Remove(staff);
        }
        if (user is not null)
        {
            Db.Users.Remove(user);
        }
        await Db.SaveChangesAsync();
    }

    private static HttpRequestMessage AuthorizedRequest(
        string token,
        long staffId,
        UpdateStaffAccountRequest payload)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/admin/staff-accounts/{staffId}")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

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

    private sealed record TargetAccount(long UserId, long StaffId);

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
