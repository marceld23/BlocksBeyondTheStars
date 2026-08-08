// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
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
/// Sealed-room base air (#794) + the energy door as the one airtight door (#793): rooms enclosed by
/// airtight full-cube blocks and energy doors, connected to a founded base, breathe beyond the radius-8
/// cube of #782 — and a mined wall both kills that air and warns everyone in reach (@base_air_lost).
/// Rocky (toxic atmosphere) makes oxygen matter; all positions stay below rocky's atmosphere line.
/// </summary>
public sealed class BaseSealedRoomTests : IDisposable
{
    // Mid-air on rocky (terrain tops out well below): core cell empty, in reach, below the atmosphere line.
    private const int CoreX = 1, CoreY = 120, CoreZ = 0;

    // The test room: a stone shell x[5..15] y[119..123] z[-3..3] with interior x[6..14] y[120..122]
    // z[-2..2]. Interior cells at x 6..9 lie within the radius-8 cube (→ the pocket "touches" the base);
    // cells at x 10..14 lie beyond it and only breathe through the sealed-room fill. The doorway is a
    // 1-wide, 3-tall hole at (5, 120..122, 0) facing the core.
    private const int ShellMinX = 5, ShellMaxX = 15, ShellMinY = 119, ShellMaxY = 123, ShellMinZ = -3, ShellMaxZ = 3;
    private static readonly Vector3f InsideBeyondCube = new(13.5f, 120.5f, 0.5f); // dx=12 from the core

    private readonly string _root;
    private readonly GameContent _content;

    public BaseSealedRoomTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_sealedroom_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every server send so a test can assert the air-lost warning was told.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer Start(out SqliteWorldRepository repo, string name, IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky", // toxic atmosphere → oxygen drains outside a life-support field
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Adds a player on foot (no starter ship → the aboard default must be dropped explicitly).</summary>
    private static PlayerSession OnFoot(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>Founds the base and builds the room shell out of <paramref name="wallKey"/>, leaving the
    /// 3-tall doorway hole; optionally fills it with a placed door block (energy or slide).</summary>
    private PlayerSession BuildRoom(SvGameServer server, string wallKey, string? doorKey)
    {
        var p = OnFoot(server, "Builder");
        p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
        p.State.Inventory.Add("base_core", 2, 16);
        server.PlaceBlock("Builder", CoreX, CoreY, CoreZ, "base_core");
        Assert.Single(server.BaseSnapshots);

        var wall = _content.GetBlock(wallKey)!.NumericId;
        for (int x = ShellMinX; x <= ShellMaxX; x++)
            for (int y = ShellMinY; y <= ShellMaxY; y++)
                for (int z = ShellMinZ; z <= ShellMaxZ; z++)
                {
                    bool interior = x > ShellMinX && x < ShellMaxX && y > ShellMinY && y < ShellMaxY
                        && z > ShellMinZ && z < ShellMaxZ;
                    bool doorway = x == ShellMinX && z == 0 && y >= CoreY && y <= CoreY + 2;
                    if (!interior && !doorway)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), wall);
                    }
                }

        if (doorKey != null)
        {
            p.State.Inventory.Add(doorKey, 2, 16);
            p.State.Position = new Vector3f(ShellMinX - 1, CoreY, 0.5f);
            server.PlaceBlock("Builder", ShellMinX, CoreY, 0, doorKey);
            Assert.Contains(server.DoorSnapshots, d => d.Kind == (doorKey == "door_energy" ? "energy" : "slide"));
        }

