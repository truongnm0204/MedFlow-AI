using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Consultation;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.Infrastructure.Persistence;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace MedFlow.IntegrationTests.Payments;

/// <summary>
/// Stage E1 PR #857 Review Follow-Up Real-PostgreSQL Acceptance &amp; Deterministic Concurrency Suite.
/// Covers:
/// - E1-01: DR-01..DR-16 (fail-closed CLS routing, cross-dept/null-dept rejection, explicit point/room 409,
///          atomic batch rollback, PAID preservation, IN_PROGRESS maintenance completion &amp; UC43) + CR-E1-01
/// - E1-02: RV-T01..RV-T20 (RevalidateDiagnosticOrder decoupled from CompleteClinicalReview) + CR-E1-02..CR-E1-04
/// - E1-03: SN-01..SN-14 (v2 ExpectedResultSnapshot binding, inline approval, DRAFT->ISSUED Rx gate,
///          ReadyForReturnAt preservation on already-overdue visits) + CR-E1-05..CR-E1-08.
/// </summary>
public sealed class StageE1Pr857ReviewFixesAndConcurrencyPostgresTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly ControllableVietnamClock _clock = new(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;

    public StageE1Pr857ReviewFixesAndConcurrencyPostgresTests(TestWebAppFactory factory)
    {
        _app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
                    {
                        var nowUtc = _clock.GetUtcNow().UtcDateTime;
                        var afterStart = !notBefore.HasValue || nowUtc >= notBefore.Value;
                        var beforeExpiry = !expires.HasValue || nowUtc < expires.Value;
                        return afterStart && beforeExpiry;
                    };
                });
            });
        });

        _client = _app.CreateClient();
        _scope = _app.Services.CreateScope();
    }

    private MedFlowDbContext Db => _scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

    public void Dispose()
    {
        _client.Dispose();
        _scope.Dispose();
        _app.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    // =========================================================================
    // 1. E1-01: DR-01..DR-14 & DR-16 REAL POSTGRESQL CLS ROUTING MATRIX
    // =========================================================================

    [Fact]
    public async Task Dr01_To_Dr14_And_Dr16_FailClosedClsRouting_SharedVsCrossDept_BatchAtomic_PaidIntact_AndInProgressMaintenanceCompletion()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "PR857DR", examPrice: 150_000m, payExamNow: true);

            long labOrderId;
            string labOrderCode;
            long imgOrderId;
            string imgOrderCode;
            await using (var orderScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = orderScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(ClinicalFindings: "Khám ổn định.", TreatmentPlan: "Kiểm thử DR-01..DR-16."));
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        TransitionToAwaitingResults: true,
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "XN máu DR", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "Siêu âm DR", RequiredForConclusion: true),
                        ]));
                labOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                labOrderCode = created.CreatedDiagnosticOrders[0].OrderCode;
                imgOrderId = created.CreatedDiagnosticOrders[1].OrderId;
                imgOrderCode = created.CreatedDiagnosticOrders[1].OrderCode;
            }

            var labPaymentId = await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, imgOrderId);

            // Advance to D+1 for cross-day routing scenarios
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 8, 30, 0, DateTimeKind.Unspecified));

            // DR-03 & DR-12: Deactivate exact LabSp; add a SHARED point in ImagingDept (different dept!) while Ultrasound stays active.
            // Lab order MUST reject with 409 [NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT] and NEVER route to ImgDept shared point or ImgSp!
            await Db.QueueServicePoints.Where(sp => sp.Id == fx.LabSpId)
                .ExecuteUpdateAsync(s => s.SetProperty(sp => sp.IsActive, false));

            var crossDeptSharedSp = new QueueServicePoint
            {
                HospitalId = fx.HospitalId,
                RoomId = fx.ImagingRoomId, // ImagingDeptId != LabDeptId!
                ServiceId = null,
                PointType = "DIAGNOSTIC",
                Name = $"Shared CĐHA {fx.Suffix}",
                IsActive = true,
            };
            var roomlessLabSp = new QueueServicePoint
            {
                HospitalId = fx.HospitalId,
                RoomId = null, // DR-06: roomless DIAGNOSTIC point -> MUST reject!
                ServiceId = fx.LabServiceId,
                PointType = "DIAGNOSTIC",
                Name = $"Roomless Lab {fx.Suffix}",
                IsActive = true,
            };
            Db.QueueServicePoints.AddRange(crossDeptSharedSp, roomlessLabSp);
            await Db.SaveChangesAsync();

            var dr03Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: labOrderId),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, dr03Res.StatusCode);
            Assert.Contains("NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT", await dr03Res.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // DR-09: Foreign hospital ServicePoint -> 403 Forbidden (no cross-tenant leak!)
            var dr09Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderId: labOrderId,
                    TargetServicePointId: fx.ForeignDiagSpId),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Forbidden, dr09Res.StatusCode);

            // DR-02 & DR-11: Add a valid SHARED point in LabDept (ServiceId == null, active LabRoom & LabDept) ->
            // stale prior-day LabSp ticket cleanly reroutes to same-department sharedLabSp!
            var sharedLabSp = new QueueServicePoint
            {
                HospitalId = fx.HospitalId,
                RoomId = fx.LabRoomId,
                ServiceId = null,
                PointType = "DIAGNOSTIC",
                Name = $"Shared Lab {fx.Suffix}",
                IsActive = true,
            };
            Db.QueueServicePoints.Add(sharedLabSp);
            await Db.SaveChangesAsync();

            // DR-10: Even when sharedLabSp is active, passing explicit incompatible ImagingSpId fails 409 [INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT]!
            var dr10Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderId: labOrderId,
                    TargetServicePointId: fx.ImagingSpId),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, dr10Res.StatusCode);
            Assert.Contains("INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT", await dr10Res.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // DR-14: Verify PAID payment & allocation remain 100% intact after all 409/403 routing rejections
            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Paid, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == labPaymentId)).Status);
            Assert.Equal(PaymentAllocationStatuses.Paid, (await Db.PaymentAllocations.AsNoTracking().SingleAsync(a => a.PaymentId == labPaymentId)).Status);
            Assert.Equal(1, await Db.VisitCharges.AsNoTracking().CountAsync(c => c.DiagnosticOrderId == labOrderId));

            // DR-02 + DR-11: Resume without explicit SP routes Lab order to sharedLabSp and Imaging order to ImagingSpId
            var resumedBoth = await PostResumeAsync(
                fx,
                visit.VisitCode!,
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderIds: [labOrderId, imgOrderId]));
            Assert.Equal(2, resumedBoth.ResumedQueues.Count);
            Assert.Contains(resumedBoth.ResumedQueues, q => q.ServicePointId == sharedLabSp.Id);
            Assert.Contains(resumedBoth.ResumedQueues, q => q.ServicePointId == fx.ImagingSpId);

            // Confirm both orders at UC39 so we can test DR-13 (Batch UC40 atomic rollback)
            await using (var uc39Scope = _app.Services.CreateAsyncScope())
            {
                var reviewSvc = uc39Scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                await reviewSvc.ConfirmDiagnosticOrdersBatchAsync(
                    fx.DiagStaff1UserId,
                    new ConfirmDiagnosticOrdersBatchRequest(
                        OrderIds: [labOrderId, imgOrderId],
                        IsEligibilityVerified: true));
            }

            // DR-13: Put ImagingRoom into MAINTENANCE -> Batch Receive [labOrderId, imgOrderId] MUST fail 409 atomically
            //        without moving labOrderId's queue entry to READY!
            await Db.HospitalRooms.Where(r => r.Id == fx.ImagingRoomId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "MAINTENANCE"));

            Db.ChangeTracker.Clear();
            var labQeIdBeforeBatch = (await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == labOrderId)).QueueEntryId!.Value;
            var labQeStatusBeforeBatch = (await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == labQeIdBeforeBatch)).Status;
            Assert.Equal("WAITING", labQeStatusBeforeBatch);

            await using (var uc40BatchScope = _app.Services.CreateAsyncScope())
            {
                var receiveSvc = uc40BatchScope.ServiceProvider.GetRequiredService<IDiagnosticPatientReceiveService>();
                var batchEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    receiveSvc.ReceiveDiagnosticPatientsBatchAsync(
                        fx.DiagStaff1UserId,
                        new ReceiveDiagnosticPatientsBatchRequest(
                            OrderIds: [labOrderId, imgOrderId],
                            IsPatientPresent: true,
                            IsIdentityVerified: true,
                            IsPreparationVerified: true)));
                Assert.Contains("NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT", batchEx.Message, StringComparison.Ordinal);
            }

            Db.ChangeTracker.Clear();
            Assert.Equal("WAITING", (await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == labQeIdBeforeBatch)).Status);

            // Restore ImagingRoom, receive & start labOrder (IN_PROGRESS), THEN put LabRoom into MAINTENANCE + sharedLabSp IsActive = false
            // DR-16: Already-IN_PROGRESS order MUST still complete UC42 (IN_PROGRESS -> COMPLETED) AND enter/amend UC43 results!
            await Db.HospitalRooms.Where(r => r.Id == fx.ImagingRoomId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "ACTIVE"));

            await using (var startLabScope = _app.Services.CreateAsyncScope())
            {
                var receiveSvc = startLabScope.ServiceProvider.GetRequiredService<IDiagnosticPatientReceiveService>();
                await receiveSvc.ReceiveDiagnosticPatientAsync(
                    fx.DiagStaff1UserId,
                    labOrderCode,
                    new ReceiveDiagnosticPatientRequest(IsPatientPresent: true, IsIdentityVerified: true, IsPreparationVerified: true));

                var execSvc = startLabScope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                var started = await execSvc.StartServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    labOrderCode,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"));
                Assert.Equal("IN_PROGRESS", started.Order.Status);
            }

            await Db.HospitalRooms.Where(r => r.Id == fx.LabRoomId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "MAINTENANCE"));
            await Db.QueueServicePoints.Where(sp => sp.Id == sharedLabSp.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(sp => sp.IsActive, false));

            await using (var dr16Scope = _app.Services.CreateAsyncScope())
            {
                var execSvc = dr16Scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                var completed = await execSvc.CompleteServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    labOrderCode,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED"));
                Assert.Equal("COMPLETED", completed.Order.Status);

                var resultSvc = dr16Scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                var entered = await resultSvc.EnterDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    labOrderCode,
                    new EnterDiagnosticResultRequest(
                        ResultText: "HGB 142 g/L, WBC 6.8 G/L.",
                        Conclusion: "Công thức máu bình thường dù phòng bảo trì sau lấy mẫu.",
                        Status: "FINALIZED",
                        IsFinalized: true));
                Assert.True(entered.IsFinalized);

                var amended = await resultSvc.UpdateDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    labOrderCode,
                    new UpdateDiagnosticResultRequest(
                        ResultText: "HGB 143 g/L, WBC 6.8 G/L.",
                        Conclusion: "Công thức máu bình thường (đính chính nhẹ).",
                        CorrectionReason: "Đính chính chỉ số HGB sau đối chiếu máy."));
                Assert.Equal(2, amended.VersionNo);
            }

            // Cleanup extra test service points before fixture teardown
            await Db.QueueEntries.Where(q => q.ServicePointId == sharedLabSp.Id || q.CurrentServicePointId == sharedLabSp.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.ServicePointId, fx.LabSpId)
                    .SetProperty(q => q.CurrentServicePointId, fx.LabSpId));
            await Db.QueueServicePoints
                .Where(sp => sp.Id == crossDeptSharedSp.Id || sp.Id == roomlessLabSp.Id || sp.Id == sharedLabSp.Id)
                .ExecuteDeleteAsync();
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 2. CR-E1-01 & DR-15: CONCURRENT ADMIN ROOM DEACTIVATION VS RESUME / CLS RECEIVE
    // =========================================================================

    [Fact]
    public async Task CrE1_01_And_Dr15_ConcurrentAdminRoomDeactivation_Vs_ResumeAndClsReceive_SerializesPostLockWithoutRoutingToInactiveRoom()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var connStr = Db.Database.GetConnectionString()!;

            // Sub-race A: Admin deactivates LabRoom + LabSp while ResumeVisitAsync(DIAGNOSTIC) is in flight after pre-lock reads
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visitA = await CreateCheckedInVisitAsync(fx, "CRE101A", examPrice: 150_000m, payExamNow: true);
            var (orderAId, _) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visitA.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "XN máu đua CR-E1-01A");
            await PayDiagnosticOrderByCashAsync(fx, visitA.VisitCode!, orderAId);

            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 8, 30, 0, DateTimeKind.Unspecified));
            var dPlus1 = new DateOnly(2026, 11, 11);

            await RunRoomLockDeactivationRaceAsync(
                connStr,
                fx.LabRoomId,
                fx.LabSpId,
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var crossDaySvc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                    var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                        crossDaySvc.ResumeVisitAsync(
                            fx.Reception1UserId,
                            visitA.VisitCode!,
                            new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: orderAId)));
                    Assert.Contains("NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT", ex.Message, StringComparison.Ordinal);
                });

            Db.ChangeTracker.Clear();
            Assert.Equal(
                0,
                await Db.QueueEntries.AsNoTracking().CountAsync(q =>
                    q.VisitId == visitA.Id &&
                    q.QueueDate == dPlus1 &&
                    q.QueueType == "DIAGNOSTIC"));

            // Restore LabRoom & LabSp for Sub-race B: Admin deactivates LabRoom + LabSp while ReceiveDiagnosticPatientAsync (UC40) is in flight
            await Db.HospitalRooms.Where(r => r.Id == fx.LabRoomId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "ACTIVE"));
            await Db.QueueServicePoints.Where(sp => sp.Id == fx.LabSpId).ExecuteUpdateAsync(s => s.SetProperty(sp => sp.IsActive, true));

            await PostResumeAsync(fx, visitA.VisitCode!, new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: orderAId));
            var orderACode = (await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == orderAId)).OrderCode!;
            await using (var confirmScope = _app.Services.CreateAsyncScope())
            {
                var reviewSvc = confirmScope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                await reviewSvc.ConfirmDiagnosticOrderAsync(
                    fx.DiagStaff1UserId,
                    orderACode,
                    new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true));
            }

            await RunRoomLockDeactivationRaceAsync(
                connStr,
                fx.LabRoomId,
                fx.LabSpId,
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var receiveSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticPatientReceiveService>();
                    var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                        receiveSvc.ReceiveDiagnosticPatientAsync(
                            fx.DiagStaff1UserId,
                            orderACode,
                            new ReceiveDiagnosticPatientRequest(
                                IsPatientPresent: true,
                                IsIdentityVerified: true,
                                IsPreparationVerified: true)));
                    Assert.Contains("NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT", ex.Message, StringComparison.Ordinal);
                });

            Db.ChangeTracker.Clear();
            var dPlus1Qe = await Db.QueueEntries.AsNoTracking()
                .SingleAsync(q => q.VisitId == visitA.Id && q.QueueDate == dPlus1 && q.QueueType == "DIAGNOSTIC");
            Assert.Equal("WAITING", dPlus1Qe.Status);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 3. E1-02: RV-T01..RV-T20 CLINICAL REVIEW & REVALIDATION MATRIX
    // =========================================================================

    [Fact]
    public async Task RvT01_To_RvT20_RevalidationNeverAutoClosesSession_CompleteClinicalReviewGatesAndRoutesDeterministically()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "PR857RV", examPrice: 150_000m, payExamNow: true);

            long labOrderId;
            string labOrderCode;
            long imgOrderId;
            string imgOrderCode;
            long optDeclinedId;
            long optSkippedId;
            long reviewReqId;
            string reviewReqCode;

            // Seed initial EXAMINATION IN_PROGRESS session (also tests RV-T15 & RV-T19!)
            await using (var examScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = examScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var wsExam = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(ClinicalFindings: "Khám tổng quát.", TreatmentPlan: "Kiểm thử RV-T01..20."));

                // RV-T19: Calling CompleteClinicalReview on a normal EXAMINATION session -> 409 [INVALID_SESSION_PURPOSE_FOR_REVIEW_COMPLETION]
                var rv19Ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteClinicalReviewAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CompleteClinicalReviewRequest(ExpectedStateVersion: wsExam.Queue!.StateVersion)));
                Assert.Contains("INVALID_SESSION_PURPOSE_FOR_REVIEW_COMPLETION", rv19Ex.Message, StringComparison.Ordinal);

                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        TransitionToAwaitingResults: false,
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "XN máu bắt buộc", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "Siêu âm bắt buộc", RequiredForConclusion: true),
                        ]));
                labOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                labOrderCode = created.CreatedDiagnosticOrders[0].OrderCode;
                imgOrderId = created.CreatedDiagnosticOrders[1].OrderId;
                imgOrderCode = created.CreatedDiagnosticOrders[1].OrderCode;

                // RV-T15: Revalidating during an open EXAMINATION IN_PROGRESS session extends validity WITHOUT closing EXAMINATION!
                var rv15Ws = await consultSvc.RevalidateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    labOrderId.ToString(),
                    new RevalidateDiagnosticOrderRequest(
                        Reason: "Gia hạn trong phiên khám ban đầu kiểm thử RV-T15",
                        ExtendDays: 2,
                        TransitionToAwaitingResults: true));
                Assert.Equal("IN_PROGRESS", rv15Ws.WorkspaceState);
                Assert.Equal("IN_PROGRESS", rv15Ws.Queue!.Status);
            }

            // Create auxiliary optional / technical-abort orders to verify RV-T12, RV-T13, RV-T14, RV-T16
            var auxDeclinedOrder = new DiagnosticOrder
            {
                VisitId = visit.Id,
                OrderingDoctorId = fx.Doctor1Id,
                ServiceId = fx.VerifyLabServiceId,
                OrderCode = $"DIA-DEC-{fx.Suffix}",
                Status = "ORDERED",
                SelectionStatus = DiagnosticSelectionStatuses.Declined,
                RequiredForConclusion = false,
                OrderedAt = new DateTime(2026, 11, 10, 9, 5, 0, DateTimeKind.Unspecified),
            };
            var auxSkippedOrder = new DiagnosticOrder
            {
                VisitId = visit.Id,
                OrderingDoctorId = fx.Doctor1Id,
                ServiceId = fx.VerifyLabServiceId,
                OrderCode = $"DIA-SKP-{fx.Suffix}",
                Status = "ORDERED",
                SelectionStatus = DiagnosticSelectionStatuses.SkippedAfterPaid,
                RequiredForConclusion = false,
                OrderedAt = new DateTime(2026, 11, 10, 9, 5, 0, DateTimeKind.Unspecified),
            };
            var auxReviewReqOrder = new DiagnosticOrder
            {
                VisitId = visit.Id,
                OrderingDoctorId = fx.Doctor1Id,
                ServiceId = fx.VerifyLabServiceId,
                OrderCode = $"DIA-RVR-{fx.Suffix}",
                Status = "ORDERED",
                SelectionStatus = DiagnosticSelectionStatuses.ReviewRequired,
                RequiredForConclusion = false,
                OrderedAt = new DateTime(2026, 11, 10, 9, 5, 0, DateTimeKind.Unspecified),
            };
            var auxCompletedUc42Order = new DiagnosticOrder
            {
                VisitId = visit.Id,
                OrderingDoctorId = fx.Doctor1Id,
                ServiceId = fx.VerifyLabServiceId,
                OrderCode = $"DIA-C42-{fx.Suffix}",
                Status = "COMPLETED",
                SelectionStatus = DiagnosticSelectionStatuses.Accepted,
                RequiredForConclusion = false,
                OrderedAt = new DateTime(2026, 11, 10, 9, 5, 0, DateTimeKind.Unspecified),
                ConfirmedByStaffId = fx.Diag1StaffId,
                ConfirmedAt = new DateTime(2026, 11, 10, 9, 10, 0, DateTimeKind.Unspecified),
            };
            Db.DiagnosticOrders.AddRange(auxDeclinedOrder, auxSkippedOrder, auxReviewReqOrder, auxCompletedUc42Order);
            await Db.SaveChangesAsync();
            optDeclinedId = auxDeclinedOrder.Id;
            optSkippedId = auxSkippedOrder.Id;
            reviewReqId = auxReviewReqOrder.Id;
            reviewReqCode = auxReviewReqOrder.OrderCode!;

            // Close Day-D clinical session to AWAITING_RESULTS and pay the 2 mandatory orders
            await Db.QueueEntries.Where(q => q.VisitId == visit.Id && q.QueueType == "EXAMINATION")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.Status, "COMPLETED")
                    .SetProperty(q => q.CompletedAt, new DateTime(2026, 11, 10, 9, 15, 0, DateTimeKind.Unspecified)));
            await Db.Visits.Where(v => v.Id == visit.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, "AWAITING_RESULTS"));

            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, imgOrderId);

            // Advance 12 days to D+12 (2026-11-22 -> both mandatory orders & auxReviewReqOrder are expired!)
            var dPlus12 = new DateTime(2026, 11, 22, 9, 0, 0, DateTimeKind.Unspecified);
            _clock.SetVietnamLocalTime(dPlus12);

            // RV-T04: No open clinical ticket -> Revalidate fails 409 [CLINICAL_SESSION_NOT_IN_PROGRESS]
            var rv04Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/diagnostic-orders/{labOrderId}/revalidate",
                new RevalidateDiagnosticOrderRequest(Reason: "Thử gia hạn khi chưa có phiên khám mở"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Conflict, rv04Res.StatusCode);
            Assert.Contains("CLINICAL_SESSION_NOT_IN_PROGRESS", await rv04Res.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // Reception coordinates CLINICAL_REVIEW (ticket starts READY)
            var reviewResume = await PostResumeAsync(
                fx,
                visit.VisitCode!,
                new ResumeCrossDayVisitRequestDto(
                    Target: "RETURN_CONSULTATION",
                    CoordinationPurpose: QueueCoordinationPurposes.ClinicalReview,
                    Notes: "Điều phối CLINICAL_REVIEW kiểm thử RV-T01..20"));
            var reviewQueueId = reviewResume.ResumedQueues[0].QueueEntryId;

            // RV-T01 (WAITING) & RV-T02 (READY): Revalidate fails 409 [CLINICAL_SESSION_NOT_IN_PROGRESS]
            await Db.QueueEntries.Where(q => q.Id == reviewQueueId).ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, "WAITING"));
            var rv01Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/diagnostic-orders/{labOrderId}/revalidate",
                new RevalidateDiagnosticOrderRequest(Reason: "Thử gia hạn khi vé đang WAITING"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Conflict, rv01Res.StatusCode);
            Assert.Contains("CLINICAL_SESSION_NOT_IN_PROGRESS", await rv01Res.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            await Db.QueueEntries.Where(q => q.Id == reviewQueueId).ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, "READY"));
            var rv02Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/diagnostic-orders/{labOrderId}/revalidate",
                new RevalidateDiagnosticOrderRequest(Reason: "Thử gia hạn khi vé đang READY"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Conflict, rv02Res.StatusCode);
            Assert.Contains("CLINICAL_SESSION_NOT_IN_PROGRESS", await rv02Res.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // Call next -> ticket is CALLED -> RV-T03: Revalidate still fails 409 [CLINICAL_SESSION_NOT_IN_PROGRESS] until OpenWorkspace!
            var callRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi vào phòng khám CLINICAL_REVIEW" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callRes.StatusCode);

            var rv03Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/diagnostic-orders/{labOrderId}/revalidate",
                new RevalidateDiagnosticOrderRequest(Reason: "Thử gia hạn khi vé mới CALLED chưa mở phiên"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Conflict, rv03Res.StatusCode);
            Assert.Contains("CLINICAL_SESSION_NOT_IN_PROGRESS", await rv03Res.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // OpenWorkspace -> session is now IN_PROGRESS!
            int reviewStateVer;
            await using (var openScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = openScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var ws = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                reviewStateVer = ws.Queue!.StateVersion;

                // RV-T05: Validation bounds (ExtendDays <= 0, > 365, short reason, ValidityExtendedUntil <= now or > max) -> 400
                await Assert.ThrowsAsync<ValidationException>(() =>
                    consultSvc.RevalidateDiagnosticOrderAsync(
                        fx.Doctor1UserId, visit.VisitCode!, labOrderId.ToString(),
                        new RevalidateDiagnosticOrderRequest(Reason: "ab")));
                await Assert.ThrowsAsync<ValidationException>(() =>
                    consultSvc.RevalidateDiagnosticOrderAsync(
                        fx.Doctor1UserId, visit.VisitCode!, labOrderId.ToString(),
                        new RevalidateDiagnosticOrderRequest(Reason: "Lý do hợp lệ", ExtendDays: 0)));
                await Assert.ThrowsAsync<ValidationException>(() =>
                    consultSvc.RevalidateDiagnosticOrderAsync(
                        fx.Doctor1UserId, visit.VisitCode!, labOrderId.ToString(),
                        new RevalidateDiagnosticOrderRequest(Reason: "Lý do hợp lệ", ExtendDays: 366)));
                await Assert.ThrowsAsync<ValidationException>(() =>
                    consultSvc.RevalidateDiagnosticOrderAsync(
                        fx.Doctor1UserId, visit.VisitCode!, labOrderId.ToString(),
                        new RevalidateDiagnosticOrderRequest(Reason: "Lý do hợp lệ", ValidityExtendedUntil: dPlus12)));
                await Assert.ThrowsAsync<ValidationException>(() =>
                    consultSvc.RevalidateDiagnosticOrderAsync(
                        fx.Doctor1UserId, visit.VisitCode!, labOrderId.ToString(),
                        new RevalidateDiagnosticOrderRequest(Reason: "Lý do hợp lệ", ExtendDays: 5, ValidityExtendedUntil: dPlus12.AddDays(10))));

                // RV-T12 & RV-T13: COMPLETED, DECLINED, SKIPPED_AFTER_PAID orders -> 409
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RevalidateDiagnosticOrderAsync(
                        fx.Doctor1UserId, visit.VisitCode!, auxCompletedUc42Order.Id.ToString(),
                        new RevalidateDiagnosticOrderRequest(Reason: "Thử gia hạn order đã COMPLETED")));
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RevalidateDiagnosticOrderAsync(
                        fx.Doctor1UserId, visit.VisitCode!, optDeclinedId.ToString(),
                        new RevalidateDiagnosticOrderRequest(Reason: "Thử gia hạn order đã DECLINED")));
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RevalidateDiagnosticOrderAsync(
                        fx.Doctor1UserId, visit.VisitCode!, optSkippedId.ToString(),
                        new RevalidateDiagnosticOrderRequest(Reason: "Thử gia hạn order đã SKIPPED_AFTER_PAID")));

                // RV-T18: Unassigned Doctor2 gets 403 on both Revalidate and CompleteClinicalReview
                await Assert.ThrowsAsync<ForbiddenException>(() =>
                    consultSvc.RevalidateDiagnosticOrderAsync(
                        fx.Doctor2UserId, visit.VisitCode!, labOrderId.ToString(),
                        new RevalidateDiagnosticOrderRequest(Reason: "BS khác thử gia hạn")));
                await Assert.ThrowsAsync<ForbiddenException>(() =>
                    consultSvc.CompleteClinicalReviewAsync(
                        fx.Doctor2UserId, visit.VisitCode!,
                        new CompleteClinicalReviewRequest(ExpectedStateVersion: reviewStateVer)));

                // RV-T17: Mandatory results still missing -> CONFIRMED Dx, ISSUED Rx, CompleteVisit all blocked 409
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId, visit.VisitCode!,
                        new RecordDiagnosisRequest(PrimaryIcd10Code: fx.Icd10Code, ClinicalConclusion: "Thử chốt", IsConfirmed: true)));
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CreatePrescriptionAsync(
                        fx.Doctor1UserId, visit.VisitCode!,
                        new CreatePrescriptionRequest(
                            IssueImmediately: true,
                            Items: [new CreatePrescriptionItemRequest(MedicineId: fx.MedicineId, PrescribedQuantity: 5, SingleDoseQuantity: 1, SingleDoseUnit: "viên", Frequency: "1 lần/ngày", DurationDays: 5, AdministrationRoute: "Uống", Instructions: "Trước ăn")])));
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(fx.Doctor1UserId, visit.VisitCode!, new CompleteVisitRequest(ConfirmNoPrescription: true)));

                // RV-T06, RV-T07, RV-T08: Revalidate labOrder (even with TransitionToAwaitingResults: true!)
                // -> labOrder extended, imgOrder untouched, CLINICAL_REVIEW session STAYS IN_PROGRESS!
                var afterReval1 = await consultSvc.RevalidateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    labOrderId.ToString(),
                    new RevalidateDiagnosticOrderRequest(
                        Reason: "Gia hạn xét nghiệm máu thêm 7 ngày",
                        ExtendDays: 7,
                        TransitionToAwaitingResults: true,
                        ExpectedStateVersion: reviewStateVer));
                Assert.Equal("IN_PROGRESS", afterReval1.WorkspaceState);
                Assert.Equal("IN_PROGRESS", afterReval1.Queue!.Status);

                // RV-T09: CompleteClinicalReview while imgOrder is still expired -> 409 [UNRESOLVED_CLINICAL_REVIEW_ITEMS]
                var rv09Ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteClinicalReviewAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CompleteClinicalReviewRequest(ExpectedStateVersion: reviewStateVer)));
                Assert.Contains("UNRESOLVED_CLINICAL_REVIEW_ITEMS", rv09Ex.Message, StringComparison.Ordinal);

                // RV-T14: Revalidate auxReviewReqOrder -> SelectionStatus MUST remain REVIEW_REQUIRED and STILL block CompleteClinicalReview!
                await consultSvc.RevalidateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    imgOrderId.ToString(),
                    new RevalidateDiagnosticOrderRequest(Reason: "Gia hạn siêu âm thêm 7 ngày", ExtendDays: 7));
                await consultSvc.RevalidateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    reviewReqId.ToString(),
                    new RevalidateDiagnosticOrderRequest(Reason: "Gia hạn thời hạn nhưng chưa xử lý REVIEW_REQUIRED", ExtendDays: 7));

                Db.ChangeTracker.Clear();
                var rvrAfterReval = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == reviewReqId);
                Assert.Equal(DiagnosticSelectionStatuses.ReviewRequired, rvrAfterReval.SelectionStatus);

                var rv14BlockEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteClinicalReviewAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CompleteClinicalReviewRequest(ExpectedStateVersion: reviewStateVer)));
                Assert.Contains("UNRESOLVED_CLINICAL_REVIEW_ITEMS", rv14BlockEx.Message, StringComparison.Ordinal);

                // Clinically cancel auxReviewReqOrder -> now all expired & REVIEW_REQUIRED items are resolved!
                await consultSvc.CancelDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    reviewReqCode,
                    new CancelConsultationDiagnosticOrderRequest(Reason: "Hủy lâm sàng chỉ định tùy chọn lỗi kỹ thuật"));

                // RV-T10: CompleteClinicalReview closes ticket, sets Visit = AWAITING_RESULTS, creates ZERO new queue entries!
                var queueCountBeforeFinish = await Db.QueueEntries.AsNoTracking().CountAsync(q => q.VisitId == visit.Id);
                var finishedReview = await consultSvc.CompleteClinicalReviewAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CompleteClinicalReviewRequest(
                        ExpectedStateVersion: reviewStateVer,
                        ClinicalNote: "Hoàn tất đánh giá lại toàn bộ chỉ định quá hạn"));
                Assert.Equal("AWAITING_RESULTS", finishedReview.Visit.Status);
                Assert.Equal(queueCountBeforeFinish, await Db.QueueEntries.AsNoTracking().CountAsync(q => q.VisitId == visit.Id));
            }

            // RV-T16: auxCompletedUc42Order (COMPLETED at UC42 on Day D, 12 days ago, RequiresResultVerification = true)
            //         accepts UC43 result entry & verification without needing revalidation!
            await using (var rv16Scope = _app.Services.CreateAsyncScope())
            {
                var resultSvc = rv16Scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                var pendingRes = await resultSvc.EnterDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    auxCompletedUc42Order.OrderCode!,
                    new EnterDiagnosticResultRequest(
                        ResultText: "CRP 2.1 mg/L",
                        Conclusion: "CRP bình thường",
                        Status: "PENDING_VERIFY",
                        IsFinalized: false));
                Assert.False(pendingRes.IsFinalized);

                var verifiedRes = await resultSvc.VerifyDiagnosticResultAsync(
                    fx.DiagStaff2UserId,
                    auxCompletedUc42Order.OrderCode!,
                    new VerifyDiagnosticResultRequest(VerificationNote: "Thẩm định độc lập kết quả CRP hợp lệ"));
                Assert.True(verifiedRes.IsFinalized);
            }

            // RV-T20: Resume & execute revalidated paid orders without re-charging!
            var chargeCountBeforeCls = await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == visit.Id);
            var paymentCountBeforeCls = await Db.Payments.AsNoTracking().CountAsync(p => p.VisitId == visit.Id);

            await PostResumeAsync(
                fx,
                visit.VisitCode!,
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderIds: [labOrderId, imgOrderId]));
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, imgOrderCode);
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "CTM bình thường.", "Bình thường.");
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, imgOrderCode, "Siêu âm bình thường.", "Bình thường.");

            Assert.Equal(chargeCountBeforeCls, await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == visit.Id));
            Assert.Equal(paymentCountBeforeCls, await Db.Payments.AsNoTracking().CountAsync(p => p.VisitId == visit.Id));

            // RV-T11: Add an optional order that expires -> open CLINICAL_REVIEW -> cancel expired optional order
            //         when all mandatory results are ALREADY finalized -> CompleteClinicalReview sets Visit = RETURN_CONSULTATION
            //         with ReadyForReturnAt != null and ZERO new queue tickets!
            var lateOptOrder = new DiagnosticOrder
            {
                VisitId = visit.Id,
                OrderingDoctorId = fx.Doctor1Id,
                ServiceId = fx.LabServiceId,
                OrderCode = $"DIA-RV11-{fx.Suffix}",
                Status = "ORDERED",
                SelectionStatus = DiagnosticSelectionStatuses.Undecided,
                RequiredForConclusion = false,
                OrderedAt = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified),
            };
            Db.DiagnosticOrders.Add(lateOptOrder);
            await Db.Visits.Where(v => v.Id == visit.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, "AWAITING_RESULTS"));
            await Db.SaveChangesAsync();

            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Unspecified));
            await PostResumeAsync(
                fx,
                visit.VisitCode!,
                new ResumeCrossDayVisitRequestDto(
                    Target: "RETURN_CONSULTATION",
                    CoordinationPurpose: QueueCoordinationPurposes.ClinicalReview));
            await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi vào khám RV-T11" },
                fx.Reception1UserId,
                RoleNames.Reception));

            await using (var rv11Scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = rv11Scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var ws11 = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.CancelDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    lateOptOrder.OrderCode!,
                    new CancelConsultationDiagnosticOrderRequest(Reason: "Không cần làm thêm chỉ định tùy chọn quá hạn"));

                var qCountBefore11 = await Db.QueueEntries.AsNoTracking().CountAsync(q => q.VisitId == visit.Id);
                var done11 = await consultSvc.CompleteClinicalReviewAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CompleteClinicalReviewRequest(ExpectedStateVersion: ws11.Queue!.StateVersion));
                Assert.Equal("RETURN_CONSULTATION", done11.Visit.Status);
                Assert.NotNull(done11.Visit.ReadyForReturnAt);
                Assert.Equal(qCountBefore11, await Db.QueueEntries.AsNoTracking().CountAsync(q => q.VisitId == visit.Id));
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 4. E1-03: SN-01..SN-14 OVERDUE RETURN SNAPSHOT V2 & SN-14 PRESERVATION
    // =========================================================================

    [Fact]
    public async Task Sn01_To_Sn14_OverdueSnapshotV2_Validation_DraftToIssuedRxGate_InlineApproval_AndReadyForReturnPreservation()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "PR857SN", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "XN sinh hóa kiểm thử SN-01..14");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);
            await FinalizeDiagnosticResultAsync(
                fx.DiagStaff1UserId,
                labOrderCode,
                "Creatinin 82 umol/L.",
                "Chức năng thận bình thường.",
                reportFileUrl: "/storage/diagnostic-reports/crea-v1.pdf");

            Db.ChangeTracker.Clear();
            var originalReadyForReturnAt = (await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).ReadyForReturnAt;
            Assert.NotNull(originalReadyForReturnAt);

            // Advance 10 days to D+10 (2026-11-20 > 7-day return_consultation_window_days)
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Unspecified));

            await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"));
            await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi vào khám kết luận quá hạn SN-01..14" },
                fx.Reception1UserId,
                RoleNames.Reception));

            string snap1;
            await using (var snScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = snScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var ws = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                var diagResults = await consultSvc.GetDiagnosticResultsAsync(fx.Doctor1UserId, visit.VisitCode!);

                // SN-01: Workspace & DiagnosticResults return identical valid 64-hex CurrentResultSnapshot
                snap1 = ws.Visit.CurrentResultSnapshot!;
                Assert.True(OverdueReturnSnapshotHelper.IsValidSnapshotHex(snap1));
                Assert.Equal(snap1, diagResults.CurrentResultSnapshot);

                // SN-02: Missing / malformed ExpectedResultSnapshot -> 400 ValidationException
                await Assert.ThrowsAsync<ValidationException>(() =>
                    consultSvc.ApproveOverdueReturnResultsAsync(
                        fx.Doctor1UserId, visit.VisitCode!,
                        new ApproveOverdueReturnResultsRequest(Reason: "Nhận định hợp lệ", ExpectedResultSnapshot: null)));
                await Assert.ThrowsAsync<ValidationException>(() =>
                    consultSvc.ApproveOverdueReturnResultsAsync(
                        fx.Doctor1UserId, visit.VisitCode!,
                        new ApproveOverdueReturnResultsRequest(Reason: "Nhận định hợp lệ", ExpectedResultSnapshot: "not-64-hex")));

                // SN-03: Valid 64-hex but wrong digest -> 409 [OVERDUE_RESULT_SNAPSHOT_MISMATCH]
                var wrongHex = new string('f', 64);
                var sn03Ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.ApproveOverdueReturnResultsAsync(
                        fx.Doctor1UserId, visit.VisitCode!,
                        new ApproveOverdueReturnResultsRequest(Reason: "Nhận định hợp lệ", ExpectedResultSnapshot: wrongHex)));
                Assert.Contains("OVERDUE_RESULT_SNAPSHOT_MISMATCH", sn03Ex.Message, StringComparison.Ordinal);

                // SN-05: Unassigned Doctor2 -> 403 Forbidden
                await Assert.ThrowsAsync<ForbiddenException>(() =>
                    consultSvc.ApproveOverdueReturnResultsAsync(
                        fx.Doctor2UserId, visit.VisitCode!,
                        new ApproveOverdueReturnResultsRequest(Reason: "BS khác thử xác nhận", ExpectedResultSnapshot: snap1)));

                // SN-06 & SN-07: Provisional Dx + DRAFT Rx allowed; issuing that DRAFT Rx (IssueImmediately: true) is blocked 409!
                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId, visit.VisitCode!,
                    new RecordDiagnosisRequest(PrimaryIcd10Code: fx.Icd10Code, ClinicalConclusion: "Theo dõi sơ bộ", IsConfirmed: false));

                var draftRxWs = await consultSvc.CreatePrescriptionAsync(
                    fx.Doctor1UserId, visit.VisitCode!,
                    new CreatePrescriptionRequest(
                        Status: "DRAFT",
                        IssueImmediately: false,
                        Items: [new CreatePrescriptionItemRequest(MedicineId: fx.MedicineId, PrescribedQuantity: 7, SingleDoseQuantity: 1, SingleDoseUnit: "viên", Frequency: "1 lần/ngày", DurationDays: 7, AdministrationRoute: "Uống", Instructions: "Trước ăn sáng")]));
                Assert.Equal("DRAFT", draftRxWs.ClinicalContext.PrescriptionSummary.LatestPrescriptionStatus);

                var issueFromDraftBlocked = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CreatePrescriptionAsync(
                        fx.Doctor1UserId, visit.VisitCode!,
                        new CreatePrescriptionRequest(
                            Status: "ISSUED",
                            IssueImmediately: true,
                            Items: [new CreatePrescriptionItemRequest(MedicineId: fx.MedicineId, PrescribedQuantity: 7, SingleDoseQuantity: 1, SingleDoseUnit: "viên", Frequency: "1 lần/ngày", DurationDays: 7, AdministrationRoute: "Uống", Instructions: "Trước ăn sáng")])));
                Assert.Contains("OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED", issueFromDraftBlocked.Message, StringComparison.Ordinal);

                // Inline approval without ExpectedResultSnapshot -> 400; with stale ExpectedResultSnapshot -> 409 [OVERDUE_RESULT_SNAPSHOT_MISMATCH]
                await Assert.ThrowsAsync<ValidationException>(() =>
                    consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId, visit.VisitCode!,
                        new RecordDiagnosisRequest(
                            PrimaryIcd10Code: fx.Icd10Code,
                            ClinicalConclusion: "Thử inline thiếu snapshot",
                            IsConfirmed: true,
                            OverdueReturnApprovalNote: "Xác nhận inline thiếu mã snapshot")));

                var inlineStaleEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId, visit.VisitCode!,
                        new RecordDiagnosisRequest(
                            PrimaryIcd10Code: fx.Icd10Code,
                            ClinicalConclusion: "Thử inline sai snapshot",
                            IsConfirmed: true,
                            OverdueReturnApprovalNote: "Xác nhận inline sai mã snapshot",
                            ExpectedResultSnapshot: wrongHex)));
                Assert.Contains("OVERDUE_RESULT_SNAPSHOT_MISMATCH", inlineStaleEx.Message, StringComparison.Ordinal);

                // SN-04: Valid snapshot approval -> stamps hash & unlocks CONFIRMED Dx!
                var approvedWs = await consultSvc.ApproveOverdueReturnResultsAsync(
                    fx.Doctor1UserId, visit.VisitCode!,
                    new ApproveOverdueReturnResultsRequest(
                        Reason: "Đối chiếu lâm sàng kết quả chức năng thận còn nguyên giá trị",
                        ExpectedResultSnapshot: snap1));
                Assert.True(approvedWs.Visit.IsOverdueReturnApproved);

                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId, visit.VisitCode!,
                    new RecordDiagnosisRequest(PrimaryIcd10Code: fx.Icd10Code, ClinicalConclusion: "Chẩn đoán xác định", IsConfirmed: true));
            }

            // SN-10: Amending ReportFileUrl only (keeping ResultText & Conclusion same? UpdateDiagnosticResult requires at least 1 change -> change ReportFileUrl!)
            await using (var sn10Scope = _app.Services.CreateAsyncScope())
            {
                var resultSvc = sn10Scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                await resultSvc.UpdateDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    labOrderCode,
                    new UpdateDiagnosticResultRequest(
                        ResultText: "Creatinin 82 umol/L.",
                        Conclusion: "Chức năng thận bình thường.",
                        ReportFileUrl: "/storage/diagnostic-reports/crea-v2-reupload.pdf",
                        CorrectionReason: "Tải lại bản PDF ký số chuẩn"));
            }

            // SN-14: Visit was ALREADY return-overdue (originalReadyForReturnAt = 2026-11-10).
            //        Now on D+10 (2026-11-20), Doctor orders a NEW mandatory Imaging test and completes it 15 minutes later!
            //        ReadyForReturnAt MUST preserve originalReadyForReturnAt (NOT reset to 2026-11-20!), so the Visit
            //        REMAINS return-overdue and requires fresh approval of the combined [Lab + Imaging] snapshot!
            string newImgOrderCode;
            await using (var sn14OrderScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = sn14OrderScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var wsAfterPdfEdit = await consultSvc.GetWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                Assert.False(wsAfterPdfEdit.Visit.IsOverdueReturnApproved); // Invalidated by SN-10 ReportFileUrl edit!

                var createdNewMandatory = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.ImagingServiceId,
                        Instruction: "Siêu âm thận bổ sung trên ca quá hạn SN-14",
                        RequiredForConclusion: true,
                        TransitionToAwaitingResults: true));
                var newImgItem = createdNewMandatory.CreatedDiagnosticOrders![0];
                newImgOrderCode = newImgItem.OrderCode;

                await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, newImgItem.OrderId);
            }

            Db.ChangeTracker.Clear();
            var visitDuringNewMandatory = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.Equal(originalReadyForReturnAt, visitDuringNewMandatory.ReadyForReturnAt);

            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 20, 9, 15, 0, DateTimeKind.Unspecified));
            await ExecuteDiagnosticOrderThroughUc42Async(fx, newImgOrderCode);
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, newImgOrderCode, "Hai thận kích thước bình thường.", "Siêu âm thận bình thường.");

            Db.ChangeTracker.Clear();
            var visitAfterNewMandatoryDone = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.Equal(originalReadyForReturnAt, visitAfterNewMandatoryDone.ReadyForReturnAt);

            // Call next & reopen return consultation on D+10 -> STILL IsReturnOverdue == true!
            await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi lại khám kết luận sau siêu âm bổ sung SN-14" },
                fx.Reception1UserId,
                RoleNames.Reception));

            await using (var sn14FinishScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = sn14FinishScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var wsReopened = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                Assert.True(wsReopened.Visit.IsReturnOverdue);
                Assert.False(wsReopened.Visit.IsOverdueReturnApproved);

                // Reconfirm diagnosis + complete visit using valid inline approval + matching ExpectedResultSnapshot!
                var combinedSnap = wsReopened.Visit.CurrentResultSnapshot!;
                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordDiagnosisRequest(
                        PrimaryIcd10Code: fx.Icd10Code,
                        ClinicalConclusion: "Chức năng thận và siêu âm thận bình thường.",
                        IsConfirmed: true,
                        OverdueReturnApprovalNote: "Xác nhận đồng bộ kết quả xét nghiệm cũ và siêu âm mới đều hợp lệ",
                        ExpectedResultSnapshot: combinedSnap));

                // Issue the existing DRAFT prescription now that snapshot is approved
                var issuedRxWs = await consultSvc.CreatePrescriptionAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreatePrescriptionRequest(
                        Status: "ISSUED",
                        IssueImmediately: true,
                        Items: [new CreatePrescriptionItemRequest(MedicineId: fx.MedicineId, PrescribedQuantity: 7, SingleDoseQuantity: 1, SingleDoseUnit: "viên", Frequency: "1 lần/ngày", DurationDays: 7, AdministrationRoute: "Uống", Instructions: "Trước ăn sáng")]));
                Assert.Equal("ISSUED", issuedRxWs.ClinicalContext.PrescriptionSummary.LatestPrescriptionStatus);

                var completedWs = await consultSvc.CompleteVisitAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CompleteVisitRequest(DischargeInstructions: "Uống thuốc đúng giờ và tái khám theo hẹn."));
                Assert.Equal("COMPLETED", completedWs.Visit.Status);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 5. CR-E1-02, CR-E1-03 & CR-E1-04: CLINICAL REVIEW DETERMINISTIC RACES
    // =========================================================================

    [Fact]
    public async Task CrE1_02_03_04_DeterministicClinicalReviewConcurrencyRaces_ReassignDoctor_AddOrder_AndDoubleCompleteReview()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var connStr = Db.Database.GetConnectionString()!;
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CRE1234", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, _) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "XN máu kiểm thử CR-E1-02..04");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);

            // Advance to D+9 -> order expired -> open CLINICAL_REVIEW session in IN_PROGRESS
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Unspecified));
            await PostResumeAsync(
                fx,
                visit.VisitCode!,
                new ResumeCrossDayVisitRequestDto(
                    Target: "RETURN_CONSULTATION",
                    CoordinationPurpose: QueueCoordinationPurposes.ClinicalReview));
            await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi vào khám CR-E1-02..04" },
                fx.Reception1UserId,
                RoleNames.Reception));

            int stateVer;
            await using (var openScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = openScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var ws = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                stateVer = ws.Queue!.StateVersion;
            }

            // CR-E1-02: Reassign Visit to Doctor2 while Doctor1's RevalidateDiagnosticOrderAsync waits on Visit FOR UPDATE lock -> 403!
            await RunVisitLockMutationRaceAsync(
                connStr,
                visit.Id,
                async (lockConn, tx) =>
                {
                    await using var cmd = new NpgsqlCommand(
                        "UPDATE visits SET assigned_doctor_id = @doc2, examination_room_id = @room2 WHERE id = @vid;",
                        lockConn,
                        tx);
                    cmd.Parameters.AddWithValue("doc2", fx.Doctor2Id);
                    cmd.Parameters.AddWithValue("room2", fx.ExamRoom2Id);
                    cmd.Parameters.AddWithValue("vid", visit.Id);
                    await cmd.ExecuteNonQueryAsync();
                },
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                    await Assert.ThrowsAsync<ForbiddenException>(() =>
                        consultSvc.RevalidateDiagnosticOrderAsync(
                            fx.Doctor1UserId,
                            visit.VisitCode!,
                            labOrderId.ToString(),
                            new RevalidateDiagnosticOrderRequest(Reason: "BS cũ thử gia hạn trong cửa sổ TOCTOU")));
                });

            Db.ChangeTracker.Clear();
            Assert.Null((await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == labOrderId)).ValidityExtendedUntil);

            // Restore AssignedDoctorId = Doctor1Id and revalidate labOrder so session is ready for CR-E1-03 & CR-E1-04
            await Db.Visits.Where(v => v.Id == visit.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.AssignedDoctorId, fx.Doctor1Id)
                    .SetProperty(v => v.ExaminationRoomId, fx.ExamRoom1Id));

            await using (var revalScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = revalScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.RevalidateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    labOrderId.ToString(),
                    new RevalidateDiagnosticOrderRequest(Reason: "BS Một gia hạn hợp lệ trước khi test CR-E1-03", ExtendDays: 7));
            }

            // CR-E1-03: While CompleteClinicalReviewAsync waits on Visit FOR UPDATE lock, a new unresolved REVIEW_REQUIRED
            //           order is committed -> CompleteClinicalReviewAsync wakes up post-lock and fails 409 [UNRESOLVED_CLINICAL_REVIEW_ITEMS]!
            long injectedOrderId = 0;
            await RunVisitLockMutationRaceAsync(
                connStr,
                visit.Id,
                async (lockConn, tx) =>
                {
                    await using var cmd = new NpgsqlCommand(
                        """
                        INSERT INTO diagnostic_orders (visit_id, ordering_doctor_id, service_id, order_code, status, selection_status, required_for_conclusion, ordered_at)
                        VALUES (@vid, @docId, @svcId, @code, 'ORDERED', 'REVIEW_REQUIRED', false, '2026-11-10 09:00:00')
                        RETURNING id;
                        """,
                        lockConn,
                        tx);
                    cmd.Parameters.AddWithValue("vid", visit.Id);
                    cmd.Parameters.AddWithValue("docId", fx.Doctor1Id);
                    cmd.Parameters.AddWithValue("svcId", fx.ImagingServiceId);
                    cmd.Parameters.AddWithValue("code", $"DIA-CR03-{fx.Suffix}");
                    injectedOrderId = (long)(await cmd.ExecuteScalarAsync())!;
                },
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                    var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                        consultSvc.CompleteClinicalReviewAsync(
                            fx.Doctor1UserId,
                            visit.VisitCode!,
                            new CompleteClinicalReviewRequest(ExpectedStateVersion: stateVer)));
                    Assert.Contains("UNRESOLVED_CLINICAL_REVIEW_ITEMS", ex.Message, StringComparison.Ordinal);
                });

            await Db.DiagnosticOrders.Where(o => o.Id == injectedOrderId).ExecuteDeleteAsync();

            // CR-E1-04: Simultaneous double-submit of CompleteClinicalReviewAsync with identical ExpectedStateVersion
            //           -> 1 succeeds (200), 1 fails 409, StateVersion increments once, single audit row!
            var readyCount = 0;
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<Exception?> AttemptCompleteReviewAsync()
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                if (Interlocked.Increment(ref readyCount) == 2)
                {
                    startGate.TrySetResult();
                }

                await startGate.Task.WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    await consultSvc.CompleteClinicalReviewAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CompleteClinicalReviewRequest(ExpectedStateVersion: stateVer, ClinicalNote: "Đua đóng phiên CLINICAL_REVIEW"));
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var outcomes = await Task.WhenAll(Task.Run(AttemptCompleteReviewAsync), Task.Run(AttemptCompleteReviewAsync));
            Assert.Single(outcomes, o => o is null);
            var conflict = Assert.Single(outcomes, o => o is not null);
            Assert.IsType<ConflictException>(conflict);

            Db.ChangeTracker.Clear();
            var closedEntry = await Db.QueueEntries.AsNoTracking()
                .Where(q => q.VisitId == visit.Id && q.CoordinationPurpose == QueueCoordinationPurposes.ClinicalReview)
                .SingleAsync();
            Assert.Equal("COMPLETED", closedEntry.Status);
            Assert.Equal(stateVer + 1, closedEntry.StateVersion);
            Assert.Equal(
                1,
                await Db.AuditLogs.AsNoTracking().CountAsync(a =>
                    a.HospitalId == fx.HospitalId &&
                    a.Action == "CLINICAL_REVIEW_COMPLETED"));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 6. CR-E1-05..CR-E1-08: OVERDUE SNAPSHOT DETERMINISTIC CONCURRENCY RACES
    // =========================================================================

    [Fact]
    public async Task CrE1_05_06_07_08_DeterministicOverdueSnapshotConcurrencyRaces_Amend_Verify_CompleteVisit_AndDoctorReassign()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var connStr = Db.Database.GetConnectionString()!;
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CRE5678", examPrice: 150_000m, payExamNow: true);

            long labOrderId;
            string labOrderCode;
            long verifyOptOrderId;
            string verifyOptOrderCode;
            await using (var initScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = initScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(ClinicalFindings: "Khám ổn định.", TreatmentPlan: "Kiểm thử CR-E1-05..08."));
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        TransitionToAwaitingResults: true,
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "XN bắt buộc CR-E1-05", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.VerifyLabServiceId, Instruction: "XN 2 bước tùy chọn CR-E1-06", RequiredForConclusion: false, PatientAccepted: false),
                        ]));
                labOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                labOrderCode = created.CreatedDiagnosticOrders[0].OrderCode;
                verifyOptOrderId = created.CreatedDiagnosticOrders[1].OrderId;
                verifyOptOrderCode = created.CreatedDiagnosticOrders[1].OrderCode;
            }

            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, verifyOptOrderId);

            // Execute mandatory Lab and finalize v1; execute optional 2-step VerifyLab and enter PENDING_VERIFY v1
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "AST 22 U/L.", "Men gan bình thường v1.");

            await ExecuteDiagnosticOrderThroughUc42Async(fx, verifyOptOrderCode);
            await using (var pvScope = _app.Services.CreateAsyncScope())
            {
                var resultSvc = pvScope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                // Keep SelectionStatus = UNDECIDED so Visit remains ready for return while optional result is PENDING_VERIFY!
                await Db.DiagnosticOrders.Where(o => o.Id == verifyOptOrderId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.SelectionStatus, DiagnosticSelectionStatuses.Undecided));
                await resultSvc.EnterDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    verifyOptOrderCode,
                    new EnterDiagnosticResultRequest(
                        ResultText: "Troponin I 4.0 ng/L",
                        Conclusion: "Chờ thẩm định bước 2",
                        Status: "PENDING_VERIFY",
                        IsFinalized: false));
            }

            // Advance 10 days to D+10 (2026-11-20 -> overdue return consultation)
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Unspecified));
            await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"));
            await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi vào khám CR-E1-05..08" },
                fx.Reception1UserId,
                RoleNames.Reception));

            string snapInitial;
            await using (var openScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = openScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var ws = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordDiagnosisRequest(PrimaryIcd10Code: fx.Icd10Code, ClinicalConclusion: "Chẩn đoán sơ bộ", IsConfirmed: false));
                snapInitial = ws.Visit.CurrentResultSnapshot!;
            }

            Db.ChangeTracker.Clear();
            var labResultRow = await Db.DiagnosticResults.AsNoTracking().SingleAsync(r => r.DiagnosticOrderId == labOrderId);
            var optResultRow = await Db.DiagnosticResults.AsNoTracking().SingleAsync(r => r.DiagnosticOrderId == verifyOptOrderId);

            // CR-E1-05: CLS amends labResult (v2) under Visit lock while ApproveOverdueReturnResultsAsync(snapInitial) waits -> 409 [OVERDUE_RESULT_SNAPSHOT_MISMATCH]!
            await RunVisitLockMutationRaceAsync(
                connStr,
                visit.Id,
                async (lockConn, tx) =>
                {
                    await using var cmd = new NpgsqlCommand(
                        """
                        INSERT INTO diagnostic_result_versions (diagnostic_result_id, version_no, result_text, conclusion, entered_by_staff_id, entered_at, is_finalized, verified_by_staff_id, verified_at, correction_reason)
                        VALUES (@rid, 2, 'AST 68 U/L (tăng nhẹ).', 'Tăng men gan nhẹ sau hiệu chuẩn.', @staffId, '2026-11-20 09:01:00', true, @staffId, '2026-11-20 09:01:00', 'Hiệu chuẩn máy');
                        UPDATE visits SET overdue_return_result_snapshot = NULL WHERE id = @vid;
                        """,
                        lockConn,
                        tx);
                    cmd.Parameters.AddWithValue("rid", labResultRow.Id);
                    cmd.Parameters.AddWithValue("staffId", fx.Diag1StaffId);
                    cmd.Parameters.AddWithValue("vid", visit.Id);
                    await cmd.ExecuteNonQueryAsync();
                },
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                    var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                        consultSvc.ApproveOverdueReturnResultsAsync(
                            fx.Doctor1UserId,
                            visit.VisitCode!,
                            new ApproveOverdueReturnResultsRequest(
                                Reason: "BS thử xác nhận bản v1 cũ",
                                ExpectedResultSnapshot: snapInitial)));
                    Assert.Contains("OVERDUE_RESULT_SNAPSHOT_MISMATCH", ex.Message, StringComparison.Ordinal);
                });

            Db.ChangeTracker.Clear();
            Assert.Null((await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).OverdueReturnApprovedAt);

            // CR-E1-06: Staff2 verifies PENDING_VERIFY -> VERIFIED on optResultRow while ApproveOverdueReturnResultsAsync(snapAfterAmend) waits -> 409 [OVERDUE_RESULT_SNAPSHOT_MISMATCH]!
            string snapPreVerify;
            await using (var s2Scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = s2Scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                snapPreVerify = (await consultSvc.GetWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!)).Visit.CurrentResultSnapshot!;
            }

            await RunVisitLockMutationRaceAsync(
                connStr,
                visit.Id,
                async (lockConn, tx) =>
                {
                    await using var cmd = new NpgsqlCommand(
                        """
                        UPDATE diagnostic_results SET status = 'VERIFIED', finalized_at = '2026-11-20 09:02:00', finalized_by_staff_id = @staff2Id WHERE id = @rid;
                        INSERT INTO diagnostic_result_versions (diagnostic_result_id, version_no, result_text, conclusion, entered_by_staff_id, entered_at, is_finalized, verified_by_staff_id, verified_at, correction_reason)
                        VALUES (@rid, 2, 'Troponin I 4.0 ng/L', 'Âm tính đã thẩm định 2 bước', @staff1Id, '2026-11-10 09:20:00', true, @staff2Id, '2026-11-20 09:02:00', 'Thẩm định bước 2');
                        UPDATE diagnostic_orders SET status = 'COMPLETED' WHERE id = @oid;
                        UPDATE visits SET overdue_return_result_snapshot = NULL WHERE id = @vid;
                        """,
                        lockConn,
                        tx);
                    cmd.Parameters.AddWithValue("rid", optResultRow.Id);
                    cmd.Parameters.AddWithValue("oid", verifyOptOrderId);
                    cmd.Parameters.AddWithValue("staff1Id", fx.Diag1StaffId);
                    cmd.Parameters.AddWithValue("staff2Id", fx.Diag2StaffId);
                    cmd.Parameters.AddWithValue("vid", visit.Id);
                    await cmd.ExecuteNonQueryAsync();
                },
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                    var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                        consultSvc.ApproveOverdueReturnResultsAsync(
                            fx.Doctor1UserId,
                            visit.VisitCode!,
                            new ApproveOverdueReturnResultsRequest(
                                Reason: "BS thử xác nhận khi kết quả đang chuyển VERIFIED",
                                ExpectedResultSnapshot: snapPreVerify)));
                    Assert.Contains("OVERDUE_RESULT_SNAPSHOT_MISMATCH", ex.Message, StringComparison.Ordinal);
                });

            // Approve current post-verify snapshot & confirm diagnosis -> ready to test CR-E1-07 (CompleteVisit vs concurrent result amend)!
            string snapVerified;
            await using (var approveScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = approveScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                snapVerified = (await consultSvc.GetWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!)).Visit.CurrentResultSnapshot!;
                await consultSvc.ApproveOverdueReturnResultsAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new ApproveOverdueReturnResultsRequest(
                        Reason: "BS xác nhận bộ kết quả sau thẩm định",
                        ExpectedResultSnapshot: snapVerified));
                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordDiagnosisRequest(
                        PrimaryIcd10Code: fx.Icd10Code,
                        ClinicalConclusion: "Tăng men gan nhẹ lành tính.",
                        IsConfirmed: true));
            }

            // CR-E1-07: CompleteVisitAsync waits on Visit FOR UPDATE lock while CLS appends v3 result -> CompleteVisit fails 409, EMR stays DRAFT!
            await RunVisitLockMutationRaceAsync(
                connStr,
                visit.Id,
                async (lockConn, tx) =>
                {
                    await using var cmd = new NpgsqlCommand(
                        """
                        INSERT INTO diagnostic_result_versions (diagnostic_result_id, version_no, result_text, conclusion, entered_by_staff_id, entered_at, is_finalized, verified_by_staff_id, verified_at, correction_reason)
                        VALUES (@rid, 3, 'AST 145 U/L (đính chính khẩn).', 'Tăng men gan rõ rệt.', @staffId, '2026-11-20 09:05:00', true, @staffId, '2026-11-20 09:05:00', 'Đính chính khẩn cấp');
                        UPDATE visits SET overdue_return_result_snapshot = NULL WHERE id = @vid;
                        """,
                        lockConn,
                        tx);
                    cmd.Parameters.AddWithValue("rid", labResultRow.Id);
                    cmd.Parameters.AddWithValue("staffId", fx.Diag1StaffId);
                    cmd.Parameters.AddWithValue("vid", visit.Id);
                    await cmd.ExecuteNonQueryAsync();
                },
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                    var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                        consultSvc.CompleteVisitAsync(
                            fx.Doctor1UserId,
                            visit.VisitCode!,
                            new CompleteVisitRequest(
                                ConfirmNoPrescription: true,
                                DischargeInstructions: "Thử đóng ca khi CLS vừa sửa kết quả khẩn")));
                    Assert.Contains("OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED", ex.Message, StringComparison.Ordinal);
                });

            Db.ChangeTracker.Clear();
            Assert.NotEqual("COMPLETED", (await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).Status);
            Assert.Equal("DRAFT", (await Db.MedicalRecords.AsNoTracking().SingleAsync(m => m.VisitId == visit.Id)).Status);

            // CR-E1-08: Reassign Visit to Doctor2 while Doctor1's ApproveOverdueReturnResultsAsync waits on Visit FOR UPDATE lock -> 403!
            string snapV3;
            await using (var v3Scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = v3Scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                snapV3 = (await consultSvc.GetWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!)).Visit.CurrentResultSnapshot!;
            }

            await Db.Visits.Where(v => v.Id == visit.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.OverdueReturnApprovedByDoctorId, (long?)null)
                    .SetProperty(v => v.OverdueReturnApprovedAt, (DateTime?)null)
                    .SetProperty(v => v.OverdueReturnResultSnapshot, (string?)null));

            await RunVisitLockMutationRaceAsync(
                connStr,
                visit.Id,
                async (lockConn, tx) =>
                {
                    await using var cmd = new NpgsqlCommand(
                        "UPDATE visits SET assigned_doctor_id = @doc2, examination_room_id = @room2 WHERE id = @vid;",
                        lockConn,
                        tx);
                    cmd.Parameters.AddWithValue("doc2", fx.Doctor2Id);
                    cmd.Parameters.AddWithValue("room2", fx.ExamRoom2Id);
                    cmd.Parameters.AddWithValue("vid", visit.Id);
                    await cmd.ExecuteNonQueryAsync();
                },
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                    await Assert.ThrowsAsync<ForbiddenException>(() =>
                        consultSvc.ApproveOverdueReturnResultsAsync(
                            fx.Doctor1UserId,
                            visit.VisitCode!,
                            new ApproveOverdueReturnResultsRequest(
                                Reason: "BS cũ thử xác nhận sau khi đã chuyển sang BS Hai",
                                ExpectedResultSnapshot: snapV3)));
                });

            Db.ChangeTracker.Clear();
            Assert.Null((await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).OverdueReturnApprovedByDoctorId);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // DETERMINISTIC POSTGRESQL LOCK BARRIER HELPERS (ZERO Thread.Sleep)
    // =========================================================================

    private static async Task RunRoomLockDeactivationRaceAsync(
        string connectionString,
        long roomId,
        long spId,
        Func<Task> contenderAction)
    {
        var unpooledConnStr = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        await using var lockConn = new NpgsqlConnection(unpooledConnStr);
        await lockConn.OpenAsync();
        await using var tx = await lockConn.BeginTransactionAsync();

        int holderPid;
        await using (var pidCmd = new NpgsqlCommand("SELECT pg_backend_pid();", lockConn, tx))
        {
            holderPid = (int)(await pidCmd.ExecuteScalarAsync())!;
        }

        await using (var lockCmd = new NpgsqlCommand("SELECT id FROM hospital_rooms WHERE id = @rid FOR UPDATE;", lockConn, tx))
        {
            lockCmd.Parameters.AddWithValue("rid", roomId);
            await lockCmd.ExecuteScalarAsync();
        }

        var contenderTask = Task.Run(contenderAction);
        await WaitForBlockedBackendAsync(unpooledConnStr, holderPid, contenderTask);

        await using (var deactivateCmd = new NpgsqlCommand(
            """
            SELECT id FROM queue_service_points WHERE id = @spId FOR UPDATE;
            UPDATE hospital_rooms SET status = 'MAINTENANCE' WHERE id = @rid;
            UPDATE queue_service_points SET is_active = false WHERE id = @spId;
            """,
            lockConn,
            tx))
        {
            deactivateCmd.Parameters.AddWithValue("rid", roomId);
            deactivateCmd.Parameters.AddWithValue("spId", spId);
            await deactivateCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        await contenderTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task RunVisitLockMutationRaceAsync(
        string connectionString,
        long visitId,
        Func<NpgsqlConnection, NpgsqlTransaction, Task> whileContenderBlockedMutation,
        Func<Task> contenderAction)
    {
        var unpooledConnStr = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        await using var lockConn = new NpgsqlConnection(unpooledConnStr);
        await lockConn.OpenAsync();
        await using var tx = await lockConn.BeginTransactionAsync();

        int holderPid;
        await using (var pidCmd = new NpgsqlCommand("SELECT pg_backend_pid();", lockConn, tx))
        {
            holderPid = (int)(await pidCmd.ExecuteScalarAsync())!;
        }

        await using (var lockCmd = new NpgsqlCommand("SELECT id FROM visits WHERE id = @vid FOR UPDATE;", lockConn, tx))
        {
            lockCmd.Parameters.AddWithValue("vid", visitId);
            await lockCmd.ExecuteScalarAsync();
        }

        var contenderTask = Task.Run(contenderAction);
        await WaitForBlockedBackendAsync(unpooledConnStr, holderPid, contenderTask);

        await whileContenderBlockedMutation(lockConn, tx);
        await tx.CommitAsync();
        await contenderTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task WaitForBlockedBackendAsync(
        string connectionString,
        int holderPid,
        Task contenderTask)
    {
        var unpooledConnStr = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        await using var obsConn = new NpgsqlConnection(unpooledConnStr);
        await obsConn.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (contenderTask.IsCompleted)
            {
                await contenderTask;
                Assert.Fail("Contender completed before reaching PostgreSQL FOR UPDATE lock barrier.");
            }

            await using var cmd = new NpgsqlCommand(
                """
                SELECT COUNT(*)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND @holderPid = ANY(pg_blocking_pids(pid));
                """,
                obsConn);
            cmd.Parameters.AddWithValue("holderPid", holderPid);
            var blocked = (long)(await cmd.ExecuteScalarAsync())!;
            if (blocked >= 1)
            {
                return;
            }

            await Task.Delay(15);
        }

        Assert.Fail("Timed out waiting for contender to block on PostgreSQL row lock.");
    }

    // =========================================================================
    // ISOLATED MULTI-DEPARTMENT / MULTI-HOSPITAL FIXTURE & HELPERS
    // =========================================================================

    private HttpRequestMessage AuthedPostJson<T>(string url, T body, long userId, string role)
    {
        using var scope = _app.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(new User { Id = userId, Email = $"{role}-{userId}@medflow.test" }, [role]).AccessToken;
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private async Task<ResumeCrossDayVisitResponseDto> PostResumeAsync(
        Pr857Fixture fx,
        string visitCode,
        ResumeCrossDayVisitRequestDto request)
    {
        var res = await _client.SendAsync(AuthedPostJson(
            $"/api/v1/reception/visits/{visitCode}/resume",
            request,
            fx.Reception1UserId,
            RoleNames.Reception));
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<ResumeCrossDayVisitResponseDto>(body, WebJson)!;
    }

    private async Task<(long OrderId, string OrderCode)> OpenExamAndCreateSingleDiagnosticOrderAsync(
        Pr857Fixture fx,
        string visitCode,
        long serviceId,
        bool requiredForConclusion,
        string instruction)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
        await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visitCode);
        await consultSvc.RecordClinicalExaminationAsync(
            fx.Doctor1UserId,
            visitCode,
            new RecordClinicalExaminationRequest(
                ClinicalFindings: "Khám lâm sàng ban đầu ổn định.",
                TreatmentPlan: "Theo dõi cận lâm sàng PR #857."));
        var created = await consultSvc.CreateDiagnosticOrderAsync(
            fx.Doctor1UserId,
            visitCode,
            new CreateDiagnosticOrderRequest(
                ServiceId: serviceId,
                Instruction: instruction,
                RequiredForConclusion: requiredForConclusion,
                TransitionToAwaitingResults: true));
        var item = created.CreatedDiagnosticOrders![0];
        return (item.OrderId, item.OrderCode);
    }

    private async Task<long> PayDiagnosticOrderByCashAsync(Pr857Fixture fx, string visitCode, long orderId)
    {
        Db.ChangeTracker.Clear();
        var charge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderId);
        var cashRes = await _client.SendAsync(AuthedPostJson(
            $"/api/v1/reception/visits/{visitCode}/payments/cash",
            new CollectCashPaymentRequestDto(
                VisitChargeIds: [charge.Id],
                IdempotencyKey: $"cash-pr857-{orderId}-{Guid.NewGuid():N}"),
            fx.Reception1UserId,
            RoleNames.Reception));
        var body = await cashRes.Content.ReadAsStringAsync();
        Assert.True(cashRes.StatusCode == HttpStatusCode.Created, body);
        var dto = JsonSerializer.Deserialize<CollectCashPaymentResponseDto>(body, WebJson)!;
        return dto.PaymentId;
    }

    private async Task ExecuteDiagnosticOrderThroughUc42Async(Pr857Fixture fx, string orderCode)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
        await reviewSvc.ConfirmDiagnosticOrderAsync(
            fx.DiagStaff1UserId,
            orderCode,
            new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true));

        var receiveSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticPatientReceiveService>();
        await receiveSvc.ReceiveDiagnosticPatientAsync(
            fx.DiagStaff1UserId,
            orderCode,
            new ReceiveDiagnosticPatientRequest(IsPatientPresent: true, IsIdentityVerified: true, IsPreparationVerified: true));

        var execSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
        var started = await execSvc.StartServiceExecutionAsync(
            fx.DiagStaff1UserId,
            orderCode,
            new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"));
        Assert.Equal("IN_PROGRESS", started.Order.Status);

        var completed = await execSvc.CompleteServiceExecutionAsync(
            fx.DiagStaff1UserId,
            orderCode,
            new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED"));
        Assert.Equal("COMPLETED", completed.Order.Status);
    }

    private async Task FinalizeDiagnosticResultAsync(
        long diagStaffUserId,
        string orderCode,
        string resultText,
        string conclusion,
        string? reportFileUrl = null)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var resultSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
        var res = await resultSvc.EnterDiagnosticResultAsync(
            diagStaffUserId,
            orderCode,
            new EnterDiagnosticResultRequest(
                ResultText: resultText,
                Conclusion: conclusion,
                ReportFileUrl: reportFileUrl,
                Status: "FINALIZED",
                IsFinalized: true));
        Assert.True(res.IsFinalized);
    }

    private async Task<Visit> CreateCheckedInVisitAsync(
        Pr857Fixture fx,
        string tag,
        decimal examPrice,
        bool payExamNow)
    {
        Db.ChangeTracker.Clear();
        var nowLocal = HospitalClock.GetNow(_clock);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"V857-{tag}-{fx.Suffix}",
            PatientId = fx.PatientId,
            AssignedDoctorId = fx.Doctor1Id,
            ExaminationRoomId = fx.ExamRoom1Id,
            Status = "CHECKED_IN",
            CheckedInAt = nowLocal.AddMinutes(-10),
            LastPresentDate = today,
            PresenceState = VisitPresenceStates.Present,
            ReasonForVisit = "Khám ngoại trú kiểm thử PR #857",
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
            ArrivedAt = nowLocal.AddMinutes(-9),
            ReadyAt = nowLocal.AddMinutes(-8),
            CalledAt = nowLocal.AddMinutes(-3),
            StateVersion = 2,
        });

        var examCharge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, examPrice, nowLocal.AddMinutes(-9));
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
                IdempotencyKey = $"seed-pr857-exam-{visit.Id}-{Guid.NewGuid():N}",
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

    private async Task<Pr857Fixture> CreateIsolatedFixtureAsync()
    {
        Db.ChangeTracker.Clear();
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var seedBaseTime = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified);

        var docRole = await EnsureRoleAsync(RoleNames.Doctor);
        var diagRole = await EnsureRoleAsync(RoleNames.DiagnosticStaff);
        var recRole = await EnsureRoleAsync(RoleNames.Reception);
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
                Code = $"K29.{suffix[..2]}",
                NameVi = "Viêm dạ dày kiểm thử PR #857",
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
            var spec = new Specialty { SpecialtyCode = $"S857{suffix}", Name = $"Nội PR857 {suffix}", IsActive = true };
            Db.Specialties.Add(spec);
            await Db.SaveChangesAsync();
            specialtyId = spec.Id;
        }

        var hospital = new Hospital { HospitalCode = $"H857{suffix}", Name = $"BV PR857 {suffix}", IsActive = true };
        var foreignHospital = new Hospital { HospitalCode = $"FH85{suffix}", Name = $"BV Khác {suffix}", IsActive = true };
        Db.Hospitals.AddRange(hospital, foreignHospital);
        await Db.SaveChangesAsync();

        // Separate Clinical, Laboratory, Imaging, and Foreign departments so DR-02/DR-03/DR-09 test true department boundaries!
        var examDept = new Department { HospitalId = hospital.Id, DepartmentCode = $"EXD{suffix}", Name = $"Khoa Khám {suffix}", IsActive = true };
        var labDept = new Department { HospitalId = hospital.Id, DepartmentCode = $"LBD{suffix}", Name = $"Khoa Xét nghiệm {suffix}", IsActive = true };
        var imgDept = new Department { HospitalId = hospital.Id, DepartmentCode = $"IMD{suffix}", Name = $"Khoa CĐHA {suffix}", IsActive = true };
        var foreignDept = new Department { HospitalId = foreignHospital.Id, DepartmentCode = $"FRD{suffix}", Name = $"Khoa Viện Khác {suffix}", IsActive = true };
        Db.Departments.AddRange(examDept, labDept, imgDept, foreignDept);
        await Db.SaveChangesAsync();

        var examService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = examDept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"EX857{suffix}",
            Name = "Khám Nội PR857",
            ServiceType = "EXAMINATION",
            IsActive = true,
        };
        var labService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = labDept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"LB857{suffix}",
            Name = "Xét nghiệm máu PR857",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var verifyLabService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = labDept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"VL857{suffix}",
            Name = "Xét nghiệm miễn dịch 2 bước PR857",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = true,
            IsActive = true,
        };
        var imagingService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = imgDept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"IM857{suffix}",
            Name = "Siêu âm tổng quát PR857",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        Db.Services.AddRange(examService, labService, verifyLabService, imagingService);
        await Db.SaveChangesAsync();

        Db.ServicePrices.AddRange(
            new ServicePrice { ServiceId = examService.Id, PriceAmount = 150_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = labService.Id, PriceAmount = 120_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = verifyLabService.Id, PriceAmount = 180_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = imagingService.Id, PriceAmount = 240_000m, EffectiveFrom = seedBaseTime });

        var examRoom1 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = examDept.Id, RoomCode = $"R1P{suffix}", Name = "PK 101", RoomType = "EXAMINATION", Capacity = 10, Status = "ACTIVE" };
        var examRoom2 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = examDept.Id, RoomCode = $"R2P{suffix}", Name = "PK 102", RoomType = "EXAMINATION", Capacity = 10, Status = "ACTIVE" };
        var labRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = labDept.Id, RoomCode = $"RLP{suffix}", Name = "PXN 201", RoomType = "DIAGNOSTIC", Capacity = 2, Status = "ACTIVE" };
        var imgRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = imgDept.Id, RoomCode = $"RIP{suffix}", Name = "PSA 202", RoomType = "DIAGNOSTIC", Capacity = 2, Status = "ACTIVE" };
        var foreignRoom = new HospitalRoom { HospitalId = foreignHospital.Id, DepartmentId = foreignDept.Id, RoomCode = $"RFP{suffix}", Name = "PXN Viện Khác", RoomType = "DIAGNOSTIC", Capacity = 2, Status = "ACTIVE" };
        Db.HospitalRooms.AddRange(examRoom1, examRoom2, labRoom, imgRoom, foreignRoom);
        await Db.SaveChangesAsync();

        var doc1User = new User { Email = $"d1-857-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var doc2User = new User { Email = $"d2-857-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var diag1User = new User { Email = $"dg1-857-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var diag2User = new User { Email = $"dg2-857-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var rec1User = new User { Email = $"rc1-857-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var patUser = new User { Email = $"pt1-857-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        Db.Users.AddRange(doc1User, doc2User, diag1User, diag2User, rec1User, patUser);
        await Db.SaveChangesAsync();

        Db.UserRoles.AddRange(
            new UserRole { UserId = doc1User.Id, RoleId = docRole.Id },
            new UserRole { UserId = doc2User.Id, RoleId = docRole.Id },
            new UserRole { UserId = diag1User.Id, RoleId = diagRole.Id },
            new UserRole { UserId = diag2User.Id, RoleId = diagRole.Id },
            new UserRole { UserId = rec1User.Id, RoleId = recRole.Id },
            new UserRole { UserId = patUser.Id, RoleId = patRole.Id });

        var doc1Staff = new Staff { UserId = doc1User.Id, HospitalId = hospital.Id, DepartmentId = examDept.Id, StaffCode = $"SD1857{suffix}", FullName = "BS. Một PR857", Status = "ACTIVE" };
        var doc2Staff = new Staff { UserId = doc2User.Id, HospitalId = hospital.Id, DepartmentId = examDept.Id, StaffCode = $"SD2857{suffix}", FullName = "BS. Hai PR857", Status = "ACTIVE" };
        var diag1Staff = new Staff { UserId = diag1User.Id, HospitalId = hospital.Id, DepartmentId = labDept.Id, StaffCode = $"SG1857{suffix}", FullName = "KTV. Một PR857", Status = "ACTIVE" };
        var diag2Staff = new Staff { UserId = diag2User.Id, HospitalId = hospital.Id, DepartmentId = labDept.Id, StaffCode = $"SG2857{suffix}", FullName = "KTV. Hai PR857", Status = "ACTIVE" };
        var rec1Staff = new Staff { UserId = rec1User.Id, HospitalId = hospital.Id, DepartmentId = examDept.Id, StaffCode = $"SRC857{suffix}", FullName = "TN. Tiếp Đón PR857", Status = "ACTIVE" };
        Db.Staff.AddRange(doc1Staff, doc2Staff, diag1Staff, diag2Staff, rec1Staff);
        await Db.SaveChangesAsync();

        var doc1 = new Doctor { StaffId = doc1Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialtyId, DoctorCode = $"DC1857{suffix}", ProfessionalTitle = "BS.CKI", IsActive = true };
        var doc2 = new Doctor { StaffId = doc2Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialtyId, DoctorCode = $"DC2857{suffix}", ProfessionalTitle = "BS.CKI", IsActive = true };
        Db.Doctors.AddRange(doc1, doc2);
        await Db.SaveChangesAsync();

        foreach (var dId in new[] { doc1.Id, doc2.Id })
        {
            foreach (var sId in new[] { examService.Id, labService.Id, verifyLabService.Id, imagingService.Id })
            {
                Db.DoctorServiceAuthorizations.Add(new DoctorServiceAuthorization { DoctorId = dId, ServiceId = sId, IsActive = true });
            }
        }

        var examSp1 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom1.Id, DoctorId = doc1.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "PK 101", IsActive = true };
        var examSp2 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom2.Id, DoctorId = doc2.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "PK 102", IsActive = true };
        var labSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = labRoom.Id, ServiceId = labService.Id, PointType = "DIAGNOSTIC", Name = "PXN 201", IsActive = true };
        var verifyLabSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = labRoom.Id, ServiceId = verifyLabService.Id, PointType = "DIAGNOSTIC", Name = "PXN Miễn dịch 201B", IsActive = true };
        var imgSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = imgRoom.Id, ServiceId = imagingService.Id, PointType = "DIAGNOSTIC", Name = "PSA 202", IsActive = true };
        var foreignSp = new QueueServicePoint { HospitalId = foreignHospital.Id, RoomId = foreignRoom.Id, ServiceId = null, PointType = "DIAGNOSTIC", Name = "PXN Viện Khác", IsActive = true };
        Db.QueueServicePoints.AddRange(examSp1, examSp2, labSp, verifyLabSp, imgSp, foreignSp);

        var medicine = new Medicine
        {
            HospitalId = hospital.Id,
            MedicineCode = $"M857{suffix}",
            Name = $"Esomeprazol 40mg PR857 {suffix}",
            ActiveIngredient = "Esomeprazol",
            Strength = "40mg",
            DosageForm = "Viên nang",
            DispensingUnit = "viên",
            ContentQuantity = 40m,
            ContentUnit = "mg",
            IsActive = true,
        };
        Db.Medicines.Add(medicine);
        await Db.SaveChangesAsync();

        Db.MedicineBatches.Add(new MedicineBatch
        {
            MedicineId = medicine.Id,
            BatchNumber = $"LOT857-{suffix}",
            ReceivedAt = seedBaseTime,
            ExpiryDate = new DateOnly(2028, 12, 31),
            QuantityOnHand = 500m,
            IsBlocked = false,
        });

        var patient = new Patient
        {
            UserId = patUser.Id,
            PatientCode = $"P857{suffix}",
            FullName = "Trần Thị Kiểm Thử PR857",
            Gender = "FEMALE",
            Phone = "0908111222",
            IdentificationHash = $"pr857-id-{suffix}",
            Status = "ACTIVE",
        };
        Db.Patients.Add(patient);
        await Db.SaveChangesAsync();

        return new Pr857Fixture(
            Suffix: suffix,
            HospitalId: hospital.Id,
            ForeignHospitalId: foreignHospital.Id,
            AllDeptIds: [examDept.Id, labDept.Id, imgDept.Id, foreignDept.Id],
            ExamRoom1Id: examRoom1.Id,
            ExamRoom2Id: examRoom2.Id,
            LabRoomId: labRoom.Id,
            ImagingRoomId: imgRoom.Id,
            AllRoomIds: [examRoom1.Id, examRoom2.Id, labRoom.Id, imgRoom.Id, foreignRoom.Id],
            ExamSp1Id: examSp1.Id,
            ExamSp2Id: examSp2.Id,
            LabSpId: labSp.Id,
            VerifyLabSpId: verifyLabSp.Id,
            ImagingSpId: imgSp.Id,
            ForeignDiagSpId: foreignSp.Id,
            AllSpIds: [examSp1.Id, examSp2.Id, labSp.Id, verifyLabSp.Id, imgSp.Id, foreignSp.Id],
            ExamServiceId: examService.Id,
            LabServiceId: labService.Id,
            VerifyLabServiceId: verifyLabService.Id,
            ImagingServiceId: imagingService.Id,
            AllServiceIds: [examService.Id, labService.Id, verifyLabService.Id, imagingService.Id],
            MedicineId: medicine.Id,
            Doctor1UserId: doc1User.Id,
            Doctor1Id: doc1.Id,
            Doctor2UserId: doc2User.Id,
            Doctor2Id: doc2.Id,
            DiagStaff1UserId: diag1User.Id,
            Diag1StaffId: diag1Staff.Id,
            DiagStaff2UserId: diag2User.Id,
            Diag2StaffId: diag2Staff.Id,
            Reception1UserId: rec1User.Id,
            Reception1StaffId: rec1Staff.Id,
            PatientUserId: patUser.Id,
            PatientId: patient.Id,
            AllUserIds: [doc1User.Id, doc2User.Id, diag1User.Id, diag2User.Id, rec1User.Id, patUser.Id],
            AllStaffIds: [doc1Staff.Id, doc2Staff.Id, diag1Staff.Id, diag2Staff.Id, rec1Staff.Id],
            Icd10Code: icdCode,
            CreatedIcd10Id: createdIcdId);
    }

    private async Task<Role> EnsureRoleAsync(string code)
    {
        var role = await Db.Roles.FirstOrDefaultAsync(r => r.Code == code);
        if (role is not null)
        {
            return role;
        }

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

    private async Task CleanupIsolatedFixtureAsync(Pr857Fixture fx)
    {
        Db.ChangeTracker.Clear();

        var visitIds = await Db.Visits
            .Where(v => v.PatientId == fx.PatientId)
            .Select(v => v.Id)
            .ToListAsync();

        await Db.Notifications.Where(n =>
            (n.VisitId.HasValue && visitIds.Contains(n.VisitId.Value)) ||
            n.PatientId == fx.PatientId).ExecuteDeleteAsync();

        // Stage E2 (migration 027): fk_queue_entries_diagnostic_order_visit + fk_queue_entries_prescription_visit
        // are ON DELETE RESTRICT — clear queue_entries bindings before prescriptions/orders/visits below.
        await Db.QueueEntries.Where(q => q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)).ExecuteDeleteAsync();
        if (visitIds.Count > 0)
        {
            var rxIds = await Db.Prescriptions
                .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
                .Select(p => p.Id)
                .ToListAsync();
            if (rxIds.Count > 0)
            {
                await Db.PrescriptionItems.Where(pi => pi.PrescriptionId.HasValue && rxIds.Contains(pi.PrescriptionId.Value)).ExecuteDeleteAsync();
                await Db.Prescriptions.Where(p => rxIds.Contains(p.Id)).ExecuteDeleteAsync();
            }
        }

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
                await Db.PaymentAllocations.Where(pa => paymentIds.Contains(pa.PaymentId)).ExecuteDeleteAsync();
                await Db.Payments.Where(p => paymentIds.Contains(p.Id)).ExecuteDeleteAsync();
            }

            await Db.VisitCharges.Where(c => c.VisitId.HasValue && visitIds.Contains(c.VisitId.Value)).ExecuteDeleteAsync();
            await Db.DiagnosticOrders.Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
        }

        var allHospitalSpIds = await Db.QueueServicePoints
            .Where(sp => sp.HospitalId == fx.HospitalId || sp.HospitalId == fx.ForeignHospitalId || fx.AllSpIds.Contains(sp.Id))
            .Select(sp => sp.Id)
            .ToListAsync();

        await Db.QueueFairnessStates.Where(f => allHospitalSpIds.Contains(f.ServicePointId)).ExecuteDeleteAsync();
        await Db.QueueEntries.Where(q =>
            (q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)) ||
            (q.ServicePointId.HasValue && allHospitalSpIds.Contains(q.ServicePointId.Value)) ||
            (q.CurrentServicePointId.HasValue && allHospitalSpIds.Contains(q.CurrentServicePointId.Value))).ExecuteDeleteAsync();

        await Db.AuditLogs.Where(a =>
            a.HospitalId == fx.HospitalId ||
            a.HospitalId == fx.ForeignHospitalId ||
            (a.ActorUserId.HasValue && fx.AllUserIds.Contains(a.ActorUserId.Value))).ExecuteDeleteAsync();

        if (visitIds.Count > 0)
        {
            await Db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
        }

        await Db.MedicineBatches.Where(mb => mb.MedicineId == fx.MedicineId).ExecuteDeleteAsync();
        await Db.Medicines.Where(m => m.Id == fx.MedicineId).ExecuteDeleteAsync();
        await Db.QueueServicePoints.Where(sp => allHospitalSpIds.Contains(sp.Id)).ExecuteDeleteAsync();
        await Db.DoctorServiceAuthorizations.Where(a => a.DoctorId == fx.Doctor1Id || a.DoctorId == fx.Doctor2Id).ExecuteDeleteAsync();
        await Db.ServicePrices.Where(sp => sp.ServiceId.HasValue && fx.AllServiceIds.Contains(sp.ServiceId.Value)).ExecuteDeleteAsync();
        await Db.Patients.Where(p => p.Id == fx.PatientId).ExecuteDeleteAsync();
        await Db.Doctors.Where(d => d.Id == fx.Doctor1Id || d.Id == fx.Doctor2Id).ExecuteDeleteAsync();
        await Db.Staff.Where(s => fx.AllStaffIds.Contains(s.Id)).ExecuteDeleteAsync();
        await Db.UserRoles.Where(ur => fx.AllUserIds.Contains(ur.UserId)).ExecuteDeleteAsync();
        await Db.Users.Where(u => fx.AllUserIds.Contains(u.Id)).ExecuteDeleteAsync();
        await Db.HospitalRooms.Where(r => fx.AllRoomIds.Contains(r.Id)).ExecuteDeleteAsync();
        await Db.Services.Where(s => fx.AllServiceIds.Contains(s.Id)).ExecuteDeleteAsync();
        await Db.Departments.Where(d => fx.AllDeptIds.Contains(d.Id)).ExecuteDeleteAsync();
        await Db.Hospitals.Where(h => h.Id == fx.HospitalId || h.Id == fx.ForeignHospitalId).ExecuteDeleteAsync();

        if (fx.CreatedIcd10Id.HasValue)
        {
            await Db.Icd10Codes.Where(c => c.Id == fx.CreatedIcd10Id.Value).ExecuteDeleteAsync();
        }
    }

    private sealed class ControllableVietnamClock : TimeProvider
    {
        private long _utcTicks;

        public ControllableVietnamClock(DateTime initialVietnamLocalTime)
        {
            SetVietnamLocalTime(initialVietnamLocalTime);
        }

        public void SetVietnamLocalTime(DateTime vietnamLocalTime)
        {
            var unspecified = DateTime.SpecifyKind(vietnamLocalTime, DateTimeKind.Unspecified);
            var dto = new DateTimeOffset(unspecified, TimeSpan.FromHours(7));
            Interlocked.Exchange(ref _utcTicks, dto.UtcDateTime.Ticks);
        }

        public override DateTimeOffset GetUtcNow()
            => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    }

    private sealed record Pr857Fixture(
        string Suffix,
        long HospitalId,
        long ForeignHospitalId,
        IReadOnlyList<long> AllDeptIds,
        long ExamRoom1Id,
        long ExamRoom2Id,
        long LabRoomId,
        long ImagingRoomId,
        IReadOnlyList<long> AllRoomIds,
        long ExamSp1Id,
        long ExamSp2Id,
        long LabSpId,
        long VerifyLabSpId,
        long ImagingSpId,
        long ForeignDiagSpId,
        IReadOnlyList<long> AllSpIds,
        long ExamServiceId,
        long LabServiceId,
        long VerifyLabServiceId,
        long ImagingServiceId,
        IReadOnlyList<long> AllServiceIds,
        long MedicineId,
        long Doctor1UserId,
        long Doctor1Id,
        long Doctor2UserId,
        long Doctor2Id,
        long DiagStaff1UserId,
        long Diag1StaffId,
        long DiagStaff2UserId,
        long Diag2StaffId,
        long Reception1UserId,
        long Reception1StaffId,
        long PatientUserId,
        long PatientId,
        IReadOnlyList<long> AllUserIds,
        IReadOnlyList<long> AllStaffIds,
        string Icd10Code,
        long? CreatedIcd10Id);
}
