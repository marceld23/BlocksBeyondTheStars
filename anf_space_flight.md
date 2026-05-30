Hier ist das neue große Anforderungsdokument für die neuen Punkte: **Schleusen/Docking, persönliche Landeplattformen, freier Raumflug, Schiffskampf, PvP-Regeln, NPC-Raumschiffe und Gegner auf Planeten**.

# Spacecraft – Zusatzanforderungen: Schiffsdocking, persönliche Landezonen, freier Raumflug, Raumkampf und Gegner-Systeme

## 1. Ziel dieses Dokuments

Dieses Dokument beschreibt neue Erweiterungsanforderungen für **Spacecraft**.

Die neuen Anforderungen betreffen:

1. Blaupausen für **Schleusen / Docking-Module** am Raumschiff
2. persönliche **Landeplattformen / Landebereiche** für Spieler auf Servern
3. die Möglichkeit, dass Spieler-Raumschiffe miteinander **andocken**
4. das Betreten anderer Spielerschiffe nach beidseitiger Bestätigung
5. freier Flug im Weltraum
6. Dogfights zwischen Raumschiffen
7. Schiffskanonen und andere Schiffsbewaffnung über Blaupausen
8. Kampf gegen Asteroiden, NPC-Schiffe, UFOs, Raumkreuzer oder andere Gegner im freien Raum
9. PvP-Schiffskampf, sofern der Server das erlaubt
10. NPC-Gegner im freien Raum abhängig von Welteinstellungen
11. Monster und Gegner auf Planeten abhängig von Server-Settings
12. klare Konzept- und Machbarkeitsprüfung vor Umsetzung des vollständigen Raumkampf-Systems

Diese Anforderungen ergänzen die bisherigen Spacecraft-Konzepte: eigenes Raumschiff, prozedurale Planeten, Crafting, Server-Settings, Kreativmodus, Überlebensmodus, Admin-Weltenbeschreibung, Missionen, KI-Backend und selbst hostbare Server.

---

# 2. Grundidee der Erweiterung

Das Raumschiff soll noch stärker zum zentralen Spielobjekt werden.

Bisher ist das Raumschiff:

* Zuhause
* Lager
* Werkstatt
* Medbay
* Crafting-Ort
* Sternenkarten-Zentrale
* Transportmittel
* Fortschrittssystem

Mit dieser Erweiterung wird das Raumschiff zusätzlich:

* andockbares Objekt
* sozialer Treffpunkt
* Multiplayer-Verbindungsraum
* frei fliegbares Raumfahrzeug
* kampffähige Einheit
* PvE- oder PvP-Kampfplattform
* mobiler Außenposten im freien Raum

Das Ziel ist:

> Spieler sollen ihr Raumschiff nicht nur als Basis benutzen, sondern es auch mit anderen Schiffen verbinden, im Raum fliegen, aufrüsten, verteidigen und in Kämpfen einsetzen können.

---

# 3. Persönliche Landeplattformen und Landebereiche

## 3.1 Grundidee

Auf Multiplayer-Servern soll jeder neue Spieler automatisch einen eigenen **Landebereich** beziehungsweise eine eigene **Landeplattform** erhalten.

Dieser Landebereich ist der feste Ankunftsort des Spielers auf einem Planeten oder an einem bestimmten Zielort.

Die Idee:

> Jeder Spieler hat auf einem Planeten seinen eigenen sicheren Landeplatz. Wenn er diesen Planeten anfliegt, landet sein Schiff immer auf seinem zugewiesenen Landebereich.

---

## 3.2 Zweck der persönlichen Landezonen

Persönliche Landebereiche sollen mehrere Probleme lösen:

* Schiffe landen nicht übereinander.
* Neue Spieler bekommen einen sicheren Startbereich.
* Spieler finden ihren eigenen Landeplatz wieder.
* Server können mehrere Spieler auf demselben Planeten unterstützen.
* Landepunkte bleiben geordnet.
* Spieler können ihren eigenen Bereich später ausbauen.
* Fremde Spieler blockieren nicht automatisch das eigene Schiff.

---

## 3.3 Automatische Zuweisung

Wenn ein neuer Spieler auf einem Server startet, kann der Server automatisch einen Landebereich erzeugen.

Möglicher Ablauf:

1. Spieler betritt Server zum ersten Mal.
2. Server weist dem Spieler ein Startschiff zu.
3. Server erzeugt oder reserviert einen persönlichen Landebereich.
4. Landebereich wird mit Spieler-ID verknüpft.
5. Schiff landet beim ersten Besuch dort.
6. Bei späteren Landungen auf diesem Planeten wird derselbe Landebereich genutzt.

---

## 3.4 Landebereiche pro Planet

Es soll geprüft werden, ob Spieler auf jedem Planeten eigene Landebereiche erhalten.

Mögliche Varianten:

### Variante A: Persönlicher Landebereich nur im Startsystem

Einfacher Einstieg.

* Jeder Spieler hat im Startsystem einen sicheren Landeplatz.
* Auf fremden Planeten werden Landezonen dynamisch gewählt.

