# System Architecture — MedFlowAI

Kien truc he thong MedFlowAI: backend .NET 10 N-Layer, frontend React 19 SPA, PostgreSQL 18 schema `medflow`.

Lien quan:
- Code standards: `backend-code-standards.md`, `frontend-code-standards.md`.
- DB chi tiet: `database-auth-tables.md`.
- Plan gan nhat: `plans/261012-2037-authen-and-homepage/plan.md`.

## Tong quan

```
[Browser SPA React 19]
        | HTTPS/JSON (axios, JWT Bearer)
        v
[ASP.NET Core 10 API] --- MailKit/SMTP ---> [Gmail smtp.gmail.com:587]
        | EF Core 10 + Npgsql
        v
[PostgreSQL 18 @ localhost:5432]
   medflow_db (schema: medflow)
   medflow_test (integration tests)
```

## Backend — N-Layer

Cau truc 4 project, dependency mot chieu:

```
MedFlow.API            -> Controllers, Middleware, Program.cs (DI, CORS, rate limit, auth)
   | depends
MedFlow.Application    -> DTOs, Services (business logic), Interfaces (IApplicationDbContext, IEmailSender...)
   | depends
MedFlow.Infrastructure -> MedFlowDbContext, Configurations, Identity (Jwt/RefreshToken), Integrations (Smtp)
   | depends
MedFlow.Domain         -> Entities, Enums, Exceptions
```

Quy tac:
- Application khong reference Infrastructure truc tiep — giao tiep qua interfaces.
- DI wiring o `Infrastructure/DependencyInjection.cs` + `Application/DependencyInjection.cs`.
- Controllers chi nhan request + goi service; khong chua business logic.

## Auth flow

### Register + OTP verify

```
register -> user PENDING + patient + user_roles(PATIENT) + challenge PENDING (1 tx)
         -> SmtpEmailSender gui OTP 6 digits (TTL 5min, HMAC-SHA256 hash)
verify-otp -> challenge VERIFIED -> user ACTIVE + email_verified_at
```

### Login + refresh rotation (ADR-0048)

```
login   -> check ACTIVE + locked_until -> JWT access 60min + auth_sessions row (SHA-256 hash)
request -> 401 -> POST /refresh
refresh -> 1 DB transaction: lookup token_hash -> generate candidate in-memory
        -> conditional ExecuteUpdateAsync (RevokedAt == null && ExpiresAt > now) + insert new session
        -> neu updated=0 -> reuse -> RevokeAllForUserAsync + audit REFRESH_REUSE_DETECTED (khong tao orphan row)
logout  -> revoke token (idempotent)
```

### Brute force + rate limit

- `failed_login_attempts` + `locked_until`: 5 lan sai -> khoa 15 phut.
- Rate limit tach rieng theo endpoint:
  - Nhom Auth (phan vung theo IP client `RemoteIpAddress` sau `UseForwardedHeaders` voi `KnownProxies`/`KnownIPNetworks` tuong minh): `auth-login` (10/min/IP), `auth-register` (5/min/IP), `auth-refresh` (30/min/IP), `otp-verify` (10/min/IP), `otp-resend` (5/min/IP); resend throttle 45s/email.
  - Nhom nghiep vu nhay cam (chay sau `UseAuthentication` + `UseAuthorization`, phan vung theo `UserId` tu JWT `sub` khi da xac thuc hoac fallback IP client khi an danh, `QueueLimit = 0`, ADR-0057): `appointment-write` (10/min/UserId — dung chung giua dat/doi/huy/cap nhat ly do lich hen), `payment-qr-generate` (10/min/UserId), `ai-recommend` (15/min/UserId hoac IP).
  - Khi vuot han ngach tra `429 ProblemDetails` + header `Retry-After` (duoc expose qua CORS `WithExposedHeaders("Retry-After")`).
- HTTPS redirection tat trong `Development` va `Test` de giu nguyen `Authorization` header qua Vite HTTP proxy; bat buoc `UseHsts()` + `UseHttpsRedirection()` trong `Production`.
- Audit log cho moi su kien: `REGISTER`, `LOGIN_SUCCESS`, `LOGIN_FAILED`, `OTP_VERIFY`, `OTP_RESEND`, `LOGOUT`, `REFRESH_REUSE_DETECTED`, `EMAIL_SEND_FAILED`.

