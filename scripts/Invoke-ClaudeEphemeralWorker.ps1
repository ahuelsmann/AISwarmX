<#
.SYNOPSIS
    Runs an ephemeral, non-persistent Claude Code CLI worker (Option A) without forking the standing Desktop COO session.
.DESCRIPTION
    Invokes `claude.exe --print --no-session-persistence` against a bounded working directory with strict allowed-models
    and permission boundaries (`Review` = read-only plan mode; `ScopedFix` = worktree-only edits when Desktop is idle or -Force).
    Logs every invocation to %LOCALAPPDATA%\MOBAdev\claude-workers.log.
#>
[CmdletBinding()]
param(
    [string]$Prompt,
    [string]$PromptFile,
    [ValidateSet("Review", "ScopedFix")]
    [string]$Mode = "Review",
    [ValidateSet("claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-4-5")]
    [string]$Model = "claude-opus-5-5",
    [string]$WorkingDirectory = (Get-Location).Path,
    [string]$TargetLabel = "adhoc",
    [switch]$Force,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Prompt) -and -not [string]::IsNullOrWhiteSpace($PromptFile)) {
    if (-not (Test-Path -LiteralPath $PromptFile)) {
        throw "PromptFile not found: $PromptFile"
    }
    $Prompt = Get-Content -LiteralPath $PromptFile -Raw -Encoding utf8
}

if ([string]::IsNullOrWhiteSpace($Prompt)) {
    throw "Either -Prompt or -PromptFile with non-empty content is required."
}

if ($Model -eq "claude-haiku-4-5") {
    throw "Model 'claude-haiku-4-5' is restricted to read-only Explore triage per MOBAdev/docs/allowed-models.md and is not permitted for Review or ScopedFix."
}

if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) {
    throw "WorkingDirectory does not exist: $WorkingDirectory"
}

$resolvedWorkDir = (Resolve-Path -LiteralPath $WorkingDirectory).Path.TrimEnd('\', '/')
$forbiddenRoot = "C:\Repo\ahuelsmann\MOBAflow"

if ($Mode -eq "ScopedFix") {
    if ([string]::Equals($resolvedWorkDir, $forbiddenRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing ScopedFix directly in '$forbiddenRoot' root. Point -WorkingDirectory to an isolated worktree under artifacts\worktrees\<slice>."
    }

    try {
        $monResp = Invoke-RestMethod -Uri "http://127.0.0.1:5179/api/monitors" -TimeoutSec 3 -ErrorAction Stop
        $claudeDesktop = @($monResp.monitors) | Where-Object { $_.displayName -eq "MOBAi Claude Opus" } | Select-Object -First 1
        if ($null -ne $claudeDesktop -and $claudeDesktop.state -eq "busy" -and -not $Force) {
            throw "MOBAi Claude Opus Desktop session is currently 'busy'. Re-run with -Force only if working in a strictly disjoint worktree."
        }
    }
    catch {
        if ($_.Exception.Message -like "*currently 'busy'*") {
            throw
        }
        # Dashboard offline -> allow ScopedFix in isolated worktree.
    }
}

$claudeExeCandidates = @(
    (Join-Path $env:USERPROFILE ".local\bin\claude.exe")
)
$claudeExe = $claudeExeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $claudeExe) {
    $cmd = Get-Command claude -ErrorAction SilentlyContinue
    if ($cmd) {
        $claudeExe = $cmd.Source
    }
}
if (-not $claudeExe) {
    throw "claude.exe not found in '$($claudeExeCandidates[0])' or PATH."
}

$claudeArgs = [System.Collections.Generic.List[string]]::new()
$claudeArgs.Add("--print")
$claudeArgs.Add("--no-session-persistence")
$claudeArgs.Add("--model")
$claudeArgs.Add($Model)
$claudeArgs.Add("--permission-prompts")
$claudeArgs.Add("none")
$claudeArgs.Add("--output-format")
$claudeArgs.Add("text")

if ($Mode -eq "Review") {
    $claudeArgs.Add("--permission-mode")
    $claudeArgs.Add("plan")
    $claudeArgs.Add("--tools")
    $claudeArgs.Add("Read,Grep,Glob")
    $claudeArgs.Add("--allowedTools")
    $claudeArgs.Add("Read,Grep,Glob")
}
else {
    $claudeArgs.Add("--permission-mode")
    $claudeArgs.Add("acceptEdits")
    $claudeArgs.Add("--allowedTools")
    $claudeArgs.Add("Read,Grep,Glob,Edit,Write,Bash(git status*),Bash(git diff*),Bash(dotnet test*),Bash(npm *)")
}

$localAppRoot = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { Join-Path $env:USERPROFILE "AppData\Local" } else { $env:LOCALAPPDATA }
$logDir = Join-Path $localAppRoot "MOBAdev"
$logPath = Join-Path $logDir "claude-workers.log"
if (-not (Test-Path -LiteralPath $logDir)) {
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$rawLines = @()
$exitCode = 0

Push-Location -LiteralPath $resolvedWorkDir
try {
    $prevPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $rawLines = $Prompt | & $claudeExe @claudeArgs 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $prevPreference
    }
}
finally {
    Pop-Location
    $sw.Stop()
}

$outputText = (($rawLines | ForEach-Object { "$_" }) -join [Environment]::NewLine).Trim()
$durationSec = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
$previewSingleLine = ($outputText -replace '[\r\n\t]+', ' ').Trim()
if ($previewSingleLine.Length -gt 240) {
    $previewSingleLine = $previewSingleLine.Substring(0, 240) + "..."
}

$utcNow = [DateTimeOffset]::UtcNow.ToString("O")
$logEntry = "[$utcNow] [Target=$TargetLabel] [Mode=$Mode] [Model=$Model] [Exit=$exitCode] [Duration=${durationSec}s] [Cwd=$resolvedWorkDir] $previewSingleLine"
Add-Content -LiteralPath $logPath -Value $logEntry -Encoding utf8NoBOM

$result = [pscustomobject]@{
    TargetLabel      = $TargetLabel
    Mode             = $Mode
    Model            = $Model
    WorkingDirectory = $resolvedWorkDir
    ExitCode         = $exitCode
    DurationSec      = $durationSec
    LogPath          = $logPath
    Output           = $outputText
}

if ($Json) {
    $result | ConvertTo-Json -Depth 4
}
else {
    Write-Output $outputText
    if ($exitCode -ne 0) {
        exit $exitCode
    }
}
