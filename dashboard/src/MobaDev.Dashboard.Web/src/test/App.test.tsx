import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import type { DashboardSnapshot } from '../types/dashboard';

function createSnapshotFixture(overrides?: Partial<DashboardSnapshot>): DashboardSnapshot {
  const nowIso = new Date().toISOString();
  return {
    generatedAtUtc: nowIso,
    readOnly: true,
    bindAddress: 'http://127.0.0.1:5179',
    overallStatus: 'warnings',
    warningCount: 3,
    monitors: {
      generatedAtUtc: nowIso,
      source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
      monitors: [
        {
          id: 'claude-monitor',
          displayName: 'Claude Monitor',
          role: 'Chief Operating Officer (COO)',
          profile: 'P1',
          tool: 'Claude Code',
          state: 'rate_limited',
          model: 'claude-opus-5-5',
          lastActivityUtc: nowIso,
          rateLimitResetLabel: '12am (Europe/Berlin)',
          activeSessionCount: 1,
          automationStatus: null,
          source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false }
        },
        {
          id: 'codex-monitor',
          displayName: 'Codex Monitor',
          role: 'Technical Director & CPM',
          profile: 'P2',
          tool: 'ChatGPT Codex',
          state: 'standby',
          model: 'gpt-6.1-sol',
          lastActivityUtc: nowIso,
          rateLimitResetLabel: null,
          activeSessionCount: 2,
          automationStatus: 'ACTIVE (FREQ=HOURLY;INTERVAL=1)',
          source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false }
        },
        {
          id: 'antigravity-monitor',
          displayName: 'Antigravity Monitor',
          role: 'Head of AI Strategy',
          profile: 'P3',
          tool: 'Antigravity',
          state: 'busy',
          model: 'Orchestrator (Argon / Neon)',
          lastActivityUtc: nowIso,
          rateLimitResetLabel: null,
          activeSessionCount: 1,
          automationStatus: null,
          source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false }
        }
      ]
    },
    governance: {
      generatedAtUtc: nowIso,
      profiles: [
        {
          profile: 'P1',
          name: 'Depth, Architecture & Safety',
          models: 'Claude Opus 5.5',
          strengths: 'Deep architecture and safety gate'
        }
      ],
      roles: [
        {
          role: 'Chief Operating Officer (COO)',
          profiles: 'P1 (Claude)',
          responsibility: 'Operational steering'
        }
      ],
      riskClasses: [
        {
          code: 'L',
          name: 'Low',
          areas: 'docs/, specs/',
          requiredDiversity: 'Final P1 + cross-family'
        }
      ],
      agents: [
        {
          tool: 'Antigravity',
          kind: 'agent',
          name: 'head-of-ai-strategy',
          fileName: 'head-of-ai-strategy.md',
          description: 'Strategic lead',
          profileHint: 'P3',
          syncStatus: 'drift',
          repoHash: '5df3fc1eba23',
          localHash: '4c75aab1d12b',
          driftSummary: 'Inhalt weicht ab (+2/-1 Zeilen)'
        },
        {
          tool: 'Antigravity',
          kind: 'rule',
          name: 'mobaflow-agent-network',
          fileName: 'mobaflow-agent-network.md',
          description: 'Global agent rules',
          profileHint: 'P3',
          syncStatus: 'synced',
          repoHash: '95258e9f906b',
          localHash: '95258e9f906b',
          driftSummary: null
        }
      ],
      skills: [
        {
          name: 'mobaflow-delivery-handoff',
          description: 'Structured delivery handoff',
          sourceRepo: 'MOBAdev',
          relativePath: 'skills/mobaflow-delivery-handoff/SKILL.md'
        }
      ],
      syncedCount: 1,
      driftCount: 1,
      missingCount: 0,
      source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false }
    },
    pipeline: {
      generatedAtUtc: nowIso,
      repository: 'ahuelsmann/MOBAflow',
      projectBoardAvailable: false,
      projectBoardNotice: 'Projekt-Daten ausgeblendet: Scope read:project fehlt oder Kanban-CLI nicht erreichbar.',
      source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
      pullRequests: [
        {
          number: 492,
          title: 'docs(agents): track slices on MOBAflow Kanban',
          url: 'https://github.com/ahuelsmann/MOBAflow/pull/492',
          branch: 'codex/strategy-mobaflow-kanban-board',
          state: 'OPEN',
          stateLabel: 'Draft',
          isDraft: true,
          autoMergeEnabled: false,
          mergeState: 'BEHIND',
          ciStatus: 'pass',
          sonarStatus: 'pass',
          riskClass: 'L',
          lockedAreas: ['AGENTS.md'],
          updatedAtUtc: nowIso,
          mergedAtUtc: null,
          projectStatus: null,
          participants: [
            {
              agent: 'Codex Writer',
              tool: 'ChatGPT Codex',
              role: 'Writer',
              profile: 'P2',
              model: 'GPT-6 Astra',
              status: 'Umgesetzt',
              evidenceSource: 'Branch'
            }
          ]
        }
      ],
      recentlyMerged: [
        {
          number: 495,
          title: 'build(deps): apply minor updates',
          url: 'https://github.com/ahuelsmann/MOBAflow/pull/495',
          branch: 'claude/deps-494',
          state: 'MERGED',
          stateLabel: 'Gemergt',
          isDraft: false,
          autoMergeEnabled: false,
          mergeState: 'MERGED',
          ciStatus: 'pass',
          sonarStatus: 'pass',
          riskClass: 'L',
          lockedAreas: [],
          updatedAtUtc: nowIso,
          mergedAtUtc: nowIso,
          projectStatus: null,
          participants: [
            {
              agent: 'Fresh Claude Reviewer',
              tool: 'Claude Code',
              role: 'Final Reviewer',
              profile: 'P1',
              model: 'Claude Opus 5.5',
              status: 'PASS',
              evidenceSource: 'P1-Final-Review'
            },
            {
              agent: 'Antigravity Verifier',
              tool: 'Antigravity',
              role: 'Cross-Family Verifier',
              profile: 'P3',
              model: 'Gemini 4 Argon',
              status: 'PASS',
              evidenceSource: 'Cross-Family-Check'
            }
          ]
        }
      ]
    },
    hygiene: {
      generatedAtUtc: nowIso,
      source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
      staleWorktreeCount: 1,
      sessions: [
        {
          tool: 'Claude Code',
          monitorName: 'Claude Monitor',
          targetCount: 1,
          actualCount: 1,
          status: 'ok',
          notice: null
        },
        {
          tool: 'ChatGPT Codex',
          monitorName: 'Codex Monitor',
          targetCount: 1,
          actualCount: 2,
          status: 'warning',
          notice: '2 aktive Sessions gefunden – überschüssige Sessions archivieren.'
        }
      ],
      worktrees: [
        {
          folderName: 'artifacts/worktrees/old-merged',
          branch: 'codex/old-merged',
          headShort: 'a12e6063',
          isMain: false,
          status: 'stale',
          linkedPrNumber: null
        },
        {
          folderName: 'MOBAflow (main)',
          branch: 'main',
          headShort: '72a78bd0',
          isMain: true,
          status: 'main',
          linkedPrNumber: null
        }
      ]
    },
    ...overrides
  };
}

