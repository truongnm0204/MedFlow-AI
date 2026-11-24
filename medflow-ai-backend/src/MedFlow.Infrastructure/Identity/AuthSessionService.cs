using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MedFlow.Infrastructure.Identity;

public class AuthSessionService : IAuthSessionService
{
    private readonly MedFlowDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public AuthSessionService(
        MedFlowDbContext db,
        IConfiguration configuration,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _configuration = configuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AuthSessionResult> GenerateAsync(long userId, string? createdByIp, CancellationToken ct = default)
    {
        var expiryDays = int.TryParse(_configuration["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 7;
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var entity = new AuthSession
        {
            UserId = userId,
            RefreshTokenHash = ComputeHash(rawToken),
            ExpiresAt = now.AddDays(expiryDays),
            CreatedByIp = createdByIp,
            CreatedAt = now,
        };
        _db.AuthSessions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new AuthSessionResult { RawToken = rawToken, Entity = entity };
    }

    public async Task<RotateResult> RotateAsync(string rawToken, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return new RotateResult();

        var hash = ComputeHash(rawToken);
        var isRelational = _db.Database.IsRelational();
        await using var transaction = isRelational && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        var existing = await _db.AuthSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);

        if (existing is null)
            return new RotateResult();

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Reuse: token đã bị revoke nhưng vẫn được gửi lên → thu hồi toàn bộ family (không có grace period).
        if (existing.RevokedAt is not null)
        {
            await RevokeActiveSessionsCoreAsync(existing.UserId, ipAddress, now, isRelational, ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);

            return new RotateResult { ReuseDetected = true, User = existing.User };
        }

        if (existing.ExpiresAt <= now)
            return new RotateResult { User = existing.User };

        if (existing.User.AccountStatus != AccountStatus.ACTIVE ||
            (existing.User.LockedUntil.HasValue && existing.User.LockedUntil.Value > now))
        {
            await RevokeActiveSessionsCoreAsync(existing.UserId, ipAddress, now, isRelational, ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);

            return new RotateResult { User = existing.User };
        }

        // Sinh token mới trên RAM trước; chỉ INSERT vào DB sau khi thắng atomic UPDATE trên session cũ.
        var expiryDays = int.TryParse(_configuration["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 7;
        var newRawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var newTokenHash = ComputeHash(newRawToken);

        int updated;
        if (isRelational)
        {
            updated = await _db.AuthSessions
                .Where(s => s.Id == existing.Id && s.RevokedAt == null && s.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedByIp, ipAddress)
                    .SetProperty(x => x.ReplacedByTokenHash, newTokenHash), ct);
        }
        else
        {
            existing.RevokedAt = now;
            existing.RevokedByIp = ipAddress;
            existing.ReplacedByTokenHash = newTokenHash;
            updated = await _db.SaveChangesAsync(ct);
        }

        if (updated == 0)
        {
            // Request khác đã rotate/revoke trước trong cuộc đua song song → kiểm tra trạng thái thực tế.
            var latest = await _db.AuthSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == existing.Id, ct);

            if (latest?.RevokedAt is not null)
            {
                await RevokeActiveSessionsCoreAsync(existing.UserId, ipAddress, now, isRelational, ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);

                return new RotateResult { ReuseDetected = true, User = existing.User };
            }

            return new RotateResult { User = existing.User };
        }

        // Đồng bộ entity in-memory đã track (ExecuteUpdate bypass change tracker).
        existing.RevokedAt = now;
        existing.RevokedByIp = ipAddress;
        existing.ReplacedByTokenHash = newTokenHash;

        var newEntity = new AuthSession
        {
            UserId = existing.UserId,
            RefreshTokenHash = newTokenHash,
            ExpiresAt = now.AddDays(expiryDays),
            CreatedByIp = ipAddress,
            CreatedAt = now,
        };

        _db.AuthSessions.Add(newEntity);
        await _db.SaveChangesAsync(ct);

        if (transaction is not null)
            await transaction.CommitAsync(ct);

        return new RotateResult
        {
            Success = true,
            RawToken = newRawToken,
            Entity = newEntity,
            User = existing.User,
        };
    }

    public async Task RevokeAsync(string rawToken, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return;

        var hash = ComputeHash(rawToken);
        var token = await _db.AuthSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null)
            return;

        token.RevokedAt = _timeProvider.GetUtcNow().UtcDateTime;
        token.RevokedByIp = ipAddress;
        await _db.SaveChangesAsync(ct);
    }

    public Task RevokeAllForUserAsync(long userId, string? ipAddress, CancellationToken ct = default)
        => RevokeActiveSessionsCoreAsync(userId, ipAddress, _timeProvider.GetUtcNow().UtcDateTime, _db.Database.IsRelational(), ct);

    private async Task RevokeActiveSessionsCoreAsync(
        long userId,
        string? ipAddress,
        DateTime now,
        bool isRelational,
        CancellationToken ct)
    {
        if (isRelational)
        {
            await _db.AuthSessions
                .Where(s => s.UserId == userId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedByIp, ipAddress), ct);
            return;
        }

        var tokens = await _db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedByIp = ipAddress;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string ComputeHash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
