Hier ist ein vollständiger Konzeptentwurf, so formuliert, dass ein Entwickler den Sinn, die Spielidee und die grundlegenden Anforderungen versteht — bewusst ohne technische Bibliotheken, Programmiersprachen oder Code-Konzepte.

# Spacecraft – Konzeptdokument für ein 3D-Klötzchen-Weltraumspiel

## 1. Grundidee des Spiels

**Spacecraft** ist ein 3D-Spiel für Windows in einer bewusst einfachen, kantigen **Klötzchen-Optik**. Die visuelle Grundstimmung erinnert an blockbasierte Spiele wie Minecraft, aber das zentrale Thema ist nicht nur das Abbauen und Bauen auf einer Welt, sondern das Leben, Erweitern und Nutzen eines eigenen Raumschiffs.

Der Spieler besitzt ein eigenes Raumschiff, das am Anfang sehr klein und funktional ist. Dieses Schiff ist die persönliche Basis, das Zuhause, der Lagerort, die Werkstatt, die Transportmöglichkeit und der wichtigste Fortschrittsgegenstand des Spiels. Der Spieler fliegt mit diesem Schiff zwischen Sternensystemen, landet auf Planeten, Monden, Asteroiden oder Raumstationen, steigt aus, sammelt Rohstoffe, baut Blöcke ab, errichtet bei Bedarf Außenposten und nutzt die gewonnenen Materialien, um neue Ausrüstung, Werkzeuge, Schiffsmodule und Erweiterungen herzustellen.

Das Spiel soll sich anfühlen wie:

**„Minecraft trifft Raumschiff-Ausbau, Planeten-Erkundung und Survival-Crafting im Weltraum.“**

Der Spieler soll immer das Gefühl haben:

> „Ich starte mit einem kleinen, einfachen Schiff. Durch Erkundung, Bergbau, Crafting und Forschung baue ich mir nach und nach mein eigenes, größeres, besseres Raumschiff und erreiche dadurch immer gefährlichere und spannendere Welten.“

---

## 2. Kernfantasie des Spiels

Die zentrale Spielerfantasie ist:

**Ich bin ein Weltraum-Pionier mit meinem eigenen modularen Raumschiff. Ich entdecke fremde Klötzchen-Welten, sammle seltene Materialien, verbessere mein Schiff und dringe immer tiefer ins All vor.**

Das Raumschiff ist nicht nur ein Menü oder ein Ladebildschirm, sondern ein begehbarer, sichtbarer und erweiterbarer Ort. Es soll ein echter persönlicher Mittelpunkt sein.

Der Spieler soll sich mit seinem Schiff identifizieren können:

* Das Schiff ist am Anfang klein.
* Es hat klare Räume mit Funktionen.
* Es kann erweitert werden.
* Neue Räume schalten neue Möglichkeiten frei.
* Mehr Lager, bessere Werkstätten, stärkere Antriebe und bessere Lebenserhaltung ermöglichen neue Reisen.
* Das Schiff wächst sichtbar mit dem Fortschritt des Spielers.

---

## 3. Hauptsäulen des Spiels

### 3.1 Das eigene Raumschiff als Zuhause

Das Raumschiff ist die wichtigste Basis des Spielers. Es enthält Räume mit klarer Funktion:

* Cockpit
* Schlafbereich / Crewquartier
* Medbay
* Werkstatt / Craftingbereich
* Frachtraum
* Energieversorgung
* Lebenserhaltung
* später zusätzliche Module wie Labor, Raffinerie, Gewächshaus, Schildgenerator, Drohnenbucht oder Hangar

Das Schiff soll aus rechteckigen, blockartigen Feldern bestehen. Der Spieler kann es später erweitern, indem er neue Räume freischaltet und baut.

---

### 3.2 Klötzchenwelten zum Landen, Abbauen und Bauen

Jeder Planet, Mond oder Asteroid besteht aus einer prozedural generierten Klötzchenwelt. Der Spieler kann dort landen, aussteigen und sich frei bewegen.

Auf diesen Welten kann der Spieler:

* Blöcke abbauen
* Rohstoffe sammeln
* Höhlen erkunden
* seltene Materialien suchen
* kleine Basen oder Gebäude errichten
* Markierungen setzen
* Außenposten bauen
* Ressourcen automatisch ins Schiff übertragen lassen
* später vielleicht Drohnen oder Maschinen einsetzen

Die Welten sollen nicht nur Rohstoffflächen sein, sondern unterschiedliche Gefahren, Atmosphären, Farben, Biome und Besonderheiten haben.

---

### 3.3 Crafting und Fortschritt

Der Spieler stellt neue Gegenstände, Werkzeuge, Ausrüstung und Schiffsmodule her.

Es gibt:

* einfache Rezepte für Basisgegenstände
* fortgeschrittene Rezepte für bessere Werkzeuge
* Schiffserweiterungen
* Tech-Tree-Freischaltungen
* Blaupausen
* seltene Materialien für besondere Module

Der Fortschritt soll zweistufig funktionieren:

1. **Blaupause freischalten**
   Der Spieler benötigt Forschungsdaten, Ressourcen oder bestimmte Funde, um den Plan für ein neues Objekt freizuschalten.

2. **Objekt bauen oder Schiff erweitern**
   Danach benötigt der Spieler weitere Rohstoffe, um das Objekt tatsächlich herzustellen oder das Raumschiff zu erweitern.

Dadurch entsteht ein sinnvoller Fortschrittsbogen: Erst entdecken, dann erforschen, dann bauen.

---

### 3.4 Erkunden über Sternenkarten

Im Cockpit hat der Spieler Zugriff auf eine Sternenkarte.

Die Sternenkarte zeigt:

* Sternensysteme
* Planeten
* Monde
* Asteroidenfelder
* Raumstationen
* unbekannte Signale
* Gefahrenzonen
* bereits entdeckte Landeorte
* Rohstoffhinweise nach Scans

Am Anfang sind nur wenige Orte sichtbar. Durch bessere Scanner, Navigationsmodule und Antriebe werden neue Gebiete erreichbar.

---

## 4. Spielstart

Der Spieler beginnt mit einem kleinen Basisschiff.

Dieses Schiff soll nicht luxuriös sein, aber alle Grundfunktionen besitzen.

### 4.1 Startschiff: Mindest-Räume

Das Startschiff enthält:

#### Cockpit

Funktion:

* Zugriff auf Sternenkarte
* Auswahl von Reisezielen
* Starten und Landen
* Scannen von Planeten
* Anzeigen von Schiffszustand
* Anzeigen von Sauerstoff, Energie, Frachtraum und Missionshinweisen

#### Schlafbereich

Funktion:

* Spieler kann schlafen
* Zeit kann übersprungen werden
* Gesundheit oder Erschöpfung kann sich regenerieren
* optional: Speicherpunkt / Rückkehrpunkt

#### Medbay

Funktion:

* Heilung
* Behandlung von Strahlung, Vergiftung, Kälte, Hitze oder Sauerstoffmangel
* Herstellung einfacher Medpacks
* später Erweiterung zu besserer medizinischer Versorgung

