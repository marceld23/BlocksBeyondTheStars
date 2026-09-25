// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Terrain generation 11 — the cave flora wave. Caves grow plants: the cave habitat's own species (the dark
/// cave cap, glow moss on the floor, glow threads hanging from the ceiling), the fungi of the surface, and the rainbow
/// glow class, whose plants each take their own colour. The rainbow class also grows in rare clusters on the surface.
/// Cold-adapted species survive the cold. A per-chunk pass after the column loop reads the finished chunk (a floor is
/// an air cell on cave rock, a ceiling an air cell under it), so the y-loop — one of the largest methods, see the
/// #1740 JIT cliff — stays untouched. Nothing here runs on a world older than generation 11.</summary>
public sealed partial class WorldGenerator
{
    private const long CaveFloorSalt = 0xCA7E01;
    private const long CavePickSalt = 0xCA7E02;
    private const long CaveCeilingSalt = 0xCA7E03;
    private const long CavePatchSalt = 0xCA7E04;
    private const long RainbowClusterSalt = 0xCA7E05;
    private const long RainbowRollSalt = 0xCA7E06;

    /// <summary>Share of cave floor cells that grow a plant, before the patch field (which averages out near 1).</summary>
    internal const double CaveFloorDensity = 0.12;

    /// <summary>Share of cave ceiling cells a hanging plant roots in, before the patch field.</summary>
    internal const double CaveCeilingDensity = 0.05;

    /// <summary>A barren world's caves (they roll their flora at all only half the time) grow at this fraction.</summary>
    private const double BarrenCaveDensityMul = 0.5;

    /// <summary>A cave plant grows at least this many cells below the column's ground top — a cave, not a cave mouth
    /// or an overhang in the open.</summary>
    internal const int CaveFloraMinDepth = 6;

    /// <summary>The surface rainbow clusters: where the cluster field rises above this, and then on this share of the
    /// columns inside it — rare glowing patches of every colour, a find rather than a carpet.</summary>
    private const double RainbowClusterThreshold = 0.74;
    private const double RainbowClusterFill = 0.35;

    // Cold-adapted flora (Cold tag) keeps full density down to this surface temperature and fades out by the lower one.
    private const double ColdAdaptedFadeHiC = -30.0, ColdAdaptedFadeLoC = -45.0;

    // Cave pick weights: the cave habitat's own species lead, the surface fungi follow, the rainbow class is the find.
    private const int CaveOnlyWeight = 3, CaveGuestWeight = 2, CaveRainbowWeight = 2;

    // floor host block id -> this world's active cave floor species with their pick weights.
    private readonly System.Collections.Generic.Dictionary<ushort, (BlockId Id, int Weight)[]> _caveFloorByHost = new();
    // ceiling host block id -> this world's active hanging cave species.
    private readonly System.Collections.Generic.Dictionary<ushort, BlockId[]> _caveCeilingByHost = new();
    private readonly System.Collections.Generic.HashSet<ushort> _rainbowSurfaceHosts = new();
    private readonly System.Collections.Generic.HashSet<ushort> _coldAdaptedFlora = new();
    private BlockId _rainbowFloraId = BlockId.Air;
    private bool _barrenCaves;

