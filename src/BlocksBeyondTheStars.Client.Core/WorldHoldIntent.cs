// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One panel's "hold the world while I'm open" request, with the keep-alive cadence the server relies on.
    ///
    /// <para>
    /// The Esc menu has asked the server to hold the world since #612; the feedback dialog (F1/F2) does the same
    /// since #1330. Both go through this so there is exactly one copy of the rule: send the intent on open, send the
    /// release on close, and REPEAT the held intent every <see cref="KeepAliveSeconds"/> while open. The repeat is
    /// what lets the server drop a client that crashed behind its menu instead of leaving the world frozen for
    /// everyone else (#973) — and it doubles as the capability probe that tells the server this client sends
    /// keep-alives at all.
    /// </para>
    ///
    /// Pure (no UnityEngine) so it lives in Client.Core and is unit-tested headless; the Unity layer feeds it
    /// <c>Time.realtimeSinceStartup</c> and a sender that wraps <c>NetworkClient.SendPause</c>.
    /// </summary>
    public sealed class WorldHoldIntent
    {
        /// <summary>How often the held intent is re-sent while the panel stays open. Well inside the server's 90 s
        /// silent-session budget, so a few lost packets never read as a dead client.</summary>
        public const float KeepAliveSeconds = 15f;

        private readonly Action<bool> _send;
        private float _nextKeepAlive;

        /// <param name="send">Delivers a pause intent: <c>true</c> = hold, <c>false</c> = release.</param>
        public WorldHoldIntent(Action<bool> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        /// <summary>True between <see cref="Hold"/> and <see cref="Release"/>.</summary>
        public bool Holding { get; private set; }

        /// <summary>Asks the server to hold the world and starts the keep-alive cadence. Idempotent: a second call
        /// while already holding sends nothing (the cadence keeps its own schedule).</summary>
        public void Hold(float now)
        {
            if (Holding)
            {
                return;
            }

            Holding = true;
            _nextKeepAlive = now + KeepAliveSeconds;
            _send(true);
        }

        /// <summary>Lets the world run again. Sends nothing when no hold is open, so a panel can call it from every
        /// close path without checking first.</summary>
        public void Release()
        {
            if (!Holding)
            {
                return;
            }

            Holding = false;
            _nextKeepAlive = 0f;
            _send(false);
        }

        /// <summary>Drops the request WITHOUT telling the server — for a panel whose world is already gone (the
        /// player left to the main menu). A later <see cref="Hold"/> starts fresh instead of a stale keep-alive
        /// putting the NEXT world to sleep.</summary>
        public void Forget()
        {
            Holding = false;
            _nextKeepAlive = 0f;
        }

        /// <summary>Call once per frame: re-sends the held intent whenever the cadence is due. No-op while not holding.</summary>
        public void Tick(float now)
        {
            if (!Holding || now < _nextKeepAlive)
            {
                return;
            }

            _nextKeepAlive = now + KeepAliveSeconds;
            _send(true);
        }
    }
}
