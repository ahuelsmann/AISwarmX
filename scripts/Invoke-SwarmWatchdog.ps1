<#
.SYNOPSIS
    Zero-token swarm hygiene, Kanban board health, and readiness watchdog (no LLM tokens consumed).
.DESCRIPTION
    Optionally heals stale Closed/Merged items on GitHub Project #1, invokes lossless repo hygiene,
    inspects live MOBAi head telemetry from the local Dashboard API (:5179) and MOBAflow open PRs/Kanban,
    and prints concrete actionable signals plus the exact kickoff prompt when capacity is ready.
#>
[CmdletBinding()]
param(
    [switch]$SkipHygiene,
    [switch]$AutoHealBoard,
    [switch]$Json,
    [switch]$RegisterScheduledTask,
    [switch]$UnregisterScheduledTask,
    [int]$IntervalMinutes = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$TaskName = "MOBAi-ZeroToken-HygieneWatchdog"
$ScriptPath = $MyInvocation.MyCommand.Path
$RepoDevRoot = Split-Path -Parent (Split-Path -Parent $ScriptPath)
$HygieneScriptPath = Join-Path $PSScriptRoot "Invoke-RepoHygiene.ps1"

if ($RegisterScheduledTask) {
    $argList = "-NoProfile -WindowStyle Hidden -File `"$ScriptPath`" -AutoHealBoard -Json"
    $action = New-ScheduledTaskAction -Execute "pwsh.exe" -Argument $argList -WorkingDirectory $RepoDevRoot
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description "Zero-token MOBAflow/MOBAdev hygiene and Kanban health check" -Force | Out-Null
    Write-Host "Registered Windows Scheduled Task '$TaskName' (every $IntervalMinutes minutes)."
    return
}

if ($UnregisterScheduledTask) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Unregistered Windows Scheduled Task '$TaskName'."
    return
}

# 1. Run repo hygiene unless skipped
$hygieneResult = $null
if (-not $SkipHygiene -and (Test-Path $HygieneScriptPath)) {
    try {
        $hygieneJson = & pwsh -NoProfile -File $HygieneScriptPath -Json
        if (-not [string]::IsNullOrWhiteSpace($hygieneJson)) {
            $hygieneResult = ($hygieneJson -join "`n") | ConvertFrom-Json
        }
    }
    catch { }
}

# 2. Query local Dashboard :5179 monitors
$monitorsOnline = $false
$monitorsSummary = @()
$claudeRateLimited = $false
try {
    $monResp = Invoke-RestMethod -Uri "http://127.0.0.1:5179/api/monitors?refresh=true" -TimeoutSec 5 -ErrorAction Stop
    $monitorsOnline = $true
    foreach ($m in @($monResp.monitors)) {
        if ($m.displayName -eq "MOBAi Claude Opus" -and $m.state -eq "rate_limited") {
            $claudeRateLimited = $true
        }
        $monitorsSummary += [pscustomobject]@{
            DisplayName         = $m.displayName
            State               = $m.state
            Model               = $m.model
            RateLimitResetLabel = $m.rateLimitResetLabel
        }
    }
}
catch { }

# 3. Query MOBAflow Open PRs
$openPrs = @()
try {
    $prJson = & gh pr list --repo ahuelsmann/MOBAflow --state open --limit 50 --json number,title,isDraft,mergeStateStatus,statusCheckRollup,body 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($prJson)) {
        $openPrs = @($prJson | ConvertFrom-Json)
    }
}
catch { }

$openCodePrCount = 0
$fastPathCandidates = [System.Collections.Generic.List[object]]::new()

foreach ($pr in $openPrs) {
    $bodyText = if ($pr.body) { "$($pr.body)" } else { "" }
    $titleText = if ($pr.title) { "$($pr.title)" } else { "" }
    $isLowDocs = ($bodyText -match '(?im)Risk class[^\r\n]*:\s*L\b') -or
                 ($titleText -match '^(docs|spec|chore)(\([^)]*\))?:' -and $bodyText -notmatch '(?im)Risk class[^\r\n]*:\s*[HM]\b')
    if (-not $isLowDocs) {
        $openCodePrCount++
    }

    $checks = @($pr.statusCheckRollup)
    $allGreen = $checks.Count -gt 0
    foreach ($c in $checks) {
        $status = if ($c.status) { "$($c.status)" } else { "" }
        $conclusion = if ($c.conclusion) { "$($c.conclusion)" } else { "" }
        if ($status -and $status -ne "COMPLETED") { $allGreen = $false; break }
        if ($conclusion -notin @("SUCCESS", "NEUTRAL", "SKIPPED")) { $allGreen = $false; break }
    }

    if ($allGreen -and ($pr.mergeStateStatus -eq "BEHIND" -or $pr.isDraft)) {
        $fastPathCandidates.Add([pscustomobject]@{
            Number           = $pr.number
            Title            = $pr.title
            IsDraft          = [bool]$pr.isDraft
            MergeStateStatus = $pr.mergeStateStatus
        })
    }
}

