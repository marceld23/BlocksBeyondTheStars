using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Lootable containers: a player's death drops a salvage capsule holding their carried items, and
/// another player standing next to it can loot the contents — the capsule despawns once emptied.
/// Generalises the M10 salvage capsule into a lootable corpse/container (combat loot).
/// </summary>
public sealed class ContainerLootTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ContainerLootTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_loot_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "loot"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "loot",
            Seed = 3,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceVaults = false, // these tests assert exact container counts — no vault loot in the world
            Rules = new GameRules { DeathPenalty = DeathPenalty.Normal, KeepInventoryOnDeath = false },
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Kills a player at their current position and ticks so the death + capsule drop resolves.</summary>
    private static void DieAt(SvGameServer server, BlocksBeyondTheStars.Shared.State.PlayerState p, Vector3f where)
    {
        p.AboardShip = false; // outside the ship → no health regen, so death sticks
        p.Position = where;
        p.Health = 0f;
        server.Tick(0.1);
    }

    [Fact]
    public void Death_DropsLootableCapsule_WithCarriedItems()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var victim = server.AddLocalPlayer("Victim");
            victim.State.Inventory.Add("iron_ore", 8, 99);
            DieAt(server, victim.State, new Vector3f(10, 64, 10));

            Assert.Single(server.Containers);
            var capsule = server.Containers[0];
            Assert.Equal("salvage_capsule", capsule.Kind);
            Assert.Contains(capsule.Items, s => s.Item == "iron_ore" && s.Count == 8);
            Assert.Equal(0, victim.State.Inventory.CountOf("iron_ore")); // dropped on death
        }
    }

    [Fact]
    public void Looting_TransfersItems_AndDespawnsEmptyCapsule()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var victim = server.AddLocalPlayer("Victim");
            victim.State.Inventory.Add("iron_ore", 8, 99);
            DieAt(server, victim.State, new Vector3f(10, 64, 10));
            var capsuleId = server.Containers[0].Id;

            var looter = server.AddLocalPlayer("Looter");
            looter.State.Position = new Vector3f(10, 64, 10); // standing on the capsule

            server.LootContainer("Looter", capsuleId);

            Assert.Equal(8, looter.State.Inventory.CountOf("iron_ore")); // looted
            Assert.Empty(server.Containers);                              // capsule despawned
        }
    }

    [Fact]
    public void Looting_OutOfRange_IsRejected()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var victim = server.AddLocalPlayer("Victim");
            victim.State.Inventory.Add("iron_ore", 8, 99);
            DieAt(server, victim.State, new Vector3f(10, 64, 10));
            var capsuleId = server.Containers[0].Id;

            var looter = server.AddLocalPlayer("Looter");
            looter.State.Position = new Vector3f(100, 64, 100); // far away

            server.LootContainer("Looter", capsuleId);

            Assert.Equal(0, looter.State.Inventory.CountOf("iron_ore")); // nothing taken
            Assert.Single(server.Containers);                            // capsule remains
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
