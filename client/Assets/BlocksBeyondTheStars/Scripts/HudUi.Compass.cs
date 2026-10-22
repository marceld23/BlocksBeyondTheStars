// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The round compass of <see cref="HudUi"/>: the heading-up dial with its north marker and ship pointer, the
    /// waypoint, ship, Guardian core, story, fragment, trader, beacon and marker blips with their distance lines, and the
    /// radar scanner's contacts (#2292).
    /// </summary>
    public sealed partial class HudUi
    {
        /// <summary>The net-fragment signal's colour — the planet map's "fragment_signal" marker tint (#1859).</summary>
        private static readonly Color FragmentSignalCol = new Color(0.45f, 0.95f, 1f);

        private TMP_Text _compassDist, _compassWpDist;
        private TMP_Text _compassCoreDist; // Guardian core distance under the waypoint line (#1792)
        private RectTransform _compassCore;
        private int _lastCompassCoreDist = int.MinValue;
        private TMP_Text _compassFragDist; // nearest net-fragment signal (#1859) — shares the core line's row
        private RectTransform _compassFrag;
        private int _lastCompassFragDist = int.MinValue;
        private TMP_Text _compassStoryDist; // VEGA's story help marker (#2570) — the core line's row, before the fragment
        private RectTransform _compassStory;
        private int _lastCompassStoryDist = int.MinValue;
        private RectTransform _compassTrader; // a landed trader ship on this world (#1904) — blip only, no caption row left
        private RectTransform _compassShip, _compassWp, _compassNorth, _compassShipArrow;
        private Transform _compassParent; // parent for pooled beacon blips (item 37)
        private readonly System.Collections.Generic.List<RectTransform> _compassBeacons = new();
        private readonly System.Collections.Generic.List<RectTransform> _compassMarkers = new(); // named markers + pings (#1217)

        // The radar scanner on the compass (#2292): pooled contact blips, the hostiles in range last frame (a new one pings)
        // and the other players' positions, read from the presence list at 5 Hz.
        private const float RadarRange = 48f;
        private static readonly Color RadarHostileCol = new Color(1f, 0.3f, 0.25f);
        private static readonly Color RadarCreatureCol = new Color(0.4f, 0.95f, 0.45f);
        private static readonly Color RadarPlayerCol = new Color(0.35f, 1f, 0.92f);
        private readonly System.Collections.Generic.List<Image> _radarBlips = new();
        private System.Collections.Generic.HashSet<string> _radarHostiles = new(), _radarHostilesNow = new();
        private readonly System.Collections.Generic.List<Vector3> _radarPlayers = new();
        private float _radarPlayersAt, _radarCueAt = -10f;
        private RemotePlayers _remotes;

        private int _lastCompassDist = int.MinValue;
        private int _lastCompassWpDist = int.MinValue;

        private GameObject BuildCompass(Transform root)
        {
            // Compass (round).
            var comp = new GameObject("Compass", typeof(RectTransform));
            comp.transform.SetParent(root, false);
            UiKit.Place(comp, W - 130f, 10, 120, 120);
            if (UiHolo.Available)
            {
                var face = comp.AddComponent<Image>();
                face.raycastTarget = false;
                face.color = new Color(0.04f, 0.10f, 0.20f, 1f);
                UiHolo.Apply(face, UiHolo.Style.Ring, 60f, 2f, 1.2f).FillOpacity = 0.62f;
            }
            else
            {
                var craw = comp.AddComponent<RawImage>();
                craw.texture = UiKit.RadarCircle;
                craw.raycastTarget = false;
            }
            // The dial is heading-up (the blips are rotated by the player's yaw, so the top is the way you look).
            // #1597: a rotating N marker says where north is — the fixed ▲ that sat here read as a north needle
            // and disagreed with the north-up planet map. Created before the blips so they draw over it; it
            // orbits inside the blip radius so it never lands on the captions under the dial.
            var north = UiText.Add(comp.transform, 0, 0, 16, 16, "N", 12, new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _compassNorth = north.rectTransform;
            _compassNorth.anchorMin = _compassNorth.anchorMax = new Vector2(0.5f, 0.5f);
            _compassNorth.pivot = new Vector2(0.5f, 0.5f);
            // #1682: the ship pointer. The blip alone is an 8 px square among the waypoint's and the beacons'
            // squares, and a player who had used the old ▲ to find his ship reported the ship as simply gone.
            // This rides the rim at a CONSTANT radius and points at the ship, so the direction stays readable
            // however far away it is — the blip inside the dial keeps showing how close you are getting.
            var arrow = UiKit.AddImage(comp.transform, 0, 0, CompassArrowSize, CompassArrowSize,
                UiKit.TriangleSprite, new Color(0.3f, 0.8f, 1f));
            arrow.raycastTarget = false;
            _compassShipArrow = arrow.rectTransform;
            _compassShipArrow.anchorMin = _compassShipArrow.anchorMax = new Vector2(0.5f, 0.5f);
            _compassShipArrow.pivot = new Vector2(0.5f, 0.5f);

            // Both distance lines carry their localized name ("Ship 114 m", "Waypoint 138 m") in the blip's
            // colour — #1594: a first-time player saw two coloured dots and two numbers and did not know the
            // blue one was the ship. The planet map has a legend; this is the compass's.
            _compassDist = UiText.Add(comp.transform, 0, 100, 120, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            // Waypoint distance on its own line under the ship distance — before #592 the compass number
            // was the SHIP only and the waypoint's distance existed nowhere outside the map panel.
            _compassWpDist = UiText.Add(comp.transform, 0, 118, 120, 18, string.Empty, 14, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter, FontStyle.Bold);
            // The Guardian core (#1792): a third line + blip in the map marker's colour, only while the finale body
            // publishes the core POI — the one place on that planet worth walking to, so it gets the ship's treatment.
            _compassCoreDist = UiText.Add(comp.transform, 0, 136, 120, 18, string.Empty, 14, WorldMap.GuardianCoreCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            _compassCore = Blip(comp.transform, WorldMap.GuardianCoreCol, 10f);
            // The net-fragment signal (#1859): the story chip says "follow VEGA's signal", so the signal gets the
            // core's treatment — a blip in the map marker's colour plus a distance line. It SHARES the core's
            // caption row (the core wins when both exist): a fourth row would sit on the time-of-day panel below.
            _compassFragDist = UiText.Add(comp.transform, 0, 136, 120, 18, string.Empty, 14, FragmentSignalCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            _compassFrag = Blip(comp.transform, FragmentSignalCol, 10f);
            // VEGA's story help marker (#2570): the goal flag of the map in its violet, plus "Goal 120 m" on the same shared
            // row — after the core, before the fragment (it is the one thing VEGA just pointed the player at).
            _compassStoryDist = UiText.Add(comp.transform, 0, 136, 120, 18, string.Empty, 14, WorldMap.StoryTargetCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            _compassStory = Blip(comp.transform, WorldMap.StoryTargetCol, 14f, "map_mark_flag");
            // A landed trader ship (#1904): its map marker's ship icon in trade gold. Created before the own-ship blip
            // so that one stays on top where the two meet.
            _compassTrader = Blip(comp.transform, WorldMap.TraderShipCol, 14f, "map_ship");
            _compassShip = Blip(comp.transform, new Color(0.3f, 0.8f, 1f), 8f);
            // The waypoint blip is the map_waypoint ICON, not another plain square — at 7 px amber it was
            // nearly indistinguishable from the 6 px amber beacon blips (#592).
            _compassWp = Blip(comp.transform, new Color(1f, 0.85f, 0.3f), 16f, "map_waypoint");
            _compassParent = comp.transform;

            return comp.gameObject;
        }

        // --- compass ---

        /// <summary>A compass blip: a plain square, or a white-ink map icon when <paramref name="icon"/>
        /// is given (the waypoint — a shaped, larger blip so it never reads as just another beacon, #592).</summary>
        private RectTransform Blip(Transform parent, Color color, float size, string icon = null)
        {
            var sprite = icon != null ? UiKit.Icon(icon) : null;
            var img = UiKit.AddImage(parent, 0, 0, size, size, sprite != null ? sprite : UiKit.SolidSprite, color);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        private void RefreshCompass()
        {
            // The round compass is on-foot/EVA navigation (north marker, ship + waypoint + beacon blips). While
            // piloting the ship you ARE the ship and the flight view draws its own radar, so hide it then; on an
            // EVA it stays — the ship blip + distance is how you float your way back to the hull.
            bool piloting = Game.SpaceViewActive && !Game.InEva;
            var compassGo = _compassParent != null ? _compassParent.gameObject : null;
            if (compassGo != null && compassGo.activeSelf == piloting)
            {
                compassGo.SetActive(!piloting);
            }

            if (piloting)
            {
                return;
            }

            const float radius = 44f;
            PlaceBlip(_compassWp, Game.Waypoint.HasValue, Game.Waypoint ?? Vector3.zero, radius, out float wpDist);
            PlaceBlip(_compassShip, Game.ShipPosition.HasValue, Game.ShipPosition ?? Vector3.zero, radius, out float dist);

            // Ship pointer (#1682): same bearing as the blip, but at a fixed rim radius and rotated so the apex
            // aims outward — "the ship is THAT way" stays legible when the blip has crept out to the rim.
            if (_compassShipArrow != null)
            {
                bool haveShip = Game.ShipPosition.HasValue;
                if (_compassShipArrow.gameObject.activeSelf != haveShip)
                {
                    _compassShipArrow.gameObject.SetActive(haveShip);
                }

                if (haveShip)
                {
                    float shipAng = CompassBearing(Game.ShipPosition.Value, out _);
                    _compassShipArrow.anchoredPosition = new Vector2(Mathf.Sin(shipAng) * CompassArrowRadius, Mathf.Cos(shipAng) * CompassArrowRadius);
                    // The sprite points up at zero rotation; Z rotates counter-clockwise, so the apex follows
                    // the bearing when we turn by its negative.
                    _compassShipArrow.localRotation = Quaternion.Euler(0f, 0f, -shipAng * Mathf.Rad2Deg);
                }
            }

            // North marker (#1597): bearing 0° minus the player's yaw — the same rotation PlaceBlip applies, so the N
            // sits at the top while facing north and swings left when the player turns right.
            if (_compassNorth != null)
            {
                const float northRadius = 37f;
                float northAng = -Game.PlayerYaw * Mathf.Deg2Rad;
                _compassNorth.anchoredPosition = new Vector2(Mathf.Sin(northAng) * northRadius, Mathf.Cos(northAng) * northRadius);
            }

            // This runs per frame, so only rebuild the distance strings when the rounded value changed.
            int distNow = Game.ShipPosition.HasValue ? Mathf.RoundToInt(dist) : -1;
            if (distNow != _lastCompassDist)
            {
                _lastCompassDist = distNow;
                _compassDist.text = distNow >= 0 ? $"{CompassLabel("ui.hud.ship", "Ship")} {distNow} m" : string.Empty;
            }

            // Amber (matching the waypoint blip) vs the cyan ship line above — colour is the label,
            // no glyph prefix (the UI font has no guaranteed ✛).
            int wpDistNow = Game.Waypoint.HasValue ? Mathf.RoundToInt(wpDist) : -1;
            if (wpDistNow != _lastCompassWpDist)
            {
                _lastCompassWpDist = wpDistNow;
                _compassWpDist.text = wpDistNow >= 0 ? $"{CompassLabel("ui.map.waypoint", "Waypoint")} {wpDistNow} m" : string.Empty;
            }

            // Guardian core (#1792): the finale body's one aperture. A player dug 50 blocks straight down under
            // the ship looking for it — the chamber is 20 blocks deep, but 2000 blocks away. Blip + distance line
            // in the map marker's colour, like the waypoint's.
            bool haveCore = TryGetGuardianCore(out var corePos);
            PlaceBlip(_compassCore, haveCore, corePos, radius, out float coreDist);
            int coreDistNow = haveCore ? Mathf.RoundToInt(coreDist) : -1;
            if (coreDistNow != _lastCompassCoreDist)
            {
                _lastCompassCoreDist = coreDistNow;
                _compassCoreDist.text = coreDistNow >= 0 ? $"{CompassLabel("poi.guardian_core", "Guardian core")} {coreDistNow} m" : string.Empty;
            }

            // VEGA's story help marker (#2570): the target of the story step the player has been stuck on, up until that
            // step is done or skipped. Its own flag blip (the nearest one, should there be several); its caption takes the
            // shared row unless the core holds it.
            bool haveStory = TryGetStoryMarker(out var storyPos);
            PlaceBlip(_compassStory, haveStory, storyPos, radius, out float storyDist);
            int storyDistNow = haveStory && !haveCore ? Mathf.RoundToInt(storyDist) : -1;
            if (storyDistNow != _lastCompassStoryDist)
            {
                _lastCompassStoryDist = storyDistNow;
                _compassStoryDist.text = storyDistNow >= 0 ? StoryMarkerText.CompassCaption(storyDistNow, CompassL) : string.Empty;
            }

            // Net-fragment signal (#1859): the nearest revealed fragment marker on this world — the story chip
            // says "follow VEGA's signal", this is the signal. The core keeps the row when both are on the map; the story
            // marker's caption (#2570) keeps it from the fragment's (the fragment's blip stays).
            Vector3 fragPos = Vector3.zero; // assigned up front: the out-call is skipped while the core holds the row
            bool haveFrag = !haveCore && TryGetFragmentSignal(out fragPos);
            PlaceBlip(_compassFrag, haveFrag, fragPos, radius, out float fragDist);
            int fragDistNow = haveFrag && storyDistNow < 0 ? Mathf.RoundToInt(fragDist) : -1;
            if (fragDistNow != _lastCompassFragDist)
            {
                _lastCompassFragDist = fragDistNow;
                _compassFragDist.text = fragDistNow >= 0
                    ? string.Format(CompassLabel("ui.hud.compass_fragment", "Fragment {0} m"), fragDistNow)
                    : string.Empty;
            }

            // Landed trader ship (#1904): the server lists it among the planet POIs while it is parked here, so a
            // player whose pad is hundreds of blocks away can walk over before it lifts off.
            bool haveTrader = TryGetLandedTrader(out var traderPos);
            PlaceBlip(_compassTrader, haveTrader, traderPos, radius);

            // Player-placed beacons (item 37): amber blips, pooled since their count varies.
            var beacons = Game.Beacons;
            int bn = beacons?.Length ?? 0;
            for (int i = 0; i < bn; i++)
            {
                if (i >= _compassBeacons.Count)
                {
                    _compassBeacons.Add(Blip(_compassParent, new Color(1f, 0.72f, 0.2f), 6f));
                }

                PlaceBlip(_compassBeacons[i], true, new Vector3(beacons[i].X, beacons[i].Y, beacons[i].Z), radius);
            }

            for (int i = bn; i < _compassBeacons.Count; i++)
            {
                _compassBeacons[i].gameObject.SetActive(false);
            }

            // Named markers + pings (#1217): pooled palette-coloured blips; a ping blinks so it reads as "now".
            var markers = Game.Markers;
            int mn = markers?.Length ?? 0;
            for (int i = 0; i < mn; i++)
            {
                if (i >= _compassMarkers.Count)
                {
                    _compassMarkers.Add(Blip(_compassParent, Color.white, 6f));
                }

                var m = markers[i];
                if (StoryMarkerText.IsStory(m))
                {
                    _compassMarkers[i].gameObject.SetActive(false); // #2570: drawn as its own flag blip (_compassStory)
                    continue;
                }

                var img = _compassMarkers[i].GetComponent<Image>();
                if (img != null)
                {
                    if (m.Ping)
                    {
                        var c = new Color(1f, 0.9f, 0.4f);
                        c.a = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.time * 5f));
                        img.color = c;
                    }
                    else
                    {
                        img.color = BlocksBeyondTheStars.Client.PlanetMarkerPalette.Colors[
                            Mathf.Clamp(m.Color, 0, BlocksBeyondTheStars.Client.PlanetMarkerPalette.Colors.Length - 1)];
                    }
                }

                PlaceBlip(_compassMarkers[i], true, new Vector3(m.X, m.Y, m.Z), radius);
            }

            for (int i = mn; i < _compassMarkers.Count; i++)
            {
                _compassMarkers[i].gameObject.SetActive(false);
            }

            RefreshRadar(radius); // #2292: last, so the contacts draw over the other blips
        }

        /// <summary>The radar scanner (#2292): while it is worn, every creature within <see cref="RadarRange"/> m is a blip on
        /// the compass — hostile ones red (robots and bandits among them), the rest green — and every other player cyan;
        /// a cloaked player stays hidden (the presence list leaves them out). A hostile newly in range pings, at most once
        /// every two seconds. Same bearing and log radius as every other blip; pooled, all hidden without the scanner.</summary>
        private object _radarEquipment; // the Game.Equipment snapshot _radarWorn was read from
        private bool _radarWorn;

        private void RefreshRadar(float radius)
        {
            int used = 0;
            _radarHostilesNow.Clear();
            if (!ReferenceEquals(Game.Equipment, _radarEquipment))
            {
                // Looked up once per equipment snapshot, not per frame (Wears strips the key of every changed piece).
                _radarEquipment = Game.Equipment;
                _radarWorn = Game.Wears("radar_scanner");
            }

            if (_radarWorn)
            {
                var me = Game.PlayerPosition;
                const float rangeSq = RadarRange * RadarRange;
                var creatures = Game.Creatures;
                if (creatures != null)
                {
                    for (int i = 0; i < creatures.Length; i++)
                    {
                        var c = creatures[i];
                        if (c != null && RadarBlip(ref used, new Vector3(c.X, c.Y, c.Z), me, rangeSq, c.Hostile ? RadarHostileCol : RadarCreatureCol, radius)
                            && c.Hostile)
                        {
                            _radarHostilesNow.Add(c.Id);
                        }
                    }
                }

                var enemies = Game.PlanetEnemies;
                if (enemies != null)
                {
                    for (int i = 0; i < enemies.Length; i++)
                    {
                        var e = enemies[i];
                        if (e != null && RadarBlip(ref used, new Vector3(e.X, e.Y, e.Z), me, rangeSq, e.Hostile ? RadarHostileCol : RadarCreatureCol, radius)
                            && e.Hostile)
                        {
                            _radarHostilesNow.Add(e.Id);
                        }
                    }
                }

                // Other players: the presence list at 5 Hz (it allocates an enumerator); the blips still turn every frame.
                if (Time.unscaledTime >= _radarPlayersAt)
                {
                    _radarPlayersAt = Time.unscaledTime + 0.2f;
                    _radarPlayers.Clear();
                    if (_remotes == null)
                    {
                        _remotes = FindAnyObjectByType<RemotePlayers>();
                    }

                    if (_remotes != null)
                    {
                        foreach (var contact in _remotes.Contacts())
                        {
                            _radarPlayers.Add(contact.Scene);
                        }
                    }
                }

                for (int i = 0; i < _radarPlayers.Count; i++)
                {
                    RadarBlip(ref used, _radarPlayers[i], me, rangeSq, RadarPlayerCol, radius);
                }
            }

            for (int i = used; i < _radarBlips.Count; i++)
            {
                if (_radarBlips[i].gameObject.activeSelf)
                {
                    _radarBlips[i].gameObject.SetActive(false);
                }
            }

            bool fresh = false;
            foreach (var id in _radarHostilesNow)
            {
                if (!_radarHostiles.Contains(id))
                {
                    fresh = true;
                    break;
                }
            }

            (_radarHostiles, _radarHostilesNow) = (_radarHostilesNow, _radarHostiles);
            if (fresh && Time.unscaledTime - _radarCueAt >= 2f)
            {
                _radarCueAt = Time.unscaledTime;
                ClientAudio.Instance?.Cue("radar_contact");
            }
        }

        /// <summary>Puts the next pooled radar blip on <paramref name="target"/> (a world or a scene position — both map to
        /// the copy nearest the player) when it lies within range; true when it did.</summary>
        private bool RadarBlip(ref int used, Vector3 target, Vector3 me, float rangeSq, Color color, float radius)
        {
            if ((Game.ScenePos(target.x, target.y, target.z) - me).sqrMagnitude > rangeSq)
            {
                return false;
            }

            if (used >= _radarBlips.Count)
            {
                _radarBlips.Add(Blip(_compassParent, color, 5f).GetComponent<Image>());
            }

            var blip = _radarBlips[used++];
            blip.color = color;
            PlaceBlip(blip.rectTransform, true, target, radius);
            return true;
        }

        private void PlaceBlip(RectTransform blip, bool active, Vector3 target, float radius)
            => PlaceBlip(blip, active, target, radius, out _);

        /// <summary>The caption in front of a compass distance — the same locale keys the planet-map legend
        /// uses (#1594), so both panels call the ship and the waypoint the same thing.</summary>
        /// <summary>The Guardian core's ground position from the planet's POI list (#1792) — present on the finale
        /// body only. At the player's own height, so the flat compass distance is the walk, not the dig.</summary>
        private bool TryGetGuardianCore(out Vector3 pos)
        {
            var pois = Game.PlanetPois;
            if (pois != null)
            {
                for (int i = 0; i < pois.Length; i++)
                {
                    if (pois[i].Type == "guardian_core")
                    {
                        pos = new Vector3(pois[i].X, Game.PlayerPosition.y, pois[i].Z);
                        return true;
                    }
                }
            }

            pos = Vector3.zero;
            return false;
        }

        /// <summary>The landed trader ship's pilot on this world (#1904), from the planet's POI list — at most one trader
        /// lands per body. At the player's own height, like the Guardian core.</summary>
        private bool TryGetLandedTrader(out Vector3 pos)
        {
            var pois = Game.PlanetPois;
            if (pois != null)
            {
                for (int i = 0; i < pois.Length; i++)
                {
                    if (pois[i].Type == WorldMap.TraderShipPoi)
                    {
                        pos = new Vector3(pois[i].X, Game.PlayerPosition.y, pois[i].Z);
                        return true;
                    }
                }
            }

            pos = Vector3.zero;
            return false;
        }

        /// <summary>The nearest revealed net-fragment signal on this world (#1859), from the planet's POI list —
        /// the server puts every unread fragment there while the story objective points at it. Flat distance at
        /// the player's own height, wrap-resolved through the same bearing the blips use.</summary>
        private bool TryGetFragmentSignal(out Vector3 pos)
        {
            pos = Vector3.zero;
            float best = float.MaxValue;
            var pois = Game.PlanetPois;
            if (pois == null)
            {
                return false;
            }

            for (int i = 0; i < pois.Length; i++)
            {
                if (pois[i].Type != "fragment_signal")
                {
                    continue;
                }

                var candidate = new Vector3(pois[i].X, Game.PlayerPosition.y, pois[i].Z);
                CompassBearing(candidate, out float dist);
                if (dist < best)
                {
                    best = dist;
                    pos = candidate;
                }
            }

            return best < float.MaxValue;
        }

        /// <summary>#2570: the nearest of VEGA's story help markers on this world (the server sends the player's own only,
        /// while the story step behind it is open) — wrap-resolved through the same bearing the blips use.</summary>
        private bool TryGetStoryMarker(out Vector3 pos)
        {
            pos = Vector3.zero;
            float best = float.MaxValue;
            var markers = Game.Markers;
            if (markers == null)
            {
                return false;
            }

            for (int i = 0; i < markers.Length; i++)
            {
                if (!StoryMarkerText.IsStory(markers[i]))
                {
                    continue;
                }

                var candidate = new Vector3(markers[i].X, markers[i].Y, markers[i].Z);
                CompassBearing(candidate, out float dist);
                if (dist < best)
                {
                    best = dist;
                    pos = candidate;
                }
            }

            return best < float.MaxValue;
        }

        private string CompassLabel(string key, string fallback)
        {
            var loc = Game?.Localizer;
            return loc != null ? loc.Get(key) : fallback;
        }

        /// <summary>#2570: <see cref="CompassLabel"/> as a one-argument lookup for the Client.Core text helpers (without a
        /// localizer the story caption reads in English).</summary>
        private string CompassL(string key) => CompassLabel(key, key == StoryMarkerText.CompassKey ? "Goal {0} m" : key);

        /// <summary>Size and rim radius of the ship pointer on the 120 px dial (#1682): 50 + half of 14 = 57,
        /// so the whole glyph stays inside the ring border at 60, and clear of the blips, which only reach
        /// their 44 px rim past about 2 km. Created before the distance captions, so those draw over it in the
        /// narrow sector where a ship directly behind you puts the pointer on the same pixels.</summary>
        private const float CompassArrowSize = 14f;
        private const float CompassArrowRadius = 50f;

        /// <summary>
        /// Dial-space bearing to a world target, in radians, plus the flat distance to it.
        /// <para>#1307: <paramref name="target"/> is canonical world space, but the player transform runs
        /// UNBOUNDED as it laps the world — measure against the copy of the target nearest the player (the
        /// SceneX/SceneZ mapping every other world object already uses). Subtracting raw coordinates flipped
        /// the bearing and added a whole circumference to the distance the moment either side sat across a
        /// seam (">3000 m" to a ship a few blocks away, on a 10128-block world).</para>
        /// </summary>
        private float CompassBearing(Vector3 target, out float dist)
        {
            var near = Game.ScenePos(target.x, target.y, target.z);
            float dx = near.x - Game.PlayerPosition.x, dz = near.z - Game.PlayerPosition.z;
            dist = Mathf.Sqrt(dx * dx + dz * dz);
            return (Mathf.Atan2(dx, dz) * Mathf.Rad2Deg - Game.PlayerYaw) * Mathf.Deg2Rad;
        }

        private void PlaceBlip(RectTransform blip, bool active, Vector3 target, float radius, out float dist)
        {
            dist = 0f;
            if (blip == null) return;
            if (!active) { blip.gameObject.SetActive(false); return; }
            float ang = CompassBearing(target, out dist);
            // Log-scaled radius (#592): the old linear dist*1.2 pinned everything past ~37 m to the rim,
            // so approach progress was unreadable. Log keeps direction AND lets "getting closer" show:
            // ~20 px at 40 m, ~34 px at 400 m, rim only past ~2 km.
            float r = 8f + (radius - 8f) * Mathf.Clamp01(Mathf.Log10(1f + dist / 8f) / Mathf.Log10(1f + 2000f / 8f));
            blip.gameObject.SetActive(true);
            blip.anchoredPosition = new Vector2(Mathf.Sin(ang) * r, Mathf.Cos(ang) * r); // +Y up = north
        }
    }
}
