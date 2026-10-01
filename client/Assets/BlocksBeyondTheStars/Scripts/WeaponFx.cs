// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The world's weapon/tool effect facade. Since the VFX overhaul (#2152/#2154) it draws everything through
    /// <see cref="FxKit"/> and <see cref="FxShots"/> — layered beams, glowing projectiles, slash ribbons, stretched sparks,
    /// glow cards and FX lights — instead of spawning an opaque <c>Unlit/Color</c> cube (and a new Material that was never
    /// destroyed, #2151) per beam, trail dot, spark and pulse bit. The old entry points stay for every caller that only
    /// knows a colour (sentries, drones, bandits, the speeder, giants); the player's own weapons use <see cref="Fire"/> /
    /// <see cref="Swing"/> with the data-driven look of the held item. Render-only; combat stays server-authoritative.
    /// </summary>
    public sealed class WeaponFx : MonoBehaviour
    {
        /// <summary>The look of a held weapon/tool fired from <paramref name="muzzle"/> (#2154).</summary>
        public void Fire(FxLook look, Vector3 muzzle, Vector3 target, bool impact, Vector3 normal, bool local)
            => FxShots.Fire(look, muzzle, target, impact, normal, local);

        /// <summary>A melee swing of a held weapon (#2154).</summary>
        public void Swing(FxLook look, Vector3 center, Vector3 forward, Vector3 up, bool hit, Vector3 hitPoint, bool local)
            => FxShots.Swing(look, center, forward, up, hit, hitPoint, local);

        /// <summary>An instant energy beam of a colour from <paramref name="from"/> to <paramref name="to"/> with a muzzle
        /// flash and an impact (sentries, drones, bandit gunners, the drill laser).</summary>
        public void Shoot(Vector3 from, Vector3 to, Color color)
            => FxShots.Fire(new FxLook("laser", color, Color.Lerp(color, Color.white, 0.75f), 0f, 0.8f, 0f), from, to, true, Vector3.zero, false);

        /// <summary>A travelling kinetic bolt of a colour that bursts on arrival.</summary>
        public void Projectile(Vector3 from, Vector3 to, Color color)
            => FxShots.Fire(new FxLook("slug", color, Color.Lerp(color, Color.white, 0.7f), 0f, 1f, 55f), from, to, true, Vector3.zero, false);

        /// <summary>A quick slash ribbon swept in front of <paramref name="center"/> (melee weapons / fists).</summary>
        public void MeleeArc(Vector3 center, Vector3 forward, Vector3 up, Color color)
            => FxShots.Swing(new FxLook("slash", color, Color.white, 0f, 1f, 0f), center, forward, up, false, center, false);

        /// <summary>A brief additive glow card (muzzle / impact / detonation flash).</summary>
        public void Flash(Vector3 at, Color color, float size)
        {
            FxKit.Flash(at, color, size * 1.6f, 0.14f);
            if (size >= 0.9f)
            {
                FxLights.Flash(at, color, 1.2f * size, 5f * size, 0.2f);
            }
        }

        /// <summary>A shower of stretched sparks flying out of a point (impacts, drilling, exhausts).</summary>
        public void Sparks(Vector3 at, Color color, int count = 5)
            => FxKit.Burst(FxKit.Kind.Sparks, at, count, Vector3.up, 75f, 1.5f, 4.2f, 0.035f, 0.06f, 0.25f, 0.45f, color, Color.Lerp(color, Color.white, 0.5f));

        /// <summary>A short expanding ground ring with a few motes (a ping / a pulse).</summary>
        public void Pulse(Vector3 at, Color color)
        {
            FxKit.Ring(at, Vector3.up, color, 0.2f, 2.2f, 0.5f, thickness: 0.18f, fill: 0.05f, intensity: 2.2f);
            FxKit.Burst(FxKit.Kind.Motes, at, 8, Vector3.up, 70f, 0.4f, 1.4f, 0.04f, 0.07f, 0.4f, 0.7f, color);
        }

        /// <summary>The classic tan dust of a footfall on ordinary ground.</summary>
        private static readonly Color DefaultDust = new Color(0.62f, 0.57f, 0.47f);

        /// <summary>A low dust puff (landing / footfall).</summary>
        public void Dust(Vector3 at, int count = 7) => Dust(at, count, DefaultDust);

        /// <summary>A low dust puff in the colour of the ground it comes from (#2074: a black-sand sea throws black dust).</summary>
        public void Dust(Vector3 at, int count, Color tint)
            => FxKit.Burst(FxKit.Kind.Dust, at + Vector3.up * 0.05f, count, Vector3.up, 70f, 0.5f, 1.6f, 0.14f, 0.3f, 0.4f, 0.8f,
                new Color(tint.r * 1.06f, tint.g * 1.05f, tint.b * 1.06f, 0.7f));

        private static Material _thrustMat;

        /// <summary>#1511: a persistent, LOOPING thrust flame (additive glow dots, world-space, arcing under gravity, fading
        /// and shrinking), parented to an avatar and driven through <see cref="SetThrust"/>. The jetpack used to spawn two
        /// one-shot particle systems EVERY FRAME while firing. Returns null when the particle shader is unavailable.</summary>
        public ParticleSystem CreateThrustFlame(Transform parent, Vector3 localOffset, Color color)
        {
            _thrustMat ??= FxKit.SparkMaterial();
            if (_thrustMat == null)
            {
                return null;
            }

            var go = new GameObject("ThrustFlame");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localOffset;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f * 0.6f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.6f * 0.3f, 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.13f * 0.6f, 0.13f);
            main.startColor = FxKit.Lin(color);
            main.maxParticles = 96;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.9f;

            var em = ps.emission;
            em.rateOverTime = 0f; // driven by SetThrust

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _thrustMat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
            return ps;
        }

        /// <summary>Particles per second one thrust flame emits while firing — three per frame at 60 fps was what
        /// the per-frame bursts produced.</summary>
        public const float ThrustRate = 180f;

        /// <summary>Turns a <see cref="CreateThrustFlame"/> emitter on or off (cheap to call every frame).</summary>
        public static void SetThrust(ParticleSystem flame, bool on)
        {
            if (flame == null)
            {
                return;
            }

            var em = flame.emission;
            float want = on ? ThrustRate * Mathf.Max(0.5f, FxKit.Density) : 0f;
            if (em.rateOverTime.constant != want)
            {
                em.rateOverTime = want;
            }
        }
    }
}
