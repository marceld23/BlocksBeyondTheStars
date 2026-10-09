// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Shooting stars on a planet's night sky (#2400): now and then a short additive streak crosses the dark half of
    /// the sky, more often during a meteor shower; very rarely a slow "distant ship" with a blinking light drifts by.
    /// One velocity-stretched particle system on the additive <c>Particle</c> shader, placed just inside the far plane
    /// (terrain occludes it like the stars). Off by day, in space, on airless bodies and aboard a station — those have
    /// their own sky. Deterministic enough for captures: the next streak is scheduled, never random per frame.
    /// </summary>
    public sealed class ShootingStars : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public bool Enabled = true;

        private ParticleSystem _ps;
        private ParticleSystemRenderer _renderer;
        private float _nextAt;
        private float _shipUntil;
        private Vector3 _shipPos, _shipVel;
        private readonly System.Random _rng = new System.Random(20261009);

        private void Awake()
        {
            var go = new GameObject("ShootingStars");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 16;
            main.startLifetime = 0.8f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.gravityModifier = 0f;
            var emission = _ps.emission;
            emission.enabled = false;
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            _renderer = go.GetComponent<ParticleSystemRenderer>();
            _renderer.renderMode = ParticleSystemRenderMode.Stretch;
            _renderer.velocityScale = 0.06f;
            _renderer.lengthScale = 1f;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            var shader = Shader.Find("BlocksBeyondTheStars/Particle");
            if (shader != null)
            {
                _renderer.sharedMaterial = new Material(shader) { mainTexture = SkyVisuals.GlowTexture() };
                _renderer.sharedMaterial.SetFloat("_Intensity", 2.5f);
            }

            _ps.Stop();
            ScheduleNext(8f);
        }

        private void ScheduleNext(float minDelay)
            => _nextAt = Time.time + minDelay + (float)_rng.NextDouble() * minDelay * 1.5f;

        private void Update()
        {
            if (!Enabled || Game == null || Camera == null)
            {
                return;
            }

            var env = Game.Environment;
            bool skyHere = env != null && !env.SpaceSky && !Game.SpaceViewActive && string.IsNullOrEmpty(Game.StationName)
                           && !Game.OnFootInSpace;
            if (!skyHere)
            {
                return;
            }

            // Only the dark hours: the sun well below the horizon.
            float t = Game.LocalTimeOfDay;
            float sunHeight = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f);
            if (sunHeight > -0.15f)
            {
                return;
            }

            bool shower = string.Equals(env.Weather, "meteor_shower", System.StringComparison.Ordinal);
            if (Time.time >= _nextAt)
            {
                if (_rng.NextDouble() < 0.06 && _shipUntil < Time.time)
                {
                    StartShip();
                }
                else
                {
                    EmitStreak(shower);
                }

                ScheduleNext(shower ? 2f : 45f);
            }

            TickShip();
        }

        /// <summary>One streak: a point high on the dome moving fast along a shallow line, 0.6–1 s long.</summary>
        private void EmitStreak(bool shower)
        {
            Vector3 cam = Camera.transform.position;
            float r = Mathf.Max(200f, Camera.farClipPlane) * 0.42f;
            float az = (float)(_rng.NextDouble() * Mathf.PI * 2.0);
            float el = 0.35f + (float)_rng.NextDouble() * 0.9f; // 20°–70° up
            Vector3 dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
            Vector3 pos = cam + dir * r;
            // Travel roughly along the dome, slightly downward.
            Vector3 tangent = Vector3.Cross(dir, Vector3.up).normalized;
            if (_rng.NextDouble() < 0.5)
            {
                tangent = -tangent;
            }

            Vector3 vel = (tangent + Vector3.down * 0.35f).normalized * (r * (shower ? 1.4f : 1.1f));
            var p = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = r * 0.012f,
                startLifetime = shower ? 0.5f : 0.75f,
                startColor = shower ? new Color(1f, 0.85f, 0.6f) : new Color(0.9f, 0.95f, 1f),
            };
            _ps.Emit(p, 1);
        }

        /// <summary>A distant ship: a slow point with a blinking light, crossing over half a minute.</summary>
        private void StartShip()
        {
            Vector3 cam = Camera.transform.position;
            float r = Mathf.Max(200f, Camera.farClipPlane) * 0.42f;
            float az = (float)(_rng.NextDouble() * Mathf.PI * 2.0);
            float el = 0.5f + (float)_rng.NextDouble() * 0.6f;
            Vector3 dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
            _shipPos = cam + dir * r;
            _shipVel = Vector3.Cross(dir, Vector3.up).normalized * (r * 0.02f);
            _shipUntil = Time.time + 30f;
        }

        private void TickShip()
        {
            if (_shipUntil < Time.time)
            {
                return;
            }

            _shipPos += _shipVel * Time.deltaTime;
            // A steady faint dot plus a blink every 1.5 s (far below the flash limit, and tiny).
            bool blink = Mathf.Repeat(Time.time, 1.5f) < 0.12f;
            float r = Mathf.Max(200f, Camera.farClipPlane) * 0.42f;
            var p = new ParticleSystem.EmitParams
            {
                position = _shipPos,
                velocity = Vector3.zero,
                startSize = r * (blink ? 0.012f : 0.006f),
                startLifetime = 0.08f,
                startColor = blink ? new Color(1f, 0.55f, 0.45f) : new Color(0.8f, 0.85f, 0.9f, 0.6f),
            };
            _ps.Emit(p, 1);
        }
    }
}