#### Werkstatt / Craftingbereich

Funktion:

* Herstellung einfacher Werkzeuge
* Herstellung einfacher Bauteile
* Verarbeitung von Rohstoffen zu Komponenten
* Reparatur von Ausrüstung
* später Erweiterung zu Labor, Raffinerie oder Fabrikmodul

#### Frachtraum

Funktion:

* Lagerung gesammelter Rohstoffe
* automatischer Transfer abgebauter Materialien vom Planeten ins Schiff
* Sortierung nach Materialklassen
* später erweiterbar für größere Expeditionen

---

## 5. Grundlegender Gameplay-Loop

Der wiederkehrende Spielablauf soll klar und motivierend sein.

### 5.1 Standard-Spielablauf

1. Der Spieler befindet sich im Raumschiff.
2. Er öffnet im Cockpit die Sternenkarte.
3. Er wählt ein Sternensystem, einen Planeten, Mond, Asteroiden oder eine Station.
4. Er scannt den Ort, um Informationen über Rohstoffe, Gefahren und Besonderheiten zu erhalten.
5. Er landet mit dem Schiff.
6. Er steigt aus.
7. Er erkundet die Klötzchenwelt.
8. Er baut Rohstoffe ab.
9. Rohstoffe werden automatisch oder manuell ins Schiff übertragen.
10. Der Spieler kehrt ins Schiff zurück.
11. Er verarbeitet Rohstoffe.
12. Er stellt Ausrüstung, Werkzeuge oder Bauteile her.
13. Er schaltet neue Blaupausen frei.
14. Er erweitert sein Schiff.
15. Mit dem verbesserten Schiff kann er gefährlichere oder weiter entfernte Orte erreichen.

Dieser Kreislauf soll das gesamte Spiel tragen.

---

## 6. Perspektive und Spielgefühl

Das Spiel sollte sich direkt und verständlich anfühlen.

### 6.1 Bewegung auf Planeten

Auf Planeten bewegt sich der Spieler wie in einem blockbasierten 3D-Erkundungsspiel.

Wichtige Anforderungen:

* frei begehbare 3D-Welten
* Springen oder Klettern über blockartige Strukturen
* Abbauen von Blöcken
* Platzieren von Blöcken
* Nutzung einer Hotbar
* einfache Werkzeuge und Gegenstände direkt auswählbar
* Gefühl von Entdeckung und Sammeln

### 6.2 Bewegung im Raumschiff

Das Raumschiff soll begehbar sein.

Der Spieler kann:

* durch Korridore laufen
* Räume betreten
* Stationen benutzen
* Lager öffnen
* im Cockpit die Karte nutzen
* an der Werkbank craften
* in der Medbay heilen
* im Schlafbereich schlafen
* Erweiterungen ansehen

Das Raumschiff soll sich nicht wie ein Menü anfühlen, sondern wie ein echter Ort.

---

## 7. Inventar und Hotbar

Der Spieler besitzt ein persönliches Inventar und eine sichtbare Hotbar am unteren Bildschirmrand.

### 7.1 Hotbar

Die Hotbar enthält Schnellzugriffe auf:

* Werkzeuge
* Blöcke
* Waffen oder Verteidigungswerkzeuge
* Verbrauchsgegenstände
* Sauerstoffkapseln
* Medpacks
* Scanner
* Bauteile

Beispiel-Hotbar:

1. Titanbohrer
2. Blockplatzierer
3. Scanner
4. Sauerstoffkapsel
5. Medpack
6. Metallblock
7. Glasblock
8. Energiekabel
9. Markierungsboje

### 7.2 Persönliches Inventar

Das persönliche Inventar ist begrenzt. Der Spieler kann kleine Mengen tragen, aber große Mengen sollen ins Schiff übertragen werden.

Das verhindert, dass das Schiff unwichtig wird.

### 7.3 Schiffsfrachtraum

Der Frachtraum ist das Hauptlager.

Er speichert:

* Erze
* Metalle
* Kristalle
* organische Materialien
* Bauteile
* Treibstoff
* seltene Artefakte
* Sauerstoffvorräte
* Werkzeuge
* Ersatzteile

### 7.4 Automatischer Materialtransfer

Ein zentrales Komfortsystem:

Wenn der Spieler auf einem Planeten Material abbaut, können Standard-Rohstoffe automatisch ins Schiff übertragen werden, solange:

* das Schiff in Reichweite ist oder
* ein Schiffstransfermodul aktiv ist oder
* der Spieler einen Transfer-Rucksack besitzt oder
* eine Landungszone als aktiv markiert ist

Das verhindert zu viel Inventar-Mikromanagement.

Vorschlag:

* Häufige Rohstoffe gehen automatisch in den Frachtraum.
* Seltene Funde gehen zuerst ins persönliche Inventar.
* Ist der Frachtraum voll, landet Material im persönlichen Inventar.
* Ist beides voll, kann nicht weiter gesammelt werden oder Material fällt als Block auf den Boden.

---

## 8. Ressourcenarten

Das Spiel braucht verschiedene Materialklassen, damit Crafting und Fortschritt interessant werden.

### 8.1 Basisrohstoffe

Diese Materialien findet man früh:

* Eisen
* Kupfer
* Silikat
* Kohlenstoff
* Eis
* Stein
* Basalt
* Aluminium
* einfache Kristalle
* organische Fasern

Verwendung:

* einfache Baublöcke
* Kabel
* Werkzeuge
* Basisreparaturen
* einfache Module
* Glas
* einfache Energiezellen

### 8.2 Fortgeschrittene Rohstoffe

Diese Materialien kommen auf gefährlicheren Planeten oder tieferen Schichten vor:

* Titan
* Nickel
* Kobalt
* Lithium
* Uranerz
* Neonkristalle
* Platin
* Iridium
* seltene Gase
* biomechanische Fasern
* magnetisches Erz

Verwendung:

* bessere Bohrer
* Schiffserweiterungen
* Energieversorgung
* Scanner
* Antriebe
* medizinische Systeme
* Atmosphärenfilter
* bessere Raumanzüge

### 8.3 Seltene Spezialmaterialien

Diese Materialien sind für späte Fortschritte gedacht:

* Plasmakern
* Quantenkristall
* Sternenstaub
* Antimaterie-Partikel
* Exo-Legierung
* Hyperraumspule
* lebendes Metall
* Dunkelglas
* Gravitationskern
* Alien-Datenfragment

Verwendung:

* Hyperraum-Upgrades
* große Schiffsmodule
* Hochleistungswerkzeuge
* Schutzschilde
* besondere Forschungsstationen
* Spezialausrüstung
* Endgame-Schiffsausbau

---

## 9. Werkzeuge und Ausrüstung

Es soll keine klassische Fantasy-Spitzhacke geben. Die Werkzeuge sollen zum Weltraum-Setting passen.

### 9.1 Abbauwerkzeuge

#### Basis-Titanbohrer

Startwerkzeug oder erstes wichtiges Werkzeug.

Funktion:

