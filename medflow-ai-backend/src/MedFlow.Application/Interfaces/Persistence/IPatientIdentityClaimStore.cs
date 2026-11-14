using MedFlow.Domain.Entities;

namespace MedFlow.Application.Interfaces.Persistence;

/// <summary>Truy cập bảng claim tách khỏi IApplicationDbContext để không mở rộng test double.</summary>
public interface IPatientIdentityClaimStore
{
    void Add(PatientIdentityClaim claim);

    Task<PatientIdentityClaim?> GetAsync(long claimId, CancellationToken ct);

    Task<List<PatientIdentityClaim>> ListByRequesterAsync(long userId, CancellationToken ct);

    Task<List<PatientIdentityClaim>> ListForReviewAsync(
        long hospitalId,
        IReadOnlyList<string> statuses,
        CancellationToken ct);

    Task<List<PatientIdentityClaim>> ListByStatusesAsync(
        IReadOnlyList<string> statuses,
        CancellationToken ct);

    Task<bool> HasOpenForUserAsync(long userId, CancellationToken ct);

    Task<int> TryDetachPatientUserAsync(long patientId, long userId, CancellationToken ct);

    Task<int> TryLinkPatientUserAsync(long patientId, long userId, CancellationToken ct);

    Task<int> ArchivePatientAsync(long patientId, CancellationToken ct);

    Task<int> RejectOtherOpenClaimsAsync(
        long targetPatientId,
        long exceptClaimId,
        string note,
        DateTime decidedAtUtc,
        CancellationToken ct);

    Task<int> ExpirePendingAsync(DateTime nowUtc, CancellationToken ct);
}
