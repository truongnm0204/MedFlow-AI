using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Visits;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Consultation;

public sealed partial class ConsultationWorkspaceService
{
    private const string DuplicatePrescriptionMedicineMessage =
        "Thuốc bị trùng lặp trong đơn. Vui lòng gộp số lượng vào một dòng.";

    public async Task<ConsultationWorkspaceResponseDto> CreatePrescriptionAsync(
        long actorUserId,
        string visitIdOrCode,
        CreatePrescriptionRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (numericId, code, isQueuePrefix) = ParseVisitOrQueueIdentifier(visitIdOrCode);
        var validated = ValidateCreatePrescriptionRequest(request);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var doctorAccess = await EnsureDoctorAccessAsync(actorUserId, nowUtc, PermissionPrescriptionIssue, ct);
        if (!doctorAccess.Permissions.Contains(PermissionMedicalRecordWrite))
        {
            throw new ForbiddenException(
                "Bác sĩ không có quyền mở hoặc cập nhật hồ sơ khám lâm sàng (MEDICAL_RECORD_WRITE).");
        }

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
            throw new ConflictException("Không thể kê đơn thuốc cho lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (visitStatus is "COMPLETED" or "DONE" ||
            (activeEntry is not null && NormalizeQueueStatus(activeEntry.Status) == "COMPLETED"))
        {
            throw new ConflictException("Lượt khám đã hoàn tất (COMPLETED), không thể kê đơn thuốc.");
        }

        if (activeEntry is null || NormalizeQueueStatus(activeEntry.Status) == "CANCELLED")
        {
            throw new ConflictException("Lượt khám chưa có số thứ tự hàng đợi lâm sàng hợp lệ để kê đơn thuốc.");
        }

        var currentQueueStatus = NormalizeQueueStatus(activeEntry.Status);
        var openDiagnosticCount = CountOpenDiagnosticEntries(context.AllVisitQueueEntries);

        if (visitStatus is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS" ||
            (currentQueueStatus != "IN_PROGRESS" && openDiagnosticCount > 0))
        {
            throw new ConflictException(
                "Bệnh nhân đang chờ kết quả cận lâm sàng. Vui lòng mở lại phiên khám (IN_PROGRESS) trước khi kê đơn thuốc.");
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
                "Chưa thể kê đơn thuốc khi kết quả cận lâm sàng bắt buộc chưa hoàn tất.");
        }

        if (currentQueueStatus != "IN_PROGRESS" ||
            visitStatus is not ("IN_PROGRESS" or "IN_CONSULTATION" or "SERVING"))
        {
            var effectiveStatus = currentQueueStatus != "IN_PROGRESS" ? currentQueueStatus : visitStatus;
            throw new ConflictException(
                $"Lượt khám hiện đang ở trạng thái {effectiveStatus}. Vui lòng bắt đầu phiên khám lâm sàng (IN_PROGRESS) trước khi kê đơn thuốc.");
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

        var medicalRecord = await _db.MedicalRecords
            .Where(m => m.VisitId == visit.Id)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);

        if (medicalRecord is null)
        {
            throw new ConflictException(
                "Chưa thể kê đơn thuốc khi chưa khởi tạo hồ sơ bệnh án lâm sàng.");
        }

