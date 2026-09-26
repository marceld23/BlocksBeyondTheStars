# The Crystal Net — a visible ON/OFF signal network for bases and stations

Epic #2045 (parts #2046–#2059), branch `feat/crystal-net`, 2026-09-27. Status: implemented (see
[../../TODO.md](../../TODO.md) for the live Done/Open status; the release ships protocol **v7**, so older game
versions cannot join a v7 server). The design decision is recorded in
[ADR 0013](adr/0013-crystal-net-binary-visible-signal-network.md).

## 1. Goal

Give players — kids of eight and their parents first — a way to make their base *do* things: a doorbell, a night
light, an airlock that locks while the alarm runs, a quarry that fills a crate, a beam pad that fires when someone
steps on it. The model is deliberately smaller than redstone: **crystal conduits carry one bit.** A network is ON
or OFF, nothing has a strength or a colour, and every wire is a visible block that glows while its signal is ON,
so a circuit can be read by looking at it.

## 2. Design principles

- **The server is the truth.** Every level, pulse, timer and machine job is computed on the server; the client
  draws what `CrystalNetList` / `CrystalDeviceList` tell it and sends nothing but `SetCrystalDeviceIntent`
  (toggle / press / configure), which is validated for reach, ownership and value range.
- **Binary and visible.** ON/OFF only; the conduit glows while ON; a door shows a red/green mode light; a
  device's own output (a switch's lever, a sensor's eye, a drill's "blocked" light) is on the wire as `Output`.
- **State never rides in the voxel.** A conduit is a plain block. A device's owner / mode / config is a
  cell-keyed row (`crystal_cell`, like beacons and beam pads), and the level of a network reaches the client as
  one list per change instead of a `BlockChanged` (and a chunk re-mesh) per conduit. The only voxel change the
  net makes is the lamp swap (`light_white` ↔ `light_white_off`).
- **Event-driven beats with a stated cost.** Nothing at all runs on a world without net cells. On a built base
  the cost is one pass over the cells every **100 ms** (logic beat), the world queries every **500 ms** (sensor
  beat, entity lists gathered once per beat into a flat presence list), and a rate limit of one world change per
  actuator per 500 ms. Hard caps (§8) keep a relay-tiled base from turning a beat into a world sweep — the power
  relay's 32-hop precedent.
- **No offline simulation.** The tick runs under `Guard` in the per-world loop, i.e. only on a resident (occupied)
  world. Timers and loops restart OFF on activation; "your base wakes up with you".
