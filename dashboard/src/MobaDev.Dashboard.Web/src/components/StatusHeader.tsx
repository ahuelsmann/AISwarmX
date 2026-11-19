import {
  AlertTriangle,
  CheckCircle2,
  Eye,
  Monitor,
  Moon,
  RefreshCw,
  Sun,
  WifiOff
} from 'lucide-react';
import { formatAbsoluteDe, formatRelativeTimeDe } from '../api/dashboardClient';
import type { DashboardTab, ThemePreference } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';

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
  const renderOverallBadge = () => {
    if (!overallStatus) {
      return <span className="badge">Verbinde…</span>;
    }

    if (overallStatus === 'degraded') {
      return (
        <span className="badge badge--amber">
          <WifiOff size={13} aria-hidden="true" />
          Teilweise offline{warningCount > 0 ? ` · ${warningCount} Hinweise` : ''}
        </span>
      );
    }

    if (overallStatus === 'warnings' || warningCount > 0) {
      return (
        <span className="badge badge--amber">
          <AlertTriangle size={13} aria-hidden="true" />
          {warningCount === 1 ? '1 Hinweis' : `${warningCount} Hinweise`}
        </span>
      );
    }

    return (
      <span className="badge badge--green">
        <CheckCircle2 size={13} aria-hidden="true" />
        System stabil
      </span>
    );
  };

  return (
    <header className="status-header">
      <div className="status-bar">
        <div className="brand-group">
          <h1 className="brand-title">MOBAdev Agent Network</h1>
          <GlossaryTooltip term="Nur Lesen" showIcon>
            <span className="badge badge--blue">
              <Eye size={12} aria-hidden="true" />
              Nur Lesen · 127.0.0.1
            </span>
          </GlossaryTooltip>
          {renderOverallBadge()}
        </div>

        <div className="header-controls">
          <span
            className="freshness-group tabular"
            title={generatedAtUtc ? `Aktualisiert: ${formatAbsoluteDe(generatedAtUtc)}` : undefined}
          >
            Stand: {formatRelativeTimeDe(generatedAtUtc, nowMs)}
          </span>

          <button
            type="button"
            className="btn"
            onClick={onRefresh}
            disabled={refreshing}
            aria-label="Dashboard aktualisieren"
          >
            <RefreshCw size={13} className={refreshing ? 'spin' : undefined} aria-hidden="true" />
            Aktualisieren
          </button>

          <div className="segmented-control" role="group" aria-label="Farbschema wählen">
            <button
              type="button"
              className="segmented-btn"
              aria-pressed={themePreference === 'system'}
              onClick={() => onThemeChange('system')}
            >
              <Monitor size={12} aria-hidden="true" />
              System
            </button>
            <button
              type="button"
              className="segmented-btn"
              aria-pressed={themePreference === 'light'}
              onClick={() => onThemeChange('light')}
            >
              <Sun size={12} aria-hidden="true" />
              Hell
            </button>
            <button
              type="button"
              className="segmented-btn"
              aria-pressed={themePreference === 'dark'}
              onClick={() => onThemeChange('dark')}
            >
              <Moon size={12} aria-hidden="true" />
              Dunkel
            </button>
          </div>
        </div>
      </div>

      <nav className="tab-strip" role="tablist" aria-label="Dashboard-Ansichten">
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'live'}
          className="tab-btn"
          onClick={() => onTabChange('live')}
        >
          Live-Betrieb
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'matrix'}
          className="tab-btn"
          onClick={() => onTabChange('matrix')}
        >
          Matrix &amp; Sync
          {driftBadgeCount > 0 && (
            <span className="badge badge--amber tabular">{driftBadgeCount}</span>
          )}
        </button>
      </nav>
    </header>
  );
}
