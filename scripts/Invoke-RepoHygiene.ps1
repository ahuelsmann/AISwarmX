<#
.SYNOPSIS
    Lossless Git worktree and local branch hygiene for MOBAflow and MOBAdev (zero LLM tokens).
.DESCRIPTION
    Safely removes only clean, provably merged/redundant worktrees and local branches.
    Dirty worktrees, unmerged work, standing 'claude/review-from-start', 'main', and branches
    backing open PRs are always preserved. Never deletes remote GitHub branches.
#>
[CmdletBinding()]
param(
    [string]$MobaFlowPath = "C:\Repo\ahuelsmann\MOBAflow",
    [string]$MobaDevPath = "C:\Repo\ahuelsmann\MOBAdev",
    [switch]$DryRun,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

function Test-GitHubReachable {
    try {
        $resolved = Resolve-DnsName github.com -ErrorAction SilentlyContinue
        return ($null -ne $resolved)
    }
    catch {
        return $false
    }
}

function Invoke-GitSafe {
    param(
        [string]$RepoPath,
        [string[]]$GitArgs
    )
    $output = & git -C $RepoPath @GitArgs 2>&1
    $exitCode = $LASTEXITCODE
    return [pscustomobject]@{
        ExitCode = $exitCode
        Lines    = @($output | ForEach-Object { "$_".TrimEnd() } | Where-Object { $_ -ne $null })
    }
}

function Parse-WorktreePorcelain {
    param([string[]]$Lines)
    $items = [System.Collections.Generic.List[object]]::new()
    $currentPath = $null
    $currentHead = ""
    $currentBranch = "(detached)"

    foreach ($rawLine in $Lines) {
        $line = "$rawLine".Trim()
        if ([string]::IsNullOrWhiteSpace($line)) {
            if ($null -ne $currentPath) {
                $items.Add([pscustomobject]@{
                    Path   = $currentPath
                    Head   = $currentHead
                    Branch = $currentBranch
                })
                $currentPath = $null
                $currentHead = ""
                $currentBranch = "(detached)"
            }
            continue
        }

        if ($line.StartsWith("worktree ", [System.StringComparison]::Ordinal)) {
            $currentPath = $line.Substring("worktree ".Length).Trim()
        }
        elseif ($line.StartsWith("HEAD ", [System.StringComparison]::Ordinal)) {
            $currentHead = $line.Substring("HEAD ".Length).Trim()
        }
        elseif ($line.StartsWith("branch ", [System.StringComparison]::Ordinal)) {
            $ref = $line.Substring("branch ".Length).Trim()
            $currentBranch = if ($ref.StartsWith("refs/heads/", [System.StringComparison]::Ordinal)) {
                $ref.Substring("refs/heads/".Length)
            }
            else {
                $ref
            }
        }
        elseif ($line -eq "detached") {
            $currentBranch = "(detached)"
        }
    }

    if ($null -ne $currentPath) {
        $items.Add([pscustomobject]@{
            Path   = $currentPath
            Head   = $currentHead
            Branch = $currentBranch
        })
    }

    return $items
}

function Get-RepoPrTruth {
    param([string]$RepoSlug)
    $openBranches = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $mergedBranches = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $mergedHeadOids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $mergedPrNumbers = [System.Collections.Generic.HashSet[int]]::new()

    try {
        $openRaw = & gh pr list --repo $RepoSlug --state open --limit 50 --json number,headRefName 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($openRaw)) {
            $openList = $openRaw | ConvertFrom-Json
            foreach ($pr in @($openList)) {
                if ($pr.headRefName) { [void]$openBranches.Add("$($pr.headRefName)") }
            }
        }
    }
    catch { }

    try {
        $mergedRaw = & gh pr list --repo $RepoSlug --state merged --limit 300 --json number,headRefName,headRefOid 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($mergedRaw)) {
            $mergedList = $mergedRaw | ConvertFrom-Json
            foreach ($pr in @($mergedList)) {
                if ($pr.headRefName) { [void]$mergedBranches.Add("$($pr.headRefName)") }
                if ($pr.headRefOid) { [void]$mergedHeadOids.Add("$($pr.headRefOid)") }
                if ($pr.number) { [void]$mergedPrNumbers.Add([int]$pr.number) }
            }
        }
    }
    catch { }

    return [pscustomobject]@{
        OpenBranches    = $openBranches
        MergedBranches  = $mergedBranches
        MergedHeadOids  = $mergedHeadOids
        MergedPrNumbers = $mergedPrNumbers
    }
}

$githubOnline = Test-GitHubReachable
$syncNotes = [System.Collections.Generic.List[string]]::new()

