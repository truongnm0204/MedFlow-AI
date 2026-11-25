using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;

namespace MedFlow.Application.Services.Appointments;

internal static class PatientAccessScope
{
    public static Task<Patient> LoadActiveSelfAsync(
        IApplicationDbContext db,
        long userId,
        CancellationToken ct,
        TimeProvider? timeProvider = null)
        => PatientSelfResolver.LoadActiveAsync(db, userId, ct, timeProvider);

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
             (l.VerificationMethod.ToUpper() == "GUARDIAN_CREATED" &&
              l.MemberPatient!.UserId == null) ||
             (l.VerificationMethod.ToUpper() == "LEGACY" &&
              l.LegacyAccessUntil != null &&
              l.LegacyAccessUntil > nowLocal)));
    }

    /// <summary>PHI chỉ được cấp cho link đã xác minh độc lập (STAFF/OTP). LEGACY và GUARDIAN_CREATED bị loại.</summary>
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

    public static bool CanBookForMember(
        PatientFamilyLink link,
        Patient member,
        DateTime nowLocal)
    {
        if (!IsBaseActiveLink(link, member, nowLocal))
            return false;

        var status = link.VerificationStatus?.Trim().ToUpperInvariant();
        var method = link.VerificationMethod?.Trim().ToUpperInvariant();
        if (status != "VERIFIED" || string.IsNullOrEmpty(method))
            return false;

        return method is "STAFF" or "OTP" ||
               (method == "GUARDIAN_CREATED" && member.UserId is null) ||
               (method == "LEGACY" && link.LegacyAccessUntil.HasValue && link.LegacyAccessUntil.Value > nowLocal);
    }

    public static bool CanViewPhiForMember(
        PatientFamilyLink link,
        Patient member,
        DateTime nowLocal)
    {
        if (!IsBaseActiveLink(link, member, nowLocal))
            return false;

        var status = link.VerificationStatus?.Trim().ToUpperInvariant();
        var method = link.VerificationMethod?.Trim().ToUpperInvariant();
        return status == "VERIFIED" && method is "STAFF" or "OTP";
    }

    private static bool IsBaseActiveLink(
        PatientFamilyLink link,
        Patient member,
        DateTime nowLocal)
        => link.RevokedAt is null &&
           (!link.ValidUntil.HasValue || link.ValidUntil.Value > nowLocal) &&
           string.Equals(member.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);

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
