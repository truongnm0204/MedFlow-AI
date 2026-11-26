using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MedFlow.IntegrationTests.Payments;

/// <summary>
/// Stage B (GH-851 / ADR-0060) Real-PostgreSQL Verification Suite.
/// Covers PAY-01..PAY-22, RBAC matrices, live UC58 permission revocation, idempotency replay/mismatch,
/// Migration 024 + preflight_024_audit.sql verification, and deterministic multi-connection concurrency races.
/// </summary>
public sealed class StageBPaymentSelectionAndCashPostgresTests : AuthTestBase
{
    private const string WebhookUrl = "/api/v1/payments/sepay/webhook";
    private const string WebhookApiKey = "IntegrationTest_SePay_Key";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static long _webhookSeq = 900_000;

    public StageBPaymentSelectionAndCashPostgresTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    // =========================================================================
    // PAY-01 & PAY-02:
    // Unpaid Exam charge blocks UC31 workspace while allowing QR/Cash;
    // Paid Exam charge unlocks UC31 without unlocking subsequent Diagnostic charges.
    // =========================================================================

    [Fact]
    public async Task Pay01_Pay02_ExamUnpaidBlocksWorkspace_AndExamPaidDoesNotUnlockDiagnosticCharges()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "P0102", examPrice: 150_000m, payExamNow: false);

            // PAY-01: Unpaid Exam charge blocks UC31 OpenWorkspaceAsync (409)
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!));
            }

            // Patient can view charges and generate VietQR for unpaid Exam
            var chargesRes = await Client.SendAsync(AuthedGet(
                $"/api/v1/patients/visits/{visit.VisitCode}/charges",
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, chargesRes.StatusCode);
            var chargesDto = await chargesRes.Content.ReadFromJsonAsync<VisitChargesResponseDto>(WebJson);
            Assert.NotNull(chargesDto);
            Assert.True(chargesDto!.CanPayByQr);
            Assert.Equal(150_000m, chargesDto.AmountDue);

            var qrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [chargesDto.Charges[0].ChargeId!.Value]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, qrRes.StatusCode);
            var qrDto = await qrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson);
            Assert.NotNull(qrDto);
            Assert.Equal(150_000m, qrDto!.Amount);

            // Settle Exam via SePay webhook -> PAY-02: Exam PAID unlocks UC31
            var whRes = await PostWebhookAsync(NextWebhookId(), 150_000m, qrDto.PaymentCode);
            Assert.Equal(HttpStatusCode.OK, whRes.StatusCode);

            long mandatoryOrderId;
            string mandatoryOrderCode;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var opened = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                Assert.Equal("IN_PROGRESS", opened.WorkspaceState);

                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.LabServiceId,
                        Instruction: "Xét nghiệm công thức máu bắt buộc",
                        RequiredForConclusion: true));
                mandatoryOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                mandatoryOrderCode = created.CreatedDiagnosticOrders[0].OrderCode;
            }

            // PAY-02: Exam PAID never unlocks newly created CLS order
            var afterOrderCharges = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            Assert.Equal("UNPAID", afterOrderCharges.SettlementStatus);
            Assert.Equal(120_000m, afterOrderCharges.AmountDue);
            var diagLine = Assert.Single(afterOrderCharges.Charges, l => l.DiagnosticOrderId == mandatoryOrderId);
            Assert.Equal("UNPAID", diagLine.LineSettlementStatus);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    reviewSvc.ConfirmDiagnosticOrderAsync(
                        fx.DiagStaffUserId,
                        mandatoryOrderCode,
                        new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true)));
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // PAY-03, PAY-04 & PAY-18:
    // Mandatory CLS starts ACCEPTED & blocks CompleteVisit/UC42 while unpaid;
    // Optional CLS starts UNDECIDED, excluded from SelectedAmountDue, blocked in UC42;
    // Additional post-exam CLS creates independent charge without touching prior PAID allocations.
    // =========================================================================

    [Fact]
    public async Task Pay03_Pay04_Pay18_MandatoryVsOptionalSelectionDefaults_IndependentPostExamOrder_AndUc42Gates()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "P0304", examPrice: 150_000m, payExamNow: true);
            long mandatoryOrderId;
            string mandatoryOrderCode;
            long optionalOrderId;
            string optionalOrderCode;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Bụng mềm, ấn tức thượng vị.",
                        TreatmentPlan: "Theo dõi xét nghiệm máu."));

                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(
                                ServiceId: fx.LabServiceId,
                                Instruction: "Xét nghiệm bắt buộc",
                                RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(
                                ServiceId: fx.ImagingServiceId,
                                Instruction: "Siêu âm tùy chọn",
                                RequiredForConclusion: false),
                        ]));

                mandatoryOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                mandatoryOrderCode = created.CreatedDiagnosticOrders[0].OrderCode;
                optionalOrderId = created.CreatedDiagnosticOrders[1].OrderId;
                optionalOrderCode = created.CreatedDiagnosticOrders[1].OrderCode;
            }

            Db.ChangeTracker.Clear();
            var mandatoryOrder = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == mandatoryOrderId);
            var optionalOrder = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == optionalOrderId);

            // PAY-03 & PAY-04 selection defaults
            Assert.Equal(DiagnosticSelectionStatuses.Accepted, mandatoryOrder.SelectionStatus);
            Assert.Equal(DiagnosticSelectionStatuses.Undecided, optionalOrder.SelectionStatus);

            var snapshot = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            // PayableTotal = 150k (Exam) + 120k (Mandatory) + 240k (Optional) = 510k
            // AmountDue = 360k, SelectedAmountDue = 120k (excludes UNDECIDED optional 240k!)
            Assert.Equal(510_000m, snapshot.PayableTotal);
            Assert.Equal(360_000m, snapshot.AmountDue);
            Assert.Equal(120_000m, snapshot.SelectedAmountDue);

            // PAY-03: Mandatory unpaid order blocks CompleteVisitAsync & UC39/UC42
            // PAY-04: Optional UNDECIDED order also blocks UC39/UC42
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();

                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(fx.Doctor1UserId, visit.VisitCode!));

                await Assert.ThrowsAsync<ConflictException>(() =>
                    reviewSvc.ConfirmDiagnosticOrderAsync(
                        fx.DiagStaffUserId,
                        mandatoryOrderCode,
                        new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true)));

                await Assert.ThrowsAsync<ConflictException>(() =>
                    reviewSvc.ConfirmDiagnosticOrderAsync(
                        fx.DiagStaffUserId,
                        optionalOrderCode,
                        new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true)));
            }

            // Even if an optional order has 0đ (WAIVED) price, UNDECIDED still blocks UC39/UC42 until accepted!
            long freeOptionalOrderId;
            string freeOptionalOrderCode;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var createdFree = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.FreeDiagServiceId,
                        Instruction: "Đo điện tim miễn phí tùy chọn",
                        RequiredForConclusion: false));
                freeOptionalOrderId = createdFree.CreatedDiagnosticOrders![0].OrderId;
                freeOptionalOrderCode = createdFree.CreatedDiagnosticOrders[0].OrderCode;

                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var undecidedEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    reviewSvc.ConfirmDiagnosticOrderAsync(
                        fx.DiagStaffUserId,
                        freeOptionalOrderCode,
                        new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true)));
                Assert.Contains("UNDECIDED", undecidedEx.Message, StringComparison.OrdinalIgnoreCase);
            }

            // Pay Mandatory order independently via Reception Cash
            var mandatoryChargeId = snapshot.Charges.Single(c => c.DiagnosticOrderId == mandatoryOrderId).ChargeId!.Value;
            var cashRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [mandatoryChargeId],
                    IdempotencyKey: $"cash-p03-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Created, cashRes.StatusCode);

            // PAY-18: Create an additional post-exam diagnostic order -> independent charge, prior PAID allocations untouched
            long additionalOrderId;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var extra = await consultSvc.CreateAdditionalDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateAdditionalDiagnosticOrderRequest(
                        AdditionalOrderReason: "Cần siêu âm bổ sung đối chiếu.",
                        ServiceId: fx.Imaging2ServiceId,
                        Instruction: "Siêu âm Doppler mạch bụng",
                        RequiredForConclusion: false));
                additionalOrderId = extra.CreatedDiagnosticOrders![^1].OrderId;
            }

            Db.ChangeTracker.Clear();
            var mandatoryAlloc = await Db.PaymentAllocations.AsNoTracking()
                .SingleAsync(pa => pa.VisitChargeId == mandatoryChargeId);
            Assert.Equal(PaymentAllocationStatuses.Paid, mandatoryAlloc.Status);

            var extraCharge = await Db.VisitCharges.AsNoTracking()
                .SingleAsync(c => c.DiagnosticOrderId == additionalOrderId);
            Assert.Equal(VisitChargeStatuses.Active, extraCharge.Status);
            Assert.Equal(180_000m, extraCharge.UnitPrice);
            Assert.Equal(0, await Db.PaymentAllocations.CountAsync(pa => pa.VisitChargeId == extraCharge.Id));

            // Suppress unused warning on freeOptionalOrderId
            Assert.True(freeOptionalOrderId > 0);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // PAY-05:
    // Unpaid Optional CLS decline cancels open PENDING QR + allocations, writes audit,
    // cancels charge & order (DECLINED), and unblocks CompleteVisitAsync.
    // =========================================================================

    [Fact]
    public async Task Pay05_UnpaidOptionalDecline_CancelsPendingQr_WritesAudit_AndUnblocksCompleteVisit()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "P05", examPrice: 150_000m, payExamNow: true);
            long optOrderId;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Tim đều, phổi trong.",
                        TreatmentPlan: "Điều trị ngoại trú."));

                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.ImagingServiceId,
                        Instruction: "Siêu âm ổ bụng tùy chọn",
                        RequiredForConclusion: false));
                optOrderId = created.CreatedDiagnosticOrders![0].OrderId;
            }

            var chargesBefore = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            var optChargeId = chargesBefore.Charges.Single(c => c.DiagnosticOrderId == optOrderId).ChargeId!.Value;

            // Patient first selects and generates a PENDING QR for the optional order
            var qrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [optChargeId]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, qrRes.StatusCode);
            var qrDto = await qrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson);
            Assert.NotNull(qrDto);

            // Patient then declines the unpaid optional order
            var declineRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/diagnostic-orders/{optOrderId}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Bệnh nhân chưa có nhu cầu siêu âm hôm nay."),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, declineRes.StatusCode);
            var declineDto = await declineRes.Content.ReadFromJsonAsync<DeclineDiagnosticOrderResponseDto>(WebJson);
            Assert.NotNull(declineDto);
            Assert.Equal("CANCELLED", declineDto!.OrderStatus);
            Assert.Equal(DiagnosticSelectionStatuses.Declined, declineDto.SelectionStatus);
            Assert.Equal(VisitChargeStatuses.Cancelled, declineDto.ChargeStatus);
            Assert.Equal(1, declineDto.CancelledPendingQrCount);

            Db.ChangeTracker.Clear();
            var cancelledQr = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == qrDto!.PaymentId);
            Assert.Equal(PaymentStatuses.Cancelled, cancelledQr.Status);
            var cancelledAlloc = await Db.PaymentAllocations.AsNoTracking().SingleAsync(pa => pa.PaymentId == qrDto!.PaymentId);
            Assert.Equal(PaymentAllocationStatuses.Cancelled, cancelledAlloc.Status);

            Assert.True(await Db.AuditLogs.AnyAsync(a =>
                (a.EntityId == optOrderId.ToString() || a.EntityId == declineDto.OrderCode) &&
                a.Action == "DIAGNOSTIC_ORDER_DECLINED"));

            // Doctor completes visit cleanly without payment for declined optional order
            await CompleteVisitWithConfirmedDiagnosisAsync(fx, visit.VisitCode!);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // PAY-06:
    // Mandatory CLS decline/cancel is blocked (409 MANDATORY_ORDER_REVIEW_REQUIRED) for
    // PATIENT, RECEPTION, and non-technical DIAGNOSTIC_STAFF; TechnicalAbort sets REVIEW_REQUIRED
    // and STILL blocks CompleteVisitAsync until DOCTOR clinically resolves it.
    // =========================================================================

    [Fact]
    public async Task Pay06_MandatoryDeclineBlockedForPatientReceptionAndStaff_AndTechnicalAbortBlocksConclusionUntilDoctorResolves()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "P06", examPrice: 150_000m, payExamNow: true);
            long mandatoryOrderId;
            string mandatoryOrderCode;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Đau thượng vị cấp.",
                        TreatmentPlan: "Chờ xét nghiệm bắt buộc."));

                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.LabServiceId,
                        Instruction: "Xét nghiệm bắt buộc phục vụ chẩn đoán",
                        RequiredForConclusion: true));
                mandatoryOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                mandatoryOrderCode = created.CreatedDiagnosticOrders[0].OrderCode;
            }

            // 1. PATIENT decline on Mandatory -> 409 [MANDATORY_ORDER_REVIEW_REQUIRED]
            var patDecline = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/diagnostic-orders/{mandatoryOrderId}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Sợ lấy máu"),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Conflict, patDecline.StatusCode);
            Assert.Contains("MANDATORY_ORDER_REVIEW_REQUIRED", await patDecline.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // 2. RECEPTION decline on Mandatory -> 409 [MANDATORY_ORDER_REVIEW_REQUIRED]
            var recDecline = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/diagnostic-orders/{mandatoryOrderId}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Bệnh nhân báo tại quầy"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, recDecline.StatusCode);
            Assert.Contains("MANDATORY_ORDER_REVIEW_REQUIRED", await recDecline.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // 3. DIAGNOSTIC_STAFF non-technical cancel on Mandatory -> 409 [MANDATORY_ORDER_REVIEW_REQUIRED]
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var nonTechEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    reviewSvc.CancelDiagnosticOrderAsync(
                        fx.DiagStaffUserId,
                        mandatoryOrderCode,
                        new CancelDiagnosticOrderRequest(Reason: "Bệnh nhân từ chối", TechnicalAbort: false)));
                Assert.Contains("MANDATORY_ORDER_REVIEW_REQUIRED", nonTechEx.Message, StringComparison.Ordinal);
            }

            // 4. Patient requests clinical review via /selection -> sets REVIEW_REQUIRED + audit without cancelling
            var reviewReqRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/diagnostic-orders/selection",
                new UpdateDiagnosticOrderSelectionRequestDto(
                    ReviewRequestedOrderIds: [mandatoryOrderId],
                    Reason: "Xin gặp lại bác sĩ tư vấn"),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, reviewReqRes.StatusCode);

            Db.ChangeTracker.Clear();
            var afterReviewReq = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == mandatoryOrderId);
            Assert.Equal("ORDERED", afterReviewReq.Status);
            Assert.True(afterReviewReq.RequiredForConclusion);
            Assert.Equal(DiagnosticSelectionStatuses.ReviewRequired, afterReviewReq.SelectionStatus);
            Assert.True(await Db.AuditLogs.AnyAsync(a =>
                a.EntityId == mandatoryOrderId.ToString() &&
                a.Action == "MANDATORY_ORDER_REVIEW_REQUESTED"));

            // Reset selection to ACCEPTED by Doctor & settle charge so DIAGNOSTIC_STAFF runs TechnicalAbort
            await Client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/visits/{visit.VisitCode}/diagnostic-orders/selection",
                new UpdateDiagnosticOrderSelectionRequestDto(AcceptedOrderIds: [mandatoryOrderId]),
                fx.Doctor1UserId,
                RoleNames.Doctor));

            var charges = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            var mandatoryChargeId = charges.Charges.Single(c => c.DiagnosticOrderId == mandatoryOrderId).ChargeId!.Value;
            var cashRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [mandatoryChargeId],
                    IdempotencyKey: $"cash-p06-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Created, cashRes.StatusCode);

            // 5. DIAGNOSTIC_STAFF executes TechnicalAbort = true -> order CANCELLED + SelectionStatus = REVIEW_REQUIRED
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var aborted = await reviewSvc.CancelDiagnosticOrderAsync(
                    fx.DiagStaffUserId,
                    mandatoryOrderCode,
                    new CancelDiagnosticOrderRequest(
                        Reason: "Hệ thống máy huyết học dừng hiệu chuẩn khẩn cấp",
                        TechnicalAbort: true));
                Assert.Equal("CANCELLED", aborted.OrderStatus);
            }

            Db.ChangeTracker.Clear();
            var abortedOrder = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == mandatoryOrderId);
            Assert.Equal(DiagnosticSelectionStatuses.ReviewRequired, abortedOrder.SelectionStatus);

            // CRITICAL (PAY-06 / BR-03): TechnicalAbort MUST NOT bypass mandatory conclusion gate!
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new RecordDiagnosisRequest(
                            PrimaryIcd10Code: fx.Icd10Code,
                            ClinicalConclusion: "Cố kết luận khi CLS bắt buộc bị hủy kỹ thuật chưa xử lý",
                            IsConfirmed: true)));

                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(fx.Doctor1UserId, visit.VisitCode!));

                // 6. Doctor clinically resolves the REVIEW_REQUIRED mandatory order -> unblocks conclusion!
                await consultSvc.CancelDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    mandatoryOrderCode,
                    new CancelConsultationDiagnosticOrderRequest(
                        Reason: "Bác sĩ xác nhận chuyển phác đồ lâm sàng không cần xét nghiệm máy đang bảo trì.",
                        ReconciliationRequired: true));
            }

            await CompleteVisitWithConfirmedDiagnosisAsync(fx, visit.VisitCode!);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // PAY-07, PAY-08, PAY-09, PAY-21 & VietQR RBAC + Idempotency:
    // Single QR, Grouped QR (1 Payment + N PaymentAllocations), Role Matrix (Doctor/Reception),
    // Selection change cancels ENTIRE old grouped QR + allocations, SePay atomic settlement,
    // and VietQR IdempotencyKey replay/mismatch.
    // =========================================================================

    [Fact]
    public async Task Pay07_Pay08_Pay09_Pay21_SingleAndGroupedVietQr_RbacMatrix_SelectionChangeCancelsOldQr_AndAtomicSePaySettlement()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "P0721", examPrice: 150_000m, payExamNow: true);
            long cls1OrderId;
            long cls2OrderId;
            long cls3OrderId;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "CLS 1 (120k)", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "CLS 2 (240k)", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.Imaging2ServiceId, Instruction: "CLS 3 (180k)", RequiredForConclusion: false),
                        ]));
                cls1OrderId = created.CreatedDiagnosticOrders![0].OrderId;
                cls2OrderId = created.CreatedDiagnosticOrders[1].OrderId;
                cls3OrderId = created.CreatedDiagnosticOrders[2].OrderId;
            }

            var charges = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            var cls1ChargeId = charges.Charges.Single(c => c.DiagnosticOrderId == cls1OrderId).ChargeId!.Value;
            var cls2ChargeId = charges.Charges.Single(c => c.DiagnosticOrderId == cls2OrderId).ChargeId!.Value;
            var cls3ChargeId = charges.Charges.Single(c => c.DiagnosticOrderId == cls3OrderId).ChargeId!.Value;

            // --- VietQR RBAC Matrix (§4) ---
            // Unassigned same-hospital Doctor2 -> 403
            var unassignedDocRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cls1ChargeId]),
                fx.Doctor2UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Forbidden, unassignedDocRes.StatusCode);

            // Cross-hospital Reception2 -> 403
            var crossHospRecRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cls1ChargeId]),
                fx.OtherHospReceptionUserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Forbidden, crossHospRecRes.StatusCode);

            // DiagnosticStaff -> 403
            var diagQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cls1ChargeId]),
                fx.DiagStaffUserId,
                RoleNames.DiagnosticStaff));
            Assert.Equal(HttpStatusCode.Forbidden, diagQrRes.StatusCode);

            // PAY-09: Assigned Doctor1 generates single-charge QR + IdempotencyKey replay & mismatch
            var qrIdemKey = $"qr-idem-{Guid.NewGuid():N}";
            var singleQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cls1ChargeId], IdempotencyKey: qrIdemKey),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Created, singleQrRes.StatusCode);
            var singleQrDto = (await singleQrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
            Assert.Equal(120_000m, singleQrDto.Amount);
            Assert.Single(singleQrDto.Allocations!);

            // Idempotent replay with same IdempotencyKey & same chargeIds -> 200 OK Reused
            var replayQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cls1ChargeId], IdempotencyKey: qrIdemKey),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.OK, replayQrRes.StatusCode);
            var replayQrDto = (await replayQrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
            Assert.Equal(singleQrDto.PaymentId, replayQrDto.PaymentId);

            // Same IdempotencyKey with different chargeIds -> 409 [IDEMPOTENCY_PAYLOAD_MISMATCH]
            var mismatchQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cls1ChargeId, cls2ChargeId], IdempotencyKey: qrIdemKey),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Conflict, mismatchQrRes.StatusCode);
            Assert.Contains("IDEMPOTENCY_PAYLOAD_MISMATCH", await mismatchQrRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // PAY-07 & PAY-21: Patient requests Grouped QR [cls1, cls2] = 360k -> cancels singleQrDto, creates grouped PAY-001
            var grouped1Res = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cls1ChargeId, cls2ChargeId]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, grouped1Res.StatusCode);
            var grouped1Dto = (await grouped1Res.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
            Assert.Equal(360_000m, grouped1Dto.Amount);
            Assert.Null(grouped1Dto.VisitChargeId);
            Assert.Equal(2, grouped1Dto.Allocations!.Count);

            // PAY-21: Patient changes selection from [cls1, cls2] (360k) to [cls2, cls3] (420k)
            // -> ENTIRE grouped1Dto (PAY-001) + BOTH its allocations MUST become CANCELLED (Amount 360k preserved!)
            var grouped2Res = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cls2ChargeId, cls3ChargeId]),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Created, grouped2Res.StatusCode);
            var grouped2Dto = (await grouped2Res.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
            Assert.Equal(420_000m, grouped2Dto.Amount);
            Assert.Equal(2, grouped2Dto.Allocations!.Count);

            Db.ChangeTracker.Clear();
            var pay001 = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == grouped1Dto.PaymentId);
            Assert.Equal(PaymentStatuses.Cancelled, pay001.Status);
            Assert.Equal(360_000m, pay001.Amount);
            var pay001Allocs = await Db.PaymentAllocations.AsNoTracking()
                .Where(pa => pa.PaymentId == grouped1Dto.PaymentId)
                .ToListAsync();
            Assert.Equal(2, pay001Allocs.Count);
            Assert.All(pay001Allocs, pa => Assert.Equal(PaymentAllocationStatuses.Cancelled, pa.Status));

            // PAY-08: SePay exact transfer 420k settles grouped2Dto atomically -> cls2 & cls3 PAID, cls1 remains UNPAID!
            var settleRes = await PostWebhookAsync(NextWebhookId(), 420_000m, grouped2Dto.PaymentCode);
            Assert.Equal(HttpStatusCode.OK, settleRes.StatusCode);

            Db.ChangeTracker.Clear();
            var pay002 = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == grouped2Dto.PaymentId);
            Assert.Equal(PaymentStatuses.Paid, pay002.Status);
            var pay002Allocs = await Db.PaymentAllocations.AsNoTracking()
                .Where(pa => pa.PaymentId == grouped2Dto.PaymentId)
                .ToListAsync();
            Assert.Equal(2, pay002Allocs.Count);
            Assert.All(pay002Allocs, pa => Assert.Equal(PaymentAllocationStatuses.Paid, pa.Status));

            // Optional cls3 was automatically promoted to ACCEPTED when selected/paid!
            var cls3OrderAfter = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == cls3OrderId);
            Assert.Equal(DiagnosticSelectionStatuses.Accepted, cls3OrderAfter.SelectionStatus);

            var finalCharges = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            Assert.Equal("UNPAID", finalCharges.Charges.Single(c => c.ChargeId == cls1ChargeId).LineSettlementStatus);
            Assert.Equal("PAID", finalCharges.Charges.Single(c => c.ChargeId == cls2ChargeId).LineSettlementStatus);
            Assert.Equal("PAID", finalCharges.Charges.Single(c => c.ChargeId == cls3ChargeId).LineSettlementStatus);
            Assert.Equal(120_000m, finalCharges.AmountDue);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // PAY-10, PAY-11 & Supplemental Cash RBAC, Zero Admin Bypass, Live Revocation & Idempotency:
    // - Reception with PAYMENT_COLLECT collects single & grouped CASH (audit + staff_id + cancels pending QR)
    // - DOCTOR, DIAGNOSTIC_STAFF, PATIENT, SYSTEM_ADMIN -> 403
    // - Cross-hospital Reception -> 403
    // - Reception without PAYMENT_COLLECT -> 403
    // - HOSPITAL_ADMIN without PAYMENT_COLLECT -> 403 (zero admin bypass!)
    // - Live UC58 revocation of PAYMENT_COLLECT blocks next request immediately -> 403
    // - Same idempotencyKey + same payload -> 200 OK replay (1 Payment row); different payload -> 409
    // =========================================================================

    [Fact]
    public async Task Pay10_Pay11_ReceptionCashCollection_RbacMatrix_NoHospitalAdminBypass_LiveRevocation_AndIdempotency()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "P1011", examPrice: 150_000m, payExamNow: false);
            var visit2 = await CreateCheckedInVisitAsync(fx, "P1011B", examPrice: 150_000m, payExamNow: false);

            var v1Charges = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            var examChargeId = v1Charges.Charges[0].ChargeId!.Value;

            // Missing idempotencyKey -> 400 BadRequest
            var missingKeyRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [examChargeId], IdempotencyKey: "   "),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.BadRequest, missingKeyRes.StatusCode);

            // Disallowed roles -> 403 Forbidden
            foreach (var (uid, role) in new (long, string)[]
                     {
                         (fx.Doctor1UserId, RoleNames.Doctor),
                         (fx.DiagStaffUserId, RoleNames.DiagnosticStaff),
                         (fx.PatientUserId, RoleNames.Patient),
                         (fx.SysAdminUserId, RoleNames.SystemAdmin),
                     })
            {
                var roleRes = await Client.SendAsync(AuthedPostJson(
                    $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                    new CollectCashPaymentRequestDto(VisitChargeIds: [examChargeId], IdempotencyKey: $"forbid-{role}-{Guid.NewGuid():N}"),
                    uid,
                    role));
                Assert.Equal(HttpStatusCode.Forbidden, roleRes.StatusCode);
            }

            // Cross-hospital Reception -> 403 Forbidden
            var crossHospRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [examChargeId], IdempotencyKey: $"cross-{Guid.NewGuid():N}"),
                fx.OtherHospReceptionUserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Forbidden, crossHospRes.StatusCode);

            // Seed an open PENDING QR on examChargeId first to verify Cash cancels overlapping PENDING QR
            var pendingQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [examChargeId]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, pendingQrRes.StatusCode);
            var pendingQrDto = (await pendingQrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;

            // PAY-10: Reception with PAYMENT_COLLECT collects CASH + cancels overlapping PENDING QR
            var cashKey = $"cash-exam-{Guid.NewGuid():N}";
            var cashCreatedRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [examChargeId],
                    IdempotencyKey: cashKey,
                    Notes: "Thu tiền mặt phí khám tại quầy 1"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Created, cashCreatedRes.StatusCode);
            var cashDto = (await cashCreatedRes.Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;
            Assert.Equal(150_000m, cashDto.Amount);
            Assert.Equal(PaymentMethods.Cash, cashDto.PaymentMethod);
            Assert.Equal(fx.Reception1StaffId, cashDto.CollectedByStaffId);
            Assert.False(cashDto.IsIdempotentReplay);

            // Idempotent replay: same key + same visit + same charges -> 200 OK, IsIdempotentReplay = true, 1 CASH row
            var cashReplayRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [examChargeId], IdempotencyKey: cashKey),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, cashReplayRes.StatusCode);
            var replayDto = (await cashReplayRes.Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;
            Assert.True(replayDto.IsIdempotentReplay);
            Assert.Equal(cashDto.PaymentId, replayDto.PaymentId);

            // Idempotency payload mismatch: same key on different visit -> 409 [IDEMPOTENCY_PAYLOAD_MISMATCH]
            var v2Charges = await GetChargesAsPatientAsync(fx, visit2.VisitCode!);
            var v2ExamChargeId = v2Charges.Charges[0].ChargeId!.Value;
            var mismatchRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit2.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [v2ExamChargeId], IdempotencyKey: cashKey),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, mismatchRes.StatusCode);
            Assert.Contains("IDEMPOTENCY_PAYLOAD_MISMATCH", await mismatchRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Cancelled, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == pendingQrDto.PaymentId)).Status);
            Assert.True(await Db.AuditLogs.AnyAsync(a =>
                a.EntityId == cashDto.PaymentId.ToString() &&
                a.Action == CollectCashPaymentService.AuditAction));

            // Grouped CASH collection on 2 diagnostic orders of Visit 1
            long d1Id;
            long d2Id;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "CLS Cash 1", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "CLS Cash 2", RequiredForConclusion: false),
                        ]));
                d1Id = created.CreatedDiagnosticOrders![0].OrderId;
                d2Id = created.CreatedDiagnosticOrders[1].OrderId;
            }

            var v1AfterOrders = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            var d1ChargeId = v1AfterOrders.Charges.Single(c => c.DiagnosticOrderId == d1Id).ChargeId!.Value;
            var d2ChargeId = v1AfterOrders.Charges.Single(c => c.DiagnosticOrderId == d2Id).ChargeId!.Value;

            var groupedCashRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [d1ChargeId, d2ChargeId],
                    IdempotencyKey: $"cash-grp-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Created, groupedCashRes.StatusCode);
            var groupedCashDto = (await groupedCashRes.Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;
            Assert.Equal(360_000m, groupedCashDto.Amount);
            Assert.Equal(2, groupedCashDto.Allocations.Count);

            // PAY-11 + Zero HOSPITAL_ADMIN bypass + Live UC58 revocation:
            // Temporarily revoke PAYMENT_COLLECT from RECEPTION and HOSPITAL_ADMIN -> both MUST get 403 immediately!
            var paymentCollectPerm = await Db.Permissions.AsNoTracking().SingleAsync(p => p.Code == PermissionCodes.PaymentCollect);
            var receptionRole = await Db.Roles.AsNoTracking().SingleAsync(r => r.Code == RoleNames.Reception);
            var hospAdminRole = await Db.Roles.AsNoTracking().SingleAsync(r => r.Code == RoleNames.HospitalAdmin);

            var existingMappings = await Db.RolePermissions.AsNoTracking()
                .Where(rp => rp.PermissionId == paymentCollectPerm.Id &&
                             (rp.RoleId == receptionRole.Id || rp.RoleId == hospAdminRole.Id))
                .ToListAsync();

            try
            {
                await Db.RolePermissions
                    .Where(rp => rp.PermissionId == paymentCollectPerm.Id &&
                                 (rp.RoleId == receptionRole.Id || rp.RoleId == hospAdminRole.Id))
                    .ExecuteDeleteAsync();

                // Reception1 (using same JWT!) immediately blocked with 403 [PAYMENT_COLLECT_REQUIRED]
                var revokedRecRes = await Client.SendAsync(AuthedPostJson(
                    $"/api/v1/reception/visits/{visit2.VisitCode}/payments/cash",
                    new CollectCashPaymentRequestDto(VisitChargeIds: [v2ExamChargeId], IdempotencyKey: $"rev-rec-{Guid.NewGuid():N}"),
                    fx.Reception1UserId,
                    RoleNames.Reception));
                Assert.Equal(HttpStatusCode.Forbidden, revokedRecRes.StatusCode);
                Assert.Contains("PAYMENT_COLLECT", await revokedRecRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);

                // HOSPITAL_ADMIN without PAYMENT_COLLECT -> 403 Forbidden (zero admin bypass!)
                var revokedAdminRes = await Client.SendAsync(AuthedPostJson(
                    $"/api/v1/visits/{visit2.VisitCode}/payments/cash",
                    new CollectCashPaymentRequestDto(VisitChargeIds: [v2ExamChargeId], IdempotencyKey: $"rev-adm-{Guid.NewGuid():N}"),
                    fx.HospAdminUserId,
                    RoleNames.HospitalAdmin));
                Assert.Equal(HttpStatusCode.Forbidden, revokedAdminRes.StatusCode);
                Assert.Contains("PAYMENT_COLLECT", await revokedAdminRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var mapping in existingMappings)
                {
                    if (!await Db.RolePermissions.AnyAsync(rp => rp.RoleId == mapping.RoleId && rp.PermissionId == mapping.PermissionId))
                    {
                        Db.RolePermissions.Add(new RolePermission { RoleId = mapping.RoleId, PermissionId = mapping.PermissionId });
                    }
                }
                await Db.SaveChangesAsync();
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // PAY-12, PAY-13 & PAY-14:
    // Expired grouped QR regenerates without double charging;
    // Grouped QR wrong transfer amount (partial / overpaid) -> MISMATCH + RECONCILE (0 partial PAID);
    // Duplicate webhook on PAID is idempotent and never downgrades PAID.
    // =========================================================================

    [Fact]
    public async Task Pay12_Pay13_Pay14_ExpiredQrRegeneration_GroupedMismatchZeroPartialPaid_AndDuplicateWebhookIdempotency()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "P1214", examPrice: 150_000m, payExamNow: true);
            long d1Id;
            long d2Id;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "Lab 120k", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "Imaging 240k", RequiredForConclusion: true),
                        ]));
                d1Id = created.CreatedDiagnosticOrders![0].OrderId;
                d2Id = created.CreatedDiagnosticOrders[1].OrderId;
            }

            var charges = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            var c1Id = charges.Charges.Single(c => c.DiagnosticOrderId == d1Id).ChargeId!.Value;
            var c2Id = charges.Charges.Single(c => c.DiagnosticOrderId == d2Id).ChargeId!.Value;

            // PAY-12: Create grouped QR, age it > 15m, regenerate -> old Payment & Allocations EXPIRED, new PENDING issued
            var firstQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [c1Id, c2Id]),
                fx.PatientUserId,
                RoleNames.Patient));
            var firstQr = (await firstQrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;

            var staleCreatedAt = HospitalClock.GetNow(TimeProvider.System).AddMinutes(-20);
            await Db.Payments.Where(p => p.Id == firstQr.PaymentId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, staleCreatedAt));

            var regenRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [c1Id, c2Id]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, regenRes.StatusCode);
            var regenQr = (await regenRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
            Assert.NotEqual(firstQr.PaymentId, regenQr.PaymentId);
            Assert.Equal(360_000m, regenQr.Amount);

            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Expired, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == firstQr.PaymentId)).Status);
            var expiredAllocs = await Db.PaymentAllocations.AsNoTracking().Where(pa => pa.PaymentId == firstQr.PaymentId).ToListAsync();
            Assert.All(expiredAllocs, pa => Assert.Equal(PaymentAllocationStatuses.Expired, pa.Status));

            // PAY-13: Wrong transfer amount (120k instead of 360k) on grouped QR -> MISMATCH + ALL allocations RECONCILE, ZERO PAID!
            var underpayRes = await PostWebhookAsync(NextWebhookId(), 120_000m, regenQr.PaymentCode);
            Assert.Equal(HttpStatusCode.OK, underpayRes.StatusCode);

            Db.ChangeTracker.Clear();
            var mismatchPayment = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == regenQr.PaymentId);
            Assert.Equal(SePayWebhookService.MismatchPaymentStatus, mismatchPayment.Status);
            var mismatchAllocs = await Db.PaymentAllocations.AsNoTracking().Where(pa => pa.PaymentId == regenQr.PaymentId).ToListAsync();
            Assert.Equal(2, mismatchAllocs.Count);
            Assert.All(mismatchAllocs, pa => Assert.Equal(PaymentAllocationStatuses.Reconcile, pa.Status));
            Assert.Equal(0, await Db.PaymentAllocations.CountAsync(pa =>
                (pa.VisitChargeId == c1Id || pa.VisitChargeId == c2Id) && pa.Status == PaymentAllocationStatuses.Paid));

            // PAY-14: Issue fresh QR on a clean visit, settle exact amount, then send duplicate webhooks (same id + new id)
            var visitClean = await CreateCheckedInVisitAsync(fx, "P14", examPrice: 150_000m, payExamNow: false);
            var cleanCharges = await GetChargesAsPatientAsync(fx, visitClean.VisitCode!);
            var cleanExamChargeId = cleanCharges.Charges[0].ChargeId!.Value;
            var cleanQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visitClean.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cleanExamChargeId]),
                fx.PatientUserId,
                RoleNames.Patient));
            var cleanQr = (await cleanQrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;

            var exactEventId = NextWebhookId();
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(exactEventId, 150_000m, cleanQr.PaymentCode)).StatusCode);
            // Same externalId replay -> Ignored
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(exactEventId, 150_000m, cleanQr.PaymentCode)).StatusCode);
            // Different externalId on already-PAID payment (even with weird amount) -> never downgrades PAID!
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(NextWebhookId(), 50_000m, cleanQr.PaymentCode)).StatusCode);

            Db.ChangeTracker.Clear();
            var cleanPaid = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == cleanQr.PaymentId);
            Assert.Equal(PaymentStatuses.Paid, cleanPaid.Status);
            var cleanPaidAlloc = await Db.PaymentAllocations.AsNoTracking().SingleAsync(pa => pa.PaymentId == cleanQr.PaymentId);
            Assert.Equal(PaymentAllocationStatuses.Paid, cleanPaidAlloc.Status);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // PAY-15 & PAY-16:
    // Paid deferred order keeps ACCEPTED / ACTIVE / PAID;
    // Already-PAID Optional CLS voluntary skip:
    // - PATIENT self-call rejected (409 PAID_ORDER_STAFF_CONFIRMATION_REQUIRED)
    // - RECEPTION with confirmAbandonPaid=true sets SKIPPED_AFTER_PAID + PAID_NOT_PERFORMED,
    //   preserves Payment & PaymentAllocation = PAID (zero refund!), writes audit,
    //   transitions AWAITING_RESULTS -> RETURN_CONSULTATION, and unblocks CompleteVisitAsync.
    // =========================================================================

    [Fact]
    public async Task Pay15_Pay16_PaidDeferredOrderPreservesPaid_AndPaidOptionalVoluntarySkipWithoutRefund()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "P1516", examPrice: 150_000m, payExamNow: true);
            long optPaidOrderId;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Khám tổng quát ổn định.",
                        TreatmentPlan: "Theo dõi lâm sàng."));

                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.ImagingServiceId,
                        Instruction: "Siêu âm bụng tùy chọn chuyển sang AWAITING_RESULTS",
                        RequiredForConclusion: false,
                        TransitionToAwaitingResults: true));
                optPaidOrderId = created.CreatedDiagnosticOrders![0].OrderId;
            }

            var charges = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            var optChargeId = charges.Charges.Single(c => c.DiagnosticOrderId == optPaidOrderId).ChargeId!.Value;

            // Collect CASH for optional order
            var cashRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [optChargeId],
                    IdempotencyKey: $"cash-p1516-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Created, cashRes.StatusCode);
            var cashDto = (await cashRes.Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;

            // PAY-15: Setting DeferredUntilDate on paid order preserves ACCEPTED + ACTIVE + PAID
            var tomorrow = DateOnly.FromDateTime(HospitalClock.GetNow(TimeProvider.System)).AddDays(1);
            await Db.DiagnosticOrders.Where(o => o.Id == optPaidOrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.DeferredUntilDate, tomorrow));

            var deferredSnapshot = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            var deferredLine = deferredSnapshot.Charges.Single(c => c.ChargeId == optChargeId);
            Assert.Equal("PAID", deferredLine.LineSettlementStatus);
            Assert.Equal(VisitChargeStatuses.Active, deferredLine.ChargeStatus);
            Assert.Equal(DiagnosticSelectionStatuses.Accepted, deferredLine.SelectionStatus);

            // PAY-16a: PATIENT self-call on already-PAID optional order -> 409 [PAID_ORDER_STAFF_CONFIRMATION_REQUIRED]
            var patSkipRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/diagnostic-orders/{optPaidOrderId}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Tôi muốn tự hủy trên điện thoại", ConfirmAbandonPaid: true),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Conflict, patSkipRes.StatusCode);
            Assert.Contains("PAID_ORDER_STAFF_CONFIRMATION_REQUIRED", await patSkipRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // PAY-16b: RECEPTION without confirmAbandonPaid=true -> 400 BadRequest (ValidationException)
            var missingConfirmRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/diagnostic-orders/{optPaidOrderId}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Bỏ qua", ConfirmAbandonPaid: false),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.BadRequest, missingConfirmRes.StatusCode);

            // PAY-16c: RECEPTION with confirmAbandonPaid=true -> SKIPPED_AFTER_PAID + PAID_NOT_PERFORMED, Payment stays PAID!
            var staffSkipRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/diagnostic-orders/{optPaidOrderId}/decline",
                new DeclineDiagnosticOrderRequestDto(
                    Reason: "Bệnh nhân có việc gấp xin bỏ qua siêu âm tùy chọn đã đóng tiền.",
                    ConfirmAbandonPaid: true),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, staffSkipRes.StatusCode);
            var skipDto = (await staffSkipRes.Content.ReadFromJsonAsync<DeclineDiagnosticOrderResponseDto>(WebJson))!;
            Assert.True(skipDto.WasAlreadyPaid);
            Assert.Equal("CANCELLED", skipDto.OrderStatus);
            Assert.Equal(DiagnosticSelectionStatuses.SkippedAfterPaid, skipDto.SelectionStatus);
            Assert.Equal(VisitChargeStatuses.PaidNotPerformed, skipDto.ChargeStatus);
            Assert.True(skipDto.TransitionedToReturnConsultation);
            Assert.Equal("RETURN_CONSULTATION", skipDto.VisitStatus);

            Db.ChangeTracker.Clear();
            var paymentAfterSkip = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == cashDto.PaymentId);
            Assert.Equal(PaymentStatuses.Paid, paymentAfterSkip.Status);
            var allocAfterSkip = await Db.PaymentAllocations.AsNoTracking().SingleAsync(pa => pa.PaymentId == cashDto.PaymentId);
            Assert.Equal(PaymentAllocationStatuses.Paid, allocAfterSkip.Status);

            Assert.True(await Db.AuditLogs.AnyAsync(a =>
                (a.EntityId == optPaidOrderId.ToString() || a.EntityId == skipDto.OrderCode) &&
                a.Action == "PAID_DIAGNOSTIC_ORDER_SKIPPED_BY_PATIENT"));

            // Unblocks CompleteVisitAsync cleanly!
            await CompleteVisitWithConfirmedDiagnosisAsync(fx, visit.VisitCode!);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // PAY-17:
    // Late SePay transfer on an old cancelled grouped QR (after optional CLS declined
    // and Exam settled via Cash) reconciles safely without unlocking declined CLS or
    // downgrading valid PAID, and NEVER blocks unrelated ACTIVE UNPAID charges on the Visit!
    // =========================================================================

    [Fact]
    public async Task Pay17_LateWebhookOnCancelledGroupedQr_ReconcilesSafely_PreservesPaid_AndIsolatesUnrelatedCharges()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "P17", examPrice: 150_000m, payExamNow: true);
            long clsKeepId;
            long clsDeclineId;
            long clsUnrelatedId;

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "CLS Keep (120k)", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "CLS Decline (240k)", RequiredForConclusion: false),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.Imaging2ServiceId, Instruction: "CLS Unrelated (180k)", RequiredForConclusion: true),
                        ]));
                clsKeepId = created.CreatedDiagnosticOrders![0].OrderId;
                clsDeclineId = created.CreatedDiagnosticOrders[1].OrderId;
                clsUnrelatedId = created.CreatedDiagnosticOrders[2].OrderId;
            }

            var charges = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            var keepChargeId = charges.Charges.Single(c => c.DiagnosticOrderId == clsKeepId).ChargeId!.Value;
            var declineChargeId = charges.Charges.Single(c => c.DiagnosticOrderId == clsDeclineId).ChargeId!.Value;
            var unrelatedChargeId = charges.Charges.Single(c => c.DiagnosticOrderId == clsUnrelatedId).ChargeId!.Value;

            // 1. Patient creates old grouped QR for [keepChargeId, declineChargeId] = 360k
            var oldQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [keepChargeId, declineChargeId]),
                fx.PatientUserId,
                RoleNames.Patient));
            var oldQr = (await oldQrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
            Assert.Equal(360_000m, oldQr.Amount);

            // 2. Patient declines clsDeclineId -> cancels oldQr + marks declineChargeId CANCELLED
            var declineRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/diagnostic-orders/{clsDeclineId}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Không làm siêu âm"),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, declineRes.StatusCode);

            // 3. Reception collects valid CASH for keepChargeId (120k)
            var cashRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [keepChargeId],
                    IdempotencyKey: $"cash-p17-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            var cashDto = (await cashRes.Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;

            // 4. Late bank transfer arrives for oldQr.PaymentCode (360k)!
            var lateWhRes = await PostWebhookAsync(NextWebhookId(), 360_000m, oldQr.PaymentCode);
            Assert.Equal(HttpStatusCode.OK, lateWhRes.StatusCode);

            Db.ChangeTracker.Clear();
            // Old QR becomes MISMATCH + RECONCILE
            var oldPaymentReloaded = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == oldQr.PaymentId);
            Assert.Equal(SePayWebhookService.MismatchPaymentStatus, oldPaymentReloaded.Status);
            var oldAllocs = await Db.PaymentAllocations.AsNoTracking().Where(pa => pa.PaymentId == oldQr.PaymentId).ToListAsync();
            Assert.All(oldAllocs, pa => Assert.Equal(PaymentAllocationStatuses.Reconcile, pa.Status));

            // Declined charge becomes CANCELLED_REFUND_PENDING, order remains CANCELLED/DECLINED (never unlocked!)
            var declinedChargeReloaded = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.Id == declineChargeId);
            Assert.Equal(VisitChargeStatuses.CancelledRefundPending, declinedChargeReloaded.Status);

            // Valid CASH payment & allocation on keepChargeId remain 100% PAID!
            var cashPaymentReloaded = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == cashDto.PaymentId);
            Assert.Equal(PaymentStatuses.Paid, cashPaymentReloaded.Status);
            var keepChargeReloaded = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.Id == keepChargeId);
            Assert.Equal(VisitChargeStatuses.Active, keepChargeReloaded.Status);

            // CRITICAL (PAY-17): HasReconciliationItems == true, BUT unrelatedChargeId (180k) remains CanPayByQr == true and payable!
            var afterLateSnapshot = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            Assert.True(afterLateSnapshot.HasReconciliationItems);
            Assert.Equal("UNPAID", afterLateSnapshot.SettlementStatus);
            Assert.True(afterLateSnapshot.CanPayByQr);
            Assert.Equal(180_000m, afterLateSnapshot.AmountDue);

            var unrelatedQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [unrelatedChargeId]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, unrelatedQrRes.StatusCode);
            var unrelatedQr = (await unrelatedQrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
            Assert.Equal(180_000m, unrelatedQr.Amount);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // PAY-19, PAY-20 & PAY-22:
    // - PAY-19: 0đ WAIVED charge unlocks workspace & rejects QR (NothingDue), while null UnitPrice blocks & rejects QR/Cash (409)
    // - PAY-20: Other patient, revoked family link, and cross-visit charge injection all rejected (404) with zero leakage
    // - PAY-22: CompleteVisitAsync with open UNDECIDED optional CLS -> 409 when CloseUndecidedOptionalOrders omitted/false,
    //           and atomically closes as DECLINED + CANCELLED when CloseUndecidedOptionalOrders = true!
    // =========================================================================

    [Fact]
    public async Task Pay19_Pay20_Pay22_ZeroDongWaivedVsUnpriced_FamilyAndCrossVisitSecurity_AndCompleteVisitUndecidedHandling()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            // --- PAY-19: 0đ WAIVED Exam vs Unpriced charge ---
            var waivedVisit = await CreateCheckedInVisitAsync(fx, "P19W", examPrice: 0m, payExamNow: false);
            var waivedCharges = await GetChargesAsPatientAsync(fx, waivedVisit.VisitCode!);
            Assert.Equal(0m, waivedCharges.AmountDue);
            Assert.False(waivedCharges.CanPayByQr);

            var waivedQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{waivedVisit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [waivedCharges.Charges[0].ChargeId!.Value]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Conflict, waivedQrRes.StatusCode);

            // Unpriced charge (UnitPrice = null) blocks QR and Cash with 409
            var unpricedVisit = await CreateCheckedInVisitAsync(fx, "P19U", examPrice: 150_000m, payExamNow: false);
            var unpricedCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == unpricedVisit.Id);
            await Db.VisitCharges.Where(c => c.Id == unpricedCharge.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UnitPrice, (decimal?)null));

            var unpricedQrRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{unpricedVisit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [unpricedCharge.Id]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Conflict, unpricedQrRes.StatusCode);

            var unpricedCashRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{unpricedVisit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [unpricedCharge.Id], IdempotencyKey: $"unp-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, unpricedCashRes.StatusCode);

            // --- PAY-20: Revoked family link & cross-visit charge injection ---
            var targetVisit = await CreateCheckedInVisitAsync(fx, "P20A", examPrice: 333_000m, payExamNow: false);
            var otherVisit = await CreateCheckedInVisitAsync(fx, "P20B", examPrice: 150_000m, payExamNow: false);
            var targetChargeId = (await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == targetVisit.Id)).Id;
            var otherChargeId = (await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == otherVisit.Id)).Id;

            // Seed a REVOKED PatientFamilyLink from OtherPatient -> Patient
            var nowLocal = HospitalClock.GetNow(TimeProvider.System);
            Db.PatientFamilyLinks.Add(new PatientFamilyLink
            {
                OwnerPatientId = fx.OtherPatientId,
                MemberPatientId = fx.PatientId,
                Relationship = "SPOUSE",
                VerificationStatus = "VERIFIED",
                VerifiedAt = nowLocal.AddDays(-5),
                RevokedAt = nowLocal.AddHours(-1),
            });
            await Db.SaveChangesAsync();

            var revokedFamilyGet = await Client.SendAsync(AuthedGet(
                $"/api/v1/patients/visits/{targetVisit.VisitCode}/charges",
                fx.OtherPatientUserId,
                RoleNames.Patient));
            var revokedBody = await revokedFamilyGet.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, revokedFamilyGet.StatusCode);
            Assert.DoesNotContain("333000", revokedBody, StringComparison.Ordinal);

            // Cross-visit charge injection on QR and Cash -> 404 NotFound
            var crossChargeQr = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{targetVisit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [targetChargeId, otherChargeId]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.NotFound, crossChargeQr.StatusCode);

            var crossChargeCash = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{targetVisit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [targetChargeId, otherChargeId],
                    IdempotencyKey: $"xvis-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.NotFound, crossChargeCash.StatusCode);

            // --- PAY-22: CompleteVisitAsync with open UNDECIDED optional CLS ---
            long undecidedOrderId;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, waivedVisit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    waivedVisit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Họng sạch, tim phổi bình thường.",
                        TreatmentPlan: "Theo dõi tại nhà."));

                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    waivedVisit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.ImagingServiceId,
                        Instruction: "Siêu âm tùy chọn bệnh nhân chưa quyết định",
                        RequiredForConclusion: false));
                undecidedOrderId = created.CreatedDiagnosticOrders![0].OrderId;

                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    waivedVisit.VisitCode!,
                    new RecordDiagnosisRequest(
                        PrimaryIcd10Code: fx.Icd10Code,
                        ClinicalConclusion: "Khám kiểm tra sức khỏe ổn định",
                        IsConfirmed: true));

                // Without CloseUndecidedOptionalOrders = true -> 409 [UNDECIDED_OPTIONAL_ORDERS_OPEN]
                var openUndecidedEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(
                        fx.Doctor1UserId,
                        waivedVisit.VisitCode!,
                        new CompleteVisitRequest(ConfirmNoPrescription: true)));
                Assert.Contains("UNDECIDED_OPTIONAL_ORDERS_OPEN", openUndecidedEx.Message, StringComparison.Ordinal);

                // With CloseUndecidedOptionalOrders = true -> atomically closes as DECLINED + CANCELLED and completes Visit!
                var completed = await consultSvc.CompleteVisitAsync(
                    fx.Doctor1UserId,
                    waivedVisit.VisitCode!,
                    new CompleteVisitRequest(
                        ConfirmNoPrescription: true,
                        CloseUndecidedOptionalOrders: true,
                        DischargeInstructions: "Nghỉ ngơi hợp lý."));
                Assert.Equal("COMPLETED", completed.Visit.Status);
            }

            Db.ChangeTracker.Clear();
            var closedOptOrder = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == undecidedOrderId);
            Assert.Equal("CANCELLED", closedOptOrder.Status);
            Assert.Equal(DiagnosticSelectionStatuses.Declined, closedOptOrder.SelectionStatus);
            var closedOptCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == undecidedOrderId);
            Assert.Equal(VisitChargeStatuses.Cancelled, closedOptCharge.Status);
            Assert.True(await Db.AuditLogs.AnyAsync(a =>
                (a.EntityId == undecidedOrderId.ToString() || a.EntityId == closedOptOrder.OrderCode) &&
                a.Action == "DIAGNOSTIC_ORDER_CLOSED_UNDECIDED_AT_COMPLETION"));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // MIGRATION 024 & PREFLIGHT AUDIT VERIFICATION:
    // Isolated temporary PostgreSQL databases prove:
    // 1) preflight_024_audit.sql passes before & after 024;
    // 2) 024 is 100% idempotent on re-run, backfills CHARGE payments 1-to-1,
    //    and leaves LEGACY_VISIT payments with 0 guessed allocations;
    // 3) Dirty negative payment aborts 024 with [PREFLIGHT_024_FAILED] without clamping.
    // =========================================================================

    [Fact]
    public async Task Migration024_TempDatabase_Preflight_IdempotentBackfill_LegacyVisitPreservation_AndDirtyDataAbort()
    {
        var adminConn = ResolveAdminConnectionString();
        var repoRoot = ResolveRepoRoot();
        var preflightSql = await File.ReadAllTextAsync(Path.Combine(repoRoot, "database", "scripts", "preflight_024_audit.sql"));
        var migration024Sql = await File.ReadAllTextAsync(
            Path.Combine(repoRoot, "database", "migrations", "024_grouped_payment_allocations_selection_and_cross_day_visits.sql"));

        // Part A: Clean DB through 023 + deterministic CHARGE backfill + LEGACY_VISIT preservation
        await using (var cleanDb = await TempStageBDatabase.CreateAsync(adminConn))
        {
            await ApplyMigrationsUpTo023Async(cleanDb.ConnectionString, repoRoot);

            // Seed 2 explicit CHARGE payments (1 PAID, 1 PENDING) before running 024
            await ExecuteSqlAsync(
                cleanDb.ConnectionString,
                """
                INSERT INTO payments (payment_code, visit_id, visit_charge_id, payment_scope, amount, status, created_at)
                VALUES ('MF-BF-PAID', 1, 1, 'CHARGE', 200000, 'PAID', '2026-11-26 08:00:00'),
                       ('MF-BF-PEND', 1, 2, 'CHARGE', 90000, 'PENDING', '2026-11-26 08:05:00');
                """);

            await AssertPreflightZeroViolationsAsync(cleanDb.ConnectionString, preflightSql);

            // Apply 024 twice -> idempotent!
            await ExecuteSqlAsync(cleanDb.ConnectionString, migration024Sql);
            await ExecuteSqlAsync(cleanDb.ConnectionString, migration024Sql);

            await AssertPreflightZeroViolationsAsync(cleanDb.ConnectionString, preflightSql);

            await using var conn = new NpgsqlConnection(cleanDb.ConnectionString);
            await conn.OpenAsync();

            // 10 seeded LEGACY_VISIT rows remain untouched with 0 allocations
            await using (var legCmd = new NpgsqlCommand(
                """
                SELECT COUNT(*) FROM payments p
                WHERE p.payment_scope = 'LEGACY_VISIT'
                  AND p.visit_charge_id IS NULL
                  AND NOT EXISTS (SELECT 1 FROM payment_allocations pa WHERE pa.payment_id = p.id);
                """,
                conn))
            {
                Assert.Equal(10L, (long)(await legCmd.ExecuteScalarAsync())!);
            }

            // Exactly 2 backfilled allocations matching the 2 CHARGE payments
            await using (var bfCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM payment_allocations WHERE status IN ('PAID', 'PENDING');",
                conn))
            {
                Assert.Equal(2L, (long)(await bfCmd.ExecuteScalarAsync())!);
            }
        }

        // Part B: Dirty negative payment aborts Migration 024 with [PREFLIGHT_024_FAILED] and preserves dirty value
        await using (var dirtyDb = await TempStageBDatabase.CreateAsync(adminConn))
        {
            await ApplyMigrationsUpTo023Async(dirtyDb.ConnectionString, repoRoot);
            await ExecuteSqlAsync(
                dirtyDb.ConnectionString,
                """
                ALTER TABLE payments DROP CONSTRAINT IF EXISTS chk_payments_amount_non_negative;
                UPDATE payments SET amount = -99000 WHERE id = 1;
                """);

            var pgEx = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteSqlAsync(dirtyDb.ConnectionString, migration024Sql));
            Assert.Contains("[PREFLIGHT_024_FAILED]", pgEx.MessageText, StringComparison.Ordinal);

            await using var conn = new NpgsqlConnection(dirtyDb.ConnectionString);
            await conn.OpenAsync();
            await using var verifyCmd = new NpgsqlCommand("SELECT amount FROM payments WHERE id = 1;", conn);
            Assert.Equal(-99_000m, (decimal)(await verifyCmd.ExecuteScalarAsync())!);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 1:
    // Grouped QR [Exam, CLS1] vs Single QR [Exam] fired simultaneously across
    // independent DI scopes -> serialized under Visit lock; never >1 PENDING allocation per charge!
    // =========================================================================

    [Fact]
    public async Task Concurrency1_GroupedQrVsSingleQr_SharingCommonCharge_SerializesWithAtMostOnePendingAllocationPerCharge()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var visit = await CreateCheckedInVisitAsync(fx, "C1", examPrice: 150_000m, payExamNow: false);
            var nowLocal = HospitalClock.GetNow(TimeProvider.System);
            var order = new DiagnosticOrder
            {
                OrderCode = $"DIA-C1-{fx.Suffix}",
                VisitId = visit.Id,
                OrderingDoctorId = fx.Doctor1Id,
                ServiceId = fx.LabServiceId,
                Status = "ORDERED",
                RequiredForConclusion = true,
                SelectionStatus = DiagnosticSelectionStatuses.Accepted,
                OrderedAt = nowLocal,
            };
            Db.DiagnosticOrders.Add(order);
            await Db.SaveChangesAsync();

            var clsCharge = VisitCharge.CreateDiagnostic(visit.Id, order.Id, fx.LabServiceId, 120_000m, nowLocal);
            Db.VisitCharges.Add(clsCharge);
            await Db.SaveChangesAsync();

            var examChargeId = (await Db.VisitCharges.AsNoTracking()
                .SingleAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Examination)).Id;

            using var ready = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<GenerateQrPaymentResponseDto> RunQrAsync(IReadOnlyList<long> chargeIds)
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IGenerateQrPaymentService>();
                ready.Release();
                await release.Task;
                return await svc.CreateAsync(
                    fx.PatientUserId,
                    visit.VisitCode,
                    "127.0.0.1",
                    new GenerateQrPaymentRequestDto(VisitChargeIds: chargeIds));
            }

            var tGrouped = Task.Run(() => RunQrAsync([examChargeId, clsCharge.Id]));
            var tSingle = Task.Run(() => RunQrAsync([examChargeId]));

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();

            var results = await Task.WhenAll(tGrouped, tSingle);
            Assert.Equal(2, results.Length);

            Db.ChangeTracker.Clear();
            var pendingAllocs = await Db.PaymentAllocations.AsNoTracking()
                .Where(pa => pa.VisitId == visit.Id && pa.Status == PaymentAllocationStatuses.Pending)
                .ToListAsync();

            // At most ONE active PENDING allocation per charge, and only ONE active PENDING payment remains!
            Assert.True(pendingAllocs.Count(pa => pa.VisitChargeId == examChargeId) == 1);
            Assert.True(pendingAllocs.Count(pa => pa.VisitChargeId == clsCharge.Id) <= 1);
            Assert.Equal(1, await Db.Payments.CountAsync(p => p.VisitId == visit.Id && p.Status == PaymentStatuses.Pending));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 2:
    // Reception Cash Collection vs SePay Webhook:
    // - Simultaneous barrier race across independent connections: exactly 1 valid PAID allocation!
    // - Cash-first order: Cash PAID stands, SePay records DUPLICATE_RECONCILE without touching Cash PAID!
    // - Webhook-first order: SePay PAID stands, Cash receives 409 [CHARGE_ALREADY_PAID]!
    // =========================================================================

    [Fact]
    public async Task Concurrency2_CashCollectionVsSePayWebhook_SimultaneousAndBothWinOrders_NeverDoubleSettles()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            // --- 2A: Simultaneous Barrier Race ---
            var visitRace = await CreateCheckedInVisitAsync(fx, "C2R", examPrice: 150_000m, payExamNow: false);
            var raceChargeId = (await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == visitRace.Id)).Id;
            var qrRaceRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visitRace.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [raceChargeId]),
                fx.PatientUserId,
                RoleNames.Patient));
            var qrRace = (await qrRaceRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;

            using var ready = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tCash = Task.Run(async () =>
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var cashSvc = scope.ServiceProvider.GetRequiredService<ICollectCashPaymentService>();
                ready.Release();
                await release.Task;
                try
                {
                    await cashSvc.CollectAsync(
                        fx.Reception1UserId,
                        visitRace.VisitCode,
                        new CollectCashPaymentRequestDto(
                            VisitChargeIds: [raceChargeId],
                            IdempotencyKey: $"race-cash-{Guid.NewGuid():N}"));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var whRaceId = NextWebhookId();
            var tWebhook = Task.Run(async () =>
            {
                using var http = Factory.CreateClient();
                var req = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
                {
                    Content = JsonContent.Create(WebhookBody(whRaceId, 150_000m, qrRace.PaymentCode)),
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Apikey", WebhookApiKey);
                ready.Release();
                await release.Task;
                return await http.SendAsync(req);
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            await ready.WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();

            var cashErr = await tCash;
            var whHttpRes = await tWebhook;
            Assert.Equal(HttpStatusCode.OK, whHttpRes.StatusCode);

            Db.ChangeTracker.Clear();
            Assert.Equal(1, await Db.Payments.CountAsync(p => p.VisitId == visitRace.Id && p.Status == PaymentStatuses.Paid));
            Assert.Equal(1, await Db.PaymentAllocations.CountAsync(pa => pa.VisitChargeId == raceChargeId && pa.Status == PaymentAllocationStatuses.Paid));
            Assert.Equal(0, await Db.PaymentAllocations.CountAsync(pa => pa.VisitChargeId == raceChargeId && pa.Status == PaymentAllocationStatuses.Pending));

            if (cashErr is null)
            {
                // Cash won -> Webhook safely flagged DUPLICATE_RECONCILE
                var gwEvent = await Db.PaymentGatewayEvents.AsNoTracking()
                    .SingleAsync(e => e.ExternalId == whRaceId.ToString());
                Assert.Equal(SePayWebhookService.DuplicateChargeReconcileOutcome, gwEvent.Outcome);
            }
            else
            {
                // Webhook won -> Cash rejected with ConflictException [CHARGE_ALREADY_PAID]
                var conflict = Assert.IsType<ConflictException>(cashErr);
                Assert.Contains("CHARGE_ALREADY_PAID", conflict.Message, StringComparison.Ordinal);
            }

            // --- 2B: Deterministic Cash-First -> Late SePay Webhook ---
            var visitCashFirst = await CreateCheckedInVisitAsync(fx, "C2CF", examPrice: 150_000m, payExamNow: false);
            var cfChargeId = (await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == visitCashFirst.Id)).Id;
            var cfQr = (await (await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visitCashFirst.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [cfChargeId]),
                fx.PatientUserId,
                RoleNames.Patient))).Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;

            var cfCash = (await (await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visitCashFirst.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [cfChargeId], IdempotencyKey: $"cf-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception))).Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;

            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(NextWebhookId(), 150_000m, cfQr.PaymentCode)).StatusCode);

            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Paid, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == cfCash.PaymentId)).Status);
            Assert.Equal(SePayWebhookService.MismatchPaymentStatus, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == cfQr.PaymentId)).Status);

            // --- 2C: Deterministic Webhook-First -> Cash Rejected 409 [CHARGE_ALREADY_PAID] ---
            var visitWhFirst = await CreateCheckedInVisitAsync(fx, "C2WF", examPrice: 150_000m, payExamNow: false);
            var wfChargeId = (await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == visitWhFirst.Id)).Id;
            var wfQr = (await (await Client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visitWhFirst.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [wfChargeId]),
                fx.PatientUserId,
                RoleNames.Patient))).Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;

            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(NextWebhookId(), 150_000m, wfQr.PaymentCode)).StatusCode);

            var wfCashRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visitWhFirst.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [wfChargeId], IdempotencyKey: $"wf-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, wfCashRes.StatusCode);
            Assert.Contains("CHARGE_ALREADY_PAID", await wfCashRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 3:
    // (3A) Decline Unpaid Optional CLS vs Generate Grouped QR [Exam, OptionalCLS]
    // (3B) Decline Paid Optional CLS vs StartServiceExecutionAsync (UC42)
    // Both serialize under ADR-0060 lock order with zero inconsistent state!
    // =========================================================================

    [Fact]
    public async Task Concurrency3_DeclineOptionalClsVsGroupedQr_AndDeclinePaidOptionalVsStartExecution_SerializeCleanly()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            // --- 3A: Decline Unpaid Optional CLS vs Grouped QR [Exam, OptionalCLS] ---
            var visit3A = await CreateCheckedInVisitAsync(fx, "C3A", examPrice: 150_000m, payExamNow: false);
            var nowLocal = HospitalClock.GetNow(TimeProvider.System);
            var optOrder3A = new DiagnosticOrder
            {
                OrderCode = $"DIA-C3A-{fx.Suffix}",
                VisitId = visit3A.Id,
                OrderingDoctorId = fx.Doctor1Id,
                ServiceId = fx.ImagingServiceId,
                Status = "ORDERED",
                RequiredForConclusion = false,
                SelectionStatus = DiagnosticSelectionStatuses.Accepted,
                OrderedAt = nowLocal,
            };
            Db.DiagnosticOrders.Add(optOrder3A);
            await Db.SaveChangesAsync();

            var optCharge3A = VisitCharge.CreateDiagnostic(visit3A.Id, optOrder3A.Id, fx.ImagingServiceId, 240_000m, nowLocal);
            Db.VisitCharges.Add(optCharge3A);
            await Db.SaveChangesAsync();
            var examCharge3AId = (await Db.VisitCharges.AsNoTracking()
                .SingleAsync(c => c.VisitId == visit3A.Id && c.ChargeType == VisitChargeTypes.Examination)).Id;

            using (var ready = new SemaphoreSlim(0, 2))
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var tDecline = Task.Run(async () =>
                {
                    await using var scope = Factory.Services.CreateAsyncScope();
                    var selSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderSelectionService>();
                    ready.Release();
                    await release.Task;
                    return await selSvc.DeclineOrderAsync(
                        fx.PatientUserId,
                        visit3A.VisitCode,
                        optOrder3A.Id.ToString(),
                        new DeclineDiagnosticOrderRequestDto(Reason: "Từ chối đồng thời"));
                });

                var tQr = Task.Run(async () =>
                {
                    await using var scope = Factory.Services.CreateAsyncScope();
                    var qrSvc = scope.ServiceProvider.GetRequiredService<IGenerateQrPaymentService>();
                    ready.Release();
                    await release.Task;
                    try
                    {
                        await qrSvc.CreateAsync(
                            fx.PatientUserId,
                            visit3A.VisitCode,
                            "127.0.0.1",
                            new GenerateQrPaymentRequestDto(VisitChargeIds: [examCharge3AId, optCharge3A.Id]));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await ready.WaitAsync(TimeSpan.FromSeconds(5));
                await ready.WaitAsync(TimeSpan.FromSeconds(5));
                release.SetResult();

                var declineOutcome = await tDecline;
                var qrErr = await tQr;
                Assert.Equal("CANCELLED", declineOutcome.OrderStatus);
                if (qrErr is not null)
                {
                    Assert.IsType<ConflictException>(qrErr);
                }

                Db.ChangeTracker.Clear();
                // Zero PENDING allocations may remain on a declined optional order!
                Assert.Equal(0, await Db.PaymentAllocations.CountAsync(pa =>
                    pa.VisitChargeId == optCharge3A.Id && pa.Status == PaymentAllocationStatuses.Pending));
                Assert.Equal(0, await Db.Payments.CountAsync(p =>
                    p.VisitId == visit3A.Id && p.Status == PaymentStatuses.Pending));
            }

            // --- 3B: Decline Paid Optional CLS vs StartServiceExecutionAsync (UC42) ---
            var visit3B = await CreateCheckedInVisitAsync(fx, "C3B", examPrice: 150_000m, payExamNow: true);
            long optOrder3BId;
            string optOrder3BCode;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit3B.VisitCode!);
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit3B.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.ImagingServiceId,
                        Instruction: "Siêu âm tùy chọn đua với bắt đầu thực hiện UC42",
                        RequiredForConclusion: false));
                optOrder3BId = created.CreatedDiagnosticOrders![0].OrderId;
                optOrder3BCode = created.CreatedDiagnosticOrders[0].OrderCode;
            }

            var optCharge3BId = (await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == optOrder3BId)).Id;
            await Client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit3B.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [optCharge3BId], IdempotencyKey: $"c3b-{Guid.NewGuid():N}"),
                fx.Reception1UserId,
                RoleNames.Reception));

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                await reviewSvc.ConfirmDiagnosticOrderAsync(
                    fx.DiagStaffUserId,
                    optOrder3BCode,
                    new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true));
            }

            using (var ready = new SemaphoreSlim(0, 2))
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var tSkipPaid = Task.Run(async () =>
                {
                    await using var scope = Factory.Services.CreateAsyncScope();
                    var selSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderSelectionService>();
                    ready.Release();
                    await release.Task;
                    try
                    {
                        await selSvc.DeclineOrderAsync(
                            fx.Reception1UserId,
                            visit3B.VisitCode,
                            optOrder3BId.ToString(),
                            new DeclineDiagnosticOrderRequestDto(
                                Reason: "Bệnh nhân báo bỏ qua đúng lúc phòng siêu âm bấm bắt đầu",
                                ConfirmAbandonPaid: true));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                var tStartExec = Task.Run(async () =>
                {
                    await using var scope = Factory.Services.CreateAsyncScope();
                    var execSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                    ready.Release();
                    await release.Task;
                    try
                    {
                        await execSvc.StartServiceExecutionAsync(
                            fx.DiagStaffUserId,
                            optOrder3BCode,
                            new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await ready.WaitAsync(TimeSpan.FromSeconds(5));
                await ready.WaitAsync(TimeSpan.FromSeconds(5));
                release.SetResult();

                var skipErr = await tSkipPaid;
                var execErr = await tStartExec;

                // Exactly ONE succeeds and ONE throws ConflictException!
                Assert.True((skipErr is null) ^ (execErr is null));

                Db.ChangeTracker.Clear();
                var finalOrder = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == optOrder3BId);
                var finalCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.Id == optCharge3BId);

                if (skipErr is null)
                {
                    Assert.IsType<ConflictException>(execErr);
                    Assert.Equal("CANCELLED", finalOrder.Status);
                    Assert.Equal(DiagnosticSelectionStatuses.SkippedAfterPaid, finalOrder.SelectionStatus);
                    Assert.Equal(VisitChargeStatuses.PaidNotPerformed, finalCharge.Status);
                }
                else
                {
                    Assert.IsType<ConflictException>(skipErr);
                    Assert.Equal("IN_PROGRESS", finalOrder.Status);
                    Assert.Equal(DiagnosticSelectionStatuses.Accepted, finalOrder.SelectionStatus);
                    Assert.Equal(VisitChargeStatuses.Active, finalCharge.Status);
                }
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // HELPERS & ISOLATED FIXTURE MANAGEMENT
    // =========================================================================

    private static long NextWebhookId() => Interlocked.Increment(ref _webhookSeq);

    private HttpRequestMessage AuthedGet(string url, long userId, string role)
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(new User { Id = userId, Email = $"{role}-{userId}@medflow.test" }, [role]).AccessToken;
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private HttpRequestMessage AuthedPostJson<T>(string url, T body, long userId, string role)
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(new User { Id = userId, Email = $"{role}-{userId}@medflow.test" }, [role]).AccessToken;
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private async Task<VisitChargesResponseDto> GetChargesAsPatientAsync(StageBFixture fx, string visitCode)
    {
        var res = await Client.SendAsync(AuthedGet(
            $"/api/v1/patients/visits/{visitCode}/charges",
            fx.PatientUserId,
            RoleNames.Patient));
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<VisitChargesResponseDto>(body, WebJson)!;
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(long id, decimal amount, string content)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = JsonContent.Create(WebhookBody(id, amount, content)),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Apikey", WebhookApiKey);
        return await Client.SendAsync(req);
    }

    private static object WebhookBody(long id, decimal amount, string content) => new
    {
        id,
        gateway = "Vietcombank",
        transactionDate = "2026-11-26 10:30:00",
        accountNumber = "0123456789",
        code = (string?)null,
        content,
        transferType = "in",
        transferAmount = amount,
        accumulated = amount,
        subAccount = (string?)null,
        referenceCode = $"FT-SB-{id}",
    };

    private async Task CompleteVisitWithConfirmedDiagnosisAsync(StageBFixture fx, string visitCode)
    {
        Db.ChangeTracker.Clear();
        var currentVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.VisitCode == visitCode);
        if (string.Equals(currentVisit.Status, "RETURN_CONSULTATION", StringComparison.OrdinalIgnoreCase))
        {
            var callNextRes = await Client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi bệnh nhân quay lại đọc kết quả / kết luận" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callNextRes.StatusCode);

            await using var reopenScope = Factory.Services.CreateAsyncScope();
            var reopenSvc = reopenScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
            await reopenSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visitCode);
        }

        await using var scope = Factory.Services.CreateAsyncScope();
        var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();

        await consultSvc.RecordDiagnosisAsync(
            fx.Doctor1UserId,
            visitCode,
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: fx.Icd10Code,
                ClinicalConclusion: "Tình trạng nội khoa ổn định, cho về theo dõi ngoại trú.",
                IsConfirmed: true));

        var completed = await consultSvc.CompleteVisitAsync(
            fx.Doctor1UserId,
            visitCode,
            new CompleteVisitRequest(
                ConfirmNoPrescription: true,
                DischargeInstructions: "Uống đủ nước, tái khám khi cần."));
        Assert.Equal("COMPLETED", completed.Visit.Status);
    }

    private async Task<Visit> CreateCheckedInVisitAsync(
        StageBFixture fx,
        string tag,
        decimal examPrice,
        bool payExamNow)
    {
        Db.ChangeTracker.Clear();
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"VSB-{tag}-{fx.Suffix}",
            PatientId = fx.PatientId,
            AssignedDoctorId = fx.Doctor1Id,
            ExaminationRoomId = fx.ExamRoom1Id,
            Status = "CHECKED_IN",
            CheckedInAt = nowLocal.AddMinutes(-15),
            ReasonForVisit = "Khám kiểm tra tổng quát Stage B",
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        var nextQueueNo = (await Db.QueueEntries
            .Where(q => q.ServicePointId == fx.ExamSp1Id && q.QueueDate == today)
            .MaxAsync(q => (int?)q.QueueNumber) ?? 0) + 1;

        Db.QueueEntries.Add(new QueueEntry
        {
            VisitId = visit.Id,
            ServicePointId = fx.ExamSp1Id,
            CurrentServicePointId = fx.ExamSp1Id,
            QueueDate = today,
            QueueNumber = nextQueueNo,
            QueueType = "EXAMINATION",
            Status = "CALLED",
            PresenceStatus = "PRESENT",
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            ArrivedAt = nowLocal.AddMinutes(-14),
            ReadyAt = nowLocal.AddMinutes(-13),
            CalledAt = nowLocal.AddMinutes(-4),
            StateVersion = 2,
        });

        var examCharge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, examPrice, nowLocal.AddMinutes(-14));

        Db.VisitCharges.Add(examCharge);
        await Db.SaveChangesAsync();

        if (payExamNow && examPrice > 0m)
        {
            var paid = new Payment
            {
                VisitId = visit.Id,
                VisitChargeId = examCharge.Id,
                PaymentScope = PaymentScopes.Allocated,
                PaymentMethod = PaymentMethods.Cash,
                CollectedByStaffId = fx.Reception1StaffId,
                IdempotencyKey = $"seed-exam-{visit.Id}-{Guid.NewGuid():N}",
                Amount = examPrice,
                Status = PaymentStatuses.Paid,
                CreatedAt = nowLocal,
                PaidAt = nowLocal,
            };
            Db.Payments.Add(paid);
            await Db.SaveChangesAsync();
            paid.PaymentCode = $"CASH{paid.Id}";
            Db.PaymentAllocations.Add(PaymentAllocation.Create(
                paid.Id,
                examCharge.Id,
                visit.Id,
                examPrice,
                PaymentAllocationStatuses.Paid,
                nowLocal));
            await Db.SaveChangesAsync();
        }

        return visit;
    }

    private async Task<StageBFixture> CreateIsolatedFixtureAsync()
    {
        Db.ChangeTracker.Clear();
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);

        var docRole = await EnsureRoleAsync(RoleNames.Doctor);
        var diagRole = await EnsureRoleAsync(RoleNames.DiagnosticStaff);
        var recRole = await EnsureRoleAsync(RoleNames.Reception);
        var hospAdminRole = await EnsureRoleAsync(RoleNames.HospitalAdmin);
        var sysAdminRole = await EnsureRoleAsync(RoleNames.SystemAdmin);
        var patRole = await EnsureRoleAsync(RoleNames.Patient);

        await EnsureRolePermissionsAsync(docRole.Id,
        [
            PermissionCodes.MedicalRecordWrite,
            PermissionCodes.DiagnosticOrder,
            PermissionCodes.PrescriptionIssue,
            PermissionCodes.PatientProfileRead,
        ]);

        await EnsureRolePermissionsAsync(diagRole.Id,
        [
            PermissionCodes.DiagnosticOrder,
            PermissionCodes.DiagnosticResultWrite,
            PermissionCodes.QueueManage,
        ]);

        await EnsureRolePermissionsAsync(recRole.Id,
        [
            PermissionCodes.PaymentView,
            PermissionCodes.PaymentCollect,
            PermissionCodes.PatientCheckin,
            PermissionCodes.QueueManage,
        ]);

        await EnsureRolePermissionsAsync(hospAdminRole.Id,
        [
            PermissionCodes.PaymentView,
            PermissionCodes.PaymentCollect,
            PermissionCodes.HospitalConfig,
        ]);

        var existingIcd = await Db.Icd10Codes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsSelectable == true && c.NotPrimaryDx != true);
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
                Code = $"K30.{suffix[..2]}",
                NameVi = "Khó tiêu chức năng kiểm thử Stage B",
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
            var spec = new Specialty { SpecialtyCode = $"SB{suffix}", Name = $"Nội Stage B {suffix}", IsActive = true };
            Db.Specialties.Add(spec);
            await Db.SaveChangesAsync();
            specialtyId = spec.Id;
        }

        var hospital = new Hospital { HospitalCode = $"SB1{suffix}", Name = $"BV Stage B {suffix}", IsActive = true };
        var otherHospital = new Hospital { HospitalCode = $"SB2{suffix}", Name = $"BV Khác {suffix}", IsActive = true };
        Db.Hospitals.AddRange(hospital, otherHospital);
        await Db.SaveChangesAsync();

        var policy = new HospitalPolicy
        {
            HospitalId = hospital.Id,
            PolicyType = "PAYMENT",
            PolicyKey = "vietqr_account",
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN STAGE B"}""",
            PolicyVersion = 1,
        };
        Db.HospitalPolicies.Add(policy);

        var dept = new Department { HospitalId = hospital.Id, DepartmentCode = $"D1{suffix}", Name = $"Khoa Nội {suffix}", IsActive = true };
        var otherDept = new Department { HospitalId = otherHospital.Id, DepartmentCode = $"D2{suffix}", Name = $"Khoa Khác {suffix}", IsActive = true };
        Db.Departments.AddRange(dept, otherDept);
        await Db.SaveChangesAsync();

        var examService = new Service
        {
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"EX{suffix}",
            Name = "Khám Nội Stage B",
            ServiceType = "EXAMINATION",
            IsActive = true,
        };
        var labService = new Service
        {
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"LB{suffix}",
            Name = "Xét nghiệm máu Stage B",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var imagingService = new Service
        {
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"IM{suffix}",
            Name = "Siêu âm ổ bụng Stage B",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var imaging2Service = new Service
        {
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"I2{suffix}",
            Name = "Siêu âm Doppler Stage B",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var freeDiagService = new Service
        {
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"FR{suffix}",
            Name = "Đo điện tim miễn phí Stage B",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        Db.Services.AddRange(examService, labService, imagingService, imaging2Service, freeDiagService);
        await Db.SaveChangesAsync();

        Db.ServicePrices.AddRange(
            new ServicePrice { ServiceId = examService.Id, PriceAmount = 150_000m, EffectiveFrom = nowLocal.AddDays(-30) },
            new ServicePrice { ServiceId = labService.Id, PriceAmount = 120_000m, EffectiveFrom = nowLocal.AddDays(-30) },
            new ServicePrice { ServiceId = imagingService.Id, PriceAmount = 240_000m, EffectiveFrom = nowLocal.AddDays(-30) },
            new ServicePrice { ServiceId = imaging2Service.Id, PriceAmount = 180_000m, EffectiveFrom = nowLocal.AddDays(-30) },
            new ServicePrice { ServiceId = freeDiagService.Id, PriceAmount = 0m, EffectiveFrom = nowLocal.AddDays(-30) });

        var examRoom1 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"R1{suffix}", Name = "PK 101", RoomType = "CONSULTATION", Capacity = 1, Status = "ACTIVE" };
        var examRoom2 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"R2{suffix}", Name = "PK 102", RoomType = "CONSULTATION", Capacity = 1, Status = "ACTIVE" };
        var labRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"RL{suffix}", Name = "PXN 201", RoomType = "DIAGNOSTIC", Capacity = 2, Status = "ACTIVE" };
        var imgRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"RI{suffix}", Name = "PSA 202", RoomType = "DIAGNOSTIC", Capacity = 2, Status = "ACTIVE" };
        Db.HospitalRooms.AddRange(examRoom1, examRoom2, labRoom, imgRoom);
        await Db.SaveChangesAsync();

        var doc1User = new User { Email = $"d1-sb-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var doc2User = new User { Email = $"d2-sb-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var diagUser = new User { Email = $"dg-sb-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var rec1User = new User { Email = $"rc1-sb-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var otherRecUser = new User { Email = $"rc2-sb-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var hospAdminUser = new User { Email = $"ha-sb-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var sysAdminUser = new User { Email = $"sa-sb-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var patUser = new User { Email = $"pt1-sb-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var otherPatUser = new User { Email = $"pt2-sb-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        Db.Users.AddRange(doc1User, doc2User, diagUser, rec1User, otherRecUser, hospAdminUser, sysAdminUser, patUser, otherPatUser);
        await Db.SaveChangesAsync();

        Db.UserRoles.AddRange(
            new UserRole { UserId = doc1User.Id, RoleId = docRole.Id },
            new UserRole { UserId = doc2User.Id, RoleId = docRole.Id },
            new UserRole { UserId = diagUser.Id, RoleId = diagRole.Id },
            new UserRole { UserId = rec1User.Id, RoleId = recRole.Id },
            new UserRole { UserId = otherRecUser.Id, RoleId = recRole.Id },
            new UserRole { UserId = hospAdminUser.Id, RoleId = hospAdminRole.Id },
            new UserRole { UserId = sysAdminUser.Id, RoleId = sysAdminRole.Id },
            new UserRole { UserId = patUser.Id, RoleId = patRole.Id },
            new UserRole { UserId = otherPatUser.Id, RoleId = patRole.Id });

        var doc1Staff = new Staff { UserId = doc1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SD1{suffix}", FullName = "BS. Một StageB", Status = "ACTIVE" };
        var doc2Staff = new Staff { UserId = doc2User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SD2{suffix}", FullName = "BS. Hai StageB", Status = "ACTIVE" };
        var diagStaff = new Staff { UserId = diagUser.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SDG{suffix}", FullName = "KTV. CLS StageB", Status = "ACTIVE" };
        var rec1Staff = new Staff { UserId = rec1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SRC{suffix}", FullName = "TN. Thu Ngân StageB", Status = "ACTIVE" };
        var otherRecStaff = new Staff { UserId = otherRecUser.Id, HospitalId = otherHospital.Id, DepartmentId = otherDept.Id, StaffCode = $"SRO{suffix}", FullName = "TN. Cơ Sở Khác", Status = "ACTIVE" };
        var hospAdminStaff = new Staff { UserId = hospAdminUser.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SHA{suffix}", FullName = "QT. Bệnh Viện StageB", Status = "ACTIVE" };
        Db.Staff.AddRange(doc1Staff, doc2Staff, diagStaff, rec1Staff, otherRecStaff, hospAdminStaff);
        await Db.SaveChangesAsync();

        var doc1 = new Doctor { StaffId = doc1Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialtyId, DoctorCode = $"DC1{suffix}", ProfessionalTitle = "BS.CKI", IsActive = true };
        var doc2 = new Doctor { StaffId = doc2Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialtyId, DoctorCode = $"DC2{suffix}", ProfessionalTitle = "BS.CKI", IsActive = true };
        Db.Doctors.AddRange(doc1, doc2);
        await Db.SaveChangesAsync();

        Db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = examService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = labService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = imagingService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = imaging2Service.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = freeDiagService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc2.Id, ServiceId = examService.Id, IsActive = true });

        var examSp1 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom1.Id, DoctorId = doc1.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "PK 101 SB", IsActive = true };
        var examSp2 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom2.Id, DoctorId = doc2.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "PK 102 SB", IsActive = true };
        var labSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = labRoom.Id, ServiceId = labService.Id, PointType = "DIAGNOSTIC", Name = "PXN 201 SB", IsActive = true };
        var imgSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = imgRoom.Id, ServiceId = imagingService.Id, PointType = "DIAGNOSTIC", Name = "PSA 202 SB", IsActive = true };
        var img2Sp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = imgRoom.Id, ServiceId = imaging2Service.Id, PointType = "DIAGNOSTIC", Name = "PSA Doppler SB", IsActive = true };
        var freeSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = imgRoom.Id, ServiceId = freeDiagService.Id, PointType = "DIAGNOSTIC", Name = "PĐT Miễn Phí SB", IsActive = true };
        Db.QueueServicePoints.AddRange(examSp1, examSp2, labSp, imgSp, img2Sp, freeSp);

        var patient = new Patient
        {
            UserId = patUser.Id,
            PatientCode = $"PS1{suffix}",
            FullName = "Nguyễn Văn Bệnh Nhân SB",
            Gender = "MALE",
            Phone = "0909100200",
            IdentificationHash = $"sb-id-1-{suffix}",
            Status = "ACTIVE",
        };
        var otherPatient = new Patient
        {
            UserId = otherPatUser.Id,
            PatientCode = $"PS2{suffix}",
            FullName = "Trần Thị Người Khác SB",
            Gender = "FEMALE",
            Phone = "0909300400",
            IdentificationHash = $"sb-id-2-{suffix}",
            Status = "ACTIVE",
        };
        Db.Patients.AddRange(patient, otherPatient);
        await Db.SaveChangesAsync();

        return new StageBFixture(
            Suffix: suffix,
            HospitalId: hospital.Id,
            OtherHospitalId: otherHospital.Id,
            DeptId: dept.Id,
            OtherDeptId: otherDept.Id,
            PolicyId: policy.Id,
            ExamRoom1Id: examRoom1.Id,
            AllRoomIds: [examRoom1.Id, examRoom2.Id, labRoom.Id, imgRoom.Id],
            ExamSp1Id: examSp1.Id,
            AllSpIds: [examSp1.Id, examSp2.Id, labSp.Id, imgSp.Id, img2Sp.Id, freeSp.Id],
            ExamServiceId: examService.Id,
            LabServiceId: labService.Id,
            ImagingServiceId: imagingService.Id,
            Imaging2ServiceId: imaging2Service.Id,
            FreeDiagServiceId: freeDiagService.Id,
            AllServiceIds: [examService.Id, labService.Id, imagingService.Id, imaging2Service.Id, freeDiagService.Id],
            Doctor1UserId: doc1User.Id,
            Doctor1Id: doc1.Id,
            Doctor2UserId: doc2User.Id,
            Doctor2Id: doc2.Id,
            DiagStaffUserId: diagUser.Id,
            Reception1UserId: rec1User.Id,
            Reception1StaffId: rec1Staff.Id,
            OtherHospReceptionUserId: otherRecUser.Id,
            HospAdminUserId: hospAdminUser.Id,
            SysAdminUserId: sysAdminUser.Id,
            PatientUserId: patUser.Id,
            PatientId: patient.Id,
            OtherPatientUserId: otherPatUser.Id,
            OtherPatientId: otherPatient.Id,
            AllUserIds: [doc1User.Id, doc2User.Id, diagUser.Id, rec1User.Id, otherRecUser.Id, hospAdminUser.Id, sysAdminUser.Id, patUser.Id, otherPatUser.Id],
            AllStaffIds: [doc1Staff.Id, doc2Staff.Id, diagStaff.Id, rec1Staff.Id, otherRecStaff.Id, hospAdminStaff.Id],
            Icd10Code: icdCode,
            CreatedIcd10Id: createdIcdId);
    }

    private async Task<Role> EnsureRoleAsync(string code)
    {
        var role = await Db.Roles.FirstOrDefaultAsync(r => r.Code == code);
        if (role is not null)
            return role;

        role = new Role { Code = code, Name = code };
        Db.Roles.Add(role);
        await Db.SaveChangesAsync();
        return role;
    }

    private async Task EnsureRolePermissionsAsync(long roleId, IReadOnlyList<string> codes)
    {
        foreach (var code in codes)
        {
            var perm = await Db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
            if (perm is null)
            {
                perm = new Permission { Code = code };
                Db.Permissions.Add(perm);
                await Db.SaveChangesAsync();
            }

            if (!await Db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == perm.Id))
            {
                Db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = perm.Id });
                await Db.SaveChangesAsync();
            }
        }
    }

    private async Task CleanupIsolatedFixtureAsync(StageBFixture fx)
    {
        Db.ChangeTracker.Clear();

        var patientIds = new[] { fx.PatientId, fx.OtherPatientId };
        var visitIds = await Db.Visits
            .Where(v => v.PatientId.HasValue && patientIds.Contains(v.PatientId.Value))
            .Select(v => v.Id)
            .ToListAsync();

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
            await Db.DiagnosticResultVersions.Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value)).ExecuteDeleteAsync();
            await Db.DiagnosticResults.Where(r => resultIds.Contains(r.Id)).ExecuteDeleteAsync();
        }

        var emrIds = await Db.MedicalRecords
            .Where(mr => mr.VisitId.HasValue && visitIds.Contains(mr.VisitId.Value))
            .Select(mr => mr.Id)
            .ToListAsync();
        if (emrIds.Count > 0)
        {
            await Db.MedicalRecordRevisions.Where(r => r.MedicalRecordId.HasValue && emrIds.Contains(r.MedicalRecordId.Value)).ExecuteDeleteAsync();
            await Db.MedicalRecords.Where(mr => emrIds.Contains(mr.Id)).ExecuteDeleteAsync();
        }

        // Stage E2 (migration 027): fk_queue_entries_diagnostic_order_visit + fk_queue_entries_prescription_visit
        // are ON DELETE RESTRICT. Before deleting queue_entries we must (1) clear diagnostic_orders.queue_entry_id
        // (fk_diagnostic_orders_queue_entry_id) and (2) clear queue_fairness_states.last_started_queue_entry_id
        // (fk_queue_fairness_states_last_started_queue_entry_id) — both back-reference queue_entries.
        await Db.DiagnosticOrders
            .Where(o => o.QueueEntryId.HasValue && o.VisitId.HasValue && visitIds.Contains(o.VisitId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.QueueEntryId, (long?)null));
        await Db.QueueFairnessStates.Where(f => fx.AllSpIds.Contains(f.ServicePointId)).ExecuteDeleteAsync();
        await Db.QueueEntries.Where(q => q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)).ExecuteDeleteAsync();
        if (visitIds.Count > 0)
        {
            await Db.VisitDiagnoses.Where(d => d.VisitId.HasValue && visitIds.Contains(d.VisitId.Value)).ExecuteDeleteAsync();

            var paymentIds = await Db.Payments
                .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
                .Select(p => p.Id)
                .ToListAsync();

            if (paymentIds.Count > 0)
            {
                await Db.PaymentGatewayEvents.Where(e => e.PaymentId.HasValue && paymentIds.Contains(e.PaymentId.Value)).ExecuteDeleteAsync();
                await Db.Payments.Where(p => paymentIds.Contains(p.Id)).ExecuteDeleteAsync();
            }

            await Db.VisitCharges.Where(c => c.VisitId.HasValue && visitIds.Contains(c.VisitId.Value)).ExecuteDeleteAsync();
            await Db.DiagnosticOrders.Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
        }

        await Db.QueueFairnessStates.Where(f => fx.AllSpIds.Contains(f.ServicePointId)).ExecuteDeleteAsync();
        if (visitIds.Count > 0)
        {
            await Db.QueueEntries.Where(q => q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)).ExecuteDeleteAsync();
        }
        await Db.AuditLogs.Where(a =>
            a.HospitalId == fx.HospitalId ||
            a.HospitalId == fx.OtherHospitalId ||
            (a.ActorUserId.HasValue && fx.AllUserIds.Contains(a.ActorUserId.Value))).ExecuteDeleteAsync();

        if (visitIds.Count > 0)
        {
            await Db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
        }

        await Db.PatientFamilyLinks.Where(l =>
            patientIds.Contains(l.OwnerPatientId) || patientIds.Contains(l.MemberPatientId)).ExecuteDeleteAsync();

        await Db.QueueServicePoints.Where(sp => fx.AllSpIds.Contains(sp.Id)).ExecuteDeleteAsync();
        await Db.DoctorServiceAuthorizations.Where(a => a.DoctorId == fx.Doctor1Id || a.DoctorId == fx.Doctor2Id).ExecuteDeleteAsync();
        await Db.ServicePrices.Where(sp => sp.ServiceId.HasValue && fx.AllServiceIds.Contains(sp.ServiceId.Value)).ExecuteDeleteAsync();
        await Db.Patients.Where(p => patientIds.Contains(p.Id)).ExecuteDeleteAsync();
        await Db.Doctors.Where(d => d.Id == fx.Doctor1Id || d.Id == fx.Doctor2Id).ExecuteDeleteAsync();
        await Db.Staff.Where(s => fx.AllStaffIds.Contains(s.Id)).ExecuteDeleteAsync();
        await Db.UserRoles.Where(ur => fx.AllUserIds.Contains(ur.UserId)).ExecuteDeleteAsync();
        await Db.Users.Where(u => fx.AllUserIds.Contains(u.Id)).ExecuteDeleteAsync();
        await Db.HospitalRooms.Where(r => fx.AllRoomIds.Contains(r.Id)).ExecuteDeleteAsync();
        await Db.Services.Where(s => fx.AllServiceIds.Contains(s.Id)).ExecuteDeleteAsync();
        await Db.Departments.Where(d => d.Id == fx.DeptId || d.Id == fx.OtherDeptId).ExecuteDeleteAsync();
        await Db.HospitalPolicies.Where(p => p.Id == fx.PolicyId).ExecuteDeleteAsync();
        await Db.Hospitals.Where(h => h.Id == fx.HospitalId || h.Id == fx.OtherHospitalId).ExecuteDeleteAsync();

        if (fx.CreatedIcd10Id.HasValue)
        {
            await Db.Icd10Codes.Where(c => c.Id == fx.CreatedIcd10Id.Value).ExecuteDeleteAsync();
        }
    }

    private static async Task AssertPreflightZeroViolationsAsync(string connectionString, string preflightSql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(preflightSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var checks = 0;
        while (await reader.ReadAsync())
        {
            checks++;
            var checkName = reader.GetString(0);
            var violations = reader.GetInt64(1);
            Assert.True(violations == 0, $"Preflight 024 check '{checkName}' found {violations} violations.");
        }

        Assert.Equal(6, checks);
    }

    private static async Task ApplyMigrationsUpTo023Async(string connectionString, string repoRoot)
    {
        var migrationsDir = Path.Combine(repoRoot, "database", "migrations");
        var files = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("001_", StringComparison.Ordinal))
                    return false;
                if (string.CompareOrdinal(name, "024_") >= 0)
                    return false;
                return true;
            })
            .ToList();

        foreach (var file in files)
        {
            await ExecuteSqlAsync(connectionString, await File.ReadAllTextAsync(file));
        }
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync();
    }

    private static string ResolveAdminConnectionString()
    {
        var baseConn = Environment.GetEnvironmentVariable("MEDFLOW_TEST_DB")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5433;Database=medflow_test_stage_b;Username=postgres;Password=medflow_dev";
        return new NpgsqlConnectionStringBuilder(baseConn)
        {
            Database = "postgres",
            Pooling = false,
        }.ConnectionString;
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record StageBFixture(
        string Suffix,
        long HospitalId,
        long OtherHospitalId,
        long DeptId,
        long OtherDeptId,
        long PolicyId,
        long ExamRoom1Id,
        IReadOnlyList<long> AllRoomIds,
        long ExamSp1Id,
        IReadOnlyList<long> AllSpIds,
        long ExamServiceId,
        long LabServiceId,
        long ImagingServiceId,
        long Imaging2ServiceId,
        long FreeDiagServiceId,
        IReadOnlyList<long> AllServiceIds,
        long Doctor1UserId,
        long Doctor1Id,
        long Doctor2UserId,
        long Doctor2Id,
        long DiagStaffUserId,
        long Reception1UserId,
        long Reception1StaffId,
        long OtherHospReceptionUserId,
        long HospAdminUserId,
        long SysAdminUserId,
        long PatientUserId,
        long PatientId,
        long OtherPatientUserId,
        long OtherPatientId,
        IReadOnlyList<long> AllUserIds,
        IReadOnlyList<long> AllStaffIds,
        string Icd10Code,
        long? CreatedIcd10Id);

    private sealed class TempStageBDatabase : IAsyncDisposable
    {
        private readonly string _adminConn;
        public string DatabaseName { get; }
        public string ConnectionString { get; }

        private TempStageBDatabase(string adminConn, string dbName, string connStr)
        {
            _adminConn = adminConn;
            DatabaseName = dbName;
            ConnectionString = connStr;
        }

        public static async Task<TempStageBDatabase> CreateAsync(string adminConn)
        {
            var dbName = $"medflow_sb_{Guid.NewGuid():N}"[..27];
            await using (var admin = new NpgsqlConnection(adminConn))
            {
                await admin.OpenAsync();
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
                await cmd.ExecuteNonQueryAsync();
            }

            var target = new NpgsqlConnectionStringBuilder(adminConn)
            {
                Database = dbName,
                Pooling = true,
            }.ConnectionString;
            return new TempStageBDatabase(adminConn, dbName, target);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConn);
            await admin.OpenAsync();
            await using (var term = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{DatabaseName}' AND pid <> pg_backend_pid();",
                admin))
            {
                await term.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
