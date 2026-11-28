using MedFlow.Domain.Constants;

namespace MedFlow.API.Middleware;

/// <summary>
/// Registration-scope enforcement (plan v2.1 mục 3.1). A JWT carrying
/// <c>scope=ekyc-registration</c> is a registration-in-progress token: the account is
/// PENDING-activation and the bearer may ONLY complete email/eKYC verification and read own
/// status. This middleware rejects any scoped request outside the whitelist with 403 —
/// defense in depth on top of role-gates (bare <c>[Authorize]</c> endpoints exist, so
/// role-stripping alone is not sufficient).
///
/// Whitelist (path-prefix, case-insensitive):
/// <list type="bullet">
///   <item><c>/api/v1/ekyc</c> — session create/status/finalize/proxy.</item>
///   <item><c>/api/v1/auth/me</c> — xem trạng thái.</item>
///   <item><c>/api/v1/auth/logout</c> — abandon registration cleanly.</item>
///   <item><c>/api/v1/auth/verify-otp</c>, <c>/api/v1/auth/resend-otp</c> — verify email.
///         Anonymous endpoints, but a client that attaches the registration token must still be
///         allowed to complete email verification (plan v2.1 mục 3.1).</item>
/// </list>
/// Unscoped tokens pass through untouched — zero behavior change for existing accounts.
/// </summary>
public sealed class RegistrationScopeMiddleware
{
    private static readonly string[] WhitelistPrefixes =
    [
        "/api/v1/ekyc",
        "/api/v1/auth/me",
        "/api/v1/auth/logout",
        "/api/v1/auth/verify-otp",
        "/api/v1/auth/resend-otp",
    ];

    private readonly RequestDelegate _next;

    public RegistrationScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var scope = context.User.FindFirst(AuthTokenScopes.ClaimType)?.Value;
        if (!string.Equals(scope, AuthTokenScopes.EkycRegistration, StringComparison.Ordinal))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        foreach (var prefix in WhitelistPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (path.Length == prefix.Length || path[prefix.Length] == '/'))
            {
                await _next(context);
                return;
            }
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            title = "Forbidden",
            status = 403,
            detail = "Tài khoản đang chờ xác minh danh tính. Vui lòng hoàn tất xác minh eKYC để sử dụng đầy đủ chức năng.",
        }, context.RequestAborted);
    }
}
