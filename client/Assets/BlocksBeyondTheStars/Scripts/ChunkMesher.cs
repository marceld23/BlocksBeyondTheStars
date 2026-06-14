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

        /// <summary>Builds the render mesh (opaque + see-through submeshes) and a separate collision mesh that
        /// excludes fluids (water/lava), so the player falls into water/lava instead of standing on it while
        /// glass/force-fields still block. Both share vertex positions; the collider has no normals/uvs.</summary>
        public static (Mesh Render, Mesh Collider) Build(ChunkData chunk, GameContent content, System.Func<int, int, int, BlockId> worldBlock, BlockTextureAtlas atlas = null,
            System.Func<BlockId, Color> floraTint = null, System.Func<BlockId, Color> paintTint = null,
            IReadOnlyList<(Vector3i Pos, int Rgb)> lights = null)
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
                // shader falls back to the planet's uniform flora hue. Tree trunks keep their bark colour.
                bool isFlora = IsFloraBlock(content, id);
                Color speciesTint = isFlora && floraTint != null ? floraTint(id) : Color.black;
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
                float floraFlag = dyed ? 3f : isFlora ? 1f : painted ? 2f : 0f;
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
                    AddCrossPlant(verts, tris, colors, uvs, tangents, skyUv, leafUv, blockLight, blockLightDir,
                        new Vector3(x, y, z), plantCol, uv, plantSky, speciesTint, plantBl, plantBlDir, plantH, plantW);
                    continue;
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

                    // A submerged fluid cell (the same fluid sits directly above it) must NOT draw its vertical
                    // SIDE faces: they'd paint the surface-looking water tile onto an underwater edge — e.g. the
                    // step between deep (swimmable) and shallow water — which looks wrong seen from below (B43).
                    // Only the true top layer (air above) keeps its faces, so the real water surface still shows.
                    if (drawFace && dir.Y == 0 && IsFluidBlock(content, id) && worldBlock(wx, wy + 1, wz).Value == id.Value)
                    {
                        drawFace = false;
                    }

                    if (!drawFace)
                    {
                        continue; // face hidden
                    }

                    float s = FaceShade(f);
                    // With an atlas the lit shader does the directional shading, so the vertex colour
                    // carries material params instead: r=gloss, g=metal, b=per-face AO (subtle edge
                    // definition). Without one, fall back to the flat palette colour x face shade.
                    var col = atlas != null
                        ? new Color(matR, matG, s, emission)
                        : new Color(baseColor.r * s, baseColor.g * s, baseColor.b * s, 1f);
                    int faceBase = verts.Count;
                    AddFace(verts, transparent ? trisT : tris, colors, uvs, tangents, new Vector3(x, y, z), f, col, uv,
                        dir.Y != 0 ? uvRot : 0); // rotate only top/bottom faces — sides keep their up-orientation
                    if (collidable)
                    {
                        colliderTris.Add(faceBase); colliderTris.Add(faceBase + 1); colliderTris.Add(faceBase + 2);
                        colliderTris.Add(faceBase); colliderTris.Add(faceBase + 2); colliderTris.Add(faceBase + 3);
                    }

                    float sky = Skylight(nx, ny, nz); // soft sky-occlusion (cave mouths feather, deep stays dark)
                    skyUv.Add(new Vector2(sky, floraFlag)); skyUv.Add(new Vector2(sky, floraFlag));
                    skyUv.Add(new Vector2(sky, floraFlag)); skyUv.Add(new Vector2(sky, floraFlag));
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
                    else
                    {
                        var tint = dyed ? dye : painted ? paint : speciesTint;
                        var leaf = new Vector4(leafFlag, tint.r, tint.g, tint.b);
                        leafUv.Add(leaf); leafUv.Add(leaf); leafUv.Add(leaf); leafUv.Add(leaf);
                    }
                }
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            // Two submeshes sharing one vertex buffer: 0 = opaque (BlockAtlas), 1 = see-through
            // (BlockAtlasTransparent). The renderer is given both materials in the same order. Submesh 1
            // is empty for chunks with no glass/fields — an empty submesh just draws nothing.
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tris, 0);
            mesh.SetTriangles(trisT, 1);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, skyUv); // skylight in TEXCOORD1.x, tint mode in .y (1 flora, 2 hull paint, 3 player dye)
            mesh.SetUVs(2, leafUv); // foliage cutout flag in TEXCOORD2.x, flora/hull/dye tint in .yzw
            mesh.SetUVs(3, blockLight); // TEXCOORD3.xyz: propagated coloured block-light (placed lights illuminate)
            mesh.SetUVs(4, blockLightDir); // TEXCOORD4.xyz: dominant block-light direction (N·L shaping + glint)
            mesh.SetTangents(tangents);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Collision mesh: same vertices, but only the solid (non-fluid) faces, so water/lava are passable.
            Mesh collider = null;
            if (colliderTris.Count > 0)
            {
                collider = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                collider.SetVertices(verts);
                collider.SetTriangles(colliderTris, 0);
                collider.RecalculateBounds();
            }

            return (mesh, collider);
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

        /// <summary>Adds a cross-billboard plant: two diagonal cutout quads through the cell, each emitted with
        /// BOTH windings (the atlas shaders cull back faces), slightly inset to avoid z-fighting. Render-only —
        /// no collider triangles, so small plants are walk-through. <paramref name="heightScale"/> /
        /// <paramref name="widthScale"/> give each plant its own size (so a field reads as tall + short tufts).</summary>
        private static void AddCrossPlant(List<Vector3> verts, List<int> tris, List<Color> colors, List<Vector2> uvs,
            List<Vector4> tangents, List<Vector2> skyUv, List<Vector4> leafUv, List<Vector3> blockLight, List<Vector3> blockLightDir, Vector3 cell, Color col, Rect uv, float sky,
            Color tint, Vector3 bl, Vector3 blDir, float heightScale = 1f, float widthScale = 1f)
        {
            // The two crossed planes' floor diagonals, inset symmetrically from the cell centre (so the width
            // scales about the middle), clamped inside the cell to avoid bleeding into neighbours.
            float half = Mathf.Clamp(0.42f * widthScale, 0.18f, 0.49f);
            float lo = 0.5f - half, hi = 0.5f + half;
            var diagonals = new[]
            {
                (A: new Vector3(cell.x + lo, cell.y, cell.z + lo), B: new Vector3(cell.x + hi, cell.y, cell.z + hi)),
                (A: new Vector3(cell.x + hi, cell.y, cell.z + lo), B: new Vector3(cell.x + lo, cell.y, cell.z + hi)),
            };

            foreach (var (a, b) in diagonals)
            {
                var up = Vector3.up * heightScale;
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

        private static void AddFace(List<Vector3> verts, List<int> tris, List<Color> colors, List<Vector2> uvs, List<Vector4> tangents, Vector3 p, int face, Color color, Rect uv, int uvRot = 0)
        {
            int baseIndex = verts.Count;
            Vector3[] q = FaceQuad(p, face);
            verts.Add(q[0]); verts.Add(q[1]); verts.Add(q[2]); verts.Add(q[3]);
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);

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
    }
}
