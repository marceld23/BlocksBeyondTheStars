// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Granular blocks (#1319 — sand/ash/snow settle when their support goes, instant through air, one cell per
/// step through fluid) and the lava cadence (#1316 — half the water speed). Everything is built high in the
/// air column (y ≥ 120) where the rocky test world is guaranteed empty.
/// </summary>
public sealed class GranularBlockTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GranularBlockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_granular_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world = "granular", bool ship = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = ship };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private BlockId Id(string key) => _content.GetBlock(key)!.NumericId;

    private void Floor(SvGameServer server, int y, int r = 3, int cx = 0, int cz = 0)
    {
        for (int x = cx - r; x <= cx + r; x++)
            for (int z = cz - r; z <= cz + r; z++)
            {
                server.World.SetBlock(new Vector3i(x, y, z), Id("stone"));
            }
    }

    private static void Steps(SvGameServer server, int n)
    {
        for (int i = 0; i < n; i++)
        {
            server.Tick(0.3); // > the fluid/granular interval → one step each
        }
    }

    [Fact]
    public void SandAshAndSnow_AreGranular_StoneIsNot()
    {
        Assert.True(_content.GetBlock("sand")!.Granular);
        Assert.True(_content.GetBlock("ash")!.Granular);
        Assert.True(_content.GetBlock("snow")!.Granular);
        Assert.False(_content.GetBlock("stone")!.Granular);
        Assert.False(_content.GetBlock("dirt")!.Granular);
    }

    [Fact]
    public void MiningTheSupport_DropsTheWholeSandColumnOntoTheNextFloor()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Floor(server, 120);
            server.World.SetBlock(new Vector3i(0, 125, 0), Id("stone")); // a pillar top holding three sand
            for (int y = 126; y <= 128; y++)
            {
                server.World.SetBlock(new Vector3i(0, y, 0), Id("sand"));
            }

            server.RemoveBlockForTest(0, 125, 0); // the mining path's wake
            Steps(server, 6);

            for (int y = 121; y <= 123; y++)
            {
                Assert.Equal(Id("sand").Value, server.World.GetBlock(new Vector3i(0, y, 0)).Value);
            }

            for (int y = 124; y <= 128; y++)
            {
                Assert.True(server.World.GetBlock(new Vector3i(0, y, 0)).IsAir, $"y={y} should be empty after the column settled");
            }
        }
    }

    [Fact]
    public void SandPlacedOverAPit_LandsAtTheBottom()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Floor(server, 120);
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 132f, 0.5f);
            p.State.Inventory.Add("sand", 4, 64);

            server.PlaceBlock("Builder", 0, 130, 0, "sand");
            Assert.Equal(Id("sand").Value, server.World.GetBlock(new Vector3i(0, 130, 0)).Value); // placed where aimed
            Steps(server, 2);

            Assert.True(server.World.GetBlock(new Vector3i(0, 130, 0)).IsAir);
            Assert.Equal(Id("sand").Value, server.World.GetBlock(new Vector3i(0, 121, 0)).Value); // one step, nine cells down
        }
    }

    [Fact]
    public void SandDroppedOntoLava_SinksOneCellPerStep_ReplacingTheLava()
    {
        // Lyxette's use case: "rutschende Blöcke in der Lava versenken … sich durch die Lava durcharbeiten".
        var server = Started(out var repo);
        using (repo)
        {
            Floor(server, 120);
            // A 1-wide stone well (walls x/z ±1, y 121..123) holding a three-deep lava column so it cannot spread.
            for (int y = 121; y <= 123; y++)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        server.World.SetBlock(new Vector3i(dx, y, dz), dx == 0 && dz == 0 ? Id("lava") : Id("stone"));
                    }

            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(2.5f, 126f, 0.5f);
            p.State.Inventory.Add("sand", 4, 64);
            server.PlaceBlock("Builder", 0, 126, 0, "sand");

            Steps(server, 1); // instant through air: lands ON the lava (y 124)
            Assert.Equal(Id("sand").Value, server.World.GetBlock(new Vector3i(0, 124, 0)).Value);
            Assert.Equal(Id("lava").Value, server.World.GetBlock(new Vector3i(0, 123, 0)).Value);

            Steps(server, 1); // one cell per step through the melt
            Assert.Equal(Id("sand").Value, server.World.GetBlock(new Vector3i(0, 123, 0)).Value);
            Assert.True(server.World.GetBlock(new Vector3i(0, 124, 0)).IsAir);
            Assert.Equal(Id("lava").Value, server.World.GetBlock(new Vector3i(0, 122, 0)).Value);

            Steps(server, 4);
            Assert.Equal(Id("sand").Value, server.World.GetBlock(new Vector3i(0, 121, 0)).Value); // on the stone floor
            for (int y = 122; y <= 126; y++)
            {
                Assert.NotEqual(Id("lava").Value, server.World.GetBlock(new Vector3i(0, y, 0)).Value);
            }
        }
    }

    [Fact]
    public void ACarvedSandForm_StaysWhereItWasBuilt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Floor(server, 120);
            server.World.SetBlock(new Vector3i(0, 125, 0), Id("stone"));
            server.World.SetBlock(new Vector3i(0, 126, 0), Id("sand"), shape: ShapeCode.Pack(1, 0)); // a built form

            server.RemoveBlockForTest(0, 125, 0);
            Steps(server, 3);

            Assert.Equal(Id("sand").Value, server.World.GetBlock(new Vector3i(0, 126, 0)).Value);
        }
    }

    [Fact]
    public void DyedSand_KeepsItsDyeAfterFalling()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Floor(server, 120);
            server.World.SetBlock(new Vector3i(0, 125, 0), Id("stone"));
            server.World.SetBlock(new Vector3i(0, 126, 0), Id("sand"), tint: 5, glow: 2);

            server.RemoveBlockForTest(0, 125, 0);
            Steps(server, 2);

            var landed = new Vector3i(0, 121, 0);
            Assert.Equal(Id("sand").Value, server.World.GetBlock(landed).Value);
            Assert.Equal((5, 2), server.World.GetModifier(landed));
        }
    }

    [Fact]
    public void GeneratedOverhangs_StayUntilTouched()
    {
        // Direct world writes stand in for generated terrain: nothing wakes them, so nothing moves — the
        // discipline that keeps dune overhangs (and the worldgen determinism audits) intact.
        var server = Started(out var repo);
        using (repo)
        {
            Floor(server, 120);
            server.World.SetBlock(new Vector3i(0, 126, 0), Id("sand")); // floating, untouched
            Steps(server, 4);
            Assert.Equal(Id("sand").Value, server.World.GetBlock(new Vector3i(0, 126, 0)).Value);

            server.WakeGranularForTest(0, 126, 0); // now something touched it
            Steps(server, 2);
            Assert.Equal(Id("sand").Value, server.World.GetBlock(new Vector3i(0, 121, 0)).Value);
        }
    }

    [Fact]
    public void SandInAParkedShip_IsLeftAlone()
    {
        var server = Started(out var repo, "granship", ship: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot"); // spawns inside the ship
            server.Tick(0.1);
            var floor = p.State.Position.ToBlock();
            Vector3i? top = null, below = null;
            for (int dy = 0; dy <= 8 && top == null; dy++)
            {
                var t = new Vector3i(floor.X, floor.Y + dy + 1, floor.Z);
                var b = new Vector3i(floor.X, floor.Y + dy, floor.Z);
                if (server.ShipInteriorContainsCellForTest(t.X, t.Y, t.Z) && server.World.GetBlock(t).IsAir
                    && server.ShipInteriorContainsCellForTest(b.X, b.Y, b.Z) && server.World.GetBlock(b).IsAir)
                {
                    top = t;
                    below = b;
                }
            }

            Assert.NotNull(top);
            p.State.Position = new Vector3f(floor.X + 0.5f, floor.Y + 4f, floor.Z + 0.5f); // out of the landing cell
            server.World.SetBlock(top!.Value, Id("sand"));
            server.WakeGranularForTest(top.Value.X, top.Value.Y, top.Value.Z);
            Steps(server, 3);

            Assert.Equal(Id("sand").Value, server.World.GetBlock(top.Value).Value); // cabin furnishing stays put
            Assert.True(server.World.GetBlock(below!.Value).IsAir);
        }
    }

    [Fact]
    public void Lava_FlowsAtHalfTheWaterSpeed()
    {
        // #1316: two identical floors far apart, a water source on one and a lava source on the other.
        var server = Started(out var repo);
        using (repo)
        {
            int y = 130;
            Floor(server, y - 1, r: 6, cx: 0, cz: 0);
            Floor(server, y - 1, r: 6, cx: 100, cz: 0);
            server.PlaceFluidSource("water", 0, y, 0);
            server.PlaceFluidSource("lava", 100, y, 0);

            Steps(server, 4);

            int waterReach = Reach(server, Id("water").Value, 0, y);
            int lavaReach = Reach(server, Id("lava").Value, 100, y);
            Assert.True(waterReach >= 3, $"water should have spread a few cells (got {waterReach})");
            Assert.True(lavaReach >= 1, $"lava must still flow (got {lavaReach})");
            Assert.True(lavaReach <= waterReach / 2 + 1 && lavaReach < waterReach,
                $"lava should reach about half as far as water (water {waterReach}, lava {lavaReach})");
        }
    }

    private static int Reach(SvGameServer server, ushort fluid, int cx, int y)
    {
        int reach = 0;
        for (int dx = 1; dx <= 6; dx++)
        {
            if (server.World.GetBlock(new Vector3i(cx + dx, y, 0)).Value == fluid)
            {
                reach = dx;
            }
        }

        return reach;
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
