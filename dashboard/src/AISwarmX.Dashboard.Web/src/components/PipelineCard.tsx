import { Fragment, useState } from 'react';
import {
  Badge,
  Card,
  Link,
  MessageBar,
  MessageBarBody,
  Text,
  ToggleButton,
  Tooltip,
  makeStyles,
  shorthands,
  tokens
} from '@fluentui/react-components';
import {
  ArrowUp16Regular,
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
import { getStatusStyles, type AccentTone } from '../theme/mobaFluentTheme';
import type {
  ActiveIssueInfo,
  LinkedIssueInfo,
  LinkedPullRequestRef,
  MonitorInfo,
  PipelineResponse,
  PullRequestInfo,
  SourceHealth,
  TaskParticipantInfo,
  WorkflowRunInfo
} from '../types/dashboard';
import { AgentRoundTable } from './AgentRoundTable';
import { GlossaryTooltip } from './GlossaryTooltip';
import { ContextChip } from './shared/ContextChip';
import { StatusBadge } from './shared/StatusBadge';

const useStyles = makeStyles({
  card: {
    padding: '12px 14px',
    gap: '10px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderTop: `3px solid ${tokens.colorNeutralStroke1}`
  },
  header: {
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
    flexDirection: 'column',
    gap: '12px'
  },
  prRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: '5px',
    padding: '10px 12px',
    borderRadius: tokens.borderRadiusLarge,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    boxShadow: tokens.shadow4
  },
  prTop: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: '6px'
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
    gap: '4px'
  },
  issuesRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '4px'
  },
  issuePillLink: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '5px',
    padding: '2px 8px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
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
    gap: '4px'
  },
  monoPill: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '5px',
    padding: '1px 6px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200,
    lineHeight: '16px',
    color: tokens.colorNeutralForeground1,
    maxWidth: '100%',
    whiteSpace: 'normal',
    wordBreak: 'break-word'
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
  },
  kanbanSummaryStrip: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    padding: '8px 10px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  kanbanCountsGroup: {
    display: 'inline-flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '6px'
  },
  chevronSep: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    userSelect: 'none'
  },
  stageBarTrack: {
    display: 'flex',
    gap: '2px',
    height: '4px',
    width: '100%',
    borderRadius: tokens.borderRadiusCircular,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground3,
    marginTop: '-6px'
  },
  kanbanGroupHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    marginTop: '2px',
    paddingTop: '6px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`
  },
  kanbanGroupTitle: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: 'uppercase',
    letterSpacing: '0.03em'
  },
  workflowRunsStrip: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px',
    padding: '10px 12px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  workflowRunsHeader: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px'
  },
  workflowRunsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '6px'
  },
  workflowRunRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    paddingTop: '6px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`
  },
  workflowRunLeft: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '8px',
    minWidth: 0,
    flex: '1 1 260px'
  },
  workflowRunTitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    maxWidth: 'min(340px, 100%)',
    display: 'inline-flex',
    alignItems: 'center',
    gap: '4px'
  }
});

interface PipelineCardProps {
  pipeline: PipelineResponse;
  monitors?: readonly MonitorInfo[];
  nowMs: number;
  isDark: boolean;
}

type PipelineViewMode = 'open' | 'issues';

const KANBAN_GROUPS = ['Ready', 'In progress', 'In review', 'Via Open PR'] as const;

