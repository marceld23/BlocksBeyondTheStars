// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace BlocksBeyondTheStars.Networking.Transport;

/// <summary>
/// WebSocket server transport for browser clients (technical requirements /
/// `anf_webclient.md` §8): browsers cannot open native UDP sockets, so the web client
/// connects over WebSocket and exchanges the exact same <see cref="NetCodec"/> payloads as
/// native clients. Network events are queued on background threads and surfaced during
/// <see cref="Poll"/>, matching the single-threaded, tick-driven server model.
/// </summary>
public sealed class WebSocketServerTransport : IServerTransport
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001:Types that own disposable fields should be disposable",
        Justification = "Per-connection holder; the socket is torn down when the receive loop ends or the listener stops, and SendLock is used only for WaitAsync/Release (no WaitHandle is ever allocated), so there is nothing requiring deterministic disposal.")]
    private sealed class Client
    {
        public WebSocket Socket = null!;
        public readonly SemaphoreSlim SendLock = new(1, 1);
    }

    private enum EventKind { Connect, Disconnect, Payload }

    private readonly string _bindHost;
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<int, Client> _clients = new();
    private readonly ConcurrentQueue<(EventKind kind, int id, byte[] payload)> _events = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextId;
    private volatile bool _running;

    public event Action<int>? ClientConnected;
    public event Action<int>? ClientDisconnected;
    public event Action<int, byte[]>? PayloadReceived;

    /// <param name="bindHost">Host for the HTTP prefix; "localhost" for dev/LAN, "+" for all interfaces (may need elevation on Windows).</param>
    public WebSocketServerTransport(string bindHost = "localhost") => _bindHost = bindHost;

    public void Start(int port)
    {
        _listener.Prefixes.Add($"http://{_bindHost}:{port}/");
        _listener.Start();
        _running = true;
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (_running)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                break; // listener stopped
            }

            if (!ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
                continue;
            }

            _ = HandleClientAsync(ctx);
        }
    }

    private async Task HandleClientAsync(HttpListenerContext ctx)
    {
        WebSocketContext wsCtx;
        try
        {
            wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        }
        catch
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
            return;
        }

        int id = Interlocked.Increment(ref _nextId);
        var client = new Client { Socket = wsCtx.WebSocket };
        _clients[id] = client;
        _events.Enqueue((EventKind.Connect, id, Array.Empty<byte>()));

        var buffer = new byte[8192];
        using var ms = new System.IO.MemoryStream();
        try
        {
            while (client.Socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

#pragma warning disable VSTHRD103 // MemoryStream.Write is an in-memory copy with nothing to await.
                    ms.Write(buffer, 0, result.Count);
#pragma warning restore VSTHRD103
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                _events.Enqueue((EventKind.Payload, id, ms.ToArray()));
            }
        }
        catch
        {
            // connection error -> treated as disconnect below
        }
        finally
        {
            _clients.TryRemove(id, out _);
            _events.Enqueue((EventKind.Disconnect, id, Array.Empty<byte>()));
        }
    }

    public void Send(int connectionId, byte[] payload, DeliveryMode mode)
    {
        if (_clients.TryGetValue(connectionId, out var client))
        {
            _ = SendAsync(client, payload);
        }
    }

    public void Broadcast(byte[] payload, DeliveryMode mode)
    {
        foreach (var client in _clients.Values)
        {
            _ = SendAsync(client, payload);
        }
    }

    private static async Task SendAsync(Client client, byte[] payload)
    {
        await client.SendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (client.Socket.State == WebSocketState.Open)
            {
                await client.Socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Binary, true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // drop on error; the receive loop will surface the disconnect
        }
        finally
        {
            client.SendLock.Release();
        }
    }

    public void Poll()
    {
        while (_events.TryDequeue(out var e))
        {
            switch (e.kind)
            {
                case EventKind.Connect: ClientConnected?.Invoke(e.id); break;
                case EventKind.Disconnect: ClientDisconnected?.Invoke(e.id); break;
                case EventKind.Payload: PayloadReceived?.Invoke(e.id, e.payload); break;
            }
        }
    }

    public void Stop()
    {
        _running = false;
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        try { _listener.Close(); } catch { }
        _cts.Dispose();
    }
}
