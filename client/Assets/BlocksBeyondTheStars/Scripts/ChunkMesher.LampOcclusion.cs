// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client
{
    public static partial class ChunkMesher
    {
        /// <summary>#3233: an opaque cube stops the headlamp completely.</summary>
        public const byte LampOpaque = 255;

        /// <summary>#3233: a tree crown's leaves let some light through the gaps.</summary>
        public const byte LampLeaves = 140;

        /// <summary>#3233: a shaped block (a slab, a stair, a wedge) fills part of its cell.</summary>
        public const byte LampShaped = 170;

        /// <summary>
        /// #3233: how much each block id stops the headlamp in the High occupancy volume, from the mesher's own trait table
        /// (so it can never disagree with what is drawn): air, glass, water, gas, fire, plants, torches, ladders and the
        /// other slim props are open; crown leaves partly; every other cube — lava and oil too — opaque. A shaped cell is
        /// read through <see cref="LampOpacity"/>.
        /// </summary>
        public static byte[] LampOpacityTable(GameContent content)
        {
            var traits = TraitsFor(content);
            int size = 1;
            foreach (var b in content.Blocks.Values)
            {
                size = System.Math.Max(size, b.NumericId.Value + 1);
            }

            var table = new byte[size];
            for (int i = 1; i < size; i++)
            {
                table[i] = LampOpacityOf(traits.FlagsOf(new BlockId((ushort)i)));
            }

            return table;
        }

        /// <summary>The opacity for one block's trait flags (see <see cref="LampOpacityTable"/>).</summary>
        private static byte LampOpacityOf(uint f)
        {
            if ((f & TraitFoliage) != 0)
            {
                return (f & TraitThinPlant) != 0 ? (byte)0 : LampLeaves; // a cross-billboard plant / a crown
            }

            if ((f & TraitSolidFlora) != 0)
            {
                return LampOpaque; // a solid plant body (a cactus, a pumpkin)
            }

            if ((f & TraitCutoutCube) != 0)
            {
                return LampLeaves; // the energy fence, a lift gate: rails with gaps
            }

            bool open = (f & (TraitTransparent | TraitFlora | TraitSlimProp | TraitWater | TraitFire | TraitLadder)) != 0;
            return open ? (byte)0 : LampOpaque;
        }

        /// <summary>#3233: one cell's opacity for the headlamp — the table's value, a partial one for a shaped opaque block.</summary>
        public static byte LampOpacity(byte[] table, ushort id, int shape)
        {
            byte v = id < table.Length ? table[id] : LampOpaque;
            return shape != 0 && v == LampOpaque ? LampShaped : v;
        }
    }
}
