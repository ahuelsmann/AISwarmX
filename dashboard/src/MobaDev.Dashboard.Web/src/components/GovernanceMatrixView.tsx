import { useState } from 'react';
import {
  Badge,
  Card,
  MessageBar,
  MessageBarBody,
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
  CheckmarkCircle16Regular,
  Prohibited16Regular,
  Warning16Regular
} from '@fluentui/react-icons';
import type { GovernanceResponse } from '../types/dashboard';
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
  }
});

interface GovernanceMatrixViewProps {
  governance: GovernanceResponse;
  onlyDrift: boolean;
  onOnlyDriftChange: (value: boolean) => void;
  isDark: boolean;
}

export function GovernanceMatrixView({
  governance,
  onlyDrift,
  onOnlyDriftChange,
  isDark
}: GovernanceMatrixViewProps) {
  const styles = useStyles();
  const [profileFilter, setProfileFilter] = useState<string>('ALL');
  const [riskFilter, setRiskFilter] = useState<string>('ALL');

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

  return (
    <div className={styles.stack}>
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
            <GlossaryTooltip term="Drift" showIcon />
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
                {filteredAgents.map((agent) => (
                  <TableRow key={`${agent.tool}-${agent.kind}-${agent.fileName}`}>
                    <TableCell>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 3, padding: '4px 0' }}>
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
                    <TableCell>
                      <div>{agent.tool}</div>
                      <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                        {agent.kind === 'rule' ? 'Rule' : 'Agent'}
                      </Text>
                    </TableCell>
                    <TableCell>
                      {agent.profileHint ? (
                        <GlossaryTooltip term={agent.profileHint}>
                          <ProfileBadge profile={agent.profileHint} isDark={isDark} />
                        </GlossaryTooltip>
                      ) : (
                        '–'
                      )}
                    </TableCell>
                    <TableCell>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'flex-start' }}>
                        <SyncBadge status={agent.syncStatus} />
                        {agent.driftSummary && (
                          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                            {agent.driftSummary}
                          </Text>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="mono">
                      <div>Actual: {agent.localHash ?? '–'}</div>
                      <div>Target: {agent.repoHash ?? '–'}</div>
                    </TableCell>
                  </TableRow>
                ))}
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
                  <TableCell>
                    <GlossaryTooltip term={p.profile} showIcon>
                      <ProfileBadge profile={p.profile} isDark={isDark} />
                    </GlossaryTooltip>
                  </TableCell>
                  <TableCell>
                    <Text weight="semibold">{p.name}</Text>
                  </TableCell>
                  <TableCell>
                    <ModelBadge model={p.models} isDark={isDark} mono />
                  </TableCell>
                  <TableCell>{p.strengths}</TableCell>
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
                    <TableCell>
                      <RoleBadge role={role.role} isDark={isDark} compact />
                    </TableCell>
                    <TableCell>
                      <Badge appearance="tint" color="subtle" shape="rounded">
                        {role.profiles}
                      </Badge>
                    </TableCell>
                    <TableCell>{role.responsibility}</TableCell>
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
                      <TableCell>
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
                            {rc.code} · {rc.name}
                          </Badge>
                        </GlossaryTooltip>
                      </TableCell>
                      <TableCell>{rc.areas}</TableCell>
                      <TableCell>{rc.requiredDiversity}</TableCell>
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
                  <Badge appearance="outline" color="subtle" shape="rounded" className="mono">
                    {skill.relativePath}
                  </Badge>
                </div>
              ))}
            </div>
          </Card>
        </div>
      </div>
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
