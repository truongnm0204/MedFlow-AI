using System.Text;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Ekyc;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// Strict finalize — the TRUST ANCHOR of the eKYC pipeline (PM ruling: frontend never submits
/// verdicts; backend decides everything here). If finalize passes wrongly the entire eKYC is
/// meaningless — this service is fail-closed on every uncertainty.
///
/// Validation chain (ALL must pass, in this order — cheapest/most decisive first):
/// <list type="number">
///   <item>(e) ownership/mode authority + non-terminal + within TTL + not consumed (single-use).</item>
///   <item>(b) step completeness: OCR document + card liveness are REQUIRED (owner scope 2026-11-28:
///       CCCD verification needs front/back OCR + card anti-spoofing + tampering verdict). Face
///       liveness and face compare are OPTIONAL. When compare is present, document must precede it.</item>
///   <item>(c) binding: required only when compare ran — binding_state must be VALID; no compare →
///       NOT_APPLICABLE (never a rejection). Compare present with invalid binding stays fail-closed.</item>
///   <item>(a) dataSign valid for EVERY captured step payload via PUBLIC_KEY_CA
///       (RSA-SHA256/PKCS1v15 over the verbatim dataBase64 string — probe Q2).</item>
///   <item>(d) policy verdict: card liveness pass + tampering is_legal + OCR matches declared
///       name/DOB/CCCD; if optional face steps ran their verdicts must also pass (fail-closed);
///       compare.prob ≥ configured threshold only when compare ran.</item>
/// </list>
/// Atomicity: the session row is locked FOR UPDATE inside a transaction (ADR-0059 level 20);
/// consumed_at is set exactly once — concurrent finalize is single-winner, loser gets 409.
///
/// FUZZY MATCHING RULES (documented business decision):
/// <list type="bullet">
///   <item>Normalize Unicode (NFC), strip diacritics ( Vietnamese tonal marks removed), uppercase,
///         collapse whitespace, trim — both sides.</item>
///   <item>Name: exact equality after normalization. Rationale: CCCD OCR of the chip-printed name
///         is highly accurate; accepting partial/fuzzy name matches would weaken composite-attack
///         resistance for marginal UX gain. Documented deviation candidates (middle-name ordering)
///         are NOT auto-accepted — mismatch → REJECTED, user corrects registration or retries.</item>
///   <item>DOB: parse-tolerant — VNPT emits dd/MM/yyyy; registration stores DateOnly. Compared as
///         calendar dates, never strings. Unparseable OCR date → fail-closed.</item>
///   <item>CCCD number: OCR id is normalized and HMAC'd with the server secret, then compared in
///         constant time to the declared identification HMAC (session intake or patient record).
///         The plaintext declared number is never stored. Missing/mismatched OCR id → fail-closed.</item>
/// </list>
/// </summary>
public sealed class EkycVerificationService : IEkycVerificationService
{
    public const string SessionNotFoundMessage = "Không tìm thấy phiên xác minh danh tính.";
    public const string SessionNotFinalizableMessage =
        "Phiên xác minh danh tính không còn ở trạng thái cho phép kết thúc.";
    public const string SessionExpiredMessage =
        "Phiên xác minh danh tính đã hết hạn. Vui lòng tạo phiên mới.";
    public const string ConcurrentFinalizeMessage =
        "Phiên xác minh danh tính đang được xử lý kết thúc bởi một yêu cầu khác.";

    // Failure codes — coarse, machine-readable, never leaking internals.
    public const string FailMissingStep = "MISSING_STEP";
    public const string FailInvalidBinding = "INVALID_BINDING";
    public const string FailSignatureInvalid = "SIGNATURE_INVALID";
    public const string FailLiveness = "LIVENESS_FAILED";
    public const string FailCompareBelowThreshold = "COMPARE_BELOW_THRESHOLD";
    public const string FailTampering = "TAMPERING_DETECTED";
    public const string FailOcrMismatch = "OCR_MISMATCH";
    public const string FailEvidenceMissing = "EVIDENCE_MISSING";

    // Endpoint classifiers (probe Q3 paths; ocr family matched by substring in ClassifyStep).
    internal const string CardLivenessSuffix = "card/liveness";
    internal const string FaceLivenessSuffix = "face/liveness";
    internal const string CompareSuffix = "face/compare";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly EkycOptions _options;
    private readonly IOptions<IdentityHashOptions>? _identityOptions;
    private readonly ILogger<EkycVerificationService>? _logger;

