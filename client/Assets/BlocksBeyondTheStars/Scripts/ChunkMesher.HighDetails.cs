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
            int wx, int wz, float skylight, Vector3 light, bool edited, bool underPlant = false)
        {
            // Cover under plants: under a plant only the carpet grows — no loose tuft, no dry scrub, no pebble
            // (those would poke through the plant, and under a water plant they would be plain wrong).
            int stype = underPlant ? -1 : traits.ScatterOf(id);
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

            // Ground cover: bare natural ground under the open sky grows dry tufts in loose patches and
            // carries a few round pebbles — drawn by GrassClumps in the ground's own colour, so recorded only where the
            // clumps run (GameLook.RecordGrassClumps).
            int scrub = GameLook.GroundCover && GameLook.RecordGrassClumps && !underPlant ? traits.ScrubOf(id) : 0;
            if (scrub > 0 && GroundScatterRules.Accepts(skylight, edited))
            {
                uint h = unchecked((uint)((wx * 83492791) ^ (wz * 19349663) ^ (id.Value * 73856093)));
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                float patch = ScrubPatch(wx, wz);
                int kind = -1;
                if (scrub == 1 && (h & 0xFFu) < (uint)(patch * patch * 150f))
                {
                    kind = 0; // a dry tuft: up to six cells in ten in the heart of a patch, none between the patches
                }
                else if (((h >> 8) & 0xFFu) < 15u)
                {
                    kind = 1; // pebbles: about one cell in seventeen
                }

                if (kind >= 0)
                {
                    data.ScrubTops.Add(new Vector4(x + 0.5f, y + 1f, z + 0.5f, id.Value + Mathf.Clamp(skylight, 0f, 0.999f)));
                    data.ScrubLight.Add(new Vector4(light.x, light.y, light.z, kind)); // in lockstep with ScrubTops
                }
            }
        }

        /// <summary>Ground cover: 0..1, a smooth field seven blocks wide — where it is high the dry tufts stand
        /// close, where it is low the ground stays bare, so they grow in patches instead of an even sprinkle.</summary>
        private static float ScrubPatch(int wx, int wz)
        {
            float fx = wx / 7f, fz = wz / 7f;
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0, tz = fz - z0;
            tx = tx * tx * (3f - (2f * tx));
            tz = tz * tz * (3f - (2f * tz));
            float a = Mathf.Lerp(ScrubLattice(x0, z0), ScrubLattice(x0 + 1, z0), tx);
            float b = Mathf.Lerp(ScrubLattice(x0, z0 + 1), ScrubLattice(x0 + 1, z0 + 1), tx);
            return Mathf.Lerp(a, b, tz);
        }

        private static float ScrubLattice(int x, int z)
        {
            uint h = unchecked((uint)((x * 73856093) ^ (z * 19349663)));
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFFu) / 65535f;
        }

        /// <summary>Ground cover: what a block's bare top carries — 1 = dry tufts and pebbles (soft ground),
        /// 2 = pebbles only (rock — and Mars's red sand, where nothing grows, #4018), 0 = nothing.</summary>
        internal static byte ScrubKindOf(string key) => key switch
        {
            "sand" or "dirt" or "mud" or "peat" or "ash" or "salt" => 1,
            "stone" or "basalt" or "granite" or "sandstone" or "moss_stone" or "red_rock" or "red_sand" => 2,
            _ => 0,
        };

        private sealed partial class BlockTraits
        {
            private bool[] _carpet;
            private float[] _relief;
            private byte[] _scrub;
            private int _vine = -1;

            /// <summary>Crown vines: the block whose body hangs under the crowns (the hanging kelp), 0 when
            /// the content has none. Looked up once.</summary>
            public ushort VineOf()
            {
                if (_vine < 0)
                {
                    // The hanging kelp's body: one stem with leaves down its length reads as a vine (the glowing thread's
                    // two bare strands are too thin to see under a crown); the thread is the fallback.
                    var def = Content.GetBlock("flora_hangkelp") ?? Content.GetBlock("flora_glowthread");
                    _vine = def != null && PlantFamilyOf(def.NumericId) >= 0 ? def.NumericId.Value : 0;
                }

                return (ushort)_vine;
            }

            private byte[] _coverUnder;

            /// <summary>Cover under plants: how much ground cover grows under this plant — 2 = the full grass
            /// (a plant on a stem: stalk flower, bush on a stem, sapling, the cup bloom), 1 = low grass (leaves or blades
            /// near the ground: rosette, tuft, strand, pitcher), 0 = none (not a plant with a body, a low cushion, a
            /// solid or a water plant, a crystal). Built lazily like the scrub table.</summary>
            public byte CoverUnderOf(BlockId id)
            {
                var table = _coverUnder ?? BuildCoverUnder();
                return id.Value < table.Length ? table[id.Value] : (byte)0;
            }

            private byte[] BuildCoverUnder()
            {
                var table = new byte[_flags.Length];
                for (int i = 0; i < table.Length; i++)
                {
                    table[i] = PlantFamilyOf(new BlockId((ushort)i)) switch
                    {
                        PlantForms.Flower or PlantForms.Bush or PlantForms.Sapling or PlantForms.Bloom => 2,
                        PlantForms.Rosette or PlantForms.Tuft or PlantForms.Strand or PlantForms.Pitcher => 1,
                        _ => 0,
                    };
                }

                _coverUnder = table;
                return table;
            }

            /// <summary>Ground cover: <see cref="ScrubKindOf"/> per block id, built lazily like the carpet table.</summary>
            public byte ScrubOf(BlockId id)
            {
                var scrub = _scrub ?? BuildScrub();
                return id.Value < scrub.Length ? scrub[id.Value] : (byte)0;
            }

            private byte[] BuildScrub()
            {
                var scrub = new byte[_flags.Length];
                for (int i = 0; i < scrub.Length; i++)
                {
                    scrub[i] = ScrubKindOf(Content.BlockById(new BlockId((ushort)i))?.Key);
                }

                _scrub = scrub;
                return scrub;
            }

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