- **No power requirement** (for now — #1101 honoured): a conduit costs a crystal and glass, not energy.
- **Kid rules.** Clones are always wild and never hostile; an announcer reaches only the owner and their allies;
  labels pass the same content screen as beacon names.

## 3. Where it runs

Planets, moons, asteroids **and player-built stations** — a boarded station is an ordinary world with its own
`CrystalNetState`, and the lamp swap writes back into the station's cell store (`WriteBackStationCell`).
**Not on ships** (Marcel's decision; a ship's hull is an object, not player-edited world cells, so nothing ever
registers there). The **caller** and the **clone tank** are planet-only (`CrystalNetRules.IsPlanetOnly`):
creatures never tick on a void world, so on a station the block is placed as decoration and VEGA says so
(`vega.sys.crystal_planet_only`).

## 4. The network index (`GameServerCrystalNet.cs`)

Per world: `CrystalNetState` with `Cells` (cell → `ServerCrystalCell`) and `Nets` (id → `CrystalNetwork`).

- **A network is a connected component** of conduit + device cells under six-face adjacency
  (`CrystalNetRules.Faces`). Its `Level` is re-derived every logic beat: OR over the `Output` of its member
  devices, plus every gate whose output face points into it.
- **Join / merge.** `JoinCrystalNet`: a placed non-gate cell looks at its six neighbours; no net beside it opens a
  new network, one joins it, several **merge** into the one with the smallest id. A merge that would exceed
  `MaxCellsPerNet` (256) leaves the new cell **inert** instead.
- **Split.** `UnregisterCrystalCell` removes the cell and its row and re-floods the remaining cells
  (`SplitCrystalNet`): the first component keeps the id, every further component becomes a network of its own —
  a cut conduit splits a line in two.
- **Gates are members of no network** (`NetId = 0`). A logic or timer block reads the networks on its five
  input faces (`CrystalGateInputs`; a neighbouring gate is read by its `Output` directly; the network on the
  output face is never an input) and drives the network on its **output face** — the face the player looked at
  when placing it, stored as a quarter-turn `yaw` (`CrystalNetRules.OutputFace`). A gate's output is the
  result of the *previous* beat, so a loop through gates is well-defined (one beat of delay per gate).
- **Passive ports join when a conduit meets them.** Lamps (every `category: light` block and its `_off` twin),
  beacons, beam pads, sentry posts, thumpers, water spouts, energy gates and hydro trays are *devices*, but an
  old base's lamps must stay ordinary lamps: `OnCrystalBlockPlaced` registers such a block only when a net cell
  already touches it, and `DiscoverCrystalNeighbours` registers the ports around every fresh conduit / device
  (six block reads, loaded chunks only). When the last real net cell beside a port is mined, the port is
  unregistered again and its world state restored (`StopCrystalActuator`: a dark lamp lights up, a disabled
  sentry fires again, a closed spout pours again) — two lamps beside each other do not keep each other in the
  net (`HasCrystalNeighbour(excludePassive: true)`).
- **Doors attach through their neighbours.** A door is a server entity over air cells, not a block.
  `CrystalDoorBeat` looks at the gap's floor cell and the cell above it, six faces each: any net cell beside
  them → the door is **Locked** (OFF) or **HeldOpen** (ON); none → **Normal**. The mode goes out in
  `NetDoor.Mode` and `TickDoors` applies it (the hand toggle is refused while locked).
- **Registration** (`RegisterCrystalCell`): assigns a device id, creates a `TimerState` for a timer block,
  restores a switch's lever from `Mode` (1 = ON), marks level sinks as already applied in their world state
  (a lamp is lit, a sentry fires, a spout pours — so an OFF network is a change the first beat applies), checks
  the caps (§8), joins the net, persists the row and marks both wire lists dirty.

## 5. The beats (what runs when)

`TickCrystalNet(dt)` — under `Guard("TickCrystalNet")` in the per-world loop; returns at once when the world
has no cells. On the first tick after activation it respawns the clone tanks' clones (§7).

**Sensor beat (every 500 ms, `CrystalSensorBeat`)** — polls every sensor and every port that reads the world:

| Device | Reads |
|---|---|
| step plate | someone matching the filter stands on the cell (0.8-block footprint, −0.6…+1.6 blocks in height) |
| proximity sensor | someone matching the filter within radius `r` (config `r=0..2` → 4 / 6 / 8 blocks) |
| daylight sensor | `IsNightAt(cell)` compared with the mode (0 = ON by day, 1 = ON by night) |
| storage sensor | the crate beside it: full / empty / holds ≥ `n` of its first filter item (or config `item=`) |
| radio beacon | the owner within `BeaconOwnerRange` (12 blocks) |
| hydro tray | a crop stands on it (ripe) |

The presence list is gathered **once per beat** (`GatherCrystalPresence`: players on the ground, NPCs,
creatures as wild / tame / hostile, planet enemies and bandits as hostile), so twenty plates cost one pass over
the entities. A sensor never overwrites a running pulse.

**Logic beat (every 100 ms, `CrystalLogicBeat`)**, in this order:

1. Pulses fall back (`PulseUntil` reached → `Output = false`).
2. Network levels: OR over member outputs, plus the gates that drive into the network (previous beat's output).
3. Gates compute their next output: `LogicGate.Evaluate(mode, inputs)` (AND / OR / NOT / XOR; NOT with no input
   is ON — the inverter every airlock needs; AND with no input is OFF) or `TimerState.Step` (delay / clock /
   counter / toggle; config `period=` 0.5…10 s, `count=` 1…64; an unconnected timer runs — a free clock).
4. Actuators follow their network when the level differs from `Applied` and ≥ 500 ms passed since the last
   world change (`ActuatorMinIntervalSeconds`).
5. Doors (`CrystalDoorBeat`).
6. Machines held ON advance on their own beat (`CrystalMachineBeat`, §7).

Then the lists go out, coalesced once per tick: `CrystalNetList` when any level differs from what the clients
were last told, `CrystalDeviceList` when a device's output / mode / config changed, and the creature list when a
machine moved or spawned an animal.

**Watchers** are not polled: `LoadCrystalNet` subscribes once to `_world.BlockSet`, and
`OnCrystalWatchedCellChanged` pulses every watcher whose eye (`cell + OutputFace(yaw)`) is the changed cell.

## 6. Device catalogue

Kinds are `CrystalDeviceKind`; block keys map to kinds in `CrystalNetRules.KindByBlockKey` (lamps by category).
*Source* = contributes `Output` to its network; *gate* = between networks; *sink* = follows the level (level
sinks act on both edges, edge sinks on the rising edge only); *machine* = does a bounded job per rising edge
and keeps working on its own beat while held ON.

| Block key | Kind | Role | Input | Output | Mode / config |
|---|---|---|---|---|---|
| `crystal_conduit` | Conduit | wire | — | — | — |
| `crystal_switch` | Switch | source | Interact toggles | its lever (persisted as mode 1 = ON) | — |
| `crystal_button` | Button | source | Interact presses | 0.5 s pulse | — |
| `step_plate` | StepPlate | source (sensor) | who stands on it | level | mode 0–3: anyone / players / owner / creatures |
| `proximity_sensor` | ProximitySensor | source (sensor) | who is near | level | mode = `PresenceFilter` (7); config `r=0..2` |
| `daylight_sensor` | DaylightSensor | source (sensor) | local sun | level | mode 0 day / 1 night |
| `storage_sensor` | StorageSensor | source (sensor) | crate beside it | level | mode = `StorageSensorMode`; config `item=`, `n=` |
| `watcher` | Watcher | source (event) | the cell in front changes | pulse | config `yaw=` (eye) |
| `logic_block` | LogicBlock | gate | up to 5 faces | output face, one beat later | mode = `LogicMode`; config `yaw=` |
| `timer_block` | TimerBlock | gate | up to 5 faces | output face | mode = `TimerMode`; config `period=`, `count=`, `yaw=` |
| `alarm_siren` | AlarmSiren | level sink | level | — | mode 0–2: which siren loop |
| `chime` | Chime | edge sink | pulse | — | mode 0–3 |
| `horn` | Horn | edge sink | pulse | — | mode 0–2 |
| `melody_block` | MelodyBlock | edge sink | pulse | — | mode 0–7 note; config `inst=0..3` |
| `announcer` | Announcer | edge sink | pulse | — | mode 0–5 preset line; label = own line |
| `fabricator` | Fabricator | machine | pulse / held | ON = blocked | config `recipe=<key>` |
| `caller` | Caller | machine (planet-only) | pulse | — | — |
| `clone_tank` | CloneTank | machine (planet-only) | start / held | ON while growing; pulse when ready | mode 0 release automatically / 1 on signal; config `sp=`, `growing=`, `clones=` |
| `auto_drill_1/2/3` | AutoDrill | machine | held | ON = blocked / done | mode = `AutoDrillMode` (only ore / everything) |
| `matter_sender` | MatterSender | machine | pulse / held | ON = blocked | config `pair=<receiver device id>` |
| `matter_receiver` | MatterReceiver | source (port) | — | pulse on arrival | label = its name |
| every `category: light` block | Light | level sink | level | — | — (OFF swaps to `<key>_off`) |
| `radio_beacon` | Beacon | sink + source | level → alarm marker | ON while the owner is within 12 | — |
| `beam_block` | BeamPad | edge sink + source | pulse → beams whoever stands on it | pulse on arrival | config `pair=<beam id>` |
| `sentry_post` | Sentry | level sink + source | OFF = holds fire | ON while it has a target | — |
| `thumper` | Thumper | edge sink | pulse starts its run | — | — |
| `water_spout` | Spout | level sink | OFF stops pouring | — | — |
| `energy_gate` | EnergyGate | level sink | ON lets animals through | — | — |
| `hydro_tray` | HydroTray | edge sink + source | pulse harvests into the crate beside it | ON while ripe | — |

Rows: every kind except Conduit and Light carries a persisted row (`NeedsRow`). Menus: `IsConfigurable` lists the
kinds whose Interact opens a picker or a list; `ModeCount` tells the client how many grid entries to build.

## 7. Machines (`GameServerCrystalMachines.cs`, `GameServerCrystalCreatures.cs`)

One job per rising edge (or per manual start at the block, `SetCrystalDeviceIntent.Action = 1`, owner / ally
only); a held level keeps the machine on its own beat. Every job is bounded — one stack, one craft, one mined
block — so a base full of machines costs a handful of container operations per beat. A machine that cannot do
its job sets its `Output` ON ("blocked"), which a lamp or a chime on its network can show.

- **Matter link (#2054).** The sender's config `pair=` names a receiver by device id (the receiver is named at
  placement; the sender's menu lists the receivers its owner may use — own or allied). A shot takes the first
  stack of the crate beside the sender that the far crate's filter accepts, moves ≤ `MoveStackSize` (16) into
  the crate beside the receiver (`NpcDepositToContainer`), pulses the receiver's port and draws a `BeamFx` with
  two `beam_teleport` cues. Held ON: one shot every `MoveBeatSeconds` (2 s). Same world only, no conduit
  between the pair, free per shot (Marcel's decision) — the pace is the cap.
- **Fabricator (#2056).** Config `recipe=` picks one workshop (or hand) recipe without a market theme. The
  **owner must be present** and hold the recipe's blueprint, exactly like a hand craft. Inputs are taken
  all-or-nothing from every crate on its six faces; the output goes to the first crate whose filter takes it
  and that has room. Held ON: one craft per 2 s.
- **Auto-drill (#2055).** A stationary quarry (`CrystalNetRules.DrillTiers`): Mk1 5×5 / 8 layers / one block
  per 2 s / drill tier 1; Mk2 7×7 / 16 / 1 per s / tier 2; Mk3 9×9 / 32 / 2 per s / tier 3. It walks its volume
  layer by layer below itself (`Cursor`), never above its own level, scanning at most one layer per step. It
  skips air, fluids, unmineable cells and anything the tier cannot mine; in **only ore** mode everything that is
  not `category: ore` stays; it never touches ships, settlements, stations, the Guardian core, factories, other
  players' bases or any player-placed cell (`DrillCellProtected`), and never opens a cell with water or lava
  beside it. Drops go into the crate beside the drill (`NpcCrateHasRoom` dry run first — a full crate pauses the
  drill with `Output` ON, the cursor stays on that cell); a finished volume leaves `Output` ON until the drill is
  re-placed. World budget: `MaxDrillBlocksPerTick` (2) across every drill.
- **Caller (#2057).** A pulse marks the block active for `CallerHoldSeconds` (20 s) and snaps the owner's
  companions within `CallerRange` (24) to it. The creature tick (`TryCallerIntent`, run after the begging
  routine had nothing to say) sends every passive or skittish **land** animal within range — not companions,
  giants or hostiles — into an orbit around the nearest active caller, then lets it trot off; a startled animal
  forgets the call.
- **Clone tank (#2057).** The species list (`CloneableSpeciesFor`) is the world's roster filtered to non-hostile
  species the owner has **scanned** (`creature:<id>`) or **tamed on this world**. Start (signal or Interact) takes
  the price — one bait of the species' preference (`PreferredBait`) + 2 `matter_dust` — from the crate beside the
  tank, else from the pocket of whoever pressed start; then `growing=1`, `Output` ON, a bubbling loop, and
  `CloneGrowSeconds` (60 s) later the clone is released beside the tank as a **wild** animal tagged
  `CloneOf = "<x,y,z>"` (mode 1 waits for the level instead). The tank's config counts its `clones=`; clones are
  never persisted as entities — `RespawnCrystalClones` re-spawns them beside their tank on activation, and a
  mined tank clears the tag so they join the ordinary wild population. `MaxLivingClonesPerOwner` (6) is checked
  at start.
- **Existing ports** (#2053) reach their own code: a beam pad beams everyone standing on it to `pair=` (owner /
  ally check as with a hand beam) and pulses the far pad; a hydro tray harvests the crop into the adjacent
  crate and schedules the regrow; a thumper starts its run; the sentry's firing pass goes through
  `FireSentryLinked` (disabled → holds fire; its port reads "has a target"); a spout is skipped by
  `PourFromSpout` while closed and woken via `_activeFluid` when reopened; an open energy gate lets fauna pass.

## 8. Caps (`CrystalNetRules`)

| Cap | Value | Enforced where |
|---|---|---|
| cells per network | 256 | `JoinCrystalNet` — a merge over the cap leaves the new cell inert |
| networks per world | 64 | `OverCrystalCap` — only a brand-new network counts |
| sensors per world | 32 | `OverCrystalCap` (step plate, proximity, daylight, storage) |
| sound devices playing | 8 | `SetCrystalLoop` / `PlayCrystalSound` — no ninth siren |
| auto-drills / matter senders / fabricators per owner | 4 / 4 / 4 | `OverCrystalCap` |
| clone tanks per owner | 2 | `OverCrystalCap` |
| living clones per owner | 6 | `CloneTankStart` |
| mined blocks per tick (all drills) | 2 | `AutoDrillStep` |

A cell over a cap registers **inert**: it exists, it is listed, it does nothing, and the player is told
(`vega.sys.crystal_cap`); mining something frees the slot. The first conduit a player ever places triggers
`vega.sys.crystal_first`.

## 9. Intents and permissions

`HandleSetCrystalDevice` — reach is checked like every block action (`WithinReach`); the cell must hold a
device (not a conduit).

- **Action 0 — toggle** (switch only): flips `Output`, persists it as `Mode`, plays `crystal_switch`.
- **Action 1 — press**: a button pulses (`crystal_button`); on a fabricator, matter sender, clone tank,
  auto-drill, caller, thumper or hydro tray it is a **manual start** on the same path as a rising edge —
  owner, ally or admin only (`CanConfigureCrystal`).
- **Action 2 — configure** (owner / ally / admin): `Mode` clamped to `ModeCount`, `Config` sanitised
  (`SanitizeCrystalConfig`: printable, ≤ 96 chars, no `|`/newlines; the `yaw=` of a gate or watcher is
  re-applied, it is not the player's to overwrite), `Label` screened like a beacon name (#1221 — a refused
  label aborts the intent after telling the player), a timer resets, a switch takes its mode as its lever.

Rejections: `srv.crystal.gone`, `out_of_reach`, `srv.crystal.owner_only`, `srv.crystal.clone_cap`,
`srv.crystal.clone_price:<bait>`.

## 10. Persistence

Table `crystal_cell` (SQLite and PostgreSQL, `IWorldRepository.SaveCrystalCell` / `ListCrystalCells` /
`DeleteCrystalCell`):

| Column | Meaning |
|---|---|
| `planet` | the world's location id (a station world has its own) |
| `x`, `y`, `z` | the cell — the primary key together with `planet` |
| `kind` | the `CrystalDeviceKind` name (`"Conduit"`, `"Switch"`, …) |
| `owner` | the placing player's id (empty = anyone may configure) |
| `mode` | the picked mode; a switch's lever |
| `config` | `key=value;key=value` — `yaw=`, `period=`, `count=`, `r=`, `inst=`, `recipe=`, `pair=`, `sp=`, `growing=`, `clones=`, `item=`, `n=`; on load `key=` may name the block key of a multi-key kind |
| `label` | the player-typed name of a receiver / announcer line (screened) |

The voxel itself comes back through the normal block-edit store. `LoadCrystalNet` runs on every world
activation (after doors, beacons and beams), clears first (idempotent), re-registers every row without
persisting, re-floods the networks and resets both beats; runtime state (pulses, timers, loops, the drill's
cursor, an active caller) starts OFF — a switch keeps its lever. Conduits and lamps are rows too (a conduit's
row is what rebuilds the network without touching a chunk; a lamp's row exists only while a net cell touches it).

## 11. Wire (`Networking/Messages/CrystalNetMessages.cs`, protocol v7)

| Message | Direction | Content |
|---|---|---|
| `CrystalNetList` | server → world | every network: `Id`, `On`, `Cells` as x/y/z triples — the client draws the glow on exactly these cells |
| `CrystalDeviceList` | server → world | every non-conduit cell: `Id`, cell, `Kind` (enum name), `Mode`, `Config`, `Label`, `OwnerId`, `Output` |
| `SetCrystalDeviceIntent` | client → server | cell, `Action` 0/1/2, `Mode`, `Config`, `Label` |
| `SoundFx` | server → world | `SoundId`, position, `Pitch`, `Loop`, `Stop`, `SourceId` (device id, so a stop finds its loop) |
| `NetDoor.Mode` | server → world | 0 normal / 1 locked / 2 held open (the client shows a red / green frame light) |

Both lists are sent on join (`SendCrystalNet`) and coalesced once per tick on change. A loop is started once
and stopped with `Stop`; nothing is re-sent per beat. `Protocol.Version` 6 → 7: a v6 peer cannot decode the
lists, so older clients are refused (release note).

## 12. Client contract (#2049)

The client is presentation only. `GameBootstrap` keeps the two lists (`CrystalNets`, `CrystalDevices`);
`CrystalNetView` (`client/…/Scripts/CrystalNetView.cs`) builds **one mesh for the whole world** — a translucent
violet shell that pulses softly on every cell of an ON network — rebuilt only when a new list arrives, positioned
through `ScenePos` so the wrap seam is honoured, capped at 4096 cells. `DoorView` draws the red / green mode
light from `NetDoor.Mode`; `ClientAudio` plays `SoundFx` (the sixteen ElevenLabs clips; `alarm_siren*` loops are
tracked by `SourceId` so a stop finds them) and `ProceduralAudio` synthesises the melody notes
`note_<inst>_<n>` (instruments crystal / bell / bass / blip, C4…C5 — see [SOUND_DESIGN.md](SOUND_DESIGN.md) §12).
The HUD names what Interact does to the device under the crosshair (`Game.AimedCrystalDevice`,
`ui.crystal.prompt.toggle / press / menu / linked`, in the active device's glyph). `CrystalDeviceUi`
(`Scripts/CrystalDeviceUi.cs`) is the **menu** for `IsConfigurable` kinds: a mode grid with `ModeCount` buttons
(labels `ui.crystal.mode.<kind>.<n>`), setting rows that cycle on click (radius, period, count, instrument), a
list for the pairing kinds (matter receivers, beam pads, recipes, animals) and a *Start now* button for machines;
every click goes to the server at once, which validates, persists and echoes the device back. Modal like
`BeamPadUi`: pad-navigable, closes on Esc / pad **B** / the Close button, touch taps the buttons directly.
Everything rides the existing **Interact** action — no new binding (see
[INPUT_AND_CONTROLLER.md](INPUT_AND_CONTROLLER.md)). Step plates and sensors need no input at all.

## 13. Content and assets

- **Blueprints** (`data/blueprints.json`, category `Crystal` → tech tab "Crystal Net", `ui.tech.cat_crystal`):
  `crystal_conduit` (← `comm_radio`; unlocks conduit, switch, button, step plate) → `crystal_sensors`,
  `crystal_logic`, `crystal_sound`; `fabricator` (← `crystal_logic`); `caller` (← `crystal_conduit` +
  `creature_translator`); `clone_tank` (← `caller` + `matter_forge`); `auto_drill_1` (← `crystal_conduit` +
  `titanium_drill`) → `auto_drill_2` (← `diamond_drill`) → `auto_drill_3` (← `mining_beam`); `matter_sender`
  (← `beam_block` + `matter_forge`) → `matter_receiver`. Every device is blueprint-gated (Marcel's decision).
- **Recipes** (`data/recipes.json`): workshop, every one with crystal (a conduit is 1 crystal + 1 glass → 4).
- **Locales**: `block.<key>.name`, `item.<key>.name/.desc`, `blueprint.<key>.name/.desc`, the server lines
  `srv.crystal.*` (rejections, the six announcer presets) and `vega.sys.crystal_first/_cap/_planet_only` —
  EN + DE mandatory, the twelve community locales by the machine pass.
- **Art**: 23 block tiles (`tools/ai-assets/gen_textures.py`) + 23 inventory icons (`gen_item_icons.py`),
  OpenAI; the unlit lamp twins have no asset — the atlas darkens the lit tile. **Audio**: 16 ElevenLabs clips via
  `gen_sound.py` (`alarm_siren_0..2` loops, `chime_0..3`, `horn_0..2`, `auto_drill_loop`, `clone_tank_bubble`,
  `fabricator_craft`, `caller_whistle`, `crystal_switch`, `crystal_button`); melody notes are synthesised in
  code (`ProceduralAudio`). All logged in `NOTICES.md`.

## 14. How to add a device (checklist)

1. **`data/blocks.json`** — the block (a new lamp: `category: light` plus its `<key>_off` twin; a port that
   should be recognised by key: any category).
2. **`data/items.json`** — the block item. **`data/recipes.json`** — a workshop recipe with
   `requiredBlueprint`. **`data/blueprints.json`** — the blueprint in category `Crystal` with its prerequisites.
3. **Locales** — `block.<key>.name`, `item.<key>.name/.desc`, `blueprint.<key>.name/.desc` in `en.json` and
   `de.json` (never in a community locale by hand); any new picker labels / server lines the same way.
4. **Asset manifests** — a tile in `tools/ai-assets/gen_textures.py`, an icon in `gen_item_icons.py`
   (then `bundle_textures.py`), sounds in `gen_sound.py`; list them in `NOTICES.md`.
5. **Shared rules** (`CrystalNet.cs`) — a `CrystalDeviceKind` value, the entry in `CrystalNetRules.KindByBlockKey`,
   and the predicates it belongs to: `IsSensor` (polled), `IsGate`, `IsConfigurable`, `IsPlanetOnly`,
   `ModeCount` (picker size); `NeedsRow` is true for everything but conduits and lamps.
6. **Server** (`GameServerCrystalNet.cs`) — `KeyForKind` (the block key an old row places), the passive list in
   `OnCrystalBlockPlaced` / `IsPassiveKind` if it is a port that joins only beside a conduit, a per-owner cap
   in `OverCrystalCap` if it is a machine; then the handler: a sensor → the `switch` in `CrystalSensorBeat`; a
   sink → `ApplyCrystalActuator` (+ `StopCrystalActuator` for what it must undo when it leaves the net); a
   machine → `TriggerCrystalMachine` and, if it works while held, `CrystalMachineBeat`
   (`GameServerCrystalMachines.cs`).
7. **Client** — the mode grid's icons / labels for the new `ModeCount`, or a list source for a pairing kind;
   a visible `Output` state if the kind has one.
8. **Test** — a `[Fact]` in `tests/BlocksBeyondTheStars.Tests/CrystalNetTests.cs` (place with `Builder`, tick
   with `Ticks`, assert through the seams `CrystalLevelAt` / `CrystalDeviceOutput` / `CrystalNetSnapshots`).
9. **Docs** — the catalogue above, `docs/user/USER_MANUAL.md` (Crystal Net subsection), the Codex article
   `crystal-net` in `data/wiki/articles.json`, `TODO.md`.

## 15. Testing

`tests/BlocksBeyondTheStars.Tests/CrystalNetTests.cs` runs a real `GameServer` on a temporary SQLite world with
the starter ship off and a builder hovering in the air (empty cells within reach). Public seams:
`CrystalNetSnapshots` (id, level, cell count), `CrystalLevelAt(cell)`, `CrystalDeviceOutput(cell)`,
`CrystalCellCount`, `SetCrystalDeviceForTest`, `CrystalReceiversFor`, `CloneableSpeciesFor`, `CrystalDrillCursor`.

Facts: conduits merge into one network and split when cut; a switch drives its network and a lamp swaps to its
unlit twin; a button pulses for half a second; a step plate reads who stands on it and the owner filter ignores
others; a door beside a conduit locks when OFF and holds open when ON; a logic block in NOT mode inverts its
input one beat later; a timer block in toggle mode flips on every pulse; a daylight sensor follows the local
sun; a watcher pulses when the cell in front changes; an alarm siren starts a loop when ON and stops it when
OFF; devices come back from their rows after a reload; a matter link beams a stack between the crates of its
pair; an auto-drill mines only ore below itself into its crate; a clone tank grows a wild animal of a scanned
species; a caller marks itself active for the creature tick; `LogicGate` and `TimerState` are pure.

## 16. Known limits / follow-ups

- **One bit, one beat.** No signal strength, no colours, no wireless except the matter pair (deliberate). A
  gate adds 100 ms; a long chain of gates is visibly slow — that is the model, not a bug.
- **Lists, not deltas.** Both wire lists are sent whole on every change; at the 64 × 256 cap that is still a
  small message, but a base at the cap that flickers a clock will re-send its device list ten times a second.
  A delta message is the first optimisation if it ever shows.
- **Only the first crate** beside a sensor, sender, receiver, drill or tank counts (`AdjacentCrystalCrate`);
  the fabricator alone reads all six faces.
- **Storage "full"** is a line drawn for the sensor: eight full stacks in a wood crate, 32 stacks in a
  workshop crate.
- **No offline automation** by design; a drill does not fill a crate while the owner is on another world.
- **Melody block** plays one note per pulse; a tune needs a timer per note — a sequencer block is a follow-up
  if kids ask for it.
- **Playtest open**: a fresh world (doorbell, night light, airlock, quarry) and a player station (lamps and
  doors on a boarded station).
