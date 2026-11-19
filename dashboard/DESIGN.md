# MOBAdev Agent Network Dashboard — Architektur- & UI/UX-Konzept

**Status:** Umgesetzt (v1 — Slices 1–3 komplett)  
**Verantwortlich:** `Antigravity Monitor` (`Head of AI Strategy`, `P3`) gemeinsam mit `Head of UI/UX` (`P3`)  
**Ziel-Repository:** `ahuelsmann/MOBAdev` (`PRIVATE`, unter `dashboard/`)  
**Stack:** ASP.NET Core Minimal API (`.NET 10`) + React (`TypeScript` + `Vite`)

---

## 1. Ziel, Leitprinzipien & Abgrenzung

Das **MOBAdev Agent Network Dashboard** macht das Multi-Model-Agentennetzwerk über **Claude Code**, **ChatGPT Codex** und **Antigravity** auf einen Blick transparent: welche Monitore laufen, welche Modelle/Profile (`P1`–`P4`) aktiv sind, welche Regeln/Agenten synchron sind und welche Aufgaben/PRs in `MOBAflow` gerade in Arbeit sind.

### Vier verbindliche Leitprinzipien
1. **Zero-Duplication (keine zweite Wahrheitsquelle):** Das Dashboard pflegt keine eigene Datenbank für Regeln, Aufgaben oder Status. Es aggregiert ausschließlich **live** aus den Originalquellen (`MOBAdev`, `MOBAflow`, lokale Tool-Metadaten, GitHub). Dadurch entstehen keine Konflikte zwischen `MOBAdev` und `MOBAflow`.
2. **Strikt Read-Only („Mission Control“, keine Fernsteuerung):** Keine Schreib-, Merge-, Start- oder Prompt-Aktionen im Dashboard. Operative Steuerung, Locks und Auto-Merge verbleiben vollständig beim `Claude Monitor` (`COO`, `P1`) und im **Decision & Steering Triad**.
3. **Lokal-Only (`127.0.0.1`):** Kestrel bindet ausschließlich an Loopback (`http://127.0.0.1:5179`). Kein LAN-Binding, kein Cloud-Hosting, kein offener Netzwerk-Port.
4. **Harte Datenquellen-Whitelist (Privacy & Secrets-Schutz):** Credential-Dateien, Tokens und rohe Chat-Inhalte werden niemals eingelesen oder an das Frontend ausgeliefert.

---

## 2. Architektur & Verzeichnisstruktur

```text
MOBAdev/
└── dashboard/
    ├── DESIGN.md                           # Dieses Konzept (Slice 1)
    ├── Start-Dashboard.ps1                 # Manuelles lokales Startskript (Slice 3; kein Autostart)
    ├── MobaDev.Dashboard.slnx              # Eigenständige Solution (entkoppelt von MOBAflow)
    ├── src/
    │   ├── MobaDev.Dashboard.Api/          # ASP.NET Core Minimal API (net10.0)
    │   │   ├── Configuration/              # DashboardOptions (lokale Pfade via Env / Development.json)
    │   │   ├── Contracts/                  # Typisierte Read-Only DTOs (JSON)
    │   │   ├── Providers/                  # Isolierte Quellen-Adapter (Claude, Codex, Antigravity, Git/GH, Governance)
    │   │   └── Services/                   # Snapshot-Aggregator, TTL-Cache, optional SSE-Stream
    │   └── MobaDev.Dashboard.Web/          # React + TypeScript + Vite (statisch auslieferbar)
    └── tests/
        └── MobaDev.Dashboard.Api.Tests/    # Unit-Tests gegen synthetische Fixtures (Whitelist, Drift, Graceful Degradation)
```

### Laufzeit- & Aktualisierungsmodell
- **Quellen-Caching (TTL):**
  - Lokale Dateisystem-/SQLite-Adapter: TTL **5–10 Sekunden** (schont Festplatte & minimiert SQLite-Locks).
  - GitHub-Adapter (`gh pr list`, optional `gh project item-list`): TTL **30–60 Sekunden** mit In-Memory-Cache als Fallback bei kurzzeitiger Offline-Phase oder Rate-Limit.
