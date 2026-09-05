// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>biome resolution and the per-column biome index (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>A biome resolved for this world: its surface/sub-surface blocks plus the per-biome flora
    /// theme + density multipliers used when seeding plants and trees (so one region reads lush + tropical
    /// and another sparse + arid within the same world).</summary>
    internal readonly struct BiomeResolved
    {
        public BiomeResolved(BlockId surface, BlockId sub, double floraMul, double treeMul, FloraThemes.Theme theme)
        {
            Surface = surface;
            Sub = sub;
            FloraMul = floraMul;
            TreeMul = treeMul;
            Theme = theme;
        }

        public BlockId Surface { get; }
        public BlockId Sub { get; }
        public double FloraMul { get; }
        public double TreeMul { get; }
        public FloraThemes.Theme Theme { get; }
    }

    /// <summary>
    /// Resolves the surface/sub-surface blocks (+ per-biome flora theme &amp; density) the planet actually
    /// uses. A multi-biome planet lists a *pool* of biomes; how many of them this world uses (2..pool)
    /// AND which ones make the cut are randomised per world from the seed, so each multi-biome world
    /// differs. Single-biome → one entry.
    /// </summary>
    internal List<BiomeResolved> ResolveBiomes(PlanetType planet)
    {
        var planetTheme = FloraThemes.Resolve(planet.FloraTheme);
        var list = new List<BiomeResolved>();
        if (planet.Biomes.Count <= 0)
        {
            list.Add(new BiomeResolved(ResolveBlock(planet.SurfaceBlock), ResolveBlock(planet.SubSurfaceBlock),
                1.0, 1.0, planetTheme));
            return list;
        }

        int pool = planet.Biomes.Count;
        int count = pool;
        var order = new int[pool];
        for (int i = 0; i < pool; i++)
        {
            order[i] = i;
        }

        if (pool > 1)
        {
            long s = PlanetSeed(planet) ^ 0x0B10C0;
            count = 2 + (int)((ulong)(s < 0 ? -s : s) % (ulong)(pool - 1)); // 2..pool, seed-derived

            // WHICH biomes make the cut is shuffled per world too (#696): previously the first N pool
            // entries always won, so the tail entries were missing from every world that rolled a
            // smaller count. Fisher–Yates seeded from the world so server and client preview agree.
            var rng = new DeterministicRandom((PlanetSeed(planet) ^ 0x0B10C7) * 2654435761L);
            for (int i = pool - 1; i > 0; i--)
            {
                int j = rng.Range(0, i);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }

        for (int i = 0; i < count; i++)
        {
            var b = planet.Biomes[order[i]];
            var theme = string.IsNullOrWhiteSpace(b.FloraTheme) ? planetTheme : FloraThemes.Resolve(b.FloraTheme);
            list.Add(new BiomeResolved(ResolveBlock(b.SurfaceBlock), ResolveBlock(b.SubSurfaceBlock),
                b.FloraDensityMul, b.TreeDensityMul, theme));
        }

        return list;
    }

    /// <summary>True when this column's biome surface is welcoming ground (grass or dirt). The landing-pad
    /// chooser PREFERS such columns so new players spawn on green topsoil — where the thin-topsoil ore
    /// windows (Severin M3) are visible — instead of a mud marsh or bare rock. A preference only, never a
    /// hard requirement (see <see cref="HasEarthySurfaceBiome"/>).</summary>
    public bool IsEarthySurface(PlanetType planet, int worldX, int worldZ)
    {
        var biomes = ResolveBiomes(planet);
        var b = biomes[biomes.Count <= 1
            ? 0
            : BiomeIndex(CalibFor(planet), PlanetSeed(planet), worldX, worldZ, biomes.Count,
                SurfaceHeight(planet, worldX, worldZ))];
        var grass = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirt = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        return (!grass.IsAir && b.Surface == grass) || (!dirt.IsAir && b.Surface == dirt);
    }

    /// <summary>Whether this world has any grass/dirt biome at all — desert/ice/exotic worlds don't, and
    /// their pad placement must not waste its search (or reject every candidate) looking for one.</summary>
    public bool HasEarthySurfaceBiome(PlanetType planet)
    {
        var grass = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirt = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        foreach (var b in ResolveBiomes(planet))
        {
            if ((!grass.IsAir && b.Surface == grass) || (!dirt.IsAir && b.Surface == dirt))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The biome index at a world position (large regions), for per-biome systems like weather.</summary>
    public int BiomeIndexAt(PlanetType planet, int worldX, int worldZ)
    {
        int count = ResolveBiomes(planet).Count;
        return count <= 1
            ? 0
            : BiomeIndex(CalibFor(planet), PlanetSeed(planet), worldX, worldZ, count,
                SurfaceHeight(planet, worldX, worldZ));
    }

    /// <summary>How many distinct biomes this planet's world uses.</summary>
    public int BiomeCount(PlanetType planet) => ResolveBiomes(planet).Count;

    /// <summary>Picks a biome per column: broad region noise (stretched so the outer list entries actually
    /// get real coverage — the raw FBM clusters around 0.5 and starved them) blended with the column's
    /// normalised ALTITUDE (#476), so a planet's biome list reads bottom-to-top: entry 0 hugs the lowlands,
    /// the last entry caps the peaks. Regions stay large so per-biome weather covers a meaningful area.</summary>
    private int BiomeIndex(WorldCalibration calib, long seed, int worldX, int worldZ, int count, int surfaceY)
    {
        double n = Noise.FbmTorus(seed ^ 0x0B10E, worldX, worldZ, _circumference,
            WorldConstants.LatitudePeriodFor(_circumference), 360.0, octaves: 3);
        double spread = System.Math.Clamp((n - 0.5) * 2.4 + 0.5, 0.0, 1.0);
        // Normalise against the 2–98 % height band, not the absolute extremes: a lone massif summit or
        // rift floor (#578) would otherwise stretch the span and compress every ordinary column into the
        // middle biome entries. Landmark columns simply clamp to the top/bottom entry — which is right.
        double span = System.Math.Max(1.0, calib.AltHi - calib.AltLo);
        double alt = System.Math.Clamp((surfaceY - calib.AltLo) / span, 0.0, 1.0);
        double mix = System.Math.Clamp(spread * 0.6 + alt * 0.4, 0.0, 0.9999);
        int idx = (int)(mix * count);
        return idx < 0 ? 0 : (idx >= count ? count - 1 : idx);
    }
}
