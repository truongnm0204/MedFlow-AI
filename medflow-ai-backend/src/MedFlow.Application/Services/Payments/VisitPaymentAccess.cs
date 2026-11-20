using System.Globalization;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

internal static class VisitPaymentAccess
{
    public const string VisitNotFoundMessage = "Không tìm thấy lượt khám.";
    public const string InvalidVisitMessage = "Mã lượt khám không hợp lệ.";
    public const string InvalidActorMessage = "Thông tin định danh người dùng không hợp lệ.";
    public const string InactiveActorMessage = "Tài khoản người dùng hiện không ở trạng thái hoạt động.";
    public const string LockedActorMessage = "Tài khoản người dùng hiện đang bị tạm khóa.";
    public const string HospitalConflictMessage = "Không xác định được cơ sở của lượt khám.";

    private const int MaxIdentifierLength = 40;

    public static async Task<User> RequireActiveUserAsync(
        IApplicationDbContext db,
        long actorUserId,
        TimeProvider time,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException(InvalidActorMessage);

        var nowUtc = time.GetUtcNow().UtcDateTime;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new UnauthorizedAccessException(InvalidActorMessage);

        if (user.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException(InactiveActorMessage);

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc)
            throw new UnauthorizedAccessException(LockedActorMessage);

        return user;
    }

    public static async Task<HashSet<long>> LoadOwnedPatientIdsAsync(
        IApplicationDbContext db,
        long actorUserId,
        TimeProvider time,
        CancellationToken ct)
    {
        var nowLocal = AppointmentBookingRules.GetVietnamNow(time);
        var owned = await AppointmentBookingRules.LoadOwnedPatientsPhiAsync(db, actorUserId, nowLocal, ct);
        var ids = new HashSet<long> { owned.Self.Id };
        foreach (var link in owned.FamilyLinks)
            ids.Add(link.MemberPatientId);
        return ids;
    }

    public static ParsedVisit ParseVisitIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException(InvalidVisitMessage);

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].Trim();

        if (trimmed.Length == 0 || trimmed.Length > MaxIdentifierLength)
            throw new ValidationException(InvalidVisitMessage);

        var numeric = trimmed[0] == '-'
            ? trimmed.Length > 1 && trimmed[1..].All(static c => c is >= '0' and <= '9')
            : trimmed.All(static c => c is >= '0' and <= '9');
        if (numeric)
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ValidationException(InvalidVisitMessage);
            return new ParsedVisit(id, null);
        }

        return new ParsedVisit(null, trimmed.ToUpperInvariant());
    }

    public static async Task<Visit?> FindVisitAsync(
        IApplicationDbContext db,
        ParsedVisit parsed,
        CancellationToken ct)
    {
        if (parsed.Id is long id)
            return await db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);

        var code = parsed.Code!;
        return await db.Visits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == code, ct);
    }

    internal readonly record struct ParsedVisit(long? Id, string? Code);
}
