// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using TMPro;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The opt-in coordinate line (#2993): with the setting <c>ShowCoordinates</c> on (default off), one small line says where
    /// the player stands in the world's own block coordinates — <c>X 812 · Y 71 · Z -1904</c>, the numbers the Quantum
    /// Teleporter takes. Hidden in space, on a spacewalk, aboard a ship or a station: those scenes share their interior
    /// coordinates and a number there would mean nothing.
    /// <para>#3033 (Justus: "under the player values, like in Minecraft"): the line sits right under the vitals panel, whose
    /// height varies (<see cref="VitalsBottomY"/>), and the toast line makes room below it while it shows.</para>
    /// </summary>
    public sealed partial class HudUi
    {
        private const float CoordsGap = 4f, CoordsH = 30f;

        /// <summary>The toast line's own height (y down from the top) when nothing sits between it and the vitals.</summary>
        private const float ToastBaseY = 268f;

        private GameObject _coordsPanel;
        private RectTransform _coordsRect;
        private TMP_Text _coordsText;
        private Vector3Int _coordsShown = new Vector3Int(int.MinValue, 0, 0);

        private void BuildCoordinates(Transform root)
        {
            _coordsPanel = Panel(root, 10f, VitalsBottomY + CoordsGap, 226f, CoordsH).gameObject;
            _coordsRect = _coordsPanel.GetComponent<RectTransform>();
            _coordsText = UiText.Add(_coordsPanel.transform, 12, 1, 206, 28, string.Empty, 15, UiKit.Cyan, TextAnchor.MiddleLeft);
            _coordsPanel.SetActive(false);
        }

        private void RefreshCoordinates()
        {
            if (_coordsPanel == null)
            {
                return;
            }

            bool show = Settings != null && Settings.ShowCoordinates && Game != null && Game.Environment != null
                        && !Game.SpaceViewActive && !Game.InSpace && !Game.OnFootInSpace && !Game.Aboard
                        && string.IsNullOrEmpty(Game.CurrentStationId);
            if (_coordsPanel.activeSelf != show)
            {
                _coordsPanel.SetActive(show);
            }

            float top = VitalsBottomY + CoordsGap; // the vitals panel grows and shrinks with its rows
            if (!Mathf.Approximately(_coordsRect.anchoredPosition.y, -top))
            {
                _coordsRect.anchoredPosition = new Vector2(_coordsRect.anchoredPosition.x, -top);
            }

            _toastBasePos = new Vector2(_toastBasePos.x, -(show ? Mathf.Max(ToastBaseY, top + CoordsH + CoordsGap) : ToastBaseY));
            if (!show)
            {
                return;
            }

            var p = Game.PlayerPosition;
            var (x, y, z) = CoordinateText.Canonical(p.x, p.y, p.z, Game.Circumference);
            var key = new Vector3Int(x, y, z);
            if (key == _coordsShown)
            {
                return; // changes when the player crosses a block, refreshed every frame
            }

            _coordsShown = key;
            _coordsText.text = CoordinateText.Format(p.x, p.y, p.z, Game.Circumference);
        }
    }
}
