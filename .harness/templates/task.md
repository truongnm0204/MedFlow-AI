# {TASK-ID} — {TITLE}

## Goal

{The observable outcome — what must be true when this task is done.}

## Acceptance criteria

| AC | Criterion | Evidence |
|----|-----------|----------|
| AC-1 | {Verifiable statement — mirrored as id/text in task.json} | {filled after verification} |

## Scope

- Modules: {backend | frontend | none}
- Files/dirs:
  - {repo-relative path}

## Constraints and risk

- Risk lane: {tiny | normal | high-risk}
- Invariants that must not break: {e.g. IApplicationDbContext boundary, committed SQL migrations immutable, 2-layer authz}
- Dependencies: {TASK-XXXX or none}
- Open decisions: {ASSUMPTION/OPEN QUESTION flags or none}

## Plan

{Required for normal/high-risk. Link to `plans/<plan-dir>/plan.md` in the scope dir per AGENTS.md §2. Tiny tasks may work from this brief alone.}

## Verification mapping

| AC | Check | Expected result | Evidence |
|----|-------|-----------------|----------|
| AC-1 | {command or manual check} | {expected} | {path or note} |

## Handoff

Execution status lives ONLY in `task.json` — do not maintain a second state copy here.
On finish/block, fill `handoff.md` (run `pwsh scripts/harness/harness.ps1 new-handoff -Task {TASK-ID}`).
