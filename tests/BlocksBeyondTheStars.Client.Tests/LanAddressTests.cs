// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The address the host screen shows friends (#984). Every case here is a machine that really exists in
/// the wild — a Hyper-V box, a VirtualBox box, a laptop with the cable unplugged — because the failure
/// mode is silent: a wrong address looks exactly as trustworthy as the right one, and the two players
/// only find out when the join times out.
/// </summary>
public sealed class LanAddressTests
{
    private static LanCandidate Wifi(string address) => new(address, hasGateway: true, physical: true, "Wi-Fi (Intel AX211)");

    [Fact]
    public void Pick_PrefersTheRealAdapterOverAHyperVSwitch()
    {
        // The order Windows enumerates them in on a dev box: the virtual switch comes first.
        var picked = LanAddress.Pick(new[]
        {
            new LanCandidate("172.28.16.1", hasGateway: false, physical: false, "vEthernet (Default Switch)"),
            Wifi("192.168.1.42"),
        });

        Assert.Equal("192.168.1.42", picked);
    }

    [Fact]
    public void Pick_SkipsVirtualBoxHostOnlyNet()
    {
        var picked = LanAddress.Pick(new[]
        {
            new LanCandidate("192.168.56.1", hasGateway: false, physical: true, "VirtualBox Host-Only Network"),
            new LanCandidate("192.168.1.7", hasGateway: true, physical: true, "Ethernet"),
        });

        Assert.Equal("192.168.1.7", picked);
    }

    [Fact]
    public void Pick_PrefersAGatewayedInterface()
    {
        // Same wording, same range: only the route tells them apart.
        var picked = LanAddress.Pick(new[]
        {
            new LanCandidate("192.168.4.9", hasGateway: false, physical: true, "Ethernet 2"),
            new LanCandidate("192.168.4.10", hasGateway: true, physical: true, "Ethernet"),
        });

        Assert.Equal("192.168.4.10", picked);
    }

    [Fact]
    public void Pick_PrefersAHomeLanRangeOverAMeshVpn()
    {
        var picked = LanAddress.Pick(new[]
        {
            new LanCandidate("100.101.102.103", hasGateway: true, physical: true, "tailscale0"),
            Wifi("10.0.0.5"),
        });

        Assert.Equal("10.0.0.5", picked);
    }

    [Theory]
    [InlineData("127.0.0.1")]      // loopback
    [InlineData("169.254.13.7")]   // link-local: no DHCP answered
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.251")]    // multicast
    [InlineData("::1")]            // not IPv4 at all
    [InlineData("192.168.1.999")]
    [InlineData("192.168.1")]
    [InlineData("")]
    public void Pick_NeverReturnsAnUnusableAddress(string address)
    {
        var picked = LanAddress.Pick(new[] { new LanCandidate(address, hasGateway: true, physical: true, "Ethernet") });

        Assert.Equal(LanAddress.Loopback, picked);
        Assert.False(LanAddress.IsUsable(address));
    }

    [Fact]
    public void Pick_FallsBackToLoopbackWhenTheMachineIsOffline()
    {
        Assert.Equal(LanAddress.Loopback, LanAddress.Pick(System.Array.Empty<LanCandidate>()));
        Assert.Equal(LanAddress.Loopback, LanAddress.Pick(null!));
    }

    [Fact]
    public void Pick_IsStableForEquallyGoodCandidates()
    {
        // Two real adapters on the same LAN (docked laptop: cable AND Wi-Fi). Either would work, so the
        // answer must at least not flip between two openings of the host screen.
        var candidates = new[] { Wifi("192.168.1.20"), Wifi("192.168.1.21") };

        Assert.Equal("192.168.1.20", LanAddress.Pick(candidates));
        Assert.Equal(LanAddress.Pick(candidates), LanAddress.Pick(candidates));
    }

    [Fact]
    public void Pick_TakesAGatewaylessRealAdapterOverNothing()
    {
        // Isolated switch, no router: friends plugged into it can still reach this address.
        var picked = LanAddress.Pick(new[] { new LanCandidate("192.168.9.4", hasGateway: false, physical: true, "Ethernet") });

        Assert.Equal("192.168.9.4", picked);
    }

    [Fact]
    public void Score_RanksTheRealAdapterAboveEveryVirtualOne()
    {
        var real = Wifi("192.168.1.42");
        foreach (string virtualName in new[] { "vEthernet (WSL)", "VMware Network Adapter VMnet8", "Docker0", "TAP-Windows Adapter V9" })
        {
            var fake = new LanCandidate("192.168.1.43", hasGateway: true, physical: true, virtualName);
            Assert.True(LanAddress.Score(real) > LanAddress.Score(fake), $"'{virtualName}' outranked the real adapter");
        }
    }
}
