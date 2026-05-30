Hier ist das aktualisierte neue Dokument mit **Kreativmodus, Überlebensmodus, Weltenbeschreibung/Weltgenerierung und Admin-Cheats** eingearbeitet.

# Spacecraft – Zusatzanforderung: Spielmodi, Server-Settings, Weltbeschreibung, prozedurale Welten und Admin-Cheats

## 1. Ziel dieses Dokuments

Dieses Dokument beschreibt weitere Anforderungen für **Spacecraft**.

Es ergänzt die bisherigen Konzepte um:

1. **Kreativmodus** und **Überlebensmodus**
2. umfangreiche **Server-Settings** für Spielstil, Kampf, Aliens, Waffen und Gefahren
3. eine vom Admin festlegbare **Weltenbeschreibung**
4. prozedural generierte, aber nach Generierung dauerhaft stabile Welten
5. optionale **Cheat- und Adminfunktionen**
6. klare Rechte: Cheats nur für den **Weltenadmin** beziehungsweise berechtigte Admins

Ziel ist, dass ein selbst gehosteter Spacecraft-Server stark anpassbar ist. Ein Admin soll entscheiden können, ob die Welt eher friedlich, kreativ, gefährlich, survival-orientiert, PvP-lastig oder familienfreundlich ist.

---

# 2. Grundidee

Spacecraft soll nicht nur eine feste Spielweise besitzen.

Ein Server kann zum Beispiel sein:

* friedlicher Kreativserver
* klassischer Überlebensserver
* Koop-Survival-Server
* Alien-Survival-Server
* PvP-Server
* Familienserver ohne Waffen
* Bau- und Erkundungsserver ohne Monster
* Experimentierwelt mit Admin-Cheats
* harte Survival-Welt mit Inventarverlust

Diese Spielweise wird durch Server-Settings und Weltregeln festgelegt.

Die wichtigste Leitlinie lautet:

> Der Admin bestimmt die Grundregeln der Welt.
> Der Server setzt diese Regeln autoritativ durch.
> Spieler sehen beim Beitritt klar, welche Regeln auf dem Server gelten.

---

# 3. Spielmodi

Spacecraft soll mindestens zwei grundlegende Spielmodi unterstützen:

1. **Überlebensmodus**
2. **Kreativmodus**

Diese Modi können pro Welt oder Server festgelegt werden. Zusätzlich kann der Admin einzelne Regeln weiter anpassen.

---

## 3.1 Überlebensmodus

Der Überlebensmodus ist der normale Spacecraft-Spielmodus.

Der Spieler startet mit einem einfachen Raumschiff und muss Rohstoffe sammeln, Werkzeuge herstellen, Technologien freischalten und sein Schiff ausbauen.

### Merkmale

Im Überlebensmodus gelten:

* begrenztes Inventar
* begrenzter Frachtraum
* Rohstoffe müssen gesammelt werden
* Crafting benötigt Materialien
* Schiffserweiterungen benötigen Ressourcen
* Sauerstoff kann verbraucht werden
* Umweltgefahren können Schaden verursachen
* Tod oder kritischer Zustand führt zum Respawn im Heiltank
* Werkzeuge können Energie verbrauchen oder beschädigt werden
* Missionen und Belohnungen haben echte Bedeutung
* Tech-Tree und Blaupausen strukturieren den Fortschritt

Der Überlebensmodus ist der Standardmodus für das eigentliche Spielgefühl.

---

## 3.2 Kreativmodus

Der Kreativmodus ist ein freier Bau- und Experimentiermodus.

Er soll ähnlich wie bei Minecraft ermöglichen, schnell und ohne Ressourcenbegrenzung zu bauen, zu testen und Welten zu gestalten.

### Merkmale

Im Kreativmodus können gelten:

* unbegrenzte oder stark vereinfachte Ressourcen
* Zugriff auf viele oder alle Blöcke
* Zugriff auf viele oder alle Bauteile
* keine oder reduzierte Umweltgefahren
* kein Sauerstoffverbrauch, falls so eingestellt
* kein Inventarverlust
* schnelleres Bauen
* schnelleres Abbauen
* optional Flugmodus
* optional Teleportfunktionen für Admins
* optional sofortiges Platzieren größerer Strukturen
* Crafting kann übersprungen oder stark vereinfacht werden
* Schiffserweiterungen können ohne Materialkosten platziert werden, sofern aktiviert

Der Kreativmodus ist besonders geeignet für:

* Testserver
* Bauprojekte
* Kinder- und Familienwelten
* Admins, die Strukturen vorbereiten wollen
* Serverdesign
* Außenpostenbau
* Missions- und Weltgestaltung

