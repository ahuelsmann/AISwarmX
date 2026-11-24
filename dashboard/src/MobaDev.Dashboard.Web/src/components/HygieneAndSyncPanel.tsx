import { useState } from 'react';
import {
  Badge,
  Button,
  Card,
  MessageBar,
  MessageBarBody,
  Text,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import {
  ArrowRight16Regular,
  BranchFork16Regular,
  CheckmarkCircle16Regular,
  ChevronDown16Regular,
  ChevronUp16Regular,
  Folder16Regular,
  ShieldTask16Regular,
  Warning16Regular
} from '@fluentui/react-icons';
import type { GovernanceResponse, HygieneResponse } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';

const useStyles = makeStyles({
  stack: {
    display: 'flex',
    flexDirection: 'column',
    gap: '14px'
  },
  card: {
    padding: '14px 16px',
    gap: '12px',
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
  summaryGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gap: '8px'
  },
  statTile: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: '2px',
    padding: '10px 8px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  statValue: {
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1
  },
  statLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px'
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '10px',
    padding: '8px 10px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  rowMain: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    flex: '1 1 0',
    minWidth: 0,
    overflow: 'hidden'
  },
  rowTitle: {
    display: 'block',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  },
  rowSub: {
    display: 'block',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  }
});

interface HygieneAndSyncPanelProps {
  governance: GovernanceResponse;
  hygiene: HygieneResponse;
  onOpenMatrixSync: (onlyDrift: boolean) => void;
}

const MAX_COLLAPSED_STALE_WORKTREES = 3;
const MAX_COLLAPSED_STALE_BRANCHES = 4;
const SAFE_PRUNE_COMMAND =
  'pwsh -NoProfile -File C:/Repo/ahuelsmann/MOBAdev/scripts/Invoke-RepoHygiene.ps1';
const TOOL_DISPLAY_ORDER = ['ChatGPT Codex', 'Claude Code', 'Antigravity'];

function sortSessions<T extends { tool: string }>(sessions: readonly T[]): T[] {
  return [...sessions].sort((a, b) => {
    const idxA = TOOL_DISPLAY_ORDER.indexOf(a.tool);
    const idxB = TOOL_DISPLAY_ORDER.indexOf(b.tool);
    const rankA = idxA === -1 ? Number.MAX_SAFE_INTEGER : idxA;
    const rankB = idxB === -1 ? Number.MAX_SAFE_INTEGER : idxB;
    return rankA - rankB;
  });
}

