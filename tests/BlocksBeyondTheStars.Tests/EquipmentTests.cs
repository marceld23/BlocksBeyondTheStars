// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Suit equipment effects (carried gear): stealth (creatures/enemies ignore you), armor (damage
/// resistance), a bigger oxygen tank (higher max), and the advanced scanner (more knowledge). The
/// mining beam is a tier-3 tool. Defaults give Survival + planet enemies, used for the combat cases.
/// </summary>
public sealed class EquipmentTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public EquipmentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_equip_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = planet, Seed = 4242, StartPlanet = planet, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start(); // default rules: Survival + PlanetEnemies Normal
        return server;
    }

    [Fact]
    public void StealthSuit_HidesPlayerFromEnemies()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ghost");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Stealthed = true;

            server.Tick(6.0); // spawn an enemy nearby
            server.Tick(3.0); // it would damage a visible player
            Assert.Equal(100f, p.State.Health); // cloaked → untouched
        }
    }

    [Fact]
    public void Armor_ReducesIncomingDamage()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var armored = server.AddLocalPlayer("Tank").State;
            var soft = server.AddLocalPlayer("Soft").State;
            foreach (var s in new[] { armored, soft })
            {
                s.AboardShip = false;
                s.Position = new Vector3f(0, 64, 0);
                s.Health = 100f;
            }

            armored.Inventory.Add("armor_chest", 1, 1);
            armored.Inventory.Add("helmet", 1, 1);

            server.Tick(6.0); // spawn
            // Enemies spawn a short distance away on the surface; put both players on it so both are in range.
            var enemy = server.PlanetEnemies[0];
            foreach (var s in new[] { armored, soft })
            {
                s.Position = new Vector3f(enemy.Position.X, enemy.Position.Y, enemy.Position.Z);
            }

            server.Tick(3.0); // damage both

            Assert.True(soft.Health < 100f, "The unarmored player should take damage.");
            Assert.True(armored.Health > soft.Health, "Armor should reduce the damage taken.");
        }
    }

    [Fact]
    public void LargeOxygenTank_RaisesMaxOxygen()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Diver");
            p.State.AboardShip = true; // life support regenerates
            p.State.Oxygen = 100f;
            p.State.Inventory.Add("oxygen_tank_2", 1, 1); // +100 max

            server.Tick(3.0);
            Assert.True(p.State.Oxygen > 100f, "The bigger tank should let oxygen exceed 100.");
        }
    }

    [Fact]
    public void OxygenTanks_DoNotStack_HighestTierWins()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Diver");
            p.State.AboardShip = true; // life support regenerates up to the suit maximum
            p.State.Oxygen = 100f;
            p.State.Inventory.Add("oxygen_tank_1", 1, 1); // +50
            p.State.Inventory.Add("oxygen_tank_2", 1, 1); // +100 — only this (the best) should count

            // Regen aboard ship (~25/s) for long enough to saturate at the suit maximum, then read the cap.
            server.Tick(20.0);

            // Max = 100 + best bonus (100) = 200. If the bonuses stacked it would saturate at 250.
            Assert.True(p.State.Oxygen > 150f, "The +100 tank should apply (oxygen well above 150).");
            Assert.True(p.State.Oxygen <= 201f, "Tanks must not stack — the cap should be 200, not 250.");
        }
    }

    [Fact]
    public void AdvancedScanner_GrantsMoreKnowledge()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Pro");
            var b = server.AddLocalPlayer("Rookie");
            a.State.Inventory.Add("advanced_scanner", 1, 1);

            var speciesId = server.SpeciesRoster.First().Id;
            var pro = server.ScanSubject("Pro", "creature", speciesId);
            var rookie = server.ScanSubject("Rookie", "creature", speciesId);

            Assert.True(pro.KnowledgeGained > rookie.KnowledgeGained, "The advanced scanner should earn more knowledge.");
        }
    }

    [Fact]
    public void MiningBeam_IsTopTierTool()
    {
        var beam = _content.GetItem("mining_beam")!;
        Assert.Equal(ToolKind.Drill, beam.Tool!.Kind);
        Assert.True(beam.Tool.Tier >= 3);
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
