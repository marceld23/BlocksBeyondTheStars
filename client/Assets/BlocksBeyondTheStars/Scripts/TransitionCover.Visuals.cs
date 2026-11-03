// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The cover's three looks (#3140–#3142): the cloud layer, the glare and the hangar doors, built once from
    /// code-drawn textures.</summary>
    public sealed partial class TransitionCover
    {
        private static Texture2D _puffTex, _shadeTex, _glowTex, _streakTex, _plateTex, _hazardTex;

        private RectTransform _doorTop, _doorBottom;
        private RawImage _hazardTop, _hazardBottom;
        private Image _stripTop, _stripBottom, _seam;

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("Transition Cover");
            _canvas.sortingOrder = 74; // above the HUD, menus and the warp (70); under the loading veil (75)
            _root = (RectTransform)_canvas.transform;
            _wash = Stretch<Image>(_root, "Wash");
            _wash.sprite = UiKit.SolidSprite;
            _shade = Stretch<RawImage>(_root, "Shade");
            _shade.texture = ShadeTexture();
            for (int i = 0; i < PuffCount; i++)
            {
                var img = Child<RawImage>(_root, "Puff");
                img.texture = PuffTexture();
                _puffImg[i] = img;
                _puffs[i] = img.rectTransform;
            }

            _glareCore = Child<RawImage>(_root, "GlareCore");
            _glareCore.texture = GlowTexture();
            _glareStreak = Child<RawImage>(_root, "GlareStreak");
            _glareStreak.texture = StreakTexture();
            _glareStreak2 = Child<RawImage>(_root, "GlareStreak2");
            _glareStreak2.texture = StreakTexture();
            BuildDoors();
            _caption = Caption("Caption", 44, FontStyle.Bold, -180f);
            _captionSub = Caption("CaptionSub", 24, FontStyle.Normal, -232f);
            _hint = UiKit.AddText(_root, 0f, 0f, 700f, 36f, string.Empty, 20, Color.clear, TextAnchor.LowerRight);
            _hint.name = "SkipHint";
            _hint.raycastTarget = false;
            var hr = _hint.rectTransform;
            hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(1f, 0f);
            hr.anchoredPosition = new Vector2(-36f, 30f);
            BuildWind();
            _canvas.enabled = false;
        }

        private void BuildDoors()
        {
            _doorTop = Door("DoorTop", top: true, out _hazardTop, out _stripTop);
            _doorBottom = Door("DoorBottom", top: false, out _hazardBottom, out _stripBottom);
            _seam = Child<Image>(_root, "Seam");
            _seam.sprite = UiKit.SolidSprite;
            var rt = _seam.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(0f, 6f);
        }

        private RectTransform Door(string name, bool top, out RawImage hazard, out Image strip)
        {
            var plate = Child<RawImage>(_root, name);
            plate.texture = PlateTexture();
            var rt = plate.rectTransform;
            rt.anchorMin = new Vector2(0f, top ? 0.5f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            hazard = Child<RawImage>(rt, "Hazard");
            hazard.texture = HazardTexture();
            var h = hazard.rectTransform;
            h.anchorMin = new Vector2(0f, top ? 0f : 1f);
            h.anchorMax = new Vector2(1f, top ? 0f : 1f);
            h.pivot = new Vector2(0.5f, top ? 0f : 1f);
            h.sizeDelta = new Vector2(0f, 44f);

            strip = Child<Image>(rt, "Strip");
            strip.sprite = UiKit.SolidSprite;
            var s = strip.rectTransform;
            s.anchorMin = new Vector2(0.08f, top ? 0f : 1f);
            s.anchorMax = new Vector2(0.92f, top ? 0f : 1f);
            s.pivot = new Vector2(0.5f, top ? 0f : 1f);
            s.anchoredPosition = new Vector2(0f, top ? 52f : -52f);
            s.sizeDelta = new Vector2(0f, 5f);
            return rt;
        }

        private Text Caption(string name, int size, FontStyle style, float y)
        {
            var text = UiKit.AddText(_root, 0f, 0f, 1400f, size + 16f, string.Empty, size, Color.clear, TextAnchor.MiddleCenter, style);
            text.name = name;
            text.raycastTarget = false;
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            return text;
        }

        private void BuildWind()
        {
            var clip = ClientAudio.Instance?.Clip("cloud_rush") ?? Resources.Load<AudioClip>("audio/cloud_rush");
            if (clip == null)
            {
                return;
            }

            _wind = gameObject.AddComponent<AudioSource>();
            _wind.clip = clip;
            _wind.loop = true;
            _wind.spatialBlend = 0f;
            _wind.playOnAwake = false;
            _wind.volume = 0f;
        }

        // ---- per-frame looks --------------------------------------------------------------------------------------

        private void ScatterPuffs()
        {
            for (int i = 0; i < PuffCount; i++)
            {
                _puffDepth[i] = Random.value;
                _puffShade[i] = Random.Range(0.86f, 1f);
                _puffPos[i] = new Vector2(Random.Range(-0.15f, 1.15f), Random.Range(-0.3f, 1.3f));
            }
        }

        private void DrawClouds(float dt)
        {
            SetDoors(0f);
            SetGlare(0f, 0f);
            float body = Smooth(0.35f, 1f, _amount);   // the solid cloud interior comes in last and goes first
            float edge = Smooth(0f, 0.6f, _amount);    // the leading puffs
            var wash = Color.Lerp(_tint2, _tint, 0.65f);
            _wash.color = new Color(wash.r, wash.g, wash.b, body);
            _shade.color = new Color(_tint2.r * 0.55f, _tint2.g * 0.6f, _tint2.b * 0.7f, body * 0.45f);
            var size = _root.rect.size;
            float speed = 0.22f + 0.5f * (1f - Mathf.Abs(_amount * 2f - 1f)); // fastest while punching in or out
            for (int i = 0; i < PuffCount; i++)
            {
                float depth = _puffDepth[i];
                var p = _puffPos[i];
                p.y += _drift * dt * speed * (0.5f + depth);
                p.x += (p.x - 0.5f) * dt * 0.25f * (0.5f + depth); // the layer opens around the camera flying through
                if (p.y > 1.35f || p.y < -0.35f || p.x < -0.4f || p.x > 1.4f)
                {
                    p = new Vector2(Random.Range(0.1f, 0.9f), _drift > 0f ? -0.3f : 1.3f);
                }

                _puffPos[i] = p;
                float w = Mathf.Lerp(520f, 1250f, depth) * (0.85f + 0.3f * Mathf.Sin(_time * 0.3f + i));
                var rt = _puffs[i];
                rt.anchoredPosition = new Vector2((p.x - 0.5f) * size.x, (p.y - 0.5f) * size.y);
                rt.sizeDelta = new Vector2(w, w * 0.62f);
                float s = _puffShade[i];
                _puffImg[i].color = new Color(_tint.r * s, _tint.g * s, _tint.b * s, edge * Mathf.Lerp(0.55f, 0.95f, depth));
            }
        }

        private void DrawGlare()
        {
            SetDoors(0f);
            HidePuffs();
            float flash = FxKit.FlashScale;
            float a = Smooth(0f, 1f, _amount);
            float flicker = 0.9f + 0.1f * Mathf.PerlinNoise(_time * 9f, 0.3f);
            var core = Color.Lerp(new Color(0.42f, 0.36f, 0.3f), _tint, flash);
            _wash.color = new Color(core.r, core.g, core.b, Smooth(0.45f, 1f, _amount));
            _shade.color = new Color(0f, 0f, 0f, 0f);
            SetGlare(a * flicker, flash);
        }

        private void SetGlare(float a, float flash)
        {
            var size = _root.rect.size;
            float span = Mathf.Max(size.x, size.y);
            var c = new Color(1f, 0.97f, 0.9f, a * Mathf.Lerp(0.5f, 1f, flash));
            Place(_glareCore, Vector2.zero, new Vector2(span * (0.6f + a), span * (0.6f + a)), c);
            Place(_glareStreak, Vector2.zero, new Vector2(span * 1.6f, 70f + 120f * a), new Color(1f, 0.85f, 0.65f, a * 0.8f));
            Place(_glareStreak2, new Vector2(0f, 0f), new Vector2(span * 1.1f, 18f + 30f * a), new Color(0.8f, 0.9f, 1f, a * 0.6f));
        }

        private void DrawDoors()
        {
            HidePuffs();
            SetGlare(0f, 0f);
            _wash.color = new Color(0f, 0f, 0f, 0f);
            _shade.color = new Color(0f, 0f, 0f, 0f);
            SetDoors(EaseInOut(_amount));
            float tile = Mathf.Max(1f, _root.rect.width / 44f);
            _hazardTop.uvRect = new Rect(_time * 0.05f, 0f, tile, 1f);
            _hazardBottom.uvRect = new Rect(-_time * 0.05f, 0f, tile, 1f);
            float breathe = Shut ? 0.65f + 0.35f * Mathf.Sin(_held * 3f) : _amount;
            var strip = new Color(0.35f, 0.95f, 1f, 0.35f + 0.65f * breathe);
            _stripTop.color = strip;
            _stripBottom.color = strip;
            if (Shut && !_sealPlayed)
            {
                _sealPlayed = true;
                FxCamera.AddTrauma(0.12f);
            }

            float seal = Shut ? Mathf.Clamp01(1f - _held * 2.5f) : 0f;
            _seam.color = new Color(0.75f, 0.97f, 1f, seal * FxKit.FlashScale);
        }

        private void SetDoors(float close)
        {
            float off = (1f - close) * 0.56f;
            if (_doorTop == null)
            {
                return;
            }

            _doorTop.anchorMin = new Vector2(0f, 0.5f + off);
            _doorTop.anchorMax = new Vector2(1f, 1f + off);
            _doorBottom.anchorMin = new Vector2(0f, 0f - off);
            _doorBottom.anchorMax = new Vector2(1f, 0.5f - off);
            bool show = close > 0.001f;
            _doorTop.gameObject.SetActive(show);
            _doorBottom.gameObject.SetActive(show);
            if (!show)
            {
                _seam.color = Color.clear;
            }
        }

        private void HidePuffs()
        {
            for (int i = 0; i < PuffCount; i++)
            {
                _puffImg[i].color = Color.clear;
            }
        }

        private static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;

        private static void Place(RawImage img, Vector2 at, Vector2 size, Color color)
        {
            var rt = img.rectTransform;
            rt.anchoredPosition = at;
            rt.sizeDelta = size;
            img.color = color;
        }

        private static T Stretch<T>(Transform parent, string name) where T : Graphic
        {
            var g = Child<T>(parent, name);
            var rt = g.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return g;
        }

        private static T Child<T>(Transform parent, string name) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<T>();
            g.raycastTarget = false;
            g.color = Color.clear;
            var rt = g.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            return g;
        }

        // ---- code-drawn textures ----------------------------------------------------------------------------------

        /// <summary>A soft, lumpy cloud puff: a round falloff broken up by two octaves of noise.</summary>
        private static Texture2D PuffTexture()
        {
            if (_puffTex != null)
            {
                return _puffTex;
            }

            const int n = 128;
            _puffTex = NewTex(n, n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (n - 1f) * 2f - 1f, v = y / (n - 1f) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float noise = Mathf.PerlinNoise(x * 0.045f, y * 0.045f) * 0.65f + Mathf.PerlinNoise(x * 0.11f + 7f, y * 0.11f + 3f) * 0.35f;
                    float a = Mathf.Clamp01((1f - r) * 1.6f) * Mathf.Clamp01(noise * 1.5f - 0.15f);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            _puffTex.SetPixels32(px);
            _puffTex.Apply(false, true);
            return _puffTex;
        }

        /// <summary>A vertical shade: clear above, dense below — the cloud interior darkens away from the sun.</summary>
        private static Texture2D ShadeTexture()
        {
            if (_shadeTex != null)
            {
                return _shadeTex;
            }

            _shadeTex = NewTex(1, 64);
            for (int y = 0; y < 64; y++)
            {
                float a = 1f - y / 63f;
                _shadeTex.SetPixel(0, y, new Color(1f, 1f, 1f, a * a));
            }

            _shadeTex.Apply(false, true);
            return _shadeTex;
        }

        /// <summary>A round glow: bright core, long soft falloff.</summary>
        private static Texture2D GlowTexture()
        {
            if (_glowTex != null)
            {
                return _glowTex;
            }

            const int n = 64;
            _glowTex = NewTex(n, n);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (n - 1f) * 2f - 1f, v = y / (n - 1f) * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                    _glowTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }

            _glowTex.Apply(false, true);
            return _glowTex;
        }

        /// <summary>An anamorphic flare streak: a thin bar fading toward both ends and both edges.</summary>
        private static Texture2D StreakTexture()
        {
            if (_streakTex != null)
            {
                return _streakTex;
            }

            _streakTex = NewTex(128, 16);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float u = Mathf.Abs(x / 127f * 2f - 1f), v = Mathf.Abs(y / 15f * 2f - 1f);
                    float a = (1f - u) * (1f - u) * (1f - v * v);
                    _streakTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            _streakTex.Apply(false, true);
            return _streakTex;
        }

        /// <summary>Hangar door plating: dark steel with panel seams, a rivet row and a soft top light.</summary>
        private static Texture2D PlateTexture()
        {
            if (_plateTex != null)
            {
                return _plateTex;
            }

            const int w = 128, h = 128;
            _plateTex = NewTex(w, h);
            _plateTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float g = 0.16f + 0.05f * (y / (h - 1f)) + 0.02f * Mathf.PerlinNoise(x * 0.2f, y * 0.05f);
                    bool seam = x % 32 == 0 || y % 42 == 0;
                    bool rivet = (y % 42 == 6 || y % 42 == 36) && (x % 32 == 5 || x % 32 == 27);
                    if (seam)
                    {
                        g *= 0.55f;
                    }
                    else if (rivet)
                    {
                        g += 0.12f;
                    }

                    _plateTex.SetPixel(x, y, new Color(g, g * 1.04f, g * 1.12f, 1f));
                }
            }

            _plateTex.Apply(false, true);
            return _plateTex;
        }

        /// <summary>Yellow and black hazard stripes (tiled along the door edge).</summary>
        private static Texture2D HazardTexture()
        {
            if (_hazardTex != null)
            {
                return _hazardTex;
            }

            const int n = 32;
            _hazardTex = NewTex(n, n);
            _hazardTex.wrapMode = TextureWrapMode.Repeat;
            var yellow = new Color(0.95f, 0.76f, 0.12f);
            var black = new Color(0.08f, 0.08f, 0.09f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    _hazardTex.SetPixel(x, y, (x + y) % n < n / 2 ? yellow : black);
                }
            }

            _hazardTex.Apply(false, true);
            return _hazardTex;
        }

        private static Texture2D NewTex(int w, int h)
            => new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
    }
}
