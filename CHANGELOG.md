# Changelog

All notable changes to **MOBAdev** and the local **MOBAdev Agent Swarm Gen#3 Dashboard** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/) within **Gen#3** (`3.0.0+`; earlier MOBAflow AI setups predate this repository as Gen#1 and Gen#2).

## [3.14.0] - 2026-11-26

### Added & Changed
- **Swarm Bus Work Queue, Review Claims & State Deduplication (`bus/mobai-swarm-bus.mjs`, `bus/mobai-swarm-bus.test.mjs`):**
  - Added transactional (`BEGIN IMMEDIATE`) additive SQLite schema migrations (`messages.state`, `messages.wake_ok`, `messages.wake_state`, `messages.wake_attempt_expires_at_utc`, `review_claims`, `work_queue`, `meta`) preserving existing data under concurrent startup.
  - Added `bus_review_claim` (MCP + `--cli review-claim`) enforcing canonical 40-char lowercase hex `head_sha` and single-holder lease lifecycle (`action: 'claim' | 'complete' | 'release' | 'list'`, configurable `ttl_minutes` defaulting `60`) per `(pr_number, head_sha, role)`.
  - Added `bus_queue` (MCP + `--cli queue`) supporting `list`, `next` (transactional resume-first + 4h lease renewal, expired lease takeover, priority/oldest order, `effectiveCodeWip = code_wip + activeClaimedCodeCount >= 2` `L`-only gate treating unclassified items as code), `release`, `done`, `upsert`, and `sync-meta`.
  - Added atomic recipient-scoped `state` deduplication and `wake_state` (`'pending'|'ok'|'failed'`) + `wake_attempt_expires_at_utc` (`120s`) atomic wake reservation on `bus_send` (`--codex-thread-id` support included).
- **Watchdog Queue Sync & Auto-Wake Events (`scripts/Invoke-SwarmWatchdog.ps1`):**
  - Added `-SyncQueue` (also implied by `-DispatchEvents` and `-RegisterScheduledTask`) with full GraphQL cursor pagination (`pageInfo`), explicit `$boardReadSucceeded` fail-safe guard (preserving existing `work_queue` and `meta.code_wip` on API/GraphQL errors), Project #1 Priority/Risk schema verification, `work_queue` synchronization (preserving `'claimed'` leases through `In progress` until a linked open PR or terminal `Closed`/`Done`), and file-based `meta.code_wip` calculation.
  - Added deduplicated `PrMerged` (`NextWorkAvailable`) and `HeadIdleWake` (forwarding `-CodexThreadId` via `--codex-thread-id` and `MOBADEV_CODEX_THREAD_ID`, waking Codex when idle `> 30m` and not `rate_limited` while `code_wip < 3` and queued work exists).
- **Governance & Review Marker Standardization (`agents/`, `docs/delivery-workflow.md`, `docs/hygiene-and-automation.md`):** Documented cross-family final review (`claude`/`openai`/`gemini`), non-orchestrator dispatcher rule, `<!-- moba-final-review ... base=<40-char sha> ... -->` PR comment marker, `bus_review_claim` lifecycle (`claim`/`complete`/`release`/`list`, `ttl_minutes`), `bus_send` `state` deduplication, and session-start `bus_queue next` resume flow.

---

## [3.13.0] - 2026-11-26

### Added & Changed
- **Monitor `idle` State (`DashboardOptions`, `MonitorStateRules`, `AntigravityMonitorProvider`, `ClaudeMonitorProvider`, `DashboardSnapshotService`, Dashboard Web):**
  - Added configurable `BusyIdleAfterMinutes` (`10` minutes default, normalized to `>= 1`) in `DashboardOptions.cs` and `appsettings.json`, plus `MonitorStateRules.ApplyIdle(...)` to transition stale `"busy"` monitors (no `lastActivityUtc` or elapsed time `> threshold`) to `"idle"` for Antigravity and Claude while keeping Codex's existing `90s` `BusyThreshold` unchanged.
  - Counted `"idle"` monitors as online/ready (and NOT busy) across `DashboardSnapshotService.ComputeActiveAgentsSummary`, `ActiveAgentsStrip`, `DecisionTriadCard`, and `MonitorCards`.
  - Rendered `"idle"` monitors with neutral/subtle badge styling (`Idle · no activity for N min` / `Idle · inactive` in `MonitorCards.tsx`, `Idle` in `ArchitectureDiagramsView.tsx` and `DecisionTriadCard.tsx`), covered by new API (`MonitorProvidersTests.cs`) and Web (`App.test.tsx`) unit tests.

---

## [3.12.1] - 2026-11-26

### Fixed
- **Swarm Bus Unread Hygiene (`bus/mobai-swarm-bus.mjs`, `bus/mobai-swarm-bus.test.mjs`):**
  - Auto-marked messages sent to headless senders (`watchdog`, `claude-opus-worker`, `system`) as `acked` (`auto-headless`) so `bus_ack` courtesy replies never linger unread in the Communication inbox.
  - Auto-superseded older `unread` Watchdog alerts for the same `pr_number` when a newer Watchdog status event arrives for that PR.
  - Fixed `bus_inbox` with `agent: "all"` (or `"*"`) to aggregate unread/recent messages across all recipients instead of filtering strictly to literal `recipient = 'all'`.

---

## [3.12.0] - 2026-11-25

### Added
- **Main Landing Queue Card (`MainLandingTickerCard.tsx`, `App.tsx`, `App.test.tsx`):** Added an ordered **`Main Landing Queue`** arrival board (`Variante A`) to `Live Operations` that deterministically ranks open features by proximity to `main` (`#1 Auto-Merge · Ready` -> `Auto-Merge · Running` -> `Ready · Checks Green` -> `Draft · Checks Green` -> `CI / Review Running` -> `Action Needed`) alongside a **`Just landed on main`** strip of recently merged PRs.

---

## [3.11.3] - 2026-11-24

### Fixed & Changed
- **Swarm Bus `Confirmed` Label & Lock Expiry Countdown (`SwarmBusCard.tsx`, `App.test.tsx`):** Replaced `Acked` / `acked` with clearer **`Confirmed`** / **`confirmed`** status wording in the **Communication** tab, and added `formatRemainingUtc` so future cross-tool lock expiries render as remaining time (e.g. `(exp in 1h 59m)`) instead of `(exp 0s ago)`.

---

## [3.11.2] - 2026-11-24

