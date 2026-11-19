import { useState } from 'react';
import {
  Badge,
  Card,
  Link,
  MessageBar,
  MessageBarBody,
  Text,
  ToggleButton,
  makeStyles,
  shorthands,
  tokens
} from '@fluentui/react-components';
import {
  ArrowUp16Regular,
  Bot16Regular,
  BranchFork16Regular,
  BranchRequest20Regular,
  Checkmark16Regular,
  CheckmarkCircle16Regular,
  Circle16Regular,
  Clock16Regular,
  Dismiss16Regular,
  DismissCircle16Regular,
  LockClosed16Regular,
  Merge16Regular,
  Open16Regular,
  ShieldCheckmark16Regular,
  TicketDiagonal16Regular,
  Warning16Regular,
  WifiOff20Regular
} from '@fluentui/react-icons';
import { formatAbsoluteEn, formatRelativeTimeEn } from '../api/dashboardClient';
import type {
  LinkedIssueInfo,
  PipelineResponse,
  PullRequestInfo,
  TaskParticipantInfo
} from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';
import { ModelBadge, ProfileBadge, RoleBadge } from './RoleAndModelIcons';

const useStyles = makeStyles({
  card: {
    padding: '16px',
    gap: '14px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderTop: `3px solid ${tokens.colorBrandBackground}`
  },
  header: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '10px'
  },
  titleGroup: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px'
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  filterGroup: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '2px',
    padding: '2px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  prList: {
    display: 'flex',
    flexDirection: 'column'
  },
  prRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px',
    padding: '12px 0',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    ':first-child': {
      paddingTop: '2px',
      borderTop: 'none'
    },
    ':last-child': {
      paddingBottom: 0
    }
  },
  prTop: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: '10px'
  },
  prTitleWrap: {
    display: 'flex',
    alignItems: 'baseline',
    gap: '8px',
    minWidth: 0,
    flex: '1 1 320px'
  },
  prMetaRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '6px'
  },
  issuesRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '6px'
  },
  issuePillLink: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '5px',
    padding: '2px 8px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200,
    textDecorationLine: 'none',
    ':hover': {
      ...shorthands.borderColor(tokens.colorBrandStroke1)
    }
  },
  gatesRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '6px'
  },
  participantsBlock: {
    display: 'flex',
    flexDirection: 'column',
    gap: '6px'
  },
  participantsHeader: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2
  },
  participantsRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '6px'
  },
  participantPill: {
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
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    textAlign: 'center',
    gap: '6px',
    padding: '28px 16px',
    color: tokens.colorNeutralForeground2
  }
});

interface PipelineCardProps {
  pipeline: PipelineResponse;
  nowMs: number;
  isDark: boolean;
}

type PipelineViewMode = 'open' | 'merged';

