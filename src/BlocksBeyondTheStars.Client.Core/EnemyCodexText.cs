// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Codex's "Enemies" chapter (#2971, Justus: "a tablet with the enemies — from UFOs and spaceships to drones — with
    /// damage, health, abilities and names"). Unity-free, so the text is unit-tested; <c>WikiUI</c> only shows it. Space
    /// first, then the ground; every enemy shows its name, health, damage, loot, what it is and how to beat it from the
    /// start (#3031, Justus: "you should not have to discover them first") — one the player has met carries a "✓ met" mark,
    /// and the head counts them. The numbers are <c>data/enemies.json</c>'s, the same the server fights with. <c>WikiUI</c>
    /// draws the head, each group's heading and each entry beside its portrait (#3032); <see cref="Build"/> is the chapter as
    /// one text.
    /// </summary>
    public static class EnemyCodexText
    {
        /// <summary>The chapter as uGUI rich text. <paramref name="l"/> localizes a key, <paramref name="itemName"/> names an
        /// item key in the reader's language.</summary>
        public static string Build(IReadOnlyList<EnemyDefinition> enemies, ICollection<string> met, Func<string, string> l, Func<string, string> itemName)
        {
            var sb = new StringBuilder(Head(enemies, met, l));
            foreach (var group in Groups(enemies))
            {
                sb.Append(GroupHeading(group.Where, l));
                foreach (var e in group.Enemies)
                {
                    sb.Append(Entry(e, met.Contains(e.Key), l, itemName));
                }
            }

            return sb.ToString();
        }

        /// <summary>The chapter's title and the "x of n enemies met" line.</summary>
        public static string Head(IReadOnlyList<EnemyDefinition> enemies, ICollection<string> met, Func<string, string> l)
        {
            int known = 0;
            foreach (var e in enemies)
            {
                if (met.Contains(e.Key))
                {
                    known++;
                }
            }

            return "<b><size=24>" + l("ui.wiki.enemies") + "</size></b>\n\n"
                   + string.Format(CultureInfo.InvariantCulture, l("ui.wiki.enemies.count"), known, enemies.Count) + "\n\n";
        }

        /// <summary>The enemies by where they are met — space first, then the planets — in the file's order.</summary>
        public static List<(string Where, List<EnemyDefinition> Enemies)> Groups(IReadOnlyList<EnemyDefinition> enemies)
        {
            var groups = new List<(string, List<EnemyDefinition>)>();
            foreach (var where in new[] { EnemyWhere.Space, EnemyWhere.Planet })
            {
                var list = new List<EnemyDefinition>();
                foreach (var e in enemies)
                {
                    if (e.Where == where)
                    {
                        list.Add(e);
                    }
                }

                groups.Add((where, list));
            }

            return groups;
        }

        /// <summary>A group's heading ("In space", "On planets").</summary>
        public static string GroupHeading(string where, Func<string, string> l)
            => "<b><size=20>" + l("ui.wiki.enemies.where_" + where) + "</size></b>\n\n";

        /// <summary>One enemy: its name (with "✓ met" when <paramref name="met"/>), health and damage (the stronger kind in
        /// brackets), loot, what it is and the tip.</summary>
        public static string Entry(EnemyDefinition e, bool met, Func<string, string> l, Func<string, string> itemName)
        {
            var sb = new StringBuilder();
            var normal = e.Tier(EnemyTiers.Normal);
            e.Tiers.TryGetValue(EnemyTiers.Elite, out var elite);
            sb.Append("<b>").Append(l(e.NameKey)).Append("</b>");
            if (met)
            {
                sb.Append("  <color=#7fe0a8>✓ ").Append(l("ui.wiki.enemies.met")).Append("</color>");
            }

            sb.Append('\n');
            sb.Append("  ").Append(l("ui.wiki.enemies.health")).Append(": ").Append(Health(normal));
            if (elite is not null)
            {
                sb.Append(" (").Append(l("ui.wiki.enemies.elite")).Append(": ").Append(Health(elite)).Append(')');
            }

            sb.Append("  ·  ").Append(l("ui.wiki.enemies.damage")).Append(": ").Append(Dps(normal.DamagePerSecond));
            if (elite is not null)
            {
                sb.Append(" (").Append(l("ui.wiki.enemies.elite")).Append(": ").Append(Dps(elite.DamagePerSecond)).Append(')');
            }

            sb.Append('\n');
            if (normal.Loot.Count > 0)
            {
                sb.Append("  ").Append(l("ui.wiki.enemies.loot")).Append(": ");
                for (int i = 0; i < normal.Loot.Count; i++)
                {
                    sb.Append(i > 0 ? ", " : string.Empty).Append(normal.Loot[i].Count).Append("× ").Append(itemName(normal.Loot[i].Item));
                }

                sb.Append('\n');
            }

            AppendLine(sb, "  <i>", l(e.DescriptionKey), e.DescriptionKey, "</i>");
            AppendLine(sb, "  " + l("ui.wiki.enemies.tip") + ": ", l(e.TipKey), e.TipKey, string.Empty);
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>One optional line — skipped when its key has no text (an unlocalized key reads back as itself).</summary>
        private static void AppendLine(StringBuilder sb, string prefix, string text, string key, string suffix)
        {
            if (!string.IsNullOrEmpty(text) && text != key)
            {
                sb.Append(prefix).Append(text).Append(suffix).Append('\n');
            }
        }

        private static string Health(EnemyTier tier)
            => tier.HullMax > tier.Hull
                ? Number(tier.Hull) + "–" + Number(tier.HullMax)
                : Number(tier.Hull);

        private static string Dps(float dps) => Number(dps) + "/s";

        private static string Number(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
