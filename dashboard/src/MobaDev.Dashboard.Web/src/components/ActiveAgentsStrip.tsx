import { Badge, Card, Text, makeStyles, tokens } from '@fluentui/react-components';
import { Bot16Regular, PeopleTeam16Regular } from '@fluentui/react-icons';
import { getStatusStyles, getToneStyles, type StatusKey } from '../theme/mobaFluentTheme';
import type {
  ActiveAgentsSummary,
  MonitorInfo,
  MonitorsResponse,
  PipelineResponse
} from '../types/dashboard';
import { resolveProfileTone, resolveToolTone } from './RoleAndModelIcons';
import { ContextChip } from './shared/ContextChip';
import { StatusBadge } from './shared/StatusBadge';

const useStyles = makeStyles({
  card: {
    padding: '8px 14px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    flexWrap: 'wrap'
  },
  leftGroup: {
    display: 'flex',
    alignItems: 'center',
    gap: '10px',
    flexWrap: 'wrap'
  },
  rightGroup: {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    flexWrap: 'wrap'
  },
  toolChip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    padding: '3px 9px',
    borderRadius: tokens.borderRadiusMedium,
    fontSize: tokens.fontSizeBase200
  },
  dot: {
    width: '7px',
    height: '7px',
    borderRadius: '999px',
    flexShrink: 0
  }
});

const MONITOR_ORDER = ['codex-monitor', 'claude-monitor', 'antigravity-monitor'];

function sortMonitors(monitors: MonitorInfo[]): MonitorInfo[] {
  return [...monitors].sort((a, b) => {
    const idxA = MONITOR_ORDER.indexOf(a.id);
    const idxB = MONITOR_ORDER.indexOf(b.id);
    return (idxA === -1 ? 99 : idxA) - (idxB === -1 ? 99 : idxB);
  });
}

export function deriveActiveAgentsSummary(monitorsResponse: MonitorsResponse): ActiveAgentsSummary {
  if (monitorsResponse.summary) {
    return monitorsResponse.summary;
  }

  const monitors = monitorsResponse.monitors;
  const standingMonitorsTotal = monitors.length;
  const standingMonitorsOnline = monitors.filter(
    (m) => m.state === 'standby' || m.state === 'busy' || m.state === 'rate_limited'
  ).length;
  const standingMonitorsBusy = monitors.filter((m) => m.state === 'busy').length;
  const activeSubAgentsTotal = monitors.reduce((acc, m) => acc + (m.activeSubAgentCount ?? 0), 0);

  return {
    standingMonitorsTotal,
    standingMonitorsOnline,
    standingMonitorsBusy,
    activeSubAgentsTotal,
    totalActiveAgents: standingMonitorsBusy + activeSubAgentsTotal
  };
}

export function resolveActiveSummaryTone(summary: ActiveAgentsSummary): StatusKey {
  if (summary.totalActiveAgents > 0) {
    return 'warn';
  }

  return summary.standingMonitorsOnline > 0 ? 'ok' : 'danger';
}

export function countPipelineSeats(pipeline: PipelineResponse): number {
  const prSeats = pipeline.pullRequests.reduce((acc, pr) => acc + pr.participants.length, 0);
  const issueSeats = (pipeline.activeIssues ?? []).reduce(
    (acc, item) => acc + item.participants.length,
    0
  );
  return prSeats + issueSeats;
}

function getToolDetectionHint(id: string): string {
  if (id === 'codex-monitor') {
    return '3m active thread window (read-only)';
  }
  if (id === 'claude-monitor') {
    return 'busy extra sessions + 2m subagent mtime (read-only)';
  }
  if (id === 'antigravity-monitor') {
    return 'live RUNNING conversations (read-only)';
  }
  return 'recent activity (read-only)';
}

interface ActiveAgentsStripProps {
  monitorsResponse: MonitorsResponse;
  isDark: boolean;
  pipelineSeatCount?: number;
}

