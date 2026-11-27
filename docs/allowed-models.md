# Allowed AI Models (`AISwarmX` / `AiSwarmX`)

**Status:** Binding allowlist across **ChatGPT Codex**, **Claude Code**, and **Antigravity** (both standing `AiSwarmX` Root Heads and spawned runtime sub-agents, as well as new projects adopting this governance).

Only the **8 approved AI models** listed in this catalog may be selected or dispatched (in addition to Codex's internal `codex-auto-review` Guardian model). Unlisted or superseded models require explicit maintainer approval and an update to this document before use.

---

## Approved Models

| Model | Identifier / Alias | Family | Profile | Permitted Tools | Primary Responsibility |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`GPT-6.2 Sol`** | `gpt-6.2-sol` | OpenAI (`X`) | `P2` | ChatGPT Codex (`AiSwarmX ChatGPT Codex`) | Technical Director & CPM, repo/build-graph/Spec-Kit precision, complex `X`-family implementation & cross-family reviews |
| **`GPT-6.2 Astra`** | `gpt-6.2-astra` | OpenAI (`X`) | `P2` / `P4` Workhorse | ChatGPT Codex (`AiSwarmX ChatGPT Codex`) | Fast, quota-efficient routine code, test, and repo-search sub-agents preserving `GPT-6.2 Sol` quota *(plus Codex's internal `codex-auto-review` Guardian model)* |
| **`Opus 5.5`** | `claude-opus-5-5` / `Claude Opus 5.5` | Anthropic (`C`) | `P1` | Claude Code (`AiSwarmX Claude Opus`), Antigravity (`AiSwarmX Antigravity Opus`) | COO / Acting COO, deep architecture, concurrency/safety invariants; Antigravity `Opus 5.5` (Google AI plan) handles Fresh Independent `P1` Final Reviews by default when quota is available to shield `AiSwarmX Claude Opus` (Anthropic plan), failing back to Claude Code when Antigravity partner quota hits `0%` |
| **`Sonnet 5.5`** | `claude-sonnet-5-5` / `Claude Sonnet 5.5` | Anthropic (`C`) | `P4` | Claude Code, Antigravity | Fast routine code/test/doc slices and triage |
| **`Claude Haiku 4.5`** | `claude-haiku-4-5` | Anthropic (`C`) | `P4` Read-Only Explore | Claude Code (`AiSwarmX Claude Opus`) | **Strictly read-only repository search & `Explore` triage only — never permitted for code edits or reviews** |
| **`Gemini 4 Carbon`** | `gemini-4-carbon-medium` / `Orchestrator · Gemini 4 Carbon` | Google (`G`) | `P3` | Antigravity (`AiSwarmX Antigravity Gemini`) | Head of AI Strategy, system context, Product & UI/UX architecture, `G`-family orchestration & cross-family review |
| **`Gemini 4 Hydrogen`** | `gemini-4-hydrogen-xhigh` | Google (`G`) | `P4` | Antigravity | Fast inner execution turns, bounded inspections, and routine worker tasks *(never used as standalone final sign-off reviewer)* |
| **`GPT-OSS 120B`** | `gpt-oss-120b` | Open-weight (`X`-family failover) | `P2` (Failover) | Antigravity | Cross-family `P2` failover verification and worker tasks inside Antigravity during ChatGPT Codex quota pauses |

---

## Standing `AiSwarmX` Root Head Mapping

| Root Head Session | Tool | Profile | Default / Permitted Model(s) |
| :--- | :--- | :--- | :--- |
| `AiSwarmX ChatGPT Codex` | ChatGPT Codex | `P2` | `GPT-6.2 Sol` *(spawns `GPT-6.2 Sol` / `GPT-6.2 Astra`)* |
| `AiSwarmX Claude Opus` | Claude Code | `P1` | `Opus 5.5` *(lean COO intake & steering on Anthropic plan; spawns `Opus 5.5` / `Sonnet 5.5`, plus `Claude Haiku 4.5` for read-only `Explore`)* |
| `AiSwarmX Antigravity Opus` | Antigravity | `P1` | `Opus 5.5` *(default Fresh `P1` Final Review & Acting COO pool on Google AI plan; spawns `Opus 5.5` / `Sonnet 5.5`)* |
| `AiSwarmX Antigravity Gemini` | Antigravity | `P3` | `Gemini 4 Carbon` *(execution turns via `Gemini 4 Hydrogen`; failover pool within allowed list incl. `GPT-OSS 120B`)* |

> **Bidirectional `Opus 5.5` Failover:** When Antigravity's third-party partner quota (`Opus 5.5` / `Sonnet 5.5` / `GPT-OSS 120B`) reaches `0%`, `Fresh Independent P1 Final Reviews` route cleanly via a fresh `Opus 5.5` sub-agent in Claude Code (`AiSwarmX Claude Opus`, separate Anthropic plan), and vice versa during Anthropic quota pauses.

---

## Prohibited & Superseded Models

- **`Claude Fable 5.2` (Strictly Prohibited):** Never use `Claude Fable 5.2` anywhere. `Opus 5.5` is the maximum permitted Claude tier (`P1`), `Sonnet 5.5` covers `P4`, and `Claude Haiku 4.5` is restricted solely to Claude Code read-only `Explore` searches.
- **Superseded / Unlisted Models:** Models outside the 8-model table above (e.g., `Gemini 4 Argon`, `Gemini 4 Neon`, `GPT-6 Astra`, `GPT-6.1 Sol`) must not be selected for new sessions, sub-agents, or reviews unless explicitly added to this allowlist by the maintainer (`GPT-6 Astra` / `gpt-6-astra` and `GPT-6.1 Sol` are superseded by `GPT-6.2 Astra` and `GPT-6.2 Sol`).
