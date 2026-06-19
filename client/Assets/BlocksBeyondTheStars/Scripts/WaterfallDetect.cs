using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Purely local, client-side detection of falling water (a waterfall column) from block ids alone — no
    /// worldgen or network data, so it works on every world and old saves, like <see cref="WaterSurface"/>.
    /// The server marks falling fluid internally but never sends it, so both the mesher (visible cascade) and
    /// the mist VFX (<c>WaterfallMistView</c>) infer it here from the same rule.
    /// </summary>
    public static class WaterfallDetect
    {
        /// <summary>A <i>falling</i> water cell: water with water directly above (so it's fed from above, not a
        /// surface) and at least two of its four horizontal neighbours open to air — a thin hanging stream.
        /// The air-side test is what separates a waterfall column from a filled deep pool (whose sides are
        /// water or bank), so a calm lake never reads as falling.</summary>
        public static bool IsFalling(System.Func<int, int, int, BlockId> worldBlock, BlockId waterId, int x, int y, int z)
        {
            if (worldBlock(x, y, z).Value != waterId.Value)
            {
                return false;
            }

            if (worldBlock(x, y + 1, z).Value != waterId.Value)
            {
                return false; // nothing above → it's the surface, not a falling column
            }

            int air = 0;
            if (worldBlock(x + 1, y, z).IsAir) air++;
            if (worldBlock(x - 1, y, z).IsAir) air++;
            if (worldBlock(x, y, z + 1).IsAir) air++;
            if (worldBlock(x, y, z - 1).IsAir) air++;
            return air >= 2;
        }

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
