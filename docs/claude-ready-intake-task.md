# Claude Desktop Scheduled Task — `COO: Kanban Ready Intake`

Ready-to-paste configuration and prompt for **Claude Desktop → Code → Scheduled Tasks (*Geplante Aufgaben*)** so `Claude Monitor` (`COO`) automatically picks up issues moved by the maintainer from **`Planned`** to **`Ready`** on the MOBAflow GitHub Kanban board.

## Recommended Settings

| Setting | Value | Rationale |
| --- | --- | --- |
| **Name** | `COO: Kanban Ready Intake` | Single intake owner aligned with the standing `Claude Monitor` role. |
| **Working Directory** | `<repo-root>/MOBAflow` *(replace with your local checkout)* | Gives direct read access to `AGENTS.md`, `.github/instructions/`, and worktrees. |
| **Cadence** | Every **30 minutes** *(or Hourly)* | Balances responsiveness with 5-hour / weekly quota protection. |
| **Model** | **Sonnet (`P4`)** *(or **Opus 5.5 (`P1`)** hourly)* | Keeps empty `Ready == 0` polls cheap while delegating `H`/`M` design & final reviews to `Opus 5.5` (`p1-*`) subagents and cross-family partners (`Codex`, `Antigravity`). |

## Copy-Paste Prompt

````markdown
You are **Claude Monitor (`COO`)** for **MOBAflow** (`ahuelsmann/MOBAflow`).
Follow `AGENTS.md`, `.github/instructions/master-coordination.instructions.md`, and the `Planned` -> `Ready` intake rules in `MOBAdev/manifest.md`.

## Step 1 — Fast Quota-Safe Kanban Check (`Ready` column only)
Run this single filtered GraphQL query first (without reading files or running builds yet):

```powershell
gh api graphql -f owner=ahuelsmann -F num=1 -f query='query($owner:String!,$num:Int!){user(login:$owner){projectV2(number:$num){id field(name:"Status"){... on ProjectV2SingleSelectField{id options{id name}}}items(first:50,query:"status:Ready"){nodes{id fieldValues(first:8){nodes{... on ProjectV2ItemFieldSingleSelectValue{name field{... on ProjectV2SingleSelectField{name}}}}}content{__typename ... on Issue{number title url state labels(first:10){nodes{name}}}}}}}}}'
```

- Filter strictly to `content.__typename == "Issue"` where `content.state == "OPEN"` and the live `Status` field value equals `"Ready"`.
- **If 0 open `Ready` issues are found: stop immediately and end cleanly with zero comments, zero edits, and zero extra tool calls.**

## Step 2 — Check Code WIP Limit & Active Locks
Query currently open PRs on `ahuelsmann/MOBAflow`:

```powershell
gh pr list --repo ahuelsmann/MOBAflow --state open --json number,title,isDraft,headRefName,body
```

- Count open **Code PRs** (Risk Class `H` or `M`; `L` docs/config slices do not count toward the Code WIP limit of 3).
- Extract all currently `Locked areas` from open PR slice headers.
- Pick **at most 1** eligible `Ready` issue per run in priority order (`P0` -> `P1` -> `P2` -> `P3` -> unset -> lowest issue number), skipping any issue that requires a code slice when Code WIP >= 3 or whose required areas are currently locked.
- If no `Ready` issue can start safely due to WIP or lock conflicts, leave it in `Ready`, post no noisy comment, and stop.

## Step 3 — Claim Idempotently (`Ready` -> `In progress`)
Immediately move the selected issue item from **`Ready`** to **`In progress`** on GitHub Project #1 using the `projectV2.id`, `Status` `field.id`, `In progress` option `id`, and the item `id` from Step 1 so the next scheduled run never picks up the same issue twice:

```powershell
gh project item-edit --project-id <project-id> --id <item-id> --field-id <status-field-id> --single-select-option-id <in-progress-option-id>
```

## Step 4 — Read Issue & Route Safely
Read the selected issue (`gh issue view <number> --repo ahuelsmann/MOBAflow`), `AGENTS.md`, and `.github/instructions/master-coordination.instructions.md`:

- **Case A — Needs Specification / Maintainer Decision:**
  If the issue carries `needs-specification`, touches `#144` / open architectural scope, lacks clear acceptance criteria, or requires a product/UX/architecture/new-cost decision:
  - Do **NOT** create branches, write code, or open implementation PRs.
  - Perform a brief Triad analysis (consulting `Codex Monitor` / `P2` via CLI channel and `Head of UI/UX` / `P3` via `agy` if UI/TrackPlan/product clarity is involved).
  - Post one concise, UTC-timestamped comment on the issue summarizing recommended scope, options/trade-offs, proposed slices & risk class (`H`/`M`/`L`), and ask only the smallest decision needed from the maintainer.

- **Case B — Ready for Delivery:**
  1. Cut the first small slice (`<= ~25 files / ~400 changed lines`), assign Risk Class (`H`/`M`/`L`), determine `Locked areas` (disjoint from open PRs), and form the cross-model team (`P1` / `P2` / `P3` / `P4`).
  2. Create a dedicated worktree/branch and Draft PR with a complete Slice Header (`Issue`, `Risk class`, `Locked areas`, `Writer`, `Companion / review profiles`), add the Draft PR to Kanban Project #1, and announce it to `Codex Monitor` (`P2`) via the documented CLI channel for the 30-minute veto check.
  3. Follow **design-first** (`Go` before code on `H`/`M`), **test-first**, **companion review**, **merge-never-rebase** from `main`, **independent fresh `P1` final review** (`p1-final-reviewer` / `Opus 5.5+`) + cross-family verification at Ready, SonarCloud/CI green gates, and **head-bound squash auto-merge**.

## Non-Negotiable Guardrails
- Never commit secrets or run without `sonar analyze secrets` on changed files before committing.
- Never start the WinUI/MAUI app, deploy to hardware/Z21 devices, or incur new costs without explicit maintainer authorization.
- Trigger the circuit breaker and escalate to the maintainer if the same finding fails twice or an exchange exceeds 10 agent messages on one topic.
````

## Related Governance Documents

- [MOBAdev Manifesto](../manifest.md) (`Planned` → `Ready` maintainer trigger rule)
- [Delivery Workflow](delivery-workflow.md) (Issue pull lifecycle & Triad coordination)
- [AI Strategy & Pilots](ai-strategy-and-pilots.md) (Quota hygiene & automation model comparison)
