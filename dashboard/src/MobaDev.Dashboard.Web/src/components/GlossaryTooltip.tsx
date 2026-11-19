import { useId, useState, type ReactNode } from 'react';
import { HelpCircle } from 'lucide-react';

const GLOSSARY_ENTRIES: Record<string, string> = {
  P1: 'Profil P1 (Tiefe, Architektur & Sicherheit): Claude Opus 5.5 für komplexe Architektur, Z21-Sicherheit und verpflichtende Endprüfung.',
  P2: 'Profil P2 (Repo, Build & Faktenpräzision): GPT-6 Astra für Solution-Build-Graph (Moba.slnx), CI/Sonar und technische Gegenchecks.',
  P3: 'Profil P3 (Systemkontext, Produkt & UI/UX): Gemini 4 Argon für große Kontexte, UI/UX-Design (WinUI/MAUI) und verständliche Doku.',
  P4: 'Profil P4 (Kadenz & schnelle Lieferung): Schnelle Modelle (Fable / Hydrogen) für überschaubare Routine-Aufgaben.',
  H: 'Risikoklasse H (Hoch): Sicherheitskritisch (Backend, Z21, ESP32). Verlangt Design-Go und Prüfung durch zwei Modellfamilien (P1 + P2).',
  M: 'Risikoklasse M (Mittel): Produkt-Features, UI, Audio oder MOBAnews. Verlangt Begleit-Review plus UI/UX-Check bei Oberflächenthemen.',
  L: 'Risikoklasse L (Niedrig): Dokumentation, Wiki oder kleine Fixes ohne Verhaltensänderung.',
  Drift: 'Abweichung zwischen der lokalen Konfigurationsdatei auf diesem Rechner und der gesicherten Version im Repository MOBAdev.',
  Worktree: 'Separates Git-Arbeitsverzeichnis für einen isolierten Branch, damit Aufgaben sauber getrennt bleiben.',
  CLEAN: 'Branch lässt sich ohne Konflikte und ohne fehlende main-Commits direkt zusammenführen.',
  BEHIND: 'Der Branch liegt hinter dem aktuellen main-Stand und sollte vor dem Merge aktualisiert werden.',
  'Auto-Merge': 'GitHub führt den PR automatisch zusammen, sobald alle erforderlichen Prüfungen grün sind.',
  'Nur Lesen': 'Dieses Cockpit zeigt ausschließlich aktuelle Zustände an und startet oder ändert nichts.'
};

interface GlossaryTooltipProps {
  term: string;
  children?: ReactNode;
  showIcon?: boolean;
}

export function GlossaryTooltip({ term, children, showIcon = false }: GlossaryTooltipProps) {
  const [open, setOpen] = useState(false);
  const tooltipId = useId();
  const description = GLOSSARY_ENTRIES[term];

  if (!description) {
    return <>{children ?? term}</>;
  }

  return (
    <span
      className="tooltip-wrap"
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
    >
      <button
        type="button"
        className="tooltip-trigger"
        aria-describedby={open ? tooltipId : undefined}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            setOpen(false);
          }
        }}
      >
        {children ?? term}
        {showIcon && <HelpCircle size={12} aria-hidden="true" />}
      </button>
      {open && (
        <span id={tooltipId} role="tooltip" className="tooltip-bubble">
          {description}
        </span>
      )}
    </span>
  );
}
