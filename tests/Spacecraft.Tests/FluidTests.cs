using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.State;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>Flowing fluids: water/lava spread down + sideways with level decay (World systems).</summary>
public sealed class FluidTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FluidTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_fluid_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fluid"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "fluid", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void WaterAndLava_AreMineable_OnlyByTheMiningBeam()
    {
        // Fluids can be cleared now — but only with the mining beam (a tier-3 drill), not the starter drill.
        foreach (var key in new[] { "water", "lava" })
        {
            var def = _content.GetBlock(key)!;
            Assert.True(def.Mineable, $"{key} should be mineable.");
            Assert.Equal(ToolKind.Drill, def.RequiredTool);
            Assert.Equal(3, def.MinToolTier); // basic (1) + titanium (2) drills can't touch it
        }
    }

    [Fact]
    public void Water_BasicDrillCannot_MiningBeamCan()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var water = _content.GetBlock("water")!.NumericId.Value;
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 132f, 0.5f);

            var pos = new Vector3i(0, 130, 0); // high in the air column, isolated
            server.World.SetBlock(pos, new BlockId(water));

            // Starter basic drill (slot 0) — too weak for a fluid.
            server.MineBlock("Miner", pos.X, pos.Y, pos.Z);
            Assert.Equal(water, server.World.GetBlock(pos).Value);

            // Mining beam — clears it.
            p.State.Inventory.SetSlot(6, new ItemStack("mining_beam", 1));
            p.State.SelectedHotbarSlot = 6;
            server.MineBlock("Miner", pos.X, pos.Y, pos.Z);
            Assert.True(server.World.GetBlock(pos).IsAir, "The mining beam should clear water.");
        }
    }

    [Fact]
    public void WaterBody_RefillsAMinedHole()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var stone = _content.GetBlock("stone")!.NumericId;
            var water = _content.GetBlock("water")!.NumericId;
            int y0 = 60;

            // A stone basin (floor at y0) filled with a 9x9x3 body of static "sea" water (y0+1..y0+3).
            for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
            {
                server.World.SetBlock(new Vector3i(x, y0, z), stone);
                for (int y = y0 + 1; y <= y0 + 3; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), water);
                }
            }

            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, y0 + 5f, 0.5f);
            p.State.Inventory.SetSlot(6, new ItemStack("mining_beam", 1));
            p.State.SelectedHotbarSlot = 6;

            // Punch a hole in the middle of the body (beam radius clears a 3x3x3 pocket).
            var hole = new Vector3i(0, y0 + 2, 0);
            server.MineBlock("Miner", hole.X, hole.Y, hole.Z);
            Assert.True(server.World.GetBlock(hole).IsAir, "Mining should open the hole first.");

            // The surrounding body flows back in over a few fluid steps.
            for (int i = 0; i < 12; i++)
            {
                server.Tick(0.3);
            }

            Assert.Equal(water.Value, server.World.GetBlock(hole).Value);
        }
    }

    [Fact]
    public void Water_FlowsDownIntoAir()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // High in the air column above the surface = guaranteed empty cells.
            var src = new Vector3i(0, 130, 0);
            var below = new Vector3i(0, 129, 0);
            Assert.True(server.World.GetBlock(below).IsAir);

            server.PlaceFluidSource("water", src.X, src.Y, src.Z);
            server.Tick(0.3); // > fluid interval → one flow step

            Assert.False(server.World.GetBlock(below).IsAir); // water fell down
            Assert.Equal(_content.GetBlock("water")!.NumericId.Value, server.World.GetBlock(below).Value);
        }
    }

    [Fact]
    public void Water_SpreadsSidewaysOnFloor()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // Build a small solid floor in the air, place a source on it, let it spread.
            var stone = _content.GetBlock("stone")!.NumericId;
            int y = 130;
            for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 3; z++)
            {
                server.World.SetBlock(new Vector3i(x, y - 1, z), stone);
            }

            server.PlaceFluidSource("water", 0, y, 0);
            for (int i = 0; i < 6; i++)
            {
                server.Tick(0.3); // several flow steps
            }

            // Water should have reached a side cell on the floor.
            var side = new Vector3i(2, y, 0);
            Assert.Equal(_content.GetBlock("water")!.NumericId.Value, server.World.GetBlock(side).Value);
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
