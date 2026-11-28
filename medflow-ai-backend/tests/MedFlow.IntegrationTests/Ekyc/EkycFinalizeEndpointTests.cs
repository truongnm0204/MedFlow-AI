using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MedFlow.Domain.Constants;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Ekyc;

/// <summary>
/// End-to-end finalize tests through the real HTTP pipeline against real PostgreSQL:
/// full happy-path verification, evidence capture→verify chain, rejection paths,
/// and the concurrent-finalize race (real FOR UPDATE row locking — exactly one winner).
/// The VNPT upstream is an in-process stub that signs payloads with a real RSA keypair,
/// so the cryptographic verification chain is genuinely exercised.
/// </summary>
public sealed class EkycFinalizeEndpointTests : IClassFixture<EkycTestWebAppFactory>, IDisposable
{
    private readonly EkycTestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;

    public EkycFinalizeEndpointTests(EkycTestWebAppFactory factory)
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
        var email = $"ekyc-fin-{Guid.NewGuid():N}@example.com";
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

    private async Task ProxyPostAsync(string token, string proxyBase, string vnptPath, string jsonBody)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{proxyBase}{vnptPath}")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Simulates the full SDK flow through the proxy: upload, OCR, card liveness, face liveness, compare.</summary>
    private async Task RunFullFlowAsync(string token, string proxyBase)
    {
        const string docHash = "idg20261127/IDG01_stubbed_upload";

        await ProxyPostAsync(token, proxyBase, "/file-service/v1/addFile", "fake-upload");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/ocr/id/front",
            "{\"img_front\":\"" + docHash + "\",\"client_session\":\"x\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/card/liveness",
            "{\"img\":\"" + docHash + "\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/face/liveness",
            "{\"img\":\"idg20261127/IDG01_faceimg\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/face/compare",
            "{\"img_front\":\"" + docHash + "\",\"img\":\"idg20261127/IDG01_faceimg\"}");
    }

    private async Task<HttpResponseMessage> FinalizeAsync(string token, long sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ekyc/sessions/{sessionId}/finalize");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    // ------------------------------------------------------------------
    // Happy path: full flow → VERIFIED with evidence persisted
    // ------------------------------------------------------------------

    [Fact]
    public async Task Finalize_FullValidFlow_VerifiedWithEvidence()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);
        await RunFullFlowAsync(token, proxyBase);

        var response = await FinalizeAsync(token, sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("VERIFIED", body);

        var session = await Db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycVerificationStatuses.Verified, session.Status);
        Assert.NotNull(session.ConsumedAt);
        Assert.NotNull(session.OcrSnapshot);
        Assert.NotNull(session.IdentificationHmac);
        Assert.NotNull(session.EvidenceDataSign);
        Assert.NotNull(session.EvidencePayloadDigest);
        Assert.NotNull(session.EvidencePayloadFull);
        Assert.NotNull(session.CompareResult);
        Assert.NotNull(session.TamperingVerdict);

        // OCR snapshot must not contain any base64 image material — bounded size sanity.
        Assert.True(session.OcrSnapshot!.Length < 2000);
    }

    [Fact]
    public async Task Finalize_DocumentAndCardOnly_Verified()
    {
        // Owner scope 2026-11-28: face liveness + compare are OPTIONAL. Document OCR + card
        // liveness are the required CCCD proof.
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);

        await ProxyPostAsync(token, proxyBase, "/file-service/v1/addFile", "fake-upload");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/ocr/id/front",
            "{\"img_front\":\"idg20261127/IDG01_stubbed_upload\",\"client_session\":\"x\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/card/liveness",
            "{\"img\":\"idg20261127/IDG01_stubbed_upload\"}");

        var response = await FinalizeAsync(token, sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("VERIFIED", body);
        Assert.Contains(EkycBindingStates.NotApplicable, await Db.EkycVerificationSessions
            .Where(s => s.Id == sessionId).Select(s => s.BindingState).FirstAsync());
    }

    [Fact]
    public async Task Finalize_DeclaredCccdMismatch_Rejected()
    {
        var token = await CreatePatientTokenAsync();
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ekyc/sessions")
        {
            Content = JsonContent.Create(new
            {
                consentVersion = "ekyc-consent-v1",
                consentAccepted = true,
                identityCard = "099999999999", // differs from the stub OCR id
            }),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResponse = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateSessionShape>();

        await ProxyPostAsync(token, created!.ProxyBasePath, "/file-service/v1/addFile", "fake-upload");
        await ProxyPostAsync(token, created.ProxyBasePath, "/ai/v1/web/ocr/id/front",
            "{\"img_front\":\"idg20261127/IDG01_stubbed_upload\"}");
        await ProxyPostAsync(token, created.ProxyBasePath, "/ai/v1/web/card/liveness",
            "{\"img\":\"idg20261127/IDG01_stubbed_upload\"}");

        var response = await FinalizeAsync(token, long.Parse(created.SessionId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("REJECTED", body);
        Assert.Contains("OCR_MISMATCH", body);
    }

    [Fact]
    public async Task Finalize_TamperingDetected_Rejected()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);

        _factory.VnptHandler.PayloadOverrides["ocr/id"] =
            """{"object":{"id":"012345678901","name":"NGUYEN VAN EKYC","birth_day":"15/06/1995","gender":"Nam","tampering":{"is_legal":"no","warning":["edited"]}},"type_document":9,"challengeCode":"11111"}""";
        try
        {
            await ProxyPostAsync(token, proxyBase, "/file-service/v1/addFile", "fake-upload");
            await ProxyPostAsync(token, proxyBase, "/ai/v1/web/ocr/id/front",
                "{\"img_front\":\"idg20261127/IDG01_stubbed_upload\"}");
            await ProxyPostAsync(token, proxyBase, "/ai/v1/web/card/liveness",
                "{\"img\":\"idg20261127/IDG01_stubbed_upload\"}");
        }
        finally
        {
            _factory.VnptHandler.PayloadOverrides.TryRemove("ocr/id", out _);
        }

        var response = await FinalizeAsync(token, sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("REJECTED", body);
        Assert.Contains("TAMPERING_DETECTED", body);
    }

    [Fact]
    public async Task Finalize_CompareBelowThreshold_Rejected()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);

        _factory.VnptHandler.PayloadOverrides["face/compare"] =
            """{"object":{"result":"success","msg":"match","prob":"10.00"},"challengeCode":"11111"}""";
        try
        {
            await RunFullFlowAsync(token, proxyBase);
        }
        finally
        {
            _factory.VnptHandler.PayloadOverrides.TryRemove("face/compare", out _);
        }

        var response = await FinalizeAsync(token, sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("REJECTED", body);
        Assert.Contains("COMPARE_BELOW_THRESHOLD", body);
    }

    // ------------------------------------------------------------------
    // Rejections
    // ------------------------------------------------------------------

    [Fact]
    public async Task Finalize_MissingSteps_Rejected()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);

        // Only document — no liveness, no compare.
        await ProxyPostAsync(token, proxyBase, "/file-service/v1/addFile", "fake-upload");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/ocr/id/front",
            "{\"img_front\":\"idg20261127/IDG01_stubbed_upload\"}");

        var response = await FinalizeAsync(token, sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("REJECTED", body);
        Assert.Contains("MISSING_STEP", body);
    }

    [Fact]
    public async Task Finalize_InvalidBinding_Rejected()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);

        // Compare references a foreign document hash — composite-attack signal.
        await ProxyPostAsync(token, proxyBase, "/file-service/v1/addFile", "fake-upload");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/ocr/id/front",
            "{\"img_front\":\"idg20261127/IDG01_stubbed_upload\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/card/liveness",
            "{\"img\":\"idg20261127/IDG01_stubbed_upload\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/face/liveness",
            "{\"img\":\"idg20261127/IDG01_face\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/face/compare",
            "{\"img_front\":\"idg20261127/IDG01_FOREIGN\",\"img\":\"idg20261127/IDG01_face\"}");

        var response = await FinalizeAsync(token, sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("REJECTED", body);
        Assert.Contains("INVALID_BINDING", body);
    }

    [Fact]
    public async Task Finalize_ReplayAfterVerified_Conflict()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);
        await RunFullFlowAsync(token, proxyBase);

        var first = await FinalizeAsync(token, sessionId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await FinalizeAsync(token, sessionId);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
    }

    [Fact]
    public async Task Finalize_ForeignSession_NotFound()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);
        await RunFullFlowAsync(token, proxyBase);

        var otherToken = await CreatePatientTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ekyc/sessions/{sessionId}/finalize");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Finalize_NoEvidence_Rejected()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, _) = await CreateSessionAsync(token);

        var response = await FinalizeAsync(token, sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("REJECTED", body);
    }

    [Fact]
    public async Task Finalize_ExpiredSession_ConflictAndMarksExpired()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);
        await RunFullFlowAsync(token, proxyBase);

        // Force TTL expiry after evidence was captured: finalize must reject and persist EXPIRED.
        var session = await Db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        session.ExpiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-1), DateTimeKind.Unspecified);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var response = await FinalizeAsync(token, sessionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var persisted = await Db.EkycVerificationSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycVerificationStatuses.Expired, persisted.Status);
        Assert.Null(persisted.ConsumedAt);
    }

    // ------------------------------------------------------------------
    // RACE TEST — the critical one: concurrent finalize on real PostgreSQL.
    // FOR UPDATE row lock must serialize exactly one winner.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Finalize_Concurrent_ExactlyOneWinner()
    {
        var token = await CreatePatientTokenAsync();
        var (sessionId, proxyBase) = await CreateSessionAsync(token);
        await RunFullFlowAsync(token, proxyBase);

        // Fire 4 concurrent finalize requests at the same session.
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ekyc/sessions/{sessionId}/finalize");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _client.SendAsync(request);
            return response.StatusCode;
        })).ToArray();

        var statuses = await Task.WhenAll(tasks);

        var okCount = statuses.Count(s => s == HttpStatusCode.OK);
        var conflictCount = statuses.Count(s => s == HttpStatusCode.Conflict);

        Assert.Equal(1, okCount);
        Assert.Equal(3, conflictCount);

        // consumed_at exactly once — the session row shows a single consumption timestamp.
        var session = await Db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycVerificationStatuses.Verified, session.Status);
        Assert.NotNull(session.ConsumedAt);
    }
}
