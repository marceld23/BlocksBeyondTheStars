// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using LiteNetLib;

namespace BlocksBeyondTheStars.Networking.Transport;

/// <summary>Maps our <see cref="DeliveryMode"/> onto LiteNetLib delivery methods.</summary>
internal static class DeliveryMapping
{
    public static DeliveryMethod ToLiteNetLib(this DeliveryMode mode) => mode switch
    {
        DeliveryMode.ReliableOrdered => DeliveryMethod.ReliableOrdered,
        DeliveryMode.Unreliable => DeliveryMethod.Unreliable,
        _ => DeliveryMethod.ReliableOrdered,
    };
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

    public LiteNetLibServerTransport(int maxConnections = 16)
    {
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

    public void Start(int port) => _manager.Start(port);

    public void Send(int connectionId, byte[] payload, DeliveryMode mode)
    {
        if (_peers.TryGetValue(connectionId, out var peer))
        {
            peer.Send(payload, mode.ToLiteNetLib());
        }
    }

    public void Broadcast(byte[] payload, DeliveryMode mode)
        => _manager.SendToAll(payload, mode.ToLiteNetLib());

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
        _manager = new NetManager(_listener) { AutoRecycle = true, DisconnectTimeout = 10000, PingInterval = 1000 };

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

    public void Connect(string host, int port)
    {
        _manager.Start();
        _manager.Connect(host, port, ConnectionKey);
    }

    public void Send(byte[] payload, DeliveryMode mode) => _serverPeer?.Send(payload, mode.ToLiteNetLib());

    public void Poll() => _manager.PollEvents();

    public void Disconnect() => _manager.Stop();

    public void Dispose() => _manager.Stop();
}
