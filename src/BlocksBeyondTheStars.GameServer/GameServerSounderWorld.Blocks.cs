// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3358: the block writing the three Soaniri sites share — the palette, a seam-safe set, the levelling of a round footprint
/// (fill up from the natural ground to the floor, clear the hills and the meadow props above it) and the helpers for walls.
/// Everything here runs once, inside the stamp's repo transaction.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far above the floor (or the natural ground, where that is higher) the levelling clears — the meadow props
    /// stand up to seven cells tall.</summary>
    private const int SounderClearHeadroom = 9;

    /// <summary>The blocks the Soaniri sites are built of (air where a key is missing, which leaves that part out).</summary>
    private readonly struct SounderBlocks
    {
        public SounderBlocks(GameServer s)
        {
            CarvedObsidian = s.SounderBlock("carved_obsidian");
            Obsidian = s.SounderBlock("obsidian");
            Crystal = s.SounderBlock("resonance_crystal");
            Moss = s.SounderBlock("chime_moss");
            Scree = s.SounderBlock("scree");
            Dirt = s.SounderBlock("dirt");
            Stone = s.SounderBlock("stone");
            Brick = s.SounderBlock("ancient_brick");
            Rune = s.SounderBlock("rune_stone");
            Planks = s.SounderBlock("wood_planks");
            LightPlanks = s.SounderBlock("wood_planks_light");
            Lantern = s.SounderBlock("paper_lantern");
            Chime = s.SounderBlock("chime");
            Reed = s.SounderBlock(FloraCatalog.ChimeReedKey);
            Bloom = s.SounderBlock(FloraCatalog.BellBloomKey);
            Echo = s.SounderBlock(FloraCatalog.EchoFlowerKey);
            Vine = s.SounderBlock(FloraCatalog.HarpVineKey);
        }

        public BlockId CarvedObsidian { get; }
        public BlockId Obsidian { get; }
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
        public BlockId Vine { get; }
    }

    private BlockId SounderBlock(string key) => _content.GetBlock(key)?.NumericId ?? BlockId.Air;

    /// <summary>The canonical cell of a site coordinate (the world wraps east–west and north–south).</summary>
    private Vector3i SounderCell(int x, int y, int z) => WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);

    /// <summary>Writes one site cell (a missing block — air id — writes nothing).</summary>
    private void SounderSet(int x, int y, int z, BlockId id)
    {
        if (!id.IsAir)
        {
            _world.SetBlock(SounderCell(x, y, z), id);
        }
    }

    /// <summary>Clears one cell to air — only when it holds something, so the empty sky costs no edit.</summary>
    private void SounderClear(int x, int y, int z)
    {
        var cell = SounderCell(x, y, z);
        if (!_world.GetBlock(cell).IsAir)
        {
            _world.SetBlock(cell, BlockId.Air);
        }
    }

    /// <summary>
    /// Levels a round footprint: every column within <paramref name="radius"/> gets <paramref name="floor"/> at
    /// <paramref name="floorY"/>, <paramref name="fill"/> from its natural ground up to the floor, and air from the floor up to
    /// <see cref="SounderClearHeadroom"/> over the higher of floor and ground (hills cut back, meadow props removed).
    /// </summary>
    private void LevelSounderDisc(int cx, int cz, int floorY, int radius, BlockId floor, BlockId fill)
    {
        var planet = _world.Planet;
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
                int ground = _generator.SurfaceHeight(planet, x, z);
                for (int y = ground + 1; y < floorY; y++)
                {
                    SounderSet(x, y, z, fill);
                }

                SounderSet(x, floorY, z, floor);
                int top = Math.Max(ground, floorY) + SounderClearHeadroom;
                for (int y = floorY + 1; y <= top; y++)
                {
                    SounderClear(x, y, z);
                }
            }
        }
    }

    /// <summary>A column of <paramref name="id"/> from <paramref name="fromY"/> to <paramref name="toY"/> (inclusive).</summary>
    private void SounderColumn(int x, int z, int fromY, int toY, BlockId id)
    {
        for (int y = fromY; y <= toY; y++)
        {
            SounderSet(x, y, z, id);
        }
    }

    /// <summary>A plant on a floor cell — the floor under it becomes chime moss so the plant has its host.</summary>
    private void SounderPlant(in SounderBlocks b, int x, int floorY, int z, BlockId plant)
    {
        if (plant.IsAir)
        {
            return;
        }

        SounderSet(x, floorY, z, b.Moss);
        SounderSet(x, floorY + 1, z, plant);
    }

    /// <summary>A stable per-cell hash in [0, 1) for the decorative choices of a site (broken heights, gaps).</summary>
    private double SounderHash(string salt, int x, int z)
        => (WorldGenerator.StableHash(FormattableString.Invariant($"{salt}:{_world.LocationId}:{x}:{z}"))
            & 0xFFFFFF) / (double)0x1000000;
}
