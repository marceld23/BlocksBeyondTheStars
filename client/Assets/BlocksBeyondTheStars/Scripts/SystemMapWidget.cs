using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A small animated orrery for the travel screen's detail pane: the selected system's star plus its
    /// bodies as slowly-orbiting coloured dots over a faint projection disc. Drawn entirely with uGUI (no
    /// camera / RenderTexture), so it is cheap and self-contained. Rebuilt when the shown system changes;
    /// it animates continuously while visible.
    /// </summary>
    public sealed class SystemMapWidget : MonoBehaviour
    {
        private static Sprite _disc; // shared soft-edged disc used for the star + every body dot

        private readonly List<RectTransform> _orbiters = new();
        private readonly List<float> _radius = new();
        private readonly List<float> _phase = new();
        private readonly List<float> _speed = new();
        private RectTransform _rt;
        private float _t;

        /// <summary>Creates the widget under <paramref name="parent"/> at the given rect (top-left coords).</summary>
        public static SystemMapWidget Create(Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject("SystemMap", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, w, h);

            // Faint projection disc behind the orrery.
            var bg = go.AddComponent<Image>();
            bg.sprite = Disc();
            bg.color = new Color(0.10f, 0.20f, 0.34f, 0.30f);

            var widget = go.AddComponent<SystemMapWidget>();
            widget._rt = go.GetComponent<RectTransform>();
            return widget;
        }

        /// <summary>(Re)builds the orrery for a system's bodies; <paramref name="activeBodyId"/> is highlighted
        /// (the body the player is on) and <paramref name="selectedBodyId"/> is ringed (the menu selection).</summary>
        public void Show(IReadOnlyList<NetBody> bodies, string activeBodyId, string selectedBodyId)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            _orbiters.Clear();
            _radius.Clear();
            _phase.Clear();
            _speed.Clear();

            var size = _rt.rect.size;
            var centre = new Vector2(size.x * 0.5f, -size.y * 0.5f); // top-left-anchored content → its centre
            float maxR = Mathf.Min(size.x, size.y) * 0.42f;

            AddDot(centre, 30f, new Color(1f, 0.86f, 0.5f)); // the system's star

            int n = Mathf.Max(1, bodies.Count);
            for (int i = 0; i < bodies.Count; i++)
            {
                float r = maxR * (0.32f + 0.68f * (i + 1) / n);
                bool active = bodies[i].Id == activeBodyId;
                bool selected = bodies[i].Id == selectedBodyId;
                float dotSize = active ? 18f : selected ? 16f : 12f;
                var col = PlanetColor(bodies[i].PlanetType, bodies[i].Kind);
                if (active)
                {
                    col = Color.Lerp(col, Color.white, 0.4f); // the body you're on glows brighter
                }

                var dot = AddDot(centre, dotSize, col);
                _orbiters.Add(dot);
                _radius.Add(r);
                _phase.Add(((bodies[i].Id?.GetHashCode() ?? i) & 0x3FF) * 0.00614f); // a stable starting angle per body
                _speed.Add(7f + (i % 3) * 5f); // deg/s, varied per ring so they don't move in lockstep
            }
        }

        private void Update()
        {
            if (_rt == null)
            {
                return;
            }

            _t += Time.unscaledDeltaTime;
            var size = _rt.rect.size;
            var centre = new Vector2(size.x * 0.5f, -size.y * 0.5f);
            for (int i = 0; i < _orbiters.Count; i++)
            {
                if (_orbiters[i] == null)
                {
                    continue;
                }

                float ang = _phase[i] + _t * _speed[i] * Mathf.Deg2Rad;
                _orbiters[i].anchoredPosition = centre + new Vector2(Mathf.Cos(ang) * _radius[i], Mathf.Sin(ang) * _radius[i]);
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

        /// <summary>A planet-type → dot colour mapping (a few known biomes, else a stable hash hue); stations
        /// and asteroid fields get neutral greys.</summary>
        private static Color PlanetColor(string type, string kind)
        {
            if (string.IsNullOrEmpty(type))
            {
                return kind == "AsteroidField" ? new Color(0.55f, 0.5f, 0.45f) : new Color(0.6f, 0.7f, 0.8f);
            }

            string t = type.ToLowerInvariant();
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
