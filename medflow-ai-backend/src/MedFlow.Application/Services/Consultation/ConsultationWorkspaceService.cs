using System.Globalization;
using System.Text.Json;
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

        var doctorAccess = await EnsureDoctorAccessAsync(actorUserId, nowUtc, requireWritePermission: false, ct);
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

    private async Task<DoctorAccessContext> EnsureDoctorAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        bool requireWritePermission,
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

        if (requireWritePermission && !actorPermissions.Contains(PermissionMedicalRecordWrite))
        {
            throw new ForbiddenException(
                "Bác sĩ không có quyền mở hoặc cập nhật hồ sơ khám lâm sàng (MEDICAL_RECORD_WRITE).");
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
        CancellationToken ct)
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

        var diagnosticSummary = BuildDiagnosticSummary(context.AllVisitQueueEntries, policy.FinalResultRequired);
        var clinicalContext = await BuildClinicalContextAsync(
            visit,
            appointment,
            diagnosticSummary,
            ct);

        var queueDate = activeEntry?.QueueDate
            ?? (visit.CheckedInAt.HasValue
                ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
                : appointment?.Slot?.StartsAt.HasValue == true
                    ? DateOnly.FromDateTime(appointment.Slot.StartsAt.Value)
                    : DateOnly.FromDateTime(nowLocal));

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
            AllowedActions: allowedActions);
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
        ConsultationDiagnosticSummaryDto diagnosticSummary,
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
            PrescriptionSummary: prescriptionSummary);
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
        bool finalResultRequired)
    {
        var diagnosticEntries = allVisitQueueEntries
            .Where(q =>
                string.Equals(q.QueueType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
                NormalizeQueueStatus(q.Status) != "CANCELLED")
            .ToList();

        var total = diagnosticEntries.Count;
        var completed = diagnosticEntries.Count(q =>
            q.CompletedAt.HasValue || NormalizeQueueStatus(q.Status) == "COMPLETED");
        var open = total - completed;
        var allReady = !finalResultRequired || open == 0;

        return new ConsultationDiagnosticSummaryDto(
            TotalQueueEntries: total,
            OpenQueueEntries: open,
            CompletedQueueEntries: completed,
            AllMandatoryResultsReady: allReady,
            FinalResultRequired: finalResultRequired);
    }

    private static int CountOpenDiagnosticEntries(IReadOnlyList<QueueEntry> allVisitQueueEntries)
        => allVisitQueueEntries.Count(q =>
            string.Equals(q.QueueType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
            !q.CompletedAt.HasValue &&
            NormalizeQueueStatus(q.Status) is not ("COMPLETED" or "CANCELLED"));

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
}
