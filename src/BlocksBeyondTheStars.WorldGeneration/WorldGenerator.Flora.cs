// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>flora rosters, surface + water flora selection and densities (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>Places aquatic flora in one submerged column: a seabed plant — a kelp/seagrass stalk that grows
    /// up a few cells (leaving the top open water) or a single coral clump on the bed — and, separately, an
    /// occasional lily pad on the surface. Per-column + deterministic from the seed, so no cross-chunk margin
    /// is needed (unlike trees). Density is generous so a lake reads as visibly planted, not bare.</summary>
    private void StampWaterFlora(ChunkData chunk, Vector3i origin, int lx, int lz, long seed,
        int worldX, int worldZ, int surfaceY, int fluidLevel, BlockId kelpId, BlockId lilyId,
        BlockId coralId, BlockId seagrassId, double floraDensity)
    {
        int columnDepth = fluidLevel - surfaceY; // water cells above the seabed (>= 1 here)
        double roll = Noise.Value01(seed + 9007, WorldConstants.WrapX(worldX, _circumference), 11, Wz(worldZ));

        // The seabed plant for this column: pick deterministically among the active seabed archetypes, then
        // place it if the planting roll lands in this column's (generous) density band. Coral sits as a single
        // clump on the bed (shallow-friendly); kelp/seagrass need a little depth and grow up a stalk.
        var stalkOptions = new System.Collections.Generic.List<BlockId>(2);
        if (_kelpActive && !kelpId.IsAir) stalkOptions.Add(kelpId);
        if (_seagrassActive && !seagrassId.IsAir) stalkOptions.Add(seagrassId);
        bool coral = _coralActive && !coralId.IsAir;

        // A coherent patch field decides WHICH seabed plant dominates here (not per-cell salt-and-pepper).
        double pick = FbmT(seed + 0x5EA6, worldX, worldZ, 14.0, octaves: 2);

        if ((stalkOptions.Count > 0 || coral) && roll < floraDensity * 2.4)
        {
            // Prefer a stalk where there's room; fall back to a coral clump in shallow water.
            if (stalkOptions.Count > 0 && columnDepth >= 2)
            {
                var stalk = stalkOptions[System.Math.Min(stalkOptions.Count - 1, (int)(pick * stalkOptions.Count))];
                int height = 2 + (int)(roll * 997) % 3; // 2..4 cells
                int top = System.Math.Min(fluidLevel - 1, surfaceY + height);
                for (int wy = surfaceY + 1; wy <= top; wy++)
                {
                    int sly = wy - origin.Y;
                    if (sly >= 0 && sly < WorldConstants.ChunkSize)
                    {
                        chunk.Set(lx, sly, lz, stalk);
                    }
                }

                return;
            }

            if (coral)
            {
                int bed = (surfaceY + 1) - origin.Y; // the bottom water cell, sitting on the seabed
                if (bed >= 0 && bed < WorldConstants.ChunkSize)
                {
                    chunk.Set(lx, bed, lz, coralId);
                }

                return;
            }
        }

        // Separately, an occasional lily pad floating on the topmost water cell (if the lily archetype is active).
        if (_lilyActive && !lilyId.IsAir && roll > 1.0 - floraDensity * 0.9)
        {
            int lily = fluidLevel - origin.Y;
            if (lily >= 0 && lily < WorldConstants.ChunkSize)
            {
                chunk.Set(lx, lily, lz, lilyId);
            }
        }
    }

    // The planet key the resolved flora state below belongs to (null = not yet resolved). Flora is a
    // PER-PLANET subset (FloraGenerator.GenerateRoster XORs the planet key into the seed), and this one
    // generator instance serves every body in the save — so the state must be re-resolved whenever the
    // requested planet changes, or the first-visited planet's flora would contaminate all others (and the
    // baseline would depend on visit order instead of the seed).
    private string? _floraResolvedFor;
    private long _floraResolvedSalt; // the body salt the pools were resolved under (#478 — per-body rosters)
    private bool _kelpActive, _lilyActive; // whether the seabed kelp / surface lily archetypes grow on this world
    private bool _coralActive, _seagrassActive; // the other two seabed archetypes (coral reefs / seagrass)
    // surface block id -> the pool of (this world's active) flora that may grow on it.
    private readonly System.Collections.Generic.Dictionary<ushort, BlockId[]> _floraBySurface = new();
    // flora block id -> its climate tags (for theme-weighted, patchy species selection).
    private readonly System.Collections.Generic.Dictionary<ushort, FloraTag> _floraTagByBlock = new();

    /// <summary>Resolves this world's active flora subset (once): builds the per-surface land-flora pools from
    /// only the archetypes <see cref="FloraGenerator"/> activated for this world, and records whether the two
    /// aquatic archetypes are active. Different worlds activate different forms (coverage is kept, so no host
    /// surface or the seas ever go bare).</summary>
    private void ResolveFlora(PlanetType planet)
    {
        if (_floraResolvedFor == planet.Key && _floraResolvedSalt == _locationSalt)
        {
            return;
        }

        _floraResolvedFor = planet.Key;
        _floraResolvedSalt = _locationSalt;
        _floraBySurface.Clear(); // re-resolving for a different planet/body: drop the previous pools
        _floraTagByBlock.Clear();

        var active = new System.Collections.Generic.HashSet<string>();
        foreach (var fs in FloraGenerator.GenerateRoster(planet, RosterSeed))
        {
            if (fs.Active)
            {
                active.Add(fs.BlockKey);
            }
        }

        _kelpActive = active.Contains("flora_kelp");
        _lilyActive = active.Contains("flora_lily");
        _coralActive = active.Contains("flora_coral");
        _seagrassActive = active.Contains("flora_seagrass");

        var acc = new System.Collections.Generic.Dictionary<ushort, System.Collections.Generic.List<BlockId>>();
        foreach (var sp in BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.All)
        {
            if (sp.Aquatic || !active.Contains(sp.Key) || _content.GetBlock(sp.Key) is not { } flora)
            {
                continue; // aquatic flora are placed in submerged columns; inactive forms don't grow here
            }

            _floraTagByBlock[flora.NumericId.Value] = sp.Tags;
            foreach (var hostKey in sp.Hosts)
            {
                if (_content.GetBlock(hostKey) is { } host)
                {
                    if (!acc.TryGetValue(host.NumericId.Value, out var list))
                    {
                        acc[host.NumericId.Value] = list = new System.Collections.Generic.List<BlockId>();
                    }

                    list.Add(flora.NumericId);
                }
            }
        }

        foreach (var kv in acc)
        {
            _floraBySurface[kv.Key] = kv.Value.ToArray();
        }
    }

    /// <summary>
    /// Picks the flora block for a biome's surface (Air = none). Selection is PATCHY (a low-frequency noise,
    /// not per-cell white noise) so one species dominates a contiguous patch — a fern glade here, a flower
    /// meadow there — instead of a salt-and-pepper mix; and it is THEME-WEIGHTED so the biome's preferred
    /// climate species fill most of the patches while off-theme ones still turn up for variety.
    /// </summary>
    private BlockId FloraForSurface(PlanetType planet, BiomeResolved biome, long seed, int worldX, int worldZ,
        BlockId? surfaceOverride = null)
    {
        ResolveFlora(planet);
        var host = surfaceOverride ?? biome.Surface; // a beach column hosts the beach block's flora (#679)
        if (!_floraBySurface.TryGetValue(host.Value, out var pool) || pool.Length == 0)
        {
            return BlockId.Air;
        }

        if (pool.Length == 1)
        {
            return pool[0];
        }

        // Theme weights: preferred species count more, so a patch is most likely one of the biome's signature
        // plants. Total is small (pools are a handful of species) so recomputing per column is cheap.
        int total = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            total += _floraTagByBlock.TryGetValue(pool[i].Value, out var tag)
                ? FloraThemes.PickWeight(biome.Theme, tag) : 1;
        }

        // A low-frequency patch field selects WITHIN the weighted distribution; nearby columns share a value,
        // so the chosen species changes only at patch boundaries (coherent fields, not per-cell noise).
        double t = FbmT(seed + 9101, worldX, worldZ, 18.0, octaves: 2);
        int target = (int)(t * total);
        if (target >= total)
        {
            target = total - 1;
        }

        int acc = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            acc += _floraTagByBlock.TryGetValue(pool[i].Value, out var tag)
                ? FloraThemes.PickWeight(biome.Theme, tag) : 1;
            if (target < acc)
            {
                return pool[i];
            }
        }

        return pool[pool.Length - 1];
    }

    /// <summary>The per-column surface-flora density: the world/biome base scaled by a vegetation-richness
    /// mask (lush thickets vs sparse open ground) and the per-biome density, capped so even the lushest
    /// patch leaves some bare ground.</summary>
    private double LocalFloraDensity(PlanetType planet, BiomeResolved biome, double baseDensity, long seed, int wx, int wz)
    {
        double d = baseDensity * biome.FloraMul * biome.Theme.DensityMul * VegetationRichness(planet, seed, wx, wz);
        return d > 0.95 ? 0.95 : d;
    }

    /// <summary>0.45..2.2 vegetation-richness multiplier per column. Couples undergrowth to the SAME forest
    /// mask the trees cluster in (so woods get a carpeted floor, not bare ground under the trunks) plus an
    /// independent meadow mask, so treeless biomes also break into lush thickets and sparse clearings.</summary>
    private double VegetationRichness(PlanetType planet, long seed, int wx, int wz)
    {
        double forest = FbmT(seed + 0xF07E57, wx, wz, planet.TerrainScale * 2.0, octaves: 3); // matches StampTrees' grove mask
        double meadow = FbmT(seed + 0x9E2D07, wx, wz, planet.TerrainScale * 1.6, octaves: 2); // independent lush/sparse patches
        double m = forest > meadow ? forest : meadow; // a wood OR a meadow makes a column lush
        return m > 0.62 ? 2.2 : m > 0.52 ? 1.5 : m > 0.40 ? 1.0 : 0.45;
    }
}
