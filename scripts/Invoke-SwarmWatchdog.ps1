<#
.SYNOPSIS
    Zero-token swarm hygiene, Kanban board health, and event-driven watchdog (no LLM tokens consumed).
.DESCRIPTION
    Optionally heals stale Closed/Merged items on GitHub Project #1, invokes lossless repo hygiene,
    inspects live MOBAi head telemetry from the local Dashboard API (:5179) and MOBAflow open PRs/Kanban,
    dispatches deduplicated CI/Sonar failure or all-green Ready events (to Codex CLI queue and local alert log),
    and prints concrete actionable signals plus the exact kickoff prompt when capacity is ready.
#>
[CmdletBinding()]
param(
    [switch]$SkipHygiene,
    [switch]$AutoHealBoard,
    [switch]$DispatchEvents,
    [switch]$SyncQueue,
    [switch]$ResetEventState,
    [string]$CodexThreadId = "01a14504-bd6c-7a50-b715-1a2f2ec76467",
    [string]$DashboardApiUrl = "http://127.0.0.1:5179",
    [switch]$Json,
    [switch]$RegisterScheduledTask,
    [switch]$UnregisterScheduledTask,
    [int]$IntervalMinutes = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$TaskName = "MOBAi-ZeroToken-HygieneWatchdog"
$ScriptPath = $MyInvocation.MyCommand.Path
$RepoDevRoot = Split-Path -Parent (Split-Path -Parent $ScriptPath)
$HygieneScriptPath = Join-Path $PSScriptRoot "Invoke-RepoHygiene.ps1"
$LocalAppRoot = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { Join-Path $env:USERPROFILE "AppData\Local" } else { $env:LOCALAPPDATA }
$StateDir = Join-Path $LocalAppRoot "MOBAdev"
$StatePath = Join-Path $StateDir "watchdog-state.json"
$AlertLogPath = Join-Path $StateDir "watchdog-alerts.log"
$effectiveSyncQueue = [bool]($SyncQueue -or $DispatchEvents)

function Get-TargetOwnerFromBranch {
    param([string]$BranchName)
    if ($BranchName -and $BranchName.StartsWith("codex/", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Owner = "Codex"; BusId = "codex" }
    }
    if ($BranchName -and $BranchName.StartsWith("antigravity/", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Owner = "Antigravity"; BusId = "antigravity-gemini" }
    }
    if ($BranchName -and ($BranchName.StartsWith("claude/", [System.StringComparison]::OrdinalIgnoreCase) -or $BranchName.StartsWith("opus/", [System.StringComparison]::OrdinalIgnoreCase))) {
        return [pscustomobject]@{ Owner = "Claude"; BusId = "claude-opus" }
    }
    return [pscustomobject]@{ Owner = "Claude"; BusId = "claude-opus" }
}

if ($RegisterScheduledTask) {
    $argList = "-NoProfile -WindowStyle Hidden -File `"$ScriptPath`" -AutoHealBoard -DispatchEvents -SyncQueue -Json"
    $action = New-ScheduledTaskAction -Execute "pwsh.exe" -Argument $argList -WorkingDirectory $RepoDevRoot
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description "Zero-token MOBAflow/MOBAdev hygiene, Kanban health, and event dispatcher" -Force | Out-Null
    Write-Host "Registered Windows Scheduled Task '$TaskName' (every $IntervalMinutes minutes, -AutoHealBoard -DispatchEvents -SyncQueue)."
    return
}

if ($UnregisterScheduledTask) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Unregistered Windows Scheduled Task '$TaskName'."
    return
}

if ($ResetEventState -and (Test-Path -LiteralPath $StatePath)) {
    Remove-Item -LiteralPath $StatePath -Force -ErrorAction SilentlyContinue
}

# Load deduplication state (prune entries older than 24 hours)
$dispatchedKeys = [hashtable]::new([System.StringComparer]::OrdinalIgnoreCase)
$pruneCutoffUtc = [DateTimeOffset]::UtcNow.AddHours(-24)
if (Test-Path -LiteralPath $StatePath) {
    try {
        $rawState = Get-Content -LiteralPath $StatePath -Raw -Encoding utf8NoBOM
        if (-not [string]::IsNullOrWhiteSpace($rawState)) {
            $parsedState = $rawState | ConvertFrom-Json
            foreach ($prop in $parsedState.PSObject.Properties) {
                $ts = [DateTimeOffset]::MinValue
                if ([DateTimeOffset]::TryParse("$($prop.Value)", [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref]$ts)) {
                    if ($ts -ge $pruneCutoffUtc) {
                        $dispatchedKeys[$prop.Name] = $ts.ToUniversalTime().ToString("O")
                    }
                }
            }
        }
    }
    catch {
        Write-Verbose "Watchdog state load failed: $_"
    }
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
    catch {
        Write-Verbose "Watchdog repo hygiene check failed: $_"
    }
}

# 2. Query local Dashboard :5179 monitors
$monitorsOnline = $false
$monitorsSummary = @()
$claudeRateLimited = $false
$codexMonitor = $null
try {
    $monitorsBaseUrl = $DashboardApiUrl.TrimEnd('/')
    $monResp = Invoke-RestMethod -Uri "$monitorsBaseUrl/api/monitors?refresh=true" -TimeoutSec 5 -ErrorAction Stop
    $monitorsOnline = $true
    foreach ($m in @($monResp.monitors)) {
        if ($m.displayName -eq "MOBAi Claude Opus" -and $m.state -eq "rate_limited") {
            $claudeRateLimited = $true
        }
        $monId = if ($m.PSObject.Properties["id"] -and $m.id) { "$($m.id)" } else { "" }
        $lastAct = if ($m.PSObject.Properties["lastActivityUtc"] -and $m.lastActivityUtc) { "$($m.lastActivityUtc)" } else { $null }
        $monSummary = [pscustomobject]@{
            Id                  = $monId
            DisplayName         = $m.displayName
            State               = $m.state
            Model               = $m.model
            LastActivityUtc     = $lastAct
            RateLimitResetLabel = $m.rateLimitResetLabel
        }
        if ($monId -eq "codex-monitor" -or $m.displayName -eq "MOBAi ChatGPT Codex") {
            $codexMonitor = $monSummary
        }
        $monitorsSummary += $monSummary
    }
}
catch { }

# 3. Query MOBAflow Open PRs
$openPrs = @()
$openPrsReadSucceeded = $false
try {
    $prJson = & gh pr list --repo ahuelsmann/MOBAflow --state open --limit 50 --json number,title,url,headRefName,headRefOid,isDraft,mergeStateStatus,statusCheckRollup,body,files,closingIssuesReferences 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($prJson)) {
        $openPrs = @($prJson | ConvertFrom-Json)
        $openPrsReadSucceeded = $true
    }
}
catch {
    Write-Verbose "Watchdog open PR query failed: $_"
}

$openCodePrCount = 0
$openPrDetails = [System.Collections.Generic.List[object]]::new()
$fastPathCandidates = [System.Collections.Generic.List[object]]::new()
$failedChecksPrs = [System.Collections.Generic.List[object]]::new()
$detectedEvents = [System.Collections.Generic.List[object]]::new()

foreach ($pr in $openPrs) {
    $bodyText = if ($pr.body) { "$($pr.body)" } else { "" }
    $titleText = if ($pr.title) { "$($pr.title)" } else { "" }
    $headRef = if ($pr.PSObject.Properties["headRefName"] -and $pr.headRefName) { "$($pr.headRefName)" } else { "" }
    $headOid = if ($pr.PSObject.Properties["headRefOid"] -and $pr.headRefOid) { "$($pr.headRefOid)" } else { "" }
    $prUrl = if ($pr.PSObject.Properties["url"] -and $pr.url) { "$($pr.url)" } else { "https://github.com/ahuelsmann/MOBAflow/pull/$($pr.number)" }
    $headShort = if ($headOid.Length -gt 8) { $headOid.Substring(0, 8) } else { $headOid }

    $ownerInfo = Get-TargetOwnerFromBranch -BranchName $headRef
    $targetOwner = $ownerInfo.Owner

    $isCodePr = $true
    $prFiles = if ($pr.PSObject.Properties["files"] -and $null -ne $pr.files) { @($pr.files) } else { @() }
    if ($prFiles.Count -gt 0) {
        $allDocsOnly = $true
        foreach ($f in $prFiles) {
            $fpath = if ($f.PSObject.Properties["path"] -and $f.path) { ("$($f.path)" -replace '\\', '/').TrimStart('./') } else { "" }
            if ($fpath -notmatch '^(?i)(docs|specs|plans)/') {
                $allDocsOnly = $false
                break
            }
        }
        $isCodePr = -not $allDocsOnly
    }
    else {
        $isLowDocs = ($bodyText -match '(?im)Risk class[^\r\n]*:\s*L\b') -or
                     ($titleText -match '^(docs|spec|chore)(\([^)]*\))?:' -and $bodyText -notmatch '(?im)Risk class[^\r\n]*:\s*[HM]\b')
        $isCodePr = -not $isLowDocs
    }
    if ($isCodePr) {
        $openCodePrCount++
    }

    $linkedIssues = [System.Collections.Generic.HashSet[int]]::new()
    if ($pr.PSObject.Properties["closingIssuesReferences"] -and $null -ne $pr.closingIssuesReferences) {
        $closingNodes = if ($pr.closingIssuesReferences.PSObject.Properties["nodes"]) { @($pr.closingIssuesReferences.nodes) } else { @($pr.closingIssuesReferences) }
        foreach ($cRef in $closingNodes) {
            if ($null -ne $cRef -and $cRef.PSObject.Properties["number"] -and $cRef.number) {
                $null = $linkedIssues.Add([int]$cRef.number)
            }
        }
    }
    foreach ($m in [regex]::Matches("$titleText`n$bodyText", '(?i)\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?|refs?|issue)\s+#(\d+)\b')) {
        $null = $linkedIssues.Add([int]$m.Groups[1].Value)
    }
    $branchMatch = [regex]::Match($headRef, '^[^/]+/(?:issue-)?(\d{1,6})(?:[-_/]|$)')
    if ($branchMatch.Success) {
        $null = $linkedIssues.Add([int]$branchMatch.Groups[1].Value)
    }
    $openPrDetails.Add([pscustomobject]@{
        Number       = [int]$pr.number
        IsCodePr     = $isCodePr
        LinkedIssues = $linkedIssues
    })

    $checks = @($pr.statusCheckRollup)
    $allGreen = $checks.Count -gt 0
    $failedNames = [System.Collections.Generic.List[string]]::new()

    foreach ($c in $checks) {
        if ($null -eq $c) { continue }
        $checkName = if ($c.PSObject.Properties["name"] -and $c.name) { "$($c.name)" } else { "check" }
        $status = if ($c.PSObject.Properties["status"] -and $c.status) { "$($c.status)" } else { "" }
        $conclusion = if ($c.PSObject.Properties["conclusion"] -and $c.conclusion) { "$($c.conclusion)" } else { "" }
        if ($status -and $status -ne "COMPLETED") {
            $allGreen = $false
        }
        elseif ($conclusion -in @("FAILURE", "TIMED_OUT", "CANCELLED")) {
            $allGreen = $false
            $failedNames.Add($checkName)
        }
        elseif ($conclusion -notin @("SUCCESS", "NEUTRAL", "SKIPPED")) {
            $allGreen = $false
        }
    }

    if ($failedNames.Count -gt 0) {
        $sortedFailed = @($failedNames | Sort-Object -Unique)
        $failedChecksPrs.Add([pscustomobject]@{
            Number      = [int]$pr.number
            Title       = $titleText
            Branch      = $headRef
            HeadShort   = $headShort
            TargetOwner = $targetOwner
            Failed      = $sortedFailed
            Url         = $prUrl
        })
        $detectedEvents.Add([pscustomobject]@{
            EventKey     = "pr-$($pr.number):${headShort}:failed:$($sortedFailed -join ',')"
            EventType    = "CiOrSonarFailed"
            PrNumber     = [int]$pr.number
            IssueNumber  = $null
            TargetOwner  = $targetOwner
            BusRecipient = $ownerInfo.BusId
            State        = "${headShort}:failed:$($sortedFailed -join ',')"
            WakeViaBus   = $false
            Url          = $prUrl
            Message      = "[Watchdog CiOrSonarFailed] PR #$($pr.number) ($headRef @ $headShort): Failed check(s): $($sortedFailed -join ', '). Fix required -> $prUrl"
        })
    }
    elseif ($allGreen -and ($pr.mergeStateStatus -eq "BEHIND" -or [bool]$pr.isDraft)) {
        $fastPathCandidates.Add([pscustomobject]@{
            Number           = [int]$pr.number
            Title            = $titleText
            Branch           = $headRef
            HeadShort        = $headShort
            TargetOwner      = $targetOwner
            IsDraft          = [bool]$pr.isDraft
            MergeStateStatus = $pr.mergeStateStatus
            Url              = $prUrl
        })
        $detectedEvents.Add([pscustomobject]@{
            EventKey     = "pr-$($pr.number):${headShort}:green-ready"
            EventType    = "AllChecksGreenReady"
            PrNumber     = [int]$pr.number
            IssueNumber  = $null
            TargetOwner  = $targetOwner
            BusRecipient = $ownerInfo.BusId
            State        = "${headShort}:green-ready"
            WakeViaBus   = $false
            Url          = $prUrl
            Message      = "[Watchdog AllChecksGreenReady] PR #$($pr.number) ($headRef @ $headShort): All $($checks.Count) checks green (Draft=$([bool]$pr.isDraft), MergeState=$($pr.mergeStateStatus)) -> ready for Fast-Path / Ready promotion / Final Review -> $prUrl"
        })
    }
}

