# AGENTS.md — MedFlowAI

Project-level operating rules for coding agents working in this repo.
Stack: `.NET 10` backend (`medflow-ai-backend/`) + `React 19 + Vite + TS` frontend (`medflow-ai-frontend/`) + PostgreSQL.
Docs root: `docs/` — read `docs/context-rules.md` FIRST to know what to read for the current task.

## 1. Intake — classify every task before coding

Before any implementation, classify the task into a risk lane. Restate the work item in one line, count flags, pick lane.

**Flags (1 point each):** touches >3 files · >1 layer · new endpoint/route · new entity/DTO · new dependency · changes public contract · touches >1 feature folder.

**Hard gates (auto high-risk regardless of count):**
- Auth / authorization (JWT, OTP, refresh, roles, `[Authorize]`, ownership checks)
- DB schema / migration (`database/`, EF Configuration, new table/column)
- Security / PHI / PII / audit log handling
- External provider behavior (SMTP email, payment, third-party API)
- Removing or weakening any validation / test

| Flags | Lane | Requirements |
|---|---|---|
| 0–1, no hard gate | **tiny** | Patch directly, run quick check, update docs if needed |
| 2–3, no hard gate | **normal** | Plan file + phase file in scope dir, validation run, report |
| 4+ or any hard gate | **high-risk** | Plan folder, decision record in `docs/decisions/`, human confirm if ambiguous |

## 2. Plan placement by scope

| Task scope | Plan dir | Reports dir |
|---|---|---|
| Backend only | `medflow-ai-backend/plans/` | `medflow-ai-backend/plans/reports/` |
| Frontend only | `medflow-ai-frontend/plans/` | `medflow-ai-frontend/plans/reports/` |
| Full-stack / repo / docs / infra | `plans/` | `plans/reports/` |

Plan dir naming: `yymmdd-hhmm-{slug}/` containing `plan.md` + `phase-XX-*.md` + optional `assets/`.

## 3. Mandatory reads (by lane — details in `docs/context-rules.md`)

- **tiny**: `AGENTS.md` + the file being edited. ~2K tokens.
- **normal**: `AGENTS.md` + `docs/context-rules.md` + relevant code-standards doc + files being changed. ~5K.
- **high-risk**: everything above + `docs/system-architecture.md` + `docs/backend-auth-architecture.md` (if auth) + `database/medflow_schema_v1_2.sql` (if DB) + `docs/decisions/*` + `.claude/rules/*`. ~10K.

Retrieval triggers: auth → auth-architecture doc + rules; DB → schema SQL + ef-migration rule; MCP file output → artifact-hygiene rule.

## 4. Hard rules (non-negotiable)

- **Impact before edit**: before modifying a public method/class with multiple callers (e.g. `IAuthService`, `IApplicationDbContext`, domain behaviors), trace references first (GKG query if indexed, else Explore agent `find_referencing_symbols`). HIGH/CRITICAL blast radius → warn user before proceeding.
- **Two-layer authz**: `[Authorize]` is layer 1 only. Every endpoint touching owned data needs a service-level ownership check (`.claude/rules/authorization-two-layers.md`).
- **Never edit committed SQL migrations** — new file `database/migrations/NNN_desc.sql` (`.claude/rules/ef-migration-and-schema.md`).
- **`MedFlow.Application` never references EF Core** — only `IApplicationDbContext`.
- **No secrets in repo** — conn strings, JWT secret, SMTP creds via user-secrets/env only.
- **MCP artifacts**: all tool-generated files go to `.mcp-artifacts/<tool>/` (gitignored). Promote to `docs/assets/` or `plans/<plan>/assets/` only when durable (`.claude/rules/mcp-artifact-hygiene.md`).

## 5. Quality gates

Backend: `dotnet build` clean, `dotnet test` pass before push.
Frontend: `npm run lint` before commit, `npm run test` before push, `tsc -b` clean.
Files < ~200 lines; split when growing (already enforced in code-standards docs).

## 6. Done definition

A task is done when: code compiles + tests pass + relevant docs updated (changelog/roadmap) + report file written to scope `plans/reports/` noting files changed, outcome, friction encountered.
