# Changelog

All notable changes to **Blocks Beyond the Stars** are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions are date-based (CalVer) `YYYY.MM.N` — year, month, release counter within the month
(e.g. `2026.7.2`; SemVer2-valid, so no leading zeros and never a fourth part — see
[ADR 0012](docs/developer/adr/0012-calver-date-based-versioning.md)). Releases up to
[0.9.1] followed [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Each release below mirrors its [GitHub release notes](https://github.com/marceld23/BlocksBeyondTheStars/releases);
the richer, screenshot-laden versions live there. `(#123)` references the pull request or issue.

## [Unreleased]

### 🪐 Planetary rings (#596)

- **Some planets now carry Saturn-like ring systems** — tilted, banded discs with seeded gaps and
  an icy tint, unique per planet. You see them from orbit while flying the system, on the discs of
  ringed neighbours in another world's sky (pale and sky-washed by day, like the real Moon), and —
  standing on a ringed planet itself — as a ribbon arcing across the sky: bold at night, a faint
  pale arc by day.
- Ring assignment is deterministic from the world seed (every player sees the same rings) and
  purely visual; rings are deliberately rare (~1 planet in 9; large and icy/crystal ones more
  often), but **the planet you start on always carries them** — the band across your home sky
  greets you from the first spawn. **Existing worlds gain rings too** — nothing else about your
  universe changes.

### 🧭 Flight system chart & nav waypoint (#597)

- **Press M while flying to open the system chart** — a top-down map of the current system drawn
  from the real flight positions: star, planets, moons, asteroids, stations, wrecks, hostiles, and
  your ship with its heading. The ship holds position while the chart is open; the key is
  rebindable in Settings → Controls.
- **Click to set a nav waypoint** — click a body or station to target it, or click empty space for
  a free waypoint. The space radar shows it as an amber marker with its own live distance readout,
  and the VEGA autopilot (P, AI Core Mk2) now flies to *your* waypoint instead of just the nearest
  station. The waypoint clears automatically when you travel — no stale markers in the next world.

### 🎒 Stacks hold 1024 instead of 99 (#603)

- **Bulk items stack to 1024 per slot.** Every block, ore, material and component (108 items) now fills a
  slot with up to 1024 instead of 99 — a backpack holds ~24,500 blocks instead of ~2,400, so long mining
  runs stop ending with a full inventory that silently eats what you dig.
- **Tools, weapons and equipment are unchanged** (still one per slot), and the deliberately scarce goods
  keep their small caps (medpacks 20, energy cells 50, access codes 10, food 16).
- **Crafting keeps up.** A single craft order accepts a full stack (was capped at 999), and the crafting
  screen's "Max" button now offers up to a full stack instead of stopping at 99.
- Existing saves need no migration: old 99-stacks simply keep filling past 99 as you pick items up.

### 🗑️ Throw unwanted items away

- **New "Throw away" button** in the Inventory and Cargo Hold tabs (#599). Until now nothing could actually
  be *removed* — the cargo hold, a storage crate and the ✕ on the quick-bar all just move an item somewhere
  else — so a stack of dirt you no longer want was carried around forever. It asks once before destroying,
  and clears every stack of that item at once.
- **Your starting equipment is safe.** The drill, scanner, suit lamp, machete and sidearm have no throw-away
  button and are refused by the server, so nobody can strand themselves without a way to dig or to see.
  Everything else — ores, blocks, food, better tools — can go.
- **A full backpack no longer eats things in silence** (#600). With inventory *and* cargo hold full, mined
  drops and craft outputs were destroyed without any message at all. You now get a warning instead — rarer
  now that stacks hold 1024, but no longer invisible when it does happen.

### 🧭 Admin teleport by landmark, not by coordinates

- **`/tp` takes a target word.** World admins can jump to a landmark on the body they are standing on —
  `/tp ship`, `/tp village2`, `/tp pad1`, `/tp factory1`, plus `ruin`, `vault`, `wreck`, `camp`,
  `monument`, `treasure` and anything a player built here (`base`, `beacon`, `beam`, `station`).
  Targets are addressed by kind and a stable 1-based number rather than by their generated names, and
  the number can be written either way round: `/tp village2` and `/tp village 2` mean the same place.
- **`/tp` on its own lists what is here** — every resolvable target with the exact word to type and its
  distance, so the numbering is discoverable instead of guessed at.
- **Factories now show on the planet map.** They were tracked by the server but never drawn.

### 🧑 Bandits look like people again (#601)

- **No more red-eyed robbers.** The bandit's red headband sat across the top of the head and, at any
  distance, read as a pair of glowing red eyes — exactly the look of the Guardian machines. Bandits
  now have a real face (eye whites, pupils, brow), hair, and a **cloth mask over nose and mouth** as
  the "this one is a robber" cue.
- **Their gear stopped glowing Guardian red.** The energy blade, the blaster muzzle and the gunner's
  tracer shot are now cold blue — bought human tech, not alien machine.
- **Every bandit looks like a different person.** Skin tone, hair, mask and jacket colour all vary,
  so a raider camp is a group of individuals instead of one face copied four times.

## [2026.7.22] — 2026-07-29

The wayfinder release: the planet map becomes a real navigation tool and the world generator
stops losing villages. Map markers are redrawn as bold, backed, colour-true pictograms, waypoints
get their own compass icon with a live distance readout, and every structure a world rolls is now
guaranteed a home — with foundations that adapt to slopes, lakes (stilt villages!) and lava.
Rounding it off: menu dialogs are properly opaque again, and moons no longer hang as black
silhouettes in the daytime sky.

### 🗺️ Planet map you can actually read (#592)

- **Landmark markers are finally legible.** The world-map marker set (`map_*`) is regenerated as
  bold filled pictograms (the old thin-line icons lit as few as 1 % of their pixels), every marker
  now sits on a dark backing disc so it separates from any terrain colour, and markers are ~1.5×
  larger — and additionally scale with the UI-scale accessibility setting.
- **Marker colours mean something again.** The icons are pure white ink, so the tints show true:
  occupied landing pads are red (they rendered grey before), waypoints amber (they rendered green),
  and the legend shows each marker in its real on-map colour — with the full marker set listed,
  including the previously missing base icon.
- **Waypoints work as navigation.** The HUD compass shows the map-set waypoint as its own flag icon
  (no longer a tiny amber square identical to beacon blips) with its own distance readout, compass
  blip distance is log-scaled so approaching a target is visible (before, everything past ~37 m
  pinned to the rim), and a stale waypoint no longer survives travelling to another planet.

### 🏗️ Structure placement guarantee & terrain-adaptive foundations (#586)

- **What the world rolls, the world gets.** Settlements, ruins, factories, bandit camps, monuments,
  vaults, treasure chests and data cubes are no longer silently dropped when the terrain is dramatic:
  the placement search now escalates — classic dry-and-flat spots first, then widening best-fit rings —
  until every rolled structure has a home. (New worlds; existing worlds keep their exact layout.)
- **Foundations that fit the terrain.** The seat adapts to what it lands on: slopes get stepped terrace
  aprons instead of sheer foundation walls, rugged massif flanks get a cut-and-fill rock shelf, lakes
  carry **stilt villages** on pile platforms (and drowned ruins), lava plains may hold a dead city on a
  basalt plinth, and vaults under a water body rise as a diveable stone **well head**.
- **Placements are now pinned.** Where each structure landed is stored with the save at first stamp, so
  future placement improvements can never move villages, camps or vaults out from under their own blocks
  (previously the positions were re-derived from the seed on every load).

### Fixed

- Other planets and moons in the surface sky no longer read as dark silhouettes against the
  daytime sky: a sky-colour atmosphere wash makes them pale and sky-tinted by day (like the real
  daytime Moon), and their unlit night sides survive as a faint disc instead of vanishing to
  black. Airless worlds and the space view are unchanged. (#585)
- Menu dialogs (world options, account, settings, …) are properly opaque again instead of
  ghost-transparent, and they all share one consistent darkening scrim behind the panel — the
  menu behind a dialog no longer bleeds through the text. (#588)

## [2026.7.21] — 2026-07-29

The landforms release: worlds finally dare vertical drama. The regional terrain pool grows from
five to eight archetypes, a rare drama tail pushes peaks toward Y ≈ 280 under automatic snow,
table mountains, massifs and rift chasms land as far-visible landmarks, and three new planet
types join the galaxy. Below ground the build band opens to Y −2100 — the deep kilometre — with
ore density that ramps up and deep caves that stay partly open below the lava table. Existing
worlds reshape once (like the 0.9.0 overhaul); everything players built stays in place.

### ⛰️ Terrain extremes & landform variety (#576, #577, #578, #579)

> ⚠️ **One-time world reshape:** terrain is derived from the seed, so existing worlds change shape
> once with this release (like the 0.9.0 worldgen overhaul). Player-built blocks survive in place.

- Three new regional terrain archetypes join the blend pool: **plateau decks** (terraced mesa
  country), **extreme peaks** (sharpened crests far above the old ceiling) and **rift gorges** —
  and worlds now draw 2–8 archetypes instead of 2–5, so regions differ more. (#576)
- A **~6 % drama tail**: a small share of bodies rolls 1.9–2.6× relief instead of 0.9–1.5× — the
  rare world that reads genuinely extreme, with peaks toward Y ≈ 280 under automatic snow and ice.
  (#576)
- **Table mountains**: sparse flat-topped buttes with near-vertical walls on dry, rocky-reading
  worlds (dunes/mesa/canyon styles + savanna) — a climbable landmark with a dead-flat crown. (#577)
- **Massifs and rift chasms**: rare single giant mountains (+120–220, ridged flanks, iced summits,
  visible from very far) and deep straight gorges (50–130 blocks) that flood into fjord lakes where
  they dip under the sea. At most one landmark claims a column; everything stays seam-free and caps
  safely under the atmosphere line. (#578)
- Three new planet types: **Tablelands** (monumental grand-mesa terraces), **Badlands**
  (fine-ridged painted gully country) and the exotic **Karst** (sheer jungle towers with walkable
  crowns) — with DE+EN names and descriptions. (#579)

### ⛏️ The deep kilometre opens up (#580)
- The vertical build band now reaches **Y −2100** (was −512): even the deepest-rolled world
  foundation is reachable, so "dig to the bedrock" works on every world.
- Cave/ore calibration now samples the full depth band, deep caves below the lava table stay
  **partly open** (coherent molten regions instead of a uniform lava bath), and ore density ramps
  up to **+60 %** over the first ~600 blocks down — digging deep rewards instead of frustrates,
  while shallow starter veins stay exactly where they were.

### 🧹 Fixed
- Monuments: a body whose roll decided "no monuments here" never persisted that decision and
  re-rolled it on every load — the decision is now recorded like every other stamp. (#578)

## [2026.7.20] — 2026-07-28

The suit-up release: pilots finally look like astronauts — a proper spacesuit with an open helmet,
visor band, gloves and a life-support pack, while NPCs deliberately stay civilian so a suited
figure is always a real player at a glance. And a forgotten account password is no longer fatal:
rescue codes at signup, in-game password change and an operator reset path close the last gap in
account access. Also the first release the new startup update notice announces on its own.

### 🧑‍🚀 Player avatars now wear a spacesuit (#564)
- Your avatar (third-person, other players, the avatar editor and colour-menu previews) now reads
  as an astronaut: an open helmet with a raised dark-glass visor band, gloves, a neck seal and
  collar ring, a chest control panel and a life-support backpack with twin tanks. The suit tints
  with your chosen avatar colours, and your face — including a drawn pixel face — stays visible
  through the open visor. Settlement and station NPCs deliberately keep their civilian look, so a
  suited figure is always a real player at a glance.
- Fixed invisible head gear: the armor-helmet and helmet-lamp visuals had been buried inside the
  avatar's head since gear existed (a coordinate-space slip). The armor helmet now shows as an open
  face-guard shell over the suit helmet, the lamp sits visibly on its side, and the armor backpack
  replaces the suit's life-support pack while worn.

### 🧹 Fixed
- Portal website: the rules-consent checkbox rendered as a centered full-width block (the global
  input styling caught it) and its label wrapped awkwardly around the button — it is now an
  inline checkbox in a proper flex row, on both the signup card and the rules re-accept box. (#560)
- WorldHost: the admin password-reset log lines no longer include raw account ids (they log the
  account name instead, matching the signup log rule; CodeQL cs/cleartext-storage), and the API
  twin now resolves the account first so unknown ids take the refusal path too. (#561)

### 🆘 Rescue codes + operator password reset — a forgotten password is no longer fatal (#557, #558)
- **Rescue codes ("Rettungscodes")**: every new account gets 3 one-time codes at signup — shown
  exactly once with a big "write these on paper!" prompt (in-game and on the portal website). A
  code plus a new password resets a forgotten account password via the new "Forgot password?"
  button; codes are stored only as PBKDF2 hashes, survive sloppy typing (case/spaces/dashes),
  and can be re-issued from the Account panel (current password required — the old set is void).
- **Operator reset**: the `/admin` account lookup gained a "reset password" button — it shows a
  one-time readable temp password (never in a URL), signs out every session, and the next login
  lands the player directly in the change-password form until they pick their own. Developer
  accounts are excluded on every path, so admin credentials can never take over the operator.

### 🔑 Account access: change your password + clearer sign-in (#555, #556)
- **Change your account password in-game**: the Official Worlds → Account panel now rotates a known
  password (current one required; every other signed-in device is signed out). New endpoint
  `POST /api/account/password` — wrong-guess attempts share the failed-login budget, so a stolen
  session cannot brute-force its way to owning the account.
- **The sign-in form no longer causes lockouts**: signing out keeps the account name prefilled (it
  was blanked, and players then typed their *player* name and concluded the account was gone), the
  password field is labelled "account password" (it borrowed the world-password wording), and both
  the in-game and web signup say clearly that the account name is for signing in only — the player
  name stays a separate, freely changeable identity.

## [2026.7.19] — 2026-07-28

The constellation release — and the first under the date-based version scheme (`YYYY.MM.N`, see
[ADR 0012](docs/developer/adr/0012-calver-date-based-versioning.md)): after v0.9.1 comes
v2026.7.19, July's nineteenth release. Newly created worlds roll star-system archetypes from lone
giants to pirate havens, the installed client finally announces new versions by itself from an
official update feed, and the devblog release notes are readable in-game.

### 🌌 Star systems have character now (#546–#549)
- **Every system rolls an archetype** in newly created worlds: alongside the familiar mix there are
  **Lone Giants** (one oversized planet with 4–8 moons), **Swarms** (many small planets, hardly any
  moons), **Belts** (asteroid fields with barely a planet), **Hubs** (guaranteed stations and busy
  trade lanes), **Desolate** systems (empty, silent space), **Pirate Havens** (always lawless — no
  stations, more camps, more wrecks) and **Twin Worlds** (two like-sized planets on close orbits).
  The home system never rolls the hostile/empty archetypes, so a fresh start stays friendly (#546).
- **Planets genuinely differ in size now** (#549): archetypes bias the walkable circumference —
  giants reach 16000 blocks around, swarm dwarfs go down to 4000 (the classic band is 5000–12000) —
  and the orbit view, the sky and gravity all reflect it.
- **The inhabitants follow the archetype** (#547): trader traffic, pirate-space flags, bandit-camp
  odds, ambient drones/UFOs and wreck frequency all read the system's character; the previously dead
  **Danger** world option now scales hostile encounter odds globally. The synthesised fallback
  station only appears in the home system anymore — a Desolate system out there really has none.
- **The space view keeps up** (#548): moons stack on their own orbit rings around their true parent
  planet (a giant's 8 moons read as a family, not one crowded shell), and the from-the-surface sky
  sizes bodies by their real circumference, capped at the 14 most prominent.
- **Existing worlds are completely untouched**: variance only applies to worlds created from this
  release on (the galaxy re-derives from the seed, so changing counts on an old save would orphan
  visited worlds — bias 0 and the Standard archetype reproduce the old layout bit-for-bit).

### 📅 Date-based versions
- **The version scheme switches from SemVer to CalVer `YYYY.MM.N`** (year, month, release counter
  within the month) — this release is the first under the new scheme; v0.9.1 was the last SemVer
  release. The tag stays the single source of truth and the format stays SemVer2-compatible, so
  auto-updates keep working across the switch. The machine-wide MSI shows the year mapped down
  (`26.x.x`) in Apps & Features because Windows Installer caps its major version field at 255 —
  all other surfaces show the full version.
- **The MSI install wizard's finish page now shows a copyright line** (© year JuMaVe Games).

### 🔔 The game finally tells you about updates (#543)
- **Update notice on startup.** An installed client now quietly checks for a newer release while the
  splash screens play and, if one exists, offers it on the main menu: *Install now* downloads and
  restarts into the new version, *Later* dismisses the notice until the next launch. Can be turned
  off in **Settings → Software update**.
- **An official update feed exists now.** Release builds attach the Velopack feed files to the GitHub
  release, and the client's update server defaults to it — previously the feed was never published
  anywhere and the URL shipped empty, so even the manual "Check for updates" button could never find
  one. Self-hosters can still point the URL at their own server's `/updates` endpoint.
- Applies from this release onward: older installs (0.9.1 and before) don't carry the check yet, so
  they need one last manual download.

### 📰 "What's new?" in the main menu (#543)
- **The devblog release notes are readable in-game now** — a new bottom-bar button opens them,
  localized in German and English, newest first. After an update the notes open **once** by
  themselves, so you see what changed without hunting for a blog post.
- Sourced from the same posts the website devblog publishes; the game fetches the latest feed online
  and falls back to the notes bundled with the build when offline.

### 🔗 Fixed
- **The GitHub link in the "Join in" overlay actually opens now** (#544) — it was link-styled text
  with no click handler on every desktop platform. It's a real button now, joined by a second one
  that opens the game website in your language.
- **Settings: language and back are always visible now** — they used to be the last rows of the
  scroll list, so leaving the screen meant scrolling the whole way down first. They sit in a fixed
  footer under the list now.
- **The pilot-name field on the main menu stands out** — an accented backdrop and a bold label make
  the required first step obvious instead of a grey side note.

## [0.9.1] — 2026-07-27

The relics release: somebody was here before you. Ancient monuments now stand on worlds — and on
airless moons — with glowing runes worth scanning, asteroids finally come in five distinct families
with their own crater relief, stopping a hosted world saves it properly again, and the in-game chat
help stopped shouting HTML at everyone.

> ⚠️ **Existing asteroids change with this release.** An asteroid's family and crater relief are
> derived deterministically from the world seed and were never persisted, so asteroids in existing
> worlds change surface type and shape — the same one-time cut as the 0.9.0 worldgen overhaul.
> **Everything players built or mined is preserved**, though blocks placed on an old surface can end
> up floating above or buried under the new one.

### 🗿 Monuments: somebody was here first (#522–#527)
- **Five new relics** stand on the surface of a world: a half-collapsed **arcade** of arches, a
  free-standing **gate** that leads nowhere, a ring of **standing stones**, a weathered **obelisk**
  and a **rune altar**. Up to three per world, never two of the same kind.
- **They are the only structure that also stands on airless moons** — whoever raised them did not
  need air either, and nothing has weathered them since.
- **Glowing runes.** Every monument is carved with them. **Scan the runes where they stand** and you
  read the inscriptions themselves: worth far more knowledge than identifying a stone, with a lore
  line and a new **Codex → Discoveries → Monuments** entry. Each kind of monument pays once **per
  planet**, so the stone circle on the next world is worth the walk too.
- **New building materials.** Ancient Brick and Rune Stone are freely mineable — like ruins, what you
  clear stays cleared — and yours to build with. About one relic in three hides a small relic cache.
- **Fallen towns finally have a landmark again:** ruins now keep the broken version of their central
  feature — snapped column stumps, an arch springer jutting into nothing, toppled inscribed stones.
- A new world feature that ships in a later release can no longer be stamped on top of somebody's
  base: the placement check now skips any footprint that already holds player-built blocks.

### ☄️ Asteroids come in five families now (#515, #518)
- **Every landable asteroid used to be the same crystal-covered rock** — one hardcoded type with no
  biome list, so every column of every asteroid surfaced as crystal. Asteroids now roll one of
  **five families** per body: **stony** (the workhorse — stone, basalt and sand over shallow iron,
  silicate, copper and tin), **metallic** (nearly solid metal, with cobalt, tungsten, platinum and
  neodymium deep down), **icy** (−95 °C, volatiles instead of metals), **carbon** (soot-black,
  cave-riddled, uranium and diamond deep, the best data-cache odds) — and **crystalline**, yesterday's
  everywhere-look, now the rare find.
- **Craters have character per body.** Crater density, basin width, depth, rim height and how rolling
  the regolith in between is are now rolled from each body's own seed instead of five global
  constants — and airless **moons** share the same path, so they gain the same variance.
- Every family carries copper and silicate, so the cable progression can never strand you on a rock
  without them — a content test now guarantees it.

### 🛟 Stopping a hosted world saves it again (#519)
- **Stopping a world from the admin page silently lost everything since the last autosave.** The game
  server inside the container inherited an ignored interrupt signal (a POSIX shell rule for
  background jobs), so the polite shutdown never arrived and Docker hard-killed the world after its
  full grace period — on every admin stop, scheduled restart and image redeploy. The server now
  handles **SIGTERM** directly and drains + saves before exit. Idle shutdowns were never affected.
- **The admin page answers immediately** instead of hanging for the whole stop: both stop endpoints
  run the container stop off the request path. And for an instance that will not go down there is a
  new, clearly-labelled emergency **kill** button (no drain, no save — the lever of last resort).

### 💬 Chat help that speaks your language (#507)
- **`/help` no longer floods the chat with what looked like broken HTML.** Placeholders lost their
  angle brackets (`/give Gegenstand [Anzahl]` instead of `/give <item> [count]`, DE + EN), the player
  help is two short lines again, and the 509-character admin wall moved behind **`/help admin`**,
  split into readable grouped lines. Ten hardcoded English `usage:` lines became proper DE + EN
  locale strings.
- **Closed on the way: a chat rich-text hole.** Any player could type `<color=…>` or `<size=200>`
  and recolour or blow up everyone's scrollback — every line the chat log shows is now sanitized.

### 🛠️ The in-game content editors match the game again (#508–#514, #516, #517, #520)
- The Material and Item & Recipe editors had drifted from the game's data model: the crafting-station
  picker offered two stations that **do not exist** (exporting them made the game refuse to start)
  while three real ones were unreachable — it is now derived from the game's own station list. Ore
  depth bands reach the true 2 048 blocks, a live readout shows the share of rock a vein actually
  claims, materials gained the palette category and dyeable/shapeable flags, items gained their
  missing stats (area mining, cooldown, scan-knowledge multiplier, vendor theme), and typed item
  keys are checked against the live registry before export instead of failing later at startup.
- **The merge pipeline behind the editors was broken outright**: the recipe merge tool crashed on the
  game's own shipped recipe file (legal `//` comments), and both tools rewrote whole data files —
  merging one material produced a 5 000-line reformat. A new splice-based tool now lands each merge
  as a 1–3 line diff.

### 🔧 Under the hood
- A faulted browser-gateway request now gets an honest `500` instead of a torn-down socket, and the
  gateway no longer waits out a ~2-minute idle sweep on shutdown — which also means a stopping hosted
  world's process exits promptly instead of being parked by an idle browser connection. (#536)
- CI got a broad wall-clock pass: platform-correct Unity Library caches (the Windows build was
  restoring the WebGL cache), a saved WebGL cache on the release critical path, and the 10 GB cache
  budget reclaimed. (#528–#533)

## [0.9.0] — 2026-07-27

The frontier release: space got wilder. Every world now has its own face, seas and volcanoes are real,
bandits roam the frontier — and the family running the servers got real oversight tools to keep
everyone safe out there.

> ⚠️ **Existing worlds change with this release.** The world generator was overhauled from the ground
> up, and terrain that no player has touched regenerates under the new rules — coastlines, caves, ore,
> rivers and snow lines will differ from what you remember. **Everything players built or mined is
> preserved** (player edits always override the generator), and each body's planet *type* is now pinned
> so it can never change again. This is a deliberate one-time cut for much better worlds ahead.

### 🌋 Every world is now its own world (#466–#481)
- **Per-body identity.** Two jungle planets used to be the same jungle twice. Now every celestial body
  seeds its own terrain, plant and creature rosters, settlements, ruins and colours — the map preview
  in the menu shows the actual world you will land on.
- **Real seas and coasts.** Sea level is derived from each world's actual terrain, so jungle, swamp,
  savanna and varied worlds finally have oceans (measured, not guessed: ~20–27 % water) — and ocean
  worlds roll how much land they keep, from island chains to near-endless water.
- **Ore you can actually find.** Cave and ore generation is calibrated against each world's real rock
  distribution — the old constants sat so deep in the noise tail that "there is no ore" was literally
  true. Veins now reach 2 048 blocks deep, starter veins near the surface pay out double, and deep
  caves below the lava table fill with molten rock.
- **Rivers, waterfalls, volcanoes.** Rivers get real headwaters and run 2–3 blocks deep, waterfalls
  actually fire (~200 on a highland world), and watery worlds grow basalt volcanoes with molten
  summit craters, vents and hot springs. Water meeting lava chills into the new **obsidian** block.
- **Cold peaks, warm valleys.** Temperature now drops with altitude: dithered snow caps and tree
  lines on highlands, cold-faded flora, and the HUD thermometer reads your position — rain in the
  valley can be snow on the summit.
- **Living-world fixes.** Fauna spawning reaches its intended population (the old cap starved it),
  lava creatures can finally spawn on melt pools, amphibians hold the water surface — and mined
  ruins, claimed wrecks and looted vaults **stay** mined, claimed and looted across re-entry instead
  of resurrecting (which also closes a free-ship exploit).

### 🏴‍☠️ Bandits: hold-ups on foot, raider camps, pirate ambushes in space (#504)
- **Lone robbers now roam some worlds.** Rarely, a scruffy figure with a red bandana walks straight
  up to you — and demands about a third of your two biggest stacks (never your tools). You get a
  real choice with a 25-second timer: **hand it over** and the robber keeps its word and leaves you
  alone for a long while, or **refuse** (or attack, or just stay silent) and it fights. Players with
  empty pockets are not worth the trouble and are simply left in peace. Killing a robber wins its
  loot — including anything you paid it earlier. New world option **"Bandits"** (Off/Rare/Normal/
  Frequent/Extreme, default Normal on survival worlds, Off on peaceful/family presets), live-editable.
- **Bandit camps.** Some worlds carry a small raider outpost — log huts behind a palisade around a
  campfire, guarded by melee and gunner bandits that attack on sight but never chase far from camp.
  The reward is their stash (better loot than ruins). Camps are yours to raze: the blocks are
  unprotected, a demolished camp **stays** demolished, and once every guard is down the camp is
  permanently cleared — the guards never respawn.
- **Pirate space.** About a quarter of all star systems are bandit country. In those, a raider ship
  may warp in mid-flight, close in, and hail you with a cargo demand (drawn from your inventory
  **and** hold) — the same pay-or-fight choice, while you keep flying. Pay and it warps out for
  good; refuse or open fire and it fights like any hostile. Bandit ships only ever appear when the
  rules let you shoot back (space combat on + ship weapons usable), so an unkillable extortionist
  can never happen. Destroying one returns your goods.
- **VEGA teaches the rules BEFORE the first hold-up.** The first time you enter a flagged sector or
  land on a bandit world, the ship AI explains what bandits want and that paying is a safe, valid
  choice — afterwards you get short "pirate activity flagged" warnings on entry, always before any
  bandit shows up.

### 🛡️ Moderation that explains itself
- **A blocked player is told what happened, at the next sign-in.** Until now a banned account signed
  in completely normally — the world list loaded, everything looked fine, and the wall only appeared
  at *create world* or *join*, with no way to find out why. The sign-in (and a poll behind it, since
  a ban can land while someone stays signed in for weeks) now carries the state, and the game client
  and the portal show a proper screen: the reason, since when, until when, and what to do if you
  think it is a mistake. (#496)
- **Bans can be timeouts.** The admin panel offers 1 / 3 / 7 / 30 days or "until lifted", with the
  3-day timeout as the default — it ends by itself, so nobody has to remember to lift it, and the
  player can be told the day they are welcome back. Alongside the operator's own words there is now
  a canned reason (chat, griefing, cheating, name, other) that the player reads in their own
  language. A ban also ends the sessions that are running right now instead of letting the offender
  play on until they feel like logging off. (#496)
- **A deleted world no longer just vanishes.** When an operator deletes someone's world, the owner
  gets a message with the world's name and the reason typed into the admin panel — previously the
  world row was gone and there was literally nothing left to explain it with. (#496)
- **World owners can block and kick players from their own world.** *Manage world → Manage players*
  (in the game and on the portal) lists everyone who has played there and lets the owner kick them
  out right now or block them for good — enforced at the join grant, so it holds for every client,
  and matching on the account, so a rename does not get around it. The rest of the game is untouched:
  this is the small hammer next to the operator's fleet-wide ban. World admins also get
  `/kick <player>` in chat. (#497)
- **The operator can never be banned or kicked** — not by a world owner's ban list, not by a kick, not
  even from the admin panel. Oversight of worlds where kids play must not be something anyone can switch
  off, and an operator locked out of their own fleet would have nobody left to lift it. (#496, #497)
- A kick now says the truth: if the player is not in the world at that moment (or the world is asleep),
  the owner reads *"the player is not in this world right now"* instead of a "kicked ✓" that never
  happened. The block itself always holds — it decides the next join. (#502)

### 🪐 Room to fly between the worlds
- **Planets and their moons no longer huddle together in space.** Out in the flight view, every moon
  used to sit glued to the surface of its planet, and the odd pair of planets came out nearly
  touching — the view kept a flat 8-unit gap between any two bodies, whether they were pebbles or gas
  giants, and that gap is less than the distance the ship itself is held at. The gap now grows with
  the bodies, so a moon rides at a real altitude above its planet and neighbouring worlds read as
  separate places you fly *between*. Measured across 2 400 generated systems the typical clear space
  between two bodies went from 8 to 21 units, and moon-to-planet from 8 to 22 — while the time to
  cruise out to the system's farthest body is unchanged. A moon of the world you launched from could
  also end up stuck *inside* it; it can't any more (#493).

### 🔍 The UI got readable (#482–#484)
- **VEGA speaks up.** The ship AI's dialogue panel and the scan-result panel were sized for a
  magnifying glass; both grew properly, and scan results arrive in your language instead of raw keys.
- **A real "HUD size" setting.** Settings gains a live stepper that scales the whole in-game HUD —
  change it and watch it apply, no restart. Scanned discoveries also land in a new Codex
  "Discoveries" chapter, with the species names captured at scan time (every world names its own
  species, so the name has to be remembered when it is known).

### 👁️ Family oversight: the operator can check on any world (#487–#490, #495)
For a game whose players are mostly kids, the family running the servers needs to be able to look
after them — without disturbing anyone's game.
- **Observer mode.** A *fleet admin* (the operator of the installation — not the same as a world's
  owner) can enter any world invisibly with `/spectate`: no avatar, no nameplate, no parked ship, no
  claimed landing pad, no player slot used, ignored by creatures and NPCs, invulnerable, flying
  freely through walls. Muted by default (`/say` to speak deliberately); block *removal* stays
  possible as the one in-world moderation lever, and every action is logged.
- **Finding things.** `/players` lists everyone the world knows — online and offline, with their last
  position and when they were last seen. `/builds` lists named bases, beacons, teleporter pads and
  stations with owners. `/goto` jumps to a player (even on another planet — the old `/tp` never
  could), to a named build, or to raw coordinates.
- **A world-detail page for the fleet panel.** Each world on `/admin` links to a page showing its
  players, structures and **build hotspots** — clusters of changed blocks that reveal a house built
  without any registered base — each with a ready-to-paste `/goto` line.
- **Who built this?** Block changes now record who last changed each cell and when ("last editor
  wins" — no history is kept, and the table gains zero extra rows). Old cells stay anonymous;
  attribution starts with this release.
- **Any world, safely.** The operator reaches private and password-protected worlds too — the worlds
  kids actually play on. The power is double-locked: it needs a developer account (registered only
  with a secret code) *and* the operator's player name, which is auto-reserved so nobody else can
  ever claim it. A normal account sees none of this — not even the world list.

### 🛰️ Fleet operations
- **The fleet admin panel can delete worlds.** Every row on `/admin` gets a folded-away `delete…`
  control: type the world's name (checked on the server — there is no undo), then either `delete`,
  which stops the instance and drops it from the registry but leaves its saves recoverable on disk,
  or `purge saves`, which erases them including the archived copy. Both now also remove the world's
  container object — instances run without `--rm`, and a deleted world never wakes again to clean up
  its own leftovers, so until today every deleted world left one behind for good. Arcade worlds show
  `reset…` instead, because the glitch.fun pool refills itself (and its replacement worlds are now
  numbered from the highest name in use, so a reset can no longer produce two "Glitch Arcade 3").
  Scriptable twin for bulk cleanup: `DELETE /api/admin/worlds/{id}[?purge=true]`.

### 🌍 Worlds portal
- **The portal links to the game's website.** The shared footer carries a "Game website ↗" link on
  every page of play.blocksbeyondthestars.de, and the landing page repeats it in a line under the
  headline where first-time visitors actually look — German pages to the German site, English pages
  to `/en`. Self-hosters can point both elsewhere or drop the link entirely
  (`BBS_WH_WEBSITE_URL` / `_EN`, `-` = off).

### ❄️ Cold worlds freeze over
- **Water on cold worlds now generates frozen.** Below the freeze line a sea, pond or river carries
  a solid ice sheet — one to four blocks thick, growing with the cold — and in the deep cold
  (ice-type worlds) the whole body freezes through to the seabed. The waterline is walkable: no more
  diving into open water on a −38 °C planet. On merely-cold worlds (tundra, high mountain lakes on
  temperate planets) liquid water survives **under** the sheet — mine through the hand-diggable ice
  to reach the water, the kelp and the fish below, and take an oxygen reserve: under a closed sheet
  you dig your way back out. Mined ice stacks and crafts into water by hand (2 ice → 1 water, as
  before), so frozen worlds feed an algae tank just fine, and ice remains placeable as a building
  block. Ships treat thickly frozen seas as solid ground and may land on them. Freeze edges are
  noise-dithered, so partially frozen lakes with open patches appear right at the freeze line.
  ⚠️ *One-time world change:* existing cold worlds freeze retroactively on next load (player-built
  blocks are untouched). (#501, closes #494)

## [0.8.7] — 2026-07-25

The homestead release: player bases and space stations get their first real life support. A new heal tank block heals, feeds and recharges everyone nearby, doubles as a settable home spawn point, and dying now lets you choose whether to wake up at your ship or at your base — plus the ship's console and lab stations finally do something, and glitch.fun visitors see Singleplayer first.

### 🏠 Your base becomes a home
- **New block: the heal tank** (workshop recipe behind a research blueprint). Placed in a base or a player-built station, it slowly heals and feeds every player within a few blocks and recharges the suit — the only way to refill suit energy away from the ship (a code comment had promised "only refills at a heal-tank" for months while no such block existed). Deliberately simple and robust: the placed block itself is the whole machine — no wiring, no fuel, nothing extra to persist. (#464, closes #460)
- **Press E on a placed heal tank to make it your home spawn.** The spot is stored per player, remembers which planet or station it is on and survives save/load — deliberately separate from the ship's medbay respawn point, which the game rewrites on every landing and jump (the reason it could never hold a base). (#464, closes #461)
- **Dying now asks where you want to wake up.** With a home spawn set, the death screen offers *"wake up at &lt;your base&gt;"* vs *"wake up at your ship"* — including a full world transition if you died on another planet (your ship re-homes with you), or a re-board if your home is a space station. Without a home spawn everything behaves exactly as before. Fail-safes everywhere: an unanswered choice falls back to the ship after ~30 s, and so does a home whose tank was mined or whose station no longer exists — you can never be stranded. (#464, closes #462)
- Fixed along the way: dying while boarded on a space station left you flagged as *"in station"* forever afterwards — permanent free life support and a hunger bar that never drained again until relog.

### 🛠️ The ship's console and lab finally do something
- The **console** and **lab** stations in ship interiors showed a "Press E" prompt with a raw untranslated key — and pressing E did nothing at all (a silent server no-op). E on the console now opens the Ship tab (status + repairs — the cockpit without the helm, so still no launching from a console), E on the lab opens the Tech/research tab, both prompts are properly named in German and English, and any future unknown station id logs itself instead of failing silently. (#464, closes #463)

### 🕹️ glitch.fun: Singleplayer first
- On play.glitch.fun the main menu now leads with **Singleplayer**, and the shared-worlds entry moved to second place as **"Multiplayer (Arcade)"** — the first live store feedback showed visitors didn't discover that an instant in-browser singleplayer exists at all. The swap is gated to the glitch context; native builds and the fleet's own /play menu are unchanged. (#458)

## [0.8.6] — 2026-07-22

The new-player release: this cycle went back to the first ten minutes — the exact stretch where Severin's second handwritten playtest kept getting stuck — and fixed the whole first-run loop, from the mission text that ran off-screen to the hunger bar that emptied too fast. It also makes the ship/station/settlement build editors genuinely usable in both languages, and clears the last see-through holes and washed-out panels off ship hulls.

### 🚀 The first ten minutes finally hold together
The six fixes below all come from Severin's second playtest and land squarely in the new-player loop. (#456, closes #450–#455)
- **The mission objective text is no longer clipped.** The description drew with no word-wrap inside a masked scroll box, so the tail — the actual "dig straight down for iron" hint — ran off-screen. It now wraps and scrolls to its real height, in both languages. (#450)
- **"Station not available" now tells you _which_ station.** Trying to cook meat used to fail with a hard-coded English line that never said you needed a **detoxifier**, not the workbench you were standing at. The server now sends a machine-readable token the client localizes and names the exact station (DE + EN). (#451)
- **Diggable ore is more common and easier to reach on every planet.** The per-world richness multiplier was raised, `iron_ore` was added to ice planets (where it was simply absent — an iron mission there was impossible), and the surface layer is now uneven: its thickness rolls between one block and the planet's full topsoil depth per column, so in the thin patches ore-bearing stone surfaces within a block or two and shallow digging is sometimes rewarded. (#452)
- **Hunger is gentler and now tiered like oxygen.** The flat, fast drain (a full bar in ~200 s) became an Off / Slow / Normal / Fast setting, with Normal draining over ~330 s. The world-options screen gets a proper four-step row (DE + EN); old `hunger=true/false` saves still load. (#453)
- **Consistent headroom under two-block-high ceilings.** The player capsule's skin width was left at Unity's default, leaving almost no clearance, and a step-up sweep would randomly punch your head into the ceiling ("sometimes I get stuck, sometimes not"). Flat two-high tunnels now pass reliably, while slabs, stairs and ramps step up exactly as before. (#454)
- **Crouch / sneak.** Hold Ctrl or C on the ground to shrink down, walk slower, and stop at ledges instead of walking off them — which also lets you lean out and place a bridging block against a ledge's side face. You stay crouched under a low ceiling instead of popping up through it. (#455)

### 🧱 Build palettes you can actually read
- The ship, station and settlement **build editors** shipped with a flat ~150-row material list sorted by internal English id and dotted with random hash-coloured swatches, and much of the ship editor's UI was hard-coded English. The palette is now grouped under localized section headers (markers and ship parts first, then block categories — building, terrain, ore, flora, light, door, machine), sorted by each block's **localized** name, and each entry shows the block's **real procedural atlas tile** as its icon — whose average colour also tints the 3D placement preview, so builds preview in true material colours. The ship and structure editors are fully localized (shape names, size tiers, markers, statuses), the Save button sits on its own full-width row so its label never gets clipped, and search fields have a proper placeholder. Roughly 75 new locale keys, and a content test now fails the build if any block category is missing a translation. (#446)

### 🩹 Ship hulls and crash telemetry
- **The last see-through holes in ship hulls are plugged.** A full hull cube next to a shaped block (slab, ramp, stairs, sphere) had its shared face culled while the shaped cell didn't fill the gap — a hole straight into the interior, visible in flight, on landed ships, in the paint preview and on other players' ships. Builders never saw it in the editor, only after launch. Raised **greeble** hull panels also rendered near-white from a zero-area texture footprint (the same defect fixed for bevels earlier); they now sample the hull's own texel and read as proper darker plating. (#448, closes #420)
- **Crash reports upload whole, and a broken Arcade says so.** Crash bodies were written straight into the live spool, so a reader could catch a half-written file and upload truncated JSON — reports are now written to a temp file and atomically moved into place. And if the minigame catalogue fails to load, the Arcade used to claim you had "no data fragments yet" (wrong, and it hid the real problem); it now shows a distinct "Games couldn't be loaded" screen instead. (#449, closes #425)

## [0.8.5] — 2026-07-19

The audit release: a full static bug-hunt swept the whole stack — client, server and the hosted-worlds fleet — and this release ships the fixes. The headline for players: the locked-cursor family of bugs is gone for good, world changes no longer leave ghosts behind, touch players can't get soft-locked anymore, and a series of exploits and denial-of-service holes on the server side is closed.

### 🖱️ One owner for the cursor — the locked-cursor bugs are gone
- **A dock or trade request arriving while the Tab menu was open used to permanently lock the cursor** — the accept/decline panel sat invisible under the crafting menu, the cursor never came back, and every T/K/U interaction was dead until restart. That worst case was fixed first with a targeted patch, and then the whole family was closed for good: instead of ~12 panels each writing the shared "menu open" flag and forcing the cursor lock on their own (last writer wins), a single arbiter now derives the cursor state **every frame** from the set of open panels and overlays. (#407, #413)
- The same rework fixes the whole checklist of cousins: pause-menu **Resume** re-locks the cursor again; **Alt-Tab** re-locks reliably in every mode, including space flight; a ship destroyed while its landing-pad chooser was open no longer leaves a dead map floating over the on-foot world; the Esc that closes a dialog can no longer *also* pop the quit prompt; Esc/Tab while typing in a search or name field just leaves the field instead of closing the menu; opening a menu while driving a speeder holds the hover position instead of dropping you; the planet map closes on Esc and won't open over the death prompt; and the crafting menu can no longer stack over trade/beacon/dock dialogs. The freeze during settle/teleport also stops draining suit energy. (#413)

### 👻 World changes no longer leave ghosts behind
- Landing, boarding a station, hyperjumping or entering a ship interior used to carry stale state along: the old world's **robots kept growling and firing** on peaceful destinations, ghost map markers and speeder prompts lingered, and **frozen remote players** stood around with live trade/dock prompts. A world change now clears every world-scoped entity list and remote avatar; the new world re-sends what actually exists there. (#412)
- **Ship hatches stopped ghosting for other players**: a launched ship's hatch used to float in place (and a freshly parked ship's hatch stayed missing) for everyone else until some other door toggled, because door registry changes were never re-broadcast. They are now. (#412)
- **The client stopped leaking ~20 MB+ per menu↔world cycle.** Unity never garbage-collects procedurally created textures, materials and meshes on its own — and this single-scene game never asked it to. Atlas textures, chunk materials, ship/speeder preview meshes and the static icon caches are now freed on world teardown, followed by a full unused-asset sweep. Especially relevant in the browser, where repeated world-hopping could run a tab out of memory. (#423)

### 📱 Touch & browser: no more soft-locks and dead ends
- **Naming a beacon or beam pad soft-locked touch players** (tablets, play.glitch.fun): the modal only closed on a physical Enter/Esc, and every on-screen touch control was hidden behind it — reload was the only way out. The dialog now has proper on-screen Confirm/Cancel buttons. (#408)
- **A failed content load now explains itself and offers Retry.** A malformed data file used to freeze the shell with no message; a failed WebGL content download left a dead menu showing raw `ui.*` keys forever. Both now raise a bilingual error overlay with a working Retry, and a mid-session re-load failure keeps the working in-memory content instead of tearing it down. (#422)
- **Browser builds finally ship crash telemetry**: the crash uploader ran on a thread pool that never executes on WebGL, so browsers reported nothing while the local spool grew forever. Uploads now use the browser-native path and the spool is capped. (#421)

### 🚀 Teleports actually stick now
- Admin teleports (`teleport_to_location`, `teleport_to_player`) and the ship-recall teleport were **silently reverted** a moment after arrival — the position change travelled on a channel the client's own movement stream immediately overwrote. They now use the same snap channel as the void-fall rescue, so the move is authoritative (and doesn't flash the death screen). (#414)
- The craftable **suit teleporter** gained its missing trigger: right-click the held item to recall to your ship — the server validates device, energy and cooldown, and rejections surface as toasts. (#414)

### 🛟 Failing loudly instead of silently
- **A failed singleplayer launch no longer strands you in a void.** When the bundled local server couldn't start — antivirus blocking the fresh EXE, a broken update, the port already taken — the client sailed on into an empty, chunk-less world with no explanation (exactly the first-run experience that makes people quietly uninstall). It now returns to the menu with a clear notice (including an antivirus hint), and a mistyped multiplayer address gets a proper "connection failed" instead of an endless loading veil. (#409)
- **A corrupt settings file can no longer destroy your identity.** A truncated `client_settings.json` used to silently reset everything *including the PlayerToken* that backs your claimed player name — leaving you permanently rejected under your own name. Saves are now atomic with a rolling backup, the token is additionally mirrored to its own file that settings writes never touch, and the menu tells you whether settings were restored from backup or reset. (#410)
- **A failed chunk mesh build is retried instead of becoming a permanent hole.** The off-thread mesher swallowed exceptions and never re-queued the chunk — leaving an un-meshed 16³ hole with no collider. Failures now re-dirty the chunk (bounded retries) and log the fault. (#421)

### 🛡️ Server: exploits closed, hardening everywhere
- **Item duplication via trade offers is fixed**: an offer listing the same item twice (with only one stack owned) validated per entry and paid out per entry — a straight item-minting exploit. Offers are now merged per item id before validation, with overflow-safe sums. (#406)
- **Wrecked ships stay wrecked**: the "downed" state never survived a server restart, so any rejoin returned the wreck fully repaired and flight-ready for free. It now persists on every backend; old saves are unaffected. (#419)
- **The admin `/api` fails closed**: without an admin password on a non-loopback bind, every admin route (config, backups, missions, logs) used to run *unauthenticated* — a password-less public Docker deploy was open to full takeover. Such deploys now answer 401 with a hint; local dashboards keep working; the public portal/download pages are unaffected. (#411)
- **Rate limits can no longer be bypassed with a forged `X-Forwarded-For`**: the fleet's world host trusted the header from *anyone*, letting a single machine mint fresh rate-limit buckets per request (unlimited signup floods, login brute force). Only configured proxies are trusted now (`BBS_WH_TRUSTED_PROXIES`, sane private-range default), and logins get a per-account backoff on failures — the real owner with the right password is never locked out. (#418)
- **Protocol and transport hardening** (#424): re-sent join requests are dropped instead of rolling the player back to their last autosave and re-running the expensive join burst; joins pass a per-connection token bucket; browser WebSocket connections get a cap and a handshake window (no more slow-loris socket hoarding); incoming MessagePack decodes with untrusted-data limits; a hung Docker daemon can no longer wedge the fleet's join path; and every secret comparison (admin tokens, report keys, server passwords) is fixed-time.
- **Fleet resilience** (#415, #416, #417, #426): the world reaper and the hourly archive sweep no longer race a world that is just waking up (which could SIGKILL a live container or move its database out from under it); one bad request can no longer kill the browser gateway's accept loop (previously a silent browser-only outage until restart); two unbounded server-side caches now prune themselves; ship **docking now requires same world + proximity on both request and accept** (it's guest access, and range was never checked server-side); and per-connection socket resources are reliably released.

### 📸 Also in this release
- All 13 English marketing screenshots were regenerated against the current engine, and the unattended capture pipeline that produces them was repaired. (#405)

## [0.8.4] — 2026-07-18

The visual-fidelity release: a real twilight, name-tagged ships, and an end to the "black silhouette / see-through floor" rendering bugs — plus the Medium-preset ambient-occlusion work and the browser-feedback fixes from this cycle.

### 🌗 Day & night now flow through a real twilight / golden hour
- Crossing the day/night terminator on a planet — or just standing still while the local day advances — used to **snap** almost instantly to a dark, starry sky, with no visible dusk or dawn. `Sky.cs` now computes a smoothstepped twilight weight that peaks with the sun on the horizon: a warm golden-hour horizon glow (which the distance fog inherits) plus a warm amber cast on the block light, sun disc and god-rays at low sun angles. Because it's driven by sun **height**, the dusk length scales with each planet's `DayLengthSeconds` on its own — long day → long lazy dusk, short day → quick one, no per-planet tuning. Stars now **rise as the sun sinks past the horizon** instead of popping on a still-bright sky. Airless / space-sky bodies keep a hard terminator (no atmosphere → no scattering), which also sets those worlds apart. Client-visual only. (#393)

### 🏷️ Ships now show their pilot's name in flight
- Other pilots in a space instance were rendered as ships / EVA suits but carried **no name label**, so you couldn't tell who was who. A floating nameplate now rides over each ship or suit via the shared screen-label layer — the same path as the ground remote-players, with a radar-scale distance fade (90 → 140 m). Synthetic NPC-trader poses have empty names and get no plate. Client-only; the name was already on the wire. (#386)

### 🎨 A sweep of rendering fixes — colour is back
- **Creatures no longer render as flat black silhouettes** — wild or tamed, flying or ground. The species colour arrived correct on the client but was crushed to ~15% of its true value by a stack of multipliers (grayscale hide-tile × the 0.35 ambient floor × the 0.6 asleep-dim, worst in linear space). `LitColor` gains per-material `_Floor` / `_Fill` (defaults leave every other user — avatars, doors, ships, stations, held items — byte-identical), only `CreatureBuilder` raises them, and creature hide tiles are lifted toward near-white on load (they're multiply-tint detail maps, not dark albedo). Detail is preserved; only shadows rise. (#396)
- **Sky bodies no longer render as black discs by day.** From a planet surface the sun and every visible body share the upper hemisphere, so the camera sees the body's unlit far side ("new moon") — a dark disc against the bright day sky. A new `_DayLight` blend in `SkyBodyPhase.shader`, driven by the day factor, lifts back-lit bodies toward a fully front-lit disc by day (limb darkening keeps their round depth); at night and twilight the true crescent/phase is preserved. Defaults to 0, so space view and the menu background are unchanged. (#399)
- **Flora no longer shows a see-through "transparent floor".** The ground under cross-plant flora and at the base of solid flora (puffballs, cacti, crystal domes) showed a hole to the sky. The mesher was culling the ground-top face because `IsTransparent` lists only glass/fluids/fields — not flora/foliage — so the sealed face left a gap the thin billboard couldn't cover. Flora and foliage neighbours are now treated as non-occluding, mirroring the AO occluder exactly; tree crowns stay a thin shell. Render-only; verified across biomes with a regression test. (#382)
- **Edge-bevel corners stopped rendering white.** The render-only T0 edge bevel drew white triangles at exposed convex corners because every chamfer vertex shared one zero-area UV (`uv.center`), which samples a near-white cross-tile average on the mipmapped, gutter-less block atlas. Each bevel vertex now gets a real per-vertex UV projected onto the block's own tile, so the chamfer samples the block's own, correctly-lit texel. (#382)

### 🌅 Distant terrain no longer pops in at the horizon
- **The horizon stopped popping.** Distant terrain used to appear abruptly at the view edge because the fog ended *past* the streamed terrain (up to 1.6× the view radius) and the haze was capped at 60–75% opacity — so the outermost chunks were always partly visible when they streamed in. The fog now saturates fully at the view edge, and the server streams **one extra chunk ring beyond the fog line**, so the last ring is already loaded-and-hazed and simply fades in as you walk instead of materializing from nothing. (#388)
- **The world no longer assembles in front of you after loading.** The loading screen used to lift as soon as the single chunk under your feet had loaded, while the rest of the view was still streaming and meshing — so you watched the landscape build itself for a few seconds. It now holds until the streamed view has actually finished arriving and meshing (with the same hard time cap so it can never feel stuck). (#390)

### 🤖 Walking ground robots actually spawn now
- The planet Guardian machines come in two variants — a flying scan-drone and a walking three-eyed ground robot — but only drones ever appeared in playtests. The spawn mix keyed off the raw live count while the spawn guard only fires below the cap, so at the default cap the count was always 0 or 1 at decision time and every slot filled as a drone; robots only showed at Frequent/Extreme or with 2+ players. The mix now keys off how many drones are already alive, guaranteeing a walking robot even at cap 2 (steady state: 1 drone + 1 robot) while still converging on the intended ~2-in-5 drone ratio at larger caps. Server-only. (#398)

### 💾 Singleplayer quit saves your live position
- Native singleplayer quit hard-killed the bundled server before its graceful drain + save could run, so everything since the last 5-minute autosave was lost — and on reload you reappeared at the last **durable** checkpoint (often the landing pad at the ship's heal-tank), a few blocks above your ship, falling onto the hull. Quit now closes the server's stdin and the server drains + saves your live position on the tick thread (up to a 5 s wait) before exit, mirroring the existing SIGINT → save path; it falls back to a hard kill only if the server wedges. Flag-guarded, so dedicated/docker hosts are untouched, and WebGL (in-process) is unaffected. (#401)

### ⚡ Medium preset: ambient occlusion at half the cost
- Itemised the Medium preset's GPU cost on the reference laptop (Ryzen 9 7940HS / RTX 2000 Ada) with a new per-feature probe: **SSAO was the entire Low→Medium frame-time cliff** — everything else Medium switches on (depth/opaque copies, SMAA, ground scatter) sat within measurement noise. Medium now runs **SSAO at half resolution**: it keeps ambient occlusion but roughly halves the pass cost (~2.8 ms vs ~4–7 ms full-res across runs), recovering a few milliseconds per frame. **High is unchanged** (full-res SSAO). (#374)
- The main-light shadowmap also drops 4096 → 2048 below High — free on the reference GPU, a small win on weaker ones; High keeps 4096. (#374)
- The PerfProbe harness gained per-feature toggles (`-perfFeature ssao=off|half|full,depth=off,smaa=off,scatter=off,shadowmap=…,shadowdist=…`) so one preset feature's cost can be isolated in a single thermal session, plus an optional dense night scene (`-perfDense`: Extreme creature abundance at forced midnight) to measure the glowing-entity light cost for a future pass. (#374)
- Hosted worlds now cap chunk streaming — and the first-visit worldgen that happens inside it — at a per-tick wall-clock budget (default **25 ms**), so one player's fresh join or fast flight over new terrain can no longer stall the shared tick for everyone else in that world. At least one chunk always streams; the rest resume the next tick, nearest-first. Tunable via `BBS_WH_CHUNK_STREAM_BUDGET_MS` (0 = off); dedicated servers can set it directly with `--chunk-stream-budget-ms` / `BBS_CHUNK_STREAM_BUDGET_MS`. (#360)

### 🐛 Reports show the right version
- Bug reports and feedback in the report inbox showed the game version `1.0.0` for reports that came in through the server (a `/bump`, its feedback forward, or a server crash), because the dedicated-server build never stamped its version and defaulted to `1.0.0`. The server build now bakes in the release version, and a `/bump` additionally carries the reporting player's own client build so the inbox shows the player's version rather than the server's. (#389)
- The server-side `/bump` forward now also carries the **screenshot** into the report inbox (top-level base64 node matching the F1 wire contract), so a graphics-feedback bump no longer arrives image-less — a reliable path for the picture even on older/native builds where the client-direct upload may not run. (#381)

### 📮 Browser feedback fixes
- **Feedback in the browser no longer needs F1** — F1 opens the browser's own help, so the in-game feedback dialog now opens with **F2** in WebGL builds (F1 stays on desktop). The HUD and flight hints show the right key per platform. (#376)
- **Raw `ui.*` keys no longer stick in the browser UI.** On WebGL the locale files load asynchronously, so a screen built during that window (e.g. the main menu) could show untranslated resource keys and never refresh. Cached shell screens are now rebuilt once the language finishes loading, and the missing `ui.feedback.send` label was added. (#377)
- **Browser feedback actually reaches the inbox now.** The WebGL build was uploading an empty `{}` body (IL2CPP stripped the report type's metadata), so reports were silently dropped while the game said "queued". The type is now preserved, and a server rejection shows a real error instead of a false "saved, will retry". (#378)
- **The two admin inboxes now point at each other** — the fleet admin and the ReportHost admin each note what the other one holds, so in-game feedback isn't hunted for on the wrong page. (#379)

## [0.8.3] — 2026-07-17

The performance release: a measured pass over the whole stack — meshing, physics, graphics presets and the network wire. Every claim below comes from automated before/after captures on the same reference laptop (Ryzen 9 7940HS / RTX 2000 Ada); the tool that produced them ships in this release too.

### ⚡ Exploring no longer stutters
- **Chunk meshing stopped allocating.** Building a chunk's mesh used to create fresh geometry buffers every time — the garbage collector ran ~25 times per second during a walk on High, each one a small stop-the-world pause. The buffers are now pooled and reused (~1 collection/s), and the worst frame in a full minute of walking dropped from 61 ms to 26 ms — **zero frames above 33 ms remain**. (#368)
- **The collision mesh is greedy-merged:** coplanar block faces collapse into large rectangles (collision-only — the rendered world is untouched, and collision behaves identically). Far fewer triangles to cook means cheaper physics baking on desktop and a lighter single-threaded collider build in the browser. (#369)
- The HUD stopped rebuilding its texts every frame, and the entity views stopped allocating per frame. (#365)
- Net effect on High/VD8: **41 → 48 FPS** in the walking benchmark, with the hitches gone entirely.

### 🎚️ Quality presets that actually scale (Low: +35%)
- MSAA 4x and HDR were silently on for **every** preset — Potato and the browser included. Now they scale: MSAA off on Potato/Low, 2x on Medium, 4x on High; HDR off on Potato; Potato additionally renders the 3D view at 75% resolution while the UI stays pixel-crisp. (#370)
- The holographic visor HUD rendered a second full-screen camera into a texture every frame — on every preset. That pipeline now only runs on Medium+ with visor effects enabled; below that the HUD is the same clean flat overlay it always falls back to. Flipping the setting or preset in the pause menu switches live. (#370)
- Measured on the Low preset: **72 → 97 FPS** in the same walk, zero hitch frames.

### 🌐 Browser: world data ~20x smaller on the wire
- Terrain chunks now travel **run-length encoded**: the browser's JSON wire path shrinks from ~15–25 KB per chunk to usually a few hundred bytes, and an initial view fill drops from several megabytes to a fraction — faster arcade joins on glitch.fun, a faster first picture in browser singleplayer, and lighter server egress. Native clients get smaller payloads too. The server always sends whichever encoding is smaller, so worst-case terrain costs nothing extra. (#371)
- Browser singleplayer already streams world data under a per-frame time budget, and phone/tablet browsers render at a capped pixel density since this cycle's quick-wins round. (#365)
- ⚠️ **Compatibility:** the network protocol version moves to 2. Updated servers reject older game versions at join with a clear "please update" message — the desktop launcher updates automatically, and browser players always run the latest build. Singleplayer is unaffected (client and bundled server always match).

### 📮 Singleplayer feedback finally reaches us
- Crash reports from the bundled singleplayer server and the rich F1-feedback snapshots now flow to our own report inbox (release builds only — dev builds stay local), so "it broke in singleplayer" no longer disappears into a folder nobody sees. Your screenshot is only ever sent once, and the local file remains the source of truth. (#373)
- Fleet worlds forward the crash-report key to every world instance, so a crashed hosted world reports itself on its next start. (#364)

### 🔬 For the curious
- **PerfProbe** — the automated benchmark behind this release's numbers: `-perfProbe` boots a fixed-seed world, records a standing and a scripted-walk phase and writes frame-time/GC stats, so every future perf claim is one command away. (#367)
- The pull-request CI gate is back under five minutes. (#366)

## [0.8.2] — 2026-07-16

A small arcade-identity patch for glitch.fun, straight from the first post-launch live tests.

### 🕹️ glitch.fun: your arcade identity now survives updates
- Returning visitors were rejected with "The name '…' belongs to another player" — over their **own** name: the name-verification claim keyed on browser-local storage, which Glitch effectively resets with every deployment (each release is served from a fresh content path). The claim is now derived from the Glitch install itself, so the same install keeps its identity in every browser and across every update — no more lockouts after a release. (#345)
- The player-name field in the menu now actually does something on glitch.fun: the requested name is sent along to the arcade gateway (which keeps the stable 3-character install suffix, so custom names stay unique), instead of being silently ignored and overwritten. (#346)

## [0.8.1] — 2026-07-16

The glitch.fun edition's launch-day round: the first live tests turned up several arcade rough edges (all fixed here), and in-game feedback now works in the browser and flows to our own inbox.

### 📮 In-game feedback (F1): works in the browser, lands in our own inbox
- **The F1 feedback key now works in the WebGL player.** Browsers have neither the HTTP client nor the threads the old uploader used, so the browser build could never actually send feedback — it now posts through the proper browser path. (#342)
- **F1 works during space flight too** — it was silently blocked in the flight view; the dialog also restores your cursor cleanly on close (matters for the landing-pad picker). (#342)
- **Feedback never gets lost:** a failed send is spooled locally and retried on your next launches (a handful of times, then quietly parked — never deleted), with a clear "saved, will retry" note. (#342)
- Under the hood, player feedback and crash reports now go to **our own report service** (`reports.blocksbeyondthestars.de`) instead of the old third-party endpoint. Already-installed builds keep using the old one for now. (#342)

### 🕹️ glitch.fun: arcade joins actually work now
- The arcade session gateway validated a visitor's install **before** registering it with Glitch — but Glitch's contract requires create-then-validate, so every fresh browser session was rejected with "could not be verified" and nobody could enter the arcade worlds. The gateway now follows the required call order (create/resume first, then validate), with a regression test pinning the sequence. (#334)
- **CORS knew the wrong address:** Glitch serves the actual game files from its S3 content bucket, not from `play.glitch.fun` — so the browser refused to even send the arcade requests. The bucket origin is now part of the default allowlist. (#336)
- **The arcade never sleeps:** the pool worlds now run permanently — woken at startup, never idle-exiting, re-woken automatically if one crashes — so a store visitor lands in a running world instead of waiting out a cold world generation. (`BBS_WH_GLITCH_KEEP_AWAKE=false` opts tight hosts out.) (#336)
- Fleet side (no download needed): the arcade gateway env passthrough was fixed in deployment, so the gateway is actually switched on. (#330)

### 🕹️ glitch.fun: pick your mode — arcade or singleplayer
- The page used to jump straight into the shared arcade world, so nobody ever learned that a full **browser singleplayer** exists. glitch.fun now lands on the menu with two clear choices: **Play Online (Arcade)** and **Singleplayer** — still one click to play, but a chosen one. (#340)
- That menu also stops offering the generic browser actions that made no sense on Glitch: **Play** used to dial a meaningless default host and drop the player into an empty, serverless void, and **"Connect to a server…"** suggested picking a server — never the player's job on Glitch. The arcade button requests a session properly and the manual picker is gone. (#331)

## [0.8.0] — 2026-07-15

The glitch.fun edition: Blocks Beyond the Stars becomes instantly playable on [glitch.fun](https://glitch.fun) — shared arcade worlds you join with one click, plus full singleplayer that runs entirely in your browser and follows you across devices via cloud saves. (#326)

### 🕹️ Arcade worlds on glitch.fun (#322)
- A small pool of persistent **multiplayer arcade worlds that exist only on glitch.fun**: one click on the store and you are in — no account, no world creation, no password. The platform's `install_id` is validated server-side, you get a stable guest identity, and a sleeping world wakes on demand.
- These worlds never appear on our own portal (not in the public browser, not in any world list) — **the Baumhaus rule for our own platform is unchanged**: your worlds stay password + word-of-mouth only. The arcade is a separate playground under Glitch's platform accounts and rules; a devblog explains the amendment.
- Moderation without accounts: the operator can ban a Glitch install — banned installs get no new sessions and are live-kicked from a running world.

### 🌍 Singleplayer in the browser (#323)
- The **real authoritative game server now runs inside the WebGL page** — the same simulation the fleet and desktop run, pumped over an in-memory loopback instead of sockets. The browser menu has its Singleplayer button back, and `?singleplayer=1` deep-links straight into your world.
- One persistent world per browser: saved automatically every two minutes, when the tab goes to the background, and on exit — into the browser's own storage, as a compact snapshot of the whole world.
- Under the hood this retargets the server simulation to a Unity-consumable library (desktop singleplayer keeps its bundled server), adds a fully managed save backend (no native SQLite in WASM), and switches the in-browser wire to the JSON envelope — MessagePack's runtime formatters don't run under IL2CPP.

### ☁️ Cloud saves for logged-in Glitch players (#324)
- Playing on glitch.fun with a Glitch account? Your singleplayer world syncs to **Glitch Cloud Saves** and follows you to any device. Conflicts use Glitch's explicit resolve flow (the live session wins; nothing is silently overwritten), and guests simply stay local.
- The sync runs through our WorldHost as a relay, so the Glitch API credential never ships inside the public browser build; uploads are size-capped and rate-limited.

### 🚀 Releases now ship to glitch.fun automatically (#325)
- Every tagged release mirrors its WebGL build to glitch.fun through the platform's deployments API — same gating as the itch.io mirror (full test suite first, skipped cleanly on forks). One build serves both our portal's `/play` and the Glitch store.
- Small fix on the way: the browser menu's *My Worlds* button now always points at our worlds portal (on glitch.fun it used to be a dead link) — the door from the arcade to creating your own world with friends.

### ✅ Verification
- The suite grows to **1127 tests**, including 31 for the glitch gateway/registry (guest tokens, capacity, bans, heartbeat + save relay, CORS) and 7 for the browser persistence path (full mine→save→reload round-trip on the real server + client). An automated real-browser smoke test drives the in-browser world end-to-end: boot, join, durable save. (#326)

### ⚠️ Known limitations
- In-game player reports need a portal session and are unavailable for arcade guests and browser singleplayer (operator recourse: install bans, world stop/reset).
- The in-browser world runs with AI texts off (static lines) — the LLM backend is not reachable from a browser.

## [0.7.7] — 2026-07-14

A pure stability release: a full code audit of the game server and the Unity client turned up eight bugs — all fixed here, the risky ones with new regression tests. Highlights: your saves are now safe across future content updates, multi-world hosting got fairer and lighter, and long play sessions stop growing in memory. (#319)

### 💾 Saves are now safe across content updates
- Saved worlds stored every block as a numeric id derived from the sorted content list — adding a new block in a later update could shift those ids and silently decode your existing builds into the *wrong* blocks. Worlds now persist an id→block palette and remap all stored ids (block & structure edits, regrown flora, space structures) when the content set changes — atomically, on SQLite and PostgreSQL alike. (#318)

### 🛰️ Multi-world hosting: fairer, lighter, harder to freeze
- Presence updates, enemy sync and void-rescue checks each ran on a **single shared timer** for the whole server — with several occupied worlds, one world could starve all the others of those updates. Each occupied world now keeps its own cadence. (#315)
- A world now really unloads (and frees its memory) when its last player disconnects — before, the unload could silently do nothing and the world stayed resident. (#313)
- `/ai_mission` called the AI backend synchronously on the tick thread, so a slow LLM response froze the entire server for everyone. Mission generation now runs off-thread and the result is applied on a later tick. (#314)

### 🪐 Planets: flora variety restored
- The set of plants for a planet was resolved once — for the first planet visited after server start — and then reused everywhere, so every world grew that first planet's flora. Each planet now resolves its own subset, deterministic from the seed again. (#317)

### 🧠 Client: mesh memory leaks plugged
- Unity never frees procedurally-built meshes on its own — chunk re-meshing, chunk unload, world reset and ship/asteroid re-meshing each leaked their outgoing render/collision meshes. All of these now destroy the replaced meshes, so long sessions no longer creep up in memory. (#316)

### 🤝 Trading: the right cargo on both sides (security fix)
- Player-to-player trades (and the admin `give_item` command) now validate and swap each partner's items against that player's **own** ship cargo instead of the last ship the server happened to look at — an offer made while aboard someone else's ship no longer resolves against the wrong hold. (#319)
- **Security note** *(disclosed only after the official fleet was patched)*: the wrong-cargo lookup was player-reproducible and could be abused to **duplicate items** in multiplayer trades. The official hosted worlds already run v0.7.7 — if you host your own server, please update / pull the rebuilt Docker image promptly.

### ✅ Verification
- The full suite is now **1116 tests** (998 server + 118 client), including 5 new regression tests: block-palette remap, per-partner trade cargo, per-planet flora determinism, and world unload on disconnect. (#319)

## [0.7.6] — 2026-07-12

A small under-the-hood release: the entire server-side stack moves to .NET 10 LTS (with a security fix included), and a subtle singleplayer start-world bug is gone for good. (#307, #309)

### ⚙️ .NET 10 LTS (server stack)
- The game server, web portal/API, world host, launcher and tooling now run on **.NET 10 LTS** (supported until November 2028) instead of .NET 8, whose support ends this November. (#309)
- **Security:** the upgrade surfaced a known high-severity vulnerability in the SQLite native library we were shipping ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)) — patched in this release.
- **No action needed for players** — the bundled singleplayer server and the launchers are self-contained and bring their own runtime. Self-hosters just pull the rebuilt Docker image; only local development now needs the .NET 10 SDK.

### 🪐 Singleplayer: start world can no longer get stuck on an old default
- The bundled singleplayer server used to read a `config/server.json` it once wrote next to its exe — a leftover file from an older version could silently pin outdated defaults, most visibly the start planet staying the old **rocky** world instead of the varied start worlds introduced in v0.7.x. The singleplayer host now always starts from current defaults and ignores any stale config. (#307)
- Official installers were never affected — this hardens portable-zip-overwrite setups and local builds. Dedicated servers are unchanged and keep their editable `config/server.json`.

### 📖 Docs
- The README now spells out the hosted-worlds friend flow up front: create your own world → set a join password → list it publicly so friends can find and join it. (#305)

## [0.7.5] — 2026-07-10

A small onboarding-and-trust release: newcomers now get the hosted-worlds model explained where they actually look, and Windows builds are moving to free, verified code signing. (#301, #302)

### 🌐 "Official Worlds," explained for first-timers
The game has **no open public servers** — everyone creates their own world, sets a join password, and lists it publicly so friends can find it. That rule was enforced everywhere but never spelled out for someone opening the menu for the first time. Now it is. (#301)
- **The signed-out Official Worlds screen** opens with a one-line explainer of how online play works: create your own world → set a join password → list it publicly → friends join with the password.
- **The public worlds list** now shows the note that was written for it but never displayed — these are worlds shared by other players, and you need the creator's password to join.
- **Creating a world** shows a friends hint right in the dialog: set a join password now, then list the world publicly under *Manage* so friends can find and join it.
- **The web portal landing page** gains a *"So funktioniert's / How it works"* card with the three steps and the no-open-servers rule, instead of just a one-line tagline before you log in.
- **The README and user manual** now state the model and the friends-join path up front.

### 🔒 Windows code signing (SignPath)
- Windows installers are moving to **free, verified code signing** through the [SignPath Foundation](https://signpath.org)'s open-source program, so Microsoft Defender SmartScreen stops flagging them as coming from an "unknown publisher." A new **CODE_SIGNING.md** documents exactly what is signed, from where, and by whom; the README security notice and SECURITY.md link to it. (#302)
- **During onboarding:** while the certificate is still being provisioned, some builds may remain unsigned and SmartScreen may still warn — the notice explains the one-time *More info → Run anyway* step until signing is fully live.

### 📓 Devblog
- We keep a **development blog** ([blocksbeyondthestars.com/blog](https://www.blocksbeyondthestars.com/en/blog)) with the story behind the game — it now has its own link in the README header row.

## [0.7.4] — 2026-07-08

A round of new-player and comfort fixes from a hands-on playtest by **Severin**, plus Official Worlds & portal polish. (#289, #290, #298, #299)

### 🎮 Comfort & controls
- **Pause menu on Esc** — pressing **Esc** in-game now opens a small pause menu (**Resume / Settings / Quit to main menu**) instead of jumping straight to a "leave the game?" prompt. You can finally reach **Settings — and the volume — without leaving your world**, and volume changes are audible immediately. (#291)
- **Settings apply live in a running world** — language now switches the live HUD/chat instantly (the world previously kept its own snapshot until re-entry), quality preset / VSync / FPS cap / lens flare / motion blur / volumetric fog / mouse sensitivity / invert-Y push straight to the running world, and the view-distance row honestly says it takes effect at the next world start (the server streams the radius it was started with). (#291)
- **Settings screen readability** — the window (main menu and in-game, it's the same screen) is wider (600 → 900), every label/control lines up on one shared column pair, and the section headers are now real headers: bold, uppercase, with a divider line and breathing room, so the long list finally scans. (#291)
- **Mouse no longer escapes after Alt-Tab** — tabbing out and back used to leave the cursor free to drift or click outside the window; the game now re-locks it on focus return during normal play. (#292)

### 🌊 Survival tweaks
- **Shallow water breaks your fall** — landing in even a single block of water now cushions the drop (like Minecraft), instead of only saving you when you were chest-deep. (#293)
- **Oxygen lasts longer** — suit-oxygen drain has been softened again across all rates (at Normal a full tank now lasts ~285 s on foot). (#294)

### 🧭 New-player guidance
- **The oxygen mechanic is finally visible** — on a breathable world the vitals bar now reads **"Oxygen (breathable)"** so you understand why it never drains here, and a one-time advisor line explains that space, water and toxic worlds will empty your suit tank. (#294)
- **Finding iron** — the starter mission and the mining tutorial now tell you iron sits just a few blocks underground, so "where's the iron?" has an answer. Ores in general — and iron in particular — are also a bit more common now. (#295)
- **The first minutes stay yours** — the guaranteed starter data cube no longer glows right beside your ship; it now sits a short walk off the landing pad (~20 blocks), so new players aren't pulled into a minigame before they've even looked around. (#296)

### 🙌 Credits
- **Severin** is credited as a playtester (README + in-game credits) for the feedback behind all of the above. (#297)

### 🖱️ Official Worlds & portal UX polish
Follow-up polish to the hosted-worlds flow (portal + in-game *Official Worlds* menu). (#289, #290)
- **Loading spinners** everywhere an action runs in the background — waking/playing, creating, stopping, deleting and uploading a save. The web portal now also gives stopping and deleting a progress state (button disabled + "Stopping…/Deleting…" → "stopped/deleted"); before, those looked like nothing happened until the list refreshed.
- **Play → clearer name handling in the game client**: if the chosen player name is reserved or not allowed, an in-place prompt lets you pick another name and retries the join, instead of a dead-end error you could only fix from the main menu.
- **"Play" (portal)** reads a prefilled, remembered player-name field instead of a pop-up prompt; the browser-play button is the clear primary action with the native host/port/token in an expander.
- **Official Worlds window** widened and laid out as a clean grid: the five action buttons share one uniform row, the per-world Play/Manage buttons line up with the columns above them, and the world **status** ("starting…", "running") gets its own wide column so it never hides behind the buttons.
- **One window for both world lists (game client)**: public worlds no longer open a second window. The *Official Worlds* screen now shows your own worlds and the public ones in a single scrollable view, split into clearly labelled **"My worlds"** and **"Public worlds"** sections (own worlds are filtered out of the public list).

## [0.7.3] — 2026-07-07

Find and join worlds other players are running, plus a round of interface fixes. (#287)

### 🌍 Public world browser
Hosted worlds used to be invite-only — there was no way to discover a world someone else made; you needed a link or token from its owner. Now there's an opt-in public list.
- Owners can list a world publicly with a per-world toggle — in the portal **and** in the game's **Official Worlds** menu — but **only once the world has a join password**, so a listed world is never wide open. Worlds stay private by default, and removing the password automatically un-lists the world.
- A new **"Public worlds"** section (portal card + in-game browse dialog) shows everyone's listed worlds; joining still asks for the owner-shared password, so discovery never means free entry.

### 🖥️ Interface fixes
- **Credits screen** — the contributor text now scrolls inside its panel (with a scrollbar) instead of spilling out the bottom and being covered by the Back button.
- **"Create world" (portal)** — the button now shows a "Creating…" → "Created 🚀" state and refreshes the list itself, so it no longer looks like nothing happened until you reload the page.
- **"Play" (portal)** — reads a prefilled, remembered player-name field instead of a pop-up prompt; clearer "waking…/ready" messages, with the browser-play button as the obvious primary action and the native host/port/token tucked into an expander.
- **Official Worlds** — the "View rules" button no longer crowds the panel's edge.

## [0.7.2] — 2026-07-06

Play the whole hosted-worlds flow without ever opening a browser, grow your own food, pen in your creatures, get treasure hints from villagers, and start new games somewhere friendlier — plus a round of UI polish.

### 🖥️ Play without a browser — full client-portal parity
Everything you could previously only do on the web portal now works inside the game's **Official Worlds** menu; for desktop players the website is optional. (#271, closes #268–#270, #272)
- **Sign up in-game** with the community rules shown and accepted right there. A new anonymous `GET /api/terms` single-sources the rules text with the `/rules` page (via a new `CommunityRules` class) so they can never drift, and an outdated-terms login opens a **re-accept flow**.
- **Create a world** (name + optional join password with a repeat check) and a **per-world manage dialog**: set/remove the join password, stop the world, or delete it behind a type-the-name confirmation.
- **Save backup round-trip without a browser** — download to / upload from `portal_saves/<worldId>-world.db` with an "Open folder" button (world must be stopped; the 50 MB cap surfaces server-side).
- **Feedback & ideas** form and **GDPR account deletion** (type-the-name confirm; erases the account, its worlds and saves) are both reachable in-game.
- **WebGL round-trip** — the browser client gains a visible "My Worlds / Account" button linking back to the worlds portal on the same origin (#272). It still never selects servers itself, so no portal features are duplicated in WebGL.
- **UI polish** — modal dialogs are now fully opaque everywhere (forms no longer shine through), the settings list scrolls, overlay notices wrap instead of clipping, the Play/Manage row buttons are wide enough for their labels, signup shows a proper "Password (at least 8 characters)" label, and the rules screen shows a friendly offline message + retry instead of silently dropping "accept" when the terms can't be reached.

### 🌱 Algae tank — grow food from water at a base
- New **algae tank** station block, the game's first food-producing machine (detoxifier station-recipe pattern, base-only). (#265, closes #261)
- Crafted at the workshop (2 metal panel + 3 glass, no blueprint); turns **1 water → 2 algae rations** (+30 hunger). A new hand recipe (2 ice → 1 water) feeds it.
- Dedicated bubbling craft cue, new block tile + ration icon, and full bilingual Codex coverage (survival-guide article + auto chapters).

### 🐾 Energy fence & gate — pen in creatures, companions and enemies
Wild creatures, companions and planet enemies never consult the voxel world for collision, so ordinary walls can't hold them. Two new blocks change that. (#266, closes #263)
- **`energy_fence`** — a solid emissive pylon that blocks players, NPCs *and* all fauna (workshop: 2 metal panel + 2 cable → 4).
- **`energy_gate`** — a non-solid membrane players and NPCs walk through but fauna bounce off; a door with no open/close state (workshop: 2 metal panel + 1 energy cell + 1 circuit board → 1).
- A new fauna fence sweep wires this into creature, companion and enemy movement — so a pen holds your own animals and the fence doubles as base defense. Flying creatures glide over normal-height fences (documented).
- New atlas tiles for both blocks plus an `energy_fence_hum` loop; DE+EN locales, taming wiki article and manual sections.

### 🗺️ NPC treasure hints
Settlement NPCs can now point you toward the crashed wreck and hidden caches. (#267, closes #262)
- On greeting there's a **35% chance to emit a hint** instead of the flavour line: the wreck (while unclaimed) for everyone; the nearest unlooted chest only once your relationship reaches "known".
- The reveal is **world-global and co-op friendly** — the POI is added to the map and broadcast to all joined players, and persisted in world metadata (no codec change).
- The spoken line is deterministic and localized DE+EN: an 8-way compass direction + distance, torus-wrap aware. Claimed wrecks and looted chests drop out automatically and are never re-hinted.

### 🪨 Friendlier start planet
- New games now start on a **hospitable planet** (varied) instead of toxic, plantless rocky; carbon ore was added to varied so the medpack/energy-cell chain still works. Only new worlds are affected — existing saves keep their persisted start type. (#264, closes #260)
- World generation is hardened: an unknown or unmatched start type no longer crashes world load (it falls back to the first breathable planet with flora).
- Fixed a latent bug this surfaced: breathable-surface health regen ran before the same-tick death check, so a 0-HP player was nudged to 0.2 HP and **never respawned**. No regen at 0 HP now.

### 🌐 Portal polish & security
- **Discoverable DE/EN language switcher** — a header "DE | EN" pill (the old switcher was invisible grey footer links), plus `Accept-Language` detection on first visit. Auto-detection sets no cookie; only an explicit choice persists. (#259)
- Resolved both open CodeQL alerts: the `bbs_lang` cookie now sets `Secure` + `HttpOnly`, and the `/admin/announce` handler logs the world id through the safe helper. (#259)

### 📚 Docs
- README now links to the game's website. (#264)

## [0.7.1] — 2026-07-06

This release is all about **playing together safely**: world creators can password-protect their worlds, server restarts announce themselves with an in-game countdown, reporting and feedback are easier to find (especially for kids), the portal got a kid-friendly rework, and the Play button finally launches the game right in your browser.

### 🔒 World join passwords
World creators can lock a hosted world with a password — perfect for "just my friends" worlds. (#255)
- Set, change or remove the password any time in the portal; new worlds can get one at creation (with a confirm field so no typos lock you out).
- Joining asks for the password — in the portal and in the game client (masked input). The world owner never needs one.
- Guess-proof: 10 wrong tries in 15 minutes puts a cooldown on that world, and sleeping worlds aren't even woken for wrong guesses.

### 🔧 Maintenance announcements
Server restarts no longer come out of nowhere. (#255)
- In-game countdown banner when a maintenance restart is scheduled, plus an acknowledgeable dialog and a proper "server is restarting" screen instead of a generic disconnect.
- New commands `//restart <minutes>` and `//cancelrestart` for world admins, a token-gated `/announce` endpoint, and an **Announce card in `/admin`** with a per-world graceful-restart button.
- Worlds finish the countdown, save, and stop cleanly — no more surprise kicks.

### 🧒 Kid-friendly portal & feedback
The web portal now reads like the family project it is. (#258)
- New **"Feedback & ideas" card** on the worlds page — kids and parents can send wishes directly, without filing a "report" against anyone; feedback lands in its own `/admin` queue, separate from player reports.
- Parental notice on the landing page, and "ask your parents first" is now the first community rule.
- Kid UX pass: create-account card comes first, the name field speaks plain words, a "write your password down!" warning replaces jargon, and the join prompt remembers your player name.
- Reporting is discoverable: rules and portal now point at the in-game Report button and `/report` command first.

### 🛡️ Easier reporting in-game
- New **`/report <name>` chat command** on official worlds — it attaches the last 10 chat lines as evidence. (#248)
- Every report now records **which world it happened in** — from the button, the command and the portal form. (#248)
- `/help` now leads with the commands for everyone (`/report`, `/bump`), and official worlds show a one-time chat tip pointing at both report paths (DE+EN). (#258)

### 🌐 Play in the browser, for real
- The portal **Play button now launches the game directly in your browser** — signed in, world picked, one click, playing — by deep-linking the WebGL client with a short-lived join token. (#256)
- Fixed the Play button doing nothing for returning players (a permission wipe ate the grant). (#256)
- The whole portal is now fully bilingual (DE/EN, `?lang=en` or the toggle) and finally shows the game logo and a favicon. (#256)

### 🛠️ Fixes & polish
- **Stopping a server during initial world generation now works** — previously the stop was ignored and the container was force-killed (exit 137); the generation grace period is also longer now. (#247)
- Admin server-health card shows the real host core count, so the load bar means what it says.
- Hardened the admin stop endpoint against log forging (CodeQL): world ids are sanitized before logging.

## [0.7.0] — 2026-07-05

A big visual glow-up with the **organic look overhaul**, plus **gamepad and touch controls**, hosted **Official Worlds** with a web portal, a fleshed-out in-game **Codex**, and a **browser client** that's actually playable end-to-end.

### 🎨 Organic look overhaul
A full presentation pass — all client-side, no save/world migration needed. (#192)
- Softer voxel surfaces (per-vertex + cavity AO, beveled convex edges). (#186)
- GPU-instanced grass tufts and pebbles on open ground. (#187)
- 3D mesh flora — leaning 3-plane plant rosettes; cacti, crystals and mushrooms are real shapes. (#188)
- Finer building shapes: panel, post, beam, low ramp, quarter cube. (#189)
- Fully orientable shapes (all 24 orientations, auto-orient, rebindable rotate key). (#190)
- Sparse raised plating on exposed ship hull faces. (#191)

### 🎮 Gamepad & touch controls (experimental)
The whole input epic (#193) shipped — inert on desktop keyboard+mouse.
- Gamepad/controller support behind a new input-abstraction layer (flight, EVA, menus, building). (#199)
- Pad rebinding in Settings with device-aware button glyphs. (#206)
- On-screen touch controls with localized labels (DE+EN). (#204, #206)
- Text entry on touch in the browser; menus navigable by pad focus-nav. (#206)
- ⚠️ On-device feel still being playtested (#201–#203).

### 🌍 Hosted Official Worlds (beta)
Player-created worlds hosted by us, joinable in one click — no port forwarding, no server setup. (#227)
- "Official Worlds" in the main menu; sleeping worlds wake on demand and idle worlds shut down cleanly.
- Web portal with privacy-minimal sign-up (name + password, no email), world create/manage, and save upload/download.
- Kid-friendly bilingual community rules + a beta notice; **DSGVO account self-deletion**. (#233)
- One-tap in-game **Report** button + portal report form; operators can review and ban, enforced at join.
- Optional AI backend runs containerized in the fleet (internal-only, OpenAI-compatible) and feeds NPC chatter. (#234)
- Basic-Auth `/admin` panel: live fleet overview, report queue, ban/unban, server-health card; public aggregate-only `/api/stats`. (#234, #246)
- Per-world memory/CPU/pids fences + a global cap on awake worlds. (#234)
- Fixed a stale container blocking every re-wake, found in the first real end-to-end test. (#235)
- Developer names protected against impersonation + a blocked-name filter. (#230)
- Worlds untouched for months are archived, not deleted, and restored transparently on join. (#230)
- Rate limits on signups, logins, save uploads and reports + Prometheus metrics. (#230)
- Self-hosting unchanged — everything inert by default. Fleet deploys from the repo via an approval-gated SSH workflow. (#229, #231)

### 🐛 Self-owned bug-report inbox
- New **ReportHost** service: a small Docker container that receives F1 feedback and crash reports (unchanged wire contract), stores them in SQLite with screenshots, and offers a filterable triage UI. (#227)
- One-click JSON bulk export honoring the current filters. (#234)

### 📖 Codex
- Every block, item and all 19 worlds now have bilingual (EN/DE) descriptions, plus 14 new guide articles. (#184)
- Codex is fully navigable again: working chapter links, list descriptions, correct scan names. (#178)

### 🌐 Browser client
The experimental WebGL path is now genuinely playable.
- Fixed falling through terrain on landing — chunks and colliders now build synchronously on WebGL. (#205, #207)
- One-click `/play` flow polished (required name, deep-linking, dark loading template, reliable touch). (#217–#222)
- WebGL builds no longer drag in the desktop updater (Velopack); dedicated WebGL attach workflow. (#151)

### 🛠️ Fixes & polish
- You can walk out of your ship again (nozzles moved, doorway cleared, hatch raised to 3 blocks). (#179, #211, #212, #214, #215)
- Deep water reads see-through — vertical depth, softer reflections and glint. (#213, #216)
- Helmet lamp switches off when you leave a planet. (#180, #182)
- Opaque world-options dialog with non-overlapping footer buttons. (#181, #182, #209, #210)
- All 27 Unity compiler/build warnings resolved — the client builds warning-clean.
- Game-input hardening on every server: packet-size caps, per-connection rate limiting, validated face data, voice frame-rate cap, control characters stripped from chat/labels. (#233)
- CI: two-tier test gate — fast PR checks, full suite on main + before release. (#208)

## [0.6.2] — 2026-06-29

Sharpens core gameplay, adds oxygen-tank tiers, seals a station/space exploit, and lays the groundwork for playing in the browser.

### 🎮 Gameplay
- Shaped blocks now have proper icons in hotbar, crafting and inventory. (#125)
- Ladders are climbable (forward/Jump up, back/Ctrl down). (#126)
- Auto-step onto slabs & stairs without jumping. (#127)
- Tool-gated mining — stone needs a drill (the starter kit grants one); soft blocks stay bare-handed. (#128)

### 🫧 Survival
- Oxygen Tank tiers I/II/III — worn components giving +50/+100/+200 max oxygen; only your best tank counts, Tank III gated behind titanium. (#133)
- Easier to climb out of water. (#133)

### 🌐 Browser client (experimental)
- Hosted WebGL client + optional PostgreSQL backend, servable from the dedicated-server Docker image. Contributed by **[@ProdigyView](https://github.com/ProdigyView) (Devin Dixon)**. (#116, #123)
- Slimmed browser start menu to name + Play. (#122)
- ⚠️ Not yet verified end-to-end — ships as experimental.

### 🛠️ Fixes & polish
- Sealed a station-to-space gap via a bounded flood-fill enclosure check. (#134, #135)
- Dev builds isolated under a separate pack ID. (#115)
- German localization fixes — thanks to **[@Maqbool61](https://github.com/Maqbool61) (Maqbool Ahmed)**. (#112)

## [0.6.1] — 2026-06-28

A focused follow-up to v0.6.0: an experimental macOS build, automatic crash reports, and one-click client downloads from your own server.

- 🍎 **Experimental macOS build** (`StandaloneOSX`, portable zip) — unsigned/un-notarized, not yet tested on real hardware; testers wanted ([#87](https://github.com/marceld23/BlocksBeyondTheStars/issues/87)). (#84, #105)
- 🐧 Native Linux client (new in v0.6.0) gains a one-click download from the server portal. (#83)
- 🛡️ **Automatic crash reporting** (server + client, opt-in, with a PII scrubber); the server tick is hardened so one bad event can't crash the world. (#103)
- ⬇️ The dedicated-server portal now hands out Windows, Linux (AppImage) and macOS clients directly. (#107)
- 👨‍👩‍👦 README, Code of Conduct, in-game Credits and Codex now state the kid-friendly intent. (#109)
- ⚙️ Faster, cleaner release CI (version resolved once and shared; Docker build decoupled; overlapping runs serialized). (#106)

## [0.6.0] — 2026-06-27

Our biggest update yet: claim your own factory, explore alien ruins — and play natively on **Linux**.

- 🎉 **First community contribution** — native Linux support by **[@corarona](https://github.com/corarona) (Cora de la Mouche)**. (#69)
- 🏭 **Factories** — rare industrial halls with animated machine bays and a production terminal that turns cheap materials into bulk output.
- 🏚️ **Ruins** — collapsed settlements you can mine, plus standalone treasure chests with richer loot.
- 🔑 **SPS access codes** — a rare item that lets you claim a factory as your own editable base, shared with allies.
- 🐧 **Native Linux client** — real `StandaloneLinux64` build as AppImage + Portable.zip; the Windows-only embedded browser (CEF) was removed so the Codex Wiki and Arcade run without it.
- 🛠️ Deeper crafting: new **Transmuter** station, the **Refinery** becomes full metallurgy, and the titanium/carbide deadlock is fixed.
- 🌍 Bigger in-game editors, longer view distance, smoother movement, downhill rivers/lava with waterfalls, brighter worlds, per-world named tree species.
- 📚 New factory/ruins docs (manual + Codex, EN/DE) and a canonical source-code URL (AGPL).

## [0.5.0] — 2026-06-25

A graphics-quality pass and a licensing/foundation cleanup.

- 🎨 Pro-look graphics quick-wins: in-game post + SMAA + SSAO + emissive glow. (#50)
- 🎨 Per-biome cinematic mood LUTs (#51), PBR-ish GGX specular + roughness-aware reflections on voxels (#52), and a global Brightness control. (#53)
- 💧 Soft water reflections + visible sky bodies (#55); clear water you can see into again. (#56)
- 📜 Relicensed **MIT → AGPL-3.0-or-later** + a Contributor License Agreement. (#59)
- 🧩 Native in-game Arcade + Wiki — removed UnityWebBrowser/CEF, unblocking Linux/macOS. (#58)
- ⚡ Runtime-quality pass: client perf, netcode area-of-interest & interpolation, input remapping. (#60)
- 🚀 Ship cargo hold is now actively usable — manual transfer, capacity, auto-stow. (#62)

## [0.4.2] — 2026-06-23

- 🍽️ **You no longer starve in the first few minutes** — starter food (5 berries + 2 rations), an "eat" onboarding objective, an earlier hunger warning, and clearer food descriptions. (#46)
- 🔫 **A fighting chance against early hostiles** — a weak ranged starter **scrap pistol**, fixed ranged weapons only firing at point-blank, and a 3D line-of-sight gate so enemies can't hit what they can't see. (#45)
- 🐧 **Linux/Proton: no more hard 30 fps lock** — VSync is now its own setting with a separate frame-rate cap. (#47)
- 📝 README intro reworked around the family-and-community project story. (#44)

## [0.4.1] — 2026-06-22

- 🚀 No more creatures trapped in your ship — the ship evicts wild creatures on landing (companions may still follow you aboard); same guard for planet enemies. (#43)
- 🛡️ You're truly safe once aboard — visible attacks (drone fire, robot claws, creature bites) now stop the moment you board. (#41)
- 🧭 Cleaner HUD in space — the planet compass and day/night–temperature–gravity panel hide while piloting (compass stays on during EVA). (#39)
- ⌨️ Feedback is now simply **F1** — the never-clickable bottom-right button is gone. (#42)
- 🎥 Automated in-game clip recorder (video + frame-synced audio) for marketing clips. (#40)
- 🐳 Docker Desktop local-test walkthrough in SELF_HOSTING. (#38)

## [0.4.0] — 2026-06-21

- 👥 **Raised the default player cap from 4 to 12** + a 12-player smoke test. (#19, #21)
- 📻 **Tiered radio reach + live voice chat.** (#30)
- 🐳 **Optional Docker image for the dedicated server.** (#26)
- 🧰 UI fix: split the in-game menu top bar into two rows so Close no longer overlaps tabs. (#35)
- ⚙️ CI hardening: PR build/test gate (warnings-as-errors, docs-only skip), format/lint/CodeQL checks, Actions pinned to SHAs, CodeQL compiles C# in manual build mode, release notes prepend the Docker pull command. (#16–#37)

## [0.3.2] — 2026-06-21

- 🚀 Explosion stays with no landing animation after ship destruction (#10); rear hatch shows a blue energy door in flight, not an open hole. (#11)
- 🛬 Landing flies the descent to the chosen planet, not the launch body. (#12)
- 👾 Floating scan-drones fire a ranged laser. (#13)
- 📸 Per-planet surface screenshots for variety (#8) with terrain-aware capture placement. (#14)
- 📝 README gains a Project Status section, table of contents and itch.io/releases links. (#6, #15)

## [0.3.1] — 2026-06-20

- 🧹 Release CI strips the Burst `_DoNotShip` folder and mirrors installers to itch.io. (#4)

## [0.3.0] — 2026-06-20

- 🔀 Require a pull-request workflow (repo public, `main` protected). (#1)
- 🔒 Patched langchain/langsmith/starlette security alerts in the AI backend. (#2)
- 🧹 Cleared analyzer warnings (nullable, async, dispose). (#3)

## [0.2.0] — 2026-06-20

- Early release. See the [full diff](https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.1.0...v0.2.0).

## [0.1.0] — 2026-06-20

- Initial public release.

[Unreleased]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.7.22...HEAD
[2026.7.22]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.7.21...v2026.7.22
[2026.7.21]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.7.20...v2026.7.21
[2026.7.20]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.7.19...v2026.7.20
[2026.7.19]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.9.1...v2026.7.19
[0.9.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.7...v0.9.0
[0.8.7]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.6...v0.8.7
[0.8.6]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.5...v0.8.6
[0.8.5]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.4...v0.8.5
[0.8.4]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.3...v0.8.4
[0.8.3]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.2...v0.8.3
[0.8.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.1...v0.8.2
[0.8.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.7...v0.8.0
[0.7.7]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.6...v0.7.7
[0.7.6]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.5...v0.7.6
[0.7.5]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.4...v0.7.5
[0.7.4]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.3...v0.7.4
[0.7.3]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.2...v0.7.3
[0.7.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.1...v0.7.2
[0.7.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.6.2...v0.7.0
[0.6.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.4.2...v0.5.0
[0.4.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.4.1...v0.4.2
[0.4.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.3.2...v0.4.0
[0.3.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/marceld23/BlocksBeyondTheStars/releases/tag/v0.1.0
