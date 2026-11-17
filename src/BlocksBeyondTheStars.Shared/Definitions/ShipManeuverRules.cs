// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>The manoeuvre a <c>ShipManeuverIntent</c> asks for (#3753, #3754).</summary>
public static class ShipManeuverKinds
{
    /// <summary>A barrel roll: the server opens the evade window and starts the cooldown.</summary>
    public const byte Roll = 0;

    /// <summary>The boost goes on or off (<c>ShipManeuverIntent.On</c>).</summary>
    public const byte Boost = 1;
}

/// <summary>What the flown ship can do with its two manoeuvres — worked out by the server from the ship's design and its
/// modules and sent to the pilot, so both sides run the same numbers.</summary>
public readonly struct ShipManeuverStats
{
    public ShipManeuverStats(float boostPower, float boostSeconds, float boostRechargeSeconds, float rollCooldown)
    {
        BoostPower = boostPower;
        BoostSeconds = boostSeconds;
        BoostRechargeSeconds = boostRechargeSeconds;
        RollCooldown = rollCooldown;
    }

    /// <summary>The factor on the top speed while the boost is on.</summary>
    public float BoostPower { get; }

    /// <summary>Seconds a full boost charge lasts.</summary>
    public float BoostSeconds { get; }

    /// <summary>Seconds an empty charge takes to fill.</summary>
    public float BoostRechargeSeconds { get; }

    /// <summary>Seconds between two barrel rolls.</summary>
    public float RollCooldown { get; }

    /// <summary>The values of a ship without any data: the balanced starter.</summary>
    public static ShipManeuverStats Default => ShipManeuverRules.Stats(null, 1f, null);
}

/// <summary>The boost charge (#3754): it drains while the boost is on and refills while it is off. One rule for the
/// server, which owns the charge, and the client, which shows it and flies by it.</summary>
public struct ShipBoostMeter
{
    /// <summary>Seconds of boost left.</summary>
    public float Charge;

    /// <summary>Whether the boost is on right now.</summary>
    public bool Active;

    /// <summary>A full charge, boost off — the state a flight starts with.</summary>
    public void Fill(in ShipManeuverStats stats)
    {
        Charge = stats.BoostSeconds;
        Active = false;
    }

    /// <summary>One step. <paramref name="want"/> is the pilot holding the boost: it starts only with at least
    /// <see cref="ShipManeuverRules.BoostStartShare"/> of a full charge, runs until the charge is empty or the pilot lets
    /// go, and the charge refills whenever the boost is off.</summary>
    public void Tick(float dt, bool want, in ShipManeuverStats stats)
    {
        if (dt <= 0f)
        {
            return;
        }

        if (!want)
        {
            Active = false;
        }
        else if (!Active && Charge >= stats.BoostSeconds * ShipManeuverRules.BoostStartShare)
        {
            Active = true;
        }

        if (Active)
        {
            Charge -= dt;
            if (Charge <= 0f)
            {
                Charge = 0f;
                Active = false;
            }

            return;
        }

        float refill = stats.BoostRechargeSeconds > 0f ? stats.BoostSeconds / stats.BoostRechargeSeconds : stats.BoostSeconds;
        Charge = Math.Min(stats.BoostSeconds, Charge + (dt * refill));
    }
}

/// <summary>
/// The numbers of the barrel roll and the boost (#3753, #3754) and of the speed check on reported ship poses. The server
/// applies them — the evade window, the cooldown, the charge, how far a ship may have flown — and the client flies and
/// draws by the same ones.
/// </summary>
public static class ShipManeuverRules
{
    /// <summary>A ship's top speed at <c>flightSpeed</c> 1, flight units per second.</summary>
    public const float BaseSpeed = 14f;

    /// <summary>Seconds the roll's turn about the nose takes.</summary>
    public const float RollSeconds = 0.6f;

    /// <summary>Seconds hostile fire misses a rolling ship. A little longer than the turn itself, so a roll started
    /// on the warning still covers a shot that lands as it ends.</summary>
    public const float RollEvadeSeconds = 0.75f;

    /// <summary>The roll cooldown of a ship with handling 1.</summary>
    public const float RollBaseCooldown = 4f;

    public const float RollCooldownMin = 1.5f;
    public const float RollCooldownMax = 8f;

    /// <summary>How far a roll slides the ship sideways, flight units, spread over the turn.</summary>
    public const float RollSlideDistance = 7f;

    /// <summary>The server lets a roll through this much before its own cooldown has run out — the client's clock and
    /// the network never agree to the millisecond.</summary>
    public const float RollCooldownSlack = 0.25f;

