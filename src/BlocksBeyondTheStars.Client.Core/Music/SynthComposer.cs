// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Music
{
    /// <summary>The four moods of the Synth music style (unchanged from the original code-synth pads).</summary>
    public enum SynthMood
    {
        Menu,
        Planet,
        Space,
        Combat,
    }

    /// <summary>
    /// One generated Synth piece: the musical decisions (<see cref="SynthComposer.Compose"/>) that the
    /// sample renderer (<see cref="SynthComposer.Render"/>) turns into audio. Plain data so tests can
    /// inspect what was composed without rendering a minute of audio.
    /// </summary>
    public sealed class SynthScore
    {
        public SynthMood Mood { get; internal set; }
        public int Seed { get; internal set; }
        public string Flavor { get; internal set; } = string.Empty;
        public int SampleRate { get; internal set; }
        public string ModeName { get; internal set; } = string.Empty;
        public float RootHz { get; internal set; }
        public float Tempo { get; internal set; }
        /// <summary>Length of one chord in seconds (a whole number of beats).</summary>
        public float ChordSeconds { get; internal set; }
        public int ChordSamples { get; internal set; }
        /// <summary>Voiced pad frequencies per chord (2–4 tones).</summary>
        public IReadOnlyList<float[]> Chords { get; internal set; } = Array.Empty<float[]>();
        /// <summary>Low drone frequency per chord.</summary>
        public IReadOnlyList<float> DroneHz { get; internal set; } = Array.Empty<float>();
        /// <summary>Eighth-note steps per chord.</summary>
        public int StepsPerChord { get; internal set; }
        /// <summary>Two arpeggio patterns (even / odd chords); per step the chord-tone index, −1 = rest.</summary>
        public int[][] ArpPatterns { get; internal set; } = Array.Empty<int[]>();
        public float ArpLevel { get; internal set; }
        public float PadLevel { get; internal set; }
        public float DroneLevel { get; internal set; }
        /// <summary>0..1 — how much second partial the pad voices carry (0 = pure sines).</summary>
        public float Brightness { get; internal set; }
        /// <summary>Ratio offset of the detuned second pad voice (0 = none).</summary>
        public float Detune { get; internal set; }
        /// <summary>Combat: slow amplitude throb on the whole mix.</summary>
        public bool Pulse { get; internal set; }
        public float PulseHz { get; internal set; }

        public int TotalSamples => ChordSamples * Chords.Count;
        public float Seconds => SampleRate > 0 ? TotalSamples / (float)SampleRate : 0f;
    }

    /// <summary>
    /// The generative engine behind the <b>Synth</b> music style (#1176). The original style was four fixed
    /// 10–24 s loops with hard-coded chord tables — the most repetitive thing in the game, and the fallback
    /// every Tracks-mode failure lands on. This composes a fresh 60–120 s piece per seed from a small
    /// per-mood palette (mode, tempo, chord progression, arpeggio pattern, pad timbre, drone) and renders it
    /// in pure code: no assets, no download, also in the browser.
    ///
    /// Biome identity (decided 2026-08-22): the <em>root and mode</em> of a planet piece come from the biome
    /// key (every ice planet shares one flavour); the seed only varies progression, pattern and timbre. Seams
    /// are click-free — every chord (and every arpeggio note) sits under a half-sine envelope that reaches
    /// zero at its boundaries, and oscillator phases run on absolute time, so the loop point is silent.
    ///
    /// Pure (no UnityEngine): Client.Core hosts it and the tests inspect scores and rendered samples; the
    /// Unity director renders in chunks across frames and wraps the samples in an <c>AudioClip</c>.
    /// </summary>
    public static class SynthComposer
    {
        /// <summary>Generated pads carry nothing above a few kHz; half the usual rate halves render cost and RAM.</summary>
        public const int DefaultSampleRate = 22050;

        private static readonly int[] Ionian = { 0, 2, 4, 5, 7, 9, 11 };
        private static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
        private static readonly int[] Phrygian = { 0, 1, 3, 5, 7, 8, 10 };
        private static readonly int[] Lydian = { 0, 2, 4, 6, 7, 9, 11 };
        private static readonly int[] Mixolydian = { 0, 2, 4, 5, 7, 9, 10 };
        private static readonly int[] Aeolian = { 0, 2, 3, 5, 7, 8, 10 };

        private static readonly (string Name, int[] Steps)[] Modes =
        {
            ("ionian", Ionian), ("dorian", Dorian), ("phrygian", Phrygian),
            ("lydian", Lydian), ("mixolydian", Mixolydian), ("aeolian", Aeolian),
        };

        private const float C3 = 130.8128f; // C3 in Hz; roots are semitone offsets from here

        // Chord progressions as 0-based scale degrees; two are concatenated per piece.
        private static readonly int[][] MenuProgressions =
        {
            new[] { 0, 3, 4, 5 }, new[] { 0, 5, 3, 4 }, new[] { 0, 2, 3, 0 }, new[] { 5, 3, 0, 4 }, new[] { 0, 3, 0, 4 },
        };

        private static readonly int[][] PlanetProgressions =
        {
            new[] { 0, 3, 4, 5 }, new[] { 0, 5, 3, 4 }, new[] { 0, 4, 5, 3 }, new[] { 1, 4, 0, 0 },
            new[] { 0, 2, 3, 0 }, new[] { 5, 3, 0, 4 }, new[] { 0, 3, 0, 4 }, new[] { 3, 0, 4, 0 },
        };

        private static readonly int[][] SpaceProgressions =
        {
            new[] { 0, 5, 3, 0 }, new[] { 0, 4, 0, 5 }, new[] { 5, 0, 5, 3 }, new[] { 0, 0, 3, 0 },
        };

        private static readonly int[][] CombatProgressions =
        {
            new[] { 0, 5, 0, 3 }, new[] { 0, 1, 0, 4 }, new[] { 5, 3, 0, 0 }, new[] { 0, 3, 1, 0 },
        };

        /// <summary>Composes a piece. <paramref name="flavor"/> is a <see cref="MusicLibrary"/> context key
        /// (planet biome) that fixes root + mode for <see cref="SynthMood.Planet"/>; ignored otherwise.</summary>
        public static SynthScore Compose(SynthMood mood, int seed, string? flavor = null, int sampleRate = DefaultSampleRate)
        {
            if (sampleRate < 8000)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "sample rate must be at least 8 kHz");
            }

            var rng = new Random(seed);
            var score = new SynthScore { Mood = mood, Seed = seed, Flavor = flavor ?? string.Empty, SampleRate = sampleRate };

            // Root + mode: fixed per biome flavour for planets, a small per-mood choice otherwise.
            int rootSemis;
            int[] mode;
            string modeName;
            int[][] progressions;
            float tempo;
            int beatsPerChord;
            float octave = 1f;
            float restChance;
            switch (mood)
            {
                case SynthMood.Menu:
                    rootSemis = 9; // A
                    (modeName, mode) = Pick(rng, ("ionian", Ionian), ("lydian", Lydian), ("mixolydian", Mixolydian));
                    progressions = MenuProgressions;
                    tempo = 60f + rng.Next(11);
                    beatsPerChord = 8;
                    restChance = 0.4f;
                    score.PadLevel = 0.34f;
                    score.ArpLevel = 0.06f;
                    score.DroneLevel = 0.09f;
                    break;
                case SynthMood.Space:
                    rootSemis = 9; // A, an octave down
                    (modeName, mode) = Pick(rng, ("aeolian", Aeolian), ("dorian", Dorian));
                    progressions = SpaceProgressions;
                    tempo = 44f + rng.Next(13);
                    beatsPerChord = 8;
                    octave = 0.5f;
                    restChance = 0.75f;
                    score.PadLevel = 0.30f;
                    score.ArpLevel = 0.035f;
                    score.DroneLevel = 0.11f;
                    break;
                case SynthMood.Combat:
                    rootSemis = 4; // E
                    (modeName, mode) = Pick(rng, ("aeolian", Aeolian), ("phrygian", Phrygian));
                    progressions = CombatProgressions;
                    tempo = 84f + rng.Next(17);
                    beatsPerChord = 8; // two bars per chord → ~40 s pieces (a combat window is ~14 s; re-rolls stay rare)
                    restChance = 0f;
                    score.PadLevel = 0.32f;
                    score.ArpLevel = 0.09f;
                    score.DroneLevel = 0.10f;
                    score.Pulse = true;
                    score.PulseHz = 2f;
                    break;
                default:
                    (rootSemis, modeName, mode, octave) = PlanetFlavor(flavor);
                    progressions = PlanetProgressions;
                    tempo = 56f + rng.Next(17);
                    beatsPerChord = 8;
                    restChance = 0.45f;
                    score.PadLevel = 0.34f;
                    score.ArpLevel = 0.07f;
                    score.DroneLevel = 0.10f;
                    break;
            }

            score.ModeName = modeName;
            score.RootHz = C3 * (float)Math.Pow(2.0, rootSemis / 12.0) * octave;
            score.Tempo = tempo;
            score.ChordSeconds = beatsPerChord * 60f / tempo;
            score.ChordSamples = (int)Math.Ceiling(sampleRate * score.ChordSeconds);
            score.StepsPerChord = beatsPerChord * 2; // eighth notes
            score.Brightness = mood == SynthMood.Space ? 0.15f + (float)rng.NextDouble() * 0.2f : 0.3f + (float)rng.NextDouble() * 0.4f;
            score.Detune = rng.NextDouble() < 0.6 ? 0.002f + (float)rng.NextDouble() * 0.003f : 0f;

            // Progression: two distinct phrases back to back (8 chords ≈ 60–110 s).
            int first = rng.Next(progressions.Length);
            int second = rng.Next(progressions.Length - 1);
            if (second >= first)
            {
                second++;
            }

            var degrees = new List<int>(progressions[first]);
            degrees.AddRange(progressions[second]);

            bool dyads = mood == SynthMood.Space;
            bool sevenths = (mood == SynthMood.Menu || mood == SynthMood.Planet) && rng.NextDouble() < 0.35;
            var chords = new List<float[]>(degrees.Count);
            var drones = new List<float>(degrees.Count);
            foreach (int degree in degrees)
            {
                var tones = new List<float> { Degree(score.RootHz, mode, degree) };
                if (dyads)
                {
                    tones.Add(Degree(score.RootHz, mode, degree + 4));
                }
                else
                {
                    bool spread = rng.NextDouble() < 0.4; // third up an octave: wider, airier voicing
                    tones.Add(Degree(score.RootHz, mode, degree + (spread ? 9 : 2)));
                    tones.Add(Degree(score.RootHz, mode, degree + 4));
                    if (sevenths)
                    {
                        tones.Add(Degree(score.RootHz, mode, degree + 6));
                    }
                }

                chords.Add(tones.ToArray());
                drones.Add(Degree(score.RootHz, mode, degree) * 0.5f);
            }

            score.Chords = chords;
            score.DroneHz = drones;

            // Arpeggio: two patterns (even / odd chords). Combat = a steady root pulse alternating octaves.
            int maxTone = dyads ? 2 : (sevenths ? 4 : 3);
            score.ArpPatterns = new[] { Pattern(rng, score.StepsPerChord, maxTone, restChance, mood), Pattern(rng, score.StepsPerChord, maxTone, restChance, mood) };
            return score;
        }

        /// <summary>Renders samples [<paramref name="startSample"/>, + <paramref name="count"/>) of the piece into
        /// <paramref name="buffer"/> (from index 0). Chunk-safe: any split yields the same samples as one call.</summary>
        public static void Render(SynthScore score, float[] buffer, int startSample, int count)
        {
            if (score == null)
            {
                throw new ArgumentNullException(nameof(score));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (startSample < 0 || count < 0 || count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            int total = score.TotalSamples;
            int chordSamples = score.ChordSamples;
            int chordCount = score.Chords.Count;
            float rate = score.SampleRate;
            float stepSeconds = score.ChordSeconds / score.StepsPerChord;
            const float TwoPi = 2f * (float)Math.PI;

            for (int n = 0; n < count; n++)
            {
                int i = startSample + n;
                if (i >= total)
                {
                    buffer[n] = 0f;
                    continue;
                }

                float t = i / rate;                                   // absolute time → continuous phase
                int chord = (i / chordSamples) % chordCount;
                float local = (i % chordSamples) / rate;
                float env = (float)Math.Sin(Math.PI * local / score.ChordSeconds); // 0 at the seams

                float[] tones = score.Chords[chord];
                float pad = 0f;
                foreach (float f in tones)
                {
                    float s = (float)Math.Sin(TwoPi * f * t);
                    if (score.Brightness > 0f)
                    {
                        s += score.Brightness * 0.4f * (float)Math.Sin(TwoPi * 2f * f * t);
                    }

                    if (score.Detune > 0f)
                    {
                        s += 0.5f * (float)Math.Sin(TwoPi * f * (1f + score.Detune) * t);
                    }

                    pad += s;
                }

                pad *= score.PadLevel / (tones.Length * (1f + score.Brightness * 0.4f + (score.Detune > 0f ? 0.5f : 0f)));

                float drone = (float)Math.Sin(TwoPi * score.DroneHz[chord] * t)
                              * score.DroneLevel * (0.8f + 0.2f * (float)Math.Sin(TwoPi * 0.05f * t));

                float arp = 0f;
                int step = (int)(local / stepSeconds);
                if (step >= score.StepsPerChord)
                {
                    step = score.StepsPerChord - 1;
                }

                int[] pattern = score.ArpPatterns[chord % score.ArpPatterns.Length];
                int tone = pattern[step];
                if (tone >= 0)
                {
                    int index = tone % tones.Length;
                    float noteT = local - step * stepSeconds;
                    float noteEnv = (float)Math.Sin(Math.PI * noteT / stepSeconds);
                    if (noteEnv < 0f)
                    {
                        noteEnv = 0f;
                    }

                    float f = tones[index] * (score.Mood == SynthMood.Combat && (step & 1) == 1 ? 4f : 2f); // an octave (or two) above the pad
                    arp = (float)Math.Sin(TwoPi * f * t) * score.ArpLevel * noteEnv;
                }

                float mix = pad + drone + arp;
                if (score.Pulse)
                {
                    mix *= 0.72f + 0.28f * (float)Math.Sin(TwoPi * score.PulseHz * t);
                }

                buffer[n] = mix * env;
            }
        }

        /// <summary>Renders the whole piece (tests / small pieces). Not normalized — see <see cref="Normalize"/>.</summary>
        public static float[] RenderAll(SynthScore score)
        {
            if (score == null)
            {
                throw new ArgumentNullException(nameof(score));
            }

            var data = new float[score.TotalSamples];
            Render(score, data, 0, data.Length);
            return data;
        }

        /// <summary>Target RMS amplitude of a finished piece (≈ −22 dBFS, about −20 LUFS for these pads): the
        /// track library sits around −13 LUFS, so a synth piece is deliberately ~7 dB quieter — pure tones read
        /// as louder than a produced mix at equal level, and the Synth style must never jump out.</summary>
        public const float TargetRms = 0.08f;

        /// <summary>Hard ceiling for any sample after normalization (−4.4 dBFS): leaves headroom for the
        /// cross-fade overlap of two pieces and the game's master bus.</summary>
        public const float PeakCap = 0.6f;

        /// <summary>Scales a rendered piece in place so its RMS hits <see cref="TargetRms"/> — but never lets a
        /// sample exceed <see cref="PeakCap"/> (the peak limit wins; quiet pieces only come up, loud ones go
        /// down). Returns the gain that was applied. A silent buffer is left alone.</summary>
        public static float Normalize(float[] data, float targetRms = TargetRms, float peakCap = PeakCap)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            double sumSquares = 0.0;
            float peak = 0f;
            foreach (float v in data)
            {
                sumSquares += (double)v * v;
                float a = Math.Abs(v);
                if (a > peak)
                {
                    peak = a;
                }
            }

            if (data.Length == 0 || peak <= 1e-6f)
            {
                return 1f;
            }

            float rms = (float)Math.Sqrt(sumSquares / data.Length);
            float gain = Math.Min(targetRms / rms, peakCap / peak);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= gain;
            }

            return gain;
        }

        private static (int RootSemis, string ModeName, int[] Mode, float Octave) PlanetFlavor(string? flavor) => flavor switch
        {
            MusicLibrary.PlanetIce => (2, "dorian", Dorian, 1f),        // D dorian — cool, open
            MusicLibrary.PlanetDesert => (2, "mixolydian", Mixolydian, 1f), // D mixolydian — warm, dusty
            MusicLibrary.PlanetLava => (4, "aeolian", Aeolian, 1f),    // E aeolian — dark heat
            MusicLibrary.PlanetToxic => (6, "dorian", Dorian, 1f),     // F# dorian — uneasy, alien
            MusicLibrary.PlanetOcean => (7, "ionian", Ionian, 1f),     // G ionian — flowing, soft
            MusicLibrary.PlanetVerdant => (0, "lydian", Lydian, 1f),   // C lydian — alive, curious
            MusicLibrary.PlanetCrystal => (11, "lydian", Lydian, 1f),  // B lydian — sparkling
            MusicLibrary.PlanetCave => (2, "aeolian", Aeolian, 0.5f),  // D aeolian, an octave down — deep
            MusicLibrary.PlanetDeep => (7, "aeolian", Aeolian, 0.5f),  // G aeolian low — submerged
            _ => (0, "ionian", Ionian, 1f),                             // C ionian — the generic wonder pad
        };

        private static (string Name, int[] Steps) Pick(Random rng, params (string Name, int[] Steps)[] options)
            => options[rng.Next(options.Length)];

        /// <summary>Frequency of scale degree <paramref name="degree"/> (0-based, may exceed 6 → next octave).</summary>
        private static float Degree(float rootHz, int[] mode, int degree)
        {
            int octave = degree / mode.Length;
            int step = degree % mode.Length;
            int semis = mode[step] + 12 * octave;
            return rootHz * (float)Math.Pow(2.0, semis / 12.0);
        }

        private static int[] Pattern(Random rng, int steps, int maxTone, float restChance, SynthMood mood)
        {
            var pattern = new int[steps];
            for (int s = 0; s < steps; s++)
            {
                if (mood == SynthMood.Combat)
                {
                    pattern[s] = 0; // root pulse; the renderer alternates octaves on odd steps
                    continue;
                }

                pattern[s] = rng.NextDouble() < restChance ? -1 : rng.Next(maxTone);
            }

            if (mood != SynthMood.Combat && Array.IndexOf(pattern, -1) == -1 && steps > 2)
            {
                pattern[steps - 1] = -1; // never a wall of notes: at least one breath per chord
            }

            return pattern;
        }
    }
}