### Variante B: Persönlicher Landebereich pro besuchtem Planeten

Jeder Spieler erhält beim ersten Besuch eines Planeten einen eigenen Landebereich.

Vorteil:

* sehr geordnet
* gute Wiedererkennbarkeit

Nachteil:

* mehr gespeicherte Landepunkte
* mehr Weltverwaltung

### Variante C: Gemeinsame öffentliche Landeplätze plus private Landezonen

Es gibt öffentliche Landeplätze und optional private Spielerbereiche.

Vorteil:

* gut für Raumstationen, Städte und Serverprojekte
* flexibel

Empfehlung:

Für die erste Version sollte mindestens ein persönlicher Start-Landebereich pro Spieler existieren. Später kann das auf weitere Planeten erweitert werden.

---

## 3.5 Persistenz

Persönliche Landebereiche müssen gespeichert werden.

Zu speichern sind:

* Spieler-ID
* Planet oder Ort
* Koordinaten
* Landebereichsgröße
* zugehöriges Schiff
* Besitzrechte
* Schutzstatus
* Ausbaustatus
* eventuell gebaute Strukturen
* Docking-Status, falls dort Docking möglich ist

---

## 3.6 Schutz persönlicher Landezonen

Der Admin soll einstellen können, ob persönliche Landezonen geschützt sind.

Mögliche Einstellung:

```text
PersonalLandingZoneProtection = Aus / Nur Startzone / Alle persönlichen Landezonen
```

Schutz kann bedeuten:

* andere Spieler können dort nicht bauen
* andere Spieler können dort nicht abbauen
* fremde Schiffe können dort nicht landen
* PvP ist dort deaktiviert
* Monster spawnen dort nicht
* Schiffe können dort nicht beschädigt werden

---

# 4. Schleusen- und Docking-Module

## 4.1 Grundidee

Spieler sollen eine Blaupause für eine **Schleuse** beziehungsweise ein **Docking-Modul** freischalten können.

Dieses Modul kann an das eigene Raumschiff angebaut werden.

Funktion:

* ermöglicht Andocken an andere Spieler-Schiffe
* ermöglicht Andocken an Raumstationen
* ermöglicht Betreten anderer Schiffe nach Freigabe
* ermöglicht später Handel, gemeinsame Missionen oder Crew-Besuche
* kann als Zugangspunkt zwischen Schiffen dienen

---

## 4.2 Schleuse als Schiffserweiterung

Die Schleuse soll als baubares Schiffmodul existieren.

Beispiel:

**Blaupause: Standard-Schleuse I**

Freischaltung:

* Werkstatt-Erweiterung I
* 2 Datenfragmente
* 10 Aluminiumplatten
* 5 Dichtungsmodule

Bau benötigt:

* 20 Aluminiumplatten
* 10 Titanplatten
* 8 Druckdichtungen
* 6 Kupferkabel
* 2 Luftdruckregler
* 1 Türmodul
* 1 Docking-Kontrollmodul

Funktion:

* ermöglicht einfaches Andocken
* erzeugt einen luftdichten Übergang
* verbindet zwei Schiffe über eine Schleuse

---

## 4.3 Erweiterte Docking-Module

Später können verschiedene Stufen existieren.

### Schleuse I – Standard-Docking

* Andocken an andere kleine Schiffe
* manuelle Bestätigung notwendig
* einfacher Luftdruckausgleich

### Schleuse II – Erweiterte Schleuse

* schnelleres Andocken
* bessere Sicherheit
* Zugriffsbeschränkungen
* Verbindung zu Stationen

### Fracht-Dockingmodul

* Materialtransfer zwischen Schiffen
* Frachtraum-zu-Frachtraum-Verbindung
* nur mit Berechtigung

### Crew-Dockingmodul

* Spieler können Schiffe betreten
* Gästerechte möglich
* Zugriffsbereiche definierbar

### Militärisches Dockingmodul

* nur auf Servern mit Kampf-/PvP-Einstellungen
* könnte Entermanöver oder erzwungenes Andocken ermöglichen
* nicht für erste Version empfohlen

---

# 5. Spieler-Schiff-zu-Schiff-Docking

## 5.1 Grundidee

Spieler sollen ihre Raumschiffe miteinander verbinden können.

Ein Spieler kann mit seinem Schiff an das Schiff eines anderen Spielers andocken.

Wichtig:

> Andocken zwischen Spielerschiffen benötigt grundsätzlich Zustimmung beider Seiten.

---

## 5.2 Ablauf eines normalen Docking-Vorgangs

1. Spieler A fliegt mit seinem Schiff in die Nähe von Spieler B.
2. Beide Schiffe besitzen ein kompatibles Docking-Modul.
3. Spieler A sendet eine Docking-Anfrage.
4. Spieler B erhält eine Anfrage.
5. Spieler B bestätigt oder lehnt ab.
6. Bei Bestätigung richtet der Server beide Schiffe aus.
7. Schleusen verbinden sich.
8. Luftdruckausgleich wird simuliert oder angezeigt.
9. Türen werden freigegeben.
10. Spieler können zwischen den Schiffen wechseln.

