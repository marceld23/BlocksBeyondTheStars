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
    /// The admin view's Reports tab (#3508), Unity-free: the world's report list as the server last sent it (open ones first,
    /// then the newest done ones). <see cref="MarkLocally"/> flips a row at once when the admin clicks Done, before the server's
    /// new list arrives.
    /// </summary>
    public sealed class AdminReportsModel
    {
        private List<NetAdminReport> _reports = new List<NetAdminReport>();

        /// <summary>The rows, in the server's order.</summary>
        public IReadOnlyList<NetAdminReport> Reports => _reports;

        /// <summary>How many rows are still open.</summary>
        public int OpenCount => _reports.Count(r => !r.Done);

        /// <summary>Counts up with every change.</summary>
        public int Version { get; private set; }

        /// <summary>Takes a new list from the server.</summary>
        public void Apply(AdminReportList? list)
        {
            _reports = (list?.Reports ?? Array.Empty<NetAdminReport>()).ToList();
            Version++;
        }

        /// <summary>The row with this id, or null.</summary>
        public NetAdminReport? Find(long id) => _reports.FirstOrDefault(r => r.Id == id);

        /// <summary>Marks a row done (or open) right away, re-sorting the open ones first; the server's list confirms it.</summary>
        public void MarkLocally(long id, bool done)
        {
            if (Find(id) is not { } row || row.Done == done)
            {
                return;
            }

            row.Done = done;
            _reports = _reports.OrderBy(r => r.Done ? 1 : 0).ThenByDescending(r => r.Id).ToList();
            Version++;
        }

        /// <summary>Forgets the list (a new session).</summary>
        public void Clear()
        {
            _reports = new List<NetAdminReport>();
            Version++;
        }
    }
}
