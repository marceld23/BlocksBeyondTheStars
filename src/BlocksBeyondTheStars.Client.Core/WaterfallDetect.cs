// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Purely local, client-side detection of falling water (a waterfall column) from block ids alone — no
    /// worldgen or network data, so it works on every world and old saves, like <c>WaterSurface</c>.
    /// The server marks falling fluid internally but never sends it, so both the mesher (visible cascade) and
    /// the mist VFX (<c>WaterfallMistView</c>) infer it here from the same rule. Lives in Client.Core since
    /// #1745 so the rule is covered by plain .NET tests; the Unity scripts only call it.
    /// </summary>
    public static class WaterfallDetect
    {
        /// <summary>A <i>falling</i> water cell: water with water directly above (so it's fed from above, not a
        /// surface) and open to the air on its sides — a thin hanging stream. The air-side test is what
        /// separates a waterfall column from a filled deep pool (whose sides are water or bank), so a calm lake
        /// never reads as falling.
        /// <para>Two open sides settle it. ONE open side is a curtain (#1745): a row of waterfall blocks along a
        /// wall pours a sheet of side-by-side columns, and every column in it has water to its left and right,
        /// the wall behind and air only in front. Such a cell counts as falling when a neighbour along the sheet
        /// is the same kind of cell — water, water above, open toward the same side. A pool cell beside a bank
        /// has no such neighbour: its neighbours along the shore are closed on that side too.</para></summary>
        /// <param name="loaded">Optional "is this cell's chunk actually streamed in?" test. A cell we don't hold
        /// reads as air through <paramref name="worldBlock"/>, which used to make every submerged cell along the
        /// streamed region's edge a "waterfall" — scrolling streaks down the flanks of an ordinary deep sea
        /// (#987). Null = everything is loaded.</param>
        public static bool IsFalling(System.Func<int, int, int, BlockId> worldBlock, BlockId waterId, int x, int y, int z,
            System.Func<int, int, int, bool>? loaded = null)
        {
            if (worldBlock(x, y, z).Value != waterId.Value)
            {
                return false;
            }

            if (worldBlock(x, y + 1, z).Value != waterId.Value)
            {
                return false; // nothing above → it's the surface, not a falling column
            }

            bool px = OpenAir(worldBlock, loaded, x + 1, y, z);
            bool mx = OpenAir(worldBlock, loaded, x - 1, y, z);
            bool pz = OpenAir(worldBlock, loaded, x, y, z + 1);
            bool mz = OpenAir(worldBlock, loaded, x, y, z - 1);
            int air = (px ? 1 : 0) + (mx ? 1 : 0) + (pz ? 1 : 0) + (mz ? 1 : 0);
            if (air >= 2)
            {
                return true;
            }

            if (air == 0)
            {
                return false;
            }

            // Exactly one open side: a curtain cell if a neighbour along the sheet hangs the same way.
            if (px || mx)
            {
                int ox = px ? 1 : -1;
                return SheetNeighbour(worldBlock, loaded, waterId, x, y, z + 1, ox, 0)
                    || SheetNeighbour(worldBlock, loaded, waterId, x, y, z - 1, ox, 0);
            }

            int oz = pz ? 1 : -1;
            return SheetNeighbour(worldBlock, loaded, waterId, x + 1, y, z, 0, oz)
                || SheetNeighbour(worldBlock, loaded, waterId, x - 1, y, z, 0, oz);
        }

        /// <summary>A cell that belongs to the same hanging sheet: water, fed from above, and open to the air
        /// on the same side (<paramref name="ox"/>/<paramref name="oz"/>) as the cell asking.</summary>
        private static bool SheetNeighbour(System.Func<int, int, int, BlockId> worldBlock, System.Func<int, int, int, bool>? loaded,
            BlockId waterId, int x, int y, int z, int ox, int oz)
            => worldBlock(x, y, z).Value == waterId.Value
            && worldBlock(x, y + 1, z).Value == waterId.Value
            && OpenAir(worldBlock, loaded, x + ox, y, z + oz);

        /// <summary>Air the column could actually fall through: empty AND in a chunk we hold.</summary>
        private static bool OpenAir(System.Func<int, int, int, BlockId> worldBlock, System.Func<int, int, int, bool>? loaded, int x, int y, int z)
            => worldBlock(x, y, z).IsAir && (loaded == null || loaded(x, y, z));

        /// <summary>If (x,y,z) is the LANDING cell a waterfall rests on — i.e. it is not itself falling water but
        /// the cell directly above it is — return how many falling-water cells stack above it (the drop height),
        /// counted up to <paramref name="cap"/>. Otherwise 0. The mist source sits just above this cell. This is
        /// what lets the caller spawn spray only where a real drop terminates, once per column.</summary>
        public static int ImpactDrop(System.Func<int, int, int, BlockId> worldBlock, BlockId waterId, int x, int y, int z, int cap)
        {
            if (IsFalling(worldBlock, waterId, x, y, z))
            {
                return 0; // mid-column, not a landing
            }

            if (!IsFalling(worldBlock, waterId, x, y + 1, z))
            {
                return 0; // nothing falling onto this cell
            }

            int h = 0;
            for (int d = 1; d <= cap; d++)
            {
                if (!IsFalling(worldBlock, waterId, x, y + d, z))
                {
                    break;
                }

                h++;
            }

            return h;
        }
    }
}
