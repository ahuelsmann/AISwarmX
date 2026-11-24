# Hygiene & Zero-Token Automation (`MOBAflow` / `MOBAdev`)

Operational automation scripts live in `scripts/` inside `MOBAdev` and consume **zero LLM tokens** during routine checks, repository cleanup, and Kanban self-healing.

---

## 1. Lossless Git Worktree & Branch Hygiene (`scripts/Invoke-RepoHygiene.ps1`)

Safely removes only **clean, provably merged/redundant** Git worktrees and local branches across `MOBAflow` (`C:\Repo\ahuelsmann\MOBAflow`) and `MOBAdev` (`C:\Repo\ahuelsmann\MOBAdev`).

### Safety Guarantees
- **Hard Dirty Guard:** Runs `git -C <worktree> status --porcelain` on every non-`main` worktree. Worktrees with any uncommitted edits or untracked files (such as prototype sandboxes) are recorded under `PreservedDirty` and are **never** removed.
- **Hard Merged Verification:** Only removes a clean worktree or local branch when at least one lossless proof holds:
  1. Commit is already an ancestor of `origin/main` (`git merge-base --is-ancestor`).
  2. Branch name or commit SHA matches a merged GitHub PR (`gh pr list --state merged`).
  3. Branch tag (`pr<N>` / `wt-<N>`) corresponds to a merged PR.
  4. All files modified relative to `origin/main` have zero remaining diff against `origin/main` (`squash-content-identical-to-origin/main`).
- **Protected Refs:** `main`, `claude/review-from-start`, branches backing open PRs, and branches currently checked out in any remaining worktree are always preserved. Never deletes remote GitHub branches (`git push --delete` is never invoked).

### Usage

```powershell
# Preview safely without modifying anything
pwsh -NoProfile -File C:\Repo\ahuelsmann\MOBAdev\scripts\Invoke-RepoHygiene.ps1 -DryRun

# Execute lossless cleanup
pwsh -NoProfile -File C:\Repo\ahuelsmann\MOBAdev\scripts\Invoke-RepoHygiene.ps1

# Emit machine-readable JSON
pwsh -NoProfile -File C:\Repo\ahuelsmann\MOBAdev\scripts\Invoke-RepoHygiene.ps1 -Json
```

---

## 2. Zero-Token Swarm Watchdog (`scripts/Invoke-SwarmWatchdog.ps1`)

Checks live readiness and board synchronization via `gh` CLI and the read-only local Dashboard API (`http://127.0.0.1:5179`) without spending Claude/Codex/Gemini tokens.

### Capabilities
- **Auto Repo Hygiene:** Runs `Invoke-RepoHygiene.ps1 -Json` (disable with `-SkipHygiene`).
- **Kanban Self-Healing (`-AutoHealBoard`):** Moves any `CLOSED` Issue or `MERGED`/`CLOSED` Pull Request still stuck in `Ready`, `In progress`, or `In review` on GitHub Project #1 directly to `Done`.
- **Actionable Readiness Signals:**
  - `QuietWindowOpenForRf30`: True when `OpenCodePrCount == 0` and Issue `#452` (`RF-30`) is open in `In progress`.
  - `FastPathOrReviewCandidates`: Lists open PRs whose CI/Sonar checks are all green (`BEHIND` or `Draft`) ready for `Disjoint main-Sync Fast-Path` promotion/auto-merge.
  - `ReadyIntakeAvailable` / `ReadyColumnOverloaded`: Flags whether Code WIP (`< 3`) has capacity and warns when `Ready` exceeds the target `2–3` issues.
- **Exact Kickoff Guidance:** Prints the recommended Root Head owner (honoring `MOBAi Claude Opus` quota pause failover) and ready-to-paste kickoff prompt.

### Usage

```powershell
# Run once with Kanban self-healing
pwsh -NoProfile -File C:\Repo\ahuelsmann\MOBAdev\scripts\Invoke-SwarmWatchdog.ps1 -AutoHealBoard

# Optional: register a local Windows Scheduled Task (runs every 30 min, hidden, zero LLM tokens)
pwsh -NoProfile -File C:\Repo\ahuelsmann\MOBAdev\scripts\Invoke-SwarmWatchdog.ps1 -RegisterScheduledTask -IntervalMinutes 30

# Optional: remove the Scheduled Task
pwsh -NoProfile -File C:\Repo\ahuelsmann\MOBAdev\scripts\Invoke-SwarmWatchdog.ps1 -UnregisterScheduledTask
```
