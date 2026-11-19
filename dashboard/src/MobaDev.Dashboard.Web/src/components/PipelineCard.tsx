import {
  AlertTriangle,
  ArrowUp,
  Check,
  Clock,
  ExternalLink,
  GitPullRequest,
  Info,
  Lock,
  WifiOff,
  X
} from 'lucide-react';
import { formatAbsoluteDe, formatRelativeTimeDe } from '../api/dashboardClient';
import type { PipelineResponse, PullRequestInfo } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';

interface PipelineCardProps {
  pipeline: PipelineResponse;
  nowMs: number;
}

export function PipelineCard({ pipeline, nowMs }: PipelineCardProps) {
  return (
    <section className="card card--mobaflow" aria-label="Aufgaben- und PR-Pipeline MOBAflow">
      <div className="card-header">
        <div className="card-title-row">
          <GitPullRequest size={17} aria-hidden="true" />
          <h2 className="card-title">Aufgaben- &amp; PR-Pipeline</h2>
          <span className="badge badge--blue mono">Repository: {pipeline.repository}</span>
        </div>
        <span className="badge tabular">{pipeline.pullRequests.length} offen</span>
      </div>

      {pipeline.source.status === 'degraded' && (
        <div className="notice-banner notice-banner--amber">
          <AlertTriangle size={15} aria-hidden="true" />
          <span>
            {pipeline.source.notice ?? 'GitHub nicht erreichbar – zeige letzten Cache-Stand.'}{' '}
            <span className="tabular">({formatRelativeTimeDe(pipeline.source.updatedAtUtc, nowMs)})</span>
          </span>
        </div>
      )}

      {pipeline.source.status === 'unavailable' && (
        <div className="empty-state">
          <WifiOff size={20} aria-hidden="true" />
          <strong>GitHub nicht erreichbar</strong>
          <span>{pipeline.source.notice ?? 'PR-Daten konnten aktuell nicht geladen werden.'}</span>
        </div>
      )}

      {!pipeline.projectBoardAvailable && pipeline.projectBoardNotice && (
        <div className="notice-banner">
          <Info size={15} aria-hidden="true" />
          <span>{pipeline.projectBoardNotice}</span>
        </div>
      )}

      {pipeline.source.status !== 'unavailable' && pipeline.pullRequests.length === 0 && (
        <div className="empty-state">
          <Check size={20} aria-hidden="true" />
          <strong>Keine offenen PRs in MOBAflow</strong>
          <span>Alle Änderungen sind aktuell auf main integriert.</span>
        </div>
      )}

      {pipeline.pullRequests.length > 0 && (
        <div className="pr-list">
          {pipeline.pullRequests.map((pr) => (
            <PullRequestRow key={pr.number} pr={pr} nowMs={nowMs} />
          ))}
        </div>
      )}
    </section>
  );
}

function PullRequestRow({ pr, nowMs }: { pr: PullRequestInfo; nowMs: number }) {
  return (
    <div className="pr-item">
      <div className="pr-top">
        <div className="pr-title-wrap">
          <a
            href={pr.url}
            target="_blank"
            rel="noreferrer"
            className="pr-number mono"
            title={`PR #${pr.number} auf GitHub öffnen`}
          >
            #{pr.number} <ExternalLink size={11} aria-hidden="true" />
          </a>
          <span className="pr-title">{pr.title}</span>
        </div>

        <div className="pr-meta-row">
          <PrStateBadge stateLabel={pr.stateLabel} />
          <MergeStateBadge mergeState={pr.mergeState} />
          <RiskClassBadge riskClass={pr.riskClass} />
        </div>
      </div>

      <div className="pr-meta-row">
        <span className="model-chip mono" title={pr.branch}>
          {pr.branch}
        </span>

        <CheckBadge label="CI" status={pr.ciStatus} />
        <CheckBadge label="Sonar" status={pr.sonarStatus} />

        {pr.projectStatus && <span className="badge badge--blue">Kanban: {pr.projectStatus}</span>}

        <span
          className="slot-label tabular"
          title={formatAbsoluteDe(pr.updatedAtUtc)}
        >
          Update {formatRelativeTimeDe(pr.updatedAtUtc, nowMs)}
        </span>
      </div>

      {pr.lockedAreas.length > 0 && (
        <div className="lock-tags">
          <Lock size={12} aria-hidden="true" />
          <span className="slot-label">Gesperrte Bereiche:</span>
          {pr.lockedAreas.map((area) => (
            <span
              key={area}
              className={`badge mono ${pr.riskClass === 'H' ? 'badge--red' : ''}`}
            >
              {area}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

function PrStateBadge({ stateLabel }: { stateLabel: string }) {
  if (stateLabel === 'Auto-Merge') {
    return (
      <GlossaryTooltip term="Auto-Merge">
        <span className="badge badge--violet">Auto-Merge</span>
      </GlossaryTooltip>
    );
  }
  if (stateLabel === 'Draft') {
    return <span className="badge badge--dashed">Draft</span>;
  }
  return <span className="badge badge--blue">Ready</span>;
}

function MergeStateBadge({ mergeState }: { mergeState: string }) {
  switch (mergeState.toUpperCase()) {
    case 'CLEAN':
      return (
        <GlossaryTooltip term="CLEAN">
          <span className="badge badge--green">
            <Check size={12} aria-hidden="true" />
            CLEAN
          </span>
        </GlossaryTooltip>
      );
    case 'BEHIND':
      return (
        <GlossaryTooltip term="BEHIND">
          <span className="badge badge--amber">
            <ArrowUp size={12} aria-hidden="true" />
            BEHIND
          </span>
        </GlossaryTooltip>
      );
    case 'DIRTY':
    case 'BLOCKED':
      return (
        <span className="badge badge--red">
          <AlertTriangle size={12} aria-hidden="true" />
          {mergeState.toUpperCase()}
        </span>
      );
    default:
      return <span className="badge">{mergeState}</span>;
  }
}

function CheckBadge({ label, status }: { label: string; status: string }) {
  switch (status) {
    case 'pass':
      return (
        <span className="badge badge--green">
          <Check size={11} aria-hidden="true" />
          {label}
        </span>
      );
    case 'pending':
      return (
        <span className="badge badge--amber">
          <Clock size={11} aria-hidden="true" />
          {label}
        </span>
      );
    case 'fail':
      return (
        <span className="badge badge--red">
          <X size={11} aria-hidden="true" />
          {label}
        </span>
      );
    default:
      return <span className="badge">{label}: –</span>;
  }
}

function RiskClassBadge({ riskClass }: { riskClass: string }) {
  if (riskClass === 'H') {
    return (
      <GlossaryTooltip term="H" showIcon>
        <span className="badge badge--red badge--outline">Klasse H</span>
      </GlossaryTooltip>
    );
  }
  if (riskClass === 'M') {
    return (
      <GlossaryTooltip term="M" showIcon>
        <span className="badge badge--amber badge--outline">Klasse M</span>
      </GlossaryTooltip>
    );
  }
  if (riskClass === 'L') {
    return (
      <GlossaryTooltip term="L" showIcon>
        <span className="badge badge--green badge--outline">Klasse L</span>
      </GlossaryTooltip>
    );
  }
  return <span className="badge badge--outline">Klasse ?</span>;
}
