using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Market trading is pure resource barter (no currency): "market" recipes are give→get trade
/// offers executed through the crafting path, available at the ship's trade console (aboard).
/// </summary>
public sealed class MarketTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MarketTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_market_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "market"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "market", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Market_BartersResources_WhenAboard()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Trader"); // aboard by default
            p.State.Inventory.Add("iron_ore", 5, 99);

            server.Craft("Trader", "market_iron_to_titanium"); // give 5 iron -> get 1 titanium

            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(1, p.State.Inventory.CountOf("titanium_ore"));
        }
    }

    [Fact]
    public void Market_RequiresBeingAboard()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Trader");
            p.State.AboardShip = false; // away from the trade console
            p.State.Inventory.Add("iron_ore", 5, 99);

            server.Craft("Trader", "market_iron_to_titanium");

            Assert.Equal(5, p.State.Inventory.CountOf("iron_ore")); // nothing traded
            Assert.Equal(0, p.State.Inventory.CountOf("titanium_ore"));
        }
    }

    [Fact]
    public void Market_Fails_WithoutTheResources()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Trader"); // aboard, but without the trade goods

            server.Craft("Trader", "market_iron_to_titanium"); // needs 5 iron_ore (has none)

            Assert.Equal(0, p.State.Inventory.CountOf("titanium_ore")); // no trade happened
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