if (-not $DryRun) {
    foreach ($repo in @($MobaFlowPath, $MobaDevPath)) {
        if (Test-Path $repo) {
            if ($githubOnline) {
                $null = Invoke-GitSafe -RepoPath $repo -GitArgs @("fetch", "-4", "origin", "--prune")
                if ($LASTEXITCODE -eq 0) {
                    $syncNotes.Add("Fetched & pruned origin in $repo")
                }
            }
            $null = Invoke-GitSafe -RepoPath $repo -GitArgs @("worktree", "prune")
        }
    }
}

$flowTruth = if ($githubOnline) {
    Get-RepoPrTruth -RepoSlug "ahuelsmann/MOBAflow"
}
else {
    [pscustomobject]@{
        OpenBranches    = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        MergedBranches  = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        MergedHeadOids  = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        MergedPrNumbers = [System.Collections.Generic.HashSet[int]]::new()
    }
}

$devTruth = if ($githubOnline) {
    Get-RepoPrTruth -RepoSlug "ahuelsmann/MOBAdev"
}
else {
    [pscustomobject]@{
        OpenBranches    = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        MergedBranches  = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        MergedHeadOids  = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        MergedPrNumbers = [System.Collections.Generic.HashSet[int]]::new()
    }
}

# 1. Lossless Worktree Prune (MOBAflow)
$removedWorktrees = [System.Collections.Generic.List[object]]::new()
$preservedDirty = [System.Collections.Generic.List[object]]::new()
$preservedUnmerged = [System.Collections.Generic.List[object]]::new()
$preservedActive = [System.Collections.Generic.List[object]]::new()