export function ActiveAgentsStrip({
  monitorsResponse,
  isDark,
  pipelineSeatCount
}: ActiveAgentsStripProps) {
  const styles = useStyles();
  const summary = deriveActiveAgentsSummary(monitorsResponse);
  const orderedMonitors = sortMonitors(monitorsResponse.monitors);

  const totalTone = resolveActiveSummaryTone(summary);

  const monitorTone =
    summary.standingMonitorsOnline === summary.standingMonitorsTotal && summary.standingMonitorsTotal > 0
      ? 'ok'
      : summary.standingMonitorsOnline > 0
        ? 'warn'
        : 'danger';

  const subTone = summary.activeSubAgentsTotal > 0 ? 'warn' : 'neutral';

  const neutralStyles = getStatusStyles('neutral', isDark);

  return (
    <section aria-label="Live Active Agents and Sub-Agents Summary">
      <Card className={styles.card} appearance="filled">
        <div className={styles.row}>
          <div className={styles.leftGroup}>
            <StatusBadge
              tone={totalTone}
              title="Standing Busy Monitors plus live/recent runtime Sub-Agents (Antigravity: RUNNING state; Codex: 3m window; Claude: busy sessions + 2m mtime)"
              icon={
                summary.totalActiveAgents > 0 ? (
                  <span className="pulse-dot" aria-hidden="true" />
                ) : (
                  <Bot16Regular aria-hidden="true" />
                )
              }
            >
              <span className="tabular">
                {summary.totalActiveAgents} Busy / Recent
              </span>
            </StatusBadge>

            <StatusBadge tone={monitorTone} appearance="outline" icon={<PeopleTeam16Regular />}>
              <span className="tabular">
                Standing Monitors: {summary.standingMonitorsOnline}/{summary.standingMonitorsTotal} online
                {summary.standingMonitorsBusy > 0 ? ` · ${summary.standingMonitorsBusy} busy` : ''}
              </span>
            </StatusBadge>

            <StatusBadge
              tone={subTone}
              appearance={summary.activeSubAgentsTotal > 0 ? 'tint' : 'outline'}
              title="Runtime sub-agents detected live (Antigravity RUNNING state) or via recent 2-3m activity window (Codex/Claude)"
            >
              <span className="tabular">
                Sub-Agents (Live/Recent): {summary.activeSubAgentsTotal}
              </span>
            </StatusBadge>

            {pipelineSeatCount !== undefined && (
              <ContextChip
                appearance="outline"
                title="Agent roles recorded on open GitHub Issues & PRs (assigned/historical Round Table seats — not necessarily running locally right now)"
              >
                <span className="tabular">
                  Pipeline Seats (GitHub): {pipelineSeatCount}
                </span>
              </ContextChip>
            )}
          </div>

          <div className={styles.rightGroup} aria-label="Live or recent sub-agents per tool">
            <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
              Sub-agents (live/recent):
            </Text>
            {orderedMonitors.map((m) => {
              const subCount = m.activeSubAgentCount ?? 0;
              const pTone = resolveProfileTone(m.profile);
              const tTone = pTone !== 'neutral' ? pTone : resolveToolTone(m.tool);
              const chipStyles = subCount > 0 ? getToneStyles(tTone, isDark) : neutralStyles;

              const shortTool =
                m.tool.includes('Codex')
                  ? 'Codex'
                  : m.tool.includes('Claude')
                    ? 'Claude'
                    : m.tool.includes('Antigravity')
                      ? 'Antigravity'
                      : m.tool;

              return (
                <div
                  key={m.id}
                  className={styles.toolChip}
                  style={{
                    backgroundColor: subCount > 0 ? chipStyles.bg : tokens.colorNeutralBackground3,
                    border: `1px solid ${subCount > 0 ? chipStyles.border : tokens.colorNeutralStroke2}`,
                    color: subCount > 0 ? chipStyles.fg : tokens.colorNeutralForeground2
                  }}
                  title={`${m.tool} (${m.profile}, tone ${tTone}): ${subCount} (${getToolDetectionHint(m.id)})`}
                >
                  <span
                    className={styles.dot}
                    style={{ backgroundColor: chipStyles.solid }}
                    aria-hidden="true"
                  />
                  <span>{shortTool}:</span>
                  <Badge
                    appearance="outline"
                    color={subCount > 0 ? 'warning' : 'subtle'}
                    shape="rounded"
                    size="small"
                    className="tabular"
                  >
                    {subCount}
                  </Badge>
                </div>
              );
            })}
          </div>
        </div>
      </Card>
    </section>
  );
}
