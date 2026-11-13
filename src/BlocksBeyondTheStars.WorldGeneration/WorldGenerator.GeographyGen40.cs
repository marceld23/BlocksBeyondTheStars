// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 40 (#3555, the geography package): the surface height in three stages. The NATURAL stage is
/// <see cref="NaturalSurfaceHeight"/> — every older generation's whole answer. The VALLEY stage eases the ground down
/// toward every surface river (<see cref="RiverValleys"/>). The SITE-GRADE stage levels the box of every reserved site
/// (<see cref="SiteFlat"/>) and grades the land around it. <see cref="SurfaceHeight"/> answers with the last stage, so
/// every consumer — the chunk fill, the stamps, the server's queries, the client's far view — sees the same ground.
/// <para>What must not see the later stages reads the natural one: the calibration (the sea level is a percentile of the
/// natural heights), the river routing and rasterisation (the valleys are cut toward the routed water, so the routing
/// cannot depend on them), the planet-map previews (a sky body's bake must never build a river field) and the biome pick
/// (valleys and grades never move a biome border). Both later stages only follow rules that keep generated water where it
/// is: a river column keeps its carve, a column beside a river never sinks below that river's water, and land never sinks
/// into the sea.</para> (partial of <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>The reserved sites of the current world mode (<see cref="SetWorldMode"/>), a detached copy.</summary>
    private SiteFlat[] _sites = System.Array.Empty<SiteFlat>();

    /// <summary>The staged heights (generation 40), memoised like the natural ones and cleared with them.</summary>
    private readonly Dictionary<(string, long), int> _stagedCache = new();

    /// <summary>While above zero on this thread, <see cref="SurfaceHeight"/> answers with the natural stage — set by the
    /// river field's build (which routes over the natural ground and must never ask for a valley, i.e. for itself) and by
    /// the planet-map summaries. Thread-static because a world's field is built by whichever thread asks first.</summary>
    [System.ThreadStatic]
    private static int t_naturalOnly;

    /// <summary>The sites the current world mode levels (inspection, the layer export).</summary>
    public IReadOnlyList<SiteFlat> SiteFlats => _sites;

    /// <summary>True on a world of the geography generation (#3555).</summary>
    private bool GeographyWorld => _terrainGeneration >= WorldDescription.GeographyGeneration;

    /// <summary>
    /// The surface height (world Y of the top solid block) of a column — on a generation-40 world the natural height with
    /// the river valleys and the site grades laid over it, on every older world the natural height
    /// (<see cref="NaturalSurfaceHeight"/>). Everything that consumes terrain goes through here, so every system sees the
    /// same ground.
    /// </summary>
    public int SurfaceHeight(PlanetType planet, int worldX, int worldZ)
    {
        if (!StagesApply(planet))
        {
            return NaturalSurfaceHeight(planet, worldX, worldZ);
        }

        var key = (planet.Key, ColumnKey(worldX, worldZ));
        lock (_columnLock)
        {
            if (_stagedCache.TryGetValue(key, out int cached))
            {
                return cached;
            }
        }

        int h = StagedHeight(planet, worldX, worldZ, NaturalSurfaceHeight(planet, worldX, worldZ));
        lock (_columnLock)
        {
            if (_stagedCache.Count >= SurfaceCacheCap)
            {
                _stagedCache.Clear();
            }

            _stagedCache[key] = h;
        }

        return h;
    }

    /// <summary>The memo-free surface height — what <see cref="SurfaceHeight"/> caches (tests compare the two).</summary>
    internal int SurfaceHeightUncached(PlanetType planet, int worldX, int worldZ)
    {
        int natural = NaturalSurfaceHeightUncached(planet, worldX, worldZ);
        return StagesApply(planet) ? StagedHeight(planet, worldX, worldZ, natural) : natural;
    }

    /// <summary>Whether the valley and site-grade stages apply to a query right now: a generation-40 world, not while the
    /// calibration samples, not inside a natural-only scope, never on a void world.</summary>
    private bool StagesApply(PlanetType planet)
        => GeographyWorld && !_calibrating && t_naturalOnly == 0 && !planet.Void;

    private int StagedHeight(PlanetType planet, int worldX, int worldZ, int natural)
    {
        var field = RiverFieldFor(planet); // routed over the natural stage, built once per world
        int h = ValleyHeight(planet, field, worldX, worldZ, natural);
        return _sites.Length == 0 ? h : GradedHeight(planet, field, worldX, worldZ, h);
    }

    /// <summary>Runs <paramref name="body"/> with <see cref="SurfaceHeight"/> answering the natural stage on this thread.</summary>
    private T NaturalOnly<T>(System.Func<T> body)
    {
        t_naturalOnly++;
        try
        {
            return body();
        }
        finally
        {
            t_naturalOnly--;
        }
    }

    // ------------------------------------------------------------------ the valley stage (#3558)

    /// <summary>The valley stage of a column: the natural height eased toward the nearest surface river, then held at the
    /// banks and above the sea. A river column itself keeps its natural height (its carve is the field's).</summary>
    private int ValleyHeight(PlanetType planet, RiverField field, int worldX, int worldZ, int natural)
    {
        var valleys = field.Valleys;
        if (valleys is null || !valleys.Near(worldX, worldZ) || field.TryGet(worldX, worldZ, out _))
        {
            return natural;
        }

        int h = valleys.Lower(worldX, worldZ, natural);
        if (h >= natural)
        {
            return natural;
        }

        h = KeepAboveSea(planet, natural, h);
        return KeepWaterSealed(field, worldX, worldZ, natural, h);
    }

    /// <summary>A column whose ground stood above the sea stays above it: a valley or a cut never turns land into sea (the
    /// calibration's land/sea split is the natural one).</summary>
    private int KeepAboveSea(PlanetType planet, int before, int after)
    {
        var (sea, _) = ResolveSeaFluid(planet);
        if (sea == int.MinValue || before <= sea || after > sea)
        {
            return after;
        }

        return sea + 1;
    }

    /// <summary>A column beside a surface river column keeps its ground at least at that river's water, so the water's side
    /// stays rock (generated river water is endless, and an air neighbour is where it would flow once woken) — never higher
    /// than the column stood before.</summary>
    private static int KeepWaterSealed(RiverField field, int worldX, int worldZ, int before, int after)
    {
        int need = int.MinValue;
        Need(worldX + 1, worldZ);
        Need(worldX - 1, worldZ);
        Need(worldX, worldZ + 1);
        Need(worldX, worldZ - 1);
        return need == int.MinValue ? after : System.Math.Max(after, System.Math.Min(before, need));

        void Need(int x, int z)
        {
            if (field.TryGet(x, z, out var col) && !col.Underground && col.WaterSurfaceY > need)
            {
                need = col.WaterSurfaceY;
            }
        }
    }

    // ------------------------------------------------------------------ the site-grade stage (#3567)

    /// <summary>The site-grade stage: inside a reserved box the ground is the box's floor; within a site's radius around it
    /// the ground blends from the floor to the valley-stage height (several grades blend by weight). The change is held
    /// within the site's cut and fill limits — where the land is steeper than that, the stamper's skirt and cut finish the
    /// job — and follows the water rules: pads win, a river column keeps its carve, a column beside a river never sinks
    /// below its water, land never sinks into the sea and a column at or below the sea is never raised.</summary>
    private int GradedHeight(PlanetType planet, RiverField field, int worldX, int worldZ, int h)
    {
        if (!TryGradeTarget(worldX, worldZ, h, out double target, out SiteFlat lead) || PadColumnAt(worldX, worldZ, out _, out _)
            || field.TryGet(worldX, worldZ, out _))
        {
            return h;
        }

        var (sea, _) = ResolveSeaFluid(planet);
        if (sea != int.MinValue && h <= sea)
        {
            return h; // a sea column is never raised, nor cut
        }

        int g = (int)System.Math.Floor(target + 0.5);
        g = System.Math.Clamp(g, h - lead.MaxCut, h + lead.MaxFill);
        if (g >= h)
        {
            return g;
        }

        g = KeepAboveSea(planet, h, g);
        return KeepWaterSealed(field, worldX, worldZ, h, g);
    }

    /// <summary>The height the sites ask of a column, and the site whose limits hold it (the box it lies in, else the one
    /// with the largest weight). False when no site reaches the column.</summary>
    private bool TryGradeTarget(int worldX, int worldZ, int h, out double target, out SiteFlat lead)
    {
        target = h;
        lead = default;
        double sumW = 0.0, sumT = 0.0, bestW = 0.0;
        foreach (var site in _sites)
        {
            double d = DistanceToBox(site, worldX, worldZ);
            if (d <= 0.0)
            {
                target = site.FloorY; // inside a box its floor wins outright: the box is level
                lead = site;
                return true;
            }

            if (d > site.Radius)
            {
                continue;
            }

            double t = (site.Radius + 1 - d) / (site.Radius + 1);
            double w = t * t * (3.0 - 2.0 * t); // smoothstep: full at the box's edge, none past the radius
            sumW += w;
            sumT += w * site.FloorY;
            if (w > bestW)
            {
                bestW = w;
                lead = site;
            }
        }

        if (sumW <= 0.0)
        {
            return false;
        }

        target = sumW >= 1.0 ? sumT / sumW : h + (sumT - (sumW * h));
        return true;
    }

    /// <summary>The horizontal distance from a column to a site's box, across both seams (0 inside the box).</summary>
    private double DistanceToBox(in SiteFlat site, int worldX, int worldZ)
    {
        int u = WorldConstants.WrapX(worldX - site.MinX, _circumference); // [0, C)
        int dx = u < site.Width ? 0 : System.Math.Min(u - (site.Width - 1), _circumference - u);
        int v = WorldConstants.WrapDeltaZ(worldZ - site.MinZ, _circumference);
        int dz = v < 0 ? -v : v < site.Length ? 0 : v - (site.Length - 1);
        if (dx == 0 && dz == 0)
        {
            return 0.0;
        }

        return System.Math.Sqrt(((long)dx * dx) + ((long)dz * dz));
    }

    /// <summary>The site whose box holds this column, if any.</summary>
    private bool TrySiteBoxAt(int worldX, int worldZ, out SiteFlat site)
    {
        foreach (var s in _sites)
        {
            if (DistanceToBox(s, worldX, worldZ) <= 0.0)
            {
                site = s;
                return true;
            }
        }

        site = default;
        return false;
    }

    /// <summary>True when a reserved site's box lies within <paramref name="reach"/> blocks of this column — a tree, a prop or
    /// a giant plant anchored here would grow into the town, so the passes skip the anchor (#3568). Always false without
    /// sites (every older world).</summary>
    private bool SiteClaims(int worldX, int worldZ, int reach)
    {
        foreach (var s in _sites)
        {
            if (DistanceToBox(s, worldX, worldZ) <= reach + 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The cells under a reserved box that a cave, a tunnel or a cavern opened are plugged, eight deep, like the
    /// cells under a pad (#3568): a town never stands over a hole. Runs after the stamps, before the pads.</summary>
    private void PlugSiteFoundations(PlanetType planet, ChunkData chunk, ChunkCoord coord, List<BiomeResolved> biomes, long seed)
    {
        if (_sites.Length == 0)
        {
            return;
        }

        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        var calib = CalibFor(planet);
        for (int lx = 0; lx < cs; lx++)
            for (int lz = 0; lz < cs; lz++)
            {
                int wx = origin.X + lx, wz = origin.Z + lz;
                if (!TrySiteBoxAt(wx, wz, out var site) || site.FloorY < origin.Y - 1 || site.FloorY - SiteFoundationDepth >= origin.Y + cs)
                {
                    continue;
                }

                var b = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, wx, wz, biomes.Count, NaturalSurfaceHeight(planet, wx, wz))];
                for (int ly = 0; ly < cs; ly++)
                {
                    int y = origin.Y + ly;
                    if (y > site.FloorY || y < site.FloorY - SiteFoundationDepth || !chunk.Get(lx, ly, lz).IsAir)
                    {
                        continue;
                    }

                    chunk.Set(lx, ly, lz, y == site.FloorY ? b.Surface : b.Sub);
                }
            }
    }

    /// <summary>How deep under a reserved box a cave is plugged (the pad rule).</summary>
    private const int SiteFoundationDepth = 8;

    // ------------------------------------------------------------------ queries for the site search (#3561, #3564)

    /// <summary>Blocks from a column to the nearest fresh water (a surface river or lake), at 16-block resolution;
    /// <see cref="RiverGeography.None"/> on a world without rivers or before generation 40.</summary>
    public int FreshWaterDistance(PlanetType planet, int worldX, int worldZ)
        => GeographyWorld && RiverFieldFor(planet).Geography is { } g ? g.FreshWaterBlocks(worldX, worldZ) : RiverGeography.None;

    /// <summary>Blocks from a column to the nearest sea, at 16-block resolution; <see cref="RiverGeography.None"/> on a world
    /// without routed rivers (its sea is not on the network's grid) or before generation 40.</summary>
    public int CoastDistance(PlanetType planet, int worldX, int worldZ)
        => GeographyWorld && RiverFieldFor(planet).Geography is { } g ? g.CoastBlocks(worldX, worldZ) : RiverGeography.None;

    /// <summary>Where a height lies among the world's measured surface heights: 0 = the lowest, 1 = the highest (the
    /// calibration's whole-world sample).</summary>
    public double AltitudeQuantile(PlanetType planet, int height)
    {
        var sorted = CalibFor(planet).SortedHeights;
        if (sorted.Length == 0)
        {
            return 0.5;
        }

        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sorted[mid] < height) lo = mid + 1;
            else hi = mid;
        }

        return lo / (double)sorted.Length;
    }

    /// <summary>How far the valley and site-grade stages lowered (positive) or raised (negative) a column (the layer export,
    /// tests). 0 on every older world.</summary>
    public int StageDepthAt(PlanetType planet, int worldX, int worldZ)
        => NaturalSurfaceHeight(planet, worldX, worldZ) - SurfaceHeight(planet, worldX, worldZ);

    /// <summary>The biome's sub-surface block KEY at a column (the ground a structure's skirt is filled with, #3565), or null
    /// on a void world.</summary>
    public string? BiomeSubSurfaceKeyAt(PlanetType planet, int worldX, int worldZ)
    {
        var biomes = ResolveBiomes(planet);
        if (biomes.Count == 0)
        {
            return null;
        }

        int index = System.Math.Clamp(BiomeIndexAt(planet, worldX, worldZ), 0, biomes.Count - 1);
        return _content.BlockById(biomes[index].Sub)?.Key;
    }

    /// <summary>The height the biome pick reads for a column: on a generation-40 world the natural height whenever the caller
    /// passes the column's own surface (valleys and grades never move a biome border), any other height as given (a crown's
    /// top, a hall's floor, a pad's level).</summary>
    private int BiomeAltitude(PlanetType planet, int worldX, int worldZ, int y)
        => GeographyWorld && y == SurfaceHeight(planet, worldX, worldZ) ? NaturalSurfaceHeight(planet, worldX, worldZ) : y;

    /// <summary>The ground height the public biome queries read: the natural one on a generation-40 world (so asking another
    /// body's biome never builds its river field), the surface height before.</summary>
    private int BiomeQueryHeight(PlanetType planet, int worldX, int worldZ)
        => GeographyWorld ? NaturalSurfaceHeight(planet, worldX, worldZ) : SurfaceHeight(planet, worldX, worldZ);

    /// <summary>The river valleys' strength for a planet type, in percent (#3559): the type's <c>valleyStrength</c> (default
    /// 1), half of it for lava rivers; 0 before generation 40.</summary>
    private int ValleyStrengthPercent(PlanetType planet, bool lava)
    {
        if (!GeographyWorld)
        {
            return 0;
        }

        double s = System.Math.Max(0.0, planet.ValleyStrength ?? 1.0) * (lava ? 0.5 : 1.0);
        return (int)System.Math.Round(s * 100.0);
    }

    /// <summary>The site flats as the client gets them (far-terrain handshake).</summary>
    public int[] PackedSiteFlats() => SiteFlat.Pack(_sites);

    /// <summary>Whether this world's river field is built already (tests: a preview bake must never build it).</summary>
    internal bool RiverFieldCachedForTest(PlanetType planet)
        => _riverFields.TryGet((_worldSeed, planet.Key, _circumference, _crateredWorld, _locationSalt, _continentsEnabled, _lavaCoreVolcanoes,
            _terrainGeneration), out _);

}
