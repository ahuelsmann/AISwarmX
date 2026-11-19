import { useState } from 'react';
import { AlertTriangle, CheckCircle2, Slash } from 'lucide-react';
import type { GovernanceResponse } from '../types/dashboard';
import { GlossaryTooltip } from './GlossaryTooltip';

interface GovernanceMatrixViewProps {
  governance: GovernanceResponse;
  onlyDrift: boolean;
  onOnlyDriftChange: (value: boolean) => void;
}

export function GovernanceMatrixView({
  governance,
  onlyDrift,
  onOnlyDriftChange
}: GovernanceMatrixViewProps) {
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
    <div className="side-stack">
      {/* Head Sync Banner */}
      {driftTotal === 0 ? (
        <div className="notice-banner notice-banner--green">
          <CheckCircle2 size={16} aria-hidden="true" />
          <span>
            <strong>Synchron:</strong> Alle {governance.syncedCount} lokalen Agenten und Regeln stimmen mit dem MOBAdev-Backup überein.
          </span>
        </div>
      ) : (
        <div className="notice-banner notice-banner--amber">
          <AlertTriangle size={16} aria-hidden="true" />
          <span>
            <strong>Abweichung / Drift erkannt:</strong> {governance.driftCount} Abweichung(en) und {governance.missingCount} fehlende Zuordnung(en) zwischen lokalem Rechner und MOBAdev.
          </span>
        </div>
      )}

      {/* Filter Chips */}
      <div className="filter-bar" role="region" aria-label="Matrix-Filter">
        <div className="filter-group">
          <span className="filter-label">Profil:</span>
          {['ALL', 'P1', 'P2', 'P3', 'P4'].map((p) => (
            <button
              key={p}
              type="button"
              className="chip-btn"
              aria-pressed={profileFilter === p}
              onClick={() => setProfileFilter(p)}
            >
              {p === 'ALL' ? 'Alle' : p}
            </button>
          ))}
        </div>

        <div className="filter-group">
          <span className="filter-label">Risikoklasse:</span>
          {['ALL', 'H', 'M', 'L'].map((r) => (
            <button
              key={r}
              type="button"
              className="chip-btn"
              aria-pressed={riskFilter === r}
              onClick={() => setRiskFilter(r)}
            >
              {r === 'ALL' ? 'Alle' : `Klasse ${r}`}
            </button>
          ))}
        </div>

        <button
          type="button"
          className="chip-btn"
          aria-pressed={onlyDrift}
          onClick={() => onOnlyDriftChange(!onlyDrift)}
        >
          Nur Abweichungen ({driftTotal})
        </button>
      </div>

      {/* Agent & Rule Sync Table */}
      <section className="card" aria-label="Agenten- und Regel-Sync">
        <div className="card-header">
          <div className="card-title-row">
            <h2 className="card-title">Agenten &amp; Regeln (Lokal vs. MOBAdev)</h2>
            <GlossaryTooltip term="Drift" showIcon />
          </div>
          <span className="badge tabular">{filteredAgents.length} Einträge</span>
        </div>

        {filteredAgents.length === 0 ? (
          <div className="empty-state">
            <CheckCircle2 size={20} aria-hidden="true" />
            <strong>Keine Treffer für die aktiven Filter</strong>
            <span>Alle lokalen Regeln sind synchron mit MOBAdev oder durch einen Filter ausgeblendet.</span>
          </div>
        ) : (
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Name &amp; Datei</th>
                  <th>Tool / Typ</th>
                  <th>Profil</th>
                  <th>Sync-Status</th>
                  <th>Hashes (Repo / Lokal)</th>
                </tr>
              </thead>
              <tbody>
                {filteredAgents.map((agent) => (
                  <tr key={`${agent.tool}-${agent.kind}-${agent.fileName}`}>
                    <td>
                      <strong>{agent.name}</strong>
                      <div className="hygiene-sub mono">{agent.fileName}</div>
                      {agent.description && <div className="monitor-subtitle">{agent.description}</div>}
                    </td>
                    <td>
                      {agent.tool}
                      <div className="slot-label">{agent.kind === 'rule' ? 'Regel' : 'Agent'}</div>
                    </td>
                    <td>
                      {agent.profileHint ? (
                        <GlossaryTooltip term={agent.profileHint}>
                          <span className="badge">{agent.profileHint}</span>
                        </GlossaryTooltip>
                      ) : (
                        '–'
                      )}
                    </td>
                    <td>
                      <SyncBadge status={agent.syncStatus} />
                      {agent.driftSummary && <div className="slot-label">{agent.driftSummary}</div>}
                    </td>
                    <td className="mono">
                      <div>Repo: {agent.repoHash ?? '–'}</div>
                      <div>Lokal: {agent.localHash ?? '–'}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* Capability Profiles P1-P4 */}
      <section className="card" aria-label="Fähigkeitsprofile und Modelle">
        <div className="card-header">
          <h2 className="card-title">Fähigkeitsprofile (P1–P4) &amp; Modell-Zuordnung</h2>
        </div>
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                <th>Profil</th>
                <th>Bezeichnung</th>
                <th>Aktive Modelle &amp; Clients</th>
                <th>Stärken &amp; typische Aufgaben</th>
              </tr>
            </thead>
            <tbody>
              {governance.profiles.map((p) => (
                <tr key={p.profile}>
                  <td>
                    <GlossaryTooltip term={p.profile} showIcon>
                      <span className="badge badge--blue">{p.profile}</span>
                    </GlossaryTooltip>
                  </td>
                  <td>
                    <strong>{p.name}</strong>
                  </td>
                  <td>
                    <span className="model-chip mono">{p.models}</span>
                  </td>
                  <td>{p.strengths}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {/* Roles & Risk Classes */}
      <div className="live-split">
        <section className="card" aria-label="Rollenmatrix">
          <div className="card-header">
            <h2 className="card-title">Rollen &amp; Zuständigkeiten</h2>
          </div>
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Rolle</th>
                  <th>Profil</th>
                  <th>Verantwortung</th>
                </tr>
              </thead>
              <tbody>
                {filteredRoles.map((role) => (
                  <tr key={role.role}>
                    <td>
                      <strong>{role.role}</strong>
                    </td>
                    <td>
                      <span className="badge">{role.profiles}</span>
                    </td>
                    <td>{role.responsibility}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <div className="side-stack">
          <section className="card" aria-label="Risikoklassen H M L">
            <div className="card-header">
              <h2 className="card-title">Risikoklassen (H / M / L)</h2>
            </div>
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Klasse</th>
                    <th>MOBAflow-Bereiche</th>
                    <th>Prüf- &amp; Team-Regel</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredRiskClasses.map((rc) => (
                    <tr key={rc.code}>
                      <td>
                        <GlossaryTooltip term={rc.code} showIcon>
                          <span
                            className={`badge badge--outline ${
                              rc.code === 'H'
                                ? 'badge--red'
                                : rc.code === 'M'
                                  ? 'badge--amber'
                                  : 'badge--green'
                            }`}
                          >
                            {rc.code} · {rc.name}
                          </span>
                        </GlossaryTooltip>
                      </td>
                      <td>{rc.areas}</td>
                      <td>{rc.requiredDiversity}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="card" aria-label="Versionierte Skills in MOBAdev">
            <div className="card-header">
              <h2 className="card-title">Skills ({governance.skills.length})</h2>
            </div>
            <div className="hygiene-list">
              {governance.skills.map((skill) => (
                <div key={skill.name} className="hygiene-row">
                  <div className="hygiene-row-main">
                    <span className="hygiene-name">{skill.name}</span>
                    <span className="hygiene-sub">{skill.description}</span>
                  </div>
                  <span className="model-chip mono">{skill.relativePath}</span>
                </div>
              ))}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}

function SyncBadge({ status }: { status: string }) {
  switch (status) {
    case 'synced':
      return (
        <span className="badge badge--green">
          <CheckCircle2 size={12} aria-hidden="true" />
          Synchron
        </span>
      );
    case 'drift':
      return (
        <span className="badge badge--amber">
          <AlertTriangle size={12} aria-hidden="true" />
          Abweichung / Drift
        </span>
      );
    default:
      return (
        <span className="badge">
          <Slash size={12} aria-hidden="true" />
          Quelle fehlt
        </span>
      );
  }
}
