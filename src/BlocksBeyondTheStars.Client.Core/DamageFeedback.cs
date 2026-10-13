// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One damage effect for the HUD (#2518): how strongly each part of the hit feedback fires. Every value
    /// scales with the share of max health the hit took, with no fixed minimum — chip damage stays a faint edge glow.</summary>
    public readonly struct DamagePulse
    {
        public DamagePulse(float damage, float share, float flashSeconds, float vignette, float visorGlitch, float trauma)
        {
            Damage = damage;
            Share = share;
            FlashSeconds = flashSeconds;
            Vignette = vignette;
            VisorGlitch = visorGlitch;
            Trauma = trauma;
        }

        /// <summary>Health lost in this pulse, in HP.</summary>
        public float Damage { get; }

        /// <summary><see cref="Damage"/> as a share of max health, 0..1.</summary>
        public float Share { get; }

        /// <summary>Seconds of the full-screen red flash; 0 for a hit below <see cref="DamageFeedback.FlashShare"/>.</summary>
        public float FlashSeconds { get; }

        /// <summary>True when the hit is big enough for the full-screen red flash.</summary>
        public bool FullScreenFlash => FlashSeconds > 0f;

        /// <summary>Strength of the red edge vignette kick, 0..1.</summary>
        public float Vignette { get; }

        /// <summary>Strength of the visor hologram glitch, 0..1.</summary>
        public float VisorGlitch { get; }

        /// <summary>Camera-shake trauma to add, 0..<see cref="DamageFeedback.MaxTrauma"/>.</summary>
        public float Trauma { get; }
    }

    /// <summary>
    /// Turns the player's health stream into damage effects (#2518). The server sends health at the tick rate
    /// (15 Hz), so continuous damage — a scan drone's aura, a bite, lava — arrives as a drop on every update. Firing
    /// the full effect on each of them stacked the camera shake to its maximum and left a steady red layer on the
    /// screen; a drop below a fixed threshold (armour) fired nothing at all.
    /// <para>Instead the drops are summed. The first damage after a quiet spell fires at once (a hit must feel
    /// immediate) and opens a <see cref="WindowSeconds"/> window; damage inside the window is held and fires as ONE
    /// effect when it closes, so continuous damage pulses about twice a second. A single update that takes at least
    /// <see cref="FlashShare"/> of max health breaks through an open window — a real hit is never delayed. Health
    /// gains (healing, respawn) never fire and never cancel held damage.</para>
    /// </summary>
    public sealed class DamageFeedback
    {
        /// <summary>The player's full health (PlayerState.Health starts at and is capped to 100).</summary>
        public const float PlayerMaxHealth = 100f;

        /// <summary>Damage inside this many seconds after an effect is merged into one effect.</summary>
        public const float WindowSeconds = 0.5f;

        /// <summary>Share of max health from which a hit flashes the whole screen red; smaller damage only tints the
        /// screen edge.</summary>
        public const float FlashShare = 0.05f;

        /// <summary>Most camera-shake trauma that damage may build up (an explosion nearby adds its own on top).</summary>
        public const float MaxTrauma = 0.3f;

        /// <summary>Held damage below this many HP never fires (float noise in the health stream).</summary>
        public const float NoiseHp = 0.01f;

        /// <summary>Share of max health at which each effect reaches its full strength.</summary>
        private const float VignetteFullShare = 0.2f;
        private const float GlitchFullShare = 0.25f;
        private const float TraumaFullShare = 0.25f;
        private const float FlashFullShare = 0.25f;
        private const float FlashMinSeconds = 0.25f;
        private const float FlashMaxSeconds = 0.6f;

        private float _prevHealth;
        private bool _hasPrev;
        private float _pending;
        private float _windowLeft;

        /// <summary>HP of damage held in the open window, not yet fired.</summary>
        public float PendingDamage => _pending;

        /// <summary>Feeds this frame's health. Returns true with the effect to play when damage fires now.</summary>
        /// <param name="health">The player's current health.</param>
        /// <param name="maxHealth">The player's full health (a non-positive value means <see cref="PlayerMaxHealth"/>).</param>
        /// <param name="dt">Seconds since the last call.</param>
        /// <param name="pulse">The effect to play, or default when nothing fires.</param>
        public bool Update(float health, float maxHealth, float dt, out DamagePulse pulse)
        {
            pulse = default;
            if (!_hasPrev)
            {
                Reset(health); // the first sample is the baseline, not a hit
                return false;
            }

            float max = maxHealth > 0f ? maxHealth : PlayerMaxHealth;
            float drop = _prevHealth - health;
            _prevHealth = health;
            if (drop > 0f)
            {
                _pending += drop;
            }

            if (_windowLeft > 0f)
            {
                _windowLeft -= Math.Max(0f, dt);
            }

            bool bigHit = drop >= (FlashShare * max) - 1e-4f;
            if (_pending < NoiseHp || (_windowLeft > 0f && !bigHit))
            {
                return false;
            }

            pulse = Scale(_pending, max);
            _pending = 0f;
            _windowLeft = WindowSeconds;
            return true;
        }

        /// <summary>Re-baselines on <paramref name="health"/> and drops held damage without an effect (respawn, a new
        /// world).</summary>
        public void Reset(float health)
        {
            _prevHealth = health;
            _hasPrev = true;
            _pending = 0f;
            _windowLeft = 0f;
        }

        /// <summary>The effect for <paramref name="damage"/> HP out of <paramref name="maxHealth"/>: every part grows with
        /// the share of max health and is 0 for no damage — no fixed minimums. The vignette follows the square root of the
        /// share, so chip damage still shows a faint edge glow while the flash and the shake stay away.</summary>
        public static DamagePulse Scale(float damage, float maxHealth)
        {
            float max = maxHealth > 0f ? maxHealth : PlayerMaxHealth;
            float dmg = Math.Max(0f, damage);
            float share = Clamp01(dmg / max);
            float flash = share >= FlashShare - 1e-6f
                ? FlashMinSeconds + ((FlashMaxSeconds - FlashMinSeconds) * Clamp01((share - FlashShare) / (FlashFullShare - FlashShare)))
                : 0f;
            float vignette = Clamp01((float)Math.Sqrt(share / VignetteFullShare));
            float glitch = Clamp01(share / GlitchFullShare);
            float trauma = MaxTrauma * Clamp01(share / TraumaFullShare);
            return new DamagePulse(dmg, share, flash, vignette, glitch, trauma);
        }

        /// <summary>Adds damage trauma to the camera's <paramref name="current"/> trauma without pushing it past
        /// <paramref name="cap"/>. Trauma that is already higher (an explosion) is kept, never lowered.</summary>
        public static float AddCappedTrauma(float current, float add, float cap = MaxTrauma)
        {
            if (add <= 0f || current >= cap)
            {
                return current;
            }

            return Math.Min(cap, current + add);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
