# Editors → World-Gen Integration & Developer-Editor Signposting — Analysis

Status: **analysis only — nothing implemented.** Two requests:

1. Build the **ship editor** and the **station / village / city (settlement) editors** so that
   structures saved in them are picked up by the **random world generator** in new games — and let the
   player choose **at world / server creation which structures get used**.
2. Make it **clear in the game menu** that the **Material editor** and the **Tech (Item & Recipe)
   editor** are **developer editors** — i.e. that those edits do **not** flow straight into a player's
   own game.

This builds on [SHIP_TYPE_EDITOR_PLAN.md](SHIP_TYPE_EDITOR_PLAN.md) and
[STATION_SETTLEMENT_EDITOR_PLAN.md](STATION_SETTLEMENT_EDITOR_PLAN.md), which are now **largely
implemented** (see §1). This doc re-grounds against the shipped code and focuses on the two *new* asks:
**runtime ingestion + per-world structure selection**, and **dev-editor signposting**.

---

## 1. What already exists (ground truth)

The editor → export → merge → stamp pipeline the earlier plans describe is **built and wired**:

### Editors (client, `client/Assets/BlocksBeyondTheStars/Scripts/`)
- **ShipEditor.cs** — free-fly build room, ~20-item palette (hull / glass / lights / doors / 8 station
  markers / engine), metadata + stats + craft-cost panel, exports to
  `persistentDataPath/ship_exports/<key>/{ship.json, layout.json}`.
- **StructureEditor.cs** — one component, two modes (`Mode.Station`, `Mode.Settlement`); palette + size
  tiers switch by mode; markers (vendor / mission_board / hangar / heal_tank / quarters / npc / loot);
  exports to `persistentDataPath/<station|settlement>_exports/<key>/{structure.json, layout.json}`.
- **MaterialEditor.cs** — paint a 64×64 tile + set mining / look / world-spawn; exports to
  `material_exports/<key>/{material.json, texture.bytes}`.
- **ContentEditor.cs** ("Item & Recipe Editor", the *Tech editor*) — define item + recipe + blueprint
  gating; exports to `content_exports/<key>/content.json`.
- All reached from **Main Menu → Editors** ([UiMainMenu.cs](../client/Assets/BlocksBeyondTheStars/Scripts/UiMainMenu.cs),
  [UiEditors.cs](../client/Assets/BlocksBeyondTheStars/Scripts/UiEditors.cs)); phases in
  `AppShell.ShellPhase` (`ShipEditor`, `StructureEditor`, `ContentEditor`, `MaterialEditor`, `Editors`).

### Content models + load
- `ShipLayout` (`Key`, `W/H/L`, `Cells[{X,Y,Z,Kind,Id}]`) and `StructureTemplate`
  (`Key`, `Name`, `Tier`, `Kind`, `W/H/L`, `Cells[{X,Y,Z,Kind,Id}]`) in
  `src/BlocksBeyondTheStars.Shared/Definitions/`.
- [ContentLoader.cs](../src/BlocksBeyondTheStars.Shared/Content/ContentLoader.cs) loads
  `data/ship_layouts/*.json`, and **optionally** `data/station_templates.json` +
  `data/settlement_templates.json` into `GameContent.StationTemplates` / `.SettlementTemplates`.
- `data/ship_layouts/` ships 3 layouts (scout / corvette / hauler). **`station_templates.json` and
  `settlement_templates.json` are not shipped yet** → both pools are empty by default.

### Merge tools (`tools/`, uv)
- `merge_ship.py` → folds an export into `data/ships.json` + `data/ship_layouts/<key>.json`.
- `merge_structure.py` → folds an export into `data/station_templates.json` **or**
  `data/settlement_templates.json`.
- `merge_material.py`, `merge_recipe.py` → fold Material / Content editor exports into
  `data/{blocks,items,recipes,blueprints,planets}.json` + locale placeholders.