# 4. Query MOBAflow Kanban Project #1 (PVT_kwHOAPwF2s4BZ8_z) with full cursor pagination
$projectId = "PVT_kwHOAPwF2s4BZ8_z"
$statusFieldId = "PVTSSF_lAHOAPwF2s4BZ8_zzhU4AOM"
$doneOptionId = "98236657"

$boardItems = @()
$projectFieldNames = @()
$autoHealedItems = [System.Collections.Generic.List[object]]::new()
$boardReadSucceeded = $false
$boardReadError = $null
$graphQuery = 'query($owner:String!,$num:Int!,$cursor:String){user(login:$owner){projectV2(number:$num){fields(first:20){nodes{... on ProjectV2SingleSelectField{name}}}items(first:100,after:$cursor,query:"-status:Done -status:Planned -status:Inbox"){pageInfo{hasNextPage endCursor}nodes{id fieldValues(first:20){nodes{... on ProjectV2ItemFieldSingleSelectValue{name field{... on ProjectV2SingleSelectField{name}}}}}content{__typename ... on Issue{number title state} ... on PullRequest{number title state}}}}}}}'

try {
    $cursor = $null
    $pageCount = 0
    $maxPages = 50
    while ($pageCount -lt $maxPages) {
        $pageCount++
        $gqlArgs = @("api", "graphql", "-f", "owner=ahuelsmann", "-F", "num=1", "-f", "query=$graphQuery")
        if (-not [string]::IsNullOrWhiteSpace($cursor)) {
            $gqlArgs += @("-f", "cursor=$cursor")
        }
        $gqlRaw = & gh @gqlArgs 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gqlRaw)) {
            $boardReadError = "gh api graphql exited with $LASTEXITCODE"
            break
        }

        $gqlObj = ($gqlRaw -join "`n") | ConvertFrom-Json
        if ($gqlObj.PSObject.Properties["errors"] -and @($gqlObj.errors).Count -gt 0) {
            $boardReadError = "GraphQL returned API errors"
            break
        }

        $userNode = if ($gqlObj.PSObject.Properties["data"] -and $gqlObj.data) { $gqlObj.data.user } else { $null }
        $projectNode = if ($userNode) { $userNode.projectV2 } else { $null }
        $itemsPage = if ($projectNode) { $projectNode.items } else { $null }
        if ($null -eq $projectNode -or $null -eq $itemsPage) {
            $boardReadError = "GraphQL response missing projectV2.items"
            break
        }

        if ($projectFieldNames.Count -eq 0 -and $projectNode.fields -and $projectNode.fields.nodes) {
            $projectFieldNames = @($projectNode.fields.nodes | Where-Object { $_ -and $_.PSObject.Properties["name"] -and $_.name } | ForEach-Object { "$($_.name)" })
        }

        $nodes = @($itemsPage.nodes)
        foreach ($node in $nodes) {
            if ($null -eq $node -or $null -eq $node.content) { continue }
            $kanbanStatus = $null
            $priority = $null
            $riskClass = $null
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
                    elseif ($fieldNameProp.Value -eq "Risk class") { $riskClass = "$($nameProp.Value)" }
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
                RiskClass    = $riskClass
            }
        }

        $pageInfo = if ($itemsPage.PSObject.Properties["pageInfo"]) { $itemsPage.pageInfo } else { $null }
        $hasNext = ($null -ne $pageInfo -and [bool]$pageInfo.hasNextPage)
        if ($hasNext) {
            $nextCursor = if ($pageInfo.PSObject.Properties["endCursor"] -and $pageInfo.endCursor) { "$($pageInfo.endCursor)" } else { "" }
            if ([string]::IsNullOrWhiteSpace($nextCursor)) {
                $boardReadError = "GraphQL pagination missing endCursor while hasNextPage=true"
                break
            }
            $cursor = $nextCursor
        }
        else {
            $boardReadSucceeded = $true
            break
        }
    }

    if (-not $boardReadSucceeded -and -not $boardReadError) {
        $boardReadError = "GraphQL pagination exceeded $maxPages pages"
    }
}
catch {
    $boardReadSucceeded = $false
    $boardReadError = "$_"
    Write-Warning "Watchdog GraphQL error: $_"
}

