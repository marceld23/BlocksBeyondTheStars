// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The vents' particles (#3275): the steady steam, the eruption's billows, and the droplets or embers it throws
    /// (partial of <see cref="GeyserView"/>). All through the shared <see cref="FxKit"/> emitters, density-scaled.</summary>
    public sealed partial class GeyserView
    {
        private const float SteamPerSecond = 4f;    // calm steam puffs per second at density 1, close by
        private const float PuffsPerSecond = 16f;   // eruption billows per second at full strength
        private const float BitsPerSecond = 34f;    // droplets (water) or embers (volcanic) per second at full strength
        private const float SmokeDrift = 0.6f;      // the Smoke emitter's speed limit: what a puff drifts at in the end
        private const float Gravity = 9.81f;

        private static readonly Color AshColor = new Color(0.3f, 0.27f, 0.25f, 0.72f);
        private static readonly Color DropColor = new Color(0.86f, 0.93f, 1f, 0.75f);
        private static readonly Color EmberLow = new Color(1f, 0.42f, 0.12f);
        private static readonly Color EmberHigh = new Color(1f, 0.72f, 0.3f);

        /// <summary>The calm column: a few soft puffs a second, born over the vent, rising and widening, leaning with the
        /// wind under open sky, gone before a roof. Far vents breathe fewer, bigger puffs; it calms while the vent erupts.</summary>
        private void Breathe(Vent v, float dt, Vector3 wind)
        {
            float rate = SteamPerSecond * FxKit.Density * VentRules.SteamRateScale(v.Distance) * (v.EruptAge >= 0f ? 0.4f : 1f);
            v.SteamAcc = Mathf.Min(v.SteamAcc + rate * dt, 3f);
            if (v.SteamAcc < 1f)
            {
                return;
            }

            float far = Mathf.Lerp(1f, 1.7f, v.Distance / VentRules.SteamRange);
            var color = _volcanic ? Color.Lerp(_steam, AshColor, 0.3f) : _steam;
            color.a = 0.55f;
            Vector3 lean = v.Open ? wind * 0.4f : Vector3.zero;
            while (v.SteamAcc >= 1f)
            {
                v.SteamAcc -= 1f;
                var at = v.Base + new Vector3(Random.Range(-0.22f, 0.22f), 0.05f, Random.Range(-0.22f, 0.22f));
                var vel = new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(0.9f, 1.4f), Random.Range(-0.06f, 0.06f)) + lean;
                // The speed limit drags the puff to SmokeDrift within a second; the life ends before the ceiling.
                float life = Mathf.Min(Random.Range(3.6f, 5.2f), Mathf.Max(0.8f, (v.Headroom - 0.4f) / SmokeDrift));
                FxKit.Emit(FxKit.Kind.Smoke, at, vel, Random.Range(1.1f, 1.6f) * far, life, color); // ~0.5 m at birth, 2–3 m at the top
            }
        }

        /// <summary>An eruption starts: its length follows the strength; a whoosh, and on a volcanic vent a warm glow.</summary>
        private void StartEruption(Vent v, float strength)
        {
            v.Strength = strength;
            v.EruptAge = 0f;
            v.EruptLength = VentRules.EruptionSeconds(strength);
            v.PuffAcc = 1f;
            v.BitAcc = 1f;
            ClientAudio.Instance?.At("geyser_erupt", v.Base, Random.Range(0.9f, 1.08f), 0.5f + 0.45f * strength);
            if (_volcanic)
            {
                var glow = v.Base + Vector3.up * 1.2f;
                FxKit.Flash(glow, new Color(1f, 0.55f, 0.2f), (1.6f + 1.4f * strength) * FxKit.FlashScale, 0.5f);
                FxLights.Flash(glow, new Color(1f, 0.55f, 0.25f), 0.9f * strength, 7f, 0.9f);
            }
        }

        /// <summary>One frame of an eruption: billows and droplets/embers, strongest at the start and tapering off.</summary>
        private void Erupt(Vent v, float dt, Vector3 wind)
        {
            v.EruptAge += dt;
            float t = v.EruptAge / Mathf.Max(0.1f, v.EruptLength);
            if (t >= 1f)
            {
                v.EruptAge = -1f;
                return;
            }

            float surge = 1f - t * t; // a strong start that tapers
            float density = FxKit.Density;
            // The tallest throw the free room over the vent allows: v² = 2·g·h.
            float maxSpeed = Mathf.Sqrt(2f * Gravity * Mathf.Max(1f, v.Headroom - 0.5f));

            v.PuffAcc = Mathf.Min(v.PuffAcc + PuffsPerSecond * v.Strength * surge * density * dt, 4f);
            while (v.PuffAcc >= 1f)
            {
                v.PuffAcc -= 1f;
                Billow(v, surge, maxSpeed, wind);
            }

            v.BitAcc = Mathf.Min(v.BitAcc + BitsPerSecond * v.Strength * surge * density * dt, 6f);
            while (v.BitAcc >= 1f)
            {
                v.BitAcc -= 1f;
                if (_volcanic)
                {
                    Ember(v, maxSpeed);
                }
                else
                {
                    Droplet(v, maxSpeed);
                }
            }
        }

        /// <summary>A billowing puff thrown up fast: the Smoke emitter's speed limit drags it to a drift within a second or
        /// two while it swells — steam on a water geyser, dark ash on a volcanic vent.</summary>
        private void Billow(Vent v, float surge, float maxSpeed, Vector3 wind)
        {
            var at = v.Base + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0f, 0.3f), Random.Range(-0.3f, 0.3f));
            float speed = Mathf.Min(maxSpeed * 1.3f, Mathf.Lerp(7f, 15f, v.Strength) * Random.Range(0.75f, 1.1f) * (0.55f + 0.45f * surge));
            var vel = FxKit.RandomInCone(Vector3.up, 13f) * speed + (v.Open ? wind * 0.6f : Vector3.zero);
            Color color;
            if (_volcanic)
            {
                color = Color.Lerp(AshColor, new Color(0.45f, 0.42f, 0.4f, 0.65f), Random.value * 0.5f);
            }
            else
            {
                color = _steam;
                color.a = 0.6f;
            }

            float size = Random.Range(0.75f, 1.25f) * Mathf.Lerp(0.85f, 1.35f, v.Strength);
            FxKit.Emit(FxKit.Kind.Smoke, at, vel, size, Random.Range(3.2f, 5f), color);
        }

        /// <summary>A white water droplet arcing up and falling back, gone as it reaches the vent's height again.</summary>
        private static void Droplet(Vent v, float maxSpeed)
        {
            var at = v.Base + new Vector3(Random.Range(-0.15f, 0.15f), 0.1f, Random.Range(-0.15f, 0.15f));
            float speed = Mathf.Min(maxSpeed, Mathf.Lerp(8f, 14f, v.Strength) * Random.Range(0.7f, 1.05f));
            var vel = FxKit.RandomInCone(Vector3.up, 15f) * speed;
            float life = 2f * Mathf.Max(0.5f, vel.y) / Gravity * Random.Range(0.85f, 1f); // FxKit's Spray falls at 1 g
            FxKit.Emit(FxKit.Kind.Spray, at, vel, Random.Range(0.05f, 0.09f), life, DropColor);
        }

        /// <summary>A glowing ember flung out of a volcanic vent, falling back under gravity.</summary>
        private static void Ember(Vent v, float maxSpeed)
        {
            var at = v.Base + new Vector3(Random.Range(-0.15f, 0.15f), 0.1f, Random.Range(-0.15f, 0.15f));
            float speed = Mathf.Min(maxSpeed, Mathf.Lerp(5f, 11f, v.Strength) * Random.Range(0.7f, 1.05f));
            var vel = FxKit.RandomInCone(Vector3.up, 24f) * speed;
            float life = 2f * Mathf.Max(0.5f, vel.y) / (Gravity * 1.1f) * Random.Range(0.6f, 0.95f); // Sparks fall at 1.1 g
            FxKit.Emit(FxKit.Kind.Sparks, at, vel, Random.Range(0.05f, 0.09f), life, Color.Lerp(EmberLow, EmberHigh, Random.value));
        }
    }
}
