// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3131: own names (#2952) given while flying — on the flight chart or in the travel screen — show at once. The
    /// landables' labels (land prompt, target lock, chart) were fixed when the scene was built; every star map the
    /// server sends after a rename refreshes them here. Clearing an own name brings the generated name back.
    /// </summary>
    public sealed partial class SpaceView
    {
        private void OnStarMapNames(StarMapData map) => RefreshLandableNames(map);

        /// <summary>Re-reads the shown name of every landable from <paramref name="map"/>; the home body (empty id) is the
        /// map's active location. Positions, radii and ids stay as built.</summary>
        public void RefreshLandableNames(StarMapData map)
        {
            if (map?.Systems == null || _landables.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _landables.Count; i++)
            {
                var l = _landables[i];
                var body = FindBody(map, string.IsNullOrEmpty(l.Id) ? map.ActiveLocationId : l.Id);
                if (body == null || string.IsNullOrEmpty(body.Shown()))
                {
                    continue;
                }

                string name = body.Shown();
                if (name != l.Name)
                {
                    _landables[i] = (l.Id, name, l.Pos, l.Radius);
                }
            }
        }

        private static NetBody FindBody(StarMapData map, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                foreach (var b in sys.Bodies)
                {
                    if (b.Id == id)
                    {
                        return b;
                    }
                }
            }

            return null;
        }
    }
}
