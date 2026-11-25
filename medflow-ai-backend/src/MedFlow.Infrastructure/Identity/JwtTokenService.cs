using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MedFlow.Infrastructure.Identity;

public class JwtTokenService : IJwtTokenService
{
    public const string SessionIdClaimType = "sid";

    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IConfiguration configuration, TimeProvider? timeProvider = null)
    {
        _configuration = configuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public JwtTokenResult GenerateAccessToken(User user, IEnumerable<string> roles)
        => GenerateAccessToken(user, roles, sessionId: null);

    public JwtTokenResult GenerateAccessToken(User user, IEnumerable<string> roles, long? sessionId)
    {
        var key = _configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret not configured.");
        var issuer = _configuration["Jwt:Issuer"] ?? "MedFlow";
        var audience = _configuration["Jwt:Audience"] ?? "MedFlow";
        var expiryMinutes = int.TryParse(_configuration["Jwt:AccessTokenExpiryMinutes"], out var m) ? m : 60;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = nowUtc.AddMinutes(expiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (sessionId is > 0)
        {
            claims.Add(new Claim(SessionIdClaimType, sessionId.Value.ToString(CultureInfo.InvariantCulture)));
        }
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtTokenResult
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiresAt,
        };
    }
}
