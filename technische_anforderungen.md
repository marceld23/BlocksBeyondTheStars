Hier ist ein technisches Anforderungsdokument für einen Entwickler, basierend auf der bisherigen Architekturentscheidung: **Unity-Client, selbst hostbarer C#/.NET-Server, auch lauffähig auf Raspberry Pi 5 unter Linux**.

# Spacecraft – Technisches Anforderungsdokument

## 1. Ziel des Dokuments

Dieses Dokument beschreibt die technischen Grundanforderungen für die Umsetzung des Spiels **Spacecraft**.

Spacecraft ist ein blockbasiertes 3D-Weltraum-Crafting-Spiel mit eigenem Raumschiff, prozedural generierten Planeten, Rohstoffabbau, Crafting, Schiffsausbau und späterer Multiplayer-Fähigkeit.

Der Fokus dieses Dokuments liegt nicht auf Gameplay-Details, sondern auf der technischen Zielarchitektur, den Plattformanforderungen und den grundlegenden Systementscheidungen.

---

# 2. Grundsätzliche technische Zielsetzung

Spacecraft soll von Anfang an so entwickelt werden, dass es später multiplayerfähig ist.

Auch wenn eine erste Version zunächst im Singleplayer spielbar sein kann, soll die interne Architektur bereits nach dem Prinzip funktionieren:

> Der Client zeigt die Welt an und nimmt Eingaben entgegen.
> Der Server entscheidet, was wirklich passiert.

Das bedeutet:

* Der Spielclient ist nicht die Quelle der Wahrheit.
* Der Server verwaltet den autoritativen Weltzustand.
* Der Server entscheidet über Inventare, Blockabbau, Crafting, Schiffszustand, Sauerstoff, Schaden und Fortschritt.
* Der Client sendet Aktionen an den Server.
* Der Server validiert diese Aktionen und sendet den neuen Zustand zurück.

Diese Architektur soll verhindern, dass Multiplayer später mühsam nachgerüstet werden muss.

---

# 3. Zielplattformen

## 3.1 Spielclient

Der Spielclient soll primär für **Windows-PCs** entwickelt werden.

Zielplattform:

* Windows 10 / Windows 11
* Desktop-PC oder Laptop
* 3D-Grafik mit blockbasierter Klötzchen-Optik
* Maus/Tastatur als primäre Steuerung
* Controller-Unterstützung optional später möglich

---

## 3.2 Server

Der dedizierte Spielserver soll auf mehreren Plattformen lauffähig sein:

* Windows x64
* Linux x64
* Linux ARM64
* Raspberry Pi 5 unter Linux
* optional Docker

Besonders wichtig:

> Der Server soll von Spielern selbst gehostet werden können.

Das bedeutet, ein Spieler soll später in der Lage sein, einen Spacecraft-Server selbst zu starten, zum Beispiel auf:

* seinem eigenen Windows-PC
* einem Raspberry Pi 5
* einem Mini-PC
* einem Linux-Server
* einem gemieteten VPS
* einem NAS, sofern kompatibel

---

# 4. Empfohlener Technologie-Stack

## 4.1 Client

Für den Spielclient wird empfohlen:

* **Engine:** Unity
* **Programmiersprache:** C#
* **Ziel:** Windows-3D-Spiel mit blockbasierter Welt, Raumschiff-Innenräumen, UI, Hotbar, Inventar, Crafting-Oberflächen, Animationen, Audio und Effekten

Unity eignet sich für den Client, weil:

* C# als Hauptsprache genutzt wird
* 3D-Szenen, UI und Input gut unterstützt werden
* Windows als Zielplattform sehr gut unterstützt wird
* später Multiplayer-Anbindung möglich ist
* der gleiche Sprachraum wie im Backend genutzt werden kann

---

## 4.2 Server

Für den Server wird empfohlen:

