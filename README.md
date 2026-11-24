# MOBAdev

MOBAdev is the versioned setup for the [MOBAflow](https://github.com/ahuelsmann/MOBAflow) development team. It documents how AI agents are configured and how they collaborate across Codex, Claude Code, and Antigravity.

## What belongs here

- **Roles and agents** define responsibilities, decision rights, and skill assignments.
- **Skills** describe repeatable procedures in reusable `SKILL.md` files.
- **Tool configuration** records how each supported tool discovers agent definitions and skills.
- **Workflows** document how the team assigns work, hands off evidence, reviews changes, and verifies acceptance.

Project-specific build and validation rules live with the product code. Claude, as Chief Operating Officer (COO), leads development and owns operational assignments. The [role configuration register](docs/role-configurations.md) tracks evidence of the transition.

## Delivery workflow

Read the [MOBAflow agent workflow manifesto](manifest.md) for the active team principles and use the [small-batch delivery guide](docs/delivery-workflow.md) to find the board, roles, risk classes (`H`/`M`/`L`) and review flow for MOBAflow work.

- [Versioned agent and rule backups](agents/README.md) for Antigravity and Claude Code
- [Allowed AI models](docs/allowed-models.md) — binding allowlist of permitted models across Codex, Claude Code, and Antigravity
- [Hygiene & zero-token automation](docs/hygiene-and-automation.md) — lossless worktree/branch pruning (`Invoke-RepoHygiene.ps1`) and zero-token Kanban/PR watchdog (`Invoke-SwarmWatchdog.ps1`)
- [AI strategy and orchestration pilots](docs/ai-strategy-and-pilots.md) (`Orchestrator (Neon)` & `Sol 6.2` monitor strategy, `Gemini 4 Carbon` decision record)
- [MOBAdev Agent Swarm Gen#3 Dashboard design concept](dashboard/DESIGN.md) (read-only local ASP.NET Core + React cockpit — launch detached via `.\dashboard\Start-Dashboard.ps1 -Background` or enable Windows logon autostart via `.\dashboard\Start-Dashboard.ps1 -RegisterAutoStart`)
- [Changelog](CHANGELOG.md) — versioned history of `MOBAdev` and the Agent Swarm dashboard

## Status

For a new notebook, follow the [rebuild runbook](docs/rebuild.md).

The [setup inventory](docs/setup-inventory.md) records current sources and remaining configuration checks.

The first three pilot skills are available:

- [Delivery Handoff](skills/mobaflow-delivery-handoff/SKILL.md)
- [Threat Modeling](skills/mobaflow-threat-modeling/SKILL.md)
- [Practical Acceptance](skills/mobaflow-practical-acceptance/SKILL.md)

Skill activation is verified separately in each target tool.

## License

[MIT License](LICENSE).
