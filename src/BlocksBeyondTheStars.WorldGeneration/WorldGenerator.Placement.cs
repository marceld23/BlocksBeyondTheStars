// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// What the landing-pad planner and the structure placer ask before seating a footprint (#2334, the spectacle
/// package): is there a band over the column — an arch bar, a crown, a sky island — and is there a void under it
/// — a mega-cavern, a tunnel, a daylight hall? Both read the memoised column machinery and answer nothing on a world
/// of an older generation: pads are pinned (#1989) and placements are frozen, but a NEW world of an older generation
/// must still plan the pads its goldens and pad tests know. (partial of <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>True when an extra band stands over this column's ground (any kind but a waterfall) on a
    /// generation-21 world — a seat under an arch or a crown. False everywhere on an older generation.</summary>
    public bool ColumnHasBandAbove(PlanetType planet, int worldX, int worldZ)
    {
        if (_terrainGeneration < WorldDescription.SpectacleGeneration || !HasExtraBands(planet))
        {
            return false;
        }

        System.Span<ColumnBand> bands = stackalloc ColumnBand[MaxColumnBands];
        int n = GetExtraBands(planet, worldX, worldZ, bands);
        if (n == 0)
        {
            return false;
        }

        int surface = SurfaceHeight(planet, worldX, worldZ);
        for (int i = 0; i < n; i++)
        {
            if (bands[i].Kind != BandKind.Waterfall && bands[i].Top > surface)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a mega-cavern or a tunnel span opens within <paramref name="depth"/> cells under this
    /// column's surface on a generation-21 world — a village over a hall, a plinth over a passage. The blob caves
    /// are not asked: a stamper's foundation plugs those like a pad's does. False on an older generation.</summary>
    public bool ColumnHasVoidBelow(PlanetType planet, int worldX, int worldZ, int depth)
    {
        if (_terrainGeneration < WorldDescription.SpectacleGeneration || planet.Void || planet.CaveThreshold <= 0.0)
        {
            return false;
        }

        int surface = SurfaceHeight(planet, worldX, worldZ);
        int floor = surface - depth;
        if (TryGetCavernSpan(planet, worldX, worldZ, out _, out int cavHi, out _) && cavHi >= floor)
        {
            return true;
        }

        System.Span<(int Lo, int Hi)> spans = stackalloc (int Lo, int Hi)[TunnelMaxSpans];
        int count = TunnelSpans(planet, worldX, worldZ, spans);
        for (int i = 0; i < count; i++)
        {
            if (spans[i].Hi >= floor && spans[i].Lo <= surface + 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The footprint probe the placers share (#2334): the centre and the four corners of a
    /// <paramref name="w"/> × <paramref name="l"/> box at <paramref name="ox"/>, <paramref name="oz"/> must carry
    /// no band above the ground and no void within <paramref name="foundationDepth"/> below it. Five columns, so a
    /// hall or a crown wider than the box is always seen; a thin bar across a corner may be missed, which the
    /// stamper's own fill then simply builds through.</summary>
    public bool FootprintClear(PlanetType planet, int ox, int oz, int w, int l, int foundationDepth)
    {
        if (_terrainGeneration < WorldDescription.SpectacleGeneration)
        {
            return true;
        }

        int x1 = ox + System.Math.Max(0, w - 1), z1 = oz + System.Math.Max(0, l - 1);
        int cx = ox + w / 2, cz = oz + l / 2;
        return Clear(cx, cz) && Clear(ox, oz) && Clear(x1, oz) && Clear(ox, z1) && Clear(x1, z1);

        bool Clear(int x, int z) => !ColumnHasBandAbove(planet, x, z) && !ColumnHasVoidBelow(planet, x, z, foundationDepth);
    }
}
