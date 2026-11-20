using System.Text.Json;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Patients;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.IdentityClaims;

public sealed class PatientIdentityClaimService : IPatientIdentityClaimService
{
    public const string PermissionCode = "PATIENT_IDENTITY_VERIFY";
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Expired = "EXPIRED";
    public const string NeedsReconciliation = "NEEDS_RECONCILIATION";

    public const string ForbiddenMessage = "Bạn không có quyền xử lý yêu cầu liên kết hồ sơ.";
    public const string ClaimNotFoundMessage = "Không tìm thấy yêu cầu liên kết hồ sơ.";
    public const string OpenClaimMessage = "Bạn đang có một yêu cầu liên kết chưa xử lý.";
    public const string ClaimClosedMessage = "Yêu cầu liên kết đã được xử lý hoặc đã hết hạn.";
    public const string TargetMissingMessage = "Không tìm thấy hồ sơ cần liên kết.";
    public const string TargetLinkedMessage = "Hồ sơ đã được liên kết với một tài khoản khác.";
    public const string SameProfileMessage = "Hồ sơ cần liên kết đang là hồ sơ hiện tại của bạn.";
    public const string ReconciliationMessage =
        "Hồ sơ hiện tại còn dữ liệu nghiệp vụ; cần bộ phận quản lý hồ sơ đối soát trước khi liên kết.";
    public const string OtpRequiredMessage = "Vui lòng xác thực OTP trước khi gửi yêu cầu.";
    public const string NoteRequiredMessage = "Vui lòng nhập lý do từ chối (tối thiểu 5 ký tự).";

    private static readonly TimeSpan ClaimTtl = TimeSpan.FromDays(7);
    private static readonly string[] ReviewStatuses = [Pending, NeedsReconciliation];

    private readonly IApplicationDbContext _db;
    private readonly IPatientIdentityClaimStore _store;
    private readonly AuthOtpService _otp;
    private readonly TimeProvider _time;

    public PatientIdentityClaimService(
        IApplicationDbContext db,
        IPatientIdentityClaimStore store,
        AuthOtpService otp,
        TimeProvider time)
    {
        _db = db;
        _store = store;
        _otp = otp;
        _time = time;
    }

    public async Task<SendIdentityClaimOtpResponse> SendOtpAsync(
        long actorUserId,
        CancellationToken ct = default)
    {
        await EnsurePatientRoleAsync(actorUserId, ct);
        var expiresAt = await _otp.SendIdentityClaimOtpAsync(actorUserId, ct);
        return new SendIdentityClaimOtpResponse(expiresAt);
    }

    public async Task<IdentityClaimResponse> CreateAsync(
        long actorUserId,
        CreateIdentityClaimRequest request,
        CancellationToken ct = default)
    {
        await EnsurePatientRoleAsync(actorUserId, ct);

        if (string.IsNullOrWhiteSpace(request.Otp))
            throw new ValidationException(OtpRequiredMessage);

        var code = request.PatientCode?.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 40)
            throw new ValidationException("Mã hồ sơ bệnh nhân không hợp lệ.");

        var hospitalExists = await _db.Hospitals.AsNoTracking()
            .AnyAsync(h => h.Id == request.HospitalId && h.IsActive != false, ct);
        if (!hospitalExists)
            throw new ValidationException("Cơ sở xác minh không hợp lệ.");

        var current = await PatientSelfResolver.LoadActiveAsync(_db, actorUserId, ct);

        if (await _store.HasOpenForUserAsync(actorUserId, ct))
            throw new ConflictException(OpenClaimMessage);

        await _otp.VerifyIdentityClaimOtpAsync(actorUserId, request.Otp.Trim(), ct);

