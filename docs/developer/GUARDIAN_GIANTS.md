# Guardian giants — how they work

> **Status: implemented (server), #3676 / #3681 / #3683.** Justus' idea: huge, fallen, block-built machines lie in the
> wilderness; rarely one wakes up when a player comes near, stands up and is a boss with a boss bar. This document covers
> the server runtime; the structure stamp that lays them in new worlds and the client (animation, boss bar, effects) have
> their own sections.

## The pieces

| What | Where |
|---|---|
| Numbers, moves and their timings, ranges, damage, POI / effect names, VEGA moments | `src/BlocksBeyondTheStars.Shared/Definitions/GuardianGiantRules.cs` |
| The body (standing cells per limb, the lying pose, facing, lights) | `src/BlocksBeyondTheStars.Shared/Definitions/GuardianGiantBody.cs` |
| The record of one giant | `src/BlocksBeyondTheStars.Shared/State/GuardianGiantRecord.cs`, `WorldMetadata.GuardianGiants` (keyed `SiteId` = `<body>#<index>`) |
| The status message | `src/BlocksBeyondTheStars.Networking/Messages/GuardianGiantMessages.cs` (`GuardianGiantStatus`, NetCodec 348) |
| The runtime | `src/BlocksBeyondTheStars.GameServer/GameServerGuardianGiants*.cs` (tick + found/stir/wake + map + VEGA; `…Fight` the AI and the streams; `…Attacks` the blows and the damage hook; `…Defeat` the corpse and the loot; `…Body` the block body; `…Admin` the admin tools and the test seams) |
| Hull and base loot | `data/enemies.json` → `guardian_giant` (also the Codex's "Enemies" page) |
| Achievement | `data/achievements.json` → `giant_slayer` (counter `defeat:guardian_giant`) |

## The record and its states

The structure stamp writes one `GuardianGiantRecord` per lying giant together with its blocks: the anchor `X/Y/Z` (the
root's cell in the lying pose; the lowest lying row is `GuardianGiantBody.LyingBottom` below it), `Facing` (quarter turns),
`Seed` (which plates rusted — the same body on the server and every client) and `CanWake` (about one in four). The blocks
themselves are ordinary block edits — mineable, unprotected. The runtime keeps the rest:

| State | Meaning |
|---|---|
| `dormant` | lying, lights out. Dead for good unless `CanWake`. |
| `lit` | it woke once and lay down again: lights on, healed; it rises again at the wake range (no stir, no delay). |
| `awake` | standing — the lying body's blocks are gone and the boss is a planet enemy. A world loaded in this state (the server stopped mid-fight) stamps the body back, lit, at its resting place and sets `lit`. |
| `defeated` | it fell for good: `CorpseX/Y/Z/Facing` is where it lies (lights out), `DefeatedAt` when; it never wakes again. |

`Found` turns true the first time any player comes within `FoundRange` (48) of it — whatever its state, wakeable or not —
and from then on its map marker shows. `Rewarded` holds every player who got a loot crate from it.

## The tick

`TickGuardianGiants` is a per-world step (after `TickHunterSites`), modelled on the hunter shuttle camps:

- **The scan** walks the active body's records once a second — four times a second while a player on foot is within the
  stir range (+8) of a giant that can wake (the flicker needs it). A body without a giant costs one dictionary walk a
  second. The scan restores an `awake` record that has no live fight, marks giants found, and runs the stir and the wake.
- **The fight** of an awake giant runs at the full tick rate. At most one giant per body is awake at a time.
- The runtime (`GuardianGiantWorld`, per body) is never persisted; a reloaded world starts it over (the first tick
  scans), and the records carry everything that must survive.

## The gate

A giant stirs and wakes only where planet machines are active: `PlanetEnemiesActive` (Survival, the `PlanetEnemies` rule
not Off). In Peaceful or Creative they only lie there (they can still be found). The Remnant era (after the story finale)
does **not** stop them — they were cut off from the Guardian core — and none of the Remnant era's machine limits apply. If
the rule switches off mid-fight, the giant walks home and lies down.

## Stir, wake, rise

- **Stir** — a player on foot within `StirRange` (40, flat distance to the lying body's centre) of a `dormant` giant that
  can wake: one visor light (picked by the seed) flickers — real block swaps `light_red_off` ↔ `light_red`, broadcast as
  `BlockChanged`, lit in about a third of the quarter-second slots, so a few swaps a second at most; it goes dark again
  when nobody is near. VEGA warns once per approach (the approach ends 8 blocks past the stir range).
- **Wake** — a player on foot within `WakeRange` (20), or a player breaking one of its body blocks (`BreakBlockCore` →
  `GuardianGiantOnBlockBroken`), where the gate allows: every light of a `dormant` body switches on, and one second later
  it rises; a `lit` giant rises at once. VEGA speaks to everyone within `StatusRange` (96).
- **Rise** — every lying cell that still holds the body's own block (a light either way) turns to air; a block a player put
  there stays. The boss is a planet enemy (`CombatEntityKind.GuardianGiant`, wire kind `GuardianGiant`) built from
  `data/enemies.json` (hull 600; `DefaultHull` without the entry), standing at the **root**: the anchor's X/Z as a point
  (the cell corner between the feet), on the highest floor of the four columns around it; `Yaw` = facing × 90°. The record
  turns `awake`, the move is `Rise` (3 s), the map marker turns "awake".

## The fight

Every move is a `GuardianGiantMove` on the snapshot clock (`SnapshotTime`, the clock of the planet-enemy lists). A timed move
runs to its end; a looping one (`Idle`, `Walk`, `Search`) is re-decided every tick:

1. **Home** — nobody it may hurt on foot within `FightRange` (64) for 8 s (or the gate is off): it walks straight back to its
   resting place (unwatched or stuck for 4 s it is set down there), turns the way it lay and plays `LieDown` (3 s). Then the
   lying body is stamped **lit** at the record's anchor (only into air and plants), the entity is removed, the record is
   `lit` — healed: it rises with a full hull next time.
2. **Rage** — once, at `RageAt` (half hull): `Enrage` (1.5 s), the faster walk and attack pause from then on.
3. **The target** — the nearest player on foot within `FightRange` it may hurt (alive, not waiting for a respawn choice,
   not one hostiles ignore — cloaked, god mode, Sandbox), seen from the visor (`HasLineOfSight`). Only hidden ones →
   `Search`. A companion's ward does not hold it (it is no network machine).
4. **An attack** when its pause is over (`AttackPause`, rage `RageAttackPause`, counted from the attack's end):
   `Summon` when due (from the rage on, at most `MaxSummons`, 25 s apart, only while none of its robots is left);
   `Stomp` within `StompRadius`; `JumpAttack` beyond 12 and within `JumpRange` when the landing (the target's spot) is dry
   ground within the leash; `Laser` and `Throw` in turn up to the shorter of their ranges.
5. **The walk** toward the target at `WalkSpeed` / `RageWalkSpeed`, never further than `LeashRange` (40) from its resting
   place. The ground is probed per column (no-load reads): up to `StepHeight` it walks, higher or lower up to `HopHeight`
   it hops (`Hop`, 3 blocks), beyond that, into deep water, onto lava or onto unloaded ground it stops.

**Every blow lands exactly at `Spec(move).Impact`** and is announced by `Spec(move).Warning`; the aim point is fixed when the
move starts and travels in the status message:

| Move | Aim | What lands |
|---|---|---|
| `Stomp` | its root | a shock wave there: `WaveDamage(StompDamage, StompRadius, flat distance)`, nobody more than 3 blocks above or below; `WorldFx` `guardian_giant_wave` (radius) — the client pushes its own player (`KnockSpeed`, `KnockLift`) |
| `JumpAttack` | the landing spot | it lands there (the arc runs from `JumpTakeOff`), the same wave with `JumpRadius` / `JumpDamage` |
| `Laser` | where the beam from the visor toward the target's chest meets the first solid block, else its full reach | `LaserDamage` to every player within `LaserWidth` of the beam (up to `LaserRange`) with nothing solid between the visor and them |
| `Throw` | the target's feet | the rock splashes there: `WaveDamage(ThrowDamage, ThrowRadius, distance)` for everyone in its sight; `WorldFx` `guardian_giant_rock` |
| `Summon` | — | two Guardian robots (the ordinary walking machines, `NewPlanetMachine`) 5–7 blocks to its left and right |
| `Hop` | the landing | it lands there — no damage |

The damage takes the machines' path (`HurtPlayer`: armour resistance, god mode, the HUD's attacker marker, the recovery;
death line `srv.death.giant`). **No attack ever changes a block.**

**Taking damage.** The giant is a normal planet-enemy target (`AttackEntityIntent`; the aim point is the nearest point of a
vertical capsule about its root, radius 2, a block above the root to a block below the crest — measured along the
attacker's aim ray), and sentries and the hunter robot shoot it. Every damage path calls `OnGuardianGiantDamaged` after
taking hull off: no push, no flinch on the server, no loot, no removal — the attacker within its fight range becomes a
fighter, and at zero hull it collapses. It does not burn, the shock gloves and the resonance blaster do not push it, the
machines' AI, damage aura and far-despawn skip it, and it takes no machine's slot in the spawn cap.

## The end

- **Collapse** (3 s): it stops being a target (`Hostile` false), its pose settles on the ground where it stands — the corpse
  anchor is the root rounded to a cell corner, the facing its heading rounded to a quarter turn, the anchor one row over
  the floor (the back sunk one row into the ground, like the structure stamp's) — and the status carries that anchor.
- When it is over: the body is stamped there, **lights out**, only into air and plants; the entity goes
  (`PlanetEnemyDefeated`); the record is `defeated` with the corpse's pose and `DefeatedAt`; a last status with `Move =
  None`; VEGA cheers to everyone near. The corpse's blocks are ordinary blocks.
- **Fighters** are the players who **hit it within its fight range or were hit by it** during the fight. Each fighter still
  in the game gets, once (`Rewarded`):
  - an **owner-locked crate** (#3294's `PlaceOwnerCrate`) on a ring 9–15 blocks from the corpse's centre (no spot: the
    backpack; full: the ground), holding the `guardian_giant` loot of `data/enemies.json` (carbon, guardian plate ±2 by a
    roll per giant and player — 12 to 16), the giant's core (`guardian_giant_core`, when the content defines it), and — for
    a fighter who knows every guardian blueprint — one finished guardian piece (`guardian_helmet` / `_chest` / `_legs` /
    `_sword` / `_drill`, seeded);
  - otherwise **one guardian blueprint they do not know yet** (`guardian_gear`, `guardian_sword` or `guardian_drill`,
    seeded) — learned at once (there are no blueprint items), with a `srv.giant.blueprint` message;
  - the achievement counter `defeat:guardian_giant` ("Giant Slayer"), the defeat counter, `defeat:any` (`giant`) and the
    story event `GameEventKeys.GuardianGiantDefeated` (argument: the site id).

## The status message

`GuardianGiantStatus` goes to every player of the surface within `StatusRange` (96) of the giant: on every move change, twice a
second while it is awake, and once with `Move = None` (and an empty `EntityId`) when it lies down or its collapse is over.
It carries `SiteId`, `EntityId`, `Move`, `MoveElapsed` and `ServerTime` (the snapshot clock), the aim point, `Rage`,
`Hull`/`HullMax`, and the lying pose: the record's anchor/facing for `Rise` and `LieDown`, the corpse's for `Collapse`, and
`Seed`. Position, heading (`Yaw`) and velocity come with the planet-enemy list like every machine's.

## The map

A found giant is a planet-map POI of type `guardian_giant` (name `poi.guardian_giant`) at the lying body's centre (the
corpse's, or the awake giant's position), with `NetPoi.State` = `lying`, `awake` or `defeated`. Whether it can wake is
never revealed.

## VEGA

Every giant line goes through `SendGuardianGiantVega(session, GuardianGiantVegaMoment)`, which picks its key in ONE seam,
`GuardianGiantVegaKey` — the story chooses its own variants there; the neutral lines (which never name the Guardian) are
the fallback: `vega.giant.found` (the first giant a player finds, once per player — milestone `guardian_giant:found`),
`.stir` (once per approach), `.rise`, `.rage`, `.victory` and `.lie_down` (to everyone near).

## Admin

- `/giant guardian` (the `summon_giant` verb, a cheat): lays a giant that **can** wake about 30 blocks in front of the admin
  (26–38 when that ground is no good) on fairly flat (two rows), dry ground outside every base zone and village — its feet
  toward the admin — stamped whole, with a `dormant` record under an index from 1000 (never colliding with the structure
  stamp's). Aliases: `guardian_giant`, `wächter`, `gigant`, ….
- `/tp giant [n]`: every giant of the current body by index, named with its state ("Guardian giant 2 · awake"); the admin
  lands on a standing spot 12–16 blocks from it — past the lying body's (or the corpse's) feet, in front of an awake one —
  never on a body cell. No giant here: `srv.tp.no_giant`.

## Tests

`GuardianGiantServerTests` (the gate in Peaceful and Creative, the Remnant era still waking, the stir's flicker and its
warning per approach, the wake's lights and the rise removing only its own blocks, waking by mining, the leash lying down
lit and healed, the restore of an awake record, the defeat — corpse, record, crates per fighter with a missing blueprint or
a finished piece, achievement, story event, never waking again — the stomp's falloff and no block changed, the laser
stopped by a wall, the status message, the found marker) and `GuardianGiantAdminTests` (`/giant guardian`, `/tp giant`).
Seams: `LayGuardianGiantForTest`, `TickGuardianGiantsForTest`, `StartGuardianGiantMoveForTest`,
`DamageGuardianGiantForTest`, `ForgetGuardianGiantRuntimeForTest`, ….

## Client

The client is presentation only: it never decides a move, a hit or the block body — it draws what the server's
`GuardianGiantStatus` and the giant's planet-enemy entry (`NetCombatEntity`, kind `GuardianGiant`) say, on the server's
clock.

**Where it lives.**

| Piece | Files |
|---|---|
| Unity-free logic (tested headless) | `src/BlocksBeyondTheStars.Client.Core/GuardianGiants/` — `GuardianGiantFight` (the status tracks, move clocks, boss bar, music switch), `GuardianGiantMotion` + `GuardianGiantFrame` (the motion set), `GuardianGiantPose` (the lying transform, arcs, shake falloff, the waves' push, footfalls) |
| The model | `client/…/Scripts/GuardianGiantModel.cs` (block-limb meshes, bones, hit boxes, light overlays, the rock, the Codex portrait) |
| The view | `GuardianGiantView.cs` (lifetime, following the entity, the hit boxes), `.Pose.cs` (root + bones, look-at, flinch), `.Fx.cs` (warnings, impacts, rock, beam, footfalls, sounds), `.Lights.cs` (blink, flare, visor, chest light, search light) |
| Routing | `WorldEntities.GuardianGiants.cs` (one view per live fight; the waves' push; the eye flicker sound) — the generic enemy loop in `WorldEntities.cs` skips kind `GuardianGiant` (no floating health bar) |
| HUD, map, music, Codex | `HudUi.BossBar.cs`, `WorldMap.GuardianGiants.cs`, the `giant_fight` context in `ClientMusic.cs`, the `guardian_giant` case in `EnemyPortraitRig.cs` |
| Tests | `tests/BlocksBeyondTheStars.Client.Tests/GuardianGiantPoseTests.cs`, `GuardianGiantMotionTests.cs`, `GuardianGiantFightTests.cs` |

**The fight state.** `GameBootstrap.GuardianGiantFight` takes every `GuardianGiantStatus`, stamped with `WorldTime` as it
arrives (the world clock — a paused singleplayer world freezes the moves too). A move's clock is `MoveElapsed` counted on
locally; a later status only moves it when it disagrees by more than 0.35 s, and the same move starting again (two stomps
in a row) counts as a new move. A move change cross-fades the poses over 0.2 s. A fight not heard of for 3 s is over for
this client.

**The model.** One mesh per `GuardianGiantPart` from `GuardianGiantBody.Standing(seed, lit)`, meshed with the block atlas
and `ChunkMesher` in one 16³ chunk (body cell + (8, 1, 8)), its light blocks flooding their colour onto their neighbours —
six draw calls plus four small light overlays. Each limb hangs from a bone at `GuardianGiantBody.Joint` (head and arms on
the torso; torso and legs on the root). It is re-meshed only when the status's seed or the lit state changes (lit from the
rise until the collapse lands).

**Lying and standing.** The lying pose is the root at the anchor cell's minimum corner, turned
`Quaternion.Euler(0, facing·90, 0) * Quaternion.Euler(-90, 0, 0)` — `GuardianGiantPoseTests` proves that this puts every
standing cell exactly onto the cell `GuardianGiantBody.Lying` stamps, for every facing. The rise starts exactly there (sit
up on the arms, then push up about the heels); a collapse (kneel, fall backwards, land at the impact) and a lie-down end
exactly there, at the status's anchor (the corpse's for a collapse). After them the model holds the lying pose until the
client world shows the stamped body (one torso plate's cell is solid, plus 0.12 s for the chunk to re-mesh; at most
1.5 s), then goes — no gap, no double body. Standing, the root follows the entity through the same buffered interpolation
as every planet machine, its heading eased toward `NetCombatEntity.Yaw`. The client takes the entity's position as the
root (the ground point between the feet) and expects the rise's heading to be `facing·90` (it stands up about its heels,
facing the way the body lay); before the entry appears it stands on the ground under the anchor.

**The motion set** (`GuardianGiantMotion`; every warning is on screen for exactly the move's `Warning`):

| Move | What the client shows |
|---|---|
| Rise / LieDown | sit up on the arms → push up about the heels (lie-down is the reverse); `giant_wake` + a red light flash at the start, `giant_rise` at 0.6 s, dust under the feet at the end |
| Idle | breathing, arm sway, the head scanning |
| Walk | a heavy biped walk on `CreatureGait` with cadence = speed ÷ stride × duty, so the planted sole moves backwards at exactly the body's speed; the root sinks by what the straightest planted (rigid) leg cannot reach; dust + `giant_step` per footfall |
| Hop | crouch, a jump 2.5 blocks over the server's climb, landing dust |
| Stomp | the right foot high and forward with a red ring of `StompRadius` around the feet (warning), the slam at the impact |
| JumpAttack | a deep crouch with a red landing ring of `JumpRadius` at the aim (warning), the leap from `JumpTakeOff` along an arc to the aim, the landing at the impact |
| Laser | the head turns to the aim, the visor glows up to white-hot (warning, `giant_laser_charge`), the beam from the visor for `LaserBeamSeconds` from the impact (`giant_laser_beam`) |
| Throw | it bends down, tears a rock out (a block-textured 2×2×2 effect cube, never a world block) at 0.3 s, winds up behind its head, hurls it at `ThrowRelease` along an arc to the aim (`giant_throw`); a red ring of `ThrowRadius` at the aim until it lands (`giant_rock_impact`) |
| Summon | arms up, the lights flare, a red pulse at the impact (`giant_summon` at the start) |
| Search | the head sweeps with a search light (a widening beam and a lit spot where it meets the ground) |
| Enrage | shudder, arms out and up, the lights flare, a red pulse (`giant_wake`, pitched up) |
| Collapse | `giant_collapse`, kneel, fall backwards (gathering speed), land at the impact (dust along the body, a deep `giant_stomp`, the lights go out for good) |

A hull drop is the client-only **flinch**: the limb nearest the local player's shot (else the torso) flashes and jerks for
0.25 s without breaking the move; the crosshair's hit marker fires for the player's own hit.

**Impacts** (stomp, leap landing, rock, collapse): `giant_stomp` / `giant_rock_impact`, dust, a shock ring out to the
wave's reach, ground cracks (`FxDecals.Crater`, High preset) and a camera shake of `GuardianGiantPose.Shake` —
`ShakeStrength` at the centre, nothing at twice the radius — through the player's camera shake (the screen-shake and
camera-motion settings apply); none at all under reduced motion. They run on the move's clock, at its `Impact`.

**The waves' push.** The server's `guardian_giant_wave` (`GuardianGiantRules.WaveFx`: a stomp, a leap's landing) pushes the
local player on foot away from its centre: `GuardianGiantPose.WavePush` — `KnockSpeed` across the ground and `KnockLift`
up, both falling off like the damage (`WaveDamage`), nothing beyond the radius or more than `GuardianGiantRules.WaveHeight`
(the server's own rule) above or below. `guardian_giant_rock` needs nothing more: the rock's impact is drawn on the move's
clock.

**Lights.** The light blocks glow on their own; per-limb overlays blink on top — a slow soft pulse while calm, fast and
brighter in the rage phase, a steady glow under reduced motion or reduced flashes — and flare with the summons and the
rage. The chest core lights the ground red (`FxLights`).

**Hit detection.** Each limb has a box collider on the giants' layer (20), registered with `CreatureView.RegisterGiant`
under the entity id (body plan `GiantRideRules.GuardianGiantPlan`): the aim ray hits the body, the melee/auto-aim sweep
measures from the body's nearest point, the player's capsule bumps into it, and a player on its back is carried and
shaken off after 4 s (`GiantRideRules.ShakesOff`). The boxes are switched off while the giant is no target (no entry yet,
collapsing). The planet-enemy sphere at the feet (`Scale`) is skipped for a giant whose boxes are registered.

**The stir.** A server block swap between `light_red` and `light_red_off` with `BlockChanged.Moved` set, within 60 blocks,
plays `giant_flicker` (at most twice a second). Every body write of the server is a moved change, so the rise and the
stamp play no mining crumble or place pop either.

**The Codex** shows the giant's damage as the range of its blows ("15–22 (per blow)", `EnemyCodexText.GiantBlows` from
`GuardianGiantRules`) — it has no damage per second by design.

**Boss bar** (`HudUi.BossBar.cs`): the name (`enemy.guardian_giant.name`) and the hull, top centre under the toast lane
(one constant, `BossBarY`), a mark at `RageAt`; deeper and pulsing red in the rage phase. Shown while
`GuardianGiantFight.BossBar` names a fight — a live status (not none, heard within 3 s) with hull left; the most recently
heard one when two giants fight.

**Music.** While `GuardianGiantFight.Fighting` (a live fight with hull, not collapsing or lying down) and the player is on
foot on the planet, `ClientMusic` switches to the `giant_fight` context: `music_boss_gauntlet` loops (the combat synth mood
if the file is missing), neither ducked nor muffled by the hostile-nearby rule; the normal music returns when the fight is
decided.

**Map and Codex.** A `guardian_giant` POI is the danger mark tinted by `NetPoi.State` — muted red lying, Guardian red
awake, grey defeated — with its own legend entry (`ui.map.kind_guardian_giant`). The Codex portrait is the same model,
standing, lit, seed 0.

## Story

The giants belong to "The VEGA Protocol" (#3685; canon **C18** in [LORE_STRUCTURE.md](LORE_STRUCTURE.md): the Service's own
construction and terraforming giants, taken over by the Guardian core in the cull, fallen where they stood when VEGA
severed the network — a few still wake to its last order, also after the finale). Code: `GameServerGuardianGiantStory.cs`.

- **Score and memory.** The story listens to `guardian_giant:defeated` (while a world story runs): the giant counts as
  `GuardianGiantStoryKills` = 5 machine kills — once per giant (`guardian_giant:<site>` in `StoryState.MilestoneKeys`), inside
  the pack's kill cap like every kill — and every fighter's next personal memory unlocks, once per giant
  (`story:giant_memory:<site>`).
- **The scanner.** Any block of a lying or fallen giant reads as the giant (kind `guardian_giant`, `ui.scan.guardian_giant`
  / `_defeated`; a relic's knowledge and a Codex discovery once per giant, ledger `guardian_giant:<site>`); a scan of kind
  `guardian_giant` names the awake one when the player stands within its fight range (`ui.scan.guardian_giant_awake`). Every
  giant scan speaks VEGA's memory of the builders — the insight `guardian_giant` (after B5, once; kept in VEGA's notes).
- **The mission** *The Fallen Giant* (chapter 4, after *The Long Quiet*): `guardian_giant:found` when a player comes within
  the found range (once per approach, measured at 1 Hz only while a step listens), `world:guardian_giants`,
  `world:guardian_giant_near`; the beat card `giant.remembers`. Without giants its stand-in *Echo of the Builders* ends on
  the same memory (insight `mission:m_giant_echo`). Details: [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md).
- **The memory core** (`guardian_giant_core`, one per fighter's crate): while a player carries one, VEGA reads its field
  record `giant_core_order` (lore site `guardian_giant_core`: the builder's work order, overwritten by Protocol G) into the
  reader and the Journal — once per player, only with a story whose lore holds it.
- **VEGA's lines** follow what the player's OWN story heard (`StoryGuardianGiantVegaKey(session, moment, neutralKey)`):
  `story.vega.giant.<found|stir|rise|victory>.<puzzled|service|guardian|silent>` in the VEGA pack's locale — puzzled before
  B2, a Service builder from B2, the Guardian's hand from B5, "the core is silent, but this one never heard" after the
  finale; `rage`, `lie_down`, worlds without the VEGA pack (or its built-in stand-in) keep the neutral `vega.giant.*` line.
  The Guardian is never named before the player's own story heard of it.

Tests: `VegaGuardianGiantStoryTests` (the mission and its stand-ins, the score and the memory, the core, the lines, the
predicates and the found event).