---

## 5.3 Docking-Bestätigung

Der angefragte Spieler muss klar entscheiden können:

```text
Spieler Marcel möchte an dein Schiff andocken.
[Annehmen] [Ablehnen]
```

Optional kann er sehen:

* Spielername
* Schiffname
* Entfernung
* Docking-Modul
* Server-Regeln
* PvP-Status
* angebotene Zugriffsrechte

---

## 5.4 Zugriffsrechte nach Docking

Nach erfolgreichem Docking darf der Gast nicht automatisch alles tun.

Es braucht Rechte.

Mögliche Zugriffsrechte:

* nur Durchgang
* Cockpit gesperrt
* Frachtraum gesperrt
* Werkstatt nutzbar
* Medbay nutzbar
* Missionsterminal nutzbar
* Container gesperrt
* private Räume gesperrt
* Schiffserweiterungen nicht veränderbar
* keine Blöcke abbauen
* keine Blöcke platzieren
* keine Schiffsmodule demontieren

Empfehlung:

Standardmäßig dürfen Gäste:

* das andere Schiff betreten
* sich in freigegebenen Bereichen bewegen
* mit dem Besitzer interagieren
* eventuell Medbay oder Werkbank nutzen, falls erlaubt

Standardmäßig dürfen Gäste nicht:

* Frachtraum plündern
* Schiff umbauen
* Schiff steuern
* Module entfernen
* Systeme sabotieren

---

## 5.5 Abdocken

Beide Spieler sollen den Docking-Zustand beenden können.

Mögliche Regeln:

* Besitzer eines Schiffs kann Abdocken anfordern.
* Beide Seiten können Abdocken bestätigen.
* In Notfällen kann ein Spieler Not-Abdocken auslösen.
* Not-Abdocken kann Schäden verursachen, falls im Kampfmodus.
* Im friedlichen Modus sollte Abdocken ohne Schaden möglich sein.

---

## 5.6 Docking im Kampf

Docking während Kampf oder PvP sollte gesondert geregelt werden.

Mögliche Server-Setting:

```text
CombatDocking = Aus / Nur freiwillig / Erzwungen erlaubt
```

Empfehlung:

Für erste Version:

> Docking ist nur freiwillig und nicht als Enterkampf-System gedacht.

Erzwungenes Docking oder Entern kann später als fortgeschrittenes PvP-Feature geprüft werden.

---

# 6. Freier Raumflug

## 6.1 Grundidee

Zusätzlich zur Sternenkarte und zum Landen auf Planeten soll es einen Modus geben, in dem Spieler ihr Raumschiff frei im Weltraum fliegen können.

Dieser Modus ermöglicht:

* freie Bewegung zwischen Objekten in einem lokalen Raumgebiet
* Flug um Asteroiden
* Annäherung an andere Schiffe
* Andocken
* Dogfights
* Begegnungen mit NPC-Schiffen
* Raumkampf
* Erkundung von Wracks, Stationen oder Signalen

---

## 6.2 Unterschied zwischen Sternenkarte und freiem Raumflug

### Sternenkarte

Dient für:

* Reise zwischen Sternensystemen
* Auswahl von Planeten
* Scannen
* Hyperraumreisen
* strategische Navigation

### Freier Raumflug

Dient für:

* lokale Bewegung im Raum
* Kampf
* Dogfights
* Annäherung an Stationen
* Andocken
* Asteroidenfelder
* NPC-Begegnungen
* Weltraum-Ereignisse

---

## 6.3 Flugräume / Rauminstanzen

Da das gesamte Universum nicht durchgehend physisch simuliert werden muss, kann freier Raumflug in lokalen Rauminstanzen stattfinden.

Beispiele:

* Orbit eines Planeten
* Asteroidenfeld
* Bereich um eine Raumstation
* Wrackfeld
* Kampfzone
* Rendezvous-Zone zwischen Spielerschiffen
* Zufallsbegegnung im freien Raum

Diese Instanzen können beim Betreten geladen und beim Verlassen entladen werden.

---

## 6.4 Anforderungen an freien Raumflug

Der freie Raumflug soll ermöglichen:

* Schiff steuern
* beschleunigen
* bremsen
* wenden
* ausweichen
* Objekte anvisieren
* Docking-Ziele anfliegen
* Asteroiden umgehen
* Waffen abfeuern, falls erlaubt
* Treffer erhalten, falls Kampf aktiv
* NPC-Schiffe verfolgen oder meiden
* Flucht in Hyperraum oder zur Sternenkarte, falls erlaubt

---

## 6.5 Konzeptprüfung vor Umsetzung

Freier Raumflug und Dogfights sind große Gameplay-Erweiterungen.

Deshalb soll vor vollständiger Umsetzung geprüft werden:

