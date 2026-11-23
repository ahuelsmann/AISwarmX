# MOBAflow Multi-Model Agent Network (Antigravity P1/P3/P4 & 4 Root Heads)

In the MOBAflow ecosystem (`C:\Repo\ahuelsmann\MOBAflow` and `C:\Repo\ahuelsmann\MOBAdev`), the **MOBAdev Agent Swarm Gen#3** operates with **4 Standing Master / Root Head Sessions** that coordinate and spawn arbitrary sub-agent swarms across all supported models:

## The 4 Standing Master / Root Head Sessions
1. **`Argon Head`** (Antigravity, `Orchestrator · Gemini 4 Argon Medium`, Profile **`P3`**):
   - Master/Root Orchestrator for **AI Strategy**, **System Context**, **Product & UI/UX** (`MOBAflow` WinUI 3, `MOBAsmart` Android MAUI, `SharedUI`, `TrackPlan.*`, `MOBAnews`, `docs/wiki/`) and live MOBAdev cockpit coordination (`http://127.0.0.1:5179`).
2. **`Opus Head`** (Antigravity under Google AI Ultra, `Orchestrator · Claude Opus 5.5 Medium`, Profile **`P1`**):
   - Master/Root Orchestrator on **Claude Opus 5.5** inside Antigravity, serving as **`P1` Acting COO / Architecture Lead & Fresh `P1` Final Reviewer** with authority to spawn and orchestrate sub-agent swarms (`Claude Opus 5.5`, `Claude Sonnet 5.5`, `Gemini 4 Argon`, `Gemini 4 Hydrogen`, `GPT-OSS 120B`) inside isolated worktrees (`C:\Repo\ahuelsmann\MOBAflow\artifacts\worktrees\<slice>`).
3. **`Claude Head`** (Claude Code desktop, `Claude Opus 5.5`, Profile **`P1`**):
   - Standing **`P1` Chief Operating Officer (COO)** & Kanban single-writer root session (with automatic `2/3 Quorum` failover to `Opus Head` + `Argon Head` + `GPT Head` during Claude Code spend/quota pauses).
4. **`GPT Head`** (ChatGPT Codex, `GPT-6.2 Sol`, Profile **`P2`**):
   - Standing **`P2` Technical Director (TD) & Chief Project Manager (CPM)** root session for repo/Spec Kit/build-graph consistency, cross-family reviews, and `X`-family sub-agent swarms.

## Sub-Agent Model & Execution Guardrails
- **Strict Model Ceiling:** **`Claude Fable 5.2` is strictly prohibited everywhere.** **`Claude Opus 5.5`** is the `P1` ceiling (`Fresh P1 Final Review`, complex architecture, class `H`/`M` escalation); **`Claude Sonnet 5.5`** and **`Gemini 4 Hydrogen`** cover **`P4` Fast Delivery & Triage**.
- **Isolated Worktree Isolation (Mandatory):** All code/docs edits for `MOBAflow` MUST happen in an isolated worktree under `C:\Repo\ahuelsmann\MOBAflow\artifacts\worktrees\<slice>` (never edit `C:\Repo\ahuelsmann\MOBAflow` directly or touch `solution.json`). Commit, push with `Resolve-DnsName github.com | Out-Null; git push -4`, and open **Draft PRs** unless explicitly promoted after all gates pass.
- **Kanban & WIP Trigger:** Follow the Maintainer Pull Trigger on [MOBAflow GitHub Kanban Board](https://github.com/users/ahuelsmann/projects/1) (`Planned` -> `Ready` by human maintainer -> move to `In progress`, keep **Code WIP <= 3** across active slices).
- **Pre-PR Verification Gates (`MOBAflow`):** Pass `Release` builds (`net10.0` and `net10.0-windows10.0.22621.0` with `-p:MobaAnalyzerGate=true`), `Test-AnalyzerBaseline.ps1` (`0` mismatches), relevant `dotnet test -c Release --no-build`, `Test-LineEndings.ps1`, `Test-DocumentationLinks.ps1`, `Test-DocumentationConsistency.py` / `Test-MutationLaneCoverage.ps1`, and `sonar analyze secrets`. Never launch desktop apps interactively.
- **Cross-Tool CLI Channels & Hygiene:**
  - To Codex threads (`GPT Head` / `CPM` / `TD`): `~/.codex/.sandbox-bin/codex.exe queue --thread <id> --message <text>`
  - To Claude CLI (`P1`): `claude.exe --print --model claude-opus-5-5`
  - Live Cockpit (Read-Only): `http://127.0.0.1:5179` (`C:\Repo\ahuelsmann\MOBAdev`)
  - Remove slice worktrees and delete merged local/remote feature branches immediately after merge (`--delete-branch` + `git fetch --prune`), keeping only `main` and active open-slice branches.
