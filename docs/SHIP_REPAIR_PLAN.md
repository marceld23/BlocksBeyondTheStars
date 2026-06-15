# Ship Repair — Unified Concept (Hull + Structure)

> Goal: give the player a **single, coherent way to repair their own ship** that covers
> **both** dimensions of ship integrity that exist today but cannot currently be restored:
> (1) the **numeric hull** stat (`ShipState.Hull`, dented by combat/collisions, never regenerates),
> and (2) the **structural voxels** of the ship (missing `Baseline` cells, today only refillable by
> placing each block back by hand). The design reuses and generalizes the **existing wreck-repair
> system**, so wreck repair, own-ship voxel repair, and hull repair all become instances of one
> verb: *restore the ship toward its design reference, paying a material manifest.*

> **Status (2026-06-15): IMPLEMENTED (server + client wiring) — needs a Unity client build.**
> Server engine, messages, dispatch and the cockpit hook are done and covered by tests (589/589 green,
> 5 new in `ShipRepairTests`). Client wiring (NetworkClient event/send, GameBootstrap state, HudUi
> "Repair ship" panel at the cockpit, bilingual locales) is in place; libs+locales synced via
> `scripts/sync-client-libs.ps1`. **Remaining manual step:** open Unity and build the client.
> **Follow-up (not yet built):** a client field/EVA per-cell highlight UI — the server already accepts
> `RepairShipIntent{Mode="cell"}`, only the in-world highlight/interact affordance is outstanding.
>
> The design choices were resolved with the user — see §11 (all decisions **locked**).

> **Locked decisions (§11):** repair is allowed **both** at a safe service point **and** as
> **field repair via EVA/interior**; the service point is the ship's **cockpit** station (no new
> station, no medbay); **material-only** hull repair (**no** passive hull regen of any kind);
> ship destruction keeps **today's free full restore** (§8.5 unchanged).

---

## 1. Why this is needed (recap of the analysis)

Three facts from the current code (see [GameServerSpaceCombat.cs](../src/BlocksBeyondTheStars.GameServer/GameServerSpaceCombat.cs),
[GameServerSpaceStructure.cs](../src/BlocksBeyondTheStars.GameServer/GameServerSpaceStructure.cs),
[GameServerWrecks.cs](../src/BlocksBeyondTheStars.GameServer/GameServerWrecks.cs)):

- **Combat damage is numeric only.** `ApplyShipDamage` drains `Shield` first, then `Hull`
  (`GameServerSpaceCombat.cs:1199`). No voxels are touched. Sources: hostile DPS while inside
  `ShipEngageRange = 70` and speed-scaled asteroid rams (capped 18).
- **Shield regenerates, hull does not.** Out of combat the shield recharges at
  `BaselineShipShieldRegen = 2/s` + module `shield_regen` (`:1008-1011`). `_ship.Hull` is only
  ever written by damage (`:1211`), a clamp that fires solely at `≤0` or `> max` (`:210-212`),
  and the full restore in `DisableShip` (`:1224`). **A partial hull (e.g. 40/100) never recovers**
  except by being destroyed — which is free. There is no repair point, station, or action.
- **Structural voxels are only restorable by hand.** Missing `Baseline` cells (carved during EVA,
  persisted via `SetStructureBlock`) can be refilled by manually placing each block
  (`HandleLandedShipEdit` / `HandleStructureEdit`), costing the matching block item, with **no
  guidance** about which block goes where.
- **Wrecks already have the mechanic we want.** `RepairWreck` validates each breach against a
  stored **intact design mask** (`_intact`), consumes the *exact matching* block item, fills the
  cell, broadcasts, and `ClaimWreck` finalizes (`GameServerWrecks.cs:154-260`). This is precisely
  "guided material repair against a design reference."

The player's own ship already carries the same kind of reference a wreck does:
`SpaceStructure.Baseline` (the design-derived cells, snapshotted before player edits). So the data
needed for a unified repair already exists — it is only the *action* that is missing.

---

## 2. The unifying idea

Treat **ship integrity as the diff between a design reference and the current state**, in two
layers, and repair = paying to close that diff:

| Layer | Reference (target) | Current | Diff to repair |
|-------|--------------------|---------|----------------|
| **Structure** (voxels) | `Baseline` cells (own ship) / `_intact` (wreck) | present `Cells` | each `Baseline` cell now air → needs its **design block item** |
| **Hull** (stat) | `_shipHullMax` (design + modules) | `_ship.Hull` | the deficit → needs a generic **plating material** |

One abstraction expresses both as a single material cost (`List<ItemAmount>`); one verb consumes
it. Wrecks, own-ship voxels, and hull all flow through the same code. That is the "einheitlich".

---

## 3. Shared spine: the Repair Manifest

