# Giant ship scripts (`tools/giant_ships/`)

One script per GIANT SHIP (#3617): `<key>.py` with a `build()` that returns a `ships.ShipDesign` (the toolkit in
[tools/landmarks/ships.py](../landmarks/ships.py), documented in
[tools/landmarks/README.md § Giant ships](../landmarks/README.md#giant-ships)). The generator writes
`data/giant_ships/<key>.json` — a compact `StructureTemplate` with `giantShip: true`, `minTerrainGeneration: 42`,
`nameKey: giantship.<key>.name`, `weight`, `groundRow`, `style` — that the game only ever shows as a wreck, crashed on a
planet or drifting in space.

```bash
uv run --no-project python tools/gen_giant_ships.py                       # every <key>.py -> data/giant_ships/<key>.json
uv run --no-project python tools/gen_giant_ships.py --only carrier --out artifacts/gs/ships --preview
uv run --no-project python tools/gen_giant_ships.py --check               # build and check only, write nothing
uv run --no-project python tools/gen_giant_ships.py --list                # the scripts, the _demo too
uv run --no-project python tools/landmarks/preview.py artifacts/gs/ships/carrier.json --split   # look again
```

- `_demo_hull.py` shows every helper in build order (hull, decks, corridor and rooms, ladder shaft, windows, dome, hull
  doors, chests, sound, guns, conduit, engines, greebles, a ruin zone). A script whose name starts with `_` runs only
  with `--only`.
- Scripts import the toolkit as top-level modules (`from ships import ShipDesign, Mat, Mix, ship_section`); importing
  this package puts `tools/landmarks` on the path.
- The generator refuses a ship (exit code 1, no file) that is larger than 256 on an axis, has no `entrance` (standing on
  a floor with two cells of headroom), has a marker outside the ship or a `door_slide` without `axis=x|z`, or names an
  unknown block or a material token. It prints the stats line, the bounding box of the blocks, the glass share and the
  compact JSON size with a deflate estimate.
- Content rules: the game is for children — abandoned wrecks, everyone got away, nothing frightening; no names, symbols
  or recognisable designs of a real franchise; existing blocks only. The name and the Codex text are locale keys
  (`giantship.<key>.name`, `giantship.<key>.desc`), never literals.
