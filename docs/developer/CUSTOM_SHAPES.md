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

## Known limits (state these in the manual, not just here)

- Behaviour keyed on specific built-in forms — sitting, beds, campfires — does not extend to custom forms.
  They are decorative geometry.
- Airtightness follows the block's `Solid` flag, not its shape, so a hollow form still seals a room, exactly
  as a thin `Sheet` does today.
- Paint works on a custom form, but a 32×32 design maps per face, so on a very fine form it reads as noise.
