# Hand-off: Restrukturierung unseres autonomen KI-Agenten-Setups ("State of the AI")

## 1. Einleitung & Der Geist unserer Unterhaltung
**Wo wir stehen:** 
Wir haben in letzter Zeit ein konzeptionell herausragendes KI-System aufgebaut. Durch den Einsatz von Frontier-Modellen (Opus 5.5, Gemini 4 Argon, etc.), die völlig autark über CLI-Schnittstellen kommunizieren und mit festen Scrum-Rollen (PM, Dev, QA/Reviewer) agieren, haben wir ein massives technisches Potenzial erschlossen. Die reine Entwicklungsleistung der KIs ist hervorragend.

**Warum wir trotzdem umbauen müssen:**
Trotz der hohen Leistungsfähigkeit hat sich das System zuletzt zäh angefühlt. Wir haben zu viele parallele Baustellen (Features vs. Refactoring), riesige Pull Requests, die lange dauern, und späte Erkenntnisse in Reviews ("Warum hat das Team das nicht gleich berücksichtigt?"). 
*Die entscheidende Erkenntnis:* Wir haben kein technisches Limit erreicht, sondern ein organisatorisches. Unsere KI-Agenten replizieren aktuell 1:1 die Anti-Pattern isoliert arbeitender, menschlicher Entwicklerteams. Sie rennen los, schreiben Code in Silos und werfen das Ergebnis am Ende über den Zaun.

**Das Ziel der Umstrukturierung:**
Wir wechseln von isolierter Quantität zu ausgerichteter Qualität. Wir implementieren den "Shift Left" – Probleme werden durch vorausschauende Planung gelöst, bevor eine Zeile Code geschrieben wird. Zudem lösen wir den zentralen PR-Stau durch Trunk-Based Development, Micro-Batches und eine strikte "Hol-Schuld" der Agenten ab.

---

## 2. Die neuen Architektur-Direktiven (Rollen-Updates)

Diese Regeln müssen zwingend als neue System-Prompts / Instruktionen in den jeweiligen Agenten-Rollen verankert werden.

### A. Für den Projektmanager (PM) Agenten
Der PM ist ab sofort der Torwächter für den "Blast Radius" (Auswirkungsbereich) und den Fokus des Systems.
*   **Micro-Batches:** Große Aufgaben (Tickets) sind strikt in minimale, isolierte Arbeitspakete zu zerlegen (z.B. nur die DB-Tabelle, dann nur der API-Endpunkt). Lange laufende Feature-Branches sind zu unterbinden.
*   **Domain Isolation (Locking):** Refactorings und Feature-Implementierungen dürfen *niemals* unkontrolliert parallel in derselben Code-Domäne stattfinden. Wird ein Modul (z.B. `UserAuth`) refactored, verhängt der PM einen "Lock" für dieses Modul. Andere Agenten dürfen dort in dieser Zeit keine neuen Features ansetzen.
*   **WIP-Limits (Work in Progress):** Harte Limitierung der gleichzeitig offenen Tasks im CLI-System, um Kontext-Chaos zu vermeiden.

### B. Für die Entwickler (Dev) Agenten
Entwickler-Agenten dürfen nicht mehr einfach "drauflos tippen".
*   **Shift-Left (Design-First):** Bevor Code-Dateien angefasst werden, generiert der Dev-Agent zwingend ein kurzes Design-Doc / einen Implementierungsplan auf der CLI. Dieser muss vom Reviewer/Tech-Lead konzeptionell mit einem "Go" freigegeben werden.
*   **Die Rebase-Pflicht (Hol-Schuld):** Bevor Code zur Prüfung eingereicht oder gepusht wird, MUSS der Dev-Agent ein `git pull --rebase origin main` (oder auf den aktuellen Production-Tag) ausführen.
*   **Lokale Konfliktlösung:** Merge-Konflikte sind *lokal* auf dem Branch vom Dev-Agenten selbst zu lösen und zu testen, bevor der Code das System weiter durchläuft.

### C. Für den Reviewer / QA Agenten
Der PR als bloßes Abfangbecken am Ende des Prozesses hat ausgedient.
*   **Test-Driven Development (TDD) als Gesetz:** Bevor das "Go" für die Entwicklung eines neuen Features erteilt wird, schreibt der QA-Agent die fehlschlagenden Unit-/Integrationstests.
*   **Synchrones Pair-Programming:** Der Reviewer prüft den Code iterativ während der Entstehung auf dem Branch, nicht erst ganz am Ende in einem massiven PR.
*   **Der Weg zu "YOLO auf Main":** Pull Requests werden künftig nur noch als Container für die CI/CD-Pipeline genutzt. Wenn der Reviewer sein "LGTM" (Looks Good To Me) gibt UND alle Tests grün sind, löst das System einen **Auto-Merge** aus.

---

## 3. Technische Absicherung auf CLI-Ebene

Um dieses autonome System abzusichern, ohne die Freiheit der Agenten einzuschränken, ziehen wir folgende unsichtbare Leitplanken in unsere Automatisierungsskripte (bzw. CLI-Wrapper) ein:

1.  **Circuit Breaker (Endlosschleifen-Schutz):** Autonome Diskussionen zwischen Agenten (z.B. Dev und Reviewer über einen Bug) erhalten ein hartes Limit (z.B. max. 10 Tool-/CLI-Aufrufe). Wird es überschritten, eskaliert das System an den Menschen, um API-Kosten und Deadlocks zu verhindern.
2.  **Audit-Logging:** Jeder CLI-Befehl wird mit Timestamp und Agenten-ID geloggt, um nachträgliches Debugging ("Wer hat diese Datei wann gelöscht?") zu vereinfachen.
3.  **Feature Toggles (optional, aber empfohlen):** Code fließt künftig kontinuierlich (Continuous Integration). Neue Features sollten hinter Feature-Flags versteckt werden, die standardmäßig auf `false` stehen.

---

## 4. Nächste Schritte zur Implementierung
1.  **System-Prompts aktualisieren:** Die PM-, Dev- und QA-Agenten bekommen diesen Text (oder die Essenz daraus) als Kontext-Update.
2.  **CLI-Wrapper anpassen:** Einbau der Rebase-Pflicht-Kontrolle und des Auto-Merge-Triggers in unsere Skripte.
3.  **Testlauf:** Ein einzelnes, unkritisches Ticket mit dem neuen Micro-Batching und Design-Doc-Workflow komplett durchlaufen lassen.
