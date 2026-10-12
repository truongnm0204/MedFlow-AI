# TASK-0001 — Bootstrap MedFlowAI agent harness

## Goal

MedFlowAI co co che resume/handoff co trang thai cho agent work: neu session bi gian doan, session sau doc `tasks/<id>/task.json` + `handoff.md` la tiep tuc duoc tu dung cho — thay vi doan lai tu plans/ roi rac.

## Acceptance criteria

| AC | Criterion | Evidence |
|----|-----------|----------|
| AC-1 | `.harness/` chua config.json, task schema, task/handoff/role-report templates | `.harness/` tree |
| AC-2 | `scripts/harness/harness.ps1` ho tro new/status/verify/resume/finish/new-handoff, chay tren pwsh 7 | smoke test output |
| AC-3 | `tasks/README.md` ghi quy uoc; TASK-0001 ton tai lam seed mau | `tasks/` tree |
| AC-4 | `docs/` van gitignored; `.harness/runs/` ignored; `tasks/` + `.harness/` tracked | `.gitignore`, `git status` |

## Scope

- Modules: none (repo tooling)
- Files/dirs: `.harness/`, `scripts/harness/`, `tasks/`, `docs/AGENT_WORKFLOW.md`, `docs/CODING_RULES.md`, `AGENTS.md`, `README.md`, `.gitignore`

## Constraints and risk

- Risk lane: normal
- Invariants: khong copy nguyen Dau_Viet runner (6 modules, fingerprint) — giu 1 file gon; khong SQLite/harness.db; `docs/` van local-only.
- Dependencies: khong
- Open decisions: khong

## Plan

`plans/` — harness adoption plan (261013 series).

## Verification mapping

| AC | Check | Expected result | Evidence |
|----|-------|-----------------|----------|
| AC-1 | `ls .harness` + `ls .harness/templates` | config + schema + 3 templates | `.harness/` |
| AC-2 | `pwsh scripts/harness/harness.ps1 status` | exit 0, in danh sach task | run log |
| AC-3 | `ls tasks` | README + TASK-0001 | `tasks/` |
| AC-4 | `git check-ignore docs/ .harness/runs/` va `git status` | docs + runs ignored; tasks + .harness staged | `.gitignore` |

## Handoff

Xem `handoff.md`.
