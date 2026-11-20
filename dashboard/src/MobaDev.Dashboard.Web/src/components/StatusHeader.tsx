import { useState } from 'react';
import {
  Badge,
  Button,
  CounterBadge,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Tab,
  TabList,
  Text,
  ToggleButton,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import {
  ArrowClockwise16Regular,
  ArrowRight16Regular,
  CheckmarkCircle16Regular,
  Desktop16Regular,
  Eye16Regular,
  Warning16Regular,
  WeatherMoon16Regular,
  WeatherSunny16Regular,
  WifiOff20Regular
} from '@fluentui/react-icons';
import { formatAbsoluteEn, formatRelativeTimeEn } from '../api/dashboardClient';
import type { DashboardTab, ThemePreference, WarningBreakdown } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';

const useStyles = makeStyles({
  header: {
    position: 'sticky',
    top: 0,
    zIndex: 20,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    padding: '10px 24px 0'
  },
  inner: {
    maxWidth: '1440px',
    margin: '0 auto',
    display: 'flex',
    flexDirection: 'column',
    gap: '6px'
  },
  topRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px'
  },
  brandGroup: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '10px'
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    letterSpacing: '-0.01em'
  },
  controls: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '10px'
  },
  themeGroup: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '2px',
    padding: '2px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  },
  tabRow: {
    display: 'flex',
    alignItems: 'center',
    overflowX: 'auto'
  },
  popoverContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px',
    minWidth: '260px'
  },
  popoverRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    padding: '6px 8px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`
  }
});

export type WarningTarget = 'rules' | 'worktrees' | 'branches' | 'sessions' | 'monitors';

interface StatusHeaderProps {
  overallStatus?: string;
  warningCount: number;
  warningBreakdown: WarningBreakdown;
  onJumpToWarning: (target: WarningTarget) => void;
  changelogVersion?: string | null;
  onOpenChangelog?: () => void;
  generatedAtUtc?: string;
  nowMs: number;
  refreshing: boolean;
  onRefresh: () => void;
  themePreference: ThemePreference;
  onThemeChange: (pref: ThemePreference) => void;
  activeTab: DashboardTab;
  onTabChange: (tab: DashboardTab) => void;
  driftBadgeCount: number;
}

