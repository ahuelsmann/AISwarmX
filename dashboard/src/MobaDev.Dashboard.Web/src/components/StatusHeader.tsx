import {
  Badge,
  Button,
  CounterBadge,
  Tab,
  TabList,
  Text,
  ToggleButton,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import {
  ArrowClockwise16Regular,
  CheckmarkCircle16Regular,
  Desktop16Regular,
  Eye16Regular,
  Warning16Regular,
  WeatherMoon16Regular,
  WeatherSunny16Regular,
  WifiOff20Regular
} from '@fluentui/react-icons';
import { formatAbsoluteEn, formatRelativeTimeEn } from '../api/dashboardClient';
import type { DashboardTab, ThemePreference } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';

const useStyles = makeStyles({
  header: {
    position: 'sticky',
    top: 0,
    zIndex: 20,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    padding: '12px 24px 0'
  },
  inner: {
    maxWidth: '1440px',
    margin: '0 auto',
    display: 'flex',
    flexDirection: 'column',
    gap: '8px'
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
    alignItems: 'center'
  }
});

interface StatusHeaderProps {
  overallStatus?: string;
  warningCount: number;
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

  const renderOverallBadge = () => {
    if (!overallStatus) {
      return (
        <Badge appearance="tint" color="subtle" shape="rounded">
          Connecting…
        </Badge>
      );
    }

    if (overallStatus === 'degraded') {
      return (
        <Badge
          appearance="tint"
          color="warning"
          shape="rounded"
          icon={<WifiOff20Regular style={{ width: 14, height: 14 }} />}
        >
          Partially Offline
          {warningCount > 0 ? ` · ${warningCount === 1 ? '1 Warning' : `${warningCount} Warnings`}` : ''}
        </Badge>
      );
    }

    if (overallStatus === 'warnings' || warningCount > 0) {
      return (
        <Badge appearance="tint" color="warning" shape="rounded" icon={<Warning16Regular />}>
          {warningCount === 1 ? '1 Warning' : `${warningCount} Warnings`}
        </Badge>
      );
    }

    return (
      <Badge appearance="tint" color="success" shape="rounded" icon={<CheckmarkCircle16Regular />}>
        System Healthy
      </Badge>
    );
  };

  return (
    <header className={styles.header}>
      <div className={styles.inner}>
        <div className={styles.topRow}>
          <div className={styles.brandGroup}>
            <h1 className={styles.title}>MOBAdev Agent Network</h1>
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
          </TabList>
        </nav>
      </div>
    </header>
  );
}
