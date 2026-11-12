// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3522: the server's former direct writers — vault (+ crystal puzzle, wellhead), Singing Shrine, Sealed Observatory, Mirun,
/// Ornasa, Choir Tower, Guardian-core chamber, hunter camp — are <c>WorldGeneration</c> builders, and their sources are the
/// builder on flat ground. Here every source is checked against its builder run into an independent flat world in miniature
/// (written blocks with their dye/glow/shape, carved ground as cavity, markers in order, the surface as the ground row), plus
/// the invariants each one promises. That the server's writers still put the very same blocks into a world is pinned by the
/// placement goldens (vaults incl. a wellhead and a crystal vault, the shrine, the observatory, Soaniri) and the writer
/// goldens (Guardian core, hunter camp) of #3511.
/// </summary>
public sealed partial class StructureSourceTests
{
    private const int Flat = FlatGroundSink.GroundY;

    /// <summary>A flat world in miniature: stone up to <see cref="Flat"/>, air above; it applies a builder's writes like a
    /// world and remembers whether a cell ever held something (terrain or a write) before it was carved.</summary>
    private sealed class FlatWorld : IStructureCellSink
    {
        private readonly BlockId _ground;

        public FlatWorld(BlockId ground) => _ground = ground;

        public Dictionary<(int X, int Y, int Z), (BlockId Block, int Tint, int Glow, int Shape)> Cells { get; } = new();

        public HashSet<(int X, int Y, int Z)> HeldSomething { get; } = new();

        public List<BuiltMarker> Marks { get; } = new();

        public void Set(int x, int y, int z, BlockId block, int tint, int glow, int shape)
        {
            if (!Get(x, y, z).IsAir)
            {
                HeldSomething.Add((x, y, z));
            }

            Cells[(x, y, z)] = (block, tint, glow, shape);
        }

        public BlockId Get(int x, int y, int z) => Cells.TryGetValue((x, y, z), out var c) ? c.Block : y <= Flat ? _ground : BlockId.Air;

        public void Mark(string type, int x, int y, int z, string data) => Marks.Add(new BuiltMarker(type, x, y, z, data));
    }

    private static BlockId Stone => Content.GetBlock("stone")!.NumericId;

    private static System.Random Rng(long seed) => new(unchecked((int)(seed ^ (seed >> 32))));

