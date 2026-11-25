using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Patients;

public sealed class PatientMedicalRecordService : IPatientMedicalRecordService
{
    public const string AuditActionRecordViewed = "PATIENT_MEDICAL_RECORD_VIEWED";

    private const int MaxIdentifierLength = 40;
    private const int MaxKeywordLength = 100;
    private const int MaxPageSize = 50;
    private const string NotFoundMessage = "Không tìm thấy hồ sơ bệnh án đã hoàn tất.";
    private const string ForbiddenRecordMessage = "Bạn không có quyền xem hồ sơ bệnh án này.";
    private const string ForbiddenMemberMessage = "Bạn không có quyền xem hồ sơ bệnh án của thành viên này.";

    private static readonly string[] VisiblePrescriptionStatuses = ["ISSUED", "PREPARING", "READY", "DISPENSED"];

    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _timeProvider;

    public PatientMedicalRecordService(
        IApplicationDbContext db,
        IAuditLogger auditLogger,
        TimeProvider timeProvider)
    {
        _db = db;
        _auditLogger = auditLogger;
        _timeProvider = timeProvider;
    }

    public async Task<PatientMedicalRecordListResponseDto> GetMyMedicalRecordsAsync(
        long actorUserId,
        GetPatientMedicalRecordsRequest? request,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        await EnsureActiveUserAsync(actorUserId, ct);
        var filter = NormalizeListRequest(request);
        var phiContext = await LoadPhiScopeAsync(actorUserId, ct);

        var activeScope = phiContext.Members;
        if (filter.PatientId is long requestedPatientId)
        {
            activeScope = activeScope
                .Where(member => member.Scope.Patient.Id == requestedPatientId)
                .ToList();
            if (activeScope.Count == 0)
                throw new ForbiddenException(ForbiddenMemberMessage);
        }

        var scopeMemberDtos = phiContext.Members
            .Select(member => new PatientEmrScopeMemberDto(
                member.Scope.Patient.Id,
                BlankToNull(member.Scope.Patient.PatientCode),
                member.Scope.Patient.FullName,
                member.Scope.RelationshipLabel,
                member.IsSelf))
            .ToList();

        var patientIds = activeScope.Select(m => m.Scope.Patient.Id).Distinct().ToList();
        var candidates = await LoadCompletedVisitRecordsAsync(patientIds, ct);
        if (candidates.Count == 0)
        {
            await WriteAuditAsync(
                actorUserId,
                "PATIENT",
                (filter.PatientId ?? phiContext.SelfId).ToString(CultureInfo.InvariantCulture),
                new { scope = "LIST", page = filter.Page, pageSize = filter.PageSize, patientId = filter.PatientId, resultCount = 0 },
                ipAddress,
                ct);

            return new PatientMedicalRecordListResponseDto(
                [],
                filter.Page,
                filter.PageSize,
                0,
                scopeMemberDtos,
                [],
                []);
        }

        var lookups = await LoadLookupsAsync(candidates, ct);
        var scopeByPatientId = activeScope.ToDictionary(m => m.Scope.Patient.Id);

        var allProjected = candidates
            .Where(c => c.Visit.PatientId.HasValue && scopeByPatientId.ContainsKey(c.Visit.PatientId.Value))
            .Select(c => ProjectListEntry(c, scopeByPatientId[c.Visit.PatientId!.Value], lookups))
            .ToList();

        var hospitalOptions = allProjected
            .Where(item => item.Item.HospitalId.HasValue && !string.IsNullOrWhiteSpace(item.Item.HospitalName))
            .GroupBy(item => item.Item.HospitalId!.Value)
            .Select(g => new PatientEmrFilterOptionDto(
                g.Key,
                g.First().HospitalCode,
                g.First().Item.HospitalName!))
            .OrderBy(opt => opt.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var specialtyOptions = allProjected
            .Where(item => item.Item.SpecialtyId.HasValue && !string.IsNullOrWhiteSpace(item.Item.SpecialtyName))
            .GroupBy(item => item.Item.SpecialtyId!.Value)
            .Select(g => new PatientEmrFilterOptionDto(
                g.Key,
                g.First().Item.SpecialtyCode,
                g.First().Item.SpecialtyName!))
            .OrderBy(opt => opt.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filtered = ApplyFilters(allProjected, filter)
            .OrderByDescending(entry => entry.SortTimestamp)
            .ThenByDescending(entry => entry.Item.MedicalRecordId)
            .Select(entry => entry.Item)
            .ToList();

        var pageItems = filtered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        await WriteAuditAsync(
            actorUserId,
            "PATIENT",
            (filter.PatientId ?? phiContext.SelfId).ToString(CultureInfo.InvariantCulture),
            new { scope = "LIST", page = filter.Page, pageSize = filter.PageSize, patientId = filter.PatientId, resultCount = pageItems.Count },
            ipAddress,
            ct);

        return new PatientMedicalRecordListResponseDto(
            pageItems,
            filter.Page,
            filter.PageSize,
            filtered.Count,
            scopeMemberDtos,
            hospitalOptions,
            specialtyOptions);
    }

    public async Task<PatientMedicalRecordDetailDto> GetMyMedicalRecordAsync(
        long actorUserId,
        string? idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        await EnsureActiveUserAsync(actorUserId, ct);
        var identifier = ParseIdentifier(idOrCode);
        var phiContext = await LoadPhiScopeAsync(actorUserId, ct);
        var scopeByPatientId = phiContext.Members.ToDictionary(m => m.Scope.Patient.Id);

        var (record, visit) = await ResolveTargetRecordAndVisitAsync(identifier, ct);
        var resolvedPatientId = await ResolveVisitPatientIdAsync(visit, ct);
        if (!resolvedPatientId.HasValue)
            throw new NotFoundException(NotFoundMessage);

        if (!scopeByPatientId.TryGetValue(resolvedPatientId.Value, out var member))
            throw new ForbiddenException(ForbiddenRecordMessage);

        if (!IsVisitVisibleToPatient(visit) || record is null || !IsRecordFinalized(record))
            throw new NotFoundException(NotFoundMessage);

        var candidate = new VisitRecordCandidate(visit, record);
        var lookups = await LoadLookupsAsync([candidate], ct);
        var detail = MapDetail(candidate, member, lookups);

        await WriteAuditAsync(
            actorUserId,
            "MEDICAL_RECORD",
            record.Id.ToString(CultureInfo.InvariantCulture),
            new { scope = "DETAIL", visitId = visit.Id, patientId = member.Scope.Patient.Id, isSelf = member.IsSelf },
            ipAddress,
            ct);

        return detail;
    }

    private async Task EnsureActiveUserAsync(long actorUserId, CancellationToken ct)
    {
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (user.AccountStatus != AccountStatus.ACTIVE ||
            (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc))
        {
            throw new UnauthorizedAccessException("Tài khoản bệnh nhân chưa kích hoạt hoặc đang bị khóa.");
        }
    }

    private async Task<PhiScopeContext> LoadPhiScopeAsync(long actorUserId, CancellationToken ct)
    {
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var owned = await AppointmentBookingRules.LoadOwnedPatientsPhiAsync(_db, actorUserId, nowLocal, ct);
        var built = AppointmentReadModel.BuildScopeMembers(
            owned.Self,
            owned.FamilyLinks,
            DateOnly.FromDateTime(nowLocal));

        var members = built
            .Select(m => new ScopedPhiMember(m, m.Patient.Id == owned.Self.Id))
            .ToList();

        return new PhiScopeContext(owned.Self.Id, members);
    }

    private async Task<(MedicalRecord? Record, Visit Visit)> ResolveTargetRecordAndVisitAsync(
        RecordIdentifier identifier,
        CancellationToken ct)
    {
        if (identifier.RecordId is long recordId)
        {
            var byRecordId = await _db.MedicalRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == recordId, ct);
            if (byRecordId?.VisitId is long recordVisitId)
            {
                var visitForRecord = await _db.Visits.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == recordVisitId, ct);
                if (visitForRecord is not null)
                    return (byRecordId, visitForRecord);
            }

            if (identifier.VisitCode is null)
                throw new NotFoundException(NotFoundMessage);
        }

        if (identifier.VisitCode is string visitCode)
        {
            var visitByCode = await _db.Visits.AsNoTracking()
                .FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == visitCode, ct);
            if (visitByCode is null)
                throw new NotFoundException(NotFoundMessage);

            var records = await _db.MedicalRecords.AsNoTracking()
                .Where(r => r.VisitId == visitByCode.Id)
                .ToListAsync(ct);
            var chosenRecord = ChoosePrimaryRecord(records);
            return (chosenRecord, visitByCode);
        }

