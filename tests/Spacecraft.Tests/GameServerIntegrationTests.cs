using Spacecraft.GameServer;
using Spacecraft.Networking;
using Spacecraft.Networking.Messages;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

public sealed class GameServerIntegrationTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GameServerIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_it_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private ServerConfig Config() => new()
    {
        WorldName = "it",
        Seed = 123456,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999, // never auto-save during the test
        ViewDistanceChunks = 1,
        MaxPlayers = 4,
        PlaceStarterShip = false, // these tests assume bare terrain at the spawn column
    };

    private static void JoinAndDrain(SvGameServer server, LoopbackClientTransport client, string name)
    {
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1); // process connect + join
        client.Poll();    // drain server replies
    }

    [Fact]
    public void Join_Mine_InventoryGrows_AndPersistsAcrossReload()
    {
        Vector3i target;
        string dropItem;

        // --- First session: join, mine a block, stop (which saves). ---
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "it")))
        using (var serverTransport = new LoopbackServerTransport(NewLink(out var link)))
        using (var client = new LoopbackClientTransport(link))
        {
            var server = new SvGameServer(Config(), _content, serverTransport, repo);
            server.Start();
            JoinAndDrain(server, client, "Tester");

            var session = server.Sessions[1];
            Assert.True(session.Joined);

            // Find the topmost solid block in the spawn column.
            int topY = (int)System.Math.Floor(session.State.Position.Y);
            target = default;
            bool found = false;
            for (int y = topY; y > topY - 12; y--)
            {
                var pos = new Vector3i(0, y, 0);
                if (!server.World.GetBlock(pos).IsAir)
                {
                    target = pos;
                    found = true;
                    break;
                }
            }

            Assert.True(found, "Expected to find a solid block near spawn.");

            var blockDef = server.World.Definition(server.World.GetBlock(target))!;
            dropItem = blockDef.Drops[0].Item;
            int before = session.State.Inventory.CountOf(dropItem);

            client.Send(NetCodec.Encode(new MineBlockIntent { X = target.X, Y = target.Y, Z = target.Z }),
                DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            Assert.True(server.World.GetBlock(target).IsAir, "Block should be air after mining.");
            Assert.Equal(before + 1, session.State.Inventory.CountOf(dropItem));

            server.Stop(); // saves world edits + player
        }

        SqliteConnectionReset();

        // --- Second session: reload and verify the edit + inventory survived. ---
        using (var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, "it")))
        using (var serverTransport2 = new LoopbackServerTransport(NewLink(out var link2)))
        using (var client2 = new LoopbackClientTransport(link2))
        {
            var server2 = new SvGameServer(Config(), _content, serverTransport2, repo2);
            server2.Start();

            // World edit persisted via block_edit table (applied on chunk load).
            Assert.True(server2.World.GetBlock(target).IsAir, "Mined block should still be air after reload.");

            // Player inventory persisted.
            JoinAndDrain(server2, client2, "Tester");
            Assert.Equal(1, server2.Sessions[1].State.Inventory.CountOf(dropItem));
        }
    }

    [Fact]
    public void Craft_ConsumesInputs_AndProducesOutput()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "craft"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);

        var config = Config();
        config.WorldName = "craft";
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Smith");

        var player = server.Sessions[1].State;
        player.Inventory.Add("iron_ore", 4, 99); // workshop recipe: 2 ore -> 1 ingot

        client.Send(NetCodec.Encode(new CraftIntent { RecipeKey = "iron_ingot", Count = 2 }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(0, player.Inventory.CountOf("iron_ore"));
        Assert.Equal(2, player.Inventory.CountOf("iron_ingot"));
    }

    [Fact]
    public void Craft_Rejected_WhenBlueprintNotUnlocked()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "bp"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);

        CraftResult? result = null;
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is CraftResult r)
            {
                result = r;
            }
        };

        var config = Config();
        config.WorldName = "bp";
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Eng");

        var player = server.Sessions[1].State;
        // Provide all materials for the titanium drill, but do NOT unlock its blueprint.
        player.Inventory.Add("titanium_plate", 6, 99);
        player.Inventory.Add("cable", 4, 99);
        player.Inventory.Add("energy_cell_1", 2, 99);

        client.Send(NetCodec.Encode(new CraftIntent { RecipeKey = "titanium_drill" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(0, player.Inventory.CountOf("titanium_drill"));
    }

    [Fact]
    public void StreamChunks_SendsThePlayersOwnChunkFirst_SoSpawnGetsGroundFast()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "stream"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);

        var chunks = new System.Collections.Generic.List<ChunkDataMessage>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is ChunkDataMessage c) chunks.Add(c);
        };

        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Pilot");
        client.Poll();

        Assert.NotEmpty(chunks);
        // Nearest-first streaming: the very first chunk a fresh spawn receives is its own chunk (its floor),
        // so it gets solid ground under it immediately instead of falling through while terrain loads.
        var center = Spacecraft.Shared.World.WorldConstants.WorldToChunk(server.Sessions[1].State.Position.ToBlock());
        Assert.Equal(center.X, chunks[0].Cx);
        Assert.Equal(center.Y, chunks[0].Cy);
        Assert.Equal(center.Z, chunks[0].Cz);
    }

    [Fact]
    public void WalkingPastTheSeam_WrapsLongitude_AndBlocksRoundTripAcrossIt()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "wrap"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);

        var config = Config();
        config.WorldName = "wrap";
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Walker");
        var session = server.Sessions[1];

        const int C = Spacecraft.Shared.World.WorldConstants.Circumference;

        // Walk a full lap plus 50 east: the authoritative longitude wraps back into [0, C).
        client.Send(NetCodec.Encode(new MoveIntent { X = C + 50, Y = session.State.Position.Y, Z = 0 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(50f, session.State.Position.X, 3);

        // A column reached the long way round (x = C + 7) is the identical block as its canonical twin
        // (x = 7) — proving GetBlock canonicalizes and generation is seam-free.
        int y = (int)System.Math.Floor(session.State.Position.Y);
        int solidY = y;
        while (solidY > y - 40 && server.World.GetBlock(new Vector3i(7, solidY, 0)).IsAir)
        {
            solidY--;
        }

        Assert.Equal(server.World.GetBlock(new Vector3i(7, solidY, 0)),
                     server.World.GetBlock(new Vector3i(C + 7, solidY, 0)));

        // Digging at the wrapped coordinate edits the canonical column (shared cache + persistence key).
        server.World.SetBlock(new Vector3i(C + 7, solidY, 0), Spacecraft.Shared.Primitives.BlockId.Air);
        Assert.True(server.World.GetBlock(new Vector3i(7, solidY, 0)).IsAir);
    }

    [Fact]
    public void Jetpack_DrainsSuitEnergy_WhileActive_AndRejectsWithoutOne()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "jet"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);

        var config = Config();
        config.WorldName = "jet";
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Pilot");
        var session = server.Sessions[1];

        // No jetpack carried: the request is rejected and the player is not jetpacking.
        session.State.SuitEnergy = 100f;
        client.Send(NetCodec.Encode(new SetJetpackIntent { Active = true }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.False(session.State.Jetpacking);

        // With a jetpack and energy: it activates and drains suit energy over ticks.
        session.State.Inventory.Add("jetpack", 1, 1);
        client.Send(NetCodec.Encode(new SetJetpackIntent { Active = true }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.True(session.State.Jetpacking);

        float before = session.State.SuitEnergy;
        server.Tick(0.5); // not aboard ship → no recharge, so it must fall
        Assert.True(session.State.SuitEnergy < before, "Jetpack should drain suit energy while firing.");

        // Releasing the thrust stops the drain.
        client.Send(NetCodec.Encode(new SetJetpackIntent { Active = false }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.False(session.State.Jetpacking);
    }

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }

    private static void SqliteConnectionReset() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore Windows file-lock cleanup races
        }
    }
}
