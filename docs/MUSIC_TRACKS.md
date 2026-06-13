# Background music tracks (Suno library)

The granular background-music library used by the **Tracks** music mode (see
`docs/SOUND_DESIGN.md` §11). 23 instrumental, calm, loop-friendly sci-fi tracks generated with
[Suno](https://suno.com/) by the project owner. They live in
`client/Assets/Resources/music/*.mp3` (imported as **Streaming** audio so the multi-minute songs do
not sit decompressed in memory). The player picks **Synth** (the original code-synth ambient pads) or
**Tracks** in *Settings → Audio → Music style*; SFX/ambience are untouched and ride their own
`SfxVolume` bus.

The director ([`ClientMusic`](../client/Assets/BlocksBeyondTheStars/Scripts/ClientMusic.cs)) maps the
shell phase and — in-game — the world state to a **context**, then cross-fades (~2.5 s). When a
context has several fitting tracks the choice is **random**, and a long stay **re-rolls** to another
track at the loop seam so nothing repeats forever. **Combat** intentionally stays on the tense synth
mood — the whole library is calm by design.

## Context → track mapping

| Context | Detection | Track pool (random pick) |
|---|---|---|
| Main menu | shell `MainMenu`/`Settings`/`Credits`/editors | `music_main_menu` |
| Loading screen | shell `Loading` | `music_loading` |
| Splash | shell `Splash`/`Studio` | *(silent — splash stings play instead)* |
| Ship interior | in-game, `Aboard`, not flying | `music_ship_interior`, `music_crafting_workshop`, `music_research_blueprints` |
| Station / hub | in-game, `NearVendor` or `orbital_station` | `music_multiplayer_hub` |
| Space flight | in-game, `InSpace`/`SpaceViewActive` | `music_space_orbit`, `music_deep_space_lonely`, `music_mystery_signal`, `music_asteroid_mining`, `music_cockpit_starmap` |
| Space combat | hull+shield dropped in space (14 s) | *(synth combat mood — no Suno track)* |
| Planet — ice | biome `ice`/`tundra` | `music_planet_ice` |
| Planet — desert | biome `desert`/`salt_flats` | `music_planet_desert` |
| Planet — lava | biome `lava`/`ashen` | `music_planet_lava` |
| Planet — toxic | biome `fungal`/`corrupted` | `music_planet_toxic` |
| Planet — ocean | biome `ocean` | `music_planet_ocean` |
| Planet — verdant | biome `jungle`/`forest`/`savanna`/`swamp` | `music_planet_verdant`, `music_explore_planet` |
| Planet — crystal | biome contains `crystal` | `music_moon_crystal`, `music_explore_planet` |
| Planet — cave | on a planet, not sky-exposed | `music_planet_cave` |
| Planet — generic (day) | any other surface, daytime | `music_explore_planet`, `music_idle_default`, `music_planet_sunrise` |
| Planet — generic (night) | any other surface, nighttime | `music_explore_planet`, `music_idle_default`, `music_planet_night` |

If a track file is ever missing, its context falls back to the matching synth mood, so the game
always stays musical.

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