* **Programmiersprache:** C#
* **Runtime:** .NET, bevorzugt aktuelle LTS-Version
* **Meta/API-Backend:** ASP.NET Core
* **Game-Server:** eigenständiger .NET Dedicated Server mit eigener Spiel-Tick-Logik
* **Standarddatenbank:** SQLite
* **Optionale spätere Datenbank:** PostgreSQL
* **Realtime-Kommunikation:** UDP-basiert oder vergleichbare leichte Realtime-Kommunikation
* **Admin/API-Kommunikation:** HTTP/WebSocket

Wichtig:

> Der Server soll kein Unity-Headless-Server als Standard sein.

Der Server soll bewusst ohne Unity-Runtime funktionieren, damit er leichtgewichtig bleibt und auch auf einem Raspberry Pi 5 betrieben werden kann.

---

# 5. Gesamtarchitektur

Die Zielarchitektur besteht aus drei Hauptteilen:

```text
Spacecraft Client
Unity + C#
Windows-Spiel
Darstellung, Steuerung, UI, Audio, Effekte

        ↕ Netzwerk

Spacecraft Dedicated Game Server
C# / .NET
Autoritative Spielwelt, Chunks, Spieler, Schiff, Inventar, Crafting

        ↕

Persistenz / Meta-Backend
ASP.NET Core + SQLite
Spielstände, Serverstatus, Konfiguration, Admin-Funktionen
```

---

# 6. Client-Anforderungen

Der Client ist für Darstellung und Interaktion zuständig.

## 6.1 Aufgaben des Clients

Der Client übernimmt:

* Rendering der 3D-Welt
* Darstellung der Klötzchenlandschaft
* Darstellung des Raumschiff-Innenraums
* Spielersteuerung
* Kamera
* Animationen
* Audio
* Partikeleffekte
* Benutzeroberfläche
* Hotbar
* Inventaranzeige
* Crafting-Oberfläche
* Sternenkarte im Cockpit
* lokale Vorschau beim Platzieren von Blöcken
* Anzeige von Serverzuständen

---

## 6.2 Was der Client nicht alleine entscheiden darf

Der Client darf nicht endgültig entscheiden:

* ob ein Block wirklich abgebaut wurde
* welche Rohstoffe ein Spieler bekommt
* ob ein Item hergestellt wurde
* ob ein Spieler genug Materialien besitzt
* ob ein Schiff erweitert wurde
* ob Sauerstoff verbraucht wurde
* ob Schaden entstanden ist
* ob eine Blaupause freigeschaltet wurde
* ob ein Spieler teleportiert, gelandet oder gereist ist

Diese Entscheidungen müssen vom Server validiert werden.

---

# 7. Server-Anforderungen

Der Server ist die autoritative Quelle des Spielzustands.

## 7.1 Aufgaben des Dedicated Game Servers

Der Game Server verwaltet:

* aktive Spielwelt
* aktive Planeteninstanz
* aktive Spieler
* Spielerpositionen
* Spieleraktionen
* Blockabbau
* Blockplatzierung
* Chunk-Zustand
* Inventare
* Schiffsfrachtraum
* Crafting während der Session
* Sauerstoffverbrauch
* Umweltgefahren
* Schaden und Heilung
* Schiffszustand
* Raumschiffmodule
* Landezustand
* Außenposten
* einfache Kreaturen oder Gegner, falls später vorhanden
* Synchronisation zwischen mehreren Spielern

---

## 7.2 Server-Tick

Der Game Server soll mit einem eigenen Spiel-Tick arbeiten.

Da Spacecraft kein schneller Shooter ist, muss die Tickrate nicht extrem hoch sein.

Empfohlene Zielwerte:

* 10 bis 20 Ticks pro Sekunde für frühe Versionen
* höhere Frequenz nur bei Bedarf
* Fokus auf Stabilität, geringe Last und saubere Synchronisation

