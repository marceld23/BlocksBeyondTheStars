// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One server the player joined by address (#3665) — the Multiplayer screen offers it again with one click. A
    /// flat class so JsonUtility can persist it.</summary>
    [Serializable]
    public sealed class RecentServer
    {
        public string Host = "";
        public string Port = "";

        /// <summary>When it was last joined (Unix seconds) — the list's order, newest first.</summary>
        public long LastJoinedUnix;
    }

    /// <summary>
    /// Settings of the UI layout overhaul (#3662–#3673). Old settings files have none of these fields, so JsonUtility keeps
    /// the defaults below: the short controls line and no remembered servers.
    /// </summary>
    public sealed partial class ClientSettings
    {
        /// <summary>The controls line at the bottom right (#3668): the short set fits the situation and shows at most
        /// four keys; the full line is the old one-row list of every key; off hides it (F1 still shows all keys).</summary>
        public const int ControlsHintShort = 0, ControlsHintFull = 1, ControlsHintOff = 2;

        /// <summary>How many joined servers the Multiplayer screen remembers.</summary>
        public const int MaxRecentServers = 5;

        /// <summary>The controls line's mode: <see cref="ControlsHintShort"/>, <see cref="ControlsHintFull"/> or
        /// <see cref="ControlsHintOff"/>.</summary>
        public int ControlsHint = ControlsHintShort;

        /// <summary>Servers joined by address, newest first, at most <see cref="MaxRecentServers"/> (#3665).</summary>
        public List<RecentServer> RecentServers = new List<RecentServer>();

        /// <summary>Remembers a server the player just joined by address: moves it to the front (or adds it) and drops the
        /// oldest beyond <see cref="MaxRecentServers"/>. Host names compare case-blind.</summary>
        public void RememberServer(string host, string port)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }

            host = host.Trim();
            port = (port ?? "").Trim();
            RecentServers ??= new List<RecentServer>();
            RecentServers.RemoveAll(s => s == null
                || (string.Equals(s.Host, host, StringComparison.OrdinalIgnoreCase) && s.Port == port));
            RecentServers.Insert(0, new RecentServer { Host = host, Port = port, LastJoinedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            if (RecentServers.Count > MaxRecentServers)
            {
                RecentServers.RemoveRange(MaxRecentServers, RecentServers.Count - MaxRecentServers);
            }
        }

        /// <summary>The controls-line mode clamped to the three known values — a hand-edited file stays in range.</summary>
        public int ControlsHintMode => ControlsHint < ControlsHintShort || ControlsHint > ControlsHintOff ? ControlsHintShort : ControlsHint;
    }
}
