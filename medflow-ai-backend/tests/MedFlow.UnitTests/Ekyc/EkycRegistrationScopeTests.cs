using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MedFlow.UnitTests.Ekyc;

/// <summary>
/// Registration-scope token mechanics (plan v2.1 mục 3.1): the token carries
/// <c>scope=ekyc-registration</c>, NO role claims, validates against the same signing key,
/// and the middleware whitelist logic admits only eKYC/auth-status paths.
/// </summary>
public sealed class EkycRegistrationScopeTests
{
    private const string TestSecret = "unit-test-jwt-secret-key-at-least-32-bytes-long!!";

    private static MedFlow.Infrastructure.Identity.JwtTokenService CreateJwtService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TestSecret,
                ["Jwt:Issuer"] = "MedFlow-Test",
                ["Jwt:Audience"] = "MedFlow-Test",
                ["Jwt:AccessTokenExpiryMinutes"] = "30",
            })
            .Build();
        return new MedFlow.Infrastructure.Identity.JwtTokenService(config);
    }

    private static User TestUser() => new()
    {
        Id = 42,
        Email = "pending@example.com",
        AuthProvider = "LOCAL",
    };

    [Fact]
    public void RegistrationToken_CarriesScopeClaimAndNoRoles()
    {
        var sut = CreateJwtService();

        var result = sut.GenerateRegistrationScopeToken(TestUser(), sessionId: null);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.Contains(token.Claims, c =>
            c.Type == AuthTokenScopes.ClaimType && c.Value == AuthTokenScopes.EkycRegistration);
        Assert.DoesNotContain(token.Claims, c => c.Type == ClaimTypes.Role);
        Assert.Equal("42", token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
    }

    [Fact]
    public void RegistrationToken_ValidatesWithSameSigningKey()
    {
        var sut = CreateJwtService();
        var result = sut.GenerateRegistrationScopeToken(TestUser(), sessionId: null);

        // Validate exactly as the API bearer pipeline does.
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(result.AccessToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "MedFlow-Test",
            ValidAudience = "MedFlow-Test",
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(TestSecret)),
            ClockSkew = TimeSpan.Zero,
        }, out _);

        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal(AuthTokenScopes.EkycRegistration,
            principal.FindFirst(AuthTokenScopes.ClaimType)?.Value);
    }

    [Fact]
    public void FullToken_DoesNotCarryScopeClaim()
    {
        var sut = CreateJwtService();

        var result = sut.GenerateAccessToken(TestUser(), ["PATIENT"], sessionId: null);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.DoesNotContain(token.Claims, c => c.Type == AuthTokenScopes.ClaimType);
        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Role && c.Value == "PATIENT");
    }

    // ---------------------------------------------------------------
    // Whitelist path logic — mirror of RegistrationScopeMiddleware rules,
    // asserted as pure path-matching decisions.
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("/api/v1/ekyc/sessions", true)]
    [InlineData("/api/v1/ekyc/sessions/123/finalize", true)]
    [InlineData("/api/v1/ekyc/proxy/123/ai/v1/web/ocr/id", true)]
    [InlineData("/api/v1/auth/me", true)]
    [InlineData("/api/v1/auth/logout", true)]
    [InlineData("/api/v1/auth/verify-otp", true)]
    [InlineData("/api/v1/auth/resend-otp", true)]
    [InlineData("/api/v1/patients/me", false)]
    [InlineData("/api/v1/appointments", false)]
    [InlineData("/api/v1/auth/change-password", false)]
    [InlineData("/api/v1/auth/refresh", false)]
    [InlineData("/api/v1/ekycx/sessions", false)] // prefix-trick guard: must not match "/api/v1/ekyc"
    [InlineData("/api/v1/auth/verify-otpx", false)] // prefix-trick guard on the email endpoints
    public void WhitelistPathMatching(string path, bool expectedAllowed)
    {
        Assert.Equal(expectedAllowed, IsWhitelisted(path));
    }

    /// <summary>Mirror of the middleware's prefix logic — keep in sync with RegistrationScopeMiddleware.</summary>
    private static bool IsWhitelisted(string path)
    {
        string[] prefixes =
        [
            "/api/v1/ekyc",
            "/api/v1/auth/me",
            "/api/v1/auth/logout",
            "/api/v1/auth/verify-otp",
            "/api/v1/auth/resend-otp",
        ];
        foreach (var prefix in prefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (path.Length == prefix.Length || path[prefix.Length] == '/'))
            {
                return true;
            }
        }

        return false;
    }
}