* baut einfache Blöcke ab
* geeignet für Stein, Erde, Eis, Eisen, Kupfer
* langsam bei härteren Materialien

#### Verbesserter Titanbohrer

Funktion:

* schnellerer Abbau
* kann Titan und härtere Gesteine abbauen
* verbraucht mehr Energie

#### Plasmabohrer

Funktion:

* baut harte und seltene Materialien ab
* kann Kristalle, Platin und Iridium abbauen
* erzeugt Hitze
* benötigt Energiezellen

#### Quantenbohrer

Spätes Werkzeug.

Funktion:

* baut sehr harte Spezialmaterialien ab
* kann seltene Erze fast verlustfrei abbauen
* kann besondere Alien-Strukturen analysieren
* benötigt seltene Komponenten

---

### 9.2 Bauwerkzeuge

#### Blockplatzierer

Funktion:

* setzt Blöcke aus dem Inventar
* erlaubt einfache Gebäude
* kann Außenposten errichten

#### Strukturprojektor

Fortgeschrittenes Bauwerkzeug.

Funktion:

* platziert größere Struktur-Vorlagen
* baut kleine Basisteile
* zeigt benötigte Ressourcen an
* kann unfertige Gebäude als Hologramm anzeigen

---

### 9.3 Scanner

#### Handscanner

Funktion:

* erkennt nahe Rohstoffe
* zeigt Materialnamen
* markiert interessante Blöcke
* findet Höhlen und kleine Signale

#### Tiefenscanner

Funktion:

* erkennt seltene Rohstoffe tiefer im Boden
* findet Höhlen, Ruinen und Energiequellen
* benötigt Energie

#### Bioscanner

Funktion:

* erkennt Pflanzen, organische Materialien und Lebewesen
* analysiert Gefahren
* findet medizinische Zutaten

---

### 9.4 Raumanzug

Der Raumanzug ist wichtig für das Überleben auf gefährlichen Planeten.

Basiswerte:

* Sauerstoff
* Strahlenschutz
* Hitzeschutz
* Kälteschutz
* Druckschutz
* Energie
* Inventarkapazität

Mögliche Upgrades:

* größerer Sauerstofftank
* bessere Isolierung
* Strahlungsschutz
* Jetpack
* Magnetstiefel
* automatischer Materialtransfer
* Gefahrensensor
* Notfall-Heilmodul

---

## 10. Sauerstoff und Umweltgefahren

Der Spieler soll auf Planeten nicht überall unbegrenzt sicher sein. Gleichzeitig soll das Spiel nicht zu frustrierend werden.

### 10.1 Sauerstoff

Auf Welten ohne atembare Atmosphäre verbraucht der Spieler Sauerstoff.

Sauerstoff kann kommen aus:

* Raumanzug-Tank
* Sauerstoffkapseln
* Schiffsvorrat
* Außenposten mit Lebenserhaltung
* später tragbare Sauerstoffgeneratoren

### 10.2 Umweltgefahren

Mögliche Gefahren:

* keine Atmosphäre
* giftige Atmosphäre
* extreme Hitze
* extreme Kälte
* Strahlung
* niedrige Schwerkraft
* hohe Schwerkraft
* Meteoritenschauer
* Säureseen
* Lavagebiete
* elektrische Stürme
* aggressive Kreaturen
* instabile Höhlen

Diese Gefahren sollen durch bessere Ausrüstung, bessere Schiffsmodule und Forschung kontrollierbar werden.

---

## 11. Planeten, Monde, Asteroiden und Stationen

### 11.1 Planetentypen

Jede Welt soll eine eigene Identität haben.

Beispiele:

#### Felsplanet

* viele Stein- und Metallerze
* Höhlen
* einfache Gefahren
* guter Startplanet

#### Eisplanet

* Eis, seltene Kristalle, gefrorene Gase
* Kältegefahr
* rutschige Flächen
* unterirdische Höhlen

#### Lavaplanet

* viel Energieerz, Titan, seltene Metalle
* extreme Hitze
* Lavaflüsse
* hoher Ausrüstungsbedarf

#### Dschungelplanet

* organische Materialien
* Pflanzenfasern
* medizinische Ressourcen
* fremde Lebensformen
* Giftgefahr

#### Wüstenplanet

* Silikat, Kristalle, alte Ruinen
* wenig Wasser
* Sandstürme
* gute Fundorte für Glas- und Solarbauteile

#### Ozeanplanet

* Inseln, Unterwasserhöhlen
* seltene biologische Materialien
* Druckgefahr
* später Unterwasser-Ausrüstung möglich

#### Kristallmond

* seltene Kristalle
* wenig Schwerkraft
* viele tiefe Spalten
* starke Scanner-Störungen

---

### 11.2 Asteroiden

Asteroiden sind kleinere Abbaugebiete.

Eigenschaften:

* wenig oder keine Atmosphäre
* meist viele Metalle
* kleine Klötzchenlandschaften
* gefährliche Abgründe
* begrenzte Rohstoffvorkommen
* manchmal alte Wracks oder Alien-Fragmente

Asteroiden sollen sich gut für kurze Mining-Missionen eignen.

---

### 11.3 Raumstationen

Raumstationen können unterschiedliche Rollen haben:

* verlassene Station
* Handelsstation
* Forschungsstation
* Piratenstation
* zerstörtes Wrack
* neutrale Reparaturstation

Dort kann der Spieler:

* handeln
* besondere Blaupausen finden
* Missionen erhalten
* Datenfragmente sammeln
* seltene Komponenten kaufen
* beschädigte Module bergen

---

## 12. Bauen auf Planeten

Der Spieler soll auf Planeten Blöcke setzen und einfache Gebäude bauen können.

### 12.1 Grundfunktionen

Der Spieler kann:

* Wände bauen
* Böden bauen
* Türen setzen
* Lampen setzen
* Lagerkisten platzieren
* kleine Werkbänke platzieren
* Außenposten errichten
* Markierungen setzen
* Schutzräume bauen

### 12.2 Außenposten

Außenposten sind kleine Basen auf Planeten.

Mögliche Außenposten-Module:

* Sauerstoffstation
* Lagerkiste
* kleiner Generator
* automatische Erzsammlung
* Scanner-Mast
* Landemarkierung
* Schutzkuppel
* medizinischer Notfallpunkt

Außenposten sollen nicht wichtiger als das Schiff sein, aber nützlich für wiederkehrende Erkundung.

---

## 13. Raumschiff-Erweiterung

Das Raumschiff ist modular aufgebaut und kann erweitert werden.

### 13.1 Grundprinzip

Der Spieler kann neue Module freischalten und bauen.

Beispiele:

* größerer Frachtraum
* verbesserte Werkstatt
* Labor
* Raffinerie
* zusätzlicher Schlafraum
* Hydroponik / Gewächshaus
* Sauerstoffgenerator
* Schildgenerator
* verbesserter Antrieb
* Hyperraummodul
* Drohnenbucht
* Kartenraum
* Maschinenraum
* Waffenkammer
* Dockingmodul

### 13.2 Erweiterungslogik

