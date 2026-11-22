import { useState } from 'react';
import { Button, Card, Text, makeStyles, tokens } from '@fluentui/react-components';
import {
  ArrowRight16Regular,
  BranchFork16Regular,
  ShieldCheckmark16Regular
} from '@fluentui/react-icons';
import { getIdentityStyles, getStatusStyles, type StatusKey } from '../theme/mobaFluentTheme';
import type { DashboardSnapshot, MonitorInfo } from '../types/dashboard';
import {
  countPipelineSeats,
  deriveActiveAgentsSummary,
  resolveActiveSummaryTone
} from './ActiveAgentsStrip';
import { StatusBadge } from './shared/StatusBadge';
import { ContextChip } from './shared/ContextChip';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: '14px'
  },
  introCard: {
    padding: '12px 16px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  introRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    flexWrap: 'wrap'
  },
  card: {
    padding: '14px 16px',
    gap: '10px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  cardHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '10px',
    flexWrap: 'wrap'
  },
  sectionTitle: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  svgWrap: {
    width: '100%',
    overflowX: 'auto'
  },
  detailBanner: {
    padding: '10px 12px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    display: 'flex',
    flexDirection: 'column',
    gap: '4px'
  },
  bottomSplit: {
    display: 'grid',
    gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
    gap: '14px',
    '@media (max-width: 999px)': {
      gridTemplateColumns: 'minmax(0, 1fr)'
    }
  }
});

type TopologyNodeId =
  | 'maintainer'
  | 'triad'
  | 'codex-monitor'
  | 'claude-monitor'
  | 'antigravity-monitor'
  | 'subagents'
  | 'delivery';

type LifecycleStageId = 'planned' | 'ready' | 'in-progress' | 'in-review' | 'done';

function stateToStatusKey(state: string): StatusKey {
  switch (state) {
    case 'standby':
      return 'ok';
    case 'busy':
    case 'rate_limited':
      return 'warn';
    case 'offline':
      return 'danger';
    default:
      return 'neutral';
  }
}

function stateLabelShort(state: string): string {
  switch (state) {
    case 'busy':
      return 'Busy';
    case 'standby':
      return 'Standby';
    case 'rate_limited':
      return 'Quota Pause';
    case 'offline':
      return 'Offline';
    default:
      return 'Unknown';
  }
}

function findMonitor(monitors: MonitorInfo[], id: string): MonitorInfo | undefined {
  return monitors.find((m) => m.id === id);
}

interface ArchitectureDiagramsViewProps {
  snapshot: DashboardSnapshot;
  isDark: boolean;
  onOpenLive: () => void;
  onOpenMatrix: () => void;
}