        return p;
    }

    /// <summary>Ticks with the player pinned at <paramref name="at"/> — long enough (3 s) to cover the
    /// sealed-volume recompute interval (1.5 s) at least once.</summary>
    private static void TickAt(SvGameServer server, PlayerSession p, Vector3f at, int halfSeconds = 6)
    {
        for (int i = 0; i < halfSeconds; i++)
        {
            p.State.Position = at;
            server.TickForTest(0.5);
        }
    }

    [Fact]
    public void SealedRoomBeyondTheCube_Breathes_AndAHoleKillsIt()
    {
        var server = Start(out var repo, "sealedroom");
        using (repo)
        {
            var p = BuildRoom(server, "stone", "door_energy");
            Assert.False(server.AtmosphereBreathable, "rocky should be toxic for this test");

            // Well beyond the radius-8 cube, but inside the sealed room: the base air reaches it.
            float inside = p.State.Oxygen = 50f;
            TickAt(server, p, InsideBeyondCube);
            Assert.True(p.State.Oxygen > inside,
                $"Oxygen should refill inside the sealed room beyond the cube (was {p.State.Oxygen}).");

            // Knock a hole in the ceiling above that spot: the pocket leaks to the open sky → drains.
            server.World.SetBlock(new Vector3i(13, ShellMaxY, 0), BlockId.Air);
            float holed = p.State.Oxygen = 80f;
            TickAt(server, p, InsideBeyondCube, halfSeconds: 8);
            Assert.True(p.State.Oxygen < holed,
                $"Oxygen should drain once the room has a hole (was {p.State.Oxygen}).");
        }
    }

    [Fact]
    public void DirtWalls_NeverSeal()
    {
        var server = Start(out var repo, "sealeddirt");
        using (repo)
        {
            var p = BuildRoom(server, "dirt", "door_energy");
            float before = p.State.Oxygen = 80f;
            TickAt(server, p, InsideBeyondCube, halfSeconds: 8);
            Assert.True(p.State.Oxygen < before,
                $"A dirt room is not airtight — oxygen should drain (was {p.State.Oxygen}).");
        }
    }

    [Fact]
    public void MechanicalDoor_Leaks_OnlyTheEnergyDoorSeals()
    {
        var server = Start(out var repo, "sealedslide");
        using (repo)
        {
            // The identical stone room, but with a SLIDE door in the doorway: mechanical doors are no
            // air boundary, so the room reads as open through its own doorway and never seals.
            var p = BuildRoom(server, "stone", "door_slide");
            float before = p.State.Oxygen = 80f;
            TickAt(server, p, InsideBeyondCube, halfSeconds: 8);
            Assert.True(p.State.Oxygen < before,
                $"A slide door must not seal the room — oxygen should drain (was {p.State.Oxygen}).");
        }
    }

    [Fact]
    public void MiningTheCore_KillsTheSealedRoomAirToo()
    {
        var server = Start(out var repo, "sealedcoregone");
        using (repo)
        {
            var p = BuildRoom(server, "stone", "door_energy");
            float sanity = p.State.Oxygen = 50f;
            TickAt(server, p, InsideBeyondCube);
            Assert.True(p.State.Oxygen > sanity, $"Sanity: the sealed room should breathe (was {p.State.Oxygen}).");

            p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
            server.MineBlock("Builder", CoreX, CoreY, CoreZ);
            Assert.Empty(server.BaseSnapshots);

            float after = p.State.Oxygen = 80f;
            TickAt(server, p, InsideBeyondCube, halfSeconds: 8);
            Assert.True(p.State.Oxygen < after,
                $"Without the base there is no sealed-room air either (was {p.State.Oxygen}).");
        }
    }

    [Fact]
    public void BreakingTheSeal_WarnsPlayersInReach()
    {
        var transport = new RecordingTransport();
        var server = Start(out var repo, "sealedwarn", transport);
        using (repo)
        {
            var p = BuildRoom(server, "stone", "door_energy");
            TickAt(server, p, InsideBeyondCube); // let the volume compute + prove the seal
            Assert.True(p.State.LifeSupportSource == 3,
                $"Sanity: the player should breathe base air (source was {p.State.LifeSupportSource}).");

            transport.Sent.Clear();
            server.World.SetBlock(new Vector3i(13, ShellMaxY, 0), BlockId.Air);
            TickAt(server, p, InsideBeyondCube, halfSeconds: 8);

            var lines = transport.Sent.Where(x => x.Msg is ServerMessage).Select(x => ((ServerMessage)x.Msg).Text).ToList();
            Assert.Contains("@base_air_lost", lines);
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