## Frontend — React 19

Feature-folder contract (moi feature co day du):

```
src/features/{feature}/
  api/         -> axios calls
  components/  -> UI components
  hooks/       -> custom hooks (use-auth, use-logout...)
  pages/       -> route-level pages
  schemas/     -> zod schemas
  store/       -> zustand stores
  types/       -> TypeScript types
  __tests__/   -> vitest tests
```

`src/shared/`:
- `ui/` — 12 primitives (button, input, card, popover, avatar, modal...) + `@theme` tokens.
- `lib/axios.ts` — apiClient + 401 interceptor.
- `components/layout/` — `app-header`, `public-navbar`, `patient-navbar`, `user-menu`, `patient-footer`.

### Axios 401 -> single-flight + Web Locks + error-classified refresh (ADR-0048)

```
request -> 401 -> interceptor (bo qua /auth/login|register|refresh|verify-otp|resend-otp|logout)
                | refreshPromise == null -> withCrossTabLock('medflow:auth:refresh')
                |   -> re-read localStorage: neu tab khac da rotate -> tai su dung token moi
                |   -> neu chua -> POST /auth/refresh (kiem tra sessionEpoch/userId chong stale response)
                | refreshPromise != null -> reuse promise dang chay trong tab
       <- refresh OK -> retry request goc voi access token moi (toi da 1 lan)
       <- refresh 401/403 hoac mat refresh token -> clearAuth + redirect /login?returnUrl=...
       <- refresh 429/5xx/timeout/network -> giu nguyen phien, ton trong Retry-After cooldown
```

State: Zustand `useAuthStore` persist `medflow.auth` kem `sessionEpoch` + dong bo da tab qua `storage` event va `BroadcastChannel` (khong ghi nguoc gay vong lap); tu dong `queryClient.clear()` dọn PHI cache khi tab khac logout.

## Database — schema `medflow`

PostgreSQL 18 `medflow_db`. Bang auth chinh:

| Bang | Vai tro |
|---|---|
| `users` | credentials, `account_status`, `failed_login_attempts`, `locked_until` |
| `roles` / `user_roles` | RBAC; role `PATIENT` tu gan khi register |
| `role_permissions` / `permissions` | fine-grain permissions |
| `patients` | profile patient 1-1 voi `users` |
| `auth_verification_challenges` | OTP challenges (HMAC-SHA256 hash, TTL, attempts) |
| `audit_logs` | audit trail `actor_user_id`, `action`, `ip_address`, `new_value` jsonb |
| `refresh_tokens` | refresh token hash (SHA-256), rotate chain `replaced_by_token_hash`, IP tracking |

`refresh_tokens` khong co trong DDL v1.2 -> tao bang manual SQL `database/migrations/001_create_refresh_tokens.sql`.

Test DB: `medflow_test` — cung DDL + migration; `TestWebAppFactory` set `ConnectionStrings__DefaultConnection` env var.

## Configuration & secrets

- `appsettings.Development.json` / `appsettings.Test.json` — khong chua secrets; `Password=DEV_LOCAL_ONLY` placeholder.
- Secrets that dat qua `dotnet user-secrets` (dev) hoac env var (prod/CI):
  - `ConnectionStrings:DefaultConnection`
  - `Auth:JwtSecret`
  - `Auth:OtpSecret`
  - `Smtp:Password` (Gmail App Password)
- Frontend: `vite.config.ts` proxy `/api` -> `VITE_API_PROXY_TARGET` (mac dinh `http://localhost:5168`, tuy chon HTTPS).

## Testing

- Backend unit: `tests/MedFlow.UnitTests` (xUnit) — OtpHasher, PasswordValidator, UserEntity.
- Backend integration: `tests/MedFlow.IntegrationTests` — `TestWebAppFactory` + `FakeEmailSender`; rate limiter TAT trong env `Test`.
- Frontend: Vitest + Testing Library — store, schemas, otp-input, guards, axios-refresh.