export function StatusHeader({
  overallStatus,
  warningCount,
  warningBreakdown,
  onJumpToWarning,
  changelogVersion,
  onOpenChangelog,
  generatedAtUtc,
  nowMs,
  refreshing,
  onRefresh,
  themePreference,
  onThemeChange,
  activeTab,
  onTabChange,
  driftBadgeCount
}: StatusHeaderProps) {
  const styles = useStyles();
  const [popoverOpen, setPopoverOpen] = useState(false);

  const allBreakdownRows: {
    label: string;
    actionLabel: string;
    count: number;
    target: WarningTarget;
  }[] = [
    {
      label:
        warningBreakdown.rulesDrift === 1
          ? '1 Rule Drift / Missing'
          : `${warningBreakdown.rulesDrift} Rules Drift / Missing`,
      actionLabel: 'Review Drift',
      count: warningBreakdown.rulesDrift,
      target: 'rules'
    },
    {
      label:
        warningBreakdown.staleWorktrees === 1
          ? '1 Stale Worktree'
          : `${warningBreakdown.staleWorktrees} Stale Worktrees`,
      actionLabel: 'View Worktrees',
      count: warningBreakdown.staleWorktrees,
      target: 'worktrees'
    },
    {
      label:
        warningBreakdown.staleBranches === 1
          ? '1 Stale Branch'
          : `${warningBreakdown.staleBranches} Stale Branches`,
      actionLabel: 'View Branches',
      count: warningBreakdown.staleBranches,
      target: 'branches'
    },
    {
      label:
        warningBreakdown.extraSessions === 1
          ? '1 Extra Session'
          : `${warningBreakdown.extraSessions} Extra Sessions`,
      actionLabel: 'View Sessions',
      count: warningBreakdown.extraSessions,
      target: 'sessions'
    },
    {
      label:
        warningBreakdown.quotaPauses === 1
          ? '1 Quota Pause'
          : `${warningBreakdown.quotaPauses} Quota Pauses`,
      actionLabel: 'View Monitors',
      count: warningBreakdown.quotaPauses,
      target: 'monitors'
    }
  ];

  const breakdownRows = allBreakdownRows.filter((r) => r.count > 0);

  const handleJump = (target: WarningTarget) => {
    setPopoverOpen(false);
    onJumpToWarning(target);
  };

  const renderOverallBadge = () => {
    if (!overallStatus) {
      return (
        <Badge appearance="tint" color="subtle" shape="rounded">
          Connecting…
        </Badge>
      );
    }

    if (overallStatus === 'healthy' && warningCount === 0) {
      return (
        <Badge appearance="tint" color="success" shape="rounded" icon={<CheckmarkCircle16Regular />}>
          System Healthy
        </Badge>
      );
    }

    const isDegraded = overallStatus === 'degraded';
    const badgeLabel = isDegraded
      ? `Partially Offline${warningCount > 0 ? ` · ${warningCount === 1 ? '1 Warning' : `${warningCount} Warnings`}` : ''}`
      : warningCount === 1
        ? '1 Warning'
        : `${warningCount} Warnings`;

    const badgeElement = (
      <Badge
        appearance="tint"
        color="warning"
        shape="rounded"
        icon={isDegraded ? <WifiOff20Regular style={{ width: 14, height: 14 }} /> : <Warning16Regular />}
      >
        {badgeLabel}
      </Badge>
    );

    if (warningCount === 0 && breakdownRows.length === 0) {
      return badgeElement;
    }

    return (
      <Popover
        open={popoverOpen}
        onOpenChange={(_, data) => setPopoverOpen(data.open)}
        withArrow
        positioning="below-start"
      >
        <PopoverTrigger disableButtonEnhancement>
          <Button
            size="small"
            appearance="transparent"
            style={{ padding: 0, minWidth: 0, height: 'auto' }}
            aria-label={`${badgeLabel} — show breakdown`}
          >
            {badgeElement}
          </Button>
        </PopoverTrigger>

        <PopoverSurface>
          <div className={styles.popoverContent} role="region" aria-label="Warning breakdown">
            <Text weight="semibold" size={300}>
              Warning Breakdown
            </Text>

            {breakdownRows.length === 0 ? (
              <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                One or more data sources are currently degraded or offline.
              </Text>
            ) : (
              breakdownRows.map((row) => (
                <div key={row.target} className={styles.popoverRow}>
                  <Text size={200} className="tabular">
                    {row.label}
                  </Text>
                  <Button
                    size="small"
                    appearance="subtle"
                    icon={<ArrowRight16Regular />}
                    iconPosition="after"
                    onClick={() => handleJump(row.target)}
                  >
                    {row.actionLabel}
                  </Button>
                </div>
              ))
            )}
          </div>
        </PopoverSurface>
      </Popover>
    );
  };

  return (
    <header className={styles.header}>
      <div className={styles.inner}>
        <div className={styles.topRow}>
          <div className={styles.brandGroup}>
            <h1 className={styles.title}>MOBAdev Agent Swarm Gen#3</h1>
            {changelogVersion && (
              <Button
                size="small"
                appearance="transparent"
                style={{ padding: 0, minWidth: 0, height: 'auto' }}
                onClick={onOpenChangelog}
                title="Internal version from CHANGELOG.md — open changelog"
                aria-label={`Version v${changelogVersion} — open CHANGELOG.md`}
              >
                <Badge appearance="outline" color="subtle" shape="rounded" className="mono">
                  v{changelogVersion}
                </Badge>
              </Button>
            )}
            <GlossaryTooltip term="Read-Only" showIcon>
              <Badge appearance="tint" color="brand" shape="rounded" icon={<Eye16Regular />}>
                Read-Only · 127.0.0.1
              </Badge>
            </GlossaryTooltip>
            {renderOverallBadge()}
          </div>

          <div className={styles.controls}>
            <Text
              size={200}
              className="tabular"
              style={{ color: tokens.colorNeutralForeground2 }}
              title={generatedAtUtc ? `Updated: ${formatAbsoluteEn(generatedAtUtc)}` : undefined}
            >
              Updated: {formatRelativeTimeEn(generatedAtUtc, nowMs)}
            </Text>

            <Button
              size="small"
              appearance="subtle"
              onClick={onRefresh}
              disabled={refreshing}
              aria-label="Refresh dashboard"
              icon={
                <ArrowClockwise16Regular className={refreshing ? 'spin' : undefined} aria-hidden="true" />
              }
            >
              Refresh
            </Button>

            <div className={styles.themeGroup} role="group" aria-label="Color theme">
              <ToggleButton
                size="small"
                appearance={themePreference === 'system' ? 'primary' : 'subtle'}
                checked={themePreference === 'system'}
                onClick={() => onThemeChange('system')}
                icon={<Desktop16Regular />}
              >
                System
              </ToggleButton>
              <ToggleButton
                size="small"
                appearance={themePreference === 'light' ? 'primary' : 'subtle'}
                checked={themePreference === 'light'}
                onClick={() => onThemeChange('light')}
                icon={<WeatherSunny16Regular />}
              >
                Light
              </ToggleButton>
              <ToggleButton
                size="small"
                appearance={themePreference === 'dark' ? 'primary' : 'subtle'}
                checked={themePreference === 'dark'}
                onClick={() => onThemeChange('dark')}
                icon={<WeatherMoon16Regular />}
              >
                Dark
              </ToggleButton>
            </div>
          </div>
        </div>

        <nav className={styles.tabRow} aria-label="Dashboard views">
          <TabList
            selectedValue={activeTab}
            onTabSelect={(_, data) => onTabChange(data.value as DashboardTab)}
          >
            <Tab value="live">Live Operations</Tab>
            <Tab value="matrix">
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <span>Matrix &amp; Rules</span>
                {driftBadgeCount > 0 && (
                  <CounterBadge count={driftBadgeCount} color="important" size="small" />
                )}
              </span>
            </Tab>
            <Tab value="mobadev-docs">Configuration</Tab>
          </TabList>
        </nav>
      </div>
    </header>
  );
}
