Hier ist die überarbeitete Fassung der relevanten KI-Anforderungen. Der MVP-Teil bleibt dabei bewusst unverändert: **Die erste Version des Missionssystems funktioniert ohne KI.** Die KI-Erweiterung ist als späteres, optionales Modul gedacht.

# Spacecraft – Überarbeitung der KI-Anforderungen für das Missionssystem

## 1. Ziel dieser Überarbeitung

Dieses Dokument ersetzt beziehungsweise erweitert die KI-bezogenen Abschnitte des bisherigen Missionssystem-Dokuments.

Die Grundidee bleibt:

* Es gibt einen Missionscomputer.
* Spieler können Missionen annehmen.
* Spieler können Missionen für andere Spieler erstellen.
* Belohnungen für ausführende Spieler werden in einem Missions-Teleporter beziehungsweise Belohnungsdepot hinterlegt.
* Auftraggeber erhalten zusätzlich eine systemische Belohnung aus dem Spiel heraus.
* Die erste Version des Missionssystems soll ohne KI funktionieren.

Neu präzisiert wird:

> Die KI soll später nicht nur vorhandene Missionen textlich verbessern, sondern auch vollständig eigene Missionen, Missionsketten, Dialoge, Funksprüche, Ereignisse und erzählerische Aufträge frei erzeugen können.

Die KI soll also perspektivisch als kreativer Missions- und Dialoggenerator dienen.

---

# 2. Grundsätzliche Einordnung der KI-Nutzung

Die KI ist ein späteres Zusatzsystem und nicht Teil des ersten MVP.

Das Missionssystem muss in der ersten Version vollständig ohne KI funktionieren.

Die KI wird später als optionales Feature ergänzt, das ein Serverbetreiber aktivieren oder deaktivieren kann.

Wichtig:

> Die KI darf kreativ frei Missionen und Dialoge entwerfen.
> Die spielmechanische Ausführung muss aber weiterhin vom autoritativen Hauptserver überprüfbar und kontrollierbar sein.

Das bedeutet:

* Die KI darf Missionen frei vorschlagen.
* Die KI darf vollständige Missionsgeschichten schreiben.
* Die KI darf Dialoge komplett selbst erzeugen.
* Die KI darf Missionsketten entwerfen.
* Die KI darf NPC-ähnliche Auftraggeber simulieren.
* Die KI darf Funksprüche, Logbucheinträge, Warnungen und Abschlussdialoge schreiben.
* Die KI darf aber nicht direkt Items erzeugen, Belohnungen auszahlen oder Spielregeln umgehen.

Die kreative Erzeugung liegt beim KI-Backend.
Die autoritative Prüfung und Umsetzung liegt beim Hauptspielserver.

---

# 3. Separates Python-KI-Backend

Für KI-Funktionen soll es ein eigenes dediziertes Backend geben.

## 3.1 Grundanforderung

Das KI-Backend soll separat vom Hauptspielserver entwickelt werden.

Vorgabe:

* Programmiersprache: **Python**
* eigener Dienst / eigenes Backend
* optional aktivierbar
* nicht zwingend für den Spielbetrieb
* später separat definierbar
* kann lokal, auf demselben Server oder extern betrieben werden

Der Hauptspielserver bleibt in C#/.NET.
Das KI-Backend ist ein zusätzlicher Python-Dienst für generative Inhalte.

---

## 3.2 Verhältnis zwischen Hauptserver und KI-Backend

Der Hauptserver fragt das KI-Backend an, wenn KI-Funktionen aktiviert sind.

Beispiele:

* „Erzeuge eine neue Mission für diesen Planeten.“
* „Erzeuge eine Missionskette für diesen Spieler.“
* „Schreibe einen Dialog für einen Auftraggeber.“
* „Erzeuge einen Funkspruch beim Start der Mission.“
* „Erzeuge einen Abschlussdialog.“
* „Erzeuge eine Folge-Mission passend zu diesem Ereignis.“

Das KI-Backend antwortet mit generierten Inhalten.

Der Hauptserver prüft anschließend:

