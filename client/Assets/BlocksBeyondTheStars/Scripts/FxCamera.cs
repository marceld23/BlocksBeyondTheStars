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
    /// <para>#2562: every part is frame-rate independent and bounded — the kick is an exact closed-form spring
    /// (<see cref="CameraKickSpring"/>; its old Euler step blew up below ~15 FPS and wobbled the view without end),
    /// trauma and the FOV punch fade linearly and are clamped, and a non-finite input is ignored.</para>
    /// </summary>
    public static class FxCamera
    {
        private const float MaxPitch = 6f;
        private const float MaxYaw = 5f;
        private const float MaxRoll = 4f;
        private const float Frequency = 22f;
        private const float MaxFovPunch = 20f;

        /// <summary>The shake noise's time wraps after this many noise units so its input never grows large enough to
        /// lose float precision in a long session (a jump in the noise is invisible — it already changes every frame).</summary>
        private const double NoisePeriod = 4096.0;

        private static readonly CameraKickSpring KickSpring = new CameraKickSpring(); // degrees of upward kick
        private static float _trauma;
        private static float _fovPunch;  // degrees, eases back

        /// <summary>Adds shake trauma (0..1). A light hit ≈ 0.15, an explosion nearby ≈ 0.6. #2746: it also rumbles the
        /// gamepad (explosions, ship hits, hyperspace, a giant's stomp) unless <paramref name="rumble"/> is false — trauma
        /// under <see cref="RumbleMixer.MinTrauma"/> (a hot hull's shimmer) never does. The rumble is its own setting,
        /// independent of the camera-motion comfort switch.</summary>
        public static void AddTrauma(float amount, bool rumble = true)
        {
            if (Finite(amount))
            {
                _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));
                if (rumble)
                {
                    PadRumble.Trauma(amount);
                }
            }
        }

        /// <summary>#2518: trauma from the player's own damage. It builds up to <see cref="DamageFeedback.MaxTrauma"/> at
        /// most (a light rattle, never the explosion-level shake continuous damage used to stack up to) and never lowers
        /// trauma something else already added.</summary>
        public static void AddDamageTrauma(float amount)
        {
            if (Finite(amount))
            {
                _trauma = DamageFeedback.AddCappedTrauma(_trauma, amount);
            }
        }

        /// <summary>A recoil kick of <paramref name="degrees"/> upward pitch that springs back (weapons — only the local
        /// player's own shots and swings kick). #2746: the shot also rumbles the gamepad.</summary>
        public static void Kick(float degrees)
        {
            KickSpring.Kick(degrees);
            PadRumble.WeaponKick(degrees);
        }

        /// <summary>A brief FOV widening (boost, hyperjump arrival, a heavy shot).</summary>
        public static void FovPunch(float degrees)
        {
            if (Finite(degrees))
            {
                _fovPunch = Mathf.Clamp(Mathf.Max(_fovPunch, degrees), 0f, MaxFovPunch);
            }
        }

        /// <summary>The current trauma, for effects that scale with it.</summary>
        public static float Trauma => _trauma;

        /// <summary>Advances the camera feel by <paramref name="dt"/> and returns the rotation offset (pitch, yaw, roll in
        /// degrees) and the FOV offset to add this frame. Zero when the Camera-motion setting is off.</summary>
        public static void Sample(float dt, out Vector3 euler, out float fov)
        {
            if (!Finite(dt) || dt < 0f)
            {
                dt = 0f; // a broken frame time advances nothing rather than poisoning the camera
            }

            _trauma = Mathf.MoveTowards(_trauma, 0f, dt * 1.1f);
            // #2562: exact for any frame time — a slow frame can no longer make the kick oscillate and grow.
            float kick = KickSpring.Step(dt);
            _fovPunch = Mathf.MoveTowards(_fovPunch, 0f, dt * 18f);

            float scale = FxKit.CameraMotion ? 1f : 0f;
            float shake = _trauma * _trauma * FxKit.ScreenShake * scale;
            float t = (float)((Time.timeAsDouble * Frequency) % NoisePeriod);
            euler = new Vector3(
                (Mathf.PerlinNoise(t, 0.13f) * 2f - 1f) * MaxPitch * shake - kick * scale * Mathf.Max(0.35f, FxKit.ScreenShake),
                (Mathf.PerlinNoise(t, 7.71f) * 2f - 1f) * MaxYaw * shake,
                (Mathf.PerlinNoise(t, 3.37f) * 2f - 1f) * MaxRoll * shake);
            fov = _fovPunch * scale;
        }

        /// <summary>Clears all camera feel (world teardown, cinematics) — and stops the gamepad's motors (#2746).</summary>
        public static void Reset()
        {
            _trauma = 0f;
            KickSpring.Reset();
            _fovPunch = 0f;
            PadRumble.StopAll();
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
