import { useCallback, useEffect, useState } from 'react';
import {
  FluentProvider,
  Link,
  MessageBar,
  MessageBarBody,
  Skeleton,
  SkeletonItem,
  makeStyles,
  tokens
} from '@fluentui/react-components';
import { ArrowUp16Regular } from '@fluentui/react-icons';
import { fetchSnapshot } from './api/dashboardClient';
import { subscribeSwarmHub, type LiveConnectionState } from './api/swarmHubClient';
import { ArchitectureDiagramsView } from './components/ArchitectureDiagramsView';
import { ConfigDocsView } from './components/ConfigDocsView';
import { GovernanceMatrixView } from './components/GovernanceMatrixView';
import { HygieneAndSyncPanel } from './components/HygieneAndSyncPanel';
import { MainLandingTickerCard } from './components/MainLandingTickerCard';
import { MonitorCards } from './components/MonitorCards';
import { PipelineCard } from './components/PipelineCard';
import { StatusHeader, type WarningTarget } from './components/StatusHeader';
import { SwarmActivityPulseBar } from './components/SwarmActivityPulseBar';
import { SwarmBusCard } from './components/SwarmBusCard';
import { mobaDarkTheme, mobaLightTheme } from './theme/mobaFluentTheme';
import type {
  ConfigDocsScope,
  DashboardSnapshot,
  DashboardTab,
  ThemePreference,
  WarningBreakdown
} from './types/dashboard';

const POLL_INTERVAL_MS = 15_000;
const THEME_STORAGE_KEY = 'aiswarmx-theme';
const LEGACY_THEME_STORAGE_KEY = 'mobadev-theme';

const useStyles = makeStyles({
  appShell: {
    minHeight: '100vh',
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1
  },
  mainContent: {
    maxWidth: '1440px',
    margin: '0 auto',
    padding: '16px 24px 32px',
    display: 'flex',
    flexDirection: 'column',
    gap: '12px'
  },
  hygieneSkeletonGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(380px, 1fr))',
    gap: '14px'
  },
  monitorsRow: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gap: '12px',
    '@media (max-width: 959px)': {
      gridTemplateColumns: 'minmax(0, 1fr)'
    }
  },
  liveFooter: {
    display: 'flex',
    justifyContent: 'flex-end',
    alignItems: 'center',
    paddingTop: '10px',
    marginTop: '4px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`
  },
  backToTopLink: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold
  }
});

