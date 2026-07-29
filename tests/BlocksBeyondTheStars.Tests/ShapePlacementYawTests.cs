// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Player-controlled placement angle for shaped blocks. Asked for by a player: "Ich will das Treppen in
/// verschiedenen Winkeln platzierbar sind."
/// <para>
/// The shape descriptor has always packed shape × yaw × up-face (24 orientations) into the chunk and the wire
/// format, and the rotate key already drove the up-face — but the YAW came solely from where the player was
/// looking, so getting the turn you wanted meant standing in a particular direction. These tests cover the
/// explicit yaw override, and that leaving it out keeps the old facing-derived behaviour.
/// </para>
/// </summary>
public sealed class ShapePlacementYawTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShapePlacementYawTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_yaw_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "yaw"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "yaw",
            Seed = 4,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A stone staircase item, in the air where nothing else interferes.</summary>
    private (SvGameServer Server, BlocksBeyondTheStars.GameServer.PlayerSession Player, string Item) Builder(
        SvGameServer server)
    {
        var p = server.AddLocalPlayer("Justus");
        p.State.Position = new Vector3f(0, 200, 0);
        p.State.Yaw = 0f; // looking along +Z → facing-derived yaw would be 0
        string stairs = ItemKey.Compose("stone", 0, 0, (int)BlockShape.Stairs);
        p.State.Inventory.Add(stairs, 64, _content.MaxStackOf("stone")); // enough for all 24 orientations
        return (server, p, stairs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnExplicitYaw_IsStoredOnThePlacedBlock(int yaw)
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (_, p, stairs) = Builder(server);
            var pos = new Vector3i(1, 200, 0);

            // Standing still, looking the same way every time — only the requested turn differs.
            server.PlaceBlock("Justus", pos.X, pos.Y, pos.Z, stairs, upFace: ShapeCode.UpPlusY, yaw: yaw);

            int desc = server.World.GetShape(pos);
            Assert.Equal((int)BlockShape.Stairs, ShapeCode.ShapeOf(desc));
            Assert.Equal(yaw, ShapeCode.OrientationOf(desc));
        }
    }

    [Fact]
    public void WithoutAnOverride_TheAngleStillFollowsWhereThePlayerLooks()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (_, p, stairs) = Builder(server);
            p.State.Yaw = 180f; // a half turn → facing-derived yaw of 2

            var pos = new Vector3i(2, 200, 0);
            server.PlaceBlock("Justus", pos.X, pos.Y, pos.Z, stairs); // no override

            Assert.Equal(2, ShapeCode.OrientationOf(server.World.GetShape(pos)));
        }
    }

    [Fact]
    public void YawAndUpFaceAreIndependent_SoAllTwentyFourOrientationsAreReachable()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (_, p, stairs) = Builder(server);

            var seen = new HashSet<(int Up, int Yaw)>();
            int x = 4;
            for (int up = 0; up <= 5; up++)
            {
                for (int yaw = 0; yaw <= 3; yaw++)
                {
                    var pos = new Vector3i(x++, 200, 0);

                    // Stay next to the target cell — placement is reach-limited, so walking the row matters.
                    p.State.Position = new Vector3f(pos.X - 1.2f, pos.Y, pos.Z);
                    server.PlaceBlock("Justus", pos.X, pos.Y, pos.Z, stairs, upFace: up, yaw: yaw);

                    int desc = server.World.GetShape(pos);
                    Assert.Equal((int)BlockShape.Stairs, ShapeCode.ShapeOf(desc)); // it really was placed
                    seen.Add((ShapeCode.UpFaceOf(desc), ShapeCode.OrientationOf(desc)));
                }
            }

            Assert.Equal(24, seen.Count); // every combination round-trips distinctly
        }
    }

    [Fact]
    public void AnOutOfRangeYaw_FallsBackToFacing_RatherThanCorrupting()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (_, p, stairs) = Builder(server);
            p.State.Yaw = 90f; // → facing-derived yaw of 1

            var pos = new Vector3i(3, 200, 0);
            server.PlaceBlock("Justus", pos.X, pos.Y, pos.Z, stairs, upFace: ShapeCode.UpPlusY, yaw: 99);

            Assert.Equal(1, ShapeCode.OrientationOf(server.World.GetShape(pos)));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked save file must never fail the test run.
        }
    }
}
