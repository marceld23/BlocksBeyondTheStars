Ich habe die Browser-Anforderung als **zusätzliches Entwicklerdokument** formuliert. Grundlage: Unitys Web-Plattform unterstützt Desktop-Browser mit WebGL 2, HTML5 und WebAssembly; Web-Builds haben aber wichtige Grenzen, vor allem bei nativen Sockets, .NET-Netzwerkklassen und C#-Threads. Für Browser-Multiplayer muss daher WebSocket/WebRTC bzw. ein Gateway eingeplant werden, nicht derselbe native UDP-Weg wie beim Windows-Client. ([Unity Dokumentation][1])

# Spacecraft – Zusatzanforderung: Optionaler Webclient / Browser-Client

## 1. Ziel dieses Dokuments

Dieses Dokument beschreibt eine zusätzliche technische und produktseitige Anforderung für **Spacecraft**:

Neben dem nativen Windows-Client soll nach Möglichkeit auch ein **Webclient** beziehungsweise **Browser-Client** unterstützt werden.

Die Grundidee ist:

> Ein Spieler oder Serverbetreiber startet irgendwo einen Spacecraft-Server.
> Andere Spieler können sich entweder den nativen Client herunterladen oder direkt im Browser spielen.

Der Webclient soll besonders dann wichtig sein, wenn ein Spieler kein Windows-System besitzt oder keinen nativen Client installieren möchte.

---

# 2. Grundidee des Webclients

Spacecraft soll grundsätzlich als selbst hostbares Spiel funktionieren.

Ein Serverbetreiber startet den Spacecraft-Server zum Beispiel auf:

* Windows-PC
* Linux-Server
* Raspberry Pi 5
* Mini-PC
* VPS
* NAS, sofern kompatibel

Danach soll der Server idealerweise eine kleine Web-Startseite anbieten.

Diese Startseite könnte zwei Hauptoptionen zeigen:

1. **Client herunterladen**
2. **Im Browser spielen**

Beispielhafte Startseite:

```text
Willkommen auf Marcels Spacecraft-Server

[Windows-Client herunterladen]
[Linux-Client herunterladen, falls später verfügbar]
[Im Browser spielen]
[Serverstatus anzeigen]
```

Damit wird der Server zu einem kleinen Einstiegspunkt für Spieler.

---

# 3. Zweck des Browser-Clients

Der Browser-Client soll mehrere Ziele erfüllen.

## 3.1 Niedrige Einstiegshürde

Spieler sollen ohne Installation ausprobieren können, ob sie dem Server beitreten möchten.

Besonders hilfreich für:

* Freunde, die nur kurz mitspielen wollen
* Spieler ohne Windows-PC
* Linux- oder macOS-Nutzer
* Schul-/Familien-/LAN-Situationen
* Tests während der Entwicklung
* schnelle Demos

---

## 3.2 Plattformunabhängiger Zugang

Der native Hauptclient ist zunächst Windows-orientiert.

Der Browser-Client soll eine alternative Zugriffsmöglichkeit bieten für:

* macOS
* Linux
* eventuell Chromebooks
* eventuell moderne Tablets
* eventuell leistungsfähige Smartphones, sofern sinnvoll

Wichtig:

Der Browser-Client soll nicht automatisch dieselbe Leistungsfähigkeit wie der Windows-Client haben. Er ist ein zusätzlicher Zugang, nicht zwingend die Hauptplattform.

---

## 3.3 Einfaches Self-Hosting-Erlebnis

Der Serverbetreiber soll nicht zusätzlich eine komplizierte Website hosten müssen.

Idealerweise kann der Spacecraft-Server selbst ausliefern:

* Webclient-Dateien
* Downloadlinks für native Clients
* Serverstatus
* einfache Verbindungsinformationen
* optional Admin- oder Login-Seite

---

# 4. Machbarkeitsprüfung als feste Anforderung

Bevor der Webclient vollständig zugesagt wird, muss eine technische Machbarkeitsprüfung erfolgen.

