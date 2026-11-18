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

/// <summary>Space combat that moves (#3762): the barrel roll's evade window (#3753), the boost charge and the speed check
/// on ship poses (#3754), hostiles that circle and the heavy shot (#3756), the ship classes' traits (#3760) and the
/// upgrade modules (#3759).</summary>
public sealed class SpaceManeuverTests : IDisposable
{
    private const string Pilot = "Pilot";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public SpaceManeuverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_maneuver_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A server with a pilot in space: PvE on, weapons allowed, no ambient hostiles (tests place their own).</summary>
    private SvGameServer InSpace(string name, Action<SvGameServer>? beforeLaunch = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE;
        config.Rules.ShipWeapons = ShipWeaponMode.NpcsOnly;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        server.AddLocalPlayer(Pilot);
        beforeLaunch?.Invoke(server);
        server.EnterSpace(Pilot);
        Assert.True(server.InSpace(Pilot));
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

    private static float Health(SvGameServer server) => server.Ship.Hull + server.Ship.Shield;

    private static CombatEntity Drone(SvGameServer server, float x)
        => server.SpawnSpaceHostileForTest(Pilot, EnemyCatalog.SpaceDrone, CombatEntityKind.Drone, new Vector3f(x, 0f, 0f))!;

    // ---------------- #3753: the barrel roll ----------------

    [Fact]
    public void Roll_OpensAnEvadeWindow_InWhichHostileFireMisses()
    {
        var server = InSpace("roll_evade");
        Drone(server, 12f);

        float before = Health(server);
        Run(server, 0.5);
        Assert.True(Health(server) < before, "a drone in range hurts a ship that does not roll");

        Assert.True(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll));
        Assert.True(server.IsEvadingForTest(Pilot));
        float atRoll = Health(server);
        Run(server, 0.5);
        Assert.Equal(atRoll, Health(server), 3); // inside the window nothing lands

        Run(server, 1.0); // the window (0.75 s) is over
        Assert.False(server.IsEvadingForTest(Pilot));
        Assert.True(Health(server) < atRoll, "after the window the drone hits again");
    }

