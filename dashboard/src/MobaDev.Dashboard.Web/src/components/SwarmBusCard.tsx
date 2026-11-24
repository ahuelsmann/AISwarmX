import { useState } from 'react';
import { Badge, Button, Card, Text, makeStyles, tokens } from '@fluentui/react-components';
import { Chat16Regular, LockClosed16Regular } from '@fluentui/react-icons';
import type { SwarmBusResponse } from '../types/dashboard';

interface SwarmBusCardProps {
  swarmBus?: SwarmBusResponse | null;
  nowMs: number;
}

type StatusFilter = 'all' | 'unread' | 'acked';

const useStyles = makeStyles({
  page: {
    display: 'flex',
    flexDirection: 'column',
    gap: '14px'
  },
  card: {
    padding: '16px 18px',
    gap: '12px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '10px',
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
  filterBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    flexWrap: 'wrap'
  },
  filterButtons: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '4px'
  },
  locksStrip: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '6px',
    padding: '8px 10px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  lockChip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '5px',
    padding: '3px 8px',
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200
  },
  feed: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px'
  },
  row: {
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
    padding: '10px 12px',
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

function formatRemainingUtc(iso: string, nowMs: number): string {
  const ms = Date.parse(iso);
  if (Number.isNaN(ms)) return 'active';
  const remSec = Math.max(0, Math.floor((ms - nowMs) / 1000));
  if (remSec < 60) return 'in <1m';
  const remMin = Math.floor(remSec / 60);
  if (remMin < 60) return `in ${remMin}m`;
  const remHours = Math.floor(remMin / 60);
  const remMinutesRemainder = remMin % 60;
  return remMinutesRemainder > 0 ? `in ${remHours}h ${remMinutesRemainder}m` : `in ${remHours}h`;
}

export function SwarmBusCard({ swarmBus, nowMs }: SwarmBusCardProps) {
  const styles = useStyles();
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');

  if (!swarmBus) {
    return null;
  }

  const unreadTotal = swarmBus.unreadCount;
  const confirmedTotal = swarmBus.recentMessages.filter((m) => m.status === 'acked').length;
  const filteredMessages = swarmBus.recentMessages.filter((m) => {
    if (statusFilter === 'unread') return m.status === 'unread';
    if (statusFilter === 'acked') return m.status === 'acked';
    return true;
  });

  return (
    <section className={styles.page} aria-label="Swarm Bus Cross-Tool MCP Feed">
      <Card className={styles.card}>
        <div className={styles.header}>
          <div className={styles.titleGroup}>
            <Chat16Regular aria-hidden="true" />
            <h2 className={styles.title}>Swarm Bus · Cross-Tool MCP</h2>
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

        <div className={styles.locksStrip} aria-label="Active Cross-Tool Locks">
          {swarmBus.activeLocks.length === 0 ? (
            <Text className={styles.empty}>No active cross-tool path locks.</Text>
          ) : (
            swarmBus.activeLocks.map((lock) => (
              <span key={lock.id} className={styles.lockChip}>
                <LockClosed16Regular aria-hidden="true" />
                <strong>{lock.agent}</strong>
                <span>{lock.pathPattern}</span>
                {lock.prNumber ? <span>PR #{lock.prNumber}</span> : null}
                <span>(exp {formatRemainingUtc(lock.expiresAtUtc, nowMs)})</span>
              </span>
            ))
          )}
        </div>

        <div className={styles.filterBar}>
          <div className={styles.filterButtons} role="group" aria-label="Filter Swarm Bus messages">
            <Button
              size="small"
              appearance={statusFilter === 'all' ? 'primary' : 'subtle'}
              onClick={() => setStatusFilter('all')}
            >
              All ({swarmBus.recentMessages.length})
            </Button>
            <Button
              size="small"
              appearance={statusFilter === 'unread' ? 'primary' : 'subtle'}
              onClick={() => setStatusFilter('unread')}
            >
              Unread ({unreadTotal})
            </Button>
            <Button
              size="small"
              appearance={statusFilter === 'acked' ? 'primary' : 'subtle'}
              onClick={() => setStatusFilter('acked')}
            >
              Confirmed ({confirmedTotal})
            </Button>
          </div>
          <Text className={styles.empty}>
            Shared SQLite MCP channel across Claude Code, ChatGPT Codex &amp; Antigravity
          </Text>
        </div>

        {filteredMessages.length === 0 ? (
          <Text className={styles.empty}>
            {swarmBus.source.notice ?? 'No matching cross-tool bus messages.'}
          </Text>
        ) : (
          <div className={styles.feed}>
            {filteredMessages.map((msg) => (
              <div key={msg.id} className={styles.row}>
                <div className={styles.rowTop}>
                  <span className={styles.route}>
                    <span>
                      #{msg.id} · {msg.sender} → {msg.recipient}
                    </span>
                    <Badge appearance="outline" color="informative">
                      {msg.topic}
                    </Badge>
                    {msg.priority === 'high' || msg.priority === 'urgent' ? (
                      <Badge appearance="tint" color="warning">
                        {msg.priority}
                      </Badge>
                    ) : null}
                    {msg.prNumber ? (
                      <Badge appearance="tint" color="subtle">
                        PR #{msg.prNumber}
                      </Badge>
                    ) : null}
                    {msg.issueNumber ? (
                      <Badge appearance="tint" color="subtle">
                        #{msg.issueNumber}
                      </Badge>
                    ) : null}
                  </span>
                  <span className={styles.meta}>
                    <Badge
                      appearance="tint"
                      color={msg.status === 'unread' ? 'warning' : 'success'}
                    >
                      {msg.status === 'acked' ? 'confirmed' : msg.status}
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
    </section>
  );
}