Jede Erweiterung braucht zwei Schritte:

#### Schritt 1: Blaupause freischalten

Beispiel:

**Frachtraum-Erweiterung I freischalten**

Benötigt:

* 20 Eisen
* 10 Aluminium
* 3 Kabel
* 1 Datenfragment

Danach kennt der Spieler die Blaupause.

#### Schritt 2: Modul bauen

Benötigt:

* 80 Eisenplatten
* 30 Aluminiumträger
* 12 Kabel
* 4 Energieknoten
* 1 Türmodul

Danach wird der Frachtraum sichtbar erweitert.

---

### 13.3 Sichtbare Veränderung

Wichtig: Erweiterungen sollen nicht nur Zahlenwerte erhöhen, sondern sichtbar im Schiff erscheinen.

Wenn der Spieler einen Frachtraum baut, soll das Schiff innen wirklich mehr Lagerraum haben. Wenn er ein Labor baut, soll es einen sichtbaren Laborraum geben. Wenn er ein neues Schlafmodul baut, soll dort ein neuer Raum mit Betten entstehen.

---

### 13.4 Einschränkungen beim Schiffsbau

Damit das System verständlich bleibt:

* Cockpit ist Pflicht und kann nicht entfernt werden.
* Energieversorgung ist Pflicht.
* Lebenserhaltung ist Pflicht.
* Räume müssen verbunden sein.
* Module brauchen Energie.
* Manche Module brauchen Sauerstoffversorgung.
* Frachtraum hat begrenzte Kapazität.
* Antrieb bestimmt Reichweite.
* Landemodul bestimmt, auf welchen Planeten gelandet werden kann.
* Hitzeschutz, Druckschutz und Strahlungsschutz bestimmen gefährliche Landezonen.

---

## 14. Schiffswerte

Das Schiff sollte klare Werte besitzen.

### 14.1 Wichtige Schiffswerte

* Frachtraumkapazität
* Energieproduktion
* Energieverbrauch
* Sauerstoffproduktion
* Sauerstoffspeicher
* Antriebsreichweite
* Hyperraumreichweite
* Scannerstärke
* Hüllenstabilität
* Landefähigkeit
* Strahlenschutz
* Hitzeschutz
* Kälteschutz
* Modulplätze
* Crewkomfort, falls später Crew eingeführt wird

### 14.2 Beispiele für Einschränkungen

* Ohne besseren Antrieb erreicht man keine äußeren Systeme.
* Ohne Hitzeschutz kann man nicht auf Lavaplaneten landen.
* Ohne Strahlungsschutz sind verstrahlte Monde zu gefährlich.
* Ohne größeren Frachtraum lohnen sich lange Mining-Touren weniger.
* Ohne Labor kann man keine fortgeschrittenen Techs erforschen.
* Ohne Raffinerie kann man bestimmte Erze nicht verarbeiten.

---

## 15. Crafting-System

Das Crafting-System soll verständlich, aber motivierend sein.

### 15.1 Materialverarbeitung

Rohstoffe werden oft nicht direkt verwendet, sondern verarbeitet.

Beispiele:

* Eisenerz → Eisenbarren
* Eisenbarren → Eisenplatten
* Kupfererz → Kupferdraht
* Kupferdraht + Silikat → Kabel
* Silikat → Glas
* Titanerz → Titanplatten
* Kristall → Energielinse
* Kohlenstoff → Verbundstoff
* Lithium → Energiezelle

### 15.2 Crafting-Orte

Nicht alles kann überall hergestellt werden.

#### Persönliches Schnellcrafting

Für einfache Dinge:

* Fackel / Leuchtstab
* Markierung
* einfache Reparatur
* Notfall-Sauerstoff
* einfache Blöcke

#### Werkstatt

Für normale Gegenstände:

* Bohrer
* Werkzeuge
* Kabel
* Bauteile
* Medpacks
* einfache Module

#### Raffinerie

Für Materialverarbeitung:

* Erze reinigen
* Legierungen herstellen
* Treibstoff herstellen
* seltene Materialien veredeln

#### Labor

Für Forschung:

* Blaupausen freischalten
* Alien-Daten analysieren
* medizinische Rezepte entdecken
* neue Technologien entwickeln

#### Maschinenraum / Fabrikmodul

Für große Schiffsteile:

* Raumsektionen
* Antriebe
* Energiekerne
* größere Module
* Schiffspanzerung

---

## 16. Beispiel-Rezepte

### 16.1 Basis-Rezepte

#### Eisenplatte

Benötigt:

* 2 Eisenbarren

Ergebnis:

* 1 Eisenplatte

Verwendung:

* Wände
* Bodenplatten
* Schiffsteile
* Werkzeuge

---

#### Kupferkabel

Benötigt:

* 2 Kupferdraht
* 1 Silikat-Isolierung

Ergebnis:

* 1 Kabel

Verwendung:

* Module
* Energieversorgung
* Werkzeuge
* Scanner

---

#### Glasblock

Benötigt:

* 3 Silikat
* 1 Energieeinheit zum Schmelzen

Ergebnis:

* 1 Glasblock

Verwendung:

* Fenster
* Gewächshaus
* Cockpit-Erweiterungen
* Sichtkuppeln

---

#### Energiezelle I

Benötigt:

* 2 Lithium
* 1 Kupferkabel
* 1 Kohlenstoffgehäuse

Ergebnis:

* 1 einfache Energiezelle

Verwendung:

* Werkzeuge
* Scanner
* Außenposten
* kleine Maschinen

---

### 16.2 Werkzeug-Rezepte

#### Titanbohrer

Benötigt:

* 6 Titanplatten
* 4 Kupferkabel
* 2 Energiezellen I
* 1 Bohrkopf
* 2 Eisenplatten

Funktion:

* Standardwerkzeug für harte Blöcke
* baut einfache und mittlere Rohstoffe ab

---

#### Verbesserter Titanbohrer

Benötigt:

* 1 Titanbohrer
* 8 Titanplatten
* 4 Kobaltverstärkungen
* 3 Energiezellen II
* 2 Präzisionslager

Funktion:

* schnellerer Abbau
* höhere Haltbarkeit
* kann härtere Erze abbauen

---

#### Plasmabohrer

Benötigt:

* 1 verbesserter Titanbohrer
* 3 Plasmalinsen
* 6 Iridiumplatten
* 4 Energiezellen III
* 2 Wärmeregulatoren
* 1 Plasmakern

Funktion:

* baut seltene Erze und harte Kristalle ab
* benötigt viel Energie
* für gefährlichere Planeten geeignet

---

#### Quantenbohrer

Benötigt:

* 1 Plasmabohrer
* 2 Quantenkristalle
* 4 Exo-Legierungen
* 1 Gravitationsstabilisator
* 4 Energielinsen
* 1 Alien-Datenfragment

Funktion:

* spätes High-End-Werkzeug
* kann Spezialmaterialien abbauen
* sehr effizient
* wichtig für Endgame-Schiffsausbau

---

### 16.3 Raumanzug-Rezepte

#### Sauerstofftank I

