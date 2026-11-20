using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;

namespace MedFlow.UnitTests.Patients;

public sealed class PatientAccessScopeTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 10, 0, 0);

    [Fact]
    public void WherePhiMemberOf_AllowsOnlyStaffOrOtp()
    {
        var links = BuildLinks();

        var result = links.WherePhiMemberOf(1, Now).Select(l => l.MemberPatientId).ToList();

        Assert.Equal([10L, 11L], result);
    }

    [Fact]
    public void WhereActiveMemberOf_AllowsLegacyWithinGraceOnly()
    {
        var links = BuildLinks();

        var result = links.WhereActiveMemberOf(1, Now).Select(l => l.MemberPatientId).ToList();

        Assert.Equal([10L, 11L, 12L], result);
    }

    private static IQueryable<PatientFamilyLink> BuildLinks()
    {
        return new List<PatientFamilyLink>
        {
            Link(10, "VERIFIED", "STAFF", null),
            Link(11, "VERIFIED", "OTP", null),
            Link(12, "VERIFIED", "LEGACY", Now.AddDays(1)),
            Link(13, "VERIFIED", "LEGACY", Now.AddDays(-1)),
            Link(14, "VERIFIED", null, null),
            Link(15, "PENDING", "STAFF", null),
        }.AsQueryable();
    }

    private static PatientFamilyLink Link(
        long memberId,
        string status,
        string? method,
        DateTime? legacyUntil) => new()
        {
            OwnerPatientId = 1,
            MemberPatientId = memberId,
            VerificationStatus = status,
            VerificationMethod = method,
            LegacyAccessUntil = legacyUntil,
            MemberPatient = new Patient { Id = memberId, Status = "ACTIVE" },
        };
}