export function PipelineCard({ pipeline, monitors, nowMs, isDark }: PipelineCardProps) {
  const styles = useStyles();
  const [viewMode, setViewMode] = useState<PipelineViewMode>('issues');

  const allKanbanItems = pipeline.activeIssues ?? [];
  const inReviewItems = allKanbanItems.filter((i) => i.kanbanStatus.toLowerCase() === 'in review');
  const inProgressItems = allKanbanItems.filter((i) => i.kanbanStatus.toLowerCase() === 'in progress');
  const readyItems = allKanbanItems.filter((i) => i.kanbanStatus.toLowerCase() === 'ready');
  const fallbackItems = allKanbanItems.filter(
    (i) => !['in review', 'in progress', 'ready'].includes(i.kanbanStatus.toLowerCase())
  );

  const columnGroups: { label: string; items: ActiveIssueInfo[] }[] = [
    { label: 'Ready', items: readyItems },
    { label: 'In progress', items: inProgressItems },
    { label: 'In review', items: inReviewItems },
    ...(fallbackItems.length > 0 ? [{ label: 'Via Open PR', items: fallbackItems }] : [])
  ].filter((g) => g.items.length > 0);

  const activePrList = pipeline.pullRequests;
  const hasPullRequests = activePrList.length > 0;
  const effectiveViewMode: PipelineViewMode = hasPullRequests ? viewMode : 'issues';

  return (
    <Card className={styles.card} appearance="filled" aria-label="Task and PR Pipeline MOBAflow">
      <div className={styles.header}>
        <div className={styles.titleGroup}>
          <BranchRequest20Regular aria-hidden="true" />
          <h2 className={styles.title}>Task &amp; PR Pipeline</h2>
          <ContextChip mono>Repository: {pipeline.repository}</ContextChip>
        </div>

        {hasPullRequests ? (
          <div className={styles.filterGroup} role="group" aria-label="Filter pipeline view">
            <ToggleButton
              size="small"
              appearance={effectiveViewMode === 'issues' ? 'primary' : 'subtle'}
              checked={effectiveViewMode === 'issues'}
              onClick={() => setViewMode('issues')}
            >
              <span className="tabular">Open Issues ({allKanbanItems.length})</span>
            </ToggleButton>
            <ToggleButton
              size="small"
              appearance={effectiveViewMode === 'open' ? 'primary' : 'subtle'}
              checked={effectiveViewMode === 'open'}
              onClick={() => setViewMode('open')}
            >
              <span className="tabular">Open PRs ({activePrList.length})</span>
            </ToggleButton>
          </div>
        ) : (
          <Badge appearance="tint" color="subtle" shape="rounded" className="tabular">
            Open Issues ({allKanbanItems.length})
          </Badge>
        )}
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

      {(pipeline.workflowRuns?.length ?? 0) > 0 && (
        <WorkflowRunsStrip
          runs={pipeline.workflowRuns!}
          runsSource={pipeline.workflowRunsSource}
          nowMs={nowMs}
          isDark={isDark}
          styles={styles}
        />
      )}

      {pipeline.source.status !== 'unavailable' && effectiveViewMode === 'open' && activePrList.length === 0 && (
        <div className={styles.emptyState}>
          <CheckmarkCircle16Regular style={{ width: 22, height: 22 }} aria-hidden="true" />
          <Text weight="semibold">No open PRs in MOBAflow</Text>
          <Text size={200}>All changes are currently integrated on main.</Text>
        </div>
      )}

      {effectiveViewMode === 'open' && activePrList.length > 0 && (
        <div className={styles.prList}>
          {activePrList.map((pr) => (
            <PullRequestRow
              key={`${pr.state}-${pr.number}`}
              pr={pr}
              monitors={monitors}
              nowMs={nowMs}
              isDark={isDark}
              styles={styles}
            />
          ))}
        </div>
      )}

      {pipeline.source.status !== 'unavailable' && effectiveViewMode === 'issues' && (
        <>
          <div className={styles.kanbanSummaryStrip}>
            <div className={styles.kanbanCountsGroup}>
              <StatusBadge tone="ok" className="tabular">
                Ready: {readyItems.length}
              </StatusBadge>
              <span className={styles.chevronSep} aria-hidden="true">
                ›
              </span>
              <StatusBadge tone="warn" className="tabular">
                In progress: {inProgressItems.length}
              </StatusBadge>
              <span className={styles.chevronSep} aria-hidden="true">
                ›
              </span>
              <Badge appearance="tint" color="informative" shape="rounded" className="tabular">
                In review: {inReviewItems.length}
              </Badge>
            </div>

            {pipeline.projectBoardUrl && (
              <Link
                href={pipeline.projectBoardUrl}
                target="_blank"
                rel="noreferrer"
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 4,
                  fontSize: tokens.fontSizeBase200,
                  fontWeight: tokens.fontWeightSemibold
                }}
              >
                Open GitHub Kanban{' '}
                <Open16Regular style={{ width: 12, height: 12 }} aria-hidden="true" />
              </Link>
            )}
          </div>

          {readyItems.length + inProgressItems.length + inReviewItems.length > 0 && (
            <div
              className={styles.stageBarTrack}
              role="img"
              aria-label={`Workflow distribution: Ready ${readyItems.length}, In progress ${inProgressItems.length}, In review ${inReviewItems.length}`}
            >
              {readyItems.length > 0 && (
                <div
                  style={{
                    flexGrow: readyItems.length,
                    backgroundColor: getStatusStyles('ok', isDark).solid
                  }}
                />
              )}
              {inProgressItems.length > 0 && (
                <div
                  style={{
                    flexGrow: inProgressItems.length,
                    backgroundColor: getStatusStyles('warn', isDark).solid
                  }}
                />
              )}
              {inReviewItems.length > 0 && (
                <div
                  style={{
                    flexGrow: inReviewItems.length,
                    backgroundColor: getStatusStyles('neutral', isDark).solid
                  }}
                />
              )}
            </div>
          )}

          {allKanbanItems.length === 0 ? (
            <div className={styles.emptyState}>
              <CheckmarkCircle16Regular style={{ width: 22, height: 22 }} aria-hidden="true" />
              <Text weight="semibold">No open issues on Kanban ({KANBAN_GROUPS.slice(0, 3).join(' / ')})</Text>
              <Text size={200}>
                Issues and PRs in Ready, In progress, or In review appear here 1:1 with GitHub Kanban.
              </Text>
            </div>
          ) : (
            columnGroups.map((group) => (
              <div key={group.label}>
                <div className={styles.kanbanGroupHeader}>
                  <span className={styles.kanbanGroupTitle}>
                    {group.label} ({group.items.length})
                  </span>
                </div>
                <div className={styles.prList}>
                  {group.items.map((item) => (
                    <KanbanItemRow
                      key={`${item.itemKind ?? 'Issue'}-${item.number}`}
                      issue={item}
                      monitors={monitors}
                      nowMs={nowMs}
                      isDark={isDark}
                      styles={styles}
                    />
                  ))}
                </div>
              </div>
            ))
          )}
        </>
      )}
    </Card>
  );
}

