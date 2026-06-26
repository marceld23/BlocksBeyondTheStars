# Matter Converter ("Transmuter") — Design & Implementation

Status: **IMPLEMENTED** (branch `feat/matter-converter`, local commits only). Needs a local Unity
build to import the new assets and to confirm the client surfaces the station label.

## Goal

Give the player a sink for the effectively-infinite "trash" terrain (sand, dirt, mud, stone, basalt,
ash) by letting them craft it back into more valuable resources at a new gated station — without
undermining mining progression, depth-gating, or the market-trade economy.

## Balance invariants (pinned by `MatterConverterTests`)

- **I1 — No Tier-3 output.** The converter never mints diamond / uranium / tungsten / platinum /
  neodymium. Those stay mining-exclusive endgame ores.
- **I2 — No from-nothing bootstrap.** Every synthesis burns an `energy_cell_1` (Tier-1) or `power_cell`
  (Tier-2), both of which need already-mined copper/silicate/carbon (and lithium/cobalt) to make.
  Only the trash → `matter_dust` compaction step is "free", and it is deliberately steep.
- **I3 — Lossy intermediate.** Trash never converts straight to ore. It goes through `matter_dust`, the
  single tunable bottleneck, so the infinite source is decoupled from the valuable output.

## What it is

A new crafting station `Transmuter` (mirrors the Detoxifier pattern): a placeable block `matter_forge`
for bases **and** a ship module `transmuter`, both unlocked by the `matter_forge` blueprint
(prereq: `refinery`). Crafting stays instant — cost comes from ratios + energy cells, not from time
(the engine has no timed processing and we did not add any).

### Stage 1 — `matter_forge` blueprint

- Compaction: `64×` sand/dirt/mud/ash → `1 matter_dust`; `48×` stone/basalt → `1 matter_dust`.
- Synthesis (Tier-1): `3–4 matter_dust + 1 energy_cell_1` → `2×` iron_ore / copper_ore / silicate / carbon.

### Stage 2 — `matter_resynth` blueprint (prereq: `matter_forge`)

- Resynthesis (Tier-2): `10–12 matter_dust + 1 power_cell` → `1×` titanium_ore / silver_ore / lithium / cobalt_ore.
  Always costlier than mining.

Ratios are intentionally steep starting values; tune in `data/recipes.json` after playtest.

## Files touched

| Area | File | Change |
|------|------|--------|
| Block | `data/blocks.json` | `matter_forge` block |
| Items | `data/items.json` | `matter_forge` (block item) + `matter_dust` (material) |
| Module | `data/ship_modules.json` | `transmuter` module |
| Blueprints | `data/blueprints.json` | `matter_forge` + `matter_resynth` |
| Recipes | `data/recipes.json` | device build + 6 compaction + 4 synth + 4 resynth |
| Locales | `data/locales/{en,de}.json` | block/item/module/blueprint strings + `ui.craft.station_transmuter` |
| Enum | `src/…/Shared/Definitions/Enums.cs` | `CraftingStation.Transmuter` |
| Server gate | `src/…/GameServer/GameServer.cs` | `StationAvailable` → block `matter_forge` / module `transmuter` |
| Client tile | `client/…/BlockTextureAtlas.cs` | `matter_forge` base colour (violet) |
| Client audio | `client/…/CraftingTechShipUI.cs` | `matter_synth` cue on a transmuter craft |
| Assets | `client/Assets/Resources/icons/item_{matter_dust,transmuter}.png` | OpenAI icons |
| Assets | `client/Assets/Resources/audio/matter_synth.mp3` | ElevenLabs SFX |
| Asset gen | `tools/ai-assets/gen_item_icons.py` | manifest entries for the two icons |
| Tests | `tests/…/MatterConverterTests.cs` | invariants + functional craft |

## Why the client needed almost no code

- Recipes are listed by their **output item's category**, not by station → transmuter recipes show up
  automatically.
- The per-recipe station label is data-driven: `ui.craft.station_<station>` → one locale key.
- The block tile is procedural from the block `color` (no atlas art), as long as the block's numeric id
  stays `< 256`. Adding one block keeps the count well under that.
- The craft is server-authoritative via `StationAvailable`; the client gate matches the existing
  forge/detoxifier behaviour with no new logic.

## Notes / caveats

- Adding a block is a **palette break**: `AssignBlockIds` numbers blocks by ordinal key order, so a new
  key shifts the ids of later blocks and invalidates pre-existing saved chunks — consistent with every
  prior block addition.
- Lore: matter transmutation is not in the VEGA canon; framed purely functionally as salvaged tech in
  the blueprint/item descriptions (no story-engine changes).
- Follow-up tuning candidates: compaction ratios, whether to accept more trash types (snow/ice on cold
  worlds), and whether Tier-2 resynthesis should require a placed station only (no ship module).