# 4. Query MOBAflow Kanban Project #1 (PVT_kwHOAPwF2s4BZ8_z)
$projectId = "PVT_kwHOAPwF2s4BZ8_z"
$statusFieldId = "PVTSSF_lAHOAPwF2s4BZ8_zzhU4AOM"
$doneOptionId = "98236657"

$boardItems = @()
$autoHealedItems = [System.Collections.Generic.List[object]]::new()
$graphQuery = 'query($owner:String!,$num:Int!){user(login:$owner){projectV2(number:$num){items(first:100,query:"-status:Done -status:Planned -status:Inbox"){nodes{id fieldValues(first:20){nodes{... on ProjectV2ItemFieldSingleSelectValue{name field{... on ProjectV2SingleSelectField{name}}}}}content{__typename ... on Issue{number title state} ... on PullRequest{number title state}}}}}}}'

try {
    $gqlRaw = & gh api graphql -f owner=ahuelsmann -F num=1 -f "query=$graphQuery" 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gqlRaw)) {
        $gqlObj = ($gqlRaw -join "`n") | ConvertFrom-Json
        $nodes = @($gqlObj.data.user.projectV2.items.nodes)
        foreach ($node in $nodes) {
            if ($null -eq $node -or $null -eq $node.content) { continue }
            $kanbanStatus = $null
            $priority = $null
            if ($null -ne $node.fieldValues -and $null -ne $node.fieldValues.nodes) {
                foreach ($fv in @($node.fieldValues.nodes)) {
                    if ($null -eq $fv) { continue }
                    $fieldProp = $fv.PSObject.Properties["field"]
                    $nameProp = $fv.PSObject.Properties["name"]
                    if ($null -eq $fieldProp -or $null -eq $fieldProp.Value -or $null -eq $nameProp) { continue }
                    $fieldNameProp = $fieldProp.Value.PSObject.Properties["name"]
                    if ($null -eq $fieldNameProp) { continue }
                    if ($fieldNameProp.Value -eq "Status") { $kanbanStatus = "$($nameProp.Value)" }
                    elseif ($fieldNameProp.Value -eq "Priority") { $priority = "$($nameProp.Value)" }
                }
            }

            $ghState = "$($node.content.state)"
            $kind = "$($node.content.__typename)"
            $num = [int]$node.content.number
            $title = "$($node.content.title)"

            if ($AutoHealBoard -and (($kind -eq "Issue" -and $ghState -eq "CLOSED") -or ($kind -eq "PullRequest" -and $ghState -in @("MERGED", "CLOSED")))) {
                & gh project item-edit --project-id $projectId --id $node.id --field-id $statusFieldId --single-select-option-id $doneOptionId 2>$null | Out-Null
                if ($LASTEXITCODE -eq 0) {
                    $autoHealedItems.Add([pscustomobject]@{ Kind = $kind; Number = $num; PreviousStatus = $kanbanStatus; Title = $title })
                    continue
                }
            }

            $boardItems += [pscustomobject]@{
                ItemId       = $node.id
                Kind         = $kind
                Number       = $num
                Title        = $title
                GitHubState  = $ghState
                KanbanStatus = $kanbanStatus
                Priority     = $priority
            }
        }
    }
}
catch {
    Write-Warning "Watchdog GraphQL error: $_"
}

$readyIssues = @($boardItems | Where-Object { $_.Kind -eq "Issue" -and $_.GitHubState -eq "OPEN" -and $_.KanbanStatus -eq "Ready" })
$rf30OpenInProgress = @($boardItems | Where-Object { $_.Kind -eq "Issue" -and $_.Number -eq 452 -and $_.GitHubState -eq "OPEN" -and $_.KanbanStatus -eq "In progress" }).Count -gt 0

