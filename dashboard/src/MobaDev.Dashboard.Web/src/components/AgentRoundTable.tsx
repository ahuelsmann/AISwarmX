import { useId, useState, type ReactNode } from 'react';
import { Badge, Text, ToggleButton, makeStyles, tokens } from '@fluentui/react-components';
import { Bot16Regular } from '@fluentui/react-icons';
import { getToneStyles, type AccentTone } from '../theme/mobaFluentTheme';
import type { TaskParticipantInfo } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';
import { resolveProfileTone } from './RoleAndModelIcons';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px'
  },
  headerStrip: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px'
  },
  leftMeta: {
    display: 'inline-flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2
  },
  avatarStack: {
    display: 'inline-flex',
    alignItems: 'center'
  },
  avatarDot: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '20px',
    height: '20px',
    borderRadius: tokens.borderRadiusCircular,
    fontSize: '10px',
    fontWeight: tokens.fontWeightBold,
    marginLeft: '-6px',
    border: '2px solid',
    ':first-child': {
      marginLeft: 0
    }
  },
  rightControls: {
    display: 'inline-flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '6px'
  },
  modeToggleGroup: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '2px',
    padding: '2px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  svgContainer: {
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    padding: '6px 8px'
  },
  inspectorFooter: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    paddingTop: '6px',
    marginTop: '4px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200
  }
});

interface AgentRoundTableProps {
  centerLabel: string;
  centerSub: string;
  centerStatus: string;
  centerTone: AccentTone;
  participants: TaskParticipantInfo[];
  isDark: boolean;
  defaultView?: 'table' | 'pills';
  renderPills: () => ReactNode;
}

interface PositionedSeat {
  key: string;
  participant: TaskParticipantInfo;
  isSteering: boolean;
  spokeToCenter: boolean;
  angleDeg: number;
  rad: number;
  x: number;
  y: number;
  statusTone: AccentTone;
  statusDashed: boolean;
  iconKey: RoleIconKey;
}

type RoleIconKey = 'briefcase' | 'terminal' | 'pencil' | 'scales' | 'wrench' | 'sparkles' | 'bot';

const CX = 340;
const CY = 156;
const R_TABLE = 54;
const R_ORBIT = 112;
const R_NODE = 24;
const TRIM_DIST = 31;

function roleSortWeight(participant: TaskParticipantInfo): {
  weight: number;
  isSteering: boolean;
  spokeToCenter: boolean;
  iconKey: RoleIconKey;
} {
  const role = participant.role.toLowerCase();
  if (role.includes('coo') || role.includes('chief operating')) {
    return { weight: 0, isSteering: true, spokeToCenter: true, iconKey: 'briefcase' };
  }
  if (role.includes('ai strategy')) {
    return { weight: 1, isSteering: true, spokeToCenter: true, iconKey: 'sparkles' };
  }
  if (role.includes('ui/ux') || role.includes('ui ux') || role.includes('product experience')) {
    return { weight: 2, isSteering: false, spokeToCenter: true, iconKey: 'sparkles' };
  }
  if (role.includes('writer') || role.includes('senior developer')) {
    return { weight: 3, isSteering: false, spokeToCenter: false, iconKey: 'terminal' };
  }
  if (role.includes('companion') || role.includes('auto-review')) {
    return { weight: 4, isSteering: false, spokeToCenter: false, iconKey: 'pencil' };
  }
  if (role.includes('final review')) {
    return { weight: 5, isSteering: false, spokeToCenter: false, iconKey: 'scales' };
  }
  if (role.includes('cross-family') || role.includes('verifier')) {
    return { weight: 6, isSteering: false, spokeToCenter: false, iconKey: 'wrench' };
  }
  return { weight: 7, isSteering: false, spokeToCenter: false, iconKey: 'bot' };
}

