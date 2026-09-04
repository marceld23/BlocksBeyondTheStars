// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using LiteNetLib;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Protocol v4 (#1533 #1534 #1535): LZ4 block arrays above <see cref="NetCodec.CompressionMinLengthBytes"/>, the
/// presence beat on a sequenced delivery, and inventory updates that omit an unchanged blueprint list. The
/// version bump exists because a v3 reader cannot open an LZ4 body and would read an omitted blueprint list as
/// "none" — both pinned here.
/// </summary>
public sealed class ProtocolV4Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_v4_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly MessagePackSerializerOptions V3Options =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData);

    private static CreatureList BigCreatureList()
        => new()
        {
            Creatures = Enumerable.Range(0, 30).Select(i => new NetCreature
            {
                Id = "c" + i,
                SpeciesId = "fauna_grazer_" + (i % 3),
                Name = "Grazer",
                X = i * 3f,
                Y = 64f,
                Z = -i * 2f,
                Hull = 10f + i,
                HullMax = 40f,
            }).ToArray(),
        };

    [Fact]
    public void Version_IsFour()
    {
        Assert.Equal(4, Protocol.Version);
        Assert.Equal(4, new JoinRequest().ProtocolVersion);
    }

    [Fact]
    public void LargeMessages_CompressAboveTheThreshold_SmallOnesStayPlain()
    {
        NetCodec.UseJsonEncoding = false;
        var big = BigCreatureList();
        var plain = MessagePackSerializer.Serialize(big, V3Options);
        var encoded = NetCodec.Encode(big);
        Assert.True(plain.Length > NetCodec.CompressionMinLengthBytes, "the fixture must be above the threshold");
        Assert.True(encoded.Length < plain.Length / 3, $"a 30-creature list should shrink to well under a third ({encoded.Length} vs {plain.Length})");
        var back = Assert.IsType<CreatureList>(NetCodec.Decode(encoded));
        Assert.Equal(30, back.Creatures.Length);
        Assert.Equal("fauna_grazer_2", back.Creatures[29].SpeciesId);

        var small = new PlayerPresence { PlayerId = "p1", Name = "Alice", X = 1f, Y = 64f, Z = 2f };
        var smallPlain = MessagePackSerializer.Serialize(small, V3Options);
        var smallEncoded = NetCodec.Encode(small);
        Assert.True(smallPlain.Length < NetCodec.CompressionMinLengthBytes);
        Assert.Equal(smallPlain, smallEncoded.AsSpan(1).ToArray()); // byte-identical to v3 below the threshold
    }

    [Fact]
    public void CompressedBody_IsUnreadableForAV3Reader_WhichIsWhyTheVersionMoved()
    {
        NetCodec.UseJsonEncoding = false;
        var encoded = NetCodec.Encode(BigCreatureList());
        var body = new ReadOnlyMemory<byte>(encoded, 1, encoded.Length - 1);
        Assert.ThrowsAny<Exception>(() => MessagePackSerializer.Deserialize<CreatureList>(body, V3Options));
        Assert.NotNull(NetCodec.Decode(encoded)); // the v4 reader is fine
    }

    [Fact]
    public void CorruptedCompressedPayload_DecodesToNull_NeverThrows()
    {
        NetCodec.UseJsonEncoding = false;
        var encoded = NetCodec.Encode(BigCreatureList());
        for (int i = 1; i < encoded.Length; i += 7)
        {
            var mutated = (byte[])encoded.Clone();
            mutated[i] ^= 0xFF;
            var ex = Record.Exception(() => NetCodec.Decode(mutated));
            Assert.Null(ex);
        }

        for (int cut = 2; cut < encoded.Length; cut += 11)
        {
            var truncated = encoded.AsSpan(0, cut).ToArray();
            Assert.Null(Record.Exception(() => NetCodec.Decode(truncated)));
        }
    }

    [Fact]
    public void Sequenced_MapsToLiteNetLibSequenced()
    {
        Assert.Equal(DeliveryMethod.Sequenced, DeliveryMode.Sequenced.ToLiteNetLib());
        Assert.Equal(DeliveryMethod.ReliableOrdered, DeliveryMode.ReliableOrdered.ToLiteNetLib());
        Assert.Equal(DeliveryMethod.Unreliable, DeliveryMode.Unreliable.ToLiteNetLib());
    }

    private sealed class ModeRecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;
        public readonly List<(int Conn, object Msg, DeliveryMode Mode)> Sent = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m, mode));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m, mode));
        }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private (SvGameServer Server, ModeRecordingTransport Transport, SqliteWorldRepository Repo) NewServer(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var transport = new ModeRecordingTransport();
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return (server, transport, repo);
    }

    [Fact]
    public void PresenceBeat_IsSequenced_TheJoinSnapshotStaysReliable()
    {
        var (server, transport, repo) = NewServer("beat");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            bob.State.Position = new Vector3f(5, 64, 7);
            transport.Sent.Clear();
            server.Tick(0.2);

            var beats = transport.Sent.Where(x => x.Msg is PlayerPresence p && p.PlayerId == "Bob" && x.Conn == alice.ConnectionId).ToList();
            Assert.NotEmpty(beats);
            Assert.All(beats, b => Assert.Equal(DeliveryMode.Sequenced, b.Mode));

            // everything else on the stream is still reliable
            Assert.All(transport.Sent.Where(x => x.Msg is not PlayerPresence), s => Assert.NotEqual(DeliveryMode.Sequenced, s.Mode));
        }
    }

    [Fact]
    public void InventoryUpdate_OmitsTheBlueprintList_UntilItChanges()
    {
        var (server, transport, repo) = NewServer("blueprints");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            alice.State.UnlockedBlueprints.Add("bp_alpha");
            transport.Sent.Clear();

            server.SendInventoryForTest(alice);
            var first = (InventoryUpdate)transport.Sent.Last(x => x.Msg is InventoryUpdate).Msg;
            // the join already sent one list; whether this one repeats it depends on the join — what matters is
            // that the NEXT one, with nothing changed, omits it
            transport.Sent.Clear();
            server.SendInventoryForTest(alice);
            var second = (InventoryUpdate)transport.Sent.Last(x => x.Msg is InventoryUpdate).Msg;
            Assert.True(second.BlueprintsUnchanged);
            Assert.Empty(second.UnlockedBlueprints);

            alice.State.UnlockedBlueprints.Add("bp_beta");
            transport.Sent.Clear();
            server.SendInventoryForTest(alice);
            var third = (InventoryUpdate)transport.Sent.Last(x => x.Msg is InventoryUpdate).Msg;
            Assert.False(third.BlueprintsUnchanged);
            Assert.Contains("bp_alpha", third.UnlockedBlueprints);
            Assert.Contains("bp_beta", third.UnlockedBlueprints);
            _ = first;
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
