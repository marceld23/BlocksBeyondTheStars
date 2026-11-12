// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Admin
{
    /// <summary>
    /// The admin view's Players tab (#3505), Unity-free: the latest <see cref="AdminRoster"/> the server sent (once a second
    /// while the panel is open), grouped by body for the list. The panel redraws when <see cref="Version"/> changed.
    /// </summary>
    public sealed class AdminRosterModel
    {
        private NetAdminPlayer[] _players = Array.Empty<NetAdminPlayer>();

        /// <summary>Every player of the last roster, in the server's order (by body, then name).</summary>
        public IReadOnlyList<NetAdminPlayer> Players => _players;

        /// <summary>Counts up with every roster applied.</summary>
        public int Version { get; private set; }

        /// <summary>Takes a new roster from the server.</summary>
        public void Apply(AdminRoster? roster)
        {
            _players = roster?.Players ?? Array.Empty<NetAdminPlayer>();
            Version++;
        }

        /// <summary>Forgets the roster (a new session, admin mode left).</summary>
        public void Clear()
        {
            _players = Array.Empty<NetAdminPlayer>();
            Version++;
        }

        /// <summary>The player with this id, or null when they are not in the last roster (offline, or on no list for us).</summary>
        public NetAdminPlayer? Find(string? playerId)
            => string.IsNullOrEmpty(playerId) ? null : _players.FirstOrDefault(p => p.PlayerId == playerId);

        /// <summary>The players grouped by body, the bodies sorted by name, each group by player name.</summary>
        public IReadOnlyList<AdminRosterGroup> ByBody()
            => _players
                .GroupBy(p => p.LocationId ?? string.Empty, StringComparer.Ordinal)
                .Select(g => new AdminRosterGroup(
                    g.Key,
                    g.Select(p => p.LocationName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? g.Key,
                    g.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()))
                .OrderBy(g => g.LocationName, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    /// <summary>The players on one body in <see cref="AdminRosterModel.ByBody"/>.</summary>
    public sealed class AdminRosterGroup
    {
        public AdminRosterGroup(string locationId, string locationName, IReadOnlyList<NetAdminPlayer> players)
        {
            LocationId = locationId;
            LocationName = locationName;
            Players = players;
        }

        public string LocationId { get; }

        public string LocationName { get; }

        public IReadOnlyList<NetAdminPlayer> Players { get; }
    }
}