Der Server muss in der Lage sein, pro Tick Spieleraktionen zu verarbeiten, Weltänderungen zu berechnen und relevante Zustände an Clients zu senden.

---

## 7.3 Autoritative Serverlogik

Der Server muss jede relevante Aktion prüfen.

Beispiele:

### Blockabbau

Der Server prüft:

* Ist der Spieler nah genug am Block?
* Existiert der Block?
* Ist der Block abbaubar?
* Hat der Spieler das passende Werkzeug?
* Hat das Werkzeug genug Energie?
* Ist der Spieler in einem erlaubten Zustand?
* Passt die Abbaudauer?
* Welche Ressourcen entstehen?
* Wohin werden die Ressourcen übertragen?

### Crafting

Der Server prüft:

* Ist das Rezept freigeschaltet?
* Befindet sich der Spieler an der passenden Station?
* Sind die benötigten Materialien vorhanden?
* Liegen Materialien im Spielerinventar oder Schiffsfrachtraum?
* Gibt es genug Platz für das Ergebnis?
* Werden Materialien korrekt verbraucht?
* Wird das Ergebnis korrekt erzeugt?

### Schiffserweiterung

Der Server prüft:

* Ist die Blaupause freigeschaltet?
* Sind alle Ressourcen vorhanden?
* Ist genügend Platz oder Modulkapazität vorhanden?
* Sind Pflichtmodule weiterhin erreichbar?
* Verändert sich der Schiffszustand korrekt?
* Wird die Erweiterung gespeichert?

---

# 8. Self-Hosting-Anforderungen

Ein zentrales Ziel ist, dass Spieler eigene Server hosten können.

## 8.1 Serverpakete

Der Server soll später in mehreren Varianten ausgeliefert werden:

* Windows x64 ZIP
* Linux x64 TAR/ZIP
* Linux ARM64 TAR/ZIP für Raspberry Pi 5
* optional Docker-Image

Der Spieler soll den Server möglichst einfach starten können.

Beispielhafte Zielerfahrung:

```text
1. Serverpaket herunterladen
2. Entpacken
3. Startdatei ausführen
4. Welt erstellen
5. Freunde verbinden sich
```

---

## 8.2 Server-Modi

Der Server soll mehrere Nutzungsarten unterstützen.

### Lokaler Singleplayer-Server

Für Singleplayer startet das Spiel lokal eine Serverinstanz im Hintergrund.

Ziel:

* Singleplayer nutzt dieselbe Spiellogik wie Multiplayer
* keine doppelte Logik zwischen Singleplayer und Multiplayer
* spätere Multiplayer-Erweiterung bleibt einfach

---

### LAN-Server

Ein Spieler startet einen Server im Heimnetzwerk.

Andere Spieler können sich per LAN verbinden.

Ziel:

* einfacher Koop-Betrieb
* ideal für private kleine Gruppen
* Raspberry Pi 5 als Heimserver möglich

---

### Internet-Server

Ein Spieler kann den Server öffentlich oder halböffentlich bereitstellen.

Dafür benötigt der Server:

* Serverpasswort
* Whitelist
* Admin-Passwort
* konfigurierbare Ports
* verständliche Netzwerkeinstellungen
* klare Anzeige, welche Ports freigegeben werden müssen

---

# 9. Raspberry-Pi-5-Anforderungen

Der Server soll auf einem Raspberry Pi 5 unter Linux lauffähig sein.

## 9.1 Wichtige Einschränkungen

Da der Raspberry Pi 5 ein kleiner ARM64-Server ist, muss der Server ressourcenschonend sein.

Wichtig:

* keine Unity-Runtime auf dem Server
* kein Rendering auf dem Server
* keine Grafikabhängigkeiten
* keine unnötige Physiksimulation
* geringe CPU-Last
* sparsame RAM-Nutzung
* geringe Schreiblast auf Datenträger
* bevorzugt SSD statt microSD für Dauerbetrieb

---

## 9.2 Zielgröße für Pi-Hosting

