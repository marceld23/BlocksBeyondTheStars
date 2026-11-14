// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Codex's discovery groups and, #3642, its lost civilizations (heading <c>ui.wiki.discoveries.civilization</c>): a
    /// civilization is listed once per player (ledger <c>civ:&lt;key&gt;</c>), named after the civilization file's Codex title
    /// with its Codex text under it, found where the player first came near one of its sites — the words come from
    /// <see cref="CivilizationText"/>.
    /// </summary>
    public sealed partial class WikiUI
    {
        /// <summary>The discovery groups, in the order the Codex lists them (heading <c>ui.wiki.discoveries.&lt;kind&gt;</c>).</summary>
        private static readonly string[] DiscoveryKinds =
        {
            "place", "creature", "microfauna", "tree", "flora", "block", "monument", "landmark", GiantWreckText.CodexGroup,
            CivilizationText.CodexGroup, "asteroid",
        };

        /// <summary>The group of a ledger key: the giant wrecks' and the civilizations' own, else the kind before the first colon
        /// (<paramref name="colon"/>; "asteroid" has none).</summary>
        private static string DiscoveryKindOf(string ledgerKey, int colon)
            => GiantWreckText.CodexKind(ledgerKey) ?? CivilizationText.CodexKind(ledgerKey)
               ?? (colon > 0 ? ledgerKey.Substring(0, colon) : ledgerKey);

        /// <summary>One discovery row of a group: name, where it was found, the dim text under it (a landmark's, a giant wreck's
        /// and a civilization's Codex line).</summary>
        private (string Name, string Where, string Desc) DiscoveryRow(string kind, string ledgerKey, string recordedName, string subject)
        {
            if (kind == GiantWreckText.CodexGroup)
            {
                return GiantWreckDiscovery(ledgerKey, recordedName);
            }

            if (kind == CivilizationText.CodexGroup)
            {
                return CivilizationDiscovery(ledgerKey, recordedName);
            }

            return (SubjectName(subject), WhereFound(ledgerKey, kind), kind == "landmark" ? LandmarkDesc(ledgerKey) : string.Empty);
        }

        /// <summary>One discovery row of the civilization group.</summary>
        private (string Name, string Where, string Desc) CivilizationDiscovery(string ledgerKey, string recordedName)
        {
            var (name, desc) = CivilizationText.CodexLine(ledgerKey, recordedName, Game?.Content,
                key => Game?.Localizer != null && Game.Localizer.Has(key) ? Desc(key) : null);
            return (name, WhereFound(ledgerKey, CivilizationText.CodexGroup), desc);
        }
    }
}
