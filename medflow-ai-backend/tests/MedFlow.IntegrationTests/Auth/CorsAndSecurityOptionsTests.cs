using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MedFlow.IntegrationTests.Auth;

public sealed class CorsAndSecurityOptionsTests
{
    private static readonly object EnvLock = new();

    private const string DefaultTestConnection =
        "Host=localhost;Port=5433;Database=medflow_test;Username=postgres;Password=medflow_dev";

    [Fact]
    public async Task Preflight_WithConfiguredAllowedOrigin_ReturnsAllowOriginHeader_AndBlocksDisallowedOrigin()
    {
        using var host = CreateHostScope(
            environmentName: "Test",
            allowedOrigins: ["https://app.medflow.vn/", "https://portal.medflow.vn"]);

        using var allowedResponse = await SendPreflightAsync(host.Client, "https://app.medflow.vn");
        Assert.True(allowedResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigins));
        Assert.Equal("https://app.medflow.vn", Assert.Single(allowOrigins));
        Assert.True(allowedResponse.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCreds));
        Assert.Equal("true", Assert.Single(allowCreds));

        using var evilResponse = await SendPreflightAsync(host.Client, "https://evil.example");
        Assert.False(evilResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_WhenAllowedOriginsEmptyInDevelopment_AllowsLocalhost5173FallbacksOnly()
    {
        using var host = CreateHostScope(
            environmentName: "Development",
            allowedOrigins: []);

        using var httpLocalResponse = await SendPreflightAsync(host.Client, "http://localhost:5173");
        Assert.True(httpLocalResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var httpValues));
        Assert.Equal("http://localhost:5173", Assert.Single(httpValues));

        using var httpsLocalResponse = await SendPreflightAsync(host.Client, "https://localhost:5173");
        Assert.True(httpsLocalResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var httpsValues));
        Assert.Equal("https://localhost:5173", Assert.Single(httpsValues));

        using var evilResponse = await SendPreflightAsync(host.Client, "https://evil.example");
        Assert.False(evilResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("Production")]
    public async Task Preflight_WhenAllowedOriginsEmptyInNonDevelopment_BlocksAllOriginsIncludingLocalhost5173(
        string environmentName)
    {
        using var host = CreateHostScope(
            environmentName: environmentName,
            allowedOrigins: []);

        using var httpLocalResponse = await SendPreflightAsync(host.Client, "http://localhost:5173");
        Assert.False(httpLocalResponse.Headers.Contains("Access-Control-Allow-Origin"));

        using var httpsLocalResponse = await SendPreflightAsync(host.Client, "https://localhost:5173");
        Assert.False(httpsLocalResponse.Headers.Contains("Access-Control-Allow-Origin"));

        using var evilResponse = await SendPreflightAsync(host.Client, "https://evil.example");
        Assert.False(evilResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void SecurityAndQrPaymentOptions_CustomConfiguration_BindsExpectedThresholds()
    {
        using var host = CreateHostScope(
            environmentName: "Test",
            allowedOrigins: ["https://app.medflow.vn"],
            extraConfig: new Dictionary<string, string?>
            {
                ["Auth:OtpTtlMinutes"] = "10",
                ["Auth:OtpResendThrottleSeconds"] = "60",
                ["Auth:OtpMaxAttempts"] = "4",
                ["Auth:MaxLoginAttempts"] = "6",
                ["Auth:LockoutDurationMinutes"] = "30",
                ["Auth:IdentityClaimTtlDays"] = "14",
                ["QrPayment:PendingExpiryMinutes"] = "25",
            });

        var authOptions = host.Factory.Services.GetRequiredService<IOptions<AuthSecurityOptions>>().Value;
        Assert.Equal(10, authOptions.OtpTtlMinutes);
        Assert.Equal(60, authOptions.OtpResendThrottleSeconds);
        Assert.Equal(4, authOptions.OtpMaxAttempts);
        Assert.Equal(6, authOptions.MaxLoginAttempts);
        Assert.Equal(30, authOptions.LockoutDurationMinutes);
        Assert.Equal(14, authOptions.IdentityClaimTtlDays);

        var qrOptions = host.Factory.Services.GetRequiredService<IOptions<QrPaymentOptions>>().Value;
        Assert.Equal(25, qrOptions.PendingExpiryMinutes);
    }

    [Fact]
    public void SecurityAndQrPaymentOptions_NonPositiveThresholds_FailStartupValidation()
    {
        var authEx = Record.Exception(() =>
        {
            using var _ = CreateHostScope(
                environmentName: "Test",
                allowedOrigins: [],
                extraConfig: new Dictionary<string, string?>
                {
                    ["Auth:LockoutDurationMinutes"] = "0",
                });
        });
        Assert.NotNull(authEx);
        Assert.True(
            authEx is OptionsValidationException || authEx.InnerException is OptionsValidationException,
            $"Expected OptionsValidationException, got {authEx.GetType().FullName}: {authEx.Message}");

        var qrEx = Record.Exception(() =>
        {
            using var _ = CreateHostScope(
                environmentName: "Test",
                allowedOrigins: [],
                extraConfig: new Dictionary<string, string?>
                {
                    ["QrPayment:PendingExpiryMinutes"] = "0",
                });
        });
        Assert.NotNull(qrEx);
        Assert.True(
            qrEx is OptionsValidationException || qrEx.InnerException is OptionsValidationException,
            $"Expected OptionsValidationException, got {qrEx.GetType().FullName}: {qrEx.Message}");
    }

    private static Task<HttpResponseMessage> SendPreflightAsync(HttpClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return client.SendAsync(request);
    }

    private static IsolatedHostScope CreateHostScope(
        string environmentName,
        string[] allowedOrigins,
        IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        lock (EnvLock)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")))
            {
                var conn = Environment.GetEnvironmentVariable("MEDFLOW_TEST_DB") ?? DefaultTestConnection;
                Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", conn);
            }

            var prevAspNetEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var prevDotNetEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            var prevOrigin0 = Environment.GetEnvironmentVariable("Cors__AllowedOrigins__0");
            var prevOrigin1 = Environment.GetEnvironmentVariable("Cors__AllowedOrigins__1");
            var prevOrigin2 = Environment.GetEnvironmentVariable("Cors__AllowedOrigins__2");

            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environmentName);
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environmentName);

                // Override array slots 0..2 (including appsettings.Development.json's 2 default origins);
                // whitespace entries are stripped by Program.cs so allowedOrigins=[] yields an empty list.
                Environment.SetEnvironmentVariable(
                    "Cors__AllowedOrigins__0",
                    allowedOrigins.Length > 0 ? allowedOrigins[0] : " ");
                Environment.SetEnvironmentVariable(
                    "Cors__AllowedOrigins__1",
                    allowedOrigins.Length > 1 ? allowedOrigins[1] : " ");
                Environment.SetEnvironmentVariable(
                    "Cors__AllowedOrigins__2",
                    allowedOrigins.Length > 2 ? allowedOrigins[2] : " ");

                var baseFactory = new WebApplicationFactory<Program>();
                var configuredFactory = baseFactory.WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment(environmentName);

                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        var settings = new Dictionary<string, string?>
                        {
                            ["Jwt:Secret"] = "IntegrationTest_JwtSecret_AtLeast32Chars_2026",
                            ["Jwt:Issuer"] = "MedFlow",
                            ["Jwt:Audience"] = "MedFlow",
                            ["Jwt:AccessTokenExpiryMinutes"] = "60",
                            ["Jwt:RefreshTokenExpiryDays"] = "7",
                            ["Auth:OtpSecret"] = "IntegrationTest_OtpHmacSecret_2026",
                            ["Smtp:Host"] = "localhost",
                            ["Identity:HashKey"] = "IntegrationTest_IdentityHash_Key_2026",
                            ["Identity:Mode"] = "dual",
                        };

                        if (extraConfig is not null)
                        {
                            foreach (var (key, value) in extraConfig)
                            {
                                settings[key] = value;
                            }
                        }

                        config.AddInMemoryCollection(settings);
                    });

                    builder.ConfigureTestServices(services =>
                    {
                        var fakeEmail = new FakeEmailSender();
                        services.RemoveAll<IEmailSender>();
                        services.AddSingleton(fakeEmail);
                        services.AddSingleton<IEmailSender>(fakeEmail);
                    });
                });

                try
                {
                    var client = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions
                    {
                        BaseAddress = new Uri("https://localhost"),
                        AllowAutoRedirect = false,
                    });

                    return new IsolatedHostScope(baseFactory, configuredFactory, client);
                }
                catch
                {
                    configuredFactory.Dispose();
                    baseFactory.Dispose();
                    throw;
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prevAspNetEnv);
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", prevDotNetEnv);
                Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", prevOrigin0);
                Environment.SetEnvironmentVariable("Cors__AllowedOrigins__1", prevOrigin1);
                Environment.SetEnvironmentVariable("Cors__AllowedOrigins__2", prevOrigin2);
            }
        }
    }

    private sealed class IsolatedHostScope(
        WebApplicationFactory<Program> baseFactory,
        WebApplicationFactory<Program> configuredFactory,
        HttpClient client) : IDisposable
    {
        public WebApplicationFactory<Program> Factory => configuredFactory;

        public HttpClient Client => client;

        public void Dispose()
        {
            client.Dispose();
            configuredFactory.Dispose();
            baseFactory.Dispose();
        }
    }
}
