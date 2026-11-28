import { Badge, Button, Card, Text, makeStyles, tokens } from '@fluentui/react-components';
import { Chat16Regular, Pulse20Regular } from '@fluentui/react-icons';
import { formatRelativeTimeEn } from '../api/dashboardClient';
import type { LiveConnectionState } from '../api/swarmHubClient';
import { getStatusStyles } from '../theme/mobaFluentTheme';
import type {
  MonitorInfo,
  SourceHealth,
  SwarmBusMessageInfo,
  SwarmBusResponse,
  WorkflowRunInfo
} from '../types/dashboard';
import { StatusBadge } from './shared/StatusBadge';

export interface ActivitySparklineBucket {
  index: number;
  busCount: number;
  ciCount: number;
  total: number;
}

const DEFAULT_BUCKET_COUNT = 24;
const DEFAULT_BUCKET_MINUTES = 15;
const FUTURE_CLOCK_SKEW_MS = 60_000;

export function buildActivitySparklineBuckets(
  messages: readonly SwarmBusMessageInfo[] | null | undefined,
  workflowRuns: readonly WorkflowRunInfo[] | null | undefined,
  nowMs: number,
  bucketCount = DEFAULT_BUCKET_COUNT,
  bucketMinutes = DEFAULT_BUCKET_MINUTES
): ActivitySparklineBucket[] {
  const safeCount = Math.max(1, bucketCount);
  const bucketMs = Math.max(1, bucketMinutes) * 60_000;
  const windowMs = safeCount * bucketMs;

  const buckets: ActivitySparklineBucket[] = Array.from({ length: safeCount }, (_, index) => ({
    index,
    busCount: 0,
    ciCount: 0,
    total: 0
  }));

  const resolveBucketIndex = (iso: string | null | undefined): number | null => {
    if (!iso || !iso.trim()) {
      return null;
    }
    const parsedMs = Date.parse(iso);
    if (!Number.isFinite(parsedMs)) {
      return null;
    }
    const ageMs = nowMs - parsedMs;
    if (ageMs < -FUTURE_CLOCK_SKEW_MS || ageMs >= windowMs) {
      return null;
    }
    if (ageMs < 0) {
      return safeCount - 1;
    }
    const offsetFromNewest = Math.min(safeCount - 1, Math.floor(ageMs / bucketMs));
    return safeCount - 1 - offsetFromNewest;
  };

  for (const msg of messages ?? []) {
    const idx = resolveBucketIndex(msg?.createdAtUtc);
    if (idx !== null) {
      buckets[idx].busCount += 1;
      buckets[idx].total += 1;
    }
  }

  for (const run of workflowRuns ?? []) {
    const idx = resolveBucketIndex(run?.updatedAtUtc ?? run?.createdAtUtc);
    if (idx !== null) {
      buckets[idx].ciCount += 1;
      buckets[idx].total += 1;
    }
  }

  return buckets;
}

const useStyles = makeStyles({
  card: {
    padding: '10px 14px',
    gap: '8px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  topRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '10px'
  },
  statusGroup: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px'
  },
  titleWrap: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  monitorCount: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2
  },
  sparklineGroup: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '8px'
  },
  sparklineCaption: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    whiteSpace: 'nowrap'
  },
  handoffsRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    paddingTop: '6px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`
  },
  handoffsList: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '6px',
    flex: '1 1 480px',
    minWidth: 0
  },
  handoffChip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    flexWrap: 'wrap',
    padding: '4px 8px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200,
    maxWidth: '100%'
  },
  handoffId: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2
  },
  handoffRoute: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  handoffTime: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground2
  },
  emptyText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3
  }
});

interface SwarmActivityPulseBarProps {
  swarmBus?: SwarmBusResponse | null;
  workflowRuns?: readonly WorkflowRunInfo[] | null;
  workflowRunsSource?: SourceHealth | null;
  monitors?: readonly MonitorInfo[] | null;
  liveConnectionState: LiveConnectionState;
  nowMs: number;
  isDark: boolean;
  onOpenCommunication: () => void;
}

function isSourceStaleOrDegraded(source?: SourceHealth | null): boolean {
  if (!source) {
    return false;
  }
  return (
    source.status === 'degraded' ||
    source.status === 'unavailable' ||
    Boolean(source.fromCache)
  );
}

