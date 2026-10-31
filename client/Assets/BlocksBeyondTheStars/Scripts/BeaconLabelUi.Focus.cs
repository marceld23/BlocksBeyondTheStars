// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The base core screen's focus row (#3036, Justus: "a base can only have one upgrade, which can be changed"): five
    /// buttons — none, Market, Miner, Lumberjack, Radio centre — above the yield strip, the chosen one ticked and greyed out,
    /// and under them what the focus does; a Miner adds its shaft's depth and the next ore down, a Lumberjack on a world
    /// without trees says so. A click sends the new focus; the server's status answer redraws the row. Free, any time.
    /// </summary>
    public sealed partial class BeaconLabelUi
    {
        private GameObject _focusPanel;
        private Text _focusDesc;
        private readonly Button[] _focusButtons = new Button[BaseFocus.All.Length];

        private void BuildFocusRow(Transform root, float stripY)
        {
            const float w = 760f, h = 136f;
            var panel = UiKit.AddPanel(root, (1920f - w) * 0.5f, stripY - h - 12f, w, h, UiKit.Panel);
            _focusPanel = panel.gameObject;
            UiKit.AddText(panel.transform, 16f, 6f, w - 32f, 26f, L("ui.base.focus.title"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            int n = BaseFocus.All.Length;
            float bw = (w - 32f - ((n - 1) * 8f)) / n;
            for (int i = 0; i < n; i++)
            {
                string focus = BaseFocus.All[i];
                _focusButtons[i] = UiKit.AddButton(panel.transform, 16f + (i * (bw + 8f)), 36f, bw, 42f, L(BaseFocus.NameKey(focus)), () => PickFocus(focus));
            }

            _focusDesc = UiKit.AddText(panel.transform, 16f, 84f, w - 32f, 48f, string.Empty, 15, UiKit.TextCol, TextAnchor.UpperLeft);
            _focusDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            _focusPanel.SetActive(false);
        }

        private void PickFocus(string focus)
        {
            if (_yieldKind == YieldIntent.KindBase)
            {
                Game?.Network?.SendBaseFocus(_yieldBody, _yieldCell.x, _yieldCell.y, _yieldCell.z, focus);
            }
        }

        /// <summary>Draws the row for a base's status; a station's store has no focus.</summary>
        private void ShowFocus(YieldStatus s)
        {
            if (_focusPanel == null)
            {
                return;
            }

            if (s.Kind != YieldIntent.KindBase)
            {
                _focusPanel.SetActive(false);
                return;
            }

            string current = BaseFocus.Normalize(s.Focus);
            for (int i = 0; i < _focusButtons.Length; i++)
            {
                string focus = BaseFocus.All[i];
                bool chosen = focus == current;
                _focusButtons[i].interactable = !chosen;
                var label = _focusButtons[i].GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = (chosen ? "✓ " : string.Empty) + L(BaseFocus.NameKey(focus));
                }
            }

            _focusDesc.text = FocusText(s, current);
            _focusPanel.SetActive(true);
        }

        /// <summary>What the focus does, and a Miner's shaft or a Lumberjack's barren world.</summary>
        private string FocusText(YieldStatus s, string focus)
        {
            var sb = new StringBuilder(L(BaseFocus.DescKey(focus)));
            if (focus == BaseFocus.Miner)
            {
                sb.Append('\n').Append(L("ui.base.focus.depth").Replace("{n}", s.Depth.ToString(CultureInfo.InvariantCulture))).Append("  ·  ");
                sb.Append(string.IsNullOrEmpty(s.NextOre)
                    ? L("ui.base.focus.all_ores")
                    : L("ui.base.focus.next_ore").Replace("{item}", L("item." + s.NextOre + ".name"))
                        .Replace("{n}", s.NextOreDepth.ToString(CultureInfo.InvariantCulture)));
            }
            else if (focus == BaseFocus.Lumberjack && s.NoTrees)
            {
                sb.Append('\n').Append(L("ui.base.focus.no_trees"));
            }

            return sb.ToString();
        }
    }
}
