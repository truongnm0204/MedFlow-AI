using MedFlow.Application.DTOs.Consultation;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.UnitTests.Consultation;

public sealed partial class ConsultationWorkspaceServiceTests
{
    [Fact]
    public async Task CreatePrescription_ValidItems_PersistsDraftWithItemsCodeAndAudit()
    {
        await OpenDiagnosedVisitAsync();
        var queueBefore = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        var visitBefore = await _db.Visits.FirstAsync(v => v.Id == 11);

        var result = await _service.CreatePrescriptionAsync(
            3,
            "VIS-202610-0011",
            new CreatePrescriptionRequest(
                Items:
                [
                    new CreatePrescriptionItemRequest(
                        MedicineId: 1,
                        PrescribedQuantity: 14,
                        SingleDoseQuantity: 1,
                        DoseScheduleText: "Sáng 1 viên trước ăn",
                        Frequency: "1 lần/ngày",
                        DurationDays: 14,
                        AdministrationRoute: "Uống",
                        Instructions: "Uống trước ăn 30 phút"),
                    new CreatePrescriptionItemRequest(
                        MedicineCode: "med-002",
                        PrescribedQuantity: 20,
                        SingleDoseQuantity: 2,
                        SingleDoseUnit: "Gói",
                        DoseScheduleText: "Sau ăn sáng và tối",
                        Frequency: "2 lần/ngày",
                        DurationDays: 7,
                        AdministrationRoute: "Uống",
                        Instructions: "Uống sau ăn"),
                ]));

        var dbRx = await _db.Prescriptions.SingleAsync(p => p.VisitId == 11);
        var expectedCode = $"RX-202610-{dbRx.Id:D4}";

        Assert.Equal("PRESCRIPTION_CREATED", result.ActionOutcome);
        Assert.Equal(1, result.ClinicalContext.PrescriptionSummary.TotalPrescriptions);
        Assert.Equal("DRAFT", result.ClinicalContext.PrescriptionSummary.LatestPrescriptionStatus);
        Assert.Equal(2, result.ClinicalContext.PrescriptionSummary.ItemCount);
        Assert.Equal(expectedCode, result.ClinicalContext.PrescriptionSummary.LatestPrescriptionCode);

        Assert.Equal("DRAFT", dbRx.Status);
        Assert.Null(dbRx.IssuedAt);
        Assert.Equal(1, dbRx.PrescribingDoctorId);
        Assert.Equal("HOSPITAL", dbRx.FulfillmentChoice);
        Assert.Equal(expectedCode, dbRx.PrescriptionCode);

        var items = await _db.PrescriptionItems
            .Where(i => i.PrescriptionId == dbRx.Id)
            .OrderBy(i => i.Id)
            .ToListAsync();
        Assert.Equal(2, items.Count);

        Assert.Equal(1, items[0].MedicineId);
        Assert.Equal(14m, items[0].PrescribedQuantity);
        Assert.Equal(1m, items[0].SingleDoseQuantity);
        Assert.Equal("Viên", items[0].SingleDoseUnit);
        Assert.Equal("Sáng 1 viên trước ăn", items[0].DoseScheduleText);
        Assert.Equal("1 lần/ngày", items[0].Frequency);
        Assert.Equal(14, items[0].DurationDays);
        Assert.Equal("Uống", items[0].AdministrationRoute);
        Assert.Equal("Uống trước ăn 30 phút", items[0].Instructions);

        Assert.Equal(2, items[1].MedicineId);
        Assert.Equal(20m, items[1].PrescribedQuantity);
        Assert.Equal(2m, items[1].SingleDoseQuantity);
        Assert.Equal("Gói", items[1].SingleDoseUnit);
        Assert.Equal("Sau ăn sáng và tối", items[1].DoseScheduleText);
        Assert.Equal("2 lần/ngày", items[1].Frequency);
        Assert.Equal(7, items[1].DurationDays);
        Assert.Equal("Uống", items[1].AdministrationRoute);
        Assert.Equal("Uống sau ăn", items[1].Instructions);

        var audit = await _db.AuditLogs.SingleAsync(a => a.Action == "PRESCRIPTION_CREATED");
        Assert.Equal("PRESCRIPTION", audit.EntityType);
        Assert.Equal(dbRx.Id.ToString(), audit.EntityId);

        var queueAfter = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        var visitAfter = await _db.Visits.FirstAsync(v => v.Id == 11);
        Assert.Equal(queueBefore.StateVersion, queueAfter.StateVersion);
        Assert.Equal(queueBefore.Status, queueAfter.Status);
        Assert.Equal(visitBefore.Status, visitAfter.Status);
    }