A new value object (server-side, e.g. `ShipRepairManifest`) computed from a structure + hull state:

```
ShipRepairManifest BuildManifest(SpaceStructure s, float hull, float hullMax):
    structuralCells = [ (cell, requiredBlock) for cell in s.Baseline if s.Get(cell).IsAir ]
    hullDeficit     = max(0, hullMax - hull)
    cost            = group structuralCells by requiredBlock → ItemAmount(blockItem, count)
                      + ItemAmount(PlatingItem, ceil(hullDeficit / HullPerPlate))
    return { structuralCells, hullDeficit, cost }
```

- `structuralCells` mirrors `EnumerateWreckRepairCells` — same shape, so the wreck path can produce
  a manifest too (`Baseline := _intact`). This is where wreck + own-ship repair converge.
- `cost` is one flat `List<ItemAmount>`, validated/charged through the existing
  [`MaterialPool`](../src/BlocksBeyondTheStars.GameServer) (player inventory + ship cargo), exactly
  like crafting and wreck repair.
- Modules (`StationCells`) are never in the manifest — they are protected and cannot go missing.

Everything below is just *how* a manifest is presented and consumed.

---

## 4. Mechanic 1 — Hull (stat) repair

Replace "hull never recovers except by dying" with **material repair only** (locked: no passive
regen of any kind):

- **Material repair.** The hull deficit is part of the manifest. Repairing converts `PlatingItem`
  into hull points at a fixed rate (`HullPerPlate`, e.g. 1 plate = 10 hull), up to `_shipHullMax`.
  Implemented as a small helper that raises `_ship.Hull` and re-sends `ShipCombatStatus`. Reuses
  `RecomputeShipCombatStats` for the cap.

> **No hull regen at all** — not in combat, not free-floating, and (by decision) **not** a safe
> docked trickle either. Hull only rises by spending materials. The shield stays the only thing
> that recharges (out of combat), keeping the shield's role intact. The §9 free destruction-recovery
> is the casual safety net that prevents a true soft-lock.

---

## 5. Mechanic 2 — Structural (voxel) repair of own ship

Generalize wreck repair to the player's own `Baseline`:

- **5a. Guided one-cell repair.** Same UX as wreck breaches: missing `Baseline` cells are
  highlighted; aiming + acting fills one cell with its **correct** design block, auto-charging the
  matching item (no need to know/own the right block by guesswork). This is `RepairWreck`'s logic
  keyed on `Baseline` instead of `_intact`, writing to the live `SpaceStructure` and persisting via
  `SetStructureBlock` (so it survives restart, same as today's manual fills).
- **5b. Works wherever the structure exists.** In space during **EVA** (live `SpaceStructure`) and
  on the **landed/interior** ship (`LandedShip.Structure`) — both already hold `Baseline`. The
  free-form manual place/mine paths stay exactly as they are; guided repair is an *assist* layered
  on top, not a replacement.

This makes "fehlende Teile nach EVA-Mining" trivially fixable, and uses the identical reference the
wreck system already trusts.

---

## 6. The unified repair surface (one place, both layers)

One interaction repairs **both** layers from the shared manifest. There are **two surfaces, one
manifest** (both locked in):

- **Service point = the ship's `cockpit` station.** The cockpit is where the pilot manages the
  ship, so the "Repair Ship" panel lives there — reusing the existing `StationCells` cockpit marker,
  no new station/content. Usable when at the cockpit (landed/interior or aboard).
- **Field repair = EVA / interior, guided per-cell** (§5a) — fill missing `Baseline` cells anywhere
  the structure exists, paying the same per-cell cost. This is the "anywhere" option the user chose
  alongside the cockpit service point.
- The cockpit panel opens a **Ship Repair panel**:
  - Hull bar (`hull / hullMax`) and **missing-cells count**.
  - The full **material cost** (the manifest) and what the player currently has.
  - **"Repair All"** → consume the whole manifest at once: refill every missing `Baseline` cell +
    restore hull to max. One click, both layers.
  - (Optional) **"Guided repair"** toggle for the free-form per-cell mode of §5a, for players who
    want to do it by hand / can only partially afford it.
- **Free-build parity:** when `!Rules.CraftingCostsMaterials || p.InstantBuild`, repair is free —
  same rule the wreck/structure/crafting paths already honor.

So: emergency hand-repair in the field (EVA/interior, §5) **and** a clean one-click service at a
repair point (§6) both read and pay the **same manifest**.

---

## 7. Reusing & generalizing the wreck system

Refactor the wreck-specific repair into a shared `StructureRepair` helper used by three callers:

| Caller | Reference mask | Target store | Persist | Finalize |
|--------|----------------|--------------|---------|----------|
| Wreck (existing) | `_intact` | world blocks (`SetBlock`) | wreck state | `ClaimWreck` |
| Own ship — voxels | `Baseline` | `SpaceStructure.Cells` | `SetStructureBlock` | — |
| Own ship — hull | (n/a) | `_ship.Hull` | ship snapshot | — |

Shared verbs: `BuildManifest`, `RepairCell(ref, cell, itemKey)`, `RepairAll(ref)`. The existing
`RepairWreck` becomes a thin wrapper over `RepairCell`. Net result: **one repair engine**, three
front-ends. This is the core of "möglichst einheitlich".

---

## 8. Networking & persistence

- **New messages** (must be `Register()`'d in `NetCodec` or they silently no-op):
  - `ShipRepairStatus` (server→client): hull/hullMax, missing-cell count, manifest cost, affordable?
  - `RepairShipIntent` (client→server): mode = `All` | `Cell(x,y,z,itemKey)`.
- **Reuse existing** for the visible result: `StructureBlockChanged` (own-ship cells),
  `BlockChanged` (wreck world blocks), `ShipCombatStatus` (hull update). The client highlight of
  missing cells can reuse whatever the wreck-breach UI already does.
- **Persistence:** structural fills persist via the existing `SetStructureBlock` per-cell deltas;
  the hull value persists in the existing ship snapshot (`SaveShip`). No new tables.

---

## 9. Balance interaction: destruction recovery vs. paid repair

**Locked: keep today's free full restore (option A).** `DisableShip` stays exactly as it is — on
hull 0 the ship is recovered to base with full hull/shield, no permanent loss (§8.5). Repair's value
is therefore **avoiding** the destruction event entirely: keeping your cargo/position/run and
skipping the recovery downtime, rather than being the only path back to full. This also means a
broke player can never be hard soft-locked (the free recovery is the floor that lets §4 stay
material-only with no passive regen). Revisit only if suicide-repair turns out to feel gamey.

---

## 10. What stays unchanged

- Shield mechanics (baseline + regen out of combat) — untouched.
- Damage sources/values, `ApplyShipDamage`, `DisableShip` flow — untouched (except the §9 decision).
- Module protection (`StationCells` never removable/never in manifest).
- Free-form EVA/interior build & mine — untouched; guided repair is additive.
- Wreck claim flow — same; only its internals are refactored onto the shared engine.

---

## 11. Decisions (resolved 2026-06-15)

1. **Where can you repair?** → **Both.** Safe service point **and** field repair via EVA/interior
   guided per-cell. *(locked)*
2. **Service point identity?** → **The `cockpit` station.** No medbay, no new Repair-Bay station —
   reuse the existing cockpit `StationCell`. *(locked)*
3. **Passive hull regen?** → **None.** Hull is **material-only**; not even a safe docked trickle.
   *(locked — §4)*
4. **Destruction recovery?** → **Keep today's free full restore (A).** `DisableShip` unchanged.
   *(locked — §9)*

**Still to pick during implementation (tuning, not blocking the concept):**

- **Hull material `PlatingItem` + `HullPerPlate` rate.** *(recommended: an existing common metal
  item, e.g. `iron_plate`/metal, to avoid new content; tune the points-per-item.)*
- Exact per-cell vs whole-manifest UX affordances in the cockpit panel (cosmetic).

---

## 12. Implementation phases (for later — no code yet)

- **P0 — Shared engine:** extract `StructureRepair` + `ShipRepairManifest` from wreck logic;
  re-point `RepairWreck` onto it (pure refactor, existing wreck tests stay green).
- **P1 — Hull material repair:** manifest hull term + `RepairAll` hull restore + `ShipCombatStatus`.
- **P2 — Own-ship voxel repair:** `Baseline`-keyed `RepairCell`/`RepairAll`, persistence, broadcast.
- **P3 — Repair surface:** `ShipRepairStatus`/`RepairShipIntent` messages (register in `NetCodec`),
  server panel data, **cockpit-station** interaction + field-EVA guided per-cell repair.
- **P4 — Client UI:** Ship Repair panel at the cockpit (hull bar, missing-cell count, cost,
  "Repair All", guided per-cell mode + missing-cell highlight reusing the wreck-breach visuals).

> No passive-regen phase and no `DisableShip` change — both are locked out by §11 (material-only
> hull, free destruction recovery unchanged).

## 13. Test plan sketch

- Manifest: damaged hull + N missing `Baseline` cells → correct cost; full structure + full hull →
  empty manifest.
- `RepairAll`: enough materials → hull = max, all cells filled, items consumed, status broadcast;
  insufficient → rejected/partial per rule; free-build → no consumption.
- Voxel repair persists across re-entry/landing (reuses `SetStructureBlock` delta path).
- Wreck repair still passes via the refactored engine (regression).
- Hull cannot exceed `_shipHullMax`; modules never appear in the manifest.