* Wie komplex soll die Steuerung sein?
* Arcade oder physiknah?
* First-Person-Cockpit, Third-Person-Schiffskamera oder beides?
* Wie groß sind Rauminstanzen?
* Wie viele Schiffe können gleichzeitig aktiv sein?
* Wie funktioniert Multiplayer-Synchronisation?
* Wie wird Andocken präzise gelöst?
* Wie werden Treffer serverseitig berechnet?
* Wie viel Raumkampf passt zum Kernspiel?
* Wie stark beeinflusst Raumkampf Progression und Crafting?
* Soll Raumkampf optional pro Server komplett abschaltbar sein?

---

# 7. Schiffsbewaffnung und Kanonen-Blaupausen

## 7.1 Grundidee

Spieler sollen Blaupausen für Schiffsbewaffnung freischalten können.

Beispiel:

* einfache Schiffskanone
* Laserkanone
* Plasmageschütz
* Asteroidenbrecher
* Verteidigungsturm
* Raketenwerfer, falls später gewünscht
* EMP-Gerät
* Schildgenerator
* Zielsystem

Diese Waffen sollen am Schiff angebaut werden.

---

## 7.2 Serverabhängigkeit

Schiffsbewaffnung darf nur funktionieren, wenn der Server Waffen und Raumkampf erlaubt.

Mögliche Einstellungen:

```text
ShipWeapons = Aus / Nur gegen NPCs / PvP erlaubt / Alle erlaubt
```

### Aus

* keine Schiffskanonen
* keine Schiffskampf-Rezepte
* keine Waffenwirkung im Raum

### Nur gegen NPCs

* Spieler können NPC-Schiffe oder feindliche Aliens bekämpfen
* kein Schaden gegen Spielerschiffe

### PvP erlaubt

* Spieler können andere Spielerschiffe beschädigen, wenn PvP aktiv ist

### Alle erlaubt

* PvE und PvP vollständig aktiv, abhängig von weiteren Schutzregeln

---

## 7.3 Beispiel-Blaupause: Schiffskanone I

Freischaltung:

* Werkstatt II
* 3 Datenfragmente
* 10 Titanplatten
* 2 Energielinsen
* Server erlaubt Schiffsbewaffnung

Bau benötigt:

* 30 Titanplatten
* 12 Kupferkabel
* 4 Energieknoten
* 2 Zielmodule
* 1 rotierendes Geschützlager
* 2 Energiezellen II

Funktion:

* einfache Energiekanone
* geeignet gegen kleine Asteroiden und schwache NPC-Drohnen
* geringer Schaden
* geringer Energieverbrauch

---

## 7.4 Beispiel-Blaupause: Asteroidenbrecher

Funktion:

* baut kleine Asteroiden im freien Raum ab oder zerstört sie
* kann Rohstoffe aus Asteroiden lösen
* primär Werkzeug, nicht Waffe
* auf friedlichen Servern eventuell erlaubt, obwohl Waffen deaktiviert sind

Wichtig:

Es sollte zwischen Schiffswerkzeugen und Schiffskampfwaffen unterschieden werden.

---

## 7.5 Beispiel-Blaupause: Laserkanone II

Funktion:

* stärkeres Geschütz
* geeignet gegen NPC-Jäger
* kann bei PvP auch Spielerschiffe beschädigen, wenn erlaubt
* verbraucht Energiezellen oder Schiffenergie

---

## 7.6 Energie und Munition

Schiffsw Waffen sollen Ressourcen verbrauchen.

Mögliche Verbrauchsarten:

* Schiffenergie
* Energiezellen
* Munitionsmodule
* Plasmakerne
* Kühlzeit
* Überhitzung

Dadurch bleibt Kampf mit Crafting und Ressourcen verbunden.

---

# 8. Raumkampf und Dogfights

## 8.1 Grundidee

In freiem Raumflug sollen Dogfights möglich sein.

Dogfights können stattfinden gegen:

* NPC-Jäger
* Alien-UFOs
* Drohnen
* Piratenschiffe
* große Raumkreuzer
* andere Spieler, wenn PvP aktiv ist

---

## 8.2 Raumkampf als optionales Server-Feature

Nicht jeder Server soll Raumkampf haben müssen.

Mögliche Einstellung:

```text
SpaceCombat = Aus / PvE / PvP / PvE und PvP
```

### Aus

* kein Raumkampf
* freie Raumbewegung kann trotzdem für Erkundung und Andocken existieren

### PvE

* Kampf gegen NPC-Schiffe
* keine Kämpfe gegen Spieler

### PvP

* Kämpfe zwischen Spielern möglich
* NPC-Gegner optional

### PvE und PvP

* vollständiger Kampfmodus

---

## 8.3 Dogfight-Anforderungen

Dogfights benötigen:

* Zielerfassung
* Schiffsgeschwindigkeit
* Wendigkeit
* Beschleunigung
* Ausweichmanöver
* Trefferzonen oder vereinfachte Trefferberechnung
* Schilde oder Hülle
* Energieverbrauch
* Waffenabklingzeiten
* Schadensfeedback
* Fluchtmöglichkeit
* Servervalidierung

