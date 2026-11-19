import type { ReactNode } from 'react';
import { Tooltip, tokens } from '@fluentui/react-components';
import { QuestionCircle16Regular } from '@fluentui/react-icons';

const GLOSSARY_ENTRIES: Record<string, string> = {
  P1: 'Profile P1 (Depth, Architecture & Safety): Claude Opus 5.5 for deep architecture, Z21 protocol safety, and mandatory independent final review.',
  P2: 'Profile P2 (Repo, Build & Fact Precision): GPT-6 Astra for solution build graph (Moba.slnx), CI/Sonar checks, and technical cross-verification.',
  P3: 'Profile P3 (System Context, Product & UI/UX): Gemini 4 Argon for large multi-file context, WinUI/MAUI UI design, and clear documentation.',
  P4: 'Profile P4 (Cadence & Fast Delivery): Fast models (Claude Fable / Gemini Hydrogen) for routine slices, docs, and triage.',
  H: 'Risk Class H (High): Safety-critical areas (Backend, Z21, ESP32). Requires design Go and independent review across two model families (P1 + P2).',
  M: 'Risk Class M (Medium): Product features, UI, audio, or MOBAnews. Requires companion review plus P3 UI/UX check for user-facing slices.',
  L: 'Risk Class L (Low): Documentation, wiki, or small non-behavioral fixes. Requires final P1 review plus cross-family check.',
  Drift:
    'Difference between the local configuration file on this machine (Actual) and the versioned baseline in MOBAdev (Target).',
  Worktree:
    'Separate Git working directory for an isolated branch so parallel tasks stay cleanly separated.',
  CLEAN: 'Branch can be merged cleanly without conflicts or missing commits from main.',
  BEHIND:
    'Branch is behind current main. Merge main into the branch before merging (never rebase shared branches).',
  UNSTABLE:
    'One or more GitHub status checks are still running or failing (GitHub mergeStateStatus: UNSTABLE).',
  'Auto-Merge':
    'GitHub merges the PR automatically once all required checks and reviews are green.',
  'Read-Only':
    'This cockpit only reads and displays live state. It never writes, edits, merges, or starts anything.',
  Triad:
    'Decision & Steering Triad: Cross-model council (P1 COO, P2 CPM/TD, P3 AI Strategy & UI/UX) that cuts tasks into slices, assigns risk classes (H/M/L), selects participating agents and AI models, and grants Design Go.',
  WIP:
    'MOBAflow WIP limit: at most 3 open code slices (Class H/M) at once. Documentation slices (Class L) do not count toward the code WIP limit.'
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
