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
///   <item>(e) ownership + non-terminal + within TTL + not consumed (single-use).</item>
///   <item>(b) step completeness: document front + back + face + compare present in the session's
///       signed evidence, document observed before compare (loose ordering otherwise).</item>
///   <item>(c) binding: session binding_state must be VALID (slice-2 sticky fail-closed marker).</item>
///   <item>(a) dataSign valid for EVERY captured step payload via PUBLIC_KEY_CA
///       (RSA-SHA256/PKCS1v15 over the verbatim dataBase64 string — probe Q2).</item>
///   <item>(d) policy verdict: liveness pass (card front/back/face), compare.prob ≥ configured
///       threshold (never hardcoded), tampering is_legal, OCR name+DOB fuzzy-match registration data.</item>
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

        // Ownership enforcement — never leak existence cross-account.
        if (session.UserId != actorUserId)
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

        // Registration data for OCR matching: the patient profile created at registration.
        // Absent patient (edge: pre-activation registration-scope flow, slice-4) → matching skipped
        // by null overrides, documented as a slice-4 tightening point.
        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == actorUserId, ct);

        var evaluation = EvaluateEvidence(
            session, logs, config, encryptionKey, hmacKey, actorUserId,
            expectedNameOverride: patient?.FullName,
            expectedDobOverride: patient?.DateOfBirth);

        EkycDedupeResolution? dedupe = null;
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

            _db.AuditLogs.Add(BuildAudit(actorUserId, session.Id, EkycConstants.AuditActionVerified,
                "Xác minh danh tính điện tử thành công.", ipAddress, null));

            // --- Activation gate + dedupe matrix (plan v2.1 mục 3.2/3.3, atomicity mục 3.8) -------
            // Runs INSIDE this transaction: consume + dedupe + activation commit or roll back
            // together. Only for pending-activation accounts under mandatory-registration whose
            // email is verified (the registration-scope token is only issued after OTP verify);
            // ACTIVE accounts (mature-account verification) skip activation entirely — NOT_APPLICABLE.
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == actorUserId, ct);
            if (user is not null &&
                user.AccountStatus == Domain.Enums.AccountStatus.PENDING &&
                user.EmailVerifiedAt is not null &&
                _options.MandatoryRegistration)
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
                    LinkedPatientId: null,
                    ReviewReason: null);
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
            ActivationReviewReason: dedupe?.ReviewReason);
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
        DateOnly? expectedDobOverride = null)
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
        // Probe-realistic model (Q3 endpoints): document OCR (ocr/id or ocr/id/front — both mặt
        // in one call), card liveness, face liveness, face compare. All four required.
        string[] requiredSteps = ["document", "card_liveness", "face_liveness", "compare"];
        foreach (var required in requiredSteps)
        {
            if (!steps.ContainsKey(required))
            {
                return Fail(FailMissingStep, $"Thiếu bước xác minh bắt buộc ({required}).");
            }
        }

        // Document-before-compare ordering: first compare capture must come after the document
        // capture. Loose otherwise (PM ruling — no general ordering enforcement).
        var firstCompareAt = steps["compare"].Log.CreatedAt;
        if (steps["document"].Log.CreatedAt > firstCompareAt)
        {
            return Fail(FailMissingStep, "Thứ tự bước xác minh không hợp lệ: giấy tờ phải trước so khớp khuôn mặt.");
        }

        // --- (c) Binding ------------------------------------------------------------------------
        if (!string.Equals(session.BindingState, EkycBindingStates.Valid, StringComparison.Ordinal))
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
        // Liveness: card front/back/face must all pass. VNPT payload shapes (probe Q1):
        // object.liveness == "success" (string) — anything else is a failure.
        foreach (var stepKey in new[] { "card_liveness", "face_liveness" })
        {
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

        // Compare prob ≥ configured threshold (never hardcoded).
        var compareProb = ExtractCompareProb(steps["compare"].Payload);
        if (compareProb is null || compareProb.Value < config.CompareThreshold)
        {
            return Fail(FailCompareBelowThreshold,
                "Độ tương đồng khuôn mặt với ảnh thẻ chưa đạt ngưỡng chấp nhận.");
        }

        // OCR fields — the front OCR payload carries identity fields.
        var ocr = ExtractOcrFields(steps["document"].Payload);
        if (ocr is null)
        {
            return Fail(FailEvidenceMissing, "Không trích xuất được thông tin định danh từ giấy tờ.");
        }

        // OCR-vs-registration fuzzy matching (rules documented at class level).
        if (expectedNameOverride is not null || expectedDobOverride is not null)
        {
            var nameMatches = expectedNameOverride is null
                || NamesMatch(ocr.Value.Name, expectedNameOverride);
            var dobMatches = expectedDobOverride is null
                || ocr.Value.BirthDay == expectedDobOverride.Value;

            if (!nameMatches || !dobMatches)
            {
                return Fail(FailOcrMismatch,
                    "Thông tin trên giấy tờ không khớp với thông tin đăng ký tài khoản (họ tên hoặc ngày sinh).");
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
            CompareResult: BuildCompareResultJson(steps["compare"].Payload),
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
        string? failureCode)
    {
        return new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = EkycConstants.AuditEntityType,
            EntityId = sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = failureCode is null ? null : JsonSerializer.Serialize(new { failureCode }),
            IpAddress = System.Net.IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
            // audit_logs.occurred_at has no explicit EF type mapping → Npgsql timestamptz inference
            // requires Kind=Utc (AuditLogger convention), unlike the ekyc timestamp-without-time-zone columns.
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
    }

    private IOptions<EkycOptions> OptionsWrapper()
        => Microsoft.Extensions.Options.Options.Create(_options);

    private static EvaluationResult Fail(string code, string reason)
        => new(false, code, reason, null, null, null, null, null, null, null, null, null, null);
}
