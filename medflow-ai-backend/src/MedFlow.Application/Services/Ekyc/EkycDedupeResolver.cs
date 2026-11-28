using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// Dedupe matrix resolution (owner 3.1, plan v2.1 mục 3.3) — runs INSIDE the finalize transaction
/// so session consume + dedupe + activation are atomic (v2.1 mục 3.8).
///
/// The identification_hmac proven by the verified eKYC evidence is compared against
/// <c>patients.identification_hash_hmac</c>. Six cases, each with an explicit outcome — NONE of
/// them auto-merges medical data:
///
/// <list type="number">
///   <item><b>Chưa có hồ sơ</b> — no patient carries the hmac: the registration-created patient
///         record IS the correct one; write the verified identification onto it. ACTIVATE.</item>
///   <item><b>Đã khám chưa tài khoản</b> — exactly one patient carries the hmac with user_id NULL:
///         claim link. The registration shell is empty by construction (created minutes ago, zero
///         visits) — unlink + void it (audit trail preserved) and point the user at the existing
///         record. NOT a merge: no medical data moves. ACTIVATE + CLAIM_EKYC_MATCHED audit.</item>
///   <item><b>Đã có người thân quản lý</b> — the matched patient is a VERIFIED family-link member:
///         eKYC proves the registrant is the person, never that they may claim a managed record.
///         ACTIVATE with the registration shell; linkage PENDING reception review.</item>
///   <item><b>CCCD đã gắn tài khoản khác</b> — matched patient belongs to a DIFFERENT user:
///         activation REFUSED, account stays PENDING, user directed to account recovery.</item>
///   <item><b>Trùng CCCD không khớp info</b> — hmac matches but full-name/DOB disagree: manual
///         review. ACTIVATE with registration shell; linkage PENDING.</item>
///   <item><b>Nhiều hồ sơ cùng định danh</b> — 2+ rows carry the hmac: data conflict, NEVER
///         auto-merge. ACTIVATE with registration shell; linkage PENDING reception resolution.</item>
/// </list>
///
/// Review-required outcomes (3/5/6) activate the account — the person IS verified — but write the
/// review marker into the session failure_reason-adjacent audit detail; reception resolution is a
/// Phase-2/3 workflow, deliberately out of scope here.
/// </summary>
public static class EkycDedupeResolver
{
    public const string AuditActionAccountActivated = "ACCOUNT_ACTIVATED";
    public const string AuditActionActivationRefused = "ACCOUNT_ACTIVATION_REFUSED";
    public const string AuditActionDedupeReviewRequired = "EKYC_DEDUPE_REVIEW_REQUIRED";
    public const string PatientStatusVoidedDuplicate = "VOID_DUPLICATE";

