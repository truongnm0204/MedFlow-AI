using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Infrastructure.Persistence;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;

namespace MedFlow.IntegrationTests.EndToEnd;

/// <summary>
/// Stage D (Phase 07 / GH-850 / ADR-0059 / ADR-0060 / ADR-0047) Full Outpatient E2E Acceptance Suite.
/// Executes all 8 mandatory outpatient scenarios (<c>E2E-01 .. E2E-08</c>) over real HTTP +
/// SignalR WebSockets (<c>/hubs/clinical</c>) + clean-slate PostgreSQL (<c>medflow_test_p07</c>),
/// with controllable hospital clock for deterministic multi-day execution and strict FK child-to-parent cleanup.
/// </summary>
public sealed class Phase07FullOutpatientPayBeforeServiceE2ETests
    : IClassFixture<TestWebAppFactory>, IDisposable
{
    private const string HubPath = "/hubs/clinical";
    private const string WebhookUrl = "/api/v1/payments/sepay/webhook";
    private const string SePayApiKey = "IntegrationTest_SePay_Key";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static long _webhookSeq = 7_100_000;

    private sealed class MutableVietnamClock : TimeProvider
    {
        private long _utcTicks;

        public MutableVietnamClock(DateTime initialVietnamLocalUnspecified)
        {
            SetVietnamLocalTime(initialVietnamLocalUnspecified);
        }

        public void SetVietnamLocalTime(DateTime vietnamLocalUnspecified)
        {
            var unspecified = DateTime.SpecifyKind(vietnamLocalUnspecified, DateTimeKind.Unspecified);
            var dto = new DateTimeOffset(unspecified, TimeSpan.FromHours(7));
            Interlocked.Exchange(ref _utcTicks, dto.UtcDateTime.Ticks);
        }

        public override DateTimeOffset GetUtcNow()
            => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    }

    private readonly MutableVietnamClock _clock = new(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;

    public Phase07FullOutpatientPayBeforeServiceE2ETests(TestWebAppFactory baseFactory)
    {
        _app = baseFactory.WithWebHostBuilder(builder =>
        {
            // This suite mints JWTs against the injected virtual clock (MutableVietnamClock), so the
            // tokens are already expired on the REAL system clock. CloseOnAuthenticationExpiration
            // evaluates the `exp` claim against the real clock and would abort the WebSocket
            // nondeterministically right after connect (E2E01 root cause — PR #859, see
            // plans/reports/pr859-e2e01-round2.md). The security feature stays enabled by default
            // in production, where JWTs are minted against the real clock and never pre-expired;
            // the virtual clock is a test-only artifact, so disabling the feature is scoped to
            // this suite's factory instance only.
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Realtime:CloseOnAuthenticationExpiration"] = "false",
                }));

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
    // E2E-01: Full Same-Day 12-Step Journey
    // Online booking -> Check-in -> Unpaid gate -> Exam VietQR + SePay + SignalR
    // Zero-PHI -> Provisional Dx + 2 Mandatory CLS -> Pay & UC42 BloodTest ->
    // Pay & UC42 Ultrasound -> UC43 Draft + Two-Person Verify + Finalize ->
    // 1 READY RETURN_CONSULTATION -> 7 EMR fields + Supersede/Confirm Dx +
    // HOSPITAL_PHARMACY Rx -> Multi-batch FEFO + Idempotent Dispense + ADR-0047
    // Receipt -> Patient Portal PHI + 3 PAID Payments & 3 PAID Allocations (500k)
    // =========================================================================

    [Fact]
    public async Task E2E01_SameDayFull12StepJourney_TwoMandatoryOrders_SingleChargeQr_TwoPersonVerify_MultiBatchFefo_AndPortalPhi()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 8, 30, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [6m, 10m]);
        try
        {
            var nowLocal = HospitalClock.GetNow(_clock);
            var today = DateOnly.FromDateTime(nowLocal);

            var slot = new ExaminationSlot
            {
                ServiceId = fx.ExamServiceId,
                DoctorId = fx.Doctor1Id,
                RoomId = fx.ExamRoom1Id,
                StartsAt = today.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified),
                EndsAt = today.ToDateTime(new TimeOnly(9, 30), DateTimeKind.Unspecified),
                Capacity = 3,
                Status = "OPEN",
            };
            Db.ExaminationSlots.Add(slot);
            await Db.SaveChangesAsync();
            fx.CreatedSlotIds.Add(slot.Id);

            var patientToken = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var strangerToken = await CreateSessionJwtAsync(fx.StrangerPatientUserId, RoleNames.Patient);
            var receptionToken = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var doctorToken = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);
            var labStaff1Token = await CreateSessionJwtAsync(fx.DiagStaff1UserId, RoleNames.DiagnosticStaff);
            var labStaff2Token = await CreateSessionJwtAsync(fx.DiagStaff2UserId, RoleNames.DiagnosticStaff);
            var pharmacyToken = await CreateSessionJwtAsync(fx.PharmacistUserId, RoleNames.PharmacyStaff);

            // STEP 1: Online booking -> BOOKED + RESERVED queue
            string appointmentCode;
            long appointmentId;
            using (var bookResp = await SendJsonAsync(
                HttpMethod.Post,
                "/api/v1/appointments",
                new CreateAppointmentRequest
                {
                    Mode = "DOCTOR",
                    PatientId = fx.PatientId.ToString(CultureInfo.InvariantCulture),
                    DoctorId = fx.Doctor1Code,
                    ServiceId = fx.ExamServiceId.ToString(CultureInfo.InvariantCulture),
                    DateIso = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    SlotOrShiftId = slot.Id.ToString(CultureInfo.InvariantCulture),
                    SymptomNote = "Đau vùng thượng vị kèm đầy hơi kéo dài nhiều ngày",
                },
                patientToken))
            {
                Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);
                var booked = (await bookResp.Content.ReadFromJsonAsync<CreateAppointmentResponse>(WebJson))!;
                appointmentId = long.Parse(booked.AppointmentId, CultureInfo.InvariantCulture);
                appointmentCode = booked.AppointmentCode;
                Assert.Equal("BOOKED", booked.Status);
                fx.CreatedAppointmentIds.Add(appointmentId);
            }

            using (var queueTrackResp = await SendJsonAsync(
                HttpMethod.Get,
                $"/api/v1/appointments/{appointmentCode}/queue-status",
                null,
                patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, queueTrackResp.StatusCode);
                var queueTrack = (await queueTrackResp.Content.ReadFromJsonAsync<AppointmentQueueStatusDto>(WebJson))!;
                Assert.Equal("AWAITING_CHECK_IN", queueTrack.Status);
                Assert.False(string.IsNullOrWhiteSpace(queueTrack.QueueNumber));
            }

            // STEP 2: Reception check-in -> Visit + EXAMINATION VisitCharge; unpaid CallNext & OpenWorkspace blocked (409)
            using (var checkInResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/appointments/{appointmentCode}/check-in",
                new CheckInScheduledPatientRequest(
                    ReasonForVisit: "Đau vùng thượng vị kèm đầy hơi kéo dài",
                    IsIdentityVerified: true,
                    Priority: "NORMAL"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.OK, checkInResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            var visit = await Db.Visits.AsNoTracking().SingleAsync(v => v.AppointmentId == appointmentId);
            var visitCode = visit.VisitCode!;
            var examCharge = await Db.VisitCharges.AsNoTracking()
                .SingleAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Examination);
            Assert.Equal(150_000m, examCharge.UnitPrice);
            Assert.Equal("REQUIRED", examCharge.PaymentRequirement);
            Assert.Equal(VisitChargeStatuses.Active, examCharge.Status);

            using (var unpaidCallNextResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/queue/service-points/sp-{fx.ExamServicePoint1Id}/call-next",
                new CallNextPatientRequest(Notes: "Thử gọi khám khi chưa đóng phí khám"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, unpaidCallNextResp.StatusCode);
                var problem = await unpaidCallNextResp.Content.ReadAsStringAsync();
                Assert.Contains("UNPAID_EXAMINATION_FEE", problem, StringComparison.OrdinalIgnoreCase);
            }

            using (var unpaidOpenResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/open",
                new { },
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, unpaidOpenResp.StatusCode);
            }

            // STEP 3: Exam VietQR + SePay webhook + real WebSocket VisitPaymentChanged (zero PHI on wire) -> CallNext & OpenWorkspace
            var wsChannel = Channel.CreateUnbounded<string>();
            await using var hub = CreateHubConnection(patientToken);
            hub.On<JsonElement>("VisitPaymentChanged", payload =>
            {
                wsChannel.Writer.TryWrite(payload.GetRawText());
            });
            await hub.StartAsync();
            var joinAck = await hub.InvokeAsync<JsonElement>("JoinVisitPaymentGroup", visitCode);
            Assert.Contains($"visit-payment:{visit.Id}", joinAck.GetRawText(), StringComparison.Ordinal);

            GenerateQrPaymentResponseDto examQr;
            using (var qrResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [examCharge.Id]),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.Created, qrResp.StatusCode);
                examQr = (await qrResp.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
                Assert.Equal(150_000m, examQr.Amount);
            }

            var examOutcome = await PostSePayWebhookAsync(examQr.PaymentCode, 150_000m, $"FT-E2E01-EX-{fx.Suffix}");
            Assert.Equal(PaymentGatewayOutcomes.Paid, examOutcome);

            using var wsCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var wireJson = await wsChannel.Reader.ReadAsync(wsCts.Token);
            using (var wireDoc = JsonDocument.Parse(wireJson))
            {
                var props = wireDoc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
                Assert.Equal(["eventType", "visitId"], props);
                Assert.Equal("VisitPaymentChanged", wireDoc.RootElement.GetProperty("eventType").GetString());
                Assert.Equal(visit.Id, wireDoc.RootElement.GetProperty("visitId").GetInt64());
                Assert.DoesNotContain("Nguyễn", wireJson, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("0909", wireJson, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("150000", wireJson, StringComparison.OrdinalIgnoreCase);
            }

            await hub.StopAsync();

            using (var paidCallNextResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/queue/service-points/sp-{fx.ExamServicePoint1Id}/call-next",
                new CallNextPatientRequest(Notes: "Gọi bệnh nhân sau khi hoàn tất phí khám"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.OK, paidCallNextResp.StatusCode);
                var called = (await paidCallNextResp.Content.ReadFromJsonAsync<CallNextPatientResponseDto>(WebJson))!;
                Assert.NotNull(called.CalledEntry);
                Assert.Equal(visitCode, called.CalledEntry!.VisitCode);
            }

            using (var openResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/open",
                new { },
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, openResp.StatusCode);
                var wsDto = (await openResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
                Assert.Equal("IN_PROGRESS", wsDto.WorkspaceState);
            }

            // STEP 4: Clinical findings + PROVISIONAL dx + 2 mandatory CLS (BloodTest 200k verify=true, Ultrasound 150k verify=false)
            using (var examRecResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/clinical-examination",
                new RecordClinicalExaminationRequest(
                    ClinicalFindings: "Ấn tức nhẹ vùng thượng vị, tim đều, phổi trong."),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, examRecResp.StatusCode);
            }

            using (var provDxResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnosis",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: fx.Icd10Code,
                    ClinicalConclusion: "Theo dõi viêm dạ dày tá tràng cấp",
                    IsConfirmed: false),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, provDxResp.StatusCode);
            }

            ConsultationWorkspaceResponseDto orderedWs;
            using (var orderResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnostic-orders",
                new CreateDiagnosticOrderRequest(
                    TransitionToAwaitingResults: false,
                    Items:
                    [
                        new CreateDiagnosticOrderItemRequest(
                            ServiceId: fx.BloodTestServiceId,
                            Instruction: "Xét nghiệm công thức máu bắt buộc kết luận",
                            Priority: "URGENT",
                            RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(
                            ServiceId: fx.UltrasoundServiceId,
                            Instruction: "Siêu âm ổ bụng bắt buộc kết luận",
                            Priority: "NORMAL",
                            RequiredForConclusion: true),
                    ]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, orderResp.StatusCode);
                orderedWs = (await orderResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
                Assert.Equal(2, orderedWs.CreatedDiagnosticOrders!.Count);
            }

            var bloodOrderSummary = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.BloodTestServiceId);
            var ultrasoundOrderSummary = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.UltrasoundServiceId);
            var bloodOrderCode = bloodOrderSummary.OrderCode;
            var ultrasoundOrderCode = ultrasoundOrderSummary.OrderCode;

            Db.ChangeTracker.Clear();
            var bloodCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == bloodOrderSummary.OrderId);
            var ultrasoundCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == ultrasoundOrderSummary.OrderId);
            Assert.Equal(200_000m, bloodCharge.UnitPrice);
            Assert.Equal(150_000m, ultrasoundCharge.UnitPrice);

            using (var confirmUnpaidBlood = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{bloodOrderCode}/confirm",
                new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true),
                labStaff1Token))
            {
                Assert.Equal(HttpStatusCode.Conflict, confirmUnpaidBlood.StatusCode);
            }

            using (var confirmUnpaidUs = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{ultrasoundOrderCode}/confirm",
                new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true),
                labStaff1Token))
            {
                Assert.Equal(HttpStatusCode.Conflict, confirmUnpaidUs.StatusCode);
            }

            // STEP 5 & 6: Pay ONLY BloodTest (200k) -> UC40..UC42 -> PERFORMED_AWAITING_RESULT; Ultrasound still 409;
            // PROVISIONAL dx & DRAFT Rx allowed; CONFIRMED dx, ISSUED Rx, CompleteVisit blocked 409!
            await IssueSingleQrAndSettleAsync(visitCode, bloodCharge.Id, 200_000m, patientToken, $"FT-E2E01-BLD-{fx.Suffix}");

            using (var confirmPaidBlood = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{bloodOrderCode}/confirm",
                new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true, ServicePointId: fx.LabServicePointId),
                labStaff1Token))
            {
                Assert.Equal(HttpStatusCode.OK, confirmPaidBlood.StatusCode);
            }

            using (var confirmStillUnpaidUs = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{ultrasoundOrderCode}/confirm",
                new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true, ServicePointId: fx.ImagingServicePointId),
                labStaff1Token))
            {
                Assert.Equal(HttpStatusCode.Conflict, confirmStillUnpaidUs.StatusCode);
            }

            // Stage E2 (QE-23 / PE-06): the clinical session must be handed off before CLS execution.
            using (var handoffResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/handoff-to-diagnostic",
                new HandoffToDiagnosticRequest(),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, handoffResp.StatusCode);
            }

            await ExecuteUc40ToUc42Async(bloodOrderCode, fx.LabServicePointId, visitCode, fx.PatientId, labStaff1Token);

            using (var wsGet = await SendJsonAsync(HttpMethod.Get, $"/api/v1/doctor/consultation/{visitCode}", null, doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, wsGet.StatusCode);
                var wsAfterUc42 = (await wsGet.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
                var bloodInWs = Assert.Single(wsAfterUc42.ClinicalContext.DiagnosticOrders!, o => o.OrderId == bloodOrderSummary.OrderId);
                Assert.Equal("PERFORMED_AWAITING_RESULT", bloodInWs.ResultStatus);
                Assert.False(bloodInWs.IsResultFinalized);
            }

            using (var provAllowedResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnosis",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: fx.Icd10Code,
                    ClinicalConclusion: "Chẩn đoán sơ bộ cập nhật khi chờ kết quả huyết học",
                    IsConfirmed: false),
                doctorToken))
            {
                // Stage E2 (QE-23): after handoff the visit is AWAITING_RESULTS and clinical writes are gated.
                Assert.Equal(HttpStatusCode.Conflict, provAllowedResp.StatusCode);
            }

            using (var draftRxAllowedResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/prescriptions",
                new CreatePrescriptionRequest(
                    Status: "DRAFT",
                    IssueImmediately: false,
                    FulfillmentChoice: "HOSPITAL_PHARMACY",
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineId: fx.MedicineId,
                            PrescribedQuantity: 10m,
                            SingleDoseQuantity: 1m,
                            SingleDoseUnit: "viên",
                            Frequency: "2 lần/ngày",
                            DurationDays: 5,
                            Instructions: "Uống trước ăn 30 phút"),
                    ]),
                doctorToken))
            {
                // Stage E2 (QE-23): clinical writes (incl. draft Rx) are gated post-handoff (AWAITING_RESULTS).
                Assert.Equal(HttpStatusCode.Conflict, draftRxAllowedResp.StatusCode);
            }

            using (var blockedConfirmedDx = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnosis",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: fx.Icd10Code,
                    ClinicalConclusion: "Cố xác nhận chẩn đoán khi chưa có KQ CLS",
                    IsConfirmed: true),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, blockedConfirmedDx.StatusCode);
            }

            using (var blockedIssuedRx = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/prescriptions",
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
                            Instructions: "Uống trước ăn 30 phút"),
                    ]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, blockedIssuedRx.StatusCode);
            }

            using (var blockedComplete = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/complete",
                new CompleteVisitRequest(DischargeInstructions: "Cố đóng lượt khám sớm"),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, blockedComplete.StatusCode);
            }

            // Stage E2 (QE-10): the handoff above already released the exam room into AWAITING_RESULTS, and the
            // legacy doctor-initiated return-to-doctor ticket mint is Forbidden (Reception-only). Assert the state.
            Db.ChangeTracker.Clear();
            Assert.Equal("AWAITING_RESULTS", (await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).Status);

            // STEP 7 & 8: Pay Ultrasound (150k) + parallel UC40..UC42
            await IssueSingleQrAndSettleAsync(visitCode, ultrasoundCharge.Id, 150_000m, patientToken, $"FT-E2E01-US-{fx.Suffix}");

            using (var confirmUsResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{ultrasoundOrderCode}/confirm",
                new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true, ServicePointId: fx.ImagingServicePointId),
                labStaff1Token))
            {
                Assert.Equal(HttpStatusCode.OK, confirmUsResp.StatusCode);
            }

            await ExecuteUc40ToUc42Async(ultrasoundOrderCode, fx.ImagingServicePointId, visitCode, fx.PatientId, labStaff1Token);

            // STEP 9: BloodTest DRAFT empty conclusion (200), PENDING_VERIFY, self-verify blocked (409), 2nd staff verifies;
            // Ultrasound FINALIZED with /storage/diagnostic-reports/... -> Visit == RETURN_CONSULTATION, ReadyForReturnAt != null, 1 READY queue
            using (var draftLabResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{bloodOrderCode}/result",
                new EnterDiagnosticResultRequest(
                    ResultText: "WBC 8.4 G/L; HGB 141 g/L; PLT 260 G/L.",
                    Conclusion: "",
                    ReportFileUrl: $"/storage/diagnostic-reports/{bloodOrderCode}.pdf"),
                labStaff1Token))
            {
                Assert.Equal(HttpStatusCode.OK, draftLabResp.StatusCode);
                var draftDto = (await draftLabResp.Content.ReadFromJsonAsync<EnterDiagnosticResultResponseDto>(WebJson))!;
                Assert.Equal("DRAFT", draftDto.ResultStatus);
            }

            using (var pendingVerifyResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{bloodOrderCode}/result",
                new EnterDiagnosticResultRequest(
                    ResultText: "WBC 8.4 G/L; HGB 141 g/L; PLT 260 G/L.",
                    Conclusion: "Công thức máu bình thường.",
                    ReportFileUrl: $"/storage/diagnostic-reports/{bloodOrderCode}.pdf",
                    Status: "PENDING_VERIFY",
                    IsFinalized: false,
                    CorrectionReason: "Chuyển trình trưởng khoa xét nghiệm duyệt."),
                labStaff1Token))
            {
                Assert.Equal(HttpStatusCode.OK, pendingVerifyResp.StatusCode);
            }

            using (var selfVerifyResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{bloodOrderCode}/result/verify",
                new VerifyDiagnosticResultRequest(
                    Conclusion: "Công thức máu bình thường.",
                    VerificationNote: "Tự thẩm định trái phép"),
                labStaff1Token))
            {
                Assert.Equal(HttpStatusCode.Conflict, selfVerifyResp.StatusCode);
            }

            using (var independentVerifyResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{bloodOrderCode}/result/verify",
                new VerifyDiagnosticResultRequest(
                    Conclusion: "Công thức máu bình thường, không thiếu máu.",
                    VerificationNote: "Đã thẩm định độc lập người thứ hai."),
                labStaff2Token))
            {
                Assert.Equal(HttpStatusCode.OK, independentVerifyResp.StatusCode);
                var verifiedDto = (await independentVerifyResp.Content.ReadFromJsonAsync<EnterDiagnosticResultResponseDto>(WebJson))!;
                Assert.Equal("VERIFIED", verifiedDto.ResultStatus);
                Assert.True(verifiedDto.IsFinalized);
            }

            Db.ChangeTracker.Clear();
            Assert.True(await Db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.HospitalId == fx.HospitalId && a.Action == "DIAGNOSTIC_RESULT_VERIFIED"));
            Assert.Equal("AWAITING_RESULTS", (await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).Status);

            using (var finalizeUsResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{ultrasoundOrderCode}/result",
                new EnterDiagnosticResultRequest(
                    ResultText: "Gan mật tụy lách kích thước bình thường, thành dạ dày dày nhẹ vùng hang vị.",
                    Conclusion: "Hình ảnh theo dõi viêm niêm mạc hang vị nhẹ.",
                    ReportFileUrl: $"/storage/diagnostic-reports/{ultrasoundOrderCode}.pdf",
                    Status: "FINALIZED",
                    IsFinalized: true),
                labStaff1Token))
            {
                Assert.Equal(HttpStatusCode.OK, finalizeUsResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            var visitReturn = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            // Stage E2 (QE-10): finalizing the last order stamps ReadyForReturnAt and holds AWAITING_RESULTS;
            // it does NOT auto-mint a RETURN_CONSULTATION ticket. Reception coordinates the single READY ticket.
            Assert.Equal("AWAITING_RESULTS", visitReturn.Status);
            Assert.NotNull(visitReturn.ReadyForReturnAt);
            Assert.Empty(await Db.QueueEntries.AsNoTracking()
                .Where(q =>
                    q.VisitId == visit.Id &&
                    q.QueueType == "RETURN_CONSULTATION" &&
                    q.Status != "CANCELLED" &&
                    q.Status != "COMPLETED")
                .ToListAsync());

            using (var enrollReturnResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/return-enrollment",
                new CreateReturnEnrollmentRequestDto(
                    Purpose: QueueCoordinationPurposes.ResultConclusion,
                    IdempotencyKey: $"e2e01-return-{visitCode}"),
                receptionToken))
            {
                Assert.True(enrollReturnResp.StatusCode == HttpStatusCode.OK, await enrollReturnResp.Content.ReadAsStringAsync());
            }

            Db.ChangeTracker.Clear();
            Assert.Equal("RETURN_CONSULTATION", (await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).Status);
            var singleReturnQueue = Assert.Single(await Db.QueueEntries.AsNoTracking()
                .Where(q =>
                    q.VisitId == visit.Id &&
                    q.QueueType == "RETURN_CONSULTATION" &&
                    q.Status != "CANCELLED" &&
                    q.Status != "COMPLETED")
                .ToListAsync());
            Assert.Equal("READY", singleReturnQueue.Status);

            // STEP 10: Reopen, record all 7 MedicalRecordRevision fields without overwriting ClinicalFindings,
            // supersede & confirm dx, issue HOSPITAL_PHARMACY Rx, CompleteVisit (FINALIZED EMR)
            using (var callReturnResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/queue/service-points/sp-{fx.ExamServicePoint1Id}/call-next",
                new CallNextPatientRequest(Notes: "Gọi bệnh nhân quay lại đọc kết quả CLS"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.OK, callReturnResp.StatusCode);
            }

            using (var reopenResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/open",
                new { },
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, reopenResp.StatusCode);
            }

            using (var reviewResultsResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnostic-results/review",
                new ReviewDiagnosticResultsRequest(
                    ClinicalInterpretation: "Công thức máu bình thường; siêu âm phù hợp viêm niêm mạc hang vị dạ dày nhẹ."),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, reviewResultsResp.StatusCode);
            }

            using (var progressionResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/clinical-examination",
                new RecordClinicalExaminationRequest(
                    ClinicalProgressionNote: "Bệnh nhân tỉnh, giảm đầy hơi sau nghỉ ngơi, sinh hiệu ổn định.",
                    TreatmentPlan: "Ức chế bơm proton 14 ngày kết hợp điều chỉnh chế độ ăn."),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, progressionResp.StatusCode);
            }

            using (var initialConfirmDxResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnosis",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: fx.SecondaryIcd10Code,
                    ClinicalConclusion: "Khó tiêu chức năng vùng thượng vị",
                    IsConfirmed: true),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, initialConfirmDxResp.StatusCode);
            }

            using (var confirmDxResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnosis",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: fx.Icd10Code,
                    ClinicalConclusion: "Viêm niêm mạc hang vị dạ dày cấp mức độ nhẹ",
                    IsConfirmed: true),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, confirmDxResp.StatusCode);
            }

            using (var issueRxResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/prescriptions",
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
                            Frequency: "2 lần/ngày",
                            DurationDays: 5,
                            Instructions: "Uống trước ăn sáng và tối 30 phút"),
                    ]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, issueRxResp.StatusCode);
            }

            using (var completeResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/complete",
                new CompleteVisitRequest(
                    DischargeInstructions: "Kiêng rượu bia, đồ chua cay, không thức khuya.",
                    FollowUpInstructions: "Tái khám nội tiêu hóa sau 14 ngày.",
                    FollowUpDate: today.AddDays(14),
                    WarningSigns: "Nôn ra máu, đi ngoài phân đen hoặc đau bụng dữ dội."),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);
                var completedDto = (await completeResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
                Assert.Equal("COMPLETED", completedDto.Visit.Status);
            }

            Db.ChangeTracker.Clear();
            var visitDiagnoses = await Db.VisitDiagnoses.AsNoTracking().Where(d => d.VisitId == visit.Id).ToListAsync();
            Assert.Contains(visitDiagnoses, d => d.Status == "SUPERSEDED" && d.CodeSnapshot == fx.SecondaryIcd10Code);
            Assert.Contains(visitDiagnoses, d => d.Status == "CONFIRMED" && d.IsPrimary == true && d.CodeSnapshot == fx.Icd10Code);

            var emr = await Db.MedicalRecords.AsNoTracking().SingleAsync(m => m.VisitId == visit.Id);
            Assert.Equal("FINALIZED", emr.Status);
            var latestRevision = await Db.MedicalRecordRevisions.AsNoTracking()
                .Where(r => r.MedicalRecordId == emr.Id)
                .OrderByDescending(r => r.RevisionNo)
                .FirstAsync();
            Assert.Equal("Ấn tức nhẹ vùng thượng vị, tim đều, phổi trong.", latestRevision.ClinicalFindings);
            Assert.Equal("Bệnh nhân tỉnh, giảm đầy hơi sau nghỉ ngơi, sinh hiệu ổn định.", latestRevision.ClinicalProgressionNote);
            Assert.Equal("Công thức máu bình thường; siêu âm phù hợp viêm niêm mạc hang vị dạ dày nhẹ.", latestRevision.ClinicalInterpretation);
            Assert.Equal("Ức chế bơm proton 14 ngày kết hợp điều chỉnh chế độ ăn.", latestRevision.TreatmentPlan);
            Assert.Equal("Kiêng rượu bia, đồ chua cay, không thức khuya.", latestRevision.DischargeInstructions);
            Assert.Equal("Tái khám nội tiêu hóa sau 14 ngày.", latestRevision.FollowUpInstructions);
            Assert.Equal(today.AddDays(14), latestRevision.FollowUpDate);
            Assert.Equal("Nôn ra máu, đi ngoài phân đen hoặc đau bụng dữ dội.", latestRevision.WarningSigns);

            var issuedRx = await Db.Prescriptions.AsNoTracking()
                .SingleAsync(p => p.VisitId == visit.Id && p.Status == "ISSUED");
            var rxCode = issuedRx.PrescriptionCode!;
            var rxItem = await Db.PrescriptionItems.AsNoTracking()
                .SingleAsync(pi => pi.PrescriptionId == issuedRx.Id);

            // STEP 11: Pharmacy multi-batch FEFO (6 + 4), DispensedQuantity == null before dispense,
            // idempotent ${prescriptionId}:${attemptId} replay (200), StockTransactions (-6, -4), SCR-PHA-04 receipt (ADR-0047)
            var batchA = fx.BatchIds[0];
            var batchB = fx.BatchIds[1];

            using (var fefoSelectResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/prescriptions/{rxCode}/fefo-batches",
                new SelectFefoBatchesRequest(
                [
                    new SelectFefoBatchItemRequest(rxItem.Id, MedicineBatchId: batchA, PickedQuantity: 6m),
                    new SelectFefoBatchItemRequest(rxItem.Id, MedicineBatchId: batchB, PickedQuantity: 4m),
                ]),
                pharmacyToken))
            {
                Assert.Equal(HttpStatusCode.OK, fefoSelectResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            var preDispenseRows = await Db.DispenseItems.AsNoTracking()
                .Where(di => di.PrescriptionItemId == rxItem.Id)
                .ToListAsync();
            Assert.Equal(2, preDispenseRows.Count);
            Assert.All(preDispenseRows, di => Assert.Null(di.DispensedQuantity));

            using (var prepResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/prescriptions/{rxCode}/prepare",
                new PreparePrescriptionRequest(
                    Items:
                    [
                        new PrepareMedicineItemRequest(rxItem.Id, MedicineBatchId: batchA, BatchCode: null, PickedQuantity: 6m),
                        new PrepareMedicineItemRequest(rxItem.Id, MedicineBatchId: batchB, BatchCode: null, PickedQuantity: 4m),
                    ],
                    Notes: "Soạn thuốc FEFO đa lô 6 + 4 viên"),
                pharmacyToken))
            {
                Assert.Equal(HttpStatusCode.OK, prepResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            Assert.All(
                await Db.DispenseItems.AsNoTracking().Where(di => di.PrescriptionItemId == rxItem.Id).ToListAsync(),
                di => Assert.Null(di.DispensedQuantity));

            var idempotencyKey = $"{issuedRx.Id}:attempt-01";
            DispensingResultDetailDto dispenseResult;
            using (var confirmDispResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/prescriptions/{rxCode}/dispense",
                new ConfirmDispenseRequest(
                    ReceiverName: "Nguyễn Văn Bệnh Nhân",
                    Relationship: "Bản thân",
                    IsIdentityVerified: true,
                    ChecksConfirmed: true,
                    IdempotencyKey: idempotencyKey,
                    Items:
                    [
                        new ConfirmDispenseItemRequest(rxItem.Id, batchA, 6m),
                        new ConfirmDispenseItemRequest(rxItem.Id, batchB, 4m),
                    ]),
                pharmacyToken))
            {
                Assert.Equal(HttpStatusCode.OK, confirmDispResp.StatusCode);
                dispenseResult = (await confirmDispResp.Content.ReadFromJsonAsync<DispensingResultDetailDto>(WebJson))!;
            }

            using (var replayDispResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/prescriptions/{rxCode}/dispense",
                new ConfirmDispenseRequest(
                    ReceiverName: "Nguyễn Văn Bệnh Nhân",
                    Relationship: "Bản thân",
                    IsIdentityVerified: true,
                    ChecksConfirmed: true,
                    IdempotencyKey: idempotencyKey,
                    Items:
                    [
                        new ConfirmDispenseItemRequest(rxItem.Id, batchA, 6m),
                        new ConfirmDispenseItemRequest(rxItem.Id, batchB, 4m),
                    ]),
                pharmacyToken))
            {
                Assert.Equal(HttpStatusCode.OK, replayDispResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            Assert.Equal(0m, (await Db.MedicineBatches.AsNoTracking().SingleAsync(b => b.Id == batchA)).QuantityOnHand);
            Assert.Equal(6m, (await Db.MedicineBatches.AsNoTracking().SingleAsync(b => b.Id == batchB)).QuantityOnHand);

            var stockTransactions = await Db.StockTransactions.AsNoTracking()
                .Where(st => st.MedicineBatchId == batchA || st.MedicineBatchId == batchB)
                .OrderBy(st => st.MedicineBatchId)
                .ToListAsync();
            Assert.Equal(2, stockTransactions.Count);
            Assert.Equal(-6m, stockTransactions[0].QuantityDelta);
            Assert.Equal(-4m, stockTransactions[1].QuantityDelta);

            using (var scrPha04Resp = await SendJsonAsync(
                HttpMethod.Get,
                $"/api/v1/pharmacy/dispenses/{dispenseResult.DispenseCode}",
                null,
                pharmacyToken))
            {
                Assert.Equal(HttpStatusCode.OK, scrPha04Resp.StatusCode);
                var receipt = (await scrPha04Resp.Content.ReadFromJsonAsync<DispensingResultDetailDto>(WebJson))!;
                Assert.Null(receipt.TotalAmount);
                Assert.Equal("Chưa có dữ liệu", receipt.TotalAmountFormatted);
                Assert.All(receipt.Items, item =>
                {
                    Assert.Null(item.UnitPrice);
                    Assert.Null(item.LineTotal);
                    Assert.Equal("Chưa có dữ liệu", item.UnitPriceFormatted);
                    Assert.Equal("Chưa có dữ liệu", item.LineTotalFormatted);
                });
            }

            // STEP 12: Patient Portal EMR, diagnostic results, protected PDF report (200 owner, 403 stranger), prescription, and charges
            using (var emrPortalResp = await SendJsonAsync(HttpMethod.Get, $"/api/v1/patients/me/medical-records/{visitCode}", null, patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, emrPortalResp.StatusCode);
                var emrDetail = (await emrPortalResp.Content.ReadFromJsonAsync<PatientMedicalRecordDetailDto>(WebJson))!;
                Assert.Equal("Ấn tức nhẹ vùng thượng vị, tim đều, phổi trong.", emrDetail.ClinicalFindings);
                Assert.Equal("Bệnh nhân tỉnh, giảm đầy hơi sau nghỉ ngơi, sinh hiệu ổn định.", emrDetail.ClinicalProgressionNote);
                Assert.Equal("Công thức máu bình thường; siêu âm phù hợp viêm niêm mạc hang vị dạ dày nhẹ.", emrDetail.ClinicalInterpretation);
                Assert.Equal("Ức chế bơm proton 14 ngày kết hợp điều chỉnh chế độ ăn.", emrDetail.TreatmentPlan);
                Assert.Equal("Kiêng rượu bia, đồ chua cay, không thức khuya.", emrDetail.DischargeInstructions);
                Assert.Equal("Tái khám nội tiêu hóa sau 14 ngày.", emrDetail.FollowUpInstructions);
                Assert.Equal(today.AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), emrDetail.FollowUpDate);
                Assert.Equal("Nôn ra máu, đi ngoài phân đen hoặc đau bụng dữ dội.", emrDetail.WarningSigns);
            }

            using (var diagListResp = await SendJsonAsync(
                HttpMethod.Get,
                $"/api/v1/patients/me/diagnostic-results?visitCode={Uri.EscapeDataString(visitCode)}",
                null,
                patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, diagListResp.StatusCode);
                var diagList = (await diagListResp.Content.ReadFromJsonAsync<PatientDiagnosticResultListResponseDto>(WebJson))!;
                Assert.Equal(2, diagList.Items.Count);
            }

            using (var ownReportResp = await SendJsonAsync(HttpMethod.Get, $"/api/v1/diagnostic/results/{bloodOrderCode}/report-file", null, patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, ownReportResp.StatusCode);
                Assert.Equal("application/pdf", ownReportResp.Content.Headers.ContentType?.MediaType);
            }

            using (var strangerReportResp = await SendJsonAsync(HttpMethod.Get, $"/api/v1/diagnostic/results/{bloodOrderCode}/report-file", null, strangerToken))
            {
                Assert.Equal(HttpStatusCode.Forbidden, strangerReportResp.StatusCode);
            }

            using (var rxPortalResp = await SendJsonAsync(HttpMethod.Get, $"/api/v1/patients/me/prescriptions/{rxCode}", null, patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, rxPortalResp.StatusCode);
                var rxDetail = (await rxPortalResp.Content.ReadFromJsonAsync<PatientPrescriptionDetailDto>(WebJson))!;
                Assert.Equal(rxCode, rxDetail.Prescription.PrescriptionCode);
                Assert.Single(rxDetail.Items);
            }

            using (var chargesResp = await SendJsonAsync(HttpMethod.Get, $"/api/v1/visits/{visitCode}/charges", null, patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, chargesResp.StatusCode);
                var chargesDto = (await chargesResp.Content.ReadFromJsonAsync<VisitChargesResponseDto>(WebJson))!;
                Assert.Equal(VisitChargesService.SettlementPaid, chargesDto.SettlementStatus);
                Assert.Equal(0m, chargesDto.AmountDue);
                Assert.Equal(500_000m, chargesDto.RecognizedPaidTotal);
                Assert.Equal(3, chargesDto.Charges.Count);
                Assert.All(chargesDto.Charges, line =>
                {
                    Assert.Equal(VisitChargesService.SettlementPaid, line.LineSettlementStatus);
                    Assert.False(line.CanPayByQr);
                });
            }

            Db.ChangeTracker.Clear();
            var dbPayments = await Db.Payments.AsNoTracking()
                .Where(p => p.VisitId == visit.Id && p.Status == PaymentStatuses.Paid)
                .ToListAsync();
            var dbPaidAllocations = await Db.PaymentAllocations.AsNoTracking()
                .Where(pa => pa.VisitId == visit.Id && pa.Status == PaymentAllocationStatuses.Paid)
                .ToListAsync();
            Assert.Equal(3, dbPayments.Count);
            Assert.Equal(3, dbPaidAllocations.Count);
            Assert.Equal(500_000m, dbPaidAllocations.Sum(pa => pa.AllocatedAmount));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // E2E-02: Grouped QR at Doctor Workspace + 3-Attribute Orthogonality + EXTERNAL_PHARMACY
    // =========================================================================

    [Fact]
    public async Task E2E02_GroupedQrAtDoctorWorkspace_TwoMandatoryAndOneOptional_DeclineOptional_SingleGroupedQr_AndExternalPharmacy()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [20m]);
        try
        {
            var patientToken = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var receptionToken = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var doctorToken = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);
            var diag1Token = await CreateSessionJwtAsync(fx.DiagStaff1UserId, RoleNames.DiagnosticStaff);
            var diag2Token = await CreateSessionJwtAsync(fx.DiagStaff2UserId, RoleNames.DiagnosticStaff);
            var pharmacyToken = await CreateSessionJwtAsync(fx.PharmacistUserId, RoleNames.PharmacyStaff);

            var visit = await CreatePaidExamVisitReadyForConsultationAsync(fx, "E2E02", patientToken, doctorToken);
            var visitCode = visit.VisitCode!;

            using (var examNoteResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/clinical-examination",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Bụng mềm, huyết áp 135/85 mmHg."),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, examNoteResp.StatusCode);
            }

            // Order 2 mandatory CLS (200k + 150k), 1 optional CLS (100k), and 1 free mandatory BP (0đ, WAIVED, verify=false)
            ConsultationWorkspaceResponseDto orderedWs;
            using (var orderResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnostic-orders",
                new CreateDiagnosticOrderRequest(
                    TransitionToAwaitingResults: true,
                    Items:
                    [
                        new CreateDiagnosticOrderItemRequest(
                            ServiceId: fx.BloodTestServiceId,
                            Instruction: "Xét nghiệm máu bắt buộc (200k, thẩm định 2 người)",
                            Priority: "URGENT",
                            RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(
                            ServiceId: fx.UltrasoundServiceId,
                            Instruction: "Siêu âm ổ bụng bắt buộc (150k)",
                            Priority: "NORMAL",
                            RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(
                            ServiceId: fx.OptionalUltrasoundServiceId,
                            Instruction: "Siêu âm tầm soát tùy chọn (100k)",
                            Priority: "NORMAL",
                            RequiredForConclusion: false),
                        new CreateDiagnosticOrderItemRequest(
                            ServiceId: fx.FreeBpServiceId,
                            Instruction: "Đo huyết áp miễn phí bắt buộc kết luận (0đ)",
                            Priority: "NORMAL",
                            RequiredForConclusion: true),
                    ]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, orderResp.StatusCode);
                orderedWs = (await orderResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
                Assert.Equal(4, orderedWs.CreatedDiagnosticOrders!.Count);
            }

            var bloodOrder = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.BloodTestServiceId);
            var ultrasoundOrder = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.UltrasoundServiceId);
            var optOrder = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.OptionalUltrasoundServiceId);
            var freeBpOrder = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.FreeBpServiceId);

            Db.ChangeTracker.Clear();
            var bloodCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == bloodOrder.OrderId);
            var ultrasoundCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == ultrasoundOrder.OrderId);
            var optCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == optOrder.OrderId);
            var freeBpCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == freeBpOrder.OrderId);
            Assert.Equal(0m, freeBpCharge.UnitPrice);
            Assert.Equal("WAIVED", freeBpCharge.PaymentRequirement);

            // Decline optional CLS -> SelectionStatus = DECLINED, ChargeStatus = CANCELLED
            using (var declineResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/diagnostic-orders/{optOrder.OrderId}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Bệnh nhân từ chối siêu âm tầm soát tùy chọn"),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, declineResp.StatusCode);
                var declineDto = (await declineResp.Content.ReadFromJsonAsync<DeclineDiagnosticOrderResponseDto>(WebJson))!;
                Assert.Equal(DiagnosticSelectionStatuses.Declined, declineDto.SelectionStatus);
                Assert.Equal(VisitChargeStatuses.Cancelled, declineDto.ChargeStatus);
            }

            // DOCTOR gets 403 Forbidden on /payments/cash
            using (var docCashResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [bloodCharge.Id, ultrasoundCharge.Id],
                    IdempotencyKey: $"doc-forbid-{fx.Suffix}"),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.Forbidden, docCashResp.StatusCode);
            }

            // Assigned DOCTOR issues grouped QR for [bloodCharge.Id, ultrasoundCharge.Id] = 350,000m
            GenerateQrPaymentResponseDto groupedQr;
            using (var docQrResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/visits/{visitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [bloodCharge.Id, ultrasoundCharge.Id]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.Created, docQrResp.StatusCode);
                groupedQr = (await docQrResp.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
                Assert.Equal(350_000m, groupedQr.Amount);
                Assert.Null(groupedQr.VisitChargeId);
                Assert.Equal(2, groupedQr.Allocations!.Count);
            }

            Db.ChangeTracker.Clear();
            var groupedPaymentPending = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == groupedQr.PaymentId);
            Assert.Equal(PaymentScopes.Allocated, groupedPaymentPending.PaymentScope);
            var pendingAllocs = await Db.PaymentAllocations.AsNoTracking()
                .Where(pa => pa.PaymentId == groupedQr.PaymentId)
                .ToListAsync();
            Assert.Equal(2, pendingAllocs.Count);
            Assert.All(pendingAllocs, pa => Assert.Equal(PaymentAllocationStatuses.Pending, pa.Status));

            // SePay settles 350,000m -> both mandatory charges PAID atomically while optional stays CANCELLED
            var groupedOutcome = await PostSePayWebhookAsync(groupedQr.PaymentCode, 350_000m, $"FT-E2E02-GRP-{fx.Suffix}");
            Assert.Equal(PaymentGatewayOutcomes.Paid, groupedOutcome);

            Db.ChangeTracker.Clear();
            Assert.All(
                await Db.PaymentAllocations.AsNoTracking().Where(pa => pa.PaymentId == groupedQr.PaymentId).ToListAsync(),
                pa => Assert.Equal(PaymentAllocationStatuses.Paid, pa.Status));
            Assert.Equal(VisitChargeStatuses.Cancelled, (await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.Id == optCharge.Id)).Status);

            // Execute UC39..UC42 for BloodTest, Ultrasound, and Free Mandatory BP (0đ needs 0 payment!)
            await ConfirmAndExecuteUc40ToUc42Async(bloodOrder.OrderCode, fx.LabServicePointId, visitCode, fx.PatientId, diag1Token);
            await ConfirmAndExecuteUc40ToUc42Async(ultrasoundOrder.OrderCode, fx.ImagingServicePointId, visitCode, fx.PatientId, diag1Token);
            await ConfirmAndExecuteUc40ToUc42Async(freeBpOrder.OrderCode, fx.FunctionalServicePointId, visitCode, fx.PatientId, diag1Token);

            // Finalize BloodTest (2-person verify) & Ultrasound -> Free Mandatory BP STILL blocks CompleteVisit until finalized!
            await SubmitAndVerifyTwoPersonResultAsync(bloodOrder.OrderCode, "WBC 7.8 G/L; HGB 140 g/L.", "Công thức máu bình thường.", diag1Token, diag2Token);
            await FinalizeSinglePersonResultAsync(ultrasoundOrder.OrderCode, "Ổ bụng bình thường.", "Siêu âm ổ bụng bình thường.", diag1Token);

            Db.ChangeTracker.Clear();
            Assert.Equal("AWAITING_RESULTS", (await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id)).Status);
            using (var blockedByFreeBp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/complete",
                new CompleteVisitRequest(ConfirmNoPrescription: true, DischargeInstructions: "Thử kết thúc khi Free BP chưa có kết quả"),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, blockedByFreeBp.StatusCode);
            }

            // Finalize Free Mandatory BP -> RETURN_CONSULTATION -> EXTERNAL_PHARMACY Rx + CompleteVisit
            await FinalizeSinglePersonResultAsync(freeBpOrder.OrderCode, "Huyết áp nghỉ 122/78 mmHg.", "Huyết áp ổn định.", diag1Token);

            // Stage E2 (QE-10): all mandatory results finalized → visit AWAITING_RESULTS; Reception coordinates
            // the RETURN_CONSULTATION ticket before the doctor is called back in to issue the Rx & complete.
            using (var enrollResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/return-enrollment",
                new CreateReturnEnrollmentRequestDto(
                    Purpose: QueueCoordinationPurposes.ResultConclusion,
                    IdempotencyKey: $"e2e02-return-{visitCode}"),
                receptionToken))
            {
                Assert.True(enrollResp.StatusCode == HttpStatusCode.OK, await enrollResp.Content.ReadAsStringAsync());
            }

            using (await SendJsonAsync(HttpMethod.Post, $"/api/v1/queue/service-points/sp-{fx.ExamServicePoint1Id}/call-next", new CallNextPatientRequest(), receptionToken)) { }
            using (await SendJsonAsync(HttpMethod.Post, $"/api/v1/doctor/consultation/{visitCode}/open", new { }, doctorToken)) { }
            using (await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnosis",
                new RecordDiagnosisRequest(PrimaryIcd10Code: fx.Icd10Code, ClinicalConclusion: "Rối loạn tiêu hóa nhẹ ổn định", IsConfirmed: true),
                doctorToken))
            {
            }

            using (var extRxResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/prescriptions",
                new CreatePrescriptionRequest(
                    Status: "ISSUED",
                    IssueImmediately: true,
                    FulfillmentChoice: "EXTERNAL_PHARMACY",
                    Items:
                    [
                        new CreatePrescriptionItemRequest(
                            MedicineId: fx.MedicineId,
                            PrescribedQuantity: 14m,
                            SingleDoseQuantity: 1m,
                            SingleDoseUnit: "viên",
                            Frequency: "2 lần/ngày",
                            DurationDays: 7,
                            Instructions: "Mua thuốc tại nhà thuốc ngoài bệnh viện"),
                    ]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, extRxResp.StatusCode);
            }

            using (var completeResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/complete",
                new CompleteVisitRequest(DischargeInstructions: "Theo dõi tại nhà."),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            var extRx = await Db.Prescriptions.AsNoTracking().SingleAsync(p => p.VisitId == visit.Id && p.Status == "ISSUED");
            var extRxItem = await Db.PrescriptionItems.AsNoTracking().SingleAsync(pi => pi.PrescriptionId == extRx.Id);

            // Hospital Pharmacy FEFO / Prepare / Dispense all reject EXTERNAL_PHARMACY (409)
            using (var fefoExt = await SendJsonAsync(HttpMethod.Get, $"/api/v1/pharmacy/prescriptions/{extRx.PrescriptionCode}/fefo-batches", null, pharmacyToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, fefoExt.StatusCode);
            }

            using (var prepExt = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/prescriptions/{extRx.PrescriptionCode}/prepare",
                new PreparePrescriptionRequest(Items: [new PrepareMedicineItemRequest(extRxItem.Id, MedicineBatchId: fx.BatchIds[0], BatchCode: null, PickedQuantity: 14m)]),
                pharmacyToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, prepExt.StatusCode);
            }

            using (var dispExt = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/pharmacy/prescriptions/{extRx.PrescriptionCode}/dispense",
                new ConfirmDispenseRequest(
                    ReceiverName: "Nguyễn Văn Bệnh Nhân",
                    IsIdentityVerified: true,
                    ChecksConfirmed: true,
                    IdempotencyKey: $"{extRx.Id}:ext-02",
                    Items: [new ConfirmDispenseItemRequest(extRxItem.Id, fx.BatchIds[0], 14m)]),
                pharmacyToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, dispExt.StatusCode);
            }

            // Assert exact 2 PAID Payments (150k exam + 350k grouped CLS) and 3 priced PAID charges (500,000m)
            var paidPayments = await Db.Payments.AsNoTracking()
                .Where(p => p.VisitId == visit.Id && p.Status == PaymentStatuses.Paid)
                .ToListAsync();
            var paidAllocations = await Db.PaymentAllocations.AsNoTracking()
                .Where(pa => pa.VisitId == visit.Id && pa.Status == PaymentAllocationStatuses.Paid)
                .ToListAsync();
            Assert.Equal(2, paidPayments.Count);
            Assert.Equal(3, paidAllocations.Count);
            Assert.Equal(500_000m, paidPayments.Sum(p => p.Amount ?? 0m));
            Assert.Equal(500_000m, paidAllocations.Sum(pa => pa.AllocatedAmount));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // E2E-03: Reception Cash Collection (Single & Grouped), Permission Enforced,
    // Receipt Audit, Competing Pending QR Cancelled, and Late Webhook DUPLICATE_RECONCILE
    // =========================================================================

    [Fact]
    public async Task E2E03_ReceptionCashCollection_SingleAndGrouped_PermissionEnforced_ReceiptAudit_AndWebhookNoDoubleSettle()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 11, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var patientToken = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var receptionToken = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var doctorToken = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);
            var diag1Token = await CreateSessionJwtAsync(fx.DiagStaff1UserId, RoleNames.DiagnosticStaff);
            var diag2Token = await CreateSessionJwtAsync(fx.DiagStaff2UserId, RoleNames.DiagnosticStaff);

            var visit = await CreateUnpaidCheckedInVisitAsync(fx, "E2E03");
            var visit2 = await CreateUnpaidCheckedInVisitAsync(fx, "E2E03B");
            var visitCode = visit.VisitCode!;

            var examCharge = await Db.VisitCharges.AsNoTracking()
                .SingleAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Examination);
            var visit2ExamCharge = await Db.VisitCharges.AsNoTracking()
                .SingleAsync(c => c.VisitId == visit2.Id && c.ChargeType == VisitChargeTypes.Examination);

            // DOCTOR -> 403 on /payments/cash
            using (var docCash = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [examCharge.Id], IdempotencyKey: $"doc-e03-{fx.Suffix}"),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.Forbidden, docCash.StatusCode);
            }

            // Receptionist WITHOUT PAYMENT_COLLECT -> 403 on /payments/cash
            var recRole = await Db.Roles.AsNoTracking().SingleAsync(r => r.Code == RoleNames.Reception);
            var payCollectPerm = await Db.Permissions.AsNoTracking().SingleAsync(p => p.Code == PermissionCodes.PaymentCollect);
            await Db.RolePermissions
                .Where(rp => rp.RoleId == recRole.Id && rp.PermissionId == payCollectPerm.Id)
                .ExecuteDeleteAsync();
            try
            {
                using var noPermCash = await SendJsonAsync(
                    HttpMethod.Post,
                    $"/api/v1/reception/visits/{visitCode}/payments/cash",
                    new CollectCashPaymentRequestDto(VisitChargeIds: [examCharge.Id], IdempotencyKey: $"noperm-e03-{fx.Suffix}"),
                    receptionToken);
                Assert.Equal(HttpStatusCode.Forbidden, noPermCash.StatusCode);
                Assert.Contains("PAYMENT_COLLECT", await noPermCash.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            finally
            {
                Db.RolePermissions.Add(new RolePermission { RoleId = recRole.Id, PermissionId = payCollectPerm.Id });
                await Db.SaveChangesAsync();
            }

            // Receptionist WITH PAYMENT_COLLECT collects Exam (150k) via Cash + Idempotency replay (200) + mismatch (409)
            var examCashKey = $"cash-exam-e03-{fx.Suffix}";
            CollectCashPaymentResponseDto examCashDto;
            using (var examCashResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [examCharge.Id],
                    IdempotencyKey: examCashKey,
                    Notes: "Thu tiền mặt phí khám ban đầu"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.Created, examCashResp.StatusCode);
                examCashDto = (await examCashResp.Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;
                Assert.Equal(150_000m, examCashDto.Amount);
                Assert.Equal(fx.ReceptionStaffId, examCashDto.CollectedByStaffId);
                Assert.False(examCashDto.IsIdempotentReplay);
            }

            using (var replayResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [examCharge.Id], IdempotencyKey: examCashKey),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.OK, replayResp.StatusCode);
                var replayDto = (await replayResp.Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;
                Assert.True(replayDto.IsIdempotentReplay);
                Assert.Equal(examCashDto.PaymentId, replayDto.PaymentId);
            }

            using (var mismatchResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visit2.VisitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [visit2ExamCharge.Id], IdempotencyKey: examCashKey),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, mismatchResp.StatusCode);
                Assert.Contains("IDEMPOTENCY_PAYLOAD_MISMATCH", await mismatchResp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            // Doctor consults & orders 2 mandatory CLS (200k + 150k) + 1 optional CLS (100k)
            using (await SendJsonAsync(HttpMethod.Post, $"/api/v1/queue/service-points/sp-{fx.ExamServicePoint1Id}/call-next", new CallNextPatientRequest(), receptionToken)) { }
            using (await SendJsonAsync(HttpMethod.Post, $"/api/v1/doctor/consultation/{visitCode}/open", new { }, doctorToken)) { }
            using (await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/clinical-examination",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Đau bụng âm ỉ, chỉ định 3 dịch vụ CLS."),
                doctorToken))
            {
            }

            ConsultationWorkspaceResponseDto orderedWs;
            using (var orderResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnostic-orders",
                new CreateDiagnosticOrderRequest(
                    TransitionToAwaitingResults: true,
                    Items:
                    [
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.BloodTestServiceId, Instruction: "CLS 1 (200k)", RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.UltrasoundServiceId, Instruction: "CLS 2 (150k)", RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.OptionalUltrasoundServiceId, Instruction: "CLS 3 tùy chọn (100k)", RequiredForConclusion: false),
                    ]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, orderResp.StatusCode);
                orderedWs = (await orderResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
            }

            var ord1 = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.BloodTestServiceId);
            var ord2 = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.UltrasoundServiceId);
            var ord3 = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.OptionalUltrasoundServiceId);

            // Patient accepts optional CLS via /selection and opens a competing PENDING grouped QR (450,000m)
            using (var selResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/diagnostic-orders/selection",
                new UpdateDiagnosticOrderSelectionRequestDto(AcceptedOrderIds: [ord3.OrderId]),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, selResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            var chg1 = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == ord1.OrderId);
            var chg2 = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == ord2.OrderId);
            var chg3 = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == ord3.OrderId);

            GenerateQrPaymentResponseDto competingQr;
            using (var qrResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [chg1.Id, chg2.Id, chg3.Id]),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.Created, qrResp.StatusCode);
                competingQr = (await qrResp.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
                Assert.Equal(450_000m, competingQr.Amount);
            }

            // Reception collects grouped CASH (450,000m) for all 3 CLS charges -> competing QR becomes CANCELLED
            CollectCashPaymentResponseDto groupedCashDto;
            using (var grpCashResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/payments/cash",
                new CollectCashPaymentRequestDto(
                    VisitChargeIds: [chg1.Id, chg2.Id, chg3.Id],
                    IdempotencyKey: $"cash-grp-e03-{fx.Suffix}",
                    Notes: "Thu gộp tiền mặt 3 CLS tại quầy thu ngân"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.Created, grpCashResp.StatusCode);
                groupedCashDto = (await grpCashResp.Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;
                Assert.Equal(450_000m, groupedCashDto.Amount);
                Assert.Equal(fx.ReceptionStaffId, groupedCashDto.CollectedByStaffId);
                Assert.Equal(3, groupedCashDto.Allocations.Count);
            }

            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Cancelled, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == competingQr.PaymentId)).Status);
            Assert.True(await Db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.EntityId == groupedCashDto.PaymentId.ToString(CultureInfo.InvariantCulture) &&
                a.Action == CollectCashPaymentService.AuditAction));

            // Late SePay webhook on cancelled QR records DUPLICATE_RECONCILE without double-settling or breaking PAID status
            var lateOutcome = await PostSePayWebhookAsync(competingQr.PaymentCode, 450_000m, $"FT-E2E03-LATE-{fx.Suffix}");
            Assert.Equal(SePayWebhookService.DuplicateChargeReconcileOutcome, lateOutcome);

            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Paid, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == groupedCashDto.PaymentId)).Status);
            Assert.Equal(SePayWebhookService.MismatchPaymentStatus, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == competingQr.PaymentId)).Status);
            Assert.Equal(4, await Db.PaymentAllocations.AsNoTracking().CountAsync(pa => pa.VisitId == visit.Id && pa.Status == PaymentAllocationStatuses.Paid));

            // Execute all 3 CLS & CompleteVisit
            // Stage E2 (strict single-active per patient): visit2 is a second same-patient visit used only for
            // cash/idempotency checks; its own active exam queue would block CLS execution on `visit`. Close it.
            await Db.QueueEntries
                .Where(q => q.VisitId == visit2.Id && q.CompletedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.Status, "COMPLETED")
                    .SetProperty(q => q.CompletedAt, HospitalClock.GetNow(_clock)));
            await ConfirmAndExecuteUc40ToUc42Async(ord1.OrderCode, fx.LabServicePointId, visitCode, fx.PatientId, diag1Token);
            await ConfirmAndExecuteUc40ToUc42Async(ord2.OrderCode, fx.ImagingServicePointId, visitCode, fx.PatientId, diag1Token);
            await ConfirmAndExecuteUc40ToUc42Async(ord3.OrderCode, fx.ImagingServicePointId, visitCode, fx.PatientId, diag1Token);

            await SubmitAndVerifyTwoPersonResultAsync(ord1.OrderCode, "WBC 7.5 G/L.", "Bình thường.", diag1Token, diag2Token);
            await FinalizeSinglePersonResultAsync(ord2.OrderCode, "Gan mật bình thường.", "Bình thường.", diag1Token);
            await FinalizeSinglePersonResultAsync(ord3.OrderCode, "Ổ bụng bình thường.", "Bình thường.", diag1Token);

            await CallReopenConfirmDxAndCompleteNoRxAsync(visitCode, fx.ExamServicePoint1Id, fx.Icd10Code, receptionToken, doctorToken);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // E2E-04: Selective Single CLS Payment, PATIENT_BUSY_AT_ANOTHER_POINT Guard,
    // Parallel Execution While Awaiting Sibling UC43 Result, and Optional Decline
    // =========================================================================

    [Fact]
    public async Task E2E04_SelectiveSingleClsPayment_ParallelExecutionWhileAwaitingSiblingResult_OptionalDecline_AndDirectApiUnpaidBlock()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 13, 30, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var patientToken = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var receptionToken = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var doctorToken = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);
            var diag1Token = await CreateSessionJwtAsync(fx.DiagStaff1UserId, RoleNames.DiagnosticStaff);
            var diag2Token = await CreateSessionJwtAsync(fx.DiagStaff2UserId, RoleNames.DiagnosticStaff);

            var visit = await CreatePaidExamVisitReadyForConsultationAsync(fx, "E2E04", patientToken, doctorToken);
            var visitCode = visit.VisitCode!;

            using (await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/clinical-examination",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Đau bụng vùng quanh rốn."),
                doctorToken))
            {
            }

            ConsultationWorkspaceResponseDto orderedWs;
            using (var orderResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnostic-orders",
                new CreateDiagnosticOrderRequest(
                    TransitionToAwaitingResults: true,
                    Items:
                    [
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.BloodTestServiceId, Instruction: "Mandatory A (200k)", RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.UltrasoundServiceId, Instruction: "Mandatory B (150k)", RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.OptionalUltrasoundServiceId, Instruction: "Optional C (100k)", RequiredForConclusion: false),
                    ]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, orderResp.StatusCode);
                orderedWs = (await orderResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
            }

            var orderA = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.BloodTestServiceId);
            var orderB = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.UltrasoundServiceId);
            var orderC = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.OptionalUltrasoundServiceId);

            // Direct API confirm / receive / start on unpaid A, B, C ALL return 409 Conflict!
            foreach (var unpaidCode in new[] { orderA.OrderCode, orderB.OrderCode, orderC.OrderCode })
            {
                using var cResp = await SendJsonAsync(
                    HttpMethod.Post,
                    $"/api/v1/diagnostic/orders/{unpaidCode}/confirm",
                    new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true),
                    diag1Token);
                Assert.Equal(HttpStatusCode.Conflict, cResp.StatusCode);

                using var rResp = await SendJsonAsync(
                    HttpMethod.Post,
                    $"/api/v1/diagnostic/orders/{unpaidCode}/receive",
                    new ReceiveDiagnosticPatientRequest(),
                    diag1Token);
                Assert.Equal(HttpStatusCode.Conflict, rResp.StatusCode);

                using var sResp = await SendJsonAsync(
                    HttpMethod.Post,
                    $"/api/v1/diagnostic/orders/{unpaidCode}/start",
                    new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS"),
                    diag1Token);
                Assert.Equal(HttpStatusCode.Conflict, sResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            var chargeA = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderA.OrderId);
            var chargeB = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderB.OrderId);

            // Pay ONLY Mandatory A (200k) -> confirm, receive, call, and START A (IN_PROGRESS at LabServicePointId)
            await IssueSingleQrAndSettleAsync(visitCode, chargeA.Id, 200_000m, patientToken, $"FT-E2E04-A-{fx.Suffix}");

            using (var cA = await SendJsonAsync(HttpMethod.Post, $"/api/v1/diagnostic/orders/{orderA.OrderCode}/confirm", new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true, ServicePointId: fx.LabServicePointId), diag1Token))
                Assert.Equal(HttpStatusCode.OK, cA.StatusCode);
            using (var rA = await SendJsonAsync(HttpMethod.Post, $"/api/v1/diagnostic/orders/{orderA.OrderCode}/receive", new ReceiveDiagnosticPatientRequest(), diag1Token))
                Assert.Equal(HttpStatusCode.OK, rA.StatusCode);
            using (var callA = await SendJsonAsync(HttpMethod.Post, $"/api/v1/diagnostic/queue/service-points/{fx.LabServicePointId}/call-next", new CallNextDiagnosticPatientRequest(), diag1Token))
                Assert.Equal(HttpStatusCode.OK, callA.StatusCode);
            using (var startA = await SendJsonAsync(HttpMethod.Post, $"/api/v1/diagnostic/orders/{orderA.OrderCode}/start", new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS", ExpectedVisitCode: visitCode, ExpectedPatientId: fx.PatientId), diag1Token))
                Assert.Equal(HttpStatusCode.OK, startA.StatusCode);

            // Pay ONLY Mandatory B (150k) -> confirm & receive B at ImagingServicePointId;
            // verify starting B while A is still IN_PROGRESS returns 409 PATIENT_BUSY_AT_ANOTHER_POINT!
            await IssueSingleQrAndSettleAsync(visitCode, chargeB.Id, 150_000m, patientToken, $"FT-E2E04-B-{fx.Suffix}");

            using (var cB = await SendJsonAsync(HttpMethod.Post, $"/api/v1/diagnostic/orders/{orderB.OrderCode}/confirm", new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true, ServicePointId: fx.ImagingServicePointId), diag1Token))
                Assert.Equal(HttpStatusCode.OK, cB.StatusCode);

            // Stage E2 (single-active): receiving B while A is an active serving ticket -> 409 ACTIVE_QUEUE
            // (one active serving queue per patient). This replaces the legacy PATIENT_BUSY_AT_ANOTHER_POINT.
            using (var rB = await SendJsonAsync(HttpMethod.Post, $"/api/v1/diagnostic/orders/{orderB.OrderCode}/receive", new ReceiveDiagnosticPatientRequest(), diag1Token))
            {
                Assert.Equal(HttpStatusCode.Conflict, rB.StatusCode);
                Assert.Contains("ACTIVE_QUEUE_ALREADY_EXISTS", await rB.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            // Finish UC42 for A (PERFORMED_AWAITING_RESULT, awaiting UC43) -> single-active released.
            using (var compA = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{orderA.OrderCode}/complete",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED", ExpectedVisitCode: visitCode, ExpectedPatientId: fx.PatientId),
                diag1Token))
            {
                Assert.Equal(HttpStatusCode.OK, compA.StatusCode);
            }

            // B now receives, is called, started and completed UC42 cleanly.
            using (var rBClean = await SendJsonAsync(HttpMethod.Post, $"/api/v1/diagnostic/orders/{orderB.OrderCode}/receive", new ReceiveDiagnosticPatientRequest(), diag1Token))
                Assert.Equal(HttpStatusCode.OK, rBClean.StatusCode);
            using (var callB = await SendJsonAsync(HttpMethod.Post, $"/api/v1/diagnostic/queue/service-points/{fx.ImagingServicePointId}/call-next", new CallNextDiagnosticPatientRequest(), diag1Token))
                Assert.Equal(HttpStatusCode.OK, callB.StatusCode);

            using (var startBClean = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{orderB.OrderCode}/start",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS", ExpectedVisitCode: visitCode, ExpectedPatientId: fx.PatientId),
                diag1Token))
            {
                Assert.Equal(HttpStatusCode.OK, startBClean.StatusCode);
            }

            using (var compB = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/diagnostic/orders/{orderB.OrderCode}/complete",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED", ExpectedVisitCode: visitCode, ExpectedPatientId: fx.PatientId),
                diag1Token))
            {
                Assert.Equal(HttpStatusCode.OK, compB.StatusCode);
            }

            // Decline Optional C -> DECLINED
            using (var decC = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/diagnostic-orders/{orderC.OrderId}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Không làm dịch vụ tùy chọn C"),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, decC.StatusCode);
            }

            // Finalize UC43 for A & B -> CompleteVisit
            await SubmitAndVerifyTwoPersonResultAsync(orderA.OrderCode, "WBC 8.1 G/L.", "Bình thường.", diag1Token, diag2Token);
            await FinalizeSinglePersonResultAsync(orderB.OrderCode, "Ổ bụng bình thường.", "Bình thường.", diag1Token);

            await CallReopenConfirmDxAndCompleteNoRxAsync(visitCode, fx.ExamServicePoint1Id, fx.Icd10Code, receptionToken, doctorToken);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // E2E-05: Three-Day Cross-Day Visit (Day D -> Day D+1 -> Day D+2)
    // =========================================================================

    [Fact]
    public async Task E2E05_ThreeDayCrossDayVisit_DayDPayAndDefer_DayDPlus1ResumeAndLeave_DayDPlus2AbsentResultReadyThenResumeReturnConsultation()
    {
        var dayD = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified);
        _clock.SetVietnamLocalTime(dayD);

        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            // ---------------- DAY D (2026-11-10) ----------------
            var patientTokenD = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var receptionTokenD = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var doctorTokenD = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);

            var visit = await CreatePaidExamVisitReadyForConsultationAsync(fx, "E2E05", patientTokenD, doctorTokenD);
            var visitCode = visit.VisitCode!;
            var originalCheckedInAt = visit.CheckedInAt;

            using (await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/clinical-examination",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Khám ngày D, cần nhịn đói làm CLS sáng mai."),
                doctorTokenD))
            {
            }

            ConsultationWorkspaceResponseDto orderedWs;
            using (var orderResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnostic-orders",
                new CreateDiagnosticOrderRequest(
                    TransitionToAwaitingResults: true,
                    Items:
                    [
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.BloodTestServiceId, Instruction: "Mandatory A (200k)", RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.UltrasoundServiceId, Instruction: "Mandatory B (150k)", RequiredForConclusion: true),
                    ]),
                doctorTokenD))
            {
                Assert.Equal(HttpStatusCode.OK, orderResp.StatusCode);
                orderedWs = (await orderResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
            }

            var orderA = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.BloodTestServiceId);
            var orderB = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.UltrasoundServiceId);

            Db.ChangeTracker.Clear();
            var chargeA = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderA.OrderId);
            var chargeB = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderB.OrderId);

            // Pay Mandatory A (200k) on Day D, then Defer to Day D+1 (PatientLeftHospital: true)
            await IssueSingleQrAndSettleAsync(visitCode, chargeA.Id, 200_000m, patientTokenD, $"FT-E2E05-DA-{fx.Suffix}");

            using (var deferDResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/defer",
                new DeferVisitOrOrdersRequestDto(
                    Reason: "Bệnh nhân đã ăn sáng, hẹn sáng mai D+1 nhịn đói lấy máu và siêu âm",
                    DeferredUntilDate: new DateOnly(2026, 11, 11),
                    DiagnosticOrderIds: [orderA.OrderId, orderB.OrderId],
                    PatientLeftHospital: true),
                receptionTokenD))
            {
                Assert.Equal(HttpStatusCode.OK, deferDResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            var visitAfterDeferD = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.Equal(VisitPresenceStates.Deferred, visitAfterDeferD.PresenceState);
            Assert.All(
                await Db.QueueEntries.AsNoTracking().Where(q => q.VisitId == visit.Id && q.QueueType == "DIAGNOSTIC").ToListAsync(),
                q => Assert.Equal("DEFERRED", q.Status));

            // ---------------- DAY D+1 (2026-11-11) ----------------
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 8, 30, 0, DateTimeKind.Unspecified));
            var patientTokenD1 = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var receptionTokenD1 = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var diag1TokenD1 = await CreateSessionJwtAsync(fx.DiagStaff1UserId, RoleNames.DiagnosticStaff);

            // Accidental Walk-in for same patient hits 409 VISIT_RESUME_REQUIRED
            using (var walkInBlockResp = await SendJsonAsync(
                HttpMethod.Post,
                "/api/v1/reception/visits/walk-in",
                new CreateWalkInVisitRequest(
                    PatientId: fx.PatientId.ToString(CultureInfo.InvariantCulture),
                    DepartmentId: fx.DepartmentId.ToString(CultureInfo.InvariantCulture),
                    ServiceId: fx.ExamServiceId.ToString(CultureInfo.InvariantCulture),
                    DoctorId: fx.Doctor1Id.ToString(CultureInfo.InvariantCulture),
                    ExaminationRoomId: fx.ExamRoom1Id.ToString(CultureInfo.InvariantCulture),
                    ReasonForVisit: "Tiếp đón nhầm lượt mới khi lượt hôm qua còn mở",
                    PaymentMethod: "CASH",
                    IsIdentityVerified: true),
                receptionTokenD1))
            {
                Assert.Equal(HttpStatusCode.Conflict, walkInBlockResp.StatusCode);
                Assert.Contains("VISIT_RESUME_REQUIRED", await walkInBlockResp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            // Reception GET .../resume-context & POST .../resume (DIAGNOSTIC for Order A)
            using (var ctxD1Resp = await SendJsonAsync(
                HttpMethod.Get,
                $"/api/v1/reception/visits/{visitCode}/resume-context",
                null,
                receptionTokenD1))
            {
                Assert.Equal(HttpStatusCode.OK, ctxD1Resp.StatusCode);
                var ctxD1 = (await ctxD1Resp.Content.ReadFromJsonAsync<VisitResumeContextResponseDto>(WebJson))!;
                Assert.False(ctxD1.IsPresentToday);
                Assert.Equal(originalCheckedInAt, ctxD1.CheckedInAt);
            }

            using (var resumeAResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderId: orderA.OrderId,
                    IdempotencyKey: $"res-e05-a-{fx.Suffix}"),
                receptionTokenD1))
            {
                Assert.Equal(HttpStatusCode.OK, resumeAResp.StatusCode);
                var resADto = (await resumeAResp.Content.ReadFromJsonAsync<ResumeCrossDayVisitResponseDto>(WebJson))!;
                Assert.Equal(visit.Id, resADto.VisitId);
                Assert.Equal(originalCheckedInAt, resADto.CheckedInAt);
                var qA = Assert.Single(resADto.ResumedQueues);
                Assert.Equal(new DateOnly(2026, 11, 11), qA.QueueDate);
                Assert.Equal("DIAGNOSTIC", qA.QueueType);
            }

            await ConfirmAndExecuteUc40ToUc42Async(orderA.OrderCode, fx.LabServicePointId, visitCode, fx.PatientId, diag1TokenD1);

            // Pay Mandatory B (150k) on D+1, resume DIAGNOSTIC for B, execute UC40..UC42, then patient leaves hospital
            await IssueSingleQrAndSettleAsync(visitCode, chargeB.Id, 150_000m, patientTokenD1, $"FT-E2E05-DB-{fx.Suffix}");

            using (var resumeBResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "DIAGNOSTIC",
                    DiagnosticOrderId: orderB.OrderId,
                    IdempotencyKey: $"res-e05-b-{fx.Suffix}"),
                receptionTokenD1))
            {
                Assert.Equal(HttpStatusCode.OK, resumeBResp.StatusCode);
            }

            await ConfirmAndExecuteUc40ToUc42Async(orderB.OrderCode, fx.ImagingServicePointId, visitCode, fx.PatientId, diag1TokenD1);

            using (var leaveD1Resp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/defer",
                new DeferVisitOrOrdersRequestDto(
                    Reason: "Đã lấy mẫu và siêu âm xong chiều D+1, bệnh nhân về nhà chờ kết quả",
                    PatientLeftHospital: true),
                receptionTokenD1))
            {
                Assert.Equal(HttpStatusCode.OK, leaveD1Resp.StatusCode);
            }

            // ---------------- DAY D+2 (2026-11-12) ----------------
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Unspecified));
            var receptionTokenD2 = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var doctorTokenD2 = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);
            var diag1TokenD2 = await CreateSessionJwtAsync(fx.DiagStaff1UserId, RoleNames.DiagnosticStaff);
            var diag2TokenD2 = await CreateSessionJwtAsync(fx.DiagStaff2UserId, RoleNames.DiagnosticStaff);

            // Finalize UC43 for A & B while patient is absent (!IsPresentToday)
            await SubmitAndVerifyTwoPersonResultAsync(
                orderA.OrderCode,
                "HbA1c = 7.6%, Glucose lúc đói = 8.9 mmol/L.",
                "Rối loạn đường huyết lúc đói cần tư vấn điều trị.",
                diag1TokenD2,
                diag2TokenD2);
            await FinalizeSinglePersonResultAsync(
                orderB.OrderCode,
                "Gan nhiễm mỡ độ 1, mật tụy bình thường.",
                "Gan nhiễm mỡ nhẹ.",
                diag1TokenD2);

            Db.ChangeTracker.Clear();
            var visitBeforeArriveD2 = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == visit.Id);
            Assert.NotNull(visitBeforeArriveD2.ReadyForReturnAt);

            var readyNotifs = await Db.Notifications.AsNoTracking()
                .Where(n => n.VisitId == visit.Id && n.NotificationType == Notification.TypeVisitReturnReady)
                .ToListAsync();
            var singleNotif = Assert.Single(readyNotifs);
            Assert.DoesNotContain("HbA1c", singleNotif.Body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("7.6", singleNotif.Body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Gan nhiễm mỡ", singleNotif.Body ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            // ZERO active callable RETURN_CONSULTATION queues while absent -> CallNext at exam room cannot call absent patient!
            var activeReturnBeforeResume = await Db.QueueEntries.AsNoTracking()
                .Where(q => q.VisitId == visit.Id && q.QueueType == "RETURN_CONSULTATION" && q.CompletedAt == null)
                .ToListAsync();
            Assert.Empty(activeReturnBeforeResume);

            using (var callAbsentResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/queue/service-points/{fx.ExamServicePoint1Id}/call-next",
                new CallNextPatientRequest(Notes: "Thử gọi khi bệnh nhân chưa đến bệnh viện ngày D+2"),
                receptionTokenD2))
            {
                if (callAbsentResp.StatusCode == HttpStatusCode.OK)
                {
                    var callAbsentDto = (await callAbsentResp.Content.ReadFromJsonAsync<CallNextPatientResponseDto>(WebJson))!;
                    Assert.Null(callAbsentDto.CalledEntry);
                }
                else
                {
                    Assert.Equal(HttpStatusCode.Conflict, callAbsentResp.StatusCode);
                }
            }

            // Patient arrives on D+2 -> Reception POST .../resume (Target: RETURN_CONSULTATION) -> 1 QueueDate == D+2 queue, 0 extra exam fee
            using (var resumeReturnResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/resume",
                new ResumeCrossDayVisitRequestDto(Target: "RETURN_CONSULTATION", IdempotencyKey: $"res-e05-rc-{fx.Suffix}", ConfirmPresent: true),
                receptionTokenD2))
            {
                Assert.Equal(HttpStatusCode.OK, resumeReturnResp.StatusCode);
                var resRcDto = (await resumeReturnResp.Content.ReadFromJsonAsync<ResumeCrossDayVisitResponseDto>(WebJson))!;
                Assert.Equal(visit.Id, resRcDto.VisitId);
                Assert.Equal("RETURN_CONSULTATION", resRcDto.VisitStatus);
                var rcQueue = Assert.Single(resRcDto.ResumedQueues);
                Assert.Equal(new DateOnly(2026, 11, 12), rcQueue.QueueDate);
                Assert.Equal("RETURN_CONSULTATION", rcQueue.QueueType);
                Assert.Equal("READY", rcQueue.Status);
            }

            await CallReopenConfirmDxAndCompleteNoRxAsync(visitCode, fx.ExamServicePoint1Id, fx.Icd10Code, receptionTokenD2, doctorTokenD2, coordinateReturn: false);

            Db.ChangeTracker.Clear();
            Assert.Equal(1, await Db.Visits.AsNoTracking().CountAsync(v => v.PatientId == fx.PatientId));
            var allVisitCharges = await Db.VisitCharges.AsNoTracking().Where(c => c.VisitId == visit.Id).ToListAsync();
            Assert.Equal(3, allVisitCharges.Count);
            Assert.Single(allVisitCharges, c => c.ChargeType == VisitChargeTypes.Examination);

            var allQueues = await Db.QueueEntries.AsNoTracking().Where(q => q.VisitId == visit.Id).ToListAsync();
            Assert.Contains(allQueues, q => q.QueueDate == new DateOnly(2026, 11, 10));
            Assert.Contains(allQueues, q => q.QueueDate == new DateOnly(2026, 11, 11));
            Assert.Contains(allQueues, q => q.QueueDate == new DateOnly(2026, 11, 12) && q.QueueType == "RETURN_CONSULTATION");
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // E2E-06: Voluntary Abandonment of Paid Optional CLS (SKIPPED_AFTER_PAID /
    // PAID_NOT_PERFORMED, Zero Refund Module), Sibling Active Charge Payable,
    // and Mandatory Refusal Requires Doctor Review
    // =========================================================================

    [Fact]
    public async Task E2E06_VoluntaryAbandonmentOfPaidOptionalCls_NoRefundModule_SiblingActiveChargePayable_AndMandatoryRefusalRequiresDoctorReview()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var patientToken = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var receptionToken = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var doctorToken = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);
            var diag1Token = await CreateSessionJwtAsync(fx.DiagStaff1UserId, RoleNames.DiagnosticStaff);
            var diag2Token = await CreateSessionJwtAsync(fx.DiagStaff2UserId, RoleNames.DiagnosticStaff);

            var visit = await CreatePaidExamVisitReadyForConsultationAsync(fx, "E2E06", patientToken, doctorToken);
            var visitCode = visit.VisitCode!;

            using (await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/clinical-examination",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Đau vùng thượng vị, chỉ định XN bắt buộc + SA tùy chọn."),
                doctorToken))
            {
            }

            ConsultationWorkspaceResponseDto orderedWs;
            using (var orderResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnostic-orders",
                new CreateDiagnosticOrderRequest(
                    TransitionToAwaitingResults: true,
                    Items:
                    [
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.BloodTestServiceId, Instruction: "Mandatory A (200k)", RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.OptionalUltrasoundServiceId, Instruction: "Optional B (100k)", RequiredForConclusion: false),
                    ]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, orderResp.StatusCode);
                orderedWs = (await orderResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
            }

            var orderA = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.BloodTestServiceId);
            var orderB = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.OptionalUltrasoundServiceId);

            Db.ChangeTracker.Clear();
            var chargeA = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderA.OrderId);
            var chargeB = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderB.OrderId);

            // Patient accepts & pays Optional B (100k)
            var paidOptQr = await IssueSingleQrAndSettleAsync(visitCode, chargeB.Id, 100_000m, patientToken, $"FT-E2E06-OPTB-{fx.Suffix}");

            // Patient self-decline on PAID Optional B -> 409
            using (var patSelfDeclinePaid = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/diagnostic-orders/{orderB.OrderCode}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Tự bấm hủy sau khi đã thanh toán", ConfirmAbandonPaid: true),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, patSelfDeclinePaid.StatusCode);
                Assert.Contains("PAID_ORDER_STAFF_CONFIRMATION_REQUIRED", await patSelfDeclinePaid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            // Patient & Reception decline on Mandatory A -> 409 MANDATORY_ORDER_REVIEW_REQUIRED
            using (var patDeclineMand = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/diagnostic-orders/{orderA.OrderCode}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Xin tự bỏ xét nghiệm bắt buộc"),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, patDeclineMand.StatusCode);
                Assert.Contains("MANDATORY_ORDER_REVIEW_REQUIRED", await patDeclineMand.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            using (var recDeclineMand = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/diagnostic-orders/{orderA.OrderCode}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Quầy thử hủy xét nghiệm bắt buộc"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, recDeclineMand.StatusCode);
                Assert.Contains("MANDATORY_ORDER_REVIEW_REQUIRED", await recDeclineMand.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            // Patient requests doctor review on Mandatory A -> SelectionStatus = REVIEW_REQUIRED, CompleteVisit blocked
            using (var reqReviewResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/diagnostic-orders/selection",
                new UpdateDiagnosticOrderSelectionRequestDto(
                    ReviewRequestedOrderIds: [orderA.OrderId],
                    Reason: "Xin bác sĩ tư vấn lại trước khi lấy máu"),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, reqReviewResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            Assert.Equal(
                DiagnosticSelectionStatuses.ReviewRequired,
                (await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == orderA.OrderId)).SelectionStatus);

            using (var blockedComplete = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/complete",
                new CompleteVisitRequest(ConfirmNoPrescription: true),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, blockedComplete.StatusCode);
            }

            // Reception confirms voluntary abandonment of PAID Optional B (ConfirmAbandonPaid: true) ->
            // SKIPPED_AFTER_PAID + CANCELLED + PAID_NOT_PERFORMED, while Payment & PaymentAllocation remain PAID!
            using (var recSkipPaidResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/diagnostic-orders/{orderB.OrderCode}/decline",
                new DeclineDiagnosticOrderRequestDto(
                    Reason: "Bệnh nhân tự nguyện bỏ siêu âm tùy chọn đã đóng tiền vì có việc gấp",
                    ConfirmAbandonPaid: true),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.OK, recSkipPaidResp.StatusCode);
                var skipDto = (await recSkipPaidResp.Content.ReadFromJsonAsync<DeclineDiagnosticOrderResponseDto>(WebJson))!;
                Assert.Equal(DiagnosticSelectionStatuses.SkippedAfterPaid, skipDto.SelectionStatus);
                Assert.Equal("CANCELLED", skipDto.OrderStatus);
                Assert.Equal(VisitChargeStatuses.PaidNotPerformed, skipDto.ChargeStatus);
            }

            Db.ChangeTracker.Clear();
            var optPaymentAfterSkip = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == paidOptQr.PaymentId);
            var optAllocAfterSkip = await Db.PaymentAllocations.AsNoTracking().SingleAsync(pa => pa.PaymentId == paidOptQr.PaymentId);
            Assert.Equal(PaymentStatuses.Paid, optPaymentAfterSkip.Status);
            Assert.Equal(PaymentAllocationStatuses.Paid, optAllocAfterSkip.Status);

            // Doctor counsels patient and restores Mandatory A to ACCEPTED -> Mandatory A (200k ACTIVE) generates QR & settles -> executes -> CompleteVisit!
            using (var docReAcceptResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/visits/{visitCode}/diagnostic-orders/selection",
                new UpdateDiagnosticOrderSelectionRequestDto(AcceptedOrderIds: [orderA.OrderId]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, docReAcceptResp.StatusCode);
            }

            await IssueSingleQrAndSettleAsync(visitCode, chargeA.Id, 200_000m, patientToken, $"FT-E2E06-MANDA-{fx.Suffix}");
            await ConfirmAndExecuteUc40ToUc42Async(orderA.OrderCode, fx.LabServicePointId, visitCode, fx.PatientId, diag1Token);
            await SubmitAndVerifyTwoPersonResultAsync(orderA.OrderCode, "WBC 7.9 G/L.", "Bình thường.", diag1Token, diag2Token);

            await CallReopenConfirmDxAndCompleteNoRxAsync(visitCode, fx.ExamServicePoint1Id, fx.Icd10Code, receptionToken, doctorToken);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // E2E-07: Cross-Day Doctor Reassignment When Original Doctor Off-Duty +
    // Post-Completion Follow-Up Creates Brand-New Visit
    // =========================================================================

    [Fact]
    public async Task E2E07_CrossDayDoctorReassignmentWhenOriginalDoctorOffDuty_AndPostCompletionFollowUpCreatesNewVisit()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var patientTokenD = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var doctor1TokenD = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);
            var diag1TokenD = await CreateSessionJwtAsync(fx.DiagStaff1UserId, RoleNames.DiagnosticStaff);

            var oldVisit = await CreatePaidExamVisitReadyForConsultationAsync(fx, "E2E07", patientTokenD, doctor1TokenD);
            var oldVisitCode = oldVisit.VisitCode!;

            using (await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{oldVisitCode}/clinical-examination",
                new RecordClinicalExaminationRequest(ClinicalFindings: "BS 1 khám ban đầu ngày D."),
                doctor1TokenD))
            {
            }

            string orderCode;
            long orderId;
            using (var orderResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{oldVisitCode}/diagnostic-orders",
                new CreateDiagnosticOrderRequest(
                    ServiceId: fx.UltrasoundServiceId,
                    Instruction: "Siêu âm bắt buộc",
                    RequiredForConclusion: true,
                    TransitionToAwaitingResults: true),
                doctor1TokenD))
            {
                Assert.Equal(HttpStatusCode.OK, orderResp.StatusCode);
                var ws = (await orderResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
                orderId = ws.CreatedDiagnosticOrders![0].OrderId;
                orderCode = ws.CreatedDiagnosticOrders[0].OrderCode;
            }

            Db.ChangeTracker.Clear();
            var clsCharge = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderId);
            await IssueSingleQrAndSettleAsync(oldVisitCode, clsCharge.Id, 150_000m, patientTokenD, $"FT-E2E07-CLS-{fx.Suffix}");
            await ConfirmAndExecuteUc40ToUc42Async(orderCode, fx.ImagingServicePointId, oldVisitCode, fx.PatientId, diag1TokenD);

            // Day D+1: Result finalized while patient absent; Doctor 1 is deactivated/off-duty
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Unspecified));
            var receptionTokenD1 = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var doctor1TokenD1 = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);
            var doctor2TokenD1 = await CreateSessionJwtAsync(fx.Doctor2UserId, RoleNames.Doctor);
            var diag1TokenD1 = await CreateSessionJwtAsync(fx.DiagStaff1UserId, RoleNames.DiagnosticStaff);

            await FinalizeSinglePersonResultAsync(orderCode, "Ổ bụng bình thường.", "Bình thường.", diag1TokenD1);

            await Db.Doctors.Where(d => d.Id == fx.Doctor1Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false));

            // Resume with ReassignDoctorId = Doctor2Id -> reassigns Visit to Doctor 2 with 0 extra exam fee
            using (var resumeResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{oldVisitCode}/resume",
                new ResumeCrossDayVisitRequestDto(
                    Target: "RETURN_CONSULTATION",
                    ConfirmPresent: true,
                    ReassignDoctorId: fx.Doctor2Id,
                    Notes: "BS 1 nghỉ phép ngày D+1, chuyển BS 2 khám kết luận"),
                receptionTokenD1))
            {
                Assert.Equal(HttpStatusCode.OK, resumeResp.StatusCode);
                var resDto = (await resumeResp.Content.ReadFromJsonAsync<ResumeCrossDayVisitResponseDto>(WebJson))!;
                Assert.Equal(fx.Doctor2Id, resDto.AssignedDoctorId);
                Assert.Equal(fx.ExamRoom2Id, resDto.ExaminationRoomId);
            }

            Db.ChangeTracker.Clear();
            Assert.Equal(
                1,
                await Db.VisitCharges.AsNoTracking().CountAsync(c => c.VisitId == oldVisit.Id && c.ChargeType == VisitChargeTypes.Examination));

            // Re-enable Doctor 1 to verify two-layer ownership check returns 403 Forbidden on Doctor 1 workspace write
            await Db.Doctors.Where(d => d.Id == fx.Doctor1Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, true));

            using (var doc1ForbiddenWrite = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{oldVisitCode}/diagnosis",
                new RecordDiagnosisRequest(PrimaryIcd10Code: fx.Icd10Code, ClinicalConclusion: "BS 1 ghi trái phép", IsConfirmed: true),
                doctor1TokenD1))
            {
                Assert.Equal(HttpStatusCode.Forbidden, doc1ForbiddenWrite.StatusCode);
            }

            // Doctor 2 calls, opens, confirms diagnosis & completes Visit (FINALIZED EMR)
            await CallReopenConfirmDxAndCompleteNoRxAsync(oldVisitCode, fx.ExamServicePoint2Id, fx.Icd10Code, receptionTokenD1, doctor2TokenD1, coordinateReturn: false);

            Db.ChangeTracker.Clear();
            var oldEmr = await Db.MedicalRecords.AsNoTracking().SingleAsync(mr => mr.VisitId == oldVisit.Id);
            Assert.Equal("FINALIZED", oldEmr.Status);

            // Day D+7 (2026-11-17): Patient books follow-up appointment -> Reception checks in -> brand-new Visit + new EXAMINATION charge!
            _clock.SetVietnamLocalTime(new DateTime(2026, 11, 17, 8, 30, 0, DateTimeKind.Unspecified));
            var patientTokenD7 = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var receptionTokenD7 = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);

            var followUpSlot = new ExaminationSlot
            {
                ServiceId = fx.ExamServiceId,
                DoctorId = fx.Doctor1Id,
                RoomId = fx.ExamRoom1Id,
                StartsAt = new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Unspecified),
                EndsAt = new DateTime(2026, 11, 17, 9, 30, 0, DateTimeKind.Unspecified),
                Capacity = 3,
                Status = "OPEN",
            };
            Db.ExaminationSlots.Add(followUpSlot);
            await Db.SaveChangesAsync();
            fx.CreatedSlotIds.Add(followUpSlot.Id);

            string followUpApptCode;
            long followUpApptId;
            using (var bookResp = await SendJsonAsync(
                HttpMethod.Post,
                "/api/v1/appointments",
                new CreateAppointmentRequest
                {
                    Mode = "DOCTOR",
                    PatientId = fx.PatientId.ToString(CultureInfo.InvariantCulture),
                    DoctorId = fx.Doctor1Code,
                    ServiceId = fx.ExamServiceId.ToString(CultureInfo.InvariantCulture),
                    DateIso = "2026-11-17",
                    SlotOrShiftId = followUpSlot.Id.ToString(CultureInfo.InvariantCulture),
                    SymptomNote = "Tái khám theo hẹn sau 7 ngày",
                },
                patientTokenD7))
            {
                Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);
                var booked = (await bookResp.Content.ReadFromJsonAsync<CreateAppointmentResponse>(WebJson))!;
                followUpApptId = long.Parse(booked.AppointmentId, CultureInfo.InvariantCulture);
                followUpApptCode = booked.AppointmentCode;
                fx.CreatedAppointmentIds.Add(followUpApptId);
            }

            using (var checkInFollowUpResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/appointments/{followUpApptCode}/check-in",
                new CheckInScheduledPatientRequest(ReasonForVisit: "Tái khám theo hẹn", IsIdentityVerified: true),
                receptionTokenD7))
            {
                Assert.Equal(HttpStatusCode.OK, checkInFollowUpResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            var newVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.AppointmentId == followUpApptId);
            Assert.NotEqual(oldVisit.Id, newVisit.Id);
            Assert.Single(await Db.VisitCharges.AsNoTracking()
                .Where(c => c.VisitId == newVisit.Id && c.ChargeType == VisitChargeTypes.Examination)
                .ToListAsync());

            var reloadedOldVisit = await Db.Visits.AsNoTracking().SingleAsync(v => v.Id == oldVisit.Id);
            var reloadedOldEmr = await Db.MedicalRecords.AsNoTracking().SingleAsync(mr => mr.VisitId == oldVisit.Id);
            Assert.Equal("COMPLETED", reloadedOldVisit.Status);
            Assert.Equal("FINALIZED", reloadedOldEmr.Status);
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // E2E-08: Late Webhook on Cancelled/Expired QR + Cash vs Webhook Reconciliation
    // Isolation and Replay Idempotency
    // =========================================================================

    [Fact]
    public async Task E2E08_LateWebhookOnCancelledOrExpiredQr_AndCashVsWebhookReconciliationIsolationAndIdempotency()
    {
        _clock.SetVietnamLocalTime(new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Unspecified));
        var fx = await CreateIsolatedFixtureAsync(batchQuantities: [10m]);
        try
        {
            var patientToken = await CreateSessionJwtAsync(fx.PatientUserId, RoleNames.Patient);
            var receptionToken = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
            var doctorToken = await CreateSessionJwtAsync(fx.Doctor1UserId, RoleNames.Doctor);

            var visit = await CreatePaidExamVisitReadyForConsultationAsync(fx, "E2E08", patientToken, doctorToken);
            var visitCode = visit.VisitCode!;

            using (await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/clinical-examination",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Kiểm tra đối soát thanh toán E2E-08."),
                doctorToken))
            {
            }

            ConsultationWorkspaceResponseDto orderedWs;
            using (var orderResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/doctor/consultation/{visitCode}/diagnostic-orders",
                new CreateDiagnosticOrderRequest(
                    TransitionToAwaitingResults: true,
                    Items:
                    [
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.BloodTestServiceId, Instruction: "Mandatory A (200k)", RequiredForConclusion: true),
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.OptionalUltrasoundServiceId, Instruction: "Optional B (100k)", RequiredForConclusion: false),
                        new CreateDiagnosticOrderItemRequest(ServiceId: fx.UltrasoundServiceId, Instruction: "Mandatory C (150k)", RequiredForConclusion: true),
                    ]),
                doctorToken))
            {
                Assert.Equal(HttpStatusCode.OK, orderResp.StatusCode);
                orderedWs = (await orderResp.Content.ReadFromJsonAsync<ConsultationWorkspaceResponseDto>(WebJson))!;
            }

            var orderA = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.BloodTestServiceId);
            var orderB = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.OptionalUltrasoundServiceId);
            var orderC = Assert.Single(orderedWs.CreatedDiagnosticOrders!, o => o.ServiceId == fx.UltrasoundServiceId);

            Db.ChangeTracker.Clear();
            var chargeA = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderA.OrderId);
            var chargeB = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderB.OrderId);
            var chargeC = await Db.VisitCharges.AsNoTracking().SingleAsync(c => c.DiagnosticOrderId == orderC.OrderId);

            // 1. Issue grouped QR PAY-OLD (300k for Mandatory A 200k + Optional B 100k)
            GenerateQrPaymentResponseDto payOld;
            using (var oldQrResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [chargeA.Id, chargeB.Id]),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.Created, oldQrResp.StatusCode);
                payOld = (await oldQrResp.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
                Assert.Equal(300_000m, payOld.Amount);
            }

            // 2. Decline Optional B -> PAY-OLD + both allocations become CANCELLED while Amount == 300k stays immutable
            using (var decBResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/diagnostic-orders/{orderB.OrderId}/decline",
                new DeclineDiagnosticOrderRequestDto(Reason: "Không làm Optional B"),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.OK, decBResp.StatusCode);
            }

            Db.ChangeTracker.Clear();
            var payOldAfterDecline = await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == payOld.PaymentId);
            Assert.Equal(PaymentStatuses.Cancelled, payOldAfterDecline.Status);
            Assert.Equal(300_000m, payOldAfterDecline.Amount);
            Assert.All(
                await Db.PaymentAllocations.AsNoTracking().Where(pa => pa.PaymentId == payOld.PaymentId).ToListAsync(),
                pa => Assert.Equal(PaymentAllocationStatuses.Cancelled, pa.Status));

            // 3. Issue new QR PAY-NEW (200k for Mandatory A)
            GenerateQrPaymentResponseDto payNew;
            using (var newQrResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [chargeA.Id]),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.Created, newQrResp.StatusCode);
                payNew = (await newQrResp.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
                Assert.Equal(200_000m, payNew.Amount);
            }

            // 4. Late SePay webhook on PAY-OLD (300k) -> records reconciliation without unlocking declined B or blocking PAY-NEW!
            var lateOldOutcome = await PostSePayWebhookAsync(payOld.PaymentCode, 300_000m, $"FT-E2E08-OLD-{fx.Suffix}");
            Assert.Equal(SePayWebhookService.CancelledServiceRefundRequiredOutcome, lateOldOutcome);

            Db.ChangeTracker.Clear();
            Assert.Equal(SePayWebhookService.MismatchPaymentStatus, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == payOld.PaymentId)).Status);
            Assert.Equal("CANCELLED", (await Db.DiagnosticOrders.AsNoTracking().SingleAsync(o => o.Id == orderB.OrderId)).Status);
            Assert.Equal(PaymentStatuses.Pending, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == payNew.PaymentId)).Status);

            await using (var gateScope = _app.Services.CreateAsyncScope())
            {
                var reader = gateScope.ServiceProvider.GetRequiredService<IVisitChargeSettlementReader>();
                Assert.False((await reader.CheckDiagnosticOrderEligibilityAsync(orderB.OrderId)).IsUnlocked);
            }

            // 5. Settle PAY-NEW (200k PAID) + replay identical SePay webhook (0 double credit)
            var payNewWhId = Interlocked.Increment(ref _webhookSeq);
            var settleNewOutcome = await PostSePayWebhookWithIdAsync(payNewWhId, payNew.PaymentCode, 200_000m, $"FT-E2E08-NEW-{fx.Suffix}");
            Assert.Equal(PaymentGatewayOutcomes.Paid, settleNewOutcome);

            // Replay identical webhook externalId
            await SendRawSePayWebhookAsync(payNewWhId, payNew.PaymentCode, 200_000m, $"FT-E2E08-NEW-{fx.Suffix}");
            // Replay different externalId on already-PAID PAY-NEW -> DUPLICATE_RECONCILE while preserving PAID intact
            var secondOnPaidOutcome = await PostSePayWebhookAsync(payNew.PaymentCode, 200_000m, $"FT-E2E08-NEW-DUP-{fx.Suffix}");
            Assert.Equal(SePayWebhookService.DuplicateChargeReconcileOutcome, secondOnPaidOutcome);
            Assert.Equal(PaymentStatuses.Paid, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == payNew.PaymentId)).Status);

            Db.ChangeTracker.Clear();
            Assert.Equal(
                1,
                await Db.PaymentAllocations.AsNoTracking()
                    .CountAsync(pa => pa.VisitChargeId == chargeA.Id && pa.Status == PaymentAllocationStatuses.Paid));

            // 6. Also verify PENDING QR on Mandatory C (150k) + Reception CASH settlement + late SePay webhook -> DUPLICATE_RECONCILE while CASH PAID remains intact
            GenerateQrPaymentResponseDto qrC;
            using (var qrCResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/patients/visits/{visitCode}/payments/qr",
                new GenerateQrPaymentRequestDto(VisitChargeIds: [chargeC.Id]),
                patientToken))
            {
                Assert.Equal(HttpStatusCode.Created, qrCResp.StatusCode);
                qrC = (await qrCResp.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
                Assert.Equal(150_000m, qrC.Amount);
            }

            CollectCashPaymentResponseDto cashC;
            using (var cashCResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/payments/cash",
                new CollectCashPaymentRequestDto(VisitChargeIds: [chargeC.Id], IdempotencyKey: $"cash-e08-c-{fx.Suffix}"),
                receptionToken))
            {
                Assert.Equal(HttpStatusCode.Created, cashCResp.StatusCode);
                cashC = (await cashCResp.Content.ReadFromJsonAsync<CollectCashPaymentResponseDto>(WebJson))!;
            }

            var dupReconcileOutcome = await PostSePayWebhookAsync(qrC.PaymentCode, 150_000m, $"FT-E2E08-QRC-{fx.Suffix}");
            Assert.Equal(SePayWebhookService.DuplicateChargeReconcileOutcome, dupReconcileOutcome);

            Db.ChangeTracker.Clear();
            Assert.Equal(PaymentStatuses.Paid, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == cashC.PaymentId)).Status);
            Assert.Equal(SePayWebhookService.MismatchPaymentStatus, (await Db.Payments.AsNoTracking().SingleAsync(p => p.Id == qrC.PaymentId)).Status);
            Assert.Equal(
                1,
                await Db.PaymentAllocations.AsNoTracking()
                    .CountAsync(pa => pa.VisitChargeId == chargeC.Id && pa.Status == PaymentAllocationStatuses.Paid));
        }
        finally
        {
            await CleanupIsolatedFixtureAsync(fx);
        }
    }

    // =========================================================================
    // Shared Step Helpers & Self-Cleaning Fixture
    // =========================================================================

    private async Task ConfirmAndExecuteUc40ToUc42Async(
        string orderCode,
        long servicePointId,
        string visitCode,
        long patientId,
        string diagStaffToken)
    {
        using (var confirmResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/diagnostic/orders/{orderCode}/confirm",
            new ConfirmDiagnosticOrderRequest(IsEligibilityVerified: true, ServicePointId: servicePointId),
            diagStaffToken))
        {
            Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);
        }

        await ExecuteUc40ToUc42Async(orderCode, servicePointId, visitCode, patientId, diagStaffToken);
    }

    private async Task ExecuteUc40ToUc42Async(
        string orderCode,
        long servicePointId,
        string expectedVisitCode,
        long expectedPatientId,
        string diagStaffToken)
    {
        using (var receiveResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/diagnostic/orders/{orderCode}/receive",
            new ReceiveDiagnosticPatientRequest(),
            diagStaffToken))
        {
            Assert.Equal(HttpStatusCode.OK, receiveResp.StatusCode);
        }

        using (var callResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/diagnostic/queue/service-points/{servicePointId}/call-next",
            new CallNextDiagnosticPatientRequest(),
            diagStaffToken))
        {
            Assert.Equal(HttpStatusCode.OK, callResp.StatusCode);
        }

        using (var startResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/diagnostic/orders/{orderCode}/start",
            new UpdateDiagnosticServiceStatusRequest(
                TargetStatus: "IN_PROGRESS",
                ExpectedVisitCode: expectedVisitCode,
                ExpectedPatientId: expectedPatientId),
            diagStaffToken))
        {
            Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
        }

        using (var completeResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/diagnostic/orders/{orderCode}/complete",
            new UpdateDiagnosticServiceStatusRequest(
                TargetStatus: "COMPLETED",
                ExpectedVisitCode: expectedVisitCode,
                ExpectedPatientId: expectedPatientId),
            diagStaffToken))
        {
            Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);
        }
    }

    private async Task SubmitAndVerifyTwoPersonResultAsync(
        string orderCode,
        string resultText,
        string conclusion,
        string enterStaffToken,
        string verifyStaffToken)
    {
        using (var pendingResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/diagnostic/orders/{orderCode}/result",
            new EnterDiagnosticResultRequest(
                ResultText: resultText,
                Conclusion: conclusion,
                Status: "PENDING_VERIFY",
                IsFinalized: false),
            enterStaffToken))
        {
            Assert.Equal(HttpStatusCode.OK, pendingResp.StatusCode);
        }

        using (var verifyResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/diagnostic/orders/{orderCode}/result/verify",
            new VerifyDiagnosticResultRequest(
                Conclusion: conclusion,
                VerificationNote: "Đã thẩm định độc lập hai người."),
            verifyStaffToken))
        {
            Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
        }
    }

    private async Task FinalizeSinglePersonResultAsync(
        string orderCode,
        string resultText,
        string conclusion,
        string diagStaffToken)
    {
        using var resp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/diagnostic/orders/{orderCode}/result",
            new EnterDiagnosticResultRequest(
                ResultText: resultText,
                Conclusion: conclusion,
                Status: "FINALIZED",
                IsFinalized: true),
            diagStaffToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private async Task CallReopenConfirmDxAndCompleteNoRxAsync(
        string visitCode,
        long examServicePointId,
        string icd10Code,
        string receptionToken,
        string doctorToken,
        bool coordinateReturn = true)
    {
        // Stage E2 (QE-10): UC43 finalize holds the visit in AWAITING_RESULTS; Reception must coordinate the
        // RETURN_CONSULTATION ticket before the doctor can be called back in and reopen the workspace.
        // Cross-day/reassignment flows that already created a RETURN_CONSULTATION ticket pass coordinateReturn: false.
        if (coordinateReturn)
        {
            using var enrollResp = await SendJsonAsync(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{visitCode}/return-enrollment",
                new CreateReturnEnrollmentRequestDto(
                    Purpose: QueueCoordinationPurposes.ResultConclusion,
                    IdempotencyKey: $"e2e-return-{visitCode}"),
                receptionToken);
            Assert.True(enrollResp.StatusCode == HttpStatusCode.OK, await enrollResp.Content.ReadAsStringAsync());
        }

        using (var callResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/queue/service-points/sp-{examServicePointId}/call-next",
            new CallNextPatientRequest(Notes: "Gọi khám kết luận"),
            receptionToken))
        {
            Assert.Equal(HttpStatusCode.OK, callResp.StatusCode);
        }

        using (var openResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/doctor/consultation/{visitCode}/open",
            new { },
            doctorToken))
        {
            Assert.Equal(HttpStatusCode.OK, openResp.StatusCode);
        }

        using (var dxResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/doctor/consultation/{visitCode}/diagnosis",
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: icd10Code,
                ClinicalConclusion: "Tình trạng ổn định, hoàn tất lượt khám ngoại trú.",
                IsConfirmed: true),
            doctorToken))
        {
            Assert.Equal(HttpStatusCode.OK, dxResp.StatusCode);
        }

        using (var completeResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/doctor/consultation/{visitCode}/complete",
            new CompleteVisitRequest(
                ConfirmNoPrescription: true,
                DischargeInstructions: "Theo dõi tại nhà và tái khám khi cần."),
            doctorToken))
        {
            Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);
        }
    }

    private async Task<GenerateQrPaymentResponseDto> IssueSingleQrAndSettleAsync(
        string visitCode,
        long visitChargeId,
        decimal expectedAmount,
        string patientToken,
        string bankReference)
    {
        GenerateQrPaymentResponseDto qrDto;
        using (var qrResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/patients/visits/{visitCode}/payments/qr",
            new GenerateQrPaymentRequestDto(VisitChargeIds: [visitChargeId]),
            patientToken))
        {
            Assert.True(
                qrResp.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                $"Expected 201/200 from QR endpoint, got {(int)qrResp.StatusCode}: {await qrResp.Content.ReadAsStringAsync()}");
            qrDto = (await qrResp.Content.ReadFromJsonAsync<GenerateQrPaymentResponseDto>(WebJson))!;
            Assert.Equal(expectedAmount, qrDto.Amount);
        }

        var outcome = await PostSePayWebhookAsync(qrDto.PaymentCode, expectedAmount, bankReference);
        Assert.Equal(PaymentGatewayOutcomes.Paid, outcome);
        return qrDto;
    }

    private Task<string> PostSePayWebhookAsync(string paymentCode, decimal amount, string referenceCode)
        => PostSePayWebhookWithIdAsync(Interlocked.Increment(ref _webhookSeq), paymentCode, amount, referenceCode);

    private async Task<string> PostSePayWebhookWithIdAsync(long extId, string paymentCode, decimal amount, string referenceCode)
    {
        await SendRawSePayWebhookAsync(extId, paymentCode, amount, referenceCode);

        Db.ChangeTracker.Clear();
        var extIdStr = extId.ToString(CultureInfo.InvariantCulture);
        var persistedEvent = await Db.PaymentGatewayEvents.AsNoTracking()
            .SingleAsync(e => e.Provider == SePayWebhookService.Provider && e.ExternalId == extIdStr);
        return persistedEvent.Outcome!;
    }

    private async Task SendRawSePayWebhookAsync(long extId, string paymentCode, decimal amount, string referenceCode)
    {
        var payload = new SePayWebhookRequest
        {
            Id = extId,
            Gateway = "MBBank",
            TransactionDate = HospitalClock.GetNow(_clock).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            AccountNumber = "0123456789",
            Code = paymentCode,
            Content = paymentCode,
            TransferType = "in",
            TransferAmount = amount,
            ReferenceCode = referenceCode,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Apikey {SePayApiKey}");

        using var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
    }

    private async Task<Visit> CreateUnpaidCheckedInVisitAsync(IsolatedPhase07Fixture fx, string tag)
    {
        Db.ChangeTracker.Clear();
        var nowLocal = HospitalClock.GetNow(_clock);
        var today = DateOnly.FromDateTime(nowLocal);

        var visit = new Visit
        {
            VisitCode = $"VIS-P07-{tag}-{fx.Suffix}",
            PatientId = fx.PatientId,
            AssignedDoctorId = fx.Doctor1Id,
            ExaminationRoomId = fx.ExamRoom1Id,
            CheckedInByStaffId = fx.ReceptionStaffId,
            Status = "CHECKED_IN",
            CheckedInAt = nowLocal.AddMinutes(-15),
            LastPresentDate = today,
            PresenceState = VisitPresenceStates.Present,
            ReasonForVisit = $"Khám nội tổng quát {tag}",
        };
        Db.Visits.Add(visit);
        await Db.SaveChangesAsync();

        var nextQueueNumber = (await Db.QueueEntries
            .Where(q => q.ServicePointId == fx.ExamServicePoint1Id && q.QueueDate == today)
            .MaxAsync(q => (int?)q.QueueNumber) ?? 0) + 1;

        Db.QueueEntries.Add(new QueueEntry
        {
            VisitId = visit.Id,
            ServicePointId = fx.ExamServicePoint1Id,
            CurrentServicePointId = fx.ExamServicePoint1Id,
            QueueDate = today,
            QueueNumber = nextQueueNumber,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            PresenceStatus = "PRESENT",
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            ArrivedAt = nowLocal.AddMinutes(-14),
            ReadyAt = nowLocal.AddMinutes(-14),
            StateVersion = 1,
        });

        Db.VisitCharges.Add(VisitCharge.CreateExamination(visit.Id, fx.ExamServiceId, 150_000m, nowLocal.AddMinutes(-14)));
        await Db.SaveChangesAsync();

        return visit;
    }

    private async Task<Visit> CreatePaidExamVisitReadyForConsultationAsync(
        IsolatedPhase07Fixture fx,
        string tag,
        string patientToken,
        string doctorToken)
    {
        var visit = await CreateUnpaidCheckedInVisitAsync(fx, tag);
        var examCharge = await Db.VisitCharges.AsNoTracking()
            .SingleAsync(c => c.VisitId == visit.Id && c.ChargeType == VisitChargeTypes.Examination);

        await IssueSingleQrAndSettleAsync(visit.VisitCode!, examCharge.Id, 150_000m, patientToken, $"FT-EX-{tag}-{fx.Suffix}");

        var receptionToken = await CreateSessionJwtAsync(fx.ReceptionUserId, RoleNames.Reception);
        using (var callResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/queue/service-points/sp-{fx.ExamServicePoint1Id}/call-next",
            new CallNextPatientRequest(),
            receptionToken))
        {
            Assert.Equal(HttpStatusCode.OK, callResp.StatusCode);
        }

        using (var openResp = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/doctor/consultation/{visit.VisitCode}/open",
            new { },
            doctorToken))
        {
            Assert.Equal(HttpStatusCode.OK, openResp.StatusCode);
        }

        return visit;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        string bearerToken)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (body is not null && method != HttpMethod.Get)
        {
            req.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(req);
    }

    private HubConnection CreateHubConnection(string accessToken)
    {
        var hubUri = new Uri(_app.Server.BaseAddress, HubPath);
        return new HubConnectionBuilder()
            .WithUrl(
                hubUri,
                options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                    options.Headers["Origin"] = "http://localhost:5173";
                    options.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
                    options.Transports = HttpTransportType.WebSockets;
                    options.WebSocketFactory = async (context, ct) =>
                    {
                        var wsClient = _app.Server.CreateWebSocketClient();
                        wsClient.ConfigureRequest = req => req.Headers.Origin = "http://localhost:5173";
                        var targetUri = context.Uri;
                        if (!targetUri.Query.Contains("access_token=", StringComparison.OrdinalIgnoreCase))
                        {
                            var sep = string.IsNullOrEmpty(targetUri.Query) ? "?" : "&";
                            targetUri = new Uri($"{targetUri}{sep}access_token={Uri.EscapeDataString(accessToken)}");
                        }

                        return await wsClient.ConnectAsync(targetUri, ct);
                    };
                })
            .Build();
    }

    private async Task<string> CreateSessionJwtAsync(long userId, string roleCode)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();
        var sessionSvc = scope.ServiceProvider.GetRequiredService<IAuthSessionService>();
        var jwtSvc = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var session = await sessionSvc.GenerateAsync(userId, "127.0.0.1");
        return jwtSvc.GenerateAccessToken(user, [roleCode], session.Entity.Id).AccessToken;
    }

    private sealed record IsolatedPhase07Fixture(
        string Suffix,
        long HospitalId,
        long DepartmentId,
        long SpecialtyId,
        long ExamRoom1Id,
        long ExamRoom2Id,
        long LabRoomId,
        long ImagingRoomId,
        long FunctionalRoomId,
        long ExamServicePoint1Id,
        long ExamServicePoint2Id,
        long LabServicePointId,
        long ImagingServicePointId,
        long FunctionalServicePointId,
        long ExamServiceId,
        long BloodTestServiceId,
        long UltrasoundServiceId,
        long OptionalUltrasoundServiceId,
        long FreeBpServiceId,
        long AdminUserId,
        long AdminStaffId,
        long ReceptionUserId,
        long ReceptionStaffId,
        long Doctor1UserId,
        long Doctor1StaffId,
        long Doctor1Id,
        string Doctor1Code,
        long Doctor2UserId,
        long Doctor2StaffId,
        long Doctor2Id,
        long DiagStaff1UserId,
        long DiagStaff1Id,
        long DiagStaff2UserId,
        long DiagStaff2Id,
        long PharmacistUserId,
        long PharmacistStaffId,
        long PatientUserId,
        long PatientId,
        long StrangerPatientUserId,
        long StrangerPatientId,
        long MedicineId,
        List<long> BatchIds,
        List<long> CreatedSlotIds,
        List<long> CreatedAppointmentIds,
        string Icd10Code,
        long? CreatedIcd10Id,
        string SecondaryIcd10Code,
        long? CreatedSecondaryIcd10Id);

    private async Task<IsolatedPhase07Fixture> CreateIsolatedFixtureAsync(IReadOnlyList<decimal> batchQuantities)
    {
        Db.ChangeTracker.Clear();
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var seedBaseTime = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified);
        var today = DateOnly.FromDateTime(HospitalClock.GetNow(_clock));

        var recRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Reception);
        var docRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Doctor);
        var diagRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.DiagnosticStaff);
        var pharmRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.PharmacyStaff);
        var patRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Patient);
        var adminRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.HospitalAdmin);

        await EnsureRolePermissionsAsync(recRole.Id, ["PATIENT_CHECKIN", "PAYMENT_VIEW", "PAYMENT_COLLECT", "QUEUE_MANAGE", "VISIT_ASSIGN"]);
        await EnsureRolePermissionsAsync(docRole.Id, ["MEDICAL_RECORD_WRITE", "DIAGNOSTIC_ORDER", "PRESCRIPTION_WRITE", "PRESCRIPTION_ISSUE", "DIAGNOSTIC_RESULT_READ", "PATIENT_PROFILE_READ", "QUEUE_MANAGE"]);
        await EnsureRolePermissionsAsync(diagRole.Id, ["DIAGNOSTIC_ORDER", "QUEUE_MANAGE", "DIAGNOSTIC_RESULT_READ", "DIAGNOSTIC_RESULT_WRITE"]);
        await EnsureRolePermissionsAsync(pharmRole.Id, ["MEDICINE_DISPENSE"]);
        await EnsureRolePermissionsAsync(adminRole.Id, ["HOSPITAL_CONFIG"]);

        var existingIcds = await Db.Icd10Codes.AsNoTracking()
            .Where(c => c.IsSelectable == true && c.NotPrimaryDx != true && c.NodeType == "LEAF" && c.Code != null)
            .OrderBy(c => c.Id)
            .Take(2)
            .ToListAsync();
        long? createdIcdId = null;
        string icdCode;
        if (existingIcds.Count >= 1 && !string.IsNullOrWhiteSpace(existingIcds[0].Code))
        {
            icdCode = existingIcds[0].Code!;
        }
        else
        {
            var customIcd = new Icd10Code
            {
                Code = $"K29.{suffix[..2]}",
                NameVi = "Viêm dạ dày cấp kiểm thử P07",
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

        long? createdSecondaryIcdId = null;
        string secondaryIcdCode;
        if (existingIcds.Count >= 2 && !string.IsNullOrWhiteSpace(existingIcds[1].Code))
        {
            secondaryIcdCode = existingIcds[1].Code!;
        }
        else
        {
            var customIcd2 = new Icd10Code
            {
                Code = $"K30.{suffix[..2]}",
                NameVi = "Khó tiêu chức năng kiểm thử P07",
                CatalogVersion = "ICD10-VN",
                NodeType = "LEAF",
                IsSelectable = true,
                NotPrimaryDx = false,
            };
            Db.Icd10Codes.Add(customIcd2);
            await Db.SaveChangesAsync();
            createdSecondaryIcdId = customIcd2.Id;
            secondaryIcdCode = customIcd2.Code;
        }

        var specialty = new Specialty { SpecialtyCode = $"SP7{suffix}", Name = $"Chuyên khoa Nội P07 {suffix}", IsActive = true };
        var hospital = new Hospital { HospitalCode = $"P07{suffix}", Name = $"Bệnh viện Đa khoa P07 {suffix}", IsActive = true };
        Db.Specialties.Add(specialty);
        Db.Hospitals.Add(hospital);
        await Db.SaveChangesAsync();

        Db.HospitalPolicies.Add(new HospitalPolicy
        {
            HospitalId = hospital.Id,
            PolicyType = "PAYMENT",
            PolicyKey = "vietqr_account",
            PolicyValue = """{"bin":"970422","accountNumber":"0123456789","accountName":"BENH VIEN P07"}""",
            PolicyVersion = 1,
        });

        var dept = new Department { HospitalId = hospital.Id, DepartmentCode = $"DP7{suffix}", Name = $"Khoa Nội & CLS P07 {suffix}", IsActive = true };
        Db.Departments.Add(dept);
        await Db.SaveChangesAsync();

        var examService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialty.Id,
            ServiceCode = $"EX7{suffix}",
            Name = "Khám Nội Tiêu Hóa P07",
            ServiceType = "EXAMINATION",
            AllowsOnlineBooking = true,
            IsActive = true,
        };
        var bloodTestService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialty.Id,
            ServiceCode = $"XN7{suffix}",
            Name = "Xét nghiệm công thức máu P07",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = true,
            IsActive = true,
        };
        var ultrasoundService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialty.Id,
            ServiceCode = $"SA7{suffix}",
            Name = "Siêu âm ổ bụng bắt buộc P07",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var optionalUltrasoundService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialty.Id,
            ServiceCode = $"SAO{suffix}",
            Name = "Siêu âm ổ bụng tầm soát tùy chọn P07",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        var freeBpService = new Service
        {
            HospitalId = hospital.Id,
            DepartmentId = dept.Id,
            SpecialtyId = specialty.Id,
            ServiceCode = $"TDCN{suffix}",
            Name = "Đo theo dõi huyết áp miễn phí P07",
            ServiceType = "DIAGNOSTIC",
            RequiresResultVerification = false,
            IsActive = true,
        };
        Db.Services.AddRange(examService, bloodTestService, ultrasoundService, optionalUltrasoundService, freeBpService);
        await Db.SaveChangesAsync();

        Db.ServicePrices.AddRange(
            new ServicePrice { ServiceId = examService.Id, PriceAmount = 150_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = bloodTestService.Id, PriceAmount = 200_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = ultrasoundService.Id, PriceAmount = 150_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = optionalUltrasoundService.Id, PriceAmount = 100_000m, EffectiveFrom = seedBaseTime },
            new ServicePrice { ServiceId = freeBpService.Id, PriceAmount = 0m, EffectiveFrom = seedBaseTime });

        var examRoom1 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"E71{suffix}"[..9], Name = "PK Nội 101 P07", RoomType = "EXAMINATION", Capacity = 5, Status = "ACTIVE" };
        var examRoom2 = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"E72{suffix}"[..9], Name = "PK Nội 102 P07", RoomType = "EXAMINATION", Capacity = 5, Status = "ACTIVE" };
        var labRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"LB7{suffix}"[..9], Name = "PXN 201 P07", RoomType = "DIAGNOSTIC", Capacity = 3, Status = "ACTIVE" };
        var imagingRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"IM7{suffix}"[..9], Name = "PSA 202 P07", RoomType = "DIAGNOSTIC", Capacity = 3, Status = "ACTIVE" };
        var functionalRoom = new HospitalRoom { HospitalId = hospital.Id, DepartmentId = dept.Id, RoomCode = $"FN7{suffix}"[..9], Name = "PTDCN 203 P07", RoomType = "DIAGNOSTIC", Capacity = 3, Status = "ACTIVE" };
        Db.HospitalRooms.AddRange(examRoom1, examRoom2, labRoom, imagingRoom, functionalRoom);
        await Db.SaveChangesAsync();

        var admUser = new User { Email = $"adm-p07-{suffix}@medflow.test", AuthProvider = "LOCAL", AccountStatus = AccountStatus.ACTIVE };
        var recUser = new User { Email = $"rec-p07-{suffix}@medflow.test", AuthProvider = "LOCAL", AccountStatus = AccountStatus.ACTIVE };
        var doc1User = new User { Email = $"doc1-p07-{suffix}@medflow.test", AuthProvider = "LOCAL", AccountStatus = AccountStatus.ACTIVE };
        var doc2User = new User { Email = $"doc2-p07-{suffix}@medflow.test", AuthProvider = "LOCAL", AccountStatus = AccountStatus.ACTIVE };
        var diag1User = new User { Email = $"dg1-p07-{suffix}@medflow.test", AuthProvider = "LOCAL", AccountStatus = AccountStatus.ACTIVE };
        var diag2User = new User { Email = $"dg2-p07-{suffix}@medflow.test", AuthProvider = "LOCAL", AccountStatus = AccountStatus.ACTIVE };
        var pharmUser = new User { Email = $"pha-p07-{suffix}@medflow.test", AuthProvider = "LOCAL", AccountStatus = AccountStatus.ACTIVE };
        var patUser = new User { Email = $"pat1-p07-{suffix}@medflow.test", AuthProvider = "LOCAL", AccountStatus = AccountStatus.ACTIVE };
        var strangerUser = new User { Email = $"pat2-p07-{suffix}@medflow.test", AuthProvider = "LOCAL", AccountStatus = AccountStatus.ACTIVE };
        Db.Users.AddRange(admUser, recUser, doc1User, doc2User, diag1User, diag2User, pharmUser, patUser, strangerUser);
        await Db.SaveChangesAsync();

        Db.UserRoles.AddRange(
            new UserRole { UserId = admUser.Id, RoleId = adminRole.Id },
            new UserRole { UserId = recUser.Id, RoleId = recRole.Id },
            new UserRole { UserId = doc1User.Id, RoleId = docRole.Id },
            new UserRole { UserId = doc2User.Id, RoleId = docRole.Id },
            new UserRole { UserId = diag1User.Id, RoleId = diagRole.Id },
            new UserRole { UserId = diag2User.Id, RoleId = diagRole.Id },
            new UserRole { UserId = pharmUser.Id, RoleId = pharmRole.Id },
            new UserRole { UserId = patUser.Id, RoleId = patRole.Id },
            new UserRole { UserId = strangerUser.Id, RoleId = patRole.Id });

        var admStaff = new Staff { UserId = admUser.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"AD7{suffix}", FullName = "Quản trị Bệnh viện P07", Status = "ACTIVE" };
        var recStaff = new Staff { UserId = recUser.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"RC7{suffix}", FullName = "Lễ tân Nguyễn Thị Tiếp Đón", Status = "ACTIVE" };
        var doc1Staff = new Staff { UserId = doc1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"D17{suffix}", FullName = "BS.CKI Trần Văn Một", Status = "ACTIVE" };
        var doc2Staff = new Staff { UserId = doc2User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"D27{suffix}", FullName = "BS.CKII Lê Văn Hai", Status = "ACTIVE" };
        var diag1Staff = new Staff { UserId = diag1User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"DG1{suffix}", FullName = "KTV. Lê Thị Xét Nghiệm", Status = "ACTIVE" };
        var diag2Staff = new Staff { UserId = diag2User.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"DG2{suffix}", FullName = "BS.CKII Phạm Thẩm Định", Status = "ACTIVE" };
        var pharmStaff = new Staff { UserId = pharmUser.Id, HospitalId = hospital.Id, DepartmentId = dept.Id, StaffCode = $"PH7{suffix}", FullName = "DS. Hoàng Thị Cấp Phát", Status = "ACTIVE" };
        Db.Staff.AddRange(admStaff, recStaff, doc1Staff, doc2Staff, diag1Staff, diag2Staff, pharmStaff);
        await Db.SaveChangesAsync();

        var doctor1 = new Doctor { StaffId = doc1Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialty.Id, DoctorCode = $"DC17{suffix}", ProfessionalTitle = "BS.CKI", LicenseNumber = $"LC17-{suffix}", IsActive = true };
        var doctor2 = new Doctor { StaffId = doc2Staff.Id, HospitalId = hospital.Id, SpecialtyId = specialty.Id, DoctorCode = $"DC27{suffix}", ProfessionalTitle = "BS.CKII", LicenseNumber = $"LC27-{suffix}", IsActive = true };
        Db.Doctors.AddRange(doctor1, doctor2);
        await Db.SaveChangesAsync();

        foreach (var docId in new[] { doctor1.Id, doctor2.Id })
        {
            Db.DoctorServiceAuthorizations.AddRange(
                new DoctorServiceAuthorization { DoctorId = docId, ServiceId = examService.Id, IsActive = true },
                new DoctorServiceAuthorization { DoctorId = docId, ServiceId = bloodTestService.Id, IsActive = true },
                new DoctorServiceAuthorization { DoctorId = docId, ServiceId = ultrasoundService.Id, IsActive = true },
                new DoctorServiceAuthorization { DoctorId = docId, ServiceId = optionalUltrasoundService.Id, IsActive = true },
                new DoctorServiceAuthorization { DoctorId = docId, ServiceId = freeBpService.Id, IsActive = true });
        }

        var examSp1 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom1.Id, DoctorId = doctor1.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "Điểm khám 101 P07", IsActive = true };
        var examSp2 = new QueueServicePoint { HospitalId = hospital.Id, RoomId = examRoom2.Id, DoctorId = doctor2.Id, ServiceId = examService.Id, PointType = "EXAMINATION_ROOM", Name = "Điểm khám 102 P07", IsActive = true };
        var labSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = labRoom.Id, ServiceId = bloodTestService.Id, PointType = "DIAGNOSTIC", Name = "Điểm XN 201 P07", IsActive = true };
        var imagingSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = imagingRoom.Id, ServiceId = null, PointType = "DIAGNOSTIC", Name = "Điểm SA 202 P07", IsActive = true };
        var functionalSp = new QueueServicePoint { HospitalId = hospital.Id, RoomId = functionalRoom.Id, ServiceId = freeBpService.Id, PointType = "DIAGNOSTIC", Name = "Điểm TDCN 203 P07", IsActive = true };
        Db.QueueServicePoints.AddRange(examSp1, examSp2, labSp, imagingSp, functionalSp);

        var patient = new Patient
        {
            UserId = patUser.Id,
            PatientCode = $"BN71{suffix}",
            FullName = "Nguyễn Văn Bệnh Nhân",
            DateOfBirth = new DateOnly(1990, 5, 15),
            Gender = "MALE",
            Phone = "0909123456",
            IdentificationHash = $"p07-cccd-1-{suffix}",
            IdentificationHashHmac = $"p07-hmac-1-{suffix}",
            Status = "ACTIVE",
        };
        var strangerPatient = new Patient
        {
            UserId = strangerUser.Id,
            PatientCode = $"BN72{suffix}",
            FullName = "Trần Thị Người Lạ",
            DateOfBirth = new DateOnly(1993, 8, 22),
            Gender = "FEMALE",
            Phone = "0909654321",
            IdentificationHash = $"p07-cccd-2-{suffix}",
            IdentificationHashHmac = $"p07-hmac-2-{suffix}",
            Status = "ACTIVE",
        };
        Db.Patients.AddRange(patient, strangerPatient);

        var medicine = new Medicine
        {
            HospitalId = hospital.Id,
            MedicineCode = $"MD7{suffix}",
            Name = $"Esomeprazol 40mg P07 {suffix}",
            ActiveIngredient = "Esomeprazole",
            Strength = "40mg",
            DosageForm = "Viên nang kháng dịch vị",
            DispensingUnit = "viên",
            IsActive = true,
        };
        Db.Medicines.Add(medicine);
        await Db.SaveChangesAsync();

        var batchIds = new List<long>(batchQuantities.Count);
        for (var i = 0; i < batchQuantities.Count; i++)
        {
            var batch = new MedicineBatch
            {
                MedicineId = medicine.Id,
                BatchNumber = $"LOT7-{suffix}-{i + 1}",
                ExpiryDate = today.AddMonths(3 + (i * 4)),
                QuantityOnHand = batchQuantities[i],
                IsBlocked = false,
            };
            Db.MedicineBatches.Add(batch);
            await Db.SaveChangesAsync();
            batchIds.Add(batch.Id);
        }

        await using (var polScope = _app.Services.CreateAsyncScope())
        {
            var policySvc = polScope.ServiceProvider.GetRequiredService<IAppointmentPolicyService>();
            await policySvc.UpdateForAdminAsync(
                admUser.Id,
                new UpdateAppointmentPolicyRequest
                {
                    BookingWindowDays = 30,
                    BookingLeadHours = 0,
                    MaxActiveBookings = 5,
                    RescheduleMinimumHours = 0,
                    MaxReschedules = 2,
                    CancelMinimumHours = 0,
                    NoShowGraceMinutes = 15,
                    NoShowMaxCount = 5,
                    NoShowLookbackDays = 90,
                    NoShowBlockDays = 0,
                },
                "127.0.0.1");
        }

        return new IsolatedPhase07Fixture(
            Suffix: suffix,
            HospitalId: hospital.Id,
            DepartmentId: dept.Id,
            SpecialtyId: specialty.Id,
            ExamRoom1Id: examRoom1.Id,
            ExamRoom2Id: examRoom2.Id,
            LabRoomId: labRoom.Id,
            ImagingRoomId: imagingRoom.Id,
            FunctionalRoomId: functionalRoom.Id,
            ExamServicePoint1Id: examSp1.Id,
            ExamServicePoint2Id: examSp2.Id,
            LabServicePointId: labSp.Id,
            ImagingServicePointId: imagingSp.Id,
            FunctionalServicePointId: functionalSp.Id,
            ExamServiceId: examService.Id,
            BloodTestServiceId: bloodTestService.Id,
            UltrasoundServiceId: ultrasoundService.Id,
            OptionalUltrasoundServiceId: optionalUltrasoundService.Id,
            FreeBpServiceId: freeBpService.Id,
            AdminUserId: admUser.Id,
            AdminStaffId: admStaff.Id,
            ReceptionUserId: recUser.Id,
            ReceptionStaffId: recStaff.Id,
            Doctor1UserId: doc1User.Id,
            Doctor1StaffId: doc1Staff.Id,
            Doctor1Id: doctor1.Id,
            Doctor1Code: doctor1.DoctorCode,
            Doctor2UserId: doc2User.Id,
            Doctor2StaffId: doc2Staff.Id,
            Doctor2Id: doctor2.Id,
            DiagStaff1UserId: diag1User.Id,
            DiagStaff1Id: diag1Staff.Id,
            DiagStaff2UserId: diag2User.Id,
            DiagStaff2Id: diag2Staff.Id,
            PharmacistUserId: pharmUser.Id,
            PharmacistStaffId: pharmStaff.Id,
            PatientUserId: patUser.Id,
            PatientId: patient.Id,
            StrangerPatientUserId: strangerUser.Id,
            StrangerPatientId: strangerPatient.Id,
            MedicineId: medicine.Id,
            BatchIds: batchIds,
            CreatedSlotIds: [],
            CreatedAppointmentIds: [],
            Icd10Code: icdCode,
            CreatedIcd10Id: createdIcdId,
            SecondaryIcd10Code: secondaryIcdCode,
            CreatedSecondaryIcd10Id: createdSecondaryIcdId);
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

            if (!await Db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == perm.Id))
            {
                Db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = perm.Id });
                await Db.SaveChangesAsync();
            }
        }
    }

    private async Task CleanupIsolatedFixtureAsync(IsolatedPhase07Fixture fx)
    {
        Db.ChangeTracker.Clear();

        var patientIds = new[] { fx.PatientId, fx.StrangerPatientId };
        var userIds = new[]
        {
            fx.AdminUserId,
            fx.ReceptionUserId,
            fx.Doctor1UserId,
            fx.Doctor2UserId,
            fx.DiagStaff1UserId,
            fx.DiagStaff2UserId,
            fx.PharmacistUserId,
            fx.PatientUserId,
            fx.StrangerPatientUserId,
        };

        var apptIds = await Db.Appointments
            .Where(a => (a.PatientId.HasValue && patientIds.Contains(a.PatientId.Value)) || fx.CreatedAppointmentIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync();

        var visitIds = await Db.Visits
            .Where(v => (v.PatientId.HasValue && patientIds.Contains(v.PatientId.Value)) ||
                        (v.AppointmentId.HasValue && apptIds.Contains(v.AppointmentId.Value)))
            .Select(v => v.Id)
            .ToListAsync();

        await Db.Notifications
            .Where(n => (n.VisitId.HasValue && visitIds.Contains(n.VisitId.Value)) ||
                        (n.AppointmentId.HasValue && apptIds.Contains(n.AppointmentId.Value)) ||
                        (n.PatientId.HasValue && patientIds.Contains(n.PatientId.Value)))
            .ExecuteDeleteAsync();

        var orderIds = await Db.DiagnosticOrders
            .Where(o => o.VisitId.HasValue && visitIds.Contains(o.VisitId.Value))
            .Select(o => o.Id)
            .ToListAsync();

        if (orderIds.Count > 0)
        {
            var resultIds = await Db.DiagnosticResults
                .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                .Select(r => r.Id)
                .ToListAsync();

            if (resultIds.Count > 0)
            {
                await Db.DiagnosticResultVersions
                    .Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value))
                    .ExecuteDeleteAsync();
                await Db.DiagnosticResults.Where(r => resultIds.Contains(r.Id)).ExecuteDeleteAsync();
            }
        }

        var rxIds = await Db.Prescriptions
            .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
            .Select(p => p.Id)
            .ToListAsync();

        if (rxIds.Count > 0 || fx.BatchIds.Count > 0)
        {
            var dispIds = await Db.MedicationDispenses
                .Where(md => md.PrescriptionId.HasValue && rxIds.Contains(md.PrescriptionId.Value))
                .Select(md => md.Id)
                .ToListAsync();

            var dispItemIds = await Db.DispenseItems
                .Where(di => (di.DispenseId.HasValue && dispIds.Contains(di.DispenseId.Value)) ||
                             (di.MedicineBatchId.HasValue && fx.BatchIds.Contains(di.MedicineBatchId.Value)))
                .Select(di => di.Id)
                .ToListAsync();

            if (dispItemIds.Count > 0 || fx.BatchIds.Count > 0)
            {
                await Db.StockTransactions
                    .Where(st => (st.DispenseItemId.HasValue && dispItemIds.Contains(st.DispenseItemId.Value)) ||
                                 (st.MedicineBatchId.HasValue && fx.BatchIds.Contains(st.MedicineBatchId.Value)))
                    .ExecuteDeleteAsync();
            }

            if (dispItemIds.Count > 0)
            {
                await Db.DispenseItems.Where(di => dispItemIds.Contains(di.Id)).ExecuteDeleteAsync();
            }

            if (dispIds.Count > 0)
            {
                await Db.MedicationDispenses.Where(md => dispIds.Contains(md.Id)).ExecuteDeleteAsync();
            }

            if (rxIds.Count > 0)
            {
                await Db.PrescriptionItems.Where(pi => pi.PrescriptionId.HasValue && rxIds.Contains(pi.PrescriptionId.Value)).ExecuteDeleteAsync();
                await Db.Prescriptions.Where(p => rxIds.Contains(p.Id)).ExecuteDeleteAsync();
            }
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
        }

        var paymentIds = await Db.Payments
            .Where(p => p.VisitId.HasValue && visitIds.Contains(p.VisitId.Value))
            .Select(p => p.Id)
            .ToListAsync();
        if (paymentIds.Count > 0 || visitIds.Count > 0)
        {
            await Db.PaymentGatewayEvents.Where(e => e.PaymentId.HasValue && paymentIds.Contains(e.PaymentId.Value)).ExecuteDeleteAsync();
            await Db.PaymentAllocations.Where(pa => paymentIds.Contains(pa.PaymentId) || visitIds.Contains(pa.VisitId)).ExecuteDeleteAsync();
            await Db.Payments.Where(p => paymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        }

        var spIds = new[]
        {
            fx.ExamServicePoint1Id,
            fx.ExamServicePoint2Id,
            fx.LabServicePointId,
            fx.ImagingServicePointId,
            fx.FunctionalServicePointId,
        };
        // Stage E2 (migration 027): fk_queue_entries_diagnostic_order_visit ON DELETE RESTRICT, and
        // fk_diagnostic_orders_queue_entry_id / fk_queue_fairness_states_last_started_queue_entry_id back-
        // reference queue_entries. Clear those pointers, then fairness, then queue_entries BEFORE the
        // diagnostic_orders / prescriptions they bind are deleted below.
        await Db.DiagnosticOrders
            .Where(o => o.QueueEntryId.HasValue && o.VisitId.HasValue && visitIds.Contains(o.VisitId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.QueueEntryId, (long?)null));
        await Db.QueueFairnessStates.Where(f => spIds.Contains(f.ServicePointId)).ExecuteDeleteAsync();

        if (visitIds.Count > 0 || apptIds.Count > 0)
        {
            await Db.VisitCharges.Where(c => c.VisitId.HasValue && visitIds.Contains(c.VisitId.Value)).ExecuteDeleteAsync();

            await Db.QueueEntries
                .Where(q => (q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value)) ||
                            (q.AppointmentId.HasValue && apptIds.Contains(q.AppointmentId.Value)) ||
                            (q.ServicePointId.HasValue && spIds.Contains(q.ServicePointId.Value)))
                .ExecuteDeleteAsync();

            if (orderIds.Count > 0)
            {
                await Db.DiagnosticOrders.Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
            }
        }

        await Db.AuditLogs
            .Where(a => a.HospitalId == fx.HospitalId || (a.ActorUserId.HasValue && userIds.Contains(a.ActorUserId.Value)))
            .ExecuteDeleteAsync();

        if (visitIds.Count > 0)
        {
            await Db.Visits.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync();
        }

        if (apptIds.Count > 0)
        {
            await Db.Appointments
                .Where(a => apptIds.Contains(a.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.RescheduledFromAppointmentId, (long?)null)
                    .SetProperty(a => a.RootAppointmentId, (long?)null));
            await Db.Appointments.Where(a => apptIds.Contains(a.Id)).ExecuteDeleteAsync();
        }

        if (fx.CreatedSlotIds.Count > 0)
        {
            await Db.ExaminationSlots.Where(s => fx.CreatedSlotIds.Contains(s.Id)).ExecuteDeleteAsync();
        }

        await Db.QueueServicePoints.Where(sp => spIds.Contains(sp.Id)).ExecuteDeleteAsync();
        await Db.DoctorServiceAuthorizations.Where(a => a.DoctorId == fx.Doctor1Id || a.DoctorId == fx.Doctor2Id).ExecuteDeleteAsync();

        var serviceIds = new[]
        {
            fx.ExamServiceId,
            fx.BloodTestServiceId,
            fx.UltrasoundServiceId,
            fx.OptionalUltrasoundServiceId,
            fx.FreeBpServiceId,
        };
        await Db.ServicePrices.Where(sp => sp.ServiceId.HasValue && serviceIds.Contains(sp.ServiceId.Value)).ExecuteDeleteAsync();

        if (fx.BatchIds.Count > 0)
        {
            await Db.MedicineBatches.Where(b => fx.BatchIds.Contains(b.Id)).ExecuteDeleteAsync();
        }

        await Db.Medicines.Where(m => m.Id == fx.MedicineId).ExecuteDeleteAsync();
        await Db.Patients.Where(p => patientIds.Contains(p.Id)).ExecuteDeleteAsync();
        await Db.Doctors.Where(d => d.Id == fx.Doctor1Id || d.Id == fx.Doctor2Id).ExecuteDeleteAsync();

        var staffIds = new[]
        {
            fx.AdminStaffId,
            fx.ReceptionStaffId,
            fx.Doctor1StaffId,
            fx.Doctor2StaffId,
            fx.DiagStaff1Id,
            fx.DiagStaff2Id,
            fx.PharmacistStaffId,
        };
        await Db.Staff.Where(s => staffIds.Contains(s.Id)).ExecuteDeleteAsync();

        await Db.AuthSessions.Where(s => userIds.Contains(s.UserId)).ExecuteDeleteAsync();
        await Db.UserRoles.Where(ur => userIds.Contains(ur.UserId)).ExecuteDeleteAsync();
        await Db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();

        var roomIds = new[] { fx.ExamRoom1Id, fx.ExamRoom2Id, fx.LabRoomId, fx.ImagingRoomId, fx.FunctionalRoomId };
        await Db.HospitalRooms.Where(r => roomIds.Contains(r.Id)).ExecuteDeleteAsync();
        await Db.Services.Where(s => serviceIds.Contains(s.Id)).ExecuteDeleteAsync();
        await Db.Departments.Where(d => d.Id == fx.DepartmentId).ExecuteDeleteAsync();
        await Db.HospitalPolicies.Where(p => p.HospitalId == fx.HospitalId).ExecuteDeleteAsync();
        await Db.Hospitals.Where(h => h.Id == fx.HospitalId).ExecuteDeleteAsync();
        await Db.Specialties.Where(sp => sp.Id == fx.SpecialtyId).ExecuteDeleteAsync();

        if (fx.CreatedIcd10Id.HasValue)
        {
            await Db.Icd10Codes.Where(c => c.Id == fx.CreatedIcd10Id.Value).ExecuteDeleteAsync();
        }

        if (fx.CreatedSecondaryIcd10Id.HasValue)
        {
            await Db.Icd10Codes.Where(c => c.Id == fx.CreatedSecondaryIcd10Id.Value).ExecuteDeleteAsync();
        }

        Db.ChangeTracker.Clear();
    }
}
