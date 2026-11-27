using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.Infrastructure.BackgroundJobs;
using MedFlow.Infrastructure.Persistence;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace MedFlow.IntegrationTests.EndToEnd;

/// <summary>
/// Stage D (Phase 07 / GH-850 / ADR-0059 / ADR-0060) Cross-Module Deterministic Barrier Concurrency &amp;
/// SignalR Post-Commit / Rollback Integrity Suite against clean-slate <c>medflow_test_p07</c>.
/// Proves <c>0</c> <c>40P01</c> deadlocks, <c>0</c> unhandled server errors, strict final invariants,
/// and <c>0</c> fake SignalR events on rolled-back or non-unlocking reconciliation paths.
/// </summary>
public sealed class Phase07CrossModuleConcurrencyAndSignalRIntegrityTests
    : IClassFixture<TestWebAppFactory>, IDisposable
{
    private static long _webhookSeq = 8_200_000;

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private long _utcTicks = initialUtc.UtcTicks;

        public override DateTimeOffset GetUtcNow()
            => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void SetVietnamLocalTime(DateTime vnLocalUnspecified)
        {
            var unspecified = DateTime.SpecifyKind(vnLocalUnspecified, DateTimeKind.Unspecified);
            var dto = new DateTimeOffset(unspecified, TimeSpan.FromHours(7));
            Interlocked.Exchange(ref _utcTicks, dto.UtcDateTime.Ticks);
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class RecordingRealtimeNotificationPublisher : IRealtimeNotificationPublisher
    {
        public ConcurrentQueue<QueueChangedNotification> QueueEvents { get; } = new();
        public ConcurrentQueue<VisitPaymentChangedNotification> PaymentEvents { get; } = new();

        public void Clear()
        {
            while (QueueEvents.TryDequeue(out _)) { }
            while (PaymentEvents.TryDequeue(out _)) { }
        }

        public Task PublishQueueChangedAsync(
            QueueChangedNotification notification,
            CancellationToken ct = default)
        {
            QueueEvents.Enqueue(notification);
            return Task.CompletedTask;
        }

        public Task PublishVisitPaymentChangedAsync(
            VisitPaymentChangedNotification notification,
            CancellationToken ct = default)
        {
            PaymentEvents.Enqueue(notification);
            return Task.CompletedTask;
        }
    }

    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 11, 26, 2, 0, 0, TimeSpan.Zero));
    private readonly RecordingRealtimeNotificationPublisher _spyPublisher = new();
    private readonly WebApplicationFactory<Program> _app;

    public Phase07CrossModuleConcurrencyAndSignalRIntegrityTests(TestWebAppFactory baseFactory)
    {
        _app = baseFactory.WithWebHostBuilder(builder =>
        {
            // Same virtual-clock rationale as Phase07FullOutpatientPayBeforeServiceE2ETests: JWTs are
            // minted against MutableTimeProvider, so CloseOnAuthenticationExpiration (real system clock)
            // would see pre-expired tokens and abort hub connections nondeterministically. Production
            // default stays enabled; the override is scoped to this suite's factory instance only.
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Realtime:CloseOnAuthenticationExpiration"] = "false",
                }));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);

                services.RemoveAll<IRealtimeNotificationPublisher>();
                services.AddSingleton<IRealtimeNotificationPublisher>(_spyPublisher);
            });
        });
    }

    public void Dispose()
    {
        _app.Dispose();
    }

    // =========================================================================
    // TEST 1: Cross-Module Appointment vs Check-In (Merged Phase 04 & 05)
    // Multi-iteration races: CheckIn vs Cancel, CheckIn vs Reschedule,
    // CheckIn vs AutoNoShow -> 0 40P01 deadlocks, 0 HTTP 500s, 0 orphan VisitCharges!
    // =========================================================================

    [Fact]
    public async Task CrossModuleAppointmentVsCheckIn_MergedPhase04And05_ZeroDeadlocksAndZeroOrphanCharges()
    {
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            const int iterationsPerRace = 3;

            // --- Race Pair A: CheckIn vs Cancel ---
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified));
            for (var iter = 0; iter < iterationsPerRace; iter++)
            {
                var (patUserId, patId) = await CreatePatientAsync(fx, $"ca-{iter}");
                var slot = await CreateSlotAsync(
                    fx,
                    fx.Room1Id,
                    fx.Doctor1Id,
                    new DateTime(2026, 11, 26, 14, 0, 0).AddMinutes(iter),
                    new DateTime(2026, 11, 26, 14, 30, 0).AddMinutes(iter));
                var appt = await SeedBookedAppointmentAsync(fx, patId, patUserId, slot.Id);

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var checkInTask = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<ICheckInScheduledPatientService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await svc.CheckInScheduledPatientAsync(
                            fx.ReceptionUserId,
                            appt.Id.ToString(CultureInfo.InvariantCulture),
                            new CheckInScheduledPatientRequest(IsIdentityVerified: true));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                var cancelTask = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IAppointmentCancelService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await svc.CancelMyAppointmentAsync(
                            patUserId,
                            appt.Id.ToString(CultureInfo.InvariantCulture),
                            new CancelAppointmentRequest { CancellationReason = "Bệnh nhân hủy lịch đồng thời với check-in" },
                            "127.0.0.1");
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                go.SetResult();

                var outcomes = await Task.WhenAll(checkInTask, cancelTask);
                AssertZeroDeadlockOrUnhandledServerFailure(outcomes);
                Assert.Equal(1, outcomes.Count(e => e is null));

                await AssertConsistentAppointmentVisitAndChargesAsync(appt.Id, patId);
            }

            // --- Race Pair B: CheckIn vs Reschedule ---
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 9, 15, 0, DateTimeKind.Unspecified));
            for (var iter = 0; iter < iterationsPerRace; iter++)
            {
                var (patUserId, patId) = await CreatePatientAsync(fx, $"rs-{iter}");
                var curSlot = await CreateSlotAsync(
                    fx,
                    fx.Room1Id,
                    fx.Doctor1Id,
                    new DateTime(2026, 11, 26, 15, 0, 0).AddMinutes(iter),
                    new DateTime(2026, 11, 26, 15, 30, 0).AddMinutes(iter));
                var nextSlot = await CreateSlotAsync(
                    fx,
                    fx.Room1Id,
                    fx.Doctor1Id,
                    new DateTime(2026, 11, 27, 9, 0, 0).AddMinutes(iter),
                    new DateTime(2026, 11, 27, 9, 30, 0).AddMinutes(iter));
                var appt = await SeedBookedAppointmentAsync(fx, patId, patUserId, curSlot.Id);

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var checkInTask = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<ICheckInScheduledPatientService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await svc.CheckInScheduledPatientAsync(
                            fx.ReceptionUserId,
                            appt.Id.ToString(CultureInfo.InvariantCulture),
                            new CheckInScheduledPatientRequest(IsIdentityVerified: true));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                var rescheduleTask = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IAppointmentRescheduleService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        var dto = await svc.RescheduleAsync(
                            patUserId,
                            appt.Id.ToString(CultureInfo.InvariantCulture),
                            new RescheduleAppointmentRequest
                            {
                                TargetSlotId = nextSlot.Id.ToString(CultureInfo.InvariantCulture),
                                RescheduleNote = "Đổi lịch hẹn đồng thời với tiếp đón check-in",
                            },
                            "127.0.0.1");
                        lock (fx.CreatedAppointmentIds)
                        {
                            fx.CreatedAppointmentIds.Add(long.Parse(dto.Id, CultureInfo.InvariantCulture));
                        }

                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                go.SetResult();

                var outcomes = await Task.WhenAll(checkInTask, rescheduleTask);
                AssertZeroDeadlockOrUnhandledServerFailure(outcomes);
                Assert.Equal(1, outcomes.Count(e => e is null));

                await AssertConsistentAppointmentVisitAndChargesAsync(appt.Id, patId);
            }

            // --- Race Pair C: CheckIn vs AutoNoShow ---
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Unspecified));
            var processor = new AppointmentLifecycleProcessor(
                _app.Services.GetRequiredService<IServiceScopeFactory>(),
                new StaticOptionsMonitor<ScheduleJobsOptions>(new ScheduleJobsOptions { BatchSize = 50 }),
                _clock,
                NullLogger<AppointmentLifecycleProcessor>.Instance);

            for (var iter = 0; iter < iterationsPerRace; iter++)
            {
                var (patUserId, patId) = await CreatePatientAsync(fx, $"ns-{iter}");
                var overdueSlot = await CreateSlotAsync(
                    fx,
                    fx.Room1Id,
                    fx.Doctor1Id,
                    new DateTime(2026, 11, 26, 8, 0, 0).AddMinutes(iter),
                    new DateTime(2026, 11, 26, 8, 30, 0).AddMinutes(iter));
                var appt = await SeedBookedAppointmentAsync(fx, patId, patUserId, overdueSlot.Id);

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var checkInTask = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<ICheckInScheduledPatientService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await svc.CheckInScheduledPatientAsync(
                            fx.ReceptionUserId,
                            appt.Id.ToString(CultureInfo.InvariantCulture),
                            new CheckInScheduledPatientRequest(IsIdentityVerified: true));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                var autoNoShowTask = Task.Run(async () =>
                {
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await processor.ProcessCycleAsync(CancellationToken.None);
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                go.SetResult();

                var outcomes = await Task.WhenAll(checkInTask, autoNoShowTask);
                AssertZeroDeadlockOrUnhandledServerFailure(outcomes);
                Assert.Null(await autoNoShowTask);

                await AssertConsistentAppointmentVisitAndChargesAsync(appt.Id, patId);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // TEST 2: Cross-Module Room/Doctor Transfer vs Doctor 1 Consultation Write & Order
    // Verifies pre-transfer write stays valid if Doctor 1 won first, and post-transfer
    // write deterministically throws 403 ForbiddenException with 0 orphan orders/charges!
    // =========================================================================

    [Fact]
    public async Task CrossModuleRoomTransferVsDoctorConsultationWriteAndOrder_SerializesOnVisitLock_BlocksTransferredOutDoctor403()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 10, 30, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            for (var iter = 0; iter < 4; iter++)
            {
                var (_, patId) = await CreatePatientAsync(fx, $"tr-{iter}");
                var visit = await SeedWaitingPaidVisitInRoom1Async(fx, patId, $"TR-{iter}");

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var transferTask = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var transferSvc = scope.ServiceProvider.GetRequiredService<IVisitRoomTransferService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await transferSvc.TransferToCompatibleRoomAsync(
                            fx.ReceptionUserId,
                            visit.VisitCode!,
                            new TransferVisitRoomRequest(
                                TargetRoomId: fx.Room2Id,
                                Reason: "Điều phối chuyển buồng khám sang Bác sĩ 2",
                                TargetDoctorId: fx.Doctor2Id),
                            "127.0.0.1");
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                var doctor1WriteTask = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        if (iter % 2 == 0)
                        {
                            await consultSvc.CreateDiagnosticOrderAsync(
                                fx.Doctor1UserId,
                                visit.VisitCode!,
                                new CreateDiagnosticOrderRequest(
                                    ServiceId: fx.DiagServiceId,
                                    Instruction: "Chỉ định CLS đồng thời với điều phối chuyển phòng"));
                        }
                        else
                        {
                            await consultSvc.RecordDiagnosisAsync(
                                fx.Doctor1UserId,
                                visit.VisitCode!,
                                new RecordDiagnosisRequest(
                                    PrimaryIcd10Code: fx.Icd10Code,
                                    ClinicalConclusion: "Chẩn đoán sơ bộ đồng thời với chuyển phòng",
                                    IsConfirmed: false));
                        }

                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                go.SetResult();

                var outcomes = await Task.WhenAll(transferTask, doctor1WriteTask);
                AssertZeroDeadlockOrUnhandledServerFailure(outcomes);

                var transferErr = await transferTask;
                var doc1Err = await doctor1WriteTask;

                await using var verifyScope = _app.Services.CreateAsyncScope();
                var db = verifyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var reloadedVisit = await db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);

                if (transferErr is null)
                {
                    Assert.Equal(fx.Room2Id, reloadedVisit.ExaminationRoomId);
                    Assert.Equal(fx.Doctor2Id, reloadedVisit.AssignedDoctorId);

                    if (doc1Err is null)
                    {
                        // Doctor 1 won the Visit lock BEFORE transfer committed -> pre-transfer clinical write stays valid!
                        if (iter % 2 == 0)
                        {
                            Assert.Equal(1, await db.DiagnosticOrders.CountAsync(o => o.VisitId == visit.Id));
                            Assert.Equal(1, await db.VisitCharges.CountAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Diagnostic));
                        }
                        else
                        {
                            Assert.Equal(1, await db.VisitDiagnoses.CountAsync(d => d.VisitId == visit.Id));
                        }
                    }
                    else
                    {
                        // Transfer won first -> Doctor 1 rejected and created ZERO orphan orders/charges!
                        Assert.True(doc1Err is ForbiddenException or ConflictException);
                        Assert.Equal(0, await db.DiagnosticOrders.CountAsync(o => o.VisitId == visit.Id));
                        Assert.Equal(0, await db.VisitCharges.CountAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Diagnostic));
                    }

                    // Post-transfer invariant: subsequent Doctor 1 writes deterministically throw 403 ForbiddenException with 0 extra rows!
                    var preOrderCount = await db.DiagnosticOrders.CountAsync(o => o.VisitId == visit.Id);
                    var preDiagChargeCount = await db.VisitCharges.CountAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Diagnostic);

                    var consultAfter = verifyScope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                    await Assert.ThrowsAsync<ForbiddenException>(() =>
                        consultAfter.RecordDiagnosisAsync(
                            fx.Doctor1UserId,
                            visit.VisitCode!,
                            new RecordDiagnosisRequest(
                                PrimaryIcd10Code: fx.Icd10Code,
                                ClinicalConclusion: "BS 1 cố ghi sau khi đã chuyển sang BS 2",
                                IsConfirmed: false)));

                    await Assert.ThrowsAsync<ForbiddenException>(() =>
                        consultAfter.CreateDiagnosticOrderAsync(
                            fx.Doctor1UserId,
                            visit.VisitCode!,
                            new CreateDiagnosticOrderRequest(
                                ServiceId: fx.DiagServiceId,
                                Instruction: "BS 1 cố tạo chỉ định sau khi đã chuyển sang BS 2")));

                    Assert.Equal(preOrderCount, await db.DiagnosticOrders.CountAsync(o => o.VisitId == visit.Id));
                    Assert.Equal(preDiagChargeCount, await db.VisitCharges.CountAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Diagnostic));
                }
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // TEST 3: Cross-Module Stage B & Stage C Barrier Races (End-to-End Lock Hierarchy)
    // Verifies 0 deadlocks and strict final invariants across:
    // 1) Single QR vs Grouped QR
    // 2) CASH vs SePay Webhook
    // 3) Decline Paid Optional vs StartDiagnosticExecution
    // 4) Defer vs StartDiagnosticExecution
    // 5) Resume(RETURN_CONSULTATION) vs Finalize UC43
    // 6) TechnicalAbort Mandatory vs CompleteVisit
    // =========================================================================

    [Fact]
    public async Task CrossModuleStageBAndStageCBarrierRaces_VerifyEndToEndLockHierarchyInvariants()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync();
        try
        {
            // --- 3.1: Single QR vs Grouped QR sharing common charge ---
            {
                var (patUserId, patId) = await CreatePatientAsync(fx, "sb-qr");
                var visit = await SeedUnpaidWaitingVisitWithClsOrderAsync(fx, patId, "SB-QR", requiredForConclusion: true);

                long examChargeId;
                long clsChargeId;
                await using (var s = _app.Services.CreateAsyncScope())
                {
                    var db = s.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                    examChargeId = (await db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Examination)).Id;
                    clsChargeId = (await db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Diagnostic)).Id;
                }

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                async Task<Exception?> RunQrAsync(IReadOnlyList<long> chargeIds)
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IGenerateQrPaymentService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await svc.CreateAsync(patUserId, visit.VisitCode!, "127.0.0.1", new GenerateQrPaymentRequestDto(VisitChargeIds: chargeIds));
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                }

                var tSingle = Task.Run(() => RunQrAsync([examChargeId]));
                var tGrouped = Task.Run(() => RunQrAsync([examChargeId, clsChargeId]));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                go.SetResult();

                var errs = await Task.WhenAll(tSingle, tGrouped);
                AssertZeroDeadlockOrUnhandledServerFailure(errs, "3.1 QR-single-vs-grouped");

                await using var checkScope = _app.Services.CreateAsyncScope();
                var checkDb = checkScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                Assert.Equal(1, await checkDb.Payments.CountAsync(p => p.VisitId == visit.Id && p.Status == PaymentStatuses.Pending));
                Assert.Equal(1, await checkDb.PaymentAllocations.CountAsync(pa => pa.VisitChargeId == examChargeId && pa.Status == PaymentAllocationStatuses.Pending));
            }

            // --- 3.2: Reception CASH vs SePay Webhook ---
            {
                var (patUserId, patId) = await CreatePatientAsync(fx, "sb-cw");
                var visit = await SeedUnpaidWaitingVisitWithClsOrderAsync(fx, patId, "SB-CW", requiredForConclusion: true);
                long examChargeId;
                GenerateQrPaymentResponseDto pendingQr;
                await using (var s = _app.Services.CreateAsyncScope())
                {
                    var db = s.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                    examChargeId = (await db.VisitCharges.AsNoTracking().SingleAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Examination)).Id;
                    var qrSvc = s.ServiceProvider.GetRequiredService<IGenerateQrPaymentService>();
                    pendingQr = await qrSvc.CreateAsync(patUserId, visit.VisitCode!, "127.0.0.1", new GenerateQrPaymentRequestDto(VisitChargeIds: [examChargeId]));
                }

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var whExtId = Interlocked.Increment(ref _webhookSeq);

                var tCash = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var cashSvc = scope.ServiceProvider.GetRequiredService<ICollectCashPaymentService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await cashSvc.CollectAsync(
                            fx.ReceptionUserId,
                            visit.VisitCode!,
                            new CollectCashPaymentRequestDto(VisitChargeIds: [examChargeId], IdempotencyKey: $"race-cw-{fx.Tag}"));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                var tWh = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var whSvc = scope.ServiceProvider.GetRequiredService<ISePayWebhookService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await whSvc.HandleAsync(new SePayWebhookRequest
                        {
                            Id = whExtId,
                            Gateway = "MBBank",
                            TransactionDate = "2026-11-26 10:05:00",
                            AccountNumber = "0123456789",
                            Code = pendingQr.PaymentCode,
                            Content = pendingQr.PaymentCode,
                            TransferType = "in",
                            TransferAmount = 150_000m,
                            ReferenceCode = $"FT-CW-{whExtId}",
                        });
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                go.SetResult();

                var errs = await Task.WhenAll(tCash, tWh);
                AssertZeroDeadlockOrUnhandledServerFailure(errs, "3.2 cash-vs-webhook");

                await using var checkScope = _app.Services.CreateAsyncScope();
                var checkDb = checkScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                Assert.Equal(1, await checkDb.Payments.CountAsync(p => p.VisitId == visit.Id && p.Status == PaymentStatuses.Paid));
                Assert.Equal(1, await checkDb.PaymentAllocations.CountAsync(pa => pa.VisitChargeId == examChargeId && pa.Status == PaymentAllocationStatuses.Paid));
            }

            // --- 3.3: Decline Paid Optional CLS vs StartDiagnosticExecution ---
            {
                var (_, patId) = await CreatePatientAsync(fx, "sb-ds");
                var (visit, orderId, orderCode, chargeId) = await SeedPaidClsOrderConfirmedForExecutionAsync(fx, patId, "SB-DS", requiredForConclusion: false);

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var tDecline = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var selSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderSelectionService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await selSvc.DeclineOrderAsync(
                            fx.ReceptionUserId,
                            visit.VisitCode!,
                            orderId.ToString(CultureInfo.InvariantCulture),
                            new DeclineDiagnosticOrderRequestDto(Reason: "Bỏ qua đúng lúc bắt đầu CLS", ConfirmAbandonPaid: true));
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
                    await go.Task;
                    try
                    {
                        await execSvc.StartServiceExecutionAsync(
                            fx.DiagStaffUserId,
                            orderCode,
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
                go.SetResult();

                var decErr = await tDecline;
                var startErr = await tStart;
                AssertZeroDeadlockOrUnhandledServerFailure([decErr, startErr], "3.3 decline-vs-start");
                Assert.True((decErr is null) ^ (startErr is null));

                await using var checkScope = _app.Services.CreateAsyncScope();
                var checkDb = checkScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var finalOrder = await checkDb.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
                var finalCharge = await checkDb.VisitCharges.AsNoTracking().SingleAsync(c => c.Id == chargeId);
                if (decErr is null)
                {
                    Assert.Equal("CANCELLED", finalOrder.Status);
                    Assert.Equal(DiagnosticSelectionStatuses.SkippedAfterPaid, finalOrder.SelectionStatus);
                    Assert.Equal(VisitChargeStatuses.PaidNotPerformed, finalCharge.Status);
                }
                else
                {
                    Assert.Equal("IN_PROGRESS", finalOrder.Status);
                    Assert.Equal(VisitChargeStatuses.Active, finalCharge.Status);
                    var cleanExecSvc = checkScope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                    await cleanExecSvc.CompleteServiceExecutionAsync(
                        fx.DiagStaffUserId,
                        orderCode,
                        new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED"));
                }
            }

            // --- 3.4: Defer vs StartDiagnosticExecution ---
            {
                var (_, patId) = await CreatePatientAsync(fx, "sc-df");
                var (visit, orderId, orderCode, _) = await SeedPaidClsOrderConfirmedForExecutionAsync(fx, patId, "SC-DF", requiredForConclusion: true);

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var tDefer = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var deferSvc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await deferSvc.DeferVisitOrOrdersAsync(
                            fx.ReceptionUserId,
                            visit.VisitCode!,
                            new DeferVisitOrOrdersRequestDto(
                                Reason: "Hoãn sang ngày mai đúng lúc phòng CLS bấm bắt đầu",
                                DeferredUntilDate: new DateOnly(2026, 11, 27),
                                DiagnosticOrderIds: [orderId],
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
                    await go.Task;
                    try
                    {
                        await execSvc.StartServiceExecutionAsync(
                            fx.DiagStaffUserId,
                            orderCode,
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
                go.SetResult();

                var deferErr = await tDefer;
                var startErr = await tStart;
                AssertZeroDeadlockOrUnhandledServerFailure([deferErr, startErr], "3.4 defer-vs-start");
                Assert.True((deferErr is null) ^ (startErr is null));
                if (startErr is null)
                {
                    await using var cleanScope = _app.Services.CreateAsyncScope();
                    var cleanExecSvc = cleanScope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                    await cleanExecSvc.CompleteServiceExecutionAsync(
                        fx.DiagStaffUserId,
                        orderCode,
                        new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED"));
                }
            }

            // --- 3.5: Resume(RETURN_CONSULTATION) vs Finalize UC43 on Day D+1 ---
            {
                _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 11, 0, 0, DateTimeKind.Unspecified));
                var (_, patId) = await CreatePatientAsync(fx, "sc-ru");
                var (visit, _, orderCode, _) = await SeedPaidClsOrderConfirmedForExecutionAsync(fx, patId, "SC-RU", requiredForConclusion: true);

                await using (var execScope = _app.Services.CreateAsyncScope())
                {
                    var execSvc = execScope.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                    await execSvc.StartServiceExecutionAsync(fx.DiagStaffUserId, orderCode, new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"));
                    await execSvc.CompleteServiceExecutionAsync(fx.DiagStaffUserId, orderCode, new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED"));
                }

                // Advance to D+1 (2026-11-27)
                _clock.SetVietnamLocalTime(new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Unspecified));

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var tResume = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var resumeSvc = scope.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await resumeSvc.ResumeVisitAsync(
                            fx.ReceptionUserId,
                            visit.VisitCode!,
                            new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION", ConfirmPresent: true));
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
                    var resSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticResultEntryService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await resSvc.EnterDiagnosticResultAsync(
                            fx.DiagStaffUserId,
                            orderCode,
                            new EnterDiagnosticResultRequest(
                                ResultText: "Kết quả CLS hoàn tất đồng thời với Resume.",
                                Conclusion: "Bình thường.",
                                Status: "FINALIZED",
                                IsFinalized: true));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                go.SetResult();

                var resumeErr = await tResume;
                var uc43Err = await tUc43;
                AssertZeroDeadlockOrUnhandledServerFailure([resumeErr, uc43Err], "3.5 resume-return-vs-finalize");
                Assert.Null(uc43Err);

                if (resumeErr is not null)
                {
                    Assert.IsType<ConflictException>(resumeErr);
                    await using var followUp = _app.Services.CreateAsyncScope();
                    var resumeSvc = followUp.ServiceProvider.GetRequiredService<IVisitCrossDayLifecycleService>();
                    await resumeSvc.ResumeVisitAsync(fx.ReceptionUserId, visit.VisitCode!, new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION", ConfirmPresent: true));
                }

                await using var checkScope = _app.Services.CreateAsyncScope();
                var checkDb = checkScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var finalVisit = await checkDb.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
                Assert.NotNull(finalVisit.ReadyForReturnAt);
                Assert.Equal(
                    1,
                    await checkDb.QueueEntries.AsNoTracking().CountAsync(q =>
                        q.VisitId == visit.Id &&
                        q.QueueType == "RETURN_CONSULTATION" &&
                        q.QueueDate == new DateOnly(2026, 11, 27) &&
                        q.CompletedAt == null));
            }

            // --- 3.6: TechnicalAbort Mandatory Order vs CompleteVisit ---
            {
                _clock.SetVietnamLocalTime(new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Unspecified));
                var (_, patId) = await CreatePatientAsync(fx, "sc-ta");
                var (visit, _, orderCode, _) = await SeedPaidClsOrderConfirmedForExecutionAsync(
                    fx,
                    patId,
                    "SC-TA",
                    requiredForConclusion: true,
                    transitionToAwaitingResults: false);

                using var ready = new SemaphoreSlim(0, 2);
                var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var tAbort = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await reviewSvc.CancelDiagnosticOrderAsync(
                            fx.DiagStaffUserId,
                            orderCode,
                            new CancelDiagnosticOrderRequest(Reason: "Máy hỏng đột xuất", TechnicalAbort: true));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                var tComplete = Task.Run(async () =>
                {
                    await using var scope = _app.Services.CreateAsyncScope();
                    var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
                    ready.Release();
                    await go.Task;
                    try
                    {
                        await consultSvc.CompleteVisitAsync(
                            fx.Doctor1UserId,
                            visit.VisitCode!,
                            new CompleteVisitRequest(ConfirmNoPrescription: true));
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                await ready.WaitAsync(TimeSpan.FromSeconds(10));
                go.SetResult();

                var abortErr = await tAbort;
                var completeErr = await tComplete;
                AssertZeroDeadlockOrUnhandledServerFailure([abortErr, completeErr]);
                Assert.Null(abortErr);
                Assert.IsType<ConflictException>(completeErr);

                await using var checkScope = _app.Services.CreateAsyncScope();
                var checkDb = checkScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                Assert.NotEqual("COMPLETED", (await checkDb.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).Status);
            }
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // TEST 4: Rolled-Back & Conflicting Transactions Emit ZERO Fake SignalR Events
    // Verifies 0 SignalR events on:
    // - 409 insufficient batch stock dispense
    // - 409 unpaid examination CallNext
    // - 409 full-slot appointment booking
    // - 409 PATIENT_BUSY_AT_ANOTHER_POINT simultaneous diagnostic start
    // - MISMATCH / CANCELLED_REFUND_PENDING / DUPLICATE_RECONCILE webhook reconciliation
    //   never emits fake PAID state or PHI on wire!
    // =========================================================================

    [Fact]
    public async Task RolledBackAndConflictingTransactions_EmitZeroFakePaidOrSuccessSignalREvents()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 26, 11, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync(batchStock: 5m);
        try
        {
            var (pat1UserId, pat1Id) = await CreatePatientAsync(fx, "sr1");
            var (pat2UserId, pat2Id) = await CreatePatientAsync(fx, "sr2");

            // Setup 1: Paid Visit + ISSUED Rx requesting 8 units against batch with 5 on hand
            var pharmVisit = await SeedWaitingPaidVisitInRoom1Async(fx, pat1Id, "SR-PHA", queueStatus: "COMPLETED");
            long rxItemId;
            string rxCode;
            await using (var setupScope = _app.Services.CreateAsyncScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var nowLocal = HospitalClock.GetNow(_clock);
                var rx = new Prescription
                {
                    VisitId = pharmVisit.Id,
                    PrescribingDoctorId = fx.Doctor1Id,
                    PrescriptionCode = $"RX-P07-SR-{fx.Tag}",
                    Status = "ISSUED",
                    FulfillmentChoice = "HOSPITAL_PHARMACY",
                    CreatedAt = nowLocal,
                    IssuedAt = nowLocal,
                };
                db.Prescriptions.Add(rx);
                await db.SaveChangesAsync();
                rxCode = rx.PrescriptionCode;

                var item = new PrescriptionItem
                {
                    PrescriptionId = rx.Id,
                    MedicineId = fx.MedicineId,
                    PrescribedQuantity = 8m,
                    SingleDoseQuantity = 1m,
                    SingleDoseUnit = "viên",
                    Instructions = "Uống sau ăn",
                };
                db.PrescriptionItems.Add(item);
                await db.SaveChangesAsync();
                rxItemId = item.Id;
            }

            // Setup 2: Unpaid Waiting Visit at Room 2 Service Point
            var (_, unpaidPatId) = await CreatePatientAsync(fx, "sr-unpaid");
            await SeedUnpaidWaitingVisitInRoom2Async(fx, unpaidPatId, "SR-UNP");

            // Setup 3: Full ExaminationSlot (capacity = 1, 1 BOOKED appointment already occupying it)
            var fullSlot = await CreateSlotAsync(
                fx,
                fx.Room1Id,
                fx.Doctor1Id,
                new DateTime(2026, 11, 27, 10, 0, 0),
                new DateTime(2026, 11, 27, 10, 30, 0),
                capacity: 1);
            await SeedBookedAppointmentAsync(fx, pat1Id, pat1UserId, fullSlot.Id);

            // Setup 4: Visit with CLS order 1 already IN_PROGRESS at DiagServicePointId and CLS order 2 PAID & CONFIRMED at DiagServicePoint2Id
            var (_, busyPatId) = await CreatePatientAsync(fx, "sr-busy");
            string busyOrder2Code;
            await using (var busyScope = _app.Services.CreateAsyncScope())
            {
                var db = busyScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var nowLocal = HospitalClock.GetNow(_clock);
                var today = DateOnly.FromDateTime(nowLocal);

                var busyVisit = new Visit
                {
                    VisitCode = $"VIS-P07-BUSY-{fx.Tag}",
                    PatientId = busyPatId,
                    AssignedDoctorId = fx.Doctor1Id,
                    ExaminationRoomId = fx.Room1Id,
                    CheckedInByStaffId = fx.ReceptionStaffId,
                    Status = "AWAITING_RESULTS",
                    CheckedInAt = nowLocal.AddMinutes(-20),
                    LastPresentDate = today,
                    PresenceState = VisitPresenceStates.Present,
                };
                db.Visits.Add(busyVisit);
                await db.SaveChangesAsync();

                var ord1 = new DiagnosticOrder
                {
                    VisitId = busyVisit.Id,
                    ServiceId = fx.DiagServiceId,
                    OrderCode = $"DIA-BUSY1-{fx.Tag}",
                    Status = "IN_PROGRESS",
                    OrderingDoctorId = fx.Doctor1Id,
                    OrderedAt = nowLocal.AddMinutes(-15),
                    RequiredForConclusion = true,
                    SelectionStatus = DiagnosticSelectionStatuses.Accepted,
                };
                var ord2 = new DiagnosticOrder
                {
                    VisitId = busyVisit.Id,
                    ServiceId = fx.Diag2ServiceId,
                    OrderCode = $"DIA-BUSY2-{fx.Tag}",
                    Status = "CONFIRMED",
                    OrderingDoctorId = fx.Doctor1Id,
                    OrderedAt = nowLocal.AddMinutes(-15),
                    RequiredForConclusion = true,
                    SelectionStatus = DiagnosticSelectionStatuses.Accepted,
                };
                db.DiagnosticOrders.AddRange(ord1, ord2);
                await db.SaveChangesAsync();

                var q1 = new QueueEntry
                {
                    VisitId = busyVisit.Id,
                    ServicePointId = fx.DiagServicePointId,
                    CurrentServicePointId = fx.DiagServicePointId,
                    QueueDate = today,
                    QueueNumber = 1,
                    QueueType = "DIAGNOSTIC",
                    Status = "IN_PROGRESS",
                    PresenceStatus = "PRESENT",
                    ArrivedAt = nowLocal.AddMinutes(-10),
                    ReadyAt = nowLocal.AddMinutes(-9),
                    CalledAt = nowLocal.AddMinutes(-8),
                    StartedAt = nowLocal.AddMinutes(-5),
                    StateVersion = 2,
                };
                // Stage E2 (QE-03 + single-active per visit): order 2 (PAID & CONFIRMED) holds NO active queue —
                // only order 1's IN_PROGRESS DIAGNOSTIC ticket exists on this visit. Starting order 2 must
                // fail closed (no second active post-exam ticket per visit, no second active serving queue).
                db.QueueEntries.Add(q1);
                await db.SaveChangesAsync();

                ord1.QueueEntryId = q1.Id;

                var c1 = VisitCharge.CreateDiagnostic(busyVisit.Id, ord1.Id, fx.DiagServiceId, 100_000m, nowLocal.AddMinutes(-15));
                var c2 = VisitCharge.CreateDiagnostic(busyVisit.Id, ord2.Id, fx.Diag2ServiceId, 120_000m, nowLocal.AddMinutes(-15));
                db.VisitCharges.AddRange(c1, c2);
                await db.SaveChangesAsync();

                var payBusy = new Payment
                {
                    VisitId = busyVisit.Id,
                    PaymentScope = PaymentScopes.Allocated,
                    PaymentMethod = PaymentMethods.Cash,
                    CollectedByStaffId = fx.ReceptionStaffId,
                    IdempotencyKey = $"busy-seed-{fx.Tag}",
                    Amount = 220_000m,
                    Status = PaymentStatuses.Paid,
                    CreatedAt = nowLocal,
                    PaidAt = nowLocal,
                };
                db.Payments.Add(payBusy);
                await db.SaveChangesAsync();
                payBusy.PaymentCode = $"CASH{payBusy.Id}";
                db.PaymentAllocations.AddRange(
                    PaymentAllocation.Create(payBusy.Id, c1.Id, busyVisit.Id, 100_000m, PaymentAllocationStatuses.Paid, nowLocal),
                    PaymentAllocation.Create(payBusy.Id, c2.Id, busyVisit.Id, 120_000m, PaymentAllocationStatuses.Paid, nowLocal));
                await db.SaveChangesAsync();

                busyOrder2Code = ord2.OrderCode;
            }

            // Setup 5: Partial MISMATCH, CANCELLED_REFUND_PENDING, and DUPLICATE_RECONCILE webhook cases
            var (_, payPatId) = await CreatePatientAsync(fx, "sr-pay");
            long payVisitId;
            string partialPaymentCode;
            string cancelledLatePaymentCode;
            string validPaymentCode;
            await using (var paySetupScope = _app.Services.CreateAsyncScope())
            {
                var db = paySetupScope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
                var nowLocal = HospitalClock.GetNow(_clock);

                var payVisit = new Visit
                {
                    VisitCode = $"VIS-P07-SRPAY-{fx.Tag}",
                    PatientId = payPatId,
                    AssignedDoctorId = fx.Doctor1Id,
                    ExaminationRoomId = fx.Room1Id,
                    CheckedInByStaffId = fx.ReceptionStaffId,
                    Status = "AWAITING_RESULTS",
                    CheckedInAt = nowLocal.AddMinutes(-20),
                };
                db.Visits.Add(payVisit);
                await db.SaveChangesAsync();
                payVisitId = payVisit.Id;

                var activeExamCharge = VisitCharge.CreateExamination(payVisit.Id, fx.ExamServiceId, 150_000m, nowLocal.AddMinutes(-20));
                var validDiagOrder = new DiagnosticOrder
                {
                    VisitId = payVisit.Id,
                    ServiceId = fx.Diag2ServiceId,
                    OrderCode = $"DIA-P07-SRVAL-{fx.Tag}",
                    Status = "ORDERED",
                    OrderingDoctorId = fx.Doctor1Id,
                    OrderedAt = nowLocal.AddMinutes(-18),
                    RequiredForConclusion = true,
                    SelectionStatus = DiagnosticSelectionStatuses.Accepted,
                };
                var cancelledDiagOrder = new DiagnosticOrder
                {
                    VisitId = payVisit.Id,
                    ServiceId = fx.DiagServiceId,
                    OrderCode = $"DIA-P07-SRCAN-{fx.Tag}",
                    Status = "CANCELLED",
                    OrderingDoctorId = fx.Doctor1Id,
                    OrderedAt = nowLocal.AddMinutes(-15),
                    RequiredForConclusion = false,
                    SelectionStatus = DiagnosticSelectionStatuses.Declined,
                };
                db.DiagnosticOrders.AddRange(validDiagOrder, cancelledDiagOrder);
                db.VisitCharges.Add(activeExamCharge);
                await db.SaveChangesAsync();

                var validDiagCharge = VisitCharge.CreateDiagnostic(
                    payVisit.Id,
                    validDiagOrder.Id,
                    fx.Diag2ServiceId,
                    120_000m,
                    nowLocal.AddMinutes(-18));
                var cancelledDiagCharge = new VisitCharge
                {
                    VisitId = payVisit.Id,
                    ServiceId = fx.DiagServiceId,
                    DiagnosticOrderId = cancelledDiagOrder.Id,
                    ChargeType = VisitChargeTypes.Diagnostic,
                    UnitPrice = 100_000m,
                    PaymentRequirement = "REQUIRED",
                    Status = VisitChargeStatuses.Cancelled,
                    CreatedAt = nowLocal.AddMinutes(-15),
                    CancelledAt = nowLocal.AddMinutes(-5),
                };
                db.VisitCharges.AddRange(validDiagCharge, cancelledDiagCharge);
                await db.SaveChangesAsync();

                var pendingExamPayment = new Payment
                {
                    VisitId = payVisit.Id,
                    VisitChargeId = activeExamCharge.Id,
                    PaymentScope = PaymentScopes.Allocated,
                    Amount = 150_000m,
                    Status = PaymentStatuses.Pending,
                    CreatedAt = nowLocal,
                };
                var expiredCancelledPayment = new Payment
                {
                    VisitId = payVisit.Id,
                    VisitChargeId = cancelledDiagCharge.Id,
                    PaymentScope = PaymentScopes.Allocated,
                    Amount = 100_000m,
                    Status = PaymentStatuses.Expired,
                    CreatedAt = nowLocal.AddMinutes(-10),
                };
                var validPendingPayment = new Payment
                {
                    VisitId = payVisit.Id,
                    VisitChargeId = validDiagCharge.Id,
                    PaymentScope = PaymentScopes.Allocated,
                    Amount = 120_000m,
                    Status = PaymentStatuses.Pending,
                    CreatedAt = nowLocal,
                };
                db.Payments.AddRange(pendingExamPayment, expiredCancelledPayment, validPendingPayment);
                await db.SaveChangesAsync();

                pendingExamPayment.PaymentCode = $"MF{pendingExamPayment.Id}";
                expiredCancelledPayment.PaymentCode = $"MF{expiredCancelledPayment.Id}";
                validPendingPayment.PaymentCode = $"MF{validPendingPayment.Id}";
                db.PaymentAllocations.AddRange(
                    PaymentAllocation.Create(pendingExamPayment.Id, activeExamCharge.Id, payVisit.Id, 150_000m, PaymentAllocationStatuses.Pending, nowLocal),
                    PaymentAllocation.Create(expiredCancelledPayment.Id, cancelledDiagCharge.Id, payVisit.Id, 100_000m, PaymentAllocationStatuses.Expired, nowLocal),
                    PaymentAllocation.Create(validPendingPayment.Id, validDiagCharge.Id, payVisit.Id, 120_000m, PaymentAllocationStatuses.Pending, nowLocal));
                await db.SaveChangesAsync();

                partialPaymentCode = pendingExamPayment.PaymentCode;
                cancelledLatePaymentCode = expiredCancelledPayment.PaymentCode;
                validPaymentCode = validPendingPayment.PaymentCode;
            }

            // Reset spy publisher right before executing the conflicting / non-unlocking paths
            _spyPublisher.Clear();

            // 1) 409 Insufficient batch stock dispense
            await using (var s1 = _app.Services.CreateAsyncScope())
            {
                var dispSvc = s1.ServiceProvider.GetRequiredService<IConfirmMedicineDispensingService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    dispSvc.ConfirmDispenseAsync(
                        fx.PharmacistUserId,
                        rxCode,
                        new ConfirmDispenseRequest(
                            ReceiverName: "Người bệnh kiểm thử",
                            IsIdentityVerified: true,
                            ChecksConfirmed: true,
                            IdempotencyKey: $"sr-insufficient-{fx.Tag}",
                            Items: [new ConfirmDispenseItemRequest(rxItemId, fx.BatchId, 8m)])));
            }
            Assert.Empty(_spyPublisher.QueueEvents);
            Assert.Empty(_spyPublisher.PaymentEvents);

            // 2) 409 Unpaid examination CallNext
            await using (var s2 = _app.Services.CreateAsyncScope())
            {
                var queueSvc = s2.ServiceProvider.GetRequiredService<IExaminationQueueService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    queueSvc.CallNextPatientAsync(
                        fx.ReceptionUserId,
                        $"sp-{fx.ExamServicePoint2Id}",
                        new CallNextPatientRequest(Notes: "Thử gọi bệnh nhân chưa nộp phí khám")));
            }
            Assert.Empty(_spyPublisher.QueueEvents);
            Assert.Empty(_spyPublisher.PaymentEvents);

            // 3) 409 Full-slot appointment booking
            await using (var s3 = _app.Services.CreateAsyncScope())
            {
                var bookSvc = s3.ServiceProvider.GetRequiredService<IAppointmentBookingService>();
                await Assert.ThrowsAsync<ConflictException>(() =>
                    bookSvc.CreateAsync(
                        pat2UserId,
                        new CreateAppointmentRequest
                        {
                            Mode = "DOCTOR",
                            PatientId = pat2Id.ToString(CultureInfo.InvariantCulture),
                            DoctorId = fx.Doctor1Code,
                            ServiceId = fx.ExamServiceId.ToString(CultureInfo.InvariantCulture),
                            DateIso = "2026-11-27",
                            SlotOrShiftId = fullSlot.Id.ToString(CultureInfo.InvariantCulture),
                            SymptomNote = "Đặt vào slot đã đầy chỗ",
                        },
                        "127.0.0.1"));
            }
            Assert.Empty(_spyPublisher.QueueEvents);
            Assert.Empty(_spyPublisher.PaymentEvents);

            // 4) Failing-closed simultaneous diagnostic start: order 2 (CONFIRMED, no ticket) cannot start
            // while order 1 holds the patient's single active serving ticket (E2: ACTIVE_QUEUE_ALREADY_EXISTS;
            // legacy busy-point check may report PATIENT_BUSY_AT_ANOTHER_POINT — both denote the same conflict).
            await using (var s4 = _app.Services.CreateAsyncScope())
            {
                var execSvc = s4.ServiceProvider.GetRequiredService<IDiagnosticServiceExecutionService>();
                var busyEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    execSvc.StartServiceExecutionAsync(
                        fx.DiagStaffUserId,
                        busyOrder2Code,
                        new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS")));
                Assert.True(
                    busyEx.Message.Contains("PATIENT_BUSY_AT_ANOTHER_POINT", StringComparison.Ordinal) ||
                    busyEx.Message.Contains("ACTIVE_QUEUE_ALREADY_EXISTS", StringComparison.Ordinal),
                    busyEx.Message);
            }
            Assert.Empty(_spyPublisher.QueueEvents);
            Assert.Empty(_spyPublisher.PaymentEvents);

            // 5) MISMATCH partial transfer & CANCELLED_REFUND_PENDING late transfer -> 0 fake PAID or unlock events
            await using (var s5 = _app.Services.CreateAsyncScope())
            {
                var webhookSvc = s5.ServiceProvider.GetRequiredService<ISePayWebhookService>();

                var partialRes = await webhookSvc.HandleAsync(new SePayWebhookRequest
                {
                    Id = Interlocked.Increment(ref _webhookSeq),
                    Gateway = "MBBank",
                    TransactionDate = "2026-11-26 11:02:00",
                    AccountNumber = "0123456789",
                    Code = partialPaymentCode,
                    Content = partialPaymentCode,
                    TransferType = "in",
                    TransferAmount = 50_000m,
                    ReferenceCode = $"FT-SR-PARTIAL-{fx.Tag}",
                });
                Assert.Equal(PaymentGatewayOutcomes.Partial, partialRes.Outcome);

                var lateCancelledRes = await webhookSvc.HandleAsync(new SePayWebhookRequest
                {
                    Id = Interlocked.Increment(ref _webhookSeq),
                    Gateway = "MBBank",
                    TransactionDate = "2026-11-26 11:03:00",
                    AccountNumber = "0123456789",
                    Code = cancelledLatePaymentCode,
                    Content = cancelledLatePaymentCode,
                    TransferType = "in",
                    TransferAmount = 100_000m,
                    ReferenceCode = $"FT-SR-LATE-{fx.Tag}",
                });
                Assert.Equal(SePayWebhookService.CancelledServiceRefundRequiredOutcome, lateCancelledRes.Outcome);
            }

            Assert.Empty(_spyPublisher.QueueEvents);
            Assert.Empty(_spyPublisher.PaymentEvents);

            // Positive post-commit control: valid exact PAID transfer emits minimal VisitPaymentChanged with ZERO PHI
            await using (var s6 = _app.Services.CreateAsyncScope())
            {
                var webhookSvc = s6.ServiceProvider.GetRequiredService<ISePayWebhookService>();
                var validRes = await webhookSvc.HandleAsync(new SePayWebhookRequest
                {
                    Id = Interlocked.Increment(ref _webhookSeq),
                    Gateway = "MBBank",
                    TransactionDate = "2026-11-26 11:04:00",
                    AccountNumber = "0123456789",
                    Code = validPaymentCode,
                    Content = validPaymentCode,
                    TransferType = "in",
                    TransferAmount = 120_000m,
                    ReferenceCode = $"FT-SR-VALID-{fx.Tag}",
                });
                Assert.Equal(PaymentGatewayOutcomes.Paid, validRes.Outcome);
            }

            var publishedPayEvent = Assert.Single(_spyPublisher.PaymentEvents);
            Assert.Equal(payVisitId, publishedPayEvent.VisitId);
            var serializedEvent = JsonSerializer.Serialize(publishedPayEvent);
            Assert.DoesNotContain("0911", serializedEvent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("120000", serializedEvent, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // Invariant Assertions & Isolated Self-Cleaning Fixture Helpers
    // =========================================================================

    private static void AssertZeroDeadlockOrUnhandledServerFailure(IEnumerable<Exception?> exceptions, string? context = null)
    {
        foreach (var ex in exceptions)
        {
            if (ex is null)
            {
                continue;
            }

            var sqlState = PostgresConcurrencyException.TryGetSqlState(ex);
            Assert.True(
                sqlState != "40P01",
                $"PostgreSQL 40P01 deadlock detected in [{context ?? "unknown"}] : {ex.Message}");
            Assert.True(
                ex is ConflictException or ForbiddenException,
                $"Unexpected non-domain exception {ex.GetType().Name} (SqlState={sqlState ?? "none"}) in [{context ?? "unknown"}]: {ex.Message}");
        }
    }

    private async Task AssertConsistentAppointmentVisitAndChargesAsync(long appointmentId, long patientId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        var appt = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        var visits = await db.Visits.AsNoTracking()
            .Where(v => v.AppointmentId == appointmentId || v.PatientId == patientId)
            .ToListAsync();
        var activeVisits = visits.Where(v => v.Status != "CANCELLED" && v.Status != "NO_SHOW").ToList();
        var visitIds = visits.Select(v => v.Id).ToList();

        var activeCharges = visitIds.Count == 0
            ? []
            : await db.VisitCharges.AsNoTracking()
                .Where(c => c.VisitId.HasValue && visitIds.Contains(c.VisitId.Value) && c.Status == VisitChargeStatuses.Active)
                .ToListAsync();

        if (appt.Status == "CHECKED_IN")
        {
            var singleVisit = Assert.Single(activeVisits);
            var singleCharge = Assert.Single(activeCharges);
            Assert.Equal(singleVisit.Id, singleCharge.VisitId);
        }
        else
        {
            Assert.Contains(appt.Status, new[] { "CANCELLED", "NO_SHOW", "RESCHEDULED" });
            Assert.Empty(activeVisits);
            Assert.Empty(activeCharges);
        }
    }

    private sealed record IsolatedConcurrencyFixture(
        string Tag,
        long HospitalId,
        long DepartmentId,
        long SpecialtyId,
        long Room1Id,
        long Room2Id,
        long DiagRoom1Id,
        long DiagRoom2Id,
        long ExamServicePoint1Id,
        long ExamServicePoint2Id,
        long DiagServicePointId,
        long DiagServicePoint2Id,
        long Doctor1Id,
        string Doctor1Code,
        long Doctor1UserId,
        long Doctor1StaffId,
        long Doctor2Id,
        long Doctor2UserId,
        long Doctor2StaffId,
        long DiagStaffUserId,
        long DiagStaffId,
        long ReceptionUserId,
        long ReceptionStaffId,
        long AdminUserId,
        long AdminStaffId,
        long PharmacistUserId,
        long PharmacistStaffId,
        long ExamServiceId,
        long DiagServiceId,
        long Diag2ServiceId,
        long MedicineId,
        long BatchId,
        string Icd10Code,
        long? CreatedIcd10Id,
        List<long> CreatedUserIds,
        List<long> CreatedPatientIds,
        List<long> CreatedSlotIds,
        List<long> CreatedAppointmentIds);

    private async Task<IsolatedConcurrencyFixture> CreateIsolatedFixtureAsync(decimal batchStock = 10m)
    {
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var seedBaseTime = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified);
        var today = DateOnly.FromDateTime(HospitalClock.GetNow(_clock));

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        var adminRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.HospitalAdmin);
        var recRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Reception);
        var docRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Doctor);
        var diagRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.DiagnosticStaff);
        var pharmRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.PharmacyStaff);

        await EnsureRolePermissionsAsync(db, adminRole.Id, ["HOSPITAL_CONFIG"]);
        await EnsureRolePermissionsAsync(db, recRole.Id, ["PATIENT_CHECKIN", "PAYMENT_VIEW", "PAYMENT_COLLECT", "QUEUE_MANAGE", "VISIT_ASSIGN"]);
        await EnsureRolePermissionsAsync(db, docRole.Id, ["MEDICAL_RECORD_WRITE", "DIAGNOSTIC_ORDER", "PRESCRIPTION_WRITE", "PRESCRIPTION_ISSUE", "DIAGNOSTIC_RESULT_READ", "PATIENT_PROFILE_READ", "QUEUE_MANAGE"]);
        await EnsureRolePermissionsAsync(db, diagRole.Id, ["DIAGNOSTIC_ORDER", "QUEUE_MANAGE", "DIAGNOSTIC_RESULT_READ", "DIAGNOSTIC_RESULT_WRITE"]);
        await EnsureRolePermissionsAsync(db, pharmRole.Id, ["MEDICINE_DISPENSE"]);

        var existingIcd = await db.Icd10Codes.AsNoTracking()
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
                Code = $"K30.{tag[..2]}",
                NameVi = "Khó tiêu chức năng kiểm thử P07",
                CatalogVersion = "ICD10-VN",
                NodeType = "LEAF",
                IsSelectable = true,
                NotPrimaryDx = false,
            };
            db.Icd10Codes.Add(customIcd);
            await db.SaveChangesAsync();
            createdIcdId = customIcd.Id;
            icdCode = customIcd.Code;
        }

        var hospital = new Hospital { HospitalCode = $"P07C-{tag}", Name = $"Bệnh viện Concurrency P07 {tag}", IsActive = true };
        var specialty = new Specialty { SpecialtyCode = $"SPC-{tag}", Name = $"Chuyên khoa Nội C {tag}", IsActive = true };
        db.Hospitals.Add(hospital);
        db.Specialties.Add(specialty);
        await db.SaveChangesAsync();

        db.HospitalPolicies.Add(new HospitalPolicy
        {
            HospitalId = hospital.Id,
            PolicyType = "PAYMENT",
            PolicyKey = "vietqr_account",
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN P07C"}""",
            PolicyVersion = 1,
        });

        var dept = new Department { HospitalId = hospital.Id, DepartmentCode = $"DPC-{tag}", Name = $"Khoa Nội Tổng Hợp {tag}", IsActive = true };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();

        var room1 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"R1C{tag}"[..9], Name = $"Buồng khám A {tag}", RoomType = "EXAMINATION", Capacity = 5, Status = "ACTIVE" };
        var room2 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"R2C{tag}"[..9], Name = $"Buồng khám B {tag}", RoomType = "EXAMINATION", Capacity = 5, Status = "ACTIVE" };
        var diagRoom1 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"D1C{tag}"[..9], Name = $"Phòng CLS 1 {tag}", RoomType = "DIAGNOSTIC", Capacity = 3, Status = "ACTIVE" };
        var diagRoom2 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"D2C{tag}"[..9], Name = $"Phòng CLS 2 {tag}", RoomType = "DIAGNOSTIC", Capacity = 3, Status = "ACTIVE" };
        db.HospitalRooms.AddRange(room1, room2, diagRoom1, diagRoom2);
        await db.SaveChangesAsync();

        var createdUserIds = new List<long>();
        async Task<(User User, Staff Staff)> CreateStaffAsync(string prefix, long roleId)
        {
            var u = new User
            {
                Email = $"p07c-{prefix}-{tag.ToLowerInvariant()}-{Guid.NewGuid():N}@medflow.test",
                AuthProvider = "LOCAL",
                AccountStatus = AccountStatus.ACTIVE,
            };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            createdUserIds.Add(u.Id);
            db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = roleId });

            var st = new Staff
            {
                UserId = u.Id,
                HospitalId = hospital.Id,
                DepartmentId = dept.Id,
                StaffCode = $"{prefix.ToUpperInvariant()}{tag}"[..Math.Min(12, prefix.Length + tag.Length)],
                FullName = $"Nhân sự {prefix} {tag}",
                Status = "ACTIVE",
            };
            db.Staff.Add(st);
            await db.SaveChangesAsync();
            return (u, st);
        }

        var (adminUser, adminStaff) = await CreateStaffAsync("ad", adminRole.Id);
        var (recUser, recStaff) = await CreateStaffAsync("rc", recRole.Id);
        var (doc1User, doc1Staff) = await CreateStaffAsync("d1", docRole.Id);
        var (doc2User, doc2Staff) = await CreateStaffAsync("d2", docRole.Id);
        var (diagUser, diagStaff) = await CreateStaffAsync("dg", diagRole.Id);
        var (pharmUser, pharmStaff) = await CreateStaffAsync("ph", pharmRole.Id);

        var doc1 = new Doctor { HospitalId = hospital.Id, StaffId = doc1Staff.Id, SpecialtyId = specialty.Id, DoctorCode = $"DC1-{tag}", ProfessionalTitle = "BS.CKI", LicenseNumber = $"LC1-{tag}", IsActive = true };
        var doc2 = new Doctor { HospitalId = hospital.Id, StaffId = doc2Staff.Id, SpecialtyId = specialty.Id, DoctorCode = $"DC2-{tag}", ProfessionalTitle = "BS.CKII", LicenseNumber = $"LC2-{tag}", IsActive = true };
        db.Doctors.AddRange(doc1, doc2);
        await db.SaveChangesAsync();

        var examService = new Service { HospitalId = hospital.Id, DepartmentId = dept.Id, SpecialtyId = specialty.Id, ServiceCode = $"EXC-{tag}", Name = $"Khám Nội {tag}", ServiceType = "EXAMINATION", AllowsOnlineBooking = true, IsActive = true };
        var diagService = new Service { HospitalId = hospital.Id, DepartmentId = dept.Id, SpecialtyId = specialty.Id, ServiceCode = $"DGC-{tag}", Name = $"Siêu âm {tag}", ServiceType = "DIAGNOSTIC", RequiresResultVerification = false, IsActive = true };
        var diag2Service = new Service { HospitalId = hospital.Id, DepartmentId = dept.Id, SpecialtyId = specialty.Id, ServiceCode = $"DG2-{tag}", Name = $"Xét nghiệm {tag}", ServiceType = "DIAGNOSTIC", RequiresResultVerification = false, IsActive = true };
        db.Services.AddRange(examService, diagService, diag2Service);
        await db.SaveChangesAsync();

        db.ServicePrices.AddRange(
            new ServicePrice { ServiceId = examService.Id, PriceAmount = 150_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = diagService.Id, PriceAmount = 100_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = diag2Service.Id, PriceAmount = 120_000m, EffectiveFrom = seedBaseTime });

        foreach (var dId in new[] { doc1.Id, doc2.Id })
        {
            db.DoctorServiceAuthorizations.AddRange(
                new DoctorServiceAuthorization { DoctorId = dId, ServiceId = examService.Id, IsActive = true },
                new DoctorServiceAuthorization { DoctorId = dId, ServiceId = diagService.Id, IsActive = true },
                new DoctorServiceAuthorization { DoctorId = dId, ServiceId = diag2Service.Id, IsActive = true });
        }

        var sp1 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = room1.Id, DoctorId = doc1.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = $"SP A {tag}", IsActive = true };
        var sp2 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = room2.Id, DoctorId = doc2.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = $"SP B {tag}", IsActive = true };
        var diagSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = diagRoom1.Id, ServiceId = diagService.Id, PointType = "DIAGNOSTIC", Name = $"SP CLS 1 {tag}", IsActive = true };
        var diagSp2 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = diagRoom2.Id, ServiceId = diag2Service.Id, PointType = "DIAGNOSTIC", Name = $"SP CLS 2 {tag}", IsActive = true };
        db.QueueServicePoints.AddRange(sp1, sp2, diagSp, diagSp2);

        var medicine = new Medicine { HospitalId = hospital.Id, MedicineCode = $"MDC-{tag}", Name = $"Pantoprazol 40mg {tag}", DosageForm = "Viên nén", DispensingUnit = "viên", IsActive = true };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();

        var batch = new MedicineBatch { MedicineId = medicine.Id, BatchNumber = $"LOTC-{tag}", ExpiryDate = today.AddMonths(6), QuantityOnHand = batchStock, IsBlocked = false };
        db.MedicineBatches.Add(batch);
        await db.SaveChangesAsync();

        var policySvc = scope.ServiceProvider.GetRequiredService<IAppointmentPolicyService>();
        await policySvc.UpdateForAdminAsync(
            adminUser.Id,
            new UpdateAppointmentPolicyRequest
            {
                BookingWindowDays = 30,
                BookingLeadHours = 0,
                MaxActiveBookings = 5,
                RescheduleMinimumHours = 0,
                MaxReschedules = 3,
                CancelMinimumHours = 0,
                NoShowGraceMinutes = 15,
                NoShowMaxCount = 5,
                NoShowLookbackDays = 90,
                NoShowBlockDays = 0,
            },
            "127.0.0.1");

        return new IsolatedConcurrencyFixture(
            Tag: tag,
            HospitalId: hospital.Id,
            DepartmentId: dept.Id,
            SpecialtyId: specialty.Id,
            Room1Id: room1.Id,
            Room2Id: room2.Id,
            DiagRoom1Id: diagRoom1.Id,
            DiagRoom2Id: diagRoom2.Id,
            ExamServicePoint1Id: sp1.Id,
            ExamServicePoint2Id: sp2.Id,
            DiagServicePointId: diagSp.Id,
            DiagServicePoint2Id: diagSp2.Id,
            Doctor1Id: doc1.Id,
            Doctor1Code: doc1.DoctorCode,
            Doctor1UserId: doc1User.Id,
            Doctor1StaffId: doc1Staff.Id,
            Doctor2Id: doc2.Id,
            Doctor2UserId: doc2User.Id,
            Doctor2StaffId: doc2Staff.Id,
            DiagStaffUserId: diagUser.Id,
            DiagStaffId: diagStaff.Id,
            ReceptionUserId: recUser.Id,
            ReceptionStaffId: recStaff.Id,
            AdminUserId: adminUser.Id,
            AdminStaffId: adminStaff.Id,
            PharmacistUserId: pharmUser.Id,
            PharmacistStaffId: pharmStaff.Id,
            ExamServiceId: examService.Id,
            DiagServiceId: diagService.Id,
            Diag2ServiceId: diag2Service.Id,
            MedicineId: medicine.Id,
            BatchId: batch.Id,
            Icd10Code: icdCode,
            CreatedIcd10Id: createdIcdId,
            CreatedUserIds: createdUserIds,
            CreatedPatientIds: [],
            CreatedSlotIds: [],
            CreatedAppointmentIds: []);
    }

    private async Task<(long UserId, long PatientId)> CreatePatientAsync(IsolatedConcurrencyFixture fx, string label)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
        var patRole = await db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Patient);

        var user = new User
        {
            Email = $"p07c-pat-{label}-{fx.Tag.ToLowerInvariant()}-{Guid.NewGuid():N}@medflow.test",
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = patRole.Id });

        var patient = new Patient
        {
            UserId = user.Id,
            PatientCode = $"P7C{Guid.NewGuid():N}"[..11].ToUpperInvariant(),
            FullName = $"Bệnh nhân {label} {fx.Tag}",
            DateOfBirth = new DateOnly(1991, 6, 12),
            Gender = "MALE",
            Phone = "0911223344",
            IdentificationHash = $"hash-{label}-{fx.Tag}-{Guid.NewGuid():N}",
            IdentificationHashHmac = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Status = "ACTIVE",
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        lock (fx.CreatedUserIds)
        {
            fx.CreatedUserIds.Add(user.Id);
            fx.CreatedPatientIds.Add(patient.Id);
        }

        return (user.Id, patient.Id);
    }

    private async Task<ExaminationSlot> CreateSlotAsync(
        IsolatedConcurrencyFixture fx,
        long roomId,
        long doctorId,
        DateTime startsAt,
        DateTime endsAt,
        int capacity = 3)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        var slot = new ExaminationSlot
        {
            ServiceId = fx.ExamServiceId,
            DoctorId = doctorId,
            RoomId = roomId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Capacity = capacity,
            Status = "OPEN",
        };
        db.ExaminationSlots.Add(slot);
        await db.SaveChangesAsync();

        lock (fx.CreatedSlotIds)
        {
            fx.CreatedSlotIds.Add(slot.Id);
        }

        return slot;
    }

    private async Task<Appointment> SeedBookedAppointmentAsync(
        IsolatedConcurrencyFixture fx,
        long patientId,
        long bookedByUserId,
        long slotId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
        var nowLocal = HospitalClock.GetNow(_clock);

        var appt = new Appointment
        {
            PatientId = patientId,
            SlotId = slotId,
            BookingChannel = "ONLINE",
            BookedByUserId = bookedByUserId,
            BookedAt = nowLocal.AddHours(-1),
            Status = "BOOKED",
            ReasonForVisit = "Đau bụng vùng thượng vị cần khám nội khoa",
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();
        appt.AppointmentCode = $"APT-P07C-{appt.Id:D6}";
        appt.RootAppointmentId = appt.Id;
        await db.SaveChangesAsync();

        lock (fx.CreatedAppointmentIds)
        {
            fx.CreatedAppointmentIds.Add(appt.Id);
        }

        return appt;
    }

    private async Task<Visit> SeedWaitingPaidVisitInRoom1Async(
        IsolatedConcurrencyFixture fx,
        long patientId,
        string tag,
        string queueStatus = "CALLED")
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
        var nowLocal = HospitalClock.GetNow(_clock);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"VIS-P07C-{tag}-{fx.Tag}",
            PatientId = patientId,
            AssignedDoctorId = fx.Doctor1Id,
            ExaminationRoomId = fx.Room1Id,
            CheckedInByStaffId = fx.ReceptionStaffId,
            Status = queueStatus == "COMPLETED" ? "COMPLETED" : "WAITING",
            CheckedInAt = nowLocal.AddMinutes(-15),
            LastPresentDate = today,
            PresenceState = VisitPresenceStates.Present,
            ReasonForVisit = "Khám kiểm tra xung đột chuyển buồng",
        };
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        var nextQ = (await db.QueueEntries
            .Where(q => q.ServicePointId == fx.ExamServicePoint1Id && q.QueueDate == today)
            .MaxAsync(q => (int?)q.QueueNumber) ?? 0) + 1;

        db.QueueEntries.Add(new QueueEntry
        {
            VisitId = visit.Id,
            ServicePointId = fx.ExamServicePoint1Id,
            CurrentServicePointId = fx.ExamServicePoint1Id,
            QueueDate = today,
            QueueNumber = nextQ,
            QueueType = "EXAMINATION",
            Status = queueStatus,
            PresenceStatus = "PRESENT",
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            StateVersion = 1,
            ArrivedAt = nowLocal.AddMinutes(-14),
            ReadyAt = nowLocal.AddMinutes(-14),
            CalledAt = queueStatus == "CALLED" ? nowLocal.AddMinutes(-2) : null,
            CompletedAt = queueStatus == "COMPLETED" ? nowLocal.AddMinutes(-1) : null,
        });

        var examCharge = VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 150_000m, nowLocal.AddMinutes(-14));
        db.VisitCharges.Add(examCharge);
        await db.SaveChangesAsync();

        var paid = new Payment
        {
            VisitId = visit.Id,
            VisitChargeId = examCharge.Id,
            PaymentScope = PaymentScopes.Allocated,
            PaymentMethod = PaymentMethods.Cash,
            CollectedByStaffId = fx.ReceptionStaffId,
            IdempotencyKey = $"seed-p07c-{visit.Id}-{Guid.NewGuid():N}",
            Amount = 150_000m,
            Status = PaymentStatuses.Paid,
            CreatedAt = nowLocal.AddMinutes(-10),
            PaidAt = nowLocal.AddMinutes(-10),
        };
        db.Payments.Add(paid);
        await db.SaveChangesAsync();
        paid.PaymentCode = $"CASH{paid.Id}";
        db.PaymentAllocations.Add(PaymentAllocation.Create(
            paid.Id,
            examCharge.Id,
            visit.Id,
            150_000m,
            PaymentAllocationStatuses.Paid,
            nowLocal.AddMinutes(-10)));
        await db.SaveChangesAsync();

        return visit;
    }

    private async Task<Visit> SeedUnpaidWaitingVisitWithClsOrderAsync(
        IsolatedConcurrencyFixture fx,
        long patientId,
        string tag,
        bool requiredForConclusion)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
        var nowLocal = HospitalClock.GetNow(_clock);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"VIS-P07C-{tag}-{fx.Tag}",
            PatientId = patientId,
            AssignedDoctorId = fx.Doctor1Id,
            ExaminationRoomId = fx.Room1Id,
            CheckedInByStaffId = fx.ReceptionStaffId,
            Status = "CHECKED_IN",
            CheckedInAt = nowLocal.AddMinutes(-12),
            LastPresentDate = today,
            PresenceState = VisitPresenceStates.Present,
            ReasonForVisit = "Khám kiểm tra race Stage B",
        };
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        var order = new DiagnosticOrder
        {
            OrderCode = $"DIA-{tag}-{fx.Tag}",
            VisitId = visit.Id,
            OrderingDoctorId = fx.Doctor1Id,
            ServiceId = fx.DiagServiceId,
            Status = "ORDERED",
            RequiredForConclusion = requiredForConclusion,
            SelectionStatus = DiagnosticSelectionStatuses.Accepted,
            OrderedAt = nowLocal.AddMinutes(-5),
        };
        db.DiagnosticOrders.Add(order);
        await db.SaveChangesAsync();

        db.VisitCharges.AddRange(
            VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 150_000m, nowLocal.AddMinutes(-11)),
            VisitCharge.CreateDiagnostic(visit.Id, order.Id, fx.DiagServiceId, 100_000m, nowLocal.AddMinutes(-5)));
        await db.SaveChangesAsync();

        return visit;
    }

    private async Task<(Visit Visit, long OrderId, string OrderCode, long ChargeId)> SeedPaidClsOrderConfirmedForExecutionAsync(
        IsolatedConcurrencyFixture fx,
        long patientId,
        string tag,
        bool requiredForConclusion,
        bool transitionToAwaitingResults = true)
    {
        var visit = await SeedWaitingPaidVisitInRoom1Async(fx, patientId, tag);
        long orderId;
        string orderCode;
        long chargeId;

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var consultSvc = scope.ServiceProvider.GetRequiredService<IConsultationWorkspaceService>();
            await consultSvc.OpenWorkspaceAsync(fx.Doctor1UserId, visit.VisitCode!);
            await consultSvc.RecordClinicalExaminationAsync(
                fx.Doctor1UserId,
                visit.VisitCode!,
                new RecordClinicalExaminationRequest(ClinicalFindings: "Khám lâm sàng sơ bộ.", TreatmentPlan: "Làm CLS."));
            await consultSvc.RecordDiagnosisAsync(
                fx.Doctor1UserId,
                visit.VisitCode!,
                new RecordDiagnosisRequest(PrimaryIcd10Code: fx.Icd10Code, ClinicalConclusion: "Chẩn đoán sơ bộ.", IsConfirmed: false));

            var created = await consultSvc.CreateDiagnosticOrderAsync(
                fx.Doctor1UserId,
                visit.VisitCode!,
                new CreateDiagnosticOrderRequest(
                    ServiceId: fx.DiagServiceId,
                    Instruction: $"Chỉ định CLS {tag}",
                    RequiredForConclusion: requiredForConclusion,
                    TransitionToAwaitingResults: transitionToAwaitingResults));
            orderId = created.CreatedDiagnosticOrders![0].OrderId;
            orderCode = created.CreatedDiagnosticOrders[0].OrderCode;

            var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
            chargeId = (await db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderId)).Id;

            var cashSvc = scope.ServiceProvider.GetRequiredService<ICollectCashPaymentService>();
            await cashSvc.CollectAsync(
                fx.ReceptionUserId,
                visit.VisitCode!,
                new CollectCashPaymentRequestDto(VisitChargeIds: [chargeId], IdempotencyKey: $"seed-cls-{tag}-{fx.Tag}"));

            var reviewSvc = scope.ServiceProvider.GetRequiredService<IDiagnosticOrderReviewService>();
            await reviewSvc.ConfirmDiagnosticOrderAsync(
                fx.DiagStaffUserId,
                orderCode,
                new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true, ServicePointId: fx.DiagServicePointId));
        }

        return (visit, orderId, orderCode, chargeId);
    }

    private async Task SeedUnpaidWaitingVisitInRoom2Async(IsolatedConcurrencyFixture fx, long patientId, string tag)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
        var nowLocal = HospitalClock.GetNow(_clock);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"VIS-P07C-{tag}-{fx.Tag}",
            PatientId = patientId,
            AssignedDoctorId = fx.Doctor2Id,
            ExaminationRoomId = fx.Room2Id,
            CheckedInByStaffId = fx.ReceptionStaffId,
            Status = "WAITING",
            CheckedInAt = nowLocal.AddMinutes(-10),
            LastPresentDate = today,
            PresenceState = VisitPresenceStates.Present,
            ReasonForVisit = "Chờ nộp phí khám trước khi gọi",
        };
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        db.QueueEntries.Add(new QueueEntry
        {
            VisitId = visit.Id,
            ServicePointId = fx.ExamServicePoint2Id,
            CurrentServicePointId = fx.ExamServicePoint2Id,
            QueueDate = today,
            QueueNumber = 1,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            PresenceStatus = "PRESENT",
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            StateVersion = 1,
            ArrivedAt = nowLocal.AddMinutes(-9),
            ReadyAt = nowLocal.AddMinutes(-9),
        });

        db.VisitCharges.Add(VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 150_000m, nowLocal.AddMinutes(-9)));
        await db.SaveChangesAsync();
    }

    private static async Task EnsureRolePermissionsAsync(MedFlowDbContext db, long roleId, IReadOnlyList<string> codes)
    {
        foreach (var code in codes)
        {
            var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
            if (perm is null)
            {
                perm = new Permission { Code = code };
                db.Permissions.Add(perm);
                await db.SaveChangesAsync();
            }

            if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == perm.Id))
            {
                db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = perm.Id });
                await db.SaveChangesAsync();
            }
        }
    }

    private async Task CleanupIsolatedFixtureAsync(IsolatedConcurrencyFixture fx)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        var apptIds = await db.Appointments.AsNoTracking()
            .Where(a => (a.SlotId.HasValue && fx.CreatedSlotIds.Contains(a.SlotId.Value)) ||
                        (a.PatientId.HasValue && fx.CreatedPatientIds.Contains(a.PatientId.Value)) ||
                        fx.CreatedAppointmentIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync();

        var visitIds = await db.Visits.AsNoTracking()
            .Where(v => (v.PatientId.HasValue && fx.CreatedPatientIds.Contains(v.PatientId.Value)) ||
                        (v.AppointmentId.HasValue && apptIds.Contains(v.AppointmentId.Value)) ||
                        v.AssignedDoctorId == fx.Doctor1Id ||
                        v.AssignedDoctorId == fx.Doctor2Id)
            .Select(v => v.Id)
            .ToListAsync();

        await db.Notifications
            .Where(n => (n.VisitId.HasValue && visitIds.Contains(n.VisitId.Value)) ||
                        (n.AppointmentId.HasValue && apptIds.Contains(n.AppointmentId.Value)) ||
                        (n.PatientId.HasValue && fx.CreatedPatientIds.Contains(n.PatientId.Value)))
            .ExecuteDeleteAsync();

        // Stage E2 (migration 027): fk_queue_entries_diagnostic_order_visit + fk_queue_entries_prescription_visit
        // are ON DELETE RESTRICT. Clear diagnostic_orders.queue_entry_id + queue_fairness_states
        // .last_started_queue_entry_id back-pointers before deleting queue_entries.
        var spIds = new[] { fx.ExamServicePoint1Id, fx.ExamServicePoint2Id, fx.DiagServicePointId, fx.DiagServicePoint2Id };
        await db.DiagnosticOrders
            .Where(o => o.QueueEntryId.HasValue && o.VisitId.HasValue && visitIds.Contains(o.VisitId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.QueueEntryId, (long?)null));
        await db.QueueFairnessStates.Where(f => spIds.Contains(f.ServicePointId)).ExecuteDeleteAsync();
        await db.QueueEntries
            .Where(q => (q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)) ||
                        (q.AppointmentId.HasValue && apptIds.Contains(q.AppointmentId.Value)) ||
                        (q.ServicePointId.HasValue && spIds.Contains(q.ServicePointId.Value)))
            .ExecuteDeleteAsync();
        var orderIds = await db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.VisitId.HasValue && visitIds.Contains(o.VisitId.Value))
            .Select(o => o.Id)
            .ToListAsync();
        if (orderIds.Count > 0)
        {
            var resIds = await db.DiagnosticResults.AsNoTracking()
                .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                .Select(r => r.Id)
                .ToListAsync();
            if (resIds.Count > 0)
            {
                await db.DiagnosticResultVersions.Where(v => v.DiagnosticResultId.HasValue && resIds.Contains(v.DiagnosticResultId.Value)).ExecuteDeleteAsync();
                await db.DiagnosticResults.Where(r => resIds.Contains(r.Id)).ExecuteDeleteAsync();
            }
        }

        var rxIds = await db.Prescriptions.AsNoTracking()
            .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
            .Select(p => p.Id)
            .ToListAsync();

        if (rxIds.Count > 0)
        {
            var dispIds = await db.MedicationDispenses.AsNoTracking()
                .Where(md => md.PrescriptionId.HasValue && rxIds.Contains(md.PrescriptionId.Value))
                .Select(md => md.Id)
                .ToListAsync();

            if (dispIds.Count > 0)
            {
                await db.StockTransactions.Where(st => st.MedicineBatchId == fx.BatchId).ExecuteDeleteAsync();
                await db.DispenseItems.Where(di => di.DispenseId.HasValue && dispIds.Contains(di.DispenseId.Value)).ExecuteDeleteAsync();
                await db.MedicationDispenses.Where(md => dispIds.Contains(md.Id)).ExecuteDeleteAsync();
            }

            await db.PrescriptionItems.Where(pi => pi.PrescriptionId.HasValue && rxIds.Contains(pi.PrescriptionId.Value)).ExecuteDeleteAsync();
            await db.Prescriptions.Where(p => rxIds.Contains(p.Id)).ExecuteDeleteAsync();
        }

        var emrIds = await db.MedicalRecords.AsNoTracking()
            .Where(mr => mr.VisitId.HasValue && visitIds.Contains(mr.VisitId.Value))
            .Select(mr => mr.Id)
            .ToListAsync();
        if (emrIds.Count > 0)
        {
            await db.MedicalRecordRevisions.Where(r => r.MedicalRecordId.HasValue && emrIds.Contains(r.MedicalRecordId.Value)).ExecuteDeleteAsync();
            await db.MedicalRecords.Where(mr => emrIds.Contains(mr.Id)).ExecuteDeleteAsync();
        }

        if (visitIds.Count > 0)
        {
            await db.VisitDiagnoses.Where(d => d.VisitId.HasValue && visitIds.Contains(d.VisitId.Value)).ExecuteDeleteAsync();
        }

        var paymentIds = await db.Payments.AsNoTracking()
            .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
            .Select(p => p.Id)
            .ToListAsync();
        if (paymentIds.Count > 0 || visitIds.Count > 0)
        {
            await db.PaymentGatewayEvents.Where(e => e.PaymentId.HasValue && paymentIds.Contains(e.PaymentId.Value)).ExecuteDeleteAsync();
            await db.PaymentAllocations.Where(pa => paymentIds.Contains(pa.PaymentId) || visitIds.Contains(pa.VisitId)).ExecuteDeleteAsync();
            await db.Payments.Where(p => paymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        }


        if (visitIds.Count > 0 || apptIds.Count > 0)
        {
            await db.VisitCharges.Where(c => c.VisitId.HasValue && visitIds.Contains(c.VisitId.Value)).ExecuteDeleteAsync();
            await db.DiagnosticOrders.Where(o => o.VisitId.HasValue && visitIds.Contains(o.VisitId.Value)).ExecuteDeleteAsync();
            await db.QueueEntries
                .Where(q => (q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)) ||
                            (q.AppointmentId.HasValue && apptIds.Contains(q.AppointmentId.Value)) ||
                            (q.ServicePointId.HasValue && spIds.Contains(q.ServicePointId.Value)))
                .ExecuteDeleteAsync();
        }

        await db.AuditLogs
            .Where(a => a.HospitalId == fx.HospitalId ||
                        (a.ActorUserId.HasValue && fx.CreatedUserIds.Contains(a.ActorUserId.Value)))
            .ExecuteDeleteAsync();

        if (visitIds.Count > 0)
        {
            await db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
        }

        if (apptIds.Count > 0)
        {
            await db.Appointments
                .Where(a => apptIds.Contains(a.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.RescheduledFromAppointmentId, (long?)null)
                    .SetProperty(a => a.RootAppointmentId, (long?)null));
            await db.Appointments.Where(a => apptIds.Contains(a.Id)).ExecuteDeleteAsync();
        }

        if (fx.CreatedSlotIds.Count > 0)
        {
            await db.ExaminationSlots.Where(s => fx.CreatedSlotIds.Contains(s.Id)).ExecuteDeleteAsync();
        }

        await db.QueueServicePoints.Where(sp => spIds.Contains(sp.Id)).ExecuteDeleteAsync();
        await db.DoctorServiceAuthorizations
            .Where(a => a.DoctorId == fx.Doctor1Id || a.DoctorId == fx.Doctor2Id)
            .ExecuteDeleteAsync();

        var svcIds = new[] { fx.ExamServiceId, fx.DiagServiceId, fx.Diag2ServiceId };
        await db.ServicePrices.Where(p => p.ServiceId.HasValue && svcIds.Contains(p.ServiceId.Value)).ExecuteDeleteAsync();

        await db.MedicineBatches.Where(b => b.Id == fx.BatchId).ExecuteDeleteAsync();
        await db.Medicines.Where(m => m.Id == fx.MedicineId).ExecuteDeleteAsync();

        if (fx.CreatedPatientIds.Count > 0)
        {
            await db.Patients.Where(p => fx.CreatedPatientIds.Contains(p.Id)).ExecuteDeleteAsync();
        }

        await db.Doctors.Where(d => d.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.Staff.Where(s => s.HospitalId == fx.HospitalId).ExecuteDeleteAsync();

        if (fx.CreatedUserIds.Count > 0)
        {
            await db.AuthSessions.Where(s => fx.CreatedUserIds.Contains(s.UserId)).ExecuteDeleteAsync();
            await db.UserRoles.Where(ur => fx.CreatedUserIds.Contains(ur.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(u => fx.CreatedUserIds.Contains(u.Id)).ExecuteDeleteAsync();
        }

        await db.HospitalRooms.Where(r => r.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.Services.Where(s => s.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.Departments.Where(d => d.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.HospitalPolicies.Where(p => p.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await db.Hospitals.Where(h => h.Id == fx.HospitalId).ExecuteDeleteAsync();
        await db.Specialties.Where(sp => sp.Id == fx.SpecialtyId).ExecuteDeleteAsync();

        if (fx.CreatedIcd10Id.HasValue)
        {
            await db.Icd10Codes.Where(c => c.Id == fx.CreatedIcd10Id.Value).ExecuteDeleteAsync();
        }
    }
}
