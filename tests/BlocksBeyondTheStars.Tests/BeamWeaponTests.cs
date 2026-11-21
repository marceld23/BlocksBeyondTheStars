// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The held beam of a weapon (#3898, the Guardian laser): the data, the unchanged tap shot, and the server's side of the
/// hold — ticks that land the shot's own way, the cadence against a spamming client, the suit's reserve, every way the
/// beam ends, the line of fire, terrain set alight, and what the other players are told. Everything stands in the open
/// sky (nothing blocks a line, nothing ground-snaps) and no test runs a full server tick; the clock moves by
/// <c>AdvanceUptimeForTest</c>.
/// </summary>
public sealed class BeamWeaponTests : IDisposable
{
    private const float Sky = 300f;
    private const string Laser = "guardian_laser";

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_beam_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    private static ToolProperties LaserTool => Content.Value.GetItem(Laser)!.Tool!;

    private static float Tick => BeamWeaponRules.Tick(LaserTool);

    private SvGameServer NewServer(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var server = new SvGameServer(
            new ServerConfig
            {
                WorldName = name,
                Seed = 11,
                StartPlanet = "rocky",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
            },
            Content.Value,
            transport,
            repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static PlayerSession OnFoot(SvGameServer server, string name, float x, float z, string held = Laser)
    {
        var s = server.AddLocalPlayer(name);
        s.State.AboardShip = false;
        s.State.Position = new Vector3f(x, Sky, z);
        s.State.SuitEnergy = 100f;
        s.State.Inventory.SetSlot(0, new ItemStack(held, 1));
        s.State.SelectedHotbarSlot = 0;
        return s;
    }

    /// <summary>A machine east of the shooter, tough enough to stand in a beam for a while.</summary>
    private static CombatEntity Machine(SvGameServer server, float x, float z, float hull = 1000f)
    {
        server.SpawnPlanetEnemyAtForTest(new Vector3f(x, Sky, z), damagePerSecond: 0f);
        var e = server.PlanetEnemies[^1];
        e.Hull = hull;
        e.HullMax = hull;
        return e;
    }

    private static BeamWeaponIntent TickAt(string entityId, float dx = 1f, float dy = 0f, float dz = 0f)
        => new() { Active = true, EntityId = entityId, DirX = dx, DirY = dy, DirZ = dz };

    private static List<ActionFx> BeamFxTo(RecordingTransport t, PlayerSession who, byte kind)
        => t.To<ActionFx>(who).Where(f => f.Kind == kind).ToList();

    // ---------------- Data ----------------

    [Fact]
    public void OnlyTheGuardianLaser_HasABeam_AndItsValuesArePinned()
    {
        var tool = LaserTool;
        Assert.True(BeamWeaponRules.IsBeamWeapon(tool));
        Assert.Equal((55f, 9f, 22f, 0.25f), (tool.BeamDamagePerSecond, tool.BeamEnergyPerSecond, tool.BeamRange, tool.BeamTick));
        Assert.True(tool.BeamRange < tool.Range, "the beam reaches less far than the shot");
        Assert.Equal(13.75f, BeamWeaponRules.DamagePerTick(tool), 3);
        Assert.Equal(2.25f, BeamWeaponRules.EnergyPerTick(tool), 3);

        var others = Content.Value.Items.Values.Where(i => i.Key != Laser && BeamWeaponRules.IsBeamWeapon(i.Tool)).Select(i => i.Key);
        Assert.Empty(others);
    }

    [Fact]
    public void TheBalance_AFullSuitOfBeam_IsFarLessThanAFullSuitOfShots()
    {
        var tool = LaserTool;
        var blaster = Content.Value.GetItem("plasma_blaster")!.Tool!;

        // 100 energy, 15 kept: 37 ticks = 9.25 s of beam and about 509 damage.
        int ticks = BeamWeaponRules.TicksUntilReserve(tool, 100f, 100f);
        Assert.Equal(37, ticks);
        Assert.Equal(9.25f, ticks * BeamWeaponRules.Tick(tool), 3);
        float beamDamage = ticks * BeamWeaponRules.DamagePerTick(tool);
        Assert.Equal(508.75f, beamDamage, 2);

        // The same suit fires 50 plasma shots (2750 damage) — the beam is the comfortable way, never the strong one.
        float blasterDamage = (float)Math.Floor(100f / blaster.EnergyPerUse) * blaster.Damage;
        Assert.Equal(2750f, blasterDamage, 2);
        Assert.True(beamDamage < blasterDamage * 0.25f);
        Assert.True(tool.Damage > blaster.Damage, "per shot the laser is the stronger pistol");

        // With the suit battery (150): 56 ticks = 14 s.
        Assert.Equal(56, BeamWeaponRules.TicksUntilReserve(tool, 150f, 150f));
    }

    [Fact]
    public void TheValidation_RefusesHalfABeam()
    {
        static List<string> Problems(ToolProperties tool)
        {
            var problems = new List<string>();
            BeamWeaponRules.Validate(new ItemDefinition { Key = "x", Tool = tool }, problems);
            return problems;
        }

        Assert.Empty(Problems(LaserTool));
        Assert.Empty(Problems(Content.Value.GetItem("laser_pistol")!.Tool!)); // no beam values at all
        Assert.NotEmpty(Problems(new ToolProperties { Kind = ToolKind.Weapon, Range = 30f, BeamDamagePerSecond = 10f })); // no energy, no reach
        Assert.NotEmpty(Problems(new ToolProperties { Kind = ToolKind.Weapon, Range = 30f, BeamDamagePerSecond = 10f, BeamEnergyPerSecond = 5f, BeamRange = 40f })); // past the shot
        Assert.NotEmpty(Problems(new ToolProperties { Kind = ToolKind.Weapon, Range = 4f, BeamDamagePerSecond = 10f, BeamEnergyPerSecond = 5f, BeamRange = 4f })); // a blade
        Assert.NotEmpty(Problems(new ToolProperties { Kind = ToolKind.Drill, Range = 30f, BeamDamagePerSecond = 10f, BeamEnergyPerSecond = 5f, BeamRange = 20f }));
        Assert.NotEmpty(Problems(new ToolProperties { Kind = ToolKind.Weapon, Range = 30f, BeamDamagePerSecond = 10f, BeamEnergyPerSecond = 5f, BeamRange = 20f, BeamTick = 0.01f }));
    }

    [Fact]
    public void AChangedLaser_KeepsItsBeam_AndTheLabsLeversMoveIt()
    {
        var tool = LaserTool;
        var changed = ToolMods.Apply(tool, new ItemMods(ModStat.Power, 2, ModStat.None, 0, ModStat.None, 0));
        Assert.True(BeamWeaponRules.IsBeamWeapon(changed));
        Assert.Equal(tool.BeamDamagePerSecond * (1f + (2 * ToolMods.PowerPerLevel)), changed.BeamDamagePerSecond, 3);
        Assert.Equal(tool.BeamEnergyPerSecond, changed.BeamEnergyPerSecond, 3);
        Assert.Equal(tool.BeamRange, changed.BeamRange, 3);
        Assert.Equal(tool.BeamTick, changed.BeamTick, 3);
    }

    // ---------------- The tap ----------------

    [Fact]
    public void ATap_IsTheOrdinaryShot_FullDamageOneShotOfEnergy()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_tap", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 130.5f, 100.5f); // 30 blocks: inside the shot's 34, outside the beam's 22

        server.AttackEntity("Alice", machine.Id, 1f, 0f, 0f);

        Assert.Equal(1000f - LaserTool.Damage, machine.Hull, 3);
        Assert.Equal(100f - LaserTool.EnergyPerUse, p.State.SuitEnergy, 3);
        Assert.False(server.HeldBeamActiveForTest("Alice"));
    }

    // ---------------- The beam ----------------

    [Fact]
    public void BeamTicks_LandDamage_AndDrainEnergy()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_ticks", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 110.5f, 100.5f);

        for (int i = 0; i < 4; i++)
        {
            server.BeamWeaponForTest("Alice", TickAt(machine.Id));
            server.AdvanceUptimeForTest(Tick);
        }

        Assert.True(server.HeldBeamActiveForTest("Alice"));
        Assert.Equal(1000f - (4 * 13.75f), machine.Hull, 2);
        Assert.Equal(100f - (4 * 2.25f), p.State.SuitEnergy, 2);
        Assert.Empty(t.To<ActionRejected>(p));
    }