    [Fact]
    public void Roll_BeforeTheCooldownHasPassed_IsRefusedByTheServer()
    {
        var server = InSpace("roll_cooldown");

        Assert.True(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll));
        Run(server, 1.0);
        Assert.False(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll)); // the starter's cooldown is 4 s
        Assert.False(server.IsEvadingForTest(Pilot));

        Run(server, 3.2);
        Assert.True(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll));
    }

    [Fact]
    public void RollCooldown_CountsFromWhenTheRollWasDue_NotFromAnEarlyGrant()
    {
        // The server grants a roll a quarter of a second early (clock jitter). Restarting the cooldown from that grant
        // made every roll come a little sooner than the last: the real period was the cooldown minus the slack.
        var server = InSpace("roll_period");

        Assert.True(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll)); // t = 0: due again at 4.0
        Run(server, 3.8);
        Assert.True(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll)); // inside the slack: granted, due again at 8.0
        Run(server, 3.9);                                                // t = 7.7 — before 8.0 minus the slack
        Assert.False(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll));
        Run(server, 0.2);
        Assert.True(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll));
    }

    [Fact]
    public void RollCooldown_ComesFromTheShipsHandling()
    {
        var courier = ShipManeuverRules.Stats(_content.GetShip("courier"), _content.GetShip("courier")!.Handling, null);
        var starter = ShipManeuverRules.Stats(_content.GetShip("starter"), 1f, null);
        var deathblock = ShipManeuverRules.Stats(_content.GetShip("deathblock"), _content.GetShip("deathblock")!.Handling, null);

        Assert.Equal(ShipManeuverRules.RollBaseCooldown, starter.RollCooldown, 3);
        Assert.True(courier.RollCooldown < starter.RollCooldown, "an agile ship rolls more often");
        Assert.True(deathblock.RollCooldown > starter.RollCooldown, "a sluggish ship rolls rarely");
        Assert.Equal(ShipManeuverRules.RollCooldownMax, deathblock.RollCooldown, 3);
    }

    // ---------------- #3754: the boost ----------------

    [Fact]
    public void Boost_DrainsTheCharge_RunsDry_AndRefills()
    {
        var server = InSpace("boost_charge");
        Run(server, 0.1);
        float full = server.BoostChargeForTest(Pilot);
        Assert.Equal(ShipManeuverRules.DefaultBoostSeconds, full, 3);

        server.ShipManeuver(Pilot, ShipManeuverKinds.Boost, on: true);
        Run(server, 1.0);
        Assert.True(server.BoostActiveForTest(Pilot));
        Assert.Equal(full - 1f, server.BoostChargeForTest(Pilot), 2);

        Run(server, 2.5); // past the 3 s the starter has
        Assert.False(server.BoostActiveForTest(Pilot));
        Assert.True(server.BoostChargeForTest(Pilot) < 0.5f);

        // Still "held" on the client: the server does not start it again by itself — it takes a new press.
        Run(server, 7.5);
        Assert.False(server.BoostActiveForTest(Pilot));
        Assert.Equal(full, server.BoostChargeForTest(Pilot), 2);

        server.ShipManeuver(Pilot, ShipManeuverKinds.Boost, on: true);
        Run(server, 0.2);
        Assert.True(server.BoostActiveForTest(Pilot));
        server.ShipManeuver(Pilot, ShipManeuverKinds.Boost, on: false);
        Run(server, 0.2);
        Assert.False(server.BoostActiveForTest(Pilot));
    }

    [Fact]
    public void BoostMeter_NeedsAQuarterChargeToStart()
    {
        var stats = ShipManeuverStats.Default;
        var meter = default(ShipBoostMeter);
        meter.Charge = stats.BoostSeconds * 0.1f;

        meter.Tick(0.1f, want: true, stats);
        Assert.False(meter.Active);

        meter.Charge = stats.BoostSeconds * 0.5f;
        meter.Tick(0.1f, want: true, stats);
        Assert.True(meter.Active);
    }

    [Fact]
    public void SpeedCheck_HoldsBackAJump_ButNotAFlightAtFullSpeed()
    {
        var server = InSpace("speed_check");
        server.ShipSpeedCheck = true;
        server.ShipMove(Pilot, 0f, 0f, 0f); // the baseline
        Run(server, 0.1);

        // Full speed for a second in 12 Hz steps: 14 units — every step is accepted as reported.
        float x = 0f;
        for (int i = 0; i < 12; i++)
        {
            server.Tick(1.0 / 12.0);
            x += ShipManeuverRules.BaseSpeed / 12f;
            server.ShipMove(Pilot, x, 0f, 0f);
        }

        Assert.Equal(x, server.ShipPoseForTest(Pilot)!.Value.X, 3);

        // A jump of 500 units within a tenth of a second is pulled back to what the ship can have flown.
        server.Tick(0.1);
        server.ShipMove(Pilot, x + 500f, 0f, 0f);
        float held = server.ShipPoseForTest(Pilot)!.Value.X - x;
        Assert.InRange(held, 1f, ShipManeuverRules.AllowedTravel(1f, 1f, 0.1) + 0.01f);
    }

    [Fact]
    public void SpeedCheck_AllowsMore_WhileTheBoostIsOn()
    {
        float plain = ShipManeuverRules.AllowedTravel(1f, 1f, 1.0);
        float boosted = ShipManeuverRules.AllowedTravel(1f, ShipManeuverRules.DefaultBoostPower, 1.0);
        Assert.True(boosted > plain);

        var server = InSpace("speed_boost");
        server.ShipSpeedCheck = true;
        server.ShipMove(Pilot, 0f, 0f, 0f);
        Run(server, 0.1);
        server.ShipManeuver(Pilot, ShipManeuverKinds.Boost, on: true);
        server.Tick(1.0);
        float want = (plain + boosted) / 2f; // further than an unboosted ship may be, within the boosted allowance
        server.ShipMove(Pilot, want, 0f, 0f);
        Assert.Equal(want, server.ShipPoseForTest(Pilot)!.Value.X, 3);
    }

    [Fact]
    public void SpeedCheck_IsOffUnlessTheHostTurnsItOn()
    {
        var server = InSpace("speed_off");
        server.Tick(0.1);
        server.ShipMove(Pilot, 900f, 0f, 0f);
        Assert.Equal(900f, server.ShipPoseForTest(Pilot)!.Value.X, 3);
    }

    // ---------------- #3759: upgrade modules, #3760: ship classes ----------------

    [Fact]
    public void Afterburner_AndThrusters_ChangeWhatTheServerUses()
    {
        var plain = InSpace("mods_plain");
        var basic = plain.ShipManeuverStatsForTest;

        var fitted = InSpace("mods_fitted", s =>
        {
            s.Ship.Modules.Add("afterburner");
            s.Ship.Modules.Add("maneuvering_thrusters");
        });
        var tuned = fitted.ShipManeuverStatsForTest;

        Assert.True(tuned.BoostSeconds > basic.BoostSeconds, "the afterburner lengthens the boost");
        Assert.True(tuned.BoostRechargeSeconds < basic.BoostRechargeSeconds, "… and refills it faster");
        Assert.True(tuned.RollCooldown < basic.RollCooldown, "the thrusters shorten the roll cooldown");
    }

    [Fact]
    public void ShipClasses_DifferInBoostRollAndWeaponDamage()
    {
        ShipManeuverStats Of(string key) => ShipManeuverRules.Stats(_content.GetShip(key), _content.GetShip(key)!.Handling, null);

        Assert.True(Of("courier").BoostSeconds > Of("starter").BoostSeconds, "a light ship boosts longer");
        Assert.True(Of("deathblock").BoostPower < Of("starter").BoostPower, "a heavy ship boosts weakly");
        Assert.True(Of("thunderbolt").BoostPower > Of("starter").BoostPower, "the strike ship has the hardest kick");
        Assert.True(_content.GetShip("thunderbolt")!.WeaponDamage > 1f);
        Assert.Equal(1f, _content.GetShip("starter")!.WeaponDamage);

        var server = InSpace("class_damage", s => s.Ship.ShipType = "thunderbolt");
        Assert.Equal(_content.GetShip("thunderbolt")!.WeaponDamage, server.ShipWeaponDamageForTest, 3);
    }

    // ---------------- #3756: hostiles that circle, the heavy shot ----------------

    [Fact]
    public void AHostileThatHoldsItsRing_CirclesItsQuarry()
    {
        var server = InSpace("orbit");
        var drone = Drone(server, 12f); // inside its stand-off ring (16)
        var start = drone.Position;

        Run(server, 2.0);

        Assert.True(drone.Position.DistanceSquared(start) > 4f, "it moved along its ring instead of standing still");
        float dist = (float)Math.Sqrt(drone.Position.DistanceSquared(new Vector3f(0f, 0f, 0f)));
        Assert.InRange(dist, 11f, 13f); // … and kept its distance
    }

    [Fact]
    public void HeavyShot_ChargesThenHits_AndARollMakesItMiss()
    {
        float Fight(string name, bool roll)
        {
            var server = InSpace(name);
            var cruiser = server.SpawnSpaceHostileForTest(Pilot, EnemyCatalog.Cruiser, CombatEntityKind.Cruiser, new Vector3f(30f, 0f, 0f))!;

            double waited = 0;
            while (server.HeavyChargeLeftForTest(Pilot, cruiser.Id) <= 0 && waited < 20)
            {
                server.Tick(0.1);
                waited += 0.1;
            }

            Assert.True(server.HeavyChargeLeftForTest(Pilot, cruiser.Id) > 0, "the cruiser charges a heavy shot at a pilot in range");
            while (server.HeavyChargeLeftForTest(Pilot, cruiser.Id) > 0.3)
            {
                server.Tick(0.1);
            }

            if (roll)
            {
                Assert.True(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll));
            }

            float before = Health(server);
            Run(server, 0.5);
            Assert.True(server.HeavyChargeLeftForTest(Pilot, cruiser.Id) <= 0, "the shot has fired");
            return before - Health(server);
        }

        float heavy = _content.Enemy(EnemyCatalog.Cruiser).Tier(EnemyTiers.Normal).HeavyShotDamage;
        Assert.True(heavy > 0f);
        Assert.True(Fight("heavy_hit", roll: false) >= heavy, "the heavy shot lands on a ship that does not roll");
        Assert.Equal(0f, Fight("heavy_miss", roll: true), 3);
    }
}
