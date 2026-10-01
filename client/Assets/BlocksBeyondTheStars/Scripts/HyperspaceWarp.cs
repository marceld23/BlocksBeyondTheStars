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
    /// In the flight view (#2157) the jump is 3D: an <c>FxTunnel</c> of racing streaks wraps the camera, the space dust
    /// stretches into star lines, the FOV punches out at the flash and the camera rattles — the 2D overlay then only
    /// adds a faint wash. The flash honours Reduce flashes.
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
        private bool _threeD;
        private bool _punched;
        private MeshRenderer _tunnel;
        private static Mesh _tunnelMesh;

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

            if (_tunnel != null)
            {
                Destroy(_tunnel.gameObject);
            }

            FxSpaceDust.WarpBoost = 0f;
        }

        public void Play()
        {
            EnsureBuilt();
            _t = 0f;
            _playing = true;
            _canvas.enabled = true;
            _punched = false;
            var cam = Camera.main;
            _threeD = Game != null && Game.SpaceViewActive && cam != null;
            if (_threeD)
            {
                EnsureTunnel(cam.transform);
                FxCamera.AddTrauma(0.3f);
                ClientAudio.Instance?.Cue("hyperspace_charge", 0.6f);
            }
        }

        /// <summary>The 3D tunnel: an open cylinder around the camera's view axis, parented to the camera.</summary>
        private void EnsureTunnel(Transform cam)
        {
            var mat = FxKit.Cached("BlocksBeyondTheStars/FxTunnel", "warp");
            if (mat == null)
            {
                _threeD = false;
                return;
            }

            if (_tunnel == null)
            {
                var go = new GameObject("WarpTunnel");
                go.AddComponent<MeshFilter>().sharedMesh = TunnelMesh();
                _tunnel = go.AddComponent<MeshRenderer>();
                _tunnel.sharedMaterial = mat;
                _tunnel.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _tunnel.receiveShadows = false;
            }

            _tunnel.transform.SetParent(cam, false);
            _tunnel.transform.localPosition = Vector3.zero;
            _tunnel.transform.localRotation = Quaternion.identity;
            _tunnel.gameObject.SetActive(true);
        }

        /// <summary>A 48-sided open tube of radius 7 from 8 blocks behind to 140 ahead (UV x around, y along).</summary>
        private static Mesh TunnelMesh()
        {
            if (_tunnelMesh != null)
            {
                return _tunnelMesh;
            }

            const int seg = 48;
            var verts = new Vector3[(seg + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var ring = new Vector3(Mathf.Cos(a) * 7f, Mathf.Sin(a) * 7f, 0f);
                verts[i * 2] = ring + new Vector3(0f, 0f, -8f);
                verts[i * 2 + 1] = ring + new Vector3(0f, 0f, 140f);
                uvs[i * 2] = new Vector2(i / (float)seg, 0f);
                uvs[i * 2 + 1] = new Vector2(i / (float)seg, 1f);
            }

            for (int i = 0; i < seg; i++)
            {
                int v = i * 2;
                int k = i * 6;
                tris[k] = v;
                tris[k + 1] = v + 1;
                tris[k + 2] = v + 2;
                tris[k + 3] = v + 1;
                tris[k + 4] = v + 3;
                tris[k + 5] = v + 2;
            }

            _tunnelMesh = new Mesh { name = "FxWarpTunnel", vertices = verts, uv = uvs, triangles = tris };
            _tunnelMesh.bounds = new Bounds(new Vector3(0f, 0f, 66f), new Vector3(20f, 20f, 160f));
            return _tunnelMesh;
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

            // In the flight view the 3D tunnel carries the jump; the overlay only adds a faint wash (#2157).
            float overlay = _threeD ? 0.3f : 1f;
            _backdrop.color = new Color(0.02f, 0.04f, 0.10f, 0.85f * intensity * (_threeD ? 0.4f : 1f));

            // A bright white flash as we punch through (peaks just past the midpoint) — softened by Reduce flashes.
            float flash = Mathf.Clamp01(1f - Mathf.Abs(t - 1.95f) / 0.35f);
            _flash.color = new Color(0.85f, 0.92f, 1f, flash * 0.9f * FxKit.FlashScale);

            if (_threeD && _tunnel != null)
            {
                var b = FxKit.Block;
                b.Clear();
                b.SetColor("_Color", FxKit.Lin(new Color(0.55f, 0.75f, 1f)) * new Color(1f, 1f, 1f, intensity));
                b.SetColor("_Color2", FxKit.Lin(new Color(0.85f, 0.6f, 1f)));
                b.SetFloat("_Intensity", 2.4f);
                b.SetFloat("_Speed", 1.5f + 4f * intensity);
                _tunnel.SetPropertyBlock(b);
                FxSpaceDust.WarpBoost = intensity;
                if (!_punched && t >= 1.9f)
                {
                    _punched = true;
                    FxCamera.FovPunch(9f);
                    FxCamera.AddTrauma(0.35f);
                }
            }

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
                _streakImg[i].color = new Color(0.7f, 0.85f, 1f, a * overlay);
            }

            if (_t >= Duration)
            {
                _playing = false;
                _canvas.enabled = false;
                FxSpaceDust.WarpBoost = 0f;
                if (_tunnel != null)
                {
                    _tunnel.gameObject.SetActive(false);
                }
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
