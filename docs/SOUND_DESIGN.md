# Sound Design — what the game needs (M26)

A catalogue of every sound the game needs, where it triggers, how many distinct variants,
and **how it should be sourced**. Drives the M26 audio work.

> Status today: procedural SFX (mine/place/craft/reject/ship-hit/scan-blip) + a code-synth
> ambient **music** loop + a ship **ambience** hum are implemented (`ClientAudio`, `ClientMusic`).
> Everything below extends that. The server already broadcasts the events most sounds hook onto.

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

One file per run, cost shown before the call. **A single test sound is approved first**; each
**batch** needs separate manual approval. Procedural/MIDI need no gate.

---

## 1. UI / menu — *procedural*

| Sound | Trigger | # |
|---|---|---|
| hover, click, confirm, back/cancel, error, tab-switch, slider-tick | shell + in-game menus | ~7 |

Source: procedural (short tonal cues). Needs a small shell-level audio hook (menu is currently silent).

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

## 11. Music — ✅ SHIPPED (context cross-fade, 2026-06-12)

Context tracks, cross-faded over ~2.5 s: **in-game menu ✓, planet ✓, space ✓, combat ✓** — four
AI-generated ElevenLabs ambient loops (`Resources/audio/music_menu|planet|space|combat.mp3`, 24 s
seamless) with mood-matched code-synth fallbacks in `ClientMusic` (so the game stays musical
without assets). Combat is inferred client-side (hull+shield drop while in space → 14 s tension
window). Still open: a main-menu (AppShell-level) music hook — `ClientMusic` lives in the world rig.

---

## Totals & rollout

- **Procedural (free, now):** UI bank, vitals/alerts, station cues, toggles, docking, more music
  contexts — ~30 cues. No cost; implement in `ClientAudio`/`ClientMusic` + a shell audio hook.
- **ElevenLabs (paid, batched, gated):** ~**90–110** recorded SFX total across mining/weapons/
  footsteps/ship/creatures(~30)/NPCs(~10)/weather(~20). Generated in approved batches, one file per
  run, logged in `NOTICES.md`.
- **MIDI music (free):** 3–4 context tracks.

**Suggested order (each ElevenLabs batch separately approved):**
1. **P1 — core gameplay** (mining ×4, weapons, footsteps, place/loot/eat) ≈ 20 sounds.
2. **P2 — ship & space** (engine, launch/land, weapon/hull/shield, doors) ≈ 15.
3. **P3 — creatures** (6 banks × 5) ≈ 30.
4. **P4 — NPCs + weather** ≈ 30.
5. Procedural UI/vitals/menu-music in parallel (no gate); music expansion last.

> Recommendation: lock the **test sound** style first, then run **P1** as the first approved batch
> (~20 files) so the most-heard gameplay sounds get the recorded-quality upgrade with bounded cost.