Diese Prüfung ist Teil der Anforderungen.

Der Entwickler soll prüfen:

* Kann der Unity-Client sinnvoll als Web-Build erzeugt werden?
* Welche Spielfunktionen funktionieren im Browser unverändert?
* Welche Spielfunktionen benötigen Anpassungen?
* Welche Netzwerkverbindung ist im Browser möglich?
* Wie groß wird der Webclient?
* Wie schnell lädt der Webclient?
* Wie viel Speicher benötigt er im Browser?
* Wie gut funktioniert die Klötzchenwelt im Browser?
* Wie viele Chunks können im Browser dargestellt werden?
* Welche Browser werden realistisch unterstützt?
* Welche mobilen Browser sind sinnvoll nutzbar?
* Welche Funktionen müssen im Webclient reduziert werden?
* Wie wird der Webclient vom Server ausgeliefert?
* Wie wird der Webclient mit dem Game-Server verbunden?

Das Ergebnis der Prüfung soll eine klare Entscheidung ermöglichen:

1. Webclient voll umsetzbar
2. Webclient mit Einschränkungen umsetzbar
3. Webclient nur als Lite-Version sinnvoll
4. Webclient vorerst nicht sinnvoll

---

# 5. Erwartete Einschränkungen des Browser-Clients

Der Browser-Client wird voraussichtlich nicht vollständig identisch mit dem nativen Windows-Client sein.

## 5.1 Netzwerk-Einschränkungen

Browser erlauben in der Regel keinen direkten Zugriff auf native IP-Sockets wie ein normales Desktop-Spiel.

Das bedeutet:

* Der Browser-Client kann nicht einfach denselben nativen UDP-Netzwerkweg nutzen wie ein Windows-Client.
* Der Browser-Client benötigt voraussichtlich WebSocket, WebRTC oder eine ähnliche browserkompatible Verbindung.
* Der Server muss dafür einen zusätzlichen Netzwerkzugang bereitstellen.
* Alternativ benötigt der Server ein WebSocket-Gateway.

Anforderung:

> Die Serverarchitektur muss so geplant werden, dass sowohl native Clients als auch Browser-Clients unterstützt werden können.

---

## 5.2 Performance-Einschränkungen

Ein Browser-Client kann schwächer sein als ein nativer Client.

Mögliche Einschränkungen:

* weniger aktive Chunks
* geringere Sichtweite
* reduzierte Schatten
* einfachere Partikeleffekte
* reduzierte Texturauflösung
* geringere Objektanzahl
* vereinfachtes Terrain-Rendering
* geringere Ziel-Framerate
* weniger parallele Hintergrundverarbeitung
* stärkeres Streaming von Assets

Anforderung:

> Der Webclient darf eigene Qualitäts- und Performance-Einstellungen besitzen.

---

## 5.3 Speicher- und Ladezeit-Einschränkungen

Browser-Spiele müssen geladen werden, bevor sie spielbar sind.

Das betrifft:

* Initialdownload
* Texturen
* Sounds
* Modelle
* Weltdaten
* UI
* Shader
* Spielcode

Anforderung:

> Der Webclient muss so gebaut werden, dass Ladezeiten und Downloadgröße kontrolliert bleiben.

Mögliche Maßnahmen:

* reduzierte Asset-Pakete
* Streaming von Assets
* getrennte Asset-Bundles
* Browser-spezifische Texturgrößen
* Ladebildschirm mit Fortschritt
* Cache-Nutzung
* Server prüft kompatible Versionen

---

## 5.4 Eingabe und Steuerung

Der Webclient muss Maus und Tastatur unterstützen.

Besondere Punkte:

* Browser können bestimmte Tastenkombinationen abfangen.
* Vollbildmodus kann Einschränkungen haben.
* Maus-Lock muss sauber funktionieren.
* Tastaturlayouts können sich anders verhalten.
* Mobile Touch-Steuerung ist optional und nicht Kernanforderung.

Anforderung:

> Der Webclient muss mindestens Maus- und Tastatursteuerung für Desktop-Browser unterstützen.