Benötigt:

* 4 Aluminiumbehälter
* 2 Dichtungen
* 1 Sauerstoffventil
* 2 Kupferkabel

Effekt:

* erhöht Sauerstoffzeit leicht

---

#### Sauerstofftank II

Benötigt:

* 1 Sauerstofftank I
* 4 Titanbehälter
* 2 Druckregulatoren
* 2 Energiezellen I
* 1 Filtereinheit

Effekt:

* deutlich längere Außenmissionen

---

#### Strahlungsschutz

Benötigt:

* 6 Bleiplatten
* 4 Titanplatten
* 2 Filtermodule
* 1 Sensorchip

Effekt:

* reduziert Strahlungsschaden

---

#### Thermoanzug-Modul

Benötigt:

* 5 Isolierfasern
* 3 Titanplatten
* 2 Wärmeregulatoren
* 2 Energiezellen II

Effekt:

* schützt vor Hitze und Kälte

---

### 16.4 Medbay-Rezepte

#### Einfaches Medpack

Benötigt:

* 2 organische Fasern
* 1 steriles Gel
* 1 Kohlenstoffverband

Effekt:

* stellt Gesundheit wieder her

---

#### Antitoxin

Benötigt:

* 2 Giftproben
* 1 Filterextrakt
* 1 medizinisches Gel

Effekt:

* heilt Vergiftung

---

#### Strahlungsblocker

Benötigt:

* 1 medizinisches Gel
* 1 Bleisalz
* 1 Kristallpulver
* 1 sterile Ampulle

Effekt:

* reduziert kurzfristig Strahlungsschaden

---

### 16.5 Schiffserweiterungs-Rezepte

#### Frachtraum-Erweiterung I

Blaupause freischalten:

* 1 Datenfragment
* 20 Eisen
* 10 Aluminium
* 5 Kupferkabel

Bau benötigt:

* 80 Eisenplatten
* 40 Aluminiumträger
* 16 Kupferkabel
* 4 Energieknoten
* 2 Türmodule

Effekt:

* erhöht Frachtraumkapazität
* fügt sichtbaren Lagerraum hinzu

---

#### Werkstatt-Erweiterung I

Blaupause freischalten:

* 2 Datenfragmente
* 10 Titan
* 5 Kristalle

Bau benötigt:

* 40 Eisenplatten
* 20 Titanplatten
* 10 Kupferkabel
* 4 Energiezellen I
* 2 Werkzeughalterungen
* 1 Fertigungstisch

Effekt:

* bessere Werkzeuge herstellbar
* Reparaturen günstiger
* neue Crafting-Rezepte

---

#### Raffinerie-Modul

Blaupause freischalten:

* 3 Datenfragmente
* 20 Titan
* 10 Kobalt
* 1 Analyseprobe von einem Erzvorkommen

Bau benötigt:

* 50 Titanplatten
* 20 Kobaltverstärkungen
* 12 Wärmeregulatoren
* 8 Energiezellen II
* 10 Kupferkabel
* 1 Schmelzkammer

Effekt:

* Erze können effizienter verarbeitet werden
* Legierungen werden freigeschaltet
* seltene Materialien werden nutzbar

---

#### Labor-Modul

Blaupause freischalten:

* 5 Datenfragmente
* 10 Kristalle
* 1 Alien-Artefakt

Bau benötigt:

* 30 Glasblöcke
* 20 Titanplatten
* 12 Energielinsen
* 8 Sensorchips
* 4 Analysegeräte
* 1 Forschungskern

Effekt:

* Tech-Tree wird erweitert
* fortgeschrittene Blaupausen freischaltbar
* Alien-Funde analysierbar

---

#### Hyperraum-Antrieb I

Blaupause freischalten:

* 8 Datenfragmente
* 2 Quantenkristalle
* 1 Hyperraumspule

Bau benötigt:

* 40 Iridiumplatten
* 20 Exo-Legierungen
* 8 Energiezellen III
* 4 Gravitationsstabilisatoren
* 2 Quantenkristalle
* 1 Antriebskern

Effekt:

* neue Sternensysteme erreichbar
* größere Reisedistanz
* Zugang zu seltenen Welten

---

#### Sauerstoffgenerator

Blaupause freischalten:

* 2 Datenfragmente
* 20 Eis
* 10 Kupfer
* 5 Silikat

Bau benötigt:

* 20 Aluminiumplatten
* 10 Kupferkabel
* 6 Filtermodule
* 4 Druckregulatoren
* 2 Energiezellen I

Effekt:

* Schiff produziert Sauerstoff
* längere Expeditionen möglich
* unterstützt Außenpostenversorgung

---

#### Gewächshaus-Modul

Blaupause freischalten:

* 3 Datenfragmente
* 5 organische Proben
* 10 Glas

Bau benötigt:

* 40 Glasblöcke
* 20 Aluminiumträger
* 10 Wasserbehälter
* 8 Wachstumslampen
* 4 Nährstoffsysteme
* 2 Sauerstoffleitungen

Effekt:

* Pflanzenanbau
* Sauerstoffbonus
* Herstellung medizinischer Zutaten
* langfristige Versorgung

---

## 17. Tech-Tree

Der Tech-Tree soll den Fortschritt strukturieren.

### 17.1 Hauptkategorien

#### 1. Schiffsausbau

* Frachtraum I, II, III
* Werkstatt I, II
* Labor
* Raffinerie
* Maschinenraum
* Hyperraum-Antrieb
* bessere Landestützen
* Hitzeschutz-Landung
* Strahlenschutz-Hülle
* Schildgenerator

#### 2. Werkzeuge

* Titanbohrer
* verbesserter Titanbohrer
* Plasmabohrer
* Quantenbohrer
* Blockplatzierer
* Strukturprojektor
* Reparaturwerkzeug
* Bergbaudrohne

#### 3. Raumanzug

* Sauerstofftank I, II, III
* Thermoschutz
* Strahlungsschutz
* Giftschutz
* Jetpack
* Materialtransfer-Modul
* Gefahrensensor
* Notfall-Heilmodul

#### 4. Navigation und Scanner

* Planetenscanner I
* Planetenscanner II
* Tiefenscanner
* Bioscanner
* Artefaktscanner
* Sternenkarten-Erweiterung
* Signal-Analyse
* Hyperraum-Navigation

#### 5. Produktion und Verarbeitung

* einfache Werkstatt
* Raffinerie
* Legierungsherstellung
* Energiezellen-Produktion
* Kristallverarbeitung
* Plasmaverarbeitung
* Quantenmaterial-Verarbeitung

#### 6. Außenpostenbau

* Sauerstoffstation
* Landemarkierung
* Lagerkiste
* Solargenerator
* Schutzkuppel
* Scanner-Mast
* automatische Erzstation
* Telemetrie-Modul

---

## 18. Beispiel-Fortschrittsphasen

### Phase 1: Überleben und erste Rohstoffe

Spieler hat:

* kleines Schiff
* einfachen Bohrer
* kleine Sauerstoffmenge
* kleinen Frachtraum
* einfache Werkstatt