Für eine frühe Version sollte der Pi-Server realistisch ausgelegt sein auf:

* 1 bis 4 Spieler sehr gut
* 4 bis 8 Spieler mit Optimierung möglich
* mehr als 8 Spieler nicht als erstes Ziel

Der Server soll Einstellungen anbieten, um Last zu begrenzen:

* maximale Spielerzahl
* Sichtweite in Chunks
* maximale aktive Chunks pro Spieler
* Autosave-Intervall
* maximale aktive Kreaturen
* maximale aktive Außenposten
* maximale Simulationstiefe entfernter Gebiete

---

# 10. Datenhaltung und Speicherstrategie

## 10.1 Standarddatenbank

Als Standard für selbst gehostete Server wird **SQLite** empfohlen.

Grund:

* keine separate Datenbankinstallation notwendig
* einfache Weltdateien
* leicht zu sichern
* leicht zu kopieren
* ideal für private Server
* ideal für Raspberry Pi 5 und kleine Heimserver

---

## 10.2 Optionale spätere Datenbank

Für größere Server kann später PostgreSQL unterstützt werden.

PostgreSQL sollte aber nicht Voraussetzung für normale Spieler sein.

---

## 10.3 Savegame-Struktur

Savegames sollen portabel sein.

Empfohlene Struktur:

```text
spacecraft-server/
  config/
    server.json
  saves/
    world_001/
      world.db
      chunks/
      backups/
      logs/
```

Ziel:

* Welten können kopiert werden
* Welten können gesichert werden
* Welten können auf andere Server übertragen werden
* Backups sind nachvollziehbar

---

# 11. Prozedurale Welt- und Chunk-Speicherung

Die Welten sollen prozedural generiert werden.

Wichtig:

> Es soll nicht jeder Block einer Welt dauerhaft gespeichert werden.

Stattdessen:

```text
Welt = Seed + Weltparameter + gespeicherte Spieleränderungen
```

## 11.1 Gespeichert werden sollen

* Planet-Seed
* Weltparameter
* Biome
* Rohstoffverteilung
* entdeckte Orte
* abgebaute Blöcke
* platzierte Blöcke
* gebaute Außenposten
* Kisteninhalte
* Maschinenzustände
* Landepunkte
* besondere Funde
* Spieleränderungen am Terrain

## 11.2 Nicht dauerhaft gespeichert werden muss

* jeder unveränderte natürliche Block
* vollständig generierte Planetenoberfläche
* unveränderte Höhlen
* unveränderte Rohstoffvorkommen

Diese Strategie reduziert Speicherbedarf und macht den Server besser für Raspberry Pi 5 geeignet.

---

# 12. Netzwerk-Anforderungen

## 12.1 Gameplay-Kommunikation

Für aktives Gameplay wird eine leichte Realtime-Kommunikation benötigt.

Diese Kommunikation muss geeignet sein für:

* Spielerbewegung
* Blickrichtung
* Werkzeugnutzung
* Blockabbau
* Blockplatzierung
* einfache Kampfaktionen
* Weltzustandsupdates
* Chunk-Updates

Empfohlen:

* UDP-basiertes Protokoll oder vergleichbare Game-Networking-Lösung
* zuverlässige Nachrichten für wichtige Aktionen
* unzuverlässige schnelle Nachrichten für Positionsupdates

---

## 12.2 Meta-Kommunikation

Für langsamere Verwaltungsfunktionen eignet sich HTTP/WebSocket.

Verwendung:

* Login oder Spielername
* Serverstatus
* Weltliste
* Admin-Oberfläche
* Speicherstände
* Konfiguration
* Chat oder Lobby
* Servermeldungen

---

## 12.3 Verbindungsprinzip

Der Client verbindet sich mit einem Server über:

* IP-Adresse und Port
* LAN-Serverliste optional
* Favoritenliste optional
* Serverpasswort optional

