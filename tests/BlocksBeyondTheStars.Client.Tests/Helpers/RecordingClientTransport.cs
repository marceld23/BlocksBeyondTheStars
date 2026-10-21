// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The shared in-memory client transport: records every message the client sends (decoded) and how often it hung up, and
/// feeds it server messages through <see cref="Deliver"/>. Use it instead of a private copy in the test class
/// (<c>SharedTestHelperGuardTests</c> in the server suite scans this project too).
/// </summary>
public sealed class RecordingClientTransport : IClientTransport
{
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<byte[]>? PayloadReceived;

    public List<object?> Sent { get; } = new();

    public int Disconnects { get; private set; }

    public void Connect(string host, int port) => Connected?.Invoke();

    public void Send(byte[] payload, DeliveryMode mode) => Sent.Add(NetCodec.Decode(payload));

    public void Poll()
    {
    }

    public void Disconnect()
    {
        Disconnects++;
        Disconnected?.Invoke();
    }

    public void Dispose()
    {
    }

    public void Deliver(object message) => PayloadReceived?.Invoke(NetCodec.Encode(message));
}
