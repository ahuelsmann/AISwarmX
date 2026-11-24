# AI Strategy and Orchestration Pilots

The **Head of AI Strategy** (`P3` / `Orchestrator (Neon)` in Antigravity) continuously evaluates and refines MOBAflow's multi-model agent network across **Antigravity**, **Claude Code**, and **ChatGPT Codex** under the maxim **"Quality, Quality, Quality"**, guards [manifest.md](../manifest.md), and keeps sanitized setup backups versioned in `ahuelsmann/MOBAdev`.

## Standing Monitoring Agents vs. Runtime Ephemeral Agents

To eliminate stale chat clutter and preserve fresh review context:

- **Exactly 1 standing Monitoring & Steering Agent per tool (3 total):**
  1. **Claude Code (`Claude Monitor` — `Chief Operating Officer (COO)`, `P1` Claude Opus 5.5):** Central task workflow entry point — initiates workflows, cuts slices, assigns risk classes (`H`/`M`/`L`), spawns/dispatches runtime agents, and arms auto-merge.
  2. **ChatGPT Codex (`Codex Monitor` — `Technical Director & CPM`, `P2` Sol 6.2 / `gpt-6.2-sol`):** Backlog/Spec-Kit/build-graph precision (`Moba.slnx`) and 30-minute board/lock check.
  3. **Antigravity (`Antigravity Monitor` — `Head of AI Strategy`, `P3` Orchestrator (Neon) / `gemini-pro-orchestrator-neon`):** Monitors multi-model orchestration, evaluates pilots, and guards `manifest.md` and `MOBAdev` backups.
- **Runtime Spawning & Auto-Archival:** All task-scoped execution, review, and domain specialist agents (`Writer`, `Companion Reviewer`, `Final Reviewer`, `Head of UI/UX`, `Head of MOBAnews`, `Security`, `DevOps`) are spawned automatically at task runtime with their assigned model (`P1`–`P4`) and archived or deleted (along with their worktree) immediately on completion (`claude rm` / `--no-session-persistence`, `codex archive`, ephemeral `agy --agent` runs).

## Active Monitor & Orchestration Mode: `Orchestrator (Neon)` (`gemini-pro-orchestrator-neon`)

- **Updated:** 2026-11-19 (promoted from the 2026-11-18 `Orchestrator (Argon)` / `gemini-pro-orchestrator-g1-pro` pilot to **`Orchestrator (Neon)`** / `gemini-pro-orchestrator-neon` as the defined standing model for `Antigravity Monitor`)
- **Architecture:** High-level orchestration design, strategy, governance trade-offs, and verification stay on `Gemini 4 Neon`; bounded inspections, consistency checks, and routine execution steps are delegated cleanly to `Gemini 4 Hydrogen`.

### Lightweight Evaluation Scorecard

1. **Delegation boundary:** Does `Neon` retain architectural/strategic judgment while delegating only bounded inspection or execution work to `Hydrogen`?
2. **Quality impact:** Are real defects, setup drift, or missing backups detected accurately without regressions?
3. **Latency & responsiveness:** Does multi-step turn completion improve compared to single-model runs?
4. **Quota efficiency:** How does the mode affect the shared 5-hour and weekly Antigravity `Neon` / `Argon` quota?

### Mandatory Review Integrity Guardrail

Formal `P3` UX/code sign-off reviews and high-risk (`H`) safety judgments must run on standalone `Gemini 4 Neon (High)` / `Gemini 4 Argon (High)`, `P1` (`Claude Opus 5.5`), or `P2` (`Sol 6.2` / `GPT-6 Astra`). Never delegate a final review verdict to `Gemini 4 Hydrogen`.

### Adoption Criterion

Document `Orchestrator (Neon)` as an explicit `P3`/`P4` option in `MOBAflow/docs/AI-DEVELOPMENT.md` after cross-repo sync validation and maintainer approval.

## Standing `Codex Monitor` Model Update: `Sol 6.2` (`gpt-6.2-sol`)

- **Updated:** 2026-11-19
- **Assignment:** `Codex Monitor` (`Technical Director & CPM`, `P2`) runs on **`Sol 6.2`** (`gpt-6.2-sol`) in ChatGPT Codex for backlog precision, Spec Kit alignment, build-graph checks (`Moba.slnx`), and 30-minute board/lock reviews.

## Kanban `Ready` Pull-Trigger & Local Automation Strategy

