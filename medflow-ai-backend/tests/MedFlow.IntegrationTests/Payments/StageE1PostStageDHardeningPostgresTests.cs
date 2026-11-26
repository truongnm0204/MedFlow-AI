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
/// Stage E1 (GH-856 / ADR-0061) Real-PostgreSQL Hardening &amp; Clinical Revalidation Suite.
/// Covers Migration 026 + preflight_026_audit.sql + dirty-data fail-fast rollback,
/// BUG-01 (PE-01 stale/closed QR idempotency replay &amp; empty payload when non-PENDING),
/// BUG-02 (post-lock Doctor reassignment TOCTOU barrier across Selection / Decline / Doctor QR),
/// BUG-03 (compatible Diagnostic ServicePoint resolution without cross-service fallback),
/// BUG-04 (QE-24 expired CLS clinical review &amp; revalidation + RC-20 overdue return SHA-256 snapshot approval),
/// BUG-05 (PE-07 VisitChargeAssessor delegation on legacy visits in resume-context),
/// and PE-02 (distinct SePay duplicate transfer reconciliation without mutating PAID).
/// </summary>
public sealed class StageE1PostStageDHardeningPostgresTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    private const string WebhookUrl = "/api/v1/payments/sepay/webhook";
    private const string WebhookApiKey = "IntegrationTest_SePay_Key";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static long _webhookSeq = 1_900_000;

    private readonly ControllableVietnamClock _clock = new(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;

    public StageE1PostStageDHardeningPostgresTests(TestWebAppFactory factory)
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
    // 1. MIGRATION 026 & PREFLIGHT 026 AUDIT + DIRTY DATA ROLLBACK
    // =========================================================================

    [Fact]
    public async Task Migration026_Preflight_IdempotentApply_AndDirtyCoordinationPurposeRollback()
    {
        var repoRoot = ResolveRepoRoot();
        var preflightSql = await File.ReadAllTextAsync(
            Path.Combine(repoRoot, "database", "scripts", "preflight_026_audit.sql"));
        var migration026Sql = await File.ReadAllTextAsync(
            Path.Combine(repoRoot, "database", "migrations", "026_clinical_revalidation_overdue_approval_and_review_purpose.sql"));

        var adminConn = ResolveAdminConnectionString();

        // Part A: Clean DB -> preflight (0) -> Run 1 & Run 2 idempotent -> preflight (0) -> constraints verified
        await using (var cleanDb = await TempStageE1Database.CreateAsync(adminConn))
        {
            await ApplyMigrationsUpTo025Async(cleanDb.ConnectionString, repoRoot);
            await AssertPreflight026ZeroViolationsAsync(cleanDb.ConnectionString, preflightSql);

            await ExecuteSqlAsync(cleanDb.ConnectionString, migration026Sql);
            await ExecuteSqlAsync(cleanDb.ConnectionString, migration026Sql);

            await AssertPreflight026ZeroViolationsAsync(cleanDb.ConnectionString, preflightSql);

            // Valid coordination_purpose values succeed
            await ExecuteSqlAsync(
                cleanDb.ConnectionString,
                """
                INSERT INTO queue_entries (queue_date, queue_number, queue_type, status, coordination_purpose)
                VALUES ('2026-11-10', 801, 'RETURN_CONSULTATION', 'READY', NULL),
                       ('2026-11-10', 802, 'RETURN_CONSULTATION', 'READY', 'RESULT_CONCLUSION'),
                       ('2026-11-10', 803, 'RETURN_CONSULTATION', 'READY', 'CLINICAL_REVIEW');
                """);

            // Invalid coordination_purpose rejected by chk_queue_entries_coordination_purpose (23514)
            var checkEx = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteSqlAsync(
                    cleanDb.ConnectionString,
                    """
                    INSERT INTO queue_entries (queue_date, queue_number, queue_type, status, coordination_purpose)
                    VALUES ('2026-11-10', 804, 'RETURN_CONSULTATION', 'READY', 'BOGUS_PURPOSE');
                    """));
            Assert.Equal("23514", checkEx.SqlState);

            // Orphan doctor FK rejected on visits.overdue_return_approved_by_doctor_id (23503)
            var fkVisitEx = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteSqlAsync(
                    cleanDb.ConnectionString,
                    """
                    INSERT INTO visits (visit_code, status, presence_state, overdue_return_approved_by_doctor_id)
                    VALUES ('VIS-FK-026', 'RETURN_CONSULTATION', 'PRESENT', -99999);
                    """));
            Assert.Equal("23503", fkVisitEx.SqlState);
        }

        // Part B: Dirty pre-existing coordination_purpose -> 026 fails fast and rolls back completely
        await using (var dirtyDb = await TempStageE1Database.CreateAsync(adminConn))
        {
            await ApplyMigrationsUpTo025Async(dirtyDb.ConnectionString, repoRoot);

            await ExecuteSqlAsync(
                dirtyDb.ConnectionString,
                """
                ALTER TABLE queue_entries ADD COLUMN coordination_purpose varchar(24) NULL;
                INSERT INTO queue_entries (queue_date, queue_number, queue_type, status, coordination_purpose)
                VALUES ('2026-11-10', 999, 'RETURN_CONSULTATION', 'READY', 'INVALID_DIRTY_PURPOSE');
                """);

            var preflightViolations = await QueryPreflightViolationsAsync(dirtyDb.ConnectionString, preflightSql);
            Assert.Equal(1L, preflightViolations["queue_entries_invalid_coordination_purpose"]);

            var migrationEx = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteSqlAsync(dirtyDb.ConnectionString, migration026Sql));
            Assert.Contains("Migration 026 preflight failed", migrationEx.MessageText, StringComparison.Ordinal);

            // Verify transactional rollback: diagnostic_orders.validity_extended_until & visits.overdue_return_result_snapshot were NOT added
            await using var verifyConn = new NpgsqlConnection(dirtyDb.ConnectionString);
            await verifyConn.OpenAsync();
            await using var checkColsCmd = new NpgsqlCommand(
                """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND ((table_name = 'diagnostic_orders' AND column_name = 'validity_extended_until')
                    OR (table_name = 'visits' AND column_name = 'overdue_return_result_snapshot'));
                """,
                verifyConn);
            var leakedColumns = (long)(await checkColsCmd.ExecuteScalarAsync())!;
            Assert.Equal(0L, leakedColumns);
        }
    }

    // =========================================================================
    // 2. BUG-01 (PE-01): STALE / CLOSED QR IDEMPOTENCY REPLAY & EMPTY PAYLOAD
    // =========================================================================

    [Fact]
    public async Task Bug01_Pe01_StaleQrIdempotencyReplay_CommitsExpiredBefore409_RejectsCancelledAndMismatch_AndReturnsEmptyQrPayloadWhenPaid()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "E1B01", examPrice: 150_000m, payExamNow: true);
            long labOrderId;
            long imgOrderId;
            await using (var orderScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = orderScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(ClinicalFindings: "Khám ổn định.", TreatmentPlan: "Chỉ định CLS kiểm thử PE-01."));
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        TransitionToAwaitingResults: true,
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "XN máu PE-01", RequiredForConclusion: false, PatientAccepted: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "Siêu âm PE-01", RequiredForConclusion: false, PatientAccepted: true),
                        ]));
                labOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                imgOrderId = created.CreatedDiagnosticOrders[1].OrderId;
            }

            Db.ChangeTracker.Clear();
            var labCharge = await Db.VisitCharges.AsNoTracking()
                .SingleAsync(c => c.DiagnosticOrderId == labOrderId);
            var imgCharge = await Db.VisitCharges.AsNoTracking()
                .SingleAsync(c => c.DiagnosticOrderId == imgOrderId);

            // Case 1: PENDING QR expires -> replay commits EXPIRED BEFORE throwing 409 [QR_ATTEMPT_EXPIRED]
            var keyExpired = $"e1-qr-exp-{fx.Suffix}";
            var createExpRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [labCharge.Id], IdempotencyKey: keyExpired),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, createExpRes.StatusCode);
            var expQrDto = await createExpRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson);
            Assert.NotNull(expQrDto);
            Assert.Equal("PENDING", expQrDto!.Status);
            Assert.False(string.IsNullOrWhiteSpace(expQrDto.QrPayload));

            // Advance 16 minutes (> 15-minute PendingExpiryMinutes)
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 16, 0, DateTimeKind.Unspecified));
            var replayExpRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [labCharge.Id], IdempotencyKey: keyExpired),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Conflict, replayExpRes.StatusCode);
            Assert.Contains("QR_ATTEMPT_EXPIRED", await replayExpRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // Prove EXPIRED transition was committed (NOT rolled back!) and zero new payment rows minted
            Db.ChangeTracker.Clear();
            var persistedExpPayment = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == expQrDto.PaymentId);
            var persistedExpAlloc = await Db.PaymentAllocations.AsNoTracking().SingleAsync(a => a.PaymentId == expQrDto.PaymentId);
            Assert.Equal(PaymentStatuses.Expired, persistedExpPayment.Status);
            Assert.Equal(PaymentAllocationStatuses.Expired, persistedExpAlloc.Status);
            Assert.Equal(1, await Db.Payments.AsNoTracking().CountAsync(p => p.VisitId == visit.Id && p.PaymentMethod == PaymentMethods.VietQr));

            // Subsequent replay of the now-EXPIRED key -> 409 [QR_ATTEMPT_CLOSED]
            var replayExpAgainRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [labCharge.Id], IdempotencyKey: keyExpired),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Conflict, replayExpAgainRes.StatusCode);
            Assert.Contains("QR_ATTEMPT_CLOSED", await replayExpAgainRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // Case 2: CANCELLED QR replay -> 409 [QR_ATTEMPT_CLOSED], zero new rows
            var keyCancelled = $"e1-qr-can-{fx.Suffix}";
            var createCanRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [labCharge.Id], IdempotencyKey: keyCancelled),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, createCanRes.StatusCode);

            // Switching optional order to UNDECIDED cancels the PENDING QR attempt
            var unselectRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/diagnostic-orders/selection",
                new UpdateDiagnosticOrderSelectionRequestDto(UndecidedOrderIds: [labOrderId]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, unselectRes.StatusCode);

            var reselectRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/diagnostic-orders/selection",
                new UpdateDiagnosticOrderSelectionRequestDto(AcceptedOrderIds: [labOrderId]),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, reselectRes.StatusCode);

            var replayCanRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [labCharge.Id], IdempotencyKey: keyCancelled),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Conflict, replayCanRes.StatusCode);
            Assert.Contains("QR_ATTEMPT_CLOSED", await replayCanRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(2, await Db.Payments.AsNoTracking().CountAsync(p => p.VisitId == visit.Id && p.PaymentMethod == PaymentMethods.VietQr));

            // Case 3: PAID QR replay -> 200 OK with IssueKind == "ALREADY_PAID" and QrPayload == ""
            var keyPaid = $"e1-qr-paid-{fx.Suffix}";
            var createPaidRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [labCharge.Id], IdempotencyKey: keyPaid),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, createPaidRes.StatusCode);
            var paidQrDto = await createPaidRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson);
            Assert.NotNull(paidQrDto);

            var paidWhRes = await PostWebhookAsync(NextWebhookId(), 120_000m, paidQrDto!.PaymentCode);
            Assert.Equal(HttpStatusCode.OK, paidWhRes.StatusCode);

            var replayPaidRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [labCharge.Id], IdempotencyKey: keyPaid),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.OK, replayPaidRes.StatusCode);
            var replayPaidDto = await replayPaidRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson);
            Assert.NotNull(replayPaidDto);
            Assert.Equal(PaymentStatuses.Paid, replayPaidDto!.Status);
            Assert.Equal(QrPaymentIssueKind.AlreadyPaid, replayPaidDto.IssueKind);
            Assert.Equal(string.Empty, replayPaidDto.QrPayload);
            Assert.Equal(3, await Db.Payments.AsNoTracking().CountAsync(p => p.VisitId == visit.Id && p.PaymentMethod == PaymentMethods.VietQr));

            // Case 4: MISMATCH QR replay on sibling charge -> 409 [QR_ATTEMPT_CLOSED], zero new rows
            var keyMismatch = $"e1-qr-mis-{fx.Suffix}";
            var createMisRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [imgCharge.Id], IdempotencyKey: keyMismatch),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, createMisRes.StatusCode);
            var misQrDto = await createMisRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson);
            Assert.NotNull(misQrDto);

            var misWhRes = await PostWebhookAsync(NextWebhookId(), 50_000m, misQrDto!.PaymentCode);
            Assert.Equal(HttpStatusCode.OK, misWhRes.StatusCode);

            var replayMisRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [imgCharge.Id], IdempotencyKey: keyMismatch),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Conflict, replayMisRes.StatusCode);
            Assert.Contains("QR_ATTEMPT_CLOSED", await replayMisRes.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(4, await Db.Payments.AsNoTracking().CountAsync(p => p.VisitId == visit.Id && p.PaymentMethod == PaymentMethods.VietQr));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 3. BUG-02: POST-LOCK DOCTOR REASSIGNMENT TOCTOU BARRIER CONCURRENCY
    // =========================================================================

    [Fact]
    public async Task Bug02_DoctorReassignmentToctou_BarrierConcurrencyAgainstSelectionDeclineAndDoctorQr_BlocksOldDoctorWith403()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var connStr = Db.Database.GetConnectionString()!;

            // Race A: DoctorReassignment vs DiagnosticOrderSelectionService.UpdateSelectionAsync
            var visitA = await CreateCheckedInVisitAsync(fx, "E1B02A", examPrice: 150_000m, payExamNow: true);
            var (orderAId, _) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visitA.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: false,
                instruction: "Xét nghiệm Race A Selection");

            await RunPostLockReassignmentBarrierAsync(
                connStr,
                visitA.Id,
                fx.Doctor2Id,
                fx.ExamRoom2Id,
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderSelectionService>();
                    await svc.UpdateSelectionAsync(
                        fx.Doctor1UserId,
                        visitA.VisitCode!,
                        new UpdateDiagnosticOrderSelectionRequestDto(AcceptedOrderIds: [orderAId]));
                });

            Db.ChangeTracker.Clear();
            var orderAAfter = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == orderAId);
            Assert.Equal(DiagnosticSelectionStatuses.Undecided, orderAAfter.SelectionStatus);

            // Race B: DoctorReassignment vs DiagnosticOrderSelectionService.DeclineOrderAsync
            var visitB = await CreateCheckedInVisitAsync(fx, "E1B02B", examPrice: 150_000m, payExamNow: true);
            var (orderBId, orderBCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visitB.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: false,
                instruction: "Xét nghiệm Race B Decline");

            await RunPostLockReassignmentBarrierAsync(
                connStr,
                visitB.Id,
                fx.Doctor2Id,
                fx.ExamRoom2Id,
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderSelectionService>();
                    await svc.DeclineOrderAsync(
                        fx.Doctor1UserId,
                        visitB.VisitCode!,
                        orderBCode,
                        new DeclineDiagnosticOrderRequestDto(Reason: "BS cũ thử hủy sau khi đã chuyển BS"));
                });

            Db.ChangeTracker.Clear();
            var orderBAfter = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == orderBId);
            var chargeBAfter = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderBId);
            Assert.Equal("ORDERED", orderBAfter.Status);
            Assert.Equal(DiagnosticSelectionStatuses.Undecided, orderBAfter.SelectionStatus);
            Assert.Equal(VisitChargeStatuses.Active, chargeBAfter.Status);

            // Race C: DoctorReassignment vs GenerateQrPaymentService.CreateAsync (Doctor actor)
            var visitC = await CreateCheckedInVisitAsync(fx, "E1B02C", examPrice: 150_000m, payExamNow: true);
            var (orderCId, _) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visitC.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm Race C Doctor QR");
            var chargeC = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderCId);

            await RunPostLockReassignmentBarrierAsync(
                connStr,
                visitC.Id,
                fx.Doctor2Id,
                fx.ExamRoom2Id,
                async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IGenerateQrPaymentService>();
                    await svc.CreateAsync(
                        fx.Doctor1UserId,
                        visitC.VisitCode!,
                        "127.0.0.1",
                        new GenerateQrPaymentRequestDto(
                            VisitChargeIds: [chargeC.Id],
                            IdempotencyKey: $"race-c-doc1-{fx.Suffix}"));
                });

            Db.ChangeTracker.Clear();
            Assert.Equal(0, await Db.Payments.AsNoTracking().CountAsync(p => p.VisitId == visitC.Id && p.PaymentMethod == PaymentMethods.VietQr));

            // Confirm newly assigned Doctor2 succeeds cleanly on visitC
            var doc2QrRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/visits/{visitC.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [chargeC.Id], IdempotencyKey: $"race-c-doc2-{fx.Suffix}"),
                fx.Doctor2UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Created, doc2QrRes.StatusCode);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 4. BUG-03: STRICT COMPATIBLE DIAGNOSTIC SERVICE POINT RESOLUTION
    // =========================================================================

    [Fact]
    public async Task Bug03_IncompatibleDiagnosticServicePoint_WhenLabClosedAndUltrasoundActive_RejectsResumeWith409InsteadOfRoutingToUltrasound()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "E1B03", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, _) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm sinh hóa máu kiểm thử BUG-03");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);

            // Advance to D+1: Lab room enters MAINTENANCE while Ultrasound (ImagingSpId) stays ACTIVE
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 8, 30, 0, DateTimeKind.Unspecified));
            await Db.HospitalRooms.Where(r => r.Id == fx.LabRoomId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "MAINTENANCE"));

            var closedRoomRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: labOrderId),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, closedRoomRes.StatusCode);
            Assert.Contains(
                "NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT",
                await closedRoomRes.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            // Explicit Ultrasound TargetServicePointId for a Lab order -> 409 [INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT]
            var incompatibleExplicitRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderId: labOrderId,
                    TargetServicePointId: fx.ImagingSpId),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, incompatibleExplicitRes.StatusCode);
            Assert.Contains(
                "INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT",
                await incompatibleExplicitRes.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            // Zero queue entries routed to Ultrasound!
            Db.ChangeTracker.Clear();
            Assert.Equal(
                0,
                await Db.QueueEntries.AsNoTracking().CountAsync(q =>
                    q.VisitId == visit.Id &&
                    q.ServicePointId == fx.ImagingSpId));

            // Restore Lab room to ACTIVE -> Resume routes strictly to LabSpId
            await Db.HospitalRooms.Where(r => r.Id == fx.LabRoomId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "ACTIVE"));

            var validResume = await PostResumeAsync(
                fx,
                visit.VisitCode!,
                new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: labOrderId));
            Assert.Single(validResume.ResumedQueues);
            Assert.Equal(fx.LabSpId, validResume.ResumedQueues[0].ServicePointId);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 5. BUG-04 (QE-24): EXPIRED ORDER BLOCKS RECEPTION & ALL CLS ENTRY POINTS;
    //    CLINICAL_REVIEW + DOCTOR REVALIDATION UNLOCKS WITHOUT RE-CHARGING
    // =========================================================================

    [Fact]
    public async Task Bug04_Qe24_ExpiredDiagnosticOrder_BlocksReceptionOverrideAndAllClsEntryPoints_AllowsClinicalReviewAndDoctorRevalidation_PreservingPaid()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "E1QE24", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm công thức máu kiểm thử QE-24");
            var labPaymentId = await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);

            Db.ChangeTracker.Clear();
            var dayDQueueEntryId = (await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == labOrderId)).QueueEntryId;

            // Advance 9 days to D+9 (2026-11-19 > 7-day diagnostic_order_validity_days)
            var dPlus9 = new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Unspecified);
            var dPlus9Date = DateOnly.FromDateTime(dPlus9);
            _clock.SetVietnamLocalTime(dPlus9);

            // 1) Reception Resume WITH ConfirmOverdueOverride = true is strictly blocked!
            var recOverrideRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderId: labOrderId,
                    ConfirmOverdueOverride: true,
                    Notes: "Lễ tân thử ghi đè quá hạn"),
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.Conflict, recOverrideRes.StatusCode);
            Assert.Contains(
                "DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED",
                await recOverrideRes.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            // 2) Prove ALL 5 CLS execution entry points (UC39, UC40, UC41 CallNext, UC41 manual status, UC42 StartExecution)
            //    enforce [DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED] even if daily presence & queue entry exist!
            await Db.Visits.Where(v => v.Id == visit.Id).ExecuteUpdateAsync(s => s
                .SetProperty(v => v.LastPresentDate, dPlus9Date)
                .SetProperty(v => v.PresenceState, VisitPresenceStates.Present));

            var tempClsQueue = new QueueEntry
            {
                VisitId = visit.Id,
                ServicePointId = fx.LabSpId,
                CurrentServicePointId = fx.LabSpId,
                QueueDate = dPlus9Date,
                QueueNumber = 1,
                QueueType = "DIAGNOSTIC",
                Status = "READY",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                ArrivedAt = dPlus9,
                ReadyAt = dPlus9,
                StateVersion = 1,
            };
            Db.QueueEntries.Add(tempClsQueue);
            await Db.SaveChangesAsync();
            await Db.DiagnosticOrders.Where(o => o.Id == labOrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.QueueEntryId, tempClsQueue.Id));

            await using (var clsScope = _app.Services.CreateAsyncScope())
            {
                var reviewSvc = clsScope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                var uc39Ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    reviewSvc.ConfirmDiagnosticOrderAsync(
                        fx.DiagStaff1UserId,
                        labOrderCode,
                        new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true)));
                Assert.Contains("DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED", uc39Ex.Message, StringComparison.Ordinal);
            }

            // Simulate order that was confirmed on Day D (CONFIRMED + WAITING) and returned expired on D+9 for UC40
            await Db.DiagnosticOrders.Where(o => o.Id == labOrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "CONFIRMED"));
            await Db.QueueEntries.Where(q => q.Id == tempClsQueue.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, "WAITING"));

            await using (var uc40Scope = _app.Services.CreateAsyncScope())
            {
                var receiveSvc = uc40Scope.ServiceProvider.GetRequiredService<IDiagnosticPatientReceiveService>();
                var uc40Ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    receiveSvc.ReceiveDiagnosticPatientAsync(
                        fx.DiagStaff1UserId,
                        labOrderCode,
                        new ReceiveDiagnosticPatientRequest(IsPatientPresent: true, IsIdentityVerified: true, IsPreparationVerified: true)));
                Assert.Contains("DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED", uc40Ex.Message, StringComparison.Ordinal);
            }

            // Simulate order in READY queue on D+9 for UC41 CallNext, UC41 manual status, and UC42 StartServiceExecution
            await Db.QueueEntries.Where(q => q.Id == tempClsQueue.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, "READY"));

            await using (var uc4142Scope = _app.Services.CreateAsyncScope())
            {
                var queueSvc = uc4142Scope.ServiceProvider.GetRequiredService<IDiagnosticQueueService>();
                var uc41CallEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    queueSvc.CallNextDiagnosticPatientAsync(
                        fx.DiagStaff1UserId,
                        fx.LabSpId.ToString(),
                        new CallNextDiagnosticPatientRequest(Notes: "Thử gọi CLS quá hạn")));
                Assert.Contains("DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED", uc41CallEx.Message, StringComparison.Ordinal);

                var uc41StatusEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    queueSvc.UpdateDiagnosticQueueEntryStatusAsync(
                        fx.DiagStaff1UserId,
                        tempClsQueue.Id.ToString(),
                        new UpdateDiagnosticQueueStatusRequest(TargetStatus: "CALLED", Notes: "Thử chuyển trạng thái CLS quá hạn")));
                Assert.Contains("DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED", uc41StatusEx.Message, StringComparison.Ordinal);

                var execSvc = uc4142Scope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                var uc42Ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    execSvc.StartServiceExecutionAsync(
                        fx.DiagStaff1UserId,
                        labOrderCode,
                        new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS")));
                Assert.Contains("DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED", uc42Ex.Message, StringComparison.Ordinal);
            }

            // Restore pre-resume state so Reception CLINICAL_REVIEW + Resume(DIAGNOSTIC) run from clean cross-day state
            await Db.DiagnosticOrders.Where(o => o.Id == labOrderId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, "ORDERED")
                    .SetProperty(o => o.QueueEntryId, dayDQueueEntryId));
            await Db.QueueEntries.Where(q => q.Id == tempClsQueue.Id).ExecuteDeleteAsync();

            // Verify PAID payment & allocation remain 100% intact
            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Paid, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == labPaymentId)).Status);
            Assert.Equal(PaymentAllocationStatuses.Paid, (await Db.PaymentAllocations.AsNoTracking().SingleAsync(a => a.PaymentId == labPaymentId)).Status);

            // 3) Reception coordinates CLINICAL_REVIEW (zero extra examination charge!)
            var reviewResume = await PostResumeAsync(fx, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(
                Target: "RETURN_CONSULTATION",
                CoordinationPurpose: QueueCoordinationPurposes.ClinicalReview,
                Notes: "Điều phối gặp bác sĩ đánh giá lại chỉ định quá hạn"));
            Assert.Single(reviewResume.ResumedQueues);
            Assert.Equal(1, await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == visit.Id && c.ChargeType == "EXAMINATION"));

            var callReviewRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi vào khám đánh giá lại chỉ định quá hạn" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callReviewRes.StatusCode);

            await using (var docScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = docScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var ws = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                Assert.Equal(QueueCoordinationPurposes.ClinicalReview, ws.Queue!.CoordinationPurpose);

                // Mandatory missing result STILL blocks CONFIRMED diagnosis, ISSUED prescription, and CompleteVisit!
                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new RecordDiagnosisRequest(
                            PrimaryIcd10Code: fx.Icd10Code,
                            ClinicalConclusion: "Thử xác nhận chẩn đoán khi thiếu CLS bắt buộc",
                            IsConfirmed: true)));

                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CreatePrescriptionAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CreatePrescriptionRequest(
                            IssueImmediately: true,
                            Items:
                            [
                                new CreatePrescriptionItemRequest(
                                    MedicineId: fx.MedicineId,
                                    PrescribedQuantity: 10,
                                    SingleDoseQuantity: 1,
                                    SingleDoseUnit: "viên",
                                    Frequency: "1 lần/ngày",
                                    DurationDays: 10,
                                    AdministrationRoute: "Uống",
                                    Instructions: "Uống trước ăn sáng 30 phút"),
                            ])));

                await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CompleteVisitRequest(ConfirmNoPrescription: true)));
            }

            // 4) Unassigned Doctor2 cannot revalidate (403 Forbidden)
            var doc2RevalRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/diagnostic-orders/{labOrderId}/revalidate",
                new RevalidateDiagnosticOrderRequest(Reason: "BS không phụ trách thử gia hạn"),
                fx.Doctor2UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Forbidden, doc2RevalRes.StatusCode);

            // 5) Assigned Doctor1 revalidates -> stamps ValidityExtendedUntil, audits DIAGNOSTIC_ORDER_REVALIDATED, releases Visit to AWAITING_RESULTS
            var doc1RevalRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/diagnostic-orders/{labOrderId}/revalidate",
                new RevalidateDiagnosticOrderRequest(Reason: "Tình trạng lâm sàng ổn định, gia hạn chỉ định xét nghiệm máu thêm 7 ngày"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.OK, doc1RevalRes.StatusCode);

            Db.ChangeTracker.Clear();
            var revalidatedOrder = await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == labOrderId);
            var releasedVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.NotNull(revalidatedOrder.ValidityExtendedUntil);
            Assert.Equal(fx.Doctor1Id, revalidatedOrder.RevalidatedByDoctorId);
            Assert.Equal("AWAITING_RESULTS", releasedVisit.Status);
            Assert.True(await Db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.HospitalId == fx.HospitalId &&
                a.Action == "DIAGNOSTIC_ORDER_REVALIDATED" &&
                a.ActorUserId == fx.Doctor1UserId));

            // 6) Reception resumes DIAGNOSTIC without re-charging -> UC39..UC43 + Return Consultation complete!
            var diagResume = await PostResumeAsync(
                fx,
                visit.VisitCode!,
                new ResumeCrossDayVisitRequestDto(Target: "DIAGNOSTIC", DiagnosticOrderId: labOrderId));
            Assert.Single(diagResume.ResumedQueues);

            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "Công thức máu bình thường.", "Bình thường.");
            await CallNextAndCompleteVisitAsync(fx, visit.VisitCode!, fx.ExamSp1Id, fx.Doctor1UserId);

            Db.ChangeTracker.Clear();
            Assert.Equal(2, await Db.Payments.AsNoTracking().CountAsync(p => p.VisitId == visit.Id && p.Status == PaymentStatuses.Paid));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 6. BUG-04 (RC-20): OVERDUE RETURN SHA-256 SNAPSHOT APPROVAL & INVALIDATIONS
    // =========================================================================

    [Fact]
    public async Task Bug04_Rc20_OverdueReturnConsultation_BlocksConfirmedDxIssuedRxAndCompleteUntilDoctorApprovesSnapshot_AndInvalidatesOnResultEditNewOrderOrDoctorChange()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "E1RC20", examPrice: 150_000m, payExamNow: true);
            var (labOrderId, labOrderCode) = await OpenExamAndCreateSingleDiagnosticOrderAsync(
                fx,
                visit.VisitCode!,
                fx.LabServiceId,
                requiredForConclusion: true,
                instruction: "Xét nghiệm men gan kiểm thử RC-20");
            await PayDiagnosticOrderByCashAsync(fx, visit.VisitCode!, labOrderId);
            await ExecuteDiagnosticOrderThroughUc42Async(fx, labOrderCode);
            await FinalizeDiagnosticResultAsync(fx.DiagStaff1UserId, labOrderCode, "AST 24 U/L, ALT 26 U/L.", "Men gan bình thường.");

            // Advance 10 days to D+10 (2026-11-20 > 7-day return_consultation_window_days)
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Unspecified));

            // Reception coordinates RESULT_CONCLUSION without override
            var returnResume = await PostResumeAsync(
                fx,
                visit.VisitCode!,
                new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"));
            Assert.Single(returnResume.ResumedQueues);

            var callRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp1Id}/call-next",
                new { notes = "Gọi bệnh nhân khám kết luận quá hạn" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callRes.StatusCode);

            // Before approval: provisional Dx & DRAFT Rx succeed, while CONFIRMED Dx / ISSUED Rx / CompleteVisit fail 409
            await using (var preScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = preScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var ws = await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                Assert.True(ws.Visit.IsReturnOverdue);
                Assert.False(ws.Visit.IsOverdueReturnApproved);

                var provDx = await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordDiagnosisRequest(
                        PrimaryIcd10Code: fx.Icd10Code,
                        ClinicalConclusion: "Chẩn đoán sơ bộ khi chưa xác nhận kết quả quá hạn",
                        IsConfirmed: false));
                Assert.NotEmpty(provDx.ClinicalContext.Diagnoses);

                var draftRx = await consultSvc.CreatePrescriptionAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreatePrescriptionRequest(
                        Status: "DRAFT",
                        IssueImmediately: false,
                        Items:
                        [
                            new CreatePrescriptionItemRequest(
                                MedicineId: fx.MedicineId,
                                PrescribedQuantity: 7,
                                SingleDoseQuantity: 1,
                                SingleDoseUnit: "viên",
                                Frequency: "1 lần/ngày",
                                DurationDays: 7,
                                AdministrationRoute: "Uống",
                                Instructions: "Uống trước ăn sáng 30 phút"),
                        ]));
                Assert.Equal("DRAFT", draftRx.ClinicalContext.PrescriptionSummary.LatestPrescriptionStatus);

                var confDxEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.RecordDiagnosisAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new RecordDiagnosisRequest(
                            PrimaryIcd10Code: fx.Icd10Code,
                            ClinicalConclusion: "Thử xác nhận chẩn đoán",
                            IsConfirmed: true)));
                Assert.Contains("OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED", confDxEx.Message, StringComparison.Ordinal);

                var issueRxEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CreatePrescriptionAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CreatePrescriptionRequest(
                            IssueImmediately: true,
                            Items:
                            [
                                new CreatePrescriptionItemRequest(
                                    MedicineId: fx.MedicineId,
                                    PrescribedQuantity: 7,
                                    SingleDoseQuantity: 1,
                                    SingleDoseUnit: "viên",
                                    Frequency: "1 lần/ngày",
                                    DurationDays: 7,
                                    AdministrationRoute: "Uống",
                                    Instructions: "Uống trước ăn sáng 30 phút"),
                            ])));
                Assert.Contains("OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED", issueRxEx.Message, StringComparison.Ordinal);

                var completeEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CompleteVisitRequest(ConfirmNoPrescription: true)));
                Assert.Contains("OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED", completeEx.Message, StringComparison.Ordinal);
            }

            // Unassigned Doctor2 gets 403 Forbidden on approve-overdue-results
            var doc2ApproveRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/approve-overdue-results",
                new ApproveOverdueReturnResultsRequest(Reason: "BS không phụ trách thử duyệt"),
                fx.Doctor2UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.Forbidden, doc2ApproveRes.StatusCode);

            // Doctor1 approves current snapshot -> 64-hex SHA-256 stamped + audit recorded -> CONFIRMED Dx succeeds
            var approve1Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/approve-overdue-results",
                new ApproveOverdueReturnResultsRequest(Reason: "Kết quả men gan vẫn còn giá trị lâm sàng để kết luận"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.OK, approve1Res.StatusCode);

            Db.ChangeTracker.Clear();
            var snap1Visit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.NotNull(snap1Visit.OverdueReturnResultSnapshot);
            Assert.Equal(64, snap1Visit.OverdueReturnResultSnapshot!.Length);
            Assert.True(await Db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.HospitalId == fx.HospitalId &&
                a.Action == "OVERDUE_RETURN_RESULTS_CLINICALLY_APPROVED" &&
                a.ActorUserId == fx.Doctor1UserId));

            await using (var dxOkScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = dxOkScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.RecordDiagnosisAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordDiagnosisRequest(
                        PrimaryIcd10Code: fx.Icd10Code,
                        ClinicalConclusion: "Viêm dạ dày ổn định, chức năng gan bình thường.",
                        IsConfirmed: true));
            }

            // Invalidation 1: CLS amends DiagnosticResult -> snapshot invalidated -> CompleteVisit blocked 409!
            await using (var amendScope = _app.Services.CreateAsyncScope())
            {
                var resultSvc = amendScope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                await resultSvc.UpdateDiagnosticResultAsync(
                    fx.DiagStaff1UserId,
                    labOrderCode,
                    new UpdateDiagnosticResultRequest(
                        ResultText: "AST 25 U/L, ALT 27 U/L (đính chính đơn vị máy).",
                        Conclusion: "Men gan bình thường sau đính chính.",
                        AmendmentReason: "Đính chính sai số hiệu chuẩn máy sinh hóa"));
            }

            await using (var inv1Scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = inv1Scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var inv1Ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CompleteVisitRequest(DischargeInstructions: "Thử hoàn tất sau khi sửa kết quả")));
                Assert.Contains("OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED", inv1Ex.Message, StringComparison.Ordinal);
            }

            // Invalidation 2: Doctor1 re-approves, then adds an optional DiagnosticOrder -> snapshot cleared -> CompleteVisit blocked 409!
            var approve2Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/approve-overdue-results",
                new ApproveOverdueReturnResultsRequest(Reason: "Đã xem lại bản đính chính kết quả xét nghiệm men gan"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.OK, approve2Res.StatusCode);

            string optOrderCode;
            await using (var inv2Scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = inv2Scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var optCreated = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        ServiceId: fx.ImagingServiceId,
                        Instruction: "Siêu âm kiểm tra tùy chọn",
                        RequiredForConclusion: false,
                        TransitionToAwaitingResults: false));
                optOrderCode = optCreated.CreatedDiagnosticOrders![0].OrderCode;

                var selSvc = inv2Scope.ServiceProvider.GetRequiredService<IDiagnosticOrderSelectionService>();
                await selSvc.DeclineOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    optOrderCode,
                    new DeclineDiagnosticOrderRequestDto(Reason: "Bệnh nhân từ chối siêu âm tùy chọn"));

                var inv2Ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(
                        fx.Doctor1UserId,
                        visit.VisitCode!,
                        new CompleteVisitRequest(DischargeInstructions: "Thử hoàn tất sau khi phát sinh chỉ định mới")));
                Assert.Contains("OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED", inv2Ex.Message, StringComparison.Ordinal);
            }

            // Invalidation 3: Doctor1 re-approves, then Visit moves to D+11 and is reassigned to Doctor2 -> Doctor2 must re-approve!
            var approve3Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/approve-overdue-results",
                new ApproveOverdueReturnResultsRequest(Reason: "Xác nhận lại kết quả sau khi hủy chỉ định siêu âm tùy chọn"),
                fx.Doctor1UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.OK, approve3Res.StatusCode);

            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Unspecified));
            await Db.Doctors.Where(d => d.Id == fx.Doctor1Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false));

            await PostResumeAsync(
                fx,
                visit.VisitCode!,
                new ResumeCrossDayVisitRequestDto(
                    Target: "RETURN_CONSULTATION",
                    ReassignDoctorId: fx.Doctor2Id,
                    TargetServicePointId: fx.ExamSp2Id,
                    Notes: "BS Một nghỉ phép, điều phối sang BS Hai khám kết luận"));

            var callDoc2Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{fx.ExamSp2Id}/call-next",
                new { notes = "Gọi bệnh nhân vào PK 102 gặp BS Hai" },
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, callDoc2Res.StatusCode);

            await using (var inv3Scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = inv3Scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var wsDoc2 = await consultSvc.OpenWorkspaceAsync(fx.Doctor2UserId, visit.VisitCode!);
                Assert.True(wsDoc2.Visit.IsReturnOverdue);
                Assert.False(wsDoc2.Visit.IsOverdueReturnApproved);

                var inv3Ex = await Assert.ThrowsAsync<ConflictException>(() =>
                    consultSvc.CompleteVisitAsync(
                        fx.Doctor2UserId,
                        visit.VisitCode!,
                        new CompleteVisitRequest(DischargeInstructions: "BS Hai thử hoàn tất khi chưa tự xác nhận kết quả quá hạn")));
                Assert.Contains("OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED", inv3Ex.Message, StringComparison.Ordinal);
            }

            // Doctor2 approves current snapshot, issues Rx, and completes the Visit!
            var approveDoc2Res = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/doctor/consultation/{visit.VisitCode}/approve-overdue-results",
                new ApproveOverdueReturnResultsRequest(Reason: "BS Hai trực tiếp đối chiếu lâm sàng và xác nhận kết quả xét nghiệm còn giá trị"),
                fx.Doctor2UserId,
                RoleNames.Doctor));
            Assert.Equal(HttpStatusCode.OK, approveDoc2Res.StatusCode);

            await using (var finishScope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = finishScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                var issuedRx = await consultSvc.CreatePrescriptionAsync(
                    fx.Doctor2UserId,
                    visit.VisitCode!,
                    new CreatePrescriptionRequest(
                        IssueImmediately: true,
                        Items:
                        [
                            new CreatePrescriptionItemRequest(
                                MedicineId: fx.MedicineId,
                                PrescribedQuantity: 14,
                                SingleDoseQuantity: 1,
                                SingleDoseUnit: "viên",
                                Frequency: "1 lần/ngày",
                                DurationDays: 14,
                                AdministrationRoute: "Uống",
                                Instructions: "Uống trước ăn sáng 30 phút"),
                        ]));
                Assert.Equal("ISSUED", issuedRx.ClinicalContext.PrescriptionSummary.LatestPrescriptionStatus);

                var completed = await consultSvc.CompleteVisitAsync(
                    fx.Doctor2UserId,
                    visit.VisitCode!,
                    new CompleteVisitRequest(DischargeInstructions: "Dùng thuốc theo đơn và tái khám sau 2 tuần."));
                Assert.Equal("COMPLETED", completed.Visit.Status);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 7. BUG-05 (PE-07): LEGACY VISIT CHARGE ASSESSOR DELEGATION IN RESUME-CONTEXT
    // =========================================================================

    [Fact]
    public async Task Bug05_Pe07_LegacyVisitExamPaidWithNewUnpaidDiagnosticOrder_ResumeContextReportsExactUnpaidAmountAndUnpricedNull()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            var nowLocal = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified);
            _clock.SetVietnamLocalTime(nowLocal);

            // Seed pre-023 legacy visit: LEGACY_VISIT PAID payment (150k, VisitChargeId = null, 0 PaymentAllocations)
            var visit = new Visit
            {
                VisitCode = $"VE1-PE07-{fx.Suffix}",
                PatientId = fx.PatientId,
                AssignedDoctorId = fx.Doctor1Id,
                ExaminationRoomId = fx.ExamRoom1Id,
                Status = "AWAITING_RESULTS",
                CheckedInAt = nowLocal.AddDays(-1),
                LastPresentDate = DateOnly.FromDateTime(nowLocal.AddDays(-1)),
                PresenceState = VisitPresenceStates.Left,
                ReasonForVisit = "Khám ngoại trú dữ liệu legacy trước 023",
            };
            Db.Visits.Add(visit);
            await Db.SaveChangesAsync();

            var examCharge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 150_000m, nowLocal.AddDays(-1));
            Db.VisitCharges.Add(examCharge);

            var legacyExamPayment = new Payment
            {
                VisitId = visit.Id,
                VisitChargeId = null,
                PaymentScope = PaymentScopes.LegacyVisit,
                PaymentMethod = PaymentMethods.Cash,
                CollectedByStaffId = fx.Reception1StaffId,
                IdempotencyKey = $"leg-pe07-{fx.Suffix}",
                PaymentCode = $"LEG{visit.Id}",
                Amount = 150_000m,
                Status = PaymentStatuses.Paid,
                CreatedAt = nowLocal.AddDays(-1),
                PaidAt = nowLocal.AddDays(-1),
            };
            Db.Payments.Add(legacyExamPayment);

            var newDiagOrder = new DiagnosticOrder
            {
                OrderCode = $"ORD-PE07-{fx.Suffix}",
                VisitId = visit.Id,
                ServiceId = fx.LabServiceId,
                OrderingDoctorId = fx.Doctor1Id,
                Status = "ORDERED",
                SelectionStatus = DiagnosticSelectionStatuses.Accepted,
                RequiredForConclusion = true,
                OrderedAt = nowLocal.AddDays(-1).AddMinutes(20),
            };
            Db.DiagnosticOrders.Add(newDiagOrder);
            await Db.SaveChangesAsync();

            var unpaidDiagCharge = VisitCharge.CreateDiagnostic(visit.Id, newDiagOrder.Id, fx.LabServiceId, 200_000m, nowLocal.AddDays(-1).AddMinutes(20));
            Db.VisitCharges.Add(unpaidDiagCharge);
            await Db.SaveChangesAsync();

            // Resume-context MUST report PayableTotal = 350k, RecognizedPaidTotal = 150k, AmountDue = 200k (NOT 0!)
            var ctxRes = await _client.SendAsync(AuthedGet(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume-context",
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, ctxRes.StatusCode);
            var ctx = await ctxRes.Content.ReadFromJsonAsync<VisitResumeContextResponseDto>(WebJson);
            Assert.NotNull(ctx);
            Assert.Equal(350_000m, ctx!.ChargesSummary.PayableTotal);
            Assert.Equal(150_000m, ctx.ChargesSummary.RecognizedPaidTotal);
            Assert.Equal(200_000m, ctx.ChargesSummary.AmountDue);
            Assert.False(ctx.DiagnosticOrders.Single(o => o.OrderId == newDiagOrder.Id).IsPaymentSettled);

            // Adding an unpriced active charge -> AmountDue MUST be null (never 0m!)
            var unpricedOrder = new DiagnosticOrder
            {
                OrderCode = $"ORD-UNP-{fx.Suffix}",
                VisitId = visit.Id,
                ServiceId = fx.ImagingServiceId,
                OrderingDoctorId = fx.Doctor1Id,
                Status = "ORDERED",
                SelectionStatus = DiagnosticSelectionStatuses.Accepted,
                RequiredForConclusion = false,
                OrderedAt = nowLocal,
            };
            Db.DiagnosticOrders.Add(unpricedOrder);
            await Db.SaveChangesAsync();

            Db.VisitCharges.Add(new VisitCharge
            {
                VisitId = visit.Id,
                DiagnosticOrderId = unpricedOrder.Id,
                ServiceId = fx.ImagingServiceId,
                ChargeType = VisitChargeTypes.Diagnostic,
                Status = VisitChargeStatuses.Active,
                PaymentRequirement = "REQUIRED",
                UnitPrice = null,
                CreatedAt = nowLocal,
            });
            await Db.SaveChangesAsync();

            var unpricedCtxRes = await _client.SendAsync(AuthedGet(
                $"/api/v1/reception/visits/{visit.VisitCode}/resume-context",
                fx.Reception1UserId,
                RoleNames.Reception));
            Assert.Equal(HttpStatusCode.OK, unpricedCtxRes.StatusCode);
            var unpricedCtx = await unpricedCtxRes.Content.ReadFromJsonAsync<VisitResumeContextResponseDto>(WebJson);
            Assert.NotNull(unpricedCtx);
            Assert.Null(unpricedCtx!.ChargesSummary.AmountDue);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // 8. DUPLICATE TRANSFER (PE-02): DISTINCT EXTERNAL ID ON PAID QR
    // =========================================================================

    [Fact]
    public async Task DuplicateTransfer_Pe02_SameExternalIdIgnored_WhileNewExternalIdOnPaidQrRecordsDuplicateReconcileAuditAndFlagWithoutMutatingPaid()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
            var visit = await CreateCheckedInVisitAsync(fx, "E1PE02", examPrice: 150_000m, payExamNow: true);

            long labOrderId;
            long imgOrderId;
            await using (var scope = _app.Services.CreateAsyncScope())
            {
                var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
                await consultSvc.RecordClinicalExaminationAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new RecordClinicalExaminationRequest(ClinicalFindings: "Khám ổn định.", TreatmentPlan: "Chỉ định 2 CLS."));
                var created = await consultSvc.CreateDiagnosticOrderAsync(
                    fx.Doctor1UserId,
                    visit.VisitCode!,
                    new CreateDiagnosticOrderRequest(
                        TransitionToAwaitingResults: true,
                        Items:
                        [
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.LabServiceId, Instruction: "XN máu", RequiredForConclusion: true),
                            new CreateDiagnosticOrderItemRequest(ServiceId: fx.ImagingServiceId, Instruction: "Siêu âm bụng", RequiredForConclusion: false),
                        ]));
                labOrderId = created.CreatedDiagnosticOrders![0].OrderId;
                imgOrderId = created.CreatedDiagnosticOrders[1].OrderId;
            }

            Db.ChangeTracker.Clear();
            var labCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == labOrderId);
            var imgCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == imgOrderId);

            // Patient creates QR for Lab ONLY (120,000 VND) while Imaging (240,000 VND) stays UNPAID
            var qrRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/patients/visits/{visit.VisitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [labCharge.Id], IdempotencyKey: $"pe02-lab-{fx.Suffix}"),
                fx.PatientUserId,
                RoleNames.Patient));
            Assert.Equal(HttpStatusCode.Created, qrRes.StatusCode);
            var qrDto = await qrRes.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson);
            Assert.NotNull(qrDto);

            // 1) First transfer (ext1) -> PAID
            var ext1 = NextWebhookId();
            var wh1Res = await PostWebhookAsync(ext1, 120_000m, qrDto!.PaymentCode);
            Assert.Equal(HttpStatusCode.OK, wh1Res.StatusCode);

            Db.ChangeTracker.Clear();
            var paidBefore = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == qrDto.PaymentId);
            var ext1Event = await Db.PaymentGatewayEvents.AsNoTracking()
                .SingleAsync(e => e.PaymentId == qrDto.PaymentId && e.ExternalId == ext1.ToString());
            Assert.Equal("PAID", ext1Event.Outcome);
            Assert.Equal(PaymentStatuses.Paid, paidBefore.Status);
            Assert.NotNull(paidBefore.PaidAt);

            // 2) Same externalId replay (ext1) -> IGNORED (no duplicate event row), HasReconciliationItems == false, 0 duplicate audits
            var whReplayRes = await PostWebhookAsync(ext1, 120_000m, qrDto.PaymentCode);
            Assert.Equal(HttpStatusCode.OK, whReplayRes.StatusCode);
            Assert.Equal(1, await Db.PaymentGatewayEvents.AsNoTracking().CountAsync(e => e.PaymentId == qrDto.PaymentId));

            var chargesAfterReplay = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            Assert.False(chargesAfterReplay.HasReconciliationItems);
            Assert.Equal(0, await Db.AuditLogs.AsNoTracking().CountAsync(a =>
                a.HospitalId == fx.HospitalId &&
                a.Action == "LATE_DUPLICATE_TRANSFER_DETECTED"));

            // 3) Distinct second transfer (ext2 != ext1) on same PaymentCode -> DUPLICATE_RECONCILE
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 5, 0, DateTimeKind.Unspecified));
            var ext2 = NextWebhookId();
            var wh2Res = await PostWebhookAsync(ext2, 120_000m, qrDto.PaymentCode);
            Assert.Equal(HttpStatusCode.OK, wh2Res.StatusCode);

            // Verify PAID Payment & Allocation were NOT mutated, gateway event + audit were persisted,
            // HasReconciliationItems == true, and sibling Imaging charge remains UNPAID!
            Db.ChangeTracker.Clear();
            var paidAfter = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == qrDto.PaymentId);
            var allocAfter = await Db.PaymentAllocations.AsNoTracking().SingleAsync(a => a.PaymentId == qrDto.PaymentId);
            var dupEvent = await Db.PaymentGatewayEvents.AsNoTracking()
                .SingleAsync(e => e.PaymentId == qrDto.PaymentId && e.ExternalId == ext2.ToString());

            Assert.Equal(PaymentStatuses.Paid, paidAfter.Status);
            Assert.Equal(paidBefore.PaidAt, paidAfter.PaidAt);
            Assert.Equal(PaymentAllocationStatuses.Paid, allocAfter.Status);
            Assert.Equal(SePayWebhookService.DuplicateChargeReconcileOutcome, dupEvent.Outcome);
            Assert.Equal(1, await Db.AuditLogs.AsNoTracking().CountAsync(a =>
                a.HospitalId == fx.HospitalId &&
                a.Action == "LATE_DUPLICATE_TRANSFER_DETECTED"));

            var chargesAfterDup = await GetChargesAsPatientAsync(fx, visit.VisitCode!);
            Assert.True(chargesAfterDup.HasReconciliationItems);

            var labLine = chargesAfterDup.Charges.Single(c => c.ChargeId == labCharge.Id);
            var imgLine = chargesAfterDup.Charges.Single(c => c.ChargeId == imgCharge.Id);
            Assert.Equal("PAID", labLine.LineSettlementStatus);
            Assert.Equal("UNPAID", imgLine.LineSettlementStatus);
            Assert.True(imgLine.CanPayByQr);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // BARRIER CONCURRENCY HELPER & FIXTURE INFRASTRUCTURE
    // =========================================================================

    private static async Task RunPostLockReassignmentBarrierAsync(
        string connectionString,
        long visitId,
        long newDoctorId,
        long newRoomId,
        Func<Task> doctorAAction)
    {
        await using var lockConn = new NpgsqlConnection(connectionString);
        await lockConn.OpenAsync();
        await using var tx = await lockConn.BeginTransactionAsync();

        await using (var lockCmd = new NpgsqlCommand("SELECT id FROM visits WHERE id = @visitId FOR UPDATE;", lockConn, tx))
        {
            lockCmd.Parameters.AddWithValue("visitId", visitId);
            await lockCmd.ExecuteScalarAsync();
        }

        var doctorTask = Task.Run(async () => await Assert.ThrowsAsync<ForbiddenException>(doctorAAction));

        // Wait until Doctor A passes pre-lock reads and blocks on PostgreSQL row lock (`wait_event_type = 'Lock'`)
        await using (var obsConn = new NpgsqlConnection(connectionString))
        {
            await obsConn.OpenAsync();
            var deadline = DateTime.UtcNow.AddSeconds(8);
            var blockedOnLock = false;
            while (DateTime.UtcNow < deadline)
            {
                if (doctorTask.IsCompleted)
                {
                    break;
                }

                await using var waitCmd = new NpgsqlCommand(
                    """
                    SELECT COUNT(*)
                    FROM pg_stat_activity
                    WHERE datname = current_database()
                      AND state = 'active'
                      AND wait_event_type = 'Lock'
                      AND pid <> pg_backend_pid();
                    """,
                    obsConn);
                var waiters = (long)(await waitCmd.ExecuteScalarAsync())!;
                if (waiters >= 1)
                {
                    blockedOnLock = true;
                    break;
                }

                await Task.Delay(15);
            }

            Assert.True(blockedOnLock, "Expected Doctor A action to reach FOR UPDATE lock wait inside TOCTOU window.");
        }

        await using (var reassignCmd = new NpgsqlCommand(
            "UPDATE visits SET assigned_doctor_id = @docId, examination_room_id = @roomId WHERE id = @visitId;",
            lockConn,
            tx))
        {
            reassignCmd.Parameters.AddWithValue("docId", newDoctorId);
            reassignCmd.Parameters.AddWithValue("roomId", newRoomId);
            reassignCmd.Parameters.AddWithValue("visitId", visitId);
            await reassignCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        await doctorTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static long NextWebhookId() => Interlocked.Increment(ref _webhookSeq);

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

    private async Task<HttpResponseMessage> PostWebhookAsync(long id, decimal amount, string content)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = JsonContent.Create(new
            {
                id,
                gateway = "Vietcombank",
                transactionDate = "2026-11-10 09:05:00",
                accountNumber = "0123456789",
                code = (string?)null,
                content,
                transferType = "in",
                transferAmount = amount,
                accumulated = amount,
                subAccount = (string?)null,
                referenceCode = $"FT-E1-{id}",
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Apikey", WebhookApiKey);
        return await _client.SendAsync(req);
    }

    private async Task<VisitChargesResponseDto> GetChargesAsPatientAsync(StageE1Fixture fx, string visitCode)
    {
        var res = await _client.SendAsync(AuthedGet(
            $"/api/v1/patients/visits/{visitCode}/charges",
            fx.PatientUserId,
            RoleNames.Patient));
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<VisitChargesResponseDto>(body, WebJson)!;
    }

    private async Task<ResumeCrossDayVisitResponseDto> PostResumeAsync(
        StageE1Fixture fx,
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
        StageE1Fixture fx,
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
                TreatmentPlan: "Theo dõi cận lâm sàng Stage E1."));
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

    private async Task<long> PayDiagnosticOrderByCashAsync(StageE1Fixture fx, string visitCode, long orderId)
    {
        Db.ChangeTracker.Clear();
        var charge = await Db.VisitCharges.AsNoTracking()
            .SingleAsync(c => c.DiagnosticOrderId == orderId);
        var cashRes = await _client.SendAsync(AuthedPostJson(
            $"/api/v1/reception/visits/{visitCode}/payments/cash",
            new CollectCashPaymentRequestDto(
                VisitChargeIds: [charge.Id],
                IdempotencyKey: $"cash-e1-{orderId}-{Guid.NewGuid():N}"),
            fx.Reception1UserId,
            RoleNames.Reception));
        var body = await cashRes.Content.ReadAsStringAsync();
        Assert.True(cashRes.StatusCode == HttpStatusCode.Created, body);
        var dto = JsonSerializer.Deserialize<CollectCashPaymentResponseDto>(body, WebJson)!;
        return dto.PaymentId;
    }

    private async Task ExecuteDiagnosticOrderThroughUc42Async(StageE1Fixture fx, string orderCode)
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
        StageE1Fixture fx,
        string visitCode,
        long examSpId,
        long doctorUserId)
    {
        Db.ChangeTracker.Clear();
        var currentVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.VisitCode == visitCode);
        if (string.Equals(currentVisit.Status, "AWAITING_RESULTS", StringComparison.OrdinalIgnoreCase))
        {
            await PostResumeAsync(fx, visitCode, new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION"));
            currentVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.VisitCode == visitCode);
        }

        if (string.Equals(currentVisit.Status, "RETURN_CONSULTATION", StringComparison.OrdinalIgnoreCase))
        {
            var callNextRes = await _client.SendAsync(AuthedPostJson(
                $"/api/v1/queue/service-points/{examSpId}/call-next",
                new { notes = "Gọi bệnh nhân vào phòng khám kết luận Stage E1" },
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
                ClinicalConclusion: "Tình trạng ổn định, hoàn tất lượt khám ngoại trú Stage E1.",
                IsConfirmed: true));

        var completed = await consultSvc.CompleteVisitAsync(
            doctorUserId,
            visitCode,
            new CompleteVisitRequest(
                ConfirmNoPrescription: true,
                DischargeInstructions: "Theo dõi tại nhà và tái khám khi cần."));
        Assert.Equal("COMPLETED", completed.Visit.Status);
    }

    private async Task<Visit> CreateCheckedInVisitAsync(
        StageE1Fixture fx,
        string tag,
        decimal examPrice,
        bool payExamNow)
    {
        Db.ChangeTracker.Clear();
        var nowLocal = HospitalClock.GetNow(_clock);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"VE1-{tag}-{fx.Suffix}",
            PatientId = fx.PatientId,
            AssignedDoctorId = fx.Doctor1Id,
            ExaminationRoomId = fx.ExamRoom1Id,
            Status = "CHECKED_IN",
            CheckedInAt = nowLocal.AddMinutes(-10),
            LastPresentDate = today,
            PresenceState = VisitPresenceStates.Present,
            ReasonForVisit = "Khám ngoại trú kiểm thử Stage E1",
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
                IdempotencyKey = $"seed-e1-exam-{visit.Id}-{Guid.NewGuid():N}",
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

    private async Task<StageE1Fixture> CreateIsolatedFixtureAsync()
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
                Code = $"K31.{suffix[..2]}",
                NameVi = "Bệnh lý dạ dày kiểm thử Stage E1",
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
            var spec = new Specialty { SpecialtyCode = $"E1{suffix}", Name = $"Nội Stage E1 {suffix}", IsActive = true };
            Db.Specialties.Add(spec);
            await Db.SaveChangesAsync();
            specialtyId = spec.Id;
        }

        var hospital = new Hospital { HospitalCode = $"E1H{suffix}", Name = $"BV Stage E1 {suffix}", IsActive = true };
        Db.Hospitals.Add(hospital);
        await Db.SaveChangesAsync();

        Db.HospitalPolicies.Add(new HospitalPolicy
        {
            HospitalId = hospital.Id,
            PolicyType = "PAYMENT",
            PolicyKey = "vietqr_account",
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN STAGE E1"}""",
            PolicyVersion = 1,
        });

        var dept = new Department { HospitalId = hospital.Id, DepartmentCode = $"DE1{suffix}", Name = $"Khoa Nội E1 {suffix}", IsActive = true };
        Db.Departments.Add(dept);
        await Db.SaveChangesAsync();

        var examService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"EXE1{suffix}",
            Name = "Khám Nội Stage E1",
            ServiceType = "EXAMINATION",
            IsActive = true,
        };
        var labService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"LBE1{suffix}",
            Name = "Xét nghiệm máu Stage E1",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var imagingService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialtyId,
            ServiceCode = $"IME1{suffix}",
            Name = "Siêu âm tổng quát Stage E1",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        Db.Services.AddRange(examService, labService, imagingService);
        await Db.SaveChangesAsync();

        Db.ServicePrices.AddRange(
            new ServicePrice { ServiceId = examService.Id, PriceAmount = 150_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = labService.Id, PriceAmount = 120_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = imagingService.Id, PriceAmount = 240_000m, EffectiveFrom = seedBaseTime });

        var examRoom1 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"R1E1{suffix}", Name = "PK 101 E1", RoomType = "EXAMINATION", Capacity = 10, Status = "ACTIVE" };
        var examRoom2 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"R2E1{suffix}", Name = "PK 102 E1", RoomType = "EXAMINATION", Capacity = 10, Status = "ACTIVE" };
        var labRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"RLE1{suffix}", Name = "PXN 201 E1", RoomType = "DIAGNOSTIC", Capacity = 2, Status = "ACTIVE" };
        var imgRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"RIE1{suffix}", Name = "PSA 202 E1", RoomType = "DIAGNOSTIC", Capacity = 2, Status = "ACTIVE" };
        Db.HospitalRooms.AddRange(examRoom1, examRoom2, labRoom, imgRoom);
        await Db.SaveChangesAsync();

        var doc1User = new User { Email = $"d1-e1-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var doc2User = new User { Email = $"d2-e1-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var diag1User = new User { Email = $"dg1-e1-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var rec1User = new User { Email = $"rc1-e1-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var patUser = new User { Email = $"pt1-e1-{suffix}@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        Db.Users.AddRange(doc1User, doc2User, diag1User, rec1User, patUser);
        await Db.SaveChangesAsync();

        Db.UserRoles.AddRange(
            new UserRole { UserId = doc1User.Id, RoleId = docRole.Id },
            new UserRole { UserId = doc2User.Id, RoleId = docRole.Id },
            new UserRole { UserId = diag1User.Id, RoleId = diagRole.Id },
            new UserRole { UserId = rec1User.Id, RoleId = recRole.Id },
            new UserRole { UserId = patUser.Id, RoleId = patRole.Id });

        var doc1Staff = new Staff { UserId = doc1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SD1E1{suffix}", FullName = "BS. Một StageE1", Status = "ACTIVE" };
        var doc2Staff = new Staff { UserId = doc2User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SD2E1{suffix}", FullName = "BS. Hai StageE1", Status = "ACTIVE" };
        var diag1Staff = new Staff { UserId = diag1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SG1E1{suffix}", FullName = "KTV. Một StageE1", Status = "ACTIVE" };
        var rec1Staff = new Staff { UserId = rec1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"SRCE1{suffix}", FullName = "TN. Tiếp Đón StageE1", Status = "ACTIVE" };
        Db.Staff.AddRange(doc1Staff, doc2Staff, diag1Staff, rec1Staff);
        await Db.SaveChangesAsync();

        var doc1 = new Doctor { StaffId = doc1Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialtyId, DoctorCode = $"DC1E1{suffix}", ProfessionalTitle = "BS.CKI", IsActive = true };
        var doc2 = new Doctor { StaffId = doc2Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialtyId, DoctorCode = $"DC2E1{suffix}", ProfessionalTitle = "BS.CKI", IsActive = true };
        Db.Doctors.AddRange(doc1, doc2);
        await Db.SaveChangesAsync();

        Db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = examService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = labService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc1.Id, ServiceId = imagingService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc2.Id, ServiceId = examService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc2.Id, ServiceId = labService.Id, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = doc2.Id, ServiceId = imagingService.Id, IsActive = true });

        var examSp1 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom1.Id, DoctorId = doc1.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "PK 101 E1", IsActive = true };
        var examSp2 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom2.Id, DoctorId = doc2.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "PK 102 E1", IsActive = true };
        var labSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = labRoom.Id, ServiceId = labService.Id, PointType = "DIAGNOSTIC", Name = "PXN 201 E1", IsActive = true };
        var imgSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = imgRoom.Id, ServiceId = imagingService.Id, PointType = "DIAGNOSTIC", Name = "PSA 202 E1", IsActive = true };
        Db.QueueServicePoints.AddRange(examSp1, examSp2, labSp, imgSp);

        var medicine = new Medicine
        {
            HospitalId = hospital.Id,
            MedicineCode = $"MEDE1{suffix}",
            Name = $"Pantoprazol 40mg Stage E1 {suffix}",
            ActiveIngredient = "Pantoprazol",
            Strength = "40mg",
            DosageForm = "Viên nén",
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
            BatchNumber = $"LOTE1-{suffix}",
            ReceivedAt = seedBaseTime,
            ExpiryDate = new DateOnly(2028, 12, 31),
            QuantityOnHand = 500m,
            IsBlocked = false,
        });

        var patient = new Patient
        {
            UserId = patUser.Id,
            PatientCode = $"PE1{suffix}",
            FullName = "Nguyễn Văn Bệnh Nhân E1",
            Gender = "MALE",
            Phone = "0909888777",
            IdentificationHash = $"e1-id-{suffix}",
            Status = "ACTIVE",
        };
        Db.Patients.Add(patient);
        await Db.SaveChangesAsync();

        return new StageE1Fixture(
            Suffix: suffix,
            HospitalId: hospital.Id,
            DeptId: dept.Id,
            ExamRoom1Id: examRoom1.Id,
            ExamRoom2Id: examRoom2.Id,
            LabRoomId: labRoom.Id,
            ImagingRoomId: imgRoom.Id,
            AllRoomIds: [examRoom1.Id, examRoom2.Id, labRoom.Id, imgRoom.Id],
            ExamSp1Id: examSp1.Id,
            ExamSp2Id: examSp2.Id,
            LabSpId: labSp.Id,
            ImagingSpId: imgSp.Id,
            AllSpIds: [examSp1.Id, examSp2.Id, labSp.Id, imgSp.Id],
            ExamServiceId: examService.Id,
            LabServiceId: labService.Id,
            ImagingServiceId: imagingService.Id,
            AllServiceIds: [examService.Id, labService.Id, imagingService.Id],
            MedicineId: medicine.Id,
            Doctor1UserId: doc1User.Id,
            Doctor1Id: doc1.Id,
            Doctor2UserId: doc2User.Id,
            Doctor2Id: doc2.Id,
            DiagStaff1UserId: diag1User.Id,
            Reception1UserId: rec1User.Id,
            Reception1StaffId: rec1Staff.Id,
            PatientUserId: patUser.Id,
            PatientId: patient.Id,
            AllUserIds: [doc1User.Id, doc2User.Id, diag1User.Id, rec1User.Id, patUser.Id],
            AllStaffIds: [doc1Staff.Id, doc2Staff.Id, diag1Staff.Id, rec1Staff.Id],
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

    private async Task CleanupIsolatedFixtureAsync(StageE1Fixture fx)
    {
        Db.ChangeTracker.Clear();

        var visitIds = await Db.Visits
            .Where(v => v.PatientId == fx.PatientId)
            .Select(v => v.Id)
            .ToListAsync();

        await Db.Notifications.Where(n =>
            (n.VisitId.HasValue && visitIds.Contains(n.VisitId.Value)) ||
            n.PatientId == fx.PatientId).ExecuteDeleteAsync();

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

        await Db.QueueFairnessStates.Where(f => fx.AllSpIds.Contains(f.ServicePointId)).ExecuteDeleteAsync();
        await Db.QueueEntries.Where(q =>
            (q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)) ||
            (q.ServicePointId.HasValue && fx.AllSpIds.Contains(q.ServicePointId.Value))).ExecuteDeleteAsync();

        await Db.AuditLogs.Where(a =>
            a.HospitalId == fx.HospitalId ||
            (a.ActorUserId.HasValue && fx.AllUserIds.Contains(a.ActorUserId.Value))).ExecuteDeleteAsync();

        if (visitIds.Count > 0)
        {
            await Db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
        }

        await Db.MedicineBatches.Where(mb => mb.MedicineId == fx.MedicineId).ExecuteDeleteAsync();
        await Db.Medicines.Where(m => m.Id == fx.MedicineId).ExecuteDeleteAsync();
        await Db.QueueServicePoints.Where(sp => fx.AllSpIds.Contains(sp.Id)).ExecuteDeleteAsync();
        await Db.DoctorServiceAuthorizations.Where(a => a.DoctorId == fx.Doctor1Id || a.DoctorId == fx.Doctor2Id).ExecuteDeleteAsync();
        await Db.ServicePrices.Where(sp => sp.ServiceId.HasValue && fx.AllServiceIds.Contains(sp.ServiceId.Value)).ExecuteDeleteAsync();
        await Db.Patients.Where(p => p.Id == fx.PatientId).ExecuteDeleteAsync();
        await Db.Doctors.Where(d => d.Id == fx.Doctor1Id || d.Id == fx.Doctor2Id).ExecuteDeleteAsync();
        await Db.Staff.Where(s => fx.AllStaffIds.Contains(s.Id)).ExecuteDeleteAsync();
        await Db.UserRoles.Where(ur => fx.AllUserIds.Contains(ur.UserId)).ExecuteDeleteAsync();
        await Db.Users.Where(u => fx.AllUserIds.Contains(u.Id)).ExecuteDeleteAsync();
        await Db.HospitalRooms.Where(r => fx.AllRoomIds.Contains(r.Id)).ExecuteDeleteAsync();
        await Db.Services.Where(s => fx.AllServiceIds.Contains(s.Id)).ExecuteDeleteAsync();
        await Db.Departments.Where(d => d.Id == fx.DeptId).ExecuteDeleteAsync();
        await Db.HospitalPolicies.Where(p => p.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await Db.Hospitals.Where(h => h.Id == fx.HospitalId).ExecuteDeleteAsync();

        if (fx.CreatedIcd10Id.HasValue)
        {
            await Db.Icd10Codes.Where(c => c.Id == fx.CreatedIcd10Id.Value).ExecuteDeleteAsync();
        }
    }

    private static async Task<Dictionary<string, long>> QueryPreflightViolationsAsync(
        string connectionString,
        string preflightSql)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(preflightSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetInt64(1);
        }

        return result;
    }

    private static async Task AssertPreflight026ZeroViolationsAsync(string connectionString, string preflightSql)
    {
        var checks = await QueryPreflightViolationsAsync(connectionString, preflightSql);
        Assert.Equal(4, checks.Count);
        foreach (var (checkName, violations) in checks)
        {
            Assert.True(violations == 0, $"Preflight 026 check '{checkName}' found {violations} violations.");
        }
    }

    private static async Task ApplyMigrationsUpTo025Async(string connectionString, string repoRoot)
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

                if (string.CompareOrdinal(name, "026_") >= 0)
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
            ?? "Host=localhost;Port=5433;Database=medflow_test_stage_e1;Username=postgres;Password=medflow_dev";
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

    private sealed record StageE1Fixture(
        string Suffix,
        long HospitalId,
        long DeptId,
        long ExamRoom1Id,
        long ExamRoom2Id,
        long LabRoomId,
        long ImagingRoomId,
        IReadOnlyList<long> AllRoomIds,
        long ExamSp1Id,
        long ExamSp2Id,
        long LabSpId,
        long ImagingSpId,
        IReadOnlyList<long> AllSpIds,
        long ExamServiceId,
        long LabServiceId,
        long ImagingServiceId,
        IReadOnlyList<long> AllServiceIds,
        long MedicineId,
        long Doctor1UserId,
        long Doctor1Id,
        long Doctor2UserId,
        long Doctor2Id,
        long DiagStaff1UserId,
        long Reception1UserId,
        long Reception1StaffId,
        long PatientUserId,
        long PatientId,
        IReadOnlyList<long> AllUserIds,
        IReadOnlyList<long> AllStaffIds,
        string Icd10Code,
        long? CreatedIcd10Id);

    private sealed class TempStageE1Database : IAsyncDisposable
    {
        private readonly string _adminConn;
        public string DatabaseName { get; }
        public string ConnectionString { get; }

        private TempStageE1Database(string adminConn, string dbName, string connStr)
        {
            _adminConn = adminConn;
            DatabaseName = dbName;
            ConnectionString = connStr;
        }

        public static async Task<TempStageE1Database> CreateAsync(string adminConn)
        {
            var dbName = $"medflow_se1_{Guid.NewGuid():N}"[..27];
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
            return new TempStageE1Database(adminConn, dbName, target);
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
