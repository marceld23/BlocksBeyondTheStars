// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// What makes a ship module a weapon and how it behaves (#3757, #3758) — read from the module's free-form stats in
/// <c>data/ship_modules.json</c>, so a new weapon is data. The server resolves every shot by these; the client uses the
/// same reads to build its ship-systems bar and to draw what the server will do.
/// <list type="bullet">
/// <item><c>weapon_damage</c> — the module is a weapon (with <c>weapon_class</c>, <c>weapon_range</c>,
/// <c>weapon_cooldown</c>, <c>weapon_energy</c> as before).</item>
/// <item><c>weapon_homing</c> 1 — the shot finds its target by itself: it is fired at the pilot's locked target and the
/// nose need not point at it (the missile launcher).</item>
/// <item><c>weapon_flight_speed</c> — the shot flies at this speed and hits on arrival instead of at once.</item>
/// <item><c>weapon_targets</c> + <c>weapon_cone</c> — one shot hits up to that many targets within that many degrees of
/// the aimed one (the scatter cannon).</item>
/// <item><c>weapon_stun</c> — seconds the hit target neither moves nor fires (the ion pulse).</item>
/// <item><c>weapon_auto</c> 1 — the weapon fires by itself at the nearest hostile in range (the point-defence turret);
/// it takes no place in the ship-systems bar.</item>
/// </list>
/// </summary>
public static class ShipWeaponRules
{
    public const string StatDamage = "weapon_damage";
    public const string StatClass = "weapon_class";
    public const string StatRange = "weapon_range";
    public const string StatCooldown = "weapon_cooldown";
    public const string StatEnergy = "weapon_energy";
    public const string StatHoming = "weapon_homing";
    public const string StatFlightSpeed = "weapon_flight_speed";
    public const string StatTargets = "weapon_targets";
    public const string StatCone = "weapon_cone";
    public const string StatStun = "weapon_stun";
    public const string StatAuto = "weapon_auto";

    /// <summary>After an ion pulse wore off, the target cannot be stunned again for this long (it can still be hit).</summary>
    public const float StunImmuneSeconds = 2.5f;

    /// <summary>Whether the module is a ship weapon at all.</summary>
    public static bool IsWeapon(ShipModuleDefinition? def) => def != null && def.Stats.ContainsKey(StatDamage);

    /// <summary>Whether the module fires by itself (the turret).</summary>
    public static bool IsAuto(ShipModuleDefinition? def) => Stat(def, StatAuto) > 0.0;

    /// <summary>Whether the pilot picks and fires the module from the ship-systems bar: every weapon but the turret.</summary>
    public static bool IsSelectable(ShipModuleDefinition? def) => IsWeapon(def) && !IsAuto(def);

    /// <summary>Whether the shot finds its target by itself (fired at the locked target, no boresight needed).</summary>
    public static bool IsHoming(ShipModuleDefinition? def) => Stat(def, StatHoming) > 0.0;

    /// <summary>Seconds a shot of this module needs to reach a target <paramref name="distance"/> away; 0 = it hits at once.</summary>
    public static float FlightSeconds(ShipModuleDefinition? def, float distance)
    {
        double speed = Stat(def, StatFlightSpeed);
        return speed > 0.0 ? (float)(Math.Max(0f, distance) / speed) : 0f;
    }

    /// <summary>How many targets one shot hits (1 for every ordinary weapon).</summary>
    public static int Targets(ShipModuleDefinition? def) => Math.Max(1, (int)Stat(def, StatTargets));

    /// <summary>The cosine of the scatter cone's half angle: another target counts when the direction to it and the
    /// direction to the aimed target are at least this aligned.</summary>
    public static float ConeCos(ShipModuleDefinition? def)
    {
        double degrees = Stat(def, StatCone);
        return degrees > 0.0 ? (float)Math.Cos(Math.Min(degrees, 180.0) * Math.PI / 180.0) : 1f;
    }

    /// <summary>Whether a second target lies inside the scatter cone around the aimed one — both directions from the
    /// ship, not normalised.</summary>
    public static bool InCone(float ax, float ay, float az, float bx, float by, float bz, float coneCos)
    {
        double la = Math.Sqrt((ax * ax) + (ay * ay) + (az * az));
        double lb = Math.Sqrt((bx * bx) + (by * by) + (bz * bz));
        if (la < 1e-4 || lb < 1e-4)
        {
            return true; // on top of the ship: it is in every cone
        }

        return ((ax * bx) + (ay * by) + (az * bz)) / (la * lb) >= coneCos;
    }

    /// <summary>A stat of the module, 0 when it has none.</summary>
    public static double Stat(ShipModuleDefinition? def, string key)
        => def != null && def.Stats.TryGetValue(key, out double v) ? v : 0.0;
}