- **Single Intake Owner (`Claude Monitor` / `COO`):** Only `Claude Monitor` picks up issues moved to **`Ready`** on the MOBAflow Kanban board. Keeping a single intake owner eliminates race conditions across Claude Code, ChatGPT Codex, and Antigravity, while the local MOBAdev dashboard stays strictly read-only.
- **Active Mode — Maintainer `Planned` -> `Ready` + Manual Ping in `Claude Monitor` (`COO`):** The maintainer controls the explicit start gate by moving an issue to **`Ready`** on GitHub Project #1 and sending a brief kickoff ping to the standing local **`Claude Monitor`** session (0 idle tokens, 0 background scripts, guaranteed local toolchain). Once started, `Claude Monitor` and the swarm autonomously transition Kanban status through **`In progress`** -> **`In review`** -> **`Done`** — see the kickoff playbook in [claude-ready-intake-task.md](claude-ready-intake-task.md).
- **Local Pull-Polling Instead of Inbound Webhooks:** Because local workstations stay loopback-only (`127.0.0.1`, no open inbound ports or tunnels) and cloud-hosted autonomous agent teams (`MOBAflow #337`) remain deferred, GitHub state checks stay strictly pull-based via lightweight filtered GraphQL (`status:Ready`, `< 1s`).
- **Deferred Background Automation Findings:**
  - **Claude Desktop `GitHub-Ereignis` triggers (Rejected for Kanban Intake):** Dragging an issue across Kanban columns emits a Project v2 event (`projects_v2_item`), not a repository `issues` webhook (`Alle Issue-Ereignisse`), unfiltered issue triggers burn Opus 5.5 quota on routine comments/bots, and event-triggered routines run in cloud sandboxes without local `gh` scopes, worktrees, or local CLI channels (`codex.exe`, `agy.exe`, `sonar.exe`).
  - **Zero-Token Local Pre-Check (Optional Future Upgrade):** Windows Task Scheduler + `gh api graphql` invoking `claude.exe` only when `Ready > 0` remains a clean future upgrade if unattended 2–5 minute pickup with zero idle LLM spend is ever desired.
  - **Codex & Antigravity Automations:** Keep Codex's hourly `automation.toml` strictly as a read-only `TD/CPM` observer and reserve Antigravity `/automation` cron sidecars for periodic strategy/hygiene checks — never create competing intake triggers.

## Evaluated & Deferred Model Record: `Gemini 4 Carbon`

- **Reviewed:** 2026-11-18
- **Decision:** Deferred without subscription upgrade or pay-as-you-go API spend.
- **Rationale:** `P1` (`Claude Opus 5.5`) and `P2` (`Sol 6.2` / `GPT-6 Astra`) already cover deep architecture, concurrency/safety invariants, and build/repo precision. Re-evaluate only if `Gemini 4 Carbon` becomes included in the existing plan at no additional cost or if a concrete capability or quota bottleneck emerges.

---

## Claude Quota Failover via Antigravity Multi-Model Pool & `Fable 5.2` Prohibition

- **Updated:** 2026-11-22
- **Claude Ceiling (`Max Opus 5.5`, zero `Fable 5.2`):** `Claude Fable 5.2` is explicitly **prohibited** across all tools and profiles due to disproportional token/budget burn. Maximum permitted Claude tier is **`Claude Opus 5.5`** (`P1`), while **`Claude Sonnet 5.5`** serves `P4` routine tasks.
- **`P1` Quota Shield & Default Dual-Plan Splitting:** Keep **`MOBAi Claude Opus`** (Anthropic plan) focused on lean COO intake, Kanban steering, and Class `H` safety architecture. Route bulk slice writing primarily to **ChatGPT Codex (`P2`, `GPT-6.2 Sol`)** and **Antigravity (`P3`/`P4`, `Gemini 4 Carbon` / `Hydrogen` / `Sonnet 5.5`)**, and route **`Fresh Independent P1 Final Reviews`** by default to Antigravity's **`Opus 5.5`** quota pool (Google AI plan) even during normal operation — preserving `MOBAi Claude Opus` budget and eliminating COO bottlenecks.
- **Disjoint `main`-Sync Fast-Path:** Avoid burning review tokens after pure `origin/main` merges when `LockedAreas` and slice diffs are untouched (`0` conflict edits in slice files); rely on CI, SonarCloud, and Stryker mutation gates for the synced merge commit.
- **Antigravity Multi-Model Pool during `Claude Code` Quota Pauses (`2/3 Quorum Failover`):**
  Because Antigravity exposes multiple vendor families natively from [allowed-models.md](allowed-models.md) (`Opus 5.5`, `Sonnet 5.5`, `GPT-OSS 120B`, `Gemini 4 Carbon`, `Gemini 4 Hydrogen`), the swarm never stalls when standalone `Claude Code` hits a 5-hour or monthly spend limit:
  - **`Acting COO` (`2/3 Quorum`):** `Codex Monitor` (`P2`) + `Antigravity Monitor` (`P3`) take over `Ready -> In progress` intake, area locks, and merge readiness.
  - **`P1` Architecture & Fresh Final Review:** Routed to **`Claude Opus 5.5`** inside Antigravity (`fresh subagent`), preserving the mandatory `P1` gate and cross-family check with `P2` (`Sol 6.2` / `GPT-6 Astra` or `GPT-OSS 120B`) and `P3` (`Gemini 4 Argon / Neon`).
  - **`P4` Fast Delivery:** Routed to **`Gemini 4 Hydrogen`** or **`Claude Sonnet 5.5`** (never `Fable 5.2`).
- **Cloud Fleet Direction (Shared GitHub Coordination Bus across All Tools):**
  Aligns with `MOBAflow #337` so local outages or exhausted desktop quotas do not block cloud-eligible work (~85% of issues: `MOBAdemo`, `Common`, `Backend`, `TrackPlan`, `MOBApi`, `RF-*`, `docs/wiki/`). GitHub (`Project v2`, structured PR slice headers, PR review comments, and `ExecutionHost: local | codex-cloud | claude-cloud | github-actions` lease tracking) serves as the single message bus across **ChatGPT Codex Cloud**, **Claude Web / GitHub Actions**, and **Gemini / Antigravity Cloud Runners**, while physical Z21/ESP32/WinUI-device acceptance remains strictly local under maintainer authorization.
