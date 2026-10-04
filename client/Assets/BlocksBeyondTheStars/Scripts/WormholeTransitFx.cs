// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Flying through a wormhole (#2242) — the rift transit, a short first-person effect in place of the hyperspace
    /// warp. ENTER (the pilot pressed E at the rift): the tear rips across the screen like cracking glass, a violet wash
    /// rises, a burst of chroma and grain, the camera shudders. ARRIVAL (the twin system's flight state arrived): a
    /// white-violet flash, the wash clears and the cracks fall away behind the ship. If the server refused (no arrival
    /// within a few seconds) it simply fades. Respects "reduce flashes" (<see cref="FxKit.FlashScale"/>) and the camera
    /// comfort switch (FxCamera). A uGUI overlay with one generated crack texture — no shader, runs everywhere.
    /// </summary>
    public sealed class WormholeTransitFx : MonoBehaviour
    {
        private const float EnterSeconds = 0.9f;
        private const float ArriveSeconds = 0.9f;
        private const float TimeoutSeconds = 4f;

        private static WormholeTransitFx _instance;
        private static Texture2D _crackTex;

        private Canvas _canvas;
        private RawImage _crack;
        private Image _wash;
        private Image _flash;
        private float _age;
        private int _state; // 0 idle, 1 entering/holding, 2 arriving

        /// <summary>True while a transit is running (the "fly through" prompt hides, a second E does nothing).</summary>
        public static bool Busy => _instance != null && _instance._state != 0;

        public static void PlayEnter(ClientSettings settings)
        {
            var fx = Ensure();
            fx._state = 1;
            fx._age = 0f;
            fx._canvas.enabled = true;
            ClientAudio.Instance?.Cue("wormhole_enter", 0.9f);
            UrpScenePost.Instance?.Burst(0.9f, 0.5f, EnterSeconds + 0.4f);
            FxCamera.AddTrauma(0.35f);
        }

        public static void PlayArrival()
        {
            var fx = Ensure();
            fx._state = 2;
            fx._age = 0f;
            fx._canvas.enabled = true;
            ClientAudio.Instance?.Cue("wormhole_exit", 0.9f);
            UrpScenePost.Instance?.Burst(0.6f, 0.3f, ArriveSeconds);
            FxCamera.AddTrauma(0.25f);
            FxCamera.FovPunch(7f);
        }

        private static WormholeTransitFx Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("WormholeTransitFx");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WormholeTransitFx>();
            _instance.Build();
            return _instance;
        }

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 72; // over the flight HUD, like the hyperspace warp overlay
            _canvas.enabled = false;

            _wash = FullScreen("Wash").AddComponent<Image>();
            _wash.sprite = UiKit.SolidSprite;
            _wash.raycastTarget = false;

            var crackGo = new GameObject("Crack", typeof(RectTransform));
            crackGo.transform.SetParent(transform, false);
            var crt = crackGo.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(1400f, 1400f);
            _crack = crackGo.AddComponent<RawImage>();
            _crack.texture = CrackTexture();
            _crack.raycastTarget = false;

            _flash = FullScreen("Flash").AddComponent<Image>();
            _flash.sprite = UiKit.SolidSprite;
            _flash.raycastTarget = false;
        }

        private GameObject FullScreen(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        private void Update()
        {
            if (_state == 0)
            {
                return;
            }

            _age += Time.unscaledDeltaTime;
            var violet = WormholeView.Violet;
            float flashScale = FxKit.FlashScale;
            float side = Mathf.Max(Screen.width, Screen.height) / Mathf.Max(0.01f, _canvas.scaleFactor);

            if (_state == 1)
            {
                float k = Mathf.Clamp01(_age / EnterSeconds);
                float e = 1f - (1f - k) * (1f - k);
                _crack.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(side * 0.15f, side * 1.9f, e);
                _crack.rectTransform.localRotation = Quaternion.Euler(0f, 0f, k * 12f);
                _crack.color = new Color(0.85f, 0.9f, 1f, Mathf.Lerp(0.2f, 0.95f, e));
                _wash.color = new Color(violet.r * 0.4f, violet.g * 0.3f, violet.b * 0.6f, Mathf.Lerp(0f, 0.65f, e));
                _flash.color = new Color(1f, 1f, 1f, 0f);
                if (_age > TimeoutSeconds)
                {
                    _state = 3; // refused or lost — fade back out
                    _age = 0f;
                }
            }
            else if (_state == 2)
            {
                float k = Mathf.Clamp01(_age / ArriveSeconds);
                float flash = Mathf.Clamp01(1f - k * 3f) * flashScale;
                _flash.color = new Color(0.92f, 0.85f, 1f, flash * 0.9f);
                _crack.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(side * 1.9f, side * 3.2f, k);
                _crack.color = new Color(0.85f, 0.9f, 1f, (1f - k) * 0.9f);
                _wash.color = new Color(violet.r * 0.4f, violet.g * 0.3f, violet.b * 0.6f, (1f - k) * 0.65f);
                if (k >= 1f)
                {
                    Stop();
                }
            }
            else
            {
                float k = Mathf.Clamp01(_age / 0.6f);
                _crack.color = new Color(0.85f, 0.9f, 1f, (1f - k) * 0.9f);
                _wash.color = new Color(violet.r * 0.4f, violet.g * 0.3f, violet.b * 0.6f, (1f - k) * 0.65f);
                if (k >= 1f)
                {
                    Stop();
                }
            }
        }

        private void Stop()
        {
            _state = 0;
            _canvas.enabled = false;
        }

        /// <summary>A white crack network radiating from the middle — jagged strands that fork — on transparent; made
        /// once (512², a few milliseconds) and scaled by the effect.</summary>
        private static Texture2D CrackTexture()
        {
            if (_crackTex != null)
            {
                return _crackTex;
            }

            const int n = 512;
            var px = new Color32[n * n];
            var rng = new System.Random(2242);
            for (int s = 0; s < 14; s++)
            {
                float ang = (float)(s / 14.0 * System.Math.PI * 2.0 + rng.NextDouble() * 0.4);
                Strand(px, n, rng, n * 0.5f, n * 0.5f, ang, n * 0.48f, 3.2f, 2);
            }

            _crackTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _crackTex.SetPixels32(px);
            _crackTex.Apply(false, true);
            return _crackTex;
        }

        private static void Strand(Color32[] px, int n, System.Random rng, float x, float y, float ang, float length, float width, int forks)
        {
            float step = 6f;
            int steps = (int)(length / step);
            for (int i = 0; i < steps; i++)
            {
                ang += (float)(rng.NextDouble() - 0.5) * 0.9f;
                float nx = x + Mathf.Cos(ang) * step;
                float ny = y + Mathf.Sin(ang) * step;
                float w = Mathf.Max(0.8f, width * (1f - i / (float)steps));
                Line(px, n, x, y, nx, ny, w);
                x = nx;
                y = ny;
                if (forks > 0 && rng.NextDouble() < 0.08)
                {
                    Strand(px, n, rng, x, y, ang + (rng.NextDouble() < 0.5 ? -0.8f : 0.8f), length * 0.35f, w * 0.7f, forks - 1);
                }
            }
        }

        private static void Line(Color32[] px, int n, float x0, float y0, float x1, float y1, float w)
        {
            int minX = Mathf.Max(0, (int)(Mathf.Min(x0, x1) - w - 1)), maxX = Mathf.Min(n - 1, (int)(Mathf.Max(x0, x1) + w + 1));
            int minY = Mathf.Max(0, (int)(Mathf.Min(y0, y1) - w - 1)), maxY = Mathf.Min(n - 1, (int)(Mathf.Max(y0, y1) + w + 1));
            var a = new Vector2(x0, y0);
            var b = new Vector2(x1, y1);
            var ab = b - a;
            float len2 = Mathf.Max(0.0001f, ab.sqrMagnitude);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float d = Vector2.Distance(p, a + ab * t);
                    float alpha = Mathf.Clamp01(1f - (d - w * 0.5f) / 1.5f);
                    if (alpha <= 0f)
                    {
                        continue;
                    }

                    int idx = y * n + x;
                    byte v = (byte)Mathf.Max(px[idx].a, alpha * 255f);
                    px[idx] = new Color32(255, 255, 255, v);
                }
            }
        }
    }
}
