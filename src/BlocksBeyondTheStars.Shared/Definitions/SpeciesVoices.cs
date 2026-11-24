// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>What an animal with recorded calls says, by the mood it is in (#3973). The client picks the mood from the state
/// the server sends; <see cref="SpeciesVoices"/> says which recorded call each mood plays.</summary>
public enum VoiceMood
{
    /// <summary>The idle call, a herd member's answer and the distant call — a dog barks, a cow moos.</summary>
    Calm,

    /// <summary>Startled, hurt, turning on a player, a companion warning its owner — a dog growls, a cat hisses.</summary>
    Alarm,

    /// <summary>Begging, fed, tamed, content beside its owner, asleep — a dog whines, a cat purrs.</summary>
    Friendly,
}

/// <summary>
/// A species' own call pool (#3973, <c>voices</c> on a <c>data/creatures.json</c> record): the recorded calls it makes instead
/// of the generated voice of its body plan, as a small table by <see cref="VoiceMood"/>. Each entry names a CALL
/// (<c>animal_dog_bark</c>); its recorded takes (<see cref="AnimalCalls.Takes"/>, <c>_a</c> and <c>_b</c>) are the clips, so
/// every call alternates between both. <see cref="Calm"/> is required; an empty <see cref="Alarm"/> or <see cref="Friendly"/>
/// plays the calm calls — except asleep, where an animal without a friendly call stays quiet (a cow does not moo in its sleep).
/// A recorded voice keeps its natural pitch and is never processed (no op, no layer, one call per phrase).
/// </summary>
public sealed class SpeciesVoices
{
    /// <summary>The idle calls (at least one).</summary>
    public List<string> Calm { get; set; } = new();

    /// <summary>The alarm calls; empty = the calm calls.</summary>
    public List<string> Alarm { get; set; } = new();

    /// <summary>The friendly calls; empty = the calm calls (and silence asleep).</summary>
    public List<string> Friendly { get; set; } = new();

    /// <summary>True without a calm call — the species then speaks with its body plan's generated voice.</summary>
    public bool IsEmpty => Calm.Count == 0;

    /// <summary>Whether the table names calls of its own for <paramref name="mood"/> (the calm calls always count).</summary>
    public bool Has(VoiceMood mood) => Own(mood).Count > 0;

    /// <summary>The calls of <paramref name="mood"/>: its own, else the calm ones.</summary>
    public IReadOnlyList<string> CallsFor(VoiceMood mood)
    {
        var own = Own(mood);
        return own.Count > 0 ? own : Calm;
    }

    /// <summary>One recorded take of a call of <paramref name="mood"/>, chosen by <paramref name="roll"/> (any int): the call
    /// from the roll's upper bits, the take from its lowest. Null for an empty table.</summary>
    public string? Take(VoiceMood mood, int roll)
    {
        var calls = CallsFor(mood);
        if (calls.Count == 0)
        {
            return null;
        }

        uint r = unchecked((uint)roll);
        string call = calls[(int)((r >> 1) % (uint)calls.Count)];
        return call + AnimalCalls.TakeSuffixes[(int)(r & 1u)];
    }

    /// <summary>Every take of every call in the table — what a client must have bundled.</summary>
    public IEnumerable<string> AllTakes()
    {
        foreach (var list in new[] { Calm, Alarm, Friendly })
        {
            foreach (var call in list)
            {
                foreach (var take in AnimalCalls.Takes(call))
                {
                    yield return take;
                }
            }
        }
    }

    /// <summary>An independent copy.</summary>
    public SpeciesVoices Clone() => new()
    {
        Calm = new List<string>(Calm),
        Alarm = new List<string>(Alarm),
        Friendly = new List<string>(Friendly),
    };

    /// <summary>The wire form (<c>NetCreature.Voices</c>, <c>NetFaunaSpecies.Voices</c>): the three lists comma-joined, the
    /// moods separated by '|' — <c>animal_dog_bark|animal_dog_growl|animal_dog_whine</c>. Empty for an empty table.</summary>
    public string Encode()
        => IsEmpty ? string.Empty : string.Join(",", Calm) + "|" + string.Join(",", Alarm) + "|" + string.Join(",", Friendly);

    /// <summary>Reads <see cref="Encode"/>'s form back; null for an empty or calm-less string (an older server sends none).</summary>
    public static SpeciesVoices? Decode(string? wire)
    {
        if (string.IsNullOrEmpty(wire))
        {
            return null;
        }

        string[] moods = wire!.Split('|');
        var v = new SpeciesVoices
        {
            Calm = Split(moods[0]),
            Alarm = moods.Length > 1 ? Split(moods[1]) : new List<string>(),
            Friendly = moods.Length > 2 ? Split(moods[2]) : new List<string>(),
        };
        return v.IsEmpty ? null : v;
    }

    private List<string> Own(VoiceMood mood) => mood switch
    {
        VoiceMood.Alarm => Alarm,
        VoiceMood.Friendly => Friendly,
        _ => Calm,
    };

    private static List<string> Split(string part)
    {
        var list = new List<string>();
        foreach (var s in part.Split(','))
        {
            if (s.Length > 0)
            {
                list.Add(s);
            }
        }

        return list;
    }
}

/// <summary>
/// The recorded animal calls the client bundles (#3973, <c>client/Assets/Resources/audio/animal_&lt;animal&gt;_&lt;call&gt;_&lt;a|b&gt;.mp3</c>):
/// two takes of every call. A species' <see cref="SpeciesVoices"/> may only name these — content load reports any other
/// name, and a test keeps this list and the bundled files in step.
/// </summary>
public static class AnimalCalls
{
    /// <summary>The take suffixes every call is recorded in.</summary>
    public static readonly IReadOnlyList<string> TakeSuffixes = new[] { "_a", "_b" };

    /// <summary>Every recorded call, without its take suffix.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "animal_dog_bark", "animal_dog_growl", "animal_dog_whine",
        "animal_cat_meow", "animal_cat_purr", "animal_cat_hiss",
        "animal_rabbit_squeak", "animal_rabbit_thump",
        "animal_cow_moo",
        "animal_sheep_baa",
        "animal_chicken_cluck", "animal_chicken_crow",
        "animal_monkey_call", "animal_monkey_screech",
        "animal_parrot_squawk", "animal_parrot_whistle",
        "animal_elephant_trumpet", "animal_elephant_rumble",
        "animal_eagle_scream",
        "animal_dolphin_click", "animal_dolphin_whistle",
        "animal_whale_song",
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    /// <summary>True for a call of <see cref="All"/>.</summary>
    public static bool IsKnown(string? call) => call != null && Known.Contains(call);

    /// <summary>The clip ids of a call's takes.</summary>
    public static IEnumerable<string> Takes(string call)
    {
        foreach (var suffix in TakeSuffixes)
        {
            yield return call + suffix;
        }
    }

    /// <summary>The problems of a voice table (no calm call, an unknown call), each prefixed with <paramref name="context"/>.
    /// A null table is fine — the species keeps its body plan's voice.</summary>
    public static IEnumerable<string> Problems(string context, SpeciesVoices? voices)
    {
        if (voices is null)
        {
            yield break;
        }

        if (voices.IsEmpty)
        {
            yield return $"{context} voices name no calm call.";
        }

        foreach (var list in new[] { voices.Calm, voices.Alarm, voices.Friendly })
        {
            foreach (var call in list)
            {
                if (!IsKnown(call))
                {
                    yield return $"{context} voices name unknown call '{call}' (recorded calls: {nameof(AnimalCalls)}.{nameof(All)}).";
                }
            }
        }
    }
}
