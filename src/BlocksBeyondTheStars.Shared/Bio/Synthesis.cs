// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>What the lab made: a compound in a form. The whole result rides in the preparation's item key
/// (<see cref="ToPayload"/>), so equal results stack and a tooltip needs no question to the server. Levels are stored,
/// never magnitudes — the numbers behind a level stay in <see cref="BioRules"/>.</summary>
public sealed class Compound
{
    /// <summary>Hex digits of the key payload.</summary>
    public const int PayloadLength = 10;

    public BioForm Form { get; set; }
    public BioEffect Effect { get; set; }
    public int Level { get; set; }
    public BioSideEffect Side { get; set; }
    public int SideLevel { get; set; }
    public BioThermal Thermal { get; set; }
    public int DurationSeconds { get; set; }
    public BioEffect Secondary { get; set; }
    public int SecondaryLevel { get; set; }

    /// <summary>0..100. Not part of the key — it decided the values above.</summary>
    public int Stability { get; set; }

    /// <summary>What the two samples did to each other.</summary>
    public BioReaction Reaction { get; set; }

    /// <summary>True when the mix fell apart (stability below <see cref="BioRules.FailStability"/>): nothing is made.</summary>
    public bool Failed { get; set; }

    /// <summary>The coined name of the compound — the same for the same result.</summary>
    public string Name => BioNames.Compound(Signature());