---

## 3.3 Gemischte Modi

Es soll möglich sein, zwischen Grundmodus und Detailregeln zu unterscheiden.

Beispiele:

### Kreativwelt ohne Gefahr

* Kreativmodus aktiv
* keine aggressiven Aliens
* keine Waffen
* kein PvP
* keine Umweltgefahren

### Kreativwelt mit Alien-Test

* Kreativmodus aktiv
* aggressive Aliens aktiv
* Waffen aktiv
* Spieler können Kampfsituationen testen

### Überlebenswelt ohne PvP

* Überlebensmodus aktiv
* Aliens aktiv
* Umweltgefahren aktiv
* PvP deaktiviert

### Überlebenswelt ohne Waffen

* Überlebensmodus aktiv
* keine Laserwaffen
* nur Werkzeuge
* keine aggressiven Aliens oder nur sehr seltene Aliens

---

# 4. Server-Presets

Damit Admins nicht jede Einstellung einzeln setzen müssen, soll es vordefinierte Profile geben.

## 4.1 Friedlicher Kreativserver

```text
GameMode = Creative
AggressiveAliens = Aus
PassiveCreatures = An
PlayerDamage = Aus
WeaponMode = Keine Waffen
EnvironmentalHazards = Aus
OxygenConsumption = Aus
DeathPenalty = Keine
KeepInventoryOnDeath = An
AllowPlayerStructureDamage = Aus
ShipDamageByPlayers = Aus
AdminCheats = An
```

Ziel:

* freies Bauen
* keine Gefahr
* keine Waffen
* ideal für kreative Projekte

---

## 4.2 Familienfreundliches Erkunden

```text
GameMode = Survival
AggressiveAliens = Aus
PassiveCreatures = An
PlayerDamage = Aus
WeaponMode = Nur Werkzeuge
EnvironmentalHazards = Leicht
OxygenConsumption = Langsam
DeathPenalty = Keine oder Leicht
KeepInventoryOnDeath = An
ShipDamageByPlayers = Aus
```

Ziel:

* ruhiges Erkunden
* Survivalgefühl ohne harte Strafen
* keine echten Waffen

---

## 4.3 Koop-Survival

```text
GameMode = Survival
AggressiveAliens = Normal
PassiveCreatures = An
PlayerDamage = Aus
WeaponMode = Laserwaffen erlaubt
EnvironmentalHazards = Normal
OxygenConsumption = Normal
DeathPenalty = Leicht
KeepInventoryOnDeath = Optional
AllowPlayerStructureDamage = Nur mit Rechten
ShipDamageByPlayers = Aus
```

Ziel:

* gemeinsames Überleben
* Aliens als Gefahr
* kein PvP

---

## 4.4 Gefährliche Alien-Welten

```text
GameMode = Survival
AggressiveAliens = Häufig
PassiveCreatures = An
PlayerDamage = Aus
WeaponMode = Laserwaffen erlaubt
EnvironmentalHazards = Schwer
OxygenConsumption = Normal
DeathPenalty = Normal
KeepInventoryOnDeath = Aus
AllowPlayerStructureDamage = Nur mit Rechten
ShipDamageByPlayers = Aus
```

Ziel:

* gefährlicher PvE-Server
* harte Expeditionen
* Vorbereitung wichtig

---

## 4.5 PvP-Server

```text
GameMode = Survival
AggressiveAliens = Normal
PlayerDamage = An
FriendlyFire = An
WeaponMode = Alle Waffen erlaubt
LaserWeapons = An
MeleeWeapons = An
EnvironmentalHazards = Normal
DeathPenalty = Normal
KeepInventoryOnDeath = Aus
AllowPlayerStructureDamage = An oder PvP-Zonen
ShipDamageByPlayers = Nur PvP-Zonen oder An
```

Ziel:

* Wettbewerb
* Konflikt zwischen Spielern
* höheres Risiko

---

# 5. Server-Settings für Kampf, Aliens und Waffen

## 5.1 Aggressive Aliens / Monster

Der Admin kann einstellen, ob aggressive außerirdische Kreaturen erscheinen.

```text
AggressiveAliens = Aus / Selten / Normal / Häufig / Extrem
```

### Aus

* keine aggressiven Aliens
* keine Alien-Angriffe
* keine Kampfmissionen gegen Aliens

### Selten

* Aliens erscheinen nur gelegentlich

### Normal

* Aliens sind Teil des Standard-Survival-Erlebnisses

### Häufig

* Aliens sind eine wichtige Gefahr

### Extrem

* sehr gefährlicher Servermodus

---

