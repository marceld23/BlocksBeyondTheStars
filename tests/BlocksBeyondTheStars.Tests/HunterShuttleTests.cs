// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3097 (Justus: "In space a hunter shuttle would be cool. It is bad at attacking in space, but when it lands and you find
/// it, there is a hunter and several robot spawners."): the weak shuttle skirmishes, runs when hurt and lands a camp on the
/// planet; the camp is stamped from existing blocks with its Guardian bays and a hunter on guard; the bays release robots only
/// while a player is near and never past their cap; mining every bay clears the camp for good; Peaceful never sees any of it.
/// </summary>
public sealed class HunterShuttleTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public HunterShuttleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_huntershuttle_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer Started(string world, RecordingTransport transport, Action<GameRules>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.PlanetEnemies = AlienActivity.Normal;
        config.Rules.PlanetDrones = false; // the bays release walking robots only — a fixed count to assert
        config.Rules.Bandits = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE;
        config.Rules.ShipWeapons = ShipWeaponMode.NpcsOnly;
        config.Rules.SpaceNpcEnemies = AlienActivity.Normal;
        config.Rules.AlienUfos = AlienActivity.Off;
        configure?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private string KeyAt(SvGameServer server, Vector3i cell) => _content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    /// <summary>A player on foot, and a camp stamped on dry, flat ground some way off from them (the start of this seed is
    /// an island — the plan goes where the spot search will take it).</summary>
    private static HunterShuttleSiteRecord StampedCamp(SvGameServer server, PlayerSession p)
    {
        int px = (int)p.State.Position.X, pz = (int)p.State.Position.Z;
        var spot = (from r in Enumerable.Range(3, 8)
                    from a in Enumerable.Range(0, 16)
                    let x = px + (int)Math.Round(Math.Cos(a * Math.PI / 8) * r * 16)
                    let z = pz + (int)Math.Round(Math.Sin(a * Math.PI / 8) * r * 16)
                    let spread = server.HunterSpotSpreadForTest(x, z)
                    where spread is >= 0 and <= 2
                    select (X: x, Z: z)).First();
        Assert.True(server.PlanHunterSiteForTest(spot.X, spot.Z));
        server.TickHunterSitesForTest(1.0); // someone is on the body: the camp lands
        var site = server.HunterSiteForTest(server.Metadata.ActiveLocationId)!;
        Assert.Equal(HunterShuttleSiteRecord.Active, site.State);
        return site;
    }

    /// <summary>Puts the player just behind the shuttle's open hatch — inside the camp's waking range.</summary>
    private static void StandAtTheCamp(PlayerSession p, HunterShuttleSiteRecord site)
        => p.State.Position = new Vector3f(site.X + 3.5f, site.Y + 1f, site.Z - 6.5f);

    private static void Advance(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 1.0)
        {
            server.TickHunterSitesForTest(1.0);
        }
    }

    // ---------------- in space ----------------

    [Fact]
    public void TheShuttle_IsWeak_RunsWhenHurt_AndLandsACampOnThePlanetItFledFrom()
    {
        var server = Started("flee", new RecordingTransport());
        var pilot = server.AddLocalPlayer("Pilot");
        pilot.State.AboardShip = true;
        string body = server.Metadata.ActiveLocationId;
        server.EnterSpace("Pilot");
        server.SpawnHunterShuttleForTest("Pilot");

        var shuttle = server.HunterShuttleForTest("Pilot");
        Assert.NotNull(shuttle);
        var tier = _content.Enemy(EnemyCatalog.HunterShuttle).Tier(EnemyTiers.Normal);
        Assert.Equal(EnemyCatalog.HunterShuttle, shuttle!.EnemyKey);
        Assert.Equal(tier.Hull, shuttle.HullMax);
        Assert.True(tier.DamagePerSecond <= 2f, "a poor fighter: its gun is the weakest in space");
        Assert.True(shuttle.Hostile);
        Assert.Equal(HunterShuttlePhase.Skirmish, shuttle.ShuttlePhase);

        shuttle.Hull = shuttle.HullMax * 0.4f; // hit hard
        server.TickHunterShuttlesForTest("Pilot", 0.1);
        Assert.Equal(HunterShuttlePhase.Fleeing, shuttle.ShuttlePhase);
        Assert.Equal(0f, shuttle.DamagePerSecond); // it stops shooting and runs

        for (int i = 0; i < 200 && server.HunterShuttleForTest("Pilot") is not null; i++)
        {
            server.TickHunterShuttlesForTest("Pilot", 0.1);
        }

        Assert.Null(server.HunterShuttleForTest("Pilot")); // jumped away
        var site = server.HunterSiteForTest(body);
        Assert.NotNull(site);
        Assert.Equal(HunterShuttleSiteRecord.Pending, site!.State); // stamped when someone stands on that body
    }

    [Fact]
    public void LeftAlone_ItGivesUpAfterAWhile_AndRunsAllTheSame()
    {
        var server = Started("patience", new RecordingTransport());
        server.AddLocalPlayer("Pilot").State.AboardShip = true;
        server.EnterSpace("Pilot");
        server.SpawnHunterShuttleForTest("Pilot");
        var shuttle = server.HunterShuttleForTest("Pilot")!;

        shuttle.ShuttleClock = shuttle.ShuttlePatience; // its patience ran out near the pilot
        server.TickHunterShuttlesForTest("Pilot", 0.1);

        Assert.Equal(HunterShuttlePhase.Fleeing, shuttle.ShuttlePhase);
        Assert.Equal(shuttle.HullMax, shuttle.Hull); // unhurt — it runs anyway
    }

    // ---------------- the camp ----------------

    [Fact]
    public void TheCamp_IsStampedFromExistingBlocks_WithTwoOrThreeBays_AndBroadcast()
    {
        var transport = new RecordingTransport();
        var server = Started("stamp", transport);
        var p = server.AddLocalPlayer("Walker");
        p.State.AboardShip = false;
        transport.Sent.Clear();

        var site = StampedCamp(server, p);

        Assert.InRange(site.Spawners.Count, 2, 3);
        foreach (var c in site.Spawners)
        {
            Assert.Equal(SvGameServer.GuardianBayBlockKey, KeyAt(server, new Vector3i(c.X, c.Y, c.Z)));
        }

        Assert.Equal("metal_panel", KeyAt(server, new Vector3i(site.X + 1, site.Y + 1, site.Z + 3))); // a flank
        Assert.Equal("light_red", KeyAt(server, new Vector3i(site.X + 3, site.Y + 1, site.Z + 9)));   // its sensor eye
        Assert.Equal("air", KeyAt(server, new Vector3i(site.X + 3, site.Y + 1, site.Z)));            // the open hatch
        var changes = transport.Sent.Select(s => s.Msg).OfType<BlockChanged>().ToList();
        Assert.Contains(changes, m => m.X == site.Spawners[0].X && m.Y == site.Spawners[0].Y && m.Z == site.Spawners[0].Z);
        Assert.Contains(transport.Sent.Select(s => s.Msg).OfType<PlanetPoiList>(),
            l => l.Pois.Any(poi => poi.Type == SvGameServer.HunterSitePoiType)); // marked on the map
    }

    [Fact]
    public void TheBays_ReleaseRobots_OnlyWhileAPlayerIsNear_AndNeverPastTheirCap()
    {
        var server = Started("docks", new RecordingTransport());
        var p = server.AddLocalPlayer("Walker");
        p.State.AboardShip = false;
        var site = StampedCamp(server, p);

        p.State.Position = new Vector3f(site.X + 300f, site.Y + 1f, site.Z + 300f); // far away
        Advance(server, 120);
        Assert.Empty(server.HunterDockRobotsForTest());
        Assert.Equal(string.Empty, server.HunterGuardIdForTest());

        StandAtTheCamp(p, site);
        Advance(server, 30); // the first releases come a few seconds after someone came near
        Assert.Equal(site.Spawners.Count, server.HunterDockRobotsForTest().Count); // one each on Normal
        var guard = server.PlanetEnemies.Single(e => e.Id == server.HunterGuardIdForTest());
        Assert.Equal(EnemyCatalog.Hunter, guard.EnemyKey); // the shuttle's hunter keeps watch

        Advance(server, 200);
        Assert.Equal(site.Spawners.Count, server.HunterDockRobotsForTest().Count); // the cap holds

        string first = server.HunterDockRobotsForTest()[0];
        Assert.True(server.RemovePlanetMachineForTest(first));
        Advance(server, 20);
        Assert.Equal(site.Spawners.Count - 1, server.HunterDockRobotsForTest().Count); // not replaced at once…
        Advance(server, 40);
        Assert.Equal(site.Spawners.Count, server.HunterDockRobotsForTest().Count); // …but after its interval
    }

    [Fact]
    public void ADefeatedGuard_NeverComesBack()
    {
        var server = Started("guard", new RecordingTransport());
        var p = server.AddLocalPlayer("Walker");
        p.State.AboardShip = false;
        var site = StampedCamp(server, p);
        StandAtTheCamp(p, site);
        Advance(server, 2);
        string guard = server.HunterGuardIdForTest();
        Assert.NotEqual(string.Empty, guard);

        Assert.True(server.RemovePlanetMachineForTest(guard)); // defeated in front of the player
        Advance(server, 60);

        Assert.True(site.GuardDefeated);
        Assert.Equal(string.Empty, server.HunterGuardIdForTest());
    }

    [Fact]
    public void MiningEveryBay_ClearsTheCamp_ForGood_WithLoot()
    {
        var server = Started("clear", new RecordingTransport());
        var p = server.AddLocalPlayer("Walker");
        p.State.AboardShip = false;
        string body = server.Metadata.ActiveLocationId;
        var site = StampedCamp(server, p);
        StandAtTheCamp(p, site);
        Advance(server, 2);

        foreach (var c in site.Spawners)
        {
            server.World.SetBlock(new Vector3i(c.X, c.Y, c.Z), BlockId.Air); // mined
        }

        Advance(server, 1);

        Assert.Equal(HunterShuttleSiteRecord.Cleared, site.State);
        Assert.Contains(server.Metadata.GeneratedLoot, k => k.StartsWith("hunter_shuttle:chest:", StringComparison.Ordinal));
        Assert.Contains(server.Containers, c => c.Id.StartsWith("loot_hunter_shuttle_chest_", StringComparison.Ordinal));
        var saved = _repos[^1].LoadMetadata()!;
        Assert.Equal(HunterShuttleSiteRecord.Cleared, saved.HunterShuttleSites[body].State); // persisted

        int machines = server.PlanetEnemies.Count;
        Advance(server, 120);
        Assert.Equal(machines, server.PlanetEnemies.Count); // a cleared camp sends nothing more
    }

    // ---------------- the gates ----------------

    [Fact]
    public void Peaceful_NeverSeesAShuttle_NorACamp()
    {
        var server = Started("peaceful", new RecordingTransport(), r => r.PlanetEnemies = AlienActivity.Off);
        server.AddLocalPlayer("Kid").State.AboardShip = true;
        server.EnterSpace("Kid");

        Assert.False(server.HunterShuttlesAllowedForTest);
        for (int i = 0; i < 50; i++)
        {
            Assert.False(server.RollHunterShuttleForTest("Kid"));
        }

        Assert.False(server.PlanHunterSiteForTest(0, 0)); // nothing lands either
        Assert.Empty(server.Metadata.HunterShuttleSites);
    }

    [Fact]
    public void SpaceHostilesOff_OrNoWayToShootBack_KeepsItAway()
    {
        Assert.False(Started("nospace", new RecordingTransport(), r => r.SpaceNpcEnemies = AlienActivity.Off).HunterShuttlesAllowedForTest);
        Assert.False(Started("noguns", new RecordingTransport(), r => r.ShipWeapons = ShipWeaponMode.MiningOnly).HunterShuttlesAllowedForTest);
        Assert.False(Started("creative", new RecordingTransport(), r => r.GameMode = GameMode.Creative).HunterShuttlesAllowedForTest);
    }

    [Fact]
    public void WhereItIsAllowed_TheRollSometimesBringsOne()
    {
        var server = Started("roll", new RecordingTransport(), r => r.SpaceNpcEnemies = AlienActivity.Extreme);
        server.AddLocalPlayer("Pilot").State.AboardShip = true;
        server.EnterSpace("Pilot");

        Assert.True(server.HunterShuttlesAllowedForTest);
        int comes = 0;
        for (int i = 0; i < 60; i++)
        {
            comes += server.RollHunterShuttleForTest("Pilot") ? 1 : 0;
        }

        Assert.InRange(comes, 1, 59); // a chance, never a certainty
    }

    [Fact]
    public void TheHull_IsMadeOfExistingBlocksOnly()
    {
        foreach (var key in new[] { "metal_panel", "steel_floor", "glass_clear", "glass", "light_red", "engine_nozzle", SvGameServer.GuardianBayBlockKey })
        {
            Assert.NotNull(_content.GetBlock(key));
        }

        Assert.True(_content.GetBlock("metal_panel")!.Tintable);
        Assert.True(_content.GetBlock("steel_floor")!.Tintable);
    }
}
