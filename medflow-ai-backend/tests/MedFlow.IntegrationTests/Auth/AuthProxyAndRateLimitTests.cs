using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>
/// Kiểm thử tích hợp cho Phase 01:
/// - Không redirect 307 sang HTTPS trong Development / Test (giữ nguyên Authorization header).
/// - Bắt buộc HTTPS redirect trong Production trừ khi đi qua Reverse Proxy được cấu hình tin cậy.
/// - Chống giả mạo X-Forwarded-For từ client không tin cậy.
/// - Tách riêng bucket Rate Limit cho login / register / refresh, phân vùng theo IP và trả Retry-After + ProblemDetails.
/// </summary>
public sealed class AuthProxyAndRateLimitTests
{
    private const string DefaultTestConnection =
        "Host=localhost;Port=5433;Database=medflow_test;Username=postgres;Password=medflow_dev";

    private static CustomEnvWebAppFactory CreateFactory(
        string environmentName,
        IDictionary<string, string?>? extraConfig = null,
        IPAddress? remoteIp = null)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")))
        {
            var conn = Environment.GetEnvironmentVariable("MEDFLOW_TEST_DB") ?? DefaultTestConnection;
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", conn);
        }

        return new CustomEnvWebAppFactory(environmentName, extraConfig, remoteIp);
    }

    [Fact]
    public async Task Development_HttpProtectedEndpoint_Returns200_Without307HttpsRedirect()
    {
        await using var factory = CreateFactory(
            "Development",
            new Dictionary<string, string?>
            {
                ["https_port"] = "7063",
                ["RateLimiting:LoginPermitLimit"] = "50",
                ["RateLimiting:RegisterPermitLimit"] = "50",
                ["RateLimiting:OtpVerifyPermitLimit"] = "50",
            });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5168"),
            AllowAutoRedirect = false,
        });

        var email = $"dev-proxy-{Guid.NewGuid():N}@example.com";
        var regResp = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest
        {
            Email = email,
            Password = "MedFlow@2026",
            ConfirmPassword = "MedFlow@2026",
            FullName = "Nguyễn Văn Dev",
            DateOfBirth = new DateOnly(1994, 4, 10),
            Gender = "MALE",
            Phone = "0911222333",
        });
        regResp.EnsureSuccessStatusCode();

        var otp = factory.EmailSender.LatestOtpFor(email)!;
        var verifyResp = await client.PostAsJsonAsync("/api/v1/auth/verify-otp",
            new VerifyOtpRequest { Email = email, Otp = otp });
        verifyResp.EnsureSuccessStatusCode();

        var loginResp = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Email = email, Password = "MedFlow@2026" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var tokens = (await loginResp.Content.ReadFromJsonAsync<LoginResponse>())!;

        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var meResp = await client.SendAsync(meReq);

        Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);
    }

    [Fact]
    public async Task Production_DirectHttpRedirects307_WhileTrustedProxyForwardedHttpsSucceeds_AndUntrustedSpoofIgnored()
    {
        // 1) Kết nối HTTP trực tiếp từ IP không thuộc KnownProxies dù gửi kèm X-Forwarded-Proto: https vẫn bị ép 307 sang HTTPS.
        await using (var untrustedFactory = CreateFactory(
            "Production",
            new Dictionary<string, string?>
            {
                ["https_port"] = "7063",
                ["ReverseProxy:KnownProxies:0"] = "10.10.0.1",
            },
            remoteIp: IPAddress.Parse("203.0.113.55")))
        {
            using var untrustedClient = untrustedFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost:5168"),
                AllowAutoRedirect = false,
            });

            using var spoofedReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
            spoofedReq.Headers.Add("X-Forwarded-Proto", "https");
            spoofedReq.Headers.Add("X-Forwarded-For", "1.2.3.4");

            var resp = await untrustedClient.SendAsync(spoofedReq);
            Assert.Equal(HttpStatusCode.TemporaryRedirect, resp.StatusCode);
            Assert.Equal("https", resp.Headers.Location?.Scheme);
        }

        // 2) Kết nối đi qua đúng Reverse Proxy đã cấu hình (10.10.0.1) với X-Forwarded-Proto: https không bị 307 và ghi đúng IP thật vào audit_logs.
        await using (var trustedFactory = CreateFactory(
            "Production",
            new Dictionary<string, string?>
            {
                ["https_port"] = "7063",
                ["ReverseProxy:KnownProxies:0"] = "10.10.0.1",
            },
            remoteIp: IPAddress.Parse("10.10.0.1")))
        {
            using var trustedClient = trustedFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost:5168"),
                AllowAutoRedirect = false,
            });

            var email = $"prod-proxy-{Guid.NewGuid():N}@example.com";
            using var regReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
            {
                Content = JsonContent.Create(new RegisterRequest
                {
                    Email = email,
                    Password = "MedFlow@2026",
                    ConfirmPassword = "MedFlow@2026",
                    FullName = "Trần Thị Prod",
                    DateOfBirth = new DateOnly(1992, 8, 20),
                    Gender = "FEMALE",
                    Phone = "0988777666",
                }),
            };
            regReq.Headers.Add("X-Forwarded-Proto", "https");
            regReq.Headers.Add("X-Forwarded-For", "198.51.100.42");

            var regResp = await trustedClient.SendAsync(regReq);
            Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);

            using var scope = trustedFactory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            var createdUser = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
            var audit = await db.AuditLogs.AsNoTracking()
                .SingleAsync(a => a.Action == "REGISTER" && a.ActorUserId == createdUser.Id);
            Assert.Equal(IPAddress.Parse("198.51.100.42"), audit.IpAddress);
        }
    }

    [Fact]
    public async Task RateLimiting_IsolatesLoginFromRefresh_PartitionsByIp_AndReturnsProblemDetailsWithRetryAfter()
    {
        await using var factory = CreateFactory(
            "Test",
            new Dictionary<string, string?>
            {
                ["RateLimiting:EnabledInTest"] = "true",
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:LoginPermitLimit"] = "2",
                ["RateLimiting:RefreshPermitLimit"] = "4",
                ["RateLimiting:RegisterPermitLimit"] = "5",
                ["RateLimiting:OtpVerifyPermitLimit"] = "5",
                ["ReverseProxy:KnownProxies:0"] = "10.0.0.10",
            },
            remoteIp: IPAddress.Parse("10.0.0.10"));

        using var client = factory.CreateClient();

        // Tạo sẵn 1 user hợp lệ từ IP A (198.51.100.1) để lấy refreshToken.
        var email = $"ratelimit-{Guid.NewGuid():N}@example.com";
        using (var regReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest
            {
                Email = email,
                Password = "MedFlow@2026",
                ConfirmPassword = "MedFlow@2026",
                FullName = "Lê Văn Limit",
                DateOfBirth = new DateOnly(1990, 1, 15),
                Gender = "MALE",
                Phone = "0933444555",
            }),
        })
        {
            regReq.Headers.Add("X-Forwarded-For", "198.51.100.1");
            (await client.SendAsync(regReq)).EnsureSuccessStatusCode();
        }

        var otp = factory.EmailSender.LatestOtpFor(email)!;
        using (var verifyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/verify-otp")
        {
            Content = JsonContent.Create(new VerifyOtpRequest { Email = email, Otp = otp }),
        })
        {
            verifyReq.Headers.Add("X-Forwarded-For", "198.51.100.1");
            (await client.SendAsync(verifyReq)).EnsureSuccessStatusCode();
        }

        // Lượt login 1 & 2 từ IP 198.51.100.1 được phép (LoginPermitLimit = 2).
        LoginResponse? loginTokens = null;
        for (var i = 0; i < 2; i++)
        {
            using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest { Email = email, Password = "MedFlow@2026" }),
            };
            loginReq.Headers.Add("X-Forwarded-For", "198.51.100.1");
            var loginResp = await client.SendAsync(loginReq);
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            loginTokens = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        }

        // Lượt login thứ 3 từ cùng IP 198.51.100.1 bị chặn 429 kèm Retry-After & ProblemDetails.
        using (var throttledLoginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = email, Password = "MedFlow@2026" }),
        })
        {
            throttledLoginReq.Headers.Add("X-Forwarded-For", "198.51.100.1");
            var throttledResp = await client.SendAsync(throttledLoginReq);
            Assert.Equal(HttpStatusCode.TooManyRequests, throttledResp.StatusCode);
            Assert.True(throttledResp.Headers.RetryAfter?.Delta.HasValue ?? false);

            var problem = await throttledResp.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(429, problem.Status);
        }

        // Trong khi login của IP 198.51.100.1 đã chạm trần, /auth/refresh của chính IP đó vẫn hoạt động bình thường (bucket riêng).
        using (var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new RefreshRequest { RefreshToken = loginTokens!.RefreshToken }),
        })
        {
            refreshReq.Headers.Add("X-Forwarded-For", "198.51.100.1");
            var refreshResp = await client.SendAsync(refreshReq);
            Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
        }

        // Và người dùng ở IP khác (198.51.100.2) vẫn đăng nhập thành công (không bị dùng chung bộ đếm toàn cục).
        using (var otherIpLoginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = email, Password = "MedFlow@2026" }),
        })
        {
            otherIpLoginReq.Headers.Add("X-Forwarded-For", "198.51.100.2");
            var otherIpResp = await client.SendAsync(otherIpLoginReq);
            Assert.Equal(HttpStatusCode.OK, otherIpResp.StatusCode);
        }
    }

    private sealed class CustomEnvWebAppFactory(
        string environmentName,
        IDictionary<string, string?>? extraConfig,
        IPAddress? remoteIp) : WebApplicationFactory<Program>
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
                };

                if (extraConfig is not null)
                {
                    foreach (var kv in extraConfig)
                    {
                        settings[kv.Key] = kv.Value;
                    }
                }

                config.AddInMemoryCollection(settings);
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton(EmailSender);
                services.AddSingleton<IEmailSender>(EmailSender);

                if (remoteIp is not null)
                {
                    services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new RemoteIpStartupFilter(remoteIp)));
                }
            });
        }

        private sealed class RemoteIpStartupFilter(IPAddress ip) : IStartupFilter
        {
            public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
                Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next)
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
