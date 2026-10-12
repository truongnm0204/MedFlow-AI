# Handoff — {TASK-ID}

## Goal recap

{One line: what this task set out to do.}

## Current state

- Status: {status from task.json}
- Resume from: {resumeFrom stage, if blocked}

## What was done

- {Change 1 — files touched}
- {Change 2}

## Checks run

| Gate | Command | Exit | Result |
|------|---------|------|--------|
| {gate id} | `{cmd}` | {code} | {pass/fail/stale + log path} |

## Blockers / open issues

- {None, or concrete blocker + who owns it}

## Next action

{Exact next step — mirrors `nextAction` in task.json. A new session should be able to resume from this alone.}

## Resume hint

```
pwsh scripts/harness/harness.ps1 resume -Task {TASK-ID}
```
