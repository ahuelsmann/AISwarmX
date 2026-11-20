# Versioned Agents and Rules

This folder stores sanitized, versioned backups of custom agents and rules across **Antigravity** and **Claude Code**. Machine-local paths, session IDs, thread UUIDs, and credentials are intentionally excluded.

## Target installation paths

| Tool | Source in `MOBAdev` | Target directory on machine |
| --- | --- | --- |
| Antigravity custom agents | `agents/antigravity/agents/*.md` | `~/.gemini/config/agents/` |
| Antigravity global rules | `agents/antigravity/rules/*.md` | `~/.gemini/config/rules/` |
| Claude Code custom agents | `agents/claude/agents/*.md` | `~/.claude/agents/` |

When copying `agents/antigravity/agents/head-of-ai-strategy.md` or `agents/antigravity/rules/mobaflow-agent-network.md` to a local machine, replace `<repo-root>/MOBAflow` with your local `MOBAflow` checkout path if desired.

For **`Claude Monitor` (`COO`) Kanban `Ready` intake** (maintainer `Planned` → `Ready` + manual ping in the standing local `Claude Monitor` session), see the kickoff playbook and prompt template in [`docs/claude-ready-intake-task.md`](../docs/claude-ready-intake-task.md).

## Antigravity frontmatter rule

Do **not** add a raw `model: <model-id>` field (such as `gemini-pro-orchestrator-neon`) to the YAML frontmatter of `.md` files in `~/.gemini/config/agents/`. The Antigravity loader silently ignores agent files with explicit model IDs in frontmatter. Instead, select the model in the Antigravity UI picker or via CLI flag (`agy --agent <name> --model <model-id>`).

## Verify Antigravity discovery

Run on Windows PowerShell:

```powershell
& "$env:LOCALAPPDATA\agy\bin\agy.exe" agents
```

Expected output lists `head-of-ai-strategy`, `head-of-mobanews`, and `head-of-ui-ux`.
