import { Fragment, useState } from 'react';
import {
  Badge,
  Button,
  Card,
  MessageBar,
  MessageBarBody,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  ToggleButton,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import {
  ArrowRight16Regular,
  BranchRequest16Regular,
  CheckmarkCircle16Regular,
  ChevronDown16Regular,
  ChevronRight16Regular,
  Prohibited16Regular,
  Warning16Regular
} from '@fluentui/react-icons';
import { fetchRuleDiff } from '../api/dashboardClient';
import { getToneStyles } from '../theme/mobaFluentTheme';
import type {
  AgentSyncItem,
  ConfigDocsScope,
  GovernanceResponse,
  RuleDiffResponse
} from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';
import { ModelBadge, ProfileBadge, RoleBadge } from './RoleAndModelIcons';

const useStyles = makeStyles({
  stack: {
    display: 'flex',
    flexDirection: 'column',
    gap: '14px'
  },
  filterBar: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    padding: '10px 14px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  filterGroup: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '6px'
  },
  card: {
    padding: '16px',
    gap: '12px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  cardHeader: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px'
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
  tableScroll: {
    overflowX: 'auto'
  },
  split: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 7fr) minmax(0, 5fr)',
    gap: '14px',
    '@media (max-width: 1099px)': {
      gridTemplateColumns: 'minmax(0, 1fr)'
    }
  },
  skillList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px'
  },
  skillRow: {
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
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    textAlign: 'center',
    gap: '6px',
    padding: '24px 16px',
    color: tokens.colorNeutralForeground2
  },
  diffContainer: {
    margin: '6px 0',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    overflow: 'hidden'
  },
  diffHeader: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    padding: '8px 12px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`
  },
  diffScroll: {
    maxHeight: '280px',
    overflowY: 'auto',
    fontSize: tokens.fontSizeBase200,
    lineHeight: '20px'
  },
  diffLine: {
    display: 'grid',
    gridTemplateColumns: '40px 40px 18px minmax(0, 1fr)',
    gap: '6px',
    padding: '1px 10px',
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word'
  },
  strategyGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
    gap: '10px'
  },
  strategyTile: {
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
    padding: '10px 12px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  tableCell: {
    paddingTop: '10px',
    paddingBottom: '10px',
    verticalAlign: 'top'
  },
  wrapPill: {
    display: 'inline-block',
    padding: '3px 8px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200,
    lineHeight: '17px',
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'normal',
    wordBreak: 'break-word',
    maxWidth: '100%'
  },
  riskCellStack: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: '4px'
  }
});

interface GovernanceMatrixViewProps {
  governance: GovernanceResponse;
  onlyDrift: boolean;
  onOnlyDriftChange: (value: boolean) => void;
  isDark: boolean;
  onOpenDoc?: (scope: ConfigDocsScope, relativePath: string) => void;
}

const GIT_STRATEGY_INVARIANTS = [
  {
    title: '1. Small Batches on main',
    detail: '1 Draft PR = 1 focused slice (~25 files / ~400 lines); no long-running branches.'
  },
  {
    title: '2. Worktree Isolation',
    detail: 'Every active slice runs in its own Git worktree; main stays clean at all times.'
  },
  {
    title: '3. Merge, Never Rebase',
    detail: 'BEHIND branches merge main in locally; shared branches are never rebased or force-pushed.'
  },
  {
    title: '4. One Writer · Area Locks · Code WIP <= 3',
    detail: 'Single writer per branch, non-overlapping locks, max 3 open code slices (Class L docs excluded).'
  },
  {
    title: '5. Head-Bound Squash Auto-Merge',
    detail: 'Armed by COO (--squash --auto --match-head-commit) only after green CI, Sonar = 0, P1 Final & Cross-Family PASS.'
  }
];

function rowKey(agent: AgentSyncItem): string {
  return `${agent.tool}::${agent.kind}::${agent.fileName}`;
}