        throw new NotFoundException(NotFoundMessage);
    }

    private async Task<long?> ResolveVisitPatientIdAsync(Visit visit, CancellationToken ct)
    {
        if (visit.PatientId.HasValue)
            return visit.PatientId.Value;

        if (!visit.AppointmentId.HasValue)
            return null;

        var appointments = await TryQueryListAsync(() => _db.Appointments.AsNoTracking()
            .Where(a => a.Id == visit.AppointmentId.Value)
            .ToListAsync(ct));
        return appointments.FirstOrDefault()?.PatientId;
    }

    private async Task<List<VisitRecordCandidate>> LoadCompletedVisitRecordsAsync(
        IReadOnlyList<long> patientIds,
        CancellationToken ct)
    {
        if (patientIds.Count == 0)
            return [];

        var rawVisits = await _db.Visits.AsNoTracking()
            .Where(v => v.PatientId != null && patientIds.Contains(v.PatientId.Value))
            .ToListAsync(ct);

        var completedVisits = rawVisits
            .Where(IsVisitVisibleToPatient)
            .ToList();
        if (completedVisits.Count == 0)
            return [];

        var visitIds = completedVisits.Select(v => v.Id).ToList();
        var rawRecords = await _db.MedicalRecords.AsNoTracking()
            .Where(r => r.VisitId != null && visitIds.Contains(r.VisitId.Value))
            .ToListAsync(ct);

        var finalizedByVisitId = rawRecords
            .Where(r => r.VisitId.HasValue && IsRecordFinalized(r))
            .GroupBy(r => r.VisitId!.Value)
            .ToDictionary(g => g.Key, g => ChoosePrimaryRecord(g)!);

        var candidates = new List<VisitRecordCandidate>(finalizedByVisitId.Count);
        foreach (var visit in completedVisits)
        {
            if (finalizedByVisitId.TryGetValue(visit.Id, out var record))
                candidates.Add(new VisitRecordCandidate(visit, record));
        }

        return candidates;
    }

    private async Task<EmrLookups> LoadLookupsAsync(
        IReadOnlyList<VisitRecordCandidate> candidates,
        CancellationToken ct)
    {
        var visitIds = candidates.Select(c => c.Visit.Id).Distinct().ToList();
        var recordIds = candidates.Select(c => c.Record.Id).Distinct().ToList();

        var revisions = await TryQueryListAsync(() => _db.MedicalRecordRevisions.AsNoTracking()
            .Where(r => r.MedicalRecordId != null && recordIds.Contains(r.MedicalRecordId.Value))
            .ToListAsync(ct));
        var revisionsByRecordId = revisions
            .Where(r => r.MedicalRecordId.HasValue)
            .GroupBy(r => r.MedicalRecordId!.Value)
            .ToDictionary(g => g.Key, g => ChooseConfirmedRevision(g));

        var diagnoses = await TryQueryListAsync(() => _db.VisitDiagnoses.AsNoTracking()
            .Where(d => d.VisitId != null && visitIds.Contains(d.VisitId.Value))
            .ToListAsync(ct));
        var activeDiagnoses = diagnoses
            .Where(d => AppointmentBookingRules.NormalizeStatusKey(d.Status) is not ("CANCELLED" or "REMOVED" or "SUPERSEDED"))
            .ToList();
        var diagnosesByVisitId = activeDiagnoses
            .Where(d => d.VisitId.HasValue)
            .GroupBy(d => d.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<VisitDiagnosis>)g
                    .OrderByDescending(d => d.IsPrimary == true)
                    .ThenBy(d => d.Id)
                    .ToList());

        var icd10Ids = activeDiagnoses
            .Where(d => d.Icd10CodeId.HasValue)
            .Select(d => d.Icd10CodeId!.Value)
            .Distinct()
            .ToList();
        var icd10Rows = icd10Ids.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.Icd10Codes.AsNoTracking()
                .Where(c => icd10Ids.Contains(c.Id))
                .ToListAsync(ct));
        var icd10ById = icd10Rows.ToDictionary(c => c.Id);

        var orders = await TryQueryListAsync(() => _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.VisitId != null && visitIds.Contains(o.VisitId.Value))
            .ToListAsync(ct));
        var nonCancelledOrders = orders
            .Where(o => AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED")
            .ToList();
        var orderIds = nonCancelledOrders.Select(o => o.Id).ToList();

        var results = orderIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.DiagnosticResults.AsNoTracking()
                .Where(r => r.DiagnosticOrderId != null && orderIds.Contains(r.DiagnosticOrderId.Value))
                .ToListAsync(ct));
        var resultByOrderId = results
            .Where(r => r.DiagnosticOrderId.HasValue &&
                        AppointmentBookingRules.NormalizeStatusKey(r.Status) != "CANCELLED")
            .GroupBy(r => r.DiagnosticOrderId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First());

        var resultIds = resultByOrderId.Values.Select(r => r.Id).ToList();
        var versions = resultIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.DiagnosticResultVersions.AsNoTracking()
                .Where(v => v.DiagnosticResultId != null && resultIds.Contains(v.DiagnosticResultId.Value))
                .ToListAsync(ct));
        var versionsByResultId = versions
            .Where(v => v.DiagnosticResultId.HasValue)
            .GroupBy(v => v.DiagnosticResultId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DiagnosticResultVersion>)g.ToList());

        var ordersByVisitId = nonCancelledOrders
            .Where(o => o.VisitId.HasValue)
            .GroupBy(o => o.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DiagnosticOrder>)g
                    .OrderBy(o => o.OrderedAt ?? DateTime.MinValue)
                    .ThenBy(o => o.Id)
                    .ToList());

        var rawPrescriptions = await TryQueryListAsync(() => _db.Prescriptions.AsNoTracking()
            .Where(p => p.VisitId != null && visitIds.Contains(p.VisitId.Value))
            .ToListAsync(ct));
        var visibleStatusRx = rawPrescriptions
            .Where(p => p.Status != null && VisiblePrescriptionStatuses.Contains(p.Status.Trim().ToUpperInvariant()))
            .ToList();
        var replacedIds = visibleStatusRx
            .Where(p => p.ReplacesPrescriptionId.HasValue)
            .Select(p => p.ReplacesPrescriptionId!.Value)
            .ToHashSet();
        var activePrescriptions = visibleStatusRx
            .Where(p => !replacedIds.Contains(p.Id))
            .ToList();

        var prescriptionIds = activePrescriptions.Select(p => p.Id).ToList();
        var prescriptionItems = prescriptionIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.PrescriptionItems.AsNoTracking()
                .Where(pi => pi.PrescriptionId != null && prescriptionIds.Contains(pi.PrescriptionId.Value))
                .ToListAsync(ct));
        var itemsByPrescriptionId = prescriptionItems
            .Where(pi => pi.PrescriptionId.HasValue && pi.MedicineId.HasValue && pi.PrescribedQuantity > 0)
            .GroupBy(pi => pi.PrescriptionId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PrescriptionItem>)g.OrderBy(pi => pi.Id).ToList());

        var medicineIds = prescriptionItems
            .Where(pi => pi.MedicineId.HasValue)
            .Select(pi => pi.MedicineId!.Value)
            .Distinct()
            .ToList();
        var medicines = medicineIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.Medicines.AsNoTracking()
                .Where(m => medicineIds.Contains(m.Id))
                .ToListAsync(ct));
        var medicinesById = medicines.ToDictionary(m => m.Id);

        var prescriptionsByVisitId = activePrescriptions
            .Where(p =>
                p.VisitId.HasValue &&
                itemsByPrescriptionId.TryGetValue(p.Id, out var pItems) &&
                pItems.Count > 0 &&
                pItems.All(pi => pi.MedicineId.HasValue && medicinesById.ContainsKey(pi.MedicineId.Value)))
            .GroupBy(p => p.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Prescription>)g
                    .OrderByDescending(p => p.IssuedAt ?? p.CreatedAt ?? DateTime.MinValue)
                    .ThenByDescending(p => p.Id)
                    .ToList());

        var appointmentIds = candidates
            .Where(c => c.Visit.AppointmentId.HasValue)
            .Select(c => c.Visit.AppointmentId!.Value)
            .Distinct()
            .ToList();
        var appointments = appointmentIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.Appointments.AsNoTracking()
                .Where(a => appointmentIds.Contains(a.Id))
                .ToListAsync(ct));
        var appointmentsById = appointments.ToDictionary(a => a.Id);

        var slotIds = appointments
            .Where(a => a.SlotId.HasValue)
            .Select(a => a.SlotId!.Value)
            .Distinct()
            .ToList();
        var slots = slotIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.ExaminationSlots.AsNoTracking()
                .Where(s => slotIds.Contains(s.Id))
                .ToListAsync(ct));
        var slotsById = slots.ToDictionary(s => s.Id);

        var serviceIds = slots.Select(s => s.ServiceId)
            .Concat(nonCancelledOrders.Select(o => o.ServiceId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var services = serviceIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.Services.AsNoTracking()
                .Where(s => serviceIds.Contains(s.Id))
                .ToListAsync(ct));
        var servicesById = services.ToDictionary(s => s.Id);

        var roomIds = candidates.Select(c => c.Visit.ExaminationRoomId)
            .Concat(slots.Select(s => s.RoomId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var rooms = roomIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.HospitalRooms.AsNoTracking()
                .Where(r => roomIds.Contains(r.Id))
                .ToListAsync(ct));
        var roomsById = rooms.ToDictionary(r => r.Id);

        var doctorIds = candidates.Select(c => c.Visit.CompletedByDoctorId)
            .Concat(candidates.Select(c => c.Visit.AssignedDoctorId))
            .Concat(candidates.Select(c => c.Record.CreatedByDoctorId))
            .Concat(revisionsByRecordId.Values.Select(r => r?.EditedByDoctorId))
            .Concat(slots.Select(s => s.DoctorId))
            .Concat(activePrescriptions.Select(p => p.PrescribingDoctorId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var doctors = doctorIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.Doctors.AsNoTracking()
                .Where(d => doctorIds.Contains(d.Id))
                .ToListAsync(ct));
        var doctorsById = doctors.ToDictionary(d => d.Id);

        var staffIds = doctors.Select(d => d.StaffId)
            .Concat(candidates.Select(c => c.Visit.CheckedInByStaffId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var staffRows = staffIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.Staff.AsNoTracking()
                .Where(s => staffIds.Contains(s.Id))
                .ToListAsync(ct));
        var staffById = staffRows.ToDictionary(s => s.Id);

        var departmentIds = rooms.Select(r => r.DepartmentId)
            .Concat(services.Select(s => s.DepartmentId))
            .Concat(staffRows.Select(s => s.DepartmentId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var departments = departmentIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.Departments.AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToListAsync(ct));
        var departmentsById = departments.ToDictionary(d => d.Id);

        var specialtyIds = doctors.Select(d => d.SpecialtyId)
            .Concat(services.Select(s => s.SpecialtyId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var specialties = specialtyIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.Specialties.AsNoTracking()
                .Where(s => specialtyIds.Contains(s.Id))
                .ToListAsync(ct));
        var specialtiesById = specialties.ToDictionary(s => s.Id);

        var hospitalIds = rooms.Select(r => r.HospitalId)
            .Concat(departments.Select(d => d.HospitalId))
            .Concat(services.Select(s => s.HospitalId))
            .Concat(doctors.Select(d => d.HospitalId))
            .Concat(staffRows.Select(s => s.HospitalId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var hospitals = hospitalIds.Count == 0
            ? []
            : await TryQueryListAsync(() => _db.Hospitals.AsNoTracking()
                .Where(h => hospitalIds.Contains(h.Id))
                .ToListAsync(ct));
        var hospitalsById = hospitals.ToDictionary(h => h.Id);

        return new EmrLookups(
            revisionsByRecordId,
            diagnosesByVisitId,
            icd10ById,
            ordersByVisitId,
            resultByOrderId,
            versionsByResultId,
            prescriptionsByVisitId,
            itemsByPrescriptionId,
            medicinesById,
            appointmentsById,
            slotsById,
            servicesById,
            roomsById,
            doctorsById,
            staffById,
            departmentsById,
            specialtiesById,
            hospitalsById);
    }

    private static ProjectedListEntry ProjectListEntry(
        VisitRecordCandidate candidate,
        ScopedPhiMember member,
        EmrLookups lookups)
    {
        var context = ResolveVisitContext(candidate, lookups);
        lookups.RevisionsByRecordId.TryGetValue(candidate.Record.Id, out var revision);
        lookups.DiagnosesByVisitId.TryGetValue(candidate.Visit.Id, out var visitDiagnoses);
        lookups.OrdersByVisitId.TryGetValue(candidate.Visit.Id, out var visitOrders);
        lookups.PrescriptionsByVisitId.TryGetValue(candidate.Visit.Id, out var visitPrescriptions);

        visitDiagnoses ??= [];
        visitOrders ??= [];
        visitPrescriptions ??= [];

        var primaryDx = visitDiagnoses.FirstOrDefault();
        var (primaryIcdCode, primaryDxName) = ResolveDiagnosisDisplay(primaryDx, lookups.Icd10ById);

        var sortTime = candidate.Visit.CompletedAt
            ?? candidate.Record.FinalizedAt
            ?? candidate.Visit.CheckedInAt
            ?? context.Slot?.StartsAt
            ?? DateTime.MinValue;
        var visitDateOnly = sortTime == DateTime.MinValue
            ? null
            : (DateOnly?)DateOnly.FromDateTime(sortTime);
        var visitDateStr = visitDateOnly?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

        var item = new PatientMedicalRecordListItemDto(
            MedicalRecordId: candidate.Record.Id,
            RecordCode: FormatRecordCode(candidate.Record.Id),
            VisitId: candidate.Visit.Id,
            VisitCode: BlankToNull(candidate.Visit.VisitCode),
            VisitDate: visitDateStr,
            CheckedInAt: candidate.Visit.CheckedInAt,
            CompletedAt: candidate.Visit.CompletedAt,
            FinalizedAt: candidate.Record.FinalizedAt,
            PatientId: member.Scope.Patient.Id,
            PatientCode: BlankToNull(member.Scope.Patient.PatientCode),
            PatientName: member.Scope.Patient.FullName,
            RelationshipLabel: member.Scope.RelationshipLabel,
            IsSelfProfile: member.IsSelf,
            HospitalId: context.Hospital?.Id,
            HospitalName: BlankToNull(context.Hospital?.Name),
            DepartmentName: BlankToNull(context.Department?.Name),
            SpecialtyId: context.Specialty?.Id,
            SpecialtyCode: BlankToNull(context.Specialty?.SpecialtyCode),
            SpecialtyName: BlankToNull(context.Specialty?.Name),
            DoctorName: context.DoctorName,
            PrimaryIcd10Code: primaryIcdCode,
            PrimaryDiagnosisName: primaryDxName,
            ClinicalConclusion: BlankToNull(revision?.ClinicalConclusion),
            DiagnosisCount: visitDiagnoses.Count,
            DiagnosticOrderCount: visitOrders.Count,
            PrescriptionCount: visitPrescriptions.Count);

        return new ProjectedListEntry(
            item,
            visitDateOnly,
            sortTime,
            BlankToNull(context.Hospital?.HospitalCode),
            BlankToNull(candidate.Visit.ReasonForVisit ?? context.Appointment?.ReasonForVisit));
    }

    private static PatientMedicalRecordDetailDto MapDetail(
        VisitRecordCandidate candidate,
        ScopedPhiMember member,
        EmrLookups lookups)
    {
        var projected = ProjectListEntry(candidate, member, lookups);
        var context = ResolveVisitContext(candidate, lookups);
        lookups.RevisionsByRecordId.TryGetValue(candidate.Record.Id, out var revision);
        lookups.DiagnosesByVisitId.TryGetValue(candidate.Visit.Id, out var visitDiagnoses);
        lookups.OrdersByVisitId.TryGetValue(candidate.Visit.Id, out var visitOrders);
        lookups.PrescriptionsByVisitId.TryGetValue(candidate.Visit.Id, out var visitPrescriptions);

        visitDiagnoses ??= [];
        visitOrders ??= [];
        visitPrescriptions ??= [];

        var diagnosisDtos = visitDiagnoses
            .Select(d =>
            {
                var (icdCode, nameVi) = ResolveDiagnosisDisplay(d, lookups.Icd10ById);
                return new PatientMedicalRecordDiagnosisDto(
                    DiagnosisId: d.Id,
                    Icd10Code: icdCode ?? string.Empty,
                    Icd10NameVi: nameVi ?? string.Empty,
                    ClinicalDiagnosis: BlankToNull(d.ClinicalDiagnosis),
                    IsPrimary: d.IsPrimary == true,
                    ConfirmedAt: d.ConfirmedAt ?? d.RecordedAt);
            })
            .ToList();

        var diagnosticDtos = visitOrders
            .Select(order => MapDiagnosticSummary(order, lookups))
            .ToList();

        var prescriptionDtos = visitPrescriptions
            .Select(rx => MapPrescriptionSummary(rx, member.IsSelf, lookups))
            .ToList();

        var patient = member.Scope.Patient;
        return new PatientMedicalRecordDetailDto(
            MedicalRecordId: candidate.Record.Id,
            RecordCode: projected.Item.RecordCode,
            Status: "FINALIZED",
            FinalizedAt: candidate.Record.FinalizedAt,
            VisitId: candidate.Visit.Id,
            VisitCode: projected.Item.VisitCode,
            VisitType: candidate.Visit.AppointmentId.HasValue ? "SCHEDULED" : "WALK_IN",
            VisitDate: projected.Item.VisitDate,
            CheckedInAt: candidate.Visit.CheckedInAt,
            CompletedAt: candidate.Visit.CompletedAt,
            ReasonForVisit: BlankToNull(candidate.Visit.ReasonForVisit ?? context.Appointment?.ReasonForVisit),
            PatientId: patient.Id,
            PatientCode: BlankToNull(patient.PatientCode),
            PatientName: patient.FullName,
            RelationshipLabel: member.Scope.RelationshipLabel,
            IsSelfProfile: member.IsSelf,
            Gender: BlankToNull(patient.Gender),
            DateOfBirth: patient.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BloodType: BlankToNull(patient.BloodType),
            HospitalId: context.Hospital?.Id,
            HospitalCode: BlankToNull(context.Hospital?.HospitalCode),
            HospitalName: BlankToNull(context.Hospital?.Name),
            DepartmentName: BlankToNull(context.Department?.Name),
            SpecialtyId: context.Specialty?.Id,
            SpecialtyCode: BlankToNull(context.Specialty?.SpecialtyCode),
            SpecialtyName: BlankToNull(context.Specialty?.Name),
            RoomCode: BlankToNull(context.Room?.RoomCode),
            RoomName: BlankToNull(context.Room?.Name),
            DoctorName: context.DoctorName,
            PresentingComplaint: BlankToNull(revision?.PresentingComplaint),
            SymptomHistory: BlankToNull(revision?.SymptomHistory),
            RelevantHistoryAllergies: BlankToNull(revision?.RelevantHistoryAllergies),
            ClinicalFindings: BlankToNull(revision?.ClinicalFindings),
            ClinicalConclusion: BlankToNull(revision?.ClinicalConclusion),
            RecordedAt: revision?.RecordedAt ?? candidate.Record.FinalizedAt,
            VitalSigns: ParseVitalSigns(revision?.VitalSigns),
            Diagnoses: diagnosisDtos,
            DiagnosticOrders: diagnosticDtos,
            Prescriptions: prescriptionDtos,
            ClinicalProgressionNote: BlankToNull(revision?.ClinicalProgressionNote),
            ClinicalInterpretation: BlankToNull(revision?.ClinicalInterpretation),
            TreatmentPlan: BlankToNull(revision?.TreatmentPlan),
            DischargeInstructions: BlankToNull(revision?.DischargeInstructions),
            FollowUpInstructions: BlankToNull(revision?.FollowUpInstructions),
            FollowUpDate: revision?.FollowUpDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WarningSigns: BlankToNull(revision?.WarningSigns));
    }

    private static PatientMedicalRecordDiagnosticSummaryDto MapDiagnosticSummary(
        DiagnosticOrder order,
        EmrLookups lookups)
    {
        Service? service = null;
        if (order.ServiceId is long serviceId)
            lookups.ServicesById.TryGetValue(serviceId, out service);

        lookups.ResultByOrderId.TryGetValue(order.Id, out var result);
        IReadOnlyList<DiagnosticResultVersion> versions = [];
        if (result is not null && lookups.VersionsByResultId.TryGetValue(result.Id, out var foundVersions))
            versions = foundVersions;

        var latestVersion = versions
            .OrderByDescending(v => v.VersionNo ?? 0)
            .ThenByDescending(v => v.Id)
            .FirstOrDefault();
        var publishedVersion = versions
            .Where(v => v.IsFinalized == true || v.VerifiedAt.HasValue)
            .OrderByDescending(v => v.VersionNo ?? 0)
            .ThenByDescending(v => v.Id)
            .FirstOrDefault();

        var isRowFinalized = result is not null &&
            (result.FinalizedAt.HasValue ||
             AppointmentBookingRules.NormalizeStatusKey(result.Status) is "FINALIZED" or "VERIFIED" ||
             latestVersion?.IsFinalized == true ||
             latestVersion?.VerifiedAt.HasValue == true);

        var isPublished = result is not null && (isRowFinalized || publishedVersion is not null);
        var content = isPublished ? publishedVersion ?? latestVersion : null;
        var signedAt = isPublished ? content?.VerifiedAt ?? result?.FinalizedAt : null;
        var hasReportFile = isPublished && !string.IsNullOrWhiteSpace(content?.ReportFileUrl);
        var reportDownloadUrl = hasReportFile && result is not null
            ? $"/api/v1/diagnostic/results/{result.Id.ToString(CultureInfo.InvariantCulture)}/report-file"
            : null;
        var rawOrderStatus = NormalizeOrDefault(order.Status, "ORDERED");
        var displayStatus = isPublished
            ? "PUBLISHED"
            : rawOrderStatus == "COMPLETED"
                ? "PERFORMED_AWAITING_RESULT"
                : rawOrderStatus;

        return new PatientMedicalRecordDiagnosticSummaryDto(
            OrderId: order.Id,
            OrderCode: BlankToNull(order.OrderCode) ?? $"ORD-{order.Id}",
            ServiceCode: BlankToNull(service?.ServiceCode),
            ServiceName: BlankToNull(service?.Name) ?? "Chỉ định cận lâm sàng",
            Priority: NormalizeOrDefault(order.Priority, "NORMAL"),
            Status: displayStatus,
            OrderedAt: order.OrderedAt,
            IsResultPublished: isPublished,
            SignedAt: signedAt,
            ConclusionSummary: isPublished ? BlankToNull(content?.Conclusion) : null,
            HasReportFile: hasReportFile,
            ResultId: isPublished ? result?.Id : null,
            ReportFileDownloadUrl: reportDownloadUrl);
    }

    private static PatientMedicalRecordPrescriptionSummaryDto MapPrescriptionSummary(
        Prescription prescription,
        bool isSelfProfile,
        EmrLookups lookups)
    {
        lookups.ItemsByPrescriptionId.TryGetValue(prescription.Id, out var rawItems);
        rawItems ??= [];

        var itemDtos = rawItems
            .Select(item =>
            {
                lookups.MedicinesById.TryGetValue(item.MedicineId!.Value, out var medicine);
                return new PatientMedicalRecordPrescriptionItemSummaryDto(
                    ItemId: item.Id,
                    MedicineCode: BlankToNull(medicine?.MedicineCode),
                    MedicineName: BlankToNull(medicine?.Name) ?? "Thuốc kê đơn",
                    Strength: BlankToNull(medicine?.Strength),
                    DosageForm: BlankToNull(medicine?.DosageForm),
                    PrescribedQuantity: item.PrescribedQuantity ?? 0m,
                    DispensingUnit: BlankToNull(medicine?.DispensingUnit ?? item.SingleDoseUnit),
                    DoseScheduleText: BlankToNull(item.DoseScheduleText),
                    Frequency: BlankToNull(item.Frequency),
                    DurationDays: item.DurationDays,
                    AdministrationRoute: BlankToNull(item.AdministrationRoute),
                    Instructions: BlankToNull(item.Instructions));
            })
            .ToList();

        var doctorName = ResolveDoctorDisplayName(prescription.PrescribingDoctorId, lookups);

        return new PatientMedicalRecordPrescriptionSummaryDto(
            PrescriptionId: prescription.Id,
            PrescriptionCode: BlankToNull(prescription.PrescriptionCode),
            Status: NormalizeOrDefault(prescription.Status, "ISSUED"),
            IssuedAt: prescription.IssuedAt ?? prescription.CreatedAt,
            PrescribingDoctorName: doctorName,
            ItemCount: itemDtos.Count,
            CanViewPrescriptionDetail: isSelfProfile,
            Items: itemDtos);
    }

    private static ResolvedVisitContext ResolveVisitContext(
        VisitRecordCandidate candidate,
        EmrLookups lookups)
    {
        Appointment? appointment = null;
        if (candidate.Visit.AppointmentId is long apptId)
            lookups.AppointmentsById.TryGetValue(apptId, out appointment);

        ExaminationSlot? slot = null;
        if (appointment?.SlotId is long slotId)
            lookups.SlotsById.TryGetValue(slotId, out slot);

        Service? slotService = null;
        if (slot?.ServiceId is long serviceId)
            lookups.ServicesById.TryGetValue(serviceId, out slotService);

        lookups.RevisionsByRecordId.TryGetValue(candidate.Record.Id, out var revision);

        var doctorId = candidate.Visit.CompletedByDoctorId
            ?? candidate.Visit.AssignedDoctorId
            ?? slot?.DoctorId
            ?? revision?.EditedByDoctorId
            ?? candidate.Record.CreatedByDoctorId;

        Doctor? doctor = null;
        if (doctorId is long docId)
            lookups.DoctorsById.TryGetValue(docId, out doctor);

        Staff? doctorStaff = null;
        if (doctor?.StaffId is long docStaffId)
            lookups.StaffById.TryGetValue(docStaffId, out doctorStaff);

        Staff? checkedInStaff = null;
        if (candidate.Visit.CheckedInByStaffId is long checkInStaffId)
            lookups.StaffById.TryGetValue(checkInStaffId, out checkedInStaff);

        HospitalRoom? room = null;
        var roomId = candidate.Visit.ExaminationRoomId ?? slot?.RoomId;
        if (roomId is long resolvedRoomId)
            lookups.RoomsById.TryGetValue(resolvedRoomId, out room);

        Department? department = null;
        var departmentId = room?.DepartmentId ?? slotService?.DepartmentId ?? doctorStaff?.DepartmentId;
        if (departmentId is long resolvedDepartmentId)
            lookups.DepartmentsById.TryGetValue(resolvedDepartmentId, out department);

        Specialty? specialty = null;
        var specialtyId = doctor?.SpecialtyId ?? slotService?.SpecialtyId;
        if (specialtyId is long resolvedSpecialtyId)
            lookups.SpecialtiesById.TryGetValue(resolvedSpecialtyId, out specialty);

        Hospital? hospital = null;
        var hospitalId = room?.HospitalId
            ?? department?.HospitalId
            ?? slotService?.HospitalId
            ?? doctor?.HospitalId
            ?? doctorStaff?.HospitalId
            ?? checkedInStaff?.HospitalId;
        if (hospitalId is long resolvedHospitalId)
            lookups.HospitalsById.TryGetValue(resolvedHospitalId, out hospital);

        var doctorName = ResolveDoctorDisplayName(doctorId, lookups);

        return new ResolvedVisitContext(
            appointment,
            slot,
            room,
            department,
            specialty,
            hospital,
            doctorName);
    }

    private static string? ResolveDoctorDisplayName(long? doctorId, EmrLookups lookups)
    {
        if (doctorId is not long id || !lookups.DoctorsById.TryGetValue(id, out var doctor))
            return null;

        lookups.StaffById.TryGetValue(doctor.StaffId ?? 0, out var staff);
        var formatted = DoctorCatalogService.FormatDisplayName(staff?.FullName, doctor.ProfessionalTitle ?? string.Empty);
        return BlankToNull(formatted) ?? BlankToNull(doctor.DoctorCode);
    }

    private static (string? IcdCode, string? NameVi) ResolveDiagnosisDisplay(
        VisitDiagnosis? diagnosis,
        IReadOnlyDictionary<long, Icd10Code> icd10ById)
    {
        if (diagnosis is null)
            return (null, null);

        Icd10Code? icd = null;
        if (diagnosis.Icd10CodeId is long icdId)
            icd10ById.TryGetValue(icdId, out icd);

        var code = BlankToNull(diagnosis.CodeSnapshot) ?? BlankToNull(icd?.Code);
        var name = BlankToNull(diagnosis.NameSnapshot)
            ?? BlankToNull(icd?.NameVi)
            ?? BlankToNull(diagnosis.ClinicalDiagnosis);

        return (code, name);
    }

    private static IEnumerable<ProjectedListEntry> ApplyFilters(
        IEnumerable<ProjectedListEntry> entries,
        NormalizedListFilter filter)
    {
        var query = entries;

        if (filter.HospitalId is long hospitalId)
            query = query.Where(e => e.Item.HospitalId == hospitalId);

        if (filter.SpecialtyId is long specialtyId)
            query = query.Where(e => e.Item.SpecialtyId == specialtyId);

        if (filter.SpecialtyCode is string specialtyCode)
        {
            query = query.Where(e =>
                string.Equals(e.Item.SpecialtyCode, specialtyCode, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.FromDate is DateOnly fromDate)
            query = query.Where(e => e.VisitDate.HasValue && e.VisitDate.Value >= fromDate);

        if (filter.ToDate is DateOnly toDate)
            query = query.Where(e => e.VisitDate.HasValue && e.VisitDate.Value <= toDate);

        if (filter.Keyword is string keyword)
        {
            query = query.Where(e =>
                ContainsIgnoreCase(e.Item.RecordCode, keyword) ||
                ContainsIgnoreCase(e.Item.VisitCode, keyword) ||
                ContainsIgnoreCase(e.Item.HospitalName, keyword) ||
                ContainsIgnoreCase(e.Item.DepartmentName, keyword) ||
                ContainsIgnoreCase(e.Item.SpecialtyName, keyword) ||
                ContainsIgnoreCase(e.Item.DoctorName, keyword) ||
                ContainsIgnoreCase(e.Item.PrimaryIcd10Code, keyword) ||
                ContainsIgnoreCase(e.Item.PrimaryDiagnosisName, keyword) ||
                ContainsIgnoreCase(e.Item.ClinicalConclusion, keyword) ||
                ContainsIgnoreCase(e.ReasonForVisit, keyword));
        }

        return query;
    }

    private async Task WriteAuditAsync(
        long actorUserId,
        string entityType,
        string entityId,
        object changeData,
        string? ipAddress,
        CancellationToken ct)
    {
        _auditLogger.Add(
            actorUserId,
            AuditActionRecordViewed,
            entityType,
            entityId,
            changeData,
            ipAddress);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (NotSupportedException)
        {
            // Minimal read-only unit test contexts may omit AuditLogs DbSet.
        }
    }

    private static bool IsVisitVisibleToPatient(Visit visit)
        => AppointmentBookingRules.IsCompletedVisitStatus(visit.Status);

    private static bool IsRecordFinalized(MedicalRecord record)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(record.Status);
        if (normalized == "CANCELLED")
            return false;

        return normalized == "FINALIZED" || record.FinalizedAt.HasValue;
    }

    private static MedicalRecord? ChoosePrimaryRecord(IEnumerable<MedicalRecord> records)
        => records
            .OrderByDescending(IsRecordFinalized)
            .ThenByDescending(r => r.FinalizedAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

    private static MedicalRecordRevision? ChooseConfirmedRevision(IEnumerable<MedicalRecordRevision> revisions)
    {
        var list = revisions.ToList();
        if (list.Count == 0)
            return null;

        var confirmed = list
            .Where(r => r.IsConfirmed == true)
            .OrderByDescending(r => r.RevisionNo ?? 0)
            .ThenByDescending(r => r.RecordedAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

        return confirmed ?? list
            .OrderByDescending(r => r.RevisionNo ?? 0)
            .ThenByDescending(r => r.RecordedAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
    }

    internal static PatientMedicalRecordVitalSignsDto? ParseVitalSigns(string? vitalSignsJson)
    {
        if (string.IsNullOrWhiteSpace(vitalSignsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(vitalSignsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var sys = TryGetInt(root, "bp_systolic_mmhg") ?? TryGetInt(root, "bpSystolicMmhg");
            var dia = TryGetInt(root, "bp_diastolic_mmhg") ?? TryGetInt(root, "bpDiastolicMmhg");
            var pulse = TryGetInt(root, "pulse_bpm") ?? TryGetInt(root, "pulseBpm");
            var temp = TryGetDecimal(root, "temperature_c") ?? TryGetDecimal(root, "temperatureC");
            var spo2 = TryGetInt(root, "spo2_percent") ?? TryGetInt(root, "spo2Percent");
            var heightCm = TryGetDecimal(root, "height_cm") ?? TryGetDecimal(root, "heightCm");
            var weightKg = TryGetDecimal(root, "weight_kg") ?? TryGetDecimal(root, "weightKg");

            if (sys is null && dia is null && pulse is null && temp is null &&
                spo2 is null && heightCm is null && weightKg is null)
            {
                return null;
            }

            var bpLabel = sys.HasValue && dia.HasValue
                ? $"{sys.Value}/{dia.Value} mmHg"
                : null;

            decimal? bmi = null;
            if (heightCm is > 0 && weightKg is > 0)
            {
                var heightM = heightCm.Value / 100m;
                bmi = Math.Round(weightKg.Value / (heightM * heightM), 1, MidpointRounding.AwayFromZero);
            }

            return new PatientMedicalRecordVitalSignsDto(
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

    private static int? TryGetInt(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
            return value;

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
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var value))
            return value;

        if (prop.ValueKind == JsonValueKind.String &&
            decimal.TryParse(prop.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static NormalizedListFilter NormalizeListRequest(GetPatientMedicalRecordsRequest? request)
    {
        var page = request?.Page ?? 1;
        var pageSize = request?.PageSize ?? 20;
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize || page > int.MaxValue / pageSize)
            throw new ValidationException("Tham số phân trang không hợp lệ.");

        if (request?.PatientId is <= 0)
            throw new ValidationException("Mã hồ sơ thành viên không hợp lệ.");

        if (request?.HospitalId is <= 0)
            throw new ValidationException("Mã cơ sở y tế không hợp lệ.");

        if (request?.SpecialtyId is <= 0)
            throw new ValidationException("Mã chuyên khoa không hợp lệ.");

        string? specialtyCode = null;
        if (!string.IsNullOrWhiteSpace(request?.SpecialtyCode))
        {
            var trimmedSpecialty = request.SpecialtyCode.Trim();
            if (!string.Equals(trimmedSpecialty, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmedSpecialty.Length > MaxIdentifierLength ||
                    !trimmedSpecialty.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
                {
                    throw new ValidationException("Mã chuyên khoa không hợp lệ.");
                }

                specialtyCode = trimmedSpecialty.ToUpperInvariant();
            }
        }

        DateOnly? fromDate = null;
        if (!string.IsNullOrWhiteSpace(request?.FromDate))
        {
            if (!DateOnly.TryParseExact(
                    request.FromDate.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedFrom))
            {
                throw new ValidationException("Ngày bắt đầu không hợp lệ (định dạng YYYY-MM-DD).");
            }

            fromDate = parsedFrom;
        }

        DateOnly? toDate = null;
        if (!string.IsNullOrWhiteSpace(request?.ToDate))
        {
            if (!DateOnly.TryParseExact(
                    request.ToDate.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedTo))
            {
                throw new ValidationException("Ngày kết thúc không hợp lệ (định dạng YYYY-MM-DD).");
            }

            toDate = parsedTo;
        }

        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            throw new ValidationException("Ngày bắt đầu không được lớn hơn ngày kết thúc.");

        string? keyword = null;
        if (!string.IsNullOrWhiteSpace(request?.Keyword))
        {
            keyword = request.Keyword.Trim();
            if (keyword.Length > MaxKeywordLength || keyword.Contains('<') || keyword.Contains('>'))
                throw new ValidationException("Từ khóa tìm kiếm không hợp lệ.");
        }

        return new NormalizedListFilter(
            page,
            pageSize,
            request?.PatientId,
            fromDate,
            toDate,
            request?.HospitalId,
            request?.SpecialtyId,
            specialtyCode,
            keyword);
    }

    private static RecordIdentifier ParseIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException("Mã hồ sơ bệnh án không hợp lệ.");

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].TrimStart();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxIdentifierLength)
            throw new ValidationException("Mã hồ sơ bệnh án không hợp lệ.");

        if (trimmed.All(char.IsAsciiDigit) ||
            (trimmed.StartsWith('-') && trimmed.Length > 1 && trimmed[1..].All(char.IsAsciiDigit)))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId) ||
                numericId <= 0)
            {
                throw new ValidationException("Mã hồ sơ bệnh án không hợp lệ.");
            }

            return new RecordIdentifier(numericId, null);
        }

        if (trimmed.StartsWith('-') ||
            !trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
        {
            throw new ValidationException("Mã hồ sơ bệnh án không hợp lệ.");
        }

        var upper = trimmed.ToUpperInvariant();
        if (upper.StartsWith("EMR-", StringComparison.Ordinal) &&
            upper.Length > 4 &&
            upper[4..].All(char.IsAsciiDigit) &&
            long.TryParse(upper[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var emrId) &&
            emrId > 0)
        {
            return new RecordIdentifier(emrId, upper);
        }

        return new RecordIdentifier(null, upper);
    }

    private static string FormatRecordCode(long medicalRecordId)
        => $"EMR-{medicalRecordId.ToString(CultureInfo.InvariantCulture)}";

    private static string NormalizeOrDefault(string? status, string fallback)
        => AppointmentBookingRules.NormalizeStatusKey(status) is { Length: > 0 } normalized
            ? normalized
            : fallback;

    private static bool ContainsIgnoreCase(string? source, string term)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string? BlankToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<List<T>> TryQueryListAsync<T>(Func<Task<List<T>>> query)
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

    private sealed record ScopedPhiMember(AppointmentReadModel.ScopeMember Scope, bool IsSelf);

    private sealed record PhiScopeContext(long SelfId, IReadOnlyList<ScopedPhiMember> Members);

    private sealed record VisitRecordCandidate(Visit Visit, MedicalRecord Record);

    private sealed record RecordIdentifier(long? RecordId, string? VisitCode);

    private sealed record NormalizedListFilter(
        int Page,
        int PageSize,
        long? PatientId,
        DateOnly? FromDate,
        DateOnly? ToDate,
        long? HospitalId,
        long? SpecialtyId,
        string? SpecialtyCode,
        string? Keyword);

    private sealed record ProjectedListEntry(
        PatientMedicalRecordListItemDto Item,
        DateOnly? VisitDate,
        DateTime SortTimestamp,
        string? HospitalCode,
        string? ReasonForVisit);

    private sealed record ResolvedVisitContext(
        Appointment? Appointment,
        ExaminationSlot? Slot,
        HospitalRoom? Room,
        Department? Department,
        Specialty? Specialty,
        Hospital? Hospital,
        string? DoctorName);

    private sealed record EmrLookups(
        IReadOnlyDictionary<long, MedicalRecordRevision?> RevisionsByRecordId,
        IReadOnlyDictionary<long, IReadOnlyList<VisitDiagnosis>> DiagnosesByVisitId,
        IReadOnlyDictionary<long, Icd10Code> Icd10ById,
        IReadOnlyDictionary<long, IReadOnlyList<DiagnosticOrder>> OrdersByVisitId,
        IReadOnlyDictionary<long, DiagnosticResult> ResultByOrderId,
        IReadOnlyDictionary<long, IReadOnlyList<DiagnosticResultVersion>> VersionsByResultId,
        IReadOnlyDictionary<long, IReadOnlyList<Prescription>> PrescriptionsByVisitId,
        IReadOnlyDictionary<long, IReadOnlyList<PrescriptionItem>> ItemsByPrescriptionId,
        IReadOnlyDictionary<long, Medicine> MedicinesById,
        IReadOnlyDictionary<long, Appointment> AppointmentsById,
        IReadOnlyDictionary<long, ExaminationSlot> SlotsById,
        IReadOnlyDictionary<long, Service> ServicesById,
        IReadOnlyDictionary<long, HospitalRoom> RoomsById,
        IReadOnlyDictionary<long, Doctor> DoctorsById,
        IReadOnlyDictionary<long, Staff> StaffById,
        IReadOnlyDictionary<long, Department> DepartmentsById,
        IReadOnlyDictionary<long, Specialty> SpecialtiesById,
        IReadOnlyDictionary<long, Hospital> HospitalsById);
}