export function SwarmActivityPulseBar({
  swarmBus,
  workflowRuns,
  workflowRunsSource,
  monitors,
  liveConnectionState,
  nowMs,
  isDark,
  onOpenCommunication
}: SwarmActivityPulseBarProps) {
  const styles = useStyles();
  const safeMonitors = monitors ?? [];
  const workingMonitors = safeMonitors.filter((m) => m.state === 'busy').length;
  const totalMonitors = safeMonitors.length;

  const busStale = isSourceStaleOrDegraded(swarmBus?.source);
  const ciStale = isSourceStaleOrDegraded(workflowRunsSource);
  const hasFeedWarning = busStale || ciStale;
  const isCachedOnly =
    hasFeedWarning &&
    (Boolean(swarmBus?.source?.fromCache) || Boolean(workflowRunsSource?.fromCache)) &&
    swarmBus?.source?.status !== 'unavailable' &&
    workflowRunsSource?.status !== 'unavailable';
  const feedWarningLabel = isCachedOnly ? 'Cached Feed' : 'Feed Degraded';
  const feedWarningNotice = [swarmBus?.source?.notice, workflowRunsSource?.notice]
    .filter((n): n is string => Boolean(n && n.trim()))
    .join(' · ');

  const connectionTone =
    liveConnectionState === 'live'
      ? 'ok'
      : liveConnectionState === 'reconnecting'
        ? 'warn'
        : 'neutral';
  const connectionLabel =
    liveConnectionState === 'live'
      ? 'Live Push'
      : liveConnectionState === 'reconnecting'
        ? 'Reconnecting…'
        : 'Polling';
  const dotColor = getStatusStyles(connectionTone, isDark).solid;
  const activeBarColor = getStatusStyles('ok', isDark).solid;

  const buckets = buildActivitySparklineBuckets(
    swarmBus?.recentMessages,
    workflowRuns,
    nowMs
  );
  const maxTotal = Math.max(1, ...buckets.map((b) => b.total));
  const totalEvents = buckets.reduce((acc, b) => acc + b.total, 0);
  const latestHandoffs = (swarmBus?.recentMessages ?? []).slice(0, 3);

  const svgWidth = 120;
  const svgHeight = 24;
  const barStep = svgWidth / buckets.length;
  const barWidth = 3.5;

  return (
    <section aria-label="Live Swarm Activity Pulse">
      <Card className={styles.card} appearance="filled">
        <div className={styles.topRow}>
          <div className={styles.statusGroup}>
            <span className={styles.titleWrap}>
              <Pulse20Regular style={{ width: 16, height: 16 }} aria-hidden="true" />
              <span>Swarm Activity Pulse</span>
            </span>
            <span
              className="pulse-dot"
              style={{ backgroundColor: dotColor }}
              aria-hidden="true"
            />
            <StatusBadge tone={connectionTone}>{connectionLabel}</StatusBadge>
            {hasFeedWarning && (
              <StatusBadge
                tone="warn"
                title={feedWarningNotice || 'SwarmBus or CI Actions feed is degraded or cached.'}
              >
                {feedWarningLabel}
              </StatusBadge>
            )}
            <Text className={`${styles.monitorCount} tabular`}>
              {workingMonitors}/{totalMonitors} working
            </Text>
          </div>

          <div className={styles.sparklineGroup}>
            <span
              className={`${styles.sparklineCaption} tabular`}
              title="Bounded recent feed window: up to 20 SwarmBus messages and 12 GitHub Actions runs over the last 6 hours."
            >
              Recent window ({totalEvents} · up to 20 bus / 12 CI in 6h)
            </span>
            <svg
              width={svgWidth}
              height={svgHeight}
              viewBox={`0 0 ${svgWidth} ${svgHeight}`}
              role="img"
              aria-label={`Recent activity sparkline over 6 hours: ${totalEvents} sampled events across 24 buckets (bounded window: up to 20 bus, 12 CI)`}
            >
              {buckets.map((bucket, idx) => {
                const height =
                  bucket.total === 0
                    ? 2
                    : Math.max(4, Math.round((bucket.total / maxTotal) * (svgHeight - 3)));
                const x = idx * barStep + (barStep - barWidth) / 2;
                const y = svgHeight - height;
                return (
                  <rect
                    key={bucket.index}
                    x={x.toFixed(1)}
                    y={y}
                    width={barWidth}
                    height={height}
                    rx={1}
                    fill={bucket.total > 0 ? activeBarColor : tokens.colorNeutralStroke2}
                  >
                    <title>
                      {`Bucket ${idx + 1}/24: ${bucket.busCount} bus, ${bucket.ciCount} CI`}
                    </title>
                  </rect>
                );
              })}
            </svg>
          </div>
        </div>

        <div className={styles.handoffsRow}>
          <div className={styles.handoffsList}>
            {latestHandoffs.length === 0 ? (
              <Text className={styles.emptyText}>No recent SwarmBus handoffs.</Text>
            ) : (
              latestHandoffs.map((msg) => (
                <div key={msg.id} className={styles.handoffChip}>
                  <span className={`${styles.handoffId} mono tabular`}>#{msg.id}</span>
                  {msg.workspaceId && msg.workspaceId !== 'default' && (
                    <Badge appearance="outline" color="subtle" size="small">
                      {msg.workspaceId}
                    </Badge>
                  )}
                  <span className={styles.handoffRoute}>
                    {msg.sender} → {msg.recipient}
                  </span>
                  <Badge appearance="outline" color="informative" size="small">
                    {msg.topic}
                  </Badge>
                  <span className={`${styles.handoffTime} tabular`}>
                    {formatRelativeTimeEn(msg.createdAtUtc, nowMs)}
                  </span>
                </div>
              ))
            )}
          </div>

          <Button
            size="small"
            appearance="subtle"
            icon={<Chat16Regular aria-hidden="true" />}
            onClick={onOpenCommunication}
          >
            Open Communication
          </Button>
        </div>
      </Card>
    </section>
  );
}
