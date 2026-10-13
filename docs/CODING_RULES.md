# MedFlowAI Coding Rules

Distilled rulebook. Chi tiet + templates xem `docs/backend-code-standards.md` va
`docs/frontend-code-standards.md`. Rule cu the tung chu de xem `.claude/rules/`.

## 1. Non-negotiable

- **Backend authoritative** — moi business rule, authz, ownership check o backend. Frontend chi la UX layer; an link khong phai authorization.
- **`MedFlow.Application` chi dung EF Core qua `IApplicationDbContext`** — `DbSet` + async LINQ duoc phep; cam `MedFlowDbContext`/`new DbContext`/connection config truc tiep (phan do o Infrastructure).
- **Committed SQL migrations immutable** — thay doi schema = file moi `database/migrations/NNN_desc.sql`. Khong edit file da commit.
- **DTO-based contracts** — khong tra EF entity truc tiep ra API; khong nhan entity tu client.
- **Two-layer authz** — `[Authorize]` la layer 1; moi endpoint cham du lieu co owner can service-level ownership check (`.claude/rules/authorization-two-layers.md`).
- **Idempotent write ops** — cac endpoint duoc retry/register can unique key hoac idempotency guard; khong tin client-sent `userId`, `role`, `verified`.
- **Khong secrets trong repo** — conn strings, JWT secret, SMTP creds qua user-secrets/env.
- **File < ~200 dong** — split khi lon hon (KHONG tao file "enhanced" moi: update truc tiep file cu).

## 2. Naming & style

| Ngon ngu | Quy uoc |
|---|---|
| C# | PascalCase (types, methods, public members), camelCase (locals/params/private fields `_camelCase` cho fields) |
| TypeScript | PascalCase components/types, camelCase vars/functions, kebab-case file names |
| SQL | snake_case tables/columns, schema `medflow` |
| JSON/API | camelCase |
| Enums/const | UPPER_SNAKE |

Code bang English. Comment giai thich *tai sao*, khong nhac lai *cai gi*. Khong magic strings — dung const/enum/config.

## 3. Backend (.NET 10)

- Thin controllers: route → service → ProblemDetails. Khong business logic trong controller.
- Request/response DTOs tach biet; validate bang FluentValidation hoac DataAnnotations tuong duong codebase hien co.
- Constructor injection; domain logic trong Domain layer (entities co behavior methods).
- Typed domain exceptions → middleware map sang ProblemDetails. Message chong enumeration (`.claude/rules/api-contract-and-errors.md`).
- `IApplicationDbContext` bridge — query EF (DbSet + async LINQ) viet trong Application service; cam cham `MedFlowDbContext` truc tiep.
- Async all the way; `CancellationToken` truyen tu controller.
- Audit log moi thao tac nhay cam (login, OTP, role change, PHI access).

## 4. Database

- `database/medflow_schema_v1_2.sql` la source of truth schema `medflow`; migrations `database/migrations/NNN_desc.sql`.
- 1 EF Configuration file moi entity (`MedFlow.Infrastructure/Persistence/Configurations/`).
- UTC timestamps; soft-delete hoac unique constraint cho idempotency khi phu hop.
- Khong hard-delete du lieu co audit/evidence value (PHI).

## 5. Frontend (React 19 + Vite + TS)

- Feature-folder structure (`features/<name>/{components,hooks,api,types}` — xem frontend-code-standards §4).
- API call chi qua `apiClient` (axios interceptors) — khong goi axios/fetch truc tiep trong component.
- TanStack Query cho server state; Zustand cho client state; RHF + Zod cho forms.
- Loading/empty/error states cho moi data view.
- Khong magic strings trong role/route — dung constants.

## 6. API & auth

- Routes `/api/v1/...`; versioning giu nguyen contract cu khi breaking (version moi).
- JWT access + refresh rotation + OTP — flow xem `docs/backend-auth-architecture.md`.
- Error contract on nhat (ProblemDetails), status codes dung ngu nghia (401 vs 403 vs 404).
- Test authorization voi id cua user khac phai tra 403/404.

## 7. Testing

- Backend: `dotnet test` — unit cho domain/service, integration cho authz/idempotency.
- Frontend: `npm test` — component + hook tests.
- Bug fix = regression test kem theo.
- Assert DB row counts khi test write ops (khong chi response code).

## 8. Git & PR

- Conventional commits (`feat:`, `fix:`, `refactor:`, `test:`, `chore:`), khong AI references.
- 1 muc tieu moi commit; khong merge build do.
- Quality gates: backend `dotnet build` clean + `dotnet test`; frontend `npm run lint` + `npm test` + `tsc -b` clean.

## 9. Definition of Done

Code compiles + tests pass + ACs co evidence + review doc lap + docs updated (changelog/roadmap neu dang ke) + report/handoff ghi friction. Xem `AGENTS.md` §7 va `docs/AGENT_WORKFLOW.md` §7.
