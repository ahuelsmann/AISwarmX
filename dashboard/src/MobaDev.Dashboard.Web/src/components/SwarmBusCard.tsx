import { Badge, Card, Text, makeStyles, tokens } from '@fluentui/react-components';
import { Chat16Regular, LockClosed16Regular } from '@fluentui/react-icons';
import type { SwarmBusResponse } from '../types/dashboard';

interface SwarmBusCardProps {
  swarmBus?: SwarmBusResponse | null;
  nowMs: number;
}

const useStyles = makeStyles({
  card: {
    padding: '14px 16px',
    gap: '10px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  header: {
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
  badges: {
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    flexWrap: 'wrap'
  },
  locksStrip: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '6px',
    padding: '6px 8px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  lockChip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '5px',
    padding: '2px 8px',
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200
  },
  feed: {
    display: 'flex',
    flexDirection: 'column',
    gap: '6px'
  },
  row: {
    display: 'flex',
    flexDirection: 'column',
    gap: '3px',
    padding: '8px 10px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  rowTop: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    flexWrap: 'wrap'
  },
  route: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    flexWrap: 'wrap',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  meta: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground2
  },
  body: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    wordBreak: 'break-word'
  },
  empty: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3
  }
});

function formatRelativeUtc(iso: string, nowMs: number): string {
  const ms = Date.parse(iso);
  if (Number.isNaN(ms)) return '';
  const diffSec = Math.max(0, Math.floor((nowMs - ms) / 1000));
  if (diffSec < 60) return `${diffSec}s ago`;
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHours = Math.floor(diffMin / 60);
  return `${diffHours}h ago`;
}

export function SwarmBusCard({ swarmBus, nowMs }: SwarmBusCardProps) {
  const styles = useStyles();

  if (!swarmBus) {
    return null;
  }

  const recent = swarmBus.recentMessages.slice(0, 6);

  return (
    <Card className={styles.card} aria-label="Swarm Bus Cross-Tool MCP Feed">
      <div className={styles.header}>
        <div className={styles.titleGroup}>
          <Chat16Regular aria-hidden="true" />
          <h3 className={styles.title}>Swarm Bus · Cross-Tool MCP</h3>
          <Badge appearance="tint" color="informative">
            Read-Only Feed
          </Badge>
        </div>
        <div className={styles.badges}>
          <Badge
            appearance="tint"
            color={swarmBus.unreadCount > 0 ? 'warning' : 'success'}
          >
            {swarmBus.unreadCount} Unread
          </Badge>
          <Badge
            appearance="tint"
            color={swarmBus.activeLockCount > 0 ? 'informative' : 'subtle'}
          >
            {swarmBus.activeLockCount} Locks
          </Badge>
        </div>
      </div>

      {swarmBus.activeLocks.length > 0 && (
        <div className={styles.locksStrip} aria-label="Active Cross-Tool Locks">
          {swarmBus.activeLocks.map((lock) => (
            <span key={lock.id} className={styles.lockChip}>
              <LockClosed16Regular aria-hidden="true" />
              <strong>{lock.agent}</strong>
              <span>{lock.pathPattern}</span>
              {lock.prNumber ? <span>PR #{lock.prNumber}</span> : null}
            </span>
          ))}
        </div>
      )}

      {recent.length === 0 ? (
        <Text className={styles.empty}>
          {swarmBus.source.notice ?? 'No cross-tool bus messages recorded yet.'}
        </Text>
      ) : (
        <div className={styles.feed}>
          {recent.map((msg) => (
            <div key={msg.id} className={styles.row}>
              <div className={styles.rowTop}>
                <span className={styles.route}>
                  <span>
                    {msg.sender} → {msg.recipient}
                  </span>
                  <Badge appearance="outline" color="informative">
                    {msg.topic}
                  </Badge>
                  {msg.prNumber ? (
                    <Badge appearance="tint" color="subtle">
                      PR #{msg.prNumber}
                    </Badge>
                  ) : null}
                </span>
                <span className={styles.meta}>
                  <Badge
                    appearance="tint"
                    color={msg.status === 'unread' ? 'warning' : 'success'}
                  >
                    {msg.status}
                  </Badge>
                  <span>{formatRelativeUtc(msg.createdAtUtc, nowMs)}</span>
                </span>
              </div>
              <Text className={styles.body}>{msg.body}</Text>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}
