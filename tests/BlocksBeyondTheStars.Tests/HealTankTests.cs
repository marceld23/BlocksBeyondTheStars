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
/// The heal tank (issue #460): the placeable life-support unit for player bases and stations. Standing
/// near a placed tank slowly restores health and hunger and recharges the suit — the ONLY way the suit
/// recharges off-ship. Stateless like the algae tank: the voxel is the whole machine. Blueprint-gated
/// (research), unlike the deliberately-free algae tank.
/// </summary>
public sealed class HealTankTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public HealTankTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_healtank_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "healtank"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "healtank", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void HealTank_ContentIsWired()
    {
        // Block + placing item exist and reference each other.
        Assert.NotNull(_content.GetBlock("heal_tank"));
        var item = _content.GetItem("heal_tank");
        Assert.NotNull(item);
        Assert.Equal("heal_tank", item!.PlacesBlock);

        // Building the tank is a workshop recipe gated behind the research blueprint.
        var build = _content.Recipes["heal_tank"];
        Assert.Equal(CraftingStation.Workshop, build.Station);
        Assert.Equal("heal_tank", build.RequiredBlueprint);
        Assert.NotNull(_content.GetBlueprint("heal_tank"));
    }

    [Fact]
    public void HealTank_Craft_IsBlueprintGated()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            // A workbench next to the player provides the workshop station off-ship.
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("workbench")!.NumericId);

            // Recipe inputs in the pocket — but the blueprint is not researched yet: craft must fail.
            foreach (var input in _content.Recipes["heal_tank"].Inputs)
            {
                p.State.Inventory.Add(input.Item, input.Count, 99);
            }

            server.Craft("Builder", "heal_tank");
            Assert.Equal(0, p.State.Inventory.CountOf("heal_tank"));

            // Research it (materials + knowledge threshold), then the same craft succeeds.
            var bp = _content.GetBlueprint("heal_tank")!;
            foreach (var cost in bp.UnlockCost)
            {
                p.State.Inventory.Add(cost.Item, cost.Count, 99);
            }

            p.State.KnowledgePoints = bp.KnowledgeCost;
            server.UnlockBlueprint(p.State.PlayerId, "heal_tank");
            Assert.Contains("heal_tank", p.State.UnlockedBlueprints);

            server.Craft("Builder", "heal_tank");
            Assert.Equal(1, p.State.Inventory.CountOf("heal_tank"));
        }
    }

    [Fact]
    public void HealTank_RegeneratesVitals_NearThePlacedTank()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Settler");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Health = 50f;
            p.State.Hunger = 50f;
            p.State.SuitEnergy = 10f;

            // Without a tank the suit NEVER recharges off-ship — the baseline for the assertion below.
            server.TickForTest(1.0);
            Assert.Equal(10f, p.State.SuitEnergy, 3);

            // Place a tank two blocks away → all three vitals rise.
            server.World.SetBlock(new Vector3i(2, 64, 0), _content.GetBlock("heal_tank")!.NumericId);
            float health = p.State.Health, hunger = p.State.Hunger;
            server.TickForTest(2.0);

            Assert.True(p.State.SuitEnergy > 10f, $"suit energy should recharge near the tank (was {p.State.SuitEnergy})");
            Assert.True(p.State.Health > health, $"health should regenerate near the tank (was {p.State.Health})");
            Assert.True(p.State.Hunger > hunger, $"hunger should be sated near the tank (was {p.State.Hunger})");
        }
    }

    [Fact]
    public void HealTank_Proximity_HasLimitedRange()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ranger");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            server.World.SetBlock(new Vector3i(4, 64, 0), _content.GetBlock("heal_tank")!.NumericId);

            Assert.True(server.NearHealTankForTest(p.State.PlayerId));

            p.State.Position = new Vector3f(20, 64, 0); // far outside the regen field
            Assert.False(server.NearHealTankForTest(p.State.PlayerId));
        }
    }

    [Fact]
    public void HealTank_DoesNotTrickleHeal_ADownedPlayer()
    {
        // A dead player next to a tank must go through the normal death → respawn flow, not be
        // quietly trickle-healed in place: after the tick they are back at the respawn point with
        // reset vitals (the tank's regen never outruns the death check).
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Downed");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("heal_tank")!.NumericId);

            p.State.Health = 0f;
            server.TickForTest(0.1);

            Assert.Equal(100f, p.State.Health); // full respawn reset, not 0 + a trickle
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
        }
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
