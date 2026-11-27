import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import * as swarmHubClient from '../api/swarmHubClient';
import { App } from '../App';
import { deriveActiveAgentsSummary } from '../components/ActiveAgentsStrip';
import { isParticipantActivelyWorking } from '../components/AgentRoundTable';
import { rankPullRequestsForMain } from '../components/MainLandingTickerCard';
import {
  clearMonitorGridCookie,
  formatQuotaPauseSuffix,
  LEGACY_MONITOR_GRID_COOKIE,
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
          displayName: 'AiSwarmX Claude Opus',
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
          displayName: 'AiSwarmX ChatGPT Codex',
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
          displayName: 'AiSwarmX Antigravity Gemini',
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
          displayName: 'AiSwarmX Antigravity Opus',
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
          sourceRepo: 'AISwarmX',
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
          repository: 'AISwarmX',
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

  const aiswarmxCatalog: ConfigDocsCatalogResponse = {
    generatedAtUtc: nowIso,
    scope: 'aiswarmx',
    repository: 'ahuelsmann/AISwarmX',
    source: { status: 'ok', updatedAtUtc: nowIso, fromCache: false },
    files: [
      {
        scope: 'aiswarmx',
        relativePath: 'manifest.md',
        fileName: 'manifest.md',
        group: 'Core',
        repository: 'ahuelsmann/AISwarmX',
        sizeBytes: 120,
        lastModifiedUtc: nowIso
      },
      {
        scope: 'aiswarmx',
        relativePath: 'docs/delivery-workflow.md',
        fileName: 'delivery-workflow.md',
        group: 'Docs',
        repository: 'ahuelsmann/AISwarmX',
        sizeBytes: 240,
        lastModifiedUtc: nowIso
      }
    ]
  };

  const docContents: Record<string, ConfigDocContentResponse> = {
    'aiswarmx::manifest.md': {
      scope: 'aiswarmx',
      relativePath: 'manifest.md',
      fileName: 'manifest.md',
      group: 'Core',
      repository: 'ahuelsmann/AISwarmX',
      sizeBytes: 120,
      lineCount: 4,
      lastModifiedUtc: nowIso,
      truncated: false,
      content: '# AISwarmX Manifest\n\nCentral configuration baseline for `AISwarmX`.'
    },
    'aiswarmx::docs/delivery-workflow.md': {
      scope: 'aiswarmx',
      relativePath: 'docs/delivery-workflow.md',
      fileName: 'delivery-workflow.md',
      group: 'Docs',
      repository: 'ahuelsmann/AISwarmX',
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
          json: async () => aiswarmxCatalog
        };
      }

      return { ok: true, json: async () => snapshot };
    })
  );
}