function resolveParticipantStatusTone(status: string): { tone: AccentTone; dashed: boolean } {
  if (
    status === 'PASS' ||
    status === 'Approved' ||
    status === 'Consulted · PASS' ||
    status === 'Reviewed'
  ) {
    return { tone: 'green', dashed: false };
  }
  if (status === 'Findings' || status === 'Quota Limit') {
    return { tone: 'gold', dashed: false };
  }
  if (status === 'Blocked') {
    return { tone: 'red', dashed: false };
  }
  if (status === 'Implemented' || status === 'Active' || status === 'Involved') {
    return { tone: 'blue', dashed: false };
  }
  return { tone: 'neutral', dashed: true };
}

function shortRoleLabel(role: string): string {
  const lower = role.toLowerCase();
  if (lower.startsWith('coo')) return 'COO (Steering)';
  if (lower.includes('companion')) return 'Companion Review';
  if (lower.includes('cross-family') || lower.includes('verifier')) return 'Cross-Family Verifier';
  if (lower.includes('ui/ux') || lower.includes('product experience')) return 'Head of UI/UX';
  if (role.length > 24) return `${role.slice(0, 22).trimEnd()}…`;
  return role;
}

function shortModelLabel(model: string): string {
  return model.replace(/\s*\(Codex\)\s*/gi, '').trim();
}

