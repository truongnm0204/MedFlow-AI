#requires -Version 7.4
<#
.SYNOPSIS
  MedFlowAI task-harness entrypoint.
  Lightweight state runner for tasks/TASK-XXXX — supports resume/handoff.

.USAGE
  pwsh scripts/harness/harness.ps1 <verb> [-Task TASK-XXXX] [options]

  new         -Id TASK-0002 -Title "..." -Risk tiny|normal|high-risk -Type feature [-Goal "..."] [-Modules backend,frontend]
  status      [-Task TASK-XXXX]            task detail, or one-line list of all tasks
  verify      -Task TASK-XXXX              run module gates from .harness/config.json, write evidence to .harness/runs/<task>/
  resume      -Task TASK-XXXX              JSON: state, warnings, blockers, resumeFrom, nextAction
  finish      -Task TASK-XXXX              schema-validate + check blockers, walk state machine to done
  new-handoff -Task TASK-XXXX              render handoff.md from template (idempotent)

.EXIT CODES
  0 ok · 1 error · 3 verify failed · 5 finish blocked · 6 resume blockers
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('new', 'status', 'verify', 'resume', 'finish', 'new-handoff')]
    [string]$Verb,

    [Alias('Task')]
    [string]$TaskId,

    [string]$Id,
    [string]$Title,
    [string]$Goal,
    [ValidateSet('tiny', 'normal', 'high-risk')]
    [string]$Risk = 'normal',
    [ValidateSet('docs', 'feature', 'fix', 'refactor', 'bootstrap', 'harness', 'chore')]
    [string]$Type = 'feature',
    [string[]]$Modules = @(),
    [string]$Owner = 'claude'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # scripts/harness -> repo root
if (-not (Test-Path "$RepoRoot/.harness/config.json")) {
    $RepoRoot = Resolve-Path "$PSScriptRoot/../.."
}
Set-Location $RepoRoot

$Config = Get-Content "$RepoRoot/.harness/config.json" -Raw | ConvertFrom-Json
$TasksDir = "$RepoRoot/tasks"
$TaskSchemaPath = "$RepoRoot/.harness/schemas/task.schema.json"

# Task types that change source code — must declare at least one module so gates exist.
$SourceChangingTypes = @('feature', 'fix', 'refactor')

# ---------- helpers ----------

function Get-TaskPath([string]$id) { Join-Path $TasksDir "$id/task.json" }

function Read-Task([string]$id) {
    $p = Get-TaskPath $id
    if (-not (Test-Path $p)) { throw "Task not found: $id ($p)" }
    Get-Content $p -Raw | ConvertFrom-Json
}

function Write-Task([string]$id, $task) {
    $task.updatedAt = (Get-Date).ToString('o')
    $tmp = Get-TaskPath $id + ".tmp"
    $task | ConvertTo-Json -Depth 10 | Set-Content $tmp -Encoding utf8
    Move-Item -Force $tmp (Get-TaskPath $id)
}

function Test-TaskSchema([string]$id) {
    # Test-Json supports Draft 2020-12 via JsonSchema.NET on PowerShell >= 7.4.
    # Older runtimes lack the -Schema keyword entirely -> fail loudly, never skip.
    if (-not (Get-Command Test-Json -ErrorAction SilentlyContinue)) {
        throw "Test-Json unavailable — PowerShell >= 7.4 required for schema validation."
    }
    $errors = $null
    $ok = Test-Json -Json (Get-Content (Get-TaskPath $id) -Raw) `
        -Schema (Get-Content $TaskSchemaPath -Raw) -ErrorVariable errors
    return @{ valid = $ok; errors = @($errors) }
}

function Get-ContentFingerprint {
    # Deterministic snapshot of HEAD + working tree so finish can prove the
    # verified code is the code being completed. Re-verify after every commit.
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $acc = New-Object System.Text.StringBuilder

    $head = (git rev-parse HEAD 2>$null)
    [void]$acc.Append("HEAD:$head`n")

    # Unstaged + staged tracked diffs (binary-safe, path-stable ordering).
    foreach ($args in @(@('diff', '--binary'), @('diff', '--cached', '--binary'))) {
        $out = & git @args 2>$null
        [void]$acc.Append(($out -join "`n")).Append("`n")
    }

    # Untracked-but-not-ignored files: hash each file's content (paths sorted).
    $untracked = git ls-files --others --exclude-standard 2>$null | Sort-Object
    foreach ($f in $untracked) {
        $fp = Join-Path $RepoRoot $f
        if (Test-Path $fp -PathType Leaf) {
            $h = (Get-FileHash $fp -Algorithm SHA256).Hash
            [void]$acc.Append("UT:$f`:$h`n")
        }
    }

    # Harness config gates are part of what "passed" means.
    $cfg = (Get-FileHash "$RepoRoot/.harness/config.json" -Algorithm SHA256).Hash
    [void]$acc.Append("CFG:$cfg")

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($acc.ToString())
    return [Convert]::ToHexString($sha.ComputeHash($bytes)).ToLowerInvariant()
}

