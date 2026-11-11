# MedFlowAI Agent Workflow

Quy trinh thuc thi cho coding agents trong repo nay. `AGENTS.md` la entry point;
file nay mo ta pipeline day du. Task state song duy nhat o `tasks/<id>/task.json` —
khong duy tri 2 ban trang thai doc lap.

## 1. Intake

Moi yeu cau di qua intake truoc:

1. Restate work item trong 1 dong.
2. Dem flags + check hard gates (`AGENTS.md` §1) → chon lane:

| Lane | Artifact | Runner |
|---|---|---|
| tiny | khong can task | patch truc tiep |
| normal | `tasks/TASK-XXXX/` + plan file trong scope dir | `harness.ps1 new` |
| high-risk | nhu normal + co the `docs/decisions/` ADR + human confirm | `harness.ps1 new` |

## 2. Plan

- Lane normal/high-risk: viet `plan.md` + `phase-XX-*.md` trong scope dir (`medflow-ai-backend/plans/`, `medflow-ai-frontend/plans/`, hoac `plans/` cho full-stack — theo `AGENTS.md` §2).
- `task.json.planPath` tro toi plan dir. `status: draft → ready` khi brief + ACs + plan san sang.
- Impact analysis truoc khi chot plan (GKG `get_references` hoac Explore fallback — `.claude/rules/impact-analysis-before-edit.md`).

## 3. Implement

- `status: ready → implementing`.
- Sequential mac dinh. Chi parallel khi: agents ghi disjoint files, khong co data dependency, da co merge plan.
- Mot writer moi file moi thoi diem.
- Code theo `docs/CODING_RULES.md` + `.claude/rules/`. Khi gap ambiguities: ghi `ASSUMPTION`/`OPEN QUESTION` trong brief, dung invent requirements.

## 4. Verify

- `status: implementing → verifying`.
- `pwsh scripts/harness/harness.ps1 verify -Task TASK-XXXX` chay module gates tu `.harness/config.json` (backend: dotnet build/test; frontend: npm lint/test). Evidence ghi `.harness/runs/<task>/evidence.json` (local-only).
- Test theo lane: normal can coverage path chinh + error case; high-risk can them security/idempotency/2-layer-authz test.
- Toi da 3 vong fix cung mot loi — sau do block voi root cause (`status: blocked` + `blockedReason` + `resumeFrom`).

## 5. Review

- `status: verifying → reviewing`.
- Reviewer phai doc lap voi implementer (code-reviewer agent). Neu khong co co che doc lap: bao han che, khong tu nhan "da review".
- Review doi chieu plan goc + ACs, khong chi lint. Ghi report vao `reviewRefs` (role-report template).

## 6. Document

- Cap nhat `docs/project-changelog.md` + `docs/development-roadmap.md` neu feature/fix dang ke.
- ADR vao `docs/decisions/NNNN-slug.md` khi co quyet dinh kien truc.
- AC evidence: moi AC trong `task.json.acEvidence` tro toi artifact that (test output, evidence.json, file path).

## 7. Handoff

- `new-handoff` → dien `tasks/<id>/handoff.md` (goal, done, checks, blockers, nextAction).
- `finish` → runner walk `verifying → reviewing → done`. Finish block khi: thieu AC evidence, thieu review, thieu handoffRef.
- Blocked giua chung: set `status=blocked`, dien `blockedReason` + `resumeFrom` + `nextAction` — session sau resume dung cho.

## Resume protocol

Session moi (hoac sau compact):

```powershell
pwsh scripts/harness/harness.ps1 status              # tim task dang mo
pwsh scripts/harness/harness.ps1 resume -Task TASK-XXXX
```

`resume` tra JSON: `status`, `resumeFrom`, `nextAction`, `issues` (missing evidence...).
`canContinue=true` → tiep tuc tu `resumeFrom`; `issues` khac rong → xu ly issue truoc.

## Quy tac cung

- Khong auto commit/push — commit thu cong theo conventional commits.
- Raw logs khong vao Git; `.harness/runs/` gitignored.
- Evidence summaries phai scrub secrets/PHI truoc khi viet.
- GKG ho tro impact; khong quyet dinh done — done can AC evidence + review doc lap + handoff.