function readInitialThemePreference(): ThemePreference {
  try {
    const stored =
      window.localStorage.getItem(THEME_STORAGE_KEY) ??
      window.localStorage.getItem(LEGACY_THEME_STORAGE_KEY);
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
  const [requestedDoc, setRequestedDoc] = useState<{
    scope: ConfigDocsScope;
    path: string;
  } | null>(null);
  const [themePreference, setThemePreference] = useState<ThemePreference>(readInitialThemePreference);
  const [resolvedTheme, setResolvedTheme] = useState<'light' | 'dark'>(() =>
    resolveTheme(readInitialThemePreference())
  );
  const [nowMs, setNowMs] = useState(() => Date.now());
  const [liveConnectionState, setLiveConnectionState] = useState<LiveConnectionState>('polling');

  const mergeIfFresher = (
    prev: DashboardSnapshot | null,
    incoming: DashboardSnapshot
  ): DashboardSnapshot => {
    const prevBusMs = prev?.swarmBus ? Date.parse(prev.swarmBus.generatedAtUtc) : Number.NaN;
    const incomingBusMs = incoming.swarmBus ? Date.parse(incoming.swarmBus.generatedAtUtc) : Number.NaN;
    if (
      prev?.swarmBus &&
      !Number.isNaN(prevBusMs) &&
      (Number.isNaN(incomingBusMs) || prevBusMs > incomingBusMs)
    ) {
      return { ...incoming, swarmBus: prev.swarmBus };
    }

    return incoming;
  };

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
      setSnapshot((prev) => mergeIfFresher(prev, data));
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

    const unsubscribeHub = subscribeSwarmHub({
      onSwarmBusUpdated: (swarmBus) => {
        setSnapshot((prev) => (prev ? { ...prev, swarmBus } : prev));
        setNowMs(Date.now());
      },
      onSnapshotUpdated: (nextSnapshot) => {
        setSnapshot((prev) => mergeIfFresher(prev, nextSnapshot));
        setApiError(null);
        setNowMs(Date.now());
      },
      onStateChange: setLiveConnectionState
    });

    return () => {
      controller.abort();
      unsubscribeHub();
      window.clearInterval(pollTimer);
      window.clearInterval(clockTimer);
    };
  }, [loadSnapshot]);

  const handleOpenMatrixSync = (filterOnlyDrift: boolean) => {
    setOnlyDrift(filterOnlyDrift);
    setActiveTab('matrix');
  };

  const handleJumpToWarning = (target: WarningTarget) => {
    if (target === 'rules') {
      handleOpenMatrixSync(true);
      return;
    }

    const reducedMotion =
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const scrollBehavior: ScrollBehavior = reducedMotion ? 'auto' : 'smooth';

    setActiveTab('live');
    if (target === 'monitors') {
      window.scrollTo?.({ top: 0, behavior: scrollBehavior });
      return;
    }

    window.requestAnimationFrame(() => {
      const id =
        target === 'worktrees'
          ? 'section-worktrees'
          : target === 'branches'
            ? 'section-branches'
            : 'section-sessions';
      document.getElementById(id)?.scrollIntoView?.({ behavior: scrollBehavior, block: 'start' });
    });
  };

  const handleOpenDoc = (scope: ConfigDocsScope, relativePath: string) => {
    setRequestedDoc({ scope, path: relativePath });
    setActiveTab('mobadev-docs');
  };

  const handleScrollToTop = () => {
    const reducedMotion =
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const scrollBehavior: ScrollBehavior = reducedMotion ? 'auto' : 'smooth';
    window.scrollTo?.({ top: 0, behavior: scrollBehavior });
  };

  const driftBadgeCount = snapshot
    ? snapshot.governance.driftCount + snapshot.governance.missingCount
    : 0;

  const warningBreakdown: WarningBreakdown = snapshot
    ? {
        rulesDrift: snapshot.governance.driftCount + snapshot.governance.missingCount,
        staleWorktrees: snapshot.hygiene.staleWorktreeCount,
        staleBranches:
          (snapshot.hygiene.staleLocalBranchCount ?? 0) +
          (snapshot.hygiene.staleRemoteBranchCount ?? 0),
        extraSessions: snapshot.hygiene.sessions.filter((s) => s.status === 'warning').length,
        quotaPauses: snapshot.monitors.monitors.filter((m) => m.state === 'rate_limited').length
      }
    : { rulesDrift: 0, staleWorktrees: 0, staleBranches: 0, extraSessions: 0, quotaPauses: 0 };

  const isDark = resolvedTheme === 'dark';

  return (
    <FluentProvider theme={isDark ? mobaDarkTheme : mobaLightTheme}>
      <div id="live-top" className={styles.appShell}>
        <StatusHeader
          overallStatus={snapshot?.overallStatus}
          warningCount={snapshot?.warningCount ?? 0}
          warningBreakdown={warningBreakdown}
          onJumpToWarning={handleJumpToWarning}
          changelogVersion={snapshot?.changelogVersion}
          onOpenChangelog={() => handleOpenDoc('aiswarmx', 'CHANGELOG.md')}
          generatedAtUtc={snapshot?.generatedAtUtc}
          nowMs={nowMs}
          refreshing={refreshing}
          onRefresh={() => void loadSnapshot(true)}
          themePreference={themePreference}
          onThemeChange={setThemePreference}
          activeTab={activeTab}
          onTabChange={setActiveTab}
          driftBadgeCount={driftBadgeCount}
          communicationUnreadCount={snapshot?.swarmBus?.unreadCount ?? 0}
          isDark={isDark}
        />

        <main className={styles.mainContent}>
          {apiError && (
            <MessageBar intent="error" role="alert">
              <MessageBarBody>{apiError}</MessageBarBody>
            </MessageBar>
          )}

          {loading &&
            !snapshot &&
            (activeTab === 'live' ||
              activeTab === 'communication' ||
              activeTab === 'architecture' ||
              activeTab === 'matrix') && (
              <LoadingSkeleton styles={styles} />
            )}

          {snapshot && activeTab === 'live' && (
            <>
              <MonitorCards
                monitors={snapshot.monitors.monitors}
                monitorsSummary={snapshot.monitors.summary}
                nowMs={nowMs}
                isDark={isDark}
                governance={snapshot.governance}
                pullRequests={snapshot.pipeline.pullRequests}
                onOpenMatrix={() => handleOpenMatrixSync(false)}
              />

              <SwarmActivityPulseBar
                swarmBus={snapshot.swarmBus}
                workflowRuns={snapshot.pipeline.workflowRuns}
                monitors={snapshot.monitors.monitors}
                liveConnectionState={liveConnectionState}
                nowMs={nowMs}
                isDark={isDark}
                onOpenCommunication={() => setActiveTab('communication')}
              />

              <MainLandingTickerCard pipeline={snapshot.pipeline} nowMs={nowMs} />

              <PipelineCard
                pipeline={snapshot.pipeline}
                monitors={snapshot.monitors.monitors}
                nowMs={nowMs}
                isDark={isDark}
              />

              <HygieneAndSyncPanel
                governance={snapshot.governance}
                hygiene={snapshot.hygiene}
                onOpenMatrixSync={handleOpenMatrixSync}
              />

              <div className={styles.liveFooter}>
                <Link
                  href="#live-top"
                  className={styles.backToTopLink}
                  onClick={(e) => {
                    e.preventDefault();
                    handleScrollToTop();
                  }}
                >
                  <ArrowUp16Regular aria-hidden="true" />
                  <span>Back to top</span>
                </Link>
              </div>
            </>
          )}

          {snapshot && activeTab === 'communication' && (
            <SwarmBusCard
              swarmBus={snapshot.swarmBus}
              nowMs={nowMs}
              liveConnectionState={liveConnectionState}
            />
          )}

          {snapshot && activeTab === 'architecture' && (
            <ArchitectureDiagramsView
              snapshot={snapshot}
              isDark={isDark}
              onOpenLive={() => setActiveTab('live')}
              onOpenMatrix={() => handleOpenMatrixSync(false)}
            />
          )}

          {snapshot && activeTab === 'matrix' && (
            <GovernanceMatrixView
              governance={snapshot.governance}
              onlyDrift={onlyDrift}
              onOnlyDriftChange={setOnlyDrift}
              isDark={isDark}
              onOpenDoc={handleOpenDoc}
            />
          )}

          {activeTab === 'mobadev-docs' && (
            <ConfigDocsView
              key="aiswarmx"
              scope="aiswarmx"
              isDark={isDark}
              requestedPath={
                requestedDoc?.scope === 'aiswarmx' || requestedDoc?.scope === 'mobadev'
                  ? requestedDoc.path
                  : null
              }
              onConsumeRequestedPath={() => setRequestedDoc(null)}
            />
          )}
        </main>
      </div>
    </FluentProvider>
  );
}

function LoadingSkeleton({ styles }: { styles: ReturnType<typeof useStyles> }) {
  return (
    <Skeleton aria-label="Loading dashboard" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div className={styles.monitorsRow}>
        <SkeletonItem style={{ height: 112, borderRadius: tokens.borderRadiusMedium }} />
        <SkeletonItem style={{ height: 112, borderRadius: tokens.borderRadiusMedium }} />
        <SkeletonItem style={{ height: 112, borderRadius: tokens.borderRadiusMedium }} />
      </div>
      <SkeletonItem style={{ height: 68, borderRadius: tokens.borderRadiusMedium }} />
      <SkeletonItem style={{ height: 280, borderRadius: tokens.borderRadiusMedium }} />
      <div className={styles.hygieneSkeletonGrid}>
        <SkeletonItem style={{ height: 180, borderRadius: tokens.borderRadiusMedium }} />
        <SkeletonItem style={{ height: 180, borderRadius: tokens.borderRadiusMedium }} />
      </div>
    </Skeleton>
  );
}