export function PipelineCard({ pipeline, nowMs, isDark }: PipelineCardProps) {
  const styles = useStyles();
  const [viewMode, setViewMode] = useState<PipelineViewMode>('open');
  const activeList = viewMode === 'open' ? pipeline.pullRequests : pipeline.recentlyMerged;

  return (
    <Card className={styles.card} appearance="filled" aria-label="Task and PR Pipeline MOBAflow">
      <div className={styles.header}>
        <div className={styles.titleGroup}>
          <BranchRequest20Regular aria-hidden="true" />
          <h2 className={styles.title}>Task &amp; PR Pipeline</h2>
          <Badge appearance="tint" color="brand" shape="rounded" className="mono">
            Repository: {pipeline.repository}
          </Badge>
        </div>

        <div className={styles.filterGroup} role="group" aria-label="Filter PR status">
          <ToggleButton
            size="small"
            appearance={viewMode === 'open' ? 'primary' : 'subtle'}
            checked={viewMode === 'open'}
            onClick={() => setViewMode('open')}
          >
            <span className="tabular">Open ({pipeline.pullRequests.length})</span>
          </ToggleButton>
          <ToggleButton
            size="small"
            appearance={viewMode === 'merged' ? 'primary' : 'subtle'}
            checked={viewMode === 'merged'}
            onClick={() => setViewMode('merged')}
          >
            <span className="tabular">Recently Merged ({pipeline.recentlyMerged.length})</span>
          </ToggleButton>
        </div>
      </div>

      {pipeline.source.status === 'degraded' && (
        <MessageBar intent="warning">
          <MessageBarBody>
            {pipeline.source.notice ?? 'GitHub unreachable — showing last cached snapshot.'}{' '}
            <span className="tabular">({formatRelativeTimeEn(pipeline.source.updatedAtUtc, nowMs)})</span>
          </MessageBarBody>
        </MessageBar>
      )}

      {pipeline.source.status === 'unavailable' && (
        <div className={styles.emptyState}>
          <WifiOff20Regular aria-hidden="true" />
          <Text weight="semibold">GitHub Unreachable</Text>
          <Text size={200}>{pipeline.source.notice ?? 'Unable to load PR data right now.'}</Text>
        </div>
      )}

      {!pipeline.projectBoardAvailable && pipeline.projectBoardNotice && (
        <MessageBar intent="info">
          <MessageBarBody>{pipeline.projectBoardNotice}</MessageBarBody>
        </MessageBar>
      )}

      {pipeline.source.status !== 'unavailable' && activeList.length === 0 && (
        <div className={styles.emptyState}>
          <CheckmarkCircle16Regular style={{ width: 22, height: 22 }} aria-hidden="true" />
          <Text weight="semibold">
            {viewMode === 'open' ? 'No open PRs in MOBAflow' : 'No recently merged PRs found'}
          </Text>
          <Text size={200}>
            {viewMode === 'open'
              ? 'All changes are currently integrated on main.'
              : 'Merged tasks will appear here with their participating agents.'}
          </Text>
        </div>
      )}

      {activeList.length > 0 && (
        <div className={styles.prList}>
          {activeList.map((pr) => (
            <PullRequestRow
              key={`${pr.state}-${pr.number}`}
              pr={pr}
              nowMs={nowMs}
              isDark={isDark}
              styles={styles}
            />
          ))}
        </div>
      )}
    </Card>
  );
}

