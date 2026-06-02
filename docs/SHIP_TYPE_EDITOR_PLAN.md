# Ship Type Editor — Plan

A menu-launched, in-engine **ship designer**: an empty build room where the user flies/walks in 3D
(like in-game) and places blocks to design a spaceship — hull, viewports, **all ship stations**, an
**exterior hatch**, **ship lights** and the **engine/thruster** — then names the design, sets its
stats and its blueprint + crafting costs, and **saves it as a ship type** that a developer can drop
into the game with minimal effort.

Linked from `TODO.md` ("ship-type editor") and `plans/IMPLEMENTATION_PLAN.md` (M27+ tooling). Honour
the UX principles in `plans/ux_verbesserung.md` (clarity, preview-before-commit, explain blocks).

---

## 1. Goal & scope

- **In:** a main-menu entry "Ship Editor"; a free-build sandbox (3D movement + place/remove blocks);
  a palette of all ship-relevant blocks + station markers + hatch + lights + engine; a metadata panel
  (name, stats, blueprint cost, craft cost, required blueprint); **save/export** the design as a
  ship-type bundle (definition JSON + voxel layout); a **merge tool** so the saved type becomes a real,
  playable ship.
- **Out (later):** in-game player-facing ship building/expansion (separate feature); multiplayer
  co-design; sharing/upload.

---

## 2. How it fits the current ship system

Today a ship type (`data/ships.json` → `ShipDefinition`) is **parametric**: `interiorWidth/length/
height` + `startModules` + stats; the server's `StampShip` builds a hollow **box** from those dims and
drops station markers inside. There is **no freeform voxel layout**.

The editor introduces an **optional voxel layout** per ship type:
- `ShipDefinition` gains an optional `Layout` reference (a voxel blueprint: size + a list of placed
  cells `{x,y,z, block/marker}`), stored next to `ships.json` (e.g. `data/ship_layouts/<key>.json`).
- `StampShip` **uses the layout when present** (stamps the exact design, derives the hatch/markers/
  spawn from it), and falls back to the parametric box when absent (all existing ships keep working).
- Stats (hull/shield/flightSpeed/handling/cargo) stay explicit values the designer sets — they are not
  derived from the geometry (keeps balance in the designer's hands).

This keeps backward compatibility (every existing ship still stamps as before) while letting designed
ships stamp their real shape.

---

## 3. Placeable palette

Grouped, icon-labelled, searchable. Reuses the existing block atlas textures + UI icons.

- **Hull / structure:** `iron_wall` (hull), `glass` (viewport), maybe a few decorative blocks.
- **Stations (markers):** cockpit, reactor, life_support, workshop, medbay, quarters, cargo (the
  existing ship station types) + hangar. Each is a single placeable cell that also adds the matching
  logical module to the ship.
- **New placeable elements (need new block/marker kinds):**
  - **Hatch / airlock** — the exterior entry door (drives the open hatch in the stamped hull).
  - **Ship lights** — emissive light blocks (a new `light` block; ties into the lit-shader/emissive
    work; for the editor an emissive cube is enough).
  - **Engine / thruster** — the rear engine block (drives the visible thruster + "engines off on the
    ground" look).

New content (blocks + locale keys + icons) is added in `data/` so the palette and the stamped ship
share one source of truth.

---

## 4. Editor mode (client)

- **Launch:** AppShell gets a new phase `ShipEditor` (main menu button → enter; Esc → back to menu).
- **Build room:** a small flat platform in an empty starfield (reuse `MenuBackground`/`Sky` ambiance);
  a bounded build volume (e.g. up to 24×16×24) with a faint grid + origin marker.
- **Movement:** reuse a trimmed `PlayerController` (walk + jump, or a free-fly toggle) with no vitals/
  combat — just look + move + place/remove. Block selection box + place/remove like in-game (MiningFx /
  the place path), but client-only (no server).
- **Palette UI:** a side panel (uGUI, per the sci-fi theme) with the grouped palette + current
  selection; scroll/click to pick; hotbar-style quick slots.
- **Metadata panel:** name (bilingual handled by the dev later), description, and editable stats:
  base hull, base shield, flight speed, handling, cargo slots; **blueprint cost** (required blueprint +
  unlock cost) and **craft cost** (item + count rows, add/remove). Live "ship card" preview of the
  values.
- **Validation/preview (UX):** show a derived summary (bounding box, block count, has-cockpit?,
  has-hatch?, has-engine?) and warn on missing essentials (no cockpit / no hatch / not enclosed),
  mirroring the "always show what's missing" principle — but **don't hard-block saving** (designers may
  iterate).

---

## 5. Save / export format

On **Save**, the editor writes a self-contained bundle to a known export folder (e.g.
`%USERPROFILE%/…/Spacecraft/ship_exports/<key>/`):
- `ship.json` — a `ShipDefinition` (key, name, description, stats, cargo, required blueprint, craft
  cost) + a `layout` pointer.
- `layout.json` — the voxel design: `{ width, height, length, cells: [ {x,y,z, kind, block|station} ] }`.

The format is plain JSON so a developer can read/diff it. The key must be unique + slug-safe.

---

## 6. Developer integration (merge)

A tool `tools/merge_ship.py` (uv) folds an export bundle into the game:
1. Validates the bundle (unique key, known blocks/stations/items, sane stats).
2. Appends/updates the `ShipDefinition` entry in `data/ships.json`.
3. Copies `layout.json` to `data/ship_layouts/<key>.json`.
4. Adds placeholder en/de locale strings (`ship.<key>.name/.desc`) if missing.
5. Prints a summary; the next server build/run picks up the new craftable ship type.

(Consistent with the planned content-editor + merge-script workflow; later this can extend to a unified
merge for ships + items + recipes.)

---

## 7. Phases

1. **P1 — Editor MVP (client):** ShipEditor phase + build room + movement + place/remove + palette
   (existing hull/glass + station markers) + metadata panel (name + stats + costs) + **export** to
   `ship.json` + `layout.json`. No game integration yet — the designer can build + save.
2. **P2 — New elements:** hatch, lights, engine as new blocks/markers (content + icons) in the palette.
3. **P3 — Integration:** `merge_ship.py` + `StampShip` uses the saved layout (the designed ship becomes
   a real, craftable, switchable ship that stamps its true shape, with the hatch/markers from the
   layout). Tests for layout stamping + merge validation.
4. **P4 — Polish:** copy/mirror/fill tools, undo, symmetry, a rotating 3D preview on the ship card,
   load-an-existing-type-to-edit, controller support.

---

## 8. Open questions (to confirm before/at P1)

1. **Movement:** walk-on-platform (like in-game) **or** free-fly (6-DoF, easier to build all sides)?
   Recommend a **free-fly toggle** (default fly) for building, with walk as an option.
2. **Layout fidelity in-game:** confirm the designed **voxel layout should be the actual ship shape**
   when stamped (P3), not just a preview. (Assumed yes.)
3. **Where to save / how to integrate:** export to a user folder + `merge_ship.py` into `data/` (the
   recommended path), or write straight into `data/` from the editor (simpler for a solo dev, but mixes
   tool output with source). Recommend **export + merge script**.
4. **Build-volume bounds + block budget:** a sensible max size (e.g. 24×16×24) to keep stamping/cost
   reasonable — confirm or set.