## 5.2 Passive Kreaturen

```text
PassiveCreatures = An / Aus
```

Passive Kreaturen können Atmosphäre schaffen, Ressourcen liefern oder gescannt werden. Sie greifen nicht aktiv an.

---

## 5.3 PvP / Spieler-Schaden

```text
PlayerDamage = Aus / Nur Duelle / Gruppenabhängig / An
```

### Aus

* Spieler können sich nicht gegenseitig verletzen

### Nur Duelle

* Schaden nur nach Zustimmung beider Spieler

### Gruppenabhängig

* Schaden abhängig von Team, Gruppe oder Fraktion

### An

* Spieler können anderen Spielern Schaden zufügen

---

## 5.4 Friendly Fire

```text
FriendlyFire = An / Aus
```

Wenn aus:

* Teammitglieder können sich nicht gegenseitig verletzen

Wenn an:

* Schaden gilt auch für Teammitglieder

---

## 5.5 Waffenmodus

```text
WeaponMode = Keine Waffen / Nur Werkzeuge / Nicht-tödliche Waffen / Laserwaffen erlaubt / Alle Waffen erlaubt
```

### Keine Waffen

* keine Waffenrezepte
* keine Waffen als Loot
* keine Waffen als Missionsbelohnung

### Nur Werkzeuge

* Bohrer, Scanner, Bauwerkzeuge und Reparaturwerkzeuge sind erlaubt
* keine echten Kampfwaffen

### Nicht-tödliche Waffen

* Betäubungsstrahler
* Schutzfeldwerfer
* Kreaturen-Abwehrsender
* Ablenkungsbojen

### Laserwaffen erlaubt

* Laserwaffen können gecraftet, gefunden oder als Belohnung erhalten werden

### Alle Waffen erlaubt

* alle vorgesehenen Waffentypen sind erlaubt

---

# 6. Survival-, Tod- und Respawn-Settings

## 6.1 Umweltgefahren

```text
EnvironmentalHazards = Aus / Leicht / Normal / Schwer
```

Gefahren können sein:

* Sauerstoffmangel
* Strahlung
* Hitze
* Kälte
* giftige Atmosphäre
* Säure
* Meteoritenschauer
* Stürme
* Lavagebiete
* hohe Schwerkraft
* niedrige Schwerkraft

---

## 6.2 Sauerstoffverbrauch

```text
OxygenConsumption = Aus / Langsam / Normal / Schnell
```

Im Kreativmodus kann Sauerstoff standardmäßig deaktiviert sein.

Im Überlebensmodus ist Sauerstoff normalerweise aktiv.

---

## 6.3 DeathPenalty

```text
DeathPenalty = Keine / Leicht / Normal / Hart
```

### Keine

* Respawn im Heiltank ohne Verlust

### Leicht

* kleine Nachteile, aber keine harte Bestrafung

### Normal

* Inventar teilweise in Bergungskapsel
* Werkzeuge können beschädigt werden

### Hart

* stärkere Verluste
* für harte Survival-Server

---

## 6.4 Inventar beim Tod behalten

Cheat- oder Komfortoption:

```text
KeepInventoryOnDeath = An / Aus
```

Wenn aktiv:

* Spieler behalten ihr persönliches Inventar nach Tod oder kritischem Zustand
* kein Inventarverlust beim Respawn im Heiltank
* Bergungskapsel kann deaktiviert werden

Wenn deaktiviert:

* Inventarverlust oder Bergungskapsel folgt den DeathPenalty-Regeln

Diese Option kann sowohl für Kreativmodus als auch für Überlebensmodus verfügbar sein, je nach Admin-Einstellung.

---

## 6.5 Schiff beim Tod behalten

Da das Raumschiff ein zentraler Fortschrittsgegenstand ist, soll der Admin festlegen können, ob Schiffszustand und Schiffsinventar durch Tod beeinflusst werden.

```text
KeepShipOnDeath = An / Aus
```

Empfehlung:

Standardmäßig sollte diese Option aktiv sein.

Das bedeutet:

* Tod des Spielers zerstört nicht sein Raumschiff
* Schiffsmodule bleiben erhalten
* Frachtraum bleibt erhalten
* Tech-Fortschritt bleibt erhalten

Optional können auf sehr harten Servern Schäden oder Kosten entstehen.

---

# 7. Bau- und Zerstörungsregeln

## 7.1 Fremde Gebäude abbauen

```text
AllowPlayerStructureDamage = Aus / Nur mit Rechten / An
```

### Aus

* fremde Gebäude können nicht zerstört werden

### Nur mit Rechten

* nur eigene oder freigegebene Strukturen können verändert werden

