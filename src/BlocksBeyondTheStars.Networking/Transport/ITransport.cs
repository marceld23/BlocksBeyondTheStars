// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Transport;

/// <summary>
/// Server-side transport. Carries raw payloads (already encoded by <see cref="NetCodec"/>)
/// to/from connected clients. Events are raised during <see cref="Poll"/>, so the game
/// server stays single-threaded and tick-driven.
/// </summary>
public interface IServerTransport : IDisposable
{
    event Action<int>? ClientConnected;
    event Action<int>? ClientDisconnected;
    event Action<int, byte[]>? PayloadReceived;

    void Start(int port);
    void Send(int connectionId, byte[] payload, DeliveryMode mode);
    void Broadcast(byte[] payload, DeliveryMode mode);

    /// <summary>Processes pending network events; call once per server tick.</summary>
    void Poll();

    /// <summary>Drops one client (moderation: kick/ban). The client is expected to have been told why
    /// first — this only closes the pipe, so a modified client cannot simply ignore the message and play
    /// on. Default: a no-op, for transports where the concept does not apply (loopback singleplayer) or
    /// test doubles that never own a socket.</summary>
    void DisconnectClient(int connectionId)
    {
    }

    void Stop();
}

/// <summary>Client-side transport mirror of <see cref="IServerTransport"/>.</summary>
public interface IClientTransport : IDisposable
{
    event Action? Connected;
    event Action? Disconnected;
    event Action<byte[]>? PayloadReceived;

    void Connect(string host, int port);
    void Send(byte[] payload, DeliveryMode mode);

    /// <summary>Processes pending network events; call once per client frame.</summary>
    void Poll();

    void Disconnect();
}

/// <summary>#1531: a server transport that can hand decoded message OBJECTS to its peer instead of encoded bytes —
/// the in-process loopback, where sender and receiver share the heap. The server prefers this path whenever the
/// transport offers it; every other transport keeps the byte path. Contract: a message handed over is never
/// mutated by the sender afterwards (built fresh per send, or cached read-only), and the receiver treats it as
/// read-only.</summary>
public interface IObjectServerTransport
{
    void SendMessage(int connectionId, object message, DeliveryMode mode);

    void BroadcastMessage(object message, DeliveryMode mode);
}

/// <summary>#1531: the client side of the object path — decoded messages arrive through
/// <see cref="MessageReceived"/> in the same order as byte payloads through <c>PayloadReceived</c>.</summary>
public interface IObjectClientTransport
{
    event Action<object>? MessageReceived;
}
