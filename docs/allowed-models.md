# Allowed AI Models (`MOBAdev` / `MOBAi`)

**Status:** Binding allowlist across **ChatGPT Codex**, **Claude Code**, and **Antigravity** (both standing `MOBAi` Root Heads and spawned runtime sub-agents, as well as new projects adopting this governance).

Only the **6 approved AI models** listed in this catalog may be selected or dispatched. Unlisted or superseded models require explicit maintainer approval and an update to this document before use.

---

## Approved Models

| Model | Identifier / Alias | Family | Profile | Permitted Tools | Primary Responsibility |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`GPT-6.2 Sol`** | `gpt-6.2-sol` | OpenAI (`X`) | `P2` | ChatGPT Codex (`MOBAi ChatGPT Codex`) | Technical Director & CPM, repo/build-graph/Spec-Kit precision, `X`-family implementation & cross-family reviews |
| **`Opus 5.5`** | `claude-opus-5-5` / `Claude Opus 5.5` | Anthropic (`C`) | `P1` | Claude Code (`MOBAi Claude Opus`), Antigravity (`MOBAi Antigravity Opus`) | COO / Acting COO, deep architecture, concurrency/safety invariants, mandatory Fresh Independent `P1` Final Review (hard Claude ceiling) |
| **`Sonnet 5.5`** | `claude-sonnet-5-5` / `Claude Sonnet 5.5` | Anthropic (`C`) | `P4` | Claude Code, Antigravity | Fast routine code/test/doc slices and triage |
| **`Gemini 4 Carbon`** | `gemini-4-carbon-medium` / `Orchestrator · Gemini 4 Carbon` | Google (`G`) | `P3` | Antigravity (`MOBAi Antigravity Gemini`) | Head of AI Strategy, system context, Product & UI/UX architecture, `G`-family orchestration & cross-family review |
| **`Gemini 4 Hydrogen`** | `gemini-4-hydrogen-xhigh` | Google (`G`) | `P4` | Antigravity | Fast inner execution turns, bounded inspections, and routine worker tasks *(never used as standalone final sign-off reviewer)* |
| **`GPT-OSS 120B`** | `gpt-oss-120b` | Open-weight (`X`-family failover) | `P2` (Failover) | Antigravity | Cross-family `P2` failover verification and worker tasks inside Antigravity during ChatGPT Codex quota pauses |

---

## Standing `MOBAi` Root Head Mapping

| Root Head Session | Tool | Profile | Default / Permitted Model(s) |
| :--- | :--- | :--- | :--- |
| `MOBAi ChatGPT Codex` | ChatGPT Codex | `P2` | `GPT-6.2 Sol` |
| `MOBAi Claude Opus` | Claude Code | `P1` | `Opus 5.5` *(spawns `Opus 5.5` / `Sonnet 5.5`)* |
| `MOBAi Antigravity Opus` | Antigravity | `P1` | `Opus 5.5` *(sub-agent pool within allowed list)* |
| `MOBAi Antigravity Gemini` | Antigravity | `P3` | `Gemini 4 Carbon` *(execution turns via `Gemini 4 Hydrogen`; failover pool within allowed list incl. `GPT-OSS 120B`)* |

---

## Prohibited & Superseded Models

- **`Claude Fable 5.2` (Strictly Prohibited):** Never use `Claude Fable 5.2` anywhere. `Opus 5.5` is the maximum permitted Claude tier (`P1`), and `Sonnet 5.5` covers `P4`.
- **Superseded / Unlisted Models:** Models outside the 6-model table above (e.g., `Gemini 4 Argon`, `Gemini 4 Neon`, `GPT-6 Astra`, `GPT-6.1 Sol`) must not be selected for new sessions, sub-agents, or reviews unless explicitly added to this allowlist by the maintainer.