---

## 8.4 Schiffsschaden

Schiffe können unterschiedliche Schadensarten haben.

Mögliche Systeme:

* Schild
* Hülle
* Module
* Antrieb
* Waffen
* Schleuse
* Frachtraum
* Energieversorgung

Für eine frühe Version sollte Schiffsschaden einfach sein:

* Schildpunkte
* Hüllenpunkte
* zerstörte oder deaktivierte Module optional später

---

## 8.5 Konsequenzen bei Schiffsniederlage

Wenn ein Schiff im Raumkampf besiegt wird, muss klar sein, was passiert.

Mögliche Optionen:

### Friedliche/leichte Server

* Schiff wird kampfunfähig
* Spieler respawnt im Heiltank
* Schiff kehrt automatisch zur letzten Landebasis zurück
* kein permanenter Verlust

### Normale Survival-Server

* Schiff wird beschädigt
* Reparatur nötig
* Frachtraum kann teilweise verloren gehen
* Bergungsmission entsteht

### Harte PvP-Server

* Schiff kann schwer beschädigt oder geplündert werden
* nur wenn Server das ausdrücklich erlaubt

Empfehlung:

Standardmäßig keine permanente Schiffzerstörung.

Das Raumschiff ist zu zentral für den Fortschritt, um leicht endgültig verloren zu gehen.

---

# 9. PvP-Schiffskampf

## 9.1 Grundidee

Spieler sollen andere Spielerschiffe nur dann angreifen können, wenn der Server PvP und Schiffskampf erlaubt.

Wichtige Regel:

> PvP-Schiffskampf ist nie automatisch aktiv. Er muss über Server-Settings ausdrücklich erlaubt werden.

---

## 9.2 PvP-Abhängigkeiten

PvP-Schiffskampf darf nur aktiv sein, wenn mehrere Bedingungen erfüllt sind:

* PlayerDamage ist aktiv oder PvP-Modus erlaubt.
* ShipWeapons sind aktiv.
* SpaceCombat erlaubt PvP.
* ShipDamageByPlayers erlaubt Schaden an Spielerschiffen.
* Schutzgebiet-Regeln blockieren den Angriff nicht.
* beide Spieler sind nicht in geschützter Startzone.
* Server erlaubt Kampf in dieser Rauminstanz.

---

## 9.3 PvP-Schutzregeln

Admins sollen Schutzregeln definieren können:

* keine Angriffe auf Startschiffe
* keine Angriffe in persönlichen Landezonen
* keine Angriffe in Raumstationsnähe
* keine Angriffe auf offline-Spieler-Schiffe
* keine Angriffe während Docking
* keine Angriffe auf neue Spieler für eine Schutzzeit
* PvP nur in markierten Zonen

---

## 9.4 PvP-Bestätigung / Duellmodus

Optional kann es einen Raumduellmodus geben.

Ablauf:

1. Spieler A fordert Spieler B zu einem Raumduell heraus.
2. Spieler B nimmt an.
3. Beide Schiffe werden in eine Duellzone versetzt oder markiert.
4. Kampf ist nur zwischen diesen Schiffen aktiv.
5. Nach Ende gibt es Belohnung oder Status.

Das passt gut zu Servern, die PvP nur freiwillig erlauben.

---

# 10. NPC-Schiffe und Raumgegner

## 10.1 Grundidee

Im freien Raum sollen Computergegner auftauchen können.

Diese Gegner können sein:

* menschliche NPC-Piraten
* Händler, die feindlich werden können
* Alien-UFOs
* automatische Drohnen
* alte Verteidigungssysteme
* Raumminen
* kleine Jäger
* Fregatten
* große Raumkreuzer
* Transporter
* verlassene, aber aktive Wrackschiffe
* Boss-Schiffe

---

## 10.2 Abhängigkeit von Server-Settings

NPC-Raumgegner sollen abhängig von Servereinstellungen aktiv sein.

Mögliche Einstellung:

```text
SpaceNPCEnemies = Aus / Selten / Normal / Häufig / Extrem
```

Wenn aus:

* keine feindlichen NPC-Schiffe
* keine UFO-Angriffe
* keine Raumkampf-Zufallsbegegnungen

Wenn aktiv:

* NPC-Gegner können in passenden Rauminstanzen erscheinen
* Häufigkeit und Stärke hängen von Einstellung ab

---

## 10.3 Friedliche NPC-Schiffe

Neben Gegnern kann es auch friedliche NPC-Schiffe geben.

Beispiele:

* Händler
* Frachter
* Forschungsschiffe
* Bergbauschiffe
* Rettungsschiffe
* neutrale Stationstransporter

Server-Setting:

```text
NeutralNPCShips = An / Aus
```

Friedliche NPCs können Missionen, Handel oder Atmosphäre bieten.

---

## 10.4 Alien-UFOs

Alien-UFOs sind besondere Raumgegner.

Eigenschaften:

* ungewöhnliche Flugmuster
* hohe Wendigkeit
* fremdartige Waffen
* seltene Ressourcen oder Artefakte als Belohnung
* können an bestimmten Planetentypen oder Signalen erscheinen

Server-Setting:

```text
AlienUFOs = Aus / Selten / Normal / Häufig
```

---

## 10.5 Raumkreuzer und große Gegner

Große Raumkreuzer sollen besondere Begegnungen sein.

Eigenschaften:

* viele Trefferpunkte
* mehrere Geschütze
* langsame Bewegung
* eventuell Schwachpunkte
* mehrere Spieler können gemeinsam kämpfen
* können als Server-Event erscheinen

Nicht für erste Version zwingend erforderlich.

---

# 11. Asteroiden und zerstörbare Raumobjekte

## 11.1 Asteroiden im freien Raum

Asteroiden sollen nicht nur Kulisse sein.

Mögliche Funktionen:

* Hindernisse im Dogfight
* Abbauobjekte
* Deckung
* Missionsziele
* Rohstoffquelle
* Gefahr durch Kollision
* zerstörbare Objekte

---

## 11.2 Asteroiden abschießen oder abbauen

Spieler können mit passenden Schiffswerkzeugen oder Waffen Asteroiden bearbeiten.

Mögliche Unterscheidung:

### Mining-Werkzeug

* löst Rohstoffe aus Asteroiden
* wenig oder kein Kampfschaden
* auf friedlichen Servern erlaubt

### Schiffskanone

* zerstört Asteroiden
* kann Rohstoffe freisetzen
* kann auch als Waffe dienen, wenn erlaubt

---

## 11.3 Server-Settings

```text
AsteroidDestruction = Aus / Nur Mining / Waffen erlaubt
```

---

# 12. Gegner auf Planeten

## 12.1 Grundidee

Zusätzlich zu Raumgegnern soll es auch auf Planeten Gegner geben können.

Diese Gegner können Monster, aggressive Tiere, Aliens oder mechanische Einheiten sein.

Sie erscheinen abhängig von:

* Server-Settings
* Planetentyp
* Biomen
* Gefahrenstufe
* Missionen
* Ereignissen
* Admin-Konfiguration
* KI-generierten Missionen, falls später aktiv

---

## 12.2 Gegnertypen auf Planeten

Mögliche Kategorien:

### Aggressive Tiere

* greifen an, wenn Spieler zu nah kommt
* passen zu Biomen
* eher natürliche Gefahr

### Alien-Monster

* fremdartige Kreaturen
* stärkere Bedrohung
* seltenere Ressourcen oder Scan-Daten

### Schwarmgegner

* kleine Gegner in Gruppen
* gefährlich in Höhlen

### Große Kreaturen

* seltene Mini-Bosse
* bewachen besondere Ressourcen oder Orte

### Mechanische Gegner

* alte Drohnen
* Verteidigungsroboter
* beschädigte Stationssysteme
* Wächter alter Ruinen

---

## 12.3 Server-Setting für Planetengegner

```text
PlanetEnemies = Aus / Selten / Normal / Häufig / Extrem
```

Wenn aus:

* keine aggressiven Gegner auf Planeten

Wenn aktiv:

* Gegner können abhängig von Weltregeln erscheinen

---

## 12.4 Monster auf friedlichen Servern

Auf friedlichen Servern sollen keine aggressiven Monster erscheinen.

Friedliche Kreaturen können optional weiter existieren.

Mögliche Regel:

```text
PeacefulModeDisablesHostileCreatures = true
```

---

## 12.5 Gegner und Missionen

Wenn Gegner deaktiviert sind:

* keine Missionen vom Typ „Besiege Monster“
* keine KI-Missionen mit Kampfvoraussetzung
* keine Admin-Missionen mit Gegnerkampf, außer Admin überschreibt bewusst
* keine Kampfbelohnungen als Standardmission

Wenn Gegner aktiviert sind:

* Missionen können Gegner enthalten
* NPCs können Orte bewachen
* Planeten können gefährlicher sein
* Waffen und Schutzmodule werden wichtiger

---

# 13. Integration in Server-Settings

Die neuen Systeme müssen in die vorhandenen Server-Settings eingebaut werden.

## 13.1 Neue Settings

Empfohlene neue Einstellungen:

```text
PersonalLandingZones = An / Aus
PersonalLandingZoneProtection = Aus / Startzone / Alle

ShipDocking = Aus / Nur Freunde / Anfrage erforderlich / Frei
CargoTransferBetweenShips = Aus / Nur mit Zustimmung / An
GuestAccessToShips = Aus / Nur freigegebene Bereiche / Frei

FreeSpaceFlight = Aus / An
SpaceCombat = Aus / PvE / PvP / PvE und PvP
ShipWeapons = Aus / Nur Mining-Werkzeuge / Nur gegen NPCs / PvP erlaubt / Alle erlaubt
ShipDamageByPlayers = Aus / PvP-Zonen / An

SpaceNPCEnemies = Aus / Selten / Normal / Häufig / Extrem
NeutralNPCShips = An / Aus
AlienUFOs = Aus / Selten / Normal / Häufig
LargeSpaceEnemies = Aus / Selten / Events

AsteroidDestruction = Aus / Nur Mining / Waffen erlaubt

PlanetEnemies = Aus / Selten / Normal / Häufig / Extrem
PassiveCreatures = An / Aus
```