    [Fact]
    public async Task CreatePrescription_ExistingDraft_ReplacesItemsKeepsCodeAndAuditsUpdate()
    {
        await OpenDiagnosedVisitAsync();

        var created = await _service.CreatePrescriptionAsync(
            3,
            "VIS-202610-0011",
            new CreatePrescriptionRequest(
                Items:
                [
                    new CreatePrescriptionItemRequest(
                        MedicineId: 1,
                        PrescribedQuantity: 14,
                        Instructions: "Uống trước ăn 30 phút"),
                ]));

        var updated = await _service.CreatePrescriptionAsync(
            3,
            "VIS-202610-0011",
            new CreatePrescriptionRequest(
                Items:
                [
                    new CreatePrescriptionItemRequest(
                        MedicineCode: "MED-002",
                        PrescribedQuantity: 7,
                        SingleDoseUnit: "Gói",
                        Instructions: "Uống sau ăn"),
                ],
                FulfillmentChoice: "external_pharmacy"));

        Assert.Equal("PRESCRIPTION_UPDATED", updated.ActionOutcome);
        Assert.Equal(
            created.ClinicalContext.PrescriptionSummary.LatestPrescriptionCode,
            updated.ClinicalContext.PrescriptionSummary.LatestPrescriptionCode);

        var prescriptions = await _db.Prescriptions.Where(p => p.VisitId == 11).ToListAsync();
        Assert.Single(prescriptions);
        Assert.Equal(created.ClinicalContext.PrescriptionSummary.LatestPrescriptionCode, prescriptions[0].PrescriptionCode);
        Assert.Equal("DRAFT", prescriptions[0].Status);
        Assert.Equal("EXTERNAL_PHARMACY", prescriptions[0].FulfillmentChoice);
        Assert.Equal(1, prescriptions[0].PrescribingDoctorId);

        var items = await _db.PrescriptionItems
            .Where(i => i.PrescriptionId == prescriptions[0].Id)
            .ToListAsync();
        var item = Assert.Single(items);
        Assert.Equal(2, item.MedicineId);
        Assert.Equal(7m, item.PrescribedQuantity);
        Assert.Equal("Gói", item.SingleDoseUnit);
        Assert.Equal("Uống sau ăn", item.Instructions);

        var audits = await _db.AuditLogs
            .Where(a => a.EntityType == "PRESCRIPTION")
            .OrderBy(a => a.Id)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Equal("PRESCRIPTION_CREATED", audits[0].Action);
        Assert.Equal("PRESCRIPTION_UPDATED", audits[1].Action);
    }

    [Fact]
    public async Task CreatePrescription_ThenCompleteVisit_IssuesPrescription()
    {
        await OpenDiagnosedVisitAsync();
        await _service.CreatePrescriptionAsync(
            3,
            "VIS-202610-0011",
            new CreatePrescriptionRequest(
                Items:
                [
                    new CreatePrescriptionItemRequest(
                        MedicineId: 1,
                        PrescribedQuantity: 14,
                        DoseScheduleText: "Sáng 1 viên",
                        Instructions: "Uống trước ăn 30 phút"),
                ]));

        var completed = await _service.CompleteVisitAsync(3, "q-011");

        Assert.Equal("VISIT_COMPLETED", completed.ActionOutcome);
        Assert.Equal("ISSUED", completed.ClinicalContext.PrescriptionSummary.LatestPrescriptionStatus);
        Assert.NotNull(completed.ClinicalContext.PrescriptionSummary.LatestIssuedAt);

        var dbRx = await _db.Prescriptions.SingleAsync(p => p.VisitId == 11);
        Assert.Equal("ISSUED", dbRx.Status);
        Assert.NotNull(dbRx.IssuedAt);
    }

    [Fact]
    public async Task CreatePrescription_InvalidPayload_ThrowsValidationException()
    {
        await OpenDiagnosedVisitAsync();
        var baseline = await _db.Prescriptions.CountAsync();

        var nullRequest = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(3, "VIS-202610-0011", null!));
        Assert.Equal("Dữ liệu đơn thuốc không được để trống.", nullRequest.Message);

