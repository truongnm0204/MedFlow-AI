# tasks/ — MedFlowAI execution source of truth

Moi task normal/high-risk co mot thu muc `TASK-XXXX/` voi:

| File | Vai tro |
|---|---|
| `task.json` | Trang thai may moc (state machine) — duy nhat cho state. Xem `.harness/schemas/task.schema.json` |
| `brief.md` | Yeu cau, acceptance criteria, verification mapping (template: `.harness/templates/task.md`) |
| `handoff.md` | Ban giao khi done/blocked — tao bang `new-handoff` |

## Khi nao tao task

- **tiny** (0–1 flag, khong hard gate): khong can task — patch truc tiep theo `AGENTS.md` §1.
- **normal** (2–3 flags): tao task + plan file trong scope dir (`medflow-ai-backend/plans/`, `medflow-ai-frontend/plans/` hoac `plans/`).
- **high-risk** (4+ flags hoac hard gate): tao task + plan + co the can `docs/decisions/` ADR + human confirm.

## Runner

```powershell
pwsh scripts/harness/harness.ps1 new -Id TASK-0002 -Title "..." -Risk normal -Type feature -Modules backend
pwsh scripts/harness/harness.ps1 status              # liet ke tat ca
pwsh scripts/harness/harness.ps1 verify -Task TASK-0002   # chay gates theo config
pwsh scripts/harness/harness.ps1 resume -Task TASK-0002   # JSON resume state
pwsh scripts/harness/harness.ps1 new-handoff -Task TASK-0002
pwsh scripts/harness/harness.ps1 finish -Task TASK-0002
```

States: `draft → ready → implementing → verifying → reviewing → done`. `blocked` bat buoc `blockedReason` + `resumeFrom`.

## Nguyen tac

- **Khong duy tri 2 ban state** — `task.json` la duy nhat. brief/handoff la narrative, khong chua trang thai song.
- `tasks/` duoc **commit** — execution source of truth can share/review duoc.
- `.harness/runs/` (gate logs, evidence.json) la local-only, gitignored.
- GKG ho tro impact analysis truoc khi edit; khong quyet dinh "done" — done can AC evidence + review + handoff.
