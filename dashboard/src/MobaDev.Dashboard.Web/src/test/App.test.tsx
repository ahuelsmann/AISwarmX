import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { resolveModelVisual, resolveProfileTone, resolveRoleVisual } from '../components/RoleAndModelIcons';
import type {
  ConfigDocContentResponse,
  ConfigDocsCatalogResponse,
  DashboardSnapshot,
  RuleDiffResponse
} from '../types/dashboard';

function createSnapshotFixture(overrides?: Partial<DashboardSnapshot>): DashboardSnapshot {
  const nowIso = new Date().toISOString();
  return {
    generatedAtUtc: nowIso,
    readOnly: true,
    bindAddress: 'http://127.0.0.1:5179',
    overallStatus: 'warnings',
    warningCount: 7,
    changelogVersion: '3.5.5',
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
          model: 'gpt-6.2-sol',
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
          model: 'Orchestrator (Neon)',
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
          role: 'Decision & Steering Triad',
          profiles: 'P1 (COO) + P2 (CPM/TD) + P3 (Product/UX)',
          responsibility:
            'Cross-model steering for slice cutting, risk classification, design Go and escalations'
        },
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
          driftSummary: 'Content differs (+1/-1 lines)'
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
      projectBoardNotice:
        'Project board data hidden: missing read:project scope or Kanban CLI unreachable.',
      projectBoardUrl: 'https://github.com/users/ahuelsmann/projects/1',
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
          mergeState: 'UNSTABLE',
          ciStatus: 'pending',
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
              status: 'Implemented',
              evidenceSource: 'Slice Header'
            }
          ],
          linkedIssues: [
            {
              number: 366,
              title: 'RF-09: Coverage and mutation ratchets',
              url: 'https://github.com/ahuelsmann/MOBAflow/issues/366',
              state: 'OPEN'
            }
          ],
          mergeBlockReasons: []
        }
      ],
      recentlyMerged: [
        {
          number: 495,
          title: 'build(deps): apply minor updates',
          url: 'https://github.com/ahuelsmann/MOBAflow/pull/495',
          branch: 'claude/deps-494',
          state: 'MERGED',
          stateLabel: 'Merged',
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
              evidenceSource: 'P1 Final Review'
            },
            {
              agent: 'Antigravity Verifier',
              tool: 'Antigravity',
              role: 'Cross-Family Verifier',
              profile: 'P3',
              model: 'Gemini 4 Argon',
              status: 'PASS',
              evidenceSource: 'Cross-Family Check'
            }
          ],
          linkedIssues: [],
          mergeBlockReasons: []
        }
      ],
      activeIssues: [
        {
          number: 501,
          itemKind: 'PullRequest',
          title: 'ci(quality): enforce C# whitespace gate (RF-10 slice 3)',
          url: 'https://github.com/ahuelsmann/MOBAflow/pull/501',
          gitHubState: 'OPEN',
          kanbanStatus: 'In review',
          priority: null,
          area: null,
          issueType: null,
          riskClass: 'L',
          writer: 'COO (Claude)',
          lockedAreas: ['quality.yml'],
          updatedAtUtc: nowIso,
          linkedPullRequests: [],
          linkedIssues: [
            {
              number: 451,
              title: 'RF-10: Formatting baseline and gate',
              url: 'https://github.com/ahuelsmann/MOBAflow/issues/451',
              state: 'OPEN'
            }
          ],
          participants: [],
          boardDriftReason: null
        },
        {
          number: 451,
          itemKind: 'Issue',
          title: 'RF-10: Formatting baseline and gate',
          url: 'https://github.com/ahuelsmann/MOBAflow/issues/451',
          gitHubState: 'OPEN',
          kanbanStatus: 'In progress',
          priority: 'P1',
          area: 'Infrastructure',
          issueType: 'Refactoring',
          riskClass: 'L',
          writer: 'COO (Claude)',
          lockedAreas: ['quality.yml'],
          updatedAtUtc: nowIso,
          linkedPullRequests: [
            {
              number: 501,
              title: 'ci(quality): enforce C# whitespace gate (RF-10 slice 3)',
              url: 'https://github.com/ahuelsmann/MOBAflow/pull/501',
              stateLabel: 'Draft',
              isMerged: false
            }
          ],
          linkedIssues: [],
          participants: [],
          boardDriftReason: null
        },
        {
          number: 47,
          itemKind: 'Issue',
          title: 'Quality and refactoring programme: RF-01 through RF-18',
          url: 'https://github.com/ahuelsmann/MOBAflow/issues/47',
          gitHubState: 'OPEN',
          kanbanStatus: 'In progress',
          priority: 'P0',
          area: 'Infrastructure',
          issueType: 'Refactoring',
          riskClass: null,
          writer: null,
          lockedAreas: [],
          updatedAtUtc: nowIso,
          linkedPullRequests: [],
          linkedIssues: [],
          participants: [],
          boardDriftReason: null
        },
        {
          number: 266,
          itemKind: 'Issue',
          title: 'ci: keep documentation consistent within each pull request',
          url: 'https://github.com/ahuelsmann/MOBAflow/issues/266',
          gitHubState: 'CLOSED',
          kanbanStatus: 'In progress',
          priority: null,
          area: null,
          issueType: null,
          riskClass: null,
          writer: null,
          lockedAreas: [],
          updatedAtUtc: nowIso,
          linkedPullRequests: [],
          linkedIssues: [],
          participants: [],
          boardDriftReason: 'Closed on GitHub — move to Done on Kanban'
        }
      ]
    },
    hygiene: {
      generatedAtUtc: nowIso,
      source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
      staleWorktreeCount: 4,
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
          notice: '2 active sessions found — archive extra sessions.'
        }
      ],
      worktrees: [
        {
          folderName: 'artifacts/worktrees/old-1',
          branch: 'codex/old-1',
          headShort: 'a12e6061',
          isMain: false,
          status: 'stale',
          linkedPrNumber: null
        },
        {
          folderName: 'artifacts/worktrees/old-2',
          branch: 'codex/old-2',
          headShort: 'a12e6062',
          isMain: false,
          status: 'stale',
          linkedPrNumber: null
        },
        {
          folderName: 'artifacts/worktrees/old-3',
          branch: 'codex/old-3',
          headShort: 'a12e6063',
          isMain: false,
          status: 'stale',
          linkedPrNumber: null
        },
        {
          folderName: 'artifacts/worktrees/old-4',
          branch: 'codex/old-4',
          headShort: 'a12e6064',
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

function createDiffFixture(): RuleDiffResponse {
  return {
    tool: 'Antigravity',
    kind: 'agent',
    fileName: 'head-of-ai-strategy.md',
    syncStatus: 'drift',
    targetHash: '5df3fc1eba23',
    actualHash: '4c75aab1d12b',
    addedLines: 1,
    removedLines: 1,
    truncated: false,
    lines: [
      { kind: 'hunk', targetLineNumber: null, actualLineNumber: null, text: '@@ Target L4 / Actual L4 @@' },
      { kind: 'removed', targetLineNumber: 4, actualLineNumber: null, text: 'Line Old' },
      { kind: 'added', targetLineNumber: null, actualLineNumber: 4, text: 'Line New' }
    ]
  };
}

function stubDashboardFetch(snapshot = createSnapshotFixture()) {
  const nowIso = new Date().toISOString();

  const mobaDevCatalog: ConfigDocsCatalogResponse = {
    generatedAtUtc: nowIso,
    scope: 'mobadev',
    repository: 'ahuelsmann/MOBAdev',
    source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
    files: [
      {
        scope: 'mobadev',
        relativePath: 'manifest.md',
        fileName: 'manifest.md',
        group: 'Core',
        repository: 'ahuelsmann/MOBAdev',
        sizeBytes: 120,
        lastModifiedUtc: nowIso
      },
      {
        scope: 'mobadev',
        relativePath: 'docs/delivery-workflow.md',
        fileName: 'delivery-workflow.md',
        group: 'Docs',
        repository: 'ahuelsmann/MOBAdev',
        sizeBytes: 240,
        lastModifiedUtc: nowIso
      }
    ]
  };

  const docContents: Record<string, ConfigDocContentResponse> = {
    'mobadev::manifest.md': {
      scope: 'mobadev',
      relativePath: 'manifest.md',
      fileName: 'manifest.md',
      group: 'Core',
      repository: 'ahuelsmann/MOBAdev',
      sizeBytes: 120,
      lineCount: 4,
      lastModifiedUtc: nowIso,
      truncated: false,
      content: '# MOBAdev Manifest\n\nCentral configuration baseline for `MOBAdev`.'
    },
    'mobadev::docs/delivery-workflow.md': {
      scope: 'mobadev',
      relativePath: 'docs/delivery-workflow.md',
      fileName: 'delivery-workflow.md',
      group: 'Docs',
      repository: 'ahuelsmann/MOBAdev',
      sizeBytes: 240,
      lineCount: 3,
      lastModifiedUtc: nowIso,
      truncated: false,
      content: '# Delivery Workflow\n\nSmall batches on main with merge, never rebase.'
    }
  };

  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.startsWith('/api/governance/diff')) {
        return { ok: true, json: async () => createDiffFixture() };
      }

      if (url.startsWith('/api/docs/content')) {
        const params = new URLSearchParams(url.split('?')[1] ?? '');
        const key = `${params.get('scope')}::${params.get('path')}`;
        const doc = docContents[key];
        if (!doc) {
          return { ok: false, status: 404, json: async () => ({}) };
        }
        return { ok: true, json: async () => doc };
      }

      if (url.startsWith('/api/docs')) {
        return {
          ok: true,
          json: async () => mobaDevCatalog
        };
      }

      return { ok: true, json: async () => snapshot };
    })
  );
}