$readyIssues = @($boardItems | Where-Object { $_.Kind -eq "Issue" -and $_.GitHubState -eq "OPEN" -and $_.KanbanStatus -eq "Ready" })
$rf30OpenInProgress = @($boardItems | Where-Object { $_.Kind -eq "Issue" -and $_.Number -eq 452 -and $_.GitHubState -eq "OPEN" -and $_.KanbanStatus -eq "In progress" }).Count -gt 0

# 5. Re-plan trigger check & fail-safe work_queue / meta.code_wip sync
$busScript = Join-Path $RepoDevRoot "bus\mobai-swarm-bus.mjs"
$nodeCmd = Get-Command node -ErrorAction SilentlyContinue
$codexExe = Join-Path $env:USERPROFILE ".codex\.sandbox-bin\codex.exe"

$hasPriorityField = $projectFieldNames -contains "Priority"
$hasRiskField = $projectFieldNames -contains "Risk class"
$unexpectedFieldValues = @()
$readyHasAnyPriorityOrRisk = $false
foreach ($ri in $readyIssues) {
    if ($ri.Priority -or $ri.RiskClass) { $readyHasAnyPriorityOrRisk = $true }
    if ($ri.Priority -and "$($ri.Priority)".Trim().ToUpperInvariant() -notmatch '^P[0-3]$') {
        $unexpectedFieldValues += "#$($ri.Number):Priority=$($ri.Priority)"
    }
    if ($ri.RiskClass -and "$($ri.RiskClass)".Trim().ToUpperInvariant() -notin @("H", "M", "L")) {
        $unexpectedFieldValues += "#$($ri.Number):RiskClass=$($ri.RiskClass)"
    }
}

