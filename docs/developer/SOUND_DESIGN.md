# Sound Design — architecture + catalogue (M26)

A catalogue of every sound the game uses, where it triggers, how many distinct variants,
and **how it is sourced**. This was the M26 audio plan; the bulk of it has since shipped.

> Status today: **mostly implemented.** The recorded ElevenLabs SFX banks (P1–P4 below) are
> generated and bundled under `client/Assets/Resources/audio/` — mining ×4, place/loot/heal,
> drill/weapons/melee, footsteps, ship & space one-shots, **6×5 creature banks**, **2×5 NPC
> reaction banks + 6×3 NPC speech banks** (§9), and weather/biome beds — and are loaded by `ClientAudio` (filename → clip). Procedural
> cues (UI hover/click/confirm/back, scan ping, alarms, toggles, ambience-loop fallbacks) live in
> `ProceduralAudio`; **menu/shell UI sounds are wired** via `UiSound` (UiKit buttons play click on
> press, hover on enter). Both Synth and Suno **music** modes ship (`ClientMusic`). The remaining
> open items are noted inline; the server already broadcasts the events the sounds hook onto.

## Principles

- **Buses:** master → {SFX, music}. Volumes come from `ClientSettings` (`MasterVolume`,
  `SfxVolume`, `MusicVolume`); the master bus is the `AudioListener`.
- **Three sources**, chosen per category by what sounds best for the least cost/risk:
  1. **Procedural** (code-synth in `ClientAudio`/`ClientMusic`) — free, no assets. Best for
     UI/sci-fi tones, blips, hums, simple cues.
  2. **ElevenLabs** (recorded SFX via `tools/ai-assets/gen_sound.py`) — paid, cost-gated. Best
     for organic/impact/whoosh/creature/weather sounds a sine tone can't fake.
  3. **MIDI / synth music** (code) — free. Background music tracks.
- **Spatialisation:** world sounds (creatures, NPCs, weapons, doors, lava) are 3D AudioSources at
  the source position; UI/music/own-vitals are 2D.
- **NPCs never speak words** — a wordless vocoder babble per speech bubble plus a few non-verbal reactions, humans
  *and* androids, all through one voice budget (§9).
- **Variety from few assets:** pitch/rate-shift a small bank per instance (esp. creatures) instead
  of authoring one sound per species — the game has unbounded procedural species.
- Every bundled audio file is logged in `NOTICES.md` with source + licence.
- **Every clip is levelled to its class target** (`tools/audio/level_clips.py`, guarded by `AudioLevelManifestTests`, §27);
  the master slider is `AudioListener.volume` alone. Every 3D one-shot gets a distance profile, a voice budget and roof
  occlusion by its cue id (§28); vacuum is heard through the suit (§29).

## Cost gate (ElevenLabs)

ElevenLabs sound generation is now **blanket-approved** (no per-batch gate) — generated via
`tools/ai-assets/gen_sound.py`, one file per run, logged in `NOTICES.md`. Procedural/MIDI need no gate.

---

## 1. UI / menu — *procedural*

| Sound | Trigger | # |
|---|---|---|
| hover, click, confirm, back/cancel, error, tab-switch, slider-tick | shell + in-game menus | ~7 |

Source: procedural (short tonal cues). **Implemented** in `ProceduralAudio` (`ui_hover`, `ui_click`,
`ui_confirm`, `ui_back`) and played everywhere via the `UiSound` shell hook (UiKit buttons fire
click-on-press + hover-on-enter).

## 2. Player actions — *procedural + a few ElevenLabs*

| Sound | Trigger (event) | # | Source |
|---|---|---|---|
| mine hit (per material class: stone / metal / crystal / soft) | `BlockChanged`→air | 4 | ElevenLabs |
| place block | `BlockChanged` | 1–2 | proc/EL |
| craft success / fail | `CraftResult` | 2 | proc |
| blueprint unlock | `CraftResult`/server msg | 1 | proc |
| action rejected | `ActionRejected` | 1 | proc ✓ |
| hotbar select, inventory move | client input | 2 | proc |
| eat / drink / heal | `ConsumeItem` | 2 | EL |
| loot container | `LootContainer` | 1 | EL |
| disassemble | `Disassemble` | 1 | proc |
| scan blip | `ScanResult` | 1 | proc ✓ |

## 3. Tools & weapons — *ElevenLabs (organic/energy) + procedural toggles*

