using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using MedFlow.API.Hubs;
using MedFlow.API.Middleware;
using MedFlow.API.Realtime;
using MedFlow.Application;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MedFlow AI API",
        Version = "v1",
        Description = "MedFlow AI Hospital Outpatient Management API",
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Nhập JWT Access Token (không cần gõ chữ 'Bearer ' ở đầu).",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});
builder.Services.AddApplication();
builder.Services.Configure<MedFlow.Application.Options.IdentityHashOptions>(
    builder.Configuration.GetSection("Identity"));
builder.Services.AddOptions<MedFlow.Application.Options.AuthSecurityOptions>()
    .Bind(builder.Configuration.GetSection(MedFlow.Application.Options.AuthSecurityOptions.SectionName))
    .Validate(
        o => o.OtpTtlMinutes > 0 &&
             o.OtpResendThrottleSeconds >= 0 &&
             o.OtpMaxAttempts > 0 &&
             o.MaxLoginAttempts > 0 &&
             o.LockoutDurationMinutes > 0 &&
             o.IdentityClaimTtlDays > 0,
        "Auth security options must specify positive thresholds and non-negative throttle seconds.")
    .ValidateOnStart();
builder.Services.AddOptions<MedFlow.Application.Options.QrPaymentOptions>()
    .Bind(builder.Configuration.GetSection(MedFlow.Application.Options.QrPaymentOptions.SectionName))
    .Validate(
        o => o.PendingExpiryMinutes > 0,
        "QrPayment:PendingExpiryMinutes must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<MedFlow.Application.Options.RealtimeOptions>()
    .Bind(builder.Configuration.GetSection(MedFlow.Application.Options.RealtimeOptions.SectionName))
    .Validate(
        o => o.MaxGroupsPerConnection > 0 &&
             o.MaxInvocationsPerMinute > 0 &&
             o.MaxReceiveMessageSizeBytes >= 1024 &&
             o.MembershipTtlMinutes > 0 &&
             o.PublishTimeoutSeconds > 0,
        "Realtime options must specify positive bounds and MaxReceiveMessageSizeBytes >= 1024.")
    .ValidateOnStart();
builder.Services.AddInfrastructure(builder.Configuration);

var realtimeReceiveSize = Math.Max(
    1024L,
    builder.Configuration.GetValue<long>("Realtime:MaxReceiveMessageSizeBytes", 8 * 1024));
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = realtimeReceiveSize;
});
builder.Services.AddSingleton<ClinicalRealtimeConnectionRegistry>();
builder.Services.AddScoped<IRealtimeNotificationPublisher, SignalRRealtimeNotificationPublisher>();
builder.Services.AddScoped<IRealtimeAccessRevoker, SignalRRealtimeAccessRevoker>();

builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = Math.Max(1, configuration.GetValue("ReverseProxy:ForwardLimit", 1));
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        var configuredProxies = configuration
            .GetSection("ReverseProxy:KnownProxies")
            .Get<string[]>() ?? [];

        foreach (var rawProxy in configuredProxies)
        {
            if (!string.IsNullOrWhiteSpace(rawProxy) && IPAddress.TryParse(rawProxy.Trim(), out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }

        var configuredNetworks = configuration
            .GetSection("ReverseProxy:KnownIPNetworks")
            .Get<string[]>() ?? [];

        foreach (var rawNetwork in configuredNetworks)
        {
            if (!string.IsNullOrWhiteSpace(rawNetwork) && System.Net.IPNetwork.TryParse(rawNetwork.Trim(), out var network))
            {
                options.KnownIPNetworks.Add(network);
            }
        }
    });

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var jwtSecret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret not configured.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? "MedFlow",
            ValidAudience = configuration["Jwt:Audience"] ?? "MedFlow",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero,
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Scoped strictly to /hubs/clinical WebSocket/SSE handshake when Authorization header is absent.
                if (string.IsNullOrEmpty(context.Token) &&
                    string.IsNullOrWhiteSpace(context.Request.Headers.Authorization) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs/clinical") &&
                    context.Request.Query.TryGetValue("access_token", out var accessToken))
                {
                    var rawToken = accessToken.ToString().Trim();
                    if (!string.IsNullOrEmpty(rawToken))
                    {
                        context.Token = rawToken;
                    }
                }

                return Task.CompletedTask;
            },
        };
    })
    .ValidateOnStart();

builder.Services.AddAuthorization();

static string ResolveClientIpPartitionKey(HttpContext httpContext, string policyName)
{
    var ip = httpContext.Connection.RemoteIpAddress?.ToString();
    var normalizedIp = string.IsNullOrWhiteSpace(ip) ? "unknown" : ip;
    return $"{policyName}:{normalizedIp}";
}