### An

* Spieler können fremde Strukturen beschädigen

---

## 7.2 Schiffsschaden durch Spieler

```text
ShipDamageByPlayers = Aus / Nur PvP-Zonen / An
```

Empfehlung:

Standardmäßig aus, damit fremde Schiffe geschützt sind.

---

## 7.3 Schutzgebiete

Admins können Schutzregeln definieren:

* Startschiffe geschützt
* Medbay geschützt
* Heiltank geschützt
* Spawnzonen geschützt
* Raumstationen geschützt
* Außenposten geschützt
* Admin-Zonen geschützt

---

# 8. Weltenbeschreibung durch Admin

## 8.1 Grundidee

Beim Erstellen einer neuen Welt soll der Weltenadmin eine **Weltenbeschreibung** festlegen können.

Diese Weltenbeschreibung bestimmt, wie groß und wie dicht das Universum des Servers ist.

Sie ist nicht nur ein Text, sondern eine strukturierte Konfiguration für die prozedurale Generierung.

Der Admin legt fest:

* wie viele Sternensysteme es gibt
* wie viele Planeten pro System möglich sind
* wie häufig Monde sind
* wie häufig Asteroiden sind
* wie häufig Raumstationen sind
* wie häufig besondere Orte sind
* welche Planetentypen vorkommen
* welche Gefahren häufig sind
* wie selten besondere Ressourcen sind
* wie stark das Universum auf Erkundung, Bergbau, Kampf oder Bauen ausgerichtet ist

---

## 8.2 Beispiel einer Weltenbeschreibung

```text
WorldName = Nova-Randsektor
WorldSeed = 839201

StarSystemCount = 12

PlanetsPerSystem = 2 bis 7
MoonsPerPlanet = 0 bis 3

AsteroidFieldFrequency = Normal
SmallAsteroidsFrequency = Häufig
LargeAsteroidsFrequency = Selten

SpaceStationFrequency = Selten
AbandonedStationFrequency = Selten
WreckFrequency = Normal

PlanetTypes:
- Felsplanet: Häufig
- Eisplanet: Normal
- Wüstenplanet: Normal
- Lavaplanet: Selten
- Dschungelplanet: Selten
- Ozeanplanet: Selten
- Kristallmond: Sehr selten

ResourceDensity = Normal
RareResourceFrequency = Selten
AlienArtifactFrequency = Sehr selten

EnvironmentalDanger = Normal
ExplorationFocus = Hoch
CombatFocus = Niedrig
```

---

## 8.3 Einstellbare Weltparameter

Die Admin-Oberfläche soll Parameter anbieten wie:

### Universumsgröße

```text
StarSystemCount = Klein / Mittel / Groß / Benutzerdefiniert
```

Oder konkret:

```text
StarSystemCount = 5
StarSystemCount = 12
StarSystemCount = 30
```

---

### Planetenhäufigkeit

```text
PlanetsPerSystemMin
PlanetsPerSystemMax
```

Beispiel:

```text
PlanetsPerSystemMin = 2
PlanetsPerSystemMax = 7
```

---

### Mondhäufigkeit

```text
MoonsPerPlanetMin
MoonsPerPlanetMax
MoonFrequency = Selten / Normal / Häufig
```

---

### Asteroidenhäufigkeit

```text
AsteroidFields = Aus / Selten / Normal / Häufig
SmallAsteroids = Selten / Normal / Häufig
LargeAsteroids = Selten / Normal / Häufig
```

---

### Raumstationen und Wracks

```text
SpaceStations = Aus / Selten / Normal / Häufig
AbandonedStations = Aus / Selten / Normal / Häufig
ShipWrecks = Aus / Selten / Normal / Häufig
```

---

### Planetentypen

Admin kann festlegen, welche Planetentypen vorkommen dürfen:

* Felsplanet
* Eisplanet
* Lavaplanet
* Wüstenplanet
* Dschungelplanet
* Ozeanplanet
* Kristallmond
* Giftplanet
* Metallplanet
* verstrahlter Mond
* Asteroidenwelt

Für jeden Typ kann eine Häufigkeit gesetzt werden:

```text
Aus / Sehr selten / Selten / Normal / Häufig
```

---

### Ressourcenverteilung

```text
ResourceDensity = Niedrig / Normal / Hoch
RareResourceFrequency = Sehr selten / Selten / Normal / Häufig
StartingSystemResourceSafety = Niedrig / Normal / Hoch
```

Damit kann der Admin bestimmen, ob der Server eher langsam und survival-lastig oder schnell und baulastig ist.

---

### Gefahrenverteilung