$script:LegalTransitions = @{
    'draft'        = @('ready')
    'ready'        = @('implementing')
    'implementing' = @('verifying', 'blocked')
    'verifying'    = @('reviewing', 'implementing', 'blocked')
    'reviewing'    = @('done', 'implementing', 'blocked')
    'blocked'      = @('ready', 'implementing', 'verifying', 'reviewing')
    'done'         = @()
}

function Set-TaskStatus($task, [string]$to) {
    $legal = $script:LegalTransitions[$task.status]
    if ($to -notin $legal) { throw "Illegal transition $($task.id): $($task.status) -> $to" }
    $task.status = $to
}

function Get-TaskGates($task) {
    $gates = @()
    foreach ($m in ($task.modules ?? @())) {
        $mod = $Config.modules.$m
        if (-not $mod) { throw "Unknown module '$m' in $($task.id)" }
        $gates += $mod.gates
    }
    return $gates
}

function Read-Evidence([string]$taskId) {
    $p = "$RepoRoot/.harness/runs/$taskId/evidence.json"
    if (-not (Test-Path $p)) { return $null }
    Get-Content $p -Raw | ConvertFrom-Json
}

# ---------- verbs ----------

function Invoke-New {
    if (-not $Id) { throw "new requires -Id TASK-XXXX" }
    if ($Id -notmatch '^TASK-[0-9]{4,}$') { throw "Id must match TASK-####" }
    if ($SourceChangingTypes -contains $Type -and $Modules.Count -eq 0) {
        throw "type '$Type' changes source — declare -Modules so required gates are non-empty"
    }
    $dir = Join-Path $TasksDir $Id
    if (Test-Path $dir) { Write-Output "$Id already exists at $dir"; return }
    New-Item -ItemType Directory -Path $dir | Out-Null

    $task = [ordered]@{
        schemaVersion      = 1
        id                 = $Id
        type               = $Type
        risk               = $Risk
        status             = 'draft'
        goal               = ($Goal ?? ($Title ?? ''))
        owner              = $Owner
        modules            = @($Modules)
        scope              = @()
        acceptanceCriteria = @(@{ id = 'AC-1'; text = ($Title ?? 'Define acceptance criterion') })
        evidenceRefs       = @()
        reviewRefs         = @()
        nextAction         = 'Write brief.md, define ACs, then set status to ready'
        createdAt          = (Get-Date).ToString('o')
        updatedAt          = (Get-Date).ToString('o')
    }
    if ($Risk -ne 'tiny') { $task.planPath = '' }   # fill before ready

    Write-Task $Id ([pscustomobject]$task)

    $brief = Get-Content "$RepoRoot/.harness/templates/task.md" -Raw
    $brief = $brief -replace '\{TASK-ID\}', $Id -replace '\{TITLE\}', ($Title ?? $Id)
    Set-Content (Join-Path $dir 'brief.md') $brief -Encoding utf8
    Write-Output "Created $Id (risk=$Risk) at tasks/$Id/"
}

function Invoke-Status {
    if ($TaskId) {
        Read-Task $TaskId | ConvertTo-Json -Depth 10
        return
    }
    Get-ChildItem $TasksDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'task.json') } |
        ForEach-Object {
            $t = Get-Content (Join-Path $_.FullName 'task.json') -Raw | ConvertFrom-Json
            "{0}  [{1,-12}] {2,-8} {3}" -f $t.id, $t.status, $t.risk, $t.goal
        }
}