    [Fact]
    public void ABeamIntoTheAir_DrainsTheSameEnergy_AndHurtsNothing()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_air", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 110.5f, 100.5f);

        server.BeamWeaponForTest("Alice", TickAt(string.Empty));

        Assert.Equal(1000f, machine.Hull);
        Assert.Equal(100f - 2.25f, p.State.SuitEnergy, 3);
    }

    [Fact]
    public void TheBeamKills_ThroughTheShotsOwnPath_LootAndAll()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_kill", t);
        OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 108.5f, 100.5f, hull: 30f); // a Guardian robot's hull: three ticks

        for (int i = 0; i < 3; i++)
        {
            server.BeamWeaponForTest("Alice", TickAt(machine.Id));
            server.AdvanceUptimeForTest(Tick);
        }

        Assert.DoesNotContain(server.PlanetEnemies, e => e.Id == machine.Id);
        Assert.Contains(t.Messages(), m => m is PlanetEnemyDefeated d && d.Id == machine.Id);
    }

    [Fact]
    public void ASpammingClient_GetsNoMoreTicksThanTheCadence()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_spam", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 110.5f, 100.5f);

        // Two seconds of a tick every 10 ms, with a release thrown in every tenth message (a press/release flutter).
        for (int i = 0; i < 200; i++)
        {
            server.BeamWeaponForTest("Alice", i % 10 == 9 ? new BeamWeaponIntent { Active = false } : TickAt(machine.Id));
            server.AdvanceUptimeForTest(0.01);
        }

        int ticks = (int)Math.Round((1000f - machine.Hull) / 13.75f);
        Assert.InRange(ticks, 7, 9); // 2 s at 0.25 s: eight, give or take the jitter slack — never 180
        Assert.Equal(100f - (ticks * 2.25f), p.State.SuitEnergy, 2);
    }

    [Fact]
    public void ATickALittleEarly_StillLands_ButEarlyNeverAddsUp()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_jitter", t);
        OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 110.5f, 100.5f);

        // The client paces its ticks exactly; the network delivers some 0.05 s early and some late. All six land.
        foreach (double gap in new[] { 0.25, 0.20, 0.30, 0.20, 0.30, 0.25 })
        {
            server.BeamWeaponForTest("Alice", TickAt(machine.Id));
            server.AdvanceUptimeForTest(gap);
        }

        Assert.Equal(1000f - (6 * 13.75f), machine.Hull, 2);

        // A client that keeps sending 20 % too fast gains nothing: over the next 1.8 s (ten sends) it lands at most
        // the eight ticks the cadence allows — the schedule moves on by whole ticks, so being early never adds up.
        server.AdvanceUptimeForTest(1.0);
        float before = machine.Hull;
        for (int i = 0; i < 10; i++)
        {
            server.BeamWeaponForTest("Alice", TickAt(machine.Id));
            server.AdvanceUptimeForTest(Tick - 0.05);
        }

        int ticks = (int)Math.Round((before - machine.Hull) / 13.75f);
        Assert.InRange(ticks, 6, 8);
    }

    [Fact]
    public void TheBeam_StopsAtTheReserve_AndSaysSoOnce()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_reserve", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 104.5f, 104.5f, held: "laser_pistol");
        var machine = Machine(server, 110.5f, 100.5f, hull: 100000f);
        p.State.SuitEnergy = 20f; // reserve 15: two ticks (20 → 17.75 → 15.5), the third would go below

        for (int i = 0; i < 12; i++)
        {
            server.BeamWeaponForTest("Alice", TickAt(machine.Id));
            server.AdvanceUptimeForTest(Tick);
        }

        Assert.Equal(15.5f, p.State.SuitEnergy, 3);
        Assert.True(p.State.SuitEnergy >= BeamWeaponRules.Reserve(100f));
        Assert.Equal(100000f - (2 * 13.75f), machine.Hull, 2);
        Assert.False(server.HeldBeamActiveForTest("Alice"));
        Assert.Single(t.TextsTo(p), text => text == "@srv.attack.beam_reserve"); // paced: once in these three seconds
        Assert.Single(BeamFxTo(t, p, FxActionKinds.WeaponBeamEnd));  // the shooter's client lets go
        Assert.Single(BeamFxTo(t, bob, FxActionKinds.WeaponBeamEnd)); // and the others hear it end — once, not per message

        // The tap still works below the beam's reserve: a single shot costs 2.
        server.AttackEntity("Alice", machine.Id, 1f, 0f, 0f);
        Assert.Equal(13.5f, p.State.SuitEnergy, 3);
    }

    [Fact]
    public void TheBeam_Stops_WhenTheItemLeavesTheHand_WhenThePlayerSits_AndWhenNoTickArrives()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_stop", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 110.5f, 100.5f);

        // The item leaves the hand (another slot is selected).
        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        Assert.True(server.HeldBeamActiveForTest("Alice"));
        p.State.SelectedHotbarSlot = 3;
        server.TickBeamWeaponsForTest("Alice");
        Assert.False(server.HeldBeamActiveForTest("Alice"));
        Assert.Single(BeamFxTo(t, p, FxActionKinds.WeaponBeamEnd));

        // Sitting down.
        p.State.SelectedHotbarSlot = 0;
        server.AdvanceUptimeForTest(1.0);
        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        Assert.True(server.HeldBeamActiveForTest("Alice"));
        p.State.Seated = true;
        server.TickBeamWeaponsForTest("Alice");
        Assert.False(server.HeldBeamActiveForTest("Alice"));
        float hull = machine.Hull;
        server.AdvanceUptimeForTest(1.0);
        server.BeamWeaponForTest("Alice", TickAt(machine.Id)); // and no beam starts while seated
        Assert.False(server.HeldBeamActiveForTest("Alice"));
        Assert.Equal(hull, machine.Hull);
        p.State.Seated = false;

        // No tick for longer than the timeout.
        server.AdvanceUptimeForTest(1.0);
        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        server.AdvanceUptimeForTest(BeamWeaponRules.TimeoutSeconds * 0.5);
        server.TickBeamWeaponsForTest("Alice");
        Assert.True(server.HeldBeamActiveForTest("Alice")); // half the timeout: still on
        server.AdvanceUptimeForTest(BeamWeaponRules.TimeoutSeconds);
        server.TickBeamWeaponsForTest("Alice");
        Assert.False(server.HeldBeamActiveForTest("Alice"));

        // A release ends it at once, without telling the shooter (their client already let go).
        server.AdvanceUptimeForTest(1.0);
        int endsBefore = BeamFxTo(t, p, FxActionKinds.WeaponBeamEnd).Count;
        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        server.BeamWeaponForTest("Alice", new BeamWeaponIntent { Active = false });
        Assert.False(server.HeldBeamActiveForTest("Alice"));
        Assert.Equal(endsBefore, BeamFxTo(t, p, FxActionKinds.WeaponBeamEnd).Count);
    }

    [Fact]
    public void TheBeam_Stops_WhenThePlayerCrossesToTheOtherSide_OrGoesDown()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_cross", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 110.5f, 100.5f);

        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        Assert.True(server.HeldBeamActiveForTest("Alice"));
        p.OtherSide = true;
        server.TickBeamWeaponsForTest("Alice");
        Assert.False(server.HeldBeamActiveForTest("Alice"));

        // Over there the intent is refused at the gate like every weapon.
        float hull = machine.Hull;
        server.AdvanceUptimeForTest(1.0);
        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        Assert.False(server.HeldBeamActiveForTest("Alice"));
        Assert.Equal(hull, machine.Hull);
        p.OtherSide = false;

        server.AdvanceUptimeForTest(1.0);
        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        Assert.True(server.HeldBeamActiveForTest("Alice"));
        p.State.Health = 0f;
        server.TickBeamWeaponsForTest("Alice");
        Assert.False(server.HeldBeamActiveForTest("Alice"));
    }

    [Fact]
    public void TheBeam_HasItsOwnShorterReach_AndNeedsAClearLine()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_reach", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var far = Machine(server, 130.5f, 100.5f);   // inside the shot's reach, outside the beam's
        var walled = Machine(server, 100.5f, 110.5f); // ten blocks south, behind a wall

        server.BeamWeaponForTest("Alice", TickAt(far.Id));
        Assert.Equal(1000f, far.Hull);
        Assert.Contains(t.To<ActionRejected>(p), r => r.Reason == "@srv.attack.out_of_reach");
        Assert.Equal(100f - 2.25f, p.State.SuitEnergy, 3); // the beam burned all the same

        var stone = Content.Value.GetBlock("stone")!.NumericId;
        for (int x = 99; x <= 101; x++)
        {
            for (int y = (int)Sky; y <= (int)Sky + 2; y++)
            {
                server.World.SetBlock(new Vector3i(x, y, 105), stone);
            }
        }

        server.AdvanceUptimeForTest(Tick);
        server.BeamWeaponForTest("Alice", TickAt(walled.Id, 0f, 0f, 1f));
        Assert.Equal(1000f, walled.Hull);
        Assert.Contains(t.To<ActionRejected>(p), r => r.Reason == "@srv.attack.no_line");

        // Held on: the same refusal is not repeated four times a second.
        for (int i = 0; i < 6; i++)
        {
            server.AdvanceUptimeForTest(Tick);
            server.BeamWeaponForTest("Alice", TickAt(walled.Id, 0f, 0f, 1f));
        }

        Assert.Single(t.To<ActionRejected>(p), r => r.Reason == "@srv.attack.no_line");
        Assert.Equal(1000f, walled.Hull);
    }

    [Fact]
    public void TheBeam_MustBeAimedAtItsTarget()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_aim", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 110.5f, 100.5f);

        server.BeamWeaponForTest("Alice", TickAt(machine.Id, -1f, 0f, 0f)); // looking the other way
        Assert.Equal(1000f, machine.Hull);
        Assert.Contains(t.To<ActionRejected>(p), r => r.Reason == "@srv.attack.not_ahead");

        // Without auto-aim the ray itself has to pass the body.
        server.RulesForTest.AutoAim = false;
        server.AdvanceUptimeForTest(Tick);
        server.BeamWeaponForTest("Alice", TickAt(machine.Id, 1f, 0f, 1f)); // 45° off
        Assert.Equal(1000f, machine.Hull);
        server.AdvanceUptimeForTest(Tick);
        server.BeamWeaponForTest("Alice", TickAt(machine.Id, 1f, 0f, 0f));
        Assert.Equal(1000f - 13.75f, machine.Hull, 2);
    }

    [Fact]
    public void AnotherWeapon_HasNoBeam()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_other", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f, held: "plasma_blaster");
        var machine = Machine(server, 110.5f, 100.5f);

        for (int i = 0; i < 4; i++)
        {
            server.BeamWeaponForTest("Alice", TickAt(machine.Id));
            server.AdvanceUptimeForTest(Tick);
        }

        Assert.False(server.HeldBeamActiveForTest("Alice"));
        Assert.Equal(1000f, machine.Hull);
        Assert.Equal(100f, p.State.SuitEnergy);

        // The client that asked is told to let go — once per hold, not once per tick it still sends.
        Assert.Single(BeamFxTo(t, p, FxActionKinds.WeaponBeamEnd));
        server.BeamWeaponForTest("Alice", new BeamWeaponIntent { Active = false });
        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        Assert.Equal(2, BeamFxTo(t, p, FxActionKinds.WeaponBeamEnd).Count); // a new hold, told again
    }

    /// <summary>A client that stops sending ticks without a release (it left its own "spent" state, or the release was
    /// lost) and holds again later: the server must tell that new hold too, or the client draws a beam that does nothing.</summary>
    [Fact]
    public void AHoldRefusedFromItsFirstTick_IsToldAgain_AlsoWithoutARelease()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_refused_again", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var machine = Machine(server, 110.5f, 100.5f);
        p.State.SuitEnergy = 10f; // below the reserve of 15: no tick is ever paid

        for (int i = 0; i < 4; i++)
        {
            server.BeamWeaponForTest("Alice", TickAt(machine.Id));
            server.AdvanceUptimeForTest(Tick);
        }

        Assert.False(server.HeldBeamActiveForTest("Alice"));
        Assert.Single(BeamFxTo(t, p, FxActionKinds.WeaponBeamEnd)); // once per hold, however many ticks it sends

        // No release arrives. Later the control is held again — still refused from its first tick.
        server.AdvanceUptimeForTest(BeamWeaponRules.TimeoutSeconds + 0.1);
        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        Assert.Equal(2, BeamFxTo(t, p, FxActionKinds.WeaponBeamEnd).Count);

        for (int i = 0; i < 3; i++)
        {
            server.AdvanceUptimeForTest(Tick);
            server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        }

        Assert.Equal(2, BeamFxTo(t, p, FxActionKinds.WeaponBeamEnd).Count); // and once for that hold as well
        Assert.Equal(1000f, machine.Hull);
        Assert.Equal(10f, p.State.SuitEnergy);
    }

    [Fact]
    public void TheBeam_NeverHurtsACompanion()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_pet", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        string wildId = server.SpawnCreatureAtForTest(new Vector3f(106.5f, Sky, 100.5f));
        var animal = server.Creatures.First(c => c.Id == wildId);
        animal.Hull = animal.HullMax = 400f;

        // A wild animal is hit like any target …
        server.BeamWeaponForTest("Alice", TickAt(wildId));
        Assert.Equal(400f - 13.75f, animal.Hull, 2);

        // … a tamed one never, and the tick still costs its energy.
        animal.OwnerId = "Alice";
        server.AdvanceUptimeForTest(Tick);
        server.BeamWeaponForTest("Alice", TickAt(wildId));
        Assert.Equal(400f - 13.75f, animal.Hull, 2);
        Assert.Contains(t.To<ActionRejected>(p), r => r.Reason == "@srv.attack.companion");
        Assert.Equal(100f - (2 * 2.25f), p.State.SuitEnergy, 3);
    }

    [Fact]
    public void TheBeam_SetsAPlantAlight_LikeTheShot()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_fire", t);
        var p = OnFoot(server, "Alice", 100.5f, 100.5f);
        var plant = new Vector3i(108, (int)Sky + 1, 100);
        server.World.SetBlock(plant, Content.Value.GetBlock("flora_bush")!.NumericId);
        ushort fire = Content.Value.GetBlock("fire")!.NumericId.Value;

        var far = new Vector3i(160, (int)Sky + 1, 100); // far past the beam's reach
        server.World.SetBlock(far, Content.Value.GetBlock("flora_bush")!.NumericId);
        server.BeamWeaponForTest("Alice", new BeamWeaponIntent { Active = true, DirX = 1f, HasCell = true, X = far.X, Y = far.Y, Z = far.Z });
        Assert.NotEqual(fire, server.World.GetBlock(far).Value);

        server.AdvanceUptimeForTest(Tick);
        server.BeamWeaponForTest("Alice", new BeamWeaponIntent { Active = true, DirX = 1f, HasCell = true, X = plant.X, Y = plant.Y, Z = plant.Z });
        Assert.Equal(fire, server.World.GetBlock(plant).Value);
        Assert.Equal(100f - (2 * 2.25f), p.State.SuitEnergy, 3); // a tick's energy each, nothing on top
    }

    [Fact]
    public void TheOthersNear_SeeEveryTick_AlongTheValidatedLine_NeverTheShooter()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_fx", t);
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 104.5f, 104.5f, held: "laser_pistol");
        var carol = OnFoot(server, "Carol", 300.5f, 100.5f, held: "laser_pistol"); // beyond the audience
        var machine = Machine(server, 110.5f, 100.5f);

        t.Sent.Clear();
        server.BeamWeaponForTest("Alice", TickAt(machine.Id));
        server.AdvanceUptimeForTest(Tick);
        server.BeamWeaponForTest("Alice", TickAt(string.Empty)); // the second tick meets nothing: the beam runs its full reach

        var seen = BeamFxTo(t, bob, FxActionKinds.WeaponBeam);
        Assert.Equal(2, seen.Count);
        Assert.All(seen, f => Assert.Equal(("Alice", Laser), (f.PlayerId, f.ItemKey)));
        Assert.True(seen[0].Hit);
        Assert.Equal(100.5f, seen[0].FromX, 2);
        Assert.Equal(Sky + 1.5f, seen[0].FromY, 2); // the shooter's eye
        Assert.Equal(110.5f, seen[0].ToX, 1);        // the machine
        Assert.False(seen[1].Hit);
        Assert.Equal(100.5f + LaserTool.BeamRange, seen[1].ToX, 1);
        Assert.Empty(BeamFxTo(t, alice, FxActionKinds.WeaponBeam));
        Assert.Empty(BeamFxTo(t, carol, FxActionKinds.WeaponBeam));

        // Released: the others hear it end; the shooter is not told what they did themselves.
        server.BeamWeaponForTest("Alice", new BeamWeaponIntent { Active = false });
        Assert.Single(BeamFxTo(t, bob, FxActionKinds.WeaponBeamEnd));
        Assert.Empty(BeamFxTo(t, alice, FxActionKinds.WeaponBeamEnd));
        Assert.False(FxActionKinds.IsValid(FxActionKinds.WeaponBeam)); // no client may send the beam's effect itself
        Assert.False(FxActionKinds.IsValid(FxActionKinds.WeaponBeamEnd));
    }

    [Fact]
    public void AnotherPlayer_IsOnlyHit_WhereTheWorldsPvpRulesAllowIt()
    {
        var t = new RecordingTransport();
        var server = NewServer("beam_pvp", t);
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 108.5f, 100.5f, held: "laser_pistol");
        string bobTarget = PvpRules.TargetId("Bob");

        server.BeamWeaponForTest("Alice", TickAt(bobTarget));
        Assert.Equal(100f, bob.State.Health); // PvP is off by default
        Assert.Equal(100f - 2.25f, alice.State.SuitEnergy, 3);

        server.RulesForTest.Pvp = PvpMode.On;
        server.ClearPvpProtectionForTest("Alice");
        server.ClearPvpProtectionForTest("Bob");
        server.AdvanceUptimeForTest(Tick);
        server.BeamWeaponForTest("Alice", TickAt(bobTarget));
        Assert.True(bob.State.Health < 100f, "with PvP on a beam tick hurts like a shot would");
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked save file must never fail the test run.
        }
    }
}
