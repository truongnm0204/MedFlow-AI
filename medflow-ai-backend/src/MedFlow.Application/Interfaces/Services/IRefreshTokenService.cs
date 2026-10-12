using MedFlow.Domain.Entities;

namespace MedFlow.Application.Interfaces.Services;

public class RefreshTokenResult
{
    public string RawToken { get; set; } = string.Empty;
    public RefreshToken Entity { get; set; } = null!;
}

public class RotateResult
{
    public bool Success { get; set; }
    public bool ReuseDetected { get; set; }
    public string? RawToken { get; set; }
    public RefreshToken? Entity { get; set; }
    public User? User { get; set; }
}

public interface IRefreshTokenService
{
    Task<RefreshTokenResult> GenerateAsync(Guid userId, string? createdByIp, CancellationToken ct = default);
    Task<RotateResult> RotateAsync(string rawToken, string? ipAddress, CancellationToken ct = default);
    Task RevokeAsync(string rawToken, string? ipAddress, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, string? ipAddress, CancellationToken ct = default);
}
