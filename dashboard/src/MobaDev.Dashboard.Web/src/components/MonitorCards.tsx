import { useState } from 'react';
import {
  Badge,
  Button,
  Card,
  MessageBar,
  MessageBarBody,
  Text,
  makeStyles,
  mergeClasses,
  tokens
} from '@fluentui/react-components';
import {
  ArrowSwap16Regular,
  CheckmarkCircle16Regular,
  Clock16Regular,
  Prohibited16Regular,
  ReOrderDotsVertical16Regular,
  Timer16Regular
} from '@fluentui/react-icons';
import {
  formatAbsoluteEn,
  formatAutomationStatusEn,
  formatRelativeTimeEn
} from '../api/dashboardClient';
import { getToneStyles } from '../theme/mobaFluentTheme';
import type { MonitorInfo } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';
import {
  ModelBadge,
  ProfileBadge,
  RoleBadge,
  resolveProfileTone,
  resolveToolTone
} from './RoleAndModelIcons';

export const MONITOR_GRID_COOKIE = 'mobadev_monitor_grid_v1';
export const MONITOR_GRID_SLOT_COUNT = 6;
export const DEFAULT_MONITOR_SLOTS: (string | null)[] = [
  'codex-monitor',
  'claude-monitor',
  'antigravity-monitor',
  'antigravity-opus-head',
  null,
  null
];

