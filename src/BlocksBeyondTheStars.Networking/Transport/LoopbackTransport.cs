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
/// </summary>
public sealed class LoopbackLink
{
    internal const int ClientConnectionId = 1;

    private readonly object _gate = new();
    private readonly Queue<byte[]> _clientToServer = new();
    private readonly Queue<byte[]> _serverToClient = new();

    internal bool ConnectRequested;
    internal bool ConnectAcknowledgedByServer;
    internal bool ConnectSignaledToClient;
    internal bool DisconnectRequested;
    internal bool DisconnectSignaledToServer;

    internal void EnqueueToServer(byte[] payload)
    {
        lock (_gate) { _clientToServer.Enqueue(payload); }
    }

    internal void EnqueueToClient(byte[] payload)
    {
        lock (_gate) { _serverToClient.Enqueue(payload); }
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

    internal List<byte[]> DrainToClient()
    {
        lock (_gate)
        {
            var list = new List<byte[]>(_serverToClient);
            _serverToClient.Clear();
            return list;
        }
    }
}

public sealed class LoopbackServerTransport : IServerTransport
{
    private readonly LoopbackLink _link;

    public event Action<int>? ClientConnected;
    public event Action<int>? ClientDisconnected;
    public event Action<int, byte[]>? PayloadReceived;

    public LoopbackServerTransport(LoopbackLink link) => _link = link;

    public void Start(int port) { /* nothing to bind for loopback */ }

    public void Send(int connectionId, byte[] payload, DeliveryMode mode) => _link.EnqueueToClient(payload);

    public void Broadcast(byte[] payload, DeliveryMode mode) => _link.EnqueueToClient(payload);

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

public sealed class LoopbackClientTransport : IClientTransport
{
    private readonly LoopbackLink _link;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<byte[]>? PayloadReceived;

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

        foreach (var payload in _link.DrainToClient())
        {
            PayloadReceived?.Invoke(payload);
        }
    }

    public void Disconnect()
    {
        _link.DisconnectRequested = true;
        Disconnected?.Invoke();
    }

    public void Dispose() { }
}