function renderRoleGlyphSvg(iconKey: RoleIconKey) {
  switch (iconKey) {
    case 'briefcase':
      return (
        <>
          <rect x="-6" y="-4" width="12" height="9" rx="1.6" fill="none" stroke="currentColor" strokeWidth="1.5" />
          <path d="M-2.5,-4 V-6 H2.5 V-4 M-6,-0.5 H6" fill="none" stroke="currentColor" strokeWidth="1.3" />
        </>
      );
    case 'terminal':
      return (
        <>
          <rect x="-6" y="-5" width="12" height="10" rx="1.6" fill="none" stroke="currentColor" strokeWidth="1.5" />
          <path d="M-3.2,-1.5 L-0.8,0.5 L-3.2,2.5 M0.5,2.5 H3.6" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
        </>
      );
    case 'pencil':
      return (
        <path
          d="M-5,5 L-4,1.5 L2.5,-5 A1.5,1.5 0 0,1 4.6,-2.9 L-1.8,3.7 Z M1,-3.5 L3.2,-1.3"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.4"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      );
    case 'scales':
      return (
        <path
          d="M0,-6 V5 M-3.5,5 H3.5 M-5,-3.5 H5 M-5,-3.5 L-6.8,1.2 H-3.2 Z M5,-3.5 L3.2,1.2 H6.8 Z"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.35"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      );
    case 'wrench':
      return (
        <path
          d="M2,-5.5 A3.2,3.2 0 0,0 -1,-1.8 L-5.2,2.4 A1.4,1.4 0 0,0 -3.2,4.4 L1,0.2 A3.2,3.2 0 0,0 4.8,-2.8 L2.6,-1.2 L1.4,-2.4 Z"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.35"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      );
    case 'sparkles':
      return (
        <path
          d="M-1,-6 L0.2,-2.2 L4,-1 L0.2,0.2 L-1,4 L-2.2,0.2 L-6,-1 L-2.2,-2.2 Z M4,1.8 L4.6,3.4 L6.2,4 L4.6,4.6 L4,6.2 L3.4,4.6 L1.8,4 L3.4,3.4 Z"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.25"
          strokeLinejoin="round"
        />
      );
    default:
      return (
        <>
          <rect x="-5.5" y="-4" width="11" height="8.5" rx="2" fill="none" stroke="currentColor" strokeWidth="1.4" />
          <circle cx="-2" cy="0" r="1" fill="currentColor" />
          <circle cx="2" cy="0" r="1" fill="currentColor" />
          <path d="M0,-4 V-6.2" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
        </>
      );
  }
}

export function AgentRoundTable({
  centerLabel,
  centerSub,
  centerStatus,
  centerTone,
  participants,
  isDark,
  defaultView = 'table',
  renderPills
}: AgentRoundTableProps) {
  const styles = useStyles();
  const markerPrefix = useId().replace(/:/g, '');
  const [viewMode, setViewMode] = useState<'table' | 'pills'>(defaultView);
  const [showArrows, setShowArrows] = useState(true);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [hoveredKey, setHoveredKey] = useState<string | null>(null);

  const countLabel = participants.length === 1 ? '1 Agent' : `${participants.length} Agents`;

  if (participants.length === 0) {
    return (
      <div className={styles.root}>
        <div className={styles.headerStrip}>
          <span className={styles.leftMeta}>
            <Bot16Regular aria-hidden="true" />
            <span>Participating Agents</span>
            <Badge appearance="tint" color="subtle" shape="rounded" size="small">
              0 Agents
            </Badge>
          </span>
        </div>
        <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
          No agent evidence recorded on this item yet.
        </Text>
      </div>
    );
  }

  const ordered = [...participants]
    .map((p, origIndex) => ({ participant: p, origIndex, meta: roleSortWeight(p) }))
    .sort((a, b) => a.meta.weight - b.meta.weight || a.origIndex - b.origIndex);

  const seats: PositionedSeat[] = ordered.map((entry, idx) => {
    const angleDeg = ordered.length === 1 ? 0 : -90 + (360 * idx) / ordered.length;
    const rad = (angleDeg * Math.PI) / 180;
    const { tone, dashed } = resolveParticipantStatusTone(entry.participant.status);
    const key = `${entry.participant.role}-${entry.participant.tool}-${entry.participant.profile}-${idx}`;

    return {
      key,
      participant: entry.participant,
      isSteering: entry.meta.isSteering,
      spokeToCenter: ordered.length === 1 ? true : entry.meta.spokeToCenter,
      angleDeg,
      rad,
      x: CX + R_ORBIT * Math.cos(rad),
      y: CY + R_ORBIT * Math.sin(rad),
      statusTone: tone,
      statusDashed: dashed,
      iconKey: entry.meta.iconKey
    };
  });

  const workflowSeats = seats.filter((s) => !s.isSteering);
  const focusKey = hoveredKey ?? selectedKey;
  const focusedSeat = focusKey ? seats.find((s) => s.key === focusKey) : undefined;
  const centerToneStyles = getToneStyles(centerTone, isDark);

  const deltaTheta = 2 * Math.asin(TRIM_DIST / (2 * R_ORBIT));

  return (
    <div className={styles.root}>
      <div className={styles.headerStrip}>
        <div className={styles.leftMeta}>
          <Bot16Regular aria-hidden="true" />
          <span>Participating Agents</span>
          <Badge appearance="tint" color="brand" shape="rounded" size="small" className="tabular">
            {countLabel}
          </Badge>
          <span className={styles.avatarStack} aria-hidden="true">
            {seats.map((seat) => {
              const pStyles = getToneStyles(resolveProfileTone(seat.participant.profile), isDark);
              const sStyles = getToneStyles(seat.statusTone, isDark);
              return (
                <span
                  key={seat.key}
                  className={styles.avatarDot}
                  title={`${seat.participant.profile} · ${seat.participant.role} · ${seat.participant.model} · ${seat.participant.status}`}
                  style={{
                    backgroundColor: tokens.colorNeutralBackground1,
                    borderColor: sStyles.fg,
                    color: pStyles.fg
                  }}
                >
                  {seat.participant.profile}
                </span>
              );
            })}
          </span>
        </div>

        <div className={styles.rightControls}>
          {viewMode === 'table' && participants.length >= 2 && (
            <ToggleButton
              size="small"
              appearance={showArrows ? 'secondary' : 'subtle'}
              checked={showArrows}
              onClick={() => setShowArrows((prev) => !prev)}
            >
              {showArrows ? 'Arrows: On' : 'Arrows: Off'}
            </ToggleButton>
          )}

          <div className={styles.modeToggleGroup} role="group" aria-label={`Agent view for ${centerLabel}`}>
            <ToggleButton
              size="small"
              appearance={viewMode === 'table' ? 'primary' : 'subtle'}
              checked={viewMode === 'table'}
              onClick={() => setViewMode('table')}
            >
              Round Table
            </ToggleButton>
            <ToggleButton
              size="small"
              appearance={viewMode === 'pills' ? 'primary' : 'subtle'}
              checked={viewMode === 'pills'}
              onClick={() => setViewMode('pills')}
            >
              Pills
            </ToggleButton>
          </div>
        </div>
      </div>

      {viewMode === 'pills' ? (
        renderPills()
      ) : (
        <div className={styles.svgContainer}>
          <svg
            viewBox="0 0 680 310"
            style={{ width: '100%', height: 'auto', maxHeight: 280, display: 'block' }}
            role="img"
            aria-label={`Round table of ${countLabel} for ${centerLabel}`}
          >
            <defs>
              {(['green', 'blue', 'gold', 'red', 'neutral'] as const).map((toneKey) => (
                <marker
                  key={toneKey}
                  id={`${markerPrefix}-arrow-${toneKey}`}
                  viewBox="0 0 10 10"
                  refX="7"
                  refY="5"
                  markerWidth="6"
                  markerHeight="6"
                  orient="auto"
                >
                  <path
                    d="M 1 1.5 L 9 5 L 1 8.5 z"
                    fill={
                      toneKey === 'neutral'
                        ? tokens.colorNeutralForeground3
                        : getToneStyles(toneKey, isDark).fg
                    }
                  />
                </marker>
              ))}
            </defs>

            {/* Seating orbit guide */}
            <circle
              cx={CX}
              cy={CY}
              r={R_ORBIT}
              fill="none"
              stroke={tokens.colorNeutralStroke2}
              strokeWidth={1}
              strokeDasharray={showArrows ? '2 4' : undefined}
              opacity={showArrows ? 0.65 : 0.9}
            />

            {/* Sequential workflow arcs */}
            {showArrows &&
              workflowSeats.map((from, idx) => {
                if (idx >= workflowSeats.length - 1) {
                  return null;
                }
                const to = workflowSeats[idx + 1];
                let a1 = from.rad;
                let a2 = to.rad;
                while (a2 <= a1) {
                  a2 += Math.PI * 2;
                }
                const span = a2 - a1 - 2 * deltaTheta;
                if (span <= 0.05) {
                  return null;
                }

                const startRad = a1 + deltaTheta;
                const endRad = a2 - deltaTheta;
                const sx = CX + R_ORBIT * Math.cos(startRad);
                const sy = CY + R_ORBIT * Math.sin(startRad);
                const ex = CX + R_ORBIT * Math.cos(endRad);
                const ey = CY + R_ORBIT * Math.sin(endRad);
                const largeArc = span > Math.PI ? 1 : 0;

                const isRelated = !focusKey || focusKey === from.key || focusKey === to.key;
                const edgeToneKey =
                  to.statusTone === 'red' || to.statusTone === 'gold'
                    ? to.statusTone
                    : to.statusTone === 'neutral'
                      ? 'neutral'
                      : from.statusTone === 'green' && to.statusTone === 'green'
                        ? 'green'
                        : 'blue';
                const strokeColor =
                  edgeToneKey === 'neutral'
                    ? tokens.colorNeutralForeground3
                    : getToneStyles(edgeToneKey, isDark).fg;

                return (
                  <path
                    key={`edge-${from.key}-${to.key}`}
                    d={`M ${sx.toFixed(1)} ${sy.toFixed(1)} A ${R_ORBIT} ${R_ORBIT} 0 ${largeArc} 1 ${ex.toFixed(1)} ${ey.toFixed(1)}`}
                    fill="none"
                    stroke={strokeColor}
                    strokeWidth={isRelated && focusKey ? 2.4 : 1.8}
                    strokeLinecap="round"
                    strokeDasharray={to.statusDashed ? '4 4' : undefined}
                    markerEnd={`url(#${markerPrefix}-arrow-${edgeToneKey})`}
                    opacity={focusKey ? (isRelated ? 1 : 0.22) : to.statusDashed ? 0.68 : 0.9}
                  />
                );
              })}

            {/* Dashed steering/advisory spokes to center table */}
            {showArrows &&
              seats
                .filter((s) => s.spokeToCenter)
                .map((seat) => {
                  const dx = (CX - seat.x) / R_ORBIT;
                  const dy = (CY - seat.y) / R_ORBIT;
                  const sx = seat.x + 29 * dx;
                  const sy = seat.y + 29 * dy;
                  const ex = CX - (R_TABLE + 6) * dx;
                  const ey = CY - (R_TABLE + 6) * dy;
                  const isRelated = !focusKey || focusKey === seat.key;
                  const strokeTone =
                    seat.statusTone === 'neutral' ? 'neutral' : (seat.statusTone as 'green' | 'blue' | 'gold' | 'red');
                  const strokeColor =
                    strokeTone === 'neutral'
                      ? tokens.colorNeutralForeground3
                      : getToneStyles(strokeTone, isDark).fg;

                  return (
                    <line
                      key={`spoke-${seat.key}`}
                      x1={sx.toFixed(1)}
                      y1={sy.toFixed(1)}
                      x2={ex.toFixed(1)}
                      y2={ey.toFixed(1)}
                      stroke={strokeColor}
                      strokeWidth={isRelated && focusKey ? 2.1 : 1.5}
                      strokeDasharray="4 3"
                      markerEnd={`url(#${markerPrefix}-arrow-${strokeTone})`}
                      opacity={focusKey ? (isRelated ? 0.95 : 0.2) : 0.78}
                    />
                  );
                })}

            {/* Center Round Table */}
            <circle
              cx={CX}
              cy={CY}
              r={R_TABLE + 6}
              fill="none"
              stroke={tokens.colorNeutralStroke2}
              strokeWidth={1}
              opacity={0.75}
            />
            <circle
              cx={CX}
              cy={CY}
              r={R_TABLE}
              fill={tokens.colorNeutralBackground1}
              stroke={centerToneStyles.fg}
              strokeWidth={2}
            />
            <text
              x={CX}
              y={CY - 13}
              textAnchor="middle"
              fill={tokens.colorNeutralForeground1}
              fontSize={13}
              fontWeight={700}
              fontFamily="var(--fontFamilyMonospace, monospace)"
            >
              {centerLabel}
            </text>
            <text
              x={CX}
              y={CY + 3}
              textAnchor="middle"
              fill={tokens.colorNeutralForeground2}
              fontSize={10.5}
              fontWeight={500}
            >
              {centerSub}
            </text>
            {(() => {
              const badgeW = Math.max(72, centerStatus.length * 5.8 + 14);
              return (
                <>
                  <rect
                    x={CX - badgeW / 2}
                    y={CY + 12}
                    width={badgeW}
                    height={18}
                    rx={9}
                    fill={centerToneStyles.bg}
                    stroke={centerToneStyles.fg}
                    strokeWidth={1}
                  />
                  <text
                    x={CX}
                    y={CY + 24}
                    textAnchor="middle"
                    fill={centerToneStyles.fg}
                    fontSize={10.5}
                    fontWeight={600}
                  >
                    {centerStatus}
                  </text>
                </>
              );
            })()}

            {/* Agent Seats */}
            {seats.map((seat) => {
              const isFocused = focusKey === seat.key;
              const statusColors = getToneStyles(seat.statusTone, isDark);
              const profileColors = getToneStyles(
                resolveProfileTone(seat.participant.profile),
                isDark
              );
              const ringStroke = seat.statusDashed
                ? tokens.colorNeutralStroke1
                : statusColors.fg;
              const isLeft = seat.x < CX - 15;
              const labelX = isLeft ? seat.x - 32 : seat.x + 32;
              const anchor = isLeft ? 'end' : 'start';
              const dotX = isLeft ? labelX + 5 : labelX - 5;

              const toggleSelect = () => {
                setSelectedKey((prev) => (prev === seat.key ? null : seat.key));
              };

              return (
                <g
                  key={seat.key}
                  style={{ cursor: 'pointer' }}
                  tabIndex={0}
                  role="button"
                  aria-label={`${seat.participant.profile} ${seat.participant.role} · ${seat.participant.model} · ${seat.participant.status}`}
                  opacity={focusKey && !isFocused ? 0.55 : 1}
                  onMouseEnter={() => setHoveredKey(seat.key)}
                  onMouseLeave={() => setHoveredKey(null)}
                  onClick={toggleSelect}
                  onKeyDown={(ev) => {
                    if (ev.key === 'Enter' || ev.key === ' ') {
                      ev.preventDefault();
                      toggleSelect();
                    }
                  }}
                >
                  <title>{`${seat.participant.agent} (${seat.participant.tool} · ${seat.participant.model}) · Evidence: ${seat.participant.evidenceSource}`}</title>

                  {isFocused && (
                    <circle
                      cx={seat.x.toFixed(1)}
                      cy={seat.y.toFixed(1)}
                      r={R_NODE + 5}
                      fill="none"
                      stroke={ringStroke}
                      strokeWidth={2}
                      opacity={0.45}
                    />
                  )}

                  <circle
                    cx={seat.x.toFixed(1)}
                    cy={seat.y.toFixed(1)}
                    r={R_NODE}
                    fill={tokens.colorNeutralBackground1}
                    stroke={ringStroke}
                    strokeWidth={3}
                    strokeDasharray={seat.statusDashed ? '4 3' : undefined}
                  />

                  <g
                    transform={`translate(${seat.x.toFixed(1)}, ${(seat.y - 5).toFixed(1)})`}
                    style={{
                      color: seat.statusDashed
                        ? tokens.colorNeutralForeground2
                        : statusColors.fg
                    }}
                  >
                    {renderRoleGlyphSvg(seat.iconKey)}
                  </g>

                  <text
                    x={seat.x.toFixed(1)}
                    y={(seat.y + 15).toFixed(1)}
                    textAnchor="middle"
                    fill={profileColors.fg}
                    fontSize={10.5}
                    fontWeight={700}
                    fontFamily="var(--fontFamilyMonospace, monospace)"
                  >
                    {seat.participant.profile}
                  </text>

                  <text
                    x={labelX.toFixed(1)}
                    y={(seat.y - 3).toFixed(1)}
                    textAnchor={anchor}
                    fill={tokens.colorNeutralForeground1}
                    fontSize={12}
                    fontWeight={600}
                  >
                    {shortRoleLabel(seat.participant.role)}
                  </text>

                  <text
                    x={labelX.toFixed(1)}
                    y={(seat.y + 12).toFixed(1)}
                    textAnchor={anchor}
                    fill={tokens.colorNeutralForeground2}
                    fontSize={11}
                  >
                    {`${shortModelLabel(seat.participant.model)} · ${seat.participant.status}`}
                  </text>

                  <circle
                    cx={dotX.toFixed(1)}
                    cy={(seat.y + 8.5).toFixed(1)}
                    r={2.5}
                    fill={seat.statusDashed ? tokens.colorNeutralForeground3 : statusColors.fg}
                  />
                </g>
              );
            })}
          </svg>

          <div className={styles.inspectorFooter}>
            {focusedSeat ? (
              <>
                <div style={{ display: 'inline-flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
                  <GlossaryTooltip term={focusedSeat.participant.profile}>
                    <span
                      className="mono"
                      style={{
                        fontWeight: tokens.fontWeightBold,
                        color: getToneStyles(
                          resolveProfileTone(focusedSeat.participant.profile),
                          isDark
                        ).fg
                      }}
                    >
                      {focusedSeat.participant.profile}
                    </span>
                  </GlossaryTooltip>
                  <Text weight="semibold" size={200}>
                    {focusedSeat.participant.role}
                  </Text>
                  <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                    · {focusedSeat.participant.tool} ·{' '}
                    <span className="mono">{focusedSeat.participant.model}</span> ·{' '}
                    {focusedSeat.participant.status}
                  </Text>
                </div>
                <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                  Evidence: {focusedSeat.participant.evidenceSource}
                </Text>
              </>
            ) : (
              <>
                <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                  Click or hover an agent seat for full role, model, and PR evidence details.
                </Text>
                <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                  Solid ring = Workflow · Dashed spoke = Steering
                </Text>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