        if (string.Equals(medicalRecord.Status?.Trim(), "FINALIZED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "Hồ sơ bệnh án đã được khóa hoàn tất (FINALIZED), không thể kê đơn thuốc.");
        }

        var latestRevision = await _db.MedicalRecordRevisions
            .AsNoTracking()
            .Where(r => r.MedicalRecordId == medicalRecord.Id)
            .OrderByDescending(r => r.RevisionNo)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var currentRevisionNo = latestRevision?.RevisionNo ?? 0;
        if (validated.ExpectedRevisionNo.HasValue &&
            validated.ExpectedRevisionNo.Value != currentRevisionNo)
        {
            throw new ConflictException(
                "Hồ sơ khám lâm sàng đã có phiên bản mới hơn (revision_no mismatch). Vui lòng tải lại trước khi kê đơn thuốc.");
        }

        var hasPrimaryDiagnosis = await _db.VisitDiagnoses.AnyAsync(
            d => d.VisitId == visit.Id &&
                 d.IsPrimary == true &&
                 d.CodeSnapshot != null &&
                 d.CodeSnapshot != "",
            ct);

        if (!hasPrimaryDiagnosis)
        {
            throw new ConflictException(
                "Chưa thể kê đơn thuốc khi chưa ghi nhận chẩn đoán bệnh chính (ICD-10).");
        }

        var resolvedItems = await ResolvePrescriptionMedicinesAsync(
            validated,
            doctorAccess.Hospital.Id,
            ct);

        var visitPrescriptions = await _db.Prescriptions
            .Where(p => p.VisitId == visit.Id)
            .ToListAsync(ct);

        var existingDraft = visitPrescriptions
            .Where(p => AppointmentBookingRules.NormalizeStatusKey(p.Status) is "DRAFT" or "PENDING" or "")
            .OrderByDescending(p => p.Id)
            .FirstOrDefault();

        var queueDate = activeEntry.QueueDate ?? DateOnly.FromDateTime(nowLocal);
        var previousRows = new List<PrescriptionItem>();
        Prescription prescription;
        string outcome;

        if (existingDraft is null)
        {
            prescription = new Prescription
            {
                VisitId = visit.Id,
                PrescribingDoctorId = doctorAccess.Doctor.Id,
                FulfillmentChoice = validated.FulfillmentChoice,
                Status = "DRAFT",
                CreatedAt = nowLocal,
                IssuedAt = null,
                ReplacesPrescriptionId = null,
            };
            _db.Prescriptions.Add(prescription);
            await SaveChangesWithConcurrencyGuardAsync(ct);
            prescription.PrescriptionCode = FormatPrescriptionCode(queueDate, prescription.Id);
            outcome = "PRESCRIPTION_CREATED";
        }
        else
        {
            previousRows = await _db.PrescriptionItems
                .Where(i => i.PrescriptionId == existingDraft.Id)
                .ToListAsync(ct);
            _db.PrescriptionItems.RemoveRange(previousRows);

            existingDraft.FulfillmentChoice = validated.FulfillmentChoice;
            existingDraft.PrescribingDoctorId = doctorAccess.Doctor.Id;
            existingDraft.Status = "DRAFT";
            if (string.IsNullOrWhiteSpace(existingDraft.PrescriptionCode))
            {
                existingDraft.PrescriptionCode = FormatPrescriptionCode(queueDate, existingDraft.Id);
            }

            prescription = existingDraft;
            outcome = "PRESCRIPTION_UPDATED";
        }

        var persistedItems = new List<PrescriptionItem>(resolvedItems.Count);
        foreach (var (item, medicine) in resolvedItems)
        {
            var persisted = new PrescriptionItem
            {
                PrescriptionId = prescription.Id,
                MedicineId = medicine.Id,
                PrescribedQuantity = item.PrescribedQuantity,
                SingleDoseQuantity = item.SingleDoseQuantity,
                SingleDoseUnit = item.SingleDoseUnit ?? medicine.DispensingUnit,
                DoseScheduleText = item.DoseScheduleText,
                Frequency = item.Frequency,
                DurationDays = item.DurationDays,
                AdministrationRoute = item.AdministrationRoute,
                Instructions = item.Instructions,
            };
            persistedItems.Add(persisted);
            _db.PrescriptionItems.Add(persisted);
        }

        var previousItemsPayload = await BuildPreviousPrescriptionItemAuditAsync(previousRows, ct);
        var itemsPayload = new List<object>(resolvedItems.Count);
        for (var index = 0; index < resolvedItems.Count; index++)
        {
            var (item, medicine) = resolvedItems[index];
            var persisted = persistedItems[index];
            itemsPayload.Add(BuildPrescriptionItemAuditPayload(
                medicine.Id,
                medicine.MedicineCode,
                medicine.Name,
                persisted.PrescribedQuantity,
                persisted.SingleDoseQuantity,
                persisted.SingleDoseUnit,
                persisted.DoseScheduleText,
                persisted.Frequency,
                persisted.DurationDays,
                persisted.AdministrationRoute,
                persisted.Instructions));
        }

        var changePayload = new
        {
            visitId = visit.Id,
            visitCode = visit.VisitCode,
            prescriptionId = prescription.Id,
            prescriptionCode = prescription.PrescriptionCode,
            status = prescription.Status,
            fulfillmentChoice = prescription.FulfillmentChoice,
            items = itemsPayload,
            previousItems = previousItemsPayload,
            stateVersion = currentStateVersion,
            revisionNo = currentRevisionNo,
            ipAddress,
        };

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = doctorAccess.Hospital.Id,
            Action = outcome,
            EntityType = "PRESCRIPTION",
            EntityId = prescription.Id.ToString(CultureInfo.InvariantCulture),
            Reason = outcome == "PRESCRIPTION_CREATED"
                ? "Bác sĩ kê đơn thuốc ngoại trú cho lượt khám"
                : "Bác sĩ cập nhật đơn thuốc nháp của lượt khám",
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
            actionOutcome: outcome,
            nowUtc,
            nowLocal,
            ct);
    }

    private static ValidatedCreatePrescriptionInput ValidateCreatePrescriptionRequest(CreatePrescriptionRequest? request)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu đơn thuốc không được để trống.");
        }

        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        if (request.ExpectedRevisionNo is < 1)
        {
            throw new ValidationException("Phiên bản hồ sơ bệnh án (ExpectedRevisionNo) phải lớn hơn hoặc bằng 1.");
        }

        var fulfillmentChoice = string.IsNullOrWhiteSpace(request.FulfillmentChoice)
            ? "HOSPITAL"
            : request.FulfillmentChoice.Trim().ToUpperInvariant();
        if (fulfillmentChoice is not ("HOSPITAL" or "HOSPITAL_PHARMACY" or "EXTERNAL_PHARMACY"))
        {
            throw new ValidationException("Hình thức nhận thuốc (FulfillmentChoice) không hợp lệ.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ValidationException("Đơn thuốc phải có ít nhất một thuốc.");
        }

        if (request.Items.Count > 20)
        {
            throw new ValidationException("Mỗi đơn thuốc tối đa 20 thuốc.");
        }

        var items = new List<ValidatedPrescriptionItemInput>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (item is null)
            {
                throw new ValidationException("Thông tin thuốc trong đơn không hợp lệ.");
            }

            if (item.MedicineId is <= 0)
            {
                throw new ValidationException("ID thuốc phải lớn hơn 0.");
            }

            string? medicineCode = null;
            if (!string.IsNullOrWhiteSpace(item.MedicineCode))
            {
                var trimmedCode = item.MedicineCode.Trim();
                if (trimmedCode.Length > 40 || trimmedCode.Contains('<') || trimmedCode.Contains('>'))
                {
                    throw new ValidationException("Mã thuốc không hợp lệ.");
                }

                medicineCode = trimmedCode.ToUpperInvariant();
            }

            if (!item.MedicineId.HasValue && medicineCode is null)
            {
                throw new ValidationException("Vui lòng cung cấp ID hoặc mã thuốc.");
            }

            if (!item.PrescribedQuantity.HasValue ||
                item.PrescribedQuantity.Value <= 0 ||
                item.PrescribedQuantity.Value > 9999)
            {
                throw new ValidationException("Số lượng kê đơn phải lớn hơn 0 và không vượt quá 9999.");
            }

            if (item.SingleDoseQuantity.HasValue &&
                (item.SingleDoseQuantity.Value <= 0 || item.SingleDoseQuantity.Value > 9999))
            {
                throw new ValidationException("Liều mỗi lần phải lớn hơn 0 và không vượt quá 9999.");
            }

            if (item.DurationDays.HasValue &&
                (item.DurationDays.Value < 1 || item.DurationDays.Value > 365))
            {
                throw new ValidationException("Số ngày dùng thuốc phải từ 1 đến 365.");
            }

            var singleDoseUnit = ValidateClinicalTextField(item.SingleDoseUnit, "Đơn vị liều", 40);
            var frequency = ValidateClinicalTextField(item.Frequency, "Tần suất dùng thuốc", 160);
            var administrationRoute = ValidateClinicalTextField(item.AdministrationRoute, "Đường dùng", 100);
            var doseScheduleText = ValidateClinicalTextField(item.DoseScheduleText, "Lịch dùng thuốc", 500);
            var instructions = ValidateClinicalTextField(item.Instructions, "Hướng dẫn sử dụng", 1000);

            if (doseScheduleText is null && instructions is null)
            {
                throw new ValidationException("Vui lòng nhập hướng dẫn sử dụng cho từng thuốc.");
            }

            items.Add(new ValidatedPrescriptionItemInput(
                MedicineId: item.MedicineId,
                MedicineCode: medicineCode,
                PrescribedQuantity: item.PrescribedQuantity.Value,
                SingleDoseQuantity: item.SingleDoseQuantity,
                SingleDoseUnit: singleDoseUnit,
                DoseScheduleText: doseScheduleText,
                Frequency: frequency,
                DurationDays: item.DurationDays,
                AdministrationRoute: administrationRoute,
                Instructions: instructions));
        }

        var seenIds = new HashSet<long>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.MedicineId.HasValue && !seenIds.Add(item.MedicineId.Value))
            {
                throw new ValidationException(DuplicatePrescriptionMedicineMessage);
            }

            if (item.MedicineCode is not null && !seenCodes.Add(item.MedicineCode))
            {
                throw new ValidationException(DuplicatePrescriptionMedicineMessage);
            }
        }

        return new ValidatedCreatePrescriptionInput(
            Items: items,
            FulfillmentChoice: fulfillmentChoice,
            ExpectedStateVersion: request.ExpectedStateVersion,
            ExpectedRevisionNo: request.ExpectedRevisionNo);
    }

    private async Task<IReadOnlyList<(ValidatedPrescriptionItemInput Item, Medicine Medicine)>> ResolvePrescriptionMedicinesAsync(
        ValidatedCreatePrescriptionInput validated,
        long hospitalId,
        CancellationToken ct)
    {
        var ids = validated.Items
            .Where(i => i.MedicineId.HasValue)
            .Select(i => i.MedicineId!.Value)
            .Distinct()
            .ToList();
        var codes = validated.Items
            .Select(i => i.MedicineCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var medicines = await _db.Medicines
            .AsNoTracking()
            .Where(m =>
                m.HospitalId == hospitalId &&
                (ids.Contains(m.Id) ||
                 (m.MedicineCode != null && codes.Contains(m.MedicineCode.ToUpper()))))
            .ToListAsync(ct);

        var resolved = new List<(ValidatedPrescriptionItemInput Item, Medicine Medicine)>(validated.Items.Count);
        var seenMedicineIds = new HashSet<long>();

        foreach (var item in validated.Items)
        {
            Medicine? medicine;
            if (item.MedicineId.HasValue)
            {
                medicine = medicines.FirstOrDefault(m => m.Id == item.MedicineId.Value);
                if (medicine is null)
                {
                    throw new ValidationException(
                        $"Thuốc '{item.MedicineId.Value.ToString(CultureInfo.InvariantCulture)}' không tồn tại trong danh mục của cơ sở y tế.");
                }

                if (!string.IsNullOrWhiteSpace(item.MedicineCode) &&
                    !string.Equals(medicine.MedicineCode?.Trim(), item.MedicineCode, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException("ID và mã thuốc không khớp nhau.");
                }
            }
            else
            {
                var codeMatches = medicines
                    .Where(m => string.Equals(
                        m.MedicineCode?.Trim(),
                        item.MedicineCode,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (codeMatches.Count > 1)
                {
                    throw new ValidationException(
                        $"Mã thuốc '{item.MedicineCode}' không duy nhất trong danh mục, vui lòng chọn theo ID.");
                }

                medicine = codeMatches.Count == 1 ? codeMatches[0] : null;
                if (medicine is null)
                {
                    throw new ValidationException(
                        $"Thuốc '{item.MedicineCode}' không tồn tại trong danh mục của cơ sở y tế.");
                }
            }

            if (medicine.IsActive == false)
            {
                var label = string.IsNullOrWhiteSpace(medicine.MedicineCode)
                    ? item.MedicineCode ?? item.MedicineId?.ToString(CultureInfo.InvariantCulture)
                    : medicine.MedicineCode.Trim();
                throw new ValidationException($"Thuốc '{label}' đã ngừng sử dụng, không thể kê đơn.");
            }

            if (!seenMedicineIds.Add(medicine.Id))
            {
                throw new ValidationException(DuplicatePrescriptionMedicineMessage);
            }

            resolved.Add((item, medicine));
        }

        return resolved;
    }

    private async Task<IReadOnlyList<object>> BuildPreviousPrescriptionItemAuditAsync(
        IReadOnlyList<PrescriptionItem> previousRows,
        CancellationToken ct)
    {
        if (previousRows.Count == 0)
        {
            return [];
        }

        var medicineIds = previousRows
            .Where(i => i.MedicineId.HasValue)
            .Select(i => i.MedicineId!.Value)
            .Distinct()
            .ToList();

        var medicines = medicineIds.Count == 0
            ? new Dictionary<long, Medicine>()
            : (await _db.Medicines
                .AsNoTracking()
                .Where(m => medicineIds.Contains(m.Id))
                .ToListAsync(ct))
                .ToDictionary(m => m.Id);

        var payload = new List<object>(previousRows.Count);
        foreach (var row in previousRows)
        {
            Medicine? medicine = null;
            if (row.MedicineId.HasValue)
            {
                medicines.TryGetValue(row.MedicineId.Value, out medicine);
            }

            payload.Add(BuildPrescriptionItemAuditPayload(
                row.MedicineId,
                medicine?.MedicineCode,
                medicine?.Name,
                row.PrescribedQuantity,
                row.SingleDoseQuantity,
                row.SingleDoseUnit,
                row.DoseScheduleText,
                row.Frequency,
                row.DurationDays,
                row.AdministrationRoute,
                row.Instructions));
        }

        return payload;
    }

    private static object BuildPrescriptionItemAuditPayload(
        long? medicineId,
        string? medicineCode,
        string? medicineName,
        decimal? prescribedQuantity,
        decimal? singleDoseQuantity,
        string? singleDoseUnit,
        string? doseScheduleText,
        string? frequency,
        int? durationDays,
        string? administrationRoute,
        string? instructions)
        => new
        {
            medicineId,
            medicineCode,
            medicineName,
            prescribedQuantity,
            singleDoseQuantity,
            singleDoseUnit,
            doseScheduleText,
            frequency,
            durationDays,
            administrationRoute,
            instructions,
        };

    private static string FormatPrescriptionCode(DateOnly queueDate, long prescriptionId)
        => $"RX-{queueDate:yyyyMM}-{prescriptionId:D4}";

    private sealed record ValidatedCreatePrescriptionInput(
        IReadOnlyList<ValidatedPrescriptionItemInput> Items,
        string FulfillmentChoice,
        int? ExpectedStateVersion,
        int? ExpectedRevisionNo);

    private sealed record ValidatedPrescriptionItemInput(
        long? MedicineId,
        string? MedicineCode,
        decimal PrescribedQuantity,
        decimal? SingleDoseQuantity,
        string? SingleDoseUnit,
        string? DoseScheduleText,
        string? Frequency,
        int? DurationDays,
        string? AdministrationRoute,
        string? Instructions);
}
