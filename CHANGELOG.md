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

### 🏔️ Landscape variety, part 4 of 6 — water in the desert, moss on the rock, five new blocks (#1647)

- **Five new blocks:** moss stone, tar, bone, sandstone and scree — each a material you can mine, carry and
  place, with its own texture, available in the build editors and (except tar) in the dye and shape actions.
- **New worlds only — water where it was missing:** marsh sheets with reeds on wet flats, oases with a grass
  ring and palms in the desert, steaming hot springs, lakes in calderas and shield volcanoes (lava on
  volcanic worlds), small round maar lakes, salt-crusted playas, and tarns at the head of glacial valleys.
- **The ground tells its story:** scree and bare rock on steep slopes, ash around volcano cones, dry
  riverbeds on dry worlds, banded sandstone and granite decks on mesa worlds, bare soil patches in the
  grass, and moss on the rock of warm wet worlds. Underground strata are sandstone now.
- Existing worlds are unchanged. (Under the hood the world-generation golden test now compares block
  names instead of numeric ids, so adding a block no longer moves every checksum.)

### 🏔️ Landscape variety, part 3 of 6 — new kinds of mountains, holes and hidden places (#1646)

- **New worlds only.** Twelve new landform kinds join the volcanoes, buttes and rifts: broad shield
  volcanoes, round impact basins that fill with water, glacier-carved U-valleys, wind-carved yardang ridges,
  drumlin fields, lone granite domes (inselbergs), star dunes, fields of little mud volcanoes, chains of
  sinkholes, small crater bowls (maars), mushroom rocks, and glacier tongues running down cold mountains.
- **Things that hang in the air:** natural rock bridges over gorges, wave-cut ledges at the foot of sea
  cliffs, snow cornices on the ridges of the coldest worlds.
- **Under the ground:** hollow crystal geodes to break into, caverns that hold whole underground lakes on
  wet worlds, and layered rock strata in the upper crust.
- Existing worlds are byte-identical.

### 🏔️ Landscape variety, part 2 of 6 — every new world rolls its own relief (#1645)

- **New worlds only.** A planet type used to have one landscape: every desert a dune sea, every highland a
  mountain world. New worlds now roll one to three landscape styles from their type's pool and lay them
  out as regions — a desert can be dune seas next to badlands next to flat country with buttes, a highland
  mountains next to fjord coasts. Seven new styles join in: island archipelagos, fjordlands, chalk downs,
  shattered rift country, terraces, drumlin fields and glacial trough highlands.
- **Each world has its own wavelength.** Hill spacing and dune pitch vary from world to world of the same type.
- **Biomes shape the ground.** Marsh biomes lie flatter, stone country rises rougher — on the same world.
- **Rare whole-planet shapes:** tilted worlds (one hemisphere high, one low), three-storey stepped worlds
  and a ridge girdling the equator. Three new regional landform types (moorland, knob-and-kettle, coastal
  cliffs) join the blend.
- Existing worlds are byte-identical (the generation-0 goldens prove it).

### 🏔️ Landscape variety, part 1 of 6 — the foundation (#1644)

- **Nothing visible changes yet — every existing world stays byte-identical.** New worlds now carry a
  *terrain generation* number in their save (the classic generators are generation 0), so the coming
  landscape parts reach new worlds only and never move terrain under an existing base. The world-options
  panel ("Hand-designed structures…" page, under *Galaxy & terrain*) shows which landscape generation a new
  world gets. Under the hood the world generator is split into one file per landform family, its
  planet-type gates read data tags instead of type names, and landmarks and scatter props are registered
  in tables — adding a new kind of mountain or prop is one row from now on.

### 🧭 A star system you jumped into but never landed in can be jumped to again (#1638)

- **"Hyperjump to this system" for every other system.** The travel screen used to offer the jump only for
  a system you had never entered. After one jump into a system without landing there, its worlds showed up
  locked ("not visited") and the jump entry was gone — the system could not be reached again from the
  screen. Now every system other than the one you're in keeps its jump button above its worlds, and a
  locked world in another system offers the same jump in its detail pane. Thanks to Lyxette for the report.

### 📬 Report inbox — a report pair keyed alike on both halves has one conversation again (#1642)

- **The admin page of a report said "no replies yet" although the report had been answered.** Since #1359 the
  game server forwards the player's reply key with its `/bump` snapshot, so both halves of a current report carry
  the same key — and the #1378 rule "a keyed row owns its own thread" then gave each half a thread of its own.
  The list links the `/bump` half, an answer posted on the client row was invisible from it (and vice versa);
  the player's game, which polls by key, always saw both. Whichever half you open now hands the conversation to
  the client row, and the page shows the entries of both halves merged, naming the half an older, split entry
  still lives on.

### 🌋 Volcanoes wherever the core is molten, and volcanic islands in the sea (#1631)

- **Volcanoes on every world with a lava core.** Basalt cones with a molten crater used to grow only on
  watery, breathable worlds; now any world whose depths end in lava can carry them — desert, salt flats,
  toxic rock, even the lava worlds themselves. Cratered moons and asteroids stay dead.
- **Volcanic islands.** A volcano whose base lies under the sea now rises until its crater clears the
  water by a good margin, with a wider base — an ocean world's horizon gets a smoking mountain instead of
  a drowned bump. Which cells get one is still the same seeded roll, so some ocean worlds have several and
  some none.
- **New worlds only.** Like the continents, this switches on at world creation; your existing saves keep
  exactly the terrain they have — no mountain grows over a base you already built.

### 🏝️ Landing on ocean worlds: real islands first, proper islets otherwise, the seabed only in the shallows (#1618 #1619 #1620 #1621 #1622)

