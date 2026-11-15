// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3700: THE CRATER RIM of a sunken civilization site (see GameServerCivilizationStamp.cs). Where the sea was too shallow
/// and the site was dug into the floor, the writer used to leave a shaft with sheer walls up to 36 blocks high (the Grand
/// Technicians' giant ruin on a sandbank, its cubes invisible at the bottom). Now the natural floor around the footprint is
/// lowered to a slope, one block down per block toward the footprint, from the site's ground up, out to
/// <see cref="LandmarkInstance.SunkenRim"/> blocks; every lowered cell becomes water, the new top keeps the floor's own
/// surface block (so no cave below opens into the sea), and the flora that stood on the old floor goes with it.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Writes the crater rim of a sunken site into the site's overlay. Returns the cells written.</summary>
    private int WriteSunkenRim(IStructureCellSink sink, LandmarkInstance lm, int waterTop, BlockId water)
    {
        int rim = lm.SunkenRim;
        if (rim <= 0)
        {
            return 0;
        }

        var others = RimKeepClear(lm);
        int x0 = lm.Origin.X, z0 = lm.Origin.Z;
        int x1 = x0 + lm.Structure.Width - 1, z1 = z0 + lm.Structure.Length - 1;
        int n = 0;
        for (int wx = x0 - rim; wx <= x1 + rim; wx++)
        {
            int dx = System.Math.Max(System.Math.Max(x0 - wx, wx - x1), 0);
            for (int wz = z0 - rim; wz <= z1 + rim; wz++)
            {
                int dz = System.Math.Max(System.Math.Max(z0 - wz, wz - z1), 0);
                double d = System.Math.Sqrt((dx * dx) + (dz * dz));
                if ((dx == 0 && dz == 0) || d > rim)
                {
                    continue; // the footprint itself, or past the rim
                }

                int x = WorldConstants.WrapX(wx, _world.Circumference);
                if (OverlapsFootprint(x, wz, 0, 0, others, 0))
                {
                    continue; // another structure's footprint or a pad keeps its ground
                }

                int slope = lm.GroundY + (int)System.Math.Ceiling(d) + RimJitter(x, wz, lm.Seed);
                n += LowerRimColumn(sink, x, wz, slope, waterTop, water);
            }
        }

        return n;
    }

    /// <summary>The registered footprints the rim keeps off: every one but the site's own (its box and its rim share its
    /// centre).</summary>
    private List<(int Cx, int Cz, int Hw, int Hl)> RimKeepClear(LandmarkInstance lm)
    {
        var (cx, cz, _, _) = lm.Rect;
        return KeepClearFor(StructureTypeRules.Civilization, LandmarkReserved()).Where(r => r.Cx != cx || r.Cz != cz).ToList();
    }

    /// <summary>A little unevenness on the slope (0 or 1 block, a hash of the column and the site's seed), so the rim does not
    /// read as a cut pyramid.</summary>
    private static int RimJitter(int x, int z, long seed)
        => (int)(((uint)((x * 73856093) ^ (z * 19349663) ^ (int)seed) >> 7) & 1u);

    /// <summary>Lowers one natural column to <paramref name="slope"/>: when its floor lies under the water and above the slope,
    /// every cell from the slope up to the floor becomes water, the new top takes the floor's surface block and the flora on the
    /// old floor goes. Dry land (an island, a shore) is never touched. Returns the cells written.</summary>
    private int LowerRimColumn(IStructureCellSink sink, int x, int z, int slope, int waterTop, BlockId water)
    {
        // The seabed, not SurfaceHeight: over the sea that is the water's surface.
        if (NaturalSeabed(x, z) is not { } bed || bed >= waterTop || bed <= slope)
        {
            return 0;
        }

        var surface = sink.Get(x, bed, z);
        int n = 0;
        for (int y = slope + 1; y <= bed; y++, n++)
        {
            sink.Put(x, y, z, water);
        }

        if (_content.BlockById(surface) is { Solid: true } def && def.Category != "flora")
        {
            sink.Put(x, slope, z, surface);
            n++;
        }

        return n + FloodFloatingFlora(sink, x, bed + 1, z, waterTop, water);
    }

    /// <summary>The natural seabed of a column (its last solid cell under the water), or null where no liquid water covers it
    /// (dry land, lava, a sea frozen through).</summary>
    private int? NaturalSeabed(int x, int z)
        => _generator.TryGetWaterSurface(_world.Planet, x, z, out _, out int bed) ? bed : null;

    /// <summary>Test seam: <see cref="NaturalSeabed"/>.</summary>
    internal int? NaturalSeabedForTest(int x, int z) => NaturalSeabed(x, z);
}
