using System.Net;
using System.Text;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace MedFlow.IntegrationTests.Ekyc;

/// <summary>Records what the upstream VNPT stub actually received.</summary>
public sealed class StubVnptUpstreamHandler : HttpMessageHandler
{
    public readonly List<(string Path, string? Authorization, string? TokenId, string? TokenKey, string? Origin)> Seen = [];
    private readonly object _lock = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var auth = request.Headers.Authorization?.ToString();
        var tokenId = request.Headers.TryGetValues("Token-id", out var tid) ? string.Join(",", tid) : null;
        var tokenKey = request.Headers.TryGetValues("Token-key", out var tk) ? string.Join(",", tk) : null;
        var origin = request.Headers.TryGetValues("Origin", out var og) ? string.Join(",", og) : null;

        lock (_lock)
        {
            Seen.Add((path, auth, tokenId, tokenKey, origin));
        }

        // addFile returns an object.hash; everything else a minimal 200 JSON envelope.
        var body = path.Contains("addFile", StringComparison.OrdinalIgnoreCase)
            ? """{"object":{"hash":"idg20261127/IDG01_stubbed_upload"},"message":"ok"}"""
            : """{"message":"ok"}""";

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// WebApplicationFactory for eKYC integration tests: real medflow_test DB, FakeEmailSender,
/// and the "vnpt-ekyc" HttpClient rewired to an in-process stub upstream — zero real network.
/// Credentials are test-only fakes; the assertions verify they are the values being injected.
/// </summary>
public sealed class EkycTestWebAppFactory : WebApplicationFactory<Program>
{
    private const string DefaultTestConnection =
        "Host=localhost;Port=5433;Database=medflow_test;Username=postgres;Password=medflow_dev";

    public MedFlow.IntegrationTests.Auth.FakeEmailSender EmailSender { get; } = new();
    public StubVnptUpstreamHandler VnptHandler { get; } = new();

    public EkycTestWebAppFactory()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")))
        {
            var conn = Environment.GetEnvironmentVariable("MEDFLOW_TEST_DB") ?? DefaultTestConnection;
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", conn);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
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
                ["ScheduleJobs:Enabled"] = "false",
                ["Ekyc:BackendUrl"] = "https://stubbed-vnpt-upstream.test",
                ["Ekyc:CompareThreshold"] = "70",
                ["Ekyc:SessionTtlMinutes"] = "15",
                ["Ekyc:MaxAttempts"] = "5",
                ["Ekyc:ConsentVersion"] = "ekyc-consent-v1",
                ["Ekyc:TokenId"] = "integration-token-id",
                ["Ekyc:TokenKey"] = "integration-token-key",
                ["Ekyc:AccessToken"] = "integration-access-token",
                ["Ekyc:PublicKeyCa"] = "integration-public-key-ca",
                ["Ekyc:EncryptionKeyBase64"] = Convert.ToBase64String(new byte[32]),
                ["Ekyc:HmacKey"] = "integration-ekyc-hmac-key",
            }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton(EmailSender);
            services.AddSingleton<IEmailSender>(EmailSender);

            // Rewire the vnpt-ekyc named client to the in-process stub upstream.
            services.AddSingleton(VnptHandler);
            services.AddHttpClient("vnpt-ekyc")
                .ConfigureHttpClient(client =>
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("MedFlowAI-EkycProxy/1.0-test");
                })
                .ConfigurePrimaryHttpMessageHandler(() => VnptHandler);
        });
    }
}