### Added & Changed
- **Cross-Tool Swarm Bus Instruction Rollout (`~/.codex/AGENTS.md`, `~/.claude/CLAUDE.md`, `manifest.md`, `agents/`):** Documented `mobai-swarm-bus` (`bus_send`, `bus_inbox`, `bus_ack`, `bus_locks`, `bus_status`) across global Codex & Claude instructions, `MOBAdev/manifest.md`, and all synced Claude & Antigravity agent definitions (`0` Governance drift).

---

## [3.11.1] - 2026-11-24

### Added
- **Claude Code Swarm Bus Hook (`bus/claude-swarm-bus-hook.mjs`, `~/.claude/settings.json`):** Added a read-only `SessionStart` and `UserPromptSubmit` hook that automatically surfaces unread `MOBAi Swarm Bus` messages (`claude-opus`/`all`) and active foreign path locks into Claude's prompt context while emitting zero tokens when idle, plus pre-approved `mcp__mobai-swarm-bus__*` permissions.

### Changed
- **Dedicated `Communication` Dashboard Tab (`StatusHeader.tsx`, `SwarmBusCard.tsx`, `App.tsx`):** Moved **Swarm Bus · Cross-Tool MCP** out of `Live Operations` onto its own dedicated **`Communication`** tab page with an unread counter badge in the header, status filters (`All` / `Unread` / `Acked`), full message list, and active cross-tool path locks strip.

---

## [3.11.0] - 2026-11-24

### Added
- **Cross-Tool `MOBAi Swarm Bus` (`bus/mobai-swarm-bus.mjs`, `bus/mobai-swarm-bus.test.mjs`):** Added a local WAL-mode SQLite MCP stdio server & zero-token CLI (`bus_send`, `bus_inbox`, `bus_ack`, `bus_locks`, `bus_status`) shared across Claude Code/Desktop, ChatGPT Codex (`codex.exe queue` wake support), Antigravity, and `Invoke-SwarmWatchdog.ps1`, enabling cross-tool messaging, threaded acknowledgments, and overlapping path-lock collision prevention.
- **Ephemeral Claude CLI Worker — Option A (`scripts/Invoke-ClaudeEphemeralWorker.ps1`):** Added `claude.exe --print --no-session-persistence` wrapper with `Review` (`plan` read-only) and `ScopedFix` (worktree-only + busy-Desktop guard) modes, strict allowed-model enforcement, and audit logging to `%LOCALAPPDATA%\MOBAdev\claude-workers.log`.
- **Dashboard Read-Only Swarm Bus Feed (`GET /api/bus`, `SwarmBusProvider.cs`, `SwarmBusCard.tsx`):** Added read-only SQLite bus/lock inspection to `MobaDev.Dashboard.Api` (`v3.11.0`) and a live **Swarm Bus · Cross-Tool MCP** card to the Dashboard Live view.

---

## [3.10.0] - 2026-11-24

### Added
- **Event-Driven Zero-Token Swarm Watchdog (`scripts/Invoke-SwarmWatchdog.ps1`, `docs/hygiene-and-automation.md`):** Added deduplicated event detection & dispatch (`-DispatchEvents`, `-ResetEventState`, `-IntervalMinutes 10`) persisted locally in `%LOCALAPPDATA%\MOBAdev\watchdog-state.json` (24h retention) and `%LOCALAPPDATA%\MOBAdev\watchdog-alerts.log`, routing `CiOrSonarFailed` and `AllChecksGreenReady` events automatically to `codex.exe queue` for `codex/*` PRs and logging Claude/Antigravity alerts without burning LLM tokens.
- **3-World Parallel Execution & Plan-Slice Fast-Start (`manifest.md`, `docs/delivery-workflow.md`, `docs/claude-ready-intake-task.md`, `agents/antigravity/rules/mobaflow-agent-network.md`):** Established the **GitHub-as-Mailbox Rule** (no stranded local-chat handoffs), **Pre-Approved Plan-Slice Fast-Start (`Class M`)** (direct Draft PR TDD from approved Spec Kit plans), and **Multi-Tool Parallel Intake** across `Claude`, `Codex`, and `Antigravity` on disjoint paths (keeping `~/.gemini/config/rules/` at `0` drift).

---

## [3.9.10] - 2026-11-24

### Fixed
- **Job-Object-Independent Detached Dashboard Auto-Start (`dashboard/Start-Dashboard.ps1`):** Configured `MOBAdev-Dashboard-AutoStart` to invoke `MobaDev.Dashboard.Api.exe` directly with unlimited execution time (`ExecutionTimeLimit = 0`), and updated `-Background` to launch via `Start-ScheduledTask` (with `Win32_Process.Create` fallback) so `:5179` survives Windows Job Object closes when agent shells exit as well as Antigravity server restarts.

---

## [3.9.9] - 2026-11-24

### Added
- **Detached Dashboard Auto-Start (`dashboard/Start-Dashboard.ps1`, `agents/antigravity/rules/mobaflow-agent-network.md`):** Added `-Background` (idempotent detached launch with `/api/health` readiness probe so Antigravity server restarts do not terminate `:5179`), `-RegisterAutoStart`, and `-UnregisterAutoStart` (`MOBAdev-Dashboard-AutoStart` `AtLogOn` Windows Scheduled Task), plus synced Antigravity startup rule and documentation ([docs/hygiene-and-automation.md](docs/hygiene-and-automation.md), [README.md](README.md), [dashboard/DESIGN.md](dashboard/DESIGN.md)).

---

## [3.9.8] - 2026-11-24

### Changed
- **Dynamic Quorum / Quota-Pause Context Label (`MonitorCards.tsx`, `DecisionTriadCard.tsx`, `App.test.tsx`):** Replaced the hardcoded `(Antigravity + Codex Failover)` Quorum suffix with `formatQuotaPauseSuffix(monitors)` so the badge reflects the actual active failover pool (for example, `(Claude + Codex + Gemini Active)` when only `MOBAi Antigravity Opus` partner credits are paused, `(Codex + Gemini Failover)` when both Opus pools pause, or `(Antigravity + Codex Failover)` when `MOBAi Claude Opus` pauses).

---

## [3.9.7] - 2026-11-24

### Changed
- **Clean Empty 2×3 Monitor Grid Cells (`MonitorCards.tsx`, `App.test.tsx`):** Removed the visible placeholder box, dashed border, and `Empty slot · Row X, Col Y` / `Free 2×3 slot` text from unoccupied 2×3 Monitor Grid cells in idle state so free slots stay visually clean and empty, while still revealing drop/place targets on demand during drag-and-drop or click-to-move actions.