    /// <summary>
    /// Resolves the dedupe matrix for a finalize-verified session. MUST be called inside the
    /// finalize transaction, AFTER the session row is locked — caller guarantees atomicity.
    /// Pure decision + mutation on already-loaded entities where possible; queries are read-only.
    /// </summary>
    /// <param name="db">The ambient application db context (same transaction as finalize).</param>
    /// <param name="user">The session owner — tracked, mutable.</param>
    /// <param name="registrationPatient">The patient record created at registration (user-linked), tracked.</param>
    /// <param name="session">The verified session — patient_id linkage is written here.</param>
    /// <param name="identificationHmac">Verified identification HMAC; null means no id extracted.</param>
    /// <param name="ocrFullName">OCR full name (normalized comparison input).</param>
    /// <param name="ocrBirthDay">OCR date of birth.</param>
    /// <param name="actorUserId">Auditing actor.</param>
    /// <param name="ipAddress">Auditing ip.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<EkycDedupeResolution> ResolveAsync(
        IApplicationDbContext db,
        User user,
        Patient? registrationPatient,
        EkycVerificationSession session,
        string? identificationHmac,
        string? ocrFullName,
        DateOnly? ocrBirthDay,
        long actorUserId,
        string? ipAddress,
        CancellationToken ct)
    {
        // No verified identification → the activation question cannot depend on dedupe:
        // treat as case 1 (identity proven by eKYC even without a parseable id — activation is
        // about the PERSON; patient identification simply isn't written).
        var candidates = new List<Patient>();
        if (!string.IsNullOrWhiteSpace(identificationHmac))
        {
            candidates = await db.Patients
                .Where(p => p.IdentificationHashHmac == identificationHmac)
                .ToListAsync(ct);
        }

        // Exclude the registration shell itself — it never counts as a "pre-existing" record.
        if (registrationPatient is not null)
        {
            candidates = candidates.Where(p => p.Id != registrationPatient.Id).ToList();
        }

        switch (candidates.Count)
        {
            case 0:
                return ResolveCase1(user, registrationPatient, session, identificationHmac, actorUserId, ipAddress, db);

            case 1:
                return await ResolveSingleMatchAsync(
                    db, user, registrationPatient, session, candidates[0],
                    identificationHmac, ocrFullName, ocrBirthDay, actorUserId, ipAddress, ct);

            default:
                return ResolveCase6(user, registrationPatient, session, candidates, actorUserId, ipAddress, db);
        }
    }

    // ---------------------------------------------------------------
    // Case 1 — chưa có hồ sơ: registration shell is the correct record.
    // ---------------------------------------------------------------
    private static EkycDedupeResolution ResolveCase1(
        User user,
        Patient? registrationPatient,
        EkycVerificationSession session,
        string? identificationHmac,
        long actorUserId,
        string? ipAddress,
        IApplicationDbContext db)
    {
        if (registrationPatient is not null && !string.IsNullOrWhiteSpace(identificationHmac))
        {
            registrationPatient.IdentificationHashHmac = identificationHmac;
            registrationPatient.HashVersion = 1;
        }

        if (registrationPatient is not null)
        {
            session.PatientId = registrationPatient.Id;
        }

        Activate(user, session, actorUserId, ipAddress, db,
            new { outcome = EkycDedupeOutcomes.NewIdentityActivated, patientId = registrationPatient?.Id });

        return new EkycDedupeResolution(
            EkycDedupeOutcomes.NewIdentityActivated,
            AccountActivated: true,
            LinkedPatientId: registrationPatient?.Id,
            ReviewReason: null);
    }

    // ---------------------------------------------------------------
    // Single pre-existing match — cases 2/3/4/5 by ownership + info agreement.
    // ---------------------------------------------------------------
    private static async Task<EkycDedupeResolution> ResolveSingleMatchAsync(
        IApplicationDbContext db,
        User user,
        Patient? registrationPatient,
        EkycVerificationSession session,
        Patient matched,
        string? identificationHmac,
        string? ocrFullName,
        DateOnly? ocrBirthDay,
        long actorUserId,
        string? ipAddress,
        CancellationToken ct)
    {
        // Case 4 — identification already bound to a DIFFERENT account: hard refusal.
        if (matched.UserId is not null && matched.UserId != user.Id)
        {
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                Action = AuditActionActivationRefused,
                EntityType = EkycConstants.AuditEntityType,
                EntityId = session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ChangeData = JsonSerializer.Serialize(new
                {
                    outcome = EkycDedupeOutcomes.ConflictAccountExists,
                    reason = "identification_bound_to_other_account",
                }),
                IpAddress = ParseIp(ipAddress),
                OccurredAt = DateTime.UtcNow,
            });

            return new EkycDedupeResolution(
                EkycDedupeOutcomes.ConflictAccountExists,
                AccountActivated: false,
                LinkedPatientId: null,
                ReviewReason: "Số định danh này đã gắn với một tài khoản khác. Vui lòng dùng chức năng khôi phục tài khoản hoặc liên hệ quầy lễ tân.");
        }

        // Same-user re-verification edge (mature account re-running eKYC): treat as case 1 linkage.
        if (matched.UserId == user.Id)
        {
            session.PatientId = matched.Id;
            Activate(user, session, actorUserId, ipAddress, db,
                new { outcome = EkycDedupeOutcomes.NewIdentityActivated, patientId = matched.Id, selfMatch = true });
            return new EkycDedupeResolution(
                EkycDedupeOutcomes.NewIdentityActivated,
                AccountActivated: true,
                LinkedPatientId: matched.Id,
                ReviewReason: null);
        }

        // matched.UserId is null below — the record exists without an account.

        // Case 3 — family-managed record: verified link where this patient is the MEMBER.
        var familyManaged = await db.PatientFamilyLinks
            .AnyAsync(l => l.MemberPatientId == matched.Id &&
                           l.VerificationStatus == "VERIFIED" &&
                           l.RevokedAt == null, ct);
        if (familyManaged)
        {
            ActivateWithReview(user, registrationPatient, session, actorUserId, ipAddress, db,
                EkycDedupeOutcomes.FamilyManagedPendingReview,
                "Hồ sơ y tế với định danh này đang được quản lý bởi người thân. Việc liên kết quyền sở hữu cần quầy lễ tân xác minh.");
            return new EkycDedupeResolution(
                EkycDedupeOutcomes.FamilyManagedPendingReview,
                AccountActivated: true,
                LinkedPatientId: registrationPatient?.Id,
                ReviewReason: "Hồ sơ y tế với định danh này đang được quản lý bởi người thân — chờ quầy lễ tân xác minh quyền sở hữu.");
        }

        // Case 5 — hmac matches but name/DOB disagree: manual review.
        var nameMatches = ocrFullName is null || NamesMatchLoose(matched.FullName, ocrFullName);
        var dobMatches = ocrBirthDay is null || matched.DateOfBirth is null || matched.DateOfBirth == ocrBirthDay;
        if (!nameMatches || !dobMatches)
        {
            ActivateWithReview(user, registrationPatient, session, actorUserId, ipAddress, db,
                EkycDedupeOutcomes.InfoMismatchPendingReview,
                "Thông tin định danh trùng khớp nhưng họ tên hoặc ngày sinh trên hồ sơ hiện có không khớp — cần xem xét thủ công.");
            return new EkycDedupeResolution(
                EkycDedupeOutcomes.InfoMismatchPendingReview,
                AccountActivated: true,
                LinkedPatientId: registrationPatient?.Id,
                ReviewReason: "Định danh trùng hồ sơ hiện có nhưng thông tin cá nhân không khớp — chờ quầy lễ tân xem xét.");
        }

        // Case 2 — clean claim link: record exists, no account, no family manager, info agrees.
        // The registration shell is empty by construction: unlink + void (audit trail), re-point
        // the user at the pre-existing record. NOTHING medical moves — not a merge.
        if (registrationPatient is not null)
        {
            registrationPatient.UserId = null;
            registrationPatient.Status = PatientStatusVoidedDuplicate;
        }

        matched.UserId = user.Id;
        if (string.IsNullOrWhiteSpace(matched.IdentificationHashHmac) &&
            !string.IsNullOrWhiteSpace(identificationHmac))
        {
            matched.IdentificationHashHmac = identificationHmac;
            matched.HashVersion = 1;
        }

        session.PatientId = matched.Id;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = EkycConstants.AuditActionClaimEkycMatched,
            EntityType = EkycConstants.AuditEntityType,
            EntityId = session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ChangeData = JsonSerializer.Serialize(new
            {
                outcome = EkycDedupeOutcomes.ClaimLinkedActivated,
                claimedPatientId = matched.Id,
                voidedShellPatientId = registrationPatient?.Id,
            }),
            IpAddress = ParseIp(ipAddress),
            OccurredAt = DateTime.UtcNow,
        });

        Activate(user, session, actorUserId, ipAddress, db,
            new { outcome = EkycDedupeOutcomes.ClaimLinkedActivated, patientId = matched.Id });

        return new EkycDedupeResolution(
            EkycDedupeOutcomes.ClaimLinkedActivated,
            AccountActivated: true,
            LinkedPatientId: matched.Id,
            ReviewReason: null);
    }

    // ---------------------------------------------------------------
    // Case 6 — multiple records share the identification: conflict, NEVER auto-merge.
    // ---------------------------------------------------------------
    private static EkycDedupeResolution ResolveCase6(
        User user,
        Patient? registrationPatient,
        EkycVerificationSession session,
        List<Patient> candidates,
        long actorUserId,
        string? ipAddress,
        IApplicationDbContext db)
    {
        // If ANY candidate is bound to a different account, the conflict is even harder — still
        // review, not refusal: activation proceeds but linkage is impossible until data is fixed.
        ActivateWithReview(user, registrationPatient, session, actorUserId, ipAddress, db,
            EkycDedupeOutcomes.MultipleRecordsConflict,
            $"Phát hiện {candidates.Count} hồ sơ cùng số định danh — xung đột dữ liệu, cần quầy lễ tân xử lý thủ công. Không tự động gộp.");

        return new EkycDedupeResolution(
            EkycDedupeOutcomes.MultipleRecordsConflict,
            AccountActivated: true,
            LinkedPatientId: registrationPatient?.Id,
            ReviewReason: $"Có {candidates.Count} hồ sơ trùng số định danh — chờ quầy lễ tân xử lý xung đột.");
    }

    // ---------------------------------------------------------------
    // Activation helpers
    // ---------------------------------------------------------------

    private static void Activate(
        User user,
        EkycVerificationSession session,
        long actorUserId,
        string? ipAddress,
        IApplicationDbContext db,
        object auditDetail)
    {
        user.AccountStatus = Domain.Enums.AccountStatus.ACTIVE;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = AuditActionAccountActivated,
            EntityType = EkycConstants.AuditEntityType,
            EntityId = session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ChangeData = JsonSerializer.Serialize(auditDetail),
            IpAddress = ParseIp(ipAddress),
            OccurredAt = DateTime.UtcNow,
        });
    }

    private static void ActivateWithReview(
        User user,
        Patient? registrationPatient,
        EkycVerificationSession session,
        long actorUserId,
        string? ipAddress,
        IApplicationDbContext db,
        string outcome,
        string reviewReason)
    {
        if (registrationPatient is not null)
        {
            session.PatientId = registrationPatient.Id;
        }

        user.AccountStatus = Domain.Enums.AccountStatus.ACTIVE;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = AuditActionDedupeReviewRequired,
            EntityType = EkycConstants.AuditEntityType,
            EntityId = session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ChangeData = JsonSerializer.Serialize(new { outcome, reviewReason }),
            IpAddress = ParseIp(ipAddress),
            OccurredAt = DateTime.UtcNow,
        });

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = AuditActionAccountActivated,
            EntityType = EkycConstants.AuditEntityType,
            EntityId = session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ChangeData = JsonSerializer.Serialize(new { outcome, pendingReview = true }),
            IpAddress = ParseIp(ipAddress),
            OccurredAt = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// Loose agreement check for case-5 detection: exact equality after uppercasing + whitespace
    /// collapse (no diacritic stripping — the comparison is patient-record-vs-OCR where both are
    /// Vietnamese; disagreement detection intentionally conservative).
    /// </summary>
    private static bool NamesMatchLoose(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return true; // missing data never triggers mismatch — absence is not disagreement
        }

        static string Normalize(string s) =>
            string.Join(' ', s.Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
    }

    private static System.Net.IPAddress? ParseIp(string? ip)
        => System.Net.IPAddress.TryParse(ip, out var parsed) ? parsed : null;
}
