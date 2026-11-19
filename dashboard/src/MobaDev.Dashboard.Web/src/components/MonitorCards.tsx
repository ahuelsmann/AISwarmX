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
import {
  formatAbsoluteEn,
  formatAutomationStatusEn,
  formatRelativeTimeEn
} from '../api/dashboardClient';
import type { MonitorInfo } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';
import { ModelBadge, ProfileBadge, RoleBadge } from './RoleAndModelIcons';

const useStyles = makeStyles({
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gap: '12px',
    '@media (max-width: 959px)': {
      gridTemplateColumns: 'minmax(0, 1fr)'
    }
  },
  card: {
    padding: '10px 14px',
    gap: '8px',
    justifyContent: 'space-between',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  topBand: {
    display: 'flex',
    flexDirection: 'column',
    gap: '6px'
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
    gap: '6px',
    flexWrap: 'wrap'
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  roleRow: {
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    flexWrap: 'wrap'
  },
  footerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    flexWrap: 'wrap',
    paddingTop: '8px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`
  },
  activityWrap: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '4px',
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
        {monitors.map((monitor) => {
          const automationLabel = formatAutomationStatusEn(monitor.automationStatus);

          return (
            <Card key={monitor.id} className={styles.card} appearance="filled">
              <div className={styles.topBand}>
                <div className={styles.headerRow}>
                  <div className={styles.titleGroup}>
                    <h2 className={styles.title}>{monitor.displayName}</h2>
                    <GlossaryTooltip term={monitor.profile}>
                      <ProfileBadge profile={monitor.profile} isDark={isDark} />
                    </GlossaryTooltip>
                  </div>
                  <MonitorStateBadge state={monitor.state} />
                </div>

                <div className={styles.roleRow}>
                  <RoleBadge role={monitor.role} isDark={isDark} compact />
                  <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                    · {monitor.tool}
                  </Text>
                  {automationLabel && (
                    <Badge
                      appearance="tint"
                      color="subtle"
                      shape="rounded"
                      size="small"
                      title={`Automation: ${monitor.automationStatus}`}
                    >
                      {automationLabel}
                    </Badge>
                  )}
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

              <div className={styles.footerRow}>
                <ModelBadge model={monitor.model} isDark={isDark} mono />

                <span
                  className={styles.activityWrap}
                  title={`Last Activity: ${formatAbsoluteEn(monitor.lastActivityUtc)}`}
                >
                  <Clock16Regular style={{ width: 13, height: 13 }} aria-hidden="true" />
                  <Text size={200} className="tabular">
                    {formatRelativeTimeEn(monitor.lastActivityUtc, nowMs)}
                  </Text>
                </span>
              </div>
            </Card>
          );
        })}
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