* Ist die Mission regelkonform?
* Sind die Ziele technisch messbar?
* Sind die Belohnungen erlaubt?
* Ist der Zielort erreichbar?
* Existieren die referenzierten Ressourcen, Planeten oder Objekte?
* Ist die Mission für den aktuellen Spieler oder Serverzustand sinnvoll?
* Muss ein Admin die Mission freigeben?
* Darf die Mission veröffentlicht werden?

---

# 4. KI darf vollständige Missionen erzeugen

Die KI soll später nicht nur Texte verbessern, sondern vollständige Missionen erzeugen können.

## 4.1 Mögliche KI-generierte Missionen

Die KI kann beispielsweise erzeugen:

* einfache Sammelmissionen
* Abbaumissionen
* Liefermissionen
* Reisemissionen
* Scanmissionen
* Baumissionen
* Erkundungsmissionen
* Rettungsmissionen
* Reparaturmissionen
* Handelsmissionen
* Forschungsmissionen
* mehrstufige Missionsketten
* erzählerische Mini-Abenteuer
* serverweite Ereignismissionen

---

## 4.2 Beispiel: komplett KI-generierte Mission

Der Hauptserver sendet Kontext an das KI-Backend:

```text
Spieler hat gerade einen Eisplaneten entdeckt.
Der Planet enthält Lithium, Eis und seltene blaue Kristalle.
Der Spieler besitzt Sauerstofftank II, aber keinen Thermoschutz.
Auf dem Server fehlen aktuell Energiezellen.
```

Die KI könnte daraus eine Mission erzeugen:

**Titel:**
„Die blauen Adern von Veyra-9“

**Beschreibung:**
„Unter der gefrorenen Oberfläche von Veyra-9 wurden ungewöhnlich reine Kristalladern entdeckt. Die Energieabteilung glaubt, daraus bessere Energiezellen herstellen zu können. Die Kälte ist gefährlich, aber ein kurzer Einsatz könnte ausreichen.“

**Ziele:**

* Reise nach Veyra-9.
* Baue 12 blaue Kristallblöcke ab.
* Kehre zum Missionscomputer zurück.
* Liefere die Kristalle ab.

**Empfohlene Ausrüstung:**

* Sauerstofftank II
* mindestens Basis-Titanbohrer
* Kälteschutz empfohlen, aber nicht zwingend

**Belohnung:**

* Energiezellen II
* Missionspunkte
* Fortschritt in Kristallverarbeitung

Der Hauptserver muss diese Mission anschließend in ein technisch validierbares Missionsmodell übersetzen oder von der KI bereits in einem strukturierten Format erhalten.

---

# 5. KI darf vollständige Dialoge frei gestalten

Die KI soll später komplette Dialoge frei erzeugen können.

Das betrifft nicht nur eine Verbesserung vorhandener Texte, sondern die eigenständige Erstellung von Dialogen, Funksprüchen und erzählerischen Momenten.

## 5.1 Mögliche KI-generierte Dialogformen

Die KI kann erzeugen:

* Auftraggeber-Dialoge
* Funksprüche beim Missionsstart
* Warnungen während einer Mission
* Kommentare bei Missionsfortschritt
* Dialoge beim Fund eines besonderen Ortes
* Abschlussdialoge
* Dankesnachrichten
* kurze Streitgespräche zwischen NPCs
* Logbucheinträge
* Stationsdurchsagen
* Notrufe
* Nachrichten von automatischen Systemen
* Textfragmente aus verlassenen Stationen

---

## 5.2 Beispiel: KI-generierter Missionsdialog

Mission: Spieler soll ein beschädigtes Signal auf einem Asteroiden untersuchen.

Die KI könnte erzeugen:

**Missionsstart:**
„Hier spricht Außenposten Luma. Wir empfangen seit drei Stunden ein wiederkehrendes Signal aus dem Asteroidenfeld. Es ist zu regelmäßig für natürliches Rauschen und zu schwach für eine normale Funkquelle. Bitte untersuche das.“

**Beim Erreichen des Zielgebiets:**
„Das Signal wird stärker. Es kommt nicht von der Oberfläche. Irgendetwas sendet aus dem Inneren des Asteroiden.“

