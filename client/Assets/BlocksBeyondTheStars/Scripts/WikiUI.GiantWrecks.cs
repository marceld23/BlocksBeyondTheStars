// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3611: the Codex's giant ship wrecks (heading <c>ui.wiki.discoveries.giantwreck</c>). A wreck on a planet is listed
    /// once per body and ship, named after its ship class with the class's Codex text; a wreck in space is listed once per
    /// wreck, named after its coined name, found in its star system, with its class and the class's text when this session
    /// scanned it (<see cref="GameBootstrap.GiantWreckClassByName"/>) — the words come from <see cref="GiantWreckText"/>.
    /// </summary>
    public sealed partial class WikiUI
    {
        /// <summary>One discovery row of the giant wreck group: name, where it was found, the dim text under it.</summary>
        private (string Name, string Where, string Desc) GiantWreckDiscovery(string ledgerKey, string recordedName)
        {
            var (name, desc) = GiantWreckText.CodexLine(ledgerKey, recordedName,
                key => Game?.Localizer != null && Game.Localizer.Has(key) ? Desc(key) : null, Game?.GiantWreckClassByName);

            // A wreck in space drifts in its system; the body the flight was anchored to says nothing about it.
            string where = WhereFound(ledgerKey, GiantWreckText.IsSpaceLedger(ledgerKey) ? "place" : GiantWreckText.CodexGroup);
            return (name, where, desc);
        }
    }
}
