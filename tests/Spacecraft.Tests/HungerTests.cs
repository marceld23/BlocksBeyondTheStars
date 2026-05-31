using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

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
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_hunger_" + Guid.NewGuid().ToString("N"));
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
