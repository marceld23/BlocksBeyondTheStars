// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The small system chart in the travel screen's detail pane (#2745): the selected system's star and its bodies as
    /// coloured dots over a faint projection disc — a static snapshot of the REAL system, laid out like the flight system
    /// chart (<see cref="SpaceMap"/>): every body at its real position, the chart centred on the star, an orbit ring
    /// through every planet and asteroid that circles the star, a belt as one band, the same orientation. Moons (and a
    /// station hanging over a planet) sit just outside their planet's dot in the direction they really lie
    /// (<see cref="SystemOrreryLayout"/>). It used to invent its orbits from the list index and keep the dots circling at
    /// made-up speeds. Drawn entirely with uGUI (no camera / RenderTexture); rebuilt when the shown system changes.
    /// </summary>
    public sealed class SystemMapWidget : MonoBehaviour
    {
        private static Sprite _disc; // shared soft-edged disc used for the star + every body dot

        private const float RimMargin = 10f;      // the projection disc's soft rim stays clear of the dots
        private const float HaloPad = 7f;         // a marked body's halo reaches this far beyond its dot
        private const float OrbitAlpha = 0.34f;   // the flight chart's orbit-path alpha
        private const float OrbitThickness = 1.5f;
        private const float BeltPad = 4f;         // chart units a belt band extends past its outermost member
        private static readonly Color BeltCol = new Color(0.78f, 0.72f, 0.6f, 0.12f);

        private RectTransform _rt;

        /// <summary>Creates the widget under <paramref name="parent"/> at the given rect (top-left coords). The chart is
        /// round: a square rect shows the whole projection disc.</summary>
        public static SystemMapWidget Create(Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject("SystemMap", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, w, h);

            // Faint projection disc behind the chart.
            var bg = go.AddComponent<Image>();
            bg.sprite = Disc();
            bg.color = new Color(0.10f, 0.20f, 0.34f, 0.30f);

            var widget = go.AddComponent<SystemMapWidget>();
            widget._rt = go.GetComponent<RectTransform>();
            return widget;
        }

        /// <summary>(Re)builds the chart for a system's bodies; <paramref name="activeBodyId"/> is highlighted
        /// (the body the player is on) and <paramref name="selectedBodyId"/> is ringed (the menu selection).
        /// <para>
        /// <paramref name="markerColorOf"/> supplies the player's own coloured mark for a body id (-1 for
        /// unmarked): a marked body gets a bright halo in that colour, so several planets can be told apart at a
        /// glance — the thing a player asked for ("Planeten im Weltraum markieren, mit verschiedenen Farben").
        /// </para></summary>
        public void Show(IReadOnlyList<NetBody> bodies, string activeBodyId, string selectedBodyId,
                         System.Func<string, int> markerColorOf = null, System.Func<string, string> weatherGlyphOf = null)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            var size = _rt.rect.size;
            var centre = new Vector2(size.x * 0.5f, -size.y * 0.5f); // top-left-anchored content → its centre
            float chartHalf = Mathf.Max(20f, Mathf.Min(size.x, size.y) * 0.5f - RimMargin);
            var layout = SystemOrreryLayout.Layout(bodies, chartHalf, HaloPad,
                b => DotSize(b, activeBodyId, selectedBodyId) * 0.5f);

            // The star at the true centre with a soft corona — drawn first, so the orbit rings read on top of its glow.
            AddDot(centre, 34f, new Color(1f, 0.86f, 0.5f, 0.18f));
            AddDot(centre, 18f, new Color(1f, 0.9f, 0.62f));

            // Belts (#683): one translucent band each instead of a stack of rings. UiOrbitRing is centre-anchored, so
            // Vector2.zero is the chart's centre — the same point as `centre` for the top-left-anchored dots.
            foreach (var belt in layout.Belts)
            {
                float outer = belt.Outer + BeltPad;
                UiOrbitRing.Create(transform, Vector2.zero, new Vector2(outer * 2f, outer * 2f), BeltCol,
                    belt.Outer - belt.Inner + (BeltPad * 2f));
            }

            // Orbit paths: one ring per body circling the star, through its dot.
            foreach (var m in layout.Markers)
            {
                if (m.RingRadius > 0f)
                {
                    var b = bodies[m.Index];
                    var ringCol = PlanetColor(b.PlanetType, b.Kind);
                    ringCol.a = OrbitAlpha;
                    UiOrbitRing.Create(transform, Vector2.zero, new Vector2(m.RingRadius * 2f, m.RingRadius * 2f), ringCol, OrbitThickness);
                }
            }

            // The bodies: the ones at their own position first, then the moons and stations beside them on top.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var m in layout.Markers)
                {
                    if (m.IsSatellite == (pass == 0))
                    {
                        continue;
                    }

                    DrawBody(bodies[m.Index], centre + new Vector2(m.X, m.Y), m.DotRadius * 2f, activeBodyId, selectedBodyId,
                        markerColorOf, weatherGlyphOf);
                }
            }
        }

        /// <summary>The dot diameter of a body: the body you are on largest, the selected one a little larger, a gas giant
        /// half again as big (#2437).</summary>
        private static float DotSize(NetBody body, string activeBodyId, string selectedBodyId)
            => (body.Id == activeBodyId ? 18f : body.Id == selectedBodyId ? 16f : 12f) * DotScale(body.PlanetType);

        private void DrawBody(NetBody body, Vector2 pos, float dotSize, string activeBodyId, string selectedBodyId,
            System.Func<string, int> markerColorOf, System.Func<string, string> weatherGlyphOf)
        {
            bool active = body.Id == activeBodyId;
            var col = PlanetColor(body.PlanetType, body.Kind);
            if (active)
            {
                col = Color.Lerp(col, Color.white, 0.4f); // the body you're on glows brighter
            }

            // A marked body gets a halo in the player's chosen colour, added FIRST so it sits behind the dot and reads as a
            // ring around it.
            int marker = markerColorOf?.Invoke(body.Id) ?? -1;
            if (marker >= 0 && marker < PlanetMarkerPalette.Count)
            {
                var halo = PlanetMarkerPalette.Colors[marker];
                halo.a = 0.75f;
                AddDot(pos, dotSize + 12f, halo);
            }

            var dot = AddDot(pos, dotSize, col);

            // The menu selection wears a thin white ring (centre-anchored like the orbit rings: offset from the centre).
            if (!string.IsNullOrEmpty(selectedBodyId) && body.Id == selectedBodyId)
            {
                var centre = new Vector2(_rt.rect.size.x * 0.5f, -_rt.rect.size.y * 0.5f);
                UiOrbitRing.Create(transform, pos - centre, new Vector2(dotSize + 7f, dotSize + 7f), new Color(1f, 1f, 1f, 0.85f), 1.5f);
            }

            // #2178: the body's live weather rides beside its dot (only for the system you are in).
            string glyph = weatherGlyphOf?.Invoke(body.Id) ?? string.Empty;
            if (glyph.Length > 0)
            {
                var t = UiKit.AddText(dot, dotSize + 2f, dotSize * 0.5f - 9f, 30f, 18f, glyph, 13, Color.white, TextAnchor.MiddleLeft);
                t.raycastTarget = false;
                t.supportRichText = true;
            }
        }

        private RectTransform AddDot(Vector2 anchoredPos, float size, Color c)
        {
            var go = new GameObject("Dot", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.sprite = Disc();
            img.color = c;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>#2437: the gas giant's chart colour — the amber of its banded clouds, unlike any landable world's dot, so
        /// Justus's "no gas planet anywhere" is answered by a glance at the system chart, visited or not.</summary>
        internal static readonly Color GasGiantColor = new Color(0.96f, 0.72f, 0.42f);

        /// <summary>#2437: a gas giant draws half again as big as the other dots — it IS the biggest thing in its system.</summary>
        internal static float DotScale(string type) => type == "gas_giant" ? 1.5f : 1f;

        /// <summary>A planet-type → dot colour mapping (a few known biomes, else a stable hash hue); stations
        /// and asteroid fields get neutral greys. Shared with the flight system chart (#597).</summary>
        internal static Color PlanetColor(string type, string kind)
        {
            if (string.IsNullOrEmpty(type))
            {
                return kind == "AsteroidField" ? new Color(0.55f, 0.5f, 0.45f)
                    : kind == "Wreck" ? new Color(0.85f, 0.65f, 0.35f) // scorched amber, like its radar/chart marker (#1664)
                    : new Color(0.6f, 0.7f, 0.8f);
            }

            string t = type.ToLowerInvariant();
            if (t == "gas_giant") return GasGiantColor; // #2437: before the "gas" rule below, which would paint it toxic green
            if (t.Contains("lava") || t.Contains("volcan")) return new Color(0.9f, 0.4f, 0.2f);
            if (t.Contains("ice") || t.Contains("frost") || t.Contains("snow")) return new Color(0.7f, 0.88f, 1f);
            if (t.Contains("ocean") || t.Contains("water")) return new Color(0.3f, 0.55f, 0.95f);
            if (t.Contains("jungle") || t.Contains("forest") || t.Contains("lush")) return new Color(0.35f, 0.75f, 0.4f);
            if (t.Contains("desert") || t.Contains("sand") || t.Contains("dune")) return new Color(0.85f, 0.74f, 0.45f);
            if (t.Contains("toxic") || t.Contains("acid") || t.Contains("gas")) return new Color(0.6f, 0.85f, 0.3f);
            if (t.Contains("crystal")) return new Color(0.7f, 0.6f, 0.95f);
            if (t.Contains("rock") || t.Contains("barren") || t.Contains("moon")) return new Color(0.6f, 0.6f, 0.62f);

            int h = 0;
            foreach (char ch in type)
            {
                h = h * 31 + ch;
            }

            var rng = new System.Random(h);
            return new Color(0.4f + 0.5f * (float)rng.NextDouble(), 0.4f + 0.5f * (float)rng.NextDouble(), 0.4f + 0.5f * (float)rng.NextDouble());
        }

        /// <summary>A cached soft-edged white disc sprite (radial alpha), tinted per use via Image.color.</summary>
        private static Sprite Disc()
        {
            if (_disc != null)
            {
                return _disc;
            }

            const int s = 48;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            float c = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.78f, 1f, d)); // solid core, soft rim
                    px[y * s + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            _disc = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            return _disc;
        }
    }
}
