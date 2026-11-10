using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Visits;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Consultation;

public sealed class ConsultationWorkspaceService : IConsultationWorkspaceService
{
    private const string DoctorRoleCode = "DOCTOR";
    private const string PermissionMedicalRecordWrite = "MEDICAL_RECORD_WRITE";
    private const string PermissionDiagnosticOrder = "DIAGNOSTIC_ORDER";
    private const string PermissionPrescriptionIssue = "PRESCRIPTION_ISSUE";

    private static readonly string[] FallbackDoctorPermissions =
    [
        PermissionDiagnosticOrder,
        PermissionMedicalRecordWrite,
        "PAYMENT_VIEW",
        PermissionPrescriptionIssue,
    ];

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Regex Icd10CodeRegex = new(
        @"^[A-Z][0-9]{2}(\.[0-9A-Z]{1,4})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ConsultationWorkspaceService(
        IApplicationDbContext db,
        TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<ConsultationWorkspaceResponseDto> GetWorkspaceAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (numericId, code, isQueuePrefix) = ParseVisitOrQueueIdentifier(visitIdOrCode);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var doctorAccess = await EnsureDoctorAccessAsync(actorUserId, nowUtc, requiredPermission: null, ct);
        var context = await ResolveAndAuthorizeVisitContextAsync(
            doctorAccess,
            numericId,
            code,
            isQueuePrefix,
            visitIdOrCode,
            ct);

        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(context.Visit.Status);
        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Không thể xem không gian khám của lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (context.ActiveClinicalEntry is not null &&
            NormalizeQueueStatus(context.ActiveClinicalEntry.Status) == "CANCELLED")
        {
            throw new ConflictException("Lượt xếp hàng của ca khám này đã bị hủy.");
        }

        return await BuildWorkspaceResponseAsync(
            doctorAccess,
            context,
            actionOutcome: "VIEWED",
            nowUtc,
            nowLocal,
            ct);
    }

    public async Task<ConsultationWorkspaceResponseDto> OpenWorkspaceAsync(
        long actorUserId,
        string visitIdOrCode,
        OpenConsultationWorkspaceRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (numericId, code, isQueuePrefix) = ParseVisitOrQueueIdentifier(visitIdOrCode);
        var trimmedNotes = ValidateOpenRequest(request);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var doctorAccess = await EnsureDoctorAccessAsync(actorUserId, nowUtc, PermissionMedicalRecordWrite, ct);

        await using var tx = await _db.BeginTransactionAsync(ct);

        var context = await ResolveAndAuthorizeVisitContextAsync(
            doctorAccess,
            numericId,
            code,
            isQueuePrefix,
            visitIdOrCode,
            ct);

        var visit = context.Visit;
        var activeEntry = context.ActiveClinicalEntry;
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);

        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Không thể mở không gian khám cho lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (visitStatus is "COMPLETED" or "DONE" ||
            (activeEntry is not null && NormalizeQueueStatus(activeEntry.Status) == "COMPLETED"))
        {
            throw new ConflictException("Lượt khám đã hoàn tất. Vui lòng sử dụng chế độ xem hồ sơ khám (GET) thay vì mở phiên khám mới.");
        }

        if (activeEntry is null || NormalizeQueueStatus(activeEntry.Status) == "CANCELLED")
        {
            throw new ConflictException("Lượt khám chưa có số thứ tự hàng đợi lâm sàng hợp lệ để bắt đầu khám.");
        }

        var currentStateVersion = activeEntry.StateVersion is > 0 ? activeEntry.StateVersion.Value : 1;
        if (request?.ExpectedStateVersion.HasValue == true &&
            request.ExpectedStateVersion.Value != currentStateVersion)
        {
            throw new ConflictException("Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        if (!visit.ExaminationRoomId.HasValue &&
            !activeEntry.ServicePointId.HasValue &&
            !activeEntry.CurrentServicePointId.HasValue)
        {
            throw new ConflictException("Lượt khám chưa được gán phòng khám lâm sàng, không thể bắt đầu khám.");
        }

        if (context.Room is not null &&
            !string.Equals(context.Room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Phòng khám của lượt khám hiện không hoạt động hoặc đang bảo trì.");
        }

        if (context.ServicePoint is not null && context.ServicePoint.IsActive == false)
        {
            throw new ConflictException("Điểm phục vụ của buồng khám hiện đang ngừng hoạt động.");
        }

        var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, doctorAccess.Hospital.Id, nowUtc, ct);
        var servicePointIdForPolicy = activeEntry.CurrentServicePointId ?? activeEntry.ServicePointId ?? context.ServicePoint?.Id;
        var policy = policySet.ForServicePoint(servicePointIdForPolicy);

        var openDiagnosticCount = CountOpenDiagnosticEntries(context.AllVisitQueueEntries);
        var resolvedQueueType = ClinicalQueueRules.ResolveQueueType(activeEntry.QueueType, visit.Status);

        if (resolvedQueueType == ClinicalQueueRules.QueueTypeReturnConsultation &&
            policy.FinalResultRequired &&
            openDiagnosticCount > 0)
        {
            throw new ConflictException("Chưa thể mở phiên khám lại vì kết quả cận lâm sàng bắt buộc chưa sẵn sàng.");
        }

        var presenceStatus = string.IsNullOrWhiteSpace(activeEntry.PresenceStatus)
            ? "PRESENT"
            : activeEntry.PresenceStatus.Trim().ToUpperInvariant();

        if (presenceStatus is "NOT_ARRIVED" or "LEFT")
        {
            throw new ConflictException("Không thể bắt đầu khám khi bệnh nhân chưa có mặt tại phòng khám.");
        }

        var currentQueueStatus = NormalizeQueueStatus(activeEntry.Status);
        string actionOutcome;

        if (currentQueueStatus == "IN_PROGRESS")
        {
            actionOutcome = "ALREADY_IN_PROGRESS";
            var mutated = false;

            if (!string.Equals(visit.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
            {
                visit.Status = "IN_PROGRESS";
                mutated = true;
            }

            var (_, createdRecord) = await EnsureDraftMedicalRecordAsync(
                visit,
                context.Appointment,
                doctorAccess.Doctor.Id,
                nowLocal,
                ct);

            if (mutated || createdRecord)
            {
                await SaveChangesWithConcurrencyGuardAsync(ct);
            }

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }
        }
        else if (currentQueueStatus == "CALLED")
        {
            await EnsureNoOtherActiveInProgressEntryAsync(activeEntry, visit, ct);

            var previousQueueStatus = activeEntry.Status ?? "CALLED";
            var previousVisitStatus = visit.Status ?? "CALLED";

            activeEntry.Status = "IN_PROGRESS";
            activeEntry.ArrivedAt ??= visit.CheckedInAt ?? nowLocal;
            activeEntry.ReadyAt ??= nowLocal;
            activeEntry.CalledAt ??= nowLocal;
            activeEntry.StartedAt = nowLocal;
            activeEntry.PresenceStatus = "PRESENT";
            activeEntry.BlockedReason = null;
            activeEntry.StateVersion = currentStateVersion + 1;

            visit.Status = "IN_PROGRESS";
            visit.AssignedDoctorId ??= doctorAccess.Doctor.Id;
            visit.ExaminationRoomId ??= context.Room?.Id;

            var (medicalRecord, _) = await EnsureDraftMedicalRecordAsync(
                visit,
                context.Appointment,
                doctorAccess.Doctor.Id,
                nowLocal,
                ct);

            var changePayload = new
            {
                visitId = visit.Id,
                visitCode = visit.VisitCode,
                queueEntryId = activeEntry.Id,
                previousQueueStatus,
                targetQueueStatus = "IN_PROGRESS",
                previousVisitStatus,
                targetVisitStatus = "IN_PROGRESS",
                previousStateVersion = currentStateVersion,
                stateVersion = activeEntry.StateVersion,
                medicalRecordId = medicalRecord?.Id,
                ipAddress,
            };

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = doctorAccess.Hospital.Id,
                Action = "CONSULTATION_WORKSPACE_OPENED",
                EntityType = "VISIT",
                EntityId = !string.IsNullOrWhiteSpace(visit.VisitCode)
                    ? visit.VisitCode.Trim()
                    : visit.Id.ToString(CultureInfo.InvariantCulture),
                Reason = trimmedNotes ?? "Bác sĩ mở không gian khám lâm sàng",
                ChangeData = JsonSerializer.Serialize(changePayload, AuditJsonOptions),
                IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                OccurredAt = nowUtc,
            });

            await SaveChangesWithConcurrencyGuardAsync(ct);

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }

