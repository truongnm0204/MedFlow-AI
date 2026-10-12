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
}
