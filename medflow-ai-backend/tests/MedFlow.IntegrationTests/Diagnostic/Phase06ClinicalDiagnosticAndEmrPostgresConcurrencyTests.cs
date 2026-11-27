using System.Net;
using System.Net.Http.Headers;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Diagnostic;

/// <summary>
/// Phase 06 (GH-846 / ADR-0059) Mandatory Real-PostgreSQL Concurrency, Per-Order Payment Gate,
/// Specimen Safety, Two-Person Result Verification &amp; EMR Integrity Tests.
/// Runs against isolated PostgreSQL database (medflow_test_p06) using independent DI scopes / connections.
/// </summary>
public sealed class Phase06ClinicalDiagnosticAndEmrPostgresConcurrencyTests : AuthTestBase
{
    public Phase06ClinicalDiagnosticAndEmrPostgresConcurrencyTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    // =========================================================================
    // SCENARIO 1 (BUG-P6-01 & BUG-P6-02):
    // Per-order DIAGNOSTIC VisitCharge snapshot (fk_visit_charges_diagnostic_order_visit),
    // Exam & UC42 per-order payment gates, PERFORMED_AWAITING_RESULT granular consultation gates,
    // and independent parallel execution of Order #2 while Order #1 awaits UC43 lab results.
    // =========================================================================

    [Fact]
    public async Task Scenario1_PerOrderDiagnosticCharge_PaymentGates_PerformedAwaitingResultGranularGates_AndParallelOrderExecution()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await AddCheckedInVisitWithExamChargeAsync(fx, "S1", examPrice: 150_000m, payExamNow: false);