export function ArchitectureDiagramsView({
  snapshot,
  isDark,
  onOpenLive,
  onOpenMatrix
}: ArchitectureDiagramsViewProps) {
  const styles = useStyles();
  const [selectedTopology, setSelectedTopology] = useState<TopologyNodeId>('subagents');
  const [selectedStage, setSelectedStage] = useState<LifecycleStageId>('in-progress');

  const okColors = getStatusStyles('ok', isDark);
  const warnColors = getStatusStyles('warn', isDark);
  const dangerColors = getStatusStyles('danger', isDark);
  const neutralColors = getStatusStyles('neutral', isDark);

  const p2Colors = getIdentityStyles('p2', isDark);
  const p1Colors = getIdentityStyles('p1', isDark);
  const p3Colors = getIdentityStyles('p3', isDark);
  const p4Colors = getIdentityStyles('p4', isDark);
  const triadColors = getIdentityStyles('triad', isDark);

  const summary = deriveActiveAgentsSummary(snapshot.monitors);
  const codexMon = findMonitor(snapshot.monitors.monitors, 'codex-monitor');
  const claudeMon = findMonitor(snapshot.monitors.monitors, 'claude-monitor');
  const agMon = findMonitor(snapshot.monitors.monitors, 'antigravity-monitor');

  const activeIssues = snapshot.pipeline.activeIssues ?? [];
  const plannedCount = activeIssues.filter((i) =>
    /planned|inbox|backlog/i.test(i.kanbanStatus)
  ).length;
  const readyCount = activeIssues.filter((i) => /^ready$/i.test(i.kanbanStatus)).length;
  const inProgressCount = activeIssues.filter((i) =>
    /in progress/i.test(i.kanbanStatus)
  ).length;
  const inReviewCount = activeIssues.filter((i) =>
    /in review/i.test(i.kanbanStatus)
  ).length;
  const openPrCount = snapshot.pipeline.pullRequests.length;
  const mergedCount = snapshot.pipeline.recentlyMerged.length;

  const participantSeatCount = countPipelineSeats(snapshot.pipeline);

  const topologyDetails: Record<
    TopologyNodeId,
    { title: string; badge: string; tone: StatusKey; body: string }
  > = {
    maintainer: {
      title: 'Human Maintainer (Andreas)',
      badge: 'Goal & Approval Authority',
      tone: 'neutral',
      body: 'Defines product goals, moves issues to Ready (the mandatory start signal), resolves ambiguous trade-offs, and performs manual merges whenever required.'
    },
    triad: {
      title: 'Decision & Steering Triad (Standing Council)',
      badge: `3/3 Seats · Code WIP ${openPrCount}/3`,
      tone: openPrCount > 3 ? 'danger' : 'ok',
      body: 'The standing alignment layer connecting Codex Monitor (P2 Technical Director & CPM), Claude Monitor (P1 COO), and Antigravity Monitor (P3 Head of AI Strategy). During a Claude Code quota pause, Codex + Antigravity activate 2/3 Acting COO failover and route fresh P1 Final Reviews via Antigravity Claude Opus 5.5.'
    },
    'codex-monitor': {
      title: `Codex Monitor · P2 (${codexMon?.model ?? 'ChatGPT Codex'})`,
      badge: `${stateLabelShort(codexMon?.state ?? 'offline')} · Sub-agents: ${codexMon?.activeSubAgentCount ?? 0}`,
      tone: stateToStatusKey(codexMon?.state ?? 'offline'),
      body: 'Standing ChatGPT Codex session running as Technical Director & CPM. Tracks Kanban slices, dispatches Codex Writer / Reviewer sub-agents in isolated worktrees, and runs read-only P2 cross-family reviews.'
    },
    'claude-monitor': {
      title: `Claude Monitor · P1 (${claudeMon?.model ?? 'Claude Code'})`,
      badge: `${stateLabelShort(claudeMon?.state ?? 'offline')} · Sub-agents: ${claudeMon?.activeSubAgentCount ?? 0}`,
      tone: stateToStatusKey(claudeMon?.state ?? 'offline'),
      body: 'Standing Claude Code session acting as Chief Operating Officer (COO). Spawns fresh P1 sub-agents for deep architectural implementation and clean-context Final Reviews.'
    },
    'antigravity-monitor': {
      title: `Antigravity Monitor · P3 (${agMon?.model ?? 'Antigravity'})`,
      badge: `${stateLabelShort(agMon?.state ?? 'offline')} · Sub-agents: ${agMon?.activeSubAgentCount ?? 0}`,
      tone: stateToStatusKey(agMon?.state ?? 'offline'),
      body: 'Standing Antigravity host acting as Head of AI Strategy & Multi-Model Failover Hub. Runs Gemini 4 (Argon / Neon / Hydrogen) alongside built-in Claude Opus 5.5, Claude Sonnet 5.5, and GPT-OSS 120B so P1–P4 workflows continue cleanly during external quota limits.'
    },
    subagents: {
      title: 'Ephemeral Runtime Sub-Agents (Spawned per Task / PR)',
      badge: `${summary.activeSubAgentsTotal} Active Runtime · ${participantSeatCount} Pipeline Seats`,
      tone: summary.activeSubAgentsTotal > 0 ? 'warn' : 'ok',
      body: 'Short-lived specialists created inside tool sessions for a specific slice (Writer, Solution Architect, Companion Reviewer, Fresh P1 Final Reviewer, Cross-Family Verifier, UI/UX, MOBAnews, or P4 Fast Delivery). They work strictly inside isolated git worktrees and terminate when their slice closes.'
    },
    delivery: {
      title: 'Repositories & GitHub Project Source of Truth',
      badge: `${openPrCount} Open PRs · ${activeIssues.length} Active Issues`,
      tone: snapshot.warningCount > 0 ? 'warn' : 'ok',
      body: 'All decisions, locks, checks, and evidence converge back into MOBAflow, MOBAdev, and the GitHub Kanban board so no work exists only in private chat history.'
    }
  };

  const stageDetails: Record<
    LifecycleStageId,
    { title: string; countLabel: string; tone: StatusKey; body: string }
  > = {
    planned: {
      title: 'Stage 1 · Inbox & Planned',
      countLabel: `${plannedCount} issue(s)`,
      tone: 'neutral',
      body: 'Ideas, OneNote capture notes, and refined specs reside here before implementation starts. Agents may analyze or plan, but MUST NOT start coding while an item is Inbox or Planned.'
    },
    ready: {
      title: 'Stage 2 · Ready (Human Maintainer Trigger)',
      countLabel: `${readyCount} issue(s)`,
      tone: readyCount > 0 ? 'ok' : 'neutral',
      body: 'Explicit authorization from the maintainer to execute. Once Ready, the Triad classifies Risk (L/M/H), reserves LockedAreas, and creates an isolated git worktree.'
    },
    'in-progress': {
      title: 'Stage 3 · In progress (Worktrees + Active Sub-Agents)',
      countLabel: `${inProgressCount} issue(s) · Code WIP ${openPrCount}/3`,
      tone: openPrCount > 3 ? 'danger' : inProgressCount > 0 ? 'warn' : 'neutral',
      body: 'Standing Monitors spawn ephemeral Writer & Companion Sub-Agents per task. Maximum 3 parallel code branches at once; shared or high-risk areas require non-overlapping area locks.'
    },
    'in-review': {
      title: 'Stage 4 · In review (CI, Sonar & Cross-Family Gates)',
      countLabel: `${inReviewCount} issue(s) · ${openPrCount} Open PR(s)`,
      tone: openPrCount > 0 ? 'warn' : 'neutral',
      body: 'Automated GitHub CI & SonarCloud run alongside a Fresh P1 Final Reviewer and Cross-Family Verifier (distinct AI vendor family). PRs stay Draft/Blocked until every required gate turns green.'
    },
    done: {
      title: 'Stage 5 · Done (Head-Bound Auto-Merge & Branch Hygiene)',
      countLabel: `${mergedCount} recently merged`,
      tone:
        snapshot.hygiene.staleWorktreeCount +
          (snapshot.hygiene.staleLocalBranchCount ?? 0) +
          (snapshot.hygiene.staleRemoteBranchCount ?? 0) >
        0
          ? 'warn'
          : 'ok',
      body: 'Once all Merge Gates are CLEAN, Head-Bound Squash Auto-Merge lands the commit on main and Branch Hygiene removes stale worktrees, local branches, and extra tool sessions.'
    }
  };

  const activeTopoInfo = topologyDetails[selectedTopology];
  const activeStageInfo = stageDetails[selectedStage];

  const monitorBoxes: Array<{
    id: 'codex-monitor' | 'claude-monitor' | 'antigravity-monitor';
    x: number;
    title: string;
    subtitle: string;
    profile: string;
    identityColors: ReturnType<typeof getIdentityStyles>;
    monitor: MonitorInfo | undefined;
  }> = [
    {
      id: 'codex-monitor',
      x: 40,
      title: 'Codex Monitor',
      subtitle: 'Technical Director & CPM',
      profile: 'P2 · ChatGPT Codex',
      identityColors: p2Colors,
      monitor: codexMon
    },
    {
      id: 'claude-monitor',
      x: 340,
      title: 'Claude Monitor',
      subtitle: 'Chief Operating Officer',
      profile: 'P1 · Claude Code',
      identityColors: p1Colors,
      monitor: claudeMon
    },
    {
      id: 'antigravity-monitor',
      x: 640,
      title: 'Antigravity Monitor',
      subtitle: 'Head of AI Strategy',
      profile: 'P3 · Antigravity',
      identityColors: p3Colors,
      monitor: agMon
    }
  ];

  const lifecycleNodes: Array<{
    id: LifecycleStageId;
    x: number;
    label: string;
    sublabel: string;
    count: string;
    colors: ReturnType<typeof getStatusStyles>;
  }> = [
    {
      id: 'planned',
      x: 20,
      label: '1. Planned / Inbox',
      sublabel: 'Triage & Spec',
      count: `${plannedCount} item(s)`,
      colors: neutralColors
    },
    {
      id: 'ready',
      x: 200,
      label: '2. Ready',
      sublabel: 'Maintainer Trigger',
      count: `${readyCount} ready`,
      colors: okColors
    },
    {
      id: 'in-progress',
      x: 380,
      label: '3. In progress',
      sublabel: 'Worktree + Sub-Agents',
      count: `${inProgressCount} active`,
      colors: warnColors
    },
    {
      id: 'in-review',
      x: 560,
      label: '4. In review',
      sublabel: 'CI + Sonar + P1 + X-Fam',
      count: `${openPrCount} open PR(s)`,
      colors: warnColors
    },
    {
      id: 'done',
      x: 740,
      label: '5. Done',
      sublabel: 'Auto-Merge + Hygiene',
      count: `${mergedCount} merged`,
      colors: okColors
    }
  ];

  const summaryTone = resolveActiveSummaryTone(summary);

  return (
    <div className={styles.root}>
      {/* Overview header card */}
      <Card className={styles.introCard} appearance="filled">
        <div className={styles.introRow}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <BranchFork16Regular aria-hidden="true" />
            <Text weight="semibold" size={400}>
              System Architecture, Agent Hierarchy &amp; Live Workflow Diagrams
            </Text>
            <StatusBadge tone={summaryTone}>
              <span className="tabular">
                {summary.standingMonitorsOnline}/{summary.standingMonitorsTotal} Monitors ·{' '}
                {summary.activeSubAgentsTotal} Live/Recent Sub-Agents
              </span>
            </StatusBadge>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
              Click or focus any block in the diagrams below for plain-English context.
            </Text>
            <Button
              size="small"
              appearance="subtle"
              icon={<ArrowRight16Regular />}
              iconPosition="after"
              onClick={onOpenLive}
            >
              Back to Live Operations
            </Button>
          </div>
        </div>
      </Card>

      {/* Diagram 1: Live Agent & Sub-Agent Topology */}
      <Card className={styles.card} appearance="filled">
        <div className={styles.cardHeader}>
          <h2 className={styles.sectionTitle}>
            1. Swarm Topology: Standing Monitors, Triad &amp; Ephemeral Sub-Agents
          </h2>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <ContextChip appearance="tint">
              Order: Codex (P2) → Claude (P1) → Antigravity (P3)
            </ContextChip>
            <StatusBadge
              tone={summary.activeSubAgentsTotal > 0 ? 'warn' : 'neutral'}
              title="Antigravity uses live RUNNING status; Codex (3m) and Claude (2m mtime + busy sessions) reflect recent sub-agent activity"
            >
              <span className="tabular">Live/Recent Sub-Agents: {summary.activeSubAgentsTotal}</span>
            </StatusBadge>
          </div>
        </div>

        <div className={styles.svgWrap}>
          <svg
            viewBox="0 0 920 430"
            width="100%"
            style={{ minWidth: 680, maxWidth: '100%', height: 'auto', display: 'block' }}
            role="group"
            aria-label="Interactive Swarm Topology Diagram connecting Maintainer, Triad, 3 Standing Monitors, Ephemeral Sub-Agents, and Repositories"
          >
            {/* Layer 1 -> Layer 2 trunk */}
            <line
              x1="460"
              y1="56"
              x2="460"
              y2="86"
              stroke={tokens.colorNeutralStroke1}
              strokeWidth="2"
            />
            <polygon points="455,84 465,84 460,92" fill={tokens.colorNeutralStroke1} />

            {/* Triad horizontal bus connecting the 3 Monitors */}
            <line
              x1="160"
              y1="146"
              x2="760"
              y2="146"
              stroke={triadColors.solid}
              strokeWidth="2"
            />
            <line x1="160" y1="146" x2="160" y2="168" stroke={p2Colors.solid} strokeWidth="2" />
            <line x1="460" y1="146" x2="460" y2="168" stroke={p1Colors.solid} strokeWidth="2" />
            <line x1="760" y1="146" x2="760" y2="168" stroke={p3Colors.solid} strokeWidth="2" />

            {/* Arrows down from 3 Monitors into Sub-Agent Runtime Pool */}
            {[160, 460, 760].map((cx, idx) => {
              const stroke = idx === 0 ? p2Colors.solid : idx === 1 ? p1Colors.solid : p3Colors.solid;
              return (
                <g key={cx}>
                  <line x1={cx} y1="252" x2={cx} y2="284" stroke={stroke} strokeWidth="2" />
                  <polygon points={`${cx - 5},282 ${cx + 5},282 ${cx},290`} fill={stroke} />
                </g>
              );
            })}

            {/* Arrow down from Sub-Agents to Repositories */}
            <line
              x1="460"
              y1="356"
              x2="460"
              y2="376"
              stroke={tokens.colorNeutralStroke1}
              strokeWidth="2"
            />
            <polygon points="455,374 465,374 460,382" fill={tokens.colorNeutralStroke1} />

            {/* Layer 1: Maintainer */}
            <g
              role="button"
              tabIndex={0}
              aria-label="Select Maintainer layer"
              aria-pressed={selectedTopology === 'maintainer'}
              aria-controls="topology-node-details"
              style={{ cursor: 'pointer' }}
              onClick={() => setSelectedTopology('maintainer')}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  setSelectedTopology('maintainer');
                }
              }}
            >
              <rect
                x="300"
                y="10"
                width="320"
                height="46"
                rx="8"
                fill={tokens.colorNeutralBackground2}
                stroke={
                  selectedTopology === 'maintainer'
                    ? tokens.colorBrandForeground1
                    : tokens.colorNeutralStroke1
                }
                strokeWidth={selectedTopology === 'maintainer' ? 2.4 : 1.4}
              />
              <text
                x="460"
                y="30"
                textAnchor="middle"
                fill={tokens.colorNeutralForeground1}
                fontSize="13"
                fontWeight="700"
              >
                Human Maintainer (Andreas)
              </text>
              <text
                x="460"
                y="47"
                textAnchor="middle"
                fill={tokens.colorNeutralForeground2}
                fontSize="11"
              >
                Product Goals · Ready Approval Trigger · Escalations
              </text>
            </g>

            {/* Layer 2A: Decision & Steering Triad */}
            <g
              role="button"
              tabIndex={0}
              aria-label="Select Decision and Steering Triad layer"
              aria-pressed={selectedTopology === 'triad'}
              aria-controls="topology-node-details"
              style={{ cursor: 'pointer' }}
              onClick={() => setSelectedTopology('triad')}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  setSelectedTopology('triad');
                }
              }}
            >
              <rect
                x="230"
                y="94"
                width="460"
                height="46"
                rx="8"
                fill={triadColors.bg}
                stroke={
                  selectedTopology === 'triad' ? tokens.colorBrandForeground1 : triadColors.solid
                }
                strokeWidth={selectedTopology === 'triad' ? 2.4 : 1.6}
              />
              <text
                x="460"
                y="114"
                textAnchor="middle"
                fill={triadColors.fg}
                fontSize="13"
                fontWeight="700"
              >
                Decision &amp; Steering Triad (Purple Identity)
              </text>
              <text
                x="460"
                y="131"
                textAnchor="middle"
                fill={tokens.colorNeutralForeground1}
                fontSize="11"
              >
                Risk L / M / H · Slice Scope · Area Locks · Code WIP {openPrCount}/3
              </text>
            </g>

            {/* Layer 2B: 3 Standing Monitors */}
            {monitorBoxes.map((box) => {
              const stKey = stateToStatusKey(box.monitor?.state ?? 'offline');
              const stColors = getStatusStyles(stKey, isDark);
              const subCount = box.monitor?.activeSubAgentCount ?? 0;
              const isSelected = selectedTopology === box.id;

              return (
                <g
                  key={box.id}
                  role="button"
                  tabIndex={0}
                  aria-label={`Select ${box.title}`}
                  aria-pressed={isSelected}
                  aria-controls="topology-node-details"
                  style={{ cursor: 'pointer' }}
                  onClick={() => setSelectedTopology(box.id)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      setSelectedTopology(box.id);
                    }
                  }}
                >
                  <rect
                    x={box.x}
                    y="168"
                    width="240"
                    height="84"
                    rx="8"
                    fill={box.identityColors.bg}
                    stroke={isSelected ? tokens.colorBrandForeground1 : box.identityColors.solid}
                    strokeWidth={isSelected ? 2.5 : 1.6}
                  />
                  <rect
                    x={box.x}
                    y="168"
                    width="240"
                    height="5"
                    rx="3"
                    fill={box.identityColors.solid}
                  />
                  <text
                    x={box.x + 12}
                    y="191"
                    fill={box.identityColors.fg}
                    fontSize="13"
                    fontWeight="700"
                  >
                    {box.title}
                  </text>
                  <text
                    x={box.x + 12}
                    y="207"
                    fill={tokens.colorNeutralForeground2}
                    fontSize="11"
                  >
                    {box.subtitle} · {box.profile}
                  </text>

                  {/* Status Pill inside Monitor Card */}
                  <rect
                    x={box.x + 12}
                    y="218"
                    width="96"
                    height="22"
                    rx="11"
                    fill={stColors.bg}
                    stroke={stColors.border}
                    strokeWidth="1"
                  />
                  <circle cx={box.x + 24} cy="229" r="4" fill={stColors.solid} />
                  <text
                    x={box.x + 33}
                    y="233"
                    fill={stColors.fg}
                    fontSize="11"
                    fontWeight="600"
                  >
                    {stateLabelShort(box.monitor?.state ?? 'offline')}
                  </text>

                  {/* Sub-agent Counter Pill inside Monitor Card */}
                  <rect
                    x={box.x + 116}
                    y="218"
                    width="112"
                    height="22"
                    rx="11"
                    fill={subCount > 0 ? warnColors.bg : neutralColors.bg}
                    stroke={subCount > 0 ? warnColors.border : neutralColors.border}
                    strokeWidth="1"
                  />
                  <text
                    x={box.x + 172}
                    y="233"
                    textAnchor="middle"
                    fill={subCount > 0 ? warnColors.fg : neutralColors.fg}
                    fontSize="11"
                    fontWeight="600"
                  >
                    Sub-agents: {subCount}
                  </text>
                </g>
              );
            })}

            {/* Layer 3: Ephemeral Sub-Agent Pool */}
            <g
              role="button"
              tabIndex={0}
              aria-label="Select Ephemeral Sub-Agents layer"
              aria-pressed={selectedTopology === 'subagents'}
              aria-controls="topology-node-details"
              style={{ cursor: 'pointer' }}
              onClick={() => setSelectedTopology('subagents')}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  setSelectedTopology('subagents');
                }
              }}
            >
              <rect
                x="40"
                y="290"
                width="840"
                height="66"
                rx="8"
                fill={summary.activeSubAgentsTotal > 0 ? warnColors.bg : tokens.colorNeutralBackground2}
                stroke={
                  selectedTopology === 'subagents'
                    ? tokens.colorBrandForeground1
                    : summary.activeSubAgentsTotal > 0
                      ? warnColors.solid
                      : tokens.colorNeutralStroke1
                }
                strokeWidth={selectedTopology === 'subagents' ? 2.5 : 1.5}
              />
              <text
                x="56"
                y="313"
                fill={tokens.colorNeutralForeground1}
                fontSize="13"
                fontWeight="700"
              >
                Ephemeral Task &amp; PR Sub-Agents (Spawned per Slice inside Isolated Worktrees)
              </text>
              <text
                x="864"
                y="313"
                textAnchor="end"
                fill={summary.activeSubAgentsTotal > 0 ? warnColors.fg : neutralColors.fg}
                fontSize="12"
                fontWeight="700"
              >
                Live/Recent: {summary.activeSubAgentsTotal} · Pipeline Seats: {participantSeatCount}
              </text>
              <text
                x="56"
                y="335"
                fill={tokens.colorNeutralForeground2}
                fontSize="11.5"
              >
                Writer / Companion · Fresh P1 Final (Opus 5.5 Failover) · X-Family (Codex / GPT-OSS) · UI/UX · P4 Sonnet 5.5 / Hydrogen (Pink)
              </text>
              <circle cx="854" cy="332" r="5" fill={p4Colors.solid} />
            </g>

            {/* Layer 4: Output Repos */}
            <g
              role="button"
              tabIndex={0}
              aria-label="Select Delivery Repositories layer"
              aria-pressed={selectedTopology === 'delivery'}
              aria-controls="topology-node-details"
              style={{ cursor: 'pointer' }}
              onClick={() => setSelectedTopology('delivery')}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  setSelectedTopology('delivery');
                }
              }}
            >
              <rect
                x="40"
                y="382"
                width="840"
                height="40"
                rx="8"
                fill={tokens.colorNeutralBackground3}
                stroke={
                  selectedTopology === 'delivery'
                    ? tokens.colorBrandForeground1
                    : tokens.colorNeutralStroke1
                }
                strokeWidth={selectedTopology === 'delivery' ? 2.4 : 1.3}
              />
              <text
                x="460"
                y="406"
                textAnchor="middle"
                fill={tokens.colorNeutralForeground1}
                fontSize="12.5"
                fontWeight="600"
              >
                Live Source of Truth: MOBAflow ({snapshot.hygiene.worktrees.length} Worktree/s) · MOBAdev (Rules &amp; Dashboard) · GitHub Project Kanban
              </text>
            </g>
          </svg>
        </div>

        <div
          id="topology-node-details"
          className={styles.detailBanner}
          role="region"
          aria-label="Selected topology node details"
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <Text weight="semibold" size={300}>
              {activeTopoInfo.title}
            </Text>
            <StatusBadge tone={activeTopoInfo.tone} size="small">
              {activeTopoInfo.badge}
            </StatusBadge>
          </div>
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            {activeTopoInfo.body}
          </Text>
        </div>
      </Card>

      {/* Diagram 2: End-to-End Task Lifecycle & Merge Gates */}
      <Card className={styles.card} appearance="filled">
        <div className={styles.cardHeader}>
          <h2 className={styles.sectionTitle}>
            2. End-to-End Task Lifecycle: From Maintainer Ready Trigger to Clean Merge
          </h2>
          <ContextChip appearance="outline">
            Workflow Order: Planned → Ready → In progress → In review → Done
          </ContextChip>
        </div>

        <div className={styles.svgWrap}>
          <svg
            viewBox="0 0 920 120"
            width="100%"
            style={{ minWidth: 680, maxWidth: '100%', height: 'auto', display: 'block' }}
            role="group"
            aria-label="Task Lifecycle Diagram from Planned to Ready, In progress, In review, and Done"
          >
            {lifecycleNodes.map((node, idx) => {
              const isSelected = selectedStage === node.id;
              return (
                <g key={node.id}>
                  <g
                    role="button"
                    tabIndex={0}
                    aria-label={`Select lifecycle stage ${node.label}`}
                    aria-pressed={isSelected}
                    aria-controls="lifecycle-stage-details"
                    style={{ cursor: 'pointer' }}
                    onClick={() => setSelectedStage(node.id)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        setSelectedStage(node.id);
                      }
                    }}
                  >
                    <rect
                      x={node.x}
                      y="12"
                      width="158"
                      height="92"
                      rx="8"
                      fill={node.colors.bg}
                      stroke={isSelected ? tokens.colorBrandForeground1 : node.colors.solid}
                      strokeWidth={isSelected ? 2.5 : 1.5}
                    />
                    <text
                      x={node.x + 12}
                      y="36"
                      fill={node.colors.fg}
                      fontSize="12.5"
                      fontWeight="700"
                    >
                      {node.label}
                    </text>
                    <text
                      x={node.x + 12}
                      y="56"
                      fill={tokens.colorNeutralForeground1}
                      fontSize="11"
                    >
                      {node.sublabel}
                    </text>
                    <rect
                      x={node.x + 12}
                      y="70"
                      width="134"
                      height="22"
                      rx="11"
                      fill={tokens.colorNeutralBackground1}
                      stroke={node.colors.border}
                      strokeWidth="1"
                    />
                    <text
                      x={node.x + 79}
                      y="85"
                      textAnchor="middle"
                      fill={node.colors.fg}
                      fontSize="11"
                      fontWeight="600"
                    >
                      {node.count}
                    </text>
                  </g>

                  {idx < lifecycleNodes.length - 1 && (
                    <g>
                      <line
                        x1={node.x + 158}
                        y1="58"
                        x2={node.x + 174}
                        y2="58"
                        stroke={tokens.colorNeutralStroke1}
                        strokeWidth="2"
                      />
                      <polygon
                        points={`${node.x + 172},53 ${node.x + 172},63 ${node.x + 180},58`}
                        fill={tokens.colorNeutralStroke1}
                      />
                    </g>
                  )}
                </g>
              );
            })}
          </svg>
        </div>

        <div
          id="lifecycle-stage-details"
          className={styles.detailBanner}
          role="region"
          aria-label="Selected lifecycle stage details"
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <Text weight="semibold" size={300}>
              {activeStageInfo.title}
            </Text>
            <StatusBadge tone={activeStageInfo.tone} size="small">
              {activeStageInfo.countLabel}
            </StatusBadge>
          </div>
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            {activeStageInfo.body}
          </Text>
        </div>
      </Card>

      {/* Diagram 3: Color System & Security Boundary */}
      <div className={styles.bottomSplit}>
        <Card className={styles.card} appearance="filled">
          <div className={styles.cardHeader}>
            <h2 className={styles.sectionTitle}>3A. Orthogonal 2-Axis Color Architecture</h2>
            <Button size="small" appearance="subtle" onClick={onOpenMatrix}>
              View Governance Rules
            </Button>
          </div>

          <svg
            viewBox="0 0 430 175"
            width="100%"
            style={{ height: 'auto', display: 'block' }}
            role="img"
            aria-label="2-Axis Color Architecture separating AI Identity from Semantic Status"
          >
            {/* Axis 1 box */}
            <rect
              x="6"
              y="8"
              width="202"
              height="158"
              rx="8"
              fill={tokens.colorNeutralBackground2}
              stroke={tokens.colorNeutralStroke2}
            />
            <text
              x="18"
              y="28"
              fill={tokens.colorNeutralForeground1}
              fontSize="12"
              fontWeight="700"
            >
              Axis 1 · AI Identity (WHO)
            </text>
            {[
              { y: 42, label: 'P2 · ChatGPT / Codex (Teal)', c: p2Colors },
              { y: 66, label: 'P1 · Claude / Opus (Orange)', c: p1Colors },
              { y: 90, label: 'P3 · Antigravity / Gemini (Blue)', c: p3Colors },
              { y: 114, label: 'P4 · Fast Delivery (Pink + Flash)', c: p4Colors },
              { y: 138, label: 'Triad · Steering Council (Purple)', c: triadColors }
            ].map((row) => (
              <g key={row.label}>
                <circle cx="26" cy={row.y + 6} r="6" fill={row.c.solid} />
                <text
                  x="38"
                  y={row.y + 10}
                  fill={row.c.fg}
                  fontSize="11"
                  fontWeight="600"
                >
                  {row.label}
                </text>
              </g>
            ))}

            {/* Axis 2 + Brand box */}
            <rect
              x="220"
              y="8"
              width="204"
              height="158"
              rx="8"
              fill={tokens.colorNeutralBackground2}
              stroke={tokens.colorNeutralStroke2}
            />
            <text
              x="232"
              y="28"
              fill={tokens.colorNeutralForeground1}
              fontSize="12"
              fontWeight="700"
            >
              Axis 2 · Status (STATE)
            </text>
            {[
              { y: 42, label: 'Green · Pass / Standby / Ready', c: okColors },
              { y: 66, label: 'Amber · Busy / Pending / Drift', c: warnColors },
              { y: 90, label: 'Red · Blocked / Fail / Class H', c: dangerColors },
              { y: 114, label: 'Neutral · Chrome / Idle / Info', c: neutralColors }
            ].map((row) => (
              <g key={row.label}>
                <rect
                  x="232"
                  y={row.y}
                  width="12"
                  height="12"
                  rx="3"
                  fill={row.c.solid}
                />
                <text
                  x="250"
                  y={row.y + 10}
                  fill={row.c.fg}
                  fontSize="11"
                  fontWeight="600"
                >
                  {row.label}
                </text>
              </g>
            ))}
            <line
              x1="232"
              y1="132"
              x2="412"
              y2="132"
              stroke={tokens.colorNeutralStroke2}
            />
            <text
              x="232"
              y="150"
              fill={tokens.colorBrandForeground1}
              fontSize="11"
              fontWeight="700"
            >
              Interaction Blue: Tabs, Links &amp; Focus Only
            </text>
          </svg>
        </Card>

        <Card className={styles.card} appearance="filled">
          <div className={styles.cardHeader}>
            <h2 className={styles.sectionTitle}>3B. Read-Only &amp; Privacy Boundary</h2>
            <StatusBadge tone="ok" icon={<ShieldCheckmark16Regular />}>
              Loopback Guarded
            </StatusBadge>
          </div>

          <svg
            viewBox="0 0 430 175"
            width="100%"
            style={{ height: 'auto', display: 'block' }}
            role="img"
            aria-label="Read-Only Dashboard Security and Privacy Boundary Diagram"
          >
            {/* Column 1: Sources */}
            <rect
              x="6"
              y="12"
              width="128"
              height="150"
              rx="8"
              fill={tokens.colorNeutralBackground2}
              stroke={tokens.colorNeutralStroke2}
            />
            <text
              x="70"
              y="32"
              textAnchor="middle"
              fill={tokens.colorNeutralForeground1}
              fontSize="11.5"
              fontWeight="700"
            >
              Live Sources
            </text>
            <text x="16" y="54" fill={p2Colors.fg} fontSize="10.5" fontWeight="600">
              Codex SQLite (ro)
            </text>
            <text x="16" y="74" fill={p1Colors.fg} fontSize="10.5" fontWeight="600">
              Claude Metadata
            </text>
            <text x="16" y="94" fill={p3Colors.fg} fontSize="10.5" fontWeight="600">
              Antigravity DB (ro)
            </text>
            <text x="16" y="114" fill={neutralColors.fg} fontSize="10.5">
              git + gh CLI
            </text>
            <text x="16" y="138" fill={dangerColors.fg} fontSize="10" fontWeight="600">
              Blocked: credentials,
            </text>
            <text x="16" y="151" fill={dangerColors.fg} fontSize="10" fontWeight="600">
              .env, keys, chat text
            </text>

            {/* Arrow 1 -> 2 */}
            <line x1="134" y1="86" x2="154" y2="86" stroke={okColors.solid} strokeWidth="2" />
            <polygon points="152,81 152,91 160,86" fill={okColors.solid} />

            {/* Column 2: API Guard */}
            <rect
              x="160"
              y="12"
              width="132"
              height="150"
              rx="8"
              fill={okColors.bg}
              stroke={okColors.solid}
              strokeWidth="1.5"
            />
            <text
              x="226"
              y="32"
              textAnchor="middle"
              fill={okColors.fg}
              fontSize="11.5"
              fontWeight="700"
            >
              ASP.NET Core API
            </text>
            <text x="170" y="58" fill={tokens.colorNeutralForeground1} fontSize="10.5">
              127.0.0.1 Loopback
            </text>
            <text x="170" y="78" fill={tokens.colorNeutralForeground1} fontSize="10.5">
              GET-Only (POST 403)
            </text>
            <text x="170" y="98" fill={tokens.colorNeutralForeground1} fontSize="10.5">
              PathWhitelistGuard
            </text>
            <text x="170" y="118" fill={tokens.colorNeutralForeground1} fontSize="10.5">
              Zero Duplication
            </text>

            {/* Arrow 2 -> 3 */}
            <line x1="292" y1="86" x2="312" y2="86" stroke={okColors.solid} strokeWidth="2" />
            <polygon points="310,81 310,91 318,86" fill={okColors.solid} />

            {/* Column 3: Browser UI */}
            <rect
              x="318"
              y="12"
              width="106"
              height="150"
              rx="8"
              fill={tokens.colorNeutralBackground2}
              stroke={tokens.colorNeutralStroke2}
            />
            <text
              x="371"
              y="32"
              textAnchor="middle"
              fill={tokens.colorNeutralForeground1}
              fontSize="11.5"
              fontWeight="700"
            >
              React UI
            </text>
            <text x="328" y="60" fill={tokens.colorNeutralForeground2} fontSize="10.5">
              Fluent UI 2
            </text>
            <text x="328" y="80" fill={tokens.colorNeutralForeground2} fontSize="10.5">
              Light &amp; Dark AA
            </text>
            <text x="328" y="100" fill={tokens.colorNeutralForeground2} fontSize="10.5">
              15s Live Poll
            </text>
            <text x="328" y="120" fill={tokens.colorNeutralForeground2} fontSize="10.5">
              Zero Write UI
            </text>
          </svg>
        </Card>
      </div>
    </div>
  );
}