| Sound | # | Source |
|---|---|---|
| drill loop + impact | 2 | EL |
| energy weapons: gauss / laser / plasma fire | 3 | EL |
| melee: swing + hit (machete/vibro/plasma) | 2–3 | EL |
| glove weapons: whoosh, shock blast, energy hit, charge, dizzy (#2278 — see §17) | 5 | EL |
| scanner ping ✓, lamp on/off, stealth on/off, teleporter, jetpack loop | ~6 | proc |

## 4. Movement — *ElevenLabs (footsteps) + procedural*

| Sound | # | Source |
|---|---|---|
| footstep per surface (rock/sand/metal-deck/grass/snow) | 5 | EL |
| jump, land, swim stroke, jetpack thrust | 4 | proc/EL |
| spring-boot jump ✓ (`spring_jump`), glider open ✓ (`glider_open`) + wind loop while gliding ✓ (`glider_wind`, seamless, `GlideTick`) (#2295/#2296) | 3 | EL ✓ |
| hover speeder: engine loop ✓ + startup / shutdown / impact ✓ (`vehicle_*`) | 4 | EL ✓ |
| boat (#1215): outboard putter loop ✓ (`boat_engine_loop`, seamless) + launch / board / dismount splash ✓ (`boat_splash`) | 2 | EL ✓ |

**Footsteps (#3239)** follow `Client.Core/Audio/FootstepRules` + `FootstepCadence`: the interval follows the horizontal speed (0.45 s at the world's walk, down to 0.32 s running, 0.6 s sneaking at 60 % level), the feet alternate (±3 % pitch, ±8 % level), the three takes of a surface (`step_x`, `_2`, `_3`) never repeat back to back, and every step carries ±4 % pitch / ±10 % level. `FootstepSurface` reads what covers the ground at the feet first (shallow water → `step_water`, moss/needles/leaves → `step_leaves`, a grass tuft → grass), then the cell under the feet — a parked ship's cell first, then the world's (#2743; the world alone read air on a ship's deck). The key heuristic knows rock, grass, sand, snow, metal, wood, leaves, grate (bridge deck, lift platform), crystal/glass, and treats ores as the stone they sit in. A landing is picked by the fall speed against the world's safe fall speed: nothing for a stair (< 0.28 s in the air), a footstep for a hop, `land` for a drop, `land_heavy` from 0.85 × the safe speed. Water (`SwimSounds`): a splash when the feet hit the water falling (by speed), a smaller one leaving it after a swim, strokes every ~0.95 s, a very quiet bubble bed while the head is under, a gasp after a dive over 6 s. A climber's hand-over-hand taps use the wall's step takes. Other players' steps and landings are read from their drawn body; only the nearest three within 16 m play, through the remote-player voice budget. On a spacewalk the suit thrusters puff (`suit_thruster`/`_2`) when a push starts or turns by more than 60°, at most every 0.25 s. All own body sounds go through `ClientAudio.PlayOwn` (2D, pitched, the cabin's room aboard, the suit in vacuum). In third person the one AudioListener (a child of the camera, "Player Ears") sits at the head, keeping the camera's rotation; in first person, the flight view and cinematics it is on the camera.

## 5. Vitals & alerts — *procedural*

low-oxygen warning (loop), low-health heartbeat, hunger pang, take-damage, death, respawn, heal-tank
restore → **~7**, procedural cues (alarms/tones).

**The body layer (#3240, `Client.Core/Audio/BodySounds`, light by decision):** a calm exhale after a run of more than 5 s,
a jetpack burn of more than 3 s or a hard landing, at most every 8 s; at or below the HUD's 25 O₂ alarm line a soft
`breath_quick` every 5–7 s in addition to the beeps; below 20 % health a soft `heartbeat_soft` every 1.1–1.4 s at 0.25,
fading in over 4 s. All of it stops at once while dead or respawning, in a menu, a cinematic or a cutscene; no breath while
the head is under water.

## 6. Ship as a place — *procedural loops + ElevenLabs one-shots*

| Sound | Trigger | # | Source |
|---|---|---|---|
| hull ambience hum loop ✓ | aboard | 1 | proc ✓ |
| door / airlock open-close | enter/exit | 2 | EL |
| world doors: slide open/close (sci-fi), hinge creak (village) ✓ | `DoorView` open/close | 3 | EL ✓ |
| hatch pressure hiss ✓ (#2817) — your own ship's hatch closes behind you | `DoorView` close, aboard | 1 | proc ✓ (`station_board`, see §23) |
| station use: heal-tank, cockpit, workshop, cargo, quarters | `UseStation` | 5 | proc |

## 7. Ship systems & space — *ElevenLabs + procedural*

| Sound | Trigger | # | Source |
|---|---|---|---|
| engine idle + throttle loop | in space (SpaceView) | 1–2 | EL/proc |
| launch roar, landing settle | enter/leave space | 2 | EL |
| hyperspace charge + jump (warp) | (planned travel) | 2 | EL |
| ship weapon fire, hull hit, shield hit, ship destroyed | `FireWeapon`/`ShipCombatStatus` | 4 | EL |
| docking clamp / undock | `DockStatus` | 2 | proc |
| asteroid break | `SpaceEntityDestroyed` | 1 | EL |

## 8. Creatures (procedural species) — *ElevenLabs banks, pitch-varied*

The game generates **unbounded** species, so we author **parametric voice banks** and pitch/rate-shift
per individual by its `Size` (and brighten/distort for `Hostile`). Bank chosen by **size tier ×
disposition**:

| Bank (size × disposition) | States per bank | 
|---|---|
| small-calm, small-hostile, medium-calm, medium-hostile, large-calm, large-hostile | idle, alert, attack, hurt, die |

→ **6 banks × 5 states = ~30 source sounds**, ElevenLabs (organic). Per-creature variety comes from
pitch ∝ 1/Size + small random detune, so 30 assets cover every species. (A leaner v1 = 4 banks ×
4 states = 16.) Bioluminescent/insectoid flavour can add 1–2 extra "chirp" banks later. Hooks:
`CreatureList` positions + `AttackEntity`/hurt/death already exist.

**Companion guard growl (#1210, shipped 2026-08-23):** `creature_companion_growl` + `_2` — two ElevenLabs
takes ("short warning growl of a friendly alien pet standing guard, protective not aggressive"), played once per
`NetCreature.Alerting` flip through the species' own timbre (`CreatureVoiceBank.Resolve`, pitch ∝ 1/Size), so a
tiny pet yips and a titan rumbles off the same file.

**Giants (#1998–#2002, real sounds since 2026-09-26):** six ElevenLabs one-shots played by `CreatureView.PlayWorldFx`
straight from the server's `WorldFx` kind, positioned at the event, volume falling off with distance like the camera
shake — `giant_stomp` (the colossus footfall; a sub-bass impact with debris), `sandworm_breach` (sand bursting + the
roar), `sandworm_strike` (the head coming down), `sandworm_dive` (the sand collapsing into the hole),
`sandworm_rumble` (the approach, every 1.5 s at the worm's position, scaled by the server's strength) and
`thumper_thump` (the piston thud every 2 s). Until then the stomp and the strike borrowed the storm's thunder
one-shots and the breach / rumble the `creature_call_rumble` voice sample.

**Arena Nigra (#2076/#2079, 2026-09-27):** two more ElevenLabs clips — `sandworm_swallow` (a worm gulping an animal
under the sand: wet crunch, gulp, cascading sand; played by `CreatureView.PlayWorldFx` for the server's `swallow` kind
at the eaten animal's spot) and the ambience bed `amb_black_dunes` (hot wind over black dunes, distant lava, cooling
rock — `ClientAudio.BiomeBed` for the `arena_nigra` key). The Crystal Net's chime, horn and siren keep their own clips
(#2052); on sea sand they now also shake the ground for the worm, which is a server rule, not a sound.

**The leviathan (#2111, 2026-09-27, Justus' idea):** four more ElevenLabs one-shots for the sea giant's `WorldFx` kinds
(the sandworm's kinds in water, played by `CreatureView.PlayWorldFx` with pale spray instead of dust and a smaller camera
shake — water carries no tremor to the shore): `leviathan_breach` (a wall of water erupting + a deep whale-like bellow),
`leviathan_strike` (the body slamming down, a hull cracking), `leviathan_dive` (a massive body sliding back under, water
closing over it) and `leviathan_wake` (the approach: something enormous moving fast just under the surface, every 1.5 s at
the giant's position, scaled by the server's strength).

**The gas giant (#2112, 2026-09-27, Justus' idea):** two ElevenLabs clips — the ambience bed `amb_gas_giant` (an endless
howling wind over a bottomless sea of cold gas, thunder far below; `ClientAudio.BiomeBed` for the `gas_giant` key, a
seamless loop) and `sky_giant_call` (a vast, slow, resonant whale-like moan carried on the wind — played by
`CreatureView.PlayWorldFx` for the server's `skycall` kind at the giant's position every half minute or so, no camera
shake: it is far up).

## 9. NPCs (humans + androids) — *ElevenLabs, wordless speech + reactions* (#2592, 2026-10-14)

No words, ever — nothing to translate or re-record. Until #2592 the ten NPC clips were bundled but never played (this
section used to call them wired). Now NPCs **speak like VEGA** (#761/#2495): whenever an NPC shows a speech bubble, a
short babble of vocoder clips plays at the NPC. The Unity-free rules live in `Client.Core` (`NpcVoice`, `NpcVoiceGate`,
`NpcGreetingTracker`; tested in `NpcVoiceTests` / `NpcVoiceGateTests` / `NpcGreetingTrackerTests`); `NpcView` plays them.

**Speech banks — 6 × 3 clips** (`npc_voice_{human,robot}_{f,m,n}_{1..3}`, ~1.6 s each): human female / male / neutral,
robot bright / deep / neutral. Each clip is non-verbal babble (questioning, exclaiming, explaining).

- **A voice per NPC, matching the look** (`NpcVoice.VoiceFor`), deterministic from the face seed the avatar is built
  with: a human with facial hair (`NpcLooks.FacialHairFor`) speaks male (~80 %) or neutral, **never female**; other
  humans female ~45 % / male ~35 % / neutral ~20 %; androids one of the robot banks; the G.D.S. guardians the deep robot
  voice. Plus a **stable pitch** 0.92–1.08 (a human's divided by √body size), ±2 % jitter per clip.
- **Babble length** (`NpcVoice.BabblePlan`): one clip per ~40 characters of the bubble, 1–3 clips, chained, never the
  same clip twice in a row (also not across two lines of the same NPC). A sleepy NPC (greeted in bed) speaks lower
  (×0.88) and quieter (×0.5).
- **When:** every new bubble (greeting, passing hello, welcome, villager chatter, cheer, guest), and every line of the
  dialog panel (`NpcDialogUi` → `NpcView.DialogLine`; the first line as the greeting, later answers with a nod). An **AI
  upgrade** of a bubble (same `NpcGreeting.GreetingId`) only swaps the text — never a second babble or gesture.

**Reactions — the optional layer** (`NpcVoice.ReactionsEnabled`, on): `npc_human_{idle,trade,alert}` for humans,
`NpcVoice.RobotReactionPrefix` (`npc_alien_`, decided in the maintainer's sound review — one constant) for androids. A
finished market trade → the nearest vendor nods and plays *trade*; an alarm bubble (`Kind = Alert`) plays *alert*
instead of a babble; a rare quiet *idle* murmur from an NPC within 6 m (each NPC every 30–60 s); a server-sent **nod**
gesture plays the short *ack* "mm-hm" (#3238, `NpcReaction.Ack`, through the same budget, never over a babble). The old
*greet* grunt is replaced by the babble (never both for one bubble) — its clips were removed in #3238.

**The voice budget** (`NpcVoiceGate` — maintainer requirement: a station deck or a busy village must never become a
jumble). Every NPC sound asks it first; a refused request is dropped, never queued — the bubble still shows, silent.

| Rule | Value |
|---|---|
| Babble channel | **one babble at a time**, globally. Priority: addressed (dialog / E greeting) > passing, welcome, guest > chatter, cheer > idle. A higher priority cuts the current babble (0.15 s fade); lower or equal is dropped. The speaking NPC may start its own next line. Requests of one frame resolve nearest-first. |
| Audible radius | babble ≤ 12 m from the listener (the addressed NPC ≤ 20 m), reactions ≤ 10 m |
| Crowd (> 6 NPCs within 15 m of the player) | every non-addressed babble shares one 6 s gap; chatter only from the conversation nearest the player; greetings at ×0.6; no idle murmurs |
| Reactions | one at a time, never over a babble (a babble cuts a reaction); alarm ≤ 1 per 8 s; idle murmur ≥ 20 s apart globally and ≥ 5 s after a babble |
| VEGA / cutscenes | while VEGA's chatter plays (`NarratorVoice.AnySpeaking`) or a cutscene runs, only the addressed NPC babbles, at ×0.5; everything else is skipped (a running babble fades out) |
| Per NPC | ≥ 1 s between two of its own babbles |

Playback: two caller-owned positional babble sources (a cut one fades while the next starts) and one reaction source on
`NpcView` (`ClientAudio.PlayVoiceOn`: 3D, linear 4–20 m rolloff, master × SFX, the underwater muffle), following the
speaker's head. The banks are preloaded when the first NPC with that voice appears (a browser fetches audio on demand).

## 10. Weather & environment — *ElevenLabs loops + procedural ambience*

| Sound | Trigger (`WorldEnvironment`) | # | Source |
|---|---|---|---|
| wind loop (light/strong) | weather state | 2 | EL |
| rain loop, storm loop | weather | 2 | EL |
| thunder one-shots (muffled under a roof since #2805) | storm | 2–3 | EL |
| per-biome ambient bed (forest/desert/ice/lava/swamp/cave) | planet/biome | ~6 | EL |
| lava bubbling loop, water/shore loop; fire crackle (a spreading fire, a lit campfire — #2806) | near fluid / fire | 2 | EL |
| night layer ✓ (#2825): crickets, frogs, night wind, hum, chimes — picked from the world's data, see §23 | world clock + world | 5 | EL ✓ |
| poisoned-world bed `amb_toxic` (Toxica-Maxima + the toxic class, #2068) | planet key | 1 | EL |
| acid-storm bed `toxic_storm_loop` (the `toxic_storm` event, and a storm whose rain is acid, #2063/#2064) | weather / precipitation | 1 | EL |
| decontaminator rinse `decontaminate` (#2067) | wash craft cue | 1 | EL |

## 11. Music — ✅ SHIPPED (context cross-fade + Suno track library, 2026-06-13)

Two **player-selectable** music sources (*Settings → Audio → Music style*,
`ClientSettings.MusicMode`), both cross-faded over ~2.5 s on the music bus by the **persistent**
`ClientMusic` director (now owned by `AppShell`, so it spans **splash → menu → loading → in-game** —
the old "main-menu hook still open" gap is closed):

- **Synth** — a **generative ambient engine** (`SynthComposer` in Client.Core, #1176): every piece is
  composed fresh from a seed per mood (menu / planet / space / combat) — mode, tempo, two chord phrases,
  arpeggio patterns, pad timbre, drone — and rendered in code over a few frames (22.05 kHz mono, 40–110 s,
  click-free seams). Planet pieces take root + mode from the biome (all ice planets share one flavour),
  so biomes stay recognisable while no two pieces repeat. It replaced the four fixed 10–24 s
  `Resources/audio/music_*` loops and is also the fallback whenever a Tracks-mode file is missing.
- **Tracks** (default) — the AI-composed library shipped as raw MP3s in `client/Music/` → synced to
  `StreamingAssets/music/` and **streamed on demand** by `ClientMusic` (`UnityWebRequestMultimedia`; only
  the playing/fading/planned clips stay in memory — #1167), mapped to many contexts by `MusicLibrary`
  (Client.Core): main menu, loading, ship interior, station/hub, space flight, star chart, workshop /
  research tabs, per-biome planet beds (ice / desert / lava / toxic / ocean / verdant / crystal / cave),
  deep water and a generic idle pool. See [MUSIC_TRACKS.md](MUSIC_TRACKS.md) for the full mapping + the
  prompt of every track.

Variety (#1172–#1174): `MusicPicker` plays every track of a pool once before anything repeats (shuffle
bag), blends the neutral all-round beds into the biome pools at a minority share (biome identity stays;
the time of day only tints the filler set — sunrise at dawn, the nocturnal track at night) and keeps a
short cross-context history; `MusicRestPolicy` lets the music **rest** (ambience only, 60–180 s) after a
track ends on planets / in space / aboard, never in the menu, loading, station or finale. A storm or a
hostile creature nearby **ducks** the music (hostile: plus a 1.4 kHz low-pass), a long dive switches to
the deep-water bed, the first landing on a planet in a session opens with the sunrise track.

Combat is inferred client-side (hull+shield drop while in space → 14 s window) and always uses the
tense **synth** combat mood in both modes — the track library is intentionally all-calm. Music
muffles underwater (low-pass), and rides `MusicVolume` while SFX/ambience stay independent on
`SfxVolume`. The studio/title splash stings are left untouched (music is silent over the splash).

## 12. Crystal Net devices (#2052 / #2058) — *ElevenLabs clips + procedural notes*

The server drives every device sound through one message, `SoundFx` (`SoundId`, position, `Pitch`, `Loop`,
`Stop`, `SourceId` = the device id so a stop finds its loop). A loop is started once and stopped once; nothing is
re-sent per beat, and at most **8** sound devices play per world (`CrystalNetRules.MaxSoundDevicesPlayingPerWorld`).
All cues are 3D at the device cell. See [CRYSTAL_NET.md](CRYSTAL_NET.md).

| Sound | Trigger | Files | Source |
|---|---|---|---|
| alarm siren (3 to pick from) | network ON → loop, OFF → stop | `alarm_siren_0..2` (seamless loops) | EL |
| chime (4) | rising edge | `chime_0..3` | EL |
| horn (3) | rising edge | `horn_0..2` | EL |
| melody block: 8 notes (C4…C5) × 4 instruments (crystal sine / bell / bass / blip) | rising edge | `note_<inst>_<n>` — no files | **procedural** (`ProceduralAudio.Note`, pre-filled into the cue table) |
| announcer | rising edge (a toast to the owner + allies) | `ai_blip` (existing VEGA chirp) | EL (existing) |
| switch flip / button press | the player's Interact | `crystal_switch`, `crystal_button` | EL |
| auto-drill | working (loop while its network holds it ON and it is not blocked — wired by #3238, `GameServerCrystalDrillSound`) | `auto_drill_loop` | EL |
| drill laser (#2108) | one cell cut (per `WorldFx laser`) | `drill_laser_zap` | EL |
| clone tank | growing (loop for 60 s), stop on release | `clone_tank_bubble` | EL |
| fabricator | one craft done | `fabricator_craft` | EL |
| caller | pulse | `caller_whistle` | EL |
| matter link | one shot, at both ends | `beam_teleport` (existing) | EL (existing) |
| auto-drill mined block | per block | `WorldFx` "thump" (existing giants path) | EL (existing) |

Sixteen new ElevenLabs clips in total (`tools/ai-assets/gen_sound.py`, logged in `NOTICES.md`); the melody
notes are the only new procedural cue. The lamp swap makes no sound.

**Crystal Net 2 (#2251, #2270)** — twelve more ElevenLabs clips, all 3D at the device (aboard a parked ship at the
cell's world position):

| Sound | Trigger | Files | Source |
|---|---|---|---|
| phase block | it opens or closes | `phase_shimmer` | EL |
| trapdoor | it folds open / closes | `trapdoor_open`, `trapdoor_close` | EL |
| force field | it switches on / off | `force_field_on`, `force_field_off` | EL |
| bridge motor | one deck plank laid or taken up | `bridge_motor` | EL |
| piston | push / retract | `piston_push`, `piston_retract` | EL |
| lift | moving (loop, `SourceId` 100000 + lift id) / arrived | `lift_motor` (seamless loop), `lift_arrive` | EL |
| dice block | every roll | `dice_roll` | EL |
| remote control | a flip (to the user only) | `remote_click` | EL |

Campfires and forges switching on and off are silent, like the lamp swap (a lit campfire crackles while you are near it —
§23).

## 13. Wall climbing (#2193) — *ElevenLabs clips + the step set*

Local, non-positional cues from `PlayerController` (see [CLIMBING.md](CLIMBING.md)). The grip has no HUD bar, so
the breath is one of the ways the player feels it tiring.

| Sound | Trigger | Files | Source |
|---|---|---|---|
| grab | a wall is grabbed, a pull-up starts | `climb_grab` | EL |
| hand over hand | every ~0.55 s while moving on the wall (slower as the grip tires) | the wall's `step_*` clip | EL (existing) |
| strained breath | every 2.2 → 1.3 s while the grip is below 25 % | `climb_strain` | EL |
| slide | every 0.9 s while a spent grip slides down | `climb_slide` | EL |

---

## 14. The bio lab (#2210) — *ElevenLabs clips*

Positional one-shots the server sends as `SoundFx` at the player (the sampler's at the animal); see
[BIO_LAB.md](BIO_LAB.md). `ProceduralAudio` holds a synthesised stand-in for each id.

| Sound | Trigger | Files | Source |
|---|---|---|---|
| analysis | a sample is analysed, a seedling is raised | `bio_lab_analyse` | EL |
| mix | a mix holds, a tool or a piece of gear is changed | `bio_lab_mix` | EL |
| failed mix | a mix falls apart | `bio_lab_fail` | EL |
| effect starts | a preparation is taken | `bio_effect_start` | EL |
| effect ends | a status effect runs out | `bio_effect_end` | EL |
| sample taken | the sampler takes a sample from a living animal | `bio_sample_take` | EL |

## 15. Ship scanner, space encounters, wormholes (#2243) — *ElevenLabs clips*

Played by the client (`ClientAudio.Cue`, loops on the encounter's own source); see [SHIP_SCANNER.md](SHIP_SCANNER.md)
and [WORMHOLES.md](WORMHOLES.md).

| Sound | Trigger | Files | Source |
|---|---|---|---|
| lock-on | the scanner locks a target | `ship_scan_lock` | EL |
| charge | fire held on a target | `ship_scan_charge` | EL |
| scan done / hostile | a scan completes (a machine or raider: the warning blip) | `ship_scan_complete`, `ship_scan_hostile` | EL |
| planet overview | a body scan or the Quantum system sweep | `planet_scan_overview` | EL |
| pod beacon (loop) / rescue | a life pod nearby / it is pulled aboard | `pod_beacon`, `pod_rescue` | EL |
| anomaly hum (loop) / reaction | an anomaly nearby / it is scanned | `anomaly_hum`, `anomaly_react` | EL |
| rift hum (loop) / enter / exit | a wormhole nearby / flying in / coming out | `wormhole_hum`, `wormhole_enter`, `wormhole_exit` | EL |

## 16. Flight target lock (#2283) — *ElevenLabs clips*

Short 2D cues (`ClientAudio.Cue`, ≈0.5 s each, soft — no alarm tones, the lock must never feel like a siren to a
child); see `SpaceView.Targeting.cs`. `ProceduralAudio.TargetIds` holds a synthesised stand-in for each id.

| Sound | Trigger | Files | Source |
|---|---|---|---|
| lock | a fresh lock on a neutral or friendly target | `target_lock` | EL |
| lock on an enemy | a fresh lock (or the auto-lock) on an enemy or a raider demanding cargo | `target_lock_hostile` | EL |
| cycle | switching the lock to another target | `target_cycle` | EL |
| lost | the lock is let go, drifts out of range or its target vanishes | `target_lost` | EL |

## 17. Glove weapons (#2278) and other players' melee (#2279) — *ElevenLabs clips*

Short cues, friendly on purpose — air, crackle and a cartoon "boing", never a blow that sounds like it hurts. The cue
of a melee swing and of its hit is chosen by the item's `fx.style` in one place, `FxStyleResolver.MeleeSwingCue` /
`MeleeHitCue` (Client.Core), so the player's own swing (`PlayerController.PlayWeaponSound`, 2D) and another player's
(`FxRemote`, 3D at the remote avatar — **#2279: other players' melee used to be silent**) always match.
`ProceduralAudio.GloveIds` holds a synthesised stand-in for each id.

| Sound | Trigger | Files | Source |
|---|---|---|---|
| glove whoosh | every energy-glove jab (2D own, 3D others) | `glove_whoosh` | EL |
| shock blast | the shock push leaving the palms (after the 0.12 s wind-up); quieter at the target on a hit | `glove_shock_blast` | EL |
| energy hit | an energy-glove punch landing (instead of `melee_hit`) | `glove_energy_hit` | EL |
| charge | the shock gloves' wind-up (soft), and pulling either pair on (switching to them in the hotbar) | `glove_charge` | EL |
| dizzy | a creature, robot or bandit starts its daze (3D at the target, once per push) | `glove_stagger` | EL |

Every other melee look keeps `melee_swing` / `melee_hit` — now also heard from other players.

## 18. Energy rope gun (#2317) — *ElevenLabs clips*

The rope is friendly and springy, never a weapon: a zip for the shot, a soft thunk with a click when the hook bites, a
gentle winch hum while reeling (a loop, faded in and out like the glider's wind), and a twang-fizzle when it lets go.
`ProceduralAudio.RopeIds` holds a synthesised stand-in for each id; the prompts are recorded in
`tools/ai-assets/gen_batch.py`.

| Sound | Trigger | Files | Source |
|---|---|---|---|
| shot | the rope leaves the gun (2D own; 3D at another player's hand on their confirmed shot) | `rope_fire` | EL |
| hook | the server confirmed the anchor — 3D at the anchor; quieter when the winch locks into a hang | `rope_anchor` | EL |
| winch | every frame of a pull (`ClientAudio.RopeTick`) | `rope_reel` (loop) | EL |
| let go | a crouch, a hop, a snapped rope — and quietly a shot that found nothing to hold | `rope_release` | EL |

## 19. Debris fields and raiders (#2358) — *ElevenLabs clips*

A debris field is peaceful content with a little texture: the rubble that taps the shield is a dull clank with a fizz,
never an impact; a salvage capsule coming aboard is a latch and a warm two-note chime (the tractor-beam look does the
rest); the raider's warp-in gets the sting a trader's silent arrival never had — deep, heavy, menacing, not frightening.
The shield-hit cue (`ship_shield_hit`) and the camera jolt still fire on the status drop itself; the clank is layered on
top only when the client sees no attacker and the ship inside a field. The prompts are recorded in
`tools/ai-assets/gen_batch.py`.

| Sound | Trigger | Files | Source |
|---|---|---|---|
| rubble tap | a shield drop with no hull damage and no hostile in range while inside a debris field (3D at the hull) | `debris_bump` | EL |
| capsule aboard | a salvage capsule vanishes within tractor reach of the ship (2D, with the beam) | `salvage_capsule` | EL |
| raider warp-in | the `SpaceWarpFx` flash flagged `Raider` (3D at the arrival point) | `raider_warp_in` | EL |
| hunter shuttle warp (#3097) | the `SpaceWarpFx` flash with `Style` `hunter_shuttle` — its arrival, and its jump away to land (pitched up) (3D at the flash) | `hunter_shuttle_warp` | EL |

## 20. Story chapter stings (#2474) — *ElevenLabs clips*

VEGA's chapter scenes and beat cards ([CUTSCENES.md](CUTSCENES.md#vegas-chapter-scenes-and-beat-cards-2474)) open with a
short sting played by the cutscene director (`sfx` cue of the scene, `ClientAudio.Cue`). Calm, friendly sci-fi — a
"chapter begins" signal for kids, never a scare; 2.5–3 s, each under 50 KB because every `Resources/audio` clip is part of
the browser download. Generated with `tools/ai-assets/gen_sound.py` (`--influence 0.5`), 2026-10-12, prompts below.

| Sound | Trigger | File | Prompt (duration) |
|---|---|---|---|
| chapter begins | chapter cards 1, 4, 6 | `chapter_sting` | "Short calm sci-fi chapter title sting: a soft warm synth pad swells up and resolves on one gentle bright chime, hopeful and friendly, clean and simple, no drums, no voice" (2.5 s) |
| VEGA remembers | chapter 2's memory chart, every beat card | `chapter_sting_memory` | "Short dreamy sci-fi memory sting: soft shimmering crystal bells over a warm low synth pad, gentle and a little mysterious, like a fond memory coming back, no drums, no voice" (2.5 s) |
| a big reveal | chapters 3, 5 and 7 (the staged scenes) | `chapter_sting_reveal` | "Short sci-fi discovery sting: a deep soft synth swell rising into a wide glowing chord with a gentle sparkle on top, wondrous and awe-inspiring but never scary, no drums, no voice" (3 s) |

**VEGA's voice and ambience ducking in scenes (#2495)** — no new clips. VEGA's non-verbal chatter (`vega_chatter_1..3`,
#761) now also plays in the cutscene speaker box while it types: one shared `NarratorVoice` (the panel and the director;
random clip, never the same twice in a row, pitch 0.94–1.08, master × SFX × 0.5, silent behind the loading veil). The
clips a speaker uses come from the pack's `narrator.voice` / `speakers[].voice` (both validators check they exist); the
Guardian core has none. While a scene plays, `ClientAudio` ducks the **ambience beds** (weather/biome/cave bed,
water/lava/fire/fence bed, ship hum, thunder) to 0.7 under a card and 0.4 under a staged scene, eased over about a second;
cues, the voice and the music are never ducked. Details: [CUTSCENES.md](CUTSCENES.md#unity-cutscenedirector).

## 21. The derelict guardian bay (#2635) — *ElevenLabs clip*

A derelict's guardian bay ([DERELICT_STATIONS.md](DERELICT_STATIONS.md#guardians--the-guardian-bay-2635)) releasing a
robot or a drone. The server sends it as a `SoundFx` (3D at the bay) together with a red spark `WorldFx` of the same kind;
the machines themselves keep their planet banks (`enemy_growl` / `_attack` / `_hurt` / `_die`). Generated with
`tools/ai-assets/gen_sound.py` (`--influence 0.4`), 2026-10-15.

| Sound | Trigger | File | Prompt (duration) |
|---|---|---|---|
| bay opens | a guardian is released | `guardian_bay` | "an old sci-fi robot maintenance dock opening: a short pneumatic hiss, a heavy metal hatch clunk and a rising robotic servo whir powering up, slightly broken and crackly" (2 s) |

## 22. The wilderness folk at work (#2647) — *ElevenLabs clips*

The lodge residents and wanderers ([WILDERNESS_FOLK.md](WILDERNESS_FOLK.md)). Quiet and short-range on purpose, so a busy
hut never turns into a sound jumble: the bow shot and the falling tree arrive as positional `SoundFx` from the server
(volume 0.45 / 0.55 instead of 0.8, skipped beyond 20 m); the axe chop is client-local, 0.28 s into the woodcutter's swing
while it is felling, within 16 m, at most one every 0.5 s across all woodcutters and never while VEGA or a cutscene speaks
(`NpcChopGate`). NPC voices are unchanged (`NpcVoiceGate`). Generated with `tools/ai-assets/gen_sound.py`, 2026-10-15.

| Sound | Trigger | File | Prompt (duration, `--influence`) |
|---|---|---|---|
| bow shot | a hunter shoots (with the arrow `WorldFx`) | `npc_bow_shot` | "a single soft wooden bow shot: one gentle bowstring twang and a quick light arrow whoosh flying away, close and clean, short, no impact, no echo, no voice" (1.0 s, 0.4) |
| axe chop | a woodcutter's swing while felling | `npc_axe_chop` | "woodcutter chopping a tree: one single axe strike into a tree trunk, a dry wooden thock, close and clear, single hit, no echo, no voice" (0.7 s, 0.5) |
| tree falls | a felled tree starts to come down | `tree_fall` | "a small tree falling over in a forest: a slow creaking crack of wood, branches and leaves rustling and swishing down, then a soft muffled thump on grass, gentle and not scary, outdoors, no voice" (2.5 s, 0.4) |

## 23. The cozy package (#2802): rain on the hull, the campfire, the night, the hatch

**Through a roof (#2805).** A weather bed (rain, storm, drizzle, wind, gale, blizzard, sand, ash, acid, ion, spores,
meteors) used to fall silent the moment the sky was covered. Now, under ANY roof on a planet — aboard the ship, in a house,
in a cave — it keeps playing at about a third of its open-air level (`MuffledBed.RoofLevel` 0.33) with its highs cut (a
1 kHz low-pass), and bad weather wins over the cave bed. Thunder rolls in through the roof too: muffled (the underwater bake,
`SampleKit.Muffle`) at half its level. In space, in the flight view, on a spacewalk or a station the weather stays silent as
before. The biome beds and the cave bed are not muffled.

The bed and the night layer each live on their own child object of `ClientAudio` (`MuffledBed`): a filter on the
ClientAudio object would muffle every cue on it. **Desktop:** an `AudioLowPassFilter` on that child sweeps from 22 kHz down
to 1 kHz on a log scale (and to 680 Hz under water, which the bus filter used to do for the bed). **Browser:** Unity Web
ignores filter components (#878), so a muffled copy of the loop is baked once (`SampleKit.MuffledLoop`: mono, two one-pole
low-passes, half rate, the filter warmed on the clip's tail so the copy still loops seamlessly) and cross-faded in on a twin
source. Baking needs readable samples, so the weather beds and the night loops carry a **WebGL importer override to
Decompress On Load** (their default stays Compressed In Memory, #1524); a clip that still cannot be read plays dry at a lower
level instead. The copies are owned by the bed (at most eight) and destroyed with `ClientAudio`.

**The campfire (#2806).** The nearby-block scan starts the `fire_crackle` bed for a lit `campfire` too (not `campfire_off`),
at 0.28 of the SFX level instead of a spreading fire's 0.4. A spreading fire and lava still win; a campfire wins over any
water in reach (you sit at it).

**The night layer (#2825).** A second quiet loop over the day bed: it fades in from a little before sunset to shortly after
(`NightAmbience.NightLevel`, the sky's own sun-height curve `sin((t − 0.25)·2π)`, from +0.15 down to −0.15) and out the same
way at dawn; only while no weather bed plays (rain and storms take over); through a roof like the weather bed; never in
space, the flight view, on a spacewalk or a station. While it plays, the biome bed eases to 70 %. The worlds are procedural,
so the loop is picked from what the world and the spot **have**, never from a planet's name (`NightAmbience.Pick`, Client.Core,
swept over every planet type of the data in `NightAmbienceTests`):

| Pick | When (first rule that matches) |
|---|---|
| silence | no air (airless type, space sky, no atmosphere density), a cabin/station deck (`Void`), a gas world |
| silence | poisoned: contaminated fauna, any chance of corrosive air, acid falling (the toxic-world class) |
| silence | scorched: ash flora, a lava-rich type (lava abundance ≥ 0.3), lava in reach, ash falling, 45 °C or hotter |
| `night_chimes` | a living world (`PlanetType.HasLife`) with crystal flora |
| `night_hum` | a living world with fungal or alien flora |
| `night_wind` | frozen (below 6 °C at the player), barren (no life) or air no animal of ours breathes |
| silence | a living, breathable world, but fewer than three plants/leaves/grass cells sampled around the player |
| `night_frogs` | water in reach, 12 °C or warmer and a humid world (tropical or swamp flora, or the wet season ≥ 0.6) |
| `night_crickets` | everything else that is green, breathable and mild |

The surroundings come from a sparse scan (every second cell of a 25 × 9 × 25 box around the player, every 2 s and only at
night). While a loop plays, a different pick must hold for three scans in a row (~6 s) before the layer changes — walking
along a forest edge does not flip it back and forth; a new world starts afresh. The five loops are ElevenLabs, generated with
`tools/ai-assets/gen_sound.py --loop` (2026-10-19), imported mono at Vorbis quality 0.5 (quiet beds; it keeps the browser
download small):

| File | Length | Prompt (`--influence`) |
|---|---|---|
| `night_crickets` | 18 s | "seamless loop of a calm peaceful summer night in a grassy meadow: many soft crickets chirping steadily, some near and some far away, gentle and soothing, no birds, no frogs, no wind, no music, no voices" (0.5) |
| `night_frogs` | 18 s | "seamless loop of frogs at night by a pond: a lively but relaxed chorus of many small frogs croaking and chirping, crickets softly in the background, warm humid summer night, no birds, no rain, no music, no voices" (0.5) |
| `night_wind` | 12 s | "night wind ambience, a steady breeze blowing across an open empty landscape, whooshing air rising and falling" (0.3) |
| `night_hum` | 15 s | "eerie but calm alien mushroom forest night ambience, a low warm humming drone with soft gentle bubbling, no music, no voices" (0.3) |
| `night_chimes` | 15 s | "seamless loop of a glittering crystal field at night: soft scattered glassy chimes and gentle crystal tinkles ringing in a light breeze over a faint low shimmering drone, calm and magical, no melody, no music, no voices" (0.5) |

Generation note: prompts that leaned hard on "soft / gentle / calm" for wind and hum came back nearly silent or as an
inaudible sub-bass rumble; the plainer prompts above did not. Check a new loop's band levels before bundling it.

**The hatch (#2817).** When a sliding door of the player's **own** parked ship closes while they are aboard, and that door
leads outside — the hatch, or a side door built into the outer wall (`ShipHatch.OpensToOutside`: a line across the doorway
leaves the hull on one side) — the synthetic airlock (`station_board`, `ProceduralAudio.Airlock`: a pressure hiss and a clunk)
plays at the door 0.7 s after the slide. Doors between two rooms, hand doors and a friend's ship stay plain.

## 24. Festival jingles (#3025) — *ElevenLabs clips*

The festival banner (a festival the player celebrates begins while they play, `HudUi.Toasts`) plays a three-second jingle
instead of the plain success chime: the festival's own clip when one exists (`festival_halloween`, cosy-spooky pizzicato and
a friendly organ), else its theme's — `festival_harvest` (marimba, guitar), `festival_lights` (bells, glockenspiel),
`festival_newyear` (a small fanfare, firework sparkle), `festival_blossoms` (wind chimes, flute), `festival_colours`
(marimba, claps), `festival_stars` (shimmering synth bells), `festival_midsummer` (folk fiddle, hand drum),
`festival_children` (toy xylophone, slide whistle). About 49 KB each in `Resources/audio` (the WebGL payload grows by
~440 KB). With the festival mood off the plain chime plays.

## 25. The tank spider (#3096) — *ElevenLabs clips*

The tank spider ([SPACE_COMBAT_CONCEPT.md](SPACE_COMBAT_CONCEPT.md#planet-enemies-server)) warns before it bursts. Both
clips are client-side and 3D: the hiss plays at the spider the moment the server's `Inflating` turns on
(`WorldEntities.TankSpider`), the pop with the burst's `WorldFx` (`tank_burst`) — and, quieter and higher (pitch 1.45,
volume 0.45), when the spider is shot before it bursts and only pops. Its growl, hurt and push cues are the machines'
banks at a higher pitch. Generated with `tools/ai-assets/gen_sound.py`, 2026-11-01; ~45 KB together.

| Sound | Trigger | File | Prompt (duration, `--influence`) |
|---|---|---|---|
| tank swells | the tank starts to swell (the warning, 1.5 s before the burst) | `tank_spider_inflate` | "crescendo air hiss: a pressure tank being pumped up, a high hissing whistle that gets louder and higher in pitch all the way to the very end, with soft metal creaks, steady build-up, cartoon game sound effect, ends abruptly at its loudest" (1.5 s, 0.6) — the model's steady hiss, trimmed to 1.12 s, stretched to 1.44 s (`atempo=0.77`) and faded up from 30 % to full with ffmpeg so it really builds to the burst |
| burst / pop | the tank bursts (or, quieter, pops when shot) | `tank_spider_burst` | "a cartoonish metal pressure tank bursting: one punchy hollow pop with a short whoosh of escaping air and a few tinkling metal bits, a light thump, game sound effect, not violent" (1 s, 0.5) |

## 26. Held-item flourishes (#3095) — *ElevenLabs clips*

A drill, a gun or a blade that a hotbar switch has just drawn plays a short flourish (see
[HELD_ITEMS.md](HELD_ITEMS.md#5-flourishes-and-the-drill-bit-3095)), and its cue plays with it at volume 0.45
(`HeldFlourishes.CueVolume`): 2D in first person (`Viewmodel`), 3D at the hand of a figure in third person and of other
players (`PlayerAvatar`). A blade's idle twirl (every 9–18 s while it is held still — the player's, a villager guard's, a
calm robber's) plays its swish too, at 60 % of that. Never while the
item is used — any use cuts the flourish short. Generated with `tools/ai-assets/gen_sound.py`, 2026-11-01; ~38 KB
together.

| Sound | Trigger | File | Prompt (duration, `--influence`) |
|---|---|---|---|
| drill test spin | a drill is drawn — the bit spins up and winds down | `drill_spinup` | "short handheld sci-fi power drill test spin, a quick rising electric whirr that spins up and winds back down, light and clean, no impact, toy-like game sound" (0.8 s, 0.5) |
| cosmetic reload | a gun is drawn — tipped and racked once | `gun_reload` | "short sci-fi energy pistol cosmetic reload, a crisp mechanical click-clack of a power cell snapping in, then a tiny soft rising charge chirp, friendly game sound, no shot" (0.8 s, 0.5) |
| blade twirl | a blade is drawn, and now and then while it is held | `blade_twirl` | "very short light airy swish of a small glowing energy blade twirled in the hand, a quick whirling whoosh with a faint shimmering hum, no impact" (0.6 s, 0.5) |

## 27. Levels: every clip on its class target (#3237)

The ElevenLabs renders went into the game at whatever level the generator produced. Unity's `normalize` import flag acts
only during a `forceToMono` downmix, which was on for the 74 creature calls only. So groups the code plays at the same
volume sat far apart (EBU R128 momentary max):

| Group | Spread | Example |
|---|---|---|
| footsteps | 20 dB | `step_metal` −43, `step_snow` −23 |
| mining | 32 dB | `mine_stone` −43, `mine_metal` −11 |
| hand weapons | 33 dB | `weapon_scrap` −42, `weapon_plasma` −8 |
| jump / land | 28 dB | `land` −37, `jump` −9 |
| rain family | 48 dB | `drizzle_loop` −52, `storm_loop` −4 |

**The rule now:** every clip in `Resources/audio` sits on its **class target**, ± 3 dB. One-shots are measured by their
loudest moment, loops and beds by their integrated loudness. The exception is a clip whose peak already reaches −2 dBFS,
like a click or a gunshot: it may stay below its target.

| Class | Metric | Target (LUFS) | What |
|---|---|---|---|
| step | momentary max | −24 | footsteps (all surfaces and takes) |
| foley | momentary max | −20 | climbing, landing, swimming, breath, wings, suit puffs, RCS, cooling ticks, cargo creaks |
| creature | momentary max | −10 | creature calls and combat banks |
| voice | momentary max | −11 | NPC babble and reactions, VEGA's chatter |
| sting | momentary max | −9 | festival jingles, chapter stings, splash stings |
| oneshot | momentary max | −12 | everything else that plays once |
| weather | integrated | −18 | weather beds (rain, storm, wind, sand …), rain on the hull |
| bed | integrated | −20 | biome, night and fluid beds, quiet ambience loops |
| machine | integrated | −18 | engines, motors, hums |

The targets sit at each class's old median, so the mix keeps the shape the code volumes were tuned for and only the
outliers moved. Two classes were wholly too quiet and moved as a class: steps (median −35) and body foley (median −37).

**How to add or replace a clip:**
1. Put the raw files in a folder.
2. Run `python tools/audio/level_clips.py --import <folder>`. This trims silence off steps and foley, levels each clip,
   writes a `.meta` (loops Compressed In Memory, one-shots Decompress On Load and mono) and rewrites
   `tools/audio/audio_levels.json` (class, level, peak, SHA-256).
3. `AudioLevelManifestTests` fails for any clip that is missing from the manifest, has changed since it was levelled, or
   sits off its target.

Never boost a clip by more than ~25 dB: re-generate it instead (the batch file `tools/audio/sound_immersion_batch.py`
holds every prompt of the 2026-11 set). `--apply --only <ids>` re-levels single clips. All `.meta` files carry
`normalize: 0`, so Unity's peak normalisation no longer undoes the levelling.

**The master bus.** `AudioListener.volume` is the master slider (`ClientSettings.Apply`). The effects level
(`ClientAudio.SfxVol`), the UI sounds, VEGA's cutscene voice, voice chat and the splash stings used to multiply master in
a second time (effects followed master²); they no longer do. The flight engine now follows the effects slider (it played
at an absolute level).

## 28. Mixing: distance profiles, roof occlusion, voice budgets (#3249)

Every 3D one-shot goes through one path (`ClientAudio.PlayAt` in ClientAudio.Mix.cs). The rules are Unity-free
(`AudioMixRules`, tested in `AudioMixRulesTests`).

- **Distance profile by cue id** (full volume inside min, linear to silence at max):

  | Profile | Range | Cues |
  |---|---|---|
  | small | 2–14 m | steps, wings, slither, splashes, breath, climbing |
  | medium | 4–20 m | the default |
  | large | 10–80 m | giants, blasts, warps, horns, other ships' shots |
  | huge | 20–160 m | ship launch/landing/touchdown, engine thrust, explosions, the sky giant |

- **Voice budget per category:** creature calls 4, creature foley 6, other players' steps 3. A refused request is
  dropped, never queued. At the cap a nearer sound replaces the farthest one of its kind.
- **Roof occlusion:** a sound with a roof (or a parked ship's hull) on exactly one side, the listener's or its own, plays
  its baked muffled copy (`SampleKit.Muffle`, so the browser hears it too) at half level. Nothing within 6 m is occluded.
  This is the weather beds' rule (#2805) applied to one-shots.
- `PlayOwn(id, vol, pitch)` plays the player's own body sounds: 2D, with a pitch, with a small baked metal-room tail
  aboard the ship and the suit treatment in vacuum.
- `AtTagged(id, pos, pitch, vol, category)` lets a caller name the budget a cue counts against.

## 29. Stylised vacuum (#3241)

Maintainer decision: the vacuum is **stylised, not silent**. In vacuum the player hears the world through the suit. "In
vacuum" (`VacuumAudio`, tested) means:
- a spacewalk;
- on foot in space;
- the surface of a body without air: an airless type, a space sky, or no atmosphere density.

A cabin, the ship and a station deck have air again. In vacuum:
- the weather, wind and biome beds stop;
- a soft suit room tone plays (`suit_room_tone`, 0.35 of the bed level);
- sounds out there arrive muffled at 0.45;
- what the player touches (within 3.5 m: their own steps, the drill, the mining target) stays clear at 0.8;
- the player's own body sounds are a little duller;
- cues that need air never play (`sonic_boom`, the wind loops, `cloud_rush`, the glider's wind).

A lift-off from an airless body makes no sonic boom (`ClientAudio.WorldAirless`), seen from the cockpit or from the ground.
On a spacewalk, short suit-thruster puffs (`suit_thruster`, `_2`) mark the moves. UI, VEGA and the music are unchanged.

## 30. Creatures: foley, repertoire, rhythm, herd, combat from the own voice, distant fauna (#3242–#3246)

All of this sits in `CreatureView.Audio.cs`; the rules are Unity-free and unit-tested.

**Movement foley without clutter (#3242, `CreatureFoleyGate`).** Animals used to move in silence. Foley added naively
turns a herd into rain: twelve four-legged grazers plant about 100 feet a second. So:
- **per species**, only the **two nearest moving animals** make footfalls;
- **per animal**, at most one footfall per gait cycle (a big body steps slower), never for tiny animals (size < 0.6),
  only while moving, within 12 m × √size (capped at 24 m);
- **globally**, at most **6 foley events a second** and never two closer than 70 ms;
- the mixer caps concurrent foley voices at 6 on top.

| Sound | Trigger | Clips |
|---|---|---|
| footfall | `CreatureFeet.FootPlanted` (the rig's foot comes down; giants keep their stomp) | the surface's step set (`FootstepSurface`), pitch ∝ 1/size, level ∝ size, clearly under the player's own steps |
| wing beat | an airborne flier that is not gliding; only the 2 nearest, each ≥ 0.5 s apart | `wing_flap`, `_2` |
| slither | the nearest limbless crawler within 10 m — ONE shared 3D loop, never one per animal | `slither_loop` |
| splash | a water/shore animal crossing the surface within 14 m (≥ 1.5 s apart) | `water_splash_in` / `_out` |
| chewing | a calm, awake animal standing still for 2 s within 7 m, every 6–12 s | `eat`, pitched by size, quiet |

**Repertoire (#3243).** A species has more than one thing to say:
- **contact call**: the voice genome's call, as before;
- **alarm call**: `CreatureVoice.AlarmCall`, a second call of the same body class (its own salt, so the main call never
  moved). It is spoken as 2–3 quick rising pulses when a herd member is hurt or turns hostile. Up to 3 conspecifics
  within 25 m echo it 0.2–0.6 s apart, so the herd startles together;
- **content sound**: the size's calm `_idle` bank clip (bundled since the creature banks, never played until #3238)
  through the species' timbre. It plays when fed (stops begging close by), when tamed, and on 30 % of a companion's idle
  phrases;
- **begging**: the call, higher (×1.1) and bowl-shaped.

**Rhythm (#3243, `CreatureCallRhythm`).** The species' calling interval is multiplied by:
- the hour against its activity: diurnal ×0.55 at dawn and dusk (a chorus), ×1 by day, ×2.2 at night; nocturnal ×0.85
  at night; crepuscular ×0.5 in the twilight; cathemeral ×1;
- the weather: rain ×2.5, a storm ×5;
- the end of a storm: ×0.5 for 25 s (a burst of calls).

An answer comes from the nearest real herd member (else from a spot nearby). The answer chance grows with the group:
0.12 + 0.05 per conspecific, at most 0.4.

**Combat cues from the own voice (#3244).** Hurt, alert, attack and die no longer come from the 24 shared bank files
alone. `SampleKit.CombatVariant` shapes the species' call, with the bank file as a quiet layer underneath (0.38):
- hurt: cut to 250 ms, gliding up 1→1.3, driven;
- attack: cut to 180 ms, driven;
- alert: the call gliding up a little;
- die: gliding down 1→0.7, fading, with a tail.

Hostile species bake their four cues at spawn (one bake per frame, #901); calm ones bake on their first blow. Four new
ops were appended to `VoiceOp` (never inserted: the value is part of every bake key): `Glide`, `Vibrato`, `Stutter`,
`Truncate`. A third of the voices that had no op now roll Glide, Vibrato or Stutter (a separate salt, so no voice that
already had an op changed).

**New material (#3246).**
- an **ethereal** class for medusae and tentacled glowers: `creature_call_ethereal` (+`_2`), `creature_call_chime_drone`;
- an **insect** class for six-plus legs or segmented bodies with legs: `creature_call_buzz` (+`_2`),
  `creature_call_rattle`, chitter, click;
- one more arachnid call `creature_call_click_hiss` and one more biped call `creature_call_gibber_squeal`;
- re-takes of the near-silent `creature_call_echo_click` and `creature_call_flutter`.

The pools only grew (rendezvous selection keeps ~(N−1)/N of the species on their voice). The alternate take `*_2` now
also plays for baked voices.

**Distant fauna (#3245).** The client only learns about the animals near it, so a world whose animals were out of sight
used to sound empty. On every world entry (join, travel, respawn, station, ship interior, ship visit) the server sends
`FaunaRoster` (tag 318, no protocol bump). It carries each species' id, `VoiceSeed`, its `VoiceTraits` fields and its
`Activity`, goes out with the environment send that follows the `WorldReset`, once per world (the 5 s heartbeat costs
one reference compare), and is empty on stations, in ship interiors and when the creature-abundance rule is Off.

`Client.Core/Audio/DistantFauna` (Unity-free, seeded, covered by `DistantFaunaTests`) plays one distant call every
20–60 s:
- **When it is quiet:** nothing calls in rain or a storm, under a roof, in vacuum, in space or during a cutscene. A call
  that came due meanwhile plays 2–8 s after the silence lifts.
- **Who calls:**
  - a species awake at this hour may always call, a sleeping one only now and then;
  - water and shore species only near water, cave species only underground (where nothing else calls);
  - a species already within hearing does not call from afar;
  - the same species never twice in a row while another one may;
  - medusas and tentacled bodies rarely.
- **How it sounds:** the caller stands 40–120 blocks out (small animals at most 75), at a random bearing, its level
  falling from 0.6 to 0.35. The client plays the species' own call (`CreatureVoices.Derive(VoiceSeed, VoiceTraits)`, the
  voice it has up close), dull and with a long tail. It is placed at a short virtual distance in that direction, so the
  panning gives the bearing while the scheduler sets the level.

## 31. The ship as a place (#3247)

| Sound | Trigger | # | Source |
|---|---|---|---|
| hull hum from the design ✓ — pitch from the hull's size, reactor pulse, life-support air cycle, jump-coil shimmer | aboard | 1 | proc ✓ (`ShipSoundscape.SynthesizeHum`) |
| module emitters ✓ — reactor, life support, medbay tank, cargo creaks; the nearest 3 within 8 m | aboard, walking | 4 | EL ✓ (`reactor_hum`, `life_support_cycle`, `cargo_creak*`) + `clone_tank_bubble` |
| cooling ticks ✓ — pings slowing down over ~30 s | after a landing, aboard and outside | 2 | EL ✓ (`ship_cooling_tick*`) |
| parked-ship idle hum ✓ | outside, within 15 m of your own hull | 1 | EL ✓ (`ship_exterior_idle`) |
| structure creaks ✓ — rare, ≥ 8 s apart | aboard, hull < 30 % | 2 | EL ✓ (`hull_creak*`) |
| rain on the hull ✓ | aboard on a planet in rain, drizzle, a storm or an acid storm | 1 | EL ✓ (`rain_on_hull`) |
| the airlock hatch ✓ — `door_open` / `door_close` instead of the plain slide | your own ship's outer door, aboard | 2 | EL ✓ (bundled since P2, never played until #3238) |

**The hum.** The ship's hum is generated from its design. `ShipSoundscape` (Client.Core) sets the base pitch from the
hull's size (a scout at 96 Hz, the hammerhead around 65 Hz) and adds a reactor pulse, the life support's breathing air
and a jump generator's shimmer. The result is one seamless 4 s loop, so there is nothing to download.
`ShipSoundscapeView` plays it in place of the router's old two-sine hum (`ClientAudio.ShipHumOverride`), at the bed level,
so it ducks under a cutscene (`ClientAudio.BedLevel`).

**Aboard.** Only the nearest three module emitters within 8 m play, quietly and faded by distance:
- the reactor at the engines;
- the life support at the ceiling;
- the medbay tank and the cargo hold at their stations.

While rain drums on the hull, the muffled outdoor rain eases to 35 % and `rain_on_hull` carries it.

**After landing and outside.** After a landing the hull ticks as it cools for about half a minute, heard aboard and
outside. Outside, a quiet idle hum comes from the nearest point of your own parked ship within 15 m, so a child can find
the ship by ear at night.

**Damage.** Below 30 % hull the structure creaks now and then. A downed wreck has no power and so no hum.

Everything is volume, pitch and position (Unity Web has no filters or Doppler), and a missing recording simply stays
silent. The client knows no ship power value, so the "hum dips at low power" idea is not built.

## 32. Flight audio (#3248)

**The engine.** The ship's engine is three layers instead of one loop (`FlightEngineMix`, Client.Core, tested):
- a steady **idle** hum at the old engine level;
- a **thrust** layer that swells gently with the throttle (and when you strafe);
- a short **afterburner** surge (`engine_boost`) when you push the throttle quickly from low to full, which then settles
  back, so full throttle is never a constant roar.

The pitch rises a little with drive. Every flight sound follows the effects slider.

**Perspective and manoeuvring.** In the cockpit view (V) you hear a quiet cockpit-electronics bed (`cockpit_ambience`)
and a darker engine: the idle carries, thrust and afterburner are held back, the pitch sits lower. The third-person view
hears the full exterior engine. Starting or reversing a strafe, yaw or pitch gives a soft manoeuvring-thruster puff
(`rcs_puff`, `_2`; at most a few per second, alternating).

**Other ships.** The nearest three other ships within about 90 units have their own 3D engine sound: big hulls rumble
lower, drones whine higher. Each fades with distance and rises or drops in pitch as it approaches or passes. This
Doppler is hand-made, so it also works in the browser build. Other pilots' and enemies' shots are heard from where they
fire (the large range).

**Weapons and hits.**
- The cannons play the recorded `ship_weapon` shot (bundled since P2, never played until #3238), the laser keeps its
  zap, mining keeps the mining zap.
- When your shield starts recharging after hits, `shield_recharge` plays once (at most every 5 s).
- Hull and shield hit cues are as loud as the blow, like the camera shake (0.45 for a scratch up to 1).

## 33. Ressaw blocks crack and leak (#3336, #3343) — *ElevenLabs clips*

In the buried G.D.S. ruins a glowing `ressaw_block` cracks when a player lingers next to it, and a cracked block that
breaks lets its Ressaw seep out into a small still puddle (#3336). The two cues are keyed by their file names like every
recorded clip (`ClientAudio` loads `Resources/audio` by name). Both are plain one-shots (class `oneshot`, levelled into
`tools/audio/audio_levels.json` by `level_clips.py`'s import path; both sit just under their target, limiter-bound).
Generated with `tools/ai-assets/gen_sound.py`, 2026-11-07, the second take of each (the first crack was a hissing noise
burst with a whistling 10 kHz ring, the first leak ~30 dB too quiet); ~41 KB together.

| Sound | Trigger | File | Prompt (duration, `--influence`) |
|---|---|---|---|
| crystal crack | a Ressaw block cracks (`ressaw_block` → `ressaw_block_cracked`) | `ressaw_crack` | "a single sharp crisp crack of a thick crystal glass block, like ice cracking under pressure: one clear snap followed by two quick smaller crackles and a faint glassy ring fading out, close-up, video game sound effect" (1 s, 0.5) |
| Ressaw leaks | a cracked block breaks and Ressaw seeps out | `ressaw_leak` | "close-up liquid leaking from a crack: thick milky liquid gushing out in a short glugging gurgle with a few wet drips splashing onto stone, clear and present, video game sound effect" (1.5 s, 0.5) |

---

## 33. The Advent calendar and Rudolpho (#3316, #3322) — *ElevenLabs clips*

`calendar_door_open` (~1 s, a small paper door and a sparkle; generated quiet, raised with ffmpeg, then levelled with the
other one-shots by `tools/audio/level_clips.py` and recorded in `audio_levels.json`) plays when a calendar door pops open (`UiFestivalCalendar.Reveal`). `reindeer_bells` (~1.5 s, a soft sleigh-bell
jingle) plays once when Rudolpho appears within 20 blocks of the camera (`CreatureBuilder.Reindeer`'s `ReindeerBells`).
Both in `Resources/audio`, loaded by `ClientAudio` like their neighbours.

## Totals & rollout

The planned rollout has largely landed. Current state:

- **Procedural (free):** UI bank (hover/click/confirm/back), vitals/alerts (O₂ warning, hull alarm,
  hurt), toggles (lamp/teleport/scan/station-board), ship one-shot fallbacks, and ambience-loop
  fallbacks — all in `ProceduralAudio`; menu hook in `UiSound`. **Done.**
- **ElevenLabs (recorded, now blanket-approved):** the recorded banks are **generated and bundled**
  under `Resources/audio/` — mining ×4, place/loot/heal, drill loop + impact, gauss/laser/plasma +
  melee, 5 footstep surfaces, ship & space one-shots (engine, launch/land, hyperspace, weapon/hull/
  shield/destroyed, asteroid, doors), **6×5 creature banks**, **2×5 NPC reaction banks + 6×3 NPC speech banks**
  (played since #2592, §9), and weather/biome beds (wind, rain/storm, 3 thunder, per-biome ambience, lava/water). **Done.**
- **Music:** both Synth and Suno **Tracks** modes ship (`ClientMusic`); see §11 + [MUSIC_TRACKS.md](MUSIC_TRACKS.md).

Rollout history (P1–P4), all shipped:
1. **P1 — core gameplay** (mining ×4, weapons, footsteps, place/loot/eat).
2. **P2 — ship & space** (engine, launch/land, weapon/hull/shield, doors).
3. **P3 — creatures** (6 banks × 5).
4. **P4 — NPCs + weather**.

> Open follow-ups: a few catalogued cues are not yet authored as dedicated assets (e.g. swim stroke,
> dedicated craft success/fail, separate eat/drink) — these currently rely on existing cues or
> procedural fallbacks. Extend with the same filename → clip pattern in `ClientAudio`.
