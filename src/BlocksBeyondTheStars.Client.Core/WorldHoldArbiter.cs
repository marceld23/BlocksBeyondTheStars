// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Combines every reason this client has to hold the world into the ONE pause intent the server knows (#2461).
    /// <para>
    /// The server keeps a single pause flag per session (<c>PlayerSession.WantsPause</c>), and each panel used to drive
    /// it through its own <see cref="WorldHoldIntent"/>: the Esc menu, the F1/F2 feedback dialog and the feedback reply
    /// all sent "hold" on open and "release" on close. Two of them open at once overwrote each other — closing the report
    /// dialog over the pause menu released the menu's hold, and the world ran on behind a pause menu. A later cutscene
    /// hold would have been released by any menu closing the same way.
    /// </para>
    /// <para>
    /// Now every holder registers by name; the intent goes out on the first holder, the release only when the LAST one
    /// lets go, and the keep-alive (<see cref="WorldHoldIntent.KeepAliveSeconds"/>) runs once for all of them. The wire
    /// and the server are unchanged. Unity-free so the rule is covered by plain .NET tests; the client keeps one live
    /// instance per world (<c>GameBootstrap.WorldHold</c>), fed with <c>Time.realtimeSinceStartup</c>.
    /// </para>
    /// </summary>
    public sealed class WorldHoldArbiter
    {
        /// <summary>The in-game pause menu (Esc).</summary>
        public const string Menu = "menu";

        /// <summary>The F1/F2 feedback dialog.</summary>
        public const string Feedback = "feedback";

        /// <summary>The reply to a feedback report, read and answered in game.</summary>
        public const string FeedbackReply = "feedback_reply";

        /// <summary>A staged cutscene holding a lone player's world (the cutscene director).</summary>
        public const string Cutscene = "cutscene";

        private readonly WorldHoldIntent _intent;
        private readonly HashSet<string> _holders = new HashSet<string>(StringComparer.Ordinal);

        /// <param name="send">Delivers a pause intent: <c>true</c> = hold, <c>false</c> = release.</param>
        public WorldHoldArbiter(Action<bool> send)
        {
            _intent = new WorldHoldIntent(send);
        }

        /// <summary>True while at least one holder asks for the hold (and the server has been told).</summary>
        public bool Holding => _intent.Holding;

        /// <summary>How many holders are registered right now.</summary>
        public int HolderCount => _holders.Count;

        /// <summary>Whether <paramref name="holder"/> currently holds the world.</summary>
        public bool IsHeldBy(string holder) => holder != null && _holders.Contains(holder);

        /// <summary>Registers <paramref name="holder"/>. The first holder sends the hold intent; a later one (or the same
        /// one again) sends nothing — the keep-alive cadence keeps its own schedule.</summary>
        public void Hold(string holder, float now)
        {
            if (string.IsNullOrEmpty(holder))
            {
                throw new ArgumentException("A world hold needs a holder name.", nameof(holder));
            }

            _holders.Add(holder);
            _intent.Hold(now);
        }

        /// <summary>Unregisters <paramref name="holder"/>. The release goes out only when no holder is left; releasing a
        /// holder that holds nothing is a no-op, so a panel can call it from every close path.</summary>
        public void Release(string holder)
        {
            if (holder == null || !_holders.Remove(holder))
            {
                return;
            }

            if (_holders.Count == 0)
            {
                _intent.Release();
            }
        }

        /// <summary>Call once per frame: re-sends the held intent on the keep-alive cadence while anyone holds.</summary>
        public void Tick(float now) => _intent.Tick(now);

        /// <summary>Drops every holder WITHOUT telling the server — the world they held is already gone.</summary>
        public void Forget()
        {
            _holders.Clear();
            _intent.Forget();
        }
    }
}