$queueSchemaValid = $true
$queueSyncWarning = $null
if (-not $boardReadSucceeded) {
    $queueSchemaValid = $false
    $queueSyncWarning = if ($boardReadError) {
        "Project #1 board read failed ($boardReadError); preserving existing work_queue and meta.code_wip."
    }
    else {
        "Project #1 board read incomplete; preserving existing work_queue and meta.code_wip."
    }
}
elseif ($projectFieldNames.Count -gt 0 -and (-not $hasPriorityField -or -not $hasRiskField)) {
    $queueSchemaValid = $false
    $queueSyncWarning = "Project #1 schema check failed: missing Priority ($hasPriorityField) or Risk class ($hasRiskField) field."
}
elseif ($readyIssues.Count -gt 0 -and -not $readyHasAnyPriorityOrRisk) {
    $queueSchemaValid = $false
    $queueSyncWarning = "Project #1 Ready items expose no Priority/Risk class values."
}
elseif ($unexpectedFieldValues.Count -gt 0) {
    $queueSyncWarning = "Unexpected Kanban Priority/Risk values: $($unexpectedFieldValues -join ', ')"
}

if ($queueSyncWarning -and -not $Json) {
    Write-Warning $queueSyncWarning
}

$queueSyncResult = [pscustomobject]@{
    Enabled            = $effectiveSyncQueue
    BoardReadSucceeded = $boardReadSucceeded
    SchemaValid        = $queueSchemaValid
    Warning            = $queueSyncWarning
    UpsertedCount      = 0
    RemovedCount       = 0
    DoneCount          = 0
    CodeWip            = $openCodePrCount
}