---

## [3.9.6] - 2026-11-24

### Added
- **Zero-Token Hygiene & Swarm Watchdog Scripts (`scripts/Invoke-RepoHygiene.ps1`, `scripts/Invoke-SwarmWatchdog.ps1`, `docs/hygiene-and-automation.md`):** Added lossless Git worktree/local-branch cleanup (with hard dirty guard + PR/ancestor/squash-diff verification) and a zero-token `gh`/Dashboard watchdog with `-DryRun`, `-AutoHealBoard` Kanban self-healing, `QuietWindowOpenForRf30` & `FastPathOrReviewCandidates` detection, and optional `-RegisterScheduledTask`.
- **Antigravity Partner Quota Pause Detection (`AntigravityMonitorProvider.cs`):** Added dual-signal read-only quota detection for Antigravity Root Heads (`transcript_full.jsonl` / `transcript.jsonl` `swarm` model quota snapshots + `conversations/<id>.db` `steps` HTTP `429` `RESOURCE_EXHAUSTED` fallback), surfacing `rate_limited` (`Quota Pause`) and local reset timestamps directly on `MOBAi Antigravity Opus` when partner credits are `0%` while `Gemini 4 Carbon`/`Hydrogen` remain active.
- **Dirty-Aware Worktree Preservation & Quick Prune Command (`WorktreeProvider.cs`, `DashboardSnapshotService.cs`, `DashboardContracts.cs`, `HygieneAndSyncPanel.tsx`):** Detects uncommitted worktrees (`IsDirty`) via bounded `git status --porcelain`, badges them `Dirty · Preserved` in the Dashboard UI, excludes intentionally preserved dirty prototype worktrees from `StaleWorktreeCount`/`WarningCount`, and adds a read-only `Copy Safe Prune Cmd` clipboard action when stale worktrees or branches remain.

---

## [3.9.5] - 2026-11-24

### Changed
- **Expand Allowlist to 8 Approved Models (`docs/allowed-models.md`, `manifest.md`, `docs/delivery-workflow.md`, `DashboardContracts.cs`, `ActiveAgentsStrip.tsx`, `RoleAndModelIcons.tsx`, `MonitorCards.tsx`):** Added **`GPT-6.2 Astra`** (`gpt-6.2-astra`) as Codex's quota-efficient workhorse sub-agent alongside `GPT-6.2 Sol` (noting `codex-auto-review` as Codex's internal Guardian model) and **`Claude Haiku 4.5`** (`claude-haiku-4-5`) strictly for read-only repository search / `Explore` triage in `MOBAi Claude Opus` (and excluded from `MOBAi Antigravity Opus`), plus bidirectional `Opus 5.5` quota-failover guidance between Claude Code and Antigravity.

---

## [3.9.4] - 2026-11-24

### Changed
- **Single-Line Sub-Agent Chip Labels (`MonitorCards.tsx`):** Shortened `Gemini 4 Carbon` and `Gemini 4 Hydrogen` chip labels to `Carbon` and `Hydrogen` (retaining full names in tooltips and identity tones) so all three chips (`Carbon: 0`, `Hydrogen: 0`, `GPT-OSS 120B: 0`) fit cleanly on a single line inside `MOBAi Antigravity Gemini`.
- **Streamlined Issue Card Header (`PipelineCard.tsx`):** Combined `Area`, `IssueType`, inline `Locked Areas` pills, optional `Writer` pill (omitted when Round Table participants already exist), and right-aligned relative `Updated` timestamp into a single compact metadata row (reducing the header from 4 rows to 2 rows), while normalizing legacy `Kanban` `Gemini 4 Argon` entries to `Gemini 4 Carbon`.

---

## [3.9.3] - 2026-11-24

### Changed
- **Variant A Card-Body Sub-Agent Counters & Unified Triad Summary (`MonitorCards.tsx`, `ActiveAgentsStrip.tsx`, `App.tsx`, `DashboardContracts.cs`):** Dissolved the standalone top `<ActiveAgentsStrip />` bar, moved the compact `{N} Busy / Recent` badge into the `Decision & Steering Triad` header row, relocated per-model sub-agent counter chips (`GPT-6.2 Sol`, `Opus 5.5`, `Sonnet 5.5`, `Gemini 4 Carbon`, `Gemini 4 Hydrogen`, `GPT-OSS 120B`) directly inside each Master Session card body tailored to that tool's allowed model pool from `docs/allowed-models.md`, and removed the redundant generic `Sub-agents: N` footer badge.
- **Exact Antigravity Sub-Agent Parent Attribution & `GPT-OSS 120B` Recognition (`AntigravityMonitorProvider.cs`, `MonitorProvidersTests.cs`):** Inspects `invoke_subagent` `conversationId` references in each Antigravity Root Head's transcript tail so spawned sub-agents credit the exact parent session (`MOBAi Antigravity Gemini` vs. `MOBAi Antigravity Opus`), and recognizes `gpt-oss-120b` (`GPT-OSS 120B`) in both transcript formatting and `SubAgentModelCatalog`.

---

## [3.9.2] - 2026-11-24

### Changed
- **Disjoint `main`-Sync Fast-Path & Default `Opus 5.5` Quota-Splitting (`manifest.md`, `docs/delivery-workflow.md`, `docs/ai-strategy-and-pilots.md`, `docs/allowed-models.md`):** Eliminated redundant LLM re-reviews after pure `origin/main` merges when `LockedAreas` and slice diffs are untouched (`0` conflict edits in slice files), defaulted `Fresh Independent P1 Final Reviews` to Antigravity's separate `Opus 5.5` quota pool (Google AI plan) so `MOBAi Claude Opus` stays shielded for COO steering, aligned docs to the 4 `MOBAi` Root Heads, and standardized a focused `2–3` slice `Ready` queue.
- **Open PR Deduplication in `Task & PR Pipeline -> Open Issues` (`PipelineProvider.cs`, `PipelineAndWorktreeProviderTests.cs`):** Excluded non-drifting `OPEN` `PullRequest` cards from `ActiveIssues` (while keeping `MERGED`/`CLOSED` PRs on the Kanban board surfaced with `BoardDriftReason`) so `Open Issues` reflects actual issues rather than duplicating `Open PRs`.

---

## [3.9.1] - 2026-11-24

