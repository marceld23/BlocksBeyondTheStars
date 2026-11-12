// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The school club's Soaniri landmarks and their two friends (terrain generation 39): the castle of Soaniri (a ruin full of
/// Soundis) and the singer statue (intact, protected) stand on every generation-39 Soaniri as its two FIXED landmarks, clear of
/// the village, the ruin city and the Choir Tower and of each other; the Soundi statue (every world with air) and Blocks King
/// (inhabited worlds; its keeper sells burgers to customers in the closed order room) stand intact and protected. The checks
/// every landmark shares — loading, cell budget, the ruin per seed, the overlay — are rows of <see cref="SchoolClubLandmarkTests"/>.
/// </summary>
public sealed class SchoolClubSoaniriLandmarkTests : IDisposable
{
    private const string Castle = "soaniri_castle";
    private const string Singer = "singer_statue";
    private const string SoundiStatue = "soundi_statue";
    private const string BlocksKing = "blocks_king";
    private const string Soaniri = "sounder_world";

    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_club_soaniri_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public SchoolClubSoaniriLandmarkTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        _repos.ForEach(r => r.Dispose());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    private static StructureTemplate Template(string key)
        => Shipped.Value.Landmarks.SingleOrDefault(l => l.Key == key) ?? throw new Xunit.Sdk.XunitException($"no landmark {key}");

