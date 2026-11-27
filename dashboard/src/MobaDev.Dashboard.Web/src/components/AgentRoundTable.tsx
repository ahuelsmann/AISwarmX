import { useId, useState, type ReactNode } from 'react';
import { Badge, Text, ToggleButton, makeStyles, tokens } from '@fluentui/react-components';
import { Bot16Regular } from '@fluentui/react-icons';
import {
  STATUS_TONES,
  getToneStyles,
  type AccentTone,
  type StatusToneKey,
  type ToneStylePair
} from '../theme/mobaFluentTheme';
import type { MonitorInfo, TaskParticipantInfo } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';
import { resolveProfileTone, resolveToolTone } from './RoleAndModelIcons';

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
    padding: '8px 10px'
  },
  inspectorFooter: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    paddingTop: '6px',
    marginTop: '6px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200
  }
});

interface AgentRoundTableProps {
  centerLabel: string;
  centerSub: string;
  centerStatus: string;
  centerTone: AccentTone;
  isTerminal?: boolean;
  participants: TaskParticipantInfo[];
  monitors?: readonly MonitorInfo[];
  isDark: boolean;
  defaultView?: 'table' | 'pills';
  renderPills: () => ReactNode;
}

type RoleIconKey = 'briefcase' | 'terminal' | 'pencil' | 'scales' | 'wrench' | 'sparkles' | 'bot';
type CardPlacementSide = 'top' | 'bottom' | 'left' | 'right';

interface PositionedSeat {
  key: string;
  participant: TaskParticipantInfo;
  isSteering: boolean;
  spokeToCenter: boolean;
  workflowStep: number | null;
  identityTone: AccentTone;
  angleDeg: number;
  rad: number;
  x: number;
  y: number;
  cardSide: CardPlacementSide;
  cardX: number;
  cardY: number;
  connectorX1: number;
  connectorY1: number;
  connectorX2: number;
  connectorY2: number;
  statusTone: StatusToneKey;
  statusDashed: boolean;
  iconKey: RoleIconKey;
}

const SVG_W = 760;
const CARD_W = 224;
const CARD_H = 54;
const R_TABLE = 54;
const R_ORBIT = 102;
const R_NODE = 23;
const TRIM_DIST = R_NODE + 7;

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

function resolveSeatIdentityTone(participant: TaskParticipantInfo): AccentTone {
  const profileTone = resolveProfileTone(participant.profile);
  if (profileTone !== 'neutral') {
    return profileTone;
  }
  return resolveToolTone(participant.tool);
}

function resolveParticipantStatusTone(status: string): { tone: StatusToneKey; dashed: boolean } {
  const normalized = status.trim().toLowerCase();
  if (/\bnot\s+(?:pass|approved)\b|\bblocked\b|\bfail(?:ed|ing)?\b|\berror\b/.test(normalized)) {
    return { tone: STATUS_TONES.danger, dashed: false };
  }
  if (/\b(?:pass|approved|reviewed)\b/.test(normalized)) {
    return { tone: STATUS_TONES.ok, dashed: false };
  }
  if (
    /\b(?:active|in\s+progress|working|reviewing|implemented|involved|findings|pending)\b/.test(normalized) ||
    /\bquota\b/.test(normalized)
  ) {
    return { tone: STATUS_TONES.warn, dashed: false };
  }
  return { tone: STATUS_TONES.neutral, dashed: true };
}

type ToolFamily = 'codex' | 'claude' | 'antigravity';

function normalizeToolFamily(toolOrModel: string | null | undefined): ToolFamily | null {
  const normalized = (toolOrModel ?? '').trim().toLowerCase();
  if (/codex|chatgpt|\bgpt\b|openai/.test(normalized)) {
    return 'codex';
  }
  if (/antigravity|gemini|google/.test(normalized)) {
    return 'antigravity';
  }
  if (/claude|anthropic/.test(normalized)) {
    return 'claude';
  }
  return null;
}

const MODEL_TOKENS = [
  'gpt-oss',
  'sol',
  'astra',
  'opus',
  'sonnet',
  'haiku',
  'carbon',
  'argon',
  'hydrogen',
  'claude',
  'gemini'
] as const;