$queueItemsSnapshot = @()
if ($nodeCmd -and (Test-Path -LiteralPath $busScript)) {
    if ($effectiveSyncQueue -and $boardReadSucceeded) {
        try {
            if ($openPrsReadSucceeded) {
                & $nodeCmd.Source --no-warnings $busScript --cli queue --action sync-meta --meta-key "code_wip" --meta-value "$openCodePrCount" 2>$null | Out-Null
            }

            if ($queueSchemaValid) {
                $preListJson = & $nodeCmd.Source --no-warnings $busScript --cli queue --action list 2>$null
                $existingQueueItems = @()
                if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($preListJson)) {
                    $preListObj = ($preListJson -join "`n") | ConvertFrom-Json
                    $existingQueueItems = @($preListObj.items)
                }

                $readyIssueSet = [System.Collections.Generic.HashSet[int]]::new()
                foreach ($ri in $readyIssues) {
                    $null = $readyIssueSet.Add([int]$ri.Number)
                    $prioInt = switch -Regex ("$($ri.Priority)".Trim().ToUpperInvariant()) {
                        '^P0$' { 0 }
                        '^P1$' { 1 }
                        '^P2$' { 2 }
                        '^P3$' { 3 }
                        default { 2 }
                    }
                    $riskVal = if ($ri.RiskClass) { "$($ri.RiskClass)".Trim().ToUpperInvariant() } else { "" }
                    $upsertArgs = @("--no-warnings", $busScript, "--cli", "queue", "--action", "upsert", "--issue-number", "$($ri.Number)", "--title", "$($ri.Title)", "--priority", "$prioInt", "--status", "queued")
                    if ($riskVal -in @("H", "M", "L")) {
                        $upsertArgs += @("--risk-class", $riskVal)
                    }
                    & $nodeCmd.Source @upsertArgs 2>$null | Out-Null
                    if ($LASTEXITCODE -eq 0) { $queueSyncResult.UpsertedCount++ }
                }

                foreach ($qi in $existingQueueItems) {
                    if ($qi.status -notin @("queued", "claimed")) { continue }
                    $qNum = [int]$qi.issue_number
                    if ($readyIssueSet.Contains($qNum)) { continue }

                    $boardMatch = $boardItems | Where-Object { $_.Kind -eq "Issue" -and $_.Number -eq $qNum } | Select-Object -First 1

                    if ($qi.status -eq "claimed") {
                        $qiRisk = if ($qi.risk_class) { "$($qi.risk_class)".Trim().ToUpperInvariant() } else { "" }
                        $isCodeItem = ($qiRisk -ne "L")
                        $hasLinkedOpenPr = $false
                        foreach ($prInfo in $openPrDetails) {
                            if ($prInfo.LinkedIssues.Contains($qNum) -and (-not $isCodeItem -or $prInfo.IsCodePr)) {
                                $hasLinkedOpenPr = $true
                                break
                            }
                        }

                        if ($hasLinkedOpenPr) {
                            & $nodeCmd.Source --no-warnings $busScript --cli queue --action done --issue-number "$qNum" 2>$null | Out-Null
                            if ($LASTEXITCODE -eq 0) { $queueSyncResult.DoneCount++ }
                            continue
                        }

                        if ($boardMatch) {
                            if ($boardMatch.GitHubState -eq "CLOSED" -or $boardMatch.KanbanStatus -eq "Done") {
                                & $nodeCmd.Source --no-warnings $busScript --cli queue --action done --issue-number "$qNum" 2>$null | Out-Null
                                if ($LASTEXITCODE -eq 0) { $queueSyncResult.DoneCount++ }
                            }
                            elseif ($boardMatch.KanbanStatus -in @("Planned", "Inbox")) {
                                & $nodeCmd.Source --no-warnings $busScript --cli queue --action upsert --issue-number "$qNum" --status "removed" 2>$null | Out-Null
                                if ($LASTEXITCODE -eq 0) { $queueSyncResult.RemovedCount++ }
                            }
                            # Otherwise the issue is open (e.g. In progress / In review) with no linked PR yet: preserve lease & WIP reservation.
                            continue
                        }

                        $ghIssueRaw = (& gh issue view $qNum --repo ahuelsmann/MOBAflow --json state,projectItems 2>$null)
                        $claimTransition = $null
                        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($ghIssueRaw)) {
                            $rawTrimmed = ($ghIssueRaw -join "`n").Trim()
                            if ($rawTrimmed -eq "CLOSED") {
                                $claimTransition = "done"
                            }
                            else {
                                try {
                                    $issueObj = $rawTrimmed | ConvertFrom-Json
                                    if ($issueObj.PSObject.Properties["state"] -and "$($issueObj.state)".Trim() -eq "CLOSED") {
                                        $claimTransition = "done"
                                    }
                                    elseif ($issueObj.PSObject.Properties["projectItems"] -and $issueObj.projectItems) {
                                        foreach ($pi in @($issueObj.projectItems)) {
                                            if ($null -eq $pi) { continue }
                                            $piStatus = if ($pi.PSObject.Properties["status"] -and $pi.status) { "$($pi.status)".Trim() } else { "" }
                                            if ($piStatus -eq "Done") {
                                                $claimTransition = "done"
                                                break
                                            }
                                            if ($piStatus -in @("Planned", "Inbox")) {
                                                $claimTransition = "removed"
                                                break
                                            }
                                        }
                                    }
                                }
                                catch {
                                    Write-Verbose "Watchdog issue view JSON parse skipped for #${qNum}: $_"
                                }
                            }
                        }

                        if ($claimTransition -eq "done") {
                            & $nodeCmd.Source --no-warnings $busScript --cli queue --action done --issue-number "$qNum" 2>$null | Out-Null
                            if ($LASTEXITCODE -eq 0) { $queueSyncResult.DoneCount++ }
                        }
                        elseif ($claimTransition -eq "removed") {
                            & $nodeCmd.Source --no-warnings $busScript --cli queue --action upsert --issue-number "$qNum" --status "removed" 2>$null | Out-Null
                            if ($LASTEXITCODE -eq 0) { $queueSyncResult.RemovedCount++ }
                        }
                        continue
                    }

                    $isClosed = $false
                    if ($boardMatch) {
                        $isClosed = ($boardMatch.GitHubState -eq "CLOSED" -or $boardMatch.KanbanStatus -eq "Done")
                    }
                    else {
                        $ghIssueState = (& gh issue view $qNum --repo ahuelsmann/MOBAflow --json state -q .state 2>$null)
                        if ($LASTEXITCODE -eq 0 -and "$ghIssueState".Trim() -eq "CLOSED") {
                            $isClosed = $true
                        }
                    }

                    if ($isClosed) {
                        & $nodeCmd.Source --no-warnings $busScript --cli queue --action done --issue-number "$qNum" 2>$null | Out-Null
                        if ($LASTEXITCODE -eq 0) { $queueSyncResult.DoneCount++ }
                    }
                    else {
                        & $nodeCmd.Source --no-warnings $busScript --cli queue --action upsert --issue-number "$qNum" --status "removed" 2>$null | Out-Null
                        if ($LASTEXITCODE -eq 0) { $queueSyncResult.RemovedCount++ }
                    }
                }
            }
        }
        catch {
            $queueSyncResult.Warning = if ($queueSyncResult.Warning) { "$($queueSyncResult.Warning); $_" } else { "$_" }
        }
    }

    try {
        $postListJson = & $nodeCmd.Source --no-warnings $busScript --cli queue --action list 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($postListJson)) {
            $postListObj = ($postListJson -join "`n") | ConvertFrom-Json
            $queueItemsSnapshot = @($postListObj.items)
        }
    }
    catch {
        $queueSyncResult.Warning = if ($queueSyncResult.Warning) { "$($queueSyncResult.Warning); $_" } else { "$_" }
    }
}

