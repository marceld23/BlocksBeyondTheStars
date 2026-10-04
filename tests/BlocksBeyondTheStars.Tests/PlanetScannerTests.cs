// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The ship's planet scanner (#2140 — Bloody Mary's "Ressourcenscan"): a module researched and built like the others;
/// fitted, it surveys the bodies of the current star system — their ore veins, the world's richness and the extras —
/// from the same rolls the terrain generator uses. Also the VEGA nudge toward the terrain scanner (#2139).
/// </summary>
public sealed class PlanetScannerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlanetScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_planetscan_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Aboard(SvGameServer server, bool fitted)
    {
        var p = server.AddLocalPlayer("Surveyor");
        p.State.AboardShip = true;
        if (fitted && !server.Ship.HasModule("planet_scanner"))
        {
            server.Ship.Modules.Add("planet_scanner");
        }

        return p;
    }

    [Fact]
    public void Content_TheModuleIsResearchedThenBuilt_FromEarlyMaterials()
    {
        var module = Assert.Single(_content.ShipModules.Values, m => m.Key == "planet_scanner");
        Assert.Equal("planet_scanner", module.RequiredBlueprint);
        Assert.True(module.Removable);
        var bp = _content.Blueprints["planet_scanner"];
        Assert.Empty(bp.Prerequisites);

        // Reachable before titanium: the scan answers "where do I find what" — it must not need the rare metal it helps find.
        Assert.DoesNotContain(module.BuildCost, c => c.Item.Contains("titanium", StringComparison.Ordinal));
        Assert.DoesNotContain(bp.UnlockCost, c => c.Item.Contains("titanium", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_WithoutTheModule_GivesTheOverview_ButNoResources()
    {
        // #2239/#2240: every ship scans with its cockpit (tier 1) — the overview card, not the ore veins; those need
        // this module (now the ship scanner's tier 2, the Deep scanner). It used to refuse the whole scan.
        var server = Started("noscanner", out var repo);
        using (repo)
        {
            Aboard(server, fitted: false);
            var report = server.PlanetScanForTest("Surveyor", string.Empty, out string reason);
            Assert.NotNull(report);
            Assert.Equal(string.Empty, reason);
            Assert.Equal(1, report!.Tier);
            Assert.True(report.ResourcesLocked);
            Assert.Empty(report.Ores);
            Assert.NotEmpty(report.Rows);
        }
    }

    [Fact]
    public void Scan_OnFootOffTheShip_IsRejected()
    {
        var server = Started("onfoot", out var repo);
        using (repo)
        {
            var p = Aboard(server, fitted: true);
            p.State.AboardShip = false;
            Assert.Null(server.PlanetScanForTest("Surveyor", string.Empty, out string reason));
            Assert.Equal("@srv.planetscan.aboard", reason);
        }
    }

    [Fact]
    public void Scan_OfTheBodyHere_ListsItsPlanetTypesVeins_RichestFirst()
    {
        var server = Started("here", out var repo);
        using (repo)
        {
            var p = Aboard(server, fitted: true);
            var scan = server.PlanetScanForTest("Surveyor", string.Empty, out string reason);
            Assert.True(scan != null, reason);

            var body = server.Galaxy.FindBody(scan!.BodyId);
            Assert.NotNull(body);
            Assert.Equal(body!.PlanetType, scan.PlanetType);
            var planet = _content.Planets[scan.PlanetType];
            Assert.Equal(planet.Ores.Select(o => o.Block).OrderBy(b => b), scan.Ores.Select(o => o.Block).OrderBy(b => b));
            Assert.Equal(scan.Ores.OrderByDescending(o => o.Abundance).Select(o => o.Abundance), scan.Ores.Select(o => o.Abundance));
            foreach (var ore in scan.Ores)
            {
                var vein = planet.Ores.Single(o => o.Block == ore.Block);
                Assert.Equal(vein.MinDepth, ore.MinDepth);
                Assert.Equal(vein.RareTier, ore.RareTier);
            }

            Assert.InRange(scan.Richness, (byte)0, (byte)2);
        }
    }

    [Fact]
    public void Scan_ReachesTheBodiesOfThisSystem_NotOthers_AndNeverAStation()
    {
        var server = Started("range", out var repo);
        using (repo)
        {
            Aboard(server, fitted: true);
            var here = server.PlanetScanForTest("Surveyor", string.Empty, out _);
            Assert.NotNull(here);
            var system = server.Galaxy.FindBody(here!.BodyId)!.SystemId;

            var neighbour = server.Galaxy.AllBodies().FirstOrDefault(b => b.SystemId == system && b.Id != here.BodyId
                && !string.IsNullOrEmpty(b.PlanetType) && _content.Planets.TryGetValue(b.PlanetType!, out var t) && !t.Void);
            if (neighbour != null)
            {
                Assert.NotNull(server.PlanetScanForTest("Surveyor", neighbour.Id, out string ok));
            }

            var elsewhere = server.Galaxy.AllBodies().First(b => b.SystemId != system && !string.IsNullOrEmpty(b.PlanetType));
            Assert.Null(server.PlanetScanForTest("Surveyor", elsewhere.Id, out string far));
            Assert.Equal("@srv.planetscan.out_of_range", far);

            var station = server.Galaxy.AllBodies().FirstOrDefault(b => b.SystemId == system && b.Kind == CelestialKind.SpaceStation);
            if (station != null)
            {
                Assert.Null(server.PlanetScanForTest("Surveyor", station.Id, out string none));
                Assert.Equal("@srv.planetscan.no_surface", none);
            }
        }
    }

    [Fact]
    public void Scan_OverTheWire_AnswersWithAReport()
    {
        // The intent + reply are registered protocol messages (a missing Register silently drops them in the game).
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "wire"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = "wire", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.Sessions[1].State.AboardShip = true;
        server.Ship.Modules.Add("planet_scanner");

        PlanetScanResult? report = null;
        client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is PlanetScanResult r) report = r; };
        client.Send(NetCodec.Encode(new PlanetScanIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.NotNull(report);
        Assert.NotEmpty(report!.Ores);
    }

    [Fact]
    public void Survey_IsDeterministic_AndEveryWorldHasItsOwnRichness()
    {
        var planet = _content.Planets["varied"];
        var gen = new WorldGenerator(42, _content);
        gen.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);

        var a = gen.SurveyResources(planet, "sys0-p1", 1.0, cratered: false);
        var again = new WorldGenerator(42, _content);
        again.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);
        var b = again.SurveyResources(planet, "sys0-p1", 1.0, cratered: false);
        Assert.Equal(a.Richness, b.Richness);
        Assert.Equal(a.Veins.Select(v => (v.Block, v.Density)), b.Veins.Select(v => (v.Block, v.Density)));

        // The per-world roll is 1.2..2.2 × the world ore option (1.0 here); two bodies of one type differ.
        Assert.InRange(a.Richness, 1.2, 2.2);
        var richness = Enumerable.Range(1, 8).Select(i => gen.SurveyResources(planet, "sys0-p" + i, 1.0, false).Richness).Distinct().Count();
        Assert.True(richness > 1, "every body should roll its own ore richness");

        // A vein's density is the terrain's own number: type rarity × world richness (no frontier boost at 1.0).
        foreach (var v in a.Veins)
        {
            var vein = planet.Ores.Single(o => o.Block == v.Block);
            Assert.Equal(vein.Rarity * a.Richness, v.Density, 12);
        }

        // Oil lies on living worlds from generation 18; an airless cratered body carries crater metals, no oil.
        Assert.Equal(planet.HasLife, a.OilPockets);
        var cratered = gen.SurveyResources(planet, "sys0-p1-m1", 1.0, cratered: true);
        Assert.True(cratered.CraterMetals);
        Assert.False(cratered.OilPockets);
    }

    [Fact]
    public void Vega_PointsToTheTerrainScanner_OnlyUntilItsBlueprintIsKnown()
    {
        var server = Started("vega", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Digger");
            p.State.AboardShip = false;
            p.VegaMineRecent = 6.0; // has been digging a lot

            Assert.Contains("scanner_unknown", server.VegaTipCandidatesForTest("Digger").Candidates);

            p.State.UnlockedBlueprints.Add("terrain_scanner");
            Assert.DoesNotContain("scanner_unknown", server.VegaTipCandidatesForTest("Digger").Candidates);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
