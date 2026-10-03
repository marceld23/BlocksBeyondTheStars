// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The client's half of the content fingerprint (#2222): it names its block palette in the join, and it refuses a
/// world whose palette is not its own — with the reason the server uses for the same thing, before a single chunk
/// is drawn.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class ContentFingerprintClientTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    /// <summary>An in-memory client transport that records what the client sends and whether it hung up.</summary>
    private sealed class RecordingClientTransport : IClientTransport
    {
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<byte[]>? PayloadReceived;

        public List<object?> Sent { get; } = new();

        public int Disconnects { get; private set; }

        public void Connect(string host, int port) => Connected?.Invoke();

        public void Send(byte[] payload, DeliveryMode mode) => Sent.Add(NetCodec.Decode(payload));

        public void Poll()
        {
        }

        public void Disconnect()
        {
            Disconnects++;
            Disconnected?.Invoke();
        }

        public void Dispose()
        {
        }

        public void Deliver(object message) => PayloadReceived?.Invoke(NetCodec.Encode(message));
    }

    [Fact]
    public void Join_CarriesTheClientsFingerprint()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport) { ContentFingerprint = "palette-a" };

        client.Join("Pilot");

        var join = Assert.IsType<JoinRequest>(Assert.Single(transport.Sent));
        Assert.Equal("palette-a", join.ContentFingerprint);
        Assert.Equal(Protocol.Version, join.ProtocolVersion);
    }

    [Fact]
    public void Join_WithoutAnExplicitFingerprint_NamesTheContentTheProcessLoaded()
    {
        // The game's client is never handed a fingerprint: it loads one content set and the join names that one.
        // (Every content this test process loads is the shipped data, so "the one built last" is this one.)
        var content = LoadContent();
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport);

        client.Join("Pilot");

        var join = Assert.IsType<JoinRequest>(Assert.Single(transport.Sent));
        Assert.Equal(content.BlockFingerprint, join.ContentFingerprint);
    }

    [Fact]
    public void MatchingServer_IsJoined()
    {
        using var h = new ClientServerHarness(LoadContent());

        h.Join("Pilot");

        Assert.Null(h.JoinRejected);
        Assert.NotNull(h.JoinAccepted);
        Assert.Equal(h.Client.ContentFingerprint, h.JoinAccepted!.ContentFingerprint);
        Assert.True(h.Client.Connected);
    }

    [Fact]
    public void ServerRefusesAClient_WhosePaletteIsAnotherOne()
    {
        using var h = new ClientServerHarness(LoadContent());
        h.Client.ContentFingerprint = "0123456789abcdef"; // a build with another block set

        h.Join("Pilot");

        Assert.Null(h.JoinAccepted);
        Assert.NotNull(h.JoinRejected);
        Assert.Equal(Protocol.ContentMismatchReason, h.JoinRejected!.Reason);
    }

    [Theory]
    [InlineData("palette-b")] // a server with another block set
    [InlineData("")]          // a server that names none
    public void ClientRefusesAServer_WhosePaletteIsMissingOrDifferent(string serverFingerprint)
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport) { ContentFingerprint = "palette-a" };
        var accepted = new List<JoinAccepted>();
        var rejected = new List<JoinRejected>();
        int chunks = 0;
        int messages = 0;
        client.JoinAccepted += accepted.Add;
        client.JoinRejected += rejected.Add;
        client.ChunkReceived += _ => chunks++;
        client.ServerMessageReceived += _ => messages++;
        client.Connect("scripted", 0);
        Assert.True(client.Connected);

        // A chunk that overtook the JoinAccepted (held back), the JoinAccepted itself, and what follows it.
        transport.Deliver(new ChunkDataMessage { Cx = 1, WorldId = 1, Blocks = new ushort[WorldConstants.BlocksPerChunk] });
        transport.Deliver(new JoinAccepted { WorldId = 1, PlayerId = "Pilot", ContentFingerprint = serverFingerprint });
        transport.Deliver(new ServerMessage { Text = "welcome" });
        transport.Deliver(new ChunkDataMessage { Cx = 2, WorldId = 1, Blocks = new ushort[WorldConstants.BlocksPerChunk] });
        client.Poll();
        client.Poll();

        Assert.Empty(accepted);                                             // the host never enters the world
        Assert.Equal(Protocol.ContentMismatchReason, Assert.Single(rejected).Reason); // the reason a server refusal carries
        Assert.Equal(1, transport.Disconnects);                             // and the client hung up
        Assert.False(client.Connected);
        Assert.Equal(0, chunks);                                            // nothing of that world was handed on
        Assert.Equal(0, messages);
        Assert.Equal(0, client.PendingPayloads);
    }

    [Fact]
    public void AfterRefusingAServer_NothingItStillDelivers_ReachesTheHost_UntilTheNextConnect()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport) { ContentFingerprint = "palette-a" };
        var accepted = new List<JoinAccepted>();
        int chunks = 0;
        int messages = 0;
        int inventories = 0;
        client.JoinAccepted += accepted.Add;
        client.ChunkReceived += _ => chunks++;
        client.ServerMessageReceived += _ => messages++;
        client.InventoryUpdated += _ => inventories++;
        client.Connect("scripted", 0);

        // The join burst arrives in two halves: the refusal happens in the first poll …
        transport.Deliver(new JoinAccepted { WorldId = 1, PlayerId = "Pilot", ContentFingerprint = "palette-b" });
        client.Poll();
        Assert.Equal(1, transport.Disconnects);

        // … and the transport still hands over the second half afterwards.
        transport.Deliver(new InventoryUpdate());
        transport.Deliver(new ServerMessage { Text = "welcome" });
        transport.Deliver(new ChunkDataMessage { Cx = 2, WorldId = 1, Blocks = new ushort[WorldConstants.BlocksPerChunk] });
        client.Poll();
        client.Poll();

        Assert.Equal(0, inventories);
        Assert.Equal(0, messages);
        Assert.Equal(0, chunks);
        Assert.Equal(0, client.PendingPayloads);
        Assert.Empty(accepted);

        // A new connection is judged afresh: a server with the right palette is joined as usual.
        client.Connect("scripted", 0);
        transport.Deliver(new JoinAccepted { WorldId = 1, PlayerId = "Pilot", ContentFingerprint = "palette-a" });
        transport.Deliver(new ServerMessage { Text = "welcome" });
        client.Poll();

        Assert.Single(accepted);
        Assert.Equal(1, messages);
    }
}
