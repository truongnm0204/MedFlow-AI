# Context Rules — Doc gi, khi nao, khi nao dung doc

Muc tieu: dua dung thong tin vao context cho task hien tai, khong doc thua. Sacrifice grammar for concision.

## Risk lanes

| Lane | Dinh nghia | Artifact bat buoc |
|---|---|---|
| **Tiny** | Docs/copy/names/narrow edit; setup khong cham domain schema/auth/provider | Intake note, patch, quick check |
| **Normal** | Story-sized behavior, blast radius gioi han | Plan file + phase files, validation |
| **High-risk** | Cham security, data, scope, contract, nhieu role/platform | Plan folder + decision record + human confirm |

**Hard gates** (auto high-risk): auth/authorization, DB migration/schema, security/PHI, external provider (SMTP email, payment), xoa/weaken validation.

## Must/Should/Skip reads theo phase x lane

### Intake

| | Tiny | Normal | High-risk |
|---|---|---|---|
| AGENTS.md | Must | Must | Must |
| docs/context-rules.md (file nay) | Skip | Must | Must |
| .claude/rules/* | Skip | Should | Must |
| Codebase structure docs | Skip | Should | Must |

### Planning

| | Tiny | Normal | High-risk |
|---|---|---|---|
| File se sua | Must | Must | Must |
| Plans lien quan (`plans/*/`, scope dir) | Skip | Must | Must |
| docs/code-standards (be hoac fe) | Skip | Must | Must |
| docs/system-architecture.md | Skip | Should | Must |
| docs/backend-auth-architecture.md | Skip | Only if auth | Must |
| docs/decisions/* | Skip | Should | Must |
| database/medflow_schema_v1_2.sql | Skip | Only if DB | Must |

### Implementation

| | Tiny | Normal | High-risk |
|---|---|---|---|
| File dang sua + truc tiep lien quan | Must | Must | Must |
| Tests hien co cua module | Skip | Must | Must |
| reports/ cua task tuong tu | Skip | Should | Must |

### Validation / Trace

| | Tiny | Normal | High-risk |
|---|---|---|---|
| `git status --short` | Must | Must | Must |
| Validation output (lint/test/build) | Must | Must | Must |
| Trace/report file | Skip | Must | Must |

## Retrieval triggers

Cham vung nao → doc them:

- **Auth/JWT/OTP/refresh** → `docs/backend-auth-architecture.md` + `.claude/rules/authorization-two-layers.md` + `.claude/rules/api-contract-and-errors.md`
- **DB/migration/entity** → `database/medflow_schema_v1_2.sql` + `.claude/rules/ef-migration-and-schema.md` + `docs/decisions/`
- **Frontend UI** → `docs/frontend-code-standards.md` + feature folder contract
- **External provider** (SMTP, payment, API ngoai) → hard gate, doc config + audit pattern
- **MCP tool tao file** → `.claude/rules/mcp-artifact-hygiene.md`
- **Final response** → re-read `git status --short` + validation evidence truoc khi bao done

## Token budget (goi y)

| Lane | Budget |
|---|---|
| Tiny | ~2K |
| Normal | ~5K |
| High-risk | ~10K |

Prefer targeted `Grep`/`find_symbol` over bulk reads. Doc section, khong doc full file khi chi can 1 method.