const useStyles = makeStyles({
  toolbarRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    flexWrap: 'wrap',
    marginBottom: '6px'
  },
  toolbarHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200
  },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gridTemplateRows: 'repeat(2, minmax(108px, auto))',
    gap: '12px',
    '@media (max-width: 799px)': {
      gridTemplateColumns: 'minmax(0, 1fr)',
      gridTemplateRows: 'auto'
    }
  },
  slotWrapper: {
    display: 'flex',
    flexDirection: 'column',
    minHeight: '108px',
    borderRadius: tokens.borderRadiusMedium,
    transitionProperty: 'outline-color, background-color, opacity',
    transitionDuration: '120ms'
  },
  slotDropTarget: {
    outline: `2px dashed ${tokens.colorBrandStroke1}`,
    outlineOffset: '1px'
  },
  slotMoveReady: {
    cursor: 'pointer'
  },
  card: {
    flex: '1 1 auto',
    padding: '10px 14px',
    gap: '8px',
    justifyContent: 'space-between',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    cursor: 'grab'
  },
  cardDragging: {
    opacity: 0.55
  },
  emptySlot: {
    flex: '1 1 auto',
    minHeight: '108px',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '4px',
    padding: '10px 14px',
    borderRadius: tokens.borderRadiusMedium,
    border: `1px dashed ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    opacity: 0.72,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center'
  },
  emptySlotActive: {
    opacity: 1,
    border: `1px dashed ${tokens.colorBrandStroke1}`,
    color: tokens.colorNeutralForeground1
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
  moveButton: {
    minWidth: '24px',
    width: '24px',
    height: '24px',
    padding: '0',
    color: tokens.colorNeutralForeground3
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

const MONITOR_DISPLAY_ORDER = [
  'codex-monitor',
  'claude-monitor',
  'antigravity-monitor',
  'antigravity-opus-head'
];

export function readMonitorGridCookie(): (string | null)[] | null {
  try {
    if (typeof document === 'undefined' || !document.cookie) {
      return null;
    }

    const prefix = `${MONITOR_GRID_COOKIE}=`;
    const parts = document.cookie.split(';');
    for (const part of parts) {
      const trimmed = part.trim();
      if (!trimmed.startsWith(prefix)) {
        continue;
      }

      const rawValue = trimmed.slice(prefix.length);
      if (!rawValue) {
        return null;
      }

      const parsed: unknown = JSON.parse(decodeURIComponent(rawValue));
      if (!Array.isArray(parsed) || parsed.length !== MONITOR_GRID_SLOT_COUNT) {
        return null;
      }

      const valid = parsed.every(
        (entry) => entry === null || (typeof entry === 'string' && entry.length > 0)
      );
      return valid ? (parsed as (string | null)[]) : null;
    }
  } catch {
    // Ignore malformed cookies or restricted cookie access.
  }

  return null;
}

export function writeMonitorGridCookie(slots: (string | null)[]): void {
  try {
    if (typeof document === 'undefined') {
      return;
    }

    const normalized: (string | null)[] = Array.from(
      { length: MONITOR_GRID_SLOT_COUNT },
      (_, idx) => {
        const value = slots[idx];
        return typeof value === 'string' && value.length > 0 ? value : null;
      }
    );
    document.cookie = `${MONITOR_GRID_COOKIE}=${encodeURIComponent(JSON.stringify(normalized))}; path=/; max-age=31536000; SameSite=Lax`;
  } catch {
    // Ignore restricted cookie write errors.
  }
}

export function clearMonitorGridCookie(): void {
  try {
    if (typeof document === 'undefined') {
      return;
    }

    document.cookie = `${MONITOR_GRID_COOKIE}=; path=/; max-age=0; SameSite=Lax`;
  } catch {
    // Ignore restricted cookie write errors.
  }
}

function sortMonitors(monitors: MonitorInfo[]): MonitorInfo[] {
  return [...monitors].sort((a, b) => {
    const idxA = MONITOR_DISPLAY_ORDER.indexOf(a.id);
    const idxB = MONITOR_DISPLAY_ORDER.indexOf(b.id);
    const rankA = idxA === -1 ? Number.MAX_SAFE_INTEGER : idxA;
    const rankB = idxB === -1 ? Number.MAX_SAFE_INTEGER : idxB;
    return rankA - rankB;
  });
}

export function resolveMonitorGridSlots(
  monitors: MonitorInfo[],
  savedSlots: (string | null)[] | null
): { slots: (MonitorInfo | null)[]; slotIds: (string | null)[] } {
  const sorted = sortMonitors(monitors);
  const byId = new Map(sorted.map((monitor) => [monitor.id, monitor]));
  const slots: (MonitorInfo | null)[] = Array.from(
    { length: MONITOR_GRID_SLOT_COUNT },
    () => null
  );
  const placed = new Set<string>();
  const sourceOrder = savedSlots ?? DEFAULT_MONITOR_SLOTS;

  for (let i = 0; i < MONITOR_GRID_SLOT_COUNT; i += 1) {
    const id = sourceOrder[i];
    if (typeof id === 'string' && !placed.has(id)) {
      const monitor = byId.get(id);
      if (monitor) {
        slots[i] = monitor;
        placed.add(id);
      }
    }
  }

  for (const monitor of sorted) {
    if (placed.has(monitor.id)) {
      continue;
    }

    const freeIdx = slots.indexOf(null);
    if (freeIdx === -1) {
      break;
    }

    slots[freeIdx] = monitor;
    placed.add(monitor.id);
  }

  return {
    slots,
    slotIds: slots.map((monitor) => monitor?.id ?? null)
  };
}

function slotsEqual(left: (string | null)[], right: (string | null)[]): boolean {
  return (
    left.length === right.length && left.every((value, index) => value === right[index])
  );
}

function resolveMonitorMandate(monitor: MonitorInfo): string {
  if (monitor.id === 'antigravity-opus-head' || /opus head/i.test(monitor.displayName)) {
    return 'Master/Root Orchestrator (P1 Opus 5.5) · Acting COO & Sub-Agent Swarm Spawner';
  }
  if (monitor.id === 'antigravity-monitor' || /argon head/i.test(monitor.displayName)) {
    return 'Master/Root Orchestrator (P3 Argon) · AI Strategy, UI/UX & Sub-Agent Swarm Spawner';
  }
  if (monitor.id === 'claude-monitor' || /claude/i.test(monitor.displayName)) {
    return 'Master/Root COO (P1) · Kanban Single-Writer & Swarm Spawner';
  }
  if (monitor.id === 'codex-monitor' || /gpt head|codex/i.test(monitor.displayName)) {
    return 'Master/Root TD & CPM (P2) · Architecture Veto & Cross-Family Swarm Spawner';
  }
  return monitor.role;
}

interface MonitorCardsProps {
  monitors: MonitorInfo[];
  nowMs: number;
  isDark: boolean;
}

export function MonitorCards({ monitors, nowMs, isDark }: MonitorCardsProps) {
  const styles = useStyles();
  const [savedSlotIds, setSavedSlotIds] = useState<(string | null)[] | null>(() =>
    readMonitorGridCookie()
  );
  const [draggedSlotIndex, setDraggedSlotIndex] = useState<number | null>(null);
  const [dragOverSlotIndex, setDragOverSlotIndex] = useState<number | null>(null);
  const [movingSlotIndex, setMovingSlotIndex] = useState<number | null>(null);

  const { slots, slotIds } = resolveMonitorGridSlots(monitors, savedSlotIds);
  const defaultSlotIds = resolveMonitorGridSlots(monitors, null).slotIds;
  const isCustomLayout = !slotsEqual(slotIds, defaultSlotIds);

  const moveOrSwapSlot = (fromIndex: number, toIndex: number) => {
    if (
      fromIndex < 0 ||
      fromIndex >= MONITOR_GRID_SLOT_COUNT ||
      toIndex < 0 ||
      toIndex >= MONITOR_GRID_SLOT_COUNT
    ) {
      setDraggedSlotIndex(null);
      setDragOverSlotIndex(null);
      setMovingSlotIndex(null);
      return;
    }

    if (fromIndex !== toIndex) {
      const nextSlotIds = [...slotIds];
      const targetValue = nextSlotIds[toIndex];
      nextSlotIds[toIndex] = nextSlotIds[fromIndex];
      nextSlotIds[fromIndex] = targetValue;
      setSavedSlotIds(nextSlotIds);
      writeMonitorGridCookie(nextSlotIds);
    }

    setDraggedSlotIndex(null);
    setDragOverSlotIndex(null);
    setMovingSlotIndex(null);
  };

  const handleResetLayout = () => {
    setSavedSlotIds(null);
    clearMonitorGridCookie();
    setDraggedSlotIndex(null);
    setDragOverSlotIndex(null);
    setMovingSlotIndex(null);
  };

  return (
    <section aria-label="Live Monitor Status">
      <div className={styles.toolbarRow}>
        <Text className={styles.toolbarHint}>
          {movingSlotIndex !== null
            ? 'Select a target slot (2×3 grid) to place or swap the tile, or click the move icon again to cancel.'
            : '2×3 grid · Drag tiles or use the grip icon to arrange freely (saved in cookie)'}
        </Text>
        {isCustomLayout && (
          <Button
            appearance="subtle"
            size="small"
            icon={<ArrowSwap16Regular />}
            onClick={handleResetLayout}
          >
            Reset 2×3 layout
          </Button>
        )}
      </div>

      <div className={styles.grid}>
        {slots.map((monitor, slotIndex) => {
          const row = Math.floor(slotIndex / 3) + 1;
          const col = (slotIndex % 3) + 1;
          const isDropHovered =
            dragOverSlotIndex === slotIndex &&
            draggedSlotIndex !== null &&
            draggedSlotIndex !== slotIndex;
          const isMoveTarget = movingSlotIndex !== null && movingSlotIndex !== slotIndex;

          return (
            <div
              key={`slot-${slotIndex}`}
              data-testid={`monitor-slot-${slotIndex}`}
              aria-label={`Grid slot ${slotIndex + 1} (Row ${row}, Column ${col})`}
              className={mergeClasses(
                styles.slotWrapper,
                (isDropHovered || movingSlotIndex === slotIndex) && styles.slotDropTarget,
                isMoveTarget && styles.slotMoveReady
              )}
              onDragOver={(e) => {
                e.preventDefault();
                if (e.dataTransfer) {
                  e.dataTransfer.dropEffect = 'move';
                }
                if (dragOverSlotIndex !== slotIndex) {
                  setDragOverSlotIndex(slotIndex);
                }
              }}
              onDragEnter={(e) => {
                e.preventDefault();
                setDragOverSlotIndex(slotIndex);
              }}
              onDragLeave={() => {
                if (dragOverSlotIndex === slotIndex) {
                  setDragOverSlotIndex(null);
                }
              }}
              onDrop={(e) => {
                e.preventDefault();
                const raw = e.dataTransfer?.getData('text/plain');
                const parsed =
                  raw !== undefined && raw !== '' ? Number.parseInt(raw, 10) : Number.NaN;
                const fromIndex = Number.isInteger(parsed) ? parsed : draggedSlotIndex;
                if (fromIndex !== null && Number.isInteger(fromIndex)) {
                  moveOrSwapSlot(fromIndex, slotIndex);
                } else {
                  setDraggedSlotIndex(null);
                  setDragOverSlotIndex(null);
                }
              }}
              onClick={() => {
                if (movingSlotIndex !== null) {
                  moveOrSwapSlot(movingSlotIndex, slotIndex);
                }
              }}
            >
              {monitor ? (
                (() => {
                  const automationLabel = formatAutomationStatusEn(monitor.automationStatus);
                  const profileTone = resolveProfileTone(monitor.profile);
                  const identityTone =
                    profileTone !== 'neutral' ? profileTone : resolveToolTone(monitor.tool);
                  const identityStyles = getToneStyles(identityTone, isDark);

                  return (
                    <Card
                      className={mergeClasses(
                        styles.card,
                        draggedSlotIndex === slotIndex && styles.cardDragging
                      )}
                      appearance="filled"
                      draggable
                      title={resolveMonitorMandate(monitor)}
                      style={{ borderTop: `3px solid ${identityStyles.solid}` }}
                      onDragStart={(e) => {
                        setMovingSlotIndex(null);
                        setDraggedSlotIndex(slotIndex);
                        e.dataTransfer?.setData('text/plain', String(slotIndex));
                        if (e.dataTransfer) {
                          e.dataTransfer.effectAllowed = 'move';
                        }
                      }}
                      onDragEnd={() => {
                        setDraggedSlotIndex(null);
                        setDragOverSlotIndex(null);
                      }}
                    >
                      <div className={styles.topBand}>
                        <div className={styles.headerRow}>
                          <div className={styles.titleGroup}>
                            <Button
                              appearance={movingSlotIndex === slotIndex ? 'primary' : 'subtle'}
                              size="small"
                              className={styles.moveButton}
                              icon={<ReOrderDotsVertical16Regular />}
                              aria-label={`Move ${monitor.displayName}`}
                              aria-pressed={movingSlotIndex === slotIndex}
                              title="Drag tile or click to move to another 2×3 grid slot"
                              onClick={(e) => {
                                e.stopPropagation();
                                if (movingSlotIndex === slotIndex) {
                                  setMovingSlotIndex(null);
                                } else if (movingSlotIndex !== null) {
                                  moveOrSwapSlot(movingSlotIndex, slotIndex);
                                } else {
                                  setMovingSlotIndex(slotIndex);
                                }
                              }}
                            />
                            <h2 className={styles.title}>{monitor.displayName}</h2>
                            <GlossaryTooltip term={monitor.profile}>
                              <ProfileBadge profile={monitor.profile} isDark={isDark} />
                            </GlossaryTooltip>
                          </div>
                          <MonitorStateBadge state={monitor.state} />
                        </div>

                        <div className={styles.roleRow}>
                          <RoleBadge
                            role={monitor.role}
                            isDark={isDark}
                            compact
                            identityTone={identityTone}
                          />
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
                        <div
                          style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: 6,
                            flexWrap: 'wrap'
                          }}
                        >
                          <ModelBadge
                            model={monitor.model}
                            isDark={isDark}
                            mono
                            identityTone={identityTone}
                            ghosted={
                              monitor.state === 'rate_limited' ||
                              monitor.state === 'offline' ||
                              /^<?synthetic>?$/i.test((monitor.model ?? '').trim())
                            }
                          />
                          <Badge
                            appearance="tint"
                            shape="rounded"
                            size="small"
                            className="tabular"
                            style={{
                              backgroundColor: identityStyles.bg,
                              border: `1px solid ${identityStyles.border}`,
                              color: identityStyles.fg,
                              opacity: (monitor.activeSubAgentCount ?? 0) > 0 ? 1 : 0.48
                            }}
                            title="Active or recently updated sub-agents (Antigravity: RUNNING state; Codex: 3m window; Claude: busy sessions + 2m mtime)"
                          >
                            Sub-agents: {monitor.activeSubAgentCount ?? 0}
                          </Badge>
                        </div>

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
                })()
              ) : (
                <div
                  className={mergeClasses(
                    styles.emptySlot,
                    (isDropHovered || isMoveTarget) && styles.emptySlotActive
                  )}
                >
                  <Text size={200} weight="semibold">
                    Empty slot · Row {row}, Col {col}
                  </Text>
                  <Text size={100}>
                    {isMoveTarget || draggedSlotIndex !== null
                      ? 'Drop or click to place tile here'
                      : 'Free 2×3 slot'}
                  </Text>
                  {isMoveTarget && (
                    <Button
                      appearance="subtle"
                      size="small"
                      aria-label={`Place in grid slot ${slotIndex + 1}`}
                      onClick={(e) => {
                        e.stopPropagation();
                        if (movingSlotIndex !== null) {
                          moveOrSwapSlot(movingSlotIndex, slotIndex);
                        }
                      }}
                    >
                      Place here
                    </Button>
                  )}
                </div>
              )}
            </div>
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
        <Badge appearance="tint" color="warning" shape="rounded">
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