    public const float DefaultBoostPower = 1.7f;
    public const float DefaultBoostSeconds = 3f;
    public const float DefaultBoostRechargeSeconds = 7f;

    /// <summary>The share of a full charge a boost needs to start — an empty tank does not stutter on and off.</summary>
    public const float BoostStartShare = 0.25f;

    /// <summary>Module stat: seconds added to the boost charge (the afterburner).</summary>
    public const string StatBoostSeconds = "boost_seconds";

    /// <summary>Module stat: how much faster the charge refills, as a fraction (0.4 = 40 % faster).</summary>
    public const string StatBoostRecharge = "boost_recharge";

    /// <summary>Module stat: how much shorter the roll cooldown is, as a fraction (0.4 = 40 % shorter).</summary>
    public const string StatRollCooldown = "roll_cooldown";

    /// <summary>Seconds between two rolls for a ship of this handling, before modules.</summary>
    public static float RollCooldownFor(float handling)
        => Clamp(RollBaseCooldown / Math.Max(0.1f, handling), RollCooldownMin, RollCooldownMax);

    /// <summary>The manoeuvre values of a ship: its design's own numbers where the data sets them (0 = not set), else the
    /// defaults — the roll cooldown from the handling — with the fitted modules on top.</summary>
    public static ShipManeuverStats Stats(ShipDefinition? design, float handling, IEnumerable<ShipModuleDefinition>? modules)
    {
        float seconds = 0f;
        float recharge = 0f;
        float rollCut = 0f;
        if (modules != null)
        {
            foreach (var m in modules)
            {
                seconds += Stat(m, StatBoostSeconds);
                recharge += Stat(m, StatBoostRecharge);
                rollCut += Stat(m, StatRollCooldown);
            }
        }

        return Stats(design?.BoostPower ?? 0f, design?.BoostSeconds ?? 0f, design?.RollCooldown ?? 0f, handling, seconds, recharge, rollCut);
    }

    /// <summary>The manoeuvre values from plain numbers: the design's three values (0 = not set → the default, the
    /// roll cooldown from <paramref name="handling"/>) and the modules' three stats already summed (the server sums
    /// them in the pass it makes over the modules anyway).</summary>
    public static ShipManeuverStats Stats(float boostPower, float boostSeconds, float rollCooldown, float handling,
        float moduleBoostSeconds, float recharge, float rollCut)
    {
        float power = boostPower > 0f ? boostPower : DefaultBoostPower;
        float seconds = (boostSeconds > 0f ? boostSeconds : DefaultBoostSeconds) + moduleBoostSeconds;
        float roll = rollCooldown > 0f ? rollCooldown : RollCooldownFor(handling);

        return new ShipManeuverStats(
            Clamp(power, 1f, 3f),
            Clamp(seconds, 0.5f, 12f),
            DefaultBoostRechargeSeconds / (1f + Clamp(recharge, 0f, 3f)),
            Clamp(roll * (1f - Clamp(rollCut, 0f, 0.8f)), 1f, RollCooldownMax));
    }

    /// <summary>The manoeuvre values of a self-built ship (#949): a hull with much thrust for its mass boosts longer
    /// and harder; the roll comes from its handling like everyone's.</summary>
    public static void CustomBoost(float flightSpeed, out float boostPower, out float boostSeconds)
    {
        float t = Clamp((flightSpeed - 0.4f) / 1.4f, 0f, 1f); // 0.4 … 1.8, the band self-built ships fly in
        boostPower = 1.4f + (0.4f * t);
        boostSeconds = 2f + (2.5f * t);
    }

    /// <summary>How far a ship may have flown since its last accepted pose (#3754): its top speed — with the boost
    /// while the server has it on — times the time that passed, with slack for latency and a frame hitch and a small
    /// fixed margin. A long silence (a menu, a cut scene) counts for at most <see cref="TravelWindowSeconds"/>.</summary>
    public static float AllowedTravel(float flightSpeed, float boostPower, double elapsedSeconds)
    {
        float seconds = (float)Math.Min(Math.Max(elapsedSeconds, 0.0), TravelWindowSeconds);
        return (BaseSpeed * Math.Max(0.1f, flightSpeed) * Math.Max(1f, boostPower) * seconds * TravelSlack) + TravelMargin;
    }

    public const float TravelSlack = 1.4f;
    public const float TravelMargin = 6f;
    public const float TravelWindowSeconds = 5f;

    private static float Stat(ShipModuleDefinition m, string key)
        => m.Stats.TryGetValue(key, out double v) ? (float)v : 0f;

    private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
}
