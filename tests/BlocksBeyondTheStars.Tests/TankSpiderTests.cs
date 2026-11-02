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
/// #3096 (Justus: "a tank spider — when you get too close its tank inflates and it explodes, without destroying blocks, only
/// with damage"): a player close by sets the tank swelling, and after the warning it bursts — hurting the players in reach,
/// less toward the edge, sparing those out of reach or behind a wall, and never changing a block. Shot in time it only
/// pops. It only spawns where the planet machines may, and never among the first machines of a visit.
/// </summary>
public sealed class TankSpiderTests : IDisposable
{
    private const int Half = 6; // the test floor reaches this far around the origin

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public TankSpiderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tankspider_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, RecordingTransport transport, Action<GameRules>? rules = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.Bandits = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        rules?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A flat stone floor around the origin with open air above it; returns the y a body stands at.</summary>
    private int Floor(SvGameServer server)
    {
        int sy = 64;
        for (int y = 220; y > 1; y--)
        {
            if (!server.World.GetBlock(new Vector3i(0, y, 0)).IsAir)
            {
                sy = y;
                break;
            }
        }

        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = -Half; x <= Half; x++)
        {
            for (int z = -Half; z <= Half; z++)
            {
                server.World.SetBlock(new Vector3i(x, sy, z), stone);
                for (int y = sy + 1; y <= sy + 8; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                }
            }
        }