function extractModelToken(model: string | null | undefined): string | null {
  const normalized = (model ?? '').trim().toLowerCase();
  for (const token of MODEL_TOKENS) {
    const matched =
      token === 'sol'
        ? /(?:^|[\s\-_/])sol(?:$|[\s\-_/0-9.])/.test(normalized)
        : normalized.includes(token);
    if (matched) {
      return token === 'argon' || token === 'carbon' ? 'gemini' : token;
    }
  }
  return null;
}

function isCompatibleStandingMonitor(
  monitor: MonitorInfo,
  participantProfile: string,
  participantModelToken: string | null
): boolean {
  const monitorProfile = (monitor.profile ?? '').trim().toLowerCase();
  const monitorModelToken = extractModelToken(monitor.model);

  if (participantProfile && monitorProfile && participantProfile !== monitorProfile) {
    return false;
  }
  if (participantModelToken && monitorModelToken && participantModelToken !== monitorModelToken) {
    return false;
  }
  if (!participantProfile && !participantModelToken) {
    return true;
  }
  return (
    (participantProfile !== '' && monitorProfile === participantProfile) ||
    (participantModelToken !== null && monitorModelToken === participantModelToken)
  );
}

/**
 * Returns true only when a Round Table seat is actively executing (e.g. `Active`/`In progress`,
 * not `Assigned`/`PASS`/`Merged`) AND either a compatible standing monitor reports `busy`
 * or an in-family monitor reports active sub-agents for that seat's model.
 */
export function isParticipantActivelyWorking(
  participant: TaskParticipantInfo,
  monitors?: readonly MonitorInfo[],
  centerStatus?: string,
  isTerminal?: boolean
): boolean {
  if (!monitors || monitors.length === 0 || isTerminal) {
    return false;
  }

  if (centerStatus && /\b(?:merged|closed|done)\b/i.test(centerStatus)) {
    return false;
  }

  const normalizedStatus = (participant.status ?? '').trim().toLowerCase();
  if (
    /\b(?:pass|approved|reviewed|assigned|blocked|fail(?:ed|ing)?|error|quota)\b/.test(
      normalizedStatus
    ) ||
    !/\b(?:active|in\s+progress|working|reviewing)\b/.test(normalizedStatus)
  ) {
    return false;
  }

  const participantFamily =
    normalizeToolFamily(participant.tool) ?? normalizeToolFamily(participant.model);
  if (!participantFamily) {
    return false;
  }

  const toolMatches = monitors.filter(
    (m) => normalizeToolFamily(m.tool) === participantFamily
  );
  if (toolMatches.length === 0) {
    return false;
  }

  const participantProfile = (participant.profile ?? '').trim().toLowerCase();
  const participantModelToken = extractModelToken(participant.model);

  const compatibleBusy = toolMatches.some(
    (m) =>
      m.state === 'busy' &&
      isCompatibleStandingMonitor(m, participantProfile, participantModelToken)
  );
  if (compatibleBusy) {
    return true;
  }

  if (!participantModelToken) {
    return false;
  }

  return toolMatches.some((m) => {
    const subAgents = m.activeSubAgentsByModel;
    if (!subAgents) {
      return false;
    }
    return Object.entries(subAgents).some(
      ([subModel, count]) =>
        count > 0 && extractModelToken(subModel) === participantModelToken
    );
  });
}

function shortRoleLabel(role: string): string {
  const lower = role.toLowerCase();
  if (lower.startsWith('coo')) return 'COO (Steering)';
  if (lower.includes('companion')) return 'Companion Review';
  if (lower.includes('cross-family') || lower.includes('verifier')) return 'Cross-Family';
  if (lower.includes('ui/ux') || lower.includes('product experience')) return 'Head of UI/UX';
  if (role.length > 20) return `${role.slice(0, 18).trimEnd()}…`;
  return role;
}

function shortToolLabel(tool: string): string {
  const lower = tool.toLowerCase();
  if (lower.includes('codex')) return 'Codex';
  if (lower.includes('claude')) return 'Claude';
  return tool;
}