### Added
- **Allowed AI Models Catalog (`docs/allowed-models.md`, `README.md`):** Added the binding 6-model allowlist (`GPT-6.2 Sol`, `Opus 5.5`, `Sonnet 5.5`, `Gemini 4 Carbon`, `Gemini 4 Hydrogen`, `GPT-OSS 120B`) with standing `MOBAi` Root Head mappings and explicit prohibitions (`Claude Fable 5.2` and unlisted/superseded models), linked from `README.md` and discoverable under Dashboard `Configuration`.

---

## [3.9.0] - 2026-11-24

### Changed
- **`MOBAi` Root Head Naming (Variant B) & Multi-Project Namespace Filtering (`CodexMonitorProvider.cs`, `ClaudeMonitorProvider.cs`, `AntigravityMonitorProvider.cs`, `DashboardContracts.cs`, Frontend Components):** Standardized the 4 standing Root Head titles to `MOBAi ChatGPT Codex` (`codex-monitor`), `MOBAi Claude Opus` (`claude-monitor`), `MOBAi Antigravity Gemini` (`antigravity-monitor`), and `MOBAi Antigravity Opus` (`antigravity-opus-head`), filtered sessions/threads/summaries by the `MOBAi ` namespace (with backward-compatible legacy fallback for `GPT Head`/`Claude Head`/`Argon Head`/`Opus Head`) so future sessions of separate projects do not trigger hygiene warnings or sub-agent overcounting, and added `Orchestrator · Gemini 4 Carbon` transcript model detection.

---

## [3.8.9] - 2026-11-23

### Changed
- **Codex Quota Exhaustion (`rate_limited`) Detection (`CodexMonitorProvider.cs`, `MonitorProvidersTests.cs`):** Inspects the tail of local Codex session rollout telemetry (`~/.codex/sessions/**/*.jsonl`) strictly read-only without exposing prompts or chat content so usage-limit aborts (`codex_error_info: "usage_limit_exceeded"`) and exhausted `token_count.rate_limits` windows report `Quota Pause` (`rate_limited`) plus the reset timestamp on `GPT Head`, and automatically clear once a subsequent turn completes cleanly or the reset window passes.

---

## [3.8.8] - 2026-11-23

### Changed
- **Combined Decision Triad & 2×3 Agent Monitor Grid (`MonitorCards.tsx`, `App.tsx`):** Integrated the `Decision & Steering Triad` status header (Quorum, `Code WIP`, `Escalation: Human`, `Selection Rules`, and `Reset 2×3 layout`) directly above the 2×3 Agent Monitor Grid and removed the separate redundant bottom seat-pill bar so each of the 4 Root Heads renders once cleanly.

---

## [3.8.7] - 2026-11-23

### Changed
- **Free 2×3 Agent Monitor Grid with Cookie Persistence (`MonitorCards.tsx`):** Arranged `Live Monitor Status` into a 2-row × 3-column grid (6 slots including free drop zones) where tiles can be freely reordered or placed into empty slots via Drag & Drop or the tile grip/move control, persisted across reloads in the `mobadev_monitor_grid_v1` cookie (with a one-click `Reset 2×3 layout` action).

---

## [3.8.6] - 2026-11-23

### Changed
- **Sub-Agent Counting by AI Model (`DashboardContracts.cs`, `CodexMonitorProvider.cs`, `ClaudeMonitorProvider.cs`, `AntigravityMonitorProvider.cs`, `DashboardSnapshotService.cs`, `ActiveAgentsStrip.tsx`, `dashboard.ts`):** Replaced the tool-grouped `Codex` / `Claude` / duplicate `Antigravity` chips in `ActiveAgentsStrip` with canonical per-model counts (`GPT-6.2-sol`, `Claude Opus 5.5`, `Gemini 4 Argon`, `Gemini 4 Hydrogen`, `Claude Sonnet 5.5`), read-only inspected from Codex thread models, Claude subagent/session transcripts, and Antigravity sub-agent transcripts while keeping `0`-count chips ghosted in their native model identity color.

---

## [3.8.5] - 2026-11-23

### Changed
- **4 Master / Root Head Sessions (`CodexMonitorProvider.cs`, `ClaudeMonitorProvider.cs`, `AntigravityMonitorProvider.cs`, `DashboardSnapshotService.cs`, `MonitorCards.tsx`, `DecisionTriadCard.tsx`, `ArchitectureDiagramsView.tsx`):** Upgraded cockpit discovery, session hygiene (`TargetCount: 2` for Antigravity Root Heads), monitor cards, Triad/Steering bar, and Swarm Topology diagram to represent all 4 Master/Root Heads (`GPT Head`, `Claude Head`, `Argon Head`, `Opus Head`) with read-only Antigravity transcript tail model detection (`Orchestrator · Gemini 4 Argon` and `Orchestrator · Claude Opus 5.5`).
- **Live Operations & Architecture Fluent 2 Refinements (`App.tsx`, `PipelineCard.tsx`, `MonitorCards.tsx`, `DecisionTriadCard.tsx`, `ActiveAgentsStrip.tsx`, `RoleAndModelIcons.tsx`, `ArchitectureDiagramsView.tsx`):** Added a `"Back to top"` link (`#live-top`) at the bottom of Live Operations, kept deactivated/`<synthetic>`/`0`-count pills in their original AI identity color with ghosted opacity (`0.48`–`0.52`), removed the `Closed PRs` view from `Task & PR Pipeline`, and refined the Swarm Topology SVG to `viewBox="0 0 920 310"`.

---

## [3.8.4] - 2026-11-22

### Changed
- **Multi-Agent Quorum Swarm Parsing for Kanban Issues (`PrParticipantExtractor.cs`, `PipelineProvider.cs`, `PrParticipantExtractorTests.cs`):** Added `ParseKanbanParticipants` and `MergePrAndKanbanParticipants` so multi-agent `2/3 Quorum` `Writer` assignments on GitHub Project `#1` (e.g., `Antigravity (P3 Gemini 4 Argon + P1 Claude Opus 5.5) + Codex (P2 GPT-6.2 Sol)`) expand into all individual Round-Table seats (`Writer`, `Final Reviewer (Quorum)` on `Claude Opus 5.5`, `Quorum TD / Companion` on `GPT-6.2 Sol`, `Fast Slice Swarm` on `Claude Sonnet 5.5`) and merge cleanly with live PR evidence.

---

## [3.8.3] - 2026-11-22