Ziele:

* Eisen, Kupfer, Silikat sammeln
* erste Werkzeuge bauen
* Sauerstofftank verbessern
* Frachtraum erweitern
* erste Planeten scannen

---

### Phase 2: Besseres Mining und erste Schiffserweiterungen

Spieler erreicht:

* Titan
* Kobalt
* Lithium
* bessere Energiezellen
* Werkstatt-Erweiterung
* Frachtraum-Erweiterung

Ziele:

* Titanbohrer verbessern
* Raffinerie bauen
* gefährlichere Welten betreten
* bessere Raumanzugmodule herstellen

---

### Phase 3: Forschung und Spezialwelten

Spieler erreicht:

* Labor
* Datenfragmente
* Alien-Artefakte
* seltene Kristalle
* Strahlungs- und Hitzeschutz

Ziele:

* neue Technologien freischalten
* Spezialmaterialien abbauen
* Planeten mit Umweltgefahren erkunden
* Hyperraum-Technologie vorbereiten

---

### Phase 4: Erweiterte Raumfahrt

Spieler erreicht:

* Hyperraum-Antrieb
* große Schiffsmodule
* Plasmabohrer
* bessere Scanner
* komplexere Außenposten

Ziele:

* neue Sternensysteme erreichen
* seltene Materialien sammeln
* Schiff stark erweitern
* größere Bauprojekte umsetzen

---

### Phase 5: Endgame

Spieler erreicht:

* Quantenbohrer
* Exo-Legierungen
* große modulare Schiffserweiterungen
* seltene Alien-Technologien
* sehr gefährliche Planeten

Ziele:

* eigenes großes Schiff gestalten
* seltenste Materialien finden
* alle Sternensysteme erkunden
* besondere Bauwerke oder Stationen errichten
* Endgame-Projekte abschließen

---

## 19. Sternenkarte und Erkundung

Die Sternenkarte im Cockpit ist ein zentrales Element.

### 19.1 Funktionen

Der Spieler kann:

* bekannte Sternensysteme ansehen
* Planeten auswählen
* Planeten scannen
* Gefahren einschätzen
* Rohstoffwahrscheinlichkeiten sehen
* Landeplätze auswählen
* entdeckte Orte markieren
* Außenposten wiederfinden
* unbekannte Signale verfolgen

### 19.2 Planetenscan

Vor der Landung kann der Spieler eine Welt scannen.

Ein Scan zeigt beispielsweise:

* Atmosphäre: atembar / giftig / keine
* Temperatur: normal / kalt / heiß / extrem
* Strahlung: niedrig / mittel / hoch
* Hauptrohstoffe
* seltene Rohstoffe
* Lebensformen
* Ruinenwahrscheinlichkeit
* Landeschwierigkeit
* empfohlene Ausrüstung

Beispiel:

**Planet: Arkon-7**

* Typ: Eisplanet
* Atmosphäre: dünn, nicht atembar
* Temperatur: extrem kalt
* Rohstoffe: Eis, Nickel, Kristalle, Lithium
* Gefahr: Kälteschaden
* Empfohlen: Thermoanzug-Modul, Sauerstofftank II
* Besonderheit: unbekanntes Signal unter der Oberfläche

---

## 20. Missionen und Ziele

Das Spiel kann frei erkundbar sein, sollte aber klare Ziele anbieten.

### 20.1 Kurzfristige Ziele

* Sammle 50 Eisen
* Baue einen Titanbohrer
* Erweitere den Frachtraum
* Scanne einen neuen Planeten
* Baue einen Sauerstofftank
* Finde ein Datenfragment

### 20.2 Mittelfristige Ziele

* Baue eine Raffinerie
* Erforsche den Plasmabohrer
* Errichte einen Außenposten
* Lande auf einem gefährlichen Planeten
* Finde ein Alien-Artefakt
* Baue den Hyperraum-Antrieb

### 20.3 Langfristige Ziele

* Erreiche ein neues Sternensystem
* Baue ein großes Raumschiff
* Erkunde seltene Planeten
* Erforsche Alien-Technologie
* Baue eine eigene Raumstation oder Planetenbasis
* Entdecke die Herkunft bestimmter Artefakte

---

## 21. Mögliche Story-Grundlage

Das Spiel kann auch ohne starke Story funktionieren. Trotzdem hilft eine einfache Rahmenhandlung.

Vorschlag:

Der Spieler besitzt ein altes, kleines Raumschiff und startet in einem bekannten Randsektor. Viele Sternensysteme sind nur teilweise kartografiert. Alte Signale, verlassene Stationen und unbekannte Artefakte deuten darauf hin, dass es früher eine größere Zivilisation oder ein altes Raumfahrernetzwerk gab.

Der Spieler baut sein Schiff aus, folgt Signalen, entdeckt neue Welten und findet nach und nach Hinweise auf diese alte Technologie.

Die Story soll nicht zu stark vorschreiben, was der Spieler tun muss. Sie soll eher Motivation für Erkundung und Forschung liefern.

---

## 22. Gegner und Konflikte

Das Grundkonzept benötigt nicht zwingend Kampf, aber Konflikte können das Spiel spannender machen.

Mögliche Gefahren:

* aggressive Kreaturen
* Weltraumpiraten
* automatische Verteidigungsdrohnen
* alte Alien-Wächter
* instabile Roboter
* Umweltkatastrophen
* Meteoriteneinschläge
* Schiffsschäden
* Sauerstoffnotfälle

Kampf sollte nicht der Hauptfokus sein, sondern eine Ergänzung zur Erkundung.

Passende Ausrüstung statt klassischer Fantasy-Waffen:

* Laserschneider
* Impulsgewehr
* Betäubungsstrahler
* Plasmaschneider
* Verteidigungsdrohne
* Energieschild
* Reparaturdrohne

---

## 23. Baublöcke

Die Welt und die Gebäude bestehen aus Blöcken.

### 23.1 Natürliche Blöcke

* Stein
* Erde
* Sand
* Eis
* Basalt
* Kristall
* Erzblock
* organischer Block
* Lava-Gestein
* giftiger Schleimblock

### 23.2 Verarbeitete Blöcke

* Eisenwand
* Aluminiumwand
* Titanwand
* Glasblock
* Lichtblock
* Energieleitung
* Sauerstoffleitung
* Bodenplatte
* Türblock
* Fensterblock
* Maschinenblock

### 23.3 Schiffsspezifische Blöcke

* Hüllenblock
* Innenwandblock
* Korridorblock
* Türmodul
* Fensterkuppel
* Energieknoten
* Lagersegment
* Werkbanksegment
* Maschinenwand
* Cockpit-Konsole

---

## 24. Benutzeroberfläche aus Spielsicht

Keine technische Umsetzung, nur funktionale Anforderungen.

### 24.1 Standard-HUD

Das Spiel sollte dem Spieler wichtige Werte zeigen:

* Gesundheit
* Sauerstoff
* Energie des Werkzeugs
* ausgewählter Hotbar-Slot
* Warnungen
* Frachtraumstatus
* Umgebungstemperatur oder Gefahr
* aktuelle Missionshinweise

