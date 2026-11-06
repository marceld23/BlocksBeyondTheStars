// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>A volcano's summit crater seen as a distant landmark (#3277) — what the client's crater smoke needs.</summary>
public readonly struct VolcanoCrater
{
    public VolcanoCrater(int x, int z, double radius, int rimY, int lavaTopY)
    {
        X = x;
        Z = z;
        Radius = radius;
        RimY = rimY;
        LavaTopY = lavaTopY;
    }

    /// <summary>The crater centre's world X in the frame of the query point: the short way round the wrap seam, so it
    /// may lie outside the canonical range (like the light sources the mesher gets).</summary>
    public int X { get; }

    /// <summary>The crater centre's world Z in the frame of the query point (the short way round the latitude seam).</summary>
    public int Z { get; }

    /// <summary>The crater's radius in blocks: the bowl ends at the rim.</summary>
    public double Radius { get; }

    /// <summary>The rim's surface height — the top solid block's Y one crater radius east of the centre.</summary>
    public int RimY { get; }

    /// <summary>The Y of the molten pool's topmost lava cell.</summary>
    public int LavaTopY { get; }
}

/// <summary>Read-only queries for the client's world effects: the volcano craters near a point (#3277) and whether this
/// world's geyser vents are volcanic (#3275). Pure functions of the seed, so the client's own generator (far terrain)
/// answers exactly what the server's world holds — no network data (partial of <see cref="WorldGenerator"/>).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>
    /// #3277: every volcano crater whose centre lies within <paramref name="radius"/> blocks (horizontally, measured the
    /// short way round both wrap seams) of (<paramref name="worldX"/>, <paramref name="worldZ"/>), appended to
    /// <paramref name="result"/> in the query point's own frame. Walks the volcano hotspot grid (a handful of cells per
    /// world, memoised), so it is cheap enough to ask every few seconds. Nothing on a world without volcanoes.
    /// </summary>
    public void VolcanoCratersNear(PlanetType planet, int worldX, int worldZ, int radius, List<VolcanoCrater> result)
    {
        if (radius < 0 || !HasVolcanoes(planet))
        {
            return;
        }

        long seed = PlanetSeed(planet);
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / VolcanoCellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / VolcanoCellSize));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        long reach = (long)radius * radius;
        for (int cz = 0; cz < nz; cz++)
            for (int cx = 0; cx < nx; cx++)
            {
                if (!TryGetVolcanoInCell(planet, seed, cx, cz, cw, ch, period, out var v))
                {
                    continue;
                }

                int dx = WorldConstants.WrapDeltaX(v.CenterX - worldX, _circumference);
                int dz = WorldConstants.WrapDeltaZ(v.CenterZ - worldZ, _circumference);
                if ((long)dx * dx + (long)dz * dz > reach)
                {
                    continue;
                }

                int rimY = SurfaceHeight(planet, v.CenterX + (int)System.Math.Round(v.CraterR), v.CenterZ);
                result.Add(new VolcanoCrater(worldX + dx, worldZ + dz, v.CraterR, rimY, CraterLavaTop(planet, v)));
            }
    }

    /// <summary>
    /// #3275: whether this world's geyser vents are volcanic (dark ash and embers) rather than water geysers (white steam
    /// and droplets) — read from the same traits that make the world vent at all, never from a list of planet keys: a
    /// world with lava or the volcanic tag vents ash; a wet world vents water; a dry world whose vents come only from its
    /// volcanoes vents ash too.
    /// </summary>
    public bool VentsAreVolcanic(PlanetType planet) => VentsVolcanicFor(planet, HasVolcanoes(planet));

    /// <summary>The rule of <see cref="VentsAreVolcanic"/> for a caller without a configured generator
    /// (<paramref name="volcanoWorld"/> = whether the world grows volcanoes).</summary>
    public static bool VentsVolcanicFor(PlanetType planet, bool volcanoWorld)
        => (planet.LavaAbundance ?? 0.0) > 0.0 || planet.HasTag(TerrainTag.Volcanic)
           || (volcanoWorld && GeyserWaterFor(planet) <= GeyserWetThreshold);

    /// <summary>The water share a world must exceed to grow water geysers (item 21 follow-up).</summary>
    private const double GeyserWetThreshold = 0.25;

    /// <summary>How wet the world is for its geysers: its water abundance, else 0.55 under an atmosphere and 0 without.</summary>
    private static double GeyserWaterFor(PlanetType planet)
        => planet.WaterAbundance
           ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
}
