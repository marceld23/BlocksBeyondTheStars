// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Net;
using LiteNetLib;

namespace BlocksBeyondTheStars.Networking.Transport;

/// <summary>Maps our <see cref="DeliveryMode"/> onto LiteNetLib delivery methods.</summary>
public static class DeliveryMapping
{
    public static DeliveryMethod ToLiteNetLib(this DeliveryMode mode) => mode switch
    {
        DeliveryMode.ReliableOrdered => DeliveryMethod.ReliableOrdered,
        DeliveryMode.ReliableOrderedBulk => DeliveryMethod.ReliableOrdered,
        DeliveryMode.Unreliable => DeliveryMethod.Unreliable,
        DeliveryMode.Sequenced => DeliveryMethod.Sequenced, // #1534: its own queue — not behind chunk resends
        _ => DeliveryMethod.ReliableOrdered,
    };

    /// <summary>The LiteNetLib channel: the world stream (chunks, block changes) rides channel 1, everything
    /// else channel 0 (#1534). Both peers run <see cref="ChannelCount"/> channels — protocol v5.</summary>
    public static byte Channel(this DeliveryMode mode) => mode == DeliveryMode.ReliableOrderedBulk ? (byte)1 : (byte)0;

    public const byte ChannelCount = 2;
}

/// <summary>
/// UDP server transport built on LiteNetLib. Lightweight and dependency-free of any game
/// engine, so it runs on a plain .NET host. Connection ids are
/// LiteNetLib peer ids.
/// </summary>
public sealed class LiteNetLibServerTransport : IServerTransport
{
    private const string ConnectionKey = "blocks-beyond-the-stars";

    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private readonly Dictionary<int, NetPeer> _peers = new();
    private readonly int _maxConnections;

    public event Action<int>? ClientConnected;
    public event Action<int>? ClientDisconnected;
    public event Action<int, byte[]>? PayloadReceived;

    /// <summary>Raised when a connection request is refused because every transport slot is taken — a silent
    /// reject is undebuggable (the server log showed nothing at all for a player who could not get in, #964).</summary>
    public event Action? ConnectionRejected;

    private readonly IPAddress _bindIPv4;
    private readonly IPAddress? _bindIPv6;

    /// <param name="maxConnections">Transport slots.</param>
    /// <param name="bindAddress">Which interfaces the gameplay port binds (<see cref="UdpBind.TryParseServerBind"/>):
    /// empty = every interface, <c>127.0.0.1</c> = this machine only — the bundled singleplayer server uses that,
    /// so Windows Firewall has nothing to ask about (#3707).</param>
    public LiteNetLibServerTransport(int maxConnections = 16, string? bindAddress = null)
    {
        (_bindIPv4, _bindIPv6) = ParseBind(bindAddress);
        _maxConnections = maxConnections;
        _manager = new NetManager(_listener)
        {
            AutoRecycle = true,
            // Spell the timeouts out rather than inheriting library defaults (#964): how fast a dead peer
            // frees its slot is gameplay-visible — it decides how long a crashed player's session keeps
            // holding their name. NOTE this only covers a peer that stops ANSWERING; a client whose game
            // froze but whose transport thread still pings looks perfectly alive here, which is why the
            // server also runs an app-level heartbeat on top.
            DisconnectTimeout = 10000,
            PingInterval = 1000,
            ChannelsCount = DeliveryMapping.ChannelCount, // #1534
        };

        _listener.ConnectionRequestEvent += request =>
        {
            if (_manager.ConnectedPeersCount < _maxConnections)
            {
                request.AcceptIfKey(ConnectionKey);
            }
            else
            {
                request.Reject();
                ConnectionRejected?.Invoke();
            }
        };

        _listener.PeerConnectedEvent += peer =>
        {
            _peers[peer.Id] = peer;
            ClientConnected?.Invoke(peer.Id);
        };

        _listener.PeerDisconnectedEvent += (peer, _) =>
        {
            _peers.Remove(peer.Id);
            ClientDisconnected?.Invoke(peer.Id);
        };

        _listener.NetworkReceiveEvent += (peer, reader, _, _) =>
        {
            // Drop oversized native packets before they reach the decoder. LiteNetLib reliable
            // fragmentation can assemble buffers far above MTU; without this ceiling a malicious client
            // could push a multi-MB payload (mirrors the WebSocket path's frame cap). No real intent
            // is anywhere near this size.
            int available = reader.AvailableBytes;
            if (available <= 0 || available > NetCodec.MaxPacketBytes)
            {
                return;
            }

            var bytes = reader.GetRemainingBytes();
            PayloadReceived?.Invoke(peer.Id, bytes);
        };
    }

