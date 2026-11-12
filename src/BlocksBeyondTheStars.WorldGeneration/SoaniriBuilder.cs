// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The geometry of Soaniri's three places (#3358; #3522 moved it out of <c>GameServerSounderWorld</c>): the village
/// <b>Mirun</b>, the ruin city <b>Ornasa</b> and the <b>Choir Tower</b>, each around its centre column with its floor at
/// <c>floorY</c>. Each levels a round footprint first (fill up from the natural ground — <c>ground</c> — to the floor, clear
/// the hills and the meadow props above it), so it reads the sink: a clear writes only where something stands, the Mirun reed
/// fringe grows only on open floor and the Choir Tower's barren ring turns only chime moss into scree. The decorative choices
/// (broken heights, gaps, the paving) come from a stable hash of the salt, the body's location id and the cell
/// (<see cref="Hash"/>) — the same body always gets the same ruin. A block missing from the content writes nothing.
/// </summary>
public sealed partial class SoaniriBuilder
{
    /// <summary>The village of the last Sounders.</summary>
    public const string MirunKey = "mirun";

    /// <summary>The ruin city of the old Sounders.</summary>
    public const string OrnasaKey = "ornasa";

    /// <summary>The Choir Tower — the finale stage.</summary>
    public const string ChoirTowerKey = "choir_tower";

    /// <summary>How far above the floor (or the natural ground, where that is higher) the levelling clears — the meadow props
    /// stand up to seven cells tall.</summary>
    public const int ClearHeadroom = 9;

    private readonly IStructureCellSink _sink;
    private readonly GameContent _content;
    private readonly string _locationId;
    private readonly Func<int, int, int> _ground;
    private readonly Palette _b;

    /// <summary>A builder writing into <paramref name="sink"/>: the blocks from <paramref name="content"/>, the decorative
    /// hashes keyed by <paramref name="locationId"/> (the body's id), the natural ground from <paramref name="ground"/>
    /// (x, z → the surface row).</summary>
    public SoaniriBuilder(IStructureCellSink sink, GameContent content, string locationId, Func<int, int, int> ground)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _locationId = locationId ?? string.Empty;
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _b = new Palette(content);
    }

    /// <summary>Builds the site <paramref name="key"/> (<see cref="MirunKey"/>, <see cref="OrnasaKey"/> or
    /// <see cref="ChoirTowerKey"/>) around (<paramref name="cx"/>, <paramref name="cz"/>) on the floor row
    /// <paramref name="floorY"/>; any other key builds nothing.</summary>
    public void Build(string key, int cx, int floorY, int cz)
    {
        switch (key)
        {
            case MirunKey:
                BuildMirun(cx, floorY, cz);
                break;
            case OrnasaKey:
                BuildOrnasa(cx, floorY, cz);
                break;
            case ChoirTowerKey:
                BuildChoirTower(cx, floorY, cz);
                break;
        }
    }

    /// <summary>The blocks the Soaniri sites are built of (air where a key is missing, which leaves that part out).</summary>
    private sealed class Palette
    {
        public Palette(GameContent c)
        {
            CarvedObsidian = Of(c, "carved_obsidian");
            Crystal = Of(c, "resonance_crystal");
            Moss = Of(c, "chime_moss");
            Scree = Of(c, "scree");
            Dirt = Of(c, "dirt");
            Stone = Of(c, "stone");
            Brick = Of(c, "ancient_brick");
            Rune = Of(c, "rune_stone");
            Planks = Of(c, "wood_planks");
            LightPlanks = Of(c, "wood_planks_light");
            Lantern = Of(c, "paper_lantern");
            Chime = Of(c, "chime");
            Reed = Of(c, FloraCatalog.ChimeReedKey);
            Bloom = Of(c, FloraCatalog.BellBloomKey);
            Echo = Of(c, FloraCatalog.EchoFlowerKey);
        }

        public BlockId CarvedObsidian { get; }
        public BlockId Crystal { get; }
        public BlockId Moss { get; }
        public BlockId Scree { get; }
        public BlockId Dirt { get; }
        public BlockId Stone { get; }
        public BlockId Brick { get; }
        public BlockId Rune { get; }
        public BlockId Planks { get; }
        public BlockId LightPlanks { get; }
        public BlockId Lantern { get; }
        public BlockId Chime { get; }
        public BlockId Reed { get; }
        public BlockId Bloom { get; }
        public BlockId Echo { get; }

        private static BlockId Of(GameContent c, string key) => c.GetBlock(key)?.NumericId ?? BlockId.Air;
    }

    /// <summary>Writes one site cell (a missing block — air id — writes nothing).</summary>
    private void Set(int x, int y, int z, BlockId id)
    {
        if (!id.IsAir)
        {
            _sink.Put(x, y, z, id);
        }
    }

    /// <summary>Clears one cell to air — only when it holds something, so the empty sky costs no edit.</summary>
    private void Clear(int x, int y, int z)
    {
        if (!_sink.Get(x, y, z).IsAir)
        {
            _sink.Put(x, y, z, BlockId.Air);
        }
    }

    /// <summary>
    /// Levels a round footprint: every column within <paramref name="radius"/> gets <paramref name="floor"/> at
    /// <paramref name="floorY"/>, <paramref name="fill"/> from its natural ground up to the floor, and air from the floor up to
    /// <see cref="ClearHeadroom"/> over the higher of floor and ground (hills cut back, meadow props removed).
    /// </summary>
    private void LevelDisc(int cx, int cz, int floorY, int radius, BlockId floor, BlockId fill)
    {
        int r2 = (radius * radius) + radius;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                if ((dx * dx) + (dz * dz) > r2)
                {
                    continue;
                }

                int x = cx + dx, z = cz + dz;
                int ground = _ground(x, z);
                for (int y = ground + 1; y < floorY; y++)
                {
                    Set(x, y, z, fill);
                }

                Set(x, floorY, z, floor);
                int top = Math.Max(ground, floorY) + ClearHeadroom;
                for (int y = floorY + 1; y <= top; y++)
                {
                    Clear(x, y, z);
                }
            }
        }
    }

    /// <summary>A column of <paramref name="id"/> from <paramref name="fromY"/> to <paramref name="toY"/> (inclusive).</summary>
    private void Column(int x, int z, int fromY, int toY, BlockId id)
    {
        for (int y = fromY; y <= toY; y++)
        {
            Set(x, y, z, id);
        }
    }

    /// <summary>A plant on a floor cell — the floor under it becomes chime moss so the plant has its host.</summary>
    private void Plant(int x, int floorY, int z, BlockId plant)
    {
        if (plant.IsAir)
        {
            return;
        }

        Set(x, floorY, z, _b.Moss);
        Set(x, floorY + 1, z, plant);
    }

    /// <summary>A stable per-cell hash in [0, 1) for the decorative choices of a site (broken heights, gaps): the salt, the
    /// body's location id and the cell.</summary>
    public static double Hash(string salt, string locationId, int x, int z)
        => (WorldGenerator.StableHash(FormattableString.Invariant($"{salt}:{locationId}:{x}:{z}")) & 0xFFFFFF) / (double)0x1000000;

    private double Hash(string salt, int x, int z) => Hash(salt, _locationId, x, z);
}
