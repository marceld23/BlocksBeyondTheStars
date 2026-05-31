using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>
/// Player-to-player trading: both sides stage an offer and confirm; only when both confirm does the
/// server atomically swap the items. Changing an offer voids the ready flags, offering items you
/// don't have is rejected, and partners must be next to each other.
/// </summary>
public sealed class TradeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TradeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_trade_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "trade"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "trade", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static (Spacecraft.Shared.State.PlayerState a, Spacecraft.Shared.State.PlayerState b) TwoTradersTogether(SvGameServer server)
    {
        var a = server.AddLocalPlayer("Alice").State;
        var b = server.AddLocalPlayer("Bob").State;
        // Trade personal inventories (not aboard → MaterialPool excludes the shared ship cargo).
        a.AboardShip = false;
        b.AboardShip = false;
        a.Position = new Vector3f(0, 64, 0);
        b.Position = new Vector3f(1, 64, 0); // within trade range
        a.Inventory.Add("iron_ore", 10, 99);
        b.Inventory.Add("carbon", 5, 99);
        return (a, b);
    }

    private static void OpenTrade(SvGameServer server)
    {
        server.RequestTrade("Alice", "Bob");
        server.RespondTrade("Bob", true);
    }

    [Fact]
    public void Trade_SwapsItems_WhenBothConfirm()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (a, b) = TwoTradersTogether(server);
            OpenTrade(server);
            Assert.NotNull(server.ActiveTrade("Alice"));

            server.SetTradeOffer("Alice", new[] { new ItemAmount("iron_ore", 4) });
            server.SetTradeOffer("Bob", new[] { new ItemAmount("carbon", 2) });
            server.ConfirmTrade("Alice");
            server.ConfirmTrade("Bob"); // both ready → commit

            Assert.Null(server.ActiveTrade("Alice"));
            Assert.Equal(6, a.Inventory.CountOf("iron_ore"));
            Assert.Equal(2, a.Inventory.CountOf("carbon"));
            Assert.Equal(3, b.Inventory.CountOf("carbon"));
            Assert.Equal(4, b.Inventory.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void Trade_DoesNotSwap_UntilBothConfirm()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (a, b) = TwoTradersTogether(server);
            OpenTrade(server);
            server.SetTradeOffer("Alice", new[] { new ItemAmount("iron_ore", 4) });
            server.SetTradeOffer("Bob", new[] { new ItemAmount("carbon", 2) });

            server.ConfirmTrade("Alice"); // only one side

            Assert.NotNull(server.ActiveTrade("Alice")); // still open
            Assert.Equal(10, a.Inventory.CountOf("iron_ore")); // nothing moved
            Assert.Equal(5, b.Inventory.CountOf("carbon"));
        }
    }

    [Fact]
    public void ChangingOffer_ResetsBothConfirms()
    {
        var server = Started(out var repo);
        using (repo)
        {
            TwoTradersTogether(server);
            OpenTrade(server);
            server.SetTradeOffer("Alice", new[] { new ItemAmount("iron_ore", 4) });
            server.ConfirmTrade("Alice");
            Assert.True(server.ActiveTrade("Alice")!.ConfirmA);

            server.SetTradeOffer("Bob", new[] { new ItemAmount("carbon", 1) }); // change voids readiness

            var t = server.ActiveTrade("Alice")!;
            Assert.False(t.ConfirmA);
            Assert.False(t.ConfirmB);
        }
    }

    [Fact]
    public void OfferingItemsYouDontHave_IsRejected()
    {
        var server = Started(out var repo);
        using (repo)
        {
            TwoTradersTogether(server);
            OpenTrade(server);

            server.SetTradeOffer("Alice", new[] { new ItemAmount("iron_ore", 999) }); // only has 10

            Assert.Empty(server.ActiveTrade("Alice")!.OfferA); // offer not accepted
        }
    }

    [Fact]
    public void Trade_RejectedWhenPartnersTooFarApart()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (_, b) = TwoTradersTogether(server);
            b.Position = new Vector3f(100, 64, 100); // out of range

            server.RequestTrade("Alice", "Bob");
            server.RespondTrade("Bob", true);

            Assert.Null(server.ActiveTrade("Alice"));
        }
    }

    [Fact]
    public void Cancel_ClosesTheTrade()
    {
        var server = Started(out var repo);
        using (repo)
        {
            TwoTradersTogether(server);
            OpenTrade(server);
            Assert.NotNull(server.ActiveTrade("Bob"));

            server.CancelTrade("Alice");

            Assert.Null(server.ActiveTrade("Alice"));
            Assert.Null(server.ActiveTrade("Bob"));
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
