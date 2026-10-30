// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using TMPro;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The opt-in coordinate line (#2993): with the setting <c>ShowCoordinates</c> on (default off), one small line under the
    /// clock and the playtime says where the player stands in the world's own block coordinates — <c>X 812 · Y 71 · Z -1904</c>,
    /// the numbers the Quantum Teleporter takes. Hidden in space, on a spacewalk, aboard a ship or a station: those scenes
    /// share their interior coordinates and a number there would mean nothing.
    /// </summary>
    public sealed partial class HudUi
    {
        private GameObject _coordsPanel;
        private TMP_Text _coordsText;
        private Vector3Int _coordsShown = new Vector3Int(int.MinValue, 0, 0);

        private void BuildCoordinates(Transform root)
        {
            _coordsPanel = Panel(root, W - 210f, 244, 200, 32).gameObject;
            _coordsText = UiText.Add(_coordsPanel.transform, 10, 2, 184, 28, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft);
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
