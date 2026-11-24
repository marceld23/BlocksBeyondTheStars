# Calm tile set (2026-11)

The scripts that made the game's current block, creature, suit and device tiles: every bundled tile in
`client/Assets/Resources/textures/*.bytes` except the 16 coat pattern masks `creature_pattern_*` and the 5 horn ramps
`creature_horn_*` (583 tiles; per-key origin in `../texture_provenance.json`, licence notes in `NOTICES.md`). The calm
style is soft forms instead of pixel grit: the block atlas does not calm these tiles any further (`CalmTileRule`,
`BlockTextureAtlas.CalmTiles.cs`).

They are a record of how the set was made, not a build step. **Image generation costs money and is not reproducible**
(the image API takes no seed): a second run makes different pictures. No test and no build runs these scripts; the
committed `.bytes` are the source of truth, and `texture_provenance.json` marks every key so the other bundlers in
`tools/ai-assets` skip it unless `--force`. The whole set cost about 18 US dollars with `gpt-image-2.6-sunburst` at
quality `low`.

## Folders

| Path | What |
| --- | --- |
| `client/Assets/Resources/textures` | where the finished tiles go — written directly, there is no second tile folder |
| `tools/ai-assets/out/calm_tiles/previous` | the tile set **before** the calm set: the reference of every redraw and every colour |
| `tools/ai-assets/out/calm_tiles/forms`, `redraws`, `devices` | raw generated images (git-ignored, never committed) |
| `tools/ai-assets/out/calm_tiles/built`, `sheets` | finished tiles as PNG and the contact sheets for the approval |

`previous` must hold the tiles of the commit before the calm set (redrawing today's tiles would redraw a redraw):

```bash
mkdir -p tools/ai-assets/out/calm_tiles/previous
git archive <commit before the calm set> client/Assets/Resources/textures \
  | tar -x --strip-components=4 -C tools/ai-assets/out/calm_tiles/previous
```

## Order

Run from the repository root with `uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/<script>`.
The OpenAI key is read from `tools/ai-assets/.env`. **Paid** steps are marked; every paid script skips a picture that
already exists in `out/`, and has `--dry-run`.

1. `gen_calm_tiles.py` — **paid.** Greyscale forms of the ground, rock, bark, plank and metal surfaces, and the masks of a
   second material (ore in rock, moss on stone, the marks of a birch), one prompt each, into `forms/`.
2. `make_sheet.py` — contact sheets of the candidate forms. Also the shared helpers: seam healing (Moisan's periodic part,
   a cross-fade where a structure still breaks at the border), the form-times-colour-ramp colouring.
3. `restyle_tiles.py` — **paid.** Redraws a previous tile in the calm style with the tile itself as the image-edit
   reference, into `redraws/`: the picture tiles by default, `--surfaces` (hides, suit, tool materials; the liquids with
   `--surfaces --only water,lava,gas,oil,gt_fluid,ressaw`), `--cutouts` (plants, leaves, critters), `--mural` (the 4 x 2
   mural as one picture).
4. `build_tiles.py --bundle` — builds every tile: a form coloured with the previous tile's colours (one or two materials),
   or a redraw reduced to 64 px (a cut-out keeps the previous alpha exactly, a surface wraps and keeps its mean colour).
   Writes the tiles into `textures` and the sheets.
5. `make_sod.py` — the grass sides `face_grass_side`, `face_alien_grass_side`, `face_mycelium_side`: the calm top tile
   hanging over the calm dirt tile. No model.
6. `make_mushroom_cap.py` — `mushroom_cap` and its gills `face_mushroom_cap_bottom`, drawn by calculation. No model.
7. `make_geyser_hole.py` — `geyser_hole`, the sinter ring cut out of the calm `geyser_vent` tile (a cut-out). No model.
8. `heal_built_seams.py` — seam repair of `fifi_stem`, `saguaro`, `paul_stem`, `paul_petals`, `burger_bun`, and
   `burger_patty` drawn anew by rule. No model.
9. `gen_device_tiles.py` — **paid.** Material pictures for the device forms; only `devform_embers`,
   `devform_liquid_teal` and `devform_liquid_green` are kept from this step.
10. `make_raster_tiles.py` — the device tiles drawn by rule on the 8 px micro-cell grid: `devform_metal_dark`,
    `devform_metal_light`, `devform_deck`, `devform_screen`, `devform_vent`, `devform_lamp_amber`. No model.

11. `rock_tiles.py --generate` — **paid** (round 6, 2026-11-24, #4000). Players found the calm rocks flat (granite one
    pink with black dots, ores like paint on the rock), so the 39 rock and ore tiles are redrawn as natural stone of up
    to three materials: an image edit per tile with the `previous` tile as the reference (bedrock and obsidian, which had
    no bundled tile, from text), into `out/calm_tiles/rocks/`. `rock_tiles.py --build --bundle` reduces them to 64 px,
    heals the seams, keeps the previous mean colour, sets the contrast (rocks 0.6 × the previous tile's, ores 0.85 × and
    never below the calm tile's, so they stay easy to spot) and writes the approval sheets (previous | calm | new) and
    the tiles. It keeps a copy of the calm tiles it replaces in `out/calm_tiles/rocks_calm/`, so a later build compares
    with them. `build_tiles.py --bundle` leaves these keys alone (their provenance names `rock_tiles.py`).

12. `make_mars_tiles.py --bundle` — Mars's ground (#4018): `red_sand` from the calm `sand` tile, `red_rock` from the
    natural `stone` tile, each recoloured pixel by pixel to its block's data colour (brightness relative to the tile's
    mean, a contrast factor per tile), read from a fixed git revision so a rerun gives the same tiles. No model.

`tilelib.py` holds the paths and the sheet helper. After a change of the set: update `texture_provenance.json` and
`NOTICES.md`, and run the tile tests (`BlockTileAlphaTests`, `CreatureCoatTilesTests`, `CalmTileRuleTests`, …).
