Hier ist ein weiteres gemischtes Anforderungsdokument mit den beiden neuen Punkten: **Respawn im Heiltank der Medbay** und **Admin-Editor zur serverseitigen Erweiterung von Missionen, Blaupausen und Crafting-Inhalten**.

# Spacecraft – Zusatzanforderungen: Heiltank-Respawn und Admin-Erweiterungseditor

## 1. Ziel dieses Dokuments

Dieses Dokument beschreibt zwei zusätzliche Anforderungen für **Spacecraft**:

1. Der Spieler soll nach Tod, schwerer Verletzung oder kritischem Systemausfall nicht einfach im Bett respawnen, sondern in einem **Heiltank in der Medbay seines Raumschiffs**.
2. Server-Admins sollen Werkzeuge erhalten, um das Spiel auf ihrem Server zu erweitern, insbesondere durch eigene Missionen, zufällig auftretende Missionsereignisse, Blaupausen und Crafting-Rezepte.

Diese Anforderungen ergänzen das bestehende Grundkonzept von Spacecraft als blockbasiertes 3D-Weltraum-Crafting-Spiel mit eigenem Raumschiff, prozeduralen Planeten, Crafting, Missionen, Self-Hosting und späterer Multiplayer-Fähigkeit.

---

# 2. Heiltank als Respawn-Punkt

## 2.1 Grundidee

Der zentrale Respawn-Punkt des Spielers soll nicht das Bett im Schlafbereich sein.

Stattdessen soll der Spieler nach Tod oder kritischem Zustand in einem **Heiltank** beziehungsweise **Regenerationstank** in der Medbay seines Raumschiffs wieder aufwachen.

Der Heiltank ist damit ein wichtiger Bestandteil der Medbay und des Startschiffs.

Die Grundfantasie:

> Der Spieler wird nach einem Unfall, einer Verletzung oder einem Missionsversagen automatisch in der Medbay seines Schiffs medizinisch regeneriert und erwacht in einem Heiltank.

Das passt besser zum Science-Fiction-Setting als ein klassisches Respawn-Bett.

---

## 2.2 Platzierung im Startschiff

Das Startraumschiff des Spielers muss von Anfang an eine Medbay besitzen.

In dieser Medbay befindet sich mindestens ein Heiltank.

Der Heiltank ist:

* sichtbares Objekt im Raum
* funktionaler Respawn-Punkt
* medizinisches Gerät
* später eventuell upgradefähig
* zentraler Teil des Überlebenssystems

Das Bett oder der Schlafbereich kann weiterhin existieren, aber mit anderer Funktion.

---

## 2.3 Unterschied zwischen Bett und Heiltank

## Bett / Schlafbereich

Der Schlafbereich dient für:

* Zeit überspringen
* Erholung
* Komfort
* eventuell Speichern
* eventuell Reduktion von Erschöpfung
* eventuell Rollenspiel- oder Crew-Komfort

Das Bett ist **nicht** der primäre Respawn-Ort.

## Heiltank / Medbay

Der Heiltank dient für:

* Respawn nach Tod
* Heilung nach schweren Verletzungen
* Regeneration nach Umweltgefahren
* Behandlung kritischer Zustände
* Wiederherstellung des Spielers nach Missionsversagen
* optional Entfernung von Statusproblemen

Der Heiltank ist der zentrale medizinische Rückkehrpunkt.

---

# 3. Respawn-Regeln

## 3.1 Standard-Respawn

Wenn der Spieler stirbt oder einen kritischen Zustand erreicht, wird er in den Heiltank seines eigenen Raumschiffs zurückgebracht.

Beispiele für Auslöser:

* Gesundheit fällt auf 0
* Sauerstoff ist vollständig verbraucht
* tödliche Strahlungsdosis
* extreme Hitze oder Kälte
* tödlicher Sturz
* Angriff durch Kreaturen oder Gegner
* Missionsunfall
* Schiffsexterne Katastrophe
* verlorener Kontakt zum Spielercharakter auf einer gefährlichen Welt

Nach dem Respawn:

* Spieler erwacht im Heiltank.
* Medbay-Animation oder kurzer Heilvorgang wird abgespielt.
* Spieler erhält Grundgesundheit zurück.
* kritische Zustände werden reduziert oder entfernt.
* eventuell gibt es eine Strafe oder Kosten.

---

## 3.2 Respawn als Erlebnis

Der Respawn soll nicht nur ein Ladebildschirm sein.

Empfohlenes Erlebnis:

1. Bildschirm wird dunkel oder zeigt Systemausfall.
2. Kurzer medizinischer Neustart.
3. Geräusche von Flüssigkeit, Pumpen, Scannern oder medizinischen Geräten.
4. Spieler sieht von innen oder außen den Heiltank.
5. Tank öffnet sich.
6. Spieler steigt aus oder übernimmt direkt wieder Kontrolle.
7. Medbay-System gibt kurze Meldung.

Beispielmeldung:

```text
Medbay-System aktiv.
Körperstabilisierung abgeschlossen.
Warnung: Ausrüstung teilweise beschädigt.
```

---

## 3.3 Mögliche Respawn-Kosten

Respawn sollte eine Konsequenz haben, aber nicht zu frustrierend sein.

Mögliche Konsequenzen:

* Verlust eines Teils der getragenen Rohstoffe
* Beschädigung von Werkzeugen
* Verbrauch medizinischer Vorräte
* Verbrauch von Schiffssauerstoff
* kurze Schwächung nach Respawn
* Rückholungskosten
* verlorener Missionsfortschritt bei bestimmten Missionen
* Items bleiben am Todesort als Bergungskapsel zurück

Empfehlung:

Für eine frühe Version sollte der Spieler nicht hart bestraft werden.

Guter Kompromiss:

* Spieler respawnt sicher im Heiltank.
* Ein Teil der mitgeführten Materialien bleibt in einer Bergungskapsel am Todesort.
* Werkzeuge werden nicht vollständig gelöscht, aber eventuell beschädigt.
* Medbay verbraucht medizinische Vorräte oder Energie.

---

## 3.4 Bergungskapsel

Optional kann beim Tod eine Bergungskapsel entstehen.

Diese enthält:

* Teil des Inventars
* seltene gesammelte Materialien
* eventuell beschädigte Ausrüstung
* Missionsgegenstände, falls erlaubt

Der Spieler kann später zur Todesstelle zurückkehren und die Kapsel bergen.

Das erzeugt einen motivierenden Rückkehr-Loop:

> Ich bin auf dem Lavaplaneten gescheitert, aber meine Ausrüstung liegt noch dort. Ich verbessere meinen Schutzanzug und hole sie zurück.

---

# 4. Heiltank-Upgrades

Der Heiltank kann später verbessert werden.

## 4.1 Mögliche Upgrades

### Heiltank I – Basisversion

Im Startschiff vorhanden.

Funktion:

* Standard-Respawn
* einfache Heilung
* entfernt einfache Verletzungen

---

### Heiltank II – Erweiterte Regeneration

Funktion:

* schnellere Heilung
* geringere Respawn-Strafen
* entfernt Vergiftung
* entfernt leichte Strahlungsschäden

---

### Heiltank III – Biomedizinische Rekonstruktion

Funktion:

* starke Heilung
* reduziert Werkzeugschaden nach Tod
* bessere Behandlung von Umweltfolgen
* erhöht maximale Gesundheit optional

---

### Notfall-Rückholsystem

Funktion:

* holt Spieler bei kritischem Zustand automatisch zurück
* verhindert Tod einmalig oder mit langer Abklingzeit
* verbraucht seltene medizinische Ressourcen

---

## 4.2 Abhängigkeit von Medbay und Schiffszustand

Der Respawn hängt vom Schiff ab.

Mögliche Regeln:

* Wenn das Schiff funktionsfähig ist, respawnt der Spieler im Heiltank.
* Wenn die Medbay beschädigt ist, dauert Respawn länger oder kostet mehr.
* Wenn keine Energie vorhanden ist, kann der Heiltank nur eingeschränkt arbeiten.
* Wenn medizinische Vorräte fehlen, entstehen höhere Nachteile.
* Wenn das Schiff weit entfernt ist, wird der Spieler trotzdem zurückgebracht, aber mit höheren Kosten oder Zeitverzug.

Für eine erste Version sollte das System einfach bleiben:

> Spieler respawnt immer sicher im Heiltank seines Schiffs.

Komplexere Abhängigkeiten können später ergänzt werden.

---

# 5. Admin-Erweiterungseditor

## 5.1 Grundidee

Server-Admins sollen das Spiel auf ihrem eigenen Server erweitern können.

Dafür soll es einen **Admin-Erweiterungseditor** geben.

Dieser Editor soll nicht nur Servereinstellungen verwalten, sondern echte Spielinhalte anlegen oder konfigurieren können.