Touch-Steuerung kann später geprüft werden.

---

## 5.5 Mobile Browser

Mobile Browser sollen nicht als erste Zielplattform betrachtet werden.

Grund:

* begrenzte Leistung
* kleiner Bildschirm
* Touch-Steuerung nötig
* höhere Speichergrenzen
* schwierigeres UI
* schlechtere Bedienbarkeit für ein 3D-Crafting-Spiel

Anforderung:

> Der Browser-Client soll zuerst für Desktop-Browser entwickelt und getestet werden. Mobile Nutzung ist optional und nur nach separater Prüfung relevant.

---

# 6. Zielarchitektur mit Webclient

Die bestehende Architektur wird erweitert.

## 6.1 Bisherige Architektur

```text
Unity Windows Client
        ↕
Spacecraft Dedicated Server
C# / .NET
        ↕
SQLite / Persistenz
```

## 6.2 Erweiterte Architektur mit Webclient

```text
Native Clients
Windows-Client
        ↕
Native Gameplay-Verbindung

Browser-Client
Unity Web Build oder alternative Web-Umsetzung
        ↕
WebSocket/WebRTC/Gateway-Verbindung

        ↕
Spacecraft Dedicated Server
C# / .NET
Autoritative Spielwelt

        ↕
SQLite / Persistenz
```

---

# 7. Server-Anforderungen für Webclient-Unterstützung

Der Spacecraft-Server soll später nicht nur Game-Server sein, sondern auch Einstiegspunkt für Clients.

## 7.1 Webportal des Servers

Der Server soll eine einfache Weboberfläche bereitstellen können.

Diese Weboberfläche soll zeigen:

* Servername
* Serverstatus
* aktive Spieler
* Version des Servers
* unterstützte Client-Version
* Downloadlink für nativen Client
* Button „Im Browser spielen“
* Hinweis auf Passwort/Whitelist, falls aktiv
* einfache Verbindungsdaten
* optional Changelog oder Servernachricht

---

## 7.2 Auslieferung des Webclients

Der Server soll optional den Webclient selbst ausliefern können.

Beispiel:

```text
http://server-ip:port/play
```

oder bei HTTPS:

```text
https://server-domain/play
```

Der Webclient wird dann direkt im Browser geladen.

Anforderung:

> Der selbst gehostete Server soll den Browser-Client möglichst ohne zusätzliche Webserver-Installation bereitstellen können.

Optional kann der Webclient auch über einen separaten Webserver ausgeliefert werden. Das soll aber nicht zwingend erforderlich sein.

---

## 7.3 Client-Downloads

Der Server soll außerdem native Clients verlinken können.

Beispiele:

* Windows-Client herunterladen
* später Linux-Client herunterladen
* später macOS-Client herunterladen, falls unterstützt

Diese Downloadlinks können entweder lokal vom Server bereitgestellt oder auf eine offizielle Downloadseite verweisen.

---

# 8. Netzwerk-Anforderungen für Browser-Client

## 8.1 Native Clients

Native Clients können weiterhin über den geplanten nativen Realtime-Kanal kommunizieren.

Beispiele:

* UDP-basiertes Protokoll
* zuverlässige und unzuverlässige Nachrichten
* optimiert für Gameplay

---

## 8.2 Browser-Client

Der Browser-Client benötigt einen browserkompatiblen Kommunikationsweg.

Mögliche Varianten:

### Variante A: WebSocket-Gateway

Der Server bietet zusätzlich zum nativen Game-Port einen WebSocket-Port an.

```text
Browser-Client
    ↕ WebSocket
Spacecraft WebSocket Gateway
    ↕ interne Serverlogik
Game Server
```

Vorteile:

* relativ einfach
* gut kompatibel mit Browsern
* gut für selbst gehostete Server
* passt zu ASP.NET Core

Nachteile:

* nicht identisch mit UDP
* eventuell höhere Latenz
* andere Optimierung nötig

---

### Variante B: WebRTC

