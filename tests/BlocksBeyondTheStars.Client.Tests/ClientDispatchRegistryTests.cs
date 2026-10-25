// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2894: the client's message dispatch became a type-keyed registry (<c>NetworkClientDispatch.cs</c>) instead of a
/// 136-arm type-pattern <c>switch</c>, mirroring the server's <c>GameServerDispatch.cs</c>. These facts pin that no
/// message lost its event, that the exact-type lookup still equals the pattern switch, and how the two registries
/// cover the protocol between them. The arms that did more than raise an event stay covered by their live tests:
/// <c>ContentFingerprintClientTests</c> (a JoinAccepted from a mismatched server is refused),
/// <c>HotbarSlotSyncTests</c> (the hotbar resync on JoinAccepted and WorldReset), <c>BrowserWorldResetTests</c> and
/// <c>ProtocolV5ClientOrderingTests</c> (the world-stream guard, the replay after a WorldReset and the far-terrain
/// world check), <c>ReceiveBudgetTests</c> and <c>ShipVisitClientTests</c>.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class ClientDispatchRegistryTests
{
    [Fact]
    public void TheClientRegistry_HoldsEveryStateMessageTheSwitchHandled()
    {
        var types = NetworkClient.HandledMessageTypesForTest.ToHashSet();

        Assert.Equal(139, types.Count); // #2971 EnemyLog, #2972 PvpTargets + DuelNotice
        Assert.Contains(typeof(JoinAccepted), types);           // the switch's first arm
        Assert.Contains(typeof(BanditEncounterResult), types);  // and its last
        Assert.Contains(typeof(VoiceFrame), types);             // the one message both sides handle
        Assert.Contains(typeof(WorldReset), types);             // an arm that had an inline body
        Assert.DoesNotContain(typeof(MoveIntent), types);       // client → server
    }

    [Fact]
    public void EveryHandledMessage_IsARegisteredSealedMessage()
    {
        // Sealed is what makes the exact-type lookup equal to the former `case X m:` pattern: no subclass can arrive.
        Assert.All(NetworkClient.HandledMessageTypesForTest, t =>
        {
            Assert.True(NetCodec.RegisteredMessageTags.ContainsKey(t), t.Name + " is not registered with NetCodec");
            Assert.True(t.IsSealed, t.Name + " is not sealed");
        });
    }

    [Fact]
    public void EveryRegisteredMessage_IsHandledByExactlyOneSide_ExceptVoiceAndTheJoinRequest()
    {
        var server = SvGameServer.HandledIntentTypesForTest.ToHashSet();
        var client = NetworkClient.HandledMessageTypesForTest.ToHashSet();
        var registered = NetCodec.RegisteredMessageTags.Keys.ToHashSet();

        // Voice is relayed: the server forwards a speaker's frame and every listener's client plays it.
        Assert.Equal(new[] { typeof(VoiceFrame) }, server.Intersect(client));

        // JoinRequest is answered in OnPayload before Dispatch. It is the only gap since the never-sent, never-handled
        // WeatherForecastRequest (tag 200) was retired (#2928): a registered message neither side handles is dead wire.
        var unhandled = registered.Except(server).Except(client).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(new[] { "JoinRequest" }, unhandled);
    }

    [Fact]
    public void AnUnknownTag_IsIgnoredWithoutAnEvent()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport);
        int messages = 0;
        int accepted = 0;
        client.ServerMessageReceived += _ => messages++;
        client.JoinAccepted += _ => accepted++;
        client.Connect("scripted", 0);
        Assert.False(NetCodec.RegisteredMessages.ContainsKey(253)); // the tag below really is unknown

        // Both decode to null; the old switch matched no arm and the registry must not trip over the null either.
        transport.DeliverRaw(new byte[] { 253 });
        transport.DeliverRaw(Array.Empty<byte>());
        client.Poll();

        Assert.Equal(0, messages);
        Assert.Equal(0, accepted);
        Assert.Equal(0, client.PendingPayloads);

        // And the stream carries on: the next real message still reaches its event.
        transport.Deliver(new ServerMessage { Text = "after" });
        client.Poll();
        Assert.Equal(1, messages);
    }
}
