// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>What covers a far-terrain column.</summary>
    public enum FarSurface : byte
    {
        Ground = 0,
        Water = 1,
        Lava = 2,
        Gas = 3, // #2112: the gas giant's sea of gas
    }

    /// <summary>One sampled far-terrain column: the top of what the player would see there.</summary>
    public readonly struct FarSample
    {
        /// <summary>World Y of the top surface (the upper face of the top block, or the fluid surface).</summary>
        public readonly int Top;
        public readonly FarSurface Surface;

        /// <summary>The surface block's numeric id (ground), 0 for fluids or when unknown.</summary>
        public readonly ushort Block;

        public FarSample(int top, FarSurface surface, ushort block)
        {
            Top = top;
            Surface = surface;
            Block = block;
        }
    }

    /// <summary>
    /// The far terrain's view of the world (#1820): a private <see cref="WorldGenerator"/> configured exactly like the
    /// server's for this world (<see cref="FarTerrainWorldInfo"/>) — seed, body salt, size, cratering, pads, continents,
    /// volcanoes, terrain generation. Heights and fluids therefore match the streamed chunks column for column; only
    /// what was BUILT is missing, which <see cref="FarTerrainOverlay"/> adds. Single-threaded use (the generator's memos
    /// make repeated columns cheap).
    /// </summary>
    public sealed class FarTerrainSource
    {
        private readonly WorldGenerator _generator;
        private readonly Dictionary<string, ushort> _blockIds = new Dictionary<string, ushort>();
        private readonly GameContent _content;
        private readonly bool _seaIsWater;
        private readonly bool _seaIsGas; // #2112
        private readonly bool _pondsAndRivers;
        private readonly bool _bands; // #2333: the world can carry extra bands (sky islands, arch bars, crowns)

        public PlanetType Planet { get; }
        public int Circumference { get; }
        public int LatitudePeriod { get; }

        /// <summary>World Y of the sea surface, or <see cref="int.MinValue"/> on a world without a sea.</summary>
        public int SeaLevel { get; }

        public int WorldId { get; }

        private FarTerrainSource(GameContent content, WorldGenerator generator, PlanetType planet, FarTerrainWorldInfo info)
        {
            _content = content;
            _generator = generator;
            Planet = planet;
            WorldId = info.WorldId;
            Circumference = info.Circumference > 0 ? info.Circumference : WorldConstants.Circumference;
            LatitudePeriod = WorldConstants.LatitudePeriodFor(Circumference);
            SeaLevel = generator.SeaLevel(planet);
            _seaIsWater = SeaLevel != int.MinValue && generator.SeaIsWater(planet);
            _seaIsGas = SeaLevel != int.MinValue && generator.SeaIsGas(planet);
            _pondsAndRivers = !planet.IsAirless;
            _bands = generator.HasExtraBands(planet);
        }

        /// <summary>Builds the source for a world, or null for a void world / an unknown planet type.</summary>
        public static FarTerrainSource? Create(GameContent content, long worldSeed, FarTerrainWorldInfo info)
        {
            if (info.Void || content.GetPlanet(info.PlanetType) is not { } planet || planet.Void)
            {
                return null;
            }

            var generator = new WorldGenerator(worldSeed, content);
            generator.SetContinentsEnabled(info.ContinentsEnabled);
            generator.SetLavaCoreVolcanoes(info.LavaCoreVolcanoes);
            generator.SetTerrainGeneration(info.TerrainGeneration);
            generator.SetWorldMode(info.Circumference > 0 ? info.Circumference : WorldConstants.Circumference,
                info.Cratered, UnpackPads(info.Pads), string.IsNullOrEmpty(info.LocationId) ? null : info.LocationId);
            return new FarTerrainSource(content, generator, planet, info);
        }

        internal static List<LandingPadFlatten> UnpackPads(int[]? packed)
        {
            var pads = new List<LandingPadFlatten>();
            if (packed == null)
            {
                return pads;
            }

            for (int o = 0; o + FarTerrainWorldInfo.PadStride <= packed.Length; o += FarTerrainWorldInfo.PadStride)
            {
                pads.Add(new LandingPadFlatten(packed[o], packed[o + 1], packed[o + 2], packed[o + 3],
                    islet: packed[o + 4] != 0, plateauRadius: packed[o + 5], isletRadius: packed[o + 6],
                    classicShape: packed[o + 7] != 0, molten: packed[o + 4] == 2, deck: packed[o + 4] == 3));
            }

            return pads;
        }

        /// <summary>The generator's natural terrain height at a column (the top solid block's Y).</summary>
        public int GroundHeight(int worldX, int worldZ) => _generator.SurfaceHeight(Planet, worldX, worldZ);

        /// <summary>Samples a column. <paramref name="detail"/> adds ponds, rivers and the biome surface block (the near
        /// level); the coarse level reads the planet's default surface to stay cheap.</summary>
        public FarSample Sample(int worldX, int worldZ, bool detail)
        {
            int ground = _generator.SurfaceHeight(Planet, worldX, worldZ);

            // #2333: a band over the column — a sky island, an arch bar, a pillar crown — is what the eye sees from
            // afar, so it is the sample's top, over dry ground and over the sea alike.
            if (_bands && _generator.TryGetHighestBand(Planet, worldX, worldZ, out var band) && band.Top >= ground
                && (SeaLevel == int.MinValue || band.Top >= SeaLevel))
            {
                return BandSample(band, worldX, worldZ, detail);
            }

            if (SeaLevel != int.MinValue && ground <= SeaLevel) // the generator's own sea-column test
            {
                return new FarSample(SeaLevel + 1, _seaIsWater ? FarSurface.Water : _seaIsGas ? FarSurface.Gas : FarSurface.Lava, 0);
            }

            if (detail && _pondsAndRivers
                && (_generator.SurfacePondDepth(Planet, worldX, worldZ) > 0 || _generator.SurfaceRiverDepth(Planet, worldX, worldZ) > 0))
            {
                return new FarSample(ground + 1, FarSurface.Water, 0);
            }

            string? key = detail ? _generator.BiomeSurfaceKeyAt(Planet, worldX, worldZ) : null;
            return new FarSample(ground + 1, FarSurface.Ground, BlockIdOf(key ?? Planet.SurfaceBlock));
        }

        /// <summary>What a band's top reads as from afar (#2333): a pond or a fluid band is the sea's fluid, an ice
        /// band ice, a mat mud, an island / crown / afloat deck the biome's ground (the planet's surface block on the
        /// coarse level), a rock bar or cap its own material or the deep block.</summary>
        private FarSample BandSample(in WorldGenerator.ColumnBand band, int worldX, int worldZ, bool detail)
        {
            int top = band.Top + 1;
            switch (band.Kind)
            {
                case WorldGenerator.BandKind.IslandPond:
                    return new FarSample(top, FarSurface.Water, 0);
                case WorldGenerator.BandKind.Fluid:
                    return new FarSample(top, _seaIsWater ? FarSurface.Water : _seaIsGas ? FarSurface.Gas : FarSurface.Lava, 0);
                case WorldGenerator.BandKind.Ice:
                    return new FarSample(top, FarSurface.Ground, BlockIdOf("ice"));
                case WorldGenerator.BandKind.Mat:
                    return new FarSample(top, FarSurface.Ground, BlockIdOf("mud"));
                case WorldGenerator.BandKind.Rock:
                case WorldGenerator.BandKind.Cap:
                    return new FarSample(top, FarSurface.Ground,
                        band.Material.IsAir ? BlockIdOf(Planet.DeepBlock) : band.Material.Value);
                default: // Island, Afloat, Crown: a meadow on the band
                    string? key = detail ? _generator.BiomeSurfaceKeyAt(Planet, worldX, worldZ) : null;
                    return new FarSample(top, FarSurface.Ground, BlockIdOf(key ?? Planet.SurfaceBlock));
            }
        }

        private ushort BlockIdOf(string key)
        {
            if (!_blockIds.TryGetValue(key, out var id))
            {
                id = _content.GetBlock(key)?.NumericId.Value ?? 0;
                _blockIds[key] = id;
            }

            return id;
        }
    }
}
