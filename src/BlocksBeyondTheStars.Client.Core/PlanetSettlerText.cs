// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The planet scan's settler block (#2998), worded once for the overview card and the Map tab report: one line per player
    /// who built on the body, in the server's order (most bases first) — <c>⌂ 3  ◆ 1  Lyxette</c>, the scanning player's own
    /// line marked — at most <see cref="MaxRows"/>, then "and n more"; a body nobody built on says so. A Quantum scan that
    /// pinned bases on the planet map says how many. An older server sends no settlers: no block at all.
    /// </summary>
    public static class PlanetSettlerText
    {
        /// <summary>Players one block lists before "and n more".</summary>
        public const int MaxRows = 5;

        /// <summary>The block's lines (empty when the server sent no settler list).</summary>
        public static List<string> Lines(PlanetScanResult? r, Func<string, string> localize)
        {
            var lines = new List<string>();
            if (r == null || !r.SettlersKnown)
            {
                return lines;
            }

            var settlers = r.Settlers ?? Array.Empty<NetPlanetSettler>();
            if (settlers.Length == 0)
            {
                lines.Add(localize("ui.overview.settlers_none"));
            }

            for (int i = 0; i < settlers.Length && i < MaxRows; i++)
            {
                lines.Add(Row(settlers[i], localize));
            }

            if (settlers.Length > MaxRows)
            {
                lines.Add(localize("ui.overview.settlers_more").Replace("{n}", (settlers.Length - MaxRows).ToString(CultureInfo.InvariantCulture)));
            }

            if (r.BasesRevealed > 0)
            {
                lines.Add(localize("ui.overview.bases_revealed").Replace("{n}", r.BasesRevealed.ToString(CultureInfo.InvariantCulture)));
            }

            return lines;
        }

        /// <summary>"⌂ 3  ◆ 1  Lyxette" — the station count only when there is one; the own line reads "you".</summary>
        public static string Row(NetPlanetSettler s, Func<string, string> localize)
        {
            string who = s.Relation == NetBase.RelationOwn ? localize("ui.overview.settlers_you").Replace("{name}", s.Name) : s.Name;
            string row = "⌂ " + s.Bases.ToString(CultureInfo.InvariantCulture);
            if (s.Stations > 0)
            {
                row += "  ◆ " + s.Stations.ToString(CultureInfo.InvariantCulture);
            }

            return row + "  " + who;
        }
    }
}
