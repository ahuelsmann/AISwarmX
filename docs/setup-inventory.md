# MOBAdev setup inventory

Versioned setup for the MOBAflow development department. This initial inventory records intended ownership and placement as of 2026-11-05. Live activation is verified separately for each agent.

| Source | What is verified | Proposed placement |
| --- | --- | --- |
| MOBAflow product root `AGENTS.md` | Contains repository workflow, architecture, build, and validation rules | Reference the product file. Extract shared department policy only after its owner agrees on the boundary. |
| MOBAflow product root `CLAUDE.md` | Imports `AGENTS.md` and describes Claude-specific skill mirrors | Reference the product entry point. Review mirror ownership before changing either copy. |
| Product `.agents/skills/` and `.claude/skills/` | Spec Kit, quality audit, diagnosis, and review entries were found. Diagnosis and review are documented as mirrored. | Keep product-scoped skills at their current source. Determine the source and revision for any shared candidate. |
| Product checkout `skills/` | A separate collection of templates is present. Its two handoff entries summarize a conversation or start a new background Claude agent. | Review individually for provenance, license, and relevance before reuse. Use the delivery handoff pilot for evidence and owner transfers in the existing queue. |
| Management `TEAM-COMMUNICATION.md`, `CLI-COMMUNICATION.md`, and `API-first-Entwicklungsablauf-2026-10-23.md` | Existing references for communication and delivery. Some text is historical or tied to local sessions. | Derive current, sanitized workflows. Confirm each rule with the Project Manager before making it canonical. |
| Three pilot skills in `skills/` | Original drafts passed the standard validator. The Project Manager reported checking queue references to the drafts. | Start shared versioning here and verify each target's active version during its first use. |
| Codex, Claude Code, and Antigravity live role configuration | The pilot role assignments are known. Exact live definitions, discovery scope, and writable configuration owners are still to be verified. | Add sanitized native definitions or launch configuration under `agents/` and `tools/` when their owners confirm the source. |

## Provider coverage

See the [role configuration evidence](role-configurations.md) for verified role records and open runtime checks.

Inventory each provider independently across project files, user defaults, and the effective runtime. Include app and CLI settings and any cloud or hosted configuration that a role actually uses. The table lists sources to check. It does not certify that they have been captured.

| Provider | Configuration sources to check | Provider-specific verification |
| --- | --- | --- |
| Codex | MOBAflow project configuration, user profiles, agent instructions, skills, hooks, rules, MCP and plugins, desktop projects and chats, and hosted automations used by the team | Record the effective model, working context, skill version, permissions, and owner of each relevant automation. |
| Claude Code | `CLAUDE.md`, project and user settings, agent definitions, skills, hooks, MCP and plugins, local CLI and desktop roles, and hosted plans or scheduled runs used by the team | Distinguish a named conversation from a reusable definition. Verify the target role, configuration sources, and evidence returned by the actual review or delivery path. |
| Antigravity | Workspace and user rules, custom agents, skills, hooks, MCP and plugins, desktop projects and roles, CLI profiles and role bindings, and hosted configuration where used | Verify the workspace, role, and supported route independently in app and CLI. Record whether a route addresses the original role or starts a separate conversation. |

For each source, record the owner, the authoritative location, the scope and precedence, the safe versioning or recreation method, and the last verification. Include reusable decisions or workflow knowledge currently kept only in a chat or memory as a reviewed summary with its owner. Record external dependencies and manual setup steps separately from reusable configuration. Keep authentication material, raw conversation history, personal memory, and machine-local session identifiers outside the repository.

For each required cross-tool handoff, record the sender and receiving role, outbound channel, return channel, acceptance signal, fallback, and last observed result. Check the role and workspace before sending. Treat successful delivery, recipient acceptance, and returned work as separate evidence. The Project Manager owns the operational routing and queue.

## First rollout

| Skill | Initial role | Verification |
| --- | --- | --- |
| Delivery Handoff | Project Manager, Technical Director, delivery owner | Use in the next regular handoff and capture the resolved version and result. |
| Threat Modeling | Security Engineer | Verify discovery in the intended Claude Code configuration and use on an assigned security case. |
| Practical Acceptance | Quality Manager and Head of UI/UX | Confirm access from the Antigravity role and use on a scoped acceptance case. Keep test execution subject to its assignment. |

The Project Manager owns the existing operational queue. Use the setup repository for durable role definitions, reusable skills, and tool configuration. Keep credentials, chats, personal memory, machine-local identifiers, and live queue records out of version control. Review each imported template's provenance and license.
