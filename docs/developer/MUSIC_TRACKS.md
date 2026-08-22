# Background music tracks (Suno + ElevenLabs library)

The granular background-music library used by the **Tracks** music mode (see
[SOUND_DESIGN.md](SOUND_DESIGN.md) §11). 23 instrumental, calm, loop-friendly sci-fi tracks generated with
[Suno](https://suno.com/) by the project owner, plus a **variance pack** of 12 `_2` B-sides (wired
in `MusicLibrary` and **all present** — see *Variance pack* below), the 5 dramatic **finale/boss**
tracks (also present + wired — see the last section) and **11 tracks composed with the ElevenLabs Music
API** (nine `_3` third variants + `music_planet_night_2` / `music_planet_sunrise_2`, #1175 — see *ElevenLabs
additions*): **51 tracks** in total. All live as raw MP3s in **`client/Music/*.mp3`**
(tracked, deliberately *outside* `client/Assets/`); `scripts/sync-client-libs.{ps1,sh}` copies them to
`client/Assets/StreamingAssets/music/` (git-ignored) on every build, and `ClientMusic` **streams each
track on demand** with `UnityWebRequestMultimedia` — over HTTP in the browser, from disk on desktop —
keeping only the playing/fading/prefetched clips in memory and releasing the rest (#1167). They used to be
`Resources/music` assets, which baked all 40 songs (164 MB) into the WebGL player data file that every
browser visitor had to download before the first frame. The player picks **Synth** (the original
code-synth ambient pads) or **Tracks** in *Settings → Audio → Music style*; SFX/ambience are untouched and
ride their own `SfxVolume` bus.

The director ([`ClientMusic`](../../client/Assets/BlocksBeyondTheStars/Scripts/ClientMusic.cs)) maps the
shell phase and — in-game — the world state to a **context**, then cross-fades (~2.5 s). The pools
themselves live in the Unity-free
[`MusicLibrary`](../../src/BlocksBeyondTheStars.Client.Core/Music/MusicLibrary.cs) (Client.Core, so a test
guards that every referenced file ships), the next track is chosen by
[`MusicPicker`](../../src/BlocksBeyondTheStars.Client.Core/Music/MusicPicker.cs), and rests between tracks
by [`MusicRestPolicy`](../../src/BlocksBeyondTheStars.Client.Core/Music/MusicRestPolicy.cs) — see *How the
director varies the music* below. **Combat** intentionally stays on the tense synth mood — the whole
library is calm by design. (The separate **finale / boss** set — see the last section — is the deliberate
dramatic exception, reserved for the story finale.)

## Context → track mapping

Each context has its **own tracks** (the majority of what plays there) and — on planets — a set of
**neutral fillers** that are blended in at a minority share (`MusicLibrary.FillerShare`, 0.35 for the
surface biomes, 0.3 generic, 0.25 cave, 0 elsewhere), so a two-track biome no longer alternates A-B-A-B
while the biome keeps its identity (every planet of a biome uses the same pool — no per-planet
randomisation, by decision). The time of day only changes the **filler** set: by day the all-round beds
`music_idle_default`(`_2`) + `music_explore_planet`(`_2`); at **dawn** (local time 0.23–0.30)
`music_planet_sunrise`(`_2`) + the explore pair; at **night** (< 0.23 / ≥ 0.78) `music_planet_night`(`_2`) + the
idle pair; underground only the idle pair (no sky).

| Context | Detection | Own tracks | Fillers |
|---|---|---|---|
| Main menu | shell `MainMenu`/`Settings`/`Credits`/editors | `music_main_menu`, `music_main_menu_2`, `music_main_menu_3` | — |
| Loading screen | shell `Loading` | `music_loading`, `music_loading_2`, `music_loading_3` | — |
| Splash | shell `Splash`/`Studio` | *(silent — splash stings play instead)* | — |
| Ship interior | in-game, `Aboard`, not flying | `music_ship_interior`, `music_crafting_workshop`, `music_research_blueprints` | — |
| Station / hub | in-game, `NearVendor` or `orbital_station` | `music_multiplayer_hub`, `music_multiplayer_hub_2`, `music_multiplayer_hub_3` | — |
| Space flight | in-game, `InSpace`/`SpaceViewActive` | `music_space_orbit`, `music_deep_space_lonely`, `music_mystery_signal`, `music_asteroid_mining`, `music_cockpit_starmap` | — |
| Star chart | in space, the flight system chart is open (`GameBootstrap.StarChartOpen`) | `music_cockpit_starmap` (loops) | — |
| Space combat | hull+shield dropped in space (14 s) | *(synth combat mood — no Suno track)* | — |
| Workshop | Tab menu **Crafting** tab open ≥ 30 s (on foot / aboard) | `music_crafting_workshop` (loops) | — |
| Research | Tab menu **Tech** tab open ≥ 30 s | `music_research_blueprints` (loops) | — |
| Planet — ice | biome `ice`/`tundra`/`glacier` | `music_planet_ice`, `music_planet_ice_2`, `music_planet_ice_3` | neutral set by time of day |
| Planet — desert | biome `desert`/`salt_flats` | `music_planet_desert`, `music_planet_desert_2`, `music_planet_desert_3` | neutral set |
| Planet — lava | biome `lava`/`ashen`/`volcanic` | `music_planet_lava`, `music_planet_lava_2`, `music_planet_lava_3` | neutral set |
| Planet — toxic | biome `fungal`/`corrupted` | `music_planet_toxic`, `music_planet_toxic_2`, `music_planet_toxic_3` | neutral set |
| Planet — ocean | biome `ocean` | `music_planet_ocean`, `music_planet_ocean_2`, `music_planet_ocean_3` | neutral set |
| Planet — verdant | biome `jungle`/`forest`/`savanna`/`swamp` | `music_planet_verdant`, `music_planet_verdant_2` | neutral set |
| Planet — crystal | biome contains `crystal` | `music_moon_crystal`, `music_explore_planet`, `music_explore_planet_2` | neutral set |
| Planet — cave | on a planet, not sky-exposed | `music_planet_cave`, `music_planet_cave_2`, `music_planet_cave_3` | `music_idle_default`(`_2`) |
| Planet — deep water | head submerged ≥ 8 s (back to the surface pool 5 s after surfacing) | `music_planet_ocean_2` (loops) | — |
| Planet — generic | any other surface (rocky / varied / highland / skylands / asteroid) | `music_explore_planet`(`_2`), `music_idle_default`(`_2`) | `music_planet_sunrise`(`_2`) at dawn, `music_planet_night`(`_2`) at night |
| First landing | first time a planet is walked in this session | `music_planet_sunrise` once, then the pool | — |

The `_2` tracks are the **variance pack** (see *Variance pack* section below). All 12 are present in
`client/Music/` and live in the pools today; a track whose `.mp3` fails to load is dropped from its pool
for the session.

## How the director varies the music (#1172–#1174)

- **Shuffle bag** (`MusicPicker`): every track of a pool plays once, in random order, before anything
  repeats; the track that just ended is never picked again immediately; the neutral fillers rotate in one
  **shared bag** across all contexts, and a short history (last 4 picks) keeps a neutral that just played
  on the surface from popping up right after entering a cave. The successor is **chosen 45 s before the
  current track ends** and prefetched, so the re-roll lands on exactly that track (one fetch per change).
- **Rests** (`MusicRestPolicy`): after a track ends on a planet (55 %), in a cave / under water (45 %), in
  space (50 %) or aboard the parked ship (30 %) the music takes a breath of 60–180 s (45–120 s aboard) —
  only the ambience beds play — then the next track fades in. Menu, loading, station, the UI beds and
  the finale never rest. A context change ends a rest at once.
- **Ducking**: a violent weather episode (storm / blizzard / sandstorm, by `WeatherFamily` × intensity)
  pulls the music down to ~55 %, rain / fog / gale to ~80 %; a **hostile creature within 20 m** (on foot)
  ducks to 60 % and darkens the music (low-pass 1.4 kHz) — a tension treatment, not a track switch. Under
  water the existing 680 Hz muffle applies.
- **Synth style** (`SynthComposer`, #1176): the Synth music style is no longer four fixed 10–24 s loops but
  a seeded generative ambient engine — per piece a mode, tempo, two chord phrases (8 chords, 40–110 s), two
  arpeggio patterns, pad timbre and drone are composed in code and rendered over a few frames; planet
  pieces take root + mode from the biome (every ice planet is D dorian, every lava planet E aeolian, …), so
  biomes stay recognisable while no two pieces repeat. Combat keeps its 2 Hz throb on a steady root pulse.
  It is also the fallback whenever a Tracks-mode file is missing.

If a track file is ever missing (or, in the browser, unreachable), its context falls back to the matching
synth mood, so the game always stays musical. Because tracks are fetched on first use, a context's track
starts a moment after the context is entered (instant from disk; a few seconds over a slow connection) —
the director keeps the previous track playing until the new one has arrived, and prefetches the next
re-roll candidate 45 s before the current track ends so the seam needs no wait.

Browser gotcha (#1169): in WebGL `DownloadHandlerAudioClip.audioClip` is handed back **before** the MP3 is
decoded — the clip reports `length == 0` and its `loadState` stays `Unloaded` (it flips to `Loaded` only
later, so it is no "still loading" signal for web-request clips). `LoadTrack` therefore waits until
`clip.length > 0` (60 s cap; desktop/Editor clips are complete at once, so nothing waits there) before
caching and fading a clip in, and the re-roll/prefetch check ignores length-0 clips — otherwise the
director read "length 0" as "track over" and downloaded a second track right away. A clip that never
decodes is dropped from its pool like a missing file.

Adding a track: drop the `.mp3` into `client/Music/`, add it to the matching pool in
`MusicLibrary` (`src/BlocksBeyondTheStars.Client.Core/Music/MusicLibrary.cs` — `MusicLibraryTests` fails
if a pool names a file that does not ship), document prompt + context here, and add it to `NOTICES.md` if
the source changes. Never put it under
`client/Assets/` (Unity would import and bake it) or `StreamingAssets/data/` (the browser prefetches that
folder's manifest eagerly).

## Tracks & Suno prompts

General Suno guidance for every track: *instrumental, no vocals, no lyrics, seamless loop, calm,
atmospheric, sci-fi, fits exploration / crafting / travel / ship management, not dramatic, no combat,
no trailer music.* Lyrics box: `[Instrumental only] [No vocals] [No lyrics] [Seamless loop]`.

### `music_explore_planet` (was 01) — general planet exploration
Friendly, wide, curious, non-threatening: walking planets, gathering resources, first discoveries.
```text
Instrumental ambient sci-fi game soundtrack for a block-based space exploration and crafting game. Calm exploration mood, soft synthesizer pads, gentle arpeggios, light electronic percussion, warm bass drones, subtle sparkling textures, sense of wonder and discovery. Designed as background music for walking across alien planets and collecting resources. Seamless loop, no vocals, no lyrics, not dramatic, not combat music.
```

### `music_ship_interior` (was 02) — calm spaceship interior
Inside your own ship (workshop, cockpit, cargo, medbay, quarters): safe, warm, technical hum.
```text
Instrumental sci-fi ambient music for the interior of a small modular spaceship. Soft humming drones, gentle analog synths, quiet electronic pulses, warm cabin atmosphere, subtle machine-like rhythm, calm and safe feeling. Suitable for crafting, inventory management, ship upgrades and planning the next journey. Seamless loop, no vocals, no lyrics, calm background game music, no action, no combat.
```

### `music_cockpit_starmap` (was 03) — cockpit & star map
Cockpit, star map, planet scan, target selection, route planning: holographic, expectant, no pressure.
```text
Instrumental space navigation soundtrack for a sci-fi crafting game. Calm futuristic synth pads, soft pulsing sequencer, distant starfield ambience, subtle holographic UI feeling, slow evolving chords, gentle sense of anticipation. Music for opening the star map, scanning planets and choosing a destination. Seamless loop, no vocals, no lyrics, not epic trailer music, not combat music.
```

### `music_space_orbit` (was 04) — orbit & peaceful spaceflight
Calm orbital flight, slow travel between planets, free space movement without combat.
```text
Instrumental ambient spaceflight music, peaceful orbital travel, deep space atmosphere, soft synth pads, slow bass movement, light shimmering melodies, distant cosmic textures, gentle engine-like pulse. Background music for flying a small blocky starship between planets. Seamless loop, no vocals, no lyrics, calm exploration, no battle, no dramatic drums.
```

### `music_planet_ice` (was 05) — ice planet
Cold worlds, ice fields, snow, blue crystals, thin atmospheres: lonely, beautiful, cold, mysterious.
```text
Instrumental ambient sci-fi soundtrack for exploring a frozen alien planet. Cold blue atmosphere, glassy synth pads, delicate bell-like tones, soft wind textures, minimal percussion, slow emotional chords, lonely but beautiful mood. Suitable for calm exploration, mining ice and discovering crystals. Seamless loop, no vocals, no lyrics, no combat, no heavy drums.
```

### `music_planet_desert` (was 06) — desert planet
Sand planets, dust, rock deserts, warm dry worlds: vastness, heat, solitude, calm survival.
```text
Instrumental ambient sci-fi desert planet music. Warm synth pads, soft low drones, subtle hand percussion mixed with electronic pulses, dusty wind ambience, slow mysterious melody, feeling of heat, distance and survival. Background music for exploring a blocky alien desert world. Seamless loop, no vocals, no lyrics, calm exploration, not combat music.
```

### `music_planet_lava` (was 07) — lava planet
Lava worlds, volcanoes, hot caves, basalt, dangerous zones: may feel threatening, still not combat.
```text
Instrumental dark ambient sci-fi music for exploring a volcanic lava planet. Deep warm drones, slow pulsing synth bass, glowing ember-like textures, distant rumbling, subtle low percussion, tense but not action-heavy. Mood should feel dangerous, hot and mysterious, but still suitable as background exploration music. Seamless loop, no vocals, no lyrics, no combat drums, no cinematic climax.
```

### `music_planet_toxic` (was 08) — toxic planet
Poison atmospheres, green fog, alien plants, spores, eerie conditions: uneasy and alien, not horror.
```text
Instrumental eerie ambient sci-fi soundtrack for a toxic alien planet. Strange organic synth textures, soft green atmospheric feeling, slow pulsing drones, subtle bubbling effects, mysterious pads, minimal rhythm, uneasy but calm exploration mood. Suitable for walking through poisonous fog and scanning alien plants. Seamless loop, no vocals, no lyrics, not horror, not combat music.
```

### `music_planet_ocean` (was 09) — ocean planet
Water worlds, islands, coasts, underwater, calm blue planets: soft, flowing, deep, peaceful.
```text
Instrumental ambient sci-fi music for an ocean planet. Flowing synth pads, soft aquatic textures, gentle echoing bells, slow warm bass, subtle wave-like rhythm, calm sense of depth and discovery. Background music for exploring islands, underwater areas and alien sea biomes. Seamless loop, no vocals, no lyrics, peaceful exploration, no combat.
```

### `music_moon_crystal` (was 10) — crystal moon
Moons with crystals, rare resources, thin atmosphere, mysterious signals: sparkling, still, magical-scientific.
```text
Instrumental atmospheric sci-fi soundtrack for a quiet crystal moon. Sparkling synth arpeggios, glassy pads, deep space ambience, soft resonant tones, minimal percussion, magical but scientific feeling. Music for discovering rare crystals and strange signals on a silent blocky moon. Seamless loop, no vocals, no lyrics, calm exploration, no combat.
```

### `music_asteroid_mining` (was 11) — asteroid field / mining
Calm mining in asteroid fields, collecting ore in space, focused resource work: focused, technical, calm.
```text
Instrumental sci-fi mining ambience for a calm asteroid field. Deep space drones, soft mechanical pulses, subtle metallic percussion, slow synth bass, distant radio-like textures, focused and steady mood. Background music for mining asteroids, collecting ore and managing ship resources. Seamless loop, no vocals, no lyrics, not tense, not combat music.
```

### `music_crafting_workshop` (was 12) — crafting & workshop
Workshop, item crafting, resource processing, repairs, calm building in the ship: productive, cozy, technical.
```text
Instrumental cozy sci-fi workshop music for a space crafting game. Soft electronic rhythm, warm synth chords, small mechanical clicks, gentle bass pulse, calm productive mood, feeling of building and upgrading equipment inside a spaceship. Suitable for crafting items, processing resources and repairing tools. Seamless loop, no vocals, no lyrics, no action, no combat.
```

### `music_research_blueprints` (was 13) — research & blueprints
Lab, tech tree, data fragments, research, unlocking blueprints: intelligent, calm, futuristic, inventive.
```text
Instrumental futuristic research lab soundtrack. Calm intelligent sci-fi mood, soft holographic synths, light arpeggios, subtle data-like pulses, gentle evolving pads, sense of discovery and invention. Background music for unlocking blueprints, analyzing alien data and researching new ship modules. Seamless loop, no vocals, no lyrics, no dramatic climax, no combat.
```

### `music_main_menu` (was 14) — main menu
Main menu, start screen, server select, first atmosphere: more memorable than usual, still calm.
```text
Instrumental main menu theme for a block-based sci-fi space crafting game. Calm but memorable, hopeful exploration mood, wide synth pads, gentle melody, soft electronic pulse, subtle orchestral warmth, feeling of stars, building, family-friendly adventure and endless worlds. Loopable game menu music, no vocals, no lyrics, not too epic, not combat music.
```

### `music_loading` (was 15) — loading screen
Loading screens, world generation, travel transitions, waiting: anticipation but calm.
```text
Instrumental loading screen music for a sci-fi voxel space exploration game. Soft ambient synth pads, gentle rhythmic pulse, subtle starfield sparkle, calm anticipation, sense of preparing a journey. Should feel futuristic, clean and relaxing while worlds are generated. Seamless loop, no vocals, no lyrics, no action, no heavy drums.
```

### `music_multiplayer_hub` (was 16) — peaceful multiplayer hub
Peaceful co-op, meeting players, stations, mission computer, trade, preparing expeditions together.
```text
Instrumental peaceful multiplayer hub music for a sci-fi crafting game. Warm friendly synths, soft bass, light electronic percussion, calm optimistic melody, cooperative and safe atmosphere. Suitable for players meeting, trading, building, managing missions and preparing expeditions. Seamless loop, no vocals, no lyrics, no combat, not dramatic.
```

### `music_deep_space_lonely` (was 17) — lonely deep space
Very calm, empty, melancholic moments in space: long journeys, distant systems, lonely planets, quiet exploration.
```text
Instrumental deep space ambient soundtrack. Very calm, slow evolving synth pads, distant cosmic drones, minimal melody, lonely but beautiful mood, feeling of floating between stars. Background music for quiet exploration, empty space, distant planets and long journeys. Seamless loop, no vocals, no lyrics, no drums, no combat, no cinematic climax.
```

### `music_mystery_signal` (was 18) — mysterious signal
Unknown signals, abandoned places, wrecks, ruins, strange finds: curious and mysterious, not horror/combat.
```text
Instrumental mysterious sci-fi exploration music. Soft dark synth pads, distant pulses, subtle glitch textures, quiet suspense, slow minimal melody, sense of discovering an unknown signal or abandoned structure. Calm but curious, not scary, not action-heavy. Seamless loop, no vocals, no lyrics, no combat drums, no jump scares.
```

### `music_planet_sunrise` (was 19) — sunrise / beautiful planet moment
Especially beautiful calm moments: sunrise, first sight of a landscape, peaceful discoveries.
```text
Instrumental uplifting ambient sci-fi music for sunrise on an alien planet. Warm evolving synth pads, gentle sparkling tones, soft slow melody, peaceful wonder, feeling of a new day on a strange blocky world. Background exploration music, emotional but subtle. Seamless loop, no vocals, no lyrics, no combat, no big cinematic climax.
```

### `music_idle_default` (was 20) — standard idle loop
The most important all-round bed: normal calm phases (explore, gather, build, ship, short travel). Long-listenable.
```text
Instrumental seamless ambient loop for a block-based sci-fi space crafting game. Calm background music for idle exploration, resource gathering, ship management and peaceful travel. Soft synth pads, gentle arpeggios, light electronic pulse, warm low bass, subtle cosmic atmosphere, no strong melody, no vocals, no lyrics, no combat, no dramatic changes. Designed to play for a long time without becoming annoying.
```

### `music_planet_cave` (was a) — underground / cave *(gap track)*
Subterranean exploration: caves, tunnels, mines, underground bases. Deep, calm, faintly echoing, mysterious-but-safe.
```text
Instrumental ambient sci-fi underground cave music. Deep subterranean atmosphere, low resonant drones, soft echoing drips, distant cavern reverb, sparse glassy tones, slow evolving pads, calm and mysterious, feeling of exploring tunnels and mines beneath an alien planet. Seamless loop, no vocals, no lyrics, not horror, not combat, no heavy drums.
```

### `music_planet_verdant` (was b) — lush green / jungle world *(gap track)*
Living green worlds: jungles, forests, swamps full of life. Warm, organic, curious, alive — distinct from the cold/toxic mood.
```text
Instrumental ambient sci-fi music for a lush green alien jungle planet. Warm organic synth pads, gentle wooden mallet tones, soft flute-like textures, subtle living ambience, light bouncing arpeggios, curious and alive but calm mood, feeling of dense alien forests and growth. Seamless loop, no vocals, no lyrics, peaceful exploration, no combat, not dramatic.
```

### `music_planet_night` (was c) — planet at night *(gap track)*
Calm planetary night, the counterpart to the sunrise track. Quiet, starlit, gently melancholic, peaceful.
```text
Instrumental calm sci-fi night ambience for an alien planet after dark. Soft starlit synth pads, gentle low bass, sparse twinkling tones, quiet nocturnal mood, peaceful and slightly melancholic, feeling of a clear alien night sky. Seamless loop, no vocals, no lyrics, no combat, no dramatic climax.
```

## Variance pack (B-side tracks)

These add a **second** (and a couple of third) track to the contexts that previously had only one, so a
long stay no longer loops the same song. They are **deliberately a different musical angle** from their
sibling (noted per track) — not a re-roll of the same idea — so the pair genuinely alternates. Same
general guidance as above: *instrumental, no vocals, seamless loop, calm, atmospheric sci-fi, no combat,
no trailer drama.* Lyrics box: `[Instrumental only] [No vocals] [No lyrics] [Seamless loop]`. **All 12
B-sides are present** in `client/Music/` and live in the pools (the prompts below are
kept for reference / re-generation).

### `music_planet_ice_2` — ice planet (B-side)
*Different angle vs `music_planet_ice`: warmer and more hopeful, gentle aurora shimmer and slow movement instead of the lonely glassy stillness.*
```text
Instrumental ambient sci-fi soundtrack for a frozen alien planet, second variant. Cold but hopeful mood, slowly shifting aurora-like synth pads, warm glassy bells, soft breathing wind, a gentle rising melody, light shimmering high textures, quiet sense of beauty under the ice. Calm exploration of snowfields and frozen crystals. Seamless loop, no vocals, no lyrics, no combat, no heavy drums.
```

### `music_planet_desert_2` — desert planet (B-side)
*Different angle vs `music_planet_desert`: cool desert night and mirage shimmer, more melodic and flowing instead of the dry midday heat.*
```text
Instrumental ambient sci-fi music for an alien desert planet at night, second variant. Cool dusk atmosphere, soft mirage-like synth shimmer, slow flowing melody, warm low drones, distant wind, sparse glassy tones, a feeling of vast dunes under strange stars. Calm exploration, mysterious and beautiful. Seamless loop, no vocals, no lyrics, calm, not combat music.
```

### `music_planet_lava_2` — lava planet (B-side)
*Different angle vs `music_planet_lava`: awe and molten grandeur instead of pure dread — slow, glowing, almost majestic.*
```text
Instrumental atmospheric sci-fi music for a volcanic lava planet, second variant. Slow majestic mood of glowing molten landscapes, deep warm drones, slowly swelling synth pads, soft glowing ember textures, a quiet sense of awe and raw power, sparse low melody. Dangerous but beautiful, still background exploration music. Seamless loop, no vocals, no lyrics, no combat drums, no cinematic climax.
```

### `music_planet_toxic_2` — toxic planet (B-side)
*Different angle vs `music_planet_toxic`: dreamy psychedelic alien beauty and floating wonder instead of queasy unease.*
```text
Instrumental dreamy ambient sci-fi music for a toxic alien planet, second variant. Floating psychedelic synth textures, soft glowing green-purple pads, slow drifting bell tones, gentle bubbling spore-like sounds, hypnotic and curious mood, strange alien beauty. Calm exploration through luminous poisonous flora. Seamless loop, no vocals, no lyrics, not horror, not combat music.
```

### `music_planet_ocean_2` — ocean planet (B-side)
*Different angle vs `music_planet_ocean`: deep submerged vastness, darker and slower, whale-like instead of bright surface waves.*
```text
Instrumental deep underwater ambient music for an alien ocean planet, second variant. Vast submerged atmosphere, slow dark synth pads, soft whale-like low tones, distant sonar pulses, gentle filtered echoes, a feeling of drifting far below the surface. Calm, deep and mysterious exploration. Seamless loop, no vocals, no lyrics, peaceful, no combat.
```

### `music_planet_cave_2` — cave / underground (B-side)
*Different angle vs `music_planet_cave`: luminous crystal cavern with sparkle and wonder instead of the deep, heavy, drip-echo dark.*
```text
Instrumental ambient sci-fi music for a glowing underground crystal cavern, second variant. Luminous and wondrous mood, soft resonant crystal tones, sparkling synth arpeggios, gentle deep drones, distant cavern reverb, slow magical pads, a feeling of discovering a glowing cave beneath an alien world. Calm, mysterious and beautiful. Seamless loop, no vocals, no lyrics, not horror, not combat.
```

### `music_planet_verdant_2` — lush green / jungle (B-side)
*Different angle vs `music_planet_verdant`: a warm organic groove with light percussion and forward motion instead of the still, mallet-and-flute calm.*
```text
Instrumental ambient sci-fi music for a lush green alien jungle planet, second variant. Warm organic groove, soft wooden percussion and light hand drums, gentle bouncing arpeggios, flute-like synth lines, living forest textures, a curious and energetic but still relaxed mood, a sense of trekking through dense alien growth. Seamless loop, no vocals, no lyrics, peaceful exploration, no combat, not dramatic.
```

### `music_multiplayer_hub_2` — station / hub (B-side)
*Different angle vs `music_multiplayer_hub`: a brighter, social, lounge-like bustle instead of the calm cooperative bed.*
```text
Instrumental friendly sci-fi space station lounge music, second variant. Bright optimistic mood, warm electronic groove, soft light percussion, gentle melodic synth hook, mellow bass, a relaxed social atmosphere of a busy but peaceful hub where players trade and meet. Cozy and upbeat. Seamless loop, no vocals, no lyrics, no combat, not dramatic.
```

### `music_main_menu_2` — main menu (B-side)
*Different angle vs `music_main_menu`: more reflective and spacious, deep starlit awe instead of the bright hopeful theme — so the start screen alternates.*
```text
Instrumental main menu theme for a block-based sci-fi space game, second variant. Reflective and spacious mood, wide slow synth pads, distant starlit shimmer, a gentle emotional melody, soft warm bass, a feeling of standing before a vast galaxy of endless worlds. Calm, hopeful and a little wistful. Loopable game menu music, no vocals, no lyrics, not too epic, not combat music.
```

### `music_loading_2` — loading screen (B-side)
*Different angle vs `music_loading`: more forward momentum and gentle rhythmic pulse — a journey beginning rather than calm waiting.*
```text
Instrumental loading screen music for a sci-fi voxel space game, second variant. Gentle forward momentum, soft pulsing sequencer, light rhythmic synth, subtle starfield sparkle, a clean futuristic feeling of a journey about to begin. Relaxing but with quiet anticipation. Seamless loop, no vocals, no lyrics, no action, no heavy drums.
```

### `music_idle_default_2` — standard idle loop (B-side)
*Different angle vs `music_idle_default`: a warmer, more organic palette so the most-heard all-round bed alternates between two distinct neutrals.*
```text
Instrumental seamless ambient loop for a block-based sci-fi space crafting game, second all-round variant. Warm organic palette, soft analog synth pads, gentle plucked tones, light electronic pulse, mellow low bass, subtle cosmic warmth, no strong melody. Calm background music for idle exploration, gathering, building and short travel, designed to play for a long time without becoming annoying. Seamless loop, no vocals, no lyrics, no combat, no dramatic changes.
```

### `music_explore_planet_2` — general planet exploration (B-side)
*Different angle vs `music_explore_planet`: more adventurous and forward-moving, a light recurring melodic hook — a sense of setting out rather than gentle wonder.*
```text
Instrumental ambient sci-fi exploration music for a block-based space game, second variant. Adventurous forward-moving mood, light recurring melodic synth hook, soft driving arpeggios, gentle electronic percussion, warm bass, an optimistic sense of setting out across an alien world. Curious and uplifting but still calm background music. Seamless loop, no vocals, no lyrics, not dramatic, not combat music.
```

## ElevenLabs additions (#1175, 2026-08-22)

Eleven tracks composed with the **ElevenLabs Music API** via `tools/ai-assets/gen_music.py` (prompt mode,
`--length 165`, `mp3_44100_192`, instrumental forced) — three trial tracks were auditioned by the owner
before the batch. Nine are **third variants** for the two-track pools (a different angle again, noted per
track); two are B-sides of the neutral dawn / night fillers, which since #1172 play on every biome. Same
general guidance as the Suno set: *instrumental, no vocals, seamless loop, calm, atmospheric sci-fi,
not dramatic, no combat.* Regenerate with `uv run gen_music.py --prompt "<prompt>" --length 165 --format
mp3_44100_192 --out out/music/<name>.mp3`.

### `music_planet_ice_3` — ice planet at night *(very quiet, still, starlit)*
```text
Instrumental ambient sci-fi soundtrack for a frozen alien planet at night, third variant. Very quiet and still, deep cold drones, sparse crystalline bell tones, slow breathing pads, faint aurora shimmer, distant wind, a sense of vast starlit snowfields. Calm exploration music for a block-based space game, seamless loop, no vocals, no lyrics, no drums, no combat, no cinematic climax.
```

### `music_planet_desert_3` — desert at high noon *(heat haze, sparse, vast)*
```text
Instrumental ambient sci-fi music for an alien desert planet at high noon, third variant. Shimmering heat haze, very sparse and still, warm sustained synth pads, a slow distant melodic fragment, dry wind textures, faint sand-grain percussion far away, a vast empty horizon feeling. Calm exploration music for a block-based space game. Seamless loop, no vocals, no lyrics, no drums, not combat music, no cinematic climax.
```

### `music_planet_lava_3` — lava planet at night *(glowing rivers, crackling embers)*
```text
Instrumental dark ambient sci-fi music for a volcanic lava planet at night, third variant. Glowing rivers of lava under a black sky, deep slow drones, soft crackling ember textures, sparse low bell tones, slowly breathing pads, quiet tension but calm, no action. Background exploration music for a block-based space game. Seamless loop, no vocals, no lyrics, no combat drums, no cinematic climax.
```

### `music_planet_toxic_3` — spore-fog world *(luminous fog, organic pulses)*
```text
Instrumental ambient sci-fi music for a toxic spore-fog alien planet, third variant. Drifting luminous fog, slow pulsing organic drones, soft wet bubbling textures, gentle detuned glassy pads, a quiet mysterious melody appearing and fading, uneasy but calm and beautiful. Background exploration music for a block-based space game. Seamless loop, no vocals, no lyrics, not horror, not combat music.
```

### `music_planet_ocean_3` — ocean at night *(bioluminescent waves, dreamy)*
```text
Instrumental ambient sci-fi music for an alien ocean planet at night, third variant. Bioluminescent waves under starlight, slow flowing synth pads, soft echoing bell tones, gentle wave-like swells, warm low bass, calm and dreamy sense of depth and distance. Background exploration music for a block-based space game. Seamless loop, no vocals, no lyrics, peaceful, no combat.
```

### `music_planet_cave_3` — deep mine *(calm machinery hum, focused)*
```text
Instrumental ambient sci-fi music for a deep underground mine on an alien planet, third variant. Calm focused mood, low resonant drones, soft distant machinery hum, slow echoing pulses, sparse glassy tones, subtle dripping water textures, feeling of working far below the surface of a block-based world. Seamless loop, no vocals, no lyrics, not horror, not combat, no heavy drums.
```

### `music_multiplayer_hub_3` — station late shift *(electric piano, cozy)*
```text
Instrumental peaceful sci-fi space station music, third variant. Calm late-shift atmosphere, soft warm electric piano chords, gentle slow electronic pulse, mellow bass, distant docking-bay ambience, a relaxed sense of a quiet hub where travellers rest and trade. Cozy and hopeful background music for a block-based space game. Seamless loop, no vocals, no lyrics, no combat, not dramatic.
```

### `music_main_menu_3` — main menu *(warm, inviting, "home before the journey")*
```text
Instrumental main menu theme for a block-based sci-fi space exploration and crafting game, third variant. Warm and inviting, a gentle memorable synth melody over wide soft pads, slow electronic pulse, subtle starfield sparkle, a feeling of home before a long journey, hopeful and calm. Loopable game menu music, no vocals, no lyrics, not epic, not combat music.
```

### `music_loading_3` — loading screen *(patient, systems coming online)*
```text
Instrumental loading screen music for a sci-fi voxel space exploration game, third variant. Calm patient mood, slowly evolving synth pads, soft ticking sequencer, gentle rising tones like systems coming online, light cosmic shimmer, clean and futuristic. Seamless loop, no vocals, no lyrics, no action, no heavy drums.
```

### `music_planet_night_2` — planet at night, B-side *(lullaby fragment, nocturnal textures)*
```text
Instrumental calm sci-fi night ambience for an alien planet after dark, second variant. Very quiet, slow warm low pads, sparse distant chimes, soft nocturnal insect-like textures, a gentle lullaby-like melodic fragment, peaceful starlit stillness. Background exploration music for a block-based space game. Seamless loop, no vocals, no lyrics, no combat, no dramatic climax.
```

### `music_planet_sunrise_2` — dawn, B-side *(first light, sparkling arpeggios)*
```text
Instrumental uplifting ambient sci-fi music for dawn on an alien planet, second variant. Soft light growing, warm rising synth pads, gentle sparkling arpeggios, a hopeful slow melody, a feeling of first light over a strange blocky landscape, emotional but subtle. Background exploration music for a block-based space game. Seamless loop, no vocals, no lyrics, no combat, no big cinematic climax.
```

## Finale / boss music (story "The VEGA Protocol", plan P6)

**The deliberate exception to the calm-by-design library.** Five **dramatic** instrumental tracks for the
multi-stage finale against the dormant Guardian core (see
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) §P6). Generated in Suno by the project owner and
stored in `client/Music/`. **Present and wired:** `ClientMusic` has the finale contexts
(`FinaleApproach/Gauntlet/Hack/Dialogue/Resolution`) which **override every other context** and always play
their dedicated boss track regardless of music mode. The active phase is derived from the story flags, the
location id (the finale system is `guardian_finale*`) and the live `FinaleView`; the resolution track plays
in a one-shot window after the core falls, then normal music resumes.

| Track key | Finale phase | Mood |
|---|---|---|
| `music_boss_approach` | arrival at the Guardian system (only a sun + the core) | ominous, vast, foreboding calm |
| `music_boss_gauntlet` | Stage 1 — the drone gauntlet (hardest space combat) | epic, driving, intense |
| `music_boss_hack` | Stage 3 — hack the core (channel-and-defend) | tense, ticking, rising suspense |
| `music_boss_dialogue` | Stage 4 — the argument duel with the core | eerie, cerebral, escalating |
| `music_boss_resolution` | the core powers down / galaxy pacified | hopeful, cathartic, uplifting |

The Suno style prompts that produced these are in
[STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) Appendix A.
