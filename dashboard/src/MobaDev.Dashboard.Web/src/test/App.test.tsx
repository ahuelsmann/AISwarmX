import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import {
  clearMonitorGridCookie,
  formatQuotaPauseSuffix,
  MONITOR_GRID_COOKIE,
  readMonitorGridCookie
} from '../components/MonitorCards';
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
    warningCount: 10,
    changelogVersion: '3.8.3',
    monitors: {
      generatedAtUtc: nowIso,
      source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
      summary: {
        standingMonitorsTotal: 4,
        standingMonitorsOnline: 4,
        standingMonitorsBusy: 1,
        activeSubAgentsTotal: 2,
        totalActiveAgents: 3,
        subAgentsByModel: [
          { model: 'GPT-6.2-sol', profile: 'P2', count: 1 },
          { model: 'Claude Opus 5.5', profile: 'P1', count: 0 },
          { model: 'Gemini 4 Carbon', profile: 'P3', count: 0 },
          { model: 'Gemini 4 Hydrogen', profile: 'P4', count: 1 },
          { model: 'Claude Sonnet 5.5', profile: 'P4', count: 0 },
          { model: 'GPT-OSS 120B', profile: 'P2', count: 0 }
        ]
      },
      monitors: [
        {
          id: 'claude-monitor',
          displayName: 'MOBAi Claude Opus',
          role: 'Chief Operating Officer (COO)',
          profile: 'P1',
          tool: 'Claude Code',
          state: 'rate_limited',
          model: 'claude-opus-5-5',
          lastActivityUtc: nowIso,
          rateLimitResetLabel: '12am (Europe/Berlin)',
          activeSessionCount: 1,
          automationStatus: null,
          source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
          activeSubAgentCount: 0
        },
        {
          id: 'codex-monitor',
          displayName: 'MOBAi ChatGPT Codex',
          role: 'Technical Director & CPM',
          profile: 'P2',
          tool: 'ChatGPT Codex',
          state: 'standby',
          model: 'gpt-6.2-sol',
          lastActivityUtc: nowIso,
          rateLimitResetLabel: null,
          activeSessionCount: 1,
          automationStatus: 'ACTIVE (FREQ=HOURLY;INTERVAL=1)',
          source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
          activeSubAgentCount: 1
        },
        {
          id: 'antigravity-monitor',
          displayName: 'MOBAi Antigravity Gemini',
          role: 'Head of AI Strategy',
          profile: 'P3',
          tool: 'Antigravity',
          state: 'busy',
          model: 'Orchestrator · Gemini 4 Argon',
          lastActivityUtc: nowIso,
          rateLimitResetLabel: null,
          activeSessionCount: 1,
          automationStatus: null,
          source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
          activeSubAgentCount: 1
        },
        {
          id: 'antigravity-opus-head',
          displayName: 'MOBAi Antigravity Opus',
          role: 'Acting COO & Opus Orchestrator',
          profile: 'P1',
          tool: 'Antigravity',
          state: 'standby',
          model: 'Orchestrator · Claude Opus 5.5',
          lastActivityUtc: nowIso,
          rateLimitResetLabel: null,
          activeSessionCount: 1,
          automationStatus: null,
          source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
          activeSubAgentCount: 0
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
            },
            {
              agent: 'Fresh Claude Reviewer',
              tool: 'Claude Code',
              role: 'Final Reviewer',
              profile: 'P1',
              model: 'Claude Opus 5.5',
              status: 'PASS',
              evidenceSource: 'P1 Final Review'
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
      ],
      staleLocalBranchCount: 2,
      staleRemoteBranchCount: 1,
      branches: [
        {
          repository: 'MOBAflow',
          name: 'claude/old-merged-branch',
          scope: 'local',
          status: 'stale',
          upstreamGone: true,
          linkedPrNumber: null
        },
        {
          repository: 'MOBAdev',
          name: 'ui/old-dev',
          scope: 'local',
          status: 'stale',
          upstreamGone: true,
          linkedPrNumber: null
        },
        {
          repository: 'MOBAflow',
          name: 'codex/stale-remote',
          scope: 'remote',
          status: 'stale',
          upstreamGone: false,
          linkedPrNumber: null
        },
        {
          repository: 'MOBAflow',
          name: 'codex/strategy-mobaflow-kanban-board',
          scope: 'local',
          status: 'active',
          upstreamGone: false,
          linkedPrNumber: 492
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
    clearMonitorGridCookie();
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
    expect(screen.getByText('v3.8.3')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Version v3.8.3 — open CHANGELOG.md' })
    ).toBeInTheDocument();
    expect(screen.getByText('Read-Only · 127.0.0.1')).toBeInTheDocument();

    expect(
      screen.queryByRole('region', { name: 'Live Active Agents and Sub-Agents Summary' })
    ).not.toBeInTheDocument();

    const monitorSectionPreview = screen.getByRole('region', { name: 'Live Monitor Status' });
    const triadBar = within(monitorSectionPreview).getByLabelText(
      'Decision and Steering Triad selection panel'
    );
    expect(within(triadBar).getByText('3 Busy / Recent')).toBeInTheDocument();

    const codexSubs = within(monitorSectionPreview).getByLabelText('Sub-agents for MOBAi ChatGPT Codex');
    expect(within(codexSubs).getByText('GPT-6.2 Sol:')).toBeInTheDocument();
    expect(within(codexSubs).getByText('GPT-6.2 Astra:')).toBeInTheDocument();

    const claudeSubs = within(monitorSectionPreview).getByLabelText('Sub-agents for MOBAi Claude Opus');
    expect(within(claudeSubs).getByText('Opus 5.5:')).toBeInTheDocument();
    expect(within(claudeSubs).getByText('Sonnet 5.5:')).toBeInTheDocument();
    expect(within(claudeSubs).getByText('Haiku 4.5:')).toBeInTheDocument();

    const geminiSubs = within(monitorSectionPreview).getByLabelText('Sub-agents for MOBAi Antigravity Gemini');
    expect(within(geminiSubs).getByText('Carbon:')).toBeInTheDocument();
    expect(within(geminiSubs).getByText('Hydrogen:')).toBeInTheDocument();
    expect(within(geminiSubs).getByText('GPT-OSS 120B:')).toBeInTheDocument();

    const opusSubs = within(monitorSectionPreview).getByLabelText('Sub-agents for MOBAi Antigravity Opus');
    expect(within(opusSubs).getByText('Opus 5.5:')).toBeInTheDocument();
    expect(within(opusSubs).getByText('Sonnet 5.5:')).toBeInTheDocument();
    expect(within(opusSubs).queryByText('Haiku 4.5:')).not.toBeInTheDocument();

    // Interactive Color & Symbol Legend popover mounts cleanly on demand without polluting DOM
    expect(screen.queryByRole('region', { name: 'Color and symbol legend' })).not.toBeInTheDocument();
    const legendBtn = screen.getByRole('button', { name: 'Open color and symbol legend' });
    await user.click(legendBtn);
    const legendRegion = await screen.findByRole('region', { name: 'Color and symbol legend' });
    expect(within(legendRegion).getByText('Axis 1 · AI Identity (Who)')).toBeInTheDocument();
    expect(within(legendRegion).getByText('Axis 2 · Status (State)')).toBeInTheDocument();
    expect(within(legendRegion).getByText('Round Table Key')).toBeInTheDocument();
    expect(within(legendRegion).getByText('Fast Delivery (Sonnet 5.5 & Hydrogen)')).toBeInTheDocument();
    await user.click(legendBtn);
    expect(screen.queryByRole('region', { name: 'Color and symbol legend' })).not.toBeInTheDocument();

    const pipelineToggleLabels = within(
      screen.getByRole('group', { name: 'Filter pipeline view' })
    )
      .getAllByRole('button')
      .map((btn) => btn.textContent?.trim());
    expect(pipelineToggleLabels).toEqual([
      'Open Issues (4)',
      'Open PRs (1)'
    ]);

    // Row 1: Compact monitor cards ordered MOBAi ChatGPT Codex -> MOBAi Claude Opus -> MOBAi Antigravity Gemini -> MOBAi Antigravity Opus
    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    expect(
      within(monitorSection)
        .getAllByRole('heading', { level: 2 })
        .map((h) => h.textContent)
    ).toEqual([
      'MOBAi ChatGPT Codex',
      'MOBAi Claude Opus',
      'MOBAi Antigravity Gemini',
      'MOBAi Antigravity Opus'
    ]);
    expect(screen.getByText(/Reset: 12am \(Europe\/Berlin\)/)).toBeInTheDocument();
    expect(screen.getByText('Active · Hourly')).toBeInTheDocument();
    expect(screen.getAllByText('Standby').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Active / Busy')).toBeInTheDocument();

    // Unified Decision & Steering Triad header bar integrated into the 2x3 Monitor Grid without duplicate pills
    const triadRegion = screen.getByLabelText('Decision and Steering Triad selection panel');
    expect(screen.getByRole('heading', { name: 'Decision & Steering Triad' })).toBeInTheDocument();
    expect(
      screen.getByText('Quorum 3 / 4 · 1 Quota Pause (Antigravity + Codex Failover)')
    ).toBeInTheDocument();
    expect(screen.getByText('Code WIP: 0 / 3 (+1 Docs)')).toBeInTheDocument();
    expect(screen.getByText('Escalation: Human')).toBeInTheDocument();
    expect(within(triadRegion).getByRole('button', { name: 'Selection Rules' })).toBeInTheDocument();
    expect(monitorSection.contains(triadRegion)).toBe(true);
    expect(screen.queryByText('CPM & TD')).not.toBeInTheDocument();
    expect(screen.queryByText('Opus Root')).not.toBeInTheDocument();
    expect(resolveModelVisual('Orchestrator (Neon)').tone).toBe('blue');
    expect(resolveModelVisual('Orchestrator · Gemini 4 Argon').tone).toBe('blue');
    expect(resolveModelVisual('Orchestrator · Claude Opus 5.5').tone).toBe('orange');
    expect(resolveModelVisual('Gemini 4 Argon').tone).toBe('blue');
    expect(resolveRoleVisual('Head of AI Strategy').tone).toBe('blue');
    expect(resolveModelVisual('gpt-6.2-sol').tone).toBe('teal');
    expect(resolveRoleVisual('Technical Director & CPM').tone).toBe('teal');
    expect(resolveProfileTone('P2')).toBe('teal');
    expect(resolveProfileTone('P4')).toBe('pink');
    expect(resolveModelVisual('Gemini 4 Hydrogen').tone).toBe('pink');
    expect(resolveRoleVisual('Final Reviewer').tone).toBe('neutral');

    // Row 3: Switch to Open PRs (#492 with Checks Pending, Merge Gates, linked issue, Arrows toggle & Round Table Visitenkarte)
    await user.click(screen.getByRole('button', { name: /Open PRs \(1\)/i }));
    expect(screen.getByText('Repository: ahuelsmann/MOBAflow')).toBeInTheDocument();
    expect(screen.getAllByText('#492').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Checks Pending')).toBeInTheDocument();
    expect(screen.getByText('Gates 2/5:')).toBeInTheDocument();
    expect(screen.getByText('Issue #366')).toBeInTheDocument();
    expect(screen.getByText('2 Agents')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Round table of 2 Agents for #492' })).toBeInTheDocument();
    const arrowsBtn = screen.getByRole('button', { name: 'Arrows: On' });
    await user.click(arrowsBtn);
    expect(screen.getByRole('button', { name: 'Arrows: Off' })).toBeInTheDocument();
    expect(screen.getByText('GPT-6 Astra')).toBeInTheDocument();
    expect(screen.getByText('Implemented')).toBeInTheDocument();
    const pipelineSection = screen.getByLabelText('Task and PR Pipeline MOBAflow');
    await user.click(within(pipelineSection).getByRole('button', { name: 'Pills' }));
    expect(
      screen.queryByRole('img', { name: 'Round table of 2 Agents for #492' })
    ).not.toBeInTheDocument();
    expect(screen.getByText('GPT-6 Astra')).toBeInTheDocument();
    expect(screen.getByText('Final Reviewer')).toBeInTheDocument();
    expect(screen.getByText('Claude Opus 5.5')).toBeInTheDocument();
    expect(screen.getByText(/missing read:project scope/i)).toBeInTheDocument();

    // Branch Hygiene card shows local/remote stale counts and active branches
    const branchCard = screen.getByLabelText('Branch Hygiene MOBAflow and MOBAdev');
    expect(within(branchCard).getByText('3 Stale')).toBeInTheDocument();
    expect(within(branchCard).getByText('claude/old-merged-branch')).toBeInTheDocument();
    expect(within(branchCard).getByText('ui/old-dev')).toBeInTheDocument();
    expect(within(branchCard).getByText('codex/stale-remote')).toBeInTheDocument();
    expect(within(branchCard).getByText('Active (#492)')).toBeInTheDocument();

    // Stale worktrees collapsed when > 3 stale
    expect(screen.getByText('codex/old-1')).toBeInTheDocument();
    expect(screen.queryByText('codex/old-4')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Show all 4 stale worktrees/i }));
    expect(screen.getByText('codex/old-4')).toBeInTheDocument();

    // Closed PRs toggle is intentionally removed in Live Operations
    expect(screen.queryByRole('button', { name: /Closed PRs/i })).not.toBeInTheDocument();
    },
    10_000
  );

  it(
    'opens Warning Breakdown popover, jumps to Matrix & Rules Diff viewer, displays Git Strategy card and links to instructions',
    async () => {
    const user = userEvent.setup();
    stubDashboardFetch();

    render(<App />);
    const warnBtn = await screen.findByRole('button', { name: /10 Warnings — show breakdown/i });
    await user.click(warnBtn);

    expect(screen.getByText('Warning Breakdown')).toBeInTheDocument();
    expect(screen.getByText('1 Rule Drift / Missing')).toBeInTheDocument();
    expect(screen.getByText('4 Stale Worktrees')).toBeInTheDocument();
    expect(screen.getByText('3 Stale Branches')).toBeInTheDocument();
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
      const user = userEvent.setup();
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
      await user.click(await screen.findByRole('button', { name: /Open PRs \(1\)/i }));
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
    'displays Open Issues by default in workflow order (Ready -> In progress -> In review) with Round Table everywhere',
    async () => {
      stubDashboardFetch();

      render(<App />);
      await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });

      const pipelineSection = screen.getByLabelText('Task and PR Pipeline MOBAflow');
      expect(within(pipelineSection).getByText('Ready: 0')).toBeInTheDocument();
      expect(within(pipelineSection).getByText('In progress: 3')).toBeInTheDocument();
      expect(within(pipelineSection).getByText('In review: 1')).toBeInTheDocument();

      const pipelineText = pipelineSection.textContent ?? '';
      expect(pipelineText.indexOf('Ready: 0')).toBeLessThan(pipelineText.indexOf('In progress: 3'));
      expect(pipelineText.indexOf('In progress: 3')).toBeLessThan(pipelineText.indexOf('In review: 1'));
      expect(pipelineText.indexOf('In progress (3)')).toBeLessThan(
        pipelineText.indexOf('In review (1)')
      );

      expect(screen.getByRole('link', { name: /Open GitHub Kanban/i })).toHaveAttribute(
        'href',
        'https://github.com/users/ahuelsmann/projects/1'
      );

      expect(
        screen.getByText('ci(quality): enforce C# whitespace gate (RF-10 slice 3)')
      ).toBeInTheDocument();
      expect(screen.getByText('RF-10: Formatting baseline and gate')).toBeInTheDocument();
      expect(screen.getAllByText('PR #501').length).toBeGreaterThanOrEqual(2);
      expect(
        screen.getByText('Quality and refactoring programme: RF-01 through RF-18')
      ).toBeInTheDocument();
      expect(screen.getByText('No PR yet')).toBeInTheDocument();

      // Round Table renders graphically by default even on 0-agent open issues; Writer pill shows when 0 participants
      expect(screen.getAllByText('Writer: COO (Claude)').length).toBeGreaterThanOrEqual(1);
      expect(screen.getByRole('img', { name: 'Round table of 0 Agents for Issue #47' })).toBeInTheDocument();
      expect(screen.getAllByText('Awaiting Agent Assignment').length).toBeGreaterThanOrEqual(1);

      // Drift item is shown inline immediately (zero hidden items)
      expect(
        screen.getByText('ci: keep documentation consistent within each pull request')
      ).toBeInTheDocument();
      expect(screen.getByText(/Closed on GitHub — move to Done on Kanban/)).toBeInTheDocument();
    },
    10_000
  );

  it('omits redundant Writer pill when issue has Round Table participants and normalizes Kanban Argon model to Carbon', async () => {
    const snap = createSnapshotFixture();
    snap.pipeline.activeIssues![2].writer = 'REDUNDANT_MULTI_AGENT_WRITER_TEXT';
    snap.pipeline.activeIssues![2].participants = [
      {
        agent: 'Antigravity Specialist',
        tool: 'Antigravity',
        role: 'UI/UX & Docs Swarm',
        profile: 'P3',
        model: 'Gemini 4 Argon',
        status: 'Assigned',
        evidenceSource: 'Kanban Quorum'
      }
    ];
    stubDashboardFetch(snap);

    render(<App />);
    await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });

    const pipelineSection = screen.getByLabelText('Task and PR Pipeline MOBAflow');
    expect(
      within(pipelineSection).queryByText(/REDUNDANT_MULTI_AGENT_WRITER_TEXT/)
    ).not.toBeInTheDocument();
    expect(within(pipelineSection).getAllByText('Writer: COO (Claude)').length).toBeGreaterThanOrEqual(1);
    expect(within(pipelineSection).getByText('Gemini 4 Carbon')).toBeInTheDocument();
  });

  it(
    'switches to Architecture tab and renders interactive Swarm Topology, Lifecycle and Boundary diagrams',
    async () => {
      const user = userEvent.setup();
      stubDashboardFetch();

      render(<App />);
      await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });

      await user.click(screen.getByRole('tab', { name: 'Architecture' }));

      expect(
        await screen.findByText('System Architecture, Agent Hierarchy & Live Workflow Diagrams')
      ).toBeInTheDocument();
      expect(
        screen.getByRole('group', {
          name: /Interactive Swarm Topology Diagram/i
        })
      ).toBeInTheDocument();
      expect(
        screen.getByRole('group', {
          name: /Task Lifecycle Diagram/i
        })
      ).toBeInTheDocument();
      expect(
        screen.getByRole('img', {
          name: /2-Axis Color Architecture/i
        })
      ).toBeInTheDocument();
      expect(
        screen.getByRole('img', {
          name: /Read-Only Dashboard Security and Privacy Boundary Diagram/i
        })
      ).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Select MOBAi ChatGPT Codex' }));
      expect(screen.getByText(/MOBAi ChatGPT Codex · P2/)).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Select MOBAi Antigravity Opus' }));
      expect(screen.getByText(/MOBAi Antigravity Opus · P1/)).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Select MOBAi Antigravity Gemini' }));
      expect(screen.getByText(/MOBAi Antigravity Gemini · P3/)).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Select lifecycle stage 2. Ready' }));
      expect(screen.getByText('Stage 2 · Ready (Human Maintainer Trigger)')).toBeInTheDocument();
    },
    10_000
  );

  it('keeps deactivated <synthetic> and zero-count card-body sub-agent chips in their original identity color with ghosted opacity', async () => {
    const snap = createSnapshotFixture();
    snap.monitors.monitors[0].model = '<synthetic>';
    snap.monitors.monitors[0].state = 'rate_limited';
    snap.monitors.monitors[0].activeSubAgentCount = 0;
    stubDashboardFetch(snap);

    render(<App />);
    await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });

    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    const claudeSubs = within(monitorSection).getByLabelText('Sub-agents for MOBAi Claude Opus');
    const zeroChip = within(claudeSubs).getByText('Opus 5.5:').parentElement as HTMLElement;
    expect(zeroChip.style.opacity).toBe('0.48');
    expect(zeroChip.style.color).not.toBe('');

    const syntheticText = within(monitorSection).getByText('<synthetic>');
    const syntheticBadge = syntheticText.parentElement as HTMLElement;
    expect(syntheticBadge.style.opacity).toBe('0.52');
    expect(syntheticBadge.style.borderColor).toBe('rgba(255, 154, 82, 0.34)');
  });

  it('renders Back to top link at the bottom of Live Operations and scrolls to top when clicked', async () => {
    const user = userEvent.setup();
    const scrollToSpy = vi.fn();
    vi.stubGlobal('scrollTo', scrollToSpy);
    stubDashboardFetch();

    render(<App />);
    await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });

    const backToTopLink = screen.getByRole('link', { name: /Back to top/i });
    expect(backToTopLink).toBeInTheDocument();
    await user.click(backToTopLink);
    expect(scrollToSpy).toHaveBeenCalledWith(expect.objectContaining({ top: 0 }));
  });

  it('renders per-card body sub-agent counts by model with ghosted zero-count identity chips when summary is omitted', async () => {
    const snap = createSnapshotFixture();
    snap.monitors.summary = null;
    snap.monitors.monitors[1].activeSubAgentsByModel = { 'gpt-6.2-sol': 1 };
    snap.monitors.monitors[2].activeSubAgentsByModel = {
      'gemini-4-hydrogen-xhigh': 2,
      'gpt-oss-120b': 1
    };
    snap.monitors.monitors[3].activeSubAgentsByModel = { 'claude-opus-5-5-medium': 1 };
    stubDashboardFetch(snap);

    render(<App />);
    await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });

    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    const codexSubs = within(monitorSection).getByLabelText('Sub-agents for MOBAi ChatGPT Codex');
    expect(within(codexSubs).getByText('GPT-6.2 Sol:').parentElement?.style.opacity).toBe('1');

    const geminiSubs = within(monitorSection).getByLabelText(
      'Sub-agents for MOBAi Antigravity Gemini'
    );
    const hydrogenChip = within(geminiSubs).getByText('Hydrogen:').parentElement as HTMLElement;
    expect(hydrogenChip.style.opacity).toBe('1');
    expect(hydrogenChip).toHaveTextContent('2');
    expect(within(geminiSubs).getByText('GPT-OSS 120B:').parentElement?.style.opacity).toBe('1');

    const carbonChip = within(geminiSubs).getByText('Carbon:').parentElement as HTMLElement;
    expect(carbonChip.style.opacity).toBe('0.48');
    expect(carbonChip.style.color).not.toBe('');

    const opusSubs = within(monitorSection).getByLabelText('Sub-agents for MOBAi Antigravity Opus');
    expect(within(opusSubs).getByText('Opus 5.5:').parentElement?.style.opacity).toBe('1');
    expect(within(opusSubs).getByText('Sonnet 5.5:').parentElement?.style.opacity).toBe('0.48');
  });

  it('renders a 2x3 monitor grid (6 slots), persists free arrangement in cookie, and restores it on reload', async () => {
    const user = userEvent.setup();
    stubDashboardFetch();

    const { unmount } = render(<App />);
    await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });

    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    for (let idx = 0; idx < 6; idx += 1) {
      expect(within(monitorSection).getByTestId(`monitor-slot-${idx}`)).toBeInTheDocument();
    }

    expect(
      within(within(monitorSection).getByTestId('monitor-slot-3')).getByRole('heading', {
        name: 'MOBAi Antigravity Opus'
      })
    ).toBeInTheDocument();
    expect(within(monitorSection).getByTestId('monitor-slot-4')).toBeEmptyDOMElement();
    expect(within(monitorSection).getByTestId('monitor-slot-5')).toBeEmptyDOMElement();

    // Move MOBAi Antigravity Opus (slot 3) to empty slot 5 via click-move button
    await user.click(
      within(monitorSection).getByRole('button', { name: 'Move MOBAi Antigravity Opus' })
    );
    await user.click(within(monitorSection).getByRole('button', { name: 'Place in grid slot 6' }));

    expect(
      within(within(monitorSection).getByTestId('monitor-slot-5')).getByRole('heading', {
        name: 'MOBAi Antigravity Opus'
      })
    ).toBeInTheDocument();
    expect(within(monitorSection).getByTestId('monitor-slot-3')).toBeEmptyDOMElement();
    expect(document.cookie).toContain(`${MONITOR_GRID_COOKIE}=`);
    expect(readMonitorGridCookie()).toEqual([
      'codex-monitor',
      'claude-monitor',
      'antigravity-monitor',
      null,
      null,
      'antigravity-opus-head'
    ]);

    // Drag & drop swap: drag MOBAi Claude Opus (slot 1) onto MOBAi Antigravity Gemini (slot 2)
    const slot1 = within(monitorSection).getByTestId('monitor-slot-1');
    const slot2 = within(monitorSection).getByTestId('monitor-slot-2');
    const claudeCard = slot1.querySelector('[draggable="true"]') as HTMLElement;
    expect(claudeCard).not.toBeNull();
    const dataTransfer = {
      data: {} as Record<string, string>,
      effectAllowed: 'move',
      dropEffect: 'move',
      setData(type: string, val: string) {
        this.data[type] = val;
      },
      getData(type: string) {
        return this.data[type] ?? '';
      }
    };
    fireEvent.dragStart(claudeCard, { dataTransfer });
    fireEvent.dragOver(slot2, { dataTransfer });
    fireEvent.drop(slot2, { dataTransfer });

    expect(readMonitorGridCookie()).toEqual([
      'codex-monitor',
      'antigravity-monitor',
      'claude-monitor',
      null,
      null,
      'antigravity-opus-head'
    ]);

    // Unmount and remount cleanly -> restores layout from cookie
    unmount();
    render(<App />);
    await screen.findByRole('heading', { name: 'MOBAdev Agent Swarm Gen#3' });
    const reloadedSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    expect(
      within(within(reloadedSection).getByTestId('monitor-slot-1')).getByRole('heading', {
        name: 'MOBAi Antigravity Gemini'
      })
    ).toBeInTheDocument();
    expect(
      within(within(reloadedSection).getByTestId('monitor-slot-2')).getByRole('heading', {
        name: 'MOBAi Claude Opus'
      })
    ).toBeInTheDocument();
    expect(
      within(within(reloadedSection).getByTestId('monitor-slot-5')).getByRole('heading', {
        name: 'MOBAi Antigravity Opus'
      })
    ).toBeInTheDocument();

    // Reset layout clears custom cookie back to default order
    await user.click(within(reloadedSection).getByRole('button', { name: 'Reset 2×3 layout' }));
    expect(readMonitorGridCookie()).toBeNull();
    expect(
      within(within(reloadedSection).getByTestId('monitor-slot-3')).getByRole('heading', {
        name: 'MOBAi Antigravity Opus'
      })
    ).toBeInTheDocument();
  });

  it('renders Dirty · Preserved badge and Copy Safe Prune Cmd action in Hygiene panel', async () => {
    const user = userEvent.setup();
    const customPayload = createSnapshotFixture();
    customPayload.hygiene.staleWorktreeCount = 1;
    customPayload.hygiene.worktrees = [
      {
        folderName: 'artifacts/worktrees/clean-merged',
        branch: 'codex/clean-merged',
        headShort: 'a12e6061',
        isMain: false,
        status: 'stale',
        linkedPrNumber: null,
        isDirty: false
      },
      {
        folderName: 'artifacts/worktrees/issue-398-fluent-design-accents',
        branch: 'codex/issue-398-fluent-design-accents',
        headShort: 'b45e9012',
        isMain: false,
        status: 'stale',
        linkedPrNumber: null,
        isDirty: true
      },
      {
        folderName: 'MOBAflow (main)',
        branch: 'main',
        headShort: '72a78bd0',
        isMain: true,
        status: 'main',
        linkedPrNumber: null,
        isDirty: false
      }
    ];
    stubDashboardFetch(customPayload);

    render(<App />);
    const worktreesCard = await screen.findByLabelText('Git Worktrees MOBAflow');
    expect(within(worktreesCard).getByText('1 Stale')).toBeInTheDocument();
    expect(within(worktreesCard).getByText('Stale / Merged')).toBeInTheDocument();
    expect(within(worktreesCard).getByText('Dirty · Preserved')).toBeInTheDocument();
    expect(within(worktreesCard).getByText('codex/issue-398-fluent-design-accents')).toBeInTheDocument();

    const copyBtn = within(worktreesCard).getByRole('button', { name: 'Copy Safe Prune Cmd' });
    await user.click(copyBtn);
    expect(within(worktreesCard).getByRole('button', { name: 'Copied!' })).toBeInTheDocument();
  });

  it('displays dynamic Quorum quota-pause context matching the paused vs ready Root Heads', async () => {
    const customPayload = createSnapshotFixture();
    customPayload.monitors.monitors = customPayload.monitors.monitors.map((m) => {
      if (m.id === 'claude-monitor') {
        return { ...m, state: 'standby', rateLimitResetLabel: null };
      }
      if (m.id === 'antigravity-opus-head') {
        return { ...m, state: 'rate_limited', rateLimitResetLabel: 'Nov 30, 08:14 · quota exhausted' };
      }
      return m;
    });
    stubDashboardFetch(customPayload);

    render(<App />);
    const triadRegion = await screen.findByLabelText('Decision and Steering Triad selection panel');
    expect(
      within(triadRegion).getByText('Quorum 3 / 4 · 1 Quota Pause (Claude + Codex + Gemini Active)')
    ).toBeInTheDocument();

    // Dual-Opus pause (both Claude Opus and Antigravity Opus rate-limited) -> Codex + Gemini Failover
    const dualOpusPaused = customPayload.monitors.monitors.map((m) =>
      m.id === 'claude-monitor' || m.id === 'antigravity-opus-head'
        ? { ...m, state: 'rate_limited' }
        : { ...m, state: 'standby' }
    );
    expect(formatQuotaPauseSuffix(dualOpusPaused)).toBe('(Codex + Gemini Failover)');
  });

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