    /// <summary>Builds the generation-11 flora state from this world's active species (called by ResolveFlora).</summary>
    private void ResolveCaveFlora(PlanetType planet, System.Collections.Generic.HashSet<string> active)
    {
        _caveFloorByHost.Clear();
        _caveCeilingByHost.Clear();
        _rainbowSurfaceHosts.Clear();
        _coldAdaptedFlora.Clear();
        _rainbowFloraId = BlockId.Air;
        _barrenCaves = false;
        if (_terrainGeneration < WorldDescription.CaveFloraGeneration)
        {
            return;
        }

        _barrenCaves = planet.IsAirless || planet.FloraDensity <= 0;

        var floor = new System.Collections.Generic.Dictionary<ushort, System.Collections.Generic.List<(BlockId, int)>>();
        var ceiling = new System.Collections.Generic.Dictionary<ushort, System.Collections.Generic.List<BlockId>>();
        foreach (var sp in FloraCatalog.All)
        {
            if (!active.Contains(sp.Key) || _content.GetBlock(sp.Key) is not { } flora)
            {
                continue;
            }

            if ((sp.Tags & FloraTag.Cold) != 0)
            {
                _coldAdaptedFlora.Add(flora.NumericId.Value);
            }

            if (sp.Rainbow)
            {
                _rainbowFloraId = flora.NumericId;
                foreach (var hostKey in sp.Hosts)
                {
                    if (_content.GetBlock(hostKey) is { } host)
                    {
                        _rainbowSurfaceHosts.Add(host.NumericId.Value);
                    }
                }
            }

            if (!sp.InCaves)
            {
                continue;
            }

            int weight = sp.Rainbow ? CaveRainbowWeight : sp.Habitat == FloraHabitat.Cave ? CaveOnlyWeight : CaveGuestWeight;
            foreach (var hostKey in sp.CaveHosts)
            {
                if (_content.GetBlock(hostKey) is not { } host)
                {
                    continue;
                }

                if (sp.Hanging)
                {
                    if (!ceiling.TryGetValue(host.NumericId.Value, out var hangList))
                    {
                        ceiling[host.NumericId.Value] = hangList = new System.Collections.Generic.List<BlockId>();
                    }

                    hangList.Add(flora.NumericId);
                }
                else
                {
                    if (!floor.TryGetValue(host.NumericId.Value, out var floorList))
                    {
                        floor[host.NumericId.Value] = floorList = new System.Collections.Generic.List<(BlockId, int)>();
                    }

                    floorList.Add((flora.NumericId, weight));
                }
            }
        }

        foreach (var kv in floor)
        {
            _caveFloorByHost[kv.Key] = kv.Value.ToArray();
        }

        foreach (var kv in ceiling)
        {
            _caveCeilingByHost[kv.Key] = kv.Value.ToArray();
        }
    }

    /// <summary>The generation-11 surface cold rule: a cold-adapted species (frostflower, snow bush, ice reed, lichen)
    /// keeps growing far below the classic fade — at <see cref="FrozenFloraShare"/> of the ground's density down to
    /// −30 °C, gone by −45 °C, so frozen ground reads sparser than a meadow — and at full density wherever the classic
    /// fade would allow more. Every other plant keeps the classic fade; on an older world every species does.</summary>
    private double SurfaceFloraColdFactor(WorldCalibration c, int surfaceY, BlockId floraId)
    {
        double classic = ColdFloraFactor(c, surfaceY);
        if (_terrainGeneration >= WorldDescription.CaveFloraGeneration && _coldAdaptedFlora.Contains(floraId.Value))
        {
            double t = TempAt(c, surfaceY);
            double adapted = System.Math.Clamp((t - ColdAdaptedFadeLoC) / (ColdAdaptedFadeHiC - ColdAdaptedFadeLoC), 0.0, 1.0);
            return System.Math.Max(classic, FrozenFloraShare * adapted);
        }

        return classic;
    }

    /// <summary>How dense cold-adapted flora grows on frozen ground, relative to the ground's ordinary density.</summary>
    private const double FrozenFloraShare = 0.6;

    /// <summary>Generation 11: altitude snow and ice (and a snow blanket) host their own flora instead of the biome's —
    /// a snow cap grows frost flowers, not grass plants fading in the cold.</summary>
    private bool FrozenFloraHost(BlockId surfaceId, BlockId snowId, BlockId iceId)
        => _terrainGeneration >= WorldDescription.CaveFloraGeneration
           && ((!snowId.IsAir && surfaceId == snowId) || (!iceId.IsAir && surfaceId == iceId));

    /// <summary>The generation-11 chunk pass: cave floors and ceilings, then the rainbow clusters on dry ground.
    /// <paramref name="groundTops"/> / <paramref name="waterTops"/> hold each column's ground top (the seabed on a
    /// water column) and water top, indexed <c>lx * ChunkSize + lz</c>.</summary>
    private void StampFloraGen11(PlanetType planet, long seed, ChunkData chunk, Vector3i origin, WorldCalibration calib,
        System.ReadOnlySpan<int> groundTops, System.ReadOnlySpan<int> waterTops, bool surfaceFlora)
    {
        if (_terrainGeneration < WorldDescription.CaveFloraGeneration)
        {
            return;
        }

        if (_caveFloorByHost.Count > 0 || _caveCeilingByHost.Count > 0)
        {
            StampCaveFlora(seed, chunk, origin, groundTops);
        }

        if (surfaceFlora && !_rainbowFloraId.IsAir && !planet.Void)
        {
            StampRainbowClusters(seed, chunk, origin, calib, groundTops, waterTops);
        }
    }

