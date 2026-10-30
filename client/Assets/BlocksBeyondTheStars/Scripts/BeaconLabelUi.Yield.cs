// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The yield strip of the base core and station core screens (#2999): a panel above the card says what the store holds
    /// ("Ready to collect: 14 of 64 · 10 per hour" and the items), and <b>Collect</b> moves it into the backpack. The server
    /// fills the store from the wall clock and answers every read and collect with a <see cref="YieldStatus"/>; this strip
    /// only shows the latest answer for the screen that is open.
    /// </summary>
    public sealed partial class BeaconLabelUi
    {
        private GameObject _yieldPanel;
        private Text _yieldText;
        private Button _yieldCollect;
        private string _yieldKind = string.Empty;
        private string _yieldBody = string.Empty;
        private Vector3Int _yieldCell;
        private string _yieldStation = string.Empty;

        private void BuildYieldStrip(Transform root, float cardY)
        {
            const float w = 760f, h = 96f;
            var panel = UiKit.AddPanel(root, (1920f - w) * 0.5f, cardY - h - 12f, w, h, UiKit.Panel);
            _yieldPanel = panel.gameObject;
            _yieldText = UiKit.AddText(panel.transform, 16f, 8f, w - 220f, h - 16f, string.Empty, 17, UiKit.TextCol, TextAnchor.MiddleLeft);
            _yieldText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _yieldCollect = UiKit.AddButton(panel.transform, w - 196f, 22f, 180f, 52f, L("ui.yield.collect"), Collect);
            _yieldPanel.SetActive(false);
        }

        private void ClearYield()
        {
            _yieldKind = string.Empty;
            if (_yieldPanel != null)
            {
                _yieldPanel.SetActive(false);
            }
        }

        private void ShowBaseYield(string body, Vector3Int cell)
        {
            _yieldKind = YieldIntent.KindBase;
            _yieldBody = body ?? string.Empty;
            _yieldCell = cell;
            Game?.Network?.SendBaseYield(_yieldBody, cell.x, cell.y, cell.z, collect: false);
        }

        private void ShowStationYield(string stationId)
        {
            _yieldKind = YieldIntent.KindStation;
            _yieldStation = stationId ?? string.Empty;
            Game?.Network?.SendStationYield(_yieldStation, collect: false);
        }

        private void Collect()
        {
            if (_yieldKind == YieldIntent.KindBase)
            {
                Game?.Network?.SendBaseYield(_yieldBody, _yieldCell.x, _yieldCell.y, _yieldCell.z, collect: true);
            }
            else if (_yieldKind == YieldIntent.KindStation)
            {
                Game?.Network?.SendStationYield(_yieldStation, collect: true);
            }
        }

        private bool IsForThisScreen(YieldStatus s)
            => s.Kind == _yieldKind && (s.Kind == YieldIntent.KindStation
                ? s.StationId == _yieldStation
                : s.BodyId == _yieldBody && s.CellX == _yieldCell.x && s.CellY == _yieldCell.y && s.CellZ == _yieldCell.z);

        private void OnYieldStatus(YieldStatus s)
        {
            if (!Shown || s == null || _yieldKind.Length == 0 || !IsForThisScreen(s) || !IsBuilt)
            {
                return;
            }

            if (s.Capacity <= 0)
            {
                _yieldPanel.SetActive(false); // not a store of this player's (or an older server)
                return;
            }

            _yieldText.text = YieldText(s);
            _yieldCollect.interactable = s.Ready > 0;
            _yieldPanel.SetActive(true);
        }

        /// <summary>"Ready to collect: 14 of 64 · 10 per hour" and the items on a second line.</summary>
        private string YieldText(YieldStatus s)
        {
            var sb = new StringBuilder();
            sb.Append(L("ui.yield.ready").Replace("{n}", s.Ready.ToString(CultureInfo.InvariantCulture))
                .Replace("{max}", s.Capacity.ToString(CultureInfo.InvariantCulture)));
            sb.Append("  ·  ");
            sb.Append(s.PerHour <= 0f
                ? L("ui.yield.off")
                : L("ui.yield.rate").Replace("{n}", Mathf.RoundToInt(s.PerHour).ToString(CultureInfo.InvariantCulture)));
            if (s.Items != null && s.Items.Length > 0)
            {
                sb.Append('\n');
                for (int i = 0; i < s.Items.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append("  ·  ");
                    }

                    sb.Append(L("item." + s.Items[i].Item + ".name")).Append(" ×").Append(s.Items[i].Count.ToString(CultureInfo.InvariantCulture));
                }
            }

            return sb.ToString();
        }
    }
}
