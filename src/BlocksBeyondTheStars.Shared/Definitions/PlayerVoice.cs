// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The player figure's voice (#3767 — Justus: "I want to choose in the skin editor what the sounds the figure makes with
/// its mouth are like … man or woman, and young, adult or old"). The figure breathes, strains and grunts with one fixed
/// set of clips; the voice retunes them. It is <b>two sliders, not categories</b> (maintainer decision): a <i>pitch</i>
/// from low to high and an <i>age</i> from young to old, each 0…100 with the middle at 50 — nobody has to sort themselves
/// into "man" or "woman", every position in between works, and the middle is the sound the game always had.
/// <list type="bullet">
/// <item><b>Pitch</b> moves the playback pitch by up to ±<see cref="PitchSemitones"/> semitones.</item>
/// <item><b>Age</b> nudges the pitch a little the other way (young a touch higher, old a touch lower) and picks a timbre
/// that is baked into the clip: young thin and bright, old dull and soft (<see cref="Timbre"/>).</item>
/// </list>
/// The voice travels packed in one int (<see cref="Pack(int, int)"/>); 0 is "not set" — an older client or save — and reads as the
/// middle. Server and client share this class, so a value is clamped the same way on both sides.
/// <para><b>A second recorded voice</b> (#3826 — the same player, two days later: "you still cannot sound like a
/// woman"). A slider only plays the one recording faster or slower: higher, it sounds smaller and younger, never like
/// another person. So the eleven clips exist twice — voice 1, the neutral recording the game always had, and voice 2,
/// recorded in a woman's voice — and the player picks one (<see cref="Kinds"/>, <see cref="ClipFor"/>). The voices are
/// numbered, not named: the sliders work on either, and still nobody has to sort themselves.</para>
/// </summary>
public static class PlayerVoice
{
    /// <summary>A slider's lowest and highest value, its middle and its step.</summary>
    public const int Min = 0, Max = 100, Middle = 50, Step = 10;

    /// <summary>The quick buttons above the pitch slider: Low · Middle · High.</summary>
    public const int Low = 20, High = 80;

    /// <summary>How far the pitch slider moves the voice, in semitones either way.</summary>
    public const float PitchSemitones = 5f;

    /// <summary>How far the age slider nudges the pitch, in semitones (young up, old down).</summary>
    public const float AgeSemitones = 1.5f;

    /// <summary>A slider value brought into 0…100.</summary>
    public static int Clamp(int value) => value < Min ? Min : value > Max ? Max : value;

    /// <summary>A slider value snapped to the nearest <see cref="Step"/>.</summary>
    public static int Snap(int value) => Clamp((int)Math.Round(Clamp(value) / (double)Step) * Step);

    /// <summary>How many recorded voices there are: 0 is the first (the neutral recording), 1 the second (#3826).</summary>
    public const int Kinds = 2;

    /// <summary>The clip-name prefix of the second voice's recordings: <c>v2_hurt_player</c> beside <c>hurt_player</c>.</summary>
    public const string SecondVoicePrefix = "v2_";

    /// <summary>A voice number brought into 0…<see cref="Kinds"/> − 1; anything else is the first voice.</summary>
    public static int ClampKind(int kind) => kind < 0 || kind >= Kinds ? 0 : kind;

    /// <summary>Pitch and age in one int for the wire: each stored + 1 in a byte, so 0 stays "not set".</summary>
    public static int Pack(int pitch, int age) => (Clamp(pitch) + 1) | ((Clamp(age) + 1) << 8);

    /// <summary>Pitch, age and the recorded voice in one int: the voice in the third byte, as it is — so a value packed
    /// before there was a choice (third byte 0) is the first voice.</summary>
    public static int Pack(int pitch, int age, int kind) => Pack(pitch, age) | (ClampKind(kind) << 16);

    /// <summary>The recorded voice of a packed voice (0 for a value that is not set or carries a number nobody knows).</summary>
    public static int KindOf(int packed) => ClampKind((packed >> 16) & 0xFF);

    /// <summary>The pitch and the age of a packed voice; a part that is not set (0) is the middle. Anything out of range
    /// is clamped, so a foreign value can never produce an unplayable pitch.</summary>
    public static (int Pitch, int Age) Unpack(int packed)
    {
        int pitch = packed & 0xFF;
        int age = (packed >> 8) & 0xFF;
        return (pitch == 0 ? Middle : Clamp(pitch - 1), age == 0 ? Middle : Clamp(age - 1));
    }

    /// <summary>A packed voice as the server keeps it: unpacked, clamped, packed again; 0 stays 0 (not set).</summary>
    public static int Sanitize(int packed)
    {
        if (packed == 0)
        {
            return 0;
        }

        var (pitch, age) = Unpack(packed);
        return Pack(pitch, age, KindOf(packed));
    }

    /// <summary>The playback pitch factor of a voice: 1 at the middle, 2^(semitones / 12) elsewhere.</summary>
    public static float Factor(int pitch, int age)
    {
        double semitones = (((Clamp(pitch) - Middle) / (double)Middle) * PitchSemitones)
                           + (((Middle - Clamp(age)) / (double)Middle) * AgeSemitones);
        return (float)Math.Pow(2.0, semitones / 12.0);
    }

    /// <summary>The pitch factor of a packed voice.</summary>
    public static float Factor(int packed)
    {
        var (pitch, age) = Unpack(packed);
        return Factor(pitch, age);
    }

    /// <summary>The timbre an age bakes into a voice clip, and how strongly (0–2): young thin and bright, the middle
    /// dry, old dull and soft.</summary>
    public static (VoiceOp Op, int Amount) Timbre(int age)
    {
        int a = Clamp(age);
        if (a <= 15)
        {
            return (VoiceOp.Thin, 2);
        }

        if (a <= 35)
        {
            return (VoiceOp.Thin, 1);
        }

        if (a >= 85)
        {
            return (VoiceOp.Dull, 2);
        }

        return a >= 65 ? (VoiceOp.Dull, 1) : (VoiceOp.None, 0);
    }

    /// <summary>Whether a clip is one of the figure's own mouth and breath sounds — the ones the voice retunes: the
    /// breaths, the gasp at the surface, the strain, the pain grunt, the fall. Boots, the suit and the body's other
    /// sounds keep their pitch.</summary>
    public static bool IsVoiceClip(string? id)
        => id is not null
           && (IsFirstVoiceClip(id)
               || (id.StartsWith(SecondVoicePrefix, StringComparison.Ordinal) && IsFirstVoiceClip(id.Substring(SecondVoicePrefix.Length))));

    /// <summary>The clip that voice <paramref name="kind"/> plays for the voice sound <paramref name="id"/>: the id itself
    /// for the first voice, the second voice's recording of it otherwise. Anything that is not a voice sound — and a
    /// name that already is a second-voice clip — comes back unchanged.</summary>
    public static string ClipFor(string id, int kind)
        => ClampKind(kind) == 1 && IsFirstVoiceClip(id) ? SecondVoicePrefix + id : id;

    private static bool IsFirstVoiceClip(string id)
        => id.StartsWith("breath_", StringComparison.Ordinal) || id == "surface_gasp" || id == "climb_strain"
           || id == "player_death" || id.StartsWith("hurt_player", StringComparison.Ordinal);
}