describe('MOBAdev Agent Swarm Gen#3 Dashboard', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  it(
    'renders Agent Swarm header, compact monitor cards, 1-line Triad with Code WIP, PR gates & collapsible stale worktrees',
    async () => {
    const user = userEvent.setup();
    stubDashboardFetch();

    render(<App />);

    expect(
      await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' })
    ).toBeInTheDocument();
    expect(screen.getByText('v3.5.5')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Version v3.5.5 — open CHANGELOG.md' })
    ).toBeInTheDocument();
    expect(screen.getByText('Read-Only · 127.0.0.1')).toBeInTheDocument();

    const pipelineToggleLabels = within(
      screen.getByRole('group', { name: 'Filter pipeline view' })
    )
      .getAllByRole('button')
      .map((btn) => btn.textContent?.trim());
    expect(pipelineToggleLabels).toEqual([
      'Kanban Board (4)',
      'Open PRs (1)',
      'Recently Merged (1)'
    ]);

    // Row 1: Compact monitor cards ordered Codex -> Claude -> Antigravity with human-friendly RRULE label
    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    expect(
      within(monitorSection)
        .getAllByRole('heading', { level: 2 })
        .map((h) => h.textContent)
    ).toEqual(['Codex Monitor', 'Claude Monitor', 'Antigravity Monitor']);
    expect(screen.getByText(/Reset: 12am \(Europe\/Berlin\)/)).toBeInTheDocument();
    expect(screen.getByText('Active · Hourly')).toBeInTheDocument();
    expect(screen.getByText('Standby')).toBeInTheDocument();
    expect(screen.getByText('Active / Busy')).toBeInTheDocument();

    // Row 2: Compact Triad bar in traditional order (Codex -> Claude -> Antigravity) with Code WIP
    const triadRegion = screen.getByLabelText('Decision and Steering Triad selection panel');
    expect(screen.getByRole('heading', { name: 'Decision & Steering Triad' })).toBeInTheDocument();
    expect(screen.getByText('Quorum 2 / 3 · 1 Quota Pause')).toBeInTheDocument();
    expect(screen.getByText('Code WIP: 0 / 3 (+1 Docs)')).toBeInTheDocument();
    expect(screen.getByText('Escalation: Human')).toBeInTheDocument();
    const triadText = triadRegion.textContent ?? '';
    const idxCpm = triadText.indexOf('CPM & TD');
    const idxCoo = triadText.indexOf('COO');
    const idxStrategy = triadText.indexOf('AI Strategy');
    expect(idxCpm).toBeGreaterThan(-1);
    expect(idxCoo).toBeGreaterThan(idxCpm);
    expect(idxStrategy).toBeGreaterThan(idxCoo);
    expect(resolveModelVisual('Orchestrator (Neon)').tone).toBe('blue');
    expect(resolveModelVisual('Gemini 4 Argon').tone).toBe('blue');
    expect(resolveRoleVisual('Head of AI Strategy').tone).toBe('blue');
    expect(resolveModelVisual('gpt-6.2-sol').tone).toBe('teal');
    expect(resolveRoleVisual('Technical Director & CPM').tone).toBe('teal');
    expect(resolveProfileTone('P2')).toBe('teal');

    // Row 3: PR #492 with Checks Pending (UNSTABLE), Merge Gates, linked issue & single-line participant pill
    expect(screen.getByText('Repository: ahuelsmann/MOBAflow')).toBeInTheDocument();
    expect(screen.getByText('#492')).toBeInTheDocument();
    expect(screen.getByText('Checks Pending')).toBeInTheDocument();
    expect(screen.getByText('Gates 1/5:')).toBeInTheDocument();
    expect(screen.getByText('Issue #366')).toBeInTheDocument();
    expect(screen.getByText('GPT-6 Astra')).toBeInTheDocument();
    expect(screen.getByText(/missing read:project scope/i)).toBeInTheDocument();

    // Stale worktrees collapsed when > 3 stale
    expect(screen.getByText('codex/old-1')).toBeInTheDocument();
    expect(screen.queryByText('codex/old-4')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Show all 4 stale worktrees/i }));
    expect(screen.getByText('codex/old-4')).toBeInTheDocument();

    // Switch to Recently Merged tasks and verify multi-model participation
    await user.click(screen.getByRole('button', { name: /Recently Merged \(1\)/i }));
    expect(screen.getByText('#495')).toBeInTheDocument();
    expect(screen.getByText('Final Reviewer')).toBeInTheDocument();
    expect(screen.getByText('Claude Opus 5.5')).toBeInTheDocument();
    expect(screen.getByText('Gemini 4 Argon')).toBeInTheDocument();
    },
    10_000
  );

  it(
    'opens Warning Breakdown popover, jumps to Matrix & Rules Diff viewer, displays Git Strategy card and links to instructions',
    async () => {
    const user = userEvent.setup();
    stubDashboardFetch();

    render(<App />);
    const warnBtn = await screen.findByRole('button', { name: /7 Warnings — show breakdown/i });
    await user.click(warnBtn);

    expect(screen.getByText('Warning Breakdown')).toBeInTheDocument();
    expect(screen.getByText('1 Rule Drift / Missing')).toBeInTheDocument();
    expect(screen.getByText('4 Stale Worktrees')).toBeInTheDocument();
    expect(screen.getByText('1 Extra Session')).toBeInTheDocument();
    expect(screen.getByText('1 Quota Pause')).toBeInTheDocument();

    // Jump straight from popover to Matrix & Rules pre-filtered to Drift
    const popoverRegion = screen.getByRole('region', { name: 'Warning breakdown' });
    await user.click(within(popoverRegion).getByRole('button', { name: 'Review Drift' }));

    const matrixHeadings = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent);
    expect(matrixHeadings[0]).toBe('Git & Delivery Strategy');
    expect(matrixHeadings[1]).toMatch(/Rules State · Actual vs\. Target/i);

    expect(
      screen.getByRole('heading', { name: /Rules State · Actual vs\. Target/i })
    ).toBeInTheDocument();
    expect(screen.getByText('1 Entry')).toBeInTheDocument();
    expect(screen.getByText('Actual: 4c75aab1d12b')).toBeInTheDocument();
    expect(screen.getByText('Target: 5df3fc1eba23')).toBeInTheDocument();
    expect(screen.getByText('head-of-ai-strategy.md')).toBeInTheDocument();

    // Expand read-only diff preview
    await user.click(screen.getByRole('button', { name: 'Show Diff' }));
    expect(await screen.findByText('Target (MOBAdev) vs. Actual (Local)')).toBeInTheDocument();
    expect(screen.getByText('+1 added')).toBeInTheDocument();
    expect(screen.getByText('-1 removed')).toBeInTheDocument();
    expect(screen.getByText('Line Old')).toBeInTheDocument();
    expect(screen.getByText('Line New')).toBeInTheDocument();

    // Verify Git & Delivery Strategy reference card & jump to Config tab
    expect(screen.queryByRole('tab', { name: /MOBAflow Instructions/i })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Git & Delivery Strategy/i })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Delivery Workflow' }));

    expect(
      await screen.findByRole('heading', { name: 'Delivery Workflow' })
    ).toBeInTheDocument();
    expect(screen.getByText('Small batches on main with merge, never rebase.')).toBeInTheDocument();

    // Theme switching
    await user.click(screen.getByRole('button', { name: 'Light' }));
    expect(document.documentElement.dataset.theme).toBe('light');

    await user.click(screen.getByRole('button', { name: 'Dark' }));
    expect(document.documentElement.dataset.theme).toBe('dark');
    },
    15_000
  );

  it(
    'renders read-only Configuration Markdown tab without tab icon and supports Formatted and Raw views',
    async () => {
    const user = userEvent.setup();
    stubDashboardFetch();

    render(<App />);
    await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });

    const configTab = screen.getByRole('tab', { name: 'Configuration' });
    expect(configTab.querySelector('svg')).toBeNull();
    await user.click(configTab);
    expect(await screen.findByRole('heading', { name: 'MOBAdev Manifest' })).toBeInTheDocument();
    expect(screen.getByText(/Central configuration baseline for/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Raw' }));
    expect(screen.getByText(/# MOBAdev Manifest/)).toBeInTheDocument();
    },
    10_000
  );

  it(
    'displays explicit BLOCKED reason banner directly under blocked PRs',
    async () => {
      const blockedSnapshot = createSnapshotFixture();
      blockedSnapshot.pipeline.pullRequests = [
        {
          ...blockedSnapshot.pipeline.pullRequests[0],
          number: 501,
          title: 'ci(quality): enforce C# whitespace gate (RF-10 slice 3)',
          mergeState: 'BLOCKED',
          ciStatus: 'pending',
          sonarStatus: 'pass',
          projectStatus: 'In review',
          mergeBlockReasons: [
            'Draft PR (not yet marked Ready for review)',
            '2 checks running: Windows desktop build, Backend mutation testing'
          ]
        }
      ];
      stubDashboardFetch(blockedSnapshot);

      render(<App />);
      expect(await screen.findByText('BLOCKED')).toBeInTheDocument();
      expect(screen.getByText('Kanban: In review')).toBeInTheDocument();
      const blockedLabel = screen.getByText('Blocked reason:');
      expect(blockedLabel).toBeInTheDocument();
      expect(blockedLabel.parentElement).toHaveTextContent(
        /Draft PR \(not yet marked Ready for review\).*2 checks running/
      );
    },
    10_000
  );

  it(
    'displays 1:1 Kanban Board view with In review PRs, In progress Issues, No PR yet, and inline drift without hiding items',
    async () => {
      const user = userEvent.setup();
      stubDashboardFetch();

      render(<App />);
      await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });

      await user.click(screen.getByRole('button', { name: /Kanban Board \(4\)/i }));

      expect(await screen.findByText('In review: 1')).toBeInTheDocument();
      expect(screen.getByText('In progress: 3')).toBeInTheDocument();
      expect(screen.getByText('Ready: 0')).toBeInTheDocument();
      expect(screen.getByRole('link', { name: /Open GitHub Kanban/i })).toHaveAttribute(
        'href',
        'https://github.com/users/ahuelsmann/projects/1'
      );

      expect(
        screen.getByText('ci(quality): enforce C# whitespace gate (RF-10 slice 3)')
      ).toBeInTheDocument();
      expect(screen.getByText('RF-10: Formatting baseline and gate')).toBeInTheDocument();
      expect(screen.getByText('PR #501')).toBeInTheDocument();
      expect(
        screen.getByText('Quality and refactoring programme: RF-01 through RF-18')
      ).toBeInTheDocument();
      expect(screen.getByText('No PR yet')).toBeInTheDocument();

      // Drift item is shown inline immediately (zero hidden items)
      expect(
        screen.getByText('ci: keep documentation consistent within each pull request')
      ).toBeInTheDocument();
      expect(screen.getByText(/Closed on GitHub — move to Done on Kanban/)).toBeInTheDocument();
    },
    10_000
  );

  it('degrades gracefully when backend is unreachable', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockRejectedValue(new Error('Connection refused'))
    );

    render(<App />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      /Local dashboard backend unreachable/i
    );
  });
});