Admins sollen zum Beispiel:

* eigene Missionen erstellen
* Missionen als zufällige Ereignisse auf Welten platzieren
* Missionsvorlagen anlegen
* Blaupausen erstellen
* Crafting-Rezepte definieren
* neue servereigene Belohnungen konfigurieren
* Missionsketten vorbereiten
* Inhalte für bestimmte Planeten oder Biome festlegen
* servereigene Ereignisse planen

Das Ziel ist:

> Ein selbst gehosteter Spacecraft-Server soll nicht nur eine Kopie des Grundspiels sein, sondern vom Admin erweitert und personalisiert werden können.

---

## 5.2 Unterschied zwischen Spieler-Missionseditor und Admin-Editor

Es gibt zwei unterschiedliche Ebenen.

## Spieler-Missionseditor

Für normale Spieler.

Erlaubt:

* Aufträge für andere Spieler erstellen
* Belohnung in Missionsdepot hinterlegen
* einfache strukturierte Missionsziele wählen
* Mission veröffentlichen
* eigene Missionen verwalten

Einschränkungen:

* keine neuen Items
* keine neuen Rezepte
* keine neuen Blaupausen
* keine globalen Weltregeln
* keine zufälligen Weltmissionen
* keine systemischen Belohnungstabellen

---

## Admin-Erweiterungseditor

Für Server-Admins.

Erlaubt zusätzlich:

* serverweite Missionen erstellen
* zufällige Missionsereignisse definieren
* Missionen an Planeten/Biome koppeln
* eigene Blaupausen anlegen
* Crafting-Rezepte erstellen oder anpassen
* Belohnungen konfigurieren
* servereigene Inhalte aktivieren/deaktivieren
* Spielbalance für den Server anpassen
* Inhalte freigeben oder sperren
* Missionen moderieren
* KI-Missionen später prüfen und freigeben

---

# 6. Admin-Missionseditor

## 6.1 Zweck

Der Admin-Missionseditor dient dazu, Missionen auf Serverebene zu erstellen.

Diese Missionen können:

* sofort verfügbar sein
* nur unter Bedingungen erscheinen
* zufällig auf Welten auftauchen
* an bestimmte Planeten gebunden sein
* an bestimmte Ressourcen gebunden sein
* an den Fortschritt der Spieler gebunden sein
* als wiederholbare Servermissionen funktionieren
* Teil einer Missionskette sein

---

## 6.2 Missionen mit zufälligem Auftreten

Admins sollen Missionen definieren können, die zufällig oder halbzufällig auf Welten erscheinen.

Beispiele:

### Beispiel 1: Notruf auf Eisplanet

Bedingung:

* Planetentyp: Eisplanet
* Spieler landet zum ersten Mal
* Wahrscheinlichkeit: 15 %

Mission:

* Finde eine beschädigte Sonde.
* Scanne die Sonde.
* Bringe Daten zum Schiff zurück.

Belohnung:

* Missionspunkte
* Lithium
* Scanner-Erfahrung
* mögliche Blaupausenfragmente

---

### Beispiel 2: Titanauftrag auf Lavaplanet

Bedingung:

* Planetentyp: Lavaplanet
* Ressource Titan vorhanden
* Spieler besitzt Thermoschutz
* Wahrscheinlichkeit: 25 %

Mission:

* Baue 50 Titanerz ab.
* Kehre zur Raffinerie zurück.

Belohnung:

* Energiezellen
* Plasmabohrer-Fortschritt
* Ruf bei einer Station

---

### Beispiel 3: Verlassene Kiste im Asteroidenfeld

Bedingung:

* Ortstyp: Asteroid
* Tiefe oder Entfernung vom Landepunkt erreicht
* Wahrscheinlichkeit: 10 %

Mission:

* Finde eine alte Frachtkapsel.
* Öffne oder berge sie.
* Liefere Inhalt an Missionscomputer.

Belohnung:

* zufällige Bauteile
* Datenfragment
* Admin-definierte Belohnungstabelle

---

## 6.3 Missionsbedingungen

Admin-Missionen sollen mit Bedingungen erstellt werden können.

Mögliche Bedingungen:

* Planetentyp
* Biometyp
* Sternensystem
* bestimmter Planet
* bestimmte Ressource vorhanden
* Spieler besitzt bestimmtes Werkzeug
* Spieler besitzt bestimmtes Schiffmodul
* Spieler hat bestimmte Blaupause freigeschaltet
* Spieler hat bestimmte Mission abgeschlossen
* Serverfortschritt erreicht
* Zufallschance
* Spieleranzahl online
* Zeit seit Serverstart
* bestimmtes Ereignis ausgelöst
* bestimmter Außenposten existiert
* bestimmter Gefahrenwert auf Planet

