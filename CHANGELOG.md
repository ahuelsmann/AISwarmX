# Changelog

All notable changes to **MOBAdev** and the local **MOBAdev Agent Swarm Gen#3 Dashboard** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/) aligned with MOBAdev generations (`Gen#1 = 1.x`, `Gen#2 = 2.x`, `Gen#3 = 3.0.0+`).

## [3.1.1] - 2026-11-20

### Changed
- **1:1 GitHub Kanban Sync (`PipelineProvider.cs`, `DashboardContracts.cs`, `PipelineCard.tsx`):** Updated the third Task & PR Pipeline toggle to **`Kanban Board (N)`** mirroring GitHub Project #1 (`Ready`, `In progress`, `In review`) 1:1 with both **PRs and Issues**, live column summary counters, direct `Open GitHub Kanban` link, and zero hidden items (board-drift warnings appear inline without filtering items out).
- **Kanban Cleanup (Option A):** Returned legacy idle issues (`#35`, `#36`, `#136`, `#140`) on MOBAflow Kanban Project #1 back to `Planned`.

---

## [3.1.0] - 2026-11-19 (PR #16)

### Added
- **Active Issues Kanban View (`PipelineProvider.cs`, `DashboardContracts.cs`, `PipelineCard.tsx`):** Queries active GitHub Project Kanban columns (`In progress`, `In review`, `Ready`) via a single filtered GraphQL call (`TryLoadActiveKanbanBoardAsync`), enriches issues with PR links/writers/locks/participants, surfaces issues that do not have an open PR yet (`No PR yet`), and isolates `Closed on GitHub` / `Stale / No Writer` board entries into a collapsible **Board Drift** section.

---

## [3.0.3] - 2026-11-19 (PR #15)

### Added
- **Explicit PR `BLOCKED` / `DIRTY` Reasons (`PipelineProvider.cs`, `PipelineCard.tsx`):** Extracts concrete blocking causes (`Draft PR`, merge conflicts, behind `main`, `REVIEW_REQUIRED` / `CHANGES_REQUESTED`, `BLOCKED` review gates, plus failing/running check names from `statusCheckRollup`) and renders them directly inline under blocked PR rows and in badge tooltips.
- **Inline Kanban Status on Open PRs (`PipelineProvider.cs`):** Reads `projectItems` directly on open PRs so items like PR `#501` display their live Kanban column (e.g., `Kanban: In review`) regardless of overall GitHub Project item count.

---

## [3.0.2] - 2026-11-19 (PR #14)

### Changed
- **`Configuration` Tab Header (`StatusHeader.tsx`):** Renamed the third header tab from `Config` to `Configuration` and removed the leading document icon.
- **Monitor Card Order (`DashboardSnapshotService.cs`, `MonitorCards.tsx`):** Ordered the standing monitor cards with `Codex Monitor` first, followed by `Claude Monitor` and `Antigravity Monitor`.

---

## [3.0.1] - 2026-11-19 (PR #13)

### Changed & Removed
- **AI Strategy & Monitor Model Update (`docs/ai-strategy-and-pilots.md`, `manifest.md`, `docs/delivery-workflow.md`, `agents/antigravity/agents/head-of-ai-strategy.md`, Dashboard providers/UI):** Defined **`Orchestrator (Neon)`** (`gemini-pro-orchestrator-neon`) as the standing model for `Antigravity Monitor` and updated `Codex Monitor` to **`Sol 6.2`** (`gpt-6.2-sol`).
- **Generation-Aligned Versioning (`CHANGELOG.md`, `package.json`, `dashboard/DESIGN.md`):** Aligned Semantic Versioning with MOBAdev generations (`Gen#1 = 1.x`, `Gen#2 = 2.x`, `Gen#3 = 3.0.0+`).
- **Simplified Header & `MOBAdev`-Only Docs (`StatusHeader.tsx`, `ConfigDocsProvider.cs`, `GovernanceMatrixView.tsx`):** Removed the `MOBAflow Instructions` tab and backend `mobaflow` scope to keep the cockpit focused on `MOBAdev` configuration files; shortened the third header tab title to `Config` and repointed `Git & Delivery Strategy` quick links to `Manifest` (`manifest.md`) and `Delivery Workflow` (`docs/delivery-workflow.md`).