**Beim Fund:**
„Das ist kein normales Wrackteil. Die Struktur wirkt absichtlich verborgen. Sichere die Daten, aber bleib nicht länger dort als nötig.“

**Abschluss:**
„Daten empfangen. Das war kein gewöhnliches Signal. Wir müssen das analysieren. Gute Arbeit, Pilot.“

Diese Dialoge werden komplett von der KI erstellt, müssen aber zum Spielzustand und zur Mission passen.

---

# 6. KI-generierte Missionen müssen technisch prüfbar bleiben

Auch wenn die KI Missionen frei entwirft, müssen Missionen vom Spielserver überprüfbar bleiben.

Deshalb darf eine KI-Mission nicht nur aus Freitext bestehen.

Eine KI-Mission muss aus zwei Ebenen bestehen:

## 6.1 Kreative Ebene

Diese enthält:

* Titel
* Beschreibung
* Dialoge
* Funksprüche
* Atmosphäre
* erzählerische Begründung
* Auftraggebertext
* Abschlussnachricht
* optionale Zwischenereignisse

## 6.2 Regel-Ebene

Diese enthält strukturierte, prüfbare Daten:

* Missionstyp
* Zielort
* Zielressource
* Zielmenge
* benötigter Scan
* benötigter Blockabbau
* benötigtes Item
* Abgabeort
* Zielkoordinaten
* erlaubte Belohnung
* Zeitlimit
* Schwierigkeit
* Abschlussbedingungen

Nur wenn die Regel-Ebene gültig ist, kann die Mission im Spiel veröffentlicht werden.

---

# 7. Empfohlenes Prinzip: KI erzeugt MissionPlan, Server validiert Mission

Die KI sollte später nicht nur Fließtext zurückgeben, sondern einen Missionsvorschlag in einer strukturierten Form.

Begriffsvorschlag:

**MissionPlan**

Ein MissionPlan enthält:

* erzählerische Inhalte
* technische Missionsziele
* Belohnungsvorschlag
* Schwierigkeit
* empfohlene Ausrüstung
* Dialogbausteine
* mögliche Folgeereignisse

Der Hauptserver wandelt einen gültigen MissionPlan in eine echte Mission um.

Ablauf:

1. Hauptserver sammelt Spielkontext.
2. Hauptserver fragt Python-KI-Backend an.
3. KI erzeugt MissionPlan.
4. Hauptserver prüft MissionPlan.
5. Ungültige Teile werden verworfen oder ersetzt.
6. Optional wird ein Admin oder Spieler um Freigabe gebeten.
7. Hauptserver veröffentlicht die Mission.
8. Hauptserver validiert später die Erfüllung.
9. Hauptserver zahlt Belohnungen aus.

---

# 8. KI kann Missionsketten erzeugen

Später soll die KI nicht nur Einzelmissionen erzeugen können, sondern auch mehrstufige Missionsketten.

## 8.1 Beispiel für Missionskette

### Mission 1: Signal entdecken

* Reise zu einem Asteroidenfeld.
* Scanne ein unbekanntes Signal.

### Mission 2: Zugang schaffen

* Baue dich in einen Asteroiden hinein.
* Finde eine versteckte Kammer.

### Mission 3: Daten bergen

* Scanne ein altes Datenmodul.
* Bringe die Daten zum Schiff zurück.

### Mission 4: Folgeauftrag

* Das Datenmodul verweist auf einen Eisplaneten.
* Reise dorthin und finde den nächsten Hinweis.

Die KI kann diese Kette erzählerisch frei gestalten.
Der Server muss aber jede einzelne Stufe als prüfbare Teilmission abbilden.

---

# 9. KI kann dynamische Auftraggeber simulieren

Die KI kann später Auftraggeber erzeugen, die nicht zwingend als vollwertige NPCs in der Welt existieren müssen.

Beispiele:

* Stationsmechaniker
* Forschungsteam
* automatische Notfallboje
* Minenkolonie
* Händler
* unbekannter Funksender
* beschädigte KI einer verlassenen Station
* Außenpostenleiter
* Frachtercrew

