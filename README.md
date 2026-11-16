# MOBAdev

MOBAdev is the versioned setup for the [MOBAflow](https://github.com/ahuelsmann/MOBAflow) development team. It documents how AI agents are configured and how they collaborate across Codex, Claude Code, and Antigravity.

## What belongs here

- **Roles and agents** define responsibilities, decision rights, and skill assignments.
- **Skills** describe repeatable procedures in reusable `SKILL.md` files.
- **Tool configuration** records how each supported tool discovers agent definitions and skills.
- **Workflows** document how the team assigns work, hands off evidence, reviews changes, and verifies acceptance.

Project-specific build and validation rules live with the product code. Claude, as Chief Operating Officer (COO), leads development and owns operational assignments. The [role configuration register](docs/role-configurations.md) tracks evidence of the transition.

## Delivery workflow

Use the [small-batch delivery guide](docs/delivery-workflow.md) to find the board, roles and review flow for MOBAflow work.

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
