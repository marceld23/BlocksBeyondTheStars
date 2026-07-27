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

    /// <summary>Default cap on concurrent WebSocket connections (#424 S9). Unlike the native transport
    /// (LiteNetLib rejects past maxConnections), an uncapped WS gateway would hold a socket + buffers +
    /// a receive task for every idler that never joins — MaxPlayers only counts JOINED sessions.</summary>
    public const int DefaultMaxConnections = 64;

    /// <summary>Default window a fresh connection gets to send its first complete message (the join
    /// handshake, #424 S9). A real client sends JoinRequest immediately; a slow-loris connection that
    /// just idles is dropped when the window expires.</summary>
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(15);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001:Types that own disposable fields should be disposable",
        Justification = "Per-connection holder; socket + SendLock are disposed by the receive loop's finally (#426 S16) — the loop outlives every other reference to them, so the holder itself never needs to be IDisposable.")]
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

    /// <summary>When set together with <see cref="AnnounceToken"/>, <c>POST /kick</c> accepts a moderation
    /// kick (JSON <c>{"playerName":"…","reason":"…"}</c>) from the control plane and forwards it to the game
    /// server (name, reason → accepted). Same token, same accept-loop-to-tick handover as the announcement
    /// route: a ban decides the next join, this is what ends the session already in progress.</summary>
    public Func<string, string?, bool>? KickReceiver { get; set; }

    /// <summary>Shared secret required by <c>POST /announce</c> and <c>POST /kick</c>; empty disables both.</summary>
    public string AnnounceToken { get; set; } = string.Empty;

    private readonly int _maxConnections;
    private readonly TimeSpan _handshakeTimeout;

    /// <param name="bindHost">Host for the HTTP prefix; "localhost" for dev/LAN, "+" for all interfaces (may need elevation on Windows).</param>
    /// <param name="maxConnections">Cap on concurrent WebSocket connections; further upgrades are answered 503.</param>
    /// <param name="handshakeTimeout">How long a fresh connection may idle before its first complete message.</param>
    public WebSocketServerTransport(string bindHost = "localhost", int maxConnections = DefaultMaxConnections, TimeSpan? handshakeTimeout = null)
    {
        _bindHost = bindHost;
        _maxConnections = maxConnections;
        _handshakeTimeout = handshakeTimeout ?? DefaultHandshakeTimeout;
    }

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
                // Close the faulted request CLEANLY rather than aborting it. Abort() tears the socket down
                // without a response, and on Linux (managed HttpListener) the connection object is then only
                // reclaimed by the listener's 2-minute idle sweep — Dispose() waits for that sweep, which is
                // why the one test that exercises this path measured 180-212 s against a 120 s budget while
                // every other test in the suite finished in under 11 s (#536). Windows (http.sys) never
                // showed it. Writing a real 500 and closing releases the connection immediately.
                //
                // KeepAlive = false is the load-bearing part: without it the listener holds the connection
                // open for reuse and we are back in the idle sweep.
                //
                // It also behaves better in production: a transient StatusJsonProvider fault used to hand
                // the browser a connection reset, and now hands it an honest 500.
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.KeepAlive = false;
                    ctx.Response.Close();
                }
                catch
                {
                    // Headers already went out (the fault happened mid-body-write), so a clean close is no
                    // longer possible — abort is all that is left.
                    try { ctx.Response.Abort(); } catch { }
                }
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
        else if (ctx.Request.HttpMethod == "POST"
            && ctx.Request.Url?.AbsolutePath == "/kick"
            && KickReceiver is { } kickReceiver
            && !string.IsNullOrEmpty(AnnounceToken))
        {
            ctx.Response.StatusCode = await HandleKickAsync(ctx, kickReceiver).ConfigureAwait(false);
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

    /// <summary>Authenticates and parses a <c>POST /kick</c> request; returns the HTTP status code. 404 when
    /// the named player is not online — the control plane treats that as "nothing to do", not as an error.</summary>
    private async Task<int> HandleKickAsync(HttpListenerContext ctx, Func<string, string?, bool> receiver)
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
            string name = (root.TryGetProperty("playerName", out var n) ? n.GetString() : null) ?? string.Empty;
            string? reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
            return name.Length == 0 ? 400 : receiver(name, reason) ? 200 : 404;
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
        // Connection cap (#424 S9): reject BEFORE the upgrade so an idler flood can't pile up sockets,
        // receive tasks and buffers. Racing handshakes may overshoot by a few — the bound still holds.
        if (_clients.Count >= _maxConnections)
        {
            LogWarning($"Rejected browser WebSocket connection: {_clients.Count}/{_maxConnections} connections in use.");
            try
            {
                ctx.Response.StatusCode = 503;
                ctx.Response.Close();
            }
            catch
            {
                // the connection is already gone — nothing left to answer
            }

            return;
        }

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

        // Handshake window (#424 S9): until the FIRST complete message arrives (a real client's
        // JoinRequest, sent immediately), receives run under a deadline — a connection that only idles
        // (slow-loris) is dropped instead of holding its socket + receive task forever. After the first
        // message the connection is the session layer's to manage and receives wait indefinitely again.
        var handshakeClock = System.Diagnostics.Stopwatch.StartNew(); // netstandard2.1: no Environment.TickCount64
        bool firstMessageReceived = false;
        try
        {
            while (client.Socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    if (!firstMessageReceived)
                    {
                        long remainingMs = (long)(_handshakeTimeout - handshakeClock.Elapsed).TotalMilliseconds;
                        if (remainingMs <= 0)
                        {
                            LogWarning($"Dropped browser WebSocket connection {id}: no message within the handshake window.");
                            await client.Socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Handshake timeout", CancellationToken.None)
                                .ConfigureAwait(false);
                            return;
                        }

                        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                        handshakeCts.CancelAfter((int)System.Math.Min(remainingMs, int.MaxValue));
                        try
                        {
                            result = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), handshakeCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                        {
                            LogWarning($"Dropped browser WebSocket connection {id}: no message within the handshake window.");
                            client.Socket.Abort(); // the pending receive was cancelled — a graceful close can't complete
                            return;
                        }
                    }
                    else
                    {
                        result = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                    }

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
                firstMessageReceived = true;
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

            // Tear the per-connection resources down for real (#426 S16): before this, every connection
            // ever accepted leaked its WebSocket (and semaphore) until process exit. Removal from
            // _clients above stops NEW sends from picking the client up; an in-flight SendAsync that
            // already holds a reference tolerates both disposals (its catch swallows the socket, its
            // Release is ObjectDisposedException-guarded).
            try { client.Socket.Dispose(); } catch { }
            try { client.SendLock.Dispose(); } catch { }
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

    /// <summary>Closes a browser connection (kick). Aborting rather than a graceful close is deliberate:
    /// the receive loop is parked in ReceiveAsync, so Abort is what makes it fall through to its finally —
    /// which is also where the Disconnect event and the resource teardown live.</summary>
    public void DisconnectClient(int connectionId)
    {
        if (_clients.TryGetValue(connectionId, out var client))
        {
            try { client.Socket.Abort(); } catch { }
        }
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
        try
        {
            await client.SendLock.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return; // connection torn down between lookup and send (#426 S16) — nothing to deliver to
        }

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
            try { client.SendLock.Release(); } catch (ObjectDisposedException) { }
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

        // Abort(), not Stop()+Close() (#536): on Linux the managed HttpListener's graceful teardown waits
        // for every connection it still tracks, and a connection with no in-flight request is only
        // reclaimed by the listener's ~2-minute idle sweep. Measured: whichever transport test tore down
        // last paid ~120 s alone on an idle machine (5 m 51 s → 3 m 41 s suite total with Abort), and in
        // production an idle browser connection parked on the gateway would delay a stopping hosted
        // world's process exit the same way. Stop() only ever runs when the transport is going away — the
        // gameplay drain + save happened upstream — so a hard close is the correct semantic: WebSocket
        // peers are torn down via _cts anyway, and an unserved idle socket on a dying server gets a reset
        // instead of a silently dead keep-alive.
        try { _listener.Abort(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        // No-op after the Abort() above (the listener is already torn down) — kept because CA2213 wants a
        // Close/Dispose call on the field, and a disposed listener's Close returns immediately.
        try { _listener.Close(); } catch { }
        _cts.Dispose();
    }
}
