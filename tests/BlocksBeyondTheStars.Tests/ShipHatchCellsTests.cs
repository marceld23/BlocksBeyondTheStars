// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
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
/// #2587: the flight view's rear-hatch panel grew past the hull when leaves sat at the stern — it guessed the opening
/// from the stern's cells. The ship's design message now names the design's own doorways to space (the hatch), so the
/// panel sits exactly on them; the owner's own doors travel apart (#2119) and show their own field (#2607).
/// </summary>
public sealed class ShipHatchCellsTests : IDisposable
{
    private readonly string _root;

    public ShipHatchCellsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_hatch_" + Guid.NewGuid().ToString("N"));
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
        catch
        {
            // best-effort temp cleanup
        }
    }

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<object> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    /// <summary>A server whose (free) starter ship is built from <paramref name="layoutKey"/> (null = the code box).</summary>
    private SvGameServer Started(string? layoutKey, out SqliteWorldRepository repo, IServerTransport? transport = null)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.GetShip("starter")!.Layout = layoutKey;
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "hatch_" + (layoutKey ?? "box")));
        var config = new ServerConfig { WorldName = "hatch", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        server.AddLocalPlayer("Host");
        return server;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ship_scout")]
    [InlineData("ship_corvette")]
    [InlineData("ship_hauler")]
    [InlineData("ship_courier")]
    [InlineData("ship_thunderbolt")]
    [InlineData("ship_deathblock")]
    [InlineData("ship_hammerhead")]
    public void EveryShip_NamesItsRearHatch_AndNoInnerDoor(string? layoutKey)
    {
        var server = Started(layoutKey, out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            var hatch = SvGameServer.ShipHatchCellsForTest(s);

            Assert.NotEmpty(hatch);
            Assert.All(hatch, c => Assert.Equal(0, c.Z)); // the stern only — the doors between cabins look at walls
            Assert.All(hatch, c => Assert.True(s.Get(c).IsAir));
            Assert.Equal(hatch.Count, hatch.Distinct().Count());

            // The whole three-high doorway, and the door's own cell among it.
            var rearDoor = s.DoorCells.Single(d => d.Z == 0);
            Assert.Contains(rearDoor, hatch);
            Assert.Equal(3, hatch.Select(c => c.Y).Distinct().Count());
            if (layoutKey is null)
            {
                Assert.Equal(9, hatch.Count); // the box ship's 3 × 3 hatch
            }
        }
    }

    [Fact]
    public void ARoomBuiltBehindTheStern_ClosesTheHatchIn()
    {
        // #2120: the owner grew the ship past its stern — the hatch now opens into that room, not into space.
        var server = Started(null, out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            var iron = new BlockId(1);
            for (int x = 0; x <= 4; x++)
            {
                for (int y = 0; y <= 4; y++)
                {
                    s.Set(new Vector3i(x, y, -3), iron); // a back wall two cells behind the hatch
                }
            }

            Assert.Empty(SvGameServer.ShipHatchCellsForTest(s));
        }
    }

    [Fact]
    public void AnOwnerBuiltDoor_IsNeverAHatchCell()
    {
        var server = Started(null, out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            var side = new Vector3i(0, 1, 3); // a doorway the owner cut into the −X wall and hung a door in
            s.Set(side, BlockId.Air);
            s.DoorCells.Add(side);
            s.DoorKinds[side] = "energy";
            s.PlacedDoorAxes[side] = false;

            var hatch = SvGameServer.ShipHatchCellsForTest(s);

            Assert.DoesNotContain(side, hatch);
            Assert.Equal(9, hatch.Count);
        }
    }

    [Fact]
    public void TheDesignMessage_CarriesTheHatch_AndTheOwner()
    {
        var transport = new RecordingTransport();
        var server = Started(null, out var repo, transport);
        using (repo)
        {
            server.EnterSpace("Host");

            var design = transport.Sent.OfType<SpaceShipDesign>().Last(d => d.Kind == "ship");
            Assert.True(design.HatchKnown); // an empty list from this server would mean "no hatch", not "guess"
            Assert.Equal(9, design.HatchX.Length);
            Assert.Equal(design.HatchX.Length, design.HatchY.Length);
            Assert.Equal(design.HatchX.Length, design.HatchZ.Length);
            Assert.All(design.HatchZ, z => Assert.Equal(0, z));
            Assert.Equal("Host", design.OwnerId);
        }
    }
}
