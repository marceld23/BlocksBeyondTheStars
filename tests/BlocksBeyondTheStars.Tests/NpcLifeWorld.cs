// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The arena the living-NPC tests (#1865–#1868) share: a stone pad floating above every natural feature, a base
/// core in its middle (feet level = pad + 1), machines to earn the first settler, and small builders for beds,
/// chairs, huts and walls. Blocks go in through <see cref="ServerWorld.SetBlock"/> with the owner set, so the
/// block-edit store — which the base index reads — holds them exactly as if the player had placed them.
/// </summary>
internal sealed class NpcLifeWorld : IDisposable
{
    public const int Cx = 40, Cz = 40;
    public const string Owner = "Homesteader";

    public readonly SvGameServer Server;
    public readonly GameContent Content;
    public readonly RecordingTransport Transport;
    public readonly PlayerSession Player;
    public readonly int PadY;
    public int Feet => PadY + 1;
    public int BaseId { get; private set; }

    private readonly string _root;
    private readonly SqliteWorldRepository _repo;

    public sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((connectionId, m));
            }
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((int.MinValue, m));
            }
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }

        public void Stop() { }

        public void Dispose() { }
    }

    public NpcLifeWorld(GameContent content, string name, int padHalf = 24, Action<ServerConfig>? configure = null)
    {
        Content = content;
        _root = Path.Combine(Path.GetTempPath(), "bbts_npclife_" + Guid.NewGuid().ToString("N"));
        _repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        Transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
        };
        configure?.Invoke(config);
        Server = new SvGameServer(config, content, Transport, _repo);
        Server.Start();

        var stone = Block("stone");
        PadY = MaxTopY(Cx, Cz, padHalf) + 8;
        for (int dx = -padHalf; dx <= padHalf; dx++)
            for (int dz = -padHalf; dz <= padHalf; dz++)
            {
                Server.World.SetBlock(new Vector3i(Cx + dx, PadY, Cz + dz), stone);
            }

        Player = Server.AddLocalPlayer(Owner);
        Player.State.AboardShip = false;
        Player.State.Position = new Vector3f(Cx - 1.5f, Feet, Cz - 1.5f);
    }

    public BlocksBeyondTheStars.Shared.Primitives.BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    private int MaxTopY(int cx, int cz, int r)
    {
        int top = 0;
        for (int dx = -r; dx <= r; dx += 4)
            for (int dz = -r; dz <= r; dz += 4)
            {
                for (int y = 200; y > -200; y--)
                {
                    if (!Server.World.GetBlock(new Vector3i(cx + dx, y, cz + dz)).IsAir)
                    {
                        top = Math.Max(top, y);
                        break;
                    }
                }
            }

        return top;
    }

    /// <summary>A block the player placed.</summary>
    public void Set(int x, int y, int z, string key, int shape = 0)
        => Server.World.SetBlock(new Vector3i(x, y, z), Block(key), 0, 0, shape, Owner);

    /// <summary>Founds the base at the pad's centre and puts three machines (without a job of their own) beside it.</summary>
    public int FoundBase(string machine = "detoxifier")
    {
        Player.State.Inventory.Add("base_core", 1, 16);
        Server.PlaceBlock(Owner, Cx, Feet, Cz, "base_core");
        BaseId = Server.BaseSnapshots.Single(b => b.OwnerId == Player.State.PlayerId).Id;
        Set(Cx + 1, Feet, Cz - 6, machine);
        Set(Cx + 2, Feet, Cz - 6, machine);
        Set(Cx + 3, Feet, Cz - 6, machine);
        return BaseId;
    }

    /// <summary>A two-cell bed: the head at the cell, the foot one block toward +Z (yaw 0).</summary>
    public Vector3i Bed(int x, int z, int? y = null)
    {
        var head = new Vector3i(x, y ?? Feet, z);
        int desc = ShapeCode.Pack((int)BlockShape.BedHead, 0);
        Set(head.X, head.Y, head.Z, "bed", desc);
        Set(head.X, head.Y, head.Z + 1, "bed", FurnitureShapes.BedPartnerDescriptor(desc));
        return head;
    }

    /// <summary>A stone chair, backrest toward +Z.</summary>
    public Vector3i Chair(int x, int z)
    {
        Set(x, Feet, z, "stone", ShapeCode.Pack((int)BlockShape.Chair, 0));
        return new Vector3i(x, Feet, z);
    }

    /// <summary>A closed stone hut: walls three high and a roof around an inner room of (2·half−1)² cells, its floor the
    /// pad. <paramref name="doorway"/> cuts a two-high gap in the +X wall at the hut's middle.</summary>
    public void Hut(int cx, int cz, int half, bool doorway)
    {
        for (int dx = -half; dx <= half; dx++)
            for (int dz = -half; dz <= half; dz++)
            {
                bool wall = Math.Abs(dx) == half || Math.Abs(dz) == half;
                for (int y = 0; y < 3; y++)
                {
                    if (wall)
                    {
                        Set(cx + dx, Feet + y, cz + dz, "stone");
                    }
                }

                Set(cx + dx, Feet + 3, cz + dz, "stone"); // the roof
            }

        if (doorway)
        {
            Server.RemoveBlockForTest(cx + half, Feet, cz);
            Server.RemoveBlockForTest(cx + half, Feet + 1, cz);
        }
    }

    /// <summary>A two-high stone ring of Chebyshev radius <paramref name="ring"/> around the core (a roofless yard).</summary>
    public void WallRing(int ring)
    {
        for (int dx = -ring; dx <= ring; dx++)
            for (int dz = -ring; dz <= ring; dz++)
            {
                if (Math.Abs(dx) == ring || Math.Abs(dz) == ring)
                {
                    Set(Cx + dx, Feet, Cz + dz, "stone");
                    Set(Cx + dx, Feet + 1, Cz + dz, "stone");
                }
            }
    }

    /// <summary>Ticks with the local sun at the pad pinned to <paramref name="dayFraction"/> and the player standing on
    /// the pad (NPCs freeze without a player on the surface).</summary>
    public void TickAt(double dayFraction, double seconds, double dt = 0.25, Func<bool>? until = null, Action? every = null)
    {
        int steps = (int)Math.Ceiling(seconds / dt);
        for (int i = 0; i < steps; i++)
        {
            Server.SetLocalDayFractionForTest(dayFraction, Cx);
            Player.State.Health = 100f; // a night on a Survival world may bring a machine by — the test is about the NPCs
            Player.State.Oxygen = 100f;
            Player.State.Hunger = 100f;
            Server.TickForTest(dt);
            every?.Invoke();
            if (until != null && until())
            {
                return;
            }
        }
    }

    public IEnumerable<string> MessagesToPlayer()
        => Transport.Sent.Where(s => s.Conn == Player.ConnectionId).Select(s => s.Msg)
            .OfType<BlocksBeyondTheStars.Networking.Messages.ServerMessage>().Select(m => m.Text);

    public void Dispose()
    {
        try
        {
            Transport.Dispose();
            _repo.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
