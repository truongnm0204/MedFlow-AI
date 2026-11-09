using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Consultation;

public sealed class MedicalHistoryService : IMedicalHistoryService
{
    private const string DoctorRoleCode = "DOCTOR";
    private const string AuditActionMedicalHistoryViewed = "MEDICAL_HISTORY_VIEWED";
    private const string DefaultAllergiesSummary = "Chưa ghi nhận tiền sử dị ứng";

    private static readonly HashSet<string> AllowedFilterStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPLETED",
        "IN_PROGRESS",
        "IN_CONSULTATION",
        "WAITING",
        "CALLED",
        "AWAITING_RESULTS",
        "FINALIZED",
        "DRAFT",
        "CHECKED_IN",
    };

    private static readonly HashSet<string> RequiredDoctorPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "MEDICAL_RECORD_WRITE",
        "PATIENT_PROFILE_READ",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public MedicalHistoryService(
        IApplicationDbContext db,
        TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<PatientMedicalHistoryResponseDto> GetByPatientAsync(
        long actorUserId,
        string patientIdOrCode,
        GetPatientMedicalHistoryRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        request ??= new GetPatientMedicalHistoryRequest();
        var (patientId, patientCode, trimmedPatientIdentifier) = ParseEntityIdentifier(patientIdOrCode, "bệnh nhân");
        ValidateFilters(request);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (hospital, actorDoctor) = await EnsureDoctorAccessAsync(actorUserId, nowUtc, ct);

        Patient? patient;
        if (patientId.HasValue)
        {
            patient = await _db.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == patientId.Value, ct);
        }
        else
        {
            patient = await _db.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PatientCode.ToUpper() == patientCode, ct);
        }

        if (patient is null)
        {
            throw new NotFoundException("Patient", trimmedPatientIdentifier);
        }

        if (!string.Equals(patient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động.");
        }

        Visit? currentVisit = null;
        if (!string.IsNullOrWhiteSpace(request.VisitIdOrCode))
        {
            var (visitId, visitCode, trimmedVisitIdentifier) = ParseEntityIdentifier(request.VisitIdOrCode, "lượt khám");
            currentVisit = await FindVisitAsync(visitId, visitCode, ct)
                ?? throw new NotFoundException("Visit", trimmedVisitIdentifier);

            Appointment? currentVisitAppointment = null;
            if (currentVisit.AppointmentId.HasValue)
            {
                currentVisitAppointment = await _db.Appointments
                    .AsNoTracking()
                    .Include(a => a.Slot)
                        .ThenInclude(s => s!.Service)
                    .Include(a => a.Slot)
                        .ThenInclude(s => s!.Doctor)
                    .Include(a => a.Slot)
                        .ThenInclude(s => s!.Room)
                    .FirstOrDefaultAsync(a => a.Id == currentVisit.AppointmentId.Value, ct);
            }

            var resolvedVisitPatientId = currentVisit.PatientId ?? currentVisitAppointment?.PatientId;
            if (resolvedVisitPatientId != patient.Id)
            {
                throw new ValidationException("Lượt khám hiện tại không thuộc về bệnh nhân được yêu cầu.");
            }

            await EnsureVisitWithinHospitalAndActiveAsync(currentVisit, currentVisitAppointment, hospital.Id, ct);
        }

        return await BuildMedicalHistoryResponseAsync(
            actorUserId,
            hospital,
            actorDoctor,
            patient,
            currentVisit,
            request,
            ipAddress,
            nowUtc,
            ct);
    }

    public async Task<PatientMedicalHistoryResponseDto> GetByVisitAsync(
        long actorUserId,
        string visitIdOrCode,
        GetPatientMedicalHistoryRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        request ??= new GetPatientMedicalHistoryRequest();
        var (visitId, visitCode, trimmedVisitIdentifier) = ParseEntityIdentifier(visitIdOrCode, "lượt khám");
        ValidateFilters(request);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (hospital, actorDoctor) = await EnsureDoctorAccessAsync(actorUserId, nowUtc, ct);

        var visit = await FindVisitAsync(visitId, visitCode, ct)
            ?? throw new NotFoundException("Visit", trimmedVisitIdentifier);

        Appointment? appointment = null;
        if (visit.AppointmentId.HasValue)
        {
            appointment = await _db.Appointments
                .AsNoTracking()
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Service)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Doctor)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Room)
                .FirstOrDefaultAsync(a => a.Id == visit.AppointmentId.Value, ct);
        }

        await EnsureVisitWithinHospitalAndActiveAsync(visit, appointment, hospital.Id, ct);

        var patientId = visit.PatientId ?? appointment?.PatientId;
        if (!patientId.HasValue)
        {
            throw new NotFoundException("Patient", 0);
        }

        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == patientId.Value, ct)
            ?? throw new NotFoundException("Patient", patientId.Value);

        if (!string.Equals(patient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động.");
        }

        return await BuildMedicalHistoryResponseAsync(
            actorUserId,
            hospital,
            actorDoctor,
            patient,
            visit,
            request,
            ipAddress,
            nowUtc,
            ct);
    }

    private async Task<PatientMedicalHistoryResponseDto> BuildMedicalHistoryResponseAsync(
        long actorUserId,
        Hospital hospital,
        Doctor actorDoctor,
        Patient patient,
        Visit? currentVisit,
        GetPatientMedicalHistoryRequest request,
        string? ipAddress,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == hospital.Id)
            .ToListAsync(ct);
        var departmentsById = departments.ToDictionary(d => d.Id);

        var rooms = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId == hospital.Id)
            .ToListAsync(ct);
        var roomsById = rooms.ToDictionary(r => r.Id);

        var specialties = await _db.Specialties
            .AsNoTracking()
            .ToListAsync(ct);
        var specialtiesById = specialties.ToDictionary(s => s.Id);

        var doctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .Include(d => d.Specialty)
            .ToListAsync(ct);
        var doctorsById = doctors.ToDictionary(d => d.Id);

        var allStaff = await _db.Staff
            .AsNoTracking()
            .ToListAsync(ct);
        var staffById = allStaff.ToDictionary(s => s.Id);

        var services = await _db.Services
            .AsNoTracking()
            .Where(s => s.HospitalId == hospital.Id)
            .ToListAsync(ct);
        var servicesById = services.ToDictionary(s => s.Id);

        var medicines = await TryQueryAsync(
            () => _db.Medicines
                .AsNoTracking()
                .Where(m => !m.HospitalId.HasValue || m.HospitalId == hospital.Id)
                .ToListAsync(ct));
        var medicinesById = medicines.ToDictionary(m => m.Id);

        var servicePoints = await TryQueryAsync(
            () => _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp => !sp.HospitalId.HasValue || sp.HospitalId == hospital.Id)
                .ToListAsync(ct));
        var servicePointsById = servicePoints.ToDictionary(sp => sp.Id);

        var patientAppointments = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
            .Where(a => a.PatientId == patient.Id)
            .ToListAsync(ct);
        var appointmentsById = patientAppointments.ToDictionary(a => a.Id);

        var patientAppointmentIds = patientAppointments.Select(a => a.Id).ToHashSet();

        var rawVisits = await _db.Visits
            .AsNoTracking()
            .Where(v => v.PatientId == patient.Id || (v.AppointmentId.HasValue && patientAppointmentIds.Contains(v.AppointmentId.Value)))
            .ToListAsync(ct);

        if (currentVisit is not null && rawVisits.All(v => v.Id != currentVisit.Id))
        {
            rawVisits.Add(currentVisit);
        }

        var missingAppointmentIds = rawVisits
            .Where(v => v.AppointmentId.HasValue && !appointmentsById.ContainsKey(v.AppointmentId.Value))
            .Select(v => v.AppointmentId!.Value)
            .Distinct()
            .ToList();

        if (missingAppointmentIds.Count > 0)
        {
            var extraAppointments = await _db.Appointments
                .AsNoTracking()
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Service)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Doctor)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Room)
                .Where(a => missingAppointmentIds.Contains(a.Id))
                .ToListAsync(ct);

            foreach (var extra in extraAppointments)
            {
                appointmentsById[extra.Id] = extra;
            }
        }

        // Filter visits to current hospital and non-cancelled status
        var scopedVisits = new List<Visit>();
        var sawForeignHospitalVisit = false;

        foreach (var v in rawVisits)
        {
            appointmentsById.TryGetValue(v.AppointmentId ?? 0, out var appt);
            var visitHospitalIds = GetVisitHospitalIds(v, appt, roomsById, doctorsById, staffById);
            if (visitHospitalIds.Any(id => id != hospital.Id))
            {
                sawForeignHospitalVisit = true;
                continue;
            }

            var normStatus = AppointmentBookingRules.NormalizeStatusKey(v.Status);
            if (normStatus is "CANCELLED" or "NO_SHOW")
            {
                continue;
            }

            scopedVisits.Add(v);
        }

        if (scopedVisits.Count == 0 && sawForeignHospitalVisit)
        {
            throw new ForbiddenException("Bạn không có quyền truy cập lịch sử khám của cơ sở y tế khác.");
        }

        var scopedVisitIds = scopedVisits.Select(v => v.Id).ToList();

        var medicalRecords = scopedVisitIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.MedicalRecords
                .AsNoTracking()
                .Where(r => r.VisitId.HasValue && scopedVisitIds.Contains(r.VisitId.Value))
                .ToListAsync(ct));

        var queueEntries = scopedVisitIds.Count == 0 && patientAppointmentIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.QueueEntries
                .AsNoTracking()
                .Where(qe =>
                    (qe.VisitId.HasValue && scopedVisitIds.Contains(qe.VisitId.Value)) ||
                    (qe.AppointmentId.HasValue && patientAppointmentIds.Contains(qe.AppointmentId.Value)))
                .ToListAsync(ct));

        var medicalRecordsByVisitId = medicalRecords
            .Where(r => r.VisitId.HasValue)
            .GroupBy(r => r.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.FinalizedAt ?? DateTime.MinValue).ThenByDescending(r => r.Id).First());

        var queueEntriesByVisitId = queueEntries
            .Where(qe => qe.VisitId.HasValue)
            .GroupBy(qe => qe.VisitId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Layer-2 Care Relationship Ownership Check
        if (currentVisit is not null)
        {
            appointmentsById.TryGetValue(currentVisit.AppointmentId ?? 0, out var currentAppt);
            medicalRecordsByVisitId.TryGetValue(currentVisit.Id, out var currentRecord);
            queueEntriesByVisitId.TryGetValue(currentVisit.Id, out var currentQueueEntries);

            if (!IsVisitAssignedToDoctor(
                    currentVisit,
                    currentAppt,
                    currentRecord,
                    currentQueueEntries,
                    servicePointsById,
                    actorDoctor.Id))
            {
                throw new ForbiddenException("Bác sĩ chỉ được xem lịch sử khám của bệnh nhân thuộc phạm vi phụ trách lâm sàng.");
            }
        }
        else
        {
            var hasCareRelationship = scopedVisits.Any(v =>
            {
                appointmentsById.TryGetValue(v.AppointmentId ?? 0, out var appt);
                medicalRecordsByVisitId.TryGetValue(v.Id, out var record);
                queueEntriesByVisitId.TryGetValue(v.Id, out var qEntries);
                return IsVisitAssignedToDoctor(v, appt, record, qEntries, servicePointsById, actorDoctor.Id);
            });

            if (!hasCareRelationship)
            {
                hasCareRelationship = patientAppointments.Any(a =>
                {
                    var apptStatus = AppointmentBookingRules.NormalizeStatusKey(a.Status);
                    if (apptStatus is "CANCELLED" or "NO_SHOW")
                    {
                        return false;
                    }

                    return a.Slot?.DoctorId == actorDoctor.Id;
                });
            }

            if (!hasCareRelationship)
            {
                hasCareRelationship = queueEntries.Any(qe =>
                {
                    var qeStatus = AppointmentBookingRules.NormalizeStatusKey(qe.Status);
                    if (qeStatus is "CANCELLED" or "NO_SHOW")
                    {
                        return false;
                    }

                    return (qe.ServicePointId.HasValue &&
                            servicePointsById.TryGetValue(qe.ServicePointId.Value, out var sp1) &&
                            sp1.DoctorId == actorDoctor.Id) ||
                           (qe.CurrentServicePointId.HasValue &&
                            servicePointsById.TryGetValue(qe.CurrentServicePointId.Value, out var sp2) &&
                            sp2.DoctorId == actorDoctor.Id);
                });
            }

            if (!hasCareRelationship)
            {
                throw new ForbiddenException("Bác sĩ chỉ được xem lịch sử khám của bệnh nhân thuộc phạm vi phụ trách lâm sàng.");
            }
        }

        // Load clinical child entities for scopedVisits
        var recordIds = medicalRecords.Select(r => r.Id).ToList();
        var revisions = recordIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.MedicalRecordRevisions
                .AsNoTracking()
                .Where(rev => rev.MedicalRecordId.HasValue && recordIds.Contains(rev.MedicalRecordId.Value))
                .ToListAsync(ct));

        var revisionsByRecordId = revisions
            .Where(rev => rev.MedicalRecordId.HasValue)
            .GroupBy(rev => rev.MedicalRecordId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.RevisionNo ?? 0)
                      .ThenByDescending(r => r.RecordedAt ?? DateTime.MinValue)
                      .ThenByDescending(r => r.Id)
                      .ToList());

        var diagnoses = scopedVisitIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.VisitDiagnoses
                .AsNoTracking()
                .Where(d => d.VisitId.HasValue && scopedVisitIds.Contains(d.VisitId.Value))
                .ToListAsync(ct));

        var icd10Ids = diagnoses
            .Where(d => d.Icd10CodeId.HasValue)
            .Select(d => d.Icd10CodeId!.Value)
            .Distinct()
            .ToList();

        var icd10Codes = icd10Ids.Count == 0
            ? []
            : await TryQueryAsync(() => _db.Icd10Codes
                .AsNoTracking()
                .Where(c => icd10Ids.Contains(c.Id))
                .ToListAsync(ct));
        var icd10ById = icd10Codes.ToDictionary(c => c.Id);

        var diagnosesByVisitId = diagnoses
            .Where(d => d.VisitId.HasValue)
            .GroupBy(d => d.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(d => d.IsPrimary == true)
                      .ThenBy(d => d.Id)
                      .ToList());

        var addenda = scopedVisitIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.ClinicalAddenda
                .AsNoTracking()
                .Where(a => a.VisitId.HasValue && scopedVisitIds.Contains(a.VisitId.Value))
                .ToListAsync(ct));

        var addendaByVisitId = addenda
            .Where(a => a.VisitId.HasValue)
            .GroupBy(a => a.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.RecordedAt ?? DateTime.MinValue)
                      .ThenByDescending(a => a.Id)
                      .ToList());

        var diagnosticOrders = scopedVisitIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.DiagnosticOrders
                .AsNoTracking()
                .Where(o => o.VisitId.HasValue && scopedVisitIds.Contains(o.VisitId.Value))
                .ToListAsync(ct));

        var orderIds = diagnosticOrders.Select(o => o.Id).ToList();
        var diagnosticResults = orderIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.DiagnosticResults
                .AsNoTracking()
                .Where(dr => dr.DiagnosticOrderId.HasValue && orderIds.Contains(dr.DiagnosticOrderId.Value))
                .ToListAsync(ct));

        var resultsByOrderId = diagnosticResults
            .Where(dr => dr.DiagnosticOrderId.HasValue)
            .GroupBy(dr => dr.DiagnosticOrderId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.FinalizedAt ?? DateTime.MinValue)
                      .ThenByDescending(r => r.Id)
                      .First());

        var resultIds = diagnosticResults.Select(r => r.Id).ToList();
        var resultVersions = resultIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.DiagnosticResultVersions
                .AsNoTracking()
                .Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value))
                .ToListAsync(ct));

        var latestVersionByResultId = resultVersions
            .Where(v => v.DiagnosticResultId.HasValue)
            .GroupBy(v => v.DiagnosticResultId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(v => v.IsFinalized == true)
                      .ThenByDescending(v => v.VersionNo ?? 0)
                      .ThenByDescending(v => v.VerifiedAt ?? v.EnteredAt ?? DateTime.MinValue)
                      .ThenByDescending(v => v.Id)
                      .First());

        var ordersByVisitId = diagnosticOrders
            .Where(o => o.VisitId.HasValue)
            .GroupBy(o => o.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(o => o.OrderedAt ?? DateTime.MinValue)
                      .ThenBy(o => o.Id)
                      .ToList());

        var prescriptions = scopedVisitIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.Prescriptions
                .AsNoTracking()
                .Where(p => p.VisitId.HasValue && scopedVisitIds.Contains(p.VisitId.Value))
                .ToListAsync(ct));

        var prescriptionIds = prescriptions.Select(p => p.Id).ToList();
        var prescriptionItems = prescriptionIds.Count == 0
            ? []
            : await TryQueryAsync(() => _db.PrescriptionItems
                .AsNoTracking()
                .Where(pi => pi.PrescriptionId.HasValue && prescriptionIds.Contains(pi.PrescriptionId.Value))
                .ToListAsync(ct));

        var itemsByPrescriptionId = prescriptionItems
            .Where(pi => pi.PrescriptionId.HasValue)
            .GroupBy(pi => pi.PrescriptionId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(pi => pi.Id).ToList());

        var prescriptionsByVisitId = prescriptions
            .Where(p => p.VisitId.HasValue)
            .GroupBy(p => p.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(p => p.IssuedAt ?? p.CreatedAt ?? DateTime.MinValue)
                      .ThenByDescending(p => p.Id)
                      .ToList());

        // Map all scoped visits to DTOs (ordered newest first)
        var orderedScopedVisits = scopedVisits
            .OrderByDescending(v => v.CheckedInAt ?? v.CompletedAt ?? DateTime.MinValue)
            .ThenByDescending(v => v.Id)
            .ToList();

        var allVisitEntries = new List<MedicalHistoryVisitEntryDto>(orderedScopedVisits.Count);

        foreach (var v in orderedScopedVisits)
        {
            appointmentsById.TryGetValue(v.AppointmentId ?? 0, out var appt);
            medicalRecordsByVisitId.TryGetValue(v.Id, out var medRecord);
            queueEntriesByVisitId.TryGetValue(v.Id, out var qEntries);

            var doctorId = v.AssignedDoctorId ?? v.CompletedByDoctorId ?? appt?.Slot?.DoctorId ?? medRecord?.CreatedByDoctorId;
            Doctor? visitDoctor = null;
            if (doctorId.HasValue)
            {
                doctorsById.TryGetValue(doctorId.Value, out visitDoctor);
            }

            var roomId = v.ExaminationRoomId ?? appt?.Slot?.RoomId;
            HospitalRoom? visitRoom = null;
            if (roomId.HasValue)
            {
                roomsById.TryGetValue(roomId.Value, out visitRoom);
            }

            var departmentId = visitRoom?.DepartmentId
                ?? appt?.Slot?.Service?.DepartmentId
                ?? visitDoctor?.Staff?.DepartmentId;
            Department? visitDepartment = null;
            if (departmentId.HasValue)
            {
                departmentsById.TryGetValue(departmentId.Value, out visitDepartment);
            }

            var specialtyId = visitDoctor?.SpecialtyId ?? appt?.Slot?.Service?.SpecialtyId;
            Specialty? visitSpecialty = null;
            if (specialtyId.HasValue)
            {
                specialtiesById.TryGetValue(specialtyId.Value, out visitSpecialty);
            }
            visitSpecialty ??= visitDoctor?.Specialty;

            var visitTimestamp = v.CheckedInAt ?? v.CompletedAt ?? appt?.Slot?.StartsAt;
            var visitDate = visitTimestamp.HasValue
                ? DateOnly.FromDateTime(visitTimestamp.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : string.Empty;

            var visitStatus = AppointmentBookingRules.NormalizeStatusKey(v.Status);
            if (string.IsNullOrEmpty(visitStatus))
            {
                visitStatus = "CHECKED_IN";
            }

            var visitType = v.AppointmentId.HasValue ? "SCHEDULED" : "WALK_IN";
            var isAssignedToCurrentDoctor = IsVisitAssignedToDoctor(
                v,
                appt,
                medRecord,
                qEntries,
                servicePointsById,
                actorDoctor.Id);

            // Map MedicalRecord
            MedicalHistoryRecordDto? recordDto = null;
            if (medRecord is not null)
            {
                revisionsByRecordId.TryGetValue(medRecord.Id, out var recordRevisions);
                recordRevisions ??= [];

                var latestRevision = recordRevisions.FirstOrDefault();
                var editedByDoctorName = ResolveDoctorName(
                    latestRevision?.EditedByDoctorId ?? medRecord.CreatedByDoctorId,
                    doctorsById);

                var revisionSummaries = recordRevisions
                    .Select(rev => new MedicalHistoryRevisionSummaryDto(
                        RevisionId: rev.Id,
                        RevisionNo: rev.RevisionNo ?? 1,
                        RecordedAt: rev.RecordedAt,
                        EditedByDoctorName: ResolveDoctorName(rev.EditedByDoctorId, doctorsById),
                        IsConfirmed: rev.IsConfirmed == true,
                        AmendmentReason: rev.AmendmentReason,
                        ClinicalConclusion: rev.ClinicalConclusion))
                    .ToList();

                addendaByVisitId.TryGetValue(v.Id, out var visitAddenda);
                visitAddenda ??= [];

                var addendaDtos = visitAddenda
                    .Select(a => new MedicalHistoryAddendumDto(
                        AddendumId: a.Id,
                        RecordType: a.RecordType ?? "MEDICAL_RECORD",
                        TargetRecordId: a.TargetRecordId,
                        Reason: a.Reason,
                        Note: ParseAddendumNote(a.CorrectedContent),
                        CorrectedContentJson: a.CorrectedContent,
                        AuthorDoctorName: ResolveDoctorName(a.AuthorDoctorId, doctorsById),
                        RecordedAt: a.RecordedAt))
                    .ToList();

                recordDto = new MedicalHistoryRecordDto(
                    MedicalRecordId: medRecord.Id,
                    Status: AppointmentBookingRules.NormalizeStatusKey(medRecord.Status) is { Length: > 0 } recStatus
                        ? recStatus
                        : "DRAFT",
                    FinalizedAt: medRecord.FinalizedAt,
                    LatestRevisionNo: latestRevision?.RevisionNo ?? (recordRevisions.Count > 0 ? 1 : 0),
                    IsConfirmed: latestRevision?.IsConfirmed == true,
                    RecordedAt: latestRevision?.RecordedAt ?? medRecord.FinalizedAt,
                    EditedByDoctorName: editedByDoctorName,
                    PresentingComplaint: latestRevision?.PresentingComplaint,
                    SymptomHistory: latestRevision?.SymptomHistory,
                    RelevantHistoryAllergies: latestRevision?.RelevantHistoryAllergies,
                    ClinicalFindings: latestRevision?.ClinicalFindings,
                    ClinicalConclusion: latestRevision?.ClinicalConclusion,
                    AmendmentReason: latestRevision?.AmendmentReason,
                    VitalSigns: ParseVitalSigns(latestRevision?.VitalSigns),
                    RevisionCount: recordRevisions.Count,
                    Revisions: revisionSummaries,
                    Addenda: addendaDtos);
            }

            // Map Diagnoses
            diagnosesByVisitId.TryGetValue(v.Id, out var visitDiagnoses);
            visitDiagnoses ??= [];

            var diagnosisDtos = visitDiagnoses
                .Select(d =>
                {
                    Icd10Code? icd = null;
                    if (d.Icd10CodeId.HasValue)
                    {
                        icd10ById.TryGetValue(d.Icd10CodeId.Value, out icd);
                    }

                    var code = !string.IsNullOrWhiteSpace(d.CodeSnapshot)
                        ? d.CodeSnapshot.Trim()
                        : icd?.Code?.Trim() ?? string.Empty;
                    var nameVi = !string.IsNullOrWhiteSpace(d.NameSnapshot)
                        ? d.NameSnapshot.Trim()
                        : icd?.NameVi?.Trim() ?? string.Empty;

                    return new MedicalHistoryDiagnosisDto(
                        DiagnosisId: d.Id,
                        Icd10CodeId: d.Icd10CodeId,
                        Icd10Code: code,
                        Icd10NameVi: nameVi,
                        CatalogVersion: d.CatalogVersion ?? icd?.CatalogVersion,
                        ClinicalDiagnosis: d.ClinicalDiagnosis,
                        IsPrimary: d.IsPrimary == true,
                        Status: AppointmentBookingRules.NormalizeStatusKey(d.Status) is { Length: > 0 } dxStatus
                            ? dxStatus
                            : "CONFIRMED",
                        ConfirmedAt: d.ConfirmedAt,
                        RecordedAt: d.RecordedAt,
                        RecordedByDoctorName: ResolveDoctorName(d.RecordedByDoctorId, doctorsById));
                })
                .ToList();

            // Map DiagnosticOrders
            ordersByVisitId.TryGetValue(v.Id, out var visitOrders);
            visitOrders ??= [];

            var orderDtos = visitOrders
                .Select(o =>
                {
                    Service? svc = null;
                    if (o.ServiceId.HasValue)
                    {
                        servicesById.TryGetValue(o.ServiceId.Value, out svc);
                    }

                    resultsByOrderId.TryGetValue(o.Id, out var result);
                    DiagnosticResultVersion? version = null;
                    if (result is not null)
                    {
                        latestVersionByResultId.TryGetValue(result.Id, out version);
                    }

                    var resultStatus = result is not null
                        ? AppointmentBookingRules.NormalizeStatusKey(result.Status)
                        : null;
                    var isResultFinalized = version?.IsFinalized == true ||
                                            string.Equals(resultStatus, "FINALIZED", StringComparison.OrdinalIgnoreCase);

                    return new MedicalHistoryDiagnosticOrderDto(
                        OrderId: o.Id,
                        OrderCode: o.OrderCode ?? $"DIA-{o.Id}",
                        ServiceId: o.ServiceId,
                        ServiceCode: svc?.ServiceCode,
                        ServiceName: svc?.Name ?? "Chỉ định cận lâm sàng",
                        Priority: AppointmentBookingRules.NormalizeStatusKey(o.Priority) is { Length: > 0 } prio
                            ? prio
                            : "NORMAL",
                        Status: AppointmentBookingRules.NormalizeStatusKey(o.Status) is { Length: > 0 } ordStatus
                            ? ordStatus
                            : "ORDERED",
                        RequiredForConclusion: o.RequiredForConclusion == true,
                        Instruction: o.Instruction,
                        OrderedAt: o.OrderedAt,
                        ConfirmedAt: o.ConfirmedAt,
                        ResultStatus: resultStatus,
                        ResultFinalizedAt: result?.FinalizedAt ?? version?.VerifiedAt,
                        ResultVersionNo: version?.VersionNo,
                        ResultText: version?.ResultText,
                        Conclusion: version?.Conclusion,
                        ReportFileUrl: version?.ReportFileUrl,
                        IsResultFinalized: isResultFinalized);
                })
                .ToList();

            // Map Prescriptions
            prescriptionsByVisitId.TryGetValue(v.Id, out var visitPrescriptions);
            visitPrescriptions ??= [];

            var prescriptionDtos = visitPrescriptions
                .Select(p =>
                {
                    itemsByPrescriptionId.TryGetValue(p.Id, out var pItems);
                    pItems ??= [];

                    var itemDtos = pItems
                        .Select(pi =>
                        {
                            Medicine? med = null;
                            if (pi.MedicineId.HasValue)
                            {
                                medicinesById.TryGetValue(pi.MedicineId.Value, out med);
                            }

                            return new MedicalHistoryPrescriptionItemDto(
                                ItemId: pi.Id,
                                MedicineId: pi.MedicineId,
                                MedicineCode: med?.MedicineCode,
                                MedicineName: med?.Name ?? "Thuốc kê đơn",
                                ActiveIngredient: med?.ActiveIngredient,
                                Strength: med?.Strength,
                                DosageForm: med?.DosageForm,
                                DispensingUnit: med?.DispensingUnit ?? pi.SingleDoseUnit,
                                PrescribedQuantity: pi.PrescribedQuantity,
                                SingleDoseQuantity: pi.SingleDoseQuantity,
                                SingleDoseUnit: pi.SingleDoseUnit,
                                DoseScheduleText: pi.DoseScheduleText,
                                Frequency: pi.Frequency,
                                DurationDays: pi.DurationDays,
                                AdministrationRoute: pi.AdministrationRoute,
                                Instructions: pi.Instructions);
                        })
                        .ToList();

                    return new MedicalHistoryPrescriptionDto(
                        PrescriptionId: p.Id,
                        PrescriptionCode: p.PrescriptionCode ?? $"RX-{p.Id}",
                        Status: AppointmentBookingRules.NormalizeStatusKey(p.Status) is { Length: > 0 } rxStatus
                            ? rxStatus
                            : "ISSUED",
                        FulfillmentChoice: p.FulfillmentChoice,
                        IssuedAt: p.IssuedAt ?? p.CreatedAt,
                        PrescribingDoctorName: ResolveDoctorName(p.PrescribingDoctorId, doctorsById),
                        Items: itemDtos);
                })
                .ToList();

            allVisitEntries.Add(new MedicalHistoryVisitEntryDto(
                VisitId: v.Id,
                VisitCode: v.VisitCode ?? $"VIS-{v.Id}",
                VisitDate: visitDate,
                CheckedInAt: v.CheckedInAt,
                CompletedAt: v.CompletedAt,
                Status: visitStatus,
                VisitType: visitType,
                ReasonForVisit: v.ReasonForVisit ?? appt?.ReasonForVisit,
                DepartmentId: visitDepartment?.Id ?? departmentId,
                DepartmentName: visitDepartment?.Name ?? "Khoa Khám bệnh",
                SpecialtyCode: visitSpecialty?.SpecialtyCode,
                SpecialtyName: visitSpecialty?.Name ?? "Đa khoa",
                RoomCode: visitRoom?.RoomCode,
                RoomName: visitRoom?.Name,
                DoctorId: visitDoctor?.Id ?? doctorId,
                DoctorCode: visitDoctor?.DoctorCode,
                DoctorName: FormatDoctorDisplayName(visitDoctor) ?? "Chưa phân công",
                IsAssignedToCurrentDoctor: isAssignedToCurrentDoctor,
                IsCurrentVisit: currentVisit is not null && v.Id == currentVisit.Id,
                MedicalRecord: recordDto,
                Diagnoses: diagnosisDtos,
                DiagnosticOrders: orderDtos,
                Prescriptions: prescriptionDtos));
        }

        // Compute overall patient summary & stats across all scoped hospital visits BEFORE timeline filters
        var latestAllergies = allVisitEntries
            .Select(e => e.MedicalRecord?.RelevantHistoryAllergies)
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
            ?.Trim()
            ?? DefaultAllergiesSummary;

        var chronicConditions = allVisitEntries
            .SelectMany(e => e.Diagnoses)
            .Where(d => !string.IsNullOrWhiteSpace(d.Icd10Code) && !string.IsNullOrWhiteSpace(d.Icd10NameVi))
            .Select(d => $"{d.Icd10Code} - {d.Icd10NameVi}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var vietnamNow = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(vietnamNow);
        var age = ComputeAge(patient.DateOfBirth, today);

        var patientSummary = new MedicalHistoryPatientSummaryDto(
            PatientId: patient.Id,
            PatientCode: patient.PatientCode,
            FullName: patient.FullName,
            Gender: string.IsNullOrWhiteSpace(patient.Gender) ? "OTHER" : patient.Gender.Trim().ToUpperInvariant(),
            DateOfBirth: patient.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Age: age,
            BloodType: patient.BloodType,
            AllergiesSummary: latestAllergies,
            ChronicConditions: chronicConditions);

        MedicalHistoryCurrentVisitDto? currentVisitDto = null;
        if (currentVisit is not null)
        {
            var matchedCurrent = allVisitEntries.FirstOrDefault(e => e.VisitId == currentVisit.Id);
            if (matchedCurrent is not null)
            {
                currentVisitDto = new MedicalHistoryCurrentVisitDto(
                    VisitId: matchedCurrent.VisitId,
                    VisitCode: matchedCurrent.VisitCode,
                    Status: matchedCurrent.Status,
                    ReasonForVisit: matchedCurrent.ReasonForVisit,
                    CheckedInAt: matchedCurrent.CheckedInAt,
                    RoomCode: matchedCurrent.RoomCode,
                    RoomName: matchedCurrent.RoomName,
                    DepartmentName: matchedCurrent.DepartmentName,
                    DoctorName: matchedCurrent.DoctorName);
            }
        }

        var stats = new MedicalHistoryStatsDto(
            TotalVisits: allVisitEntries.Count,
            CompletedVisits: allVisitEntries.Count(e => string.Equals(e.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)),
            FinalizedRecordsCount: allVisitEntries.Count(e => string.Equals(e.MedicalRecord?.Status, "FINALIZED", StringComparison.OrdinalIgnoreCase)),
            TotalDiagnosesCount: allVisitEntries.Sum(e => e.Diagnoses.Count),
            TotalDiagnosticOrdersCount: allVisitEntries.Sum(e => e.DiagnosticOrders.Count),
            TotalPrescriptionsCount: allVisitEntries.Sum(e => e.Prescriptions.Count),
            LastVisitAt: allVisitEntries
                .Select(e => e.CheckedInAt ?? e.CompletedAt)
                .Where(dt => dt.HasValue)
                .OrderByDescending(dt => dt!.Value)
                .FirstOrDefault());

        // Apply timeline filters
        var filteredVisits = ApplyTimelineFilters(allVisitEntries, currentVisit?.Id, request);

        // Persist PHI AuditLog (MEDICAL_HISTORY_VIEWED)
        await WriteAuditLogAsync(
            actorUserId,
            hospital.Id,
            actorDoctor,
            patient,
            currentVisitDto,
            filteredVisits.Count,
            ipAddress,
            nowUtc,
            ct);

        return new PatientMedicalHistoryResponseDto(
            FacilityName: hospital.Name,
            FacilityCode: hospital.HospitalCode,
            GeneratedAtUtc: nowUtc,
            Patient: patientSummary,
            CurrentVisit: currentVisitDto,
            Stats: stats,
            Visits: filteredVisits);
    }

    private static IReadOnlyList<MedicalHistoryVisitEntryDto> ApplyTimelineFilters(
        List<MedicalHistoryVisitEntryDto> entries,
        long? currentVisitId,
        GetPatientMedicalHistoryRequest request)
    {
        IEnumerable<MedicalHistoryVisitEntryDto> query = entries;

        if (!request.IncludeCurrentVisit && currentVisitId.HasValue)
        {
            query = query.Where(e => e.VisitId != currentVisitId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var normalizedStatus = AppointmentBookingRules.NormalizeStatusKey(request.Status);
            if (!string.Equals(normalizedStatus, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(e =>
                    string.Equals(e.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.MedicalRecord?.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (request.DepartmentId.HasValue)
        {
            query = query.Where(e => e.DepartmentId == request.DepartmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SpecialtyCode))
        {
            var trimmedSpecialty = request.SpecialtyCode.Trim();
            if (!string.Equals(trimmedSpecialty, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(e =>
                    string.Equals(e.SpecialtyCode, trimmedSpecialty, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.FromDate) &&
            DateOnly.TryParseExact(request.FromDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate))
        {
            query = query.Where(e =>
                DateOnly.TryParseExact(e.VisitDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var visitDate) &&
                visitDate >= fromDate);
        }

        if (!string.IsNullOrWhiteSpace(request.ToDate) &&
            DateOnly.TryParseExact(request.ToDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
        {
            query = query.Where(e =>
                DateOnly.TryParseExact(e.VisitDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var visitDate) &&
                visitDate <= toDate);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(e => MatchesKeyword(e, keyword));
        }

        return query
            .Take(request.Limit)
            .ToList();
    }

    private static bool MatchesKeyword(MedicalHistoryVisitEntryDto entry, string keyword)
    {
        static bool ContainsText(string? source, string term)
            => !string.IsNullOrWhiteSpace(source) &&
               source.Contains(term, StringComparison.OrdinalIgnoreCase);

        if (ContainsText(entry.VisitCode, keyword) ||
            ContainsText(entry.ReasonForVisit, keyword) ||
            ContainsText(entry.DepartmentName, keyword) ||
            ContainsText(entry.SpecialtyName, keyword) ||
            ContainsText(entry.SpecialtyCode, keyword) ||
            ContainsText(entry.DoctorName, keyword) ||
            ContainsText(entry.RoomName, keyword))
        {
            return true;
        }

        if (entry.MedicalRecord is { } rec &&
            (ContainsText(rec.PresentingComplaint, keyword) ||
             ContainsText(rec.SymptomHistory, keyword) ||
             ContainsText(rec.RelevantHistoryAllergies, keyword) ||
             ContainsText(rec.ClinicalFindings, keyword) ||
             ContainsText(rec.ClinicalConclusion, keyword)))
        {
            return true;
        }

        if (entry.Diagnoses.Any(d =>
                ContainsText(d.Icd10Code, keyword) ||
                ContainsText(d.Icd10NameVi, keyword) ||
                ContainsText(d.ClinicalDiagnosis, keyword)))
        {
            return true;
        }

        if (entry.DiagnosticOrders.Any(o =>
                ContainsText(o.OrderCode, keyword) ||
                ContainsText(o.ServiceCode, keyword) ||
                ContainsText(o.ServiceName, keyword) ||
                ContainsText(o.ResultText, keyword) ||
                ContainsText(o.Conclusion, keyword)))
        {
            return true;
        }

        if (entry.Prescriptions.Any(p =>
                ContainsText(p.PrescriptionCode, keyword) ||
                p.Items.Any(i =>
                    ContainsText(i.MedicineCode, keyword) ||
                    ContainsText(i.MedicineName, keyword) ||
                    ContainsText(i.ActiveIngredient, keyword))))
        {
            return true;
        }

        return false;
    }

    private async Task WriteAuditLogAsync(
        long actorUserId,
        long hospitalId,
        Doctor actorDoctor,
        Patient patient,
        MedicalHistoryCurrentVisitDto? currentVisit,
        int returnedVisitCount,
        string? ipAddress,
        DateTime nowUtc,
        CancellationToken ct)
    {
        try
        {
            var changeData = JsonSerializer.Serialize(new
            {
                patientId = patient.Id,
                patientCode = patient.PatientCode,
                currentVisitId = currentVisit?.VisitId,
                currentVisitCode = currentVisit?.VisitCode,
                doctorId = actorDoctor.Id,
                doctorCode = actorDoctor.DoctorCode,
                returnedVisitCount,
            });

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = hospitalId,
                Action = AuditActionMedicalHistoryViewed,
                EntityType = "PATIENT",
                EntityId = patient.Id.ToString(CultureInfo.InvariantCulture),
                Reason = "Bác sĩ tra cứu lịch sử khám bệnh lâm sàng của bệnh nhân (UC32).",
                ChangeData = changeData,
                IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                OccurredAt = nowUtc,
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (NotSupportedException)
        {
            // Optional in minimal read-only unit test contexts that omit AuditLogs DbSet.
        }
    }

    private async Task<(Hospital Hospital, Doctor Doctor)> EnsureDoctorAccessAsync(
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
            throw new ForbiddenException("Tài khoản bác sĩ không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản bác sĩ đang bị tạm khóa.");
        }

        var actorRoleIds = (await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct))
            .ToHashSet();

        var actorRoleCodes = await _db.Roles
            .AsNoTracking()
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        if (!actorRoleCodes.Any(c => string.Equals(c, DoctorRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền xem lịch sử khám bệnh của Bác sĩ.");
        }

        var allRolePermissions = await TryQueryAsync(() => _db.RolePermissions.AsNoTracking().ToListAsync(ct));
        if (allRolePermissions.Count > 0)
        {
            var allPermissions = await TryQueryAsync(() => _db.Permissions.AsNoTracking().ToListAsync(ct));
            var permissionsById = allPermissions.ToDictionary(p => p.Id, p => p.Code);

            var actorPermissionCodes = allRolePermissions
                .Where(rp => actorRoleIds.Contains(rp.RoleId) && permissionsById.ContainsKey(rp.PermissionId))
                .Select(rp => permissionsById[rp.PermissionId])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!actorPermissionCodes.Overlaps(RequiredDoctorPermissions))
            {
                throw new ForbiddenException("Tài khoản bác sĩ chưa được cấp quyền truy cập hồ sơ bệnh án lâm sàng.");
            }
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản bác sĩ chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của bác sĩ đã ngừng hoạt động.");
        }

        var actorDoctor = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .Include(d => d.Specialty)
            .FirstOrDefaultAsync(d => d.StaffId == actorStaff.Id, ct)
            ?? throw new ForbiddenException("Tài khoản chưa được liên kết với hồ sơ bác sĩ lâm sàng.");

        if (!actorDoctor.IsActive)
        {
            throw new ForbiddenException("Hồ sơ hành nghề của bác sĩ hiện đang tạm ngừng hoạt động.");
        }

        if (actorDoctor.HospitalId.HasValue &&
            actorDoctor.HospitalId.Value != actorStaff.HospitalId.Value)
        {
            throw new ForbiddenException("Hồ sơ bác sĩ không thuộc cùng cơ sở y tế với hồ sơ nhân sự.");
        }

        var hospitalId = actorDoctor.HospitalId ?? actorStaff.HospitalId.Value;

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho bác sĩ hiện đang ngừng hoạt động.");
        }

        return (hospital, actorDoctor);
    }

    private async Task<Visit?> FindVisitAsync(long? visitId, string? visitCode, CancellationToken ct)
    {
        if (visitId.HasValue)
        {
            return await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == visitId.Value, ct);
        }

        return await _db.Visits
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == visitCode, ct);
    }

    private async Task EnsureVisitWithinHospitalAndActiveAsync(
        Visit visit,
        Appointment? appointment,
        long hospitalId,
        CancellationToken ct)
    {
        Staff? checkedInStaff = null;
        if (visit.CheckedInByStaffId.HasValue)
        {
            checkedInStaff = await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == visit.CheckedInByStaffId.Value, ct);
        }

        HospitalRoom? room = null;
        if (visit.ExaminationRoomId.HasValue)
        {
            room = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value, ct);
        }

        Doctor? assignedDoctor = null;
        if (visit.AssignedDoctorId.HasValue)
        {
            assignedDoctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == visit.AssignedDoctorId.Value, ct);
        }

        Doctor? completedDoctor = null;
        if (visit.CompletedByDoctorId.HasValue)
        {
            completedDoctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == visit.CompletedByDoctorId.Value, ct);
        }

        var associatedHospitalIds = new long?[]
        {
            room?.HospitalId,
            room?.Department?.HospitalId,
            assignedDoctor?.HospitalId,
            assignedDoctor?.Staff?.HospitalId,
            completedDoctor?.HospitalId,
            completedDoctor?.Staff?.HospitalId,
            checkedInStaff?.HospitalId,
            appointment?.Slot?.Service?.HospitalId,
            appointment?.Slot?.Doctor?.HospitalId,
            appointment?.Slot?.Room?.HospitalId,
        };

        if (associatedHospitalIds.Any(hId => hId.HasValue && hId.Value != hospitalId))
        {
            throw new ForbiddenException("Bạn không có quyền truy cập lịch sử khám của cơ sở y tế khác.");
        }

        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Lượt khám đã bị hủy hoặc vắng mặt, không thể mở lịch sử khám từ lượt khám này.");
        }
    }

    private static IReadOnlyList<long> GetVisitHospitalIds(
        Visit visit,
        Appointment? appointment,
        IReadOnlyDictionary<long, HospitalRoom> roomsWithinHospital,
        IReadOnlyDictionary<long, Doctor> allDoctorsById,
        IReadOnlyDictionary<long, Staff> allStaffById)
    {
        var ids = new List<long>(8);

        if (visit.ExaminationRoomId.HasValue &&
            roomsWithinHospital.TryGetValue(visit.ExaminationRoomId.Value, out var room) &&
            room.HospitalId.HasValue)
        {
            ids.Add(room.HospitalId.Value);
        }

        if (visit.AssignedDoctorId.HasValue &&
            allDoctorsById.TryGetValue(visit.AssignedDoctorId.Value, out var assignedDoc))
        {
            if (assignedDoc.HospitalId.HasValue)
                ids.Add(assignedDoc.HospitalId.Value);
            else if (assignedDoc.Staff?.HospitalId is { } staffHospId)
                ids.Add(staffHospId);
        }

        if (visit.CompletedByDoctorId.HasValue &&
            allDoctorsById.TryGetValue(visit.CompletedByDoctorId.Value, out var completedDoc))
        {
            if (completedDoc.HospitalId.HasValue)
                ids.Add(completedDoc.HospitalId.Value);
            else if (completedDoc.Staff?.HospitalId is { } staffHospId)
                ids.Add(staffHospId);
        }

        if (visit.CheckedInByStaffId.HasValue &&
            allStaffById.TryGetValue(visit.CheckedInByStaffId.Value, out var checkedInStaff) &&
            checkedInStaff.HospitalId.HasValue)
        {
            ids.Add(checkedInStaff.HospitalId.Value);
        }

        if (appointment?.Slot?.Service?.HospitalId is { } svcHospitalId)
        {
            ids.Add(svcHospitalId);
        }

        if (appointment?.Slot?.Doctor?.HospitalId is { } slotDocHospitalId)
        {
            ids.Add(slotDocHospitalId);
        }

        if (appointment?.Slot?.Room?.HospitalId is { } slotRoomHospitalId)
        {
            ids.Add(slotRoomHospitalId);
        }

        return ids;
    }

    private static bool IsVisitAssignedToDoctor(
        Visit visit,
        Appointment? appointment,
        MedicalRecord? medicalRecord,
        IReadOnlyList<QueueEntry>? queueEntries,
        IReadOnlyDictionary<long, QueueServicePoint> servicePointsById,
        long actorDoctorId)
    {
        if (visit.AssignedDoctorId == actorDoctorId ||
            visit.CompletedByDoctorId == actorDoctorId ||
            appointment?.Slot?.DoctorId == actorDoctorId ||
            medicalRecord?.CreatedByDoctorId == actorDoctorId)
        {
            return true;
        }

        if (queueEntries is { Count: > 0 })
        {
            foreach (var qe in queueEntries)
            {
                var qeStatus = AppointmentBookingRules.NormalizeStatusKey(qe.Status);
                if (qeStatus is "CANCELLED" or "NO_SHOW")
                {
                    continue;
                }

                if (qe.ServicePointId.HasValue &&
                    servicePointsById.TryGetValue(qe.ServicePointId.Value, out var sp1) &&
                    sp1.DoctorId == actorDoctorId)
                {
                    return true;
                }

                if (qe.CurrentServicePointId.HasValue &&
                    servicePointsById.TryGetValue(qe.CurrentServicePointId.Value, out var sp2) &&
                    sp2.DoctorId == actorDoctorId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static (long? Id, string? Code, string Trimmed) ParseEntityIdentifier(string? raw, string label)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ValidationException($"Mã định danh {label} không hợp lệ.");
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 40)
        {
            throw new ValidationException($"Mã định danh {label} không hợp lệ.");
        }

        if (trimmed.All(char.IsAsciiDigit) ||
            (trimmed.StartsWith('-') && trimmed.Length > 1 && trimmed[1..].All(char.IsAsciiDigit)))
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId) ||
                numericId <= 0)
            {
                throw new ValidationException($"ID {label} phải lớn hơn 0.");
            }

            return (numericId, null, trimmed);
        }

        if (trimmed.StartsWith('-') ||
            !trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
        {
            throw new ValidationException($"Mã định danh {label} không hợp lệ.");
        }

        return (null, trimmed.ToUpperInvariant(), trimmed);
    }

    private static void ValidateFilters(GetPatientMedicalHistoryRequest request)
    {
        if (request.Limit is < 1 or > 50)
        {
            throw new ValidationException("Số lượng lịch sử khám hiển thị phải từ 1 đến 50.");
        }

        if (request.DepartmentId.HasValue && request.DepartmentId.Value <= 0)
        {
            throw new ValidationException("Khoa chuyên môn lọc không hợp lệ.");
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var normalizedStatus = AppointmentBookingRules.NormalizeStatusKey(request.Status);
            if (!string.Equals(normalizedStatus, "ALL", StringComparison.OrdinalIgnoreCase) &&
                !AllowedFilterStatuses.Contains(normalizedStatus))
            {
                throw new ValidationException("Trạng thái lịch sử khám lọc không hợp lệ.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SpecialtyCode))
        {
            var trimmedSpecialty = request.SpecialtyCode.Trim();
            if (!string.Equals(trimmedSpecialty, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmedSpecialty.Length > 30 ||
                    !trimmedSpecialty.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
                {
                    throw new ValidationException("Mã chuyên khoa lọc không hợp lệ.");
                }
            }
        }

        DateOnly? fromDate = null;
        if (!string.IsNullOrWhiteSpace(request.FromDate))
        {
            if (!DateOnly.TryParseExact(request.FromDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
            {
                throw new ValidationException("Khoảng ngày lọc không hợp lệ (định dạng YYYY-MM-DD).");
            }

            fromDate = parsedFrom;
        }

        DateOnly? toDate = null;
        if (!string.IsNullOrWhiteSpace(request.ToDate))
        {
            if (!DateOnly.TryParseExact(request.ToDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
            {
                throw new ValidationException("Khoảng ngày lọc không hợp lệ (định dạng YYYY-MM-DD).");
            }

            toDate = parsedTo;
        }

        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        {
            throw new ValidationException("Ngày bắt đầu không được lớn hơn ngày kết thúc.");
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword) && request.Keyword.Trim().Length > 100)
        {
            throw new ValidationException("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");
        }

        if (!string.IsNullOrWhiteSpace(request.VisitIdOrCode))
        {
            _ = ParseEntityIdentifier(request.VisitIdOrCode, "lượt khám");
        }
    }

    internal static MedicalHistoryVitalSignsDto? ParseVitalSigns(string? vitalSignsJson)
    {
        if (string.IsNullOrWhiteSpace(vitalSignsJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(vitalSignsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var sys = TryGetInt(root, "bp_systolic_mmhg");
            var dia = TryGetInt(root, "bp_diastolic_mmhg");
            var pulse = TryGetInt(root, "pulse_bpm");
            var temp = TryGetDecimal(root, "temperature_c");
            var spo2 = TryGetInt(root, "spo2_percent");
            var heightCm = TryGetDecimal(root, "height_cm");
            var weightKg = TryGetDecimal(root, "weight_kg");

            var bpLabel = sys.HasValue && dia.HasValue
                ? $"{sys.Value}/{dia.Value} mmHg"
                : null;

            decimal? bmi = null;
            if (heightCm is > 0 && weightKg is > 0)
            {
                var heightM = heightCm.Value / 100m;
                bmi = Math.Round(weightKg.Value / (heightM * heightM), 1, MidpointRounding.AwayFromZero);
            }

            return new MedicalHistoryVitalSignsDto(
                BpSystolicMmhg: sys,
                BpDiastolicMmhg: dia,
                BloodPressureLabel: bpLabel,
                PulseBpm: pulse,
                TemperatureC: temp,
                Spo2Percent: spo2,
                HeightCm: heightCm,
                WeightKg: weightKg,
                Bmi: bmi);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ParseAddendumNote(string? correctedContentJson)
    {
        if (string.IsNullOrWhiteSpace(correctedContentJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(correctedContentJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("note", out var noteEl) &&
                noteEl.ValueKind == JsonValueKind.String)
            {
                return noteEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Ignore malformed JSON and fallback to null
        }

        return null;
    }

    private static int? TryGetInt(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
        {
            return val;
        }

        if (prop.ValueKind == JsonValueKind.String &&
            int.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? TryGetDecimal(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var val))
        {
            return val;
        }

        if (prop.ValueKind == JsonValueKind.String &&
            decimal.TryParse(prop.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ResolveDoctorName(long? doctorId, IReadOnlyDictionary<long, Doctor> doctorsById)
    {
        if (!doctorId.HasValue || !doctorsById.TryGetValue(doctorId.Value, out var doctor))
        {
            return null;
        }

        return FormatDoctorDisplayName(doctor);
    }

    private static string? FormatDoctorDisplayName(Doctor? doctor)
    {
        if (doctor is null)
        {
            return null;
        }

        var fullName = doctor.Staff?.FullName?.Trim();
        var title = doctor.ProfessionalTitle?.Trim();

        if (!string.IsNullOrEmpty(fullName) && !string.IsNullOrEmpty(title))
        {
            return $"{title}. {fullName}";
        }

        if (!string.IsNullOrEmpty(fullName))
        {
            return fullName;
        }

        return !string.IsNullOrWhiteSpace(doctor.DoctorCode) ? doctor.DoctorCode : $"BS #{doctor.Id}";
    }

    private static int ComputeAge(DateOnly? dateOfBirth, DateOnly today)
    {
        if (!dateOfBirth.HasValue)
        {
            return 0;
        }

        var age = today.Year - dateOfBirth.Value.Year;
        if (dateOfBirth.Value > today.AddYears(-age))
        {
            age--;
        }

        return Math.Max(0, age);
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
