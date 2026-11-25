using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.Patients;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Realtime;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.IdentityClaims;

public sealed class PatientIdentityClaimService : IPatientIdentityClaimService
{
    public const string PermissionCode = PermissionCodes.PatientIdentityVerify;
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Expired = "EXPIRED";
    public const string NeedsReconciliation = "NEEDS_RECONCILIATION";

    public const string ForbiddenMessage = "Bạn không có quyền xử lý yêu cầu liên kết hồ sơ.";
    public const string ClaimNotFoundMessage = "Không tìm thấy yêu cầu liên kết hồ sơ.";
    public const string FamilyLinkNotFoundMessage = "Không tìm thấy liên kết hồ sơ người thân.";
    public const string FamilyLinkNotPendingMessage = "Liên kết hồ sơ người thân không ở trạng thái chờ xác minh.";
    public const string OpenClaimMessage = "Bạn đang có một yêu cầu liên kết chưa xử lý.";
    public const string ClaimClosedMessage = "Yêu cầu liên kết đã được xử lý hoặc đã hết hạn.";
    public const string TargetMissingMessage = "Không tìm thấy hồ sơ cần liên kết.";
    public const string TargetLinkedMessage = "Hồ sơ đã được liên kết với một tài khoản khác.";
    public const string SameProfileMessage = "Hồ sơ cần liên kết đang là hồ sơ hiện tại của bạn.";
    public const string ReconciliationMessage =
        "Hồ sơ hiện tại còn dữ liệu nghiệp vụ; cần bộ phận quản lý hồ sơ đối soát trước khi liên kết.";
    public const string OtpRequiredMessage = "Vui lòng xác thực OTP trước khi gửi yêu cầu.";
    public const string NoteRequiredMessage = "Vui lòng nhập lý do từ chối (tối thiểu 5 ký tự).";