### Stamping (server already consults the pools)
- `GameServerSpaceStations.StampStation` ([GameServerSpaceStations.cs:416](../src/BlocksBeyondTheStars.GameServer/GameServerSpaceStations.cs#L416)):
  if `StationTemplates` is non-empty, a seeded roll `< StructureTemplateChance` (a constant ≈ 0.35)
  stamps `StationGenerator.FromTemplate(pool[random])`; otherwise `StationGenerator.Generate(tier,…)`.
- `GameServerSettlements.StampSettlement` — same shape for `SettlementTemplates`.
- Deterministic: same world seed → same choice. Empty pool → always procedural (zero regression).

### World creation already has structure knobs
- [WorldCreationOptions.cs](../client/Assets/BlocksBeyondTheStars/Scripts/WorldCreationOptions.cs)
  exposes frequency sliders incl. **Settlements** and **Stations** (+ Wrecks, Vaults, Flora, Ore,
  Exotic, universe size, survival rules, story). `ToArgs()` emits only non-defaults as `--…` overrides.
- Flows: UI → CLI args → [ServerConfig.ApplyCommandLine](../src/BlocksBeyondTheStars.Shared/Configuration/ServerConfig.cs)
  → `WorldDescription` (frequencies) + `GameRules` → baked into `WorldMetadata` (`RulesOverride`,
  `Description`) and persisted; on reload the world keeps its own rules.

**So requests #1's plumbing mostly exists.** What's missing is exactly the *two new things the user
asked for*, plus three latent gaps. See §2.

---

## 2. Gap analysis for request #1

### Gap A — Editor output does not reach a running game without a dev rebuild
The running (local/embedded) server reads content from **`StreamingAssets/data`**
([LocalServerLauncher.cs:119](../client/Assets/BlocksBeyondTheStars/Scripts/LocalServerLauncher.cs#L119)
passes `--data <streamingAssets>/data`). Editors write to **`persistentDataPath/*_exports`**. The only
bridge is the **Python `merge_*.py` tools + a source-tree rebuild / asset re-sync**. Therefore today an
in-game saved ship/station/village is **not** usable in a new game until a developer runs a merge and
rebuilds — for *all* editors, including ship/structure. This is the core of why request #2 (signposting
dev-only editors) is even necessary, and the core obstacle to request #1 ("directly used in new games").

### Gap B — Template selection is a flat, fixed-chance random pick
`StampStation` / `StampSettlement` pick **uniformly at random from the whole pool** at a hard-coded
`StructureTemplateChance`. There is:
- **no tier matching** — a `small`-tier station slot can stamp a `colossal` template (size mismatch);
- **no per-world weighting** — every template is equally likely;
- **no way to choose *which* templates** a given world may use (all-or-nothing pool).

### Gap C — No per-world / per-server "which structures" selector
World creation can scale **how many** settlements/stations appear (frequency), but not **which authored
templates** are eligible, nor the **template-vs-procedural ratio**, nor enable/disable specific packs.

---

## 3. Design options for request #1

### 3.1 Runtime ingestion of editor structures (closing Gap A)
Three tiers, pick per how "player-facing" this should be:

- **Option A1 — Dev-only (status quo, just documented).** Keep merge + rebuild. Editors stay developer
  tooling; their output enters the game only through a content release. Cheapest; matches today.
- **Option A2 — Local user-content folder, auto-loaded (recommended for ships/structures).** Have the
  server *also* scan a writable content dir (e.g. `persistentDataPath/usercontent/{ship_layouts,
  station_templates,settlement_templates}/`) in addition to `StreamingAssets/data`, merging found
  entries into the pools at load. The editors' **Save** would (also) write there in the final
  data-shaped JSON. Then a structure built in-game appears in the *next* new world with **no Python
  step and no rebuild** — exactly "directly used by the random generator." Determinism and tests are
  unaffected (pools just have more entries). This is the smallest change that satisfies the literal ask.
- **Option A3 — Bake selected exports into the save at creation.** When creating a world, copy the
  chosen templates into the save folder so the world is self-contained / shareable. More work; only
  needed if worlds must be portable between machines.

`merge_*.py` stays the path for **shipping** content into the official `data/` (curated, reviewed,
localized). A2 is the *player/test* path; the merge tools are the *release* path. They coexist.

### 3.2 Tier-aware, weighted selection (closing Gap B)
- Give `StructureTemplate` an optional **`Weight`** and treat **`Tier`** as a filter: build
  `StationTemplatesByTier[tier]` / `SettlementTemplatesByTier[tier]` pools in `GameContent`, and at the
  stamp site pick from the **tier-matching** sub-pool (weighted), falling back to procedural when that
  sub-pool is empty. Removes the size-mismatch bug and makes authoring per-tier landmarks meaningful.
- Make `StructureTemplateChance` a **tunable** (constant today) — see §3.3.

### 3.3 Per-world / per-server "which structures" selection (closing Gap C, the core ask)
Mirror the existing frequency-slider plumbing (`WorldCreationOptions → ToArgs → ServerConfig →
WorldDescription → WorldMetadata`). Add, at increasing fidelity:

- **Level 1 — a template ratio slider** per structure kind: "Authored structures" = Off / Rare / Mix /
  Often (drives `templateChance`). Off = pure procedural (today's default); higher = more authored
  landmarks. One float per kind in `WorldDescription`, one `--station-templates` / `--settlement-
  templates` CLI arg, one slider row in the creation panel. Smallest UX that satisfies "choose how much
  the authored ones are used."
- **Level 2 — a structure pack picker.** Group templates into named **packs** (a `pack`/`tags` field on
  `StructureTemplate`, e.g. "Vanilla", "My builds", "Alien ruins"). The creation panel lists packs with
  checkboxes; the world stores the enabled pack set in `WorldDescription`; the stamp site filters the
  pool to enabled packs. This is the natural "choose which structures" UI and composes with A2 (the
  user's own builds form a "My builds" pack).
- **Level 3 — per-template allow-list** (advanced page only): toggle individual templates. Probably
  overkill; Level 2 covers the intent.

**Server side:** the same fields live in `ServerConfig`/`server.json` so a dedicated server host sets
the structure policy once; `ApplyCommandLine` already accepts world overrides, so adding
`--station-templates`, `--settlement-templates`, `--structure-packs "vanilla,mybuilds"` is consistent.

### 3.4 Suggested phasing for request #1
1. **P1 — tier-aware weighted pools** (§3.2) + ship one sample `station_templates.json` /
   `settlement_templates.json` so the pools are non-empty and testable. Pure server, low risk.
2. **P2 — `templateChance` as a world option** (§3.3 Level 1): slider + CLI + `WorldDescription` field +
   persistence. Reuses the exact frequency-slider pattern.
3. **P3 — runtime user-content folder** (§3.2 Option A2): server scans `persistentDataPath/usercontent`;
   editors save data-shaped JSON there. Now in-game builds appear in new worlds without a rebuild.
4. **P4 — structure packs** (§3.3 Level 2): pack tagging + creation-panel pack picker + server config.
5. **P5 — (optional) bake-into-save** (A3) for portable/shareable worlds.

---

## 4. Request #2 — signposting Material & Tech editors as developer tools

### 4.1 Why these two differ from ship/structure editors
- **Ship / Station / Settlement** editors produce **per-world content** (structures dropped into
  generation). With §3.1-A2 they can become genuinely player-usable ("build a base type, see it appear
  in your next world").
- **Material editor** and **Tech (Item & Recipe) editor** change the game's **fundamental content +
  balance** — new blocks/materials, items, recipes, blueprint costs, texture atlas. These are **global,
  cross-cutting, and require a merge + rebuild** (atlas regeneration, id assignment, locale keys). They
  cannot meaningfully be hot-loaded into one save and are inherently a **developer / modding** activity.
  The user's instinct to fence them off is correct.

### 4.2 Current signposting (what's already there)
- They live under a separate **Editors** submenu (not the main play flow).
- `ui.editors.info_body` mentions running `tools/merge_*.py`.
- App splash reads `PROTOTYPE / DEV BUILD`; version `0.20.0-dev`.

This is weak: a non-developer reading "Material Editor" reasonably expects in-game effect, and the merge
hint is buried in one info paragraph.

### 4.3 Options to make the developer-only nature explicit
Bilingual (DE + EN) for any visible string (per project rule). In rough order of effort:

- **S1 — Group + label as a "Developer / Creator Tools" section.** Within the Editors submenu, split
  into two groups: **"In-game creator"** (Avatar, and — once §3.1-A2 lands — Ship/Station/Town that feed
  your worlds) and **"Developer content tools (rebuild required)"** (Material, Item & Recipe). Add a
  short subtitle to the dev group: *"For mod/game developers — changes need a merge + rebuild and do not
  appear in your current game."* Locale keys e.g. `ui.editors.devtools.title` / `.note`.
- **S2 — Per-editor banner.** When entering MaterialEditor / ContentEditor, show a persistent header
  badge: **"DEVELOPER TOOL — exports to disk; not applied to your game until merged + rebuilt."** One
  label in each editor's IMGUI, reusing the existing status-line styling.
- **S3 — A confirm-on-first-open dialog** explaining the export-then-merge workflow (with a "don't show
  again" flag in `ClientSettings`). Strongest clarity; mildly intrusive.
- **S4 — Gate behind a "Developer mode" toggle** in Settings (off by default). The two dev editors are
  hidden unless enabled; enabling shows the explanation once. This most strongly communicates "not for
  normal play," at the cost of a settings flag + conditional menu build.
- **S5 — Visual de-emphasis + icon.** A wrench/`</>` "dev" icon + muted styling on the two entries vs
  the creator editors, so the distinction is legible at a glance.

**Recommended combination:** **S1 + S2** (clear grouping with an explanatory note, plus an in-editor
banner) as the baseline; add **S4** if these should be hidden from ordinary players entirely. S1+S2 are
low-risk, purely additive UI + locale work.

### 4.4 Note on consistency
If §3.1-A2 is **not** adopted, then ship/structure editors *also* require a merge + rebuild, and the
honest signpost is "all editors are developer tooling." The clean story the user is reaching for —
"material/tech are dev-only, the others feed your game" — only becomes *true* once runtime ingestion
(§3.1-A2) exists for ships/structures. So **request #2's framing depends on a request #1 decision**:
either adopt A2 (and the two-group split in §4.3-S1 is accurate), or keep everything dev-only (and label
all editors uniformly as developer tools).

---

## 5. Key open decisions (for the user, before any implementation)

1. **Runtime ingestion (§3.1):** A1 (keep dev-merge only), **A2 (auto-load a user-content folder —
   recommended)**, or A3 (bake into save)? This determines whether in-game-built structures truly appear
   in new worlds without a rebuild, and whether §4's two-group framing is accurate.
2. **Selection granularity (§3.3):** ratio slider (Level 1), **pack picker (Level 2 — recommended)**, or
   per-template allow-list (Level 3)?
3. **Tier handling (§3.2):** add `Weight` + tier-matched sub-pools now (recommended), or keep the flat
   random pick?
4. **Dev-editor signposting (§4.3):** S1+S2 (label + banner), or also S4 (hide behind a Developer-mode
   toggle, off by default)?
5. **Scope of "structures":** does "city" mean a new settlement tier beyond the existing
   hamlet/village/town/city, or just authoring large templates at the existing `city` tier?
