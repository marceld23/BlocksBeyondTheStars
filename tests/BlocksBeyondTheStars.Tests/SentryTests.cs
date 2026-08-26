// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The base sentry post (#1214): it defends a base whose owner is home, it holds fire on anything a player
/// could still talk to, and it does nothing at all on a world without hostiles. The sentry is stateless —
/// its cells are re-derived from the same base-zone walk the settler stage uses — so every test here places
/// a real block and lets the scan find it.
/// </summary>
public sealed class SentryTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SentryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_sentry_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(out SqliteWorldRepository repo, Action<ServerConfig>? configure = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sentry_" + Guid.NewGuid().ToString("N")[..8]));
        var config = new ServerConfig
        {
            WorldName = "sentry",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Founds a base at the player's feet and stands a sentry post beside its core.</summary>
    private int FoundBaseWithSentry(SvGameServer server, PlayerSession owner, out Vector3i sentry)
    {
        var feet = owner.State.Position;
        var core = new Vector3i((int)Math.Floor(feet.X) + 3, (int)Math.Floor(feet.Y) + 4, (int)Math.Floor(feet.Z));
        server.PlaceBaseForTest(owner, core);
        int baseId = server.BaseSnapshots.Single(b => b.OwnerId == owner.State.PlayerId).Id;

        sentry = new Vector3i(core.X + 1, core.Y, core.Z);
        server.World.SetBlock(sentry, _content.GetBlock("sentry_post")!.NumericId, 0, 0, 0, owner.State.Name);
        Assert.Equal(1, server.SentryCountForTest(baseId));
        return baseId;
    }

    private static Vector3f Near(Vector3i cell, float dx) => new(cell.X + dx, cell.Y + 0.5f, cell.Z + 0.5f);

    // ---------------- content ----------------

    [Fact]
    public void TheSentryPost_IsACraftableMachineBlock()
    {
        _content.Validate();

        var block = _content.GetBlock("sentry_post");
        Assert.NotNull(block);
        Assert.Equal("machine", block!.Category); // counts toward the base settler, like the issue asks
        Assert.NotNull(_content.GetItem("sentry_post"));

        var recipe = _content.Recipes.Values.Single(r => r.Key == "sentry_post");
        Assert.Equal(CraftingStation.Workshop, recipe.Station);
        Assert.Equal("sentry_post", recipe.RequiredBlueprint);

        var bp = _content.Blueprints.Values.Single(b => b.Key == "sentry_post");
        Assert.Contains("heal_tank", bp.Prerequisites); // follows the base-machinery line
    }

    // ---------------- it defends ----------------

    [Fact]
    public void ASentry_DamagesAHostileMachineInRange_AndFinishesIt()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            owner.State.AboardShip = false;
            FoundBaseWithSentry(server, owner, out var sentry);

            server.SpawnPlanetEnemyAtForTest(Near(sentry, 4f));
            var enemy = server.PlanetEnemies.Single();
            float full = enemy.Hull;

            server.TickSentriesForTest();
            Assert.True(enemy.Hull < full, "the sentry should have hit it");

            // Keep firing until it falls — the enemy is gone from the world, not merely at zero hull.
            for (int i = 0; i < 20 && server.PlanetEnemies.Count > 0; i++)
            {
                server.TickSentriesForTest();
            }

            Assert.Empty(server.PlanetEnemies);
        }
    }

    [Fact]
    public void ASentry_IgnoresAHostileOutOfRange()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            owner.State.AboardShip = false;
            FoundBaseWithSentry(server, owner, out var sentry);

            server.SpawnPlanetEnemyAtForTest(Near(sentry, 40f)); // well beyond the 14-block reach
            float full = server.PlanetEnemies.Single().Hull;

            server.TickSentriesForTest();

            Assert.Equal(full, server.PlanetEnemies.Single().Hull);
        }
    }

    // ---------------- a kill is credited to the owner (#1292) ----------------

    [Fact]
    public void ASentryFinishingAScout_CreditsTheOwner_CounterAndMissionStep()
    {
        var server = Start(out var repo, c => c.Rules.BaseVisitors = true);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            owner.State.AboardShip = false;
            int baseId = FoundBaseWithSentry(server, owner, out var sentry);

            // A "guard the homestead" step with the real Defeat target, offered over the radio so the test
            // needs no mission board (the chain tests use the same shape).
            var def = new MissionDefinition
            {
                Id = "test_homestead_guard",
                Source = MissionSource.System,
                NameKey = "mission.homestead_guard.name",
                DescriptionKey = "mission.homestead_guard.desc",
                ChainId = "test_homestead",
                Step = 1,
                Surface = MissionChains.SurfaceRadio,
                Objectives = { new MissionObjective { Type = MissionObjectiveType.Defeat, Target = "base_scout", Required = 2 } },
                Active = true,
            };
            server.AddMissionDefForTest(def);
            server.AcceptMission("Homesteader", def.Id);
            var progress = owner.State.Missions.Single(m => m.MissionId == def.Id);

            Assert.True(server.SpawnScoutsForTest(baseId));
            var scout = server.Bandits.First(b => b.ScoutBaseId == baseId);

            // The owner hit it (#1224): it stands its ground and fights — and the sentry may now answer.
            scout.Position = Near(sentry, 4f);
            scout.Hostile = true;
            scout.BanditPhase = BanditPhase.Fighting;

            for (int i = 0; i < 64 && server.Bandits.Contains(scout); i++)
            {
                server.TickSentriesForTest();
            }

            Assert.DoesNotContain(scout, server.Bandits);

            // The turret's kill is the owner's credit: the counter and the mission step both move.
            var list = server.AchievementListForTest(owner);
            Assert.Equal(1, list.Counters.TryGetValue("base:defended", out int n) ? n : 0);
            Assert.Equal(1, progress.ObjectiveProgress[0]);
        }
    }

    [Fact]
    public void ASentryFinishingACampGuard_SpillsItsLoot_AndClearsTheCamp()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            owner.State.AboardShip = false;
            FoundBaseWithSentry(server, owner, out var sentry);

            // A one-guard camp somewhere out of the way; the guard itself has come to the base. Camp guards
            // spawn hostile and outside the talk phases, so they are always fair game for the turret.
            string campKey = server.SpawnBanditCampForTest(Near(sentry, 30f), guards: 1);
            var guard = server.Bandits.Single(b => b.CampKey == campKey);
            guard.Position = Near(sentry, 4f);
            Assert.False(server.BanditCampClearedForTest(campKey));

            for (int i = 0; i < 64 && server.Bandits.Contains(guard); i++)
            {
                server.TickSentriesForTest();
            }

            Assert.DoesNotContain(guard, server.Bandits);
            Assert.True(server.BanditCampClearedForTest(campKey), "the last guard fell — the camp is cleared, whoever fired");
            Assert.Contains(server.DropPackets, p => p.Items.Any(s => s.Item == "iron_plate" && s.Count == 2)); // loot on the ground, not lost
        }
    }

    // ---------------- it holds fire ----------------

    [Fact]
    public void ASentry_DoesNothingWhileTheOwnerIsAway()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            owner.State.AboardShip = false;
            FoundBaseWithSentry(server, owner, out var sentry);
            server.SpawnPlanetEnemyAtForTest(Near(sentry, 4f));
            var enemy = server.PlanetEnemies.Single();
            float full = enemy.Hull;

            // Owner logged off: a base does not run a private war while nobody is there to see it. (Enemies
            // only spawn 35–50 blocks from a player anyway, so this is also the honest situation.)
            owner.Joined = false;
            server.TickSentriesForTest();
            Assert.Equal(full, enemy.Hull);

            // Back online, the same sentry does its job.
            owner.Joined = true;
            server.TickSentriesForTest();
            Assert.True(enemy.Hull < full, "a sentry defends a base whose owner is home");
        }
    }

    [Fact]
    public void ASentry_NeverShootsOnAPeacefulWorld()
    {
        var server = Start(out var repo, c => c.Rules.GameMode = GameMode.Creative);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Builder");
            owner.State.AboardShip = false;
            FoundBaseWithSentry(server, owner, out var sentry);

            server.SpawnPlanetEnemyAtForTest(Near(sentry, 4f));
            float full = server.PlanetEnemies.Single().Hull;

            server.TickSentriesForTest();

            Assert.Equal(full, server.PlanetEnemies.Single().Hull);
        }
    }

    [Fact]
    public void ASentry_HoldsFireOnARobberWhoIsStillTalking()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            owner.State.AboardShip = false;
            FoundBaseWithSentry(server, owner, out var sentry);

            server.SpawnPlanetEnemyAtForTest(Near(sentry, 4f), CombatEntityKind.Bandit);
            var robber = server.PlanetEnemies.Single();
            float full = robber.Hull;

            // Approach and Demanding are the talk phases: the hold-up is a conversation the player answers
            // first (#1043), and a turret must not decide it for them.
            foreach (var phase in new[] { BanditPhase.Approach, BanditPhase.Demanding, BanditPhase.Leaving, BanditPhase.Scouting }) // #1224: a scout is looking, not fighting
            {
                robber.BanditPhase = phase;
                server.TickSentriesForTest();
                Assert.Equal(full, robber.Hull);
            }

            // Once they actually fight, the sentry answers.
            robber.BanditPhase = BanditPhase.Fighting;
            server.TickSentriesForTest();
            Assert.True(robber.Hull < full, "a fighting robber is a valid target");
        }
    }

    [Fact]
    public void ASentry_NeverShootsACompanion()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            owner.State.AboardShip = false;
            FoundBaseWithSentry(server, owner, out var sentry);

            // A tame creature standing right next to the turret: not hostile, so never a target.
            server.SpawnPlanetEnemyAtForTest(Near(sentry, 2f));
            var pet = server.PlanetEnemies.Single();
            pet.Hostile = false;
            float full = pet.Hull;

            server.TickSentriesForTest();

            Assert.Equal(full, pet.Hull);
        }
    }

    [Fact]
    public void NoSentryBlock_MeansNoShooting()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            owner.State.AboardShip = false;
            var feet = owner.State.Position;
            var core = new Vector3i((int)Math.Floor(feet.X) + 3, (int)Math.Floor(feet.Y) + 4, (int)Math.Floor(feet.Z));
            server.PlaceBaseForTest(owner, core);
            int baseId = server.BaseSnapshots.Single().Id;
            Assert.Equal(0, server.SentryCountForTest(baseId));

            server.SpawnPlanetEnemyAtForTest(new Vector3f(core.X + 2f, core.Y + 0.5f, core.Z + 0.5f));
            float full = server.PlanetEnemies.Single().Hull;

            server.TickSentriesForTest();

            Assert.Equal(full, server.PlanetEnemies.Single().Hull);
        }
    }
}
