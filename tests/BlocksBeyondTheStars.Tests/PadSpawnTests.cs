// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Touchdown height (#1318): a player landed "suddenly underground" and was dug out a second later. A
/// 75-seed probe found no case where the pad's generated spread entombs anyone — the pads are nudged flat
/// and levelled to the median at generation. What the median ignores is what the player BUILT over the pad
/// since (Lyxette paved his landing site): the ship and the spawn now sit on the real ground, never below it,
/// and the rescue notice — a toast the next message overwrote — also lands in the chat scrollback.
/// </summary>
public sealed class PadSpawnTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PadSpawnTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_padspawn_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;
        public readonly List<object> Sent = new();
        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode) { if (NetCodec.Decode(payload) is { } m) Sent.Add(m); }
        public void Broadcast(byte[] payload, DeliveryMode mode) { if (NetCodec.Decode(payload) is { } m) Sent.Add(m); }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world, bool ship, IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 31,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = ship,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static bool CellSolid(SvGameServer server, int x, int y, int z) => !server.World.GetBlock(new Vector3i(x, y, z)).IsAir;

    private static bool Entombed(SvGameServer server, Vector3f pos)
    {
        int x = (int)Math.Floor(pos.X), y = (int)Math.Floor(pos.Y), z = (int)Math.Floor(pos.Z);
        return CellSolid(server, x, y, z) && CellSolid(server, x, y + 1, z);
    }

    [Fact]
    public void APavedPad_WithoutAShip_SpawnsThePlayerOnTopOfThePaving()
    {
        var server = Started(out var repo, "paved", ship: false);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            for (int y = py + 1; y <= py + 3; y++)
            {
                server.World.SetBlock(new Vector3i(px, y, pz), concrete); // three courses over the pad centre
            }

            var p = server.AddLocalPlayer("Newcomer");
            var pos = p.State.Position;
            Assert.False(Entombed(server, pos), $"the spawn must not sit inside the paving (at {pos})");
            Assert.Equal(py + 3 + 2, (int)Math.Floor(pos.Y)); // the old spawn was median + 2 = inside the concrete
        }
    }

    [Fact]
    public void APavedPad_WithAShip_ParksTheShipOnThePaving_AndThePilotAboveIt()
    {
        var server = Started(out var repo, "pavedship", ship: true);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            for (int dx = -4; dx <= 4; dx++)
                for (int dz = -4; dz <= 4; dz++)
                    for (int y = py + 1; y <= py + 2; y++)
                    {
                        server.World.SetBlock(new Vector3i(px + dx, y, pz + dz), concrete); // a 9×9 paved yard, two high
                    }

            var p = server.AddLocalPlayer("Host");
            var (origin, _) = server.LandedShipBoundsForTest("Host");
            Assert.Equal(py + 2 + 1, origin.Y); // the hull's first layer sits ON the paving, not inside it
            Assert.False(Entombed(server, p.State.Position), $"the pilot must not spawn inside the paving (at {p.State.Position})");
        }
    }

    [Fact]
    public void AnUntouchedPad_KeepsTheOldTouchdownHeight()
    {
        var server = Started(out var repo, "plain", ship: false);
        using (repo)
        {
            var (_, py, _) = server.LandingPadForTest(0);
            var p = server.AddLocalPlayer("Newcomer");
            Assert.Equal(py + 2, (int)Math.Floor(p.State.Position.Y)); // exactly what every existing save expects
        }
    }

    [Fact]
    public void TheDugOutRescue_AlsoLandsInTheChat()
    {
        var transport = new RecordingTransport();
        var server = Started(out var repo, "rescue", ship: false, transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Buried");
            p.State.AboardShip = false;
            var (px, py, pz) = server.LandingPadForTest(0);
            p.State.Position = new Vector3f(px + 0.5f, py - 6, pz + 0.5f); // sealed in the rock under the pad
            Assert.True(Entombed(server, p.State.Position));

            server.RunVoidRescueForTest();

            Assert.False(Entombed(server, p.State.Position));
            Assert.Contains(transport.Sent, m => m is RespawnNotice n && n.Reason == "@srv.misc.dug_out");
            var chat = transport.Sent.OfType<ServerMessage>().Select(m => m.Text).ToList();
            Assert.Contains("You were stuck in the rock — dug out.", chat); // plain text → chat scrollback, not just the toast
        }
    }

    [Fact]
    public void TpPad_LandsOnTopOfThePaving()
    {
        var server = Started(out var repo, "tppad", ship: false);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            for (int y = py + 1; y <= py + 3; y++)
            {
                server.World.SetBlock(new Vector3i(px, y, pz), concrete);
            }

            var p = server.AddLocalPlayer("Admin");
            var pad = server.TeleportTargetsForTest(p.State.PlayerId).First(t => t.Kind == "pad" && t.Number == 1);
            Assert.Equal(py + 3 + 2, (int)Math.Floor(pad.Position.Y)); // the old target was median + 2 = inside the concrete (#1367)
            Assert.False(Entombed(server, pad.Position));
        }
    }

    [Fact]
    public void ALegacyStampedHull_IsCleanedBeforeTheTouchdownHeightIsRead()
    {
        // Where the ship parks and what the pad's median is are deterministic from the seed — probe them.
        int px, py, pz;
        string location;
        var probe = Started(out var probeRepo, "residueprobe", ship: true);
        using (probeRepo)
        {
            (px, py, pz) = probe.LandingPadForTest(0);
            location = probe.World.LocationId;
            probe.AddLocalPlayer("Pilot");
            var (origin, _) = probe.LandedShipBoundsForTest("Pilot");
            Assert.Equal(py + 1, origin.Y);
        }

        // A pre-ship-as-object save: born without the clean flag, with the old stamped hull persisted as a world
        // block edit two cells over the pad's median — inside today's ship volume AND inside the touchdown scan.
        var setup = Started(out var setupRepo, "residuelegacy", ship: true);
        setup.Stop();
        using (setupRepo)
        {
            var meta = setupRepo.LoadMetadata()!;
            meta.CreatedWithShipObjects = false;
            setupRepo.SaveMetadata(meta);
            setupRepo.SetBlock(location, new Vector3i(px, py + 2, pz), _content.GetBlock("iron_wall")!.NumericId.Value);
        }

        var legacy = Started(out var repo, "residuelegacy", ship: true);
        using (repo)
        {
            legacy.AddLocalPlayer("Pilot");
            var (origin, _) = legacy.LandedShipBoundsForTest("Pilot");
            Assert.Equal(py + 1, origin.Y); // on the median — the ghost hull was cleaned BEFORE the raised surface was read (#1367)
            Assert.True(legacy.World.GetBlock(new Vector3i(px, py + 2, pz)).IsAir);
        }
    }

    [Fact]
    public void TheRescue_FiresOncePerEntombment_WhenThePadFallbackIsWalledInToo()
    {
        var transport = new RecordingTransport();
        var server = Started(out var repo, "entombedpad", ship: false, transport);
        using (repo)
        {
            var (px, py, pz) = server.LandingPadForTest(0);
            var concrete = _content.GetBlock("concrete")!.NumericId;
            for (int y = py + 1; y <= py + 270; y++)
            {
                server.World.SetBlock(new Vector3i(px, y, pz), concrete); // a tower over the pad taller than the dig-out ever looks
            }

            var p = server.AddLocalPlayer("Buried");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(px + 0.5f, py - 6, pz + 0.5f); // sealed in the rock under the pad, the tower above
            Assert.True(Entombed(server, p.State.Position));

            for (int i = 0; i < 4; i++)
            {
                server.RunVoidRescueForTest(); // four seconds of rescue ticks
            }

            int Rescues() => transport.Sent.OfType<RespawnNotice>().Count(n => n.Reason == "@srv.misc.dug_out");
            Assert.Equal(1, Rescues()); // one rescue per episode (#1367) — not one per second into the same blocked spot
            Assert.Equal(py + 8 + 2, (int)Math.Floor(p.State.Position.Y)); // the pad fallback, itself inside the tower
            Assert.True(Entombed(server, p.State.Position));

            // The player digs themselves out: the episode ends, and nothing more is announced.
            server.World.SetBlock(new Vector3i(px, py + 10, pz), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
            server.World.SetBlock(new Vector3i(px, py + 11, pz), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
            server.RunVoidRescueForTest();
            Assert.False(Entombed(server, p.State.Position));
            Assert.Equal(1, Rescues());
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
