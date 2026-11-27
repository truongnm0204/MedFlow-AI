# Clinical Session Recovery Authority — API contract (PR #859 / F01 Commit A)

Route prefix follows repository conventions. All endpoints require `[Authorize]` and are restricted to
`RECEPTION`, `DOCTOR`, `HOSPITAL_ADMIN` at the route layer; the Application service re-validates live
permission, active staff/doctor, hospital scope, clinical scope, assignment and `StateVersion`.

| Operation | Route |
|---|---|
| GET context | `GET /api/v1/visits/{idOrCode}/clinical-session-recovery-context` |
| POST request | `POST /api/v1/visits/{idOrCode}/clinical-session-recovery-requests` |
| POST grant | `POST /api/v1/clinical-session-recovery-requests/{id}/grant` |
| POST reject | `POST /api/v1/clinical-session-recovery-requests/{id}/reject` |
| POST revoke | `POST /api/v1/clinical-session-recovery-requests/{id}/revoke` |
| POST cancel | `POST /api/v1/clinical-session-recovery-requests/{id}/cancel` |
| POST confirm | `POST /api/v1/clinical-session-recovery-requests/{id}/confirm` |
| GET list (PM-approved Commit B) | `GET /api/v1/clinical-session-recovery-requests` |
| GET eligible recipients (PM-approved Commit B) | `GET /api/v1/clinical-session-recovery-requests/{id}/eligible-recipients` |

## List and eligible recipients (Commit B additions)

- **Presence is explicit.** `POST .../return-enrollment` accepts `confirmPresent`; when the patient is not already
  present today, staff must send `true` (otherwise 409 `PATIENT_NOT_PRESENT` when policy requires presence).
  The legacy return-to-doctor request defaults `confirmPresent=false`. The legacy cross-day resume endpoint is the
  explicit Reception presence-confirmation action and delegates to this core.
- **List** is scoped, never uniform-404: Doctor sees REQUESTED requests inside their clinical scope plus
  requests authorized to them (confirm); Reception/HospitalAdmin see their hospital's requests; other role
  classes get 403. Minimal fields only (no reasons/EMR).
- **Eligible recipients** requires a doctor with `CLINICAL_SESSION_RECOVERY_GRANT` and request visibility;
  returns active doctors in the same hospital with `CLINICAL_SESSION_RECOVER` and matching
  department/specialty, excluding the caller. Out-of-scope caller → 404; in-scope but no GRANT → 403;
  non-REQUESTED request → 409.


## Error boundary (decision D6 — no existence oracle)

- **404 + uniform `ProblemDetails`** (`[CLINICAL_SESSION_RECOVERY_NOT_FOUND]`): resource does not exist,
  belongs to another hospital, or is outside the actor's machine-checkable clinical scope. Not-found and
  not-in-scope are indistinguishable; no PHI is returned.
- **403**: the resource is inside the actor's visibility (same hospital and clinical scope) but the actor
  lacks the permission/authority for the operation (Reception/HospitalAdmin/SystemAdmin cannot grant or
  confirm; doctor without `CLINICAL_SESSION_RECOVERY_GRANT` cannot grant; non-assigned doctor without a
  valid grant cannot confirm).
- **409**: authorization passed and the resource is in scope, but the operation conflicts with state:
  serving session absent, stale `StateVersion`/assignment, expired/revoked/consumed grant, terminal Visit,
  CLS/Pharmacy execution, or an unresolved recovery request already exists.
- **400**: invalid payload/reason/attestation (`ConfirmInterrupted` must be `true`).

## Authority rules

- `CLINICAL_SESSION_RECOVERY_GRANT`: active DOCTOR only, same hospital and clinical scope as the Visit.
- `CLINICAL_SESSION_RECOVER`: active assigned DOCTOR (own-session) or the delegated grant recipient.
- Issuer ≠ recipient. A grant binds `(HospitalId, VisitId, QueueEntryId, AuthorizedDoctorId)`, is
  single-use, expires 2 hours after server-side issuance, and is revocable. Clients never supply expiry.
- Recovery releases only the authorized EXAMINATION/RETURN_CONSULTATION serving ticket
  (`DEFERRED` + `CompletedAt` + `StateVersion + 1` + explicit recovery marker). No Visit finalization,
  no EMR/Dx/Rx/order/result/payment/presence mutation, no reassignment, no new ticket.
- Reasons are stored in `audit_logs.reason` only. They never appear in SignalR payloads or application
  log bodies; clients must not put patient identifiers in the reason text.
- Idempotency for confirm binds `(actor_user_id, operation, idempotency_key)` via a partial unique index;
  replaying the same key and payload returns the prior outcome with no extra mutation/audit, while a
  different payload is rejected with `RECOVERY_IDEMPOTENCY_MISMATCH`.

## Clinical scope predicate

Machine-checkable from existing data, fail closed:

1. Visit examination service (via appointment slot) `specialty_id`/`department_id`, otherwise the Visit
   examination room's `department_id`.
2. Doctor must match the required specialty and department; service authorizations, when present, must
   include the doctor.
3. If no mapping can be resolved for the Visit, no physician is in scope (no invented competency table).