$nowUtc = [DateTimeOffset]::UtcNow
$activeClaimedCodeCount = 0
foreach ($qi in $queueItemsSnapshot) {
    if ($qi.status -ne "claimed") { continue }
    $isExpiredClaim = $false
    if ($qi.lease_expires_utc) {
        $leaseTs = [DateTimeOffset]::MinValue
        if ([DateTimeOffset]::TryParse("$($qi.lease_expires_utc)", [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref]$leaseTs)) {
            $isExpiredClaim = ($leaseTs -le $nowUtc)
        }
    }
    if ($isExpiredClaim) { continue }
    $qiRisk = if ($qi.risk_class) { "$($qi.risk_class)".Trim().ToUpperInvariant() } else { "" }
    if ($qiRisk -ne "L") {
        $activeClaimedCodeCount++
    }
}
$effectiveCodeWip = $openCodePrCount + $activeClaimedCodeCount

$topEligibleQueueItem = $null
foreach ($qi in $queueItemsSnapshot) {
    $isExpiredClaim = $false
    if ($qi.status -eq "claimed" -and $qi.lease_expires_utc) {
        $leaseTs = [DateTimeOffset]::MinValue
        if ([DateTimeOffset]::TryParse("$($qi.lease_expires_utc)", [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref]$leaseTs)) {
            $isExpiredClaim = ($leaseTs -le $nowUtc)
        }
    }
    if ($qi.status -ne "queued" -and -not $isExpiredClaim) { continue }
    $qiRisk = if ($qi.risk_class) { "$($qi.risk_class)".Trim().ToUpperInvariant() } else { "" }
    if ($effectiveCodeWip -ge 2 -and $qiRisk -ne "L") { continue }
    $topEligibleQueueItem = $qi
    break
}

