import { useCallback, useEffect, useState } from 'react';
import { WifiOff } from 'lucide-react';
import { fetchSnapshot } from './api/dashboardClient';
import { GovernanceMatrixView } from './components/GovernanceMatrixView';
import { HygieneAndSyncPanel } from './components/HygieneAndSyncPanel';
import { MonitorCards } from './components/MonitorCards';
import { PipelineCard } from './components/PipelineCard';
import { StatusHeader } from './components/StatusHeader';
import type { DashboardSnapshot, DashboardTab, ThemePreference } from './types/dashboard';

const POLL_INTERVAL_MS = 15_000;
const THEME_STORAGE_KEY = 'mobadev-theme';

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
  const [snapshot, setSnapshot] = useState<DashboardSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<DashboardTab>('live');
  const [onlyDrift, setOnlyDrift] = useState(false);
  const [themePreference, setThemePreference] = useState<ThemePreference>(readInitialThemePreference);
  const [nowMs, setNowMs] = useState(() => Date.now());

  useEffect(() => {
    const apply = () => {
      document.documentElement.dataset.theme = resolveTheme(themePreference);
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

      const message = error instanceof Error ? error.message : 'Unbekannter Fehler';
      setApiError(`Lokales Dashboard-Backend nicht erreichbar (${message}).`);
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

  return (
    <div className="app-shell">
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

      <main className="main-content">
        {apiError && (
          <div className="notice-banner notice-banner--red" role="alert">
            <WifiOff size={16} aria-hidden="true" />
            <span>{apiError}</span>
          </div>
        )}

        {loading && !snapshot && <LoadingSkeleton />}

        {snapshot && activeTab === 'live' && (
          <>
            <MonitorCards monitors={snapshot.monitors.monitors} nowMs={nowMs} />
            <div className="live-split">
              <PipelineCard pipeline={snapshot.pipeline} nowMs={nowMs} />
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
          />
        )}
      </main>
    </div>
  );
}

function LoadingSkeleton() {
  return (
    <div className="side-stack" aria-label="Lade Dashboard">
      <div className="monitors-grid">
        <div className="skeleton" style={{ height: 174 }} />
        <div className="skeleton" style={{ height: 174 }} />
        <div className="skeleton" style={{ height: 174 }} />
      </div>
      <div className="live-split">
        <div className="skeleton" style={{ height: 280 }} />
        <div className="skeleton" style={{ height: 280 }} />
      </div>
    </div>
  );
}
