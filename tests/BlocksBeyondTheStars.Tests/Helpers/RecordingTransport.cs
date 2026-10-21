// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The shared server-side transport fake (#2792): records every message the server sends, decoded, with the connection it
/// went to — <see cref="int.MinValue"/> for a broadcast — and every connection the server hung up on. It lives in
/// <c>Helpers/</c> with the other shared fakes; its queries (who was told what, which lines, which scenes …) are extension
/// methods in <see cref="RecordingTransportExtensions"/>. Use it instead of a private copy in the test class; a test that
/// needs more adds the member here (or a one-off query as a file-scoped extension) rather than forking it
/// (<c>SharedTestHelperGuardTests</c> keeps nested copies out).
/// </summary>
public sealed class RecordingTransport : IServerTransport
{
    public event Action<int>? ClientConnected;
    public event Action<int>? ClientDisconnected;
    public event Action<int, byte[]>? PayloadReceived;

    public readonly List<(int Conn, object Msg)> Sent = new();

    /// <summary>The connections the server hung up on (<see cref="DisconnectClient"/>), in order.</summary>
    public readonly List<int> Disconnected = new();

    /// <summary>Called on every per-connection send, before it is recorded. The server sends from inside its tick, so this
    /// is where a test reads what the server's state is in the middle of one.</summary>
    public Action<int, object>? OnSend { get; set; }

    public void Start(int port) { }

    public void Send(int connectionId, byte[] payload, DeliveryMode mode)
    {
        if (NetCodec.Decode(payload) is { } m)
        {
            OnSend?.Invoke(connectionId, m);
            Sent.Add((connectionId, m));
        }
    }

    public void Broadcast(byte[] payload, DeliveryMode mode)
    {
        if (NetCodec.Decode(payload) is { } m)
        {
            Sent.Add((int.MinValue, m));
        }
    }

    public void DisconnectClient(int connectionId) => Disconnected.Add(connectionId);

    // Reading the events keeps CS0067 ("never used") quiet under -warnaserror; the server subscribes to them.
    public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }

    public void Stop() { }

    public void Dispose() { }
}
