// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Ship weapons with their own behaviour (#3758) and the weapon ladder's base (#3757): the missile's flight
/// time, the scatter cannon's cone, the ion pulse's stun, the point-defence turret, and the data rules behind them.</summary>
public sealed class ShipWeaponBehaviourTests : IDisposable
{
    private const string Pilot = "Pilot";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public ShipWeaponBehaviourTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_weapons_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
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

    private SvGameServer InSpace(string name, params string[] modules) => InSpace(name, 1, modules);

    private SvGameServer InSpace(string name, long seed, params string[] modules)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = name, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE;
        config.Rules.ShipWeapons = ShipWeaponMode.NpcsOnly;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        server.AddLocalPlayer(Pilot);
        foreach (var module in modules)
        {
            server.Ship.Modules.Add(module);
        }

        server.Ship.Modules.Remove("tractor_beam"); // loot goes straight to the hold, no floating drops
        server.EnterSpace(Pilot);
        server.ShipMove(Pilot, 0f, 0f, 0f);
        return server;
    }

    private static void Run(SvGameServer server, double seconds, double step = 0.1)
    {
        for (double t = 0; t < seconds - 1e-9; t += step)
        {
            server.Tick(step);
        }
    }

    private static CombatEntity Ufo(SvGameServer server, float x, float z = 0f)
        => server.SpawnSpaceHostileForTest(Pilot, EnemyCatalog.Ufo, CombatEntityKind.Ufo, new Vector3f(x, 0f, z))!;

    private double Stat(string module, string stat) => ShipWeaponRules.Stat(_content.GetShipModule(module), stat);

    // ---------------- #3757: the base of the ladder ----------------

    [Fact]
    public void EveryWeaponModule_IsFoundByItsStats_NotByAListOfKeys()
    {
        var weapons = _content.ShipModules.Values.Where(m => ShipWeaponRules.IsWeapon(m)).Select(m => m.Key).OrderBy(k => k).ToArray();
        Assert.Equal(
            new[] { "asteroid_breaker", "ion_pulse", "laser_cannon_2", "missile_launcher", "point_defense", "scatter_cannon", "ship_cannon_1", "ship_laser_basic" },
            weapons);

        // The turret fires by itself: it is a weapon, but takes no place in the pilot's ship-systems bar.
        Assert.False(ShipWeaponRules.IsSelectable(_content.GetShipModule("point_defense")));
        Assert.True(ShipWeaponRules.IsSelectable(_content.GetShipModule("missile_launcher")));
        Assert.False(ShipWeaponRules.IsWeapon(_content.GetShipModule("afterburner")));
    }

    [Fact]
    public void ShipCannonOne_BeatsTheStarterLaser_InAFight()
    {
        double Dps(string key) => Stat(key, ShipWeaponRules.StatDamage) / Stat(key, ShipWeaponRules.StatCooldown);
        Assert.True(Dps("ship_cannon_1") > Dps("ship_laser_basic"), "the first combat upgrade must out-damage the free laser");
        Assert.True(Dps("laser_cannon_2") > Dps("ship_cannon_1"));
    }

    [Fact]
    public void EveryNewWeapon_HasItsBlueprint_AndItsOwnLook()
    {
        foreach (var key in new[] { "missile_launcher", "scatter_cannon", "ion_pulse", "point_defense", "afterburner", "maneuvering_thrusters" })
        {
            var module = _content.GetShipModule(key);
            Assert.NotNull(module);
            Assert.Equal(key, module!.RequiredBlueprint);
            var blueprint = _content.GetBlueprint(key);
            Assert.NotNull(blueprint);
            Assert.All(blueprint!.Prerequisites, p => Assert.NotNull(_content.GetBlueprint(p)));
        }

        Assert.Equal(FxStyles.Missile, _content.GetShipModule("missile_launcher")!.Fx!.Style);
        Assert.Equal(FxStyles.Scatter, _content.GetShipModule("scatter_cannon")!.Fx!.Style);
        Assert.Equal(FxStyles.IonPulse, _content.GetShipModule("ion_pulse")!.Fx!.Style);
        Assert.All(_content.ShipModules.Values.Where(m => m.Fx is not null), m => Assert.True(FxStyles.IsKnown(m.Fx!.Style), m.Key));
    }

    // ---------------- #3758: the missile ----------------

    [Fact]
    public void Missile_LandsAfterItsFlight_NotAtOnce_AndNeedsNoBoresight()
    {
        var server = InSpace("missile", "missile_launcher");
        var ufo = Ufo(server, 0f, -80f); // behind the ship, beyond the hostiles' engage range
        float hull = ufo.Hull;

        // The nose points the other way: a homing shot is accepted all the same.
        server.FireWeapon(Pilot, "missile_launcher", ufo.Id, 0f, 0f, 1f);
        Assert.Equal(1, server.MissilesInFlightForTest(Pilot));
        Assert.Equal(hull, ufo.Hull); // still flying

        Run(server, 3.0); // 80 units at 45 units per second
        Assert.Equal(0, server.MissilesInFlightForTest(Pilot));
        Assert.True(ufo.Hull < hull || !server.SpaceEntitiesFor(Pilot).Contains(ufo), "the missile landed");
    }

    [Fact]
    public void Missile_IsWasted_WhenItsTargetIsGoneOnArrival()
    {
        var server = InSpace("missile_wasted", "missile_launcher", "laser_cannon_2");
        var ufo = Ufo(server, 0f, 50f);
        var other = Ufo(server, 0f, 55f);

        server.FireWeapon(Pilot, "missile_launcher", ufo.Id, 0f, 0f, 1f);
        server.FireWeapon(Pilot, "laser_cannon_2", ufo.Id, 0f, 0f, 1f); // 45 damage: the UFO (40 hull) is gone before the missile arrives
        Assert.DoesNotContain(ufo, server.SpaceEntitiesFor(Pilot));

        float before = other.Hull;
        Run(server, 2.0);
        Assert.Equal(0, server.MissilesInFlightForTest(Pilot));
        Assert.Equal(before, other.Hull); // it does not pick another target
    }

    // ---------------- #3758: the scatter cannon ----------------

    [Fact]
    public void ScatterCannon_HitsTheHostilesInItsCone_UpToItsTargetCount()
    {
        var server = InSpace("scatter", "scatter_cannon");
        var aimed = Ufo(server, 0f, 30f);
        var left = Ufo(server, -6f, 30f);   // ~11° off the aimed one
        var right = Ufo(server, 6f, 32f);
        var fourth = Ufo(server, 3f, 34f);  // in the cone too, but the cannon hits three
        var behind = Ufo(server, 0f, -30f); // outside the cone
        float full = aimed.Hull;

        server.FireWeapon(Pilot, "scatter_cannon", aimed.Id, 0f, 0f, 1f);

        Assert.True(aimed.Hull < full);
        Assert.Equal(full, behind.Hull);
        int hit = new[] { aimed, left, right, fourth }.Count(e => e.Hull < full);
        Assert.Equal((int)Stat("scatter_cannon", ShipWeaponRules.StatTargets), hit);
    }

    // ---------------- #3758: the ion pulse ----------------

    [Fact]
    public void IonPulse_SilencesItsTarget_ForAWhile()
    {
        var server = InSpace("ion", "ion_pulse");
        var ufo = Ufo(server, 0f, 20f);

        float before = server.Ship.Hull + server.Ship.Shield;
        Run(server, 0.5);
        Assert.True(server.Ship.Hull + server.Ship.Shield < before, "the UFO fires on a ship in range");

        server.FireWeapon(Pilot, "ion_pulse", ufo.Id, 0f, 0f, 1f);
        Assert.True(server.SpaceHostileSilencedForTest(Pilot, ufo.Id));
        float silenced = server.Ship.Hull + server.Ship.Shield;
        var held = ufo.Position;
        Run(server, 2.0);
        Assert.True(server.Ship.Hull + server.Ship.Shield >= silenced, "a silenced hostile deals no damage");
        Assert.Equal(held, ufo.Position); // … and holds still

        Run(server, 3.0); // the stun (3.5 s) is over
        Assert.False(server.SpaceHostileSilencedForTest(Pilot, ufo.Id));
        float after = server.Ship.Hull + server.Ship.Shield;
        Run(server, 0.5);
        Assert.True(server.Ship.Hull + server.Ship.Shield < after, "then it fires again");
    }

    // ---------------- #3758: the point-defence turret ----------------

    [Fact]
    public void PointDefence_FiresByItself_AtTheNearestHostileInRange()
    {
        var server = InSpace("turret", "point_defense");
        var near = Ufo(server, 0f, 25f);
        var far = Ufo(server, 0f, 400f);
        float full = near.Hull;

        Run(server, 3.0); // no input at all

        Assert.True(near.Hull < full || !server.SpaceEntitiesFor(Pilot).Contains(near), "the turret hit the hostile in its range");
        Assert.Equal(far.HullMax, far.Hull); // out of its range

        // The pilot cannot pull its trigger: it is no weapon of the ship-systems bar.
        float hull = far.Hull;
        server.ShipMove(Pilot, 0f, 0f, 390f);
        server.FireWeapon(Pilot, "point_defense", far.Id, 0f, 0f, 1f);
        Assert.Equal(hull, far.Hull);
    }

    [Fact]
    public void PointDefence_StaysSilent_WhereTheWorldAllowsNoShipWeapons()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "turret_off"));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = "turret_off", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE;
        config.Rules.ShipWeapons = ShipWeaponMode.Off;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        server.AddLocalPlayer(Pilot);
        server.Ship.Modules.Add("point_defense");
        server.EnterSpace(Pilot);
        server.ShipMove(Pilot, 0f, 0f, 0f);
        var ufo = Ufo(server, 0f, 25f);

        Run(server, 3.0);

        Assert.Equal(ufo.HullMax, ufo.Hull);
    }

    // ---------------- #3758: shields, stations and wrecks ----------------

    [Fact]
    public void AHostilesShield_TakesTheDamageFirst_AndAnIonPulseDropsIt()
    {
        var server = InSpace("enemy_shield", "ship_cannon_1", "ion_pulse");
        var cruiser = server.SpawnSpaceHostileForTest(Pilot, EnemyCatalog.Cruiser, CombatEntityKind.Cruiser, new Vector3f(0f, 0f, 40f))!;
        float hull = cruiser.Hull;
        Assert.True(cruiser.Shield > 0f, "the cruiser flies with a shield");

        server.FireWeapon(Pilot, "ship_cannon_1", cruiser.Id, 0f, 0f, 1f);
        Assert.Equal(hull, cruiser.Hull);                   // the shield took it
        Assert.True(cruiser.Shield < cruiser.ShieldMax);

        server.FireWeapon(Pilot, "ion_pulse", cruiser.Id, 0f, 0f, 1f);
        Assert.Equal(0f, cruiser.Shield);                   // the pulse takes the shield down
        Assert.True(cruiser.Hull < hull, "with the shield gone the pulse's own little damage reaches the hull");
    }

    [Fact]
    public void Wreckage_IsTornApart_ByACombatCannonToo()
    {
        var server = InSpace("wreckage", "laser_cannon_2", "ship_cannon_1");
        var ufo = Ufo(server, 0f, 30f);
        server.FireWeapon(Pilot, "laser_cannon_2", ufo.Id, 0f, 0f, 1f); // destroyed — it leaves wreckage
        var debris = server.SpaceEntitiesFor(Pilot).FirstOrDefault(e => e.Kind == CombatEntityKind.Debris);
        Assert.NotNull(debris);

        server.ShipMove(Pilot, debris!.Position.X, debris.Position.Y, debris.Position.Z - 10f);
        int blocks = server.StructureBlockCountForTest(debris.Id);
        float hull = debris.Hull;
        server.FireWeapon(Pilot, "ship_cannon_1", debris.Id, 0f, 0f, 1f); // a pure combat weapon; rocks would refuse it

        Assert.True(debris.Hull < hull || !server.SpaceEntitiesFor(Pilot).Contains(debris), "the cannon damaged the wreckage");
        Assert.True(server.StructureBlockCountForTest(debris.Id) < blocks, "… and blocks came off it");
    }

    [Fact]
    public void ADerelictStation_LosesHullBlocks_WhereAShotLands()
    {
        var server = InSpace("derelict_hit", 7);
        string system = server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId;
        string id = BlocksBeyondTheStars.WorldGeneration.DerelictPlacer.BodyId(system, 0);
        var contact = server.SpaceEntitiesFor(Pilot).First(e => e.Id == id);
        var layout = server.DerelictLayoutForTest(id)!;
        server.ShipMove(Pilot, contact.Position.X, contact.Position.Y, contact.Position.Z - ((layout.Length / 2f) + 12f));
        int before = server.StructureBlockCountForTest(id);
        Assert.True(before > 0);

        server.FireWeapon(Pilot, "ship_laser_basic", id, 0f, 0f, 1f);

        int after = server.StructureBlockCountForTest(id);
        Assert.InRange(before - after, 1, SvGameServer.HullBlocksFor(14f));
    }

    [Fact]
    public void TheShieldRule_NamesTheGeneratedDerelicts_AsTheServerDoes()
    {
        Assert.Equal(BlocksBeyondTheStars.WorldGeneration.DerelictPlacer.IdPrefix, SpaceCombatRules.DerelictIdPrefix);
        Assert.Equal(SvGameServer.DerelictStationId, SpaceCombatRules.StoryDerelictId);
        Assert.True(SpaceCombatRules.StationHasShield("station:alpha"));
        Assert.True(SpaceCombatRules.StationHasShield(SpaceCombatRules.StoryDerelictId)); // the story's own keeps its rules
        Assert.False(SpaceCombatRules.StationHasShield(BlocksBeyondTheStars.WorldGeneration.DerelictPlacer.BodyId("sys0", 0)));
        Assert.False(SpaceCombatRules.StationHasShield(GiantShipRules.IdPrefix + "x")); // a giant wreck is a derelict
    }

    [Fact]
    public void AnIntactStation_IsBehindItsShield()
    {
        Assert.Equal(1, SvGameServer.HullBlocksFor(4f));
        Assert.Equal(6, SvGameServer.HullBlocksFor(70f));

        var server = InSpace("station_shield", 7);
        var station = server.SpaceEntitiesFor(Pilot).FirstOrDefault(e => e.Kind == CombatEntityKind.SpaceStation
            && !BlocksBeyondTheStars.WorldGeneration.DerelictPlacer.IsDerelictId(e.Id));
        if (station is null)
        {
            return; // this seed's start system has no intact station in flight range
        }

        server.ShipMove(Pilot, station.Position.X, station.Position.Y, station.Position.Z - 30f);
        int before = server.StructureBlockCountForTest(station.Id);
        float hull = station.Hull;
        server.FireWeapon(Pilot, "ship_laser_basic", station.Id, 0f, 0f, 1f);

        Assert.Equal(before, server.StructureBlockCountForTest(station.Id));
        Assert.Equal(hull, station.Hull);
    }
}
