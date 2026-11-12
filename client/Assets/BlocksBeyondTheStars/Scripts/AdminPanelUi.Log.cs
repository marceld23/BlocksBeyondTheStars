// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Admin;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The admin panel's Log tab (#3506): the world's event log, newest first (the last <see cref="MaxLogRows"/>), each row
    /// with its local time, its category and its text (<see cref="AdminEventLogModel.Format"/> through the localizer); a row
    /// with a place offers <b>Jump</b> in the invisible admin mode. The chips above the list show or hide a category.
    /// </summary>
    public sealed partial class AdminPanelUi
    {
        private const int MaxLogRows = 200;
        private const float LogRowH = 44f, ChipW = 236f, ChipGap = 10f, ChipH = 44f;

        private readonly Button[] _chips = new Button[6];

        private void BuildLogTab()
        {
            var categories = AdminEventLogModel.Categories;
            for (int i = 0; i < categories.Count && i < _chips.Length; i++)
            {
                string category = categories[i];
                _chips[i] = UiKit.AddFitButton(_tabRoot, i * (ChipW + ChipGap), 0f, ChipW, ChipH, L(AdminPanelText.CategoryKey(category)),
                    () => ToggleCategory(category));
            }

            _list = AddList("AdminLog", ChipH + 12f);
        }

        private void ToggleCategory(string category)
        {
            var log = Game?.Network?.AdminLog;
            if (log != null)
            {
                log.SetShown(category, !log.IsShown(category)); // the model's version moves: the list follows
            }
        }

        /// <summary>A shown category's chip is lit, a hidden one dimmed.</summary>
        private void StyleChips(AdminEventLogModel log)
        {
            var categories = AdminEventLogModel.Categories;
            for (int i = 0; i < categories.Count && i < _chips.Length; i++)
            {
                if (_chips[i] != null)
                {
                    _chips[i].GetComponent<Image>().color = log.IsShown(categories[i]) ? new Color(0.55f, 0.88f, 1f) : UiKit.TabLocked;
                }
            }
        }

        private void RefreshLog(NetworkClient net)
        {
            StyleChips(net.AdminLog);
            ClearRows(_list);
            var events = net.AdminLog.Newest(MaxLogRows);
            if (events.Count == 0)
            {
                AddEmptyNote(_list, "ui.admin.log_none");
                _list.sizeDelta = new Vector2(0f, 64f);
                return;
            }

            bool canJump = Invisible;
            float width = ContentW - 16f, y = 0f;
            foreach (var ev in events)
            {
                AddLogRow(ev, y, width, canJump && ev.HasPosition);
                y += LogRowH;
            }

            _list.sizeDelta = new Vector2(0f, y + 8f);
        }

        private void AddLogRow(NetAdminEvent ev, float y, float width, bool jump)
        {
            AddDataText(_list, 8f, y, 104f, LogRowH, AdminPanelText.LocalClock(ev.UnixTime), 16, UiKit.CyanDim);
            AddDataText(_list, 118f, y, 170f, LogRowH, L(AdminPanelText.CategoryKey(ev.Category)), 15, CategoryColour(ev.Category));
            float textW = width - 296f - (jump ? 160f : 8f);
            AddDataText(_list, 296f, y, textW, LogRowH, AdminEventLogModel.Format(ev, _loc), 17, UiKit.TextCol);
            if (jump)
            {
                string location = ev.LocationId;
                float x = ev.X, py = ev.Y, z = ev.Z;
                UiKit.AddFitButton(_list, width - 150f, y + 4f, 140f, LogRowH - 8f, L("ui.admin.jump"), () => JumpToPlace(location, x, py, z));
            }
        }

        /// <summary>PvP and deaths stand out; moderation and reports in amber; the rest in the panel's blue.</summary>
        private static Color CategoryColour(string category) => category switch
        {
            AdminEventCategories.Pvp => new Color(1f, 0.55f, 0.48f),
            AdminEventCategories.Death => new Color(1f, 0.55f, 0.48f),
            AdminEventCategories.Report => UiKit.Warn,
            AdminEventCategories.Moderation => UiKit.Warn,
            _ => UiKit.Cyan,
        };
    }
}
