// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Net;
using System.Net.Sockets;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Which local addresses the UDP transports bind (#3707). Windows Firewall asks about a program that binds a
/// fixed UDP port on a network address, so singleplayer binds loopback and a client opens only the socket its
/// target needs. The dialog itself cannot be asserted here; what can is the choice that keeps it away, and that
/// a loopback-bound pair still connects.
/// </summary>
public sealed class UdpBindTests
{
    [Theory]
    [InlineData("127.0.0.1", true, false)]
    [InlineData("127.8.9.10", true, false)]
    [InlineData("::1", true, true)]
    [InlineData("192.168.1.20", false, false)]
    [InlineData("203.0.113.7", false, false)]
    [InlineData("2001:db8::7", false, true)]
    [InlineData("::ffff:192.168.1.20", false, false)] // an IPv4 address in IPv6 clothing is still an IPv4 target
    public void ForTarget_OpensOnlyWhatTheTargetNeeds(string target, bool loopback, bool ipv6)
        => Assert.Equal(new UdpBind.ClientSockets(loopback, ipv6), UdpBind.ForTarget(IPAddress.Parse(target)));

    [Fact]
    public void PickTarget_PrefersIPv4_BecauseItNeedsNoIPv6Socket()
    {
        var resolved = new[] { IPAddress.Parse("2001:db8::7"), IPAddress.Parse("203.0.113.7"), IPAddress.Parse("203.0.113.8") };

        Assert.Equal(IPAddress.Parse("203.0.113.7"), UdpBind.PickTarget(resolved));
    }

    [Fact]
    public void PickTarget_TakesIPv6WhenThatIsAllThereIs()
    {
        Assert.Equal(IPAddress.Parse("2001:db8::7"), UdpBind.PickTarget(new[] { IPAddress.Parse("2001:db8::7") }));
        Assert.Null(UdpBind.PickTarget(Array.Empty<IPAddress>()));
    }

    [Theory]
    [InlineData("localhost", "127.0.0.1")]
    [InlineData("LOCALHOST", "127.0.0.1")]
    [InlineData(" 192.168.1.20 ", "192.168.1.20")]
    [InlineData("::1", "::1")]
    public void Resolve_ReadsLiteralsWithoutALookup(string host, string expected)
        => Assert.Equal(IPAddress.Parse(expected), UdpBind.Resolve(host));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ReturnsNullForNoHost(string host) => Assert.Null(UdpBind.Resolve(host));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("any")]
    [InlineData("*")]
    [InlineData("+")]
    [InlineData("0.0.0.0")]
    public void ServerBind_DefaultsToEveryInterface(string? text)
    {
        Assert.True(UdpBind.TryParseServerBind(text, out var ipv4, out var ipv6));
        Assert.Equal(IPAddress.Any, ipv4);
        Assert.Equal(IPAddress.IPv6Any, ipv6);
        Assert.False(UdpBind.IsLoopbackBind(text));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("Loopback")]
    [InlineData("::1")]
    public void ServerBind_LoopbackBindsThisMachineOnly(string text)
    {
        Assert.True(UdpBind.TryParseServerBind(text, out var ipv4, out var ipv6));
        Assert.Equal(IPAddress.Loopback, ipv4);
        Assert.Equal(IPAddress.IPv6Loopback, ipv6);
        Assert.True(UdpBind.IsLoopbackBind(text));
    }

    [Fact]
    public void ServerBind_OneIPv4AddressBindsThatInterfaceWithoutIPv6()
    {
        Assert.True(UdpBind.TryParseServerBind("192.168.1.20", out var ipv4, out var ipv6));
        Assert.Equal(IPAddress.Parse("192.168.1.20"), ipv4);
        Assert.Null(ipv6);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("2001:db8::7")]
    [InlineData("127.0.0.1:31550")]
    public void ServerBind_RefusesAnythingElse(string text)
    {
        Assert.False(UdpBind.TryParseServerBind(text, out _, out _));
        Assert.Throws<ArgumentException>(() => new LiteNetLibServerTransport(1, text));
    }

    /// <summary>The singleplayer pair: a server bound to loopback and a client dialing 127.0.0.1 connect and talk.</summary>
    [Fact]
    public void LoopbackBoundServer_TakesALoopbackClient()
    {
        int port = FreeUdpPort();
        using var server = new LiteNetLibServerTransport(2, "127.0.0.1");
        using var client = new LiteNetLibClientTransport();
        int? connection = null;
        byte[]? received = null;
        server.ClientConnected += id => connection = id;
        server.PayloadReceived += (_, payload) => received = payload;
        server.Start(port);

        client.Connect("127.0.0.1", port);
        client.Connect("127.0.0.1", port); // the connect retry dials again and must keep the sockets it has
        Assert.True(PollUntil(() => connection != null, server, client), "a loopback client should reach a loopback-bound server");

        client.Send(new byte[] { 42 }, DeliveryMode.ReliableOrdered);
        Assert.True(PollUntil(() => received != null, server, client), "the payload should arrive");
        Assert.Equal(new byte[] { 42 }, received);
    }

    private static bool PollUntil(Func<bool> condition, LiteNetLibServerTransport server, LiteNetLibClientTransport client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            server.Poll();
            client.Poll();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}
