// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 16 (#2085) — the Fifi plant, partial of <see cref="WorldGenerator"/>. Sophie's tree-sized plant from
/// the school club stands in groves on every world with plant life, on any soft natural ground (<see cref="FifiPlantRules"/>):
/// a green trunk, a crown of yellow leaves, glowing pink blossoms and berries hanging under the lowest leaves. A separate pass
/// like the giant flora (#1648), with its own margin (the widest crown) and rise (inside the stamps' common
/// <see cref="MaxStampRise"/>), and a first reject on the grove cell's hash before anything costly runs. Deterministic: every
/// input is a pure function of the root column, and the plant's cells are relative to it, so a plant straddling a chunk edge
/// is identical from every chunk. Nothing here runs below generation 16. Kept in its own file (#1740).
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>What every root decision of one world reads — resolved once per chunk (or per test probe).</summary>
    private sealed class FifiGround
    {
        public readonly HashSet<ushort> Hosts = new();
        public BlockId Beach = BlockId.Air;
        public BlockId Water = BlockId.Air;
        public WorldCalibration Calib = null!;
        public RiverField Rivers = null!;
        public List<BiomeResolved> Biomes = null!;
        public int FluidLevel;
        public bool Trees;
    }

    /// <summary>The generation-16 gate and the world's Fifi ground, or null when this world grows no Fifi plant: an older
    /// generation, no plant life, a content without the plant's blocks, or no ground of the world that hosts one.</summary>
    private FifiGround? FifiGroundFor(PlanetType planet, List<BiomeResolved> biomes, int fluidLevel, bool trees)
    {
        if (_terrainGeneration < WorldDescription.FifiPlantGeneration || planet.IsAirless || planet.FloraDensity <= 0 || planet.Void)
        {
            return null;
        }

        foreach (var key in FifiPlant.PartKeys)
        {
            if ((_content.GetBlock(key)?.NumericId ?? BlockId.Air).IsAir)
            {
                return null; // this content has no Fifi plant
            }
        }

        var g = new FifiGround { Biomes = biomes, FluidLevel = fluidLevel, Trees = trees };
        foreach (var key in FifiPlantRules.HostKeys)
        {
            if (_content.GetBlock(key) is { } host && !host.NumericId.IsAir)
            {
                g.Hosts.Add(host.NumericId.Value);
            }
        }

        if (!string.IsNullOrEmpty(planet.BeachBlock) && _content.GetBlock(planet.BeachBlock) is { } beach)
        {
            g.Beach = beach.NumericId;
        }

        bool anyHost = !g.Beach.IsAir && g.Hosts.Contains(g.Beach.Value);
        foreach (var b in biomes)
        {
            anyHost |= g.Hosts.Contains(b.Surface.Value);
        }

        if (!anyHost)
        {
            return null; // rock, ice, ash, crystal … — no ground of this world ever hosts one
        }

        g.Water = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        g.Calib = CalibFor(planet);
        g.Rivers = RiverFieldFor(planet);
        return g;
    }

    /// <summary>Whether a Fifi plant roots at this column, and on which surface height. <paramref name="minY"/> /
    /// <paramref name="maxY"/> bound the cells the caller can use (a chunk's rows) so a stacked chunk that none of the plant
    /// reaches stops before the costly ground checks. Cheapest test first: the grove cell (one hash rejects two cells in
    /// three), the member places, the surface, then water, woods and ground.</summary>
    private bool FifiRootAt(PlanetType planet, long seed, FifiGround g, int wx, int wz, int minY, int maxY, out int sy)
    {
        sy = 0;
        if (!FifiPlantRules.IsGroveMember(seed, wx, wz, _circumference))
        {
            return false;
        }

        sy = SurfaceHeight(planet, wx, wz);
        if (sy + 1 > maxY || sy + FifiPlantRules.MaxRise < minY)
        {
            return false; // every cell this plant could write lies outside the caller's rows
        }

        if (sy + 1 <= g.FluidLevel || SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0
            || SurfaceGen1WaterDepth(planet, wx, wz) > 0 || IsSandSeaAt(planet, wx, wz))
        {
            return false; // not in the sea, a pond, a river or a sand sea
        }

        if (g.Trees && ForestMaskAt(planet, seed, wx, wz) > GiantTreeForestMask)
        {
            return false; // groves of the open land, never inside the deep woods where the tree crowns stand
        }

        var biome = g.Biomes[g.Biomes.Count <= 1 ? 0 : BiomeIndex(g.Calib, seed, wx, wz, g.Biomes.Count, sy)];
        var ground = !g.Beach.IsAir && DryBeachAt(planet, g.Calib, seed, g.Rivers, g.Water, wx, wz, sy) ? g.Beach : biome.Surface;
        return g.Hosts.Contains(ground.Value); // soft natural ground only
    }

    private void StampFifiPlants(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, int fluidLevel, bool trees)
    {
        if (FifiGroundFor(planet, biomes, fluidLevel, trees) is not { } g)
        {
            return;
        }

        var stemId = _content.GetBlock(FifiPlant.StemKey)!.NumericId;
        var leafId = _content.GetBlock(FifiPlant.LeafKey)!.NumericId;
        var blossomId = _content.GetBlock(FifiPlant.BlossomKey)!.NumericId;
        var berriesId = _content.GetBlock(FifiPlant.BerriesKey)!.NumericId;
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const int margin = FifiPlantRules.CrownRadiusMax;
        for (int wx = origin.X - margin; wx < origin.X + cs + margin; wx++)
            for (int wz = origin.Z - margin; wz < origin.Z + cs + margin; wz++)
            {
                if (!FifiRootAt(planet, seed, g, wx, wz, origin.Y, origin.Y + cs - 1, out int sy))
                {
                    continue;
                }

                var shape = FifiShapeAt(seed, wx, wz);
                BuildFifiPlant(chunk, origin, wx, sy + 1, wz, shape, stemId, leafId, blossomId, berriesId);
            }
    }

    /// <summary>The plant rooted at this column: its own hash and a bell-shaped size factor (about ±25 %).</summary>
    private FifiPlantRules.Shape FifiShapeAt(long seed, int wx, int wz)
    {
        double sizeF = SizeFactor(FifiPlantRules.SizeSeed(seed), wx, wz, 0.25);
        return FifiPlantRules.ShapeFor(FifiPlantRules.PlantHash(seed, WorldConstants.WrapX(wx, _circumference), Wz(wz)), sizeF);
    }

    /// <summary>Writes the part of one plant that lies in this chunk: the trunk over whatever stands there (a trunk is solid),
    /// then leaves and blossoms into air only, then each berry into air only — and only under a leaf this chunk really holds
    /// (a crown pressed into a slope grows no berry under rock; when the leaf lies in the chunk above, the plant's own
    /// geometry is trusted, like the fruit trees).</summary>
    private static void BuildFifiPlant(ChunkData chunk, Vector3i origin, int wx, int rootY, int wz, FifiPlantRules.Shape shape,
        BlockId stemId, BlockId leafId, BlockId blossomId, BlockId berriesId)
    {
        int cs = WorldConstants.ChunkSize;

        bool Local(int x, int y, int z, out int lx, out int ly, out int lz)
        {
            lx = wx + x - origin.X;
            ly = rootY + y - origin.Y;
            lz = wz + z - origin.Z;
            return lx >= 0 && lx < cs && ly >= 0 && ly < cs && lz >= 0 && lz < cs;
        }

        foreach (var (x, y, z) in shape.Trunk)
        {
            if (Local(x, y, z, out int lx, out int ly, out int lz))
            {
                chunk.Set(lx, ly, lz, stemId);
            }
        }

        foreach (var (x, y, z) in shape.Leaves)
        {
            if (Local(x, y, z, out int lx, out int ly, out int lz) && chunk.Get(lx, ly, lz).IsAir)
            {
                chunk.Set(lx, ly, lz, leafId);
            }
        }

        foreach (var (x, y, z) in shape.Blossoms)
        {
            if (Local(x, y, z, out int lx, out int ly, out int lz) && chunk.Get(lx, ly, lz).IsAir)
            {
                chunk.Set(lx, ly, lz, blossomId);
            }
        }

        foreach (var (x, y, z) in shape.Berries)
        {
            if (!Local(x, y, z, out int lx, out int ly, out int lz) || !chunk.Get(lx, ly, lz).IsAir)
            {
                continue; // a neighbour chunk hangs that one, or something already stands in the cell
            }

            if (ly + 1 < cs && chunk.Get(lx, ly + 1, lz).Value != leafId.Value)
            {
                continue; // the leaf it would hang from was never written
            }

            chunk.Set(lx, ly, lz, berriesId);
        }
    }

    /// <summary>The Fifi plants rooted in a square of the world (tests, tooling): each root column with its surface height
    /// and its shape — exactly the decisions the stamp makes, without building a chunk.</summary>
    internal List<(int X, int Z, int SurfaceY, FifiPlantRules.Shape Shape)> FifiPlantsForTest(PlanetType planet, int x0, int z0, int size)
    {
        var found = new List<(int X, int Z, int SurfaceY, FifiPlantRules.Shape Shape)>();
        long seed = PlanetSeed(planet);
        var (fluidLevel, _) = ResolveSeaFluid(planet);
        bool trees = (planet.TreeDensity ?? 0.012) * _floraFactor > 0.0 && _content.GetBlock("wood_log") != null;
        if (FifiGroundFor(planet, ResolveBiomes(planet), fluidLevel, trees) is not { } g)
        {
            return found;
        }

        for (int x = x0; x < x0 + size; x++)
            for (int z = z0; z < z0 + size; z++)
            {
                if (FifiRootAt(planet, seed, g, x, z, int.MinValue / 2, int.MaxValue / 2, out int sy))
                {
                    found.Add((x, z, sy, FifiShapeAt(seed, x, z)));
                }
            }

        return found;
    }
}