```text
DangerousPlanets = Selten / Normal / Häufig
HighRadiationWorlds = Aus / Selten / Normal / Häufig
LavaWorlds = Aus / Selten / Normal / Häufig
ToxicAtmospheres = Aus / Selten / Normal / Häufig
```

---

## 8.4 Editierbarkeit der Weltenbeschreibung

Die Weltenbeschreibung soll in der Admin-Oberfläche editierbar sein.

Wichtig ist aber:

> Nicht jede Änderung kann rückwirkend auf bereits generierte Welten angewendet werden.

Deshalb muss die UI klar unterscheiden:

### Sofort wirksam

Beispiele:

* Alien-Spawnrate
* PvP
* Waffenregeln
* Wetterregeln, falls dynamisch
* Missionen
* Belohnungstabellen
* Cheats
* Umweltgefahren-Multiplikator

### Wirkt nur auf neu generierte Orte

Beispiele:

* Häufigkeit neuer Planeten
* Häufigkeit neuer Asteroiden
* Häufigkeit neuer Wracks
* Rohstoffverteilung in noch nicht generierten Chunks
* Biome in noch nicht generierten Bereichen

### Erfordert neue Welt oder Regeneration

Beispiele:

* Anzahl der Sternensysteme, wenn bereits komplett generiert
* grundlegende Weltstruktur
* bereits generierte Planeten löschen oder neu erzeugen
* vorhandene Ressourcenverteilung komplett ändern

---

# 9. Prozedurale Generierung und Persistenz

## 9.1 Grundprinzip

Die Welten sollen prozedural generiert werden.

Das bedeutet:

* Sternensysteme entstehen aus Seed und Weltenbeschreibung
* Planeten entstehen aus Seed und Parametern
* Gelände entsteht aus Seed und Planetentyp
* Rohstoffe entstehen aus Seed und Verteilungsregeln
* Höhlen, Biome, Wracks und besondere Orte entstehen aus Seed und Weltregeln

---

## 9.2 Einmal generiert, dann stabil

Eine zentrale Anforderung:

> Wenn ein Ort einmal generiert wurde, soll er beibehalten werden.

Das bedeutet:

* ein generierter Planet bleibt derselbe
* ein generierter Asteroid bleibt derselbe
* ein generierter Chunk bleibt derselbe
* abgebaute Blöcke bleiben abgebaut
* platzierte Blöcke bleiben platziert
* entdeckte Orte bleiben vorhanden
* Außenposten bleiben erhalten
* Missionen und Strukturen bleiben konsistent

Spieler sollen nicht erleben, dass eine bekannte Welt nach einem Serverneustart plötzlich anders aussieht.

---

## 9.3 Speicherung

Für prozedurale Welten soll nicht jeder natürliche Block gespeichert werden.

Stattdessen:

```text
Welt = Seed + Weltenbeschreibung + generierte Orte + Spieleränderungen
```

Gespeichert werden:

* WorldSeed
* Weltenbeschreibung
* generierte Sternensysteme
* generierte Planeten
* generierte Asteroiden
* generierte Raumstationen
* generierte besondere Orte
* generierte Chunks, sofern nötig
* Spieleränderungen
* abgebaute Blöcke
* platzierte Blöcke
* Außenposten
* Kisten
* Maschinen
* Missionsobjekte
* Entdeckungsstatus

---

## 9.4 Generierungsstatus

Jeder Ort soll einen Status haben:

```text
Nicht generiert
Generiert
Entdeckt
Besucht
Verändert
Persistiert
```

Beispiel:

Ein Sternensystem kann in der Karte schon als möglicher Ort existieren, aber seine Planeten werden erst beim Scannen oder ersten Besuch konkret generiert.

Sobald ein Planet generiert wurde, bleibt seine Struktur erhalten.

---

# 10. Cheats und Admin-Funktionen

## 10.1 Grundidee

Spacecraft soll optionale Cheats beziehungsweise Admin-Funktionen besitzen.

Diese Cheats sind nicht für normale Spieler gedacht.

Sie sollen nur verfügbar sein für:

* Weltenadmin
* Serverbesitzer
* berechtigte Admins
* eventuell Moderatoren mit speziellen Rechten

Wichtig:

> Cheats müssen vom Server aktiviert sein.
> Cheats gelten nicht automatisch für alle Spieler.
> Normale Spieler dürfen Cheats nicht nutzen.

---

## 10.2 Cheat-Aktivierung

Der Server soll eine Einstellung besitzen:

```text
AdminCheats = An / Aus
```

Zusätzlich kann es geben:

```text
AllowCheatsInSurvival = An / Aus
AllowCheatsInCreative = An / Aus
```

Empfehlung:

* Im Kreativmodus können Admin-Cheats standardmäßig erlaubt sein.
* Im Überlebensmodus sollten Admin-Cheats bewusst aktiviert werden müssen.
* Der Server sollte anzeigen, dass Cheats aktiv sind.

---

## 10.3 Nur für Weltenadmin

Der Spieler, der die Welt erstellt hat, ist standardmäßig der **Weltenadmin**.

Der Weltenadmin darf:

* Cheats nutzen, wenn aktiviert
* Server-Settings ändern
* Weltenbeschreibung ändern
* Missionen als Admin erstellen
* Blaupausen und Rezepte verwalten
* Spielerrechte vergeben
* andere Admins ernennen
* Adminrechte entziehen
* Serverregeln ändern
* Welt sichern oder exportieren

Andere Spieler dürfen diese Funktionen nur nutzen, wenn der Weltenadmin ihnen Rechte gibt.

---

## 10.4 Cheat-Funktionen im Spiel

Mögliche Admin-Cheats:

### Teleport zu Spieler

```text
TeleportToPlayer
```

Funktion:

* Admin teleportiert sich zu einem anderen Spieler

Nutzung:

* Support
* Moderation
* gemeinsames Bauen
* schnelle Navigation

---

### Spieler zu Admin teleportieren

```text
TeleportPlayerToAdmin
```

Funktion:

* Admin kann einen Spieler zu sich holen

Sollte vorsichtig genutzt werden und eventuell Bestätigung oder klare Meldung anzeigen.

---

### Teleport zu Ort

```text
TeleportToLocation
```

Funktion:

* Admin teleportiert sich zu Koordinaten, Planeten, Schiffen, Außenposten oder Stationen

---

### Tageszeit ändern

```text
SetTimeOfDay
```

Funktion:

* Admin kann Tageszeit auf einem Planeten ändern

Beispiele:

* Morgen
* Mittag
* Abend
* Nacht
* konkrete Zeit

---

### Wetter ändern

```text
SetWeather
```

Funktion:

* Admin kann Wetter auf einem Planeten ändern

Beispiele:

* klar
* Sturm
* Sandsturm
* Schneesturm
* Gewitter
* Meteorschauer
* giftiger Nebel

Wetteränderungen müssen zu Planetentyp und Serverregeln passen.

---

### Inventar geben

```text
GiveItem
```

Funktion:

* Admin kann sich selbst oder anderen Spielern Items geben

Diese Funktion sollte nur bei aktivierten Cheats und passenden Rechten möglich sein.

---

### Flugmodus

```text
FlyMode
```

Funktion:

* Admin kann frei fliegen

Besonders nützlich im Kreativmodus und beim Bauen.

---

### Unverwundbarkeit

```text
GodMode
```

Funktion:

* Admin kann Schaden deaktivieren

Sollte klar sichtbar sein und nur für berechtigte Admins nutzbar sein.

---

### Sofort bauen

```text
InstantBuild
```

Funktion:

* Admin kann Strukturen ohne Ressourcenverbrauch platzieren

---

### Weltinformationen anzeigen

```text
ShowWorldDebugInfo
```

Funktion:

* Admin kann Seed, Chunkdaten, Planetentyp, Ressourceninformationen oder Spawnzonen sehen

Nützlich zur Fehlersuche und Weltgestaltung.

---

# 11. Cheat-Regeln und Sicherheit

## 11.1 Cheats sind serverseitig

Alle Cheats müssen vom Server geprüft und ausgeführt werden.

Der Client darf nicht einfach behaupten:

* „Ich darf teleportieren“
* „Ich habe unendlich Items“
* „Ich bin unverwundbar“
* „Ich habe Wetter geändert“

Der Server prüft:

* Ist der Spieler Admin?
* Sind Cheats aktiviert?
* Hat der Spieler die konkrete Berechtigung?
* Ist der Cheat in diesem Modus erlaubt?
* Ist das Ziel gültig?

---

## 11.2 Cheat-Logging

Alle Cheat-Aktionen sollen protokolliert werden.

Log-Beispiele:

```text
Admin Marcel teleported to Player Alex.
Admin Marcel set weather on Planet Kora-3 to Snowstorm.
Admin Marcel gave 100 Titanium to Player Sam.
Admin Marcel enabled FlyMode.
```

Ziel:

* Nachvollziehbarkeit
* Moderation
* Schutz vor Missbrauch
* Debugging

---

## 11.3 Sichtbarkeit für Spieler

Der Server kann optional anzeigen, dass Cheats aktiv sind.

