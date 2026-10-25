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
    /// first, then the ground; an enemy the player has met shows its name, health, damage, loot, what it is and how to beat
    /// it — one not met yet is "???", so the chapter fills as the player gets around. The numbers are
    /// <c>data/enemies.json</c>'s, the same the server fights with.
    /// </summary>
    public static class EnemyCodexText
    {
        /// <summary>The chapter as uGUI rich text. <paramref name="l"/> localizes a key, <paramref name="itemName"/> names an
        /// item key in the reader's language.</summary>
        public static string Build(IReadOnlyList<EnemyDefinition> enemies, ICollection<string> met, Func<string, string> l, Func<string, string> itemName)
        {
            var sb = new StringBuilder();
            sb.Append("<b><size=24>").Append(l("ui.wiki.enemies")).Append("</size></b>\n\n");
            int known = 0;
            foreach (var e in enemies)
            {
                if (met.Contains(e.Key))
                {
                    known++;
                }
            }

            sb.Append(string.Format(CultureInfo.InvariantCulture, l("ui.wiki.enemies.count"), known, enemies.Count)).Append("\n\n");
            foreach (var where in new[] { EnemyWhere.Space, EnemyWhere.Planet })
            {
                sb.Append("<b><size=20>").Append(l("ui.wiki.enemies.where_" + where)).Append("</size></b>\n\n");
                foreach (var e in enemies)
                {
                    if (e.Where == where)
                    {
                        AppendEntry(sb, e, met.Contains(e.Key), l, itemName);
                    }
                }
            }

            return sb.ToString();
        }

        private static void AppendEntry(StringBuilder sb, EnemyDefinition e, bool met, Func<string, string> l, Func<string, string> itemName)
        {
            if (!met)
            {
                sb.Append("<b>???</b>\n  <color=#9fb4c8>").Append(l("ui.wiki.enemies.unknown")).Append("</color>\n\n");
                return;
            }

            var normal = e.Tier(EnemyTiers.Normal);
            e.Tiers.TryGetValue(EnemyTiers.Elite, out var elite);
            sb.Append("<b>").Append(l(e.NameKey)).Append("</b>\n");
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
