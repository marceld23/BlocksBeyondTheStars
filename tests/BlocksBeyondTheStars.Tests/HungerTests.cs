// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Hunger / eating survival loop: hunger drains outside the ship, starves health at 0, is sated
/// aboard the ship, and is refilled by eating food (creature meat) or edible plants (berries).
/// </summary>
public sealed class HungerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public HungerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_hunger_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "hunger"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "hunger",
            Seed = 99,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Resets a fresh player's starter food to empty so a test can establish its own precondition.
    /// The starter kit now seeds berries + pre-loaded dispenser rations (option A of the starvation fix), which
    /// would otherwise auto-feed and mask the bare hunger mechanics these tests exercise.</summary>
    private static void ClearStarterFood(BlocksBeyondTheStars.Shared.State.PlayerState s)
    {
        for (int i = 0; i < s.RationStore.SlotCount; i++)
        {
            s.RationStore.SetSlot(i, null);
        }

        s.Inventory.Remove("berries", s.Inventory.CountOf("berries"));
        s.Inventory.Remove("emergency_ration", s.Inventory.CountOf("emergency_ration"));
    }

    [Fact]
    public void Rule_HungerEnabled_OnlyInSurvival()
    {
        Assert.True(new GameRules { GameMode = GameMode.Survival, Hunger = true }.HungerEnabled);
        Assert.False(new GameRules { GameMode = GameMode.Creative, Hunger = true }.HungerEnabled);
        Assert.False(new GameRules { GameMode = GameMode.Survival, Hunger = false }.HungerEnabled);
    }

    [Fact]
    public void Hunger_DrainsOutsideShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Wanderer");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(10.0);
            Assert.True(p.State.Hunger < 100f, "Hunger should drain outside the ship.");
        }
    }

    [Fact]
    public void Hunger_DoesNotDrainAboardShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            p.State.AboardShip = true; // life support — sated (no stamped ship, so UpdateAboard won't override)

            server.Tick(10.0);
            Assert.Equal(100f, p.State.Hunger);
        }
    }

    [Fact]
    public void Starvation_DamagesHealth_WhenHungerEmpty()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Starving");
            ClearStarterFood(p.State); // no fallback rations — we want the empty-hunger health drain itself
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Hunger = 0f;
            p.State.Health = 100f;

            server.Tick(5.0);
            Assert.True(p.State.Health < 100f, "Empty hunger should cost health.");
        }
    }

    [Fact]
    public void EatingMeat_RestoresHunger()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eater");
            p.State.Hunger = 40f;
            p.State.Inventory.Add("creature_meat", 1, 20);

            server.ConsumeItem("Eater", "creature_meat"); // +30 hunger
            Assert.Equal(70f, p.State.Hunger);
        }
    }

    [Fact]
    public void EatingBerries_FromPlants_RestoresHunger()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Forager");
            p.State.Hunger = 40f;
            p.State.Inventory.Add("berries", 1, 20);

            server.ConsumeItem("Forager", "berries"); // +18 hunger
            Assert.Equal(58f, p.State.Hunger);
        }
    }

    [Fact]
    public void EmergencyRation_AutoFeeds_WhenHungerLow()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Starver");
            ClearStarterFood(p.State); // start empty so the loose inventory ration is the only food
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Hunger = 10f; // below the auto-feed threshold
            p.State.Inventory.Add("emergency_ration", 1, 20);

            server.Tick(1.0);

            Assert.Equal(0, p.State.Inventory.CountOf("emergency_ration")); // ration consumed
            Assert.True(p.State.Hunger > 40f, "Eating the ration should top up hunger.");
        }
    }

    [Fact]
    public void NoRation_NoAutoFeed()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Starver");
            ClearStarterFood(p.State); // genuinely nothing to eat — neither dispenser nor loose ration
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Hunger = 10f;

            server.Tick(1.0);
            Assert.True(p.State.Hunger <= 10f); // still hungry, nothing to eat
        }
    }

    [Fact]
    public void EmergencyRation_NotEaten_WhenHungerHigh()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Fed");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Hunger = 80f; // well above the threshold
            p.State.Inventory.Add("emergency_ration", 1, 20);

            server.Tick(1.0);
            Assert.Equal(1, p.State.Inventory.CountOf("emergency_ration")); // not consumed
        }
    }

    [Fact]
    public void RationDispenser_LoadsFood_AndAutoFeedsFromIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Spacer");
            ClearStarterFood(p.State); // empty dispenser + no starter berries, so we control exactly what's stocked
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Inventory.Add("berries", 3, 20);

            server.LoadRation("Spacer", "berries", 3); // stock the dispenser
            Assert.Equal(0, p.State.Inventory.CountOf("berries"));
            Assert.Equal(3, p.State.RationStore.CountOf("berries"));

            p.State.Hunger = 10f; // low → the dispenser should feed
            server.Tick(1.0);

            Assert.Equal(2, p.State.RationStore.CountOf("berries")); // one dispensed
            Assert.True(p.State.Hunger > 18f, "Dispenser food should top up hunger.");
        }
    }

    [Fact]
    public void RationDispenser_RejectsNonFood()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Spacer");
            p.State.Inventory.Add("iron_ore", 5, 99);

            server.LoadRation("Spacer", "iron_ore", 5);

            Assert.Equal(5, p.State.Inventory.CountOf("iron_ore")); // not moved
            Assert.Equal(0, p.State.RationStore.CountOf("iron_ore"));
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