static string ResolveUserOrClientIpPartitionKey(HttpContext httpContext, string policyName)
{
    if (httpContext.User.Identity?.IsAuthenticated == true)
    {
        var rawUserId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(rawUserId) &&
            long.TryParse(rawUserId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) &&
            userId > 0)
        {
            return $"{policyName}:user:{userId}";
        }
    }

    return ResolveClientIpPartitionKey(httpContext, policyName);
}

static FixedWindowRateLimiterOptions BuildFixedWindowOptions(
    HttpContext httpContext,
    string limitKey,
    int defaultPermitLimit)
{
    var section = httpContext.RequestServices.GetRequiredService<IConfiguration>().GetSection("RateLimiting");
    var windowSeconds = Math.Max(1, section.GetValue("WindowSeconds", 60));
    var permitLimit = Math.Max(1, section.GetValue(limitKey, defaultPermitLimit));

    return new FixedWindowRateLimiterOptions
    {
        Window = TimeSpan.FromSeconds(windowSeconds),
        PermitLimit = permitLimit,
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        var defaultWindowSeconds = Math.Max(
            1,
            context.HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()
                .GetValue("RateLimiting:WindowSeconds", 60));

        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : defaultWindowSeconds;

        response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Quá giới hạn truy cập",
            Detail = $"Bạn đã thực hiện quá nhiều yêu cầu. Vui lòng thử lại sau {retryAfterSeconds} giây.",
        };

        await response.WriteAsync(JsonSerializer.Serialize(problem), cancellationToken);
    };

    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveClientIpPartitionKey(httpContext, "auth-login"),
            _ => BuildFixedWindowOptions(httpContext, "LoginPermitLimit", 10)));

    options.AddPolicy("auth-register", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveClientIpPartitionKey(httpContext, "auth-register"),
            _ => BuildFixedWindowOptions(httpContext, "RegisterPermitLimit", 5)));

    options.AddPolicy("auth-refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveClientIpPartitionKey(httpContext, "auth-refresh"),
            _ => BuildFixedWindowOptions(httpContext, "RefreshPermitLimit", 30)));

    options.AddPolicy("otp-verify", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveClientIpPartitionKey(httpContext, "otp-verify"),
            _ => BuildFixedWindowOptions(httpContext, "OtpVerifyPermitLimit", 10)));

    options.AddPolicy("otp-resend", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveClientIpPartitionKey(httpContext, "otp-resend"),
            _ => BuildFixedWindowOptions(httpContext, "OtpResendPermitLimit", 5)));

    options.AddPolicy("appointment-write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveUserOrClientIpPartitionKey(httpContext, "appointment-write"),
            _ => BuildFixedWindowOptions(httpContext, "AppointmentWritePermitLimit", 10)));

    options.AddPolicy("payment-qr-generate", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveUserOrClientIpPartitionKey(httpContext, "payment-qr-generate"),
            _ => BuildFixedWindowOptions(httpContext, "PaymentQrGeneratePermitLimit", 10)));

    options.AddPolicy("ai-recommend", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveUserOrClientIpPartitionKey(httpContext, "ai-recommend"),
            _ => BuildFixedWindowOptions(httpContext, "AiRecommendPermitLimit", 15)));
});

var configuredCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? [];

if (configuredCorsOrigins.Length == 0 && builder.Environment.IsDevelopment())
{
    configuredCorsOrigins = ["http://localhost:5173", "https://localhost:5173"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (configuredCorsOrigins.Length > 0)
        {
            policy.WithOrigins(configuredCorsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("Retry-After")
                .AllowCredentials();
        }
    });
});

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MedFlow AI API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "MedFlow AI — Swagger UI";
    });
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

// Bỏ rate limit trong môi trường Test mặc định để integration tests không bị 429,
// trừ khi test chủ động bật RateLimiting:EnabledInTest = true.
if (!app.Environment.IsEnvironment("Test") ||
    app.Configuration.GetValue<bool>("RateLimiting:EnabledInTest"))
{
    app.UseRateLimiter();
}

app.MapControllers();

// Realtime:CloseOnAuthenticationExpiration — operational knob, default true (production security
// feature: server closes the WebSocket when the client JWT expires). Must stay enabled in
// production. Integration tests that inject a virtual TimeProvider set it to false because
// their JWTs are minted against the virtual clock while this ASP.NET Core feature checks the
// `exp` claim against the REAL system clock — a pre-expired test token would abort the
// connection nondeterministically (E2E01 root cause, see plans/reports/pr859-e2e01-round2.md).
var closeOnAuthenticationExpiration =
    app.Configuration.GetValue<bool>("Realtime:CloseOnAuthenticationExpiration", true);
app.MapHub<ClinicalRealtimeHub>(
        "/hubs/clinical",
        options =>
        {
            options.CloseOnAuthenticationExpiration = closeOnAuthenticationExpiration;
        })
    .RequireAuthorization();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program { }