function PullRequestRow({
  pr,
  nowMs,
  isDark,
  styles
}: {
  pr: PullRequestInfo;
  nowMs: number;
  isDark: boolean;
  styles: ReturnType<typeof useStyles>;
}) {
  const isMerged = pr.state === 'MERGED';
  const timeStamp = isMerged ? (pr.mergedAtUtc ?? pr.updatedAtUtc) : pr.updatedAtUtc;

  return (
    <div className={styles.prRow}>
      <div className={styles.prTop}>
        <div className={styles.prTitleWrap}>
          <Link
            href={pr.url}
            target="_blank"
            rel="noreferrer"
            className="mono"
            title={`Open PR #${pr.number} on GitHub`}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 4,
              fontWeight: tokens.fontWeightSemibold,
              flexShrink: 0
            }}
          >
            #{pr.number} <Open16Regular style={{ width: 12, height: 12 }} aria-hidden="true" />
          </Link>
          <Text weight="semibold">{pr.title}</Text>
        </div>

        <div className={styles.prMetaRow}>
          <PrStateBadge stateLabel={pr.stateLabel} isMerged={isMerged} />
          {!isMerged && (
            <MergeStateBadge
              mergeState={pr.mergeState}
              ciStatus={pr.ciStatus}
              sonarStatus={pr.sonarStatus}
            />
          )}
          <RiskClassBadge riskClass={pr.riskClass} />
        </div>
      </div>

      {pr.linkedIssues && pr.linkedIssues.length > 0 && (
        <div className={styles.issuesRow}>
          <TicketDiagonal16Regular
            style={{ color: tokens.colorNeutralForeground2, flexShrink: 0 }}
            aria-hidden="true"
          />
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            Linked Issues:
          </Text>
          {pr.linkedIssues.map((issue) => (
            <LinkedIssuePill key={issue.number} issue={issue} className={styles.issuePillLink} />
          ))}
        </div>
      )}

      <div className={styles.prMetaRow}>
        <Badge
          appearance="outline"
          color="subtle"
          shape="rounded"
          className="mono"
          icon={<BranchFork16Regular />}
          title={pr.branch}
        >
          {pr.branch}
        </Badge>

        {!isMerged && (
          <>
            <CheckBadge label="CI" status={pr.ciStatus} />
            <CheckBadge label="Sonar" status={pr.sonarStatus} />
          </>
        )}

        {pr.projectStatus && (
          <Badge appearance="tint" color="brand" shape="rounded">
            Kanban: {pr.projectStatus}
          </Badge>
        )}

        <Text
          size={200}
          className="tabular"
          style={{ color: tokens.colorNeutralForeground2 }}
          title={formatAbsoluteEn(timeStamp)}
        >
          {isMerged ? 'Merged' : 'Updated'} {formatRelativeTimeEn(timeStamp, nowMs)}
        </Text>
      </div>

      <MergeGatesStrip pr={pr} isMerged={isMerged} styles={styles} />

      {pr.lockedAreas.length > 0 && (
        <div className={styles.prMetaRow}>
          <LockClosed16Regular
            style={{ width: 14, height: 14, color: tokens.colorNeutralForeground2 }}
            aria-hidden="true"
          />
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            Locked Areas:
          </Text>
          {pr.lockedAreas.map((area) => (
            <Badge
              key={area}
              appearance="tint"
              color={pr.riskClass === 'H' ? 'danger' : 'subtle'}
              shape="rounded"
              className="mono"
            >
              {area}
            </Badge>
          ))}
        </div>
      )}

      <div className={styles.participantsBlock}>
        <div className={styles.participantsHeader}>
          <Bot16Regular aria-hidden="true" />
          <span>Participating Agents, Roles &amp; Models</span>
        </div>

        {pr.participants.length === 0 ? (
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            No agent evidence recorded on this PR yet.
          </Text>
        ) : (
          <div className={styles.participantsRow}>
            {pr.participants.map((participant) => (
              <ParticipantPill
                key={`${participant.role}-${participant.tool}-${participant.profile}`}
                participant={participant}
                isDark={isDark}
                styles={styles}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

type GateState = 'pass' | 'pending' | 'fail' | 'todo';

interface GateStep {
  label: string;
  state: GateState;
}

function MergeGatesStrip({
  pr,
  isMerged,
  styles
}: {
  pr: PullRequestInfo;
  isMerged: boolean;
  styles: ReturnType<typeof useStyles>;
}) {
  const hasWriter = pr.participants.some((p) => p.role === 'Writer');
  const companions = pr.participants.filter((p) => p.role.includes('Companion'));
  const finalReviews = pr.participants.filter((p) => p.role === 'Final Reviewer');
  const crossFamily = pr.participants.filter((p) => p.role.includes('Cross-Family'));

  const companionState: GateState = companions.some(
    (p) => p.status === 'Blocked'
  )
    ? 'fail'
    : companions.some((p) => p.status === 'Findings' || p.status === 'Quota Limit')
      ? 'pending'
      : companions.some((p) => p.status === 'PASS' || p.status === 'Reviewed')
        ? 'pass'
        : 'todo';

  const ciSonarState: GateState =
    isMerged || (pr.ciStatus === 'pass' && pr.sonarStatus === 'pass')
      ? 'pass'
      : pr.ciStatus === 'fail' || pr.sonarStatus === 'fail'
        ? 'fail'
        : pr.ciStatus === 'pending' || pr.sonarStatus === 'pending'
          ? 'pending'
          : 'todo';

  const finalState: GateState = finalReviews.some((p) => p.status === 'PASS')
    ? 'pass'
    : finalReviews.some((p) => p.status === 'Blocked')
      ? 'fail'
      : finalReviews.length > 0
        ? 'pending'
        : 'todo';

  const crossState: GateState = crossFamily.some((p) => p.status === 'PASS')
    ? 'pass'
    : crossFamily.some((p) => p.status === 'Blocked')
      ? 'fail'
      : crossFamily.length > 0
        ? 'pending'
        : 'todo';

  const gates: GateStep[] = [
    { label: 'Writer', state: hasWriter ? 'pass' : 'todo' },
    ...(pr.riskClass === 'H' || pr.riskClass === 'M'
      ? [{ label: 'Companion', state: companionState }]
      : []),
    { label: 'CI & Sonar', state: ciSonarState },
    { label: 'P1 Final', state: finalState },
    { label: 'Cross-Family', state: crossState },
    { label: 'Auto-Merge', state: isMerged || pr.autoMergeEnabled ? 'pass' : 'todo' }
  ];

  const passedCount = gates.filter((g) => g.state === 'pass').length;

  return (
    <div className={styles.gatesRow} aria-label={`Merge gates ${passedCount} of ${gates.length}`}>
      <ShieldCheckmark16Regular
        style={{ color: tokens.colorNeutralForeground2, flexShrink: 0 }}
        aria-hidden="true"
      />
      <Text size={200} weight="semibold" style={{ color: tokens.colorNeutralForeground2 }}>
        <span className="tabular">
          Gates {passedCount}/{gates.length}:
        </span>
      </Text>
      {gates.map((gate) => (
        <Badge
          key={gate.label}
          appearance={gate.state === 'todo' ? 'outline' : 'tint'}
          color={
            gate.state === 'pass'
              ? 'success'
              : gate.state === 'pending'
                ? 'warning'
                : gate.state === 'fail'
                  ? 'danger'
                  : 'subtle'
          }
          shape="rounded"
          size="small"
          icon={
            gate.state === 'pass' ? (
              <CheckmarkCircle16Regular />
            ) : gate.state === 'pending' ? (
              <Clock16Regular />
            ) : gate.state === 'fail' ? (
              <DismissCircle16Regular />
            ) : (
              <Circle16Regular />
            )
          }
        >
          {gate.label}
        </Badge>
      ))}
    </div>
  );
}

function LinkedIssuePill({ issue, className }: { issue: LinkedIssueInfo; className: string }) {
  return (
    <Link
      href={issue.url}
      target="_blank"
      rel="noreferrer"
      className={className}
      title={`Open Issue #${issue.number} on GitHub`}
    >
      <span className="mono" style={{ fontWeight: tokens.fontWeightSemibold }}>
        Issue #{issue.number}
      </span>
      {issue.title && (
        <span
          style={{
            color: tokens.colorNeutralForeground1,
            maxWidth: 340,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap'
          }}
        >
          · {issue.title}
        </span>
      )}
      {issue.state === 'CLOSED' && (
        <Badge appearance="tint" color="subtle" shape="rounded" size="small">
          Closed
        </Badge>
      )}
      <Open16Regular style={{ width: 12, height: 12, flexShrink: 0 }} aria-hidden="true" />
    </Link>
  );
}

function ParticipantPill({
  participant,
  isDark,
  styles
}: {
  participant: TaskParticipantInfo;
  isDark: boolean;
  styles: ReturnType<typeof useStyles>;
}) {
  const statusColor: 'success' | 'warning' | 'danger' | 'brand' | 'subtle' =
    participant.status === 'PASS' ||
    participant.status === 'Approved' ||
    participant.status === 'Consulted · PASS'
      ? 'success'
      : participant.status === 'Findings' || participant.status === 'Quota Limit'
        ? 'warning'
        : participant.status === 'Blocked'
          ? 'danger'
          : participant.status === 'Implemented' || participant.status === 'Active'
            ? 'brand'
            : 'subtle';

  const summaryTitle = `${participant.agent} (${participant.tool} · ${participant.model}) · Evidence: ${participant.evidenceSource}`;

  return (
    <div
      className={styles.participantPill}
      title={summaryTitle}
      aria-label={`${participant.profile} ${participant.role} ${participant.tool} · ${participant.model} ${participant.status}`}
    >
      <GlossaryTooltip term={participant.profile}>
        <ProfileBadge profile={participant.profile} isDark={isDark} />
      </GlossaryTooltip>
      <RoleBadge role={participant.role} isDark={isDark} compact />
      <ModelBadge model={participant.model} isDark={isDark} mono />
      <Badge appearance="tint" color={statusColor} shape="rounded" size="small">
        {participant.status}
      </Badge>
    </div>
  );
}

function PrStateBadge({ stateLabel, isMerged }: { stateLabel: string; isMerged: boolean }) {
  if (isMerged || stateLabel === 'Merged') {
    return (
      <Badge appearance="tint" color="informative" shape="rounded" icon={<Merge16Regular />}>
        Merged
      </Badge>
    );
  }
  if (stateLabel === 'Auto-Merge') {
    return (
      <GlossaryTooltip term="Auto-Merge">
        <Badge appearance="tint" color="informative" shape="rounded">
          Auto-Merge
        </Badge>
      </GlossaryTooltip>
    );
  }
  if (stateLabel === 'Draft') {
    return (
      <Badge appearance="outline" color="subtle" shape="rounded">
        Draft
      </Badge>
    );
  }
  return (
    <Badge appearance="tint" color="brand" shape="rounded">
      Ready
    </Badge>
  );
}

function MergeStateBadge({
  mergeState,
  ciStatus,
  sonarStatus
}: {
  mergeState: string;
  ciStatus: string;
  sonarStatus: string;
}) {
  switch (mergeState.toUpperCase()) {
    case 'CLEAN':
      return (
        <GlossaryTooltip term="CLEAN">
          <Badge appearance="tint" color="success" shape="rounded" icon={<Checkmark16Regular />}>
            CLEAN
          </Badge>
        </GlossaryTooltip>
      );
    case 'BEHIND':
      return (
        <GlossaryTooltip term="BEHIND">
          <Badge appearance="tint" color="warning" shape="rounded" icon={<ArrowUp16Regular />}>
            BEHIND
          </Badge>
        </GlossaryTooltip>
      );
    case 'UNSTABLE': {
      const hasFailure = ciStatus === 'fail' || sonarStatus === 'fail';
      return (
        <GlossaryTooltip term="UNSTABLE">
          <Badge
            appearance="tint"
            color={hasFailure ? 'danger' : 'warning'}
            shape="rounded"
            icon={hasFailure ? <Warning16Regular /> : <Clock16Regular />}
          >
            {hasFailure ? 'Checks Failing' : 'Checks Pending'}
          </Badge>
        </GlossaryTooltip>
      );
    }
    case 'DIRTY':
    case 'BLOCKED':
      return (
        <Badge appearance="tint" color="danger" shape="rounded" icon={<Warning16Regular />}>
          {mergeState.toUpperCase()}
        </Badge>
      );
    default:
      return (
        <Badge appearance="tint" color="subtle" shape="rounded">
          {mergeState}
        </Badge>
      );
  }
}

function CheckBadge({ label, status }: { label: string; status: string }) {
  switch (status) {
    case 'pass':
      return (
        <Badge appearance="tint" color="success" shape="rounded" icon={<Checkmark16Regular />}>
          {label}
        </Badge>
      );
    case 'pending':
      return (
        <Badge appearance="tint" color="warning" shape="rounded" icon={<Clock16Regular />}>
          {label}
        </Badge>
      );
    case 'fail':
      return (
        <Badge appearance="tint" color="danger" shape="rounded" icon={<Dismiss16Regular />}>
          {label}
        </Badge>
      );
    default:
      return (
        <Badge appearance="tint" color="subtle" shape="rounded">
          {label}: –
        </Badge>
      );
  }
}

function RiskClassBadge({ riskClass }: { riskClass: string }) {
  if (riskClass === 'H') {
    return (
      <GlossaryTooltip term="H" showIcon>
        <Badge appearance="outline" color="danger" shape="rounded">
          Class H
        </Badge>
      </GlossaryTooltip>
    );
  }
  if (riskClass === 'M') {
    return (
      <GlossaryTooltip term="M" showIcon>
        <Badge appearance="outline" color="warning" shape="rounded">
          Class M
        </Badge>
      </GlossaryTooltip>
    );
  }
  if (riskClass === 'L') {
    return (
      <GlossaryTooltip term="L" showIcon>
        <Badge appearance="outline" color="success" shape="rounded">
          Class L
        </Badge>
      </GlossaryTooltip>
    );
  }
  return (
    <Badge appearance="outline" color="subtle" shape="rounded">
      Class ?
    </Badge>
  );
}