# 6. Detect PrMerged -> NextWorkAvailable & HeadIdleWake
if ($null -ne $topEligibleQueueItem) {
    try {
        $mergedJson = & gh pr list --repo ahuelsmann/MOBAflow --state merged --limit 10 --json number,title,url,headRefName,mergeCommit,mergedAt 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($mergedJson)) {
            $mergedPrs = @($mergedJson | ConvertFrom-Json)
            $mergedCutoff = $nowUtc.AddHours(-2)
            foreach ($mpr in $mergedPrs) {
                $mergedTs = [DateTimeOffset]::MinValue
                if (-not $mpr.mergedAt -or -not [DateTimeOffset]::TryParse("$($mpr.mergedAt)", [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref]$mergedTs)) {
                    continue
                }
                if ($mergedTs -lt $mergedCutoff) { continue }

                $mergeSha = if ($mpr.mergeCommit -and $mpr.mergeCommit.oid) { "$($mpr.mergeCommit.oid)" } else { "merged" }
                $mergeShaShort = if ($mergeSha.Length -gt 8) { $mergeSha.Substring(0, 8) } else { $mergeSha }
                $mHeadRef = if ($mpr.headRefName) { "$($mpr.headRefName)" } else { "" }
                $mOwner = Get-TargetOwnerFromBranch -BranchName $mHeadRef
                $mUrl = if ($mpr.url) { "$($mpr.url)" } else { "https://github.com/ahuelsmann/MOBAflow/pull/$($mpr.number)" }
                $topRiskLabel = if ($topEligibleQueueItem.risk_class) { $topEligibleQueueItem.risk_class } else { "-" }

                $detectedEvents.Add([pscustomobject]@{
                    EventKey     = "pr-merged-$($mpr.number):${mergeShaShort}"
                    EventType    = "NextWorkAvailable"
                    PrNumber     = [int]$mpr.number
                    IssueNumber  = [int]$topEligibleQueueItem.issue_number
                    TargetOwner  = $mOwner.Owner
                    BusRecipient = $mOwner.BusId
                    State        = "merged-$($mpr.number)-${mergeShaShort}"
                    WakeViaBus   = $false
                    Url          = $mUrl
                    Message      = "[Watchdog NextWorkAvailable] PR #$($mpr.number) ($mHeadRef @ $mergeShaShort) merged. Top queued Ready item: #$($topEligibleQueueItem.issue_number) (P$($topEligibleQueueItem.priority), Risk=$topRiskLabel) $($topEligibleQueueItem.title) -> claim via bus_queue next."
                })
            }
        }
    }
    catch {
        Write-Verbose "Watchdog merged PR lookup failed: $_"
    }

    if ($openCodePrCount -lt 3 -and $monitorsOnline -and $null -ne $codexMonitor) {
        if ($codexMonitor.State -notin @("rate_limited", "busy") -and $codexMonitor.LastActivityUtc) {
            $lastActTs = [DateTimeOffset]::MinValue
            if ([DateTimeOffset]::TryParse("$($codexMonitor.LastActivityUtc)", [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref]$lastActTs)) {
                $inactiveMin = ($nowUtc - $lastActTs).TotalMinutes
                if ($inactiveMin -gt 30) {
                    $topRiskLabel = if ($topEligibleQueueItem.risk_class) { $topEligibleQueueItem.risk_class } else { "-" }
                    $detectedEvents.Add([pscustomobject]@{
                        EventKey     = "head-idle-wake:codex:issue-$($topEligibleQueueItem.issue_number)"
                        EventType    = "HeadIdleWake"
                        PrNumber     = $null
                        IssueNumber  = [int]$topEligibleQueueItem.issue_number
                        TargetOwner  = "Codex"
                        BusRecipient = "codex"
                        State        = "idle-wake-$($topEligibleQueueItem.issue_number)"
                        WakeViaBus   = $true
                        Url          = "https://github.com/ahuelsmann/MOBAflow/issues/$($topEligibleQueueItem.issue_number)"
                        Message      = "[Watchdog HeadIdleWake] Codex inactive $([Math]::Floor($inactiveMin))m (state=$($codexMonitor.State), code_wip=$openCodePrCount/3) with eligible Ready item #$($topEligibleQueueItem.issue_number) (P$($topEligibleQueueItem.priority), Risk=$topRiskLabel) $($topEligibleQueueItem.title). Claim via bus_queue next."
                    })
                }
            }
        }
    }
}

