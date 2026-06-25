// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Draws each placed radio beacon's label floating above its block in the world (item 37), so a beacon reads
    /// as a named landmark on the ground — not just a dot on the map. The beacon block itself is a normal voxel
    /// (the chunk mesher renders it); this only adds the hovering name via the shared screen-label layer.
    /// </summary>
    public sealed class BeaconView : MonoBehaviour
    {
        public GameBootstrap Game;

        private void LateUpdate()
        {
            if (Game?.Beacons == null || Game.Beacons.Length == 0 || Game.SpaceViewActive || Game.MenuOpen)
            {
                return;
            }

            var cam = Camera.main;
            var labels = ScreenLabelLayer.Instance;
            if (cam == null || labels == null)
            {
                return;
            }

            var amber = new Color(1f, 0.75f, 0.3f);
            foreach (var b in Game.Beacons)
            {
                string name = string.IsNullOrEmpty(b.Label)
                    ? (Game.Localizer?.Get("ui.beacon.default") ?? "Beacon")
                    : b.Label;
                // SceneX/SceneZ map the canonical spot to the lap nearest the player, so the label sits over the rendered block.
                var pos = new Vector3(Game.SceneX(b.X), b.Y + 1.7f, Game.SceneZ(b.Z));
                // Landmark labels carry further than NPC names: fade between 40 m and 60 m.
                labels.World(cam, pos, name, amber, false, 40f, 60f);
            }
        }
    }
}