- **SQLite-Zugriff:** Ausschließlich `Mode=ReadOnly;Cache=Shared` mit kurzem `Default Timeout=1` (`SQLITE_BUSY` führt zu kartenisolierter Degradation auf den letzten Cache-Stand, niemals zum Absturz).
- **Live-Updates:** `GET /api/snapshot` für initialen Render + zyklischer Refresh oder optional `GET /api/events` (Server-Sent Events) + manueller Refresh-Button im Header.
- **Konfiguration ohne harte Maschinenpfade:** Lokale Pfade (`MobaFlowRepoPath`, `MobaDevRepoPath`, Tool-Home-Verzeichnisse) werden über `appsettings.Development.json` (git-ignored) oder Umgebungsvariablen (`MOBADEV_DASHBOARD__*`) mit sinnvollen Defaults (`%USERPROFILE%`) aufgelöst. Keine Session-UUIDs oder Benutzerpfade im Git-Repository.

---

## 3. Datenquellen-Whitelist & Sicherheitsgrenzen

### Erlaubte Lese-Quellen (Whitelist)

| Bereich | Konkrete Quelle (nur lesend) | Extrahierte Metadaten |
| --- | --- | --- |
| **Claude Monitor** | `~/.claude/sessions/*.json` + nur die letzten ~20 Zeilen der aktiven `.jsonl` | Name, Status (`busy`/`idle`), Modell-ID, Zeitstempel; für Kontingent-Pause ausschließlich Regex-Match auf Limit-Reset-Zeit (keine Chat-Texte!) |
| **Codex Monitor** | `~/.codex/state_5.sqlite` (`threads WHERE archived = 0`), `~/.codex/automations/*/automation.toml` | Aktive Thread-Anzahl, Monitor-Name, Modell (`gpt-6.1-sol`), letztes Update, Automation-Status (`ACTIVE`) |
| **Antigravity Monitor** | `~/.gemini/antigravity/conversation_summaries.db` | Aktive Sessions, Titel, Status (`RUNNING`/`IDLE`), letzter Zeitstempel |
| **Governance & Regeln** | `MOBAdev` (`manifest.md`, `agents/`, `skills/`), `MOBAflow` (`AGENTS.md`, `master-coordination.instructions.md`, `docs/AI-DEVELOPMENT.md`) | Rollen, Profile (`P1`–`P4`), Modell-Zuordnung, Risikoklassen (`H`/`M`/`L`), Skills |
| **Sync / Drift-Check** | SHA-256-Vergleich (normalisierte Zeilenenden) zwischen `~/.gemini/config/{agents,rules}/`, `~/.claude/agents/` und `MOBAdev/agents/` | `Synchron`, `Abweichung / Drift` (Dateiname + kurze Diff-Kategorie) oder `Quelle fehlt` |
| **Aufgaben-Pipeline** | `gh pr list --repo ahuelsmann/MOBAflow --state open --json ...` (optional `gh project item-list 1`, sofern `read:project` vorhanden) | PR-Nummer, Titel, Branch, `Draft`/`Ready`, `Auto-Merge`, `CLEAN`/`BEHIND`, CI-/Sonar-Status, erkannte Risikoklasse |
| **Worktrees** | `git -C <MOBAflow> worktree list --porcelain` | Branch, Ordnername (gekürzt), Zuordnung zu offenem PR (`Aktiv` vs. `Veraltet / Gemergt`) |

### Explizite Denylist (strikt verboten)
Das Backend darf folgende Dateien/Inhalte **niemals** öffnen, lesen, loggen oder über HTTP ausliefern:
- Authentifizierungs- & Credential-Dateien: `auth.json`, `.credentials.json`, `oauth_creds.json`, `google_accounts.json`, `jetski-standalone-oauth-token`, `.env`, SSH-/API-Keys.
- Private Konversationsinhalte: keine User-Prompts, keine Assistant-Volltexte, keine Tool-Outputs, keine persönlichen Memory-Dateien.
- Schreibende Git-/GitHub-/Tool-Befehle jeglicher Art.

---

## 4. API-Vertrag (Read-Only HTTP Endpoints)

