using Spacecraft.Shared.Primitives;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Classifies a water SURFACE cell (water with air above) into a visual water body type by probing
    /// the horizontal extent of the connected surface around it. Purely local + client-side — no worldgen
    /// or network data involved — so it works on every world, old saves included.
    ///
    /// The result: x = mode (0 none, 1 calm lake/pond, 2 open water → gentle waves + coast foam,
    /// 3 river/stream → fast-flow ripples), y = foam factor (1 at the shoreline fading to 0 three
    /// blocks out), zw = flow axis for rivers (unit X or Z — the channel's long axis). The mesher
    /// repacks this per-vertex (corner-smoothing foam and turning mode into a wave-amplitude factor)
    /// before it reaches the transparent block shader via TEXCOORD2.
    /// </summary>
    public static class WaterSurface
    {
        /// <summary>How far the shore probes look in each direction. Caps the cost per surface cell and
        /// sets the "open water" scale: a body wider than ~2× this in one axis gets waves.</summary>
        public const int ScanCap = 12;

        private const int RiverMaxWidth = 5;          // total width ≤ this (either axis) → flowing channel
        private const int OpenWaterSpan = 2 * ScanCap; // one axis this wide (cap hit both ways) → waves

        public static Vector4 Classify(System.Func<int, int, int, BlockId> worldBlock, BlockId waterId, int wx, int wy, int wz)
        {
            int px = Run(worldBlock, waterId, wx, wy, wz, 1, 0);
            int nx = Run(worldBlock, waterId, wx, wy, wz, -1, 0);
            int pz = Run(worldBlock, waterId, wx, wy, wz, 0, 1);
            int nz = Run(worldBlock, waterId, wx, wy, wz, 0, -1);
            int spanX = px + nx + 1;
            int spanZ = pz + nz + 1;

            // Distance to the nearest shore (1 = right at the waterline) → foam band, full at the line,
            // gone three blocks out. Mid-ocean cells hit the scan cap everywhere and get no foam.
            int shoreDist = Mathf.Min(Mathf.Min(px, nx), Mathf.Min(pz, nz)) + 1;
            float foam = Mathf.Clamp01((4f - shoreDist) / 3f);

            if (Mathf.Min(spanX, spanZ) <= RiverMaxWidth)
            {
                // A narrow channel reads as a flowing river/brook; the flow runs along the LONG axis.
                return spanX >= spanZ ? new Vector4(3f, foam, 1f, 0f) : new Vector4(3f, foam, 0f, 1f);
            }

            if (Mathf.Max(spanX, spanZ) >= OpenWaterSpan)
            {
                return new Vector4(2f, foam, 0f, 0f); // open water: gentle waves + coastal foam
            }

            return new Vector4(1f, foam, 0f, 0f); // bounded basin: a calm lake/pond
        }

        /// <summary>Consecutive same-water cells beyond (wx,wz) in one direction, capped at <see cref="ScanCap"/>.
        /// Anything else — bank, beach, air over a lower pool (a waterfall lip), other fluids — ends the run.</summary>
        private static int Run(System.Func<int, int, int, BlockId> worldBlock, BlockId waterId, int wx, int wy, int wz, int dx, int dz)
        {
            for (int d = 1; d <= ScanCap; d++)
            {
                if (worldBlock(wx + dx * d, wy, wz + dz * d).Value != waterId.Value)
                {
                    return d - 1;
                }
            }

            return ScanCap;
        }
    }
}
