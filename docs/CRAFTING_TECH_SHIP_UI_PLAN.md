# Crafting · Tech · Ship Menu Redesign — Plan

Status: **plan (approved decisions, not yet implemented)**. Redesign the Tab menu's **Crafting**, **Tech**
and **Ship** screens to the UX concept in `plans/ux_verbesserung.md` (§4 Crafting, §5 Blueprints/Tech,
§6 Schiffsausbau) and its five guiding questions: *what can I do · why (not) · what's missing · what's
next · what's the benefit*.

**Approved decisions (this redesign):**
- **uGUI** (new Canvas UI, polished cards/tree) — not IMGUI.
- **AI-generated category icons** (OpenAI, gated paid batch, like the existing 14 UI icons).
- **Location-bound**: Crafting at the **workshop**, Tech at the **lab**, Ship expansion at the **ship
  console** (the Tab still opens, but each screen is *active* only at its station, with clear guidance
  when you're not there).
- **All three tabs together** (shared layout, card component, category sidebar, blueprint sync).

---

## 1. Current state (analysis)

`GameMenu` (IMGUI) renders the three tabs as flat `Name (cost) [Button]` lists:
- **Crafting** — every recipe, a Craft button; a Disassemble list. No categories/filter/ingredient view.
- **Tech** — every blueprint, `name (unlock cost) [Unlock]`. No tree, no status, no prerequisites shown.
- **Ship** — ship modules `name (cost) [Build]`; owned-ships fleet w/ Switch; craftable ship types
  `name (cost) [Craft]`. No stats, no preview, no "why blocked".

**Data available (sufficient):**
- `ItemDefinition`: `Category` (Material/Block/Tool/Consumable/Component), `Tool` (ToolKind drill/weapon/
  scanner/repair/placer + tier/power), `ConsumeHealth/Hunger`, `ArmorResistance`, `OxygenBonus`,
  `DescriptionKey`, `PlacesBlock` → derive **finer categories** + rich detail cards without new data.
- `RecipeDefinition`: `Station` (Hand/Workshop/Refinery/Lab/MachineRoom/…), `RequiredBlueprint`,
  `Inputs`, `Outputs`.
- `BlueprintDefinition`: `Category`, **`Prerequisites`**, `UnlockCost`, `KnowledgeCost`, `DescriptionKey`.
- `ShipModuleDefinition` (cost, mandatory), `ShipDefinition` (stats: hull/shield/flightSpeed/handling/
  cargo + craftCost + requiredBlueprint), `Game.OwnedShips`.

**The one backend gap:** the client doesn't know what's unlocked. The server holds
`PlayerState.UnlockedBlueprints` and validates server-side, but never sends it. The redesign needs it on
the client for "craftable now", locked-recipe greying, and blueprint status. → **sync it** (§3).

---

## 2. Target UX (per the concept)

A consistent **three-pane** screen for each tab:

```
┌────────────┬───────────────────────────┬───────────────────────────┐
│ CATEGORIES │ LIST (cards)              │ DETAIL of selection       │
│ (icons)    │  search ▢   ☑ craftable   │  name · icon · desc       │
│  Tools     │  [card] [card] [card]     │  ingredients (have/need)  │
│  Suit      │  [card] [card] [card]     │  required station/bp      │
│  Medicine  │  ...                      │  effect / benefit         │
│  Components│                           │  [Craft]  (reason if off) │
│  Blocks    │                           │                           │
│  Ship mod. │                           │                           │
└────────────┴───────────────────────────┴───────────────────────────┘
Materialquelle: Inventar + Frachtraum
```

Shared rules (the concept's backbone):
- **Always show "why"**: a disabled action states the reason — *missing 2 Energy Cell I*, *needs Workshop*,
  *needs blueprint X*, *disabled by server rule*. Missing ingredients are clickable → "where to get it"
  (craftable? which recipe? found on which planet? in cargo? a mission reward?).
- **Have/need** per ingredient: ✓ green when owned (counts pooled from **inventory + ship cargo**),
  ✗ red when short, with `have/need` counts.
- **"Craftable now" filter** + **search** + the category sidebar.
- **Preview before commit**; clear **success feedback** (sound + glow + "X hergestellt" + hotbar prompt).

### Per tab
- **Crafting (workshop):** categories from item category/tool-kind; recipe cards (icon, name, craftable
  highlight); detail = ingredients have/need + station + blueprint + result/benefit + Craft. Keep
  Disassemble as a sub-action. Source = inventory + cargo (shown).
- **Tech (lab):** a **visual tree/net** grouped by `Blueprint.Category`, prerequisite edges, node status
  colours (Unknown/Discovered/Researchable/Materials-missing/Unlockable/Unlocked/Server-disabled); detail
  = benefit ("unlocks recipes/modules X") + prerequisites (✓/✗) + unlock cost (have/need) + Unlock.
- **Ship (ship console):** **module cards** (icon, effect on stats Δ, cost have/need, Build + reason);
  the **fleet** as ship cards (stats, Active badge, Switch); **craftable ship types** as cards (stats +
  cost + required blueprint). (Full 3D ship-expansion preview from §6 is a later step — start with cards
  + a stat-delta preview.)

---

## 3. Backend changes (small, shared)

1. **Sync unlocked blueprints.** Add `string[] UnlockedBlueprints` to the inventory/state push (extend
   `InventoryUpdate` or `PlayerStateUpdate`, or a tiny `KnownBlueprints` message) → `GameBootstrap` keeps a
   `HashSet<string> Unlocked`. Sent on join, on unlock, and on (re)sync. Enables client-side status.
2. **Reject reasons already exist** (`CraftFail`/`Reject` carry text) — surface them, but also compute the
   blocking reason **client-side** (missing material/blueprint/station) so cards explain *before* you click.
3. **Location stations.** Location-binding needs the stations to exist. Today the ship has cockpit/
   workshop/medbay/cargo/quarters markers. Add a **lab** marker (Tech) and a **ship console** marker (Ship
   expansion) to `StampShip` + the station registry + `Game.NearbyStation`; map Crafting→workshop,
   Tech→lab, Ship→console. When not at the station: show the screen read-only with a hint + a marker on
   the compass ("Go to the Lab to research"). (Server still authoritative on craft/unlock/build.)

---

## 4. uGUI architecture

- One `CraftingTechShipUI` built in code on a `ScreenSpaceOverlay` canvas (CanvasScaler → 1920×1080,
  matching `UiScale`), reusing the existing **`UiKit`** uGUI helpers (panels, buttons, theme) from the
  main menu. Coexists with the IMGUI Tab frame: the Tab bar stays, but selecting Crafting/Tech/Ship shows
  this canvas instead of the IMGUI body (other tabs stay IMGUI for now).
- **Resolution- & DPI-independence (must look good on a high-DPI *and* a normal monitor):** the
  `CanvasScaler` uses **Scale With Screen Size**, reference **1920×1080**, `matchWidthOrHeight ≈ 0.5` so
  the layout scales by the *physical* screen, not the pixel count — a 4K/high-DPI display shows the same
  layout crisp (not tiny), a 1080p display shows it unshrunk (not overflowing). Honour the existing
  **`ClientSettings.UiScale`** and **`LargeUi`** as an extra user multiplier, **clamped** (min/max) so
  cards + text never get too small on dense 4K or overflow on small screens. The three-pane grid uses
  flexible/relative widths (not fixed pixels) and scrollable lists, mirroring how the current HUD/menus
  were already made DPI-safe via `UiScale`. Verify at 3840×2160, 2560×1440 and 1366×768.
- Reusable prefab-in-code components: `CategoryButton`, `Card` (icon + title + status pill + cost row),
  `DetailPane` (ingredient rows w/ have/need, benefit, action button + reason), `TechNode` + edge lines
  (a simple force-free layered layout by category/tier), `ShipCard`.
- Data binding: rebuild lists on open + on `InventoryUpdated`/`KnownBlueprints`/`ShipStatus` events.
- Bilingual via the `Localizer`; en/de parity test stays green (new `ui.craft.*` / `ui.tech.*` keys).

---

## 5. Category icons (AI batch)

~10 line-style cyan icons matching the existing UI set (`gen_icons.py`): tools(drill), suit(helmet),
medicine(cross), components(gear), energy(battery), blocks(cube), ship-module(rocket), weapons(if
enabled), materials(ore), consumables(flask). Generated via the gated paid batch (one test → approve →
rest), bundled to `Resources/icons`, logged in NOTICES.

---

## 6. Phasing (within this pass)

- **P1 — foundation:** blueprint sync (server→client) + the uGUI 3-pane scaffold + shared `Card`/`Detail`
  components + category derivation. (No visual tab yet wired in beyond a skeleton.)
- **P2 — Crafting:** full crafting screen (categories, search, craftable filter, ingredient have/need,
  why-blocked, craft + feedback, disassemble), workshop-bound.
- **P3 — Tech:** the visual tech tree (nodes/edges/status), lab-bound, unlock flow + feedback.
- **P4 — Ship:** module cards w/ stat deltas, fleet cards, craftable-ship cards, console-bound.
- **P5 — polish:** AI category icons, the "missing material → where to get it" popover, lab/console
  station markers + compass guidance, transitions/glow, server-rule notes.

Each phase builds + keeps tests green; bilingual strings added per phase.

---

## 7. Open questions / risks
1. **Lab + ship-console stations** don't exist yet — confirm adding them to the starter ship (and to the
   station/settlement generators?) vs. temporarily mapping Tech+Ship to the workshop too.
2. **Mixed UI** (uGUI for these 3 tabs, IMGUI for the rest) is acceptable interim; a later pass could move
   the whole Tab menu to uGUI.
3. **Tech-tree layout** for many blueprints — start with a simple per-category column/tier layout (no
   physics); revisit if it gets dense.
4. **3D ship-expansion preview** (§6 of the concept) is deferred to a later step (cards + stat deltas
   first).
