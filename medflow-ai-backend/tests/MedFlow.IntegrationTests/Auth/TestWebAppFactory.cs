using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>
/// WebApplicationFactory trỏ sang DB <c>medflow_test</c> + FakeEmailSender.
/// Rate limiter không bị tắt — các test login/refresh nằm dưới ngưỡng 5/min.
/// Connection string lấy từ env var <c>ConnectionStrings__DefaultConnection</c>;
/// nếu chưa set thì fallback về local dev postgres (medflow_test, không chứa credential thật).
/// </summary>
public sealed class TestWebAppFactory : WebApplicationFactory<Program>
{
    // Local dev postgres mặc định — chỉ dùng cho DB test, không phải credential thật.
    // Override bằng env var MEDFLOW_TEST_DB khi cần (CI, password khác).
    private const string DefaultTestConnection =
        "Host=localhost;Port=5432;Database=medflow_test;Username=postgres;Password=123";

    public FakeEmailSender EmailSender { get; } = new();

    public TestWebAppFactory()
    {
        // Program.cs đọc config khi host build — env var là cách duy nhất chắc chắn
        // override được (AddInMemoryCollection chạy sau khi Program.cs đã đọc config).
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
            }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton(EmailSender);
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }
}
