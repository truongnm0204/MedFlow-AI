using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Realtime;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Services.Visits;

/// <summary>
/// Clinical Session Recovery authority (PR #859 / Issue #858, F01 Option B).
///
/// Authority model (ADR-0061 + clinical-session-recovery-authority-decision.md):
/// - Only EXAMINATION / RETURN_CONSULTATION interrupted clinical serving tickets are recoverable.
/// - GRANT requires an active DOCTOR with CLINICAL_SESSION_RECOVERY_GRANT; the recipient must be a
///   different active DOCTOR with CLINICAL_SESSION_RECOVER in the same hospital and clinical scope.
/// - CONFIRM is the assigned doctor's own-session path (REQUESTED -> RECOVERED) or the delegated
///   grant recipient (GRANTED -> RECOVERED). Grants are single-use, default expiry 2h, revocable.
/// - SYSTEM_ADMIN / RECEPTION / HOSPITAL_ADMIN / DIAGNOSTIC_STAFF / PHARMACY_STAFF / PATIENT can
///   never grant or confirm merely by role.
/// - Recovery only releases the clinical ticket: no Visit finalization, EMR/Dx/Rx/orders/result
///   mutation, payment change, presence stamp or reassignment.
/// </summary>
public sealed class ClinicalSessionRecoveryService : IClinicalSessionRecoveryService
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HashSet<string> TerminalVisitStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPLETED", "DONE", "CANCELLED", "NO_SHOW",
    };

    private static readonly HashSet<string> ServingQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "IN_PROGRESS", "IN_CONSULTATION", "SERVING",
    };

    private static readonly HashSet<string> ClinicalQueueTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "EXAMINATION", "RETURN_CONSULTATION",
    };

    private static readonly HashSet<string> NonClinicalServingQueueTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DIAGNOSTIC", "PHARMACY",
    };

    private static readonly HashSet<string> ActiveServingVisitStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "IN_PROGRESS", "IN_CONSULTATION", "SERVING", "RETURN_CONSULTATION",
    };

    private readonly IApplicationDbContext _db;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;
    private readonly TimeProvider _timeProvider;

    public ClinicalSessionRecoveryService(
        IApplicationDbContext db,
        IRealtimeNotificationPublisher? realtimePublisher = null,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _realtimePublisher = realtimePublisher ?? new NullRealtimeNotificationPublisher();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // =====================================================================================
    // GET context
    // =====================================================================================

    public async Task<ClinicalSessionRecoveryContextDto> GetContextAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default)
    {
        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var actor = await LoadActorAsync(actorUserId, nowUtc, ct);
        EnsureNotSystemAdmin(actor);
        EnsureRecoveryRoleClass(actor);

        // D6 final ordering: visibility first (404), permission second (403).
        var (visit, hospitalId) = await ResolveVisitInScopeAsync(actor, idOrCode, ct);

        EnsureContextReader(actor);

        var queues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q => q.VisitId == visit.Id)
            .OrderBy(q => q.Id)
            .ToListAsync(ct);

        var requests = await _db.ClinicalSessionRecoveryRequests
            .AsNoTracking()
            .Where(r => r.VisitId == visit.Id)
            .OrderByDescending(r => r.Id)
            .ToListAsync(ct);

        var (assignedDoctorCode, assignedDoctorName) = await ResolveDoctorDisplayAsync(visit.AssignedDoctorId, ct);
        var clinicalQueues = queues
            .Where(q => IsClinicalQueueType(q.QueueType))
            .Select(q => new ClinicalSessionRecoveryQueueDto(
                q.Id,
                NormalizeQueueType(q.QueueType),
                Normalize(q.Status),
                q.StateVersion ?? 1,
                q.QueueDate,
                q.CompletedAt))
            .ToList();

        var canReadClinical = actor.Doctor is not null && IsActiveDoctor(actor);
        var capabilities = new ClinicalSessionRecoveryCapabilitiesDto(
            CanRequest: CanCreateRequest(actor) && clinicalQueues.Any(q => IsServingQueueStatus(q.Status)),
            CanGrant: canReadClinical && Has(actor, PermissionCodes.ClinicalSessionRecoveryGrant)
                      && requests.Any(r => r.Status == ClinicalSessionRecoveryStatuses.Requested),
            CanConfirmOwnSession: canReadClinical
                                  && Has(actor, PermissionCodes.ClinicalSessionRecover)
                                  && visit.AssignedDoctorId == actor.Doctor!.Id
                                  && requests.Any(r => r.Status == ClinicalSessionRecoveryStatuses.Requested),
            CanConfirmDelegated: canReadClinical
                                 && Has(actor, PermissionCodes.ClinicalSessionRecover)
                                 && requests.Any(r => r.Status == ClinicalSessionRecoveryStatuses.Granted
                                                      && r.AuthorizedDoctorId == actor.Doctor!.Id
                                                      && r.ExpiresAt.HasValue && r.ExpiresAt.Value > nowUtc),
            CanReject: requests.Any(r => r.Status is ClinicalSessionRecoveryStatuses.Requested or ClinicalSessionRecoveryStatuses.Granted)
                       && (CanCreateRequest(actor) || CanDecideGrant(actor)),
            CanRevoke: requests.Any(r => r.Status == ClinicalSessionRecoveryStatuses.Granted)
                       && CanDecideGrant(actor),
            CanCancel: requests.Any(r => r.Status == ClinicalSessionRecoveryStatuses.Requested
                                        && (r.RequestedByUserId == actor.UserId || CanCreateRequest(actor))));

        return new ClinicalSessionRecoveryContextDto(
            VisitId: visit.Id,
            VisitCode: FormatVisitCode(visit),
            VisitStatus: Normalize(visit.Status),
            HospitalId: hospitalId,
            AssignedDoctorId: visit.AssignedDoctorId,
            AssignedDoctorCode: assignedDoctorCode,
            AssignedDoctorName: assignedDoctorName,
            Queues: clinicalQueues,
            Requests: requests.Select(MapRequest).ToList(),
            Capabilities: capabilities);
    }

    // =====================================================================================
    // POST request
    // =====================================================================================

    public async Task<ClinicalSessionRecoveryRequestDto> RequestRecoveryAsync(
        long actorUserId,
        string idOrCode,
        CreateClinicalSessionRecoveryRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var reason = NormalizeReason(request.Reason);
        var actor = await LoadActorAsync(actorUserId, nowUtc, ct);
        EnsureNotSystemAdmin(actor);
        EnsureRecoveryRoleClass(actor);

        // D6 final ordering: resolve tenant/clinical visibility FIRST (uniform 404 for not-found,
        // other-hospital, out-of-scope or missing scope mapping). Only a visible resource can produce 403.
        var (preVisit, hospitalId) = await ResolveVisitInScopeAsync(actor, idOrCode, ct);

        if (!CanCreateRequest(actor))
        {
            throw new ForbiddenException(
                "Chỉ nhân viên Tiếp đón đủ quyền hoặc bác sĩ có quyền phục hồi phiên khám mới được tạo yêu cầu.");
        }

        IDbContextTransaction? tx = null;
        long createdRequestId = 0;
        (long HospitalId, long? ServicePointId, long? RoomId, long? AppointmentId) notification;

        try
        {
            tx = await _db.BeginTransactionAsync(ct);

            await AcquirePatientVisitAndQueueLocksAsync(preVisit, [request.QueueEntryId], ct);
            ClearChangeTracker();

            var visit = await _db.Visits.FirstAsync(v => v.Id == preVisit.Id, ct);
            EnsureVisitNotTerminal(visit);
            var queue = await LoadQueueForVisitAsync(visit.Id, request.QueueEntryId, ct);
            EnsureClinicalServingQueue(queue);
            EnsureExpectedVersion(queue, request.ExpectedStateVersion);

            // F3: expire stale grants inside the same transaction before accepting a new request.
            var unresolved = await LoadUnresolvedRequestsAsync(queue.Id, ct);
            if (unresolved.Count > 0)
            {
                await _db.TryLockClinicalSessionRecoveryRequestsForUpdateAsync(
                    unresolved.Select(r => r.Id).ToArray(), ct);
                ClearChangeTracker();
                unresolved = await LoadUnresolvedRequestsAsync(queue.Id, ct);
            }

            var expiring = unresolved
                .Where(r => r.Status == ClinicalSessionRecoveryStatuses.Granted
                            && r.ExpiresAt.HasValue && r.ExpiresAt.Value <= nowUtc)
                .ToList();
            foreach (var grant in expiring)
            {
                grant.Status = ClinicalSessionRecoveryStatuses.Expired;
                grant.ExpiredAt = nowUtc;
                grant.UpdatedAt = nowUtc;
                grant.StateVersion += 1;
                AddAudit(actor.UserId, grant.HospitalId, ClinicalSessionRecoveryConstants.RecoveryExpiredAuditAction,
                    grant.Id, visit.Id, queue.Id, actor.Doctor?.Id, null, nowUtc, ipAddress,
                    new { expiredAt = nowUtc, previousStatus = ClinicalSessionRecoveryStatuses.Granted });
            }

            var remaining = unresolved
                .Where(r => r.Status is ClinicalSessionRecoveryStatuses.Requested or ClinicalSessionRecoveryStatuses.Granted)
                .ToList();
            if (remaining.Count > 0)
            {
                throw new ConflictException(ClinicalSessionRecoveryConstants.UnresolvedRequestExistsMessage);
            }

            var entity = new ClinicalSessionRecoveryRequest
            {
                HospitalId = hospitalId,
                VisitId = visit.Id,
                QueueEntryId = queue.Id,
                OriginalDoctorId = visit.AssignedDoctorId,
                RequestedByUserId = actor.UserId,
                RequestedAt = nowUtc,
                RequestReason = reason,
                ExpectedQueueStateVersion = queue.StateVersion ?? 1,
                Status = ClinicalSessionRecoveryStatuses.Requested,
                OwnSession = false,
                StateVersion = 1,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            };
            _db.ClinicalSessionRecoveryRequests.Add(entity);
            await _db.SaveChangesAsync(ct);

            AddAudit(actor.UserId, hospitalId, ClinicalSessionRecoveryConstants.RecoveryRequestedAuditAction,
                entity.Id, visit.Id, queue.Id, actor.Doctor?.Id, visit.AssignedDoctorId, nowUtc, ipAddress,
                new { queueEntryId = queue.Id, queueStateVersion = queue.StateVersion ?? 1 }, reason);

            await _db.SaveChangesAsync(ct);
            createdRequestId = entity.Id;
            await tx.CommitAsync(ct);
            tx = null;

            notification = (hospitalId, queue.CurrentServicePointId ?? queue.ServicePointId, visit.ExaminationRoomId, visit.AppointmentId);
        }
        catch (DbUpdateException ex) when (PostgresConcurrencyException.IsUniqueViolation(ex))
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.UnresolvedRequestExistsMessage);
        }
        catch (DbUpdateException ex) when (PostgresConcurrencyException.TryGetSqlState(ex) is "23503" or "23514")
        {
            // F-03: FK/check violations are data-consistency failures, not "unresolved request exists".
            throw new ConflictException(ClinicalSessionRecoveryConstants.RequestRejectedByConstraintMessage);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.StaleStateVersionMessage);
        }
        finally
        {
            if (tx is not null)
            {
                await SafeRollbackAsync(tx, ct);
            }
        }

        await PublishQueueChangedSafeAsync(notification, ct);

        var saved = await _db.ClinicalSessionRecoveryRequests
            .AsNoTracking()
            .FirstAsync(r => r.Id == createdRequestId, ct);
        return MapRequest(saved);
    }

    // =====================================================================================
    // POST grant
    // =====================================================================================

    public async Task<ClinicalSessionRecoveryRequestDto> GrantAsync(
        long actorUserId,
        long requestId,
        GrantClinicalSessionRecoveryRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var reason = NormalizeReason(request.Reason);
        var actor = await LoadActorAsync(actorUserId, nowUtc, ct);
        EnsureNotSystemAdmin(actor);
        EnsureRecoveryRoleClass(actor);

        var (preRequest, preVisit, hospitalId) = await ResolveRequestInScopeAsync(actor, requestId, ct);
        var issuer = RequireGrantIssuer(actor);

        if (request.AuthorizedDoctorId <= 0)
        {
            throw new ValidationException("Bác sĩ nhận grant không hợp lệ.");
        }

        if (request.AuthorizedDoctorId == issuer.Id)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.SelfGrantMessage);
        }

        if (preRequest.Status == ClinicalSessionRecoveryStatuses.Granted &&
            preRequest.AuthorizedDoctorId == request.AuthorizedDoctorId &&
            preRequest.GrantedByDoctorId == issuer.Id &&
            preRequest.ExpectedQueueStateVersion == request.ExpectedStateVersion)
        {
            return MapRequest(preRequest);
        }

        if (preRequest.Status != ClinicalSessionRecoveryStatuses.Requested)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.GrantNotActiveMessage);
        }

        IDbContextTransaction? tx = null;
        (long HospitalId, long? ServicePointId, long? RoomId, long? AppointmentId) notification;

        try
        {
            tx = await _db.BeginTransactionAsync(ct);

            await AcquirePatientVisitAndQueueLocksAsync(preVisit, [preRequest.QueueEntryId], ct);
            await _db.TryLockClinicalSessionRecoveryRequestForUpdateAsync(requestId, ct);
            ClearChangeTracker();

            // Re-read live permissions and rows after locks (no TOCTOU on revoke/role update).
            actor = await LoadActorAsync(actorUserId, nowUtc, ct);
            issuer = RequireGrantIssuer(actor);

            var recovery = await LoadRequestAsync(requestId, ct);
            var visit = await _db.Visits.FirstAsync(v => v.Id == recovery.VisitId, ct);
            var queue = await LoadQueueForVisitAsync(visit.Id, recovery.QueueEntryId, ct);

            if (recovery.Status == ClinicalSessionRecoveryStatuses.Granted &&
                recovery.AuthorizedDoctorId == request.AuthorizedDoctorId &&
                recovery.GrantedByDoctorId == issuer.Id &&
                recovery.ExpectedQueueStateVersion == request.ExpectedStateVersion)
            {
                await tx.CommitAsync(ct);
                tx = null;
                return MapRequest(recovery);
            }

            if (recovery.Status != ClinicalSessionRecoveryStatuses.Requested)
            {
                throw new ConflictException(ClinicalSessionRecoveryConstants.GrantNotActiveMessage);
            }

            EnsureVisitNotTerminal(visit);
            EnsureClinicalServingQueue(queue);
            EnsureExpectedVersion(queue, request.ExpectedStateVersion);
            EnsureExpectedVersion(recovery.ExpectedQueueStateVersion, request.ExpectedStateVersion);

            // Issuer must also be inside the clinical scope of the session being authorized.
            if (!await IsDoctorInClinicalScopeAsync(issuer, visit, ct))
            {
                throw new ConflictException(ClinicalSessionRecoveryConstants.IssuerNotEligibleMessage);
            }

            if (visit.AssignedDoctorId.HasValue &&
                recovery.OriginalDoctorId.HasValue &&
                visit.AssignedDoctorId.Value != recovery.OriginalDoctorId.Value)
            {
                throw new ConflictException(ClinicalSessionRecoveryConstants.StaleStateVersionMessage);
            }

            var recipient = await LoadDoctorCandidateAsync(request.AuthorizedDoctorId, hospitalId, ct);
            if (recipient is null
                || !recipient.Staff.UserId.HasValue
                || !await HasPermissionAsync(recipient.Staff.UserId.Value, PermissionCodes.ClinicalSessionRecover, ct)
                || !await IsDoctorInClinicalScopeAsync(recipient.Doctor, visit, ct))
            {
                // RA-07: issuer has visibility; the operation fails because the target doctor lacks
                // RECOVER/active/same-hospital/clinical scope → 403 (D6 class b), not 409/404.
                throw new ForbiddenException(ClinicalSessionRecoveryConstants.RecipientNotEligibleMessage);
            }

            if (recipient.Staff.UserId == actor.UserId)
            {
                throw new ConflictException(ClinicalSessionRecoveryConstants.SelfGrantMessage);
            }

            recovery.AuthorizedDoctorId = recipient.Doctor.Id;
            recovery.GrantedByUserId = actor.UserId;
            recovery.GrantedByDoctorId = issuer.Id;
            recovery.GrantedAt = nowUtc;
            recovery.GrantReason = reason;
            recovery.ExpiresAt = nowUtc.Add(ClinicalSessionRecoveryConstants.GrantLifetime);
            recovery.Status = ClinicalSessionRecoveryStatuses.Granted;
            recovery.UpdatedAt = nowUtc;
            recovery.StateVersion += 1;

            AddAudit(actor.UserId, hospitalId, ClinicalSessionRecoveryConstants.RecoveryGrantedAuditAction,
                recovery.Id, visit.Id, queue.Id, issuer.Id, recovery.AuthorizedDoctorId, nowUtc, ipAddress,
                new
                {
                    authorizedDoctorId = recovery.AuthorizedDoctorId,
                    expiresAt = recovery.ExpiresAt,
                    expectedQueueStateVersion = recovery.ExpectedQueueStateVersion,
                }, reason);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            tx = null;

            notification = (hospitalId, queue.CurrentServicePointId ?? queue.ServicePointId, visit.ExaminationRoomId, visit.AppointmentId);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.StaleStateVersionMessage);
        }
        finally
        {
            if (tx is not null)
            {
                await SafeRollbackAsync(tx, ct);
            }
        }

        await PublishQueueChangedSafeAsync(notification, ct);

        var saved = await _db.ClinicalSessionRecoveryRequests.AsNoTracking().FirstAsync(r => r.Id == requestId, ct);
        return MapRequest(saved);
    }

    // =====================================================================================
    // POST reject
    // =====================================================================================

    public Task<ClinicalSessionRecoveryRequestDto> RejectAsync(
        long actorUserId,
        long requestId,
        ClinicalSessionRecoveryDecisionRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
        => DecideAsync(actorUserId, requestId, request, ClinicalSessionRecoveryStatuses.Rejected, ipAddress, ct);

    // =====================================================================================
    // POST revoke
    // =====================================================================================

    public Task<ClinicalSessionRecoveryRequestDto> RevokeAsync(
        long actorUserId,
        long requestId,
        ClinicalSessionRecoveryDecisionRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
        => DecideAsync(actorUserId, requestId, request, ClinicalSessionRecoveryStatuses.Revoked, ipAddress, ct);

    // =====================================================================================
    // POST cancel
    // =====================================================================================

    public Task<ClinicalSessionRecoveryRequestDto> CancelAsync(
        long actorUserId,
        long requestId,
        ClinicalSessionRecoveryDecisionRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
        => DecideAsync(actorUserId, requestId, request, ClinicalSessionRecoveryStatuses.Cancelled, ipAddress, ct);

    private async Task<ClinicalSessionRecoveryRequestDto> DecideAsync(
        long actorUserId,
        long requestId,
        ClinicalSessionRecoveryDecisionRequestDto request,
        string targetStatus,
        string? ipAddress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var reason = NormalizeReason(request.Reason);
        var actor = await LoadActorAsync(actorUserId, nowUtc, ct);
        EnsureNotSystemAdmin(actor);
        EnsureRecoveryRoleClass(actor);

        var (preRequest, preVisit, hospitalId) = await ResolveRequestInScopeAsync(actor, requestId, ct);

        // Idempotent replay of the same terminal decision.
        if (preRequest.Status == targetStatus)
        {
            var sameActor = targetStatus switch
            {
                ClinicalSessionRecoveryStatuses.Rejected => preRequest.RejectedByUserId == actor.UserId,
                ClinicalSessionRecoveryStatuses.Revoked => preRequest.RevokedByUserId == actor.UserId,
                _ => preRequest.CancelledByUserId == actor.UserId,
            };
            if (sameActor)
            {
                return MapRequest(preRequest);
            }

            throw new ConflictException(ClinicalSessionRecoveryConstants.GrantNotActiveMessage);
        }

        IDbContextTransaction? tx = null;
        (long HospitalId, long? ServicePointId, long? RoomId, long? AppointmentId) notification;

        try
        {
            tx = await _db.BeginTransactionAsync(ct);
            await AcquirePatientVisitAndQueueLocksAsync(preVisit, [preRequest.QueueEntryId], ct);
            await _db.TryLockClinicalSessionRecoveryRequestForUpdateAsync(requestId, ct);
            ClearChangeTracker();

            actor = await LoadActorAsync(actorUserId, nowUtc, ct);
            var recovery = await LoadRequestAsync(requestId, ct);
            var visit = await _db.Visits.FirstAsync(v => v.Id == recovery.VisitId, ct);
            var queue = await LoadQueueForVisitAsync(visit.Id, recovery.QueueEntryId, ct);

            if (recovery.Status == targetStatus)
            {
                await tx.CommitAsync(ct);
                tx = null;
                return MapRequest(recovery);
            }

            if (request.ExpectedStateVersion.HasValue)
            {
                EnsureExpectedVersion(queue, request.ExpectedStateVersion.Value);
            }

            switch (targetStatus)
            {
                case ClinicalSessionRecoveryStatuses.Rejected:
                    EnsureCanReject(actor, visit, recovery);
                    if (recovery.Status is not (ClinicalSessionRecoveryStatuses.Requested or ClinicalSessionRecoveryStatuses.Granted))
                    {
                        throw new ConflictException(ClinicalSessionRecoveryConstants.GrantNotActiveMessage);
                    }

                    var previousRejectedStatus = recovery.Status;
                    recovery.Status = ClinicalSessionRecoveryStatuses.Rejected;
                    recovery.RejectedByUserId = actor.UserId;
                    recovery.RejectedAt = nowUtc;
                    recovery.RejectionReason = reason;
                    AddAudit(actor.UserId, hospitalId, ClinicalSessionRecoveryConstants.RecoveryRejectedAuditAction,
                        recovery.Id, visit.Id, queue.Id, actor.Doctor?.Id, recovery.AuthorizedDoctorId, nowUtc, ipAddress,
                        new { previousStatus = previousRejectedStatus }, reason);
                    break;

                case ClinicalSessionRecoveryStatuses.Revoked:
                    if (recovery.Status != ClinicalSessionRecoveryStatuses.Granted)
                    {
                        throw new ConflictException(ClinicalSessionRecoveryConstants.GrantNotActiveMessage);
                    }

                    var revoker = RequireGrantIssuer(actor);
                    recovery.Status = ClinicalSessionRecoveryStatuses.Revoked;
                    recovery.RevokedByUserId = actor.UserId;
                    recovery.RevokedByDoctorId = revoker.Id;
                    recovery.RevokedAt = nowUtc;
                    recovery.RevocationReason = reason;
                    AddAudit(actor.UserId, hospitalId, ClinicalSessionRecoveryConstants.RecoveryRevokedAuditAction,
                        recovery.Id, visit.Id, queue.Id, revoker.Id, recovery.AuthorizedDoctorId, nowUtc, ipAddress,
                        new { previousStatus = ClinicalSessionRecoveryStatuses.Granted }, reason);
                    break;

                case ClinicalSessionRecoveryStatuses.Cancelled:
                    if (recovery.Status != ClinicalSessionRecoveryStatuses.Requested)
                    {
                        throw new ConflictException(ClinicalSessionRecoveryConstants.GrantNotActiveMessage);
                    }

                    var isRequester = recovery.RequestedByUserId == actor.UserId;
                    var isReceptionCoordinator = actor.Roles.Contains(RoleNames.Reception) &&
                                                 Has(actor, PermissionCodes.QueueEnroll);
                    if (!isRequester && !isReceptionCoordinator && !CanDecideGrant(actor))
                    {
                        throw new ForbiddenException("Người dùng không có quyền rút yêu cầu phục hồi phiên khám này.");
                    }

                    recovery.Status = ClinicalSessionRecoveryStatuses.Cancelled;
                    recovery.CancelledByUserId = actor.UserId;
                    recovery.CancelledAt = nowUtc;
                    recovery.CancellationReason = reason;
                    AddAudit(actor.UserId, hospitalId, ClinicalSessionRecoveryConstants.RecoveryCancelledAuditAction,
                        recovery.Id, visit.Id, queue.Id, actor.Doctor?.Id, recovery.AuthorizedDoctorId, nowUtc, ipAddress,
                        new { previousStatus = ClinicalSessionRecoveryStatuses.Requested }, reason);
                    break;

                default:
                    throw new ValidationException("Thao tác quyết định phục hồi không hợp lệ.");
            }

            recovery.UpdatedAt = nowUtc;
            recovery.StateVersion += 1;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            tx = null;

            notification = (hospitalId, queue.CurrentServicePointId ?? queue.ServicePointId, visit.ExaminationRoomId, visit.AppointmentId);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.StaleStateVersionMessage);
        }
        finally
        {
            if (tx is not null)
            {
                await SafeRollbackAsync(tx, ct);
            }
        }

        await PublishQueueChangedSafeAsync(notification, ct);

        var saved = await _db.ClinicalSessionRecoveryRequests.AsNoTracking().FirstAsync(r => r.Id == requestId, ct);
        return MapRequest(saved);
    }

    // =====================================================================================
    // POST confirm
    // =====================================================================================

    public async Task<ClinicalSessionRecoveryConfirmResponseDto> ConfirmAsync(
        long actorUserId,
        long requestId,
        ConfirmClinicalSessionRecoveryRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmInterrupted)
        {
            throw new ValidationException(ClinicalSessionRecoveryConstants.ConfirmationAttestationRequiredMessage);
        }

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var reason = NormalizeReason(request.Reason);
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var payloadHash = ComputeConfirmationHash(requestId, request, reason);

        var actor = await LoadActorAsync(actorUserId, nowUtc, ct);
        EnsureNotSystemAdmin(actor);
        EnsureRecoveryRoleClass(actor);

        var (preRequest, preVisit, hospitalId) = await ResolveRequestInScopeAsync(actor, requestId, ct);

        // Confirmation idempotency/replay handling before acquiring locks.
        if (preRequest.Status == ClinicalSessionRecoveryStatuses.Recovered)
        {
            return BuildReplayOrConflict(preRequest, preVisit, actor, idempotencyKey, payloadHash);
        }

        IDbContextTransaction? tx = null;
        ClinicalSessionRecoveryConfirmResponseDto response;
        (long HospitalId, long? ServicePointId, long? RoomId, long? AppointmentId) notification;

        try
        {
            tx = await _db.BeginTransactionAsync(ct);
            await AcquirePatientVisitAndQueueLocksAsync(preVisit, [preRequest.QueueEntryId], ct);
            await _db.TryLockClinicalSessionRecoveryRequestForUpdateAsync(requestId, ct);
            ClearChangeTracker();

            // Re-read permission/role/staff state at the mutation point (revoke/role-update aware).
            actor = await LoadActorAsync(actorUserId, nowUtc, ct);
            var recovery = await LoadRequestAsync(requestId, ct);
            var visit = await _db.Visits.FirstAsync(v => v.Id == recovery.VisitId, ct);
            var queue = await LoadQueueForVisitAsync(visit.Id, recovery.QueueEntryId, ct);

            if (recovery.Status == ClinicalSessionRecoveryStatuses.Recovered)
            {
                await tx.CommitAsync(ct);
                tx = null;
                return BuildReplayOrConflict(recovery, visit, actor, idempotencyKey, payloadHash);
            }

            EnsureVisitNotTerminal(visit);
            EnsureClinicalServingQueue(queue);
            EnsureExpectedVersion(queue, request.ExpectedStateVersion);
            EnsureExpectedVersion(recovery.ExpectedQueueStateVersion, request.ExpectedStateVersion);

            if (recovery.Status == ClinicalSessionRecoveryStatuses.Expired
                || recovery.Status == ClinicalSessionRecoveryStatuses.Revoked
                || recovery.Status == ClinicalSessionRecoveryStatuses.Rejected
                || recovery.Status == ClinicalSessionRecoveryStatuses.Cancelled)
            {
                throw new ConflictException(
                    recovery.Status == ClinicalSessionRecoveryStatuses.Expired
                        ? ClinicalSessionRecoveryConstants.GrantExpiredMessage
                        : ClinicalSessionRecoveryConstants.GrantNotActiveMessage);
            }

            var doctorAuthority = RequireRecoverAuthority(actor);
            if (visit.AssignedDoctorId.HasValue &&
                recovery.OriginalDoctorId.HasValue &&
                visit.AssignedDoctorId.Value != recovery.OriginalDoctorId.Value)
            {
                throw new ConflictException(ClinicalSessionRecoveryConstants.StaleStateVersionMessage);
            }

            var isOwnSession = recovery.Status == ClinicalSessionRecoveryStatuses.Requested
                               && visit.AssignedDoctorId.HasValue
                               && visit.AssignedDoctorId.Value == doctorAuthority.Id;

            if (recovery.Status == ClinicalSessionRecoveryStatuses.Granted)
            {
                if (recovery.AuthorizedDoctorId != doctorAuthority.Id)
                {
                    throw new ForbiddenException(ClinicalSessionRecoveryConstants.NoPhysicianAuthorityMessage);
                }

                if (!recovery.ExpiresAt.HasValue || recovery.ExpiresAt.Value <= nowUtc)
                {
                    recovery.Status = ClinicalSessionRecoveryStatuses.Expired;
                    recovery.ExpiredAt = nowUtc;
                    recovery.UpdatedAt = nowUtc;
                    recovery.StateVersion += 1;
                    AddAudit(actor.UserId, hospitalId, ClinicalSessionRecoveryConstants.RecoveryExpiredAuditAction,
                        recovery.Id, visit.Id, queue.Id, doctorAuthority.Id, recovery.AuthorizedDoctorId, nowUtc, ipAddress,
                        new { expiredAt = nowUtc });
                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    tx = null;
                    throw new ConflictException(ClinicalSessionRecoveryConstants.GrantExpiredMessage);
                }
            }
            else if (!isOwnSession)
            {
                throw new ForbiddenException(ClinicalSessionRecoveryConstants.NoPhysicianAuthorityMessage);
            }

            if (!await IsDoctorInClinicalScopeAsync(doctorAuthority, visit, ct))
            {
                throw new ForbiddenException(ClinicalSessionRecoveryConstants.NoPhysicianAuthorityMessage);
            }

            var previousVisitStatus = Normalize(visit.Status);
            var previousQueueStatus = Normalize(queue.Status);
            var previousQueueVersion = queue.StateVersion ?? 1;

            // Release ONLY the authorized clinical serving ticket. No new ticket, no reassignment,
            // no presence stamp, no payment/EMR/order mutation.
            queue.Status = "DEFERRED";
            queue.CompletedAt = nowLocal;
            queue.BlockedReason =
                $"{ClinicalSessionRecoveryConstants.RecoveryQueueStatusReasonPrefix} Phiên khám bị gián đoạn đã được phục hồi an toàn theo yêu cầu #{recovery.Id} (DEFERRED).";
            queue.StateVersion = previousQueueVersion + 1;

            // Stale clinical writers guard on the visit serving status; move it to a non-serving state.
            if (ActiveServingVisitStatuses.Contains(previousVisitStatus))
            {
                var hasOpenOrders = await _db.DiagnosticOrders
                    .AsNoTracking()
                    .AnyAsync(o =>
                        o.VisitId == visit.Id &&
                        (o.Status == null || !(o.Status.ToUpper() == "COMPLETED" || o.Status.ToUpper() == "CANCELLED")), ct);
                visit.Status = hasOpenOrders ? "AWAITING_RESULTS" : "CHECKED_IN";
            }

            recovery.Status = ClinicalSessionRecoveryStatuses.Recovered;
            recovery.OwnSession = isOwnSession;
            recovery.RecoveredByUserId = actor.UserId;
            recovery.RecoveredByDoctorId = doctorAuthority.Id;
            recovery.RecoveredAt = nowUtc;
            recovery.RecoveryReason = reason;
            recovery.ConfirmationPayloadHash = payloadHash;
            if (idempotencyKey is not null)
            {
                recovery.IdempotencyKey = idempotencyKey;
                recovery.IdempotencyActorUserId = actor.UserId;
                recovery.IdempotencyOperation = ClinicalSessionRecoveryOperations.Confirm;
            }

            recovery.UpdatedAt = nowUtc;
            recovery.StateVersion += 1;

            AddAudit(actor.UserId, hospitalId, ClinicalSessionRecoveryConstants.RecoveryConfirmedAuditAction,
                recovery.Id, visit.Id, queue.Id, doctorAuthority.Id, recovery.AuthorizedDoctorId, nowUtc, ipAddress,
                new
                {
                    ownSession = recovery.OwnSession,
                    previousQueueStatus,
                    previousQueueStateVersion = previousQueueVersion,
                    queueStatus = queue.Status,
                    queueStateVersion = queue.StateVersion,
                    previousVisitStatus,
                    visitStatus = visit.Status,
                    expectedQueueStateVersion = recovery.ExpectedQueueStateVersion,
                    idempotencyKey,
                }, reason);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            tx = null;

            notification = (hospitalId, queue.CurrentServicePointId ?? queue.ServicePointId, visit.ExaminationRoomId, visit.AppointmentId);
            response = new ClinicalSessionRecoveryConfirmResponseDto(
                RequestId: recovery.Id,
                Status: recovery.Status,
                VisitId: visit.Id,
                VisitCode: FormatVisitCode(visit),
                QueueEntryId: queue.Id,
                QueueStatus: Normalize(queue.Status),
                QueueStateVersion: queue.StateVersion ?? previousQueueVersion + 1,
                VisitStatus: Normalize(visit.Status),
                RecoveredAt: recovery.RecoveredAt ?? nowUtc,
                IsIdempotentReplay: false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.StaleStateVersionMessage);
        }
        finally
        {
            if (tx is not null)
            {
                await SafeRollbackAsync(tx, ct);
            }
        }

        // SignalR invalidation strictly after commit; never on rollback.
        await PublishQueueChangedSafeAsync(notification, ct);
        return response;
    }

    private ClinicalSessionRecoveryConfirmResponseDto BuildReplayOrConflict(
        ClinicalSessionRecoveryRequest recovered,
        Visit visit,
        RecoveryActor actor,
        string? idempotencyKey,
        string payloadHash)
    {
        var sameActor = recovered.RecoveredByUserId == actor.UserId;
        if (!sameActor)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.AlreadyRecoveredMessage);
        }

        if (idempotencyKey is not null)
        {
            var keyMatches = string.Equals(recovered.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)
                             && recovered.IdempotencyActorUserId == actor.UserId
                             && string.Equals(recovered.IdempotencyOperation, ClinicalSessionRecoveryOperations.Confirm, StringComparison.Ordinal);
            if (!keyMatches)
            {
                throw new ConflictException(ClinicalSessionRecoveryConstants.IdempotencyMismatchMessage);
            }
        }

        if (recovered.ConfirmationPayloadHash is not null &&
            !string.Equals(recovered.ConfirmationPayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.IdempotencyMismatchMessage);
        }

        return new ClinicalSessionRecoveryConfirmResponseDto(
            RequestId: recovered.Id,
            Status: recovered.Status,
            VisitId: recovered.VisitId,
            VisitCode: FormatVisitCode(visit),
            QueueEntryId: recovered.QueueEntryId,
            QueueStatus: "DEFERRED",
            QueueStateVersion: recovered.ExpectedQueueStateVersion + 1,
            VisitStatus: Normalize(visit.Status),
            RecoveredAt: recovered.RecoveredAt ?? recovered.UpdatedAt,
            IsIdempotentReplay: true);
    }

    // =====================================================================================
    // PM-approved Commit B additions: scoped list + eligible recipients
    // =====================================================================================

    public async Task<IReadOnlyList<ClinicalSessionRecoveryListItemDto>> ListRequestsAsync(
        long actorUserId,
        CancellationToken ct = default)
    {
        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var actor = await LoadActorAsync(actorUserId, nowUtc, ct);
        EnsureNotSystemAdmin(actor);
        EnsureRecoveryRoleClass(actor);

        // List has no single resource: visibility = actor's hospital. No uniform-404 semantics needed;
        // actors outside their own tenant simply see an empty list. Missing tenant => 403.
        var hospitalId = actor.Doctor?.HospitalId ?? actor.Staff?.HospitalId;
        if (!hospitalId.HasValue)
        {
            throw new ForbiddenException("Người dùng không có cơ sở y tế hợp lệ để xem danh sách phục hồi phiên khám.");
        }

        EnsureContextReader(actor);

        var requests = await _db.ClinicalSessionRecoveryRequests
            .AsNoTracking()
            .Where(r => r.HospitalId == hospitalId.Value)
            .OrderByDescending(r => r.Id)
            .Take(200)
            .ToListAsync(ct);
        if (requests.Count == 0)
        {
            return [];
        }

        var visitIds = requests.Select(r => r.VisitId).Distinct().ToList();
        var visits = await _db.Visits.AsNoTracking()
            .Where(v => visitIds.Contains(v.Id))
            .ToListAsync(ct);
        var visitById = visits.ToDictionary(v => v.Id);

        if (actor.Doctor is not null)
        {
            var scopeCache = new Dictionary<long, bool>();
            var scoped = new List<ClinicalSessionRecoveryRequest>();
            foreach (var request in requests)
            {
                if (request.AuthorizedDoctorId == actor.Doctor.Id)
                {
                    scoped.Add(request);
                    continue;
                }

                if (request.Status != ClinicalSessionRecoveryStatuses.Requested
                    || !visitById.TryGetValue(request.VisitId, out var visit))
                {
                    continue;
                }

                if (!scopeCache.TryGetValue(visit.Id, out var inScope))
                {
                    inScope = await IsDoctorInClinicalScopeAsync(actor.Doctor, visit, ct);
                    scopeCache[visit.Id] = inScope;
                }

                if (inScope)
                {
                    scoped.Add(request);
                }
            }

            requests = scoped;
        }

        return requests.Select(r => new ClinicalSessionRecoveryListItemDto(
                RequestId: r.Id,
                Status: Normalize(r.Status),
                HospitalId: r.HospitalId,
                VisitId: r.VisitId,
                VisitCode: visitById.TryGetValue(r.VisitId, out var v) ? FormatVisitCode(v) : $"VIS-{r.VisitId}",
                QueueEntryId: r.QueueEntryId,
                OriginalDoctorId: r.OriginalDoctorId,
                AuthorizedDoctorId: r.AuthorizedDoctorId,
                ExpectedQueueStateVersion: r.ExpectedQueueStateVersion,
                RequestedAt: r.RequestedAt,
                GrantedAt: r.GrantedAt,
                ExpiresAt: r.ExpiresAt,
                RecoveredAt: r.RecoveredAt))
            .ToList();
    }

    public async Task<IReadOnlyList<ClinicalSessionRecoveryEligibleRecipientDto>> GetEligibleRecipientsAsync(
        long actorUserId,
        long requestId,
        CancellationToken ct = default)
    {
        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        var actor = await LoadActorAsync(actorUserId, nowUtc, ct);
        EnsureNotSystemAdmin(actor);
        EnsureRecoveryRoleClass(actor);

        // D6: visibility first (404), then authority (403).
        var (request, visit, hospitalId) = await ResolveRequestInScopeAsync(actor, requestId, ct);
        var issuer = RequireGrantIssuer(actor);

        if (request.Status != ClinicalSessionRecoveryStatuses.Requested)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.GrantNotActiveMessage);
        }

        var doctors = await _db.Doctors.AsNoTracking()
            .Include(d => d.Staff)
            .Where(d =>
                d.IsActive &&
                (d.HospitalId == hospitalId || (d.Staff != null && d.Staff.HospitalId == hospitalId)))
            .OrderBy(d => d.DoctorCode)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);

        var departments = await _db.Departments.AsNoTracking()
            .Where(d => d.HospitalId == hospitalId)
            .ToDictionaryAsync(d => d.Id, ct);
        var specialties = await _db.Specialties.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, ct);

        var recipients = new List<ClinicalSessionRecoveryEligibleRecipientDto>();
        foreach (var doctor in doctors)
        {
            if (doctor.Id == issuer.Id || doctor.Staff is null || !doctor.Staff.UserId.HasValue)
            {
                continue;
            }

            var staff = doctor.Staff;
            if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!await HasPermissionAsync(staff.UserId.Value, PermissionCodes.ClinicalSessionRecover, ct))
            {
                continue;
            }

            if (!await IsDoctorInClinicalScopeAsync(doctor, visit, ct))
            {
                continue;
            }

            departments.TryGetValue(staff.DepartmentId ?? 0, out var department);
            specialties.TryGetValue(doctor.SpecialtyId ?? 0, out var specialty);
            recipients.Add(new ClinicalSessionRecoveryEligibleRecipientDto(
                DoctorId: doctor.Id,
                DoctorCode: doctor.DoctorCode ?? string.Empty,
                DoctorName: staff.FullName ?? doctor.DoctorCode ?? string.Empty,
                DepartmentId: staff.DepartmentId,
                DepartmentName: department?.Name,
                SpecialtyId: doctor.SpecialtyId,
                SpecialtyName: specialty?.Name));
        }

        return recipients.Take(100).ToList();
    }

    // =====================================================================================
    // Authorization
    // =====================================================================================

    private sealed record RecoveryActor(
        long UserId,
        HashSet<string> Roles,
        HashSet<string> Permissions,
        Staff? Staff,
        Doctor? Doctor);

    private sealed record DoctorCandidate(Doctor Doctor, Staff Staff);

    private static bool Has(RecoveryActor actor, string permission)
        => actor.Permissions.Contains(permission);

    private static bool IsActiveDoctor(RecoveryActor actor)
        => actor.Doctor is not null
           && actor.Doctor.IsActive
           && actor.Staff is not null
           && string.Equals(actor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase)
           && actor.Doctor.HospitalId.HasValue;

    private static void EnsureNotSystemAdmin(RecoveryActor actor)
    {
        if (actor.Roles.Contains(RoleNames.SystemAdmin))
        {
            throw new ForbiddenException(
                "SYSTEM_ADMIN không có quyền thực hiện phục hồi phiên khám lâm sàng (không bypass chính sách chuyên môn).");
        }
    }

    /// <summary>
    /// Role-class gate (RA-03/RA-04): PATIENT / DIAGNOSTIC_STAFF / PHARMACY_STAFF / SYSTEM_ADMIN can never
    /// operate this surface, so they get 403 before any resource lookup (no tenant visibility exists for them).
    /// RECEPTION / DOCTOR / HOSPITAL_ADMIN then pass through scope resolution before the 403 permission gate.
    /// </summary>
    private static void EnsureRecoveryRoleClass(RecoveryActor actor)
    {
        var allowed = actor.Roles.Contains(RoleNames.Reception)
                      || actor.Roles.Contains(RoleNames.Doctor)
                      || actor.Roles.Contains(RoleNames.HospitalAdmin);
        if (!allowed)
        {
            throw new ForbiddenException("Người dùng không có quyền thực hiện phục hồi phiên khám lâm sàng.");
        }
    }

    private static void EnsureContextReader(RecoveryActor actor)
    {
        var activeStaff = actor.Staff is not null
                          && string.Equals(actor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
        var isReception = actor.Roles.Contains(RoleNames.Reception)
                          && Has(actor, PermissionCodes.QueueEnroll)
                          && activeStaff;
        var isDoctor = actor.Roles.Contains(RoleNames.Doctor) && IsActiveDoctor(actor)
                       && (Has(actor, PermissionCodes.ClinicalSessionRecover) || Has(actor, PermissionCodes.ClinicalSessionRecoveryGrant));
        var isPolicyAdmin = actor.Roles.Contains(RoleNames.HospitalAdmin)
                            && activeStaff
                            && Has(actor, PermissionCodes.ClinicalSessionRecover)
                            && Has(actor, PermissionCodes.ClinicalSessionRecoveryGrant);
        if (!isReception && !isDoctor && !isPolicyAdmin)
        {
            throw new ForbiddenException("Người dùng không có quyền xem ngữ cảnh phục hồi phiên khám.");
        }
    }

    private static bool CanCreateRequest(RecoveryActor actor)
    {
        if (actor.Roles.Contains(RoleNames.Reception))
        {
            return Has(actor, PermissionCodes.QueueEnroll)
                   && actor.Staff is not null
                   && string.Equals(actor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase)
                   && actor.Staff.HospitalId.HasValue;
        }

        if (actor.Roles.Contains(RoleNames.Doctor))
        {
            return IsActiveDoctor(actor)
                   && (Has(actor, PermissionCodes.ClinicalSessionRecover) || Has(actor, PermissionCodes.ClinicalSessionRecoveryGrant));
        }

        if (actor.Roles.Contains(RoleNames.HospitalAdmin))
        {
            return Has(actor, PermissionCodes.ClinicalSessionRecover)
                   && Has(actor, PermissionCodes.ClinicalSessionRecoveryGrant)
                   && actor.Staff is not null
                   && string.Equals(actor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool CanDecideGrant(RecoveryActor actor)
        => IsActiveDoctor(actor)
           && Has(actor, PermissionCodes.ClinicalSessionRecoveryGrant)
           && actor.Doctor!.HospitalId.HasValue;

    private static Doctor RequireGrantIssuer(RecoveryActor actor)
    {
        if (!IsActiveDoctor(actor) || !Has(actor, PermissionCodes.ClinicalSessionRecoveryGrant))
        {
            throw new ForbiddenException(ClinicalSessionRecoveryConstants.IssuerNotEligibleMessage);
        }

        return actor.Doctor!;
    }

    private static Doctor RequireRecoverAuthority(RecoveryActor actor)
    {
        if (!IsActiveDoctor(actor) || !Has(actor, PermissionCodes.ClinicalSessionRecover))
        {
            throw new ForbiddenException(ClinicalSessionRecoveryConstants.NoPhysicianAuthorityMessage);
        }

        return actor.Doctor!;
    }

    private static void EnsureCanReject(RecoveryActor actor, Visit visit, ClinicalSessionRecoveryRequest recovery)
    {
        var isAssigned = actor.Doctor is not null && visit.AssignedDoctorId == actor.Doctor.Id;
        var isRecipient = actor.Doctor is not null && recovery.AuthorizedDoctorId == actor.Doctor.Id;
        var isGrantAuthority = IsActiveDoctor(actor) && Has(actor, PermissionCodes.ClinicalSessionRecoveryGrant);
        if (!(isGrantAuthority || (IsActiveDoctor(actor) && (isAssigned || isRecipient) && Has(actor, PermissionCodes.ClinicalSessionRecover))))
        {
            throw new ForbiddenException("Người dùng không có thẩm quyền từ chối yêu cầu phục hồi phiên khám.");
        }
    }

    // =====================================================================================
    // Scope resolution & loading
    // =====================================================================================

    private async Task<RecoveryActor> LoadActorAsync(long actorUserId, DateTime nowUtc, CancellationToken ct)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        if (user.AccountStatus != AccountStatus.ACTIVE ||
            (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc))
        {
            throw new ForbiddenException("Tài khoản người dùng hiện không ở trạng thái hoạt động hợp lệ.");
        }

        var roleIds = (await _db.UserRoles.AsNoTracking()
                .Where(ur => ur.UserId == actorUserId)
                .Select(ur => ur.RoleId)
                .ToListAsync(ct))
            .ToHashSet();

        var roles = roleIds.Count == 0
            ? []
            : await _db.Roles.AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => r.Code)
                .ToListAsync(ct);

        var permissions = await LoadPermissionCodesAsync(actorUserId, ct);

        var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);
        var doctor = staff is not null
            ? await _db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.StaffId == staff.Id, ct)
            : null;

        return new RecoveryActor(
            actorUserId,
            new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase),
            permissions,
            staff,
            doctor);
    }

    private async Task<HashSet<string>> LoadPermissionCodesAsync(long userId, CancellationToken ct)
    {
        var roleIds = (await _db.UserRoles.AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync(ct))
            .ToHashSet();

        if (roleIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var permIds = (await _db.RolePermissions.AsNoTracking()
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct))
            .ToHashSet();

        if (permIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var codes = await _db.Permissions.AsNoTracking()
            .Where(p => permIds.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(ct);
        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> HasPermissionAsync(long userId, string permissionCode, CancellationToken ct)
        => (await LoadPermissionCodesAsync(userId, ct)).Contains(permissionCode);

    private async Task<(Visit Visit, long HospitalId)> ResolveVisitInScopeAsync(
        RecoveryActor actor,
        string idOrCode,
        CancellationToken ct)
    {
        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.VisitCode == idOrCode, ct);
        if (visit is null &&
            long.TryParse(idOrCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId) &&
            numericId > 0)
        {
            visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == numericId, ct);
        }

        if (visit is null)
        {
            throw new NotFoundException(ClinicalSessionRecoveryConstants.ContextNotFoundMessage);
        }

        var actorHospitalId = actor.Doctor?.HospitalId ?? actor.Staff?.HospitalId;
        if (!actorHospitalId.HasValue)
        {
            // Uniform 404: no tenant scope means no visibility (fail closed, no existence oracle).
            throw new NotFoundException(ClinicalSessionRecoveryConstants.ContextNotFoundMessage);
        }

        HashSet<long> visitHospitals;
        try
        {
            visitHospitals = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        }
        catch (NotSupportedException)
        {
            visitHospitals = [];
        }

        // F-01 (security): an unresolvable visit hospital (empty set or resolver NotSupportedException)
        // is NOT proof of tenancy. Treat it exactly like a mismatch: uniform 404, fail closed. Reception /
        // HospitalAdmin have no clinical-scope fallback, so this is their only tenant constraint.
        if (visitHospitals.Count == 0 || !visitHospitals.Contains(actorHospitalId.Value))
        {
            // Uniform 404 for not-found and not-in-scope: no existence/PHI oracle.
            throw new NotFoundException(ClinicalSessionRecoveryConstants.ContextNotFoundMessage);
        }

        // D6: a DOCTOR outside the visit's clinical scope is treated as if the resource were not visible.
        if (actor.Doctor is not null
            && !await IsDoctorInClinicalScopeAsync(actor.Doctor, visit, ct))
        {
            throw new NotFoundException(ClinicalSessionRecoveryConstants.ContextNotFoundMessage);
        }

        return (visit, actorHospitalId.Value);
    }

    private async Task<(ClinicalSessionRecoveryRequest Request, Visit Visit, long HospitalId)> ResolveRequestInScopeAsync(
        RecoveryActor actor,
        long requestId,
        CancellationToken ct)
    {
        var request = await _db.ClinicalSessionRecoveryRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
        {
            throw new NotFoundException(ClinicalSessionRecoveryConstants.RequestNotFoundMessage);
        }

        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VisitId, ct);
        if (visit is null)
        {
            throw new NotFoundException(ClinicalSessionRecoveryConstants.RequestNotFoundMessage);
        }

        var actorHospitalId = actor.Doctor?.HospitalId ?? actor.Staff?.HospitalId;
        if (actorHospitalId.HasValue && actorHospitalId.Value != request.HospitalId)
        {
            throw new NotFoundException(ClinicalSessionRecoveryConstants.RequestNotFoundMessage);
        }

        if (!actorHospitalId.HasValue)
        {
            throw new NotFoundException(ClinicalSessionRecoveryConstants.RequestNotFoundMessage);
        }

        if (actor.Doctor is not null
            && !await IsDoctorInClinicalScopeAsync(actor.Doctor, visit, ct))
        {
            throw new NotFoundException(ClinicalSessionRecoveryConstants.RequestNotFoundMessage);
        }

        return (request, visit, request.HospitalId);
    }

    private async Task<ClinicalSessionRecoveryRequest> LoadRequestAsync(long requestId, CancellationToken ct)
        => await _db.ClinicalSessionRecoveryRequests.FirstAsync(r => r.Id == requestId, ct);

    private async Task<List<ClinicalSessionRecoveryRequest>> LoadUnresolvedRequestsAsync(long queueEntryId, CancellationToken ct)
        => await _db.ClinicalSessionRecoveryRequests
            .Where(r => r.QueueEntryId == queueEntryId
                        && (r.Status == ClinicalSessionRecoveryStatuses.Requested
                            || r.Status == ClinicalSessionRecoveryStatuses.Granted))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

    private async Task<QueueEntry> LoadQueueForVisitAsync(long visitId, long queueEntryId, CancellationToken ct)
    {
        var queue = await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == queueEntryId, ct);
        if (queue is null || queue.VisitId != visitId)
        {
            // Uniform not-found shape; never reveal cross-visit existence.
            throw new NotFoundException(ClinicalSessionRecoveryConstants.RequestNotFoundMessage);
        }

        return queue;
    }

    private async Task<DoctorCandidate?> LoadDoctorCandidateAsync(long doctorId, long hospitalId, CancellationToken ct)
    {
        var doctor = await _db.Doctors.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == doctorId, ct);
        if (doctor is null || !doctor.IsActive)
        {
            return null;
        }

        var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == doctor.StaffId, ct);
        if (staff is null || !staff.UserId.HasValue ||
            !string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var effectiveHospitalId = doctor.HospitalId ?? staff.HospitalId;
        if (!effectiveHospitalId.HasValue || effectiveHospitalId.Value != hospitalId)
        {
            return null;
        }

        return new DoctorCandidate(doctor, staff);
    }

    /// <summary>
    /// Machine-checkable clinical scope (decision D2): the visit's examination service department/specialty
    /// (or the examination room's owning department) must match the doctor; service authorizations, when
    /// present, must include the doctor. If no mapping can be resolved for the visit, no physician is in
    /// scope (fail closed, RA-25) — no invented competency table.
    /// </summary>
    private async Task<bool> IsDoctorInClinicalScopeAsync(Doctor doctor, Visit visit, CancellationToken ct)
    {
        var staff = await _db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == doctor.StaffId, ct);
        var doctorDepartmentId = staff?.DepartmentId;

        var requiredServiceId = await ResolveRequiredServiceIdAsync(visit, ct);
        var service = requiredServiceId.HasValue
            ? await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == requiredServiceId.Value, ct)
            : null;

        var requiredSpecialtyId = service?.SpecialtyId;
        var requiredDepartmentId = service?.DepartmentId;

        if (!requiredSpecialtyId.HasValue && !requiredDepartmentId.HasValue)
        {
            // Ambiguity guard: fall back to the visit examination room's owning department.
            requiredDepartmentId = await ResolveVisitRoomDepartmentIdAsync(visit, ct);
        }

        // Fail closed: when the repository has no machine-checkable clinical mapping for this visit,
        // no physician is considered in scope (decision D2 / RA-25). No invented competency table.
        if (!requiredSpecialtyId.HasValue && !requiredDepartmentId.HasValue)
        {
            return false;
        }

        if (requiredSpecialtyId.HasValue
            && (!doctor.SpecialtyId.HasValue || doctor.SpecialtyId.Value != requiredSpecialtyId.Value))
        {
            return false;
        }

        if (requiredDepartmentId.HasValue
            && (!doctorDepartmentId.HasValue || doctorDepartmentId.Value != requiredDepartmentId.Value))
        {
            return false;
        }

        if (requiredServiceId.HasValue)
        {
            var authorizations = await _db.DoctorServiceAuthorizations.AsNoTracking()
                .Where(a => a.ServiceId == requiredServiceId.Value)
                .ToListAsync(ct);
            if (authorizations.Count > 0 &&
                !authorizations.Any(a => a.DoctorId == doctor.Id && a.IsActive != false))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<long?> ResolveVisitRoomDepartmentIdAsync(Visit visit, CancellationToken ct)
    {
        if (visit.ExaminationRoomId is not long roomId)
        {
            return null;
        }

        var room = await _db.HospitalRooms.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);
        return room?.DepartmentId;
    }

    private async Task<long?> ResolveRequiredServiceIdAsync(Visit visit, CancellationToken ct)
    {
        if (!visit.AppointmentId.HasValue)
        {
            return null;
        }

        var appointment = await _db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == visit.AppointmentId.Value, ct);
        if (appointment?.SlotId is not long slotId)
        {
            return null;
        }

        var slot = await _db.ExaminationSlots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        return slot?.ServiceId;
    }

    private async Task<(string? Code, string? Name)> ResolveDoctorDisplayAsync(long? doctorId, CancellationToken ct)
    {
        if (!doctorId.HasValue)
        {
            return (null, null);
        }

        var doctor = await _db.Doctors.AsNoTracking()
            .Include(d => d.Staff)
            .FirstOrDefaultAsync(d => d.Id == doctorId.Value, ct);
        return doctor is null
            ? (null, null)
            : (doctor.DoctorCode, doctor.Staff?.FullName);
    }

    // =====================================================================================
    // Validation helpers
    // =====================================================================================

    private static void EnsureVisitNotTerminal(Visit visit)
    {
        if (TerminalVisitStatuses.Contains(Normalize(visit.Status)))
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.VisitTerminalMessage);
        }
    }

    private static void EnsureClinicalServingQueue(QueueEntry queue)
    {
        var queueType = NormalizeQueueType(queue.QueueType);
        var queueStatus = Normalize(queue.Status);

        if (NonClinicalServingQueueTypes.Contains(queueType))
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.NotClinicalQueueMessage);
        }

        if (!ClinicalQueueTypes.Contains(queueType))
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.NotClinicalQueueMessage);
        }

        if (queue.CompletedAt.HasValue || !ServingQueueStatuses.Contains(queueStatus))
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.SessionNotInterruptedMessage);
        }
    }

    private static void EnsureExpectedVersion(QueueEntry queue, int expectedVersion)
    {
        if (expectedVersion <= 0 || (queue.StateVersion ?? 1) != expectedVersion)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.StaleStateVersionMessage);
        }
    }

    private static void EnsureExpectedVersion(int authoritativeVersion, int expectedVersion)
    {
        if (expectedVersion <= 0 || authoritativeVersion != expectedVersion)
        {
            throw new ConflictException(ClinicalSessionRecoveryConstants.StaleStateVersionMessage);
        }
    }

    private static bool IsClinicalQueueType(string? queueType)
    {
        var normalized = NormalizeQueueType(queueType);
        return ClinicalQueueTypes.Contains(normalized);
    }

    private static bool IsServingQueueStatus(string? status)
        => ServingQueueStatuses.Contains(Normalize(status));

    private static string NormalizeQueueType(string? queueType)
    {
        var normalized = Normalize(queueType);
        return string.IsNullOrEmpty(normalized) ? "EXAMINATION" : normalized;
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string NormalizeReason(string? rawReason)
    {
        var reason = rawReason?.Trim() ?? string.Empty;
        if (reason.Length is < ClinicalSessionRecoveryConstants.ReasonMinLength or > ClinicalSessionRecoveryConstants.ReasonMaxLength)
        {
            throw new ValidationException(
                $"Lý do phải từ {ClinicalSessionRecoveryConstants.ReasonMinLength} đến {ClinicalSessionRecoveryConstants.ReasonMaxLength} ký tự.");
        }

        if (reason.Contains('<') || reason.Contains('>'))
        {
            throw new ValidationException("Lý do không được chứa ký tự HTML.");
        }

        return reason;
    }

    private static string? NormalizeIdempotencyKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return null;
        }

        var key = rawKey.Trim();
        if (key.Length > ClinicalSessionRecoveryConstants.IdempotencyKeyMaxLength || key.Any(char.IsControl))
        {
            throw new ValidationException(
                $"IdempotencyKey không hợp lệ (tối đa {ClinicalSessionRecoveryConstants.IdempotencyKeyMaxLength} ký tự, không chứa ký tự điều khiển).");
        }

        return key;
    }

    private static string ComputeConfirmationHash(
        long requestId,
        ConfirmClinicalSessionRecoveryRequestDto request,
        string normalizedReason)
    {
        var payload = string.Join('|',
            ClinicalSessionRecoveryOperations.Confirm,
            requestId.ToString(CultureInfo.InvariantCulture),
            request.ExpectedStateVersion.ToString(CultureInfo.InvariantCulture),
            request.ConfirmInterrupted ? "1" : "0",
            normalizedReason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string FormatVisitCode(Visit visit)
        => string.IsNullOrWhiteSpace(visit.VisitCode)
            ? $"VIS-{visit.Id}"
            : visit.VisitCode.Trim();

    private static ClinicalSessionRecoveryRequestDto MapRequest(ClinicalSessionRecoveryRequest request)
        => new(
            RequestId: request.Id,
            Status: Normalize(request.Status),
            HospitalId: request.HospitalId,
            VisitId: request.VisitId,
            QueueEntryId: request.QueueEntryId,
            OriginalDoctorId: request.OriginalDoctorId,
            AuthorizedDoctorId: request.AuthorizedDoctorId,
            OwnSession: request.OwnSession,
            ExpectedQueueStateVersion: request.ExpectedQueueStateVersion,
            RequestedByUserId: request.RequestedByUserId,
            RequestedAt: request.RequestedAt,
            GrantedAt: request.GrantedAt,
            ExpiresAt: request.ExpiresAt,
            RecoveredAt: request.RecoveredAt,
            RevokedAt: request.RevokedAt,
            RejectedAt: request.RejectedAt,
            CancelledAt: request.CancelledAt,
            ExpiredAt: request.ExpiredAt);

    // =====================================================================================
    // Locking / transactions / audit / realtime
    // =====================================================================================

    /// <summary>
    /// ADR-0059 canonical order: patients (02) -> visits (09) -> queue_entries (11).
    /// The caller then locks clinical_session_recovery_requests (19) before mutating.
    /// </summary>
    private async Task AcquirePatientVisitAndQueueLocksAsync(
        Visit preVisit,
        IReadOnlyCollection<long> queueEntryIds,
        CancellationToken ct)
    {
        if (preVisit.PatientId is > 0)
        {
            await _db.TryLockPatientForUpdateAsync(preVisit.PatientId.Value, ct);
        }

        await _db.TryLockVisitForUpdateAsync(preVisit.Id, ct);

        var ids = queueEntryIds.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        if (ids.Length > 0)
        {
            await _db.TryLockQueueEntriesForUpdateAsync(ids, ct);
        }
    }

    private void ClearChangeTracker()
    {
        if (_db is DbContext efContext &&
            !string.Equals(efContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal))
        {
            efContext.ChangeTracker.Clear();
        }
    }

    private void AddAudit(
        long actorUserId,
        long hospitalId,
        string action,
        long requestId,
        long visitId,
        long queueEntryId,
        long? actorDoctorId,
        long? authorizedDoctorId,
        DateTime nowUtc,
        string? ipAddress,
        object payload,
        string? reason = null)
    {
        var changeData = JsonSerializer.Serialize(new
        {
            requestId,
            visitId,
            queueEntryId,
            actorDoctorId,
            authorizedDoctorId,
            payload,
        }, AuditJsonOptions);

        var audit = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospitalId,
            Action = action,
            EntityType = "CLINICAL_SESSION_RECOVERY_REQUEST",
            EntityId = requestId.ToString(CultureInfo.InvariantCulture),
            // The free-text reason is stored in audit_logs.reason only — never in ChangeData/SignalR/logs.
            Reason = reason,
            ChangeData = changeData,
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
        };

        _db.AuditLogs.Add(audit);
    }

    private static Task SafeRollbackAsync(IDbContextTransaction tx, CancellationToken ct)
        => tx.RollbackAsync(ct);

    private async Task PublishQueueChangedSafeAsync(
        (long HospitalId, long? ServicePointId, long? RoomId, long? AppointmentId) notification,
        CancellationToken ct)
    {
        await RealtimePublishSafeExtensions.TryPublishQueueChangedSafeAsync(
            _realtimePublisher,
            new QueueChangedNotification(
                HospitalId: notification.HospitalId,
                ServicePointId: notification.ServicePointId,
                RoomId: notification.RoomId,
                AppointmentId: notification.AppointmentId),
            ct);
    }
}
