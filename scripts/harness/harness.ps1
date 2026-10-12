#requires -Version 7.0
<#
.SYNOPSIS
  MedFlowAI task-harness entrypoint.
  Lightweight state runner for tasks/TASK-XXXX — supports resume/handoff.

.USAGE
  pwsh scripts/harness/harness.ps1 <verb> [-Task TASK-XXXX] [options]

  new         -Id TASK-0002 -Title "..." -Risk tiny|normal|high-risk -Type feature [-Goal "..."] [-Modules backend,frontend]
  status      [-Task TASK-XXXX]            task detail, or one-line list of all tasks
  verify      -Task TASK-XXXX              run module gates from .harness/config.json, write evidence to .harness/runs/<task>/
  resume      -Task TASK-XXXX              JSON: state, stale hints, resumeFrom, nextAction
  finish      -Task TASK-XXXX              validate ACs+review+handoff, walk state machine to done
  new-handoff -Task TASK-XXXX              render handoff.md from template (idempotent)

.EXIT CODES
  0 ok · 1 error · 3 verify failed · 5 finish blocked · 6 resume issues
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
    # fallback: script may sit at <root>/scripts/harness
    $RepoRoot = Resolve-Path "$PSScriptRoot/../.."
}
Set-Location $RepoRoot

$Config = Get-Content "$RepoRoot/.harness/config.json" -Raw | ConvertFrom-Json
$TasksDir = "$RepoRoot/tasks"

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

# ---------- verbs ----------

function Invoke-New {
    if (-not $Id) { throw "new requires -Id TASK-XXXX" }
    if ($Id -notmatch '^TASK-[0-9]{4,}$') { throw "Id must match TASK-####" }
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

    $ev = [ordered]@{
        taskId = $TaskId; at = (Get-Date).ToString('o')
        headSha = (git rev-parse HEAD 2>$null)
        status = ($failed -eq 0 ? 'passed' : 'failed'); gates = $results
    }
    $evPath = Join-Path $runDir 'evidence.json'
    $ev | ConvertTo-Json -Depth 6 | Set-Content $evPath -Encoding utf8

    if ($task.evidenceRefs -notcontains ".harness/runs/$TaskId/evidence.json") {
        $task.evidenceRefs += ".harness/runs/$TaskId/evidence.json"
        Write-Task $TaskId $task
    }
    Write-Output "verify: $failed failed gate(s). Evidence: .harness/runs/$TaskId/evidence.json"
    if ($failed -gt 0) { exit 3 }
}

function Invoke-Resume {
    $task = Read-Task $TaskId
    $issues = @()
    if ($task.status -eq 'blocked' -and (-not $task.blockedReason -or -not $task.resumeFrom)) {
        $issues += 'blocked without blockedReason/resumeFrom (schema violation)'
    }
    foreach ($e in ($task.evidenceRefs ?? @())) {
        if (-not (Test-Path (Join-Path $RepoRoot $e))) { $issues += "missing evidence: $e" }
    }
    if ($task.status -notin @('draft', 'done') -and ($task.evidenceRefs ?? @()).Count -eq 0) {
        $issues += 'no verify evidence yet — verification still owed'
    }
    [pscustomobject]@{
        taskId      = $task.id
        status      = $task.status
        risk        = $task.risk
        goal        = $task.goal
        resumeFrom  = $task.resumeFrom
        nextAction  = $task.nextAction
        blockers    = $task.blockedReason
        issues      = $issues
        canContinue = ($issues.Count -eq 0 -and $task.status -ne 'done')
    } | ConvertTo-Json -Depth 6
    if ($issues.Count -gt 0) { exit 6 }
}

function Get-FinishBlockers($task) {
    $b = @()
    if ($task.status -eq 'done') { return $b }
    foreach ($ac in $task.acceptanceCriteria) {
        $ev = $task.acEvidence.($ac.id)
        if (-not $ev -or $ev.Count -eq 0) { $b += "AC $($ac.id) has no evidence" }
    }
    if (($task.evidenceRefs ?? @()).Count -eq 0 -and ($task.modules ?? @()).Count -gt 0) {
        $b += 'no verify evidence (.harness/runs/<task>/evidence.json)'
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
    $blockers = Get-FinishBlockers $task
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
