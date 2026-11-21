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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Space fights flown together (#3903): a downed ship sends only its own crew home while another pilot still flies in
/// the instance (a pilot alone keeps the old outcome, instance and all), and a boss pays every pilot who hurt it — the
/// loot, the kill credit and the event — while an ordinary hostile still pays the last shot.
/// </summary>
public sealed class SpaceBossMultiplayerTests : IDisposable
{
    private const string Alice = "Alice";
    private const string Bob = "Bob";
    private const string Carol = "Carol";
    private const string Cannon = "ship_cannon_1";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly List<SvGameServer> _servers = new();

    public SpaceBossMultiplayerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bossmp_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Stop();
        }

        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // a temp folder the OS still holds — the next run uses a new one
        }
    }

    /// <summary>A server with the named pilots flying armed in one instance, far apart (a test moves them where it needs
    /// them): PvE on, ship weapons allowed, no ambient hostiles, no story.</summary>
    private (SvGameServer Server, RecordingTransport Transport, Dictionary<string, PlayerSession> Pilots) Flying(string name,
        params string[] pilots)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = name, Seed = 1, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE;
        config.Rules.ShipWeapons = ShipWeaponMode.NpcsOnly;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        config.Rules.Bandits = AlienActivity.Off;
        config.Rules.StoryId = StoryRegistry.NoneStoryId;
        var transport = new RecordingTransport();
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _servers.Add(server);
        var sessions = new Dictionary<string, PlayerSession>();
        for (int i = 0; i < pilots.Length; i++)
        {
            var s = server.AddLocalPlayer(pilots[i]);
            s.Ships[s.ActiveShipId].Modules.Add(Cannon);
            server.EnterSpace(pilots[i]);
            Assert.True(server.InSpace(pilots[i]));
            server.ShipMove(pilots[i], 0f, 0f, i * 600f);
            sessions[pilots[i]] = s;
        }

        Assert.Single(pilots.Select(p => server.SpaceInstanceIdForTest(p)).Distinct()); // one flight for all
        return (server, transport, sessions);
    }

    private static void Run(SvGameServer server, double seconds, double step = 0.1)
    {
        for (double t = 0; t < seconds - 1e-9; t += step)
        {
            server.Tick(step);
        }
    }

    private static ShipState ShipOf(PlayerSession s) => s.Ships[s.ActiveShipId];

    private static int Carried(PlayerSession s, string item) => s.State.Inventory.CountOf(item) + ShipOf(s).Cargo.CountOf(item);

    /// <summary>Leaves the pilot's ship one breath from defeat and puts a drone beside it.</summary>
    private static void AlmostDown(SvGameServer server, PlayerSession s, string pilot, float z)
    {
        ShipOf(s).Hull = 0.5f;
        ShipOf(s).Shield = 0f;
        server.SpawnSpaceHostileForTest(pilot, EnemyCatalog.SpaceDrone, CombatEntityKind.Drone, new Vector3f(12f, 0f, z));
    }

    // ---------------- a downed ship ----------------

    [Fact]
    public void WithAnotherPilotInTheFlight_ADownedShip_SendsOnlyItsOwnCrewHome()
    {
        var (server, transport, pilots) = Flying("one_down", Alice, Bob);
        string instance = server.SpaceInstanceIdForTest(Bob)!;
        AlmostDown(server, pilots[Alice], Alice, 0f);
        float bobBefore = ShipOf(pilots[Bob]).Hull;

        Run(server, 1.0);

        Assert.False(server.InSpace(Alice));
        Assert.True(server.InSpace(Bob)); // the flight goes on for the pilot who is left
        Assert.True(server.SpaceInstanceLoadedForTest(instance));
        Assert.Equal(instance, server.SpaceInstanceIdForTest(Bob));
        Assert.Contains(server.SpaceEntitiesFor(Bob), e => e.Kind == CombatEntityKind.Drone); // …with its hostiles

        var closed = Assert.Single(transport.To<SpaceClosed>(pilots[Alice]));
        Assert.True(closed.ShipDisabled);
        Assert.Equal("@srv.space.ship_disabled", closed.Reason);
        Assert.Empty(transport.To<SpaceClosed>(pilots[Bob]));
        Assert.Contains("@srv.space.ally_ship_down:" + Alice, transport.TextsTo(pilots[Bob]));
        Assert.True(pilots[Alice].State.AboardShip);
        Assert.True(ShipOf(pilots[Alice]).Hull > 1f, "KeepShipOnDeath: recovered whole");
        Assert.Equal(bobBefore, ShipOf(pilots[Bob]).Hull, 3);
        Assert.DoesNotContain(server.OtherSpacePlayers(Bob), p => p.PlayerId == Alice); // Bob no longer sees that ship

        // Bob can still be beaten later — alone now, so the classic outcome: home, and the instance goes.
        AlmostDown(server, pilots[Bob], Bob, 600f);
        Run(server, 1.0);
        Assert.False(server.InSpace(Bob));
        Assert.False(server.SpaceInstanceLoadedForTest(instance));
    }

    [Fact]
    public void APilotAlone_KeepsTheOldOutcome_HomeAndTheInstanceUnloads()
    {
        var (server, transport, pilots) = Flying("alone_down", Alice);
        string instance = server.SpaceInstanceIdForTest(Alice)!;
        AlmostDown(server, pilots[Alice], Alice, 0f);

        Run(server, 1.0);

        Assert.False(server.InSpace(Alice));
        Assert.False(server.SpaceInstanceLoadedForTest(instance));
        var closed = Assert.Single(transport.To<SpaceClosed>(pilots[Alice]));
        Assert.True(closed.ShipDisabled);
        Assert.DoesNotContain(transport.Texts(), t => t.StartsWith("@srv.space.ally_ship_down", StringComparison.Ordinal));
        Assert.True(ShipOf(pilots[Alice]).Hull > 1f);
    }

    [Fact]
    public void WithKeepShipOff_OnlyTheDownedShip_IsLeftAWreck()
    {
        var (server, _, pilots) = Flying("wreck_down", Alice, Bob);
        server.RulesForTest.KeepShipOnDeath = false;
        AlmostDown(server, pilots[Alice], Alice, 0f);

        Run(server, 1.0);

        Assert.False(server.InSpace(Alice));
        Assert.True(ShipOf(pilots[Alice]).Downed);
        Assert.True(server.InSpace(Bob));
        Assert.False(ShipOf(pilots[Bob]).Downed);
    }

    // ---------------- a boss's loot ----------------

    /// <summary>Puts the boss between Alice and Bob (both in cannon range, outside its steady guns' damage for the
    /// moment a test needs) and returns it, standing still, its guns silent.</summary>
    private static CombatEntity QuietBoss(SvGameServer server, bool carriesEcho = false)
    {
        server.ShipMove(Alice, 0f, 0f, 0f);
        server.ShipMove(Bob, 0f, 0f, 60f);
        var boss = server.SpawnEscortCruiserForTest(Alice, carriesEcho, new Vector3f(0f, 0f, 30f))!;
        boss.Boss!.Speed = 0.001f;
        boss.DamagePerSecond = 0f;
        boss.HeavyDamage = 0f;
        boss.Shield = 0f;
        return boss;
    }

    [Fact]
    public void ABoss_PaysEveryPilotWhoHurtIt_TheLootTheCreditAndTheEvent()
    {
        var (server, transport, pilots) = Flying("boss_loot", Alice, Bob, Carol);
        var heard = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.CruiserDefeated, heard);
        var defeats = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.DefeatAny, defeats);
        var boss = QuietBoss(server);
        var loot = _content.Enemy(EnemyCatalog.EscortCruiser).Tier(EnemyTiers.Normal).Loot;
        Assert.NotEmpty(loot);

        float full = boss.Hull;
        server.FireWeapon(Alice, Cannon, boss.Id);
        Assert.True(boss.Hull < full, "Alice's shot lands");
        Assert.Equal(new[] { Alice }, server.SpaceBossFightersForTest(Alice));
        boss.Hull = 1f;
        server.FireWeapon(Bob, Cannon, boss.Id); // the last shot is Bob's

        Assert.Null(server.SpaceBossForTest(Alice));
        foreach (string fighter in new[] { Alice, Bob })
        {
            Assert.All(loot, l => Assert.Equal(l.Count, Carried(pilots[fighter], l.Item))); // each their own full share
            Assert.Single(heard.Events, e => e.Player == fighter);
            Assert.Single(defeats.Events, e => e.Player == fighter && e.Arg == GameEventKeys.DefeatSpaceMachine);
            Assert.Contains("vega.cruiser.down", transport.LineKeys(pilots[fighter]));
        }

        Assert.All(loot, l => Assert.Equal(0, Carried(pilots[Carol], l.Item))); // Carol only watched
        Assert.DoesNotContain(heard.Events, e => e.Player == Carol);
        Assert.All(heard.Events, e => Assert.Equal(server.Galaxy.FindBody(pilots[Alice].CurrentLocationId)!.SystemId, e.Arg));

        // The bar's last word reaches everyone in the flight, the onlooker too.
        foreach (var s in pilots.Values)
        {
            var last = transport.To<BossStatus>(s).Last();
            Assert.False(last.Active);
            Assert.True(last.Defeated);
            Assert.Equal(0f, last.Health);
            Assert.Equal(boss.Id, last.Id);
        }
    }

    [Fact]
    public void AFighterWhoseShipWentDown_StillGetsTheirShare()
    {
        var (server, _, pilots) = Flying("boss_downed_fighter", Alice, Bob);
        var heard = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.CruiserDefeated, heard);
        var boss = QuietBoss(server);
        var loot = _content.Enemy(EnemyCatalog.EscortCruiser).Tier(EnemyTiers.Normal).Loot;

        server.FireWeapon(Alice, Cannon, boss.Id);
        ShipOf(pilots[Alice]).Hull = 0.5f;
        ShipOf(pilots[Alice]).Shield = 0f;
        boss.DamagePerSecond = 7f; // its guns speak again: Alice goes home, Bob (as far away) is hurt but flies on
        Run(server, 0.3);
        Assert.False(server.InSpace(Alice));
        Assert.True(server.InSpace(Bob));
        Assert.Same(boss, server.SpaceBossForTest(Bob)); // the fight is not over because one ship fell

        boss.DamagePerSecond = 0f;
        boss.Hull = 1f;
        server.FireWeapon(Bob, Cannon, boss.Id);

        Assert.Null(server.SpaceBossForTest(Bob));
        Assert.All(loot, l => Assert.Equal(l.Count, Carried(pilots[Alice], l.Item)));
        Assert.All(loot, l => Assert.Equal(l.Count, Carried(pilots[Bob], l.Item)));
        Assert.Equal(new[] { Alice, Bob }, heard.Events.Select(e => e.Player).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void AnOrdinaryHostile_StillPaysOnlyTheLastShot()
    {
        var (server, _, pilots) = Flying("drone_loot", Alice, Bob);
        server.ShipMove(Alice, 0f, 0f, 0f);
        server.ShipMove(Bob, 0f, 0f, 60f);
        var cruiser = server.SpawnSpaceHostileForTest(Alice, EnemyCatalog.Cruiser, CombatEntityKind.Cruiser, new Vector3f(0f, 0f, 30f))!;
        cruiser.DamagePerSecond = 0f;
        cruiser.HeavyDamage = 0f;
        var loot = _content.Enemy(EnemyCatalog.Cruiser).Tier(EnemyTiers.Normal).Loot;
        Assert.Null(cruiser.Boss);
        ShipOf(pilots[Alice]).Modules.Remove("tractor_beam"); // without a tractor beam the loot is banked at once (with one it floats)
        ShipOf(pilots[Bob]).Modules.Remove("tractor_beam");

        server.FireWeapon(Alice, Cannon, cruiser.Id);
        cruiser.Hull = 1f;
        cruiser.Shield = 0f;
        server.FireWeapon(Bob, Cannon, cruiser.Id);

        Assert.DoesNotContain(server.SpaceEntitiesFor(Bob), e => e.Id == cruiser.Id);
        Assert.All(loot, l => Assert.Equal(l.Count, Carried(pilots[Bob], l.Item)));
        Assert.All(loot, l => Assert.Equal(0, Carried(pilots[Alice], l.Item)));
    }

    [Fact]
    public void TheWreckHook_HearsWhereTheStorysCruiserFell()
    {
        var (server, _, _) = Flying("boss_echo", Alice, Bob);
        var boss = QuietBoss(server, carriesEcho: true);
        var at = boss.Position;
        boss.Hull = 1f;
        server.FireWeapon(Alice, Cannon, boss.Id);

        // The entity is out of the instance, and it still says where it fell and what it carried — what the hook gets.
        Assert.DoesNotContain(server.SpaceEntitiesFor(Alice), e => e.Id == boss.Id);
        Assert.Equal(at, boss.Position);
        Assert.True(boss.Boss!.CarriesEcho);
        Assert.Equal(new[] { Alice }, boss.Boss.Fighters);
    }
}
