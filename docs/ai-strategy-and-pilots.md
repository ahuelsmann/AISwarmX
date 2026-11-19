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
- **Local Pull-Polling Instead of Inbound Webhooks:** Because local workstations stay loopback-only (`127.0.0.1`, no open inbound ports or tunnels) and cloud-hosted autonomous agent teams (`MOBAflow #337`) remain deferred, GitHub changes are discovered locally via lightweight filtered GraphQL polling (`status:Ready`, `< 1s`).
- **Tool-Native Automation Options & Quota Hygiene:**
  - **Claude Desktop Scheduled Tasks (*Geplante Aufgaben*) / CLI `/loop`:** Native fit for `Claude Monitor` (`COO`), best run on a lightweight `P4`/Sonnet pre-check or conservative 30–60 minute cadence that exits silently whenever `Ready == 0` to protect Opus 5.5 quota.
  - **Zero-Token Local Pre-Check (Windows Task Scheduler + `gh api graphql`):** Optimal for fast 2–5 minute responsiveness with **0 LLM tokens** consumed while `Ready == 0`; invokes `claude` only when an actual `Ready` issue is waiting.
  - **ChatGPT Codex Automations & Antigravity `/automation`:** Keep Codex's hourly `automation.toml` strictly as a read-only `TD/CPM` observer and reserve Antigravity `/automation` cron sidecars for periodic strategy/hygiene checks — never create competing intake triggers.

## Evaluated & Deferred Model Record: `Gemini 4 Carbon`

- **Reviewed:** 2026-11-18
- **Decision:** Deferred without subscription upgrade or pay-as-you-go API spend.
- **Rationale:** `P1` (`Claude Opus 5.5`) and `P2` (`Sol 6.2` / `GPT-6 Astra`) already cover deep architecture, concurrency/safety invariants, and build/repo precision. Re-evaluate only if `Gemini 4 Carbon` becomes included in the existing plan at no additional cost or if a concrete capability or quota bottleneck emerges.