Die KI kann diesen Auftraggebern Namen, Tonfall und kurze Dialoge geben.

Beispiel:

**Auftraggeber:**
Mira Voss, Leiterin eines kleinen Außenpostens

**Tonfall:**
praktisch, müde, aber freundlich

**Dialogstil:**
kurze, direkte Sätze, leicht sarkastisch

Die KI kann daraus eigenständig passende Missionsdialoge erzeugen.

---

# 10. KI kann server- und weltabhängig arbeiten

Das KI-Backend soll später Kontext vom Hauptserver erhalten können.

Möglicher Kontext:

* bekannte Sternensysteme
* entdeckte Planeten
* vorhandene Rohstoffe
* aktive Außenposten
* aktuelle Serverprobleme
* häufig genutzte Ressourcen
* Fortschritt einzelner Spieler
* Fortschritt der Gruppe
* vorhandene Schiffsmodule
* verfügbare Werkzeuge
* freigeschaltete Technologien
* Gefahren auf Planeten
* bereits abgeschlossene Missionen
* aktive Spieleraufträge

Dadurch kann die KI Missionen erzeugen, die zur aktuellen Welt passen.

Beispiele:

* Wenn viele Spieler Titan benötigen, entstehen Titan-bezogene Aufträge.
* Wenn ein neuer Planet entdeckt wurde, entstehen Erkundungsmissionen.
* Wenn ein Außenposten gebaut wurde, entstehen Versorgungsmissionen.
* Wenn ein Spieler einen neuen Scanner besitzt, entstehen Scanmissionen.
* Wenn ein Server lange keine Missionen hatte, erzeugt die KI neue Aufträge.

---

# 11. Konfigurierbarkeit der KI

Das KI-System muss für Serverbetreiber steuerbar sein.

## 11.1 Grundoptionen

Serverbetreiber sollen einstellen können:

* KI komplett deaktiviert
* KI nur für Dialoge
* KI nur für Missionstexte
* KI für vollständige Missionen erlaubt
* KI für Missionsketten erlaubt
* KI für serverweite Ereignisse erlaubt
* KI-Missionen benötigen Admin-Freigabe
* KI-Missionen dürfen automatisch veröffentlicht werden
* maximale KI-Anfragen pro Stunde
* maximale aktive KI-Missionen
* maximale Länge von Dialogen
* erlaubte Missionsarten
* verbotene Missionsarten
* erlaubte Belohnungsklassen
* Tonalität
* Sprache
* Altersfreigabe / Jugendschutz
* Logging aktivieren
* Debugmodus für KI-Antworten

---

## 11.2 Sicherheitsstufen

Empfohlene KI-Stufen:

### Stufe 0: KI deaktiviert

Keine KI-Funktionen.

### Stufe 1: KI nur für Texte

KI erzeugt nur Titel, Beschreibungen und Dialoge für bereits vorhandene Missionen.

### Stufe 2: KI schlägt Missionen vor

KI erzeugt komplette Missionsvorschläge.
Ein Spieler oder Admin muss sie freigeben.

### Stufe 3: KI erzeugt automatisch Missionen

KI erzeugt vollständige Missionen, die nach Serverprüfung automatisch veröffentlicht werden können.

### Stufe 4: KI erzeugt Missionsketten und Ereignisse

KI erzeugt mehrstufige Missionen, Dialoge und dynamische Ereignisse.
Diese Stufe sollte besonders stark konfigurierbar und begrenzt sein.

---

# 12. Belohnungen bei KI-Missionen

Bei KI-generierten Missionen muss klar geregelt sein, woher Belohnungen kommen.

## 12.1 KI-Missionen ohne Spielerauftraggeber

Wenn die KI eine Mission erzeugt, gibt es keinen normalen Spieler-Auftraggeber.

Dann kommt die Belohnung für den ausführenden Spieler direkt aus dem Spielsystem.

Beispiele:

* Ressourcen
* Energiezellen
* Missionspunkte
* Ruf
* Bauplanfragmente
* Scanner-Daten
* seltene Komponenten

Diese Belohnungen müssen vom Hauptserver begrenzt und validiert werden.