        var target = await _db.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PatientCode != null && p.PatientCode.ToUpper() == code.ToUpper(), ct);

        if (target is not null && target.Id == current.Id)
            throw new ConflictException(SameProfileMessage);

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var claim = new PatientIdentityClaim
        {
            RequesterUserId = actorUserId,
            CurrentPatientId = current.Id,
            TargetPatientId = target?.Id,
            RequestedPatientCode = code,
            HospitalId = request.HospitalId,
            Status = Pending,
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc.Add(ClaimTtl),
        };

        _store.Add(claim);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(ex))
        {
            if (_db is DbContext context)
                context.ChangeTracker.Clear();
            throw new ConflictException(OpenClaimMessage);
        }

        AddAudit(actorUserId, claim.HospitalId, "PATIENT_IDENTITY_CLAIM_REQUESTED", claim.Id, new
        {
            currentPatientId = current.Id,
            targetPatientId = target?.Id,
        });
        await _db.SaveChangesAsync(ct);

        return Map(claim);
    }

    public async Task<IReadOnlyList<IdentityClaimResponse>> GetMineAsync(
        long actorUserId,
        CancellationToken ct = default)
    {
        await EnsurePatientRoleAsync(actorUserId, ct);
        var claims = await _store.ListByRequesterAsync(actorUserId, ct);
        return claims.Select(Map).ToList();
    }

    public async Task<IdentityClaimResponse> GetMineByIdAsync(
        long actorUserId,
        long claimId,
        CancellationToken ct = default)
    {
        await EnsurePatientRoleAsync(actorUserId, ct);
        var claim = await _store.GetAsync(claimId, ct);
        if (claim is null || claim.RequesterUserId != actorUserId)
            throw new NotFoundException(ClaimNotFoundMessage);
        return Map(claim);
    }

    public async Task<IReadOnlyList<StaffIdentityClaimDto>> GetReviewQueueAsync(
        long actorUserId,
        string? status,
        CancellationToken ct = default)
    {
        var authority = await RequireStaffAuthorityAsync(actorUserId, ct);
        await _store.ExpirePendingAsync(_time.GetUtcNow().UtcDateTime, ct);

        var filter = ReviewStatuses;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var requested = status.Trim().ToUpperInvariant();
            if (!ReviewStatuses.Contains(requested))
                throw new ValidationException("Trạng thái yêu cầu không hợp lệ.");
            filter = [requested];
        }

        if (!authority.IsSystemAdmin && authority.HospitalId is not long hospitalId)
            throw new ForbiddenException(ForbiddenMessage);

        var claims = authority.IsSystemAdmin
            ? await _store.ListByStatusesAsync(filter, ct)
            : await _store.ListForReviewAsync(authority.HospitalId!.Value, filter, ct);

        return claims.Select(MapStaff).ToList();
    }

    public async Task<IdentityClaimResponse> ApproveAsync(
        long actorUserId,
        long claimId,
        CancellationToken ct = default)
    {
        var authority = await RequireStaffAuthorityAsync(actorUserId, ct);
        var claim = await _store.GetAsync(claimId, ct)
            ?? throw new NotFoundException(ClaimNotFoundMessage);

        if (!authority.IsSystemAdmin && authority.HospitalId != claim.HospitalId)
            throw new ForbiddenException(ForbiddenMessage);

        if (claim.Status != Pending)
            throw new ConflictException(ClaimClosedMessage);

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        if (claim.ExpiresAt <= nowUtc)
        {
            claim.Status = Expired;
            claim.DecidedAt = nowUtc;
            await _db.SaveChangesAsync(ct);
            throw new ConflictException(ClaimClosedMessage);
        }

        if (claim.TargetPatientId is not long targetId)
            throw new ConflictException(TargetMissingMessage);

        var current = await _db.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == claim.RequesterUserId, ct)
            ?? throw new ConflictException(ClaimClosedMessage);

        if (current.Id == targetId)
            throw new ConflictException(SameProfileMessage);

        if (await HasClinicalDataAsync(current.Id, ct))
        {
            claim.Status = NeedsReconciliation;
            claim.DecisionNote = ReconciliationMessage;
            AddAudit(actorUserId, claim.HospitalId, "PATIENT_IDENTITY_RECONCILIATION_REQUIRED", claim.Id, new
            {
                currentPatientId = current.Id,
                targetPatientId = targetId,
            });
            await _db.SaveChangesAsync(ct);
            return Map(claim);
        }

        await using var tx = await _db.BeginTransactionAsync(ct);

        var detached = await _store.TryDetachPatientUserAsync(current.Id, claim.RequesterUserId, ct);
        if (detached != 1)
        {
            await tx.RollbackAsync(ct);
            throw new ConflictException(TargetLinkedMessage);
        }

        var linked = await _store.TryLinkPatientUserAsync(targetId, claim.RequesterUserId, ct);
        if (linked != 1)
        {
            await tx.RollbackAsync(ct);
            throw new ConflictException(TargetLinkedMessage);
        }

        await _store.ArchivePatientAsync(current.Id, ct);

        claim.Status = Approved;
        claim.DecidedAt = nowUtc;
        claim.DecidedByStaffId = authority.StaffId;
        claim.DecisionNote = null;

        await _store.RejectOtherOpenClaimsAsync(
            targetId,
            claim.Id,
            "Hồ sơ đã được liên kết với một yêu cầu khác.",
            nowUtc,
            ct);

        AddAudit(actorUserId, claim.HospitalId, "PATIENT_IDENTITY_LINKED", claim.Id, new
        {
            currentPatientId = current.Id,
            targetPatientId = targetId,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Map(claim);
    }

    public async Task<IdentityClaimResponse> RejectAsync(
        long actorUserId,
        long claimId,
        RejectIdentityClaimRequest request,
        CancellationToken ct = default)
    {
        var authority = await RequireStaffAuthorityAsync(actorUserId, ct);
        var claim = await _store.GetAsync(claimId, ct)
            ?? throw new NotFoundException(ClaimNotFoundMessage);

        if (!authority.IsSystemAdmin && authority.HospitalId != claim.HospitalId)
            throw new ForbiddenException(ForbiddenMessage);

        if (claim.Status != Pending)
            throw new ConflictException(ClaimClosedMessage);

        var note = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(note) || note.Length < 5)
            throw new ValidationException(NoteRequiredMessage);

        claim.Status = Rejected;
        claim.DecidedAt = _time.GetUtcNow().UtcDateTime;
        claim.DecidedByStaffId = authority.StaffId;
        claim.DecisionNote = note.Length > 500 ? note[..500] : note;

        AddAudit(actorUserId, claim.HospitalId, "PATIENT_IDENTITY_CLAIM_REJECTED", claim.Id, new
        {
            reason = claim.DecisionNote,
        });

        await _db.SaveChangesAsync(ct);
        return Map(claim);
    }

    private async Task EnsurePatientRoleAsync(long actorUserId, CancellationToken ct)
    {
        await PatientSelfResolver.LoadActiveAsync(_db, actorUserId, ct);
        var codes = await LoadRoleCodesAsync(actorUserId, ct);
        if (!codes.Contains("PATIENT"))
            throw new ForbiddenException(ForbiddenMessage);
    }

    private async Task<StaffAuthority> RequireStaffAuthorityAsync(long actorUserId, CancellationToken ct)
    {
        var codes = await LoadRoleCodesAsync(actorUserId, ct);
        var isSystemAdmin = codes.Contains("SYSTEM_ADMIN");
        var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (!isSystemAdmin)
        {
            var roleIds = await _db.UserRoles.AsNoTracking()
                .Where(ur => ur.UserId == actorUserId)
                .Select(ur => ur.RoleId)
                .ToListAsync(ct);

            var hasPermission = await (
                from rp in _db.RolePermissions.AsNoTracking()
                join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                where roleIds.Contains(rp.RoleId) && p.Code == PermissionCode
                select rp).AnyAsync(ct);

            if (!hasPermission)
                throw new ForbiddenException(ForbiddenMessage);

            if (staff is null ||
                staff.HospitalId is null ||
                !string.Equals(staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException(ForbiddenMessage);
        }

        return new StaffAuthority(isSystemAdmin, staff?.Id, staff?.HospitalId);
    }

    private async Task<HashSet<string>> LoadRoleCodesAsync(long userId, CancellationToken ct)
    {
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var codes = await _db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        return codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> HasClinicalDataAsync(long patientId, CancellationToken ct)
    {
        if (await _db.Visits.AsNoTracking().AnyAsync(v => v.PatientId == patientId, ct))
            return true;
        if (await _db.Appointments.AsNoTracking().AnyAsync(a => a.PatientId == patientId, ct))
            return true;
        if (await _db.PatientFamilyLinks.AsNoTracking()
                .AnyAsync(l => l.OwnerPatientId == patientId || l.MemberPatientId == patientId, ct))
            return true;

        var visitIds = await _db.Visits.AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .Select(v => v.Id)
            .ToListAsync(ct);
        if (visitIds.Count > 0 &&
            await _db.Payments.AsNoTracking().AnyAsync(p => p.VisitId != null && visitIds.Contains(p.VisitId.Value), ct))
            return true;

        return false;
    }

    private void AddAudit(long actorUserId, long hospitalId, string action, long claimId, object data)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospitalId,
            Action = action,
            EntityType = "PATIENT_IDENTITY_CLAIM",
            EntityId = claimId.ToString(),
            ChangeData = JsonSerializer.Serialize(data),
            OccurredAt = _time.GetUtcNow().UtcDateTime,
        });
    }

    private static IdentityClaimResponse Map(PatientIdentityClaim claim) => new(
        claim.Id,
        claim.Status,
        claim.CreatedAt,
        claim.ExpiresAt,
        claim.DecidedAt,
        claim.DecisionNote);

    private static StaffIdentityClaimDto MapStaff(PatientIdentityClaim claim) => new(
        claim.Id,
        claim.RequesterUserId,
        claim.RequestedPatientCode,
        claim.TargetPatientId,
        claim.CurrentPatientId,
        claim.HospitalId,
        claim.Status,
        claim.CreatedAt,
        claim.ExpiresAt);

    private sealed record StaffAuthority(bool IsSystemAdmin, long? StaffId, long? HospitalId);
}