        return sy + 1;
    }

    /// <summary>A player on foot at <paramref name="at"/>, full health.</summary>
    private static PlayerSession OnFoot(SvGameServer server, string name, Vector3f at)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = at;
        p.State.Health = 100f;
        return p;
    }

    /// <summary>One enemy pass after <paramref name="seconds"/> of server time.</summary>
    private static void After(SvGameServer server, double seconds)
    {
        server.AdvanceUptimeForTest(seconds);
        server.TickEnemiesForTest(0.05);
    }

    private BlockId[] Snapshot(SvGameServer server, int feet)
    {
        var cells = new List<BlockId>();
        for (int x = -Half; x <= Half; x++)
        {
            for (int z = -Half; z <= Half; z++)
            {
                for (int y = feet - 3; y <= feet + 6; y++)
                {
                    cells.Add(server.World.GetBlock(new Vector3i(x, y, z)));
                }
            }
        }

        return cells.ToArray();
    }

    [Fact]
    public void ThePlayerCloseBy_SetsTheTankSwelling_AndTheBurstHurtsThem_WithoutChangingABlock()
    {
        var transport = new RecordingTransport();
        var server = Started("tank_burst", transport);
        int feet = Floor(server);
        var p = OnFoot(server, "Justus", new Vector3f(2.5f, feet, 0.5f));
        var spider = server.SpawnTankSpiderAtForTest(new Vector3f(0.5f, feet, 0.5f));
        Assert.Equal(CombatEntityKind.TankSpider, spider.Kind);
        Assert.Equal(EnemyCatalog.TankSpider, spider.EnemyKey);
        Assert.Equal(_content.Enemy(EnemyCatalog.TankSpider).Tier(EnemyTiers.Normal).BurstDamage, spider.BurstDamage);
        var before = Snapshot(server, feet);
        transport.Sent.Clear();

        server.TickEnemiesForTest(0.05);
        Assert.True(spider.BurstAt > 0, "a player two blocks away sets the tank swelling");
        Assert.Contains(spider, server.PlanetEnemies);
        Assert.Equal(100f, p.State.Health); // the warning hurts nobody

        After(server, TankSpiderRules.InflateSeconds * 0.5);
        Assert.Contains(spider, server.PlanetEnemies); // still swelling
        Assert.Equal(100f, p.State.Health);

        After(server, TankSpiderRules.InflateSeconds);
        Assert.DoesNotContain(spider, server.PlanetEnemies); // it is gone with its burst
        Assert.True(p.State.Health < 100f, "the burst hurts the player in reach");
        Assert.True(p.State.Health > 100f - spider.BurstDamage - 0.01f, "never more than the burst at its centre");
        var fx = Assert.Single(transport.Sent.Where(s => s.Conn == p.ConnectionId).Select(s => s.Msg).OfType<WorldFx>(),
            m => m.Kind == TankSpiderRules.BurstFx);
        Assert.Equal(TankSpiderRules.BurstRadius, fx.Radius);
        Assert.Contains(transport.Sent.Select(s => s.Msg).OfType<PlanetEnemyDefeated>(), m => m.Id == spider.Id);

        // Damage only: not one block changed, and no block change went out.
        Assert.Equal(before, Snapshot(server, feet));
        Assert.Empty(transport.Sent.Select(s => s.Msg).OfType<BlockChanged>());
    }

    [Fact]
    public void APlayerWhoRanOutOfReach_IsNotHurt()
    {
        var server = Started("tank_run", new RecordingTransport());
        int feet = Floor(server);
        var p = OnFoot(server, "Justus", new Vector3f(2.5f, feet, 0.5f));
        var spider = server.SpawnTankSpiderAtForTest(new Vector3f(0.5f, feet, 0.5f));

        server.TickEnemiesForTest(0.05);
        Assert.True(spider.BurstAt > 0);
        p.State.Position = new Vector3f(0.5f + TankSpiderRules.BurstRadius + 1.5f, feet, 0.5f); // ran during the warning

        After(server, TankSpiderRules.InflateSeconds + 0.1);
        Assert.DoesNotContain(spider, server.PlanetEnemies);
        Assert.Equal(100f, p.State.Health);
    }

    [Fact]
    public void TheDamage_FallsOffTowardTheEdge_AndAPlayerFurtherOut_TakesLess()
    {
        Assert.Equal(30.0, TankSpiderRules.DamageAt(30f, 0f), 3);
        Assert.Equal(30.0 * TankSpiderRules.EdgeShare, TankSpiderRules.DamageAt(30f, TankSpiderRules.BurstRadius), 3);
        Assert.Equal(0f, TankSpiderRules.DamageAt(30f, TankSpiderRules.BurstRadius + 0.01f));

        // Two players in one burst: the one beside the tank takes more than the one near the edge.
        var server = Started("tank_falloff", new RecordingTransport());
        int feet = Floor(server);
        var near = OnFoot(server, "Near", new Vector3f(1.5f, feet, 0.5f));
        var far = OnFoot(server, "Far", new Vector3f(0.5f, feet, 3.7f));
        server.SpawnTankSpiderAtForTest(new Vector3f(0.5f, feet, 0.5f));

        server.TickEnemiesForTest(0.05);
        After(server, TankSpiderRules.InflateSeconds + 0.1);

        Assert.True(far.State.Health < 100f, "the edge of the burst still reaches");
        Assert.True(100f - near.State.Health > 100f - far.State.Health, "closer is worse");
    }

    [Fact]
    public void AWallBetweenTheTankAndThePlayer_IsCover()
    {
        var server = Started("tank_wall", new RecordingTransport());
        int feet = Floor(server);
        var p = OnFoot(server, "Justus", new Vector3f(2.5f, feet, 0.5f));
        var spider = server.SpawnTankSpiderAtForTest(new Vector3f(0.5f, feet, 0.5f));
        server.TickEnemiesForTest(0.05);
        Assert.True(spider.BurstAt > 0);

        // During the warning the player steps back one block and ducks behind a wall three blocks high — still well inside
        // the burst's reach (3 blocks from the tank), but out of its sight.
        p.State.Position = new Vector3f(3.5f, feet, 0.5f);
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int y = feet; y <= feet + 2; y++)
        {
            for (int z = -1; z <= 1; z++)
            {
                server.World.SetBlock(new Vector3i(2, y, z), stone);
            }
        }

        After(server, TankSpiderRules.InflateSeconds + 0.1);
        Assert.DoesNotContain(spider, server.PlanetEnemies);
        Assert.Equal(100f, p.State.Health);
    }

    [Fact]
    public void ShotBeforeItBursts_ItOnlyPops()
    {
        var transport = new RecordingTransport();
        var server = Started("tank_shot", transport);
        int feet = Floor(server);
        var p = OnFoot(server, "Justus", new Vector3f(2.5f, feet, 0.5f));
        p.State.SuitEnergy = 100f;
        p.State.Inventory.SetSlot(0, new ItemStack("laser_pistol", 1));
        p.State.SelectedHotbarSlot = 0;
        var spider = server.SpawnTankSpiderAtForTest(new Vector3f(0.5f, feet, 0.5f));
        server.TickEnemiesForTest(0.05);
        Assert.True(spider.BurstAt > 0);

        spider.Hull = 1f;
        server.AttackEntity("Justus", spider.Id);
        Assert.DoesNotContain(spider, server.PlanetEnemies);

        transport.Sent.Clear();
        After(server, TankSpiderRules.InflateSeconds + 0.1);
        Assert.Equal(100f, p.State.Health);
        Assert.DoesNotContain(transport.Sent.Select(s => s.Msg).OfType<WorldFx>(), m => m.Kind == TankSpiderRules.BurstFx);
        Assert.True(p.State.Inventory.CountOf("carbon") >= 2, "a spider shot down drops its loot");
    }

    [Fact]
    public void APlayerTheMachinesIgnore_NeverSetsItOff()
    {
        var server = Started("tank_god", new RecordingTransport());
        int feet = Floor(server);
        var p = OnFoot(server, "Justus", new Vector3f(2.5f, feet, 0.5f));
        p.State.GodMode = true;
        var spider = server.SpawnTankSpiderAtForTest(new Vector3f(0.5f, feet, 0.5f));

        for (int i = 0; i < 5; i++)
        {
            After(server, 0.5);
        }

        Assert.Equal(0.0, spider.BurstAt);
        Assert.Contains(spider, server.PlanetEnemies);
    }

    [Fact]
    public void WithPlanetMachinesOff_NoTankSpiderEverComes()
    {
        var server = Started("tank_off", new RecordingTransport(), r => r.PlanetEnemies = AlienActivity.Off);
        server.TankSpiderShareForTest = 1.0; // every refill that may be a spider would be one
        int feet = Floor(server);
        OnFoot(server, "Justus", new Vector3f(0.5f, feet, 0.5f));

        for (int i = 0; i < 240; i++)
        {
            server.TickEnemiesForTest(0.5);
        }

        Assert.Empty(server.PlanetEnemies);
    }

    [Fact]
    public void TheFirstMachinesOfAVisit_AreRobots_AndARefillMayBeOneTankSpiderAtATime()
    {
        var server = Started("tank_refill", new RecordingTransport(), r => r.PlanetDrones = false);
        server.TankSpiderShareForTest = 1.0;
        int feet = Floor(server);
        var p = OnFoot(server, "Justus", new Vector3f(0.5f, feet, 0.5f));
        p.State.GodMode = true; // the machines leave them be, so nothing wanders off or bursts mid-test

        // The quick start fills the solo Normal cap (2) — robots only.
        TickUntil(server, () => server.PlanetEnemies.Count >= 2);
        Assert.Equal(2, server.PlanetEnemies.Count);
        Assert.DoesNotContain(server.PlanetEnemies, e => e.Kind == CombatEntityKind.TankSpider);

        // A slot frees: the refill walks in as a tank spider.
        server.RemovePlanetEnemyForTest(server.PlanetEnemies[0].Id);
        TickUntil(server, () => server.PlanetEnemies.Count >= 2);
        var spider = Assert.Single(server.PlanetEnemies, e => e.Kind == CombatEntityKind.TankSpider);
        Assert.Equal(EnemyCatalog.TankSpider, spider.EnemyKey);

        // One at a time: while it is about, the next refill is a robot again.
        server.RemovePlanetEnemyForTest(server.PlanetEnemies.First(e => e.Kind != CombatEntityKind.TankSpider).Id);
        TickUntil(server, () => server.PlanetEnemies.Count >= 2);
        Assert.Single(server.PlanetEnemies, e => e.Kind == CombatEntityKind.TankSpider);
    }

    [Fact]
    public void TheRefillShare_IsAboutAThird_AndStablePerWorldAndOrdinal()
    {
        int spiders = 0;
        for (int ordinal = 0; ordinal < 400; ordinal++)
        {
            double roll = SvGameServer.TankSpiderRollForTest("rocky", ordinal, 9);
            Assert.Equal(roll, SvGameServer.TankSpiderRollForTest("rocky", ordinal, 9));
            if (TankSpiderRules.PicksSpider(roll))
            {
                spiders++;
            }
        }

        Assert.InRange(spiders, 100, 180); // RefillShare 0.35 of 400 = 140
    }

    private static void TickUntil(SvGameServer server, Func<bool> done)
    {
        for (int i = 0; i < 400 && !done(); i++)
        {
            server.AdvanceUptimeForTest(0.5);
            server.TickEnemiesForTest(0.5);
        }
    }
}
