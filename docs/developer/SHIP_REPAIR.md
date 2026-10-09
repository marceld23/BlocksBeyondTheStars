# Own-ship repair (hull + EVA-carved cells) — how it works

Status: implemented (see [../../TODO.md](../../TODO.md) for live Done/Open status). Last updated 2026-10-09.

## Overview

The player's own ship had no repair path: combat dented only the numeric hull (which never regenerates and
was only free-restored on destruction), and EVA-carved hull cells were only refillable by placing each block
by hand. Ship repair gives one coherent action that restores **both** layers — the numeric hull stat and the
missing structural voxels — against the ship's design reference, paying a material cost. It generalizes the
existing wreck-repair mechanic (*restore toward a design reference, paying a manifest*) onto your own ship.

## How it works

**The unifying idea.** Ship integrity is the diff between a design reference and the current state, in two
layers; repair pays to close that diff:
- **Structure (voxels):** the reference is the ship's design — a pristine `BuildShipStructureFrom(persistEdits:false)`
  build whose cells equal the live structure's `Baseline`. Any baseline cell that is now air is a missing cell —
  except (#2121) a cell the **owner** took out on purpose (an own-ship spacewalk mine persists its air edit with
  `OwnerRemovedMark` in the shape field → `SpaceStructure.OwnerRemoved`) and a doorway the owner hung a door in
  (`PlacedDoorAxes`, #2119): those are the ship's new design, so the manifest skips them. A wreck's carve (and the
  test carve) writes a plain air edit and stays damage. A guided per-cell repair of an owner-removed cell still works
  and clears the mark.
- **Hull (stat):** the reference is `_shipHullMax`; the deficit is the diff. (Modules / `StationCells` are
  protected and never appear in the manifest.)

**Material model** (`GameServerShipRepair.cs`). Hull is bought with **`iron_plate`** at **10 hull/plate**.
Each missing cell costs its placing item if one exists, else `iron_plate` — so lights/engine blocks (which
have no plain placing item) never block a repair. Materials are charged through the player inventory + ship
cargo, like crafting and wreck repair. Free-build parity holds (`!CraftingCostsMaterials` / `InstantBuild`).

**The two operations:**
- `RepairShipAll` is **greedy/partial**: hull first (the common combat case), then cells as far as the
  materials stretch — never a hard block when short.
- `RepairShipCell` does one **guided cell** (field/EVA): fill a specific missing baseline cell with its
  correct design block.

Cell fills persist via `SetStructureBlock` (per-cell deltas, survive restart) and broadcast
`StructureBlockChanged` so the landed/interior ship re-meshes. Hull persists in the ship snapshot.

**No passive hull regen** of any kind (not in combat, not docked). Hull rises only by spending materials.
The shield stays the only thing that recharges out of combat. Ship destruction keeps today's **free full
restore** (`DisableShip`, §8.5 unchanged) as the soft-lock floor, so a broke player is never hard-locked;
repair's value is *avoiding* the destruction event (keeping cargo/position/run, skipping recovery downtime).

**Surface.** The repair point is the ship's existing **cockpit** `StationCell` (no new station/medbay). At
the cockpit the server pushes a `ShipRepairStatus`; HudUi shows a "Repair ship" panel (hull bar + material
needs + a button → `RepairShipIntent{all}`). Field/EVA per-cell repair uses `RepairShipIntent{Mode="cell"}`.

**Where the materials come from (2026-10-09).** The panel names the bench behind each material
(`HudUi.RepairMaterialBench`: the station of the item's shallowest non-factory, non-market recipe — *Iron plate ×10 ·
Workbench*; one shared bench is named once, otherwise per item). Reason: a playtest concluded "the repair needs a
factory". The crafting list titles its cards by output item, so `iron_plate` (workbench) and its bulk twin
`factory_iron_plate` (factory terminal) were two identical "Eisenplatte" cards, and the factory one sorted first
whenever the player lacked ingots (the within-tier key prefers shallower inputs, and raw ore is shallower than an
ingot); its detail then said "Station: Fabrikterminal". The list now labels factory recipes "· Factory terminal" (card
and detail heading) and sorts them behind their bench twin while no terminal offering them is in reach
(`CraftingTechShipUI.ReachTier` tier 2 via `IsFactoryRecipeAway`). Bench proximity itself stays out of the order (#826).

**How the trigger is explained (2026-10-09, second round).** The client gates R on `Game.NearbyStation` being the
`cockpit` or `console` station cell (looked at within reach, else within 3 blocks) while the panel is up
(`PlayerController.CanRepairOwnShip`); the server itself accepts the intent wherever the own ship structure exists. The
panel's button only works on touch — the cursor is locked in play (#413 arbiter) and the HUD never frees it — so it is
hidden on keyboard/gamepad and the panel's first line says how from here: `ui.shiprepair.how_foot` / `how_helm` /
`how_flight` (the pilot seat: F steps inside first, R there is the target lock) with the device phrase
`key_press` ("press R") or `key_pad` ("Actions (L3) → Repair"). The station prompt at the terminal appends
"Repair ship (R)" while something needs repair (the static cockpit label lost its hard-coded "(R)"), R pressed
elsewhere with the panel up toasts `ui.shiprepair.go_to_console`, and the Actions list offers `RepairWreck`
(relabelled "Repair (ship / wreck)") at the own terminal, which gives pads and the touch list a way in. The server
pushes `ShipRepairStatus` on entering the floating interior (`LoadShipInteriorFor`), so a hull dented in flight has a
current readout at the helm.

## Networking & persistence

- New messages (registered in `NetCodec`): `RepairShipIntent` (C→S: `all` | `cell(x,y,z,itemKey)`) at tag 151
  and `ShipRepairStatus` (S→C: hull/hullMax, missing-cell count, manifest cost, affordable) at tag 152.
- Reuses `StructureBlockChanged` (own-ship cells) and the ship snapshot (`SaveShip`) for the hull value. No
  new tables — structural fills ride the existing `SetStructureBlock` delta path.

## Design decisions (locked)

1. **Where can you repair?** Both a safe service point **and** field repair via EVA/interior guided per-cell.
2. **Service point identity?** The ship's `cockpit` station — no medbay, no new Repair-Bay.
3. **Passive hull regen?** None — hull is material-only.
4. **Destruction recovery?** Keep today's free full restore; `DisableShip` unchanged.

The original wreck-repair concept (design reference + matching item) was deliberately generalized rather than
re-invented: wreck repair, own-ship voxel repair, and hull repair are three front-ends on one repair idea.

## Key files / classes

- Server: `GameServerShipRepair.cs` (manifest build, `RepairShipAll`, `RepairShipCell`, cockpit
  `ShipRepairStatus` push). Related: `GameServerSpaceCombat.cs` (`ApplyShipDamage`, `DisableShip`, hull cap),
  `GameServerSpaceStructure.cs` (`BuildShipStructureFrom` / `Baseline`), `GameServerWrecks.cs` (the wreck path).
- Tests: `ShipRepairTests.cs` (5) — hull restore + plate cost, partial when short, per-cell refill + item
  cost, combined hull+cells, free-build. Full suite 589/589.
- Client: HudUi "Repair ship" panel + `NetworkClient`/`GameBootstrap` wiring for messages 151/152.

## Known remaining gaps / deferred

- Needs a Unity client build (server + client wiring done; libs/locales synced).
- A client field/EVA **per-cell highlight UI**: the server already accepts `RepairShipIntent{Mode="cell"}`;
  only the in-world highlight/interact affordance is outstanding.
- Tuning of the plating item / hull-per-plate rate is data-level, not blocking.
