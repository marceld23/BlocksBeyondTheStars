# Player-designed block forms ("Eigene Formen")

Issues #842–#847. A player sculpts a block form out of micro cubes, saves it under a name, and crafts it
from any material. This is the geometry counterpart of the paint designs in
[the paint feature](../../src/BlocksBeyondTheStars.GameServer/GameServerPaint.cs), and it deliberately
copies that feature's structure wherever it can.

## Why this needed no migration

The form already travelled as a number before this shipped. `ItemKey` carries a **2-hex shape index**
(`stone#s0d`) and `ShapeCode` reserves **6 descriptor bits** for it — of 64 possible values only 19 are
built-in forms, so **45 indices were free**. A player-designed form is simply one of those indices whose
geometry lives in a per-save registry. Crafting, the item key, placing, chunk storage, persistence, the wire
format and mining therefore needed no new field at all.

Descriptor bits 27–31 remain reserved zero. They are the escape hatch if 45 forms per save ever prove too
few: widening the index is additive (old data reads high bits 0 = the same ids), not a migration. Do that
before reaching for anything cleverer.

## The bitmap format (`Shared/World/CustomShape.cs`)

- A micro-voxel grid, **8×8×8** (default) or **4×4×4**, one lowercase hex char per cell, row-major with x
  fastest then z then y. Self-describing by **length**: 512 or 64 chars, no version byte.
- `'0'` = empty, anything else = filled. Values `2..f` are accepted-but-equivalent today, reserved for a
  later per-micro-cell tint — a form authored by a newer client must not fail validation on an older server.
- `IsValidVoxels` additionally rejects the two degenerate cases: an **empty** grid is nothing, a **full**
  grid is a cube. Neither may burn a registry slot.

### The greedy merge and the box budget

`Merge` collapses filled cells into axis-aligned boxes (grow +X, extend +Z, extend +Y). It is integer-only
and deterministic **on purpose**: the server validates the box count with exactly the code the client meshes
from, so the two can never disagree across platforms.

`MaxBoxes = 48` is the render/collider budget. Each box costs 6 quads / 24 vertices / **12 collider
triangles**, so the worst legal form is a bounded multiple of a plain cube. The chunk mesher feeds shaped
geometry into the collider stream and the synchronous `MeshCollider` cook is the most expensive thing a
remesh does — which is why the **server refuses** an over-budget form at registration rather than letting
clients discover it. `CustomShapeTests` pins the arithmetic; the frame-time half is a playtest measurement.

## Registry lifecycle (`GameServerCustomShapes.cs`)

Mirrors `GameServerPaint`: register once per save, dedup by content hash, persist, broadcast to every joined
session, and push the full list to a newcomer **before the first chunk** so blocks carrying a form mesh
immediately instead of flashing as cubes.

Two deliberate differences:

| | paint designs | player forms |
|---|---|---|
| id space | 16 bit (65 535) | **45** (shape indices 19..63) |
| wipe | tombstone, id never reused | **frees the id** for the next designer |

Never-reuse is unaffordable at 45 slots — a long-lived world would strand. The consequence is documented for
operators: after a wipe, a block or item still holding that index adopts whatever form claims the slot next.
Forms are cosmetic geometry and a wipe is an explicit moderation act, so that trade is the right way round.

**The craft** runs through `ApplyShapeExchange`, the shared tail extracted from `HandleShapeCraft` — free
1:1, colour preserved — so the built-in forms and the player-designed ones cannot drift apart. Access is
gated on carrying `shape_tool`, checked **server-side**; a greyed-out client button is not a gate.

**Unknown indices** are treated identically everywhere: the server places a plain cube, the client meshes a
plain cube, the icon factory falls back to the block tile.

## Client rendering (`CustomShapeRegistry.cs`, `BlockShapeGeometry.cs`)

The chunk mesher runs **off-thread**, so the form bitmaps reach it as an immutable snapshot published
wholesale from the main thread — the same copy-on-write discipline `PaintDesignAtlas` uses for its UV map.
`BlockShapeGeometry` also caches built face lists per `(form, yaw, up-face)`; that cache is overdue on its
own, since `Build` used to allocate a fresh list for every shaped cell of every remesh.

Micro faces carry **real per-vertex UVs** (the slice of the tile the box covers). Without them an 8³ form
renders as dozens of shrunken copies of the whole texture, and a degenerate UV comes out white on the
mipmapped atlas.

### Built-in forms: cut material and texture slots (#1900)