---

# 13. Admin-Weboberfläche

Der selbst gehostete Server soll eine einfache Admin-Weboberfläche besitzen.

## 13.1 Zweck

Der Host soll den Server ohne tiefes technisches Wissen verwalten können.

## 13.2 Funktionen

Die Admin-Oberfläche soll mindestens bieten:

* Serverstatus anzeigen
* aktive Spieler anzeigen
* Welt starten
* Welt stoppen
* Servername ändern
* maximale Spielerzahl einstellen
* Passwort setzen
* Whitelist verwalten
* Logs ansehen
* Backups erstellen
* Backup wiederherstellen
* Savegame exportieren
* Server herunterfahren
* Neustart auslösen
* aktuelle Auslastung anzeigen

---

## 13.3 Sicherheit der Admin-Oberfläche

Die Admin-Oberfläche darf nicht ungeschützt öffentlich erreichbar sein.

Mindestanforderungen:

* Admin-Passwort
* standardmäßig nur lokaler oder LAN-Zugriff
* getrennte Admin- und Gameplay-Ports
* keine Admin-Funktionen ohne Authentifizierung
* klare Warnung, wenn Admin-Port öffentlich erreichbar ist

---

# 14. Konfiguration

Der Server soll über eine einfache Konfigurationsdatei steuerbar sein.

Beispielhafte Konfigurationswerte:

```text
ServerName
WorldName
GameplayPort
AdminPort
MaxPlayers
ServerPassword
WhitelistEnabled
AutoSaveIntervalMinutes
ViewDistanceChunks
MaxLoadedChunksPerPlayer
Difficulty
AllowGuests
BackupIntervalMinutes
AdminBindAddress
```

Diese Einstellungen sollen zusätzlich über die Admin-Weboberfläche bearbeitbar sein.

---

# 15. Sicherheitsanforderungen

Da Spieler eigene Server hosten und Clients sich verbinden, muss die Serverlogik grundlegende Sicherheitsprinzipien beachten.

## 15.1 Client nicht vertrauen

Der Client darf nie unvalidiert bestimmen:

* erhaltene Ressourcen
* Itemmengen
* Teleportpositionen
* freigeschaltete Blaupausen
* erfolgreiche Crafting-Vorgänge
* Schiffserweiterungen
* Schadensfreiheit
* Sauerstoffzustand
* Weltänderungen

---

## 15.2 Serverseitige Validierung

Jede Aktion muss serverseitig geprüft werden.

Beispiele:

* Reichweite
* Werkzeugtyp
* Werkzeugzustand
* Materialverfügbarkeit
* Inventarkapazität
* Frachtraumkapazität
* benötigte Station
* Spielerstatus
* Weltstatus
* Berechtigungen

---

## 15.3 Server-Zugriff

Der Server soll unterstützen:

* Serverpasswort
* Whitelist
* Admin-Passwort
* Kick/Ban später optional
* getrennte Adminrechte
* sichere Savegame-Verwaltung
* automatische Backups

---

# 16. Persistenz-Anforderungen

## 16.1 Zu speichernde Daten

Der Server muss speichern können:

* Spielerprofile
* Spielerpositionen
* Spielerinventare
* Schiffszustand
* Schiffsräume
* Schiffsmodule
* Frachtraum
* freigeschaltete Rezepte
* freigeschaltete Blaupausen
* Tech-Tree-Fortschritt
* bekannte Sternensysteme
* bekannte Planeten
* gescannte Planetendaten
* Außenposten
* gebaute Strukturen
* veränderte Chunks
* Kisten und Container
* Weltmetadaten
* Serverkonfiguration

---

## 16.2 Autosave

Der Server soll regelmäßig automatisch speichern.

Anforderungen:

* konfigurierbares Autosave-Intervall
* manuelles Speichern über Admin-Oberfläche
* Speichern beim ordentlichen Herunterfahren
* Schutz gegen beschädigte Savegames
* idealerweise temporäre Schreibdatei und danach atomarer Austausch
* regelmäßige Backup-Erstellung