Alle Endpunkte liefern `application/json` und sind strikt auf `GET` begrenzt. Jeder Teilbereich enthält ein einheitliches `sourceStatus`-Feld (`ok` | `degraded` | `unavailable`, `updatedAtUtc`, optional `notice`), damit das Frontend Fehler pro Karte isolieren kann.

| Endpunkt | Zweck |
| --- | --- |
| `GET /api/health` | Backend-Laufzeitstatus, `.NET`-Version, Erreichbarkeit je Provider |
| `GET /api/snapshot` | Vollständiger aggregierter Dashboard-Zustand (Monitore, Governance, Pipeline, Hygiene) für 1-Request-Start |
| `GET /api/monitors` | Live-Status der 3 stehenden Monitore (`Claude Monitor`, `Codex Monitor`, `Antigravity Monitor`) |
| `GET /api/governance` | Rollen-, Profil- (`P1`–`P4`), Modell- und Risikoklassen-Matrix (`H`/`M`/`L`), Skills & Drift-Status |
| `GET /api/pipeline` | Offene `MOBAflow`-PRs inkl. CI-/Sonar-Status, Merge-Zustand und optionalem Kanban-Mapping |
| `GET /api/hygiene` | Session-Zähler pro Tool (Soll `= 1`) und `MOBAflow`-Worktree-Bestand |

---

## 5. UI/UX-Design & Informationsarchitektur

*(Co-Design mit `Head of UI/UX`, `P3`)*

### 5.1 Informationsarchitektur & Visuelle Hierarchie (< 3 s Systemüberblick)
Das Dashboard folgt einer ruhigen, strikt **lesenden** Cockpit-Struktur (`127.0.0.1`, keinerlei Schreib- oder Ausführungsaktionen) mit klarer Priorisierung:

- **Feste Kopfzeile (Status-Bar, ~56 px):** Titel, permanentes Badge `Nur Lesen · 127.0.0.1`, aggregierte System-Ampel (`System stabil` / `N Hinweise` / `Teilweise offline`), Datenstand (`vor 12 s` + manueller Refresh) sowie Theme-Schalter (`System / Hell / Dunkel`).
- **Primäre Navigation (2 schlanke Tabs):**
  - **`Live-Betrieb` (Standard, Above-the-Fold):**
    1. **Obere Hero-Zeile (12 Spalten):** **Monitor-Ampel (Live)** — 3 gleichbreite Statuskarten (`Claude`, `Codex`, `Antigravity`) für den sofortigen Zustandscheck in < 3 Sekunden.
    2. **Hauptbereich (7 / 5 Split, unter `1100 px` gestapelt):** Links **Aufgaben- & PR-Pipeline (`MOBAflow`)**, rechts **Hygiene & Worktrees** inkl. kompaktem **Sync-/Drift-Kurzstatus** (Klick öffnet `Matrix & Sync` vorgefiltert auf Abweichungen).
  - **`Matrix & Sync`:** Vollständige **Agenten-, Rollen-, Modell- & Regel-Matrix** mit Filter-Chips (`Profil`, `Risikoklasse`, `Nur Abweichungen`), damit die Live-Ansicht frei von Tabellenlast bleibt.

### 5.2 Die 4 Kernansichten & exakte Zustände

#### A. Monitor-Ampel (Live)
Drei identisch aufgebaute Karten für `Claude Monitor` (`P1`), `Codex Monitor` (`P2`) und `Antigravity Monitor` (`P3`). Feste Inhaltsplätze je Karte: Monitor-Name + Profil-Chip, Zustands-Badge, **aktives Modell-Badge** (Monospace) und letzter Heartbeat/Aktivität (relativ, z. B. `vor 18 s`; absoluter Zeitstempel im Tooltip).

| Zustand | Icon & Farbe | Darstellung & Zusatzinfo |
| --- | --- | --- |
| **Aktiv / Busy** | Animierter Punkt (statisch bei `reduced-motion`) · Blau | Laufende Aktivität/Heartbeat (neutral-aktiv, kein Alarm) |
| **Standby** | Häkchen / gefüllter Punkt · Grün | Bereit für Aufgaben, ruhige Darstellung |
| **Kontingent-Pause** | Sanduhr / Pause · Amber | Prominenter **Reset-Countdown** (`Reset in 01:42 h`, `tabular-nums`, Zieluhrzeit im Tooltip) |
| **Offline / Unbekannt** | Durchgestrichener Kreis / `?` · Grau | Keine laufende Session bzw. Datenquelle erkannt (gedämpft, kein Rot-Alarm) |