Die KI darf Belohnungen vorschlagen, aber nicht frei beliebige Belohnungen erzeugen.

---

## 12.2 KI-Missionen mit simuliertem Auftraggeber

Die KI kann einen erzählerischen Auftraggeber erzeugen, zum Beispiel eine Forschungsstation oder eine Notfallboje.

Dieser Auftraggeber ist aber spielmechanisch ein Systemauftraggeber.

Das bedeutet:

* Die Belohnung kommt aus dem Spielsystem.
* Kein echter Spieler zahlt die Belohnung.
* Der Hauptserver kontrolliert die Belohnung.
* Die KI schreibt nur die erzählerische Begründung.

---

# 13. Missbrauchsschutz bei KI-Missionen

KI-generierte Missionen können problematisch werden, wenn sie unkontrolliert Belohnungen oder Ziele erzeugen.

Deshalb braucht es Schutzmechanismen.

## 13.1 Risiken

Mögliche Risiken:

* KI erzeugt zu hohe Belohnungen.
* KI erzeugt unerfüllbare Missionen.
* KI verweist auf nicht existierende Orte.
* KI erzeugt zu viele Missionen.
* KI erzeugt unpassende Inhalte.
* KI erzeugt Missionen, die Spieler blockieren.
* KI erzeugt Texte, die nicht zum Spielstil passen.
* KI erzeugt Missionen, die Exploits ermöglichen.
* KI erzeugt wiederholt sehr ähnliche Missionen.

---

## 13.2 Gegenmaßnahmen

Empfohlene Maßnahmen:

* Hauptserver validiert alle Missionen.
* Belohnungen werden aus erlaubten Tabellen berechnet.
* KI darf keine finalen Itemmengen frei festlegen.
* KI darf nur aus erlaubten Zieltypen wählen.
* KI erhält nur den notwendigen Kontext.
* KI-Antworten werden auf Format geprüft.
* KI-Missionen können Admin-Freigabe benötigen.
* Server begrenzt Anzahl aktiver KI-Missionen.
* Server begrenzt Belohnungen pro Zeit.
* Server prüft, ob Zielorte existieren.
* Server prüft, ob Ressourcen erreichbar sind.
* Server prüft, ob Missionen lösbar sind.
* KI-Texte können moderiert oder gefiltert werden.
* Fehlerhafte KI-Antworten werden verworfen.
* Es gibt Fallback-Missionen ohne KI.

---

# 14. Fallback ohne KI

Auch nach Einführung des KI-Backends muss das Missionssystem ohne KI funktionieren.

Wenn das KI-Backend nicht erreichbar ist:

* Missionscomputer bleibt nutzbar.
* Spieler-Missionen funktionieren weiter.
* Spielgenerierte Standardmissionen funktionieren weiter.
* Bestehende KI-Missionen bleiben spielbar, sofern sie bereits gespeichert wurden.
* Neue KI-Missionen werden nicht erzeugt.
* Standardtexte werden verwendet.
* Der Server stürzt nicht ab.
* Admin-Oberfläche zeigt eine Warnung.
* Fehler werden geloggt.

---

# 15. KI-Unterstützung im Missionseditor

Wenn KI aktiviert ist, kann der Spieler-Missionseditor zusätzliche Funktionen anbieten.

## 15.1 Text- und Dialogfunktionen

Der Spieler kann optional verwenden:

* Titel generieren
* Beschreibung generieren
* Dialog generieren
* Funkspruch generieren
* Abschlussnachricht generieren
* Auftraggeber-Persona generieren
* Missionshintergrund generieren

---

## 15.2 Vollständige Missionsvorschläge

In späteren Versionen kann der Spieler auch sagen:

> „Erstelle mir eine Mission für andere Spieler.“

Dann kann die KI einen vollständigen Vorschlag erzeugen.

Der Spieler kann diesen Vorschlag prüfen, anpassen und veröffentlichen.

Beispiel:

Spielerwunsch:

```text
Ich brauche Hilfe beim Aufbau eines Außenpostens auf einem Eisplaneten.
```

KI erzeugt:

