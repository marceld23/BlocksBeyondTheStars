// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Localization;
using TMPro;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The observer badge at the top centre (#487) and its admin-mode line (#3509): <b>ADMIN</b> in the invisible admin mode,
    /// <b>ADMIN VIEW</b> in the visible variant of an official hosted world, <b>SPECTATOR</b> for the fleet admin's
    /// <c>/spectate</c>; under it, while in admin mode, the open reports and unread direct lines (from
    /// <see cref="AdminPanelUi"/>) and the player being followed. The hotbar stays hidden while observing (HudUi.Hotbar.cs).
    /// </summary>
    public sealed partial class HudUi
    {
        private TMP_Text _adminCounts;
        private int _adminCountsReports = -1, _adminCountsUnread = -1;
        private string _adminCountsFollow;
        private Localizer _adminCountsLoc;

        private void BuildAdminCounts(Transform root)
        {
            _adminCounts = UiText.Add(root, W / 2f - 260, 54, 520, 20, string.Empty, 14, UiKit.Warn, TextAnchor.MiddleCenter);
            UiText.Style(_adminCounts, UiText.Look.Outline);
        }

        private void RefreshObserverBadge(Localizer loc)
        {
            byte mode = Game.Network != null ? Game.Network.AdminMode : AdminModes.Off;
            _observer.text = mode == AdminModes.Invisible ? loc.Get("ui.hud.admin")
                : mode == AdminModes.Visible ? loc.Get("ui.hud.admin_visible")
                : Game.Spectating ? loc.Get("ui.hud.observer")
                : string.Empty;

            var panel = mode != AdminModes.Off ? Game.Context.Panel<AdminPanelUi>() : null;
            int reports = panel != null ? panel.OpenReports : 0;
            int unread = panel != null ? panel.UnreadDirect : 0;
            string follow = panel != null ? panel.FollowingName : string.Empty;
            if (reports == _adminCountsReports && unread == _adminCountsUnread && follow == _adminCountsFollow
                && ReferenceEquals(loc, _adminCountsLoc))
            {
                return; // composed only when something in it changed
            }

            _adminCountsReports = reports;
            _adminCountsUnread = unread;
            _adminCountsFollow = follow;
            _adminCountsLoc = loc;
            string line = reports > 0 || unread > 0
                ? loc.Get("ui.hud.admin_counts").Replace("{0}", reports.ToString()).Replace("{1}", unread.ToString())
                : string.Empty;
            if (!string.IsNullOrEmpty(follow))
            {
                string following = loc.Get("ui.hud.admin_following").Replace("{0}", follow);
                line = line.Length > 0 ? line + "  ·  " + following : following;
            }

            _adminCounts.text = line;
        }
    }
}