### Changed
- **Closed-Session Quota Pause Fallback (`ClaudeMonitorProvider.cs`):** When no live Claude session file exists because `Claude Code` was closed during an active spend/weekly limit, inspects the latest project transcript metadata tail (without exposing chat content) so the dashboard accurately displays `Quota Pause` and the reset timestamp instead of misleading `Offline`, and adds `weekly limit` / `monthly limit` patterns to `RateLimitResetRegex`.
- **Legacy `Fable 5.2` Table Sanitization (`GovernanceProvider.cs`):** Normalizes legacy `Claude Fable 5.2` profile text in parsed governance tables to `Claude Sonnet 5.5` so `Matrix & Rules` stays aligned with the `Fable 5.2` ban.
- **Triad & Architecture Failover Visibility (`DecisionTriadCard.tsx`, `ArchitectureDiagramsView.tsx`, `ColorLegendPopover.tsx`):** Surfaces `(Antigravity + Codex Failover)` on the Triad quorum badge during quota pauses, updates the `Antigravity Monitor` mandate and diagram details to show its built-in `Gemini 4 + Opus 5.5 + Sonnet 5.5 + GPT-OSS 120B` pool, and updates `P4` legend wording to `Fast Delivery (Sonnet 5.5 & Hydrogen)`.

---

## [3.8.2] - 2026-11-22

