// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The mesher's part of the High details (epic #3066): ground detail points for the grass carpet.</summary>
    public static partial class ChunkMesher
    {
        /// <summary>#3056: the grass-like ground blocks that grow the High shell-grass carpet. The shells take their
        /// colour from the block's own atlas tile, so any of them matches its ground (texture packs included).</summary>
        internal static bool IsCarpetKey(string key) => key is "grass" or "alien_grass" or "mycelium";

        /// <summary>Ground detail of one open-topped planet ground cell (the caller checked the air above). The T0
        /// scatter strews a sparse, world-cell-hashed set of tufts (identical on all clients) on natural ground under the
        /// open sky (<see cref="GroundScatterRules"/>: <paramref name="edited"/> = a built cell, #3118/#3119); a carpet host
        /// records its top for <see cref="GrassShells"/> — recorded on every preset, so switching to High needs no
        /// remesh. <paramref name="skylight"/> and <paramref name="light"/> (the placed block light, display-ready) are those
        /// of the air cell above — what lights a tuft and the carpet at night next to a torch (#3120).</summary>
        private static void AddGroundDetail(ChunkMeshData data, BlockTraits traits, BlockId id, int x, int y, int z,
            int wx, int wz, float skylight, Vector3 light, bool edited)
        {
            int stype = traits.ScatterOf(id);
            if (stype >= 0 && GroundScatterRules.Accepts(skylight, edited))
            {
                int sh = unchecked(wx * 374761393 + wz * 668265263 + stype * 1013904223);
                if ((uint)sh % 7u == 0u)
                {
                    data.Scatter.Add(new Vector4(x + 0.5f, y + 1f, z + 0.5f, stype));
                    data.ScatterLight.Add(new Vector4(light.x, light.y, light.z, skylight)); // in lockstep with Scatter
                }
            }

            if (traits.CarpetOf(id))
            {
                // w = block id (integer part) + skylight (fraction, < 1) — both exact enough in a float.
                data.GrassTops.Add(new Vector4(x + 0.5f, y + 1f, z + 0.5f, id.Value + Mathf.Clamp(skylight, 0f, 0.999f)));
                data.GrassLight.Add(light); // in lockstep with GrassTops
            }
        }

        private sealed partial class BlockTraits
        {
            private bool[] _carpet;
            private float[] _relief;

            /// <summary>#3055: the parallax relief of a block's faces (<see cref="BlockRelief"/>), built lazily like the
            /// carpet table.</summary>
            public float ReliefOf(BlockId id)
            {
                var relief = _relief ?? BuildRelief();
                return id.Value < relief.Length ? relief[id.Value] : 0f;
            }

            private float[] BuildRelief()
            {
                var relief = new float[_flags.Length];
                for (int i = 0; i < relief.Length; i++)
                {
                    var id = new BlockId((ushort)i);
                    // Cutout, flora and see-through blocks never get depth: their alpha edges and plants stay crisp.
                    bool flat = (_flags[i] & (TraitTransparent | TraitFlora | TraitFoliage | TraitSlimProp | TraitCutoutCube)) != 0; // #3085: the fence
                    relief[i] = flat ? 0f : BlockRelief.For(Content.BlockById(id));
                }

                _relief = relief;
                return relief;
            }

            /// <summary>Whether a block grows the #3056 grass carpet. The table is built lazily (a benign race between
            /// worker threads builds the same array twice).</summary>
            public bool CarpetOf(BlockId id)
            {
                var carpet = _carpet ?? BuildCarpet();
                return id.Value < carpet.Length && carpet[id.Value];
            }

            private bool[] BuildCarpet()
            {
                var carpet = new bool[_flags.Length];
                for (int i = 0; i < carpet.Length; i++)
                {
                    carpet[i] = IsCarpetKey(Content.BlockById(new BlockId((ushort)i))?.Key);
                }

                _carpet = carpet;
                return carpet;
            }
        }
    }

    /// <summary>The per-face parallax relief of a mesh build (#3055).</summary>
    public sealed partial class ChunkMeshData
    {
        /// <summary>Relief is packed at this fraction of its value (relief 1 → SNorm8 114): <c>Normal.w</c> above 0.95 is
        /// what a mesh built with plain 3-component normals reads (the GPU's default w = 1), and the shader keeps those
        /// flat.</summary>
        public const float ReliefScale = 114f / 127f;

        /// <summary>Faces with relief, in vertex order: (first vertex index &lt;&lt; 8) | relief byte (1..114). Sparse — only
        /// rock, ore, brick and metal faces carry an entry; every other vertex keeps <c>Normal.w = 0</c>.</summary>
        public readonly List<int> ReliefRuns = new List<int>();

        /// <summary>#3056: the placed block light above each <see cref="GrassTops"/> entry (same index), so the carpet glows
        /// with the ground next to a torch at night.</summary>
        public readonly List<Vector3> GrassLight = new List<Vector3>();

        /// <summary>#3120: the light above each <see cref="Scatter"/> tuft (same index): rgb = the placed block light, a = the
        /// skylight — the tuft shades like the ground under it instead of going black at night next to a torch.</summary>
        public readonly List<Vector4> ScatterLight = new List<Vector4>();

        /// <summary>Records the relief of the quad that starts at <paramref name="firstVertex"/> (4 vertices).</summary>
        public void MarkRelief(int firstVertex, float relief)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(relief * ReliefScale * 127f), 0, 114);
            if (r > 0 && firstVertex >= 0)
            {
                ReliefRuns.Add((firstVertex << 8) | r);
            }
        }

        /// <summary>Writes the recorded relief into the packed normals' w (SNorm8, see <see cref="ReliefScale"/>).</summary>
        private void ApplyRelief(PackedVertex[] packed)
        {
            int n = Verts.Count;
            foreach (int run in ReliefRuns)
            {
                int first = run >> 8;
                sbyte r = (sbyte)(run & 0xFF);
                for (int v = first; v < first + 4 && v < n; v++)
                {
                    packed[v].Nw = r;
                }
            }
        }
    }
}
