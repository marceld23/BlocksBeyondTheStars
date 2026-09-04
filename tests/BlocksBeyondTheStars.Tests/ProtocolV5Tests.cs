// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1534 (protocol v5): the world stream (chunks, block changes) rides its own LiteNetLib channel. The
/// client-side ordering against JoinAccepted / WorldReset is covered in the client test project
/// (<c>ProtocolV5ClientOrderingTests</c>).</summary>
public sealed class ProtocolV5Tests
{
    [Fact]
    public void Version_IsFive_AndBulkIsItsOwnChannel()
    {
        Assert.Equal(5, Protocol.Version);
        Assert.Equal(1, DeliveryMode.ReliableOrderedBulk.Channel());
        Assert.Equal(0, DeliveryMode.ReliableOrdered.Channel());
        Assert.Equal(0, DeliveryMode.Sequenced.Channel());
        Assert.Equal(LiteNetLib.DeliveryMethod.ReliableOrdered, DeliveryMode.ReliableOrderedBulk.ToLiteNetLib());
    }

    [Fact]
    public void LiteNetLib_DeliversBulkAndOrdinaryPayloads_OnTwoChannels()
    {
        int port = 34000 + Random.Shared.Next(1000);
        using var server = new LiteNetLibServerTransport(1);
        using var client = new LiteNetLibClientTransport();
        int? connectionId = null;
        var received = new List<byte>();
        server.ClientConnected += id => connectionId = id;
        client.PayloadReceived += p => received.Add(p[0]);

        server.Start(port);
        client.Connect("127.0.0.1", port);
        var sw = Stopwatch.StartNew();
        while (connectionId == null && sw.ElapsedMilliseconds < 5000)
        {
            server.Poll();
            client.Poll();
            Thread.Sleep(10);
        }

        Assert.NotNull(connectionId);
        server.Send(connectionId!.Value, new byte[] { 1, 10, 20 }, DeliveryMode.ReliableOrderedBulk);
        server.Send(connectionId.Value, new byte[] { 2, 30, 40 }, DeliveryMode.ReliableOrdered);
        server.Send(connectionId.Value, new byte[] { 3, 50, 60 }, DeliveryMode.ReliableOrderedBulk);
        while (received.Count < 3 && sw.ElapsedMilliseconds < 5000)
        {
            server.Poll();
            client.Poll();
            Thread.Sleep(10);
        }

        Assert.Equal(3, received.Count);
        Assert.Contains((byte)2, received);
        // The bulk channel keeps its own order.
        Assert.True(received.IndexOf(1) < received.IndexOf(3));
    }
}