$quietWindowOpenForRf30 = ($openCodePrCount -eq 0) -and $rf30OpenInProgress
$readyIntakeAvailable = ($openCodePrCount -lt 3) -and ($readyIssues.Count -gt 0)
$readyColumnOverloaded = $readyIssues.Count -gt 3

$recommendedOwner = if ($claudeRateLimited) {
    "MOBAi ChatGPT Codex + MOBAi Antigravity Gemini (Claude Code in Quota Pause -> 2/3 Quorum Failover)"
}
else {
    "MOBAi Claude Opus (COO)"
}

$recommendedPrompt = if ($quietWindowOpenForRf30) {
    "Das Quiet Window (0 offene PRs) ist offen: bitte führe die verbliebenen Code-Slices von #452 (RF-30 Test-Projekt-Split & Root-Namespaces) im Disjoint-Lock-Workflow bis zum Merge auf main durch."
}
elseif ($fastPathCandidates.Count -gt 0) {
    $prNums = ($fastPathCandidates | ForEach-Object { "#$($_.Number)" }) -join ", "
    "Prüfe die grünen PR-Kandidaten ($prNums) auf Disjoint main-Sync Fast-Path / Ready-Promotion und Auto-Merge."
}
elseif ($readyIntakeAvailable) {
    "Prüfe die Ready-Spalte im MOBAflow GitHub Kanban Board und starte das nächste berechtigte Issue gemäß AGENTS.md, master-coordination.instructions.md und MOBAdev/manifest.md."
}
else {
    "Keine Aktion erforderlich — Code WIP ausgelastet oder keine offenen Ready-Issues."
}

$report = [pscustomobject]@{
    GeneratedAtUtc            = [DateTimeOffset]::UtcNow.ToString("O")
    ZeroTokensUsed            = $true
    DashboardOnline           = $monitorsOnline
    Monitors                  = @($monitorsSummary)
    Hygiene                   = $hygieneResult
    OpenPrCount               = $openPrs.Count
    OpenCodePrCount           = $openCodePrCount
    ReadyIssuesCount          = $readyIssues.Count
    ReadyIssues               = @($readyIssues | Select-Object Number, Priority, Title)
    AutoHealedBoardItemsCount = $autoHealedItems.Count
    AutoHealedBoardItems      = @($autoHealedItems)
    Signals                   = [pscustomobject]@{
        QuietWindowOpenForRf30     = $quietWindowOpenForRf30
        FastPathOrReviewCandidates = @($fastPathCandidates)
        ReadyIntakeAvailable       = $readyIntakeAvailable
        ReadyColumnOverloaded      = $readyColumnOverloaded
    }
    RecommendedOwner          = $recommendedOwner
    RecommendedPrompt         = $recommendedPrompt
}

if ($Json) {
    $report | ConvertTo-Json -Depth 6
}
else {
    Write-Host "=== MOBAi Zero-Token Swarm Watchdog ==="
    Write-Host "Open PRs: $($report.OpenPrCount) (Code WIP: $($report.OpenCodePrCount) / 3) | Ready Issues: $($report.ReadyIssuesCount) (target 2-3) | Auto-healed board items: $($report.AutoHealedBoardItemsCount)"
    if ($hygieneResult) {
        Write-Host "Repo Hygiene -> Worktrees removed: $($hygieneResult.RemovedWorktreesCount) | Preserved dirty: $($hygieneResult.PreservedDirtyCount) | Local branches deleted: $($hygieneResult.DeletedBranchesCount)"
    }
    if ($monitorsOnline) {
        Write-Host "Monitors (:5179):"
        $monitorsSummary | ForEach-Object {
            $suffix = if ($_.RateLimitResetLabel) { " [$($_.RateLimitResetLabel)]" } else { "" }
            Write-Host "  - $($_.DisplayName): $($_.State)$suffix ($($_.Model))"
        }
    }
    Write-Host "Signals -> QuietWindowOpenForRf30=$($report.Signals.QuietWindowOpenForRf30) | ReadyIntakeAvailable=$($report.Signals.ReadyIntakeAvailable) | ReadyOverloaded=$($report.Signals.ReadyColumnOverloaded) | FastPathCandidates=$($fastPathCandidates.Count)"
    Write-Host "Next Recommended Owner : $($report.RecommendedOwner)"
    Write-Host "Next Kickoff Prompt    : $($report.RecommendedPrompt)"
}
