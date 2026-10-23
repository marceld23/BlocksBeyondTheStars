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
  Character — `Mode.Character`, labelled "Character" since #2505, key `ui.tab.settings`; it was "Settings" before and
  holds the character page, "People you know" and the in-world options; graphics, sound, controls and language live
  in the separate Settings screen, `UiSettings`). A
  `Mode`/tab header selects the view; the canvas scales with screen size (reference 1920×1080,
  `ScreenMatchMode.Expand`), so cards and text stay readable on 4K and unshrunk on small displays.
  It does **not** follow the player's `ClientSettings.UiScale` setting: this screen lays out in
  absolute 1920 coordinates, so scaling the canvas up would push content off-screen. Only HUD
  canvases are user-scalable (`UiKit.CreateCanvas(..., userScalable: true)`) — see #483.
- **Card → detail flow.** Lists show cards (icon, name, status, cost row); selecting one fills a detail
  pane with ingredients (have/need, pooled from inventory + ship cargo), required station/blueprint,
  the effect/benefit, and the action button with a reason when disabled. A "craftable now" filter,
  search and the category sidebar narrow the list.
- **List order and filters (#826, #2563, #2566).** Crafting sorts by reach tier (`ReachTier`: craftable, materials
  missing, factory terminal out of reach, blueprint locked), then **A–Z by the card title** (`RecipeTitle`, before
  the factory suffix), then bench before factory twin; the market category keeps its authored order. The Blueprints
  tab sorts by tree tier, then A–Z; the Sandbox catalog by item rank, then A–Z. Every A–Z uses
  `LocaleCollation.Comparer(game locale)` (Client.Core) — the game language's collation, never the OS culture —
  which probes the platform's culture comparer once and falls back to an accent-folding comparer where collation
  data is missing (invariant globalization, a stripped IL2CPP/WebGL player). The Blueprints tab's **"Researchable
  now"** toggle (`ui.tech.researchable_now`; "Enough knowledge" before) keeps only the nodes whose
  `BlueprintResearch.StateOf` (Client.Core) is `Researchable` — the same test as the card status, the header's
  "next" line and the Tech badge, mirroring the server's unlock gates (prerequisites, knowledge, materials).
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
- **Aboard, the module comes first (#2585).** Aboard a ship the server counts only a fitted **module** for a station
  (`StationAvailable`; a station block built into a ship is decoration, #2248). So aboard (`AboardShipNow`) the reason
  line, the gate-row hint and the recipe page's "Station:" line name the module and the **Ship** tab
  (`ui.craft.need_module_aboard`, `ui.craft.hint_need_module`, `ui.craft.station_module`, with the module's icon), a
  station no module provides (campfire, algae tank) reads "only on a world" (`ui.craft.need_world_only` /
  `hint_world_only`), "no station in reach" names the workshop module instead of a workbench
  (`ui.craft.hint_hand_only_aboard`), and **Craft one →** is not offered aboard for either kind — that offer is how a
  forge ended up beside the cabin's workbench. The factory keeps its own wording everywhere (a player-placed terminal
  never produces, #1265). The text choice is `CraftStationText` (Client.Core, tested headless). The server's refusal
  says the same: `@srv.craft.need_module:<module>` / `@srv.craft.world_only:<block>` aboard (off the ship, and for the
  factory, `@need_station:<station>` as before), and a material refusal names the items with have/need
  (`@srv.craft.missing_named:Titanium Ore (0/2)`);
  `GameBootstrap.CraftFailMessage` resolves every `@srv.*` reason through `ResolveServerToken`. The long reason lines
  wrap and push the rows below down (`AddWrappedDetailLine`).
- **Blueprint state on the client.** The server's `PlayerState.UnlockedBlueprints` is synced to the
  client (on join, on unlock, on resync) so cards can show "craftable now", grey locked recipes, and
  blueprint status without round-tripping.
- **Per tab.** Crafting: category/tool-kind cards + ingredient have/need + disassemble. Tech: a
  per-category/tier blueprint tree with node status colours, prerequisite display, unlock cost
  have/need. Ship: module cards with stat deltas, the fleet as ship cards (stats, Active badge,
  Switch), and craftable ship types as cards (stats + cost + required blueprint).
- **Bilingual** via `Localizer` (`ui.craft.*` / `ui.tech.*` keys, en/de parity).

## Key files & classes

- `client/Assets/BlocksBeyondTheStars/Scripts/CraftingTechShipUI.cs` — the screen's shell: header/tabs, the
  category sidebar, the list/detail dispatch and the craft-result feedback. `CraftingTechShipUI` is a sealed
  partial class; each tab or page lives in its own `CraftingTechShipUI.<Part>.cs` (Inventory, Samples, Suit,
  Crafting, Tech, Ship — with the ship preview rig —, Map, Missions, Character — with the world-rules rows —,
  Alliances, Companions, Photos, Notes, Achievements, Journal), and three partials hold what the tabs share:
  `.Recipes` (ingredient rows, costs, the catalog gate and the wrappers over `CraftGate`), `.Stations` (station gates
  and the "Where?" locator) and `.Scaffolding` (uGUI cards, scroll views, search boxes and the text helpers).
- `src/BlocksBeyondTheStars.Client.Core/CraftGate.cs` — the crafting rules, engine-free (#2932). `Check` names the
  first gate that blocks an order of N crafts as a `CraftBlock`, in the order of the reason line: in Creative only the
  room for the result; otherwise blueprint, materials, then the market (vendor, then today's offer), the factory
  terminal or the recipe's station, and last the room for the result. Materials and room are checked for the whole
  ×N order, as the server's `HandleCraft` does (#2937): the detail page asks for the stepper's count, the list cards
  for one craft. The menu maps each value to its `ui.craft.*` line. Beside it: `ResultFits` (the client's dry-run of
  the server's `MaterialPool.CanFit` over the scaled outputs), `Owned` / `HasAll` (what `MaterialPool.Count` / `Has`
  count: backpack, hold while aboard and the worn gear, each with the pieces the bio lab changed — the variant rule is
  the shared `ItemMods.IsChangedVariant`, #2937), `MaxCraftable`, `ReachTier` and `DisassembleRecipe`. The menu hands
  it a `CraftGateInput` (plain values plus four answers only Unity can give, as delegates bound once).
  `CraftGateTests` (Client.Tests) pins the order and checks `ResultFits`, `Owned` and `HasAll` against the server's
  `MaterialPool`.
- Data sources (no new data needed): `ItemDefinition` (category/tool/effects), `RecipeDefinition`
  (station/blueprint/inputs/outputs), `BlueprintDefinition` (category/prerequisites/costs),
  `ShipModuleDefinition` / `ShipDefinition` (stats/cost/blueprint), `Game.OwnedShips`.
- A recipe card and its detail page are titled with the output item's name — unless the recipe carries its own
  optional `nameKey` / `descriptionKey` (#2544, `RecipeTitle`): a second recipe of the same item that must read
  differently, like the hand-built **Wooden boat** (`boat_wood`) next to the workshop's **Boat**. The search box
  matches that title too. A factory twin keeps its "· Factory terminal" suffix.
- Every detail pane's description (crafting, catalog, inventory, tech, ship module) is one block that grows with its
  text (`AddDetailDescription`, #2545: at least 80 px, else `preferredHeight`) — a fixed 80 px box let long texts run
  into the ingredient rows below.
- Backend: blueprint sync (unlocked-blueprints push → client `Unlocked` set); station gates + locator in
  `GameServerStationAffordances.cs` (`StationsInReach`, `LocateStationIntent`/`StationLocation`); the ship
  structure emits station markers (`medbay / cockpit / workshop / cargo / quarters / console` — see
  `GameServerSpaceStructure.cs`; the phantom `lab` marker was retired in #1074, research lives at the
  cockpit). Client mapping station → block: `StationBlockKey` in `CraftingTechShipUI.Stations.cs`; station → module:
  `CraftStationText.ModuleKey` (Client.Core, mirrors the server's `StationModuleFor`); the world-side look-at prompt
  is `Game.AimedStationBlock` (#1073).

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
  **Single stacks and amounts (#2729):** the intent carries the contractless-additive `FromSlot` (a slot of the
  source side; -1 = none) and `Count` (0 = whole stack / every stack). The server moves exactly that stack
  (`MoveSlot`, optionally a part of it) or `Count` items across the item's stacks (`MoveCount`, last source stack
  first; the destination tops up its stacks before opening new ones, `maxStack` respected), and rejects with
  `@srv.cargo.full` / `@srv.cargo.backpack_full` when nothing fit. An older client sends neither field (every stack,
  as before); an older server ignores them. On the *Cargo Hold* tab each card has a **"Take"** button (that stack;
  Shift = every stack of the kind); the detail pane offers **This stack / All of this kind / How many? (1, 5, half)**
  in the page's direction, the slot remembered in `_stackSlot` and resolved by `Client.Core`'s
  `CargoStackPicker` (which also computes the amounts). The crate screen's `MoveContainerItemIntent` gained the same
  `FromSlot`, so a click stores the clicked backpack stack rather than the first one of that item.
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
- Worn gear on the body (#2294–#2297): `PlayerAvatar.SetGear(int)` takes the `GearLook` mask — the local player builds it
  from the worn slots (`GearLook.Mask(Game.Wears)`, rebuilt only when it changes), `RemotePlayers` passes the presence's
  `Gear` straight through. Titanium pieces use the `avatar_armor_titan` texture in blue-silver plus a helmet crest,
  shoulder pads and knee guards; the spring boots stand on a silver coil (`SpringStretch` on every jump, `SpringCompress`
  on a landing — remotes derive both from their interpolated height); the glider is a folded pack whose wings
  (`avatar_glider`, orange) unfold over 0.3 s on `SetGliding(true)` (local `PlayerController.Gliding`, remote
  `PlayerPresence.Gliding`) and replaces the jetpack on the back; the suit battery is a pulsing cyan cell on the belt.
  The own stealth cloak (`Game.Stealthed`) turns the third-person figure into shimmering glass (`SetStealthShimmer`) —
  other players never see a cloaked player at all. Movement: the spring boots scale the ground jump's impulse by
  √(1 + `SuitEquipment.JumpBoost`); holding Jump while falling with the glider where there is air (an atmosphere, below
  its line, no spacewalk/zero-g) brakes the fall to a 2.5 m/s sink and sails forward at ~8 m/s along the view (W 10,
  S 4, A/D sideways, gales drift it), reported by edge with `SendSetGliding`; a menu keeps an open wing, every place that
  cuts the jetpack folds it, a server refusal ("glider") folds it until Jump is let go. The HUD energy bar's full mark is
  `GameBootstrap.SuitEnergyMax` (150 with the battery).