* Missionstitel
* Beschreibung
* Zielort
* benötigte Liefergüter
* Bauziel
* empfohlene Ausrüstung
* Belohnungsvorschlag
* Abschlussdialog

Der Spieler muss anschließend bestätigen, welche Belohnung er in das Depot legt.
Die hinterlegte Belohnung kommt weiterhin vom Spieler.
Die systemische Auftraggeber-Belohnung kommt weiterhin aus dem Spiel.

---

# 16. Späteres KI-MVP

Der erste Missions-MVP bleibt ohne KI.

Ein späteres KI-MVP kann in Stufen umgesetzt werden.

## 16.1 KI-MVP Stufe 1

* Python-KI-Backend als separater Dienst
* Server kann KI-Backend optional anfragen
* KI erzeugt Titel
* KI erzeugt Beschreibung
* KI erzeugt Abschlussnachricht
* Fallback auf Standardtexte
* Admin kann KI aktivieren/deaktivieren

---

## 16.2 KI-MVP Stufe 2

* KI erzeugt vollständige Missionsvorschläge
* Missionen enthalten kreative Texte und strukturierte Ziele
* Hauptserver validiert die Missionsregeln
* Admin oder Spieler muss Missionen freigeben
* KI darf keine Belohnung final bestimmen

---

## 16.3 KI-MVP Stufe 3

* KI erzeugt eigenständig vollständige Missionen
* KI kann simulierte Auftraggeber erstellen
* KI kann Dialoge für Missionsstart, Fortschritt und Abschluss erzeugen
* Hauptserver validiert und veröffentlicht Missionen nach Regeln
* Serverbetreiber kann automatische Veröffentlichung aktivieren oder deaktivieren

---

## 16.4 KI-MVP Stufe 4

* KI erzeugt Missionsketten
* KI erzeugt dynamische Ereignisse
* KI reagiert auf Serverzustand und Weltfortschritt
* KI erzeugt wiederkehrende Auftraggeber oder Fraktionen
* Admin-Regeln und Limits begrenzen Umfang und Belohnungen

---

# 17. Nicht-Ziele für die erste Version

Für die erste Version des Missionssystems bleibt unverändert:

Nicht notwendig sind:

* KI-generierte Missionen
* KI-generierte Dialoge
* KI-generierte Missionsketten
* Python-KI-Backend
* dynamische KI-Auftraggeber
* automatische KI-Ereignisse
* freie KI-Missionsgestaltung
* komplexe Dialogbäume
* KI-Moderation
* KI-Konfiguration

Diese Funktionen sind für spätere Erweiterungsstufen vorgesehen.

---

# 18. Aktualisierte Zusammenfassung

Das Missionssystem soll zunächst ohne KI funktionieren. Spieler können über den Missionscomputer Missionen annehmen und über einen strukturierten Missionseditor Aufträge für andere Spieler erstellen. Belohnungen für ausführende Spieler werden in einem Missions-Teleporter hinterlegt. Auftraggeber erhalten zusätzlich eine systemische Belohnung aus dem Spiel.

Später soll ein separates Python-KI-Backend ergänzt werden. Diese KI soll nicht nur vorhandene Missionstexte verbessern, sondern vollständige Missionen, Missionsketten, Dialoge, Funksprüche, Auftraggeber-Personas und Ereignisse frei erzeugen können.

Trotz dieser kreativen Freiheit bleibt der Hauptspielserver autoritativ. KI-generierte Missionen müssen in eine strukturierte, serverseitig prüfbare Form gebracht werden. Die KI kann kreative Inhalte und Missionsvorschläge liefern, aber der Hauptserver entscheidet, ob eine Mission gültig ist, ob sie veröffentlicht wird, ob sie erfüllt wurde und welche Belohnungen ausgezahlt werden.

Die wichtigste Leitlinie lautet:

> Die KI darf kreativ frei gestalten.
> Der Hauptserver muss spielmechanisch alles prüfen, begrenzen und speichern.
> Die erste Version funktioniert vollständig ohne KI.
> KI wird später als optionales, konfigurierbares Python-Backend ergänzt.