### Changed
- **Prohibit `Claude Fable 5.2` & Enforce `Claude Opus 5.5` Ceiling (`manifest.md`, `delivery-workflow.md`, `p4-fast-slice-builder.md`, `PrParticipantExtractor.cs`, `GlossaryTooltip.tsx`, `RoleAndModelIcons.tsx`):** Replaced active `Claude Fable 5.2` references with `Claude Sonnet 5.5` for `P4` fast delivery (`pink` + Flash glyph) while locking `Claude Opus 5.5` as the maximum Claude tier (`P1`).
- **Antigravity Multi-Model Failover & `P1` Quota Shield (`ai-strategy-and-pilots.md`, `manifest.md`):** Formalized `2/3 Quorum Failover` when `Claude Code` hits a spend/usage limit (`Codex Monitor` + `Antigravity Monitor` as `Acting COO`, routing `Fresh P1 Final Review` and `P1`–`P4` failover execution through Antigravity's built-in `Claude Opus 5.5`, `Claude Sonnet 5.5`, `GPT-OSS 120B`, and `Gemini 4` pool) plus shared GitHub cloud-fleet architecture (`MOBAflow #337`).
- **German Claude Spend-Limit Detection (`ClaudeMonitorProvider.cs`):** Extended `RateLimitResetRegex` to recognize German spend/usage limit notices (`Monatliches Ausgabenlimit erreicht ... wird am <Datum> zurückgesetzt`).

---

## [3.8.1] - 2026-11-21

### Added
- **`Pipeline Seats (GitHub)` Clarification Chip (`ActiveAgentsStrip.tsx`, `App.tsx`, `ArchitectureDiagramsView.tsx`):** Added a neutral `Pipeline Seats (GitHub): N` context chip right beside `Sub-Agents (Live/Recent)` in the top KPI strip (sharing `countPipelineSeats`) so it is immediately clear why GitHub issues/PRs can carry historical or assigned Round Table seats while `0` ephemeral local runtime sub-agents are currently executing.

---

## [3.8.0] - 2026-11-21

### Added
- **Live Active Agents & Sub-Agents Telemetry (`CodexMonitorProvider.cs`, `ClaudeMonitorProvider.cs`, `AntigravityMonitorProvider.cs`, `DashboardSnapshotService.cs`, `ActiveAgentsStrip.tsx`):**
  - Added strictly read-only runtime sub-agent counting across all three tools without reading secrets or chat bodies: unarchived non-monitor Codex threads active within 3 minutes, busy non-monitor Claude sessions plus `<project>/<session>/subagents/*.jsonl` file mtimes within 2 minutes (gated by `PathWhitelistGuard`), and non-monitor Antigravity `CASCADE_RUN_STATUS_RUNNING` conversations.
  - Added `ActiveAgentsSummary` (`StandingMonitorsTotal`, `StandingMonitorsOnline`, `StandingMonitorsBusy`, `ActiveSubAgentsTotal`, `TotalActiveAgents`) on `MonitorsResponse` and surfaced it in a new **Live Active Agents & Sub-Agents Strip** above the monitor cards plus per-monitor `Sub-agents: N` badges.
- **Interactive Architecture & Flow Diagrams Tab (`ArchitectureDiagramsView.tsx`, `StatusHeader.tsx`, `App.tsx`):**
  - Added a dedicated **`Architecture`** tab featuring three interactive, Light/Dark WCAG-AA Fluent 2 SVG diagrams bound to live telemetry:
    1. **Swarm Topology:** Maintainer → Decision & Steering Triad → Standing Monitors (`Codex P2 → Claude P1 → Antigravity P3`) → Ephemeral Task/PR Sub-Agents → Repositories & GitHub Kanban.
    2. **End-to-End Task Lifecycle & Merge Gates:** `Planned / Inbox → Ready (Maintainer Trigger) → In progress (Worktrees & WIP ≤ 3) → In review (CI + Sonar + Fresh P1 + Cross-Family) → Done (Head-Bound Auto-Merge & Hygiene)` with live stage counts.
    3. **2-Axis Color & Read-Only Security Boundary:** Visual reference of AI Identity vs Semantic Status vs Interaction Blue alongside `PathWhitelistGuard`, `GET`-only enforcement, and `127.0.0.1` loopback binding.

---

## [3.7.0] - 2026-11-20

### Added
- **Interactive Color & Symbol Legend (`ColorLegendPopover.tsx`):** Added an on-demand `Legend` popover explaining Axis 1 (AI Identity), Axis 2 (Semantic Status), and Round Table glyphs in plain English without cluttering the live canvas.
- **Shared Badge & Card Primitives (`components/shared/StatusBadge.tsx`, `ContextChip.tsx`, `SectionCard.tsx`, `statusMaps.ts`, `theme/spacing.ts`):** Standardized status badges, neutral context chips, spacing rhythm, and card framing across all dashboard views while removing overloaded static `color="brand"` badges.
- **Plain-English Glossary Upgrades (`GlossaryTooltip.tsx`):** Added `Round Table`, `Color Semantics`, `Branch Hygiene`, `Closed PRs`, and `Merge Gates` entries, updated `P4` to reflect the magenta-orchid fast-delivery profile, and translated `UNSTABLE` into plain English (`Checks Pending / Checks Failing`).

### Changed
- **Orthogonal 2-Axis Color System (`mobaFluentTheme.ts`, `RoleAndModelIcons.tsx`, `base.css`):** Strictly separated **AI Identity** (`P2` ChatGPT/Codex = Teal, `P1` Claude/Opus = Orange, `P3` Antigravity/Gemini = Light Blue, `P4` Fast Delivery = Magenta-Orchid + Flash glyph, `Triad` = Purple) from **Semantic Status** (`green` = pass/ready, `gold`/amber = warning/active/pending/attention, `red` = blocked/error/Class H, `neutral` = context/idle), reserving Fluent Brand Blue (`#0078D4`) exclusively for interactive controls. Verified WCAG AA contrast (`>= 4.5:1` text, `>= 3.0:1` strokes) across Light and Dark `Background1/2/3` surfaces, fixed `sol` word-boundary matching, and defaulted task-dynamic roles to `neutral` rather than hardcoded danger-red or wrong-tool colors.
- **Round Table v2 & Visual Storytelling (`AgentRoundTable.tsx`, `MonitorCards.tsx`, `DecisionTriadCard.tsx`, `PipelineCard.tsx`):** Updated Round Table seats to encode Identity on the circle & left Visitenkarte bar and Status on the top-right seat dot & framed pill, added numbered sequential workflow stations (`1→2→3`), added profile top-accent bars on Monitor cards, added live per-seat status dots on Decision Triad chips, and added `prefers-reduced-motion` transition guards.

---

## [3.6.1] - 2026-11-20

### Changed
- **Pipeline Renaming & Workflow Order (`PipelineCard.tsx`, `PipelineProvider.cs`):** Renamed the `Task & PR Pipeline` toggles to **`Open Issues (N)` → `Open PRs (N)` → `Closed PRs (N)`** (`Open Issues` active by default) and aligned Kanban workflow ordering to **`Ready` → `In progress` → `In review`** across summary counters, column groups, and backend ranking (`KanbanStatusRank`).
- **Universal Round Table & Framed Agent Visitenkarten (`AgentRoundTable.tsx`, `PipelineCard.tsx`, `PrParticipantExtractor.cs`):** Made the graphical **Round Table** default across all three pipeline views (`Open Issues`, `Open PRs`, `Closed PRs`), including `0`-agent items and Kanban `Writer` participant synthesis (`TryParseKanbanWriter`), and redesigned agent labels into framed Visitenkarten (profile accent bar, role, tool, monospace model, status pill) positioned strictly outside the orbit with vertical collision relaxation so workflow arrows never cross text.

---

## [3.6.0] - 2026-11-20 (PR #24)

### Added
- **Interactive Round Table Agents View (`AgentRoundTable.tsx`, `PipelineCard.tsx`):** Replaced wrapped participant pills on active PRs with a Fluent 2 **Round Table** SVG visualization (`PR`/`Issue` + Risk Class in center, `P1`/`P2`/`P3`/`P4` profile seats around the table, clockwise sequential workflow arrows, dashed steering spokes for `COO` & `Head of UI/UX`, hover/click inspector strip, explicit `N Agents` + overlapping avatar strip, and 1-click **`Round Table` / `Pills`** & **`Arrows: On / Off`** toggles).
- **Branch Hygiene Monitoring (`WorktreeProvider.cs`, `DashboardSnapshotService.cs`, `HygieneAndSyncPanel.tsx`, `StatusHeader.tsx`):** Scans local branches across `MOBAflow` and `MOBAdev` plus `MOBAflow` remote-tracking `origin/*` refs strictly read-only, surfaces `Local Stale`, `Remote Stale`, and `Active` counts in a new **Branch Hygiene** card, and includes stale branch counts in header warnings & breakdown popover (`View Branches`).
- **Branch & Worktree Hygiene Rule (`manifest.md`, `docs/delivery-workflow.md`, `agents/antigravity/rules/mobaflow-agent-network.md`):** Codified mandatory post-merge cleanup (remove worktree, delete local and remote feature branches, and run `git fetch --prune`).

---

## [3.5.6] - 2026-11-20 (PR #23)

### Changed
- **Traditional Tool & Model Order (`ChatGPT/GPT → Claude/Opus → Antigravity/Gemini`):** Aligned `Decision & Steering Triad` (`DecisionTriadCard.tsx`: `CPM & TD` → `COO` → `AI Strategy`), `Session Hygiene` (`HygieneAndSyncPanel.tsx`), `Rules State` (`GovernanceProvider.cs`: `Claude Code` ahead of `Antigravity`), and `Configuration` agent groups (`ConfigDocsProvider.cs`) with the top monitor row order and recorded the convention in [`dashboard/DESIGN.md`](dashboard/DESIGN.md).
- **Light-Blue Antigravity / Gemini & Emerald-Teal GPT Accents (`mobaFluentTheme.ts`, `RoleAndModelIcons.tsx`):** Aligned `Orchestrator (Neon)`, `Antigravity`, and `Head of AI Strategy` to light-blue `blue` (matching `P3`) and shifted `ChatGPT / Codex / GPT / Sol / CPM` (`P2`) to emerald-teal (`teal`: `#2DD4A8` dark / `#0D7D63` light), creating clear 3-family color harmony (`P2` emerald-teal · `P1` orange · `P3` light blue).

---

## [3.5.5] - 2026-11-20 (PR #22)

### Added
- **Live Internal Version Label (`DashboardSnapshot.ChangelogVersion`, `DashboardSnapshotService.cs`, `StatusHeader.tsx`):** Streams the latest semantic version header dynamically from `CHANGELOG.md` and displays a clickable `v3.5.5` badge in the sticky dashboard header (opening `CHANGELOG.md` in `Configuration` on click) without duplicating version strings.

### Changed
- **`Git & Delivery Strategy` Top Row (`GovernanceMatrixView.tsx`):** Moved the **Git & Delivery Strategy** reference card to the first row at the top of the **Matrix & Rules** tab and tuned the invariants grid to `minmax(180px, 1fr)` so all five invariants align cleanly across one desktop row.
- **Workflow-Ordered Pipeline Toggles (`PipelineCard.tsx`):** Reordered the `Task & PR Pipeline` view toggles left-to-right to follow the delivery workflow: **`Kanban Board (N)`** → **`Open PRs (N)`** → **`Recently Merged (N)`**.

---

## [3.5.4] - 2026-11-20 (PR #21)

### Changed
- **Manual-Ping `Claude Monitor` Intake & Autonomous Swarm Status Ownership (`docs/claude-ready-intake-task.md`, `docs/ai-strategy-and-pilots.md`, `agents/README.md`):** Shifted `Planned` → `Ready` intake from Claude Desktop Scheduled Tasks to a zero-idle-token **maintainer `Ready` move + manual ping** in the standing local `Claude Monitor` (`COO`) session, while `Claude Monitor` and the swarm autonomously advance Kanban status through `In progress` → `In review` → `Done` (documenting why `GitHub-Ereignis` cloud triggers do not fit Project v2 drag-and-drop and keeping the local `Task Scheduler` pre-check as an optional future upgrade).

---

## [3.5.3] - 2026-11-20 (PR #19)

### Added
- **Claude Scheduled Task Template (`docs/claude-ready-intake-task.md`):** Added a ready-to-paste configuration guide and `COO: Kanban Ready Intake` prompt for Claude Desktop *Geplante Aufgaben*, linked from [`docs/ai-strategy-and-pilots.md`](docs/ai-strategy-and-pilots.md) and [`agents/README.md`](agents/README.md).

---

## [3.5.2] - 2026-11-20 (PR #18)

### Added
- **Kanban `Planned` → `Ready` Maintainer Pull-Trigger & Local Automation Strategy (`manifest.md`, `docs/delivery-workflow.md`, `docs/ai-strategy-and-pilots.md`):** Documented `Planned` → `Ready` as the explicit maintainer pull-trigger with `Claude Monitor` (`COO`) as sole intake owner (`In progress` handoff, WIP/lock/`needs-specification` guardrails, read-only dashboard invariant), alongside local GitHub GraphQL polling options (`Zero-Token` Windows pre-check vs. Claude Scheduled Tasks / `/loop` vs. Codex/Antigravity roles).

---

## [3.5.1] - 2026-11-20 (PR #17)

### Changed
- **1:1 GitHub Kanban Sync (`PipelineProvider.cs`, `DashboardContracts.cs`, `PipelineCard.tsx`):** Updated the third Task & PR Pipeline toggle to **`Kanban Board (N)`** mirroring GitHub Project #1 (`Ready`, `In progress`, `In review`) 1:1 with both **PRs and Issues**, live column summary counters, direct `Open GitHub Kanban` link, and zero hidden items (board-drift warnings appear inline without filtering items out).
- **Kanban Cleanup (Option A):** Returned legacy idle issues (`#35`, `#36`, `#136`, `#140`) on MOBAflow Kanban Project #1 back to `Planned`.

---

## [3.5.0] - 2026-11-19 (PR #16)

### Added
- **Active Issues Kanban View (`PipelineProvider.cs`, `DashboardContracts.cs`, `PipelineCard.tsx`):** Queries active GitHub Project Kanban columns (`In progress`, `In review`, `Ready`) via a single filtered GraphQL call (`TryLoadActiveKanbanBoardAsync`), enriches issues with PR links/writers/locks/participants, surfaces issues that do not have an open PR yet (`No PR yet`), and isolates `Closed on GitHub` / `Stale / No Writer` board entries into a collapsible **Board Drift** section.

---

## [3.4.3] - 2026-11-19 (PR #15)

### Added
- **Explicit PR `BLOCKED` / `DIRTY` Reasons (`PipelineProvider.cs`, `PipelineCard.tsx`):** Extracts concrete blocking causes (`Draft PR`, merge conflicts, behind `main`, `REVIEW_REQUIRED` / `CHANGES_REQUESTED`, `BLOCKED` review gates, plus failing/running check names from `statusCheckRollup`) and renders them directly inline under blocked PR rows and in badge tooltips.
- **Inline Kanban Status on Open PRs (`PipelineProvider.cs`):** Reads `projectItems` directly on open PRs so items like PR `#501` display their live Kanban column (e.g., `Kanban: In review`) regardless of overall GitHub Project item count.

---

## [3.4.2] - 2026-11-19 (PR #14)

### Changed
- **`Configuration` Tab Header (`StatusHeader.tsx`):** Renamed the third header tab from `Config` to `Configuration` and removed the leading document icon.
- **Monitor Card Order (`DashboardSnapshotService.cs`, `MonitorCards.tsx`):** Ordered the standing monitor cards with `Codex Monitor` first, followed by `Claude Monitor` and `Antigravity Monitor`.

---

## [3.4.1] - 2026-11-19 (PR #13)

### Changed & Removed
- **AI Strategy & Monitor Model Update (`docs/ai-strategy-and-pilots.md`, `manifest.md`, `docs/delivery-workflow.md`, `agents/antigravity/agents/head-of-ai-strategy.md`, Dashboard providers/UI):** Defined **`Orchestrator (Neon)`** (`gemini-pro-orchestrator-neon`) as the standing model for `Antigravity Monitor` and updated `Codex Monitor` to **`Sol 6.2`** (`gpt-6.2-sol`).
- **Generation-Aligned Versioning (`CHANGELOG.md`, `package.json`, `dashboard/DESIGN.md`):** Aligned `MOBAdev` Semantic Versioning on the Gen#3 `3.x` baseline (`3.0.0+`, with earlier MOBAflow setups predating this repo as Gen#1 and Gen#2).
- **Simplified Header & `MOBAdev`-Only Docs (`StatusHeader.tsx`, `ConfigDocsProvider.cs`, `GovernanceMatrixView.tsx`):** Removed the `MOBAflow Instructions` tab and backend `mobaflow` scope to keep the cockpit focused on `MOBAdev` configuration files; shortened the third header tab title to `Config` and repointed `Git & Delivery Strategy` quick links to `Manifest` (`manifest.md`) and `Delivery Workflow` (`docs/delivery-workflow.md`).

### Fixed
- **Cross-View Badge & Border Clipping (`base.css`, `HygieneAndSyncPanel.tsx`, `PipelineCard.tsx`, `ConfigDocsView.tsx`, `GovernanceMatrixView.tsx`, `RoleAndModelIcons.tsx`):** Added `.fui-Badge { flex-shrink: 0; white-space: nowrap; }`, flex truncation (`flex: 1 1 0; min-width: 0; overflow: hidden`) on Worktree/Session rows so `Stale / Merged` never wraps or clips, plus wrapping container pills (`wrapPill`, `monoPill`, `pathPill`, `riskCellStack`, `ModelBadge` `wrap`) and top-aligned `10px` table cell spacing across `Matrix & Rules`.
- **Markdown Link Cleanup in Governance Tables (`GovernanceProvider.cs`):** Stripped Markdown links (`[label](target)`) down to plain readable link text when extracting role responsibilities and matrix descriptions.

---

## [3.4.0] - 2026-11-19 (PR #12)

### Added
- **Dashboard Title & Warning Breakdown Popover:** Rebranded the read-only cockpit to **MOBAdev Agent Swarm Gen#3** and added an interactive Fluent `Popover` on the header warnings badge breaking down rules drift, stale worktrees, extra sessions, and quota pauses with 1-click jump links.
- **PR Merge Gates & Plain-English `UNSTABLE`:** Each PR row now displays a `Gates X/Y` progress strip derived from existing PR evidence (`Writer`, optional `Companion`, `CI & Sonar`, `P1 Final`, `Cross-Family`, `Auto-Merge`) and translates GitHub `UNSTABLE` into `Checks Pending` / `Checks Failing`.
- **Read-Only Rules Diff (`GET /api/governance/diff`):** Added expandable `Show Diff` / `Hide Diff` inspection in `Matrix & Rules` with strict allow-list and path-traversal protection plus `<repo-root>/MOBAflow` normalization.
- **Read-Only Configuration Tabs (`MOBAdev Config`, `MOBAflow Instructions`):** Added allow-listed endpoints (`GET /api/docs`, `GET /api/docs/content`) and a Master-Detail Markdown viewer (`Formatted` / `Raw`, search filter, YAML frontmatter bar).
- **Git & Delivery Strategy Reference Card:** Added the 5 MOBAflow Git invariants (`Small Batches`, `Worktree Isolation`, `Merge Never Rebase`, `One Writer · Locks · Code WIP <= 3`, `Head-Bound Squash Auto-Merge`) to `Matrix & Rules` with direct links to the authoritative instruction files.
- **Repository Changelog:** Added [CHANGELOG.md](CHANGELOG.md) and included it in the allow-listed `MOBAdev Config` tab and [README.md](README.md).

### Changed
- **Compact Monitor Cards & 1-Line Triad Strip:** Aligned all three Monitor cards to a unified footer divider baseline, formatted raw RRULE strings (e.g. `Active · Hourly`), moved `Escalation: Human` into the Triad header row, and refined Triad WIP to `Code WIP: N / 3 (+Docs)` matching MOBAflow's Class `L` exemption rule.
- **Scannable PR & Hygiene Density:** Reduced PR participant cards to compact single-line pills and collapsed stale MOBAflow worktrees beyond 3 entries with an interactive expand toggle.

### Fixed
- **Slice-Header Writer Detection (`PrParticipantExtractor`):** Explicitly parses `- Writer:` slice-header lines before falling back to body heuristics so `P4 slice builder (Claude)` resolves to `P4` / `Claude Fable 5.2` even when templates mention `(P1-P4)`.

---

## [3.3.0] - 2026-11-19 (PR #11)

### Added
- **Fluent UI 2 Adoption:** Migrated the React cockpit to `@fluentui/react-components` and `@fluentui/react-icons` aligned with `MOBAflow`'s WinUI 3 Light/Dark Fluent styling (`#0078D4`).
- **Role & Model Iconography (`RoleAndModelIcons.tsx`):** Added distinct outlined icons and a consistent Light/Dark 8-color system for every role and AI model across monitors, triad, PR tasks, and matrix tables.
- **`Decision & Steering Triad` Bar (`DecisionTriadCard.tsx`):** Added a compact Row-2 selection council strip between the Monitor cards and the Task & PR Pipeline.
- **Linked GitHub Issues & Risk Class Fix (`PipelineProvider.cs`):** Extracted linked GitHub issues from `closingIssuesReferences`, slice headers, explicit keywords, and branch names, resolved titles via `gh issue list`, and fixed `RiskClassRegex` for parenthesized `- Risk class (H/M/L): ...` headers.

### Changed
- Standardized all dashboard UI copy, glossary tooltips, relative timestamps, and API status notices to plain English (`Rules State · Actual vs. Target`, `Hashes (Actual / Target)`).

---

## [3.2.1] - 2026-11-19 (PR #10)

### Added
- **Per-Task Agent, Role & Model Attribution (`PrParticipantExtractor.cs`):** Added extraction of participating agents, profiles (`P1`–`P4`), models, and statuses per PR across both `Open` and `Recently Merged` views.
- **`Start-Dashboard.ps1` Readiness Wait:** Polls `/api/health` before opening the browser so `-OpenBrowser` never hits `ERR_CONNECTION_REFUSED` during startup.

---

## [3.2.0] - 2026-11-19 (PRs #7, #8, #9)

### Added
- **Dashboard Design Concept (`dashboard/DESIGN.md`):** Co-designed strictly read-only, zero-duplication, loopback-only (`127.0.0.1:5179`) architecture with `Head of UI/UX` (PR #7).
- **.NET 10 Read-Only Backend (`MobaDev.Dashboard.Api`):** Implemented whitelisted metadata providers (`ClaudeMonitorProvider`, `CodexMonitorProvider`, `AntigravityMonitorProvider`, `GovernanceProvider`, `PipelineProvider`, `WorktreeProvider`) and xUnit tests (PR #8).
- **React + Vite Cockpit (`MobaDev.Dashboard.Web`) & Launcher (`dashboard/Start-Dashboard.ps1`):** Delivered Light/Dark UI with `Live Operations` and `Matrix & Rules` tabs plus Vitest coverage (PR #9).

---

## [3.1.0] - 2026-11-18 (PRs #3, #4, #5, #6)

### Changed
- **Unified Standing Monitors & Hygiene (PRs #5, #6):** Standardized permanent monitor names (`Claude Monitor`, `Codex Monitor`, `Antigravity Monitor`) and enforced the 1-monitor-per-tool rule across agent definitions and docs.
- **MOBAflow Kanban Board Migration (PR #4):** Moved slice and lock tracking from `MOBAdev` issue #1 to the MOBAflow GitHub Project Kanban and PR headers.
- **Versioned Multi-Model Setup (PR #3):** Versioned `manifest.md`, Antigravity and Claude agent/rule backups, and `Orchestrator (Argon)` pilot notes.

---

## [3.0.0] - 2026-11-16 (PR #2 & Baseline 2026-11-05)

### Added
- Initial `MOBAdev` repository structure, MIT license, rebuild runbook, setup inventory, pilot skills (`skills/`), and MOBAflow small-batch delivery workflow documentation (`docs/delivery-workflow.md`).
