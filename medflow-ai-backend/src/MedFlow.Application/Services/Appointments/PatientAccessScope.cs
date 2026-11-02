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
        return links.Where(l =>
            l.OwnerPatientId == ownerPatientId &&
            l.VerificationStatus != null &&
            l.VerificationStatus.ToUpper() == "VERIFIED" &&
            l.RevokedAt == null &&
            (l.ValidUntil == null || l.ValidUntil > nowLocal) &&
            l.MemberPatient != null &&
            l.MemberPatient.Status != null &&
            l.MemberPatient.Status.ToUpper() == "ACTIVE");
    }
}
