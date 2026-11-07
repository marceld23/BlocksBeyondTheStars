// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>What one played tone did to a <see cref="MelodyPuzzle"/>.</summary>
public enum MelodyStep
{
    /// <summary>Nothing: the puzzle is solved already, or the tone is not one of the eight.</summary>
    Ignored = 0,

    /// <summary>The right tone; the round goes on.</summary>
    Correct = 1,

    /// <summary>The right tone, and it finished the round — the next round (and its prompt) follows.</summary>
    RoundDone = 2,

    /// <summary>The right tone, and it finished the last round.</summary>
    Solved = 3,

    /// <summary>Not the tone asked for. Never a punishment: the round restarts (Repeat, CallAndResponse) or the input resets
    /// (Compose, Harmony). <see cref="MelodyPuzzle.Mistakes"/> counts it, nothing else does.</summary>
    Wrong = 4,
}

/// <summary>
/// The pure rules of a melody puzzle (#3346): which notes the prompt of each round plays, which tones the player must
/// answer with, and what a played tone does. No clock and no I/O — the server's runtime (<c>GameServerMelodyPuzzles</c>)
/// owns the timing, the sounds and the messages; the melody bar and a site's pillars only feed <see cref="Play"/>.
/// <list type="bullet">
/// <item><b>Repeat</b> — round <c>i</c> of <c>R</c> asks for the first <c>ceil((i+1)·n/R)</c> tones of the melody, so the
/// prompt grows every round and the last round is the whole melody.</item>
/// <item><b>Compose</b> — one round: the whole target, no prompt.</item>
/// <item><b>Harmony</b> — one round: the prompt is the melody, the answer its counter-voice (<see cref="Counter"/>).</item>
/// <item><b>CallAndResponse</b> — one round per call: the prompt is the call, the answer its answer phrase (or the call
/// itself when the answer is empty).</item>
/// </list>
/// Rounds without a single tone to play (a call of rests only) are dropped.
/// </summary>
public sealed class MelodyPuzzle
{
    private readonly List<int[]> _prompts = new();
    private readonly List<int[]> _expected = new();

    public MelodyPuzzle(MelodyPuzzleSpec spec)
    {
        Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        switch (spec.Mode)
        {
            case MelodyPuzzleMode.Repeat:
                BuildRepeat(spec.Target ?? Array.Empty<int>(), spec.Rounds);
                break;
            case MelodyPuzzleMode.Compose:
                AddRound(Array.Empty<int>(), SounderAlphabet.TonesOnly(spec.Target ?? Array.Empty<int>()));
                break;
            case MelodyPuzzleMode.Harmony:
                var melody = spec.Target ?? Array.Empty<int>();
                AddRound(melody, SounderAlphabet.TonesOnly(Counter(melody, spec.HarmonyOffset)));
                break;
            case MelodyPuzzleMode.CallAndResponse:
                BuildCallAndResponse(spec.Calls ?? Array.Empty<int[]>(), spec.Answers ?? Array.Empty<int[]>());
                break;
        }
    }

    public MelodyPuzzleSpec Spec { get; }

    /// <summary>Whether the spec makes a playable puzzle: at least one round with a tone, and every note a tone or a rest.</summary>
    public bool IsValid => _expected.Count > 0;

    /// <summary>The current round, 0-based.</summary>
    public int Round { get; private set; }

    /// <summary>How many rounds the puzzle has.</summary>
    public int RoundCount => _expected.Count;

    /// <summary>The right tones played so far in this round.</summary>
    public int Entered { get; private set; }

    /// <summary>Wrong tones so far — for a gentle "nearly!" at most; nothing is ever taken away for them.</summary>
    public int Mistakes { get; private set; }

    public bool Solved { get; private set; }

    /// <summary>What the server plays to start the current round (tones and rests); empty for Compose.</summary>
    public IReadOnlyList<int> Prompt => IsValid ? _prompts[Math.Min(Round, _prompts.Count - 1)] : Array.Empty<int>();

    /// <summary>The tones the player must play in the current round, in order.</summary>
    public IReadOnlyList<int> Expected => IsValid ? _expected[Math.Min(Round, _expected.Count - 1)] : Array.Empty<int>();

    /// <summary>Whether a round's prompt plays (everything but Compose).</summary>
    public bool HasPrompt => Spec.Mode != MelodyPuzzleMode.Compose;

