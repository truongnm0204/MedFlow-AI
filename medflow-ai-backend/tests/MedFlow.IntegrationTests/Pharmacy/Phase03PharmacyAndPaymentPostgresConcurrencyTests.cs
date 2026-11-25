using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Pharmacy;

/// <summary>
/// Phase 03 (GH-841 / ADR-0059 / ADR-0047) Mandatory Real-PostgreSQL Concurrency &amp; State-Machine Tests.
/// Runs against isolated PostgreSQL database (medflow_test_p03) using independent DI scopes / connections.
/// </summary>
public sealed class Phase03PharmacyAndPaymentPostgresConcurrencyTests : AuthTestBase
{
    public Phase03PharmacyAndPaymentPostgresConcurrencyTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    // =========================================================================
    // GROUP 1: Lock-Ordered Stock Deduction, Per-Batch Sum & Anti-Oversell
    // =========================================================================

    [Fact]
    public async Task Group1A_ConcurrentTwoPharmacistsSameBatch_10OnHand_8Plus8_OneSucceedsAndOneConflicts_ZeroNegativeStock()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var visit1 = await AddPaidVisitAsync(fx, "V1A-1");
            var visit2 = await AddPaidVisitAsync(fx, "V1A-2");
            var rx1 = await AddPrescriptionAsync(fx, visit1.Id, "RX1A-1", [(fx.MedicineId, 8m)]);
            var rx2 = await AddPrescriptionAsync(fx, visit2.Id, "RX1A-2", [(fx.MedicineId, 8m)]);
            var batchId = fx.BatchIds[0];

            using var ready = new SemaphoreSlim(0, 2);
            var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<Exception?> RunPharmacistAsync(long actorUserId, Prescription rx, long pItemId, string key)
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IConfirmMedicineDispensingService>();
                ready.Release();
                await releaseGate.Task;

                try
                {
                    await svc.ConfirmDispenseAsync(
                        actorUserId,
                        rx.PrescriptionCode!,
                        new ConfirmDispenseRequest(
                            ReceiverName: "Nguyễn Văn Test",
                            Relationship: "Bản thân",
                            IsIdentityVerified: true,
                            ChecksConfirmed: true,
                            IdempotencyKey: key,
                            Items:
                            [
                                new ConfirmDispenseItemRequest(
                                    PrescriptionItemId: pItemId,
                                    MedicineBatchId: batchId,
                                    DispensedQuantity: 8m)
                            ]));
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var t1 = RunPharmacistAsync(fx.PharmacistUserId, rx1.Prescription, rx1.ItemIds[0], $"idem-1a-{Guid.NewGuid():N}");
            var t2 = RunPharmacistAsync(fx.Pharmacist2UserId, rx2.Prescription, rx2.ItemIds[0], $"idem-1b-{Guid.NewGuid():N}");

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            releaseGate.SetResult();

            var outcomes = await Task.WhenAll(t1, t2);
            var succeededCount = outcomes.Count(e => e is null);
            var conflictCount = outcomes.Count(e => e is ConflictException);

            Assert.Equal(1, succeededCount);
            Assert.Equal(1, conflictCount);

            Db.ChangeTracker.Clear();
            var reloadedBatch = await Db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == batchId);
            Assert.Equal(2m, reloadedBatch.QuantityOnHand);

            var stockTxs = await Db.StockTransactions.AsNoTracking()
                .Where(st => st.MedicineBatchId == batchId)
                .ToListAsync();
            Assert.Single(stockTxs);
            Assert.Equal(-8m, stockTxs[0].QuantityDelta);

