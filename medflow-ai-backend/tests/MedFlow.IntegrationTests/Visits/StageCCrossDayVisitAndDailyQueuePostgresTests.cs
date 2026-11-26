using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.DTOs.DoctorDashboard;
using MedFlow.Application.DTOs.HospitalAdmin;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.Infrastructure.BackgroundJobs;
using MedFlow.Infrastructure.Persistence;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace MedFlow.IntegrationTests.Visits;

/// <summary>
/// Stage C (GH-853 / ADR-0060) Real-PostgreSQL Verification Suite.
/// Covers Migration 025 + preflight_025_audit.sql, CD-T01..CD-T24, CD-10 mandatory readiness reset,
/// durable daily presence gating, cross-day Resume/Defer/Reassign, dashboard metrics,
/// and all 7 deterministic multi-connection concurrency races (§15.3).
/// </summary>
public sealed class StageCCrossDayVisitAndDailyQueuePostgresTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly ControllableVietnamClock _clock = new(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;

    public StageCCrossDayVisitAndDailyQueuePostgresTests(TestWebAppFactory factory)
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
    }

    // =========================================================================
    // MIGRATION 025 & PREFLIGHT 025 AUDIT:
    // Applies 002..024 on a clean temp DB, runs preflight_025_audit.sql,
    // executes 025 twice (idempotency), re-runs preflight_025_audit.sql,
    // and proves chk_visits_presence_state + ux_queue_entries_idempotency_key.
    // =========================================================================

    [Fact]
    public async Task Migration025_AndPreflight025Audit_IdempotentSchemaAndConstraintsVerified()
    {
        var repoRoot = ResolveRepoRoot();
        var preflightSql = await File.ReadAllTextAsync(Path.Combine(repoRoot, "database", "scripts", "preflight_025_audit.sql"));
        var migration025Sql = await File.ReadAllTextAsync(Path.Combine(repoRoot, "database", "migrations", "025_cross_day_visit_presence_and_resume_tracking.sql"));

        await using var tempDb = await TempStageCDatabase.CreateAsync(ResolveAdminConnectionString());
        await ApplyMigrationsUpTo024Async(tempDb.ConnectionString, repoRoot);

        await AssertPreflight025ZeroViolationsAsync(tempDb.ConnectionString, preflightSql);

        // Run 1 & Run 2 (100% idempotent)
        await ExecuteSqlAsync(tempDb.ConnectionString, migration025Sql);
        await ExecuteSqlAsync(tempDb.ConnectionString, migration025Sql);

        await AssertPreflight025ZeroViolationsAsync(tempDb.ConnectionString, preflightSql);

        // Verify chk_visits_presence_state rejects invalid state (23514)
        var checkEx = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                tempDb.ConnectionString,
                "INSERT INTO visits (visit_code, status, presence_state) VALUES ('VIS-CHK-025', 'CHECKED_IN', 'BOGUS_STATE');"));
        Assert.Equal("23514", checkEx.SqlState);

        // Verify ux_queue_entries_idempotency_key allows multiple NULLs but rejects duplicate non-null key (23505)
        await ExecuteSqlAsync(
            tempDb.ConnectionString,
            """
            INSERT INTO queue_entries (queue_date, queue_number, queue_type, status, idempotency_key)
            VALUES ('2026-11-10', 901, 'DIAGNOSTIC', 'WAITING', NULL),
                   ('2026-11-10', 902, 'DIAGNOSTIC', 'WAITING', NULL),
                   ('2026-11-10', 903, 'DIAGNOSTIC', 'WAITING', 'idem-stage-c-025');
            """);

        var uniqEx = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                tempDb.ConnectionString,
                """
                INSERT INTO queue_entries (queue_date, queue_number, queue_type, status, idempotency_key)
                VALUES ('2026-11-11', 904, 'DIAGNOSTIC', 'WAITING', 'idem-stage-c-025');
                """));
        Assert.Equal("23505", uniqEx.SqlState);
    }

    // =========================================================================
    // CD-T01:
    // Paid CLS order on Day D, patient defers/leaves, returns on Day D+1 ->
    // Reception resumes DIAGNOSTIC: same Visit/Order/Charge/Payment, new QueueDate = D+1,
    // CheckedInAt immutable, zero extra charges, and full UC39->UC42 execution succeeds on D+1.
    // =========================================================================

    [Fact]
    public async Task CdT01_PaidDiagnosticOrderOnDayD_ResumedOnDayDPlus1_NewQueueSameVisitZeroExtraCharge_CheckedInAtImmutable()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var dayD = new DateTime(2026, 11, 10, 9, 15, 0, DateTimeKind.Unspecified);
            _clock.SetVietnamLocalTime(dayD);

            var visit = await CreateCheckedInVisitAsync(fx, "CD01", examPrice: 150_000m, payExamNow: true);
            var originalCheckedInAt = visit.CheckedInAt;

            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm máu lúc sáng đói ngày mai");

            var paidPaymentId = await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            var chargesBeforeCount = await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == visit.Id);
            var dayDQueueEntryId = (await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == labOrderId)).QueueEntryId;

            // Patient defers to D+1 and leaves hospital
            var deferRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/defer",
                new DeferVisitOrOrdersRequestDto(
                    Reason: "Bệnh nhân đã ăn sáng, hẹn nhịn đói quay lại sáng mai lấy máu",
                    DeferredUntilDate: new DateOnly(2026, 11, 11),
                    DiagnosticOrderIds: [labOrderId],
                    PatientLeftHospital: true),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, deferRes.StatusCode);

            // Advance clock to Day D+1
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 8, 20, 0, DateTimeKind.Unspecified));

            // Yesterday's queue entry must NOT carry over onto D+1 active examination queue
            var examBoardDPlus1Res = await _client.SendAsync(AuthedGet(
                "/api/v1/reception/examination-queue?queueDate=2026-11-11",
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, examBoardDPlus1Res.StatusCode);
            var examBoardDPlus1 = await examBoardDPlus1Res.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>(WebJson);
            Assert.NotNull(examBoardDPlus1);
            Assert.DoesNotContain(
                examBoardDPlus1!.ServicePoints.SelectMany(sp => sp.Entries),
                e => e.VisitCode == visit.VisitCode);

            // Inspect resume-context on D+1 before resuming
            var ctxRes = await _client.SendAsync(AuthedGet(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume-context",
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, ctxRes.StatusCode);
            var ctx = await ctxRes.Content.ReadFromJsonAsync<VisitResumeContextResponseDto>(WebJson);
            Assert.NotNull(ctx);
            Assert.False(ctx!.IsPresentToday);
            Assert.Equal(originalCheckedInAt, ctx.CheckedInAt);
            var orderCtx = Assert.Single(ctx.DiagnosticOrders);
            Assert.True(orderCtx.IsPaymentSettled);
            Assert.True(orderCtx.CanResumeToday);
            Assert.Equal(new DateOnly(2026, 11, 11), orderCtx.DeferredUntilDate);

            // Reception resumes DIAGNOSTIC on D+1
            var resumeRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderId: labOrderId,
                    IdempotencyKey: $"resume-cd01-{visit.Id}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, resumeRes.StatusCode);
            var resumeDto = await resumeRes.Content.ReadFromJsonAsync<ResumeCrossDayVisitResponseDto>(WebJson);
            Assert.NotNull(resumeDto);
            Assert.Equal(visit.Id, resumeDto!.VisitId);
            Assert.Equal(originalCheckedInAt, resumeDto.CheckedInAt);
            Assert.Equal(VisitPresenceStates.Present, resumeDto.PresenceState);
            Assert.Equal(new DateOnly(2026, 11, 11), resumeDto.LastPresentDate);
            var newDiagQueue = Assert.Single(resumeDto.ResumedQueues);
            Assert.Equal(new DateOnly(2026, 11, 11), newDiagQueue.QueueDate);
            Assert.Equal("DIAGNOSTIC", newDiagQueue.QueueType);
            Assert.Equal("WAITING", newDiagQueue.Status);
            Assert.NotEqual(dayDQueueEntryId, newDiagQueue.QueueEntryId);

            // Zero extra charges or payments created
            Db.ChangeTracker.Clear();
            Assert.Equal(chargesBeforeCount, await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == visit.Id));
            Assert.Equal(2, await Db.Payments.AsNoTracking().CountAsync(p => p.VisitId == visit.Id));
            Assert.Equal(PaymentStatuses.Paid, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == paidPaymentId)).Status);

            var reloadedOrder = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == labOrderId);
            Assert.Equal(newDiagQueue.QueueEntryId, reloadedOrder.QueueEntryId);
            Assert.Null(reloadedOrder.DeferredUntilDate);

            // Full diagnostic technical execution (UC39 -> UC40 -> UC42) works seamlessly on D+1
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T02:
    // Unpaid CLS order on Day D -> on Day D+1 resume before payment is blocked 409 [UNPAID_DIAGNOSTIC_FEE];
    // paying on D+1 and resuming succeeds with ZERO duplicate exam or diagnostic charges.
    // =========================================================================

    [Fact]
    public async Task CdT02_UnpaidDiagnosticOrderOnDayD_PaidAndResumedOnDayDPlus1_ZeroDuplicateExamOrDiagnosticCharge()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD02", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm sinh hóa gan thận");

            // Advance to D+1 without paying Lab on Day D
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Unspecified));

            var unpaidResumeRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: labOrderId),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, unpaidResumeRes.StatusCode);
            Assert.Contains("UNPAID_DIAGNOSTIC_FEE", await unpaidResumeRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // Pay existing diagnostic charge on D+1
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);

            // Still strictly 1 EXAMINATION charge + 1 DIAGNOSTIC charge
            Db.ChangeTracker.Clear();
            var allCharges = await Db.VisitCharges.AsNoTracking().Where(c => c.VisitId == visit.Id).ToListAsync();
            Assert.Equal(2, allCharges.Count);
            Assert.Single(allCharges, c => c.ChargeType == "EXAMINATION");
            Assert.Single(allCharges, c => c.ChargeType == "DIAGNOSTIC" && c.DiagnosticOrderId == labOrderId);

            var resumeRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: labOrderId),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, resumeRes.StatusCode);

            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T03:
    // Deferring diagnostic queue across days marks QueueEntry = DEFERRED + LEFT while preserving
    // open Visit, open ACCEPTED DiagnosticOrder (with DeferredUntilDate), ACTIVE charge, and PAID allocation.
    // =========================================================================

    [Fact]
    public async Task CdT03_DeferDiagnosticQueueAcrossDays_MarksQueueDeferred_PreservesOpenVisitOrderAndPaidSettlement()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD03", examPrice: 150_000m, payExamNow: true);
            var (imgOrderId, _) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.ImagingServiceId,
                requiredForConclusion: false,
                instruction: "Siêu âm ổ bụng tổng quát");

            // Patient accepts optional order & pays
            var selectRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/diagnostic-orders/selection",
                new UpdateDiagnosticOrderSelectionRequestDto(AcceptedOrderIds: [imgOrderId]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, selectRes.StatusCode);
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, imgOrderId);

            var deferredTargetDate = new DateOnly(2026, 11, 12);
            var deferRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/visits/{visit.VisitCode}/defer",
                new DeferVisitOrOrdersRequestDto(
                    Reason: "Bệnh nhân bận công tác đột xuất chiều nay, hẹn ngày kia quay lại siêu âm",
                    DeferredUntilDate: deferredTargetDate,
                    DiagnosticOrderIds: [imgOrderId],
                    PatientLeftHospital: true),
                fx.DiagStaff1UserId,
                RoleNames.DiagnosticStaff));
            Assert.Equal(HttpStatusCode.OK, deferRes.StatusCode);
            var deferDto = await deferRes.Content.ReadFromJsonAsync<DeferVisitOrOrdersResponseDto>(WebJson);
            Assert.NotNull(deferDto);
            Assert.Equal(VisitPresenceStates.Deferred, deferDto!.PresenceState);
            Assert.Contains(imgOrderId, deferDto.UpdatedDiagnosticOrderIds);

            Db.ChangeTracker.Clear();
            var reloadedVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.Contains(reloadedVisit.Status, new[] { "AWAITING_RESULTS", "AWAITING_DIAGNOSTIC" });
            Assert.Equal(VisitPresenceStates.Deferred, reloadedVisit.PresenceState);

            var reloadedOrder = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == imgOrderId);
            Assert.Equal("ORDERED", reloadedOrder.Status);
            Assert.Equal(DiagnosticSelectionStatuses.Accepted, reloadedOrder.SelectionStatus);
            Assert.Equal(deferredTargetDate, reloadedOrder.DeferredUntilDate);

            var reloadedQueue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == reloadedOrder.QueueEntryId);
            Assert.Equal("DEFERRED", reloadedQueue.Status);
            Assert.Equal("LEFT", reloadedQueue.PresenceStatus);
            Assert.NotNull(reloadedQueue.CompletedAt);

            var charge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == imgOrderId);
            Assert.Equal(VisitChargeStatuses.Active, charge.Status);
            var alloc = await Db.PaymentAllocations.AsNoTracking().SingleAsync(pa => pa.VisitChargeId == charge.Id);
            Assert.Equal(PaymentAllocationStatuses.Paid, alloc.Status);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T04:
    // UC42 completed on Day D, UC43 finalized on Day D+1 -> EMR remains DRAFT across overnight wait
    // and only locks to FINALIZED when Doctor completes Visit.
    // =========================================================================

    [Fact]
    public async Task CdT04_Uc42CompletedOnDayD_Uc43FinalizedOnDayDPlus1_EmrRemainsDraftUntilDoctorCompletesVisit()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 11, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD04", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Nuôi cấy vi khuẩn định danh 24h");

            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);

            // Advance to D+1 while specimen finishes lab analysis
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 10, 30, 0, DateTimeKind.Unspecified));

            Db.ChangeTracker.Clear();
            var emrBeforeUc43 = await Db.MedicalRecords.AsNoTracking().SingleAsync(mr => mr.VisitId == visit.Id);
            Assert.Equal("DRAFT", emrBeforeUc43.Status);

            await FinalizeDiagnosticResultAsync(
                fx.DiagStaff1UserId,
                labOrderCode,
                resultText: "Âm tính với vi khuẩn hiếu khí gây bệnh sau 24 giờ nuôi cấy.",
                conclusion: "Không phát hiện vi khuẩn gây bệnh.");

            Db.ChangeTracker.Clear();
            var emrAfterUc43 = await Db.MedicalRecords.AsNoTracking().SingleAsync(mr => mr.VisitId == visit.Id);
            Assert.Equal("DRAFT", emrAfterUc43.Status);

            // Reception resumes RETURN_CONSULTATION on D+1, calls next, and Doctor completes Visit -> EMR FINALIZED
            var resumeRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, resumeRes.StatusCode);

            await CallNextAndCompleteVisitAsync(fx, visit.VisitCode!, fx.ExamSp1Id, fx.Doctor1UserId);

            Db.ChangeTracker.Clear();
            var emrFinal = await Db.MedicalRecords.AsNoTracking().SingleAsync(mr => mr.VisitId == visit.Id);
            Assert.Equal("FINALIZED", emrFinal.Status);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T05 & CD-T06:
    // Results finalized while patient is absent -> ReadyForReturnAt != null, Zero-PHI VISIT_RETURN_READY
    // notification created, ZERO active callable RETURN_CONSULTATION queue (CallNext cannot call absent patient);
    // Patient returns on D+2 -> Reception Resume creates RETURN_CONSULTATION (QueueDate = D+2, 0 new exam charge) ->
    // Doctor completes Visit.
    // =========================================================================

    [Fact]
    public async Task CdT05_CdT06_ResultsFinalizedWhilePatientAbsent_StampsReadyForReturnAndZeroPhiNotificationWithoutCallableQueue_ThenResumesOnDayDPlus2AndCompletes()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD0506", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm HbA1c và mỡ máu");

            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);

            // Patient leaves hospital on afternoon of Day D
            var leaveRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/defer",
                new DeferVisitOrOrdersRequestDto(Reason: "Bệnh nhân đã lấy mẫu máu xong, xin về nhà chờ kết quả", PatientLeftHospital: true),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, leaveRes.StatusCode);

            // Day D+1: Lab finalizes UC43 with sensitive PHI values while patient is at home
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 11, 0, 0, DateTimeKind.Unspecified));
            await FinalizeDiagnosticResultAsync(
                fx.DiagStaff1UserId,
                labOrderCode,
                resultText: "HbA1c = 7.9%, LDL-C = 4.6 mmol/L, Glucose = 9.4 mmol/L",
                conclusion: "Đái tháo đường típ 2 chưa kiểm soát tốt đường huyết");

            // CD-T05 Assertions: ReadyForReturnAt stamped, Visit stays AWAITING_RESULTS, ZERO active RETURN_CONSULTATION queue
            Db.ChangeTracker.Clear();
            var visitOnD1 = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.NotNull(visitOnD1.ReadyForReturnAt);
            Assert.Equal("AWAITING_RESULTS", visitOnD1.Status);

            var activeReturnQueuesD1 = await Db.QueueEntries.AsNoTracking()
                .Where(q => q.VisitId == visit.Id && q.QueueType == "RETURN_CONSULTATION" && q.CompletedAt == null)
                .ToListAsync();
            Assert.Empty(activeReturnQueuesD1);

            // CallNext on ExamSp1 MUST NOT call the absent patient
            var callNextAbsentRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Thử gọi bệnh nhân khi chưa có mặt" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callNextAbsentRes.StatusCode);
            var callNextAbsentDto = await callNextAbsentRes.Content.ReadFromJsonAsync<CallNextPatientResponseDto>(WebJson);
            Assert.NotNull(callNextAbsentDto);
            Assert.Null(callNextAbsentDto!.CalledEntry);

            // Patient portal surfaces ReadyForReturnAt != null & IsPresentToday == false
            var patCharges = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            Assert.NotNull(patCharges.ReadyForReturnAt);
            Assert.False(patCharges.IsPresentToday);

            var patVisitsRes = await _client.SendAsync(AuthedGet(
                "/api/v1/patients/me/visits",
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, patVisitsRes.StatusCode);
            var patVisitsDto = await patVisitsRes.Content.ReadFromJsonAsync<PatientVisitListResponseDto>(WebJson);
            var patVisitItem = Assert.Single(patVisitsDto!.Items, i => i.VisitId == visit.Id);
            Assert.NotNull(patVisitItem.ReadyForReturnAt);
            Assert.False(patVisitItem.IsPresentToday);

            // Zero-PHI Notification verified
            var readyNotifs = await Db.Notifications.AsNoTracking()
                .Where(n => n.VisitId == visit.Id && n.NotificationType == Notification.TypeVisitReturnReady)
                .ToListAsync();
            var notif = Assert.Single(readyNotifs);
            Assert.DoesNotContain("HbA1c", notif.Body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("7.9", notif.Body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Đái tháo đường", notif.Body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HbA1c", notif.Subject ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            // CD-T06: Patient returns to hospital on Day D+2 -> Reception resumes RETURN_CONSULTATION
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 12, 8, 45, 0, DateTimeKind.Unspecified));
            var examChargesBeforeResume = await Db.VisitCharges.AsNoTracking()
                .CountAsync(c => c.VisitId == visit.Id && c.ChargeType == "EXAMINATION");

            var resumeRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, resumeRes.StatusCode);
            var resumeDto = await resumeRes.Content.ReadFromJsonAsync<ResumeCrossDayVisitResponseDto>(WebJson);
            Assert.NotNull(resumeDto);
            Assert.Equal("RETURN_CONSULTATION", resumeDto!.VisitStatus);
            var returnQueue = Assert.Single(resumeDto.ResumedQueues);
            Assert.Equal(new DateOnly(2026, 11, 12), returnQueue.QueueDate);
            Assert.Equal("RETURN_CONSULTATION", returnQueue.QueueType);
            Assert.Equal("READY", returnQueue.Status);

            // Zero duplicate EXAMINATION charge
            Db.ChangeTracker.Clear();
            Assert.Equal(
                examChargesBeforeResume,
                await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == visit.Id && c.ChargeType == "EXAMINATION"));

            await CallNextAndCompleteVisitAsync(fx, visit.VisitCode!, fx.ExamSp1Id, fx.Doctor1UserId);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T07:
    // Results finalized while patient IS durably present on same day ->
    // automatically creates/activates EXACTLY 1 READY RETURN_CONSULTATION QueueEntry today.
    // =========================================================================

    [Fact]
    public async Task CdT07_ResultsFinalizedWhilePatientPresentSameDay_CreatesExactlyOneReadyReturnConsultationQueue()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD07", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Công thức máu khẩn trong buổi sáng");

            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);
            await FinalizeDiagnosticResultAsync(
                fx.DiagStaff1UserId,
                labOrderCode,
                resultText: "Hồng cầu, bạch cầu, tiểu cầu trong giới hạn bình thường.",
                conclusion: "Công thức máu bình thường.");

            Db.ChangeTracker.Clear();
            var reloadedVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.NotNull(reloadedVisit.ReadyForReturnAt);
            Assert.Equal("RETURN_CONSULTATION", reloadedVisit.Status);

            var activeReturnEntries = await Db.QueueEntries.AsNoTracking()
                .Where(q =>
                    q.VisitId == visit.Id &&
                    q.QueueType == "RETURN_CONSULTATION" &&
                    q.QueueDate == new DateOnly(2026, 11, 10) &&
                    q.CompletedAt == null)
                .ToListAsync();
            var singleReturn = Assert.Single(activeReturnEntries);
            Assert.Equal("READY", singleReturn.Status);
            Assert.Equal("PRESENT", singleReturn.PresenceStatus);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T08:
    // Repeated Resume requests (same IdempotencyKey, different IdempotencyKey, and null IdempotencyKey)
    // are 100% idempotent -> zero duplicate QueueEntries, Visits, or VisitCharges.
    // =========================================================================

    [Fact]
    public async Task CdT08_RepeatedResumeRequests_IdempotentZeroDuplicateQueuesChargesOrVisits()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 30, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD08", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm miễn dịch");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);

            // Day D+1: Repeat DIAGNOSTIC resume 3 times (same key, new key, null key)
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 8, 30, 0, DateTimeKind.Unspecified));
            var r1 = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "DIAGNOSTIC",
                DiagnosticOrderId: labOrderId,
                IdempotencyKey: "idem-cd08-diag-1"));
            var r2 = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "DIAGNOSTIC",
                DiagnosticOrderId: labOrderId,
                IdempotencyKey: "idem-cd08-diag-1"));
            var r3 = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "DIAGNOSTIC",
                DiagnosticOrderId: labOrderId,
                IdempotencyKey: null));

            Assert.False(r1.IsIdempotentReplay);
            Assert.True(r2.IsIdempotentReplay);
            Assert.True(r3.IsIdempotentReplay);
            Assert.Equal(r1.ResumedQueues[0].QueueEntryId, r2.ResumedQueues[0].QueueEntryId);
            Assert.Equal(r1.ResumedQueues[0].QueueEntryId, r3.ResumedQueues[0].QueueEntryId);

            Db.ChangeTracker.Clear();
            Assert.Equal(
                1,
                await Db.QueueEntries.AsNoTracking().CountAsync(q =>
                    q.VisitId == visit.Id &&
                    q.QueueType == "DIAGNOSTIC" &&
                    q.QueueDate == new DateOnly(2026, 11, 11) &&
                    q.CompletedAt == null));

            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);

            // Day D+2: Finalize UC43 then repeat RETURN_CONSULTATION resume 3 times
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Unspecified));
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "Chỉ số miễn dịch bình thường.", "Bình thường.");

            var rc1 = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "RETURN_CONSULTATION",
                IdempotencyKey: "idem-cd08-rc-1"));
            var rc2 = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "RETURN_CONSULTATION",
                IdempotencyKey: "idem-cd08-rc-1"));
            var rc3 = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "RETURN_CONSULTATION",
                IdempotencyKey: "idem-cd08-rc-different"));

            Assert.False(rc1.IsIdempotentReplay);
            Assert.True(rc2.IsIdempotentReplay);
            Assert.True(rc3.IsIdempotentReplay);
            Assert.Equal(rc1.ResumedQueues[0].QueueEntryId, rc2.ResumedQueues[0].QueueEntryId);
            Assert.Equal(rc1.ResumedQueues[0].QueueEntryId, rc3.ResumedQueues[0].QueueEntryId);

            Db.ChangeTracker.Clear();
            Assert.Equal(1, await Db.Visits.AsNoTracking().CountAsync(v => v.PatientId == fx.PatientId));
            Assert.Equal(2, await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == visit.Id));
            Assert.Equal(
                1,
                await Db.QueueEntries.AsNoTracking().CountAsync(q =>
                    q.VisitId == visit.Id &&
                    q.QueueType == "RETURN_CONSULTATION" &&
                    q.QueueDate == new DateOnly(2026, 11, 12) &&
                    q.CompletedAt == null));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T09:
    // Original doctor is off-duty/inactive on Day D+2 -> Resume without ReassignDoctorId returns 409;
    // Resume with ReassignDoctorId = Doctor2 reassigns Visit & room, blocks Doctor1 with 403,
    // and Doctor2 reads prior EMR/results and completes Visit.
    // =========================================================================

    [Fact]
    public async Task CdT09_OriginalDoctorOffDutyOnDayDPlus2_ResumeReassignsToDoctor2_BlocksDoctor1With403_Doctor2CompletesVisit()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD09", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm chức năng tuyến giáp");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);

            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 14, 0, 0, DateTimeKind.Unspecified));
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "TSH 2.1 mIU/L, FT4 15 pmol/L", "Tuyến giáp bình thường.");

            // Day D+2: Doctor1 is off-duty
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 12, 8, 30, 0, DateTimeKind.Unspecified));
            await Db.Doctors.Where(d => d.Id == fx.Doctor1Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false));

            // Resume without ReassignDoctorId -> 409 Conflict
            var noReassignRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, noReassignRes.StatusCode);

            // Resume with ReassignDoctorId = Doctor2Id -> 200 OK
            var reassignRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "RETURN_CONSULTATION",
                    ReassignDoctorId: fx.Doctor2Id,
                    Notes: "BS. Một nghỉ phép ngày 12/11, chuyển BS. Hai cùng chuyên khoa đọc kết quả"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, reassignRes.StatusCode);
            var resumed = await reassignRes.Content.ReadFromJsonAsync<ResumeCrossDayVisitResponseDto>(WebJson);
            Assert.NotNull(resumed);
            Assert.Equal(fx.Doctor2Id, resumed!.AssignedDoctorId);
            Assert.Equal(fx.ExamRoom2Id, resumed.ExaminationRoomId);
            Assert.Equal(fx.ExamSp2Id, resumed.ResumedQueues[0].ServicePointId);

            // Re-enable Doctor1 account to verify two-layer ownership check blocks Doctor1 with 403
            await Db.Doctors.Where(d => d.Id == fx.Doctor1Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, true));

            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await Assert.ThrowsAsync<ForbiddenException>(() =>
                    consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new RecordDiagnosisRequest(
                            PrimaryIcd10Code: fx.Icd10Code,
                            ClinicalConclusion: "BS cũ cố ghi sau khi đã chuyển BS mới",
                            IsConfirmed: true)));
            }

            // Reception calls next on ExamSp2 -> Doctor2 completes Visit
            await CallNextAndCompleteVisitAsync(fx, visit.VisitCode!, fx.ExamSp2Id, fx.Doctor2UserId);

            Db.ChangeTracker.Clear();
            Assert.True(await Db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.HospitalId == fx.HospitalId && a.Action == "VISIT_DOCTOR_ASSIGNED"));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T10:
    // Midnight UTC+7 rollover (23:55 D -> 00:05 D+1, while UTC is still 16:55Z -> 17:05Z on Day D!):
    // QueueDate strictly isolates 2026-11-10 vs 2026-11-11, presence resets after midnight rollover,
    // and AppointmentLifecycleProcessor NEVER marks a checked-in Visit/Appointment as NO_SHOW.
    // =========================================================================

    [Fact]
    public async Task CdT10_MidnightUtcPlus7Rollover_2355To0005_PreservesQueueDateIsolationAndNeverMarksCheckedInVisitNoShow()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            // 23:55 Vietnam Local Time on 2026-11-10 (16:55Z UTC)
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 23, 55, 0, DateTimeKind.Unspecified));

            var slot = new ExaminationSlot
            {
                ServiceId = fx.ExamServiceId,
                DoctorId = fx.Doctor1Id,
                RoomId = fx.ExamRoom1Id,
                StartsAt = new DateTime(2026, 11, 10, 23, 30, 0, DateTimeKind.Unspecified),
                EndsAt = new DateTime(2026, 11, 10, 23, 59, 0, DateTimeKind.Unspecified),
                Capacity = 5,
                Status = "OPEN",
            };
            Db.ExaminationSlots.Add(slot);
            await Db.SaveChangesAsync();

            var appt = new Appointment
            {
                AppointmentCode = $"APT-CD10-{fx.Suffix}",
                PatientId = fx.PatientId,
                SlotId = slot.Id,
                Status = "BOOKED",
                ReasonForVisit = "Khám ca tối kiểm tra ranh giới 00:00 UTC+7",
                BookedAt = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Unspecified),
            };
            Db.Appointments.Add(appt);
            await Db.SaveChangesAsync();

            var checkInRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/appointments/{appt.AppointmentCode}/check-in",
                new CheckInScheduledPatientRequest(
                    ReasonForVisit: "Khám ca tối kiểm tra ranh giới 00:00 UTC+7",
                    IsIdentityVerified: true),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.True(
                checkInRes.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                await checkInRes.Content.ReadAsStringAsync());

            Db.ChangeTracker.Clear();
            var visit = await Db.Visits.AsNoTracking().SingleAsync(v => v.AppointmentId == appt.Id);
            Assert.Equal(new DateOnly(2026, 11, 10), visit.LastPresentDate);
            var initialEntry = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.VisitId == visit.Id);
            Assert.Equal(new DateOnly(2026, 11, 10), initialEntry.QueueDate);

            // Pay Exam & order Mandatory Lab at 23:57 on 2026-11-10
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 23, 57, 0, DateTimeKind.Unspecified));
            var examCharge = await Db.VisitCharges.AsNoTracking()
                .SingleAsync(c => c.VisitId == visit.Id && c.ChargeType == "EXAMINATION");
            var cashExamRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [examCharge.Id], IdempotencyKey: $"cd10-ex-{fx.Suffix}"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Created, cashExamRes.StatusCode);

            var callRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi khám lúc 23:57" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callRes.StatusCode);

            var (labOrderId, _) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm qua mốc nửa đêm");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);

            // Roll clock 10 minutes past midnight to 00:05 on 2026-11-11 (still 2026-11-10 17:05Z UTC!)
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 0, 5, 0, DateTimeKind.Unspecified));

            // Run AppointmentLifecycleProcessor -> checked-in Appointment & Visit MUST NEVER become NO_SHOW
            var lifecycleProcessor = _app.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .OfType<AppointmentLifecycleProcessor>()
                .Single();
            await lifecycleProcessor.ProcessCycleAsync();

            Db.ChangeTracker.Clear();
            var apptAfterMidnight = await Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appt.Id);
            var visitAfterMidnight = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.NotEqual("NO_SHOW", apptAfterMidnight.Status);
            Assert.NotEqual("NO_SHOW", visitAfterMidnight.Status);

            // At 00:05 on 2026-11-11, patient has NOT resumed on 2026-11-11 -> IsPresentToday == false
            var ctxRes = await _client.SendAsync(AuthedGet(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume-context",
                fx.Reception1UserId,
                RoleNames.Reception));
            var ctx = await ctxRes.Content.ReadFromJsonAsync<VisitResumeContextResponseDto>(WebJson);
            Assert.False(ctx!.IsPresentToday);

            // Resuming at 00:05 allocates QueueDate = 2026-11-11 (NOT UTC date 2026-11-10!)
            var resumed = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "DIAGNOSTIC",
                DiagnosticOrderId: labOrderId));
            Assert.Equal(new DateOnly(2026, 11, 11), resumed.LastPresentDate);
            Assert.Equal(new DateOnly(2026, 11, 11), resumed.ResumedQueues[0].QueueDate);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T11, CD-T12 & CD-T13 (Updated for Stage E1 / ADR-0061 BUG-04):
    // - CD-T11: Unexecuted order > 7 days flags IsOverdueForReview = true, rejects Resume with
    //   409 [DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED] (even with ConfirmOverdueOverride = true)
    //   without cancelling/refunding, coordinates CLINICAL_REVIEW (0 extra exam charge), and resumes after
    //   Doctor calls POST .../diagnostic-orders/{orderId}/revalidate.
    // - CD-T12: Order that already completed UC42 (Status = COMPLETED) NEVER expires even at D+19 (> 7 days),
    //   and UC43 result entry still succeeds.
    // - CD-T13: Return consultation > 7 days after ReadyForReturnAt flags RETURN_CONSULTATION_OVERDUE_REVIEW,
    //   keeps Visit open, allows Reception RESULT_CONCLUSION enqueue without override, and requires Doctor
    //   POST .../approve-overdue-results before CONFIRMED diagnosis / ISSUED prescription / CompleteVisit.
    // =========================================================================

    [Fact]
    public async Task CdT11_CdT12_CdT13_ValidityAndReturnWindowPolicies_OverdueOrderRequiresOverrideWithoutForfeitingPaid_Uc42CompletedOnDPlus10NeverExpires_OverdueReturnStaysOpen()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD1113", examPrice: 150_000m, payExamNow: true);

            long labOrderId;
            string labOrderCode;
            long imgOrderId;
            string imgOrderCode;
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Đau hạ sườn phải âm ỉ.",
                        TreatmentPlan: "Xét nghiệm máu và siêu âm gan mật."));
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        TransitionToAwaitingResults: true,
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "CLS 1 để quá hạn 9 ngày", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "CLS 2 làm UC42 ngày D, nhập kết quả ngày D+19", RequiredForConclusion: true),
                        ]));
                labOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                labOrderCode = created.CreatedDiagnosticOrders[0].OrderCode;
                imgOrderId = created.CreatedDiagnosticOrders[1].OrderId;
                imgOrderCode = created.CreatedDiagnosticOrders[1].OrderCode;
            }

            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, imgOrderId);

            // Complete UC42 on Imaging on Day D (so it is COMPLETED awaiting UC43)
            await ExecuteDiagnosticOrderThroughUc42Async(fx, imgOrderCode);

            // Advance 9 days to D+9 (2026-11-19 > 7-day diagnostic_order_validity_days)
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Unspecified));

            var ctxD9Res = await _client.SendAsync(AuthedGet(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume-context",
                fx.Reception1UserId,
                RoleNames.Reception));
            var ctxD9 = await ctxD9Res.Content.ReadFromJsonAsync<VisitResumeContextResponseDto>(WebJson);
            Assert.NotNull(ctxD9);
            Assert.True(ctxD9!.IsOverdueForReview);
            Assert.Contains("DIAGNOSTIC_ORDER_OVERDUE_REVIEW", ctxD9.OverdueReviewReasons);

            var labCtx = ctxD9.DiagnosticOrders.Single(o => o.OrderId == labOrderId);
            var imgCtx = ctxD9.DiagnosticOrders.Single(o => o.OrderId == imgOrderId);
            Assert.True(labCtx.IsExpiredForReview);
            Assert.True(labCtx.IsPaymentSettled);
            Assert.Equal("ORDERED", labCtx.OrderStatus); // Never auto-cancelled!
            Assert.False(imgCtx.IsExpiredForReview); // CD-T12: COMPLETED UC42 order never expires!

            // CD-T11 (Stage E1): Resume overdue Lab without OR with Reception ConfirmOverdueOverride -> 409 [DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED]
            var blockedOverdueRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: labOrderId),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, blockedOverdueRes.StatusCode);
            Assert.Contains(
                "DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED",
                await blockedOverdueRes.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            var blockedReceptionOverrideRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderId: labOrderId,
                    ConfirmOverdueOverride: true,
                    Notes: "Lễ tân thử ghi đè quá hạn"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, blockedReceptionOverrideRes.StatusCode);
            Assert.Contains(
                "DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED",
                await blockedReceptionOverrideRes.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            // Reception coordinates same-visit CLINICAL_REVIEW (zero extra examination charge)
            var clinicalReviewResume = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "RETURN_CONSULTATION",
                CoordinationPurpose: QueueCoordinationPurposes.ClinicalReview,
                Notes: "Điều phối gặp bác sĩ đánh giá lại chỉ định quá hạn"));
            Assert.Single(clinicalReviewResume.ResumedQueues);
            Assert.Equal(1, await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == visit.Id && c.ChargeType == "EXAMINATION"));

            var callReviewRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi bệnh nhân vào đánh giá lại chỉ định quá hạn" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callReviewRes.StatusCode);

            await using (var reviewScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = reviewScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
            }

            var revalidateRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/diagnostic-orders/{labOrderId}/revalidate",
                new RevalidateDiagnosticOrderRequest(Reason: "Tình trạng lâm sàng ổn định, gia hạn hiệu lực xét nghiệm máu không thu lại phí"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            var revalidateBody = await revalidateRes.Content.ReadAsStringAsync();
            Assert.True(revalidateRes.StatusCode == HttpStatusCode.OK, revalidateBody);

            var revalidatedResume = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "DIAGNOSTIC",
                DiagnosticOrderId: labOrderId,
                Notes: "Tiếp tục thực hiện CLS sau khi bác sĩ gia hạn hiệu lực"));
            Assert.Single(revalidatedResume.ResumedQueues);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "Men gan AST/ALT bình thường.", "Bình thường.");

            // CD-T12: Advance another 10 days to D+19 (2026-11-29, 19 days after OrderedAt!) -> UC43 on COMPLETED Imaging STILL succeeds!
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 29, 10, 0, 0, DateTimeKind.Unspecified));
            await FinalizeDiagnosticResultAsync(
                fx.DiagStaff1UserId,
                imgOrderCode,
                resultText: "Nhu mô gan đồng nhất, đường mật không giãn.",
                conclusion: "Siêu âm gan mật bình thường.");

            Db.ChangeTracker.Clear();
            var visitAfterUc43 = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.NotNull(visitAfterUc43.ReadyForReturnAt);

            // CD-T13 (Stage E1): Advance another 9 days to D+28 (2026-12-08, > 7 days after ReadyForReturnAt)
            _clock.SetVietnamLocalTime(new DateTime(2026, 12, 8, 9, 0, 0, DateTimeKind.Unspecified));
            var ctxD28Res = await _client.SendAsync(AuthedGet(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume-context",
                fx.Reception1UserId,
                RoleNames.Reception));
            var ctxD28 = await ctxD28Res.Content.ReadFromJsonAsync<VisitResumeContextResponseDto>(WebJson);
            Assert.NotNull(ctxD28);
            Assert.Equal("AWAITING_RESULTS", ctxD28!.VisitStatus); // Never auto-cancelled!
            Assert.True(ctxD28.IsOverdueForReview);
            Assert.Contains("RETURN_CONSULTATION_OVERDUE_REVIEW", ctxD28.OverdueReviewReasons);

            // Reception coordinates RESULT_CONCLUSION without clinical override; Doctor must approve overdue snapshot before conclusion
            await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "RETURN_CONSULTATION",
                Notes: "Tiếp nhận khám kết luận quá hạn để bác sĩ xác nhận giá trị lâm sàng"));

            var callOverdueReturnRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi bệnh nhân khám kết luận quá hạn" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callOverdueReturnRes.StatusCode);

            await using (var overdueScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = overdueScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);

                var blockedDx = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new RecordDiagnosisRequest(
                            PrimaryIcd10Code: fx.Icd10Code,
                            ClinicalConclusion: "Thử xác nhận chẩn đoán khi chưa duyệt kết quả quá hạn",
                            IsConfirmed: true)));
                Assert.Contains("OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED", blockedDx.Message, StringComparison.Ordinal);
            }

            var approveOverdueRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/approve-overdue-results",
                new ApproveOverdueReturnResultsRequest(Reason: "Kết quả xét nghiệm và siêu âm vẫn còn giá trị lâm sàng để kết luận"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            var approveOverdueBody = await approveOverdueRes.Content.ReadAsStringAsync();
            Assert.True(approveOverdueRes.StatusCode == HttpStatusCode.OK, approveOverdueBody);

            await CallNextAndCompleteVisitAsync(fx, visit.VisitCode!, fx.ExamSp1Id, fx.Doctor1UserId);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T14 & CD-T15:
    // - CD-T15: Open prior-day Visit blocks both new Walk-In and Scheduled Check-In with 409 [VISIT_RESUME_REQUIRED].
    // - CD-T14: Once prior Visit is COMPLETED (EMR FINALIZED), follow-up Scheduled Check-In & Walk-In create
    //   an independent new Visit while leaving prior FINALIZED EMR 100% untouched.
    // =========================================================================

    [Fact]
    public async Task CdT14_CdT15_CompletedVisitAllowsNewFollowUpEpisodeWithFinalizedEmrIntact_WhileOpenPriorDayVisitBlocksWalkInAndCheckInWithVisitResumeRequired()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit1 = await CreateCheckedInVisitAsync(fx, "CD1415", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit1.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm đường huyết");
            await PayDiagnosticOrderByCashAsync(fx, visit1.VisitCode!, labOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);

            // Advance to D+1 while Visit 1 is still open
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Unspecified));
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "Glucose 5.1 mmol/L", "Bình thường.");

            // CD-T15A: New Walk-In attempt while Visit 1 is open -> 409 [VISIT_RESUME_REQUIRED]
            var walkInBlockedRes = await _client.SendAsync(AuthedPostJson(
                "/api/v1/reception/visits/walk-in",
                new CreateWalkInVisitRequest(
                    PatientId: fx.PatientId.ToString(),
                    DepartmentId: fx.DeptId.ToString(),
                    ServiceId: fx.ExamServiceId.ToString(),
                    DoctorId: fx.Doctor1Id.ToString(),
                    ExaminationRoomId: fx.ExamRoom1Id.ToString(),
                    ReasonForVisit: "Định đăng ký khám mới nhầm khi lượt cũ chưa đóng",
                    PaymentMethod: "CASH",
                    IsIdentityVerified: true),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, walkInBlockedRes.StatusCode);
            var walkInErrBody = await walkInBlockedRes.Content.ReadAsStringAsync();
            Assert.Contains("VISIT_RESUME_REQUIRED", walkInErrBody, StringComparison.Ordinal);
            Assert.Contains(visit1.VisitCode!, walkInErrBody, StringComparison.Ordinal);

            // CD-T15B: Scheduled Check-In attempt on D+1 while Visit 1 is open -> 409 [VISIT_RESUME_REQUIRED]
            var d1Slot = new ExaminationSlot
            {
                ServiceId = fx.ExamServiceId,
                DoctorId = fx.Doctor1Id,
                RoomId = fx.ExamRoom1Id,
                StartsAt = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Unspecified),
                EndsAt = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Unspecified),
                Capacity = 5,
                Status = "OPEN",
            };
            Db.ExaminationSlots.Add(d1Slot);
            await Db.SaveChangesAsync();

            var d1Appt = new Appointment
            {
                AppointmentCode = $"APT-CD15-{fx.Suffix}",
                PatientId = fx.PatientId,
                SlotId = d1Slot.Id,
                Status = "BOOKED",
                ReasonForVisit = "Lịch hẹn trùng khi lượt cũ đang mở",
                BookedAt = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Unspecified),
            };
            Db.Appointments.Add(d1Appt);
            await Db.SaveChangesAsync();

            var checkInBlockedRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/appointments/{d1Appt.AppointmentCode}/check-in",
                new CheckInScheduledPatientRequest(ReasonForVisit: "Check-in khi lượt cũ đang mở", IsIdentityVerified: true),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, checkInBlockedRes.StatusCode);
            Assert.Contains(
                "VISIT_RESUME_REQUIRED",
                await checkInBlockedRes.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            // Complete Visit 1 via Resume on D+1
            await PostResumeAsync(fx, visit1.VisitCode!, new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"));
            await CallNextAndCompleteVisitAsync(fx, visit1.VisitCode!, fx.ExamSp1Id, fx.Doctor1UserId);

            Db.ChangeTracker.Clear();
            var finalizedEmr1 = await Db.MedicalRecords.AsNoTracking().SingleAsync(mr => mr.VisitId == visit1.Id);
            Assert.Equal("FINALIZED", finalizedEmr1.Status);

            // CD-T14: Follow-up episode on D+3 creates a brand-new Visit 2 while keeping Visit 1's FINALIZED EMR untouched
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Unspecified));
            var followUpWalkInRes = await _client.SendAsync(AuthedPostJson(
                "/api/v1/reception/visits/walk-in",
                new CreateWalkInVisitRequest(
                    PatientId: fx.PatientId.ToString(),
                    DepartmentId: fx.DeptId.ToString(),
                    ServiceId: fx.ExamServiceId.ToString(),
                    DoctorId: fx.Doctor1Id.ToString(),
                    ExaminationRoomId: fx.ExamRoom1Id.ToString(),
                    ReasonForVisit: "Tái khám theo dõi đợt điều trị mới độc lập",
                    PaymentMethod: "CASH",
                    IsIdentityVerified: true),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.True(
                followUpWalkInRes.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                await followUpWalkInRes.Content.ReadAsStringAsync());

            Db.ChangeTracker.Clear();
            var patientVisits = await Db.Visits.AsNoTracking()
                .Where(v => v.PatientId == fx.PatientId)
                .OrderBy(v => v.Id)
                .ToListAsync();
            Assert.Equal(2, patientVisits.Count);
            Assert.Equal("COMPLETED", patientVisits[0].Status);
            Assert.NotEqual(patientVisits[0].Id, patientVisits[1].Id);

            var emr1AfterFollowUp = await Db.MedicalRecords.AsNoTracking().SingleAsync(mr => mr.Id == finalizedEmr1.Id);
            Assert.Equal("FINALIZED", emr1AfterFollowUp.Status);
            Assert.Equal(finalizedEmr1.FinalizedAt, emr1AfterFollowUp.FinalizedAt);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T16, CD-T17, CD-T18 & CD-10:
    // - CD-T16: Optional declined + Mandatory pending -> still blocks CompleteVisit (409).
    // - CD-T17 & CD-10: Mandatory finalized + Optional declined -> Visit ready for return;
    //   when Doctor orders a NEW mandatory CLS during return consultation, ReadyForReturnAt is
    //   IMMEDIATELY cleared to null!
    // - CD-T18: TechnicalAbort on mandatory order sets REVIEW_REQUIRED, keeps ReadyForReturnAt == null,
    //   and blocks CompleteVisit until Doctor clinically resolves it; after resolution, CompleteVisit succeeds!
    // =========================================================================

    [Fact]
    public async Task CdT16_CdT17_CdT18_Cd10_MandatoryVsOptionalCompletionGates_TechnicalAbortClearsReadyForReturn_AndNewMandatoryOrderInReturnConsultationResetsReadyForReturn()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD1618", examPrice: 150_000m, payExamNow: true);

            long mandatory1Id;
            string mandatory1Code;
            long optional1Id;
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Đau ngực trái khi gắng sức nhẹ.",
                        TreatmentPlan: "Làm xét nghiệm men tim bắt buộc và siêu âm tùy chọn."));
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        TransitionToAwaitingResults: true,
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "Troponin T bắt buộc", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "Siêu âm bụng tùy chọn", RequiredForConclusion: false),
                        ]));
                mandatory1Id = created.CreatedDiagnosticOrders![0].OrderId;
                mandatory1Code = created.CreatedDiagnosticOrders[0].OrderCode;
                optional1Id = created.CreatedDiagnosticOrders[1].OrderId;
            }

            // Patient declines Optional CLS
            var declineRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/diagnostic-orders/{optional1Id}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Chỉ làm xét nghiệm bắt buộc"),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, declineRes.StatusCode);

            // CD-T16: Mandatory still pending -> CompleteVisit blocked 409
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(fx.Doctor1UserId, visit.VisitCode!));
            }

            // Pay & finalize Mandatory 1 -> Visit becomes RETURN_CONSULTATION with ReadyForReturnAt != null
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, mandatory1Id);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, mandatory1Code);
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, mandatory1Code, "Troponin T: 8.5 ng/L (dưới ngưỡng 14 ng/L)", "Không tăng men tim.");

            Db.ChangeTracker.Clear();
            Assert.NotNull((await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).ReadyForReturnAt);

            // Reception calls next & Doctor reopens consultation workspace
            var callNextRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi khám kết luận" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callNextRes.StatusCode);

            long mandatory2Id;
            string mandatory2Code;
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);

                // CD-10: Doctor orders an ADDITIONAL mandatory CLS -> ReadyForReturnAt MUST be cleared to null!
                var extra = await consultSvc.CreateAdditionalDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateAdditionalDiagnosticOrderRequest(
                        AdditionalOrderReason: "Cần kiểm tra thêm siêu âm tim/mạch bắt buộc trước kết luận",
                        ServiceId: fx.ImagingServiceId,
                        Instruction: "Siêu âm Doppler bắt buộc",
                        RequiredForConclusion: true));
                mandatory2Id = extra.CreatedDiagnosticOrders![^1].OrderId;
                mandatory2Code = extra.CreatedDiagnosticOrders[^1].OrderCode;
            }

            Db.ChangeTracker.Clear();
            Assert.Null((await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).ReadyForReturnAt);

            // CD-T18: Pay Mandatory 2, then KTV performs TechnicalAbort = true -> REVIEW_REQUIRED, ReadyForReturnAt stays null, CompleteVisit blocked!
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, mandatory2Id);
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var aborted = await reviewSvc.CancelDiagnosticOrderAsync(
                    fx.DiagStaff1UserId,
                    mandatory2Code,
                    new CancelDiagnosticOrderRequest(Reason: "Đầu dò máy siêu âm lỗi hiệu chuẩn đột xuất", TechnicalAbort: true));
                Assert.Equal("CANCELLED", aborted.OrderStatus);
            }

            Db.ChangeTracker.Clear();
            Assert.Null((await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).ReadyForReturnAt);
            Assert.Equal(
                DiagnosticSelectionStatuses.ReviewRequired,
                (await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == mandatory2Id)).SelectionStatus);

            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(fx.Doctor1UserId, visit.VisitCode!));

                // Doctor clinically resolves the REVIEW_REQUIRED mandatory order -> unblocks CD-T17 completion!
                await consultSvc.CancelDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    mandatory2Code,
                    new CancelConsultationDiagnosticOrderRequest(
                        Reason: "Lâm sàng ổn định và Troponin T âm tính, không cần lặp lại siêu âm",
                        ReconciliationRequired: true));
            }

            await CallNextAndCompleteVisitAsync(fx, visit.VisitCode!, fx.ExamSp1Id, fx.Doctor1UserId);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T19, CD-T20 & CD-T21:
    // - CD-T21: While CLS A is IN_PROGRESS, starting CLS B or Consultation throws 409 [PATIENT_BUSY_AT_ANOTHER_POINT].
    // - CD-T19: Once CLS A finishes technical execution (COMPLETED awaiting UC43), CLS B can execute in parallel!
    // - CD-T20: Dashboard accurately separates NewCheckInsToday, ResumedVisitsToday,
    //   AwaitingResultsOrReturnCount, and CompletedVisitsToday across HospitalAdmin, Reception, and Doctor views.
    // =========================================================================

    [Fact]
    public async Task CdT19_CdT20_CdT21_ParallelClsExecutionWhileAwaitingOtherResult_DashboardNewVsResumedMetrics_AndSimultaneousInProgressBlocked409()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit1 = await CreateCheckedInVisitAsync(fx, "CD1921A", examPrice: 150_000m, payExamNow: true);

            long clsAId;
            string clsACode;
            long clsBId;
            string clsBCode;
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit1.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit1.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Khám tổng quát.",
                        TreatmentPlan: "Thực hiện xét nghiệm máu và siêu âm."));
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit1.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        TransitionToAwaitingResults: true,
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "CLS A", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "CLS B", RequiredForConclusion: true),
                        ]));
                clsAId = created.CreatedDiagnosticOrders![0].OrderId;
                clsACode = created.CreatedDiagnosticOrders[0].OrderCode;
                clsBId = created.CreatedDiagnosticOrders[1].OrderId;
                clsBCode = created.CreatedDiagnosticOrders[1].OrderCode;
            }

            await PayDiagnosticOrderByCashAsync(fx, visit1.VisitCode!, clsAId);
            await PayDiagnosticOrderByCashAsync(fx, visit1.VisitCode!, clsBId);

            // Confirm & Receive both CLS A and CLS B, then start CLS A -> IN_PROGRESS
            await ConfirmAndReceiveDiagnosticOrderAsync(fx, clsACode);
            await ConfirmAndReceiveDiagnosticOrderAsync(fx, clsBCode);

            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var execSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                var startedA = await execSvc.StartServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    clsACode,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"));
                Assert.Equal("IN_PROGRESS", startedA.Order.Status);

                // CD-T21: Starting CLS B while CLS A is IN_PROGRESS -> 409 [PATIENT_BUSY_AT_ANOTHER_POINT]
                var busyDiagEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    execSvc.StartServiceExecutionAsync(
                        fx.DiagStaff1UserId,
                        clsBCode,
                        new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS")));
                Assert.Contains("PATIENT_BUSY_AT_ANOTHER_POINT", busyDiagEx.Message, StringComparison.Ordinal);

                // Also starting clinical workspace while CLS A is IN_PROGRESS -> 409
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit1.VisitCode!));

                // Complete CLS A technical execution (Status = COMPLETED, awaiting UC43 result)
                await execSvc.CompleteServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    clsACode,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED"));

                // CD-T19: While CLS A is awaiting UC43 result, CLS B executes cleanly!
                var startedB = await execSvc.StartServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    clsBCode,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"));
                Assert.Equal("IN_PROGRESS", startedB.Order.Status);

                await execSvc.CompleteServiceExecutionAsync(
                    fx.DiagStaff1UserId,
                    clsBCode,
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED"));
            }

            // Advance to Day D+1 for CD-T20 dashboard metrics verification
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Unspecified));
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, clsACode, "Kết quả CLS A bình thường.", "Bình thường.");
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, clsBCode, "Kết quả CLS B bình thường.", "Bình thường.");

            // Resume Visit 1 (cross-day resumed visit on D+1) and complete it on D+1
            await PostResumeAsync(fx, visit1.VisitCode!, new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"));
            await CallNextAndCompleteVisitAsync(fx, visit1.VisitCode!, fx.ExamSp1Id, fx.Doctor1UserId);

            // Create a brand-new Visit 2 for OtherPatient on D+1 that enters AWAITING_DIAGNOSTIC / AWAITING_RESULTS
            var visit2 = await CreateCheckedInVisitForPatientAsync(fx, fx.OtherPatientId, "CD20NEW", examPrice: 150_000m, payExamNow: true);
            var (v2LabId, v2LabCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit2.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm lượt mới trong ngày D+1");
            await PayDiagnosticOrderByCashAsync(fx, visit2.VisitCode!, v2LabId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, v2LabCode);

            // CD-T20: Verify HospitalAdminDashboard, ExaminationQueueStats, and DoctorDashboard metrics on D+1
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var hospDash = scope.ServiceProvider.GetRequiredService<IHospitalAdminDashboardService>();
                var hospDashDto = await hospDash.GetDashboardAsync(fx.HospAdminUserId, new GetHospitalAdminDashboardRequest());
                Assert.Equal(1, hospDashDto.Stats.NewCheckInsToday);
                Assert.Equal(1, hospDashDto.Stats.ResumedVisitsToday);
                Assert.Equal(1, hospDashDto.Stats.AwaitingResultsOrReturnCount);
                Assert.Equal(1, hospDashDto.Stats.CompletedVisitsToday);

                var examQueueSvc = scope.ServiceProvider.GetRequiredService<IExaminationQueueService>();
                var queueBoard = await examQueueSvc.GetExaminationQueueAsync(
                    fx.Reception1UserId,
                    new GetExaminationQueueRequest(QueueDate: "2026-11-11"));
                Assert.Equal(1, queueBoard.Stats.NewCheckInsToday);
                Assert.Equal(1, queueBoard.Stats.ResumedVisitsToday);
                Assert.Equal(1, queueBoard.Stats.AwaitingResultsOrReturnCount);
                Assert.Equal(1, queueBoard.Stats.CompletedVisitsToday);

                var docDashSvc = scope.ServiceProvider.GetRequiredService<IDoctorDashboardService>();
                var docDash = await docDashSvc.GetDashboardAsync(fx.Doctor1UserId, new GetDoctorDashboardRequest());
                Assert.Equal(1, docDash.Stats.NewCheckInsToday);
                Assert.Equal(1, docDash.Stats.ResumedVisitsToday);
                Assert.Equal(1, docDash.Stats.AwaitingResultsOrReturnCount);
                Assert.Equal(1, docDash.Stats.CompletedVisitsToday);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CD-T22, CD-T23 & CD-T24:
    // - CD-T23: UC43 allows DRAFT without conclusion, requires conclusion for PENDING_VERIFY,
    //   and enforces Two-Person Independent Verification (entering staff blocked 409 from self-verifying;
    //   second staff verifies and triggers return readiness).
    // - CD-T24: Internal report file blocks unpublished access & cross-patient access with 403,
    //   while allowing owning patient 200 OK once verified.
    // - CD-T22: ADR-0047 preserves null medicine prices (zero fake medicine VisitCharges) and supports
    //   both EXTERNAL_PHARMACY and HOSPITAL_PHARMACY prescriptions cleanly.
    // =========================================================================

    [Fact]
    public async Task CdT22_CdT23_CdT24_Adr0047PrescriptionNullPricesAndExternalPharmacy_TwoPersonIndependentVerification_AndCrossPatientReportFile403()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "CD2224", examPrice: 150_000m, payExamNow: true);

            // Order VerifiedLabService (RequiresResultVerification == true)
            var (verOrderId, verOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.VerifiedLabServiceId,
                requiredForConclusion: true,
                instruction: "Giải phẫu bệnh sinh thiết cần thẩm định 2 người");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, verOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, verOrderCode);

            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var resultSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();

                // CD-T23 Step 1: Enter DRAFT without conclusion -> succeeds, not finalized
                var draftRes = await resultSvc.EnterDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    verOrderCode,
                    new EnterDiagnosticResultRequest(
                        ResultText: "Mô học niêm mạc dạ dày viêm mạn tính nhẹ, không dị sản.",
                        Conclusion: null,
                        Status: "DRAFT",
                        IsFinalized: false));
                Assert.False(draftRes.IsFinalized);

                // CD-T23 Step 2: Submit PENDING_VERIFY with conclusion & internal report path
                var pendingVerifyRes = await resultSvc.EnterDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    verOrderCode,
                    new EnterDiagnosticResultRequest(
                        ResultText: "Mô học niêm mạc dạ dày viêm mạn tính nhẹ, HP âm tính.",
                        Conclusion: "Viêm niêm mạc dạ dày mạn tính không hoạt động.",
                        ReportFileUrl: "/storage/diagnostic-reports/cd23-biopsy-report.pdf",
                        Status: "PENDING_VERIFY",
                        IsFinalized: false));
                Assert.False(pendingVerifyRes.IsFinalized);
            }

            // Unpublished report cannot be downloaded by patient yet (403) & ReadyForReturnAt is still null
            Db.ChangeTracker.Clear();
            Assert.Null((await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).ReadyForReturnAt);

            var unpubDownload = await _client.SendAsync(AuthedGet(
                $"/api/v1/diagnostic/results/{verOrderCode}/report-file",
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Forbidden, unpubDownload.StatusCode);

            // CD-T23 Step 3: Self-verification by DiagStaff1 is blocked with 409 ConflictException
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var resultSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    resultSvc.VerifyDiagnosticResultAsync(
                        fx.DiagStaff1UserId,
                        verOrderCode,
                        new VerifyDiagnosticResultRequest(VerificationNote: "Tự duyệt sai quy định")));

                // Independent DiagStaff2 verifies -> succeeds & finalizes!
                var verified = await resultSvc.VerifyDiagnosticResultAsync(
                    fx.DiagStaff2UserId,
                    verOrderCode,
                    new VerifyDiagnosticResultRequest(VerificationNote: "Đã đối chiếu tiêu bản độc lập chính xác"));
                Assert.True(verified.IsFinalized);
            }

            // CD-T24: Owning Patient1 gets 200 OK PDF; OtherPatient gets 403 Forbidden
            var ownReportRes = await _client.SendAsync(AuthedGet(
                $"/api/v1/diagnostic/results/{verOrderCode}/report-file",
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, ownReportRes.StatusCode);

            var crossPatientReportRes = await _client.SendAsync(AuthedGet(
                $"/api/v1/diagnostic/results/{verOrderCode}/report-file",
                fx.OtherPatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Forbidden, crossPatientReportRes.StatusCode);

            // CD-T22 (ADR-0047): Reopen return consultation, issue EXTERNAL_PHARMACY prescription, complete visit -> zero medicine VisitCharges!
            var callNextRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi khám kết luận và kê đơn mua ngoài" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callNextRes.StatusCode);

            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordDiagnosisRequest(
                        PrimaryIcd10Code: fx.Icd10Code,
                        ClinicalConclusion: "Viêm niêm mạc dạ dày mạn tính ổn định.",
                        IsConfirmed: true));

                await consultSvc.CreatePrescriptionAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreatePrescriptionRequest(
                        FulfillmentChoice: "EXTERNAL_PHARMACY",
                        IssueImmediately: true,
                        Items:
                        [
                            new CreatePrescriptionItemRequest(
                                MedicineId: fx.MedicineId,
                                PrescribedQuantity: 14m,
                                SingleDoseQuantity: 1m,
                                SingleDoseUnit: "viên",
                                Frequency: "2 lần/ngày",
                                DurationDays: 7,
                                AdministrationRoute: "Uống",
                                Instructions: "Uống trước ăn sáng tối 30 phút"),
                        ]));

                var completed = await consultSvc.CompleteVisitAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CompleteVisitRequest(DischargeInstructions: "Mua thuốc theo đơn ngoại trú và tái khám sau 2 tuần."));
                Assert.Equal("COMPLETED", completed.Visit.Status);
            }

            // ADR-0047 invariant: ZERO medicine VisitCharge rows fabricated; patient can read prescription
            Db.ChangeTracker.Clear();
            var visitCharges = await Db.VisitCharges.AsNoTracking().Where(c => c.VisitId == visit.Id).ToListAsync();
            Assert.Equal(2, visitCharges.Count);
            Assert.All(visitCharges, c => Assert.Contains(c.ChargeType, new[] { "EXAMINATION", "DIAGNOSTIC" }));

            var rx = await Db.Prescriptions.AsNoTracking().SingleAsync(p => p.VisitId == visit.Id);
            Assert.Equal("EXTERNAL_PHARMACY", rx.FulfillmentChoice);
            Assert.Equal("ISSUED", rx.Status);

            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var patRxSvc = scope.ServiceProvider.GetRequiredService<IPatientPrescriptionService>();
                var detail = await patRxSvc.GetMyPrescriptionAsync(fx.PatientUserId, rx.Id.ToString());
                Assert.Single(detail.Items);
            }

            // Also verify HOSPITAL_PHARMACY choice on a second visit creates zero fake medicine charges (ADR-0047)
            var visit2 = await CreateCheckedInVisitForPatientAsync(fx, fx.OtherPatientId, "CD22INT", examPrice: 150_000m, payExamNow: true);
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit2.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit2.VisitCode!,
                    new RecordClinicalExaminationRequest(ClinicalFindings: "Đau thượng vị nhẹ.", TreatmentPlan: "Cấp thuốc tại viện."));
                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit2.VisitCode!,
                    new RecordDiagnosisRequest(PrimaryIcd10Code: fx.Icd10Code, ClinicalConclusion: "Khó tiêu chức năng.", IsConfirmed: true));
                await consultSvc.CreatePrescriptionAsync(
                    fx.Doctor1UserId,
                    visit2.VisitCode!,
                    new CreatePrescriptionRequest(
                        FulfillmentChoice: "HOSPITAL_PHARMACY",
                        IssueImmediately: true,
                        Items:
                        [
                            new CreatePrescriptionItemRequest(
                                MedicineId: fx.MedicineId,
                                PrescribedQuantity: 10m,
                                SingleDoseQuantity: 1m,
                                SingleDoseUnit: "viên",
                                Frequency: "2 lần/ngày",
                                DurationDays: 5,
                                AdministrationRoute: "Uống",
                                Instructions: "Uống trước ăn"),
                        ]));
            }

            Db.ChangeTracker.Clear();
            var visit2Charges = await Db.VisitCharges.AsNoTracking().Where(c => c.VisitId == visit2.Id).ToListAsync();
            Assert.Single(visit2Charges);
            Assert.Equal("EXAMINATION", visit2Charges[0].ChargeType);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 01 (§15.3):
    // Defer vs StartDiagnosticExecution -> serializes under visits (09) -> queue_service_points (10) ->
    // queue_entries (11) -> diagnostic_orders (12) locks; exactly ONE succeeds and ONE throws 409!
    // =========================================================================

    [Fact]
    public async Task Race01_DeferVsStartDiagnosticExecution_SerializesUnderVisitLock_WinnerBlocksLoserWith409()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "RC01", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm kiểm tra race Defer vs StartExecution");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await ConfirmAndReceiveDiagnosticOrderAsync(fx, labOrderCode);

            using var ready = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tDefer = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var lifecycleSvc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    await lifecycleSvc.DeferVisitOrOrdersAsync(
                        fx.Reception1UserId,
                        visit.VisitCode!,
                        new DeferVisitOrOrdersRequestDto(
                            Reason: "Bệnh nhân báo tạm hoãn tại quầy",
                            DeferredUntilDate: new DateOnly(2026, 11, 11),
                            DiagnosticOrderIds: [labOrderId],
                            PatientLeftHospital: true));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var tStart = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var execSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    await execSvc.StartServiceExecutionAsync(
                        fx.DiagStaff1UserId,
                        labOrderCode,
                        new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            release.SetResult();

            var deferErr = await tDefer;
            var startErr = await tStart;

            // Strictly mutually exclusive: never both succeed and never both fail!
            Assert.True(
                (deferErr is null) ^ (startErr is null),
                $"Expected mutual exclusion, got deferErr={deferErr?.GetType().Name}:{deferErr?.Message}, startErr={startErr?.GetType().Name}:{startErr?.Message}");

            Db.ChangeTracker.Clear();
            var finalVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            var finalOrder = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == labOrderId);
            var finalQe = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == finalOrder.QueueEntryId);

            if (deferErr is null)
            {
                Assert.IsType<ConflictException>(startErr);
                Assert.Equal(VisitPresenceStates.Deferred, finalVisit.PresenceState);
                Assert.NotEqual("IN_PROGRESS", finalOrder.Status);
                Assert.Equal("DEFERRED", finalQe.Status);
            }
            else
            {
                Assert.IsType<ConflictException>(deferErr);
                Assert.Equal(VisitPresenceStates.Present, finalVisit.PresenceState);
                Assert.Equal("IN_PROGRESS", finalOrder.Status);
                Assert.Equal("IN_PROGRESS", finalQe.Status);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 02 (§15.3):
    // Parallel Resume vs Resume (both keyed & unkeyed) -> creates AT MOST 1 active QueueEntry today!
    // =========================================================================

    [Fact]
    public async Task Race02_ResumeVsResume_KeyedAndUnkeyedParallelRequests_CreatesAtMostOneActiveQueueEntryToday()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "RC02", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm kiểm tra race Resume vs Resume");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);

            // Sub-race 2A: Parallel keyed DIAGNOSTIC resumes on D+1
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 8, 30, 0, DateTimeKind.Unspecified));
            using (var ready = new SemaphoreSlim(0, 2))
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var runKeyed = async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                    ready.Release();
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    return await svc.ResumeVisitAsync(
                        fx.Reception1UserId,
                        visit.VisitCode!,
                        new ResumeCrossDayVisitRequestDto(
                            Target: "DIAGNOSTIC",
                            DiagnosticOrderId: labOrderId,
                            IdempotencyKey: $"race02-diag-{visit.Id}"));
                };

                var t1 = Task.Run(runKeyed);
                var t2 = Task.Run(runKeyed);
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                release.SetResult();

                var res1 = await t1;
                var res2 = await t2;
                Assert.Equal(res1.ResumedQueues[0].QueueEntryId, res2.ResumedQueues[0].QueueEntryId);
            }

            Db.ChangeTracker.Clear();
            Assert.Equal(
                1,
                await Db.QueueEntries.AsNoTracking().CountAsync(q =>
                    q.VisitId == visit.Id &&
                    q.QueueType == "DIAGNOSTIC" &&
                    q.QueueDate == new DateOnly(2026, 11, 11) &&
                    q.CompletedAt == null));

            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);

            // Sub-race 2B: Parallel UNKEYED RETURN_CONSULTATION resumes on D+2
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Unspecified));
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "Kết quả ổn định.", "Bình thường.");

            using (var ready = new SemaphoreSlim(0, 2))
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var runUnkeyed = async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                    ready.Release();
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    return await svc.ResumeVisitAsync(
                        fx.Reception1UserId,
                        visit.VisitCode!,
                        new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION", IdempotencyKey: null));
                };

                var t1 = Task.Run(runUnkeyed);
                var t2 = Task.Run(runUnkeyed);
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                release.SetResult();

                var res1 = await t1;
                var res2 = await t2;
                Assert.Equal(res1.ResumedQueues[0].QueueEntryId, res2.ResumedQueues[0].QueueEntryId);
            }

            Db.ChangeTracker.Clear();
            Assert.Equal(
                1,
                await Db.QueueEntries.AsNoTracking().CountAsync(q =>
                    q.VisitId == visit.Id &&
                    q.QueueType == "RETURN_CONSULTATION" &&
                    q.QueueDate == new DateOnly(2026, 11, 12) &&
                    q.CompletedAt == null));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 03 (§15.3):
    // Resume(RETURN_CONSULTATION) vs Final UC43 Result Entry ->
    // serializes on visits (09) -> queue_service_points (10) -> queue_entries (11);
    // never creates >1 active RETURN_CONSULTATION queue today and never loses ReadyForReturnAt!
    // =========================================================================

    [Fact]
    public async Task Race03_ResumeReturnConsultationVsFinalUc43Result_CreatesAtMostOneActiveReturnQueueTodayAndZeroLostReadiness()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "RC03", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm kiểm tra race Resume vs UC43");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);

            // Day D+1: Race Reception Resume(RETURN_CONSULTATION) vs Lab EnterDiagnosticResultAsync(IsFinalized: true)
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 9, 30, 0, DateTimeKind.Unspecified));

            using var ready = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tResume = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    await svc.ResumeVisitAsync(
                        fx.Reception1UserId,
                        visit.VisitCode!,
                        new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            var tUc43 = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var resultSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await resultSvc.EnterDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    labOrderCode,
                    new EnterDiagnosticResultRequest(
                        ResultText: "Kết quả xét nghiệm hoàn tất đồng thời.",
                        Conclusion: "Các chỉ số trong giới hạn bình thường.",
                        Status: "FINALIZED",
                        IsFinalized: true));
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            release.SetResult();

            var resumeErr = await tResume;
            await tUc43;

            // If Resume acquired lock before UC43 committed, it cleanly rejected 409 (results not ready yet);
            // a follow-up Resume then activates the single RETURN_CONSULTATION queue!
            if (resumeErr is not null)
            {
                Assert.IsType<ConflictException>(resumeErr);
                await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"));
            }

            Db.ChangeTracker.Clear();
            var finalVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.NotNull(finalVisit.ReadyForReturnAt);
            Assert.Equal("RETURN_CONSULTATION", finalVisit.Status);

            var activeReturnQueues = await Db.QueueEntries.AsNoTracking()
                .Where(q =>
                    q.VisitId == visit.Id &&
                    q.QueueType == "RETURN_CONSULTATION" &&
                    q.QueueDate == new DateOnly(2026, 11, 11) &&
                    q.CompletedAt == null)
                .ToListAsync();
            Assert.Single(activeReturnQueues);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 04 (§15.3):
    // Patient Leave/Defer vs CallNext -> serializes cleanly;
    // an absent/deferred patient NEVER remains in CALLED status!
    // =========================================================================

    [Fact]
    public async Task Race04_PatientLeaveDeferVsCallNext_SerializesCleanly_AbsentPatientNeverCalledOrResetToDeferred()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateWaitingExamVisitAsync(fx, "RC04", examPrice: 150_000m);

            using var ready = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tLeave = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var lifecycleSvc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return await lifecycleSvc.DeferVisitOrOrdersAsync(
                    fx.Reception1UserId,
                    visit.VisitCode!,
                    new DeferVisitOrOrdersRequestDto(Reason: "Bệnh nhân báo xin ra ngoài gấp", PatientLeftHospital: true));
            });

            var tCallNext = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var examQueueSvc = scope.ServiceProvider.GetRequiredService<IExaminationQueueService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    await examQueueSvc.CallNextPatientAsync(
                        fx.Reception1UserId,
                        fx.ExamSp1Id.ToString(),
                        new CallNextPatientRequest(Notes: "Gọi tiếp theo đồng thời khi bệnh nhân rời viện"));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            release.SetResult();

            await tLeave;
            var callErr = await tCallNext;

            // Whether Leave ran first (CallNext threw ConflictException: no present candidate)
            // or CallNext ran first (CALLED -> Leave transitioned CALLED to DEFERRED + LEFT),
            // the final persisted state MUST have the patient DEFERRED & LEFT (never stuck CALLED!).
            if (callErr is not null)
            {
                Assert.IsType<ConflictException>(callErr);
            }

            Db.ChangeTracker.Clear();
            var finalVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            var finalQe = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.VisitId == visit.Id);
            Assert.Contains(finalVisit.PresenceState, new[] { VisitPresenceStates.Left, VisitPresenceStates.Deferred });
            Assert.Equal("DEFERRED", finalQe.Status);
            Assert.Equal("LEFT", finalQe.PresenceStatus);
            Assert.Null(finalQe.CalledAt);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 05 (§15.3):
    // Doctor Reassignment vs Original Doctor Clinical Write ->
    // serializes on doctors (05) -> hospital_rooms (06) -> visits (09);
    // once reassigned to Doctor2, Doctor1 is strictly blocked with 403 ForbiddenException!
    // =========================================================================

    [Fact]
    public async Task Race05_DoctorReassignmentVsOriginalDoctorClinicalWrite_OriginalDoctorBlockedWith403AfterReassignment()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "RC05", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm cho race đổi bác sĩ");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);

            // Day D+1: UC43 finished while patient absent; on D+2 Reception reassigns to Doctor2 via Resume while Doctor1 attempts clinical write
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 15, 0, 0, DateTimeKind.Unspecified));
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "Chỉ số ổn định.", "Bình thường.");

            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Unspecified));

            using var ready = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tReassign = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return await svc.ResumeVisitAsync(
                    fx.Reception1UserId,
                    visit.VisitCode!,
                    new ResumeCrossDayVisitRequestDto(
                        Target: "RETURN_CONSULTATION",
                        ReassignDoctorId: fx.Doctor2Id,
                        Notes: "Chuyển BS. Hai tiếp nhận khám kết luận"));
            });

            var tDoc1Write = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    await consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new RecordDiagnosisRequest(
                            PrimaryIcd10Code: fx.Icd10Code,
                            ClinicalConclusion: "BS 1 ghi đồng thời lúc chuyển BS 2",
                            IsConfirmed: true));
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            release.SetResult();

            var resumed = await tReassign;
            var doc1Err = await tDoc1Write;
            Assert.Equal(fx.Doctor2Id, resumed.AssignedDoctorId);
            Assert.NotNull(doc1Err); // Either ForbiddenException (if Reassign won lock) or ConflictException (not IN_PROGRESS before Reassign)

            // Post-reassignment: Doctor1 is deterministically blocked with 403 ForbiddenException!
            await using (var postScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = postScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await Assert.ThrowsAsync<ForbiddenException>(() =>
                    consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!));
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 06 (§15.3):
    // TechnicalAbort on Mandatory Order vs CompleteVisit ->
    // CompleteVisit NEVER finalizes Visit without mandatory clinical resolution!
    // =========================================================================

    [Fact]
    public async Task Race06_TechnicalAbortMandatoryOrderVsCompleteVisit_NeverFinalizesVisitWithoutMandatoryResult()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "RC06", examPrice: 150_000m, payExamNow: true);

            long mandatoryOrderId;
            string mandatoryOrderCode;
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(
                        ClinicalFindings: "Đau ngực cấp.",
                        TreatmentPlan: "Làm xét nghiệm bắt buộc."));
                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordDiagnosisRequest(
                        PrimaryIcd10Code: fx.Icd10Code,
                        ClinicalConclusion: "Chẩn đoán sơ bộ.",
                        IsConfirmed: false));

                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.LabServiceId,
                        Instruction: "Xét nghiệm bắt buộc cho race TechnicalAbort vs CompleteVisit",
                        RequiredForConclusion: true));
                mandatoryOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                mandatoryOrderCode = created.CreatedDiagnosticOrders[0].OrderCode;
            }

            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, mandatoryOrderId);

            using var ready = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tAbort = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return await reviewSvc.CancelDiagnosticOrderAsync(
                    fx.DiagStaff1UserId,
                    mandatoryOrderCode,
                    new CancelDiagnosticOrderRequest(Reason: "Sự cố thiết bị phân tích", TechnicalAbort: true));
            });

            var tComplete = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CompleteVisitRequest(ConfirmNoPrescription: true)));
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            release.SetResult();

            var abortRes = await tAbort;
            var completeEx = await tComplete;
            Assert.Equal("CANCELLED", abortRes.OrderStatus);
            Assert.NotNull(completeEx);

            Db.ChangeTracker.Clear();
            var finalVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            var finalEmr = await Db.MedicalRecords.AsNoTracking().SingleAsync(mr => mr.VisitId == visit.Id);
            Assert.NotEqual("COMPLETED", finalVisit.Status);
            Assert.Null(finalVisit.ReadyForReturnAt);
            Assert.Equal("DRAFT", finalEmr.Status);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // CONCURRENCY RACE 07 (§15.3):
    // New Walk-In vs Resume Open Prior-Day Visit ->
    // serializes cleanly; Walk-In is rejected with 409 [VISIT_RESUME_REQUIRED],
    // Resume succeeds, and ZERO duplicate Visits or Exam Charges are created!
    // =========================================================================

    [Fact]
    public async Task Race07_NewWalkInOrCheckInVsResumeOpenVisit_SerializesUnderPatientLock_ZeroDuplicateOpenVisits()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "RC07", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, _) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm cho race WalkIn vs Resume");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);

            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 8, 30, 0, DateTimeKind.Unspecified));

            using var ready = new SemaphoreSlim(0, 2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tWalkIn = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var walkInSvc = scope.ServiceProvider.GetRequiredService<ICreateWalkInVisitService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return await Assert.ThrowsAsync<ConflictException>(() =>
                    walkInSvc.CreateWalkInVisitAsync(
                        fx.Reception1UserId,
                        new CreateWalkInVisitRequest(
                            PatientId: fx.PatientId.ToString(),
                            DepartmentId: fx.DeptId.ToString(),
                            ServiceId: fx.ExamServiceId.ToString(),
                            DoctorId: fx.Doctor1Id.ToString(),
                            ExaminationRoomId: fx.ExamRoom1Id.ToString(),
                            ReasonForVisit: "Quầy bên cạnh bấm tạo mới cùng lúc",
                            PaymentMethod: "CASH",
                            IsIdentityVerified: true)));
            });

            var tResume = Task.Run(async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var resumeSvc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                ready.Release();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return await resumeSvc.ResumeVisitAsync(
                    fx.Reception1UserId,
                    visit.VisitCode!,
                    new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: labOrderId));
            });

            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            await ready.WaitAsync(TimeSpan.FromSeconds(10));
            release.SetResult();

            var walkInEx = await tWalkIn;
            var resumed = await tResume;
            Assert.Contains("VISIT_RESUME_REQUIRED", walkInEx.Message, StringComparison.Ordinal);
            Assert.Equal(visit.Id, resumed.VisitId);

            Db.ChangeTracker.Clear();
            Assert.Equal(1, await Db.Visits.AsNoTracking().CountAsync(v => v.PatientId == fx.PatientId));
            Assert.Equal(1, await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == visit.Id && c.ChargeType == "EXAMINATION"));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // HELPERS, CONTROLLABLE CLOCK & ISOLATED FIXTURE MANAGEMENT
    // =========================================================================

    private HttpRequestMessage AuthedGet(string url, long userId, string role)
    {
        using var scope = _app.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(new User { Id = userId, Email = $"{role}-{userId}@medflow.test" }, [role]).AccessToken;
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

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
        StageCFixture fx,
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

    private async Task<VisitChargesResponseDto> GetChargesAsPatientAsync(StageCFixture fx, string visitCode)
    {
        var res = await _client.SendAsync(AuthedGet(
            $"/api/v1/patients/visits/{visitCode}/charges",
            fx.PatientUserId,
            RoleNames.Patient));
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<VisitChargesResponseDto>(body, WebJson)!;
    }

    private async Task<(long OrderId, string OrderCode)> OpenExamAndCreateSingleDiagnosticOrderAsync(
        StageCFixture fx,
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
                TreatmentPlan: "Theo dõi kết quả cận lâm sàng."));
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

    private async Task<long> PayDiagnosticOrderByCashAsync(StageCFixture fx, string visitCode, long orderId)
    {
        Db.ChangeTracker.Clear();
        var charge = await Db.VisitCharges.AsNoTracking()
            .SingleAsync(c => c.DiagnosticOrderId == orderId);
        var cashRes = await _client.SendAsync(AuthedPostJson(
            $"/api/v1/reception/visits/{visitCode}/payments/cash",
            new CollectCashPaymentRequestDto(
                VisitChargeIds: [charge.Id],
                IdempotencyKey: $"cash-sc-{orderId}-{Guid.NewGuid():N}"),
            fx.Reception1UserId,
            RoleNames.Reception));
        var body = await cashRes.Content.ReadAsStringAsync();
        Assert.True(cashRes.StatusCode == HttpStatusCode.Created, body);
        var dto = JsonSerializer.Deserialize<CollectCashPaymentResponseDto>(body, WebJson)!;
        return dto.PaymentId;
    }

    private async Task ConfirmAndReceiveDiagnosticOrderAsync(StageCFixture fx, string orderCode)
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
            new ReceiveDiagnosticPatientRequest(
                IsPatientPresent: true,
                IsIdentityVerified: true,
                IsPreparationVerified: true));
    }

    private async Task ExecuteDiagnosticOrderThroughUc42Async(StageCFixture fx, string orderCode)
    {
        await ConfirmAndReceiveDiagnosticOrderAsync(fx, orderCode);

        await using var scope = _app.Services.CreateAsyncScope();
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
        string conclusion)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var resultSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
        var res = await resultSvc.EnterDiagnosticResultAsync(
            diagStaffUserId,
            orderCode,
            new EnterDiagnosticResultRequest(
                ResultText: resultText,
                Conclusion: conclusion,
                Status: "FINALIZED",
                IsFinalized: true));
        Assert.True(res.IsFinalized);
    }

    private async Task CallNextAndCompleteVisitAsync(
        StageCFixture fx,
        string visitCode,
        long examSpId,
        long doctorUserId)
    {
        Db.ChangeTracker.Clear();
        var currentVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.VisitCode == visitCode);
        if (string.Equals(currentVisit.Status, "RETURN_CONSULTATION", StringComparison.OrdinalIgnoreCase))
        {
            var callNextRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{examSpId}/call-next",
                new { notes = "Gọi bệnh nhân vào phòng khám kết luận" },
                fx.Reception1UserId,
                RoleNames.Reception));
            var callBody = await callNextRes.Content.ReadAsStringAsync();
            Assert.True(callNextRes.StatusCode == HttpStatusCode.OK, callBody);

            await using var reopenScope = _app.Services.CreateAsyncScope();
            var reopenSvc = reopenScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
            await reopenSvc.OpenWorkspaceAsync(doctorUserId, visitCode);
        }

        await using var scope = _app.Services.CreateAsyncScope();
        var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
        await consultSvc.RecordDiagnosisAsync(
            doctorUserId,
            visitCode,
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: fx.Icd10Code,
                ClinicalConclusion: "Tình trạng ổn định, hoàn tất lượt khám ngoại trú.",
                IsConfirmed: true));

        var completed = await consultSvc.CompleteVisitAsync(
            doctorUserId,
            visitCode,
            new CompleteVisitRequest(
                ConfirmNoPrescription: true,
                DischargeInstructions: "Theo dõi tại nhà và tái khám khi có dấu hiệu bất thường."));
        Assert.Equal("COMPLETED", completed.Visit.Status);
    }

    private Task<Visit> CreateCheckedInVisitAsync(
        StageCFixture fx,
        string tag,
        decimal examPrice,
        bool payExamNow)
        => CreateCheckedInVisitForPatientAsync(fx, fx.PatientId, tag, examPrice, payExamNow);

    private async Task<Visit> CreateCheckedInVisitForPatientAsync(
        StageCFixture fx,
        long patientId,
        string tag,
        decimal examPrice,
        bool payExamNow)
    {
        Db.ChangeTracker.Clear();
        var nowLocal = HospitalClock.GetNow(_clock);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"VSC-{tag}-{fx.Suffix}",
            PatientId = patientId,
            AssignedDoctorId = fx.Doctor1Id,
            ExaminationRoomId = fx.ExamRoom1Id,
            Status = "CHECKED_IN",
            CheckedInAt = nowLocal.AddMinutes(-10),
            LastPresentDate = today,
            PresenceState = VisitPresenceStates.Present,
            ReasonForVisit = "Khám ngoại trú kiểm thử Stage C",
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
                IdempotencyKey = $"seed-sc-exam-{visit.Id}-{Guid.NewGuid():N}",
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

    private async Task<Visit> CreateWaitingExamVisitAsync(StageCFixture fx, string tag, decimal examPrice)
    {
        Db.ChangeTracker.Clear();
        var nowLocal = HospitalClock.GetNow(_clock);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"VSC-{tag}-{fx.Suffix}",
            PatientId = fx.PatientId,
            AssignedDoctorId = fx.Doctor1Id,
            ExaminationRoomId = fx.ExamRoom1Id,
            Status = "WAITING",
            CheckedInAt = nowLocal.AddMinutes(-10),
            LastPresentDate = today,
            PresenceState = VisitPresenceStates.Present,
            ReasonForVisit = "Khám ngoại trú hàng đợi chờ gọi",
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
            Status = "READY",
            PresenceStatus = "PRESENT",
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            ArrivedAt = nowLocal.AddMinutes(-9),
            ReadyAt = nowLocal.AddMinutes(-8),
            StateVersion = 1,
        });

        var examCharge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, examPrice, nowLocal.AddMinutes(-9));
        Db.VisitCharges.Add(examCharge);
        await Db.SaveChangesAsync();

        var paid = new Payment
        {
            VisitId = visit.Id,
            VisitChargeId = examCharge.Id,
            PaymentScope = PaymentScopes.Allocated,
            PaymentMethod = PaymentMethods.Cash,
            CollectedByStaffId = fx.Reception1StaffId,
            IdempotencyKey = $"seed-sc-wait-{visit.Id}-{Guid.NewGuid():N}",
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

        return visit;
    }

    private async Task<StageCFixture> CreateIsolatedFixtureAsync()
    {
        Db.ChangeTracker.Clear();
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var seedBaseTime = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified);

        var docRole = await EnsureRoleAsync(RoleNames.Doctor);
        var diagRole = await EnsureRoleAsync(RoleNames.DiagnosticStaff);
        var recRole = await EnsureRoleAsync(RoleNames.Reception);
        var hospAdminRole = await EnsureRoleAsync(RoleNames.HospitalAdmin);
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
                Code = $"K29.{suffix[..2]}",
                NameVi = "Viêm dạ dày kiểm thử Stage C",
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
            var spec = new Specialty { SpecialtyCode = $"SC{suffix}", Name = $"Nội Stage C {suffix}", IsActive = true };
            Db.Specialties.Add(spec);
            await Db.SaveChangesAsync();
            specialtyId = spec.Id;
        }

        var hospital = new Hospital { HospitalCode = $"SC1{suffix}", Name = $"BV Stage C {suffix}", IsActive = true };
        var otherHospital = new Hospital { HospitalCode = $"SC2{suffix}", Name = $"BV Khác SC {suffix}", IsActive = true };
        Db.Hospitals.AddRange(hospital, otherHospital);
        await Db.SaveChangesAsync();

        Db.HospitalPolicies.Add(new HospitalPolicy
        {
            HospitalId = hospital.Id,
            PolicyType = "PAYMENT",
            PolicyKey = "vietqr_account",
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN STAGE C"}""",
            PolicyVersion = 1,
        });

        var dept = new Department { HospitalId = hospital.Id, DepartmentCode = $"DC1{suffix}", Name = $"Khoa Nội SC {suffix}", IsActive = true };
        var otherDept = new Department { HospitalId = otherHospital.Id, DepartmentCode = $"DC2{suffix}", Name = $"Khoa Khác SC {suffix}", IsActive = true };
        Db.Departments.AddRange(dept, otherDept);
        await Db.SaveChangesAsync();

        var examService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"EX{suffix}",
            Name = "Khám Nội Stage C",
            ServiceType = "EXAMINATION",
            IsActive = true,
        };
        var labService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"LB{suffix}",
            Name = "Xét nghiệm máu Stage C",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var imagingService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"IM{suffix}",
            Name = "Siêu âm tổng quát Stage C",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var verifiedLabService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"VL{suffix}",
            Name = "Giải phẫu bệnh thẩm định 2 người Stage C",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = true,
            IsActive = true,
        };
        Db.Services.AddRange(examService, labService, imagingService, verifiedLabService);
        await Db.SaveChangesAsync();

        Db.ServicePrices.AddRange(
            new ServicePrice { ServiceId = examService.Id, PriceAmount = 150_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = labService.Id, PriceAmount = 120_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = imagingService.Id, PriceAmount = 240_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = verifiedLabService.Id, PriceAmount = 320_000m, EffectiveFrom = seedBaseTime });

        var examRoom1 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"R1{suffix}", Name = "PK 101 SC", RoomType = "EXAMINATION", Capacity = 10, Status = "ACTIVE" };
        var examRoom2 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"R2{suffix}", Name = "PK 102 SC", RoomType = "EXAMINATION", Capacity = 10, Status = "ACTIVE" };
        var labRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"RL{suffix}", Name = "PXN 201 SC", RoomType = "DIAGNOSTIC", Capacity = 2, Status = "ACTIVE" };
        var imgRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"RI{suffix}", Name = "PSA 202 SC", RoomType = "DIAGNOSTIC", Capacity = 2, Status = "ACTIVE" };
        Db.HospitalRooms.AddRange(examRoom1, examRoom2, labRoom, imgRoom);
        await Db.SaveChangesAsync();

        var doc1User = new User { Email = $"d1-sc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var doc2User = new User { Email = $"d2-sc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var diag1User = new User { Email = $"dg1-sc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var diag2User = new User { Email = $"dg2-sc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var rec1User = new User { Email = $"rc1-sc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var otherRecUser = new User { Email = $"rc2-sc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var hospAdminUser = new User { Email = $"ha-sc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var patUser = new User { Email = $"pt1-sc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var otherPatUser = new User { Email = $"pt2-sc-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        Db.Users.AddRange(doc1User, doc2User, diag1User, diag2User, rec1User, otherRecUser, hospAdminUser, patUser, otherPatUser);
        await Db.SaveChangesAsync();

        Db.UserRoles.AddRange(
            new UserRole { UserId = doc1User.Id, RoleId = docRole.Id },
            new UserRole { UserId = doc2User.Id, RoleId = docRole.Id },
            new UserRole { UserId = diag1User.Id, RoleId = diagRole.Id },
            new UserRole { UserId = diag2User.Id, RoleId = diagRole.Id },
            new UserRole { UserId = rec1User.Id, RoleId = recRole.Id },
            new UserRole { UserId = otherRecUser.Id, RoleId = recRole.Id },
            new UserRole { UserId = hospAdminUser.Id, RoleId = hospAdminRole.Id },
            new UserRole { UserId = patUser.Id, RoleId = patRole.Id },
            new UserRole { UserId = otherPatUser.Id, RoleId = patRole.Id });

        var doc1Staff = new Staff { UserId = doc1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SD1{suffix}", FullName = "BS. Một StageC", Status = "ACTIVE" };
        var doc2Staff = new Staff { UserId = doc2User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SD2{suffix}", FullName = "BS. Hai StageC", Status = "ACTIVE" };
        var diag1Staff = new Staff { UserId = diag1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SG1{suffix}", FullName = "KTV. Một StageC", Status = "ACTIVE" };
        var diag2Staff = new Staff { UserId = diag2User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SG2{suffix}", FullName = "BS.XN Hai StageC", Status = "ACTIVE" };
        var rec1Staff = new Staff { UserId = rec1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SRC{suffix}", FullName = "TN. Tiếp Đón StageC", Status = "ACTIVE" };
        var otherRecStaff = new Staff { UserId = otherRecUser.Id, HospitalId = otherHospital.Id, DepartmentId = otherDept.Id, StaffCode = $"SRO{suffix}", FullName = "TN. Cơ Sở Khác SC", Status = "ACTIVE" };
        var hospAdminStaff = new Staff { UserId = hospAdminUser.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SHA{suffix}", FullName = "QT. Bệnh Viện StageC", Status = "ACTIVE" };
        Db.Staff.AddRange(doc1Staff, doc2Staff, diag1Staff, diag2Staff, rec1Staff, otherRecStaff, hospAdminStaff);
        await Db.SaveChangesAsync();

        var doc1 = new Doctor { StaffId = doc1Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialtyId, DoctorCode = $"DC1{suffix}", ProfessionalTitle = "BS.CKI", IsActive = true };
        var doc2 = new Doctor { StaffId = doc2Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialtyId, DoctorCode = $"DC2{suffix}", ProfessionalTitle = "BS.CKI", IsActive = true };
        Db.Doctors.AddRange(doc1, doc2);
        await Db.SaveChangesAsync();

        Db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = examService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = labService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = imagingService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = verifiedLabService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc2.Id, ServiceId = examService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc2.Id, ServiceId = labService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc2.Id, ServiceId = imagingService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc2.Id, ServiceId = verifiedLabService.Id, IsActive = true });

        var examSp1 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom1.Id, DoctorId = doc1.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "PK 101 SC", IsActive = true };
        var examSp2 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom2.Id, DoctorId = doc2.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "PK 102 SC", IsActive = true };
        var labSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = labRoom.Id, ServiceId = labService.Id, PointType = "DIAGNOSTIC", Name = "PXN 201 SC", IsActive = true };
        var imgSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = imgRoom.Id, ServiceId = imagingService.Id, PointType = "DIAGNOSTIC", Name = "PSA 202 SC", IsActive = true };
        var verLabSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = labRoom.Id, ServiceId = verifiedLabService.Id, PointType = "DIAGNOSTIC", Name = "PGPB 203 SC", IsActive = true };
        Db.QueueServicePoints.AddRange(examSp1, examSp2, labSp, imgSp, verLabSp);

        var medicine = new Medicine
        {
            HospitalId = hospital.Id,
            MedicineCode = $"MEDSC{suffix}",
            Name = $"Esomeprazol 40mg Stage C {suffix}",
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

        var medicineBatch = new MedicineBatch
        {
            MedicineId = medicine.Id,
            BatchNumber = $"LOT-{suffix}",
            ReceivedAt = seedBaseTime,
            ExpiryDate = new DateOnly(2028, 12, 31),
            QuantityOnHand = 500m,
            IsBlocked = false,
        };
        Db.MedicineBatches.Add(medicineBatch);

        var patient = new Patient
        {
            UserId = patUser.Id,
            PatientCode = $"PSC1{suffix}",
            FullName = "Nguyễn Văn Bệnh Nhân SC",
            Gender = "MALE",
            Phone = "0909500600",
            IdentificationHash = $"sc-id-1-{suffix}",
            Status = "ACTIVE",
        };
        var otherPatient = new Patient
        {
            UserId = otherPatUser.Id,
            PatientCode = $"PSC2{suffix}",
            FullName = "Lê Thị Bệnh Nhân Khác SC",
            Gender = "FEMALE",
            Phone = "0909700800",
            IdentificationHash = $"sc-id-2-{suffix}",
            Status = "ACTIVE",
        };
        Db.Patients.AddRange(patient, otherPatient);
        await Db.SaveChangesAsync();

        return new StageCFixture(
            Suffix: suffix,
            HospitalId: hospital.Id,
            OtherHospitalId: otherHospital.Id,
            DeptId: dept.Id,
            OtherDeptId: otherDept.Id,
            ExamRoom1Id: examRoom1.Id,
            ExamRoom2Id: examRoom2.Id,
            AllRoomIds: [examRoom1.Id, examRoom2.Id, labRoom.Id, imgRoom.Id],
            ExamSp1Id: examSp1.Id,
            ExamSp2Id: examSp2.Id,
            AllSpIds: [examSp1.Id, examSp2.Id, labSp.Id, imgSp.Id, verLabSp.Id],
            ExamServiceId: examService.Id,
            LabServiceId: labService.Id,
            ImagingServiceId: imagingService.Id,
            VerifiedLabServiceId: verifiedLabService.Id,
            AllServiceIds: [examService.Id, labService.Id, imagingService.Id, verifiedLabService.Id],
            MedicineId: medicine.Id,
            Doctor1UserId: doc1User.Id,
            Doctor1Id: doc1.Id,
            Doctor2UserId: doc2User.Id,
            Doctor2Id: doc2.Id,
            DiagStaff1UserId: diag1User.Id,
            DiagStaff2UserId: diag2User.Id,
            Reception1UserId: rec1User.Id,
            Reception1StaffId: rec1Staff.Id,
            OtherHospReceptionUserId: otherRecUser.Id,
            HospAdminUserId: hospAdminUser.Id,
            PatientUserId: patUser.Id,
            PatientId: patient.Id,
            OtherPatientUserId: otherPatUser.Id,
            OtherPatientId: otherPatient.Id,
            AllUserIds: [doc1User.Id, doc2User.Id, diag1User.Id, diag2User.Id, rec1User.Id, otherRecUser.Id, hospAdminUser.Id, patUser.Id, otherPatUser.Id],
            AllStaffIds: [doc1Staff.Id, doc2Staff.Id, diag1Staff.Id, diag2Staff.Id, rec1Staff.Id, otherRecStaff.Id, hospAdminStaff.Id],
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

    private async Task CleanupIsolatedFixtureAsync(StageCFixture fx)
    {
        Db.ChangeTracker.Clear();

        var patientIds = new[] { fx.PatientId, fx.OtherPatientId };
        var visitIds = await Db.Visits
            .Where(v => v.PatientId.HasValue && patientIds.Contains(v.PatientId.Value))
            .Select(v => v.Id)
            .ToListAsync();

        var apptIds = await Db.Appointments
            .Where(a => a.PatientId.HasValue && patientIds.Contains(a.PatientId.Value))
            .Select(a => a.Id)
            .ToListAsync();

        await Db.Notifications.Where(n =>
            (n.VisitId.HasValue && visitIds.Contains(n.VisitId.Value)) ||
            (n.AppointmentId.HasValue && apptIds.Contains(n.AppointmentId.Value)) ||
            (n.PatientId.HasValue && patientIds.Contains(n.PatientId.Value))).ExecuteDeleteAsync();

        if (visitIds.Count > 0)
        {
            var rxIds = await Db.Prescriptions
                .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
                .Select(p => p.Id)
                .ToListAsync();
            if (rxIds.Count > 0)
            {
                var dispenseIds = await Db.MedicationDispenses
                    .Where(d => d.PrescriptionId.HasValue && rxIds.Contains(d.PrescriptionId.Value))
                    .Select(d => d.Id)
                    .ToListAsync();
                if (dispenseIds.Count > 0)
                {
                    await Db.DispenseItems.Where(di => di.DispenseId.HasValue && dispenseIds.Contains(di.DispenseId.Value)).ExecuteDeleteAsync();
                    await Db.MedicationDispenses.Where(d => dispenseIds.Contains(d.Id)).ExecuteDeleteAsync();
                }

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

        await Db.QueueFairnessStates.Where(f => fx.AllSpIds.Contains(f.ServicePointId)).ExecuteDeleteAsync();
        await Db.QueueEntries.Where(q =>
            (q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)) ||
            (q.AppointmentId.HasValue && apptIds.Contains(q.AppointmentId.Value)) ||
            (q.ServicePointId.HasValue && fx.AllSpIds.Contains(q.ServicePointId.Value))).ExecuteDeleteAsync();

        await Db.AuditLogs.Where(a =>
            a.HospitalId == fx.HospitalId ||
            a.HospitalId == fx.OtherHospitalId ||
            (a.ActorUserId.HasValue && fx.AllUserIds.Contains(a.ActorUserId.Value))).ExecuteDeleteAsync();

        if (visitIds.Count > 0)
        {
            await Db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
        }

        if (apptIds.Count > 0)
        {
            await Db.Appointments.Where(a => apptIds.Contains(a.Id)).ExecuteDeleteAsync();
        }

        await Db.ExaminationSlots.Where(s =>
            (s.DoctorId.HasValue && (s.DoctorId.Value == fx.Doctor1Id || s.DoctorId.Value == fx.Doctor2Id)) ||
            (s.RoomId.HasValue && fx.AllRoomIds.Contains(s.RoomId.Value))).ExecuteDeleteAsync();

        await Db.MedicineBatches.Where(mb => mb.MedicineId == fx.MedicineId).ExecuteDeleteAsync();
        await Db.Medicines.Where(m => m.Id == fx.MedicineId).ExecuteDeleteAsync();

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
        await Db.HospitalPolicies.Where(p => p.HospitalId == fx.HospitalId || p.HospitalId == fx.OtherHospitalId).ExecuteDeleteAsync();
        await Db.Hospitals.Where(h => h.Id == fx.HospitalId || h.Id == fx.OtherHospitalId).ExecuteDeleteAsync();

        if (fx.CreatedIcd10Id.HasValue)
        {
            await Db.Icd10Codes.Where(c => c.Id == fx.CreatedIcd10Id.Value).ExecuteDeleteAsync();
        }
    }

    private static async Task AssertPreflight025ZeroViolationsAsync(string connectionString, string preflightSql)
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
            Assert.True(violations == 0, $"Preflight 025 check '{checkName}' found {violations} violations.");
        }

        Assert.Equal(4, checks);
    }

    private static async Task ApplyMigrationsUpTo024Async(string connectionString, string repoRoot)
    {
        var migrationsDir = Path.Combine(repoRoot, "database", "migrations");
        var files = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("001_", StringComparison.Ordinal))
                {
                    return false;
                }

                if (string.CompareOrdinal(name, "025_") >= 0)
                {
                    return false;
                }

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
            ?? "Host=localhost;Port=5433;Database=medflow_test_stage_c;Username=postgres;Password=medflow_dev";
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

    private sealed record StageCFixture(
        string Suffix,
        long HospitalId,
        long OtherHospitalId,
        long DeptId,
        long OtherDeptId,
        long ExamRoom1Id,
        long ExamRoom2Id,
        IReadOnlyList<long> AllRoomIds,
        long ExamSp1Id,
        long ExamSp2Id,
        IReadOnlyList<long> AllSpIds,
        long ExamServiceId,
        long LabServiceId,
        long ImagingServiceId,
        long VerifiedLabServiceId,
        IReadOnlyList<long> AllServiceIds,
        long MedicineId,
        long Doctor1UserId,
        long Doctor1Id,
        long Doctor2UserId,
        long Doctor2Id,
        long DiagStaff1UserId,
        long DiagStaff2UserId,
        long Reception1UserId,
        long Reception1StaffId,
        long OtherHospReceptionUserId,
        long HospAdminUserId,
        long PatientUserId,
        long PatientId,
        long OtherPatientUserId,
        long OtherPatientId,
        IReadOnlyList<long> AllUserIds,
        IReadOnlyList<long> AllStaffIds,
        string Icd10Code,
        long? CreatedIcd10Id);

    private sealed class TempStageCDatabase : IAsyncDisposable
    {
        private readonly string _adminConn;
        public string DatabaseName { get; }
        public string ConnectionString { get; }

        private TempStageCDatabase(string adminConn, string dbName, string connStr)
        {
            _adminConn = adminConn;
            DatabaseName = dbName;
            ConnectionString = connStr;
        }

        public static async Task<TempStageCDatabase> CreateAsync(string adminConn)
        {
            var dbName = $"medflow_sc_{Guid.NewGuid():N}"[..27];
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
            return new TempStageCDatabase(adminConn, dbName, target);
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
