// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The terrain base form of a world (#3977) — the abstraction and the two procedural forms; the map form is in
/// <c>WorldGenerator.MapBaseForm.cs</c> (partial of <see cref="WorldGenerator"/>).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>
    /// The terrain base form of a world (#3977): the shape the land has before any overlay. It gives three answers —
    /// the raw height of a column, the sea level and fluid of the world, and the biome index of a column. The generator
    /// asks the form its world's profile carries (<see cref="WonderProfile.BaseForm"/>, chosen once per world by
    /// <see cref="BaseFormFor"/>) instead of branching on craters, styles, sand seas, gas, water or lava in three places.
    /// <para>Everything laid over the base form stays where it is: the landmark overlays, the sea-relative rows, the
    /// river valleys, the site grades, the caches. The forms are stateless or immutable, so one instance may serve
    /// every thread.</para>
    /// </summary>
    private abstract class TerrainBaseForm
    {
        /// <summary>The raw height (world Y) of a column, before any landmark overlay and before the generator's
        /// safety cap.</summary>
        public abstract int RawHeight(WorldGenerator g, PlanetType planet, WonderProfile w, int worldX, int worldZ);

        /// <summary>The world's sea level and the fluid that fills it, from the calibration's whole-world height
        /// sample (<see cref="WorldCalibration.SortedHeights"/>). <see cref="int.MinValue"/> = a dry world. The procedural
        /// forms flood a percentile of the sampled ground (gas, water or lava).</summary>
        public virtual (int Level, BlockId Fluid) Sea(WorldGenerator g, PlanetType planet, WorldCalibration calib)
            => g.PercentileSea(planet, calib);

        /// <summary>The biome index (into the world's resolved biomes, <paramref name="count"/> of them) of a column
        /// whose natural ground is <paramref name="surfaceY"/>. The procedural forms mix a broad region field with the
        /// altitude, after the sand sea and the hot zones have claimed their columns.</summary>
        public virtual int BiomeIndex(WorldGenerator g, WorldCalibration calib, long seed, int worldX, int worldZ, int count, int surfaceY)
            => g.ProceduralBiomeIndex(calib, seed, worldX, worldZ, count, surfaceY);

        /// <summary>Switches off the world's feature gates this form does not carry — called once while the world's
        /// profile is built, before its landmark rows are chosen. The procedural forms carry every gate.</summary>
        public virtual void RestrictFeatures(WonderProfile w)
        {
        }
    }

    /// <summary>The rolled form: the procedural relief of every world that is not cratered and has no map — archetypes,
    /// styles, drama, continents, the baseline regimes and the sand sea (<see cref="RolledRawHeight"/>).</summary>
    private sealed class RolledBaseForm : TerrainBaseForm
    {
        public static readonly RolledBaseForm Instance = new();

        public override int RawHeight(WorldGenerator g, PlanetType planet, WonderProfile w, int worldX, int worldZ)
            => g.RolledRawHeight(planet, w, worldX, worldZ);
    }

    /// <summary>The cratered form (airless moons, landable asteroids): flat regolith pocked with impact craters and
    /// crater chains (<see cref="CrateredRawHeight"/>). Its sea and biomes are the procedural ones.</summary>
    private sealed class CrateredBaseForm : TerrainBaseForm
    {
        public static readonly CrateredBaseForm Instance = new();

        public override int RawHeight(WorldGenerator g, PlanetType planet, WonderProfile w, int worldX, int worldZ)
            => g.CrateredRawHeight(planet, w, worldX, worldZ);
    }

    /// <summary>The base swell of a column in [-1, 1]: the world's 4-octave torus FBM at the profile's relief
    /// wavelength — what both procedural forms shape.</summary>
    private double BaseSwell(WonderProfile w, int worldX, int worldZ)
    {
        double n = FbmT(w.Seed, worldX, worldZ, w.Scale, octaves: 4);
        return (n - 0.5) * 2.0;
    }
}
