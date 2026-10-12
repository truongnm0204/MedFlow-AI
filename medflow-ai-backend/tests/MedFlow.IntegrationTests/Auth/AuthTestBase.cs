using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>
/// Base class cho integration tests: HttpClient + DbContext scoped
/// trỏ sang <c>medflow_test</c>, helper register/verify/login.
/// </summary>
public abstract class AuthTestBase : IClassFixture<TestWebAppFactory>, IDisposable
{
    protected readonly TestWebAppFactory Factory;
    protected readonly HttpClient Client;

    private readonly IServiceScope _scope;

    protected AuthTestBase(TestWebAppFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        _scope = factory.Services.CreateScope();
    }

    protected MedFlowDbContext Db => _scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

    protected static string UniqueEmail()
        => $"test-{Guid.NewGuid():N}@example.com";

    protected static RegisterRequest ValidRegister(string? email = null) => new()
    {
        Email = email ?? UniqueEmail(),
        Password = "MedFlow@2026",
        ConfirmPassword = "MedFlow@2026",
        FullName = "Nguyễn Văn Test",
        DateOfBirth = new DateOnly(1995, 6, 15),
        Gender = "MALE",
        Phone = "0912345678",
    };

    /// <summary>Register → trả (response, email). OTP lấy từ FakeEmailSender.</summary>
    protected async Task<(HttpResponseMessage Response, string Email)> RegisterAsync(string? email = null)
    {
        var request = ValidRegister(email);
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", request);
        return (response, request.Email);
    }

    /// <summary>Register + verify OTP → account ACTIVE.</summary>
    protected async Task<string> RegisterAndVerifyAsync()
    {
        var email = UniqueEmail();
        var register = await Client.PostAsJsonAsync("/api/v1/auth/register", ValidRegister(email));
        register.EnsureSuccessStatusCode();

        var otp = Factory.EmailSender.LatestOtpFor(email)
            ?? throw new InvalidOperationException($"No OTP sent to {email}");

        var verify = await Client.PostAsJsonAsync("/api/v1/auth/verify-otp",
            new VerifyOtpRequest { Email = email, Otp = otp });
        verify.EnsureSuccessStatusCode();
        return email;
    }

    /// <summary>Login → LoginResponse (access + refresh).</summary>
    protected async Task<LoginResponse> LoginAsync(string email, string password = "MedFlow@2026")
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Identifier = email, Password = password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    /// <summary>Register + verify + login → trả tokens.</summary>
    protected async Task<(string Email, LoginResponse Tokens)> RegisterVerifyLoginAsync()
    {
        var email = await RegisterAndVerifyAsync();
        var tokens = await LoginAsync(email);
        return (email, tokens);
    }

    public void Dispose()
    {
        Client.Dispose();
        _scope.Dispose();
        Factory.EmailSender.Clear();
        GC.SuppressFinalize(this);
    }
}
