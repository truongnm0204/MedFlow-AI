using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.DTOs.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>Refresh token (auth_sessions): rotation, reuse detection, expiry, logout.</summary>
public sealed class AuthRefreshTests(TestWebAppFactory factory) : AuthTestBase(factory)
{
    private static string Hash(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    [Fact]
    public async Task Login_Returns_AccessToken60Min_And_RefreshToken()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        Assert.InRange(tokens.AccessTokenExpiresIn, 3590, 3600);
        Assert.InRange(tokens.RefreshTokenExpiresIn, 604790, 604800);

        var row = await Db.AuthSessions
            .FirstOrDefaultAsync(rt => rt.RefreshTokenHash == Hash(tokens.RefreshToken));
        Assert.NotNull(row);
        Assert.Null(row.RevokedAt);
        Assert.True(row.ExpiresAt > DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewTokens_AndRotates()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });
        response.EnsureSuccessStatusCode();
        var refreshed = await response.Content.ReadFromJsonAsync<RefreshResponse>();

        Assert.NotNull(refreshed);
        Assert.NotEqual(tokens.RefreshToken, refreshed.RefreshToken);

        var old = await Db.AuthSessions
            .FirstAsync(rt => rt.RefreshTokenHash == Hash(tokens.RefreshToken));
        Assert.NotNull(old.RevokedAt);
        Assert.Equal(Hash(refreshed.RefreshToken), old.ReplacedByTokenHash);
    }

    [Fact]
    public async Task Refresh_OldTokenReuse_RevokesFamily()
    {
        var (email, tokens) = await RegisterVerifyLoginAsync();

        // Rotate hợp lệ → token cũ bị revoke + family mới.
        var first = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });
        first.EnsureSuccessStatusCode();

        // Dùng lại token cũ → reuse detection.
        var reuse = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var user = await Db.Users.FirstAsync(u => u.Email == email);
        var sessions = await Db.AuthSessions
            .Where(s => s.UserId == user.Id)
            .ToListAsync();
        Assert.All(sessions, s => Assert.NotNull(s.RevokedAt));

        var audit = await Db.AuditLogs
            .FirstOrDefaultAsync(a => a.Action == "REFRESH_REUSE_DETECTED" && a.ActorUserId == user.Id);
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var row = await Db.AuthSessions
            .FirstAsync(rt => rt.RefreshTokenHash == Hash(tokens.RefreshToken));
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await Db.SaveChangesAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var logout = await Client.PostAsJsonAsync("/api/v1/auth/logout",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var row = await Db.AuthSessions
            .FirstAsync(rt => rt.RefreshTokenHash == Hash(tokens.RefreshToken));
        Assert.NotNull(row.RevokedAt);

        // Refresh sau logout → reuse detection → 401.
        var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_UnknownToken_StillReturns204()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var logout = await Client.PostAsJsonAsync("/api/v1/auth/logout",
            new RefreshRequest { RefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Me_WithRefreshedAccessToken_Works()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var refreshed = await refresh.Content.ReadFromJsonAsync<RefreshResponse>();

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", refreshed!.AccessToken);
        var me = await Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Refresh_ConcurrentParallelRequests_AtMostOneRotates_NoOrphanSessions_AndReuseRevokesFamily()
    {
        var (email, tokens) = await RegisterVerifyLoginAsync();
        var user = await Db.Users.AsNoTracking().FirstAsync(u => u.Email == email);

        const int concurrency = 6;
        using var barrier = new SemaphoreSlim(0, concurrency);
        var clients = Enumerable.Range(0, concurrency).Select(_ => Factory.CreateClient()).ToArray();

        try
        {
            var tasks = clients.Select(async client =>
            {
                await barrier.WaitAsync();
                return await client.PostAsJsonAsync(
                    "/api/v1/auth/refresh",
                    new RefreshRequest { RefreshToken = tokens.RefreshToken });
            }).ToArray();

            barrier.Release(concurrency);
            var responses = await Task.WhenAll(tasks);

            var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            var unauthorizedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);

            Assert.InRange(successCount, 0, 1);
            Assert.Equal(concurrency, successCount + unauthorizedCount);

            var sessions = await Db.AuthSessions
                .AsNoTracking()
                .Where(s => s.UserId == user.Id)
                .ToListAsync();

            // Tuyệt đối không sinh orphan session: tối đa 2 bản ghi (session gốc + tối đa 1 session thắng rotate).
            Assert.InRange(sessions.Count, 1, 2);
            // Vì có request thua cuộc đua gửi trùng token đã rotate, reuse detection phải thu hồi toàn bộ family.
            Assert.All(sessions, s => Assert.NotNull(s.RevokedAt));

            var reuseAuditExists = await Db.AuditLogs
                .AsNoTracking()
                .AnyAsync(a => a.Action == "REFRESH_REUSE_DETECTED" && a.ActorUserId == user.Id);
            Assert.True(reuseAuditExists);
        }
        finally
        {
            foreach (var c in clients)
                c.Dispose();
        }
    }

    [Fact]
    public async Task RotateAsync_WhenSaveChangesFailsBeforeCommit_RollsBackOldTokenRevocation_AndCreatesZeroOrphans()
    {
        var (email, tokens) = await RegisterVerifyLoginAsync();
        var user = await Db.Users.AsNoTracking().FirstAsync(u => u.Email == email);

        var connString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")!;
        var options = new DbContextOptionsBuilder<MedFlow.Infrastructure.Persistence.MedFlowDbContext>()
            .UseNpgsql(connString)
            .Options;

        await using (var failingDb = new FailingOnInsertDbContext(options))
        {
            var config = Factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var sessionService = new MedFlow.Infrastructure.Identity.AuthSessionService(failingDb, config);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sessionService.RotateAsync(tokens.RefreshToken, "127.0.0.1"));
        }

        // Kiểm tra trên DB thật: transaction phải rollback cả ExecuteUpdateAsync trên token cũ.
        var sessionsAfterFailure = await Db.AuthSessions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .ToListAsync();

        var original = Assert.Single(sessionsAfterFailure);
        Assert.Equal(Hash(tokens.RefreshToken), original.RefreshTokenHash);
        Assert.Null(original.RevokedAt);
        Assert.Null(original.ReplacedByTokenHash);

        // Token cũ vẫn rotate thành công bình thường sau khi lỗi tạm thời đã qua.
        var retryResp = await Client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
    }

    private sealed class FailingOnInsertDbContext(
        DbContextOptions<MedFlow.Infrastructure.Persistence.MedFlowDbContext> options)
        : MedFlow.Infrastructure.Persistence.MedFlowDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var hasAddedSession = ChangeTracker
                .Entries<MedFlow.Domain.Entities.AuthSession>()
                .Any(e => e.State == EntityState.Added);

            if (hasAddedSession)
            {
                throw new InvalidOperationException("Simulated DB failure before transaction commit.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
