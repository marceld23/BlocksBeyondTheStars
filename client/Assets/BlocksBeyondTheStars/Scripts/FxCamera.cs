// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Camera feel for the VFX overhaul (#2152): trauma-based shake (Squirrel Eiserloh, GDC 2016 — "Juicing your
    /// cameras with math"), a weapon kick that springs back, and an FOV punch. Hits ADD trauma (0..1) that decays
    /// linearly; the shake is trauma² so small knocks barely move the view while big ones really rattle, and it is
    /// ROTATION-only Perlin noise (translation shake clips the camera into blocks). Everything is scaled by the
    /// Screen-shake slider and silenced by the Camera-motion comfort setting. The first-person controller and the
    /// flight camera both <see cref="Sample"/> it once per frame.
    /// </summary>
    public static class FxCamera
    {
        private const float MaxPitch = 6f;
        private const float MaxYaw = 5f;
        private const float MaxRoll = 4f;
        private const float Frequency = 22f;

        private static float _trauma;
        private static float _kick;      // degrees of upward kick, springs back
        private static float _kickVel;
        private static float _fovPunch;  // degrees, eases back

        /// <summary>Adds shake trauma (0..1). A light hit ≈ 0.15, an explosion nearby ≈ 0.6.</summary>
        public static void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));

        /// <summary>A recoil kick of <paramref name="degrees"/> upward pitch that springs back (weapons).</summary>
        public static void Kick(float degrees) => _kickVel += degrees * 18f;

        /// <summary>A brief FOV widening (boost, hyperjump arrival, a heavy shot).</summary>
        public static void FovPunch(float degrees) => _fovPunch = Mathf.Max(_fovPunch, degrees);

        /// <summary>The current trauma, for effects that scale with it.</summary>
        public static float Trauma => _trauma;

        /// <summary>Advances the camera feel by <paramref name="dt"/> and returns the rotation offset (pitch, yaw, roll in
        /// degrees) and the FOV offset to add this frame. Zero when the Camera-motion setting is off.</summary>
        public static void Sample(float dt, out Vector3 euler, out float fov)
        {
            _trauma = Mathf.MoveTowards(_trauma, 0f, dt * 1.1f);

            // Critically-damped-ish spring for the kick.
            _kickVel += (-_kick * 220f - _kickVel * 24f) * dt;
            _kick += _kickVel * dt;
            _fovPunch = Mathf.MoveTowards(_fovPunch, 0f, dt * 18f);

            float scale = FxKit.CameraMotion ? 1f : 0f;
            float shake = _trauma * _trauma * FxKit.ScreenShake * scale;
            float t = Time.time * Frequency;
            euler = new Vector3(
                (Mathf.PerlinNoise(t, 0.13f) * 2f - 1f) * MaxPitch * shake - _kick * scale * Mathf.Max(0.35f, FxKit.ScreenShake),
                (Mathf.PerlinNoise(t, 7.71f) * 2f - 1f) * MaxYaw * shake,
                (Mathf.PerlinNoise(t, 3.37f) * 2f - 1f) * MaxRoll * shake);
            fov = _fovPunch * scale;
        }

        /// <summary>Clears all camera feel (world teardown, cinematics).</summary>
        public static void Reset()
        {
            _trauma = 0f;
            _kick = 0f;
            _kickVel = 0f;
            _fovPunch = 0f;
        }
    }
}
