// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Playtests
{
    /// <summary>
    /// Keeps the server's playtest watch in step with what the task being worked on waits for: it sends the list when
    /// it changed (never faster than <see cref="MinGapSeconds"/>, so two steps closing in one moment become one
    /// message), and it finds out whether the server reports playtest events at all — a server that knows the message
    /// answers with an ack, an older one stays silent. Until the first ack arrives the answer is "not known yet"; after
    /// <see cref="AckTimeoutSeconds"/> of silence it is "no", and the page lets the player tick those steps off by hand.
    /// Pure logic: the caller passes the time and the send function, so it runs headless.
    /// </summary>
    public sealed class PlaytestWatchSync
    {
        /// <summary>The shortest time between two lists (the server's own limit is half of it).</summary>
        public const double MinGapSeconds = 0.5;

        /// <summary>How long a list waits for its ack before the server counts as one that does not report events.</summary>
        public const double AckTimeoutSeconds = 3.0;

        private readonly Action<IReadOnlyList<string>> _send;
        private List<string>? _sent;
        private double _sentAt = double.NegativeInfinity;
        private double _waitingSince = double.NegativeInfinity;
        private bool _waiting;
        private HashSet<string>? _accepted; // what the server took of the list last sent; null until its ack is here

        /// <param name="send">Sends a watch list to the server (<c>NetworkClient.SendPlaytestWatch</c>).</param>
        public PlaytestWatchSync(Action<IReadOnlyList<string>> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        /// <summary>Whether this server reports playtest events: null while not known, true after an ack, false after a
        /// list stayed unanswered.</summary>
        public bool? Supported { get; private set; }

        /// <summary>
        /// Call regularly (every frame is fine) with the patterns the task waits for now and the current time in
        /// seconds. Sends when the list differs from the one the server has and the gap has passed. An empty list is
        /// sent only to end a watch that exists.
        /// </summary>
        public void Update(IReadOnlyList<string> wanted, double nowSeconds)
        {
            if (_waiting && nowSeconds - _waitingSince >= AckTimeoutSeconds)
            {
                _waiting = false;
                Supported ??= false; // silence: an older server — but a server that answered once stays "yes"
            }

            if (Supported == false)
            {
                return; // nothing to ask an older server
            }

            bool nothingToEnd = wanted.Count == 0 && (_sent == null || _sent.Count == 0);
            if (nothingToEnd || Same(wanted, _sent) || nowSeconds - _sentAt < MinGapSeconds)
            {
                return;
            }

            _sent = new List<string>(wanted);
            _accepted = null;
            _sentAt = nowSeconds;
            if (!_waiting)
            {
                _waiting = true;
                _waitingSince = nowSeconds;
            }

            _send(_sent);
        }

        /// <summary>The server answered a list: it reports playtest events — those of <paramref name="accepted"/>, the
        /// patterns it took. A server leaves out a pattern it does not know (an event newer than that server), and it
        /// will never report that one (#3833).</summary>
        public void OnAck(IReadOnlyList<string>? accepted = null)
        {
            _waiting = false;
            Supported = true;
            _accepted = accepted == null ? null : new HashSet<string>(accepted, StringComparer.Ordinal);
        }

        /// <summary>Whether the server's answer to the list last sent left one of its patterns out.</summary>
        public bool LeftSomethingOut
        {
            get
            {
                if (_accepted == null || _sent == null)
                {
                    return false;
                }

                foreach (string pattern in _sent)
                {
                    if (!_accepted.Contains(pattern))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Whether this server reports <paramref name="pattern"/>: false for a server that reports nothing, or one that
        /// left the pattern out of its answer — the page then lets the player tick that step off by hand. Null while it
        /// is not known: no answer yet, or the pattern was not in the list last sent (a step that is closed already).
        /// </summary>
        public bool? Reports(string pattern)
        {
            if (Supported != true)
            {
                return Supported;
            }

            if (_accepted == null || _sent == null || !_sent.Contains(pattern))
            {
                return null;
            }

            return _accepted.Contains(pattern);
        }

        /// <summary>A new connection (another world, a rejoin): nothing is watched there yet and nothing is known about
        /// that server.</summary>
        public void Reset()
        {
            _sent = null;
            _accepted = null;
            _sentAt = double.NegativeInfinity;
            _waiting = false;
            Supported = null;
        }

        private static bool Same(IReadOnlyList<string> a, List<string>? b)
        {
            if (b == null || a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
