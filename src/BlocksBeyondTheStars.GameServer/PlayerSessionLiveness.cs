// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection liveness and pause bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionLiveness
{
    /// <summary>Uptime of the last payload received from this client — the app-level heartbeat (#964). A client
    /// whose process is frozen or dead keeps its transport peer alive (LiteNetLib pings from its own thread), so
    /// the transport timeout alone can leave a ghost session holding the player's name for many minutes.</summary>
    public double LastPayloadAt { get; set; }

    /// <summary>Whether this session is subject to the heartbeat sweep — true only for sessions that joined
    /// over the wire. A locally-added player (tests, in-process hosts) drives the server through direct calls
    /// and legitimately never sends a payload, so silence says nothing about it being alive.</summary>
    public bool HeartbeatTracked { get; set; }

    // --- The pause menu (#973) ------------------------------------------------------------------------
    // The world holds once EVERY joined player wants it held, so the intent lives on the session rather
    // than on the server: a single global "paused" bool had no owner and could only ever serve one player.

    /// <summary>True while this player sits in their pause menu asking for the world to be held. The world
    /// itself only stops when every joined non-spectator agrees (see <c>GameServer.RecomputePause</c>).</summary>
    public bool WantsPause { get; set; }

    /// <summary>Seconds this session has been silent while the WORLD stood still. A held world does not
    /// advance <see cref="LastPayloadAt"/>'s clock, so the normal heartbeat sweep is blind during a pause —
    /// this is the same liveness signal on a clock that keeps running (see
    /// <c>GameServer.SweepSilentPausedSessions</c>).</summary>
    public double PausedSilentSeconds { get; set; }

    /// <summary>True once this client has re-sent its pause intent while already holding — the keep-alive
    /// that proves it is still alive behind an open menu. It doubles as a capability probe: a client from
    /// before #973 sends nothing at all while paused, and must never be dropped for that.</summary>
    public bool SendsPauseKeepAlive { get; set; }

    /// <summary>True while a hold that ran out its ceiling must not be re-entered from this still-open menu.
    /// Without it the auto-resume would be pointless: the client keeps sending its keep-alive, and the very
    /// next one would put the world straight back to sleep. Cleared when the player closes the menu.</summary>
    public bool PauseHoldExpired { get; set; }
}