---

## 13.2 Preset-Beispiele

### Friedlicher Bau-Server

```text
FreeSpaceFlight = An
SpaceCombat = Aus
ShipWeapons = Aus
ShipDocking = Anfrage erforderlich
PlanetEnemies = Aus
SpaceNPCEnemies = Aus
PersonalLandingZones = An
PersonalLandingZoneProtection = Alle
```

### Koop-Survival mit Aliens

```text
FreeSpaceFlight = An
SpaceCombat = PvE
ShipWeapons = Nur gegen NPCs
ShipDocking = Anfrage erforderlich
PlanetEnemies = Normal
SpaceNPCEnemies = Normal
AlienUFOs = Selten
PersonalLandingZones = An
```

### PvP-Raumkampf-Server

```text
FreeSpaceFlight = An
SpaceCombat = PvE und PvP
ShipWeapons = PvP erlaubt
ShipDamageByPlayers = PvP-Zonen oder An
ShipDocking = Anfrage erforderlich
PlanetEnemies = Normal
SpaceNPCEnemies = Normal
AlienUFOs = Normal
```

---

# 14. Anforderungen an Server-Autorität

Alle neuen Systeme müssen serverseitig autoritativ sein.

Der Client darf nicht selbst entscheiden:

* ob ein Schiff angedockt ist
* ob eine Docking-Anfrage gültig ist
* ob ein Spieler ein fremdes Schiff betreten darf
* ob eine Kanone Schaden verursacht
* ob ein NPC-Schiff getroffen wurde
* ob ein Spieler-Schiff beschädigt wurde
* ob ein Alien spawnt
* ob ein Planetengegner existiert
* ob ein Asteroid zerstört wurde
* ob PvP erlaubt ist
* ob ein Schiff geschützt ist

Der Server prüft und entscheidet alles.

---

# 15. Persistenz-Anforderungen

Zu speichern sind:

## 15.1 Landezonen

* Spieler-ID
* Planet-ID
* Landepunkt
* Landebereichsgröße
* Schutzstatus
* Ausbaustatus

## 15.2 Docking-Module

* Schiff besitzt Docking-Modul
* Position des Moduls am Schiff
* Modulstufe
* Rechte und Zugriffseinstellungen

## 15.3 Docking-Zustand

* angedockte Schiffe
* beteiligte Spieler
* Docking-Zeitpunkt
* Zugriffsrechte
* aktiver Übergang

## 15.4 Schiffsbewaffnung

* installierte Waffenmodule
* Munition/Energie
* Zustand
* Schaden
* Upgrades

## 15.5 Rauminstanzen

* aktive Raumzone
* Spielerpositionen
* NPC-Schiffe
* zerstörte Asteroiden
* persistente Wracks
* besondere Ereignisse

## 15.6 Gegner

* generierte Gegnergruppen
* Bossgegner
* Missionsgegner
* besiegte Gegner, falls relevant
* Respawnregeln

---

# 16. UI-Anforderungen

## 16.1 Docking-UI

Der Client braucht:

* Docking-Anfrage senden
* Docking-Anfrage annehmen/ablehnen
* Docking-Status anzeigen
* Abdocken
* Zugriffsrechte anzeigen
* Warnung bei Kampf oder Gefahr

---

## 16.2 Raumflug-UI

Der Client braucht im freien Raumflug:

* Geschwindigkeit
* Richtung
* Schild/Hülle
* Energie
* Zielerfassung
* Waffenstatus
* Munition/Energieverbrauch
* Warnungen
* Distanz zu Ziel
* Docking-Reichweite
* NPC-Schiff-Anzeige
* PvP-Warnung

---

## 16.3 Gegner-UI

Bei Gegnern:

* Lebensstatus oder Trefferfeedback
* Warnsymbole
* Scanneranzeige
* Kampfwarnung
* Loot-/Bergungshinweis
* Missionsfortschritt

---

# 17. Konzeptprüfung vor vollständiger Umsetzung

Vor Umsetzung des vollständigen Systems soll ein Konzept- und Prototyping-Schritt erfolgen.

## 17.1 Zu klärende Fragen

### Docking

* Wie exakt müssen Schiffe ausgerichtet sein?
* Gibt es automatische Andockhilfe?
* Können mehrere Schiffe gleichzeitig an ein Schiff andocken?
* Gibt es Docking an Stationen?
* Was passiert bei Verbindungsabbruch während Docking?

### Raumflug

* Arcade-Steuerung oder realistischere Physik?
* Wie groß sind Raumzonen?
* Wird aus Cockpit-Perspektive oder Außenkamera geflogen?
* Gibt es Autopilot?
* Wie funktioniert Landung aus freiem Flug?

