# The Crystal Net — a visible ON/OFF signal network for bases and stations

Epic #2045 (parts #2046–#2059), branch `feat/crystal-net`, 2026-09-27; review fixes epic #2091 (parts
#2092–#2099), branch `fix/crystal-net-review`, 2026-09-27 — devices only listen, the Device Eye, direction arrows,
status lights and the travelling glow. Status: implemented (see [../../TODO.md](../../TODO.md) for the live
Done/Open status; the release ships protocol **v7**, so older game versions cannot join a v7 server — the review
fixes add one field additively and stay on v7). The design decision is recorded in
[ADR 0013](adr/0013-crystal-net-binary-visible-signal-network.md) (with its #2091 amendment).

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
- **Binary and visible.** ON/OFF only; the conduit glows while ON, with bands travelling away from the sources
  that drive it; a door shows a red/green mode lamp above its doorway; gates, watchers and eyes wear a direction
  arrow; every other device wears an amber status light while its `Output` is ON.
- **Sources talk, everything else listens (#2092).** Only a *source* (`CrystalNetRules.IsSource`: switch,
  button, step plate, proximity / daylight / storage sensor, watcher) puts its `Output` on its own network, and a
  *gate* (logic block, timer block, Device Eye) drives the network on its drive face. Every other device's
  `Output` is a **status** (blocked, arrived, owner near, has a target, ripe, growing, …): it is drawn as the
  status light and read by a Device Eye, but it never drives the device's own network — a device's report must
  never read back as its own command.
- **State never rides in the voxel.** A conduit is a plain block. A device's owner / mode / config is a
  cell-keyed row (`crystal_cell`, like beacons and beam pads), and the level of a network reaches the client as
  one list per change instead of a `BlockChanged` (and a chunk re-mesh) per conduit. The only voxel change the
  net makes is the lamp swap (`light_white` ↔ `light_white_off`).
- **Event-driven beats with a stated cost.** Nothing at all runs on a world without net cells. On a built base
  the cost is one pass over the cells every **100 ms** (logic beat), the world queries every **500 ms** (sensor
  beat, entity lists gathered once per beat into a flat presence list), and a rate limit of one world change per
  level actuator per 500 ms (an edge device: one action per 200 ms). Hard caps (§8) keep a relay-tiled base
  from turning a beat into a world sweep — the power relay's 32-hop precedent.
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
  **sources** (`CrystalNetRules.IsSource`), plus every gate whose drive face points into it. A listening member's
  `Output` is a status and is skipped (#2092). `PrevLevel` keeps the level of the previous beat for the edge
  detector (§5).
- **Join / merge.** `JoinCrystalNet`: a placed non-gate cell looks at its six neighbours; no net beside it opens a
  new network, one joins it, several **merge** into the one with the smallest id. A merge that would exceed
  `MaxCellsPerNet` (256) leaves the new cell **inert** instead.
- **Split.** `UnregisterCrystalCell` removes the cell and its row and re-floods the remaining cells
  (`SplitCrystalNet`): the first component keeps the id, every further component becomes a network of its own —
  a cut conduit splits a line in two.
- **Direction.** A logic block, timer block, watcher and Device Eye store a quarter-turn `yaw` (config `yaw=`):
  the way the player was **looking** when placing it — from the intent's rotate key, else the player's facing.
  `CrystalNetRules.OutputFace(yaw)` is that horizontal direction (yaw 0 = +Z, 1 = +X, 2 = −Z, 3 = −X), i.e. the
  face pointing *away* from the player — not "the face the player looked at". `DriveFace(kind, yaw)` is the face a
  gate drives: `OutputFace` for the logic and timer block, the opposite (back) face for the Device Eye. The client
  draws an arrow on the `OutputFace` (§12).
- **Gates are members of no network** (`NetId = 0`, `CrystalNetRules.IsGate`: logic block, timer block, Device
  Eye). A logic or timer block reads the networks on its five other faces (`CrystalGateInputs`; the network on the
  drive face is never an input; a neighbouring gate is read by its `Output` directly, but only when that gate's
  drive face points at this one) and drives the network on its drive face. A gate's output is the result of the
  *previous* beat, so a loop through gates is well-defined (one beat of delay per gate). Gates never open a
  network of their own and do not count against the network cap.
- **The Device Eye (#2092)** is gate-like: it reads no network at all. `CrystalEyeReads` looks at the cell in
  front of it (`cell + OutputFace(yaw)`): a Crystal Net device there (not a conduit, not inert) → that device's
  `Output` — a source's level or a listener's status; else a door whose gap covers that cell (the floor cell or the
  one above) → whether the door is open; nothing → OFF. The result drives the network **behind** the eye
  (`DriveFace`). This is how a listener's report reaches a wire ("the eye tells the wire what the machine is
  doing"), and how a circuit reads a door's state. Being a gate, an eye beside a door does not lock it
  (`CrystalDoorBeat` only counts cells with a `NetId`), and it is not a sensor for the sensor cap.
- **Passive ports join when a conduit meets them.** Lamps (every `category: light` block and its `_off` twin),
  beacons, beam pads, sentry posts, thumpers, water spouts, energy gates and hydro trays are *devices* (all of them
  listeners — their own reports are statuses), but an old base's lamps must stay ordinary lamps:
  `OnCrystalBlockPlaced` registers such a block only when a net cell
  already touches it, and `DiscoverCrystalNeighbours` registers the ports around every fresh conduit / device
  (six block reads, loaded chunks only). When the last real net cell beside a port is mined, the port is
  unregistered again and its world state restored (`StopCrystalActuator`: a dark lamp lights up, a disabled
  sentry fires again, a closed spout pours again) — two lamps beside each other do not keep each other in the
  net (`HasCrystalNeighbour(excludePassive: true)`).
- **Doors attach through their neighbours.** A door is a server entity over air cells, not a block.
  `CrystalDoorBeat` looks at the gap's floor cell and the cell above it, six faces each: any net cell beside
  them → the door is **Locked** (OFF) or **HeldOpen** (ON); none → **Normal**. The mode goes out in
  `NetDoor.Mode` and `TickDoors` applies it (the hand toggle is refused while locked); the client shows it as a
  small lamp above the doorway (§12).
- **Registration** (`RegisterCrystalCell`): assigns a device id, creates a `TimerState` for a timer block,
  restores a switch's lever from `Mode` (1 = ON), marks the sentry and the spout as already applied in their
  world state (a sentry fires, a spout pours — so an OFF network is a change the first beat applies), checks the
  caps (§8), joins the net (gates excepted), persists the row and marks both wire lists dirty. A **lamp is not
  assumed lit** (#2096): it may have been saved as its dark twin, so its `Synced` flag stays false until the first
  beat has read the block that actually stands there (§5). `LastActuated` starts "long ago" (−1000 s), so a
  device's first action is never rate-limited.

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
| radio beacon | *status*: the owner within `BeaconOwnerRange` (12 blocks) |
| hydro tray | *status*: a crop stands on it (ripe) |

The beacon and the hydro tray are listeners: what the sensor beat reads for them is their status (the amber
light, a Device Eye), never a level on their own network (§6). The presence list is gathered **once per beat**
(`GatherCrystalPresence`: players on the ground, NPCs, creatures as wild / tame / hostile, planet enemies and
bandits as hostile), so twenty plates cost one pass over the entities. A sensor never overwrites a running pulse.

**Logic beat (every 100 ms, `CrystalLogicBeat`)**, in this order:

1. Pulses fall back (`PulseUntil` reached → `Output = false`).
2. Network levels: OR over the outputs of the member **sources** (`IsSource` — a listener's status is skipped,
   #2092), plus the gates that drive into the network (previous beat's output, through `DriveFace`).
3. Gates compute their next output: the Device Eye `CrystalEyeReads` (§4); a logic block
   `LogicGate.Evaluate(mode, inputs)` (AND / OR / NOT / XOR; NOT with no input is ON — the inverter every airlock
   needs; AND with no input is OFF); a timer block `TimerState.Step` (delay / clock / counter / toggle; config
   `period=` 0.5…10 s, `count=` 1…64; an unconnected timer runs — a free clock). The **delay is a delay line**
   (#2095): every input edge is queued with its time and released exactly `period` seconds later, rises *and*
   falls, so a 0.5 s button pulse comes out as the same 0.5 s pulse, later. The queue holds at most
   `TimerState.MaxDelayEdges` (128) edges — a 10 s delay fed by a 0.1 s clock fits; on overflow the oldest edge is
   released at once.
4. Listeners follow their network:
   - **Edge devices** (`CrystalNetRules.IsEdgeSink`: chime, horn, melody block, announcer, thumper, hydro tray,
     beam pad, fabricator, matter sender, clone tank, auto-drill, caller) act once when the **network** rises
     (`Level && !PrevLevel`), with a floor of 0.2 s between two actions of one device
     (`CrystalEdgeMinIntervalSeconds`, so a 0.1 s flicker cannot double-fire). The edge is the network's own —
     previous beat vs this beat — not the device's last-applied state, so a 0.5 s clock rings a chime every
     0.5 s.
   - **A lamp not yet synced** (`Synced == false`, #2096): `SwapCrystalLight(c, level)` reads the block that
     stands there, swaps it if needed and returns whether the block was readable; true → `Synced`,
     `Applied = level`; false (chunk not loaded, the cell is air) → try again next beat. A lamp saved dark
     therefore lights up after a reload when its network is ON.
   - **Level devices** (lamp, alarm siren, sentry, spout, energy gate, beacon alarm) follow when the level differs
     from `Applied` and ≥ 500 ms passed since their last world change (`ActuatorMinIntervalSeconds`).
5. Doors (`CrystalDoorBeat`) — derived from the networks beside them every beat.
6. Machines held ON advance on their own beat (`CrystalMachineBeat`, §7).
7. `PrevLevel = Level` on every network — the edge detector's memory.

Then the lists go out, coalesced once per tick: `CrystalNetList` when any level differs from what the clients
were last told, `CrystalDeviceList` when a device's output / mode / config changed, and the creature list when a
machine moved or spawned an animal.

**Watchers** are not polled: `LoadCrystalNet` subscribes once to `_world.BlockSet`, and
`OnCrystalWatchedCellChanged` pulses every watcher whose eye (`cell + OutputFace(yaw)`) is the changed cell.

## 6. Device catalogue

Kinds are `CrystalDeviceKind`; block keys map to kinds in `CrystalNetRules.KindByBlockKey` (lamps by category).
*Source* (`IsSource`) = its `Output` drives its own network; *gate* (`IsGate`) = between networks, drives the
network on its drive face; everything else is a **listener**: *level* listeners follow the level (both edges,
0.5 s limit), *edge* listeners (`IsEdgeSink`) act on the rising edge of their network (0.2 s floor); a *machine*
does a bounded job per rising edge and keeps working on its own beat while held ON. A listener's `Output` is a
**status**: it is drawn as the device's amber light and read by a Device Eye placed in front of it — it never
drives its own network (#2092).

| Block key | Kind | Role | Input | Output (sources, gates) | Status (read by a Device Eye) | Mode / config |
|---|---|---|---|---|---|---|
| `crystal_conduit` | Conduit | wire | — | — | — | — |
| `crystal_switch` | Switch | source | Interact toggles | its lever (persisted as mode 1 = ON) | — | — |
| `crystal_button` | Button | source | Interact presses | 0.5 s pulse | — | — |
| `step_plate` | StepPlate | source (sensor) | who stands on it | level | — | mode 0–3: anyone / players / owner / creatures |
| `proximity_sensor` | ProximitySensor | source (sensor) | who is near | level | — | mode = `PresenceFilter` (7); config `r=0..2` |
| `daylight_sensor` | DaylightSensor | source (sensor) | local sun | level | — | mode 0 day / 1 night |
| `storage_sensor` | StorageSensor | source (sensor) | crate beside it | level | — | mode = `StorageSensorMode`; config `item=`, `n=` |
| `watcher` | Watcher | source (event) | the cell it looks at (`OutputFace`) changes | pulse | — | config `yaw=` (look direction) |
| `logic_block` | LogicBlock | gate | up to 5 faces | drive face = `OutputFace`, one beat later | — | mode = `LogicMode`; config `yaw=` |
| `timer_block` | TimerBlock | gate | up to 5 faces | drive face = `OutputFace` | — | mode = `TimerMode`; config `period=`, `count=`, `yaw=` |
| `device_eye` | DeviceEye | gate | the device (or door) it looks at (`OutputFace`) | drive face = its back | — | config `yaw=` (look direction) |
| `alarm_siren` | AlarmSiren | level listener | level | — | — | mode 0–2: which siren loop |
| `chime` | Chime | edge listener | rising edge | — | — | mode 0–3 |
| `horn` | Horn | edge listener | rising edge | — | — | mode 0–2 |
| `melody_block` | MelodyBlock | edge listener | rising edge | — | — | mode 0–7 note; config `inst=0..3` |
| `announcer` | Announcer | edge listener | rising edge | — | — | mode 0–5 preset line; label = own line |
| `fabricator` | Fabricator | machine (edge) | rising edge / held | — | ON = blocked | config `recipe=<key>` |
| `caller` | Caller | machine (edge, planet-only) | rising edge | — | — | — |
| `clone_tank` | CloneTank | machine (edge, planet-only) | start / held | — | ON while growing, then a 0.5 s "ready" pulse | mode 0 release automatically / 1 on signal; config `sp=`, `growing=`, `clones=`, and with samples `x=` (cross partner), `grow=` (#2207) |
| `auto_drill_1/2/3` | AutoDrill | machine (edge) | rising edge / held | — | ON = crate full / pit done | mode = `AutoDrillMode` (only ore / everything) |
| `drill_laser` | DrillLaser | machine (edge) | rising edge / held | — | ON = halted (water / lava / bedrock / protected / crate full / 128 deep) | mode = `AutoDrillMode`; config `depth=<blocks cut>` (#2108) |
| `matter_sender` | MatterSender | machine (edge) | rising edge / held | — | ON = blocked (cannot send) | config `pair=<receiver device id>` |
| `matter_receiver` | MatterReceiver | listener (port) | — | — | 0.5 s pulse on arrival | label = its name |
| every `category: light` block | Light | level listener | level | — | — | — (OFF swaps to `<key>_off`) |
| `radio_beacon` | Beacon | level listener | level → alarm marker | — | ON while the owner is within 12 | — |
| `beam_block` | BeamPad | edge listener | rising edge → beams whoever stands on it | — | 0.5 s pulse when someone arrives | config `pair=<beam id>` |
| `sentry_post` | Sentry | level listener | OFF = holds fire | — | ON while it has a target | — |
| `thumper` | Thumper | edge listener | rising edge starts its run | — | — | — |
| `water_spout` | Spout | level listener | OFF stops pouring | — | — | — |
| `energy_gate` | EnergyGate | level listener | ON lets animals through | — | — | — |
| `hydro_tray` | HydroTray | edge listener | rising edge harvests into the crate beside it | — | ON while something is ripe | — |
| `rail_stop` | RailStop | edge listener | rising edge departs the train halted here | — | — | — (#2113: a stop within 4 blocks of a rail line; the autopilot halts at it for 8 s — see `MONORAIL.md`) |

Rows: every kind except Conduit and Light carries a persisted row (`NeedsRow`). Menus: `IsConfigurable` lists the
kinds whose Interact opens a picker or a list (not the Device Eye: it has nothing to set); `ModeCount` tells the
client how many grid entries to build.

What the old "one port per device" model got wrong (the reason for #2092): a wired beacon alarmed itself when
its owner came near, a wired hydro tray harvested itself the moment it ripened, two wired and paired beam pads
threw a player back and forth, a clone tank on "release on signal" released at once (its own "growing" status was
the signal), and a blocked machine latched its own control line ON. With statuses kept off the network, none of
these can happen by construction.

## 7. Machines (`GameServerCrystalMachines.cs`, `GameServerCrystalCreatures.cs`)

One job per rising edge (or per manual start at the block, `SetCrystalDeviceIntent.Action = 1`, owner / ally
only); a held level keeps the machine on its own beat. Every job is bounded — one stack, one craft, one mined
block — so a base full of machines costs a handful of container operations per beat. A machine that cannot do
its job sets its `Output` ON ("blocked", `SetCrystalBlocked`). That is a **status** (#2092): the client draws it
as the amber light, and a Device Eye in front of the machine puts it on a wire — it never drives the machine's own
network (before #2092 a blocked machine latched its own control line ON).

- **Matter link (#2054).** The sender's config `pair=` names a receiver by device id (the receiver is named at
  placement; the sender's menu lists the receivers its owner may use — own or allied). A shot takes the first
  stack of the crate beside the sender that the far crate's filter accepts, moves ≤ `MoveStackSize` (16) into
  the crate beside the receiver (`NpcDepositToContainer`), pulses the receiver's status ("something arrived",
  0.5 s) and draws a `BeamFx` with two `beam_teleport` cues. Held ON: one shot every `MoveBeatSeconds` (2 s).
  Same world only, no conduit between the pair, free per shot (Marcel's decision) — the pace is the cap. The
  sender's status is ON while it cannot send (no receiver, no crate at either end, nothing the far filter takes,
  no room over there).
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
  drill with its status ON, the cursor stays on that cell); a finished volume leaves the status ON until the drill
  is re-placed. World budget: `MaxDrillBlocksPerTick` (2) across every drill.
- **Drill laser (#2108, Justus' idea, Marcel's shape).** A 1×1 shaft straight down from the device's own column
  (`DrillLaserStep`): one block every `DrillLaserBeat` (0.5 s) up to `DrillLaserDepth` (128), cutting what a
  tier-3 drill cuts. Three things set it apart from the auto-drill: it **loads the chunk it aims at**
  (`_world.GetBlock`, never `GetBlockIfLoaded` — a shaft runs far below the rows streamed around a player, and an
  unloaded cell must never read as air and be skipped); it **banks oil** (`BlockDefinition.Liquid`, #2106) as well
  as ore, and in **only ore** mode the rock is vaporised rather than left standing (a shaft has to go down); and it
  **stops for good** instead of skipping — at water or lava in the shaft or beside it, at bedrock or anything its
  tier cannot cut, at a protected cell, at a full crate (resumes from the same cell once emptied and started again)
  and at its maximum depth — with the status ON (a Device Eye reads it). The depth reached is persisted in the
  cell's config (`depth=`), so a reload resumes. Every cut broadcasts `WorldFx { Kind = "laser", Radius = depth }`:
  the client draws the beam from the device down to the cell and plays `drill_laser_zap`. Shares the world budget
  with the auto-drills; `MaxDrillLasersPerOwner` (2).
- **Caller (#2057).** A pulse marks the block active for `CallerHoldSeconds` (20 s) and snaps the owner's
  companions within `CallerRange` (24) to it. The creature tick (`TryCallerIntent`, run after the begging
  routine had nothing to say) sends every passive or skittish **land** animal within range — not companions,
  giants or hostiles — into an orbit around the nearest active caller, then lets it trot off; a startled animal
  forgets the call.
- **Clone tank (#2057, #2097).** The species list (`CloneableSpeciesFor`) is the world's roster filtered to
  non-hostile species the owner has **scanned on this world** or **tamed on this world**. Species ids repeat
  across planets and the scan ledger `Scanned` (`creature:<id>`) is global, so a scan counts only through the
  persisted per-world set `PlayerState.ScannedCreatureSites` (`"<locationId>:<speciesId>"`, recorded on every
  creature scan in `GameServerScanning`); a save older than that set falls back to the first-scan site in
  `ScannedWhere` (`BodyId == LocationId`). Tamed: `TamedSpecies` `"<locationId>:<speciesId>"`. The device list
  carries exactly this list to the client as `NetCrystalDevice.Choices` (`"speciesId|coined name"`, clone tanks
  only), so the menu offers what the server will accept. Start (signal or Interact) takes the price — one bait
  of the species' preference (`PreferredBait`) + 2 `matter_dust` — from the crate beside the tank, else from the
  pocket of whoever pressed start (refusal: `srv.crystal.clone_price.<bait>`, one line per bait); then
  `growing=1`, status ON, a bubbling loop, and `CloneGrowSeconds` (60 s) later the clone is released beside the
  tank as a **wild** animal tagged `CloneOf = "<x,y,z>"`, the status falls and a 0.5 s "ready" pulse follows.
  Mode 1 ("release on signal") waits for its network to be ON — since the growing status no longer drives that
  network, it really waits for a signal. The tank's config counts its `clones=`; clones are never persisted as
  entities — `RespawnCrystalClones` re-spawns them beside their tank on activation, and a mined tank clears the
  tag so they join the ordinary wild population. `MaxLivingClonesPerOwner` (6) is checked at start.
  **Samples and crosses (#2207–#2209).** With the bio lab the list also offers the animal and plant samples in the
  owner's sample case (`AppendSampleChoices`, choice `g:<seed hex>`); such a start goes through `BioTankStart`
  instead: it costs one sample per parent and matter dust (2 for a clone, 4 for a cross) and no bait. A species
  from another world is registered as a guest species from the save's species register, so the clone works on any
  world (a water or lava animal needs its fluid within 8 blocks). A second choice in the config (`x=`) names a
  cross partner (blueprint `bio_crossing`); `grow=` holds what the tank is growing. `sp`, `x`, `grow`, `growing`
  and `clones` are server-owned — a client `configure` cannot overwrite them. `MaxLivingClonesPerWorld` (16) is
  checked for every start. See [BIO_LAB.md](BIO_LAB.md) §11.
  **Fix round (#2214).** The tank's config keys are `sp=`, `x=`, `growing=`, `grow=`, `clones=`, `cl=`;
  `growing`, `clones`, `grow` and `cl` are server-owned (`TankOwnedKeys`) and are stripped from a client
  configure before the 96-character cut. `grow=` holds what the tank was started on for both paths (a native
  id or `g:<seed hex>`), and the finish releases from it, so changing `sp` while the tank grows changes
  nothing. `cl=` lists one entry per clone, comma-separated — the living ones, then the waiting ones;
  `clones=` mirrors the count, and an older row (`clones=N;sp=X`) is read as N clones of X. At load every
  listed clone waits (`ServerCrystalCell.CloneWaiting`) until the world's first beat; `RespawnCrystalClones`
  brings each one back as its own species and drops an entry that can never be an animal again. A native id
  is read from this world's own species table — the tables are per world since #2226, see
  `LoadedWorld.SpeciesById`. Waiting clones count against both caps. The list
  follows the living clones on release, on defeat and on taming (`ForgetClone`), and the sensor beat
  (`WatchCloneTank`) reconciles every other removal; the same beat compares `CloneChoiceStamp(owner)` and
  re-sends the device list when the owner's samples, animal scans or tames changed. A row that says
  `growing=1` at load starts its wait over; an inert tank refuses a start with `vega.sys.crystal_cap`; the
  bait path is free in a free game mode; a finished cross waits while the owner's sample case has no room.
  `LoadCrystalNet` registers tanks that are growing or have clones before all other rows, so the tank over
  the cap is not one that is in use.
- **In a ship: decoration (#2219).** A Crystal Net block is a cell of the world grid. Built into a ship —
  landed ship, ship interior in space, hull on a spacewalk, keel site, commissioned self-built ship — every kind
  with `CrystalNetRules.NeedsRow` (all devices and the port blocks: radio beacon, beam pad, sentry post,
  thumper, water spout, energy gate, hydro tray) and the bio lab is accepted like any block and does nothing
  there. `NoteShipDecor` (GameServerSpaceStructure.cs) tells the player once, after the payment:
  `ShipAiHintOnce(session, "ship_decor")` (milestone `vega:hint:ship_decor`, line `vega.hint.ship_decor`).
  Conduits and lamps send nothing. On a station spacewalk `NeedsWorldPlaceHandler` still refuses
  every kind the world place handler has to register (`srv.station.block_needs_deck`); lamps, the sentry
  post, the energy gate and the hydro tray are read from the grid and stay allowed. A new kind is refused
  there by default (`ShipFunctionBlockTests.TheLists_CoverEveryCrystalNetBlockOfTheContent`).
- **Existing ports** (#2053) reach their own code: a beam pad beams everyone standing on it to `pair=` (owner /
  ally check as with a hand beam) and pulses the far pad's status ("someone arrived"); a hydro tray harvests the
  crop into the adjacent crate and schedules the regrow; a thumper starts its run; the sentry's firing pass goes
  through `FireSentryLinked` (disabled → holds fire; its status reads "has a target"); a spout is skipped by
  `PourFromSpout` while closed and woken via `_activeFluid` when reopened; an open energy gate lets fauna pass.
- **Remote beam cooldown (#2092).** `BeamStandingPlayers` skips a player whose normal beam cooldown
  (`_beamCooldown`, `BeamCooldownSeconds` = 6 s) is still running and sets it after the jump, so a player who
  just arrived stays put — no ping-pong between pads, whatever the wiring. A remote beam still costs no suit
  energy.

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
| living clones per world | 16 | `CloneTankStart` / `BioTankStart` |
| mined blocks per tick (all drills) | 2 | `AutoDrillStep` |

Gates (logic block, timer block, Device Eye) never open a network and do not count against the network cap;
the Device Eye is not a sensor (it reads a device's status, no world query) and does not count against the
sensor cap.

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
`srv.crystal.clone_price.<bait>` (one line per bait: `.forage_bait` / `.meat_bait` / `.nectar_lure`, #2096 — no
raw item key in the text). The announcer's own line goes out as `@srv.crystal.announce_custom:<label>` and the
locale value is `{name}`: the client substitutes the argument into `{name}` only, never `{0}` — a
`srv.crystal.*` line with `{0}` would show the placeholder literally (guarded by a test).

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
| `CrystalDeviceList` | server → world | every non-conduit cell: `Id`, cell, `Kind` (enum name), `Mode`, `Config` (incl. `yaw=` for gates and watchers), `Label`, `OwnerId`, `Output` (a source's / gate's level, a listener's status), `Choices` (clone tanks only: the owner's cloneable species as `"speciesId|coined name"`, #2097) |
| `SetCrystalDeviceIntent` | client → server | cell, `Action` 0/1/2, `Mode`, `Config`, `Label` |
| `SoundFx` | server → world | `SoundId`, position, `Pitch`, `Loop`, `Stop`, `SourceId` (device id, so a stop finds its loop) |
| `NetDoor.Mode` | server → world | 0 normal / 1 locked / 2 held open (the client shows a small red / green lamp above the doorway) |

Both lists are sent on join (`SendCrystalNet`) and coalesced once per tick on change. A loop is started once
and stopped with `Stop`; nothing is re-sent per beat. `Protocol.Version` 6 → 7: a v6 peer cannot decode the
lists, so older clients are refused (release note). The review fixes (#2091) stay on v7: `Choices` is an
additive field with an empty default, so no bump.

## 12. Client contract (#2049)

The client is presentation only. `GameBootstrap` keeps the two lists (`CrystalNets`, `CrystalDevices`);
`CrystalNetView` (`client/…/Scripts/CrystalNetView.cs`, #2049 / #2093 / #2094) builds **two meshes for the whole
world**, rebuilt only when a new list arrives and every 3 s (`RebuildSeconds`, so the wrap seam follows the
player), positioned through `ScenePos`:

- **The glow** — a translucent violet shell on every cell of an ON network (capped at 4096 cells). Bright bands
  **travel along the conduit away from its active sources** (#2094): the seeds are the member cells of sources
  (`IsSource`) whose `Output` is ON and the cell every gate with an ON output drives (`cell + DriveFace`); a
  breadth-first walk over the network's cells (six-face adjacency) gives every cell its distance from the nearest
  seed. Per frame only the **vertex colours** are rewritten (eight shared vertices per cell): alpha =
  `0.16 + 0.62 · max(0, cos(k·d − k·v·t))⁴` with `k = 2π / 6` (bands 6 cells apart) and `v = 5` cells per second.
  A network with no seed the client can see falls back to the even breathing pulse. Vertex colours need the
  Always-Included `BlocksBeyondTheStars/ParticleAlpha` shader (`Unlit/Transparent` as fallback).
- **The device marks** (#2093) — a small **arrow** (a pyramid on the `OutputFace(yaw)` face, `yaw=` read from the
  device's config) on every logic block, timer block, watcher and Device Eye: bright cyan while the block's own
  `Output` is ON, dim otherwise; and a small **amber status light** (a box on top of the block) on every other
  device except lamps while its `Output` is ON — a flipped switch, a sensor that sees something, a blocked sender,
  a full drill, a growing tank. A watcher wears both.

`DoorView` shows `NetDoor.Mode` as a **small lamp above the doorway** (#2098): red = locked, green = held open, no
lamp = normal — a separate little object with two shared unlit materials, so the door's own renderers keep their
materials (the former property-block tint of the whole door broke SRP batching and turned untextured door parts
white when the mode returned to normal). `ClientAudio` plays `SoundFx` (the sixteen ElevenLabs clips; `alarm_siren*` loops are
tracked by `SourceId` so a stop finds them) and `ProceduralAudio` synthesises the melody notes
`note_<inst>_<n>` (instruments crystal / bell / bass / blip, C4…C5 — see [SOUND_DESIGN.md](SOUND_DESIGN.md) §12).
The HUD names what Interact does to the device under the crosshair (`Game.AimedCrystalDevice`,
`ui.crystal.prompt.toggle / press / menu / linked`, in the active device's glyph). `CrystalDeviceUi`
(`Scripts/CrystalDeviceUi.cs`) is the **menu** for `IsConfigurable` kinds: a mode grid with `ModeCount` buttons
(labels `ui.crystal.mode.<kind>.<n>`), setting rows that cycle on click (radius, period 0.5 / 1 / 2 / 3 / 5 / 10 s,
count, instrument), a list for the pairing kinds (matter receivers, beam pads, recipes, animals — the clone tank's
animals are exactly the device's `Choices`, #2097, so the menu never offers a species the server would refuse)
and a *Start now* button for machines; every click goes to the server at once, which validates, persists and
echoes the device back. Modal like `BeamPadUi`: pad-navigable, closes on Esc / pad **B** / the Close button, touch
taps the buttons directly. Everything rides the existing **Interact** action — no new binding (see
[INPUT_AND_CONTROLLER.md](INPUT_AND_CONTROLLER.md)). Step plates, sensors and the Device Eye need no input at all
(the eye has no menu; the HUD names it "linked").

## 13. Content and assets

- **Blueprints** (`data/blueprints.json`, category `Crystal` → tech tab "Crystal Net", `ui.tech.cat_crystal`):
  `crystal_conduit` (← `comm_radio`; unlocks conduit, switch, button, step plate) → `crystal_sensors` (the three
  sensors, the watcher and the Device Eye), `crystal_logic`, `crystal_sound`; `fabricator` (← `crystal_logic`); `caller` (← `crystal_conduit` +
  `creature_translator`); `clone_tank` (← `caller` + `matter_forge`); `auto_drill_1` (← `crystal_conduit` +
  `titanium_drill`) → `auto_drill_2` (← `diamond_drill`) → `auto_drill_3` (← `mining_beam`); `matter_sender`
  (← `beam_block` + `matter_forge`) → `matter_receiver`. Every device is blueprint-gated (Marcel's decision).
- **Recipes** (`data/recipes.json`): workshop, every one with crystal (a conduit is 1 crystal + 1 glass → 4; the
  Device Eye is 1 crystal + 1 glass + 1 circuit board, blueprint `crystal_sensors`).
- **Locales**: `block.<key>.name`, `item.<key>.name/.desc`, `blueprint.<key>.name/.desc`, the server lines
  `srv.crystal.*` (rejections incl. `clone_price.<bait>`, the six announcer presets, `announce_custom` = `{name}`)
  and `vega.sys.crystal_first/_cap/_planet_only` — EN + DE mandatory, the twelve community locales by the
  machine pass.
- **Art**: 24 block tiles (`tools/ai-assets/gen_textures.py`) + 24 inventory icons (`gen_item_icons.py`), OpenAI —
  the 23 of #2058 plus `device_eye` / `item_device_eye` (#2092); the unlit lamp twins have no asset — the atlas
  darkens the lit tile. The arrows, status lights and door lamps are procedural client geometry, no assets. **Audio**: 16 ElevenLabs clips via
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
   and the predicates it belongs to. **Decide first whether the kind is a source or a listener.** A *source*
   (`IsSource`) is a real input whose `Output` drives its own network — a switch, a plate, a sensor. Anything
   that *does* something on a signal is a *listener*: either an **edge** listener (`IsEdgeSink` — acts once per
   rising edge of its network: a ring, a start, a harvest, a beam) or a **level** listener (in neither list —
   follows the level with the 0.5 s actuator limit: a lamp, a siren, a gate for animals). A listener's own report
   is a *status* and must never be made a source — that is exactly the self-triggering #2092 removed; a player
   reads it with a Device Eye. Then: `IsSensor` (polled; counts toward the sensor cap), `IsGate` (sits between
   networks — also give it its drive face in `DriveFace`), `IsConfigurable`, `IsPlanetOnly`, `ModeCount`
   (picker size); `NeedsRow` is true for everything but conduits and lamps.
6. **Server** (`GameServerCrystalNet.cs`) — `KeyForKind` (the block key an old row places), the passive list in
   `OnCrystalBlockPlaced` / `IsPassiveKind` if it is a port that joins only beside a conduit, a per-owner cap
   in `OverCrystalCap` if it is a machine; then the handler: a sensor → the `switch` in `CrystalSensorBeat`; a
   listener → `ApplyCrystalActuator` (an edge listener is called with `on = true` once per rising edge; a level
   listener with every level change) + `StopCrystalActuator` for what it must undo when it leaves the net; a
   machine → `TriggerCrystalMachine` and, if it works while held, `CrystalMachineBeat`
   (`GameServerCrystalMachines.cs`); a status → set `Output` through `SetCrystalBlocked`, `CrystalPortLevel` or
   `CrystalPortPulse`, so the status light and a Device Eye can read it.
7. **Client** — the mode grid's icons / labels for the new `ModeCount`, or a list source for a pairing kind. The
   amber status light (any non-gate, non-lamp kind with `Output` ON) and the travelling glow's seeds (`IsSource`,
   gates' `DriveFace`) follow from the shared predicates; a new *directional* kind also needs its arrow in
   `CrystalNetView.BuildDeviceOverlay` (today: gates and the watcher).
8. **Test** — a `[Fact]` in `tests/BlocksBeyondTheStars.Tests/CrystalNetTests.cs` (place with `Builder`, tick
   with `Ticks`, assert through the seams `CrystalLevelAt` / `CrystalDeviceOutput` / `CrystalNetSnapshots`). For
   a listener with a status, assert both halves: the status shows in `CrystalDeviceOutput`, its own network stays
   OFF, and a Device Eye in front of it drives the network behind the eye.
9. **Docs** — the catalogue above, `docs/user/USER_MANUAL.md` (Crystal Net subsection), the Codex article
   `crystal-net` in `data/wiki/articles.json`, `TODO.md`.

## 15. Testing

`tests/BlocksBeyondTheStars.Tests/CrystalNetTests.cs` runs a real `GameServer` on a temporary SQLite world with
the starter ship off and a builder hovering in the air (empty cells within reach). Public seams:
`CrystalNetSnapshots` (id, level, cell count), `CrystalLevelAt(cell)`, `CrystalDeviceOutput(cell)`,
`CrystalCellCount`, `SetCrystalDeviceForTest`, `CrystalReceiversFor`, `CloneableSpeciesFor`, `CrystalDrillCursor`.

Facts (24): conduits merge into one network and split when cut; a switch drives its network and a lamp swaps to
its unlit twin; a button pulses for half a second; a step plate reads who stands on it and the owner filter
ignores others; a door beside a conduit locks when OFF and holds open when ON; a logic block in NOT mode inverts
its input one beat later; a timer block in toggle mode flips on every pulse; a daylight sensor follows the local
sun; a watcher pulses when the cell in front changes; an alarm siren starts a loop when ON and stops it when
OFF; devices come back from their rows after a reload; a matter link beams a stack between the crates of its
pair; an auto-drill mines only ore below itself into its crate; a clone tank grows a wild animal of a scanned
species; a caller marks itself active for the creature tick; `LogicGate` and `TimerState` are pure. The review
fixes (#2091) added: a wired beacon reports its owner but does not drive its own network; a Device Eye reports a
blocked matter sender into the network behind it (and joins no network); wired, paired beam blocks do not throw
an arriving player back; a 0.5 s clock rings a chime every 0.5 s; a delay passes a button pulse one period later
(and ends it again); a lamp saved dark lights up after a reload when its network is ON; a clone tank offers only
species scanned on this world; the `srv.crystal.*` lines use `{name}`, never `{0}`, and every bait has its
price line.

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
- **Melody block** plays one note per pulse; several melody blocks on one network sound together as a chord. A
  tune is a delay per note (#2095): a clock into melody block A, the same clock through a 0.5 s delay into B,
  through a 1 s delay into C, … — each melody block on a network of its own. A sequencer block is a follow-up if
  kids ask for it.
- **A delay holds at most 128 edges** (`TimerState.MaxDelayEdges`); a faster-than-that input releases its oldest
  edge early. Like every timer, a delay line starts empty on world activation.
- **One Device Eye reads one device** (the cell in front of it); a base that wants to hear three machines needs
  three eyes. An eye reads a door only through the door's gap cells (the floor cell or the one above).
- **The travelling glow is presentation only.** The client re-derives the band seeds from the device list; a
  network whose driver it cannot see (none listed) breathes evenly instead. Per frame it rewrites eight vertex
  colours per glowing cell (≤ 4096 cells).
- **Playtest open**: a fresh world (doorbell, night light, airlock, quarry) and a player station (lamps and
  doors on a boarded station); for #2091: a wired beacon, hydro tray, paired beam pads and clone tank no longer
  trigger themselves; a Device Eye on a blocked sender and on a door; the arrows, status lights and the travelling
  glow; a delay tune; the door lamp.
