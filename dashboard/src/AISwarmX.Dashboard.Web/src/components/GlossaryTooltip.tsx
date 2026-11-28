import type { ReactNode } from 'react';
import { Tooltip, tokens } from '@fluentui/react-components';
import { QuestionCircle16Regular } from '@fluentui/react-icons';

const GLOSSARY_ENTRIES: Record<string, string> = {
  P1: 'Profile P1 (Depth, Architecture & Safety): Claude Opus 5.5 for deep architecture, Z21 protocol safety, and mandatory independent final review.',
  P2: 'Profile P2 (Repo, Build & Fact Precision): GPT-6.2 Sol / GPT-6.2 Astra for solution build graph (Moba.slnx), CI/Sonar checks, and technical cross-verification.',
  P3: 'Profile P3 (System Context, Product & UI/UX): Gemini 4 Carbon (AiSwarmX Antigravity Gemini) for large multi-file context, WinUI/MAUI UI design, and clear documentation.',
  P4: 'Profile P4 (Cadence & Fast Delivery — Magenta-Orchid): Fast models (Claude Sonnet 5.5 / Gemini 4 Hydrogen; Claude Fable 5.2 prohibited) for routine slices, documentation, and quick triage.',
  H: 'Risk Class H (High): Safety-critical areas (Backend, Z21, ESP32). Requires design Go and independent review across two model families (P1 + P2).',
  M: 'Risk Class M (Medium): Product features, UI, audio, or MOBAnews. Requires companion review plus P3 UI/UX check for user-facing slices.',
  L: 'Risk Class L (Low): Documentation, wiki, or small non-behavioral fixes. Requires final P1 review plus cross-family check.',
  Drift:
    'Difference between the local configuration file on this machine (Actual) and the versioned baseline in AISwarmX (Target).',
  Worktree:
    'Separate Git working directory for an isolated branch so parallel tasks stay cleanly separated.',
  CLEAN: 'Branch can be merged cleanly without conflicts or missing commits from main.',
  BEHIND:
    'Branch is behind current main. Merge main into the branch before merging (never rebase shared branches).',
  UNSTABLE:
    'Checks Pending or Checks Failing: one or more automated GitHub build, test, or Sonar checks are still running or failed.',
  BLOCKED:
    'GitHub blocks merging right now (for example Draft PR, running required checks, or missing/requested review).',
  DIRTY:
    'Branch has merge conflicts with main. Merge main in the writer worktree and resolve conflicts first.',
  'Auto-Merge':
    'GitHub merges the PR automatically once all required checks and reviews are green.',
  'Read-Only':
    'This cockpit only reads and displays live state. It never writes, edits, merges, or starts anything.',
  Triad:
    'Decision & Steering Triad: Cross-model council (P2 CPM/TD, P1 COO, P3 AI Strategy & UI/UX) that cuts tasks into slices, assigns risk classes (H/M/L), selects participating agents and AI models, and grants Design Go.',
  WIP:
    'MOBAflow WIP limit: at most 3 open code slices (Class H/M) at once. Documentation slices (Class L) do not count toward the code WIP limit.',
  'Board Drift':
    'Issue is still listed in an active Kanban column (In progress / In review) even though it is already closed on GitHub or has no active PR, writer, or recent activity.',
  'Round Table':
    'Visual team map around a ticket or PR: framed cards outside the ring show each agent’s role, tool, and AI model; numbered clockwise arrows show workflow handoffs; dashed lines show steering oversight.',
  'Color Semantics':
    'Two-axis color rule so identity and status never collide. Axis 1 (Who): P2 ChatGPT/Codex = Teal, P1 Claude/Opus = Orange, P3 Antigravity/Gemini = Light Blue, P4 Fast Delivery = Magenta-Orchid, Triad = Purple. Axis 2 (Health): Green = ready/pass, Amber = active/pending/attention, Red = blocked/conflict/Class H, Neutral = idle/context. Fluent Blue is for clickable controls only.',
  'Branch Hygiene':
    'Scans your local PC (Local Stale) and GitHub (Remote Stale) for leftover Git feature branches whose work is already merged or deleted, keeping both repositories tidy.',
  'Merge Gates':
    'Step-by-step quality checkpoints (Writer -> Companion -> CI & Sonar -> P1 Final -> Cross-Family -> Auto-Merge) that every code change must pass before entering main.'
};

interface GlossaryTooltipProps {
  term: string;
  children?: ReactNode;
  showIcon?: boolean;
  iconOnly?: boolean;
}

export function GlossaryTooltip({
  term,
  children,
  showIcon = false,
  iconOnly = false
}: GlossaryTooltipProps) {
  const description = GLOSSARY_ENTRIES[term];

  if (!description) {
    return iconOnly ? null : <>{children ?? term}</>;
  }

  return (
    <Tooltip content={description} relationship="description" withArrow positioning="above">
      <span
        tabIndex={0}
        aria-label={`${term}: ${description}`}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 4,
          cursor: 'help',
          color: tokens.colorNeutralForeground2,
          outlineOffset: 2,
          borderRadius: tokens.borderRadiusSmall
        }}
      >
        {!iconOnly && (children ?? <span>{term}</span>)}
        {(showIcon || iconOnly) && (
          <QuestionCircle16Regular
            style={{ width: 14, height: 14, flexShrink: 0 }}
            aria-hidden="true"
          />
        )}
      </span>
    </Tooltip>
  );
}