        var emptyItems = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(Items: [])));
        Assert.Equal("Đơn thuốc phải có ít nhất một thuốc.", emptyItems.Message);

        var zeroQuantity = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineId: 1,
                            PrescribedQuantity: 0,
                            Instructions: "Uống trước ăn"),
                    ])));
        Assert.Equal("Số lượng kê đơn phải lớn hơn 0 và không vượt quá 9999.", zeroQuantity.Message);

        var missingInstructions = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineId: 1,
                            PrescribedQuantity: 14),
                    ])));
        Assert.Equal("Vui lòng nhập hướng dẫn sử dụng cho từng thuốc.", missingInstructions.Message);

        var unsafeInstructions = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineId: 1,
                            PrescribedQuantity: 14,
                            Instructions: "<script>alert(1)</script>"),
                    ])));
        Assert.Equal("Hướng dẫn sử dụng không được chứa ký tự < hoặc >.", unsafeInstructions.Message);

        var duplicate = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineId: 1,
                            PrescribedQuantity: 14,
                            Instructions: "Uống trước ăn"),
                        new CreatePrescriptionItemRequest(
                            MedicineId: 1,
                            PrescribedQuantity: 7,
                            Instructions: "Uống sau ăn"),
                    ])));
        Assert.Equal("Thuốc bị trùng lặp trong đơn. Vui lòng gộp số lượng vào một dòng.", duplicate.Message);

        var unknownCode = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineCode: "MED-999",
                            PrescribedQuantity: 14,
                            Instructions: "Uống trước ăn"),
                    ])));
        Assert.Equal("Thuốc 'MED-999' không tồn tại trong danh mục của cơ sở y tế.", unknownCode.Message);

        var inactive = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineCode: "MED-003",
                            PrescribedQuantity: 14,
                            Instructions: "Uống trước ăn"),
                    ])));
        Assert.Equal("Thuốc 'MED-003' đã ngừng sử dụng, không thể kê đơn.", inactive.Message);

        var otherHospital = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineCode: "MED-004",
                            PrescribedQuantity: 14,
                            Instructions: "Uống trước ăn"),
                    ])));
        Assert.Equal("Thuốc 'MED-004' không tồn tại trong danh mục của cơ sở y tế.", otherHospital.Message);

        var badFulfillment = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineId: 1,
                            PrescribedQuantity: 14,
                            Instructions: "Uống trước ăn"),
                    ],
                    FulfillmentChoice: "HOME_DELIVERY")));
        Assert.Equal("Hình thức nhận thuốc (FulfillmentChoice) không hợp lệ.", badFulfillment.Message);

        var badStateVersion = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                new CreatePrescriptionRequest(
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineId: 1,
                            PrescribedQuantity: 14,
                            Instructions: "Uống trước ăn"),
                    ],
                    ExpectedStateVersion: 0)));
        Assert.Equal(
            "Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.",
            badStateVersion.Message);

        Assert.Equal(baseline, await _db.Prescriptions.CountAsync());
        Assert.Equal(0, await _db.AuditLogs.CountAsync(a => a.Action.StartsWith("PRESCRIPTION_")));
    }

    [Fact]
    public async Task CreatePrescription_UnauthorizedOrForbiddenDoctor_ThrowsAndDoesNotPersist()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        var baseline = await _db.Prescriptions.CountAsync();
        var request = new CreatePrescriptionRequest(
            Items:
            [
                new CreatePrescriptionItemRequest(
                    MedicineId: 1,
                    PrescribedQuantity: 14,
                    Instructions: "Uống trước ăn 30 phút"),
            ]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.CreatePrescriptionAsync(0, "VIS-202610-0011", request));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreatePrescriptionAsync(6, "VIS-202610-0011", request));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreatePrescriptionAsync(3, "VIS-202610-0021", request));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreatePrescriptionAsync(3, "VIS-202610-0002", request));

        var issuePerm = await _db.RolePermissions
            .FirstAsync(rp => rp.RoleId == 3 && rp.PermissionId == 3);
        _db.RolePermissions.Remove(issuePerm);
        await _db.SaveChangesAsync();

        var missingPermission = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreatePrescriptionAsync(3, "VIS-202610-0011", request));
        Assert.Contains("PRESCRIPTION_ISSUE", missingPermission.Message);

        Assert.Equal(baseline, await _db.Prescriptions.CountAsync());
        Assert.Equal(0, await _db.AuditLogs.CountAsync(a => a.Action.StartsWith("PRESCRIPTION_")));
    }

    [Fact]
    public async Task CreatePrescription_StateGuards_ThrowConflictException()
    {
        var baseline = await _db.Prescriptions.CountAsync();
        var request = new CreatePrescriptionRequest(
            Items:
            [
                new CreatePrescriptionItemRequest(
                    MedicineId: 1,
                    PrescribedQuantity: 14,
                    Instructions: "Uống trước ăn 30 phút"),
            ]);

        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        var missingDiagnosis = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreatePrescriptionAsync(3, "VIS-202610-0011", request));
        Assert.Contains("chẩn đoán bệnh chính", missingDiagnosis.Message);

        await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: "K29.5",
                ClinicalConclusion: "Viêm dạ dày mạn tính, kê đơn điều trị ngoại trú"));

        var stateVersion = (await _db.QueueEntries.FirstAsync(q => q.Id == 11)).StateVersion ?? 1;
        var staleVersion = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreatePrescriptionAsync(
                3,
                "VIS-202610-0011",
                request with { ExpectedStateVersion = stateVersion + 9 }));
        Assert.Contains("state_version mismatch", staleVersion.Message);

        await _service.CompleteVisitAsync(3, "VIS-202610-0011");
        var completed = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreatePrescriptionAsync(3, "VIS-202610-0011", request));
        Assert.Contains("COMPLETED", completed.Message);

        Assert.Equal(baseline, await _db.Prescriptions.CountAsync());
    }

    private async Task OpenDiagnosedVisitAsync()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: "K29.5",
                ClinicalConclusion: "Viêm dạ dày mạn tính, kê đơn điều trị ngoại trú"));
    }
}
