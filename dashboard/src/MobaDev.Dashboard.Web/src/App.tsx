import { useCallback, useEffect, useState } from 'react';
import {
  FluentProvider,
  MessageBar,
  MessageBarBody,
  Skeleton,
  SkeletonItem,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import { fetchSnapshot } from './api/dashboardClient';
import { DecisionTriadCard } from './components/DecisionTriadCard';
import { GovernanceMatrixView } from './components/GovernanceMatrixView';
import { HygieneAndSyncPanel } from './components/HygieneAndSyncPanel';
import { MonitorCards } from './components/MonitorCards';
import { PipelineCard } from './components/PipelineCard';
import { StatusHeader } from './components/StatusHeader';
import { mobaDarkTheme, mobaLightTheme } from './theme/mobaFluentTheme';
import type { DashboardSnapshot, DashboardTab, ThemePreference } from './types/dashboard';

const POLL_INTERVAL_MS = 15_000;
const THEME_STORAGE_KEY = 'mobadev-theme';

const useStyles = makeStyles({
  appShell: {
    minHeight: '100vh',
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1
  },
  mainContent: {
    maxWidth: '1440px',
    margin: '0 auto',
    padding: '18px 24px 32px',
    display: 'flex',
    flexDirection: 'column',
    gap: '14px'
  },
  liveSplit: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 7fr) minmax(0, 5fr)',
    gap: '14px',
    alignItems: 'start',
    '@media (max-width: 1099px)': {
      gridTemplateColumns: 'minmax(0, 1fr)'
    }
  },
  monitorsRow: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gap: '14px',
    '@media (max-width: 959px)': {
      gridTemplateColumns: 'minmax(0, 1fr)'
    }
  }
});

function readInitialThemePreference(): ThemePreference {
  try {
    const stored = window.localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'system') {
      return stored;
    }
  } catch {
    // Ignore storage access errors in restricted environments.
  }

  return 'system';
}

function resolveTheme(pref: ThemePreference): 'light' | 'dark' {
  if (pref === 'light' || pref === 'dark') {
    return pref;
  }

  if (typeof window.matchMedia === 'function') {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  return 'dark';
}

export function App() {
  const styles = useStyles();
  const [snapshot, setSnapshot] = useState<DashboardSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<DashboardTab>('live');
  const [onlyDrift, setOnlyDrift] = useState(false);
  const [themePreference, setThemePreference] = useState<ThemePreference>(readInitialThemePreference);
  const [resolvedTheme, setResolvedTheme] = useState<'light' | 'dark'>(() =>
    resolveTheme(readInitialThemePreference())
  );
  const [nowMs, setNowMs] = useState(() => Date.now());

  useEffect(() => {
    const apply = () => {
      const next = resolveTheme(themePreference);
      setResolvedTheme(next);
      document.documentElement.dataset.theme = next;
    };

    apply();

    try {
      window.localStorage.setItem(THEME_STORAGE_KEY, themePreference);
    } catch {
      // Ignore storage errors.
    }

    if (themePreference !== 'system' || typeof window.matchMedia !== 'function') {
      return undefined;
    }

    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const handleChange = () => apply();
    mediaQuery.addEventListener('change', handleChange);
    return () => mediaQuery.removeEventListener('change', handleChange);
  }, [themePreference]);

  const loadSnapshot = useCallback(async (forceRefresh = false, signal?: AbortSignal) => {
    setRefreshing(true);
    try {
      const data = await fetchSnapshot(forceRefresh, signal);
      setSnapshot(data);
      setApiError(null);
      setNowMs(Date.now());
    } catch (error) {
      if (signal?.aborted) {
        return;
      }

      const message = error instanceof Error ? error.message : 'Unknown error';
      setApiError(`Local dashboard backend unreachable (${message}).`);
    } finally {
      if (!signal?.aborted) {
        setLoading(false);
        setRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void loadSnapshot(false, controller.signal);

    const pollTimer = window.setInterval(() => {
      void loadSnapshot(false);
    }, POLL_INTERVAL_MS);

    const clockTimer = window.setInterval(() => {
      setNowMs(Date.now());
    }, 1000);

    return () => {
      controller.abort();
      window.clearInterval(pollTimer);
      window.clearInterval(clockTimer);
    };
  }, [loadSnapshot]);

  const handleOpenMatrixSync = (filterOnlyDrift: boolean) => {
    setOnlyDrift(filterOnlyDrift);
    setActiveTab('matrix');
  };

  const driftBadgeCount = snapshot
    ? snapshot.governance.driftCount + snapshot.governance.missingCount
    : 0;
  const isDark = resolvedTheme === 'dark';

  return (
    <FluentProvider theme={isDark ? mobaDarkTheme : mobaLightTheme}>
      <div className={styles.appShell}>
        <StatusHeader
          overallStatus={snapshot?.overallStatus}
          warningCount={snapshot?.warningCount ?? 0}
          generatedAtUtc={snapshot?.generatedAtUtc}
          nowMs={nowMs}
          refreshing={refreshing}
          onRefresh={() => void loadSnapshot(true)}
          themePreference={themePreference}
          onThemeChange={setThemePreference}
          activeTab={activeTab}
          onTabChange={setActiveTab}
          driftBadgeCount={driftBadgeCount}
        />

        <main className={styles.mainContent}>
          {apiError && (
            <MessageBar intent="error" role="alert">
              <MessageBarBody>{apiError}</MessageBarBody>
            </MessageBar>
          )}

          {loading && !snapshot && <LoadingSkeleton styles={styles} />}

          {snapshot && activeTab === 'live' && (
            <>
              <MonitorCards
                monitors={snapshot.monitors.monitors}
                nowMs={nowMs}
                isDark={isDark}
              />

              <DecisionTriadCard
                monitors={snapshot.monitors.monitors}
                governance={snapshot.governance}
                openPrCount={snapshot.pipeline.pullRequests.length}
                isDark={isDark}
                onOpenMatrix={() => handleOpenMatrixSync(false)}
              />

              <div className={styles.liveSplit}>
                <PipelineCard pipeline={snapshot.pipeline} nowMs={nowMs} isDark={isDark} />
                <HygieneAndSyncPanel
                  governance={snapshot.governance}
                  hygiene={snapshot.hygiene}
                  onOpenMatrixSync={handleOpenMatrixSync}
                />
              </div>
            </>
          )}

          {snapshot && activeTab === 'matrix' && (
            <GovernanceMatrixView
              governance={snapshot.governance}
              onlyDrift={onlyDrift}
              onOnlyDriftChange={setOnlyDrift}
              isDark={isDark}
            />
          )}
        </main>
      </div>
    </FluentProvider>
  );
}

function LoadingSkeleton({ styles }: { styles: ReturnType<typeof useStyles> }) {
  return (
    <Skeleton aria-label="Loading dashboard" style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div className={styles.monitorsRow}>
        <SkeletonItem style={{ height: 156, borderRadius: tokens.borderRadiusMedium }} />
        <SkeletonItem style={{ height: 156, borderRadius: tokens.borderRadiusMedium }} />
        <SkeletonItem style={{ height: 156, borderRadius: tokens.borderRadiusMedium }} />
      </div>
      <SkeletonItem style={{ height: 74, borderRadius: tokens.borderRadiusMedium }} />
      <div className={styles.liveSplit}>
        <SkeletonItem style={{ height: 280, borderRadius: tokens.borderRadiusMedium }} />
        <SkeletonItem style={{ height: 280, borderRadius: tokens.borderRadiusMedium }} />
      </div>
    </Skeleton>
  );
}
