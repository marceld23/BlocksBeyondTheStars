// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A torch burns wherever there is air to breathe (#1483): on an airless body it is refused in the open but
/// accepted inside a founded base's supply cube and inside a sealed base room — the old check asked the world's
/// atmosphere only and refused the lamp in an asteroid base whose own core said "air here".
/// </summary>
public sealed class TorchAirTests : IDisposable
{
    // A long sealed room with the core near its west end: the base's air pockets are filled from the core's own
    // neighbours (and energy doors), so the room must contain the core — and it is long enough that its east end
    // lies well beyond the core's 8-block supply cube, where only the sealed room itself provides air.
    private const int CoreX = 7, CoreZ = 0;
    private const int ShellMinX = 5, ShellMaxX = 25, ShellMinZ = -3, ShellMaxZ = 3;

    private readonly string _root;
    private readonly GameContent _content;

    public TorchAirTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_torchair_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(out SqliteWorldRepository repo, string name)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "asteroid", // atmosphere "none": nothing for a flame to burn in the open
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static int SurfaceYAt(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > 0; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    /// <summary>Founds a base and carves + shells a sealed 9×3×5 room east of it, all in real blocks above the
    /// surface; returns the room's floor Y (the core sits at that height too). The builder stands inside.</summary>
    private (PlayerSession Builder, int Y) BuildBaseAndRoom(SvGameServer server)
    {
        int y0 = SurfaceYAt(server, CoreX, CoreZ) + 4; // well clear of the rock
        var iron = _content.GetBlock("iron_wall")!.NumericId;
        for (int x = ShellMinX; x <= ShellMaxX; x++)
            for (int y = y0 - 1; y <= y0 + 3; y++)
                for (int z = ShellMinZ; z <= ShellMaxZ; z++)
                {
                    bool interior = x > ShellMinX && x < ShellMaxX && y >= y0 && y <= y0 + 2 && z > ShellMinZ && z < ShellMaxZ;
                    server.World.SetBlock(new Vector3i(x, y, z), interior ? BlockId.Air : iron);
                }

        // The open-air control spots outside the room: a clear cell beside the core's cube and one far out.
        server.World.SetBlock(new Vector3i(CoreX - 4, y0 + 1, CoreZ), BlockId.Air);
        server.World.SetBlock(new Vector3i(CoreX + 33, y0 + 7, CoreZ), BlockId.Air);

        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(CoreX - 1, y0, CoreZ + 0.5f); // inside the room, west end
        p.State.Inventory.Add("base_core", 2, 16);
        p.State.Inventory.Add("torch", 16, 64);
        server.PlaceBlock("Builder", CoreX, y0, CoreZ, "base_core");
        Assert.Single(server.BaseSnapshots);
        return (p, y0);
    }

    [Fact]
    public void Torch_BurnsInBaseAir_ButNotInTheOpen_OnAnAirlessBody()
    {
        var server = Start(out var repo, "torch");
        using (repo)
        {
            Assert.False(server.AtmospherePresent, "the asteroid has no atmosphere");
            var (p, y) = BuildBaseAndRoom(server);
            var torch = _content.GetBlock("torch")!.NumericId;

            // At the room's east end, 16 cells from the core — beyond its 8-block supply cube: air of the sealed
            // room's own making → the torch is accepted.
            var roomSpot = new Vector3i(CoreX + 16, y + 1, CoreZ);
            p.State.Position = new Vector3f(roomSpot.X - 0.5f, y, CoreZ + 0.5f);
            server.PlaceBlock("Builder", roomSpot.X, roomSpot.Y, roomSpot.Z, "torch");
            Assert.Equal(torch.Value, server.World.GetBlock(roomSpot).Value);

            // Outside the room but inside the core's cube, in the open: the supply cube breathes → accepted as well.
            var cubeSpot = new Vector3i(CoreX - 4, y + 1, CoreZ);
            p.State.Position = new Vector3f(cubeSpot.X - 0.5f, y, CoreZ + 0.5f);
            server.PlaceBlock("Builder", cubeSpot.X, cubeSpot.Y, cubeSpot.Z, "torch");
            Assert.Equal(torch.Value, server.World.GetBlock(cubeSpot).Value);

            // Thirty-three blocks out in the vacuum: refused — the cell stays empty and the torch stays in the pack.
            var voidSpot = new Vector3i(CoreX + 33, y + 7, CoreZ);
            int before = p.State.Inventory.CountOf("torch");
            p.State.Position = new Vector3f(voidSpot.X - 0.5f, y + 6, CoreZ + 0.5f);
            server.PlaceBlock("Builder", voidSpot.X, voidSpot.Y, voidSpot.Z, "torch");
            Assert.True(server.World.GetBlock(voidSpot).IsAir, "no air, no flame");
            Assert.Equal(before, p.State.Inventory.CountOf("torch"));
        }
    }
}