    /// <summary>The ten hex digits a preparation's key carries after the <c>x</c> tag.</summary>
    public string ToPayload()
    {
        int units = Math.Clamp(DurationSeconds / BioRules.DurationUnit, 0, 255);
        return ((int)Effect).ToString("x2", CultureInfo.InvariantCulture)
            + (Level & 0xF).ToString("x1", CultureInfo.InvariantCulture)
            + ((int)Side & 0xF).ToString("x1", CultureInfo.InvariantCulture)
            + ((SideLevel & 0x3) | (((int)Thermal & 0x3) << 2)).ToString("x1", CultureInfo.InvariantCulture)
            + units.ToString("x2", CultureInfo.InvariantCulture)
            + ((int)Secondary).ToString("x2", CultureInfo.InvariantCulture)
            + (SecondaryLevel & 0xF).ToString("x1", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a key payload back; null for one that is no compound of this version — a malformed one, a hand-typed key (a
    /// cheat, an edited save), a key of a newer version. Whatever <see cref="Synthesis.Compute"/> makes reads back exactly
    /// as it was made. What the lab could never make is refused: an effect, a side effect or a thermal value this version
    /// does not know, a level outside 1..<see cref="BioRules.MaxLevel"/>, a second effect without a level (or a level
    /// without one). A duration beyond the lab's limits is not refused but cut back to them, the stealth limit included —
    /// so no key ever lasts longer than the best mix.
    /// </summary>
    public static Compound? FromPayload(BioForm form, string? payload)
    {
        if (payload is null || payload.Length != PayloadLength
            || !ulong.TryParse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong v))
        {
            return null;
        }

        int nibble4 = (int)((v >> 20) & 0xF);
        var effect = (BioEffect)((v >> 32) & 0xFF);
        int level = (int)((v >> 28) & 0xF);
        var side = (BioSideEffect)((v >> 24) & 0xF);
        var thermal = (BioThermal)((nibble4 >> 2) & 0x3);
        var secondary = (BioEffect)((v >> 4) & 0xFF);
        int secondaryLevel = (int)(v & 0xF);

        bool secondaryValid = secondary == BioEffect.None
            ? secondaryLevel == 0
            : BioRules.IsKnown(secondary) && secondaryLevel >= 1 && secondaryLevel <= BioRules.MaxLevel;
        if (effect == BioEffect.None || !BioRules.IsKnown(effect) || level < 1 || level > BioRules.MaxLevel
            || !BioRules.IsKnown(side) || !BioRules.IsKnown(thermal) || !secondaryValid)
        {
            return null;
        }

        int duration = Math.Clamp((int)((v >> 12) & 0xFF) * BioRules.DurationUnit, BioRules.MinDuration, BioRules.MaxDuration);
        if (effect == BioEffect.Stealth || secondary == BioEffect.Stealth)
        {
            duration = Math.Min(duration, BioRules.StealthMaxDuration);
        }

        return new Compound
        {
            Form = form,
            Effect = effect,
            Level = level,
            Side = side,
            SideLevel = nibble4 & 0x3, // two bits: always 0..3
            Thermal = thermal,
            DurationSeconds = duration,
            Secondary = secondary,
            SecondaryLevel = secondaryLevel,
        };
    }

    private ulong Signature()
        => ((ulong)Effect << 40) | ((ulong)(uint)Level << 32) | ((ulong)Side << 24) | ((ulong)(uint)SideLevel << 20)
           | ((ulong)Secondary << 8) | (uint)SecondaryLevel;
}

/// <summary>What goes into the lab's mixer.</summary>
public sealed class SynthesisInput
{
    /// <summary>The sample that gives the effect. Required.</summary>
    public BioProfile Active { get; set; } = new();

    /// <summary>True when a detoxifier stood by and washed the active sample (its toxicity then counts as 0).</summary>
    public bool ActiveCleaned { get; set; }

    /// <summary>The form the carrier makes.</summary>
    public BioForm Form { get; set; }

    /// <summary>The mineral that steadies the mix; null = none.</summary>
    public MaterialProfile? Stabiliser { get; set; }

    /// <summary>The second sample that reacts with the first; null = none.</summary>
    public BioProfile? Modifier { get; set; }
    public bool ModifierCleaned { get; set; }
}

/// <summary>
/// The lab's mixer: active substance + carrier + stabiliser + optional modifier → a compound. Computed, not looked up:
/// there is no recipe list, and the same inputs always give the same result — an experiment is never a gamble.
/// </summary>
public static class Synthesis
{
    /// <summary>Computes the compound. A failed mix (<see cref="Compound.Failed"/>) still reports its stability and
    /// reaction, so the research book can remember it.</summary>
    public static Compound Compute(SynthesisInput input)
    {
        var active = input.Active;
        var modifier = input.Modifier;
        var stabiliser = input.Stabiliser;
        int toxicity = input.ActiveCleaned ? 0 : active.Toxicity;
        int modifierToxicity = modifier is null || input.ModifierCleaned ? 0 : modifier.Toxicity;

        var reaction = modifier is null ? BioReaction.Neutral : BioRules.Reaction(active.Group, modifier.Group);

        // Stability: how well the things in the mixer get along.
        int stability = 50;
        stability += modifier is null ? 10 : reaction switch
        {
            BioReaction.Amplify => 10,
            BioReaction.Couple => 5,
            BioReaction.Transmute => -10,
            BioReaction.Inhibit => -15,
            _ => 0,
        };
        if (stabiliser is not null)
        {
            stability += 8 + 4 * stabiliser.Purity - 10 * stabiliser.LevelOf(MatTrait.Unstable);
        }

        if (BioRules.PreferredForm(active.Carrier) == input.Form)
        {
            stability += 8;
        }

        stability -= 12 * toxicity + 6 * modifierToxicity;
        stability = Math.Clamp(stability, 0, 100);

        var compound = new Compound { Form = input.Form, Stability = stability, Reaction = reaction };
        if (stability < BioRules.FailStability)
        {
            compound.Failed = true;
            return compound;
        }

        // The effect and its strength.
        var effect = reaction == BioReaction.Transmute ? BioRules.Transmuted(active.Effect) : active.Effect;
        int level = active.Level
            + (reaction == BioReaction.Amplify ? 2 : reaction == BioReaction.Inhibit ? -2 : 0)
            - toxicity;

        // The catch — and the stabiliser's trade: it takes the catch away for about a quarter of the strength.
        var side = active.Side;
        int sideLevel = active.SideLevel;
        if (side != BioSideEffect.None && stabiliser is not null && stabiliser.LevelOf(BioRules.Counter(side)) > 0)
        {
            side = BioSideEffect.None;
            sideLevel = 0;
            level -= Math.Max(1, level / 4);
        }
        else if (side != BioSideEffect.None && stability > BioRules.CleanStability)
        {
            sideLevel--;
            if (sideLevel <= 0)
            {
                side = BioSideEffect.None;
                sideLevel = 0;
            }
        }

        if (side == BioSideEffect.None && toxicity > 0)
        {
            side = BioSideEffect.Tired; // an unwashed toxic sample always leaves its mark
            sideLevel = Math.Min(3, toxicity);
        }

        var (num, den, durationPercent) = BioRules.FormFactors(input.Form);
        level = Math.Clamp(CeilDiv(Math.Max(1, level) * num, den), 1, BioRules.MaxLevel);

        // A coupled second effect at half the modifier's strength; the same effect twice just adds a level.
        var secondary = BioEffect.None;
        int secondaryLevel = 0;
        if (reaction == BioReaction.Couple && modifier is not null)
        {
            if (modifier.Effect == effect)
            {
                level = Math.Min(BioRules.MaxLevel, level + 1);
            }
            else
            {
                secondary = modifier.Effect;
                secondaryLevel = Math.Clamp(CeilDiv(CeilDiv(modifier.Level, 2) * num, den), 1, BioRules.MaxLevel);
            }
        }

        // How long it lasts: the substance, the form, and a steadier mix lasts longer.
        int duration = active.DurationSeconds * durationPercent / 100 * (75 + stability / 2) / 100;
        duration = Math.Clamp(duration, BioRules.MinDuration, BioRules.MaxDuration);
        if (effect == BioEffect.Stealth || secondary == BioEffect.Stealth)
        {
            duration = Math.Min(BioRules.StealthMaxDuration, Math.Max(BioRules.MinDuration, duration / 4));
        }

        duration = Math.Max(BioRules.DurationUnit, duration / BioRules.DurationUnit * BioRules.DurationUnit);

        // A heat-proof stabiliser steadies a heat-sensitive substance, a cold-proof one a cold-sensitive one.
        var thermal = active.Thermal;
        if (stabiliser is not null
            && ((thermal == BioThermal.HeatSensitive && stabiliser.LevelOf(MatTrait.HeatProof) > 0)
                || (thermal == BioThermal.ColdSensitive && stabiliser.LevelOf(MatTrait.ColdProof) > 0)))
        {
            thermal = BioThermal.Stable;
        }

        compound.Effect = effect;
        compound.Level = level;
        compound.Side = side;
        compound.SideLevel = sideLevel;
        compound.Thermal = thermal;
        compound.DurationSeconds = duration;
        compound.Secondary = secondary;
        compound.SecondaryLevel = secondaryLevel;
        return compound;
    }

    /// <summary>
    /// The key of an experiment in a player's research book: what went into the mixer. Known signatures are shown with
    /// their result <i>before</i> mixing; an unknown one reads "reaction unknown".
    /// <para>
    /// <paramref name="washed"/> is part of what went in (#2216): a detoxifier that washed a toxic sample gives another
    /// result than the same samples unwashed, so the washed mix is its own experiment — the same string with a
    /// <c>/w</c> at its end. Only a mix that really was washed carries it; a mix without a toxic sample never does.
    /// </para>
    /// </summary>
    public static string Signature(uint activeSeed, BioForm form, uint stabiliserSeed, string? stabiliserItem, uint modifierSeed,
        bool washed = false)
        => activeSeed.ToString("x8", CultureInfo.InvariantCulture) + "/" + (int)form
           + "/" + (stabiliserItem ?? string.Empty) + ":" + stabiliserSeed.ToString("x8", CultureInfo.InvariantCulture)
           + "/" + modifierSeed.ToString("x8", CultureInfo.InvariantCulture)
           + (washed ? "/w" : string.Empty);

    private static int CeilDiv(int a, int b) => (a + b - 1) / b;
}
