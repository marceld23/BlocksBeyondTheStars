// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
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
/// #3118: the client keeps its ground details off built ground, so the server tells it which cells are built — every
/// non-air cell with a stored delta (a player's build, a settlement stamp, weather snow) — as runs on the chunk message.
/// A tester's prepared planting soil, rock floor and roof garden grew "weeds" because the client only saw block types.
/// </summary>
public sealed class GroundDetailEditsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_groundedits_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static ChunkData SolidChunk(ushort block)
    {
        var blocks = new ushort[WorldConstants.BlocksPerChunk];
        Array.Fill(blocks, block);
        return ChunkData.FromRaw(new ChunkCoord(0, 0, 0), blocks);
    }

    [Fact]
    public void EditedCells_AreMarked_AirNeverIs_AndAirClearsTheMark()
    {
        var chunk = SolidChunk(3);
        int idx = WorldConstants.LocalIndex(1, 2, 3);
        long before = chunk.Version;

        chunk.MarkEditedLocal(idx);
        Assert.True(chunk.IsEditedLocal(idx));
        Assert.True(chunk.HasEdits);
        Assert.True(chunk.Version > before);
        Assert.False(chunk.IsEditedLocal(idx + 1));

        var copy = SolidChunk(3);
        copy.CopyEditsFrom(chunk); // the client's mesh-worker copy
        Assert.True(copy.IsEditedLocal(idx));

        chunk.Set(1, 2, 3, new BlockId(BlockId.AirValue)); // mined: the cell drops out
        Assert.False(chunk.IsEditedLocal(idx));
        Assert.False(chunk.HasEdits);
        Assert.True(copy.IsEditedLocal(idx)); // a copy, not a shared set
        chunk.MarkEditedLocal(idx); // an air cell is never built ground
        Assert.False(chunk.IsEditedLocal(idx));
    }

    [Fact]
    public void EditedRuns_RoundTrip_AndPackContiguousCellsIntoOneRun()
    {
        var rng = new Random(3118);
        for (int trial = 0; trial < 30; trial++)
        {
            var source = SolidChunk(5);
            var expected = new HashSet<int>();
            int count = rng.Next(0, 600);
            for (int i = 0; i < count; i++)
            {
                int cell = rng.Next(0, WorldConstants.BlocksPerChunk);
                source.MarkEditedLocal(cell);
                expected.Add(cell);
            }

            var copy = SolidChunk(5);
            copy.MarkEditedRuns(source.EditedRuns());
            for (int cell = 0; cell < WorldConstants.BlocksPerChunk; cell++)
            {
                Assert.Equal(expected.Contains(cell), copy.IsEditedLocal(cell));
            }
        }

        var floor = SolidChunk(5);
        for (int cell = 0; cell < WorldConstants.ChunkSize * WorldConstants.ChunkSize; cell++)
        {
            floor.MarkEditedLocal(cell); // a whole built floor layer
        }

        Assert.Equal(new ushort[] { 0, 256 }, floor.EditedRuns());
        Assert.Empty(SolidChunk(5).EditedRuns());
    }

    [Fact]
    public void MalformedRuns_UnderMark_AndNeverThrow()
    {
        var chunk = SolidChunk(5);
        chunk.MarkEditedRuns(new ushort[] { 4090, 100, 7 }); // runs past the chunk end + an odd trailing entry
        Assert.True(chunk.IsEditedLocal(4095));
        Assert.False(chunk.IsEditedLocal(7));
        chunk.MarkEditedRuns(null);
    }

    private sealed class ChunkCapture : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;
        public readonly Dictionary<ChunkCoord, ChunkDataMessage> Chunks = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.IsMessageType<ChunkDataMessage>(payload) && NetCodec.Decode(payload) is ChunkDataMessage m)
            {
                Chunks[new ChunkCoord(m.Cx, m.Cy, m.Cz)] = m;
            }
        }
        public void Broadcast(byte[] payload, DeliveryMode mode) { }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer Server(ChunkCapture transport, SqliteWorldRepository repo)
    {
        var config = new ServerConfig
        {
            WorldName = "edits",
            Seed = 3118,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 1,
            ChunkStreamPerTick = 64,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>The client's view of a streamed chunk: its blocks plus the edit runs, as ClientWorld.StoreChunk restores it.</summary>
    private static ChunkData Received(ChunkCapture transport, ChunkCoord coord)
    {
        Assert.True(transport.Chunks.TryGetValue(coord, out var m), $"chunk {coord} was not streamed");
        var chunk = ChunkData.FromRaw(coord, m!.DecodeBlocks(WorldConstants.BlocksPerChunk)!);
        chunk.MarkEditedRuns(m.EditRuns);
        return chunk;
    }

    [Fact]
    public void ABuiltCell_StreamsAsEdited_LiveAndAfterARestart_ItsNaturalNeighbourDoesNot()
    {
        NetCodec.UseJsonEncoding = false;
        var stone = _content.GetBlock("stone")!.NumericId;
        Vector3i built;
        ChunkCoord coord;
        int idx, neighbour;

        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "edits")))
        {
            var transport = new ChunkCapture();
            var server = Server(transport, repo);
            var player = server.AddLocalPlayer("Lyx");
            var p = player.State.Position;
            built = new Vector3i((int)MathF.Floor(p.X) + 2, (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z) + 2);
            coord = WorldConstants.WorldToChunk(built);
            var local = WorldConstants.WorldToLocal(built);
            idx = WorldConstants.LocalIndex(local.X, local.Y, local.Z);
            neighbour = idx ^ 1; // the next cell along x within the same chunk row

            server.World.SetBlock(built, stone, owner: "Lyx");
            Assert.True(server.World.GetOrLoadChunk(coord).IsEditedLocal(idx));
            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.1);
            }

            var seen = Received(transport, coord);
            Assert.True(seen.IsEditedLocal(idx), "the live build must reach the client as built ground");
            Assert.False(seen.IsEditedLocal(neighbour));
            server.SaveNow();
            server.Stop();
        }

        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "edits")))
        {
            var transport = new ChunkCapture();
            var server = Server(transport, repo);
            server.AddLocalPlayer("Lyx");
            Assert.True(server.World.GetOrLoadChunk(coord).IsEditedLocal(idx), "reloaded edits are built ground");
            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.1);
            }

            Assert.True(Received(transport, coord).IsEditedLocal(idx));
        }
    }

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
            // best effort
        }
    }
}
