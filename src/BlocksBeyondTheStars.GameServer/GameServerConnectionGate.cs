// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3194: the pre-join deadline. A transport connection that never joins used to keep its slot forever — the WebSocket
/// gateway's handshake window ends with the first message, LiteNetLib keeps any peer with the public key that answers
/// pings, a <see cref="Networking.Messages.JoinRejected"/> left the pipe open, and the heartbeat and the idle shutdown only
/// look at JOINED sessions. Both transports cap their connections (<c>max(64, MaxPlayers*4)</c> WebSocket,
/// <c>max(8, MaxPlayers*2+4)</c> UDP), so a handful of idle connections locked every player out of a world without
/// logging in, password or not.
/// <para>
/// Now every open connection without a joined session is closed once it has waited <see cref="PreJoinDeadlineSeconds"/>
/// — one that never sent anything, one that sent something else, and one whose join was rejected. A real client sends
/// its <c>JoinRequest</c> the moment the transport connects and the server answers it in the next tick (the world loads
/// inside that same join), so it is joined long before the deadline; after a rejection the client goes back to the menu
/// and dials a new connection for the next try, so no legitimate client waits on an old one.
/// </para>
/// <para>
/// The age runs on the tick's own delta — in a held (paused) world too, so a pause shields nothing — and starts at zero
/// on the first tick that sees the connection, so a long tick BEFORE it arrived is never charged to it. The joins a tick
/// received are handled in its network poll, before this step runs, so a slow tick cannot cut off a join that already
/// arrived either.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>
    /// #3194: seconds an open transport connection may go without a joined session before the server closes it. Twice
    /// the WebSocket gateway's 15 s first-message window (<c>WebSocketServerTransport.DefaultHandshakeTimeout</c>) — a
    /// browser has to have sent its join by then anyway — and the upper end of what the issue asked for, so a slow
    /// browser or a congested link has ample margin, while an idle connection frees its slot within half a minute.
    /// </summary>
    internal const double PreJoinDeadlineSeconds = 30.0;

    /// <summary>Seconds each open, not-yet-joined connection has waited, keyed by transport connection id.</summary>
    private readonly Dictionary<int, double> _preJoinSeconds = new();

    /// <summary>The connections this pass found past the deadline — reused, so the step allocates nothing.</summary>
    private readonly List<int> _preJoinExpired = new();

    private bool _connectionGateWired;

    /// <summary>#3194 tick step (post-world and held): ages every open connection without a joined session and closes the
    /// ones past <see cref="PreJoinDeadlineSeconds"/>, through the transport and the server's own disconnect path.</summary>
    private void TickConnectionGate(double deltaSeconds)
    {
        WireConnectionGate();
        _preJoinExpired.Clear();
        foreach (int connectionId in _openConnections)
        {
            if (_sessions.TryGetValue(connectionId, out var session) && session.Joined)
            {
                _preJoinSeconds.Remove(connectionId); // joined — the session heartbeat looks after it from here
                continue;
            }

            if (!_preJoinSeconds.TryGetValue(connectionId, out double waited))
            {
                _preJoinSeconds[connectionId] = 0; // first sight: the time before this tick was not this connection's
                continue;
            }

            waited += deltaSeconds;
            _preJoinSeconds[connectionId] = waited;
            if (waited > PreJoinDeadlineSeconds)
            {
                _preJoinExpired.Add(connectionId);
            }
        }

        foreach (int connectionId in _preJoinExpired)
        {
            _log.Warn($"Connection {connectionId} did not join within {PreJoinDeadlineSeconds:0}s — closing it.");
            _preJoinSeconds.Remove(connectionId);
            _transport.DisconnectClient(connectionId);
            OnClientDisconnected(connectionId); // forgets the connection and its join gate; the transport's echo stays quiet (#2749)
        }
    }

    /// <summary>Hooks the transport's connect and close events once (on the first gate pass — the server subscribes its
    /// own handlers in the constructor, so these run after them). Both simply forget the id: a transport may hand a
    /// closed connection's id to the next one (LiteNetLib reuses peer ids), and a new connection must start at zero
    /// instead of inheriting the old one's wait. Connections that opened before the first pass are found by the sweep.</summary>
    private void WireConnectionGate()
    {
        if (_connectionGateWired)
        {
            return;
        }

        _connectionGateWired = true;
        _transport.ClientConnected += connectionId => _preJoinSeconds.Remove(connectionId);
        _transport.ClientDisconnected += connectionId => _preJoinSeconds.Remove(connectionId);
    }

    /// <summary>#3194 test seam: how many open transport connections have no joined session.</summary>
    internal int PreJoinConnectionCountForTest
    {
        get
        {
            int count = 0;
            foreach (int connectionId in _openConnections)
            {
                if (!_sessions.TryGetValue(connectionId, out var session) || !session.Joined)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