describe('AISwarmX — Autonomous Dev Engine Gen3.5 Dashboard', () => {
  afterEach(() => {
    vi.restoreAllMocks();
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
      await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' })
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

    const codexSubs = within(monitorSectionPreview).getByLabelText('Sub-agents for AiSwarmX ChatGPT Codex');
    expect(within(codexSubs).getByText('GPT-6.2 Sol:')).toBeInTheDocument();
    expect(within(codexSubs).getByText('GPT-6.2 Astra:')).toBeInTheDocument();

    const claudeSubs = within(monitorSectionPreview).getByLabelText('Sub-agents for AiSwarmX Claude Opus');
    expect(within(claudeSubs).getByText('Opus 5.5:')).toBeInTheDocument();
    expect(within(claudeSubs).getByText('Sonnet 5.5:')).toBeInTheDocument();
    expect(within(claudeSubs).getByText('Haiku 4.5:')).toBeInTheDocument();

    const geminiSubs = within(monitorSectionPreview).getByLabelText('Sub-agents for AiSwarmX Antigravity Gemini');
    expect(within(geminiSubs).getByText('Carbon:')).toBeInTheDocument();
    expect(within(geminiSubs).getByText('Hydrogen:')).toBeInTheDocument();
    expect(within(geminiSubs).getByText('GPT-OSS 120B:')).toBeInTheDocument();

    const opusSubs = within(monitorSectionPreview).getByLabelText('Sub-agents for AiSwarmX Antigravity Opus');
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

    // Row 1: Compact monitor cards ordered AiSwarmX ChatGPT Codex -> AiSwarmX Claude Opus -> AiSwarmX Antigravity Gemini -> AiSwarmX Antigravity Opus
    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    expect(
      within(monitorSection)
        .getAllByRole('heading', { level: 2 })
        .map((h) => h.textContent)
    ).toEqual([
      'AiSwarmX ChatGPT Codex',
      'AiSwarmX Claude Opus',
      'AiSwarmX Antigravity Gemini',
      'AiSwarmX Antigravity Opus'
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
    const branchCard = screen.getByLabelText('Branch Hygiene MOBAflow and AISwarmX');
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
    expect(await screen.findByText('Target (AISwarmX) vs. Actual (Local)')).toBeInTheDocument();
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
    await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });

    const configTab = screen.getByRole('tab', { name: 'Configuration' });
    expect(configTab.querySelector('svg')).toBeNull();
    await user.click(configTab);
    expect(await screen.findByRole('heading', { name: 'AISwarmX Manifest' })).toBeInTheDocument();
    expect(screen.getByText(/Central configuration baseline for/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Raw' }));
    expect(screen.getByText(/# AISwarmX Manifest/)).toBeInTheDocument();
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
      await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });

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
    await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });

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
      await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });

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

      await user.click(screen.getByRole('button', { name: 'Select AiSwarmX ChatGPT Codex' }));
      expect(screen.getByText(/AiSwarmX ChatGPT Codex · P2/)).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Select AiSwarmX Antigravity Opus' }));
      expect(screen.getByText(/AiSwarmX Antigravity Opus · P1/)).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Select AiSwarmX Antigravity Gemini' }));
      expect(screen.getByText(/AiSwarmX Antigravity Gemini · P3/)).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Select lifecycle stage 2. Ready' }));
      expect(screen.getByText('Stage 2 · Ready (Human Maintainer Trigger)')).toBeInTheDocument();
    },
    20_000
  );

  it('keeps deactivated <synthetic> and zero-count card-body sub-agent chips in their original identity color with ghosted opacity', async () => {
    const snap = createSnapshotFixture();
    snap.monitors.monitors[0].model = '<synthetic>';
    snap.monitors.monitors[0].state = 'rate_limited';
    snap.monitors.monitors[0].activeSubAgentCount = 0;
    stubDashboardFetch(snap);

    render(<App />);
    await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });

    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    const claudeSubs = within(monitorSection).getByLabelText('Sub-agents for AiSwarmX Claude Opus');
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
    await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });

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
    await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });

    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    const codexSubs = within(monitorSection).getByLabelText('Sub-agents for AiSwarmX ChatGPT Codex');
    expect(within(codexSubs).getByText('GPT-6.2 Sol:').parentElement?.style.opacity).toBe('1');

    const geminiSubs = within(monitorSection).getByLabelText(
      'Sub-agents for AiSwarmX Antigravity Gemini'
    );
    const hydrogenChip = within(geminiSubs).getByText('Hydrogen:').parentElement as HTMLElement;
    expect(hydrogenChip.style.opacity).toBe('1');
    expect(hydrogenChip).toHaveTextContent('2');
    expect(within(geminiSubs).getByText('GPT-OSS 120B:').parentElement?.style.opacity).toBe('1');

    const carbonChip = within(geminiSubs).getByText('Carbon:').parentElement as HTMLElement;
    expect(carbonChip.style.opacity).toBe('0.48');
    expect(carbonChip.style.color).not.toBe('');

    const opusSubs = within(monitorSection).getByLabelText('Sub-agents for AiSwarmX Antigravity Opus');
    expect(within(opusSubs).getByText('Opus 5.5:').parentElement?.style.opacity).toBe('1');
    expect(within(opusSubs).getByText('Sonnet 5.5:').parentElement?.style.opacity).toBe('0.48');
  });

  it('renders a 2x3 monitor grid (6 slots), persists free arrangement in cookie, and restores it on reload', async () => {
    const user = userEvent.setup();
    stubDashboardFetch();

    const { unmount } = render(<App />);
    await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });

    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    for (let idx = 0; idx < 6; idx += 1) {
      expect(within(monitorSection).getByTestId(`monitor-slot-${idx}`)).toBeInTheDocument();
    }

    expect(
      within(within(monitorSection).getByTestId('monitor-slot-3')).getByRole('heading', {
        name: 'AiSwarmX Antigravity Opus'
      })
    ).toBeInTheDocument();
    expect(within(monitorSection).getByTestId('monitor-slot-4')).toBeEmptyDOMElement();
    expect(within(monitorSection).getByTestId('monitor-slot-5')).toBeEmptyDOMElement();

    // Move AiSwarmX Antigravity Opus (slot 3) to empty slot 5 via click-move button
    await user.click(
      within(monitorSection).getByRole('button', { name: 'Move AiSwarmX Antigravity Opus' })
    );
    await user.click(within(monitorSection).getByRole('button', { name: 'Place in grid slot 6' }));

    expect(
      within(within(monitorSection).getByTestId('monitor-slot-5')).getByRole('heading', {
        name: 'AiSwarmX Antigravity Opus'
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

    // Drag & drop swap: drag AiSwarmX Claude Opus (slot 1) onto AiSwarmX Antigravity Gemini (slot 2)
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
    await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });
    const reloadedSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    expect(
      within(within(reloadedSection).getByTestId('monitor-slot-1')).getByRole('heading', {
        name: 'AiSwarmX Antigravity Gemini'
      })
    ).toBeInTheDocument();
    expect(
      within(within(reloadedSection).getByTestId('monitor-slot-2')).getByRole('heading', {
        name: 'AiSwarmX Claude Opus'
      })
    ).toBeInTheDocument();
    expect(
      within(within(reloadedSection).getByTestId('monitor-slot-5')).getByRole('heading', {
        name: 'AiSwarmX Antigravity Opus'
      })
    ).toBeInTheDocument();

    // Reset layout clears custom cookie back to default order
    await user.click(within(reloadedSection).getByRole('button', { name: 'Reset 2×3 layout' }));
    expect(readMonitorGridCookie()).toBeNull();
    expect(
      within(within(reloadedSection).getByTestId('monitor-slot-3')).getByRole('heading', {
        name: 'AiSwarmX Antigravity Opus'
      })
    ).toBeInTheDocument();
  }, 10_000);

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
        ? { ...m, state: 'rate_limited' as const }
        : { ...m, state: 'standby' as const }
    );
    expect(formatQuotaPauseSuffix(dualOpusPaused)).toBe('(Codex + Gemini Failover)');
  });

  it('renders Swarm Bus Cross-Tool MCP Feed on dedicated Communication tab with filters and active locks', async () => {
    const user = userEvent.setup();
    const customPayload = createSnapshotFixture({
      swarmBus: {
        generatedAtUtc: new Date().toISOString(),
        unreadCount: 1,
        activeLockCount: 1,
        source: { status: 'ok', updatedAtUtc: new Date().toISOString(), fromCache: false },
        activeLocks: [
          {
            id: 1,
            agent: 'codex',
            pathPattern: 'MOBAdemo/app/src/shell',
            prNumber: 541,
            claimedAtUtc: new Date().toISOString(),
            expiresAtUtc: new Date(Date.now() + 3600_000).toISOString()
          }
        ],
        recentMessages: [
          {
            id: 7,
            createdAtUtc: new Date().toISOString(),
            sender: 'antigravity-gemini',
            recipient: 'codex',
            topic: 'review-pass',
            body: 'P3 UI/UX PASS on PR #541',
            prNumber: 541,
            priority: 'high',
            status: 'unread'
          },
          {
            id: 6,
            createdAtUtc: new Date().toISOString(),
            sender: 'watchdog',
            recipient: 'claude-opus',
            topic: 'sonar-finding',
            body: 'Acked earlier Sonar pointer',
            prNumber: 537,
            priority: 'normal',
            status: 'acked'
          }
        ]
      }
    });
    stubDashboardFetch(customPayload);

    render(<App />);
    const commTab = await screen.findByRole('tab', { name: /Communication/i });
    expect(within(commTab).getAllByText('1').length).toBeGreaterThanOrEqual(1);
    expect(screen.queryByLabelText('Swarm Bus Cross-Tool MCP Feed')).not.toBeInTheDocument();

    await user.click(commTab);
    const busCard = await screen.findByLabelText('Swarm Bus Cross-Tool MCP Feed');
    expect(within(busCard).getByText('1 Unread')).toBeInTheDocument();
    expect(within(busCard).getByText('1 Locks')).toBeInTheDocument();
    expect(within(busCard).getByText('MOBAdemo/app/src/shell')).toBeInTheDocument();
    expect(within(busCard).getByText(/^\(exp in (?:1h|59m)\)$/)).toBeInTheDocument();
    expect(within(busCard).getByText('P3 UI/UX PASS on PR #541')).toBeInTheDocument();
    expect(within(busCard).getByText('Acked earlier Sonar pointer')).toBeInTheDocument();
    expect(within(busCard).getByText('confirmed')).toBeInTheDocument();
    expect(within(busCard).getByRole('button', { name: /Confirmed \(1\)/i })).toBeInTheDocument();

    await user.click(within(busCard).getByRole('button', { name: /Unread \(1\)/i }));
    expect(within(busCard).getByText('P3 UI/UX PASS on PR #541')).toBeInTheDocument();
    expect(within(busCard).queryByText('Acked earlier Sonar pointer')).not.toBeInTheDocument();
  });

  it('renders Main Landing Queue in exact arrival order with Just landed on main strip', async () => {
    const base = createSnapshotFixture();
    const templatePr = base.pipeline.pullRequests[0]!;
    const nowIso = new Date().toISOString();
    const customPayload = createSnapshotFixture({
      pipeline: {
        ...base.pipeline,
        pullRequests: [
          {
            ...templatePr,
            number: 544,
            title: 'ci: scope Stryker lanes (#539)',
            isDraft: true,
            autoMergeEnabled: false,
            mergeState: 'CLEAN',
            ciStatus: 'pending',
            sonarStatus: 'pending',
            linkedIssues: [{ number: 539, url: 'https://github.com/ahuelsmann/MOBAflow/issues/539' }],
            updatedAtUtc: nowIso
          },
          {
            ...templatePr,
            number: 540,
            title: 'docs: HelpTopics drift (#510 S1)',
            isDraft: false,
            autoMergeEnabled: true,
            mergeState: 'CLEAN',
            ciStatus: 'pass',
            sonarStatus: 'pass',
            linkedIssues: [{ number: 510, url: 'https://github.com/ahuelsmann/MOBAflow/issues/510' }],
            updatedAtUtc: nowIso
          },
          {
            ...templatePr,
            number: 541,
            title: 'feat: MOBAdemo shell (#430 slice 7)',
            isDraft: false,
            autoMergeEnabled: false,
            mergeState: 'CLEAN',
            ciStatus: 'pass',
            sonarStatus: 'pass',
            linkedIssues: [{ number: 430, url: 'https://github.com/ahuelsmann/MOBAflow/issues/430' }],
            updatedAtUtc: nowIso
          }
        ],
        recentlyMerged: [
          {
            ...templatePr,
            number: 537,
            title: 'feat(ci): MOBAdemo CI job (#430 slice 4)',
            state: 'MERGED',
            stateLabel: 'Merged',
            isDraft: false,
            mergedAtUtc: nowIso,
            updatedAtUtc: nowIso
          }
        ]
      }
    });

    const ranked = rankPullRequestsForMain(customPayload.pipeline.pullRequests);
    expect(ranked.map((r) => r.pr.number)).toEqual([540, 541, 544]);
    expect(ranked.map((r) => r.stageLabel)).toEqual([
      'Auto-Merge · Ready',
      'Ready · Checks Green',
      'CI / Review Running'
    ]);

    stubDashboardFetch(customPayload);
    render(<App />);

    const queueCard = await screen.findByLabelText('Main Landing Queue');
    expect(within(queueCard).getByText('3 Approaching main')).toBeInTheDocument();
    expect(within(queueCard).getByText('1 Landed')).toBeInTheDocument();

    const orderedList = within(queueCard).getByRole('list', { name: 'Features approaching main' });
    const rows = within(orderedList).getAllByRole('listitem');
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveTextContent('#1');
    expect(rows[0]).toHaveTextContent('PR #540');
    expect(rows[0]).toHaveTextContent('Auto-Merge · Ready');
    expect(rows[1]).toHaveTextContent('#2');
    expect(rows[1]).toHaveTextContent('PR #541');
    expect(rows[2]).toHaveTextContent('#3');
    expect(rows[2]).toHaveTextContent('PR #544');

    const landedStrip = within(queueCard).getByLabelText('Just landed on main');
    expect(within(landedStrip).getByText('PR #537')).toBeInTheDocument();
  });

  it('renders idle monitors with inactive duration hint and excludes them from busy counts', async () => {
    const user = userEvent.setup();
    const thirtyMinutesAgoIso = new Date(Date.now() - 30 * 60_000).toISOString();
    const customPayload = createSnapshotFixture();
    customPayload.monitors.summary = undefined;
    customPayload.monitors.monitors = customPayload.monitors.monitors.map((m) => {
      if (m.id === 'antigravity-monitor') {
        return {
          ...m,
          state: 'idle',
          lastActivityUtc: thirtyMinutesAgoIso,
          activeSubAgentCount: 0
        };
      }
      if (m.id === 'claude-monitor') {
        return {
          ...m,
          state: 'idle',
          lastActivityUtc: null,
          rateLimitResetLabel: null,
          activeSubAgentCount: 0
        };
      }
      if (m.id === 'codex-monitor') {
        return { ...m, activeSubAgentCount: 0 };
      }
      return m;
    });
    const derived = deriveActiveAgentsSummary(customPayload.monitors);
    expect(derived.standingMonitorsOnline).toBe(4);
    expect(derived.standingMonitorsBusy).toBe(0);
    expect(derived.totalActiveAgents).toBe(0);

    stubDashboardFetch(customPayload);
    render(<App />);

    const monitorSection = await screen.findByRole('region', { name: 'Live Monitor Status' });
    expect(within(monitorSection).getByText(/Idle · no activity for 30 min/)).toBeInTheDocument();
    expect(within(monitorSection).getByText('Idle · inactive')).toBeInTheDocument();
    expect(within(monitorSection).queryByText('Active / Busy')).not.toBeInTheDocument();
    expect(within(monitorSection).getByText('Quorum 4 / 4 Ready')).toBeInTheDocument();
    expect(within(monitorSection).getByText('0 Busy / Recent')).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: /Architecture/i }));
    expect(await screen.findAllByText('Idle')).not.toHaveLength(0);
  });

  it('falls back to legacy mobadev-theme localStorage and mobadev_monitor_grid_v1 cookie', async () => {
    window.localStorage.setItem('mobadev-theme', 'light');
    const legacyLayout = [
      'antigravity-opus-head',
      'codex-monitor',
      'claude-monitor',
      'antigravity-monitor',
      null,
      null
    ];
    document.cookie = `${LEGACY_MONITOR_GRID_COOKIE}=${encodeURIComponent(JSON.stringify(legacyLayout))}; path=/`;

    stubDashboardFetch();
    render(<App />);
    await screen.findByRole('heading', { name: 'AISwarmX — Autonomous Dev Engine Gen3.5' });

    expect(document.documentElement.dataset.theme).toBe('light');
    expect(window.localStorage.getItem('aiswarmx-theme')).toBe('light');

    const monitorSection = screen.getByRole('region', { name: 'Live Monitor Status' });
    expect(
      within(within(monitorSection).getByTestId('monitor-slot-0')).getByRole('heading', {
        name: 'AiSwarmX Antigravity Opus'
      })
    ).toBeInTheDocument();
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

  it('merges live SignalR SwarmBusUpdated pushes and updates Live Push badge', async () => {
    const user = userEvent.setup();
    let capturedHandlers: swarmHubClient.SwarmHubSubscriptionHandlers | null = null;
    const hubSpy = vi.spyOn(swarmHubClient, 'subscribeSwarmHub').mockImplementation((handlers) => {
      capturedHandlers = handlers;
      handlers.onStateChange?.('live');
      return () => {};
    });

    try {
      const initAt = new Date().toISOString();
      const fixture = createSnapshotFixture({
        swarmBus: {
          generatedAtUtc: initAt,
          unreadCount: 0,
          activeLockCount: 0,
          recentMessages: [],
          activeLocks: [],
          source: { status: 'ok', updatedAtUtc: initAt, fromCache: false }
        }
      });
      stubDashboardFetch(fixture);
      render(<App />);

      await user.click(await screen.findByRole('tab', { name: /Communication/i }));
      const busRegion = await screen.findByRole('region', { name: 'Swarm Bus Cross-Tool MCP Feed' });
      expect(within(busRegion).getByText('Live Push')).toBeInTheDocument();

      const pushedAt = new Date().toISOString();
      act(() => {
        capturedHandlers?.onSwarmBusUpdated({
          generatedAtUtc: pushedAt,
          unreadCount: 3,
          activeLockCount: 0,
          recentMessages: [
            {
              id: 999,
              createdAtUtc: pushedAt,
              sender: 'codex',
              recipient: 'antigravity-gemini',
              topic: 'live-signalr-test',
              body: 'Live push delivered via SignalR',
              issueNumber: 56,
              priority: 'high',
              status: 'unread'
            }
          ],
          activeLocks: [],
          source: { status: 'ok', updatedAtUtc: pushedAt, fromCache: false }
        });
      });

      expect(within(busRegion).getByText('3 Unread')).toBeInTheDocument();
      expect(within(busRegion).getByText(/live-signalr-test/)).toBeInTheDocument();
    } finally {
      hubSpy.mockRestore();
    }
  });

  it('retries initial SignalR start failures with backoff and cancels pending retry on dispose', async () => {
    vi.useFakeTimers();
    try {
      const states: swarmHubClient.LiveConnectionState[] = [];
      let startCalls = 0;
      let stopCalls = 0;
      const fakeConn: { -readonly [K in keyof swarmHubClient.SwarmHubConnectionLike]: swarmHubClient.SwarmHubConnectionLike[K] } = {
        state: 'Disconnected' as never,
        on: () => {},
        onreconnecting: () => {},
        onreconnected: () => {},
        onclose: () => {},
        start: async () => {
          startCalls += 1;
          if (startCalls === 1) {
            throw new Error('backend starting');
          }
          fakeConn.state = 'Connected' as never;
        },
        stop: async () => {
          stopCalls += 1;
        }
      };

      const dispose = swarmHubClient.subscribeSwarmHub(
        {
          onSwarmBusUpdated: () => {},
          onSnapshotUpdated: () => {},
          onStateChange: (s) => states.push(s)
        },
        '/hubs/swarm',
        {
          connectionFactory: () => fakeConn,
          initialRetryBaseMs: 50,
          maxRetryDelayMs: 200
        }
      );

      await act(async () => {
        await Promise.resolve();
      });
      expect(startCalls).toBe(1);
      expect(states).toContain('polling');

      await act(async () => {
        await vi.advanceTimersByTimeAsync(60);
      });
      expect(startCalls).toBe(2);
      expect(states[states.length - 1]).toBe('live');

      dispose();
      expect(stopCalls).toBe(1);

      // Verify pending retry timer is cancelled when disposed after an initial failure.
      let disposedStartCalls = 0;
      const failingConn: swarmHubClient.SwarmHubConnectionLike = {
        state: 0 as never,
        on: () => {},
        onreconnecting: () => {},
        onreconnected: () => {},
        onclose: () => {},
        start: async () => {
          disposedStartCalls += 1;
          throw new Error('still offline');
        },
        stop: async () => {}
      };

      const disposeEarly = swarmHubClient.subscribeSwarmHub(
        {
          onSwarmBusUpdated: () => {},
          onSnapshotUpdated: () => {}
        },
        '/hubs/swarm',
        {
          connectionFactory: () => failingConn,
          initialRetryBaseMs: 50,
          maxRetryDelayMs: 200
        }
      );

      await act(async () => {
        await Promise.resolve();
      });
      expect(disposedStartCalls).toBe(1);
      disposeEarly();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(500);
      });
      expect(disposedStartCalls).toBe(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('builds cookie-free SignalR client connections with withCredentials disabled', async () => {
    const signalr = await import('@microsoft/signalr');
    const withUrlSpy = vi.spyOn(signalr.HubConnectionBuilder.prototype, 'withUrl');
    try {
      const conn = swarmHubClient.buildSwarmHubConnection('http://127.0.0.1:5179/hubs/swarm');
      expect(conn).toBeDefined();
      expect(withUrlSpy).toHaveBeenCalledWith('http://127.0.0.1:5179/hubs/swarm', {
        withCredentials: false
      });
    } finally {
      withUrlSpy.mockRestore();
    }
  });

  it('detects actively working Round Table participants only when live monitor or sub-agent is busy', () => {
    const monitors = createSnapshotFixture().monitors.monitors;
    const codexWriter = {
      agent: 'Codex Writer',
      tool: 'ChatGPT Codex',
      role: 'Writer',
      profile: 'P2',
      model: 'GPT-6.2 Sol',
      status: 'Active',
      evidenceSource: 'GitHub Kanban Assignee'
    };

    // Standby monitor -> false even though participant status says Active
    expect(isParticipantActivelyWorking(codexWriter, monitors, 'In progress')).toBe(false);

    // Busy matching monitor -> true
    const busyCodexMonitors = monitors.map((m) =>
      m.id === 'codex-monitor' ? { ...m, state: 'busy' as const } : m
    );
    expect(isParticipantActivelyWorking(codexWriter, busyCodexMonitors, 'In progress')).toBe(true);

    // Merged / Closed / PASS / Assigned -> false even when monitor is busy
    expect(isParticipantActivelyWorking(codexWriter, busyCodexMonitors, 'Merged')).toBe(false);
    expect(
      isParticipantActivelyWorking({ ...codexWriter, status: 'PASS' }, busyCodexMonitors, 'In progress')
    ).toBe(false);
    expect(
      isParticipantActivelyWorking({ ...codexWriter, status: 'Assigned' }, busyCodexMonitors, 'In progress')
    ).toBe(false);

    // Disambiguates Antigravity P1 Opus (standby) vs Antigravity P3 Gemini (busy)
    const agyP1Reviewer = {
      agent: 'Antigravity Opus Reviewer',
      tool: 'Antigravity',
      role: 'Final Reviewer',
      profile: 'P1',
      model: 'Claude Opus 5.5',
      status: 'Active',
      evidenceSource: 'Label'
    };
    const agyP3UiUx = {
      agent: 'Head of UI/UX',
      tool: 'Antigravity',
      role: 'Head of UI/UX',
      profile: 'P3',
      model: 'Gemini 4 Carbon',
      status: 'Active',
      evidenceSource: 'Label'
    };
    expect(isParticipantActivelyWorking(agyP1Reviewer, monitors, 'In review')).toBe(false);
    expect(isParticipantActivelyWorking(agyP3UiUx, monitors, 'In review')).toBe(true);

    // Active sub-agents on matching model mark participant live-working even when root monitor is standby
    const subAgentMonitors = monitors.map((m) =>
      m.id === 'codex-monitor'
        ? { ...m, state: 'standby' as const, activeSubAgentsByModel: { 'gpt-6.2-sol': 1 } }
        : m
    );
    expect(isParticipantActivelyWorking(codexWriter, subAgentMonitors, 'In progress')).toBe(true);

    // Regression (Codex + Claude P1): missing Opus monitor + busy P3 Gemini monitor must NOT pulse Active P1 Opus seat
    const onlyBusyGemini = monitors.filter((m) => m.id === 'antigravity-monitor');
    expect(isParticipantActivelyWorking(agyP1Reviewer, onlyBusyGemini, 'In review')).toBe(false);

    // Single busy Claude P1 Opus monitor must NOT pulse an Active P4 Sonnet seat unless Sonnet sub-agent is active
    const onlyBusyClaudeOpus = monitors.map((m) =>
      m.id === 'claude-monitor' ? { ...m, state: 'busy' as const, activeSubAgentsByModel: null } : m
    );
    const claudeP4Sonnet = {
      agent: 'Claude Sonnet Helper',
      tool: 'Claude Code',
      role: 'Senior Developer',
      profile: 'P4',
      model: 'Claude Sonnet 5.5',
      status: 'Active',
      evidenceSource: 'Subagent'
    };
    expect(isParticipantActivelyWorking(claudeP4Sonnet, onlyBusyClaudeOpus, 'In progress')).toBe(false);
    const busyClaudeWithSonnetSub = onlyBusyClaudeOpus.map((m) =>
      m.id === 'claude-monitor' ? { ...m, activeSubAgentsByModel: { 'claude-sonnet-5-5': 1 } } : m
    );
    expect(isParticipantActivelyWorking(claudeP4Sonnet, busyClaudeWithSonnetSub, 'In progress')).toBe(true);

    // Terminal gitHubState flag suppresses pulse even with board drift (In progress)
    expect(isParticipantActivelyWorking(codexWriter, busyCodexMonitors, 'In progress', true)).toBe(false);
  });

  it('renders pulsing halo only on Round Table seats whose live monitor is actively working', async () => {
    const busySnapshot = createSnapshotFixture();
    busySnapshot.monitors.monitors = busySnapshot.monitors.monitors.map((m) =>
      m.id === 'codex-monitor' ? { ...m, state: 'busy' as const } : m
    );
    busySnapshot.pipeline.activeIssues = [
      {
        number: 47,
        itemKind: 'Issue',
        title: 'Build feature with active Codex Writer',
        url: 'https://github.com/ahuelsmann/MOBAflow/issues/47',
        gitHubState: 'OPEN',
        kanbanStatus: 'In progress',
        priority: 'P1',
        area: 'UI',
        issueType: 'Feature',
        riskClass: 'M',
        writer: 'Codex',
        lockedAreas: [],
        updatedAtUtc: busySnapshot.generatedAtUtc,
        linkedPullRequests: [],
        linkedIssues: [],
        participants: [
          {
            agent: 'Codex Writer',
            tool: 'ChatGPT Codex',
            role: 'Writer',
            profile: 'P2',
            model: 'GPT-6.2 Sol',
            status: 'Active',
            evidenceSource: 'GitHub Kanban Assignee'
          },
          {
            agent: 'Final Reviewer',
            tool: 'Antigravity',
            role: 'Final Reviewer',
            profile: 'P1',
            model: 'Claude Opus 5.5',
            status: 'Assigned',
            evidenceSource: 'Risk Class M rule'
          }
        ],
        boardDriftReason: null
      }
    ];

    stubDashboardFetch(busySnapshot);
    const { unmount } = render(<App />);
    try {
      const writerSeat = await screen.findByRole('button', {
        name: /P2 Writer · GPT-6\.2 Sol · Active · Live working/i
      });
      expect(writerSeat).toHaveAttribute('data-live-working', 'true');
      expect(writerSeat.querySelector('.roundtable-pulse-halo')).not.toBeNull();
      expect(writerSeat.querySelector('.roundtable-pulse-dot')).not.toBeNull();

      const reviewerSeat = screen.getByRole('button', {
        name: /P1 Final Reviewer · Claude Opus 5\.5 · Assigned/i
      });
      expect(reviewerSeat).toHaveAttribute('data-live-working', 'false');
      expect(reviewerSeat.querySelector('.roundtable-pulse-halo')).toBeNull();
    } finally {
      unmount();
    }

    // Standby monitor -> no pulse halo rendered even on Active participant
    const standbySnapshot = createSnapshotFixture();
    standbySnapshot.monitors.monitors = standbySnapshot.monitors.monitors.map((m) =>
      m.id === 'codex-monitor' ? { ...m, state: 'standby' as const } : m
    );
    standbySnapshot.pipeline.activeIssues = busySnapshot.pipeline.activeIssues;
    stubDashboardFetch(standbySnapshot);
    render(<App />);

    const standbyWriterSeat = await screen.findByRole('button', {
      name: /P2 Writer · GPT-6.2 Sol · Active$/i
    });
    expect(standbyWriterSeat).toHaveAttribute('data-live-working', 'false');
    expect(standbyWriterSeat.querySelector('.roundtable-pulse-halo')).toBeNull();
  });
});