#### B. Agenten-, Rollen-, Modell- & Regel-Matrix
Strikte Spaltentrennung der orthogonalen Dimensionen (nie vermischt als Freitext):
- **Rolle** (fachliche Aufgabe, fett) · **Profil** (`P1`–`P4` als einheitliche Pill-Badges) · **Modell** (Monospace-Chip, trennt Profil sauber von der Modellzuordnung) · **Risikoklasse** (`H` Rot-, `M` Amber-, `L` Grün-Outline) · **Regel-/Gate-Scope** (kompakte Zuordnung, z. B. Review-/Design-Gate).
- **Drift-/Sync-Status (Lokal vs. `MOBAdev`-Backup):** Eigene Spalte plus Kopfbanner (`Synchron` ✓ Grün, `Abweichung / Drift` ⚠ Amber mit Zähler und aufklappbarer Datei-/Hash-Übersicht, `Quelle fehlt` ⊘ Grau) sowie 1-Klick-Filter `Nur Abweichungen`.

#### C. Aufgaben- & PR-Pipeline (`MOBAflow`)
- **Klare Repo-Abgrenzung:** Prominenter Header-Tag `Repository: MOBAflow` mit eigenem Rahmenakzent, visuell klar getrennt vom `MOBAdev`-Kontext (Konfigurations-/Backup-Sync), damit Produkt-PRs nie mit Repo-Konfiguration verwechselt werden.
- **Scannbare PR-Einträge:** PR-Nummer (`#123`, Monospace, externer Link) + Titel/Branch; Status-Pill (`Draft` Grau gestrichelt, `Ready` Blau, `Auto-Merge` Violett/Grün); Merge-Status (`CLEAN` ✓ Grün, `BEHIND` ↑ Amber, `BLOCKED/DIRTY` ! Rot); kompakte `CI`- & `Sonar`-Badges (✓ / ⏳ / ✕); **Risikoklasse** (`H`/`M`/`L`), **Gesperrte Bereiche** (Schloss-Icon + Tags, bei `H` hervorgehoben) sowie optionaler Projekt-Status (sofern `read:project` verfügbar).

#### D. Hygiene & Worktrees
- **Session-Hygiene pro Tool:** Kompakte Soll/Ist-Zeilen je Tool (`Ziel: 1 stehender Monitor`): `1` → Grün ✓ (`Soll`), `0` → Grau (`Inaktiv`), **`> 1` aktive, nicht archivierte Sessions** → **Amber-Warnung ⚠** mit konkreter Anzahl und Aufräum-Hinweis.
- **Git-Worktrees (`MOBAflow`):** Liste mit Branch & Pfad (Monospace), Warnungen nach oben sortiert: `Aktiv` (Blau/Grün, offener PR / aktuelle Aktivität) vs. **`Veraltet / Gemergt`** (Amber ⚠ bei gemergtem PR oder überschrittener Inaktivitätsschwelle).

### 5.3 Theme, Typografie & Barrierefreiheit
- **Light/Dark-Parität:** Systempräferenz (`prefers-color-scheme`) als Standard + manueller 3-Wege-Schalter (`System / Hell / Dunkel`); ruhige Developer-Tool-Ästhetik mit feinen Flächenabstufungen (`Canvas`, `Surface`, `Elevated`) und `1px`-Rahmen.
- **Farbenblinden-sichere Kodierung:** Status niemals nur über Farbe — immer als **Dreiklang aus Icon + deutschem Text-Label + Farbe** (WCAG AA Kontrast `>= 4,5:1` für Text, `>= 3:1` für Icons/Badges).
- **Typografie & Sprache:** System-Sans/Inter für Labels; **Monospace mit `tabular-nums`** für Modell-IDs, PR-Nummern, Branches, Pfade und Countdowns (kein Ziffernspringen). Durchgehend **deutsche UI-Labels**; Fachbegriffe (`P1–P4`, `H/M/L`, `Drift`, `Worktree`, `CLEAN/BEHIND`) erhalten per Maus **und** Tastatur erreichbare Kurz-Tooltips in einsteigerfreundlicher Sprache.

