using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using Microsoft.AspNetCore.Identity;
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
            new LoginRequest { Email = email, Password = "MedFlow@2026" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsFailedAttempts()
    {
        var email = await RegisterAndVerifyAsync();

        var login = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = email, Password = "Wrong@123" });
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
                new LoginRequest { Email = email, Password = "Wrong@123" });

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
            new LoginRequest { Email = email, Password = "MedFlow@2026" });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task Login_MixedCaseEmail_Works()
    {
        var email = await RegisterAndVerifyAsync();
        // Email được normalize lowercase khi register — login mixed-case vẫn phải vào.
        var login = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = email.ToUpperInvariant(), Password = "MedFlow@2026" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Login_StaffAccount_ReturnsStaffFullNameAndStaffCode_AndMeMatches()
    {
        var doctorRole = await Db.Roles.FirstOrDefaultAsync(r => r.Code == "DOCTOR");
        if (doctorRole is null)
        {
            doctorRole = new Role { Code = "DOCTOR", Name = "Bác sĩ" };
            Db.Roles.Add(doctorRole);
            await Db.SaveChangesAsync();
        }

        var email = UniqueEmail();
        var hasher = new PasswordHasher<User>();
        var staffCode = $"STF-{Guid.NewGuid():N}"[..11];
        var user = new User
        {
            Email = email,
            PasswordHash = hasher.HashPassword(null!, "MedFlow@2026"),
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
            EmailVerifiedAt = DateTime.UtcNow,
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        Db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = doctorRole.Id });
        Db.Staff.Add(new Staff
        {
            UserId = user.Id,
            FullName = "BS Nguyễn Minh Đức",
            StaffCode = staffCode,
            Status = "ACTIVE",
        });
        await Db.SaveChangesAsync();

        var loginResp = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = email, Password = "MedFlow@2026" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var loginBody = (await loginResp.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal("BS Nguyễn Minh Đức", loginBody.User.FullName);
        Assert.Equal(staffCode, loginBody.User.StaffCode);
        Assert.Null(loginBody.User.PatientCode);
        Assert.Contains("DOCTOR", loginBody.User.Roles);

        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);
        var meResp = await Client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

        var meBody = (await meResp.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal("BS Nguyễn Minh Đức", meBody.FullName);
        Assert.Equal(staffCode, meBody.StaffCode);
        Assert.Null(meBody.PatientCode);
        Assert.Contains("DOCTOR", meBody.Roles);
    }
}
