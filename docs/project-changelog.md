# Project Changelog

Format: `YYYY-MM-DD — <title>` per entry. Moi entry ghi du: scope, thay doi chinh, fix bao mat, test status, deferred items.

## 2026-10-12 — Patient Authentication + Homepage (MVP)

### Scope

- Plan: `plans/261012-2037-authen-and-homepage/plan.md` — 6 phases (UI kit → backend → auth API → FE auth → homepage → tests).
- Review: `plans/reports/code-review-2026-10-12-auth-homepage.md` — verdict APPROVE sau khi fix 6 major + 5 minor.

### Added

- **Auth endpoints** `api/v1/auth/*`: `register`, `verify-otp`, `resend-otp`, `login`, `refresh`, `logout`, `me`.
- **Backend** (.NET 10, N-Layer):
  - Entities + EF Core configurations: `User`, `Role`, `UserRole`, `Patient`, `RefreshToken`, `AuthVerificationChallenge`, `AuditLog`, `Permission`, `RolePermission` — schema `medflow`.
  - `RefreshTokenService`: opaque 64-byte token, SHA-256 hash only in DB, rotation + reuse detection → revoke family (`ExecuteUpdateAsync` atomic).
  - `JwtTokenService`: access token 60 min, claims `sub`/`email`/`jti`/`username` + roles.
  - `SmtpEmailSender` (MailKit): Gmail SMTP STARTTLS; credentials qua `Smtp:Password` user-secrets.
  - `AuthOtpService`: OTP 6 digits HMAC-SHA256, TTL 5 min, max 5 attempts, resend throttle 45 s.
  - Brute-force login lock: 5 failed → 15 min; audit `LOGIN_FAILED`/`LOGIN_SUCCESS`.
  - Rate limit: `auth` 5/min, `otp` 10/min; `ExceptionMiddleware` ProblemDetails.
- **Frontend** (React 19 + Vite 8 + Tailwind v4):
  - `useAuthStore` persist `medflow.auth` + sync `medflow.access_token`/`medflow.refresh_token`.
  - `RequireAuth`/`GuestOnly` route guards.
  - Axios 401 → single-flight refresh → retry; fallback clear + redirect login.
  - Pages: `login`, `register` (tach `register-account-section` + `register-identity-section`), `verify-otp` (OtpInput 6 o, countdown, resend).
  - Homepage don gian: 7 sections (hero, AI triage, specialties, doctors, journey, guarantee, workbench preview modal); `AppHeader` switch Public/Patient.
- **DB**: `database/migrations/001_create_refresh_tokens.sql` (uuid PK, `token_hash` unique, FK cascade, partial index active).
- **Tests**: backend 25 unit + 25 integration (`TestWebAppFactory` + `FakeEmailSender`); frontend 35 tests.

### Changed / Fixed (post review)

- `AuthService` 328 → 256 dong; extract `AuthOtpService`, `AuthCryptoHelpers`, `OtpEmailBuilder`.
- `register-page.tsx` 226 → 125 dong; extract 2 section components.
- `RotateAsync` atomic: `ExecuteUpdateAsync` `WHERE Id = @id AND RevokedAt IS NULL` → `updated == 0` = reuse.
- `IPAddress.TryParse` thay `Parse` trong audit (tranh 500 khi `X-Forwarded-For` malformed).
- `VerifyOtpAsync`/`ResendOtpAsync` chan `AccountStatus.ACTIVE` (validation error).
- `LoginAsync` `.Include(u => u.Patient)` — bo round-trip thu 2.
- `useLogout` doc `refreshToken` trong `mutationFn` (tranh stale token).
- `appsettings.Test.json` bo ConnectionStrings → `TestWebAppFactory` doc `MEDFLOW_TEST_DB` env hoac default local.
- `appsettings.Development.json` `Password=123` → `Password=DEV_LOCAL_ONLY` + comment huong dan `dotnet user-secrets`.
- Bo duplicate unique index `patients.user_id` (one-to-one tu tao).
- `BeginTransactionAsync` bo await thua.
- Package updates: `JwtBearer` 10.0.0→10.0.12, `MailKit` 4.15.0→4.18.1, `Microsoft.AspNetCore.Identity` 2.3.1 → `Microsoft.Extensions.Identity.Core` 10.0.12 (bo `System.Security.Cryptography.Xml` 8.0.2 NU1903), explicit `Microsoft.EntityFrameworkCore.Relational` 10.0.12 (fix MSB3277).

### Test status

- Backend: 50/50 pass (25 unit + 25 integration), 0 warnings.
- Frontend: 35/35 pass + `npm run build` clean.
- Tong: 85 tests.

### Security notes

- Gmail SMTP app-password chi qua `dotnet user-secrets` (`Smtp:Password`) hoac env var — KHONG commit.
- `medflow_test` connection qua env `MEDFLOW_TEST_DB` hoac default local `postgres/123` (dev-only, khong phai credential that).
- `appsettings.Development.json` chi chua placeholder `DEV_LOCAL_ONLY`.

### Deferred / known issues

- `Patient.UpdatedAt`/`User.UpdatedAt` chua co trigger update (giu nguyen `CreatedAt`) — plan khong yeu cau.
- README setup cho dev moi: can muc `dotnet user-secrets set "Smtp:Password"` + `ConnectionStrings:DefaultConnection`.
- `OtpInput` da co `inputMode="numeric"`; chua test tren mobile that.