    /// <summary>The source's structure is exactly what the builder leaves in the flat world.</summary>
    private static void AssertIsTheBuilder(BuiltStructure b, FlatWorld world)
    {
        var solid = world.Cells.Where(kv => !kv.Value.Block.IsAir).ToList();
        var carved = world.Cells.Where(kv => kv.Value.Block.IsAir && world.HeldSomething.Contains(kv.Key)).Select(kv => kv.Key).ToList();
        var all = solid.Select(kv => kv.Key).Concat(carved).Concat(world.Marks.Select(m => (m.X, m.Y, m.Z))).ToList();
        int minX = all.Min(c => c.X), minY = all.Min(c => c.Y), minZ = all.Min(c => c.Z);
        Assert.Equal(all.Max(c => c.X) - minX + 1, b.Width);
        Assert.Equal(all.Max(c => c.Y) - minY + 1, b.Height);
        Assert.Equal(all.Max(c => c.Z) - minZ + 1, b.Length);
        Assert.Equal(Math.Clamp(Flat - minY, 0, b.Height - 1), b.GroundRow);
        foreach (var (cell, v) in solid)
        {
            int x = cell.X - minX, y = cell.Y - minY, z = cell.Z - minZ;
            Assert.Equal(v.Block.Value, b.Get(x, y, z));
            Assert.Equal((v.Tint, v.Glow), b.GetModifier(x, y, z));
            Assert.Equal(v.Shape, b.GetShape(x, y, z));
        }

        Assert.Equal(solid.Count, b.CountBlocks());
        Assert.Equal(carved.Select(c => b.Index(c.X - minX, c.Y - minY, c.Z - minZ)).OrderBy(i => i), b.Cavity.OrderBy(i => i));
        AssertMarkers(world.Marks.Select(m => new BuiltMarker(m.Type, m.X - minX, m.Y - minY, m.Z - minZ, m.Data)), b.Markers);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Vault_IsTheBuildersVault(bool wellhead, bool crystal)
    {
        var world = new FlatWorld(Stone);
        int? waterTop = wellhead ? Flat + VaultSource.WellWaterDepth : null;
        VaultBuilder.Build(world, Content, 0, 0, Flat, waterTop, Rng(SeedB), (_, _) => Flat);
        if (crystal)
        {
            VaultBuilder.BuildCrystalPuzzle(world, Content, 0, 0, VaultBuilder.FloorY(Flat));
        }

        var b = Build("vault", SeedB, ("wellhead", wellhead ? "true" : "false"), ("crystal", crystal ? "true" : "false"));
        AssertIsTheBuilder(b, world);
    }

    [Fact]
    public void Vault_KeepsItsChamberSixteenDown_TheWellAboveTheWater_AndThePuzzleOnlyWhenAsked()
    {
        var plain = Build("vault", SeedA);
        var loot = plain.Markers.Where(m => m.Type is "loot" or "data_terminal").ToList();
        Assert.Equal(3, loot.Count);
        Assert.All(loot, m => Assert.Equal(VaultBuilder.ChamberDepth, plain.GroundRow - m.Y));
        Assert.DoesNotContain(plain.Markers, m => m.Type == TemplateDevices.Marker);
        Assert.InRange(plain.Height - 1 - plain.GroundRow, 1, 2); // the pillar ring: one or two stones over the surface

        var well = Build("vault", SeedA, ("wellhead", "true"));
        Assert.Equal(VaultSource.WellWaterDepth + 1, well.Height - 1 - well.GroundRow); // the tube's mouth over the water line

        var puzzle = Build("vault", SeedA, ("crystal", "true"));
        var devices = puzzle.Markers.Where(m => m.Type == TemplateDevices.Marker).ToList();
        Assert.Equal(18, devices.Count); // door ×2, the wire over it, the gate, and per side four + two wires and a switch
        Assert.All(devices, m => Assert.NotEqual(0, puzzle.Get(m.X, m.Y, m.Z)));
        Assert.Equal(4, puzzle.Markers.Count(m => m.Type == "loot" || m.Type == "data_terminal")); // + the niche's cache
    }

    [Fact]
    public void UniqueSites_AreTheirBuilders_StandingOnTheGround()
    {
        var shrine = new FlatWorld(Stone);
        UniqueSiteBuilder.BuildShrine(shrine, Content, 0, Flat, 0);
        var b = Build("singing_shrine", SeedA);
        AssertIsTheBuilder(b, shrine);
        Assert.Equal(0, b.GroundRow); // the plinth replaces the top terrain cells
        Assert.Equal(b.ContentHash(), Build("singing_shrine", SeedB).ContentHash()); // one shrine, whatever the seed

        var observatory = new FlatWorld(Stone);
        UniqueSiteBuilder.BuildObservatory(observatory, Content, 0, Flat, 0);
        var o = Build("sealed_observatory", SeedB);
        AssertIsTheBuilder(o, observatory);
        Assert.Equal(0, o.GroundRow);
        Assert.Single(o.Markers, m => m.Type == "data_terminal");
    }

    [Theory]
    [InlineData(SoaniriBuilder.MirunKey)]
    [InlineData(SoaniriBuilder.OrnasaKey)]
    [InlineData(SoaniriBuilder.ChoirTowerKey)]
    public void SoaniriSites_AreTheirBuilders_TheSeedStandingInForTheBody(string key)
    {
        foreach (long seed in new[] { SeedA, SeedB })
        {
            var world = new FlatWorld(Stone);
            new SoaniriBuilder(world, Content, seed.ToString(CultureInfo.InvariantCulture), (_, _) => Flat).Build(key, 0, Flat, 0);
            AssertIsTheBuilder(Build(key, seed), world);
        }
    }

    [Fact]
    public void SoaniriSites_BreakByTheirHash_AndTheChoirTowersRingTurnsOnlyChimeMossToScree()
    {
        Assert.NotEqual(Build("ornasa", SeedA).ContentHash(), Build("ornasa", SeedB).ContentHash()); // walls broken per body
        Assert.Equal(Build("choir_tower", SeedA).ContentHash(), Build("choir_tower", SeedB).ContentHash()); // nothing hashed

        ushort scree = Content.GetBlock("scree")!.NumericId.Value;
        var onStone = Build("choir_tower", SeedA);
        var onMoss = StructureSources.Get("choir_tower")!.Build(new StructureBuildRequest(Content, SeedA) { Surface = "chime_moss" });
        Assert.DoesNotContain(scree, onStone.Blocks);
        Assert.Contains(scree, onMoss.Blocks);
        Assert.True(onStone.Height - 1 - onStone.GroundRow > SounderSiteLayout.ChoirTowerHeight, "the tower and its pipes rise over the platform");
    }

    [Fact]
    public void GuardianCore_IsTheBuildersChamber_WithItsCoreTwentyThreeDown()
    {
        var world = new FlatWorld(Stone);
        GuardianCoreBuilder.Build(world, Content, 0, 0, GuardianCoreBuilder.FloorY(Flat), Flat, (_, _) => Flat);
        var b = Build("guardian_core", SeedA);
        AssertIsTheBuilder(b, world);
        var core = Assert.Single(b.Markers, m => m.Type == GuardianCoreBuilder.CoreMarker);
        Assert.Equal(GuardianCoreBuilder.ShaftDepth, b.GroundRow - core.Y);
        Assert.Equal(Content.GetBlock("light_red")!.NumericId.Value, b.Get(core.X, core.Y, core.Z));
        Assert.True(b.Cavity.Count > 9 * 9 * 5, "the chamber and the aperture are carved out of the ground");
    }

    [Fact]
    public void HunterCamp_IsTheBuildersCamp_WithTwoOrThreeDocksBySeed()
    {
        var world = new FlatWorld(Stone);
        HunterCampBuilder.Build(world, Content, "stone", 0, Flat + 1, 0, Rng(SeedA), (_, _) => Flat + 1, (_, _, _) => true);
        AssertIsTheBuilder(Build("hunter_camp", SeedA), world);

        var counts = new HashSet<int>();
        for (long seed = 1; seed <= 12; seed++)
        {
            var b = Build("hunter_camp", seed);
            var docks = b.Markers.Where(m => m.Type == HunterCampBuilder.DockMarker).ToList();
            counts.Add(docks.Count);
            Assert.All(docks, d => Assert.Equal(Content.GetBlock(HunterShuttleHull.DockBlockKey)!.NumericId.Value, b.Get(d.X, d.Y, d.Z)));
            Assert.Equal(b.GroundRow + 1 + HunterShuttleHull.Bay.Y, docks[0].Y); // the shuttle's own dock in the hold
            Assert.All(docks.Skip(1), d => Assert.Equal(b.GroundRow + 1, d.Y)); // the others on the ground beside it
        }

        Assert.Equal(new[] { 2, 3 }, counts.OrderBy(c => c));
    }
}
