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

    /// <summary>
    /// RSA-512 test keypair (mirrors trial CA key size). The stub signs every AI-endpoint
    /// payload exactly like VNPT: RSA-SHA256/PKCS1v15 over the verbatim dataBase64 string.
    /// The factory exposes the public key so the app's Ekyc:PublicKeyCa matches.
    /// </summary>
    public readonly System.Security.Cryptography.RSA SigningKey = CreateKey();

    private static System.Security.Cryptography.RSA CreateKey()
    {
        var rsa = System.Security.Cryptography.RSA.Create();
        rsa.KeySize = 512;
        return rsa;
    }

    public string PublicKeyBase64 => Convert.ToBase64String(SigningKey.ExportSubjectPublicKeyInfo());

    private string SignPayload(string payloadJson)
    {
        var dataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
        var signature = SigningKey.SignData(
            Encoding.UTF8.GetBytes(dataBase64),
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        var dataSign = Convert.ToBase64String(signature);
        return "{\"dataBase64\":\"" + dataBase64 + "\",\"dataSign\":\"" + dataSign + "\",\"message\":\"ok\"}";
    }

    /// <summary>Configurable payload overrides per endpoint substring (test-controlled failure injection).</summary>
    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> PayloadOverrides = new();

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

        string body;
        if (path.Contains("addFile", StringComparison.OrdinalIgnoreCase))
        {
            body = """{"object":{"hash":"idg20261127/IDG01_stubbed_upload"},"message":"ok"}""";
        }
        else
        {
            // Signed AI responses — payload shape depends on endpoint family.
            var payloadJson = ResolvePayload(path);
            body = SignPayload(payloadJson);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }

    private string ResolvePayload(string path)
    {
        foreach (var (key, overridePayload) in PayloadOverrides)
        {
            if (path.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return overridePayload;
            }
        }

        if (path.Contains("ocr/id", StringComparison.OrdinalIgnoreCase))
        {
            return """{"object":{"id":"012345678901","name":"NGUYEN VAN EKYC","birth_day":"15/06/1995","gender":"Nam","nationality":"Viet Nam","valid_date":"15/06/2040","tampering":{"is_legal":"yes","warning":[]}},"type_document":9,"challengeCode":"11111"}""";
        }

        if (path.Contains("card/liveness", StringComparison.OrdinalIgnoreCase))
        {
            return """{"object":{"liveness":"success","liveness_msg":"OK"},"challengeCode":"11111"}""";
        }

        if (path.Contains("face/liveness", StringComparison.OrdinalIgnoreCase))
        {
            return """{"object":{"liveness":"success","liveness_msg":"OK","is_eye_open":true},"challengeCode":"11111"}""";
        }

        if (path.Contains("face/compare", StringComparison.OrdinalIgnoreCase))
        {
            return """{"object":{"result":"success","msg":"match","prob":"92.45"},"challengeCode":"11111"}""";
        }

        return """{"object":{"liveness":"success"},"challengeCode":"11111"}""";
    }
}

/// <summary>
/// WebApplicationFactory for eKYC integration tests: real medflow_test DB, FakeEmailSender,
/// and the "vnpt-ekyc" HttpClient rewired to an in-process stub upstream — zero real network.
/// Credentials are test-only fakes; the assertions verify they are the values being injected.
/// </summary>
public class EkycTestWebAppFactory : WebApplicationFactory<Program>
{
    private const string DefaultTestConnection =
        "Host=localhost;Port=5433;Database=medflow_test;Username=postgres;Password=medflow_dev";

    public MedFlow.IntegrationTests.Auth.FakeEmailSender EmailSender { get; } = new();
    public StubVnptUpstreamHandler VnptHandler { get; } = new();

    /// <summary>Overridden by the mandatory-registration variant — slice-4 tests.</summary>
    protected virtual bool MandatoryRegistration => false;

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
                ["Ekyc:PublicKeyCa"] = VnptHandler.PublicKeyBase64,
                ["Ekyc:EncryptionKeyBase64"] = Convert.ToBase64String(new byte[32]),
                ["Ekyc:HmacKey"] = "integration-ekyc-hmac-key",
                ["Ekyc:MandatoryRegistration"] = MandatoryRegistration ? "true" : "false",
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

/// <summary>Flag-ON variant: mandatory eKYC registration (slice-4 end-to-end tests).</summary>
public sealed class EkycMandatoryRegistrationTestWebAppFactory : EkycTestWebAppFactory
{
    protected override bool MandatoryRegistration => true;
}
