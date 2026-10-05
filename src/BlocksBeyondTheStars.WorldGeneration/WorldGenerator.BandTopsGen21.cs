// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The band-top host pass (#2335, terrain generation 21): trees, boulders, an ore clump and a data cache on the
/// top of a crown band — a pillar island with a wood on it, a sky island with a copse, a data cache on the highest
/// balcony as the climbing reward. Every ground stamp reads <c>SurfaceHeight</c> as its floor, so until this pass
/// nothing but surface flora ever grew on a band. Runs after the ground stamps on a generation-21 world with bands;
/// its own salts, so no ground roll moves. A tree never reaches past the band's rim into the air: the four
/// neighbours of its trunk must carry the same deck (within one cell), and leaves beyond that only fill air —
/// a canopy may overhang the rim, which is what a tree on a cliff edge does. Kept in its own file (#1740).
/// (partial of <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    private const double BandTopTreeFactor = 3.0;      // a crown is a wood more often than open ground is
    private const double BandTopCacheChance = 0.0015;  // ~one data cache per crown of 600 columns
    private const double BandTopOutcropChance = 0.002; // a rare-ore clump on a crown of a type with rare veins
    private const double BandTopBoulderChance = 0.0025;

    /// <summary>Whether a band's top is a deck a tree or a prop can stand on: a crown always, a sky island from
    /// generation 21 (older sky worlds keep their bare islands — the pass never runs there).</summary>
    private static bool IsHostBand(in ColumnBand band) => band.Kind == BandKind.Crown || band.Kind == BandKind.Island;

    /// <summary>The host deck's top at a column, or <see cref="int.MinValue"/> — the highest crown / island band.</summary>
    private int HostBandTop(PlanetType planet, int wx, int wz, System.Span<ColumnBand> scratch)
    {
        int n = GetExtraBands(planet, wx, wz, scratch);
        int top = int.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (IsHostBand(scratch[i]) && scratch[i].Top > top)
            {
                top = scratch[i].Top;
            }
        }

        return top;
    }

    private void StampBandTops(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, BlockId logId, BlockId leafId, double treeDensity, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const int margin = 4; // the widest crown, as the ground pass scans
        var calib = CalibFor(planet);
        var w = WonderFor(planet);
        var grassId = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirtId = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        var mudId = _content.GetBlock("mud")?.NumericId ?? BlockId.Air;
        var sandId = _content.GetBlock("sand")?.NumericId ?? BlockId.Air;
        var cacheId = _content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;
        var deepId = ResolveBlock(planet.DeepBlock);
        bool trees = treeDensity > 0.0 && !logId.IsAir && !leafId.IsAir;
        var blocks = new TreeBlocks(logId, leafId,
            _content.GetBlock("pine_needles")?.NumericId ?? leafId,
            _content.GetBlock("palm_frond")?.NumericId ?? leafId,
            logId,
            _content.GetBlock("mushroom_stem")?.NumericId ?? logId,
            _content.GetBlock("mushroom_cap")?.NumericId ?? leafId,
            _content.GetBlock("crystal")?.NumericId ?? leafId);
        bool rareVeins = false;
        foreach (var vein in planet.Ores)
        {
            rareVeins |= vein.RareTier;
        }

        void SetCell(int wx, int wy, int wz, BlockId block, bool overwrite)
        {
            int lx = wx - origin.X, ly = wy - origin.Y, lz = wz - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                return;
            }

            if (!overwrite && !chunk.Get(lx, ly, lz).IsAir)
            {
                return; // leaves and props fill air only
            }

            chunk.Set(lx, ly, lz, block);
        }

        System.Action<int, int, int, BlockId, bool> set = SetCell;
        System.Span<ColumnBand> scratch = stackalloc ColumnBand[MaxColumnBands];
        System.Span<ColumnBand> neighbour = stackalloc ColumnBand[MaxColumnBands];

        for (int wx = origin.X - margin; wx < origin.X + cs + margin; wx++)
            for (int wz = origin.Z - margin; wz < origin.Z + cs + margin; wz++)
            {
                int cx = WorldConstants.WrapX(wx, _circumference);
                int cz = Wz(wz);
                // The cheap first reject: one roll per column decides for nearly every column before any band is asked.
                double roll = Noise.Value01(seed + 0xB7EE5, cx, 13, cz);
                double propRoll = Noise.Value01(seed + 0xB7EE6, cx, 17, cz);
                double treeBound = trees ? treeDensity * BandTopTreeFactor * 9.0 : 0.0;
                if (roll >= treeBound && propRoll >= BandTopCacheChance + BandTopOutcropChance + BandTopBoulderChance)
                {
                    continue;
                }

                int ty = HostBandTop(planet, wx, wz, scratch);
                if (ty == int.MinValue || ty + 1 > origin.Y + cs - 1 || ty + MaxStampRise < origin.Y || ty + 1 <= fluidLevel)
                {
                    continue; // no deck here, or nothing it could write lands in this chunk
                }

                var biome = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, wx, wz, biomes.Count, ty)];
                int shapeHash = (int)(Noise.Value01(seed + 0xB7EE7, cx, 41, cz) * 997);

                // Props first — rarer, and a boulder or a cache under a tree reads fine; a tree through a cache does not.
                if (propRoll < BandTopCacheChance)
                {
                    if (!cacheId.IsAir)
                    {
                        set(wx, ty + 1, wz, cacheId, false);
                    }

                    continue;
                }

                if (propRoll < BandTopCacheChance + BandTopOutcropChance)
                {
                    if (rareVeins)
                    {
                        var ore = OutcropOre(planet, shapeHash, deepId);
                        set(wx, ty + 1, wz, ore, false);
                        if ((shapeHash & 24) == 0)
                        {
                            set(wx, ty + 2, wz, ore, false);
                        }
                    }

                    continue;
                }

                if (propRoll < BandTopCacheChance + BandTopOutcropChance + BandTopBoulderChance)
                {
                    set(wx, ty + 1, wz, deepId, false);
                    if ((shapeHash & 1) == 0 && HostBandTop(planet, wx + 1, wz, neighbour) == ty)
                    {
                        set(wx + 1, ty + 1, wz, deepId, false);
                    }

                    if ((shapeHash & 12) == 0)
                    {
                        set(wx, ty + 2, wz, deepId, false);
                    }

                    continue;
                }

                if (!trees || roll >= treeDensity * biome.TreeMul * biome.Theme.TreeMul * BandTopTreeFactor)
                {
                    continue;
                }

                if (TempAt(calib, ty) < TreeLineC)
                {
                    continue; // a crown above the tree line stays bare, like a summit
                }

                var surf = biome.Surface;
                var kind = PickTreeKind(biome.Theme.PaletteFor(w.Generation), seed, wx, wz, planet.TerrainScale);
                if (kind == TreeKind.None || kind == TreeKind.Mangrove)
                {
                    continue; // no trees in this theme, and mangroves stand beside water
                }

                bool earthy = surf == grassId || surf == dirtId || surf == mudId;
                bool sandyOk = surf == sandId && (kind == TreeKind.Palm || kind == TreeKind.Dead || kind == TreeKind.Saguaro);
                if (!earthy && !sandyOk)
                {
                    continue;
                }

                // The rim rule: the trunk's four neighbours stand on the same deck, within one cell.
                bool onDeck = true;
                for (int d = 0; d < 4 && onDeck; d++)
                {
                    int nx = wx + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = wz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    int nt = HostBandTop(planet, nx, nz, neighbour);
                    onDeck = nt != int.MinValue && System.Math.Abs(nt - ty) <= 1;
                }

                if (!onDeck)
                {
                    continue;
                }

                double sizeF = SizeFactor(seed + 0xB7EE8, wx, wz, 0.30);
                double hJit = SizeFactor(seed + 0xB7EE9, wx, wz, 0.12);
                double cJit = SizeFactor(seed + 0xB7EEA, wx, wz, 0.12);
                BuildTreeOfKind(kind, wx, ty, wz, sizeF, hJit, cJit, seed, blocks, set);
            }
    }
}