    public EkycVerificationService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IOptions<EkycOptions>? options = null,
        IOptions<IdentityHashOptions>? identityOptions = null,
        ILogger<EkycVerificationService>? logger = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options?.Value ?? new EkycOptions();
        _identityOptions = identityOptions;
        _logger = logger;
    }

    public async Task<FinalizeEkycSessionResponse> FinalizeAsync(
        long actorUserId,
        long sessionId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var config = VnptEkycConfigReader.Read(OptionsWrapper());
        var encryptionKey = EkycCrypto.RequireEncryptionKey(OptionsWrapper());
        var hmacKey = EkycCrypto.RequireHmacKey(OptionsWrapper(), _identityOptions);

        await using var transaction = await _db.BeginTransactionAsync(ct);

        // --- ATOMIC single-winner: lock the session row FOR UPDATE first ----------------------
        // Loser of a concurrent race blocks until winner commits, then sees terminal status and
        // is rejected below — consumed_at can only ever be set once.
        // Non-relational test contexts return false from the default interface implementation
        // (no locking primitive) — the row-existence fallback query keeps semantics uniform:
        // in production false means row missing (same NotFound), in tests we proceed unlocked
        // and the terminal-status check still guards replay.
        var lockAcquired = await _db.TryLockEkycVerificationSessionForUpdateAsync(sessionId, ct);
        if (!lockAcquired && !await _db.EkycVerificationSessions.AnyAsync(s => s.Id == sessionId, ct))
        {
            throw new NotFoundException("EkycVerificationSession", sessionId);
        }

        var session = await _db.EkycVerificationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException("EkycVerificationSession", sessionId);

        // Ownership enforcement — never leak existence cross-account. Reception-assisted sessions
        // are operated by the staff member who created them (performed_by), and that staff member
        // must currently hold PATIENT_IDENTITY_VERIFY. The eKYC engine still decides the verdict.
        if (string.Equals(session.VerificationMode, EkycVerificationModes.ReceptionAssisted, StringComparison.Ordinal))
        {
            if (session.PerformedBy != actorUserId)
            {
                throw new NotFoundException("EkycVerificationSession", sessionId);
            }

            await EkycStaffAuthority.RequireIdentityVerifyAsync(_db, actorUserId, _timeProvider, ct);
        }
        else if (session.UserId != actorUserId)
        {
            throw new NotFoundException("EkycVerificationSession", sessionId);
        }

        // Terminal / consumed states — covers replay and concurrent-loser cases.
        if (EkycVerificationStatuses.Terminal.Contains(session.Status) || session.ConsumedAt is not null)
        {
            throw new ConflictException(SessionNotFinalizableMessage);
        }

        var nowUtc = HospitalClock.GetUtcNowUnspecified(_timeProvider);
        if (session.ExpiresAt <= nowUtc)
        {
            await RejectAsync(session, actorUserId, "SESSION_EXPIRED",
                "Phiên đã hết hạn trước khi kết thúc.", ipAddress, nowUtc, ct, expire: true);
            await transaction.CommitAsync(ct);
            throw new ConflictException(SessionExpiredMessage);
        }

        // --- Collect signed evidence from proxy call logs --------------------------------------
        var logs = await _db.EkycProxyCallLogs
            .Where(l => l.SessionId == sessionId)
            .OrderBy(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .ToListAsync(ct);

        // Declared identity for OCR matching (owner scope: name + DOB + CCCD number):
        //   1. PATIENT_PROFILE sessions bind a patient record directly.
        //   2. Self-service activation sessions fall back to the account's patient shell.
        //   3. Session-level declared values (reception intake) take precedence — they are what
        //      the patient declared at the counter and are immutable evidence for the match.
        Patient? declaredPatient = null;
        if (session.PatientId is long patientId)
        {
            declaredPatient = await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct);
        }
        else if (session.UserId is long sessionUserId)
        {
            declaredPatient = await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == sessionUserId, ct);
        }

        var evaluation = EvaluateEvidence(
            session, logs, config, encryptionKey, hmacKey, actorUserId,
            expectedNameOverride: session.DeclaredFullName ?? declaredPatient?.FullName,
            expectedDobOverride: session.DeclaredBirthDay ?? declaredPatient?.DateOfBirth,
            expectedIdentificationHmac: session.DeclaredIdentificationHmac ?? declaredPatient?.IdentificationHashHmac);

        EkycDedupeResolution? dedupe = null;
        string? verifiedPatientStatus = null;
        if (evaluation.Success)
        {
            session.Status = EkycVerificationStatuses.Verified;
            session.ConsumedAt = nowUtc;
            session.FailureReason = null;
            session.OcrSnapshot = evaluation.OcrSnapshot;
            session.LivenessVerdicts = evaluation.LivenessVerdicts;
            session.CompareResult = evaluation.CompareResult;
            session.TamperingVerdict = evaluation.TamperingVerdict;
            session.EvidenceDataSign = evaluation.EvidenceDataSign;
            session.EvidencePayloadDigest = evaluation.EvidencePayloadDigest;
            session.EvidencePayloadFull = evaluation.EvidencePayloadFull;
            session.IdentificationHmac = evaluation.IdentificationHmac;
            session.UpdatedAt = nowUtc;

            // Optional face-compare step not run → binding is not applicable (owner scope), NOT a
            // failure. If compare ran, EvaluateEvidence already required VALID binding.
            if (evaluation.CompareResult is null
                && !string.Equals(session.BindingState, EkycBindingStates.Invalid, StringComparison.Ordinal)
                && !string.Equals(session.BindingState, EkycBindingStates.Unverifiable, StringComparison.Ordinal))
            {
                session.BindingState = EkycBindingStates.NotApplicable;
            }

            _db.AuditLogs.Add(BuildAudit(actorUserId, session.Id, EkycConstants.AuditActionVerified,
                "Xác minh danh tính điện tử thành công.", ipAddress, null));

            if (string.Equals(session.Purpose, EkycPurposes.PatientProfile, StringComparison.Ordinal))
            {
                // Patient identity only — NO account activation. The profile becomes VERIFIED
                // exclusively here, after the engine passed; reception never sets it directly.
                var profile = await _db.Patients.FirstOrDefaultAsync(p => p.Id == session.PatientId, ct)
                    ?? throw new NotFoundException("Patient", session.PatientId ?? 0);

                verifiedPatientStatus = await TryMarkPatientVerifiedAsync(
                    profile, evaluation.IdentificationHmac, ct);

                _db.AuditLogs.Add(BuildAudit(actorUserId, session.Id, EkycConstants.AuditActionReceptionAssisted,
                    "Lễ tân điều khiển phiên eKYC và hồ sơ đã được xác minh bởi engine.",
                    ipAddress, null, new
                    {
                        patientId = profile.Id,
                        purpose = session.Purpose,
                        verificationMode = session.VerificationMode,
                        performedBy = session.PerformedBy,
                        bindingState = session.BindingState,
                    }));

                dedupe = new EkycDedupeResolution(
                    EkycDedupeOutcomes.NotApplicable,
                    AccountActivated: false,
                    LinkedPatientId: profile.Id,
                    ReviewReason: null);
            }
            else
            {
                // --- Activation gate + dedupe matrix (plan v2.1 mục 3.2/3.3, atomicity mục 3.8) --
                // Runs INSIDE this transaction: consume + dedupe + activation commit or roll back
                // together. Only self-service pending-activation accounts under mandatory
                // registration whose email is verified; ACTIVE accounts skip activation. Reception
                // staff can never activate an account through eKYC.
                var user = session.UserId is long subjectUserId
                    ? await _db.Users.FirstOrDefaultAsync(u => u.Id == subjectUserId, ct)
                    : null;

                if (user is not null
                    && string.Equals(session.VerificationMode, EkycVerificationModes.SelfService, StringComparison.Ordinal)
                    && user.AccountStatus == Domain.Enums.AccountStatus.PENDING
                    && user.EmailVerifiedAt is not null
                    && _options.MandatoryRegistration)
                {
                    var registrationPatient = await _db.Patients
                        .FirstOrDefaultAsync(p => p.UserId == actorUserId, ct);

                    dedupe = await EkycDedupeResolver.ResolveAsync(
                        _db, user, registrationPatient, session,
                        evaluation.IdentificationHmac,
                        evaluation.OcrFullName,
                        evaluation.OcrBirthDay,
                        actorUserId, ipAddress, ct);
                }
                else
                {
                    dedupe = new EkycDedupeResolution(
                        EkycDedupeOutcomes.NotApplicable,
                        AccountActivated: false,
                        LinkedPatientId: session.PatientId,
                        ReviewReason: null);
                }

                // Patient identity status is independent of account activation: whichever patient
                // record this verified identity resolves to becomes VERIFIED (only here).
                var verifiedPatient = session.PatientId is long linkedId
                    ? await _db.Patients.FirstOrDefaultAsync(p => p.Id == linkedId, ct)
                    : session.UserId is long userId
                        ? await _db.Patients.FirstOrDefaultAsync(p => p.UserId == userId, ct)
                        : null;

                if (verifiedPatient is not null)
                {
                    verifiedPatientStatus = await TryMarkPatientVerifiedAsync(
                        verifiedPatient, evaluation.IdentificationHmac, ct);
                    session.PatientId ??= verifiedPatient.Id;
                }
            }
        }
        else
        {
            await RejectAsync(session, actorUserId, evaluation.FailureCode!,
                evaluation.FailureReason!, ipAddress, nowUtc, ct, expire: false);
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new FinalizeEkycSessionResponse(
            SessionId: session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Status: session.Status,
            FailureReason: evaluation.Success ? null : evaluation.FailureReason,
            FailureCode: evaluation.Success ? null : evaluation.FailureCode,
            ConsumedAt: session.ConsumedAt,
            AccountActivated: dedupe?.AccountActivated,
            ActivationOutcome: dedupe?.Outcome,
            ActivationReviewReason: dedupe?.ReviewReason,
            Purpose: session.Purpose,
            PatientId: session.PatientId,
            PatientIdentityStatus: verifiedPatientStatus);
    }

    // ------------------------------------------------------------------
    // Evidence evaluation — pure decision logic, no I/O (fully unit-testable)
    // ------------------------------------------------------------------

    internal sealed record EvaluationResult(
        bool Success,
        string? FailureCode,
        string? FailureReason,
        string? OcrSnapshot,
        string? LivenessVerdicts,
        string? CompareResult,
        string? TamperingVerdict,
        string? EvidenceDataSign,
        string? EvidencePayloadDigest,
        byte[]? EvidencePayloadFull,
        string? IdentificationHmac,
        string? OcrFullName,
        DateOnly? OcrBirthDay);

    internal static EvaluationResult EvaluateEvidence(
        EkycVerificationSession session,
        IReadOnlyList<EkycProxyCallLog> logs,
        VnptEkycConfig config,
        byte[] encryptionKey,
        string hmacKey,
        long actorUserId,
        string? publicKeyCaOverride = null,
        string? expectedNameOverride = null,
        DateOnly? expectedDobOverride = null,
        string? expectedIdentificationHmac = null)
    {
        var publicKeyCa = publicKeyCaOverride ?? config.PublicKeyCa;

        // --- Decrypt + classify captured signed payloads per step ------------------------------
        var steps = new Dictionary<string, (EkycProxyCallLog Log, string DataBase64, JsonDocument Payload)>(StringComparer.Ordinal);

        foreach (var log in logs)
        {
            if (log.ResponsePayloadEncrypted is null || log.ResponseDataSign is null)
            {
                continue;
            }

            string? stepKey = ClassifyStep(log.EndpointPath);
            if (stepKey is null)
            {
                continue;
            }

            // Latest capture per step wins (re-submission is legitimate; ordering enforced later).
            byte[] payloadBytes;
            string dataBase64;
            try
            {
                payloadBytes = EkycCrypto.Decrypt(log.ResponsePayloadEncrypted, encryptionKey);
                dataBase64 = Encoding.UTF8.GetString(payloadBytes);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Tampered/corrupt at-rest evidence — fail closed.
                return Fail(FailEvidenceMissing, "Bằng chứng xác minh không toàn vẹn (không giải mã được).");
            }

            JsonDocument payload;
            try
            {
                var decoded = EkycDataSignVerifier.DecodeVerifiedPayload(dataBase64);
                if (decoded is null)
                {
                    continue;
                }

                payload = JsonDocument.Parse(decoded);
            }
            catch (JsonException)
            {
                continue;
            }

            steps[stepKey] = (log, dataBase64, payload);
        }

        // --- (b) Step completeness --------------------------------------------------------------
        // Owner scope 2026-11-28 (CCCD-only): OCR document (front+back via ocr/id or ocr/id/front)
        // and card liveness are REQUIRED. Face liveness and face compare are OPTIONAL — their
        // absence must not reject; if they ran, their verdicts are still enforced below.
        string[] requiredSteps = ["document", "card_liveness"];
        foreach (var required in requiredSteps)
        {
            if (!steps.ContainsKey(required))
            {
                return Fail(FailMissingStep, $"Thiếu bước xác minh bắt buộc ({required}).");
            }
        }

        var hasCompare = steps.ContainsKey("compare");

        // Document-before-compare ordering only applies when the optional compare step ran.
        if (hasCompare && steps["document"].Log.CreatedAt > steps["compare"].Log.CreatedAt)
        {
            return Fail(FailMissingStep, "Thứ tự bước xác minh không hợp lệ: giấy tờ phải trước so khớp khuôn mặt.");
        }

        // --- (c) Binding — enforced whenever compare ran; NOT_APPLICABLE otherwise ---------------
        if (hasCompare
            && !string.Equals(session.BindingState, EkycBindingStates.Valid, StringComparison.Ordinal))
        {
            return Fail(FailInvalidBinding,
                "Tài liệu dùng so khớp khuôn mặt không khớp với tài liệu đã tải lên trong phiên.");
        }

        // --- (a) dataSign valid for every captured step -----------------------------------------
        foreach (var (stepKey, (log, dataBase64, _)) in steps)
        {
            if (!EkycDataSignVerifier.Verify(dataBase64, log.ResponseDataSign, publicKeyCa))
            {
                return Fail(FailSignatureInvalid, $"Chữ ký kết quả bước {stepKey} không hợp lệ.");
            }
        }

        // --- (d) Policy verdict -------------------------------------------------------------------
        // Card liveness is mandatory; face liveness is optional but fail-closed when present.
        // VNPT payload shapes (probe Q1): object.liveness == "success" (string).
        foreach (var stepKey in new[] { "card_liveness", "face_liveness" })
        {
            if (!steps.ContainsKey(stepKey))
            {
                continue; // face_liveness optional when compare did not run
            }

            if (!IsLivenessPass(steps[stepKey].Payload))
            {
                return Fail(FailLiveness, $"Kiểm tra liveness bước {stepKey} không đạt.");
            }
        }

        // Tampering: document OCR payload must report is_legal.
        if (!IsTamperingClean(steps["document"].Payload))
        {
            return Fail(FailTampering, "Giấy tờ có dấu hiệu bị chỉnh sửa hoặc không hợp lệ.");
        }

        // Compare prob ≥ configured threshold — only when the optional compare step ran.
        if (hasCompare)
        {
            var compareProb = ExtractCompareProb(steps["compare"].Payload);
            if (compareProb is null || compareProb.Value < config.CompareThreshold)
            {
                return Fail(FailCompareBelowThreshold,
                    "Độ tương đồng khuôn mặt với ảnh thẻ chưa đạt ngưỡng chấp nhận.");
            }
        }

        // OCR fields — the front OCR payload carries identity fields.
        var ocr = ExtractOcrFields(steps["document"].Payload);
        if (ocr is null)
        {
            return Fail(FailEvidenceMissing, "Không trích xuất được thông tin định danh từ giấy tờ.");
        }

        // OCR-vs-declared matching (rules documented at class level): name, DOB and CCCD number.
        if (expectedNameOverride is not null || expectedDobOverride is not null
            || !string.IsNullOrWhiteSpace(expectedIdentificationHmac))
        {
            var nameMatches = expectedNameOverride is null
                || NamesMatch(ocr.Value.Name, expectedNameOverride);
            var dobMatches = expectedDobOverride is null
                || ocr.Value.BirthDay == expectedDobOverride.Value;

            var idMatches = true;
            if (!string.IsNullOrWhiteSpace(expectedIdentificationHmac))
            {
                idMatches = false;
                if (!string.IsNullOrWhiteSpace(ocr.Value.IdNumber))
                {
                    try
                    {
                        var declaredHmac = EkycCrypto.ComputeIdentificationHmac(
                            IdentificationNumber.Normalize(ocr.Value.IdNumber), hmacKey);
                        idMatches = EkycCrypto.FixedTimeEquals(declaredHmac, expectedIdentificationHmac);
                    }
                    catch (ValidationException)
                    {
                        idMatches = false;
                    }
                }
            }

            if (!nameMatches || !dobMatches || !idMatches)
            {
                return Fail(FailOcrMismatch,
                    "Thông tin trên giấy tờ không khớp với thông tin đã khai báo (họ tên, ngày sinh hoặc số CCCD).");
            }
        }

        // identification_hmac — normalized CCCD/CMND id, HMAC byte-identical to patients construction.
        string? identificationHmac = null;
        if (!string.IsNullOrWhiteSpace(ocr.Value.IdNumber))
        {
            try
            {
                var canonical = IdentificationNumber.Normalize(ocr.Value.IdNumber);
                identificationHmac = EkycCrypto.ComputeIdentificationHmac(canonical, hmacKey);
            }
            catch (ValidationException)
            {
                return Fail(FailEvidenceMissing, "Số định danh trên giấy tờ không hợp lệ.");
            }
        }

        // Evidence material for the session record: the OCR step is the identity-bearing evidence.
        var evidenceLog = steps["document"].Log;
        var evidenceDataBase64 = steps["document"].DataBase64;

        return new EvaluationResult(
            Success: true,
            FailureCode: null,
            FailureReason: null,
            OcrSnapshot: BuildOcrSnapshot(ocr.Value),
            LivenessVerdicts: BuildLivenessVerdicts(steps),
            CompareResult: hasCompare ? BuildCompareResultJson(steps["compare"].Payload) : null,
            TamperingVerdict: BuildTamperingJson(steps["document"].Payload),
            EvidenceDataSign: evidenceLog.ResponseDataSign,
            EvidencePayloadDigest: EkycCrypto.ComputePayloadDigest(evidenceDataBase64),
            EvidencePayloadFull: evidenceLog.ResponsePayloadEncrypted,
            IdentificationHmac: identificationHmac,
            OcrFullName: ocr.Value.Name,
            OcrBirthDay: ocr.Value.BirthDay);
    }

    // ------------------------------------------------------------------
    // Step classification + payload field extraction
    // ------------------------------------------------------------------

    /// <summary>
    /// Maps an endpoint path to its logical step — probe Q3 endpoint reality:
    /// <list type="bullet">
    ///   <item><c>document</c>: OCR CCCD — <c>ocr/id</c> (2 mặt) or <c>ocr/id/front</c> (1 mặt),
    ///         both carry the identity-bearing fields + tampering verdict.</item>
    ///   <item><c>card_liveness</c>: <c>card/liveness</c> — document anti-spoofing.</item>
    ///   <item><c>face_liveness</c>: <c>face/liveness</c> (incl. liveness-3d variants — path contains).</item>
    ///   <item><c>compare</c>: <c>face/compare</c> — face-vs-document similarity.</item>
    /// </list>
    /// </summary>
    internal static string? ClassifyStep(string endpointPath)
    {
        // Order matters: check compare/liveness BEFORE the OCR family so nothing overlaps.
        if (endpointPath.EndsWith(CompareSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return "compare";
        }

        if (endpointPath.Contains(FaceLivenessSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return "face_liveness";
        }

        if (endpointPath.Contains(CardLivenessSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return "card_liveness";
        }

        // OCR family: ocr/id, ocr/id/front — both are the document step (one call covers the document).
        if (endpointPath.Contains("ocr/id", StringComparison.OrdinalIgnoreCase))
        {
            return "document";
        }

        return null;
    }

    private static bool IsLivenessPass(JsonDocument payload)
    {
        // Shape (probe Q1): object.liveness. Accept "success"/"pass"/"passed"/"yes"/true.
        // STRICT: absence of a liveness verdict is NOT a pass — fail closed.
        if (TryGetObjectPayload(payload, out var obj)
            && obj.TryGetProperty("liveness", out var liveness))
        {
            return IsPositiveVerdict(liveness);
        }

        return false;
    }

    private static bool IsPositiveVerdict(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return string.Equals(value, "success", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "pass", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "passed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        return false;
    }

    private static bool IsTamperingClean(JsonDocument payload)
    {
        // Shape (probe Q1/Q6): object.tampering.is_legal == "yes"/true, OR object.is_legal.
        // Absence of a tampering verdict is NOT clean — fail closed.
        if (!TryGetObjectPayload(payload, out var obj))
        {
            return false;
        }

        if (obj.TryGetProperty("tampering", out var tampering) && tampering.ValueKind == JsonValueKind.Object)
        {
            if (tampering.TryGetProperty("is_legal", out var legal))
            {
                return IsPositiveVerdict(legal);
            }

            return false;
        }

        if (obj.TryGetProperty("is_legal", out var directLegal))
        {
            return IsPositiveVerdict(directLegal);
        }

        return false;
    }

    private static double? ExtractCompareProb(JsonDocument payload)
    {
        if (!TryGetObjectPayload(payload, out var obj))
        {
            return null;
        }

        if (!obj.TryGetProperty("prob", out var prob))
        {
            return null;
        }

        // VNPT emits prob as string ("92.45") or number.
        if (prob.ValueKind == JsonValueKind.Number && prob.TryGetDouble(out var num))
        {
            return num;
        }

        if (prob.ValueKind == JsonValueKind.String
            && double.TryParse(prob.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    internal readonly record struct OcrFields(
        string? IdNumber,
        string? Name,
        DateOnly? BirthDay,
        string? Gender,
        string? ValidDate);

    private static OcrFields? ExtractOcrFields(JsonDocument payload)
    {
        if (!TryGetObjectPayload(payload, out var obj))
        {
            return null;
        }

        var id = GetStringProp(obj, "id");
        var name = GetStringProp(obj, "name");
        var birthDayRaw = GetStringProp(obj, "birth_day");
        var gender = GetStringProp(obj, "gender");
        var validDate = GetStringProp(obj, "valid_date");

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new OcrFields(id, name, ParseVnptDate(birthDayRaw), gender, validDate);
    }

    /// <summary>VNPT emits dd/MM/yyyy (probe shape). Tolerant to ISO yyyy-MM-dd as fallback.</summary>
    internal static DateOnly? ParseVnptDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        string[] formats = ["dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy"];
        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(trimmed, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>
    /// FUZZY MATCHING RULES — documented business decision:
    /// Normalize Unicode NFC → strip Vietnamese diacritics → uppercase → collapse whitespace.
    /// Exact equality after normalization. Partial/fuzzy acceptance deliberately REJECTED:
    /// CCCD chip-printed names OCR accurately; fuzzy acceptance weakens composite-attack resistance.
    /// </summary>
    internal static bool NamesMatch(string? ocrName, string? registrationName)
    {
        if (string.IsNullOrWhiteSpace(ocrName) || string.IsNullOrWhiteSpace(registrationName))
        {
            return false;
        }

        return string.Equals(NormalizeName(ocrName), NormalizeName(registrationName), StringComparison.Ordinal);
    }

    internal static string NormalizeName(string name)
    {
        var nfc = name.Normalize(NormalizationForm.FormC).Trim();
        var builder = new StringBuilder(nfc.Length);
        var previousWasSpace = true; // leading-trim behavior
        foreach (var c in nfc)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            previousWasSpace = false;
            builder.Append(char.ToUpperInvariant(StripDiacritic(c)));
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Strip Vietnamese diacritics by NFD-decomposing and dropping non-spacing marks,
    /// plus the special đ/Đ letter which doesn't decompose.</summary>
    private static char StripDiacritic(char c)
    {
        if (c is 'đ')
        {
            return 'd';
        }

        if (c is 'Đ')
        {
            return 'D';
        }

        var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        foreach (var dc in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(dc)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                return dc;
            }
        }

        return c;
    }

    // ------------------------------------------------------------------
    // Snapshot builders (jsonb columns — NEVER images)
    // ------------------------------------------------------------------

    private static string BuildOcrSnapshot(OcrFields ocr)
        => JsonSerializer.Serialize(new
        {
            id_number = ocr.IdNumber,
            full_name = ocr.Name,
            birth_day = ocr.BirthDay?.ToString("yyyy-MM-dd"),
            gender = ocr.Gender,
            valid_date = ocr.ValidDate,
        });

    private static string BuildLivenessVerdicts(
        Dictionary<string, (EkycProxyCallLog Log, string DataBase64, JsonDocument Payload)> steps)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var key in new[] { "card_liveness", "face_liveness" })
            {
                writer.WritePropertyName(key);
                if (steps.TryGetValue(key, out var step) && TryGetObjectPayload(step.Payload, out var obj))
                {
                    writer.WriteStartObject();
                    if (obj.TryGetProperty("liveness", out var liveness))
                    {
                        writer.WriteString("liveness", liveness.ToString());
                    }

                    if (obj.TryGetProperty("liveness_msg", out var msg))
                    {
                        writer.WriteString("liveness_msg", msg.ToString());
                    }

                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteNullValue();
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildCompareResultJson(JsonDocument payload)
    {
        if (!TryGetObjectPayload(payload, out var obj))
        {
            return "{}";
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (obj.TryGetProperty("result", out var result))
            {
                writer.WriteString("result", result.ToString());
            }

            if (obj.TryGetProperty("msg", out var msg))
            {
                writer.WriteString("msg", msg.ToString());
            }

            if (obj.TryGetProperty("prob", out var prob))
            {
                writer.WriteString("prob", prob.ToString());
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildTamperingJson(JsonDocument payload)
    {
        if (!TryGetObjectPayload(payload, out var obj) || !obj.TryGetProperty("tampering", out var t))
        {
            return "{}";
        }

        return t.GetRawText();
    }

    private static bool TryGetObjectPayload(JsonDocument payload, out JsonElement obj)
    {
        // VNPT payload convention: business data under "object"; fall back to root for resilience.
        if (payload.RootElement.ValueKind == JsonValueKind.Object
            && payload.RootElement.TryGetProperty("object", out var inner)
            && inner.ValueKind == JsonValueKind.Object)
        {
            obj = inner;
            return true;
        }

        if (payload.RootElement.ValueKind == JsonValueKind.Object)
        {
            obj = payload.RootElement;
            return true;
        }

        obj = default;
        return false;
    }

    private static string? GetStringProp(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    // ------------------------------------------------------------------
    // Reject + audit helpers
    // ------------------------------------------------------------------

    private async Task RejectAsync(
        EkycVerificationSession session,
        long actorUserId,
        string failureCode,
        string failureReason,
        string? ipAddress,
        DateTime nowUtc,
        CancellationToken ct,
        bool expire)
    {
        session.Status = expire ? EkycVerificationStatuses.Expired : EkycVerificationStatuses.Rejected;
        session.FailureReason = $"{failureCode}: {failureReason}";
        session.UpdatedAt = nowUtc;

        _db.AuditLogs.Add(BuildAudit(actorUserId, session.Id, EkycConstants.AuditActionRejected,
            failureReason, ipAddress, failureCode));
        await Task.CompletedTask;
    }

    private AuditLog BuildAudit(
        long actorUserId,
        long sessionId,
        string action,
        string reason,
        string? ipAddress,
        string? failureCode,
        object? changeData = null)
    {
        return new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = EkycConstants.AuditEntityType,
            EntityId = sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = changeData is not null
                ? JsonSerializer.Serialize(changeData)
                : failureCode is null
                    ? null
                    : JsonSerializer.Serialize(new { failureCode }),
            IpAddress = System.Net.IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
            // audit_logs.occurred_at has no explicit EF type mapping → Npgsql timestamptz inference
            // requires Kind=Utc (AuditLogger convention), unlike the ekyc timestamp-without-time-zone columns.
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
    }

    /// <summary>
    /// The ONLY writer of <see cref="PatientIdentityStatuses.Verified"/>. Called exclusively from a
    /// successful finalize inside the locked transaction; never exposed through an API. The CCCD
    /// HMAC written here is the verified VNPT evidence, not a declaration.
    ///
    /// Returns null when the verified identification already belongs to a DIFFERENT patient row:
    /// the eKYC evidence is still true (session VERIFIED), but identity linkage must be resolved by
    /// reception (dedupe cases 3/4/5/6) — never by overwriting another profile or violating the
    /// unique identification index.
    /// </summary>
    private async Task<string?> TryMarkPatientVerifiedAsync(
        Patient patient,
        string? verifiedIdentificationHmac,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(verifiedIdentificationHmac))
        {
            var heldByAnother = await _db.Patients.AsNoTracking()
                .AnyAsync(p => p.IdentificationHashHmac == verifiedIdentificationHmac && p.Id != patient.Id, ct);
            if (heldByAnother)
            {
                return null;
            }

            patient.IdentificationHashHmac = verifiedIdentificationHmac;
            patient.HashVersion = 1;
        }

        patient.IdentityStatus = PatientIdentityStatuses.Verified;
        patient.UnverifiedReason = null;
        return patient.IdentityStatus;
    }

    private IOptions<EkycOptions> OptionsWrapper()
        => Microsoft.Extensions.Options.Options.Create(_options);

    private static EvaluationResult Fail(string code, string reason)
        => new(false, code, reason, null, null, null, null, null, null, null, null, null, null);
}
