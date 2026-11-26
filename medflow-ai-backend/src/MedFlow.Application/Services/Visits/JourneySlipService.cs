using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

/// <summary>
/// Stage E2 / QE-21 / E2-R09 — Visit Journey Slip.
/// Emits a minimal operational slip for the rightful viewer (patient owner or same-hospital staff).
/// The VisitCode is an internal lookup key only — never an access token; no service names,
/// payment status/amounts, diagnosis, results, or patient PHI are exposed.
/// </summary>
public sealed class JourneySlipService : IJourneySlipService
{
    private const string ViewedAction = "JOURNEY_SLIP_VIEWED";
    private const string PrintedAction = "JOURNEY_SLIP_PRINTED";
    private const string VisitEntityType = "VISIT";
    private const string SlipForbiddenMessage = "Người dùng không có quyền xem phiếu hành trình của lượt khám này.";
    private const string AuditOnlyNote = "Audit only — not a physical print job.";

    private static readonly HashSet<string> StaffRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNames.Reception,
        RoleNames.Doctor,
        RoleNames.DiagnosticStaff,
        RoleNames.PharmacyStaff,
        RoleNames.HospitalAdmin,
        RoleNames.SystemAdmin,
        RoleNames.HeadDoctor,
        RoleNames.ClinicalNurse,
    };

    private static readonly HashSet<string> TerminalQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPLETED",
        "DONE",
        "CANCELLED",
        "DEFERRED",
        "NO_SHOW",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public JourneySlipService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<JourneySlipDto> GetJourneySlipAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default)
    {
        var context = await ResolveAuthorizedContextAsync(actorUserId, visitIdOrCode, ct);
        var slip = await BuildSlipAsync(context, ct);

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = context.VisitHospitalId,
            Action = ViewedAction,
            EntityType = VisitEntityType,
            EntityId = context.Visit.Id.ToString(CultureInfo.InvariantCulture),
            Reason = "Xem phiếu hành trình lượt khám",
            ChangeData = JsonSerializer.Serialize(new
            {
                visitId = context.Visit.Id,
                visitCode = slip.VisitCode,
                actorRole = context.ActorRole,
                ticketCount = slip.Tickets.Count,
            }),
            OccurredAt = context.NowUtc,
        });
        await _db.SaveChangesAsync(ct);

        return slip;
    }

    public async Task<JourneySlipPrintResultDto> PrintJourneySlipAsync(
        long actorUserId,
        string visitIdOrCode,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var context = await ResolveAuthorizedContextAsync(actorUserId, visitIdOrCode, ct);
        var slip = await BuildSlipAsync(context, ct);

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = context.VisitHospitalId,
            Action = PrintedAction,
            EntityType = VisitEntityType,
            EntityId = context.Visit.Id.ToString(CultureInfo.InvariantCulture),
            Reason = "In phiếu hành trình lượt khám (ghi nhận kiểm toán, không in vật lý)",
            ChangeData = JsonSerializer.Serialize(new
            {
                visitId = context.Visit.Id,
                visitCode = slip.VisitCode,
                actorRole = context.ActorRole,
                ticketCount = slip.Tickets.Count,
            }),
            IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var parsedIp)
                ? parsedIp
                : null,
            OccurredAt = context.NowUtc,
        });
        await _db.SaveChangesAsync(ct);

        return new JourneySlipPrintResultDto(
            VisitId: context.Visit.Id,
            PrintedAtUtc: context.NowUtc,
            PrintAuditRecorded: true,
            Note: AuditOnlyNote);
    }

    private async Task<AuthorizedSlipContext> ResolveAuthorizedContextAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct)
    {
        var actor = await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (_, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        var visit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct)
            ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        if (hospitalIds.Count == 0)
        {
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }
        if (hospitalIds.Count > 1)
        {
            throw new ConflictException(VisitPaymentAccess.HospitalConflictMessage);
        }
        var visitHospitalId = hospitalIds.First();

        var isPatient = roleCodes.Contains(RoleNames.Patient);
        var staffRoles = roleCodes.Where(c => StaffRoleCodes.Contains(c)).ToList();
        var isStaff = staffRoles.Count > 0;

        if (!isPatient && !isStaff)
        {
            throw new ForbiddenException(SlipForbiddenMessage);
        }

        var actorRole = RoleNames.Patient;
        var authorized = false;

        if (isPatient)
        {
            var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
            if (visit.PatientId.HasValue && owned.Contains(visit.PatientId.Value))
            {
                authorized = true;
                actorRole = RoleNames.Patient;
            }
        }

        if (!authorized && isStaff)
        {
            var staff = await _db.Staff.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);
            var staffIsActive = staff is not null &&
                string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
                staff.HospitalId.HasValue;

            if (staffIsActive && staff!.HospitalId!.Value == visitHospitalId)
            {
                var hospital = await _db.Hospitals.AsNoTracking()
                    .FirstOrDefaultAsync(h => h.Id == visitHospitalId, ct);
                if (hospital?.IsActive == true)
                {
                    authorized = true;
                    actorRole = staffRoles.FirstOrDefault() ?? RoleNames.Reception;
                }
            }
        }

        if (!authorized)
        {
            throw new ForbiddenException(SlipForbiddenMessage);
        }

        _ = actor; // actor validated active above

        return new AuthorizedSlipContext(
            actorUserId,
            actorRole,
            visitHospitalId,
            visit,
            _timeProvider.GetUtcNow().UtcDateTime);
    }

    private async Task<JourneySlipDto> BuildSlipAsync(
        AuthorizedSlipContext context,
        CancellationToken ct)
    {
        var visit = context.Visit;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var visitQueues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q => q.VisitId == visit.Id)
            .OrderBy(q => q.QueueDate)
            .ThenBy(q => q.QueueNumber)
            .ThenBy(q => q.Id)
            .ToListAsync(ct);

        var servicePointIds = visitQueues
            .Select(q => q.CurrentServicePointId ?? q.ServicePointId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var servicePointsById = servicePointIds.Count == 0
            ? new Dictionary<long, QueueServicePoint>()
            : await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp => servicePointIds.Contains(sp.Id))
                .ToDictionaryAsync(sp => sp.Id, ct);

        var tickets = visitQueues
            .Where(q =>
            {
                var status = AppointmentBookingRules.NormalizeStatusKey(q.Status);
                return !TerminalQueueStatuses.Contains(status);
            })
            .Select(q =>
            {
                var spId = q.CurrentServicePointId ?? q.ServicePointId;
                var sp = spId.HasValue && servicePointsById.TryGetValue(spId.Value, out var found) ? found : null;
                var queueType = string.IsNullOrWhiteSpace(q.QueueType)
                    ? "EXAMINATION"
                    : q.QueueType.Trim().ToUpperInvariant();
                return new JourneySlipTicketDto(
                    QueueEntryId: $"q-{q.Id:D3}",
                    QueueType: queueType,
                    TicketNo: VisitQueueEnrollmentService.FormatTicketNo(queueType, q.QueueNumber),
                    ServicePointName: sp?.Name,
                    Status: AppointmentBookingRules.NormalizeStatusKey(q.Status));
            })
            .ToList();

        string? doctorDisplay = null;
        if (visit.AssignedDoctorId.HasValue)
        {
            var doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == visit.AssignedDoctorId.Value, ct);
            if (doctor is not null)
            {
                var name = doctor.Staff?.FullName;
                var title = doctor.ProfessionalTitle;
                doctorDisplay = string.IsNullOrWhiteSpace(title)
                    ? name
                    : (string.IsNullOrWhiteSpace(name) ? title : $"{title} {name}");
            }
        }

        string? roomDisplay = null;
        if (visit.ExaminationRoomId.HasValue)
        {
            var room = await _db.HospitalRooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value, ct);
            if (room is not null)
            {
                roomDisplay = string.IsNullOrWhiteSpace(room.Location)
                    ? $"{room.Name} - {room.RoomCode}"
                    : $"{room.Name} - {room.RoomCode} ({room.Location})";
            }
        }

        var servingDate = visit.CheckedInAt.HasValue
            ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
            : todayLocal;
        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode!.Trim()
            : $"VIS-{servingDate:yyyyMM}-{visit.Id:D4}";

        var presenceState = VisitPresenceEvaluator.NormalizePresenceState(visit.PresenceState);

        return new JourneySlipDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            PresenceState: presenceState,
            CurrentAssignedDoctorDisplay: doctorDisplay,
            CurrentExaminationRoomDisplay: roomDisplay,
            Tickets: tickets,
            ServingDate: servingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            GeneratedAtUtc: context.NowUtc,
            InternalLookupQr: $"MEDFLOW-VISIT:{visitCode}");
    }

    private sealed record AuthorizedSlipContext(
        long ActorUserId,
        string ActorRole,
        long VisitHospitalId,
        Visit Visit,
        DateTime NowUtc);
}
