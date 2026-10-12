using System.Net;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>Login edge cases: sai mật khẩu, khóa tài khoản, PENDING.</summary>
public sealed class AuthLoginTests(TestWebAppFactory factory) : AuthTestBase(factory)
{
    [Fact]
    public async Task Login_PendingAccount_Returns401()
    {
        var (response, email) = await RegisterAsync();
        response.EnsureSuccessStatusCode();

        var login = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Identifier = email, Password = "MedFlow@2026" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsFailedAttempts()
    {
        var email = await RegisterAndVerifyAsync();

        var login = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Identifier = email, Password = "Wrong@123" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var user = await Db.Users.FirstAsync(u => u.Email == email);
        Assert.Equal(1, user.FailedLoginAttempts);
    }

    [Fact]
    public async Task Login_FiveFailures_SetsLockedUntil()
    {
        var email = await RegisterAndVerifyAsync();

        for (var i = 0; i < 5; i++)
            await Client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest { Identifier = email, Password = "Wrong@123" });

        var user = await Db.Users.FirstAsync(u => u.Email == email);
        Assert.NotNull(user.LockedUntil);
        Assert.True(user.LockedUntil > DateTime.UtcNow);
        Assert.Equal(0, user.FailedLoginAttempts); // reset sau khi khóa
    }

    [Fact]
    public async Task Login_WhileLocked_Returns400()
    {
        var email = await RegisterAndVerifyAsync();

        var user = await Db.Users.FirstAsync(u => u.Email == email);
        user.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        await Db.SaveChangesAsync();

        // Password đúng nhưng vẫn 400 vì locked.
        var login = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Identifier = email, Password = "MedFlow@2026" });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task Login_WithUsername_Works()
    {
        var email = await RegisterAndVerifyAsync();
        // Username = email lowercase (AuthService.RegisterAsync gán như vậy).
        var login = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Identifier = email.ToLowerInvariant(), Password = "MedFlow@2026" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
