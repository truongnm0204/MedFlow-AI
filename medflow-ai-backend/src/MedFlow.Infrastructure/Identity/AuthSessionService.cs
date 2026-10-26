using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MedFlow.Infrastructure.Identity;

public class AuthSessionService : IAuthSessionService
{
    private readonly MedFlowDbContext _db;
    private readonly IConfiguration _configuration;

    public AuthSessionService(MedFlowDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<AuthSessionResult> GenerateAsync(long userId, string? createdByIp, CancellationToken ct = default)
    {
        var expiryDays = int.TryParse(_configuration["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 7;
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var entity = new AuthSession
        {
            UserId = userId,
            RefreshTokenHash = ComputeHash(rawToken),
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            CreatedByIp = createdByIp,
            CreatedAt = DateTime.UtcNow,
        };
        _db.AuthSessions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new AuthSessionResult { RawToken = rawToken, Entity = entity };
    }

    public async Task<RotateResult> RotateAsync(string rawToken, string? ipAddress, CancellationToken ct = default)
    {
        var hash = ComputeHash(rawToken);
        var existing = await _db.AuthSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);

        if (existing is null)
            return new RotateResult();

        // Reuse: token đã bị revoke nhưng vẫn được gửi lên → revoke toàn bộ family.
        if (existing.RevokedAt is not null)
        {
            await RevokeAllForUserAsync(existing.UserId, ipAddress, ct);
            return new RotateResult { ReuseDetected = true, User = existing.User };
        }

        if (existing.ExpiresAt <= DateTime.UtcNow)
            return new RotateResult { User = existing.User };

        var newToken = await GenerateAsync(existing.UserId, ipAddress, ct);

        // Atomic UPDATE: chỉ revoke khi token vẫn active — chống race condition
        // khi 2 request refresh cùng token song song (mỗi request chỉ 1 cái thắng).
        var now = DateTime.UtcNow;
        var updated = await _db.AuthSessions
            .Where(s => s.Id == existing.Id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokedByIp, ipAddress)
                .SetProperty(x => x.ReplacedByTokenHash, newToken.Entity.RefreshTokenHash), ct);

        if (updated == 0)
        {
            // Request khác đã rotate trước → coi như reuse.
            await RevokeAllForUserAsync(existing.UserId, ipAddress, ct);
            return new RotateResult { ReuseDetected = true, User = existing.User };
        }

        // Đồng bộ entity in-memory đã track (ExecuteUpdate bypass change tracker).
        existing.RevokedAt = now;
        existing.RevokedByIp = ipAddress;
        existing.ReplacedByTokenHash = newToken.Entity.RefreshTokenHash;

        return new RotateResult
        {
            Success = true,
            RawToken = newToken.RawToken,
            Entity = newToken.Entity,
            User = existing.User,
        };
    }

    public async Task RevokeAsync(string rawToken, string? ipAddress, CancellationToken ct = default)
    {
        var hash = ComputeHash(rawToken);
        var token = await _db.AuthSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null)
            return;

        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = ipAddress;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(long userId, string? ipAddress, CancellationToken ct = default)
    {
        var tokens = await _db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByIp = ipAddress;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string ComputeHash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