Since #1900 the **built-in** forms get the same treatment. `Face.Finish` projects every face's corners along its
dominant axis in the form's own frame — top/bottom by X,Z, faces toward ±X by Z,height, faces toward ±Z by
X,height — before yaw and tilt, so the mapping rotates with the form. A slab side shows the lower half of the tile, a
table leg a thin slice: material tiles look carved on every form. Before, each face got the whole tile, which is
how the two-cell bed ended up with two complete drawn beds on its mattress and a small bed on every board.

Some tiles are **pictures** of an object (the bed seen from above, the flower pot). A block declares that in
`data/blocks.json` with `"tileKind": "picture"` (default `"material"`) and dresses its form with **face slots**:

```json
"faces": [
  { "rect": [0.25, 0.844, 0.734, 0.766] },
  { "part": "bed_head", "side": "top", "rect": [0.156, 0.109, 0.844, 0.484] },
  { "part": "bed_foot", "side": "top", "rect": [0.156, 0.484, 0.844, 0.859] }
]
```

- `part`: a `ShapePart` name — `body`, `bed_head`, `bed_foot`, `pillow`, `headboard`, `footboard`, `rim` — or `*`
  (default). The geometry tags each box (`Box(..., ShapePart)`); anything built face by face is `body`.
- `side`: `top`, `bottom`, `side` or `*` (default), in the form's own frame (a ladder plate's big face stays `top`).
- `tile`: another block key whose tile the face shows (default: the block's own tile).
- `rect`: `[x0, y0, x1, y1]` in fractions of the tile **image** (0,0 = top-left pixel as the PNG is drawn). The face is
  stretched onto it: `(x0, y0)` lands on the face's start corner, `(x1, y1)` on its end corner, where "start" is the
  lowest corner along the face's two projection axes above. For a side face `y0` is the row at its bottom edge and
  `y1` the row at its top edge; swap a pair to mirror. Without `rect` the face shows the slice of the tile it covers.
- The most specific match wins: part+side, part+`*`, `*`+side, `*`+`*` (`BlockFaceTextures.Resolve`).

The client resolves the slots once per content snapshot (`ShapeFaceTextures`) and the mesher reads a flat table.
`BlockFaceTextureTests` validates every shipped slot and fails when a block the server stamps with a form
(`PropShapes.DefaultPlaceShape`) declares no `tileKind`, or a `picture` block no slots — so a new prop cannot come
back with a picture on every face. Dedicated seamless tiles (a blanket, a board) can later replace a region by
editing only the data; inventory icons keep using the whole picture tile. The structure editor's voxel view uses
the proportional UVs but not the slots.

## Sharing (#846)

Three routes, all riding on things that already existed:

1. **Copy off a block** — right-clicking a shaped block with the tool opens the editor pre-loaded with that
   form. No message, no server change: every client already holds the whole registry.
2. **Stencil** — `shape_stencil#s<id>`. The item key carries the form index for a stencil exactly as it does
   for a block, so drop / trade / container / hotbar all work unchanged. Stamping one runs the same 1:1
   exchange a material does; the server allows the stencil as a craft source alongside shapeable materials.
3. **Share code** — `BBTS1-F-<base64 of "name\npayload">` (`Shared/World/ShareCode.cs`). Decoded through
   exactly the validation the server applies before registering, so a mistyped or hand-crafted code is
   rejected at the door. It is not a security boundary and does not pretend to be one.

The same file carries `BBTS1-D-…` for paint designs, which gained names, owner attribution and the same
export/import pair in this change.

## Moderation

`/reportshape` (player) and `/shapewipe <Player|#id>` (admin) mirror `/reportpaint` + `/paintwipe`. Reports
share the `paint_report` table with a `kind` column (`"paint"` — what every pre-existing row is — or
`"shape"`). A wipe blanks every instance of that form world-wide at once, which is the registry's advantage.

## Forms over several blocks (#1961)

A third payload next to the two legacy lengths — `"m1:WHL:"` + one 8³ bitmap per cell, footprint ≤ 3×3×3, ≤ 8
cells, one registry slot per form, the cell of a placed block in descriptor bits 27–30. Format, server rules
(footprint check before the item is consumed, falling as one piece via `ServerWorld.ShapedBlockReplaced`, N
blocks of material for N cells) and the client side are described in
[CREATOR_SUITE.md](CREATOR_SUITE.md) §5. The small in-world editor stays a ONE-block editor; such forms are
designed in the main-menu Form Editor.

## Known limits (state these in the manual, not just here)

- Behaviour keyed on specific built-in forms — sitting, beds, campfires — does not extend to custom forms.
  They are decorative geometry.
- Airtightness follows the block's `Solid` flag, not its shape, so a hollow form still seals a room, exactly
  as a thin `Sheet` does today.
- Paint works on a custom form, but a 32×32 design maps per face, so on a very fine form it reads as noise.
