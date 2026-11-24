// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Sol's Mars (#4018): nothing anybody built stands there — its structure whitelist keeps only the net fragments, on every
/// save — and its ground is the red planet's: red sand (the regolith) over red rock, the polar caps of ice, the Tharsis
/// heights basalt.
/// </summary>
public sealed class MarsTests : IDisposable
{
    private const string MarsType = "sol_mars";
    private const string MarsBody = "sol-mars";
    private const int MarsCircumference = 8512;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_mars_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public MarsTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private static PlanetType Mars => Content.GetPlanet(MarsType)!;

    [Fact]
    public void Mars_KeepsOnlyTheNetFragments_OfEveryStampedKind_AndNoRuinProps()
    {
        var mars = Mars;
        Assert.True(mars.RestrictStructures);
        Assert.Equal(new[] { "net_fragments" }, mars.AllowedStructures);

        // Every stamped kind but the net fragments is off on Mars: no G.D.S. ruin, rune monument, fallen town, rail ruin, camp,
        // factory, wreck, giant wreck, vault, landmark, guide tower or fallen giant (the tablets and the Sounder sites follow
        // their own planet type and stand nowhere else).
        var rules = Content.StructureTypes;
        var allowed = StructureTypeRules.Kinds.Where(k => StructureTypeRules.AllowedOn(rules.Get(k), mars, restricted: true)).ToList();
        Assert.Equal(new[] { StructureTypeRules.NetFragment }, allowed);

        // The chunk generation's rune shrines need "monuments", its lone rune stones "rune_stones" — neither is listed.
        Assert.DoesNotContain("monuments", mars.AllowedStructures);
        Assert.DoesNotContain(StructureTypeRules.RuneStonesWhitelist, mars.AllowedStructures);
    }

    [Fact]
    public void Mars_IsRedSandOverRedRock_WithIceCapsAndTharsisBasalt()
    {
        var mars = Mars;
        Assert.Equal(("red_sand", "red_rock"), (mars.SurfaceBlock, mars.SubSurfaceBlock));
        Assert.Equal(PlanetMapBiome.Count, mars.Biomes.Count);
        Assert.Equal("ice", mars.Biomes[PlanetMapBiome.Ice].SurfaceBlock); // the polar caps
        Assert.Equal("basalt", mars.Biomes[^1].SurfaceBlock);              // the Tharsis heights
        string[] desert = { "sand", "sandstone", "dark_sandstone" };
        Assert.DoesNotContain(mars.Biomes, b => desert.Contains(b.SurfaceBlock) || desert.Contains(b.SubSurfaceBlock));

        // The regolith digs by hand and settles like sand; the red rock drills like stone. Each drops itself.
        var sand = Content.GetBlock("sand")!;
        var stone = Content.GetBlock("stone")!;
        var redSand = Content.GetBlock("red_sand")!;
        var redRock = Content.GetBlock("red_rock")!;
        Assert.Equal((true, sand.RequiredTool, sand.MinToolTier), (redSand.Granular, redSand.RequiredTool, redSand.MinToolTier));
        Assert.Equal((false, stone.RequiredTool, stone.MinToolTier), (redRock.Granular, redRock.RequiredTool, redRock.MinToolTier));
        foreach (var block in new[] { redSand, redRock })
        {
            Assert.Equal("terrain", block.Category);
            Assert.Equal(block.Key, Assert.Single(block.Drops).Item);
            Assert.Equal(block.Key, Content.GetItem(block.Key)!.PlacesBlock);
        }
    }

    /// <summary>The ground of the real Mars map away from the poles is red: no yellow sand, and no altitude ice — at −40 °C
    /// the altitude climate froze every column before <see cref="PlanetType.AltitudeSnow"/> was off.</summary>
    [Fact]
    public void OnTheRealMarsMap_TheGroundIsMostlyRed()
    {
        var gen = new WorldGenerator(20261124, Content);
        gen.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);
        gen.SetWorldMode(MarsCircumference, cratered: false, landingPads: null, locationId: MarsBody);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int samples = 0;
        for (int x = 0; x < MarsCircumference; x += 397) // across every longitude, near the equator (no polar ice)
        {
            foreach (int z in new[] { -300, 0, 300 })
            {
                int y = gen.SurfaceHeight(Mars, x, z);
                var chunk = gen.Generate(Mars, WorldConstants.WorldToChunk(new Vector3i(x, y, z)));
                int cs = WorldConstants.ChunkSize;
                string key = Content.BlockById(chunk.Get(Mod(x, cs), Mod(y, cs), Mod(z, cs)))?.Key ?? "air"; counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                samples++;
            }
        }

        _output.WriteLine(string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
        int red = counts.Where(kv => kv.Key is "red_sand" or "red_rock").Sum(kv => kv.Value);
        Assert.True(red * 100 >= samples * 60, $"only {red} of {samples} surface columns are red");
        Assert.False(counts.ContainsKey("sand"), "yellow sand on Mars");
        Assert.False(counts.ContainsKey("ice"), "altitude ice on Mars");
        Assert.False(Mars.AltitudeSnow);
    }

    private static int Mod(int v, int m) => ((v % m) + m) % m;

    /// <summary>A world's first landing on Mars (Sol revealed on its first boot) stamps none of the rolled kinds but the net
    /// fragments.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void Mars_FirstVisit_StampsNothingButTheNetFragments()
    {
        var config = new ServerConfig
        {
            WorldName = "mars",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true },
        };
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "mars"));
        _repos.Add(repo);
        var server = new SvGameServer(config, Content, new RecordingTransport(), repo);
        server.Start();
        var admin = server.AddLocalPlayer("Ada");
        admin.State.Role = PlayerRole.WorldAdmin;
        server.HandleForTest(admin, new AdminCommandIntent { Command = "reveal_system", StringArg = "sol" });

        var clock = Stopwatch.StartNew();
        Assert.True(server.VisitBodyForTest(MarsBody));
        _output.WriteLine($"first boot of {MarsBody} took {clock.ElapsedMilliseconds} ms");
        Assert.Equal(MarsCircumference, server.World.Circumference);

        var kinds = server.PlacementRecordsForTest.Where(r => r.Placed && r.LocationId == MarsBody).Select(r => r.Kind).Distinct().ToList();
        _output.WriteLine("placed kinds on Mars: " + string.Join(", ", kinds));
        _output.WriteLine("stamp report: " + string.Join(", ", server.StampReportForTest.Select(r => $"{r.Kind} {r.Placed}/{r.Requested}")));
        Assert.All(kinds, k => Assert.Equal(StructureTypeRules.NetFragment, k));
        Assert.Empty(server.LandmarksForTest);
        server.Stop();
    }
}