### Kampf

* Wie schnell soll Kampf sein?
* Wie stark darf Raumkampf den Kern des Spiels dominieren?
* Wie werden Treffer berechnet?
* Wie wird Lag im Multiplayer behandelt?
* Wie werden PvP- und PvE-Regeln getrennt?

### Gegner

* Wie komplex ist Gegner-KI?
* Wie viele NPC-Schiffe können aktiv sein?
* Wie stark werden Planetenmonster simuliert?
* Werden Gegner in entladenen Chunks simuliert oder eingefroren?

---

# 18. MVP-Anforderungen

## 18.1 Docking-MVP

* Schleusen-Blaupause
* Schleuse als Schiffmodul bauen
* Docking-Anfrage an anderen Spieler senden
* anderer Spieler kann bestätigen oder ablehnen
* nach Bestätigung können Spieler zwischen Schiffen wechseln
* einfache Gäste-Rechte
* Abdocken möglich
* serverseitig validiert

---

## 18.2 Landezonen-MVP

* jeder Spieler erhält persönlichen Start-Landebereich
* Landebereich bleibt persistent
* Schiff landet dort zuverlässig
* Bereich ist geschützt
* Admin kann Schutz einstellen

---

## 18.3 Freier Raumflug-MVP

* einfache lokale Rauminstanz
* Spieler kann Schiff frei steuern
* Asteroiden oder Raumobjekte sichtbar
* Annäherung an anderes Schiff möglich
* Docking im Raum möglich
* noch kein komplexer Kampf nötig

---

## 18.4 Raumkampf-MVP

* nur wenn Server erlaubt
* einfache Schiffskanone
* einfache Zielerfassung
* Kampf gegen einfache NPC-Drohnen
* Schild/Hülle als einfache Werte
* keine permanente Schiffzerstörung
* PvP zunächst optional oder deaktiviert

---

## 18.5 Gegner-MVP

* PlanetEnemies an/aus
* einfache aggressive Kreatur auf Planeten
* SpaceNPCEnemies an/aus
* einfache NPC-Drohne im Raum
* Server-Settings steuern Spawn
* friedliche Server haben keine Gegner

---

# 19. Nicht-Ziele für erste Umsetzung

Nicht zwingend für erste Version:

* große Raumkreuzer
* komplexe Flottenkämpfe
* mehrere andockende Schiffe gleichzeitig
* erzwungenes Entern
* vollständiger PvP-Raumkrieg
* Modulschaden an jedem Schiffsteil
* realistische Orbitalmechanik
* riesige offene Weltraumkarte ohne Instanzen
* komplexe NPC-Fraktionen
* intelligente Großkampfschiff-KI
* vollständiges Monster-Ökosystem
* Bosskämpfe auf Planeten
* persistente NPC-Flotten über das ganze Universum

---

# 20. Zusammenfassung

Spacecraft soll um mehrere große Raumschiff- und Kampf-Features erweitert werden.

Spieler können über Blaupausen Schleusen und Docking-Module an ihre Schiffe anbauen. Damit können sie mit anderen Spielern andocken, sofern beide Seiten zustimmen. Nach erfolgreichem Andocken können Spieler zwischen Schiffen wechseln und in freigegebenen Bereichen interagieren.

Auf Servern soll jeder Spieler einen persönlichen Landebereich erhalten, mindestens im Startbereich. Dieser Landebereich bleibt persistent, kann geschützt werden und sorgt dafür, dass Spieler geordnet und zuverlässig landen.

Zusätzlich soll es perspektivisch freien Raumflug geben. Spieler können ihr Schiff in lokalen Rauminstanzen steuern, Asteroidenfelder erkunden, an andere Schiffe andocken und, sofern der Server es erlaubt, Raumkämpfe austragen.

Schiffsbewaffnung soll über Blaupausen freischaltbar sein. Waffen und Kampf funktionieren nur, wenn die Server-Settings es erlauben. PvP-Schiffskampf muss ausdrücklich aktiviert sein und durch Schutzregeln begrenzt werden.

Im freien Raum können NPC-Gegner erscheinen, etwa Drohnen, Piraten, Alien-UFOs oder große Raumkreuzer. Auf Planeten können aggressive Kreaturen, Monster, Aliens oder mechanische Gegner erscheinen. All diese Gegner sind serverseitig konfigurierbar. Auf friedlichen Servern erscheinen keine aggressiven Gegner.

Die wichtigste Leitlinie lautet:

> Das Raumschiff wird zu einem sozialen, mobilen und kampffähigen Zentrum.
> Docking, Raumflug, Raumkampf und Gegner sind optionale Systeme, die stark von Server-Settings abhängen.
> Friedliche Server bleiben friedlich. Gefährliche Server können Aliens, NPC-Schiffe, Monster und PvP aktivieren.
> Vor vollständiger Umsetzung von freiem Raumflug und Dogfights muss ein Konzept- und Prototyping-Schritt klären, wie komplex diese Systeme werden sollen.
