// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
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
        /// <summary>The most floating shapes one column reports (#3958): up to three sky-island tiers, a bar or a cap, the
        /// waterfalls under them. Further ones are dropped.</summary>
        public const int MaxFloats = 6;

        /// <summary>A band whose bottom lies at most this many blocks over the column's top rests on it (#3958): a crown on
        /// its pillar, an afloat island on the sea, an iceberg — it raises the height field as before. A wider gap is open
        /// air from afar, so the band floats as its own slab.</summary>
        public const int AttachGap = 3;

        private readonly WorldGenerator _generator;
        private readonly Dictionary<string, ushort> _blockIds = new Dictionary<string, ushort>();
        private readonly GameContent _content;
        private readonly bool _seaIsWater;
        private readonly bool _seaIsGas; // #2112
        private readonly bool _pondsAndRivers;
        private readonly bool _bands; // #2333: the world can carry extra bands (sky islands, arch bars, crowns)
        private bool? _ventsVolcanic; // #3275: asked once

        public PlanetType Planet { get; }
        public int Circumference { get; }
        public int LatitudePeriod { get; }

        /// <summary>World Y of the sea surface, or <see cref="int.MinValue"/> on a world without a sea.</summary>
        public int SeaLevel { get; }

        /// <summary>True when the world's sea is water (not lava, not gas): the far-terrain shader then recolours the
        /// sea vertices from the world's water tint (#2415) instead of reading their alpha as a lava glow.</summary>
        public bool SeaIsWater => _seaIsWater;

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
            var generator = FarWorldGenerator.Create(content, worldSeed, info, out var planet); // #3289: shared with the biome looks
            return generator == null ? null : new FarTerrainSource(content, generator, planet, info);
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

        /// <summary>#3277: the volcano craters within <paramref name="radius"/> blocks of a column, in that column's frame —
        /// the generator's own answer (<see cref="WorldGenerator.VolcanoCratersNear"/>), so the crater smoke stands where
        /// the streamed volcano is.</summary>
        public void VolcanoCratersNear(int worldX, int worldZ, int radius, List<VolcanoCrater> result)
            => _generator.VolcanoCratersNear(Planet, worldX, worldZ, radius, result);

        /// <summary>#3275: whether this world's geyser vents are volcanic (ash, embers) rather than water geysers
        /// (<see cref="WorldGenerator.VentsAreVolcanic"/>).</summary>
        public bool VentsAreVolcanic => _ventsVolcanic ??= _generator.VentsAreVolcanic(Planet);

        /// <summary>Samples a column's height-field top, dropping what floats over it (see the overload).</summary>
        public FarSample Sample(int worldX, int worldZ, bool detail) => Sample(worldX, worldZ, detail, default, out _);

        /// <summary>Samples a column. <paramref name="detail"/> adds ponds, rivers and the biome surface block (the near
        /// level); the coarse level reads the planet's default surface to stay cheap. The returned sample is the column's
        /// height-field top: the ground or the sea, raised by every band that rests on it (#2333: a crown on its pillar,
        /// an afloat island, an iceberg). A band with open air under it — a sky island, an arch bar, a cap's overhang —
        /// goes into <paramref name="floats"/> instead (#3958), bottom-up, merged where bands touch, followed by the
        /// column's waterfalls (#3959); a height field drawn up to an island's top made every island a mountain.</summary>
        public FarSample Sample(int worldX, int worldZ, bool detail, System.Span<FarFloat> floats, out int floatCount)
        {
            floatCount = 0;
            int ground = _generator.SurfaceHeight(Planet, worldX, worldZ);
            var sample = GroundSample(worldX, worldZ, ground, detail);
            if (!_bands)
            {
                return sample;
            }

            System.Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
            int n = _generator.GetExtraBands(Planet, worldX, worldZ, bands);
            if (n == 0)
            {
                return sample;
            }

            SortByBottom(bands.Slice(0, n));
            for (int i = 0; i < n; i++)
            {
                var band = bands[i];
                if (band.Kind == WorldGenerator.BandKind.Waterfall || band.Top + 1 <= sample.Top)
                {
                    continue; // a waterfall comes last; a band inside the column (a sea-cave pool, a buried bar) never shows
                }

                if (floatCount == 0 && band.Bottom <= sample.Top + AttachGap)
                {
                    sample = BandSample(band, worldX, worldZ, detail); // it rests on the column: the height field rises
                    continue;
                }

                AddFloat(floats, ref floatCount, band, BandSample(band, worldX, worldZ, detail));
            }

            for (int i = 0; i < n && floatCount < floats.Length; i++)
            {
                var band = bands[i];
                if (band.Kind == WorldGenerator.BandKind.Waterfall && band.Top + 1 > sample.Top)
                {
                    var surface = new FarSample(band.Top + 1, FarSurface.Water, 0);
                    floats[floatCount++] = new FarFloat(System.Math.Max(band.Bottom, sample.Top), band.Top + 1, FarFloatKind.Waterfall, surface, 0);
                }
            }

            return sample;
        }

        /// <summary>Adds a free band to the column's floats — onto the last slab when the two touch, else as a new one.</summary>
        private void AddFloat(System.Span<FarFloat> floats, ref int count, in WorldGenerator.ColumnBand band, FarSample surface)
        {
            if (count > 0 && band.Bottom <= floats[count - 1].Top + AttachGap)
            {
                if (band.Top + 1 > floats[count - 1].Top)
                {
                    floats[count - 1] = floats[count - 1].WithTop(band.Top + 1, surface);
                }

                return;
            }

            if (count < floats.Length)
            {
                floats[count++] = new FarFloat(band.Bottom, band.Top + 1, FarFloatKind.Solid, surface, SideBlockOf(band));
            }
        }

        /// <summary>Insertion sort by bottom (a column carries a handful of bands).</summary>
        private static void SortByBottom(System.Span<WorldGenerator.ColumnBand> bands)
        {
            for (int i = 1; i < bands.Length; i++)
            {
                var band = bands[i];
                int j = i - 1;
                while (j >= 0 && bands[j].Bottom > band.Bottom)
                {
                    bands[j + 1] = bands[j];
                    j--;
                }

                bands[j + 1] = band;
            }
        }

        /// <summary>What a floating band's sides and underside are made of: its own rock, ice or mud, else the deep block.</summary>
        private ushort SideBlockOf(in WorldGenerator.ColumnBand band) => band.Kind switch
        {
            WorldGenerator.BandKind.Ice => BlockIdOf("ice"),
            WorldGenerator.BandKind.Mat => BlockIdOf("mud"),
            _ => band.Material.IsAir ? BlockIdOf(Planet.DeepBlock) : band.Material.Value,
        };

        /// <summary>The column's own top without any band: the sea (or its ice), a pond or river, or the covered land.</summary>
        private FarSample GroundSample(int worldX, int worldZ, int ground, bool detail)
        {
            if (SeaLevel != int.MinValue && ground <= SeaLevel) // the generator's own sea-column test
            {
                // #2956: a frozen sea is ice from afar, as it is in the chunks — every column asks the chunks' own freeze
                // rule (the ragged freeze edge, a hot zone's open water), on both levels, so they agree at their seam; a
                // world that never freezes answers at the first check.
                if (_seaIsWater && _generator.TryGetIceSurface(Planet, worldX, worldZ, out _))
                {
                    return new FarSample(SeaLevel + 1, FarSurface.Ground, BlockIdOf("ice"));
                }

                return new FarSample(SeaLevel + 1, _seaIsWater ? FarSurface.Water : _seaIsGas ? FarSurface.Gas : FarSurface.Lava, 0);
            }

            if (detail && _pondsAndRivers
                && (_generator.SurfacePondDepth(Planet, worldX, worldZ) > 0 || _generator.SurfaceRiverDepth(Planet, worldX, worldZ) > 0))
            {
                return _generator.TryGetIceSurface(Planet, worldX, worldZ, out _) // #2956: a frozen pond or river
                    ? new FarSample(ground + 1, FarSurface.Ground, BlockIdOf("ice"))
                    : new FarSample(ground + 1, FarSurface.Water, 0);
            }

            // #2956: the land wears the column pass's altitude snow and ice, not the bare biome surface.
            string? key = detail ? _generator.BiomeSurfaceKeyAt(Planet, worldX, worldZ) : null;
            var bare = new BlockId(BlockIdOf(key ?? Planet.SurfaceBlock));
            return new FarSample(ground + 1, FarSurface.Ground, _generator.AltitudeClimateSurface(Planet, bare, ground).Value);
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
