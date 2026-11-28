using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MedFlow.Domain.Constants;
using MedFlow.Infrastructure.Persistence;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Ekyc;

/// <summary>
/// End-to-end proxy verification through the real HTTP pipeline with a STUBBED VNPT upstream
/// (in-process HttpMessageHandler — no real network). Proves the full chain:
/// session create (consent) → proxy forward (credential injection, no Origin) → metadata logged,
/// response verbatim, no image persistence.
///
/// Requires the docker medflow_test database with migrations 029+030 applied.
/// </summary>
public sealed class EkycProxyEndpointTests : IClassFixture<EkycTestWebAppFactory>, IDisposable
{
    private readonly EkycTestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;

    public EkycProxyEndpointTests(EkycTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _scope = factory.Services.CreateScope();
    }

    private MedFlowDbContext Db => _scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

    public void Dispose()
    {
        _client.Dispose();
        _scope.Dispose();
    }

    private async Task<string> CreatePatientTokenAsync()
    {
        var email = $"ekyc-test-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "MedFlow@2026",
            confirmPassword = "MedFlow@2026",
            fullName = "Nguyễn Văn Ekyc",
            dateOfBirth = "1995-06-15",
            gender = "MALE",
            phone = "0912345678",
        });
        register.EnsureSuccessStatusCode();

        var otp = _factory.EmailSender.LatestOtpFor(email)
            ?? throw new InvalidOperationException($"No OTP captured for {email}");
        var verify = await _client.PostAsJsonAsync("/api/v1/auth/verify-otp", new { email, otp });
        verify.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "MedFlow@2026" });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<LoginTokenShape>();
        return tokens!.AccessToken;
    }

    private sealed record LoginTokenShape(string AccessToken, int AccessTokenExpiresIn, string RefreshToken);

    private async Task<(long SessionId, string ProxyBase)> CreateSessionAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ekyc/sessions")
        {
            Content = JsonContent.Create(new { consentVersion = "ekyc-consent-v1", consentAccepted = true, identityCard = "012345678901" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateSessionShape>();
        return (long.Parse(body!.SessionId), body.ProxyBasePath);
    }

    private sealed record CreateSessionShape(string SessionId, string ProxyBasePath, string Status, DateTime ExpiresAt);

    // ------------------------------------------------------------------
    // Session creation
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateSession_RequiresAuth()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/ekyc/sessions",
            new { consentVersion = "ekyc-consent-v1", consentAccepted = true, identityCard = "012345678901" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateSession_RejectsWithoutConsent()
    {
        var token = await CreatePatientTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ekyc/sessions")
        {
            Content = JsonContent.Create(new { consentVersion = "ekyc-consent-v1", consentAccepted = false }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSession_RejectsStaleConsentVersion()
    {
        var token = await CreatePatientTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ekyc/sessions")
        {
            Content = JsonContent.Create(new { consentVersion = "ekyc-consent-v0", consentAccepted = true }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSession_SubjectDailyQuotaExceeded_Returns429QuotaCodeAndAudits()
    {
        var token = await CreatePatientTokenAsync();

        // Owner scope: default 3 sessions per subject per day.
        for (var i = 0; i < 3; i++)
        {
            var (_, _) = await CreateSessionAsync(token);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ekyc/sessions")
        {
            Content = JsonContent.Create(new
            {
                consentVersion = "ekyc-consent-v1",
                consentAccepted = true,
                identityCard = "012345678901",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(EkycErrorCodes.QuotaExhausted, body);
        Assert.True(await Db.AuditLogs.AnyAsync(a => a.Action == EkycConstants.AuditActionQuotaExceeded));
    }

    // ------------------------------------------------------------------
    // Proxy forwarding end-to-end
    // ------------------------------------------------------------------

    [Fact]
    public async Task Proxy_Forward_InjectsCredentials_NoOrigin_LogsMetadata()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);

        var payload = """{"img":"idg20261127/IDG01_testref","client_session":"x"}""";
        // Unique endpoint path so shared-stub filtering across parallel tests is unambiguous.
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{proxyBase}/ai/v1/web/card/liveness-credcheck")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Even if the client sends an Origin header, upstream must never see it.
        request.Headers.Add("Origin", "https://evil.example.com");

        var response = await _client.SendAsync(request);

        var seen = Assert.Single(_factory.VnptHandler.Seen, s => s.Path == "/ai/v1/web/card/liveness-credcheck");
        Assert.Equal("Bearer integration-access-token", seen.Authorization);
        Assert.Equal("integration-token-id", seen.TokenId);
        Assert.Equal("integration-token-key", seen.TokenKey);
        Assert.Null(seen.Origin);

        var log = await Db.EkycProxyCallLogs.FirstOrDefaultAsync(l => l.SessionId == sessionId);
        Assert.NotNull(log);
        Assert.Equal("/ai/v1/web/card/liveness-credcheck", log.EndpointPath);
        Assert.Equal(200, log.HttpStatus);
        Assert.Contains("idg20261127/IDG01_testref", log.RequestObjectHashes);
    }

    [Fact]
    public async Task Proxy_RejectsForeignSession()
    {
        var token = await CreatePatientTokenAsync();
        var (_, proxyBase) = await CreateSessionAsync(token);

        // A second user must not even learn the session exists.
        var otherToken = await CreatePatientTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{proxyBase}/ai/v1/web/card/liveness")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Proxy_BindingFlow_AddFileThenCompare_MarksValid()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);

        // 1. addFile upload — stub upstream responds with object.hash
        using (var upload = new HttpRequestMessage(HttpMethod.Post, $"{proxyBase}/file-service/v1/addFile")
        {
            Content = new MultipartFormDataContent { new ByteArrayContent("fake-image-bytes"u8.ToArray()) },
        })
        {
            upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var uploadResponse = await _client.SendAsync(upload);
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        }

        // 2. compare referencing the uploaded hash
        var compareBody = """{"img_front":"idg20261127/IDG01_stubbed_upload","img":"idg20261127/IDG01_face"}""";
        using (var compare = new HttpRequestMessage(HttpMethod.Post, $"{proxyBase}/ai/v1/web/face/compare")
        {
            Content = new StringContent(compareBody, Encoding.UTF8, "application/json"),
        })
        {
            compare.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var compareResponse = await _client.SendAsync(compare);
            Assert.Equal(HttpStatusCode.OK, compareResponse.StatusCode);
        }

        var session = await Db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycBindingStates.Valid, session.BindingState);
    }

    [Fact]
    public async Task Proxy_BindingFlow_CompareForeignHash_MarksInvalid()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);

        var compareBody = """{"img_front":"idg20261127/IDG01_FOREIGN_DOC","img":"idg20261127/IDG01_face"}""";
        using var compare = new HttpRequestMessage(HttpMethod.Post, $"{proxyBase}/ai/v1/web/face/compare")
        {
            Content = new StringContent(compareBody, Encoding.UTF8, "application/json"),
        };
        compare.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(compare);

        var session = await Db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycBindingStates.Invalid, session.BindingState);
    }

    [Fact]
    public async Task Proxy_NeverPersistsImagePayloads()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);

        // A large pseudo-base64 blob embedded in a field — even if present, it must never
        // land in any persisted column (metadata extraction caps at 256 chars).
        var pseudoImage = new string('A', 2000);
        var payload = "{\"img\":\"idg20261127/IDG01_ref\",\"embedded\":\"" + pseudoImage + "\"}";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{proxyBase}/ai/v1/web/card/liveness")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(request);

        var logs = await Db.EkycProxyCallLogs.Where(l => l.SessionId == sessionId).ToListAsync();
        Assert.NotEmpty(logs);
        foreach (var log in logs)
        {
            Assert.False(log.RequestObjectHashes?.Contains(pseudoImage) == true);
            Assert.False(log.ResponseObjectHash?.Contains(pseudoImage) == true);
            Assert.False(log.ParseWarning?.Contains(pseudoImage) == true);
        }
    }

    [Fact]
    public async Task GetSessionStatus_ReturnsForOwner()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, _) = await CreateSessionAsync(token);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ekyc/sessions/{sessionId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(EkycVerificationStatuses.Initiated, body);
    }
}
