# Informationen für Eltern & Lehrkräfte

*English version: [PARENTS.md](PARENTS.md)*

Blocks Beyond the Stars ist ein Familienprojekt — entworfen von einem Vater und seinem zehnjährigen Sohn,
damit Kinder es spielen können, allein oder gemeinsam mit Erwachsenen. Diese Seite sagt klar, was im Spiel
steckt, was nicht, was Online-Spielen bedeutet und welche Schalter Sie als Eltern, Lehrkraft oder
Server-Betreiber in der Hand haben.

> **Noch keine offizielle Alterseinstufung.** IARC-Einstufungen (PEGI / USK / ESRB in einem Fragebogen)
> werden über teilnehmende Stores vergeben, auf denen das Spiel noch nicht ist — das Folgende ist also
> unsere eigene, ehrliche Erklärung, kein zertifiziertes Siegel. Die Vorarbeit für den ersten echten
> Fragebogen liegt bereit ([docs/developer/AGE_RATING_CHECKLIST.md](../developer/AGE_RATING_CHECKLIST.md));
> unsere eigene Einschätzung: Der Inhalt entspricht **PEGI 7 / USK 6**-Terrain (milde Fantasy-Gewalt), mit
> den üblichen Store-Hinweisen „Users Interact" und „User-Generated Content" für den Online-Modus.

## Inhalts-Erklärung

*(Diese Erklärung veröffentlichen wir auch auf Website und Store-Seiten.)*

**Blocks Beyond the Stars ist ein Block-Bau-Weltraumspiel für Familien.** Man erkundet prozedural
erzeugte Planeten, baut Rohstoffe ab, fertigt Ausrüstung, baut Schiffe und Basen, zähmt Kreaturen und
spielt zusammen.

- **Milder Sci-Fi-Kampf, kein Blut.** Spieler können aggressive Tiere, Roboter und comichafte „Banditen"
  mit Werkzeugen und Sci-Fi-Waffen abwehren. Es gibt kein Blut, keine drastischen Darstellungen, keine
  Sterbeanimationen von Menschen — besiegte Kreaturen und Roboter zerfallen schlicht oder fliehen;
  Banditen werden **verjagt, nie getötet**. Neue Welten starten standardmäßig im Waffenmodus **„nur
  Werkzeuge"** — Kampf ist pro Welt Opt-in.
- **Spielertod ist sanft.** Geht Luft oder Gesundheit aus, wacht man im Med-Bay des eigenen Schiffs
  wieder auf. Andere Spieler bekommen die Gegenstände nicht.
- **Kein Horror.** Höchstens dunkle Höhlen und eine unheimliche Ruine — der Ton des Spiels ist neugierig,
  nicht angsteinflößend.
- **Keine Käufe, keine Werbung, kein Glücksspiel.** Das Spiel ist kostenlos und Open Source. Es gibt
  nichts im Spiel zu kaufen, keine Werbung, keine Lootboxen; die Arcade-Minispiele geben nur
  Wissenspunkte im Spiel.
- **Keine persönlichen Daten nötig.** Zum Spielen braucht es keine E-Mail-Adresse, keinen echten Namen
  und kein Konto über einen selbst gewählten Spielernamen hinaus. Siehe „Daten" unten.
- **Online ist optional.** Der Einzelspieler funktioniert komplett offline (und im Browser). Mehrspieler
  ist etwas, das Sie oder Ihre Kinder bewusst wählen.

## Online spielen — was das bedeutet

Auf einem Mehrspieler-Server kann Ihr Kind anderen Spielern begegnen. Das heißt:

- **Text-Chat und optionaler Sprach-Chat mit Fremden** (auf öffentlichen Servern). Niemand sendet aus
  Versehen: **Sprechen geht nur per gehaltener Taste** (Push-to-Talk), *Zuhören* ist in einem
  lokalen/LAN-Spiel standardmäßig an und lässt sich unter Einstellungen → Sprache abschalten. Öffentlich
  gehostete Welten starten ohne Sprach-Chat, und jeder Server-Betreiber kann ihn ganz abschalten.
- **Nutzererzeugte Inhalte:** Spieler tippen Namen (für sich, Basen, Baken, Kreaturen, Crews,
  Kartenmarker), bauen frei mit Blöcken und können Designs malen. Alles Getippte und Gebaute können
  andere auf diesem Server sehen.

Was Spieler dort schützt — eingebaut, nicht versprochen:

- Ein **Chat- und Namensfilter** ist standardmäßig aktiv (der Betreiber wählt die Stufe; für
  Familienserver gibt es „streng", und jeder getippte Name läuft durch dieselbe Prüfung).
- **`/report`** meldet mit einem Befehl einen Spieler oder eine Nachricht an die Betreiber — auch für
  Kinder in der kostenlosen Browser-Version; auf offiziellen Servern landen Meldungen in einem
  geprüften Posteingang. Niemand wird automatisch bestraft; ein Mensch schaut es an.
- **`/mute <Name>`** blendet Chat und Stimme eines Spielers lokal aus — der andere erfährt es nicht.
- Betreiber können einen Spieler serverweit **stummschalten**, und Hosts bestimmen alle Regeln ihrer
  Welt.
- **Es gibt keine Join- oder Freundes-Codes:** Gruppen (Crews) entstehen nur, indem man einen Spieler
  einlädt, der in diesem Moment in derselben Welt online ist.

**Die sicherste Variante für jüngere Kinder** ist die eigene private Welt: auf dem eigenen Rechner
hosten (LAN) — das Spiel bringt alles mit. Siehe [SELF_HOSTING](../developer/SELF_HOSTING.md), oder
einfach Einzelspieler spielen. Eine private Familienwelt hat genau die Spieler, die Sie eingeladen
haben, und Sie halten jeden Schalter (Waffenmodus, Sprache, Chatfilter, Besucher).

## Daten

- **Konten sind bewusst pseudonym.** Nirgends wird nach E-Mail, echtem Namen oder Geburtsdatum gefragt.
  Ein Spielername ist alles.
- Das optionale Welten-Portal speichert diesen Spielernamen und technische Sitzungsdaten für gehostete
  Welten; Fehlerberichte (F1/F2) enthalten den getippten Text plus technische Logs und werden von den
  Entwicklern gelesen.
- Es gibt **kein Analyse-/Tracking-SDK und kein Werbenetzwerk** im Spiel.
- Das optionale KI-Backend (für dynamische NPC-Dialoge) verarbeitet den Gesprächstext im Spiel, um
  Antworten zu erzeugen; es ist aus, solange der Host es nicht einrichtet, und hat einen Nicht-KI-Ersatz.

## Zeit, Geld, Fairness

- Keine Energie-Timer, keine Täglich-einloggen-Mechaniken, kein Bezahlen für Fortschritt — das Spiel
  nutzt keine Bindungstricks. Der Fortschritt wird lokal (oder auf dem Server der Welt) gespeichert und
  wartet.
- Mehrspieler ist standardmäßig kooperativ. Verbündete können einander nicht schaden — selbst auf
  Servern, deren Host Kampf zwischen Spielern erlaubt hat.

## Fragen oder Sorgen

Eröffnen Sie ein Issue auf [GitHub](https://github.com/marceld23/BlocksBeyondTheStars/issues) oder
nutzen Sie die Feedback-Taste im Spiel (**F1**, Browser **F2**) — beides landet bei den Entwicklern
(einem Vater, der das Spiel mit den eigenen Kindern spielt). Meldungen wie „mein Kind hat etwas
Unangemessenes gesehen" nehmen wir ernst und handeln.
