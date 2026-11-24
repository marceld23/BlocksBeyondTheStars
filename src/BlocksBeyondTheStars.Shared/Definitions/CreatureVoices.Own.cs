// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>#3973: the species' own recorded calls (<see cref="SpeciesVoices"/>) as a voice.</summary>
public static partial class CreatureVoices
{
    /// <summary>#3973: a voice of the species' own recorded calls — a calm take as the call, an alarm take as the alarm, one
    /// recording per phrase, never processed (no op, no tail, no layer), a flat contour, the body's calling rate.</summary>
    private static CreatureVoice OwnVoice(int seed, VoiceTraits t, SpeciesVoices own)
    {
        int roll = (int)Mix(seed, "own.call");
        string call = own.Take(VoiceMood.Calm, roll)!;
        string alarm = own.Take(VoiceMood.Alarm, roll)!;
        var (cadMin, cadMax) = PickCadence(seed, t, 1);
        return new CreatureVoice(call, string.Empty, 0, 0, VoiceOp.None, 0, 0, 1, 200, VoiceContour.Flat,
            cadMin, cadMax, Roll(seed, "pitch", 37), alarm, own);
    }

    /// <summary>Whether the caller has at least one take of the table's calm calls (null = the server: always).</summary>
    private static bool HasAnyTake(SpeciesVoices own, System.Func<string, bool>? available)
    {
        if (available == null)
        {
            return true;
        }

        foreach (var call in own.Calm)
        {
            foreach (var take in AnimalCalls.Takes(call))
            {
                if (available(take))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>#3973: whether a voice keeps its recorded pitch — a species' own recorded calls, or a recorded body voice of
    /// Soaniri (<see cref="KeepsNaturalPitch(string)"/>).</summary>
    public static bool KeepsNaturalPitch(CreatureVoice voice, string bodyPlan) => voice.Own != null || KeepsNaturalPitch(bodyPlan);
}
