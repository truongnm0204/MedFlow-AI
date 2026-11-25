using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>
/// Kiểm thử tích hợp cho Issue #830 (ADR-0057):
/// - Phân vùng Rate Limiting theo UserId (từ JWT đã xác thực) hoặc fallback IP (sau UseForwardedHeaders).
/// - Dùng chung 1 bucket appointment-write trên cả 4 action ghi (Create, Cancel, Reschedule, UpdateReason),
///   kể cả khi đặt cho thành viên gia đình, giữ nguyên các endpoint GET.
/// - Độc lập bucket giữa appointment-write, payment-qr-generate và ai-recommend.
/// - Chạy sau UseAuthentication + UseAuthorization (401/403 trả trước, không nuốt thành 429, không hao permit).
/// - Chống giả mạo X-Forwarded-For từ nguồn không thuộc KnownProxies, expose Retry-After qua CORS và tự phục hồi cửa sổ.
/// </summary>
public sealed class EndpointRateLimitingTests
{
    private const string DefaultTestConnection =
        "Host=localhost;Port=5433;Database=medflow_test;Username=postgres;Password=medflow_dev";

    private static RateLimitWebAppFactory CreateFactory(
        IDictionary<string, string?>? extraConfig = null,
        IPAddress? remoteIp = null,
        bool includeAiProbeController = false,
        string environmentName = "Test")
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")))
        {
            var conn = Environment.GetEnvironmentVariable("MEDFLOW_TEST_DB") ?? DefaultTestConnection;
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", conn);
        }

