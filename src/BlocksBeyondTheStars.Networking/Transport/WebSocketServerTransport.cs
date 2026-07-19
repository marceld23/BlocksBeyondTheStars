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
/// connects over WebSocket. Browser clients use <see cref="NetCodec"/>'s JSON envelope to avoid
/// WebGL/IL2CPP contractless formatter generation, while native clients keep MessagePack. Network events are queued on background threads and surfaced during
/// <see cref="Poll"/>, matching the single-threaded, tick-driven server model.
/// </summary>
public sealed class WebSocketServerTransport : IServerTransport
{
    private const int MaxReceiveFrameBytes = NetCodec.MaxJsonPayloadBytes;

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

    /// <summary>When set, <c>GET /status</c> answers with this JSON (the game server's live snapshot: joined
    /// players, uptime, idle state). This is what a hosted-worlds control plane polls for allocation and
    /// idle decisions — the admin API only sees persisted state, not live sessions. Must be cheap and
    /// thread-safe: it is invoked on the accept loop, not the tick thread.</summary>
    public Func<string>? StatusJsonProvider { get; set; }

    /// <summary>When set together with <see cref="AnnounceToken"/>, <c>POST /announce</c> accepts a
    /// maintenance announcement (JSON <c>{"kind":0|1|2,"text":"...","seconds":600}</c>) from the control
    /// plane and forwards it to the game server (kind, text, seconds → accepted). Invoked on the accept
    /// loop — the game server queues it for the tick thread. Requests must carry the token in the
    /// <c>X-Announce-Token</c> header; without a configured token the route stays a 400 like any other
    /// unknown path, so self-hosted gateways expose nothing new.</summary>
    public Func<byte, string?, int, bool>? AnnounceReceiver { get; set; }

    /// <summary>Shared secret required by <c>POST /announce</c>; empty disables the endpoint.</summary>
    public string AnnounceToken { get; set; } = string.Empty;

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

            // One request must never take down the accept loop (#417): a client resetting the connection
            // mid-response write faults right here, and /status is polled continuously — an unhandled
            // fault would end the while loop and close the gateway to every future browser client.
            try
            {
                if (ctx.Request.IsWebSocketRequest)
                {
                    _ = HandleClientAsync(ctx);
                }
                else
                {
                    await HandleHttpRequestAsync(ctx).ConfigureAwait(false);
                }
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }
    }

    /// <summary>Serves the plain-HTTP routes (landing page/healthz, /status, /announce). Runs on the
    /// accept loop; any exception — aborted connection mid-write, faulting status provider — is
    /// contained by the caller.</summary>
    private async Task HandleHttpRequestAsync(HttpListenerContext ctx)
    {
        if (ctx.Request.HttpMethod == "GET"
            && (ctx.Request.Url?.AbsolutePath is "/" or "/healthz"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes("Blocks Beyond the Stars WebSocket gateway\n");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            ctx.Response.Close();
        }
        else if (ctx.Request.HttpMethod == "GET"
            && ctx.Request.Url?.AbsolutePath == "/status"
            && StatusJsonProvider is { } statusProvider)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(statusProvider());
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            ctx.Response.Close();
        }
        else if (ctx.Request.HttpMethod == "POST"
            && ctx.Request.Url?.AbsolutePath == "/announce"
            && AnnounceReceiver is { } announceReceiver
            && !string.IsNullOrEmpty(AnnounceToken))
        {
            ctx.Response.StatusCode = await HandleAnnounceAsync(ctx, announceReceiver).ConfigureAwait(false);
            ctx.Response.Close();
        }
        else
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
        }
    }

    /// <summary>Authenticates and parses a <c>POST /announce</c> request; returns the HTTP status code.</summary>
    private async Task<int> HandleAnnounceAsync(HttpListenerContext ctx, Func<byte, string?, int, bool> receiver)
    {
        if (!FixedTimeEquals(ctx.Request.Headers["X-Announce-Token"], AnnounceToken))
        {
            return 401;
        }

        const int MaxBodyBytes = 4096;
        if (ctx.Request.ContentLength64 is < 0 or > MaxBodyBytes)
        {
            return 400;
        }

        try
        {
            using var reader = new System.IO.StreamReader(ctx.Request.InputStream, System.Text.Encoding.UTF8);
            char[] buffer = new char[MaxBodyBytes];
            int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(new string(buffer, 0, read));
            var root = doc.RootElement;
            byte kind = root.TryGetProperty("kind", out var k) ? (byte)k.GetInt32() : (byte)0;
            string? text = root.TryGetProperty("text", out var t) ? t.GetString() : null;
            int seconds = root.TryGetProperty("seconds", out var s) ? s.GetInt32() : -1;
            return receiver(kind, text, seconds) ? 200 : 400;
        }
        catch
        {
            return 400; // malformed JSON / aborted body — never let a bad request kill the accept loop
        }
    }

    /// <summary>Constant-time string compare (hand-rolled like HostedJoinToken's — netstandard2.1/Unity
    /// compatibility rules out CryptographicOperations here).</summary>
    private static bool FixedTimeEquals(string? candidate, string expected)
    {
        if (candidate is null || candidate.Length != expected.Length)
        {
            return false;
        }

        int diff = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            diff |= candidate[i] ^ expected[i];
        }

        return diff == 0;
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
            try
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
            }
            catch
            {
                // the connection is already gone — nothing left to answer
            }

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
                    if (ms.Length + result.Count > MaxReceiveFrameBytes)
                    {
                        await client.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Frame too large", CancellationToken.None)
                            .ConfigureAwait(false);
                        LogWarning($"Dropped oversized browser WebSocket frame from connection {id}.");
                        return;
                    }

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
        if (!_clients.TryGetValue(connectionId, out var client))
        {
            return;
        }

        if (!NetCodec.TryConvertToJsonPayload(payload, out var browserPayload))
        {
            LogWarning($"Dropped server payload for browser connection {connectionId}: could not convert NetCodec payload to JSON.");
            return;
        }

        _ = SendAsync(client, browserPayload);
    }

    public void Broadcast(byte[] payload, DeliveryMode mode)
    {
        if (!NetCodec.TryConvertToJsonPayload(payload, out var browserPayload))
        {
            LogWarning("Dropped broadcast payload for browser clients: could not convert NetCodec payload to JSON.");
            return;
        }

        foreach (var client in _clients.Values)
        {
            _ = SendAsync(client, browserPayload);
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

    private static void LogWarning(string message)
        => System.Console.Error.WriteLine("[WARN] " + message);

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
