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
/// #4011: a tester carved a room out of a mountain and its walls grew the terrain forms' grooves — the client could not tell
/// player-dug air from a natural cave. The server now marks DUG cells (natural rock, soil or ore a stored edit replaced) live
/// and from the edit rows at load, and streams them with the chunk; the client keeps the forms off rock facing them.
/// </summary>
public sealed class CarvedRockDugCellsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_dugcells_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static ChunkData SolidChunk(ushort block)
    {
        var blocks = new ushort[WorldConstants.BlocksPerChunk];
        Array.Fill(blocks, block);
        return ChunkData.FromRaw(new ChunkCoord(0, 0, 0), blocks);
    }

    [Theory]
    [InlineData("stone", true)]
    [InlineData("granite", true)]
    [InlineData("dirt", true)]
    [InlineData("sand", true)]
    [InlineData("snow", true)]
    [InlineData("iron_ore", true)]
    [InlineData("water", false)]       // drained water leaves a natural cell
    [InlineData("lava", false)]
    [InlineData("flora_flower", false)] // a picked plant leaves a natural cell
    [InlineData("wood_log", false)]     // a felled tree too
    [InlineData("concrete", false)]
    public void OnlyNaturalSolidTerrainAndOre_Count(string key, bool counts)
        => Assert.Equal(counts, DugCells.Counts(_content.GetBlock(key)));

    [Fact]
    public void TheLiveRule_DigsNaturalRock_NotAPlayersOwnBlockOrWeatherSnow()
    {
        var stone = _content.GetBlock("stone");
        var snow = _content.GetBlock("snow");
        Assert.True(DugCells.DigsLive(stone, previousEdited: false, nowAir: true));
        Assert.False(DugCells.DigsLive(stone, previousEdited: true, nowAir: true));  // a block the player placed
        Assert.False(DugCells.DigsLive(snow, previousEdited: true, nowAir: true));   // weather snow that melted
        Assert.False(DugCells.DigsLive(stone, previousEdited: false, nowAir: false)); // not mined to air
        Assert.Contains(stone!.NumericId.Value, DugCells.CountingIds(_content.Blocks.Values));
    }

    [Fact]
    public void DugMarks_AreSticky_RoundTripAsRuns_AndTravelWithTheMeshCopy()
    {
        var chunk = SolidChunk(3);
        int idx = WorldConstants.LocalIndex(1, 2, 3);
        long before = chunk.Version;

        chunk.MarkDugLocal(idx);
        Assert.True(chunk.IsDugLocal(idx));
        Assert.True(chunk.HasDug);
        Assert.True(chunk.Version > before); // the cached wire payload must not survive the mark
        Assert.False(chunk.IsDugLocal(idx + 1));

        chunk.Set(1, 2, 3, BlockId.Air);
        chunk.Set(1, 2, 3, new BlockId(3)); // a block placed into the tunnel — still not natural
        Assert.True(chunk.IsDugLocal(idx));

        var copy = SolidChunk(3);
        copy.CopyEditsFrom(chunk); // the client's mesh-worker copy
        Assert.True(copy.IsDugLocal(idx));

        var wire = SolidChunk(3);
        wire.MarkDugRuns(chunk.DugRuns());
        Assert.True(wire.IsDugLocal(idx));
        Assert.Equal(new ushort[] { (ushort)idx, 1 }, chunk.DugRuns());
        Assert.Empty(SolidChunk(3).DugRuns());
        wire.MarkDugRuns(new ushort[] { 4090, 100, 7 }); // malformed: under-marks, never throws
        Assert.True(wire.IsDugLocal(4095));
    }

    [Fact]
    public void MinedRock_StreamsAsDug_LiveAndAfterARestart()
    {
        NetCodec.UseJsonEncoding = false;
        var stone = _content.GetBlock("stone")!.NumericId;
        Vector3i mined;
        ChunkCoord coord;

        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "dug")))
        {
            var transport = new ChunkCapture();
            var server = Server(transport, repo);
            var player = server.AddLocalPlayer("Lyx");
            mined = NaturalRockBelow(server, player.State.Position);
            coord = WorldConstants.WorldToChunk(mined);

            server.World.SetBlock(mined, BlockId.Air, owner: "Lyx");
            Assert.True(IsDug(server, mined), "mined natural rock is a dug cell");
            server.World.SetBlock(mined, stone, owner: "Lyx"); // a block placed back into the hole …
            server.World.SetBlock(mined, BlockId.Air, owner: "Lyx"); // … and mined again: the cell stays dug
            Assert.True(IsDug(server, mined));

            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.1);
            }

            var seen = Received(transport, coord);
            Assert.True(seen.IsDugLocal(LocalIndex(mined)), "the live mine must reach the client as a dug cell");
            server.SaveNow();
            server.Stop();
        }

        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "dug")))
        {
            var transport = new ChunkCapture();
            var server = Server(transport, repo);
            server.AddLocalPlayer("Lyx");
            Assert.True(IsDug(server, mined), "an air row over generated rock is a dug cell after a restart");
            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.1);
            }

            Assert.True(Received(transport, coord).IsDugLocal(LocalIndex(mined)));
        }
    }

    [Fact]
    public void ABlockBuiltOnNaturalAir_AndMinedAgain_LeavesNaturalAir()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "air"));
        var server = Server(new ChunkCapture(), repo);
        var player = server.AddLocalPlayer("Lyx");
        var rock = NaturalRockBelow(server, player.State.Position);
        var air = rock;
        while (!server.World.GetBlock(air).IsAir)
        {
            air += new Vector3i(0, 1, 0); // up to the first open cell over the ground
        }

        server.World.SetBlock(air, _content.GetBlock("stone")!.NumericId, owner: "Lyx");
        server.World.SetBlock(air, BlockId.Air, owner: "Lyx");
        Assert.False(IsDug(server, air), "a wall the player built and took down again leaves natural air");
    }

    private static int LocalIndex(Vector3i world)
    {
        var local = WorldConstants.WorldToLocal(world);
        return WorldConstants.LocalIndex(local.X, local.Y, local.Z);
    }

    private static bool IsDug(SvGameServer server, Vector3i world)
        => server.World.GetOrLoadChunk(WorldConstants.WorldToChunk(world)).IsDugLocal(LocalIndex(world));

    /// <summary>The first natural counting cell (rock, soil, ore) under the column two blocks beside the player.</summary>
    private Vector3i NaturalRockBelow(SvGameServer server, Vector3f position)
    {
        var cell = new Vector3i((int)MathF.Floor(position.X) + 2, (int)MathF.Floor(position.Y) + 4, (int)MathF.Floor(position.Z) + 2);
        for (int i = 0; i < 64; i++, cell -= new Vector3i(0, 1, 0))
        {
            var chunk = server.World.GetOrLoadChunk(WorldConstants.WorldToChunk(cell));
            if (DugCells.Counts(server.World.Definition(server.World.GetBlock(cell))) && !chunk.IsEditedLocal(LocalIndex(cell)))
            {
                return cell;
            }
        }

        throw new InvalidOperationException("no natural rock under the spawn");
    }

    private SvGameServer Server(ChunkCapture transport, SqliteWorldRepository repo)
    {
        var config = new ServerConfig
        {
            WorldName = "dug",
            Seed = 4011,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 1,
            ChunkStreamPerTick = 64,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>The client's view of a streamed chunk: its blocks plus the dug runs, as ClientWorld.StoreChunk restores it.</summary>
    private static ChunkData Received(ChunkCapture transport, ChunkCoord coord)
    {
        Assert.True(transport.Chunks.TryGetValue(coord, out var m), $"chunk {coord} was not streamed");
        var chunk = ChunkData.FromRaw(coord, m!.DecodeBlocks(WorldConstants.BlocksPerChunk)!);
        chunk.MarkDugRuns(m.DugRuns);
        return chunk;
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