            // 1A. Unpaid EXAMINATION charge blocks OpenWorkspaceAsync with 409 ConflictException
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!));
            }

            // Settle EXAMINATION charge -> OpenWorkspaceAsync succeeds
            await PayExamChargeAsync(visit.Id, 150_000m);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var opened = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                Assert.Equal("IN_PROGRESS", opened.WorkspaceState);

                // 1B. Unpriced service is strictly rejected with 409 (never free by default)
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CreateDiagnosticOrderAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CreateDiagnosticOrderRequest(
                            ServiceId: fx.UnpricedDiagServiceId,
                            Instruction: "Dịch vụ chưa cấu hình giá hiệu lực")));

                // 1C. Create 2 mandatory diagnostic orders (Lab 120,000 + Imaging 240,000)
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(
                                ServiceId: fx.LabVerifyServiceId,
                                Instruction: "Xét nghiệm máu bắt buộc",
                                Priority: "URGENT",
                                RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(
                                ServiceId: fx.ImagingDirectServiceId,
                                Instruction: "Siêu âm ổ bụng song song",
                                Priority: "NORMAL",
                                RequiredForConclusion: true),
                        ]));

                Assert.Equal(2, created.CreatedDiagnosticOrders!.Count);
            }

            Db.ChangeTracker.Clear();
            var orders = await Db.DiagnosticOrders.AsNoTracking()
                .Where(o => o.VisitId == visit.Id)
                .OrderBy(o => o.Id)
                .ToListAsync();
            Assert.Equal(2, orders.Count);
            var order1 = orders[0];
            var order2 = orders[1];

            var diagCharges = await Db.VisitCharges.AsNoTracking()
                .Where(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Diagnostic)
                .OrderBy(c => c.DiagnosticOrderId)
                .ToListAsync();
            Assert.Equal(2, diagCharges.Count);
            Assert.Equal(order1.Id, diagCharges[0].DiagnosticOrderId);
            Assert.Equal(120_000m, diagCharges[0].UnitPrice);
            Assert.Equal("REQUIRED", diagCharges[0].PaymentRequirement);
            Assert.Equal(VisitChargeStatuses.Active, diagCharges[0].Status);
            Assert.Equal(order2.Id, diagCharges[1].DiagnosticOrderId);
            Assert.Equal(240_000m, diagCharges[1].UnitPrice);

            // 1D. Unpaid diagnostic orders are blocked at Confirm (UC39), CallNext (UC41), and StartExecution (UC42)
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var queueSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticQueueService>();

                await Assert.ThrowsAsync<ConflictException>(() =>
                    reviewSvc.ConfirmDiagnosticOrderAsync(
                        fx.DiagStaff1UserId,
                        order1.OrderCode!,
                        new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true)));

                // Even if only unpaid orders exist, CallNext will not call an unpaid order
                var callNextUnpaid = await queueSvc.CallNextDiagnosticPatientAsync(
                    fx.DiagStaff1UserId,
                    fx.LabServicePointId.ToString(),
                    new CallNextDiagnosticPatientRequest());
                Assert.Null(callNextUnpaid.CalledEntry);
            }

            // Stage E2 (QE-23 / PE-06): the clinical session must be handed off before CLS execution.
            await using (var handoffScope = Factory.Services.CreateAsyncScope())
            {
                var handoffConsultSvc = handoffScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await handoffConsultSvc.HandoffToDiagnosticAsync(fx.Doctor1UserId, visit.VisitCode!);
            }

            // 1E. Pay Order #1 ONLY -> Order #1 completes UC42 sample collection (PERFORMED_AWAITING_RESULT)
            await PayDiagnosticChargeAsync(visit.Id, diagCharges[0].Id, 120_000m);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var execSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();

                await reviewSvc.ConfirmDiagnosticOrderAsync(
                    fx.DiagStaff1UserId,
                    order1.OrderCode!,
                    new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true));

                await execSvc.StartServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    order1.OrderCode!,
                    new UpdateDiagnosticServiceStatusRequest(
                        TargetStatus: "IN_PROGRESS",
                        ExpectedVisitCode: visit.VisitCode,
                        ExpectedPatientId: fx.PatientId));

                var completedExec1 = await execSvc.CompleteServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    order1.OrderCode!,
                    new UpdateDiagnosticServiceStatusRequest(
                        TargetStatus: "COMPLETED",
                        ExpectedVisitCode: visit.VisitCode,
                        ExpectedPatientId: fx.PatientId));
                Assert.Equal("COMPLETED", completedExec1.CurrentStatus);
            }

            // 1F. Verify Order #1 maps strictly to PERFORMED_AWAITING_RESULT (never SIGNED/FINALIZED),
            // ALLOWS draft clinical notes + PROVISIONAL diagnosis + DRAFT prescription,
            // and BLOCKS (409) CONFIRMED diagnosis + ISSUED prescription + CompleteVisit!
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var ws = await consultSvc.GetWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                var wsOrder1 = Assert.Single(ws.ClinicalContext.DiagnosticOrders!, o => o.OrderId == order1.Id);
                Assert.Equal("PERFORMED_AWAITING_RESULT", wsOrder1.ResultStatus);
                Assert.False(wsOrder1.IsResultFinalized);

                // Stage E2 (QE-23 / PE-06): after handoff the visit is AWAITING_RESULTS and the clinical
                // session is closed — draft clinical writes are gated by the closed session. Order #2 is
                // still outstanding, so the visit is NOT yet return-ready.
                Db.ChangeTracker.Clear();
                var visitAfterHandoff = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
                Assert.Equal("AWAITING_RESULTS", visitAfterHandoff.Status);
                Assert.Null(visitAfterHandoff.ReadyForReturnAt);

                // BLOCK (409): CONFIRMED diagnosis, ISSUED prescription, CompleteVisit
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new RecordDiagnosisRequest(
                            PrimaryIcd10Code: fx.Icd10Code,
                            ClinicalConclusion: "Cố xác nhận chẩn đoán khi CLS chưa có kết quả",
                            IsConfirmed: true)));

                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CreatePrescriptionAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CreatePrescriptionRequest(
                            Status: "ISSUED",
                            IssueImmediately: true,
                            FulfillmentChoice: "HOSPITAL_PHARMACY",
                            Items:
                            [
                                new CreatePrescriptionItemRequest(
                                    MedicineId: fx.MedicineId,
                                    PrescribedQuantity: 10m,
                                    SingleDoseQuantity: 1m,
                                    SingleDoseUnit: "viên",
                                    Instructions: "Uống sau ăn"),
                            ])));

                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(fx.Doctor1UserId, visit.VisitCode!));
            }

            // 1G. Parallel Order #2 executes in UC42 immediately once Order #2's charge is PAID,
            // even while Order #1 remains in PERFORMED_AWAITING_RESULT!
            await PayDiagnosticChargeAsync(visit.Id, diagCharges[1].Id, 240_000m);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var execSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();

                await reviewSvc.ConfirmDiagnosticOrderAsync(
                    fx.DiagStaff1UserId,
                    order2.OrderCode!,
                    new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true));

                var started2 = await execSvc.StartServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    order2.OrderCode!,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"));
                Assert.Equal("IN_PROGRESS", started2.CurrentStatus);

                var completed2 = await execSvc.CompleteServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    order2.OrderCode!,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED"));
                Assert.Equal("COMPLETED", completed2.CurrentStatus);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // SCENARIO 2 (BUG-P6-02):
    // State-aware diagnostic cancellation:
    // (2A) Unpaid order with open PENDING QR -> QR EXPIRED, charge CANCELLED, late SePay transfer -> CANCELLED_REFUND_PENDING without unlocking.
    // (2B) Paid order -> charge CANCELLED_REFUND_PENDING, PAID payment preserved immutable, single RETURN_CONSULTATION QueueEntry created.
    // =========================================================================

    [Fact]
    public async Task Scenario2_CancelledDiagnosticOrder_ExpiresPendingQrAndHandlesLateWebhook_AndMarksPaidOrderRefundPendingWithSingleReturnConsultationQueue()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            // --- Part 2A: Unpaid order with PENDING QR + late SePay transfer ---
            var visitA = await AddCheckedInVisitWithExamChargeAsync(fx, "S2A", examPrice: 150_000m, payExamNow: true);
            long orderAId;
            string orderACode;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visitA.VisitCode!);
                var createdA = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visitA.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.ImagingDirectServiceId,
                        Instruction: "Siêu âm thử hủy QR",
                        RequiredForConclusion: false,
                        TransitionToAwaitingResults: true));
                orderAId = createdA.CreatedDiagnosticOrders![0].OrderId;
                orderACode = createdA.CreatedDiagnosticOrders[0].OrderCode;
            }

            Db.ChangeTracker.Clear();
            var chargeA = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderAId);
            var nowLocal = HospitalClock.GetNow(TimeProvider.System);
            var pendingQr = Payment.CreateForCharge(visitA.Id, chargeA.Id, 240_000m, nowLocal, PaymentStatuses.Pending);
            Db.Payments.Add(pendingQr);
            await Db.SaveChangesAsync();
            pendingQr.PaymentCode = $"MF{pendingQr.Id}";
            await Db.SaveChangesAsync();

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var cancelRes = await reviewSvc.CancelDiagnosticOrderAsync(
                    fx.DiagStaff1UserId,
                    orderACode,
                    new CancelDiagnosticOrderRequest(Reason: "Bệnh nhân xin hủy chỉ định trước khi đóng tiền"));

                Assert.Equal("CANCELLED", cancelRes.OrderStatus);
                Assert.Equal(VisitChargeStatuses.Cancelled, cancelRes.ChargeStatus);
                Assert.Equal(1, cancelRes.ExpiredPendingPaymentsCount);
            }

            Db.ChangeTracker.Clear();
            var expiredPayment = await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == pendingQr.Id);
            Assert.Equal("EXPIRED", expiredPayment.Status);

            // Late SePay transfer arrives for the expired QR of a cancelled order -> records refund-pending without unlocking!
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var webhookSvc = scope.ServiceProvider.GetRequiredService<ISePayWebhookService>();
                var reader = scope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();

                var extId = Random.Shared.NextInt64(100_000_000, 900_000_000);
                var webhookRes = await webhookSvc.HandleAsync(BuildWebhookRequest(extId, pendingQr.PaymentCode!, 240_000m, $"FT-S2A-{fx.Suffix}"));
                Assert.Equal(SePayWebhookService.CancelledServiceRefundRequiredOutcome, webhookRes.Outcome);

                var gateAfterLateTransfer = await reader.CheckDiagnosticOrderEligibilityAsync(orderAId);
                Assert.False(gateAfterLateTransfer.IsUnlocked);
            }

            Db.ChangeTracker.Clear();
            var chargeAAfterWebhook = await Db.VisitCharges.AsNoTracking().FirstAsync(c => c.Id == chargeA.Id);
            Assert.Equal(VisitChargeStatuses.CancelledRefundPending, chargeAAfterWebhook.Status);

            // --- Part 2B: Already-PAID order cancelled while Visit is AWAITING_RESULTS -> CANCELLED_REFUND_PENDING + 1 RETURN_CONSULTATION QueueEntry ---
            var visitB = await AddCheckedInVisitWithExamChargeAsync(fx, "S2B", examPrice: 150_000m, payExamNow: true);
            long orderBId;
            string orderBCode;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visitB.VisitCode!);
                var createdB = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visitB.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.ImagingDirectServiceId,
                        Instruction: "Siêu âm đã đóng tiền nhưng hủy trước thực hiện",
                        RequiredForConclusion: false,
                        TransitionToAwaitingResults: true));
                orderBId = createdB.CreatedDiagnosticOrders![0].OrderId;
                orderBCode = createdB.CreatedDiagnosticOrders[0].OrderCode;
                Assert.Equal("AWAITING_RESULTS", createdB.WorkspaceState);
            }

            Db.ChangeTracker.Clear();
            var chargeB = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderBId);
            var paidPaymentId = await PayDiagnosticChargeAsync(visitB.Id, chargeB.Id, 240_000m);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var cancelPaidRes = await reviewSvc.CancelDiagnosticOrderAsync(
                    fx.DiagStaff1UserId,
                    orderBCode,
                    new CancelDiagnosticOrderRequest(Reason: "Đổi phác đồ điều trị trước khi chụp"));

                Assert.Equal("CANCELLED_REFUND_PENDING", cancelPaidRes.ActionOutcome);
                Assert.Equal(VisitChargeStatuses.CancelledRefundPending, cancelPaidRes.ChargeStatus);
                // Stage E2 (QE-10): cancelling a paid order no longer auto-mints a RETURN_CONSULTATION ticket.
                Assert.False(cancelPaidRes.TransitionedToReturnConsultation);
            }

            Db.ChangeTracker.Clear();
            var reloadedChargeB = await Db.VisitCharges.AsNoTracking().FirstAsync(c => c.Id == chargeB.Id);
            Assert.Equal(VisitChargeStatuses.CancelledRefundPending, reloadedChargeB.Status);
            Assert.NotNull(reloadedChargeB.CancelledAt);

            var immutablePaid = await Db.Payments.AsNoTracking().FirstAsync(p => p.Id == paidPaymentId);
            Assert.Equal("PAID", immutablePaid.Status);
            Assert.Equal(240_000m, immutablePaid.Amount);

            var reloadedVisitB = await Db.Visits.AsNoTracking().FirstAsync(v => v.Id == visitB.Id);
            // Stage E2 (QE-10): the visit is queued-ready (ReadyForReturnAt) but held in AWAITING_RESULTS.
            Assert.Equal("AWAITING_RESULTS", reloadedVisitB.Status);
            Assert.NotNull(reloadedVisitB.ReadyForReturnAt);

            // QE-10: no auto RETURN_CONSULTATION ticket; Reception issues the single ticket later
            // (covered by StageC CdT07). Here assert the visit is ticket-less after the cancel.
            var openReturnQueues = await Db.QueueEntries.AsNoTracking()
                .Where(q => q.VisitId == visitB.Id && q.QueueType == "RETURN_CONSULTATION" && q.Status != "CANCELLED" && q.Status != "COMPLETED")
                .ToListAsync();
            Assert.Empty(openReturnQueues);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // SCENARIO 3 (BUG-P6-03):
    // Post-lock AssignedDoctorId ownership check blocks transferred-out Doctor A with 403 Forbidden.
    // =========================================================================

    [Fact]
    public async Task Scenario3_PostLockAssignedDoctorOwnership_TransferredOutDoctorABlocked403_DoctorBSucceeds()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await AddCheckedInVisitWithExamChargeAsync(fx, "S3", examPrice: 150_000m, payExamNow: true);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
            }

            // Transfer Visit to Doctor 2 in Room 2
            Db.ChangeTracker.Clear();
            var trackedVisit = await Db.Visits.FirstAsync(v => v.Id == visit.Id);
            trackedVisit.AssignedDoctorId = fx.Doctor2Id;
            trackedVisit.ExaminationRoomId = fx.ExamRoom2Id;
            var clinicalQe = await Db.QueueEntries.FirstAsync(q => q.VisitId == visit.Id && q.QueueType == "EXAMINATION");
            clinicalQe.ServicePointId = fx.ExamServicePoint2Id;
            clinicalQe.CurrentServicePointId = fx.ExamServicePoint2Id;
            await Db.SaveChangesAsync();

            // Transferred-out Doctor 1 must be blocked with 403 ForbiddenException on all consultation writes
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();

                await Assert.ThrowsAsync<ForbiddenException>(() =>
                    consultSvc.RecordClinicalExaminationAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new RecordClinicalExaminationRequest(ClinicalFindings: "Bác sĩ 1 cố ghi sau khi chuyển")));

                await Assert.ThrowsAsync<ForbiddenException>(() =>
                    consultSvc.CreateDiagnosticOrderAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CreateDiagnosticOrderRequest(ServiceId: fx.ImagingDirectServiceId, Instruction: "Chỉ định trái phép")));
            }

            // Newly assigned Doctor 2 succeeds cleanly
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var updated = await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor2UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(ClinicalFindings: "Bác sĩ 2 tiếp nhận và khám lâm sàng hợp lệ"));
                Assert.Equal("Bác sĩ 2 tiếp nhận và khám lâm sàng hợp lệ", updated.ClinicalContext.ClinicalFindings);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // SCENARIO 4 (BUG-P6-04 & BUG-P6-06):
    // UC42 cross-patient specimen guard, UC43 empty-conclusion DRAFT save,
    // RequiresResultVerification two-person independent verifier rule (self-verify 409),
    // internal-only ReportFileUrl validation, and two-layer PHI report-file endpoint.
    // =========================================================================

    [Fact]
    public async Task Scenario4_SpecimenGuard_EmptyConclusionDraft_TwoPersonIndependentVerifier_AndAuthenticatedReportFilePhiAuthz()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await AddCheckedInVisitWithExamChargeAsync(fx, "S4", examPrice: 150_000m, payExamNow: true);
            long orderId;
            string orderCode;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.LabVerifyServiceId,
                        Instruction: "Xét nghiệm bắt buộc thẩm định 2 người",
                        TransitionToAwaitingResults: true));
                orderId = created.CreatedDiagnosticOrders![0].OrderId;
                orderCode = created.CreatedDiagnosticOrders[0].OrderCode;
            }

            Db.ChangeTracker.Clear();
            var charge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderId);
            await PayDiagnosticChargeAsync(visit.Id, charge.Id, 120_000m);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var execSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                var resultSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();

                await reviewSvc.ConfirmDiagnosticOrderAsync(
                    fx.DiagStaff1UserId,
                    orderCode,
                    new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true));

                // 4A. Cross-patient specimen identity guard (BUG-P6-06): mismatched ExpectedPatientId throws 409
                await Assert.ThrowsAsync<ConflictException>(() =>
                    execSvc.StartServiceExecutionAsync(
                        fx.DiagStaff1UserId,
                        orderCode,
                        new UpdateDiagnosticServiceStatusRequest(
                            TargetStatus: "IN_PROGRESS",
                            ExpectedPatientId: fx.OtherPatientId)));

                await execSvc.StartServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    orderCode,
                    new UpdateDiagnosticServiceStatusRequest(
                        TargetStatus: "IN_PROGRESS",
                        ExpectedVisitCode: visit.VisitCode,
                        ExpectedPatientId: fx.PatientId));

                await execSvc.CompleteServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    orderCode,
                    new UpdateDiagnosticServiceStatusRequest(
                        TargetStatus: "COMPLETED",
                        ExpectedVisitCode: visit.VisitCode,
                        ExpectedPatientId: fx.PatientId));

                // 4B. External URL & traversal rejected in ReportFileUrl (BUG-P6-06)
                await Assert.ThrowsAsync<ValidationException>(() =>
                    resultSvc.EnterDiagnosticResultAsync(
                        fx.DiagStaff1UserId,
                        orderCode,
                        new EnterDiagnosticResultRequest(
                            ResultText: "WBC 9.1 G/L; HGB 138 g/L; PLT 245 G/L.",
                            Conclusion: "",
                            ReportFileUrl: "https://external.example.com/leak.pdf")));

                await Assert.ThrowsAsync<ValidationException>(() =>
                    resultSvc.EnterDiagnosticResultAsync(
                        fx.DiagStaff1UserId,
                        orderCode,
                        new EnterDiagnosticResultRequest(
                            ResultText: "WBC 9.1 G/L; HGB 138 g/L; PLT 245 G/L.",
                            Conclusion: "",
                            ReportFileUrl: "/storage/diagnostic-reports/../secret.pdf")));

                // 4C. Omitted Status + empty Conclusion saves DRAFT cleanly (BUG-P6-04 / BUG-P6-06)
                var draftRes = await resultSvc.EnterDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    orderCode,
                    new EnterDiagnosticResultRequest(
                        ResultText: "WBC 9.1 G/L; HGB 138 g/L; PLT 245 G/L.",
                        Conclusion: "",
                        ReportFileUrl: $"/storage/diagnostic-reports/{orderCode}.pdf"));
                Assert.Equal("DRAFT", draftRes.ResultStatus);
                Assert.False(draftRes.IsFinalized);

                // 4D. Direct FINALIZED on RequiresResultVerification=true throws 409; PENDING_VERIFY succeeds; self-verify by Staff 1 throws 409
                await Assert.ThrowsAsync<ConflictException>(() =>
                    resultSvc.EnterDiagnosticResultAsync(
                        fx.DiagStaff1UserId,
                        orderCode,
                        new EnterDiagnosticResultRequest(
                            ResultText: "WBC 9.1 G/L; HGB 138 g/L; PLT 245 G/L.",
                            Conclusion: "Công thức máu bình thường.",
                            Status: "FINALIZED",
                            IsFinalized: true,
                            CorrectionReason: "Thử khóa trực tiếp không qua thẩm định.")));

                var pendingVerify = await resultSvc.EnterDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    orderCode,
                    new EnterDiagnosticResultRequest(
                        ResultText: "WBC 9.1 G/L; HGB 138 g/L; PLT 245 G/L.",
                        Conclusion: "Công thức máu bình thường.",
                        ReportFileUrl: $"/storage/diagnostic-reports/{orderCode}.pdf",
                        Status: "PENDING_VERIFY",
                        IsFinalized: false,
                        CorrectionReason: "Chuyển chờ bác sĩ xét nghiệm thẩm định độc lập."));
                Assert.Equal("PENDING_VERIFY", pendingVerify.ResultStatus);

                await Assert.ThrowsAsync<ConflictException>(() =>
                    resultSvc.VerifyDiagnosticResultAsync(
                        fx.DiagStaff1UserId,
                        orderCode,
                        new VerifyDiagnosticResultRequest(
                            Conclusion: "Công thức máu bình thường.",
                            VerificationNote: "Tự duyệt kết quả của chính mình")));
            }

            // 4E. Before verification, report-file HTTP endpoint blocks owning PATIENT (403) and other patient (403),
            // while allowing same-hospital DOCTOR (200 application/pdf)
            var patientToken = GenerateJwt(fx.PatientUserId, RoleNames.Patient);
            var otherPatientToken = GenerateJwt(fx.OtherPatientUserId, RoleNames.Patient);
            var doctorToken = GenerateJwt(fx.Doctor1UserId, RoleNames.Doctor);

            using (var reqUnpublished = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/diagnostic/results/{orderCode}/report-file"))
            {
                reqUnpublished.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
                var respUnpublished = await Client.SendAsync(reqUnpublished);
                Assert.Equal(HttpStatusCode.Forbidden, respUnpublished.StatusCode);
            }

            using (var reqDoctor = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/diagnostic/results/{orderCode}/report-file"))
            {
                reqDoctor.Headers.Authorization = new AuthenticationHeaderValue("Bearer", doctorToken);
                var respDoctor = await Client.SendAsync(reqDoctor);
                Assert.Equal(HttpStatusCode.OK, respDoctor.StatusCode);
                Assert.Equal("application/pdf", respDoctor.Content.Headers.ContentType?.MediaType);
            }

            // 4F. Independent second verifier Staff 2 verifies -> VERIFIED + DIAGNOSTIC_RESULT_VERIFIED audit + Visit -> RETURN_CONSULTATION
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var resultSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                var verifiedRes = await resultSvc.VerifyDiagnosticResultAsync(
                    fx.DiagStaff2UserId,
                    orderCode,
                    new VerifyDiagnosticResultRequest(
                        Conclusion: "Công thức máu trong giới hạn bình thường.",
                        VerificationNote: "Đã thẩm định độc lập lần cuối."));

                Assert.Equal("VERIFIED", verifiedRes.ResultStatus);
                Assert.True(verifiedRes.IsFinalized);
                Assert.Equal(fx.DiagStaff1Id, verifiedRes.LatestVersion.EnteredByStaffId);
                Assert.Equal(fx.DiagStaff2Id, verifiedRes.LatestVersion.VerifiedByStaffId);
            }

            // Owning PATIENT now receives 200 application/pdf; foreign PATIENT still receives 403 Forbidden!
            using (var reqOwnPatient = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/diagnostic/results/{orderCode}/report-file"))
            {
                reqOwnPatient.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
                var respOwnPatient = await Client.SendAsync(reqOwnPatient);
                Assert.Equal(HttpStatusCode.OK, respOwnPatient.StatusCode);
                Assert.Equal("application/pdf", respOwnPatient.Content.Headers.ContentType?.MediaType);
            }

            using (var reqOtherPatient = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/diagnostic/results/{orderCode}/report-file"))
            {
                reqOtherPatient.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherPatientToken);
                var respOtherPatient = await Client.SendAsync(reqOtherPatient);
                Assert.Equal(HttpStatusCode.Forbidden, respOtherPatient.StatusCode);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // SCENARIO 5 (BUG-P6-05 & BUG-P6-07):
    // Dual-connection ConsultationWorkspace save/additional-order vs DiagnosticResult finalize
    // concurrency on PostgreSQL (ADR-0059 lock order 09 -> 10 -> 11 -> 12):
    // 0 deadlocks (40P01), 0 duplicate MedicalRecords / RevisionNos / RETURN_CONSULTATION rows,
    // dedicated EMR revision columns & diagnosis history preserved through PatientMedicalRecordService.
    // =========================================================================

    [Fact]
    public async Task Scenario5_DualConnectionConsultationSaveVsDiagnosticFinalize_ZeroDeadlocks_SequentialRevisions_AndDedicatedEmrColumnsIntact()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await AddCheckedInVisitWithExamChargeAsync(fx, "S5", examPrice: 150_000m, payExamNow: true);
            long order1Id;
            string order1Code;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);

                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Khám ban đầu: họng xung huyết nhẹ, phổi trong.",
                        ClinicalProgressionNote: "Bệnh nhân tỉnh táo, sinh hiệu ổn.",
                        TreatmentPlan: "Theo dõi ngoại trú kết hợp bù điện giải."));

                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordDiagnosisRequest(
                        PrimaryIcd10Code: fx.Icd10Code,
                        ClinicalConclusion: "Chẩn đoán ban đầu trước khi chỉ định CLS"));

                // Stage E2 (QE-23 / PE-06): the clinical session is handed off before CLS execution, after
                // which clinical writes are gated. Record the progression note now while still IN_PROGRESS.
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Khám ban đầu: họng xung huyết nhẹ, phổi trong.",
                        ClinicalProgressionNote: "Diễn biến tốt trong khi chờ kết quả siêu âm.",
                        TreatmentPlan: "Tiếp tục phác đồ ngoại trú 7 ngày."));

                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.ImagingDirectServiceId,
                        Instruction: "Siêu âm kiểm tra đồng thời"));
                order1Id = created.CreatedDiagnosticOrders![0].OrderId;
                order1Code = created.CreatedDiagnosticOrders[0].OrderCode;
            }

            Db.ChangeTracker.Clear();
            var charge1 = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == order1Id);
            await PayDiagnosticChargeAsync(visit.Id, charge1.Id, 240_000m);

            // Stage E2 (QE-23 / PE-06): hand off the clinical session before CLS execution.
            await using (var handoffScope = Factory.Services.CreateAsyncScope())
            {
                var handoffConsultSvc = handoffScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await handoffConsultSvc.HandoffToDiagnosticAsync(fx.Doctor1UserId, visit.VisitCode!);
            }

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var execSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();

                await reviewSvc.ConfirmDiagnosticOrderAsync(
                    fx.DiagStaff1UserId,
                    order1Code,
                    new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true));
                await execSvc.StartServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    order1Code,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"));
                await execSvc.CompleteServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    order1Code,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED"));
            }

            // Fire dual-connection Consultation workspace read vs Diagnostic result finalization simultaneously
            // (Stage E2: the clinical session is closed post-handoff, so the concurrent consultation operation is
            // a read; the test still verifies zero deadlock and intact sequential EMR revisions).
            using var ready = new SemaphoreSlim(0, 2);
            var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var doctorTask = Task.Run(async () =>
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                ready.Release();
                await releaseGate.Task;
                return await consultSvc.GetWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
            });

            var diagnosticTask = Task.Run(async () =>
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var resultSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                ready.Release();
                await releaseGate.Task;
                return await resultSvc.EnterDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    order1Code,
                    new EnterDiagnosticResultRequest(
                        ResultText: "Gan mật tụy lách kích thước bình thường, không dịch ổ bụng.",
                        Conclusion: "Siêu âm ổ bụng tổng quát bình thường.",
                        ReportFileUrl: $"/storage/diagnostic-reports/{order1Code}.pdf",
                        Status: "FINALIZED",
                        IsFinalized: true));
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            releaseGate.SetResult();

            var docOutcome = await doctorTask;
            var diagOutcome = await diagnosticTask;
            Assert.NotNull(docOutcome);
            Assert.True(diagOutcome.IsFinalized);

            // Stage E2 (QE-10): after finalize the visit sits in AWAITING_RESULTS. Reception coordinates the
            // return consultation, calls the patient, and the doctor reopens the workspace to review + conclude.
            await using (var enrollScope = Factory.Services.CreateAsyncScope())
            {
                var returnCoord = enrollScope.ServiceProvider.GetRequiredService<IReturnCoordinationService>();
                await returnCoord.EnrollReturnConsultationAsync(
                    fx.Reception1UserId,
                    visit.VisitCode!,
                    new CreateReturnEnrollmentRequestDto(
                        Purpose: QueueCoordinationPurposes.ResultConclusion,
                        IdempotencyKey: $"p06-s5-return-{visit.VisitCode}"));
            }
            await using (var callScope = Factory.Services.CreateAsyncScope())
            {
                var queueSvc = callScope.ServiceProvider.GetRequiredService<IExaminationQueueService>();
                await queueSvc.CallNextPatientAsync(
                    fx.Reception1UserId,
                    fx.ExamServicePoint1Id.ToString(),
                    new CallNextPatientRequest(Notes: "Gọi bệnh nhân khám kết luận"));
            }
            await using (var reopenScope = Factory.Services.CreateAsyncScope())
            {
                var reopenSvc = reopenScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await reopenSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
            }

            // Re-confirm diagnosis & complete visit with dedicated EMR instructions
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.ReviewDiagnosticResultsAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new ReviewDiagnosticResultsRequest(
                        ClinicalInterpretation: "Siêu âm ổ bụng bình thường, loại trừ tổn thương khu trú."));

                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordDiagnosisRequest(
                        PrimaryIcd10Code: fx.Icd10Code,
                        ClinicalConclusion: "Viêm dạ dày cấp mức độ nhẹ ổn định",
                        IsConfirmed: true));

                var completed = await consultSvc.CompleteVisitAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CompleteVisitRequest(
                        ConfirmNoPrescription: true,
                        DischargeInstructions: "Ăn chín uống sôi, chia nhỏ bữa ăn.",
                        FollowUpInstructions: "Tái khám sau 14 ngày.",
                        FollowUpDate: new DateOnly(2026, 12, 10),
                        WarningSigns: "Sốt cao liên tục hoặc đau bụng tăng dần."));
                Assert.Equal("COMPLETED", completed.Visit.Status);
            }

            // Assert PostgreSQL EMR invariants: single MedicalRecord, sequential RevisionNos, dedicated columns intact
            Db.ChangeTracker.Clear();
            var records = await Db.MedicalRecords.AsNoTracking()
                .Where(mr => mr.VisitId == visit.Id)
                .ToListAsync();
            var emr = Assert.Single(records);
            Assert.Equal("FINALIZED", emr.Status);

            var revisions = await Db.MedicalRecordRevisions.AsNoTracking()
                .Where(r => r.MedicalRecordId == emr.Id)
                .OrderBy(r => r.RevisionNo)
                .ToListAsync();
            Assert.Equal(Enumerable.Range(1, revisions.Count), revisions.Select(r => r.RevisionNo ?? 0));

            var latestRev = revisions[^1];
            Assert.Equal("Khám ban đầu: họng xung huyết nhẹ, phổi trong.", latestRev.ClinicalFindings);
            Assert.Equal("Diễn biến tốt trong khi chờ kết quả siêu âm.", latestRev.ClinicalProgressionNote);
            Assert.Equal("Siêu âm ổ bụng bình thường, loại trừ tổn thương khu trú.", latestRev.ClinicalInterpretation);
            Assert.Equal("Tiếp tục phác đồ ngoại trú 7 ngày.", latestRev.TreatmentPlan);
            Assert.Equal("Ăn chín uống sôi, chia nhỏ bữa ăn.", latestRev.DischargeInstructions);
            Assert.Equal("Tái khám sau 14 ngày.", latestRev.FollowUpInstructions);
            Assert.Equal(new DateOnly(2026, 12, 10), latestRev.FollowUpDate);
            Assert.Equal("Sốt cao liên tục hoặc đau bụng tăng dần.", latestRev.WarningSigns);

            // Verify PatientMedicalRecordService surfaces dedicated EMR columns to the owning patient
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var patientEmrSvc = scope.ServiceProvider.GetRequiredService<IPatientMedicalRecordService>();
                var detail = await patientEmrSvc.GetMyMedicalRecordAsync(fx.PatientUserId, emr.Id.ToString(), ipAddress: "127.0.0.1");
                Assert.Equal("Khám ban đầu: họng xung huyết nhẹ, phổi trong.", detail.ClinicalFindings);
                Assert.Equal("Diễn biến tốt trong khi chờ kết quả siêu âm.", detail.ClinicalProgressionNote);
                Assert.Equal("Siêu âm ổ bụng bình thường, loại trừ tổn thương khu trú.", detail.ClinicalInterpretation);
                Assert.Equal("Tiếp tục phác đồ ngoại trú 7 ngày.", detail.TreatmentPlan);
                Assert.Equal("Ăn chín uống sôi, chia nhỏ bữa ăn.", detail.DischargeInstructions);
                Assert.Equal("Tái khám sau 14 ngày.", detail.FollowUpInstructions);
                Assert.Equal("2026-12-10", detail.FollowUpDate);
                Assert.Equal("Sốt cao liên tục hoặc đau bụng tăng dần.", detail.WarningSigns);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // Isolated Self-Cleaning Fixture Helpers (medflow_test_p06)
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

    private sealed record IsolatedPhase06Fixture(
        string Suffix,
        long HospitalId,
        long DepartmentId,
        long SpecialtyId,
        long ExamRoom1Id,
        long ExamRoom2Id,
        long LabRoomId,
        long ImagingRoomId,
        long ExamServicePoint1Id,
        long ExamServicePoint2Id,
        long LabServicePointId,
        long ImagingServicePointId,
        long ExamServiceId,
        long LabVerifyServiceId,
        long ImagingDirectServiceId,
        long UnpricedDiagServiceId,
        long Doctor1UserId,
        long Doctor1StaffId,
        long Doctor1Id,
        long Doctor2UserId,
        long Doctor2StaffId,
        long Doctor2Id,
        long DiagStaff1UserId,
        long DiagStaff1Id,
        long DiagStaff2UserId,
        long DiagStaff2Id,
        long PatientUserId,
        long PatientId,
        long OtherPatientUserId,
        long OtherPatientId,
        long MedicineId,
        long PolicyId,
        string Icd10Code,
        long? CreatedIcd10Id,
        long Reception1UserId,
        long Reception1StaffId);

    private async Task<IsolatedPhase06Fixture> CreateIsolatedFixtureAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);

        var docRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Doctor);
        var diagRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.DiagnosticStaff);
        var patRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Patient);
        var recRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Reception);

        await EnsureRolePermissionsAsync(docRole.Id, ["MEDICAL_RECORD_WRITE", "DIAGNOSTIC_ORDER", "PRESCRIPTION_WRITE", "DIAGNOSTIC_RESULT_READ", "QUEUE_MANAGE"]);
        await EnsureRolePermissionsAsync(diagRole.Id, ["QUEUE_MANAGE", "DIAGNOSTIC_RESULT_READ", "DIAGNOSTIC_RESULT_WRITE"]);
        await EnsureRolePermissionsAsync(recRole.Id, ["QUEUE_MANAGE", "QUEUE_ENROLL", "VISIT_ASSIGN", "PATIENT_CHECKIN", "PAYMENT_VIEW", "PAYMENT_COLLECT"]);

        var existingIcd = await Db.Icd10Codes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsSelectable == true && c.NotPrimaryDx != true && c.NodeType == "LEAF");
        long? createdIcdId = null;
        string icdCode;
        if (existingIcd is not null && !string.IsNullOrWhiteSpace(existingIcd.Code))
        {
            icdCode = existingIcd.Code;
        }
        else
        {
            var customIcd = new Icd10Code
            {
                Code = $"K29.{suffix[..2]}",
                NameVi = "Viêm dạ dày cấp kiểm thử P06",
                CatalogVersion = "ICD10-VN",
                NodeType = "LEAF",
                IsSelectable = true,
                NotPrimaryDx = false,
            };
            Db.Icd10Codes.Add(customIcd);
            await Db.SaveChangesAsync();
            createdIcdId = customIcd.Id;
            icdCode = customIcd.Code;
        }

        var specialty = await Db.Specialties.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive != false);
        long specialtyId;
        if (specialty is not null)
        {
            specialtyId = specialty.Id;
        }
        else
        {
            var newSpec = new Specialty { SpecialtyCode = $"SP{suffix}", Name = $"Nội P06 {suffix}", IsActive = true };
            Db.Specialties.Add(newSpec);
            await Db.SaveChangesAsync();
            specialtyId = newSpec.Id;
        }

        var hospital = new Hospital
        {
            HospitalCode = $"P06{suffix}",
            Name = $"Bệnh viện Test Phase 06 {suffix}",
            IsActive = true,
        };
        Db.Hospitals.Add(hospital);
        await Db.SaveChangesAsync();

        var policy = new HospitalPolicy
        {
            HospitalId = hospital.Id,
            PolicyType = "PAYMENT",
            PolicyKey = "vietqr_account",
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN P06"}""",
            PolicyVersion = 1,
        };
        Db.HospitalPolicies.Add(policy);

        var dept = new Department
        {
            HospitalId = hospital.Id,
            DepartmentCode = $"DP{suffix}",
            Name = $"Khoa Lâm Sàng & CLS P06 {suffix}",
            IsActive = true,
        };
        Db.Departments.Add(dept);
        await Db.SaveChangesAsync();

        var examService = new Service
        {
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"EX{suffix}",
            Name = "Khám Nội Tổng Quát P06",
            ServiceType = "EXAMINATION",
            IsActive = true,
        };
        var labVerifyService = new Service
        {
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"XN{suffix}",
            Name = "Xét nghiệm Công thức máu P06",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = true,
            IsActive = true,
        };
        var imagingDirectService = new Service
        {
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"SA{suffix}",
            Name = "Siêu âm ổ bụng tổng quát P06",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var unpricedDiagService = new Service
        {
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"UP{suffix}",
            Name = "Xét nghiệm chưa cấu hình giá P06",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        Db.Services.AddRange(examService, labVerifyService, imagingDirectService, unpricedDiagService);
        await Db.SaveChangesAsync();

        Db.ServicePrices.AddRange(
            new ServicePrice
            {
                ServiceId = examService.Id,
                PriceAmount = 150_000m,
                EffectiveFrom = nowLocal.AddDays(-30),
                EffectiveTo = null,
            },
            new ServicePrice
            {
                ServiceId = labVerifyService.Id,
                PriceAmount = 120_000m,
                EffectiveFrom = nowLocal.AddDays(-30),
                EffectiveTo = null,
            },
            new ServicePrice
            {
                ServiceId = imagingDirectService.Id,
                PriceAmount = 240_000m,
                EffectiveFrom = nowLocal.AddDays(-30),
                EffectiveTo = null,
            });

        var examRoom1 = new HospitalRoom
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            RoomCode = $"E1{suffix}",
            Name = "Phòng khám 101 P06",
            RoomType = "CONSULTATION",
            Capacity = 1,
            Status = "ACTIVE",
        };
        var examRoom2 = new HospitalRoom
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            RoomCode = $"E2{suffix}",
            Name = "Phòng khám 102 P06",
            RoomType = "CONSULTATION",
            Capacity = 1,
            Status = "ACTIVE",
        };
        var labRoom = new HospitalRoom
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            RoomCode = $"LB{suffix}",
            Name = "Phòng xét nghiệm 201 P06",
            RoomType = "DIAGNOSTIC",
            Capacity = 2,
            Status = "ACTIVE",
        };
        var imagingRoom = new HospitalRoom
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            RoomCode = $"IM{suffix}",
            Name = "Phòng siêu âm 202 P06",
            RoomType = "DIAGNOSTIC",
            Capacity = 2,
            Status = "ACTIVE",
        };
        Db.HospitalRooms.AddRange(examRoom1, examRoom2, labRoom, imagingRoom);
        await Db.SaveChangesAsync();

        var doc1User = new User { Email = $"d1-p06-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var doc2User = new User { Email = $"d2-p06-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var diag1User = new User { Email = $"dg1-p06-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var diag2User = new User { Email = $"dg2-p06-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var patUser = new User { Email = $"pt1-p06-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var otherPatUser = new User { Email = $"pt2-p06-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var rec1User = new User { Email = $"rc1-p06-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        Db.Users.AddRange(doc1User, doc2User, diag1User, diag2User, patUser, otherPatUser, rec1User);
        await Db.SaveChangesAsync();

        Db.UserRoles.AddRange(
            new UserRole { UserId = doc1User.Id, RoleId = docRole.Id },
            new UserRole { UserId = doc2User.Id, RoleId = docRole.Id },
            new UserRole { UserId = diag1User.Id, RoleId = diagRole.Id },
            new UserRole { UserId = diag2User.Id, RoleId = diagRole.Id },
            new UserRole { UserId = patUser.Id, RoleId = patRole.Id },
            new UserRole { UserId = otherPatUser.Id, RoleId = patRole.Id },
            new UserRole { UserId = rec1User.Id, RoleId = recRole.Id });

        var doc1Staff = new Staff
        {
            UserId = doc1User.Id,
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            StaffCode = $"SD1{suffix}",
            FullName = "BS. Nguyễn Văn Một",
            Status = "ACTIVE",
        };
        var doc2Staff = new Staff
        {
            UserId = doc2User.Id,
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            StaffCode = $"SD2{suffix}",
            FullName = "BS. Trần Văn Hai",
            Status = "ACTIVE",
        };
        var diag1Staff = new Staff
        {
            UserId = diag1User.Id,
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            StaffCode = $"SG1{suffix}",
            FullName = "KTV. Lê Thị Một",
            Status = "ACTIVE",
        };
        var diag2Staff = new Staff
        {
            UserId = diag2User.Id,
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            StaffCode = $"SG2{suffix}",
            FullName = "BS.CKII. Phạm Thẩm Định",
            Status = "ACTIVE",
        };
        var rec1Staff = new Staff
        {
            UserId = rec1User.Id,
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            StaffCode = $"SR1{suffix}",
            FullName = "NV. Tiếp Đón P06",
            Status = "ACTIVE",
        };
        Db.Staff.AddRange(doc1Staff, doc2Staff, diag1Staff, diag2Staff, rec1Staff);
        await Db.SaveChangesAsync();

        var doctor1 = new Doctor
        {
            StaffId = doc1Staff.Id,
            HospitalId = hospital.Id,
            SpecialtyId = specialtyId,
            DoctorCode = $"DC1{suffix}",
            ProfessionalTitle = "BS.CKI",
            IsActive = true,
        };
        var doctor2 = new Doctor
        {
            StaffId = doc2Staff.Id,
            HospitalId = hospital.Id,
            SpecialtyId = specialtyId,
            DoctorCode = $"DC2{suffix}",
            ProfessionalTitle = "BS.CKI",
            IsActive = true,
        };
        Db.Doctors.AddRange(doctor1, doctor2);
        await Db.SaveChangesAsync();

        Db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = doctor1.Id, ServiceId = examService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doctor1.Id, ServiceId = labVerifyService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doctor1.Id, ServiceId = imagingDirectService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doctor1.Id, ServiceId = unpricedDiagService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doctor2.Id, ServiceId = examService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doctor2.Id, ServiceId = labVerifyService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doctor2.Id, ServiceId = imagingDirectService.Id, IsActive = true });

        var examSp1 = new QueueServicePoint
        {
            HospitalId = hospital.Id,
            RoomId = examRoom1.Id,
            DoctorId = doctor1.Id,
            ServiceId = examService.Id,
            PointType = "EXAMINATION_ROOM",
            Name = "Điểm khám 101 P06",
            IsActive = true,
        };
        var examSp2 = new QueueServicePoint
        {
            HospitalId = hospital.Id,
            RoomId = examRoom2.Id,
            DoctorId = doctor2.Id,
            ServiceId = examService.Id,
            PointType = "EXAMINATION_ROOM",
            Name = "Điểm khám 102 P06",
            IsActive = true,
        };
        var labSp = new QueueServicePoint
        {
            HospitalId = hospital.Id,
            RoomId = labRoom.Id,
            ServiceId = labVerifyService.Id,
            PointType = "DIAGNOSTIC",
            Name = "Điểm Xét nghiệm 201 P06",
            IsActive = true,
        };
        var imagingSp = new QueueServicePoint
        {
            HospitalId = hospital.Id,
            RoomId = imagingRoom.Id,
            ServiceId = imagingDirectService.Id,
            PointType = "DIAGNOSTIC",
            Name = "Điểm Siêu âm 202 P06",
            IsActive = true,
        };
        Db.QueueServicePoints.AddRange(examSp1, examSp2, labSp, imagingSp);

        var patient = new Patient
        {
            UserId = patUser.Id,
            PatientCode = $"PT1{suffix}",
            FullName = "Nguyễn Văn Bệnh Nhân",
            Gender = "MALE",
            Phone = "0909111222",
            IdentificationHash = $"p06-hash-1-{suffix}",
            Status = "ACTIVE",
        };
        var otherPatient = new Patient
        {
            UserId = otherPatUser.Id,
            PatientCode = $"PT2{suffix}",
            FullName = "Trần Thị Hồ Sơ Khác",
            Gender = "FEMALE",
            Phone = "0909333444",
            IdentificationHash = $"p06-hash-2-{suffix}",
            Status = "ACTIVE",
        };
        Db.Patients.AddRange(patient, otherPatient);

        var medicine = new Medicine
        {
            HospitalId = hospital.Id,
            MedicineCode = $"MD{suffix}",
            Name = $"Omeprazol P06 {suffix}",
            DosageForm = "Viên nang",
            DispensingUnit = "viên",
            IsActive = true,
        };
        Db.Medicines.Add(medicine);
        await Db.SaveChangesAsync();

        return new IsolatedPhase06Fixture(
            Suffix: suffix,
            HospitalId: hospital.Id,
            DepartmentId: dept.Id,
            SpecialtyId: specialtyId,
            ExamRoom1Id: examRoom1.Id,
            ExamRoom2Id: examRoom2.Id,
            LabRoomId: labRoom.Id,
            ImagingRoomId: imagingRoom.Id,
            ExamServicePoint1Id: examSp1.Id,
            ExamServicePoint2Id: examSp2.Id,
            LabServicePointId: labSp.Id,
            ImagingServicePointId: imagingSp.Id,
            ExamServiceId: examService.Id,
            LabVerifyServiceId: labVerifyService.Id,
            ImagingDirectServiceId: imagingDirectService.Id,
            UnpricedDiagServiceId: unpricedDiagService.Id,
            Doctor1UserId: doc1User.Id,
            Doctor1StaffId: doc1Staff.Id,
            Doctor1Id: doctor1.Id,
            Doctor2UserId: doc2User.Id,
            Doctor2StaffId: doc2Staff.Id,
            Doctor2Id: doctor2.Id,
            DiagStaff1UserId: diag1User.Id,
            DiagStaff1Id: diag1Staff.Id,
            DiagStaff2UserId: diag2User.Id,
            DiagStaff2Id: diag2Staff.Id,
            PatientUserId: patUser.Id,
            PatientId: patient.Id,
            OtherPatientUserId: otherPatUser.Id,
            OtherPatientId: otherPatient.Id,
            MedicineId: medicine.Id,
            PolicyId: policy.Id,
            Icd10Code: icdCode,
            CreatedIcd10Id: createdIcdId,
            Reception1UserId: rec1User.Id,
            Reception1StaffId: rec1Staff.Id);
    }

    private async Task EnsureRolePermissionsAsync(long roleId, IReadOnlyList<string> permissionCodes)
    {
        foreach (var code in permissionCodes)
        {
            var perm = await Db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
            if (perm is null)
            {
                perm = new Permission { Code = code };
                Db.Permissions.Add(perm);
                await Db.SaveChangesAsync();
            }

            var hasMapping = await Db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == perm.Id);
            if (!hasMapping)
            {
                Db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = perm.Id });
                await Db.SaveChangesAsync();
            }
        }
    }

    private async Task<Visit> AddCheckedInVisitWithExamChargeAsync(
        IsolatedPhase06Fixture fx,
        string tag,
        decimal examPrice,
        bool payExamNow)
    {
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"VIS-{tag}-{fx.Suffix}",
            PatientId = fx.PatientId,
            AssignedDoctorId = fx.Doctor1Id,
            ExaminationRoomId = fx.ExamRoom1Id,
            Status = "CHECKED_IN",
            CheckedInAt = nowLocal.AddMinutes(-20),
            ReasonForVisit = "Đau vùng thượng vị kèm mệt mỏi",
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        var nextQueueNumber = (await Db.QueueEntries
            .Where(q => q.ServicePointId == fx.ExamServicePoint1Id && q.QueueDate == today)
            .MaxAsync(q => (int?)q.QueueNumber) ?? 0) + 1;

        var clinicalQueue = new QueueEntry
        {
            VisitId = visit.Id,
            ServicePointId = fx.ExamServicePoint1Id,
            CurrentServicePointId = fx.ExamServicePoint1Id,
            QueueDate = today,
            QueueNumber = nextQueueNumber,
            QueueType = "EXAMINATION",
            Status = "CALLED",
            PresenceStatus = "PRESENT",
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            ArrivedAt = nowLocal.AddMinutes(-19),
            ReadyAt = nowLocal.AddMinutes(-18),
            CalledAt = nowLocal.AddMinutes(-5),
            StateVersion = 2,
        };
        Db.QueueEntries.Add(clinicalQueue);

        var examCharge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, examPrice, nowLocal.AddMinutes(-19));
        Db.VisitCharges.Add(examCharge);
        await Db.SaveChangesAsync();

        if (payExamNow && examPrice > 0m)
        {
            await PayExamChargeAsync(visit.Id, examPrice);
        }

        return visit;
    }

    private async Task PayExamChargeAsync(long visitId, decimal amount)
    {
        Db.ChangeTracker.Clear();
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);
        var examCharge = await Db.VisitCharges.AsNoTracking()
            .FirstAsync(c => c.VisitId == visitId && c.ChargeType == VisitChargeTypes.Examination);
        var paid = Payment.CreateForCharge(visitId, examCharge.Id, amount, nowLocal, PaymentStatuses.Paid);
        paid.PaidAt = nowLocal;
        Db.Payments.Add(paid);
        await Db.SaveChangesAsync();
        paid.PaymentCode = $"MF{paid.Id}";
        await Db.SaveChangesAsync();
    }

    private async Task<long> PayDiagnosticChargeAsync(long visitId, long diagChargeId, decimal amount)
    {
        Db.ChangeTracker.Clear();
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);
        var paid = Payment.CreateForCharge(visitId, diagChargeId, amount, nowLocal, PaymentStatuses.Paid);
        paid.PaidAt = nowLocal;
        Db.Payments.Add(paid);
        await Db.SaveChangesAsync();
        paid.PaymentCode = $"MF{paid.Id}";
        await Db.SaveChangesAsync();
        return paid.Id;
    }

    private async Task CleanupIsolatedFixtureAsync(IsolatedPhase06Fixture fx)
    {
        Db.ChangeTracker.Clear();

        var patientIds = new[] { fx.PatientId, fx.OtherPatientId };
        var visitIds = await Db.Visits
            .Where(v => v.PatientId.HasValue && patientIds.Contains(v.PatientId.Value))
            .Select(v => v.Id)
            .ToListAsync();

        // fk_notifications_visit_id: notifications (e.g. VISIT_RETURN_READY) reference visits — clear them
        // before visits are deleted below.
        await Db.Notifications
            .Where(n => (n.VisitId.HasValue && visitIds.Contains(n.VisitId.Value)) ||
                        n.PatientId == fx.PatientId ||
                        n.PatientId == fx.OtherPatientId)
            .ExecuteDeleteAsync();

        // Stage E2 (migration 027): fk_queue_entries_diagnostic_order_visit + fk_queue_entries_prescription_visit
        // are ON DELETE RESTRICT. Clear diagnostic_orders.queue_entry_id + queue_fairness_states
        // .last_started_queue_entry_id back-pointers before deleting queue_entries.
        var spIds = new[] { fx.ExamServicePoint1Id, fx.ExamServicePoint2Id, fx.LabServicePointId, fx.ImagingServicePointId };
        await Db.DiagnosticOrders
            .Where(o => o.QueueEntryId.HasValue && o.VisitId.HasValue && visitIds.Contains(o.VisitId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.QueueEntryId, (long?)null));
        Db.QueueFairnessStates.RemoveRange(
            await Db.QueueFairnessStates.Where(f => spIds.Contains(f.ServicePointId)).ToListAsync());
        await Db.SaveChangesAsync();
        Db.QueueEntries.RemoveRange(
            await Db.QueueEntries.Where(q => q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)).ToListAsync());
        await Db.SaveChangesAsync();
        var orderIds = await Db.DiagnosticOrders
            .Where(o => o.VisitId.HasValue && visitIds.Contains(o.VisitId.Value))
            .Select(o => o.Id)
            .ToListAsync();

        var resultIds = await Db.DiagnosticResults
            .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
            .Select(r => r.Id)
            .ToListAsync();

        if (resultIds.Count > 0)
        {
            Db.DiagnosticResultVersions.RemoveRange(
                await Db.DiagnosticResultVersions.Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value)).ToListAsync());
            await Db.SaveChangesAsync();

            Db.DiagnosticResults.RemoveRange(
                await Db.DiagnosticResults.Where(r => resultIds.Contains(r.Id)).ToListAsync());
            await Db.SaveChangesAsync();
        }

        var rxIds = await Db.Prescriptions
            .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
            .Select(p => p.Id)
            .ToListAsync();
        if (rxIds.Count > 0)
        {
            Db.PrescriptionItems.RemoveRange(
                await Db.PrescriptionItems.Where(pi => pi.PrescriptionId.HasValue && rxIds.Contains(pi.PrescriptionId.Value)).ToListAsync());
            await Db.SaveChangesAsync();

            Db.Prescriptions.RemoveRange(
                await Db.Prescriptions.Where(p => rxIds.Contains(p.Id)).ToListAsync());
            await Db.SaveChangesAsync();
        }

        var emrIds = await Db.MedicalRecords
            .Where(mr => mr.VisitId.HasValue && visitIds.Contains(mr.VisitId.Value))
            .Select(mr => mr.Id)
            .ToListAsync();
        if (emrIds.Count > 0)
        {
            Db.MedicalRecordRevisions.RemoveRange(
                await Db.MedicalRecordRevisions.Where(r => r.MedicalRecordId.HasValue && emrIds.Contains(r.MedicalRecordId.Value)).ToListAsync());
            await Db.SaveChangesAsync();

            Db.MedicalRecords.RemoveRange(
                await Db.MedicalRecords.Where(mr => emrIds.Contains(mr.Id)).ToListAsync());
            await Db.SaveChangesAsync();
        }

        if (visitIds.Count > 0)
        {
            Db.VisitDiagnoses.RemoveRange(
                await Db.VisitDiagnoses.Where(d => d.VisitId.HasValue && visitIds.Contains(d.VisitId.Value)).ToListAsync());
            await Db.SaveChangesAsync();
        }

        var payments = await Db.Payments
            .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
            .ToListAsync();
        var paymentIds = payments.Select(p => p.Id).ToList();
        if (paymentIds.Count > 0)
        {
            Db.PaymentGatewayEvents.RemoveRange(
                await Db.PaymentGatewayEvents.Where(e => e.PaymentId.HasValue && paymentIds.Contains(e.PaymentId.Value)).ToListAsync());
            await Db.SaveChangesAsync();

            Db.Payments.RemoveRange(payments);
            await Db.SaveChangesAsync();
        }

        if (visitIds.Count > 0)
        {
            Db.VisitCharges.RemoveRange(
                await Db.VisitCharges.Where(c => c.VisitId.HasValue && visitIds.Contains(c.VisitId.Value)).ToListAsync());
            await Db.SaveChangesAsync();

            Db.DiagnosticOrders.RemoveRange(
                await Db.DiagnosticOrders.Where(o => orderIds.Contains(o.Id)).ToListAsync());
            await Db.SaveChangesAsync();
        }

        var userIds = new[] { fx.Doctor1UserId, fx.Doctor2UserId, fx.DiagStaff1UserId, fx.DiagStaff2UserId, fx.PatientUserId, fx.OtherPatientUserId, fx.Reception1UserId };
        Db.AuditLogs.RemoveRange(
            await Db.AuditLogs.Where(a => a.HospitalId == fx.HospitalId || (a.ActorUserId.HasValue && userIds.Contains(a.ActorUserId.Value))).ToListAsync());
        await Db.SaveChangesAsync();

        if (visitIds.Count > 0)
        {
            Db.Visits.RemoveRange(await Db.Visits.Where(v => visitIds.Contains(v.Id)).ToListAsync());
            await Db.SaveChangesAsync();
        }

        Db.QueueServicePoints.RemoveRange(await Db.QueueServicePoints.Where(sp => spIds.Contains(sp.Id)).ToListAsync());
        await Db.SaveChangesAsync();

        var docIds = new[] { fx.Doctor1Id, fx.Doctor2Id };
        Db.DoctorServiceAuthorizations.RemoveRange(
            await Db.DoctorServiceAuthorizations.Where(a => docIds.Contains(a.DoctorId)).ToListAsync());
        await Db.SaveChangesAsync();

        var serviceIds = new[] { fx.ExamServiceId, fx.LabVerifyServiceId, fx.ImagingDirectServiceId, fx.UnpricedDiagServiceId };
        Db.ServicePrices.RemoveRange(
            await Db.ServicePrices.Where(sp => sp.ServiceId.HasValue && serviceIds.Contains(sp.ServiceId.Value)).ToListAsync());
        await Db.SaveChangesAsync();

        var med = await Db.Medicines.FirstOrDefaultAsync(m => m.Id == fx.MedicineId);
        if (med is not null) Db.Medicines.Remove(med);
        await Db.SaveChangesAsync();

        Db.Patients.RemoveRange(await Db.Patients.Where(p => patientIds.Contains(p.Id)).ToListAsync());
        await Db.SaveChangesAsync();

        Db.Doctors.RemoveRange(await Db.Doctors.Where(d => docIds.Contains(d.Id)).ToListAsync());
        await Db.SaveChangesAsync();

        var staffIds = new[] { fx.Doctor1StaffId, fx.Doctor2StaffId, fx.DiagStaff1Id, fx.DiagStaff2Id, fx.Reception1StaffId };
        Db.Staff.RemoveRange(await Db.Staff.Where(s => staffIds.Contains(s.Id)).ToListAsync());
        await Db.SaveChangesAsync();

        Db.UserRoles.RemoveRange(await Db.UserRoles.Where(ur => userIds.Contains(ur.UserId)).ToListAsync());
        await Db.SaveChangesAsync();

        Db.Users.RemoveRange(await Db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync());
        await Db.SaveChangesAsync();

        var roomIds = new[] { fx.ExamRoom1Id, fx.ExamRoom2Id, fx.LabRoomId, fx.ImagingRoomId };
        Db.HospitalRooms.RemoveRange(await Db.HospitalRooms.Where(r => roomIds.Contains(r.Id)).ToListAsync());
        await Db.SaveChangesAsync();

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

        if (fx.CreatedIcd10Id.HasValue)
        {
            var createdIcd = await Db.Icd10Codes.FirstOrDefaultAsync(c => c.Id == fx.CreatedIcd10Id.Value);
            if (createdIcd is not null)
            {
                Db.Icd10Codes.Remove(createdIcd);
                await Db.SaveChangesAsync();
            }
        }
    }
}
