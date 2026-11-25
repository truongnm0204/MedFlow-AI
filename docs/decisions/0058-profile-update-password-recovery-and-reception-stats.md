# ADR-0058: Personal Profile Update (`UC05`), OTP Password Recovery / Session-Bound Change Password & Reception Dashboard KPIs

- **Status**: Accepted
- **Date**: 2026-11-25
- **Issue**: `#836` (`#12`, `#25`, `#26`)

## 1. Context

Three production gaps remained across Patient Self-Service, Authentication, and the Reception Clinical Workbench:

1. **Personal Profile (`UC05`)**: `PatientProfilePage` only supported `GET /api/v1/patients/me/profile` and held contact edits in transient React state (`contactOverride`), losing changes on reload, while `patients` lacked dedicated emergency-contact columns.
2. **Password Recovery & Authenticated Password Change**: `LoginPage` rendered a dead `#` anchor for `"Quên mật khẩu?"`, no `/forgot-password` or `/api/v1/auth/{forgot-password,reset-password,change-password}` endpoints existed, and multi-device session revocation needed coordination with JWT session identity and `#831` SignalR realtime subscriptions.
3. **Reception Dashboard KPIs (`#12`)**: `ReceptionDashboardPage` hardcoded `waitingCheckIn: 0` and `completedToday: 0` and derived queue counts only from local table rows instead of hospital-scoped server state.

Parallel-branch governance (`#819` isolation) also prohibited modifying `DependencyInjection.cs` and `Program.cs`.

## 2. Decisions

### 2.1. Personal Profile Persistence, `OptionalField<T>` PATCH Semantics & Identity Locks

- **Schema (`022_patients_emergency_contact_columns.sql`)**: Added nullable `emergency_contact_name` (`VARCHAR(200)`), `emergency_contact_relationship` (`VARCHAR(60)`), and `emergency_contact_phone` (`VARCHAR(30)`) on `patients`. Storing emergency contact directly on `patients` guarantees **zero automatic `patient_family_links` creation** and **zero PHI/EMR/prescription access escalation**.
- **Omitted vs. Explicit `null` (`OptionalField<T>`)**: Introduced `OptionalField<T>` with a custom `System.Text.Json` converter (`HandleNull => true`). On `PATCH /api/v1/patients/me/profile`, omitted JSON properties (`IsSpecified == false`) preserve existing database values, whereas explicit `null` (`IsSpecified == true, Value == null`) clears nullable fields (`address`, `bloodType`, `emergencyContact`). `PUT /api/v1/patients/me/profile` applies full-replacement semantics for editable fields.
- **Legal Identity Locking**: `fullName` and `identification_hash` / `identityCard` are strictly read-only online. `dateOfBirth` and `gender` can be updated while `IdentificationHash` is null (`isIdentityVerified == false`), and become immutable once CCCD identity is verified.
- **Self-Reported Blood Type & Mandatory Audit**: `PatientProfileService` requires a non-null `IAuditLogger` in its constructor and records `PATIENT_PROFILE_UPDATED` inside the same database transaction, tagging self-reported blood type updates with `BloodTypeSource = "PATIENT_SELF_REPORTED"`. The UI labels patient-entered blood type as `(Tự khai báo)` so clinicians never mistake it for verified lab blood typing.

### 2.2. Anti-Enumeration Password Recovery & Session-Bound `change-password`

- **Uniform Anti-Enumeration (`POST /api/v1/auth/forgot-password`)**:
  - Returns HTTP `200` with identical body (`"Nếu email đã được đăng ký và đủ điều kiện, mã OTP khôi phục mật khẩu sẽ được gửi đến email của bạn."`) across eligible (`ACTIVE` + `LOCAL`), unknown, `PENDING`/`INACTIVE`/`LOCKED`, OAuth (`GOOGLE`), 45s-throttled, and OTP-locked accounts. Only IP rate limiting returns `429` (`otp-resend`: 5 req/min/IP).
  - Always executes upfront candidate OTP generation + `HMACSHA256` computation, enforces a `180–240ms` jittered response-time floor when `Smtp:Host` is non-local, and masks SMTP transport exceptions behind `EMAIL_SEND_FAILED` audit entries.
  - Acquires a PostgreSQL `FOR UPDATE` row lock on `users` inside the challenge creation transaction so concurrent requests cannot mint two valid `PASSWORD_RESET` OTPs, and carries over unverified failed `Attempts` across resends within the lockout window so resending cannot reset brute-force counters.
  - **Why `#824` `notifications` Outbox Was Not Used**: `notifications.body` stores plaintext in PostgreSQL, which would persist raw 6-digit password-reset OTP secrets at rest, and `ScheduleJobs:Enabled` is disabled in integration test hosts.
