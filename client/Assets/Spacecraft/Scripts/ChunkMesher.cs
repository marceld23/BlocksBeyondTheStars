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

        public static Mesh Build(ChunkData chunk, GameContent content, System.Func<int, int, int, BlockId> worldBlock, BlockTextureAtlas atlas = null)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();

            var origin = WorldConstants.ChunkOrigin(chunk.Coord);
            int n = WorldConstants.ChunkSize;

            for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                var id = chunk.Get(x, y, z);
                if (id.IsAir)
                {
                    continue;
                }

                // With an atlas, the texture carries the colour and vertex colour is just the
                // per-face shade; without one, fall back to the flat palette × shade.
                Color baseColor = atlas == null ? BlockColor(content, id) : Color.white;
                Rect uv = atlas != null ? atlas.TileUv(id.Value) : new Rect(0f, 0f, 1f, 1f);
                // Per-block reflection params (gloss, metal) for the lit atlas shader.
                var mat = BlockMaterial(content, id);
                float matR = mat.x, matG = mat.y;
                int wx = origin.X + x, wy = origin.Y + y, wz = origin.Z + z;

                for (int f = 0; f < Faces.Length; f++)
                {
                    var dir = Faces[f];
                    if (!worldBlock(wx + dir.X, wy + dir.Y, wz + dir.Z).IsAir)
                    {
                        continue; // neighbour solid => face hidden
                    }

                    float s = FaceShade(f);
                    // With an atlas the lit shader does the directional shading, so the vertex colour
                    // carries material params instead: r=gloss, g=metal, b=per-face AO (subtle edge
                    // definition). Without one, fall back to the flat palette colour x face shade.
                    var col = atlas != null
                        ? new Color(matR, matG, s, 1f)
                        : new Color(baseColor.r * s, baseColor.g * s, baseColor.b * s, 1f);
                    AddFace(verts, tris, colors, uvs, new Vector3(x, y, z), f, col, uv);
                }
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
        private static Vector2 BlockMaterial(GameContent content, BlockId id)
        {
            var def = content.BlockById(id);
            switch (def?.Key)
            {
                case "glass": return new Vector2(0.90f, 0.0f);
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

        private static Color BlockColor(GameContent content, BlockId id)
        {
            var def = content.BlockById(id);
            if (def == null)
            {
                return Color.magenta;
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

        private static void AddFace(List<Vector3> verts, List<int> tris, List<Color> colors, List<Vector2> uvs, Vector3 p, int face, Color color, Rect uv)
        {
            int baseIndex = verts.Count;
            Vector3[] q = FaceQuad(p, face);
            verts.Add(q[0]); verts.Add(q[1]); verts.Add(q[2]); verts.Add(q[3]);
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
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