Browser-Client nutzt WebRTC.

Vorteile:

* kann für Echtzeitkommunikation geeignet sein
* unterstützt teils unzuverlässige Datenkanäle
* potenziell besser für Gameplay als reines WebSocket

Nachteile:

* deutlich komplexer
* benötigt Signaling
* NAT/Firewall-Themen
* für Self-Hosting schwerer zu erklären
* eventuell zusätzlicher Infrastrukturbedarf

---

### Empfehlung für erste Prüfung

Für eine erste Webclient-Machbarkeit sollte vorrangig ein **WebSocket-Gateway** geprüft werden.

Grund:

* einfacher für Self-Hosting
* leichter in ASP.NET Core integrierbar
* verständlicher für private Server
* besser kontrollierbar
* weniger Infrastruktur als WebRTC

---

# 9. Anforderungen an die gemeinsame Spiellogik

Der Webclient darf keine eigene abweichende Spiellogik erhalten.

Wichtig:

> Der Webclient ist nur ein weiterer Client.
> Die Spielwahrheit bleibt beim Server.

Das bedeutet:

* Blockabbau wird weiterhin serverseitig validiert.
* Crafting wird weiterhin serverseitig validiert.
* Inventar wird weiterhin serverseitig verwaltet.
* Missionen werden weiterhin serverseitig geprüft.
* Belohnungen werden weiterhin serverseitig ausgezahlt.
* Schiffsausbau wird weiterhin serverseitig entschieden.
* Weltänderungen werden weiterhin serverseitig gespeichert.

Der Webclient darf also nicht aus Komfortgründen client-authoritativ werden.

---

# 10. Anforderungen an Client-Parität

Der Webclient soll möglichst viele Kernfunktionen des Windows-Clients unterstützen.

## 10.1 Muss-Funktionen für Webclient-MVP

Ein erster Webclient sollte können:

* Server im Browser öffnen
* Verbindung zum Server herstellen
* Spieler bewegen
* einfache Klötzchenwelt anzeigen
* Chunks laden
* Blöcke abbauen
* Blöcke platzieren
* Hotbar anzeigen
* Inventar öffnen
* einfache Items verwenden
* Raumschiff betreten
* Missionscomputer öffnen, falls bereits vorhanden
* einfache Crafting-Oberfläche nutzen
* Chat oder einfache Servermeldungen anzeigen
* Verbindung sauber trennen

---

## 10.2 Sollte-Funktionen

Später sollte der Webclient auch können:

* Sternenkarte nutzen
* Planeten auswählen
* landen und aussteigen
* Schiffsräume anzeigen
* Schiffserweiterungen anzeigen
* Missionen erstellen
* Belohnungsdepot verwenden
* Admin-Hinweise sehen
* einfache Grafikoptionen ändern
* Audio aktivieren/deaktivieren
* Vollbildmodus nutzen

---

## 10.3 Kann-Funktionen

Optional:

* Touch-Steuerung
* Controller im Browser
* PWA-Installation
* Offline-Cache für Clientdaten
* Browser-Benachrichtigungen
* WebRTC statt WebSocket
* reduzierte mobile Version
* reine Zuschaueransicht im Browser

---

# 11. Unterschiede zwischen Windows-Client und Webclient

Der Entwickler soll klar prüfen und dokumentieren, wo beide Clients voneinander abweichen.

## 11.1 Mögliche Unterschiede

| Bereich        | Windows-Client                 | Webclient                   |
| -------------- | ------------------------------ | --------------------------- |
| Leistung       | höher                          | niedriger                   |
| Netzwerk       | nativer Realtime-Kanal möglich | WebSocket/WebRTC nötig      |
| Dateizugriff   | normal möglich                 | browserbeschränkt           |
| Speicher       | mehr verfügbar                 | stärker begrenzt            |
| Ladezeit       | lokal installiert              | Download beim Start         |
| Grafikqualität | höher                          | reduziert                   |
| Modding        | später einfacher               | eingeschränkt               |
| Vollbild       | stabiler                       | browserabhängig             |
| Eingabe        | vollständiger                  | browserabhängig             |
| Updates        | Installer/Launcher             | serverseitig aktualisierbar |

