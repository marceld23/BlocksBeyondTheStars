using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Suit teleporter: recalls the player to their ship (respawn point) if they carry the device, are
/// charged and off cooldown. Without the device it does nothing; a second use is blocked until the
/// cooldown elapses.
/// </summary>
public sealed class TeleportTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TeleportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tp_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tp"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "tp", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Teleport_RecallsToShip_WithDevice()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Spacer");
            p.State.RespawnPoint = new Vector3f(5, 70, 5);
            p.State.Position = new Vector3f(200, 64, 200); // far away
            p.State.AboardShip = false;
            p.State.SuitEnergy = 100f;
            p.State.Inventory.Add("suit_teleporter", 1, 1);

            server.TeleportToShip("Spacer");

            Assert.Equal(p.State.RespawnPoint.X, p.State.Position.X);
            Assert.Equal(p.State.RespawnPoint.Z, p.State.Position.Z);
            Assert.True(p.State.AboardShip);
            Assert.True(p.State.SuitEnergy < 100f); // energy spent
        }
    }

    [Fact]
    public void Teleport_DoesNothing_WithoutDevice()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Spacer");
            p.State.RespawnPoint = new Vector3f(5, 70, 5);
            p.State.Position = new Vector3f(200, 64, 200);

            server.TeleportToShip("Spacer");

            Assert.Equal(200f, p.State.Position.X); // unchanged
        }
    }

    [Fact]
    public void Teleport_OnCooldown_UntilItRecharges()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Spacer");
            p.State.RespawnPoint = new Vector3f(5, 70, 5);
            p.State.AboardShip = false;
            p.State.SuitEnergy = 100f;
            p.State.Inventory.Add("suit_teleporter", 1, 1);

            server.TeleportToShip("Spacer"); // first use OK
            p.State.Position = new Vector3f(200, 64, 200); // walk away again

            server.TeleportToShip("Spacer"); // still on cooldown
            Assert.Equal(200f, p.State.Position.X); // not recalled

            server.Tick(31.0); // cooldown (30s) elapses
            server.TeleportToShip("Spacer");
            Assert.Equal(p.State.RespawnPoint.X, p.State.Position.X); // recalled again
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
