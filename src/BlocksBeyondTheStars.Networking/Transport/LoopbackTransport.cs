// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Networking.Transport;

/// <summary>
/// In-memory link shared by a <see cref="LoopbackServerTransport"/> and a
/// <see cref="LoopbackClientTransport"/>. Enables singleplayer (the client hosts the
/// server in-process) and deterministic tests using the exact same server logic as
/// multiplayer — no sockets involved.
/// <para>#1531: server→client traffic passes the message OBJECTS themselves (<see cref="PassObjects"/>)
/// instead of encoding and decoding them — sender and receiver share the process, and the
/// JSON round trip the browser singleplayer had to use (IL2CPP cannot run MessagePack's runtime formatters)
/// cost three payload-sized allocations per message on the render thread. Client→server stays encoded:
/// intents are tiny, and the server must never keep a reference into a client-owned object (it stores
/// design arrays, pixel payloads and the like — the round trip is its deep copy). Every message a server
/// hands over is built fresh for that send or cached read-only (the chunk message cache), and the client
/// treats received messages as read-only — that contract is what makes the object path safe.</para>
/// </summary>
public sealed class LoopbackLink
{
    internal const int ClientConnectionId = 1;

    private readonly object _gate = new();
    private readonly Queue<byte[]> _clientToServer = new();
    private readonly Queue<object> _serverToClient = new();

    /// <summary>Hand server→client messages over as objects. Off by default so every test that reads the wire
    /// as bytes keeps working; the browser singleplayer host and the client harness switch it on.</summary>
    public bool PassObjects { get; set; }

    /// <summary>Diagnostics / tests: how many server→client messages went over as objects, and as bytes.</summary>
    public long ObjectsToClient { get; private set; }

    public long BytesToClient { get; private set; }

    internal bool ConnectRequested;
    internal bool ConnectAcknowledgedByServer;
    internal bool ConnectSignaledToClient;
    internal bool DisconnectRequested;
    internal bool DisconnectSignaledToServer;
    internal bool DisconnectSignaledToClient;

    internal void EnqueueToServer(byte[] payload)
    {
        lock (_gate) { _clientToServer.Enqueue(payload); }
    }

    internal void EnqueueToClient(byte[] payload)
    {
        lock (_gate)
        {
            _serverToClient.Enqueue(payload);
            BytesToClient++;
        }
    }

    internal void EnqueueMessageToClient(object message)
    {
        if (!PassObjects)
        {
            EnqueueToClient(NetCodec.Encode(message));
            return;
        }

        lock (_gate)
        {
            _serverToClient.Enqueue(message);
            ObjectsToClient++;
        }
    }

    internal List<byte[]> DrainToServer()
    {
        lock (_gate)
        {
            var list = new List<byte[]>(_clientToServer);
            _clientToServer.Clear();
            return list;
        }
    }

    internal List<object> DrainToClient()
    {
        lock (_gate)
        {
            var list = new List<object>(_serverToClient);
            _serverToClient.Clear();
            return list;
        }
    }
}

public sealed class LoopbackServerTransport : IServerTransport, IObjectServerTransport
{
    private readonly LoopbackLink _link;

    public event Action<int>? ClientConnected;
    public event Action<int>? ClientDisconnected;
    public event Action<int, byte[]>? PayloadReceived;

    public LoopbackServerTransport(LoopbackLink link) => _link = link;

    public void Start(int port) { /* nothing to bind for loopback */ }

    public void Send(int connectionId, byte[] payload, DeliveryMode mode) => _link.EnqueueToClient(payload);

    public void Broadcast(byte[] payload, DeliveryMode mode) => _link.EnqueueToClient(payload);

    /// <summary>#1531: the object path — no encoding at all when the link passes objects.</summary>
    public void SendMessage(int connectionId, object message, DeliveryMode mode) => _link.EnqueueMessageToClient(message);

    public void BroadcastMessage(object message, DeliveryMode mode) => _link.EnqueueMessageToClient(message);

    /// <summary>Server-side hang-up (kick). The loopback has exactly one client, so the id only has to
    /// match it; the disconnect surfaces on the next <see cref="Poll"/> like a client-side one.</summary>
    public void DisconnectClient(int connectionId)
    {
        if (connectionId == LoopbackLink.ClientConnectionId)
        {
            _link.DisconnectRequested = true;
        }
    }

    public void Poll()
    {
        if (_link.ConnectRequested && !_link.ConnectAcknowledgedByServer)
        {
            _link.ConnectAcknowledgedByServer = true;
            ClientConnected?.Invoke(LoopbackLink.ClientConnectionId);
        }

        foreach (var payload in _link.DrainToServer())
        {
            PayloadReceived?.Invoke(LoopbackLink.ClientConnectionId, payload);
        }

        if (_link.DisconnectRequested && !_link.DisconnectSignaledToServer)
        {
            _link.DisconnectSignaledToServer = true;
            ClientDisconnected?.Invoke(LoopbackLink.ClientConnectionId);
        }
    }

    public void Stop() { }

    public void Dispose() { }
}

public sealed class LoopbackClientTransport : IClientTransport, IObjectClientTransport
{
    private readonly LoopbackLink _link;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<byte[]>? PayloadReceived;
    public event Action<object>? MessageReceived;

    public LoopbackClientTransport(LoopbackLink link) => _link = link;

    public void Connect(string host, int port) => _link.ConnectRequested = true;

    public void Send(byte[] payload, DeliveryMode mode) => _link.EnqueueToServer(payload);

    public void Poll()
    {
        if (_link.ConnectAcknowledgedByServer && !_link.ConnectSignaledToClient)
        {
            _link.ConnectSignaledToClient = true;
            Connected?.Invoke();
        }

        // One queue for both kinds keeps the server's send order — a byte payload never overtakes an object.
        foreach (var item in _link.DrainToClient())
        {
            if (item is byte[] payload)
            {
                PayloadReceived?.Invoke(payload);
            }
            else
            {
                MessageReceived?.Invoke(item);
            }
        }

        // The server can hang up too (a kick, #497) — the client has to notice, or it sits in a world
        // nobody is serving any more.
        if (_link.DisconnectRequested && !_link.DisconnectSignaledToClient)
        {
            _link.DisconnectSignaledToClient = true;
            Disconnected?.Invoke();
        }
    }

    public void Disconnect()
    {
        _link.DisconnectRequested = true;
        _link.DisconnectSignaledToClient = true; // we are the ones hanging up; don't re-raise on the next Poll
        Disconnected?.Invoke();
    }

    public void Dispose() { }
}