### 5.4 Lade-, Leer-, Teilfehler- & Offline-Zustände
Grundprinzip ist **kartenisolierte Fehlertoleranz (Graceful Degradation)**: Ein Fehler einer einzelnen Quelle blockiert niemals das gesamte Dashboard.
- **Laden:** Geometrie-stabile Skeletons beim Erstaufruf; bei Hintergrund-Updates bleiben vorhandene Werte stehen (nur dezenter Lade-Indikator in der Kopfzeile).
- **Leerzustände:** Ruhig und eindeutig statt leerer Boxen (z. B. `Keine offenen PRs in MOBAflow`, `Alle lokalen Regeln synchron mit MOBAdev`).
- **Teilfehler & Offline:**
  - **`gh` ohne `read:project`-Scope:** PR-Liste bleibt vollständig nutzbar; nur der Projekt-/Aufgaben-Anteil zeigt einen kompakten Info-Hinweis (`Projekt-Daten ausgeblendet: Scope read:project fehlt`).
  - **GitHub offline / Rate-Limit:** PR-Karte wechselt in gedämpften Zustand `GitHub nicht erreichbar` (bei vorhandenem Cache: letzter Stand mit Zeitstempel-Banner); alle lokalen Ansichten laufen uneingeschränkt weiter.
  - **Tool-SQLite-DB gesperrt (`SQLITE_BUSY`) oder fehlend:** Nur die betroffene Monitor-/Hygiene-Kachel zeigt `Datenbank kurz gesperrt – nächster Versuch läuft` (letzter bekannter Wert ausgegraut) bzw. `Lokale Quelle nicht gefunden` (`Offline / Unbekannt`).

---

## 6. Umsetzungsplan in kleinen Slices (in `ahuelsmann/MOBAdev`)

Die Umsetzung läuft parallel zum `MOBAflow`-Refactoring in 3 kleinen, unabhängig mergebaren Slices — ohne `MOBAflow`-Build-Locks zu belegen:

| Slice | Risikoklasse | Scope & Deliverables | Verifikation |
| --- | --- | --- | --- |
| **Slice 1** *(dieses Dokument)* | `L` | `dashboard/DESIGN.md` + Referenz in `README.md` | Maintainer-Freigabe; optionale Sicherheitsprüfung durch `P1`/`P2` |
| **Slice 2 — Backend API** | `M` | `MobaDev.Dashboard.Api` (`.NET 10` Minimal API, `127.0.0.1`), alle Read-Only-Provider, Whitelist-Filter, Drift-Hash-Check + `MobaDev.Dashboard.Api.Tests` gegen synthetische Fixtures; `.gitignore`-Ergänzung für `appsettings.Development.json` & `dist/` | `dotnet build`, `dotnet test`, Security-/Whitelist-Tests (kein Zugriff auf Denylist-Dateien), `sonar analyze secrets` |
| **Slice 3 — React Frontend & Startskript** | `M` | `MobaDev.Dashboard.Web` (`React` + `TypeScript` + `Vite`) mit den 4 Kernansichten, Light/Dark-Toggle, deutschen Tooltips, Graceful-Degradation-Zuständen + `dashboard/Start-Dashboard.ps1` | `npm run build`, TypeScript-Check (`tsc --noEmit`), UI-Test gegen Mock-/Live-API (erst nach expliziter App-Start-Freigabe durch Maintainer) |

---

## 7. Bewusste Nicht-Ziele für v1
- Keine schreibenden Aktionen (kein Merge, kein Agent-Spawning, kein Prompt-Versand, kein Worktree-Löschen aus der UI).
- Keine Persistenz historischer Metriken/Charts (erst optional als v2, falls im Alltag gewünscht).
- Kein Einfluss auf `MOBAflow`-Produktcode, `Moba.slnx` oder `MOBAflow`-CI-Pipelines.
