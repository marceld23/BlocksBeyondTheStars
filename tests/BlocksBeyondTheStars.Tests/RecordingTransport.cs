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
/// went to — <see cref="int.MinValue"/> for a broadcast. Use it instead of a private copy in the test class; a test that
/// needs more (raising connects or payloads, a hook per send) extends this one rather than forking it
/// (<c>RecordingTransportGuardTests</c> keeps new nested copies out).
/// </summary>
public sealed class RecordingTransport : IServerTransport
{
    public event Action<int>? ClientConnected;
    public event Action<int>? ClientDisconnected;
    public event Action<int, byte[]>? PayloadReceived;

    public readonly List<(int Conn, object Msg)> Sent = new();

    public void Start(int port) { }

    public void Send(int connectionId, byte[] payload, DeliveryMode mode)
    {
        if (NetCodec.Decode(payload) is { } m)
        {
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

    // Reading the events keeps CS0067 ("never used") quiet under -warnaserror; the server subscribes to them.
    public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }

    public void Stop() { }

    public void Dispose() { }
}
