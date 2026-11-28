using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// Service-layer authorization for reception-assisted identity work (two-layer authz:
/// [Authorize] on the controller is layer 1; permission + staff status is layer 2).
/// Requires the ACTIVE account to hold PATIENT_IDENTITY_VERIFY and, unless system admin, an
/// ACTIVE staff row in an active hospital.
/// </summary>
internal static class EkycStaffAuthority
{
    public const string ForbiddenMessage = "Bạn không có quyền xác minh danh tính bệnh nhân.";

    public static async Task RequireIdentityVerifyAsync(
        IApplicationDbContext db,
        long actorUserId,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        if (user.AccountStatus != AccountStatus.ACTIVE
            || (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc))
        {
            throw new UnauthorizedAccessException("Tài khoản người dùng chưa kích hoạt hoặc đang bị khóa.");
        }

        var roleIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var roleCodes = await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);
        var isSystemAdmin = roleCodes.Contains(RoleNames.SystemAdmin, StringComparer.OrdinalIgnoreCase);

        var hasPermission = await (
            from rp in db.RolePermissions.AsNoTracking()
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where roleIds.Contains(rp.RoleId) && p.Code == PermissionCodes.PatientIdentityVerify
            select rp).AnyAsync(ct);

        if (!hasPermission)
        {
            throw new ForbiddenException(ForbiddenMessage);
        }

        if (isSystemAdmin)
        {
            return;
        }

        var staff = await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);
        if (staff is null
            || staff.HospitalId is null
            || !string.Equals(staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException(ForbiddenMessage);
        }

        var hospitalActive = await db.Hospitals.AsNoTracking()
            .AnyAsync(h => h.Id == staff.HospitalId.Value && h.IsActive != false, ct);
        if (!hospitalActive)
        {
            throw new ForbiddenException(ForbiddenMessage);
        }
    }
}
