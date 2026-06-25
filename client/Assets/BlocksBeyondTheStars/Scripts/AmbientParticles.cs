// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Slow-drifting ambient motes (dust / pollen / embers) floating around the player on a planet surface, to
    /// make the air feel alive — the cheap-but-big "atmosphere" win that pairs with fog/god-rays. A code-driven
    /// Unity <see cref="ParticleSystem"/> (the first of the VFX migration off the old Unlit/Color cubes) using the
    /// additive <c>BlocksBeyondTheStars/Particle</c> shader. The emitter follows the camera's position (not its
    /// rotation, so motes don't swing with the view); particles simulate in world space and fade in/out softly.
    /// Tinted per biome, suppressed in space / airless / station skies and thinned out for reduced-effects.
    /// </summary>
    public sealed class AmbientParticles : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public bool ReducedEffects;

        private ParticleSystem _ps;
        private Transform _dust;     // the particle object (a child — moved independently of the shared root)
        private Material _mat;
        private float _baseRate;
        private string _biome = "\0";

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Particle");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader) { mainTexture = BuildDot() };
            _baseRate = ReducedEffects ? 5f : 15f;

            var go = new GameObject("AmbientDust");
            go.transform.SetParent(transform, false); // child of the rig root; we move only THIS, never the root
            _dust = go.transform;
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.loop = true;
            main.startLifetime = 6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // stay put as the camera turns
            main.gravityModifier = 0f;
            main.startColor = new Color(0.9f, 0.92f, 1f, 0.5f);

            var emission = _ps.emission;
            emission.rateOverTime = 0f; // gated each frame in LateUpdate

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26f, 12f, 26f);

            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = 0.07f;
            noise.frequency = 0.2f;
            noise.scrollSpeed = 0.1f;

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = _mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;

            _ps.Play();
        }

        private void LateUpdate()
        {
            if (_ps == null || Camera == null || Game == null)
            {
                return;
            }

            // Follow the camera's position only (world-sim particles keep their place as you look around).
            _dust.position = Camera.transform.position;

            bool spaceSky = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.StationName)
                            || (Game.Environment != null && Game.Environment.SpaceSky) || Game.OnFootInSpace;
            var em = _ps.emission;
            em.rateOverTime = spaceSky ? 0f : _baseRate; // no airborne dust in vacuum

            string biome = Game.Environment?.Biome ?? "";
            if (biome != _biome)
            {
                _biome = biome;
                ApplyBiomeColor(biome);
            }
        }

        private void ApplyBiomeColor(string biome)
        {
            Color c = (biome ?? string.Empty).ToLowerInvariant() switch
            {
                "ice" or "frozen" => new Color(0.82f, 0.90f, 1.00f),  // icy sparkle
                "lava" or "volcanic" => new Color(1.00f, 0.52f, 0.22f), // drifting embers
                "jungle" or "forest" => new Color(0.72f, 1.00f, 0.62f), // pollen / spores
                "desert" => new Color(1.00f, 0.90f, 0.62f),            // fine sand dust
                "crystal" => new Color(0.85f, 0.80f, 1.00f),           // crystalline glints
                _ => new Color(0.90f, 0.92f, 1.00f),
            };
            c.a = 0.5f;
            var main = _ps.main;
            main.startColor = c;
        }

        /// <summary>A soft round dot (bright core → transparent rim) for each mote.</summary>
        private static Texture2D BuildDot()
        {
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