---

## 6.4 Missionsziele

Admin-Missionen sollen dieselben serverseitig validierbaren Zieltypen nutzen wie normale Missionen.

Beispiele:

* Sammle Item
* Baue Block ab
* Liefere Item
* Reise zu Ort
* Scanne Objekt
* Errichte Struktur
* Repariere Objekt
* Erkunde Bereich
* Aktiviere Gerät
* Überlebe bestimmte Zeit
* Besiege Gegner, falls Kampfsystem vorhanden
* Baue Außenpostenmodul

---

# 7. Admin-Blueprint-Editor

## 7.1 Grundidee

Admins sollen eigene Blaupausen anlegen können.

Blaupausen sind Freischaltungen, die Spielern neue Crafting- oder Bauoptionen geben.

Beispiele:

* neuer Spezialbohrer
* alternatives Frachtraummodul
* verbesserte Sauerstoffstation
* dekorative Schiffswände
* Server-spezifisches Außenpostenmodul
* besondere Medbay-Erweiterung
* spezielle Energiezelle
* Admin-eigene Questbelohnung

---

## 7.2 Funktionen des Blueprint-Editors

Admins sollen definieren können:

* Name der Blaupause
* Beschreibung
* Kategorie
* Icon oder Platzhalterbild
* Freischaltbedingungen
* benötigte Ressourcen zum Freischalten
* zugehöriges Crafting-Rezept
* erlaubte Spielergruppen
* benötigte Werkstatt oder Station
* Tech-Tree-Position
* ob Blaupause handelbar ist
* ob Blaupause nur als Missionsbelohnung verfügbar ist
* ob Blaupause global oder spielerbezogen freigeschaltet wird

---

## 7.3 Beispiel: Admin-definierte Blaupause

Name:

```text
Kompakter Eisbohrer
```

Beschreibung:

```text
Ein Spezialbohrer für gefrorene Oberflächen. Auf Eisplaneten schneller, auf Felsplaneten schwächer.
```

Freischaltung:

* Mission „Blaue Adern im Eis“ abgeschlossen
* 3 Datenfragmente
* 20 Lithium

Crafting-Rezept:

* 1 verbesserter Titanbohrer
* 10 Lithiumzellen
* 6 Kälteregulatoren
* 4 Titanplatten
* 2 Energielinsen

Effekt:

* schnellerer Abbau von Eis, gefrorenem Gestein und Kryokristallen

---

# 8. Admin-Crafting-Rezepteditor

## 8.1 Grundidee

Admins sollen Crafting-Rezepte anlegen oder anpassen können.

Dabei muss das Spiel verhindern, dass ungültige oder serverzerstörende Rezepte entstehen.

Der Editor muss daher mit klaren Regeln und Validierung arbeiten.

---

## 8.2 Funktionen

Admins sollen einstellen können:

* Rezeptname
* Ergebnis-Item
* Ergebnis-Menge
* benötigte Zutaten
* benötigte Mengen
* benötigte Station
* Crafting-Dauer
* Energiebedarf
* benötigte Blaupause
* benötigtes Tech-Level
* ob Rezept wiederholbar ist
* ob Rezept nur durch Mission freigeschaltet wird
* ob Rezept öffentlich oder versteckt ist

---

## 8.3 Beispiel-Rezept

Rezept:

```text
Kälteregulator
```

Benötigt:

* 4 Lithium
* 2 Kupferkabel
* 1 Kristalllinse
* 1 Aluminiumgehäuse

Station:

* Werkstatt II

Freischaltung:

* Blaupause „Thermotechnik I“

Ergebnis:

* 1 Kälteregulator

Verwendung:

* Eisbohrer
* Thermoanzug
* Kryo-Lager
* Medbay-Upgrades

---

# 9. Admin-Belohnungstabellen

Admins sollen eigene Belohnungstabellen definieren können.

## 9.1 Zweck

Belohnungstabellen erlauben kontrollierte Zufallsbelohnungen.

Beispiele:

* einfache Materialbelohnung
* seltene technische Bauteile
* Missionspunkte
* Bauplanfragmente
* medizinische Vorräte
* Außenposten-Komponenten
* Admin-definierte Spezialitems

---