# 7. Deduplicated event dispatch
$newEventsDispatched = [System.Collections.Generic.List[object]]::new()
if ($DispatchEvents) {
    if (-not (Test-Path -LiteralPath $StateDir)) {
        New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
    }

    foreach ($ev in $detectedEvents) {
        if ($dispatchedKeys.ContainsKey($ev.EventKey)) {
            continue
        }

        $routedTo = "AlertLog"
        if (-not $ev.WakeViaBus -and $ev.TargetOwner -eq "Codex" -and -not [string]::IsNullOrWhiteSpace($CodexThreadId) -and (Test-Path -LiteralPath $codexExe)) {
            try {
                & $codexExe queue --thread $CodexThreadId --message $ev.Message 2>$null | Out-Null
                if ($LASTEXITCODE -eq 0) {
                    $routedTo = "CodexQueue+AlertLog"
                }
            }
            catch {
                Write-Verbose "Watchdog Codex queue dispatch failed: $_"
            }
        }

        if ($nodeCmd -and (Test-Path -LiteralPath $busScript)) {
            try {
                $busRecipient = if ($ev.BusRecipient) { $ev.BusRecipient } else {
                    switch ($ev.TargetOwner) {
                        "Codex" { "codex" }
                        "Antigravity" { "antigravity-gemini" }
                        default { "claude-opus" }
                    }
                }
                $sendArgs = @("--no-warnings", $busScript, "--cli", "send", "--from", "watchdog", "--to", $busRecipient, "--topic", $ev.EventType, "--priority", "high", "--body", $ev.Message)
                if ($null -ne $ev.PrNumber -and [int]$ev.PrNumber -gt 0) {
                    $sendArgs += @("--pr", "$($ev.PrNumber)")
                }
                if ($null -ne $ev.IssueNumber -and [int]$ev.IssueNumber -gt 0) {
                    $sendArgs += @("--issue", "$($ev.IssueNumber)")
                }
                if ($ev.State) {
                    $sendArgs += @("--state", "$($ev.State)")
                }
                if ($ev.WakeViaBus) {
                    $sendArgs += "--wake"
                    if (-not [string]::IsNullOrWhiteSpace($CodexThreadId)) {
                        $sendArgs += @("--codex-thread-id", $CodexThreadId)
                    }
                }
                $prevCodexThreadEnv = $env:MOBADEV_CODEX_THREAD_ID
                if (-not [string]::IsNullOrWhiteSpace($CodexThreadId)) {
                    $env:MOBADEV_CODEX_THREAD_ID = $CodexThreadId
                }
                try {
                    & $nodeCmd.Source @sendArgs 2>$null | Out-Null
                }
                finally {
                    if ($null -eq $prevCodexThreadEnv) {
                        Remove-Item Env:MOBADEV_CODEX_THREAD_ID -ErrorAction SilentlyContinue
                    }
                    else {
                        $env:MOBADEV_CODEX_THREAD_ID = $prevCodexThreadEnv
                    }
                }
                if ($LASTEXITCODE -eq 0) {
                    $routedTo = if ($ev.WakeViaBus) { "SwarmBusWake+$routedTo" } else { "SwarmBus+$routedTo" }
                }
            }
            catch {
                Write-Verbose "Watchdog Swarm Bus dispatch failed: $_"
            }
        }

        $utcNow = [DateTimeOffset]::UtcNow.ToString("O")
        $logLine = "[$utcNow] [$($ev.EventType)] [$($ev.TargetOwner)] [$($ev.EventKey)] $($ev.Message)"
        Add-Content -LiteralPath $AlertLogPath -Value $logLine -Encoding utf8NoBOM
        $dispatchedKeys[$ev.EventKey] = $utcNow

        $newEventsDispatched.Add([pscustomobject]@{
            EventKey    = $ev.EventKey
            EventType   = $ev.EventType
            PrNumber    = $ev.PrNumber
            IssueNumber = $ev.IssueNumber
            TargetOwner = $ev.TargetOwner
            RoutedTo    = $routedTo
            Url         = $ev.Url
        })
    }

    ($dispatchedKeys | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $StatePath -Encoding utf8NoBOM
}

$quietWindowOpenForRf30 = ($openCodePrCount -eq 0) -and $rf30OpenInProgress
$readyIntakeAvailable = ($openCodePrCount -lt 3) -and ($readyIssues.Count -gt 0)
$readyColumnOverloaded = $readyIssues.Count -gt 3

$recommendedOwner = if ($claudeRateLimited) {
    "MOBAi ChatGPT Codex + MOBAi Antigravity Gemini (Claude Code in Quota Pause -> 2/3 Quorum Failover)"
}
else {
    "MOBAi Claude Opus (COO)"
}

$recommendedPrompt = if ($failedChecksPrs.Count -gt 0) {
    $failSummary = ($failedChecksPrs | ForEach-Object { "#$($_.Number) ($($_.Failed -join '/'))" }) -join ", "
    "Fixe fehlgeschlagene Checks auf $failSummary und prüfe parallele Promotion/WIP."
}
elseif ($quietWindowOpenForRf30) {
    "Das Quiet Window (0 offene PRs) ist offen: bitte führe die verbliebenen Code-Slices von #452 (RF-30 Test-Projekt-Split & Root-Namespaces) im Disjoint-Lock-Workflow bis zum Merge auf main durch."
}
elseif ($fastPathCandidates.Count -gt 0) {
    $prNums = ($fastPathCandidates | ForEach-Object { "#$($_.Number)" }) -join ", "
    "Prüfe die grünen PR-Kandidaten ($prNums) auf Disjoint main-Sync Fast-Path / Ready-Promotion und Auto-Merge."
}
elseif ($readyIntakeAvailable) {
    "Prüfe die Ready-Spalte im MOBAflow GitHub Kanban Board und starte freie disjunkte Spuren gemäß AGENTS.md, master-coordination.instructions.md und MOBAdev/manifest.md (3-Welten-Parallelbetrieb)."
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
    ReadyIssues               = @($readyIssues | Select-Object Number, Priority, RiskClass, Title)
    QueueSync                 = $queueSyncResult
    AutoHealedBoardItemsCount = $autoHealedItems.Count
    AutoHealedBoardItems      = @($autoHealedItems)
    FailedChecksPrs           = @($failedChecksPrs)
    NewEventsDispatchedCount  = $newEventsDispatched.Count
    NewEventsDispatched       = @($newEventsDispatched)
    AlertLogPath              = $AlertLogPath
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
    if ($effectiveSyncQueue) {
        Write-Host "Queue Sync -> SchemaValid=$($queueSyncResult.SchemaValid) | Upserted=$($queueSyncResult.UpsertedCount) | Removed=$($queueSyncResult.RemovedCount) | Done=$($queueSyncResult.DoneCount) | meta.code_wip=$($queueSyncResult.CodeWip)"
        if ($queueSyncResult.Warning) {
            Write-Host "Queue Sync Warning -> $($queueSyncResult.Warning)"
        }
    }
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
    if ($failedChecksPrs.Count -gt 0) {
        Write-Host "Failed Checks:"
        $failedChecksPrs | ForEach-Object { Write-Host "  ! PR #$($_.Number) [$($_.TargetOwner)] failed: $($_.Failed -join ', ')" }
    }
    if ($DispatchEvents) {
        Write-Host "Event Dispatcher -> NewEventsDispatchedCount=$($report.NewEventsDispatchedCount) | AlertLog=$AlertLogPath"
        $newEventsDispatched | ForEach-Object { Write-Host "  -> [$($_.EventType)] [$($_.RoutedTo)] $($_.EventKey)" }
    }
    Write-Host "Signals -> QuietWindowOpenForRf30=$($report.Signals.QuietWindowOpenForRf30) | ReadyIntakeAvailable=$($report.Signals.ReadyIntakeAvailable) | ReadyOverloaded=$($report.Signals.ReadyColumnOverloaded) | FastPathCandidates=$($fastPathCandidates.Count)"
    Write-Host "Next Recommended Owner : $($report.RecommendedOwner)"
    Write-Host "Next Kickoff Prompt    : $($report.RecommendedPrompt)"
}