if (Test-Path $MobaFlowPath) {
    $normFlowRoot = ($MobaFlowPath -replace '\\', '/').TrimEnd('/')
    $wtPorcelain = Invoke-GitSafe -RepoPath $MobaFlowPath -GitArgs @("worktree", "list", "--porcelain")
    $worktrees = Parse-WorktreePorcelain -Lines $wtPorcelain.Lines

    foreach ($wt in $worktrees) {
        $normPath = ($wt.Path -replace '\\', '/').TrimEnd('/')
        if ($normPath -ieq $normFlowRoot -or
            $wt.Branch -ieq "main" -or
            $wt.Branch -ieq "claude/review-from-start" -or
            $flowTruth.OpenBranches.Contains($wt.Branch)) {
            $preservedActive.Add([pscustomobject]@{ Path = $wt.Path; Branch = $wt.Branch; Reason = "standing-or-open-pr" })
            continue
        }

        if (-not (Test-Path $wt.Path)) {
            $preservedUnmerged.Add([pscustomobject]@{ Path = $wt.Path; Branch = $wt.Branch; Reason = "missing-on-disk" })
            continue
        }

        # Hard Dirty Guard
        $statusRes = Invoke-GitSafe -RepoPath $wt.Path -GitArgs @("status", "--porcelain")
        $nonEmptyStatus = @($statusRes.Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($statusRes.ExitCode -ne 0 -or $nonEmptyStatus.Count -gt 0) {
            $preservedDirty.Add([pscustomobject]@{
                Path       = $wt.Path
                Branch     = $wt.Branch
                DirtyCount = $nonEmptyStatus.Count
            })
            continue
        }

        # Hard Merged Verification (clean worktrees only)
        $isMergedSafe = $false
        $reason = ""

        $null = Invoke-GitSafe -RepoPath $MobaFlowPath -GitArgs @("merge-base", "--is-ancestor", $wt.Head, "origin/main")
        if ($LASTEXITCODE -eq 0) {
            $isMergedSafe = $true
            $reason = "ancestor-of-origin/main"
        }
        elseif ($wt.Branch -ne "(detached)" -and
            ($flowTruth.MergedBranches.Contains($wt.Branch) -or $flowTruth.MergedHeadOids.Contains($wt.Head))) {
            $isMergedSafe = $true
            $reason = "merged-pr-match"
        }
        elseif ($wt.Branch -match '^(?:pr|wt-?)(?<num>\d+)[a-z]?$' -and
            $flowTruth.MergedPrNumbers.Contains([int]$Matches['num'])) {
            $isMergedSafe = $true
            $reason = "merged-pr-tag-#$($Matches['num'])"
        }
        else {
            $mbRes = Invoke-GitSafe -RepoPath $MobaFlowPath -GitArgs @("merge-base", $wt.Head, "origin/main")
            $mb = ($mbRes.Lines | Select-Object -First 1)
            if ($mbRes.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($mb)) {
                $changedRes = Invoke-GitSafe -RepoPath $MobaFlowPath -GitArgs @("diff", "--name-only", "$mb..$($wt.Head)")
                $changedFiles = @($changedRes.Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                if ($changedFiles.Count -gt 0) {
                    $diffArgs = @("diff", $wt.Head, "origin/main", "--") + $changedFiles
                    $diffRes = Invoke-GitSafe -RepoPath $MobaFlowPath -GitArgs $diffArgs
                    if ($diffRes.ExitCode -eq 0 -and @($diffRes.Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -eq 0) {
                        $isMergedSafe = $true
                        $reason = "squash-content-identical-to-origin/main"
                    }
                }
            }
        }

        if ($isMergedSafe) {
            $removedOk = $true
            if (-not $DryRun) {
                $rmRes = Invoke-GitSafe -RepoPath $MobaFlowPath -GitArgs @("worktree", "remove", $wt.Path)
                if ($rmRes.ExitCode -ne 0) {
                    $removedOk = $false
                    $preservedUnmerged.Add([pscustomobject]@{
                        Path   = $wt.Path
                        Branch = $wt.Branch
                        Reason = "worktree-remove-failed: $($rmRes.Lines -join ' ')"
                    })
                }
            }

            if ($removedOk) {
                $removedWorktrees.Add([pscustomobject]@{
                    Path   = $wt.Path
                    Branch = $wt.Branch
                    Head   = if ($wt.Head.Length -gt 8) { $wt.Head.Substring(0, 8) } else { $wt.Head }
                    Reason = $reason
                    DryRun = [bool]$DryRun
                })
            }
        }
        else {
            $preservedUnmerged.Add([pscustomobject]@{
                Path   = $wt.Path
                Branch = $wt.Branch
                Reason = "unmerged-commits-differ-from-main"
            })
        }
    }
}

# 2. Lossless Local Branch Prune (MOBAflow & MOBAdev)
function Prune-LocalBranches {
    param(
        [string]$RepoPath,
        [string]$RepoLabel,
        $PrTruth,
        [bool]$AllowPrNumberTagPattern
    )

    $deleted = [System.Collections.Generic.List[object]]::new()
    $kept = [System.Collections.Generic.List[object]]::new()

    if (-not (Test-Path $RepoPath)) {
        return [pscustomobject]@{ Deleted = $deleted; Kept = $kept }
    }

    $checkedOut = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $wtList = Parse-WorktreePorcelain -Lines (Invoke-GitSafe -RepoPath $RepoPath -GitArgs @("worktree", "list", "--porcelain")).Lines
    foreach ($w in $wtList) {
        if ($w.Branch -and $w.Branch -ne "(detached)") {
            [void]$checkedOut.Add($w.Branch)
        }
    }

    $headBranchRes = Invoke-GitSafe -RepoPath $RepoPath -GitArgs @("rev-parse", "--abbrev-ref", "HEAD")
    $headBranch = ($headBranchRes.Lines | Select-Object -First 1)
    if ($headBranch -and $headBranch -ne "HEAD") {
        [void]$checkedOut.Add($headBranch)
    }

    $refsRes = Invoke-GitSafe -RepoPath $RepoPath -GitArgs @("for-each-ref", "--format=%(refname:short)|%(objectname)", "refs/heads")
    foreach ($rawLine in $refsRes.Lines) {
        if ([string]::IsNullOrWhiteSpace($rawLine)) { continue }
        $parts = $rawLine.Split('|', 2)
        $branchName = $parts[0].Trim()
        $oid = if ($parts.Length -gt 1) { $parts[1].Trim() } else { "" }
        if ([string]::IsNullOrWhiteSpace($branchName)) { continue }

        if ($branchName -ieq "main" -or
            $branchName -ieq "claude/review-from-start" -or
            $PrTruth.OpenBranches.Contains($branchName) -or
            $checkedOut.Contains($branchName)) {
            $kept.Add([pscustomobject]@{ Repository = $RepoLabel; Branch = $branchName; Reason = "protected-or-checked-out" })
            continue
        }

        $isMergedSafe = $false
        $reason = ""
        if (-not [string]::IsNullOrWhiteSpace($oid)) {
            $null = Invoke-GitSafe -RepoPath $RepoPath -GitArgs @("merge-base", "--is-ancestor", $oid, "origin/main")
            if ($LASTEXITCODE -eq 0) {
                $isMergedSafe = $true
                $reason = "ancestor-of-origin/main"
            }
        }

        if (-not $isMergedSafe -and ($PrTruth.MergedBranches.Contains($branchName) -or (-not [string]::IsNullOrWhiteSpace($oid) -and $PrTruth.MergedHeadOids.Contains($oid)))) {
            $isMergedSafe = $true
            $reason = "merged-pr-match"
        }

        if (-not $isMergedSafe -and $AllowPrNumberTagPattern -and
            $branchName -match '^(?:pr|wt-?)(?<num>\d+)[a-z]?$' -and
            $PrTruth.MergedPrNumbers.Contains([int]$Matches['num'])) {
            $isMergedSafe = $true
            $reason = "merged-pr-tag-#$($Matches['num'])"
        }

        if ($isMergedSafe) {
            if (-not $DryRun) {
                $delRes = Invoke-GitSafe -RepoPath $RepoPath -GitArgs @("branch", "-D", $branchName)
                if ($delRes.ExitCode -ne 0) {
                    $kept.Add([pscustomobject]@{ Repository = $RepoLabel; Branch = $branchName; Reason = "delete-failed: $($delRes.Lines -join ' ')" })
                    continue
                }
            }
            $deleted.Add([pscustomobject]@{ Repository = $RepoLabel; Branch = $branchName; Reason = $reason; DryRun = [bool]$DryRun })
        }
        else {
            $kept.Add([pscustomobject]@{ Repository = $RepoLabel; Branch = $branchName; Reason = "unmerged-local-branch" })
        }
    }

    return [pscustomobject]@{ Deleted = $deleted; Kept = $kept }
}

$flowBranchResult = Prune-LocalBranches -RepoPath $MobaFlowPath -RepoLabel "MOBAflow" -PrTruth $flowTruth -AllowPrNumberTagPattern $true
$devBranchResult = Prune-LocalBranches -RepoPath $MobaDevPath -RepoLabel "MOBAdev" -PrTruth $devTruth -AllowPrNumberTagPattern $false

$allDeletedBranches = @($flowBranchResult.Deleted) + @($devBranchResult.Deleted)
$allKeptBranches = @($flowBranchResult.Kept) + @($devBranchResult.Kept)

$summary = [pscustomobject]@{
    GeneratedAtUtc         = [DateTimeOffset]::UtcNow.ToString("O")
    DryRun                 = [bool]$DryRun
    GitHubReachable        = [bool]$githubOnline
    SyncNotes              = @($syncNotes)
    RemovedWorktreesCount  = $removedWorktrees.Count
    RemovedWorktrees       = @($removedWorktrees)
    PreservedDirtyCount    = $preservedDirty.Count
    PreservedDirty         = @($preservedDirty)
    PreservedUnmergedCount = $preservedUnmerged.Count
    PreservedUnmerged      = @($preservedUnmerged)
    PreservedActiveCount   = $preservedActive.Count
    DeletedBranchesCount   = $allDeletedBranches.Count
    DeletedBranches        = @($allDeletedBranches)
    KeptBranchesCount      = $allKeptBranches.Count
    KeptBranches           = @($allKeptBranches)
}

if ($Json) {
    $summary | ConvertTo-Json -Depth 6
}
else {
    $modeLabel = if ($DryRun) { "DRY-RUN" } else { "EXECUTED" }
    Write-Host "=== MOBAi Repo Hygiene ($modeLabel) ==="
    Write-Host "Worktrees removed : $($summary.RemovedWorktreesCount) | Preserved dirty: $($summary.PreservedDirtyCount) | Preserved unmerged: $($summary.PreservedUnmergedCount) | Active/main: $($summary.PreservedActiveCount)"
    Write-Host "Local branches del: $($summary.DeletedBranchesCount) | Preserved branches: $($summary.KeptBranchesCount)"
    if ($summary.PreservedDirtyCount -gt 0) {
        Write-Host "`nPreserved Dirty Worktrees (untouched):"
        $summary.PreservedDirty | ForEach-Object { Write-Host "  [DIRTY=$($_.DirtyCount)] $($_.Path) ($($_.Branch))" }
    }
    if ($summary.RemovedWorktreesCount -gt 0) {
        Write-Host "`nWorktrees ($(if ($DryRun) { 'would remove' } else { 'removed' })):"
        $summary.RemovedWorktrees | ForEach-Object { Write-Host "  [$($_.Reason)] $($_.Path) ($($_.Branch))" }
    }
    if ($summary.DeletedBranchesCount -gt 0) {
        Write-Host "`nLocal branches ($(if ($DryRun) { 'would delete' } else { 'deleted' })):"
        $summary.DeletedBranches | ForEach-Object { Write-Host "  [$($_.Repository)] $($_.Branch) ($($_.Reason))" }
    }
}