        return new RateLimitWebAppFactory(extraConfig, remoteIp, includeAiProbeController, environmentName);
    }

    private static string MintToken(WebApplicationFactory<Program> factory, long userId, string role = RoleNames.Patient)
    {
        using var scope = factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwt.GenerateAccessToken(
            new User { Id = userId, Email = $"user{userId}@medflow.test" },
            [role]).AccessToken;
    }

    private static HttpRequestMessage BuildRequest(
        HttpMethod method,
        string url,
        string? bearerToken = null,
        string? forwardedFor = null,
        object? jsonBody = null,
        string? origin = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            req.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        if (!string.IsNullOrWhiteSpace(origin))
        {
            req.Headers.Add("Origin", origin);
        }

        if (jsonBody is not null)
        {
            req.Content = JsonContent.Create(jsonBody);
        }

        return req;
    }

    [Fact]
    public async Task AppointmentWrite_SharesBucketAcrossAllFourMutations_IsolatesUsersOnSameIp_TracksUserAcrossIps_AndAllowsGets()
    {
        await using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["RateLimiting:EnabledInTest"] = "true",
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:AppointmentWritePermitLimit"] = "4",
                ["ReverseProxy:KnownProxies:0"] = "10.0.0.10",
            },
            remoteIp: IPAddress.Parse("10.0.0.10"));

        using var client = factory.CreateClient();
        var userAToken = MintToken(factory, userId: 1, RoleNames.Patient);
        var userBToken = MintToken(factory, userId: 2, RoleNames.Patient);

        // 1/4: Create (kể cả truyền PatientId của thành viên gia đình "3") từ IP 198.51.100.10
        using (var createReq = BuildRequest(
            HttpMethod.Post,
            "/api/v1/appointments",
            userAToken,
            forwardedFor: "198.51.100.10",
            jsonBody: new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "3",
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                DateIso = "2026-12-01",
                SlotOrShiftId = "99999999",
            }))
        {
            var resp = await client.SendAsync(createReq);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }

        // 2/4: Reschedule từ cùng IP 198.51.100.10
        using (var rescheduleReq = BuildRequest(
            HttpMethod.Post,
            "/api/v1/appointments/99999999/reschedule",
            userAToken,
            forwardedFor: "198.51.100.10",
            jsonBody: new RescheduleAppointmentRequest { TargetSlotId = "99999999" }))
        {
            var resp = await client.SendAsync(rescheduleReq);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        // 3/4: Cancel khi User A đổi sang mạng/IP khác (198.51.100.20) -> vẫn tính vào cùng bucket user:1
        using (var cancelReq = BuildRequest(
            HttpMethod.Post,
            "/api/v1/appointments/99999999/cancel",
            userAToken,
            forwardedFor: "198.51.100.20",
            jsonBody: new CancelAppointmentRequest { CancellationReason = "Bận công tác đột xuất" }))
        {
            var resp = await client.SendAsync(cancelReq);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        // 4/4: UpdateReason từ IP 198.51.100.20 -> chạm đúng ngưỡng 4/4
        using (var reasonReq = BuildRequest(
            HttpMethod.Patch,
            "/api/v1/appointments/99999999/reason",
            userAToken,
            forwardedFor: "198.51.100.20",
            jsonBody: new UpdateAppointmentReasonRequest("Đau thượng vị âm ỉ sau ăn")))
        {
            var resp = await client.SendAsync(reasonReq);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        // Lượt thứ 5 của User A (dù tiếp tục đổi sang IP thứ 3: 198.51.100.30) bị chặn 429 ngay lập tức.
        using (var throttledReq = BuildRequest(
            HttpMethod.Patch,
            "/api/v1/appointments/99999999/reason",
            userAToken,
            forwardedFor: "198.51.100.30",
            jsonBody: new UpdateAppointmentReasonRequest("Cập nhật lần 5 vượt hạn ngạch")))
        {
            var throttledResp = await client.SendAsync(throttledReq);
            Assert.Equal(HttpStatusCode.TooManyRequests, throttledResp.StatusCode);
            Assert.Equal("application/problem+json", throttledResp.Content.Headers.ContentType?.MediaType);
            Assert.True(throttledResp.Headers.RetryAfter?.Delta > TimeSpan.Zero);

            var problem = await throttledResp.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(429, problem.Status);
            Assert.Contains("giây", problem.Detail, StringComparison.Ordinal);
        }

        // Endpoint đọc GET của chính User A vẫn hoạt động bình thường (200 OK) khi hết hạn ngạch ghi.
        using (var getMeReq = BuildRequest(
            HttpMethod.Get,
            "/api/v1/appointments/me",
            userAToken,
            forwardedFor: "198.51.100.10"))
        {
            var getMeResp = await client.SendAsync(getMeReq);
            Assert.Equal(HttpStatusCode.OK, getMeResp.StatusCode);
        }

        // User B dùng chung Wi-Fi bệnh viện (cùng IP 198.51.100.10) vẫn có hạn ngạch độc lập, không bị 429.
        using (var userBReq = BuildRequest(
            HttpMethod.Post,
            "/api/v1/appointments/99999999/cancel",
            userBToken,
            forwardedFor: "198.51.100.10",
            jsonBody: new CancelAppointmentRequest { CancellationReason = "Bận công tác đột xuất" }))
        {
            var userBResp = await client.SendAsync(userBReq);
            Assert.Equal(HttpStatusCode.NotFound, userBResp.StatusCode);
        }
    }

    [Fact]
    public async Task PaymentQrGenerate_HasIndependentBucketFromAppointmentWrite_CoversBothRoutes_AndExposesRetryAfterInCors()
    {
        await using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:AppointmentWritePermitLimit"] = "1",
                ["RateLimiting:PaymentQrGeneratePermitLimit"] = "2",
            },
            environmentName: "Development");

        using var client = factory.CreateClient();
        var userToken = MintToken(factory, userId: 1, RoleNames.Patient);

        // Làm cạn bucket appointment-write (limit = 1) của User 1.
        using (var appt1 = BuildRequest(
            HttpMethod.Post,
            "/api/v1/appointments/99999999/cancel",
            userToken,
            jsonBody: new CancelAppointmentRequest { CancellationReason = "Lý do hợp lệ trên 10 ký tự" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(appt1)).StatusCode);
        }

        using (var appt2 = BuildRequest(
            HttpMethod.Post,
            "/api/v1/appointments/99999999/cancel",
            userToken,
            jsonBody: new CancelAppointmentRequest { CancellationReason = "Lý do hợp lệ trên 10 ký tự" }))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(appt2)).StatusCode);
        }

        // Bucket payment-qr-generate (limit = 2) độc lập hoàn toàn với appointment-write và áp chung cho cả 2 route QR.
        using (var qr1 = BuildRequest(HttpMethod.Post, "/api/v1/visits/99999999/payments/qr", userToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(qr1)).StatusCode);
        }

        using (var qr2 = BuildRequest(HttpMethod.Post, "/api/v1/patients/visits/99999999/payments/qr", userToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(qr2)).StatusCode);
        }

        using (var qr3 = BuildRequest(
            HttpMethod.Post,
            "/api/v1/visits/99999999/payments/qr",
            userToken,
            origin: "http://localhost:5173"))
        {
            var throttled = await client.SendAsync(qr3);
            Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
            Assert.True(throttled.Headers.RetryAfter?.Delta > TimeSpan.Zero);
            Assert.True(throttled.Headers.TryGetValues("Access-Control-Expose-Headers", out var exposed));
            Assert.Contains(exposed, v => v.Contains("Retry-After", StringComparison.OrdinalIgnoreCase));

            var problem = await throttled.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(429, problem.Status);
        }
    }

    [Fact]
    public async Task AiRecommend_PartitionsByUserIdWhenAuthenticated_FallsBackToClientIpWhenAnonymous_AndBlocksSpoofedForwardedFor()
    {
        // Phần A: Đi qua Reverse Proxy tin cậy -> khách ẩn danh phân vùng theo IP thật, user đăng nhập phân vùng theo UserId.
        await using (var trustedFactory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["RateLimiting:EnabledInTest"] = "true",
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:AiRecommendPermitLimit"] = "2",
                ["ReverseProxy:KnownProxies:0"] = "10.0.0.10",
            },
            remoteIp: IPAddress.Parse("10.0.0.10"),
            includeAiProbeController: true))
        {
            using var client = trustedFactory.CreateClient();
            var userToken = MintToken(trustedFactory, userId: 101, RoleNames.Patient);

            // Khách ẩn danh IP 198.51.100.50: 2 lần 200 OK, lần 3 bị 429.
            for (var i = 0; i < 2; i++)
            {
                using var guestReq = BuildRequest(
                    HttpMethod.Post,
                    "/api/v1/_test/ai-recommend",
                    forwardedFor: "198.51.100.50");
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(guestReq)).StatusCode);
            }

            using (var guestThrottled = BuildRequest(
                HttpMethod.Post,
                "/api/v1/_test/ai-recommend",
                forwardedFor: "198.51.100.50"))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(guestThrottled)).StatusCode);
            }

            // Khách ẩn danh ở IP khác (198.51.100.51) không bị ảnh hưởng.
            using (var otherGuest = BuildRequest(
                HttpMethod.Post,
                "/api/v1/_test/ai-recommend",
                forwardedFor: "198.51.100.51"))
            {
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(otherGuest)).StatusCode);
            }

            // User 101 đã đăng nhập đứng trên chính IP 198.51.100.50 (đã hết quota khách) vẫn có bucket riêng theo UserId.
            using (var authed1 = BuildRequest(
                HttpMethod.Post,
                "/api/v1/_test/ai-recommend",
                bearerToken: userToken,
                forwardedFor: "198.51.100.50"))
            {
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(authed1)).StatusCode);
            }

            using (var authed2 = BuildRequest(
                HttpMethod.Post,
                "/api/v1/_test/ai-recommend",
                bearerToken: userToken,
                forwardedFor: "198.51.100.51"))
            {
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(authed2)).StatusCode);
            }

            using (var authed3 = BuildRequest(
                HttpMethod.Post,
                "/api/v1/_test/ai-recommend",
                bearerToken: userToken,
                forwardedFor: "198.51.100.52"))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(authed3)).StatusCode);
            }
        }

        // Phần B: Nguồn kết nối trực tiếp không thuộc KnownProxies -> giả mạo X-Forwarded-For bị bỏ qua hoàn toàn.
        await using (var untrustedFactory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["RateLimiting:EnabledInTest"] = "true",
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:AiRecommendPermitLimit"] = "2",
                ["ReverseProxy:KnownProxies:0"] = "10.0.0.10",
            },
            remoteIp: IPAddress.Parse("203.0.113.88"),
            includeAiProbeController: true))
        {
            using var untrustedClient = untrustedFactory.CreateClient();

            using (var spoof1 = BuildRequest(HttpMethod.Post, "/api/v1/_test/ai-recommend", forwardedFor: "1.1.1.1"))
            {
                Assert.Equal(HttpStatusCode.OK, (await untrustedClient.SendAsync(spoof1)).StatusCode);
            }

            using (var spoof2 = BuildRequest(HttpMethod.Post, "/api/v1/_test/ai-recommend", forwardedFor: "2.2.2.2"))
            {
                Assert.Equal(HttpStatusCode.OK, (await untrustedClient.SendAsync(spoof2)).StatusCode);
            }

            using (var spoof3 = BuildRequest(HttpMethod.Post, "/api/v1/_test/ai-recommend", forwardedFor: "3.3.3.3"))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, (await untrustedClient.SendAsync(spoof3)).StatusCode);
            }
        }
    }

    [Fact]
    public async Task MiddlewareOrder_InvalidJwtReturns401_AndWrongRoleReturns403_WithoutBeingMaskedBy429()
    {
        await using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["RateLimiting:EnabledInTest"] = "true",
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:AppointmentWritePermitLimit"] = "1",
                ["RateLimiting:PaymentQrGeneratePermitLimit"] = "1",
            });

        using var client = factory.CreateClient();
        var doctorToken = MintToken(factory, userId: 4, RoleNames.Doctor);

        // Gửi 3 lần với JWT giả mạo vượt PermitLimit=1 -> luôn trả 401 Unauthorized, không bị nuốt thành 429.
        for (var i = 0; i < 3; i++)
        {
            using var invalidJwtReq = BuildRequest(
                HttpMethod.Post,
                "/api/v1/visits/1/payments/qr",
                bearerToken: "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.invalid.signature");
            var resp = await client.SendAsync(invalidJwtReq);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // Gửi 3 lần với đúng JWT nhưng sai role (DOCTOR gọi endpoint PATIENT) -> luôn trả 403 Forbidden.
        for (var i = 0; i < 3; i++)
        {
            using var wrongRoleReq = BuildRequest(
                HttpMethod.Post,
                "/api/v1/appointments/1/cancel",
                bearerToken: doctorToken,
                jsonBody: new CancelAppointmentRequest { CancellationReason = "Bận công tác đột xuất" });
            var resp = await client.SendAsync(wrongRoleReq);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // Sau hàng loạt 401/403 ở trên, một request PATIENT hợp lệ đầu tiên vẫn nhận đủ hạn ngạch (404 thay vì 429).
        var validPatientToken = MintToken(factory, userId: 1, RoleNames.Patient);
        using (var validPatientReq = BuildRequest(
            HttpMethod.Post,
            "/api/v1/visits/99999999/payments/qr",
            bearerToken: validPatientToken))
        {
            var resp = await client.SendAsync(validPatientReq);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }

    [Fact]
    public async Task WindowReplenishment_RestoresQuotaAfterConfiguredWindowElapses()
    {
        await using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["RateLimiting:EnabledInTest"] = "true",
                ["RateLimiting:WindowSeconds"] = "1",
                ["RateLimiting:PaymentQrGeneratePermitLimit"] = "1",
            });

        using var client = factory.CreateClient();
        var patientToken = MintToken(factory, userId: 501, RoleNames.Patient);

        using (var first = BuildRequest(HttpMethod.Post, "/api/v1/visits/99999999/payments/qr", patientToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(first)).StatusCode);
        }

        using (var secondImmediate = BuildRequest(HttpMethod.Post, "/api/v1/visits/99999999/payments/qr", patientToken))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(secondImmediate)).StatusCode);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(1250));

        using (var afterWindow = BuildRequest(HttpMethod.Post, "/api/v1/visits/99999999/payments/qr", patientToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(afterWindow)).StatusCode);
        }
    }

    [Fact]
    public void TestBuildOutput_RetainsAppsettingsTestJson_WhileApiProjectExcludesItFromPublish()
    {
        var outputConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Test.json");
        Assert.True(File.Exists(outputConfigPath), $"Expected appsettings.Test.json in test output: {outputConfigPath}");

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var apiCsprojPath = Path.Combine(repoRoot, "src", "MedFlow.API", "MedFlow.API.csproj");
        var csprojXml = File.ReadAllText(apiCsprojPath);
        Assert.Contains("appsettings.Test.json", csprojXml, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory=\"Never\"", csprojXml, StringComparison.Ordinal);
    }

    [ApiController]
    [NonController]
    public sealed class TestAiRecommendProbeController : ControllerBase
    {
        [HttpPost("/api/v1/_test/ai-recommend")]
        [AllowAnonymous]
        [EnableRateLimiting("ai-recommend")]
        public IActionResult Probe() => Ok(new { ok = true });
    }

    private sealed class ExplicitControllerFeatureProvider(Type controllerType)
        : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var typeInfo = controllerType.GetTypeInfo();
            if (!feature.Controllers.Contains(typeInfo))
            {
                feature.Controllers.Add(typeInfo);
            }
        }
    }

    private sealed class RateLimitWebAppFactory(
        IDictionary<string, string?>? extraConfig,
        IPAddress? remoteIp,
        bool includeAiProbeController,
        string environmentName) : WebApplicationFactory<Program>
    {
        public FakeEmailSender EmailSender { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
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
                    ["ScheduleJobs:Enabled"] = "false",
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
                services.RemoveAll<IEmailSender>();
                services.AddSingleton(EmailSender);
                services.AddSingleton<IEmailSender>(EmailSender);

                if (includeAiProbeController)
                {
                    services.AddControllers()
                        .ConfigureApplicationPartManager(manager =>
                            manager.FeatureProviders.Add(
                                new ExplicitControllerFeatureProvider(typeof(TestAiRecommendProbeController))));
                }

                if (remoteIp is not null)
                {
                    services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new RemoteIpStartupFilter(remoteIp)));
                }
            });
        }

        private sealed class RemoteIpStartupFilter(IPAddress ip) : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
                => app =>
                {
                    app.Use(nextMiddleware => ctx =>
                    {
                        ctx.Connection.RemoteIpAddress = ip;
                        return nextMiddleware(ctx);
                    });
                    next(app);
                };
        }
    }
}