### 24.2 Hotbar

Am unteren Bildschirmrand.

Zeigt:

* Werkzeuge
* Blöcke
* Verbrauchsgegenstände
* Scanner
* Sauerstoff
* Medpacks

### 24.3 Cockpit-Oberfläche

Im Cockpit soll eine eigene Anzeige erscheinen:

* Sternenkarte
* Planetendaten
* Schiffszustand
* Reiseoptionen
* Scanergebnisse
* Landepunkte
* Warnungen
* Zielauswahl

### 24.4 Crafting-Oberfläche

Soll anzeigen:

* verfügbare Rezepte
* gesperrte Rezepte
* fehlende Materialien
* vorhandene Materialien im Inventar
* vorhandene Materialien im Schiffsfrachtraum
* benötigte Station
* Ergebnis des Rezepts

Wichtig: Materialien im Schiffsfrachtraum sollen beim Crafting berücksichtigt werden, wenn sich der Spieler im Schiff befindet.

---

## 25. Spielerfortschritt

Der Fortschritt soll vor allem über Ausrüstung, Schiff und Wissen laufen.

### 25.1 Fortschrittsarten

* bessere Werkzeuge
* besserer Raumanzug
* größeres Schiff
* neue Räume
* bessere Scanner
* neue Sternensysteme
* neue Rohstoffe
* neue Rezepte
* neue Blaupausen
* neue Bauoptionen
* bessere Umweltresistenz

### 25.2 Keine reine Level-Abhängigkeit

Das Spiel sollte nicht hauptsächlich über klassische Charakterlevel funktionieren. Wichtiger ist:

* Was hat der Spieler gebaut?
* Welche Rohstoffe hat er gefunden?
* Welche Blaupausen kennt er?
* Wie gut ist sein Schiff?
* Welche Planeten kann er sicher betreten?

---

## 26. Beispiel: Frühe Spielstunde

Ein möglicher Ablauf der ersten Spielphase:

1. Spieler wacht im kleinen Schiff auf.
2. Cockpit zeigt einen nahen Felsmond als sicheres Ziel.
3. Spieler landet.
4. Er steigt mit einfachem Bohrer aus.
5. Er baut Stein, Eisen und Kupfer ab.
6. Materialien werden teilweise automatisch ins Schiff transferiert.
7. Spieler entdeckt eine kleine Höhle.
8. Dort findet er ein Datenfragment.
9. Sauerstoff wird knapp.
10. Spieler kehrt zum Schiff zurück.
11. In der Werkstatt verarbeitet er Eisen und Kupfer.
12. Er baut Kabel und Eisenplatten.
13. Er schaltet die Blaupause für Sauerstofftank I frei.
14. Er baut den Sauerstofftank.
15. Jetzt kann er längere Außenmissionen machen.
16. Als nächstes Ziel erscheint: Frachtraum erweitern.

Das soll den Spieler sofort in den Kernloop bringen.

---

## 27. Beispiel: Mittlere Spielphase

1. Spieler hat eine Raffinerie gebaut.
2. Er findet einen Lavaplaneten mit Titan und seltenen Energieerzen.
3. Der Planet ist zu heiß für den aktuellen Anzug.
4. Spieler muss zuerst ein Thermomodul bauen.
5. Dafür braucht er Isolierfasern von einem Dschungelplaneten.
6. Er fliegt zum Dschungelplaneten.
7. Er sammelt Pflanzenfasern und medizinische Proben.
8. Er baut das Thermomodul.
9. Jetzt kann er kurzzeitig auf dem Lavaplaneten arbeiten.
10. Dort sammelt er Titan und Energieerz.
11. Daraus entsteht der verbesserte Titanbohrer.
12. Mit diesem kann er neue, härtere Materialien abbauen.
13. Dadurch wird der Weg zum Plasmabohrer eröffnet.

So entsteht eine natürliche Kette aus Erkundung, Ziel, Vorbereitung, Rückkehr und Belohnung.

---

## 28. Beispiel: Späte Spielphase

1. Spieler entdeckt ein unbekanntes Signal in einem entfernten System.
2. Der aktuelle Antrieb reicht nicht.
3. Er muss den Hyperraum-Antrieb bauen.
4. Dafür braucht er Quantenkristalle, Iridium und eine Hyperraumspule.
5. Quantenkristalle gibt es auf einem gefährlichen Kristallmond.
6. Iridium findet man auf einem verstrahlten Asteroiden.
7. Die Hyperraumspule liegt in einer verlassenen Station.
8. Der Spieler muss mehrere Expeditionen vorbereiten.
9. Nach dem Bau des Hyperraum-Antriebs öffnet sich ein neuer Bereich der Sternenkarte.
10. Dort warten seltene Welten, neue Materialien und größere Geheimnisse.

---

## 29. Atmosphäre und Stil

### 29.1 Optik

Die Optik soll kantig, blockartig und klar lesbar sein.

Stilmerkmale:

* Klötzchen-Ästhetik
* einfache Formen
* klare Farben
* blockartige Planetenlandschaften
* modulare Raumschiff-Innenräume
* sichtbare Maschinen und Konsolen
* futuristische, aber verständliche Objekte

### 29.2 Stimmung

Das Spiel soll eine Mischung erzeugen aus:

* Entdeckung
* Einsamkeit im All
* Basteln und Verbessern
* Neugier
* leichten Survival-Spannungen
* Stolz auf das eigene Schiff
* Freude am Sammeln und Bauen

Es soll nicht zu düster sein. Der Spieler soll sich motiviert fühlen, immer noch eine weitere Welt zu besuchen.

---

## 30. Wichtige Design-Prinzipien

### 30.1 Das Schiff ist immer wichtig

Das Raumschiff darf nie nur Transportmittel sein. Es ist:

* Basis
* Zuhause
* Lager
* Werkstatt
* Forschungszentrum
* Fortschrittsobjekt
* sichtbares Ergebnis der Spielleistung

### 30.2 Jede neue Welt soll einen Grund haben

Planeten sollen sich unterscheiden durch:

* Rohstoffe
* Gefahren
* Biome
* Atmosphäre
* Schwerkraft
* besondere Funde
* visuelle Stimmung
* benötigte Ausrüstung

### 30.3 Crafting soll Ziele erzeugen

Rezepte sollen nicht nur Listen sein. Sie sollen dem Spieler Ziele geben:

* „Ich brauche Titan für den besseren Bohrer.“
* „Ich brauche Glas und organische Proben für das Gewächshaus.“
* „Ich brauche Strahlungsschutz, um den Asteroiden zu betreten.“
* „Ich brauche Quantenkristalle für den Hyperraum-Antrieb.“

### 30.4 Fortschritt soll sichtbar sein

Der Spieler soll sehen, was er erreicht hat:

* größeres Schiff
* neue Räume
* bessere Werkzeuge
* größere Lager
* neue Planeten erreichbar
* neue Außenposten
* bessere Ausrüstung

### 30.5 Freiheit statt linearer Missionsschlauch

