// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds a Unity <see cref="Mesh"/> for a chunk using simple per-face culling: a face
    /// is emitted only when the neighbouring block is air. Good enough for the blocky look;
    /// greedy meshing can replace this later for fewer triangles.
    /// </summary>
    public static class ChunkMesher
    {
        private static readonly Vector3i[] Faces =
        {
            new Vector3i(0, 1, 0), new Vector3i(0, -1, 0),
            new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0),
            new Vector3i(0, 0, 1), new Vector3i(0, 0, -1),
        };

        /// <summary>How far (in blocks) a placed light propagates its colour through open/transparent cells.
        /// Used both by the mesher's flood-fill and by callers when gathering nearby light sources.</summary>
        public const int LightRadius = 9;

        /// <summary>Edge-bevel size (fraction of a block) for plain opaque cubes: exposed CONVEX edges are cut
        /// with a small 45° chamfer so silhouettes + lighting soften and the world reads less blocky. Only
        /// applied where two exposed faces meet (a flat field of ground adds ZERO extra geometry — its side
        /// faces are hidden, so it has no convex edges). Set to 0 to disable the whole bevel pass. Tunable.</summary>
        public const float BevelAmount = 0.06f;

        /// <summary>Builds the render mesh (opaque + see-through submeshes) and a separate collision mesh that
        /// excludes fluids (water/lava), so the player falls into water/lava instead of standing on it while
        /// glass/force-fields still block. Both share vertex positions; the collider has no normals/uvs.
        /// Convenience wrapper that builds the geometry and immediately uploads it to Unity meshes on the
        /// CALLING (main) thread — used by the one-shot ship/speeder meshers.</summary>
        public static (Mesh Render, Mesh Collider) Build(ChunkData chunk, GameContent content, System.Func<int, int, int, BlockId> worldBlock, BlockTextureAtlas atlas = null,
            System.Func<BlockId, Color> floraTint = null, System.Func<BlockId, Color> paintTint = null,
            IReadOnlyList<(Vector3i Pos, int Rgb)> lights = null,
            System.Func<int, int, int, int> worldShape = null)
            => BuildGeometry(chunk, content, worldBlock, atlas, floraTint, paintTint, lights, worldShape).ToMeshes();

        /// <summary>Builds ONLY the chunk geometry (vertex/index/attribute lists + flat normals + bounds) as
        /// plain data — NO Unity Mesh API calls, so it is safe to run on a worker thread; call
        /// <see cref="ChunkMeshData.ToMeshes"/> on the main thread to upload it. The planet chunk streamer uses
        /// this to move the heavy build off the main thread (A2). When called off-thread, every input it reads
        /// (the <paramref name="chunk"/>, the <paramref name="worldBlock"/>/<paramref name="worldShape"/>
        /// delegates, <paramref name="content"/>, <paramref name="atlas"/>, the tint resolvers and
        /// <paramref name="lights"/>) MUST be a thread-safe snapshot — atomic value reads with no concurrent
        /// dictionary mutation (see the planet streamer's neighbourhood snapshot in GameBootstrap).</summary>
        public static ChunkMeshData BuildGeometry(ChunkData chunk, GameContent content, System.Func<int, int, int, BlockId> worldBlock, BlockTextureAtlas atlas = null,
            System.Func<BlockId, Color> floraTint = null, System.Func<BlockId, Color> paintTint = null,
            IReadOnlyList<(Vector3i Pos, int Rgb)> lights = null,
            System.Func<int, int, int, int> worldShape = null)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();   // submesh 0: opaque blocks
            var trisT = new List<int>();  // submesh 1: see-through blocks (glass / force fields)
            var colliderTris = new List<int>(); // solid faces only (no fluids) → the collision mesh
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var skyUv = new List<Vector2>(); // x = skylight (1 = sees sky); y = tint mode (1 flora, 2 hull paint, 3 player dye)
            // x = foliage flag (1 = clip the tile's alpha → cutout leaves); yzw = the face's tint RGB —
            // per-species flora colour (mode 1; black = the shader falls back to the global planet hue),
            // the ship's hull paint (mode 2) or the player's dye colour (mode 3).
            var leafUv = new List<Vector4>();
            var blockLight = new List<Vector3>(); // TEXCOORD3: propagated coloured block-light at each vertex (0..1 rgb)
            var blockLightDir = new List<Vector3>(); // TEXCOORD4: dominant block-light direction (toward source), 0 = none
            var tangents = new List<Vector4>(); // per-face tangents for normal mapping
            // Ground-detail scatter points (not part of the mesh): xyz = local cell-top position, w = type
            // (0 = grass tuft, 1 = pebble). GroundScatter draws them GPU-instanced, culled + quality-gated.
            var scatter = new List<Vector4>();

            var origin = WorldConstants.ChunkOrigin(chunk.Coord);
            int n = WorldConstants.ChunkSize;

            // Coloured block-light field: a per-channel flood-fill from nearby light sources (placed glow
            // blocks + dedicated light blocks), baked per-vertex so placed lights actually illuminate their
            // surroundings (in caves/at night), independent of the sun + flora tint. Cost is proportional to
            // the lit volume — chunks with no light nearby pay nothing.
            var blockLightField = BuildBlockLight(chunk, content, worldBlock, origin, n, lights);
            Vector3 BlockLightAt(int wx, int wy, int wz)
                => blockLightField != null && blockLightField.TryGetValue((wx, wy, wz), out var v) ? v : Vector3.zero;

            // Dominant direction TOWARD the block-light source at a cell: the gradient of the light field's
            // luminance (brighter neighbours pull the vector their way). Lets the shader shade placed lights
            // with N·L + a glint + normal-map relief instead of a flat wash. Zero where there's no light (or a
            // perfectly uniform field) → the shader falls back to the old flat additive look.
            Vector3 BlockLightDirAt(int wx, int wy, int wz)
            {
                if (blockLightField == null)
                {
                    return Vector3.zero;
                }

                Vector3 grad = Vector3.zero;
                for (int f = 0; f < Faces.Length; f++)
                {
                    var d = Faces[f];
                    var nb = BlockLightAt(wx + d.X, wy + d.Y, wz + d.Z);
                    float luma = nb.x * 0.299f + nb.y * 0.587f + nb.z * 0.114f;
                    grad += new Vector3(d.X, d.Y, d.Z) * luma;
                }

                return grad.sqrMagnitude > 1e-6f ? grad.normalized : Vector3.zero;
            }

            // Per-column "highest solid block" cache → a face is sky-lit only if the air cell it faces is
            // above its column's top (so caves, building/ship interiors and overhang undersides go dark,
            // while open ground + cliff faces stay sunlit). The sun/sky terms are scaled by this in the
            // block shader; the headlamp + emissive blocks light regardless, so caves need a lamp/lights.
            var colTop = new Dictionary<long, int>();
            int Top(int wx, int wz)
            {
                long key = ((long)(uint)wx) | ((long)wz << 32);
                if (colTop.TryGetValue(key, out var t))
                {
                    return t;
                }

                int top = int.MinValue;
                for (int yy = origin.Y + n + 48; yy >= origin.Y - 16; yy--)
                {
                    if (!worldBlock(wx, yy, wz).IsAir)
                    {
                        top = yy;
                        break;
                    }
                }

                colTop[key] = top;
                return top;
            }

            // Soft skylight for an air cell a face looks into: 1 if it sees the open sky directly, otherwise a
            // smooth 0..1 from how much of a 5x5 horizontal neighbourhood is open at this height. This turns the
            // old hard 0/1 edge into a gradient, so a cave MOUTH (some open neighbours) is softly lit and
            // overhang shadows feather, while a DEEP cave (no open neighbours) stays ~0 and still needs a lamp.
            const int SkyKernel = 2; // 5x5 (radius 2)
            float Skylight(int wx, int wy, int wz)
            {
                if (wy > Top(wx, wz))
                {
                    return 1f; // open straight up to the sky
                }

                int open = 0, total = 0;
                for (int ox = -SkyKernel; ox <= SkyKernel; ox++)
                for (int oz = -SkyKernel; oz <= SkyKernel; oz++)
                {
                    total++;
                    if (wy > Top(wx + ox, wz + oz))
                    {
                        open++;
                    }
                }

                // Linear fraction: a mouth (≈half its neighbourhood open) lands mid-bright and softly lit, a
                // couple of blocks deeper falls off to the dark cave floor — a smooth gradient, deep stays ~0.
                return open / (float)total;
            }

            // Per-cell water classification cache for this build (each cell is sampled by up to four
            // corners; classify it once).
            var waterCells = new Dictionary<(int X, int Y, int Z), Vector4>();
            Vector4 WaterCellData(BlockId waterId, int cwx, int cwy, int cwz)
            {
                var key = (cwx, cwy, cwz);
                if (!waterCells.TryGetValue(key, out var d))
                {
                    d = WaterSurface.Classify(worldBlock, waterId, cwx, cwy, cwz);
                    waterCells[key] = d;
                }

                return d;
            }

            // Corner-smoothed foam + wave-amplitude factor for the water-surface corner at (cwx, cwz):
            // averaged over the 4 cells meeting there, so a corner shared by neighbouring faces gets the
            // IDENTICAL value from each — foam fades in smooth gradients instead of per-block steps, and
            // wave displacement stays crack-free across block (and body-type) boundaries. Bank/step cells
            // count as shore: full foam, zero amplitude — waves die exactly at the waterline.
            Vector2 WaterCorner(BlockId waterId, int cwx, int cwy, int cwz)
            {
                float foam = 0f, amp = 0f;
                for (int ox = -1; ox <= 0; ox++)
                for (int oz = -1; oz <= 0; oz++)
                {
                    int cx = cwx + ox, cz = cwz + oz;
                    bool surface = worldBlock(cx, cwy, cz).Value == waterId.Value && worldBlock(cx, cwy + 1, cz).IsAir;
                    if (!surface)
                    {
                        foam += 1f; // the shore itself
                        continue;
                    }

                    var d = WaterCellData(waterId, cx, cwy, cz);
                    foam += d.y;
                    amp += d.x > 1.5f && d.x < 2.5f ? 1f : d.x > 0.5f && d.x < 1.5f ? 0.25f : 0f;
                }

                return new Vector2(foam * 0.25f, amp * 0.25f);
            }

            // Per-vertex ambient occlusion ("smooth lighting"): each face corner is darkened by how many of
            // the three solid blocks meeting at it (its two edge neighbours + the diagonal, in the outward
            // layer) are filled. Turns the flat per-face shade into soft contact shadows in every crevice, so
            // the blocky world reads far more grounded/organic. Baked into vertex-colour .b, which the atlas
            // shader already multiplies into the lighting — no shader change. Air, glass, water + plants don't
            // occlude; opaque solids do. The outward-cell probes are cached for the chunk.
            var aoOcc = new Dictionary<(int, int, int), bool>();
            bool AoOccluder(int ax, int ay, int az)
            {
                var key = (ax, ay, az);
                if (aoOcc.TryGetValue(key, out var o))
                {
                    return o;
                }

                var b = worldBlock(ax, ay, az);
                bool res = !b.IsAir && !IsTransparent(content, b) && !IsFloraBlock(content, b) && !IsFoliageBlock(content, b);
                aoOcc[key] = res;
                return res;
            }

            // AO brightness for a face's four corners in FaceQuad vertex order (AoDark = fully occluded ..
            // 1 = open). Samples the outward layer (block + face normal) offset along the face's two in-plane
            // axes; the classic voxel rule is that two touching side blocks fully darken the shared corner.
            const float AoDark = 0.55f;
            Vector4 FaceAo(int bx, int by, int bz, int f)
            {
                var dir = Faces[f];
                int cx = bx + dir.X, cy = by + dir.Y, cz = bz + dir.Z; // the outward (air) layer
                int na = dir.X != 0 ? 0 : dir.Y != 0 ? 1 : 2;          // normal axis (contributes no offset)
                var q = FaceQuad(Vector3.zero, f);
                var res = Vector4.one;
                for (int i = 0; i < 4; i++)
                {
                    // The two in-plane axis offsets for this corner (sign from the 0/1 quad coordinate).
                    int e1x = 0, e1y = 0, e1z = 0, e2x = 0, e2y = 0, e2z = 0;
                    if (na == 0) { e1y = q[i].y > 0.5f ? 1 : -1; e2z = q[i].z > 0.5f ? 1 : -1; }
                    else if (na == 1) { e1x = q[i].x > 0.5f ? 1 : -1; e2z = q[i].z > 0.5f ? 1 : -1; }
                    else { e1x = q[i].x > 0.5f ? 1 : -1; e2y = q[i].y > 0.5f ? 1 : -1; }

                    bool s1 = AoOccluder(cx + e1x, cy + e1y, cz + e1z);
                    bool s2 = AoOccluder(cx + e2x, cy + e2y, cz + e2z);
                    bool cn = AoOccluder(cx + e1x + e2x, cy + e1y + e2y, cz + e1z + e2z);
                    int ao = s1 && s2 ? 0 : 3 - (s1 ? 1 : 0) - (s2 ? 1 : 0) - (cn ? 1 : 0);
                    res[i] = Mathf.Lerp(AoDark, 1f, ao / 3f);
                }

                return res;
            }

            for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                var id = chunk.Get(x, y, z);
                if (id.IsAir)
                {
                    continue;
                }

                // See-through blocks (glass, force fields) render in a separate alpha-blended submesh and
                // don't hide the world behind them, so windows + energy barriers show space through.
                bool transparent = IsTransparent(content, id);

                // With an atlas, the texture carries the colour and vertex colour is just the
                // per-face shade; without one, fall back to the flat palette × shade.
                Color baseColor = atlas == null ? BlockColor(content, id) : Color.white;
                Rect uv = atlas != null ? atlas.TileUv(id.Value) : new Rect(0f, 0f, 1f, 1f);
                // Per-block reflection params (gloss, metal) for the lit atlas shader.
                var mat = BlockMaterial(content, id);
                float matR = mat.x, matG = mat.y;
                // Emission (ores/crystals/lava/light blocks glow — the bloom pass catches them, and they
                // stay lit at night). Packed into the vertex-colour alpha for the atlas shader.
                float emission = atlas != null ? BlockEmission(content, id) : 0f;
                // Flora flag (TEXCOORD1.y): the block shader desaturates + re-tints these. With a tint
                // resolver every SPECIES rolls its own per-world colour (TEXCOORD2.yzw); without one the
                // shader falls back to the planet's uniform flora hue. Tree trunks (wood_log) take their own
                // per-world DARK bark hue (mode 4) so they read clearly darker than the leaves.
                bool isFlora = IsFloraBlock(content, id);
                // Tree trunk: a per-world DARK bark hue (resolved like flora, mode 4) so trunks read clearly
                // darker than the leaves. Only on planet chunks (floraTint != null) — ship meshes carry no
                // resolver, so wood_log stays a normal paintable hull block there.
                bool isWood = floraTint != null && IsWoodBlock(content, id);
                Color speciesTint = (isFlora || isWood) && floraTint != null ? floraTint(id) : Color.black;
                // Hull paint (item 32): ship meshes pass a per-block paint resolver — a painted face raises
                // the tint-mode flag (TEXCOORD1.y) to 2 and carries the ship's hull colour in TEXCOORD2.yzw
                // (the atlas shader multiplies it into the albedo; black = unpainted).
                Color paint = !isFlora && paintTint != null ? paintTint(id) : Color.black;
                bool painted = paint.r + paint.g + paint.b > 0.001f;
                // Player dye (always-available recolour): the placed cell carries a surface tint in its chunk
                // modifier. Mode 3 = a luminance-based recolour in the shader applied everywhere (independent
                // of the flora-tint global), so dyed building blocks read vividly on any world / in caves.
                var (modTint, _) = chunk.GetModifierLocal(WorldConstants.LocalIndex(x, y, z));
                bool dyed = modTint != 0;
                Color dye = dyed ? RgbToColor(modTint) : Color.black;
                float floraFlag = dyed ? 3f : isWood ? 4f : isFlora ? 1f : painted ? 2f : 0f;
                // Foliage flag (TEXCOORD2.x): tree crowns + leafy plants whose tile carries a baked alpha
                // mask — the shader clips it so the leaves are see-through (holes), not a solid cube.
                bool foliage = IsFoliageBlock(content, id);
                float leafFlag = foliage ? 1f : 0f;
                // Water + fire render but don't collide — you swim/sink into water and walk through (and burn
                // in) fire. Lava DOES collide: you stand on its surface (and take contact damage from the cell
                // below) rather than dropping straight through it into a cave/void — you must not fall through
                // lava.
                var collKey = content.BlockById(id)?.Key;
                bool collidable = collKey != "water" && collKey != "fire";
                int wx = origin.X + x, wy = origin.Y + y, wz = origin.Z + z;

                // Ground-detail scatter (T0): on planet chunks, strew a sparse deterministic set of tufts/
                // pebbles on open-topped ground cells. Render-only decoration drawn instanced by GroundScatter
                // (no collider, no mesh geometry) — a world-cell hash keeps it stable + identical on all clients.
                if (atlas != null)
                {
                    int stype = ScatterType(collKey);
                    if (stype >= 0 && worldBlock(wx, wy + 1, wz).IsAir)
                    {
                        int sh = unchecked(wx * 374761393 + wz * 668265263 + stype * 1013904223);
                        if ((uint)sh % 7u == 0u)
                        {
                            scatter.Add(new Vector4(x + 0.5f, y + 1f, z + 0.5f, stype));
                        }
                    }
                }

                // Natural-block variation: a deterministic WORLD-position hash (stable across remeshes,
                // identical on all clients) picks one of the block's variant tiles and a 90° rotation
                // for the top/bottom faces — breaking the visible texture tiling on open ground.
                int uvRot = 0;
                if (atlas != null && atlas.TryGetVariants(id.Value, out var variantSlots))
                {
                    int hash = unchecked(wx * 73856093 ^ wy * 19349663 ^ wz * 83492791);
                    int pick = (int)((uint)hash % (uint)(variantSlots.Length + 1));
                    if (pick > 0)
                    {
                        uv = atlas.TileUv(variantSlots[pick - 1]);
                    }

                    uvRot = (hash >> 8) & 3;
                }

                // Water SURFACE cells (air above) get a body classification — open water with gentle
                // waves + coastal foam, calm lake, or flowing river — packed into the top face's
                // TEXCOORD2 for the transparent shader. Other faces/blocks keep the flora-tint layout.
                bool isWaterSurface = collKey == "water" && worldBlock(wx, wy + 1, wz).IsAir;
                Vector4 waterData = isWaterSurface ? WaterCellData(id, wx, wy, wz) : Vector4.zero;
                // Falling-water column (a waterfall): fed from above + open on its sides. Its vertical flanks
                // would normally be culled (see the submerged-fluid test below) so the cascade reads flat; keep
                // them and tag them mode 4 so the transparent shader streaks them downward.
                bool isFallingWater = collKey == "water" && WaterfallDetect.IsFalling(worldBlock, id, wx, wy, wz);

                // Lava SURFACE cell (air above): tag its faces as tint mode 5 so the opaque atlas shader animates
                // a slow molten crust over the otherwise-static glow (L1). Lava is opaque, so unlike water this
                // rides the opaque shader's skyl.y mode channel, not the transparent water layout.
                bool isLavaSurface = collKey == "lava" && worldBlock(wx, wy + 1, wz).IsAir;
                // Falling-lava column (a lavafall, L3): like falling water, but mode 6 → the opaque shader streaks
                // a hot glow straight DOWN the vertical flanks. WaterfallDetect is fluid-agnostic (takes the id).
                bool isFallingLava = collKey == "lava" && WaterfallDetect.IsFalling(worldBlock, id, wx, wy, wz);

                // Graphics quick-win: small leafy plants render as classic CROSS BILLBOARDS (two crossed
                // cutout quads, both windings) instead of decal-textured cubes — they read as real plants.
                // Tree crowns keep the cutout shell (a volume), solid flora (cactus/crystal/caps) stay cubes.
                // Cross plants get NO collider, so the player walks through grass/ferns instead of bumping.
                if (atlas != null && foliage && collKey != null && collKey.StartsWith("flora_", System.StringComparison.Ordinal))
                {
                    float plantSky = Skylight(wx, wy + 1, wz); // open sky above the plant
                    Vector3 plantBl = BlockLightAt(wx, wy, wz);  // coloured block-light reaching the plant
                    Vector3 plantBlDir = BlockLightDirAt(wx, wy, wz); // dominant light direction at the plant
                    var plantCol = new Color(matR, matG, 0.9f, emission);
                    // Per-plant size variance (a grass field is tall + short tufts, not a uniform lawn): a
                    // deterministic bell scale from the world cell, so all clients agree. Height varies more
                    // than width; they're keyed off different salts so a plant can be tall + slender or low + bushy.
                    // Tall species (ferns, reeds, grass tufts…) get a taller billboard, so vegetation reads in
                    // layers — low ground cover beneath a taller upper storey — rather than one flat carpet.
                    float tallBoost = TallFlora.Contains(collKey) ? 1.85f : 1f;
                    float plantH = CrossPlantScale(wx, wy, wz, 0x1, 0.35f) * tallBoost;
                    float plantW = CrossPlantScale(wx, wy, wz, 0x2, 0.20f);
                    // Per-plant top lean (deterministic, ±~0.12) so a field reads as naturally varied rather
                    // than a grid of upright cards.
                    var plantLean = new Vector2(
                        (CrossPlantScale(wx, wy, wz, 0x4, 1f) - 1f) * 0.12f,
                        (CrossPlantScale(wx, wy, wz, 0x8, 1f) - 1f) * 0.12f);
                    AddCrossPlant(verts, tris, colors, uvs, tangents, skyUv, leafUv, blockLight, blockLightDir,
                        new Vector3(x, y, z), plantCol, uv, plantSky, speciesTint, plantBl, plantBlDir, plantLean, plantH, plantW);
                    continue;
                }

                // Solid flora (cactus/crystal/mushroom/puffball/…): render as a fitting 3D form (cylinder/cone/
                // dome/sphere) via the tested building-shape geometry instead of a plain cube — a big step up in
                // "plant" read at ~cube cost, carrying the same flora tint (mode 1) + emission (glowcaps etc.).
                if (atlas != null && collKey != null && SolidFlora.Contains(collKey))
                {
                    float flSky = Skylight(wx, wy + 1, wz);
                    Vector3 flBl = BlockLightAt(wx, wy + 1, wz);
                    Vector3 flBlDir = BlockLightDirAt(wx, wy + 1, wz);
                    Color flTint = dyed ? dye : isFlora ? speciesTint : Color.black;
                    float flMode = dyed ? 3f : isFlora ? 1f : 0f;
                    AddShapedBlock(verts, tris, colliderTris, colors, uvs, tangents, skyUv, leafUv, blockLight, blockLightDir,
                        SolidFloraShape(collKey), 0, ShapeCode.UpPlusY, new Vector3(x, y, z), uv,
                        matR, matG, emission, flTint, flMode, flSky, flBl, flBlDir);
                    continue;
                }

                // Custom building SHAPE (sphere/dome/pyramid/ramp/…): a per-voxel form stamped on a placed
                // block. Like the cross-plant above, it replaces the standard cube faces with its own geometry
                // (+ a matching collider) and bows out of neighbour face-culling (see below), so a cube beside
                // it still draws the face between them. Carries the dye colour through.
                int shapeDesc = atlas != null ? chunk.GetShapeLocal(WorldConstants.LocalIndex(x, y, z)) : 0;
                if (!ShapeCode.IsCube(shapeDesc))
                {
                    float shSky = Skylight(wx, wy + 1, wz);          // open sky above the shaped block
                    Vector3 shBl = BlockLightAt(wx, wy + 1, wz);     // coloured block-light reaching it
                    Vector3 shBlDir = BlockLightDirAt(wx, wy + 1, wz);
                    Color shTint = dyed ? dye : (isWood || isFlora) ? speciesTint : Color.black;
                    float shTintMode = dyed ? 3f : isWood ? 4f : isFlora ? 1f : 0f; // 3 dye, 4 bark, 1 flora (matches cubes)
                    AddShapedBlock(verts, tris, colliderTris, colors, uvs, tangents, skyUv, leafUv, blockLight, blockLightDir,
                        ShapeCode.ShapeOf(shapeDesc), ShapeCode.OrientationOf(shapeDesc), ShapeCode.UpFaceOf(shapeDesc), new Vector3(x, y, z), uv,
                        matR, matG, emission, shTint, shTintMode, shSky, shBl, shBlDir);
                    continue;
                }

                // Edge bevel (T0): plain opaque cubes get their exposed convex edges chamfered. Fluids, glass/
                // fields, flora + foliage keep hard edges (their shaders/geometry are special). openMask marks
                // which of the 6 faces are exposed — used to inset only the beveled edges + emit chamfers/corners.
                bool bevel = BevelAmount > 0f && atlas != null && !transparent && !isFlora && !isWood && !foliage
                    && collKey != "water" && collKey != "lava" && collKey != "fire";
                int openMask = 0;
                if (bevel)
                {
                    for (int bf = 0; bf < Faces.Length; bf++)
                    {
                        var bd = Faces[bf];
                        int ox = wx + bd.X, oy = wy + bd.Y, oz = wz + bd.Z;
                        var onb = worldBlock(ox, oy, oz);
                        if (onb.IsAir || IsTransparent(content, onb)
                            || (worldShape != null && !ShapeCode.IsCube(worldShape(ox, oy, oz))))
                        {
                            openMask |= 1 << bf;
                        }
                    }
                }

                for (int f = 0; f < Faces.Length; f++)
                {
                    var dir = Faces[f];
                    int nx = wx + dir.X, ny = wy + dir.Y, nz = wz + dir.Z;
                    var nb = worldBlock(nx, ny, nz);
                    // Opaque blocks hide faces behind solid neighbours but still draw faces behind glass/
                    // fields (so you see the wall through the window). See-through blocks only draw their
                    // faces toward open air — that culls glass↔glass seams + the hidden side against a wall.
                    // Foliage is meshed as a thin shell (culled against its own kind), so the cutout holes in
                    // the near leaf faces show the sky/world BEHIND the tree — a clearly see-through crown,
                    // not a dense volume whose holes just reveal more leaves.
                    bool drawFace = transparent ? nb.IsAir : (nb.IsAir || IsTransparent(content, nb));

                    // A non-cube SHAPED neighbour doesn't fill its cell, so it can't seal this face — draw toward
                    // it (otherwise a cube beside a sphere/ramp would leave a hole). Only checked when the face
                    // would otherwise be culled, so chunks with no shapes pay nothing.
                    if (!drawFace && worldShape != null && !ShapeCode.IsCube(worldShape(nx, ny, nz)))
                    {
                        drawFace = true;
                    }

                    // A submerged fluid cell (the same fluid sits directly above it) must NOT draw its vertical
                    // SIDE faces: they'd paint the surface-looking water tile onto an underwater edge — e.g. the
                    // step between deep (swimmable) and shallow water — which looks wrong seen from below (B43).
                    // Only the true top layer (air above) keeps its faces, so the real water surface still shows.
                    if (drawFace && dir.Y == 0 && IsFluidBlock(content, id) && worldBlock(wx, wy + 1, wz).Value == id.Value && !isFallingWater && !isFallingLava)
                    {
                        drawFace = false;
                    }

                    if (!drawFace)
                    {
                        continue; // face hidden
                    }

                    float s = FaceShade(f);
                    // With an atlas the lit shader does the directional shading, so the vertex colour
                    // carries material params instead: r=gloss, g=metal, b=per-vertex shade×AO (soft contact
                    // shadows in crevices). Without one, fall back to the flat palette colour × shade × AO.
                    // See-through blocks (glass/water) skip AO so the water/energy shaders read a clean shade.
                    Vector4 ao = transparent ? Vector4.one : FaceAo(wx, wy, wz, f);
                    Color c0, c1, c2, c3;
                    if (atlas != null)
                    {
                        c0 = new Color(matR, matG, s * ao.x, emission);
                        c1 = new Color(matR, matG, s * ao.y, emission);
                        c2 = new Color(matR, matG, s * ao.z, emission);
                        c3 = new Color(matR, matG, s * ao.w, emission);
                    }
                    else
                    {
                        c0 = new Color(baseColor.r * s * ao.x, baseColor.g * s * ao.x, baseColor.b * s * ao.x, 1f);
                        c1 = new Color(baseColor.r * s * ao.y, baseColor.g * s * ao.y, baseColor.b * s * ao.y, 1f);
                        c2 = new Color(baseColor.r * s * ao.z, baseColor.g * s * ao.z, baseColor.b * s * ao.z, 1f);
                        c3 = new Color(baseColor.r * s * ao.w, baseColor.g * s * ao.w, baseColor.b * s * ao.w, 1f);
                    }

                    // Bevel cubes inset each exposed convex edge of the face by BevelAmount (edges against a
                    // solid neighbour stay flush → no gap); the chamfer strips + corners are added after the loop.
                    Vector3[] bevelQuad = bevel ? BevelInsetQuad(new Vector3(x, y, z), f, openMask) : null;
                    int faceBase = verts.Count;
                    AddFace(verts, transparent ? trisT : tris, colors, uvs, tangents, new Vector3(x, y, z), f,
                        c0, c1, c2, c3, uv,
                        dir.Y != 0 ? uvRot : 0, bevelQuad); // rotate only top/bottom faces — sides keep their up-orientation
                    if (collidable)
                    {
                        colliderTris.Add(faceBase); colliderTris.Add(faceBase + 1); colliderTris.Add(faceBase + 2);
                        colliderTris.Add(faceBase); colliderTris.Add(faceBase + 2); colliderTris.Add(faceBase + 3);
                    }

                    float sky = Skylight(nx, ny, nz); // soft sky-occlusion (cave mouths feather, deep stays dark)
                    // mode 5 = animated molten lava surface; mode 6 = falling-lava flank (vertical hot streak).
                    float faceMode = isLavaSurface ? 5f : (isFallingLava && dir.Y == 0) ? 6f : floraFlag;
                    skyUv.Add(new Vector2(sky, faceMode)); skyUv.Add(new Vector2(sky, faceMode));
                    skyUv.Add(new Vector2(sky, faceMode)); skyUv.Add(new Vector2(sky, faceMode));
                    // Coloured block-light reaching the air cell this face looks into (same cell the skylight
                    // samples) — placed lights illuminate the wall regardless of sun/skylight.
                    Vector3 faceBl = BlockLightAt(nx, ny, nz);
                    blockLight.Add(faceBl); blockLight.Add(faceBl); blockLight.Add(faceBl); blockLight.Add(faceBl);
                    Vector3 faceBlDir = BlockLightDirAt(nx, ny, nz);
                    blockLightDir.Add(faceBlDir); blockLightDir.Add(faceBlDir); blockLightDir.Add(faceBlDir); blockLightDir.Add(faceBlDir);
                    // Water top faces carry the water-body data instead of the (always-zero-for-water)
                    // flora tint; only the transparent shader ever reads these vertices. Foam + wave
                    // amplitude are CORNER-smoothed (x=mode, y=foam, z=amp factor, w=flow axis 0=X/1=Z)
                    // so they interpolate seamlessly across neighbouring blocks; mode/flow stay per-face.
                    if (isWaterSurface && dir.Y == 1)
                    {
                        // Corner offsets follow FaceQuad's +Y order: (0,0) (0,1) (1,1) (1,0).
                        var c00 = WaterCorner(id, wx, wy, wz);
                        var c01 = WaterCorner(id, wx, wy, wz + 1);
                        var c11 = WaterCorner(id, wx + 1, wy, wz + 1);
                        var c10 = WaterCorner(id, wx + 1, wy, wz);
                        leafUv.Add(new Vector4(waterData.x, c00.x, c00.y, waterData.w));
                        leafUv.Add(new Vector4(waterData.x, c01.x, c01.y, waterData.w));
                        leafUv.Add(new Vector4(waterData.x, c11.x, c11.y, waterData.w));
                        leafUv.Add(new Vector4(waterData.x, c10.x, c10.y, waterData.w));
                    }
                    else if (isFallingWater && dir.Y == 0)
                    {
                        // Falling-water flank: mode 4 → the transparent shader scrolls a bright streak straight down.
                        var fall = new Vector4(4f, 0f, 0f, 0f);
                        leafUv.Add(fall); leafUv.Add(fall); leafUv.Add(fall); leafUv.Add(fall);
                    }
                    else
                    {
                        var tint = dyed ? dye : painted ? paint : speciesTint;
                        var leaf = new Vector4(leafFlag, tint.r, tint.g, tint.b);
                        leafUv.Add(leaf); leafUv.Add(leaf); leafUv.Add(leaf); leafUv.Add(leaf);
                    }

                    // Ship hull greeble (T2): a sparse set of raised, slightly darker plating panels on exposed
                    // hull faces so ships read as "built" rather than bare cubes. Ship/structure context only
                    // (paintTint != null), render-only, deterministic per world cell + face. Density/size tunable.
                    if (paintTint != null && collKey == "iron_wall" && GreebleAt(wx, wy, wz, f))
                    {
                        var gTint = dyed ? dye : painted ? paint : speciesTint;
                        var gLeaf = new Vector4(leafFlag, gTint.r, gTint.g, gTint.b);
                        AddGreeblePanel(verts, tris, colors, uvs, tangents, skyUv, leafUv, blockLight, blockLightDir,
                            new Vector3(x, y, z), f, uv, matR, matG, s, emission, sky, faceMode, gLeaf, faceBl, faceBlDir);
                    }
                }

                // Bevel chamfer strips (per exposed convex edge) + corner triangles (per exposed convex
                // corner), render-only. Attributes are a representative snapshot of the block (a thin 45°
                // strip; the tiny lighting approximation is invisible). Skipped for the collider (tiny inset).
                if (bevel)
                {
                    var bevTint = dyed ? dye : painted ? paint : speciesTint;
                    var bevLeaf = new Vector4(0f, bevTint.r, bevTint.g, bevTint.b); // not foliage-cutout
                    EmitBevel(verts, tris, colors, uvs, tangents, skyUv, leafUv, blockLight, blockLightDir,
                        new Vector3(x, y, z), openMask, uv, matR, matG, emission, floraFlag, bevLeaf,
                        Skylight(wx, wy + 1, wz), BlockLightAt(wx, wy + 1, wz), BlockLightDirAt(wx, wy + 1, wz));
                }
            }

            // Flat per-face normals + bounds computed analytically here (every face owns its own
            // unwelded vertices, so each vertex normal is just its triangle's geometric normal — this
            // reproduces what Mesh.RecalculateNormals did for this mesh). Doing it as pure data, instead
            // of the main-thread-only Mesh.RecalculateNormals/RecalculateBounds, both saves that work and
            // lets the whole build move onto a worker thread later (A2).
            var normals = new Vector3[verts.Count];
            AccumulateFlatNormals(verts, tris, normals);
            AccumulateFlatNormals(verts, trisT, normals);
            var bounds = ComputeBounds(verts, n);

            // Return plain data — the Unity Mesh upload happens in ChunkMeshData.ToMeshes() on the main thread.
            return new ChunkMeshData
            {
                Verts = verts,
                OpaqueTris = tris,
                TransparentTris = trisT,
                ColliderTris = colliderTris,
                Colors = colors,
                Uvs = uvs,
                SkyUv = skyUv,
                LeafUv = leafUv,
                BlockLight = blockLight,
                BlockLightDir = blockLightDir,
                Tangents = tangents,
                Normals = normals,
                Bounds = bounds,
                Scatter = scatter,
            };
        }

        /// <summary>Writes flat per-face normals into <paramref name="normals"/>: every face emitted by the
        /// mesher owns its own (unwelded) vertices, so a vertex's normal is simply its triangle's geometric
        /// normal. This reproduces Mesh.RecalculateNormals for this mesh exactly, but as plain data so the
        /// build can run off the main thread (no Mesh API calls).</summary>
        private static void AccumulateFlatNormals(List<Vector3> verts, List<int> tris, Vector3[] normals)
        {
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                Vector3 nrm = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]).normalized;
                normals[i0] = nrm;
                normals[i1] = nrm;
                normals[i2] = nrm;
            }
        }

        /// <summary>Exact AABB over the chunk's vertices (replaces the main-thread-only
        /// Mesh.RecalculateBounds). Falls back to the full cube extent for an empty mesh.</summary>
        private static Bounds ComputeBounds(List<Vector3> verts, int n)
        {
            if (verts.Count == 0)
            {
                return new Bounds(new Vector3(n, n, n) * 0.5f, new Vector3(n, n, n));
            }

            Vector3 min = verts[0], max = verts[0];
            for (int i = 1; i < verts.Count; i++)
            {
                min = Vector3.Min(min, verts[i]);
                max = Vector3.Max(max, verts[i]);
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        /// <summary>A deterministic per-cell "bell" size factor centred on 1.0 (average of two pseudo-randoms
        /// → triangular, so most plants are about normal and extremes are rare). <paramref name="salt"/> picks
        /// an independent axis (height vs width); pure function of the world cell, so all clients agree.</summary>
        private static float CrossPlantScale(int wx, int wy, int wz, int salt, float amp)
        {
            int h = unchecked((wx * 73856093) ^ (wy * 19349663) ^ (wz * 83492791) ^ (salt * 26699));
            uint u = (uint)h;
            float a = (u & 0xFFFF) / 65535f;
            float b = ((u >> 16) & 0xFFFF) / 65535f;
            float t = (a + b) * 0.5f;
            return 1f + (t - 0.5f) * 2f * amp;
        }

        // The three vertical plane orientations of a plant rosette (degrees around Y): 0/60/120 give six
        // half-planes of coverage, so a plant reads as a rounded 3D tuft from every angle instead of a flat cross.
        private static readonly float[] PlantPlaneAngles = { 0f, 60f, 120f };

        /// <summary>Adds a leafy plant as a THREE-plane rosette of cutout quads through the cell (T3): fuller +
        /// more volumetric than the old flat cross, each plane emitted with BOTH windings (the atlas shaders cull
        /// back faces). <paramref name="lean"/> tilts the top for per-plant variation so a field doesn't look
        /// like uniform flat cards. Render-only — no collider, so small plants stay walk-through.
        /// <paramref name="heightScale"/> / <paramref name="widthScale"/> give each plant its own size.</summary>
        private static void AddCrossPlant(List<Vector3> verts, List<int> tris, List<Color> colors, List<Vector2> uvs,
            List<Vector4> tangents, List<Vector2> skyUv, List<Vector4> leafUv, List<Vector3> blockLight, List<Vector3> blockLightDir, Vector3 cell, Color col, Rect uv, float sky,
            Color tint, Vector3 bl, Vector3 blDir, Vector2 lean, float heightScale = 1f, float widthScale = 1f)
        {
            // Each plane is a vertical quad through the cell centre, its floor line along a rosette angle; the
            // width scales about the middle and is clamped inside the cell to avoid bleeding into neighbours.
            float half = Mathf.Clamp(0.42f * widthScale, 0.18f, 0.49f);
            float cx = cell.x + 0.5f, cz = cell.z + 0.5f, cy = cell.y;
            var up = new Vector3(lean.x, heightScale, lean.y); // top tilts by the per-plant lean

            foreach (float deg in PlantPlaneAngles)
            {
                float rad = deg * Mathf.Deg2Rad;
                float dx = Mathf.Cos(rad) * half, dz = Mathf.Sin(rad) * half;
                var a = new Vector3(cx - dx, cy, cz - dz);
                var b = new Vector3(cx + dx, cy, cz + dz);
                var tangent = (Vector4)((b - a).normalized);
                tangent.w = -1f;

                for (int winding = 0; winding < 2; winding++)
                {
                    int baseIdx = verts.Count;
                    if (winding == 0)
                    {
                        verts.Add(a); verts.Add(b); verts.Add(b + up); verts.Add(a + up);
                    }
                    else
                    {
                        verts.Add(b); verts.Add(a); verts.Add(a + up); verts.Add(b + up);
                    }

                    uvs.Add(new Vector2(uv.x, uv.y)); uvs.Add(new Vector2(uv.xMax, uv.y));
                    uvs.Add(new Vector2(uv.xMax, uv.yMax)); uvs.Add(new Vector2(uv.x, uv.yMax));
                    for (int i = 0; i < 4; i++)
                    {
                        colors.Add(col);
                        tangents.Add(tangent);
                        skyUv.Add(new Vector2(sky, 1f)); // flora flag on — takes the species/world tint
                        leafUv.Add(new Vector4(1f, tint.r, tint.g, tint.b)); // cutout on + per-species tint
                        blockLight.Add(bl); // coloured block-light reaching the plant
                        blockLightDir.Add(blDir); // matching block-light direction
                    }

                    tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 1);
                    tris.Add(baseIdx); tris.Add(baseIdx + 3); tris.Add(baseIdx + 2);
                }
            }
        }

        /// <summary>Adds a non-cube building shape (sphere/dome/pyramid/ramp/…) at a cell: its own outward-faced
        /// geometry into the opaque submesh AND the collider (shape-matched collision), carrying the same
        /// per-vertex streams as a cube face so it textures, tints + lights identically. Per-face shade
        /// (vertex colour .b) comes from the face normal — top-bright/bottom-dark like cube faces — while the
        /// lit atlas shader does the main directional shading from the per-face normals.</summary>
        private static void AddShapedBlock(List<Vector3> verts, List<int> tris, List<int> colliderTris, List<Color> colors,
            List<Vector2> uvs, List<Vector4> tangents, List<Vector2> skyUv, List<Vector4> leafUv, List<Vector3> blockLight,
            List<Vector3> blockLightDir, int shapeIndex, int orientation, int upFace, Vector3 cell, Rect uv, float matR, float matG,
            float emission, Color tint, float tintMode, float sky, Vector3 bl, Vector3 blDir)
        {
            var faces = BlockShapeGeometry.Build(shapeIndex, orientation, upFace);
            if (faces == null)
            {
                return;
            }

            var leaf = new Vector4(0f, tint.r, tint.g, tint.b); // not foliage (x=0); yzw = dye tint (mode 3) or black
            foreach (var face in faces)
            {
                Vector3 a = cell + face.A, b = cell + face.B, c = cell + face.C;
                Vector3 d = face.IsQuad ? cell + face.D : Vector3.zero;
                Vector3 edge1 = b - a;
                Vector3 edge2 = (face.IsQuad ? d : c) - a;
                Vector3 nrm = Vector3.Cross(edge1, edge2).normalized;
                float shade = Mathf.Clamp(0.76f + 0.24f * nrm.y, 0.5f, 1f);
                var col = new Color(matR, matG, shade, emission);
                Vector3 tanDir = edge2.sqrMagnitude > 1e-8f ? edge2.normalized : edge1.normalized;
                float hand = Vector3.Dot(Vector3.Cross(nrm, tanDir), edge1) < 0f ? -1f : 1f;
                var tan = new Vector4(tanDir.x, tanDir.y, tanDir.z, hand);

                int baseIdx = verts.Count;
                int n = face.IsQuad ? 4 : 3;
                verts.Add(a); verts.Add(b); verts.Add(c);
                if (face.IsQuad)
                {
                    verts.Add(d);
                    uvs.Add(new Vector2(uv.xMin, uv.yMin)); uvs.Add(new Vector2(uv.xMin, uv.yMax));
                    uvs.Add(new Vector2(uv.xMax, uv.yMax)); uvs.Add(new Vector2(uv.xMax, uv.yMin));
                }
                else
                {
                    uvs.Add(new Vector2(uv.xMin, uv.yMin)); uvs.Add(new Vector2(uv.xMax, uv.yMin)); uvs.Add(new Vector2(uv.xMax, uv.yMax));
                }

                for (int i = 0; i < n; i++)
                {
                    colors.Add(col);
                    tangents.Add(tan);
                    skyUv.Add(new Vector2(sky, tintMode));
                    leafUv.Add(leaf);
                    blockLight.Add(bl);
                    blockLightDir.Add(blDir);
                }

                tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                colliderTris.Add(baseIdx); colliderTris.Add(baseIdx + 1); colliderTris.Add(baseIdx + 2);
                if (face.IsQuad)
                {
                    tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                    colliderTris.Add(baseIdx); colliderTris.Add(baseIdx + 2); colliderTris.Add(baseIdx + 3);
                }
            }
        }

        /// <summary>Deterministic "does this hull face carry a greeble panel" test (~1/3 of faces), stable per
        /// world cell + face so a ship looks the same on every client and across rebuilds.</summary>
        private static bool GreebleAt(int wx, int wy, int wz, int face)
        {
            int h = unchecked(wx * 92837111 ^ wy * 689287499 ^ wz * 283923481 ^ face * 49979687);
            return (uint)h % 3u == 0u;
        }

        /// <summary>Adds one raised, slightly darker plating panel proud of a hull face (T2 greeble): a single
        /// inset quad offset outward along the face normal — render-only, winding inherited from the face so it
        /// always shows. Carries the face's tint mode + hull-paint tint so it recolours with the ship. Conservative
        /// (top quad only, no rim); raise/inset are easy to tune.</summary>
        private static void AddGreeblePanel(List<Vector3> verts, List<int> tris, List<Color> colors, List<Vector2> uvs,
            List<Vector4> tangents, List<Vector2> skyUv, List<Vector4> leafUv, List<Vector3> blockLight, List<Vector3> blockLightDir,
            Vector3 cell, int face, Rect uv, float matR, float matG, float shade, float emission, float sky, float tintMode,
            Vector4 leaf, Vector3 bl, Vector3 blDir)
        {
            const float insetT = 0.26f, raise = 0.02f;
            var dir = Faces[face];
            var nrm = new Vector3(dir.X, dir.Y, dir.Z);
            var q = FaceQuad(cell, face);
            Vector3 c = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
            Vector3 off = nrm * raise;
            Vector3 p0 = Vector3.Lerp(q[0], c, insetT) + off;
            Vector3 p1 = Vector3.Lerp(q[1], c, insetT) + off;
            Vector3 p2 = Vector3.Lerp(q[2], c, insetT) + off;
            Vector3 p3 = Vector3.Lerp(q[3], c, insetT) + off;

            int baseIdx = verts.Count;
            verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
            var col = new Color(matR, matG, shade * 0.82f, emission); // slightly darker → reads as a raised plate
            Vector3 tan3 = (p3 - p0).sqrMagnitude > 1e-8f ? (p3 - p0).normalized : (p1 - p0).normalized;
            float hand = Vector3.Dot(Vector3.Cross(nrm, tan3), p1 - p0) < 0f ? -1f : 1f;
            var tan = new Vector4(tan3.x, tan3.y, tan3.z, hand);
            Vector2 uvC = uv.center;
            for (int i = 0; i < 4; i++)
            {
                colors.Add(col); uvs.Add(uvC); tangents.Add(tan);
                skyUv.Add(new Vector2(sky, tintMode)); leafUv.Add(leaf); blockLight.Add(bl); blockLightDir.Add(blDir);
            }

            tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
            tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
        }

        /// <summary>Relative brightness per face (top brightest, bottom darkest) for a lit-looking blocky world.</summary>
        private static float FaceShade(int face) => face switch
        {
            0 => 1.00f, // +Y top
            1 => 0.50f, // -Y bottom
            2 => 0.82f, // +X
            3 => 0.72f, // -X
            4 => 0.66f, // +Z
            _ => 0.76f, // -Z
        };

        /// <summary>
        /// Per-block reflection params for the lit atlas shader: x=gloss (0 matte .. 1 mirror-ish),
        /// y=metal (0 dielectric .. 1 metal — metals tint their highlight + reflection by the albedo).
        /// Ice/glass/crystal are glossy, hull/ore metals reflective, soils matte.
        /// </summary>
        /// <summary>True for plant foliage that takes the planet's uniform flora hue (B38): the small flora
        /// plants (block key "flora_*") and tree crowns ("tree_leaves"). The server's flora colour is "one hue
        /// for all of a planet's plant life", so leaves recolour per planet too; the wood_log trunk keeps its
        /// natural bark colour.</summary>
        private static bool IsFloraBlock(GameContent content, BlockId id)
        {
            var key = content.BlockById(id)?.Key;
            return key != null
                && (key.StartsWith("flora_", System.StringComparison.Ordinal)
                    || key == "tree_leaves" || key == "pine_needles" || key == "palm_frond");
        }

        /// <summary>True for the tree trunk (wood_log): the block shader recolours it with a per-world DARK
        /// bark hue (tint mode 4) so trunks read clearly darker than the leaves they carry — never the same
        /// colour. Separate from <see cref="IsFloraBlock"/> because the bark uses its own (darker) tint band.</summary>
        private static bool IsWoodBlock(GameContent content, BlockId id)
            => content.BlockById(id)?.Key == "wood_log";

        // Tall cross-billboard flora (an upper vegetation layer above the low ground cover). MUST mirror the
        // FloraHeight.Tall, non-solid entries in FloraCatalog. Solid/cube flora ignore height, so they're absent.
        private static readonly HashSet<string> TallFlora = new HashSet<string>
        {
            "flora_fern", "flora_vine", "flora_reed", "flora_thornbush", "flora_kelp", "flora_seagrass",
            "flora_tendril", "flora_alienfern", "flora_palm", "flora_grasstuft", "flora_icereed", "flora_saltgrass",
        };

        // The structural / solid / glowing-cap flora that read better as solid cubes — everything else
        // leafy (plus tree crowns) gets the alpha-cutout leaf look. MUST match bake_leaf_alpha.py's FOLIAGE.
        private static readonly HashSet<string> SolidFlora = new HashSet<string>
        {
            "flora_cactus", "flora_crystal", "flora_succulent", "flora_mushroom", "flora_puffball",
            "flora_pitcher", "flora_glowcap", "flora_emberbloom", "flora_sporepod", "flora_glowvine",
            "flora_bulb", "flora_gasbloom", "flora_shardbloom", // item 21 V3 alien flora (bulbous/crystalline)
        };

        /// <summary>True for foliage that renders with alpha-cutout leaves (holes punched into the tile):
        /// tree crowns + leafy/flowering plants. The leaf tiles carry a baked alpha mask; the block shader
        /// clips it for leaf-flagged faces. Excludes structural/glowing flora (cactus, crystal, caps…) and
        /// never the ground "grass" block.</summary>
        private static bool IsFoliageBlock(GameContent content, BlockId id)
        {
            if (id.IsAir)
            {
                return false;
            }

            var key = content.BlockById(id)?.Key;
            if (key == null)
            {
                return false;
            }

            return key == "tree_leaves" || key == "pine_needles" || key == "palm_frond"
                || (key.StartsWith("flora_", System.StringComparison.Ordinal) && !SolidFlora.Contains(key));
        }

        /// <summary>True for fluids (water/lava) — they render but are excluded from the collision mesh.</summary>
        private static bool IsFluidBlock(GameContent content, BlockId id)
        {
            var key = content.BlockById(id)?.Key;
            return key is "water" or "lava";
        }

        /// <summary>Ground-detail scatter host classification: 0 = grass tuft (soft ground), 1 = pebble (dry/
        /// rocky ground), -1 = no scatter. Only open-topped ground blocks get decoration; ores, machines,
        /// fluids, flora and built blocks return -1.</summary>
        private static int ScatterType(string key) => key switch
        {
            "grass" or "dirt" or "mud" => 0,
            "sand" or "snow" or "stone" or "basalt" => 1,
            _ => -1,
        };

        /// <summary>Maps a solid-flora block to a fitting <see cref="BlockShape"/> index (T3): columns → cylinder,
        /// crystalline/ember blooms → cone, capped fungi → dome, bulbous forms → sphere. Reuses the tested
        /// building-shape geometry so structural plants read as 3D forms instead of cubes.</summary>
        private static int SolidFloraShape(string key) => key switch
        {
            "flora_cactus" or "flora_pitcher" or "flora_sporepod" or "flora_glowvine" => 8, // cylinder (columns)
            "flora_crystal" or "flora_shardbloom" or "flora_emberbloom" or "flora_frostflower" => 7, // cone (shards)
            "flora_mushroom" or "flora_glowcap" => 3, // dome (caps)
            _ => 4, // sphere (puffball, succulent, bulb, gasbloom, …)
        };

        private static Vector2 BlockMaterial(GameContent content, BlockId id)
        {
            var def = content.BlockById(id);
            // Data-driven override (Material Editor materials carry their own gloss/metal).
            if (def != null && (def.Gloss.HasValue || def.Metal.HasValue))
            {
                return new Vector2(def.Gloss ?? 0.05f, def.Metal ?? 0f);
            }

            switch (def?.Key)
            {
                case "glass": return new Vector2(0.90f, 0.0f);
                case "force_field": return new Vector2(0.60f, 0.0f);
                case "ice": return new Vector2(0.85f, 0.0f);
                case "water": return new Vector2(0.80f, 0.0f);
                case "crystal": return new Vector2(0.95f, 0.15f);
                case "data_cache": return new Vector2(0.90f, 0.20f);
                case "iron_wall": return new Vector2(0.60f, 0.90f);
                case "titanium_ore": return new Vector2(0.50f, 0.70f);
                case "copper_ore": return new Vector2(0.45f, 0.60f);
                case "iron_ore": return new Vector2(0.35f, 0.50f);
                case "carbon": return new Vector2(0.30f, 0.10f);
                case "silicate": return new Vector2(0.15f, 0.0f);
                case "basalt": return new Vector2(0.10f, 0.0f);
                default: return new Vector2(0.05f, 0.0f); // stone, dirt, grass, sand, mud, lava, ...
            }
        }

        /// <summary>Per-block emission (0 = none .. 1 = full glow) packed into the atlas vertex alpha:
        /// light blocks + lava glow strongly, crystals + glowing flora medium, ores a faint sheen.</summary>
        private static float BlockEmission(GameContent content, BlockId id)
        {
            var def = content.BlockById(id);
            if (def?.Emission is float e)
            {
                return e; // data-driven (Material Editor)
            }

            switch (def?.Key)
            {
                case "force_field": return 0.85f; // glowing energy barrier
                case "light_white": return 1.00f;
                case "light_red": return 1.00f;
                case "light_green": return 1.00f;
                case "lava": return 1.00f;
                case "flora_emberbloom": return 0.95f;
                case "flora_cinderbush": return 0.70f; // smouldering volcanic embers
                case "data_cache": return 0.90f;
                case "flora_glowcap": return 0.85f;
                case "flora_glowvine": return 0.80f;
                case "flora_bulb": return 0.75f;
                case "flora_crystal": return 0.70f;
                case "flora_shardbloom": return 0.6f;
                case "flora_sporepod": return 0.55f;
                case "crystal": return 0.50f;
                case "flora_frostflower": return 0.40f;
                case "copper_ore": return 0.28f;
                case "titanium_ore": return 0.22f;
                case "iron_ore": return 0.18f;
                default: return 0f;
            }
        }

        /// <summary>Converts a 0xRRGGBB integer to a linear-ish UnityEngine.Color (0..1 per channel).</summary>
        private static Color RgbToColor(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>
        /// The light colour a cell emits as 0xRRGGBB, or 0 if it is not a light source. A placed glow block
        /// carries its colour in <paramref name="glowMod"/>; otherwise the dedicated light blocks
        /// (light_*, the bright strip lights) emit their fixed colour. Natural emissives (lava, crystals,
        /// glowing ores/flora) deliberately return 0 — they keep their existing self-glow look and do NOT
        /// flood the world with propagated light. Shared by the mesher and the client light-source registry.
        /// </summary>
        public static int BlockLightColor(GameContent content, BlockId id, int glowMod)
        {
            if (glowMod != 0)
            {
                return glowMod & 0xFFFFFF;
            }

            var def = content?.BlockById(id);
            if (def == null)
            {
                return 0;
            }

            switch (def.Key)
            {
                case "light_white": return 0xFFFFFF;
                case "light_red": return 0xFF3838;
                case "light_green": return 0x53FF61;
            }

            // Bright authored light fixtures (strip lights) carry their own colour + a high emission.
            if (def.Color is int c && (def.Emission ?? 0f) >= 0.85f)
            {
                return c & 0xFFFFFF;
            }

            return 0;
        }

        /// <summary>
        /// Per-channel coloured light flood-fill from nearby sources, returning the normalised (0..1) light
        /// colour reached at each cell (null when nothing is lit). Sources are the caller-supplied
        /// <paramref name="lights"/> (placed glow blocks + light blocks gathered across chunk seams) or, when
        /// none are passed (ship/asteroid meshes), the light blocks found inside the chunk itself. Light stops
        /// at solid blocks and passes through air/glass/water/plants. Cost scales with the lit volume.
        /// </summary>
        private static Dictionary<(int, int, int), Vector3> BuildBlockLight(
            ChunkData chunk, GameContent content, System.Func<int, int, int, BlockId> worldBlock,
            Vector3i origin, int n, IReadOnlyList<(Vector3i Pos, int Rgb)> lights)
        {
            var sources = new List<(int X, int Y, int Z, Vector3 Col)>();
            if (lights != null)
            {
                int loX = origin.X - LightRadius, hiX = origin.X + n + LightRadius;
                int loY = origin.Y - LightRadius, hiY = origin.Y + n + LightRadius;
                int loZ = origin.Z - LightRadius, hiZ = origin.Z + n + LightRadius;
                foreach (var (pos, rgb) in lights)
                {
                    if (rgb == 0 || pos.X < loX || pos.X > hiX || pos.Y < loY || pos.Y > hiY || pos.Z < loZ || pos.Z > hiZ)
                    {
                        continue; // out of light range of this chunk
                    }

                    var c = RgbToColor(rgb);
                    sources.Add((pos.X, pos.Y, pos.Z, new Vector3(c.r, c.g, c.b)));
                }
            }
            else
            {
                for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                for (int z = 0; z < n; z++)
                {
                    var id = chunk.Get(x, y, z);
                    if (id.IsAir)
                    {
                        continue;
                    }

                    var (_, g) = chunk.GetModifierLocal(WorldConstants.LocalIndex(x, y, z));
                    int rgb = BlockLightColor(content, id, g);
                    if (rgb == 0)
                    {
                        continue;
                    }

                    var c = RgbToColor(rgb);
                    sources.Add((origin.X + x, origin.Y + y, origin.Z + z, new Vector3(c.r, c.g, c.b)));
                }
            }

            if (sources.Count == 0)
            {
                return null;
            }

            var field = new Dictionary<(int, int, int), Vector3>();
            var opaqueCache = new Dictionary<(int, int, int), bool>();
            bool Opaque(int wx, int wy, int wz)
            {
                var key = (wx, wy, wz);
                if (opaqueCache.TryGetValue(key, out var o))
                {
                    return o;
                }

                var b = worldBlock(wx, wy, wz);
                bool res = !b.IsAir && !IsTransparent(content, b) && !IsFloraBlock(content, b) && !IsFoliageBlock(content, b);
                opaqueCache[key] = res;
                return res;
            }

            var queue = new Queue<(int, int, int)>();
            foreach (var s in sources)
            {
                var key = (s.X, s.Y, s.Z);
                var lvl = s.Col * LightRadius; // per-channel start level (0..LightRadius)
                field[key] = field.TryGetValue(key, out var ex) ? Vector3.Max(ex, lvl) : lvl;
                queue.Enqueue(key);
            }

            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                var cur = field[p];
                for (int f = 0; f < Faces.Length; f++)
                {
                    int nx = p.Item1 + Faces[f].X, ny = p.Item2 + Faces[f].Y, nz = p.Item3 + Faces[f].Z;
                    if (Opaque(nx, ny, nz))
                    {
                        continue; // solid blocks stop light (sources are already seeded)
                    }

                    var nl = new Vector3(Mathf.Max(0f, cur.x - 1f), Mathf.Max(0f, cur.y - 1f), Mathf.Max(0f, cur.z - 1f));
                    if (nl.x <= 0f && nl.y <= 0f && nl.z <= 0f)
                    {
                        continue;
                    }

                    var key = (nx, ny, nz);
                    if (field.TryGetValue(key, out var exist))
                    {
                        var merged = Vector3.Max(exist, nl);
                        if (merged == exist)
                        {
                            continue; // no channel improved
                        }

                        field[key] = merged;
                    }
                    else
                    {
                        field[key] = nl;
                    }

                    queue.Enqueue(key);
                }
            }

            float inv = 1f / LightRadius;
            var result = new Dictionary<(int, int, int), Vector3>(field.Count);
            foreach (var kv in field)
            {
                result[kv.Key] = kv.Value * inv; // normalise 0..1
            }

            return result;
        }

        /// <summary>See-through blocks: rendered in the alpha-blended submesh and treated like air when
        /// culling neighbouring opaque faces, so the world behind shows through (windows, energy fields).</summary>
        private static bool IsTransparent(GameContent content, BlockId id)
        {
            if (id.IsAir)
            {
                return false;
            }

            var def = content.BlockById(id);
            return def?.Key is "glass" or "force_field" or "water" or "fire"; // alpha-blended — see through them
        }

        private static Color BlockColor(GameContent content, BlockId id)
        {
            var def = content.BlockById(id);
            if (def == null)
            {
                return Color.magenta;
            }

            // Data-driven base colour (Material Editor materials carry their own tint).
            if (def.Color is int rgb)
            {
                return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
            }

            // A curated palette so the world reads intentionally until a real texture atlas
            // lands (M27). Unknown keys fall back to a stable colour derived from the key.
            switch (def.Key)
            {
                case "stone": return new Color(0.55f, 0.55f, 0.57f);
                case "dirt": return new Color(0.45f, 0.32f, 0.20f);
                case "basalt": return new Color(0.24f, 0.24f, 0.27f);
                case "ice": return new Color(0.70f, 0.85f, 0.95f);
                case "iron_ore": return new Color(0.60f, 0.50f, 0.45f);
                case "copper_ore": return new Color(0.72f, 0.45f, 0.30f);
                case "silicate": return new Color(0.80f, 0.78f, 0.60f);
                case "carbon": return new Color(0.16f, 0.16f, 0.18f);
                case "titanium_ore": return new Color(0.60f, 0.62f, 0.68f);
                case "data_cache": return new Color(0.20f, 0.70f, 0.90f);
                case "glass": return new Color(0.70f, 0.90f, 0.95f);
                case "force_field": return new Color(0.35f, 0.80f, 1f);
                case "iron_wall": return new Color(0.55f, 0.57f, 0.62f);
            }

            int h = 0;
            foreach (char c in def.Key)
            {
                h = h * 31 + c;
            }

            var rng = new System.Random(h);
            return new Color(
                0.35f + 0.5f * (float)rng.NextDouble(),
                0.35f + 0.5f * (float)rng.NextDouble(),
                0.35f + 0.5f * (float)rng.NextDouble());
        }

        private static void AddFace(List<Vector3> verts, List<int> tris, List<Color> colors, List<Vector2> uvs, List<Vector4> tangents, Vector3 p, int face, Color col0, Color col1, Color col2, Color col3, Rect uv, int uvRot = 0, Vector3[] quad = null)
        {
            int baseIndex = verts.Count;
            Vector3[] q = quad ?? FaceQuad(p, face);
            verts.Add(q[0]); verts.Add(q[1]); verts.Add(q[2]); verts.Add(q[3]);
            // Per-corner colours (AO in .b) in the same q0..q3 order as the verts/uvs below.
            colors.Add(col0); colors.Add(col1); colors.Add(col2); colors.Add(col3);

            // Tangent = world-space U direction (q0→q3, matching the UV mapping below); w = bitangent
            // handedness. Lets the shader transform the tangent-space normal map into world space.
            // (A rotated UV set rotates the normal-map relief with it — for the noisy natural tiles
            // that use uvRot the slight lighting rotation is invisible, so the tangent stays as-is.)
            Vector3 e1 = q[1] - q[0], e3 = q[3] - q[0];
            Vector3 nrm = Vector3.Cross(e1, e3).normalized;
            Vector3 tan = e3.normalized;
            float hand = Vector3.Dot(Vector3.Cross(nrm, tan), e1) < 0f ? -1f : 1f;
            var tv = new Vector4(tan.x, tan.y, tan.z, hand);
            tangents.Add(tv); tangents.Add(tv); tangents.Add(tv); tangents.Add(tv);

            // 90°-step UV rotation: cycle the corner order (uvRot 0..3).
            Vector2 c0 = new(uv.xMin, uv.yMin), c1 = new(uv.xMin, uv.yMax);
            Vector2 c2 = new(uv.xMax, uv.yMax), c3 = new(uv.xMax, uv.yMin);
            var corners = new[] { c0, c1, c2, c3 };
            uvs.Add(corners[uvRot & 3]);
            uvs.Add(corners[(uvRot + 1) & 3]);
            uvs.Add(corners[(uvRot + 2) & 3]);
            uvs.Add(corners[(uvRot + 3) & 3]);
            tris.Add(baseIndex); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
            tris.Add(baseIndex); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
        }

        private static Vector3[] FaceQuad(Vector3 p, int face)
        {
            switch (face)
            {
                case 0: return new[] { p + new Vector3(0, 1, 0), p + new Vector3(0, 1, 1), p + new Vector3(1, 1, 1), p + new Vector3(1, 1, 0) }; // +Y
                case 1: return new[] { p + new Vector3(0, 0, 1), p + new Vector3(0, 0, 0), p + new Vector3(1, 0, 0), p + new Vector3(1, 0, 1) }; // -Y
                case 2: return new[] { p + new Vector3(1, 0, 0), p + new Vector3(1, 1, 0), p + new Vector3(1, 1, 1), p + new Vector3(1, 0, 1) }; // +X
                case 3: return new[] { p + new Vector3(0, 0, 1), p + new Vector3(0, 1, 1), p + new Vector3(0, 1, 0), p + new Vector3(0, 0, 0) }; // -X
                case 4: return new[] { p + new Vector3(1, 0, 1), p + new Vector3(1, 1, 1), p + new Vector3(0, 1, 1), p + new Vector3(0, 0, 1) }; // +Z
                default: return new[] { p + new Vector3(0, 0, 0), p + new Vector3(0, 1, 0), p + new Vector3(1, 1, 0), p + new Vector3(1, 0, 0) }; // -Z
            }
        }

        /// <summary>The <see cref="Faces"/> index for a signed axis (axis 0=X,1=Y,2=Z; sign ±1) — maps an
        /// in-plane neighbour direction to its face so the bevel can test whether that face is exposed.</summary>
        private static int FaceIndexFor(int axis, int sign)
            => axis == 0 ? (sign > 0 ? 2 : 3) : axis == 1 ? (sign > 0 ? 0 : 1) : (sign > 0 ? 4 : 5);

        /// <summary>A cell-local point with the three axis coordinates assigned by axis index.</summary>
        private static Vector3 AxisVec(int a0, float c0, int a1, float c1, int a2, float c2)
        {
            var v = Vector3.zero;
            v[a0] = c0; v[a1] = c1; v[a2] = c2;
            return v;
        }

        /// <summary>The face quad with each corner pulled inward by <see cref="BevelAmount"/> along an in-plane
        /// axis IFF that edge is a convex exposed edge (the in-plane neighbour face is open). Edges against a
        /// solid neighbour stay flush, so beveled and unbeveled cubes still tile without gaps.</summary>
        private static Vector3[] BevelInsetQuad(Vector3 cell, int face, int openMask)
        {
            var q = FaceQuad(cell, face);
            var dir = Faces[face];
            int na = dir.X != 0 ? 0 : dir.Y != 0 ? 1 : 2;
            for (int i = 0; i < 4; i++)
            {
                var c = q[i];
                for (int axis = 0; axis < 3; axis++)
                {
                    if (axis == na)
                    {
                        continue;
                    }

                    int sgn = c[axis] - cell[axis] > 0.5f ? 1 : -1;
                    if ((openMask & (1 << FaceIndexFor(axis, sgn))) != 0)
                    {
                        c[axis] -= sgn * BevelAmount;
                    }
                }

                q[i] = c;
            }

            return q;
        }

        /// <summary>Adds the render-only bevel geometry for one plain opaque cube: a 45° chamfer strip for every
        /// exposed convex EDGE (both faces sharing it open) and a small triangle for every exposed convex CORNER
        /// (all three faces open). Windings are fixed so each polygon's geometric normal faces outward. Carries a
        /// representative attribute snapshot of the block (a thin strip — the lighting approximation is invisible).</summary>
        private static void EmitBevel(List<Vector3> verts, List<int> tris, List<Color> colors, List<Vector2> uvs,
            List<Vector4> tangents, List<Vector2> skyUv, List<Vector4> leafUv, List<Vector3> blockLight, List<Vector3> blockLightDir,
            Vector3 cell, int openMask, Rect uv, float matR, float matG, float emission, float tintMode, Vector4 leaf,
            float sky, Vector3 bl, Vector3 blDir)
        {
            float b = BevelAmount;
            Vector2 uvC = uv.center;
            bool Open(int axis, int sign) => (openMask & (1 << FaceIndexFor(axis, sign))) != 0;

            void AddPoly(Vector3 a, Vector3 v1, Vector3 v2, bool isQuad, Vector3 v3, Vector3 outward)
            {
                a += cell; v1 += cell; v2 += cell;
                if (isQuad)
                {
                    v3 += cell;
                }

                Vector3 far = isQuad ? v3 : v2;
                bool flip = Vector3.Dot(Vector3.Cross(v1 - a, far - a), outward) < 0f;
                float shade = Mathf.Clamp(0.72f + 0.28f * outward.normalized.y, 0.5f, 1f);
                var col = new Color(matR, matG, shade, emission);
                Vector3 tan3 = (far - a).sqrMagnitude > 1e-8f ? (far - a).normalized : (v1 - a).normalized;
                var tan = new Vector4(tan3.x, tan3.y, tan3.z, -1f);
                int baseIdx = verts.Count;

                void Push(Vector3 p)
                {
                    verts.Add(p); colors.Add(col); uvs.Add(uvC); tangents.Add(tan);
                    skyUv.Add(new Vector2(sky, tintMode)); leafUv.Add(leaf); blockLight.Add(bl); blockLightDir.Add(blDir);
                }

                if (isQuad)
                {
                    if (!flip) { Push(a); Push(v1); Push(v2); Push(v3); }
                    else { Push(a); Push(v3); Push(v2); Push(v1); }
                    tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                    tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                }
                else
                {
                    if (!flip) { Push(a); Push(v1); Push(v2); }
                    else { Push(a); Push(v2); Push(v1); }
                    tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                }
            }

            // Chamfer strips — one per exposed convex edge.
            for (int axisA = 0; axisA < 3; axisA++)
            for (int axisB = axisA + 1; axisB < 3; axisB++)
            {
                int axisC = 3 - axisA - axisB;
                for (int sa = -1; sa <= 1; sa += 2)
                for (int sb = -1; sb <= 1; sb += 2)
                {
                    if (!Open(axisA, sa) || !Open(axisB, sb))
                    {
                        continue;
                    }

                    float tLo = Open(axisC, -1) ? b : 0f;
                    float tHi = Open(axisC, 1) ? 1f - b : 1f;
                    float aMain = sa > 0 ? 1f : 0f, aInset = sa > 0 ? 1f - b : b;
                    float bMain = sb > 0 ? 1f : 0f, bInset = sb > 0 ? 1f - b : b;
                    Vector3 pAlo = AxisVec(axisA, aMain, axisB, bInset, axisC, tLo);
                    Vector3 pAhi = AxisVec(axisA, aMain, axisB, bInset, axisC, tHi);
                    Vector3 pBlo = AxisVec(axisB, bMain, axisA, aInset, axisC, tLo);
                    Vector3 pBhi = AxisVec(axisB, bMain, axisA, aInset, axisC, tHi);
                    AddPoly(pAlo, pAhi, pBhi, true, pBlo, AxisVec(axisA, sa, axisB, sb, axisC, 0f));
                }
            }

            // Corner triangles — one per exposed convex corner.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                if (!Open(0, sx) || !Open(1, sy) || !Open(2, sz))
                {
                    continue;
                }

                float xI = sx > 0 ? 1f - b : b, yI = sy > 0 ? 1f - b : b, zI = sz > 0 ? 1f - b : b;
                Vector3 pX = new(sx > 0 ? 1f : 0f, yI, zI);
                Vector3 pY = new(xI, sy > 0 ? 1f : 0f, zI);
                Vector3 pZ = new(xI, yI, sz > 0 ? 1f : 0f);
                AddPoly(pX, pY, pZ, false, Vector3.zero, new Vector3(sx, sy, sz));
            }
        }
    }

    /// <summary>The plain-data result of <see cref="ChunkMesher.BuildGeometry"/>: the vertex/index/attribute
    /// lists plus precomputed flat normals and bounds, holding NO Unity Mesh objects — so it can be produced on
    /// a worker thread and uploaded later. <see cref="ToMeshes"/> performs the (main-thread-only) Mesh API
    /// calls to turn it into the render + collision meshes.</summary>
    public sealed class ChunkMeshData
    {
        public List<Vector3> Verts;
        public List<int> OpaqueTris;       // submesh 0 (BlockAtlas)
        public List<int> TransparentTris;  // submesh 1 (BlockAtlasTransparent — glass / force fields)
        public List<int> ColliderTris;     // solid faces only (fluids excluded) → the collision mesh
        public List<Color> Colors;
        public List<Vector2> Uvs;
        public List<Vector2> SkyUv;        // TEXCOORD1: skylight in .x, tint mode in .y
        public List<Vector4> LeafUv;       // TEXCOORD2: foliage cutout flag in .x, flora/hull/dye/bark tint in .yzw
        public List<Vector3> BlockLight;   // TEXCOORD3: propagated coloured block-light
        public List<Vector3> BlockLightDir;// TEXCOORD4: dominant block-light direction
        public List<Vector4> Tangents;
        public Vector3[] Normals;          // flat per-face normals (see ChunkMesher.AccumulateFlatNormals)
        public Bounds Bounds;
        public List<Vector4> Scatter;      // ground-detail scatter points (xyz = local pos, w = type); NOT uploaded to the mesh

        /// <summary>Uploads the data into a render mesh (opaque + see-through submeshes) and a separate
        /// fluid-excluded collision mesh. MUST run on the main thread (the Unity Mesh API is not thread-safe).</summary>
        public (Mesh Render, Mesh Collider) ToMeshes(Mesh reuseRender = null)
        {
            // Reuse the chunk's existing render Mesh on a rebuild (A3): Clear()+refill avoids allocating a fresh
            // Mesh — and leaking the old one — on every remesh. The collider mesh is always fresh, because it is
            // cooked asynchronously by Physics.BakeMesh and reusing a mesh mid-bake would be unsafe.
            var mesh = reuseRender != null ? reuseRender : new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            if (reuseRender != null)
            {
                mesh.Clear();
            }

            mesh.SetVertices(Verts);
            // Two submeshes sharing one vertex buffer: 0 = opaque (BlockAtlas), 1 = see-through
            // (BlockAtlasTransparent). The renderer is given both materials in the same order. Submesh 1
            // is empty for chunks with no glass/fields — an empty submesh just draws nothing.
            mesh.subMeshCount = 2;
            mesh.SetTriangles(OpaqueTris, 0);
            mesh.SetTriangles(TransparentTris, 1);
            mesh.SetColors(Colors);
            mesh.SetUVs(0, Uvs);
            mesh.SetUVs(1, SkyUv);
            mesh.SetUVs(2, LeafUv);
            mesh.SetUVs(3, BlockLight);
            mesh.SetUVs(4, BlockLightDir);
            mesh.SetTangents(Tangents);
            mesh.SetNormals(Normals);
            mesh.bounds = Bounds;

            // Collision mesh: same vertices, but only the solid (non-fluid) faces, so water/lava are passable.
            Mesh collider = null;
            if (ColliderTris.Count > 0)
            {
                collider = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                collider.SetVertices(Verts);
                collider.SetTriangles(ColliderTris, 0);
                collider.bounds = Bounds;
            }

            return (mesh, collider);
        }
    }
}