    private static Dictionary<string, string> Settings(SettlementMarkerLike m) => m.Data.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : string.Empty, StringComparer.Ordinal);

    private readonly record struct SettlementMarkerLike(string Type, string Data);

    private static List<SettlementMarkerLike> Markers(StructureTemplate t)
        => t.Cells.Where(c => c.Kind == "marker").Select(c => new SettlementMarkerLike(c.Id, c.Config)).ToList();

    // ---------------------------------------------------------------- the templates

    /// <summary>Gated to generation 39, the Soaniri pair fixed there and named for nothing else, the figures and the restaurant
    /// intact and protected, the markers as the spec lists them (Soundi dens and calls in the castle, the swelling choir at the
    /// statue, the burger keeper who sells inside), their sounds bundled and their loot tables known.</summary>
    [Fact]
    public void TheTemplates_AreGatedTo39_FixedOnSoaniriOrRolled_AndCarryTheirMarkers()
    {
        var content = Shipped.Value;
        foreach (string key in new[] { Castle, Singer, SoundiStatue, BlocksKing })
        {
            Assert.Equal(WorldDescription.SchoolClubLandmarksGeneration, Template(key).MinTerrainGeneration);
        }

        Assert.Equal(new[] { Castle, Singer }, content.GetPlanet(Soaniri)!.FixedLandmarks);
        Assert.Equal(new[] { (0, Castle), (1, Singer) }, content.FixedLandmarksFor(content.GetPlanet(Soaniri)).Select(f => (f.Index, f.Template.Key)));
        foreach (string key in new[] { Castle, Singer })
        {
            Assert.Equal(new[] { Soaniri }, Template(key).PlanetTypes); // never rolled anywhere else
        }

        var castle = Template(Castle);
        Assert.True(LandmarkRules.IsRuin(castle));
        Assert.Equal(LandmarkRules.ProfileMasonry, LandmarkRules.ProfileOf(castle));
        var dens = Markers(castle).Where(m => m.Type == "den").ToList();
        Assert.True(dens.Count >= 3, "the castle is home to several Soundi packs");
        Assert.All(dens, d => Assert.Equal("soundi", Settings(d)["species"]));
        Assert.Contains(Markers(castle), m => m.Type == "sound" && Settings(m)["sound"] == "amb_soaniri_castle");

        foreach (string key in new[] { Singer, SoundiStatue, BlocksKing })
        {
            var t = Template(key);
            Assert.False(LandmarkRules.IsRuin(t), key);
            Assert.Equal(LandmarkRules.ProtectionProtected, LandmarkRules.ProtectionOf(t));
        }

        var choir = Assert.Single(Markers(Template(Singer)), m => m.Type == "sound");
        Assert.Equal(("amb_singer_choir", "120"), (Settings(choir)["sound"], Settings(choir)["radius"]));
        Assert.Empty(Template(SoundiStatue).PlanetTypes);
        Assert.Equal(new[] { LandmarkRules.RequiresAtmosphere }, Template(SoundiStatue).Requires);
        Assert.Contains("inhabited", Template(BlocksKing).Requires);
        var keeper = Assert.Single(Markers(Template(BlocksKing)), m => m.Type == "vendor");
        Assert.Equal(("burgers", "1"), (Settings(keeper)["theme"], Settings(keeper)["inside"]));
        Assert.Contains(content.Recipes.Values, r => r.MarketTheme == "burgers");

        string audio = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");
        foreach (string clip in new[] { "amb_soaniri_castle", "amb_singer_choir" })
        {
            Assert.True(File.Exists(Path.Combine(audio, clip + ".mp3")), $"the loop {clip} is not bundled");
        }

        foreach (var m in new[] { Castle, Singer, SoundiStatue, BlocksKing }.SelectMany(k => Markers(Template(k))).Where(m => m.Type == "chest"))
        {
            if (Settings(m).TryGetValue("table", out string? table))
            {
                Assert.NotNull(content.LootTable(table));
            }
        }
    }

    // ---------------------------------------------------------------- on a world

    private SvGameServer Start(GameContent content, string name, string planet, long seed)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            LandmarkChance = 1.0,
        };
        config.World.TerrainGeneration = WorldDescription.SchoolClubLandmarksGeneration;
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private void Stop(SvGameServer server)
    {
        server.Stop();
        _repos[^1].Dispose();
        _repos.RemoveAt(_repos.Count - 1);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    private static Vector3i World(SvGameServer server, SvGameServer.LandmarkInstance lm, Vector3i local)
        => WorldConstants.CanonicalBlock(lm.Cell(local), server.World.Circumference);

    /// <summary>The first block of the landmark two rows above its ground row, scanning its footprint.</summary>
    private static Vector3i SomeBlock(SvGameServer.LandmarkInstance lm)
    {
        var s = lm.Structure;
        return Enumerable.Range(0, s.Width * s.Length)
            .Select(i => new Vector3i(i / s.Length, lm.Layout.GroundRow + 2, i % s.Length))
            .First(p => s.Get(p.X, p.Y, p.Z) != 0);
    }

    /// <summary>
    /// A fresh generation-39 Soaniri carries both fixed landmarks under records 0 and 1 — the castle a ruin and open, the statue
    /// intact and protected — each one overlay without a block edit, clear of the three Sounder sites and of each other (no two
    /// boxes of the footprint registry overlap), and a reload replays both where they stood.
    /// </summary>
    [Theory]
    [Trait("Category", "Slow")] // two world boots per seed and two giants stamped
    [InlineData(20261112L)]
    [InlineData(7L)]
    public void EveryGeneration39Soaniri_CarriesTheCastleAndTheSingerStatue_ClearOfItsSitesAndOfEachOther(long seed)
    {
        string name = "club_soaniri_" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var server = Start(Shipped.Value, name, Soaniri, seed);
        Assert.Equal(new[] { Castle, Singer }, server.LandmarksForTest.Select(l => l.Key));
        var records = server.PlacementRecordsForTest.Where(r => r.Kind == LandmarkRules.RecordKind).OrderBy(r => r.Index).ToList();
        Assert.Equal(new[] { (0, Castle, true), (1, Singer, true) }, records.Select(r => (r.Index, r.Template, r.Placed)));

        var castle = server.LandmarksForTest[0];
        var singer = server.LandmarksForTest[1];
        Assert.True(castle.Layout.Ruined);
        Assert.False(singer.Layout.Ruined);
        Assert.False(server.IsProtectedStructureCellForTest(World(server, castle, SomeBlock(castle))));
        Assert.True(server.IsProtectedStructureCellForTest(World(server, singer, SomeBlock(singer))));

        Assert.Equal(3, server.SounderSitesForTest().Count);
        var boxes = server.FootprintsForTest;
        Assert.Equal(2, boxes.Count(f => f.Kind == StructureTypeRules.Landmark));
        Assert.Equal(3, boxes.Count(f => f.Kind == StructureTypeRules.SounderSite));
        var collisions = server.FootprintCollisionsForTest();
        Assert.True(collisions.Count == 0, "footprints overlap: " + string.Join("; ", collisions.Select(c => $"{c.A.Kind}@{c.A.Cx},{c.A.Cz} × {c.B.Kind}@{c.B.Cx},{c.B.Cz}")));

        var repo = _repos[^1];
        string location = server.World.LocationId;
        foreach (var (key, budget) in new[] { ("landmark:0", 256 * 1024), ("landmark:1", 96 * 1024) })
        {
            var row = Assert.Single(repo.ListStructureOverlays(location), r => r.Key == key);
            Assert.True(StructureOverlay.TryDecode(row.Data, out var overlay));
            var cells = new HashSet<Vector3i>(overlay.Cells().Select(c => c.Cell));
            Assert.Empty(overlay.Chunks.SelectMany(c => repo.LoadChunkEdits(location, c.Coord)).Where(e => cells.Contains(e.WorldPosition)));
            _output.WriteLine($"{key}: {overlay.CellCount} cells in {overlay.Chunks.Count} chunks, {row.Data.Length} bytes");
            Assert.True(row.Data.Length <= budget, $"{key}: an overlay of {row.Data.Length} bytes, the budget is {budget}");
        }

        _output.WriteLine($"seed {seed}: castle at {castle.Origin} (turns {castle.Layout.Turns}), statue at {singer.Origin} (turns {singer.Layout.Turns}); " +
                          "sites " + string.Join(", ", server.SounderSitesForTest().Select(s => $"{s.Key}@{s.X},{s.Z}")));
        var origins = server.LandmarksForTest.Select(l => (l.Key, l.Origin, l.Layout.Turns)).ToList();
        Stop(server);

        var again = Start(Shipped.Value, name, Soaniri, seed);
        Assert.Equal(origins, again.LandmarksForTest.Select(l => (l.Key, l.Origin, l.Layout.Turns)));
        Stop(again);
    }

    /// <summary>
    /// A crash cut the boot stamp short after the castle: the statue has no record, no feature mark and no overlay. The next
    /// load replays the castle and stamps the missing statue — both stand again under records 0 and 1.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")] // two world boots and two giants stamped
    public void AFixedLandmark_ACrashLeftUnrecorded_IsStampedOnTheNextLoad()
    {
        const string name = "club_soaniri_crash";
        var server = Start(Shipped.Value, name, Soaniri, 7L);
        string location = server.World.LocationId;
        Assert.Equal(new[] { Castle, Singer }, server.LandmarksForTest.Select(l => l.Key));
        Stop(server);

        using (var edit = new SqliteWorldRepository(new SaveGamePaths(_root, name)))
        {
            edit.Initialize();
            var meta = edit.LoadMetadata()!;
            Assert.Equal(1, meta.Placements.RemoveAll(r => r.LocationId == location && r.Kind == LandmarkRules.RecordKind && r.Index == 1));
            Assert.Equal(1, meta.StampedFeatures.RemoveAll(f => f == location + "|landmark:1"));
            edit.SaveMetadata(meta);
            edit.DeleteStructureOverlay(location, "landmark:1");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var again = Start(Shipped.Value, name, Soaniri, 7L);
        Assert.Equal(new[] { Castle, Singer }, again.LandmarksForTest.Select(l => l.Key));
        var records = again.PlacementRecordsForTest.Where(r => r.Kind == LandmarkRules.RecordKind).OrderBy(r => r.Index).ToList();
        Assert.Equal(new[] { (0, Castle, true), (1, Singer, true) }, records.Select(r => (r.Index, r.Template, r.Placed)));
        Assert.Single(_repos[^1].ListStructureOverlays(location), r => r.Key == "landmark:1");
        Stop(again);
    }

    /// <summary>
    /// Blocks King on a generation-39 world (the inhabited condition lifted for the test world): seated intact and protected, its
    /// keeper sells burgers and only inside, and the order room in front of the counter is one closed room with the keeper's post
    /// (the closed-room check reaches only 24 blocks and 4096 cells — the dining halls would be far too big); the street
    /// outside is not.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")] // a world boot and a stamp
    public void BlocksKing_StandsIntactAndProtected_ItsKeeperSellsBurgersInTheClosedOrderRoom()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var t = content.Landmarks.Single(l => l.Key == BlocksKing);
        t.Requires = new List<string> { LandmarkRules.RequiresAtmosphere };
        content.SetLandmarks(new[] { t });
        var server = Start(content, "club_blocks_king", "meadowlands", 20261112L);
        var lm = Assert.Single(server.LandmarksForTest);
        Assert.False(lm.Layout.Ruined);
        Assert.True(server.IsProtectedStructureCellForTest(World(server, lm, SomeBlock(lm))));

        var keeper = Assert.Single(server.LandmarkResidentsForTest(BlocksKing), r => r.Role == "vendor");
        Assert.Equal(("burgers", true), (keeper.Theme, keeper.InsideOnly));
        Assert.Equal(2, server.LandmarkResidentsForTest(BlocksKing).Count(r => r.Role != "vendor"));
        var overlay = Assert.Single(_repos[^1].ListStructureOverlays(server.World.LocationId), r => r.Key == "landmark:0");
        _output.WriteLine($"{BlocksKing}: an overlay of {overlay.Data.Length} bytes");
        Assert.True(overlay.Data.Length <= 48 * 1024, $"{BlocksKing}: an overlay of {overlay.Data.Length} bytes");

        var post = World(server, lm, lm.Structure.Markers.Single(m => m.Type == "vendor").LocalPos);
        for (int x = -28; x <= 28; x += 4)
        {
            for (int z = -28; z <= 28; z += 4)
            {
                for (int y = -8; y <= 28; y += 4)
                {
                    server.World.GetBlock(new Vector3i(post.X + x, post.Y + y, post.Z + z)); // load the chunks round the shop
                }
            }
        }

        var keeperAt = new Vector3f(post.X + 0.5f, post.Y, post.Z + 0.5f);
        var customers = new[] { (4, 0), (-4, 0), (0, 4), (0, -4), (5, 0), (-5, 0), (0, 5), (0, -5) }
            .Select(d => new Vector3f(post.X + d.Item1 + 0.5f, post.Y, post.Z + d.Item2 + 0.5f))
            .Where(p => server.InSameClosedRoomForTest(p, keeperAt)).ToList();
        Assert.True(customers.Count > 0, "no spot in front of the counter is in the keeper's closed room");
        var street = World(server, lm, new Vector3i(1, lm.Layout.GroundRow + 1, 1));
        Assert.False(server.InSameClosedRoomForTest(new Vector3f(street.X + 0.5f, street.Y, street.Z + 0.5f), keeperAt));
        Stop(server);
    }
}
