using System.Globalization;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Reception;

public sealed class ReceptionPatientDetailService : IReceptionPatientDetailService
{
    private const int MaxPatientIdentifierLength = 40;
    private const string NotUpdated = "Chưa cập nhật";
    private const string CompletedStatus = "COMPLETED";
    private const string CancelledStatus = "CANCELLED";
    private const string NoShowStatus = "NO_SHOW";

    private const string PatientProfileReadPermissionCode = "PATIENT_PROFILE_READ";
    private const string PatientCheckInPermissionCode = "PATIENT_CHECKIN";

    private static readonly string[] ReceptionAllowedPermissions =
    [
        PatientProfileReadPermissionCode,
        PatientCheckInPermissionCode
    ];

    private static readonly string[] HospitalAdminAllowedPermissions =
    [
        PatientProfileReadPermissionCode,
        PatientCheckInPermissionCode,
        "VISIT_ASSIGN",
        "QUEUE_MANAGE",
        "HOSPITAL_CONFIG"
    ];

    private static readonly HashSet<string> InProgressVisitStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "IN_PROGRESS",
        "IN_CONSULTATION",
        "EXAMINATION_IN_PROGRESS",
        "AWAITING_RESULTS",
        "CALLED"
    };

    private static readonly HashSet<string> InProgressQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "IN_PROGRESS",
        "CALLED"
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ReceptionPatientDetailService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PatientDetailResponseDto> GetPatientDetailAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (trimmedIdentifier, parsedId, parsedCode) = ParsePatientIdentifier(idOrCode);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var actor = await EnsureActorAccessAsync(actorUserId, nowUtc, ct);

        var patientQuery = _db.Patients
            .AsNoTracking()
            .Include(p => p.User);

        var patient = parsedId.HasValue
            ? await patientQuery.FirstOrDefaultAsync(p => p.Id == parsedId.Value, ct)
            : await patientQuery.FirstOrDefaultAsync(
                p => p.PatientCode != null && p.PatientCode.ToUpper() == parsedCode,
                ct);

        if (patient is null)
        {
            throw new NotFoundException("Patient", trimmedIdentifier);
        }

        if (!string.Equals(patient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động.");
        }

        var (genderCode, genderLabel) = NormalizeGender(patient.Gender);
        var age = AppointmentSlotService.CalculateAge(patient.DateOfBirth, today);
        var dateOfBirthFormatted = patient.DateOfBirth.HasValue
            ? patient.DateOfBirth.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : NotUpdated;

        var isIdentityVerified = !string.IsNullOrWhiteSpace(patient.IdentificationHash);
        var identityCardMasked = isIdentityVerified
            ? PatientProfileService.MaskedVerifiedIdentityText
            : null;
        var cccdDisplay = isIdentityVerified
            ? PatientProfileService.MaskedVerifiedIdentityText
            : NotUpdated;

        var phone = !string.IsNullOrWhiteSpace(patient.Phone)
            ? patient.Phone.Trim()
            : NotUpdated;

        var email = FirstNonEmpty(patient.Email, patient.User?.Email);

        var address = !string.IsNullOrWhiteSpace(patient.Address)
            ? patient.Address.Trim()
            : NotUpdated;

        var bloodType = !string.IsNullOrWhiteSpace(patient.BloodType)
            ? patient.BloodType.Trim()
            : null;

        var emergencyContact = await ResolveEmergencyContactAsync(patient.Id, nowLocal, ct);
        var todayAppointment = await ResolveTodayAppointmentAsync(patient.Id, nowLocal, actor, ct);
        var (totalPastVisits, pastVisits) = await ResolvePastVisitsAsync(patient.Id, actor, ct);

        return new PatientDetailResponseDto(
            Id: patient.Id,
            PatientCode: patient.PatientCode,
            FullName: patient.FullName,
            Gender: genderCode,
            GenderLabel: genderLabel,
            Age: age,
            DateOfBirth: dateOfBirthFormatted,
            DateOfBirthIso: patient.DateOfBirth,
            Cccd: cccdDisplay,
            IdentityCardMasked: identityCardMasked,
            IsIdentityVerified: isIdentityVerified,
            Phone: phone,
            Email: email,
            BhytNumber: null,
            HealthInsuranceNumber: null,
            Address: address,
            BloodType: bloodType,
            Status: patient.Status,
            EmergencyContact: emergencyContact,
            TodayAppointment: todayAppointment,
            TotalPastVisits: totalPastVisits,
            PastVisits: pastVisits);
    }

    private async Task<PatientEmergencyContactDto?> ResolveEmergencyContactAsync(
        long patientId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var familyLinks = await TryQueryAsync(() => _db.PatientFamilyLinks
            .AsNoTracking()
            .Include(l => l.OwnerPatient)
            .Include(l => l.MemberPatient)
            .Where(l => (l.OwnerPatientId == patientId || l.MemberPatientId == patientId)
                && l.RevokedAt == null
                && (l.ValidUntil == null || l.ValidUntil >= nowLocal))
            .ToListAsync(ct));

        if (familyLinks.Count == 0)
        {
            return null;
        }

        var missingCounterpartIds = familyLinks
            .Select(l => l.OwnerPatientId == patientId
                ? (CounterpartId: l.MemberPatientId, Loaded: l.MemberPatient)
                : (CounterpartId: l.OwnerPatientId, Loaded: l.OwnerPatient))
            .Where(x => x.Loaded is null && x.CounterpartId > 0)
            .Select(x => x.CounterpartId)
            .Distinct()
            .ToList();

        Dictionary<long, Patient> fallbackPatientsById = [];
        if (missingCounterpartIds.Count > 0)
        {
            fallbackPatientsById = await _db.Patients
                .AsNoTracking()
                .Where(p => missingCounterpartIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);
        }

        var orderedLinks = familyLinks
            .OrderByDescending(l => string.Equals(l.VerificationStatus, "VERIFIED", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(l => l.VerifiedAt ?? DateTime.MinValue)
            .ToList();

        foreach (var link in orderedLinks)
        {
            var isOwner = link.OwnerPatientId == patientId;
            var counterpart = isOwner
                ? link.MemberPatient ?? fallbackPatientsById.GetValueOrDefault(link.MemberPatientId)
                : link.OwnerPatient ?? fallbackPatientsById.GetValueOrDefault(link.OwnerPatientId);

            if (counterpart is null ||
                !string.Equals(counterpart.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(counterpart.FullName))
            {
                continue;
            }

            var relationshipLabel = FormatEmergencyRelationshipLabel(
                link.Relationship,
                counterpart.Gender,
                isOwner);

            var contactPhone = !string.IsNullOrWhiteSpace(counterpart.Phone)
                ? counterpart.Phone.Trim()
                : NotUpdated;

            return new PatientEmergencyContactDto(
                Name: counterpart.FullName.Trim(),
                Relationship: relationshipLabel,
                Phone: contactPhone);
        }

        return null;
    }

    private async Task<PatientTodayAppointmentDetailDto?> ResolveTodayAppointmentAsync(
        long patientId,
        DateTime nowLocal,
        ReceptionActorContext actor,
        CancellationToken ct)
    {
        var todayStart = nowLocal.Date;
        var todayEnd = todayStart.AddDays(1);

        var candidateAppointments = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
                    .ThenInclude(sv => sv!.Department)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
                    .ThenInclude(r => r!.Department)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
                    .ThenInclude(d => d!.Staff)
                        .ThenInclude(st => st!.Department)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
                    .ThenInclude(d => d!.Specialty)
            .Where(a => a.PatientId == patientId
                && a.Slot != null
                && a.Slot.StartsAt >= todayStart
                && a.Slot.StartsAt < todayEnd
                && a.Status != AppointmentBookingRules.CancelledStatus
                && a.Status != AppointmentBookingRules.NoShowStatus)
            .ToListAsync(ct);

        if (!actor.IsSystemAdmin && actor.ActorHospitalId.HasValue)
        {
            var actorHospitalId = actor.ActorHospitalId.Value;
            candidateAppointments = candidateAppointments
                .Where(a =>
                {
                    var apptHospitalId = ResolveAppointmentHospitalId(a);
                    return !apptHospitalId.HasValue || apptHospitalId.Value == actorHospitalId;
                })
                .ToList();
        }

        var primaryAppointment = candidateAppointments
            .OrderByDescending(a => string.Equals(a.Status, AppointmentBookingRules.CheckedInStatus, StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Slot!.StartsAt)
            .ThenBy(a => a.Id)
            .FirstOrDefault();

        if (primaryAppointment is null)
        {
            return null;
        }

        var visit = await _db.Visits
            .AsNoTracking()
            .Where(v => v.AppointmentId == primaryAppointment.Id
                && v.Status != CancelledStatus
                && v.Status != NoShowStatus)
            .OrderByDescending(v => v.CheckedInAt ?? DateTime.MinValue)
            .ThenByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);

        var visitId = visit?.Id;
        var queueEntry = await _db.QueueEntries
            .AsNoTracking()
            .Where(q => (q.AppointmentId == primaryAppointment.Id
                    || (visitId.HasValue && q.VisitId == visitId.Value))
                && q.Status != CancelledStatus)
            .OrderByDescending(q => q.StartedAt ?? q.CalledAt ?? q.ArrivedAt ?? DateTime.MinValue)
            .ThenByDescending(q => q.Id)
            .FirstOrDefaultAsync(ct);

        Doctor? assignedDoctor = primaryAppointment.Slot?.Doctor;
        if (visit?.AssignedDoctorId is { } assignedDoctorId &&
            assignedDoctor?.Id != assignedDoctorId)
        {
            assignedDoctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(st => st!.Department)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == assignedDoctorId, ct)
                ?? assignedDoctor;
        }

        HospitalRoom? assignedRoom = primaryAppointment.Slot?.Room;
        if (visit?.ExaminationRoomId is { } examinationRoomId &&
            assignedRoom?.Id != examinationRoomId)
        {
            assignedRoom = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == examinationRoomId, ct)
                ?? assignedRoom;
        }

        var startsAt = primaryAppointment.Slot?.StartsAt;
        var endsAt = primaryAppointment.Slot?.EndsAt;
        var time = startsAt?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        var timeRange = startsAt.HasValue && endsAt.HasValue
            ? $"{startsAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture)} - {endsAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : time;

        var doctorName = FirstNonEmpty(
            assignedDoctor?.Staff?.FullName,
            primaryAppointment.Slot?.Doctor?.Staff?.FullName) ?? NotUpdated;

        var departmentName = FirstNonEmpty(
            assignedRoom?.Department?.Name,
            primaryAppointment.Slot?.Service?.Department?.Name,
            primaryAppointment.Slot?.Room?.Department?.Name,
            assignedDoctor?.Staff?.Department?.Name,
            assignedDoctor?.Specialty?.Name) ?? NotUpdated;

        var roomName = FirstNonEmpty(
            assignedRoom?.Name,
            primaryAppointment.Slot?.Room?.Name) ?? NotUpdated;

        var todayStatus = MapTodayAppointmentStatus(primaryAppointment.Status, visit, queueEntry);
        var appointmentCode = !string.IsNullOrWhiteSpace(primaryAppointment.AppointmentCode)
            ? primaryAppointment.AppointmentCode.Trim()
            : $"APT-{primaryAppointment.Id:D4}";

        return new PatientTodayAppointmentDetailDto(
            Id: primaryAppointment.Id,
            AppointmentCode: appointmentCode,
            Time: time,
            TimeRange: timeRange,
            DoctorName: doctorName,
            Department: departmentName,
            Room: roomName,
            RoomName: roomName,
            Status: todayStatus,
            AppointmentStatus: primaryAppointment.Status?.Trim().ToUpperInvariant() ?? "BOOKED",
            VisitId: visit?.Id,
            VisitCode: visit?.VisitCode,
            VisitStatus: visit?.Status);
    }

    private async Task<(int TotalPastVisits, IReadOnlyList<PatientPastVisitSummaryDto> PastVisits)> ResolvePastVisitsAsync(
        long patientId,
        ReceptionActorContext actor,
        CancellationToken ct)
    {
        var completedVisits = await _db.Visits
            .AsNoTracking()
            .Where(v => v.PatientId == patientId && v.Status == CompletedStatus)
            .ToListAsync(ct);

        if (completedVisits.Count == 0)
        {
            return (0, []);
        }

        var roomIds = completedVisits
            .Where(v => v.ExaminationRoomId.HasValue)
            .Select(v => v.ExaminationRoomId!.Value)
            .Distinct()
            .ToList();

        var roomsById = roomIds.Count == 0
            ? []
            : await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .Where(r => roomIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

        var doctorIds = completedVisits
            .SelectMany(v => new[] { v.AssignedDoctorId, v.CompletedByDoctorId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var doctorsById = doctorIds.Count == 0
            ? []
            : await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(st => st!.Department)
                .Include(d => d.Specialty)
                .Where(d => doctorIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct);

        var appointmentIds = completedVisits
            .Where(v => v.AppointmentId.HasValue)
            .Select(v => v.AppointmentId!.Value)
            .Distinct()
            .ToList();

        var appointmentsById = appointmentIds.Count == 0
            ? []
            : await _db.Appointments
                .AsNoTracking()
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Service)
                        .ThenInclude(sv => sv!.Department)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Room)
                        .ThenInclude(r => r!.Department)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Doctor)
                        .ThenInclude(d => d!.Staff)
                            .ThenInclude(st => st!.Department)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Doctor)
                        .ThenInclude(d => d!.Specialty)
                .Where(a => appointmentIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct);

        var scopedVisits = completedVisits;
        if (!actor.IsSystemAdmin && actor.ActorHospitalId.HasValue)
        {
            var actorHospitalId = actor.ActorHospitalId.Value;
            scopedVisits = completedVisits
                .Where(v =>
                {
                    var room = v.ExaminationRoomId.HasValue
                        ? roomsById.GetValueOrDefault(v.ExaminationRoomId.Value)
                        : null;
                    var doctorId = v.AssignedDoctorId ?? v.CompletedByDoctorId;
                    var doctor = doctorId.HasValue
                        ? doctorsById.GetValueOrDefault(doctorId.Value)
                        : null;
                    var appt = v.AppointmentId.HasValue
                        ? appointmentsById.GetValueOrDefault(v.AppointmentId.Value)
                        : null;

                    var visitHospitalId = room?.HospitalId
                        ?? room?.Department?.HospitalId
                        ?? doctor?.HospitalId
                        ?? doctor?.Staff?.HospitalId
                        ?? doctor?.Staff?.Department?.HospitalId
                        ?? ResolveAppointmentHospitalId(appt);

                    return !visitHospitalId.HasValue || visitHospitalId.Value == actorHospitalId;
                })
                .ToList();
        }

        if (scopedVisits.Count == 0)
        {
            return (0, []);
        }

        var orderedVisits = scopedVisits
            .OrderByDescending(v => v.CompletedAt ?? v.CheckedInAt ?? DateTime.MinValue)
            .ThenByDescending(v => v.Id)
            .ToList();

        var scopedVisitIds = orderedVisits.Select(v => v.Id).ToList();

        var visitDiagnoses = await TryQueryAsync(() => _db.VisitDiagnoses
            .AsNoTracking()
            .Where(d => d.VisitId.HasValue && scopedVisitIds.Contains(d.VisitId.Value))
            .ToListAsync(ct));

        var diagnosesByVisitId = visitDiagnoses
            .Where(d => d.VisitId.HasValue
                && !string.Equals(d.Status, CancelledStatus, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(d.Status, "ENTERED_IN_ERROR", StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => d.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(d => d.IsPrimary == true)
                    .ThenByDescending(d => d.ConfirmedAt ?? d.RecordedAt ?? DateTime.MinValue)
                    .ThenBy(d => d.Id)
                    .ToList());

        var medicalRecords = await TryQueryAsync(() => _db.MedicalRecords
            .AsNoTracking()
            .Where(mr => mr.VisitId.HasValue && scopedVisitIds.Contains(mr.VisitId.Value))
            .ToListAsync(ct));

        var medicalRecordIds = medicalRecords.Select(mr => mr.Id).ToList();
        var revisions = medicalRecordIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.MedicalRecordRevisions
                .AsNoTracking()
                .Where(r => r.MedicalRecordId.HasValue && medicalRecordIds.Contains(r.MedicalRecordId.Value))
                .ToListAsync(ct));

        var latestRevisionByRecordId = revisions
            .Where(r => r.MedicalRecordId.HasValue)
            .GroupBy(r => r.MedicalRecordId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(r => r.IsConfirmed == true)
                    .ThenByDescending(r => r.RevisionNo ?? 0)
                    .ThenByDescending(r => r.RecordedAt ?? DateTime.MinValue)
                    .ThenByDescending(r => r.Id)
                    .First());

        var latestRevisionByVisitId = medicalRecords
            .Where(mr => mr.VisitId.HasValue)
            .GroupBy(mr => mr.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var record = g
                        .OrderByDescending(mr => mr.FinalizedAt ?? DateTime.MinValue)
                        .ThenByDescending(mr => mr.Id)
                        .First();
                    return latestRevisionByRecordId.GetValueOrDefault(record.Id);
                });

        var summaries = new List<PatientPastVisitSummaryDto>(orderedVisits.Count);
        foreach (var v in orderedVisits)
        {
            var room = v.ExaminationRoomId.HasValue
                ? roomsById.GetValueOrDefault(v.ExaminationRoomId.Value)
                : null;
            var doctorId = v.AssignedDoctorId ?? v.CompletedByDoctorId;
            var doctor = doctorId.HasValue
                ? doctorsById.GetValueOrDefault(doctorId.Value)
                : null;
            var appt = v.AppointmentId.HasValue
                ? appointmentsById.GetValueOrDefault(v.AppointmentId.Value)
                : null;

            var visitedAt = v.CompletedAt ?? v.CheckedInAt;
            var dateFormatted = visitedAt.HasValue
                ? visitedAt.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : NotUpdated;

            var departmentName = FirstNonEmpty(
                room?.Department?.Name,
                appt?.Slot?.Service?.Department?.Name,
                appt?.Slot?.Room?.Department?.Name,
                doctor?.Staff?.Department?.Name,
                doctor?.Specialty?.Name,
                appt?.Slot?.Doctor?.Staff?.Department?.Name,
                appt?.Slot?.Doctor?.Specialty?.Name) ?? NotUpdated;

            var doctorName = FirstNonEmpty(
                doctor?.Staff?.FullName,
                appt?.Slot?.Doctor?.Staff?.FullName) ?? NotUpdated;

            string? diagnosisText = null;
            if (diagnosesByVisitId.TryGetValue(v.Id, out var visitDiagList))
            {
                foreach (var diag in visitDiagList)
                {
                    diagnosisText = FirstNonEmpty(diag.ClinicalDiagnosis, diag.NameSnapshot, diag.CodeSnapshot);
                    if (diagnosisText is not null)
                    {
                        break;
                    }
                }
            }

            if (diagnosisText is null &&
                latestRevisionByVisitId.TryGetValue(v.Id, out var latestRevision) &&
                latestRevision is not null)
            {
                diagnosisText = FirstNonEmpty(latestRevision.ClinicalConclusion);
            }

            diagnosisText ??= FirstNonEmpty(v.ReasonForVisit, appt?.ReasonForVisit) ?? NotUpdated;

            var visitCode = !string.IsNullOrWhiteSpace(v.VisitCode)
                ? v.VisitCode.Trim()
                : $"VIS-{v.Id:D4}";

            summaries.Add(new PatientPastVisitSummaryDto(
                Id: v.Id,
                VisitCode: visitCode,
                Date: dateFormatted,
                VisitedAt: visitedAt,
                Department: departmentName,
                DoctorName: doctorName,
                Diagnosis: diagnosisText));
        }

        return (orderedVisits.Count, summaries);
    }

    private sealed record ReceptionActorContext(
        bool IsSystemAdmin,
        long? ActorHospitalId,
        long? ActorStaffId,
        string? ActorStaffName);

    private async Task<ReceptionActorContext> EnsureActorAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản người dùng không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản người dùng đang bị tạm khóa.");
        }

        var actorRoleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var actorRoleCodes = await _db.Roles
            .AsNoTracking()
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        var isSystemAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase));
        var isHospitalAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, "HOSPITAL_ADMIN", StringComparison.OrdinalIgnoreCase));
        var isReception = actorRoleCodes.Any(c =>
            string.Equals(c, "RECEPTION", StringComparison.OrdinalIgnoreCase));

        if (!isSystemAdmin && !isHospitalAdmin && !isReception)
        {
            throw new ForbiddenException(
                "Người dùng không có quyền xem chi tiết hồ sơ bệnh nhân tại quầy tiếp đón.");
        }

        if (!isSystemAdmin && actorRoleIds.Count > 0)
        {
            var rolePermissionIds = await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => actorRoleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);

            if (rolePermissionIds.Count > 0)
            {
                var permissionCodes = await _db.Permissions
                    .AsNoTracking()
                    .Where(p => rolePermissionIds.Contains(p.Id))
                    .Select(p => p.Code)
                    .ToListAsync(ct);

                if (isReception && !isHospitalAdmin)
                {
                    var hasReceptionPermission = permissionCodes.Any(c =>
                        ReceptionAllowedPermissions.Any(allowed =>
                            string.Equals(c, allowed, StringComparison.OrdinalIgnoreCase)));
                    if (!hasReceptionPermission)
                    {
                        throw new ForbiddenException(
                            "Vai trò tiếp đón chưa được cấp quyền xem hồ sơ bệnh nhân (PATIENT_PROFILE_READ).");
                    }
                }
                else if (isHospitalAdmin)
                {
                    var hasHospitalAdminPermission = permissionCodes.Any(c =>
                        HospitalAdminAllowedPermissions.Any(allowed =>
                            string.Equals(c, allowed, StringComparison.OrdinalIgnoreCase)));
                    if (!hasHospitalAdminPermission)
                    {
                        throw new ForbiddenException(
                            "Vai trò hiện tại chưa được cấp quyền xem hồ sơ bệnh nhân (PATIENT_PROFILE_READ).");
                    }
                }
            }
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của người dùng không ở trạng thái hoạt động.");
        }

        if (!isSystemAdmin)
        {
            if (actorStaff is null || !actorStaff.HospitalId.HasValue)
            {
                throw new ForbiddenException("Tài khoản nhân sự chưa được gán cơ sở y tế.");
            }

            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", actorStaff.HospitalId.Value);

            if (hospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế của nhân sự hiện đang ngừng hoạt động.");
            }
        }

        return new ReceptionActorContext(
            isSystemAdmin,
            actorStaff?.HospitalId,
            actorStaff?.Id,
            actorStaff?.FullName);
    }

    private static (string Trimmed, long? Id, string? Code) ParsePatientIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã hoặc ID bệnh nhân không hợp lệ.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed) ||
            trimmed.Length > MaxPatientIdentifierLength ||
            trimmed[0] is '-' or '_' ||
            !trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
        {
            throw new ValidationException("Mã hoặc ID bệnh nhân không hợp lệ.");
        }

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                throw new ValidationException("Mã hoặc ID bệnh nhân không hợp lệ.");
            }

            return (trimmed, id, null);
        }

        return (trimmed, null, trimmed.ToUpperInvariant());
    }

    private static (string Code, string Label) NormalizeGender(string? gender)
    {
        var normalized = gender?.Trim().ToUpperInvariant();
        return normalized switch
        {
            "MALE" or "NAM" => ("MALE", "Nam"),
            "FEMALE" or "NỮ" or "NU" => ("FEMALE", "Nữ"),
            _ => ("OTHER", "Khác")
        };
    }

    private static string FormatEmergencyRelationshipLabel(
        string? relationship,
        string? counterpartGender,
        bool isPatientOwner)
    {
        var relKey = relationship?.Trim().ToUpperInvariant();
        var isFemale = string.Equals(counterpartGender?.Trim(), "FEMALE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(counterpartGender?.Trim(), "NỮ", StringComparison.OrdinalIgnoreCase);

        if (isPatientOwner)
        {
            return relKey switch
            {
                "SPOUSE" => isFemale ? "Vợ" : "Chồng",
                "WIFE" => "Vợ",
                "HUSBAND" => "Chồng",
                "CHILD" => isFemale ? "Con gái" : "Con trai",
                "SON" => "Con trai",
                "DAUGHTER" => "Con gái",
                "PARENT" => isFemale ? "Mẹ ruột" : "Bố ruột",
                "FATHER" => "Bố ruột",
                "MOTHER" => "Mẹ ruột",
                _ => "Người thân"
            };
        }

        return relKey switch
        {
            "SPOUSE" or "WIFE" or "HUSBAND" => isFemale ? "Vợ" : "Chồng",
            "CHILD" or "SON" or "DAUGHTER" => isFemale ? "Mẹ ruột" : "Bố ruột",
            "PARENT" or "FATHER" or "MOTHER" => isFemale ? "Con gái" : "Con trai",
            _ => "Người thân"
        };
    }

    private static string MapTodayAppointmentStatus(
        string? appointmentStatus,
        Visit? visit,
        QueueEntry? queueEntry)
    {
        if (string.Equals(appointmentStatus, CompletedStatus, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(visit?.Status, CompletedStatus, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(queueEntry?.Status, CompletedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return "DONE";
        }

        if ((visit?.Status is { } visitStatus && InProgressVisitStatuses.Contains(visitStatus)) ||
            (queueEntry?.Status is { } queueStatus && InProgressQueueStatuses.Contains(queueStatus)))
        {
            return "IN_PROGRESS";
        }

        if (string.Equals(appointmentStatus, AppointmentBookingRules.CheckedInStatus, StringComparison.OrdinalIgnoreCase) ||
            visit is not null ||
            string.Equals(queueEntry?.PresenceStatus, "PRESENT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(queueEntry?.Status, "WAITING", StringComparison.OrdinalIgnoreCase))
        {
            return "ARRIVED";
        }

        return "BOOKED";
    }

    private static long? ResolveAppointmentHospitalId(Appointment? appointment)
    {
        var slot = appointment?.Slot;
        if (slot is null)
        {
            return null;
        }

        return slot.Service?.HospitalId
            ?? slot.Service?.Department?.HospitalId
            ?? slot.Room?.HospitalId
            ?? slot.Room?.Department?.HospitalId
            ?? slot.Doctor?.HospitalId
            ?? slot.Doctor?.Staff?.HospitalId
            ?? slot.Doctor?.Staff?.Department?.HospitalId;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    private static async Task<List<T>> TryQueryAsync<T>(Func<Task<List<T>>> query)
    {
        try
        {
            return await query();
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }
}