---

# 17. Logging und Diagnose

Der Server soll verständliche Logs schreiben.

## 17.1 Log-Inhalte

Logs sollten enthalten:

* Serverstart
* Serverstopp
* geladene Welt
* verbundene Spieler
* getrennte Spieler
* Fehler
* Warnungen
* Speichervorgänge
* Backup-Vorgänge
* Netzwerkprobleme
* ungültige Clientaktionen
* Performance-Warnungen

---

## 17.2 Admin-Anzeige

Die wichtigsten Logs sollen in der Admin-Weboberfläche sichtbar sein.

---

# 18. Performance-Anforderungen

## 18.1 Allgemeine Ziele

Der Server soll ressourcenschonend sein.

Wichtige Ziele:

* geringe CPU-Last im Leerlauf
* niedrige RAM-Nutzung
* keine unnötigen Weltbereiche simulieren
* nur relevante Chunks aktiv halten
* entfernte Gebiete einfrieren oder stark vereinfacht simulieren
* Weltgenerierung nicht unnötig wiederholen
* Savegame-Schreiblast begrenzen

---

## 18.2 Chunk-Management

Der Server soll Chunks abhängig von Spielerpositionen laden.

Anforderungen:

* Chunks in Spielnähe aktiv halten
* entfernte Chunks entladen
* Änderungen vor Entladen speichern
* maximale aktive Chunks begrenzen
* Sichtweite pro Server konfigurierbar machen
* Chunk-Daten effizient serialisieren

---

# 19. Modulare Projektstruktur

Das Projekt sollte logisch modular aufgebaut werden.

Empfohlene Module:

```text
Spacecraft.Client
Unity-Spielclient

Spacecraft.GameServer
Dedizierter Spielserver

Spacecraft.Api
Admin/API-Backend

Spacecraft.Shared
Gemeinsame Datenmodelle und Definitionen

Spacecraft.Persistence
Speicherung, SQLite/PostgreSQL-Abstraktion

Spacecraft.WorldGeneration
Seed-basierte Welt- und Chunk-Generierung

Spacecraft.Networking
Nachrichten, Protokolle, Verbindungslogik

Spacecraft.Tools
Servertools, Export, Backup, Debugging
```

Wichtig:

Gemeinsame Definitionen wie Itemtypen, Blocktypen, Rezepte und Schiffsmodule sollten zentral verwaltet werden, damit Client und Server nicht auseinanderlaufen.

---

# 20. Datengetriebene Definitionen

Viele Spielinhalte sollten nicht fest im Code verdrahtet sein.

Datengetrieben definiert werden sollten:

* Blocktypen
* Items
* Rohstoffe
* Rezepte
* Werkzeuge
* Raumanzugmodule
* Schiffsmodule
* Tech-Tree-Knoten
* Planetenarten
* Biome
* Rohstoffvorkommen
* Umweltgefahren
* Scandaten
* Außenpostenmodule

Ziel:

* Inhalte können leichter erweitert werden
* Balancing ist einfacher
* Entwickler müssen nicht für jede Rezeptänderung Spiellogik anfassen
* spätere Modding-Fähigkeit wird möglich

---

# 21. MVP-Anforderungen

Eine erste technische Version muss nicht das gesamte Spiel enthalten. Sie soll aber die Kernarchitektur beweisen.

## 21.1 MVP-Client

Der Client soll können:

* Verbindung zu lokalem oder externem Server herstellen
* einfache 3D-Klötzchenwelt anzeigen
* Spieler bewegen
* Blöcke abbauen
* Blöcke platzieren
* Hotbar anzeigen
* Inventar anzeigen
* Raumschiff betreten
* einfache Werkstatt benutzen
* einfache Sternenkarte anzeigen

---

## 21.2 MVP-Server