function PullRequestRow({
  pr,
  monitors,
  nowMs,
  isDark,
  styles
}: {
  pr: PullRequestInfo;
  monitors?: readonly MonitorInfo[];
  nowMs: number;
  isDark: boolean;
  styles: ReturnType<typeof useStyles>;
}) {
  const isMerged = pr.state === 'MERGED';
  const timeStamp = isMerged ? (pr.mergedAtUtc ?? pr.updatedAtUtc) : pr.updatedAtUtc;
  const blockReasons = pr.mergeBlockReasons ?? [];
  const normalizedMergeState = pr.mergeState.toUpperCase();
  const showBlockBanner =
    !isMerged &&
    blockReasons.length > 0 &&
    (normalizedMergeState === 'BLOCKED' || normalizedMergeState === 'DIRTY');
  const isHardBlock =
    normalizedMergeState === 'DIRTY' ||
    blockReasons.some(
      (r) =>
        r.includes('conflict') ||
        r.includes('failing') ||
        r.includes('Changes requested') ||
        r.includes('gate marked BLOCKED')
    );

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
              mergeBlockReasons={blockReasons}
            />
          )}
          <RiskClassBadge riskClass={pr.riskClass} />
        </div>
      </div>

      {showBlockBanner && (
        <MessageBar intent={isHardBlock ? 'error' : 'warning'} layout="multiline">
          <MessageBarBody>
            <strong>
              {normalizedMergeState === 'DIRTY' ? 'Merge conflict:' : 'Blocked reason:'}
            </strong>{' '}
            {blockReasons.join(' · ')}
          </MessageBarBody>
        </MessageBar>
      )}

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
        <span className={`${styles.monoPill} mono`} title={pr.branch}>
          <BranchFork16Regular style={{ flexShrink: 0 }} aria-hidden="true" />
          <span>{pr.branch}</span>
        </span>

        {!isMerged && (
          <>
            <CheckBadge label="CI" status={pr.ciStatus} />
            <CheckBadge label="Sonar" status={pr.sonarStatus} />
          </>
        )}

        {pr.projectStatus && <ContextChip appearance="tint">Kanban: {pr.projectStatus}</ContextChip>}

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

      <LockedAreasRow
        lockedAreas={pr.lockedAreas}
        riskClass={pr.riskClass}
        isDark={isDark}
        styles={styles}
      />

      <AgentRoundTable
        centerLabel={`#${pr.number}`}
        centerSub={`${pr.linkedIssues?.[0] ? `Issue #${pr.linkedIssues[0].number} · ` : ''}Class ${pr.riskClass}`}
        centerStatus={
          isMerged
            ? 'Merged'
            : pr.autoMergeEnabled
              ? 'Auto-Merge'
              : normalizedMergeState === 'CLEAN'
                ? 'CLEAN'
                : pr.stateLabel
        }
        centerTone={
          isMerged || normalizedMergeState === 'CLEAN'
            ? ('green' as AccentTone)
            : normalizedMergeState === 'DIRTY' || normalizedMergeState === 'BLOCKED'
              ? ('red' as AccentTone)
              : normalizedMergeState === 'BEHIND' ||
                  normalizedMergeState === 'UNSTABLE' ||
                  pr.isDraft
                ? ('gold' as AccentTone)
                : ('neutral' as AccentTone)
        }
        isTerminal={isMerged}
        participants={pr.participants}
        monitors={monitors}
        isDark={isDark}
      />
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
      <GlossaryTooltip term="Merge Gates" iconOnly />
      {gates.map((gate, idx) => (
        <Fragment key={gate.label}>
          {idx > 0 && (
            <span className={styles.chevronSep} aria-hidden="true">
              ›
            </span>
          )}
          <Badge
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
        </Fragment>
      ))}
    </div>
  );
}