            actionOutcome = "OPENED";
        }
        else
        {
            throw new ConflictException(
                $"Lượt khám hiện đang ở trạng thái {currentQueueStatus}. Vui lòng gọi bệnh nhân vào phòng (CALLED) trước khi mở phiên khám lâm sàng.");
        }

        return await BuildWorkspaceResponseAsync(
            doctorAccess,
            context,
            actionOutcome,
            nowUtc,
            nowLocal,
            ct);
    }

    public async Task<ConsultationWorkspaceResponseDto> RecordClinicalExaminationAsync(
        long actorUserId,
        string visitIdOrCode,
        RecordClinicalExaminationRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (numericId, code, isQueuePrefix) = ParseVisitOrQueueIdentifier(visitIdOrCode);
        var validated = ValidateRecordClinicalExaminationRequest(request);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var doctorAccess = await EnsureDoctorAccessAsync(actorUserId, nowUtc, PermissionMedicalRecordWrite, ct);

        await using var tx = await _db.BeginTransactionAsync(ct);

        var context = await ResolveAndAuthorizeVisitContextAsync(
            doctorAccess,
            numericId,
            code,
            isQueuePrefix,
            visitIdOrCode,
            ct);

        var visit = context.Visit;
        var activeEntry = context.ActiveClinicalEntry;
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);

        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Không thể ghi nhận khám lâm sàng cho lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (visitStatus is "COMPLETED" or "DONE" ||
            (activeEntry is not null && NormalizeQueueStatus(activeEntry.Status) == "COMPLETED"))
        {
            throw new ConflictException("Lượt khám đã hoàn tất (COMPLETED), không thể chỉnh sửa khám lâm sàng trực tiếp.");
        }

        if (activeEntry is null || NormalizeQueueStatus(activeEntry.Status) == "CANCELLED")
        {
            throw new ConflictException("Lượt khám chưa có số thứ tự hàng đợi lâm sàng hợp lệ để ghi nhận khám.");
        }

        var currentQueueStatus = NormalizeQueueStatus(activeEntry.Status);
        var openDiagnosticCount = CountOpenDiagnosticEntries(context.AllVisitQueueEntries);

        if (visitStatus is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS" ||
            (currentQueueStatus != "IN_PROGRESS" && openDiagnosticCount > 0))
        {
            throw new ConflictException(
                "Bệnh nhân đang chờ kết quả cận lâm sàng. Vui lòng mở lại phiên khám (IN_PROGRESS) trước khi cập nhật khám lâm sàng.");
        }

        if (currentQueueStatus != "IN_PROGRESS" ||
            visitStatus is not ("IN_PROGRESS" or "IN_CONSULTATION" or "SERVING"))
        {
            var effectiveStatus = currentQueueStatus != "IN_PROGRESS" ? currentQueueStatus : visitStatus;
            throw new ConflictException(
                $"Lượt khám hiện đang ở trạng thái {effectiveStatus}. Vui lòng bắt đầu phiên khám lâm sàng (IN_PROGRESS) trước khi ghi nhận thông tin khám.");
        }

        if (context.Room is not null &&
            !string.Equals(context.Room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Phòng khám của lượt khám hiện không hoạt động hoặc đang bảo trì.");
        }

        if (context.ServicePoint is not null && context.ServicePoint.IsActive == false)
        {
            throw new ConflictException("Điểm phục vụ của buồng khám hiện đang ngừng hoạt động.");
        }

        var currentStateVersion = activeEntry.StateVersion is > 0 ? activeEntry.StateVersion.Value : 1;
        if (request.ExpectedStateVersion.HasValue &&
            request.ExpectedStateVersion.Value != currentStateVersion)
        {
            throw new ConflictException(
                "Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        var medicalRecord = await _db.MedicalRecords
            .Where(m => m.VisitId == visit.Id)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);

        if (medicalRecord is not null &&
            string.Equals(medicalRecord.Status?.Trim(), "FINALIZED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "Hồ sơ bệnh án đã được khóa hoàn tất (FINALIZED), không thể ghi nhận thêm phiên bản nháp.");
        }

        if (medicalRecord is null)
        {
            medicalRecord = new MedicalRecord
            {
                VisitId = visit.Id,
                CreatedByDoctorId = doctorAccess.Doctor.Id,
                Status = "DRAFT",
                FinalizedAt = null,
            };
            _db.MedicalRecords.Add(medicalRecord);
            await SaveChangesWithConcurrencyGuardAsync(ct);
        }

        var latestRevision = await _db.MedicalRecordRevisions
            .AsNoTracking()
            .Where(r => r.MedicalRecordId == medicalRecord.Id)
            .OrderByDescending(r => r.RevisionNo)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var currentRevisionNo = latestRevision?.RevisionNo ?? 0;
        if (request.ExpectedRevisionNo.HasValue &&
            request.ExpectedRevisionNo.Value != currentRevisionNo)
        {
            throw new ConflictException(
                "Hồ sơ khám lâm sàng đã có phiên bản mới hơn (revision_no mismatch). Vui lòng tải lại trước khi lưu.");
        }

        var previousVitals = ParseVitalSigns(latestRevision?.VitalSigns);
        var bpSystolic = validated.BpSystolicMmhg ?? previousVitals.BpSystolicMmhg;
        var bpDiastolic = validated.BpDiastolicMmhg ?? previousVitals.BpDiastolicMmhg;
        var pulseBpm = validated.PulseBpm ?? previousVitals.PulseBpm;
        var temperatureC = validated.TemperatureC ?? previousVitals.TemperatureC;
        var spo2Percent = validated.Spo2Percent ?? previousVitals.Spo2Percent;
        var heightCm = validated.HeightCm ?? previousVitals.HeightCm;
        var weightKg = validated.WeightKg ?? previousVitals.WeightKg;

        var vitalSignsJson = SerializeVitalSigns(
            bpSystolic,
            bpDiastolic,
            pulseBpm,
            temperatureC,
            spo2Percent,
            heightCm,
            weightKg);

        var presentingComplaint = validated.PresentingComplaint
            ?? latestRevision?.PresentingComplaint
            ?? visit.ReasonForVisit
            ?? context.Appointment?.ReasonForVisit;
        var symptomHistory = validated.SymptomHistory ?? latestRevision?.SymptomHistory;
        var relevantHistoryAllergies = validated.RelevantHistoryAllergies ?? latestRevision?.RelevantHistoryAllergies;
        var clinicalFindings = validated.ClinicalFindings ?? latestRevision?.ClinicalFindings;
        var clinicalConclusion = validated.ClinicalConclusion ?? latestRevision?.ClinicalConclusion;

        var nextRevisionNo = currentRevisionNo + 1;
        var newRevision = new MedicalRecordRevision
        {
            MedicalRecordId = medicalRecord.Id,
            RevisionNo = nextRevisionNo,
            PresentingComplaint = presentingComplaint,
            SymptomHistory = symptomHistory,
            RelevantHistoryAllergies = relevantHistoryAllergies,
            VitalSigns = vitalSignsJson,
            ClinicalFindings = clinicalFindings,
            ClinicalConclusion = clinicalConclusion,
            EditedByDoctorId = doctorAccess.Doctor.Id,
            RecordedAt = nowLocal,
            IsConfirmed = request.IsConfirmed ?? true,
            AmendmentReason = validated.AmendmentReason,
        };
        _db.MedicalRecordRevisions.Add(newRevision);

        if (!string.Equals(visit.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            visit.Status = "IN_PROGRESS";
        }

        if (!string.Equals(activeEntry.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            activeEntry.Status = "IN_PROGRESS";
        }

        var changePayload = new
        {
            visitId = visit.Id,
            visitCode = visit.VisitCode,
            queueEntryId = activeEntry.Id,
            medicalRecordId = medicalRecord.Id,
            previousRevisionNo = currentRevisionNo > 0 ? (int?)currentRevisionNo : null,
            revisionNo = nextRevisionNo,
            isConfirmed = newRevision.IsConfirmed,
            hasVitalSigns = vitalSignsJson is not null,
            stateVersion = currentStateVersion,
            ipAddress,
        };

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = doctorAccess.Hospital.Id,
            Action = "CLINICAL_EXAMINATION_RECORDED",
            EntityType = "MEDICAL_RECORD",
            EntityId = medicalRecord.Id.ToString(CultureInfo.InvariantCulture),
            Reason = validated.AmendmentReason ?? "Bác sĩ ghi nhận khám lâm sàng và sinh hiệu",
            ChangeData = JsonSerializer.Serialize(changePayload, AuditJsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        });

        await SaveChangesWithConcurrencyGuardAsync(ct);

        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return await BuildWorkspaceResponseAsync(
            doctorAccess,
            context,
            actionOutcome: "CLINICAL_EXAMINATION_RECORDED",
            nowUtc,
            nowLocal,
            ct);
    }

    public async Task<DiagnosticOrderCatalogResponseDto> GetDiagnosticOrderCatalogAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (numericId, code, isQueuePrefix) = ParseVisitOrQueueIdentifier(visitIdOrCode);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var doctorAccess = await EnsureDoctorAccessAsync(actorUserId, nowUtc, requiredPermission: null, ct);
        var context = await ResolveAndAuthorizeVisitContextAsync(
            doctorAccess,
            numericId,
            code,
            isQueuePrefix,
            visitIdOrCode,
            ct);

        var visit = context.Visit;
        var activeEntry = context.ActiveClinicalEntry;
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);

        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException(
                "Không thể xem danh mục chỉ định cận lâm sàng của lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (activeEntry is not null && NormalizeQueueStatus(activeEntry.Status) == "CANCELLED")
        {
            throw new ConflictException("Lượt xếp hàng của ca khám này đã bị hủy.");
        }

        var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, doctorAccess.Hospital.Id, nowUtc, ct);
        var servicePointIdForPolicy = activeEntry?.CurrentServicePointId
            ?? activeEntry?.ServicePointId
            ?? context.ServicePoint?.Id;
        var policy = policySet.ForServicePoint(servicePointIdForPolicy);

        var visitDiagnosticOrders = await TryLoadVisitDiagnosticOrdersAsync(visit.Id, ct);
        var diagnosticSummary = BuildDiagnosticSummary(
            context.AllVisitQueueEntries,
            visitDiagnosticOrders,
            policy.FinalResultRequired);

        var workspaceState = ResolveWorkspaceState(visit, activeEntry, diagnosticSummary.OpenQueueEntries);
        var latestMedicalRecord = await TryLoadLatestMedicalRecordAsync(visit.Id, ct);
        var isRecordFinalized = string.Equals(
            latestMedicalRecord?.Status?.Trim(),
            "FINALIZED",
            StringComparison.OrdinalIgnoreCase);

        var hasMedicalRecordWrite = doctorAccess.Permissions.Contains(PermissionMedicalRecordWrite);
        var hasDiagnosticOrder = doctorAccess.Permissions.Contains(PermissionDiagnosticOrder);
        var canEditClinical = workspaceState == "IN_PROGRESS" && !isRecordFinalized && hasMedicalRecordWrite;
        var clinicalDisabledReason = canEditClinical
            ? null
            : ResolveReadOnlyReason(canEditClinical, workspaceState, isRecordFinalized, hasMedicalRecordWrite);

        var canCreateDiagnosticOrder = workspaceState == "IN_PROGRESS" && !isRecordFinalized && hasDiagnosticOrder;
        var disabledReason = canCreateDiagnosticOrder
            ? null
            : !hasDiagnosticOrder
                ? "Bác sĩ không có quyền chỉ định cận lâm sàng (DIAGNOSTIC_ORDER)."
                : clinicalDisabledReason ?? "Chỉ có thể chỉ định cận lâm sàng khi ca khám đang diễn ra (IN_PROGRESS).";

        var referenceDate = activeEntry?.QueueDate
            ?? (visit.CheckedInAt.HasValue
                ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
                : DateOnly.FromDateTime(nowLocal));

        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode.Trim()
            : $"VIS-{referenceDate:yyyyMM}-{visit.Id:D4}";

        var diagnosticServices = await _db.Services
            .AsNoTracking()
            .Where(s =>
                (s.HospitalId == doctorAccess.Hospital.Id || s.HospitalId == null) &&
                s.ServiceType != null &&
                s.ServiceType.ToUpper() == "DIAGNOSTIC" &&
                s.IsActive != false)
            .OrderBy(s => s.ServiceCode)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        var departmentsById = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == doctorAccess.Hospital.Id || d.HospitalId == null)
            .ToDictionaryAsync(d => d.Id, ct);

        var specialtiesById = await _db.Specialties
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, ct);

        var hospitalDiagPoints = await TryLoadHospitalDiagnosticServicePointsAsync(doctorAccess.Hospital.Id, ct);
        var roomsById = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId == doctorAccess.Hospital.Id || r.HospitalId == null)
            .ToDictionaryAsync(r => r.Id, ct);

        var activeOrderServiceIds = visitDiagnosticOrders
            .Where(o => o.ServiceId.HasValue && IsDiagnosticOrderActive(o, context.AllVisitQueueEntries))
            .Select(o => o.ServiceId!.Value)
            .ToHashSet();

        var catalogItems = new List<DiagnosticServiceCatalogItemDto>(diagnosticServices.Count);
        foreach (var svc in diagnosticServices)
        {
            var dept = svc.DepartmentId.HasValue && departmentsById.TryGetValue(svc.DepartmentId.Value, out var d)
                ? d
                : null;
            if (dept is not null && dept.IsActive == false)
            {
                continue;
            }

            var spec = svc.SpecialtyId.HasValue && specialtiesById.TryGetValue(svc.SpecialtyId.Value, out var sp)
                ? sp
                : null;

            var unitPrice = await ResolveActiveServicePriceAsync(svc.Id, nowLocal, ct);
            var isAuthorized = await IsDoctorAuthorizedForServiceAsync(doctorAccess.Doctor.Id, svc.Id, ct);
            var diagSp = ResolveDiagnosticServicePointForCatalog(svc.Id, hospitalDiagPoints);
            var diagRoom = diagSp?.RoomId.HasValue == true && roomsById.TryGetValue(diagSp.RoomId.Value, out var rm)
                ? rm
                : null;

            catalogItems.Add(new DiagnosticServiceCatalogItemDto(
                ServiceId: svc.Id,
                ServiceCode: svc.ServiceCode,
                Name: svc.Name,
                Description: svc.Description,
                Category: ResolveDiagnosticCategory(svc, specialtiesById),
                DepartmentId: dept?.Id ?? svc.DepartmentId,
                DepartmentCode: dept?.DepartmentCode,
                DepartmentName: dept?.Name ?? "Khoa Cận lâm sàng",
                SpecialtyId: spec?.Id ?? svc.SpecialtyId,
                SpecialtyCode: spec?.SpecialtyCode,
                SpecialtyName: spec?.Name ?? "Cận lâm sàng",
                UnitPrice: unitPrice,
                RequiresResultVerification: svc.RequiresResultVerification ?? true,
                IsDoctorAuthorized: isAuthorized,
                ServicePointId: diagSp?.Id,
                ServicePointName: diagSp?.Name,
                RoomId: diagRoom?.Id ?? diagSp?.RoomId,
                RoomCode: diagRoom?.RoomCode,
                RoomName: diagRoom?.Name,
                HasActiveOrderInVisit: activeOrderServiceIds.Contains(svc.Id)));
        }

        var existingOrderDtos = await BuildDiagnosticOrderDtosAsync(
            visit,
            visitCode,
            referenceDate,
            context.AssignedDoctor,
            context.AllVisitQueueEntries,
            visitDiagnosticOrders,
            nowLocal,
            ct);

        return new DiagnosticOrderCatalogResponseDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            VisitStatus: NormalizeVisitDisplayStatus(visit.Status, activeEntry?.Status),
            WorkspaceState: workspaceState,
            CanCreateDiagnosticOrder: canCreateDiagnosticOrder,
            DisabledReason: disabledReason,
            GeneratedAtUtc: nowUtc,
            DiagnosticSummary: diagnosticSummary,
            Services: catalogItems,
            ExistingOrders: existingOrderDtos);
    }

    public async Task<ConsultationWorkspaceResponseDto> CreateDiagnosticOrderAsync(
        long actorUserId,
        string visitIdOrCode,
        CreateDiagnosticOrderRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (numericId, code, isQueuePrefix) = ParseVisitOrQueueIdentifier(visitIdOrCode);
        var validated = ValidateCreateDiagnosticOrderRequest(request);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var doctorAccess = await EnsureDoctorAccessAsync(actorUserId, nowUtc, PermissionDiagnosticOrder, ct);

        await using var tx = await _db.BeginTransactionAsync(ct);

        var context = await ResolveAndAuthorizeVisitContextAsync(
            doctorAccess,
            numericId,
            code,
            isQueuePrefix,
            visitIdOrCode,
            ct);

        var visit = context.Visit;
        var activeEntry = context.ActiveClinicalEntry;
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);

        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException(
                "Không thể chỉ định cận lâm sàng cho lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (visitStatus is "COMPLETED" or "DONE" ||
            (activeEntry is not null && NormalizeQueueStatus(activeEntry.Status) == "COMPLETED"))
        {
            throw new ConflictException(
                "Lượt khám đã hoàn tất (COMPLETED), không thể tạo thêm chỉ định cận lâm sàng.");
        }

        if (activeEntry is null || NormalizeQueueStatus(activeEntry.Status) == "CANCELLED")
        {
            throw new ConflictException(
                "Lượt khám chưa có số thứ tự hàng đợi lâm sàng hợp lệ để chỉ định cận lâm sàng.");
        }

        var currentQueueStatus = NormalizeQueueStatus(activeEntry.Status);
        var openDiagnosticCount = CountOpenDiagnosticEntries(context.AllVisitQueueEntries);

        if (visitStatus is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS" ||
            (currentQueueStatus != "IN_PROGRESS" && openDiagnosticCount > 0))
        {
            throw new ConflictException(
                "Bệnh nhân đang chờ kết quả cận lâm sàng. Vui lòng mở lại phiên khám (IN_PROGRESS) trước khi chỉ định bổ sung.");
        }

        if (currentQueueStatus != "IN_PROGRESS" ||
            visitStatus is not ("IN_PROGRESS" or "IN_CONSULTATION" or "SERVING"))
        {
            var effectiveStatus = currentQueueStatus != "IN_PROGRESS" ? currentQueueStatus : visitStatus;
            throw new ConflictException(
                $"Lượt khám hiện đang ở trạng thái {effectiveStatus}. Vui lòng bắt đầu phiên khám lâm sàng (IN_PROGRESS) trước khi chỉ định cận lâm sàng.");
        }

        if (context.Room is not null &&
            !string.Equals(context.Room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Phòng khám của lượt khám hiện không hoạt động hoặc đang bảo trì.");
        }

        if (context.ServicePoint is not null && context.ServicePoint.IsActive == false)
        {
            throw new ConflictException("Điểm phục vụ của buồng khám hiện đang ngừng hoạt động.");
        }

        var currentStateVersion = activeEntry.StateVersion is > 0 ? activeEntry.StateVersion.Value : 1;
        if (validated.ExpectedStateVersion.HasValue &&
            validated.ExpectedStateVersion.Value != currentStateVersion)
        {
            throw new ConflictException(
                "Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        var latestMedicalRecord = await TryLoadLatestMedicalRecordAsync(visit.Id, ct);
        if (latestMedicalRecord is not null &&
            string.Equals(latestMedicalRecord.Status?.Trim(), "FINALIZED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "Hồ sơ bệnh án đã được khóa hoàn tất (FINALIZED), không thể tạo thêm chỉ định cận lâm sàng.");
        }

        if (latestMedicalRecord is null)
        {
            await EnsureDraftMedicalRecordAsync(
                visit,
                context.Appointment,
                doctorAccess.Doctor.Id,
                nowLocal,
                ct);
        }

        var existingVisitOrders = await TryLoadVisitDiagnosticOrdersAsync(visit.Id, ct);
        var hospitalDiagPoints = await TryLoadHospitalDiagnosticServicePointsAsync(doctorAccess.Hospital.Id, ct);
        var specialtiesById = await _db.Specialties
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, ct);

        var seenServiceIds = new HashSet<long>();
        var resolvedItems = new List<ResolvedDiagnosticOrderCreationItem>(validated.Items.Count);

        foreach (var item in validated.Items)
        {
            Service? service = null;
            if (item.ServiceId.HasValue)
            {
                service = await _db.Services
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == item.ServiceId.Value, ct);

                if (service is not null &&
                    item.ServiceCode is not null &&
                    !string.Equals(service.ServiceCode?.Trim(), item.ServiceCode, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException(
                        $"Mã dịch vụ '{item.ServiceCode}' không khớp với ID dịch vụ '{item.ServiceId.Value}'.");
                }
            }
            else if (item.ServiceCode is not null)
            {
                var matchingServices = await _db.Services
                    .AsNoTracking()
                    .Where(s => s.ServiceCode != null && s.ServiceCode.ToUpper() == item.ServiceCode)
                    .OrderBy(s => s.HospitalId == doctorAccess.Hospital.Id ? 0 : s.HospitalId == null ? 1 : 2)
                    .ThenBy(s => s.Id)
                    .ToListAsync(ct);

                service = matchingServices.FirstOrDefault();
            }

            if (service is null)
            {
                throw new NotFoundException(
                    "Service",
                    item.ServiceId?.ToString(CultureInfo.InvariantCulture) ?? item.ServiceCode!);
            }

            if (service.HospitalId.HasValue && service.HospitalId.Value != doctorAccess.Hospital.Id)
            {
                throw new ForbiddenException(
                    "Không thể chỉ định dịch vụ cận lâm sàng thuộc cơ sở y tế khác.");
            }

            if (!seenServiceIds.Add(service.Id))
            {
                throw new ValidationException(
                    $"Dịch vụ cận lâm sàng '{service.Name}' bị trùng lặp trong cùng phiếu chỉ định.");
            }

            if (!string.Equals(service.ServiceType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"Dịch vụ '{service.Name}' ({service.ServiceCode}) không phải là dịch vụ cận lâm sàng (DIAGNOSTIC).");
            }

            if (service.IsActive == false)
            {
                throw new ConflictException(
                    $"Dịch vụ cận lâm sàng '{service.Name}' ({service.ServiceCode}) hiện đang ngừng hoạt động.");
            }

            if (service.DepartmentId.HasValue)
            {
                var department = await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == service.DepartmentId.Value, ct);

                if (department is not null && department.IsActive == false)
                {
                    throw new ConflictException(
                        $"Khoa thực hiện dịch vụ '{service.Name}' hiện đang ngừng hoạt động.");
                }
            }

            var isAuthorized = await IsDoctorAuthorizedForServiceAsync(doctorAccess.Doctor.Id, service.Id, ct);
            if (!isAuthorized)
            {
                throw new ForbiddenException(
                    $"Bác sĩ '{doctorAccess.Doctor.DoctorCode}' chưa được phân quyền chỉ định dịch vụ cận lâm sàng '{service.Name}' ({service.ServiceCode}).");
            }

            var hasOpenOrderForService = existingVisitOrders.Any(o =>
                o.ServiceId == service.Id &&
                IsDiagnosticOrderActive(o, context.AllVisitQueueEntries));

            if (hasOpenOrderForService)
            {
                throw new ConflictException(
                    $"Dịch vụ cận lâm sàng '{service.Name}' ({service.ServiceCode}) đã có phiếu chỉ định đang chờ xử lý trong lượt khám này.");
            }

            QueueServicePoint? diagServicePoint = null;
            var serviceSpecificPoints = hospitalDiagPoints
                .Where(sp => sp.ServiceId == service.Id)
                .OrderBy(sp => sp.Id)
                .ToList();

            if (serviceSpecificPoints.Count > 0)
            {
                diagServicePoint = serviceSpecificPoints.FirstOrDefault(sp => sp.IsActive != false);
                if (diagServicePoint is null)
                {
                    throw new ConflictException(
                        $"Điểm phục vụ cận lâm sàng cho dịch vụ '{service.Name}' hiện đang ngừng hoạt động.");
                }
            }
            else
            {
                diagServicePoint = hospitalDiagPoints
                    .Where(sp => sp.IsActive != false)
                    .OrderBy(sp => sp.ServiceId.HasValue ? 1 : 0)
                    .ThenBy(sp => sp.Id)
                    .FirstOrDefault();
            }

            HospitalRoom? diagRoom = null;
            if (diagServicePoint?.RoomId.HasValue == true)
            {
                diagRoom = await _db.HospitalRooms
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == diagServicePoint.RoomId.Value, ct);

                if (diagRoom is not null &&
                    !string.Equals(diagRoom.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConflictException(
                        $"Phòng thực hiện cận lâm sàng '{diagRoom.RoomCode}' cho dịch vụ '{service.Name}' hiện đang bảo trì hoặc ngừng hoạt động.");
                }
            }

            var unitPrice = await ResolveActiveServicePriceAsync(service.Id, nowLocal, ct);
            var category = ResolveDiagnosticCategory(service, specialtiesById);

            resolvedItems.Add(new ResolvedDiagnosticOrderCreationItem(
                Item: item,
                Service: service,
                Category: category,
                UnitPrice: unitPrice,
                ServicePoint: diagServicePoint,
                Room: diagRoom));
        }

        var queueDate = activeEntry.QueueDate ?? DateOnly.FromDateTime(nowLocal);
        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode.Trim()
            : $"VIS-{queueDate:yyyyMM}-{visit.Id:D4}";

        var doctorTitle = string.IsNullOrWhiteSpace(doctorAccess.Doctor.ProfessionalTitle)
            ? "BS"
            : doctorAccess.Doctor.ProfessionalTitle.Trim();
        var doctorFullName = doctorAccess.Doctor.Staff?.FullName ?? doctorAccess.Doctor.DoctorCode;
        var formattedDoctorName = DoctorCatalogService.FormatDisplayName(doctorFullName, doctorTitle);

        var nextStateVersion = currentStateVersion + 1;
        var createdQueueEntries = new List<QueueEntry>(resolvedItems.Count);
        var createdOrderDtos = new List<ConsultationDiagnosticOrderDto>(resolvedItems.Count);

        foreach (var resolved in resolvedItems)
        {
            var diagSpId = resolved.ServicePoint?.Id;
            var maxQueueNumber = await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.QueueDate == queueDate &&
                    (diagSpId.HasValue
                        ? (q.ServicePointId == diagSpId.Value || q.CurrentServicePointId == diagSpId.Value)
                        : (q.QueueType != null && q.QueueType.ToUpper() == "DIAGNOSTIC")))
                .MaxAsync(q => (int?)q.QueueNumber, ct);

            var nextQueueNumber = (maxQueueNumber ?? 0) + 1;

            var diagQueueEntry = new QueueEntry
            {
                VisitId = visit.Id,
                AppointmentId = null,
                ServicePointId = diagSpId,
                CurrentServicePointId = diagSpId,
                QueueDate = queueDate,
                QueueNumber = nextQueueNumber,
                QueueType = "DIAGNOSTIC",
                Status = "WAITING",
                ScheduledAt = null,
                PresenceStatus = "PRESENT",
                ArrivedAt = nowLocal,
                ReadyAt = nowLocal,
                ArrivalClass = "WALK_IN",
                ManualPriority = ClinicalQueueRules.NormalizeStoredPriority(activeEntry.ManualPriority),
                StateVersion = 1,
            };

            _db.QueueEntries.Add(diagQueueEntry);
            await SaveChangesWithConcurrencyGuardAsync(ct);
            createdQueueEntries.Add(diagQueueEntry);

            var diagnosticOrder = new DiagnosticOrder
            {
                VisitId = visit.Id,
                OrderingDoctorId = doctorAccess.Doctor.Id,
                ServiceId = resolved.Service.Id,
                QueueEntryId = diagQueueEntry.Id,
                Instruction = resolved.Item.Instruction,
                RequiredForConclusion = resolved.Item.RequiredForConclusion,
                Priority = resolved.Item.Priority,
                Status = "ORDERED",
                ConfirmedByStaffId = null,
                ConfirmedAt = null,
                OrderedAt = nowLocal,
            };

            _db.DiagnosticOrders.Add(diagnosticOrder);
            await SaveChangesWithConcurrencyGuardAsync(ct);

            diagnosticOrder.OrderCode = $"DIA-{queueDate:yyyyMM}-{diagnosticOrder.Id:D4}";
            var ticketNo = $"C{nextQueueNumber:D3}";

            var changePayload = new
            {
                visitId = visit.Id,
                visitCode,
                clinicalQueueEntryId = activeEntry.Id,
                diagnosticOrderId = diagnosticOrder.Id,
                orderCode = diagnosticOrder.OrderCode,
                serviceId = resolved.Service.Id,
                serviceCode = resolved.Service.ServiceCode,
                serviceName = resolved.Service.Name,
                category = resolved.Category,
                unitPrice = resolved.UnitPrice,
                priority = diagnosticOrder.Priority,
                status = diagnosticOrder.Status,
                requiredForConclusion = diagnosticOrder.RequiredForConclusion == true,
                instruction = diagnosticOrder.Instruction,
                diagnosticQueueEntryId = diagQueueEntry.Id,
                diagnosticServicePointId = diagSpId,
                diagnosticRoomCode = resolved.Room?.RoomCode,
                diagnosticQueueNumber = nextQueueNumber,
                diagnosticTicketNo = ticketNo,
                transitionedToAwaitingResults = validated.TransitionToAwaitingResults,
                stateVersion = nextStateVersion,
                ipAddress,
            };

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = doctorAccess.Hospital.Id,
                Action = "DIAGNOSTIC_ORDER_CREATED",
                EntityType = "DIAGNOSTIC_ORDER",
                EntityId = diagnosticOrder.Id.ToString(CultureInfo.InvariantCulture),
                Reason = validated.ClinicalNote ??
                    $"Bác sĩ chỉ định cận lâm sàng: {resolved.Service.Name} ({resolved.Service.ServiceCode})",
                ChangeData = JsonSerializer.Serialize(changePayload, AuditJsonOptions),
                IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                OccurredAt = nowUtc,
            });

            createdOrderDtos.Add(new ConsultationDiagnosticOrderDto(
                OrderId: diagnosticOrder.Id,
                OrderCode: diagnosticOrder.OrderCode,
                VisitId: visit.Id,
                VisitCode: visitCode,
                OrderingDoctorId: doctorAccess.Doctor.Id,
                OrderingDoctorCode: doctorAccess.Doctor.DoctorCode,
                OrderingDoctorName: formattedDoctorName,
                ServiceId: resolved.Service.Id,
                ServiceCode: resolved.Service.ServiceCode,
                ServiceName: resolved.Service.Name,
                Category: resolved.Category,
                UnitPrice: resolved.UnitPrice,
                RequiresResultVerification: resolved.Service.RequiresResultVerification ?? true,
                QueueEntryId: diagQueueEntry.Id,
                FormattedQueueEntryId: $"q-{diagQueueEntry.Id:D3}",
                TicketNo: ticketNo,
                QueueNumber: nextQueueNumber,
                QueueStatus: "WAITING",
                ServicePointId: diagSpId,
                ServicePointName: resolved.ServicePoint?.Name,
                RoomId: resolved.Room?.Id ?? resolved.ServicePoint?.RoomId,
                RoomCode: resolved.Room?.RoomCode,
                RoomName: resolved.Room?.Name,
                Priority: diagnosticOrder.Priority ?? "NORMAL",
                Status: diagnosticOrder.Status ?? "ORDERED",
                RequiredForConclusion: diagnosticOrder.RequiredForConclusion ?? true,
                Instruction: diagnosticOrder.Instruction,
                OrderedAt: nowLocal,
                ConfirmedAt: null,
                IsResultFinalized: false));
        }

        activeEntry.StateVersion = nextStateVersion;
        if (validated.TransitionToAwaitingResults)
        {
            visit.Status = "AWAITING_RESULTS";
            activeEntry.Status = "COMPLETED";
            activeEntry.CompletedAt ??= nowLocal;
        }
        else
        {
            visit.Status = "IN_PROGRESS";
            activeEntry.Status = "IN_PROGRESS";
        }

        await SaveChangesWithConcurrencyGuardAsync(ct);

        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        var updatedVisitQueueEntries = createdQueueEntries
            .OrderByDescending(q => q.Id)
            .Concat(context.AllVisitQueueEntries)
            .ToList();

        var refreshedContext = context with
        {
            AllVisitQueueEntries = updatedVisitQueueEntries,
        };

        return await BuildWorkspaceResponseAsync(
            doctorAccess,
            refreshedContext,
            actionOutcome: "DIAGNOSTIC_ORDER_CREATED",
            nowUtc,
            nowLocal,
            ct,
            createdOrderDtos);
    }

    public async Task<IReadOnlyList<Icd10CatalogItemDto>> SearchIcd10CodesAsync(
        long actorUserId,
        string? query = null,
        bool primaryOnly = false,
        int limit = 50,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (limit is < 1 or > 200)
        {
            throw new ValidationException("Giới hạn kết quả tra cứu ICD-10 (limit) phải nằm trong khoảng 1–200.");
        }

        string? trimmedQuery = null;
        if (!string.IsNullOrWhiteSpace(query))
        {
            trimmedQuery = query.Trim();
            if (trimmedQuery.Contains('<') || trimmedQuery.Contains('>'))
            {
                throw new ValidationException("Từ khóa tra cứu ICD-10 không được chứa ký tự < hoặc >.");
            }

            if (trimmedQuery.Length > 100)
            {
                throw new ValidationException("Từ khóa tra cứu ICD-10 tối đa 100 ký tự.");
            }
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await EnsureDoctorAccessAsync(actorUserId, nowUtc, requireWritePermission: false, ct);

        List<Icd10Code> catalogRows;
        try
        {
            catalogRows = await _db.Icd10Codes
                .AsNoTracking()
                .Where(c => c.IsSelectable != false && (!primaryOnly || c.NotPrimaryDx != true))
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }

        IEnumerable<Icd10Code> filtered = catalogRows
            .Where(c => !string.IsNullOrWhiteSpace(c.Code));

        if (!string.IsNullOrWhiteSpace(trimmedQuery))
        {
            var normalizedQueryNoDiacritics = RemoveVietnameseDiacritics(trimmedQuery);
            filtered = filtered
                .Where(c =>
                {
                    var code = c.Code?.Trim() ?? string.Empty;
                    var nameVi = c.NameVi?.Trim() ?? string.Empty;
                    if (code.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                        nameVi.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (!string.IsNullOrEmpty(normalizedQueryNoDiacritics) && !string.IsNullOrEmpty(nameVi))
                    {
                        var nameNoDiacritics = RemoveVietnameseDiacritics(nameVi);
                        return nameNoDiacritics.Contains(normalizedQueryNoDiacritics, StringComparison.OrdinalIgnoreCase);
                    }

                    return false;
                })
                .OrderBy(c =>
                {
                    var code = c.Code?.Trim() ?? string.Empty;
                    if (string.Equals(code, trimmedQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        return 0;
                    }

                    if (code.StartsWith(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        return 1;
                    }

                    if (code.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        return 2;
                    }

                    return 3;
                })
                .ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id);
        }
        else
        {
            filtered = filtered
                .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id);
        }

        return filtered
            .Take(limit)
            .Select(c => new Icd10CatalogItemDto(
                Id: c.Id,
                Code: c.Code?.Trim() ?? string.Empty,
                NameVi: c.NameVi?.Trim() ?? string.Empty,
                CatalogVersion: c.CatalogVersion,
                NodeType: c.NodeType,
                IsSelectable: c.IsSelectable != false,
                NotPrimaryDx: c.NotPrimaryDx == true))
            .ToList();
    }

    public async Task<ConsultationWorkspaceResponseDto> RecordDiagnosisAsync(
        long actorUserId,
        string visitIdOrCode,
        RecordDiagnosisRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (numericId, code, isQueuePrefix) = ParseVisitOrQueueIdentifier(visitIdOrCode);
        var validated = ValidateRecordDiagnosisRequest(request);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var doctorAccess = await EnsureDoctorAccessAsync(actorUserId, nowUtc, requireWritePermission: true, ct);

        await using var tx = await _db.BeginTransactionAsync(ct);

        var context = await ResolveAndAuthorizeVisitContextAsync(
            doctorAccess,
            numericId,
            code,
            isQueuePrefix,
            visitIdOrCode,
            ct);

        var visit = context.Visit;
        var activeEntry = context.ActiveClinicalEntry;
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);

        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Không thể ghi nhận chẩn đoán cho lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (visitStatus is "COMPLETED" or "DONE" ||
            (activeEntry is not null && NormalizeQueueStatus(activeEntry.Status) == "COMPLETED"))
        {
            throw new ConflictException("Lượt khám đã hoàn tất (COMPLETED), không thể chỉnh sửa chẩn đoán và kết luận trực tiếp.");
        }

        if (activeEntry is null || NormalizeQueueStatus(activeEntry.Status) == "CANCELLED")
        {
            throw new ConflictException("Lượt khám chưa có số thứ tự hàng đợi lâm sàng hợp lệ để ghi nhận chẩn đoán.");
        }

        var currentQueueStatus = NormalizeQueueStatus(activeEntry.Status);
        var openDiagnosticCount = CountOpenDiagnosticEntries(context.AllVisitQueueEntries);

        if (visitStatus is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS" ||
            (currentQueueStatus != "IN_PROGRESS" && openDiagnosticCount > 0))
        {
            throw new ConflictException(
                "Bệnh nhân đang chờ kết quả cận lâm sàng. Vui lòng mở lại phiên khám (IN_PROGRESS) trước khi ghi nhận chẩn đoán.");
        }

        var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, doctorAccess.Hospital.Id, nowUtc, ct);
        var servicePointIdForPolicy = activeEntry.CurrentServicePointId
            ?? activeEntry.ServicePointId
            ?? context.ServicePoint?.Id;
        var policy = policySet.ForServicePoint(servicePointIdForPolicy);
        var hasPendingMandatoryOrders = await HasPendingMandatoryDiagnosticOrdersAsync(visit.Id, ct);

        if ((policy.FinalResultRequired && openDiagnosticCount > 0) || hasPendingMandatoryOrders)
        {
            throw new ConflictException(
                "Chưa thể kết luận chẩn đoán khi kết quả cận lâm sàng bắt buộc chưa hoàn tất.");
        }

        if (currentQueueStatus != "IN_PROGRESS" ||
            visitStatus is not ("IN_PROGRESS" or "IN_CONSULTATION" or "SERVING"))
        {
            var effectiveStatus = currentQueueStatus != "IN_PROGRESS" ? currentQueueStatus : visitStatus;
            throw new ConflictException(
                $"Lượt khám hiện đang ở trạng thái {effectiveStatus}. Vui lòng bắt đầu phiên khám lâm sàng (IN_PROGRESS) trước khi ghi nhận chẩn đoán.");
        }

        if (context.Room is not null &&
            !string.Equals(context.Room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Phòng khám của lượt khám hiện không hoạt động hoặc đang bảo trì.");
        }

        if (context.ServicePoint is not null && context.ServicePoint.IsActive == false)
        {
            throw new ConflictException("Điểm phục vụ của buồng khám hiện đang ngừng hoạt động.");
        }

        var currentStateVersion = activeEntry.StateVersion is > 0 ? activeEntry.StateVersion.Value : 1;
        if (request.ExpectedStateVersion.HasValue &&
            request.ExpectedStateVersion.Value != currentStateVersion)
        {
            throw new ConflictException(
                "Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        var medicalRecord = await _db.MedicalRecords
            .Where(m => m.VisitId == visit.Id)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);

        if (medicalRecord is not null &&
            string.Equals(medicalRecord.Status?.Trim(), "FINALIZED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "Hồ sơ bệnh án đã được khóa hoàn tất (FINALIZED), không thể chỉnh sửa chẩn đoán.");
        }

        var latestRevision = medicalRecord is null
            ? null
            : await _db.MedicalRecordRevisions
                .AsNoTracking()
                .Where(r => r.MedicalRecordId == medicalRecord.Id)
                .OrderByDescending(r => r.RevisionNo)
                .ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);

        var currentRevisionNo = latestRevision?.RevisionNo ?? 0;
        if (request.ExpectedRevisionNo.HasValue &&
            request.ExpectedRevisionNo.Value != currentRevisionNo)
        {
            throw new ConflictException(
                "Hồ sơ khám lâm sàng đã có phiên bản mới hơn (revision_no mismatch). Vui lòng tải lại trước khi lưu.");
        }

        var resolvedItems = await ResolveAndValidateDiagnosisItemsAsync(validated, ct);

        if (medicalRecord is null)
        {
            medicalRecord = new MedicalRecord
            {
                VisitId = visit.Id,
                CreatedByDoctorId = doctorAccess.Doctor.Id,
                Status = "DRAFT",
                FinalizedAt = null,
            };
            _db.MedicalRecords.Add(medicalRecord);
            await SaveChangesWithConcurrencyGuardAsync(ct);
        }

        var existingDiagnoses = await _db.VisitDiagnoses
            .Where(d => d.VisitId == visit.Id)
            .OrderByDescending(d => d.IsPrimary == true)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);

        var previousDiagnosesSnapshot = existingDiagnoses
            .Select(d => new
            {
                id = d.Id,
                icd10CodeId = d.Icd10CodeId,
                code = d.CodeSnapshot,
                name = d.NameSnapshot,
                isPrimary = d.IsPrimary == true,
                clinicalDiagnosis = d.ClinicalDiagnosis,
                status = d.Status,
            })
            .ToList();

        var matchedExisting = new HashSet<VisitDiagnosis>();

        foreach (var resolved in resolvedItems)
        {
            var existing = existingDiagnoses.FirstOrDefault(d =>
                !matchedExisting.Contains(d) &&
                ((resolved.Icd10CodeId.HasValue && d.Icd10CodeId == resolved.Icd10CodeId.Value) ||
                 (!string.IsNullOrWhiteSpace(d.CodeSnapshot) &&
                  string.Equals(d.CodeSnapshot.Trim(), resolved.CodeSnapshot, StringComparison.OrdinalIgnoreCase))));

            if (existing is not null)
            {
                matchedExisting.Add(existing);
                existing.Icd10CodeId = resolved.Icd10CodeId;
                existing.IsPrimary = resolved.IsPrimary;
                existing.CodeSnapshot = resolved.CodeSnapshot;
                existing.NameSnapshot = resolved.NameSnapshot;
                existing.CatalogVersion = resolved.CatalogVersion;
                existing.ClinicalDiagnosis = resolved.ClinicalDiagnosis;
                existing.Status = resolved.Status;
                existing.ConfirmedAt = string.Equals(resolved.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase)
                    ? (existing.ConfirmedAt ?? nowLocal)
                    : null;
                existing.RecordedByDoctorId = doctorAccess.Doctor.Id;
                existing.RecordedAt = nowLocal;
            }
            else
            {
                _db.VisitDiagnoses.Add(new VisitDiagnosis
                {
                    VisitId = visit.Id,
                    Icd10CodeId = resolved.Icd10CodeId,
                    IsPrimary = resolved.IsPrimary,
                    CodeSnapshot = resolved.CodeSnapshot,
                    NameSnapshot = resolved.NameSnapshot,
                    CatalogVersion = resolved.CatalogVersion,
                    ClinicalDiagnosis = resolved.ClinicalDiagnosis,
                    Status = resolved.Status,
                    ConfirmedAt = string.Equals(resolved.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase)
                        ? nowLocal
                        : null,
                    RecordedByDoctorId = doctorAccess.Doctor.Id,
                    RecordedAt = nowLocal,
                });
            }
        }

        var toRemove = existingDiagnoses.Where(d => !matchedExisting.Contains(d)).ToList();
        if (toRemove.Count > 0)
        {
            _db.VisitDiagnoses.RemoveRange(toRemove);
        }

        var primaryDiagnosis = resolvedItems.First(i => i.IsPrimary);
        var presentingComplaint = latestRevision?.PresentingComplaint
            ?? visit.ReasonForVisit
            ?? context.Appointment?.ReasonForVisit;
        var symptomHistory = latestRevision?.SymptomHistory;
        var relevantHistoryAllergies = latestRevision?.RelevantHistoryAllergies;
        var vitalSignsJson = latestRevision?.VitalSigns;
        var clinicalFindings = validated.ClinicalFindings ?? latestRevision?.ClinicalFindings;
        var clinicalConclusion = validated.ClinicalConclusion
            ?? latestRevision?.ClinicalConclusion
            ?? primaryDiagnosis.ClinicalDiagnosis
            ?? primaryDiagnosis.NameSnapshot;

        var nextRevisionNo = currentRevisionNo + 1;
        var isConfirmed = request.IsConfirmed ??
            string.Equals(primaryDiagnosis.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase);

        var newRevision = new MedicalRecordRevision
        {
            MedicalRecordId = medicalRecord.Id,
            RevisionNo = nextRevisionNo,
            PresentingComplaint = presentingComplaint,
            SymptomHistory = symptomHistory,
            RelevantHistoryAllergies = relevantHistoryAllergies,
            VitalSigns = vitalSignsJson,
            ClinicalFindings = clinicalFindings,
            ClinicalConclusion = clinicalConclusion,
            EditedByDoctorId = doctorAccess.Doctor.Id,
            RecordedAt = nowLocal,
            IsConfirmed = isConfirmed,
            AmendmentReason = validated.AmendmentReason,
        };
        _db.MedicalRecordRevisions.Add(newRevision);

        if (!string.Equals(visit.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            visit.Status = "IN_PROGRESS";
        }

        if (!string.Equals(activeEntry.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            activeEntry.Status = "IN_PROGRESS";
        }

        var changePayload = new
        {
            visitId = visit.Id,
            visitCode = visit.VisitCode,
            queueEntryId = activeEntry.Id,
            medicalRecordId = medicalRecord.Id,
            previousRevisionNo = currentRevisionNo > 0 ? (int?)currentRevisionNo : null,
            revisionNo = nextRevisionNo,
            isConfirmed,
            primaryIcd10Code = primaryDiagnosis.CodeSnapshot,
            diagnosisCount = resolvedItems.Count,
            diagnoses = resolvedItems
                .Select(i => new
                {
                    icd10CodeId = i.Icd10CodeId,
                    code = i.CodeSnapshot,
                    name = i.NameSnapshot,
                    isPrimary = i.IsPrimary,
                    clinicalDiagnosis = i.ClinicalDiagnosis,
                    status = i.Status,
                })
                .ToList(),
            previousDiagnoses = previousDiagnosesSnapshot,
            clinicalConclusion,
            differentialNotes = validated.DifferentialNotes,
            lifestyleInstructions = validated.LifestyleInstructions,
            warningSigns = validated.WarningSigns,
            stateVersion = currentStateVersion,
            ipAddress,
        };

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = doctorAccess.Hospital.Id,
            Action = "DIAGNOSIS_RECORDED",
            EntityType = "MEDICAL_RECORD",
            EntityId = medicalRecord.Id.ToString(CultureInfo.InvariantCulture),
            Reason = validated.AmendmentReason ?? "Bác sĩ ghi nhận chẩn đoán ICD-10 và kết luận lâm sàng",
            ChangeData = JsonSerializer.Serialize(changePayload, AuditJsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        });
        await SaveChangesWithConcurrencyGuardAsync(ct);

        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return await BuildWorkspaceResponseAsync(
            doctorAccess,
            context,
            actionOutcome: "DIAGNOSIS_RECORDED",
            nowUtc,
            nowLocal,
            ct);
    }

    private static (long? NumericId, string? Code, bool IsQueuePrefix) ParseVisitOrQueueIdentifier(string? visitIdOrCode)
    {
        var (numericId, code) = QueueEntryRequestParsing.ParseIdentifier(visitIdOrCode);
        var trimmed = visitIdOrCode!.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        var isQueuePrefix = trimmed.StartsWith("q-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("q_", StringComparison.OrdinalIgnoreCase);

        return (numericId, code, isQueuePrefix);
    }

    private static string? ValidateOpenRequest(OpenConsultationWorkspaceRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            return null;
        }

        var trimmed = request.Notes.Trim();
        if (trimmed.Length > 300)
        {
            throw new ValidationException("Ghi chú tối đa 300 ký tự.");
        }

        if (trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new ValidationException("Ghi chú không được chứa ký tự < hoặc >.");
        }

        return trimmed;
    }

    private static ValidatedClinicalExaminationInput ValidateRecordClinicalExaminationRequest(
        RecordClinicalExaminationRequest? request)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu ghi nhận khám lâm sàng không được để trống.");
        }

        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        if (request.ExpectedRevisionNo is < 1)
        {
            throw new ValidationException("Phiên bản bệnh án (ExpectedRevisionNo) phải lớn hơn hoặc bằng 1.");
        }

        var presentingComplaint = ValidateClinicalTextField(
            request.PresentingComplaint,
            "Lý do khám / triệu chứng chính",
            1000);
        var symptomHistory = ValidateClinicalTextField(
            request.SymptomHistory,
            "Bệnh sử & diễn tiến triệu chứng",
            4000);
        var relevantHistoryAllergies = ValidateClinicalTextField(
            request.RelevantHistoryAllergies,
            "Tiền sử & dị ứng liên quan",
            2000);
        var clinicalFindings = ValidateClinicalTextField(
            request.ClinicalFindings,
            "Khám lâm sàng",
            4000);
        var clinicalConclusion = ValidateClinicalTextField(
            request.ClinicalConclusion,
            "Nhận định & kết luận lâm sàng ban đầu",
            2000);
        var amendmentReason = ValidateClinicalTextField(
            request.AmendmentReason,
            "Lý do cập nhật",
            500);

        var v = request.VitalSigns;
        var bpSystolic = v?.BpSystolicMmhg ?? request.BpSystolicMmhg;
        var bpDiastolic = v?.BpDiastolicMmhg ?? request.BpDiastolicMmhg;
        var pulseBpm = v?.PulseBpm ?? request.PulseBpm;
        var temperatureC = v?.TemperatureC ?? request.TemperatureC;
        var spo2Percent = v?.Spo2Percent ?? request.Spo2Percent;
        var heightCm = v?.HeightCm ?? request.HeightCm;
        var weightKg = v?.WeightKg ?? request.WeightKg;

        if (bpSystolic.HasValue != bpDiastolic.HasValue)
        {
            throw new ValidationException("Huyết áp tâm thu và huyết áp tâm trương phải được nhập cùng nhau.");
        }

        if (bpSystolic is < 40 or > 300)
        {
            throw new ValidationException("Huyết áp tâm thu phải nằm trong khoảng 40–300 mmHg.");
        }

        if (bpDiastolic is < 20 or > 200)
        {
            throw new ValidationException("Huyết áp tâm trương phải nằm trong khoảng 20–200 mmHg.");
        }

        if (bpSystolic.HasValue && bpDiastolic.HasValue && bpSystolic.Value <= bpDiastolic.Value)
        {
            throw new ValidationException("Huyết áp tâm thu phải lớn hơn huyết áp tâm trương.");
        }

        if (pulseBpm is < 20 or > 300)
        {
            throw new ValidationException("Mạch phải nằm trong khoảng 20–300 lần/phút.");
        }

        if (temperatureC is < 30.0m or > 45.0m)
        {
            throw new ValidationException("Nhiệt độ cơ thể phải nằm trong khoảng 30.0–45.0 °C.");
        }

        if (spo2Percent is < 50 or > 100)
        {
            throw new ValidationException("Chỉ số SpO2 phải nằm trong khoảng 50–100%.");
        }

        if (heightCm is < 30.0m or > 250.0m)
        {
            throw new ValidationException("Chiều cao phải nằm trong khoảng 30–250 cm.");
        }

        if (weightKg is < 1.0m or > 500.0m)
        {
            throw new ValidationException("Cân nặng phải nằm trong khoảng 1–500 kg.");
        }

        var hasAnyText = presentingComplaint is not null ||
            symptomHistory is not null ||
            relevantHistoryAllergies is not null ||
            clinicalFindings is not null ||
            clinicalConclusion is not null;

        var hasAnyVitalSign = bpSystolic.HasValue ||
            bpDiastolic.HasValue ||
            pulseBpm.HasValue ||
            temperatureC.HasValue ||
            spo2Percent.HasValue ||
            heightCm.HasValue ||
            weightKg.HasValue;

        if (!hasAnyText && !hasAnyVitalSign)
        {
            throw new ValidationException(
                "Vui lòng nhập ít nhất một thông tin triệu chứng, dấu hiệu sinh tồn hoặc nhận xét khám lâm sàng.");
        }

        return new ValidatedClinicalExaminationInput(
            PresentingComplaint: presentingComplaint,
            SymptomHistory: symptomHistory,
            RelevantHistoryAllergies: relevantHistoryAllergies,
            BpSystolicMmhg: bpSystolic,
            BpDiastolicMmhg: bpDiastolic,
            PulseBpm: pulseBpm,
            TemperatureC: temperatureC.HasValue
                ? Math.Round(temperatureC.Value, 1, MidpointRounding.AwayFromZero)
                : null,
            Spo2Percent: spo2Percent,
            HeightCm: heightCm.HasValue
                ? Math.Round(heightCm.Value, 1, MidpointRounding.AwayFromZero)
                : null,
            WeightKg: weightKg.HasValue
                ? Math.Round(weightKg.Value, 1, MidpointRounding.AwayFromZero)
                : null,
            ClinicalFindings: clinicalFindings,
            ClinicalConclusion: clinicalConclusion,
            AmendmentReason: amendmentReason);
    }

    private static ValidatedCreateDiagnosticOrderInput ValidateCreateDiagnosticOrderRequest(
        CreateDiagnosticOrderRequest? request)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu chỉ định cận lâm sàng không được để trống.");
        }

        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        var clinicalNote = ValidateClinicalTextField(
            request.ClinicalNote,
            "Ghi chú lâm sàng",
            500);

        IReadOnlyList<CreateDiagnosticOrderItemRequest> rawItems;
        if (request.Items is { Count: > 0 })
        {
            if (request.Items.Count > 10)
            {
                throw new ValidationException("Mỗi lần chỉ định tối đa 10 dịch vụ cận lâm sàng.");
            }

            rawItems = request.Items;
        }
        else if (request.ServiceId.HasValue || request.ServiceCode is not null)
        {
            rawItems =
            [
                new CreateDiagnosticOrderItemRequest(
                    ServiceId: request.ServiceId,
                    ServiceCode: request.ServiceCode,
                    Instruction: request.Instruction,
                    Priority: request.Priority,
                    RequiredForConclusion: request.RequiredForConclusion),
            ];
        }
        else
        {
            throw new ValidationException("Vui lòng chọn ít nhất một dịch vụ cận lâm sàng để chỉ định.");
        }

        var normalizedItems = new List<ValidatedDiagnosticOrderItemInput>(rawItems.Count);
        foreach (var item in rawItems)
        {
            if (item is null)
            {
                throw new ValidationException("Thông tin mục chỉ định cận lâm sàng không hợp lệ.");
            }

            if (item.ServiceId.HasValue && item.ServiceId.Value <= 0)
            {
                throw new ValidationException("ID dịch vụ cận lâm sàng phải lớn hơn 0.");
            }

            string? normalizedCode = null;
            if (item.ServiceCode is not null)
            {
                var trimmedCode = item.ServiceCode.Trim();
                if (trimmedCode.Length == 0)
                {
                    if (!item.ServiceId.HasValue)
                    {
                        throw new ValidationException("Mã dịch vụ cận lâm sàng không được để trống.");
                    }
                }
                else
                {
                    if (trimmedCode.Length > 40 || trimmedCode.Contains('<') || trimmedCode.Contains('>'))
                    {
                        throw new ValidationException("Mã dịch vụ cận lâm sàng không hợp lệ.");
                    }

                    normalizedCode = trimmedCode.ToUpperInvariant();
                }
            }

            if (!item.ServiceId.HasValue && string.IsNullOrWhiteSpace(normalizedCode))
            {
                throw new ValidationException("Vui lòng cung cấp ID hoặc mã dịch vụ cận lâm sàng.");
            }

            var instruction = ValidateClinicalTextField(
                item.Instruction,
                "Chỉ dẫn cận lâm sàng",
                1000);

            var priority = NormalizeDiagnosticOrderPriority(item.Priority);
            var requiredForConclusion = item.RequiredForConclusion ?? true;

            normalizedItems.Add(new ValidatedDiagnosticOrderItemInput(
                ServiceId: item.ServiceId,
                ServiceCode: normalizedCode,
                Instruction: instruction,
                Priority: priority,
                RequiredForConclusion: requiredForConclusion));
        }

        return new ValidatedCreateDiagnosticOrderInput(
            Items: normalizedItems,
            ExpectedStateVersion: request.ExpectedStateVersion,
            TransitionToAwaitingResults: request.TransitionToAwaitingResults == true,
            ClinicalNote: clinicalNote);
    }

    private static string NormalizeDiagnosticOrderPriority(string? rawPriority)
    {
        if (string.IsNullOrWhiteSpace(rawPriority))
        {
            return "NORMAL";
        }

        var normalized = rawPriority.Trim().ToUpperInvariant();
        return normalized switch
        {
            "NORMAL" or "STANDARD" or "ROUTINE" => "NORMAL",
            "URGENT" => "URGENT",
            "STAT" => "STAT",
            _ => throw new ValidationException(
                "Mức độ ưu tiên chỉ định cận lâm sàng không hợp lệ. Chỉ chấp nhận NORMAL, URGENT hoặc STAT."),
        };
    }

    private static ValidatedDiagnosisInput ValidateRecordDiagnosisRequest(RecordDiagnosisRequest? request)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu ghi nhận chẩn đoán và kết luận lâm sàng không được để trống.");
        }

        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        if (request.ExpectedRevisionNo is < 1)
        {
            throw new ValidationException("Phiên bản bệnh án (ExpectedRevisionNo) phải lớn hơn hoặc bằng 1.");
        }

        if (request.ClinicalConclusion is not null &&
            string.IsNullOrWhiteSpace(request.ClinicalConclusion) &&
            request.ClinicalConclusion.Length > 0)
        {
            throw new ValidationException("Kết luận lâm sàng phải có tối thiểu 3 ký tự.");
        }

        var clinicalConclusion = ValidateClinicalTextField(
            request.ClinicalConclusion,
            "Kết luận lâm sàng",
            2000);

        if (clinicalConclusion is not null && clinicalConclusion.Length < 3)
        {
            throw new ValidationException("Kết luận lâm sàng phải có tối thiểu 3 ký tự.");
        }

        var clinicalFindings = ValidateClinicalTextField(
            request.ClinicalFindings,
            "Khám lâm sàng",
            4000);
        var differentialNotes = ValidateClinicalTextField(
            request.DifferentialNotes,
            "Ghi chú chẩn đoán phân biệt",
            1000);
        var lifestyleInstructions = ValidateClinicalTextField(
            request.LifestyleInstructions,
            "Dặn dò sinh hoạt",
            1000);
        var warningSigns = ValidateClinicalTextField(
            request.WarningSigns,
            "Dấu hiệu cảnh báo",
            1000);
        var amendmentReason = ValidateClinicalTextField(
            request.AmendmentReason,
            "Lý do cập nhật",
            500);
        var primaryClinicalDiagnosis = ValidateClinicalTextField(
            request.PrimaryClinicalDiagnosis,
            "Chẩn đoán lâm sàng chính",
            500);

        var defaultStatus = request.IsConfirmed == false ? "PRELIMINARY" : "CONFIRMED";
        if (!string.IsNullOrWhiteSpace(request.DiagnosisStatus))
        {
            var normalizedReqStatus = request.DiagnosisStatus.Trim().ToUpperInvariant();
            if (normalizedReqStatus is not ("PRELIMINARY" or "CONFIRMED"))
            {
                throw new ValidationException("Trạng thái chẩn đoán chỉ chấp nhận PRELIMINARY hoặc CONFIRMED.");
            }

            defaultStatus = normalizedReqStatus;
        }

        var rawItems = new List<RecordDiagnosisItemRequest>();
        if (request.Diagnoses is not null && request.Diagnoses.Count > 0)
        {
            if (request.Diagnoses.Count == 1 && request.Diagnoses[0] is not null && request.Diagnoses[0].IsPrimary is null)
            {
                rawItems.Add(request.Diagnoses[0] with { IsPrimary = true });
            }
            else
            {
                rawItems.AddRange(request.Diagnoses);
            }
        }
        else
        {
            if (request.PrimaryIcd10CodeId.HasValue || !string.IsNullOrWhiteSpace(request.PrimaryIcd10Code))
            {
                rawItems.Add(new RecordDiagnosisItemRequest(
                    Icd10CodeId: request.PrimaryIcd10CodeId,
                    Code: request.PrimaryIcd10Code,
                    IsPrimary: true,
                    ClinicalDiagnosis: primaryClinicalDiagnosis,
                    Status: defaultStatus));
            }

            if (request.SecondaryIcd10CodeIds is not null)
            {
                foreach (var secId in request.SecondaryIcd10CodeIds)
                {
                    rawItems.Add(new RecordDiagnosisItemRequest(
                        Icd10CodeId: secId,
                        IsPrimary: false,
                        Status: defaultStatus));
                }
            }

            if (request.SecondaryIcd10Codes is not null)
            {
                foreach (var secCode in request.SecondaryIcd10Codes)
                {
                    if (!string.IsNullOrWhiteSpace(secCode))
                    {
                        rawItems.Add(new RecordDiagnosisItemRequest(
                            Code: secCode,
                            IsPrimary: false,
                            Status: defaultStatus));
                    }
                }
            }
        }

        if (rawItems.Count == 0)
        {
            throw new ValidationException("Vui lòng chọn ít nhất một chẩn đoán bệnh chính (ICD-10).");
        }

        if (rawItems.Count > 11)
        {
            throw new ValidationException("Tối đa 1 chẩn đoán chính và 10 chẩn đoán bệnh kèm theo.");
        }
        foreach (var item in rawItems)
        {
            if (item is null)
            {
                throw new ValidationException("Mục chẩn đoán không hợp lệ.");
            }
        }

        var primaryCount = rawItems.Count(i => i.IsPrimary == true);
        if (primaryCount == 0)
        {
            throw new ValidationException("Phải có đúng 1 chẩn đoán chính (isPrimary = true).");
        }

        if (primaryCount > 1)
        {
            throw new ValidationException("Chỉ được phép chọn tối đa 1 chẩn đoán chính (isPrimary = true).");
        }

        var seenIds = new HashSet<long>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validatedItems = new List<ValidatedDiagnosisItemInput>(rawItems.Count);

        foreach (var item in rawItems)
        {
            var rawCode = !string.IsNullOrWhiteSpace(item.Code) ? item.Code : item.Icd10Code;
            var hasId = item.Icd10CodeId.HasValue;
            var hasCode = !string.IsNullOrWhiteSpace(rawCode);

            if (!hasId && !hasCode)
            {
                throw new ValidationException("Mỗi mục chẩn đoán phải có mã ICD-10 (code) hoặc ID danh mục (icd10CodeId).");
            }

            if (item.Icd10CodeId is <= 0)
            {
                throw new ValidationException("Mã định danh ICD-10 (icd10CodeId) phải lớn hơn 0.");
            }

            string? normalizedCode = null;
            if (hasCode)
            {
                normalizedCode = rawCode!.Trim().ToUpperInvariant();
                if (normalizedCode.Length > 20 || !Icd10CodeRegex.IsMatch(normalizedCode))
                {
                    throw new ValidationException($"Mã ICD-10 '{rawCode.Trim()}' không hợp lệ (ví dụ: K29.5, I10).");
                }
            }

            if (hasId && !seenIds.Add(item.Icd10CodeId!.Value))
            {
                throw new ValidationException("Danh sách chẩn đoán không được chứa mã bệnh ICD-10 trùng lặp.");
            }

            if (normalizedCode is not null && !seenCodes.Add(normalizedCode))
            {
                throw new ValidationException("Danh sách chẩn đoán không được chứa mã bệnh ICD-10 trùng lặp.");
            }

            var itemName = ValidateClinicalTextField(item.Name, "Tên chẩn đoán ICD-10", 300);
            var itemClinicalDiagnosis = ValidateClinicalTextField(item.ClinicalDiagnosis, "Chẩn đoán lâm sàng", 500);

            var itemStatus = defaultStatus;
            if (!string.IsNullOrWhiteSpace(item.Status))
            {
                var normalizedItemStatus = item.Status.Trim().ToUpperInvariant();
                if (normalizedItemStatus is not ("PRELIMINARY" or "CONFIRMED"))
                {
                    throw new ValidationException("Trạng thái chẩn đoán chỉ chấp nhận PRELIMINARY hoặc CONFIRMED.");
                }

                itemStatus = normalizedItemStatus;
            }

            validatedItems.Add(new ValidatedDiagnosisItemInput(
                Icd10CodeId: item.Icd10CodeId,
                Code: normalizedCode,
                Name: itemName,
                IsPrimary: item.IsPrimary == true,
                ClinicalDiagnosis: itemClinicalDiagnosis,
                Status: itemStatus));
        }

        var orderedItems = validatedItems
            .OrderByDescending(i => i.IsPrimary)
            .ToList();

        return new ValidatedDiagnosisInput(
            Items: orderedItems,
            PrimaryClinicalDiagnosis: primaryClinicalDiagnosis,
            ClinicalConclusion: clinicalConclusion,
            ClinicalFindings: clinicalFindings,
            DifferentialNotes: differentialNotes,
            LifestyleInstructions: lifestyleInstructions,
            WarningSigns: warningSigns,
            AmendmentReason: amendmentReason);
    }

    private async Task<List<ResolvedDiagnosisItem>> ResolveAndValidateDiagnosisItemsAsync(
        ValidatedDiagnosisInput validated,
        CancellationToken ct)
    {
        List<Icd10Code> catalogRows;
        try
        {
            catalogRows = await _db.Icd10Codes
                .AsNoTracking()
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            catalogRows = [];
        }

        var resolved = new List<ResolvedDiagnosisItem>(validated.Items.Count);
        var hasCatalog = catalogRows.Count > 0;

        var byId = hasCatalog
            ? catalogRows.ToDictionary(c => c.Id)
            : new Dictionary<long, Icd10Code>();

        var byCode = hasCatalog
            ? catalogRows
                .Where(c => !string.IsNullOrWhiteSpace(c.Code))
                .GroupBy(c => c.Code!.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Icd10Code>(StringComparer.OrdinalIgnoreCase);

        var seenIds = new HashSet<long>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in validated.Items)
        {
            long? resolvedId = item.Icd10CodeId;
            string resolvedCode;
            string resolvedName;
            string resolvedVersion;

            if (hasCatalog)
            {
                Icd10Code? matchedById = null;
                Icd10Code? matchedByCode = null;

                if (item.Icd10CodeId.HasValue &&
                    !byId.TryGetValue(item.Icd10CodeId.Value, out matchedById))
                {
                    throw new ValidationException(
                        $"Không tìm thấy mã bệnh ICD-10 có ID = {item.Icd10CodeId.Value} trong danh mục.");
                }

                if (!string.IsNullOrWhiteSpace(item.Code) &&
                    !byCode.TryGetValue(item.Code, out matchedByCode))
                {
                    throw new ValidationException(
                        $"Mã bệnh ICD-10 '{item.Code}' không tồn tại trong danh mục hệ thống.");
                }

                if (matchedById is not null &&
                    matchedByCode is not null &&
                    matchedById.Id != matchedByCode.Id)
                {
                    throw new ValidationException(
                        $"Mã ICD-10 '{item.Code}' không khớp với ID danh mục {item.Icd10CodeId!.Value} ({matchedById.Code}).");
                }

                var matched = matchedById ?? matchedByCode!;
                var matchedCode = matched.Code?.Trim().ToUpperInvariant() ?? item.Code ?? string.Empty;

                if (matched.IsSelectable == false)
                {
                    throw new ValidationException(
                        $"Mã ICD-10 '{matchedCode}' là nhóm chương/mục không được phép chọn trực tiếp (is_selectable = false).");
                }

                if (item.IsPrimary && matched.NotPrimaryDx == true)
                {
                    throw new ValidationException(
                        $"Mã ICD-10 '{matchedCode}' không được phép chọn làm chẩn đoán chính (not_primary_dx = true).");
                }

                resolvedId = matched.Id;
                resolvedCode = matchedCode;
                resolvedName = !string.IsNullOrWhiteSpace(item.Name)
                    ? item.Name
                    : (matched.NameVi?.Trim() ?? matchedCode);
                resolvedVersion = !string.IsNullOrWhiteSpace(matched.CatalogVersion)
                    ? matched.CatalogVersion.Trim()
                    : "ICD-10-VN";
            }
            else
            {
                resolvedCode = item.Code ?? $"ICD-{item.Icd10CodeId!.Value}";
                resolvedName = item.Name ?? item.ClinicalDiagnosis ?? resolvedCode;
                resolvedVersion = "DEMO-ICD10";
            }

            if (resolvedId.HasValue && !seenIds.Add(resolvedId.Value))
            {
                throw new ValidationException("Danh sách chẩn đoán không được chứa mã bệnh ICD-10 trùng lặp.");
            }

            if (!seenCodes.Add(resolvedCode))
            {
                throw new ValidationException("Danh sách chẩn đoán không được chứa mã bệnh ICD-10 trùng lặp.");
            }

            var clinicalDiagnosis = item.ClinicalDiagnosis;
            if (item.IsPrimary && string.IsNullOrWhiteSpace(clinicalDiagnosis))
            {
                clinicalDiagnosis = validated.PrimaryClinicalDiagnosis
                    ?? validated.ClinicalConclusion
                    ?? resolvedName;
            }

            resolved.Add(new ResolvedDiagnosisItem(
                Icd10CodeId: resolvedId,
                CodeSnapshot: resolvedCode,
                NameSnapshot: resolvedName,
                CatalogVersion: resolvedVersion,
                IsPrimary: item.IsPrimary,
                ClinicalDiagnosis: clinicalDiagnosis,
                Status: item.Status));
        }

        return resolved;
    }

    private static string RemoveVietnameseDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(ch switch
            {
                'đ' => 'd',
                'Đ' => 'D',
                _ => ch,
            });
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string? ValidateClinicalTextField(
        string? rawValue,
        string fieldLabel,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var trimmed = rawValue.Trim();
        if (trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new ValidationException($"{fieldLabel} không được chứa ký tự < hoặc >.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new ValidationException($"{fieldLabel} tối đa {maxLength} ký tự.");
        }

        return trimmed;
    }

    private static string? SerializeVitalSigns(
        int? bpSystolic,
        int? bpDiastolic,
        int? pulseBpm,
        decimal? temperatureC,
        int? spo2Percent,
        decimal? heightCm,
        decimal? weightKg)
    {
        if (!bpSystolic.HasValue &&
            !bpDiastolic.HasValue &&
            !pulseBpm.HasValue &&
            !temperatureC.HasValue &&
            !spo2Percent.HasValue &&
            !heightCm.HasValue &&
            !weightKg.HasValue)
        {
            return null;
        }

        var payload = new Dictionary<string, object>();
        if (bpSystolic.HasValue)
        {
            payload["bp_systolic_mmhg"] = bpSystolic.Value;
        }

        if (bpDiastolic.HasValue)
        {
            payload["bp_diastolic_mmhg"] = bpDiastolic.Value;
        }

        if (pulseBpm.HasValue)
        {
            payload["pulse_bpm"] = pulseBpm.Value;
        }

        if (temperatureC.HasValue)
        {
            payload["temperature_c"] = Math.Round(temperatureC.Value, 1, MidpointRounding.AwayFromZero);
        }

        if (spo2Percent.HasValue)
        {
            payload["spo2_percent"] = spo2Percent.Value;
        }

        if (heightCm.HasValue)
        {
            payload["height_cm"] = Math.Round(heightCm.Value, 1, MidpointRounding.AwayFromZero);
        }

        if (weightKg.HasValue)
        {
            payload["weight_kg"] = Math.Round(weightKg.Value, 1, MidpointRounding.AwayFromZero);
        }

        return JsonSerializer.Serialize(payload);
    }

    private Task<DoctorAccessContext> EnsureDoctorAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        bool requireWritePermission,
        CancellationToken ct)
        => EnsureDoctorAccessAsync(
            actorUserId,
            nowUtc,
            requireWritePermission ? PermissionMedicalRecordWrite : null,
            ct);

    private async Task<DoctorAccessContext> EnsureDoctorAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        string? requiredPermission,
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
            throw new ForbiddenException("Người dùng không có quyền truy cập không gian khám lâm sàng của Bác sĩ.");
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

        var allPermissions = await _db.Permissions
            .AsNoTracking()
            .ToListAsync(ct);

        var allRolePermissions = await _db.RolePermissions
            .AsNoTracking()
            .ToListAsync(ct);

        var permissionsById = allPermissions.ToDictionary(p => p.Id, p => p.Code);

        var actorPermissions = allRolePermissions
            .Where(rp => actorRoleIds.Contains(rp.RoleId) && permissionsById.ContainsKey(rp.PermissionId))
            .Select(rp => permissionsById[rp.PermissionId])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (actorPermissions.Count == 0 && allRolePermissions.Count == 0)
        {
            actorPermissions = FallbackDoctorPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (string.Equals(requiredPermission, PermissionMedicalRecordWrite, StringComparison.OrdinalIgnoreCase) &&
            !actorPermissions.Contains(PermissionMedicalRecordWrite))
        {
            throw new ForbiddenException(
                "Bác sĩ không có quyền mở hoặc cập nhật hồ sơ khám lâm sàng (MEDICAL_RECORD_WRITE).");
        }

        if (string.Equals(requiredPermission, PermissionDiagnosticOrder, StringComparison.OrdinalIgnoreCase) &&
            !actorPermissions.Contains(PermissionDiagnosticOrder))
        {
            throw new ForbiddenException(
                "Bác sĩ không có quyền chỉ định cận lâm sàng (DIAGNOSTIC_ORDER).");
        }

        return new DoctorAccessContext(hospital, actorStaff, actorDoctor, actorPermissions);
    }

    private async Task<ResolvedVisitWorkspaceContext> ResolveAndAuthorizeVisitContextAsync(
        DoctorAccessContext doctorAccess,
        long? numericId,
        string? code,
        bool isQueuePrefix,
        string rawIdOrCode,
        CancellationToken ct)
    {
        Visit? visit = null;
        QueueEntry? explicitQueueEntry = null;

        if (numericId.HasValue && isQueuePrefix)
        {
            explicitQueueEntry = await _db.QueueEntries
                .FirstOrDefaultAsync(q => q.Id == numericId.Value, ct)
                ?? throw new NotFoundException("Không tìm thấy lượt xếp hàng khám.");

            if (!ClinicalQueueRules.IsClinicalExaminationQueueType(explicitQueueEntry.QueueType))
            {
                throw new ConflictException("Chỉ hỗ trợ mở không gian khám lâm sàng từ lượt xếp hàng khám của Bác sĩ.");
            }

            visit = await ResolveVisitFromQueueEntryAsync(explicitQueueEntry, ct);
        }
        else if (numericId.HasValue)
        {
            visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == numericId.Value, ct);
            if (visit is null)
            {
                var candidateQueueEntry = await _db.QueueEntries
                    .FirstOrDefaultAsync(q => q.Id == numericId.Value, ct);

                if (candidateQueueEntry is not null)
                {
                    if (!ClinicalQueueRules.IsClinicalExaminationQueueType(candidateQueueEntry.QueueType))
                    {
                        throw new ConflictException("Chỉ hỗ trợ mở không gian khám lâm sàng từ lượt xếp hàng khám của Bác sĩ.");
                    }

                    explicitQueueEntry = candidateQueueEntry;
                    visit = await ResolveVisitFromQueueEntryAsync(candidateQueueEntry, ct);
                }
                else
                {
                    throw new NotFoundException("Visit", rawIdOrCode.Trim());
                }
            }
        }
        else if (!string.IsNullOrEmpty(code))
        {
            visit = await _db.Visits
                .FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == code, ct);

            if (visit is null &&
                code.StartsWith("VIS-", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(code.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVisitId) &&
                parsedVisitId > 0)
            {
                visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == parsedVisitId, ct);
            }

            if (visit is null)
            {
                throw new NotFoundException("Visit", rawIdOrCode.Trim());
            }
        }

        if (visit is null)
        {
            throw new NotFoundException("Visit", rawIdOrCode.Trim());
        }

        var allVisitQueueEntries = await _db.QueueEntries
            .Where(q =>
                q.VisitId == visit.Id ||
                (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        var clinicalEntries = allVisitQueueEntries
            .Where(q => ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType))
            .ToList();

        var nonCancelledClinicalEntries = clinicalEntries
            .Where(q => NormalizeQueueStatus(q.Status) != "CANCELLED")
            .ToList();

        QueueEntry? activeClinicalEntry;
        if (explicitQueueEntry is not null && NormalizeQueueStatus(explicitQueueEntry.Status) != "CANCELLED")
        {
            activeClinicalEntry = explicitQueueEntry;
        }
        else
        {
            activeClinicalEntry = nonCancelledClinicalEntries.FirstOrDefault(q =>
                    ClinicalQueueRules.ResolveQueueType(q.QueueType, null) == ClinicalQueueRules.QueueTypeReturnConsultation)
                ?? nonCancelledClinicalEntries.FirstOrDefault()
                ?? explicitQueueEntry
                ?? clinicalEntries.FirstOrDefault();
        }

        Appointment? appointment = null;
        if (visit.AppointmentId.HasValue)
        {
            appointment = await _db.Appointments
                .AsNoTracking()
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Service)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Room)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Doctor)
                .FirstOrDefaultAsync(a => a.Id == visit.AppointmentId.Value, ct);
        }

        QueueServicePoint? servicePoint = null;
        var servicePointId = activeClinicalEntry?.CurrentServicePointId ?? activeClinicalEntry?.ServicePointId;
        if (servicePointId.HasValue)
        {
            try
            {
                servicePoint = await _db.QueueServicePoints
                    .AsNoTracking()
                    .FirstOrDefaultAsync(sp => sp.Id == servicePointId.Value, ct);
            }
            catch (NotSupportedException)
            {
                servicePoint = null;
            }
        }

        var roomId = visit.ExaminationRoomId ?? servicePoint?.RoomId ?? appointment?.Slot?.RoomId;
        HospitalRoom? room = null;
        if (roomId.HasValue)
        {
            room = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == roomId.Value, ct);
        }

        if (servicePoint is null && (roomId.HasValue || visit.AssignedDoctorId.HasValue))
        {
            servicePoint = await TryFindMatchingServicePointAsync(
                doctorAccess.Hospital.Id,
                roomId,
                visit.AssignedDoctorId,
                ct);
        }

        var assignedDoctorId = visit.AssignedDoctorId
            ?? servicePoint?.DoctorId
            ?? appointment?.Slot?.DoctorId
            ?? visit.CompletedByDoctorId;

        Doctor? assignedDoctor = null;
        if (assignedDoctorId.HasValue)
        {
            if (assignedDoctorId.Value == doctorAccess.Doctor.Id)
            {
                assignedDoctor = doctorAccess.Doctor;
            }
            else
            {
                assignedDoctor = await _db.Doctors
                    .AsNoTracking()
                    .Include(d => d.Staff)
                    .Include(d => d.Specialty)
                    .FirstOrDefaultAsync(d => d.Id == assignedDoctorId.Value, ct);
            }
        }

        // Layer 2 Hospital ownership check
        var visitHospitalId = room?.HospitalId
            ?? servicePoint?.HospitalId
            ?? assignedDoctor?.HospitalId
            ?? assignedDoctor?.Staff?.HospitalId
            ?? appointment?.Slot?.Service?.HospitalId
            ?? appointment?.Slot?.Room?.HospitalId;

        if (visitHospitalId.HasValue && visitHospitalId.Value != doctorAccess.Hospital.Id)
        {
            throw new ForbiddenException("Bạn không có quyền truy cập lượt khám thuộc cơ sở y tế khác.");
        }

        // Layer 2 Doctor/ServicePoint ownership check
        var isOwnedByDoctor = (assignedDoctorId.HasValue && assignedDoctorId.Value == doctorAccess.Doctor.Id) ||
            (servicePoint?.DoctorId.HasValue == true && servicePoint.DoctorId.Value == doctorAccess.Doctor.Id);

        if (!isOwnedByDoctor)
        {
            throw new ForbiddenException(
                "Bác sĩ chỉ được phép mở không gian khám cho lượt khám hoặc buồng khám được phân công.");
        }

        var patientId = visit.PatientId ?? appointment?.PatientId;
        if (!patientId.HasValue)
        {
            throw new NotFoundException("Patient", 0);
        }

        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == patientId.Value, ct)
            ?? throw new NotFoundException("Patient", patientId.Value);

        var serviceId = appointment?.Slot?.ServiceId ?? servicePoint?.ServiceId;
        Service? service = appointment?.Slot?.Service;
        if (service is null && serviceId.HasValue)
        {
            service = await _db.Services
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == serviceId.Value, ct);
        }

        var departmentId = room?.DepartmentId ?? service?.DepartmentId ?? assignedDoctor?.Staff?.DepartmentId;
        Department? department = room?.Department;
        if (department is null && departmentId.HasValue)
        {
            department = await _db.Departments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == departmentId.Value, ct);
        }

        var specialtyId = service?.SpecialtyId ?? assignedDoctor?.SpecialtyId ?? doctorAccess.Doctor.SpecialtyId;
        Specialty? specialty = assignedDoctor?.Specialty ?? doctorAccess.Doctor.Specialty;
        if ((specialty is null || specialty.Id != specialtyId) && specialtyId.HasValue)
        {
            specialty = await _db.Specialties
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == specialtyId.Value, ct);
        }

        return new ResolvedVisitWorkspaceContext(
            Visit: visit,
            Patient: patient,
            Appointment: appointment,
            Room: room,
            Department: department,
            Specialty: specialty,
            Service: service,
            ServicePoint: servicePoint,
            AssignedDoctor: assignedDoctor ?? doctorAccess.Doctor,
            ActiveClinicalEntry: activeClinicalEntry,
            AllVisitQueueEntries: allVisitQueueEntries);
    }

    private async Task<Visit> ResolveVisitFromQueueEntryAsync(
        QueueEntry queueEntry,
        CancellationToken ct)
    {
        Visit? visit = null;
        if (queueEntry.VisitId.HasValue)
        {
            visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == queueEntry.VisitId.Value, ct);
        }

        if (visit is null && queueEntry.AppointmentId.HasValue)
        {
            visit = await _db.Visits
                .Where(v => v.AppointmentId == queueEntry.AppointmentId.Value)
                .OrderByDescending(v => v.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (visit is null)
        {
            throw new ConflictException(
                "Bệnh nhân chưa hoàn tất tiếp nhận lượt khám (Visit), không thể mở không gian khám lâm sàng.");
        }

        return visit;
    }

    private async Task<QueueServicePoint?> TryFindMatchingServicePointAsync(
        long hospitalId,
        long? roomId,
        long? doctorId,
        CancellationToken ct)
    {
        try
        {
            var points = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.HospitalId == hospitalId &&
                    (sp.PointType == null || sp.PointType.ToUpper() == "EXAMINATION_ROOM"))
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);

            if (roomId.HasValue && doctorId.HasValue)
            {
                var exact = points.FirstOrDefault(sp => sp.RoomId == roomId.Value && sp.DoctorId == doctorId.Value);
                if (exact is not null)
                {
                    return exact;
                }
            }

            if (roomId.HasValue)
            {
                var byRoom = points.FirstOrDefault(sp => sp.RoomId == roomId.Value);
                if (byRoom is not null)
                {
                    return byRoom;
                }
            }

            if (doctorId.HasValue)
            {
                return points.FirstOrDefault(sp => sp.DoctorId == doctorId.Value);
            }

            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task EnsureNoOtherActiveInProgressEntryAsync(
        QueueEntry currentEntry,
        Visit currentVisit,
        CancellationToken ct)
    {
        var effectiveServicePointId = currentEntry.CurrentServicePointId ?? currentEntry.ServicePointId;
        var effectiveRoomId = currentVisit.ExaminationRoomId;

        if (!effectiveServicePointId.HasValue && !effectiveRoomId.HasValue)
        {
            return;
        }

        var candidateEntries = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.Id != currentEntry.Id &&
                q.CompletedAt == null &&
                (!currentEntry.QueueDate.HasValue || q.QueueDate == currentEntry.QueueDate) &&
                q.Status != null &&
                (q.Status.ToUpper() == "IN_PROGRESS" ||
                 q.Status.ToUpper() == "IN_CONSULTATION" ||
                 q.Status.ToUpper() == "SERVING"))
            .ToListAsync(ct);

        if (candidateEntries.Count == 0)
        {
            return;
        }

        if (effectiveServicePointId.HasValue &&
            candidateEntries.Any(q =>
                q.CurrentServicePointId == effectiveServicePointId.Value ||
                q.ServicePointId == effectiveServicePointId.Value))
        {
            throw new ConflictException(
                "Điểm dịch vụ/phòng khám hiện đang có bệnh nhân khác trong trạng thái đang khám (IN_PROGRESS).");
        }

        if (effectiveRoomId.HasValue)
        {
            var candidateVisitIds = candidateEntries
                .Where(q => q.VisitId.HasValue)
                .Select(q => q.VisitId!.Value)
                .Distinct()
                .ToList();

            if (candidateVisitIds.Count > 0)
            {
                var hasSameRoomActiveVisit = await _db.Visits
                    .AsNoTracking()
                    .AnyAsync(v =>
                        candidateVisitIds.Contains(v.Id) &&
                        v.ExaminationRoomId == effectiveRoomId.Value &&
                        v.CompletedAt == null,
                        ct);

                if (hasSameRoomActiveVisit)
                {
                    throw new ConflictException(
                        "Điểm dịch vụ/phòng khám hiện đang có bệnh nhân khác trong trạng thái đang khám (IN_PROGRESS).");
                }
            }
        }
    }

    private async Task<(MedicalRecord? Record, bool Created)> EnsureDraftMedicalRecordAsync(
        Visit visit,
        Appointment? appointment,
        long doctorId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        try
        {
            var existingRecord = await _db.MedicalRecords
                .FirstOrDefaultAsync(m => m.VisitId == visit.Id, ct);

            if (existingRecord is not null)
            {
                return (existingRecord, false);
            }

            var newRecord = new MedicalRecord
            {
                VisitId = visit.Id,
                CreatedByDoctorId = doctorId,
                Status = "DRAFT",
                FinalizedAt = null,
            };

            _db.MedicalRecords.Add(newRecord);
            await SaveChangesWithConcurrencyGuardAsync(ct);

            var initialRevision = new MedicalRecordRevision
            {
                MedicalRecordId = newRecord.Id,
                RevisionNo = 1,
                PresentingComplaint = visit.ReasonForVisit ?? appointment?.ReasonForVisit,
                EditedByDoctorId = doctorId,
                RecordedAt = nowLocal,
                IsConfirmed = false,
            };

            _db.MedicalRecordRevisions.Add(initialRevision);
            return (newRecord, true);
        }
        catch (NotSupportedException)
        {
            return (null, false);
        }
    }

    private async Task SaveChangesWithConcurrencyGuardAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }
    }

    private async Task<ConsultationWorkspaceResponseDto> BuildWorkspaceResponseAsync(
        DoctorAccessContext doctorAccess,
        ResolvedVisitWorkspaceContext context,
        string actionOutcome,
        DateTime nowUtc,
        DateTime nowLocal,
        CancellationToken ct,
        IReadOnlyList<ConsultationDiagnosticOrderDto>? createdDiagnosticOrders = null)
    {
        var visit = context.Visit;
        var patient = context.Patient;
        var appointment = context.Appointment;
        var activeEntry = context.ActiveClinicalEntry;

        var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, doctorAccess.Hospital.Id, nowUtc, ct);
        var servicePointIdForPolicy = activeEntry?.CurrentServicePointId
            ?? activeEntry?.ServicePointId
            ?? context.ServicePoint?.Id;
        var policy = policySet.ForServicePoint(servicePointIdForPolicy);


        var queueDate = activeEntry?.QueueDate
            ?? (visit.CheckedInAt.HasValue
                ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
                : appointment?.Slot?.StartsAt.HasValue == true
                    ? DateOnly.FromDateTime(appointment.Slot.StartsAt.Value)
                    : DateOnly.FromDateTime(nowLocal));

        var visitDiagnosticOrders = await TryLoadVisitDiagnosticOrdersAsync(visit.Id, ct);
        var diagnosticSummary = BuildDiagnosticSummary(
            context.AllVisitQueueEntries,
            visitDiagnosticOrders,
            policy.FinalResultRequired);
        var clinicalContext = await BuildClinicalContextAsync(
            visit,
            appointment,
            context.AssignedDoctor,
            context.AllVisitQueueEntries,
            visitDiagnosticOrders,
            diagnosticSummary,
            queueDate,
            nowLocal,
            ct);
        var queueContext = await BuildQueueContextAsync(
            doctorAccess.Hospital.Id,
            context,
            policy,
            queueDate,
            diagnosticSummary.OpenQueueEntries > 0,
            nowLocal,
            ct);

        var workspaceState = ResolveWorkspaceState(visit, activeEntry, diagnosticSummary.OpenQueueEntries);
        var hasMedicalRecordWrite = doctorAccess.Permissions.Contains(PermissionMedicalRecordWrite);
        var hasDiagnosticOrder = doctorAccess.Permissions.Contains(PermissionDiagnosticOrder);
        var hasPrescriptionIssue = doctorAccess.Permissions.Contains(PermissionPrescriptionIssue);

        var isRecordFinalized = string.Equals(
            clinicalContext.MedicalRecordStatus,
            "FINALIZED",
            StringComparison.OrdinalIgnoreCase);

        var canEditClinical = workspaceState == "IN_PROGRESS" && !isRecordFinalized && hasMedicalRecordWrite;
        var readOnlyReason = ResolveReadOnlyReason(
            canEditClinical,
            workspaceState,
            isRecordFinalized,
            hasMedicalRecordWrite);

        var allowedActions = BuildAllowedActions(
            workspaceState,
            canEditClinical,
            isRecordFinalized,
            hasMedicalRecordWrite,
            hasDiagnosticOrder,
            hasPrescriptionIssue,
            diagnosticSummary);

        var visitSummary = BuildVisitSummary(
            doctorAccess.Hospital,
            context,
            queueDate);

        var patientSummary = BuildPatientSummary(
            patient,
            clinicalContext.RelevantHistoryAllergies,
            queueDate);

        return new ConsultationWorkspaceResponseDto(
            ActionOutcome: actionOutcome,
            WorkspaceState: workspaceState,
            CanEditClinical: canEditClinical,
            ReadOnlyReason: readOnlyReason,
            GeneratedAtUtc: nowUtc,
            Visit: visitSummary,
            Patient: patientSummary,
            Queue: queueContext,
            ClinicalContext: clinicalContext,
            AllowedActions: allowedActions,
            CreatedDiagnosticOrders: createdDiagnosticOrders);
    }

    private static ConsultationVisitSummaryDto BuildVisitSummary(
        Hospital hospital,
        ResolvedVisitWorkspaceContext context,
        DateOnly referenceDate)
    {
        var visit = context.Visit;
        var appointment = context.Appointment;
        var slot = appointment?.Slot;
        var room = context.Room;
        var department = context.Department;
        var specialty = context.Specialty;
        var service = context.Service;
        var assignedDoctor = context.AssignedDoctor;
        var activeEntry = context.ActiveClinicalEntry;

        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode.Trim()
            : $"VIS-{referenceDate:yyyyMM}-{visit.Id:D4}";

        var normalizedStatus = NormalizeVisitDisplayStatus(visit.Status, activeEntry?.Status);
        var statusLabel = FormatVisitStatusLabel(normalizedStatus);

        var isScheduled = visit.AppointmentId.HasValue || activeEntry?.AppointmentId.HasValue == true;
        var visitType = isScheduled ? "SCHEDULED" : "WALK_IN";
        var visitTypeLabel = isScheduled ? "Đặt lịch hẹn" : "Khám trực tiếp (Vãng lai)";

        var doctorTitle = string.IsNullOrWhiteSpace(assignedDoctor.ProfessionalTitle)
            ? "BS"
            : assignedDoctor.ProfessionalTitle.Trim();
        var doctorFullName = assignedDoctor.Staff?.FullName ?? assignedDoctor.DoctorCode;
        var formattedDoctorName = DoctorCatalogService.FormatDisplayName(doctorFullName, doctorTitle);

        return new ConsultationVisitSummaryDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            Status: normalizedStatus,
            StatusLabel: statusLabel,
            VisitType: visitType,
            VisitTypeLabel: visitTypeLabel,
            ReasonForVisit: visit.ReasonForVisit ?? appointment?.ReasonForVisit,
            AppointmentId: visit.AppointmentId,
            AppointmentCode: appointment?.AppointmentCode,
            ScheduledStartAt: slot?.StartsAt ?? activeEntry?.ScheduledAt,
            ScheduledEndAt: slot?.EndsAt,
            CheckedInAt: visit.CheckedInAt ?? activeEntry?.ArrivedAt,
            StartedAt: activeEntry?.StartedAt,
            CompletedAt: visit.CompletedAt ?? activeEntry?.CompletedAt,
            HospitalId: hospital.Id,
            HospitalCode: hospital.HospitalCode,
            HospitalName: hospital.Name,
            DepartmentId: department?.Id,
            DepartmentCode: department?.DepartmentCode,
            DepartmentName: department?.Name ?? "Khoa Khám bệnh",
            SpecialtyId: specialty?.Id,
            SpecialtyCode: specialty?.SpecialtyCode,
            SpecialtyName: specialty?.Name ?? "Đa khoa",
            RoomId: room?.Id ?? visit.ExaminationRoomId,
            RoomCode: room?.RoomCode,
            RoomName: room?.Name,
            RoomLocation: room?.Location,
            AssignedDoctorId: assignedDoctor.Id,
            AssignedDoctorCode: assignedDoctor.DoctorCode,
            AssignedDoctorName: formattedDoctorName,
            AssignedDoctorTitle: doctorTitle,
            ServiceId: service?.Id,
            ServiceCode: service?.ServiceCode,
            ServiceName: service?.Name);
    }

    private static ConsultationPatientSummaryDto BuildPatientSummary(
        Patient patient,
        string? allergiesSummary,
        DateOnly referenceDate)
    {
        var dobString = patient.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var birthYear = patient.DateOfBirth?.Year;
        var age = ClinicalQueueRules.CalculateAge(patient.DateOfBirth, referenceDate);
        var gender = string.IsNullOrWhiteSpace(patient.Gender)
            ? null
            : patient.Gender.Trim().ToUpperInvariant();
        var genderLabel = gender switch
        {
            "MALE" or "NAM" => "Nam",
            "FEMALE" or "NU" or "NỮ" => "Nữ",
            "OTHER" or "KHAC" or "KHÁC" => "Khác",
            _ => "Chưa xác định",
        };

        return new ConsultationPatientSummaryDto(
            PatientId: patient.Id,
            PatientCode: patient.PatientCode,
            FullName: patient.FullName,
            DateOfBirth: dobString,
            BirthYear: birthYear,
            Age: age,
            Gender: gender,
            GenderLabel: genderLabel,
            Phone: patient.Phone,
            Email: patient.Email,
            Address: patient.Address,
            BloodType: patient.BloodType,
            AllergiesSummary: allergiesSummary);
    }

    private async Task<ConsultationQueueContextDto?> BuildQueueContextAsync(
        long hospitalId,
        ResolvedVisitWorkspaceContext context,
        QueueOrderingPolicy policy,
        DateOnly queueDate,
        bool hasOpenDiagnostics,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var entry = context.ActiveClinicalEntry;
        if (entry is null)
        {
            return null;
        }

        var visit = context.Visit;
        var slot = context.Appointment?.Slot;
        var isScheduled = (visit.AppointmentId ?? entry.AppointmentId).HasValue;
        var normalizedStatus = NormalizeQueueStatus(entry.Status);
        var queueType = ClinicalQueueRules.ResolveQueueType(entry.QueueType, visit.Status);
        var priority = ClinicalQueueRules.ResolveEffectivePriority(
            entry.ManualPriority,
            context.Patient.DateOfBirth,
            queueDate);
        var presenceStatus = string.IsNullOrWhiteSpace(entry.PresenceStatus)
            ? "PRESENT"
            : entry.PresenceStatus.Trim().ToUpperInvariant();
        var storedArrivalClass = string.IsNullOrWhiteSpace(entry.ArrivalClass)
            ? (isScheduled ? "ON_TIME" : "WALK_IN")
            : entry.ArrivalClass.Trim().ToUpperInvariant();

        var scheduledAt = entry.ScheduledAt ?? slot?.StartsAt;
        var arrivedAt = entry.ArrivedAt ?? visit.CheckedInAt;
        var readySince = entry.ReadyAt ?? arrivedAt ?? scheduledAt;
        var queueNumber = entry.QueueNumber is > 0 ? entry.QueueNumber.Value : 1;
        var blockedReason = string.IsNullOrWhiteSpace(entry.BlockedReason) ? null : entry.BlockedReason.Trim();

        var servicePointId = entry.CurrentServicePointId ?? entry.ServicePointId ?? context.ServicePoint?.Id;
        var evaluation = await EvaluateQueueEntryPositionAsync(
            entry,
            new QueueOrderingItem(
                Status: normalizedStatus,
                Priority: priority,
                PresenceStatus: presenceStatus,
                IsScheduled: isScheduled,
                StoredArrivalClass: storedArrivalClass,
                ScheduledAt: scheduledAt,
                ArrivedAt: arrivedAt,
                ReadySince: readySince,
                BackfilledAt: entry.BackfilledAt,
                QueueNumber: queueNumber,
                QueueType: queueType,
                BlockedReason: blockedReason,
                PendingRequiredResults: hasOpenDiagnostics),
            servicePointId,
            queueDate,
            policy,
            nowLocal,
            ct);

        var queueGroup = normalizedStatus switch
        {
            "IN_PROGRESS" => QueueGroups.InProgress,
            "CALLED" => QueueGroups.Called,
            "SKIPPED" => QueueGroups.Skipped,
            "COMPLETED" => QueueGroups.Completed,
            _ => evaluation.IsEligible ? QueueGroups.Eligible : QueueGroups.NotEligible,
        };

        var formattedServicePointId = servicePointId.HasValue
            ? $"sp-{servicePointId.Value:D3}"
            : context.Room is not null
                ? $"room-{context.Room.Id:D3}"
                : $"doc-{context.AssignedDoctor.Id:D3}";

        var servicePointName = !string.IsNullOrWhiteSpace(context.ServicePoint?.Name)
            ? context.ServicePoint.Name
            : !string.IsNullOrWhiteSpace(context.Room?.Name)
                ? context.Room.Name
                : $"Buồng khám #{context.AssignedDoctor.Id}";

        return new ConsultationQueueContextDto(
            QueueEntryId: entry.Id,
            FormattedQueueEntryId: $"q-{entry.Id:D3}",
            TicketNo: $"#{queueNumber:D3}",
            QueueNumber: queueNumber,
            QueueDate: queueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            QueueType: queueType,
            Status: normalizedStatus,
            QueueGroup: queueGroup,
            IneligibleReason: evaluation.IneligibleReason,
            Priority: priority,
            PresenceStatus: presenceStatus,
            ArrivalClass: evaluation.EffectiveArrivalClass,
            ServingOrder: evaluation.ServingOrder,
            IsEligible: evaluation.IsEligible,
            IsAppointmentProtected: evaluation.IsAppointmentProtected,
            HoldReason: evaluation.HoldReason,
            BlockedReason: blockedReason,
            StateVersion: entry.StateVersion is > 0 ? entry.StateVersion.Value : 1,
            ServicePointId: formattedServicePointId,
            ServicePointName: servicePointName,
            ScheduledAt: FormatTime(scheduledAt),
            ArrivedAt: FormatTime(arrivedAt),
            ReadyAt: FormatTime(entry.ReadyAt),
            CalledAt: FormatTime(entry.CalledAt),
            StartedAt: FormatTime(entry.StartedAt),
            CompletedAt: FormatTime(entry.CompletedAt));
    }

    private async Task<QueueEntryEvaluation> EvaluateQueueEntryPositionAsync(
        QueueEntry targetEntry,
        QueueOrderingItem targetOrderingItem,
        long? servicePointId,
        DateOnly queueDate,
        QueueOrderingPolicy policy,
        DateTime nowLocal,
        CancellationToken ct)
    {
        if (!servicePointId.HasValue)
        {
            var singleResult = ExaminationQueueOrderingEngine.Evaluate([targetOrderingItem], policy, nowLocal);
            return singleResult.Evaluations[0];
        }

        var bucketEntries = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.QueueDate == queueDate &&
                (q.CurrentServicePointId == servicePointId.Value || q.ServicePointId == servicePointId.Value) &&
                (q.Status == null || q.Status.ToUpper() != "CANCELLED"))
            .OrderBy(q => q.QueueNumber)
            .ThenBy(q => q.Id)
            .ToListAsync(ct);

        var clinicalBucketEntries = bucketEntries
            .Where(q => ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType))
            .ToList();

        if (clinicalBucketEntries.All(q => q.Id != targetEntry.Id))
        {
            clinicalBucketEntries.Add(targetEntry);
        }

        var visitIds = clinicalBucketEntries
            .Where(q => q.VisitId.HasValue)
            .Select(q => q.VisitId!.Value)
            .Distinct()
            .ToList();

        var openDiagVisitIds = new HashSet<long>();
        if (visitIds.Count > 0)
        {
            var openIds = await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.VisitId.HasValue &&
                    visitIds.Contains(q.VisitId.Value) &&
                    q.QueueType != null &&
                    q.QueueType.ToUpper() == "DIAGNOSTIC" &&
                    q.CompletedAt == null &&
                    (q.Status == null ||
                     (q.Status.ToUpper() != "COMPLETED" && q.Status.ToUpper() != "CANCELLED")))
                .Select(q => q.VisitId!.Value)
                .ToListAsync(ct);

            openDiagVisitIds = openIds.ToHashSet();
        }

        var items = new List<QueueOrderingItem>(clinicalBucketEntries.Count);
        var targetIndex = 0;

        for (var i = 0; i < clinicalBucketEntries.Count; i++)
        {
            var q = clinicalBucketEntries[i];
            if (q.Id == targetEntry.Id)
            {
                targetIndex = i;
                items.Add(targetOrderingItem);
                continue;
            }

            var isSched = q.AppointmentId.HasValue;
            var status = NormalizeQueueStatus(q.Status);
            var priority = ClinicalQueueRules.NormalizeStoredPriority(q.ManualPriority);
            var presence = string.IsNullOrWhiteSpace(q.PresenceStatus)
                ? "PRESENT"
                : q.PresenceStatus.Trim().ToUpperInvariant();
            var arrivalClass = string.IsNullOrWhiteSpace(q.ArrivalClass)
                ? (isSched ? "ON_TIME" : "WALK_IN")
                : q.ArrivalClass.Trim().ToUpperInvariant();
            var qType = ClinicalQueueRules.ResolveQueueType(q.QueueType, null);
            var pendingDiag = q.VisitId.HasValue && openDiagVisitIds.Contains(q.VisitId.Value);

            items.Add(new QueueOrderingItem(
                Status: status,
                Priority: priority,
                PresenceStatus: presence,
                IsScheduled: isSched,
                StoredArrivalClass: arrivalClass,
                ScheduledAt: q.ScheduledAt,
                ArrivedAt: q.ArrivedAt,
                ReadySince: q.ReadyAt ?? q.ArrivedAt ?? q.ScheduledAt,
                BackfilledAt: q.BackfilledAt,
                QueueNumber: q.QueueNumber ?? (i + 1),
                QueueType: qType,
                BlockedReason: string.IsNullOrWhiteSpace(q.BlockedReason) ? null : q.BlockedReason.Trim(),
                PendingRequiredResults: pendingDiag));
        }

        var fairnessCounter = 0;
        try
        {
            var fairnessRow = await _db.QueueFairnessStates
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.ServicePointId == servicePointId.Value && s.QueueDate == queueDate,
                    ct);

            if (fairnessRow is not null)
            {
                fairnessCounter = QueueFairnessRotation.ResolveEffectiveCounter(
                    fairnessRow.ConsecutivePriorityStarted,
                    fairnessRow.PolicyVersion,
                    policy);
            }
        }
        catch (NotSupportedException)
        {
            fairnessCounter = 0;
        }

        var result = ExaminationQueueOrderingEngine.Evaluate(items, policy, nowLocal, fairnessCounter);
        return result.Evaluations[targetIndex];
    }

    private async Task<ConsultationClinicalContextDto> BuildClinicalContextAsync(
        Visit visit,
        Appointment? appointment,
        Doctor assignedDoctor,
        IReadOnlyList<QueueEntry> allVisitQueueEntries,
        IReadOnlyList<DiagnosticOrder> visitDiagnosticOrders,
        ConsultationDiagnosticSummaryDto diagnosticSummary,
        DateOnly referenceDate,
        DateTime nowLocal,
        CancellationToken ct)
    {
        MedicalRecord? medicalRecord = null;
        MedicalRecordRevision? latestRevision = null;
        List<VisitDiagnosis> diagnoses = [];

        try
        {
            medicalRecord = await _db.MedicalRecords
                .AsNoTracking()
                .Where(m => m.VisitId == visit.Id)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(ct);

            if (medicalRecord is not null)
            {
                latestRevision = await _db.MedicalRecordRevisions
                    .AsNoTracking()
                    .Where(r => r.MedicalRecordId == medicalRecord.Id)
                    .OrderByDescending(r => r.RevisionNo)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefaultAsync(ct);
            }
        }
        catch (NotSupportedException)
        {
            medicalRecord = null;
            latestRevision = null;
        }

        try
        {
            diagnoses = await _db.VisitDiagnoses
                .AsNoTracking()
                .Where(d => d.VisitId == visit.Id)
                .OrderByDescending(d => d.IsPrimary == true)
                .ThenBy(d => d.RecordedAt)
                .ThenBy(d => d.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            diagnoses = [];
        }

        var prescriptionSummary = await BuildPrescriptionSummaryAsync(visit.Id, ct);
        var vitalSigns = ParseVitalSigns(latestRevision?.VitalSigns);

        var diagnosisDtos = diagnoses
            .Select(d => new ConsultationDiagnosisItemDto(
                Id: d.Id,
                Icd10CodeId: d.Icd10CodeId,
                Code: d.CodeSnapshot ?? string.Empty,
                Name: d.NameSnapshot ?? string.Empty,
                IsPrimary: d.IsPrimary == true,
                ClinicalDiagnosis: d.ClinicalDiagnosis,
                Status: string.IsNullOrWhiteSpace(d.Status) ? "PRELIMINARY" : d.Status.Trim().ToUpperInvariant(),
                ConfirmedAt: d.ConfirmedAt,
                RecordedAt: d.RecordedAt))
            .ToList();

        var recordStatus = string.IsNullOrWhiteSpace(medicalRecord?.Status)
            ? "NOT_CREATED"
            : medicalRecord.Status.Trim().ToUpperInvariant();

        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode.Trim()
            : $"VIS-{referenceDate:yyyyMM}-{visit.Id:D4}";

        var diagnosticOrderDtos = await BuildDiagnosticOrderDtosAsync(
            visit,
            visitCode,
            referenceDate,
            assignedDoctor,
            allVisitQueueEntries,
            visitDiagnosticOrders,
            nowLocal,
            ct);

        return new ConsultationClinicalContextDto(
            MedicalRecordId: medicalRecord?.Id,
            MedicalRecordStatus: recordStatus,
            FinalizedAt: medicalRecord?.FinalizedAt,
            CurrentRevisionNo: latestRevision?.RevisionNo,
            IsConfirmed: latestRevision?.IsConfirmed == true,
            LastRecordedAt: latestRevision?.RecordedAt,
            PresentingComplaint: latestRevision?.PresentingComplaint ?? visit.ReasonForVisit ?? appointment?.ReasonForVisit,
            SymptomHistory: latestRevision?.SymptomHistory,
            RelevantHistoryAllergies: latestRevision?.RelevantHistoryAllergies,
            VitalSigns: vitalSigns,
            ClinicalFindings: latestRevision?.ClinicalFindings,
            ClinicalConclusion: latestRevision?.ClinicalConclusion,
            AmendmentReason: latestRevision?.AmendmentReason,
            Diagnoses: diagnosisDtos,
            DiagnosticSummary: diagnosticSummary,
            PrescriptionSummary: prescriptionSummary,
            DiagnosticOrders: diagnosticOrderDtos);
    }

    private async Task<ConsultationPrescriptionSummaryDto> BuildPrescriptionSummaryAsync(
        long visitId,
        CancellationToken ct)
    {
        try
        {
            var prescriptions = await _db.Prescriptions
                .AsNoTracking()
                .Where(p => p.VisitId == visitId)
                .OrderByDescending(p => p.IssuedAt ?? p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .ToListAsync(ct);

            if (prescriptions.Count == 0)
            {
                return new ConsultationPrescriptionSummaryDto(0, null, null, null, 0);
            }

            var latest = prescriptions[0];
            var itemCount = 0;

            try
            {
                itemCount = await _db.PrescriptionItems
                    .AsNoTracking()
                    .CountAsync(i => i.PrescriptionId == latest.Id, ct);
            }
            catch (NotSupportedException)
            {
                itemCount = 0;
            }

            return new ConsultationPrescriptionSummaryDto(
                TotalPrescriptions: prescriptions.Count,
                LatestPrescriptionCode: latest.PrescriptionCode,
                LatestPrescriptionStatus: latest.Status,
                LatestIssuedAt: latest.IssuedAt ?? latest.CreatedAt,
                ItemCount: itemCount);
        }
        catch (NotSupportedException)
        {
            return new ConsultationPrescriptionSummaryDto(0, null, null, null, 0);
        }
    }

    private static ConsultationVitalSignsDto ParseVitalSigns(string? vitalSignsJson)
    {
        if (string.IsNullOrWhiteSpace(vitalSignsJson))
        {
            return new ConsultationVitalSignsDto(null, null, null, null, null, null, null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(vitalSignsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ConsultationVitalSignsDto(null, null, null, null, null, null, null, null, null);
            }

            var root = doc.RootElement;
            var sys = TryGetInt(root, "bp_systolic_mmhg");
            var dia = TryGetInt(root, "bp_diastolic_mmhg");
            var pulse = TryGetInt(root, "pulse_bpm");
            var temp = TryGetDecimal(root, "temperature_c");
            var spo2 = TryGetInt(root, "spo2_percent");
            var heightCm = TryGetDecimal(root, "height_cm");
            var weightKg = TryGetDecimal(root, "weight_kg");

            var bpLabel = sys.HasValue && dia.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"{sys.Value}/{dia.Value} mmHg")
                : null;

            decimal? bmi = null;
            if (heightCm is > 0 && weightKg is > 0)
            {
                var heightMeters = heightCm.Value / 100m;
                bmi = Math.Round(weightKg.Value / (heightMeters * heightMeters), 1, MidpointRounding.AwayFromZero);
            }

            return new ConsultationVitalSignsDto(
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
            return new ConsultationVitalSignsDto(null, null, null, null, null, null, null, null, null);
        }
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

    private static ConsultationDiagnosticSummaryDto BuildDiagnosticSummary(
        IReadOnlyList<QueueEntry> allVisitQueueEntries,
        IReadOnlyList<DiagnosticOrder> visitDiagnosticOrders,
        bool finalResultRequired)
    {
        var diagnosticEntries = allVisitQueueEntries
            .Where(q =>
                string.Equals(q.QueueType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
                NormalizeQueueStatus(q.Status) != "CANCELLED")
            .ToList();

        var ordersByQueueEntryId = visitDiagnosticOrders
            .Where(o => o.QueueEntryId.HasValue && NormalizeQueueStatus(o.Status) != "CANCELLED")
            .GroupBy(o => o.QueueEntryId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

        var total = diagnosticEntries.Count;
        var completed = 0;
        var openMandatory = 0;

        foreach (var q in diagnosticEntries)
        {
            ordersByQueueEntryId.TryGetValue(q.Id, out var linkedOrder);
            var isCompleted = q.CompletedAt.HasValue ||
                NormalizeQueueStatus(q.Status) == "COMPLETED" ||
                (linkedOrder is not null && NormalizeQueueStatus(linkedOrder.Status) == "COMPLETED");

            if (isCompleted)
            {
                completed++;
            }
            else
            {
                var isMandatory = linkedOrder?.RequiredForConclusion ?? true;
                if (isMandatory)
                {
                    openMandatory++;
                }
            }
        }

        var open = total - completed;
        var hasUncompletedMandatoryOrder = visitDiagnosticOrders.Any(o =>
            o.RequiredForConclusion == true &&
            AppointmentBookingRules.NormalizeStatusKey(o.Status) is not
                ("COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED" or "CANCELLED"));
        var allReady = (!finalResultRequired || openMandatory == 0) && !hasUncompletedMandatoryOrder;

        return new ConsultationDiagnosticSummaryDto(
            TotalQueueEntries: total,
            OpenQueueEntries: open,
            CompletedQueueEntries: completed,
            AllMandatoryResultsReady: allReady,
            FinalResultRequired: finalResultRequired);
    }

    private async Task<List<DiagnosticOrder>> TryLoadVisitDiagnosticOrdersAsync(
        long visitId,
        CancellationToken ct)
    {
        try
        {
            return await _db.DiagnosticOrders
                .AsNoTracking()
                .Where(o => o.VisitId == visitId)
                .OrderByDescending(o => o.OrderedAt)
                .ThenByDescending(o => o.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task<MedicalRecord?> TryLoadLatestMedicalRecordAsync(
        long visitId,
        CancellationToken ct)
    {
        try
        {
            return await _db.MedicalRecords
                .AsNoTracking()
                .Where(m => m.VisitId == visitId)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<List<QueueServicePoint>> TryLoadHospitalDiagnosticServicePointsAsync(
        long hospitalId,
        CancellationToken ct)
    {
        try
        {
            return await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.HospitalId == hospitalId &&
                    sp.PointType != null &&
                    sp.PointType.ToUpper() == "DIAGNOSTIC")
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private static QueueServicePoint? ResolveDiagnosticServicePointForCatalog(
        long serviceId,
        IReadOnlyList<QueueServicePoint> hospitalDiagPoints)
    {
        var serviceSpecific = hospitalDiagPoints
            .Where(sp => sp.ServiceId == serviceId)
            .OrderBy(sp => sp.IsActive == false ? 1 : 0)
            .ThenBy(sp => sp.Id)
            .FirstOrDefault();

        if (serviceSpecific is not null)
        {
            return serviceSpecific;
        }

        return hospitalDiagPoints
            .Where(sp => sp.IsActive != false)
            .OrderBy(sp => sp.ServiceId.HasValue ? 1 : 0)
            .ThenBy(sp => sp.Id)
            .FirstOrDefault();
    }

    private static bool IsDiagnosticOrderActive(
        DiagnosticOrder order,
        IReadOnlyList<QueueEntry> allVisitQueueEntries)
    {
        var orderStatus = NormalizeQueueStatus(order.Status);
        if (orderStatus is "COMPLETED" or "CANCELLED")
        {
            return false;
        }

        if (order.QueueEntryId.HasValue)
        {
            var linkedEntry = allVisitQueueEntries.FirstOrDefault(q => q.Id == order.QueueEntryId.Value);
            if (linkedEntry is not null &&
                (linkedEntry.CompletedAt.HasValue ||
                 NormalizeQueueStatus(linkedEntry.Status) is "COMPLETED" or "CANCELLED"))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> IsDoctorAuthorizedForServiceAsync(
        long doctorId,
        long serviceId,
        CancellationToken ct)
    {
        try
        {
            var anyConfigured = await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .AnyAsync(a => a.DoctorId == doctorId || a.ServiceId == serviceId, ct);

            if (!anyConfigured)
            {
                return true;
            }

            return await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .AnyAsync(
                    a => a.DoctorId == doctorId &&
                         a.ServiceId == serviceId &&
                         a.IsActive == true,
                    ct);
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }

    private async Task<decimal> ResolveActiveServicePriceAsync(
        long serviceId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        try
        {
            var price = await _db.ServicePrices
                .AsNoTracking()
                .Where(p =>
                    p.ServiceId == serviceId &&
                    (p.EffectiveFrom == null || p.EffectiveFrom <= nowLocal) &&
                    (p.EffectiveTo == null || p.EffectiveTo >= nowLocal))
                .OrderByDescending(p => p.EffectiveFrom)
                .ThenByDescending(p => p.Id)
                .Select(p => p.PriceAmount)
                .FirstOrDefaultAsync(ct);

            return price ?? 0m;
        }
        catch (NotSupportedException)
        {
            return 0m;
        }
    }

    private async Task<IReadOnlyList<ConsultationDiagnosticOrderDto>> BuildDiagnosticOrderDtosAsync(
        Visit visit,
        string visitCode,
        DateOnly referenceDate,
        Doctor defaultOrderingDoctor,
        IReadOnlyList<QueueEntry> allVisitQueueEntries,
        IReadOnlyList<DiagnosticOrder> visitDiagnosticOrders,
        DateTime nowLocal,
        CancellationToken ct)
    {
        if (visitDiagnosticOrders.Count == 0)
        {
            return [];
        }

        var serviceIds = visitDiagnosticOrders
            .Where(o => o.ServiceId.HasValue)
            .Select(o => o.ServiceId!.Value)
            .Distinct()
            .ToList();

        var servicesById = serviceIds.Count > 0
            ? await _db.Services
                .AsNoTracking()
                .Where(s => serviceIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct)
            : new Dictionary<long, Service>();

        var specialtiesById = await _db.Specialties
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, ct);

        var doctorIds = visitDiagnosticOrders
            .Where(o => o.OrderingDoctorId.HasValue)
            .Select(o => o.OrderingDoctorId!.Value)
            .Distinct()
            .ToList();

        var doctorsById = doctorIds.Count > 0
            ? await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .Where(d => doctorIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct)
            : new Dictionary<long, Doctor>();

        var queueEntriesById = allVisitQueueEntries
            .GroupBy(q => q.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var servicePointIds = allVisitQueueEntries
            .SelectMany(q => new[] { q.CurrentServicePointId, q.ServicePointId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        Dictionary<long, QueueServicePoint> servicePointsById = [];
        if (servicePointIds.Count > 0)
        {
            try
            {
                servicePointsById = await _db.QueueServicePoints
                    .AsNoTracking()
                    .Where(sp => servicePointIds.Contains(sp.Id))
                    .ToDictionaryAsync(sp => sp.Id, ct);
            }
            catch (NotSupportedException)
            {
                servicePointsById = [];
            }
        }

        var roomIds = servicePointsById.Values
            .Where(sp => sp.RoomId.HasValue)
            .Select(sp => sp.RoomId!.Value)
            .Distinct()
            .ToList();

        var roomsById = roomIds.Count > 0
            ? await _db.HospitalRooms
                .AsNoTracking()
                .Where(r => roomIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct)
            : new Dictionary<long, HospitalRoom>();

        var orderIds = visitDiagnosticOrders.Select(o => o.Id).ToList();
        HashSet<long> finalizedOrderIds = [];
        try
        {
            var finalizedIds = await _db.DiagnosticResults
                .AsNoTracking()
                .Where(r =>
                    r.DiagnosticOrderId.HasValue &&
                    orderIds.Contains(r.DiagnosticOrderId.Value) &&
                    (r.FinalizedAt != null || (r.Status != null && r.Status.ToUpper() == "FINALIZED")))
                .Select(r => r.DiagnosticOrderId!.Value)
                .ToListAsync(ct);

            finalizedOrderIds = finalizedIds.ToHashSet();
        }
        catch (NotSupportedException)
        {
            finalizedOrderIds = [];
        }

        var dtos = new List<ConsultationDiagnosticOrderDto>(visitDiagnosticOrders.Count);
        foreach (var order in visitDiagnosticOrders)
        {
            var service = order.ServiceId.HasValue && servicesById.TryGetValue(order.ServiceId.Value, out var svc)
                ? svc
                : null;

            var orderingDoctor = order.OrderingDoctorId.HasValue &&
                                 doctorsById.TryGetValue(order.OrderingDoctorId.Value, out var doc)
                ? doc
                : defaultOrderingDoctor;

            var docTitle = string.IsNullOrWhiteSpace(orderingDoctor.ProfessionalTitle)
                ? "BS"
                : orderingDoctor.ProfessionalTitle.Trim();
            var docFullName = orderingDoctor.Staff?.FullName ?? orderingDoctor.DoctorCode;
            var formattedDocName = DoctorCatalogService.FormatDisplayName(docFullName, docTitle);

            var queueEntry = order.QueueEntryId.HasValue &&
                             queueEntriesById.TryGetValue(order.QueueEntryId.Value, out var qe)
                ? qe
                : null;

            var spId = queueEntry?.CurrentServicePointId ?? queueEntry?.ServicePointId;
            var sp = spId.HasValue && servicePointsById.TryGetValue(spId.Value, out var foundSp)
                ? foundSp
                : null;
            var room = sp?.RoomId.HasValue == true && roomsById.TryGetValue(sp.RoomId.Value, out var foundRoom)
                ? foundRoom
                : null;

            var unitPrice = service is not null
                ? await ResolveActiveServicePriceAsync(service.Id, nowLocal, ct)
                : 0m;

            var category = service is not null
                ? ResolveDiagnosticCategory(service, specialtiesById)
                : "LABORATORY";

            var orderCode = !string.IsNullOrWhiteSpace(order.OrderCode)
                ? order.OrderCode.Trim()
                : $"DIA-{referenceDate:yyyyMM}-{order.Id:D4}";

            var queueNumber = queueEntry?.QueueNumber;
            var ticketNo = queueNumber is > 0 ? $"C{queueNumber.Value:D3}" : null;
            var queueStatus = queueEntry is not null ? NormalizeQueueStatus(queueEntry.Status) : null;
            var status = string.IsNullOrWhiteSpace(order.Status)
                ? "ORDERED"
                : order.Status.Trim().ToUpperInvariant();

            var isResultFinalized = finalizedOrderIds.Contains(order.Id) ||
                status == "COMPLETED" ||
                queueEntry?.CompletedAt.HasValue == true ||
                queueStatus == "COMPLETED";

            dtos.Add(new ConsultationDiagnosticOrderDto(
                OrderId: order.Id,
                OrderCode: orderCode,
                VisitId: visit.Id,
                VisitCode: visitCode,
                OrderingDoctorId: orderingDoctor.Id,
                OrderingDoctorCode: orderingDoctor.DoctorCode,
                OrderingDoctorName: formattedDocName,
                ServiceId: order.ServiceId ?? 0,
                ServiceCode: service?.ServiceCode ?? string.Empty,
                ServiceName: service?.Name ?? string.Empty,
                Category: category,
                UnitPrice: unitPrice,
                RequiresResultVerification: service?.RequiresResultVerification ?? true,
                QueueEntryId: order.QueueEntryId,
                FormattedQueueEntryId: order.QueueEntryId.HasValue ? $"q-{order.QueueEntryId.Value:D3}" : null,
                TicketNo: ticketNo,
                QueueNumber: queueNumber,
                QueueStatus: queueStatus,
                ServicePointId: sp?.Id ?? spId,
                ServicePointName: sp?.Name,
                RoomId: room?.Id ?? sp?.RoomId,
                RoomCode: room?.RoomCode,
                RoomName: room?.Name,
                Priority: string.IsNullOrWhiteSpace(order.Priority) ? "NORMAL" : order.Priority.Trim().ToUpperInvariant(),
                Status: status,
                RequiredForConclusion: order.RequiredForConclusion ?? true,
                Instruction: order.Instruction,
                OrderedAt: order.OrderedAt ?? nowLocal,
                ConfirmedAt: order.ConfirmedAt,
                IsResultFinalized: isResultFinalized));
        }

        return dtos;
    }

    private static string ResolveDiagnosticCategory(
        Service service,
        IReadOnlyDictionary<long, Specialty> specialtiesById)
    {
        var specCode = service.SpecialtyId.HasValue &&
                       specialtiesById.TryGetValue(service.SpecialtyId.Value, out var spec)
            ? spec.SpecialtyCode
            : null;

        var code = service.ServiceCode ?? string.Empty;
        var name = service.Name ?? string.Empty;

        if (string.Equals(specCode, "IMG", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("XRAY", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("XQ", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("US", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("SA", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("CT", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("MRI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("X-quang", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Siêu âm", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Chụp", StringComparison.OrdinalIgnoreCase))
        {
            return "IMAGING";
        }

        if (string.Equals(specCode, "CARD", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("ECG", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("EEG", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("TDCN", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("ENDO", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Điện tâm", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Nội soi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Thăm dò", StringComparison.OrdinalIgnoreCase))
        {
            return "FUNCTIONAL";
        }

        return "LABORATORY";
    }

    private static int CountOpenDiagnosticEntries(IReadOnlyList<QueueEntry> allVisitQueueEntries)
        => allVisitQueueEntries.Count(q =>
            string.Equals(q.QueueType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
            !q.CompletedAt.HasValue &&
            NormalizeQueueStatus(q.Status) is not ("COMPLETED" or "CANCELLED"));

    private async Task<bool> HasPendingMandatoryDiagnosticOrdersAsync(
        long visitId,
        CancellationToken ct)
    {
        try
        {
            var mandatoryOrders = await _db.DiagnosticOrders
                .AsNoTracking()
                .Where(o => o.VisitId == visitId && o.RequiredForConclusion == true)
                .ToListAsync(ct);

            return mandatoryOrders.Any(o =>
                AppointmentBookingRules.NormalizeStatusKey(o.Status) is not
                    ("COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED" or "CANCELLED"));
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static string ResolveWorkspaceState(
        Visit visit,
        QueueEntry? activeEntry,
        int openDiagnosticCount)
    {
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        var queueStatus = NormalizeQueueStatus(activeEntry?.Status);

        if (visitStatus is "COMPLETED" or "DONE" || queueStatus == "COMPLETED")
        {
            return "COMPLETED";
        }

        if (queueStatus == "IN_PROGRESS" || visitStatus is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING")
        {
            return "IN_PROGRESS";
        }

        if (openDiagnosticCount > 0 || visitStatus is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS")
        {
            return "AWAITING_RESULTS";
        }

        if (queueStatus == "CALLED" || visitStatus == "CALLED")
        {
            return "CALLED";
        }

        var resolvedQueueType = ClinicalQueueRules.ResolveQueueType(activeEntry?.QueueType, visit.Status);
        if (resolvedQueueType == ClinicalQueueRules.QueueTypeReturnConsultation)
        {
            return "RETURN_CONSULTATION";
        }

        return "WAITING";
    }

    private static string? ResolveReadOnlyReason(
        bool canEditClinical,
        string workspaceState,
        bool isRecordFinalized,
        bool hasMedicalRecordWrite)
    {
        if (canEditClinical)
        {
            return null;
        }

        if (isRecordFinalized)
        {
            return "Hồ sơ bệnh án đã được khóa hoàn tất (FINALIZED).";
        }

        if (workspaceState == "COMPLETED")
        {
            return "Lượt khám đã hoàn tất (COMPLETED).";
        }

        if (!hasMedicalRecordWrite)
        {
            return "Bác sĩ không có quyền ghi hồ sơ bệnh án (MEDICAL_RECORD_WRITE).";
        }

        if (workspaceState == "AWAITING_RESULTS")
        {
            return "Bệnh nhân đang chờ kết quả cận lâm sàng. Vui lòng gọi bệnh nhân vào khám lại sau khi có kết quả.";
        }

        return "Bệnh nhân đang ở trạng thái chờ/đã gọi. Vui lòng bấm Bắt đầu khám (IN_PROGRESS) để ghi nhận lâm sàng.";
    }

    private static IReadOnlyList<ConsultationWorkspaceActionDto> BuildAllowedActions(
        string workspaceState,
        bool canEditClinical,
        bool isRecordFinalized,
        bool hasMedicalRecordWrite,
        bool hasDiagnosticOrder,
        bool hasPrescriptionIssue,
        ConsultationDiagnosticSummaryDto diagnosticSummary)
    {
        var canOpenConsultation = workspaceState == "CALLED" &&
            hasMedicalRecordWrite &&
            diagnosticSummary.AllMandatoryResultsReady;

        string? openDisabledReason = null;
        if (!canOpenConsultation)
        {
            if (workspaceState == "IN_PROGRESS")
            {
                openDisabledReason = "Phiên khám lâm sàng đang diễn ra (IN_PROGRESS).";
            }
            else if (workspaceState == "COMPLETED")
            {
                openDisabledReason = "Lượt khám đã hoàn tất (COMPLETED).";
            }
            else if (!hasMedicalRecordWrite)
            {
                openDisabledReason = "Bác sĩ không có quyền mở hồ sơ bệnh án (MEDICAL_RECORD_WRITE).";
            }
            else if (!diagnosticSummary.AllMandatoryResultsReady)
            {
                openDisabledReason = "Chưa có đủ kết quả cận lâm sàng bắt buộc để mở phiên khám lại.";
            }
            else
            {
                openDisabledReason = "Vui lòng gọi bệnh nhân vào phòng (CALLED) trước khi bắt đầu khám.";
            }
        }

        var clinicalDisabledReason = canEditClinical
            ? null
            : ResolveReadOnlyReason(canEditClinical, workspaceState, isRecordFinalized, hasMedicalRecordWrite);

        var canCreateDiagnosticOrder = workspaceState == "IN_PROGRESS" && !isRecordFinalized && hasDiagnosticOrder;
        var diagnosticOrderDisabledReason = canCreateDiagnosticOrder
            ? null
            : !hasDiagnosticOrder
                ? "Bác sĩ không có quyền chỉ định cận lâm sàng (DIAGNOSTIC_ORDER)."
                : clinicalDisabledReason ?? "Chỉ có thể chỉ định cận lâm sàng khi ca khám đang diễn ra (IN_PROGRESS).";

        var canRecordDiagnosis = canEditClinical && diagnosticSummary.AllMandatoryResultsReady;
        var diagnosisDisabledReason = canRecordDiagnosis
            ? null
            : !diagnosticSummary.AllMandatoryResultsReady
                ? "Chưa thể kết luận chẩn đoán khi kết quả cận lâm sàng bắt buộc chưa hoàn tất."
                : clinicalDisabledReason;

        var canCreatePrescription = canEditClinical &&
            hasPrescriptionIssue &&
            diagnosticSummary.AllMandatoryResultsReady;
        var prescriptionDisabledReason = canCreatePrescription
            ? null
            : !hasPrescriptionIssue
                ? "Bác sĩ không có quyền kê đơn thuốc (PRESCRIPTION_ISSUE)."
                : !diagnosticSummary.AllMandatoryResultsReady
                    ? "Chưa thể kê đơn thuốc khi kết quả cận lâm sàng bắt buộc chưa hoàn tất."
                    : clinicalDisabledReason;

        var canCompleteVisit = canEditClinical && diagnosticSummary.AllMandatoryResultsReady;
        var completeVisitDisabledReason = canCompleteVisit
            ? null
            : !diagnosticSummary.AllMandatoryResultsReady
                ? "Chưa thể hoàn tất lượt khám khi kết quả cận lâm sàng bắt buộc chưa hoàn tất."
                : clinicalDisabledReason;

        return
        [
            new ConsultationWorkspaceActionDto(
                Id: "open-consultation",
                Label: "Bắt đầu khám lâm sàng",
                UcCode: "UC31",
                Enabled: canOpenConsultation,
                DisabledReason: openDisabledReason),
            new ConsultationWorkspaceActionDto(
                Id: "view-medical-history",
                Label: "Xem tiền sử & lịch sử khám",
                UcCode: "UC32",
                Enabled: true,
                DisabledReason: null),
            new ConsultationWorkspaceActionDto(
                Id: "record-clinical-examination",
                Label: "Ghi nhận sinh hiệu & khám lâm sàng",
                UcCode: "UC33",
                Enabled: canEditClinical,
                DisabledReason: clinicalDisabledReason),
            new ConsultationWorkspaceActionDto(
                Id: "create-diagnostic-order",
                Label: "Chỉ định cận lâm sàng",
                UcCode: "UC34",
                Enabled: canCreateDiagnosticOrder,
                DisabledReason: diagnosticOrderDisabledReason),
            new ConsultationWorkspaceActionDto(
                Id: "record-diagnosis",
                Label: "Ghi nhận chẩn đoán & kết luận",
                UcCode: "UC36",
                Enabled: canRecordDiagnosis,
                DisabledReason: diagnosisDisabledReason),
            new ConsultationWorkspaceActionDto(
                Id: "create-prescription",
                Label: "Kê đơn thuốc ngoại trú",
                UcCode: "UC37",
                Enabled: canCreatePrescription,
                DisabledReason: prescriptionDisabledReason),
            new ConsultationWorkspaceActionDto(
                Id: "complete-visit",
                Label: "Hoàn tất lượt khám",
                UcCode: "UC38",
                Enabled: canCompleteVisit,
                DisabledReason: completeVisitDisabledReason),
        ];
    }

    private static string NormalizeQueueStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "WAITING";
        }

        var normalized = status.Trim().ToUpperInvariant();
        return normalized switch
        {
            "IN_CONSULTATION" or "SERVING" => "IN_PROGRESS",
            "DONE" => "COMPLETED",
            _ => normalized,
        };
    }

    private static string NormalizeVisitDisplayStatus(string? visitStatus, string? queueStatus)
    {
        var normalizedQueue = NormalizeQueueStatus(queueStatus);
        var normalizedVisit = AppointmentBookingRules.NormalizeStatusKey(visitStatus);

        if (normalizedVisit is "COMPLETED" or "DONE" || normalizedQueue == "COMPLETED")
        {
            return "COMPLETED";
        }

        if (normalizedQueue == "IN_PROGRESS" || normalizedVisit is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING")
        {
            return "IN_PROGRESS";
        }

        if (normalizedQueue == "CALLED" || normalizedVisit == "CALLED")
        {
            return "CALLED";
        }

        if (normalizedVisit is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS")
        {
            return "AWAITING_RESULTS";
        }

        if (normalizedVisit == "RETURN_CONSULTATION")
        {
            return "RETURN_CONSULTATION";
        }

        return string.IsNullOrWhiteSpace(normalizedVisit) ? "WAITING" : normalizedVisit;
    }

    private static string FormatVisitStatusLabel(string normalizedStatus)
        => normalizedStatus switch
        {
            "IN_PROGRESS" => "Đang khám lâm sàng",
            "CALLED" => "Đã gọi vào phòng",
            "AWAITING_RESULTS" => "Chờ kết quả cận lâm sàng",
            "RETURN_CONSULTATION" => "Chờ khám lại đọc kết quả",
            "COMPLETED" => "Đã hoàn tất khám",
            "SKIPPED" => "Tạm qua lượt",
            _ => "Đang chờ khám",
        };

    private static string? FormatTime(DateTime? value)
        => value?.ToString("HH:mm", CultureInfo.InvariantCulture);

    private sealed record DoctorAccessContext(
        Hospital Hospital,
        Staff Staff,
        Doctor Doctor,
        IReadOnlySet<string> Permissions);

    private sealed record ResolvedVisitWorkspaceContext(
        Visit Visit,
        Patient Patient,
        Appointment? Appointment,
        HospitalRoom? Room,
        Department? Department,
        Specialty? Specialty,
        Service? Service,
        QueueServicePoint? ServicePoint,
        Doctor AssignedDoctor,
        QueueEntry? ActiveClinicalEntry,
        IReadOnlyList<QueueEntry> AllVisitQueueEntries);

    private sealed record ValidatedClinicalExaminationInput(
        string? PresentingComplaint,
        string? SymptomHistory,
        string? RelevantHistoryAllergies,
        int? BpSystolicMmhg,
        int? BpDiastolicMmhg,
        int? PulseBpm,
        decimal? TemperatureC,
        int? Spo2Percent,
        decimal? HeightCm,
        decimal? WeightKg,
        string? ClinicalFindings,
        string? ClinicalConclusion,
        string? AmendmentReason);

    private sealed record ValidatedCreateDiagnosticOrderInput(
        IReadOnlyList<ValidatedDiagnosticOrderItemInput> Items,
        int? ExpectedStateVersion,
        bool TransitionToAwaitingResults,
        string? ClinicalNote);

    private sealed record ValidatedDiagnosticOrderItemInput(
        long? ServiceId,
        string? ServiceCode,
        string? Instruction,
        string Priority,
        bool RequiredForConclusion);

    private sealed record ResolvedDiagnosticOrderCreationItem(
        ValidatedDiagnosticOrderItemInput Item,
        Service Service,
        string Category,
        decimal UnitPrice,
        QueueServicePoint? ServicePoint,
        HospitalRoom? Room);

    private sealed record ValidatedDiagnosisInput(
        IReadOnlyList<ValidatedDiagnosisItemInput> Items,
        string? PrimaryClinicalDiagnosis,
        string? ClinicalConclusion,
        string? ClinicalFindings,
        string? DifferentialNotes,
        string? LifestyleInstructions,
        string? WarningSigns,
        string? AmendmentReason);

    private sealed record ValidatedDiagnosisItemInput(
        long? Icd10CodeId,
        string? Code,
        string? Name,
        bool IsPrimary,
        string? ClinicalDiagnosis,
        string Status);

    private sealed record ResolvedDiagnosisItem(
        long? Icd10CodeId,
        string CodeSnapshot,
        string NameSnapshot,
        string CatalogVersion,
        bool IsPrimary,
        string? ClinicalDiagnosis,
        string Status);
}
