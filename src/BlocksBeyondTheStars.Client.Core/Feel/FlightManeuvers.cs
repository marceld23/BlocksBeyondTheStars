// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// The barrel roll as the flight view plays it (#3753): one full turn about the ship's nose in
    /// <see cref="ShipManeuverRules.RollSeconds"/>, eased in and out, with a sideways slide spread over the same time.
    /// The server decides whether the roll evades anything; this is the picture, the client's own cooldown clock (so the
    /// key does nothing while the server would refuse) and the evade window the client draws misses in.
    /// </summary>
    public struct ShipRollState
    {
        private float _time;      // seconds into the turn; at or past RollSeconds = not rolling
        private float _evadeLeft;

        /// <summary>+1 = to the right, -1 = to the left.</summary>
        public int Direction { get; private set; }

        /// <summary>Seconds until the next roll may start.</summary>
        public float CooldownLeft { get; private set; }

        /// <summary>Whether the turn is running.</summary>
        public bool Rolling => Direction != 0 && _time < ShipManeuverRules.RollSeconds;

        /// <summary>Whether hostile fire misses right now (the server's window, counted from the same press).</summary>
        public bool Evading => _evadeLeft > 0f;

        /// <summary>Starts a roll when none runs and the cooldown has passed. <paramref name="direction"/>: negative =
        /// left, anything else = right.</summary>
        public bool TryStart(int direction, float cooldown)
        {
            if (Rolling || CooldownLeft > 0f)
            {
                return false;
            }

            Direction = direction < 0 ? -1 : 1;
            _time = 0f;
            _evadeLeft = ShipManeuverRules.RollEvadeSeconds;
            CooldownLeft = Math.Max(0f, cooldown);
            return true;
        }

        /// <summary>One frame. Returns the share of the slide to move this frame (0…1 over the whole roll).</summary>
        public float Tick(float dt)
        {
            if (dt <= 0f)
            {
                return 0f;
            }

            CooldownLeft = Math.Max(0f, CooldownLeft - dt);
            _evadeLeft = Math.Max(0f, _evadeLeft - dt);
            if (!Rolling)
            {
                return 0f;
            }

            float before = Ease(_time / ShipManeuverRules.RollSeconds);
            _time = Math.Min(ShipManeuverRules.RollSeconds, _time + dt);
            return Ease(_time / ShipManeuverRules.RollSeconds) - before;
        }

        /// <summary>The turn so far, degrees about the nose: 0 at the start, 360 at the end, positive = right wing down.</summary>
        public float Angle => Rolling ? Direction * 360f * Ease(_time / ShipManeuverRules.RollSeconds) : 0f;

        /// <summary>How ready the next roll is, 0 (just rolled) … 1 (ready), for the HUD.</summary>
        public float Readiness(float cooldown) => cooldown <= 0f ? 1f : 1f - Math.Min(1f, CooldownLeft / cooldown);

        /// <summary>Forgets the roll and its cooldown (a new flight).</summary>
        public void Reset()
        {
            Direction = 0;
            _time = 0f;
            _evadeLeft = 0f;
            CooldownLeft = 0f;
        }

        /// <summary>Smoothstep: the turn starts and ends softly.</summary>
        public static float Ease(float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - (2f * t));
        }
    }

    /// <summary>What the flight view fires with (#3757, #3758) — the rules of the ship-systems bar and of a shot's
    /// picture, read from the modules' data so a new weapon needs no client code.</summary>
    public static class ShipWeaponView
    {
        /// <summary>Whether a fitted module gets a place in the ship-systems bar: every weapon the pilot fires (the
        /// point-defence turret fires by itself).</summary>
        public static bool InSystemsBar(ShipModuleDefinition? def) => ShipWeaponRules.IsSelectable(def);

        /// <summary>The <c>weapon_class</c> of a module (0 mining tool, 1 combat, 2 both); 1 when the data has none.</summary>
        public static int WeaponClass(ShipModuleDefinition? def)
            => def != null && def.Stats.TryGetValue(ShipWeaponRules.StatClass, out double c) ? (int)c : 1;

        /// <summary>Whether the module may fire on a station: a combat weapon (a mining tool never does). An intact
        /// station's shield takes the shot, a derelict's hull breaks.</summary>
        public static bool FiresOnStations(ShipModuleDefinition? def) => ShipWeaponRules.IsWeapon(def) && WeaponClass(def) != 0;
    }
}
