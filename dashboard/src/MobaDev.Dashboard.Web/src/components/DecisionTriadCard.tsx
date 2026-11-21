import { Badge, Button, Card, Text, makeStyles, tokens } from '@fluentui/react-components';
import {
  ArrowRight16Regular,
  CheckmarkCircle16Regular,
  PeopleTeam16Regular,
  Timer16Regular,
  Warning16Regular
} from '@fluentui/react-icons';
import { getIdentityStyles, getStatusStyles } from '../theme/mobaFluentTheme';
import type { GovernanceResponse, MonitorInfo, PullRequestInfo } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';
import { IconGlyphBox, ModelBadge, ProfileBadge, RoleBadge } from './RoleAndModelIcons';

const useStyles = makeStyles({
  card: {
    padding: '9px 14px',
    gap: '8px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  headerRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px'
  },
  titleGroup: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px'
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  rightControls: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px'
  },
  chipsRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px'
  },
  seatPill: {
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
  seatStateDot: {
    width: '8px',
    height: '8px',
    borderRadius: tokens.borderRadiusCircular,
    display: 'inline-block',
    flexShrink: 0
  }
});

interface SeatDefinition {
  monitorId: string;
  profile: string;
  shortTag: string;
  roleMatch: string;
  tool: string;
  fallbackModel: string;
  mandate: string;
}

const COUNCIL_SEATS: SeatDefinition[] = [
  {
    monitorId: 'codex-monitor',
    profile: 'P2',
    shortTag: 'CPM & TD',
    roleMatch: 'Technical Director & CPM',
    tool: 'ChatGPT Codex',
    fallbackModel: 'Sol 6.2',
    mandate: 'Prioritizes backlog, validates build graph & holds scope/lock veto'
  },
  {
    monitorId: 'claude-monitor',
    profile: 'P1',
    shortTag: 'COO',
    roleMatch: 'Chief Operating Officer (COO)',
    tool: 'Claude Code',
    fallbackModel: 'Claude Opus 5.5',
    mandate: 'Cuts slices, assigns teams, grants Design Go & arms Auto-Merge'
  },
  {
    monitorId: 'antigravity-monitor',
    profile: 'P3',
    shortTag: 'AI Strategy',
    roleMatch: 'Head of AI Strategy',
    tool: 'Antigravity',
    fallbackModel: 'Orchestrator (Neon)',
    mandate: 'Routes models across hosts, aligns UI/UX & safeguards clarity'
  }
];

function formatMonitorStateText(state: string | undefined): string {
  switch (state) {
    case 'busy':
      return 'Active / Busy';
    case 'standby':
      return 'Standby';
    case 'rate_limited':
      return 'Quota Pause';
    default:
      return 'Offline';
  }
}

interface DecisionTriadCardProps {
  monitors: MonitorInfo[];
  governance: GovernanceResponse;
  pullRequests: PullRequestInfo[];
  isDark: boolean;
  onOpenMatrix: () => void;
}

export function DecisionTriadCard({
  monitors,
  governance,
  pullRequests,
  isDark,
  onOpenMatrix
}: DecisionTriadCardProps) {
  const styles = useStyles();
  const triadAccent = getIdentityStyles('triad', isDark);

  const triadDef = governance.roles.find((r) => r.role.toLowerCase().includes('triad'));
  const totalSeats = monitors.length > 0 ? monitors.length : 3;
  const readyCount = monitors.filter((m) => m.state === 'busy' || m.state === 'standby').length;
  const rateLimitedCount = monitors.filter((m) => m.state === 'rate_limited').length;

  const codeSliceCount = pullRequests.filter((p) => p.riskClass !== 'L').length;
  const docSliceCount = pullRequests.filter((p) => p.riskClass === 'L').length;
  const wipText = `Code WIP: ${codeSliceCount} / 3${docSliceCount > 0 ? ` (+${docSliceCount} Docs)` : ''}`;

  return (
    <Card
      className={styles.card}
      appearance="filled"
      aria-label="Decision and Steering Triad selection panel"
      style={{ borderLeft: `3px solid ${triadAccent.solid}` }}
    >
      <div className={styles.headerRow}>
        <div className={styles.titleGroup} title={triadDef?.responsibility}>
          <IconGlyphBox
            icon={<PeopleTeam16Regular aria-hidden="true" />}
            tone="purple"
            isDark={isDark}
            size="sm"
          />
          <h2 className={styles.title}>Decision &amp; Steering Triad</h2>
          <GlossaryTooltip term="Triad" iconOnly />
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            Selection Council
          </Text>
        </div>

        <div className={styles.rightControls}>
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
                Quorum {readyCount} / {totalSeats} · {rateLimitedCount} Quota Pause
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
            className={styles.seatPill}
            title="Human (Maintainer): Product goals, priorities, material UX, architecture & hardware authorizations"
          >
            <RoleBadge
              role="Human (maintainer)"
              label="Escalation: Human"
              isDark={isDark}
              compact
            />
          </div>

          <Button
            size="small"
            appearance="subtle"
            icon={<ArrowRight16Regular />}
            iconPosition="after"
            onClick={onOpenMatrix}
          >
            Selection Rules
          </Button>
        </div>
      </div>

      <div className={styles.chipsRow}>
        {COUNCIL_SEATS.map((seat) => {
          const monitor = monitors.find((m) => m.id === seat.monitorId);
          const stateLabel = formatMonitorStateText(monitor?.state);
          const toolName = monitor?.tool ?? seat.tool;
          const isBusy = monitor?.state === 'busy';
          const dotStatusKey =
            monitor?.state === 'standby'
              ? 'ok'
              : isBusy || monitor?.state === 'rate_limited'
                ? 'warn'
                : 'neutral';
          const dotColor = getStatusStyles(dotStatusKey, isDark).solid;

          return (
            <div
              key={seat.monitorId}
              className={styles.seatPill}
              title={`${seat.roleMatch} · ${toolName} · ${stateLabel} · ${seat.mandate}`}
            >
              <span
                className={isBusy ? 'pulse-dot' : styles.seatStateDot}
                aria-hidden="true"
                style={isBusy ? { color: dotColor } : { backgroundColor: dotColor }}
              />
              <GlossaryTooltip term={seat.profile}>
                <ProfileBadge profile={seat.profile} isDark={isDark} />
              </GlossaryTooltip>
              <RoleBadge role={seat.roleMatch} label={seat.shortTag} isDark={isDark} compact />
              <ModelBadge model={monitor?.model ?? seat.fallbackModel} isDark={isDark} mono />
            </div>
          );
        })}
      </div>
    </Card>
  );
}
