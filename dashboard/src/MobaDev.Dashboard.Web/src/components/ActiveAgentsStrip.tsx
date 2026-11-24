import { Badge, Card, Text, makeStyles, tokens } from '@fluentui/react-components';
import { Bot16Regular, PeopleTeam16Regular } from '@fluentui/react-icons';
import { getToneStyles, type StatusKey } from '../theme/mobaFluentTheme';
import type {
  ActiveAgentsSummary,
  MonitorsResponse,
  PipelineResponse,
  SubAgentModelCount
} from '../types/dashboard';
import { resolveModelVisual, resolveProfileTone } from './RoleAndModelIcons';
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

const CANONICAL_SUBAGENT_MODELS: ReadonlyArray<{ model: string; profile: string }> = [
  { model: 'GPT-6.2-sol', profile: 'P2' },
  { model: 'Claude Opus 5.5', profile: 'P1' },
  { model: 'Gemini 4 Carbon', profile: 'P3' },
  { model: 'Gemini 4 Hydrogen', profile: 'P4' },
  { model: 'Claude Sonnet 5.5', profile: 'P4' },
  { model: 'GPT-OSS 120B', profile: 'P2' }
];

export function normalizeSubAgentModel(
  rawModel: string | null | undefined,
  fallbackModel = 'Gemini 4 Carbon'
): { model: string; profile: string } {
  const trimmed = (rawModel ?? '').trim();
  const candidate =
    trimmed === '' ||
    /^codex-auto-review$/i.test(trimmed) ||
    /^<?synthetic>?$/i.test(trimmed)
      ? fallbackModel
      : trimmed;
  const lower = candidate.toLowerCase();

  if (lower.includes('opus')) {
    return { model: 'Claude Opus 5.5', profile: 'P1' };
  }
  if (lower.includes('sonnet') || lower.includes('fable')) {
    return { model: 'Claude Sonnet 5.5', profile: 'P4' };
  }
  if (lower.includes('claude')) {
    return { model: 'Claude Opus 5.5', profile: 'P1' };
  }
  if (lower.includes('hydrogen')) {
    return { model: 'Gemini 4 Hydrogen', profile: 'P4' };
  }
  if (
    lower.includes('argon') ||
    lower.includes('carbon') ||
    lower.includes('neon') ||
    lower.includes('gemini') ||
    lower.includes('antigravity')
  ) {
    return { model: 'Gemini 4 Carbon', profile: 'P3' };
  }
  if (lower.includes('gpt-oss') || lower.includes('120b')) {
    return { model: 'GPT-OSS 120B', profile: 'P2' };
  }
  if (lower.includes('astra')) {
    return { model: 'GPT-6.2-astra', profile: 'P2' };
  }
  if (lower.includes('gpt') || /\bsol\b/i.test(lower) || lower.includes('codex')) {
    return { model: 'GPT-6.2-sol', profile: 'P2' };
  }

  return { model: candidate, profile: 'P3' };
}

export function deriveSubAgentsByModel(monitorsResponse: MonitorsResponse): SubAgentModelCount[] {
  if (monitorsResponse.summary?.subAgentsByModel && monitorsResponse.summary.subAgentsByModel.length > 0) {
    return monitorsResponse.summary.subAgentsByModel;
  }

  const counts = new Map<string, number>();
  const profiles = new Map<string, string>();

  for (const entry of CANONICAL_SUBAGENT_MODELS) {
    counts.set(entry.model, 0);
    profiles.set(entry.model, entry.profile);
  }

  for (const monitor of monitorsResponse.monitors) {
    const byModel = monitor.activeSubAgentsByModel;
    if (byModel && Object.keys(byModel).length > 0) {
      for (const [rawKey, rawVal] of Object.entries(byModel)) {
        if (!rawVal || rawVal <= 0) {
          continue;
        }
        const norm = normalizeSubAgentModel(rawKey, monitor.model ?? 'Gemini 4 Carbon');
        counts.set(norm.model, (counts.get(norm.model) ?? 0) + rawVal);
        if (!profiles.has(norm.model)) {
          profiles.set(norm.model, norm.profile);
        }
      }
    } else if ((monitor.activeSubAgentCount ?? 0) > 0) {
      const fallback = /codex|gpt/i.test(monitor.tool)
        ? 'GPT-6.2-sol'
        : /claude/i.test(monitor.tool)
          ? 'Claude Opus 5.5'
          : 'Gemini 4 Carbon';
      const norm = normalizeSubAgentModel(monitor.model, fallback);
      counts.set(norm.model, (counts.get(norm.model) ?? 0) + (monitor.activeSubAgentCount ?? 0));
      if (!profiles.has(norm.model)) {
        profiles.set(norm.model, norm.profile);
      }
    }
  }

  const result: SubAgentModelCount[] = CANONICAL_SUBAGENT_MODELS.map((entry) => ({
    model: entry.model,
    profile: entry.profile,
    count: counts.get(entry.model) ?? 0
  }));

  for (const [model, count] of counts.entries()) {
    if (CANONICAL_SUBAGENT_MODELS.some((entry) => entry.model === model)) {
      continue;
    }
    result.push({
      model,
      profile: profiles.get(model) ?? 'P3',
      count
    });
  }

  return result;
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
    totalActiveAgents: standingMonitorsBusy + activeSubAgentsTotal,
    subAgentsByModel: deriveSubAgentsByModel(monitorsResponse)
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
  const subAgentsByModel = deriveSubAgentsByModel(monitorsResponse);

  const totalTone = resolveActiveSummaryTone(summary);

  const monitorTone =
    summary.standingMonitorsOnline === summary.standingMonitorsTotal && summary.standingMonitorsTotal > 0
      ? 'ok'
      : summary.standingMonitorsOnline > 0
        ? 'warn'
        : 'danger';

  const subTone = summary.activeSubAgentsTotal > 0 ? 'warn' : 'neutral';

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

          <div className={styles.rightGroup} aria-label="Live or recent sub-agents per model">
            <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
              Sub-agents (live/recent):
            </Text>
            {subAgentsByModel.map((entry) => {
              const subCount = entry.count;
              const modelVisual = resolveModelVisual(entry.model);
              const tone =
                modelVisual.tone !== 'neutral'
                  ? modelVisual.tone
                  : resolveProfileTone(entry.profile);
              const chipStyles = getToneStyles(tone, isDark);

              return (
                <div
                  key={entry.model}
                  className={styles.toolChip}
                  style={{
                    backgroundColor: chipStyles.bg,
                    border: `1px solid ${chipStyles.border}`,
                    color: chipStyles.fg,
                    opacity: subCount > 0 ? 1 : 0.48
                  }}
                  title={`${entry.model} (${entry.profile}, tone ${tone}): ${subCount} live/recent sub-agent(s)`}
                >
                  <span
                    className={styles.dot}
                    style={{ backgroundColor: chipStyles.solid }}
                    aria-hidden="true"
                  />
                  <span>{entry.model}:</span>
                  <Badge
                    appearance="outline"
                    shape="rounded"
                    size="small"
                    className="tabular"
                    style={{
                      borderColor: chipStyles.border,
                      color: chipStyles.fg
                    }}
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
