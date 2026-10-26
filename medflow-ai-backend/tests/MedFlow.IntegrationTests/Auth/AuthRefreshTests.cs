using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.DTOs.Auth;
using Microsoft.EntityFrameworkCore;

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
}