    private void StampCaveFlora(long seed, ChunkData chunk, Vector3i origin, System.ReadOnlySpan<int> groundTops)
    {
        const int n = WorldConstants.ChunkSize;
        double densityMul = _barrenCaves ? BarrenCaveDensityMul : 1.0;
        for (int lx = 0; lx < n; lx++)
            for (int lz = 0; lz < n; lz++)
            {
                // Highest local cell a cave plant may stand in: CaveFloraMinDepth under the ground top.
                int maxLy = groundTops[lx * n + lz] - CaveFloraMinDepth - origin.Y;
                if (maxLy < 1)
                {
                    continue; // the whole column of this chunk lies too close to (or above) the ground
                }

                int worldX = origin.X + lx, worldZ = origin.Z + lz;
                int wx = WorldConstants.WrapX(worldX, _circumference), wz = Wz(worldZ);
                // Patches: a low-frequency field makes the plants gather into lush grottoes and bare stretches, and a
                // second one decides which species a patch is made of — a mushroom grotto here, a moss hall there.
                double patch = FbmT(seed + CavePatchSalt, worldX, worldZ, 11.0, octaves: 2);
                double patchMul = patch > 0.62 ? 2.0 : patch > 0.50 ? 1.2 : patch > 0.38 ? 0.6 : 0.15;
                double pick = FbmT(seed + CavePickSalt, worldX, worldZ, 9.0, octaves: 2);
                double floorChance = CaveFloorDensity * patchMul * densityMul;
                double ceilingChance = CaveCeilingDensity * patchMul * densityMul;
                int top = System.Math.Min(n - 1, maxLy);
                for (int ly = 1; ly <= top; ly++)
                {
                    if (!chunk.Get(lx, ly, lz).IsAir)
                    {
                        continue;
                    }

                    int worldY = origin.Y + ly;
                    var below = chunk.Get(lx, ly - 1, lz);
                    if (_caveFloorByHost.TryGetValue(below.Value, out var pool))
                    {
                        if (Noise.Value01(seed + CaveFloorSalt, wx, worldY, wz) < floorChance)
                        {
                            chunk.Set(lx, ly, lz, PickCavePlant(pool, pick));
                        }

                        continue;
                    }

                    // A hanging plant roots in the rock above and needs the cell below it open (its strands hang
                    // down); the host must sit inside this chunk, so the top layer never takes one.
                    if (ly + 1 < n && below.IsAir
                        && _caveCeilingByHost.TryGetValue(chunk.Get(lx, ly + 1, lz).Value, out var hanging)
                        && Noise.Value01(seed + CaveCeilingSalt, wx, worldY, wz) < ceilingChance)
                    {
                        int i = System.Math.Min(hanging.Length - 1, (int)(pick * hanging.Length));
                        chunk.Set(lx, ly, lz, hanging[i]);
                    }
                }
            }
    }

    private static BlockId PickCavePlant((BlockId Id, int Weight)[] pool, double t)
    {
        int total = 0;
        foreach (var e in pool)
        {
            total += e.Weight;
        }

        int target = System.Math.Min(total - 1, (int)(t * total));
        int acc = 0;
        foreach (var e in pool)
        {
            acc += e.Weight;
            if (target < acc)
            {
                return e.Id;
            }
        }

        return pool[pool.Length - 1].Id;
    }

    private void StampRainbowClusters(long seed, ChunkData chunk, Vector3i origin, WorldCalibration calib,
        System.ReadOnlySpan<int> groundTops, System.ReadOnlySpan<int> waterTops)
    {
        const int n = WorldConstants.ChunkSize;
        for (int lx = 0; lx < n; lx++)
            for (int lz = 0; lz < n; lz++)
            {
                int ground = groundTops[lx * n + lz];
                int ly = ground + 1 - origin.Y;
                if (ly < 1 || ly >= n || ground + 1 <= waterTops[lx * n + lz])
                {
                    continue; // the cell above the ground is in another chunk, or the column is under water
                }

                int worldX = origin.X + lx, worldZ = origin.Z + lz;
                if (!chunk.Get(lx, ly, lz).IsAir || !_rainbowSurfaceHosts.Contains(chunk.Get(lx, ly - 1, lz).Value)
                    || ColdFloraFactor(calib, ground) < 0.5
                    || FbmT(seed + RainbowClusterSalt, worldX, worldZ, 6.0, octaves: 2) <= RainbowClusterThreshold
                    || Noise.Value01(seed + RainbowRollSalt, WorldConstants.WrapX(worldX, _circumference), 7, Wz(worldZ)) >= RainbowClusterFill)
                {
                    continue;
                }

                chunk.Set(lx, ly, lz, _rainbowFloraId);
            }
    }
}
