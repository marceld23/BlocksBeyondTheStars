// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The teleporter's places (#2994) and the Quantum Teleporter's free points (#2995). Every list the client already holds
    /// for the planet map names a place: the base list (own and allied bases; a discovered stranger's base is no destination),
    /// the beam pads and radio beacons (own and allied), the map markers (own and the allies' shared ones; never a ping, a
    /// story flag or a wish mark). Each section is sorted nearest first and capped, so a busy world stays readable. The
    /// server finds and checks every place again — these rows only pick one.
    /// </summary>
    public sealed partial class TeleporterUi
    {
        /// <summary>Rows one section shows at most (the nearest ones).</summary>
        private const int MaxRowsPerSection = 8;

        private string _coordX = string.Empty;
        private string _coordZ = string.Empty;
        private UnityEngine.UI.InputField _xField;
        private UnityEngine.UI.InputField _zField;

        /// <summary>True while the player types a coordinate — a roster update then waits, so the field keeps its focus.</summary>
        private bool TypingCoordinates => (_xField != null && _xField.isFocused) || (_zField != null && _zField.isFocused);

        /// <summary>One destination of the list.</summary>
        private sealed class Place
        {
            public string Kind;
            public string Title;
            public string Owner;
            public float X, Y, Z;
            public string Id = string.Empty;
            public float Distance;
        }

        private static readonly string[] PlaceKinds =
        {
            TeleportRules.KindBase, TeleportRules.KindBeam, TeleportRules.KindBeacon, TeleportRules.KindMarker,
        };

        private void AddPlaceRows()
        {
            var places = new List<Place>();
            CollectBases(places);
            CollectBeamsAndBeacons(places);
            CollectMarkers(places);
            foreach (string kind in PlaceKinds)
            {
                var rows = places.Where(p => p.Kind == kind).OrderBy(p => p.Distance).Take(MaxRowsPerSection).ToList();
                if (rows.Count == 0)
                {
                    continue;
                }

                AddHeader(L("ui.tp.section." + kind));
                foreach (var p in rows)
                {
                    var target = p; // capture
                    string sub = L("ui.beam.distance").Replace("{0}", Mathf.RoundToInt(p.Distance).ToString())
                        + (string.IsNullOrEmpty(p.Owner) ? string.Empty : "  ·  " + p.Owner);
                    AddBeamRow(p.Title, sub, () => Game.Network?.SendTeleportToPoint(target.Kind, target.X, target.Y, target.Z, target.Id));
                }
            }
        }

        /// <summary>Own, an ally's or a crew mate's — the server's <c>AreAllied</c> covers both (it checks every jump again).</summary>
        private bool MineOrAllied(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId) || ownerId == Game?.LocalPlayerId)
            {
                return true;
            }

            var allies = Game?.Alliances?.Allies;
            if (allies != null && allies.Any(a => a.PartnerId == ownerId))
            {
                return true;
            }

            var crew = Game?.Crew?.Members;
            return crew != null && crew.Any(m => m.PlayerId == ownerId) && crew.Any(m => m.PlayerId == Game.LocalPlayerId);
        }

        /// <summary>The owner line of a row: empty for the player's own place.</summary>
        private string OwnerLine(string ownerId) => string.IsNullOrEmpty(ownerId) || ownerId == Game?.LocalPlayerId ? string.Empty : ownerId;

        private float PlaceDistance(float wx, float wz)
        {
            float dx = Game.SceneX(wx) - Game.PlayerPosition.x;
            float dz = Game.SceneZ(wz) - Game.PlayerPosition.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        private void CollectBases(List<Place> into)
        {
            foreach (var b in Game?.Bases ?? System.Array.Empty<NetBase>())
            {
                // The server's relation knows crews too; an older server sends 0 for every base, so own/allied is checked here then.
                bool usable = b != null && b.Relation != NetBase.RelationOther
                              && (b.Relation == NetBase.RelationAlly || MineOrAllied(b.OwnerId));
                if (!usable)
                {
                    continue;
                }

                into.Add(new Place
                {
                    Kind = TeleportRules.KindBase,
                    Title = "⌂ " + (string.IsNullOrEmpty(b.Name) ? L("ui.base.default") : b.Name),
                    Owner = OwnerLine(b.OwnerId),
                    X = b.X, Y = b.Y, Z = b.Z,
                    Distance = PlaceDistance(b.X, b.Z),
                });
            }
        }

        private void CollectBeamsAndBeacons(List<Place> into)
        {
            foreach (var b in Game?.Beams ?? System.Array.Empty<NetBeam>())
            {
                if (b != null && MineOrAllied(b.OwnerId)) // own, an ally's or a crew mate's pad (the server checks CanUseBeam)
                {
                    into.Add(new Place
                    {
                        Kind = TeleportRules.KindBeam,
                        Title = "⊕ " + (string.IsNullOrEmpty(b.Name) ? L("ui.beam.default") : b.Name),
                        Owner = OwnerLine(b.OwnerId),
                        X = b.X, Y = b.Y, Z = b.Z,
                        Distance = PlaceDistance(b.X, b.Z),
                    });
                }
            }

            foreach (var b in Game?.Beacons ?? System.Array.Empty<NetBeacon>())
            {
                if (b != null && MineOrAllied(b.OwnerId))
                {
                    into.Add(new Place
                    {
                        Kind = TeleportRules.KindBeacon,
                        Title = "◉ " + (string.IsNullOrEmpty(b.Label) ? L("ui.beacon.default") : b.Label),
                        Owner = OwnerLine(b.OwnerId),
                        X = b.X, Y = b.Y, Z = b.Z,
                        Distance = PlaceDistance(b.X, b.Z),
                    });
                }
            }
        }

        private void CollectMarkers(List<Place> into)
        {
            foreach (var m in Game?.Markers ?? System.Array.Empty<NetMarker>())
            {
                if (m == null || m.Ping || StoryMarkerText.IsStory(m) || WishPlaceText.IsWish(m) || string.IsNullOrEmpty(m.Id))
                {
                    continue; // a ping is a shout, a story flag and a wish mark are VEGA's — none is a destination
                }

                into.Add(new Place
                {
                    Kind = TeleportRules.KindMarker,
                    Title = "◆ " + (string.IsNullOrEmpty(m.Label) ? L("ui.marker.default") : m.Label),
                    Owner = OwnerLine(m.OwnerId),
                    X = m.X, Y = m.Y, Z = m.Z,
                    Id = m.Id,
                    Distance = PlaceDistance(m.X, m.Z),
                });
            }
        }

        /// <summary>#2995: where the player stands, the map waypoint and two fields for typed X/Z — the Quantum Teleporter's
        /// free points, at its own price.</summary>
        private void AddQuantumRows()
        {
            AddHeader(L("ui.tp.quantum_header") + "  ·  " + L("ui.beam.cost")
                .Replace("{0}", ((int)TeleportRules.QuantumEnergyCost).ToString())
                .Replace("{1}", ((int)TeleportRules.QuantumCooldownSeconds).ToString()));
            var here = Game.PlayerPosition;
            AddRowGo(32f, go => UiKit.AddText(go.transform, 12f, 2f, 540f, 28f,
                L("ui.tp.you_are_at").Replace("{0}", CoordinateText.Format(here.x, here.y, here.z, Game.Circumference)),
                15, UiKit.CyanDim, TextAnchor.MiddleLeft));

            if (Game.Waypoint.HasValue)
            {
                var wp = Game.Waypoint.Value;
                var (cx, _, cz) = CoordinateText.Canonical(wp.x, 0, wp.z, Game.Circumference);
                string sub = L("ui.beam.distance").Replace("{0}", Mathf.RoundToInt(PlaceDistance(wp.x, wp.z)).ToString())
                    + "  ·  X " + cx + " · Z " + cz;
                AddBeamRow("⌖ " + L("ui.tp.to_waypoint"), sub, () => Game.Network?.SendTeleportToPoint(TeleportRules.KindWaypoint, wp.x, 0f, wp.z));
            }

            AddRowGo(56f, go =>
            {
                UiKit.AddText(go.transform, 12f, 8f, 28f, 40f, "X", 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                _xField = UiKit.AddInput(go.transform, 40f, 8f, 140f, 40f, _coordX, v => _coordX = v, "812", 9);
                UiKit.AddText(go.transform, 196f, 8f, 28f, 40f, "Z", 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                _zField = UiKit.AddInput(go.transform, 224f, 8f, 140f, 40f, _coordZ, v => _coordZ = v, "-904", 9);
                UiKit.AddButton(go.transform, 404f, 8f, 150f, 40f, L("ui.tp.beam_button"), JumpToTyped);
            });
        }

        private void JumpToTyped()
        {
            if (!CoordinateText.TryParse(_coordX, out float x) || !CoordinateText.TryParse(_coordZ, out float z))
            {
                Game?.ShowMessage(L("ui.tp.coords_invalid"));
                return;
            }

            // The typed numbers are block coordinates: the column's centre, so the server's floor lands on that very block.
            Game.Network?.SendTeleportToPoint(TeleportRules.KindCoords, Mathf.Floor(x) + 0.5f, 0f, Mathf.Floor(z) + 0.5f);
            Close();
        }
    }
}
