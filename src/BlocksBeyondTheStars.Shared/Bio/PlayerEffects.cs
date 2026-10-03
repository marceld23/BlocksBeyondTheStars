// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>One effect a player is under: what, how strong, how long still, and the catch that rides along.</summary>
public sealed class ActiveEffect
{
    public BioEffect Effect { get; set; }
    public int Level { get; set; }
    public float SecondsLeft { get; set; }
    public BioSideEffect Side { get; set; }
    public int SideLevel { get; set; }
    public BioThermal Thermal { get; set; }

    public ActiveEffect Clone() => (ActiveEffect)MemberwiseClone();
}

/// <summary>What happened when a preparation was taken.</summary>
public enum EffectAddResult
{
    Applied,   // a new effect started
    Refreshed, // the same effect, as strong or stronger, started over
    Weaker,    // a stronger one of the same kind is running — nothing is used up
    Full,      // three effects are running already — nothing is used up
}

/// <summary>
/// The status effects of a player, as one set of formulas for both sides — the pattern of
/// <see cref="BlocksBeyondTheStars.Shared.State.SuitEquipment"/>: the server applies them to vitals and combat, the
/// client to movement, and both show the same numbers. An effect joins the gear formula of its stat and shares that
/// formula's cap, so a preparation never lifts a player above what the best gear allows.
/// </summary>
public static class PlayerEffects
{
    private static readonly IReadOnlyList<ActiveEffect> Empty = Array.Empty<ActiveEffect>();

    /// <summary>The magnitude of an effect the player is under (0 when it is not running).</summary>
    public static float Of(IReadOnlyList<ActiveEffect>? effects, BioEffect effect)
    {
        foreach (var e in effects ?? Empty)
        {
            if (e.Effect == effect && e.SecondsLeft > 0f)
            {
                return BioRules.Magnitude(effect, e.Level);
            }
        }

        return 0f;
    }

    /// <summary>The cost of a side effect: the worst of the running ones (catches never add up).</summary>
    public static float Side(IReadOnlyList<ActiveEffect>? effects, BioSideEffect side)
    {
        float worst = 0f;
        foreach (var e in effects ?? Empty)
        {
            if (e.Side == side && e.SecondsLeft > 0f)
            {
                worst = Math.Max(worst, BioRules.SideMagnitude(side, e.SideLevel));
            }
        }

        return worst;
    }

    /// <summary>Walking speed factor: faster under Speed, slower under Sluggish and under heavy gear.</summary>
    public static float MoveFactor(IReadOnlyList<ActiveEffect>? effects, float gearWeight = 0f)
        => (1f + Of(effects, BioEffect.Speed)) * (1f - Side(effects, BioSideEffect.Sluggish)) * (1f - Math.Clamp(gearWeight, 0f, ItemMods.MaxWeight));

    /// <summary>Jump height factor.</summary>
    public static float JumpFactor(IReadOnlyList<ActiveEffect>? effects)
        => (1f + Of(effects, BioEffect.Jump)) * (1f - Side(effects, BioSideEffect.Leaden));

    /// <summary>Damage factor of a melee hit.</summary>
    public static float MeleeFactor(IReadOnlyList<ActiveEffect>? effects) => 1f + Of(effects, BioEffect.Strength);

    /// <summary>Mining power factor.</summary>
    public static float MiningFactor(IReadOnlyList<ActiveEffect>? effects) => 1f + Of(effects, BioEffect.Mining);

    /// <summary>Cooldown factor of tools and weapons (below 1 = ready sooner).</summary>
    public static float CooldownFactor(IReadOnlyList<ActiveEffect>? effects)
        => (1f - Of(effects, BioEffect.Reflex)) * (1f + Side(effects, BioSideEffect.SlowReflex));

    /// <summary>Oxygen drain factor.</summary>
    public static float OxygenDrainFactor(IReadOnlyList<ActiveEffect>? effects)
        => (1f - Of(effects, BioEffect.Breath)) * (1f + Side(effects, BioSideEffect.Breathless));

