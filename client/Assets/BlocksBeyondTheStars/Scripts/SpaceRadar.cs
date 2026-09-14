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
    /// pilot's heading (forward = up) on a top-down disc; height shows as ▲/▼ on stations and wrecks and in
    /// the readouts (#1880). Shown only in space; reads the authoritative <c>SpaceState</c>.
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
        private int _wpLastVert = int.MinValue;
        private string _readoutName;
        private int _readoutMeters = -1;
        private int _readoutVert = int.MinValue; // rounded height of the readout's target over the pilot (#1880)
        private int _readoutKind; // 0 = none, 1 = station, 2 = body, 3 = wreck
        private readonly List<Image> _blips = new List<Image>();

        // #1880: the disc turns with the pilot's HEADING (the view flattened onto the flight plane). The last good
        // heading is kept so looking straight up or down on an EVA doesn't spin the disc.
        private float _headingX, _headingZ = 1f;

        // #1880: the flat disc drops height, so a navigation point (station / wreck) above or below the pilot gets a
        // small ▲/▼ beside its blip. Pooled parallel to _blips like the rim labels; the state is cached per slot.
        private readonly List<TMPro.TMP_Text> _vertMarks = new List<TMPro.TMP_Text>();
        private readonly List<int> _vertMarkState = new List<int>();

        // #1663: a name beside every RIM-PINNED blip (a body/wreck beyond radar range — the anonymous green
        // arrows that "only the nearest one" got a name for). Pooled parallel to _blips; the source string is
        // cached per label so the truncated text is rebuilt only when the blip changes identity.
        private readonly List<TMPro.TMP_Text> _rimLabels = new List<TMPro.TMP_Text>();
        private readonly List<string> _rimLabelSource = new List<string>();
        private const int RimLabelChars = 12;
        private static readonly Color WreckCol = new Color(1f, 0.75f, 0.35f); // scorched amber — salvage, not a threat

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

            // The flight camera is parented to the (unrotated) space scene root, so its world forward is also its
            // scene-local forward. #1880: the disc is a top-down map turned with the HEADING — that forward
            // flattened onto the flight plane. Projecting onto the tilted chase camera itself (≈17° nose-down)
            // folded height into "ahead/behind": a wreck 229 units overhead sat dead centre and read as behind you.
            var camF = Camera.transform.forward;
            SpaceRadarMath.Heading(camF.x, camF.z, _headingX, _headingZ, out _headingX, out _headingZ);

            // Bearings and heights are measured from the pilot (the ship, or the suit on an EVA) — not from the
            // chase camera 13 units behind and 4.5 above the hull.
            var pilot = SpaceView != null ? SpaceView.PilotPosition : Camera.transform.localPosition;

            // Radar range comes from the ship's radar module(s) (radar_array widens it).
            float range = Game.ShipCombat != null && Game.ShipCombat.RadarRange > 1f ? Game.ShipCombat.RadarRange : DefaultRange;
            float scale = Radius / range;

            string nearestStation = null;
            float nearestDist = float.MaxValue;
            float nearestUp = 0f; // station height relative to the ship — the radar's 2D disc drops it
            string nearestWreck = null; // #1880: the derelict takes the readout when it is nearer than any planet
            float nearestWreckDist = float.MaxValue;
            float nearestWreckUp = 0f;

            int i = 0;
            foreach (var e in Game.Space.Entities)
            {
                bool station = e.Kind == "SpaceStation";
                bool wreck = e.Kind == "Wreck"; // #1664: the system's derelict — a fixed navigation point too
                var world = new Vector3(e.X, e.Y, e.Z);
                var dir = world - pilot;
                var flat = SpaceRadarMath.Project(dir.x, dir.z, _headingX, _headingZ);
                var v = new Vector2(flat.Right, flat.Ahead) * scale;
                bool pinned = false;
                if (v.magnitude > Radius)
                {
                    // A station (or wreck) stays as a rim direction-marker (it's a fixed navigation point); an
                    // asteroid or enemy beyond radar range is simply not detected yet — don't paint a phantom
                    // blip at the rim where nothing actually is.
                    if (!station && !wreck)
                    {
                        continue;
                    }

                    v = v.normalized * Radius;
                    pinned = true;
                }
                if (station && dir.magnitude < nearestDist)
                {
                    nearestDist = dir.magnitude;
                    nearestStation = e.Name;
                    nearestUp = dir.y;
                }
                else if (wreck && dir.magnitude < nearestWreckDist)
                {
                    nearestWreckDist = dir.magnitude;
                    nearestWreck = e.Name;
                    nearestWreckUp = dir.y;
                }

                int slot = i;
                var blip = Blip(i++);
                blip.rectTransform.anchoredPosition = v; // +y already maps to up/forward
                blip.rectTransform.sizeDelta = station ? new Vector2(9f, 9f) : wreck ? new Vector2(8f, 8f) : new Vector2(6f, 6f);
                blip.color = station ? new Color(0.4f, 0.85f, 1f)
                    : wreck ? WreckCol
                    : e.Kind == "ResourceDrop" ? new Color(0.5f, 0.9f, 1f)
                    : e.Hostile ? new Color(1f, 0.35f, 0.35f)
                    : new Color(0.9f, 0.95f, 1f);
                blip.gameObject.SetActive(true);
                SetRimLabel(slot, pinned ? e.Name : null, blip.color, v);
                SetVertMark(slot, station || wreck ? SpaceRadarMath.VerticalState(dir.y) : 0, blip.color, v, pinned);
            }

            // Landable planets/moons: a green bearing marker each, clamped to the rim so a far body reads as
            // a direction arrow ("that way"). Helps you navigate the system from the cockpit.
            string nearestBody = null;
            float nearestBodyDist = float.MaxValue;
            if (SpaceView != null)
            {
                foreach (var body in SpaceView.Landables)
                {
                    var dir = body.Pos - pilot;
                    var flat = SpaceRadarMath.Project(dir.x, dir.z, _headingX, _headingZ);
                    var v = new Vector2(flat.Right, flat.Ahead) * scale;
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

                    int slot = i;
                    var blip = Blip(i++);
                    blip.rectTransform.anchoredPosition = v;
                    blip.rectTransform.sizeDelta = new Vector2(10f, 10f);
                    blip.color = new Color(0.45f, 1f, 0.55f); // green = a planet/moon you can land on
                    blip.gameObject.SetActive(true);
                    SetRimLabel(slot, offEdge ? body.Name : null, blip.color, v); // #1663: which body is "that way"
                    SetVertMark(slot, 0, blip.color, v, offEdge); // a planet is a big ball — no arrow on it
                }
            }

            for (; i < _blips.Count; i++)
            {
                if (_blips[i].gameObject.activeSelf)
                {
                    _blips[i].gameObject.SetActive(false);
                }

                if (i < _rimLabels.Count && _rimLabels[i].gameObject.activeSelf)
                {
                    _rimLabels[i].gameObject.SetActive(false);
                }

                SetVertMark(i, 0, Color.clear, Vector2.zero, false);
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
                var wdir = wpPos - pilot;
                var wflat = SpaceRadarMath.Project(wdir.x, wdir.z, _headingX, _headingZ);
                var wv = new Vector2(wflat.Right, wflat.Ahead) * scale;
                if (wv.magnitude > Radius)
                {
                    wv = wv.normalized * Radius;
                }

                _wpBlip.rectTransform.anchoredPosition = wv;
                _wpBlip.rectTransform.SetAsLastSibling(); // over the entity/body blips
                int wpMeters = Mathf.RoundToInt(wdir.magnitude);
                int wpVert = Mathf.RoundToInt(wdir.y);
                // #1516: build the label only when the rounded distance (or, #1880, the rounded height) moves.
                if (wpMeters != _wpLastMeters || wpVert != _wpLastVert)
                {
                    _wpLastMeters = wpMeters;
                    _wpLastVert = wpVert;
                    _wpLabel.text = "⌖ " + SpaceRadarMath.DistanceWithHeight(wpMeters, wpVert, KmFormat());
                }
            }

            // Readout under the radar: prefer a station name (dockable), then the system's wreck when it is nearer
            // than any planet (#1880 — it is the thing you are flying to, and the only place its height is spelled
            // out), else the nearest planet to head for. The flat disc hides height, so station and wreck carry the
            // climb as "· ▲ 2 290 km". #1516: formatted only when the name, the rounded distance or height changes.
            if (nearestStation != null)
            {
                SetReadout(1, nearestStation, nearestDist, nearestUp, string.Empty);
            }
            else if (nearestWreck != null && nearestWreckDist < nearestBodyDist)
            {
                SetReadout(3, nearestWreck, nearestWreckDist, nearestWreckUp, string.Empty);
            }
            else if (nearestBody != null)
            {
                SetReadout(2, nearestBody, nearestBodyDist, 0f, "➜ ");
            }

            _stationLabel.gameObject.SetActive(nearestStation != null || nearestWreck != null || nearestBody != null);
        }

        /// <summary>Writes the readout line under the radar — "prefix name · distance[ · ▲ height]" — when any of its
        /// displayed parts changed (#1516). A planet passes a zero height: a big ball needs no arrow.</summary>
        private void SetReadout(int kind, string name, float distance, float up, string prefix)
        {
            int meters = Mathf.RoundToInt(distance);
            int vert = Mathf.RoundToInt(up);
            if (ReferenceEquals(name, _readoutName) && meters == _readoutMeters && vert == _readoutVert && kind == _readoutKind)
            {
                return;
            }

            _readoutName = name;
            _readoutMeters = meters;
            _readoutVert = vert;
            _readoutKind = kind;
            _stationLabel.text = $"{prefix}{name} · {SpaceRadarMath.DistanceWithHeight(meters, vert, KmFormat())}";
        }

        /// <summary>The localized flight-distance format (<c>ui.space.km_fmt</c>, km at 10 km per unit — #1599), or
        /// null for the default.</summary>
        private string KmFormat() => Game != null && Game.Localizer != null ? Game.Localizer.Get("ui.space.km_fmt") : null;

        /// <summary>#1880: shows (or hides, <paramref name="state"/> 0) the ▲/▼ height mark of blip <paramref name="index"/>.
        /// An in-range blip carries it just to its right; a rim-pinned one just OUTSIDE the ring, so it never collides
        /// with the rim name label that hangs inward.</summary>
        private void SetVertMark(int index, int state, Color color, Vector2 blipPos, bool pinned)
        {
            if (state == 0)
            {
                if (index < _vertMarks.Count && _vertMarks[index].gameObject.activeSelf)
                {
                    _vertMarks[index].gameObject.SetActive(false);
                }

                return;
            }

            while (index >= _vertMarks.Count)
            {
                _vertMarks.Add(MakeVertMark());
                _vertMarkState.Add(0);
            }

            var mark = _vertMarks[index];
            var outward = blipPos.sqrMagnitude > 0.001f ? blipPos.normalized : Vector2.up;
            mark.rectTransform.anchoredPosition = pinned ? blipPos + outward * 10f : blipPos + new Vector2(9f, 0f);
            if (_vertMarkState[index] != state)
            {
                _vertMarkState[index] = state;
                mark.text = SpaceRadarMath.Glyph(state);
            }

            if (mark.color != color)
            {
                mark.color = color;
            }

            if (!mark.gameObject.activeSelf)
            {
                mark.gameObject.SetActive(true);
            }
        }

        private TMPro.TMP_Text MakeVertMark()
        {
            var go = new GameObject("BlipHeight", typeof(RectTransform));
            go.transform.SetParent(_center, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(12f, 12f);
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            t.font = UiText.Font;
            t.fontSize = 10;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            t.overflowMode = TMPro.TextOverflowModes.Overflow;
            t.raycastTarget = false;
            UiText.Style(t, UiText.Look.Outline);
            t.gameObject.SetActive(false);
            return t;
        }

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

        /// <summary>#1663: shows (or hides, <paramref name="name"/> null) the name label of blip
        /// <paramref name="index"/>. The label sits just inside the rim on the blip's centre-facing side, in the
        /// blip's colour, truncated to <see cref="RimLabelChars"/> so a long coined name never crosses the face.
        /// Only rim-pinned blips get one — an in-range body is visible on screen, and a named blip for every
        /// asteroid would clutter the HUD.</summary>
        private void SetRimLabel(int index, string name, Color color, Vector2 blipPos)
        {
            if (name == null)
            {
                if (index < _rimLabels.Count && _rimLabels[index].gameObject.activeSelf)
                {
                    _rimLabels[index].gameObject.SetActive(false);
                }

                return;
            }

            while (index >= _rimLabels.Count)
            {
                _rimLabels.Add(MakeRimLabel());
                _rimLabelSource.Add(null);
            }

            var label = _rimLabels[index];
            // Pivot on the side facing the blip, so the text hangs inward from the rim (below a blip at the top,
            // to the left of one on the right) and stays on the face.
            var inward = blipPos.sqrMagnitude > 0.001f ? -blipPos.normalized : Vector2.down;
            label.rectTransform.pivot = new Vector2(0.5f - inward.x * 0.5f, 0.5f - inward.y * 0.5f);
            label.rectTransform.anchoredPosition = blipPos + inward * 7f;
            if (!ReferenceEquals(_rimLabelSource[index], name))
            {
                _rimLabelSource[index] = name;
                label.text = name.Length <= RimLabelChars ? name : name.Substring(0, RimLabelChars - 1) + "…";
            }

            if (label.color != color)
            {
                label.color = color;
            }

            if (!label.gameObject.activeSelf)
            {
                label.gameObject.SetActive(true);
            }
        }

        private TMPro.TMP_Text MakeRimLabel()
        {
            var go = new GameObject("BlipLabel", typeof(RectTransform));
            go.transform.SetParent(_center, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(76f, 12f);
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            t.font = UiText.Font;
            t.fontSize = 9;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            t.overflowMode = TMPro.TextOverflowModes.Overflow;
            t.raycastTarget = false;
            UiText.Style(t, UiText.Look.Outline);
            t.gameObject.SetActive(false);
            return t;
        }
    }
}