function LockedAreasRow({
  lockedAreas,
  riskClass,
  isDark,
  styles
}: {
  lockedAreas: string[];
  riskClass?: string | null;
  isDark: boolean;
  styles: ReturnType<typeof useStyles>;
}) {
  if (lockedAreas.length === 0) {
    return null;
  }

  const dangerStyles = getStatusStyles('danger', isDark);

  return (
    <div className={styles.prMetaRow}>
      <LockClosed16Regular
        style={{ width: 14, height: 14, color: tokens.colorNeutralForeground2 }}
        aria-hidden="true"
      />
      <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
        Locked Areas:
      </Text>
      {lockedAreas.map((area) => (
        <span
          key={area}
          className={`${styles.monoPill} mono`}
          style={
            riskClass === 'H'
              ? {
                  borderColor: dangerStyles.border,
                  color: dangerStyles.fg
                }
              : undefined
          }
        >
          {area}
        </span>
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
    <Badge appearance="tint" color="success" shape="rounded">
      Ready
    </Badge>
  );
}

function MergeStateBadge({
  mergeState,
  ciStatus,
  sonarStatus,
  mergeBlockReasons
}: {
  mergeState: string;
  ciStatus: string;
  sonarStatus: string;
  mergeBlockReasons?: string[];
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
    case 'BLOCKED': {
      const normalized = mergeState.toUpperCase();
      const badge = (
        <Badge appearance="tint" color="danger" shape="rounded" icon={<Warning16Regular />}>
          {normalized}
        </Badge>
      );

      if (mergeBlockReasons && mergeBlockReasons.length > 0) {
        return (
          <Tooltip
            content={mergeBlockReasons.join(' · ')}
            relationship="description"
            withArrow
            positioning="above"
          >
            <span tabIndex={0} style={{ display: 'inline-flex', borderRadius: tokens.borderRadiusSmall }}>
              {badge}
            </span>
          </Tooltip>
        );
      }

      return <GlossaryTooltip term={normalized}>{badge}</GlossaryTooltip>;
    }
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

function sanitizeKanbanWriterText(raw: string): string {
  return raw
    .replace(/Gemini 4 Argon/gi, 'Gemini 4 Carbon')
    .replace(/Orchestrator\s*\(Neon\)/gi, 'Gemini 4 Carbon')
    .replace(/Claude Fable 5\.2/gi, 'Claude Sonnet 5.5');
}

function normalizeIssueParticipants(participants: TaskParticipantInfo[]): TaskParticipantInfo[] {
  return participants.map((participant) => {
    if (
      participant.evidenceSource?.startsWith('Kanban') &&
      /^gemini 4 argon$/i.test(participant.model ?? '')
    ) {
      return { ...participant, model: 'Gemini 4 Carbon' };
    }
    return participant;
  });
}

function KanbanItemRow({
  issue,
  monitors,
  nowMs,
  isDark,
  styles
}: {
  issue: ActiveIssueInfo;
  monitors?: readonly MonitorInfo[];
  nowMs: number;
  isDark: boolean;
  styles: ReturnType<typeof useStyles>;
}) {
  const isPr = issue.itemKind?.toLowerCase() === 'pullrequest';
  const isClosed = ['CLOSED', 'MERGED'].includes(issue.gitHubState.toUpperCase());
  const normalizedParticipants = normalizeIssueParticipants(issue.participants);
  const dangerStyles = getStatusStyles('danger', isDark);

  return (
    <div className={styles.prRow}>
      <div className={styles.prTop}>
        <div className={styles.prTitleWrap}>
          <Badge
            appearance={isPr ? 'tint' : 'outline'}
            color="subtle"
            shape="rounded"
            size="small"
          >
            {isPr ? 'PR' : 'Issue'}
          </Badge>
          <Link
            href={issue.url}
            target="_blank"
            rel="noreferrer"
            className="mono"
            title={`Open ${isPr ? 'PR' : 'Issue'} #${issue.number} on GitHub`}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 4,
              fontWeight: tokens.fontWeightSemibold,
              flexShrink: 0
            }}
          >
            #{issue.number} <Open16Regular style={{ width: 12, height: 12 }} aria-hidden="true" />
          </Link>
          <Text weight="semibold">{issue.title}</Text>
        </div>

        <div className={styles.prMetaRow}>
          <KanbanStatusBadge status={issue.kanbanStatus} />
          {issue.priority && <PriorityBadge priority={issue.priority} />}
          {issue.riskClass && <RiskClassBadge riskClass={issue.riskClass} />}
        </div>
      </div>

      {issue.boardDriftReason && (
        <MessageBar intent={isClosed ? 'error' : 'warning'} layout="multiline">
          <MessageBarBody>
            <strong>Board drift:</strong> {issue.boardDriftReason}
          </MessageBarBody>
        </MessageBar>
      )}

      {issue.linkedIssues && issue.linkedIssues.length > 0 && (
        <div className={styles.issuesRow}>
          <TicketDiagonal16Regular
            style={{ color: tokens.colorNeutralForeground2, flexShrink: 0 }}
            aria-hidden="true"
          />
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            Linked Issues:
          </Text>
          {issue.linkedIssues.map((linked) => (
            <LinkedIssuePill key={linked.number} issue={linked} className={styles.issuePillLink} />
          ))}
        </div>
      )}

      {issue.linkedPullRequests.length > 0 && (
        <div className={styles.issuesRow}>
          <BranchFork16Regular
            style={{ color: tokens.colorNeutralForeground2, flexShrink: 0 }}
            aria-hidden="true"
          />
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            Linked PRs:
          </Text>
          {issue.linkedPullRequests.map((prRef) => (
            <LinkedPullRequestPill
              key={prRef.number}
              prRef={prRef}
              className={styles.issuePillLink}
            />
          ))}
        </div>
      )}

      <div
        className={styles.prMetaRow}
        style={{ width: '100%' }}
      >
        {issue.area && (
          <Badge appearance="tint" color="subtle" shape="rounded">
            {issue.area}
          </Badge>
        )}
        {issue.issueType && (
          <Badge appearance="tint" color="subtle" shape="rounded">
            {issue.issueType}
          </Badge>
        )}
        {normalizedParticipants.length === 0 && issue.writer && (
          <span className={`${styles.monoPill} mono`}>
            Writer: {sanitizeKanbanWriterText(issue.writer)}
          </span>
        )}
        {issue.lockedAreas.length > 0 && (
          <>
            <span
              title="Locked Areas"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                color: tokens.colorNeutralForeground2
              }}
            >
              <LockClosed16Regular style={{ width: 14, height: 14 }} aria-hidden="true" />
            </span>
            {issue.lockedAreas.map((area) => (
              <span
                key={area}
                className={`${styles.monoPill} mono`}
                style={
                  issue.riskClass === 'H'
                    ? {
                        borderColor: dangerStyles.border,
                        color: dangerStyles.fg
                      }
                    : undefined
                }
              >
                {area}
              </span>
            ))}
          </>
        )}
        {isClosed && (
          <Badge appearance="tint" color="danger" shape="rounded">
            Closed
          </Badge>
        )}
        <Text
          size={200}
          className="tabular"
          style={{ color: tokens.colorNeutralForeground2, marginLeft: 'auto' }}
          title={formatAbsoluteEn(issue.updatedAtUtc)}
        >
          Updated {formatRelativeTimeEn(issue.updatedAtUtc, nowMs)}
        </Text>
      </div>

      <AgentRoundTable
        centerLabel={`${isPr ? 'PR' : 'Issue'} #${issue.number}`}
        centerSub={
          issue.riskClass
            ? `Class ${issue.riskClass}`
            : (issue.area ?? (isPr ? 'Pull Request' : 'GitHub Issue'))
        }
        centerStatus={issue.kanbanStatus}
        centerTone={
          issue.gitHubState.toUpperCase() === 'CLOSED'
            ? ('red' as AccentTone)
            : issue.kanbanStatus.toLowerCase() === 'ready'
              ? ('green' as AccentTone)
              : issue.kanbanStatus.toLowerCase() === 'in progress'
                ? ('gold' as AccentTone)
                : ('neutral' as AccentTone)
        }
        isTerminal={isClosed}
        participants={normalizedParticipants}
        monitors={monitors}
        isDark={isDark}
      />
    </div>
  );
}

function KanbanStatusBadge({ status }: { status: string }) {
  switch (status.toLowerCase()) {
    case 'in review':
      return (
        <Badge appearance="tint" color="informative" shape="rounded">
          {status}
        </Badge>
      );
    case 'in progress':
      return (
        <Badge appearance="tint" color="warning" shape="rounded">
          {status}
        </Badge>
      );
    case 'ready':
      return (
        <Badge appearance="tint" color="success" shape="rounded">
          {status}
        </Badge>
      );
    default:
      return (
        <Badge appearance="tint" color="subtle" shape="rounded">
          {status}
        </Badge>
      );
  }
}

function PriorityBadge({ priority }: { priority: string }) {
  const normalized = priority.toUpperCase();
  if (normalized === 'P0') {
    return (
      <Badge appearance="tint" color="danger" shape="rounded">
        {normalized}
      </Badge>
    );
  }
  return (
    <Badge appearance="outline" color="subtle" shape="rounded">
      {normalized}
    </Badge>
  );
}

function LinkedPullRequestPill({
  prRef,
  className
}: {
  prRef: LinkedPullRequestRef;
  className: string;
}) {
  return (
    <Link
      href={prRef.url}
      target="_blank"
      rel="noreferrer"
      className={className}
      title={`Open PR #${prRef.number} on GitHub`}
    >
      <span className="mono" style={{ fontWeight: tokens.fontWeightSemibold }}>
        PR #{prRef.number}
      </span>
      <Badge
        appearance="tint"
        color={prRef.isMerged ? 'success' : 'subtle'}
        shape="rounded"
        size="small"
      >
        {prRef.stateLabel}
      </Badge>
      <Open16Regular style={{ width: 12, height: 12, flexShrink: 0 }} aria-hidden="true" />
    </Link>
  );
}

function isSafeHttpUrl(url?: string | null): boolean {
  if (!url) {
    return false;
  }

  return /^https?:\/\//i.test(url.trim());
}

function isActiveWorkflowStatus(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  return (
    normalized === 'in_progress' ||
    normalized === 'queued' ||
    normalized === 'waiting' ||
    normalized === 'requested' ||
    normalized === 'pending'
  );
}

function WorkflowRunStatusIndicator({
  status,
  conclusion,
  isDark
}: {
  status: string;
  conclusion?: string | null;
  isDark: boolean;
}) {
  if (isActiveWorkflowStatus(status)) {
    const warnColors = getStatusStyles('warn', isDark);
    return (
      <span
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '5px',
          fontSize: tokens.fontSizeBase200,
          fontWeight: tokens.fontWeightSemibold,
          color: warnColors.fg
        }}
      >
        <span className="pulse-dot" style={{ backgroundColor: warnColors.solid }} aria-hidden="true" />
        <span>{status.replace(/_/g, ' ')}</span>
      </span>
    );
  }

  const normalizedConclusion = (conclusion ?? '').trim().toLowerCase();
  if (normalizedConclusion === 'success') {
    return (
      <StatusBadge tone="ok" icon={<Checkmark16Regular style={{ width: 12, height: 12 }} aria-hidden="true" />}>
        pass
      </StatusBadge>
    );
  }

  if (normalizedConclusion === 'failure' || normalizedConclusion === 'timed_out') {
    return (
      <StatusBadge tone="danger" icon={<Dismiss16Regular style={{ width: 12, height: 12 }} aria-hidden="true" />}>
        {normalizedConclusion.replace(/_/g, ' ')}
      </StatusBadge>
    );
  }

  return <StatusBadge tone="neutral">{normalizedConclusion || status}</StatusBadge>;
}