export function GovernanceMatrixView({
  governance,
  onlyDrift,
  onOnlyDriftChange,
  isDark,
  onOpenDoc
}: GovernanceMatrixViewProps) {
  const styles = useStyles();
  const [profileFilter, setProfileFilter] = useState<string>('ALL');
  const [riskFilter, setRiskFilter] = useState<string>('ALL');

  const [expandedKey, setExpandedKey] = useState<string | null>(null);
  const [diffCache, setDiffCache] = useState<Record<string, RuleDiffResponse>>({});
  const [loadingKey, setLoadingKey] = useState<string | null>(null);
  const [diffError, setDiffError] = useState<Record<string, string>>({});

  const driftTotal = governance.driftCount + governance.missingCount;

  const filteredAgents = governance.agents.filter((agent) => {
    if (onlyDrift && agent.syncStatus === 'synced') {
      return false;
    }
    if (profileFilter !== 'ALL' && agent.profileHint !== profileFilter) {
      return false;
    }
    return true;
  });

  const filteredRoles = governance.roles.filter((role) => {
    if (profileFilter === 'ALL') {
      return true;
    }
    return role.profiles.toUpperCase().includes(profileFilter);
  });

  const filteredRiskClasses = governance.riskClasses.filter((rc) => {
    if (riskFilter === 'ALL') {
      return true;
    }
    return rc.code.toUpperCase() === riskFilter;
  });

  const handleToggleDiff = async (agent: AgentSyncItem) => {
    const key = rowKey(agent);
    if (expandedKey === key) {
      setExpandedKey(null);
      return;
    }

    setExpandedKey(key);
    if (diffCache[key]) {
      return;
    }

    setLoadingKey(key);
    setDiffError((prev) => {
      const copy = { ...prev };
      delete copy[key];
      return copy;
    });

    try {
      const diff = await fetchRuleDiff(agent.tool, agent.kind, agent.fileName);
      setDiffCache((prev) => ({ ...prev, [key]: diff }));
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Unable to load diff.';
      setDiffError((prev) => ({ ...prev, [key]: msg }));
    } finally {
      setLoadingKey(null);
    }
  };

  return (
    <div className={styles.stack}>
      {/* Git & Delivery Strategy Reference */}
      <Card className={styles.card} appearance="filled" aria-label="Git and Delivery Strategy">
        <div className={styles.cardHeader}>
          <div className={styles.titleGroup}>
            <BranchRequest16Regular aria-hidden="true" />
            <h2 className={styles.title}>Git &amp; Delivery Strategy</h2>
            <Badge appearance="tint" color="subtle" shape="rounded">
              MOBAflow Invariants
            </Badge>
          </div>

          {onOpenDoc && (
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
              <Button
                size="small"
                appearance="subtle"
                icon={<ArrowRight16Regular />}
                iconPosition="after"
                onClick={() => onOpenDoc('mobadev', 'manifest.md')}
              >
                Manifest
              </Button>
              <Button
                size="small"
                appearance="subtle"
                icon={<ArrowRight16Regular />}
                iconPosition="after"
                onClick={() => onOpenDoc('mobadev', 'docs/delivery-workflow.md')}
              >
                Delivery Workflow
              </Button>
            </div>
          )}
        </div>

        <div className={styles.strategyGrid}>
          {GIT_STRATEGY_INVARIANTS.map((item) => (
            <div key={item.title} className={styles.strategyTile}>
              <Text weight="semibold" size={200}>
                {item.title}
              </Text>
              <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                {item.detail}
              </Text>
            </div>
          ))}
        </div>
      </Card>

      {driftTotal === 0 ? (
        <MessageBar intent="success">
          <MessageBarBody>
            <strong>In Sync:</strong> All {governance.syncedCount} local agents and rules match the
            MOBAdev target baseline.
          </MessageBarBody>
        </MessageBar>
      ) : (
        <MessageBar intent="warning">
          <MessageBarBody>
            <strong>Drift Detected:</strong> {governance.driftCount} drift(s) and{' '}
            {governance.missingCount} missing mapping(s) between local machine (Actual) and MOBAdev
            (Target).
          </MessageBarBody>
        </MessageBar>
      )}

      {/* Filter Bar */}
      <div className={styles.filterBar} role="region" aria-label="Matrix filters">
        <div className={styles.filterGroup}>
          <Text size={200} weight="semibold" style={{ color: tokens.colorNeutralForeground2 }}>
            Profile:
          </Text>
          {['ALL', 'P1', 'P2', 'P3', 'P4'].map((p) => (
            <ToggleButton
              key={p}
              size="small"
              appearance={profileFilter === p ? 'primary' : 'subtle'}
              checked={profileFilter === p}
              onClick={() => setProfileFilter(p)}
            >
              {p === 'ALL' ? 'All' : p}
            </ToggleButton>
          ))}
        </div>

        <div className={styles.filterGroup}>
          <Text size={200} weight="semibold" style={{ color: tokens.colorNeutralForeground2 }}>
            Risk Class:
          </Text>
          {['ALL', 'H', 'M', 'L'].map((r) => (
            <ToggleButton
              key={r}
              size="small"
              appearance={riskFilter === r ? 'primary' : 'subtle'}
              checked={riskFilter === r}
              onClick={() => setRiskFilter(r)}
            >
              {r === 'ALL' ? 'All' : `Class ${r}`}
            </ToggleButton>
          ))}
        </div>

        <ToggleButton
          size="small"
          appearance={onlyDrift ? 'primary' : 'subtle'}
          checked={onlyDrift}
          onClick={() => onOnlyDriftChange(!onlyDrift)}
        >
          <span className="tabular">Drift Only ({driftTotal})</span>
        </ToggleButton>
      </div>

      {/* Rules State · Actual vs. Target */}
      <Card className={styles.card} appearance="filled" aria-label="Rules State Actual vs Target">
        <div className={styles.cardHeader}>
          <div className={styles.titleGroup}>
            <h2 className={styles.title}>Rules State · Actual vs. Target</h2>
            <GlossaryTooltip term="Drift" iconOnly />
          </div>
          <Badge appearance="tint" color="subtle" shape="rounded">
            <span className="tabular">
              {filteredAgents.length === 1 ? '1 Entry' : `${filteredAgents.length} Entries`}
            </span>
          </Badge>
        </div>

        {filteredAgents.length === 0 ? (
          <div className={styles.emptyState}>
            <CheckmarkCircle16Regular style={{ width: 22, height: 22 }} aria-hidden="true" />
            <Text weight="semibold">No matching entries for active filters</Text>
            <Text size={200}>
              All local rules match MOBAdev or matching rows are hidden by current filters.
            </Text>
          </div>
        ) : (
          <div className={styles.tableScroll}>
            <Table size="small" aria-label="Agent and rule sync table">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Name &amp; File</TableHeaderCell>
                  <TableHeaderCell>Tool / Type</TableHeaderCell>
                  <TableHeaderCell>Profile</TableHeaderCell>
                  <TableHeaderCell>Sync Status</TableHeaderCell>
                  <TableHeaderCell>Hashes (Actual / Target)</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredAgents.map((agent) => {
                  const key = rowKey(agent);
                  const isExpanded = expandedKey === key;
                  const cachedDiff = diffCache[key];
                  const rowError = diffError[key];

                  return (
                    <Fragment key={key}>
                      <TableRow>
                        <TableCell className={styles.tableCell}>
                          <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                            <RoleBadge
                              role={agent.kind === 'rule' ? 'rule' : agent.name}
                              label={agent.name}
                              isDark={isDark}
                              compact
                            />
                            <Text size={200} className="mono" style={{ color: tokens.colorNeutralForeground2 }}>
                              {agent.fileName}
                            </Text>
                            {agent.description && (
                              <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                                {agent.description}
                              </Text>
                            )}
                          </div>
                        </TableCell>
                        <TableCell className={styles.tableCell}>
                          <div>{agent.tool}</div>
                          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                            {agent.kind === 'rule' ? 'Rule' : 'Agent'}
                          </Text>
                        </TableCell>
                        <TableCell className={styles.tableCell}>
                          {agent.profileHint ? (
                            <GlossaryTooltip term={agent.profileHint}>
                              <ProfileBadge profile={agent.profileHint} isDark={isDark} />
                            </GlossaryTooltip>
                          ) : (
                            '–'
                          )}
                        </TableCell>
                        <TableCell className={styles.tableCell}>
                          <div
                            style={{
                              display: 'flex',
                              flexDirection: 'column',
                              gap: 4,
                              alignItems: 'flex-start'
                            }}
                          >
                            <SyncBadge status={agent.syncStatus} />
                            {agent.driftSummary && (
                              <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                                {agent.driftSummary}
                              </Text>
                            )}
                            {agent.syncStatus !== 'synced' && (
                              <Button
                                size="small"
                                appearance="subtle"
                                icon={isExpanded ? <ChevronDown16Regular /> : <ChevronRight16Regular />}
                                onClick={() => void handleToggleDiff(agent)}
                              >
                                {isExpanded ? 'Hide Diff' : 'Show Diff'}
                              </Button>
                            )}
                          </div>
                        </TableCell>
                        <TableCell className={`${styles.tableCell} mono`}>
                          <div>Actual: {agent.localHash ?? '–'}</div>
                          <div>Target: {agent.repoHash ?? '–'}</div>
                        </TableCell>
                      </TableRow>

                      {isExpanded && (
                        <TableRow>
                          <TableCell colSpan={5}>
                            <RuleDiffPreview
                              diff={cachedDiff}
                              loading={loadingKey === key}
                              error={rowError}
                              isDark={isDark}
                              styles={styles}
                            />
                          </TableCell>
                        </TableRow>
                      )}
                    </Fragment>
                  );
                })}
              </TableBody>
            </Table>
          </div>
        )}
      </Card>

      {/* Capability Profiles (P1-P4) & Model Mapping */}
      <Card
        className={styles.card}
        appearance="filled"
        aria-label="Capability Profiles and Model Mapping"
      >
        <div className={styles.cardHeader}>
          <h2 className={styles.title}>Capability Profiles (P1–P4) &amp; Model Mapping</h2>
        </div>

        <div className={styles.tableScroll}>
          <Table size="small" aria-label="Capability profiles table">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Profile</TableHeaderCell>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Active Models &amp; Clients</TableHeaderCell>
                <TableHeaderCell>Strengths &amp; Typical Assignments</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {governance.profiles.map((p) => (
                <TableRow key={p.profile}>
                  <TableCell className={styles.tableCell}>
                    <GlossaryTooltip term={p.profile} showIcon>
                      <ProfileBadge profile={p.profile} isDark={isDark} />
                    </GlossaryTooltip>
                  </TableCell>
                  <TableCell className={styles.tableCell}>
                    <Text weight="semibold">{p.name}</Text>
                  </TableCell>
                  <TableCell className={styles.tableCell}>
                    <ModelBadge model={p.models} isDark={isDark} mono wrap />
                  </TableCell>
                  <TableCell className={styles.tableCell}>{p.strengths}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </Card>

      {/* Bottom Split: Roles vs. Risk Classes + Skills */}
      <div className={styles.split}>
        <Card className={styles.card} appearance="filled" aria-label="Roles and Responsibilities">
          <div className={styles.cardHeader}>
            <h2 className={styles.title}>Roles &amp; Responsibilities</h2>
          </div>

          <div className={styles.tableScroll}>
            <Table size="small" aria-label="Roles matrix table">
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Role</TableHeaderCell>
                  <TableHeaderCell>Profile</TableHeaderCell>
                  <TableHeaderCell>Responsibility</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredRoles.map((role) => (
                  <TableRow key={role.role}>
                    <TableCell className={styles.tableCell}>
                      <RoleBadge role={role.role} isDark={isDark} compact />
                    </TableCell>
                    <TableCell className={styles.tableCell}>
                      <span className={styles.wrapPill}>{role.profiles}</span>
                    </TableCell>
                    <TableCell className={styles.tableCell}>{role.responsibility}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Card>

        <div className={styles.stack}>
          <Card className={styles.card} appearance="filled" aria-label="Risk Classes H M L">
            <div className={styles.cardHeader}>
              <h2 className={styles.title}>Risk Classes (H / M / L)</h2>
            </div>

            <div className={styles.tableScroll}>
              <Table size="small" aria-label="Risk classes table">
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>Class</TableHeaderCell>
                    <TableHeaderCell>MOBAflow Areas</TableHeaderCell>
                    <TableHeaderCell>Team &amp; Review Rule</TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {filteredRiskClasses.map((rc) => (
                    <TableRow key={rc.code}>
                      <TableCell className={styles.tableCell}>
                        <div className={styles.riskCellStack}>
                          <GlossaryTooltip term={rc.code} showIcon>
                            <Badge
                              appearance="outline"
                              color={
                                rc.code === 'H'
                                  ? 'danger'
                                  : rc.code === 'M'
                                    ? 'warning'
                                    : 'success'
                              }
                              shape="rounded"
                            >
                              Class {rc.code}
                            </Badge>
                          </GlossaryTooltip>
                          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                            {rc.name}
                          </Text>
                        </div>
                      </TableCell>
                      <TableCell className={styles.tableCell}>{rc.areas}</TableCell>
                      <TableCell className={styles.tableCell}>{rc.requiredDiversity}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </Card>

          <Card className={styles.card} appearance="filled" aria-label="Versioned Skills in MOBAdev">
            <div className={styles.cardHeader}>
              <h2 className={styles.title}>Skills ({governance.skills.length})</h2>
            </div>

            <div className={styles.skillList}>
              {governance.skills.map((skill) => (
                <div key={skill.name} className={styles.skillRow}>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 }}>
                    <Text weight="semibold" size={200}>
                      {skill.name}
                    </Text>
                    <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                      {skill.description}
                    </Text>
                  </div>
                  <span className={`${styles.wrapPill} mono`}>{skill.relativePath}</span>
                </div>
              ))}
            </div>
          </Card>
        </div>
      </div>

    </div>
  );
}

function RuleDiffPreview({
  diff,
  loading,
  error,
  isDark,
  styles
}: {
  diff?: RuleDiffResponse;
  loading: boolean;
  error?: string;
  isDark: boolean;
  styles: ReturnType<typeof useStyles>;
}) {
  const greenStyles = getToneStyles('green', isDark);
  const redStyles = getToneStyles('red', isDark);

  if (loading) {
    return (
      <div style={{ padding: '12px' }}>
        <Spinner size="small" label="Loading read-only diff…" />
      </div>
    );
  }

  if (error) {
    return (
      <MessageBar intent="error" style={{ margin: '6px 0' }}>
        <MessageBarBody>{error}</MessageBarBody>
      </MessageBar>
    );
  }

  if (!diff) {
    return null;
  }

  return (
    <div className={styles.diffContainer} aria-label={`Read-only diff for ${diff.fileName}`}>
      <div className={styles.diffHeader}>
        <Text size={200} weight="semibold">
          Target (MOBAdev) vs. Actual (Local)
        </Text>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <Badge appearance="tint" color="success" shape="rounded" size="small" className="tabular">
            +{diff.addedLines} added
          </Badge>
          <Badge appearance="tint" color="danger" shape="rounded" size="small" className="tabular">
            -{diff.removedLines} removed
          </Badge>
          {diff.truncated && (
            <Badge appearance="outline" color="subtle" shape="rounded" size="small">
              Truncated
            </Badge>
          )}
        </div>
      </div>

      {diff.lines.length === 0 ? (
        <div style={{ padding: '10px 12px', color: tokens.colorNeutralForeground2 }}>
          No textual line differences detected.
        </div>
      ) : (
        <div className={`${styles.diffScroll} mono`}>
          {diff.lines.map((line, index) => {
            const bg =
              line.kind === 'added'
                ? greenStyles.bg
                : line.kind === 'removed'
                  ? redStyles.bg
                  : line.kind === 'hunk'
                    ? tokens.colorNeutralBackground3
                    : 'transparent';
            const marker =
              line.kind === 'added'
                ? '+'
                : line.kind === 'removed'
                  ? '-'
                  : line.kind === 'hunk'
                    ? '@@'
                    : ' ';
            const fg =
              line.kind === 'added'
                ? greenStyles.fg
                : line.kind === 'removed'
                  ? redStyles.fg
                  : line.kind === 'hunk'
                    ? tokens.colorNeutralForeground2
                    : tokens.colorNeutralForeground1;

            return (
              <div
                key={`dl-${index}`}
                className={styles.diffLine}
                style={{ backgroundColor: bg, color: fg }}
              >
                <span className="tabular" style={{ color: tokens.colorNeutralForeground3 }}>
                  {line.targetLineNumber ?? ''}
                </span>
                <span className="tabular" style={{ color: tokens.colorNeutralForeground3 }}>
                  {line.actualLineNumber ?? ''}
                </span>
                <span>{marker}</span>
                <span>{line.text}</span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

function SyncBadge({ status }: { status: string }) {
  switch (status) {
    case 'synced':
      return (
        <Badge appearance="tint" color="success" shape="rounded" icon={<CheckmarkCircle16Regular />}>
          In Sync
        </Badge>
      );
    case 'drift':
      return (
        <Badge appearance="tint" color="warning" shape="rounded" icon={<Warning16Regular />}>
          Drift Detected
        </Badge>
      );
    default:
      return (
        <Badge appearance="tint" color="subtle" shape="rounded" icon={<Prohibited16Regular />}>
          Missing Source
        </Badge>
      );
  }
}
