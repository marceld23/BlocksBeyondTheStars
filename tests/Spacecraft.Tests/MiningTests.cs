using System;
using System.IO;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>Block hardness + drill tiers: soft blocks break in one hit, hard ones take several, and a
/// powerful drill mines a whole area at once.</summary>
public sealed class MiningTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MiningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_mining_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "mining"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "mining", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void SoftBlock_BreaksInOneHit_WithBasicDrill()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f); // basic_drill is starter slot 0
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("mud")!.NumericId); // hardness 0.6

            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);

            Assert.True(server.World.GetBlock(pos).IsAir, "Mud should break in a single basic-drill hit.");
        }
    }

    [Fact]
    public void HardBlock_NeedsSeveralHits_WithBasicDrill()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("iron_ore")!.NumericId); // hardness 2.5, power 1

            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);
            Assert.False(server.World.GetBlock(pos).IsAir, "One hit must not break iron ore.");

            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);
            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z); // 3rd hit reaches hardness
            Assert.True(server.World.GetBlock(pos).IsAir, "Iron ore should break after a few hits.");
        }
    }

    [Fact]
    public void PowerfulDrill_MinesAnArea_AtOnce()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            p.State.Inventory.Add("mining_beam", 1, 1); // tier 3, power 4, radius 1 -> first free slot (3)
            p.State.SelectedHotbarSlot = 3;

            var stone = _content.GetBlock("stone")!.NumericId;
            var center = new Vector3i(0, 64, 0);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                server.World.SetBlock(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz), stone);
            }

            server.MineBlock("Miner", center.X, center.Y, center.Z);

            // The centre + its whole 3x3x3 neighbourhood are cleared in one go.
            int solid = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!server.World.GetBlock(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz)).IsAir)
                {
                    solid++;
                }
            }

            Assert.Equal(0, solid);
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
