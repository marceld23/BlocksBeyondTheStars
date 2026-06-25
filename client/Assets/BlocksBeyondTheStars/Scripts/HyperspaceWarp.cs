// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Hyperspace warp animation: when the server reports a jump to another star system
    /// (<see cref="GameBootstrap.HyperjumpStarted"/>), a full-screen overlay plays — a field of star
    /// streaks rushes outward from the centre (the classic "stars stretch into lines") over a dark-blue
    /// wash, climaxing in a white flash before clearing to reveal the new world (which streams in behind).
    /// Pure uGUI on a DPI-scaled canvas above everything; pooled, no bundled art.
    /// </summary>
    public sealed class HyperspaceWarp : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int StreakCount = 90;
        private const float Duration = 2.6f;
        private const float MaxRadius = 1250f; // reference units; overshoots the 1920×1080 corners

        private Canvas _canvas;
        private Image _backdrop;
        private Image _flash;
        private RectTransform _center;
        private RectTransform[] _streaks;
        private Image[] _streakImg;
        private float[] _angle;   // radians
        private float[] _depth;   // 0..1 parallax
        private float[] _phase;   // 0..1 stagger

        private bool _playing;
        private float _t;

        // WorldRig sets Game right after AddComponent, so subscribe in Start (not OnEnable, which
        // would run during AddComponent while Game is still null).
        private void Start()
        {
            if (Game != null)
            {
                Game.HyperjumpStarted += Play;
            }
        }

        private void OnDestroy()
        {
            if (Game != null)
            {
                Game.HyperjumpStarted -= Play;
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        public void Play()
        {
            EnsureBuilt();
            _t = 0f;
            _playing = true;
            _canvas.enabled = true;
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _t += Time.deltaTime;
            float t = _t;

            // Envelope: ramp up, hold, ramp down.
            float intensity =
                t < 0.45f ? Mathf.SmoothStep(0f, 1f, t / 0.45f)
                : t > Duration - 0.5f ? Mathf.SmoothStep(1f, 0f, (t - (Duration - 0.5f)) / 0.5f)
                : 1f;

            _backdrop.color = new Color(0.02f, 0.04f, 0.10f, 0.85f * intensity);

            // A bright white flash as we punch through (peaks just past the midpoint).
            float flash = Mathf.Clamp01(1f - Mathf.Abs(t - 1.95f) / 0.35f);
            _flash.color = new Color(0.85f, 0.92f, 1f, flash * 0.9f);

            for (int i = 0; i < _streaks.Length; i++)
            {
                float speed = 0.55f + _depth[i] * 0.9f;
                float p = Mathf.Repeat(t * speed + _phase[i], 1f); // 0 (centre) → 1 (edge)
                float r = p * MaxRadius;
                float len = (30f + 230f * p) * (0.5f + _depth[i]);
                float a = intensity * Mathf.Clamp01(p * 4f) * (1f - p * 0.25f);

                var rt = _streaks[i];
                rt.anchoredPosition = new Vector2(Mathf.Sin(_angle[i]) * r, Mathf.Cos(_angle[i]) * r);
                rt.sizeDelta = new Vector2(2.5f + _depth[i] * 1.5f, len);
                rt.localEulerAngles = new Vector3(0f, 0f, -_angle[i] * Mathf.Rad2Deg);
                _streakImg[i].color = new Color(0.7f, 0.85f, 1f, a);
            }

            if (_t >= Duration)
            {
                _playing = false;
                _canvas.enabled = false;
            }
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("Hyperspace Warp");
            _canvas.sortingOrder = 70; // above HUD/menus/map
            var root = _canvas.transform;

            _backdrop = FullScreen(root, "Backdrop", new Color(0.02f, 0.04f, 0.10f, 0f));

            var centerGo = new GameObject("Center", typeof(RectTransform));
            centerGo.transform.SetParent(root, false);
            _center = centerGo.GetComponent<RectTransform>();
            _center.anchorMin = _center.anchorMax = _center.pivot = new Vector2(0.5f, 0.5f);
            _center.sizeDelta = Vector2.zero;

            _streaks = new RectTransform[StreakCount];
            _streakImg = new Image[StreakCount];
            _angle = new float[StreakCount];
            _depth = new float[StreakCount];
            _phase = new float[StreakCount];
            for (int i = 0; i < StreakCount; i++)
            {
                _angle[i] = Random.value * Mathf.PI * 2f;
                _depth[i] = Random.value;
                _phase[i] = Random.value;

                var go = new GameObject("Streak", typeof(RectTransform));
                go.transform.SetParent(_center, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f); // grows outward from its inner end
                var img = go.AddComponent<Image>();
                img.sprite = UiKit.SolidSprite;
                img.raycastTarget = false;
                _streaks[i] = rt;
                _streakImg[i] = img;
            }

            _flash = FullScreen(root, "Flash", new Color(0.85f, 0.92f, 1f, 0f));
            _canvas.enabled = false;
        }

        private static Image FullScreen(Transform parent, string name, Color color)
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
    }
}
