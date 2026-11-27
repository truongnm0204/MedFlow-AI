using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Diagnostic;

/// <summary>
/// Fail-closed department boundary guard for diagnostic execution operations (UC39..UC43, QE-15, QE-16).
/// Note: cross-department staff-assisted queue enrollment is handled separately in VisitQueueEnrollmentService (QE-14).
/// </summary>
internal static class DiagnosticDepartmentScopeGuard
{
    public const string DepartmentScopeForbiddenCode = "DEPARTMENT_SCOPE_FORBIDDEN";

    public const string DepartmentScopeForbiddenMessage =
        "[DEPARTMENT_SCOPE_FORBIDDEN] Nhân viên cận lâm sàng chỉ được phép thực hiện nghiệp vụ chuyên môn trong phạm vi khoa được phân công.";

    public static void EnsureStaffInDepartmentScopeOrThrow(
        Staff? staff,
        long? requiredServiceDepartmentId,
        long? roomDepartmentId = null)
    {
        if (staff?.DepartmentId is not long staffDeptId || staffDeptId <= 0)
        {
            throw new ForbiddenException(DepartmentScopeForbiddenMessage);
        }

        if (!requiredServiceDepartmentId.HasValue && !roomDepartmentId.HasValue)
        {
            throw new ForbiddenException(DepartmentScopeForbiddenMessage);
        }

        if (requiredServiceDepartmentId.HasValue && staffDeptId != requiredServiceDepartmentId.Value)
        {
            throw new ForbiddenException(DepartmentScopeForbiddenMessage);
        }

        if (roomDepartmentId.HasValue && staffDeptId != roomDepartmentId.Value)
        {
            throw new ForbiddenException(DepartmentScopeForbiddenMessage);
        }
    }

    public static async Task<Staff> ReloadLiveStaffAndEnsureDepartmentScopeAsync(
        IApplicationDbContext db,
        long actorUserId,
        Staff fallbackStaff,
        long? requiredServiceDepartmentId,
        long? roomDepartmentId,
        CancellationToken ct)
    {
        Staff? liveStaff;
        try
        {
            liveStaff = await db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
                ?? fallbackStaff;
        }
        catch (NotSupportedException)
        {
            liveStaff = fallbackStaff;
        }

        if (liveStaff is null ||
            (!string.IsNullOrWhiteSpace(liveStaff.Status) &&
             !string.Equals(liveStaff.Status.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Hồ sơ nhân viên cận lâm sàng hiện không ở trạng thái hoạt động hợp lệ.");
        }

        EnsureStaffInDepartmentScopeOrThrow(liveStaff, requiredServiceDepartmentId, roomDepartmentId);
        return liveStaff;
    }
}
