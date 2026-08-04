// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Bounty missions (#730/#731): a settlement board on a world with an uncleared bandit camp offers a
/// "drive the bandits out" bounty (accepting reveals the camp on the planet map, clearing completes it
/// for every holder), and a station board in pirate space offers a raider-ship bounty whose held state
/// guarantees the next flight's ambush roll.
/// </summary>
public sealed class BountyMissionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BountyMissionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bounty_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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

    private SvGameServer Start(string save, long seed, Action<GameRules>? configure, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, save));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = save,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
            PlaceBanditCamps = false, // deterministic tests spawn their own camps (seed-worldgen camps stay out)
        };
        config.Rules.PlanetEnemies = AlienActivity.Off; // no machines wandering into the fight
        config.Rules.Bandits = AlienActivity.Normal;
        configure?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Finds a world whose settlement has a mission board (seed-search pattern).</summary>
    private SvGameServer StartedWithBoard(out SqliteWorldRepository repo, out long seed, string savePrefix = "bounty", Action<GameRules>? configure = null)
    {
        for (seed = 1; seed <= 60; seed++)
        {
            var server = Start(savePrefix + seed, seed, configure, out repo);
            if (server.SettlementMissionIds.Count > 0)
            {
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No settlement with a mission board found across 60 seeds.");
    }

    private static Vector3f BoardPos(SvGameServer server)
        => server.SettlementMarkers.First(m => m.Type == "mission_board").Pos;

    /// <summary>Steps up to every guard of the camp and puts it down (the last one clears the camp).</summary>
    private static void ClearCamp(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession fighter)
    {
        foreach (var guard in server.Bandits.ToList())
        {
            fighter.State.Position = guard.Position;
            for (int i = 0; i < 10 && server.Bandits.Contains(guard); i++)
            {
                server.AttackEntity(fighter.State.PlayerId, guard.Id);
            }
        }
    }

    // ---------------- Settlement camp bounty (#730) ----------------

    [Fact]
    public void CampBounty_IsOffered_WhileTheCampStands_AndDropsWhenCleared()
    {
        var server = StartedWithBoard(out var repo, out _);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hunter");
            p.State.Position = BoardPos(server);

            // No camp → no bounty on the board.
            Assert.DoesNotContain(server.AvailableBoardMissions("Hunter"), id => id.Contains("_bounty_"));

            string key = server.SpawnBanditCampForTest(new Vector3f(
                p.State.Position.X + 60f, p.State.Position.Y, p.State.Position.Z), guards: 2);
            var bountyId = Assert.Single(server.AvailableBoardMissions("Hunter"), id => id.Contains("_bounty_"));
            Assert.EndsWith("_bounty_" + key, bountyId);
            Assert.False(string.IsNullOrEmpty(server.MissionGiverName(bountyId)), "the bounty names its giver");

            // Clearing the camp takes the bounty off the board again.
            ClearCamp(server, p);
            p.State.Position = BoardPos(server);
            Assert.DoesNotContain(server.AvailableBoardMissions("Hunter"), id => id.Contains("_bounty_"));
        }
    }

    [Fact]
    public void AcceptingTheCampBounty_RevealsTheCampOnTheMap_ClearedDropsItAgain()
    {
        var server = StartedWithBoard(out var repo, out _);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hunter");
            p.State.Position = BoardPos(server);
            server.SpawnBanditCampForTest(new Vector3f(
                p.State.Position.X + 60f, p.State.Position.Y, p.State.Position.Z), guards: 2);

            // Camps are discovery content until the quest giver marks the target.
            Assert.DoesNotContain(server.PlanetPoisForTest(p.State.PlayerId), poi => poi.Type == "bandit_camp");

            var bountyId = server.AvailableBoardMissions("Hunter").First(id => id.Contains("_bounty_"));
            server.AcceptMission("Hunter", bountyId);
            Assert.Contains(p.State.Missions, m => m.MissionId == bountyId && m.Status == MissionStatus.Active);
            Assert.Contains(server.PlanetPoisForTest(p.State.PlayerId), poi => poi.Type == "bandit_camp");

            // A cleared camp is no threat any more — the marker goes away on its own.
            ClearCamp(server, p);
            Assert.DoesNotContain(server.PlanetPoisForTest(p.State.PlayerId), poi => poi.Type == "bandit_camp");
        }
    }

    [Fact]
    public void ClearingTheCamp_CompletesTheBounty_AndTurnInPays()
    {
        var server = StartedWithBoard(out var repo, out _);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hunter");
            p.State.Position = BoardPos(server);
            server.SpawnBanditCampForTest(new Vector3f(
                p.State.Position.X + 60f, p.State.Position.Y, p.State.Position.Z), guards: 2);
            var bountyId = server.AvailableBoardMissions("Hunter").First(id => id.Contains("_bounty_"));
            server.AcceptMission("Hunter", bountyId);

            ClearCamp(server, p);
            var pr = p.State.Missions.First(m => m.MissionId == bountyId);
            Assert.Equal(1, pr.ObjectiveProgress[0]); // the clear completed the Defeat objective

            int platesBefore = p.State.Inventory.CountOf("titanium_plate");
            int goldBefore = p.State.Inventory.CountOf("gold_ingot");
            p.State.Position = BoardPos(server);
            server.TurnInMission("Hunter", bountyId);

            Assert.Equal(MissionStatus.TurnedIn, pr.Status);
            Assert.True(p.State.Inventory.CountOf("titanium_plate") >= platesBefore + 3, "the bounty pays titanium plates");
            Assert.True(p.State.Inventory.CountOf("gold_ingot") >= goldBefore + 1, "the bounty pays gold");
        }
    }

    [Fact]
    public void CampClearedByAnotherPlayer_CompletesEveryHoldersBounty()
    {
        var server = StartedWithBoard(out var repo, out _);
        using (repo)
        {
            var holder = server.AddLocalPlayer("Holder");
            holder.State.Position = BoardPos(server);
            server.SpawnBanditCampForTest(new Vector3f(
                holder.State.Position.X + 60f, holder.State.Position.Y, holder.State.Position.Z), guards: 2);
            var bountyId = server.AvailableBoardMissions("Holder").First(id => id.Contains("_bounty_"));
            server.AcceptMission("Holder", bountyId);
            holder.State.Position = new Vector3f(2000, 64, 2000); // far away while the buddy fights

            var buddy = server.AddLocalPlayer("Buddy");
            ClearCamp(server, buddy);

            var pr = holder.State.Missions.First(m => m.MissionId == bountyId);
            Assert.Equal(1, pr.ObjectiveProgress[0]); // co-op: whoever lands the last blow, the crew wins
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void HeldCampBounty_SurvivesARestart()
    {
        var server = StartedWithBoard(out var repo, out var seed, savePrefix: "restart");
        string bountyId;
        using (repo)
        {
            var p = server.AddLocalPlayer("Hunter");
            p.State.Position = BoardPos(server);
            server.SpawnBanditCampForTest(new Vector3f(
                p.State.Position.X + 60f, p.State.Position.Y, p.State.Position.Z), guards: 2);
            bountyId = server.AvailableBoardMissions("Hunter").First(id => id.Contains("_bounty_"));
            server.AcceptMission("Hunter", bountyId);
            server.Stop(); // saves the player + the persisted bounty def
        }

        var reloaded = Start("restart" + seed, seed, null, out var repo2);
        using (repo2)
        {
            // The def was persisted at accept time, so the held mission is turn-in-able after a reload
            // even though the (test) camp is gone and no one re-coined the board.
            Assert.False(string.IsNullOrEmpty(reloaded.MissionGiverName(bountyId)), "the bounty def survives a restart");
            var p = reloaded.AddLocalPlayer("Hunter");
            Assert.Contains(p.State.Missions, m => m.MissionId == bountyId && m.Status == MissionStatus.Active);
        }
    }

    // ---------------- Station raider bounty (#731) ----------------

    private SvGameServer SpaceStarted(string save, long seed, Action<GameRules>? configure, out SqliteWorldRepository repo)
        => Start(save, seed, r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.ShipWeapons = ShipWeaponMode.NpcsOnly;
            r.SpaceNpcEnemies = AlienActivity.Off; // no drones muddying the instance
            r.AlienUfos = AlienActivity.Off;
            configure?.Invoke(r);
        }, out repo);

    /// <summary>Finds a seed whose start body sits in pirate space (the ambush gate needs it).</summary>
    private SvGameServer StartedInPirateSpace(out SqliteWorldRepository repo, out string pilotId)
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            var server = SpaceStarted("pirate" + seed, seed, null, out repo);
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.AboardShip = true;
            // A fresh ship still carries the planet TYPE as its location (never travelled); point it at the
            // real start body so the instance resolves to a star system, as it does after any real flight.
            server.Ship.CurrentLocationId = pilot.CurrentLocationId;
            server.EnterSpace("Pilot");
            pilotId = pilot.State.PlayerId;
            string systemId = server.SpaceSystemIdForTest(pilotId);
            if (systemId.Length > 0 && server.BanditSystemForTest(systemId))
            {
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No pirate-space start system found across 80 seeds.");
    }

    [Fact]
    public void ShipBounty_NeverOffered_WhenThePlayerCannotShootBack()
    {
        // The unkillable-UFO lesson applies to quests too: no bounty for a fight the player can't win.
        var server = SpaceStarted("gated", 5, r => r.ShipWeapons = ShipWeaponMode.Off, out var repo);
        using (repo)
        {
            Assert.False(server.ShipBountyOfferedForTest("any_station"));
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void HeldShipBounty_GuaranteesTheAmbush()
    {
        var server = StartedInPirateSpace(out var repo, out var pilotId);
        using (repo)
        {
            server.GrantShipBountyForTest(pilotId);

            // The ambush dice roll once per flight; with a held bounty they must come up raider. Full
            // ticks (not just the bandit-ship sub-tick) so the server clock reaches the warp-in delay.
            for (int i = 0; i < 200 && server.BanditShipForTest(pilotId) is null; i++)
            {
                server.Tick(1.0);
            }

            Assert.NotNull(server.BanditShipForTest(pilotId));
        }
    }

    [Fact]
    public void DefeatingTheRaider_CompletesTheShipBounty()
    {
        var server = SpaceStarted("raiderkill", 5, null, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.AboardShip = true;
            server.Ship.Modules.Add("ship_cannon_1"); // 20 dmg vs hull 55 → 3 shots
            server.Ship.Modules.Remove("tractor_beam");
            server.EnterSpace("Pilot");
            string bountyId = server.GrantShipBountyForTest(pilot.State.PlayerId);
            server.SpawnBanditShipForTest("Pilot");

            var raider = server.BanditShipForTest("Pilot");
            Assert.NotNull(raider);
            server.ShipMove("Pilot", raider!.Position.X, raider.Position.Y, raider.Position.Z);
            for (int i = 0; i < 5 && server.BanditShipForTest("Pilot") is not null; i++)
            {
                server.FireWeapon("Pilot", "ship_cannon_1", raider.Id);
                server.TickForTest(1.1); // cannon cooldown
            }

            Assert.Null(server.BanditShipForTest("Pilot"));
            var pr = pilot.State.Missions.First(m => m.MissionId == bountyId);
            Assert.Equal(1, pr.ObjectiveProgress[0]);
        }
    }

    // ---------------- Localization ----------------

    [Fact]
    public void EveryBountyLocaleKey_ExistsInBothLanguages()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");

        var required = new[]
        {
            "mission.bounty.camp.title", "mission.bounty.camp.desc",
            "mission.bounty.ship.title", "mission.bounty.ship.desc",
            "poi.bandit_camp",
        };
        foreach (var key in required)
        {
            Assert.True(en.ContainsKey(key), $"missing EN locale key: {key}");
            Assert.True(de.ContainsKey(key), $"missing DE locale key: {key}");
        }
    }
}
