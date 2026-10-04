// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The anomaly (#2241): "something no catalogue explains" — an iridescent soap bubble (~4.5 units) whose colours
    /// flow over its rim, small cubes blinking in and out inside it, and a handful of blocks circling it that now and
    /// then jump to another spot as if physics did not quite hold there; a soft hovering hum with a wandering pitch.
    /// It is round, small and rainbow on purpose — the wormhole is the big, jagged, violet tear. Once scanned it
    /// ripples and CALMS: the colours freeze, the blocks stop jumping — "already explored", also on later visits.
    /// Built from the shared FX shaders (FxShell for the bubble), no texture; runs in the browser.
    /// </summary>
    public sealed class AnomalyView : MonoBehaviour
    {
        private const float BubbleRadius = 2.3f;
        private const int InnerCubes = 6;
        private const int OrbitBlocks = 4;

        private Renderer _bubble;
        private readonly Transform[] _inner = new Transform[InnerCubes];
        private readonly Transform[] _orbit = new Transform[OrbitBlocks];
        private readonly Vector3[] _orbitParam = new Vector3[OrbitBlocks]; // (angle, height, speed)
        private MaterialPropertyBlock _block;
        private AudioSource _hum;
        private ClientSettings _settings;
        private float _seed;
        private float _glitchTimer;
        private bool _calm;
        private float _calmHue;
        private float _reactTimer;

        public void Init(string id, bool calm, ClientSettings settings)
        {
            _settings = settings;
            int h = (id ?? string.Empty).GetHashCode();
            _seed = (h & 0xFFFF) / 65535f * 100f;
            _calm = calm;
            _calmHue = Mathf.Repeat(_seed * 0.37f, 1f);
            _block = new MaterialPropertyBlock();

            var shell = FxKit.Cached("BlocksBeyondTheStars/FxShell", "anomaly");
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Bubble";
            Strip(sphere);
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * BubbleRadius * 2f;
            _bubble = sphere.GetComponent<Renderer>();
            if (shell != null)
            {
                _bubble.sharedMaterial = shell;
            }

            var cubeMat = CubeMaterial();
            for (int i = 0; i < InnerCubes; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "Inner" + i;
                Strip(c);
                c.transform.SetParent(transform, false);
                float a = i * 2.39996f + _seed;
                c.transform.localPosition = new Vector3(Mathf.Cos(a), (i % 3 - 1) * 0.5f, Mathf.Sin(a)) * 0.9f;
                c.transform.localScale = Vector3.one * 0.32f;
                if (cubeMat != null)
                {
                    c.GetComponent<Renderer>().sharedMaterial = cubeMat;
                }

                _inner[i] = c.transform;
            }

            for (int i = 0; i < OrbitBlocks; i++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Orbit" + i;
                Strip(b);
                b.transform.SetParent(transform, false);
                b.transform.localScale = Vector3.one * 0.55f;
                if (cubeMat != null)
                {
                    b.GetComponent<Renderer>().sharedMaterial = cubeMat;
                }

                _orbit[i] = b.transform;
                _orbitParam[i] = new Vector3(i * Mathf.PI * 0.5f + _seed, Random.Range(-1.2f, 1.2f), 0.5f + i * 0.12f);
            }

            var clip = Resources.Load<AudioClip>("audio/anomaly_hum") ?? ProceduralAudio.Generate("anomaly_hum");
            if (clip != null)
            {
                _hum = gameObject.AddComponent<AudioSource>();
                _hum.clip = clip;
                _hum.loop = true;
                _hum.spatialBlend = 1f;
                _hum.rolloffMode = AudioRolloffMode.Linear;
                _hum.minDistance = 4f;
                _hum.maxDistance = 60f;
                _hum.dopplerLevel = 0f;
                _hum.volume = 0.3f * (_settings?.SfxVolume ?? 0.8f);
                _hum.Play();
            }
        }

        /// <summary>The ship scanner read it: a ripple, a chime, and from then on it is calm.</summary>
        public void React()
        {
            _reactTimer = 0.8f;
            var c = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.08f + _seed, 1f), 0.6f, 1f);
            FxKit.Shell(transform.position, c, BubbleRadius, BubbleRadius * 2.4f, 0.7f, rimPower: 3f, fill: 0f, intensity: 1.6f);
            FxKit.Ring(transform.position, Vector3.zero, c, BubbleRadius, BubbleRadius * 3f, 0.8f, thickness: 0.06f, lines: 2f, intensity: 1.8f);
            ClientAudio.Instance?.Cue("anomaly_react", 0.7f);
            if (!_calm)
            {
                _calmHue = Mathf.Repeat(Time.time * 0.08f + _seed, 1f);
            }

            _calm = true;
        }

        private void Update()
        {
            float t = Time.time + _seed;
            float dt = Time.deltaTime;
            _reactTimer -= dt;

            // The bubble: flowing rainbow (or frozen, once explored); a ripple after the scan.
            float hue = _calm ? _calmHue : Mathf.Repeat(t * 0.08f, 1f);
            var col = Color.HSVToRGB(hue, _calm ? 0.35f : 0.65f, 1f);
            float ripple = _reactTimer > 0f ? Mathf.Sin(_reactTimer * 30f) * _reactTimer * 0.12f : 0f;
            if (_bubble != null)
            {
                _bubble.transform.localScale = Vector3.one * BubbleRadius * 2f * (1f + ripple + (_calm ? 0f : Mathf.Sin(t * 1.3f) * 0.03f));
                _block.Clear();
                _block.SetColor("_Color", FxKit.Lin(col));
                _block.SetFloat("_Intensity", _calm ? 1.1f : 1.8f);
                _block.SetFloat("_RimPower", 2.2f);
                _block.SetFloat("_Fill", 0.06f);
                _block.SetFloat("_Hex", 0f);
                _bubble.SetPropertyBlock(_block);
            }

            // Inner cubes blink in and out (calm: they stay, dimly).
            for (int i = 0; i < InnerCubes; i++)
            {
                if (_inner[i] == null)
                {
                    continue;
                }

                float s = _calm ? 0.22f : Mathf.Max(0f, Mathf.Sin(t * (1.4f + i * 0.31f) + i)) * 0.36f;
                _inner[i].localScale = Vector3.one * s;
                _inner[i].localRotation = Quaternion.Euler(t * 40f + i * 30f, t * 55f, 0f);
                SetCubeColor(_inner[i], Color.HSVToRGB(Mathf.Repeat(hue + i * 0.13f, 1f), 0.5f, 1f));
            }

            // Orbit blocks circle — and every few seconds one jumps somewhere else (not once calm).
            _glitchTimer -= dt;
            if (!_calm && _glitchTimer <= 0f)
            {
                _glitchTimer = Random.Range(1.2f, 2.6f);
                int k = Random.Range(0, OrbitBlocks);
                var p = _orbitParam[k];
                _orbitParam[k] = new Vector3(p.x + Random.Range(1f, 3f), Random.Range(-1.4f, 1.4f), p.z);
                if (_orbit[k] != null)
                {
                    FxKit.Flash(_orbit[k].position, col, 0.8f * FxKit.FlashScale, 0.1f);
                }
            }

            for (int i = 0; i < OrbitBlocks; i++)
            {
                if (_orbit[i] == null)
                {
                    continue;
                }

                var p = _orbitParam[i];
                float a = p.x + t * p.z * (_calm ? 0.4f : 1f);
                _orbit[i].localPosition = new Vector3(Mathf.Cos(a) * 3.3f, p.y, Mathf.Sin(a) * 3.3f);
                _orbit[i].localRotation = Quaternion.Euler(t * 30f, t * 45f + i * 20f, 0f);
                SetCubeColor(_orbit[i], Color.HSVToRGB(Mathf.Repeat(hue + 0.5f + i * 0.07f, 1f), 0.45f, 0.95f));
            }

            if (_hum != null)
            {
                _hum.pitch = (_calm ? 0.9f : 1f) + Mathf.Sin(t * 0.6f) * 0.06f;
                _hum.volume = (_calm ? 0.18f : 0.3f) * (_settings?.SfxVolume ?? 0.8f);
            }
        }

        private void SetCubeColor(Transform cube, Color c)
        {
            var r = cube.GetComponent<Renderer>();
            if (r == null)
            {
                return;
            }

            _block.Clear();
            _block.SetColor("_Color", ShaderColor.Srgb(c)); // the flat-colour convention of the ship models
            r.SetPropertyBlock(_block);
        }

        private static Material _cubeMat;

        /// <summary>One shared flat-colour material for the cubes; each cube gets its colour through a property block.</summary>
        private static Material CubeMaterial()
        {
            if (_cubeMat == null)
            {
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
                if (shader != null)
                {
                    _cubeMat = new Material(shader) { name = "AnomalyCube" };
                }
            }

            return _cubeMat;
        }

        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }
    }
}