- **Landing pads look for land north and south too.** Until now a pad only searched east and west along
  its own latitude and gave up in open sea — while real islands lay a short swim away. The search now
  circles outward in every direction, and on ocean worlds it looks further (#1618). On twelve test ocean
  worlds that put 157 of 164 pads on natural land, up from 81.
- **No more landing at the bottom of a deep sea.** A pad that still sits in open water raises an island
  whenever the water is deeper than a wade (8 blocks); only shallow water still parks the ship in its dry
  seabed shaft. Every world with a water sea follows the same rule now, not only ocean worlds (#1619).
- **The islets are islands now**: a level top wider than the ship, a gentle beach into the sea, a
  natural outline instead of a perfect disc, grass and a few plants where the world has them (#1620).
- **Your first landing prefers dry ground.** A new player's first pad, and any landing you do not pick
  by hand, takes natural land over an islet over the seabed (#1621).
- **Seabed pads are blue on the approach map** and say how deep the water is ("underwater · seabed · 6 m"),
  on the world map too (#1622).

### ✨ A bigger sky (#1615 #1616 #1617)

- **More stars in every new world.** A Normal world now starts with 12 star systems instead of 8, and the
  other Universe-size tiers grew with it: Small 6, Large 20, Huge 32. A Growing world also starts at 12 and
  keeps growing at the frontier as before. Existing saves keep the galaxy they were created with (#1615).
- Dedicated servers that do not set a system count start with the same 12 systems (#1616).

### 🌌 A map of the stars (#1603 #1604 #1605)

- **The system chart has a Hyperspace tab.** Press M while flying and switch tabs (LB/RB on a pad): the
  whole galaxy as stars in their real colours. The ringed star is where you are, named stars are systems
  you have visited, a **?** is one you have never entered, and the lines are the relay network's jump
  lanes. Click a star to read about it — and, with a jump generator aboard or a lane, hyperjump to it
  straight from the chart (#1603).
- **Stars in their true colour.** Every system's star colour now travels with the star map, so the chart
  shows the same sun you will see after landing (#1604).
- **The finale system sits beyond the frontier.** The Guardian Core, once revealed, appears out past every
  other star on the chart instead of in a corner among them (#1605).

### 🪐 More room between the planets (#1599 #1600 #1601)

- **Space distances in kilometres.** The radar and the system chart now say "830 km" to the next planet
  instead of "83 m" — a scale that fits what you are looking at. On a spacewalk the way back to your ship
  is still given in metres (#1599).
- **Roomier star systems.** Planets, moons and asteroids sit half as far apart again in the flight view, so a
  system no longer looks like a huddle. The hop to the next planet takes a little longer (about 20 s instead
  of 13 s in the starter ship). Anything you built in space next to the planet you launch from stays exactly
  where it was; a structure parked beside *another* planet is now a bit off it (#1600).
- **Moons keep their distance.** A moon rides one and a half times the clear gap off its planet instead of
  hugging it — a planet with moons reads as a family, not a clump (#1601).

### 👁️ A wider view (#1589 #1590 #1591)

- **The world no longer feels oversized.** The first-person camera now looks through an 80° field of view
  instead of the narrow 60° it silently used before, so blocks take up about a third less of your screen and a wall
  only fills the view when you are really standing at it (#1589).
- **Field of view setting.** Settings → Controls has a new *Field of view* stepper, 50° to 100°. It applies
  right away, even from the pause menu; wider shows more of the world and costs a little frame rate (#1590).
- **A smaller tool in hand.** The drill, block or hand you hold is a fifth smaller and stays the same size on
  screen whatever field of view you pick (#1591).

## [2026.9.2] — 2026-09-05

The tune-up release. Nothing new to build or discover this time — instead the whole game went in for
service. We measured where the time really went, then fixed it, piece by piece: the game now draws
**three to four times as many frames per second** on the same computer, the small stutters while
walking are almost gone, hosted worlds idle quietly instead of churning, and a busy world sends far
less data to your screen. Nothing looks or sounds different — it is simply smoother, on the desktop
and in the browser. On top of that, **Lyxette** sent his seventh round of reports from his space
station, and every one of them is fixed here too. Thank you, Lyxette! 🙏

⚠️ **Compatibility:** the network protocol version moves to **5** (#1533 #1534 #1535). Updated servers
turn away older game versions at join with a clear "protocol mismatch" message — update the game
(desktop installs update themselves; the browser version is always current). Saves migrate unchanged.

### 🎓 Credits

- **Christopher Korb (K&K Multimedia) joins the school-club block** in the README and the in-game Credits,
  in all 14 languages — for the IT support behind the club.

### 🚀 Smoother on your screen (#1511–#1529 #1550–#1556)

- **Walking is smooth** (#1528 #1529 #1550 #1555). The tiny freezes every few seconds while you
  explore — the game tidying up memory behind the terrain — are almost gone. Standing still, they do not
  happen at all.
- **Less work per frame** (#1517–#1521). Lights, shadows, physics and screen effects now do only what
  you can actually see. The first rain, the first sunrise or a change of graphics preset no longer
  stutters, because the game prepares its shaders while the loading screen is up.
- **The sky and the jetpack got cheaper** (#1511 #1513). Stars and nebulae switch off by day when they
  are invisible anyway, and the jetpack's flames are two steady jets instead of a shower of sparks
  created anew every frame.
- **A quieter HUD** (#1512 #1516 #1552 #1554). Hints, the radar, the instruments and the outlined
  texts only redraw when something on them actually changed.
- **Less memory, quicker into the world** (#1522–#1525). Game content is loaded once per session
  instead of once per world, the block textures are shared between menu, editors and world, and the
  browser only re-packs your save when something changed.
- **Small savings everywhere** (#1514 #1551 #1553 #1556): weather and finale overlays cost nothing while
  they are not showing, creature materials no longer pile up over a long session, and the ground scatter
  and creature voices are generated without leaving litter behind.

### 🖥️ A calmer server (#1502–#1510 #1526 #1527 #1530 #1536)

- **The home pad no longer keeps the server busy** (#1502). Every world's home pad sits right on the
  map seam, and the server mistook everything across that seam for "far away" — so while you stood at the
  pad it threw out and rebuilt the terrain around you every ten seconds. Fixed; a resting server now
  rests.
- **Terrain is generated several times faster** (#1526 #1527). The pieces stacked above each other share
  their work, and trees, mushrooms and geysers are only computed where they can land. The worlds come out
  exactly as before — a new safety net checks that on every change (#1503).
- **Lakes, fires and snow no longer flood the database** (#1505 #1506). A breached lake used to save every
  single cell on its own; the steps now save in one go.
- **Only what changed goes out** (#1530). Other players' positions are sent only when they moved,
  creature lists once per tick instead of several times, and far-away villagers stand still instead of
  pathfinding for nobody.
- **Streaming and idling are smarter** (#1507–#1510 #1536): a view that is fully sent is not re-scanned
  every tick, plants regrow on a one-second clock, and the server is tuned for a small container.
- **For operators** (#1504): an optional log shows where the tick time goes.

### 📡 Leaner network — protocol 5 (#1531–#1535)

- **Creature and villager lists are a third of their size** (#1533), and inventory updates no longer
  re-send the unchanged blueprint list (#1535).
- **Terrain travels on its own lane** (#1534). Player positions no longer wait behind a lost piece of
  terrain — on a shaky Wi-Fi, other avatars and creatures stop stuttering when the world hiccups.
- **The browser game keeps its memory** (#1531 #1532). Singleplayer in the browser and the connection to
  hosted worlds copy each message once instead of three times — good news for tablets, where memory is
  what ends a session.

### 🛰️ Lyxette round 7 — stations, teleporter pads, ship repair, hyperjumps (#1558–#1568)

- **Your station's west wing is part of the station again** (#1558). One step west of the original core
  counted as thousands of blocks away: the suit floated, the air stopped and "drifted too far — pulled
  back to the pad" fired every second. Stations keep their own coordinates now.
- **Air and gravity follow the whole build** (#1559). Everything you built inside before the last update
  is folded into the station on boarding, doors count towards the hull, halls up to 64 000 cells breathe,
  and an oversized hall says so instead of blaming a hole in the hull.
- **Teleporter pads work after a station visit** (#1560). Leaving a station, respawning at home or being
  recovered to the ship dropped the pads, beacons, bases and map markers from your screen until the next
  landing — the pad kept glowing but ignored E. All of them come back now, and a pad shows "Press E:
  Transporter".
- **Repair your ship with R** (#1561). Stand at the cockpit or the ship console and press R; the panel on
  the right greys out and tells you what is still missing, and it is up to date after every landing.
- **Stow blocks in the ship's hold and set up a stone crate** (#1562). "Stow all" and the crate filter
  refused building blocks; the station container no longer vanishes after you leave.
- **The factory terminal says what it is** (#1563): decoration for your own halls — only the terminal of
  a found factory produces.
- **Bug reports carry your system info** (#1564): OS, CPU, RAM, GPU and driver, plus whether the last
  session ended without a clean shutdown; the manual explains what to send after a blue screen. A memory
  leak in the item-drop bundles is fixed.
- **Hyperjumps arrive with a name** (#1565 #1566 #1567). The HUD, the orbit view and the F1 form named
  the system you had left; landing on the arrival planet set you down on the OLD planet's terrain; and the
  world stopped simulating while you cruised the new system. All three fixed, and bug-report snapshots
  now name the body you are really at.
- **No sunburn above your own station** (#1568). Floating past the gravity box no longer applies the
  spacewalk heat.

### 🔧 Playtest fixes (#1577 #1579 #1580)

- **Water shows its bed again** (#1577). At Medium and above the water had turned milky and opaque —
  a side effect of the screen-effect savings above. You can see to the bottom again.
- **No more error lines when a ship is built** (#1579 #1580), and the weather now notes every change of
  rain or snow in the log, so a "no rain in this thunderstorm" report can be traced next time.

### 🛠️ For developers

- **A Development-build switch and an allocation report** (#1537) — the tools that found the savings
  above; what was measured and rejected is documented in #1536.

## [2026.9.1] — 2026-09-03

The school-club release. On 2 September the game-development club at the Kurfürst-Balduin
Realschule plus in Trier West had its first evening — and the kids did exactly what we asked: they
played, and then they told us honestly what was wrong. **Ben, Marie and Nikita** are the first club
members in the Credits; their reports are behind the landing, ocean, scanner and camera fixes below.
Right beside them, once again, **Lyxette** — our most diligent tester by a wide margin — sent in
two rounds of reports from his own space station and his walled base: **more than twenty issues in
two days**, and every single one of them is fixed here. Thank you both, club and Lyxette. 🙏

Protocol stays 3, saves migrate — including stations you built before this update.

### 🎓 Credits — the school game club

- **The club has its own credit block** (#1468 #1499) — *School club "Building Games with AI"* (Schul-AG „Spiele
  entwickeln mit KI“) — in the README and the in-game Credits screen in all 14 languages, directly
  above the community contributors. **Ben, Marie and Nikita** open it: their first club day produced
  the reports behind #1453–#1456, #1459 and #1462.

### 🎒 School playtest — landing safely, oceans, launching, the first lesson (#1448–#1450 #1453–#1456 #1458–#1464)

- **Landing keeps your feet on the deck** (#1450). The parked ships and the ground now arrive before
  you do, so a landing — or stepping into your ship — no longer drops you through the floor.
- **Beaming to a beacon no longer puts you in the floor** (#1449). You arrive two clear blocks above the
  pad, the ground under it is sent ahead, and the game waits for the real floor before it lets go.
- **The heal tank no longer freezes you for eight seconds in the browser** (#1462). A short
  "Stabilising position…" line explains the one-second wait that remains.
- **Ocean worlds get beaches** (#1453). Where a landing pad would sit in open sea, roughly three in
  five now rise on a small sand islet — a dry beach with a gentle slope into the water. The rest keep
  the dry seabed shaft, and the **approach map and world map now say "underwater · seabed"** for
  those pads (#1454), so nobody is surprised by walls of water.
- **E at the cockpit asks "Launch into space?"** (#1455). The only way off a planet used to be a
  button at the top of the map. Confirm with the button, E or Enter; "Not yet" opens the map as before.
  VEGA explains a seabed landing the first time it happens, and a first dive gets a swimming hint.
- **VEGA's tutorial runs in creative worlds too** (#1461). A creative world hands out every blueprint at
  once, which used to read as "a veteran save" — a school browser session never heard a single lesson.
- **Titans can be scanned** (#1458). The scanner only aimed at a creature's feet, which a ten-block
  titan never offered while you looked at its flank. It now hits the same body the attacks do, and the
  "LMB: scan" prompt only appears when there is something to read.
- **No more see-through slits at block edges** (#1459). Beside a torch, lantern, ladder, plant or
  leaf block a thin line of daylight showed along the edge of the wall. Gone.
- **The bed says what it does** (#1456). Aiming at a bed or heal tank shows "E: set home spawn ·
  heals slowly nearby"; the crew quarters no longer promise "sleep to skip time". All 14 languages.
- **Cameras stop at walls** (#1460). The third-person and chase cameras pull in instead of clipping
  into geometry, and the stuck-in-block guard also checks head height and blocks placed beside you.
- **The painted hand updates at once** (#1464) when you edit the arm painting or suit colours.
- **Updates no longer hang on "not responding"** (#1448). The restart into the updater happens off
  the main loop and the game quits itself, after a running singleplayer world has been saved.
- **Far less memory when returning to the main menu** (#1463) — the spike that could end a long
  browser session with an out-of-memory at the menu is an eighth of what it was.

### ☀️ Brighter days (#1457)

- **Worlds read brighter by day** (#1457). "The worlds feel dark and oppressive even at noon" — the
  club's browser verdict. Overcast skies dim the day less (swamp, ashen and fungal worlds, which
  never clear, no longer live in permanent dusk), shaded block faces get a touch more light, the
  vignette and extra contrast are eased, the browser's Low and Potato presets use a gentler colour
  finish, and the **brightness slider sits at the top of the graphics settings**.

### 🛰️ Your own space station — Lyxette's rounds 5 and 6 (#1469–#1478 #1480–#1489 #1493)

Lyxette commissioned a station from his own ship, built it out, walked its edge — and found
everything that was still missing. Player stations are now a proper place to live:

- **Drawn once, and it stays** (#1469 #1470). A station with a built hull no longer also shows the
  generic spinning placeholder, and the hull survives your next landing instead of vanishing until
  the next restart.
- **Your station is only in its own orbit** (#1480). It used to appear as a dockable contact in every
  orbit of the star system — floating in the next moon's rings at the wrong coordinates.
- **Stations built before this update are found again** (#1493). A station deployed by a ship that
  had never landed anywhere was keyed by its planet's *type*; those rows are re-keyed on first load.
- **What you build inside stays built** (#1481). Every block placed or mined aboard is written back
  into the station and persisted — a restart no longer resets your rebuilt interior. Doors built
  inside stay doors and count as airtight. (Blocks mined *before* this update come back once.)
- **The spawn never carves into your build** (#1493). Where you appear aboard is judged after the
  station is restored; if that spot is blocked, you appear at the nearest free cell instead of the
  game cutting a hole into your wall every start.
- **Sealed rooms have air** (#1473). A station breathes only inside a pocket enclosed by airtight
  blocks (glass counts), doors and the core. A hole means helmet on, with a one-time warning; a force
  field plugs it. The HUD shows "(station life support)" while you are sealed in.
- **Nobody falls off the edge for ever** (#1485). Step over the rim while building and the suit now
  floats — jump rises, crouch sinks, you drift to a stop and float back to the deck or build the
  outer hull from outside. Drift far enough away and the station pulls you back to the pad.
- **Crew on demand** (#1472 #1487). The two filler civilians appear only around a built vendor or
  mission board — and the post is staffed only while that room is sealed and breathable; a message
  tells you when it is not.
- **Windows show something** (#1474). The host planet fills the view at eye level, with a moon and
  a sibling world for the other walls.
- **A few smaller things**: station numbering no longer collides after a restart (#1478); "falling"
  while standing on the deck now writes a diagnostic line we can act on (#1488); tint, glow and shape
  of interior blocks are visible from a spacewalk (#1493).

### 🧱 Bases — walls that hold, wildlife kept out (#1451 #1452 #1482 #1483 #1495)

- **No wild animals within 24 blocks of a base core** (#1451) — a plain guarantee for every base,
  no wall needed. Contributed by **ahmdkaml** — thank you! The base-core text and the codex now name
  this rule first and keep the wall-ring rule for larger yards (#1495).
- **Walls stop the walking robots** (#1482). A walker used to stroll straight through a wall seven
  blocks high. Walkers now climb two blocks at most, drones hover over three, and a wall of four or
  more stops the scan-drones too.
- **`/basewalls` tells you why animals got in** (#1452). Lyxette had elephants on his walled pad.
  The new admin command names the nearest base core, reads your yard as sealed, enclosed or open,
  and lists the rules — which the base-core text, VEGA's hint and the codex now state honestly:
  walls at least two high and closed at feet level, an open wooden door is a gap while slide and
  energy doors always count, unloaded neighbouring areas read as open, cave dwellers ignore walls.
- **Torches burn in base air** (#1483). A sealed base room on an airless world refused a torch;
  breathable base air now counts as atmosphere.
- **`/creatures`** (#1489) lists every animal within 48 blocks with where it stands and whether it
  is on the ground — the tool that found the walking-robot bug.

### 📦 Cargo, salvage, codex, controls (#1471 #1475–#1477 #1484 #1486)

- **Cargo tiers explain themselves** (#1471). Tier I says "+24", and the Modules tab reads
  "72 slots = 48 + 24 · tier 1 of 3 · next: II (+32)". The German remove button is "Entfernen".
- **Space salvage is lossless** (#1475). Ore that does not fit floats at the rock, survives leaving
  and returning, and a ship without a tractor beam collects it by flying through.
- **The codex lists every ore per planet type** with its start depth — and says plainly when a
  type has no rare metals (#1476). The tractor beam's range comes from its module stat now (#1477).
- **No more accidental discards** (#1484). The inventory stops moving under your click when a
  message or the contact list refreshes, the list keeps its scroll position, the discard confirm
  names the item, and suit gear gets a Keep / Throw-away choice.
- **Sneak lets you look over the edge** (#1486). The edge-stop leaves your eye past the rim, so you
  can build the outer face of a wall from above without stepping off.

### 🙏 Thanks

- **Lyxette** — two rounds of reports in two days, more than twenty issues, every one reproducible:
  the whole station chapter, the base walls, the robots, the inventory, the sneak edge.
- **Ben, Marie and Nikita** from the school club — first evening, first reports, first Credits entry.
- **ahmdkaml** for the base-core spawn guarantee (#1451), his first code contribution.
- Everyone who pressed F1 on the desktop — the updater, beacon and menu-memory fixes came from your reports.

## [2026.8.26] — 2026-09-01

The headroom release. The last release made browser play on a tablet *smooth* — but tablets with
little memory could still crash right after the loading screen, because the browser simply wouldn't
give the game the memory it asked for. This release is all about fixing that: in the browser the
game now **needs noticeably less memory**, it **keeps track** of how much it uses (so crashes can
finally be diagnosed right on the device), and if a device really can't manage, it **says so
kindly** — with a clear, translated message instead of a wall of developer text. Playing on desktop
is completely unchanged.

Protocol stays 3, saves migrate.

### 📉 Less memory needed on tablets (#1436 #1437 #1438 #1440)

- **The world no longer piles up in memory (#1438)**: on phones and tablets the game now keeps only
  as much of the world loaded as your view distance actually shows. Before, terrain you had
  travelled past quietly stayed in memory far beyond what you could see — the main reason long
  sessions ran out. Raise the view distance and you get the wider horizon back; desktop is
  untouched.
- **The right amount of memory from the start (#1437)**: the game now reserves its working memory in
  one go at startup instead of growing it in dozens of small steps during the critical
  loading phase — exactly the moment weak devices used to give up.
- **Leaner networking (#1440)**: messages between game and server are processed with far fewer
  detours, which means less memory churn every single tick — especially noticeable in browser
  singleplayer.
- **Memory tracking (#1436)**: the game page now records how much memory is in use and remembers the
  peak — the foundation for the friendly crash messages below.

### 📦 A smaller download — measured, not guessed (#1442 #1443)

- **The browser version got smaller (#1443)** — a safe trim with no effect on how the game runs.
- **A much bigger trim was tested and rejected (#1442)**: it would have cut the download by a third,
  but a real in-game measurement showed it made the game three times slower — so it doesn't ship.
  The performance test rig now runs directly in the browser, so future decisions like this one can
  be measured instead of guessed.

### 🛟 Friendly messages when memory runs out (#1445)

- **A heads-up before it goes wrong**: on devices with little memory, the game now warns you before
  it even loads — "your device has little memory, close other tabs and apps" — with a **Start
  anyway** button.
- **Clear words instead of cryptic errors**: if the browser can't provide enough memory at startup,
  or the game runs out mid-session, you now see a plain, friendly message telling you what to do
  (close tabs, restart the browser, reload) instead of a developer stack trace. Translated into all
  14 languages the game speaks — and the measured memory peak is shown right in the panel, so a
  crash can be reported without any cables or debugging tools.

## [2026.8.25] — 2026-08-31

The tablet release. Browser play on a mid-range tablet (the house Galaxy Tab A8) stuttered from the
intro on, controls felt sluggish, and leaving the ship popped a fatal-looking browser modal for an
audio hiccup the game fully recovers from. This release gives the WebGL build a real **mobile
profile**: the device is classified by GPU family, the guess is corrected by ~15 s of real frame-time
measurement, touch behaviour is live from the first menu, and mobile budgets keep the world ticking
smoothly — while desktop browsers are never stripped down. The guiding principle throughout: touch
detection and performance tiering are separate concerns.

The rest came out of Justus' latest playtest — the first-person hand finally wears your painted arm
design (and no longer renders pitch black), the bare hand throws a real straight punch instead of
showing the clipped-open forearm, and landing on another world no longer leaves a stale door floating
in mid-air. Plus a hosting cleanup: WorldHost leaked ~1 GB of Docker volumes per world wake.

Protocol stays 3, saves migrate. Operators: the **worldhost image must be redeployed** for the
volume-leak fix (#1414).

### 📱 Tablet & browser — mobile profile, auto quality, touch from frame one (#1419 #1420 #1421 #1422 #1423 #1424 #1425 #1430 #1431 #1432)

- **The fatal-looking audio modal is gone** (#1419). Under memory pressure a tablet browser can fail
  to decode a music track ("EncodingError: unable to decode audio data"); the game already falls back
  to synth music, but Unity's JS error handler turned it into an "An error occurred running the Unity
  content" modal. The template now swallows exactly that error to the console — matching Chrome's,
  Firefox's *and* Safari's wording (#1431), at both interception points (alert override +
  `unhandledrejection`).
- **DPR and canvas guards** (#1420): the devicePixelRatio-1 cap now also fires for multi-touch
  coarse-pointer devices (iPadOS reports a desktop UA), while touch-screen laptops keep native DPR;
  `ApplyWindowMode` no-ops on WebGL — the template owns the canvas size.
- **The shell respects your preset** (#1421): the intro cinematic and menu background no longer force
  High post-processing and full-resolution SSAO on Low/Potato devices; Medium and up keep the High
  cinematic look.
- **Touch from frame one** (#1422): a touch latches at AppShell level and rescales the live
  EventSystem's tap-vs-drag threshold, so the menus behave touch-sized before any world is entered.
- **Auto quality calibration** (#1423): a new `BrowserDevice` start guess by GPU family (Mali /
  Adreno / PowerVR; "Apple GPU" + touch) and a new `AutoQualityCalibrator` that samples shell frame
  times for ~15 s and steps the auto-managed preset one notch per session; choosing a preset manually
  ends auto-management for good. Windows-on-ARM laptops and Chromebooks with mobile GPU names are no
  longer misclassified as tablets — the mobile branch requires touch support, and a Direct3D renderer
  string always classifies as desktop (#1432).
- **Mobile budgets** (#1424): WebGL Low means render scale 0.8 with HDR off; mobile first-run defaults
  are view distance 3 and synth music; and mobile skips the 45-second track prefetch (each decoded
  track is ≈ 80 MB of PCM).
- **Browser singleplayer smoothness** (#1425): the embedded server is capped at 2 ticks per frame
  with a 3 ms chunk-generation budget on mobile, and the prologue camera's terrain raymarch is cached
  across 3 frames. A review fix made that cache actually revalidate (#1430) — an int-overflow
  sentinel had silently disabled the prologue camera's terrain safety net on **every** platform.

### 👊 First-person hand and doors — playtest fixes (#1427 #1428 #1429)

- The first-person hand shows your painted arm design and no longer renders as a black silhouette —
  the viewmodel never received the appearance editor's arm painting and its materials lacked the
  avatar's ambient lift, so dark suit colours sank to pure black. (#1427)
- The first-person arm no longer looks cut off at the back when punching: the camera near plane
  clipped the forearm open and the generic tool jab swung that end into view; the bare hand now
  throws a straight punch of its own. (#1428)
- Landing on another body no longer leaves a stale door floating in mid-air while your ship's hatch
  goes doorless — door ids restart per world and the client recycled an old door object for the
  ship's hatch id; doors are now dropped on every world change and rebuilt when their record moved,
  and respawns restock the door list. (#1429)

### 🧹 Hosting (#1414)

- WorldHost leaked ~1 GB of anonymous Docker volumes per world wake — the image declared `/app/clients`
  + `/app/webgl` as VOLUMEs, every start downloaded the client installers into one, and `docker rm`
  ran without `-v`. Now `rm -v`, `BBS_FETCH_CLIENT=0` for hosted instances, and only saves + config
  are VOLUMEs; the VPS was pruned from 121 GB to 18 GB and a weekly prune guards the host. (#1414)

### 📄 Project

- The CLA gained a patent-termination clause and a moral-rights waiver and now names German governing
  law. Contributors are asked to re-sign on their next PR. (#1416)
- The README's localization row now says what is actually needed: all 14 locales are key-complete —
  contributions wanted are native-speaker polish, not missing keys. (#1418)

## [2026.8.24] — 2026-08-30

The drafting-table release. The three build editors — ship, station, town — stopped being empty
rooms with a palette. **Load** now lists every built-in ship and every built-in station and town
template next to your own designs, so you start from the game's Scout or a real outpost instead of
a blank grid; the station and town editors can **generate** a procedural layout for any tier and seed
and hand it to you as editable cells; the blocks you place wear their **real textures** instead of
flat colour; and a new **colossal** station tier sits above huge. Underneath, six things that made
the editors hard to use at all — palette rows the height of a wall, a double scrollbar, WASD typing
into the name box, a menu planet left behind in every editor, no wheel zoom, a grid you could not see.

The other half is the **gamepad**. Eight menu screens were walkable with a pad in theory and not in
practice: the crafting search box swallowed the stick, the slot pie could not be navigated, text
fields never showed focus, long lists scrolled the cursor out of the frame, and nothing told you what
the buttons did. Every one of those is fixed, the selection wears a cyan frame and clicks, LB/RB step
through the tabs, the right stick scrolls any pane, and the pause menu finally has a controller route.
Plus the loading spinners that orbited their own corner, an update download that shows its percentage,
credits that no longer vanish at the bottom, and two report-inbox repairs for the operator side.

Protocol stays 3, saves migrate. Operators: the **ReportHost image must be redeployed** for the inbox
fixes (#1378, #1380).

### 🏗️ Build editors — start from a real ship or template, real textures, procedural layouts, colossal (#1394 #1395 #1396 #1397 #1398 #1399 #1400 #1401 #1402)

- **Load lists the built-in designs** (#1394, #1395). The ship, station and town editors' LOAD button
  only knew your own exports. It is now a shared, scrollable picker with sections: every built-in ship
  with a layout, the built-in station and settlement templates, your usercontent templates and your
  export bundles — each row with size and cell count. Loading fills the room and the form, frames the
  camera and asks first when something is already placed.
- **A built-in template loads as a copy** (`<key>_2`, with a hint) (#1396): usercontent is *added* to the
  pool, so saving under the original key would only have cloned it.
- **The ship's interior is an explicit frame** (#1397): width / height / length are three steppers with a
  translucent cyan frame instead of the bounding box of every placed cell — the server treats those three
  numbers as the cabin; wings and engines sit outside it. The interior origin now sits at (8, 0, 8) in the
  room, so the negative-x/z exterior cells every shipped layout has can be represented.
- **Palettes gained the missing blocks** (#1398): console (ship), NPC and greenhouse (station), chest and
  data terminal (settlement); both loaders report skipped cells instead of dropping them silently.
- **Merging keeps what you did not touch** (#1399): `merge_ship.py` preserves `startModules` (the editor
  exports the loaded definition's list), `merge_structure.py` keeps `legacyPool` / `planetTypes` by
  starting from the existing entry; both replace in place. The settlement form carries `planetTypes`.
- **Real block textures in the room** (#1400): each block face gets its atlas tile, and the editor's
  vertex-colour shader multiplies an optional texture into the colour — face shading, dye and glow still
  apply, and every mesh without texture coordinates (creatures, doors …) renders as before. Stations,
  markers and non-block elements stay flat. Shaped cells map their geometry's unit tile into the atlas.
- **Generate** (#1401): the station and town editors run the game's own `StationGenerator` /
  `SettlementGenerator` on the client for the chosen tier and seed (Reroll; villages get a surface-block
  field) and load the result as editable cells — markers first, so a block never displaces the vendor.
  Palettes gained the marker ids the generators emit (spawn, sliding and hinged doors; greenhouse for
  settlements). A test guards every tier × three seeds against the 128³ room and the palette.
- **Colossal tier + size hints** (#1402): a colossal station tier above huge, and a "Procedural: …" line
  under the tier stepper reads the generators' own layout tables so you know what a tier produces.
- Locales: 29 keys EN/DE, the 12 community locales machine-topped-up. Docs: USER_MANUAL,
  SHIP_TYPE_EDITOR, STATION_SETTLEMENT_EDITOR.

### 🧰 Build editors — palette rows, scrollbar, typing guard, orphaned planet, wheel zoom, visible grid (#1386 #1387 #1388 #1389 #1390 #1391 #1393)

- **Palette rows were 100 units tall** (#1386): `UiKit.ScrollList` lays out without controlling child
  height, so every row and header sat at the RectTransform default. Rows and headers now set their size.
- **The trade dialog showed two scrollbars** (#1387): `UiKit.ScrollList` attaches its own auto-hide bar;
  the dialog's extra call is gone.
- **Typing a name moved the camera** (#1388): WASD / QE / Space / Ctrl / R / F are skipped while a text
  field is focused.
- **A menu planet floated in every editor** (#1389): the start-screen planet and moon were parented outside
  the backdrop root and survived its teardown. They travel with it now.
- **Wheel zoom and framing** (#1390): the mouse wheel dollies along the view (Shift ×3), the opening view
  frames the room, and **F** re-frames it — shared `EditorSceneKit` for all three editors.
- **A grid you can see** (#1391): one mipmapped procedural grid texture (minor line per cell, major every
  eight) replaces 258 sub-pixel cube lines, and the placement ghost is shared by both editors — the station
  and town editors finally show the target cell.
- The build-room hint line names wheel zoom and F in all 14 locales (#1393; ru/uk also spell out "middle
  button" for remove so it no longer collides with the wheel entry).

### 🎮 Gamepad — the menus are actually navigable now (#1404 #1405 #1406 #1407 #1408 #1409 #1410 #1411)

- **The crafting search box trapped the pad** (#1404). It was the one text field built by hand instead of
  through `UiKit.AddInput`, so it lacked the bridge that keeps a field deactivated while a pad is in hand:
  landing on it killed the stick, and because the game then read "typing", **(B)** and **Start** stopped
  working too. It now goes through the shared builder like every other field.
- **The slot-action pie (R3) could not be navigated** (#1405). Two causes: its four wedges are concentric
  rects told apart by rotation only, which uGUI's automatic navigation cannot score — the selection never
  left "Swap" — and a HUD button the mouse had clicked earlier stayed selected for ever, so the pie never
  claimed the pad at all and the stick drove an invisible cursor. The wedges are now wired as an explicit
  ring (a dimmed wedge falls through to its opposite), pop-ups clear a stale selection on open, and the
  selected wedge lifts to cyan.
- **VEGA lines can be dismissed on a pad** — they always could, on the button Unity calls "Back": the game
  now names it **View**, which is what is printed on every Xbox pad since the One (the hint reads
  "Continue · View"; Share on PlayStation, − on Nintendo). Likewise the menu button is now called **Menu**
  (☰, right of the Xbox logo) instead of "Start" — a player reading "Start" reached for the Xbox-logo button,
  which Windows' Game Bar owns, and found no way into the menus.
- **Text fields showed no focus at all** (#1406): a runtime-built `InputField` has no `targetGraphic`, so
  its tint never changed. Fields now light up like buttons — which is how you find the name box.
- **The selection scrolls into view** (#1407): moving down a long list (crafting, settings, the Codex …)
  now scrolls the pane so the highlighted row stays inside the frame.
- **A hint strip along the bottom** of every pad-navigable screen says what the buttons do — "(A) choose ·
  (B) back", "type" on a text field, plus the screen's own extras — in the glyph set you picked (#1408).
- **LB / RB step through the in-game menu's tabs** (#1409), landing the cursor on the new tab.
- **The selection wears a cyan frame and moving it clicks** (#1410) — on a wall of inventory tiles the old
  tint-only highlight was invisible, and the stick moved the cursor in silence.
- **Scrollbars left the navigation graph** (#1411): the stick can no longer park the cursor on an 8 px bar.
- **The right stick (and the d-pad) scroll any pane** — credits, What's new, a story page, the settings
  list. Text-only screens hold nothing the stick can select, so a pad could not read past the first
  screenful; the hint strip names "RS scroll" whenever a screen has something to scroll.
- **The pause menu is reachable on a pad.** Esc was keyboard-only and every stock pad button is spoken for,
  so Resume / Settings / Quit had no controller route at all. The Start screen's top strip now carries a
  **Pause menu** button, the dialog is stick-navigable, and **(B)** resumes.

### 📬 Report inbox — "mark done" and delete cover both rows of a report (#1380)

- **Marking a report done or deleting it always left one row behind.** The admin list shows the two rows
  every in-game F1 report produces (client-direct + the server's `/bump` snapshot) as one report, but the
  buttons acted on the one row you had opened: the other half stayed `new` — a lone row under the status
  filter — or survived a delete under its `Bump [world]: …` title. Even without clicking, a follow-up
  question or a player's answer moved only the keyed half. Status changes and deletes now cover the pair
  (detail page and `PATCH` / `DELETE /api/reports/{id}` alike — the buttons say "both rows", the API answer
  lists the `reportIds` changed), the reply-driven flips are mirrored onto the other half, and pairs left
  triaged apart settle on their most advanced state once at startup. Operators: redeploy the ReportHost image.

### 🌀 Spinners spin in place, the update download shows its progress, the credits scroll to the end (#1382 #1383 #1384)

- **Every loading spinner orbited its own corner** instead of turning in place — `Place()` pivots at the
  top-left and `Rotate()` turns around the pivot. The spinner is re-pivoted to its centre; the update notice,
  online status, manage-world and report dialogs all inherit the fix. (#1382)
- **The update download reports progress**: the notice reads "Downloading update… 2026.8.24 · 42 %" with a
  thin bar underneath, and the settings screen's status line carries the same percentage. Velopack's
  progress callback writes a volatile int from its download thread; the UI polls it per frame. (#1383)
- **Credits vanished when scrolled to the bottom**: the text's rect was only viewport-tall while its glyphs
  overflowed below, and `RectMask2D` culls by rect — past the first page, everything was culled. The rect
  now spans the whole scroll content. (#1384)

### 📬 Report inbox — the screenshot half of a pair shows the pair's conversation (#1378)

- **Opening a report from the admin list showed an empty conversation** although the developers had answered.
  The list links each pair to its richer half — the server's `/bump` row with the screenshot — and for reports
  filed before the reply channel that half carries no reply key (the #1359 repair blanks name-derived keys on
  purpose), so its page said "no in-game reply possible" while the answers sat on the client row behind the
  small `+1` link. Whichever half you open now, the page shows the pair's one thread and says where it lives;
  the reply form, `POST /admin/report/{id}/reply` and `POST /api/reports/{id}/replies` all write to the half
  that carries the key (the API answer names it as `reportId`), so an answer is never stored where no game
  can read it. Operators: redeploy the ReportHost image.

## [2026.8.23] — 2026-08-29

The menagerie release. Animals stopped sliding along the ground and started **moving like animals**:
a derived motion class per species — walker, crawler, flier, hoverer, swimmer — and real vertical
physics underneath it. Walkers jump a one-block ledge the way you do, crawlers and giants haul
themselves over without leaving the ground, two blocks is a wall, a drop is a fall with a terminal
speed, birds land to rest and take off when disturbed, hoverers never touch down at all. Nothing was
re-rolled, so every species in your world kept its identity — it just learned how it moves.

The other half of the release is a **conversation**. Since #618 you could send us a report with F1;
now we can answer it, in the game, in the world you sent it from — with a follow-up question if we
need one, and a "Fixed in version …" once the fix has shipped. And because a report should not need
an account to exist, the browser portal grew a **Play now** button: a name, one click, singleplayer
in the browser, no account, no hosted world.

Underneath both: **Lyxette's round 3.** Fifteen more reports from his singleplayer world — the compass
across the world seam, a ship blip that outlived the world it belonged to, crouching that stopped half
a block early, drop packets that hung in the air, wildlife spawning inside his walls, a ship that
parked in his own paved landing pad, water and lava you could not even aim at. All fifteen are in.
On top of that a full audit of everything merged this week (three rounds, ~35 further points), the
Guardian machines out of flat black and into grey circuit plating, and one long-standing oddity finally
explained: every cockpit canopy in the game was being rendered as **water**.

Protocol stays 3, saves migrate. Operators: the **ReportHost image must be redeployed** for the reply
channel, and the WorldHost image for the portal's solo entry.

### 🐾 Creatures move like animals — motion classes and real vertical physics (#1331, #1332, #1333, #1334)

- **Every species now has a motion class** — Walker, Crawler, Flier, Hoverer or Swimmer — derived from
  what the species already is, with no generator roll, so no creature in an existing world changed
  identity. The scan readout names the class.
- **Gravity, jumps and landings replace the old Y snap.** Until now a land animal was simply pinned to
  the surface height under it: no gravity, no jump, no landing. A pure vertical-motion state machine
  drives all of it now. A walker *jumps* a one-block ledge like the player does (launching before the
  step, not after it); a crawler or a giant hauls itself over the same step without leaving the ground;
  two blocks is a wall for both. Step off an edge and it is a real fall.
- **Fliers land.** A bird comes down to rest and to sleep, and takes off again when something disturbs
  it. **Hoverers never come down.** An amphibian swaps its class at the waterline. A winged land walker
  bounds in flat arcs instead of gliding.
- The client integrates the jump arc locally from additive network fields and runs a per-class animator —
  wings fold on the ground, legs tuck in flight, a landing squashes, a crawler undulates.
- Two long-standing gates fell out of the work: the water gate read the generator's pond *under* a real
  floor, so fauna was walled out of any platform built over water; and the swept body check sampled
  up-steps at the low Y, so land fauna could never climb a one-block step at all.

### 💬 The developers can answer your F1 report inside the game (#1327, #1328, #1329)

- **Every report now has a reply thread.** An answer shows up as a HUD line plus a window with the conversation
  and "Fixed in version …" once the fix shipped; when the developers ask a follow-up question, the same window lets you answer right
  there (up to three answers per report). The game only asks for replies while it remembers a report you sent
  in the last 90 days — a game that never sent feedback contacts nothing. Works on desktop, on play.bbts.de and
  in the glitch.fun arcade (where answers follow your Glitch install across releases). Operators get a
  conversation view + reply form on the ReportHost detail page and `POST /api/reports/{id}/replies` for scripts;
  reports sent by older game versions are answerable too (the reply key is derived from the stored player id).
- **A second answer on the same report shows up too (#1351).** Once you had acknowledged a reply, a later
  answer or follow-up question on that report stayed hidden until the next world restart; the game now
  tracks the individual replies it has shown, so every new one opens the window again.

### 🕹️ Play solo in the browser without an account (#1321, #1322, #1323)

- **Portal landing page:** play.blocksbeyondthestars.de now leads with a player-name field and **"▶ Play
  now"** — singleplayer in the browser, no account, no hosted world, no instance to wake. The account cards
  and the "playing with friends" steps follow below; *My worlds* links the same solo entry (also in its
  "no world yet" state). The name is remembered in the browser and handed to the game, because the browser
  world keys its player on it. All fourteen portal languages.
- **WebGL menu on a bare `/play` page:** Singleplayer comes first and the dead *Play* button (it could
  only dial 127.0.0.1) is gone; *Connect to a server…* stays as the manual fallback. Portal deep-links and
  glitch.fun keep their layouts.
- **No more silent "Explorer":** a `?singleplayer=1` start that knows no name first looks at the world it
  is about to restore — a world with exactly one player belongs to that player (second device, Glitch
  cloud save) and is adopted — and otherwise asks once, *"What is your name?"*. A deep-linked
  `player_name` is now persisted like a typed one, and the menu prefills its name field from the saved
  world when the settings are empty.
- **Docs:** README, user manual and the hosted-worlds notes no longer claim browser play is "online only".
- **The name lookup no longer eats the cloud save (#1355).** On glitch.fun the deep-linked start peeked at
  the cloud world to adopt its player name, and that peek marked the cloud version as "already synced" —
  so the boot right behind it fell back to the older local world. The peek is side-effect free now; the
  boot receives the newer cloud copy again.

### 🌊 Aim at water and lava — and blocks that fall (#1310, #1316, #1319)

- **A fluid is a thing you can point at.** Aiming stops at a water or lava cell you enter from outside it,
  as long as you hold something placeable or a fluid-capable tool (a tier-3 drill) — so you can finally fill
  a hole, cap a lava vent or scoop a fluid cell instead of the crosshair sailing straight through to the rock
  behind. A ray that *starts* inside a fluid still passes through, so swimming is unchanged.
- **Lava flows at half speed.** It now sits out every other fluid step, so a breached vent gives you time to
  react instead of racing you; the water quench still fires the moment lava wakes.
- **Sand, ash and snow fall.** Granular blocks settle instantly through air and sink one cell per step through
  a fluid (replacing it). They are only woken by something that actually happened — a block mined or placed,
  a blaster shot, a doused fire, a retracting fluid, or another falling block — so nothing churns in the
  background. Carved shapes survive the fall, dye, glow and paint travel with the block, ship interiors are
  untouched, and a block will not drop on a player standing in the landing cell: it waits.

### 🧱 Walled base areas keep the wildlife out (#1315)

- **Build a wall around your yard and the wildlife stays outside it.** Within a founded base's reach the
  server floods inward from the boundary of the base box through everything an animal could walk through
  (a shut door counts as a wall); anything the flood never reaches is enclosed, and no ground-bound species
  spawns there. Fliers, cave dwellers and hostile machines are deliberately not gated — a wall is not a roof
  and a fence does not stop a Guardian.
- The rule is told where you actually read it: in the base core's description, in VEGA's hint when you found
  a base, and in the Codex base article (EN + DE).
- **A sliding gate is a wall even while it stands open for you** (#1358): a proximity door opens only for
  players and closes on its own, so standing at your own gate does not let anything in.
- **Land animals around a base in a valley again** (#1347): the first version of the fill read natural terrain
  as masonry, so no land animal spawned in any hollow within 48 blocks of a founded base. The fill walks the
  terrain the way an animal does now — one block up or down is a step, two is a wall — so only real walls fence.
- The fence check works across the world's north–south seam, and the fill is refreshed when something changes
  inside the base box rather than on every spawn attempt (#1367).

### 🦌 Fauna respects what you built (#1320, #1314, #1325)

- **Nothing sleeps inside your wall any more** (#1320). A sleeping animal re-checks the cells its body occupies
  every tick and steps aside to the nearest clear spot — or despawns if it is boxed in completely. The ground
  probe demands a large species' real collision height in headroom and scans ±24 cells for actual ground before
  falling back to the terrain noise, and the "don't spawn in the parked ship" guard grows with the footprint of
  big animals, in every path that can move one (spawn, step, steer, push-out, placement sweep).
- **Nothing spawns inside a sealed room** (#1314). One reject list now covers the herd leader and every member
  of the herd, ending with the sealed-room check — so a herd cannot squeeze in behind its leader either.
- **No more monoculture** (#1325). Each species gets a share of the live creature cap (40 %, at least 3), herds
  spawn partially against it, a biome with fewer than two eligible species skips the native pass instead of
  filling the world with the one it has, and a species over its share sheds its farthest out-of-sight members.
- **An awake animal walled in by a block steps out** (#1357): only sleepers used to re-check their body cells,
  so a cathemeral grazer built into a wall stood in it for good. Every creature checks every two seconds now,
  and at once when a block is placed into it.

### 📦 Drops land, creature loot expires, and the hold says where the ore went (#1311, #1312, #1317)

- **A drop packet falls.** A drop that appeared in mid-air (a kill over a ravine, a block mined out from under
  itself) now falls up to 32 cells to the first cell with something solid or a fluid under it, instead of hanging
  where it was born. A packet created inside solid rock still surfaces upward first.
- **Creature loot has a lifetime.** Overflow from a kill spills as a loot packet that expires after five minutes
  — aged only while somebody is actually on the world, and checkpointed every 30 s so a quit does not reset the
  clock. Loot and mining packets never merge and have their own caps; **mining overflow stays immortal**, exactly
  as promised in #853.
- **The hold says where the ore went.** Banked asteroid ore and tractored salvage now raise one throttled,
  server-localized "+n Item → backpack / cargo hold" toast (EN + DE) instead of vanishing silently into storage.
- **Fresh creature loot no longer expires with an old bundle** (#1350): a kill next to an almost-expired packet
  merged into it and vanished with it seconds later; the merge restarts the five-minute clock.
- **A kill over a meadow leaves the loot in the grass, on the ground** — not floating a cell above it — and a
  quit or shutdown save writes every expiring packet's exact age (up to 30 s of ageing used to be lost) (#1367).

### 🛬 Touchdown on the real ground over a landing pad (#1318)

- **A ship no longer parks inside your own floor.** A 75-seed probe found no entombed pad spawn from generated
  terrain — pads are nudged flat and levelled to the median height. What the median ignored were the *real blocks
  a player built over the pad*: Lyxette paved his landing site, and ship and spawn ended up inside his own floor
  until the rescue dug him out. The pad surface is now the median raised over whatever really stands on the
  footprint (up to 8 cells) and never lowered, and every path uses it — ship placement, both pad spawns, the
  new-player spawn and the rescue fallback.
- **The dug-out rescue is readable.** The "we dug you out" / "we caught your fall" notice is mirrored into chat
  as plain localized text; the HUD toast has no lifetime and the next message simply overwrote it. It announces
  itself once per entombment rather than every second when the pad is walled in too (#1367).
- **`/tp pad N` lands on top of a paved pad**, and a months-old save no longer parks its ship on its own ghost
  hull for one session (#1367).

### 🧭 Compass wrap, stale ship blip, crouch edge, honest repair panel (#1307, #1308, #1309, #1313)

- **The compass honours the world's wrap** (#1307): across a longitude/latitude seam every blip — ship, waypoint,
  beacons, markers — flipped its bearing and the distance line jumped by a whole circumference (">3000 m" to a ship a
  few blocks away). Every marker is now measured against the same nearest-copy position all other world objects use.
- **No stale ship blip** (#1308): after leaving for a new world the compass kept pointing at the previous landing site,
  because nothing ever cleared the old placement. A world change clears it; a real placement re-arrives right after.
- **The ship blip survives dying** (#1346): a death in space, inside the ship or on another body respawned you
  at the heal tank without the HUD ship marker, distance, map marker and thermal blob — the client clears the
  marker on every world reset since #1308 and the two death-respawn paths never re-sent the placement.
- **Crouching reaches the edge** (#1309): sneaking stopped half a block before a drop, in the middle of the last block
  — building an outer wall face from its top needed scaffolding. The sneak probe is now a short look ahead plus this
  frame's step, so the capsule overhangs the edge the way sneaking does in every voxel builder.
- **An honest repair panel** (#1313): one missing hull cell on a full hull raised a "SHIP REPAIR" panel with a full
  Hull 100/100 bar and no word about what was wrong. With the hull full the panel now leads with the breach count and
  hides the bar; the bar and its numbers only appear while the hull itself is short. The missing cells are outlined on
  the parked ship in hologram blue while the panel is up, so you can walk straight to the holes (#1368).

### 🤖 The Guardian machines show their circuits (#1337, #1338)

- **Robots, scan-drones, space drones and UFOs are grey circuit-plated armour, not black silhouettes.**
  The planet robot and its hovering scan-drone rendered as an 8–18 % black shape (a flat 0.13 tint under the
  lit shader) and the space drone / UFO used the black `carbon` coal tile without the ambient lift the planet
  entities have had since #711 — hard to see against dark ground, caves and the night sky, and grimmer than the
  story needs. All of them now wear one new `enemy_robot` tile (#1338): dark-grey bolted panels with etched
  circuit-board traces, coarse enough to survive thin limbs, no lights. The glowing **red eyes, dome and threat
  lights stay** — red is still the Guardian signal (#601). The finale cruiser puts the same plating on its dark
  spine and engine blocks so the gauntlet reads as one machine family; its pale iron hull is unchanged.
- The tile carries its own brightness (the entity loaders do not lift tiles the way creatures do), so it was
  desaturated and lifted before bundling; without the tile the same greys render flat. The unused pre-retheme
  `enemy_hide` tile is gone. Lore docs no longer call the machines "black".

### 🪟 Cockpit glass is glass again — no more wobble on the canopy (#1372, #1373, #1374)

- **Every ship's cockpit canopy was being rendered as water.** A clear pane had a slow rippling warp on it,
  identical from inside and outside, and you could not really see through it. The clear-glass tile shipped with
  a fully transparent alpha channel — the image model answered "perfectly clear glass" with a see-through PNG —
  and the block shader reads a see-through tile as *"this face is water"*. So the canopy ran the whole water
  path: animated refraction, screen-space reflections, and an opaque final composite. Clear glass is now picked
  out of that branch explicitly, the tile ships opaque, and a test keeps block tiles from acquiring stray alpha.
- **Fire is flame-shaped again.** Fire's tile carries a real cutout, so it fell into the same water branch and
  came out as an opaque, warping square. Emissive blocks now take the energy-field path and keep their own
  silhouette.
- **Dyed glass and force fields no longer bob.** The water wave displacement read a dyed pane's blue channel as
  a wave height, so a blue-dyed pane physically sagged and oscillated. The displacement is gated on real water.

### ⏸️ The feedback dialog holds the world too (#1330)

- **F1 (F2 in the browser) now pauses like the Esc menu.** The player feedback dialog froze your controls
  but not the world: hunger kept draining, night kept falling and creatures kept hunting while you typed a
  bug report. Opening it now asks the server for the same hold the pause menu uses — in singleplayer the
  world stops right there (and saves, as every hold does); with others joined it only counts as "this
  player is in a menu" until everyone else is too (#973). Closing the form — Esc, Cancel or the auto-close
  after a successful send — lets the world run again. The screenshot is still taken before the hold, so it
  shows live play.
- Under the hood the Esc menu and the dialog share one `WorldHoldIntent` (intent, release and the 15 s
  keep-alive the server sweeps dead clients by), so the rule has exactly one copy — and a unit test.
- **F1 and Esc in the same frame no longer leave a stuck dialog** that held the world with no key able to
  close it (#1368).

### 🌊 Outline, held drill and placement ghost agree with the click on water and lava (#1353)

- **What you highlight is what you hit.** Since water and lava became aimable, the click stopped at a fluid
  but the selection box, the held drill and the slab/stair ghost still marched through it: aiming at lava
  with a mining beam showed no box (or one on the rock behind), a diamond drill held on lava never finished
  the cell (its second hit went to the rock behind), and a held slab showed no ghost over water. All three
  now ask for the same fluid-aware target as the click — and the tool check reads the definition of the
  fluid actually under the crosshair (water and lava separately) instead of assuming lava stands for both.
- **Doors and torches are no longer offered on water or lava** (#1368). The server refused a door in any fluid
  cell and a torch in water; the crosshair now skips those cells with such an item in hand instead of bouncing
  a reject toast.

### 📬 One F1 report, one inbox row — and answers that actually arrive (#1359)

- **The report inbox no longer lists every F1 report twice.** Each in-game report reaches the inbox on two
  paths by design (straight from the game, and as the server's rich `/bump` snapshot with the screenshot), and
  the admin list was meant to fold the pair into one row since #618 — but it compared a field the two halves
  never agreed on (the game sends the install token as its player id, the server the player name), so not one
  report ever paired. The list now recognises the halves by their shared reply key, or by player name for
  older builds.
- **A developer answer reaches you from either half.** The `/bump` snapshot now carries the same reply key as
  the direct upload, so an answer typed on the richer row (the one with the screenshot) arrives in-game like any
  other — before, it would have been stored under a key derived from your player name that your game never
  asks for. The inbox stops minting such name-derived keys for server rows (a public name must not unlock a
  thread) and clears the ones it had already made.

### 📮 Report inbox — polling for answers never blocks a real report (#1352)

- **A whole class behind one NAT can poll for developer answers and still send a bug report.** The reply poll
  shared the inbox's per-IP report limit (10/min), so 25 installs on one school network polling after world
  start ate the budget — and an F1 report from that network in the same minute was bounced (kept in the
  client's spool and re-sent later, but delayed). The reply routes now have their own per-install budget
  (`BBS_REPORTS_REPLY_PER_MINUTE`, 30/min per reply key); the per-IP limit guards report submission only.
  Operators: redeploy the ReportHost image.

### 📮 Report inbox leftovers — deleted reports are forgotten, the answer box stays, admin forms are guarded (#1369)

- **A report the developers deleted is no longer polled for three months.** The game asks the inbox about the
  reports it still remembers; the ones that are gone (deleted, expired, or filed by an older browser build under
  a key the game cannot read) are forgotten on the spot, and the polling stops once none are left.
- **The answer box no longer disappears when the developers re-file your report.** Whether you can answer now
  follows the conversation itself — the newest entry is a developer question you have not answered — instead of
  the report's status label, which an operator may change by hand.
- **The whole developer answer is readable** (#1368). The reply window used to cut a thread at 1,400 characters
  with no scrollbar and no hint; it now scrolls, however long the conversation gets, and shows developer and
  player text exactly as written (no stray `<b>` or `<color>` formatting from a tag someone typed). The HUD
  toast does the same.
- **Operators**: the admin forms carry a CSRF token (403 without it) and the JSON admin routes require a JSON
  content type; the detail page says "No in-game reply possible" for arcade reports filed before the reply
  channel existed (their reply key cannot match — answer those through the old channel). The ReportHost image
  also rebuilds when the shared reply-key code changes. Redeploy the ReportHost image.
- **Tooling**: `gen_textures.py` now applies the documented Guardian-plating post-process itself, so the tile can
  be regenerated reproducibly.

### 🌐 Portal — the French name field says what it means (#1354)

- **Locale text inside form attributes is encoded.** The solo-name field's placeholder *"Comment veux-tu qu'on
  t'appelle ?"* was pasted raw into a single-quoted attribute, so the French landing page showed a mangled
  field; the same pattern sat on the account, password, recovery-code and world-password fields. All of them
  go through a small attribute encoder now, and a test renders every portal page in all 14 languages and
  fails on the first attribute a translation breaks.
- **Browser solo entry** (#1368): the "What's your name?" prompt and the menu's name field are one and the same —
  Cancel no longer leaves the field empty while Singleplayer would have started with the name typed in the prompt;
  both cap at the server's 24-character limit so the name you keep is the name you play as; the prompt waits its
  turn behind the "What's new?" notes after an update instead of hiding under them; and the menu no longer
  re-reads the whole saved world on every rebuild just to find your name (a visible hitch with a large world).

### 🔒 Privacy page — what an in-game report stores, and how to have it deleted (#1329)

- The portal privacy page now describes the F1 (F2 in the browser) reports and the in-game reply channel in
  all 14 languages: what a report carries, that the reply key is a one-way hash of your installation (never
  your password or e-mail), that developer answers appear in the game and your typed answer is stored with
  the report, that reports stay until we delete them (no automatic expiry) and are not touched by deleting a
  portal account — and that an e-mail to the address on the page gets them removed.

### 🔧 Creature and physics polish from the week's audit (#1348, #1349, #1356, #1367)

- **Pets wait at a cliff** (#1348): a walker companion under a ledge it cannot jump hopped in place for as long
  as you stood above; a crawler companion levitated straight up the wall. Both now wait at the base (the leash
  still brings them along when you walk on); one-block steps are still taken.
- **Animals fall into a pit dug under them instead of rising through the ceiling** (#1349): the ground probe
  preferred the nearest floor in either direction, lifting a ground-floor animal onto the storey above.
- **No pop-out at the edge of view** (#1356): the crowding despawn shed animals 40 blocks out — in plain sight
  at larger view distances — and could remove a hunter mid-charge; it now respects the widest joined
  player's view distance and never sheds a creature that is hunting. The 70/110-block leash prune respects
  your view distance too (#1367).
- **Falling sand no longer rests on a tuft of grass, a torch or a flame** (#1367) — it crushes the prop (its drop
  lands on top of the settled block; a flame is put out) and falls on to the real ground. A doorway holds a
  falling block up like a player does; an animal the block lands on steps aside instead of being buried.
  **Weather snow that slid off a ledge still melts**, sand dropped on a thawing drift comes down with it, and a
  sand block you placed keeps your name after it has fallen (grief reports).
- **More creature physics** (#1367): a titan is no longer hauled onto a tree crown because of a tuft of grass in
  its column; a bird whose perch is dug away falls and takes off again; an animal never jumps into a low ceiling;
  falls have a terminal speed (no more 2.7-block snaps); walkers no longer step down onto lava; a swimmer beside
  a cliff overhang keeps swimming instead of steering out of its lake.
- **More still** (#1368): a winged giant no longer floats through its hop (the client uses the server's own
  ground-bird rule for the glide instead of "has wings"); a perched bird whose perch is dug away falls with its
  wings out, and the server reports the fall as such; and the per-creature animator lookup that ran every frame
  is done once.

### 🧪 Under the hood (#1362)

- The report-host test fixture no longer looks like an 80-second network stall. It was xunit's parallel queue,
  not I/O: one in-process ReportHost per test class, every request timed, and the loopback HTTP tests moved into
  a real-time-sensitive collection so the slow first Kestrel start stays off the fast-tier clock.
- **The browser build compiles again.** The WebGL report transport (#1339) parsed the inbox answer with
  `System.Text.Json`, which Unity's runtime does not ship; only the release-tag WebGL build compiles that file, so no
  PR build caught it. It reads the one field it needs with `JsonUtility` now (#1377).

## [2026.8.22] — 2026-08-26

The outfitter release — and, more than any release before it, **one player's release**. Between 25 and
26 August, **Lyxette** sent in four reports from his singleplayer world: nine bugs, a fall-through on a
beam pad, seven "why can I not see what my gear does?" observations and one honest question about lava
and water. That became 20 issues, and every single one of them is fixed or built in this release.

So: everything you carry finally says what it does. Suit gear has always worked from anywhere in the
backpack and nothing ever said so — now there is an **Inventory → Suit** tab that shows exactly which
passive gear is doing the work, and every piece of it ends its description with "Works while carried."
Ship modules stop pretending to be a build catalogue: fitted ones say **Fitted**, come first, are listed
per ship on the Fleet tab, and — new — can be **taken back out** at 50 % salvage. The crafting menu grows
a **Machines** tab so Blocks is blocks again, glass gets one rare, blueprint-gated **clear** exception
(and every cockpit's forward pane is made of it), water quenches lava on contact, your base core tells you
how much air it holds, and nobody stands inside anybody else any more.

Alongside Lyxette's twenty: the tech tree finally chains instead of standing in eight separate columns,
every workstation has at least three things to make, the game can run out of a portable folder, and a full
completeness audit of the deepening package closed seventeen more issues.

**Protocol stays 3** and saves migrate additively. This release adds **one new network message**
(uninstall a ship module, tag 234) and a few additive fields (a ship's fitted modules, the base core's air
counts) — an older client on a new server simply cannot remove modules and does not see the air readout.
Hosts: content data changes (blueprints, recipes, ship layouts, cargo module tiers) and two save-shape
rules move server-side (base settlers are keyed by base id and deduped on join; factory rosters are pinned
at first stamp), so update the server to hand out the new ladder.

### 🎒 Suit gear and ship modules you can actually see (#1270, #1271, #1268, #1269)

All four asked for by **Lyxette**.

- **Inventory → Suit.** A filter view of the backpack showing only suit gear (armour plates, oxygen tanks,
  thermal liners, scanners, the jetpack) — the gear keeps its normal slots, nothing is equipped or locked
  away. A status line sums it up: *Armour x % · Max. oxygen n · Insulation y %*, plus a one-line version on
  the Backpack tab. The four effects (armour stacks up to 0.75, best tank and best liner only, scan
  multiplier) now live in one shared `SuitEquipment` that client and server both read, so the number on the
  screen is the number the server uses. (#1270)
- **"Works while carried."** Twelve gear descriptions end with that sentence in all 14 languages, and the
  manual no longer claims the jetpack fires "if equipped" — there are no equip slots and never were. (#1271)
- **The HUD oxygen bar uses your real maximum.** It divided by a flat 100 regardless of the tank you were
  carrying. (#1271)
- **Ship modules: Fitted, and one place that says how much fits.** The Modules tab used to offer *Build* for
  a module already aboard (which the server then refused) and had no answer to "how much can this ship
  hold?". Now fitted modules come first with a **Fitted** badge, a fit summary explains the one-of-each rule
  and the salvage rate, and the **Fleet tab lists each hangar ship's fitted modules**. (#1268)
- **Uninstall with salvage.** Any non-mandatory module that is not part of the hull (workshop, medbay,
  quarters and the basic hold stay welded in) can be removed at the ship workshop for **50 % of its parts
  back**. A cargo expansion only comes out while the remaining hold still fits every stack — otherwise the
  button says so instead of eating your cargo. No transfer between ships yet. (#1269)

### 🔨 Building & crafting — a Machines tab, one clear glass, and lava that hardens (#1273, #1274, #1283, #1284)

All four from **Lyxette**'s reports as well.

- **A "Machines" crafting tab.** 21 device items (terminals, tanks, refiners, scanners, the sentry post and
  friends) moved out of Blocks into their own tab with its own icon. Factory housings, doors, lights, beds
  and the campfire stay under Blocks. (#1273)
- **`glass_clear` — the one exception to frosted glass.** Rarer and blueprint-gated (Production, 24
  knowledge): 2 glass + 1 polymer at the workshop makes 2 panes. It builds like glass — airtight, dyeable —
  but it is genuinely see-through (alpha 0.22, no frost) and it is not shapeable. Regular glass stays
  frosted by design. (#1274)
- **Every cockpit has a clear windscreen.** On all seven layout ships (and the box starter) the forward
  pane(s) — glass on or ahead of the cockpit row with nothing of the ship in front of it — are now
  `glass_clear`. Side and rear windows stay frosted, so the ship still reads as a ship from outside. (#1283)
- **Water quenches lava, from a bucket too.** Lyxette put a water block next to lava and watched the two
  sit there side by side forever: the #477 contact rule only ever fired for *flowing* fluid. Now lava
  hardens wherever water touches it — source lava to **obsidian**, flowing lava to **basalt** (his call) —
  on placement, on every woken cell, and where a lava tongue tries to flow in. Placed lava beside water
  hardens itself. (#1284)

### 🏠 Bases and the people in them (#1267, #1272)

Both from **Lyxette**, who could not find the sealed-room air system that had been in the game since #782.

- **The base core tells you how much air it holds.** Aiming at your own core shows *"Air: n cells in k
  sealed room(s)"* (or *"No sealed room yet"* with a hint), a **here: air** overlay, and the rename key. A
  VEGA once-hint fires when you found a core on a non-breathable world, door descriptions now say whether a
  hinge, slide or wooden door leaks, and there is a new Codex article, *Bases on Airless Worlds*. (#1267)
- **People keep their personal space.** NPCs nudge apart when they come closer than about 0.8 m to each
  other — settlers, traders, guards, standing NPCs included. Only creature flocks had separation before, so
  a settlement's residents used to stack into one another. (#1272)

### 🐞 Ten bugs from Lyxette's world (#1259 – #1266, #1275, #1276)

All played on v2026.8.21, Windows singleplayer:

- 🛏️ **Heal-tank head in the ceiling lamp.** Every layout ship put its heal tank *on* the medbay block, right
  under the #779 lamp — the starter was fine, the hauler was not. It now picks the walkway cell beside the
  medbay first. (#1259)
- 🚀 **Switching ships left the old hull/shield on the HUD** — the combat status was never re-sent. (#1260)
- 📦 **Cargo numbers were a lie, and there was no upgrade path.** `cargoSlots` in `ships.json` was dead data
  (the hold is the sum of the fitted modules), so the hauler advertised 96 and had 72. One number now
  (`StartCargoSlots`), plus two new tiers: **cargo_hold_2** (+32) and **cargo_hold_3** (+48) behind the
  matching expansions. (#1261)
- 👤 **Renaming a base spawned a duplicate settler** — the settler was keyed by a hash of the base *name*.
  Now keyed by the base id; existing saves are deduped on join. (#1262)
- 🔫 **A second weapon module was never selectable** — the space quick-bar stopped at the first one. Every
  fitted weapon gets its own entry now, labelled by module name. (#1263)
- 🧰 **Crates refused blocks.** They accepted only materials and components, so every placeable block
  bounced. (#1264)
- 🏭 **The Factory tab lit up but every craft failed** — the station gate accepted the craftable terminal
  *block* instead of the real factory terminal. (#1265)
- 🪟 **The glass tooltip promised a clear pane** for glass that is frosted by design. (Now there is a clear
  glass too — see #1274.) (#1266)
- 🛰️ **A fleet with one unreadable row silently dropped you back to ship one.** Ids are kept, a warning is
  logged and you get a *ship unavailable* notice. (#1275)
- ✨ **Beaming in could drop you through a one-block slab** whose chunk had not meshed yet — the settle
  release accepted any collider within 10 m; after a server floor snap the ground ray must now hit within
  1.6 m. (#1276)

### 🌱 Content — the tech tree chains up, every station has something to make (#1202, #1203)

- **Eight leaf blueprints now hang off a parent** instead of sitting in the tree unconnected (all
  cost-monotonic, nothing was removed), and three new research nodes were added whose unlock costs are
  material sinks in themselves: **Field kitchen** (10 knowledge), **Archaeology** (25) and **Bio-refining**
  (45). (#1202)
- **No more one-recipe workstations** — every station now offers at least three things. The campfire gets a
  kiln side (char wood → carbon, melt ice → water, boil salt, six torches from a log) and, behind Field
  kitchen, three real meals: **hearty stew**, **algae soup** and **mushroom skewer**. The algae tank refines
  (biofuel, plant fibre, polymer) and the detoxifier washes toxic berries edible, filters mud into water and
  turns mushroom parts into forage bait. Obsidian melts back into glass, ancient brick makes concrete and
  researchers buy rune stones — four items that previously had no use at all. (#1203)

### 💾 Portable data folder (#1285)

- An optional `portable_data_dir.txt` next to the game executable redirects the whole persistent-data root
  (settings, name token, singleplayer saves, user content, exports, spools, photos) to the folder it names
  — empty file = `userdata` next to the executable; relative paths anchor at the executable, `%ENV%` is
  expanded, `#` starts a comment. Without the file nothing changes; an unwritable target falls back to the
  default with a `Player.log` warning. Not for the browser build.

### 🔇 Moderation and map (#1290, #1293, #1294, #1295, #1296, #1297)

- **Mute from the Alliances tab** — every player row (Find players, Allies, Crew) carries a Mute/Unmute
  button; same list as Settings → Muted players and `/mute`. (#1290)
- **Shared map markers stay while their owner is offline** — the family meeting point no longer vanishes
  when the kid logs off; the list refreshes the moment an alliance forms or ends, a crew changes, or a
  player leaves the world (their pings go with them). (#1293)
- **Chat mutes survive a reconnect** — an automatic cool-down or an admin `/silence` is tied to the player,
  not the connection; `/unsilence` also works while the player is away. Still RAM-only. (#1294)
- **The chat screen catches social handles** — `@name` (3–32 characters) counts as personal information like
  phone numbers, e-mails and links: masked in Filtered, dropped in Safe. (#1296)
- **Feeding happens in person** — the companion must be on your world within six blocks; the Feed button
  greys out otherwise. (#1295)
- **Scouts at the gate need Bandits and Planet enemies** — with hostiles off the option is hidden and nobody
  comes. (#1297)

### 🔎 Completeness audit of the deepening package (#1287 – #1303)

A read-only audit of all 28 merged slices of epic #1197 — one reviewer per area over the implementing
commits, the issue acceptance bullets, a data cross-check and the tests — found four real bugs, a set of
unmet acceptance bullets, and test holes exactly where the bugs sat. This is the whole follow-up round:

- 🎮 **The pad reticle in pointer minigames was invisible** — the virtual cursor's disc was drawn at the
  corner for every position, so the nine pointer-driven arcade games were blind on a gamepad. (#1287)
- 🎮 **B / Esc in a minigame round no longer closes the whole Tab menu.** (#1288)
- ⌨️ **On-screen keyboard** masks password and PIN fields with bullets, accepts digits only in number fields,
  and hands focus back to the field it was opened from. (#1289)
- 📋 **SPS survey orders** — "travel to a system without a relay" now really excludes the system the order
  was taken in (the station's system was never resolved), and a chain step that is impossible on this world
  (no station with an open relay left to build on, hostiles off) is skipped instead of stalling the chain.
  (#1291)
- 🛡️ **Sentry kills count for the base owner** — a scout the sentry finishes still counts towards *Guard the
  homestead* and the base-defended tally; bandit and machine bounty steps progress too. (#1292)
- 🔧 **Disassembly** no longer turns campfire outputs (carbon, salt) back into wood or water, and an item that
  would salvage nothing is kept instead of vanishing. (#1298)
- 🏭 **Factory rosters are pinned** in the placement record at first stamp, so a growing recipe set only
  reaches factories stamped after the change; existing (and claimed) factories keep what they made. (#1299)
- ⚖️ **Factory balance** — `factory_diamond` takes 4 ore, `factory_light_alloy` 6 aluminium ore (never a
  better per-ore yield than the refinery route), and researchers pay 1 data fragment per uranium bar. (#1300)
- ⛵ **Boat** — "that isn't your boat" says *boat*, the ashore snap-back only returns you to a spot the boat
  was actually seen floating, and it arms the same post-teleport guard as other server moves. (#1301)
- 📖 **Docs** — `/report` works for browser guests, blocked chat lines do not ping the operator by
  themselves, voice is push-to-talk (listening on for LAN games, off on hosted worlds), and the manual links
  the parents page. (#1302)
- 🧪 **Tests** — every fix above carries its own, plus refill ×2 and "a haven keeps its raiders" (#1206), the
  validator problem string and the hostile-scan gate (#1205), the camp-sync target filter (#1212), name
  screening for station/beam/crew/marker (#1221), sentry vs. camp guard, boat unloaded-chunk and lava, and
  `MissionBoardTests` asserts instead of scanning seeds. (#1303)

The gamepad fixes still want an on-device pass (#1227).

### 🙏 Thanks

**Lyxette** — twenty issues in two days, every one of them precise enough to fix from the report alone,
and half of them things nobody on the inside had noticed were missing. This release is largely yours.

## [2026.8.21] — 2026-08-25

The deepening release — 26 slices of the feature-deepening package (epic #1197) plus the #1048
content-validation round and the latest player-report fixes, shipped in one go. Chat is safe for a
class of strangers (filter, anti-spam, mute, `/report` for browser guests, `/silence` for admins),
a gamepad reaches every menu, every text field and all 20 arcade games, missions grow chains and
survey jobs, beating the Guardian no longer empties the galaxy, your base gets a sentry post and
(opt-in) scouts at the gate, companions fetch, warn and bond, there is a boat, crews and shared map
markers, two cultivated crops, a second buyer for every ore, and the research tab is finally called
"Blueprints". A parents page says what the game contains.

**Protocol stays 3**, so old and new still connect, and saves migrate additively (crews, markers,
companion bond, boat kind, the new world rules). This release does add **seven new network messages**
(sentry tracer, companion feed, crews, map markers) — update client and server together to get all of
it; an older client on a new server simply does not see crews, markers or the sentry's shots. Hosts:
the chat filter and anti-spam run server-side with sensible defaults (`ChatMode` Filtered, operator
`--chat-filter` Mask) — nothing to configure unless you want Strict or Off.

### 🛡️ Chat safety — filter, anti-spam, mute, report, silence, and a page for parents (#1207, #1208, #1209, #1221, #1222, #1223, #1226)

- **Server-side content filter.** Every chat line runs through one screen that folds case, diacritics,
  leet, repeats and Cyrillic/Greek look-alikes, matches whole words (German compounds pass), and masks
  phone numbers, e-mails and links. New world rule **Chat mode** (Open / Filtered / Safe — default
  Filtered, family presets Safe) and an operator override `--chat-filter` / `BBS_CHAT_FILTER`
  (Off / Mask / Strict) plus `BBS_CHAT_*_WORDS` lists. Never silent: a blocked line tells the sender
  why, and the line itself is never logged. (#1207)
- **Anti-spam with a temporary auto-mute.** More than 6 lines in 10 s, or more than 3 filter hits in
  5 min, pauses that sender's chat for 10 minutes — told once, with the duration; the operator gets one
  ping; only chat pauses, the player keeps playing. RAM-only, gone on reconnect. (#1208)
- **Mute a player — text and voice.** `/mute <name>` / `/unmute <name>` in the chat box, and an *Unmute*
  list under Settings → Muted players. Purely client-side (the server is never told), checked before
  the voice decode so a muted speaker costs nothing; the same list hides the player in chat and in the
  Alliances tab's radio mirror. (#1209)
- **Screen names and AI text.** Base, station, beacon, beam-pad and companion names now run through the
  same screen as chat (a masked name is refused; a beacon or pad with a refused label is placed with
  an empty one instead of getting stuck), and every text from the AI backend is screened once at the
  provider — a refused text falls back to the authored line. (#1221)
- **`/report Player [note]` without an account.** The server handles the report itself — no portal
  session, no equipment — and attaches the reported player's last 10 relayed lines plus both arcade
  install ids, so glitch.fun guests on a public world finally have recourse. Capped at 3 per 10
  minutes. (#1222)
- **`/silence Player [minutes]` / `/unsilence`** for admins: a chat pause (default 10 min, at most a
  day) instead of only a kick, using the same mute the anti-spam does. Fleet admins cannot be silenced.
  (#1223)
- **For parents.** `docs/user/PARENTS.md` (+ `PARENTS.de.md`): what the game contains (mild sci-fi
  combat, no blood, bandits chased off, tools-only weapons by default, no purchases, ads or
  gambling), what online play involves, every built-in safeguard, the private-family-world
  recommendation and an honest PEGI 7 / USK 6 self-assessment — explicitly *not* a certified rating.
  `AGE_RATING_CHECKLIST.md` prepares the storefront questionnaire; the in-game house-rules Codex
  article gained the content profile. (#1226)

### 🎮 Gamepad — every menu, every text field, every minigame (#1198, #1211, #1218, #1219, #1220)

- **The Tab menu was unreachable by pad** — the stick navigation walked a canvas that held none of the
  11 tabs. Fixed, and the same gap closed in the Arcade, blueprint tool, beacon label, beam pad and
  both editors. Esc/B and Tab/Start are proper actions now (keyboard key fixed so nobody strands
  themselves in a modal, pad column rebindable), B backs out of the main menu, settings, world picker
  and editors, and in-game B stays crouch. The ship and face editors are fully pad-operable: Start
  swaps panels ↔ work surface, left stick flies, right stick looks, A/X/Y place/remove/turn. (#1198)
- **D-pad up/down and the triggers** are read at last: d-pad up opens the chat, d-pad down turns the
  held block, the triggers mine/place (ship off by default — the trigger axis is the one reading that
  differs between XInput, Proton and the browser), and holding LB/RB halves the look speed for
  precise aiming. (#1220)
- **On-screen keyboard.** With a pad in hand, A on any text field (63 of them, no screen opts in)
  opens a QWERTY grid with digits, umlauts and a symbol page; the result lands in the field as if
  typed. One press of B closes exactly one thing. (#1211)
- **Controller settings page.** Dead zone, pad look speed X/Y, invert Y, mine/place on the triggers,
  Xbox / PlayStation / Nintendo button names, and a vibration switch that honestly says it does
  nothing yet. Every default is what the code used before. (#1219)
- **All 20 arcade games on a pad.** D-pad/stick → arrows, A confirm, B cancel, X secondary, Y help,
  Start pause, Back restart — and for the nine pointer-only games a virtual cursor glides across the
  canvas with A to click and drag, no per-game code. The manual gained its first real Arcade section.
  (#1218)

### 📋 Missions & endgame — scan jobs, chains, and a galaxy that survives the finale (#1199, #1205, #1206, #1212, #1213)

- **The Scan objective lives.** Survey jobs on every settlement and station board (creature, block,
  flora, tree, monument, microfauna, asteroid, anomaly), knowledge as a mission reward, and objective
  rows that finally name their type and target ("Scan · any creature 2/3"). (#1205)
- **Mission chains.** Settlement and vendor chains with "Part 2 of 4", dialogue choices that hand you
  a mission, a big order every third turn-in, and a radio nudge. Additive schema, old saves untouched.
  (#1212)
- **Remnant Protocol.** Defeating the Guardian becomes a factor rather than a switch: half the planet
  cap, twice the spawn pause, scan-drones only, raiders and ambushes confined to pirate havens —
  the galaxy stays alive and the raider bounty stays earnable. (#1206)
- **SPS Survey Orders.** After the ending, station boards post a repeatable four-step relay-survey
  chain: scan two anomalies, visit a system the relay net hasn't reached, feed circuit boards into a
  station being converted, drive off three remnant machines. New objective type *Contribute*. (#1213)
- **Every village keeps its mission board** — the board plot is exempt from the plot-skip roll (new
  worlds only). (#1199)

### 🏠 Base life & companions — sentry post, scouts at the gate, fetch and bond (#1210, #1214, #1224, #1225)

- **Sentry post** (workshop, blueprint after the heal tank): a stateless turret that fires at the
  nearest hostile within 14 blocks with line of sight — no power, no ammo, nothing persisted. Never
  targets players, tame creatures, NPCs or a robber still talking, no-op on Creative/Peaceful. Counts
  as a machine toward your settler. (#1214)
- **Scouts at the gate (opt-in).** New world rule *Base visitors* (off everywhere, on in the
  dangerous preset, shown under the bandits slider): when you are home, two bandit scouts may walk
  up to the zone edge, stand a minute and leave — never inside, never taking anything. Fight one and
  you earn the *base defended* counter and the "Guard the homestead" bounty. Old saves stay off.
  (#1224)
- **Companion payoff.** A tame companion fetches dropped packets, warns of hostiles with an amber "!"
  (and a guard growl), stalls a robber, wards off bandits at high bond and, when penned, produces its
  species' drop every ten minutes. (#1210)
- **Feed & bond.** Feed any of the three baits (+5 bond, cap 100, 60 s cooldown); bond decays one
  point per real day down to 40. Tiers: 50 wider fetch, 70 the bandit ward, 90 a scouting hint every
  five minutes. Companions tab shows a bond bar with tier ticks and a Feed button that says why it is
  dimmed. (#1225)

### 🚤 The boat (#1215)

- A water vehicle as a second kind of the speeder system: workshop recipe **without a blueprint**
  (8 wood logs, 2 iron plates, 1 cable), handed out for free on an ocean-type start. Launch needs
  water ahead, it sits on the waterline with a bob and a wake, never burns fuel, and if you run it
  aground for ~3 s the server sets you back onto the last wet spot. Old saves need no migration.
  Boat item art, engine loop and splash generated with the project's own tools.

### 👥 Crews and shared map markers (#1216, #1217)

- **Crews.** A named group of up to 8 players whose membership implies alliance — bases, beam,
  factories, stations, teleporter all just work. Owner invites (online players only, no join codes),
  kicks, renames, disbands; anyone may leave; an owner leaving hands the crew to its longest-serving
  member. New *Crew* view in the Alliances tab, persisted in additive tables. (#1216)
- **Named map markers & ping.** Up to 8 markers per player per world with label, one of 8 icons, one
  of 6 colours and a *shared* flag visible to allies and crew mates on the same body; a *ping* (default
  **C**) pulses a "look here" for 30 s. Map pins, marker list with Navigate/Delete, compass blips.
  (#1217)

### 🌾 Two crops, and a second buyer for every ore (#1200, #1204)

- **Grain and mushroom bed** join the cultivated flora with seeds and hand recipes that close the
  harvest → sow loop; settlement greenhouses and station bays now grow one of several crops. (#1204)
- **Every ore has at least two consumers and two stations:** eight factory raw-ore recipes (light
  alloy, bronze, brass, power cell, carbide, magnet, diamond, reactor fuel), market barter for
  uranium, lead, silver and diamond, sinks for silver, bronze, brass and sulfur — recipes only, and a
  generalised economy test keeps it that way. (#1200)

### 📘 The "Tech" tab is now "Blueprints" — with your knowledge balance and an "Enough knowledge" filter (#1191)

- **Renamed.** The research tab in the ship interface (and the matching Codex chapter) is called
  **Blueprints** in every language — which is what the game has always called the things you research
  there. VEGA's tutorial line and the cockpit hint say so too.
- **Knowledge at a glance.** The tab's filter row shows your **knowledge points** and the **data fragments** you
  own; every card shows *Knowledge have/need*, and a node that only lacks knowledge now says **Knowledge
  missing** instead of "Materials missing".
- **"Enough knowledge" toggle.** Hides every blueprint your knowledge does not cover yet (and the ones already
  researched), so "what can I spend my knowledge on?" is one click. Client-only; saves and servers untouched.

### 🛠️ Player-report round: ship switch, base settler, macOS network fallback, credits (#1247, #1248, #1250, #1251)

- **Switching ships while landed** no longer leaves you inside the new hull's wall — you are moved to the new
  ship's heal-tank — and the cargo hold shows the new ship's contents at once instead of the old ship's until
  you stepped out and back in. Reported by **Lyxette**.
- **Base settler** — the settler who moves into your base now picks a free spot next to the core instead of a
  fixed one that could be inside something you built, and moves out again if you later build over it.
  Reported by **Lyxette**.
- **macOS** — the client pins its working directory at start-up and the network codec falls back to its JSON
  format if the MessagePack formatters cannot be built; before, a Mac launched from a folder the app may not
  read could not send a single network message. Reported by **sasas**.
- **Credits** — Bastian (Linux playtest), Lyxette and sasas join the Playtesters in the README and the in-game
  credits (all 14 languages).

### 🧪 Content validation, save hardening, CI and docs (#1048, #1187, #1188, #1190, #1194, #1196, #1228, #1232, #1254)

- Persisted inventory stacks are clamped to each item's max stack on load; recipes with amounts below 1
  or that consume their own output fail validation; unreadable user templates are reported instead of
  skipped silently; atlas bounds, stack size and surface rules are validated with migration tests; the
  corrupted-block-palette migration is covered. Closes the #1048 content-validation round
  (contributed by **ahmdkaml**).
- The machine locales caught up on 81 missing keys — every objective row in the mission log has a type
  label in all 14 languages again.
- README and CONTRIBUTING link the starter issues, show a .NET-SDK-only quickstart and say who signs
  the CLA when an AI agent wrote the code. (#1232)
- The PR test gate runs on 6 balanced shards with refreshed weights and a drift guard — tail shard
  7:36 → 5:07. (#1254)

## [2026.8.20] — 2026-08-22

The encore release. The soundtrack stops repeating itself — every track plays once before anything comes
around again, the score takes a breath between pieces, reacts to storms, predators, deep dives and the
star chart, and grows by eleven new tracks (51 in the library). In the browser, your singleplayer world
now survives a glitch.fun release and can finally be started over with a **"New world…"** button. And the
research toast says what it actually means while the early knowledge ladder gets some air.

**Protocol stays 3**, no new network messages, saves untouched. The only server-side change is data
(blueprint knowledge costs, #1184), so hosts should update to hand out the new ladder — older servers
and new clients still connect fine.

### 🎵 The music stops repeating itself (#1172, #1173, #1174, #1175, #1176)

- **Shuffle bags instead of coin flips.** Every track of a context now plays once before anything repeats,
  the all-round beds are blended into every planet's pool at a minority share (so a two-track biome no longer
  alternates A-B-A-B while still sounding like that biome), and the music remembers what it played recently
  across contexts. Dawn brings the sunrise track, night the nocturnal one — on every biome, not only the
  generic planets. (#1172)
- **The music breathes.** After a track ends on a planet, in space or aboard the parked ship, the score may
  rest for a minute or three — only wind, rain, the biome bed and the nearby waterfall remain — before the
  next piece fades in. The menu, loading screen, stations and the finale never go quiet. (#1173)
- **The music listens.** A storm pulls it down under the weather, a hostile creature close by ducks and
  darkens it, a long dive switches to the deep-water bed, the open star chart and the crafting / research
  tabs bring their own beds, and the first landing on a planet opens with the sunrise track. (#1174)
- **Eleven new tracks.** Third variants for the ice, desert, lava, toxic, ocean and cave planets, the
  station hub, the menu and the loading screen, plus a second dawn and a second night piece — composed with
  the ElevenLabs Music API (`tools/ai-assets/gen_music.py`); the library now holds 51 tracks, still streamed
  on demand. (#1175)
- **The Synth style is generative.** Instead of four fixed 10–24 s loops it now composes every piece fresh —
  mode, tempo, chords, arpeggios, timbre — 40–110 s long, and every ice planet still sounds like ice (its
  root and mode are fixed per biome). Every piece is levelled a few dB under the track library, so the Synth
  style is never the loud one. It is also what you hear when a track file is missing. (#1176)

### 🔬 The research toast says what it means — and the early knowledge ladder is no longer flat (#1184)

- **"Enough knowledge for: X"** replaces "New research available!" on the HUD toast (all 14 languages). The
  toast fires when a knowledge gain crosses a still-locked blueprint's threshold — prerequisites and research
  materials may still be missing, and the old wording read as "blueprint unlocked" (a fresh multiplayer
  session showed it burst after a few scans and two landings, although nothing had been researched).
- **26 blueprints that cost 2–10 knowledge now cost 4–24**, same relative order, spread so no knowledge
  value carries more than two root blueprints (`paint_tool` 4 … `oxygen_generator` 24). The ≥ 30 band from
  #862 is unchanged, every prerequisite chain still climbs. Banked knowledge and unlocked blueprints in
  existing saves are kept. Data + locales only.

### 🌐 Browser singleplayer survives a glitch.fun release — and can be started over (#1177, #1178, #1179, #1181)

- **"New world…" in the browser menu.** The one-world browser singleplayer could never be reset — the menu
  always continued the saved world (and with it, nobody could replay the first-spawn prologue). A new
  button next to Singleplayer asks for confirmation, then deletes the world saved in this browser and
  starts a fresh one. It holds: a reset marker keeps the new deployment-storage migration from adopting
  an older copy and keeps Glitch Cloud Save from restoring the cloud copy at boot — the fresh world
  replaces it on its first save. Name, settings and the cloud-version meta stay. Localized in all 14
  languages. (#1181)
- **Your world follows you to the next deployment.** Every build uploaded to glitch.fun is served from a
  new content path, and the browser's Unity storage is scoped to that path — so guests (players without a
  Glitch login) started every release on a fresh world. The client now adopts the singleplayer world (and
  its cloud-version meta) the previous deployment left behind in the same browser storage, as well as the
  settings (player name, language, intro flag). Best-effort and safe: never overwrites a world the current
  deployment already has, never deletes the old copy. Logged-in players keep getting their world from
  Glitch Cloud Save as before. (#1177)
- **The menu says where your world lives.** On glitch.fun a hint next to the Singleplayer button explains
  that the world is saved in this browser and that logging in on Glitch keeps it across updates and
  devices — localized in all 14 languages. (#1178)
- **Settings are flushed to IndexedDB immediately.** `ClientSettings.Save()` now syncs the browser file
  system after every write (shared `WebGlStorage` helper, also used by the world save), so the remembered
  name and settings no longer depend on a later world save happening to sync them. (#1179)

### 📚 Docs

- The on-demand music streaming from 2026.8.19 is now described everywhere the music library is
  documented (developer docs, sound design, user manual). (#1171)

## [2026.8.19] — 2026-08-22

The featherweight release — a small, focused follow-up to the constellation release. One thing
changes, and it changes the browser experience completely: the WebGL player's first visit drops
from **~208 MB to ~40 MB**, because the music library no longer ships inside the player data file
but is streamed track by track when it is actually needed. glitch.fun and the fleet's `/play` page
are the beneficiaries; desktop installers just get a little smaller.

No server or protocol change: **protocol stays 3**, no new network messages, saves untouched.
Hosts do not need to update for this one (but may).

### 🎧 The browser loads five times faster — music streams on demand (#1167)

- The WebGL first visit (glitch.fun and the fleet's `/play`) downloaded ~208 MB before the first
  frame — 164 MB of it the 40-track Suno music library baked into the player data file, even though a
  session hears three to five songs. The tracks now ship as plain MP3s next to the player and are
  **streamed the first time their context comes up**: the first visit shrinks to roughly 40 MB (10 s
  instead of ~50 s on a normal day, ~2 min instead of ~11 on a slow one), Glitch's "Syncing assets"
  bar no longer stalls at 92 % on one giant file, and the browser releases tracks it is no longer
  playing instead of keeping every decoded song in memory. A track starts a moment after you enter
  its context (instantly from disk on desktop); the next re-roll is prefetched while the current
  track plays. Desktop builds are unaffected apart from being a little smaller. (PR #1168)
- Browser follow-up found on the first glitch.fun test deploy: the browser hands a streamed clip
  over **before it is decoded** (length 0), which made the player think the track had already ended
  and immediately download a second one. Streamed tracks now wait until they are really decoded
  (60 s cap; a clip that never decodes is dropped from its pool like a missing file) — verified in
  headless Chromium against the glitch.fun CDN copy: one fetch, no re-roll. (PR #1169)

## [2026.8.18] — 2026-08-21

The constellation release — and the biggest one yet. One question drove it: *what keeps you playing
after hour five?* The whole progression epic (#1101, 28 issues, 14 feature PRs plus two audit
rounds) ships at once. Builders get missions, whole-build share codes and coloured light; explorers
get a map that remembers, a frontier that pays and three one-of-a-kind places; story players get
fragments they can actually find, characters who talk back, and an **ending**. And past the ending,
the relay network opens the true late game: convert your stations into relays, span jump lanes,
and watch the galaxy grow at the edge because *you* linked it.

**Protocol stays 3**, but this release adds seventeen new network messages (210–226: story
objectives, explored map, share codes, relationships, story resolution, relay network, dialogues)
— hosts and players should update **client and server together**. Saves are fully compatible: all
new state is additive, and placed structures now *pin* their template so existing worlds never
change shape under you.

### 🏆 Progress you can see (#1102, #1103, #1104, #1105)

- Late-game achievements join the tab, progress figures show how far along you actually are
  (tech %, journey stats), and the last knowledge faucets are wired up so every activity pays
  somewhere. Story beats award their bonus knowledge exactly once per save, tracked as milestones.
  (PR #1130)

### ⚗️ Every material earns its keep (#1106, #1107, #1108)

- **Reactor fuel is a build cost** of the big end-game builds — Thunderbolt (2), Hammerhead (3),
  Deathblock (4), the second laser cannon and the jump generator — so the reactor loop matters
  beyond the first fill. Every metal now has at least three uses (ten new recipes), and the
  fourteen prettiest interior-decor blocks (lights, strip lights, panels, machine blocks…) are
  finally craftable instead of admin-only. (PR #1131)

### 📖 The story lets itself be found (#1109, #1110, #1111, #1112)

- Story fragments now spawn inside structures with a **pity budget** — a dry streak raises the odds,
  so the six "pure luck" fragments are gone. A story objective chip on the HUD plus a proper story
  reader show where you stand; 26 environmental lore texts give runes, wrecks and ruins something
  to say; and NPCs carry story threads into their chatter. (PR #1132)

### 🗺️ The map remembers (#1113, #1114)

- Explored-map fog is **persisted per player**; star systems you have not visited show as
  *Unknown system* until you enter them (or build a radar array). Discovering a named place pays
  5 knowledge through the new Codex "Places" page. Space stations are back to normal frequency in
  new worlds, with opt-out toggles on the structures page. (PR #1133)

### 🔨 Builder goals (#1116, #1117)

- Settlement mission boards now post **Build missions** — build N lights, N blocks in your own base,
  or a specific block. And builds are shareable: the new **blueprint tool** copies a build into a
  share code you can paste anywhere (chat included); pasting validates every cell like hand
  placement, so no code smuggles blocks you could not place yourself. (PR #1134)

### 🧑‍🚀 Living NPCs (#1118, #1119, #1120)

- NPCs have identity and **relationship stages** — trade with the same vendor and they greet you as
  a regular. NPCs now **call you on the radio** ("📻 Name (Ort)"): missions, camp hints, story
  rumours, with a settings preference and a mission-tab section for active calls. And the world
  notices your base: three machine blocks attract settlers, traders prefer worlds where players
  actually live. (PRs #1135, #1157, #1163)

### 🎭 Per-player game mode (#1121)

- Survival and creative can mix in one world: the host plays survival while a kid plays creative.
  `/mode <player> survival|creative|world` (moderator-gated), the settings tab shows the roster to
  admins, and every rule the server answers is the *effective* mode of whoever is asking. (PR #1136)

### 💎 The frontier pays (#1122, #1123)

- The galaxy has **distance tiers** now: the further a system is from home, the richer its rare
  ores (up to ×1.6), plus an extra vault and monument out there. And with the new **Growing
  galaxy** world option, reaching the known edge makes new systems appear beyond it — the map
  is no longer finite. (PR #1138)

### 🎬 The ending (#1124)

- Finish the story and it actually *ends*: a resolution cinematic, a credits roll, and an epilogue
  that hands the galaxy over to the relay network. Joins catch up exactly once per save (old saves
  included), and the Story tab lets you watch the ending again. (PR #1139)

### 📡 The relay network — the loop closes (#1125)

- Any commissioned player station can be converted into an **SPS relay** — expensive (titanium,
  circuit boards, reactor fuel) and built co-op, with personal contributions counted. Two finished
  relays in range span a **jump lane**: jumps along it need no jump generator. Every new lane can
  grow the galaxy at both ends, relay systems see more traffic, and VEGA narrates what the network
  becomes. Achievements `relay_engineer` and `network_weaver`; Codex chapter "Relays".
  (PRs #1140, #1143)

### 🌈 Coloured glass & tinted lamps (#1126)

- Glass and every light fixture (torch, lantern, lamps, strip lights) join the **dye system** —
  and dyed lamps cast **coloured light**, on planets and aboard ships. Frosted glass stays frosted,
  just in colour. New Codex chapter "Colours". (Doors are entities and deliberately follow later.)
  (PRs #1144, #1163)

### 💬 Dialogues with choices — and faces you will meet again (#1127, #1128)

- Press **E on an NPC to talk.** Dialogues are node graphs with choices and real consequences —
  standing, gifts, story fragments, radio follow-ups — and your choices persist in the save. Two
  authored characters travel the galaxy with a fixed face: **Yara Senn** in settlements and
  **Sel-9** at station vendor counters. Settlers without a scripted dialogue at least greet you now.
  (PRs #1146, #1157)

### 🌌 One-of-a-kind sites & peaceful encounters (#1129)

- Three places exist **once per galaxy**: the Singing Shrine, the Sealed Observatory, and
  *The Long Quiet* — a named, boardable derelict with its own lore voice. Trusted NPCs share each
  legend exactly once; keep an eye on the ✶ map glyphs. In space, two peaceful encounters: fly
  close to a drifting **escape pod** to rescue its survivor, and **scan anomalies** for knowledge
  and lore. All of it family-mode safe. New Codex chapter "Mysteries". (PR #1147)

### 🏙️ Settlements & stations stop repeating (#1115)

- The settlement and station template pools grow from one each to **four each**. Placed structures
  pin the template they were built from, so existing worlds keep exactly the buildings they have —
  only newly generated sites draw from the bigger pool. (PR #1148)

### 🩹 Audit rounds: the epic held against its own spec (#1149–#1156, #1158–#1162)

- Two full audit passes over the epic before release, every finding fixed: Sel-9's promised radio
  call actually arrives; authored characters no longer clone into every slot of a place; Esc-skipping
  the ending no longer pops the quit dialog; the base-settler sweep can no longer delete NPCs in
  other worlds; the loot-crate, escape-pod, arcade and small-talk **reward farms are closed**
  (dialog rewards cooldown, arcade validates against the real game catalogue); a share code too long
  for chat is rejected with a message instead of silently truncated; and the vault milestone fires.
  (PRs #1157, #1163)

### 🛡️ Corrupted saves fail loudly, not weirdly (#1137, #1141, #1142)

- A corrupted player row is now **rejected on load with the stored data preserved** instead of
  half-loading into undefined behaviour; the affected player's join is refused with a localized
  message (all 14 languages) naming the problem; and a corrupted persistence init throws
  `InvalidDataException` instead of continuing on garbage. Thanks to community contributor
  **ahmdkaml**, now credited for "tests & hardening" in the README and in-game credits. (#1145)

### ⚡ SRP Batcher works again (#573)

- All URP shaders declare their material properties in a `UnityPerMaterial` cbuffer, so the SRP
  Batcher batches them again — fewer set-pass calls, smoother frames on planet surfaces. (PR #1100)

### 🐛 Codex: Guide and Items chapters render again (#1097)

- Codex → **Guide** ("Anleitung") and **Items** showed an empty pane. One uGUI `Text` held the whole chapter,
  and past ~16 250 characters uGUI refuses to build the mesh (`Mesh can not have more than 65000 vertices`)
  — nothing at all was drawn. German crossed that line on 2026-08-08, English with the 2026.8.17 articles;
  the Items chapter (195 items with descriptions) was over it in both languages.
- Chapters are now rendered as a column of Texts split at paragraph boundaries (`UiTextChunks`, ≤ 10 000
  characters each, unit-tested against the real `articles.json` in EN and DE). Scrolling covers the whole
  chapter as before; the articles and descriptions may keep growing.

## [2026.8.17] — 2026-08-16

The wayfinder release. Two things players kept asking — *why is this tab greyed out?* and *what
should I be doing right now?* — get answers in the game itself. The Tab menu names the **block** a
gated tab needs, shows how far away it is, and points the compass at it; VEGA adds throttled,
repeatable **context tips** — the lamp is off in the dark, rare ore is right there, a settlement is
over the ridge, an asteroid is in tractor range. Minigames pay knowledge in every world (not just
the one where you set your record), the July static audit is closed out with a handful of latent
client fixes, and the release pipeline itself got ~20 minutes faster.

**Protocol stays 3**, but this release adds four new network messages (station gates, station
locator, lamp state) — hosts and players should update **client and server together**; an old
host simply never shows the new gate rows and never gives the lamp tip.

### 🧭 The Tab menu names the block it needs — and where it is (#1070, #1071, #1072, #1073, #1074, #1075)

- **The server decides what is in reach.** Client and server used to disagree on "at a station":
  the client read only the *ship's* station markers and dimmed whole tabs, the server checked placed
  *world blocks* per recipe. So a base workbench left the Crafting tab dead, a forge recipe said
  "go to the workshop" (then the server refused: needs a forge), and the Tech tab's "lab" existed
  nowhere. The server now publishes the set of **stations in reach** (on join and whenever it
  changes); the client gates **per recipe** from it, Crafting dims only when *no* station is near,
  and the Unlock/Build buttons disable with a reason instead of a failing toast. Old hosts fall back
  to the previous heuristic. (#1070)
- **Every gate names the block, with its icon.** Dimmed tabs carry a station badge, the recipe detail
  pane shows *Station: 🔥 Forge ✓/✗*, reasons read "Needs a **Forge** nearby — or your ship's
  Refinery module", the footer lists what is in reach, and mission-board rejections are localized
  instead of the raw `@srv.mission.*` token. (#1071)
- **"Where is it?"** A gate row under the tab bar (moved out of the search box it used to overlap)
  shows *Workbench · 12 m ↗* live; **Show** sets the compass waypoint, **Craft one →** jumps to the
  recipe when none is nearby, and closing the menu drops an 8-second through-wall marker on the
  block. On the ship, the parked cockpit / workshop cell is the target. (#1072)
- **Blocks say what they are for.** Aim at a placed workbench, forge, detoxifier, matter forge,
  algae tank or campfire and the hint reads "Workbench — crafting: menu (Tab) → Crafting". The ship
  `console` marker no longer vanishes into the hull. (#1073)
- **Research happens at the cockpit.** Unlocking tech now requires being aboard within reach of the
  cockpit (the helm counts while flying; free-craft worlds and worlds without a parked ship skip
  the check). The phantom "lab" is gone; the Ship tab's gate is "aboard + workshop module" in
  wording and in code. (#1074)
- Codex guide *Stations & the Tab menu* (block → tab → function), VEGA's craft/unlock hints name
  the workbench and cockpit, user manual updated. 23 new locale keys, 8 removed, all 14 languages.
  Messages 206–208. (#1075)

### 🤖 VEGA context tips — rare, repeatable, situational (#1077, #1078, #1079, #1080, #1081, #1082)

- **A tip framework instead of one-shot hints.** Every tip has a dwell (the situation must hold for a
  few seconds), a per-tip cooldown (10–15 min), a hard repeat cap per save (2–4 times, then VEGA
  considers it learned), and a priority (safety > equipment > opportunity). All tips share one
  cadence per player — at least two minutes apart, quiet for the first minute after joining, LLM
  banter counts against it; a *first* safety hint still gets through. Reacting within 30 s (lamp on,
  ore mined, settlement entered) retires the tip early. Repeat counters live in the save as
  milestones — no schema change; the journal hides them. Repeats are muted by the same VEGA settings
  as banter and are dropped while the speech queue is busy. (#1077)
- **Equipment:** "It's dark — your suit lamp is on {key}", lamp missing, torch underground, eat now,
  medkit, wrong tool, scanner idle, speeder left far behind. Darkness is judged by the server (night,
  or a solid column above your head, and no torch/lantern/campfire/lava within 6 blocks). (#1078)
- **Materials + progression:** rare ore near (rarity ≤ 3 % in *this* planet's ore table, exposed
  blocks only), the ore you still need, data cache near, "you can craft that now", "you can afford
  that blueprint". Opportunity tips wait for the "scan" onboarding stage. (#1079)
- **Places + company:** settlement, ruin, factory, treasure, trader, tameable creature and other
  players nearby — with the same reveal gating as the planet map. (#1080)
- **Space:** asteroid in range (or "you have no tool for it"), station near, hull low, jump ready.
  (#1081)
- **Vitals repeat.** O₂, energy, hunger, cold and heat warnings repeat with a 15-minute cooldown, capped
  per save. (#1082)
- Under the hood: the lamp ON/OFF state was client-only — a new `SetLampIntent` (message 209) reports
  every effective change. Block probes are a 17³ box every 10 s per player, never the O(r³) scanner
  sweep. `{key:Action}` tokens in locale lines expand to the bound key / pad glyph / touch wording
  client-side (the old hard-coded "L" in the night hint uses it in all 14 locales). 26 new
  `vega.hint.*` keys.

### 🕹️ Minigames pay knowledge in every world (#1069)

- The Arcade only reported a run to the server when it beat your **local** personal best — but that
  best is per install, while the server's 5-knowledge-per-star ledger is per player *and per world*.
  A game mastered in one world never paid out in the next, on a friend's LAN server, or for a second
  player on the same PC (permanently for bounded-score games). Every **completed** run is now
  reported; the server dedupes, so a replay that earns nothing new is a no-op. The Arcade badge and
  the "Data fragment recovered!" toast fire only for games that unlock *after* joining, not on
  every join. The `data_fragment` item description no longer claims data cubes / scanning / taming
  yield the item (they pay knowledge; the item comes from caches, terminals, chests, stashes,
  pirates and bounty missions) — corrected in all 14 locales. (#1069)

### 🩹 Audit closeout: loud fallbacks and five latent client fixes (#427, #428)

- **Server: content problems fail loudly.** A `data/ship_layouts/*.json` cell that names an unknown
  block now fails the content load instead of silently stamping `iron_wall`; a mission template whose
  item is missing from content warns once before falling back. The last ten hard-coded server lines
  (three paint rejects, seven `/paintwipe` / `/shapewipe` admin lines) are localized in all 14
  languages. (#427)
- **Placed-lamp light crosses the wrap seams.** Light from lamps and torches stopped dead at the
  X = 0 / circumference and latitude seams of a planet — skylight and AO already crossed them, block
  light now does too. (#428)
- **Localization guards that could never fire** (`localized == key`) now use `Localizer.Has` — portal
  error codes, ban-notice reasons and the Codex VEGA log show a translation or a readable fallback
  instead of `[key]`. The wiki logs a warning when `articles.json` fails to load. (#428)
- **Shader safety net.** All 21 runtime `Shader.Find` shaders (plus the `Unlit/Transparent` /
  `Sprites/Default` fallbacks) are listed in the build script and always-included; the retired
  VolumetricFog GUID is gone from GraphicsSettings. The Built-in-RP fallback of `BlockAtlas` mirrors
  the URP bark branch, so `wood_log` no longer takes the player-dye recolour on a URP fallback.
  (#428)

### 🔧 Under the hood

- **Release pipeline ~20 minutes faster.** The release test gate ran the full suite on one runner
  (33–36 min, the Docker image queued behind it). It now uses the same 4-runner sharded matrix as PR
  CI (`tests.yml` as a reusable workflow; tier-aware shard weights), and the 19-minute
  `TryGetWaterSurface_LandsInsideGeneratedWater` test — which walked ~1 M columns and never asserted
  it had found a pooled river — takes its pooled columns from the routed river field, asserts it
  found some, and runs in ~2 s in the fast tier. Expected release wall time ~57 → ~35 min, main-push
  CI ~25 → ~10 min. (#1067)
- New tests: `StationAffordanceTests`, `ShipAiTests` (dwell / cadence / cooldown / cap / learned /
  first-safety bypass, vitals repeat, rare-ore probe), `VegaTextTests`, `LightSourcesWrapTests`,
  `ContentTests.Validation_DetectsUnknownShipLayoutCell`; protocol golden list covers messages
  206–209.

## [2026.8.16] — 2026-08-16

The rendezvous release. The suit teleporter finally asks *where to* — back to the ship as always, or
straight to an ally on the same planet, and admins land beside a player instead of inside them.
Factories stop being grey boxes: the machines have textures, sculpted housings and moving parts you
can watch. The Avatar Designer keeps several named outfits, the player-to-player trade window joins
the rest of the UI, touch and gamepad players can reach every verb (VEGA, EVA building, maps, the
context-actions list), and two small post-8.15 fixes — the clipped "craftable" tag and the broken
fleet world detail page — ride along.

### 📡 Suit teleporter: back to ship — or to an ally on this planet (#1056, #1055)

- **The suit teleporter now asks where to.** Right-click on the held device opens a small picker instead of
  recalling on the spot: **Back to ship** (what it always did), and below it every **ally** who is on the
  same planet right now, with their distance when they're in view. Pick one and you appear **beside** them.
  Ships stay private — the jump is refused while your ally is aboard theirs — and the server re-checks the
  alliance, the body, energy and the shared 30 s cooldown on every use. (#1056)
- **Hosts can hand it out.** New world rule **Starter teleporter for everyone** (world-rules panel, or
  `--starter-teleporter true`): every player who joins without a suit teleporter gets one, and switching it
  on gives one to everybody online. Off by default, so singleplayer progression is unchanged. (#1056)
- **`/tpp` lands you next to the player, not inside them.** The admin teleport copied the target's position
  exactly and left both of you overlapping; it now uses the same "land beside" spot finder as the ally jump.
  (#1055)
- Codex (*Getting Around*, *Alliances*) and the user manual describe the picker; the item and blueprint
  descriptions mention both destinations. Client and server both need this update (a new message).

### 🏭 Factories look like factories (#1050, #1051, #1052, #1053)

- **The machines have faces now.** The machine housing, its pipes and the production terminal had no
  texture at all — every one painted the same flat grey, so a factory was "a room with two big grey
  boxes". All three got real tiles. (#1050)
- **Authored block colours count.** A `color` set in `blocks.json` was ignored by the texture atlas
  whenever a block had neither a tile nor a hand-picked palette entry; it is now the fallback tint, so
  bedrock, the ship core/helm/engine markers and the factory blocks show their intended colours. (#1051)
- **You can see the machines run.** The press, flywheel and conveyor were drawn *inside* the pipe block
  on the roof — all you saw was a blinking light. They now hang on the front of the housing at proper
  size: a big rimmed flywheel spinning, a piston hammering onto an anvil, parts riding a belt between
  drive rollers. (#1052)
- **Housings are sculpted, not slabs.** Dark plinth course, glass inspection windows in the sides, an
  amber work strip on the floor in front, and exhaust pipes rising from the back corners into the
  ceiling. Existing worlds get the new look on next load. (#1053)

### 👕 Avatar Designer: save and switch between several outfits (#1047)

- The Avatar Designer now keeps up to **eight named outfits** — colours, pixel face and body paint — in a
  new **Outfits** panel: **Save outfit** stores your current look under the name you typed (or updates
  the outfit that already has that name), clicking one loads it back onto the rotating figure, **Rename
  selected** and ✕ do what they say. **Apply** is still the only button that changes your in-game avatar,
  so you can browse and tweak looks freely. Older settings files simply start with an empty list. (#1047)

### 🤝 Player trade window now looks like the rest of the game (#1058)

- The trade window between two players was the odd one out: a translucent panel with the world
  shining through, `−`/`+` buttons so small their glyphs vanished (and no finger could hit them),
  plain text rows without item pictures, and no way to cancel with **Esc** or the pad. It is now a
  proper dialog — dimmed backdrop, "Trade with {partner}", your inventory as icon cards with big
  `−`/`+` buttons, a clear **You give / You get from {partner}** summary with a `READY` /
  `waiting…` badge on each side, and a Confirm button that turns green while you wait for the other
  player. **Esc** (keyboard) or **B** (gamepad) cancels; the stick walks every control; touch targets
  are finger-sized. The incoming trade-request and docking-request prompts got the same treatment,
  and "Trade complete" is finally translated. (#1058)

### 🎮 Touch and gamepad reach every verb again (#1041, #1042, #1043, #1044)

- **VEGA can be advanced without a keyboard.** The ship AI's continue key was the raw **N** and nothing
  else — on a tablet or with only a pad in hand every line, and every story page after the first,
  stayed on screen forever. Continue is now a rebindable action: **N** on the keyboard, **Back/View** on
  the pad, a **NEXT ▶** button on touch, and the hint names whichever one you hold. (#1041)
- **Touch controls: rotate, EVA building, maps, attack, and a list for everything else.** New on-screen
  buttons: **ROTATE** (appears while a rotatable block is selected), **PLACE** and **DEPLOY** in EVA
  (you could not build in space by touch at all), **MAP** on foot and at the helm, **VIEW** on foot,
  **ATTACK** while a weapon is held (hold it on the Guardian core to breach). An **ACT** button opens a
  list of every verb that applies right now — trade / dock with the player beside you, undock, loot /
  stash, repair, lamp, thermal, stow the speeder, … — one tap each. Any tap now switches the HUD to
  touch hints. (#1042)
- **Gamepad: two stick clicks, and every menu walks.** **L3** opens the same context-actions list (the
  stock Xbox layout had only two free buttons for twenty unbound verbs), **Back** advances VEGA. The
  landing-pad chooser, trade and docking requests, the trade panel, the bandit demand, the planet map
  and the flight chart are stick-navigable now and **(B)** backs out of each — a pad-only pilot could
  not land before. (#1043)
- **Ship-systems bar cycles with the wheel / d-pad / touch ◄►.** Switching from the laser to the tractor
  beam needed a number key; the same scroll that cycles the hotbar now steps through the systems. (#1044)
- Under the hood: `InputAction.VegaContinue / PlanetMap / ContextActions`, `InputMap.InjectNextFrame`,
  `ContextActionsUi`; the planet map and the finale breach hold moved off raw key polls. Twelve community
  locales topped up (26 keys each).

### 🩹 Small fixes since 2026.8.15 (#1057, #1063)

- **The ingredient source tag no longer clips.** The right-aligned **craftable** / **raw resource** tag
  next to each crafting ingredient (new in 2026.8.15) ended 4 px past the detail pane's edge and read
  `craftabl|e`; it now ends clear of the pane and its scrollbar, and a source-guard test keeps every
  right-anchored detail text inside the viewport. (#1057)
- **Fleet admin: the world detail page works again.** Every hosted world with a save file showed
  *"Could not read the world save: misuse of aggregate function MAX()"* and three empty cards — a
  correlated sub-select the SQLite planner rejects since the save gained edit attribution. The
  "last editor" query is fixed (it now really names the *most recent* editor, not the highest player
  id), and a failing section reports on its own card instead of blanking players and structures. New
  `WorldInspectorTests` write real saves — attributed, legacy schema, sabotaged table, missing file.
  (#1063)

### 🔧 Under the hood

- **Community:** end-to-end server hardening tests through the real `PayloadReceived → OnPayload →
  NetCodec.Decode → dispatch` path — malformed payloads never escape, unjoined connections and hostile
  `MoveItemIntent` slots are rejected, duplicate `JoinRequest`s are dropped, the valid `ToSlot = -1`
  stow still emits its `InventoryUpdate`, and dispatch keeps serving valid requests after rejected
  input. Thanks @ahmdkaml! (#1046, #1049; part of #569)

## [2026.8.15] — 2026-08-15

The quartermaster release. Version 2026.8.14 was tagged hours before a three-player LAN evening —
this is everything that evening turned up, plus the storage feature it inspired. Crates can now be
told what belongs in them, VEGA shows newcomers the menu and the Codex, copper turns up where you
actually dig, and a long list of things that quietly broke the flow — the heartbeat kicking players
who sat in a menu, the see-through wall behind a torch, the craft button promising more than the
backpack could hold — are gone.

### 📦 Crates that know what belongs in them (#1032)

- Aim at a placed storage crate or wood box and press **E** to choose which items it accepts — an
  ore crate, a food crate, a fuel crate. The bulk **H** stash then only moves whitelisted stacks:
  walk your loot past a row of dedicated crates and it sorts itself. Selecting nothing (or *Allow
  everything*) keeps today's accept-all behaviour. (#1038)
- The filter is enforced by the server, dyed and re-formed variants of an allowed material still
  count as that material, and a stash the filter blocked completely says so instead of claiming the
  box is full.

### 🧭 VEGA shows newcomers around (#1015, #1016, #1011)

- After the opening lines VEGA now introduces the **Tab menu** (inventory, crafting, tech, map) and
  points at the **Codex** — the two discoveries new players most often missed. Both lines stay
  re-readable in the Story tab. (#1022)
- Crafting cost lists tag every ingredient as **craftable** or **raw resource**; for a craftable
  ingredient you are short of, its own recipe inputs are listed right beneath, scaled to the missing
  amount. (#1022)
- VEGA hints and story pages no longer vanish on a 25-second timer — a fully revealed page stays
  until you continue with **N**, however fast or slow you read. Esc still skips the prologue, and
  the settings toggle still mutes advisor hints. (#1018)

### ⛏️ Copper where you dig (#1024)

- Which ore a world offered you was a lottery: each vein's whole budget went into one smooth
  large-scale noise field, which necessarily concentrates the ore into few giant blobs. LAN verdict:
  "copper is too rare — and when you finally find it, it's a mountain."
- Shallow starter ores (iron, copper, silicate) now split their budget across **two scales**: half
  stays in the big mother-lode strikes that make prospecting worth it, half goes into a fine sprinkle
  of small veins that turn up wherever you dig. Median tunnel distance to the first copper drops from
  39–91 m to 25–41 m; tunnels finding *no* copper in 256 m drop from up to a quarter to 2–4 %. Deep
  rarities (diamond, uranium, …) keep the single field — a rare strike should stay a find. (#1028)
- ⚠ Like every worldgen change, ore positions shift inside *untouched* terrain of existing worlds;
  everything players built, mined or placed persists.

### 🚀 Ship rooms read as rooms (#1009, #1021)

- The station marker blocks aboard ships were generic world blocks — the workshop a stone block, the
  medbay ice, the quarters carbon, the cargo hold invisible against the hull. Each station is now
  the themed machine block whose world function already matches the room: **heal tank** in the
  medbay, **workbench** in the workshop, **bed** in the quarters, **crate** at the cargo hold.
  Existing saves pick the markers up automatically. (#1013)
- A wooden door built into a self-built ship ignored **E**: every landed-ship door was registered as
  an auto-sliding energy hatch. Doors now keep the kind you actually placed — wood and hinge doors
  swing by hand, slide and energy doors keep their proximity auto-open. (#1026)

### 🌍 Multiplayer: the world stays under your feet (#1030, #1020, #1008)

- **"I only see space": returning to an area another player kept loaded showed void terrain.** The
  client frees far-away terrain to keep memory bounded, but the server only forgot what it had sent a
  player when the area was far from *everyone*. With a partner standing there, coming back — by
  teleport, beam or on foot — streamed nothing: ship and animals rendered over a starfield. The server
  now also forgets per player, by that player's own distance, so a return always re-streams the ground.
  `/tpp` additionally refreshes your aboard-ship state and refuses targets that are in space or on
  another planet instead of copying their coordinates into the wrong scene. (#1036)
- **Dying near another player could respawn you inside *their* ship.** Deaths dealt by the world's AI
  (creatures, guardian machines, bandits, a destroyed speeder) — and the void-rescue teleport — resolved
  the respawn target through whichever player the server had served last, dropping the victim at the
  other player's heal tank (and it could even re-home that ship). Every death and rescue path now pins
  the dying player's own ship first. (#1027)
- **Another player's landing pad never showed as occupied on your world map.** The pad list was sent
  only once, on your own arrival: anyone who landed *after* you kept showing as a free, anonymous pad
  forever. Claiming or releasing a pad (landing, joining, leaving, observer mode) now republishes the
  pad list to everyone on the body. (#1027)
- **The 90-second heartbeat kicked players who were just sitting in a menu.** The client went
  completely silent behind crafting, map, trade, chat, the star map and the pad chooser — seven kicks
  in one 80-minute LAN evening. The position stream now keeps flowing (frozen in place) behind every
  menu, so the sweep only catches actually dead connections. (#1014)

### 🧱 Placing blocks near a ship works again (#1023, #1031)

- **"Sand and dirt cannot be placed."** The materials were innocent: *any* placement into a landed
  ship's bounding box — which includes the ground-level air ring around the hull, exactly where
  players spawn — was silently rerouted into a ship-structure edit and rejected. Placements only
  become structure edits now when the aim ray actually hits that ship; aiming at the ground always
  places a world block. Most likely this also explains the "painted block won't place" report. (#1025)
- **The wall behind a torch was see-through.** Torches, lanterns and ladders never fill their block
  cell, but the mesher treated them as sealing neighbours and culled the wall face behind them —
  visually *and* from the collider. They now count as open space for face culling, ambient occlusion
  and light, so the wall stays a wall. (#1035)

### 🛠️ Crafting & inventory polish (#1010, #1012, #1033)

- **The craft button no longer promises what your backpack can't hold.** With 24/24 slots full, a
  recipe showed green and clickable, and the refusal arrived as an easy-to-miss toast. The client now
  dry-runs the same fit check the server enforces and disables the button with the localized
  "inventory full" notice up front. (#1019)
- The Inventory tab's first sidebar entry no longer repeats "Inventory" — it is **Backpack** now
  (German: *Rucksack*), next to the unchanged Cargo Hold. (#1017)
- Selecting an empty hotbar slot shows your gloved **hand** instead of nothing — tinted to match
  your suit's arm colour, with the usual swing on a punch. (#1037)

### 🔍 The scanner scans what you aim at (#1005, #1004)

- **The scanner looked stuck on its last subject.** It picked the nearest creature within reach by
  pure distance — including one behind you or behind a wall — and a missed scan was silent, leaving
  the old readout pinned. Scans are now aim-gated (a 25° cone around your view; point-blank always
  passes), and an empty or rejected scan says so in a toast. (#1007)
- **A guard drone no longer fires laser bolts at you through solid rock.** The shots were always
  cosmetic — damage checked line of sight — but the client drew and voiced them regardless. Ranged
  attack effects now hold fire without a clear sight line. (#1006)

### 🗒 Honest descriptions (#1029)

- The forge's description promised *faster* smelting — crafting is instant, there is nothing to be
  faster than. It now states the forge's real advantages: more metal out of every ore than the
  workbench, and the ability to refine rare ores (titanium, tungsten, uranium, diamond, …) the
  workbench can't handle. Fixed in all 14 languages. (#1034)

### 🛠️ Behind the scenes

- The crate filter travels as one new additive intent message and an additive save column via the
  usual idempotent migration — old saves simply read as "allow everything". (#1038)

  ℹ Multiplayer: the wire protocol stays **3**. The fixes split roughly evenly between client side
  (heartbeat, placement rerouting, craft gating, torch wall, hand, VEGA timing) and server side
  (respawn, pads, terrain re-stream, doors, room markers, ore) — for the full effect, host and
  clients should both update.

## [2026.8.14] — 2026-08-14

The shipshape release. After the last two versions turned the game into something you play with other
people, we went back over every one of those multiplayer fixes line by line and checked them against
the code instead of against our memory of writing them. All of them hold. But the walk-through found
six seams where the new features stopped just short — a pause that stopped the clock without stopping
what people could do, space actions still measured from the wrong cockpit, an observer left in the void.
No new features here; this release makes the last two honest.

### 🛰 Every pilot acts from their own cockpit (#994)

- **"Out of range" while you were sitting right next to it.** An earlier fix gave each pilot their own
  flight simulation for collisions and damage — but the *actions* you trigger yourself still measured
  from whichever ship had reported its position to the server last. With two of you over the same
  planet, a shot at an asteroid in front of you could be rejected because your wingman was far away,
  and a tractor beam could reach out from somewhere you had never been.
- Firing (range **and** aim), salvage collection, tractor pulls, station boarding, EVA structure edits
  and the dock/interior return spots now all use the position of the pilot performing them. The passive
  tractor tick collects into each pilot's own hold.

### ⏸️ A held pause really holds (#995)

- **The pause stopped the clock, not the hands.** When everybody holds the world paused, the
  simulation stops — but the server kept accepting whatever players sent it. A stock client sends
  nothing while it is in the pause menu, so nobody noticed; a modified one could hold the world frozen
  for the full ten minutes, with hunger and enemies switched off, and mine and build the whole time.
- Gameplay intents are now dropped at the door while the world is held. Resume, chat, saves, admin
  commands and everything read-only stay live, so a pause still behaves like a pause.

### 👁 Watching admins survive a pause — and stop taking pads (#996)

- **An observer flew into nothing.** A watching admin does not block a group pause and does not count
  towards one — but the terrain stream was switched off with the rest of the world, so an observer who
  kept flying during a hold ran out of loaded world and into the void, for up to ten minutes.
  Observers keep receiving chunks while the world is held.
- **An observer no longer claims a landing pad.** Landing one on the body they were already at
  reserved a communal pad, parked a ship and marked them as aboard it. Spectators are now left out of
  pad occupancy at the single place it is derived, which also closes the same leak on the travel path.

### 🧑‍🚀 A newcomer's respawn point is their own (#997)

- **You could wake up inside the host's ship.** On a world configured without a starter ship, a new
  player's respawn anchor was read while the server still had the *previous* player's ship selected —
  so the host's heal tank was saved as the newcomer's spawn. This is the last echo of the origin-spawn
  bug fixed earlier; the default configuration was never affected.

### 🧹 A failed join cleans up after itself (#998)

- **A half-finished join left a ship behind.** If a join failed after the player's ship had already
  been parked, only the session was cleaned up — the ship stayed on the world as an ownerless prop
  nobody could fly or remove. The parked ship is now torn down with the failed join (deliberately
  without saving the half-restored state).

### ✨ Multiplayer polish (#999)

- **A nameplate no longer floats where a player used to be.** When a remote avatar is hidden because
  its updates stopped, its name tag went with it — before, the tag hung in mid-air for the three to
  ten seconds until the body was removed.
- **The star map stops claiming the pads are full** when the only pad left is the one reserved for
  *you* — the same fix the flight chooser already got.
- **The number keys in the pad chooser pick the pad on the label**, not the pad in that position in
  the list.
- **The server's chunk-stream rate cannot outrun the client any more:** it is clamped below what a
  client can absorb per frame, so a raised setting cannot manufacture the backlog the pacing exists to
  prevent.

### 🛠️ Behind the scenes

- Ahmed Mohamed Abdelhady Kamel's networking-test series continues: registering the same message type
  twice is now caught by a guard instead of silently keeping one of them (#993).
- The audit that produced this release is covered by tests: intents arriving during a hold, chunk
  streaming while paused, per-pilot space actions and the chunk-cap invariant under a raised setting.

  ℹ Multiplayer: the wire protocol is unchanged (**3**). Only the floating nameplate and the pad
  number keys are client-side fixes; everything else lives on the server, so **the host's version
  decides** whether the pause, space-action and observer fixes apply to a session.

## [2026.8.13] — 2026-08-13

The crewmate release. The last version put several players into one world for the first time; this one
is what the evenings after that turned up. Almost everything here is something that was *there* but
unreachable: a trade you could ask for but never accept, a break only one player was ever allowed to
take, paintwork nobody else could see, your own landing pad refusing you, a world rule hidden under a
button. Add a sea that stopped drawing surfaces that were not there, a world that fits in a quarter of
the memory, and a worlds portal that now speaks all fourteen game languages.

### 🤝 A trade request can finally be accepted (#981)

- **T did nothing — for both of you.** Asking a nearby player to trade told them "someone wants to
  trade" in the chat line, and that was the end of it: there was no window, no key and no way at all to
  say yes, so no player-to-player trade could ever be opened.
- An invitation now opens an **Accept / Decline** window, exactly like a docking request, and accepting
  opens the trade on both sides. The asker gets a confirmation that the request went out.
- The keys also stop swallowing themselves: T and K are offered as far as the server's trade range
  reaches, instead of one metre closer, where pressing them produced no reaction whatsoever.

### ⏸️ Multiplayer pauses too — once everybody is in the menu (#973)

- **A break is a break for the whole crew.** Until now only a player alone in a world could pause it;
  in multiplayer the Esc menu said "Paused" while hunger drained, creatures hunted and night fell
  behind it. Now the world stops as soon as **every** player is sitting in their pause menu, and runs
  again the moment one of them presses Resume.
- **The dialog tells you what it is waiting for** — "Paused 1/2 — waiting for: Severin" — instead of
  claiming a pause that is not running. In 14 languages.
- Nobody can pause a world out from under anyone else: it takes everybody. Watching admins in observer
  mode neither block a pause nor count towards one.
- **A friend whose game crashes mid-break no longer holds up the room.** Their name and slot are
  released on the usual budget, and a world where every paused client died wakes up instead of sitting
  frozen (follow-up to #964).

### 🎨 Other players wear their own paintwork (#982)

- **Everyone else looked like a factory-fresh suit.** A player's self-painted face and body paint were
  only ever sent *to* them when they arrived — nobody already in the world was told what the newcomer
  looks like, and nothing re-sent it when someone travelled, landed or boarded a station. Your friends
  only saw your artwork if you happened to repaint yourself while they watched.
- Appearance is now exchanged both ways on every arrival, so you see each other's work straight away.

### 🛬 You can land on your own pad again (#977)

- **"Pad 2 is occupied — by yourself."** Your landing pad stays reserved while you are up in space, so
  nobody parks a second ship on top of yours. But the chooser showed that reservation to *you* as
  taken, with your own name under it, and it only lets you click free pads — so the one pad your ship
  is actually standing on was the one place you could not land.
- Your pad is now drawn as yours, in cyan, and stays selectable — in the flight chooser and on the
  world map. Pads held by other players stay blocked exactly as before.

### 🔌 The join dialog offers the port you actually need (#978)

- **Joining a friend's world takes one number less.** "Join server" now prefills **31550**, the port a
  world hosted from inside the game listens on — the only kind of server this dialog is used for.
  Official worlds keep arriving with their own address from the Official Worlds menu.
- The hint next to the field still names the dedicated-server port (31415) for anyone typing in a
  server address by hand, and what you type is remembered for the next time the dialog opens.

### 🖥 Two things the host screen was keeping to itself (#983, #984)

- **A world rule you could not switch on (#983).** "Keep ship when destroyed" was drawn *underneath*
  the footer buttons of the world options page, which swallowed every click on it — the newest rule in
  the list was the one rule nobody could set. The rows sit tighter now and the whole column ends clear
  of the buttons again.
- **The host now sees the address to read out (#984).** Your own join address only ever appeared after
  worldgen had finished, in the chat scrollback — so the person hosting had to go looking for the very
  number everyone was waiting on. The host bar shows **"Your address: ip:port"** with a **Copy**
  button while the world is still generating.

### 🧑‍🚀 Admin commands finally accept player names with a space (#980)

- **`/tpp mincraft Fan` works.** Commands that take a player name only ever read the first word of it,
  so any name with a space came back as "target player not found" — as if that player did not exist.
  The name is now simply the rest of the line, for `/tpp`, `/where`, `/builds`, `/kick`, `/paintwipe`
  and the trailing name in `/give iron_plate 5 mincraft Fan`.
- **Capitalisation no longer matters** for `/tpp` and `/give`: `/tpp marcel` finds *Marcel*, the way
  `/where` and `/goto` always did. Quoted names (`/tpp "mincraft Fan"`) and the `@Name` habit from
  other games are accepted everywhere too.

### 🌊 Deep water stops growing ghost surfaces in the distance (#987)

- **No more water planes hanging in mid-water.** Far away, an ocean was only streamed up to a fixed band
  above its seabed, and the game drew that cut-off as if it were the water's surface — flat panes floating
  at odd heights, with thin glowing "waterfalls" running down their edges. Distant seas are now streamed up
  to their real waterline, so what you see out there is the actual surface.
- **Nothing fake at the edge of the loaded world either.** Water and glass no longer draw a face toward
  terrain that has not arrived yet, which also removes the brief flicker of a false surface while a new
  area streams in.

### 🧠 The world takes a quarter of the memory to look at (#966)

- **Chunk meshes are far lighter.** Every visible chunk was stored twice — once on the graphics card
  and once again in main memory — and each corner of every block carried a pile of full-precision
  numbers it never needed. Both are fixed: the spare copy is gone and the data is packed, so the
  terrain around you costs roughly a quarter of what it did. A long session at the largest view
  distance was reaching 1.8 GB; the terrain part of that is now a fraction.
- Nothing about how the world looks changes — same geometry, same lighting, same colours.

### 🌍 The worlds portal now speaks all fourteen game languages (#970)

- **play.blocksbeyondthestars.de is no longer German/English only.** Creating an account, making a
  world, setting a join password, reading the community rules — all of it now renders in every
  language the game itself ships. Pick yours from the globe in the page header, or from the list at
  the bottom of every page; the choice is remembered.
- **A first visit lands in your own language.** The browser's language preference is matched against
  all fourteen now, not just German and English.
- **The in-game rules screen follows.** The community rules are single-sourced with the portal, so
  the rules the client shows before signup arrive in the player's language too.
- **Browser play greets you in your language** — the loading screen behind `/play` is localized, and
  the portal's Play button hands your language to it.
- Impressum and Datenschutz keep their German bodies (they are the legally authoritative text) and
  now carry a plain-language summary in your own language above them.

### 🐞 Fixed

- **Landing back on the planet you launched from puts you back in your ship (#971).** Picking a
  different landing pad in the chooser parked the ship there but left *you* standing at the pad you
  launched from — often thousands of blocks away, so it looked like the ship had vanished. The
  touchdown now moves you with your ship, exactly as a landing on another planet already did.

### 🛠️ Behind the scenes

- Ahmed Mohamed Abdelhady Kamel's test series continues with the networking codec: every top-level
  message must be registered before it can be sent, and CI now fails when a new one is not (#979);
  a roundtrip and byte-flip fuzz suite pins the rule that a malformed packet must never take the
  server tick down (#989).
- The world options page grew a layout guard: a test reads the page's own source and fails with the
  arithmetic in the message when an appended row would run under the footer — the exact way #983
  went unnoticed.

  ℹ Multiplayer: the wire protocol is unchanged (**3**), but trade invitations, the shared pause and
  other players' paintwork are new messages — they only work when the client **and** the server run
  this version.

## [2026.8.12] — 2026-08-13

The shipwright release. Until now the ship you flew was one the game handed you; from this version
you lay a keel on a planet, build the hull block by block, and a shipwright's check at the helm
decides whether it is airtight enough to fly — and how it flies is whatever you built. The other
half of this release came out of a long LAN evening with a real crew: ghost ships that were never
there, pilots frozen mid-air, a landing that hung forever on "Reading landing pads…", and a game
that stopped dead the moment you alt-tabbed. All of that is fixed.

### 🚀 Build your own ship — keel to commissioning, anywhere on a planet (#948, #949, #950)

- **Lay a keel, build a ship.** The new blueprint-gated **ship keel** can be placed anywhere on a
  planet surface (no landing pad needed) and founds a construction site. Build the hull onto it block
  by block — up to **15×15 blocks, 15 high** — with a helm, at least one engine and a door.
- **Commission it at the helm.** Pressing E at the new **ship helm** checks the build the way a
  shipwright would: big enough, exactly one helm, an engine, a door, and an **airtight hull** (glass
  and doors seal; every gap is a no). Pass, and the build becomes your active ship, parked right where
  you built it — launch from the menu as usual.
- **It flies the way you built it (#949).** Hull strength grows with the hull, speed and handling come
  from engines versus weight: a light frame with spare thrust darts around, a brick with one engine
  limps. Re-edit your ship on foot any time — take the engine out and the launch check grounds it
  again until you put one back.
- **Half-built ships are safe.** The construction site survives rejoins, world trips and logouts (it
  is saved with your fleet); dismantling the last block returns the parts and cancels the build.
- **Under the hood:** ship hull edits are now saved **per ship** instead of one shared pile for the
  whole fleet, so switching ships no longer re-applies another hull's edits onto the wrong design.

### 🛰 Flying together actually works now (#954, #955, #956, #957, #958, #959)

A LAN evening with several players in one world turned up six separate faults. Every one of them is
fixed:

- **Ghost ships stopped haunting the sky.** Another player's ship was spawned as a static hull with
  no collision — a decoration that looked like a ship, sat where the ship no longer was, and could
  not be hit. Worse, when a friend switched ships, *your* hull was replaced by theirs. Remote ships
  are now their own thing, and switching a ship only changes your own.
- **Pilots stopped freezing in mid-air.** Remote players jerked and then hung motionless because the
  smoothing window was shorter than the interval the positions arrive in, and a single missing update
  deleted the avatar outright. Movement is smooth again, a short gap is ridden out, and a player who
  really is gone fades after three seconds instead of leaving a statue behind.
- **Damage lands on the right ship.** Collisions and hostile fire were computed once for the whole
  instance, so an asteroid one pilot hit could bill another pilot's hull, and shields regenerated for
  the wrong ship. Each pilot now has their own flight simulation.
- **"Reading landing pads…" no longer hangs forever.** Requesting the pads of the world you are
  already at could go unanswered, and the client waited on it with no way out — the flight was simply
  over. The request is always answered now, the client retries once and then tells you it timed out
  instead of locking up.
- **Landing back on the world you launched from shows the real world again.** The client used to keep
  its stale idea of the terrain, so blocks that had been mined were back and blocks that had been
  built were missing. A landing at your home body now resyncs the world, the ships parked on it,
  stations, doors and the weather.
- **Landing pads stay yours while you are in space**, instead of being handed to someone else the
  moment you launched.

### 🧊 Alt-tab, crashes and the ghost-block storm (#963, #964, #965)

- **Alt-tabbing froze the game.** The client stopped running the moment it lost focus, while the
  network connection stayed up and kept piling up messages — so switching to a browser for ten
  seconds meant coming back to a frozen client with a mountain of backlog to chew through. The game
  now keeps running in the background, and incoming data is paced across frames instead of arriving
  as one wall.
- **A crashed client can rejoin.** After a crash your old session lingered on the server — in one
  playtest for **22 minutes** — and rejoining with the same name was refused because "you" were
  already connected. Rejoining with the same name and your own player token now evicts the dead
  session, and sessions that go silent for 90 seconds are dropped on their own.
- **Mining no longer floods the world with ghost blocks.** Every click sent the mining action
  **twice**, and each stray hit could trigger a full re-send of the chunk — with several players
  mining, that storm was enough to make blocks reappear and vanish. One click is one hit now, and a
  chunk is re-sent at most once every ten seconds.
- **Two memory leaks closed:** the minimap and the shape-icon previews threw away their old textures
  without freeing them, and chunks far behind you were only unloaded while you were moving — stand
  still in a big world and nothing was ever released.

### 🎯 The hotbar's slot actions are findable — and work on pad and touch (#935, #940)

- **The feature had no tell at all.** Recolouring, reshaping and swapping a stack from the hotbar
  (new in the last release) was a middle-click that nothing on screen mentioned. Now the controls
  hint names it while the selected slot holds an item, and a small key badge floats over the selected
  cell — bright when colour or shape apply, dim when only swapping does.
- **The verb menu became a radial pie.** Swap, Colour, Form and Close sit as four quarter-ring wedges
  around the cursor; wedges that cannot apply to the held stack stay visible but inert.
- **Gamepad and touch reach it too.** On a pad, clicking the right stick opens the pie and the stick
  navigates the wedges; on touch, a "…" button beside the hotbar arrow opens it, and it only appears
  when the pie could actually open.
- **Mouse buttons read as words.** HUD hints showed raw engine names like `Mouse2`; they now print
  the localized short name in all fourteen languages.

### 🛡 Moderation: name screening, operator pings and visible paint reports (#938)

- **Offensive names are screened at every door.** The block list now sees through accents, spacing,
  punctuation and leetspelling, and a second, softer tier flags a borderline name for review instead
  of blocking it outright. The screen runs at the world list, at the join gate *and* on the game
  server itself, so a name cannot slip in through a side entrance.
- **The operator gets pinged.** Blocked and flagged joins, crash reports and player reports can now
  push a notification to the world's operator (opt-in, off by default for self-hosters).
- **Reported paintings and shapes reach a human.** `/reportpaint` and `/reportshape` used to end in a
  log line; they now travel to the same inbox as crash reports, with the offending image attached.

### 🐛 Fixes

- **"Host Game" worlds could not be joined with the prefilled port (#936, #960).** A world hosted from
  the game menu listens on **31550**, but the join dialog prefills the official-server default
  **31415** — so joining a friend's world with the untouched field silently timed out until someone
  guessed the number. The dialog now names both defaults next to the port field, in all fourteen
  languages, on Windows, macOS and Linux alike.
- **The browser version blamed the wrong thing when the arcade worlds were full (#936).** A full
  arcade showed the generic "could not start" error, which reads like a broken game. It now says the
  worlds are full — and that singleplayer is always open.
- **The hotbar's Swap and Colour panels covered their own buttons (#953).** The Back/Close row sat on
  top of the last row of backpack slots (two slots unreachable, their clicks stolen) and the stow
  button floated outside the panel entirely. Both panels grew; the slots kept their size, so the
  touch and gamepad targets from this release are unaffected.

### 🛠️ Behind the scenes

- Ahmed Mohamed Abdelhady Kamel's test series grew to twelve pull requests — this release adds
  deterministic randomness, noise generation, chunk modifiers and `BlockId` (#934, #944, #946, #947).
  His credits entry now describes the series instead of listing individual types (#951).
- A CodeQL log-forging alert on the join gate's world id was closed by routing it through the existing
  log sanitizer (#945).
- New regression tests lock in this release's multiplayer work: the LAN playtest faults, the reconnect
  and ghost-session rules, and the client's receive budget (which must stay above the server's own
  send budget, or it would manufacture the very backlog it is meant to pace).

  ℹ Multiplayer: the wire protocol is unchanged (3), but the fixes above only take effect when both
  the client **and** the server run this version.

## [2026.8.11] — 2026-08-11

The tempest release. Weather stopped being a coin flip and became something with a temper, a
direction and a price — and every creature the generator invents now has a voice of its own to be
heard over it. Closer to home: the paint editors grew a fill tool and twice the palette, the hotbar
learned to recolour and reshape a stack without opening a single menu, ladders keep the wall you
gave them, and the Esc key finally stops the world you can actually see.

### 🌦 Weather overhaul: episodes, wind, alien skies and real stakes (#900)

- **Every world used to have the same weather.** The weather randomiser was seeded from the save seed
  alone, so all worlds in a save ran the identical sequence in lockstep — and a restart replayed it.
  Each world now has its own.
- **Weather comes in episodes.** It builds, holds and fades instead of switching on and off, with its
  own strength each time, so no two storms are alike. Every world has its own temper: some flip
  between squalls, others brood under one sky for minutes.
- **The day and the season have a shape.** Storms build through the afternoon, mist gathers around
  dawn, and a slow wet/dry season rides on top of it all.
- **New weather:** drizzle, sleet, ground fog and whiteout fog, gales, blizzards and heatwaves — plus
  the genuinely alien: **acid rain** on toxic worlds, **ember fall** on volcanic ones, **spore blooms**
  in jungles and swamps, and **ion storms** and **meteor showers**. Airless moons and asteroids get
  weather for the first time; overcast worlds are no longer frozen under one grey sky.
- **Weather moves.** Fronts drift across the world, so you can watch a storm arrive and pass, and
  mountain tops sit in cloud and snow while the valley below stays clear.
- **It matters now.** Corrosive and falling weather drains your suit out in the open, so a roof is a
  real answer. Rain waters planted flora. Scanners lose range in blown grit and charged air. Animals
  hunker down. Snow settles on the ground and melts again when it warms up.
- **…and it can pay.** An **ion storm charges an exposed suit**, a **spore bloom** fattens the harvest
  — sometimes the right move is to walk into the bad weather. The new **weather scanner** reads what
  is coming before you set out.
- **Sights and sounds:** lightning lights the whole landscape now, thunder rolls in late from a
  distant strike, the sky's speed follows the real wind, a weather chip sits in the HUD in all
  fourteen languages, and there are seven new weather soundscapes.

### 🐾 Every generated species gets its own voice (#901, #902, #903, #904, #905, #906, #907)

- **The voice was never a generated trait.** The client picked a call sample by hashing the species
  id — and ids run `sp0`…`sp8` and repeat on every planet, so the whole game had about **nine voices
  per habitat, forever**, no matter how many worlds you visited. Combat cues were worse: a 6-slot
  bank keyed only on size and hostility, so every large hostile creature in the universe screamed
  out of the same five files.
- **Every species is now issued a voice genome**, derived from its world and from the body it
  already has: a base call, an optional second call layered behind it, a timbre treatment, a phrase
  of 1–7 pulses at its own spacing, a pitch contour across that phrase, and its own calling rate —
  from a 2.8-second chatterer to a drifter that speaks once every 55 seconds.
- **You can hear the body.** An **eyeless cave dweller** fires a rising train of 4–7 clicks, because
  it navigates by sound. A **titan** lets out one slow bellow every 14–30 seconds. A **medusa** is
  nearly silent — one drawn-out drone, half a minute apart. A **pack hunter** barks one or two short
  saturated bursts on a falling contour. Horns ring, gas sacs muffle, limbless bodies waver, skittish
  animals sound thin and quick. **Combat cues inherit the same timbre**, so a creature's roar
  belongs to that creature.
- **The scanner names the call** — "Calls in rapid clicks", "Calls in slow, deep bellows" — in all
  fourteen languages, making a voice as readable a trait as colour.
- **21 new recorded calls** top up the thin habitats (water had **five**), plus lava, air, amphibian
  and land; all 69 call assets now ship mono, which halves both memory and audio processing.
- Growing a call pool no longer re-rolls every existing species' voice in that habitat: rendezvous
  hashing moves roughly 1/N of them, and only ever *to* the new call.
- Existing worlds keep every trait they had — the voice seed is folded in without consuming a single
  step of the world generator's randomness.
- A leak had to be fixed first: the sample cache was a static dictionary that was never cleared or
  capped. Per-species bakes would have grown it by ~22 MB per planet visited, with no release point
  at all across interplanetary travel — the browser tab-crash failure mode from #423, appearing
  after 20–40 minutes of play with nothing useful in the log. It now clears on world teardown and
  keeps at most 64 bakes.

### 🎨 Paint editors: fill, a findable eyedropper, 32 colours and one Appearance screen (#899)

- **Fill tool + undo.** Click to flood an area with the current colour (right-click fills with
  "empty", Shift replaces that colour everywhere in the region). It cannot leak from one box face
  onto another. Undo takes the last fill, clear or stroke back — press it twice and the change
  returns.
- **The eyedropper found its shortcuts.** **Alt+click** or the **middle mouse button** picks up the
  colour under the cursor; fill, pick and undo now sit together as one tool row.
- **32 colours instead of 16.** The new half of the palette is shading partners — a lighter and a
  darker sibling for the hues you actually shade with, two more greys and a deep skin tone — and the
  colour wheel gained a brightness column so the dark tones are reachable. Every painting drawn
  before this keeps its exact colours.
- **The canvas stopped lying.** Unpainted pixels are drawn in the colour that will really show
  through: your skin, the part's tint, or a block design's paper white.
- **One "Appearance" screen.** Face, torso, arms, legs and helmet are tabs of a single editor, with
  the base colour for the part right beside the canvas it tints and a slowly turning figure showing
  the result — including the back you just painted. It replaces nine separate menu cards.
- **Any colour for skin and suit.** Base colours come from one shared 30-colour set (the in-game menu
  and the Avatar Designer used to offer different ones) — or from the colour wheel, which is not
  limited to the list at all.

  ⚠ Multiplayer: client and server must both be on this version (protocol 3).

### 🎒 Recolour, reshape and re-texture straight from the hotbar (#924)

- **Middle mouse on the selected hotbar slot** opens a small verb panel. **Swap** exchanges the stack
  with any backpack slot; for building materials, **Colour** dyes it, makes it glow, or applies one
  of your own saved paint designs, and **Form** offers the 19 built-in shapes drawn as silhouettes of
  that very material, plus your own designed forms. The whole stack converts and lands back in the
  same slot — no crafting screen, no walk to a bench.
- **Your own painted textures now travel with the item.** A painted stack shows its design in the
  hotbar and in the swap grid, places with that design, and **mining it back recovers the design into
  the drop** — the same round trip that dye and form already made.
- The key is rebindable (`HotbarAction`). There is no default gamepad button for it yet.

### 🪜 Ladders keep their wall, and the crafted staircase is a staircase (#909)

- **A placed ladder used to store no orientation at all**, so the client re-invented which wall it
  clung to on every mesh rebuild: ladders hugged a different wall than the one you aimed at,
  free-standing ladders silently turned into poles, and mining a wall next to a ladder flipped the
  whole column. Placement now pins the real mount, and **R** cycles Auto → the four walls →
  free-standing. Ladders already standing in saves, settlements and ship layouts behave exactly as
  before; only newly placed ones are pinned. A bonus: the wall behind a ladder stops being culled away.
- **The crafted staircase placed as a plain cube**, even though stepped geometry has existed for
  shaped materials for a long time. It now has real steps, a collider that matches them, and **R**
  turns it or tips it upside down.
- **Auto uses the face you actually clicked.** Building a ramp into a corner used to snap to
  whichever neighbouring surface a fixed scan order happened to reach first; the wall under the
  crosshair now wins — a floor still wins over a wall, so extending a floor by clicking the side of
  the last slab still lays the next one flat. Preview and result are now computed by the same code,
  so the ghost can no longer disagree with what you get.

### 🔤 Long button labels stay inside their buttons (#918)

- A localized label that is wider than its button — German "Farbe aufnehmen" in the paint editor,
  and others across the menus — used to run past the button's frame. Labels now shrink to fit, which
  is what they were always meant to do; menu tabs, the sidebar headings and the touch controls get
  the same treatment. Text that already fits looks exactly as before.

### 🐛 Fixes

- **Esc paused the server, but not the world you were looking at (#908).** The pause has been a real
  server-side hold since #612 — the client was simply never told about it. So behind the dialog you
  kept **falling**, creatures kept walking, calling and lunging, bandits kept attacking, settlement
  NPCs kept strolling, and the client's own day clock kept the sun moving and the clouds sailing. A
  single-player pause now stands the visible world still too. Music, the camera and UI animation stay
  live on purpose — a completely frozen image reads as a crash, not as a pause. Also fixed: an
  invisible admin observing a world used to count as a player, silently denying the only real player
  their pause.
- **The space HUD printed "Cargo: N" straight across the hull bar (#915).** The flight overlay and
  the on-foot HUD share one coordinate space, and the cargo line sat exactly where the vitals panel
  ends *on foot* — but the panel grows by a hull and a shield row the moment you are flying a ship.
  The readout now follows the panel's live bottom edge, so no future row can collide with it either.
  While there: **hull and shield became real gauges in the flight HUD**, in the same column and style
  as the suit's bars, and they carry the low-hull blink and warning cue with them — plus a guard the
  old panel lacked, so a ship without shields stops raising a shield alarm. The instrument line is
  back to `SPD/THR/HDG`. An EVA is unchanged.
- **Dyed, glowing, shaped or painted stacks showed a raw key instead of a name (#927).** A modified
  stack carries its modifier inside the item key (`snow#t8fd030`), and three UI surfaces looked that
  composite key up in the translation table, where it does not exist — so the hotbar caption, the
  pickup feed and the new slot-action panel printed `[item.snow#t8fd030.name]`. They now show the
  real name plus what was done to it: "Snow · dyed", "Snow · slab", "Snow · painted". Two of the
  three sites date back to the dye era and were only hidden by the hotbar caption's truncation.
- **The French credits screen was one long line.** Every line break in the French credits text was
  double-escaped, so the whole screen — family, contributors, playtesters, licences — ran together
  with visible `\n` markers instead of breaking into lines. The other thirteen languages were fine.

### 🛠️ Behind the scenes

- Ahmed Mohamed Abdelhady Kamel joins the credits for the first dedicated unit tests covering
  the core world types: `Vector3i`, `ChunkCoord` and the `Frequency` tuning invariants (#917,
  #921, #923, #926).
- More of the quiet foundations got test coverage: the server presets, the mission validator's
  contracts, and the localizer's key resolution, English fallback and active-locale precedence
  (#928, #930, #931, #932).
- Translation PRs get a faster, focused CI lane: a pull request that touches nothing but
  `data/locales/*.json` now runs exactly the test classes that read the locale tables (found by
  mutating every translated string and collecting what fails) instead of the full four-runner
  matrix. Pushes to `main` and releases still run the complete suite (#922).

## [2026.8.10] — 2026-08-10

The polyglot release. In one week the game went from four languages to **fourteen** — Dutch,
Brazilian Portuguese, Polish, Turkish, a finished Italian, plus Russian, Ukrainian, Simplified
Chinese, Japanese and Korean, each a complete 3048-key pass over every menu, block, recipe,
tooltip and story line. Getting the non-Latin five in meant teaching the client a font it never
had. Alongside that: your astronaut is now paintable head to boot, and the animals stopped
sounding like a copy-paste of themselves.

### 🌍 Fourteen languages (#881, #883, #884, #885, #886, #887, #888, #889, #891, #890, #892, #893, #895)

- **Ten new complete locales.** Nederlands, Português (Brasil), Polski, Türkçe, Русский,
  Українська, 简体中文, 日本語 and 한국어 each ship a full 3048/3048 main-table pass plus the
  68-key VEGA prologue story pack — and **Italiano is finally complete** (15.5 % → 100 %). All
  of them clear the settings picker's coverage gate, so they simply appear in the language list;
  the game also picks your OS language on first run. The LLM mission backend answers in the new
  languages too, and the content-error dialog speaks them before any locale file has loaded.
- **Italian keeps its human translation.** The top-up fills only missing keys, so the
  **473 keys hand-translated by [@alessandroquirino-lab](https://github.com/alessandroquirino-lab)
  survive bit-identically** — community review continues in #625.
- **A font pipeline for non-Latin scripts.** Rajdhani covers Latin only, so Cyrillic and CJK
  would have rendered as empty boxes. The UI font now carries a fallback chain: full Noto Sans
  for Cyrillic/Greek (620 KB) plus Noto Sans SC/JP/KR **subsets built from exactly the glyphs the
  translations use** — 794/812/196 KB instead of ~16 MB each, so the browser build grows by
  ~2.4 MB rather than ~50 MB.
- **Tone, not just words.** Every language got register instructions — informal "je/jij", "ty",
  "sen", "você", "ты"/"ти", Japanese です/ます, Korean 해요체 — and a homoglyph pass cleaned Latin
  lookalikes out of Cyrillic words. Dutch got a post-pass for the IJ digraph (IJzererts, not
  Ijzererts).
- **The translation tool got fast.** Chunks now translate concurrently (`--workers`), turning a
  full 61-request language pass from the better part of an hour into minutes — which is why ten
  languages fit into one release.
- Known limits: native-speaker review and an on-screen overflow check are pending for the new
  locales (CJK and Cyrillic strings run long), and chat characters outside the subset show
  placeholder boxes in the browser build (desktop falls back to OS fonts).

### 🎨 Paint your whole astronaut, not just the face (#874, #875, #882)

- The pixel editor that used to draw only your face now opens for **torso, arms, legs and the
  space-suit helmet**. Each face of the part is painted **full-size on the 512 px canvas** — the
  same 16 px cells as the face editor — with the part's other faces stacked beside it as live
  labelled tiles you click to switch to. The whole-part overview repaints while you draw.
- **Arms and legs are painted per limb**, not mirrored, so you can go asymmetric. Torso has no
  top, boots have no soles, and the helmet has no front — it's open, your face stays visible.
- Unpainted pixels fall through to the part's tint colour, so the existing colour pickers keep
  working underneath a painting, and an unpainted avatar looks exactly as it did before. Your
  paint is saved with your character and shown to everyone else in multiplayer.
- Fixed from the first playtest: painted parts rendered inside-out (hollow shell, near face
  missing) because the generated meshes' triangle winding was inverted.

### 🐾 The animals stopped repeating themselves (#876, #877, #878, #879, #880)

- **Every call is a little different now.** Idle, alert, attack, hurt and death cues jitter their
  pitch ±7 % and volume ±15 % around the species' voice — recognizably the same animal, never
  twice the same sound. Planet enemies jitter subtler (±5 %) — they are machines.
- **Animals answer each other.** Roughly one in five idle calls schedules a quieter reply half a
  second later from a slightly offset spot, so a herd reads as a conversation instead of a
  metronome.
- **26 new recorded calls**: a second take for all 22 signature calls, picked 50/50 at call time,
  plus four new voices for thin habitats — `burble` (water), `sizzle` (lava), `keen` (air) and
  `thrum` (caves).
- **Cave echo and underwater muffle finally work in the browser.** Unity Web silently ignores
  audio filter components, so both effects had simply never played on glitch.fun. They are now
  **baked into the sound itself** — a small Schroeder reverb with a 0.9 s tail and a 680 Hz
  low-pass, rendered once per clip and cached — which also makes the cave sound identical on
  every platform.

### 🐛 Fixes

- **HUD toasts speak your language again.** Five server notices — the space-zone return, trade
  window close, docking status, respawn notice and respawn options — printed the raw message key
  (`@srv.space.returned`) instead of the translated sentence.

### 🛠️ Behind the scenes

- The marketing screenshot set (DE + EN) was regenerated on the current continents worldgen, and
  the capture tool learned sandbox worlds, toast-free frames and a planet-aimed space shot.
- New `tools/wix-i18n/` tooling audits, creates and imports the website's translations, so
  blocksbeyondthestars.com can follow the game into all fourteen languages.

## [2026.8.9] — 2026-08-09

A small, sharp follow-up to the shaper release. The headline is pure building comfort: rotate
**any** block before you place it — furniture included — with a ghost preview hovering in the
world so you see exactly what a right-click will give you. Behind the scenes it closes two
long-hunted save/spawn bugs — the base you built beside your ship no longer vanishes on
reload, and fresh spawns stop waking up entombed at the world origin — and stretches the
research tree so knowledge stays worth hunting past the first evening.

### 🔄 Rotate any block before placing (#863, #866)

- **Furniture obeys the rotate key.** Beds, campfires, rugs and flower pots turn with **R**
  before placing, cycling Auto → four quarter turns → Auto. Yaw-only by design: the up-face
  stays pinned, so furniture turns but never tips — sitting, healing, warmth and home-spawn
  keep working exactly as before.
- **A ghost shows what you'll get.** A translucent hologram-blue preview of the held form
  hovers in the exact cell and orientation a right-click would place. It is built from the
  same geometry the world mesher uses, so built-in shapes AND your own custom forms preview
  true. Auto mode mirrors the server's derivation exactly; parked-ship cells get no ghost
  (structure edits have their own rules).
- **Rotation speaks human now.** The HUD toast says "Upright / Upside down / On its side ·
  90°" instead of axis-speak ("+X · 90°"), localized in EN/DE/FR/ES. And while a rotatable
  block is held, the controls hint appends "R rotate · Shift+R back" — the rotate key existed
  before but was undiscoverable in-game.
- **Shift+R cycles backwards.** One overshoot no longer costs a full lap through all 24
  orientations.

### 💾 The base beside your ship survives a reload now (#870, #871)

- "Singleplayer doesn't save my game" — build beside the starter ship, quit, reload: the
  build was gone. The save itself was written correctly and then **deleted on the next
  join**: a one-time migration meant to erase legacy stamped ship hulls ran on **every**
  placement of the landed ship (join, landing, respawn, ship switch …) and wiped all
  persisted block edits in a box around the parked ship — footprint plus a margin ring,
  and 8 blocks underground, exactly where a new player builds first (mined tunnels
  refilled too). Now new worlds never run that cleanup at all (they can't carry legacy
  hulls), and old saves clean **once per pad**, recorded in the world metadata — builds
  placed afterwards are safe forever. Also fixed on the way: the chunk re-stream after a
  cleanup skipped the box's max faces, the source of the lingering client-side ghost
  blocks.

### 🛬 Fresh spawns no longer wake up entombed at the world origin (#865, #867)

- On every join, the client streamed position reports **before** it had processed the
  server's spawn — from the scene-default transform near the world origin, falling through
  unloaded terrain. The server trusted them and overwrote the freshly computed ship spawn
  within ~100 ms; the buried-player rescue then found the player sealed in the origin column
  and dug them out into a random cave, up to 10,000 blocks from their ship. This was the
  root cause behind the void-fall reports on fresh worlds (#834). Fixed on both ends: the
  client now freezes entirely (no gravity, no input, no movement reports) until it has
  adopted its spawn, and the server arms a spawn-adoption gate after every authoritative
  placement — join, travel landing, respawns, rescues — dropping far-away ghost reports
  until the client proves it took the spawn. The server gate protects old clients too.

### 🔬 Research pacing: the tree unlocks like a journey again (#862, #864)

- The blueprint tree still unlocked too fast: knowledge thresholds were compressed (64 % of
  blueprints at 20 or less), so a normal first session on the starter planet knowledge-satisfied
  ~60 of 70 blueprints within 1–2 hours, leaving materials as the only brake. Thresholds are
  now stretched nonlinearly: starter blueprints (≤ 10) stay exactly where they are, the
  mid-tier moves ~×2.5 (15 → 40, 40 → 100), the top end ~×2.2 (100 → 220) — endgame now lands
  around 200+ knowledge, matching the story-beat thresholds that already assumed the larger
  scale. Data-only: prerequisite chains stay strictly monotonic, scan/tame/minigame income and
  story pacing are untouched, and banked knowledge plus everything already unlocked stays
  yours.

### 🎨 Six blocks got their real faces (#868, #869)

- Ladder, stairs, station core, trading post, mission board and storage container never had
  a real texture — the atlas painted a flat procedural colour tile, and the inventory,
  hotbar and crafting icons showed the same blank tile. All six now ship proper generated
  textures, fixing the item icons and the in-world block faces in one pass — the same
  treatment every other machine and furniture block already gets.

## [2026.8.8] — 2026-08-09

The shaper release: design your own block shapes in a 3-D editor and build with forms nobody
else has — then pass them on as stencils or share codes. Around that headline the update
clears a whole evening of playtest walls: you can finally dive (and build!) under water, a
full backpack no longer stops the drill, your ships survive leaving and reloading a world,
and creatures stopped walking through walls.

### 🧊 Eigene Formen — design your own block shapes (#842–#847)

- **The shaping tool** (workshop, cheap blueprint — the paint tool's twin): right-click and a
  **3-D form editor** opens. You draw one horizontal layer at a time with the layer below
  ghosting through, mirroring and copy-layer-below at hand, a **4³/8³ grid toggle**, a live 3-D
  preview built from exactly the geometry the world will render, and a detail counter that keeps
  a form inside the collision budget.
- **A form is a real shape.** Craft it onto any material through the normal 1:1 shape exchange —
  it places, mines, stacks and persists like any built-in ramp or panel, **collides exactly as
  it looks**, and its hotbar icon is its own silhouette. Up to 45 custom forms live per world,
  registered once and shared by everyone playing in it.
- **A "My forms" library that travels.** Saved forms live on your machine, world-independent —
  just like the paint library. Saving under the same name replaces, so refining a form doesn't
  leave a trail of near-duplicates.
- **Three ways to share.** Aim the tool at a block someone else shaped and the editor opens
  pre-loaded with their form, credited to its designer. Stamp a **stencil** and trade it as a
  normal item — right-clicking it files the form into your library. Or export a **share code**,
  a text snippet you can post anywhere; codes are fully validated on import.
- The **paint library** caught up on the same comforts: designs carry player-chosen names and a
  designer credit when copied off a block, and paint got the same export/import share codes.

### ⛏️ A full backpack no longer stops the drill (#853)

- Mining used to be refused outright once your backpack and cargo hold were full — the block just
  would not break. Now it always breaks, and whatever does not fit lands on the ground as a small
  block packet. Packets **stack**: further overflow joins the bundle already lying there instead
  of littering one per block, and an area drill's whole burst leaves a single packet. Walk near
  one with room to spare and it flows back into your inventory by itself — no key, no prompt.
  Packets survive saving and reloading, and a dyed or shaped block keeps its own stack inside the
  bundle. Defeated creatures drop their loot on the ground the same way instead of it being lost.
  (Mining out in space keeps the old refusal: there is no ground to drop onto, and the block
  simply stays put.)

### 🌊 Under water at last: dive down, build up (#858, #851)

- **You can actually dive now (#858).** Swimming down in deep water instantly bounced you back onto
  the surface, once per second, with "You were stuck in the rock — dug out." The rescue that frees
  players sealed inside blocks mistook every submerged swimmer (and every ladder climber, and anyone
  in a kelp forest) for a player entombed in rock, because water counted as a solid block in the game
  data. Water, ladders, torches and lanterns are now correctly non-solid, the rescue only triggers on
  blocks that can really trap a body, and it still frees players genuinely buried in stone. NPCs keep
  treating water as a wall — nothing wanders into ponds or spots you through a lake.
- **You can build under water (#851).** Every placement while swimming was refused with "Target is
  not empty" — the cell you aim at under water holds water, and the server only accepted air. A block
  now displaces water or lava, so underwater walls, pillars out of a lake and a dry room on the
  seabed are possible at all; water only yields to a tier-3 mining beam, so before this there was no
  way around it either. Doors and torches keep their refusal: a door lives in an air cell the water
  would flow straight back into, and a torch is an open flame.

### 🚀 Your ships survive a reload (#848)

- "I start a world, leave the game, load it again — and my ship is gone." Two save gaps, both
  data loss, both closed. The **landing pad** you parked on was never saved: a reload re-parked
  the ship on the first free pad — pads are spread across the whole globe, so the ship could end
  up thousands of blocks from where you stood. And only the **active** ship was saved at all:
  every other ship you crafted or claimed from a wreck was silently deleted by the next load,
  cargo included. Now the pad and the **whole fleet** persist, each ship with its own cargo, and
  crafting, claiming a wreck or switching ships saves immediately instead of waiting for the next
  autosave. Existing saves migrate automatically — the ship you had is still there.

### 🐾 Creatures respect walls now (#855)

- Creatures and insects could pass through walls and closed doors — a drone circling your base
  would drift straight through the wall, and butterflies fluttered through ceilings. Creature
  movement is now blocked by the world for **every habitat** (walkers, swimmers, fliers): body
  and path are checked the same way NPC movement already was, herds spawn outside walls instead
  of inside them, and a companion stopped by a wall catches up through a 24-block leash instead
  of clipping after you. The small ambient fauna got the same treatment: flies, hoppers and
  crawlers veer off at walls, stay out of ceilings, and crawlers sit on the surface instead of
  half inside the ground. Grass no longer counts as a wall for NPCs.

### 🎮 One evening's playtest, nine fixes (#833–#841)

- **Creative and Sandbox worlds fly (#836).** Double-tap **Space** to lift off, Space to rise,
  Ctrl/C to sink; collision stays on, so you can still land on things and build against them.
  Flight was there all along as the admin command `/fly`, which nobody ever found — a young
  playtester asked for it in capitals while sitting in a Creative world that technically had it.
  Both non-Explorer modes now say so, in the creation panel and in the on-screen controls hint.
  Existing Creative saves get it too, not just newly created ones.
- **Logs have growth rings (#837).** Blocks can carry a separate top/bottom texture, and the first
  one to use it is `wood_log`: cut ends show end grain instead of bark, tinted with the same
  per-world bark hue as the sides. The mechanism is what grass-on-top and crate lids will want next.
- **A finer face editor with real tools (#840).** Faces are drawn at **32×32** instead of 16×16 —
  four times the pixels, enough for an eye that isn't two dots. Every face drawn at the old size
  still works and is scaled up automatically. New **Pick colour** eyedropper takes the colour under
  the cursor, and a **colour wheel** lets you choose by dragging a point around a hue ring (it snaps
  to the palette — a face stores one hex digit per pixel, so 16 colours is the format's ceiling).
  Both the main-menu Avatar Designer and the in-game Character tab share the editor, so both gain it.
- **Hovering scan-drones actually hurt now (#833).** A drone's own AI holds a 4–10 block standoff
  ring and floats 4 blocks up, so it never came within the server's 4-block damage aura — it circled
  you firing a laser and was mechanically harmless, exactly as a player reported. Ranged machines now
  damage out to their firing range (16 blocks, matching the laser you can see), still blocked by
  cover, with the drone's damage lowered to 2/s to suit the longer reach.
- **No more hitting through walls, in either direction.** The wall check on player attacks only
  applied to weapons reaching over 6 blocks, so every melee weapon swung straight through cover — and
  a client that sent no aim data skipped the check entirely. Every attack is now sightline-gated, the
  same way enemy attacks already were.
- **Players are no longer sealed inside rock (#834).** A player restored inside solid blocks is dug
  straight up to the first gap with standing room, or moved to the ship/landing pad if the column has
  none. The void guards never caught this: they ask whether there is ground below you, and someone
  entombed at the world origin has stone in every direction. One report showed a player motionless at
  (0.5, −85.5, 0.5) for a whole session with 7550 stone blocks around him.
- **Sneaking holds at corners too (#839).** The sneak edge-stop tested the two axes separately, so
  walking diagonally off an outside corner passed both checks and dropped you anyway. "In Minecraft
  fällt man nicht runter wenn man sneakt aber hir schon."
- **World names are checked as you type them (#835).** Characters that cannot go in a save name are
  refused in the box instead of being silently rewritten three layers down, and Create now says
  plainly when the name is empty or already taken. A player typed `Minecraft Wo bin ich?:(` and could
  not tell whether his world had been created, or what it ended up being called.
- **NPCs read as different people (#841).** Five skin tones existed, but four of them sat in the same
  light tan band, so a settlement looked like one face repeated. The palette is spread across the
  range now — and a human NPC from an older server no longer falls back to robot grey.
- **Bug reports describe the right planet (#838).** A `/bump` snapshot read the world the server
  happened to be ticking rather than the reporter's own, so biome, weather, gravity and the
  surrounding blocks could all belong to somewhere else entirely.

### 🌐 Browser: the splash screens and the intro speak again (#831)

- In the WebGL build the studio splash, the title splash and the intro cinematic showed raw
  localization keys instead of their texts, because the localizer waited for the entire content
  download (30+ files, fetched one at a time, re-downloaded on every start) while those screens run
  on fixed timers. The locale files are now fetched first and the localizer is published
  immediately, the remaining files download in parallel, and an already-current cache is reused
  instead of refetched. The intro's voxel ship, which the browser build silently skipped when
  content hadn't arrived yet, is now built as soon as the content lands.

### 🛠️ Internal

- README and the contributor rules stopped calling the game bilingual: EN/DE/FR/ES ship complete,
  `en.json` + `de.json` are the mandatory pair every new key must land in, and everything else
  falls back to English per missing key (#830).

## [2026.8.7] — 2026-08-08

The homestead release: the update that makes your base a home. Blocks take painted 32×32
pixel art now, the hand-tier gets a bed, a campfire, chairs you can actually sit on and
ladders you can actually climb, and a founded base finally has air — the whole base zone is
a life-support field, and sealed rooms of airtight materials extend it as far as you care
to build. Fire became something you start and fight, not just something lava does to you.
And the game now speaks French and Spanish — every toast, minigame and server message
included.

### 🎨 Paint your blocks — your art on your walls (#817–#821)

- **The paint tool** (workshop, cheap blueprint): use it on any placed solid block and a
  **32×32 pixel editor** opens — the same editor the ship faces use, just finer. Paint,
  confirm, done: the design sits on the block, on plain cubes and every shape alike, and
  other players see it too.
- **A design library that travels with you.** Save designs locally and reuse them in any
  world and on any server — your gallery sign, your warning stripes, your kitchen tiles.
- **Designs are shared per world, Minecraft-map style**: identical art is stored once
  (up to 256 designs per save). Mining a painted block returns the plain block — paint is
  decoration on the world, not an item variant.
- **Moderation from day one**: `/reportpaint` files the nearest painted design for review,
  and admins can `/paintwipe` a player's designs — wiped art blanks everywhere, live and
  across restarts.

### 🛏️ Make yourself at home — bed, campfire, furniture (#803–#809)

- **The bed** (hand recipe: logs + fibre, no blueprint) — **E** sets your home spawn, and
  resting near it heals you. The researched heal tank stays the clear upgrade: it heals
  faster and also feeds and recharges.
- **The campfire** cooks: **cooked meat** fills you far better than raw and even heals.
  It also warms — cold nights near the fire read as comfortable — and it never spreads.
- **Sit on chairs.** **E** on any chair seats you, camera at seat height, look around
  freely; other players see you sitting. Every shapeable material now forms **tables,
  chairs, fences, sheets and pots** through the normal Shape action.
- **Ladders finally climb.** A placed ladder was a full solid cube — you could never reach
  it from the side, and settlement deck holes were plugged by their own ladder. Ladders now
  mesh as a thin wall plate you step into and climb; settlement upper storeys just work.
- **Storage & decor**: a hand-tier **wood box** (small: 8 stacks — the workshop crate stays
  unlimited), a **lantern**, a tintable **rug** and a **flower pot** with a world-tinted
  flower.

### 🫁 A founded base always has air (#782)

- The base protection zone is now a **life-support field**: stand anywhere inside it and
  your oxygen regenerates, even on toxic or airless worlds — *where you can build is where
  you can breathe.* Visitors breathe too; build protection still only binds to owners and
  allies.
- Works above the atmosphere line and under water (an underwater base zone is a dome), and
  ship cabins no longer drain past the atmosphere line either.
- On worlds without breathable air the HUD says so: a "Life support" toast on zone entry
  and the O2 bar names your base as the air source.

### 🚪 Sealed base rooms breathe — and the energy door holds the air in (#793, #794, #795)

- **Build rooms with air.** A room built at your base out of **airtight materials** — stone, metal,
  concrete, brick, glass, and yes, natural rock (dig a cave!) — now gets life support **beyond** the
  base's radius-8 air zone, as long as it is sealed and connected to the base. Dirt, sand, snow and
  plants leak, and so do shaped blocks — a ramp is not a wall.
- **The energy door is THE airtight door.** New craftable **Energy Door** (workshop, blueprint): the
  same walk-through blue field as the ship's hatch. You stroll right through it, the air stays in —
  and because the curtain always seals, an auto-opening door can never depressurise your room. Wooden,
  hinged and sliding doors deliberately don't hold air. Chain room after room, door by door, into a
  whole airtight outpost. The energy door is also placeable in the ship, station and settlement editors.
- **You can SEE the air now.** On worlds without breathable air every founded base shows a soft **blue
  shield dome** over its core, and the O2 bar names what keeps you breathing.
- **Breaking the seal warns you.** Mine a wall (or let a fire eat one) and everyone at the base gets a
  **"no longer airtight"** warning the moment the rooms fall back to suit oxygen — no more silent leaks.

### 🔥 Fire you can start — and put out (#784–#791)

- **Light it.** Swing a **torch** at a plant, a log or leaves and it catches. So does a shot from a
  **laser pistol or plasma blaster** — energy weapons set flammable terrain alight, kinetic ones
  (scrap/gauss pistol) don't. Until now the only fire in the game came from flowing lava.
- **Put it out.** Hit a flame and you stamp it out; you no longer need a bucket of water to fight a
  fire. **Rain and storms** douse fires under open sky — but a fire under a roof or in a cave keeps
  burning, and the ash-rain of a lava world won't help you at all. While rain falls on it, wet
  vegetation refuses to catch in the first place.
- **Fires stay fires, not wildfires.** Flames now creep and fray instead of advancing as a perfect
  wave, and a single fire only spreads so far from where it started — a forest burns, a continent
  doesn't.
- **Nothing you built can burn.** Ships, settlements, stations, factories and claimed bases never
  catch fire. Village greenhouses — wooden frames full of crops — could previously be burned down by
  a splash of lava.
- **The right things burn now.** Pine needles, palm fronds and mushrooms went up in smoke before…
  except they didn't: a burning pine kept its canopy, while kelp and coral burned underwater.
  Flammability is data-driven now, and it matches what you'd expect.
- **Fires survive a save.** Reloading a world mid-fire used to leave flames that burned forever
  without ever turning to ash. They now burn down exactly where they left off.

### 🌍 The game speaks French and Spanish now (#810–#816)

- **Français y Español**: complete translations of every one of the game's ~2,900 text keys,
  plus the story pack. The settings language control is a **real picker** now (with native
  names — "Français", not "French"), and community languages appear in it automatically once
  their translation passes 45% — Italian, you're next.
- **Every text surface is localizable now.** Server messages — join screens, trade, docking,
  repair, admin output, all of it — used to be hardcoded English; German players saw English
  toasts all over. 334 server messages and all 8 minigames now speak the player's language.
- **VEGA and the NPCs follow along**: AI-generated dialogue, banter and mission flavour answer
  in French and Spanish too.
- Wiki articles and What's new entries can carry French/Spanish text and fall back to English
  until translated; the web portal now greets browsers that ask for neither German nor English
  in English.
- For contributors: a new [translation guide](docs/developer/TRANSLATION_GUIDE.md) plus a
  machine-first-pass pipeline (`tools/translate_locale.py`) make the next language a much
  smaller mountain.

### ⬆️ Upgrades are real upgrades now (#798, #799)

- **Tier upgrades consume their predecessor** — the oxygen tank II is built *from* your
  oxygen tank I (and III from II), the advanced scanner from the hand scanner, vibro knife →
  plasma sword from machete → vibro knife, laser pistol → plasma blaster from gauss → laser.
  Upgrade recipes are re-priced as a discount against building from scratch, and every
  consumed item can always be re-crafted (the hand scanner got a cheap rebuild recipe).
- **The Mk3 AI core replaces the Mk2** when built into your ship, salvaging half the Mk2's
  build cost back into your pools — no more dead module hoarding.
- The starter drill and scrap pistol deliberately stay out of any chain: they are your
  zero-energy fallbacks and can never be consumed.

### ⛏️ Powered drills draw suit energy (#796, #797)

- The titanium drill and the mining beam always *declared* an energy cost — and never drew
  it. They now charge suit energy per swing, exactly as their descriptions promise; an empty
  suit rejects the swing with a clear message. The **basic and diamond drills stay
  energy-free** — the diamond drill's "needs no power" niche is real now, and a drained
  player is never locked out of mining.
- The mining beam's area sweep now respects tool tier per block — it can no longer sweep up
  ore the tool couldn't mine directly. Yields are untouched: upgrades buy speed, access and
  area, never more items per block.

### 🧰 The craft list puts buildable things first (#826)

- Crafting and ship-module lists used to show entries in data-file order — locked entries
  buried the next thing you could actually make. Both lists now sort **craftable now** →
  **blueprint unlocked, materials missing** → **blueprint locked**, with simpler recipes
  first within each group. The order doesn't reshuffle as you walk around a station, and
  when something becomes craftable it jumps to the top.

### 🛠️ Internal

- README, USER_MANUAL, DEVELOPER guide and the docs index caught up with everything the
  last month shipped (#825).

## [2026.8.6] — 2026-08-06

The lights-on release: a focused round of fixes. Every ship room is actually lit now — the
Hammerhead's dark aft compartments were the tip of an iceberg where no authored ship layout
contained a single interior lamp — the prologue's opening shot stopped flying the camera
through mountainsides, and two browser bugs that hit glitch.fun players on their very first
minute (singleplayer refusing to connect, joiners falling through unstreamed terrain) are gone.

### 💡 Lights on in every ship room (#776, #779)

- **Every room gets a ceiling lamp.** Multi-room ships were only lit where navigation-light
  glow happened to bleed in through a window: the Hammerhead's bridge was bright while its
  workshop and cabins stayed pitch black, and the Courier, Thunderbolt, Deathblock and Hauler
  interiors got no light at all — no authored layout contained a single interior lamp. Ship
  finishing now hangs a lamp below the ceiling of every station room; this covers all authored
  layouts, the starter ship and player-built ships, without touching the hand-tuned layout files.
- **Light crosses chunk seams now.** Ship hulls mesh in 16-block chunks and a lamp's light
  stopped dead at the seam, so even a lit room could go dark halfway. The whole hull's light
  sources now feed every chunk — in the landed ship, the flight view and the build preview alike.

### 🎥 The prologue camera respects the terrain (#777, #778)

- The first-spawn orbit around your landed ship flew a blind circle — on a mountainside part of
  the ring sat inside the slope and you looked straight through the terrain. The cinematic now
  scans the orbit ring before it starts: it raises the shot if the classic height is blocked,
  sweeps back and forth along the widest open arc instead of forcing a full circle, switches to
  a high crane shot in craters, and falls back to the dim+panel opening when nothing clears.
  A per-frame line-of-sight guard also catches terrain that streams in mid-shot.

### 🌐 Browser fixes — glitch.fun first minutes (#771–#774)

- **Singleplayer connects reliably** (#771, #772): clicking Singleplayer in the browser could
  bounce back to the menu with "Could not connect to the server" — the loading screen handed
  off while the in-process server was still booting. It now waits for the server to be up, and
  the boot-blocking cloud-save lookup times out sooner.
- **No more falling through the world on join** (#773, #774): joining a hosted world from the
  browser dropped you into the void once an 8-second spawn grace ran out, void rescue and all.
  Joiners now hover in place until the floor below has actually streamed in, then land.

### 🛠️ Internal

- New glitch.fun-only build + deploy workflow: a browser-facing fix can now ship to the store
  build on demand, without cutting a full release (#775).

## [2026.8.5] — 2026-08-05

The premiere release: the game opens like a film now. A watchable in-engine intro cinematic
runs before your very first menu, VEGA's prologue got a real staging — letterbox bars, a slow
orbit around your landed ship, a glitch flash as she boots — and she found her voice: radio
chatter crackles along while her lines type out. Story memories flash back with a cinematic
colour grade, and the HUD celebrates the moment your knowledge unlocks new research — a moment
that means something again, because the whole knowledge ladder was stretched so the tech tree
unlocks in waves instead of all at once. A nine-part playtest batch rounds it off, fixing
everything from titans biting through ruin walls to jittery NPC ships in space.

### 🎬 Curtain up — the game gets a real opening (#759–#762)

- **Intro cinematic**: a ~28-second space cinematic between the title splash and the menu —
  starfield and nebula reveal, a sun pan with a voxel fighter crossing, planet approach,
  white-flash hand-off. Rendered live in-engine with the game's actual art, so it always
  matches what you'll play. It runs once per install, any key skips it, and a new
  **"Watch intro"** button on the Credits screen replays it. Captions in English and German.
- **The prologue got a stage** (#754, #760): VEGA's three opening pages play through the normal
  speech panel now (proper width, paging, UI scale — the full-screen black dialog is gone),
  dressed with letterbox, a slow exterior orbit of your actually landed ship, a push-in, and a
  snap back to the pilot seat with a glitch flash as VEGA boots. Esc skips the whole narration.
- **VEGA has a voice** (#761): vocoder radio-babble accompanies every line she types —
  language-independent, so German and English sound equally alive, and it stops the instant
  the line completes or you skip.
- **Memory flashbacks** (#762): story recollections pulse a short cinematic look — light
  letterbox, cooler desaturated colours, a chroma/grain burst — without ever locking your
  controls.
- Fixed along the way: the world-flavour hint no longer jumps the queue ahead of the prologue
  (the reason the opening sometimes only started after pressing [N]), and VEGA lines no longer
  play out invisibly behind the loading screen.

### 🔬 Research announces itself (#763)

- When gathered knowledge pushes a blueprint over its research threshold, the HUD celebrates
  it top-centre: the blueprint's icon, **"New research available!"** and the blueprint's name,
  with a glow pulse, a shine sweep and a soft two-note chime. Respects reduced motion.
- Fixed along the way: achievement toasts were silently mute (their sound cue never existed) —
  they play a rising arpeggio now.

### 🧠 Research pacing — the tech tree unlocks in waves again (#767)

- **Every blueprint costs knowledge now**: the ladder runs from ~3–10 for early tools through
  ~15–40 mid-game up to 60–120 for the late tiers (Deathblock, AI core Mk3, matter
  resynthesizer). Previously the whole tree topped out at 24 knowledge and about half the
  blueprints — all ships included — were free, so everything unlocked almost at once.
- **Deeper prerequisites**: ships sit behind the docking module, cannons behind hull plating,
  the heal tank behind the field medkit, the jump generator behind the radar array, and more —
  the tree reads as real progression chains now.
- **Knowledge faucets capped**: arcade minigames pay per star *newly* earned on that game
  (replays pay nothing, improving your rating pays the difference), story beats award less,
  and taming a species you already tamed no longer trickles endless points.
- Existing saves keep their banked knowledge and everything already unlocked — the new upper
  tiers simply sit above most veterans' totals, so the research tab has surprises again.

### 🦋 Micro-fauna joins the Codex (#757, #752)

- Ambient critters (butterflies, fireflies, wisps, …) can now be scanned with the handheld
  scanner — 28 localized kinds, a new **Micro-fauna** discoveries chapter and a small knowledge
  award per first find. Thermal vision shows critters as small named contacts.
- **The wisp** (#752): the bat-like skyray was replaced by a small drifting glow orb whose
  colour is unique per world — look for it at night.

### Added

- **Low-vitals warning** (#753): any HUD bar (health, oxygen, energy, hunger, hull, shield)
  below 10 % blinks red and beeps until it recovers past 15 %.

### 🔧 Fixes

- Attacking animals no longer walk through the player: hunters hold at a size-scaled ring, roaming
  machines stop at bodies too, and big creatures barely lunge (#749).
- Titans no longer spawn half inside ruins or bite through walls — spawn and movement now check the
  actual body volume (#750).
- "G: loot" no longer shows where the key does nothing (flying/driving), a full backpack reports
  "inventory full" instead of silently no-opping, and the loot sound only plays on actual success (#751).
- The controls hint finally mentions jump and crouch (Space, Ctrl/C) (#755).
- NPC ships in space (traders, UFOs, raiders, other players) move smoothly: buffered interpolation on the
  client, bandit raiders broadcast their approach/leave movement, remote pilots refresh continuously, and
  patrol stop-go easing on the server (#756).

## [2026.8.4] — 2026-08-05

The shipyard release: four new ships joined the fleet — and every one of them started as a
pencil drawing on Justus' sketch pad. The Hammerhead brings the game's first true multi-room
interior, the Courier is the fastest thing in the sky, the Thunderbolt a mid-size gunship and
the Deathblock a slow armoured brick. There is finally a reason to hunt bandits, too: quest
givers put bounties on camps and raider ships. VEGA stopped cutting off her own sentences and
keeps a re-readable tips log, the HUD tells you what you just picked up, planets roll their own
cast of critters with 11 new alien species — and the machines stopped streaming in endless
reinforcements.

### 🚀 Four new ships — drawn on paper, built in blocks (#723, #727–#729)

All four new ship types were designed by Justus — with pencil and sketch pad — and translated
plan-for-plan into the game:

- **Hammerhead** (DE *Hammerhai*): a heavy gunship and the game's **first multi-room
  interior** — a wide 12-block bridge up front, a corridor running aft, workshop and sleeping
  cabins each behind their own interior door, stern airlock between the split engine block.
  Unlocks after the corvette + ship cannon.
- **Courier** (DE *Kurier*): the **fastest ship in the game** — an unarmed messenger with a
  glass nose, swept wings and a raised lookout. "Perfect for those who report battles instead
  of fighting them." Unlocks after the scout.
- **Thunderbolt** (DE *Blitzschlag*): a mid-size strike gunship — inset bridge, full-width
  workshop hall, flank cannons plus a long bow barrel.
- **Deathblock** (DE *Todesklotz*): the slowest, tankiest assault brick in the fleet — stepped
  brutalist silhouette, overhanging quarters, a cannon per flank and quad stacked engines.
  Unlocks after the Hammerhead.
- NPC traders picked the new types up automatically — you will meet them in the wild.

### ⚔️ Bounty missions — quest givers put a price on the bandits (#730, #731)

- **Camp bounty**: a settlement mission board on a planet with an uncleared bandit camp now offers a
  bounty to drive the bandits out. Accepting it marks the camp on your planet map; clear the camp
  (everyone holding the bounty gets credit — co-op friendly) and report back for a reward that beats
  the usual gather jobs.
- **Raider bounty**: station mission boards in pirate systems offer a bounty on the raider ship
  prowling the sector. While you hold it, the raider *will* show up on your next flight — no more
  hoping for the ambush dice. Drive it off and report back; the job is repeatable while the system
  stays pirate country.
- Bounties are only offered where the fight is winnable (they respect the Bandits/space-combat/ship-
  weapon world rules) and use kid-friendly wording throughout — bandits are chased away, never killed.
- The AI mission-text generator knows about bounty jobs now, so generated postings match the job
  instead of reading like a delivery.

### 🗣️ VEGA finishes her sentences — and you can re-read them (#736–#738)

- **No more cut-off speech**: long VEGA lines (German especially) were silently truncated at
  ~4 lines. The speech panel now splits long text into **pages** — [N] turns the page, a
  `(1/2)` indicator shows where you are, and the typewriter plays per page.
- **VEGA tips log**: every onboarding lesson and advisor hint VEGA ever told you is re-readable
  in the ship terminal's Story tab — dismissed a hint too fast? It's all there, in order, story
  pack or not.
- **A proper opening**: new games with a story pack begin with a short three-page text
  prologue — a small ship, no memory of who you are, then VEGA crackles awake. [N] advances,
  [Esc] skips.

### 🎒 You can see what you pick up now (#744, #745)

- **Pickup feed**: a small column above the hotbar announces what you just collected —
  `icon +n item name`, localized, newest at the bottom; repeat pickups merge and count up
  ("+7 berries") instead of stacking rows.
- **Hotbar stack counts**: every hotbar cell shows its stack size in the corner — no more
  opening the inventory to check if you have enough wood.

### 🦋 Every planet rolls its own critters — 11 alien species (#725)

- Butterflies, beetles and fireflies stopped being identical everywhere: each planet rolls a
  deterministic **species subset and colour palette** of its own — planet A's moths are
  consistently different from planet B's, and revisits look the same.
- **11 new alien species** on two new motion styles (parabolic hops and slow balloon drift):
  prismwings and crystal beetles on crystal worlds, embermites and ashhoppers on lava worlds,
  frostmites on ice, gasbags and sporedrifters in lush jungles, skyrays, sandskimmers,
  night-glowing plankton over water and cavemoths underground — crystal, lava and ice worlds
  stop being empty.
- Every individual rolls its own size (rare giants included), rain grounds the flyers and brings
  out worms and snails, and low gravity makes everything float a little floatier.

### 🤖 Enemy pacing — machines stop streaming in, space combat is opt-in again (#740, #741)

- **No more instant reinforcements on planets**: destroying a robot or scan-drone used to summon its
  replacement within a fraction of a second (the spawn timer silently banked time while the population
  was full). Refills now wait a slow, varied 20–45 s (plus a breather after each kill), so machine
  fights are encounters with quiet in between — not an endless stream.
- **Machines let go when you leave**: hostiles far from every player despawn instead of trailing you
  across the planet forever.
- **Space hostiles wait for you to come to them**: drones and UFOs could see farther than their own
  spawn distance, so the UFO started hunting your ship the moment you launched — every single flight.
  Their detection ranges now match the "you choose to fly out to them" design; park at the launch
  point and nobody bothers you.
- **Every flight stops replaying the same wave**: destroyed drones/UFOs stay gone across relaunches
  until the sector re-arms (~8 minutes), the wave sits on a different bearing each launch, and every
  4th flight runs quieter.

### 🔧 Fixes

- **Bandit hold-up dialog fits its text** (#734): long demand lines (the German "Wegzoll" lines
  especially) stuck out past both edges of the dialog — the line wraps now and the panel grows
  to fit.
- **Same seed, same rocks — really** (#719): the mineral-family roll for mineable space rocks
  keyed on the launch seed instead of the saved world seed, so desktop worlds all shared one
  pattern and hosted worlds re-rolled their rocks on relaunch. Every world now grows its own
  stable rock families, as promised.
- **Ambient sounds are stable across platforms** (#720): procedural audio seeded from a
  per-process hash could differ between runs and platforms; it uses a stable hash now.

## [2026.8.3] — 2026-08-04

The wonders release: planets stopped playing it safe. Terrain generation learned a whole
catalogue of spectacular landforms — a canyon that girdles the globe, crater chains with ejecta
rays, stone arches and sea stacks, tiered sky islands with endless waterfalls, cenotes, lava
tubes and caves that finally open to the surface — and about half of all new worlds now form
true continents with real oceans between them. Combat grew up alongside: enemies show health
bars, the crosshair does real aiming (with auto-aim as a world rule you can switch off), and
ship weapons finally respect their own cooldown and energy budget. Rounding it off: multi-biome
worlds draw from their whole biome pool instead of always the same entries, and NPCs stand on
the ground, step into the light and stopped being clones.

### 🏜️ Terrain wonders — the surface gets spectacular (#698–#703)

- **A world-girdling mega-rift**: a rare planet carries one canyon system that wraps the entire
  globe — a genuine "Valles Marineris" you can follow around the world.
- **Craters became events**: complex craters with central peaks and terraced walls, crater
  chains, and bright ejecta rays streaking away from young impacts.
- **Per-world terrain grain**: dune seas and mountain ranges now run in a consistent per-world
  direction instead of isotropic noise — deserts read as wind-swept, ranges as ranges.
- **Geological set pieces**: hexagonal basalt-column fields, travertine terrace pools,
  penitente ice-blade fields, salt polygons, ring-mountain calderas and whole-planet
  escarpments.
- **Hybrid worlds**: landform styles and archetypes can mix on one planet now, so a world can
  be canyon country on one face and dune sea on another.
- This is a **one-time reshape**: terrain you have not yet explored regenerates with the new
  formulas — already-built and explored chunks keep their exact shape.

### 🌊 Continents and true oceans — new worlds only (#704)

- Large planets (roughly half of them, the start world included) now split into **continental
  platforms and ocean basins**: domain-warped coastlines, a shelf falling away into deep sea,
  and the sea level flooding the basins — so beaches, rivers and landmarks react for free
  (a massif offshore becomes a volcanic island, a rift becomes an ocean trench).
- Lava-ocean worlds roll **basalt continents** in their magma seas.
- **Existing worlds are untouched** — continents apply only to newly created worlds (server
  flag `continents=off` opts a new world out).

### 🪨 Real overhangs: arches, sky islands, cenotes (#705–#707)

- The engine's strict one-surface heightfield learned **multi-band columns** — and the first
  tenants are **natural arches**, **sea stacks** off rocky coasts and **hoodoo** rock spires.
- **Multi-tier sky islands** stack floating layers with stalactite undersides and **endless rim
  waterfalls** pouring into the landscape below.
- **Cenotes** — sinkholes with overhanging lips and a pool at the bottom — and vast
  **underground mega-caverns** with their own lakes.

### 🕳️ The tunnel carver: caves finally reach the surface (#708, #709)

- A new deterministic **tunnel carver** threads worm-like passages through the rock — until now
  every cave was a sealed bubble; **cave mouths and skylight shafts** now genuinely open to the
  sky, so you can walk (or fall) into the underground.
- **Lava tubes** wind under volcanic terrain, waterfalls carve **plunge pools** where they
  land, and glacier worlds crack into **crevasse fields**.

### ⚡ …and generation stayed fast (#712)

- All of the above initially made every chunk pay for every wonder — a per-world wonder
  profile, per-cell tunnel caching and distance-first feature probes brought generation back
  near its old speed, with **bit-identical terrain** (verified by a hash sweep over every
  planet type).

### ⚔️ Enemy health bars, real aiming, honest ship weapons (#692–#694)

- **Every damageable enemy shows a health bar** — bandits, creatures, space hostiles, planet
  machines — green fading through amber to red (companions cyan), visible while in combat or
  under your crosshair, with distance fades matching the nameplates. A **hit marker** flashes
  when your shot lands. Client toggle in Settings.
- **The crosshair aims now**: whatever is genuinely under your crosshair is the target; the old
  magnetic lock — which could kill an enemy *behind your back* — only assists within a forward
  cone. **AutoAim is a world rule** (default on, so nothing changes unless you want it to):
  switch it off and only real crosshair hits (on foot) or boresight shots (in space) land,
  misses visibly striking the terrain.
- **Ship weapons play by their stats**: the server now enforces weapon cooldown and a
  reactor-fed energy pool — no more fire-rate cheating — and weapon range/cadence come from
  the fitted module (the tier-2 laser finally gets its longer range).

### 🌿 Multi-biome worlds use their whole biome pool (#696)

- Worlds rolled how *many* biomes they get but always took the *first* entries of their type's
  pool — so the pool's tail biomes never appeared anywhere, and the first one appeared
  everywhere. A seeded per-world shuffle now also decides *which* biomes make the cut
  (newly generated chunks only).

### 🧑‍🚀 NPCs: feet on the ground, faces in the light (#711)

- **No more hovering**: settlement folk, camp NPCs, bandits and enemies stand on the actual
  blocks (a leftover +0.5 offset from the worldgen overhaul), and settlement NPCs re-ground
  as they stroll — dig the floor out from under one and it drops.
- **Brighter**: avatar tint textures rendered at ~40 % of their authored brightness — they are
  normalised at load now, outfit palettes widened and lifted, and bandit/Guardian materials
  get the standard lighting lift.
- **No more clones**: per-NPC size variation, independent trouser colours, varied android
  chassis tones (only ~60 % of researchers are robotic now), and each NPC rolls a stable
  face and hair of their own.

## [2026.8.2] — 2026-08-03

The prospector release: space became worth mining. Asteroids gather in real belts you can see
on the flight chart, every mineable space rock now rolls its own size and mineral family —
water ice included — and EVA mining finally plays by planet rules: the right drill, real
hardness, and ore that never silently vanishes. The planets kept pace: star systems and worlds
carry proper names now, seas and larger lakes grew sandy beaches, and temperature turned from
a HUD number into a real survival pressure that drains your suit before it ever touches your
health. Rounding it off: a true Sandbox mode at world creation, flora that stopped growing in
identical rows, scrollbars you can actually see, and selection lists that finally speak your
language.

### ✨ Real asteroid belts (#683)

- **Asteroids now orbit in belts** (new worlds): all of a system's landable asteroids share 1–2
  orbit annuli — the outer belt just beyond the outermost planet, big systems sometimes a second
  inner belt — instead of scattering randomly across planet orbits. Existing worlds keep their
  layout untouched.
- **Belts are worth flying to**: every asteroid body carries a cluster of ship-laser-mineable rocks
  at its position, and launching *from* an asteroid surrounds you with a dense 9-rock field instead
  of the usual three.
- **The flight chart shows the belt as a belt**: one translucent band with its own label
  ("Asteroid belt" / "Asteroidengürtel") instead of a smear of stacked orbit rings.

### ✨ Planetary rings in more colours (#684)

- Ring systems now roll a seeded **material family** — icy white stays the norm, but dusty tan,
  rocky grey and a rare pale violet appear too — so two ringed planets under the same star no longer
  wear the same colour. Applies everywhere a ring is drawn (orbit view, surface sky, horizon band),
  including in existing worlds (same seed → same tilt and band pattern, just richer tints).

### 🪨 Every space rock is its own rock (#687)

- Mineable mini-asteroids were identical clones — the same r=2 sphere, titanium core, iron/copper
  shell. Now each rock rolls a seeded **mineral family**: stony (40 %, iron veins in rock), metallic
  (25 %, the classic titanium core), **icy (20 %, hand-mineable water ice around a rocky heart)**,
  carbonaceous (10 %) and rare crystalline (5 %) — mirroring the landable asteroid families.
- **Three sizes**: common pebbles (7 blocks), the classic mid rock (33) and rare boulders
  (123 blocks) with a core that grows with the rock. Shoot-down loot follows family *and* size —
  boulders pay out more.
- The **first rock of every field stays the classic metallic mid-size**, so each field still
  guarantees one titanium core. Fully deterministic per world seed; respawned rocks keep rolling.

### ⛏️ EVA asteroid mining plays fair (#685)

- During a spacewalk, any asteroid block fell to a **single bare-hand click** — no drill, no
  hardness check, titanium included — and with a full inventory the ore was silently destroyed.
  EVA mining now runs the same rules as planetside: drill kind and tier gate the block, hardness
  takes several timed hits, and a full inventory refuses the break (progress is kept, the block
  drops on the next hit once there is room).
- Client-side it feels like planet mining too: **hold-to-mine** at drill pace, and the aim marker
  warms from cyan to orange with your progress. Editing your *own* ship or station stays instant —
  that's construction, not prospecting.

### ✨ Star systems & planets got real names (#678)

- **Several system-name registries** instead of one pattern: coined proper names ("Tharion"),
  catalog designations ("HX-113"), two-part region names ("Ember Veil", "Korveth's Reach") and rare
  archetype-flavored names — pirate space sounds menacing, hub space busy.
- **Planets are designations with a hierarchy**: Roman numerals ("Tharion II"), exoplanet letters in
  catalog systems ("HX-113 b") — while landmark worlds (ringed planets, the Lone Giant, Twin Worlds,
  the Hub capital and your start planet) carry coined proper names flavored by their biome: ice
  worlds sound cold, lava worlds harsh. Twin Worlds share a name stem; moons letter after their
  planet ("Tharion II-a") or get short coined names of their own around landmarks.
- **No more baked-in English**: asteroid fields and wrecks are coined single words paired with the
  localized kind label on the map; a Hub's first station is a coined port ("Port Halvek").
- Names are unique per galaxy, profanity-guarded (EN+DE), and **retroactive**: they are display-only,
  so existing worlds keep every waypoint and simply wake up with better names — the underlying body
  layout is pinned byte-identical by a new regression test.

### 🏖️ Beaches along seas and larger lakes (#679)

- Coastlines grew **real sandy beaches**: a 1–3-block band of sand above the waterline plus a sandy
  shallow-water apron, with the varied topsoil giving a genuine 2–5-block deep sand layer. A
  large-scale coast mask keeps it natural — roughly half the shore is beach, alternating with bare
  rocky stretches, and beach width follows the slope (flat coast → wide beach, cliff → sliver).
- **Larger lakes and deep ponds** get shore rings too; small pools, 1-wide rivers and puddles stay
  as they are. Tropical shores grow the occasional palm.
- Planets can override the material via a new `beachBlock` data key (default sand); dry, airless
  and lava-sea worlds keep their coasts beach-free. Applies to newly generated terrain — coastline
  you have already explored keeps its current shape.

### 🌡️ Temperature is now a real survival hazard (#666–#671)

- The temperature reading stops being cosmetic: outside the suit's comfort band (−5…40 °C, with a
  grace margin) the suit's climate control **drains suit energy** — the further past the band, the
  faster — and only once the suit is empty does **slow exposure damage** set in (starvation-level,
  never a burst kill). Tuning anchor: an ice world costs you ≈ 10 minutes unprotected, ≈ 30 with
  the tier-2 insulation liner.
- **Shelter genuinely matters**: underground blends toward a steady cave temperature over ~24 blocks
  of depth, a roof over your head halves the severity, an open fire warms like a campfire (capped
  inside the comfort band), lava-adjacent spaces run hot and ice-walled rooms stay freezing.
- **Vacuum joined in**: on EVA and above atmosphere, a sun-tracking hull temperature (−150…+120 °C
  across the day curve) feeds the same math, and the HUD shows a temperature readout on spacewalks.
- The mechanic is the first real consumer of the `EnvironmentalHazards` world rule — Creative and
  Sandbox worlds stay exempt.

### 🏗️ True Sandbox mode at world creation (#662, #677)

- The new-world panel offers a third mode next to Explorer and Creative: **Sandbox** — free
  crafting at every craft/build/repair site, no oxygen or hunger drain, planet enemies and bandits
  off, cheats allowed, and all creative grants (blueprints, ships, starter kit) forced on. The mode
  is baked into the save, so it persists without any flags.
- Fixed the launch-week playtest bug where the creative starter kit **filled the whole backpack**
  and mining rejected every swing with "inventory full" (#677): the kit's materials now land in
  the starter ship's cargo hold, tools in the backpack — mining works from the first minute.

### 🌿 No two plants alike (#675)

- Flora of one species used to render as identical clones on a grid. Every plant now rolls
  **individual size** (a natural bell around the norm with rare outliers), a height-vs-width
  squash, a slight off-grid position and its own rotation — and an 8×8 patch pattern lets meadows
  form visible stands of taller and shorter growth.
- Purely visual and hash-deterministic from the world position: every client sees the same plants,
  saves and protocol untouched, applies to existing worlds. Player-built shapes stay exact.

### 🖱️ Scrollbars you can see (#664)

- The in-game ship-computer menu (all 12 tabs), the Codex and the vendor trade dialog scrolled
  only via mouse wheel with nothing showing that a list continues below the fold. All of them now
  show a thin auto-hiding scrollbar that vanishes when a page fits.
- The vendor dialog's offer list had **no scroll region at all** — long lists clipped past the
  panel. It scrolls properly now and keeps its position across a trade.

### 🌍 Selection lists speak your language (#672)

- Playing in German (or Italian), several in-game lists still showed raw English ids and enum
  names. Now localized: tech-tree categories, map body kinds and statuses, mission objective
  types, story-log chapter tags, the crafting "Max" button, the quality presets in Settings and
  the arcade "no record yet" line.

## [2026.8.1] — 2026-08-02

The wildlife release: this one belongs to the creatures. Every world's fauna rolls from a bigger,
more varied pool now — translucent medusae drifting over the treetops, elephant-scale titans
striding across the plains in small herds, fish that actually school. And everything out there
*moves* like it lives in the world: animals respect cliffs, water and the walls you build (pens
work now!), steer around obstacles instead of jittering against them, ease over slopes, bank into
turns, and scatter as a herd when you startle one. Water learned two lessons of its own — a flowing
stream no longer fossilises into permanent puddles when the server restarts, and shallow water
finally *looks* shallow. Rounding it off: the chat overlay stays out of your HUD's way and fades
when it has nothing to say, `/tp` finally works in singleplayer, and the Italian translation
passed the 20 % mark.

### 🦑 New creature kinds: medusae and titans (#637, #638, #640)

- **Medusae** — jellyfish that drift through air and water: a translucent pulsing bell over a
  glowing nucleus, 6–10 tapering tentacles hanging in a ring, usually bioluminescent, never
  hostile. About a quarter of all air- and water-dwelling species roll the new body plan, and each
  species keeps its own preferred hover altitude instead of the old fixed height.
- **Titans** — elephant- and giraffe-scale land megafauna, far beyond the old size cap: pillar
  legs, a stacked neck (giraffe) or a trunk (elephant), horns worn as ivory tusks. Titans are
  **huntable** — 3.5× the health, 3–6 drops — but a provoked titan genuinely bites back, so bring
  a plan. They need open, level ground to appear, notice you from further away, and stride with a
  slow, weighty cadence.
- **Bigger rosters** — species per world go from 3 to 5 (sparse worlds) and 6 to 9 (rich worlds),
  with a guarantee that every world gets ground *and* airborne wildlife, and water worlds get
  something swimming.
- **Existing worlds keep their fauna.** Known species keep their names, colours, temperaments and
  stats; some gain a new silhouette, and the new species join alongside them.

### 🐾 Herds, flocks and schools (#639)

- Species have a **social group size** now: titan herds of 2–4, schooling fish in 3–5, flocks of
  fliers, the occasional grazer pair — placed together at spawn and gently held together while
  they roam, so a herd reads as a herd instead of scattered loners.

### 🏞️ Creatures respect the terrain — and your walls (#648, #650, #651)

- **Cliffs are walls now.** A land animal no longer glides diagonally *through* a mesa face,
  never marches along the seabed fully submerged, and a water creature never beaches itself and
  roams dry land until it despawns.
- **Fauna reads the actual built world**, not just the generated terrain: player-built walls
  block, dug pits hold, ramps and floors carry. **You can pen animals with builds now.**
- **They steer.** A blocked creature probes detours to either side and follows the obstacle around
  — contour- and wall-following instead of bumping and re-rolling — and neighbours keep a
  size-scaled personal space, so groups flow instead of stacking.

### 🎞️ Motion that reads as alive (#652, #653, #654)

- Land walkers **ease over terrain** instead of snapping a block per step; fliers swoop smoothly.
  Bodies **pitch along their real motion** and airborne species **bank into turns** — hoppers
  keep their pop, because the pop *is* their gait.
- **Herd panic:** wound or startle one animal and its kin nearby bolt with it — or, for the
  brave species, turn on you together. Fleeing animals **jink** in a zig-zag instead of running a
  straight, aimable line.
- The client now **extrapolates creature motion** between server updates, so everything above
  moves crisply instead of stepping at the network rate.

### 💬 Chat that stays out of the way — and /tp in singleplayer (#636, #642)

- **The chat scrollback fades out** 12 s after the last message and comes back at full strength
  the moment anything happens. It moved into the free lane of the HUD — it no longer covers the
  scan panel, the left hotbar cells or the controls hint — follows the UI-scale setting, and hides
  itself while you pilot a ship. A **Chat display** comfort setting (fade out / always on / off)
  and a rebindable **J** key put you in charge.
- **`/tp` works in singleplayer now.** Admin cheats were unreachable in solo play — the bundled
  game now enables them for your own worlds (existing saves included), while guests on hosted
  worlds and dedicated servers stay gated as before. Server replies land in the chat scrollback,
  so `/tp` can actually show you its target list.

### 💧 Water behaves (#657, #658)

- **Flowing water survives a restart as flow.** The simulation's per-cell state was memory-only,
  so every restart froze transient tongues into permanent, never-receding one-block sheets. Flow
  state is persisted now: an orphaned stream retracts properly across restarts. (Sheets fossilised
  before this release stay — mine them away.)
- **Shallow water sits visibly below the bank** instead of flush with it, so a thin spread tongue
  no longer looks like a full basin standing a block high in the landscape. Lava stays flush on
  purpose — it is opaque and you stand on it.

### 🔊 Water sounds like water (#655)

- Every fluid-simulation step used to play the full block-placed knock — flowing water sounded
  like rhythmic hammering. Fluid transitions are silent now and feed the ambient brook/waterfall
  rush instead, which was always the intended water sound.
- Mining and placing cues are **positional 3D** now: full volume at your own hands, fading with
  distance, silent past 20 m — so another player building nearby no longer sounds like they are
  inside your helmet.

### 🇮🇹 Italian passes 20 % (#645, #646)

- **183 new keys** from [@alessandroquirino-lab](https://github.com/alessandroquirino-lab):
  the first half of all item names — tools, suit gear, weapons, ores, ingots and refined goods —
  and the last six block keys, completing `block.*` at 296/296. Coverage: 12.6 % → 20.6 %.
- The translation work keeps paying off in English too: the **beam block description** claimed to
  be "a glowing structural beam" — leftover concept text; it is the named teleporter pad, and now
  says so in English and German alike (caught by the translator, #646).
- The credits now thank the translator by name.

### 🛠️ Internal

- New `fluid_cell` persistence table in all three world repositories (SQLite, PostgreSQL,
  in-memory), restored on world load without forcing chunk generation.
- Block changes raise a typed `BlockChangeApplied` event, letting fluid audio and future systems
  distinguish simulation steps from player edits.
- A tamed companion silently lost its species' gait on cloning — fixed, and a reflection parity
  test now fails if a future species field misses the clone.
- Creature wire messages gain additive body-plan fields; the codec tag is unchanged, older
  clients simply render the standard body. No save migration anywhere in this release.
- Test suite at 1332 server + 154 client tests, all green.

## [2026.7.24] — 2026-07-30

The long-view release: on foot you can finally look into the distance. Binoculars zoom up to 6×, and
their thermal upgrade paints every energy signature — creatures, bandits, NPCs, lava, whole settlements —
straight through terrain and haze, each with its name and range. Closer to home, villages and cities now
keep a glass greenhouse of ripe berries you may walk into and harvest, and stations grow the same crop
in a hydroponics bay. The in-flight system chart stops reading as a random scatter of discs — it is
centred on its star now and draws the orbit each planet actually rides — and a click on it finally lands
where you made it. Rounding it off, two community contributions: the game speaks a third language,
Italian, translated from scratch by a player, and the portal website works with a keyboard and a screen
reader after a stranger took the time to write down what was broken.

### 🔭 Binoculars, and a thermal upgrade that sees through walls (#629, #630)

- **Binoculars** — a workshop tool (2 iron plates, 2 glass, 1 cable; blueprint 1 data fragment, 4 iron
  plates, 2 glass). Hold them and **right-click to raise the optic**, right-click again to step the
  magnification (**2× · 3.3× · 6×**), once more to lower it. A scope surround, a centre reticle and the
  magnification are drawn over the view, look sensitivity and head-bob are damped along with the zoom
  (at 6× an undamped mouse is unusable), and the optic drops itself when you switch item, open a menu,
  board a speeder or fly.
- **Thermal binoculars** — the upgrade: the workshop recipe *consumes* a plain pair (plus titanium,
  a circuit board and crystal), so you carry one optic, not two. Press **I** while looking through them
  and the world grades cold while **every energy signature lights up through terrain**: hostiles hot
  red-orange, wildlife amber (dimmer asleep, icy in stasis), your tamed companions green, NPCs
  cyan-white, other players white, lava deep orange, and settlements, factories, ruins, bases and your
  ship as magenta bearing columns. Each contact carries its **name and range** ("Bandit camp · 143 m").
- **This does not extend your view distance** — terrain only exists as far as the world is streamed
  around you, and that is where the haze ends. What the thermal mode buys you is *contacts*: they read
  straight through the fog, so you can tell what is out there before you walk into it. Anything past the
  streamed edge is drawn at that edge along its true bearing, with the real distance in its tag.
- Stealthed players stay invisible — the scope cannot defeat a stealth field. The reduced-effects setting
  drops the full-screen grade and keeps the contacts. The thermal key is rebindable in Settings → Controls.

### 🌱 Greenhouses in settlements and stations (#626, #627, #628)

- **Every village and city now keeps a greenhouse you can walk into and harvest.** A village grows berries
  in soil beds under a timber frame with a pitched glass gable, lit by a torch behind a hinged door; a town
  or city runs a **two-tier hydroponics bay** — iron frame, full glass shell, trays on the floor and on a
  rack above, ceiling grow lights that make it glow at night, and a sliding door. A city keeps 2–3, a town
  1–2, a village one. Beds run along the aisle, so walking in doesn't put you in the crops. Ruined
  settlements get the shattered shell.
- **Greenhouse berries are always safe to eat.** Wild plants roll toxic on about a third of all worlds —
  a village greenhouse would have grown poison. The cultivated crop never joins a world's wild flora, never
  turns toxic and keeps its ripe red on every planet. Wild flora keeps rolling toxic, as before.
- **You may pick a plant, but never dismantle a house.** Settlement and station blocks are protected from
  mining, which also made the berries unpickable — plants are now exempt, and a picked crop **regrows on
  its bed**, so a greenhouse is a renewable food source rather than a one-time raid.
- **Stations grow food too.** Space stations carry the same hydroponics bay, and plants aboard them now
  actually regrow (worlds without a planet surface skipped the whole growth cycle before).
- **Grow your own:** 3 berries make 2 **berry seeds** by hand, and a **hydro tray** (workshop: metal panel,
  glass, plant fibre) lets you farm without soil — plant a bed at your own base.
- **New worlds only** — settlements and stations are built when a world is created, so existing saves keep
  their current villages.

### 🧭 The flight chart reads as a star-system chart (#623)

- **The system chart (M in flight) is centred on its star now** and every planet — and every landable
  asteroid — rides a drawn **orbit ring in its own colour**. Until now the star was a dot shoved against
  the rim whenever the framing lost it, and the bodies read as a scatter with nothing to say they orbit
  anything at all. A planet with rings (#596) wears them here too, as an ellipse.
- **A click now lands where you made it.** Setting a nav waypoint on the chart put it somewhere else
  entirely, and clicking a body to target it practically never took — the chart read clicks half a chart
  away from where the markers live. Both directions of the projection now come from one place, so they
  cannot drift apart again. *(Present since the chart arrived in 2026.7.23.)*
- The chart also stopped zooming out for no reason (the oversized body you launched from was padding the
  fit), and that body finally shows its real planet colour instead of a stand-in pale green.
- Moons deliberately get no ring — they sit on ladder slots just outside their parent, where a ring would
  collapse into its disc. The surface planet map is untouched.

### 🇮🇹 Italian, the third language (#99, #582)

- **The game speaks Italian now** — well, the block names do. All 290 block names and descriptions were
  translated from scratch by [@alessandroquirino-lab](https://github.com/alessandroquirino-lab), the first
  community language in the game, and more key groups are on the way.
- **Anything not yet translated stays English**, key by key, so a language can grow one group at a time
  instead of having to be finished before it works at all.
- Italian is **not offered in the settings menu yet**: it appears there once enough of the interface is
  covered. Until then it loads for anyone who selects it by hand in `client_settings.json`.

### ♿ Portal website accessibility (#574)

- **Every form field on the portal now has a visible label** — account creation, sign-in, password
  recovery, player name, new world, feedback and player reports. Placeholders alone vanished as soon as
  you started typing, and screen readers could not tell two "account name" fields apart.
- **Status and error messages are announced** — the message line at the top of the page is a live
  region now, and a blocked action moves the cursor to the field that needs fixing.
- **Enter submits again** — each flow is a real form, so pressing Enter in the password field signs you
  in instead of doing nothing.
- **"Forgot your password?" is a proper button** that reports whether it is open and jumps into the
  recovery fields, and keyboard users get a clearly visible focus ring across the whole portal.

Found and reported by [@SpaleRuby](https://github.com/SpaleRuby) — thank you!

### 🛠️ Internal

- A locale-coverage tool (`tools/locale_report.py`) reports per-language and per-key-group translation
  progress, and CI now checks contributed locale files for invented keys, lost `{0}` placeholders and
  blank values — so a translator gets told by the machine instead of in review.
- `SystemChartLayout` (Shared) holds the flight chart's projection and its inverse together, covered by
  round-trip tests over real generated star systems.
- Greenhouses ship with 20 new tests, two of them end to end — harvest and regrow a crop in a real stamped
  village, and the same aboard a boarded station.
- Both binocular tools are purely client-side: no protocol change, no server validation, no suit-energy
  cost. The two new shaders are registered as always-included so they survive player builds.

## [2026.7.23] — 2026-07-30

The milestones release: the game finally tells you what you have achieved and what to try next.
16 achievements track live progress with real item rewards, torches and hand-crafted wooden doors
make a first base possible without a workshop, shaped blocks take the angle you choose, and some
planets now carry Saturn-like rings you can see from their own surface. A flight system chart with
nav waypoints and six-colour planet marks turn space into something you can navigate, and stacks
holding 1024 mean a mining run no longer ends with a full pack. Rounding it off, this release
closes the whole batch of playtest reports about **losing things**: a full inventory can no longer
destroy what you craft or mine, you cannot fall through the world anymore, and a 2-block-high
opening is walkable again.

### 🏆 Achievements with rewards and live progress (#614)

- **16 achievements across mining, building, crafting, exploring and survival** — from "mine 5 iron"
  to visiting your first other planet. Each one shows **live progress as a tally with a filled bar**
  ("3/5"), so the list doubles as a guide to what you could do next.
- **Earning one hands you an item** — a medpack, an energy cell, ore, a better tool. If your pack is
  full the unlock is *deferred*, not consumed: you are told to make room and it is awarded on the next
  step of progress, so a reward can never be dropped into nowhere.
- Progress and earned achievements are saved per player. Existing saves start at zero and settle on
  join — which also means achievements added later are paid out on your next login.

### 🔥 Torches and cheap wooden doors (#616, #611)

- **Torches: 1 wood log + 2 plant fibre at the hand station makes four.** No workshop, no metal, no
  research — lighting a first base is no longer a grind. Aim at a wall to mount one; it is a slim prop
  with **no collider**, so you walk past torches instead of bumping into them, and the flame flickers
  (neighbouring torches deliberately flicker out of step).
- **A torch needs air.** On a body with no atmosphere placing one is refused with a reason you can act
  on instead of leaving a dud that mysteriously gives no light. A toxic atmosphere is fine — you need a
  suit, the flame does not.
- **A wooden door from 4 wood logs at the hand station.** Both existing doors were deep in the tech
  tree (metal panels, a bronze gear, a workshop; the sliding one a circuit board on top). The wooden
  door swings by hand with E, reads in warm light wood so it is clearly not the metal one, and hands
  itself back when you mine it.

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

### 🎨 Colour-marked planets in the star map (#613)

- **Mark planets in space, several at once, each in its own colour.** Until now the only marker in the
  game was the single planet-*surface* waypoint — one at a time, one colour, gone on travel. In the
  travel screen a body now takes a mark from a fixed **six-colour palette** (red, orange, yellow,
  green, blue, purple) — named and translated rather than a free colour picker.
- **One button cycles colour → next colour → off**, tinted in the current colour, so marking and
  recolouring need no extra controls. Marked bodies get a matching halo in the animated orrery (it
  tracks its planet along the orbit) and a coloured bullet with the colour's name in the body list.
- Marks are stored locally per world — your private notebook, never sent to the server.

### 🪜 Choose the placement angle for shaped blocks (#615)

- **The rotate key now walks the quarter turns first.** Stairs, slabs and every other shaped block used
  to derive their facing solely from the direction you were standing in, so getting the turn you wanted
  meant shuffling around — impossible when building into a corner or against a wall. Rotate now steps
  through the four quarter turns about the current up-face, then moves on to the next face, then back to
  Auto, and the HUD hint names both parts ("+Y · 180°").
- No world-format change and no save migration: the chunk and wire formats have always carried all 24
  orientations — only the control over the yaw was missing.

### ⏸️ The Esc menu really pauses a singleplayer world (#612)

- **Nothing stopped before.** The dialog was already titled "Pause" with a "Resume" button, but hunger
  drained, creatures kept hunting, night kept falling and the clock kept running while you read the
  menu. Opening the menu in singleplayer now genuinely holds the simulation.
- The hold is a server-side intent rather than a client freeze, because singleplayer runs the bundled
  server as a separate process — stopping only the client would have frozen the camera while the world
  carried on. Entering the hold also **saves**, which doubles as a safe point if the client never
  comes back. Multiplayer is unaffected.

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

### 🛠️ Admin teleport by landmark, not by coordinates

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

### Fixed

- **A full inventory no longer destroys what you make or mine (#607).** Crafting with all slots
  occupied and no cargo hold in reach consumed the inputs, produced nothing and still showed a success
  message. Craft, dye, re-shape, disassemble, mission turn-in and picking a placed door back up now
  check for room *before* consuming anything and refuse with a clear message; mining leaves a block
  standing (loot included) rather than clearing the cell and dropping its drops into nowhere, and area
  mining skips such blocks instead of destroying the loot. Loot from something already destroyed warns
  you instead of vanishing silently.
- **You cannot fall through the world anymore (#608).** Placing a block into your own feet cell — which
  the game allows on purpose so pillar jumping works — could, in a fast fall, wedge the collider inside
  the new block and push it down through every block below, forever. You are now lifted onto the cell's
  top explicitly, which also covers another player filling the cell. On top of that, the last safe spot
  you stood on is remembered: being stuck inside solid geometry or dropping below the build band puts
  you back there (ladders and swimming excluded).
- **A 2-block-high opening can be walked through again (#609).** The 1.8 m capsule leaves ~0.14 m of
  headroom under a 2-block ceiling, and the auto-step sweep ate far more than that, so players got
  wedged in doorways they had built themselves. Step height is now capped to the headroom actually
  available — slabs and stair treads are still climbed in the open.
- **The craft toast is translated and names the item (#610).** It read "Crafted glass" in an otherwise
  German UI, showing the raw recipe key; craft failures no longer push raw English server text at the
  player either.
- **The admin report list shows one row per report (#617).** Every in-game F1 report reaches the inbox
  twice by design (client direct + the game server's richer snapshot), and the list counted both — a
  batch of 8 player reports appeared as 16 rows. Matching halves are now paired into one row: the
  player's own wording as the label, linked to the record that carries the screenshot and snapshot,
  with a "+1" chip to reach the other. Ingest and the read API are unchanged — a player report is
  never dropped.

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

[Unreleased]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.9.2...HEAD
[2026.9.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.9.1...v2026.9.2
[2026.9.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.26...v2026.9.1
[2026.8.26]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.25...v2026.8.26
[2026.8.25]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.24...v2026.8.25
[2026.8.24]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.23...v2026.8.24
[2026.8.23]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.22...v2026.8.23
[2026.8.22]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.21...v2026.8.22
[2026.8.21]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.20...v2026.8.21
[2026.8.20]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.19...v2026.8.20
[2026.8.19]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.18...v2026.8.19
[2026.8.18]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.17...v2026.8.18
[2026.8.17]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.16...v2026.8.17
[2026.8.16]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.15...v2026.8.16
[2026.8.15]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.14...v2026.8.15
[2026.8.14]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.13...v2026.8.14
[2026.8.13]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.12...v2026.8.13
[2026.8.12]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.11...v2026.8.12
[2026.8.11]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.10...v2026.8.11
[2026.8.10]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.9...v2026.8.10
[2026.8.9]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.8...v2026.8.9
[2026.8.8]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.7...v2026.8.8
[2026.8.7]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.6...v2026.8.7
[2026.8.6]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.5...v2026.8.6
[2026.8.5]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.4...v2026.8.5
[2026.8.4]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.3...v2026.8.4
[2026.8.3]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.2...v2026.8.3
[2026.8.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.8.1...v2026.8.2
[2026.8.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.7.24...v2026.8.1
[2026.7.24]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.7.23...v2026.7.24
[2026.7.23]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v2026.7.22...v2026.7.23
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