Der Server soll können:

* Welt mit Seed erzeugen
* Chunks laden und entladen
* Spieler verbinden lassen
* Spielerpositionen verwalten
* Blockabbau validieren
* Blockplatzierung validieren
* Inventar verwalten
* einfache Rezepte craften
* Schiffsfrachtraum verwalten
* Weltänderungen speichern
* Savegame laden
* Autosave durchführen
* lokal und im LAN laufen

---

## 21.3 MVP-Self-Hosting

Der Server soll im MVP mindestens laufen auf:

* Windows x64
* Linux x64
* Linux ARM64 / Raspberry Pi 5

MVP-Ziel:

* Serverpaket entpacken
* Server starten
* Client verbindet sich per IP
* Welt wird gespeichert
* Welt kann erneut geladen werden

---

# 22. Nicht-Ziele für die erste Version

Für die erste technische Version sind folgende Dinge nicht zwingend erforderlich:

* öffentliche zentrale Serverliste
* Account-System mit Online-Login
* MMO-Struktur
* große öffentliche Multiplayer-Server
* komplexer Raumkampf
* umfangreiche Gegner-KI
* Crew-System
* Modding-System
* vollständige Storykampagne
* hochkomplexe Physik
* vollständige Automatisierung
* Cloud-Speicherung

Diese Funktionen können später ergänzt werden, sollten aber die Kernarchitektur nicht blockieren.

---

# 23. Wichtige Architekturentscheidungen

## 23.1 Kein Client-authoritativer Multiplayer

Der Client darf nicht die Spielwahrheit besitzen.

Grund:

* verhindert Cheating
* erleichtert Multiplayer
* sorgt für konsistente Weltzustände
* macht Self-Hosting robuster

---

## 23.2 Kein Unity-Server als Standard

Der Server soll nicht als Unity-Headless-Server ausgeliefert werden.

Grund:

* zu schwergewichtig für Raspberry Pi 5
* unnötige Engine-Abhängigkeit
* schlechter für Self-Hosting
* schwieriger für kleine Serverpakete
* weniger geeignet für reine serverseitige Simulation

---

## 23.3 SQLite als Standard

SQLite soll die Standarddatenbank für private Server sein.

Grund:

* einfach
* portabel
* keine Installation eines Datenbankservers
* ideal für Heimserver und Raspberry Pi
* Savegames leicht kopierbar

---

## 23.4 Gemeinsame Sprache C#

Client und Server sollen beide C# verwenden.

Grund:

* Unity nutzt C#
* Server kann in .NET gebaut werden
* gemeinsame Modelle einfacher
* geringerer Kontextwechsel
* konsistentere Spielregeln

---

# 24. Zusammenfassung der technischen Zielarchitektur

Spacecraft soll technisch als client-server-basiertes Spiel entwickelt werden.

Der Client ist ein Unity-Windows-Spiel in C#.

Der Server ist ein eigenständiger, leichtgewichtiger C#/.NET Dedicated Server, der auch auf Linux ARM64 und Raspberry Pi 5 laufen soll. Er verwaltet autoritativ Spielwelt, Chunks, Inventar, Crafting, Schiffszustand und Multiplayer-Synchronisation.

Für Self-Hosting soll der Server einfach ausgeliefert und gestartet werden können. Standardmäßig nutzt er SQLite für portable Savegames. Eine Admin-Weboberfläche auf Basis von ASP.NET Core soll Serververwaltung, Backups, Konfiguration und Statusanzeige ermöglichen.

Die wichtigste technische Leitlinie lautet:

> Der Unity-Client ist Darstellung und Eingabe.
> Der .NET-Server ist die Wahrheit der Spielwelt.

Diese Entscheidung stellt sicher, dass Spacecraft von Anfang an sauber auf Multiplayer, LAN-Hosting, Raspberry-Pi-Hosting und private dedizierte Server vorbereitet ist.
