using MedFlow.Domain.Entities;

namespace MedFlow.Application.Interfaces.Services;

public class JwtTokenResult
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string TokenType { get; set; } = "Bearer";
}

public interface IJwtTokenService
{
    JwtTokenResult GenerateAccessToken(User user, IEnumerable<string> roles);

    JwtTokenResult GenerateAccessToken(User user, IEnumerable<string> roles, long? sessionId)
        => GenerateAccessToken(user, roles);

    /// <summary>
    /// Issues a registration-scope token (plan v2.1 mục 3.1): carries <c>scope=ekyc-registration</c>
    /// and NO role claims, so role-gated endpoints reject it naturally; the registration-scope
    /// middleware additionally whitelists only eKYC + auth-status endpoints. Default implementation
    /// keeps existing implementors source-compatible.
    /// </summary>
    JwtTokenResult GenerateRegistrationScopeToken(User user, long? sessionId)
        => GenerateAccessToken(user, roles: [], sessionId);
}
