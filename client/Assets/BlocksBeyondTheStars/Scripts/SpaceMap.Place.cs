// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3131: the flight chart's System tab shows the place details of the body it targets, as the travel screen does —
    /// the generated name in small type under an own name, "Your own name — only you see it" for a planet or moon you have
    /// landed on (#2952; the server checks the visit again) and the body's colour mark. So a planet you are looking at
    /// in flight can be named without leaving the chart. The rows live in their own layer and are rebuilt only when the
    /// target changes, so a refresh never throws away a name being typed.
    /// </summary>
    public sealed partial class SpaceMap
    {
        private const float PlaceTop = 430f; // below the waypoint text in the info panel

        private RectTransform _place;
        private string _placeFor; // the body id the rows were built for

        private void BuildPlaceArea(Transform root)
        {
            var go = new GameObject("Place", typeof(RectTransform));
            go.transform.SetParent(root, false);
            UiKit.Place(go, 0, 0, 1920, 1080);
            _place = go.GetComponent<RectTransform>();
            _placeFor = null;
        }

        /// <summary>The star-map body behind the current waypoint — the launch body for the home waypoint — or null for a
        /// free point, an encounter or nothing.</summary>
        private NetBody WaypointBody()
        {
            string id = Game?.SpaceWaypointId;
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return id == SpaceView.HomeWaypointId ? Game.FindMapBody(Game.StarMap?.ActiveLocationId) : Game.FindMapBody(id);
        }

        /// <summary>Rebuilds the place rows when the target changed (or <paramref name="force"/>, after a mark).</summary>
        private void RefreshPlace(bool force)
        {
            if (_place == null)
            {
                return;
            }

            var body = WaypointBody();
            string forId = body?.Id ?? string.Empty;
            if (!force && forId == _placeFor)
            {
                return;
            }

            _placeFor = forId;
            for (int i = _place.childCount - 1; i >= 0; i--)
            {
                Destroy(_place.GetChild(i).gameObject);
            }

            if (body == null)
            {
                return;
            }

            const float x = 1010f;
            float y = PlaceDetailRows.AddOriginalNameLine(_place, x, PlaceTop, body.IsRenamed() ? body.Name : null, L) + 8f;
            bool here = body.Id == Game.StarMap?.ActiveLocationId;
            if (body.Kind is "Planet" or "Moon" && (here || Game.HasLandedOn(body.Id)))
            {
                y = PlaceDetailRows.AddOwnNameRow(_place, x, y, body.Id, body.CustomName, L, Game.Network);
            }

            PlaceDetailRows.AddMarkRow(_place, x, y, Game.Settings, body.Id, L, () => RefreshPlace(force: true));
        }
    }
}
