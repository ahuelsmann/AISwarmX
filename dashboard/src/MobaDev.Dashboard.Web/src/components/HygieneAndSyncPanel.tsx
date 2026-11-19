import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  FolderGit2,
  ShieldCheck
} from 'lucide-react';
import type { GovernanceResponse, HygieneResponse } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';

interface HygieneAndSyncPanelProps {
  governance: GovernanceResponse;
  hygiene: HygieneResponse;
  onOpenMatrixSync: (onlyDrift: boolean) => void;
}

export function HygieneAndSyncPanel({
  governance,
  hygiene,
  onOpenMatrixSync
}: HygieneAndSyncPanelProps) {
  const driftTotal = governance.driftCount + governance.missingCount;

  return (
    <div className="side-stack">
      {/* Sync-/Drift-Kurzstatus */}
      <section className="card" aria-label="Konfigurations-Sync MOBAdev">
        <div className="card-header">
          <div className="card-title-row">
            <ShieldCheck size={17} aria-hidden="true" />
            <h2 className="card-title">Sync-Status (Lokal vs. MOBAdev)</h2>
            <GlossaryTooltip term="Drift" showIcon />
          </div>
          {driftTotal === 0 ? (
            <span className="badge badge--green">
              <CheckCircle2 size={12} aria-hidden="true" />
              Synchron
            </span>
          ) : (
            <span className="badge badge--amber tabular">
              <AlertTriangle size={12} aria-hidden="true" />
              {driftTotal} Abweichung(en)
            </span>
          )}
        </div>

        <div className="sync-summary-grid">
          <div className="stat-tile">
            <span className="stat-value tabular">{governance.syncedCount}</span>
            <span className="stat-label">Synchron</span>
          </div>
          <div className="stat-tile">
            <span className="stat-value tabular">{governance.driftCount}</span>
            <span className="stat-label">Abweichung</span>
          </div>
          <div className="stat-tile">
            <span className="stat-value tabular">{governance.missingCount}</span>
            <span className="stat-label">Fehlend</span>
          </div>
        </div>

        <button
          type="button"
          className="btn btn-link"
          onClick={() => onOpenMatrixSync(driftTotal > 0)}
        >
          {driftTotal > 0 ? 'Abweichungen prüfen' : 'Matrix & Sync öffnen'}
          <ArrowRight size={13} aria-hidden="true" />
        </button>
      </section>

      {/* Session-Hygiene */}
      <section className="card" aria-label="Session-Hygiene pro Tool">
        <div className="card-header">
          <h2 className="card-title">Session-Hygiene</h2>
          <span className="slot-label">Ziel: 1 stehender Monitor</span>
        </div>

        <div className="hygiene-list">
          {hygiene.sessions.map((session) => (
            <div key={session.tool} className="hygiene-row">
              <div className="hygiene-row-main">
                <span className="hygiene-name">{session.tool}</span>
                <span className="hygiene-sub">
                  {session.notice ? session.notice : session.monitorName}
                </span>
              </div>

              {session.status === 'ok' && (
                <span className="badge badge--green tabular">
                  <CheckCircle2 size={12} aria-hidden="true" />
                  {session.actualCount} / {session.targetCount} Soll
                </span>
              )}
              {session.status === 'inactive' && (
                <span className="badge tabular">
                  {session.actualCount} / {session.targetCount} Inaktiv
                </span>
              )}
              {session.status === 'warning' && (
                <span className="badge badge--amber tabular">
                  <AlertTriangle size={12} aria-hidden="true" />
                  {session.actualCount} / {session.targetCount} Warnung
                </span>
              )}
            </div>
          ))}
        </div>
      </section>

      {/* Git-Worktrees */}
      <section className="card" aria-label="Git-Worktrees MOBAflow">
        <div className="card-header">
          <div className="card-title-row">
            <FolderGit2 size={17} aria-hidden="true" />
            <h2 className="card-title">Git-Worktrees (MOBAflow)</h2>
            <GlossaryTooltip term="Worktree" showIcon />
          </div>
          {hygiene.staleWorktreeCount > 0 ? (
            <span className="badge badge--amber tabular">
              <AlertTriangle size={12} aria-hidden="true" />
              {hygiene.staleWorktreeCount} veraltet
            </span>
          ) : (
            <span className="badge badge--green tabular">{hygiene.worktrees.length} sauber</span>
          )}
        </div>

        {hygiene.source.status !== 'ok' && hygiene.source.notice && (
          <div className="notice-banner notice-banner--amber">
            <AlertTriangle size={14} aria-hidden="true" />
            <span>{hygiene.source.notice}</span>
          </div>
        )}

        <div className="hygiene-list">
          {hygiene.worktrees.map((wt) => (
            <div key={`${wt.folderName}-${wt.branch}`} className="hygiene-row">
              <div className="hygiene-row-main">
                <span className="hygiene-name mono" title={wt.branch}>
                  {wt.branch}
                </span>
                <span className="hygiene-sub mono" title={wt.folderName}>
                  {wt.folderName} · {wt.headShort}
                </span>
              </div>

              {wt.status === 'stale' && (
                <span className="badge badge--amber">
                  <AlertTriangle size={12} aria-hidden="true" />
                  Veraltet / Gemergt
                </span>
              )}
              {wt.status === 'active' && (
                <span className="badge badge--blue tabular">
                  Aktiv{wt.linkedPrNumber ? ` (#${wt.linkedPrNumber})` : ''}
                </span>
              )}
              {wt.status === 'main' && <span className="badge badge--green">Main</span>}
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