    private static readonly string[] ReviewStatuses = [Pending, NeedsReconciliation];
    private static readonly Regex CompositeLinkIdRegex = new(@"^(\d+)[:\-_](\d+)$", RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly IPatientIdentityClaimStore _store;
    private readonly AuthOtpService _otp;
    private readonly TimeProvider _time;
    private readonly AuthSecurityOptions _authOptions;
    private readonly IRealtimeAccessRevoker _accessRevoker;

    public PatientIdentityClaimService(
        IApplicationDbContext db,
        IPatientIdentityClaimStore store,
        AuthOtpService otp,
        TimeProvider time,
        IOptions<AuthSecurityOptions>? authOptions = null,
        IRealtimeAccessRevoker? accessRevoker = null)
    {
        _db = db;
        _store = store;
        _otp = otp;
        _time = time;
        _authOptions = authOptions?.Value ?? new AuthSecurityOptions();
        _accessRevoker = accessRevoker ?? NullRealtimeAccessRevoker.Instance;
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
        var current = await EnsurePatientRoleAsync(actorUserId, ct);

        if (string.IsNullOrWhiteSpace(request.Otp))
            throw new ValidationException(OtpRequiredMessage);

        var code = request.PatientCode?.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 40)
            throw new ValidationException("Mã hồ sơ bệnh nhân không hợp lệ.");

        var hospitalExists = await _db.Hospitals.AsNoTracking()
            .AnyAsync(h => h.Id == request.HospitalId && h.IsActive != false, ct);
        if (!hospitalExists)
            throw new ValidationException("Cơ sở xác minh không hợp lệ.");

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        await _store.ExpirePendingAsync(nowUtc, ct);

        if (await _store.HasOpenForUserAsync(actorUserId, ct))
            throw new ConflictException(OpenClaimMessage);

        if (!string.IsNullOrWhiteSpace(current.PatientCode) &&
            string.Equals(current.PatientCode, code, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(SameProfileMessage);
        }

        await _otp.VerifyIdentityClaimOtpAsync(actorUserId, request.Otp.Trim(), ct);

        var target = await _db.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PatientCode != null && p.PatientCode.ToUpper() == code.ToUpper(), ct);

        if (target is not null && target.Id == current.Id)
            throw new ConflictException(SameProfileMessage);

        nowUtc = _time.GetUtcNow().UtcDateTime;
        var claim = new PatientIdentityClaim
        {
            RequesterUserId = actorUserId,
            CurrentPatientId = current.Id,
            TargetPatientId = target?.Id,
            RequestedPatientCode = code,
            HospitalId = request.HospitalId,
            Status = Pending,
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc.Add(TimeSpan.FromDays(_authOptions.IdentityClaimTtlDays)),
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
        await _store.ExpirePendingAsync(_time.GetUtcNow().UtcDateTime, ct);
        var claims = await _store.ListByRequesterAsync(actorUserId, ct);
        return claims.Select(Map).ToList();
    }

    public async Task<IdentityClaimResponse> GetMineByIdAsync(
        long actorUserId,
        long claimId,
        CancellationToken ct = default)
    {
        await EnsurePatientRoleAsync(actorUserId, ct);
        await _store.ExpirePendingAsync(_time.GetUtcNow().UtcDateTime, ct);
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
        var preClaim = await _store.GetAsync(claimId, ct)
            ?? throw new NotFoundException(ClaimNotFoundMessage);

        if (!authority.IsSystemAdmin && authority.HospitalId != preClaim.HospitalId)
            throw new ForbiddenException(ForbiddenMessage);

        var preCurrent = await _db.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == preClaim.RequesterUserId, ct);

        var patientIdsToLock = new[] { preCurrent?.Id, preClaim.CurrentPatientId, preClaim.TargetPatientId }
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        List<long> affectedGuardianOwnerUserIds = [];
        long linkedTargetId = 0;
        IdentityClaimResponse response;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            // ADR-0059 lock order: 01 users -> 02 patients (ASC) -> 03 patient_identity_claims
            await _db.TryLockUserForUpdateAsync(preClaim.RequesterUserId, ct);
            if (patientIdsToLock.Length > 0)
            {
                await _db.TryLockPatientsForUpdateAsync(patientIdsToLock, ct);
            }
            await _db.TryLockIdentityClaimForUpdateAsync(claimId, ct);

            if (_db is DbContext efContext)
            {
                efContext.ChangeTracker.Clear();
            }

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
                await tx.CommitAsync(ct);
                throw new ConflictException(ClaimClosedMessage);
            }

