# Small-batch delivery for MOBAflow

The development team uses this workflow for work on MOBAflow across Codex, Claude Code and Antigravity. It was adopted on 2026-11-16 in [MOBAflow PR #468](https://github.com/ahuelsmann/MOBAflow/pull/468). The [MOBAflow master coordination guide](https://github.com/ahuelsmann/MOBAflow/blob/main/.github/instructions/master-coordination.instructions.md) specifies the full operating rules, exceptions and gate commands. This page is the entry point from the shared team setup.

## Coordinate work

- The COO (Claude) records each slice on the [MOBAdev board](https://github.com/ahuelsmann/MOBAdev/issues/1) with its issue, PR, writer, locked areas and current state. The Chief Project Manager (Codex) prioritizes the backlog with the COO and can veto a slice or lock within 30 minutes.
- A slice addresses one concern and can merge independently. Aim for at most about 25 files or 400 changed lines and use one Draft PR per slice. Only the named writer pushes, subject to the COO's branch-update exception in the master coordination guide.
- Keep at most three code slices open and at most two local builders running. Code slices use disjoint locks. Documentation-only slices lock their edited files and do not count toward the code limit.
- Record locks, design decisions, handovers, merges and escalations on the board or in its issue with UTC time and role. Keep backlog items in the GitHub Project. Keep PR design and review evidence in the PR.

## Deliver a slice

1. Describe the goal, affected files, approach, risks and tests in a short design note. Obtain the design Go from the COO or Solution Architect before code changes. Use the applicable Spec Kit requirements and the exceptions in the master coordination guide.
2. Write the acceptance tests first for new behavior. Before handing on a branch, merge the configured GitHub remote's current `main` into it and rerun affected checks. Shared branches are never rebased or force-pushed.
3. The COO marks the Draft Ready after the applicable checks and SonarCloud evidence are current. A fresh independent Claude session using Opus 5.5 or higher reviews the comparison. The writer fixes findings and changed code receives an independent re-review.
4. The COO enables GitHub auto-merge for the reviewed commit once review findings, SonarCloud and the applicable mutation jobs meet the gates. GitHub enforces the required checks. Follow the master coordination guide if the branch or its base changes.
5. Verify the integration and release the lock. Record affected developer documentation and user wiki pages or why they need no change.

## Restore the setup

Use the [rebuild runbook](rebuild.md) to configure each role on a new machine. In a MOBAflow checkout, follow the project's [AGENTS.md](https://github.com/ahuelsmann/MOBAflow/blob/main/AGENTS.md), Claude entry point and repository skill paths. For each tool, verify the effective instructions and loaded skill revision separately. The [setup inventory](setup-inventory.md) tracks the remaining verification. App, device and hardware actions require explicit maintainer authorization.
