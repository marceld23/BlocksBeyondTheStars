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
  `Mode`/tab header selects the view; the canvas scales with screen size (reference 1920×1080,
  `ScreenMatchMode.Expand`), so cards and text stay readable on 4K and unshrunk on small displays.
  It does **not** follow the player's `ClientSettings.UiScale` setting: this screen lays out in
  absolute 1920 coordinates, so scaling the canvas up would push content off-screen. Only HUD
  canvases are user-scalable (`UiKit.CreateCanvas(..., userScalable: true)`) — see #483.
- **Card → detail flow.** Lists show cards (icon, name, status, cost row); selecting one fills a detail
  pane with ingredients (have/need, pooled from inventory + ship cargo), required station/blueprint,
  the effect/benefit, and the action button with a reason when disabled. A "craftable now" filter,
  search and the category sidebar narrow the list.
- **Always show "why".** A disabled action states the reason — missing material (with have/need
  counts), needs a station, needs a blueprint, or disabled by a server rule. The blocking reason is
  computed client-side so cards explain *before* you click; server reject text is still surfaced.
- **Location-bound — server-published (#1070/#1074).** The server sends `StationsInReach` (the crafting
  stations usable right now + `ResearchOk` + `ShipBuildOk`) on join and on change; the client never guesses
  from ship markers any more. Crafting gates **per recipe** (`StationAvailable(r.Station)`), Tech binds to
  the **cockpit** (`HandleUnlock` enforces it too), Ship to **aboard + workshop module**. The Tab still opens
  anywhere; a dimmed-but-clickable tab (`IsTabAvailable`) carries the icon of the block it waits for, and
  the **gate row** under the tab bar names that block, asks the server where the nearest one is
  (`LocateStationIntent` → `StationLocation`, live distance + arrow), offers **Show** (compass waypoint)
  and **Craft one →** (jump to the recipe that produces the block). Closing the menu drops a through-wall
  marker on the located block (`OreScanView.ShowStationMarker`). Vocabulary rule (#1071): the UI names the
  **block** (Workbench, Forge, Cockpit …), never an abstract "workshop/lab/console".
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
- Backend: blueprint sync (unlocked-blueprints push → client `Unlocked` set); station gates + locator in
  `GameServerStationAffordances.cs` (`StationsInReach`, `LocateStationIntent`/`StationLocation`); the ship
  structure emits station markers (`medbay / cockpit / workshop / cargo / quarters / console` — see
  `GameServerSpaceStructure.cs`; the phantom `lab` marker was retired in #1074, research lives at the
  cockpit). Client mapping station → block/module: `StationBlockKey` / `StationModuleKey` in
  `CraftingTechShipUI.cs`; the world-side look-at prompt is `Game.AimedStationBlock` (#1073).

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
- Have/need rows now carry a source tag — *craftable* (`GameContent.CraftDepth > 0`) vs *raw resource* —
  and a craftable ingredient the player is short of lists the materials for the missing amount one recipe
  level deep (`IngredientRow`, #1016). A deeper "which planet? cargo? reward?" popover remains deferred.
- Inventory (#2110): the **Backpack** page is a nine-wide slot grid — the backpack rows (slots 9..35) and, under a
  line, the quick-bar row (0..8, numbered); aboard, **Stow all** sits on top. Click-to-pick / click-to-place
  (`_pickKind`/`_pickIndex`), the hotbar swap's model, so mouse, touch and gamepad behave alike: two clicks →
  `MoveItemIntent`. The detail pane offers **Wear / Take off** for a wearable item. The Cargo page stays a card list.
  Gear works only while worn (`GameBootstrap.Wears`).
- Suit page (#2288): a **paper doll** (`BuildSuitPage`) — the armour / oxygen / insulation status line, a simple figure
  drawn from holo shapes with thin lines to its slots (head, chest, legs, feet on the left; back, tank, liner on the
  right; the four module slots of #2293 in a row under it), the passive hint, and **Can be used actively**: the worn
  lamp, jetpack, glider and stealth suit with their real controls (`HudUi.GlyphText` / `HudUi.JumpGlyph`; the ACT list on
  a tablet). The slots are `AddGridSlot` buttons, so the pad walks them by geometry. Clicking any slot opens the
  **slot picker** (#2289, `ShowSlotPicker`) and a worn piece also fills the detail pane (Take off there too).
- Slot picker (#2289): its own canvas at sort 61 over the menu (`UiKit.AddModalOverlay`, `UiNav` on it, the menu's
  nav suspended while it is open; Esc / pad B close it first — `GameMenu` asks `CloseSlotPicker()` before closing the
  menu). One row per fitting stack (`EquipSlots.Accepts`) from the backpack and, aboard (`AboardShipNow()`), the cargo
  hold — icon, name, an effect summary from the item definition (`EffectSummary`, `ui.equip.effect.*`) and a
  Backpack / Cargo hold badge; a module already worn in another module slot is left out. The piece worn there now sits
  on top, marked Worn, with Take off (`UnequipItemIntent`). A row sends `EquipItemIntent { FromSlot, Slot, FromCargo }`
  (the server validates slot and hold). With nothing to wear the picker offers **Show recipe** (the first non-market
  recipe for the slot whose blueprint is known → `JumpToRecipe`) or names where to research it; on foot a line says more
  may wait in the hold. `JumpToRecipe` switches the page itself (`ShowMode(Crafting)`) so a jump from another tab keeps
  its selection.
