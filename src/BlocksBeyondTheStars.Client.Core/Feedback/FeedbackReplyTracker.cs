// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>
    /// Decides which polled reply threads are NEW to the player this session (#1351). Keyed by developer
    /// reply id, not by report id: the first version remembered whole reports, so once the player had
    /// acknowledged answer A, a later answer B (or a follow-up question) on the same report stayed hidden
    /// until the world rig restarted. A thread is offered again exactly when it carries an unseen developer
    /// entry this session has not queued or shown yet; a poll that repeats already-shown ids (the ack has
    /// not landed yet, or the request failed) does not re-open the window.
    /// </summary>
    public sealed class FeedbackReplyTracker
    {
        private readonly HashSet<long> _shownReplyIds = new HashSet<long>();

        /// <summary>Number of developer entries queued or shown so far this session.</summary>
        public int ShownCount => _shownReplyIds.Count;

        /// <summary>True when <paramref name="thread"/> has at least one unseen developer entry that was not
        /// offered before; those ids are then remembered so the same poll result cannot queue the thread twice.</summary>
        public bool Offer(FeedbackReplyThread? thread)
        {
            if (thread == null)
            {
                return false;
            }

            bool fresh = false;
            foreach (long id in thread.UnseenIds)
            {
                fresh |= _shownReplyIds.Add(id);
            }

            return fresh;
        }
    }
}
