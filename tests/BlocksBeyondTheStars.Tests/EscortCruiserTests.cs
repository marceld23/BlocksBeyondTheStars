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
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The escort cruiser (#3900) — the space boss: its arrival from hyperspace with VEGA's warning, the gates of the rare
/// ambush (and that the Guardian's defeat is none of them), the mega laser (charge, hit, the three ways to make it miss,
/// the cap that keeps it from ending a flight in one hit), the second phase with its drones, and the jump away when
/// nobody stays to fight. The generic boss bar (#3901) is asserted along the way.
/// </summary>
public sealed class EscortCruiserTests : IDisposable
{
    private const string Pilot = "Pilot";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly List<SvGameServer> _servers = new();

    public EscortCruiserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_cruiser_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A server with an armed pilot in space over the start body: PvE on, ship weapons allowed, the space
    /// hostiles on (no UFOs), and — unless <paramref name="story"/> — no world story.</summary>
    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Pilot) InSpace(string name,
        Action<GameRules>? configure = null, bool story = false, bool armed = true)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = name, Seed = 1, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE;
        config.Rules.ShipWeapons = ShipWeaponMode.NpcsOnly;
        config.Rules.SpaceNpcEnemies = AlienActivity.Normal;
        config.Rules.AlienUfos = AlienActivity.Off;
        config.Rules.Bandits = AlienActivity.Off;
        if (!story)
        {
            config.Rules.StoryId = StoryRegistry.NoneStoryId;
        }

        configure?.Invoke(config.Rules);
        var transport = new RecordingTransport();
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _servers.Add(server);
        var pilot = server.AddLocalPlayer(Pilot);
        server.Ship.Modules.Add("jump_generator");
        if (armed)
        {
            server.Ship.Modules.Add("ship_cannon_1");
        }

        server.EnterSpace(Pilot);
        Assert.True(server.InSpace(Pilot));
        server.ShipMove(Pilot, 0f, 0f, 0f);
        return (server, transport, pilot);
    }

    /// <summary>Jumps the flying pilot into another star system that is not desolate (the ambush never comes at home).</summary>
    private void JumpAway(SvGameServer server, PlayerSession pilot)
    {
        string origin = server.Galaxy.FindBody(pilot.CurrentLocationId)!.SystemId;
        var target = server.Galaxy.Systems.First(s => s.Id != origin
            && server.SystemArchetypeForTest(s.Id) != SystemArchetype.Desolate
            && s.Bodies.Any(b => !string.IsNullOrEmpty(b.PlanetType) && _content.GetPlanet(b.PlanetType!) is not null));
        server.HyperjumpToSystem(Pilot, target.Id);
        Assert.True(server.InSpace(Pilot));
        Assert.Equal(target.Id, server.Galaxy.FindBody(pilot.CurrentLocationId)!.SystemId);
        server.ShipMove(Pilot, 0f, 0f, 0f);
    }

    private static void Run(SvGameServer server, double seconds, double step = 0.1)
    {
        for (double t = 0; t < seconds - 1e-9; t += step)
        {
            server.Tick(step);
        }
    }

    /// <summary>The boss, standing almost still at <paramref name="x"/> on the pilot's axis with its mega laser due — so
    /// a test decides the distance, and the next tick in range starts the charge.</summary>
    private static CombatEntity BossAt(SvGameServer server, float x)
    {
        var boss = server.SpawnEscortCruiserForTest(Pilot, at: new Vector3f(x, 0f, 0f))!;
        boss.Boss!.Speed = 0.001f;
        Arm(boss);
        return boss;
    }

    /// <summary>Makes the boss's next mega laser due at once.</summary>
    private static void Arm(CombatEntity boss)
    {
        boss.HeavyContact = true;
        boss.HeavyNextAt = 0.0001;
    }

    /// <summary>Ticks until the boss charges (true) — at most <paramref name="seconds"/>.</summary>
    private static bool RunUntilCharging(SvGameServer server, CombatEntity boss, double seconds = 3.0)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.Tick(0.1);
            if (server.HeavyChargeLeftForTest(Pilot, boss.Id) > 0)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- the data ----------------

    [Fact]
    public void TheBoss_IsTheCruiserMadeBigger_AndItsMegaLaserIsData()
    {
        var cruiser = _content.Enemy(EnemyCatalog.Cruiser).Tier(EnemyTiers.Normal);
        var boss = _content.Enemy(EnemyCatalog.EscortCruiser).Tier(EnemyTiers.Normal);
        Assert.True(boss.Boss);
        Assert.True(boss.Hull > cruiser.Hull * 2f && boss.Shield > cruiser.Shield);
        Assert.True(boss.HeavyShotDamage > cruiser.HeavyShotDamage, "the mega laser is the heavy shot made bigger");
        Assert.True(boss.HeavyShotCharge > SpaceCombatRules.HeavyChargeSeconds, "…with a longer warning");
        Assert.True(boss.HeavyShotRange > SpaceCombatRules.EngageRange, "…and a longer reach");
        Assert.InRange(boss.HeavyShotMaxHullShare, 0.01f, 0.4f);
        Assert.Equal(SpaceCombatRules.HeavyStyleMegaLaser, boss.HeavyShotStyle);
        Assert.True(boss.EnrageHeavyShotInterval < boss.HeavyShotInterval && boss.EnrageSpeed > 1f && boss.Reinforcements == 2);
        Assert.False(cruiser.Boss); // the gauntlet's cruiser stays what it was
        Assert.Equal(0f, cruiser.HeavyShotMaxHullShare);
    }

    [Theory]
    [InlineData(60f, 100f, 100f, 40f)]  // cut to 40 % of the full hull
    [InlineData(60f, 30f, 100f, 29f)]   // above a quarter of the hull: the last point stays
    [InlineData(60f, 25f, 100f, 40f)]   // at a quarter or below nothing is held back: more than is left, the flight ends
    [InlineData(10f, 100f, 100f, 10f)]  // a small rest is not raised
    [InlineData(0f, 100f, 100f, 0f)]    // the shield took it all
    public void TheCap_NeverTakesMoreThanItsShare_NorTheLastPointFromAboveAQuarter(float onHull, float hull, float hullMax, float expected)
    {
        Assert.Equal(expected, SpaceCombatRules.HeavyHullDamage(onHull, hull, hullMax, 0.4f), 3);
        Assert.Equal(onHull, SpaceCombatRules.HeavyHullDamage(onHull, hull, hullMax, 0f), 3); // no cap: the classic shot
    }

    [Fact]
    public void TheAmbushChance_IsZeroWhenOff_GrowsWithTheSlider_AndIsCapped()
    {
        Assert.Equal(0.0, EscortCruiserRules.AmbushChance(AlienActivity.Off, 1.0));
        Assert.Equal(0.0, EscortCruiserRules.AmbushChance(AlienActivity.Extreme, 0.0)); // Danger off
        double last = 0.0;
        foreach (var level in new[] { AlienActivity.Rare, AlienActivity.Normal, AlienActivity.Frequent, AlienActivity.Extreme })
        {
            double chance = EscortCruiserRules.AmbushChance(level, 1.0);
            Assert.True(chance > last, "monotone in the slider");
            last = chance;
        }

        Assert.True(EscortCruiserRules.AmbushChance(AlienActivity.Extreme, 5.0) <= EscortCruiserRules.AmbushChanceCap);
    }

    // ---------------- arrival ----------------

    [Fact]
    public void ASummon_WarnsFirst_ThenTheBossComesOutOfHyperspace_FarFromThePilot()
    {
        var (server, transport, pilot) = InSpace("summon", story: true);
        Assert.Null(server.SpaceBossForTest(Pilot));

        Assert.True(server.SummonEscortCruiser(Pilot, carriesEcho: true));
        Assert.Contains("vega.cruiser.incoming", transport.LineKeys(pilot));
        Assert.Null(server.SpaceBossForTest(Pilot)); // the warning comes first
        Assert.InRange(server.SpaceBossArrivalInForTest(Pilot), EscortCruiserRules.WarnSeconds - 0.5, EscortCruiserRules.WarnSeconds);
        Assert.False(server.SummonEscortCruiser(Pilot, carriesEcho: false)); // one boss per flight

        Run(server, EscortCruiserRules.WarnSeconds - 1.0);
        Assert.Null(server.SpaceBossForTest(Pilot));
        Run(server, 1.5);

        var boss = server.SpaceBossForTest(Pilot);
        Assert.NotNull(boss);
        Assert.Equal(EnemyCatalog.EscortCruiser, boss!.EnemyKey);
        Assert.Equal(CombatEntityKind.Cruiser, boss.Kind); // existing clients draw a (big) cruiser
        Assert.Equal(EscortCruiserRules.ModelScale, boss.Scale);
        Assert.True(boss.Boss!.CarriesEcho);
        Assert.True(boss.Hostile);
        float dist = (float)System.Math.Sqrt(boss.Position.DistanceSquared(new Vector3f(0f, 0f, 0f)));
        Assert.True(dist >= EscortCruiserRules.WarpInMinDistance, $"it arrives far away (was {dist:0})");
        Assert.True(dist <= EscortCruiserRules.NoticeRange, "…but inside its notice range, so it comes for the pilot");

        var fx = Assert.Single(transport.To<SpaceWarpFx>(pilot), f => f.Style == EscortCruiserRules.WarpStyle);
        Assert.True(fx.Arriving);
        var status = transport.To<BossStatus>(pilot).First();
        Assert.Equal(boss.Id, status.Id);
        Assert.Equal(EscortCruiserRules.NameKey, status.NameKey);
        Assert.Equal(BossSources.Space, status.Source);
        Assert.True(status.Active);
        Assert.Equal(EscortCruiserRules.PhaseNormal, status.Phase);
        Assert.Equal(1f, status.Health, 3);
        Assert.Equal(1f, status.Shield, 3);
        Assert.False(server.SummonEscortCruiser(Pilot, carriesEcho: false)); // it lives: no second one

        // The bar keeps coming about once a second while the fight is on (a late joiner hears it).
        int before = transport.To<BossStatus>(pilot).Count;
        Run(server, 3.0);
        Assert.InRange(transport.To<BossStatus>(pilot).Count - before, 2, 7);
    }

    [Theory]
    [InlineData("combat")]
    [InlineData("enemies")]
    [InlineData("weapons")]
    public void ASummon_IsRefused_WhereTheRulesAllowNoSpaceBoss(string off)
    {
        var (server, _, _) = InSpace("summon_off_" + off, r =>
        {
            if (off == "combat")
            {
                r.SpaceCombat = SpaceCombatMode.Off;
            }
            else if (off == "enemies")
            {
                r.SpaceNpcEnemies = AlienActivity.Off;
            }
            else
            {
                r.ShipWeapons = ShipWeaponMode.MiningOnly;
            }
        });
        Assert.False(server.SpaceBossesAllowedForTest);
        Assert.False(server.SummonEscortCruiser(Pilot, carriesEcho: false));
        Run(server, EscortCruiserRules.WarnSeconds + 1.0, 0.5);
        Assert.Null(server.SpaceBossForTest(Pilot));
    }

    // ---------------- the rare ambush ----------------

    [Fact]
    public void TheAmbush_NeverComesInTheStartSystem_ButElsewhereItCan()
    {
        var (server, _, pilot) = InSpace("ambush_home");
        Assert.False(server.RollEscortCruiserForTest(Pilot)); // at home: never, whatever the dice

        JumpAway(server, pilot);
        Assert.True(server.RollEscortCruiserForTest(Pilot, 0.0));
        double eta = server.SpaceBossArrivalInForTest(Pilot);
        Assert.InRange(eta, EscortCruiserRules.AmbushDelayMin - 0.5, EscortCruiserRules.AmbushDelayMax);
        Assert.False(server.RollEscortCruiserForTest(Pilot, 0.9)); // the dice decide: the chance is small and capped
        Assert.False(server.RollEscortCruiserForTest(Pilot, EscortCruiserRules.AmbushChanceCap));
    }

    [Theory]
    [InlineData("combat")]
    [InlineData("enemies")]
    [InlineData("weapons")]
    [InlineData("unarmed")]
    public void TheAmbush_NeverComesInAPeacefulWorld_NorToAnUnarmedShip(string off)
    {
        var (server, _, pilot) = InSpace("ambush_off_" + off, r =>
        {
            if (off == "combat")
            {
                r.SpaceCombat = SpaceCombatMode.Off;
            }
            else if (off == "enemies")
            {
                r.SpaceNpcEnemies = AlienActivity.Off;
            }
            else if (off == "weapons")
            {
                r.ShipWeapons = ShipWeaponMode.MiningOnly;
            }
        }, armed: off != "unarmed");
        if (off == "unarmed")
        {
            server.Ship.Modules.RemoveAll(m => ShipWeaponRules.IsWeapon(_content.GetShipModule(m)));
        }

        JumpAway(server, pilot);
        Assert.False(server.RollEscortCruiserForTest(Pilot, 0.0));
    }

    [Fact]
    public void TheAmbush_WaitsForTheStorysChapter()
    {
        var (server, _, pilot) = InSpace("ambush_story", story: true);
        JumpAway(server, pilot);
        Assert.False(server.RollEscortCruiserForTest(Pilot, 0.0)); // the story has only begun

        var late = new[] { EscortCruiserRules.AmbushFromChapter, "core", "resolution" };
        for (int i = 0; i < 400 && !late.Contains(server.WorldChapterIdForTest); i++)
        {
            server.AdminAdvanceStory(1);
        }

        Assert.Equal(EscortCruiserRules.AmbushFromChapter, server.WorldChapterIdForTest);
        Assert.True(server.RollEscortCruiserForTest(Pilot, 0.0));
    }

    [Fact]
    public void AfterTheFinale_ItCanStillCome_TheCruisersNeverHeardTheShutdown()
    {
        var (server, _, pilot) = InSpace("ambush_remnant", story: true);
        JumpAway(server, pilot);
        Assert.False(server.RollEscortCruiserForTest(Pilot, 0.0)); // before the chapter

        server.MarkGuardianDefeatedForTest();
        Assert.True(server.RollEscortCruiserForTest(Pilot, 0.0)); // the win is no gate — unlike the ambient waves (#1206)
    }

    [Fact]
    public void OneBossPerFlight_NoRollWhileOneLivesOrComes()
    {
        var (server, _, pilot) = InSpace("ambush_one");
        JumpAway(server, pilot);
        Assert.NotNull(server.SpawnEscortCruiserForTest(Pilot));
        Assert.False(server.RollEscortCruiserForTest(Pilot, 0.0));
        Assert.False(server.SummonEscortCruiser(Pilot, carriesEcho: false));
        Assert.Null(server.SpawnEscortCruiserForTest(Pilot)); // even the seam puts no second one beside it
        Assert.Single(server.SpaceEntitiesFor(Pilot), e => e.Boss is not null);
    }

    [Fact]
    public void ARolledAmbush_Warns_ThenArrives()
    {
        var (server, transport, pilot) = InSpace("ambush_arrives", story: true);
        JumpAway(server, pilot);
        server.MarkGuardianDefeatedForTest();
        Assert.True(server.RollEscortCruiserForTest(Pilot, 0.0));
        double eta = server.SpaceBossArrivalInForTest(Pilot);

        Run(server, eta - EscortCruiserRules.WarnSeconds - 2.0, 0.5);
        Assert.DoesNotContain("vega.cruiser.incoming", transport.LineKeys(pilot));
        Run(server, 4.0, 0.5);
        Assert.Contains("vega.cruiser.incoming", transport.LineKeys(pilot));
        Assert.Null(server.SpaceBossForTest(Pilot));
        Run(server, EscortCruiserRules.WarnSeconds, 0.5);
        Assert.NotNull(server.SpaceBossForTest(Pilot));
        Assert.False(server.SpaceBossForTest(Pilot)!.Boss!.CarriesEcho); // only the story's carries ECHO
    }

    // ---------------- the mega laser ----------------

    [Fact]
    public void TheMegaLaser_ChargesWithAnAimingLine_ThenHits_ButNeverEndsAFlightInOneHit()
    {
        var (server, transport, pilot) = InSpace("mega_hit");
        var tier = _content.Enemy(EnemyCatalog.EscortCruiser).Tier(EnemyTiers.Normal);
        var boss = BossAt(server, 90f); // outside its steady guns (70), inside the mega laser's range
        server.Ship.Shield = 0f;
        float full = server.HullMaxForTest;
        server.Ship.Hull = full;

        Assert.True(RunUntilCharging(server, boss));
        var charge = transport.To<SpaceHeavyShot>(pilot).Last();
        Assert.Equal(SpaceCombatRules.HeavyPhaseCharge, charge.Phase);
        Assert.Equal(boss.Id, charge.EntityId);
        Assert.Equal(Pilot, charge.TargetPlayerId);
        Assert.Equal(SpaceCombatRules.HeavyStyleMegaLaser, charge.Style);
        Assert.Equal(tier.HeavyShotCharge, charge.Seconds, 3);
        Assert.Equal(90f, charge.OriginX, 0);  // from the barrel …
        Assert.Equal(0f, charge.TargetX, 0);   // … to the target
        Assert.Equal(tier.HeavyShotRange * SpaceCombatRules.HeavyReachFactor, charge.Range, 2);

        Run(server, tier.HeavyShotCharge - 0.5);
        Assert.Equal(full, server.Ship.Hull, 3); // still charging: nothing has landed
        Run(server, 1.0);
        var shot = transport.To<SpaceHeavyShot>(pilot).Last();
        Assert.Equal(SpaceCombatRules.HeavyPhaseHit, shot.Phase);
        float lost = full - server.Ship.Hull;
        Assert.True(lost > 0f, "the mega laser lands on a ship that neither rolls nor flies clear");
        Assert.True(lost <= full * tier.HeavyShotMaxHullShare + 0.01f, $"never more than its share of the full hull (lost {lost:0.#} of {full:0})");

        // From just above a quarter of the hull the next hit leaves one point: the flight goes on.
        server.Ship.Hull = full * 0.3f;
        server.Ship.Shield = 0f;
        Arm(boss);
        Assert.True(RunUntilCharging(server, boss));
        Run(server, tier.HeavyShotCharge + 0.5);
        Assert.True(server.InSpace(Pilot));
        Assert.Equal(1f, server.Ship.Hull, 2);
    }

    [Fact]
    public void ABarrelRoll_FlyingClear_OrARockInTheWay_MakeTheMegaLaserMiss()
    {
        var tier = _content.Enemy(EnemyCatalog.EscortCruiser).Tier(EnemyTiers.Normal);

        // The roll, just before it fires.
        var (server, transport, pilot) = InSpace("mega_roll");
        var boss = BossAt(server, 90f);
        var heard = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.ShipEvaded, heard);
        Assert.True(RunUntilCharging(server, boss));
        float before = server.Ship.Hull + server.Ship.Shield;
        while (server.HeavyChargeLeftForTest(Pilot, boss.Id) > 0.35)
        {
            server.Tick(0.1);
        }

        Assert.True(server.ShipManeuver(Pilot, ShipManeuverKinds.Roll));
        Run(server, 0.6);
        Assert.Equal(SpaceCombatRules.HeavyPhaseMiss, transport.To<SpaceHeavyShot>(pilot).Last().Phase);
        Assert.True(server.Ship.Hull + server.Ship.Shield >= before - 0.001f, "a roll at the right moment: no damage");
        Assert.Single(heard.Events, e => e.Arg == GameEventKeys.EvadedHeavy);

        // Flying clear of its reach while it charges.
        (server, transport, pilot) = InSpace("mega_clear");
        boss = BossAt(server, 90f);
        Assert.True(RunUntilCharging(server, boss));
        before = server.Ship.Hull + server.Ship.Shield;
        server.ShipMove(Pilot, -200f, 0f, 0f);
        Run(server, tier.HeavyShotCharge + 0.5);
        var miss = transport.To<SpaceHeavyShot>(pilot).Last();
        Assert.Equal(SpaceCombatRules.HeavyPhaseMiss, miss.Phase);
        Assert.False(miss.Blocked);
        Assert.True(server.Ship.Hull + server.Ship.Shield >= before - 0.001f);

        // Behind a rock: the beam ends there.
        (server, transport, pilot) = InSpace("mega_rock");
        boss = BossAt(server, 90f);
        var rock = server.SpaceEntitiesFor(Pilot).First(e => e.Kind == CombatEntityKind.Asteroid);
        rock.Position = new Vector3f(45f, 1f, 0f); // between the two, a step off the line
        Assert.True(RunUntilCharging(server, boss));
        before = server.Ship.Hull + server.Ship.Shield;
        Run(server, tier.HeavyShotCharge + 0.5);
        var blocked = transport.To<SpaceHeavyShot>(pilot).Last();
        Assert.Equal(SpaceCombatRules.HeavyPhaseMiss, blocked.Phase);
        Assert.True(blocked.Blocked);
        Assert.Equal(45f, blocked.TargetX, 0);
        Assert.True(server.Ship.Hull + server.Ship.Shield >= before - 0.001f);
    }

    // ---------------- the second phase ----------------

    [Fact]
    public void AtHalfHull_ItEnragesOnce_FliesFaster_ShootsMoreOften_AndCallsItsDronesOnce()
    {
        var (server, transport, pilot) = InSpace("enrage", story: true);
        var tier = _content.Enemy(EnemyCatalog.EscortCruiser).Tier(EnemyTiers.Normal);
        var boss = server.SpawnEscortCruiserForTest(Pilot, at: new Vector3f(200f, 0f, 0f))!;
        Run(server, 0.5);
        Assert.False(boss.Boss!.Enraged);
        Assert.Equal(EscortCruiserRules.Speed, boss.Boss.Speed);
        Assert.DoesNotContain(server.SpaceEntitiesFor(Pilot), e => e.CalledByBoss);

        boss.Hull = boss.HullMax * 0.49f;
        Run(server, 0.3);
        Assert.True(boss.Boss.Enraged);
        Assert.Equal(EscortCruiserRules.Speed * tier.EnrageSpeed, boss.Boss.Speed, 3);
        Assert.Equal(tier.EnrageHeavyShotInterval, boss.HeavyInterval, 3);
        Assert.Equal(tier.Reinforcements, server.SpaceEntitiesFor(Pilot).Count(e => e.CalledByBoss));
        Assert.All(server.SpaceEntitiesFor(Pilot).Where(e => e.CalledByBoss), d => Assert.Equal(EnemyCatalog.SpaceDrone, d.EnemyKey));
        Assert.Contains("vega.cruiser.enraged", transport.LineKeys(pilot));
        Assert.Contains(transport.To<BossStatus>(pilot), s => s.Phase == EscortCruiserRules.PhaseEnraged && s.Active);

        // Healed above the mark and hurt again: no second call.
        boss.Hull = boss.HullMax * 0.9f;
        Run(server, 0.3);
        boss.Hull = boss.HullMax * 0.2f;
        Run(server, 0.3);
        Assert.True(boss.Boss.Enraged);
        Assert.Equal(tier.Reinforcements, server.SpaceEntitiesFor(Pilot).Count(e => e.CalledByBoss));
        Assert.Single(transport.Lines(pilot, "vega.cruiser.enraged"));
    }

    // ---------------- getting away ----------------

    [Fact]
    public void LeftAlone_ItJumpsAway_AndThatFlightNeverRollsAgain()
    {
        var (server, transport, pilot) = InSpace("leave", story: true);
        pilot.State.GodMode = true; // nothing hunts this pilot — the patience rule counts every pilot all the same
        float leave = EscortCruiserRules.NoticeRange * EscortCruiserRules.LeaveRangeFactor;
        var boss = server.SpawnEscortCruiserForTest(Pilot, at: new Vector3f(leave - 60f, 0f, 0f))!;
        Run(server, EscortCruiserRules.LeaveSeconds + 5.0, 0.5);
        Assert.Same(boss, server.SpaceBossForTest(Pilot)); // a pilot stays within twice its notice range: it stays

        server.ShipMove(Pilot, -3000f, 0f, 0f);
        Run(server, EscortCruiserRules.LeaveSeconds - 3.0, 0.5);
        Assert.Same(boss, server.SpaceBossForTest(Pilot));
        Run(server, 5.0, 0.5);

        Assert.Null(server.SpaceBossForTest(Pilot));
        Assert.True(server.InSpace(Pilot)); // the pilot simply got away
        Assert.Contains(transport.To<SpaceWarpFx>(pilot), f => f.Style == EscortCruiserRules.WarpStyle && !f.Arriving);
        var last = transport.To<BossStatus>(pilot).Last();
        Assert.False(last.Active);
        Assert.False(last.Defeated);
        Assert.Contains("vega.cruiser.gone", transport.LineKeys(pilot));

        Run(server, 30.0, 1.0);
        Assert.Equal(0.0, server.SpaceBossArrivalInForTest(Pilot)); // no new roll in this flight
        Assert.Null(server.SpaceBossForTest(Pilot));
    }

    [Fact]
    public void AGodModePilot_IsNeverHunted()
    {
        var (server, _, pilot) = InSpace("godmode");
        pilot.State.GodMode = true;
        var boss = server.SpawnEscortCruiserForTest(Pilot, at: new Vector3f(60f, 0f, 0f))!;
        Arm(boss);
        float before = server.Ship.Hull + server.Ship.Shield;
        Run(server, 6.0);
        Assert.Equal(before, server.Ship.Hull + server.Ship.Shield, 3);
        Assert.Equal(0.0, server.HeavyChargeLeftForTest(Pilot, boss.Id)); // it does not even take aim
    }

    // ---------------- the admin verb ----------------

    [Fact]
    public void TheAdminVerb_SummonsItIntoTheAdminsFlight_AndSaysWhyNot()
    {
        var (server, transport, pilot) = InSpace("admin", r =>
        {
            r.AdminCheats = true;
            r.AllowCheatsInSurvival = true;
        });
        pilot.State.Role = Shared.State.PlayerRole.WorldAdmin;

        server.HandleForTest(pilot, new AdminCommandIntent { Command = "summon_cruiser", StringArg = "echo" });
        Assert.Contains("@srv.cruiser.summoned", transport.TextsTo(pilot));
        Assert.True(server.SpaceBossArrivalInForTest(Pilot) > 0);

        server.HandleForTest(pilot, new AdminCommandIntent { Command = "summon_cruiser" });
        Assert.Contains(transport.To<ActionRejected>(pilot), r => r.Reason == "@srv.cruiser.already");

        Run(server, EscortCruiserRules.WarnSeconds + 0.5, 0.5);
        Assert.True(server.SpaceBossForTest(Pilot)!.Boss!.CarriesEcho);
    }

    [Fact]
    public void EveryLineOfTheBoss_ExistsInEnglishAndGerman()
    {
        string[] keys =
        {
            "enemy.escort_cruiser.name", "enemy.escort_cruiser.desc", "enemy.escort_cruiser.tip",
            "vega.cruiser.incoming", "vega.cruiser.enraged", "vega.cruiser.gone", "vega.cruiser.down",
            "srv.cruiser.not_flying", "srv.cruiser.rules_off", "srv.cruiser.already", "srv.cruiser.summoned",
            "srv.space.ally_ship_down", "ui.space.mega_laser_warn", "ui.admin.help_cruiser",
        };
        foreach (string locale in new[] { "en", "de" })
        {
            var table = TestLocales.Load(locale);
            Assert.All(keys, key => Assert.True(table.ContainsKey(key), $"{locale}: {key}"));
        }
    }
}
