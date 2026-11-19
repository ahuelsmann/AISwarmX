import {
  Badge,
  Card,
  MessageBar,
  MessageBarBody,
  Text,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import {
  CheckmarkCircle16Regular,
  Clock16Regular,
  Prohibited16Regular,
  Timer16Regular
} from '@fluentui/react-icons';
import { formatAbsoluteEn, formatRelativeTimeEn } from '../api/dashboardClient';
import type { MonitorInfo } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';
import { ModelBadge, ProfileBadge, RoleBadge } from './RoleAndModelIcons';

const useStyles = makeStyles({
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gap: '14px',
    '@media (max-width: 959px)': {
      gridTemplateColumns: 'minmax(0, 1fr)'
    }
  },
  card: {
    padding: '14px 16px',
    gap: '12px',
    justifyContent: 'space-between',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  cardTop: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px'
  },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    flexWrap: 'wrap'
  },
  titleGroup: {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    flexWrap: 'wrap'
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  subtitleRow: {
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    flexWrap: 'wrap'
  },
  slots: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px',
    paddingTop: '10px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`
  },
  slotRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    flexWrap: 'wrap'
  },
  slotLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2
  }
});

interface MonitorCardsProps {
  monitors: MonitorInfo[];
  nowMs: number;
  isDark: boolean;
}

export function MonitorCards({ monitors, nowMs, isDark }: MonitorCardsProps) {
  const styles = useStyles();

  return (
    <section aria-label="Live Monitor Status">
      <div className={styles.grid}>
        {monitors.map((monitor) => (
          <Card key={monitor.id} className={styles.card} appearance="filled">
            <div className={styles.cardTop}>
              <div className={styles.headerRow}>
                <div className={styles.titleGroup}>
                  <h2 className={styles.title}>{monitor.displayName}</h2>
                  <GlossaryTooltip term={monitor.profile} showIcon>
                    <ProfileBadge profile={monitor.profile} isDark={isDark} />
                  </GlossaryTooltip>
                </div>
                <MonitorStateBadge state={monitor.state} />
              </div>

              <div className={styles.subtitleRow}>
                <RoleBadge role={monitor.role} isDark={isDark} />
                <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                  · {monitor.tool}
                </Text>
              </div>

              {monitor.state === 'rate_limited' && monitor.rateLimitResetLabel && (
                <MessageBar intent="warning" layout="singleline">
                  <MessageBarBody className="tabular">
                    Quota Pause — Reset: {monitor.rateLimitResetLabel}
                  </MessageBarBody>
                </MessageBar>
              )}

              {monitor.source.status !== 'ok' && monitor.source.notice && (
                <MessageBar intent="warning" layout="multiline">
                  <MessageBarBody>{monitor.source.notice}</MessageBarBody>
                </MessageBar>
              )}
            </div>

            <div className={styles.slots}>
              <div className={styles.slotRow}>
                <span className={styles.slotLabel}>Active Model</span>
                <ModelBadge model={monitor.model} isDark={isDark} mono />
              </div>

              <div className={styles.slotRow}>
                <span className={styles.slotLabel}>Last Activity</span>
                <Text
                  size={200}
                  className="tabular"
                  title={formatAbsoluteEn(monitor.lastActivityUtc)}
                >
                  {formatRelativeTimeEn(monitor.lastActivityUtc, nowMs)}
                </Text>
              </div>

              {monitor.automationStatus && (
                <div className={styles.slotRow}>
                  <span className={styles.slotLabel}>Automation</span>
                  <Badge appearance="outline" color="subtle" shape="rounded" className="mono">
                    {monitor.automationStatus}
                  </Badge>
                </div>
              )}
            </div>
          </Card>
        ))}
      </div>
    </section>
  );
}

function MonitorStateBadge({ state }: { state: string }) {
  switch (state) {
    case 'busy':
      return (
        <Badge appearance="tint" color="brand" shape="rounded">
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
            <span className="pulse-dot" aria-hidden="true" />
            Active / Busy
          </span>
        </Badge>
      );
    case 'standby':
      return (
        <Badge appearance="tint" color="success" shape="rounded" icon={<CheckmarkCircle16Regular />}>
          Standby
        </Badge>
      );
    case 'rate_limited':
      return (
        <Badge appearance="tint" color="warning" shape="rounded" icon={<Timer16Regular />}>
          Quota Pause
        </Badge>
      );
    default:
      return (
        <Badge
          appearance="tint"
          color="subtle"
          shape="rounded"
          icon={state === 'offline' ? <Prohibited16Regular /> : <Clock16Regular />}
        >
          Offline / Unknown
        </Badge>
      );
  }
}