- **Single-Use Reset (`POST /api/v1/auth/reset-password`)**:
  - Rate-limited under `otp-verify` (10 req/min/IP). Failed OTP guesses increment `Attempts` (and transition to `LOCKED` at `MaxAttempts = 5`) and **commit immediately** before throwing `ValidationException`.
  - Valid OTP marks the challenge `VERIFIED` (single-use), updates `PasswordHash`, clears lockout counters, revokes **all** `auth_sessions`, writes `PASSWORD_RESET_COMPLETED` audit in one transaction, and evicts all SignalR subscriptions for the user.
- **Verified Session Retention on `POST /api/v1/auth/change-password`**:
  - `JwtTokenService` emits claim `"sid"` (`AuthSession.Id`) when issuing tokens in `LoginAsync` and `RefreshAsync`, and `ClinicalRealtimeHub` records `SessionId` per SignalR connection.
  - `AuthSessionService.RevokeOtherSessionsForUserAsync` retains `currentRefreshToken` **only** when the hashed token belongs to `actorUserId`, is unrevoked (`RevokedAt == null`), is unexpired (`ExpiresAt > nowUtc`), and matches the caller's JWT `"sid"` claim (when present). Missing, expired, revoked, cross-user, or `"sid"`-mismatched tokens revoke **all** sessions (`KeptCurrentSession = false`).
  - `AuthSessionService.RotateAsync` distinguishes replay of an **already-rotated** token (`ReplacedByTokenHash != null` → token-reuse family revocation + `REFRESH_REUSE_DETECTED`) from an **administratively revoked** session (`RevokedAt != null && ReplacedByTokenHash == null` from logout or password change → `401` without destroying the kept session on the password-changing device).
  - Wrong `currentPassword` increments `FailedLoginAttempts` (locking the account and revoking all sessions on the 5th failure) and commits `PASSWORD_CHANGE_FAILED` before returning HTTP `400`.
  - `ClinicalRealtimeConnectionRegistry.EvictUserSubscriptionsExceptSession` revokes SignalR group subscriptions on other devices while preserving the caller's active session connection.
  - Frontend `useChangePassword` awaits `getValidAccessTokenForRealtime()` before reading `refreshToken`, and `axios.ts` (`syncChangePasswordRetryRefreshToken`) updates `currentRefreshToken` if `/auth/change-password` retries after a `401` silent refresh.

### 2.3. Dual-Contract Reception Dashboard KPIs & Realtime Synchronization

- **Shared Calculation Core (`ComputeDashboardSummaryCoreAsync`)**: Powers both `GET /api/v1/reception/dashboard-summary` and `SearchPatientsResponseDto.DashboardSummary`, enforcing Two-Layer Authorization (`RECEPTION` / `HOSPITAL_ADMIN` / `SYSTEM_ADMIN` + reception permissions), `HospitalId` isolation, and `HospitalClock` (`UTC+7`) day boundaries.
- **Exact Clinical State Mapping**:
  - `WaitingCheckIn`: Today's scoped appointments in `BOOKED` or `CONFIRMED` status without a non-cancelled/non-no-show `Visit`.
  - `InQueue`: Today's scoped visits (`CheckedInAt` today) in `CHECKED_IN`, `WAITING`, `CALLED`, or `RETURN_CONSULTATION` status (including walk-ins).
  - `InConsultation`: Strictly `IN_PROGRESS` visits checked in today (**excluding** `AWAITING_RESULTS` diagnostic waits).
  - `CompletedToday`: `COMPLETED` visits whose `CompletedAt` falls within `[todayStart, todayEnd)` (**never** falling back to `CheckedInAt`).
- **SignalR & Resilient UI**: `AppointmentBookingService` and `AppointmentRescheduleService` publish `QueueChanged` when same-day appointments are booked or rescheduled. `ReceptionDashboardPage` subscribes to dedicated query `['reception', 'dashboard-summary']` and renders loading skeletons or `—` (never fake `0`s) when data is loading or unavailable.

## 3. Consequences & Trade-offs

- **Stateless JWT Natural Expiry Window**: Because `Program.cs` cannot be modified in parallel with `#819` to add per-request database session introspection middleware, stateless access tokens already issued to *other* devices remain cryptographically valid until their natural `exp` (up to 60 minutes). However, their `auth_sessions` refresh tokens and `#831` SignalR realtime subscriptions are revoked immediately upon password change or reset. If immediate sub-minute JWT revocation is required in a future milestone, `OnTokenValidated` can check `"sid"` against a distributed cache or `auth_sessions`.
- **Zero Secrets in Logs or Audit**: Raw passwords, OTP codes, refresh tokens, token hashes, and CCCD hashes are never written to `ILogger` or `audit_logs`.