            var rxStatuses = await Db.Prescriptions.AsNoTracking()
                .Where(p => p.Id == rx1.Prescription.Id || p.Id == rx2.Prescription.Id)
                .Select(p => p.Status)
                .ToListAsync();
            Assert.Contains("DISPENSED", rxStatuses);
            Assert.Contains("ISSUED", rxStatuses);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    [Fact]
    public async Task Group1B_SinglePrescriptionMultipleLinesSameBatch_6Plus6GreaterThan10_ThrowsConflictAndPreservesStock()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var visit = await AddPaidVisitAsync(fx, "V1B-1");
            var rx = await AddPrescriptionAsync(fx, visit.Id, "RX1B-1", [(fx.MedicineId, 6m), (fx.MedicineId, 6m)]);
            var batchId = fx.BatchIds[0];

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IConfirmMedicineDispensingService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.ConfirmDispenseAsync(
                        fx.PharmacistUserId,
                        rx.Prescription.PrescriptionCode!,
                        new ConfirmDispenseRequest(
                            ReceiverName: "Nguyễn Văn Test",
                            IsIdentityVerified: true,
                            ChecksConfirmed: true,
                            IdempotencyKey: $"idem-1b-sum-{Guid.NewGuid():N}",
                            Items:
                            [
                                new ConfirmDispenseItemRequest(rx.ItemIds[0], batchId, 6m),
                                new ConfirmDispenseItemRequest(rx.ItemIds[1], batchId, 6m)
                            ])));
            }

            Db.ChangeTracker.Clear();
            var batch = await Db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == batchId);
            Assert.Equal(10m, batch.QuantityOnHand);
            Assert.Equal(0, await Db.StockTransactions.CountAsync(st => st.MedicineBatchId == batchId));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    [Fact]
    public async Task Group1C_NonEmptyShortageReasonWithQtyExceedingOnHand_ThrowsConflictAndNeverProducesNegativeStock()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var visit = await AddPaidVisitAsync(fx, "V1C-1");
            // Prescribed = 20, OnHand = 10, Requested = 12 (< Prescribed so ShortageReason applies, but 12 > OnHand!)
            var rx = await AddPrescriptionAsync(fx, visit.Id, "RX1C-1", [(fx.MedicineId, 20m)]);
            var batchId = fx.BatchIds[0];

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IConfirmMedicineDispensingService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.ConfirmDispenseAsync(
                        fx.PharmacistUserId,
                        rx.Prescription.PrescriptionCode!,
                        new ConfirmDispenseRequest(
                            ReceiverName: "Nguyễn Văn Test",
                            IsIdentityVerified: true,
                            ChecksConfirmed: true,
                            IdempotencyKey: $"idem-1c-{Guid.NewGuid():N}",
                            Items:
                            [
                                new ConfirmDispenseItemRequest(
                                    PrescriptionItemId: rx.ItemIds[0],
                                    MedicineBatchId: batchId,
                                    DispensedQuantity: 12m,
                                    ShortageReason: "Kho chỉ còn ít thuốc")
                            ])));
            }

            Db.ChangeTracker.Clear();
            var batch = await Db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == batchId);
            Assert.Equal(10m, batch.QuantityOnHand);
            Assert.Equal(0, await Db.StockTransactions.CountAsync(st => st.MedicineBatchId == batchId));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // GROUP 2: DispensedQuantity (null vs 0 vs >0), Multi-Batch FEFO & SCR-PHA-04
    // =========================================================================

    [Fact]
    public async Task Group2A_FefoAndPrepareStoreNullDispensedQuantity_MultiBatchPreserved_AndScrPha04Returns200()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [6m, 10m]);
        try
        {
            var visit = await AddPaidVisitAsync(fx, "V2A-1");
            var rx = await AddPrescriptionAsync(fx, visit.Id, "RX2A-1", [(fx.MedicineId, 10m)]);
            var batchA = fx.BatchIds[0]; // 6 on hand, earlier expiry
            var batchB = fx.BatchIds[1]; // 10 on hand, later expiry
            var pItemId = rx.ItemIds[0];

            // 1. FEFO selection across 2 batches (6 + 4 = 10)
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var fefoSvc = scope.ServiceProvider.GetRequiredService<IFefoBatchSelectionService>();
                await fefoSvc.SelectFefoBatchesAsync(
                    fx.PharmacistUserId,
                    rx.Prescription.PrescriptionCode!,
                    new SelectFefoBatchesRequest(
                    [
                        new SelectFefoBatchItemRequest(pItemId, MedicineBatchId: batchA, PickedQuantity: 6m),
                        new SelectFefoBatchItemRequest(pItemId, MedicineBatchId: batchB, PickedQuantity: 4m)
                    ]));
            }

            Db.ChangeTracker.Clear();
            var fefoItems = await Db.DispenseItems.AsNoTracking()
                .Where(di => di.PrescriptionItemId == pItemId)
                .OrderBy(di => di.Id)
                .ToListAsync();
            Assert.Equal(2, fefoItems.Count);
            Assert.All(fefoItems, di => Assert.Null(di.DispensedQuantity));
            Assert.Equal([6m, 4m], fefoItems.Select(di => di.PlannedQuantity!.Value).ToArray());

            // 2. PreparePrescriptionAsync preserves multi-batch split & keeps DispensedQuantity == null
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var prepSvc = scope.ServiceProvider.GetRequiredService<IPrepareMedicinesService>();
                await prepSvc.PreparePrescriptionAsync(
                    fx.PharmacistUserId,
                    rx.Prescription.PrescriptionCode!,
                    new PreparePrescriptionRequest(
                    [
                        new PrepareMedicineItemRequest(pItemId, MedicineBatchId: batchA, BatchCode: null, PickedQuantity: 6m),
                        new PrepareMedicineItemRequest(pItemId, MedicineBatchId: batchB, BatchCode: null, PickedQuantity: 4m)
                    ]));
            }

            Db.ChangeTracker.Clear();
            var preparedItems = await Db.DispenseItems.AsNoTracking()
                .Where(di => di.PrescriptionItemId == pItemId)
                .OrderBy(di => di.Id)
                .ToListAsync();
            Assert.Equal(2, preparedItems.Count);
            Assert.All(preparedItems, di => Assert.Null(di.DispensedQuantity));

            // 3. ConfirmDispense deducts 6 from BatchA + 4 from BatchB and returns canonical DispenseCode
            DispensingResultDetailDto confirmResult;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var confirmSvc = scope.ServiceProvider.GetRequiredService<IConfirmMedicineDispensingService>();
                confirmResult = await confirmSvc.ConfirmDispenseAsync(
                    fx.PharmacistUserId,
                    rx.Prescription.PrescriptionCode!,
                    new ConfirmDispenseRequest(
                        ReceiverName: "Nguyễn Văn Test",
                        Relationship: "Bản thân",
                        IsIdentityVerified: true,
                        ChecksConfirmed: true,
                        IdempotencyKey: $"idem-2a-{Guid.NewGuid():N}"));
            }

            Assert.Equal(2, confirmResult.Items.Count);
            Assert.Null(confirmResult.TotalAmount);
            Assert.Equal("Chưa có dữ liệu", confirmResult.TotalAmountFormatted);
            Assert.StartsWith("DISP-", confirmResult.DispenseCode, StringComparison.Ordinal);

            Db.ChangeTracker.Clear();
            Assert.Equal(0m, (await Db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == batchA)).QuantityOnHand);
            Assert.Equal(6m, (await Db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == batchB)).QuantityOnHand);
            Assert.Equal(2, await Db.StockTransactions.CountAsync(st => st.MedicineBatchId == batchA || st.MedicineBatchId == batchB));

            // 4. SCR-PHA-04 GET /api/v1/pharmacy/dispenses/{dispenseCode} returns 200 OK (canonical & legacy)
            var phaToken = GenerateJwt(fx.PharmacistUserId, "PHARMACY_STAFF");
            using var canonicalReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/pharmacy/dispenses/{Uri.EscapeDataString(confirmResult.DispenseCode)}");
            canonicalReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", phaToken);
            var canonicalResp = await Client.SendAsync(canonicalReq);
            Assert.Equal(HttpStatusCode.OK, canonicalResp.StatusCode);
            var receipt = await canonicalResp.Content.ReadFromJsonAsync<DispensingResultDetailDto>();
            Assert.NotNull(receipt);
            Assert.Equal(confirmResult.DispenseId, receipt!.DispenseId);
            Assert.Equal(2, receipt.Items.Count);

            var legacyCode = $"DISP-{HospitalClock.GetToday(TimeProvider.System).Year:D4}-{confirmResult.DispenseId:D6}";
            using var legacyReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/pharmacy/dispenses/{Uri.EscapeDataString(legacyCode)}");
            legacyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", phaToken);
            var legacyResp = await Client.SendAsync(legacyReq);
            Assert.Equal(HttpStatusCode.OK, legacyResp.StatusCode);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    [Fact]
    public async Task Group2B_ExplicitZeroDispensedQuantity_RequiresShortageReason_DeductsZeroStock_AndRejectsDraftOrExternalRx()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var visit = await AddPaidVisitAsync(fx, "V2B-1");
            var rx = await AddPrescriptionAsync(fx, visit.Id, "RX2B-1", [(fx.MedicineId, 10m)]);
            var batchId = fx.BatchIds[0];

            // 1. Explicit 0 without ShortageReason -> ValidationException
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var confirmSvc = scope.ServiceProvider.GetRequiredService<IConfirmMedicineDispensingService>();
                await Assert.ThrowsAsync<ValidationException>(() =>
                    confirmSvc.ConfirmDispenseAsync(
                        fx.PharmacistUserId,
                        rx.Prescription.PrescriptionCode!,
                        new ConfirmDispenseRequest(
                            ReceiverName: "Nguyễn Văn Test",
                            IsIdentityVerified: true,
                            ChecksConfirmed: true,
                            Items: [new ConfirmDispenseItemRequest(rx.ItemIds[0], batchId, 0m, ShortageReason: null)])));
            }

            // 2. Explicit 0 WITH ShortageReason -> 0 stock deducted (never falls back to PrescribedQuantity = 10!)
            DispensingResultDetailDto zeroResult;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var confirmSvc = scope.ServiceProvider.GetRequiredService<IConfirmMedicineDispensingService>();
                zeroResult = await confirmSvc.ConfirmDispenseAsync(
                    fx.PharmacistUserId,
                    rx.Prescription.PrescriptionCode!,
                    new ConfirmDispenseRequest(
                        ReceiverName: "Nguyễn Văn Test",
                        IsIdentityVerified: true,
                        ChecksConfirmed: true,
                        IdempotencyKey: $"idem-2b-{Guid.NewGuid():N}",
                        Items:
                        [
                            new ConfirmDispenseItemRequest(
                                rx.ItemIds[0],
                                batchId,
                                0m,
                                ShortageReason: "Hết hàm lượng tương đương, bệnh nhân đồng ý nhận đơn mua bổ sung")
                        ]));
            }

            Db.ChangeTracker.Clear();
            Assert.Equal(10m, (await Db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == batchId)).QuantityOnHand);
            Assert.Equal(0, await Db.StockTransactions.CountAsync(st => st.MedicineBatchId == batchId));
            var savedItem = await Db.DispenseItems.AsNoTracking().SingleAsync(di => di.PrescriptionItemId == rx.ItemIds[0]);
            Assert.Equal(0m, savedItem.DispensedQuantity);

            // SCR-PHA-04 receipt still loads 200 OK for 0-qty shortage item
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var resultSvc = scope.ServiceProvider.GetRequiredService<IDispensingResultService>();
                var detail = await resultSvc.GetDispensingResultAsync(fx.PharmacistUserId, zeroResult.DispenseCode);
                Assert.Equal(0m, Assert.Single(detail.Items).QtyDispensed);
            }

            // 3. DRAFT and EXTERNAL_PHARMACY prescriptions rejected (409 Conflict)
            var visitOther = await AddPaidVisitAsync(fx, "V2B-2");
            var draftRx = await AddPrescriptionAsync(fx, visitOther.Id, "RX2B-DRF", [(fx.MedicineId, 2m)], status: "DRAFT");
            var extRx = await AddPrescriptionAsync(fx, visitOther.Id, "RX2B-EXT", [(fx.MedicineId, 2m)], fulfillment: "EXTERNAL_PHARMACY");

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var fefoSvc = scope.ServiceProvider.GetRequiredService<IFefoBatchSelectionService>();
                var prepSvc = scope.ServiceProvider.GetRequiredService<IPrepareMedicinesService>();
                var confirmSvc = scope.ServiceProvider.GetRequiredService<IConfirmMedicineDispensingService>();

                await Assert.ThrowsAsync<ConflictException>(() => fefoSvc.GetFefoBatchesAsync(fx.PharmacistUserId, draftRx.Prescription.PrescriptionCode!));
                await Assert.ThrowsAsync<ConflictException>(() => prepSvc.GetPreparationContextAsync(fx.PharmacistUserId, extRx.Prescription.PrescriptionCode!));
                await Assert.ThrowsAsync<ConflictException>(() => confirmSvc.GetDispenseContextAsync(fx.PharmacistUserId, extRx.Prescription.PrescriptionCode!));
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // GROUP 3: Per-Charge VietQR (Option A), Cutover & UNPRICED Rejection
    // =========================================================================

    [Fact]
    public async Task Group3_PerChargeVietQr_MultiChargeRequiresExplicitId_HistoricalCutoverMaterializesExamCharge_AndUnpricedNeverUnlocks()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var now = HospitalClock.GetNow(TimeProvider.System);

            // A) Visit with 2 unpaid charges (EXAMINATION 200k + DIAGNOSTIC 150k)
            var visitMulti = new Visit
            {
                VisitCode = $"V3-M-{fx.Suffix}",
                PatientId = fx.PatientId,
                ExaminationRoomId = fx.RoomId,
                AssignedDoctorId = fx.DoctorId,
                Status = "IN_PROGRESS",
                CheckedInAt = now,
            };
            Db.Visits.Add(visitMulti);
            await Db.SaveChangesAsync();

            var examCharge = VisitCharge.CreateExamination(visitMulti.Id, fx.ExamServiceId, 200_000m, now);
            var diagOrder = new DiagnosticOrder
            {
                OrderCode = $"DIA3-{fx.Suffix}",
                VisitId = visitMulti.Id,
                OrderingDoctorId = fx.DoctorId,
                ServiceId = fx.DiagServiceId,
                Status = "ORDERED",
                Priority = "NORMAL",
                OrderedAt = now,
            };
            Db.DiagnosticOrders.Add(diagOrder);
            await Db.SaveChangesAsync();
            var diagCharge = VisitCharge.CreateDiagnostic(visitMulti.Id, diagOrder.Id, fx.DiagServiceId, 150_000m, now);
            Db.VisitCharges.AddRange(examCharge, diagCharge);
            await Db.SaveChangesAsync();

            var patientToken = GenerateJwt(fx.PatientUserId, "PATIENT");

            // 1. Omitting visitChargeId when >1 unpaid charges exist -> 409 Conflict
            using (var ambiguousReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/patients/visits/{visitMulti.VisitCode}/payments/qr"))
            {
                ambiguousReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
                var ambiguousResp = await Client.SendAsync(ambiguousReq);
                Assert.Equal(HttpStatusCode.Conflict, ambiguousResp.StatusCode);
            }

            // 2. Requesting QR for Exam charge explicitly -> 201 Created, CHARGE scope + VisitChargeId linked
            GenerateQrPaymentResponseDto examQr;
            using (var examQrReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/patients/visits/{visitMulti.VisitCode}/payments/qr"))
            {
                examQrReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
                examQrReq.Content = JsonContent.Create(new GenerateQrPaymentRequestDto(examCharge.Id));
                var examQrResp = await Client.SendAsync(examQrReq);
                Assert.Equal(HttpStatusCode.Created, examQrResp.StatusCode);
                examQr = (await examQrResp.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>())!;
            }

            Assert.Equal(examCharge.Id, examQr.VisitChargeId);
            Assert.Equal(200_000m, examQr.Amount);

            Db.ChangeTracker.Clear();
            var storedExamPayment = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == examQr.PaymentId);
            Assert.Equal(PaymentScopes.Charge, storedExamPayment.PaymentScope);
            Assert.Equal(examCharge.Id, storedExamPayment.VisitChargeId);

            // 3. Settle Exam QR via SePay webhook -> Exam unlocked, Diagnostic remains locked & visit settlementStatus == UNPAID!
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var webhook = scope.ServiceProvider.GetRequiredService<ISePayWebhookService>();
                var res = await webhook.HandleAsync(BuildWebhookRequest(
                    Random.Shared.NextInt64(1_000_000, 9_000_000),
                    examQr.PaymentCode,
                    200_000m,
                    $"FT-G3-{fx.Suffix}"));
                Assert.Equal(PaymentGatewayOutcomes.Paid, res.Outcome);
            }

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reader = scope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();
                var examGate = await reader.CheckExaminationEligibilityAsync(visitMulti.Id);
                var diagGate = await reader.CheckDiagnosticOrderEligibilityAsync(diagOrder.Id);
                Assert.True(examGate.IsUnlocked);
                Assert.False(diagGate.IsUnlocked);
                Assert.Equal(VisitChargeSettlementReader.StatusUnpaid, diagGate.StatusCode);
            }

            using (var chargesGet = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/patients/visits/{visitMulti.VisitCode}/charges"))
            {
                chargesGet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
                var chargesResp = await Client.SendAsync(chargesGet);
                Assert.Equal(HttpStatusCode.OK, chargesResp.StatusCode);
                var dto = (await chargesResp.Content.ReadFromJsonAsync<VisitChargesResponseDto>())!;
                Assert.Equal(VisitChargesService.SettlementUnpaid, dto.SettlementStatus);
                Assert.Equal(150_000m, dto.AmountDue);
                Assert.Equal(200_000m, dto.RecognizedPaidTotal);
            }

            // B) Active Historical Visit Cutover (0 VisitCharges, walk-in audit snapshot = 180k)
            var histVisit = new Visit
            {
                VisitCode = $"V3-H-{fx.Suffix}",
                PatientId = fx.PatientId,
                ExaminationRoomId = fx.RoomId,
                Status = "CHECKED_IN",
                CheckedInAt = now,
            };
            Db.Visits.Add(histVisit);
            await Db.SaveChangesAsync();
            Db.AuditLogs.Add(new AuditLog
            {
                HospitalId = fx.HospitalId,
                Action = "WALK_IN_VISIT_CREATED",
                EntityType = "VISIT",
                EntityId = histVisit.Id.ToString(),
                ChangeData = """{"servicePrice":180000,"paymentMethod":"VIETQR"}""",
                OccurredAt = DateTime.UtcNow,
            });
            await Db.SaveChangesAsync();

            using (var cutoverReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/patients/visits/{histVisit.VisitCode}/payments/qr"))
            {
                cutoverReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
                var cutoverResp = await Client.SendAsync(cutoverReq);
                Assert.Equal(HttpStatusCode.Created, cutoverResp.StatusCode);
                var cutoverDto = (await cutoverResp.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>())!;
                Assert.Equal(180_000m, cutoverDto.Amount);
                Assert.NotNull(cutoverDto.VisitChargeId);
            }

            Db.ChangeTracker.Clear();
            var materialized = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == histVisit.Id);
            Assert.Equal(VisitChargeTypes.Examination, materialized.ChargeType);
            Assert.Equal(180_000m, materialized.UnitPrice);

            // C) UNPRICED VisitCharge (UnitPrice == null) -> never free, 409 on QR, locked in reader
            var unpricedVisit = new Visit
            {
                VisitCode = $"V3-U-{fx.Suffix}",
                PatientId = fx.PatientId,
                ExaminationRoomId = fx.RoomId,
                Status = "CHECKED_IN",
                CheckedInAt = now,
            };
            Db.Visits.Add(unpricedVisit);
            await Db.SaveChangesAsync();
            Db.VisitCharges.Add(new VisitCharge
            {
                VisitId = unpricedVisit.Id,
                ServiceId = fx.ExamServiceId,
                ChargeType = VisitChargeTypes.Examination,
                UnitPrice = null,
                PaymentRequirement = "REQUIRED",
                Status = VisitChargeStatuses.Active,
                CreatedAt = now,
            });
            await Db.SaveChangesAsync();

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reader = scope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();
                var gate = await reader.CheckExaminationEligibilityAsync(unpricedVisit.Id);
                Assert.False(gate.IsUnlocked);
                Assert.Equal(VisitChargeSettlementReader.StatusUnpriced, gate.StatusCode);

                var qrSvc = scope.ServiceProvider.GetRequiredService<IGenerateQrPaymentService>();
                var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    qrSvc.CreateAsync(fx.PatientUserId, unpricedVisit.VisitCode, "127.0.0.1"));
                Assert.Equal(GenerateQrPaymentService.UndeterminedMessage, ex.Message);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // GROUP 4: Cancelled-Service Webhook Guard (CANCELLED_REFUND_PENDING)
    // =========================================================================

    [Fact]
    public async Task Group4_WebhookArrivingAfterDiagnosticCancelled_RecordsCancelledRefundPending_AndNeverUnlocksOrder()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var now = HospitalClock.GetNow(TimeProvider.System);
            var visit = await AddPaidVisitAsync(fx, "V4-1");

            var bloodTestOrder = new DiagnosticOrder
            {
                OrderCode = $"DIA4-{fx.Suffix}",
                VisitId = visit.Id,
                OrderingDoctorId = fx.DoctorId,
                ServiceId = fx.DiagServiceId,
                Status = "ORDERED",
                Priority = "NORMAL",
                OrderedAt = now,
            };
            Db.DiagnosticOrders.Add(bloodTestOrder);
            await Db.SaveChangesAsync();

            var bloodTestCharge = VisitCharge.CreateDiagnostic(visit.Id, bloodTestOrder.Id, fx.DiagServiceId, 150_000m, now);
            Db.VisitCharges.Add(bloodTestCharge);
            await Db.SaveChangesAsync();

            GenerateQrPaymentResponseDto qr;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var qrSvc = (GenerateQrPaymentService)scope.ServiceProvider.GetRequiredService<IGenerateQrPaymentService>();
                qr = await qrSvc.CreateAsync(
                    fx.PatientUserId,
                    visit.VisitCode,
                    "127.0.0.1",
                    new GenerateQrPaymentRequestDto(bloodTestCharge.Id));
            }

            // Doctor cancels Blood Test before transfer lands
            var trackedOrder = await Db.DiagnosticOrders.FirstAsync(o => o.Id == bloodTestOrder.Id);
            var trackedCharge = await Db.VisitCharges.FirstAsync(c => c.Id == bloodTestCharge.Id);
            trackedOrder.Status = "CANCELLED";
            trackedCharge.Status = VisitChargeStatuses.Cancelled;
            trackedCharge.CancelledAt = now;
            await Db.SaveChangesAsync();

            var extId = Random.Shared.NextInt64(10_000_000, 90_000_000);
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var webhook = scope.ServiceProvider.GetRequiredService<ISePayWebhookService>();
                var res = await webhook.HandleAsync(BuildWebhookRequest(extId, qr.PaymentCode, 150_000m, $"FT-G4-{fx.Suffix}"));

                Assert.Equal(SePayWebhookService.CancelledServiceRefundRequiredOutcome, res.Outcome);
            }

            Db.ChangeTracker.Clear();
            var reloadedCharge = await Db.VisitCharges.AsNoTracking().FirstAsync(c => c.Id == bloodTestCharge.Id);
            var reloadedPayment = await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == qr.PaymentId);
            var reloadedOrder = await Db.DiagnosticOrders.AsNoTracking().FirstAsync(o => o.Id == bloodTestOrder.Id);
            var gwEvent = await Db.PaymentGatewayEvents.AsNoTracking().FirstAsync(e => e.ExternalId == extId.ToString());
            var refundAudit = await Db.AuditLogs.AsNoTracking().SingleAsync(a =>
                a.EntityId == qr.PaymentId.ToString() &&
                a.Action == SePayWebhookService.CancelledServiceAuditAction);

            Assert.Equal(VisitChargeStatuses.CancelledRefundPending, reloadedCharge.Status);
            Assert.Equal(SePayWebhookService.MismatchPaymentStatus, reloadedPayment.Status);
            Assert.Equal("CANCELLED", reloadedOrder.Status);
            Assert.Equal(SePayWebhookService.CancelledServiceRefundRequiredOutcome, gwEvent.Outcome);
            Assert.Contains(SePayWebhookService.CancelledServiceReasonCode, refundAudit.ChangeData!, StringComparison.Ordinal);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reader = scope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();
                var gate = await reader.CheckDiagnosticOrderEligibilityAsync(bloodTestOrder.Id);
                Assert.False(gate.IsUnlocked);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // GROUP 5: Old-QR vs New-QR Race (Active Unpaid vs Already Paid)
    // =========================================================================

    [Fact]
    public async Task Group5A_OldExpiredQrPaidWhileNewPendingQrOpen_TransitionsOldToPaid_ExpiresNewPending_AndUnlocksCharge()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var now = HospitalClock.GetNow(TimeProvider.System);
            var visit = new Visit
            {
                VisitCode = $"V5A-{fx.Suffix}",
                PatientId = fx.PatientId,
                ExaminationRoomId = fx.RoomId,
                Status = "CHECKED_IN",
                CheckedInAt = now,
            };
            Db.Visits.Add(visit);
            await Db.SaveChangesAsync();

            var charge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 180_000m, now);
            Db.VisitCharges.Add(charge);
            await Db.SaveChangesAsync();

            var qr1 = Payment.CreateForCharge(visit.Id, charge.Id, 180_000m, now.AddMinutes(-20), PaymentStatuses.Expired);
            Db.Payments.Add(qr1);
            await Db.SaveChangesAsync();
            qr1.PaymentCode = $"MF{qr1.Id}";

            var qr2 = Payment.CreateForCharge(visit.Id, charge.Id, 180_000m, now.AddMinutes(-2), PaymentStatuses.Pending);
            Db.Payments.Add(qr2);
            await Db.SaveChangesAsync();
            qr2.PaymentCode = $"MF{qr2.Id}";
            await Db.SaveChangesAsync();

            var extId = Random.Shared.NextInt64(100_000_000, 900_000_000);
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var webhook = scope.ServiceProvider.GetRequiredService<ISePayWebhookService>();
                var res = await webhook.HandleAsync(BuildWebhookRequest(extId, qr1.PaymentCode!, 180_000m, $"FT-G5A-{fx.Suffix}"));
                Assert.Equal(PaymentGatewayOutcomes.Paid, res.Outcome);
            }

            Db.ChangeTracker.Clear();
            var reloadedQr1 = await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == qr1.Id);
            var reloadedQr2 = await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == qr2.Id);
            Assert.Equal(PaymentStatuses.Paid, reloadedQr1.Status);
            Assert.Equal(PaymentStatuses.Expired, reloadedQr2.Status);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reader = scope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();
                var gate = await reader.CheckExaminationEligibilityAsync(visit.Id);
                Assert.True(gate.IsUnlocked);
                Assert.Equal(VisitChargeSettlementReader.StatusPaid, gate.StatusCode);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    [Fact]
    public async Task Group5B_OldExpiredQrPaidAfterNewQrAlreadyPaid_RecordsDuplicateReconcile_AndPreservesSinglePaidRow()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var now = HospitalClock.GetNow(TimeProvider.System);
            var visit = new Visit
            {
                VisitCode = $"V5B-{fx.Suffix}",
                PatientId = fx.PatientId,
                ExaminationRoomId = fx.RoomId,
                Status = "CHECKED_IN",
                CheckedInAt = now,
            };
            Db.Visits.Add(visit);
            await Db.SaveChangesAsync();

            var charge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 180_000m, now);
            Db.VisitCharges.Add(charge);
            await Db.SaveChangesAsync();

            var qr1 = Payment.CreateForCharge(visit.Id, charge.Id, 180_000m, now.AddMinutes(-25), PaymentStatuses.Expired);
            Db.Payments.Add(qr1);
            await Db.SaveChangesAsync();
            qr1.PaymentCode = $"MF{qr1.Id}";

            var qr2 = Payment.CreateForCharge(visit.Id, charge.Id, 180_000m, now.AddMinutes(-5), PaymentStatuses.Paid);
            qr2.PaidAt = now.AddMinutes(-4);
            Db.Payments.Add(qr2);
            await Db.SaveChangesAsync();
            qr2.PaymentCode = $"MF{qr2.Id}";
            await Db.SaveChangesAsync();

            var extId = Random.Shared.NextInt64(100_000_000, 900_000_000);
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var webhook = scope.ServiceProvider.GetRequiredService<ISePayWebhookService>();
                var store = scope.ServiceProvider.GetRequiredService<IPaymentGatewayEventStore>();
                var res = await webhook.HandleAsync(BuildWebhookRequest(extId, qr1.PaymentCode!, 180_000m, $"FT-G5B-{fx.Suffix}"));

                Assert.Equal(SePayWebhookService.DuplicateChargeReconcileOutcome, res.Outcome);
                Assert.Equal(0m, await store.SumReceivedAmountAsync(qr1.PaymentCode!, CancellationToken.None));
            }

            Db.ChangeTracker.Clear();
            var reloadedQr1 = await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == qr1.Id);
            var reloadedQr2 = await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == qr2.Id);
            Assert.Equal(SePayWebhookService.MismatchPaymentStatus, reloadedQr1.Status);
            Assert.Equal(PaymentStatuses.Paid, reloadedQr2.Status);
            Assert.Equal(1, await Db.Payments.CountAsync(p => p.VisitChargeId == charge.Id && p.Status == PaymentStatuses.Paid));

            var dupAudit = await Db.AuditLogs.AsNoTracking().SingleAsync(a =>
                a.EntityId == qr1.Id.ToString() &&
                a.Action == SePayWebhookService.LateDuplicateAuditAction);
            Assert.Contains(SePayWebhookService.DuplicateChargeReasonCode, dupAudit.ChangeData!, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // GROUP 6: Concurrent SePayWebhookService vs Stale QR Expiry Barrier Test
    // =========================================================================

    [Fact]
    public async Task Group6_ConcurrentSePayWebhookVsGenerateQrExpiryOnStalePending_ZeroDeadlock_AndPreservesPaidUnlock()
    {
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var now = HospitalClock.GetNow(TimeProvider.System);
            var visit = new Visit
            {
                VisitCode = $"V6-{fx.Suffix}",
                PatientId = fx.PatientId,
                ExaminationRoomId = fx.RoomId,
                Status = "CHECKED_IN",
                CheckedInAt = now.AddMinutes(-30),
            };
            Db.Visits.Add(visit);
            await Db.SaveChangesAsync();

            var charge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 220_000m, now.AddMinutes(-30));
            Db.VisitCharges.Add(charge);
            await Db.SaveChangesAsync();

            // Stale (> 15m) PENDING QR1
            var qr1 = Payment.CreateForCharge(visit.Id, charge.Id, 220_000m, now.AddMinutes(-20), PaymentStatuses.Pending);
            Db.Payments.Add(qr1);
            await Db.SaveChangesAsync();
            qr1.PaymentCode = $"MF{qr1.Id}";
            await Db.SaveChangesAsync();

            using var ready = new SemaphoreSlim(0, 2);
            var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var extId = Random.Shared.NextInt64(100_000_000, 900_000_000);

            var webhookTask = Task.Run(async () =>
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var webhook = scope.ServiceProvider.GetRequiredService<ISePayWebhookService>();
                ready.Release();
                await releaseGate.Task;
                return await webhook.HandleAsync(BuildWebhookRequest(extId, qr1.PaymentCode!, 220_000m, $"FT-G6-{fx.Suffix}"));
            });

            var generateQrTask = Task.Run(async () =>
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var qrSvc = (GenerateQrPaymentService)scope.ServiceProvider.GetRequiredService<IGenerateQrPaymentService>();
                ready.Release();
                await releaseGate.Task;
                try
                {
                    await qrSvc.CreateAsync(
                        fx.PatientUserId,
                        visit.VisitCode,
                        "127.0.0.1",
                        new GenerateQrPaymentRequestDto(charge.Id));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            releaseGate.SetResult();

            var webhookRes = await webhookTask;
            var qrErr = await generateQrTask;

            // Webhook MUST succeed as PAID regardless of lock acquisition order!
            Assert.Equal(PaymentGatewayOutcomes.Paid, webhookRes.Outcome);

            if (qrErr is not null)
            {
                var conflict = Assert.IsType<ConflictException>(qrErr);
                Assert.Equal(GenerateQrPaymentService.NothingDueMessage, conflict.Message);
            }

            Db.ChangeTracker.Clear();
            var chargePayments = await Db.Payments.AsNoTracking()
                .Where(p => p.VisitChargeId == charge.Id)
                .ToListAsync();

            var paidRow = Assert.Single(chargePayments, p => p.Status == PaymentStatuses.Paid);
            Assert.Equal(qr1.Id, paidRow.Id);
            Assert.DoesNotContain(chargePayments, p => p.Status == PaymentStatuses.Pending);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reader = scope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();
                var gate = await reader.CheckExaminationEligibilityAsync(visit.Id);
                Assert.True(gate.IsUnlocked);
                Assert.Equal(VisitChargeSettlementReader.StatusPaid, gate.StatusCode);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // Isolated Fixture Helpers (Self-cleaning inside medflow_test_p03)
    // =========================================================================

    private static SePayWebhookRequest BuildWebhookRequest(
        long externalId,
        string code,
        decimal amount,
        string referenceCode)
    {
        return new SePayWebhookRequest
        {
            Id = externalId,
            Gateway = "MBBank",
            TransactionDate = "2026-11-25 14:30:00",
            AccountNumber = "0123456789",
            Code = code,
            Content = code,
            TransferType = "in",
            TransferAmount = amount,
            ReferenceCode = referenceCode,
        };
    }

    private string GenerateJwt(long userId, string role)
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwt.GenerateAccessToken(new User { Id = userId, Email = $"u{userId}@medflow.test" }, [role]).AccessToken;
    }

    private sealed record IsolatedFixture(
        string Suffix,
        long HospitalId,
        long DepartmentId,
        long RoomId,
        long ExamServiceId,
        long DiagServiceId,
        long PharmacistUserId,
        long PharmacistStaffId,
        long Pharmacist2UserId,
        long Pharmacist2StaffId,
        long DoctorUserId,
        long DoctorStaffId,
        long DoctorId,
        long PatientUserId,
        long PatientId,
        long MedicineId,
        IReadOnlyList<long> BatchIds,
        long PolicyId);

    private sealed record SeededRx(Prescription Prescription, IReadOnlyList<long> ItemIds);

    private async Task<IsolatedFixture> CreateIsolatedFixtureAsync(decimal[] batchQuantities)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var today = HospitalClock.GetToday(TimeProvider.System);

        var phaRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.PharmacyStaff);
        var docRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Doctor);
        var patRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Patient);

        var hospital = new Hospital
        {
            HospitalCode = $"P03{suffix}"[..10],
            Name = $"Bệnh viện Test Phase 03 {suffix}",
            IsActive = true,
        };
        Db.Hospitals.Add(hospital);
        await Db.SaveChangesAsync();

        var policy = new HospitalPolicy
        {
            HospitalId = hospital.Id,
            PolicyType = "PAYMENT",
            PolicyKey = "vietqr_account",
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN P03"}""",
            PolicyVersion = 1,
        };
        Db.HospitalPolicies.Add(policy);

        var dept = new Department
        {
            HospitalId = hospital.Id,
            DepartmentCode = $"DP{suffix}"[..10],
            Name = "Khoa Dược P03",
            IsActive = true,
        };
        Db.Departments.Add(dept);
        await Db.SaveChangesAsync();

        var examService = new Service
        {
            DepartmentId = dept.Id,
            ServiceCode = $"EX{suffix}"[..10],
            Name = "Khám Nội P03",
            ServiceType = "EXAMINATION",
            IsActive = true,
        };
        var diagService = new Service
        {
            DepartmentId = dept.Id,
            ServiceCode = $"DG{suffix}"[..10],
            Name = "Xét nghiệm máu P03",
            ServiceType = "DIAGNOSTIC",
            IsActive = true,
        };
        Db.Services.AddRange(examService, diagService);

        var room = new HospitalRoom
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            RoomCode = $"RM{suffix}"[..10],
            Name = "Phòng khám P03",
            RoomType = "EXAMINATION",
            Status = "ACTIVE",
        };
        Db.HospitalRooms.Add(room);

        var phaUser1 = new User { Email = $"pha1-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var phaUser2 = new User { Email = $"pha2-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var docUser = new User { Email = $"doc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var patUser = new User { Email = $"pat-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        Db.Users.AddRange(phaUser1, phaUser2, docUser, patUser);
        await Db.SaveChangesAsync();

        Db.UserRoles.AddRange(
            new UserRole { UserId = phaUser1.Id, RoleId = phaRole.Id },
            new UserRole { UserId = phaUser2.Id, RoleId = phaRole.Id },
            new UserRole { UserId = docUser.Id, RoleId = docRole.Id },
            new UserRole { UserId = patUser.Id, RoleId = patRole.Id });

        var phaStaff1 = new Staff
        {
            UserId = phaUser1.Id,
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            StaffCode = $"SP1{suffix}"[..10],
            FullName = "Dược sĩ Một",
            Status = "ACTIVE",
        };
        var phaStaff2 = new Staff
        {
            UserId = phaUser2.Id,
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            StaffCode = $"SP2{suffix}"[..10],
            FullName = "Dược sĩ Hai",
            Status = "ACTIVE",
        };
        var docStaff = new Staff
        {
            UserId = docUser.Id,
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            StaffCode = $"SDC{suffix}"[..10],
            FullName = "Bác sĩ Điều Trị",
            Status = "ACTIVE",
        };
        Db.Staff.AddRange(phaStaff1, phaStaff2, docStaff);
        await Db.SaveChangesAsync();

        var doctor = new Doctor
        {
            StaffId = docStaff.Id,
            HospitalId = hospital.Id,
            DoctorCode = $"DOC{suffix}"[..10],
            ProfessionalTitle = "BS.CKI",
        };
        Db.Doctors.Add(doctor);

        var patient = new Patient
        {
            UserId = patUser.Id,
            PatientCode = $"PAT{suffix}"[..11],
            FullName = "Nguyễn Văn Test",
            Gender = "MALE",
            Phone = "0909123456",
            IdentificationHash = $"p03-hash-{suffix}",
            Status = "ACTIVE",
        };
        Db.Patients.Add(patient);

        var medicine = new Medicine
        {
            HospitalId = hospital.Id,
            MedicineCode = $"MED{suffix}"[..11],
            Name = $"Thuốc P03 {suffix}",
            DosageForm = "Viên nén",
            DispensingUnit = "viên",
            IsActive = true,
        };
        Db.Medicines.Add(medicine);
        await Db.SaveChangesAsync();

        var batches = new List<MedicineBatch>(batchQuantities.Length);
        for (var i = 0; i < batchQuantities.Length; i++)
        {
            batches.Add(new MedicineBatch
            {
                MedicineId = medicine.Id,
                BatchNumber = $"LOT-{suffix}-{i + 1}",
                ExpiryDate = today.AddMonths(6 + (i * 6)),
                QuantityOnHand = batchQuantities[i],
                IsBlocked = false,
            });
        }
        Db.MedicineBatches.AddRange(batches);
        await Db.SaveChangesAsync();

        return new IsolatedFixture(
            suffix,
            hospital.Id,
            dept.Id,
            room.Id,
            examService.Id,
            diagService.Id,
            phaUser1.Id,
            phaStaff1.Id,
            phaUser2.Id,
            phaStaff2.Id,
            docUser.Id,
            docStaff.Id,
            doctor.Id,
            patUser.Id,
            patient.Id,
            medicine.Id,
            batches.Select(b => b.Id).ToList(),
            policy.Id);
    }

    private async Task<Visit> AddPaidVisitAsync(IsolatedFixture fx, string tag)
    {
        var now = HospitalClock.GetNow(TimeProvider.System);
        var visit = new Visit
        {
            VisitCode = $"{tag}-{fx.Suffix}",
            PatientId = fx.PatientId,
            AssignedDoctorId = fx.DoctorId,
            ExaminationRoomId = fx.RoomId,
            Status = "COMPLETED",
            CheckedInAt = now.AddHours(-1),
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        var examCharge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 150_000m, now.AddHours(-1));
        Db.VisitCharges.Add(examCharge);
        await Db.SaveChangesAsync();

        var paid = Payment.CreateForCharge(visit.Id, examCharge.Id, 150_000m, now.AddMinutes(-55), PaymentStatuses.Paid);
        paid.PaidAt = now.AddMinutes(-50);
        Db.Payments.Add(paid);
        await Db.SaveChangesAsync();
        paid.PaymentCode = $"MF{paid.Id}";
        await Db.SaveChangesAsync();

        return visit;
    }

    private async Task<SeededRx> AddPrescriptionAsync(
        IsolatedFixture fx,
        long visitId,
        string prefix,
        IReadOnlyList<(long MedicineId, decimal Qty)> lines,
        string status = "ISSUED",
        string fulfillment = "HOSPITAL")
    {
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);
        var rx = new Prescription
        {
            PrescriptionCode = $"{prefix}-{fx.Suffix}",
            VisitId = visitId,
            PrescribingDoctorId = fx.DoctorId,
            FulfillmentChoice = fulfillment,
            Status = status,
            IssuedAt = status == "DRAFT" ? null : nowLocal,
            CreatedAt = nowLocal,
        };
        Db.Prescriptions.Add(rx);
        await Db.SaveChangesAsync();

        var items = lines.Select(l => new PrescriptionItem
        {
            PrescriptionId = rx.Id,
            MedicineId = l.MedicineId,
            PrescribedQuantity = l.Qty,
            SingleDoseQuantity = 1m,
            SingleDoseUnit = "viên",
            Instructions = "Uống sau ăn",
        }).ToList();

        Db.PrescriptionItems.AddRange(items);
        await Db.SaveChangesAsync();

        return new SeededRx(rx, items.Select(i => i.Id).ToList());
    }

    private async Task CleanupIsolatedFixtureAsync(IsolatedFixture fx)
    {
        Db.ChangeTracker.Clear();

        var visitIds = await Db.Visits
            .Where(v => v.PatientId == fx.PatientId)
            .Select(v => v.Id)
            .ToListAsync();

        var rxIds = await Db.Prescriptions
            .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
            .Select(p => p.Id)
            .ToListAsync();

        var dispenseIds = await Db.MedicationDispenses
            .Where(md => md.PrescriptionId.HasValue && rxIds.Contains(md.PrescriptionId.Value))
            .Select(md => md.Id)
            .ToListAsync();

        var stockTxs = await Db.StockTransactions
            .Where(st => st.MedicineBatchId.HasValue && fx.BatchIds.Contains(st.MedicineBatchId.Value))
            .ToListAsync();
        Db.StockTransactions.RemoveRange(stockTxs);
        await Db.SaveChangesAsync();

        var dItems = await Db.DispenseItems
            .Where(di => di.DispenseId.HasValue && dispenseIds.Contains(di.DispenseId.Value))
            .ToListAsync();
        Db.DispenseItems.RemoveRange(dItems);
        await Db.SaveChangesAsync();

        var dispenses = await Db.MedicationDispenses
            .Where(md => dispenseIds.Contains(md.Id))
            .ToListAsync();
        Db.MedicationDispenses.RemoveRange(dispenses);
        await Db.SaveChangesAsync();

        var pItems = await Db.PrescriptionItems
            .Where(pi => pi.PrescriptionId.HasValue && rxIds.Contains(pi.PrescriptionId.Value))
            .ToListAsync();
        Db.PrescriptionItems.RemoveRange(pItems);
        await Db.SaveChangesAsync();

        var rxs = await Db.Prescriptions
            .Where(p => rxIds.Contains(p.Id))
            .ToListAsync();
        Db.Prescriptions.RemoveRange(rxs);
        await Db.SaveChangesAsync();

        var payments = await Db.Payments
            .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
            .ToListAsync();
        var paymentIds = payments.Select(p => p.Id).ToList();

        var gwEvents = await Db.PaymentGatewayEvents
            .Where(e => e.PaymentId.HasValue && paymentIds.Contains(e.PaymentId.Value))
            .ToListAsync();
        Db.PaymentGatewayEvents.RemoveRange(gwEvents);
        await Db.SaveChangesAsync();

        Db.Payments.RemoveRange(payments);
        await Db.SaveChangesAsync();

        var charges = await Db.VisitCharges
            .Where(c => c.VisitId.HasValue && visitIds.Contains(c.VisitId.Value))
            .ToListAsync();
        Db.VisitCharges.RemoveRange(charges);
        await Db.SaveChangesAsync();

        var diagOrders = await Db.DiagnosticOrders
            .Where(o => o.VisitId.HasValue && visitIds.Contains(o.VisitId.Value))
            .ToListAsync();
        Db.DiagnosticOrders.RemoveRange(diagOrders);
        await Db.SaveChangesAsync();

        var paymentIdStrings = paymentIds.Select(id => id.ToString()).ToList();
        var visitIdStrings = visitIds.Select(id => id.ToString()).ToList();
        var audits = await Db.AuditLogs
            .Where(a => a.HospitalId == fx.HospitalId ||
                        (a.EntityId != null && (paymentIdStrings.Contains(a.EntityId) || visitIdStrings.Contains(a.EntityId))))
            .ToListAsync();
        Db.AuditLogs.RemoveRange(audits);
        await Db.SaveChangesAsync();

        var visits = await Db.Visits.Where(v => visitIds.Contains(v.Id)).ToListAsync();
        Db.Visits.RemoveRange(visits);
        await Db.SaveChangesAsync();

        var batches = await Db.MedicineBatches.Where(b => fx.BatchIds.Contains(b.Id)).ToListAsync();
        Db.MedicineBatches.RemoveRange(batches);
        await Db.SaveChangesAsync();

        var med = await Db.Medicines.FirstOrDefaultAsync(m => m.Id == fx.MedicineId);
        if (med is not null) Db.Medicines.Remove(med);
        await Db.SaveChangesAsync();

        var patient = await Db.Patients.FirstOrDefaultAsync(p => p.Id == fx.PatientId);
        if (patient is not null) Db.Patients.Remove(patient);
        await Db.SaveChangesAsync();

        var doctor = await Db.Doctors.FirstOrDefaultAsync(d => d.Id == fx.DoctorId);
        if (doctor is not null) Db.Doctors.Remove(doctor);
        await Db.SaveChangesAsync();

        var staffIds = new[] { fx.PharmacistStaffId, fx.Pharmacist2StaffId, fx.DoctorStaffId };
        Db.Staff.RemoveRange(await Db.Staff.Where(s => staffIds.Contains(s.Id)).ToListAsync());
        await Db.SaveChangesAsync();

        var userIds = new[] { fx.PharmacistUserId, fx.Pharmacist2UserId, fx.DoctorUserId, fx.PatientUserId };
        Db.UserRoles.RemoveRange(await Db.UserRoles.Where(ur => userIds.Contains(ur.UserId)).ToListAsync());
        await Db.SaveChangesAsync();

        Db.Users.RemoveRange(await Db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync());
        await Db.SaveChangesAsync();

        var room = await Db.HospitalRooms.FirstOrDefaultAsync(r => r.Id == fx.RoomId);
        if (room is not null) Db.HospitalRooms.Remove(room);
        await Db.SaveChangesAsync();

        var serviceIds = new[] { fx.ExamServiceId, fx.DiagServiceId };
        Db.Services.RemoveRange(await Db.Services.Where(s => serviceIds.Contains(s.Id)).ToListAsync());
        await Db.SaveChangesAsync();

        var dept = await Db.Departments.FirstOrDefaultAsync(d => d.Id == fx.DepartmentId);
        if (dept is not null) Db.Departments.Remove(dept);
        await Db.SaveChangesAsync();

        var policy = await Db.HospitalPolicies.FirstOrDefaultAsync(p => p.Id == fx.PolicyId);
        if (policy is not null) Db.HospitalPolicies.Remove(policy);
        await Db.SaveChangesAsync();

        var hosp = await Db.Hospitals.FirstOrDefaultAsync(h => h.Id == fx.HospitalId);
        if (hosp is not null) Db.Hospitals.Remove(hosp);
        await Db.SaveChangesAsync();

        // Purge any orphaned P03* fixture rows from an earlier interrupted run in strict reverse-FK order
        await Db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM stock_transactions WHERE medicine_batch_id IN (SELECT id FROM medicine_batches WHERE medicine_id IN (SELECT id FROM medicines WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%')));
            DELETE FROM medicine_batches WHERE medicine_id IN (SELECT id FROM medicines WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%'));
            DELETE FROM medicines WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%');
            DELETE FROM doctors WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%');
            DELETE FROM user_roles WHERE user_id IN (SELECT user_id FROM staff WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%') AND user_id IS NOT NULL);
            DELETE FROM user_roles WHERE user_id IN (SELECT user_id FROM patients WHERE identification_hash LIKE 'p03-hash%' AND user_id IS NOT NULL);
            DELETE FROM staff WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%');
            DELETE FROM patients WHERE identification_hash LIKE 'p03-hash%' AND NOT EXISTS (SELECT 1 FROM visits WHERE visits.patient_id = patients.id);
            DELETE FROM user_roles WHERE user_id IN (SELECT id FROM users WHERE email LIKE '%@medflow.test' AND (email LIKE 'pha1-%' OR email LIKE 'pha2-%' OR email LIKE 'doc-%' OR email LIKE 'pat-%'));
            DELETE FROM users WHERE email LIKE '%@medflow.test' AND (email LIKE 'pha1-%' OR email LIKE 'pha2-%' OR email LIKE 'doc-%' OR email LIKE 'pat-%') AND NOT EXISTS (SELECT 1 FROM staff WHERE staff.user_id = users.id) AND NOT EXISTS (SELECT 1 FROM patients WHERE patients.user_id = users.id);
            DELETE FROM hospital_rooms WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%');
            DELETE FROM services WHERE department_id IN (SELECT id FROM departments WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%'));
            DELETE FROM departments WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%');
            DELETE FROM hospital_policies WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%');
            DELETE FROM audit_logs WHERE hospital_id IN (SELECT id FROM hospitals WHERE hospital_code LIKE 'P03%');
            DELETE FROM hospitals WHERE hospital_code LIKE 'P03%';
            """);
    }
}
