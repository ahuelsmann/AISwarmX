import { AlertTriangle, CheckCircle2, Clock, Hourglass, Slash } from 'lucide-react';
import { formatAbsoluteDe, formatRelativeTimeDe } from '../api/dashboardClient';
import type { MonitorInfo } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';

interface MonitorCardsProps {
  monitors: MonitorInfo[];
  nowMs: number;
}

export function MonitorCards({ monitors, nowMs }: MonitorCardsProps) {
  return (
    <section aria-label="Monitor-Ampel (Live)">
      <div className="monitors-grid">
        {monitors.map((monitor) => (
          <article key={monitor.id} className="card monitor-card">
            <div className="monitor-meta">
              <div className="card-header">
                <div className="card-title-row">
                  <h2 className="card-title">{monitor.displayName}</h2>
                  <GlossaryTooltip term={monitor.profile} showIcon>
                    <span className="badge">{monitor.profile}</span>
                  </GlossaryTooltip>
                </div>
                <MonitorStateBadge state={monitor.state} />
              </div>

              <div className="monitor-subtitle">
                {monitor.role} · {monitor.tool}
              </div>

              {monitor.state === 'rate_limited' && monitor.rateLimitResetLabel && (
                <div className="rate-limit-box tabular">
                  <span>Kontingent-Pause</span>
                  <span>Reset: {monitor.rateLimitResetLabel}</span>
                </div>
              )}

              {monitor.source.status !== 'ok' && monitor.source.notice && (
                <div className="notice-banner notice-banner--amber">
                  <AlertTriangle size={14} aria-hidden="true" />
                  <span>{monitor.source.notice}</span>
                </div>
              )}
            </div>

            <div className="monitor-slots">
              <div className="slot-row">
                <span className="slot-label">Aktives Modell</span>
                <span className="model-chip mono">
                  {monitor.model ? monitor.model : 'nicht erkannt'}
                </span>
              </div>

              <div className="slot-row">
                <span className="slot-label">Letzte Aktivität</span>
                <span
                  className="tabular"
                  title={formatAbsoluteDe(monitor.lastActivityUtc)}
                >
                  {formatRelativeTimeDe(monitor.lastActivityUtc, nowMs)}
                </span>
              </div>

              {monitor.automationStatus && (
                <div className="slot-row">
                  <span className="slot-label">Automation</span>
                  <span className="model-chip mono">{monitor.automationStatus}</span>
                </div>
              )}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

function MonitorStateBadge({ state }: { state: string }) {
  switch (state) {
    case 'busy':
      return (
        <span className="badge badge--blue">
          <span className="pulse-dot" aria-hidden="true" />
          Aktiv / Busy
        </span>
      );
    case 'standby':
      return (
        <span className="badge badge--green">
          <CheckCircle2 size={13} aria-hidden="true" />
          Standby
        </span>
      );
    case 'rate_limited':
      return (
        <span className="badge badge--amber">
          <Hourglass size={13} aria-hidden="true" />
          Kontingent-Pause
        </span>
      );
    default:
      return (
        <span className="badge">
          {state === 'offline' ? <Slash size={13} aria-hidden="true" /> : <Clock size={13} aria-hidden="true" />}
          Offline / Unbekannt
        </span>
      );
  }
}
