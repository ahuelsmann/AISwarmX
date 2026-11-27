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
  ArrowRight16Regular,
  ArrowSwap16Regular,
  Bot16Regular,
  CheckmarkCircle16Regular,
  Clock16Regular,
  PeopleTeam16Regular,
  Prohibited16Regular,
  ReOrderDotsVertical16Regular,
  Timer16Regular,
  Warning16Regular
} from '@fluentui/react-icons';
import {
  formatAbsoluteEn,
  formatAutomationStatusEn,
  formatRelativeTimeEn
} from '../api/dashboardClient';
import { getIdentityStyles, getToneStyles } from '../theme/mobaFluentTheme';
import type {
  ActiveAgentsSummary,
  GovernanceResponse,
  MonitorInfo,
  PullRequestInfo
} from '../types/dashboard';
import { normalizeSubAgentModel } from './ActiveAgentsStrip';
import { GlossaryTooltip } from './GlossaryTooltip';
import {
  IconGlyphBox,
  ModelBadge,
  ProfileBadge,
  RoleBadge,
  resolveModelVisual,
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
  triadHeaderCard: {
    padding: '8px 14px',
    marginBottom: '10px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  triadHeaderRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px'
  },
  triadTitleGroup: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px'
  },
  triadTitle: {
    margin: 0,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  triadControls: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px'
  },
  escalationPill: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    padding: '3px 8px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    whiteSpace: 'nowrap',
    maxWidth: '100%'
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
  slotIdleEmpty: {
    '@media (max-width: 799px)': {
      minHeight: '0'
    }
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
    backgroundColor: 'transparent',
    color: tokens.colorNeutralForeground3,
    textAlign: 'center'
  },
  emptySlotActive: {
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
  subAgentsRow: {
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    flexWrap: 'wrap',
    paddingTop: '2px'
  },
  subAgentsLabel: {
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.03em'
  },
  subAgentChip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '5px',
    padding: '2px 7px',
    borderRadius: tokens.borderRadiusMedium,
    fontSize: tokens.fontSizeBase200,
    lineHeight: '16px'
  },
  subAgentDot: {
    width: '6px',
    height: '6px',
    borderRadius: '999px',
    flexShrink: 0
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

export function formatQuotaPauseSuffix(monitors: MonitorInfo[]): string {
  const isReady = (id: string) =>
    monitors.some(
      (m) => m.id === id && (m.state === 'busy' || m.state === 'idle' || m.state === 'standby')
    );

  const claudeReady = isReady('claude-monitor');
  const codexReady = isReady('codex-monitor');
  const geminiReady = isReady('antigravity-monitor');
  const agOpusReady = isReady('antigravity-opus-head');

  if (claudeReady && codexReady && geminiReady && !agOpusReady) {
    return '(Claude + Codex + Gemini Active)';
  }

  if (!claudeReady && codexReady && (geminiReady || agOpusReady)) {
    if (geminiReady && agOpusReady) {
      return '(Antigravity + Codex Failover)';
    }

    return geminiReady
      ? '(Codex + Gemini Failover)'
      : '(Antigravity Opus + Codex Failover)';
  }

  if (claudeReady && !codexReady && geminiReady && agOpusReady) {
    return '(Claude + Antigravity Active)';
  }

  const labelById: Record<string, string> = {
    'claude-monitor': 'Claude',
    'codex-monitor': 'Codex',
    'antigravity-monitor': 'Gemini',
    'antigravity-opus-head': 'Antigravity Opus'
  };

  const activeNames = monitors
    .filter((m) => m.state === 'busy' || m.state === 'idle' || m.state === 'standby')
    .map((m) => labelById[m.id] ?? m.displayName);

  return activeNames.length > 0 ? `(${activeNames.join(' + ')} Active)` : '(No Active Quorum)';
}

function resolveMonitorMandate(monitor: MonitorInfo): string {
  if (monitor.id === 'antigravity-opus-head' || /antigravity opus|opus head/i.test(monitor.displayName)) {
    return 'MOBAi Antigravity Opus (P1 Opus 5.5) · Acting COO & Sub-Agent Swarm Spawner';
  }
  if (monitor.id === 'antigravity-monitor' || /antigravity gemini|argon head/i.test(monitor.displayName)) {
    return 'MOBAi Antigravity Gemini (P3) · AI Strategy, UI/UX & Sub-Agent Swarm Spawner';
  }
  if (monitor.id === 'claude-monitor' || /claude/i.test(monitor.displayName)) {
    return 'MOBAi Claude Opus (P1 COO) · Kanban Single-Writer & Swarm Spawner';
  }
  if (monitor.id === 'codex-monitor' || /chatgpt|gpt head|codex/i.test(monitor.displayName)) {
    return 'MOBAi ChatGPT Codex (P2 TD & CPM) · Architecture Veto & Cross-Family Swarm Spawner';
  }
  return monitor.role;
}

interface SubAgentChipDescriptor {
  key: string;
  label: string;
  profile: string;
  count: number;
}

const CARD_SUBAGENT_LABELS: Record<string, string> = {
  'Claude Opus 5.5': 'Opus 5.5',
  'Claude Sonnet 5.5': 'Sonnet 5.5',
  'Claude Haiku 4.5': 'Haiku 4.5',
  'GPT-6.2-sol': 'GPT-6.2 Sol',
  'GPT-6.2-astra': 'GPT-6.2 Astra',
  'Gemini 4 Carbon': 'Carbon',
  'Gemini 4 Hydrogen': 'Hydrogen',
  'GPT-OSS 120B': 'GPT-OSS 120B'
};

function resolveCardSubAgentChips(monitor: MonitorInfo): SubAgentChipDescriptor[] {
  let basePool: ReadonlyArray<{ key: string; label: string; profile: string }>;
  switch (monitor.id) {
    case 'codex-monitor':
      basePool = [
        { key: 'GPT-6.2-sol', label: 'GPT-6.2 Sol', profile: 'P2' },
        { key: 'GPT-6.2-astra', label: 'GPT-6.2 Astra', profile: 'P2' }
      ];
      break;
    case 'claude-monitor':
      basePool = [
        { key: 'Claude Opus 5.5', label: 'Opus 5.5', profile: 'P1' },
        { key: 'Claude Sonnet 5.5', label: 'Sonnet 5.5', profile: 'P4' },
        { key: 'Claude Haiku 4.5', label: 'Haiku 4.5', profile: 'P4' }
      ];
      break;
    case 'antigravity-opus-head':
      basePool = [
        { key: 'Claude Opus 5.5', label: 'Opus 5.5', profile: 'P1' },
        { key: 'Claude Sonnet 5.5', label: 'Sonnet 5.5', profile: 'P4' }
      ];
      break;
    default:
      basePool = [
        { key: 'Gemini 4 Carbon', label: 'Carbon', profile: 'P3' },
        { key: 'Gemini 4 Hydrogen', label: 'Hydrogen', profile: 'P4' },
        { key: 'GPT-OSS 120B', label: 'GPT-OSS 120B', profile: 'P2' }
      ];
      break;
  }

  const counts = new Map<string, number>();
  const profiles = new Map<string, string>();
  for (const entry of basePool) {
    counts.set(entry.key, 0);
    profiles.set(entry.key, entry.profile);
  }

  let hasMappedEntries = false;
  if (monitor.activeSubAgentsByModel) {
    for (const [rawKey, rawCount] of Object.entries(monitor.activeSubAgentsByModel)) {
      if (!rawCount || rawCount <= 0) {
        continue;
      }
      hasMappedEntries = true;
      const norm = normalizeSubAgentModel(rawKey, basePool[0].key);
      counts.set(norm.model, (counts.get(norm.model) ?? 0) + rawCount);
      if (!profiles.has(norm.model)) {
        profiles.set(norm.model, norm.profile);
      }
    }
  }

  if (!hasMappedEntries && (monitor.activeSubAgentCount ?? 0) > 0) {
    counts.set(basePool[0].key, monitor.activeSubAgentCount ?? 0);
  }

  const chips: SubAgentChipDescriptor[] = basePool.map((entry) => ({
    key: entry.key,
    label: entry.label,
    profile: entry.profile,
    count: counts.get(entry.key) ?? 0
  }));

  for (const [modelKey, count] of counts.entries()) {
    if (count <= 0 || basePool.some((entry) => entry.key === modelKey)) {
      continue;
    }
    chips.push({
      key: modelKey,
      label: CARD_SUBAGENT_LABELS[modelKey] ?? modelKey,
      profile: profiles.get(modelKey) ?? 'P3',
      count
    });
  }

  return chips;
}

interface MonitorCardsProps {
  monitors: MonitorInfo[];
  monitorsSummary?: ActiveAgentsSummary | null;
  nowMs: number;
  isDark: boolean;
  governance?: GovernanceResponse;
  pullRequests?: PullRequestInfo[];
  onOpenMatrix?: () => void;
}

export function MonitorCards({
  monitors,
  monitorsSummary,
  nowMs,
  isDark,
  governance,
  pullRequests = [],
  onOpenMatrix
}: MonitorCardsProps) {
  const styles = useStyles();
  const triadAccent = getIdentityStyles('triad', isDark);
  const [savedSlotIds, setSavedSlotIds] = useState<(string | null)[] | null>(() =>
    readMonitorGridCookie()
  );
  const [draggedSlotIndex, setDraggedSlotIndex] = useState<number | null>(null);
  const [dragOverSlotIndex, setDragOverSlotIndex] = useState<number | null>(null);
  const [movingSlotIndex, setMovingSlotIndex] = useState<number | null>(null);

  const { slots, slotIds } = resolveMonitorGridSlots(monitors, savedSlotIds);
  const defaultSlotIds = resolveMonitorGridSlots(monitors, null).slotIds;
  const isCustomLayout = !slotsEqual(slotIds, defaultSlotIds);

  const triadDef = governance?.roles.find((r) => r.role.toLowerCase().includes('triad'));
  const totalSeats = monitors.length > 0 ? monitors.length : 4;
  const readyCount = monitors.filter(
    (m) => m.state === 'busy' || m.state === 'idle' || m.state === 'standby'
  ).length;
  const rateLimitedCount = monitors.filter((m) => m.state === 'rate_limited').length;
  const busyMonitorsCount = monitors.filter((m) => m.state === 'busy').length;
  const totalSubAgentsCount = monitors.reduce((acc, m) => acc + (m.activeSubAgentCount ?? 0), 0);
  const totalActiveAgents =
    monitorsSummary?.totalActiveAgents ?? busyMonitorsCount + totalSubAgentsCount;

  const codeSliceCount = pullRequests.filter((p) => p.riskClass !== 'L').length;
  const docSliceCount = pullRequests.filter((p) => p.riskClass === 'L').length;
  const wipText = `Code WIP: ${codeSliceCount} / 3${docSliceCount > 0 ? ` (+${docSliceCount} Docs)` : ''}`;

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
      <Card
        className={styles.triadHeaderCard}
        appearance="filled"
        aria-label="Decision and Steering Triad selection panel"
        style={{ borderLeft: `3px solid ${triadAccent.solid}` }}
      >
        <div className={styles.triadHeaderRow}>
          <div className={styles.triadTitleGroup} title={triadDef?.responsibility}>
            <IconGlyphBox
              icon={<PeopleTeam16Regular aria-hidden="true" />}
              tone="purple"
              isDark={isDark}
              size="sm"
            />
            <h3 className={styles.triadTitle}>Decision &amp; Steering Triad</h3>
            <GlossaryTooltip term="Triad" iconOnly />
            <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
              {movingSlotIndex !== null
                ? 'Select a target slot (2×3 grid) to place or swap the tile, or click the move icon again to cancel.'
                : 'Selection Council · 2×3 grid (drag or grip to arrange, saved in cookie)'}
            </Text>
          </div>

          <div className={styles.triadControls}>
            <Badge
              appearance="tint"
              color={totalActiveAgents > 0 ? 'warning' : 'subtle'}
              shape="rounded"
              title="Standing Busy Monitors plus live/recent runtime Sub-Agents"
              icon={
                totalActiveAgents > 0 ? (
                  <span className="pulse-dot" aria-hidden="true" />
                ) : (
                  <Bot16Regular aria-hidden="true" />
                )
              }
            >
              <span className="tabular">{totalActiveAgents} Busy / Recent</span>
            </Badge>

            {readyCount === totalSeats ? (
              <Badge
                appearance="tint"
                color="success"
                shape="rounded"
                icon={<CheckmarkCircle16Regular />}
              >
                <span className="tabular">
                  Quorum {readyCount} / {totalSeats} Ready
                </span>
              </Badge>
            ) : rateLimitedCount > 0 ? (
              <Badge appearance="tint" color="warning" shape="rounded" icon={<Timer16Regular />}>
                <span className="tabular">
                  Quorum {readyCount} / {totalSeats} · {rateLimitedCount} Quota Pause{' '}
                  {formatQuotaPauseSuffix(monitors)}
                </span>
              </Badge>
            ) : (
              <Badge appearance="tint" color="warning" shape="rounded" icon={<Warning16Regular />}>
                <span className="tabular">
                  Quorum {readyCount} / {totalSeats} Ready
                </span>
              </Badge>
            )}

            <GlossaryTooltip term="WIP">
              <Badge
                appearance="tint"
                color={codeSliceCount > 3 ? 'warning' : 'subtle'}
                shape="rounded"
              >
                <span className="tabular">{wipText}</span>
              </Badge>
            </GlossaryTooltip>

            <div
              className={styles.escalationPill}
              title="Human (Maintainer): Product goals, priorities, material UX, architecture & hardware authorizations"
            >
              <RoleBadge
                role="Human (maintainer)"
                label="Escalation: Human"
                isDark={isDark}
                compact
              />
            </div>

            {onOpenMatrix && (
              <Button
                size="small"
                appearance="subtle"
                icon={<ArrowRight16Regular />}
                iconPosition="after"
                onClick={onOpenMatrix}
              >
                Selection Rules
              </Button>
            )}

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
        </div>
      </Card>

      <div className={styles.grid}>
        {slots.map((monitor, slotIndex) => {
          const row = Math.floor(slotIndex / 3) + 1;
          const col = (slotIndex % 3) + 1;
          const isDropHovered =
            dragOverSlotIndex === slotIndex &&
            draggedSlotIndex !== null &&
            draggedSlotIndex !== slotIndex;
          const isMoveTarget = movingSlotIndex !== null && movingSlotIndex !== slotIndex;

          const isEmptyIdle = !monitor && !isMoveTarget && draggedSlotIndex === null;

          return (
            <div
              key={`slot-${slotIndex}`}
              data-testid={`monitor-slot-${slotIndex}`}
              aria-label={`Grid slot ${slotIndex + 1} (Row ${row}, Column ${col})`}
              className={mergeClasses(
                styles.slotWrapper,
                isEmptyIdle && styles.slotIdleEmpty,
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
                  const subAgentChips = resolveCardSubAgentChips(monitor);

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
                          <MonitorStateBadge
                            state={monitor.state}
                            lastActivityUtc={monitor.lastActivityUtc}
                            nowMs={nowMs}
                          />
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

                        <div
                          className={styles.subAgentsRow}
                          aria-label={`Sub-agents for ${monitor.displayName}`}
                        >
                          <span className={styles.subAgentsLabel}>Sub-agents</span>
                          {subAgentChips.map((chip) => {
                            const modelVisual = resolveModelVisual(chip.key);
                            const chipTone =
                              modelVisual.tone !== 'neutral'
                                ? modelVisual.tone
                                : resolveProfileTone(chip.profile);
                            const chipStyles = getToneStyles(chipTone, isDark);

                            return (
                              <div
                                key={chip.key}
                                className={styles.subAgentChip}
                                style={{
                                  backgroundColor: chipStyles.bg,
                                  border: `1px solid ${chipStyles.border}`,
                                  color: chipStyles.fg,
                                  opacity: chip.count > 0 ? 1 : 0.48
                                }}
                                title={`${chip.key} (${chip.profile}): ${chip.count} live/recent sub-agent(s)`}
                              >
                                <span
                                  className={styles.subAgentDot}
                                  style={{ backgroundColor: chipStyles.solid }}
                                  aria-hidden="true"
                                />
                                <span>{chip.label}:</span>
                                <span className="tabular" style={{ fontWeight: 600 }}>
                                  {chip.count}
                                </span>
                              </div>
                            );
                          })}
                        </div>
                      </div>

                      <div className={styles.footerRow}>
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
              ) : !isMoveTarget && draggedSlotIndex === null ? null : (
                <div
                  className={mergeClasses(
                    styles.emptySlot,
                    (isDropHovered || isMoveTarget) && styles.emptySlotActive
                  )}
                >
                  {isMoveTarget ? (
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
                  ) : (
                    <Text size={100}>Drop here</Text>
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

export function formatIdleStateLabel(
  lastActivityUtc: string | null | undefined,
  nowMs: number
): string {
  if (!lastActivityUtc) {
    return 'Idle · inactive';
  }

  const parsedMs = Date.parse(lastActivityUtc);
  if (Number.isNaN(parsedMs)) {
    return 'Idle · inactive';
  }

  const diffMinutes = Math.floor((nowMs - parsedMs) / 60_000);
  if (diffMinutes < 1) {
    return 'Idle · inactive';
  }

  return `Idle · no activity for ${diffMinutes} min`;
}

interface MonitorStateBadgeProps {
  state: string;
  lastActivityUtc?: string | null;
  nowMs: number;
}

function MonitorStateBadge({
  state,
  lastActivityUtc,
  nowMs
}: Readonly<MonitorStateBadgeProps>) {
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
    case 'idle':
      return (
        <Badge appearance="tint" color="subtle" shape="rounded" icon={<Clock16Regular />}>
          {formatIdleStateLabel(lastActivityUtc, nowMs)}
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