## 9.2 Anforderungen

Eine Belohnungstabelle soll enthalten:

* Name
* Beschreibung
* mögliche Belohnungen
* Gewichtung/Wahrscheinlichkeit
* Mindest- und Höchstmengen
* Seltenheitsstufe
* erlaubte Missionstypen
* maximale Auszahlungen pro Spieler
* maximale Auszahlungen pro Tag
* optionale Abhängigkeit von Schwierigkeit

---

# 10. Sicherheit und Validierung

Der Admin-Editor ist mächtig und kann das Spiel stark verändern. Deshalb braucht er Validierung.

## 10.1 Validierung von Missionen

Das System muss prüfen:

* Sind Zielorte gültig?
* Existieren referenzierte Items?
* Existieren referenzierte Blocktypen?
* Sind Mengen sinnvoll?
* Ist die Mission lösbar?
* Sind Belohnungen erlaubt?
* Ist die Missionsbedingung technisch prüfbar?
* Gibt es keine Endlosschleifen?
* Gibt es keine kaputten Abhängigkeiten?

---

## 10.2 Validierung von Blaupausen

Das System muss prüfen:

* Gibt es das Ergebnisobjekt?
* Gibt es ein zugehöriges Rezept?
* Sind Freischaltbedingungen erfüllbar?
* Ist die Blaupause nicht doppelt?
* Erzeugt sie keine ungültige Tech-Tree-Abhängigkeit?
* Ist sie einer Kategorie zugeordnet?
* Ist sie nicht versehentlich ohne Zugriffsmöglichkeit versteckt?

---

## 10.3 Validierung von Rezepten

Das System muss prüfen:

* Existieren alle Zutaten?
* Existiert das Ergebnisitem?
* Ist die Ergebnis-Menge sinnvoll?
* Ist keine direkte Ressourcenvervielfältigung möglich, sofern nicht gewollt?
* Ist die benötigte Station vorhanden?
* Ist das Rezept erreichbar?
* Verursacht es keine unendlichen Crafting-Schleifen?
* Verursacht es keine offensichtlichen Exploits?

---

# 11. Rechte und Rollen

Der Admin-Editor darf nur berechtigten Nutzern zugänglich sein.

## 11.1 Rollen

Mögliche Rollen:

### Server-Admin

Darf alles:

* Missionen erstellen
* Missionen löschen
* Blaupausen erstellen
* Rezepte erstellen
* Belohnungstabellen bearbeiten
* Inhalte aktivieren/deaktivieren
* Spielerrechte verwalten
* KI-Inhalte später freigeben

### Moderator

Darf:

* Spieler-Missionen prüfen
* Missionen deaktivieren
* problematische Inhalte entfernen
* Missionslogs einsehen

### Content-Designer

Darf:

* Missionen erstellen
* Missionsketten erstellen
* Blaupausen vorschlagen
* Rezepte vorschlagen

### Spieler

Darf:

* normale Spieleraufträge erstellen
* eigene Missionen verwalten
* Belohnungen hinterlegen

---

## 11.2 Freigabeprozess

Für mächtige Änderungen kann ein Entwurfsmodus sinnvoll sein.

Ablauf:

1. Inhalt wird als Entwurf angelegt.
2. Server validiert Inhalt.
3. Admin prüft Vorschau.
4. Inhalt wird aktiviert.
5. Inhalt erscheint im Spiel.

---

# 12. Persistenz und Export

Admin-erstellte Inhalte müssen gespeichert und übertragbar sein.

## 12.1 Speicherung

Zu speichern sind:

* Admin-Missionen
* Missionsvorlagen
* Missionsketten
* Blaupausen
* Crafting-Rezepte
* Belohnungstabellen
* Aktivierungsbedingungen
* Versionen
* Ersteller
* Änderungsdatum
* Status
* Abhängigkeiten

---

## 12.2 Export und Import

Admins sollen Inhalte exportieren und importieren können.

Ziel:

* Server können eigene Content-Pakete teilen.
* Inhalte können gesichert werden.
* Admins können Test- und Live-Server trennen.
* Inhalte können versioniert werden.

Beispiel:

```text
spacecraft_content_pack_iceworld.json
```

oder später:

```text
spacecraft_content_pack_iceworld.zip
```

---

# 13. Admin-UI

## 13.1 Zugriff

Der Admin-Erweiterungseditor kann Teil der bestehenden Admin-Weboberfläche sein.

Mögliche Bereiche:

