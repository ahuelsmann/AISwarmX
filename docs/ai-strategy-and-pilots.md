# AI Strategy and Orchestration Pilots

The **Head of AI Strategy** (`P3` / `Orchestrator (Argon)` in Antigravity) continuously evaluates and refines MOBAflow's multi-model agent network across **Antigravity**, **Claude Code**, and **ChatGPT Codex** under the maxim **"Quality, Quality, Quality"**, guards [manifest.md](../manifest.md), and keeps sanitized setup backups versioned in `ahuelsmann/MOBAdev`.

## Active Pilot: `Orchestrator (Argon)` (`gemini-pro-orchestrator-g1-pro`)

- **Started:** 2026-11-18 (trialed in daily practice before updating `MOBAflow/docs/AI-DEVELOPMENT.md`)
- **Architecture:** High-level orchestration design, strategy, governance trade-offs, and verification stay on `Gemini 4 Argon`; bounded inspections, consistency checks, and routine execution steps are delegated cleanly to `Gemini 4 Hydrogen`.

### Lightweight Evaluation Scorecard

1. **Delegation boundary:** Does Argon retain architectural/strategic judgment while delegating only bounded inspection or execution work to Hydrogen?
2. **Quality impact:** Are real defects, setup drift, or missing backups detected accurately without regressions?
3. **Latency & responsiveness:** Does multi-step turn completion improve compared to single-model runs?
4. **Quota efficiency:** How does the mode affect the shared 5-hour and weekly Antigravity Argon quota?

### Mandatory Review Integrity Guardrail

Formal `P3` UX/code sign-off reviews and high-risk (`H`) safety judgments must run on standalone `Gemini 4 Argon (High)`, `P1` (`Claude Opus 5.5`), or `P2` (`GPT-6 Astra`). Never delegate a final review verdict to `Gemini 4 Hydrogen`.

### Adoption Criterion

Document `Orchestrator (Argon)` as an explicit `P3`/`P4` option in `MOBAflow/docs/AI-DEVELOPMENT.md` only after practical validation across real MOBAflow/MOBAdev tasks and maintainer approval.

## Evaluated & Deferred Model Record: `Gemini 4 Carbon`

- **Reviewed:** 2026-11-18
- **Decision:** Deferred without subscription upgrade or pay-as-you-go API spend.
- **Rationale:** `P1` (`Claude Opus 5.5`) and `P2` (`GPT-6 Astra`) already cover deep architecture, concurrency/safety invariants, and build/repo precision. Re-evaluate only if `Gemini 4 Carbon` becomes included in the existing plan at no additional cost or if a concrete capability or quota bottleneck emerges.