function shortModelLabel(model: string): string {
  const cleaned = model.replace(/\s*\(Codex\)\s*/gi, '').trim();
  if (cleaned.length > 20) return `${cleaned.slice(0, 19).trimEnd()}…`;
  return cleaned;
}

function shortStatusLabel(status: string): string {
  if (status === 'Consulted · PASS') return 'PASS';
  return status;
}

function shortCenterSub(sub: string): string {
  if (sub.length > 21) return `${sub.slice(0, 19).trimEnd()}…`;
  return sub;
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

function renderCenterTable(
  cx: number,
  cy: number,
  rTable: number,
  centerLabel: string,
  centerSub: string,
  centerStatus: string,
  centerToneStyles: ToneStylePair
) {
  const sub = shortCenterSub(centerSub);
  const badgeW = Math.max(72, centerStatus.length * 5.8 + 14);
  return (
    <>
      <circle
        cx={cx}
        cy={cy}
        r={rTable + 7}
        fill="none"
        stroke={tokens.colorNeutralStroke2}
        strokeWidth={1}
        opacity={0.75}
      />
      <circle
        cx={cx}
        cy={cy}
        r={rTable}
        fill={tokens.colorNeutralBackground1}
        stroke={centerToneStyles.solid}
        strokeWidth={2.2}
      />
      <circle
        cx={cx}
        cy={cy}
        r={rTable - 4}
        fill="none"
        stroke={centerToneStyles.border}
        strokeWidth={0.9}
      />
      <text
        x={cx}
        y={cy - 13}
        textAnchor="middle"
        fill={tokens.colorNeutralForeground1}
        fontSize={13}
        fontWeight={700}
        fontFamily="var(--fontFamilyMonospace, monospace)"
      >
        {centerLabel}
      </text>
      <text
        x={cx}
        y={cy + 3}
        textAnchor="middle"
        fill={tokens.colorNeutralForeground2}
        fontSize={10.5}
        fontWeight={500}
      >
        {sub}
      </text>
      <rect
        x={cx - badgeW / 2}
        y={cy + 12}
        width={badgeW}
        height={18}
        rx={9}
        fill={centerToneStyles.bg}
        stroke={centerToneStyles.solid}
        strokeWidth={1}
      />
      <text
        x={cx}
        y={cy + 24}
        textAnchor="middle"
        fill={centerToneStyles.fg}
        fontSize={10.5}
        fontWeight={600}
      >
        {centerStatus}
      </text>
    </>
  );
}

function relaxVerticalSideCards(sideSeats: PositionedSeat[], viewHeight: number) {
  if (sideSeats.length === 0) return;

  const sorted = [...sideSeats].sort((a, b) => a.cardY - b.cardY);
  const minY = 8;
  const maxY = viewHeight - CARD_H - 8;
  const minGap = 8;
  const step = CARD_H + minGap;

  for (let i = 0; i < sorted.length; i++) {
    const floorY = i === 0 ? minY : sorted[i - 1].cardY + step;
    sorted[i].cardY = Math.max(sorted[i].cardY, floorY);
  }

  for (let i = sorted.length - 1; i >= 0; i--) {
    const ceilY = i === sorted.length - 1 ? maxY : sorted[i + 1].cardY - step;
    sorted[i].cardY = Math.min(sorted[i].cardY, ceilY);
  }
}

interface LayoutConfig {
  cx: number;
  cy: number;
  viewHeight: number;
  maxHeight: number;
  seats: PositionedSeat[];
}

function computeTableLayout(participants: TaskParticipantInfo[]): LayoutConfig {
  const ordered = [...participants]
    .map((p, origIndex) => ({ participant: p, origIndex, meta: roleSortWeight(p) }))
    .sort((a, b) => a.meta.weight - b.meta.weight || a.origIndex - b.origIndex);

  const count = ordered.length;
  const cx = count === 1 ? 236 : 380;
  const cy = count === 1 ? 88 : count === 2 ? 138 : count === 3 ? 196 : 198;
  const viewHeight = count === 1 ? 176 : count === 2 ? 250 : count === 3 ? 328 : 396;
  const maxHeight = count === 1 ? 176 : count === 2 ? 240 : count === 3 ? 310 : 368;

  let stepCounter = 0;

  const seats: PositionedSeat[] = ordered.map((entry, idx) => {
    const angleDeg =
      count === 1
        ? 0
        : count === 2
          ? idx === 0
            ? 180
            : 360
          : -90 + (360 * idx) / count;
    const rad = (angleDeg * Math.PI) / 180;
    const x = count === 1 ? 384 : cx + R_ORBIT * Math.cos(rad);
    const y = count === 1 ? cy : cy + R_ORBIT * Math.sin(rad);

    const { tone, dashed } = resolveParticipantStatusTone(entry.participant.status);
    const identityTone = resolveSeatIdentityTone(entry.participant);
    const workflowStep = entry.meta.isSteering ? null : ++stepCounter;
    const key = `${entry.participant.role}-${entry.participant.tool}-${entry.participant.profile}-${idx}`;

    let cardSide: CardPlacementSide;
    let cardX: number;
    let cardY: number;

    if (count === 1) {
      cardSide = 'right';
      cardX = 422;
      cardY = 61;
    } else if (count === 2) {
      if (Math.cos(rad) < 0) {
        cardSide = 'left';
        cardX = 17;
        cardY = cy - CARD_H / 2;
      } else {
        cardSide = 'right';
        cardX = 519;
        cardY = cy - CARD_H / 2;
      }
    } else if (Math.sin(rad) < -0.82) {
      cardSide = 'top';
      cardX = x - CARD_W / 2;
      cardY = Math.max(5, y - R_NODE - 12 - CARD_H);
    } else if (count >= 4 && Math.sin(rad) > 0.82) {
      cardSide = 'bottom';
      cardX = x - CARD_W / 2;
      cardY = Math.min(viewHeight - CARD_H - 5, y + R_NODE + 12);
    } else if (Math.cos(rad) >= 0) {
      cardSide = 'right';
      cardX = Math.max(x + R_NODE + 14, cx + R_ORBIT + 16);
      cardY = y - CARD_H / 2;
    } else {
      cardSide = 'left';
      cardX = Math.min(x - R_NODE - 14, cx - R_ORBIT - 16) - CARD_W;
      cardY = y - CARD_H / 2;
    }

    return {
      key,
      participant: entry.participant,
      isSteering: entry.meta.isSteering,
      spokeToCenter: count === 1 ? true : entry.meta.spokeToCenter,
      workflowStep,
      identityTone,
      angleDeg,
      rad,
      x,
      y,
      cardSide,
      cardX,
      cardY,
      connectorX1: x,
      connectorY1: y,
      connectorX2: x,
      connectorY2: y,
      statusTone: tone,
      statusDashed: dashed,
      iconKey: entry.meta.iconKey
    };
  });

  if (count >= 4) {
    relaxVerticalSideCards(
      seats.filter((s) => s.cardSide === 'left'),
      viewHeight
    );
    relaxVerticalSideCards(
      seats.filter((s) => s.cardSide === 'right'),
      viewHeight
    );
  }

  for (const seat of seats) {
    if (seat.cardSide === 'top') {
      seat.connectorX1 = seat.x;
      seat.connectorY1 = seat.y - R_NODE;
      seat.connectorX2 = seat.x;
      seat.connectorY2 = seat.cardY + CARD_H;
    } else if (seat.cardSide === 'bottom') {
      seat.connectorX1 = seat.x;
      seat.connectorY1 = seat.y + R_NODE;
      seat.connectorX2 = seat.x;
      seat.connectorY2 = seat.cardY;
    } else if (seat.cardSide === 'right') {
      seat.connectorX1 = seat.x + R_NODE;
      seat.connectorY1 = seat.y;
      seat.connectorX2 = seat.cardX;
      seat.connectorY2 = seat.cardY + CARD_H / 2;
    } else {
      seat.connectorX1 = seat.x - R_NODE;
      seat.connectorY1 = seat.y;
      seat.connectorX2 = seat.cardX + CARD_W;
      seat.connectorY2 = seat.cardY + CARD_H / 2;
    }
  }

  return { cx, cy, viewHeight, maxHeight, seats };
}

export function AgentRoundTable({
  centerLabel,
  centerSub,
  centerStatus,
  centerTone,
  isTerminal = false,
  participants,
  monitors,
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
  const centerToneStyles = getToneStyles(centerTone, isDark);

  if (participants.length === 0) {
    return (
      <div className={styles.root}>
        <div className={styles.headerStrip}>
          <span className={styles.leftMeta}>
            <Bot16Regular aria-hidden="true" />
            <span>Participating Agents</span>
            <Badge appearance="tint" color="subtle" shape="rounded" size="small" className="tabular">
              0 Agents
            </Badge>
          </span>

          <div className={styles.rightControls}>
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
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            No agent evidence recorded on this item yet.
          </Text>
        ) : (
          <div className={styles.svgContainer}>
            <svg
              viewBox={`0 0 ${SVG_W} 168`}
              style={{ width: '100%', height: 'auto', maxHeight: 168, display: 'block' }}
              role="img"
              aria-label={`Round table of 0 Agents for ${centerLabel}`}
            >
              <circle
                cx={250}
                cy={84}
                r={72}
                fill="none"
                stroke={tokens.colorNeutralStroke2}
                strokeWidth={1}
                strokeDasharray="4 4"
                opacity={0.75}
              />

              {renderCenterTable(250, 84, 52, centerLabel, centerSub, centerStatus, centerToneStyles)}

              <line
                x1={322}
                y1={84}
                x2={368}
                y2={84}
                stroke={tokens.colorNeutralStroke1}
                strokeWidth={1.2}
                strokeDasharray="4 4"
              />

              <rect
                x={368}
                y={57}
                width={244}
                height={54}
                rx={8}
                fill={tokens.colorNeutralBackground1}
                stroke={tokens.colorNeutralStroke1}
                strokeWidth={1}
              />
              <rect
                x={371.5}
                y={64}
                width={3.5}
                height={40}
                rx={1.75}
                fill={tokens.colorNeutralStroke1}
              />
              <text
                x={384}
                y={78}
                fill={tokens.colorNeutralForeground1}
                fontSize={11.5}
                fontWeight={600}
              >
                Awaiting Agent Assignment
              </text>
              <text
                x={384}
                y={96}
                fill={tokens.colorNeutralForeground2}
                fontSize={10}
              >
                No agent evidence recorded yet
              </text>
            </svg>
          </div>
        )}
      </div>
    );
  }

  const { cx, cy, viewHeight, maxHeight, seats } = computeTableLayout(participants);
  const workflowSeats = seats.filter((s) => !s.isSteering);
  const focusKey = hoveredKey ?? selectedKey;
  const focusedSeat = focusKey ? seats.find((s) => s.key === focusKey) : undefined;
  const deltaTheta = 2 * Math.asin(TRIM_DIST / (2 * R_ORBIT));

  return (
    <div className={styles.root}>
      <div className={styles.headerStrip}>
        <div className={styles.leftMeta}>
          <Bot16Regular aria-hidden="true" />
          <span>Participating Agents</span>
          <Badge appearance="tint" color="subtle" shape="rounded" size="small" className="tabular">
            {countLabel}
          </Badge>
          <span className={styles.avatarStack} aria-hidden="true">
            {seats.map((seat) => {
              const pStyles = getToneStyles(seat.identityTone, isDark);
              return (
                <span
                  key={seat.key}
                  className={styles.avatarDot}
                  title={`${seat.participant.profile} · ${seat.participant.role} · ${seat.participant.model} · ${seat.participant.status}`}
                  style={{
                    backgroundColor: tokens.colorNeutralBackground1,
                    borderColor: pStyles.solid,
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
            viewBox={`0 0 ${SVG_W} ${viewHeight}`}
            style={{ width: '100%', height: 'auto', maxHeight, display: 'block' }}
            role="img"
            aria-label={`Round table of ${countLabel} for ${centerLabel}`}
          >
            <defs>
              {(['green', 'gold', 'red', 'neutral'] as const).map((toneKey) => (
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
                        : getToneStyles(toneKey, isDark).solid
                    }
                  />
                </marker>
              ))}
            </defs>

            {/* Seating orbit guide for multi-agent tables */}
            {seats.length >= 2 && (
              <circle
                cx={cx}
                cy={cy}
                r={R_ORBIT}
                fill="none"
                stroke={tokens.colorNeutralStroke2}
                strokeWidth={1}
                strokeDasharray={showArrows ? '2 4' : undefined}
                opacity={showArrows ? 0.65 : 0.9}
              />
            )}

            {/* Sequential workflow arcs (Axis 2: Status only, never blue) */}
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
                const sx = cx + R_ORBIT * Math.cos(startRad);
                const sy = cy + R_ORBIT * Math.sin(startRad);
                const ex = cx + R_ORBIT * Math.cos(endRad);
                const ey = cy + R_ORBIT * Math.sin(endRad);
                const largeArc = span > Math.PI ? 1 : 0;

                const isRelated = !focusKey || focusKey === from.key || focusKey === to.key;
                const edgeToneKey: StatusToneKey =
                  to.statusTone === 'red' || to.statusTone === 'gold'
                    ? to.statusTone
                    : to.statusTone === 'neutral'
                      ? 'neutral'
                      : from.statusTone === 'green' && to.statusTone === 'green'
                        ? 'green'
                        : 'gold';
                const strokeColor =
                  edgeToneKey === 'neutral'
                    ? tokens.colorNeutralForeground3
                    : getToneStyles(edgeToneKey, isDark).solid;

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

            {/* Dashed steering/advisory spokes to center table (Axis 2: Status only) */}
            {showArrows &&
              seats
                .filter((s) => s.spokeToCenter)
                .map((seat) => {
                  const dist = Math.hypot(cx - seat.x, cy - seat.y);
                  if (dist <= R_TABLE + R_NODE + 12) {
                    return null;
                  }
                  const dx = (cx - seat.x) / dist;
                  const dy = (cy - seat.y) / dist;
                  const sx = seat.x + (R_NODE + 5) * dx;
                  const sy = seat.y + (R_NODE + 5) * dy;
                  const ex = cx - (R_TABLE + 6) * dx;
                  const ey = cy - (R_TABLE + 6) * dy;
                  const isRelated = !focusKey || focusKey === seat.key;
                  const strokeTone: StatusToneKey = seat.statusTone;
                  const strokeColor =
                    strokeTone === 'neutral'
                      ? tokens.colorNeutralForeground3
                      : getToneStyles(strokeTone, isDark).solid;

                  return (
                    <line
                      key={`spoke-${seat.key}`}
                      x1={sx.toFixed(1)}
                      y1={sy.toFixed(1)}
                      x2={ex.toFixed(1)}
                      y2={ey.toFixed(1)}
                      stroke={strokeColor}
                      strokeWidth={isRelated && focusKey ? 2.1 : 1.5}
                      strokeDasharray={seat.isSteering ? '4 3' : undefined}
                      markerEnd={`url(#${markerPrefix}-arrow-${strokeTone})`}
                      opacity={focusKey ? (isRelated ? 0.95 : 0.2) : 0.82}
                    />
                  );
                })}

            {/* Center Round Table */}
            {renderCenterTable(cx, cy, R_TABLE, centerLabel, centerSub, centerStatus, centerToneStyles)}

            {/* Agent Seats (Axis 1 Identity) + Top-Right Status Dot & Framed Visitenkarten */}
            {seats.map((seat) => {
              const isFocused = focusKey === seat.key;
              const isLiveWorking = isParticipantActivelyWorking(
                seat.participant,
                monitors,
                centerStatus,
                isTerminal
              );
              const statusColors = getToneStyles(seat.statusTone, isDark);
              const profileColors = getToneStyles(seat.identityTone, isDark);
              const statusStroke = seat.statusDashed
                ? tokens.colorNeutralStroke1
                : statusColors.solid;

              const statusText = shortStatusLabel(seat.participant.status);
              const pillW = Math.max(40, statusText.length * 5.6 + 14);
              const pillX = seat.cardX + CARD_W - 8 - pillW;
              const pillY = seat.cardY + 28;
              const pillBg = seat.statusDashed ? tokens.colorNeutralBackground3 : statusColors.bg;

              const stepX = seat.x - R_NODE * 0.72;
              const stepY = seat.y - R_NODE * 0.72;
              const statusDotX = seat.x + R_NODE * 0.72;
              const statusDotY = seat.y - R_NODE * 0.72;

              const toggleSelect = () => {
                setSelectedKey((prev) => (prev === seat.key ? null : seat.key));
              };

              return (
                <g
                  key={seat.key}
                  className="roundtable-seat"
                  data-live-working={isLiveWorking ? 'true' : 'false'}
                  style={{ cursor: 'pointer' }}
                  tabIndex={0}
                  role="button"
                  aria-label={`${seat.participant.profile} ${seat.participant.role} · ${seat.participant.model} · ${seat.participant.status}${isLiveWorking ? ' · Live working' : ''}`}
                  onMouseEnter={() => setHoveredKey(seat.key)}
                  onMouseLeave={() => setHoveredKey(null)}
                  onFocus={() => setHoveredKey(seat.key)}
                  onBlur={() => setHoveredKey((prev) => (prev === seat.key ? null : prev))}
                  onClick={toggleSelect}
                  onKeyDown={(ev) => {
                    if (ev.key === 'Enter' || ev.key === ' ') {
                      ev.preventDefault();
                      toggleSelect();
                    }
                  }}
                >
                  <title>{`${seat.participant.agent} (${seat.participant.tool} · ${seat.participant.model}) · Evidence: ${seat.participant.evidenceSource}${isLiveWorking ? ' · Live: working' : ''}`}</title>

                  {/* Connector stem between circular seat and Visitenkarte */}
                  <line
                    x1={seat.connectorX1.toFixed(1)}
                    y1={seat.connectorY1.toFixed(1)}
                    x2={seat.connectorX2.toFixed(1)}
                    y2={seat.connectorY2.toFixed(1)}
                    stroke={isFocused ? profileColors.solid : tokens.colorNeutralStroke2}
                    strokeWidth={isFocused ? 1.6 : 1.1}
                  />

                  {isFocused && (
                    <circle
                      cx={seat.x.toFixed(1)}
                      cy={seat.y.toFixed(1)}
                      r={R_NODE + 5}
                      fill="none"
                      stroke={profileColors.solid}
                      strokeWidth={2}
                      opacity={0.45}
                    />
                  )}

                  {isLiveWorking && (
                    <circle
                      className="roundtable-pulse-halo"
                      cx={seat.x.toFixed(1)}
                      cy={seat.y.toFixed(1)}
                      r={R_NODE + 1}
                      fill="none"
                      stroke={statusColors.solid}
                      strokeWidth={2.5}
                      aria-hidden="true"
                    />
                  )}

                  {/* Agent Seat Circle = WHO (AI Identity) */}
                  <circle
                    cx={seat.x.toFixed(1)}
                    cy={seat.y.toFixed(1)}
                    r={R_NODE}
                    fill={tokens.colorNeutralBackground1}
                  />
                  <circle
                    cx={seat.x.toFixed(1)}
                    cy={seat.y.toFixed(1)}
                    r={R_NODE}
                    fill={profileColors.bg}
                    stroke={profileColors.fg}
                    strokeWidth={3}
                  />

                  <g
                    transform={`translate(${seat.x.toFixed(1)}, ${(seat.y - 5).toFixed(1)})`}
                    style={{ color: profileColors.fg }}
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

                  {/* Top-left numbered workflow station badge (omitted on steering spokes) */}
                  {seat.workflowStep !== null && (
                    <g aria-hidden="true">
                      <circle
                        cx={stepX.toFixed(1)}
                        cy={stepY.toFixed(1)}
                        r={8}
                        fill={tokens.colorNeutralBackground1}
                        stroke={profileColors.fg}
                        strokeWidth={1.5}
                      />
                      <text
                        x={stepX.toFixed(1)}
                        y={(stepY + 3.2).toFixed(1)}
                        textAnchor="middle"
                        fill={profileColors.fg}
                        fontSize={9}
                        fontWeight={700}
                      >
                        {seat.workflowStep}
                      </text>
                    </g>
                  )}

                  {/* Top-right Axis-2 live status indicator dot */}
                  {isLiveWorking && (
                    <circle
                      className="roundtable-pulse-dot"
                      cx={statusDotX.toFixed(1)}
                      cy={statusDotY.toFixed(1)}
                      r={5.5}
                      fill="none"
                      stroke={statusColors.solid}
                      strokeWidth={1.6}
                      aria-hidden="true"
                    />
                  )}
                  <circle
                    cx={statusDotX.toFixed(1)}
                    cy={statusDotY.toFixed(1)}
                    r={5.5}
                    fill={seat.statusDashed ? tokens.colorNeutralBackground3 : statusColors.solid}
                    stroke={tokens.colorNeutralBackground1}
                    strokeWidth={2}
                  />
                  {seat.statusDashed && (
                    <circle
                      cx={statusDotX.toFixed(1)}
                      cy={statusDotY.toFixed(1)}
                      r={4.2}
                      fill="none"
                      stroke={tokens.colorNeutralStroke1}
                      strokeWidth={1.1}
                      strokeDasharray="2 1.5"
                    />
                  )}

                  {/* Framed Visitenkarte */}
                  <rect
                    x={seat.cardX.toFixed(1)}
                    y={seat.cardY.toFixed(1)}
                    width={CARD_W}
                    height={CARD_H}
                    rx={8}
                    fill={tokens.colorNeutralBackground1}
                    stroke={isFocused ? profileColors.solid : tokens.colorNeutralStroke1}
                    strokeWidth={isFocused ? 1.8 : 1}
                  />

                  {/* Left 4px Profile Accent Bar (Axis 1 Identity) */}
                  <rect
                    x={(seat.cardX + 3.5).toFixed(1)}
                    y={(seat.cardY + 7).toFixed(1)}
                    width={4}
                    height={CARD_H - 14}
                    rx={2}
                    fill={profileColors.fg}
                  />

                  {/* Row 1: Role + Tool */}
                  <text
                    x={(seat.cardX + 14).toFixed(1)}
                    y={(seat.cardY + 20).toFixed(1)}
                    fill={tokens.colorNeutralForeground1}
                    fontSize={11.5}
                    fontWeight={600}
                  >
                    {shortRoleLabel(seat.participant.role)}
                  </text>
                  <text
                    x={(seat.cardX + CARD_W - 10).toFixed(1)}
                    y={(seat.cardY + 20).toFixed(1)}
                    textAnchor="end"
                    fill={tokens.colorNeutralForeground3}
                    fontSize={9.5}
                    fontWeight={500}
                  >
                    {shortToolLabel(seat.participant.tool)}
                  </text>

                  {/* Row 2: Model + Framed Status Pill (Axis 2 Status) */}
                  <text
                    x={(seat.cardX + 14).toFixed(1)}
                    y={(seat.cardY + 40).toFixed(1)}
                    fill={tokens.colorNeutralForeground2}
                    fontSize={10}
                    fontFamily="var(--fontFamilyMonospace, monospace)"
                  >
                    {shortModelLabel(seat.participant.model)}
                  </text>

                  <rect
                    x={pillX.toFixed(1)}
                    y={pillY.toFixed(1)}
                    width={pillW.toFixed(1)}
                    height={17}
                    rx={8.5}
                    fill={pillBg}
                    stroke={statusStroke}
                    strokeWidth={1}
                    strokeDasharray={seat.statusDashed ? '3 2' : undefined}
                  />
                  <text
                    x={(pillX + pillW / 2).toFixed(1)}
                    y={(pillY + 11.8).toFixed(1)}
                    textAnchor="middle"
                    fill={seat.statusDashed ? tokens.colorNeutralForeground2 : statusColors.fg}
                    fontSize={9.5}
                    fontWeight={600}
                  >
                    {statusText}
                  </text>
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
                        color: getToneStyles(focusedSeat.identityTone, isDark).fg
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
                  Click or hover an agent card for full role, model, and PR evidence details.
                </Text>
                <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                  Circle &amp; bar = AI Identity · Dot &amp; pill = Status · 1→2→3 = Workflow · Dashed = Steering
                </Text>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
