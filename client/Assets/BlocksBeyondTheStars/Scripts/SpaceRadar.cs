// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Space radar (M27 polish): a HUD minimap of nearby space entities while flying — colour-coded
    /// (white = neutral asteroids/NPCs, red = hostile drones/UFOs), placed by bearing relative to the
    /// flight camera (forward = up). Shown only in space; reads the authoritative <c>SpaceState</c>.
    /// Modern uGUI build (round face + pooled blips on a DPI-scaled overlay canvas).
    /// </summary>
    public sealed class SpaceRadar : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public SpaceView SpaceView; // source of the landable-body bearings (planets/moons to fly to)

        private const float Radius = 72f;
        private const float DefaultRange = 130f; // world units mapped to the radar edge (no radar module)

        private Canvas _canvas;
        private RectTransform _center;
        private TMPro.TMP_Text _stationLabel;
        private Image _wpBlip;    // the nav waypoint (#597), amber — distinct from every entity colour
        private TMPro.TMP_Text _wpLabel;    // waypoint distance readout under the radar

        // #1516: last-formatted readout state so the label strings are built on change, not every frame.
        // Tracked in rounded flight units; the labels print them as km (#1599, SpaceDistance).
        private int _wpLastMeters = -1;
        private string _readoutName;
        private int _readoutMeters = -1;
        private int _readoutVert;
        private int _readoutKind; // 0 = none, 1 = station, 2 = body
        private readonly List<Image> _blips = new List<Image>();

        private static readonly Color WaypointCol = new Color(1f, 0.85f, 0.3f);

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            // HUD reference (1536×864) like HudUi/SpaceView — the radar was missed by the 2026-06-07
            // "bigger HUD" pass and drew 25 % smaller than the rest of the flight HUD (#482). Everything
            // in here is anchor-relative (top-centre + pixel offsets), so the lower reference just scales
            // it up in place.
            _canvas = UiKit.CreateDiegeticCanvas("Space Radar", UiKit.HudRefW, UiKit.HudRefH); // visor HUD camera when active
            _canvas.sortingOrder = 10; // HUD level
            var root = _canvas.transform;

            // Round radar face, top-centre.
            var faceGo = new GameObject("Face", typeof(RectTransform));
            faceGo.transform.SetParent(root, false);
            var face = faceGo.GetComponent<RectTransform>();
            face.anchorMin = face.anchorMax = new Vector2(0.5f, 1f);
            face.pivot = new Vector2(0.5f, 1f);
            face.sizeDelta = new Vector2((Radius + 8f) * 2f, (Radius + 8f) * 2f);
            face.anchoredPosition = new Vector2(0f, -16f);
            // Holo ring face (crisp at any DPI, slow border sweep) — the bitmap disc when the shader is out.
            if (UiHolo.Available)
            {
                var faceImg = faceGo.AddComponent<Image>();
                faceImg.raycastTarget = false;
                faceImg.color = new Color(0.04f, 0.10f, 0.20f, 1f);
                UiHolo.Apply(faceImg, UiHolo.Style.Ring, Radius + 8f, 2f, 1.2f).FillOpacity = 0.62f;
            }
            else
            {
                var faceRaw = faceGo.AddComponent<RawImage>();
                faceRaw.texture = UiKit.RadarCircle;
                faceRaw.raycastTarget = false;
            }

            // A centred anchor that the ship marker + blips position around (+y = up/forward).
            var centerGo = new GameObject("Center", typeof(RectTransform));
            centerGo.transform.SetParent(face, false);
            _center = centerGo.GetComponent<RectTransform>();
            _center.anchorMin = _center.anchorMax = _center.pivot = new Vector2(0.5f, 0.5f);
            _center.sizeDelta = Vector2.zero;

            Dot(_center, new Vector2(0f, 0f), new Vector2(4f, 4f), UiKit.TextCol);          // ship at centre
            Dot(_center, new Vector2(0f, Radius - 3.5f), new Vector2(2f, 7f), UiKit.TextCol); // forward tick

            // Nearest-station readout under the radar.
            var labelGo = new GameObject("StationLabel", typeof(RectTransform));
            labelGo.transform.SetParent(root, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(280f, 22f);
            lrt.anchoredPosition = new Vector2(0f, -(16f + (Radius + 8f) * 2f + 4f));
            var station = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
            station.font = UiText.Font;
            station.fontSize = 15;
            station.color = new Color(0.4f, 0.85f, 1f);
            station.alignment = TMPro.TextAlignmentOptions.Center;
            station.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            station.overflowMode = TMPro.TextOverflowModes.Overflow;
            station.raycastTarget = false;
            UiText.Style(station, UiText.Look.Outline);
            _stationLabel = station;

            // Nav waypoint (#597): the map_waypoint glyph as a radar blip + a distance line of its own —
            // amber like the surface compass waypoint, so the two systems read as one feature.
            var wpSprite = UiKit.Icon("map_waypoint");
            _wpBlip = Dot(_center, Vector2.zero, new Vector2(15f, 15f), WaypointCol);
            if (wpSprite != null)
            {
                _wpBlip.sprite = wpSprite;
            }

            _wpBlip.gameObject.SetActive(false);

            var wpGo = new GameObject("WaypointLabel", typeof(RectTransform));
            wpGo.transform.SetParent(root, false);
            var wrt = wpGo.GetComponent<RectTransform>();
            wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 1f);
            wrt.pivot = new Vector2(0.5f, 1f);
            wrt.sizeDelta = new Vector2(280f, 20f);
            wrt.anchoredPosition = new Vector2(0f, -(16f + (Radius + 8f) * 2f + 26f));
            var wp = wpGo.AddComponent<TMPro.TextMeshProUGUI>();
            wp.font = UiText.Font;
            wp.fontSize = 14;
            wp.color = WaypointCol;
            wp.alignment = TMPro.TextAlignmentOptions.Center;
            wp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            wp.overflowMode = TMPro.TextOverflowModes.Overflow;
            wp.raycastTarget = false;
            UiText.Style(wp, UiText.Look.Outline);
            _wpLabel = wp;
            _wpLabel.gameObject.SetActive(false);
        }

        private static Image Dot(Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject("Mark", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private void LateUpdate()
        {
            bool show = Game != null && Camera != null && Game.InSpace && Game.Space != null && !Game.MenuOpen;
            EnsureBuilt();
            if (_canvas.enabled != show)
            {
                _canvas.enabled = show;
            }

            if (!show)
            {
                return;
            }

            // The flight camera is parented to the (unrotated) space scene root, so its local
            // position + world right/forward give a stable frame for the entity bearings.
            var camPos = Camera.transform.localPosition;
            var camR = Camera.transform.right;
            var camF = Camera.transform.forward;

            // Radar range comes from the ship's radar module(s) (radar_array widens it).
            float range = Game.ShipCombat != null && Game.ShipCombat.RadarRange > 1f ? Game.ShipCombat.RadarRange : DefaultRange;
            float scale = Radius / range;

            string nearestStation = null;
            float nearestDist = float.MaxValue;
            float nearestUp = 0f; // station height relative to the ship — the radar's 2D disc drops it

            int i = 0;
            foreach (var e in Game.Space.Entities)
            {
                bool station = e.Kind == "SpaceStation";
                var world = new Vector3(e.X, e.Y, e.Z);
                var dir = world - camPos;
                var v = new Vector2(Vector3.Dot(dir, camR), Vector3.Dot(dir, camF)) * scale;
                if (v.magnitude > Radius)
                {
                    // A station stays as a rim direction-marker (it's a fixed navigation point); an asteroid
                    // or enemy beyond radar range is simply not detected yet — don't paint a phantom blip at
                    // the rim where nothing actually is.
                    if (!station)
                    {
                        continue;
                    }

                    v = v.normalized * Radius;
                }
                if (station && dir.magnitude < nearestDist)
                {
                    nearestDist = dir.magnitude;
                    nearestStation = e.Name;
                    nearestUp = world.y - camPos.y;
                }

                var blip = Blip(i++);
                blip.rectTransform.anchoredPosition = v; // +y already maps to up/forward
                blip.rectTransform.sizeDelta = station ? new Vector2(9f, 9f) : new Vector2(6f, 6f);
                blip.color = station ? new Color(0.4f, 0.85f, 1f)
                    : e.Kind == "ResourceDrop" ? new Color(0.5f, 0.9f, 1f)
                    : e.Hostile ? new Color(1f, 0.35f, 0.35f)
                    : new Color(0.9f, 0.95f, 1f);
                blip.gameObject.SetActive(true);
            }

            // Landable planets/moons: a green bearing marker each, clamped to the rim so a far body reads as
            // a direction arrow ("that way"). Helps you navigate the system from the cockpit.
            string nearestBody = null;
            float nearestBodyDist = float.MaxValue;
            if (SpaceView != null)
            {
                foreach (var body in SpaceView.Landables)
                {
                    var dir = body.Pos - camPos;
                    var v = new Vector2(Vector3.Dot(dir, camR), Vector3.Dot(dir, camF)) * scale;
                    bool offEdge = v.magnitude > Radius;
                    if (offEdge)
                    {
                        v = v.normalized * Radius; // pin to the rim → a direction arrow toward the planet
                    }

                    if (dir.magnitude < nearestBodyDist)
                    {
                        nearestBodyDist = dir.magnitude;
                        nearestBody = body.Name;
                    }

                    var blip = Blip(i++);
                    blip.rectTransform.anchoredPosition = v;
                    blip.rectTransform.sizeDelta = new Vector2(10f, 10f);
                    blip.color = new Color(0.45f, 1f, 0.55f); // green = a planet/moon you can land on
                    blip.gameObject.SetActive(true);
                }
            }

            for (; i < _blips.Count; i++)
            {
                if (_blips[i].gameObject.activeSelf)
                {
                    _blips[i].gameObject.SetActive(false);
                }
            }

            // Nav waypoint (#597): amber blip (rim-pinned when out of radar range — it's a navigation
            // point like a station) + its own distance line, so "am I getting closer?" reads at a glance.
            bool haveWp = SpaceView != null && SpaceView.TryResolveSpaceWaypoint(out var wpPos, out _);
            if (_wpBlip.gameObject.activeSelf != haveWp)
            {
                _wpBlip.gameObject.SetActive(haveWp);
                _wpLabel.gameObject.SetActive(haveWp);
            }

            if (haveWp)
            {
                SpaceView.TryResolveSpaceWaypoint(out wpPos, out _);
                var wdir = wpPos - camPos;
                var wv = new Vector2(Vector3.Dot(wdir, camR), Vector3.Dot(wdir, camF)) * scale;
                if (wv.magnitude > Radius)
                {
                    wv = wv.normalized * Radius;
                }

                _wpBlip.rectTransform.anchoredPosition = wv;
                _wpBlip.rectTransform.SetAsLastSibling(); // over the entity/body blips
                int wpMeters = Mathf.RoundToInt(wdir.magnitude);
                if (wpMeters != _wpLastMeters) // #1516: build the label only when the rounded distance moves
                {
                    _wpLastMeters = wpMeters;
                    _wpLabel.text = $"⌖ {Km(wpMeters)}";
                }
            }

            // Readout under the radar: prefer a station name (dockable), else the nearest planet to head for.
            // #1516: formatted only when the name, the rounded distance or the arrow changes (per frame before).
            if (nearestStation != null)
            {
                // The disc is flat — an arrow says "it's above/below you" so a station parked over the
                // flight plane isn't searched for at eye level.
                int vertState = nearestUp > 10f ? 1 : nearestUp < -10f ? -1 : 0;
                int meters = Mathf.RoundToInt(nearestDist);
                if (!ReferenceEquals(nearestStation, _readoutName) || meters != _readoutMeters || vertState != _readoutVert || _readoutKind != 1)
                {
                    _readoutName = nearestStation;
                    _readoutMeters = meters;
                    _readoutVert = vertState;
                    _readoutKind = 1;
                    string vert = vertState > 0 ? " ▲" : vertState < 0 ? " ▼" : string.Empty;
                    _stationLabel.text = $"{nearestStation} · {Km(meters)}{vert}";
                }
            }
            else if (nearestBody != null)
            {
                int meters = Mathf.RoundToInt(nearestBodyDist);
                if (!ReferenceEquals(nearestBody, _readoutName) || meters != _readoutMeters || _readoutKind != 2)
                {
                    _readoutName = nearestBody;
                    _readoutMeters = meters;
                    _readoutKind = 2;
                    _stationLabel.text = $"➜ {nearestBody} · {Km(meters)}";
                }
            }

            _stationLabel.gameObject.SetActive(nearestStation != null || nearestBody != null);
        }

        /// <summary>A flight-scene distance as the instruments print it — km at 10 km per unit (#1599).</summary>
        private string Km(float units)
            => SpaceDistance.Label(units, Game != null && Game.Localizer != null ? Game.Localizer.Get("ui.space.km_fmt") : null);

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private Image Blip(int index)
        {
            while (index >= _blips.Count)
            {
                _blips.Add(Dot(_center, Vector2.zero, new Vector2(6f, 6f), Color.white));
            }

            return _blips[index];
        }
    }
}
