// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2717: the "who lives here" text of the base core screen (E at your own Grundstein), from the server's
    /// <see cref="BaseResidentList"/>: a title with the count, the bed rule while there is room for more (#2715 — without a
    /// bed only the founding settler lives there), then one line per resident — name, role, what they are doing, where they
    /// are from the core (or that they are not here right now) and whether they have a bed. Unity-free so the headless suite
    /// pins it; <c>BeaconLabelUi</c> shows it under the rename field.
    /// </summary>
    public static class BaseResidentText
    {
        /// <summary>The whole text, lines separated by '\n'; <paramref name="localize"/> resolves locale keys.</summary>
        public static string Format(BaseResidentList list, Func<string, string> localize)
        {
            var people = list.People ?? Array.Empty<NetBaseResident>();
            int cap = list.Cap;
            var sb = new StringBuilder();
            sb.Append(localize("ui.base.res_title").Replace("{n}", Num(people.Length)).Replace("{max}", Num(cap)));
            if (people.Length == 0)
            {
                sb.Append('\n').Append(localize("ui.base.res_none"));
                return sb.ToString();
            }

            if (people.Length < cap)
            {
                sb.Append('\n').Append(localize("ui.base.res_beds_hint").Replace("{max}", Num(cap)));
            }

            foreach (var p in people)
            {
                sb.Append('\n').Append(Line(p, localize));
            }

            return sb.ToString();
        }

        /// <summary>One resident: "• Mila (founder) — Craftsman · working · 12 m to the northeast · has a bed".</summary>
        public static string Line(NetBaseResident p, Func<string, string> localize)
        {
            string role = string.IsNullOrEmpty(p.RoleKey) ? string.Empty : localize(p.RoleKey);
            bool named = !string.IsNullOrEmpty(p.Name);
            var sb = new StringBuilder("• ").Append(named ? p.Name : role);
            if (p.Founder)
            {
                sb.Append(" (").Append(localize("ui.base.res_founder")).Append(')');
            }

            if (named && role.Length > 0)
            {
                sb.Append(" — ").Append(role);
            }

            if (!p.Here)
            {
                sb.Append(" · ").Append(localize("ui.base.res_away"));
            }
            else
            {
                if (!string.IsNullOrEmpty(p.ActivityKey))
                {
                    sb.Append(" · ").Append(localize(p.ActivityKey));
                }

                sb.Append(" · ").Append(string.IsNullOrEmpty(p.DirectionKey)
                    ? localize("ui.base.res_near_core")
                    : localize("ui.base.res_where").Replace("{m}", Num(p.Distance)).Replace("{dir}", localize(p.DirectionKey)));
            }

            sb.Append(" · ").Append(localize(p.HasBed ? "ui.base.res_bed" : "ui.base.res_no_bed"));
            return sb.ToString();
        }

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
