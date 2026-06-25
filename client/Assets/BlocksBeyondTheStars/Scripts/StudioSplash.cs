// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Developer-studio splash — shown for ~5 s right after the mandatory "Made with Unity" screen and
    /// before the game's own BLOCKS BEYOND THE STARS title splash. A dark stage with an assembling block-cluster emblem
    /// inside a sweeping orbit ring (a little rocket circling it + twinkling stars), the gradient "JuMaVe
    /// Games" wordmark (Ju=cyan · Ma=white · Ve=orange) and the slogan "Built from imagination.". A
    /// whoosh→tada sting lands on the reveal flash. Code-built uGUI on its own overlay canvas; driven from
    /// AppShell.Update like <see cref="SplashScreen"/>. Skippable after a moment.
    /// </summary>
    public sealed class StudioSplash
    {
        private const float Duration = 5f;

        private readonly AppShell _shell;
        private float _elapsed;
        private bool _stingPlayed;

        private Canvas _canvas;
        private Sprite _glow;            // soft radial dot (glow, stars, rocket)
        private RectTransform _emblem;   // holds the cubes + ring + rocket (rotates/breathes)
        private RectTransform _ring;     // the orbit ellipse
        private RectTransform _rocket;   // rides the ring
        private CanvasGroup _wordGroup;  // JuMaVe Games
        private RectTransform _word;
        private CanvasGroup _sloganGroup;
        private Image _flash;
        private Image _glowImg;
        private readonly CanvasGroup[] _cubes = new CanvasGroup[5];
        private readonly Image[] _stars = new Image[7];

        public StudioSplash(AppShell shell) => _shell = shell;

        public void Update()
        {
            if (_shell.Phase != ShellPhase.Studio)
            {
                if (_canvas != null)
                {
                    Object.Destroy(_canvas.gameObject);
                    _canvas = null;
                }

                return;
            }

            EnsureBuilt();
            _elapsed += Time.deltaTime;
            Animate(_elapsed);

            if (!_stingPlayed && _elapsed >= 0.25f)
            {
                _stingPlayed = true;
                _shell.PlayStudioSting();
            }

            if (_elapsed >= Duration || (_elapsed > 0.7f && Input.anyKeyDown))
            {
                _shell.GoTo(ShellPhase.Splash);
            }
        }

        private void Animate(float t)
        {
            // Emblem: a gentle continuous spin of the ring + a soft breathe of the whole cluster.
            if (_ring != null)
            {
                _ring.localRotation = Quaternion.Euler(0f, 0f, -t * 42f);
            }

            // Rocket rides the elliptical ring (wider than tall).
            if (_rocket != null)
            {
                float a = t * 2.1f;
                _rocket.anchoredPosition = new Vector2(Mathf.Cos(a) * 150f, Mathf.Sin(a) * 86f);
                _rocket.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
            }

            // Cubes pop in one after another (scale + fade), then settle with a tiny breathe.
            for (int i = 0; i < _cubes.Length; i++)
            {
                if (_cubes[i] == null)
                {
                    continue;
                }

                float k = Mathf.Clamp01((t - 0.15f - i * 0.12f) / 0.45f);
                float ease = 1f - (1f - k) * (1f - k);
                float s = (0.2f + 0.8f * ease) * (1f + 0.03f * Mathf.Sin(t * 2.4f + i));
                ((RectTransform)_cubes[i].transform).localScale = new Vector3(s, s, 1f);
                _cubes[i].alpha = ease;
            }

            // Stars twinkle.
            for (int i = 0; i < _stars.Length; i++)
            {
                if (_stars[i] == null)
                {
                    continue;
                }

                float tw = 0.35f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * (1.6f + i * 0.3f) + i));
                var c = _stars[i].color; c.a = Mathf.Clamp01((t - 0.2f) / 0.6f) * tw; _stars[i].color = c;
            }

            if (_glowImg != null)
            {
                float pulse = 0.18f + 0.10f * (0.5f + 0.5f * Mathf.Sin(t * 1.6f));
                var c = _glowImg.color; c.a = Mathf.Clamp01(t / 0.5f) * pulse; _glowImg.color = c;
            }

            // Wordmark scales + fades in once the cluster is up.
            if (_wordGroup != null)
            {
                float w = Mathf.Clamp01((t - 0.75f) / 0.6f);
                float we = 1f - (1f - w) * (1f - w);
                _wordGroup.alpha = we;
                float ws = 0.86f + 0.14f * we;
                _word.localScale = new Vector3(ws, ws, 1f);
            }

            // Slogan fades in last.
            if (_sloganGroup != null)
            {
                _sloganGroup.alpha = Mathf.Clamp01((t - 1.7f) / 0.7f);
            }

            // Reveal flash on the "tada".
            if (_flash != null)
            {
                float f = t < 0.85f ? 0f : Mathf.Clamp01(1f - (t - 0.85f) / 0.45f);
                var c = _flash.color; c.a = f * 0.5f; _flash.color = c;
            }
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _elapsed = 0f;
            _stingPlayed = false;
            _glow = RadialSprite();
            _canvas = UiKit.CreateCanvas("Studio Splash");
            _canvas.sortingOrder = 60; // above the title splash
            var root = _canvas.transform;

            // Solid dark stage (covers the engine splash hand-off).
            Full(root, "Stage", new Color(0.035f, 0.05f, 0.10f, 1f));

            // Soft glow behind the emblem.
            var glowGo = new GameObject("Glow", typeof(RectTransform));
            glowGo.transform.SetParent(root, false);
            var grt = glowGo.GetComponent<RectTransform>();
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = new Vector2(0f, 110f);
            grt.sizeDelta = new Vector2(520f, 520f);
            _glowImg = glowGo.AddComponent<Image>();
            _glowImg.sprite = _glow;
            _glowImg.color = new Color(0.25f, 0.8f, 1f, 0f);
            _glowImg.raycastTarget = false;

            // Emblem container (centre-upper).
            var emGo = new GameObject("Emblem", typeof(RectTransform));
            emGo.transform.SetParent(root, false);
            _emblem = emGo.GetComponent<RectTransform>();
            _emblem.anchorMin = _emblem.anchorMax = _emblem.pivot = new Vector2(0.5f, 0.5f);
            _emblem.anchoredPosition = new Vector2(0f, 110f);
            _emblem.sizeDelta = new Vector2(360f, 360f);

            // Orbit ring (ellipse: wider than tall) — a separate child so it can spin on its own.
            var ringGo = new GameObject("Ring", typeof(RectTransform));
            ringGo.transform.SetParent(_emblem, false);
            _ring = ringGo.GetComponent<RectTransform>();
            _ring.anchorMin = _ring.anchorMax = _ring.pivot = new Vector2(0.5f, 0.5f);
            _ring.sizeDelta = new Vector2(330f, 190f);
            var ringImg = ringGo.AddComponent<RawImage>(); // RadarCircle is a ring texture (used on RawImages)
            ringImg.texture = UiKit.RadarCircle;
            ringImg.color = new Color(0.3f, 0.95f, 1f, 0.7f);
            ringImg.raycastTarget = false;

            // Stars / sparkles around the cluster.
            var rng = new System.Random(7);
            for (int i = 0; i < _stars.Length; i++)
            {
                float ang = (float)(rng.NextDouble() * 6.2831);
                float rad = 95f + (float)rng.NextDouble() * 95f;
                _stars[i] = Dot(_emblem, new Vector2(Mathf.Cos(ang) * rad * 1.4f, Mathf.Sin(ang) * rad),
                    Vector2.one * (i % 3 == 0 ? 10f : 6f), new Color(0.85f, 0.95f, 1f, 0f), _glow);
            }

            // Block cluster — a little iso-ish stack (top faces brighter), pops in staggered.
            Vector2[] cubePos = { new(-34f, -18f), new(2f, -30f), new(38f, -16f), new(-14f, 8f), new(24f, 12f) };
            Color[] cubeCol = {
                new(0.20f, 0.55f, 0.72f, 0f), new(0.45f, 0.85f, 0.95f, 0f), new(0.22f, 0.58f, 0.75f, 0f),
                new(0.55f, 0.92f, 1f, 0f),    new(0.30f, 0.70f, 0.88f, 0f),
            };
            for (int i = 0; i < _cubes.Length; i++)
            {
                _cubes[i] = Cube(_emblem, cubePos[i], cubeCol[i]);
            }

            // Rocket: a bright dart with a short warm trail, riding the ring.
            var rkGo = new GameObject("Rocket", typeof(RectTransform));
            rkGo.transform.SetParent(_emblem, false);
            _rocket = rkGo.GetComponent<RectTransform>();
            _rocket.anchorMin = _rocket.anchorMax = _rocket.pivot = new Vector2(0.5f, 0.5f);
            _rocket.sizeDelta = new Vector2(10f, 22f);
            Dot(_rocket, Vector2.zero, new Vector2(10f, 22f), new Color(0.95f, 0.98f, 1f, 1f), _glow);
            Dot(_rocket, new Vector2(0f, -16f), new Vector2(7f, 18f), new Color(1f, 0.6f, 0.2f, 0.8f), _glow);

            // Wordmark: Ju (cyan) · Ma (white) · Ve (orange) + GAMES.
            var wordGo = new GameObject("Word", typeof(RectTransform));
            wordGo.transform.SetParent(root, false);
            _word = wordGo.GetComponent<RectTransform>();
            _word.anchorMin = _word.anchorMax = _word.pivot = new Vector2(0.5f, 0.5f);
            _word.anchoredPosition = new Vector2(0f, -120f);
            _word.sizeDelta = new Vector2(900f, 140f);
            _wordGroup = wordGo.AddComponent<CanvasGroup>();
            Part(_word, new Vector2(-118f, 0f), 86, new Color(0.35f, 0.85f, 1f), "Ju");
            Part(_word, new Vector2(0f, 0f), 86, new Color(0.95f, 0.97f, 1f), "Ma");
            Part(_word, new Vector2(118f, 0f), 86, new Color(1f, 0.55f, 0.15f), "Ve");
            Part(_word, new Vector2(0f, -64f), 30, new Color(0.45f, 0.85f, 1f), "G A M E S");

            // Slogan.
            var slGo = new GameObject("Slogan", typeof(RectTransform));
            slGo.transform.SetParent(root, false);
            var slrt = slGo.GetComponent<RectTransform>();
            slrt.anchorMin = slrt.anchorMax = slrt.pivot = new Vector2(0.5f, 0.5f);
            slrt.anchoredPosition = new Vector2(0f, -230f);
            slrt.sizeDelta = new Vector2(900f, 40f);
            _sloganGroup = slGo.AddComponent<CanvasGroup>();
            Part(slrt, Vector2.zero, 24, new Color(0.8f, 0.88f, 0.96f), _shell.L("ui.studio.slogan"));

            // Open-source invite: "Contributors: your name could be here" — fades in with the slogan group.
            Part(slrt, new Vector2(0f, -46f), 18, new Color(0.55f, 0.80f, 1f), _shell.L("ui.studio.contributors"));

            // Full-screen reveal flash (on top).
            _flash = Full(root, "Flash", new Color(1f, 1f, 1f, 0f));
            _flash.transform.SetAsLastSibling();

            Animate(0f);
        }

        private static Image Full(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Image Dot(Transform parent, Vector2 pos, Vector2 size, Color color, Sprite sprite)
        {
            var go = new GameObject("Dot", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A faux-3D block: a body square with a brighter top band, inside a CanvasGroup so the whole
        /// block fades + scales as one when it pops in.</summary>
        private static CanvasGroup Cube(Transform parent, Vector2 pos, Color color)
        {
            var go = new GameObject("Cube", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(34f, 34f);
            var group = go.AddComponent<CanvasGroup>();

            var opaque = new Color(color.r, color.g, color.b, 1f);
            Dot(rt, Vector2.zero, new Vector2(34f, 34f), opaque, UiKit.SolidSprite);                 // body
            Dot(rt, new Vector2(0f, 11f), new Vector2(34f, 12f),                                     // brighter top band
                new Color(Mathf.Min(1f, color.r + 0.25f), Mathf.Min(1f, color.g + 0.18f), Mathf.Min(1f, color.b + 0.12f), 1f), UiKit.SolidSprite);
            return group;
        }

        private static void Part(Transform parent, Vector2 pos, int fontSize, Color color, string text)
        {
            var go = new GameObject("T", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(700f, fontSize + 24f);
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = fontSize;
            t.color = color;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
        }

        /// <summary>A soft round dot (bright core → transparent rim) for the glow, stars and rocket.</summary>
        private static Sprite RadialSprite()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (n - 1) * 0.5f;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 1.8f));
            }

            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
