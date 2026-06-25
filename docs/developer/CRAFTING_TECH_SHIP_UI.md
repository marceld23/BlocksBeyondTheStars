# Crafting · Tech · Ship menu — how it works

Status: implemented (see TODO.md for live Done/Open status) · 2026-06-19

## Overview

The Tab menu's **Crafting**, **Tech** and **Ship** screens are a polished uGUI redesign built around
five guiding questions: *what can I do · why (not) · what's missing · what's next · what's the
benefit*. They share one screen (`CraftingTechShipUI`) with a card/list/detail layout, are bound to
their in-world station, and surface "why blocked" before you click. The three tabs share layout, card
components, a category sidebar and blueprint state.

## How it works

- **One uGUI screen.** `CraftingTechShipUI` (new Canvas UI, not IMGUI) hosts Crafting, Tech, Ship and
  the other menu tabs it now owns (Inventory, Map, Missions, Story, Companions, Alliances, and
  Settings — `Mode.Character` is labelled "Settings"). A
  `Mode`/tab header selects the view; the canvas scales with screen size (reference 1920×1080) and
  honours `ClientSettings.UiScale` / `LargeUi`, clamped so cards and text stay readable on 4K and
  unshrunk on small displays.
- **Card → detail flow.** Lists show cards (icon, name, status, cost row); selecting one fills a detail
  pane with ingredients (have/need, pooled from inventory + ship cargo), required station/blueprint,
  the effect/benefit, and the action button with a reason when disabled. A "craftable now" filter,
  search and the category sidebar narrow the list.
- **Always show "why".** A disabled action states the reason — missing material (with have/need
  counts), needs a station, needs a blueprint, or disabled by a server rule. The blocking reason is
  computed client-side so cards explain *before* you click; server reject text is still surfaced.
- **Location-bound.** Crafting is active at the **workshop**, Tech at the **lab**, Ship expansion at
  the **ship console**. The Tab still opens anywhere; when you're not at the station the screen guides
  you there (the dimmed-but-clickable tab gating uses `IsTabAvailable`). The server stays authoritative
  on craft/unlock/build.
- **Blueprint state on the client.** The server's `PlayerState.UnlockedBlueprints` is synced to the
  client (on join, on unlock, on resync) so cards can show "craftable now", grey locked recipes, and
  blueprint status without round-tripping.
- **Per tab.** Crafting: category/tool-kind cards + ingredient have/need + disassemble. Tech: a
  per-category/tier blueprint tree with node status colours, prerequisite display, unlock cost
  have/need. Ship: module cards with stat deltas, the fleet as ship cards (stats, Active badge,
  Switch), and craftable ship types as cards (stats + cost + required blueprint).
- **Bilingual** via `Localizer` (`ui.craft.*` / `ui.tech.*` keys, en/de parity).

## Key files & classes

- `client/Assets/BlocksBeyondTheStars/Scripts/CraftingTechShipUI.cs` — the whole screen: header/tabs,
  card/detail components, crafting/tech/ship views, the embedded inventory/map/missions/character/
  alliances tabs, world-rules toggle row, ship preview rig.
- Data sources (no new data needed): `ItemDefinition` (category/tool/effects), `RecipeDefinition`
  (station/blueprint/inputs/outputs), `BlueprintDefinition` (category/prerequisites/costs),
  `ShipModuleDefinition` / `ShipDefinition` (stats/cost/blueprint), `Game.OwnedShips`.
- Backend: blueprint sync (unlocked-blueprints push → client `Unlocked` set); the ship structure emits
  station markers (`medbay / cockpit / workshop / cargo / quarters / lab` — see
  `GameServerSpaceStructure.cs`). Tech binds to `lab` (the workshop doubles as a research bench) and
  Ship binds to `console` (the cockpit doubles as the ship console), so designed ships without a
  dedicated lab/console tile still work — see `StationOkFor` in `CraftingTechShipUI.cs`.

## Design notes

- **uGUI over IMGUI** for these screens to get polished cards/tree and DPI-independence; other legacy
  tabs migrated into the same screen over time.
- **Material source is the pool** of inventory + ship cargo, shown explicitly, so have/need reflects
  what you can actually use.
- **Inventory ↔ cargo transfer** lives in the Inventory mode tabs. The *Inventory* tab offers a
  bulk **"Stow all materials in cargo"** button (loose materials/components only — the server filters
  by item category, like a storage crate) and a per-item **"Move to cargo hold"** in the detail pane;
  the *Cargo Hold* tab shows a **used/total** capacity readout, **"Take all out"**, and per-item
  **"Move to inventory"**. All of it is gated on `AboardShipNow()` client-side and re-validated by the
  server (`MoveCargoItemIntent` → `GameServerCargo.MoveCargo`, which requires `AboardShip`). On foot the
  cargo tab shows a "step aboard" hint instead of dead controls. Capacity comes from
  `InventoryUpdate.CargoSlotCount`. The optional *auto-stow on boarding* comfort toggle is purely
  client-side: `GameBootstrap` watches the not-aboard→aboard edge and fires the same bulk intent.
- **Tech-tree layout** is a simple per-category column/tier layout (no physics) — deliberately chosen
  to stay legible as blueprint count grows.

## Known gaps / deferred

- The full **3D ship-expansion preview** is deferred; the Ship tab uses cards + a stat-delta preview.
- The "missing material → where to get it" deep popover (craftable? which planet? cargo? reward?) is a
  polish item layered on the have/need rows.
- Inventory is still a category list rather than a drag/swap slot grid (tracked separately in TODO.md).