function WorkflowRunsStrip({
  runs,
  runsSource,
  nowMs,
  isDark,
  styles
}: {
  runs: WorkflowRunInfo[];
  runsSource?: SourceHealth | null;
  nowMs: number;
  isDark: boolean;
  styles: ReturnType<typeof useStyles>;
}) {
  const activeCount = runs.filter((r) => isActiveWorkflowStatus(r.status)).length;
  const visibleRuns = runs.slice(0, 8);
  const hiddenCount = Math.max(0, runs.length - visibleRuns.length);
  const uniqueRepos = new Set(runs.map((r) => r.repository)).size;
  const isStaleRuns = Boolean(runsSource?.fromCache || runsSource?.status === 'degraded');

  return (
    <section className={styles.workflowRunsStrip} aria-label="Running and Recent CI Pipelines">
      <div className={styles.workflowRunsHeader}>
        <div className={styles.titleGroup}>
          <Clock16Regular aria-hidden="true" />
          <Text size={200} weight="semibold">
            CI Pipelines ({uniqueRepos} {uniqueRepos === 1 ? 'Repo' : 'Repos'})
          </Text>
          {activeCount > 0 ? (
            <StatusBadge tone="warn" className="tabular">
              {activeCount} running
            </StatusBadge>
          ) : (
            <StatusBadge tone="ok" className="tabular">
              idle
            </StatusBadge>
          )}
          {isStaleRuns && (
            <StatusBadge tone="warn" className="tabular">
              cached
            </StatusBadge>
          )}
          {hiddenCount > 0 && (
            <Badge appearance="tint" color="subtle" shape="rounded" size="small" className="tabular">
              +{hiddenCount} more
            </Badge>
          )}
        </div>
        <Text
          size={100}
          style={{ color: tokens.colorNeutralForeground2 }}
          title={runsSource?.notice ?? undefined}
        >
          {isStaleRuns && runsSource?.updatedAtUtc
            ? `Cached GitHub Actions (${formatRelativeTimeEn(runsSource.updatedAtUtc, nowMs)})`
            : 'Live GitHub Actions'}
        </Text>
      </div>

      <div className={styles.workflowRunsList}>
        {visibleRuns.map((run, index) => {
          const shortRepo = run.repository.split('/').pop() ?? run.repository;
          return (
            <div key={`${run.repository}-${run.databaseId || `idx-${index}`}`} className={styles.workflowRunRow}>
              <div className={styles.workflowRunLeft}>
                <ContextChip appearance="tint" title={run.repository}>
                  {shortRepo}
                </ContextChip>
                <WorkflowRunStatusIndicator
                  status={run.status}
                  conclusion={run.conclusion}
                  isDark={isDark}
                />
                {isSafeHttpUrl(run.url) ? (
                  <Link
                    href={run.url}
                    target="_blank"
                    rel="noreferrer"
                    className={styles.workflowRunTitle}
                    title={`${run.workflowName}: ${run.displayTitle}`}
                  >
                    <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>
                      <strong>{run.workflowName}</strong> · {run.displayTitle}
                    </span>
                    <Open16Regular style={{ width: 12, height: 12, flexShrink: 0 }} aria-hidden="true" />
                  </Link>
                ) : (
                  <span className={styles.workflowRunTitle} title={`${run.workflowName}: ${run.displayTitle}`}>
                    <strong>{run.workflowName}</strong> · {run.displayTitle}
                  </span>
                )}
              </div>

              <div className={styles.kanbanCountsGroup}>
                <span className={`${styles.monoPill} mono`} title={`Branch: ${run.headBranch}`}>
                  <BranchFork16Regular style={{ flexShrink: 0 }} aria-hidden="true" />
                  <span>{run.headBranch}</span>
                </span>
                <Text
                  size={200}
                  className="tabular"
                  style={{ color: tokens.colorNeutralForeground2 }}
                  title={formatAbsoluteEn(run.updatedAtUtc)}
                >
                  {formatRelativeTimeEn(run.updatedAtUtc, nowMs)}
                </Text>
              </div>
            </div>
          );
        })}
      </div>
    </section>
  );
}
