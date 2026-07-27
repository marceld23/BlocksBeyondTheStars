// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Monuments (#522–#527): eroded rune relics scattered on a body's surface. They must generate
/// deterministically with a readable silhouette, keep a gate walkable, stamp their voxels exactly once,
/// hold their recorded position across reloads, appear on airless bodies, and pay knowledge points when
/// their runes are scanned — once per body and archetype.
/// </summary>
public sealed class MonumentTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MonumentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_monument_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(string world, string planet, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceChests = false,
            PlaceWrecks = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
            PlaceBanditCamps = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static int Solids(SettlementStructure s)
    {
        int n = 0;
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    if (s.Get(x, y, z) != 0) n++;
                }

        return n;
    }

    [Theory]
    [InlineData("arcade")]
    [InlineData("gate")]
    [InlineData("circle")]
    [InlineData("obelisk")]
    [InlineData("altar")]
    public void EveryArchetype_Builds_Deterministically_WithRunesShapesAndGlow(string archetype)
    {
        ushort rune = _content.GetBlock("rune_stone")!.NumericId.Value;
        bool anyRune = false, anyShape = false, anyGlow = false, anyTall = false;

        for (long seed = 1; seed <= 12; seed++)
        {
            var a = MonumentGenerator.Generate(archetype, seed, "stone", _content, withCache: true);
            var b = MonumentGenerator.Generate(archetype, seed, "stone", _content, withCache: true);

            Assert.True(Solids(a) > 8, $"A {archetype} must leave a readable silhouette standing.");

            for (int x = 0; x < a.Width; x++)
                for (int y = 0; y < a.Height; y++)
                    for (int z = 0; z < a.Length; z++)
                    {
                        // Same seed ⇒ same stones, same shapes, same glow.
                        Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                        Assert.Equal(a.GetShape(x, y, z), b.GetShape(x, y, z));
                        Assert.Equal(a.GetModifier(x, y, z), b.GetModifier(x, y, z));

                        if (a.Get(x, y, z) == 0) continue;
                        anyRune |= a.Get(x, y, z) == rune;
                        anyShape |= a.GetShape(x, y, z) != 0;
                        anyGlow |= a.GetModifier(x, y, z).Glow != 0;
                        anyTall |= y >= 3;
                    }

            // A monument is always scannable: at least one rune survives erosion.
            Assert.Contains(rune, AllBlocks(a));
        }

        Assert.True(anyRune, "Runes are the point of the feature.");
        Assert.True(anyShape, "Monuments use block shapes (columns/arches/lintels), not just cubes.");
        Assert.True(anyGlow, "Runes carry an emissive glow so they read at night.");
        Assert.True(anyTall, $"A {archetype} should stand more than knee-high.");
    }

    private static IEnumerable<ushort> AllBlocks(SettlementStructure s)
    {
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    if (s.Get(x, y, z) != 0) yield return s.Get(x, y, z);
                }
    }

    [Fact]
    public void Gate_KeepsAWalkableOpening()
    {
        for (long seed = 1; seed <= 20; seed++)
        {
            var gate = MonumentGenerator.Generate("gate", seed, "stone", _content, withCache: false);
            int cz = gate.Length / 2;

            // The doorway columns must be clear for at least 3 blocks of head-room (the clearance rule that
            // applies to every opening a player is meant to walk through).
            for (int x = 5; x <= 7; x++)
            {
                for (int y = 1; y <= 3; y++)
                {
                    Assert.Equal(0, gate.Get(x, y, cz));
                }
            }
        }
    }

    [Fact]
    public void Monuments_AreStampedOnce_AndKeepTheirPlaceAcrossReloads()
    {
        // Find a seed whose start world actually carries a monument, then reload that same save.
        for (long seed = 1; seed <= 40; seed++)
        {
            string world = "mono" + seed;
            var first = Start(world, "jungle", seed, out var repo);
            List<(Vector3f Center, string Archetype)> before;
            using (repo)
            {
                before = first.MonumentsForTest().ToList();
                Assert.True(first.FeatureStampedForTest("monuments"), "The placement decision must be persisted.");
            }

            if (before.Count == 0)
            {
                continue;
            }

            // Re-open the same save: the relics must come back exactly where they were stamped.
            var again = Start(world, "jungle", seed, out var repo2);
            using (repo2)
            {
                var after = again.MonumentsForTest().ToList();
                Assert.Equal(before.Count, after.Count);
                for (int i = 0; i < before.Count; i++)
                {
                    Assert.Equal(before[i].Archetype, after[i].Archetype);
                    Assert.Equal(before[i].Center.X, after[i].Center.X, 3);
                    Assert.Equal(before[i].Center.Y, after[i].Center.Y, 3);
                    Assert.Equal(before[i].Center.Z, after[i].Center.Z, 3);
                }
            }

            return;
        }

        Assert.Fail("No monument was placed across 40 seeds — the count roll is too stingy.");
    }

    [Fact]
    public void Monuments_AlsoAppearOnAirlessBodies()
    {
        // Settlements, ruins and camps all skip airless worlds; a relic on a dead moon is the whole point.
        var airless = _content.Planets.Values.First(p => p.IsAirless);
        bool found = false;
        for (long seed = 1; seed <= 40 && !found; seed++)
        {
            var server = Start("airless" + seed, airless.Key, seed, out var repo);
            using (repo)
            {
                found = server.MonumentsForTest().Count > 0;
            }
        }

        Assert.True(found, $"Monuments should appear on the airless '{airless.Key}' across 40 seeds.");
    }

    [Fact]
    public void ScanningRunes_AtAMonument_PaysMoreThanTheBlock_AndOnlyOncePerArchetype()
    {
        var server = Start("scan", "rocky", 4242, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Digger");
            server.SpawnMonumentForTest(new Vector3f(120.5f, 40f, 120.5f), "circle");
            p.State.Position = new Vector3f(120.5f, 40f, 120.5f);

            var first = server.ScanSubject("Digger", "block", "rune_stone");
            Assert.True(first.FirstTime);
            Assert.Equal("monument", first.Kind);
            Assert.Equal("monument_circle", first.SubjectKey);
            Assert.Equal("ui.scan.monument.circle", first.InfoKey);
            int monumentGain = first.KnowledgeGained;
            Assert.True(monumentGain > 0);

            // The same relic on the same body pays once.
            var repeat = server.ScanSubject("Digger", "block", "rune_stone");
            Assert.False(repeat.FirstTime);
            Assert.Equal(0, repeat.KnowledgeGained);

            // A rune the player carried home is only a material.
            p.State.Position = new Vector3f(600.5f, 40f, 600.5f);
            var loose = server.ScanSubject("Digger", "block", "rune_stone");
            Assert.True(loose.FirstTime);
            Assert.Equal("block", loose.Kind);
            Assert.True(loose.KnowledgeGained > 0 && loose.KnowledgeGained < monumentGain,
                "Reading the inscriptions in place must be worth more than identifying the stone.");
        }
    }

    [Fact]
    public void ScanningRunes_AtAnotherArchetype_PaysAgain()
    {
        var server = Start("scan2", "rocky", 99, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Digger");
            server.SpawnMonumentForTest(new Vector3f(100.5f, 40f, 100.5f), "gate");
            server.SpawnMonumentForTest(new Vector3f(300.5f, 40f, 300.5f), "altar");

            p.State.Position = new Vector3f(100.5f, 40f, 100.5f);
            Assert.True(server.ScanSubject("Digger", "block", "rune_stone").FirstTime);

            p.State.Position = new Vector3f(300.5f, 40f, 300.5f);
            var second = server.ScanSubject("Digger", "block", "rune_stone");
            Assert.True(second.FirstTime, "A different silhouette is a different discovery.");
            Assert.Equal("monument_altar", second.SubjectKey);
        }
    }

    [Fact]
    public void PlacementGuard_SeesPlayerBuilds_ButNotWorldgenStamps()
    {
        var server = Start("guard", "rocky", 7, out var repo);
        using (repo)
        {
            // High in the air, far from spawn: the target cell is empty and nothing else has written there.
            const int bx = 900, by = 200, bz = 900;
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(bx, by, bz);
            p.State.Inventory.Add("stone", 1, 99);
            server.PlaceBlock("Builder", bx + 1, by, bz, "stone");
            Assert.False(server.World.GetBlock(new Vector3i(bx + 1, by, bz)).IsAir, "the test build must exist");

            Assert.True(server.FootprintHasPlayerEditsForTest(bx - 3, bz - 4, by, 9, 6, 9),
                "A footprint holding a player-placed block must be rejected (#527).");
            Assert.False(server.FootprintHasPlayerEditsForTest(bx + 400, bz + 400, by, 9, 6, 9),
                "Untouched ground must still be available.");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A repo file may still be locked on Windows — the temp dir is disposable either way.
        }
    }
}