### Fixed
- **Cross-View Badge & Border Clipping (`base.css`, `HygieneAndSyncPanel.tsx`, `PipelineCard.tsx`, `ConfigDocsView.tsx`, `GovernanceMatrixView.tsx`, `RoleAndModelIcons.tsx`):** Added `.fui-Badge { flex-shrink: 0; white-space: nowrap; }`, flex truncation (`flex: 1 1 0; min-width: 0; overflow: hidden`) on Worktree/Session rows so `Stale / Merged` never wraps or clips, plus wrapping container pills (`wrapPill`, `monoPill`, `pathPill`, `riskCellStack`, `ModelBadge` `wrap`) and top-aligned `10px` table cell spacing across `Matrix & Rules`.
- **Markdown Link Cleanup in Governance Tables (`GovernanceProvider.cs`):** Stripped Markdown links (`[label](target)`) down to plain readable link text when extracting role responsibilities and matrix descriptions.

---

## [3.0.0] - 2026-11-19 (PR #12)

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

## [2.3.0] - 2026-11-19 (PR #11)

### Added
- **Fluent UI 2 Adoption:** Migrated the React cockpit to `@fluentui/react-components` and `@fluentui/react-icons` aligned with `MOBAflow`'s WinUI 3 Light/Dark Fluent styling (`#0078D4`).
- **Role & Model Iconography (`RoleAndModelIcons.tsx`):** Added distinct outlined icons and a consistent Light/Dark 8-color system for every role and AI model across monitors, triad, PR tasks, and matrix tables.
- **`Decision & Steering Triad` Bar (`DecisionTriadCard.tsx`):** Added a compact Row-2 selection council strip between the Monitor cards and the Task & PR Pipeline.
- **Linked GitHub Issues & Risk Class Fix (`PipelineProvider.cs`):** Extracted linked GitHub issues from `closingIssuesReferences`, slice headers, explicit keywords, and branch names, resolved titles via `gh issue list`, and fixed `RiskClassRegex` for parenthesized `- Risk class (H/M/L): ...` headers.

### Changed
- Standardized all dashboard UI copy, glossary tooltips, relative timestamps, and API status notices to plain English (`Rules State · Actual vs. Target`, `Hashes (Actual / Target)`).

---

## [2.2.0] - 2026-11-19 (PR #10)

### Added
- **Per-Task Agent, Role & Model Attribution (`PrParticipantExtractor.cs`):** Added extraction of participating agents, profiles (`P1`–`P4`), models, and statuses per PR across both `Open` and `Recently Merged` views.
- **`Start-Dashboard.ps1` Readiness Wait:** Polls `/api/health` before opening the browser so `-OpenBrowser` never hits `ERR_CONNECTION_REFUSED` during startup.

---

## [2.1.0] - 2026-11-19 (PRs #7, #8, #9)

### Added
- **Dashboard Design Concept (`dashboard/DESIGN.md`):** Co-designed strictly read-only, zero-duplication, loopback-only (`127.0.0.1:5179`) architecture with `Head of UI/UX` (PR #7).
- **.NET 10 Read-Only Backend (`MobaDev.Dashboard.Api`):** Implemented whitelisted metadata providers (`ClaudeMonitorProvider`, `CodexMonitorProvider`, `AntigravityMonitorProvider`, `GovernanceProvider`, `PipelineProvider`, `WorktreeProvider`) and xUnit tests (PR #8).
- **React + Vite Cockpit (`MobaDev.Dashboard.Web`) & Launcher (`dashboard/Start-Dashboard.ps1`):** Delivered Light/Dark UI with `Live Operations` and `Matrix & Rules` tabs plus Vitest coverage (PR #9).

---

## [2.0.0] - 2026-11-18 (PRs #3, #4, #5, #6)

### Changed
- **Unified Standing Monitors & Hygiene (PRs #5, #6):** Standardized permanent monitor names (`Claude Monitor`, `Codex Monitor`, `Antigravity Monitor`) and enforced the 1-monitor-per-tool rule across agent definitions and docs.
- **MOBAflow Kanban Board Migration (PR #4):** Moved slice and lock tracking from `MOBAdev` issue #1 to the MOBAflow GitHub Project Kanban and PR headers.
- **Versioned Multi-Model Setup (PR #3):** Versioned `manifest.md`, Antigravity and Claude agent/rule backups, and `Orchestrator (Argon)` pilot notes.

---

## [1.0.0] - 2026-11-16 (PR #2 & Baseline 2026-11-05)

### Added
- Initial `MOBAdev` repository structure, MIT license, rebuild runbook, setup inventory, pilot skills (`skills/`), and MOBAflow small-batch delivery workflow documentation (`docs/delivery-workflow.md`).
