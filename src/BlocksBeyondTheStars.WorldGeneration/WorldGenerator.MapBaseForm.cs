// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The map base form (#3978): terrain from a planet map (partial of <see cref="WorldGenerator"/>).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>Salt of the map worlds' rolled detail field.</summary>
    private const long MapDetailSalt = 0x3A9D0E7A1L;

    /// <summary>Salts of the two fields that jitter a map world's biome borders.</summary>
    private const long MapBiomeJitterSaltX = 0x3A9B10E1L, MapBiomeJitterSaltZ = 0x3A9B10E2L;

    /// <summary>The detail field's wavelength (blocks) — a few map cells, so a slope between two cells is no ramp.</summary>
    private const double MapDetailScale = 24.0;

    /// <summary>How far (in map cells) a biome border may wander, so borders are no pixel stairs.</summary>
    private const int MapBiomeJitterCells = 3;

    /// <summary>
    /// The map form (#3978): the ground of a planet map. Height: the map's elevation, bilinear between the cell centres
    /// in integer fixed point (<see cref="MapTerrain"/>), through the type's height curve (<see cref="MapTerrain.HeightBlocks"/>)
    /// from <see cref="PlanetType.MapSeaLevelY"/>, plus a small rolled detail that fades out toward the coast. Sea: the
    /// fixed <see cref="PlanetType.MapSeaLevelY"/>, water (a world without water is dry). Biome: the map's class at a
    /// jittered point, as an index into the type's biome list. The north and south edges were blended toward the ice
    /// class and one height when the map was prepared, so the Z seam joins. Giant landforms are off (<see cref="RestrictFeatures"/>).
    /// </summary>
    private sealed class MapBaseForm : TerrainBaseForm
    {
        private readonly MapTerrain _terrain;

        public MapBaseForm(MapTerrain terrain) => _terrain = terrain;

        public override int RawHeight(WorldGenerator g, PlanetType planet, WonderProfile w, int worldX, int worldZ)
        {
            long elevation = _terrain.ElevationFixed(worldX, worldZ, g._circumference, g.LatPeriod);
            int blocks = MapTerrain.HeightBlocks(elevation, planet);
            return planet.MapSeaLevelY + blocks + g.MapDetail(planet, w, worldX, worldZ, blocks) + g.MapCraters(planet, w, worldX, worldZ);
        }

        public override (int Level, BlockId Fluid) Sea(WorldGenerator g, PlanetType planet, WorldCalibration calib)
            => g.MapSea(planet);

        public override int BiomeIndex(WorldGenerator g, WorldCalibration calib, long seed, int worldX, int worldZ, int count, int surfaceY)
        {
            int jitter = MapBiomeJitterCells * g._circumference / _terrain.Width; // cells → blocks
            int jx = (int)System.Math.Round((g.FbmT(seed ^ MapBiomeJitterSaltX, worldX, worldZ, jitter * 4.0, octaves: 2) - 0.5) * 2.0 * jitter);
            int jz = (int)System.Math.Round((g.FbmT(seed ^ MapBiomeJitterSaltZ, worldX, worldZ, jitter * 4.0, octaves: 2) - 0.5) * 2.0 * jitter);
            int biomeClass = _terrain.BiomeClassAt(worldX + jx, worldZ + jz, g._circumference, g.LatPeriod);
            return System.Math.Min(biomeClass, count - 1);
        }

        /// <summary>A map world carries the map's own mountains and coasts: the procedural giants — volcano cones,
        /// calderas, massifs, table mountains, rifts, escarpments and the other baseline regimes, continents, shield
        /// volcanoes, impact basins, glacial troughs, the sand sea, the gaming landmarks and the spectacle families — are
        /// off. Rivers, caves, ores, the small landforms and the flora stay.</summary>
        public override void RestrictFeatures(WonderProfile w)
        {
            w.Continent = default;
            w.Volcanoes = w.Calderas = w.Massifs = w.TableMountains = w.Rifts = w.MegaRift = false;
            w.Escarpment = w.Stepped = w.Tilted = w.EquatorRidge = w.SaltPolygons = w.BasaltFields = false;
            w.ShieldVolcanoes = w.ImpactBasins = w.GlacialTroughs = w.GamingLandmarks = false;
            w.SandSea = false;
            w.SandThreshold = double.MaxValue;
            w.PillarIslands = w.ArchClusters = w.MesaClusters = w.TableVariants = w.WaveRocks = false;
            w.Abris = w.Portals = w.DaylightHalls = w.FossilRidges = w.BoneStrata = false;
        }
    }

    /// <summary>The base form a world of <paramref name="planet"/> uses: the map form when the type names a terrain map,
    /// else cratered when the type or the body is cratered (<paramref name="cratered"/> = the profile's
    /// <see cref="WonderProfile.Cratered"/>), else rolled. A map the content does not have is an error, never a silent
    /// fallback — a world built without its map would be a different world.</summary>
    private TerrainBaseForm BaseFormFor(PlanetType planet, bool cratered)
    {
        if (planet.HasTerrainMap)
        {
            var map = _content.GetPlanetMap(planet.TerrainMap)
                ?? throw new InvalidOperationException($"Planet type '{planet.Key}' takes its terrain from the map '{planet.TerrainMap}', "
                                                       + "which the content does not have (data/maps/" + planet.TerrainMap + ".bbmap).");
            return new MapBaseForm(MapTerrain.For(map));
        }

        return cratered ? CrateredBaseForm.Instance : RolledBaseForm.Instance;
    }

    /// <summary>A map world's sea: water at the fixed <see cref="PlanetType.MapSeaLevelY"/> when the world has water
    /// (its <c>waterAbundance</c>, by default any world with air), else dry.</summary>
    private (int Level, BlockId Fluid) MapSea(PlanetType planet)
    {
        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        return waterAb > 0.0 && _content.GetBlock("water") is { } water
            ? (planet.MapSeaLevelY, water.NumericId)
            : (int.MinValue, default);
    }

    /// <summary>The small rolled craters over a map column (blocks, #3979): on a cratered world — the type's <c>cratered</c> (an
    /// airless map planet, Mercury) or an airless moon (the Moon) — the cratered form's impact bowls and crater chains
    /// (<see cref="CraterCarve"/>, <see cref="CraterChainCarve"/>, the body's own crater profile) laid on top of the map; 0
    /// elsewhere. Such a body is dry (a map world floods only with water, and an airless type has none), so no crater crosses
    /// a waterline.</summary>
    private int MapCraters(PlanetType planet, WonderProfile w, int worldX, int worldZ)
        => w.Cratered
            ? (int)System.Math.Round(CraterCarve(w.Seed, worldX, worldZ, planet) + CraterChainCarve(w.Seed, worldX, worldZ))
            : 0;

    /// <summary>The rolled detail over a map column (blocks): the world's torus FBM at <see cref="MapDetailScale"/>,
    /// ±<see cref="PlanetType.MapDetailAmplitude"/>, scaled down within that many blocks of 0 m so it never moves a column
    /// across the waterline — the coastline stays the map's.</summary>
    private int MapDetail(PlanetType planet, WonderProfile w, int worldX, int worldZ, int blocks)
    {
        int amplitude = planet.MapDetailAmplitude;
        if (amplitude <= 0 || blocks == 0)
        {
            return 0;
        }

        int detail = (int)System.Math.Round((FbmT(w.Seed ^ MapDetailSalt, worldX, worldZ, MapDetailScale, octaves: 3) - 0.5) * 2.0 * amplitude);
        detail = System.Math.Max(-amplitude, System.Math.Min(amplitude, detail));
        int reach = amplitude + 1;
        return detail * System.Math.Min(System.Math.Abs(blocks), reach) / reach; // |result| < |blocks|: the sign holds
    }
}
