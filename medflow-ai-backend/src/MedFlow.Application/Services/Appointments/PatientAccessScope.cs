using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

internal static class PatientAccessScope
{
    public static async Task<Patient> LoadActiveSelfAsync(
        IApplicationDbContext db,
        long userId,
        CancellationToken ct)
    {
        var self = await db.Patients
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (self is null)
            throw new NotFoundException("Patient", userId);

        if (self.User is null || self.User.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException("Tài khoản bệnh nhân chưa kích hoạt hoặc đã bị khóa.");

        if (!string.Equals(self.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Hồ sơ bệnh nhân đang ngừng hoạt động.");

        return self;
    }

    public static IQueryable<PatientFamilyLink> WhereActiveMemberOf(
        this IQueryable<PatientFamilyLink> links,
        long ownerPatientId,
        DateTime nowLocal)
    {
        return BaseActive(links, ownerPatientId, nowLocal).Where(l =>
            l.VerificationStatus != null &&
            l.VerificationStatus.ToUpper() == "VERIFIED" &&
            l.VerificationMethod != null &&
            (l.VerificationMethod.ToUpper() == "STAFF" ||
             l.VerificationMethod.ToUpper() == "OTP" ||
             (l.VerificationMethod.ToUpper() == "LEGACY" &&
              l.LegacyAccessUntil != null &&
              l.LegacyAccessUntil > nowLocal)));
    }

    /// <summary>PHI chỉ được cấp cho link đã xác minh độc lập (STAFF/OTP). LEGACY bị loại.</summary>
    public static IQueryable<PatientFamilyLink> WherePhiMemberOf(
        this IQueryable<PatientFamilyLink> links,
        long ownerPatientId,
        DateTime nowLocal)
    {
        return BaseActive(links, ownerPatientId, nowLocal).Where(l =>
            l.VerificationStatus != null &&
            l.VerificationStatus.ToUpper() == "VERIFIED" &&
            l.VerificationMethod != null &&
            (l.VerificationMethod.ToUpper() == "STAFF" ||
             l.VerificationMethod.ToUpper() == "OTP"));
    }

    private static IQueryable<PatientFamilyLink> BaseActive(
        IQueryable<PatientFamilyLink> links,
        long ownerPatientId,
        DateTime nowLocal)
    {
        return links.Where(l =>
            l.OwnerPatientId == ownerPatientId &&
            l.RevokedAt == null &&
            (l.ValidUntil == null || l.ValidUntil > nowLocal) &&
            l.MemberPatient != null &&
            l.MemberPatient.Status != null &&
            l.MemberPatient.Status.ToUpper() == "ACTIVE");
    }
}
