using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Infrastructure.Persistence;

internal sealed class PatientIdentityClaimStore : IPatientIdentityClaimStore
{
    private readonly MedFlowDbContext _db;

    public PatientIdentityClaimStore(MedFlowDbContext db)
    {
        _db = db;
    }

    public void Add(PatientIdentityClaim claim) => _db.PatientIdentityClaims.Add(claim);

    public Task<PatientIdentityClaim?> GetAsync(long claimId, CancellationToken ct) =>
        _db.PatientIdentityClaims.FirstOrDefaultAsync(c => c.Id == claimId, ct);

    public Task<List<PatientIdentityClaim>> ListByRequesterAsync(long userId, CancellationToken ct) =>
        _db.PatientIdentityClaims.AsNoTracking()
            .Where(c => c.RequesterUserId == userId)
            .OrderByDescending(c => c.Id)
            .ToListAsync(ct);

    public Task<List<PatientIdentityClaim>> ListForReviewAsync(
        long hospitalId,
        IReadOnlyList<string> statuses,
        CancellationToken ct) =>
        _db.PatientIdentityClaims.AsNoTracking()
            .Where(c => c.HospitalId == hospitalId && statuses.Contains(c.Status))
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

    public Task<List<PatientIdentityClaim>> ListByStatusesAsync(
        IReadOnlyList<string> statuses,
        CancellationToken ct) =>
        _db.PatientIdentityClaims.AsNoTracking()
            .Where(c => statuses.Contains(c.Status))
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

    public Task<bool> HasOpenForUserAsync(long userId, CancellationToken ct) =>
        _db.PatientIdentityClaims.AsNoTracking()
            .AnyAsync(c => c.RequesterUserId == userId &&
                           (c.Status == "PENDING" || c.Status == "NEEDS_RECONCILIATION"), ct);

    public Task<int> TryDetachPatientUserAsync(long patientId, long userId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE patients SET user_id = NULL WHERE id = {patientId} AND user_id = {userId}", ct);

    public Task<int> TryLinkPatientUserAsync(long patientId, long userId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE patients SET user_id = {userId} WHERE id = {patientId} AND user_id IS NULL", ct);

    public Task<int> ArchivePatientAsync(long patientId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE patients SET status = 'ARCHIVED' WHERE id = {patientId} AND user_id IS NULL", ct);

    public Task<int> RejectOtherOpenClaimsAsync(
        long targetPatientId,
        long exceptClaimId,
        string note,
        DateTime decidedAtUtc,
        CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE patient_identity_claims
             SET status = 'REJECTED', decision_note = {note}, decided_at = {decidedAtUtc}
             WHERE target_patient_id = {targetPatientId}
               AND id <> {exceptClaimId}
               AND status IN ('PENDING', 'NEEDS_RECONCILIATION')
             """, ct);

    public Task<int> ExpirePendingAsync(DateTime nowUtc, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE patient_identity_claims SET status = 'EXPIRED' WHERE status = 'PENDING' AND expires_at < {nowUtc}", ct);
}