    /// <summary>Hunger drain factor.</summary>
    public static float HungerDrainFactor(IReadOnlyList<ActiveEffect>? effects)
        => (1f - Of(effects, BioEffect.Satiety)) * (1f + Side(effects, BioSideEffect.Hungry));

    /// <summary>The share of natural healing that still works (0 under a strong Tired).</summary>
    public static float NaturalHealingFactor(IReadOnlyList<ActiveEffect>? effects) => 1f - Side(effects, BioSideEffect.Tired);

    /// <summary>Thermal insulation an effect adds on top of the gear, against heat or against cold; the caller caps the sum.</summary>
    public static float ThermalBonus(IReadOnlyList<ActiveEffect>? effects, bool hot)
        => Of(effects, hot ? BioEffect.HeatWard : BioEffect.ColdWard);

    /// <summary>Whether a running effect takes the weather — a heat- or a cold-sensitive one. Only then does
    /// <see cref="Tick"/> read its temperature at all, so the server looks the temperature up for these players only
    /// (#2218).</summary>
    public static bool AnyThermal(IReadOnlyList<ActiveEffect>? effects)
    {
        // By index: this is asked every tick for every player under an effect, and must not cost an enumerator.
        for (int i = 0; effects is not null && i < effects.Count; i++)
        {
            if (effects[i].Thermal != BioThermal.Stable && effects[i].SecondsLeft > 0f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a running effect runs out faster at this temperature right now — a heat-sensitive one in the heat,
    /// a cold-sensitive one in the cold. While that lasts a client that counts the seconds down by itself drifts, so the
    /// server tells it the true time now and then.</summary>
    public static bool AnyStressed(IReadOnlyList<ActiveEffect>? effects, float temperatureC)
    {
        for (int i = 0; effects is not null && i < effects.Count; i++)
        {
            if (effects[i].SecondsLeft > 0f && Stressed(effects[i], temperatureC))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Stressed(ActiveEffect e, float temperatureC)
        => (e.Thermal == BioThermal.HeatSensitive && temperatureC > BioRules.HotAbove)
           || (e.Thermal == BioThermal.ColdSensitive && temperatureC < BioRules.ColdBelow);

    /// <summary>Drops the effects whose time is up and counts the rest down. A heat-sensitive effect runs out
    /// <see cref="BioRules.ThermalDecay"/> times as fast in the heat, a cold-sensitive one in the cold. Returns true when
    /// an effect ended (the set changed). <paramref name="temperatureC"/> is the air the player is really in — the cabin's
    /// aboard a ship, the world's outside (#2218); it is not read when no effect is thermal (<see cref="AnyThermal"/>).</summary>
    public static bool Tick(List<ActiveEffect> effects, float dt, float temperatureC)
    {
        bool ended = false;
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            var e = effects[i];
            e.SecondsLeft -= Stressed(e, temperatureC) ? dt * BioRules.ThermalDecay : dt;
            if (e.SecondsLeft <= 0f)
            {
                effects.RemoveAt(i);
                ended = true;
            }
        }

        return ended;
    }

    /// <summary>Whether <paramref name="add"/> could start, without changing anything.</summary>
    public static EffectAddResult Check(IReadOnlyList<ActiveEffect> effects, BioEffect effect, int level)
    {
        int running = 0;
        foreach (var e in effects)
        {
            if (e.SecondsLeft <= 0f)
            {
                continue;
            }

            if (e.Effect == effect)
            {
                return level >= e.Level ? EffectAddResult.Refreshed : EffectAddResult.Weaker;
            }

            running++;
        }

        return running >= BioRules.MaxActiveEffects ? EffectAddResult.Full : EffectAddResult.Applied;
    }

    /// <summary>Starts an effect: the same kind never stacks — a stronger or equal one replaces, a weaker one is refused;
    /// a fourth kind is refused.</summary>
    public static EffectAddResult Add(List<ActiveEffect> effects, ActiveEffect add)
    {
        var result = Check(effects, add.Effect, add.Level);
        if (result is EffectAddResult.Weaker or EffectAddResult.Full)
        {
            return result;
        }

        effects.RemoveAll(e => e.Effect == add.Effect || e.SecondsLeft <= 0f);
        effects.Add(add);
        return result;
    }
}
