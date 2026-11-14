using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;

namespace MedFlow.Application.Services.Appointments;

internal static class PatientAccessScope
{
    public static Task<Patient> LoadActiveSelfAsync(
        IApplicationDbContext db,
        long userId,
        CancellationToken ct)
        => PatientSelfResolver.LoadActiveAsync(db, userId, ct);

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
