using System.Collections.Generic;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.World;
using UnityEngine;

namespace Spacecraft.Client
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

        /// <summary>Builds the render mesh (opaque + see-through submeshes) and a separate collision mesh that
        /// excludes fluids (water/lava), so the player falls into water/lava instead of standing on it while
        /// glass/force-fields still block. Both share vertex positions; the collider has no normals/uvs.</summary>
        public static (Mesh Render, Mesh Collider) Build(ChunkData chunk, GameContent content, System.Func<int, int, int, BlockId> worldBlock, BlockTextureAtlas atlas = null)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();   // submesh 0: opaque blocks
            var trisT = new List<int>();  // submesh 1: see-through blocks (glass / force fields)
            var colliderTris = new List<int>(); // solid faces only (no fluids) → the collision mesh
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var skyUv = new List<Vector2>(); // per-vertex skylight (1 = sees sky, 0 = underground/indoors)
            var leafUv = new List<Vector2>(); // x = foliage flag (1 = clip the tile's alpha → cutout leaves)
            var tangents = new List<Vector4>(); // per-face tangents for normal mapping

            var origin = WorldConstants.ChunkOrigin(chunk.Coord);
            int n = WorldConstants.ChunkSize;

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
                // Flora flag (TEXCOORD1.y): the block shader desaturates + re-tints these to the planet's
                // uniform flora hue — the small plants AND tree crowns (B38), so a planet's foliage reads as one
                // hue; tree trunks keep their natural bark colour.
                float floraFlag = IsFloraBlock(content, id) ? 1f : 0f;
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
                    AddFace(verts, transparent ? trisT : tris, colors, uvs, tangents, new Vector3(x, y, z), f, col, uv);
                    if (collidable)
                    {
                        colliderTris.Add(faceBase); colliderTris.Add(faceBase + 1); colliderTris.Add(faceBase + 2);
                        colliderTris.Add(faceBase); colliderTris.Add(faceBase + 2); colliderTris.Add(faceBase + 3);
                    }

                    float sky = Skylight(nx, ny, nz); // soft sky-occlusion (cave mouths feather, deep stays dark)
                    skyUv.Add(new Vector2(sky, floraFlag)); skyUv.Add(new Vector2(sky, floraFlag));
                    skyUv.Add(new Vector2(sky, floraFlag)); skyUv.Add(new Vector2(sky, floraFlag));
                    var leaf = new Vector2(leafFlag, 0f);
                    leafUv.Add(leaf); leafUv.Add(leaf); leafUv.Add(leaf); leafUv.Add(leaf);
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
            mesh.SetUVs(1, skyUv); // skylight in TEXCOORD1.x, flora-tint flag in .y
            mesh.SetUVs(2, leafUv); // foliage cutout flag in TEXCOORD2.x
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
                && (key.StartsWith("flora_", System.StringComparison.Ordinal) || key == "tree_leaves");
        }

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

            return key == "tree_leaves"
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

        private static void AddFace(List<Vector3> verts, List<int> tris, List<Color> colors, List<Vector2> uvs, List<Vector4> tangents, Vector3 p, int face, Color color, Rect uv)
        {
            int baseIndex = verts.Count;
            Vector3[] q = FaceQuad(p, face);
            verts.Add(q[0]); verts.Add(q[1]); verts.Add(q[2]); verts.Add(q[3]);
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);

            // Tangent = world-space U direction (q0→q3, matching the UV mapping below); w = bitangent
            // handedness. Lets the shader transform the tangent-space normal map into world space.
            Vector3 e1 = q[1] - q[0], e3 = q[3] - q[0];
            Vector3 nrm = Vector3.Cross(e1, e3).normalized;
            Vector3 tan = e3.normalized;
            float hand = Vector3.Dot(Vector3.Cross(nrm, tan), e1) < 0f ? -1f : 1f;
            var tv = new Vector4(tan.x, tan.y, tan.z, hand);
            tangents.Add(tv); tangents.Add(tv); tangents.Add(tv); tangents.Add(tv);
            uvs.Add(new Vector2(uv.xMin, uv.yMin));
            uvs.Add(new Vector2(uv.xMin, uv.yMax));
            uvs.Add(new Vector2(uv.xMax, uv.yMax));
            uvs.Add(new Vector2(uv.xMax, uv.yMin));
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
