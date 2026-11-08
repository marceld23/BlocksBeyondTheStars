// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>
/// The creatures of Soaniri (#3359) — what the server's spawner, its combat and the client's builders share about the Soundi,
/// the Soundrak, the Qualis and the Soundelings. All four are authored records in <c>data/creatures.json</c> (keys
/// <see cref="SoundersIds.Soundi"/> …), so they live only on the planet types that list them in <c>authoredCreatures</c>; each
/// wears a body plan of its own (<see cref="CreatureBodyPlan.Soundi"/> …) and speaks with its recorded voice
/// (<see cref="CreatureVoices.SungPool"/>).
/// </summary>
public static class SoundersFauna
{
    /// <summary>The roster id of an authored species (<c>"au_" + key</c>, the generator's convention).</summary>
    public static string SpeciesIdOf(string key) => "au_" + key;

    /// <summary>Whether <paramref name="plan"/> is one of the Sounders pack's body plans (the four creatures and the Sounder).</summary>
    public static bool IsSoundersPlan(CreatureBodyPlan plan)
        => plan is CreatureBodyPlan.Sounder or CreatureBodyPlan.Soundi or CreatureBodyPlan.Soundrak
            or CreatureBodyPlan.Soundeling or CreatureBodyPlan.Quali;

    /// <summary>The Soundi — the cat-like singer.</summary>
    public static bool IsSoundi(CreatureSpecies? sp) => sp?.BodyPlan == CreatureBodyPlan.Soundi;

    /// <summary>The Soundrak — the ten-metre territorial giant.</summary>
    public static bool IsSoundrak(CreatureSpecies? sp) => sp?.BodyPlan == CreatureBodyPlan.Soundrak;

    /// <summary>A Quali — the sky jellyfish of the other reality.</summary>
    public static bool IsQuali(CreatureSpecies? sp) => sp?.BodyPlan == CreatureBodyPlan.Quali;

    /// <summary>A Soundeling — the light-loving fluff ball.</summary>
    public static bool IsSoundeling(CreatureSpecies? sp) => sp?.BodyPlan == CreatureBodyPlan.Soundeling;

    /// <summary>How many wild animals of the species may be about at once — the spawner skips it at this count: one Soundrak
    /// (it guards its patch alone), a few Qualis (a spectacle, not a swarm); no limit of its own for everything else.</summary>
    public static int MaxWildAtOnce(CreatureSpecies sp) => sp.BodyPlan switch
    {
        CreatureBodyPlan.Soundrak => SoundrakRules.MaxWildAtOnce,
        CreatureBodyPlan.Quali => QualiRules.MaxWildAtOnce,
        _ => int.MaxValue,
    };

    /// <summary>A player can never hit, push or hurt it: the Qualis are a spectacle from another reality (#3359).</summary>
    public static bool Untouchable(CreatureSpecies? sp) => IsQuali(sp);

    /// <summary>The per-animal size spread (the spawner's 0.7–1.3 bell): the two giants vary only a third as much, so a Soundrak
    /// stays "about ten metres" and a Quali about its drawn size.</summary>
    public static float SizeScaleFor(CreatureSpecies sp, float rolled)
        => sp.BodyPlan is CreatureBodyPlan.Soundrak or CreatureBodyPlan.Quali ? 1f + ((rolled - 1f) / 3f) : rolled;