Beim Serverbeitritt:

```text
Hinweis: Auf diesem Server sind Admin-Cheats aktiviert.
```

Wenn ein Admin sichtbar teleportiert oder Wetter ändert, kann eine Servermeldung erscheinen:

```text
Der Weltenadmin hat das Wetter geändert.
```

Ob solche Meldungen angezeigt werden, kann konfigurierbar sein.

---

# 12. Cheats und Spielmodi

## 12.1 Kreativmodus

Im Kreativmodus passen Admin-Cheats gut zum Spielstil.

Empfohlene erlaubte Funktionen:

* Teleport
* Flugmodus
* Tageszeit ändern
* Wetter ändern
* Items geben
* InstantBuild
* Weltinformationen anzeigen

---

## 12.2 Überlebensmodus

Im Überlebensmodus sollten Cheats vorsichtiger behandelt werden.

Empfehlung:

* Cheats standardmäßig aus
* Aktivierung nur durch Weltenadmin
* Cheat-Nutzung wird geloggt
* Server zeigt optional an, dass Cheats aktiv sind
* Admin kann Cheats für Support oder Events nutzen

---

# 13. Auswirkungen auf Missionen, Crafting und Fortschritt

## 13.1 Kreativmodus

Im Kreativmodus können Missionen optional deaktiviert, vereinfacht oder nur als Bauziele genutzt werden.

Optionen:

```text
MissionsInCreative = Aus / An / Nur Admin-Missionen
```

Crafting kann im Kreativmodus:

* normal bleiben
* keine Ressourcen kosten
* komplett über Creative-Inventar ersetzt werden

---

## 13.2 Überlebensmodus

Im Überlebensmodus behalten Missionen, Crafting und Tech-Tree ihren normalen Wert.

Cheats können den Fortschritt verfälschen, wenn sie aktiv sind.

Deshalb sollte der Server markieren können:

```text
WorldHasCheatsEnabled = true
```

Optional kann dies Auswirkungen haben auf:

* Achievements
* Bestenlisten
* offizielle Serverlisten
* Exportkennzeichnung

---

# 14. Admin-UI für Spielmodi, Welt und Cheats

Die Admin-Weboberfläche soll neue Bereiche erhalten.

## 14.1 Spielmodus

Admin kann einstellen:

* Kreativmodus
* Überlebensmodus
* Wechsel zwischen Modi, falls erlaubt
* ob Spieler pro Spieler unterschiedliche Modi haben dürfen
* ob nur Admins Kreativmodus nutzen dürfen

Empfehlung:

Für den Anfang:

> Der Modus gilt für die ganze Welt.

Später optional:

* einzelne Spieler im Kreativmodus
* normale Spieler im Überlebensmodus
* Admins mit Kreativrechten

---

## 14.2 Weltenbeschreibung

Admin kann einstellen:

* Weltname
* Seed
* Anzahl Sternensysteme
* Planeten pro System
* Monde pro Planet
* Asteroidenhäufigkeit
* Raumstationshäufigkeit
* Wrackhäufigkeit
* Planetentyp-Häufigkeiten
* Ressourcenhäufigkeit
* Gefahrenhäufigkeit
* Startsystem-Sicherheit
* seltene Funde
* Alien-Artefakte

Die UI muss anzeigen, ob Änderungen sofort, bei neu generierten Bereichen oder nur bei neuer Welt wirken.

---

## 14.3 Cheats

Admin kann einstellen:

* Cheats aktivieren/deaktivieren
* Cheatrechte vergeben
* Teleport erlauben
* Wetter ändern erlauben
* Tageszeit ändern erlauben
* GiveItem erlauben
* GodMode erlauben
* FlyMode erlauben
* InstantBuild erlauben
* Cheat-Logging anzeigen
* Cheat-Meldungen an Spieler anzeigen

---

# 15. Client-Anforderungen

## 15.1 Beim Serverbeitritt

Der Spieler soll sehen:

* Spielmodus
* PvP-Regel
* Waffenregel
* Alien-Regel
* Umweltgefahren
* DeathPenalty
* KeepInventoryOnDeath
* ob Cheats aktiv sind
* ob die Welt Kreativmodus oder Überlebensmodus nutzt

Beispiel:

```text
Server-Regeln:
Modus: Überleben
PvP: Aus
Waffen: Nur Werkzeuge
Aggressive Aliens: Aus
Umweltgefahren: Leicht
Inventar behalten: An
Admin-Cheats: Aktiv
```

---

## 15.2 Im Spiel

Der Client soll erklären, wenn etwas durch Serverregeln blockiert ist.

