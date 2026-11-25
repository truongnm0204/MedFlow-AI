using MedFlow.Application.Common.Time;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Patients;

/// <summary>
/// Nạp hồ sơ bệnh nhân gắn với user đăng nhập. Fail-fast nếu dữ liệu có nhiều hồ sơ
/// cùng user (giai đoạn chuyển tiếp trước khi unique index được áp dụng).
/// </summary>
internal static class PatientSelfResolver
{
    public const string AmbiguousMessage =
        "Tài khoản người dùng đang gắn với nhiều hồ sơ bệnh nhân. Vui lòng liên hệ quản trị dữ liệu bệnh nhân.";

    public static async Task<Patient> LoadActiveAsync(
        IApplicationDbContext db,
        long userId,
        CancellationToken ct,
        TimeProvider? timeProvider = null)
    {
        var ids = await db.Patients
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Id)
            .Take(2)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var patientId = RequireSinglePatientId(ids, userId);

        var patient = await db.Patients
            .AsNoTracking()
            .Include(p => p.User)
            .FirstAsync(p => p.Id == patientId, ct);

        EnsureUserAndPatientActive(patient, timeProvider);
        return patient;
    }

    public static async Task<Patient> LoadActiveTrackedAsync(
        IApplicationDbContext db,
        long userId,
        CancellationToken ct,
        TimeProvider? timeProvider = null)
    {
        var ids = await db.Patients
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Id)
            .Take(2)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var patientId = RequireSinglePatientId(ids, userId);

        var patient = await db.Patients
            .Include(p => p.User)
            .FirstAsync(p => p.Id == patientId, ct);

        EnsureUserAndPatientActive(patient, timeProvider);
        return patient;
    }

    private static void EnsureUserAndPatientActive(Patient patient, TimeProvider? timeProvider)
    {
        var nowUtc = HospitalClock.GetUtcNow(timeProvider ?? TimeProvider.System);
        if (patient.User is null ||
            patient.User.AccountStatus != AccountStatus.ACTIVE ||
            (patient.User.LockedUntil.HasValue && patient.User.LockedUntil.Value > nowUtc))
        {
            throw new UnauthorizedAccessException("Tài khoản bệnh nhân chưa kích hoạt hoặc đã bị khóa.");
        }

        if (!string.Equals(patient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Hồ sơ bệnh nhân đang ngừng hoạt động.");
    }

    internal static long RequireSinglePatientId(IReadOnlyList<long> ids, long userId)
    {
        if (ids.Count == 0)
            throw new NotFoundException("Patient", userId);

        if (ids.Count > 1)
            throw new ConflictException(AmbiguousMessage);

        return ids[0];
    }
}
