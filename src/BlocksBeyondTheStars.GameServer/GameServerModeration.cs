// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Kicking a player out of a running world (#497). A ban — fleet-wide or the world owner's own list — is
/// enforced by the control plane at the NEXT join grant; without this the offender simply keeps playing
/// until they disconnect on their own, which is exactly the moment when moderation matters least.
///
/// Two intake paths, same queue: the instance gateway's token-gated <c>POST /kick</c> (accept-loop thread)
/// and the in-game <c>/kick</c> command of a world admin (tick thread) — so intake is lock-guarded and the
/// tick applies it, mirroring the maintenance-announcement plumbing.
///
/// The player is told first (<see cref="JoinRejected"/>, which the client already renders as "back to the
/// menu with this reason") and the socket is closed a moment later: the notice needs to leave the transport
/// first, and a modified client must not be able to ignore it and play on.
/// </summary>
public sealed partial class GameServer
{
    private const int MaxKickReasonLength = 200;

    /// <summary>Grace between the rejection message and closing the pipe, so the packet still goes out.</summary>
    private const double KickFlushSeconds = 1.0;

    private readonly Lock _kickGate = new();
    private readonly List<(string PlayerName, string Reason)> _kickQueue = new();
    private readonly List<(int ConnectionId, double Remaining)> _kickFlush = new();

    /// <summary>
    /// Queues a kick for the next tick. Safe to call from any thread (the HTTP gateway's accept loop uses
    /// it). Returns false only when the request is unusable — whether anyone of that name is actually
    /// online is deliberately NOT answered here: the session table belongs to the tick thread, and reading
    /// it from the accept loop to produce a nicer status code would be a data race for no real gain.
    /// </summary>
    public bool EnqueueKick(string? playerName, string? reason)
    {
        string name = StripControlChars(playerName).Trim();
        if (name.Length is < 1 or > 24)
        {
            return false;
        }

        string clean = StripControlChars(reason).Trim();
        if (clean.Length > MaxKickReasonLength)
        {
            clean = clean[..MaxKickReasonLength];
        }

        lock (_kickGate)
        {
            _kickQueue.Add((name, clean));
        }

        return true;
    }

    /// <summary>Per-tick moderation bookkeeping: applies queued kicks, then closes the pipes whose grace
    /// period has run out.</summary>
    private void TickModeration(double deltaSeconds)
    {
        List<(string PlayerName, string Reason)>? pending = null;
        lock (_kickGate)
        {
            if (_kickQueue.Count > 0)
            {
                pending = new List<(string, string)>(_kickQueue);
                _kickQueue.Clear();
            }
        }

        if (pending is { })
        {
            foreach (var (playerName, reason) in pending)
            {
                ApplyKick(playerName, reason);
            }
        }

        for (int i = _kickFlush.Count - 1; i >= 0; i--)
        {
            var entry = _kickFlush[i];
            entry.Remaining -= deltaSeconds;
            if (entry.Remaining <= 0)
            {
                _transport.DisconnectClient(entry.ConnectionId);
                _kickFlush.RemoveAt(i);
            }
            else
            {
                _kickFlush[i] = entry;
            }
        }
    }

    /// <summary>Sends the rejection to every session playing under this name and arms the close.</summary>
    private void ApplyKick(string playerName, string reason)
    {
        // A fleet admin is never a kick target: a world owner must not be able to remove oversight of their
        // own world (#495), and the operator's own lever is stopping the instance, not kicking themselves.
        var targets = _sessions.Values
            .Where(s => s.Joined && !s.IsFleetAdmin
                        && string.Equals(s.State.Name, playerName, System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (targets.Count == 0)
        {
            _log.Info($"Kick requested for '{playerName}' — not online, nothing to do.");
            return;
        }

        foreach (var session in targets)
        {
            _log.Info($"Kicking '{session.State.Name}'{(reason.Length > 0 ? $" ({reason})" : string.Empty)}.");
            SendTo(session.ConnectionId, new JoinRejected { Reason = reason });
            _kickFlush.Add((session.ConnectionId, KickFlushSeconds));
        }
    }
}
