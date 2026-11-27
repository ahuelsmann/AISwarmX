# Rebuild MOBAflow development on a new notebook

Use this runbook to recreate the development environment, agent roles, and working handoffs from AISwarmX. The notebook owner handles account recovery and restoration of live work separately. This runbook is ready to follow as a checklist. A clean-profile rebuild has yet to be exercised, and the [setup inventory](setup-inventory.md) lists configuration gaps.

## 1. Establish access and the workspace

- Sign in to GitHub and verify access to AISwarmX, MOBAflow, and any other repository named by an assigned role. Recover provider and connector access using the notebook owner's chosen process.
- Install Git, the approved secrets scanner, and the current approved versions of Codex, Claude Code, and Antigravity from their official sources. Use the MOBAflow product instructions for .NET, SDKs, workloads, builds, and test prerequisites.
- Clone AISwarmX and MOBAflow as separate working copies. Record the AISwarmX commit used for the rebuild. Run the required standalone secrets scan before reading workspace files.
- Consult the notebook owner for restored work in progress. Before starting a new delivery, have the COO or designated coordinator reconcile the recovered queue with active task branches, `mobai-swarm-bus` queue/lock claims, owners, protected worktrees, and manual acceptance states. Treat missing evidence as an open recovery item.

## 2. Recreate roles and their effective configuration

Start with the COO and Technical Director. Recreate the Executive Assistant and any delegated coordination role used for the handover. Continue with the named delivery, review, quality, security, UI/UX, and communications roles needed for the next assignment. Use the [setup inventory](setup-inventory.md) to distinguish verified definitions from references that still need confirmation.

| Tool | What to establish | What to verify in the running role |
| --- | --- | --- |
| [Codex](https://learn.chatgpt.com/docs/config-file/config-basic) | Approved user or project profile, role instructions, project and working directory, skill paths, required connectors, and relevant automations | The intended role, model, permissions, effective configuration, and correct skill revision in the desktop or CLI context actually used |
| [Claude Code](https://code.claude.com/docs/en/settings) | Project and user configuration, role definition or named chat, skills, hooks, required connectors, and any approved hosted review plan | Effective settings and model for the intended local or hosted context, plus the reviewer identity, trigger, and result route for the existing independent review |
| [Antigravity](https://antigravity.google/docs/customization-discovery) | Workspace, applicable user and workspace configuration, role definitions or named chats, skills, and the intended app and CLI entry points | Correct workspace and role in each surface. Qualify the actual CLI destination and return route before treating it as a replacement for a desktop handoff. An [App conversation import](https://antigravity.google/docs/cli/commands/resume/#importing-from-the-antigravity-app) creates a CLI copy. |

Load the versioned skill explicitly when testing its availability. Record its source path and revision. Keep provider login state, tokens, and machine-local bindings in their approved stores. When a role, setting, hook, permission, or hosted trigger is missing from the inventory, have its configuration owner confirm it before enabling it. Keep scheduled or event-driven actions inactive until the owner verifies scope, target, and duplicate-run protection.

## 3. Verify the required handoffs

Use the next suitable assigned work item and the [Delivery Handoff skill](../skills/mobaflow-delivery-handoff/SKILL.md). Choose a bounded task with a real result and no need for deployment or device operation. The COO or designated coordinator uses the existing work record and names the sender, receiving role, owner, permitted actions, expected output, and version or comparison.

- Verify the Codex coordination route and the required Claude Code and Antigravity routes individually. For any route used, record app or CLI, the verified target role and workspace, and how the result returns to the COO or designated coordinator.
- Record delivery, acceptance, execution, result, and any writer release as separate observations. Use the approved fallback when the primary route is unavailable, and record that the primary route remains unverified.
- Reconcile the independent review route and any required gates with the actual delivery workflow before the first release. Keep formal CI, review, and practical acceptance tied to the specific work item.

## 4. Hand the rebuilt environment to the COO

Return one recovery handoff with the AISwarmX commit, restored roles and their effective configurations, verified skill versions, workflow and trigger checks, and the observed handoff results. List each open item with its owner and next step.

Mark the rebuild complete for the intended scope once the essential roles are usable, a regular assignment has travelled to the needed tools and back with an accepted result, and the COO or designated coordinator has reconciled the recovered operational state. Keep unavailable or untested roles and channels visible in the handoff.
