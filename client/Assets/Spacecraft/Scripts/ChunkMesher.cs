using System.Collections.Generic;
using Spacecraft.Shared.Content;
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

        public static Mesh Build(ChunkData chunk, GameContent content, System.Func<int, int, int, BlockId> worldBlock)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var colors = new List<Color>();

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

                var color = BlockColor(content, id);
                int wx = origin.X + x, wy = origin.Y + y, wz = origin.Z + z;

                for (int f = 0; f < Faces.Length; f++)
                {
                    var dir = Faces[f];
                    if (!worldBlock(wx + dir.X, wy + dir.Y, wz + dir.Z).IsAir)
                    {
                        continue; // neighbour solid => face hidden
                    }

                    AddFace(verts, tris, colors, new Vector3(x, y, z), f, color);
                }
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Color BlockColor(GameContent content, BlockId id)
        {
            // Placeholder palette: deterministic colour from the block key until textures exist.
            var def = content.BlockById(id);
            if (def == null)
            {
                return Color.magenta;
            }

            int h = 0;
            foreach (char c in def.Key)
            {
                h = h * 31 + c;
            }

            var rng = new System.Random(h);
            return new Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
        }

        private static void AddFace(List<Vector3> verts, List<int> tris, List<Color> colors, Vector3 p, int face, Color color)
        {
            int baseIndex = verts.Count;
            Vector3[] q = FaceQuad(p, face);
            verts.Add(q[0]); verts.Add(q[1]); verts.Add(q[2]); verts.Add(q[3]);
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
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
