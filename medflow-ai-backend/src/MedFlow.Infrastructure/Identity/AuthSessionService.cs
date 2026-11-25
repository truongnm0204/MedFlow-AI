using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
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
        var isRelational = _db.Database.IsRelational();
        await using var transaction = isRelational && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

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

        entity.RootSessionId = entity.Id;
        if (isRelational)
        {
            await _db.AuthSessions
                .Where(s => s.Id == entity.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RootSessionId, entity.Id), ct);
            _db.Entry(entity).State = EntityState.Unchanged;
        }
        else
        {
            await _db.SaveChangesAsync(ct);
        }

        if (transaction is not null)
            await transaction.CommitAsync(ct);

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
        var rootSessionId = existing.RootSessionId ?? existing.Id;

        // Reuse: token đã từng rotate (có ReplacedByTokenHash hoặc RevokedReason == ROTATED) nhưng bị gửi lại → thu hồi toàn bộ phiên đang hoạt động.
        if (existing.RevokedAt is not null)
        {
            if (!string.IsNullOrEmpty(existing.ReplacedByTokenHash) ||
                string.Equals(existing.RevokedReason, AuthSessionRevokeReasons.Rotated, StringComparison.OrdinalIgnoreCase))
            {
                await RevokeActiveSessionsCoreAsync(
                    existing.UserId,
                    ipAddress,
                    now,
                    AuthSessionRevokeReasons.TokenReuse,
                    isRelational,
                    ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);

                return new RotateResult { ReuseDetected = true, User = existing.User };
            }

            return new RotateResult { User = existing.User };
        }

        if (existing.ExpiresAt <= now)
            return new RotateResult { User = existing.User };

        if (existing.User.AccountStatus != AccountStatus.ACTIVE ||
            (existing.User.LockedUntil.HasValue && existing.User.LockedUntil.Value > now))
        {
            await RevokeActiveSessionsCoreAsync(
                existing.UserId,
                ipAddress,
                now,
                AuthSessionRevokeReasons.AccountLocked,
                isRelational,
                ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);

            return new RotateResult { User = existing.User };
        }

        // Check if this RootSessionId family already experienced a non-ROTATED revocation.
        var familyExplicitlyRevoked = await _db.AuthSessions.AsNoTracking()
            .AnyAsync(s =>
                s.UserId == existing.UserId &&
                (s.Id == rootSessionId || s.RootSessionId == rootSessionId) &&
                s.RevokedReason != null &&
                s.RevokedReason != AuthSessionRevokeReasons.Rotated,
                ct);
        if (familyExplicitlyRevoked)
        {
            await RevokeFamilyCoreAsync(
                existing.UserId,
                rootSessionId,
                ipAddress,
                now,
                AuthSessionRevokeReasons.AdminRevoked,
                isRelational,
                ct);
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
                    .SetProperty(x => x.RootSessionId, rootSessionId)
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedByIp, ipAddress)
                    .SetProperty(x => x.ReplacedByTokenHash, newTokenHash)
                    .SetProperty(x => x.RevokedReason, AuthSessionRevokeReasons.Rotated), ct);
        }
        else
        {
            existing.RootSessionId = rootSessionId;
            existing.RevokedAt = now;
            existing.RevokedByIp = ipAddress;
            existing.ReplacedByTokenHash = newTokenHash;
            existing.RevokedReason = AuthSessionRevokeReasons.Rotated;
            updated = await _db.SaveChangesAsync(ct);
        }

        if (updated == 0)
        {
            var latest = await _db.AuthSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == existing.Id, ct);

            if (latest?.RevokedAt is not null)
            {
                if (!string.IsNullOrEmpty(latest.ReplacedByTokenHash) ||
                    string.Equals(latest.RevokedReason, AuthSessionRevokeReasons.Rotated, StringComparison.OrdinalIgnoreCase))
                {
                    await RevokeActiveSessionsCoreAsync(
                        existing.UserId,
                        ipAddress,
                        now,
                        AuthSessionRevokeReasons.TokenReuse,
                        isRelational,
                        ct);
                    if (transaction is not null)
                        await transaction.CommitAsync(ct);

                    return new RotateResult { ReuseDetected = true, User = existing.User };
                }

                return new RotateResult { User = existing.User };
            }

            return new RotateResult { User = existing.User };
        }

        // Đồng bộ entity in-memory đã track (ExecuteUpdate bypass change tracker).
        existing.RootSessionId = rootSessionId;
        existing.RevokedAt = now;
        existing.RevokedByIp = ipAddress;
        existing.ReplacedByTokenHash = newTokenHash;
        existing.RevokedReason = AuthSessionRevokeReasons.Rotated;

        var newEntity = new AuthSession
        {
            UserId = existing.UserId,
            RootSessionId = rootSessionId,
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
        if (token is null)
            return;

        var rootId = token.RootSessionId ?? token.Id;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await RevokeFamilyCoreAsync(
            token.UserId,
            rootId,
            ipAddress,
            now,
            AuthSessionRevokeReasons.Logout,
            _db.Database.IsRelational(),
            ct);
    }

    public async Task<RevokeSessionFamilyOutcome> RevokeSessionFamilyAsync(
        long userId,
        string? rawToken,
        long? fallbackSessionId,
        string? ipAddress,
        string reasonCode = AuthSessionRevokeReasons.Logout,
        CancellationToken ct = default)
    {
        AuthSession? target = null;

        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            var hash = ComputeHash(rawToken.Trim());
            var candidate = await _db.AuthSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);

            if (candidate is not null && candidate.UserId == userId)
            {
                target = candidate;
            }
            else if (candidate is not null && candidate.UserId != userId)
            {
                // Foreign refresh token supplied -> never revoke another user's session or fall back blindly.
                return new RevokeSessionFamilyOutcome(false, null, []);
            }
        }

        if (target is null && fallbackSessionId is long sid && sid > 0)
        {
            target = await _db.AuthSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sid && s.UserId == userId, ct);
        }

        if (target is null)
        {
            return new RevokeSessionFamilyOutcome(false, null, []);
        }

        var rootId = target.RootSessionId ?? target.Id;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var isRelational = _db.Database.IsRelational();

        var familySessionIds = await _db.AuthSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && (s.Id == rootId || s.RootSessionId == rootId))
            .OrderBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);

        await RevokeFamilyCoreAsync(userId, rootId, ipAddress, now, reasonCode, isRelational, ct);

        return new RevokeSessionFamilyOutcome(true, rootId, familySessionIds);
    }

    public Task RevokeAllForUserAsync(long userId, string? ipAddress, CancellationToken ct = default)
        => RevokeAllForUserAsync(userId, ipAddress, AuthSessionRevokeReasons.AdminRevoked, ct);

    public Task RevokeAllForUserAsync(
        long userId,
        string? ipAddress,
        string reasonCode,
        CancellationToken ct = default)
        => RevokeActiveSessionsCoreAsync(
            userId,
            ipAddress,
            _timeProvider.GetUtcNow().UtcDateTime,
            reasonCode,
            _db.Database.IsRelational(),
            ct);

    public async Task<RevokeOtherSessionsOutcome> RevokeOtherSessionsForUserAsync(
        long userId,
        string? candidateKeepRawToken,
        long? expectedJwtSessionId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var isRelational = _db.Database.IsRelational();
        long? keptSessionId = null;
        long? keptRootSessionId = null;

        AuthSession? jwtSession = null;
        long? expectedRootSessionId = null;
        if (expectedJwtSessionId is long jwtSid && jwtSid > 0)
        {
            jwtSession = await _db.AuthSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == jwtSid && s.UserId == userId, ct);
            if (jwtSession is not null)
            {
                expectedRootSessionId = jwtSession.RootSessionId ?? jwtSession.Id;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidateKeepRawToken))
        {
            var keepHash = ComputeHash(candidateKeepRawToken.Trim());
            var candidate = await _db.AuthSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.RefreshTokenHash == keepHash, ct);

            if (candidate is not null && candidate.UserId == userId)
            {
                var candidateRootId = candidate.RootSessionId ?? candidate.Id;
                var matchesJwtLineage = !expectedJwtSessionId.HasValue ||
                                        (expectedRootSessionId.HasValue && expectedRootSessionId.Value == candidateRootId);

                if (matchesJwtLineage)
                {
                    var activeLeaf = await FindHealthyActiveLeafForRootAsync(userId, candidateRootId, now, ct);
                    if (activeLeaf is not null)
                    {
                        keptRootSessionId = candidateRootId;
                        keptSessionId = activeLeaf.Id;
                    }
                }
            }
        }
        else if (expectedRootSessionId is long jwtRootId)
        {
            var activeLeaf = await FindHealthyActiveLeafForRootAsync(userId, jwtRootId, now, ct);
            if (activeLeaf is not null)
            {
                keptRootSessionId = jwtRootId;
                keptSessionId = activeLeaf.Id;
            }
        }

        IReadOnlyList<long> keptFamilySessionIds = [];
        if (keptRootSessionId is long keepRoot)
        {
            keptFamilySessionIds = await _db.AuthSessions
                .AsNoTracking()
                .Where(s => s.UserId == userId && (s.Id == keepRoot || s.RootSessionId == keepRoot))
                .OrderBy(s => s.Id)
                .Select(s => s.Id)
                .ToListAsync(ct);
        }

        if (isRelational)
        {
            var query = _db.AuthSessions.Where(s => s.UserId == userId && s.RevokedAt == null);
            if (keptRootSessionId is long rootKeep)
            {
                query = query.Where(s => s.Id != rootKeep && s.RootSessionId != rootKeep);
            }

            var revokedCount = await query.ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedByIp, ipAddress)
                    .SetProperty(x => x.RevokedReason, AuthSessionRevokeReasons.PasswordChanged),
                ct);

            return new RevokeOtherSessionsOutcome(keptSessionId, revokedCount, keptRootSessionId, keptFamilySessionIds);
        }

        var inMemoryQuery = _db.AuthSessions.Where(s => s.UserId == userId && s.RevokedAt == null);
        if (keptRootSessionId is long inMemoryRootKeep)
        {
            inMemoryQuery = inMemoryQuery.Where(s => (s.RootSessionId ?? s.Id) != inMemoryRootKeep);
        }

        var tokensToRevoke = await inMemoryQuery.ToListAsync(ct);
        foreach (var token in tokensToRevoke)
        {
            token.RevokedAt = now;
            token.RevokedByIp = ipAddress;
            token.RevokedReason = AuthSessionRevokeReasons.PasswordChanged;
        }

        if (tokensToRevoke.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return new RevokeOtherSessionsOutcome(keptSessionId, tokensToRevoke.Count, keptRootSessionId, keptFamilySessionIds);
    }

    private async Task<AuthSession?> FindHealthyActiveLeafForRootAsync(
        long userId,
        long rootSessionId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var family = await _db.AuthSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && (s.Id == rootSessionId || s.RootSessionId == rootSessionId))
            .OrderByDescending(s => s.Id)
            .ToListAsync(ct);

        if (family.Count == 0)
            return null;

        var hasExplicitRevocation = family.Any(s =>
            (s.RevokedReason != null && !string.Equals(s.RevokedReason, AuthSessionRevokeReasons.Rotated, StringComparison.OrdinalIgnoreCase)) ||
            (s.RevokedAt != null && s.RevokedReason == null && string.IsNullOrEmpty(s.ReplacedByTokenHash)));

        if (hasExplicitRevocation)
            return null;

        return family.FirstOrDefault(s => s.RevokedAt is null && s.ExpiresAt > nowUtc);
    }

    private async Task RevokeFamilyCoreAsync(
        long userId,
        long rootSessionId,
        string? ipAddress,
        DateTime now,
        string reasonCode,
        bool isRelational,
        CancellationToken ct)
    {
        if (isRelational)
        {
            await _db.AuthSessions
                .Where(s => s.UserId == userId && (s.Id == rootSessionId || s.RootSessionId == rootSessionId) && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedByIp, ipAddress)
                    .SetProperty(x => x.RevokedReason, reasonCode), ct);

            await _db.AuthSessions
                .Where(s =>
                    s.UserId == userId &&
                    (s.Id == rootSessionId || s.RootSessionId == rootSessionId) &&
                    s.RevokedAt != null &&
                    (s.RevokedReason == null || s.RevokedReason == AuthSessionRevokeReasons.Rotated) &&
                    s.ReplacedByTokenHash == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedReason, reasonCode), ct);
            return;
        }

        var familyTokens = await _db.AuthSessions
            .Where(s => s.UserId == userId && (s.Id == rootSessionId || s.RootSessionId == rootSessionId))
            .ToListAsync(ct);

        var dirty = false;
        foreach (var token in familyTokens)
        {
            if (token.RevokedAt is null)
            {
                token.RevokedAt = now;
                token.RevokedByIp = ipAddress;
                token.RevokedReason = reasonCode;
                dirty = true;
            }
            else if (string.IsNullOrEmpty(token.ReplacedByTokenHash) &&
                     (token.RevokedReason is null || string.Equals(token.RevokedReason, AuthSessionRevokeReasons.Rotated, StringComparison.OrdinalIgnoreCase)))
            {
                token.RevokedReason = reasonCode;
                dirty = true;
            }
        }

        if (dirty)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task RevokeActiveSessionsCoreAsync(
        long userId,
        string? ipAddress,
        DateTime now,
        string reasonCode,
        bool isRelational,
        CancellationToken ct)
    {
        if (isRelational)
        {
            await _db.AuthSessions
                .Where(s => s.UserId == userId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedByIp, ipAddress)
                    .SetProperty(x => x.RevokedReason, reasonCode), ct);
            return;
        }

        var tokens = await _db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedByIp = ipAddress;
            token.RevokedReason = reasonCode;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string ComputeHash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