---

# 12. Webclient als Lite-Version

Falls vollständige Parität nicht sinnvoll ist, soll der Webclient als **Lite-Version** definiert werden können.

Eine Lite-Version bedeutet:

* kleinere Weltsichtweite
* reduzierte Grafikqualität
* weniger Partikeleffekte
* weniger Audio
* reduzierte Animationen
* begrenzte Chunk-Anzahl
* keine besonders schweren Spezialeffekte
* eventuell keine erweiterten Bauvorschauen
* eventuell kein komplexer Raumkampf

Wichtig:

Auch als Lite-Version muss der Webclient spielmechanisch kompatibel bleiben.

Ein Spieler im Webclient darf auf demselben Server spielen wie ein Spieler im Windows-Client, solange die Funktionen verfügbar sind.

---

# 13. Anforderungen an Versionierung

Der Webclient muss zur Serverversion passen.

Der Server soll prüfen:

* Client-Version
* Protokollversion
* Spielinhalt-Version
* Asset-Version
* unterstützte Features

Wenn Versionen nicht passen, soll der Spieler eine klare Meldung erhalten.

Beispiel:

```text
Dieser Webclient ist nicht kompatibel mit dem Server.
Bitte lade die Seite neu oder nutze den aktuellen Client.
```

Bei selbst gehostetem Webclient sollte dieses Problem reduziert sein, weil der Server den passenden Webclient direkt ausliefert.

---

# 14. Sicherheit und Zugriff

## 14.1 Verbindungssicherheit

Bei lokalem LAN-Betrieb kann HTTP zunächst ausreichen.

Für Internet-Server sollte HTTPS/WSS unterstützt oder zumindest vorbereitet werden.

Anforderungen:

* Webclient darf keine Adminrechte erhalten.
* Admin-Oberfläche und Spielclient müssen getrennt sein.
* Serverpasswort muss auch im Webclient funktionieren.
* Whitelist muss auch für Webclient-Spieler gelten.
* WebSocket-Zugriffe müssen serverseitig validiert werden.
* CORS-Regeln müssen bedacht werden, falls Client und Server getrennt gehostet werden.

---

## 14.2 Admin-Weboberfläche vs. Webclient

Die Admin-Oberfläche und der Browser-Spielclient sind unterschiedliche Dinge.

Beispiele:

```text
/admin
```

für Serververwaltung

```text
/play
```

für das Spiel im Browser

Diese Bereiche müssen getrennte Rechte und Sicherheitsregeln haben.

---

# 15. Anforderungen an Hosting und Self-Hosting

Der Webclient soll zur Self-Hosting-Idee passen.

## 15.1 Einfacher Betrieb

Ein Serverbetreiber soll möglichst nur den Spacecraft-Server starten müssen.

Danach bietet der Server:

* Spielserver
* Admin-Weboberfläche
* Webclient
* Downloadseite für Clients

---

## 15.2 Raspberry Pi 5

Auch beim Pi-Hosting soll der Server den Webclient ausliefern können.

Dabei ist zu beachten:

* Der Pi dient nur als Webserver für die Clientdateien.
* Die 3D-Grafik läuft im Browser des Spielers, nicht auf dem Pi.
* Der Pi muss keine Unity-Grafik rendern.
* Der Pi muss aber zusätzliche WebSocket-Verbindungen verarbeiten können.
* Webclient-Auslieferung erhöht Netzwerktraffic, aber nicht stark die CPU-Last nach dem Laden.

---

# 16. Anforderungen an Entwicklung und Projektstruktur

Die Projektstruktur soll Browser-Unterstützung nicht nachträglich blockieren.

## 16.1 Gemeinsame Client-Codebasis

Idealerweise nutzen Windows-Client und Webclient weitgehend dieselbe Unity-Codebasis.

Dabei muss früh geprüft werden:

* Welche Unity-APIs funktionieren im Web-Build?
* Welche Netzwerkklassen funktionieren nicht?
* Welche Dateizugriffe funktionieren nicht?
* Welche Threading-Modelle funktionieren nicht?
* Welche Plugins funktionieren nicht im Browser?
* Welche Shader funktionieren nicht oder schlecht?
* Welche Audiofunktionen sind eingeschränkt?

---

## 16.2 Plattformabstraktion

Plattformabhängige Funktionen sollen abstrahiert werden.

Beispiele:

* Netzwerktransport
* Dateispeicherung
* Asset-Loading
* Logging
* Eingabe
* Vollbildmodus
* Zwischenablage
* Audio-Aktivierung
* Browser-JavaScript-Integration

Ziel:

> Nicht überall im Spielcode sollen Browser-Sonderfälle verteilt sein.

---

## 16.3 Transport-Abstraktion

Besonders wichtig ist eine saubere Transport-Abstraktion.

Der Spielcode soll nicht direkt davon abhängen, ob die Verbindung über UDP oder WebSocket läuft.

Beispielhafte Logik:

```text
GameClient
    nutzt
INetworkTransport

UDPTransport für Windows
WebSocketTransport für Browser
```

Dadurch kann der native Client weiter optimiert werden, während der Webclient browserkompatibel bleibt.

---

# 17. MVP-Anforderung für den Webclient

Der Webclient ist nicht zwingend Teil des ersten Gesamt-MVPs von Spacecraft.

Er soll aber früh technisch geprüft werden.

## 17.1 Webclient-Prototyp

Ein erster Webclient-Prototyp soll prüfen:

* Unity-Web-Build startet im Browser.
* Verbindung zum Server per WebSocket funktioniert.
* Spieler kann eine einfache Testwelt betreten.
* Chunks können geladen werden.
* Blockabbau wird serverseitig validiert.
* Blockplatzierung wird serverseitig validiert.
* Hotbar und Inventar funktionieren rudimentär.
* Performance ist messbar.
* Speicherverbrauch ist messbar.
* Ladezeit ist messbar.

---

## 17.2 Akzeptanzkriterien für Prototyp

Der Prototyp gilt als erfolgreich, wenn:

* der Webclient in mindestens Chrome und Edge auf Desktop startet
* Verbindung zu lokalem oder LAN-Server möglich ist
* eine kleine Klötzchenwelt spielbar ist
* serverseitiger Blockabbau funktioniert
* serverseitige Blockplatzierung funktioniert
* keine kritischen Speicher- oder Absturzprobleme auftreten
* klar dokumentiert ist, welche Einschränkungen bestehen

---

# 18. Webclient nicht als Serverersatz

Der Browser-Client ist nur ein Client.

Er darf nicht den Server ersetzen.

Nicht erlaubt:

* Browser hostet die autoritative Welt
* Browser verwaltet echte Savegames als Hauptquelle
* Browser entscheidet Belohnungen
* Browser entscheidet Inventar
* Browser simuliert die Welt allein für andere Spieler
* Browser ersetzt den Dedicated Server

Der Server bleibt immer die Quelle der Wahrheit.

---

# 19. Mögliche Betriebsarten mit Webclient

## 19.1 Spieler nutzt Windows-Client

```text
Spieler lädt Windows-Client herunter.
Spieler verbindet sich mit Server.
```

## 19.2 Spieler nutzt Browser-Client

```text
Spieler öffnet Server-Website.
Spieler klickt „Im Browser spielen“.
Webclient lädt.
Spieler verbindet sich direkt.
```

## 19.3 Gemischter Server

```text
Spieler A nutzt Windows-Client.
Spieler B nutzt Browser-Client.
Spieler C nutzt später vielleicht Linux-Client.
Alle spielen auf demselben Server.
```

Das Ziel ist, gemischte Clients auf demselben Server zu ermöglichen.

---

# 20. Risiken

## 20.1 Technische Risiken

