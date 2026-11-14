# Landmark toolkit (`tools/landmarks/`)

Scripts that build the giant **landmark** templates (`data/landmarks/<key>.json`, see
[docs/developer/LANDMARKS.md](../../docs/developer/LANDMARKS.md)) out of geometric primitives, and a renderer to look at
the result without Unity. Python 3.10+, standard library only.

| file | what it is |
|---|---|
| `geom.py` | `Design` — the cells of one landmark, every primitive, markers, validation, `write()`; `Mat`, `Mix`, `Palette`, `Sculpture` |
| `solids.py` | pure geometry: functions that return **sets of cells** (cylinders, rings, domes, arches, lines, cables, stars, caves ...) |
| `shapecode.py` | the per-cell shape descriptor, mirrored from `BlockShape.cs` (`pack`, ramps, beams, stalactites, turning and mirroring) |
| `blockfont.py` | the 5 x 7 block font, mirrored from `MonumentFont.cs` (the stone text tablets' font) |
| `templatefile.py` | reads template files (compact rows), the stats line, the block / loot-table lists |
| `lightmount.py` | the light-mount pass `write()` runs (#3626): every unshaped torch / lantern gets the #2683 wall / floor / ceiling mount of the block that holds it (a torch on a wall first, never the ceiling; a lantern ceiling, floor, wall); an unheld light is printed as a warning |
| `preview.py` | renders a template to a PNG: front / side / top views, an isometric view, cut-away sections |
| `selftest.py` | checks every primitive, the C# mirrors, the samples' bytes, the PNG writer and the ship helpers |
| `ships.py` | `ShipDesign` — the GIANT SHIP toolkit (see [Giant ships](#giant-ships)): hull, decks, rooms, doors, domes ... |
| `shipgeom.py` | pure ship geometry: cross-sections, the loft, `Hull` (inside / shell / surface queries), tubes along curves |
| `<key>.py` | one script per landmark, with a `build()` that returns its `Design` |

```bash
uv run --no-project python tools/landmarks/leaning_tower.py          # build one landmark -> data/landmarks/leaning_tower.json
uv run --no-project python tools/gen_landmarks.py                    # the three samples + every tools/landmarks/<key>.py
uv run --no-project python tools/gen_landmarks.py leaning_tower      # only these keys
uv run --no-project python tools/landmarks/preview.py leaning_tower --cut y=40 --cut z=12
uv run --no-project python tools/landmarks/selftest.py               # 27 checks, exit code 1 on a failure
uvx ruff check tools/landmarks
```

`gen_landmarks.py` writes the three `pinOnly` samples with this same `Design` (byte for byte as before) and then runs
the `build()` of every other `tools/landmarks/<key>.py` (the toolkit's own modules are skipped).

## Writing a landmark script

```python
"""The leaning tower: eight storeys of arcaded galleries round a stair core, leaning a little more with each storey."""
import solids
from geom import Design, Mat, Mix, Palette


def build() -> Design:
    d = Design("leaning_tower", "Leaning Tower", nameKey="landmark.leaning_tower.name", ruin="always",
               ruinProfile="masonry", ruinStrength=0.4, requires=["atmosphere"], weight=1)
    p = Palette(stone=Mix("travertine", ("sandstone", 0.1), seed=1), core=Mat("travertine", keep=True),
                glass=Mat("glass"))
    cx, r = solids.span(0, 24)                     # a disc 24 wide starting at x = 0: centre 11.5, radius 12
    storey = 13
    d.cylinder(cx, cx, 0, 2, r, p.stone)          # plinth
    for k in range(8):
        y0 = 3 + k * storey
        wall = d.ring(cx, cx, y0, y0 + storey - 1, 8, 2, p.stone)          # core wall, 2 thick
        d.disc(cx, cx, y0, 11, p.stone)                                    # gallery floor
        for x, z, _ in solids.Ellipse(cx, cx, 10, 10).points(24):          # 24 round columns
            d.column(round(x), round(z), y0 + 1, y0 + storey - 2, "travertine", base="travertine", capital="travertine")
        d.ring_arcade(wall, solids.Ellipse(cx, cx, 7, 7), y0 + 2, 8, 2, 6)  # 8 arched openings into the core
    d.spiral_stair(cx, cx, 3, 3 + 8 * storey, 2.5, 6.5, p.core, steps_per_turn=13, newel=p.core)  # kept: keep=True
    d.lean(8, 0, 3, 3 + 8 * storey, storey=storey)  # 8 blocks toward +x at the top, one storey at a time
    d.normalize()                                    # the lean may push cells below x = 0
    d.chest(12, 4, 3, table="ruin_rich")
    return d


if __name__ == "__main__":
    build().write()
```

`write()` validates (the same rules `LandmarkRules.Problems` applies at load — a template it objects to is dropped from
the game), prints warnings (an unknown block, a marker in a block cell, an unknown loot table), writes the file with LF
line endings and prints the stats line:

```
leaning_tower: 30x111x24 | 17,062 cells (17,062 blocks, 0 cavity, 2,600 keep, 2,140 shaped, 0 tinted, 0 glowing) | 1 markers (chest 1) | palette 8, rows 2241 | 70,391 bytes
```

Rules for the content (from the spec): no child's name anywhere — not in a script, a comment, a file name or a text;
"Blocks King" keeps its name but no colours or logo of a real burger chain; every size at most 256 per axis.

## Coordinates and cells

- Template cells: **x** = width, **y** = up, **z** = length. Written files must have every cell at >= 0 (call
  `normalize()` after a lean or a mirror that went negative); the size is the largest coordinate + 1 per axis.
- **Ground row** (`groundRow`): the template row that lies on the ground. Above it, a column that carries something is
  cleared to air and stood on a plinth by the stamp, so an **empty** cell is air. Below it an empty cell keeps the
  planet's **rock** — rooms, tunnels and caves there must be `CAVITY` cells (air). `translate()` moves the ground row
  with the design.
- A cell holds a block (key, tint, glow, shape, keep) or a cavity. **Markers** are separate cells with a settings line;
  the marker helpers remove a block in their cell (a marker's cell is never filled) but keep a cavity there.
- **keep**: the ruin pass never touches the cell, and what hangs on it stays. Mark stair cores, the golden bird, a
  sign. Shaped pieces are never eroded but fall with what carries them. The settle holds things together through
  faces and edges (not corners) — the lines and trusses here are edge-connected for that reason.
- Walking: a player is 2 blocks tall and steps up 0.6 blocks without jumping, so every staircase uses the `stairs` form
  (two half steps per block). `stairs()` and `spiral_stair()` do that by default.

## Materials

Wherever a method takes `m`:

| value | means |
|---|---|
| `"travertine"` | a block key |
| `Mat(block, tint=0, glow=0, shape=0, keep=False)` | a block with its modifiers; `.kept()`, `.tinted(c)`, `.glowing(c)`, `.shaped(d)`, `.with_(**kw)` |
| `Mix("travertine", ("sandstone", 0.2), seed=3)` | weighted blocks (or `Mat`s), picked per cell by a hash — deterministic |
| `lambda x, y, z: ...` | any of these per cell, or `None` = leave the cell |
| `CLEAR` | remove the cell from the design |
| `CAVITY` | carve the cell to air (underground) |

Extra keyword arguments (`tint=`, `glow=`, `shape=`, `keep=`) override the material's own. Colours are `0xRRGGBB`
(`rgb(r, g, b)`, `hexcolor("#c0362c")`); 0 = none. `Palette(wall=..., trim=...)` is a dict with attribute access.
Material tokens (`@wall`, `@accent`, `@roof`, `@floor`, `@path`) follow the planet like in settlement modules.

`where=` on every filling method: `None` (overwrite), `"empty"` (only empty cells), `"blocks"` (only existing block
cells — swap a wall's cells for glass), `"not_cavity"`.

Every shape method **returns the set of cells it wrote**, so the result can be reused: cut windows into the wall you
just made, `keep()` the stair you just placed, scatter moss on a wall's top course.

## Header fields

`Design(key, name, **header)`; `configure(**fields)` sets more later. They are written as given, in that order.

| field | values |
|---|---|
| `nameKey` | `landmark.<key>.name` (the locale key of the display name) |
| `ruin` | `always` (derived into a ruin per seed) / `never` (as built) |
| `ruinProfile`, `ruinStrength` | `masonry` / `lattice` / `underground`; 0..1 (0 = the profile's default) |
| `ruinZones` | add with `ruin_zone(x0, y0, z0, x1, y1, z1, profile="", strength=0.0)` — at most 16; each decays with its own profile and strength, cells in no zone with the template's; where zones overlap the first wins |
| `seat` | `""` (the usual search) / `"span"` (a bridge: an unbroken gap of water or a valley — at least 40 blocks or a sixth of the long axis, its middle in the middle third — both ends on the banks at the ground row = the banks' level; long axis >= 12). Put the ground row UNDER everything that spans (deck and trusses), or the ruin pass anchors the span in it and nothing can fall |
| `protection` | `auto` (default: intact landmarks protected, ruins open) / `protected` / `open` |
| `requires` | `["atmosphere"]`, `["atmosphere", "inhabited"]` |
| `groundRow` | the row on the ground (0 = the whole thing stands on the ground) |
| `planetTypes` | planet-type keys it may stand on (empty = all) |
| `weight`, `pinOnly`, `pack` | as for every template |

Unknown fields are passed through with a warning.

## `Design` reference

Positions are `(x, y, z)` tuples where a method takes `p0`, `p1`; centres may be fractional (`.5` for an even width).
"Rows y0..y1" are inclusive.

### Cells
| method | |
|---|---|
| `block(x, y, z, block, tint=0, glow=0, shape=0, keep=False)` | one block (original API; `block` may also be a `Mat`) |
| `put(x, y, z, m, **kw)` | one cell of any material |
| `fill(cells, m, where=None, **kw)` | a set of cells (from `solids` or another method) |
| `cavity(x, y, z)`, `clear(x, y, z)` | one cavity cell / remove a cell |
| `box(x0, y0, z0, x1, y1, z1, m)`, `shell(...)`, `carve(...)` | solid box / its four side walls (no floor, no roof) / cavity box (original API) |
| `get(x, y, z)`, `is_block(...)`, `block_cells(block=None)`, `cavity_cells()`, `bounds()` | reading |
| `restyle(cells, block=, tint=, glow=, shape=, keep=)`, `paint(cells, tint)`, `keep(cells)`, `keep_box(...)` | change existing block cells (None = leave the field) |
| `replace(old_block, m, cells=None)` | swap one block for a material |
| `scatter(cells, m, density, seed=1)` | a hashed share of the cells (moss, rubble, sesame seeds) |

### Round solids
| method | |
|---|---|
| `cylinder(cx, cz, y0, y1, r, m, rz=None, wall=0)` | vertical cylinder (elliptic with `rz`); `wall` > 0 = a tube open at both ends |
| `ring(cx, cz, y0, y1, r, wall, m, rz=None)` | a ring wall `wall` thick — an amphitheatre's facade, a curtain wall |
| `disc(cx, cz, y, r, m, rz=None)` | one row of a disc (floors, roofs, burger layers) |
| `cone(cx, cz, y0, y1, r0, m, r1=0.5, wall=0, aspect=1.0)` | cone / frustum from r0 on row y0 to r1 on row y1 (y1 < y0 = upside down); hollow = open at the wide end |
| `dome(cx, cz, y0, r, m, ry=None, rz=None, wall=0)` | upper half-ellipsoid standing on row y0 up to row y0 + ry; hollow = no floor |
| `sphere(cx, cy, cz, r, m, wall=0)`, `ellipsoid(cx, cy, cz, rx, ry, rz, m, wall=0, ymin=None, ymax=None)` | balls, optionally cut to rows |
| `capsule(p0, p1, r0, m, r1=None)` | a rounded, optionally tapering cylinder between two points (limbs, horns) |
| `column(x, z, y0, y1, m, base=None, capital=None, round_shape=True)` | a one-cell column in the `cylinder` form |
| `prism(cells2d, y0, y1, m)` | extrude a 2-D shape (`solids.star`, `solids.polygon`, `solids.disc`) |
| `wall_line(x0, z0, x1, z1, y0, y1, thickness, m)`, `polygon_wall(points, y0, y1, thickness, m, closed=True)` | straight walls at any angle |

Radii: a cell belongs when its centre is within the radius. `solids.span(x0, D)` gives the centre and radius of a disc
D cells wide starting at x0. Walls up to 3 thick are cut by erosion (at least `wall` cells everywhere, closed); thicker
walls analytically, so a thick elliptic wall keeps its shape.

### Openings
| method | |
|---|---|
| `arch(axis, u0, y0, a0, a1, width, height, kind="round", rise=None, m=CLEAR)` | an opening you walk through along `axis`: `'z'` = the wall lies in x-y, the opening spans x from u0; `'x'` = spans z from u0; sill on row y0; through a0..a1 along the axis. Kinds: `round`, `segmental` (rise, default width/4), `pointed` (default equilateral), `flat` |
| `arcade(axis, u0, y0, a0, a1, count, width, height, pier, kind, rise, m=CLEAR)` | `count` arches with `pier` cells between |
| `ring_arcade(cells, ellipse, y0, count, width, height, kind="round", rise=None, phase=0.0, m=CLEAR)` | `count` arches cut radially, evenly around a ring wall (`ellipse` = usually the wall's centre line) |
| `doorway(x, y, z, width=2, height=3, along="x", door="hinge")` | a rectangular opening with a door marker in its bottom cell (`None` = open) |
| `battlements(cells, m, period=2, height=1, ellipse=None)` | merlons on the top course of a wall |

Pass another material instead of `CLEAR` to fill the opening's shape (glass in an arched window, a cavity tunnel).

### Lines, trusses, cables
| method | |
|---|---|
| `line(p0, p1, m)` | edge-connected line of full cells |
| `member(p0, p1, m, style="auto")` | a slim member: `auto` = face-connected, straight runs are `post` / `beam` forms, turns are cube nodes; `thin` = posts or beams along the main axis; `solid` = cubes |
| `polyline(points, m, style="solid")` | members through consecutive points |
| `truss(p0, p1, m, size=3, panel=4, brace="zigzag", ties=True, style="auto", brace_style="solid", brace_m=None)` | a box lattice: 4 chords at the corners of a size x size section, a tie frame every panel, a diagonal on each face (`zigzag`, `x`, None) — Eiffel legs, girders |
| `cable(p0, p1, sag, m, kind="parabola", style="auto")` | a hanging cable `sag` below the chord at its middle (`catenary` for a free chain; negative sag = an upward arc); returns the ordered cells |
| `hangers(p0, p1, sag, deck_y, m, every=4, kind="parabola", skip=0)` | vertical posts from that cable down to the row above `deck_y`, every `every` cells, none within `skip` of the ends |

### Stairs and pyramids
| method | |
|---|---|
| `stairs(x, y, z, dx, dz, steps, m, width=1, fill_below=False, shaped=True)` | climbs one row per cell toward (dx, dz), `width` to the right-hand side (+x for a z-stair, +z for an x-stair); returns the cell beyond the top step |
| `spiral_stair(cx, cz, y0, y1, r_in, r_out, m, steps_per_turn=16, ccw=True, start=0.0, newel=None, clear_above=3, shaped=True)` | a helix in the ring r_in..r_out, one row per step; clears `clear_above` rows above every tread first (floors get their stairwell hole); the first cell of each step is a `stairs` form; returns the tread cells |
| `pyramid(x0, z0, y0, base_x, m, base_z=None, step=1, rise=1, top=1, wall=0, smooth=False, corner="pyramid")` | inset `step` every `rise` rows to a top `top` wide; `smooth` (45 degrees) dresses each layer's rim with ramps rising inward and the corners with `corner`; a wider top stays a flat platform; `wall` = a crust with a solid top (chambers inside) |

### Underground
| method | |
|---|---|
| `room(x0, y0, z0, x1, y1, z1, floor=None, walls=None, ceiling=None, cavity=True)` | carve a box to air with optional floor row below, ceiling row above, one-cell lining (`cavity=False` = just cleared, for rooms above ground) |
| `tunnel(points, width=3, height=3, floor=None, frame=None, every=4, light=None, light_every=0, track=None)` | through (x, y, z) points that differ along x or z only (y = walkway row): carved, floor row, timber frames (posts + cap) every `every` cells, lights set into the side wall, a track down the middle |
| `shaft(x, z, y_bottom, y_top, width=1, ladder=None, ladder_side=(0, 1))` | a vertical shaft with a ladder up one wall |
| `cavern(cx, cy, cz, rx, ry, rz, roughness=0.3, seed=1, scale=6.0, floor=None)` | an irregular cave (noisy ellipsoid, flat `floor` row) carved to air; returns its cells |
| `dripstones(cave, m="dripstone", density=0.04, max_len=5, seed=1, up=True, down=True, keep_clear=())` | stalagmites from the cave's floor cells and stalactites from its ceiling, each ending in a cone (hanging cone = up face turned down) |
| `dripstone(x, y, z, length, m="dripstone", hanging=False)` | one of them |

### Text and statues
| method | |
|---|---|
| `text(s, x, y, z, m, dir="+x", scale=1, depth=1)` | block letters, baseline on row y; `dir` is the reading direction: `+x` reads from -Z, `-x` from +Z, `+z` from +X, `-z` from -X; depth goes away from the reader |
| `Design.text_size(s, scale=1)` | (width, height) — centre a sign with it |
| `Sculpture()` | `.ellipsoid(c, rx, ry, rz, m)`, `.sphere(c, r, m)`, `.capsule(p0, p1, r0, m, r1=None)`, `.box(...)`, `.cylinder(...)`, `.cone(...)`, `.add(cells, m)`, `.cut(cells)`, `.mirror(axis, at)` (adds a mirrored copy of every part so far), then `.write(design, wall=2, open_bottom=False)` writes only its outer `wall` cells, each in its part's material (later parts paint over earlier ones) |

### Moving and copying
`select` is `None` (everything), a 6-int box `(x0, y0, z0, x1, y1, z1)` or a set of cells. Shaped cells turn and mirror
with the building; markers come along.

| method | |
|---|---|
| `translate(dx, dy=0, dz=0)`, `normalize(y=False)` | move everything (zones and ground row too); shift the low corner to 0 |
| `copy(select, dx, dy, dz)`, `move(select, dx, dy, dz)` | |
| `mirror(axis, at, select=None, copy=True)` | across the plane axis = at (x' = 2 at - x; `.5` between two cells) — build one half, mirror it |
| `rotate(turns, cx, cz, select=None, copy=False)` | quarter turns about a vertical axis (one turn takes +Z to -X); cx, cz both whole or both .5 |
| `stamp(part, x, y, z, turns=0, mirror=None)` | write a `Design.part()` turned / mirrored with its low corner on (x, y, z) — towers, columns, statues used many times |
| `shear(offset, y0=None, y1=None)` | shift every row y by `offset(y) -> (dx, dz)` |
| `lean(dx, dz, y0, y1, storey=None)` | rows from y0 lean up to (dx, dz) at y1; with `storey` each storey shifts as one block |

### Markers
| method | settings line |
|---|---|
| `chest(x, y, z, table=None, items=None, chance=None, block=None, hidden=False)` | `items=iron_plate:2-4,gold_ingot:1;table=mine_ore;chance=0.7;block=crate;hidden=1` (neither items nor table = `ruin_common`) |
| `sound(x, y, z, sound, radius=64, volume=0.6)` | `sound=<loop>;radius=..;volume=..` — silent at the radius, full near the centre |
| `den(x, y, z, species="native", count=3, radius=10)` | `species=soundi;count=4;radius=30` |
| `vendor(x, y, z, theme, inside=False, name=None)` | `theme=burgers;inside=1` |
| `door(x, y, z, kind="hinge")` | marker `door_hinge` / `door_slide` / `door_energy`, the opening measured from the solid cells around it |
| `npc(x, y, z, post="npc", config="")` | a resident or a profession post (`doctor`, `grocer`, ...) |
| `marker(x, y, z, marker, settings="")` | anything else (original API; does not free the cell) |

A ruin keeps only chests, loot markers, sounds and dens; vendors, doors and residents need an intact landmark.

### Output
`problems()` -> (errors, warnings); `template()` -> the dict; `stats()` -> the line; `write(out=None, check=True)` ->
the path (`out` = a folder, default `data/landmarks`). `Design.load(key_or_path)` reads an existing template (also an
editor export) back into a `Design`.

## Shape codes (`shapecode`)

`pack(shape, yaw=0, up=UP_PY)` = `ShapeCode.Pack`: `((up & 7) << 8) | ((shape & 0x3F) << 2) | (yaw & 3)`. Shapes:
`CUBE 0, SLAB 1, PYRAMID 2, DOME 3, SPHERE 4, RAMP 5, STAIRS 6, CONE 7, CYLINDER 8, PANEL 9, POST 10, BEAM 11,
LOW_RAMP 12, QUARTER_CUBE 13, TABLE 14, CHAIR 15, FENCE 16, SHEET 17, POT 18, BED_FOOT 61, BED_HEAD 62, BENCH 63`
(also by name: `pack("ramp", 1)`).

The mesher turns a form by its yaw first (local +Z goes to `yaw_direction(yaw)`: 0 +Z, 1 -X, 2 -Z, 3 +X), then tilts it
so local +Y points at the up-face (0 +Y, 1 -Y, 2 +X, 3 -X, 4 +Z, 5 -Z). Ready-made:

| helper | form |
|---|---|
| `ramp(dx, dz, low=False)` | a ramp whose HIGH side points along (dx, dz) (`low` = the half-height incline) |
| `ramp_down(dx, dz)` | a ramp hung upside down, deep side along (dx, dz) — the underside of an overhang, a cheese drip |
| `stairs(dx, dz)` | a staircase cell climbing toward (dx, dz) |
| `beam("x" / "z" / "y")`, `post()` | slim bars (a beam is 0.3 thick, a post 0.4) |
| `hanging(shape)`, `stalactite()` | the form with its up face turned down (a stalactite is a `dripstone` cone hung this way) |
| `rotate_descriptor(d, turns)`, `mirror_descriptor(d, axis)` | what `rotate` / `mirror` / `stamp` apply |
| `occupancy(d)` | the form's 4 x 4 x 4 sub-cells (the preview draws with it) |

## `solids` (pure geometry)

`span`, `box`, `box_walls`, `disc`, `ring2d`, `erode2d`, `extrude`, `cylinder`, `cylinder_along(axis, ...)`, `shell`
(the outer cells of any set), `ellipsoid`, `sphere`, `dome`, `cone`, `capsule`, `line` (Bresenham), `walk`
(face-connected), `polyline`, `sag_curve` (a callable t -> point), `cable`, `segment2d`, `polygon_wall`, `polygon`,
`star_points`, `star`, `arch_inside`, `arch_profile`, `arch`, `arcade`, `Ellipse(cx, cz, rx, rz)` (`.perimeter`,
`.arc(x, z)`, `.point(s)`, `.points(count, phase, scale)` -> (x, z, angle), `.angle`, `.inside`), `ring_openings`,
`pyramid_layers`, `pyramid`, `hash01`, `value_noise`, `cavern`, `lowest_cells`, `highest_cells`, `bounds`. Every
function returns a new set (or an ordered list for lines), so they combine with `|`, `-` and `&`.

## Tips

- **Keep it hollow and the runs long.** The file stores runs of equal cells along x; a `Mix` breaks the runs, so a
  solid 160-wide pyramid of mixed stone is 2.6 MB where a hollow one (`wall=3`) of one stone is a fraction of that.
  Nobody sees the inside of a wall: hollow big volumes (`wall=`), and mix only the visible skin
  (`d.fill(solids.shell(cells, 1), Mix(...))` after filling the solid in one block).
- **Build, then lean, then normalize**: `lean` / `shear` / `mirror` may push cells below 0; `write()` refuses that.
- **Stamp repeated parts**: build a column, a tower or a statue once as `Design.part()` and `stamp` it turned and
  mirrored — its shaped cells and markers turn with it.
- **Look before you ship**: `preview.py <key> --cut y=<storey>` for each storey shows the plan with stairs, doors and
  markers; `--cut z=<middle>` shows the section.
- Speed: about 4 s to build and 8 s to write a million cells; the preview takes about as long.

## Preview

```bash
uv run --no-project python tools/landmarks/preview.py <key|file.json|export folder> [more ...]
    [--out FILE.png|FOLDER] [--scale N] [--iso-scale N] [--views front,side,top,iso]
    [--cut y=N] [--cut z=N] [--cut x=N] [--cut auto] [--split] [--no-cavity] [--no-overlay] [--stats-only]
```

Writes `artifacts/landmark-previews/<key>.png` (git-ignored) by default — a giant ship (`giantShip: true`, a key under
`data/giant_ships` or any template file, also a wreck dumped as a template) `artifacts/giant-ship-previews/<key>.png`.
A giant ship gets `--cut auto` unless cuts are given: a plan one row above each walk row that carries doors, entrances
or chests (at most four), a cross-section at the middle of the length and a long section through the centre line.
`--split` also writes every panel as `<key>.<n>-<view>.png` (front, side, top, iso, plan-y7, section-z63 ...). Front looks from -Z (x right), side from +X
(z right), top from above (x right, z up), iso from the (-X, +Y, -Z) corner. `--cut y=40` adds a plan of everything at
or below row 40; `--cut z=30` a front view of what lies at or behind z = 30; `--cut x=50` a side view of x <= 50 — the
way to look into rooms, stairwells and tunnels. Colours come from `data/blocks.json` `color`, else the block texture's
average, else a hash; tint and glow as stored; shaped cells are drawn at sub-cell resolution. Overlays: the ground row
(green dashes), ruin zones (dashed boxes with their number and profile), markers (chest orange, sound cyan, den red,
vendor yellow, door brown, other magenta) and cavity cells (dim violet where no block hides them; a ghost in the iso
view). From Python: `preview.preview(ref, out=None, cuts=("y=40",))` or `preview.render(template_dict)`.

Rendering a 250 x 250 x 120 landmark takes a few seconds; a 103 x 90 x 256 ship with four decks (300,000 cells) about
8 s, sheet and sections included. It shows the intact design; the ruin is the server's
(the editor's *Show as a ruin* runs the real pass).

## Giant ships

`ships.ShipDesign` is a `Design` that writes a GIANT SHIP template (`data/giant_ships/<key>.json`, #3617): a huge
authored spaceship (at most 256 per axis) the game shows only as a wreck. Ship scripts live in
[tools/giant_ships/](../giant_ships/README.md) and are run by `tools/gen_giant_ships.py`;
`tools/giant_ships/_demo_hull.py` uses every helper below, step by step.

```bash
uv run --no-project python tools/gen_giant_ships.py --only _demo_hull --out artifacts/gs/ships --preview
```

Orientation: x = across, y = up, z = along; the **bow points to +z**. Header (written in this order): `key, name,
giantShip: true, nameKey: giantship.<key>.name, weight, minTerrainGeneration: 42, groundRow, style` (`alien` = a crystal
wreck) plus `pinOnly`, `pack`, `planetTypes`, `ruinStrength`, `ruinZones` (profiles `crash`, `hull` or `lattice`). No
`landmark` field, no material tokens, existing blocks only.

| helper | what it does |
|---|---|
| `ShipDesign(key, name, weight=1, groundRow=0, style="", **header)` | the design; `groundRow` = the row on the ground of a planet (the rows below are buried) |
| `loft(stations, m, wall=2, skin=None, bow=0, stern=0, tip=0.2, curve="ogive", anchor="centre", taper_axes="xy", smooth=True, symmetric=True)` -> `Hull` | a hull from `(z, Section)` stations, a monotone cubic between them (equal stations = a straight midbody), the last `bow` / first `stern` slices tapered to `tip` (`ogive`, `linear`, `blunt`) about `centre`, `keel` or `deck`; written as a closed shell `wall` thick (0 = solid), the outer layer in `skin` |
| sections (`shipgeom`, re-exported) | `ship_section(cx, keel, deck, beam, bottom=None, bilge=0.35, flare=0)`, `ellipse(cx, cy, rx, ry)`, `superellipse(cx, cy, rx, ry, p=4)`, `rounded_rect(cx, cy, w, h, r)`, `polygon(points)`; `.moved(dx, dy)`, `.scaled(fx, fy, about)` |
| `add_hull(hull, m, wall=2, skin=None)` | write any `Hull` (`Hull.from_cells(solids.capsule(...))`, `hull_a \| hull_b`) as a shell and register it |
| `Hull` queries | `cells`, `interior(wall)`, `shell(wall)`, `outer()`, `contains(p, wall=0)`, `row(y, z, wall)`, `rows(z, wall)`, `column(x, z, wall)`, `section(z, wall)`, `row_cells(y, wall)`, `z_extent(x, y)`, `bounds()`, `axis_x`, `wall` |
| `tube(points, m, radius, hollow=0, kind="catmull", ends="flat", open_ends=True, carve=True)` -> `Tube(wall, bore, path)` | a ball swept along a Catmull-Rom (`poly` = straight) path; `radius` = number, `(r0, r1)`, `[(t, r), ...]` or `f(t)`; hollow keeps a wall and clears the bore (bore radius >= 1.5 = a 3 x 3 walkway) |
| `deck(hull, y, m, hatches=(), x0, x1, z0, z1)` | a floor plate on row y filling the hull's inside; `hatches` (x0, z0, x1, z1) stay open; people walk on y + 1 |
| `window_band(hull, y0, y1, m="glass", every=4, width=2, side="both", start, end, depth)` | glass through the hull wall: `width` of every `every` cells along it, `side` left / right / both / front / back; skips where the wall is thicker than `depth` |
| `glass_dome(cx, cz, y0, r, m="glass", frame="iron_block", ribs=8, rings=2, ry, rz, wall=1, clear_inside=True)` | a dome on row y0 with a frame (base row, meridian ribs, latitude rings); clears its inside and the roof under it |
| `greebles(hull, seed, m="iron_block", density=0.02, faces=("up","left","right"), kinds=("box","antenna","pipe"), region, avoid, light="light_red")` | seeded boxes, masts (up faces, a `light` tip) and pipes on non-glass hull blocks; never over an existing cell |
| `corridor(hull, y, a0, a1, axis="z", centre=None, width=3, height=3, floor, walls, ceiling, light, light_every)` -> `Corridor` | carve a corridor (walk row y) clipped to the hull's inside, on the centre line by default |
| `rooms(hull, cor, per_side=3, depth=7, sides="both", walls="steel_wall", floor, ceiling, a0, a1, height, doors=True)` -> `[Room]` | rooms off a corridor sharing one-cell walls, clipped to the inside, each with a 2 x 3 `door_slide` doorway; `room.floor`, `room.centre`, `room.corner(k)`, `room.door` |
| `slide_door(x, y, z, axis, width=2, height=3)` | a doorway with a `door_slide` marker, `axis=x` = the wall is a plane of constant x (opening along +z), `axis=z` = constant z (opening along +x) |
| `entrance(x, y, z)` / `hull_door(hull, side, y, at, width=2, height=3, frame=None)` | the `entrance` marker (floor below, two cells of headroom) / a doorway cut through the hull wall with the marker just inside; the first entrance is the boarding arrival in space |
| `chest(x, y, z, table=, items=, chance=, block=, hidden=)`, `sound(x, y, z, sound, radius, volume)` | the landmark markers (inherited); `hidden=True` = a stone on top |
| `lift_shaft(x, z, decks, opening="-z", width=2, walls="steel_wall", ladder="ladder", landing=None)` / `ladder(x, z, y0, y1)` | a ladder shaft through `decks` (walk rows) with a doorway and a landing on each; every cell `keep`, so no deck is cut off by the ruin pass |
| `turret(cx, y, cz, r=4, barrels=2, length=16, barrel_r=1, facing="+z")`, `nozzle(cx, cy, z, r, length, m, glow)` | a gun turret; an engine bell opening backwards with a glowing throat |
| `finish()` | (the generator calls it) empty cells inside a registered hull below the ground row become cavity, so buried decks stay hollow |
| `problems()`, `write(out)` | the giant-ship checks, mirroring `GiantShipRules.Problems` (size, an entrance on a solid floor with headroom, markers known and inside, door axes, blocks, style, zones); writes `data/giant_ships` unless `out` |

Rules of thumb: build the hull, then decks, corridors and rooms, shafts, windows, domes, doors, markers, guns and
engines, greebles last; `normalize()` at the end (hulls, markers and zones move along). A deck ends where the keel rises
above it — check the plan sections. Mix only the skin; keep big inner volumes hollow (decks and walls, not solid fill).
