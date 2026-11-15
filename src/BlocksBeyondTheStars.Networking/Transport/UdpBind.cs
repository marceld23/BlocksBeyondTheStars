// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace BlocksBeyondTheStars.Networking.Transport;

/// <summary>
/// Which local addresses the native UDP transports bind (#3707). Windows Firewall asks the player to allow a
/// program the moment it binds a UDP socket to a fixed port on a network address — it then looks like a
/// listener. LiteNetLib's default does exactly that even for a client that only dials out: it binds IPv4 to a
/// dynamic port and then IPv6 to that same, now fixed, port. Binding only what the connection needs keeps the
/// dialog away: loopback for a server on this machine, IPv4 alone for an IPv4 server. Measured on Windows 11:
/// loopback binds and an IPv4-only client raise no dialog, the default client and an any-address server do.
/// </summary>
public static class UdpBind
{
    /// <summary>The local sockets a client opens for one target.</summary>
    public readonly struct ClientSockets : IEquatable<ClientSockets>
    {
        public ClientSockets(bool loopback, bool ipv6)
        {
            Loopback = loopback;
            IPv6 = ipv6;
        }

        /// <summary>Bind to the loopback address only (the target is this machine).</summary>
        public bool Loopback { get; }

        /// <summary>Also open the IPv6 socket (the target is an IPv6 address).</summary>
        public bool IPv6 { get; }

        public bool Equals(ClientSockets other) => Loopback == other.Loopback && IPv6 == other.IPv6;

        public override bool Equals(object? obj) => obj is ClientSockets other && Equals(other);

        public override int GetHashCode() => (Loopback ? 1 : 0) | (IPv6 ? 2 : 0);

        public static bool operator ==(ClientSockets left, ClientSockets right) => left.Equals(right);

        public static bool operator !=(ClientSockets left, ClientSockets right) => !left.Equals(right);

        public override string ToString() => $"loopback={Loopback}, ipv6={IPv6}";
    }

    /// <summary>The sockets a client needs to reach <paramref name="target"/>: loopback for a loopback target,
    /// and the IPv6 socket only for an IPv6 target. An IPv6 target off this machine keeps LiteNetLib's fixed-port
    /// IPv6 bind, so Windows may still ask there.</summary>
    public static ClientSockets ForTarget(IPAddress target)
    {
        var address = Normalize(target);
        return new ClientSockets(IPAddress.IsLoopback(address), address.AddressFamily == AddressFamily.InterNetworkV6);
    }

    /// <summary>The address to dial out of a host's resolved addresses: the first IPv4 one, else the first IPv6
    /// one, else <c>null</c>. IPv4 first, because an IPv4 connection needs no IPv6 socket.</summary>
    public static IPAddress? PickTarget(IReadOnlyList<IPAddress> resolved)
    {
        IPAddress? ipv6 = null;
        foreach (var candidate in resolved)
        {
            var address = Normalize(candidate);
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return address;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                ipv6 ??= address;
            }
        }

        return ipv6;
    }

    /// <summary>Resolves a host name or address literal to the address to dial, or <c>null</c> when it does not
    /// resolve (the caller then lets LiteNetLib report the failure the way it always did).</summary>
    public static IPAddress? Resolve(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        host = host.Trim();
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return Normalize(literal);
        }

        try
        {
            return PickTarget(Dns.GetHostAddresses(host));
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a server's gameplay bind address. Empty, <c>any</c>, <c>*</c>, <c>+</c> or <c>0.0.0.0</c> bind every
    /// interface (IPv4 and IPv6); <c>localhost</c>, <c>loopback</c>, <c>127.0.0.1</c> or <c>::1</c> bind this machine
    /// only; any other IPv4 address binds that one interface, without IPv6.
    /// </summary>
    /// <param name="text">The configured value.</param>
    /// <param name="ipv4">The IPv4 address to bind.</param>
    /// <param name="ipv6">The IPv6 address to bind, or <c>null</c> for no IPv6 socket.</param>
    /// <returns><c>false</c> when the value is none of the above.</returns>
    public static bool TryParseServerBind(string? text, out IPAddress ipv4, out IPAddress? ipv6)
    {
        ipv4 = IPAddress.Any;
        ipv6 = IPAddress.IPv6Any;
        string value = (text ?? string.Empty).Trim().ToLowerInvariant();
        switch (value)
        {
            case "":
            case "any":
            case "*":
            case "+":
            case "0.0.0.0":
                return true;
            case "localhost":
            case "loopback":
            case "127.0.0.1":
            case "::1":
                ipv4 = IPAddress.Loopback;
                ipv6 = IPAddress.IPv6Loopback;
                return true;
        }

        if (IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork)
        {
            ipv4 = address;
            ipv6 = null;
            return true;
        }

        return false;
    }

    /// <summary>True when <paramref name="text"/> binds this machine only — a server nothing else can reach.</summary>
    public static bool IsLoopbackBind(string? text)
        => TryParseServerBind(text, out var ipv4, out _) && IPAddress.IsLoopback(ipv4);

    private static IPAddress Normalize(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