function Invoke-Verify {
    $task = Read-Task $TaskId
    $gates = Get-TaskGates $task
    if ($gates.Count -eq 0) { Write-Output "No gates for $($task.id) (docs-only or no modules)"; return }

    $fpBefore = Get-ContentFingerprint

    $runDir = "$RepoRoot/.harness/runs/$TaskId"
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    $results = @(); $failed = 0

    foreach ($g in $gates) {
        $log = Join-Path $runDir "$($g.id).log"
        Write-Output "== gate $($g.id): $($g.executable) $($g.args -join ' ')  (cwd=$($g.cwd))"
        $proc = Start-Process -FilePath $g.executable -ArgumentList $g.args `
            -WorkingDirectory (Join-Path $RepoRoot $g.cwd) -NoNewWindow -Wait `
            -RedirectStandardOutput $log -RedirectStandardError "$log.err" -PassThru
        $ok = ($proc.ExitCode -eq 0)
        if (-not $ok) { $failed++ }
        $results += [ordered]@{
            gateId = $g.id; status = ($ok ? 'passed' : 'failed')
            exitCode = $proc.ExitCode; logPath = ".harness/runs/$TaskId/$($g.id).log"
        }
    }

    $fpAfter = Get-ContentFingerprint
    $fpStable = ($fpBefore -eq $fpAfter)

    # 'passed' requires all gates green AND the tree unchanged by the gates
    # (a gate that edits source makes the evidence describe different code).
    $ev = [ordered]@{
        taskId              = $TaskId
        at                  = (Get-Date).ToString('o')
        headSha             = (git rev-parse HEAD 2>$null)
        status              = (($failed -eq 0 -and $fpStable) ? 'passed' : 'failed')
        fingerprint         = $fpAfter
        fingerprintChanged  = (-not $fpStable)
        fingerprintBefore   = $fpBefore
        fingerprintAfter    = $fpAfter
        gates               = $results
    }
    $evPath = Join-Path $runDir 'evidence.json'
    $ev | ConvertTo-Json -Depth 6 | Set-Content $evPath -Encoding utf8

    if (-not $fpStable) {
        Write-Output "verify: fingerprint changed during gates — evidence marked failed; re-run verify."
    }
    if ($task.evidenceRefs -notcontains ".harness/runs/$TaskId/evidence.json") {
        $task.evidenceRefs += ".harness/runs/$TaskId/evidence.json"
        Write-Task $TaskId $task
    }
    Write-Output "verify: $failed failed gate(s). Evidence: .harness/runs/$TaskId/evidence.json"
    if ($failed -gt 0 -or -not $fpStable) { exit 3 }
}

function Get-ResumeInfo($task) {
    # Blockers: resume cannot proceed. Warnings: proceed but act on them.
    $warnings = @(); $blockers = @()

    if ($task.status -eq 'blocked' -and (-not $task.blockedReason -or -not $task.resumeFrom)) {
        $blockers += 'blocked without blockedReason/resumeFrom (schema violation)'
    }
    if ($task.status -ne 'draft' -and $task.status -ne 'done' -and
        $task.risk -ne 'tiny' -and
        [string]::IsNullOrEmpty($task.planPath)) {
        $blockers += 'missing planPath for non-tiny task past draft'
    }
    foreach ($e in ($task.evidenceRefs ?? @())) {
        if (-not (Test-Path (Join-Path $RepoRoot $e))) { $blockers += "missing evidence file: $e" }
    }

    # Stale evidence = warning (re-verify before finish), not a blocker.
    $ev = Read-Evidence $task.id
    if ($ev -and $ev.fingerprint -and $ev.fingerprint -ne (Get-ContentFingerprint)) {
        $warnings += 'evidence fingerprint stale — re-run verify before finish'
    } elseif ($ev -and -not $ev.fingerprint -and $ev.headSha -and
              $ev.headSha -ne (git rev-parse HEAD 2>$null)) {
        $warnings += 'evidence headSha stale (legacy evidence) — re-run verify before finish'
    }
    if ($task.status -notin @('draft', 'done') -and ($task.evidenceRefs ?? @()).Count -eq 0) {
        $warnings += 'no verify evidence yet — verification still owed'
    }

    return @{ warnings = $warnings; blockers = $blockers }
}

function Invoke-Resume {
    $task = Read-Task $TaskId
    $info = Get-ResumeInfo $task
    [pscustomobject]@{
        taskId      = $task.id
        status      = $task.status
        risk        = $task.risk
        goal        = $task.goal
        resumeFrom  = $task.resumeFrom
        nextAction  = $task.nextAction
        blockedReason = $task.blockedReason
        warnings    = $info.warnings
        blockers    = $info.blockers
        canContinue = ($info.blockers.Count -eq 0 -and $task.status -ne 'done')
    } | ConvertTo-Json -Depth 6
    if ($info.blockers.Count -gt 0) { exit 6 }
}

