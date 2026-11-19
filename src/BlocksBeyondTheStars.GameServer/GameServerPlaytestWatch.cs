// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Playtests;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The playtest watch (the client's "Test Pilot" page): a client names the game events its playtest task waits for,
/// and the server tells that client when the player causes one. That is all the server does for playtests — the task,
/// its steps and the badge live in the client and in the report inbox, and none of it touches the world: nothing is
/// stored, nothing is paid, nothing is counted here. The golden rule holds the other way round too: what happened is
/// still the server's word (the client cannot make a game event up), only the tick mark is the client's.
/// <para>
/// A watch is one listener on the game-event bus (<see cref="RaiseGameEvent"/>), registered for the session's patterns
/// and removed when the session leaves or sends a new list. It only passes on what its own player did. Events are
/// collected and sent once per tick, added up per key and argument, so a walk measured in metres does not become a
/// message per metre. Registering the listener is also what makes the "measured only while a step listens" events
/// (<c>move:walked</c>, <c>flight:flown</c> …) start measuring.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The most patterns one session may watch.</summary>
    internal const int MaxPlaytestWatchPatterns = 16;

    /// <summary>The shortest gap between two watch lists of one session (the client sends one when a task starts and
    /// when a step closes — never many).</summary>
    private const double PlaytestWatchGapSeconds = 0.25;

    private readonly Dictionary<PlayerSession, PlaytestWatch> _playtestWatches = new();

    /// <summary>One session's watch: the patterns it registered and what it collected since the last tick.</summary>
    private sealed class PlaytestWatch : IGameEventListener
    {
        private readonly PlayerSession _session;

        public PlaytestWatch(PlayerSession session, List<string> patterns, double at)
        {
            _session = session;
            Patterns = patterns;
            At = at;
        }

        public List<string> Patterns { get; }

        /// <summary>The server uptime the list was accepted at (the rate limit).</summary>
        public double At { get; }

        /// <summary>Collected events since the last flush, added up per key and argument.</summary>
        public Dictionary<(string Key, string Arg), int> Pending { get; } = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount)
        {
            if (!ReferenceEquals(actor, _session))
            {
                return; // the bus is shared: only what this player did is theirs to hear
            }

            Pending.TryGetValue((key, arg), out int sum);
            Pending[(key, arg)] = sum + amount;
        }
    }

    /// <summary>A client's watch list: replaces the session's watch (an empty list ends it) and answers with what is
    /// watched now. Unknown, UI and editor patterns are left out — those are the client's own events.
    /// <para>Served while a singleplayer world is held (<c>PausedMayHandle</c>): the Test Pilot page is reached from the
    /// pause menu, and a task started there sends its list at once. The list only registers what the sender wants to
    /// hear about — nothing of the frozen world moves, and the events themselves only happen once the world runs
    /// again.</para></summary>
    private void HandlePlaytestWatch(PlayerSession session, PlaytestWatchIntent message)
    {
        if (!session.Joined)
        {
            return;
        }

        if (_playtestWatches.TryGetValue(session, out var old) && _uptime - old.At < PlaytestWatchGapSeconds && _uptime >= old.At)
        {
            return; // too fast after the last list — the client re-sends when its next step changes
        }

        DropPlaytestWatch(session);
        var accepted = new List<string>();
        foreach (string pattern in message.Patterns ?? new List<string>())
        {
            if (accepted.Count >= MaxPlaytestWatchPatterns)
            {
                break;
            }

            if (IsWatchablePlaytestPattern(pattern) && !accepted.Contains(pattern))
            {
                accepted.Add(pattern);
            }
        }

        if (accepted.Count > 0)
        {
            // Registered without overlaps, so one event reaches the watch once: a key or pattern that another
            // accepted pattern already covers ("craft:any" under "craft:*") needs no registration of its own.
            var registered = accepted.FindAll(p => !accepted.Exists(other => !ReferenceEquals(other, p) && Covers(other, p)));
            var watch = new PlaytestWatch(session, registered, _uptime);
            foreach (string pattern in registered)
            {
                AddGameEventListener(pattern, watch);
            }

            _playtestWatches[session] = watch;
        }

        Send(session, new PlaytestWatchAck { Patterns = accepted });
    }

    /// <summary>Whether the wildcard <paramref name="pattern"/> matches everything <paramref name="other"/> does.</summary>
    private static bool Covers(string pattern, string other)
    {
        if (pattern[^1] != '*' || pattern == other)
        {
            return false;
        }

        string prefix = pattern[..^1];
        string otherStem = other[^1] == '*' ? other[..^1] : other;
        return otherStem.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>A pattern a client may watch: a published server game event (exact, or a prefix ending in <c>*</c>) —
    /// never a UI teaching flag or an editor event, which the server does not raise for playtests.</summary>
    private static bool IsWatchablePlaytestPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern.Length > 64 || pattern == "*"
            || GameEventKeys.IsUiEvent(pattern) || PlaytestEditorEvents.IsEditorEvent(pattern))
        {
            return false;
        }

        int star = pattern.IndexOf('*', StringComparison.Ordinal);
        if (star >= 0 && star != pattern.Length - 1)
        {
            return false;
        }

        foreach (string key in GameEventKeys.All)
        {
            if (GameEventKeys.Matches(pattern, key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ends a session's watch (a new list arrived, or the session left).</summary>
    private void DropPlaytestWatch(PlayerSession session)
    {
        if (!_playtestWatches.Remove(session, out var watch))
        {
            return;
        }

        foreach (string pattern in watch.Patterns)
        {
            RemoveGameEventListener(pattern, watch);
        }
    }

    /// <summary>Post-world tick step: sends what every watch collected during the tick, one message per key and
    /// argument.</summary>
    private void TickPlaytestWatches()
    {
        if (_playtestWatches.Count == 0)
        {
            return;
        }

        foreach (var (session, watch) in _playtestWatches)
        {
            if (watch.Pending.Count == 0 || !session.Joined)
            {
                continue;
            }

            foreach (var ((key, arg), amount) in watch.Pending)
            {
                Send(session, new PlaytestEvent { Key = key, Arg = arg.Length <= 64 ? arg : arg[..64], Amount = amount });
            }

            watch.Pending.Clear();
        }
    }

    /// <summary>Test seam: the patterns a session watches right now (empty = no watch).</summary>
    internal IReadOnlyList<string> PlaytestWatchPatternsForTest(PlayerSession session)
        => _playtestWatches.TryGetValue(session, out var watch) ? watch.Patterns : Array.Empty<string>();

    /// <summary>Test seam: runs the flush as the next tick would.</summary>
    internal void FlushPlaytestWatchesForTest() => TickPlaytestWatches();
}
