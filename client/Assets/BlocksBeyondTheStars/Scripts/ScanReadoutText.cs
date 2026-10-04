// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The words of a scan readout (#484, #2238): the title and the description line built from the STRUCTURED
    /// payload, in this client's language. Shared by the hand scanner's HUD panel and the ship scanner's card on the
    /// right (#2247), so a readout reads the same wherever it is shown.
    /// </summary>
    public static class ScanReadoutText
    {
        /// <summary>#2238: the kinds the ship scanner reads in space whose readout is a sentence plus traits.</summary>
        public static bool IsSpaceReadout(string kind) => kind is "pod" or "station" or "machine" or "bandit" or "wormhole" or "anomaly";

        /// <summary>The description line: a space readout's sentence and traits, a creature's habitat/activity/temperament
        /// traits, a yield/resource list with localized item names, or a single remark key. Falls back to the legacy
        /// English <see cref="ScanResult.Info"/> only when the server is older than the structured fields.</summary>
        public static string Info(GameContent content, Localizer loc, ScanResult scan)
        {
            // #2238: the ship scanner's space readouts carry a sentence AND a few traits — show both.
            if (IsSpaceReadout(scan.Kind) && !string.IsNullOrEmpty(scan.InfoKey))
            {
                string info = loc.Get(scan.InfoKey);
                if (scan.TraitKeys != null && scan.TraitKeys.Length > 0)
                {
                    var traitParts = new string[scan.TraitKeys.Length];
                    for (int i = 0; i < traitParts.Length; i++)
                    {
                        traitParts[i] = loc.Get(scan.TraitKeys[i]);
                    }

                    info += "\n" + string.Join("  ·  ", traitParts);
                }

                return info;
            }

            var traits = scan.TraitKeys;
            if (traits != null && traits.Length > 0)
            {
                var parts = new string[traits.Length];
                for (int i = 0; i < traits.Length; i++)
                {
                    // "key|item" (#2082): a trait that names an item — "Loves: {item}" — in this client's language.
                    int bar = traits[i].IndexOf('|');
                    parts[i] = bar > 0
                        ? loc.Get(traits[i].Substring(0, bar)).Replace("{item}", ItemOrBlockName(content, loc, traits[i].Substring(bar + 1)))
                        : loc.Get(traits[i]);
                }

                return string.Join("  ·  ", parts);
            }

            var drops = scan.Drops;
            if (drops != null && drops.Length > 0)
            {
                var parts = new string[drops.Length];
                for (int i = 0; i < drops.Length; i++)
                {
                    // Count 0 = a resource TYPE with no quantity (asteroid scan) — no "×n" suffix then.
                    string name = ItemOrBlockName(content, loc, drops[i].Item);
                    parts[i] = drops[i].Count > 0 ? $"{name} ×{drops[i].Count}" : name;
                }

                // #2238: a derelict's salvage reads "Contents" — "Yield" is for what a creature or a plant gives.
                string label = loc.Get(scan.Kind == "asteroid" ? "ui.scan.resources" : scan.Kind == "wreck" ? "ui.scan.contents" : "ui.scan.yield");
                return $"{label}: {string.Join(", ", parts)}";
            }

            if (!string.IsNullOrEmpty(scan.InfoKey))
            {
                return loc.Get(scan.InfoKey);
            }

            return scan.Info; // pre-#484 server
        }

        /// <summary>The scan title: the subject's name — a wormhole reads "Wormhole → {its twin system}" (#2242), a
        /// Guardian machine its kind (#2238), everything else as before.</summary>
        public static string Title(GameContent content, Localizer loc, ScanResult scan)
        {
            if (scan.Kind == "wormhole")
            {
                return loc.Get("ui.scan.subject.wormhole") + " → " + (string.IsNullOrEmpty(scan.Subject) ? "???" : scan.Subject);
            }

            return SubjectName(content, loc, string.IsNullOrEmpty(scan.Subject) ? scan.SubjectKey : scan.Subject);
        }

        /// <summary>Resolves a scan subject key to a readable, localized name (block / item / creature) so the readout
        /// says what it is ("Stone") rather than the raw key ("stone").</summary>
        public static string SubjectName(GameContent content, Localizer loc, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return key;
            }

            if (content?.GetBlock(key) is { } b)
            {
                return loc.Get(b.NameKey);
            }

            if (content?.GetItem(key) is { } it)
            {
                return loc.Get(it.NameKey);
            }

            // Not a block/item key: the server already resolved creatures/flora/trees to their coined,
            // language-neutral display name (CreatureSpecies.Name etc.) and asteroids to a plain label, so
            // the subject IS the display name — show it as-is. (A real per-species localization key is
            // honoured if one exists; Localizer.Get returns "[key]" for a missing key, so we must probe
            // with Has() rather than inspect the returned string.)
            string creatureKey = $"creature.{key}.name";
            if (loc.Has(creatureKey))
            {
                return loc.Get(creatureKey);
            }

            // Generic scan subjects that are neither content nor a species — currently just "asteroid" (#484).
            string subjectKey = $"ui.scan.subject.{key}";
            return loc.Has(subjectKey) ? loc.Get(subjectKey) : key;
        }

        /// <summary>Localized name for an item key, falling back to the block table (drop lists mix both).</summary>
        public static string ItemOrBlockName(GameContent content, Localizer loc, string key)
        {
            if (content?.GetItem(key) is { } item)
            {
                return loc.Get(item.NameKey);
            }

            return content?.GetBlock(key) is { } block ? loc.Get(block.NameKey) : key;
        }
    }
}
