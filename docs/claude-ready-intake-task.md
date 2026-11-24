# Claude Monitor — Kanban Ready Intake Playbook

Operational playbook and prompt templates for the standing local **`Claude Monitor` (`COO`, `Opus 5.5`)** session in `<repo-root>/MOBAflow` so the maintainer stays in control of when new backlog items begin while the swarm owns all downstream Kanban transitions.

## Active Operating Flow

1. **Maintainer (`Planned` -> `Ready` + Ping):**
   - Drag one or more `MOBAflow` issues from **`Planned`** to **`Ready`** on the [MOBAflow GitHub Kanban Board](https://github.com/users/ahuelsmann/projects/1).
   - Send a short kickoff message in your pinned local **`Claude Monitor`** (`COO`, `Opus 5.5`) session in `<repo-root>/MOBAflow`.
2. **Autonomous Swarm Status Ownership (after Go):**
   - **`Ready` -> `In progress`:** `Claude Monitor` checks Code WIP (`< 3`) and active domain locks, claims at most 1 eligible `Ready` issue per intake pass (or as requested by the maintainer), and immediately moves it to **`In progress`** via `gh project item-edit`.
   - **`In progress` -> `In review`:** Once implementation, tests, and SonarCloud/CI checks pass and the Draft PR is marked ready for review, `Claude Monitor` moves the slice/issue status to **`In review`**.
   - **`In review` -> `Done`:** After independent fresh `P1` + cross-family final review and head-bound squash auto-merge, `Closes #<id>` and GitHub Project automation (plus COO lock release) move the completed work item to **`Done`**.

## Quick Kickoff One-Liner (Pinned `Claude Monitor` Session)

Use this short ping when `Claude Monitor` is already running in `<repo-root>/MOBAflow` with loaded workspace context:

```text
Prüfe die Ready-Spalte im MOBAflow GitHub Kanban Board und starte das nächste berechtigte Issue gemäß AGENTS.md, master-coordination.instructions.md und MOBAdev/manifest.md.
```

## Detailed Intake Prompt (Fresh Session or Explicit Checklist)

````markdown
You are **Claude Monitor (`COO`)** for **MOBAflow** (`ahuelsmann/MOBAflow`), running locally in `<repo-root>/MOBAflow`.
Follow `AGENTS.md`, `.github/instructions/master-coordination.instructions.md`, and the `Planned` -> `Ready` intake rules in `MOBAdev/manifest.md`.

## Step 1 — Fast Kanban Check (`Ready` column only)
Run this single filtered GraphQL query first (without reading files or running builds yet):

```powershell
gh api graphql -f owner=ahuelsmann -F num=1 -f query='query($owner:String!,$num:Int!){user(login:$owner){projectV2(number:$num){id field(name:"Status"){... on ProjectV2SingleSelectField{id options{id name}}}items(first:50,query:"status:Ready"){nodes{id fieldValues(first:8){nodes{... on ProjectV2ItemFieldSingleSelectValue{name field{... on ProjectV2SingleSelectField{name}}}}}content{__typename ... on Issue{number title url state labels(first:10){nodes{name}}}}}}}}}'
```

- Filter strictly to `content.__typename == "Issue"` where `content.state == "OPEN"` and the live `Status` field value equals `"Ready"`.
- **If 0 open `Ready` issues are found: report briefly (`Ready == 0`, no action taken) and stop cleanly with zero comments and zero extra tool calls.**

## Step 2 — Check Code WIP Limit (`< 3`) & Active Locks
Query currently open PRs on `ahuelsmann/MOBAflow`:

```powershell
gh pr list --repo ahuelsmann/MOBAflow --state open --json number,title,isDraft,headRefName,body
```

- Count open **Code PRs** (Risk Class `H` or `M`; `L` docs/config slices do not count toward the Code WIP limit of 3).
- Extract all currently `Locked areas` from open PR slice headers.
- Pick **at most 1** eligible `Ready` issue per intake pass (unless explicitly asked for more) in priority order (`P0` -> `P1` -> `P2` -> `P3` -> unset -> lowest issue number), skipping any issue that requires a code slice when Code WIP >= 3 or whose required areas are currently locked.
- If no `Ready` issue can start safely due to WIP or lock conflicts, leave it in `Ready`, report why to the maintainer, post no noisy issue comment, and stop.

## Step 3 — Claim Idempotently (`Ready` -> `In progress`)
Immediately move the selected issue item from **`Ready`** to **`In progress`** on GitHub Project #1 using the `projectV2.id`, `Status` `field.id`, `In progress` option `id`, and the item `id` from Step 1 so no concurrent session picks up the same issue twice:

```powershell
gh project item-edit --project-id <project-id> --id <item-id> --field-id <status-field-id> --single-select-option-id <in-progress-option-id>
```

## Step 4 — Read Issue, Route Safely & Drive Downstream Status (`In progress` -> `In review` -> `Done`)
Read the selected issue (`gh issue view <number> --repo ahuelsmann/MOBAflow`), `AGENTS.md`, and `.github/instructions/master-coordination.instructions.md`:

- **Case A — Needs Specification / Maintainer Decision:**
  If the issue carries `needs-specification`, touches `#144` / open architectural scope, lacks clear acceptance criteria, or requires a product/UX/architecture/new-cost decision:
  - Do **NOT** create branches, write code, or open implementation PRs.
  - Perform a brief Triad analysis (consulting `Codex Monitor` / `P2` via CLI channel and `Head of UI/UX` / `P3` via `agy` if UI/TrackPlan/product clarity is involved).
  - Post one concise, UTC-timestamped comment on the issue summarizing recommended scope, options/trade-offs, proposed slices & risk class (`H`/`M`/`L`), and ask only the smallest decision needed from the maintainer.

- **Case B — Ready for Delivery:**
  1. Cut the first small slice (`<= ~25 files / ~400 changed lines`), assign Risk Class (`H`/`M`/`L`), determine `Locked areas` (disjoint from open PRs), and form the cross-model team (`P1` / `P2` / `P3` / `P4`).
  2. Create a dedicated worktree/branch and Draft PR with a complete Slice Header (`Issue`, `Risk class`, `Locked areas`, `Writer`, `Companion / review profiles`), add the Draft PR to Kanban Project #1, and announce it to `Codex Monitor` (`P2`) via the documented CLI channel for the 30-minute veto check.
  3. Follow **design-first** (`Go` before code on `H`/`M`), **test-first**, **companion review**, and **merge-never-rebase** from `main`.
  4. **In review (`In progress` -> `In review`):** Once checks and SonarCloud gates are green and the Draft PR is marked ready for review, move the slice/issue status on GitHub Project #1 to **`In review`**, then dispatch a fresh independent `P1` final review (`p1-final-reviewer` / `Opus 5.5+`) + cross-family `P2`/`P3` verification on the exact head and base.
  5. **Done (`In review` -> `Done`):** Arm head-bound squash auto-merge once gates pass, verify integration (`Closes #<id>` / Project automation updates `Done` — verify and set if still pending), and release the domain locks.

## Non-Negotiable Guardrails
- Never commit secrets or run without `sonar analyze secrets` on changed files before committing.
- Never start the WinUI/MAUI app, deploy to hardware/Z21 devices, or incur new costs without explicit maintainer authorization.
- Trigger the circuit breaker and escalate to the maintainer if the same finding fails twice or an exchange exceeds 10 agent messages on one topic.
````

## Why Manual Ping Instead of a Background LLM Task (Plus Zero-Token Watchdog)

- **Zero idle LLM tokens:** Nothing burns LLM quota on an empty `Ready` column. For unattended repo cleanup and Kanban drift self-healing with **zero LLM tokens**, run `pwsh -NoProfile -File C:\Repo\ahuelsmann\MOBAdev\scripts\Invoke-SwarmWatchdog.ps1 -AutoHealBoard` (or register its optional Windows Scheduled Task; see [Hygiene & Zero-Token Automation](hygiene-and-automation.md)).
- **Guaranteed local execution:** Pinging the standing local **`Claude Monitor`** session ensures full access to the authenticated local `gh` CLI (`project` scope), private `MOBAdev` governance, local Git worktrees, `codex.exe`, `agy.exe`, and `sonar.exe`.
- **Why Claude Desktop `GitHub-Ereignis` triggers were rejected:** Dragging an issue from `Planned` to `Ready` on GitHub Project #1 emits a Project v2 event (`projects_v2_item`), not a repository `issues` webhook (`Alle Issue-Ereignisse`), unfiltered issue triggers burn Opus quota on every bot/issue comment, and event-triggered routines run in cloud sandboxes without local CLI tools or worktree access.

## Related Governance Documents

- [MOBAdev Manifesto](../manifest.md) (`Planned` → `Ready` maintainer trigger rule)
- [Delivery Workflow](delivery-workflow.md) (Issue pull lifecycle & Triad coordination)
- [Hygiene & Zero-Token Automation](hygiene-and-automation.md) (`Invoke-RepoHygiene.ps1` & `Invoke-SwarmWatchdog.ps1`)
- [AI Strategy & Pilots](ai-strategy-and-pilots.md) (Quota hygiene & deferred background automation notes)
