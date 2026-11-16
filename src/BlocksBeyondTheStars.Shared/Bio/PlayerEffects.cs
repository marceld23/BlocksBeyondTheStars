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

    /// <summary>An effect of the same kind this one outshines for a while (#3728) — the night sight a player drank, under
    /// the stronger one of a lit lamp. It holds no place of its own, acts on nothing and is not sent to the client; it
    /// counts down underneath (<see cref="PlayerEffects.Tick"/>) and takes this one's place again when this one ends.
    /// Saved with the effect, so a player who logs out under a cover keeps what is left of the covered one.</summary>
    public ActiveEffect? Under { get; set; }

    public ActiveEffect Clone()
    {
        var copy = (ActiveEffect)MemberwiseClone();
        copy.Under = Under?.Clone();
        return copy;
    }
}

/// <summary>What happened when a preparation was taken.</summary>
public enum EffectAddResult
{
    Applied,   // a new effect started
    Refreshed, // the same effect, as strong or stronger, took the running one's place (see PlayerEffects.Add)
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

    /// <summary>Whether a running effect takes the weather — a heat- or a cold-sensitive one, or one that covers such an
    /// effect (<see cref="ActiveEffect.Under"/>). Only then does <see cref="Tick"/> read its temperature at all, so the
    /// server looks the temperature up for these players only (#2218).</summary>
    public static bool AnyThermal(IReadOnlyList<ActiveEffect>? effects)
    {
        // By index: this is asked every tick for every player under an effect, and must not cost an enumerator.
        for (int i = 0; effects is not null && i < effects.Count; i++)
        {
            if (effects[i].SecondsLeft > 0f
                && (effects[i].Thermal != BioThermal.Stable || effects[i].Under is { Thermal: not BioThermal.Stable }))
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
    /// <see cref="BioRules.ThermalDecay"/> times as fast in the heat, a cold-sensitive one in the cold. An effect under a
    /// cover (<see cref="ActiveEffect.Under"/>, #3728) counts down the same way: one that runs out there is gone, and
    /// what is left of it takes the cover's place when the cover's time is up. Returns true when an effect ended (the set
    /// changed). <paramref name="temperatureC"/> is the air the player is really in — the cabin's aboard a ship, the
    /// world's outside (#2218); it is not read when no effect is thermal (<see cref="AnyThermal"/>).</summary>
    public static bool Tick(List<ActiveEffect> effects, float dt, float temperatureC)
    {
        bool ended = false;
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            var e = effects[i];
            e.SecondsLeft -= Stressed(e, temperatureC) ? dt * BioRules.ThermalDecay : dt;
            if (e.Under is { } under)
            {
                under.SecondsLeft -= Stressed(under, temperatureC) ? dt * BioRules.ThermalDecay : dt;
                if (under.SecondsLeft <= 0f)
                {
                    e.Under = null; // ran out under the cover: it does not come back
                }
            }

            if (e.SecondsLeft <= 0f)
            {
                Uncover(effects, i);
                ended = true;
            }
        }

        return ended;
    }

    /// <summary>Starts <paramref name="cover"/> in the place of the running effect <paramref name="covered"/> of the same
    /// kind (#3728): the covered one stops acting and keeps counting down underneath, and <see cref="Uncover"/> — or the
    /// cover running out — brings back what is left of it. The two share one of the three places, so a cover never makes
    /// room for a fourth effect. An effect <paramref name="covered"/> covered itself stays the one underneath.</summary>
    public static void Cover(List<ActiveEffect> effects, ActiveEffect covered, ActiveEffect cover)
    {
        int at = effects.IndexOf(covered);
        if (at < 0 || covered.Effect != cover.Effect)
        {
            return;
        }

        cover.Under = covered.Under ?? covered;
        covered.Under = null;
        effects[at] = cover;
    }

    /// <summary>Ends the effect at <paramref name="index"/>: what it covered (<see cref="ActiveEffect.Under"/>) takes its
    /// place with the time it has left, side effect and all; without one the place is free.</summary>
    public static void Uncover(List<ActiveEffect> effects, int index)
    {
        if (effects[index].Under is { SecondsLeft: > 0f } back)
        {
            effects[index] = back;
        }
        else
        {
            effects.RemoveAt(index);
        }
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

    /// <summary>Starts an effect: the same kind never stacks — a stronger one replaces the running one and starts its
    /// own time; an equal one replaces it too (its side effect and its thermal behaviour count from now on) but keeps the
    /// longer of the two remaining times (#3727: taking the same again never shortens what is running); a weaker one is
    /// refused; a fourth kind is refused. An effect the replaced one covered (<see cref="ActiveEffect.Under"/>) goes with
    /// it — it is weaker than what replaces it.</summary>
    public static EffectAddResult Add(List<ActiveEffect> effects, ActiveEffect add)
    {
        var result = Check(effects, add.Effect, add.Level);
        if (result is EffectAddResult.Weaker or EffectAddResult.Full)
        {
            return result;
        }

        foreach (var e in effects)
        {
            if (e.Effect == add.Effect && e.Level == add.Level && e.SecondsLeft > add.SecondsLeft)
            {
                add.SecondsLeft = e.SecondsLeft;
            }
        }

        effects.RemoveAll(e => e.Effect == add.Effect || e.SecondsLeft <= 0f);
        effects.Add(add);
        return result;
    }
}