            var requesterUser = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == claim.RequesterUserId, ct);
            if (requesterUser is null ||
                requesterUser.AccountStatus != AccountStatus.ACTIVE ||
                (requesterUser.LockedUntil.HasValue && requesterUser.LockedUntil.Value > nowUtc))
            {
                throw new ConflictException(ClaimClosedMessage);
            }

            if (claim.TargetPatientId is not long targetId)
                throw new ConflictException(TargetMissingMessage);

            var currentCandidates = await _db.Patients.AsNoTracking()
                .Where(p => p.UserId == claim.RequesterUserId)
                .OrderBy(p => p.Id)
                .Take(2)
                .ToListAsync(ct);
            if (currentCandidates.Count != 1)
                throw new ConflictException(ClaimClosedMessage);

            var current = currentCandidates[0];
            if (!patientIdsToLock.Contains(current.Id) ||
                !string.Equals(current.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(ClaimClosedMessage);
            }

            if (current.Id == targetId)
                throw new ConflictException(SameProfileMessage);

            var target = await _db.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == targetId, ct);
            if (target is null || !string.Equals(target.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException(TargetMissingMessage);
            if (target.UserId is not null)
                throw new ConflictException(TargetLinkedMessage);

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
                await tx.CommitAsync(ct);
                return Map(claim);
            }

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

            // BUG-P2-02: Downgrade any GUARDIAN_CREATED family links on targetPatient now that it has a formal User account.
            var guardianLinks = await _db.PatientFamilyLinks
                .Include(l => l.OwnerPatient)
                .Where(l =>
                    l.MemberPatientId == targetId &&
                    l.RevokedAt == null &&
                    l.VerificationMethod != null &&
                    l.VerificationMethod.ToUpper() == "GUARDIAN_CREATED")
                .ToListAsync(ct);

            foreach (var gLink in guardianLinks)
            {
                gLink.VerificationStatus = Pending;
                gLink.VerificationMethod = null;
                gLink.VerifiedAt = null;
                gLink.VerifiedByStaffId = null;
                if (gLink.OwnerPatient?.UserId is long ownerUserId)
                {
                    affectedGuardianOwnerUserIds.Add(ownerUserId);
                }
            }

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
                downgradedGuardianLinks = guardianLinks.Count,
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            linkedTargetId = targetId;
            response = Map(claim);
        }

        foreach (var ownerUserId in affectedGuardianOwnerUserIds.Distinct())
        {
            await RealtimeRevokeSafeExtensions.TryRevokeFamilyMemberAccessSafeAsync(
                _accessRevoker,
                ownerUserId,
                linkedTargetId,
                ct);
        }

        return response;
    }

    public async Task<IdentityClaimResponse> RejectAsync(
        long actorUserId,
        long claimId,
        RejectIdentityClaimRequest request,
        CancellationToken ct = default)
    {
        var authority = await RequireStaffAuthorityAsync(actorUserId, ct);
        var note = request?.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(note) || note.Length < 5)
            throw new ValidationException(NoteRequiredMessage);

        await using var tx = await _db.BeginTransactionAsync(ct);
        await _db.TryLockIdentityClaimForUpdateAsync(claimId, ct);
        if (_db is DbContext efContext)
        {
            efContext.ChangeTracker.Clear();
        }

        var claim = await _store.GetAsync(claimId, ct)
            ?? throw new NotFoundException(ClaimNotFoundMessage);

        if (!authority.IsSystemAdmin && authority.HospitalId != claim.HospitalId)
            throw new ForbiddenException(ForbiddenMessage);

        if (claim.Status != Pending && claim.Status != NeedsReconciliation)
            throw new ConflictException(ClaimClosedMessage);

        claim.Status = Rejected;
        claim.DecidedAt = _time.GetUtcNow().UtcDateTime;
        claim.DecidedByStaffId = authority.StaffId;
        claim.DecisionNote = note.Length > 500 ? note[..500] : note;

        AddAudit(actorUserId, claim.HospitalId, "PATIENT_IDENTITY_CLAIM_REJECTED", claim.Id, new
        {
            reason = claim.DecisionNote,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(claim);
    }

    public async Task<VerifyFamilyLinkResponse> VerifyFamilyLinkAsync(
        long actorUserId,
        string linkId,
        VerifyFamilyLinkRequest? request = null,
        CancellationToken ct = default)
    {
        var authority = await RequireStaffAuthorityAsync(actorUserId, ct);
        var (ownerPatientId, memberPatientId) = ParseFamilyLinkIdentifier(linkId, request);

        if (!authority.IsSystemAdmin &&
            request?.HospitalId is long requestedHospitalId &&
            requestedHospitalId != authority.HospitalId)
        {
            throw new ForbiddenException(ForbiddenMessage);
        }

        await using var tx = await _db.BeginTransactionAsync(ct);
        await _db.TryLockPatientsForUpdateAsync([ownerPatientId, memberPatientId], ct);
        if (_db is DbContext efContext)
        {
            efContext.ChangeTracker.Clear();
        }

        var nowLocal = HospitalClock.GetNow(_time);
        var link = await _db.PatientFamilyLinks
            .Include(l => l.OwnerPatient)
            .Include(l => l.MemberPatient)
            .FirstOrDefaultAsync(
                l => l.OwnerPatientId == ownerPatientId && l.MemberPatientId == memberPatientId,
                ct)
            ?? throw new NotFoundException(FamilyLinkNotFoundMessage);

        if (link.RevokedAt.HasValue ||
            (link.ValidUntil.HasValue && link.ValidUntil.Value <= nowLocal) ||
            link.OwnerPatient is null ||
            link.MemberPatient is null ||
            !string.Equals(link.OwnerPatient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(link.MemberPatient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotFoundException(FamilyLinkNotFoundMessage);
        }

        if (!authority.IsSystemAdmin)
        {
            var staffHospitalId = authority.HospitalId
                ?? throw new ForbiddenException(ForbiddenMessage);
            var affiliatedHospitals = await CollectAffiliatedHospitalsAsync(ownerPatientId, memberPatientId, ct);
            if (affiliatedHospitals.Count > 0 && !affiliatedHospitals.Contains(staffHospitalId))
            {
                throw new ForbiddenException(ForbiddenMessage);
            }
        }

        if (!string.Equals(link.VerificationStatus, Pending, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(FamilyLinkNotPendingMessage);
        }

        link.VerificationStatus = "VERIFIED";
        link.VerificationMethod = "STAFF";
        link.VerifiedByStaffId = authority.StaffId;
        link.VerifiedAt = nowLocal;
        link.LegacyAccessUntil = null;

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = authority.HospitalId ?? request?.HospitalId,
            Action = "PATIENT_FAMILY_LINK_VERIFIED",
            EntityType = "PATIENT_FAMILY_LINK",
            EntityId = $"{ownerPatientId}:{memberPatientId}",
            ChangeData = JsonSerializer.Serialize(new
            {
                ownerPatientId,
                memberPatientId,
                verifiedByStaffId = authority.StaffId,
            }),
            OccurredAt = _time.GetUtcNow().UtcDateTime,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new VerifyFamilyLinkResponse(
            ownerPatientId,
            memberPatientId,
            link.VerificationStatus,
            link.VerificationMethod,
            link.VerifiedByStaffId,
            nowLocal);
    }

    private static (long OwnerPatientId, long MemberPatientId) ParseFamilyLinkIdentifier(
        string? linkId,
        VerifyFamilyLinkRequest? request)
    {
        if (!string.IsNullOrWhiteSpace(linkId))
        {
            var trimmed = linkId.Trim();
            var match = CompositeLinkIdRegex.Match(trimmed);
            if (match.Success &&
                long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId) &&
                long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var memberId) &&
                ownerId > 0 &&
                memberId > 0 &&
                ownerId != memberId)
            {
                return (ownerId, memberId);
            }

            if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var singleMemberId) &&
                singleMemberId > 0 &&
                request?.OwnerPatientId is long bodyOwnerId &&
                bodyOwnerId > 0 &&
                bodyOwnerId != singleMemberId)
            {
                return (bodyOwnerId, singleMemberId);
            }
        }

        if (request?.OwnerPatientId is long reqOwner &&
            request.MemberPatientId is long reqMember &&
            reqOwner > 0 &&
            reqMember > 0 &&
            reqOwner != reqMember)
        {
            return (reqOwner, reqMember);
        }

        throw new ValidationException("Định danh liên kết hồ sơ người thân không hợp lệ (định dạng: {ownerPatientId}:{memberPatientId}).");
    }

    private async Task<HashSet<long>> CollectAffiliatedHospitalsAsync(
        long ownerPatientId,
        long memberPatientId,
        CancellationToken ct)
    {
        var patientIds = new[] { ownerPatientId, memberPatientId };
        var hospitals = new HashSet<long>();

        var visits = await _db.Visits.AsNoTracking()
            .Where(v => v.PatientId != null && patientIds.Contains(v.PatientId.Value))
            .ToListAsync(ct);
        if (visits.Count > 0)
        {
            var visitHospitalMap = await VisitHospitalResolver.CollectManyAsync(_db, visits, ct);
            foreach (var set in visitHospitalMap.Values)
            {
                foreach (var hId in set)
                    hospitals.Add(hId);
            }
        }

        var appointmentHospitalIds = await _db.Appointments.AsNoTracking()
            .Where(a => a.PatientId != null && patientIds.Contains(a.PatientId.Value) && a.Slot != null)
            .Select(a => a.Slot!.Service != null && a.Slot.Service.HospitalId != null
                ? a.Slot.Service.HospitalId
                : (a.Slot.Room != null && a.Slot.Room.HospitalId != null
                    ? a.Slot.Room.HospitalId
                    : (a.Slot.Doctor != null ? a.Slot.Doctor.HospitalId : null)))
            .Where(hId => hId != null)
            .Select(hId => hId!.Value)
            .Distinct()
            .ToListAsync(ct);
        foreach (var hId in appointmentHospitalIds)
            hospitals.Add(hId);

        return hospitals;
    }

    private async Task<Patient> EnsurePatientRoleAsync(long actorUserId, CancellationToken ct)
    {
        var self = await PatientSelfResolver.LoadActiveAsync(_db, actorUserId, ct, _time);
        var codes = await LoadRoleCodesAsync(actorUserId, ct);
        if (!codes.Contains(RoleNames.Patient))
            throw new ForbiddenException(ForbiddenMessage);
        return self;
    }

    private async Task<StaffAuthority> RequireStaffAuthorityAsync(long actorUserId, CancellationToken ct)
    {
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        if (user.AccountStatus != AccountStatus.ACTIVE ||
            (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc))
        {
            throw new UnauthorizedAccessException("Tài khoản người dùng chưa kích hoạt hoặc đang bị khóa.");
        }

        var codes = await LoadRoleCodesAsync(actorUserId, ct);
        var isSystemAdmin = codes.Contains(RoleNames.SystemAdmin);
        var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var hasPermission = await (
            from rp in _db.RolePermissions.AsNoTracking()
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where roleIds.Contains(rp.RoleId) &&
                  (p.Code == PermissionCodes.PatientIdentityVerify ||
                   (isSystemAdmin && (p.Code == PermissionCodes.UserManage || p.Code == PermissionCodes.HospitalConfig)))
            select rp).AnyAsync(ct);

        if (!hasPermission)
            throw new ForbiddenException(ForbiddenMessage);

        if (!isSystemAdmin)
        {
            if (staff is null ||
                staff.HospitalId is null ||
                !string.Equals(staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenException(ForbiddenMessage);
            }

            var hospitalActive = await _db.Hospitals.AsNoTracking()
                .AnyAsync(h => h.Id == staff.HospitalId.Value && h.IsActive != false, ct);
            if (!hospitalActive)
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
        if (visitIds.Count > 0)
        {
            if (await _db.Payments.AsNoTracking().AnyAsync(p => p.VisitId != null && visitIds.Contains(p.VisitId.Value), ct))
                return true;
            if (await TryAnyAsync(() => _db.VisitCharges.AsNoTracking().AnyAsync(vc => vc.VisitId != null && visitIds.Contains(vc.VisitId.Value), ct)))
                return true;
            if (await TryAnyAsync(() => _db.MedicalRecords.AsNoTracking().AnyAsync(mr => mr.VisitId != null && visitIds.Contains(mr.VisitId.Value), ct)))
                return true;
            if (await TryAnyAsync(() => _db.Prescriptions.AsNoTracking().AnyAsync(rx => rx.VisitId != null && visitIds.Contains(rx.VisitId.Value), ct)))
                return true;
            if (await TryAnyAsync(() => _db.DiagnosticOrders.AsNoTracking().AnyAsync(o => o.VisitId != null && visitIds.Contains(o.VisitId.Value), ct)))
                return true;
        }

        return false;
    }

    private static async Task<bool> TryAnyAsync(Func<Task<bool>> query)
    {
        try
        {
            return await query();
        }
        catch (NotSupportedException)
        {
            return false;
        }
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
