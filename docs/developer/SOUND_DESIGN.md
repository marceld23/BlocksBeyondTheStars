# Sound Design — architecture + catalogue (M26)

A catalogue of every sound the game uses, where it triggers, how many distinct variants,
and **how it is sourced**. This was the M26 audio plan; the bulk of it has since shipped.

> Status today: **mostly implemented.** The recorded ElevenLabs SFX banks (P1–P4 below) are
> generated and bundled under `client/Assets/Resources/audio/` — mining ×4, place/loot/heal,
> drill/weapons/melee, footsteps, ship & space one-shots, **6×5 creature banks**, **2×5 NPC
> banks**, and weather/biome beds — and are loaded by `ClientAudio` (filename → clip). Procedural
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
- **NPCs make non-verbal sounds only — no speech** (grunts/chirps/beeps), humans *and* aliens.
- **Variety from few assets:** pitch/rate-shift a small bank per instance (esp. creatures) instead
  of authoring one sound per species — the game has unbounded procedural species.
- Every bundled audio file is logged in `NOTICES.md` with source + licence.

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
| scanner ping ✓, lamp on/off, stealth on/off, teleporter, jetpack loop | ~6 | proc |

## 4. Movement — *ElevenLabs (footsteps) + procedural*

| Sound | # | Source |
|---|---|---|
| footstep per surface (rock/sand/metal-deck/grass/snow) | 5 | EL |
| jump, land, swim stroke, jetpack thrust | 4 | proc/EL |

## 5. Vitals & alerts — *procedural*

low-oxygen warning (loop), low-health heartbeat, hunger pang, take-damage, death, respawn, heal-tank
restore → **~7**, procedural cues (alarms/tones).

## 6. Ship as a place — *procedural loops + ElevenLabs one-shots*

| Sound | Trigger | # | Source |
|---|---|---|---|
| hull ambience hum loop ✓ | aboard | 1 | proc ✓ |
| door / airlock open-close | enter/exit | 2 | EL |
| world doors: slide open/close (sci-fi), hinge creak (village) ✓ | `DoorView` open/close | 3 | EL ✓ |
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

## 9. NPCs (humans + aliens) — *ElevenLabs, NON-VERBAL*

No speech. Short vocalisations only: **idle murmur, greet/notice, acknowledge, trade-confirm,
alert/flee** = 5 states × **{human, alien}** = **~10 sounds**, pitch-varied per NPC. Hooks: settlement/
station NPC markers + trade/mission interactions.

## 10. Weather & environment — *ElevenLabs loops + procedural ambience*

| Sound | Trigger (`WorldEnvironment`) | # | Source |
|---|---|---|---|
| wind loop (light/strong) | weather state | 2 | EL |
| rain loop, storm loop | weather | 2 | EL |
| thunder one-shots | storm | 2–3 | EL |
| per-biome ambient bed (forest/desert/ice/lava/swamp/cave) | planet/biome | ~6 | EL |
| lava bubbling loop, water/shore loop | near fluid | 2 | EL |
| day vs night ambience shift | world clock | 2 | proc/EL |

## 11. Music — ✅ SHIPPED (context cross-fade + Suno track library, 2026-06-13)

Two **player-selectable** music sources (*Settings → Audio → Music style*,
`ClientSettings.MusicMode`), both cross-faded over ~2.5 s on the music bus by the **persistent**
`ClientMusic` director (now owned by `AppShell`, so it spans **splash → menu → loading → in-game** —
the old "main-menu hook still open" gap is closed):

- **Synth** — the four code-synth ambient moods (menu / planet / space / combat), each the short
  bundled `Resources/audio/music_menu|planet|space|combat.mp3` loop with a synthesized fallback.
- **Tracks** (default) — the AI-composed Suno library shipped as raw MP3s in `client/Music/` → synced to
  `StreamingAssets/music/` and **streamed on demand** by `ClientMusic` (`UnityWebRequestMultimedia`; only
  the playing/fading/prefetched clips stay in memory — #1167), mapped to many contexts: main menu, loading, ship interior, station/hub, space flight,
  and per-biome planet beds (ice / desert / lava / toxic / ocean / verdant / crystal / cave) plus a
  day/night-tinted generic idle pool. Several tracks per context → **random pick**, and a long stay
  **re-rolls** at the loop seam for variety. See [MUSIC_TRACKS.md](MUSIC_TRACKS.md) for the full mapping + the
  Suno prompt of every track.

Combat is inferred client-side (hull+shield drop while in space → 14 s window) and always uses the
tense **synth** combat mood in both modes — the Suno library is intentionally all-calm. Music
muffles underwater (low-pass), and rides `MusicVolume` while SFX/ambience stay independent on
`SfxVolume`. The studio/title splash stings are left untouched (music is silent over the splash).

---

## Totals & rollout

The planned rollout has largely landed. Current state:

- **Procedural (free):** UI bank (hover/click/confirm/back), vitals/alerts (O₂ warning, hull alarm,
  hurt), toggles (lamp/teleport/scan/station-board), ship one-shot fallbacks, and ambience-loop
  fallbacks — all in `ProceduralAudio`; menu hook in `UiSound`. **Done.**
- **ElevenLabs (recorded, now blanket-approved):** the recorded banks are **generated and bundled**
  under `Resources/audio/` — mining ×4, place/loot/heal, drill loop + impact, gauss/laser/plasma +
  melee, 5 footstep surfaces, ship & space one-shots (engine, launch/land, hyperspace, weapon/hull/
  shield/destroyed, asteroid, doors), **6×5 creature banks**, **2×5 NPC banks**, and weather/biome
  beds (wind, rain/storm, 3 thunder, per-biome ambience, lava/water). **Done.**
- **Music:** both Synth and Suno **Tracks** modes ship (`ClientMusic`); see §11 + [MUSIC_TRACKS.md](MUSIC_TRACKS.md).

Rollout history (P1–P4), all shipped:
1. **P1 — core gameplay** (mining ×4, weapons, footsteps, place/loot/eat).
2. **P2 — ship & space** (engine, launch/land, weapon/hull/shield, doors).
3. **P3 — creatures** (6 banks × 5).
4. **P4 — NPCs + weather**.

> Open follow-ups: a few catalogued cues are not yet authored as dedicated assets (e.g. swim stroke,
> dedicated craft success/fail, separate eat/drink) — these currently rely on existing cues or
> procedural fallbacks. Extend with the same filename → clip pattern in `ClientAudio`.