    private static (IPAddress IPv4, IPAddress? IPv6) ParseBind(string? bindAddress)
        => UdpBind.TryParseServerBind(bindAddress, out var ipv4, out var ipv6)
            ? (ipv4, ipv6)
            : throw new ArgumentException($"Not a bind address: '{bindAddress}'.", nameof(bindAddress));

    public void Start(int port)
    {
        _manager.IPv6Enabled = _bindIPv6 != null;
        _manager.Start(_bindIPv4, _bindIPv6 ?? IPAddress.IPv6Any, port);
    }

    public void Send(int connectionId, byte[] payload, DeliveryMode mode)
    {
        if (_peers.TryGetValue(connectionId, out var peer))
        {
            peer.Send(payload, mode.Channel(), mode.ToLiteNetLib());
        }
    }

    public void Broadcast(byte[] payload, DeliveryMode mode)
        => _manager.SendToAll(payload, mode.Channel(), mode.ToLiteNetLib());

    /// <summary>Disconnects one native peer (kick). LiteNetLib flushes the reliable queue before the
    /// disconnect packet, so a message sent just before this still reaches the client.</summary>
    public void DisconnectClient(int connectionId)
    {
        if (_peers.TryGetValue(connectionId, out var peer))
        {
            _manager.DisconnectPeer(peer);
        }
    }

    public void Poll() => _manager.PollEvents();

    public void Stop() => _manager.Stop();

    public void Dispose() => _manager.Stop();
}

/// <summary>UDP client transport built on LiteNetLib.</summary>
public sealed class LiteNetLibClientTransport : IClientTransport
{
    private const string ConnectionKey = "blocks-beyond-the-stars";

    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private NetPeer? _serverPeer;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<byte[]>? PayloadReceived;

    public LiteNetLibClientTransport()
    {
        // Explicit, matching the server side (#964) — see the note there.
        _manager = new NetManager(_listener) { AutoRecycle = true, DisconnectTimeout = 10000, PingInterval = 1000, ChannelsCount = DeliveryMapping.ChannelCount };

        _listener.PeerConnectedEvent += peer =>
        {
            _serverPeer = peer;
            Connected?.Invoke();
        };

        _listener.PeerDisconnectedEvent += (_, _) =>
        {
            _serverPeer = null;
            Disconnected?.Invoke();
        };

        _listener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            var bytes = reader.GetRemainingBytes();
            PayloadReceived?.Invoke(bytes);
        };
    }

    private UdpBind.ClientSockets? _bound;

    /// <summary>Dials the server. The local sockets follow the target (<see cref="UdpBind.ForTarget"/>, #3707):
    /// a client that only dials out must not look like a listener to Windows Firewall. A repeated call (the
    /// connect retry) keeps the sockets it has.</summary>
    public void Connect(string host, int port)
    {
        var target = UdpBind.Resolve(host);
        if (target == null)
        {
            // Does not resolve: LiteNetLib's own path reports it the way it always did.
            Bind(new UdpBind.ClientSockets(loopback: false, ipv6: true));
            _manager.Connect(host, port, ConnectionKey);
            return;
        }

        Bind(UdpBind.ForTarget(target));
        _manager.Connect(new IPEndPoint(target, port), ConnectionKey);
    }

    private void Bind(UdpBind.ClientSockets sockets)
    {
        if (_manager.IsRunning)
        {
            if (_bound.HasValue && _bound.Value == sockets)
            {
                return;
            }

            _manager.Stop(); // another kind of target than last time: the sockets have to be opened again
        }

        _bound = sockets;
        _manager.IPv6Enabled = sockets.IPv6;
        if (sockets.Loopback)
        {
            _manager.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0);
        }
        else
        {
            _manager.Start();
        }
    }

    public void Send(byte[] payload, DeliveryMode mode) => _serverPeer?.Send(payload, mode.Channel(), mode.ToLiteNetLib());

    public void Poll() => _manager.PollEvents();

    public void Disconnect() => _manager.Stop();

    public void Dispose() => _manager.Stop();
}
