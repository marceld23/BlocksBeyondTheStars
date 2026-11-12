// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Admin
{
    /// <summary>
    /// The admin panel's texts (#3509), Unity-free so they are tested headless: the locale key of a role, a player state, a
    /// report kind and a log category; a player row's detail line; a report's headline; the clock of a log row; the prefix of
    /// a direct line in the chat window; and the roster's layout signature (the Players tab rebuilds its rows only when that
    /// changes and otherwise just rewrites their texts). <c>localize</c> is a key → text lookup (the client's localizer).
    /// </summary>
    public static class AdminPanelText
    {
        private static readonly HashSet<string> KnownStates = new HashSet<string>(StringComparer.Ordinal)
        {
            AdminPlayerStates.OnFoot, AdminPlayerStates.Aboard, AdminPlayerStates.InSpace, AdminPlayerStates.Station,
            AdminPlayerStates.Vehicle, AdminPlayerStates.Train, AdminPlayerStates.Eva, AdminPlayerStates.Flying,
            AdminPlayerStates.Dead, AdminPlayerStates.Observer,
        };

        private static readonly HashSet<string> KnownKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            AdminReportKinds.Player, AdminReportKinds.Paint, AdminReportKinds.Shape, AdminReportKinds.Texture,
            AdminReportKinds.Help, AdminReportKinds.Feedback,
        };

        /// <summary>Every player state the server sends, for the locale guard.</summary>
        public static IEnumerable<string> States => KnownStates;

        /// <summary>Every report kind the server sends, for the locale guard.</summary>
        public static IEnumerable<string> Kinds => KnownKinds;

        /// <summary>The locale key of a roster role (<c>Shared.State.PlayerRole</c> as text); a fleet admin reads as such.</summary>
        public static string RoleKey(string? role, bool fleetAdmin)
        {
            if (fleetAdmin)
            {
                return "ui.admin.role_fleet";
            }

            return role switch
            {
                "Admin" => "ui.admin.role_admin",
                "WorldAdmin" => "ui.admin.role_owner",
                "Moderator" => "ui.admin.role_moderator",
                _ => "ui.admin.role_player",
            };
        }

        /// <summary>The locale key of a roster state (<see cref="AdminPlayerStates"/>); an unknown one reads as on foot.</summary>
        public static string StateKey(string? state)
            => "ui.admin.state_" + (state != null && KnownStates.Contains(state) ? state : AdminPlayerStates.OnFoot);

        /// <summary>The locale key of a report kind (<see cref="AdminReportKinds"/>); an unknown one reads as a player report.</summary>
        public static string KindKey(string? kind)
            => "ui.admin.kind_" + (kind != null && KnownKinds.Contains(kind) ? kind : AdminReportKinds.Player);

        /// <summary>The locale key of a log category's filter chip (<see cref="AdminEventCategories"/>).</summary>
        public static string CategoryKey(string? category) => "ui.admin.cat_" + (category ?? string.Empty);

        /// <summary>A position as whole blocks, "x · y · z".</summary>
        public static string Position(float x, float y, float z)
            => string.Format(CultureInfo.InvariantCulture, "{0} · {1} · {2}", (int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));

        /// <summary>The wall-clock time of a Unix second in the given offset from UTC, "HH:mm:ss".</summary>
        public static string Clock(long unixSeconds, TimeSpan offset)
            => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(offset).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>The wall-clock time of a Unix second in the player's own time zone, "HH:mm:ss".</summary>
        public static string LocalClock(long unixSeconds)
            => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>The date and time of a Unix second in the given offset from UTC, "yyyy-MM-dd HH:mm" (a report may be days
        /// old).</summary>
        public static string DateAndTime(long unixSeconds, TimeSpan offset)
            => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        /// <summary>The date and time of a Unix second in the player's own time zone, "yyyy-MM-dd HH:mm".</summary>
        public static string LocalDateTime(long unixSeconds)
            => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        /// <summary>A player row's first line: name, role and state.</summary>
        public static string PlayerHeadline(NetAdminPlayer p, Func<string, string> localize)
            => $"{p.Name}  ·  {localize(RoleKey(p.Role, p.FleetAdmin))}  ·  {localize(StateKey(p.State))}";

        /// <summary>A player row's second line: the position (when the server sent one — never to the visible variant of an
        /// official hosted world's owner), the health, then the duel / protected / chat-paused flags and the idle minutes (from
        /// one minute on) — only those that apply.</summary>
        public static string PlayerDetail(NetAdminPlayer p, Func<string, string> localize)
        {
            var sb = new StringBuilder();
            if (p.HasPosition)
            {
                sb.Append(Position(p.X, p.Y, p.Z)).Append("  ·  ");
            }

            sb.Append(Fill(localize("ui.admin.health"), ((int)Math.Ceiling(Math.Max(0f, p.Health))).ToString(CultureInfo.InvariantCulture)));
            if (p.InDuel)
            {
                sb.Append("  ·  ").Append(Fill(localize("ui.admin.duel_with"), p.DuelWith));
            }

            if (p.Protected)
            {
                sb.Append("  ·  ").Append(localize("ui.admin.protected"));
            }

            if (p.Silenced)
            {
                sb.Append("  ·  ").Append(localize("ui.admin.silenced"));
            }

            if (p.IdleSeconds >= 60)
            {
                sb.Append("  ·  ").Append(Fill(localize("ui.admin.idle"), (p.IdleSeconds / 60).ToString(CultureInfo.InvariantCulture)));
            }

            return sb.ToString();
        }

        /// <summary>The roster's layout: the bodies and the players on them, in order. The Players tab rebuilds its rows only
        /// when this changes — a moving or idling player only rewrites their row's text (the pad focus stays on its button).</summary>
        public static string RosterLayout(IReadOnlyList<AdminRosterGroup> groups)
        {
            var sb = new StringBuilder();
            foreach (var g in groups)
            {
                sb.Append('[').Append(g.LocationId).Append(']');
                foreach (var p in g.Players)
                {
                    sb.Append(p.PlayerId).Append('|');
                }
            }

            return sb.ToString();
        }

        /// <summary>A report's headline: its kind, who filed it and, for a report about someone, about whom.</summary>
        public static string ReportHeadline(NetAdminReport r, Func<string, string> localize)
        {
            string kind = localize(KindKey(r.Kind));
            string reporter = string.IsNullOrEmpty(r.ReporterName) ? "?" : r.ReporterName;
            return string.IsNullOrEmpty(r.TargetName) ? $"{kind}  ·  {reporter}" : $"{kind}  ·  {reporter} → {r.TargetName}";
        }

        /// <summary>
        /// How the chat window shows a direct line (#3507), or null when it is not this player's own conversation (an admin's
        /// panel mirrors other admins' conversations — those stay in the panel). An incoming line reads "Admin Name → you",
        /// an outgoing one "You → Name"; the admin's side is named as an admin.
        /// </summary>
        public static string? DirectLinePrefix(DirectChatMessage line, string myPlayerId, Func<string, string> localize)
        {
            if (line is null || string.IsNullOrEmpty(myPlayerId))
            {
                return null;
            }

            bool mine = line.FromId == myPlayerId;
            if (!mine && line.ToId != myPlayerId)
            {
                return null;
            }

            // The other side is an admin when an admin wrote to me, or when I (a player) answer one.
            bool partnerIsAdmin = mine ? !line.FromAdmin : line.FromAdmin;
            string partner = mine ? line.ToName : line.FromName;
            if (partnerIsAdmin)
            {
                partner = Fill(localize("ui.chat.dm_admin"), partner);
            }

            return Fill(localize(mine ? "ui.chat.dm_out" : "ui.chat.dm_in"), partner);
        }

        /// <summary>Puts <paramref name="value"/> into the <c>{0}</c> placeholder.</summary>
        public static string Fill(string template, string? value)
            => (template ?? string.Empty).Replace("{0}", value ?? string.Empty);
    }
}