    /// <summary>The player played <paramref name="tone"/> (0–7).</summary>
    public MelodyStep Play(int tone)
    {
        if (Solved || !IsValid || tone < 0 || tone >= SounderAlphabet.ToneCount)
        {
            return MelodyStep.Ignored;
        }

        var expected = Expected;
        if (tone == expected[Entered])
        {
            Entered++;
            if (Entered < expected.Count)
            {
                return MelodyStep.Correct;
            }

            Entered = 0;
            if (Round + 1 >= RoundCount)
            {
                Solved = true;
                return MelodyStep.Solved;
            }

            Round++;
            return MelodyStep.RoundDone;
        }

        Mistakes++;
        bool restartsRound = Spec.Mode is MelodyPuzzleMode.Repeat or MelodyPuzzleMode.CallAndResponse;

        // Compose / Harmony: the input resets — but a wrong tone that is the first tone of the sequence is the start of a
        // new try (a player who lost the thread simply begins again; no extra "wrong" in between).
        Entered = !restartsRound && tone == expected[0] ? 1 : 0;
        return MelodyStep.Wrong;
    }

    /// <summary>Starts the current round again (a reminder after a long pause, or a replayed prompt).</summary>
    public void RestartRound() => Entered = 0;

    /// <summary>
    /// <paramref name="tone"/> moved by <paramref name="steps"/> scale steps on the eight-tone bar (0 = C … 7 = C'). A result
    /// past the top C' falls an octave (7 steps): G + 4 = D, a fifth above in the same pitch class; one below C rises an
    /// octave. So the counter-voice always lands on one of the bar's eight keys. Rests stay rests.
    /// </summary>
    public static int Transpose(int tone, int steps)
    {
        if (tone == SounderAlphabet.Rest)
        {
            return tone;
        }

        int t = tone + steps;
        while (t >= SounderAlphabet.ToneCount)
        {
            t -= SounderAlphabet.ToneCount - 1;
        }

        while (t < 0)
        {
            t += SounderAlphabet.ToneCount - 1;
        }

        return t;
    }

    /// <summary>The counter-voice of <paramref name="melody"/>: every tone <see cref="Transpose"/>d, rests kept.</summary>
    public static int[] Counter(IReadOnlyList<int> melody, int steps)
    {
        var voice = new int[melody.Count];
        for (int i = 0; i < voice.Length; i++)
        {
            voice[i] = Transpose(melody[i], steps);
        }

        return voice;
    }

    /// <summary>Repeat: how many tones round <paramref name="round"/> (0-based) of <paramref name="rounds"/> asks for, out of
    /// <paramref name="tones"/>.</summary>
    public static int RepeatLength(int round, int rounds, int tones)
        => Math.Min(tones, Math.Max(1, (int)Math.Ceiling((round + 1) * (double)tones / Math.Max(1, rounds))));

    private void BuildRepeat(int[] melody, int rounds)
    {
        int[] tones = SounderAlphabet.TonesOnly(melody);
        int n = tones.Length;
        int r = rounds <= 0 ? n : Math.Min(rounds, n);
        int last = 0;
        for (int i = 0; i < r; i++)
        {
            int length = RepeatLength(i, r, n);
            if (length <= last)
            {
                continue; // two rounds of the same length would ask the same thing twice
            }

            last = length;
            var expected = new int[length];
            Array.Copy(tones, expected, length);
            AddRound(PrefixWithTones(melody, length), expected);
        }
    }

    private void BuildCallAndResponse(int[][] calls, int[][] answers)
    {
        for (int i = 0; i < calls.Length; i++)
        {
            int[] call = calls[i] ?? Array.Empty<int>();
            int[] answer = i < answers.Length && answers[i] is { Length: > 0 } a ? a : call;
            AddRound(call, SounderAlphabet.TonesOnly(answer));
        }
    }

    private void AddRound(int[] prompt, int[] expected)
    {
        if (expected.Length == 0 || !SounderAlphabet.IsValidSequence(prompt) || !SounderAlphabet.IsValidSequence(expected))
        {
            return;
        }

        _prompts.Add((int[])prompt.Clone());
        _expected.Add(expected);
    }

    /// <summary>The notes of <paramref name="melody"/> up to and including its <paramref name="tones"/>-th tone (rests between
    /// kept, trailing rests dropped).</summary>
    private static int[] PrefixWithTones(int[] melody, int tones)
    {
        int seen = 0;
        for (int i = 0; i < melody.Length; i++)
        {
            if (melody[i] != SounderAlphabet.Rest && ++seen == tones)
            {
                var prefix = new int[i + 1];
                Array.Copy(melody, prefix, i + 1);
                return prefix;
            }
        }

        return (int[])melody.Clone();
    }
}