export function HygieneAndSyncPanel({
  governance,
  hygiene,
  onOpenMatrixSync
}: HygieneAndSyncPanelProps) {
  const styles = useStyles();
  const [showAllStale, setShowAllStale] = useState(false);
  const [showAllBranches, setShowAllBranches] = useState(false);
  const [copiedPruneCmd, setCopiedPruneCmd] = useState(false);

  const handleCopyPruneCommand = () => {
    if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
      void navigator.clipboard.writeText(SAFE_PRUNE_COMMAND);
    }
    setCopiedPruneCmd(true);
    window.setTimeout(() => setCopiedPruneCmd(false), 2000);
  };

  const driftTotal = governance.driftCount + governance.missingCount;

  const staleWorktrees = hygiene.worktrees.filter((w) => w.status === 'stale' && !w.isDirty);
  const dirtyPreservedWorktrees = hygiene.worktrees.filter(
    (w) => w.status === 'stale' && Boolean(w.isDirty)
  );
  const otherWorktrees = hygiene.worktrees.filter((w) => w.status !== 'stale');
  const canCollapseStale = staleWorktrees.length > MAX_COLLAPSED_STALE_WORKTREES;
  const visibleStaleWorktrees =
    canCollapseStale && !showAllStale
      ? staleWorktrees.slice(0, MAX_COLLAPSED_STALE_WORKTREES)
      : staleWorktrees;
  const visibleWorktrees = [...visibleStaleWorktrees, ...dirtyPreservedWorktrees, ...otherWorktrees];

  const allBranches = hygiene.branches ?? [];
  const staleBranches = allBranches.filter((b) => b.status === 'stale');
  const activeBranches = allBranches.filter((b) => b.status !== 'stale');
  const staleLocalCount =
    hygiene.staleLocalBranchCount ??
    staleBranches.filter((b) => b.scope === 'local').length;
  const staleRemoteCount =
    hygiene.staleRemoteBranchCount ??
    staleBranches.filter((b) => b.scope === 'remote').length;
  const staleBranchTotal = staleLocalCount + staleRemoteCount;
  const canCollapseBranches = staleBranches.length > MAX_COLLAPSED_STALE_BRANCHES;
  const visibleStaleBranches =
    canCollapseBranches && !showAllBranches
      ? staleBranches.slice(0, MAX_COLLAPSED_STALE_BRANCHES)
      : staleBranches;
  const visibleBranches = [...visibleStaleBranches, ...activeBranches];

  return (
    <div className={styles.stack}>
      {/* Rules State · Actual vs. Target */}
      <Card
        id="section-rules"
        className={styles.card}
        appearance="filled"
        aria-label="Rules State Actual vs Target"
      >
        <div className={styles.header}>
          <div className={styles.titleGroup}>
            <ShieldTask16Regular aria-hidden="true" />
            <h2 className={styles.title}>Rules State · Actual vs. Target</h2>
            <GlossaryTooltip term="Drift" iconOnly />
          </div>

          {driftTotal === 0 ? (
            <Badge
              appearance="tint"
              color="success"
              shape="rounded"
              icon={<CheckmarkCircle16Regular />}
            >
              In Sync
            </Badge>
          ) : (
            <Badge appearance="tint" color="warning" shape="rounded" icon={<Warning16Regular />}>
              <span className="tabular">{driftTotal} Out of Sync</span>
            </Badge>
          )}
        </div>

        <div className={styles.summaryGrid}>
          <div className={styles.statTile}>
            <span className={`${styles.statValue} tabular`}>{governance.syncedCount}</span>
            <span className={styles.statLabel}>In Sync</span>
          </div>
          <div className={styles.statTile}>
            <span className={`${styles.statValue} tabular`}>{governance.driftCount}</span>
            <span className={styles.statLabel}>Drift</span>
          </div>
          <div className={styles.statTile}>
            <span className={`${styles.statValue} tabular`}>{governance.missingCount}</span>
            <span className={styles.statLabel}>Missing</span>
          </div>
        </div>

        <Button
          size="small"
          appearance="subtle"
          icon={<ArrowRight16Regular />}
          iconPosition="after"
          onClick={() => onOpenMatrixSync(driftTotal > 0)}
          style={{ justifyContent: 'space-between' }}
        >
          {driftTotal > 0 ? 'Review Drift' : 'Open Matrix & Rules'}
        </Button>
      </Card>

      {/* Session Hygiene · Actual vs. Target */}
      <Card
        id="section-sessions"
        className={styles.card}
        appearance="filled"
        aria-label="Session Hygiene per Tool"
      >
        <div className={styles.header}>
          <h2 className={styles.title}>Session Hygiene · Actual vs. Target</h2>
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            Target: 1 monitor / tool
          </Text>
        </div>

        <div className={styles.list}>
          {sortSessions(hygiene.sessions).map((session) => (
            <div key={session.tool} className={styles.row}>
              <div className={styles.rowMain}>
                <Text weight="semibold" size={200} className={styles.rowTitle}>
                  {session.tool}
                </Text>
                <span className={styles.rowSub}>
                  {session.notice ? session.notice : session.monitorName}
                </span>
              </div>

              {session.status === 'ok' && (
                <Badge
                  appearance="tint"
                  color="success"
                  shape="rounded"
                  icon={<CheckmarkCircle16Regular />}
                >
                  <span className="tabular">
                    {session.actualCount} / {session.targetCount} Target
                  </span>
                </Badge>
              )}
              {session.status === 'inactive' && (
                <Badge appearance="tint" color="subtle" shape="rounded">
                  <span className="tabular">
                    {session.actualCount} / {session.targetCount} Inactive
                  </span>
                </Badge>
              )}
              {session.status === 'warning' && (
                <Badge appearance="tint" color="warning" shape="rounded" icon={<Warning16Regular />}>
                  <span className="tabular">
                    {session.actualCount} / {session.targetCount} Warning
                  </span>
                </Badge>
              )}
            </div>
          ))}
        </div>
      </Card>

      {/* Git Worktrees (MOBAflow) */}
      <Card
        id="section-worktrees"
        className={styles.card}
        appearance="filled"
        aria-label="Git Worktrees MOBAflow"
      >
        <div className={styles.header}>
          <div className={styles.titleGroup}>
            <Folder16Regular aria-hidden="true" />
            <h2 className={styles.title}>Git Worktrees (MOBAflow)</h2>
            <GlossaryTooltip term="Worktree" iconOnly />
          </div>

          {hygiene.staleWorktreeCount > 0 ? (
            <Badge appearance="tint" color="warning" shape="rounded" icon={<Warning16Regular />}>
              <span className="tabular">{hygiene.staleWorktreeCount} Stale</span>
            </Badge>
          ) : (
            <Badge appearance="tint" color="success" shape="rounded">
              <span className="tabular">{hygiene.worktrees.length} Clean</span>
            </Badge>
          )}
        </div>

        {hygiene.source.status !== 'ok' && hygiene.source.notice && (
          <MessageBar intent="warning" layout="multiline">
            <MessageBarBody>{hygiene.source.notice}</MessageBarBody>
          </MessageBar>
        )}

        <div className={styles.list}>
          {visibleWorktrees.map((wt) => (
            <div key={`${wt.folderName}-${wt.branch}`} className={styles.row}>
              <div className={styles.rowMain}>
                <Text
                  weight="semibold"
                  size={200}
                  className={`${styles.rowTitle} mono`}
                  title={wt.branch}
                >
                  {wt.branch}
                </Text>
                <span className={`${styles.rowSub} mono`} title={wt.folderName}>
                  {wt.folderName} · {wt.headShort}
                </span>
              </div>

              {wt.status === 'stale' && wt.isDirty && (
                <Badge appearance="tint" color="informative" shape="rounded">
                  Dirty · Preserved
                </Badge>
              )}
              {wt.status === 'stale' && !wt.isDirty && (
                <Badge appearance="tint" color="warning" shape="rounded" icon={<Warning16Regular />}>
                  Stale / Merged
                </Badge>
              )}
              {wt.status === 'active' && (
                <Badge appearance="tint" color="warning" shape="rounded">
                  <span className="tabular">
                    Active{wt.linkedPrNumber ? ` (#${wt.linkedPrNumber})` : ''}
                  </span>
                </Badge>
              )}
              {wt.status === 'main' && (
                <Badge appearance="tint" color="success" shape="rounded">
                  Main
                </Badge>
              )}
            </div>
          ))}
        </div>

        {(canCollapseStale || hygiene.staleWorktreeCount > 0 || staleBranchTotal > 0) && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            {canCollapseStale && (
              <Button
                size="small"
                appearance="subtle"
                icon={showAllStale ? <ChevronUp16Regular /> : <ChevronDown16Regular />}
                iconPosition="after"
                onClick={() => setShowAllStale((prev) => !prev)}
                style={{ justifyContent: 'space-between' }}
              >
                {showAllStale
                  ? 'Show fewer stale worktrees'
                  : `Show all ${staleWorktrees.length} stale worktrees`}
              </Button>
            )}
            {(hygiene.staleWorktreeCount > 0 || staleBranchTotal > 0) && (
              <Button
                size="small"
                appearance="subtle"
                icon={copiedPruneCmd ? <CheckmarkCircle16Regular /> : <ArrowRight16Regular />}
                iconPosition="after"
                onClick={handleCopyPruneCommand}
                title={SAFE_PRUNE_COMMAND}
                style={{ justifyContent: 'space-between' }}
              >
                {copiedPruneCmd ? 'Copied!' : 'Copy Safe Prune Cmd'}
              </Button>
            )}
          </div>
        )}
      </Card>

      {/* Branch Hygiene (MOBAflow & MOBAdev) */}
      <Card
        id="section-branches"
        className={styles.card}
        appearance="filled"
        aria-label="Branch Hygiene MOBAflow and MOBAdev"
      >
        <div className={styles.header}>
          <div className={styles.titleGroup}>
            <BranchFork16Regular aria-hidden="true" />
            <h2 className={styles.title}>Branch Hygiene (Local &amp; Remote)</h2>
            <GlossaryTooltip term="Branch Hygiene" iconOnly />
          </div>

          {staleBranchTotal > 0 ? (
            <Badge appearance="tint" color="warning" shape="rounded" icon={<Warning16Regular />}>
              <span className="tabular">{staleBranchTotal} Stale</span>
            </Badge>
          ) : (
            <Badge appearance="tint" color="success" shape="rounded">
              Clean
            </Badge>
          )}
        </div>

        <div className={styles.summaryGrid}>
          <div className={styles.statTile}>
            <span className={`${styles.statValue} tabular`}>{staleLocalCount}</span>
            <span className={styles.statLabel}>Local Stale</span>
          </div>
          <div className={styles.statTile}>
            <span className={`${styles.statValue} tabular`}>{staleRemoteCount}</span>
            <span className={styles.statLabel}>Remote Stale</span>
          </div>
          <div className={styles.statTile}>
            <span className={`${styles.statValue} tabular`}>{activeBranches.length}</span>
            <span className={styles.statLabel}>Active</span>
          </div>
        </div>

        {visibleBranches.length === 0 ? (
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            Only main branches present across MOBAflow and MOBAdev.
          </Text>
        ) : (
          <div className={styles.list}>
            {visibleBranches.map((branch) => (
              <div
                key={`${branch.repository}-${branch.scope}-${branch.name}`}
                className={styles.row}
              >
                <div className={styles.rowMain}>
                  <Text
                    weight="semibold"
                    size={200}
                    className={`${styles.rowTitle} mono`}
                    title={branch.name}
                  >
                    {branch.name}
                  </Text>
                  <span className={styles.rowSub}>
                    {branch.repository} · {branch.scope}
                    {branch.upstreamGone ? ' · upstream gone' : ''}
                  </span>
                </div>

                {branch.status === 'stale' ? (
                  <Badge
                    appearance="tint"
                    color="warning"
                    shape="rounded"
                    icon={<Warning16Regular />}
                  >
                    Stale
                  </Badge>
                ) : (
                  <Badge appearance="tint" color="warning" shape="rounded">
                    <span className="tabular">
                      Active{branch.linkedPrNumber ? ` (#${branch.linkedPrNumber})` : ''}
                    </span>
                  </Badge>
                )}
              </div>
            ))}
          </div>
        )}

        {canCollapseBranches && (
          <Button
            size="small"
            appearance="subtle"
            icon={showAllBranches ? <ChevronUp16Regular /> : <ChevronDown16Regular />}
            iconPosition="after"
            onClick={() => setShowAllBranches((prev) => !prev)}
            style={{ justifyContent: 'space-between' }}
          >
            {showAllBranches
              ? 'Show fewer stale branches'
              : `Show all ${staleBranches.length} stale branches`}
          </Button>
        )}
      </Card>
    </div>
  );
}