    /// <summary>A sound id of a numbered family (<c>prefix + 1..count</c>) for <paramref name="roll"/>.</summary>
    public static string Variant(string prefix, int count, int roll)
        => prefix + (1 + (int)((uint)roll % (uint)Math.Max(1, count))).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The Soundi (#3359) — exactly the Soundi of the animated series: cat-like and fluffy, light-blue fur with mint-green patches on
/// the muzzle, chest, paws and ear insides, a big round head, <b>huge pointed ears</b> taller than the head, <b>no eyes</b>, a
/// wide gentle smile, a bushy tail and four short legs; knee-high. Peaceful and curious, it lives in packs of three to five and
/// sings instead of meowing. The proportions are in the standard plan's unit (<c>u = ½·clamp(size, 0.4, 3)</c>): the client
/// builds the body from them and the server's footprint reads them.
/// </summary>
public static class SoundiRules
{
    /// <summary>The light-blue fur (#8FCBF2).</summary>
    public const int Fur = 0x8FCBF2;

    /// <summary>The mint-green patches (#BFF2CF): muzzle, chest, paws, ear insides.</summary>
    public const int Mint = 0xBFF2CF;

    /// <summary>The round body's length in units.</summary>
    public const float BodyLength = 1.15f;

    /// <summary>The round body's height in units.</summary>
    public const float BodyHeight = 0.9f;

    /// <summary>The short legs' length in units (the hip height).</summary>
    public const float LegLength = 0.5f;

    /// <summary>The big round head's edge in units — bigger than the body is tall.</summary>
    public const float HeadEdge = 1.2f;

    /// <summary>The huge ears' height in units — taller than the head.</summary>
    public const float EarHeight = 1.5f;

    /// <summary>The bushy tail's length in units.</summary>
    public const float TailLength = 1.3f;

    /// <summary>The smallest pack.</summary>
    public const int PackMin = 3;

    /// <summary>The largest pack.</summary>
    public const int PackMax = 5;

    /// <summary>The builders' unit for <paramref name="size"/>.</summary>
    public static float Unit(float size) => 0.5f * Math.Min(3f, Math.Max(0.4f, size));

    /// <summary>The ground radius: half the body and most of the big head in front of it.</summary>
    public static float FootprintRadius(float size) => ((BodyLength * 0.5f) + (HeadEdge * 0.8f)) * Unit(size);

    /// <summary>The standing height in blocks to the ear tips.</summary>
    public static float HeightFor(float size) => (LegLength + (BodyHeight * 0.6f) + HeadEdge + (EarHeight * 0.75f)) * Unit(size);
}

/// <summary>
/// The Soundrak (#3359): about ten metres tall, smooth leathery light-blue skin with a mint belly (#3423; no fur, nothing leafy
/// or crumbly), two very short toeless legs, narrow at the bottom with a thin middle and clearly wider at the top, an oval
/// bud-like mouth pointing up at the top and tentacles on its back. <b>Hostile from the start</b> (#3424, Justus: "evil from the
/// start"): a player who comes into its patch hears a short, loud scream (loud, never shrill); one it notices within
/// <see cref="NoticeRange"/> hears the scream as a warning and, still there <see cref="WarningSeconds"/> later, is attacked
/// (<see cref="AlertFor"/>). The calm disc and the Resonance Blaster calm it (<c>GameServer.CalmCreature</c>). One about at a
/// time.
/// </summary>
public static class SoundrakRules
{
    /// <summary>#3423: the light-blue skin (#7EC6EE) — a lighter blue body that stands out against the light-green moss.</summary>
    public const int Skin = 0x7EC6EE;

    /// <summary>#3423: the light-mint belly (#B2ECCF).</summary>
    public const int Belly = 0xB2ECCF;

    /// <summary>The height in blocks per unit of size — the general render-height rule (<c>Size × 1.8</c>), so the server's
    /// large-body checks measure the body that is drawn.</summary>
    public const float HeightPerSize = 1.8f;

    /// <summary>The crown's width as a share of the height (the body widens to it from a thin waist).</summary>
    public const float CrownWidthShare = 0.44f;

    /// <summary>The waist's width as a share of the height.</summary>
    public const float WaistWidthShare = 0.12f;

    /// <summary>The very short legs' share of the height.</summary>
    public const float LegShare = 0.07f;

    /// <summary>A player this close hears its territorial scream.</summary>
    public const float TerritoryRange = 18f;

    /// <summary>#3424: a player this close is noticed — it screams a warning and attacks whoever is still this close
    /// <see cref="WarningSeconds"/> later. Inside the range a Soundrak hunts from (8 + size − 2 ≈ 11.6 blocks at its size
    /// 5.6), so the charge reaches the player it noticed.</summary>
    public const float NoticeRange = 11f;

    /// <summary>#3424: the warning — seconds from the scream to the attack, time enough to back away.</summary>
    public const double WarningSeconds = 2.5;

    /// <summary>#3424: how long a scream counts as the warning. An attack needs a scream at most this long ago; after a longer
    /// quiet it screams again first — shorter than the territory screams' gap, so every new attack has a fresh warning.</summary>
    public const double WarningMemorySeconds = 8.0;

    /// <summary>The scream's shortest gap, in seconds.</summary>
    public const double ScreamCooldownMin = 9.0;

    /// <summary>The scream's longest gap, in seconds.</summary>
    public const double ScreamCooldownMax = 15.0;

    /// <summary>How long a calm (the calm disc's soothing, the Resonance Blaster's pulse) lasts by default, in seconds.</summary>
    public const double CalmSeconds = 90.0;

    /// <summary>One Soundrak about at a time.</summary>
    public const int MaxWildAtOnce = 1;

    /// <summary>The scream's sound family: <c>soundrak_scream_1..3</c>.</summary>
    public const string ScreamPrefix = "soundrak_scream_";

    /// <summary>How many screams the family holds.</summary>
    public const int ScreamVariants = 3;

    /// <summary>The height in blocks at <paramref name="size"/>.</summary>
    public static float HeightFor(float size) => Math.Max(0.4f, size) * HeightPerSize;

    /// <summary>The ground radius — half the crown, the widest part.</summary>
    public static float FootprintRadius(float size) => HeightFor(size) * CrownWidthShare * 0.5f;

    /// <summary>The scream for <paramref name="roll"/>.</summary>
    public static string ScreamSound(int roll) => SoundersFauna.Variant(ScreamPrefix, ScreamVariants, roll);

    /// <summary>The gap before the next scream for a roll in 0..1.</summary>
    public static double ScreamGap(double roll01)
        => ScreamCooldownMin + (Math.Min(1.0, Math.Max(0.0, roll01)) * (ScreamCooldownMax - ScreamCooldownMin));

    /// <summary>#3424: what a Soundrak does about a player it noticed (within <see cref="NoticeRange"/>),
    /// <paramref name="sinceScream"/> seconds after its last scream (infinity when it never screamed): scream the warning now
    /// (none yet, or longer ago than <see cref="WarningMemorySeconds"/>), wait (the warning is younger than
    /// <see cref="WarningSeconds"/>) or attack.</summary>
    public static SoundrakAlert AlertFor(double sinceScream)
    {
        if (double.IsNaN(sinceScream) || sinceScream < 0.0 || sinceScream > WarningMemorySeconds)
        {
            return SoundrakAlert.Warn;
        }

        return sinceScream < WarningSeconds ? SoundrakAlert.Wait : SoundrakAlert.Attack;
    }
}

/// <summary>#3424: a Soundrak's answer to a player it noticed (<see cref="SoundrakRules.AlertFor"/>).</summary>
public enum SoundrakAlert
{
    /// <summary>Scream the warning now.</summary>
    Warn,

    /// <summary>The warning is fresh: give the player time to back away.</summary>
    Wait,

    /// <summary>Warned and still close: attack.</summary>
    Attack,
}

/// <summary>
/// The Qualis (#3359; one Quali, many Qualis): giant translucent glowing jellyfish from the other reality — the medusa body at
/// giant scale, drifting high over Soaniri. A spectacle: never hostile and never attackable
/// (<see cref="SoundersFauna.Untouchable"/>), a few in the sky at a time, singing seldom.
/// </summary>
public static class QualiRules
{
    /// <summary>The largest drawn size (the medusa plan stops at 3).</summary>
    public const float MaxSize = 12f;

    /// <summary>A few Qualis in the sky at a time.</summary>
    public const int MaxWildAtOnce = 3;

    /// <summary>The bell's radius in blocks at <paramref name="size"/> (the medusa's 1.5 units).</summary>
    public static float BellRadius(float size) => 1.5f * 0.5f * Math.Min(MaxSize, Math.Max(0.4f, size));
}