Beispiele:

```text
Dieses Rezept ist im Kreativmodus nicht nötig.
```

```text
Laserwaffen sind auf diesem Server deaktiviert.
```

```text
PvP ist auf diesem Server ausgeschaltet.
```

```text
Nur der Weltenadmin darf diese Cheat-Funktion nutzen.
```

```text
Diese Änderung wirkt nur auf neu generierte Planeten.
```

---

# 16. MVP-Anforderungen

## 16.1 Spielmodi-MVP

Für eine erste Version:

* Überlebensmodus
* Kreativmodus
* Modus pro Welt festgelegt
* Kreativmodus mit einfachem Creative-Inventar
* Überlebensmodus mit normalem Crafting und Ressourcen
* Client zeigt aktuellen Modus an
* Server setzt Modusregeln autoritativ durch

---

## 16.2 Weltenbeschreibung-MVP

Für eine erste Version:

* Weltname
* Welt-Seed
* Anzahl Sternensysteme
* einfache Planetenzahl pro System
* einfache Asteroidenhäufigkeit
* einfache Planetentyp-Häufigkeit
* prozedurale Generierung anhand dieser Werte
* generierte Orte bleiben persistent
* Admin sieht Generierungsstatus
* Änderungen wirken klar nachvollziehbar

---

## 16.3 Cheat-MVP

Für eine erste Version:

* Cheats global aktivierbar/deaktivierbar
* nur Weltenadmin darf Cheats nutzen
* Teleport zu Spieler
* Teleport zu Ort
* Tageszeit ändern
* Wetter ändern
* GiveItem für Admin
* FlyMode für Admin
* Cheat-Logging
* Client zeigt Hinweis, wenn Cheats aktiv sind

---

## 16.4 Survival-Settings-MVP

Für eine erste Version:

* PvP an/aus
* aggressive Aliens an/aus
* Waffenmodus: Keine Waffen / Nur Werkzeuge / Laserwaffen erlaubt
* Umweltgefahren leicht/normal
* DeathPenalty
* KeepInventoryOnDeath
* KeepShipOnDeath
* Einstellungen persistent speichern
* Einstellungen in Admin-Weboberfläche anzeigen
* Server setzt Regeln autoritativ durch

---

# 17. Nicht-Ziele für die erste Version

Nicht zwingend am Anfang erforderlich:

* einzelne Spielmodi pro Spieler
* komplexe PvP-Zonen
* detaillierte Alien-Balancing-Tabellen
* vollständiges Wetter-Ökosystem
* dynamische Jahreszeiten
* komplexe Weltregenerierung
* öffentliche Cheat-Kommandokonsole für alle Spieler
* frei programmierbare Admin-Skripte
* Mod-Marktplatz
* vollständiger Creative-Struktur-Editor
* komplexe Berechtigungsgruppen
* automatische KI-Regeländerungen

---

# 18. Zusammenfassung

Spacecraft soll neben dem klassischen Überlebensmodus auch einen Kreativmodus erhalten. Im Überlebensmodus stehen Rohstoffe, Crafting, Sauerstoff, Gefahren, Tod, Heiltank-Respawn und Fortschritt im Mittelpunkt. Im Kreativmodus stehen freies Bauen, Experimentieren, Testen und Gestalten im Vordergrund.

Der Weltenadmin soll beim Erstellen einer Welt eine strukturierte Weltenbeschreibung festlegen können. Diese bestimmt Anzahl der Sternensysteme, Planeten, Monde, Asteroiden, Raumstationen, Planetentypen, Ressourcenhäufigkeiten und Gefahren. Die Welt wird prozedural aus Seed und Weltenbeschreibung erzeugt. Sobald Orte, Planeten oder Chunks einmal generiert wurden, müssen sie stabil bleiben und dürfen sich nicht willkürlich verändern.

Zusätzlich soll es Cheat- und Adminfunktionen geben. Diese Cheats müssen explizit aktiviert werden und dürfen nur vom Weltenadmin oder berechtigten Admins genutzt werden. Beispiele sind Teleport zu Spielern, Tageszeit ändern, Wetter ändern, Items geben, Flugmodus und Unverwundbarkeit. Alle Cheat-Aktionen müssen serverseitig geprüft und geloggt werden.

Die wichtigste Leitlinie lautet:

> Der Admin definiert Modus, Welt und Regeln.
> Der Server generiert die Welt prozedural, speichert generierte Orte dauerhaft und setzt alle Regeln autoritativ durch.
> Cheats sind nur für den Weltenadmin beziehungsweise berechtigte Admins verfügbar und müssen ausdrücklich aktiviert sein.