Das Spiel soll Ziele anbieten, aber der Spieler soll frei entscheiden können:

* zuerst Schiff ausbauen
* zuerst Planetenbasis bauen
* zuerst Rohstoffe sammeln
* zuerst Sternensystem erkunden
* zuerst Forschung verfolgen

---

## 31. Konkrete Mindestanforderungen für eine erste spielbare Version

Eine erste Version des Spiels sollte nicht alles enthalten, aber den Kern beweisen.

### 31.1 Mindestumfang Raumschiff

* begehbares Startschiff
* Cockpit
* Schlafbereich
* Medbay
* Werkstatt
* Frachtraum
* einfache Schiffszustandsanzeige
* eine erste Schiffserweiterung, zum Beispiel Frachtraum I

### 31.2 Mindestumfang Welt

* mindestens ein prozedural generierter Planet
* blockbasierte Landschaft
* abbaubare Blöcke
* platzierbare Blöcke
* mehrere Rohstoffe
* einfache Höhlen oder Geländeformen
* Landen und Aussteigen

### 31.3 Mindestumfang Crafting

* Hotbar
* Inventar
* Schiffsfrachtraum
* automatische Rohstoffübertragung
* einfache Rezepte
* Werkzeugherstellung
* Sauerstofftank oder Raumanzug-Upgrade
* eine Blaupause zum Freischalten

### 31.4 Mindestumfang Sternenkarte

* Cockpit-Karte
* ein Sternensystem
* mehrere Ziele
* Planetenscan mit Rohstoff- und Gefahrenanzeige
* Auswahl eines Landeortes

### 31.5 Mindestumfang Fortschritt

* besseres Werkzeug craftbar
* Frachtraum erweiterbar
* Sauerstoff oder Umweltresistenz verbesserbar
* neuer Planet oder neuer Bereich nach Upgrade erreichbar

---

## 32. Erweiterbare Wunschfunktionen für spätere Versionen

Diese Funktionen sind nicht zwingend für den Start, passen aber sehr gut zum Spiel.

### 32.1 Crew

Später könnte der Spieler Crewmitglieder finden.

Crew könnte:

* Räume betreuen
* Boni geben
* Reparaturen beschleunigen
* Forschung verbessern
* auf Außenposten arbeiten
* kleine Geschichten mitbringen

### 32.2 Schiffsdrohnen

Drohnen könnten:

* Rohstoffe sammeln
* reparieren
* scannen
* verteidigen
* Blöcke transportieren
* Außenposten versorgen

### 32.3 Raumkampf

Optional könnte es später einfache Gefahren im All geben:

* Piraten
* Drohnen
* Meteorfelder
* automatische Verteidigungsstationen

Wichtig: Raumkampf sollte nicht den Kern verdrängen. Der Kern bleibt Erkunden, Sammeln, Crafting und Schiffsausbau.

### 32.4 Handel

Stationen könnten Handel ermöglichen:

* seltene Bauteile kaufen
* überschüssige Rohstoffe verkaufen
* Blaupausen erwerben
* Aufträge annehmen

### 32.5 Basen und Raumstationen

Später könnte der Spieler nicht nur auf Planeten bauen, sondern auch:

* eigene kleine Raumstationen errichten
* Asteroidenbasen bauen
* planetare Außenposten vernetzen
* automatische Rohstoffrouten aufbauen

---

## 33. Beispielhafte Objektliste

### Werkzeuge

* Basisbohrer
* Titanbohrer
* verbesserter Titanbohrer
* Plasmabohrer
* Quantenbohrer
* Blockplatzierer
* Strukturprojektor
* Handscanner
* Tiefenscanner
* Bioscanner
* Reparaturwerkzeug

### Verbrauchsgegenstände

* Medpack
* Sauerstoffkapsel
* Energiezelle
* Antitoxin
* Strahlungsblocker
* Wärmegel
* Kälteschutz-Injektor
* Notfall-Leuchtboje

### Schiffsmodule

* Frachtraum
* Werkstatt
* Medbay
* Labor
* Raffinerie
* Maschinenraum
* Sauerstoffgenerator
* Gewächshaus
* Hyperraum-Antrieb
* Schildgenerator
* Drohnenbucht
* Kartenraum
* Dockingmodul

### Bauteile

* Eisenplatte
* Aluminiumträger
* Titanplatte
* Kupferkabel
* Energieknoten
* Druckregulator
* Sensorchip
* Energielinse
* Wärmeregulator
* Filtermodul
* Türmodul
* Hüllenblock
* Glasblock

---

## 34. Beispielhafte Tech-Tree-Struktur

### Start

* Basisbohrer
* Handscanner
* einfache Werkstatt
* kleines Schiff
* kleiner Frachtraum
* einfacher Raumanzug

### Frühe Technologie

* Sauerstofftank I
* Titanbohrer
* Frachtraum-Erweiterung I
* Werkstatt-Erweiterung I
* Planetenscanner I
* einfache Außenposten

### Mittlere Technologie

* Raffinerie
* Sauerstofftank II
* Thermomodul
* Strahlungsschutz
* verbesserter Titanbohrer
* Tiefenscanner
* Gewächshaus
* größere Schiffsräume

### Fortgeschrittene Technologie

* Labor
* Plasmabohrer
* Hyperraum-Antrieb I
* Schildgenerator
* Drohnenbucht
* Artefaktscanner
* Legierungen
* Energiezellen III

### Späte Technologie

* Quantenbohrer
* Hyperraum-Antrieb II
* große Schiffsmodule
* Raumstationsteile
* Exo-Legierungen
* Gravitationsmodule
* Alien-Technologie

---

## 35. Hauptziel des Spiels

Das Hauptziel von **Spacecraft** ist nicht nur, „zu überleben“ oder „Rohstoffe zu sammeln“. Das eigentliche Ziel ist:

**Aus einem kleinen Basisschiff durch Erkundung, Bergbau, Crafting und Forschung ein immer größeres, persönlicheres und leistungsfähigeres Raumschiff zu erschaffen, mit dem der Spieler immer weiter ins All vordringen kann.**

Das Spiel lebt von drei starken Motivationen:

1. **Was finde ich auf der nächsten Welt?**
2. **Was kann ich mit den neuen Rohstoffen bauen?**
3. **Wie verändert sich mein Schiff dadurch sichtbar?**

Wenn diese drei Fragen immer wieder neue Antworten liefern, funktioniert das Spielprinzip.

---

## 36. Kurz-Pitch

**Spacecraft** ist ein blockbasiertes 3D-Weltraum-Crafting-Spiel, in dem der Spieler mit einem kleinen eigenen Raumschiff startet, prozedural generierte Planeten erkundet, Rohstoffe abbaut, Ausrüstung herstellt, Blaupausen erforscht und sein Schiff Stück für Stück zu einer größeren, besseren und individuelleren Weltraumbasis ausbaut.

Das Raumschiff ist dabei nicht nur Transportmittel, sondern das Herz des Spiels: Zuhause, Werkstatt, Lager, Forschungszentrum und Fortschrittssystem zugleich.