* Webclient ist zu groß.
* Ladezeiten sind zu lang.
* Performance ist zu schwach.
* Browser-Netzwerk reicht nicht für gutes Spielgefühl.
* Unity-Web-Build hat Einschränkungen bei Plugins.
* Speicherverbrauch ist zu hoch.
* mobile Browser sind nicht sinnvoll nutzbar.
* Grafikqualität muss stark reduziert werden.
* WebSocket-Latenz ist höher als native Verbindung.
* Server muss zwei Netzwerkwege pflegen.

---

## 20.2 Produkt-Risiken

* Spieler erwarten dieselbe Qualität wie im nativen Client.
* Browser-Version wirkt zu schwach.
* Supportaufwand steigt.
* Unterschiedliche Browser verhalten sich unterschiedlich.
* Webclient erschwert Entwicklung, wenn zu früh volle Parität verlangt wird.

---

# 21. Empfehlung für die Umsetzung

Der Webclient soll als Ziel eingeplant, aber nicht sofort als gleichwertige Hauptplattform behandelt werden.

Empfohlener Weg:

## Phase 1: Architektur vorbereiten

* Client-server-basierte Architektur beibehalten
* Transport-Abstraktion einplanen
* keine native-only Annahmen im Clientcode
* Server-Webportal vorbereiten
* WebSocket-Gateway technisch vorsehen

## Phase 2: Machbarkeitsprototyp

* einfacher Unity-Web-Build
* einfache Testwelt
* WebSocket-Verbindung
* Blockabbau und Blockplatzierung
* Performance-Messung
* Dokumentation der Grenzen

## Phase 3: Webclient-Lite

* reduzierte Grafik
* begrenzte Chunks
* grundlegende Spielbarkeit
* Hotbar, Inventar, Crafting
* Serverbeitritt im Browser
* gemeinsame Welt mit Windows-Clients

## Phase 4: Erweiterung

* Missionscomputer
* Sternenkarte
* Raumschiffmodule
* Belohnungsdepot
* bessere Grafikoptionen
* optional PWA
* optional mobile Prüfung

---

# 22. Nicht-Ziele für die erste Webclient-Prüfung

Nicht erforderlich für den ersten Webclient-Prototyp:

* vollständige Grafikparität
* mobile Unterstützung
* Touch-Steuerung
* alle Missionstypen
* vollständiges Crafting
* komplette Raumschiff-Erweiterung
* Raumkampf
* große Planeten
* viele Spieler
* Offline-Modus
* Modding
* vollständige Adminintegration im Spielclient

---

# 23. Zusammenfassung

Spacecraft soll nach Möglichkeit zusätzlich zum nativen Windows-Client einen Browser-Client erhalten.

Der Browser-Client soll es Spielern ermöglichen, einem selbst gehosteten Server direkt über den Browser beizutreten. Der Server soll dafür idealerweise eine Web-Startseite anbieten, auf der Spieler entweder den nativen Client herunterladen oder das Spiel direkt im Browser starten können.

Die Browser-Version muss technisch geprüft werden, da Browser andere Grenzen haben als native Anwendungen. Besonders wichtig sind Netzwerk, Speicher, Ladezeit, Performance, Eingabe und Grafikqualität. Der Browser-Client wird voraussichtlich einen WebSocket- oder WebRTC-basierten Netzwerkzugang benötigen, während native Clients weiterhin einen optimierten nativen Realtime-Kanal nutzen können.

Die wichtigste Leitlinie lautet:

> Der Webclient ist ein zusätzlicher Zugang zum Spiel, nicht die Quelle der Spielwahrheit.
> Der Server bleibt autoritativ.
> Der Browser-Client soll möglichst viel vom Spiel ermöglichen, darf aber bei Bedarf als Lite-Version starten.
> Die Architektur muss früh so vorbereitet werden, dass ein Webclient später nicht durch native-only Entscheidungen blockiert wird.

[1]: https://docs.unity3d.com/Manual/webgl-browsercompatibility.html "Unity - Manual: Web browser compatibility"