* Serverstatus
* Spieler
* Missionen
* Spieleraufträge
* Admin-Missionen
* Missionsvorlagen
* Blaupausen
* Rezepte
* Belohnungstabellen
* Content-Packs
* Logs
* Validierungsfehler

---

## 13.2 Editor-Funktionen

Die Admin-UI soll bieten:

* Formularbasierte Erstellung
* Dropdowns für vorhandene Items und Blöcke
* Auswahl von Planetentypen
* Auswahl von Missionstypen
* Mengenfelder
* Bedingungseditor
* Belohnungstabellen-Editor
* Rezepteditor
* Blueprint-Editor
* Vorschau
* Validierungsprüfung
* Aktivieren/Deaktivieren
* Duplizieren bestehender Inhalte
* Import/Export

---

# 14. Verbindung zur KI-Erweiterung

Der Admin-Editor soll später gut mit dem optionalen Python-KI-Backend zusammenspielen.

Mögliche KI-Funktionen später:

* KI schlägt Missionen vor
* KI erstellt Missionsdialoge
* KI erzeugt Missionsketten
* KI erzeugt Beschreibungen für Blaupausen
* KI erzeugt Ideen für Belohnungstabellen
* KI erstellt Entwürfe, die der Admin freigeben kann

Wichtig:

> KI-generierte Inhalte sollen im Admin-Editor sichtbar, prüfbar, anpassbar und freigebbar sein.

Die KI darf Inhalte vorschlagen oder erzeugen, aber der Hauptserver validiert und der Admin kann je nach Einstellung die Freigabe kontrollieren.

---

# 15. MVP-Anforderungen für diese Erweiterung

## 15.1 Heiltank-MVP

Für eine erste Version sollte enthalten sein:

* Heiltank als sichtbares Objekt in der Medbay
* Respawn des Spielers im Heiltank
* einfache Heilsequenz oder Meldung
* Gesundheit wird wiederhergestellt
* Spieler startet nach Respawn im Schiff
* Bett bleibt vom Respawn getrennt

Optional später:

* Bergungskapsel
* Medbay-Verbrauch
* Heiltank-Upgrades
* unterschiedliche Respawn-Strafen

---

## 15.2 Admin-Editor-MVP

Für eine erste Admin-Editor-Version sollte enthalten sein:

* Admin kann Missionen anlegen
* Admin kann Missionen aktivieren/deaktivieren
* Admin kann Missionen bestimmten Planetentypen zuordnen
* Admin kann einfache Zufallswahrscheinlichkeiten setzen
* Admin kann einfache Belohnungen definieren
* Admin kann einfache Blaupausen anlegen
* Admin kann einfache Crafting-Rezepte anlegen
* Server validiert Inhalte
* Inhalte werden persistent gespeichert
* Admin kann Inhalte exportieren/importieren

---

# 16. Nicht-Ziele für die erste Version

Nicht zwingend erforderlich für die erste Umsetzung:

* komplexer visueller Node-Editor
* vollständiges Modding-System
* beliebige neue 3D-Modelle
* neue Blockgrafiken durch Admins
* komplexe Skriptsprache
* KI-generierte Inhalte
* automatische Balance-Prüfung auf hohem Niveau
* serverübergreifender Content-Marktplatz
* öffentliche Mod-Plattform
* vollständige Fraktionssimulation

---

# 17. Zusammenfassung

Spacecraft soll den Spieler nach Tod oder kritischem Zustand nicht im Bett respawnen lassen, sondern in einem **Heiltank in der Medbay des eigenen Raumschiffs**. Der Heiltank ist Teil des Startschiffs und dient als zentraler medizinischer Respawn-Punkt. Das Bett bleibt für Schlaf, Erholung oder Zeitübersprung erhalten, ist aber nicht der Haupt-Respawn-Ort.

Zusätzlich soll es für Server-Admins einen mächtigen, aber kontrollierten **Admin-Erweiterungseditor** geben. Mit diesem können Admins eigene Missionen, zufällig auftretende Weltereignisse, Blaupausen, Crafting-Rezepte und Belohnungstabellen erstellen. Dadurch können selbst gehostete Server eigene Inhalte und Spielvarianten anbieten.

Die wichtigste Leitlinie lautet:

> Der Heiltank ist der zentrale Respawn-Ort des Spielers.
> Admins sollen Serverinhalte erweitern können.
> Alle selbst erstellten Inhalte müssen serverseitig validierbar, persistent speicherbar und kontrolliert aktivierbar sein.
