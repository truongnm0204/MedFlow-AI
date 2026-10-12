# Handoff — TASK-0001

## Goal recap

Bootstrap MedFlowAI agent harness: `.harness/` + `scripts/harness/harness.ps1` + `tasks/` + workflow/coding docs.

## Current state

- Status: done
- Resume from: n/a

## What was done

- `.harness/config.json` — modules backend (dotnet build/test) + frontend (npm lint/test), policy maxRepairAttempts=3, independentReview, gitnexus=gkg endpoint `http://localhost:27495/mcp`.
- `.harness/schemas/task.schema.json` — state machine draft→ready→implementing→verifying→reviewing→done|blocked; blocked bat buoc blockedReason+resumeFrom; normal/high-risk bat buoc planPath.
- `.harness/templates/` — task.md, handoff.md, role-report.md.
- `scripts/harness/harness.ps1` — ~200 dong, verbs new/status/verify/resume/finish/new-handoff; atomic task.json write; gates chay theo config, evidence ghi `.harness/runs/<task>/`.
- `tasks/README.md` + seed `TASK-0001` (file nay la mau handoff).
- `docs/AGENT_WORKFLOW.md` + `docs/CODING_RULES.md` (local-only).
- `AGENTS.md` section 8 (task state & resume), `README.md` dong harness, `.gitignore` them `.harness/runs/`.

## Checks run

| Gate | Command | Exit | Result |
|------|---------|------|--------|
| runner-smoke | `pwsh scripts/harness/harness.ps1 status` | 0 | pass |

## Blockers / open issues

- `verify` gates chua chay that o bootstrap — can dotnet/node tren may chay; smoke-tested logic only.
- Backend manifest path gia dinh `medflow-ai-backend/MedFlow.sln` — kiem tra lai neu .sln ten khac.

## Next action

Mo `TASK-0002` cho feature/fix thuc te dau tien. Khi do: `new` → dien brief + task.json ACs → `ready` → implement → `verify` (gates that) → review → `new-handoff` → `finish`.

## Resume hint

```
pwsh scripts/harness/harness.ps1 status
```