function Get-FinishBlockers($task) {
    $b = @()

    # Schema gate first — a malformed task.json must fail loudly.
    $sv = Test-TaskSchema $task.id
    if (-not $sv.valid) { $b += "task.json fails schema validation: $($sv.errors -join '; ')" }

    if ($task.status -eq 'done') { return $b }

    foreach ($ac in $task.acceptanceCriteria) {
        $ev = $task.acEvidence.($ac.id)
        if (-not $ev -or $ev.Count -eq 0) { $b += "AC $($ac.id) has no evidence" }
    }

    if ($SourceChangingTypes -contains $task.type -and ($task.modules ?? @()).Count -eq 0) {
        $b += "type '$($task.type)' changes source but declares no modules — required gates would be empty"
    }

    $requiredGates = Get-TaskGates $task
    if (($task.evidenceRefs ?? @()).Count -eq 0 -and $requiredGates.Count -gt 0) {
        $b += 'no verify evidence (.harness/runs/<task>/evidence.json)'
    } elseif ($requiredGates.Count -gt 0) {
        $ev = Read-Evidence $task.id
        if (-not $ev) {
            $b += 'evidence file missing on disk'
        } else {
            if ($ev.status -ne 'passed') { $b += "evidence status '$($ev.status)' (must be 'passed')" }
            if (-not $ev.fingerprint) {
                $b += 'evidence has no fingerprint (legacy) — re-run verify'
            } elseif ($ev.fingerprint -ne (Get-ContentFingerprint)) {
                $b += 'evidence fingerprint stale — re-run verify before finish'
            }
            # Every gate the task requires must appear and have passed.
            $runGates = @($ev.gates | ForEach-Object { $_.gateId })
            foreach ($g in $requiredGates) {
                if ($g.id -notin $runGates) { $b += "evidence missing required gate '$($g.id)'" }
                else {
                    $r = $ev.gates | Where-Object { $_.gateId -eq $g.id } | Select-Object -First 1
                    if ($r.status -ne 'passed') { $b += "gate '$($g.id)' status '$($r.status)'" }
                }
            }
        }
    }

    if ($Config.policy.independentReview -and ($task.reviewRefs ?? @()).Count -eq 0) {
        $b += 'no independent review recorded in reviewRefs'
    }
    if ($Config.policy.requireHandoff -and -not $task.handoffRef) {
        $b += 'no handoffRef — run new-handoff and fill handoff.md'
    }
    return $b
}

function Invoke-Finish {
    $task = Read-Task $TaskId
    try {
        $blockers = Get-FinishBlockers $task
    } catch {
        # Schema validation throws on malformed task.json — surface as blockers.
        $blockers = @("task.json fails schema validation: $_")
    }
    if ($blockers.Count -gt 0) {
        $blockers | ForEach-Object { Write-Output "BLOCKED: $_" }
        exit 5
    }
    while ($task.status -ne 'done') {
        switch ($task.status) {
            'verifying'    { Set-TaskStatus $task 'reviewing' }
            'reviewing'    { Set-TaskStatus $task 'done' }
            default        { throw "Cannot finish from status $($task.status)" }
        }
    }
    $task.nextAction = 'none — task complete'
    Write-Task $TaskId $task
    Write-Output "$TaskId -> done"
}

function Invoke-NewHandoff {
    $task = Read-Task $TaskId
    $p = Join-Path $TasksDir "$TaskId/handoff.md"
    if (Test-Path $p) { Write-Output "handoff.md already exists for $TaskId"; return }
    $h = Get-Content "$RepoRoot/.harness/templates/handoff.md" -Raw
    Set-Content $p ($h -replace '\{TASK-ID\}', $TaskId) -Encoding utf8
    if (-not $task.PSObject.Properties['handoffRef']) {
        $task | Add-Member -NotePropertyName handoffRef -NotePropertyValue "tasks/$TaskId/handoff.md"
        Write-Task $TaskId $task
    } elseif (-not $task.handoffRef) {
        $task.handoffRef = "tasks/$TaskId/handoff.md"
        Write-Task $TaskId $task
    }
    Write-Output "Created tasks/$TaskId/handoff.md — fill it before finish"
}

try {
    switch ($Verb) {
        'new'         { Invoke-New }
        'status'      { Invoke-Status }
        'verify'      { if (-not $TaskId) { throw 'verify requires -Task' }; Invoke-Verify }
        'resume'      { if (-not $TaskId) { throw 'resume requires -Task' }; Invoke-Resume }
        'finish'      { if (-not $TaskId) { throw 'finish requires -Task' }; Invoke-Finish }
        'new-handoff' { if (-not $TaskId) { throw 'new-handoff requires -Task' }; Invoke-NewHandoff }
    }
} catch {
    Write-Error $_
    exit 1
}