describe('MOBAdev Agent Network Dashboard', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  it('renders live monitor states, PR pipeline with participants, missing scope notice, and recently merged toggle', async () => {
    const user = userEvent.setup();
    const fixture = createSnapshotFixture();
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => fixture
      })
    );

    render(<App />);

    expect(await screen.findByText('Nur Lesen · 127.0.0.1')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Claude Monitor' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Codex Monitor' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Antigravity Monitor' })).toBeInTheDocument();
    expect(screen.getByText('Reset: 12am (Europe/Berlin)')).toBeInTheDocument();
    expect(screen.getByText('Standby')).toBeInTheDocument();
    expect(screen.getByText('Aktiv / Busy')).toBeInTheDocument();

    // Pipeline keeps rendering PR #492 despite read:project notice and displays participant chips
    expect(screen.getByText('Repository: ahuelsmann/MOBAflow')).toBeInTheDocument();
    expect(screen.getByText('#492')).toBeInTheDocument();
    expect(screen.getByText(/ChatGPT Codex · GPT-6 Astra/)).toBeInTheDocument();
    expect(screen.getByText(/Scope read:project fehlt/i)).toBeInTheDocument();

    // Hygiene & stale worktree warnings
    expect(screen.getByText('2 / 1 Warnung')).toBeInTheDocument();
    expect(screen.getByText('Veraltet / Gemergt')).toBeInTheDocument();

    // Switch to Recently Merged tasks and verify multi-model participation
    await user.click(screen.getByRole('button', { name: /Zuletzt abgeschlossen \(1\)/i }));
    expect(screen.getByText('#495')).toBeInTheDocument();
    expect(screen.getByText('Final Reviewer')).toBeInTheDocument();
    expect(screen.getByText(/Claude Code · Claude Opus 5.5/)).toBeInTheDocument();
    expect(screen.getByText(/Antigravity · Gemini 4 Argon/)).toBeInTheDocument();
  });

  it('navigates from drift summary to Matrix & Sync pre-filtered to deviations and switches themes', async () => {
    const user = userEvent.setup();
    const fixture = createSnapshotFixture();
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => fixture
      })
    );

    render(<App />);
    const driftBtn = await screen.findByRole('button', { name: /Abweichungen prüfen/i });
    await user.click(driftBtn);

    // Pre-filtered to drift only: drifted item shown, synced item hidden
    expect(screen.getByText('head-of-ai-strategy.md')).toBeInTheDocument();
    expect(screen.queryByText('mobaflow-agent-network.md')).not.toBeInTheDocument();

    // Toggle off drift-only filter -> synced item appears
    await user.click(screen.getByRole('button', { name: /Nur Abweichungen/i }));
    expect(screen.getByText('mobaflow-agent-network.md')).toBeInTheDocument();

    // Theme switching
    await user.click(screen.getByRole('button', { name: 'Hell' }));
    expect(document.documentElement.dataset.theme).toBe('light');

    await user.click(screen.getByRole('button', { name: 'Dunkel' }));
    expect(document.documentElement.dataset.theme).toBe('dark');
  });

  it('degrades gracefully when backend is unreachable', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockRejectedValue(new Error('Connection refused'))
    );

    render(<App />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      /Lokales Dashboard-Backend nicht erreichbar/i
    );
  });
});
